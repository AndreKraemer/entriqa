using System.Text.RegularExpressions;
using Entriqa.Admin.Services;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The icon registry is a plain class, so unlike the component it executes here. #18: it must hand
/// back inline path data (AC 1), fail on an unknown name rather than render nothing (so a typo is
/// caught), and ship exactly the glyphs the admin references - no unused paths in the WASM download,
/// no missing ones (AC 2).
/// </summary>
public class IconLibraryTests
{
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline);
    private static readonly Regex IconName = new(@"<Icon\b[^>]*?\bName=""([^""]+)""", RegexOptions.Singleline);

    [Fact]
    public void GivenAGlyphTheAdminShips_WhenAskedForItsMarkup_ThenItReturnsInlineSvgPathData()
    {
        var markup = IconLibrary.Path("x");

        Assert.Contains("<path", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenANameNoGlyphShipsUnder_WhenAskedForItsMarkup_ThenItFailsRatherThanRenderNothing()
    {
        Assert.ThrowsAny<Exception>(() => IconLibrary.Path("no-such-icon"));
    }

    // AC 2: the shipped set and the referenced set are the same set - both directions. An unused glyph
    // is dead weight in the download; a referenced glyph the library lacks renders empty at runtime.
    [Fact]
    public void GivenTheShippedGlyphs_WhenComparedToWhatTheAdminReferences_ThenEveryShippedGlyphIsUsedAndEveryUsedGlyphShips()
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(AdminMarkup.Directory(), "*.razor", SearchOption.AllDirectories))
        {
            var source = RazorComment.Replace(File.ReadAllText(file), "");
            foreach (Match m in IconName.Matches(source)) used.Add(m.Groups[1].Value);
        }

        var shipped = new SortedSet<string>(IconLibrary.Names(), StringComparer.Ordinal);

        Assert.Equal(used, shipped);
    }
}
