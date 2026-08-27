using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Invariant of the form editor: every editor component that mutates the form model reports the change
/// to the page that owns the model. Blazor only re-renders the component that owns the event handler, so
/// without a bound <c>Changed</c> the page (and with it the preview) never learns about the change.
/// The defect is invisible from the outside: nothing crashes, nothing is logged, the preview just lags.
/// That is why this test inspects the Razor sources instead of the runtime behaviour.
/// </summary>
public class EditorChangedBindingTests
{
    private static readonly Regex Declaration =
        new(@"\[Parameter\][^\r\n]*EventCallback\s+Changed\s*\{", RegexOptions.Compiled);

    [Fact]
    public void GivenComponentDeclaresChangedCallback_WhenScanningRazorUsages_ThenEveryUsageBindsIt()
    {
        var admin = AdminDirectory();
        var razor = Directory.GetFiles(admin, "*.razor", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => File.ReadAllText(f));

        var withCallback = razor
            .Where(f => Declaration.IsMatch(f.Value))
            .Select(f => Path.GetFileNameWithoutExtension(f.Key))
            .ToList();
        Assert.NotEmpty(withCallback);                       // otherwise the test would silently check nothing

        var unbound = new List<string>();
        foreach (var component in withCallback)
        {
            foreach (var (path, text) in razor)
            {
                foreach (var tag in Tags(text, component))
                {
                    if (tag.Text.Contains("Changed=")) continue;
                    var line = text.Take(tag.Start).Count(c => c == '\n') + 1;
                    unbound.Add($"{Path.GetRelativePath(admin, path)}:{line} <{component}>");
                }
            }
        }

        Assert.True(unbound.Count == 0,
            "Usages without a Changed binding - the preview never learns about changes made there:"
            + Environment.NewLine + string.Join(Environment.NewLine, unbound));
    }

    /// <summary>All element tags of a component including their attributes. The closing &gt; is searched for
    /// outside of quotes so that lambdas in attribute values (<c>OnMove="d =&gt; Move(…)"</c>) do not cut a tag short.</summary>
    private static IEnumerable<(int Start, string Text)> Tags(string text, string component)
    {
        foreach (Match m in Regex.Matches(text, $@"<{Regex.Escape(component)}(?=[\s/>])"))
        {
            var quoted = false;
            for (var i = m.Index; i < text.Length; i++)
            {
                if (text[i] == '"') quoted = !quoted;
                else if (text[i] == '>' && !quoted)
                {
                    yield return (m.Index, text[m.Index..(i + 1)]);
                    break;
                }
            }
        }
    }

    private static string AdminDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var admin = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin");
            if (Directory.Exists(admin)) return admin;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/Ui/Entriqa.Admin not found above " + AppContext.BaseDirectory + ".");
    }
}
