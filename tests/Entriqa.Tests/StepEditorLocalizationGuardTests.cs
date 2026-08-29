using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// AC 4 and AC 5 of #3 live entirely in a Razor component, which does not load in this test host. That
/// is a reason to read its source, not a reason to leave it unguarded - see the pattern in
/// <c>LocaleSourceGuardTests</c> and <c>EditorChangedBindingTests</c>. Both defects here are invisible
/// from the outside: the step editor still renders, the operator just gets one control where the form
/// declares two languages, or a raw JSON textarea where AC 5 forbids one.
/// <para>
/// Weaker than executing the component and no replacement for the acceptance run, which is what actually
/// looks at the rendered screen. It catches the regression, not the defect.
/// </para>
/// </summary>
public class StepEditorLocalizationGuardTests
{
    private static string StepEditor() => File.ReadAllText(Path.Combine(AdminDirectory(), "Components", "StepEditor.razor"));

    // AC 4: a localizable field is rendered once per declared language. Pulling the branch out first
    // matters - the words "Localizable" and "Locales" appear in comments elsewhere in the file.
    [Fact]
    public void GivenALocalizableStepField_WhenReadingHowTheEditorRendersIt_ThenItLoopsOverTheDeclaredLanguages()
    {
        var branch = Regex.Match(StepEditor(), @"else if \(prop\.Localizable\)\s*\{.*?\n                \}", RegexOptions.Singleline);
        Assert.True(branch.Success, "StepEditor.razor no longer has an 'else if (prop.Localizable)' branch - update this guard.");

        Assert.Contains("foreach (var loc in Locales)", branch.Value, StringComparison.Ordinal);
        Assert.Contains("Get(prop.Name, l)", branch.Value, StringComparison.Ordinal);       // reads that language
        Assert.Contains("Set(prop.Name, l, v)", branch.Value, StringComparison.Ordinal);    // and writes it back
    }

    // AC 4, the other half: the languages have to reach the component at all. Anchored on the tag, so
    // dropping the attribute fails here rather than silently leaving every step on the default "de".
    [Fact]
    public void GivenTheFormEditor_WhenReadingHowItRendersAStep_ThenItPassesTheDeclaredLanguages()
    {
        var source = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "FormEditor.razor"));
        var tag = Regex.Match(source, @"<StepEditor\b.*?/>", RegexOptions.Singleline);
        Assert.True(tag.Success, "FormEditor.razor no longer renders <StepEditor> - update this guard.");

        Assert.Contains("Locales=", tag.Value, StringComparison.Ordinal);
    }

    // AC 5: the locale object is storage, never input. The textarea branch is the one place raw JSON is
    // typed, and it is reached by prop.Type == "object" - so it must sit behind the shared Control
    // fragment, which the localizable path never enters with a locale object in hand.
    [Fact]
    public void GivenTheEditorsRawJsonTextarea_WhenReadingWhereItLives_ThenNoLocalizableFieldCanReachIt()
    {
        var source = StepEditor();
        var textareas = Regex.Matches(source, @"<textarea[^>]*class=""mono""", RegexOptions.Singleline);
        Assert.True(textareas.Count > 0, "StepEditor.razor no longer has a raw-JSON textarea - update this guard.");

        // Exactly one, and it is inside Control(...), which renders a single value. A second one added to
        // the localizable branch would be the regression this guards.
        Assert.Single(textareas);
        var control = Regex.Match(source, @"private RenderFragment Control\(.*?\n    \};", RegexOptions.Singleline);
        Assert.True(control.Success, "StepEditor.razor no longer has a Control fragment - update this guard.");
        Assert.Contains(@"class=""mono""", control.Value, StringComparison.Ordinal);
    }

    // The per-result PDF map keeps its own table, so it needs its own language loop - the generic
    // localizable branch never sees it.
    [Fact]
    public void GivenTheQuizPdfTemplateTable_WhenReadingIt_ThenEachResultOffersATemplatePerLanguage()
    {
        var branch = Regex.Match(StepEditor(), @"@if \(prop\.Name == ""templates"".*?</table>", RegexOptions.Singleline);
        Assert.True(branch.Success, "StepEditor.razor no longer has the per-result templates table - update this guard.");

        Assert.Contains("foreach (var loc in Locales)", branch.Value, StringComparison.Ordinal);
        Assert.Contains("TemplateFor(result.Id, l)", branch.Value, StringComparison.Ordinal);
        Assert.Contains("SetTemplateFor(result.Id, l, v)", branch.Value, StringComparison.Ordinal);
    }

    private static string AdminDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var admin = Path.Combine(dir.FullName, "src", "Ui", "Entriqa.Admin");
            if (Directory.Exists(admin)) return admin;
        }
        throw new DirectoryNotFoundException($"src/Ui/Entriqa.Admin not found above {AppContext.BaseDirectory}");
    }
}
