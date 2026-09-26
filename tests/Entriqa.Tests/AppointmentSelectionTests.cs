using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Entriqa.Admin.Services;
using Entriqa.Application;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The appointment field of #7: a form offers its active, not yet begun appointments; the server accepts
/// nothing else, whatever the browser posts; the submission keeps the appointment id and a label frozen in
/// the visitor's time zone.
/// </summary>
public class AppointmentSelectionTests
{
    // The submit harness's clock: the token has to age, so it is local rather than TestData.Time.
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    // The acceptance scenario of the issue: three appointments, one past, one deactivated - only one is offered.
    private static readonly Appointment Past = new() { Slug = "webinar", Id = "past", Start = Now.AddDays(-1), Capacity = 20 };
    private static readonly Appointment Deactivated = new() { Slug = "webinar", Id = "deactivated", Start = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), Capacity = 20, Active = false };
    private static readonly Appointment Future = new()
    {
        Slug = "webinar", Id = "future", Capacity = 20, Title = "Grundlagen",
        Start = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero), End = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero),
    };
    // Begun a minute before the submission, still running for an hour: the end must not keep it on offer (AC 2).
    private static readonly Appointment Started = new() { Slug = "webinar", Id = "started", Start = Now.AddMinutes(-1), End = Now.AddHours(1), Capacity = 20 };

    // ---- AC 2 / AC 3: which appointment is on offer ---------------------------------------------

    public static TheoryData<int, int?, bool, bool> OfferCases => new()
    {
        // start (minutes from now), end (minutes from now), active, offered
        { 60, null, true, true },
        { 60, 120, true, true },
        { 0, null, true, false },          // begins right now - already begun
        { -60, 60, true, false },          // still running: a set end does not keep it visible
        { 60, null, false, false },        // deactivated
    };

    [Theory]
    [MemberData(nameof(OfferCases))]
    public void GivenAnAppointment_WhenAskingWhetherItIsOffered_ThenOnlyActiveOnesThatHaveNotBegunAre(int startIn, int? endIn, bool active, bool offered)
    {
        var appointment = new Appointment
        {
            Slug = "webinar", Id = "a", Capacity = 1, Active = active,
            Start = Now.AddMinutes(startIn), End = endIn is { } e ? Now.AddMinutes(e) : null,
        };

        Assert.Equal(offered, appointment.IsOfferedAt(Now));
    }

    // ---- AC 1: the form offers them ----------------------------------------------------------------

    [Fact]
    public async Task GivenPastDeactivatedAndFutureAppointments_WhenLoadingThePublishedForm_ThenOnlyTheFutureOneIsOffered()
    {
        var getPublished = Substitute.For<ITryGetPublishedFormQuery>();
        getPublished.ExecuteAsync("webinar", Arg.Any<CancellationToken>()).Returns(new FormVersion("webinar", 3, Registration(), Now, "test"));
        var useCase = new GetPublishedFormUseCase(getPublished, TestData.Appointments(new FakeTimeProvider(Now), Past, Deactivated, Future));

        var view = await useCase.ExecuteAsync("webinar", "de");

        var field = view.Fields.Single(f => f.Type == FieldTypes.Appointment);
        var option = Assert.Single(field.Appointments!);
        Assert.Equal(new PublicAppointmentOption("future", Future.Start, Future.End, "Grundlagen"), option);
    }

    [Fact]
    public async Task GivenADraftWithAnAppointmentField_WhenLoadingTheDraftView_ThenItOffersTheSameAppointments()
    {
        var getDraft = Substitute.For<ITryGetFormDraftQuery>();
        getDraft.ExecuteAsync("webinar", Arg.Any<CancellationToken>()).Returns(new Entriqa.Application.Ports.FormDraft("webinar", "draft", 3, Now, "test", Registration()));
        var useCase = new GetDraftFormViewUseCase(getDraft, TestData.Appointments(new FakeTimeProvider(Now), Past, Deactivated, Future));

        var view = await useCase.ExecuteAsync("webinar", "de");

        Assert.Equal(["future"], view.Fields.Single(f => f.Type == FieldTypes.Appointment).Appointments!.Select(a => a.Id));
    }

    // A 304 on a tag that only knows the version would keep a stale list alive in every browser that asked once.
    [Fact]
    public void GivenTheSameVersionWithADifferentOffer_WhenComputingTheCacheTag_ThenTheTagDiffers()
    {
        var def = Registration();

        var before = PublicFormView.From(def, 3, "de", [Future]).CacheTag();
        var after = PublicFormView.From(def, 3, "de", [Future, Future with { Id = "later", Start = Future.Start.AddDays(7) }]).CacheTag();

        Assert.NotEqual(before, after);
    }

    // Functions is not loaded by the test host, so the endpoint is read. Mutation to re-run after
    // implementing: restore the version-only tag ($"\"v{view.Version}-{view.Lang}\"").
    [Fact]
    public void GivenTheGetFormEndpoint_WhenReadingTheSource_ThenItsETagIsTheViewsCacheTag()
    {
        var source = File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions"), "Public", "PublicFunctions.cs"));
        // Up to the next function, not Block(): the route template "forms/{slug}" holds the first brace.
        var body = SourceText.Between(source, "public async Task<IActionResult> GetForm", "[Function(", "PublicFunctions no longer has GetForm - update this guard.");

        Assert.Matches(new Regex(@"^[^\S\r\n]*var etag = view\.CacheTag\(\);", RegexOptions.Multiline), body);
    }

    // ---- AC 3 / AC 6 / AC 8: the server accepts exactly one offered appointment ----------------------

    public static TheoryData<string> NotOffered => new() { "past", "started", "deactivated", "unknown" };

    [Theory]
    [MemberData(nameof(NotOffered))]
    public async Task GivenAnAppointmentIdThatIsNotOffered_WhenSubmitting_ThenTheSubmissionIsRejected(string id)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            SubmitAsync(Values(id), rows: [Past, Started, Deactivated, Future]));

        Assert.Contains(ex.Errors, e => e.Field == "termin");
    }

    [Fact]
    public async Task GivenNoAppointmentIsLeft_WhenSubmitting_ThenTheSubmissionIsRejected()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            SubmitAsync(Values("past"), rows: [Past, Deactivated]));

        Assert.Contains(ex.Errors, e => e.Field == "termin");
    }

    // AC 8: a registration always names one appointment - even when the field was saved as optional.
    [Fact]
    public async Task GivenAnOptionalAppointmentFieldLeftEmpty_WhenSubmitting_ThenTheSubmissionIsRejected()
    {
        var optional = new FieldDefinition("termin", FieldTypes.Appointment, "Termin", Required: false);
        var values = Values("future");
        values.Remove("termin");

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            SubmitAsync(values, form: Registration(optional), rows: [Future]));

        Assert.Contains(ex.Errors, e => e.Field == "termin");
    }

    // ---- AC 4 / AC 5: what the submission carries ------------------------------------------------

    [Fact]
    public async Task GivenAnOfferedAppointment_WhenSubmitting_ThenTheSubmissionCarriesItsIdAndTheLabelInTheVisitorsZone()
    {
        var stored = await SubmitAsync(Values("future"), timeZone: "America/New_York", rows: [Past, Future]);

        const string label = "Di., 13.10.2026, 04:00–06:00 (America/New_York) · Grundlagen";
        Assert.Equal(new AppointmentSnapshot("future", Future.Start, Future.End, "Grundlagen", "America/New_York", label), stored.Appointment);
        Assert.Equal(label, stored.Values["termin"]);
    }

    [Fact]
    public async Task GivenNoTimeZoneFromTheBrowser_WhenSubmitting_ThenTheLabelUsesTheConfiguredZone()
    {
        var options = TestData.Options();
        options.DefaultTimeZone = "Asia/Tokyo";

        var stored = await SubmitAsync(Values("future"), timeZone: null, options: options, rows: [Future]);

        Assert.Equal("Di., 13.10.2026, 17:00–19:00 (Asia/Tokyo) · Grundlagen", stored.Values["termin"]);
    }

    // AC 5: the snapshot has to reach the table, or the label is gone the moment the appointment is.
    // Entriqa.Data has no InternalsVisibleTo, so the mapper is read (precedent: AppointmentStorageGuardTests).
    // Mutations to re-run after implementing: drop either line, or write "Appointment = null".
    [Fact]
    public void GivenTheSubmissionMapper_WhenReadingBothDirections_ThenTheAppointmentSnapshotTravels()
    {
        var source = File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), "Mapping", "SubmissionMapper.cs"));
        var toEntity = SourceText.Block(source, "public static SubmissionEntity ToEntity", "SubmissionMapper no longer has ToEntity - update this guard.");
        var toDomain = SourceText.Between(source, "public static Submission ToDomain", "};", "SubmissionMapper no longer has ToDomain - update this guard.");

        Assert.Matches(new Regex(@"^[^\S\r\n]*AppointmentJson = .*\bs\.Appointment\b", RegexOptions.Multiline), toEntity);
        Assert.Matches(new Regex(@"^[^\S\r\n]*Appointment = .*\be\.AppointmentJson\b.*Deserialize<AppointmentSnapshot>", RegexOptions.Multiline), toDomain);
    }

    // ---- the label -------------------------------------------------------------------------------

    public static TheoryData<string, string, bool, bool, string> LabelCases => new()
    {
        // zone, lang, with end, with title, expected
        { "Europe/Berlin", "de", true, true, "Di., 13.10.2026, 10:00–12:00 (Europe/Berlin) · Grundlagen" },
        { "America/New_York", "en", false, false, "Tue, 13 Oct 2026, 04:00 (America/New_York)" },
    };

    [Theory]
    [MemberData(nameof(LabelCases))]
    public void GivenAnAppointment_WhenFormattingItsLabel_ThenDateTimeAndZoneAreInTheVisitorsZoneAndLanguage(
        string zone, string lang, bool withEnd, bool withTitle, string expected)
    {
        var appointment = Future with { End = withEnd ? Future.End : null, Title = withTitle ? Future.Title : null };

        Assert.Equal(expected, AppointmentLabel.Format(appointment, TimeZoneInfo.FindSystemTimeZoneById(zone), lang));
    }

    [Fact]
    public void GivenAnAppointmentEndingTheNextDay_WhenFormattingItsLabel_ThenBothDatesAppear()
    {
        var appointment = Future with { End = Future.Start.AddDays(1).AddHours(2), Title = null };

        Assert.Equal("Di., 13.10.2026, 10:00 – Mi., 14.10.2026, 12:00 (Europe/Berlin)",
            AppointmentLabel.Format(appointment, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), "de"));
    }

    public static TheoryData<string?, string> ZoneCases => new()
    {
        { "America/New_York", "America/New_York" },
        { "Mars/Olympus_Mons", "Europe/Berlin" },
        { null, "Europe/Berlin" },
    };

    [Theory]
    [MemberData(nameof(ZoneCases))]
    public void GivenAZoneFromTheBrowser_WhenResolvingIt_ThenAKnownZoneIsKeptAndAnythingElseFallsBack(string? requested, string expected)
    {
        Assert.Equal(expected, AppointmentLabel.ResolveZone(requested, "Europe/Berlin").Id);
    }

    // ---- AC 7 / AC 8: publish checks ---------------------------------------------------------------

    [Fact]
    public void GivenTwoAppointmentFields_WhenCheckingBeforePublish_ThenTheFormIsRejected()
    {
        var form = Registration() with
        {
            Fields = [.. Registration().Fields, new FieldDefinition("termin2", FieldTypes.Appointment, "Zweiter Termin", Required: true)],
        };

        Assert.Contains(PublishCheck().Check(form), i => i.Contains("Terminfeld", StringComparison.Ordinal));
    }

    // AC 8: a field that can be hidden could let a registration through without an appointment.
    [Fact]
    public void GivenAConditionalAppointmentField_WhenCheckingBeforePublish_ThenTheFormIsRejected()
    {
        var conditional = new FieldDefinition("termin", FieldTypes.Appointment, "Termin", Required: true, VisibleIf: new VisibleIfDefinition("name"));

        Assert.Contains(PublishCheck().Check(Registration(conditional)), i => i.Contains("Terminfeld", StringComparison.Ordinal));
    }

    // ---- the builder can add one -------------------------------------------------------------------

    [Fact]
    public void GivenTheFormBuilder_WhenListingItsFieldTypes_ThenEveryDomainFieldTypeCanBeAdded()
    {
        Assert.Empty(FieldTypes.All.Except(FieldModel.Types.Select(t => t.Value)));
    }

    // ---- harness ---------------------------------------------------------------------------------

    private static FormDefinition Registration(FieldDefinition? appointmentField = null) => TestData.Contact() with
    {
        Slug = "webinar",
        Fields = [.. TestData.Contact().Fields, appointmentField ?? new FieldDefinition("termin", FieldTypes.Appointment, "Termin", Required: true)],
    };

    private static Dictionary<string, string> Values(string appointmentId) => new()
    {
        ["name"] = "Eva Muster", ["email"] = "eva@example.org", ["msg"] = "Bis bald", ["consent"] = "on", ["termin"] = appointmentId,
    };

    /// <summary>Submits through the real use case and returns what reached the store (copied from ConsentProofTests).</summary>
    private static async Task<Submission> SubmitAsync(Dictionary<string, string> values, string? timeZone = "Europe/Berlin",
        FormDefinition? form = null, EntriqaOptions? options = null, Appointment[]? rows = null)
    {
        form ??= Registration();
        var clock = new FakeTimeProvider(Now);
        var opts = Options.Create(options ?? TestData.Options());

        var getPublished = Substitute.For<ITryGetPublishedFormQuery>();
        getPublished.ExecuteAsync(form.Slug, Arg.Any<CancellationToken>()).Returns(new FormVersion(form.Slug, 3, form, Now, "test"));
        var nonce = Substitute.For<ITryConsumeNonceCommand>();
        nonce.ExecuteAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var rateLimit = Substitute.For<IRegisterRateLimitHitCommand>();
        rateLimit.ExecuteAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(1);
        Submission? stored = null;
        var store = Substitute.For<IStoreSubmissionCommand>();
        store.ExecuteAsync(Arg.Do<Submission>(s => stored = s), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var tokens = new FormTokenService(opts, clock);
        var pipeline = new SubmissionPipelineService([new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
            opts, clock, NullLogger<SubmissionPipelineService>.Instance);
        var (proofs, _) = TestData.ConsentProofs(clock, options);
        var useCase = new SubmitFormUseCase(getPublished, store, Substitute.For<ISaveSubmissionCommand>(), nonce, rateLimit,
            Substitute.For<IStoreArtifactPort>(), tokens, new IpHasher(opts), pipeline, proofs,
            TestData.Appointments(clock, rows ?? []), opts, clock, NullLogger<SubmitFormUseCase>.Instance);

        var token = tokens.Issue(FormTokenService.KindForm, form.Slug);
        clock.Advance(TimeSpan.FromSeconds(10));
        await useCase.ExecuteAsync(new SubmitFormRequest(form.Slug, token, values, null, null, "203.0.113.9", "de", timeZone));
        return stored ?? throw new InvalidOperationException("Nothing reached the store.");
    }

    private static PublishCheckService PublishCheck() => new(new SubmissionPipelineService(
        [new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
        Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance));
}
