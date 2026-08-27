using System.Globalization;
using System.Security.Cryptography;
using Entriqa.Application.Security;
using Xunit;

namespace Entriqa.Tests;

public class LicenseServiceTests
{
    private static (string Priv, string Pub) NewPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()));
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GivenKeyWithinItsValidity_WhenValidating_ThenPlanAndExpiryAreReturned()
    {
        var (priv, pub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1001", "site", new DateOnly(2027, 8, 23));
        var info = LicenseService.Validate(key, pub, Now);
        Assert.True(info.Valid);
        Assert.Equal("site", info.Plan);
        Assert.Equal(new DateOnly(2027, 8, 23), info.ValidUntil);
    }

    [Theory]
    [InlineData("ar-SA")]   // Umm al-Qura calendar: ParseExact without a culture throws
    [InlineData("th-TH")]   // Buddhist calendar: 2027 parses as 1484, so a valid key reads "expired"
    public void GivenAServerCultureWithANonGregorianCalendar_WhenRoundTrippingAKey_ThenItStaysValid(string culture)
    {
        // Issue() formats the expiry into the SIGNED payload and Validate() parses it back. If only
        // one side pins the culture the signature verifies but the date does not survive, and every
        // licence fails on such a server. CA1305 does not flag ParseExact(string, string), so the
        // analyzer cannot stand in for this test.
        // Run on a thread of its own rather than mutating the ambient culture of a pooled test
        // thread: xUnit runs classes in parallel, and a try/finally still leaves a window in which
        // the process-visible culture is not what a neighbouring test assumes. A dedicated thread
        // has no such window - nothing else ever runs on it.
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var (priv, pub) = NewPair();
                var key = LicenseService.Issue(priv, "L-1001", "site", new DateOnly(2027, 8, 23));
                var info = LicenseService.Validate(key, pub, Now);
                Assert.Equal("valid", info.Status);
                Assert.Equal(new DateOnly(2027, 8, 23), info.ValidUntil);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException($"under {culture}: {failure.Message}");
    }

    [Fact]
    public void GivenKeyPastItsExpiry_WhenValidating_ThenStatusIsExpired()
    {
        var (priv, pub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1", "site", new DateOnly(2026, 1, 1));
        Assert.Equal("expired", LicenseService.Validate(key, pub, Now).Status);
    }

    [Fact]
    public void GivenForeignSignatureOrGarbageOrEmptyKey_WhenValidating_ThenEachIsRejectedWithItsOwnStatus()
    {
        var (priv, _) = NewPair();
        var (_, otherPub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1", "site", new DateOnly(2027, 1, 1));
        Assert.Equal("invalid", LicenseService.Validate(key, otherPub, Now).Status);
        Assert.Equal("invalid", LicenseService.Validate("ENTRIQA-kaputt", otherPub, Now).Status);
        Assert.Equal("missing", LicenseService.Validate("", otherPub, Now).Status);
    }
}
