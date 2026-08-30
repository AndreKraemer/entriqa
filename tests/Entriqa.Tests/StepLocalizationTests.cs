using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #3: a step field whose result reaches the visitor carries a value per language, and the step
/// picks the one for the language of the submission. Storage is the two shapes <see cref="LText"/>
/// already uses - a plain scalar or {locale: value} - so nothing has to be migrated.
/// </summary>
public class StepLocalizationTests
{
    // ---------------------------------------------------------------- the marked fields (AC 7, AC 5)

    /// <summary>
    /// Every step the assembly defines, discovered the way DI does rather than listed here: AC 7 is an
    /// invariant over the whole catalog, and a tenth step marking its webhook URL localizable has to fail
    /// it. A hand-kept list would simply not see that step.
    /// </summary>
    private static ISubmissionStep[] AllSteps() =>
        typeof(ISubmissionStep).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISubmissionStep).IsAssignableFrom(t))
            .Select(Instantiate)
            .ToArray();

    /// <summary>Real instances, not substitutes: the steps are sealed and their schemas are ordinary
    /// property implementations, so a mock would hand back null for every one of them.</summary>
    private static ISubmissionStep Instantiate(Type step) =>
        (ISubmissionStep)Activator.CreateInstance(step,
            step.GetConstructors().Single().GetParameters().Select(p => Argument(p.ParameterType)).ToArray())!;

    private static object Argument(Type type) =>
        type == typeof(TimeProvider) ? TestData.Time
        : type == typeof(FormTokenService) ? TestData.Tokens()          // sealed, so not substitutable
        : Substitute.For(new[] { type }, Array.Empty<object>());

    /// <summary>
    /// Where a step schema marks a localizable leaf, and the JSON type of that leaf. The marker sits on the
    /// property itself, or - for a map whose members are the leaves - on its <c>additionalProperties</c>,
    /// which is reported as "step.property.*".
    /// </summary>
    private static IEnumerable<(string Field, string Type)> MarkedLeaves(ISubmissionStep step)
    {
        var root = JsonNode.Parse(step.ConfigSchema)!.AsObject();
        foreach (var (name, node) in root["properties"]?.AsObject() ?? new JsonObject())
        {
            var o = node!.AsObject();
            if (o["localizable"]?.GetValue<bool>() == true)
                yield return ($"{step.Key}.{name}", o["type"]?.GetValue<string>() ?? "string");
            if (o["additionalProperties"] is JsonObject items && items["localizable"]?.GetValue<bool>() == true)
                yield return ($"{step.Key}.{name}.*", items["type"]?.GetValue<string>() ?? "string");
        }
    }

    [Fact]
    public void GivenTheStepCatalog_WhenListingLocalizableConfigFields_ThenExactlyTheVisitorFacingOnesAreMarked()
    {
        var expected = new[]
        {
            "brevo.contact.listIds",            // which list an English lead lands in
            "brevo.mail.templateId",            // the mail to the participant
            "doi.request.templateId",           // the confirmation mail
            "leadmagnet.link.blob",             // the file behind the download link
            "reportingcloud.pdf.template",      // the PDF template of a form without a quiz
            "reportingcloud.pdf.templates.*",   // and one per quiz result
        };

        var marked = AllSteps().SelectMany(MarkedLeaves).Select(x => x.Field).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        // Internal notification, Teams card and webhook stay single-language - a marker on one of them
        // fails here just as loudly as a forgotten one.
        Assert.Equal(expected, marked);
    }

    [Fact]
    public void GivenALocalizableConfigField_WhenReadingItsSchemaType_ThenItIsNeverAnObject()
    {
        var leaves = AllSteps().SelectMany(MarkedLeaves).ToList();

        Assert.NotEmpty(leaves);                 // otherwise this test would silently check nothing
        // An object-typed property is what sends StepEditor into its raw-JSON textarea, which AC 5 forbids
        // for a localizable field. It is also what would make {de: ...} ambiguous when reading the value.
        Assert.DoesNotContain(leaves, l => l.Type == "object");
    }

    // ---------------------------------------------------------------- the resolution rule (AC 1, AC 2)

    private static JsonElement Value(string json) => TestData.Json(json);

    [Theory]
    [InlineData("3")]                            // an integer field, e.g. templateId
    [InlineData("[7, 8]")]                       // an array field, e.g. listIds
    [InlineData("\"leadmagnets/x.pdf\"")]        // a string field, e.g. blob
    public void GivenAPlainValue_WhenResolvingForAnyLanguage_ThenItAppliesToEveryLanguage(string json)
    {
        var value = Value(json);

        Assert.Equal(json, LValue.Resolve(value, "de").GetRawText());
        Assert.Equal(json, LValue.Resolve(value, "en").GetRawText());
        Assert.True(LValue.Covers(value, "en"));
        Assert.Empty(LValue.MissingLocales(value, new[] { "de", "en" }));
    }

    [Fact]
    public void GivenAValuePerLanguage_WhenResolvingForOneOfThem_ThenThatLanguagesValueIsUsed()
    {
        var value = Value("""{"de":3,"en":9}""");

        Assert.Equal(9, LValue.Resolve(value, "en").GetInt32());
        Assert.Equal(3, LValue.Resolve(value, "de").GetInt32());
    }

    [Fact]
    public void GivenAValueMissingALanguage_WhenResolvingForIt_ThenItFallsBackToTheFirstEntry()
    {
        var value = Value("""{"de":3}""");

        // The same fallback LText.Resolve uses. A step that falls back beats a step that throws and
        // blocks the pipeline; the publish check is what stops this happening on purpose.
        Assert.Equal(3, LValue.Resolve(value, "en").GetInt32());
        Assert.False(LValue.Covers(value, "en"));
        Assert.Equal(new[] { "en" }, LValue.MissingLocales(value, new[] { "de", "en" }));
    }

    // ---------------------------------------------------------------- the steps (AC 1)

    private static StepContext Context(string? locale, QuizOutcome? quiz = null, FormDefinition? form = null) => new()
    {
        Submission = new Submission
        {
            Id = "whitepaper:1", Slug = "whitepaper", Version = 1, CreatedAt = TestData.Time.GetUtcNow(),
            Values = new Dictionary<string, string>(), Email = "a@b.de", Locale = locale, Quiz = quiz,
        },
        Form = form ?? TestData.Contact() with { Locales = new[] { "de", "en" } },
        FormVersion = 1,
        Options = TestData.Options(),
    };

    [Fact]
    public async Task GivenAConfirmationTemplatePerLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenTheEnglishTemplateIsUsed()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var step = new DoiRequestStep(mail, TestData.Tokens());

        await step.ExecuteAsync(Context("en"), Value("""{"templateId":{"de":3,"en":9}}"""), default);

        await mail.Received(1).SendAsync("a@b.de", Arg.Any<string?>(), 9,
            Arg.Any<Dictionary<string, object?>>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAParticipantMailTemplatePerLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenTheEnglishTemplateIsUsed()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var step = new BrevoMailStep(mail, Substitute.For<IStoreArtifactPort>(), Substitute.For<ICreateDownloadLinkPort>());

        await step.ExecuteAsync(Context("en"), Value("""{"templateId":{"de":4,"en":11}}"""), default);

        await mail.Received(1).SendAsync("a@b.de", Arg.Any<string?>(), 11,
            Arg.Any<Dictionary<string, object?>>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenBrevoListsPerLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenTheEnglishListsAreUsed()
    {
        var contacts = Substitute.For<IUpsertBrevoContactPort>();
        var step = new BrevoContactStep(contacts, TestData.Time);

        await step.ExecuteAsync(Context("en"), Value("""{"listIds":{"de":[7],"en":[8,9]}}"""), default);

        await contacts.Received(1).UpsertAsync("a@b.de", Arg.Is<IReadOnlyList<int>>(l => l.SequenceEqual(new[] { 8, 9 })),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenALeadMagnetFilePerLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenTheEnglishFileIsLinked()
    {
        var links = Substitute.For<ICreateDownloadLinkPort>();
        links.CreateAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://example.org/download"));
        var step = new LeadMagnetLinkStep(links);

        await step.ExecuteAsync(Context("en"),
            Value("""{"blob":{"de":"leadmagnets/wp-de.pdf","en":"leadmagnets/wp-en.pdf"}}"""), default);

        await links.Received(1).CreateAsync("leadmagnets/wp-en.pdf", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAPdfTemplatePerLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenTheEnglishTemplateIsMerged()
    {
        var merge = Substitute.For<IMergeDocumentPort>();
        merge.MergeToPdfAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1 });
        var step = new ReportingCloudPdfStep(merge, Substitute.For<IStoreArtifactPort>(), TestData.Time);

        await step.ExecuteAsync(Context("en"), Value("""{"template":{"de":"de.docx","en":"en.docx"}}"""), default);

        await merge.Received(1).MergeToPdfAsync("en.docx", Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAQuizPdfTemplatePerResultAndLanguage_WhenTheStepRunsForAnEnglishSubmission_ThenThatResultsEnglishTemplateIsMerged()
    {
        var merge = Substitute.For<IMergeDocumentPort>();
        merge.MergeToPdfAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(new byte[] { 1 });
        var step = new ReportingCloudPdfStep(merge, Substitute.For<IStoreArtifactPort>(), TestData.Time);
        var form = TestData.Contact() with { Locales = new[] { "de", "en" }, Quiz = TestData.Quiz() };
        var outcome = new QuizOutcome(new Dictionary<string, string> { ["q1"] = "a", ["q1b"] = "a" },
            new[] { "q1", "q1b" }, 0, 4, 0, "legacy", ReachedByJump: true);

        await step.ExecuteAsync(Context("en", outcome, form),
            Value("""{"templates":{"legacy":{"de":"legacy-de.docx","en":"legacy-en.docx"}}}"""), default);

        // The result dimension is the outer one and stays as it was; only the leaf gained a language.
        await merge.Received(1).MergeToPdfAsync("legacy-en.docx", Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- unchanged behaviour (AC 2, AC 3)

    [Fact]
    public async Task GivenASingleTemplateAndAFormInTwoLanguages_WhenTheStepRunsForEachOfThem_ThenTheSameTemplateIsUsed()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var step = new DoiRequestStep(mail, TestData.Tokens());
        var config = Value("""{"templateId":3}""");

        await step.ExecuteAsync(Context("de"), config, default);
        await step.ExecuteAsync(Context("en"), config, default);

        await mail.Received(2).SendAsync("a@b.de", Arg.Any<string?>(), 3,
            Arg.Any<Dictionary<string, object?>>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GivenAPublishedFormConfiguredWithPlainValues_WhenCheckingBeforePublish_ThenNothingIsReported()
    {
        var form = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Pipeline = new[]
            {
                new StepDefinition("s1", "doi.request", "always", Value("""{"templateId":3}""")),
                new StepDefinition("s2", "brevo.contact", "always", Value("""{"listIds":[7]}""")),
                new StepDefinition("s3", "leadmagnet.link", "always", Value("""{"blob":"leadmagnets/wp.pdf"}""")),
            },
        };

        // Not "no message mentioning a language": the regression this guards - a plain value no longer
        // being read - surfaces as "keine Brevo-Liste gewählt.", which a language filter would let past.
        Assert.Equal(Array.Empty<string>(), Check().Check(form));
    }

    [Fact]
    public async Task GivenASingleListAndAFormInTwoLanguages_WhenTheStepRunsForEachOfThem_ThenTheSameListsAreUsed()
    {
        var contacts = Substitute.For<IUpsertBrevoContactPort>();
        var step = new BrevoContactStep(contacts, TestData.Time);
        var config = Value("""{"listIds":[7]}""");

        await step.ExecuteAsync(Context("de"), config, default);
        await step.ExecuteAsync(Context("en"), config, default);

        await contacts.Received(2).UpsertAsync("a@b.de", Arg.Is<IReadOnlyList<int>>(l => l.SequenceEqual(new[] { 7 })),
            Arg.Any<Dictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenASingleFileAndAFormInTwoLanguages_WhenTheStepRunsForEachOfThem_ThenTheSameFileIsLinked()
    {
        var links = Substitute.For<ICreateDownloadLinkPort>();
        links.CreateAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://example.org/download"));
        var step = new LeadMagnetLinkStep(links);
        var config = Value("""{"blob":"leadmagnets/wp.pdf"}""");

        await step.ExecuteAsync(Context("de"), config, default);
        await step.ExecuteAsync(Context("en"), config, default);

        await links.Received(2).CreateAsync("leadmagnets/wp.pdf", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- a blank is not a value

    [Fact]
    public void GivenALanguageWhoseValueIsBlank_WhenCheckingCoverage_ThenItCountsAsMissing()
    {
        // The builder drops blanks on save, but the JSON tab and the admin API write a definition
        // straight through - and an empty blob reaches the storage as a request for a link to "".
        Assert.Equal(new[] { "de" }, LValue.MissingLocales(Value("""{"de":"","en":"x"}"""), DeEn));
        Assert.Equal(new[] { "de" }, LValue.MissingLocales(Value("""{"de":[],"en":[8]}"""), DeEn));
        Assert.False(LValue.Covers(Value("""{"de":"  ","en":"x"}"""), "de"));
    }

    [Fact]
    public void GivenALanguageWhoseValueIsBlank_WhenResolvingForIt_ThenALanguageThatHasOneIsUsed()
    {
        Assert.Equal("x", LValue.Resolve(Value("""{"de":"","en":"x"}"""), "de").GetString());
    }

    [Fact]
    public void GivenALocalizableFieldThatIsBlankForOneLanguage_WhenCheckingBeforePublish_ThenItIsRefused()
    {
        var form = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Pipeline = new[] { new StepDefinition("s1", "leadmagnet.link", "always", Value("""{"blob":{"de":"leadmagnets/a.pdf","en":""}}""")) },
        };

        Assert.Contains(Check().Check(form), i => i.Contains("Datei", StringComparison.Ordinal) && i.Contains("'en'", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the publish check (AC 6)

    private static PublishCheckService Check()
    {
        var steps = AllSteps();
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time,
            NullLogger<SubmissionPipelineService>.Instance);
        return new PublishCheckService(pipeline);
    }

    [Fact]
    public void GivenALocalizableFieldWithoutAValueForADeclaredLanguage_WhenCheckingBeforePublish_ThenStepFieldAndLanguageAreNamed()
    {
        var form = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Pipeline = new[] { new StepDefinition("s1", "doi.request", "always", Value("""{"templateId":{"de":3}}""")) },
        };

        var issues = Check().Check(form);

        Assert.Contains(issues, i => i.Contains("Schritt 1", StringComparison.Ordinal)
                                     && i.Contains("Bestätigungsmail", StringComparison.Ordinal)
                                     && i.Contains("'en'", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenAQuizPdfTemplateMissingALanguageForOneResult_WhenCheckingBeforePublish_ThenResultAndLanguageAreNamed()
    {
        var form = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Quiz = TestData.Quiz(),
            Pipeline = new[]
            {
                new StepDefinition("s1", "reportingcloud.pdf", "always", Value("""
                    {"templates":{"legacy":{"de":"legacy-de.docx"},"mitte":"m.docx","modern":"mo.docx"}}
                    """)),
            },
        };

        var issues = Check().Check(form);

        Assert.Contains(issues, i => i.Contains("Legacy", StringComparison.Ordinal) && i.Contains("'en'", StringComparison.Ordinal));
        // "mitte" and "modern" carry a plain value, which covers both languages - they must stay silent.
        Assert.DoesNotContain(issues, i => i.Contains("Mitte", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the editor round trip (AC 4)

    private static readonly string[] DeEn = { "de", "en" };

    [Fact]
    public void GivenAPlainValue_WhenTheEditorLoadsIt_ThenEveryLanguageShowsIt()
    {
        var perLocale = LValue.Decompose(JsonNode.Parse("3"), DeEn);

        Assert.Equal("3", perLocale["de"]!.ToJsonString());
        Assert.Equal("3", perLocale["en"]!.ToJsonString());
    }

    [Fact]
    public void GivenAValuePerLanguage_WhenTheEditorLoadsIt_ThenEachLanguageGetsItsOwnEntry()
    {
        var perLocale = LValue.Decompose(JsonNode.Parse("""{"de":3,"en":9}"""), DeEn);

        Assert.Equal("3", perLocale["de"]!.ToJsonString());
        Assert.Equal("9", perLocale["en"]!.ToJsonString());
    }

    [Fact]
    public void GivenEveryLanguageCarryingTheSameValue_WhenTheEditorWritesItBack_ThenAPlainValueIsStored()
    {
        var perLocale = new Dictionary<string, JsonNode?> { ["de"] = JsonNode.Parse("3"), ["en"] = JsonNode.Parse("3") };

        // Without this a single-language form - and any form nobody translated - would be rewritten into
        // {"de":3,"en":3} the first time it is opened and saved.
        Assert.Equal("3", LValue.Compose(perLocale, DeEn)!.ToJsonString());
    }

    [Fact]
    public void GivenLanguagesCarryingDifferentValues_WhenTheEditorWritesItBack_ThenTheLocaleObjectIsStored()
    {
        var perLocale = new Dictionary<string, JsonNode?> { ["de"] = JsonNode.Parse("3"), ["en"] = JsonNode.Parse("9") };

        Assert.Equal("""{"de":3,"en":9}""", LValue.Compose(perLocale, DeEn)!.ToJsonString());
    }

    [Fact]
    public void GivenNoLanguageCarryingAValue_WhenTheEditorWritesItBack_ThenNothingIsStored()
    {
        var perLocale = new Dictionary<string, JsonNode?> { ["de"] = null, ["en"] = null };

        Assert.Null(LValue.Compose(perLocale, DeEn));
    }
}
