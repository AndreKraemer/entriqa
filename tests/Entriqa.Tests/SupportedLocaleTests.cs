using Entriqa.Application;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Localization;
using Entriqa.Domain.Validation;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Entriqa may only offer a language it can serve completely. These tests pin the derivation
/// (which locales a catalog supports), the filtering of the configured codes, and the promise
/// the visitor actually cares about: never a form whose texts come from two languages.
/// </summary>
public class SupportedLocaleTests
{
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Catalog() =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["de"] = new Dictionary<string, string> { ["required"] = "Pflichtfeld.", ["next"] = "Weiter" },
            ["en"] = new Dictionary<string, string> { ["required"] = "Required.", ["next"] = "Next" },
            ["fr"] = new Dictionary<string, string> { ["required"] = "Obligatoire.", ["next"] = "Suivant" },
            ["it"] = new Dictionary<string, string> { ["required"] = "Obbligatorio." },
        };

    // AC5: a language added to the catalog becomes available on its own; an incomplete one never does.
    [Fact]
    public void GivenACatalogWithACompleteAndAnIncompleteLocale_WhenAskingForCompleteLocales_ThenOnlyTheCompleteOnesAreReturned() =>
        Assert.Equal(new[] { "de", "en", "fr" }, LocaleCatalog.CompleteLocales(Catalog(), "de").OrderBy(l => l));

    // AC1: a configured code without complete texts is not accepted as a language.
    [Fact]
    public void GivenAnUnsupportedCodeInTheConfiguration_WhenReadingTheSiteLocales_ThenOnlySupportedCodesRemain()
    {
        var options = TestData.Options();
        options.Locales = "de,en,fr";
        Assert.Equal(new[] { "de", "en" }, options.SiteLocales);
    }

    // AC2: the dropped code is named, so the startup warning can say which one it was.
    [Fact]
    public void GivenAnUnsupportedCodeInTheConfiguration_WhenReadingTheUnsupportedLocales_ThenThatCodeIsNamed()
    {
        var options = TestData.Options();
        options.Locales = "de,en,fr";
        Assert.Equal(new[] { "fr" }, options.UnsupportedLocales);
    }

    // AC1: filtering must not leave the site without any language at all.
    [Fact]
    public void GivenOnlyUnsupportedCodesInTheConfiguration_WhenReadingTheSiteLocales_ThenItFallsBackToGerman()
    {
        var options = TestData.Options();
        options.Locales = "fr";
        Assert.Equal(new[] { "de" }, options.SiteLocales);
    }

    // AC3: for every language the visitor can be served, both catalogs answer in that language -
    // a missing key would silently render the German text next to a translated field.
    [Fact]
    public void GivenEverySupportedLocale_WhenLookingUpBothCatalogs_ThenEveryKeyHasATextInThatLocale()
    {
        var gaps = new List<string>();
        foreach (var locale in SupportedLocales.All)
        {
            foreach (var key in ValidationMessages.For("de").Keys)
                if (!ValidationMessages.For(locale).ContainsKey(key)) gaps.Add($"ValidationMessages[{locale}]:{key}");
            foreach (var key in ErrorMessages.For("de").Keys)
                if (!ErrorMessages.For(locale).ContainsKey(key)) gaps.Add($"ErrorMessages[{locale}]:{key}");
        }
        Assert.True(gaps.Count == 0, "Supported locales with missing texts: " + string.Join(", ", gaps));
    }
}
