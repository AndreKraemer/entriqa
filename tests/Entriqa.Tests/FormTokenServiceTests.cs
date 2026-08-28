using Entriqa.Application.Security;
using Entriqa.Domain.Errors;
using Xunit;

namespace Entriqa.Tests;

public class FormTokenServiceTests
{
    [Fact]
    public void GivenTokenOlderThanTheMinimumAge_WhenValidating_ThenThePayloadRoundtrips()
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
    public void GivenTokenYoungerThanTheMinimumAge_WhenValidating_ThenTokenTooEarlyIsReported()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestData.Time.GetUtcNow());
        var svc = TestData.Tokens(time);
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        time.Advance(TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.FromSeconds(3), TimeSpan.FromHours(2)));
        Assert.Equal(ErrorCodes.TokenTooEarly, ex.ErrorCode);
    }

    [Fact]
    public void GivenTokenPastItsLifetime_WhenValidating_ThenTokenExpiredIsReported()
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
    public void GivenMalformedToken_WhenValidating_ThenItIsRejected(string token)
    {
        var svc = TestData.Tokens();
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GivenPayloadSwappedBetweenTwoTokens_WhenValidating_ThenTheSignatureCheckRejectsIt()
    {
        var svc = TestData.Tokens();
        var token = svc.Issue(FormTokenService.KindForm, "kontakt");
        var other = svc.Issue(FormTokenService.KindForm, "anderes-formular");
        var forged = other[..other.IndexOf('.')] + token[token.IndexOf('.')..];

        Assert.Throws<SecurityTokenException>(() => svc.Validate(forged, FormTokenService.KindForm, "anderes-formular", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void GivenTokenIssuedForAnotherKindOrSubject_WhenValidating_ThenItIsRejected()
    {
        var svc = TestData.Tokens();
        var token = svc.Issue(FormTokenService.KindRun, "kontakt:123");
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindForm, "kontakt:123", TimeSpan.Zero, TimeSpan.FromHours(2)));
        Assert.Throws<SecurityTokenException>(() => svc.Validate(token, FormTokenService.KindRun, "kontakt:999", TimeSpan.Zero, TimeSpan.FromHours(2)));
    }
}
