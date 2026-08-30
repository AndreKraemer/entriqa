using System.Text.RegularExpressions;

namespace Entriqa.Tests;

/// <summary>
/// Reading Razor sources for the guards that have no runtime surface. Two things every such guard needs,
/// and both have already been the reason one did not bite:
///
/// Razor comments are stripped, so a guard cannot be satisfied by markup that is commented out - the trap
/// the test-conventions skill names ("anchor patterns that can be commented out").
///
/// Tags are cut at a &gt; found outside quotes. An event handler in a tag is a lambda, so a scan to the
/// first &gt; stops inside <c>() =&gt; Open(…)</c> and drops every attribute after it, which lets a guard
/// pass while reading half a tag.
/// </summary>
internal static class AdminMarkup
{
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string Read(params string[] relativeToAdmin) =>
        RazorComment.Replace(File.ReadAllText(Path.Combine([Directory(), .. relativeToAdmin])), "");

    /// <summary>Every opening tag starting with <paramref name="opening"/>, attributes included.</summary>
    public static IEnumerable<string> Tags(string markup, string opening)
    {
        for (var start = markup.IndexOf(opening, StringComparison.Ordinal); start >= 0;
             start = markup.IndexOf(opening, start + 1, StringComparison.Ordinal))
        {
            var quoted = false;
            for (var i = start; i < markup.Length; i++)
            {
                if (markup[i] == '"') quoted = !quoted;
                else if (markup[i] == '>' && !quoted) { yield return markup[start..(i + 1)]; break; }
            }
        }
    }

    public static string Directory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var admin = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin");
            if (System.IO.Directory.Exists(admin)) return admin;
        }
        throw new DirectoryNotFoundException($"Entriqa.Admin not found above {AppContext.BaseDirectory}");
    }
}
