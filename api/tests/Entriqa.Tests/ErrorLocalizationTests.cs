using Entriqa.Application.Localization;
using Entriqa.Domain.Errors;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Exceptions carry a message key, the edge renders the text in the language of the request.
/// These tests pin both halves: the catalog and the language negotiation.
/// </summary>
public class ErrorMessageTests
{
    [Fact]
    public void GivenKnownKey_WhenRenderingWithoutLanguage_ThenGermanIsUsed() =>
        Assert.Equal("Einsendung nicht gefunden.", ErrorMessages.Get(null, ErrorMessages.SubmissionNotFound));

    [Fact]
    public void GivenKnownKey_WhenRenderingInEnglish_ThenTheEnglishTextIsUsed() =>
        Assert.Equal("Submission not found.", ErrorMessages.Get("en", ErrorMessages.SubmissionNotFound));

    [Fact]
    public void GivenUnknownLanguage_WhenRendering_ThenItFallsBackToGerman() =>
        Assert.Equal("Token fehlt.", ErrorMessages.Get("fr", ErrorMessages.TokenMissing));

    [Fact]
    public void GivenKeyWithPlaceholder_WhenRenderingWithArguments_ThenThePlaceholderIsReplaced()
    {
        var args = AppException.Args("slug", "kontakt");
        Assert.Equal("Formular 'kontakt' ist nicht veröffentlicht.", ErrorMessages.Get("de", ErrorMessages.FormNotPublished, args));
        Assert.Equal("Form 'kontakt' is not published.", ErrorMessages.Get("en", ErrorMessages.FormNotPublished, args));
    }

    [Fact]
    public void GivenUnknownKey_WhenRendering_ThenTheKeyItselfIsReturned() =>
        Assert.Equal("nope", ErrorMessages.Get("de", "nope"));

    [Fact]
    public void GivenEveryGermanKey_WhenLookingItUpInEnglish_ThenATranslationExists()
    {
        var missing = ErrorMessages.For("de").Keys.Where(k => !ErrorMessages.For("en").ContainsKey(k)).ToList();
        Assert.True(missing.Count == 0, "Keys without an English text: " + string.Join(", ", missing));
    }
}

public class AppExceptionLocalizationTests
{
    [Fact]
    public void GivenExceptionWithKeyAndArguments_WhenLocalized_ThenEachLanguageRendersItsOwnText()
    {
        var ex = new NotFoundException(ErrorCodes.FormNotFound, ErrorMessages.FormUnknown, AppException.Args("slug", "quiz"));

        Assert.Equal("Formular 'quiz' ist unbekannt.", ex.Localize("de"));
        Assert.Equal("Form 'quiz' is unknown.", ex.Localize("en"));
        Assert.Equal(404, ex.HttpStatus);
    }

    [Fact]
    public void GivenExceptionWithKey_WhenReadingMessage_ThenTheLogTextStaysGerman() =>
        Assert.Equal("Zu schnell abgeschickt.",
            new SecurityTokenException(ErrorCodes.TokenTooEarly, ErrorMessages.TokenTooEarly).Message);

    [Fact]
    public void GivenInfrastructureFailure_WhenLocalized_ThenTheCallerOnlyGetsTheGenericTextWhileTheDetailStaysInTheLog()
    {
        var ex = new InfrastructureException("ReportingCloud: HTTP 500 template broken");

        Assert.Equal("Interner Fehler.", ex.Localize("de"));
        Assert.Equal("Internal error.", ex.Localize("en"));
        Assert.Equal("ReportingCloud: HTTP 500 template broken", ex.Detail);
        Assert.Contains("ReportingCloud", ex.ToString());
    }
}

public class LocaleNegotiationTests
{
    private static readonly string[] Site = { "de", "en" };

    [Fact]
    public void GivenExplicitLanguage_WhenNegotiating_ThenItWinsOverAcceptLanguage() =>
        Assert.Equal("en", LocaleNegotiation.Pick("en", "de-DE,de;q=0.9", Site));

    [Fact]
    public void GivenNoExplicitLanguage_WhenNegotiating_ThenAcceptLanguageDecides() =>
        Assert.Equal("en", LocaleNegotiation.Pick(null, "en-GB,en;q=0.9", Site));

    [Fact]
    public void GivenAcceptLanguageWithQualities_WhenNegotiating_ThenTheBestConfiguredOneWins() =>
        Assert.Equal("de", LocaleNegotiation.Pick(null, "fr;q=0.9,de;q=0.8,en;q=0.7", Site));

    [Fact]
    public void GivenLanguageThatIsNotConfigured_WhenNegotiating_ThenTheFirstSiteLocaleIsUsed() =>
        Assert.Equal("de", LocaleNegotiation.Pick("fr", "fr-FR", Site));

    [Fact]
    public void GivenNoSignalAtAll_WhenNegotiating_ThenTheFirstSiteLocaleIsUsed() =>
        Assert.Equal("de", LocaleNegotiation.Pick(null, null, Site));

    [Fact]
    public void GivenSiteWithoutGerman_WhenNegotiating_ThenItsOwnFirstLocaleIsTheFallback() =>
        Assert.Equal("en", LocaleNegotiation.Pick(null, null, new[] { "en", "fr" }));

    [Fact]
    public void GivenAcceptLanguageRefusingALanguage_WhenNegotiating_ThenTheZeroQualityTagIsIgnored() =>
        Assert.Equal("de", LocaleNegotiation.Pick(null, "en;q=0, de;q=0.5", Site));
}
