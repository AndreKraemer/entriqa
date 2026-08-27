using System.Text.Json;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The double opt-in mail leaves the system in the participant's language, but the link in it
/// used to point at one fixed page - so whoever filled in the English form still landed on the
/// German confirmation page. A site declares the real URL per language; where it does not, the
/// fallback follows Hugo's default layout (first locale at the root, others under /{locale}/),
/// which at least keeps the language right even if the path reads oddly.
/// </summary>
public class DoiLocalizationTests
{
    private static EntriqaOptions Options(string locales = "de,en") =>
        new() { TokenSecret = "unit-test-secret-with-at-least-32-characters!!", BaseUrl = "https://example.org", SiteName = "Test", Locales = locales };

    [Fact]
    public void GivenTheDefaultLocale_WhenBuildingTheConfirmPagePath_ThenItStaysAtTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor("de"));

    [Fact]
    public void GivenASecondaryLocaleWithoutAConfiguredPath_WhenBuildingTheConfirmPagePath_ThenItFallsBackToThePrefixedDefault() =>
        Assert.Equal("/en/bestaetigen/", Options().ConfirmPagePathFor("en"));

    [Fact]
    public void GivenAPathConfiguredForThatLocale_WhenBuildingTheConfirmPagePath_ThenTheConfiguredPathWins()
    {
        var options = Options();
        options.ConfirmPagePaths["en"] = "/en/confirm/";
        Assert.Equal("/en/confirm/", options.ConfirmPagePathFor("en"));
    }

    [Fact]
    public void GivenAPathConfiguredInADifferentCasing_WhenBuildingTheConfirmPagePath_ThenItStillMatches()
    {
        var options = Options();
        options.ConfirmPagePaths["EN"] = "/en/confirm/";
        Assert.Equal("/en/confirm/", options.ConfirmPagePathFor("en"));
    }

    [Fact]
    public void GivenAPathConfiguredForTheDefaultLocale_WhenBuildingTheConfirmPagePath_ThenItWinsOverTheBareDefault()
    {
        var options = Options();
        options.ConfirmPagePaths["de"] = "/danke-bestaetigen/";
        Assert.Equal("/danke-bestaetigen/", options.ConfirmPagePathFor("de"));
    }

    [Fact]
    public void GivenNoLocaleAtAll_WhenBuildingTheConfirmPagePath_ThenItFallsBackToTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor(null));

    [Fact]
    public void GivenALocaleTheSiteDoesNotHave_WhenBuildingTheConfirmPagePath_ThenItFallsBackToTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor("fr"));

    [Fact]
    public void GivenASecondaryLocale_WhenBuildingTheConfirmedRedirectPath_ThenItFallsBackTheSameWay() =>
        Assert.Equal("/en/bestaetigt/", Options().ConfirmedRedirectPathFor("en"));

    [Fact]
    public void GivenAConfiguredRedirectForThatLocale_WhenBuildingTheConfirmedRedirectPath_ThenTheConfiguredPathWins()
    {
        var options = Options();
        options.ConfirmedRedirectPaths["en"] = "/en/confirmed/";
        Assert.Equal("/en/confirmed/", options.ConfirmedRedirectPathFor("en"));
    }

    [Fact]
    public async Task GivenASubmissionInASecondaryLocale_WhenTheDoiMailIsSent_ThenTheConfirmLinkPointsAtThatLanguage()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var options = Options();
        options.ConfirmPagePaths["en"] = "/en/confirm/";
        var step = new DoiRequestStep(mail, TestData.Tokens());
        var ctx = new StepContext
        {
            Submission = new Submission
            {
                Id = "whitepaper:1", Slug = "whitepaper", Version = 1, CreatedAt = TestData.Time.GetUtcNow(),
                Values = new Dictionary<string, string>(), Email = "a@b.de", Locale = "en",
            },
            Form = TestData.Contact(),
            FormVersion = 1,
            Options = options,
        };

        await step.ExecuteAsync(ctx, JsonDocument.Parse("""{"templateId":3}""").RootElement, default);

        await mail.Received(1).SendAsync(
            "a@b.de", Arg.Any<string?>(), 3,
            Arg.Is<Dictionary<string, object?>>(p => ((string)p["confirmUrl"]!).StartsWith("https://example.org/en/confirm/?t=")),
            null, Arg.Any<CancellationToken>());
    }
}
