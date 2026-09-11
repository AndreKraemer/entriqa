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
    private const string Me = "kim@admin.example";
    private const string Colleague = "robin@admin.example";
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
        Assert.Matches(new Regex(@"await\s+\w+\.ExecuteAsync\(\s*principal\.UserName\(req\)\s*,\s*ct\s*\)"), InboxEndpoint());
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

    private static string FunctionsDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var functions = Path.Combine(dir.FullName, "src", "Hosts", "Entriqa.Functions");
            if (Directory.Exists(functions)) return functions;
        }
        throw new DirectoryNotFoundException($"Entriqa.Functions not found above {AppContext.BaseDirectory}");
    }

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

    /// <summary>Everything the detail panel renders for a submission that tracks handling.</summary>
    private static string HandlingBlock()
    {
        var markup = AdminMarkup.Read("Components", "SubmissionDetailPanel.razor");
        var start = markup.IndexOf("_s.Handling != \"none\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the detail panel no longer guards the handling controls - update this guard.");
        var end = markup.IndexOf("<h2>", start, StringComparison.Ordinal);
        Assert.True(end > start, "the handling block is no longer followed by the values - update this guard.");
        return markup[start..end];
    }

    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsRows_ThenTheAssigneeIsOneOfTheColumns()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        Assert.Contains("Bearbeiter", markup, StringComparison.Ordinal);
        Assert.Contains("Assignee", RowBlock(markup), StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheSubmissionsList_WhenInspectingItsFilters_ThenItOffersThePersonalOne()
    {
        var markup = AdminMarkup.Read("Pages", "Submissions.razor");
        Assert.Contains("Meine offenen", markup, StringComparison.Ordinal);
    }

    private static string RowBlock(string markup)
    {
        var start = markup.IndexOf("<tr class=\"rowlink\"", StringComparison.Ordinal);
        Assert.True(start > 0, "the submissions list no longer has a clickable row - update this guard.");
        var end = markup.IndexOf("</tr>", start, StringComparison.Ordinal);
        Assert.True(end > start, "the clickable row is not closed - update this guard.");
        return markup[start..end];
    }

    // ---- The avatar the assignee is shown as ------------------------------------------------------

    [Theory]
    [InlineData("Martina Weiß", "MW")]
    [InlineData("kim.lorenz@example.org", "KL")]
    [InlineData("admin", "AD")]
    [InlineData(null, "?")]
    public void GivenAnAssigneesName_WhenItIsAbbreviatedForTheAvatar_ThenTheInitialsReadAsThatPerson(string? name, string expected) =>
        Assert.Equal(expected, Labels.Initials(name));
}
