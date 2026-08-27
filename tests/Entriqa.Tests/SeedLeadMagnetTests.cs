using System.Text.Json;
using Entriqa.Domain.Forms;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// A shipped sample that configures a download nobody ever uploads produces a failed pipeline
/// step the first time someone tries the product - which is exactly what the acceptance run for
/// issue #28 found. These tests tie every leadmagnet.link step in seed/forms to a file that
/// actually ships in seed/leadmagnets, so the sample cannot promise a download it cannot deliver.
/// </summary>
public class SeedLeadMagnetTests
{
    private static DirectoryInfo Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "seed", "forms"))) return dir;
        throw new DirectoryNotFoundException($"seed/forms not found above {AppContext.BaseDirectory}");
    }

    private static IEnumerable<(string File, string Blob)> ConfiguredDownloads()
    {
        var repo = Repo();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(repo.FullName, "seed", "forms"), "*.json"))
        {
            var form = JsonSerializer.Deserialize<FormDefinition>(File.ReadAllText(file), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            foreach (var step in form.Pipeline.Where(s => s.Step == "leadmagnet.link"))
            {
                var blob = step.Config.TryGetProperty("blob", out var p) ? p.GetString() : null;
                yield return (Path.GetFileName(file), blob ?? "");
            }
        }
    }

    [Fact]
    public void GivenASeedFormWithADownloadStep_WhenResolvingItsBlobPath_ThenTheFileShipsInSeedLeadMagnets()
    {
        var repo = Repo();
        var missing = ConfiguredDownloads()
            .Where(d => !File.Exists(Path.Combine(repo.FullName, "seed", d.Blob.Replace('/', Path.DirectorySeparatorChar))))
            .Select(d => $"{d.File} → {d.Blob}")
            .ToList();
        Assert.True(missing.Count == 0, "configured downloads without a shipped file: " + string.Join(", ", missing));
    }

    [Fact]
    public void GivenASeedFormWithADownloadStep_WhenReadingItsBlobPath_ThenItLivesUnderTheLeadmagnetsPrefix()
    {
        // BlobArtifactAdapter takes the path verbatim, and the admin's file picker writes
        // leadmagnets/… - a sample using a different prefix would not be reproducible there.
        var wrong = ConfiguredDownloads()
            .Where(d => !d.Blob.StartsWith("leadmagnets/", StringComparison.Ordinal))
            .Select(d => $"{d.File} → {d.Blob}")
            .ToList();
        Assert.True(wrong.Count == 0, "downloads outside leadmagnets/: " + string.Join(", ", wrong));
    }

    [Fact]
    public void GivenTheShippedLeadMagnetFiles_WhenReadingThem_ThenEachIsANonEmptyPdf()
    {
        var dir = Path.Combine(Repo().FullName, "seed", "leadmagnets");
        Assert.True(Directory.Exists(dir), $"seed/leadmagnets is missing ({dir})");
        var files = Directory.GetFiles(dir);
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            Assert.True(bytes.Length > 0, $"{Path.GetFileName(f)} is empty");
            Assert.Equal("%PDF"u8.ToArray(), bytes.Take(4).ToArray());
        }
    }
}
