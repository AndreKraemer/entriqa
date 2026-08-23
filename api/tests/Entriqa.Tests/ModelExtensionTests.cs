using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Quiz;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.Validation;
using Xunit;

namespace Entriqa.Tests;

public class LTextTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialisiert_String_und_Objekt()
    {
        var single = JsonSerializer.Deserialize<LText>("\"Hallo\"", Json)!;
        var map = JsonSerializer.Deserialize<LText>("""{"de":"Hallo","en":"Hello"}""", Json)!;
        Assert.Equal("Hallo", single.Resolve("en"));            // einfacher String gilt für alle Sprachen
        Assert.Equal("Hello", map.Resolve("en"));
        Assert.Equal("Hallo", map.Resolve("de"));
        Assert.Equal("Hallo", map.Resolve("fr"));               // Fallback: erster Eintrag
        Assert.True(single.Covers("en"));
        Assert.False(map.Covers("fr"));
    }

    [Fact]
    public void Serialisiert_beide_Formen_rund()
    {
        Assert.Equal("\"Hallo\"", JsonSerializer.Serialize<LText>("Hallo", Json));
        var map = JsonSerializer.Deserialize<LText>("""{"de":"A","en":"B"}""", Json)!;
        Assert.Contains("\"en\":\"B\"", JsonSerializer.Serialize(map, Json));
    }

    [Fact]
    public void Localize_loest_Definition_auf()
    {
        var def = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Intro = new LText(new Dictionary<string, string> { ["de"] = "Schreib uns", ["en"] = "Write to us" }),
        };
        Assert.Equal("Write to us", def.Localize("en").Intro!.ToString());
        Assert.Equal("Schreib uns", def.Localize("de").Intro!.ToString());
        Assert.Equal("Schreib uns", def.Localize("fr").Intro!.ToString());  // unbekannt → Standard-Locale (erste)
    }
}

public class BusinessOnlyTests
{
    private static FormDefinition Def() => TestData.Contact() with
    {
        Fields = new[]
        {
            new FieldDefinition("email", FieldTypes.Email, "E-Mail", Required: true, BusinessOnly: true),
            new FieldDefinition("consent", FieldTypes.Consent, "Einwilligung", Required: true, Text: "Ich stimme zu."),
        },
    };

    [Fact]
    public void Freemail_wird_abgelehnt_Business_nicht()
    {
        var ok = new Dictionary<string, string> { ["email"] = "a@beispiel-gmbh.de", ["consent"] = "true" };
        FormSubmissionValidator.ValidateAndThrow(Def(), ok, null);

        var bad = new Dictionary<string, string> { ["email"] = "a@gmail.com", ["consent"] = "true" };
        var ex = Assert.Throws<ValidationException>(() => FormSubmissionValidator.ValidateAndThrow(Def(), bad, null));
        Assert.Contains(ex.Errors, e => e.Field == "email" && e.Message.Contains("geschäftliche"));
    }

    [Fact]
    public void Meldung_folgt_der_Sprache()
    {
        var bad = new Dictionary<string, string> { ["email"] = "a@web.de", ["consent"] = "true" };
        var ex = Assert.Throws<ValidationException>(() => FormSubmissionValidator.ValidateAndThrow(Def(), bad, null, locale: "en"));
        Assert.Contains(ex.Errors, e => e.Field == "email" && e.Message.Contains("business email"));
    }

    [Fact]
    public void Zusatzliste_aus_den_Settings_greift()
    {
        var bad = new Dictionary<string, string> { ["email"] = "a@wegwerf.example", ["consent"] = "true" };
        var extra = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wegwerf.example" };
        Assert.Throws<ValidationException>(() => FormSubmissionValidator.ValidateAndThrow(Def(), bad, null, extraFreemailDomains: extra));
    }
}

public class QuizFindingsTests
{
    private static QuizDefinition Quiz(QuizFindings? findings) => new("sum", "optional",
        Questions: new[]
        {
            new QuizQuestion("1", "F1", new[] { new QuizOption("a", "gut", 0), new QuizOption("b", "mittel", 1), new QuizOption("c", "schlecht", 2, Finding: "Finding 1") }, Topic: "Wartung"),
            new QuizQuestion("2", "F2", new[] { new QuizOption("a", "gut", 0), new QuizOption("b", "mittel", 1), new QuizOption("c", "schlecht", 2, Finding: "Finding 2") }, Topic: "Tests"),
            new QuizQuestion("3", "F3", new[] { new QuizOption("a", "gut", 0), new QuizOption("b", "mittel", 1), new QuizOption("c", "schlecht", 2, Finding: "Finding 3") }, Topic: "Deployment"),
        },
        Results: new[] { new QuizResult("r", 0, 100, "Ergebnis", "…") },
        Findings: findings);

    private static Dictionary<string, string> Answers(string a1, string a2, string a3) =>
        new() { ["1"] = a1, ["2"] = a2, ["3"] = a3 };

    [Fact]
    public void Prioritaet_und_Maximum_bestimmen_die_Auswahl()
    {
        var quiz = Quiz(new QuizFindings(Max: 2, Priority: new[] { "3", "1", "2" }));
        var outcome = QuizEngine.Evaluate(quiz, Answers("c", "c", "c"));
        Assert.Equal(new[] { "Finding 3", "Finding 1" }, outcome.Findings);
    }

    [Fact]
    public void Warn_Template_fuellt_ueber_1_Punkt_Themen_auf()
    {
        var quiz = Quiz(new QuizFindings(WarningTemplate: "Achte auf {topic}.", EmptyText: "Alles gut."));
        var outcome = QuizEngine.Evaluate(quiz, Answers("c", "b", "a"));   // 1 Finding + 1-Punkt-Antwort bei "Tests"
        Assert.Equal(new[] { "Finding 1", "Achte auf Tests." }, outcome.Findings);
    }

    [Fact]
    public void Leertext_wenn_nichts_auffiel()
    {
        var quiz = Quiz(new QuizFindings(WarningTemplate: "Achte auf {topic}.", EmptyText: "Alles gut."));
        var outcome = QuizEngine.Evaluate(quiz, Answers("a", "a", "a"));
        Assert.Equal(new[] { "Alles gut." }, outcome.Findings);
    }

    [Fact]
    public void Ohne_Findings_Konfiguration_bleibt_null()
    {
        Assert.Null(QuizEngine.Evaluate(Quiz(null), Answers("c", "c", "c")).Findings);
    }
}

public class CriticalFlagTests
{
    private sealed class FakeStep(string key, bool critical, StepResult result) : ISubmissionStep
    {
        public int Calls;
        public string Key => key; public string Name => key; public string Description => ""; public StepMode Mode => StepMode.Inline;
        public bool CriticalByDefault => critical; public string ConfigSchema => "{}";
        public Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct)
        { Calls++; return Task.FromResult(result); }
    }

    [Fact]
    public async Task Unkritischer_Fehlschlag_blockiert_die_Folgeschritte_nicht()
    {
        var notify = new FakeStep("teams.notify", critical: false, StepResult.Failed("Webhook 500"));
        var contact = new FakeStep("brevo.contact", critical: true, StepResult.Ok);
        var svc = new SubmissionPipelineService(new ISubmissionStep[] { notify, contact }, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        var form = TestData.Contact() with { Pipeline = new[] { new StepDefinition("s1", "teams.notify", "always", TestData.Json("{}")), new StepDefinition("s2", "brevo.contact", "always", TestData.Json("{}")) } };
        var s = new Submission { Id = "kontakt:1", Slug = "kontakt", Version = 1, CreatedAt = TestData.Time.GetUtcNow(), Values = new(), Email = "a@b.de", StepRuns = svc.CreateRuns(form) };

        await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.Equal(StepRunStatus.Failed, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Ok, s.Run("s2").Status);     // nicht blockiert
        Assert.Equal(1, contact.Calls);
    }

    [Fact]
    public async Task Definition_kann_den_Standard_ueberschreiben()
    {
        var notify = new FakeStep("teams.notify", critical: false, StepResult.Failed("Webhook 500"));
        var contact = new FakeStep("brevo.contact", critical: true, StepResult.Ok);
        var svc = new SubmissionPipelineService(new ISubmissionStep[] { notify, contact }, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        var form = TestData.Contact() with { Pipeline = new[] { new StepDefinition("s1", "teams.notify", "always", TestData.Json("{}"), Critical: true), new StepDefinition("s2", "brevo.contact", "always", TestData.Json("{}")) } };
        var s = new Submission { Id = "kontakt:1", Slug = "kontakt", Version = 1, CreatedAt = TestData.Time.GetUtcNow(), Values = new(), Email = "a@b.de", StepRuns = svc.CreateRuns(form) };

        await svc.RunAsync(s, form, 1, RunMode.Inline);

        Assert.Equal(StepRunStatus.Failed, s.Run("s1").Status);
        Assert.Equal(StepRunStatus.Blocked, s.Run("s2").Status);
        Assert.Equal(0, contact.Calls);
    }
}

public class PayloadTemplateTests
{
    [Fact]
    public void Ersetzt_Platzhalter_in_verschachteltem_JSON()
    {
        var ctx = new StepContext
        {
            Submission = new Submission
            {
                Id = "kontakt:42", Slug = "kontakt", Version = 1, CreatedAt = TestData.Time.GetUtcNow(),
                Values = new() { ["name"] = "Andre" }, Locale = "de",
            },
            Form = TestData.Contact(),
            FormVersion = 1,
            Options = TestData.Options(),
        };
        var tpl = TestData.Json("""{"title":"Neu: {{form}}","who":"{{field:name}}","link":"{{adminUrl}}","keep":"{{unknown}}","nested":{"id":"{{submissionId}}"}}""");

        var result = (Dictionary<string, object?>)PayloadTemplate.Render(tpl, ctx)!;

        Assert.Equal("Neu: Kontakt", result["title"]);
        Assert.Equal("Andre", result["who"]);
        Assert.Equal("https://example.org/admin/submissions/kontakt%3A42", result["link"]);
        Assert.Equal("{{unknown}}", result["keep"]);            // Unbekanntes bleibt sichtbar stehen
        Assert.Equal("kontakt:42", ((Dictionary<string, object?>)result["nested"]!)["id"]);
    }
}

public class LocaleCompletenessTests
{
    [Fact]
    public void Publish_Check_meldet_fehlende_Sprachfassungen()
    {
        var steps = new ISubmissionStep[] { new NotifyMailStep(null!) };
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        var check = new PublishCheckService(pipeline);
        var def = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Intro = new LText(new Dictionary<string, string> { ["de"] = "nur deutsch" }),
        };

        var issues = check.Check(def);

        Assert.Contains(issues, i => i.Contains("Sprache 'en'") && i.Contains("intro"));
        Assert.DoesNotContain(issues, i => i.Contains("Sprache 'de'"));   // einfache Strings gelten für alle Sprachen
    }
}
