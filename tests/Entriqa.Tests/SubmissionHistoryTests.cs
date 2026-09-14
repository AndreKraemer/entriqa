using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #14: a submission records its state but not who moved it there. These drive the four operations
/// that now leave a trace - status, assignment, a repeated step, a resent confirmation mail - and read the
/// entry they write. The two halves nothing here can execute are guarded by reading source: the customs
/// post in Entriqa.Data (no integration tests, entities are internal) and Entriqa.Functions (the worker
/// SDK does not load in this test host), precedent <see cref="AssignmentStorageGuardTests"/>.
/// </summary>
public class SubmissionHistoryTests
{
    private const string Me = TestData.AdminMe;
    private const string Colleague = TestData.AdminColleague;
    private const string Id = "kontakt:0900";

    private static Submission Stored(string handling = HandlingStates.Open, string? assignee = null,
        StepRunStatus step = StepRunStatus.Failed, IEnumerable<SubmissionHistoryEntry>? history = null) => new()
    {
        Id = Id,
        Slug = "kontakt",
        Version = 1,
        CreatedAt = TestData.Time.GetUtcNow().AddHours(-1),
        Values = new Dictionary<string, string> { ["email"] = "eva@example.org" },
        Email = "eva@example.org",
        Handling = handling,
        Assignee = assignee,
        StepRuns = [new StepRun { StepId = "s1", StepKey = "notify.mail", Phase = StepPhase.OnSubmit, Status = step }],
        History = history is null ? new SubmissionHistory() : new SubmissionHistory(history),
    };

    private static (ITryGetSubmissionQuery Get, ISaveSubmissionCommand Save) Ports(Submission stored)
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        get.ExecuteAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
        return (get, save);
    }

    /// <summary>The submission as it was handed to the store - the only place an entry can be observed.</summary>
    private static IReadOnlyList<SubmissionHistoryEntry> Written(ISaveSubmissionCommand save)
    {
        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.NotNull(written);
        return written.History.Entries;
    }

    // ---- AC1: the handling status -----------------------------------------------------------------

    [Fact]
    public async Task GivenASubmissionThatTracksHandling_WhenAnAdminMarksItDone_ThenTheHistoryNamesTheTimeThemAndTheNewStatus()
    {
        var (get, save) = Ports(Stored());

        await new SetSubmissionHandlingUseCase(get, save, TestData.Time).ExecuteAsync(Id, HandlingStates.Done, Me);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.Handling, entry.Type);
        Assert.Equal(TestData.Time.GetUtcNow(), entry.At);
        Assert.Equal(Me, entry.By);
        Assert.Equal(HandlingStates.Done, entry.Detail);
    }

    // ---- AC2: the assignment ----------------------------------------------------------------------

    [Fact]
    public async Task GivenAnUnassignedSubmission_WhenAnAdminHandsItToAColleague_ThenTheHistoryNamesWhoAssignedAndToWhom()
    {
        var (get, save) = Ports(Stored());

        await new SetSubmissionAssigneeUseCase(get, save, TestData.Time).ExecuteAsync(Id, Colleague, Me);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.Assignee, entry.Type);
        Assert.Equal(Me, entry.By);
        Assert.Equal(Colleague, entry.Detail);
    }

    /// <summary>
    /// Unassigning is the same operation as assigning (#13), so it is the same entry type with an empty
    /// target - not a second type. Recording nothing here would let a submission silently lose its
    /// assignee while the history still claims it has one.
    /// </summary>
    [Fact]
    public async Task GivenAnAssignedSubmission_WhenTheAssignmentIsTakenBack_ThenTheHistoryRecordsThatItWentToNobody()
    {
        var (get, save) = Ports(Stored(assignee: Colleague));

        await new SetSubmissionAssigneeUseCase(get, save, TestData.Time).ExecuteAsync(Id, null, Me);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.Assignee, entry.Type);
        Assert.Equal(Me, entry.By);
        Assert.Null(entry.Detail);
    }

    // ---- AC3: a repeated step and a resent confirmation mail --------------------------------------

    [Fact]
    public async Task GivenAFailedStep_WhenAnAdminRepeatsIt_ThenTheHistoryNamesThemAndTheStep()
    {
        var (get, save) = Ports(Stored());

        await RetryOf(get, save).ExecuteAsync(Id, "s1", Me);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.StepRetry, entry.Type);
        Assert.Equal(Me, entry.By);
        Assert.Equal("s1", entry.Detail);
    }

    [Fact]
    public async Task GivenAnUnconfirmedSubmission_WhenAnAdminResendsTheConfirmationMail_ThenTheHistoryRecordsIt()
    {
        var (_, save, resend) = ResendOf();

        await resend.ExecuteAsync(Id, Me, CancellationToken.None);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.DoiResend, entry.Type);
        Assert.Equal(Me, entry.By);
        Assert.Equal(TestData.Time.GetUtcNow(), entry.At);
    }

    /// <summary>
    /// The mail is already out when the entry is written, so the order is deliberate: a lost entry is
    /// better than one claiming a mail that never left. Vacuously green against the skeleton, which
    /// records nothing at all. The mutation that has to make it fail: move the Record and the save in
    /// ResendDoiUseCase above the step's ExecuteAsync.
    /// </summary>
    [Fact]
    public async Task GivenAConfirmationMailThatCannotBeSent_WhenTheResendFails_ThenNothingIsRecordedAndNothingIsSaved()
    {
        var (_, save, resend) = ResendOf(sendFails: true);

        await Assert.ThrowsAnyAsync<Exception>(() => resend.ExecuteAsync(Id, Me, CancellationToken.None));

        Assert.Empty(save.ReceivedCalls());
    }

    // ---- AC4: what the system did is never attributed to a person ---------------------------------

    /// <summary>
    /// Housekeeping's auto retry is a repeated step that nobody clicked. Without this half, AC4's
    /// "marked as automatic" clause has nothing to be true of - and the closing note of the issue
    /// ("otherwise the history eventually says admin for things no human triggered") comes to pass.
    /// </summary>
    [Fact]
    public async Task GivenAFailedStep_WhenHousekeepingRepeatsItOnItsOwn_ThenTheEntryIsMarkedAutomaticAndNamesNobody()
    {
        var (uc, list, save) = HousekeepingOf();
        list.ListUnfinishedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Stored() });
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(Array.Empty<Submission>());

        await uc.ExecuteAsync();

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryTypes.StepRetry, entry.Type);
        Assert.Equal(HistoryOrigins.System, entry.Origin);
        Assert.Null(entry.By);
    }

    /// <summary>
    /// The third state the two-field actor carries, and the one the issue's own comment is about: under
    /// AllowAnonymousAdmin - and whenever the principal header fails to arrive - a human clicks and the
    /// request carries no name. That entry has no originator, but it is emphatically not automatic;
    /// collapsing the two would let a person's action read as a housekeeping run.
    /// </summary>
    [Fact]
    public async Task GivenAnAdminWhoseRequestCarriedNoIdentity_WhenTheyChangeTheStatus_ThenTheEntryIsStillNotAutomatic()
    {
        var (get, save) = Ports(Stored());

        await new SetSubmissionHandlingUseCase(get, save, TestData.Time).ExecuteAsync(Id, HandlingStates.Done, null);

        var entry = Assert.Single(Written(save));
        Assert.Equal(HistoryOrigins.Admin, entry.Origin);
        Assert.Null(entry.By);
    }

    [Fact]
    public void GivenTheSystemAsOriginator_WhenItIsAskedForAName_ThenItHasNone()
    {
        Assert.Null(HistoryActor.System.By);
        Assert.True(HistoryActor.System.IsAutomatic);
    }

    /// <summary>
    /// AC4's first half as the rule it is rather than one example of it: every operation that leaves a
    /// trace has to stamp a time and an origin, so this drives all four and reads every entry they wrote.
    /// A per-operation test would let the next operation ship without either.
    /// </summary>
    [Fact]
    public async Task GivenEveryOperationThatLeavesATrace_WhenItsEntriesAreRead_ThenEachCarriesATimeAndAnOrigin()
    {
        var entries = new List<SubmissionHistoryEntry>();

        var (g1, s1) = Ports(Stored());
        await new SetSubmissionHandlingUseCase(g1, s1, TestData.Time).ExecuteAsync(Id, HandlingStates.Done, Me);
        entries.AddRange(Written(s1));

        var (g2, s2) = Ports(Stored());
        await new SetSubmissionAssigneeUseCase(g2, s2, TestData.Time).ExecuteAsync(Id, Colleague, null);
        entries.AddRange(Written(s2));

        var (g3, s3) = Ports(Stored());
        await RetryOf(g3, s3).ExecuteAsync(Id, "s1", Me);
        entries.AddRange(Written(s3));

        var (_, s4, resend) = ResendOf();
        await resend.ExecuteAsync(Id, Me, CancellationToken.None);
        entries.AddRange(Written(s4));

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.NotEqual(default, e.At);
            Assert.Contains(e.Origin, new[] { HistoryOrigins.Admin, HistoryOrigins.System });
            Assert.False(string.IsNullOrWhiteSpace(e.Type));
        });
    }

    // ---- AC5: newest first ------------------------------------------------------------------------

    /// <summary>
    /// The order is decided where it can be tested - in the projection - and the panel renders what it
    /// receives. Storage stays in append order, which is what lets the cap drop the oldest.
    /// </summary>
    [Fact]
    public async Task GivenASubmissionWithSeveralEntries_WhenTheDetailViewIsBuilt_ThenTheNewestEntryComesFirst()
    {
        var older = Entry(TestData.Time.GetUtcNow().AddMinutes(-10), HistoryTypes.Handling, HandlingStates.Open);
        var newer = Entry(TestData.Time.GetUtcNow(), HistoryTypes.Assignee, Colleague);
        var get = Substitute.For<ITryGetSubmissionQuery>();
        get.ExecuteAsync(Id, Arg.Any<CancellationToken>()).Returns(Stored(history: [older, newer]));

        var view = await new GetSubmissionDetailUseCase(get, Substitute.For<ITryGetFormVersionQuery>()).ExecuteAsync(Id);

        Assert.Equal([newer.At, older.At], view.History.Select(e => e.At));
    }

    // ---- The ceiling an Azure Table string property puts on it ------------------------------------

    /// <summary>
    /// A string property in Table Storage ends at 32768 characters, and nothing caps how often an admin
    /// flips a status. An unbounded history would eventually make the submission unsaveable, which is a
    /// worse failure than a shortened one. The oldest entries give way wholesale - not the individual
    /// deletion AC6 forbids.
    /// </summary>
    [Fact]
    public void GivenAHistoryAtItsLimit_WhenAnotherEntryIsAppended_ThenTheOldestGivesWayAndTheNewestIsKept()
    {
        var start = TestData.Time.GetUtcNow();
        var history = new SubmissionHistory(Enumerable.Range(0, SubmissionHistory.Max)
            .Select(i => Entry(start.AddMinutes(i), HistoryTypes.Handling, HandlingStates.Open)));
        var newest = Entry(start.AddDays(1), HistoryTypes.Handling, HandlingStates.Done);

        history.Append(newest);

        Assert.Equal(SubmissionHistory.Max, history.Entries.Count);
        Assert.Equal(newest, history.Entries[^1]);
        Assert.DoesNotContain(history.Entries, e => e.At == start);
    }

    // ---- AC6: an entry is never rewritten and never removed on its own ----------------------------

    /// <summary>
    /// Vacuously green against the skeleton, which already has the shape. The mutation that has to make
    /// it fail: turn any <c>init</c> on SubmissionHistoryEntry into a <c>set</c>.
    /// </summary>
    [Fact]
    public void GivenAHistoryEntry_WhenItsMembersAreRead_ThenNoneOfThemCanBeSetAfterwards()
    {
        var properties = typeof(SubmissionHistoryEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, p => Assert.True(
            p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.Name == "IsExternalInit"),
            $"{p.Name} can be assigned after the entry exists"));
    }

    /// <summary>
    /// The other half of AC6: the collection. Vacuously green against the skeleton. Two mutations have to
    /// make it fail: add a Clear or a RemoveAt to SubmissionHistory, or declare Submission.History as a
    /// plain List of entries - the shape StepRuns has, and the one this deliberately does not.
    /// </summary>
    [Fact]
    public void GivenTheHistory_WhenItsMembersAreRead_ThenItOffersNoWayToRemoveOrReplaceOne()
    {
        var members = typeof(SubmissionHistory).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToList();

        Assert.Contains("Append", members);
        Assert.DoesNotContain(members, n => Removing.IsMatch(n));
        Assert.Equal(typeof(SubmissionHistory), typeof(Submission).GetProperty("History")!.PropertyType);
        Assert.Null(typeof(SubmissionHistory).GetProperty("Entries")!.SetMethod);
    }

    private static readonly Regex Removing =
        new("^(Remove|Clear|Insert|Replace|set_Item|Item)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// AC6 from the outside: no route may offer the edit or the single deletion. Vacuously green - the
    /// skeleton adds no route at all. The mutation: add a Function routed at
    /// manage/submissions/{id}/history/{index} with a delete or a post trigger.
    /// </summary>
    [Fact]
    public void GivenTheAdminApi_WhenItsRoutesAreRead_ThenNoneOfThemEditsOrDeletesASingleEntry() =>
        Assert.DoesNotMatch(new Regex("Route\\s*=\\s*\"[^\"]*history", RegexOptions.IgnoreCase), FunctionsSource());

    // ---- AC7: the history lives in the submission and ends with it --------------------------------

    /// <summary>
    /// The customs post, both directions. Entriqa.Data has no integration tests and its entities are
    /// internal, so nothing here can call the mapper - and with no guard at all, dropping the history
    /// from either direction leaves the whole suite green while every entry is lost on the next save.
    /// </summary>
    [Fact]
    public void GivenASubmissionWithHistory_WhenItIsStored_ThenItTravelsInTheSubmissionRowInBothDirections()
    {
        var source = DataSource("Mapping", "SubmissionMapper.cs");

        Assert.Matches(new Regex(@"^[^\S\r\n]*HistoryJson = ", RegexOptions.Multiline),
            SourceText.Block(source, "public static SubmissionEntity ToEntity",
                "SubmissionMapper no longer has ToEntity - update this guard."));
        Assert.Matches(new Regex(@"^[^\S\r\n]*History = ", RegexOptions.Multiline),
            SourceText.Block(source, "public static Submission ToDomain",
                "SubmissionMapper no longer has ToDomain - update this guard."));
    }

    /// <summary>
    /// AC7 is a storage decision, and the opposite one from #1: a consent proof gets its own partitioned
    /// table because it has to outlive the submission, a history entry must not. Vacuously green against
    /// the skeleton, which already puts HistoryJson on the submission row and nowhere else. The mutation:
    /// move or copy it onto another ITableEntity, or give it an entity of its own.
    /// </summary>
    [Fact]
    public void GivenAHistoryEntry_WhenLookingForWhereItLives_ThenNoTableOutsideTheSubmissionRowHoldsOne()
    {
        // Every property is "{ get; set; }", so a body cut at the first closing brace ends after the first
        // one and finds nothing - which read as "no table holds a history" and would have passed forever.
        var source = DataSource("Entities", "Entities.cs");
        var names = Regex.Matches(source, @"class (?<name>\w+Entity) : ITableEntity")
            .Select(m => m.Groups["name"].Value).ToList();
        Assert.NotEmpty(names);

        var carriers = names
            .Where(n => SourceText.Block(source, $"class {n} : ITableEntity", $"{n} is gone - update this guard.")
                .Contains("History", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(["SubmissionEntity"], carriers);
    }

    // ---- Who the operation is attributed to, and where that name comes from -----------------------

    /// <summary>
    /// The name has to reach the use case, or every entry is anonymous while the suite stays green: the
    /// parameter is nullable, so passing null compiles and reads as "nobody was signed in".
    /// </summary>
    [Theory]
    [InlineData("AdminSetHandling", "setHandling")]
    [InlineData("AdminSetAssignee", "setAssignee")]
    [InlineData("AdminRetryStep", "retry")]
    [InlineData("AdminResendDoi", "resendDoi")]
    public void GivenAnAdminOperationThatLeavesATrace_WhenItsEndpointIsRead_ThenItPassesTheSignedInName(
        string function, string useCase)
    {
        var body = Endpoint(function);
        Assert.Contains("principal.TryUserName(req)", body, StringComparison.Ordinal);
        Assert.Contains(useCase + ".ExecuteAsync(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The defect the issue's own comment names: UserName answers "admin" when no principal arrived, and
    /// that is indistinguishable from an account actually called admin. Attribution needs a reader that
    /// can say "nobody" - read from source, because the worker SDK does not load in this test host.
    /// </summary>
    [Fact]
    public void GivenARequestWithoutAPrincipal_WhenTheAttributionNameIsRead_ThenItIsAbsentInsteadOfTheLiteralAdmin()
    {
        var body = SourceText.Block(PrincipalReaderSource(), "public string? TryUserName",
            "SwaPrincipalReader no longer has TryUserName - update this guard.");

        Assert.Contains("return null", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"admin\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NotImplementedException", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same decision applied to the features that already record a name. Until now each of them
    /// stored the literal "admin" for an unauthenticated request - in the publish history, in the draft,
    /// and in the erasure audit row, where it is a GDPR record.
    /// </summary>
    [Theory]
    [InlineData("AdminSaveDraft")]
    [InlineData("AdminPublish")]
    [InlineData("AdminDeleteContact")]
    [InlineData("AdminDeleteConsentProof")]
    public void GivenAnAuditedAdminOperation_WhenItIsRead_ThenItAttributesThroughTheNullableReader(string function) =>
        Assert.Contains("TryUserName(req)", Endpoint(function), StringComparison.Ordinal);

    /// <summary>
    /// The boundary that keeps the change above from going one step too far: the same name is also the
    /// RowKey of the per-admin state row, where null has nowhere to go - it would throw, or split one
    /// admin's "new since" marker and their picker entry across two rows. Vacuously green. The mutation:
    /// point either of these at TryUserName.
    /// </summary>
    [Theory]
    [InlineData("AdminRecentSubmissions")]
    [InlineData("AdminMarkVisited")]
    public void GivenThePerAdminStateRow_WhenItsKeyIsRead_ThenItKeepsItsStableFallback(string function)
    {
        var body = Endpoint(function);
        Assert.Contains("principal.UserName(req)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TryUserName", body, StringComparison.Ordinal);
    }

    // ---- What the detail panel shows --------------------------------------------------------------

    /// <summary>
    /// The panel renders the order it is given (AC5) instead of sorting again - two sorts are how one of
    /// them silently becomes the wrong one. Nothing renders a component here, so this reads the markup.
    /// </summary>
    [Fact]
    public void GivenTheDetailPanel_WhenASubmissionIsOpened_ThenItRendersTheHistoryInTheOrderItReceivesIt()
    {
        var markup = AdminMarkup.Read("Components", "SubmissionDetailPanel.razor");

        Assert.Contains("@T[\"Verlauf\"]", markup, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"foreach\s*\(var\s+\w+\s+in\s+_s\.History\s*\)"), markup);
        Assert.DoesNotMatch(new Regex(@"_s\.History[\s\S]{0,40}(OrderBy|Reverse)"), markup);
    }

    /// <summary>
    /// Every submission that existed before this feature has an empty history, so the empty case is the
    /// common one on the day it ships. Hiding the section there would make the feature look absent.
    /// </summary>
    [Fact]
    public void GivenTheDetailPanel_WhenASubmissionHasNoHistoryYet_ThenTheSectionStillAppearsWithAHint() =>
        Assert.Contains("Noch keine Eintr\u00e4ge", AdminMarkup.Read("Components", "SubmissionDetailPanel.razor"),
            StringComparison.Ordinal);

    // ---- Fixtures --------------------------------------------------------------------------------

    private static SubmissionHistoryEntry Entry(DateTimeOffset at, string type, string? detail) =>
        new() { At = at, Type = type, Origin = HistoryOrigins.Admin, By = Me, Detail = detail };

    private sealed class FakeStep(string key, bool fails) : ISubmissionStep
    {
        public string Key => key;
        public string Name => key;
        public string Description => "";
        public StepMode Mode => StepMode.Inline;
        public string ConfigSchema => "{}";

        public Task<StepResult> ExecuteAsync(StepContext ctx, JsonElement config, CancellationToken ct) =>
            fails ? throw new InvalidOperationException("the mail server said no") : Task.FromResult(StepResult.Ok);
    }

    private static SubmissionPipelineService Pipeline(string key = "notify.mail", bool sendFails = false) =>
        new([new FakeStep(key, sendFails)], Options.Create(TestData.Options()), TestData.Time,
            NullLogger<SubmissionPipelineService>.Instance);

    private static ITryGetFormVersionQuery Version(string stepKey)
    {
        var form = TestData.Contact() with { Pipeline = [new StepDefinition("s1", stepKey, "always", TestData.Json("{}"))] };
        var getVersion = Substitute.For<ITryGetFormVersionQuery>();
        getVersion.ExecuteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new FormVersion("kontakt", 1, form, TestData.Time.GetUtcNow(), "test"));
        return getVersion;
    }

    private static RetryStepUseCase RetryOf(ITryGetSubmissionQuery get, ISaveSubmissionCommand save) =>
        new(get, Version("notify.mail"), save, Pipeline());

    private static (ITryGetSubmissionQuery Get, ISaveSubmissionCommand Save, IResendDoiUseCase Resend) ResendOf(bool sendFails = false)
    {
        var (get, save) = Ports(Stored(step: StepRunStatus.Waiting));
        var resend = new ResendDoiUseCase(get, Version("doi.request"), Pipeline("doi.request", sendFails),
            save, TestData.Time, Options.Create(TestData.Options()));
        return (get, save, resend);
    }

    private static (RunHousekeepingUseCase Uc, IListHousekeepingSubmissionsQuery List, ISaveSubmissionCommand Save) HousekeepingOf()
    {
        var list = Substitute.For<IListHousekeepingSubmissionsQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        var purge = Substitute.For<IPurgeSecurityEntriesCommand>();
        purge.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns((0, 0));
        var listArtifacts = Substitute.For<IListArtifactsPort>();
        listArtifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());
        var uc = new RunHousekeepingUseCase(list, Version("notify.mail"), save,
            Substitute.For<IDeleteSubmissionCommand>(), purge, Substitute.For<IRecordHousekeepingRunCommand>(),
            Substitute.For<IStoreArtifactPort>(), listArtifacts, Pipeline(), TestData.ConsentProofs().Service,
            Options.Create(TestData.Options()), TestData.Time, NullLogger<RunHousekeepingUseCase>.Instance);
        return (uc, list, save);
    }

    private static string FunctionsSource() =>
        File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions"), "Admin", "AdminFunctions.cs"));

    private static string PrincipalReaderSource() =>
        File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions"), "Http", "SwaPrincipalReader.cs"));

    /// <summary>One Function, from its attribute to the next one.</summary>
    private static string Endpoint(string name)
    {
        var source = FunctionsSource();
        var start = source.IndexOf($"[Function(\"{name}\")]", StringComparison.Ordinal);
        Assert.True(start > 0, $"AdminFunctions no longer has {name} - update this guard.");
        var end = source.IndexOf("[Function(", start + 10, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }

    private static string DataSource(params string[] relativeToData) =>
        File.ReadAllText(Path.Combine([SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), .. relativeToData]));
}
