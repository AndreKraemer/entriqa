using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The half of #6 that lives in Entriqa.Data, where nothing this project runs can reach it: the layer talks
/// to Table Storage, there are no integration tests, and the assembly has no InternalsVisibleTo, so the
/// mapper cannot be called from here. That is the blind spot the test-conventions skill warns about
/// (precedent: <see cref="AssignmentStorageGuardTests"/>) - so this reads the source instead.
///
/// It locks AC 2: appointments are keyed by the form <em>slug</em>, with a stable id as the row key. Pointing
/// the partition at a constant would herd every form's appointments into one partition and leak them across
/// forms, exactly as the use-case tests - which never touch the real mapper - stayed green. It also checks
/// that a deactivation survives the round trip (AC 4): a ToDomain that hard-codes Active would read every
/// stored appointment back as active.
///
/// Weaker than executing the code, and no replacement for the acceptance gate; it catches the regression,
/// not the defect. Written in the red state it fails because the mapper is a throwing skeleton - proof of
/// nothing until #6 is implemented, so the named mutations below must be re-run after the code exists.
/// </summary>
public class AppointmentStorageGuardTests
{
    [Fact]
    public void GivenTheAppointmentMapper_WhenReadingToEntity_ThenTheSlugIsThePartitionAndTheIdIsTheRow()
    {
        var body = SourceText.Block(Source(), "public static AppointmentEntity ToEntity",
            "AppointmentMapper no longer has ToEntity - update this guard.");

        // Mutations to re-run after implementing: PartitionKey = "appointment" (a constant) or
        // PartitionKey = a.Id both break the per-slug partitioning; RowKey = a.Slug drops the stable id.
        Assert.Matches(LineAssign("PartitionKey", "a", "Slug"), body);
        Assert.Matches(LineAssign("RowKey", "a", "Id"), body);
    }

    [Fact]
    public void GivenTheAppointmentMapper_WhenReadingToDomain_ThenSlugIdAndActiveTravelBack()
    {
        var body = SourceText.Block(Source(), "public static Appointment ToDomain",
            "AppointmentMapper no longer has ToDomain - update this guard.");

        // Mutations to re-run: Slug = "" / Id = "" lose the identity; Active = true hides every deactivation (AC 4).
        Assert.Matches(LineAssign("Slug", "e", "PartitionKey"), body);
        Assert.Matches(LineAssign("Id", "e", "RowKey"), body);
        Assert.Matches(LineAssign("Active", "e", "Active"), body);
    }

    /// <summary>An assignment "Target = source.Member" at the start of a line, so commenting it out fails too.</summary>
    private static Regex LineAssign(string target, string source, string member) =>
        new($@"^[^\S\r\n]*{target} = {source}\.{member}\b", RegexOptions.Multiline);

    private static string Source() =>
        File.ReadAllText(Path.Combine(SourceText.RepoDirectory("src", "Core", "Entriqa.Data"), "Mapping", "AppointmentMapper.cs"));
}
