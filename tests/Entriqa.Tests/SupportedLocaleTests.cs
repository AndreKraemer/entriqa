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
            ["zz"] = new Dictionary<string, string> { ["required"] = "Zz required.", ["next"] = "Zz next" },
            ["yy"] = new Dictionary<string, string> { ["required"] = "Yy required." },   // incomplete on purpose
        };

    // AC5: a language added to the catalog becomes available on its own; an incomplete one never does.
    [Fact]
    public void GivenACatalogWithACompleteAndAnIncompleteLocale_WhenAskingForCompleteLocales_ThenOnlyTheCompleteOnesAreReturned() =>
        Assert.Equal(new[] { "de", "en", "zz" }, LocaleCatalog.CompleteLocales(Catalog(), ValidationMessages.Reference).OrderBy(l => l));

    // The reference locale defines what "complete" means, so without it nothing can qualify. Pinned
    // because the alternative - treating every locale as complete - would silently offer all of them.
    [Fact]
    public void GivenACatalogWithoutTheReferenceLocale_WhenAskingForCompleteLocales_ThenNoneQualify() =>
        Assert.Empty(LocaleCatalog.CompleteLocales(Catalog(), "xx"));

    // The AC3 guard only works because Texts() refuses to answer for a locale the catalog does not have.
    // Letting it fall back like For() would disarm that guard while every test stayed green.
    [Fact]
    public void GivenALocaleNoCatalogCarries_WhenAskingForItsTexts_ThenNothingIsReturned()
    {
        Assert.Null(ValidationMessages.Texts("zz"));
        Assert.Null(ErrorMessages.Texts("zz"));
    }

    // AC3: the "both catalogs" rule itself. On the shipped catalogs this is invisible - they carry the
    // same locales, so intersecting and unioning them agree - so it is checked against catalogs that differ.
    [Fact]
    public void GivenALocaleOnlyOneCatalogCarries_WhenAskingWhichAreCarriedByAll_ThenItIsNotAmongThem() =>
        Assert.Equal(new[] { "de" },
            SupportedLocales.CarriedByAll(new[] { "de", "en" }, new[] { "de" }));

    // AC1: a configured code without complete texts is not accepted as a language.
    [Fact]
    public void GivenAnUnsupportedCodeInTheConfiguration_WhenReadingTheSiteLocales_ThenOnlySupportedCodesRemain()
    {
        var options = TestData.Options();
        options.Locales = "de,en,zz";
        Assert.Equal(new[] { "de", "en" }, options.SiteLocales);
    }

    // AC2: the dropped code is named, so the startup warning can say which one it was.
    [Fact]
    public void GivenAnUnsupportedCodeInTheConfiguration_WhenReadingTheUnsupportedLocales_ThenThatCodeIsNamed()
    {
        var options = TestData.Options();
        options.Locales = "de,en,zz";
        Assert.Equal(new[] { "zz" }, options.UnsupportedLocales);
    }

    // AC1: filtering must not leave the site without any language at all.
    [Fact]
    public void GivenOnlyUnsupportedCodesInTheConfiguration_WhenReadingTheSiteLocales_ThenItFallsBackToGerman()
    {
        var options = TestData.Options();
        options.Locales = "zz";
        Assert.Equal(new[] { "de" }, options.SiteLocales);
    }

    // AC3: for every language the visitor can be served, both catalogs carry that language themselves.
    // Asked through For() this could never fail - For() falls back to German for anything unknown and so
    // answers for every locale ever passed. Texts() is the seam without that fallback, which is what makes
    // this test able to go red when All claims a language one of the catalogs does not have.
    [Fact]
    public void GivenEverySupportedLocale_WhenLookingUpBothCatalogs_ThenEveryKeyHasATextInThatLocale()
    {
        Assert.NotEmpty(SupportedLocales.All);  // otherwise the loop below would check nothing

        var gaps = new List<string>();
        foreach (var locale in SupportedLocales.All)
        {
            Check("ValidationMessages", ValidationMessages.Texts(locale), ValidationMessages.Texts(ValidationMessages.Reference)!);
            Check("ErrorMessages", ErrorMessages.Texts(locale), ErrorMessages.Texts(ErrorMessages.Reference)!);

            void Check(string catalog, IReadOnlyDictionary<string, string>? texts, IReadOnlyDictionary<string, string> reference)
            {
                if (texts is null) { gaps.Add($"{catalog}[{locale}]: the catalog does not carry this locale at all"); return; }
                foreach (var key in reference.Keys)
                    if (!texts.ContainsKey(key)) gaps.Add($"{catalog}[{locale}]:{key}");
            }
        }
        Assert.True(gaps.Count == 0, "Supported locales with missing texts: " + string.Join(", ", gaps));
    }
}
