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
    public void Gueltiger_Schluessel_wird_akzeptiert()
    {
        var (priv, pub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1001", "site", new DateOnly(2027, 8, 23));
        var info = LicenseService.Validate(key, pub, Now);
        Assert.True(info.Valid);
        Assert.Equal("site", info.Plan);
        Assert.Equal(new DateOnly(2027, 8, 23), info.ValidUntil);
    }

    [Fact]
    public void Abgelaufener_Schluessel_wird_abgelehnt()
    {
        var (priv, pub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1", "site", new DateOnly(2026, 1, 1));
        Assert.Equal("expired", LicenseService.Validate(key, pub, Now).Status);
    }

    [Fact]
    public void Fremde_Signatur_und_Muell_werden_abgelehnt()
    {
        var (priv, _) = NewPair();
        var (_, otherPub) = NewPair();
        var key = LicenseService.Issue(priv, "L-1", "site", new DateOnly(2027, 1, 1));
        Assert.Equal("invalid", LicenseService.Validate(key, otherPub, Now).Status);
        Assert.Equal("invalid", LicenseService.Validate("ENTRIQA-kaputt", otherPub, Now).Status);
        Assert.Equal("missing", LicenseService.Validate("", otherPub, Now).Status);
    }
}
