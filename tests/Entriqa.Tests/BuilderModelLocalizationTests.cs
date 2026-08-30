using System.Text.Json.Nodes;
using Entriqa.Admin.Services;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The builder's half of issue #3: how a step's configuration is split per language when it is loaded
/// and put back together when it is saved. Three defects lived here undetected because nothing in this
/// project executed <c>BuilderModel</c> - not because it cannot be executed, but because the admin was
/// assumed unreachable from the test host. Only its Razor <em>components</em> are; the plain model
/// classes load fine, which is why these are ordinary tests rather than source guards.
/// </summary>
public class BuilderModelLocalizationTests
{
    private const string Schema =
        """
        {"type":"object","required":["templateId"],"properties":{
          "templateId":{"type":"integer","title":"Vorlage","format":"brevo-template","localizable":true},
          "attach":{"type":"string","enum":["none","download"],"title":"Mitschicken","default":"none"},
          "templates":{"type":"object","title":"Vorlage je Ergebnis","additionalProperties":{"type":"string","localizable":true}}}}
        """;

    private static StepModel Step(string config, params string[] locales)
    {
        var schema = StepSchema.Parse(Schema);
        var step = new StepModel { Id = "s1", Key = "brevo.mail", ConfigRaw = JsonNode.Parse(config)!.AsObject(), Schema = schema };
        step.LoadConfigText(schema, locales);
        return step;
    }

    private static string Saved(StepModel step, params string[] locales) => step.ToNode(locales)["config"]!.ToJsonString();

    // --- the schema markers ---------------------------------------------------------------------

    [Fact]
    public void GivenAStepSchema_WhenParsingIt_ThenTheLocalizableMarkersAreRead()
    {
        var schema = StepSchema.Parse(Schema);

        Assert.True(schema.Properties.Single(p => p.Name == "templateId").Localizable);
        Assert.False(schema.Properties.Single(p => p.Name == "attach").Localizable);
        // The map itself is not a locale map - only its members are.
        Assert.False(schema.Properties.Single(p => p.Name == "templates").Localizable);
        Assert.True(schema.Properties.Single(p => p.Name == "templates").LocalizableItems);
    }

    [Fact]
    public void GivenALocalizableProperty_WhenLoadingTheStep_ThenItIsHeldPerLanguageAndNotAsASingleText()
    {
        var step = Step("""{"templateId":{"de":3,"en":9},"attach":"download"}""", "de", "en");

        Assert.Equal("3", step.ConfigTextByLocale["templateId"]["de"]);
        Assert.Equal("9", step.ConfigTextByLocale["templateId"]["en"]);
        Assert.False(step.ConfigText.ContainsKey("templateId"));      // never as one raw text
        Assert.Equal("download", step.ConfigText["attach"]);          // an ordinary field is untouched
    }

    // --- the round trip -------------------------------------------------------------------------

    [Fact]
    public void GivenAValuePerLanguage_WhenSavingWithoutEditing_ThenItIsWrittenBackUnchanged()
    {
        var step = Step("""{"templateId":{"de":3,"en":9}}""", "de", "en");

        Assert.Equal("""{"templateId":{"de":3,"en":9}}""", Saved(step, "de", "en"));
    }

    [Fact]
    public void GivenAPlainValue_WhenSavingWithoutEditing_ThenItStaysPlain()
    {
        var step = Step("""{"templateId":3}""", "de", "en");

        Assert.Equal("""{"templateId":3}""", Saved(step, "de", "en"));
    }

    [Fact]
    public void GivenAnEditedLanguage_WhenSaving_ThenThatLanguagesValueIsStoredAsANumberNotText()
    {
        var step = Step("""{"templateId":3}""", "de", "en");

        step.ConfigTextByLocale["templateId"]["en"] = "9";

        // The schema type still applies per language: a quoted "9" here would reach Brevo as a string.
        Assert.Equal("""{"templateId":{"de":3,"en":9}}""", Saved(step, "de", "en"));
    }

    [Fact]
    public void GivenEveryLanguageEditedToTheSameValue_WhenSaving_ThenItCollapsesBackToAPlainValue()
    {
        var step = Step("""{"templateId":{"de":3,"en":9}}""", "de", "en");

        step.ConfigTextByLocale["templateId"]["en"] = "3";

        Assert.Equal("""{"templateId":3}""", Saved(step, "de", "en"));
    }

    [Fact]
    public void GivenALocalizableFieldClearedEverywhere_WhenSaving_ThenTheFieldIsRemoved()
    {
        var step = Step("""{"templateId":3}""", "de", "en");

        step.ConfigTextByLocale["templateId"]["de"] = "";
        step.ConfigTextByLocale["templateId"]["en"] = "";

        Assert.Equal("{}", Saved(step, "de", "en"));
    }

    // --- a language switched on after the step was loaded ---------------------------------------

    [Fact]
    public void GivenAPlainValueAndALanguageSwitchedOn_WhenSaving_ThenTheValueStillCoversEveryLanguage()
    {
        // The story's own primary flow: a German form gains English and nobody touches the steps.
        // Without ReconcileLocales this wrote {"de":3} and the form could no longer be published.
        var step = Step("""{"templateId":3}""", "de");

        step.ReconcileLocales(new[] { "de", "en" });

        Assert.Equal("3", step.ConfigTextByLocale["templateId"]["en"]);   // the editor shows it, too
        Assert.Equal("""{"templateId":3}""", Saved(step, "de", "en"));
    }

    [Fact]
    public void GivenAPartlyTranslatedFieldAndALanguageSwitchedOn_WhenReconciling_ThenTheNewLanguageStaysEmpty()
    {
        // Nothing to inherit here - de and en disagree, so guessing one for fr would be worse than
        // letting the publish check ask for it.
        var step = Step("""{"templateId":{"de":3,"en":9}}""", "de", "en");

        step.ReconcileLocales(new[] { "de", "en", "fr" });

        Assert.Equal("", step.ConfigTextByLocale["templateId"]["fr"]);
    }

    [Fact]
    public void GivenALanguageSwitchedOff_WhenSaving_ThenItsValueIsKept()
    {
        // Switching a language off and on again must not lose what was configured for it.
        var step = Step("""{"templateId":{"de":3,"en":9}}""", "de", "en");

        Assert.Equal("""{"templateId":{"de":3,"en":9}}""", Saved(step, "de"));
    }

    // --- unknown properties and ordinary fields --------------------------------------------------

    [Fact]
    public void GivenAConfigurationWithAPropertyTheSchemaDoesNotKnow_WhenSaving_ThenItSurvives()
    {
        var step = Step("""{"templateId":3,"legacyOption":true}""", "de", "en");

        Assert.Contains("""legacyOption":true""", Saved(step, "de", "en"), StringComparison.Ordinal);
    }
}
