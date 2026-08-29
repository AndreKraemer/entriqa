using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Entriqa.Application;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Entriqa.Admin.Services;

namespace Entriqa.Tests;

internal static class TestData
{
    public static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

    public static EntriqaOptions Options() => new()
    {
        TokenSecret = "unit-test-secret-with-at-least-32-characters!!",
        BaseUrl = "https://example.org",
        SiteName = "Test",
    };

    public static FormTokenService Tokens(FakeTimeProvider? time = null) => new(Microsoft.Extensions.Options.Options.Create(Options()), time ?? Time);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// A submissions list the way the admin receives it (#11): newest first, two forms, every quick
    /// filter non-empty, and the "kontakt" selection long enough to span two pages of 15.
    /// </summary>
    public static List<SubmissionListItem> SubmissionList() =>
        Enumerable.Range(0, 24).Select(i => new SubmissionListItem(
            Id: $"s{i:00}",
            Slug: i < 18 ? "kontakt" : "whitepaper",
            Version: 1,
            CreatedAt: Time.GetUtcNow().AddHours(-i),
            Email: $"p{i:00}@example.org",
            Summary: $"Person {i:00}",
            State: i % 4,
            Handling: i < 20 ? "open" : i < 22 ? "done" : "none",
            QuizResultId: null)).ToList();

    public static FormDefinition Contact() => new(
        Slug: "kontakt", Name: "Kontakt", Type: "contact", Intro: null, SubmitLabel: null,
        Fields: new[]
        {
            new FieldDefinition("name", FieldTypes.Text, "Name", Required: true),
            new FieldDefinition("email", FieldTypes.Email, "E-Mail", Required: true),
            new FieldDefinition("topic", FieldTypes.Select, "Thema", Options: new LText[] { "A", "B" }),
            new FieldDefinition("msg", FieldTypes.Textarea, "Nachricht", Required: true, MaxLength: 50),
            new FieldDefinition("consent", FieldTypes.Consent, "Einwilligung", Required: true, Text: "Ich stimme zu."),
            new FieldDefinition("src", FieldTypes.Hidden, "Quelle", Source: "utm_source"),
        },
        Pipeline: new[] { new StepDefinition("s1", "notify.mail", "always", Json("""{"to":"a@b.de","templateId":1}""")) },
        Quiz: null,
        Completion: new CompletionDefinition("message", "Danke"),
        Handling: true);

    public static QuizDefinition Quiz() => new("sum", "optional",
        Questions: new[]
        {
            new QuizQuestion("q1", "Frage 1", new[] { new QuizOption("a", "A", 0, Next: "q1b"), new QuizOption("b", "B", 1, Next: "q2"), new QuizOption("c", "C", 2, Next: "q2") }),
            new QuizQuestion("q1b", "Frage 1b", new[] { new QuizOption("a", "Nein", 0, Next: "result:legacy"), new QuizOption("b", "Bald", 1), new QuizOption("c", "Läuft", 2) }),
            new QuizQuestion("q2", "Frage 2", new[] { new QuizOption("a", "A", 0), new QuizOption("b", "B", 2) }),
        },
        Results: new[]
        {
            new QuizResult("legacy", 0, 39, "Legacy", "…"),
            new QuizResult("mitte", 40, 74, "Mitte", "…"),
            new QuizResult("modern", 75, 100, "Modern", "…"),
        });
}
