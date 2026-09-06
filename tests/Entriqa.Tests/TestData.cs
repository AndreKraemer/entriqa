using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Entriqa.Application;
using Entriqa.Application.Security;
using Entriqa.Domain.Forms;
using Entriqa.Admin.Services;
using Entriqa.Application.Consent;
using Entriqa.Application.Ports;
using Entriqa.Domain.Consent;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

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
    /// A <see cref="ConsentProofService"/> over substituted ports (#1). The doubles come back with it -
    /// the proof is only ever observable through them, never through a table.
    /// </summary>
    public static (ConsentProofService Service, ConsentProofPorts Ports) ConsentProofs(
        FakeTimeProvider? time = null, EntriqaOptions? options = null)
    {
        var ports = new ConsentProofPorts(
            Substitute.For<IStoreConsentProofCommand>(),
            Substitute.For<IConfirmConsentProofCommand>(),
            Substitute.For<IDeleteConsentProofsByEmailCommand>(),
            Substitute.For<IRecordConsentDeletionCommand>(),
            Substitute.For<IListExpiredConsentProofsQuery>(),
            Substitute.For<IDeleteConsentProofCommand>(),
            Substitute.For<IListConsentProofsByEmailQuery>());
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? Options());
        ports.ListExpired.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConsentProof>());
        var service = new ConsentProofService(ports.Store, ports.Confirm, ports.DeleteByEmail, ports.RecordDeletion,
            ports.ListExpired, ports.Delete, ports.ListByEmail, new IpHasher(opts), opts, time ?? Time,
            NullLogger<ConsentProofService>.Instance);
        return (service, ports);
    }

    /// <summary>
    /// Two proofs for one address (#2): different forms and versions, one confirmed by double opt-in and
    /// one not, and different wording - so a test that reads only the first row cannot pass by accident.
    /// Deliberately handed back oldest-first: the newest-first order is the listing's job, not the store's.
    /// </summary>
    public static List<ConsentProof> ConsentProofsFor(string email) =>
    [
        new()
        {
            Email = email, SubmissionId = "s01", Slug = "kontakt", Version = 1,
            SubmittedAt = Time.GetUtcNow().AddDays(-9),
            ConsentText = "Ich willige ein, zum Zweck der Kontaktaufnahme kontaktiert zu werden.",
            IpHash = "hash-submit-01",
        },
        new()
        {
            Email = email, SubmissionId = "s02", Slug = "whitepaper", Version = 4,
            SubmittedAt = Time.GetUtcNow().AddDays(-2),
            ConsentText = "Ja, schickt mir das Whitepaper und gelegentlich Neuigkeiten.",
            IpHash = "hash-submit-02",
            ConfirmedAt = Time.GetUtcNow().AddDays(-2).AddMinutes(6),
            ConfirmedIpHash = "hash-confirm-02",
        },
    ];

    internal sealed record ConsentProofPorts(
        IStoreConsentProofCommand Store,
        IConfirmConsentProofCommand Confirm,
        IDeleteConsentProofsByEmailCommand DeleteByEmail,
        IRecordConsentDeletionCommand RecordDeletion,
        IListExpiredConsentProofsQuery ListExpired,
        IDeleteConsentProofCommand Delete,
        IListConsentProofsByEmailQuery ListByEmail);

    /// <summary>
    /// A submissions list the way the admin receives it (#11): newest first, two forms, every quick
    /// filter non-empty, and the "kontakt" selection long enough to span two pages of 15 - with and
    /// without the "todo" filter, which has to remove something or the tests that walk it walk
    /// an unfiltered list under a name that claims otherwise.
    ///
    /// Named for the layer: Entriqa.Admin and Entriqa.Domain both have a SubmissionListItem, and this
    /// is the admin's.
    /// </summary>
    public static List<SubmissionListItem> AdminSubmissionList() =>
        Enumerable.Range(0, 24).Select(i => new SubmissionListItem(
            Id: $"s{i:00}",
            Slug: i < 18 ? "kontakt" : "whitepaper",
            Version: 1,
            CreatedAt: Time.GetUtcNow().AddHours(-i),
            Email: $"p{i:00}@example.org",
            Summary: $"Person {i:00}",
            State: i % 4,
            Handling: i >= 18 ? "none" : i is 5 or 11 ? "done" : "open",
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
