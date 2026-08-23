using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Xunit;

namespace Entriqa.Tests;

public class FormTokenServiceTests
{
    [Fact]
    public void Roundtrip_after_min_age_is_valid()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestData.Time.GetUtcNow());
        var svc = TestData.Tokens(time);
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        time.Advance(TimeSpan.FromSeconds(5));

        var payload = svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.FromSeconds(3), TimeSpan.FromHours(2));

        Assert.Equal("kontakt", payload.Subject);
        Assert.Equal(24, payload.Nonce.Length);
    }

    [Fact]
    public void Too_fast_is_rejected()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestData.Time.GetUtcNow());
        var svc = TestData.Tokens(time);
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        time.Advance(TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.FromSeconds(3), TimeSpan.FromHours(2)));
        Assert.Equal(ErrorCodes.TokenTooEarly, ex.ErrorCode);
    }

    [Fact]
    public void Expired_is_rejected()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestData.Time.GetUtcNow());
        var svc = TestData.Tokens(time);
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        time.Advance(TimeSpan.FromHours(3));

        var ex = Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.Zero, TimeSpan.FromHours(2)));
        Assert.Equal(ErrorCodes.TokenExpired, ex.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abc.def")]
    public void Garbage_is_rejected(string token)
    {
        var svc = TestData.Tokens();
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        var svc = TestData.Tokens();
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        var other = svc.Issue(FormTokenService.KindForm, "anderes-formular");
        var forged = other[..other.IndexOf('.')] + token[token.IndexOf('.')..];

        Assert.Throws<SecurityTokenException>(() => svc.Validate(forged, FormTokenService.KindForm, "anderes-formular", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Kind_and_subject_must_match()
    {
        var svc = TestData.Tokens();
        var token = svc.Issue(FormTokenService.KindRun, "kontakt:123");
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt:123", TimeSpan.Zero, TimeSpan.FromHours(2)));
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindRun, "kontakt:999", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }
}
