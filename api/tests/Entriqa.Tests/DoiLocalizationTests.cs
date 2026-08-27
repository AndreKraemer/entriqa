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
/// German confirmation page. The paths follow Hugo's default URL layout: the first configured
/// locale lives at the root, every other one under /{locale}/.
/// </summary>
public class DoiLocalizationTests
{
    private static EntriqaOptions Options(string locales = "de,en") =>
        new() { TokenSecret = "unit-test-secret-with-at-least-32-characters!!", BaseUrl = "https://example.org", SiteName = "Test", Locales = locales };

    [Fact]
    public void GivenTheDefaultLocale_WhenBuildingTheConfirmPagePath_ThenItStaysAtTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor("de"));

    [Fact]
    public void GivenASecondaryLocale_WhenBuildingTheConfirmPagePath_ThenItIsPrefixedWithThatLocale() =>
        Assert.Equal("/en/bestaetigen/", Options().ConfirmPagePathFor("en"));

    [Fact]
    public void GivenNoLocaleAtAll_WhenBuildingTheConfirmPagePath_ThenItFallsBackToTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor(null));

    [Fact]
    public void GivenALocaleTheSiteDoesNotHave_WhenBuildingTheConfirmPagePath_ThenItFallsBackToTheRoot() =>
        Assert.Equal("/bestaetigen/", Options().ConfirmPagePathFor("fr"));

    [Fact]
    public void GivenASecondaryLocale_WhenBuildingTheConfirmedRedirectPath_ThenItIsPrefixedTheSameWay() =>
        Assert.Equal("/en/bestaetigt/", Options().ConfirmedRedirectPathFor("en"));

    [Fact]
    public async Task GivenASubmissionInASecondaryLocale_WhenTheDoiMailIsSent_ThenTheConfirmLinkPointsAtThatLanguage()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var options = Options();
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
            Arg.Is<Dictionary<string, object?>>(p => ((string)p["confirmUrl"]!).StartsWith("https://example.org/en/bestaetigen/?t=")),
            null, Arg.Any<CancellationToken>());
    }
}
