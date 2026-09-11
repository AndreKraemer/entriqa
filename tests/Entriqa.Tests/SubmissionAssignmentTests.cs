using System.Reflection;
using System.Text.RegularExpressions;
using NSubstitute;
using Entriqa.Admin.Services;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Errors;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #13: the inbox knows open and done, but not who is on it. This is the write side - handing a
/// submission to an admin, handing it on, taking it back - plus the two things AC2 needs before any of
/// it is usable: that the application notices an admin has been here at all, and that those admins are
/// what the picker offers. Which submissions a personal filter then shows is decided in
/// <see cref="SubmissionSelection"/> and tested next to it.
/// </summary>
public class SubmissionAssignmentTests
{
    private const string Me = TestData.AdminMe;
    private const string Colleague = TestData.AdminColleague;
    private const string Id = "kontakt:0900";

    private static (ISetSubmissionAssigneeUseCase Assign, ITryGetSubmissionQuery Get, ISaveSubmissionCommand Save)
        Build(Submission stored)
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        get.ExecuteAsync(stored.Id, Arg.Any<CancellationToken>()).Returns(stored);
        return (new SetSubmissionAssigneeUseCase(get, save), get, save);
    }

    private static Submission Stored(string handling = HandlingStates.Open, string? assignee = null) => new()
    {
        Id = Id,
        Slug = "kontakt",
        Version = 1,
        CreatedAt = TestData.Time.GetUtcNow(),
        Values = new Dictionary<string, string> { ["email"] = "eva@example.org" },
        Email = "eva@example.org",
        Handling = handling,
        Assignee = assignee,
    };

    // ---- AC1 / AC3: a submission carries exactly one assignee ------------------------------------

    [Fact]
    public async Task GivenASubmissionThatTracksHandling_WhenAnAdminIsAssigned_ThenTheStoredSubmissionCarriesThem()
    {
        var (assign, _, save) = Build(Stored());

        await assign.ExecuteAsync(Id, Me);

        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.Equal(Me, written!.Assignee);
    }

    /// <summary>
    /// AC3 as the rule it is, not as the single example "at most one": handing a submission on has to
    /// replace the previous assignee rather than add to them. A second assignee has nowhere to live, so
    /// what this actually pins is that the hand-over is not silently ignored for an already-assigned one.
    /// </summary>
    [Fact]
    public async Task GivenASubmissionAlreadyAssigned_WhenItIsHandedToAnotherAdmin_ThenTheFirstOneIsReplaced()
    {
        var (assign, _, save) = Build(Stored(assignee: Me));

        await assign.ExecuteAsync(Id, Colleague);

        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.Equal(Colleague, written!.Assignee);
    }

    // ---- AC6: taking the assignment back ---------------------------------------------------------

    [Fact]
    public async Task GivenAnAssignedSubmission_WhenTheAssignmentIsRemoved_ThenItHasNoAssigneeAnyMore()
    {
        var (assign, _, save) = Build(Stored(assignee: Me));

        await assign.ExecuteAsync(Id, null);

        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.Null(written!.Assignee);
    }

    /// <summary>
    /// An empty string is what a &lt;select&gt; sends for its "nobody" option, and what an empty JSON body
    /// degrades to. It has to mean the same as null - otherwise a submission ends up assigned to a person
    /// whose name is "", visible nowhere and filterable by no one.
    /// </summary>
    [Fact]
    public async Task GivenAnAssignedSubmission_WhenTheAssigneeIsSetToAnEmptyName_ThenItCountsAsNobody()
    {
        var (assign, _, save) = Build(Stored(assignee: Me));

        await assign.ExecuteAsync(Id, "   ");

        var written = save.ReceivedCalls().Single().GetArguments()[0] as Submission;
        Assert.Null(written!.Assignee);
    }

    /// <summary>
    /// AC1's second half. The detail view is what the picker reads its current value from, and Assignee is
    /// a trailing optional parameter of SubmissionDetailView - so dropping it from the projection compiles,
    /// and the panel would then report "Niemand" for every assigned submission. The markup guard below
    /// cannot see that; only calling the use case can.
    /// </summary>
    [Fact]
    public async Task GivenAnAssignedSubmission_WhenTheDetailViewIsBuilt_ThenItNamesTheAssignee()
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        get.ExecuteAsync(Id, Arg.Any<CancellationToken>()).Returns(Stored(assignee: Me));

        var view = await new GetSubmissionDetailUseCase(get, Substitute.For<ITryGetFormVersionQuery>()).ExecuteAsync(Id);

        Assert.Equal(Me, view.Assignee);
    }

    // ---- The boundary: only where there is something to handle ------------------------------------

    /// <summary>
    /// Assignment stops where handling stops - the line SetSubmissionHandlingUseCase already draws. A
    /// submission of a form without a handling state can never be open, so an assignment on it could
    /// never show up in any personal filter: it would be a dead end that looks like it worked.
    /// </summary>
    [Fact]
    public async Task GivenASubmissionOfAFormWithoutHandling_WhenAnAdminIsAssigned_ThenItIsRefusedAndNothingIsStored()
    {
        var (assign, _, save) = Build(Stored(handling: HandlingStates.None));

        var ex = await Assert.ThrowsAsync<AppException>(() => assign.ExecuteAsync(Id, Me));

        Assert.Equal(ErrorMessages.HandlingUnsupported, ex.MessageKey);
        Assert.Empty(save.ReceivedCalls());
    }

    [Fact]
    public async Task GivenNoSuchSubmission_WhenAnAdminIsAssigned_ThenItIsReportedAsNotFound()
    {
        var get = Substitute.For<ITryGetSubmissionQuery>();
        var save = Substitute.For<ISaveSubmissionCommand>();
        get.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Submission?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => new SetSubmissionAssigneeUseCase(get, save).ExecuteAsync(Id, Me));
        Assert.Empty(save.ReceivedCalls());
    }

    // ---- AC7: an assignment never notifies anyone -------------------------------------------------

    /// <summary>
    /// AC7 is an absence, and an absence cannot be observed by calling the use case - a port it does not
    /// have cannot record a call. So this reads the shape instead: assigning knows the submission and how
    /// to save it, and has no collaborator that could send anything. That is the property that makes AC7
    /// true by construction rather than by care.
    ///
    /// Vacuously green against the skeleton. The mutation that has to make it fail: give
    /// SetSubmissionAssigneeUseCase an ISendTransactionalMailPort (or the pipeline service) as a
    /// constructor parameter.
    /// </summary>
    [Fact]
    public void GivenTheAssignmentUseCase_WhenItsCollaboratorsAreRead_ThenNoneOfThemCanNotifyAnyone()
    {
        var parameters = typeof(SetSubmissionAssigneeUseCase).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, name => Notifying.IsMatch(name));
    }

    private static readonly Regex Notifying =
        new("Mail|Notify|Send|Teams|Webhook|Brevo|Pipeline|Doi", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ---- AC2: who the picker can offer ------------------------------------------------------------

    /// <summary>
    /// The choices are the admins the application has seen. Today the only writer of such a row is
    /// "alles als gesehen markieren" - so a colleague who works in the admin daily and never clicks that
    /// link is not offered. This is the recording that fixes it, and it runs on the clock, not on
    /// DateTimeOffset.Now.
    /// </summary>
    [Fact]
    public async Task GivenAnAdminUsingTheInbox_WhenTheirVisitIsRecorded_ThenTheyAreStoredWithTheCurrentTime()
    {
        var record = Substitute.For<IRecordAdminSeenCommand>();

        await new RecordAdminSeenUseCase(record, TestData.Time).ExecuteAsync(Me);

        await record.Received(1).ExecuteAsync(Me, TestData.Time.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenSeveralAdminsThatHaveBeenHere_WhenTheChoicesAreListed_ThenEveryOneOfThemIsOffered()
    {
        var query = Substitute.For<IListAdminsQuery>();
        query.ExecuteAsync(Arg.Any<CancellationToken>()).Returns(new[] { Colleague, Me });

        var choices = await new ListAdminsUseCase(query).ExecuteAsync();

        Assert.Equal(new[] { Colleague, Me }, choices);
    }

    /// <summary>
    /// The recording has to sit where every admin passes, not where the assignment happens - otherwise
    /// only people who have already assigned something can be assigned something. The inbox endpoint is
    /// that place: the admin shell calls it on every page load and it already knows the caller. Nothing
    /// in the test host loads Entriqa.Functions, so this reads the source (precedent: LocaleSourceGuardTests).
    /// </summary>
    [Fact]
    public void GivenTheInboxEndpoint_WhenAnAdminLoadsIt_ThenTheirVisitIsRecorded()
    {
        // Some use case is awaited with nothing but the caller's name - which is what recording a
        // sighting looks like, and what listing the recent submissions (two more arguments) does not.
        // Anchored at the start of a line so that commenting the call out fails too: deleting it is not
        // the only way to lose it, and a bare Matches is satisfied by "// await ...".
        Assert.Matches(
            new Regex(@"^[^\S\r\n]*await\s+recordSeen\.ExecuteAsync\(\s*principal\.UserName\(req\)\s*,\s*ct\s*\)", RegexOptions.Multiline),
            InboxEndpoint());
    }

    /// <summary>
    /// The sighting is bookkeeping for the picker, not part of the answer. Before #13 the inbox served
    /// its list without writing anything; letting a failed write through would make the one page every
    /// admin starts on depend on it.
    /// </summary>
    [Fact]
    public void GivenTheInboxEndpoint_WhenTheSightingCannotBeWritten_ThenTheListIsStillServed()
    {
        var endpoint = InboxEndpoint();
        Assert.Matches(new Regex(@"try\s*\{[^}]*recordSeen", RegexOptions.Singleline), endpoint);

        // Catching and then rethrowing would satisfy a scan for try/catch while doing exactly what this
        // test forbids, so what the catch does NOT do is the half that carries the promise.
        var swallowed = SourceText.Block(endpoint, "catch (Exception ex)",
            "the sighting is no longer wrapped in a catch - update this guard.");
        Assert.DoesNotContain("throw", swallowed, StringComparison.Ordinal);
    }

    /// <summary>The AdminRecentSubmissions function, from its attribute to the next one.</summary>
    private static string InboxEndpoint()
    {
        var source = File.ReadAllText(Path.Combine(FunctionsDirectory(), "Admin", "AdminFunctions.cs"));
        var start = source.IndexOf("[Function(\"AdminRecentSubmissions\")]", StringComparison.Ordinal);
        Assert.True(start > 0, "AdminFunctions no longer has AdminRecentSubmissions - update this guard.");
        var end = source.IndexOf("[Function(", start + 10, StringComparison.Ordinal);
        Assert.True(end > start, "AdminRecentSubmissions is the last function now - update this guard.");
        return source[start..end];
    }

    private static string FunctionsDirectory() => SourceText.RepoDirectory("src", "Hosts", "Entriqa.Functions");

    // ---- AC1 / AC2 in the markup ------------------------------------------------------------------

    /// <summary>
    /// The picker sits where the handling buttons sit, inside the block that only renders for a form that
    /// tracks handling - assignment and handling share the same boundary. "Niemand" is an option of the
    /// picker, not a separate button: taking an assignment back is the same operation as giving it (AC6).
    /// </summary>
    [Fact]
    public void GivenTheDetailView_WhenASubmissionTracksHandling_ThenItOffersTheAssigneePickerIncludingNobody()
    {
        var block = HandlingBlock();
        Assert.Contains("<select", block, StringComparison.Ordinal);
        Assert.Contains("Niemand", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything the detail panel renders for a submission that tracks handling - cut at the branch's own
    /// closing brace. Cutting at the values heading instead let the picker be moved out of the branch
    /// altogether while this guard kept passing, which is precisely the property it exists to pin.
    /// </summary>
    private static string HandlingBlock() =>
        AdminMarkup.IfBody(AdminMarkup.Read("Components", "SubmissionDetailPanel.razor"), "_s.Handling != \"none\"",
            "the detail panel no longer guards the handling controls with an @if - update this guard.");

    /// <summary>
    /// The header is read inside the table head, not anywhere in the file: the filter select carries the
    /// same word as a title and "Alle Bearbeiter" contains it as well, so a whole-file scan stayed green
    /// with the column header deleted.
    /// </summary>
    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheAssigneeIsOneOfTheColumns()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        Assert.Contains("@T[\"Bearbeiter\"]", HeadBlock(markup), StringComparison.Ordinal);
        Assert.Contains("Assignee", RowBlock(markup), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsFilters_ThenItOffersThePersonalOne()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        Assert.Contains("Meine offenen", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC4's trigger. What it promises is decided in SubmissionSelection and tested by executing it, so
    /// what remains here is only that the button goes through that decision - and for whom, because a
    /// chip wired to any other name renders and counts exactly the same.
    /// </summary>
    [Fact]
    public void GivenThePersonalFilterChip_WhenItIsClicked_ThenItNavigatesThroughTheSelection()
    {
        Assert.Contains("Go(", Chip(), StringComparison.Ordinal);
        Assert.Contains("TogglePersonal(me)", Chip(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Appearance and action have to key on one condition. Lighting the chip on the assignee alone
    /// compiles, renders and leaves every other test green, while the button goes dark exactly where
    /// clicking it still leads into the filter - which is how this corner broke once already.
    /// </summary>
    [Fact]
    public void GivenThePersonalFilterChip_WhenItIsRendered_ThenItLooksOnTheConditionItActsOn() =>
        Assert.Contains("IsPersonal(me)", Chip(), StringComparison.Ordinal);

    /// <summary>
    /// The badge is code-behind that no test executes, so what keeps it from drifting back into a second
    /// copy of the filter is that it goes through the same shared expression the click does.
    /// </summary>
    [Fact]
    public void GivenThePersonalFilterChip_WhenItsBadgeIsCounted_ThenItCountsWhatTheClickLeadsTo() =>
        Assert.Contains("SubmissionSelection.Personal(", AdminMarkup.Read("Pages", "Submissions.razor"),
            StringComparison.Ordinal);

    /// <summary>
    /// Whose chip it is comes from the signed-in principal. Without that the chip never renders at all
    /// and AC4 loses its entry point, silently and with the suite green.
    /// </summary>
    [Fact]
    public void GivenThePersonalFilterChip_WhenAskingWhoItIsFor_ThenTheNameComesFromTheSignedInPrincipal() =>
        Assert.Contains("_me ??= (await Api.GetMeAsync())?.UserDetails", AdminMarkup.Read("Pages", "Submissions.razor"),
            StringComparison.Ordinal);

    /// <summary>The button that carries the personal filter, attributes included.</summary>
    private static string Chip()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        var chip = AdminMarkup.Tags(markup, "<button").FirstOrDefault(t => t.Contains("TogglePersonal", StringComparison.Ordinal));
        Assert.True(chip is not null, "no button navigates through SubmissionSelection.TogglePersonal any more - update this guard.");
        return chip!;
    }

    private static string RowBlock(string markup) => AdminMarkup.Between(markup, "<tr class=\"rowlink\"", "</tr>",
        "the submissions list no longer has a closed clickable row - update this guard.");

    private static string HeadBlock(string markup) => AdminMarkup.Between(markup, "<thead>", "</thead>",
        "the submissions list no longer has a table head - update this guard.");

    // ---- What the picker offers -------------------------------------------------------------------

    /// <summary>
    /// A select whose value matches no option does not show an empty picker - the browser falls back to
    /// the first one. So a stored assignee the admin list no longer holds would read as "Niemand" while
    /// the submission is in fact assigned, and the next change would write that back. The story accepted
    /// exactly this case ("a renamed display name leaves old assignments on the old string"), which is
    /// why the current value has to be offered whether or not it is still an admin.
    /// </summary>
    [Fact]
    public void GivenAnAssigneeTheAdminListNoLongerHolds_WhenThePickerIsBuilt_ThenItIsStillOffered() =>
        Assert.Equal([Colleague, Me], Labels.AssigneeChoices([Me], Colleague));

    [Fact]
    public void GivenAnAssigneeThatIsStillAnAdmin_WhenThePickerIsBuilt_ThenTheChoicesAreUnchanged() =>
        Assert.Equal([Colleague, Me], Labels.AssigneeChoices([Colleague, Me], Me));

    /// <summary>Nobody is an option of its own, so an unassigned submission adds nothing to the list.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GivenNoAssignee_WhenThePickerIsBuilt_ThenOnlyTheAdminsAreOffered(string? current) =>
        Assert.Equal([Me], Labels.AssigneeChoices([Me], current));

    /// <summary>The admin list may not have arrived yet - the value on the submission still has to show.</summary>
    [Fact]
    public void GivenTheAdminListHasNotLoadedYet_WhenThePickerIsBuilt_ThenTheStoredAssigneeIsTheChoice() =>
        Assert.Equal([Me], Labels.AssigneeChoices(null, Me));

    /// <summary>Both pickers have to go through it, or the one that does not keeps the defect.</summary>
    [Theory]
    [InlineData("Pages", "Submissions.razor")]
    [InlineData("Components", "SubmissionDetailPanel.razor")]
    public void GivenAnAssigneePicker_WhenReadingItsOptions_ThenTheyComeFromTheSharedChoices(string folder, string file) =>
        Assert.Contains("Labels.AssigneeChoices(", AdminMarkup.Read(folder, file), StringComparison.Ordinal);

    // ---- The avatar the assignee is shown as ------------------------------------------------------

    [Theory]
    [InlineData("Martina Weiß", "MW")]
    [InlineData("kim.lorenz@example.org", "KL")]
    [InlineData("admin", "AD")]
    [InlineData("admin@example.org", "AD")]      // unstripped this reads "AO" - the provider, not the person
    [InlineData(null, "?")]
    public void GivenAnAssigneesName_WhenItIsAbbreviatedForTheAvatar_ThenTheInitialsReadAsThatPerson(string? name, string expected) =>
        Assert.Equal(expected, Labels.Initials(name));
}
