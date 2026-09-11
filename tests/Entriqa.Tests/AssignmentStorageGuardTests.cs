using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The half of #13 that lives in Entriqa.Data, where nothing this project runs can reach it: the layer
/// talks to Table Storage, there are no integration tests, and the assembly has no InternalsVisibleTo,
/// so the mapper and the commands cannot even be called from here. That is exactly the blind spot the
/// test-conventions skill warns about - #3 hid a round-trip defect from four source guards - and it is
/// not hypothetical here: with no guard at all, setting every Assignee assignment in the mapper to null
/// left the whole suite green while assigning, the list column and both personal filters were dead in
/// production. So these read the source (precedent: ConsentProofTests, LocaleSourceGuardTests).
///
/// Weaker than executing the code, and no replacement for the acceptance gate. It catches the
/// regression, not the defect.
/// </summary>
public class AssignmentStorageGuardTests
{
    /// <summary>
    /// Every direction of the customs post. Anchored at the start of a line so that commenting an
    /// assignment out fails too, and read inside the respective member so that a mention anywhere else
    /// in the file cannot satisfy it.
    /// </summary>
    [Fact]
    public void GivenTheSubmissionMapper_WhenReadingIt_ThenTheAssigneeTravelsInEveryDirection()
    {
        var source = Source("Mapping", "SubmissionMapper.cs");

        Assert.Matches(Assignment("s"), SourceText.Block(source, "public static SubmissionEntity ToEntity",
            "SubmissionMapper no longer has ToEntity - update this guard."));
        Assert.Matches(Assignment("e"), SourceText.Block(source, "public static Submission ToDomain",
            "SubmissionMapper no longer has ToDomain - update this guard."));
        Assert.Matches(new Regex(@"^[^\S\r\n]*[^/\r\n]*e\.Assignee", RegexOptions.Multiline),
            SourceText.Block(source, "public static SubmissionListItem ToListItem",
                "SubmissionMapper no longer has ToListItem - update this guard."));
    }

    private static Regex Assignment(string from) =>
        new($@"^[^\S\r\n]*Assignee = {from}\.Assignee\b", RegexOptions.Multiline);

    /// <summary>
    /// Two paths write the same admin row, each owning one column: the last visit drives the blue "new
    /// since" dot, the sighting drives the assignment picker. A replace on either silently erases the
    /// other - and the mutation is not exotic, it is the shape of the housekeeping command three lines
    /// further down, which legitimately replaces its own row in a different partition.
    /// </summary>
    [Theory]
    [InlineData("class SetLastVisitCommand", "LastVisitAt", "SeenAt")]
    [InlineData("class RecordAdminSeenCommand", "SeenAt", "LastVisitAt")]
    public void GivenAWriterOfTheAdminStateRow_WhenReadingIt_ThenItMergesItsOwnColumnOnly(string writer, string own, string other)
    {
        var body = SourceText.Block(Source("Commands", "AdminStateCommands.cs"), writer,
            $"AdminStateCommands no longer has {writer} - update this guard.");

        Assert.Contains("TableUpdateMode.Merge", body, StringComparison.Ordinal);
        Assert.DoesNotContain("TableUpdateMode.Replace", body, StringComparison.Ordinal);

        // Merging is only half of it. A merge that names the other column overwrites it just as surely
        // as a replace would - and for the sighting, which runs on every page load, that would clear the
        // last visit and put out the blue "new since" dot on every row, permanently.
        Assert.Contains($"[\"{own}\"]", body, StringComparison.Ordinal);
        Assert.DoesNotContain($"[\"{other}\"]", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC2's actual selection rule. The use case above it only passes the query through, so without this
    /// the partition the choices come from is unguarded - pointing it at another one leaves every test
    /// green and offers the admin a list of housekeeping rows.
    /// </summary>
    [Fact]
    public void GivenTheAdminListQuery_WhenReadingIt_ThenItOffersTheAdminsAndNothingElse()
    {
        var body = SourceText.Block(Source("Queries", "AdminOverviewQueries.cs"), "class ListAdminsQuery",
            "AdminOverviewQueries no longer has ListAdminsQuery - update this guard.");

        Assert.Contains("PartitionKey == \"state\"", body, StringComparison.Ordinal);
    }

    private static string Source(params string[] relativeToData) =>
        File.ReadAllText(Path.Combine([SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), .. relativeToData]));
}
