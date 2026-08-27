using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The admin's German UI strings are at once the translation keys (<c>T["Speichern"]</c>), and the
/// English table is a dictionary built with indexer initialisers. A duplicate key there produces
/// no compiler diagnostic and no runtime exception - the later entry simply wins and one German
/// string silently loses its English translation. That happened: ["Fertig"] carried both "Done"
/// and "Finished", so the dialog buttons read "Finished" in English.
///
/// Scanned from source rather than loaded, following the precedent in EditorChangedBindingTests:
/// Entriqa.Admin is a Blazor WASM project and does not load in this test host.
/// </summary>
public class AdminTranslationKeyTests
{
    // Only the key half: requiring the value literal and its trailing comma on the same line
    // silently skipped every wrapped entry - 21 of 366 in Ui.cs, all of them long strings whose
    // translation sits on the following line. A duplicate among those stayed invisible.
    private static readonly Regex Entry = new(@"^\s*\[""(?<key>(?:[^""\\]|\\.)*)""\]\s*=",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The English table only - a second indexer-initialised dictionary in the same file
    /// would otherwise contribute phantom duplicates.</summary>
    private static string EnglishTable()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin", "Services", "Ui.cs");
            if (!File.Exists(file)) continue;
            var text = File.ReadAllText(file);
            var start = text.IndexOf("En = new()", StringComparison.Ordinal);
            Assert.True(start >= 0, "the En dictionary was not found in Ui.cs - did it move or get renamed?");
            var end = text.IndexOf("};", start, StringComparison.Ordinal);
            Assert.True(end > start, "the En dictionary is not terminated");
            return text[start..end];
        }
        throw new FileNotFoundException($"Ui.cs not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void GivenTheEnglishTranslationTable_WhenCollectingItsKeys_ThenNoKeyIsDefinedTwice()
    {
        var keys = Entry.Matches(EnglishTable()).Select(m => m.Groups["key"].Value).ToList();
        Assert.NotEmpty(keys);
        var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => $"\"{g.Key}\" ×{g.Count()}").ToList();
        Assert.True(duplicates.Count == 0,
            "duplicate translation keys - the later entry silently wins: " + string.Join(", ", duplicates));
    }
}
