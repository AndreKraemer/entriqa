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
    private static readonly Regex Entry = new(@"^\s*\[""(?<key>(?:[^""\\]|\\.)*)""\]\s*=\s*""(?<value>(?:[^""\\]|\\.)*)"",",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static string UiSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin", "Services", "Ui.cs");
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException($"Ui.cs not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void GivenTheEnglishTranslationTable_WhenCollectingItsKeys_ThenNoKeyIsDefinedTwice()
    {
        var keys = Entry.Matches(UiSource()).Select(m => m.Groups["key"].Value).ToList();
        Assert.NotEmpty(keys);
        var duplicates = keys.GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => $"\"{g.Key}\" ×{g.Count()}").ToList();
        Assert.True(duplicates.Count == 0,
            "duplicate translation keys - the later entry silently wins: " + string.Join(", ", duplicates));
    }
}
