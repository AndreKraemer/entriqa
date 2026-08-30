using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// AC 4 and AC 5 of #3 live in a Razor component, which cannot be rendered in this test host - unlike the
/// admin's plain model classes, which <c>BuilderModelLocalizationTests</c> executes directly. That is a
/// reason to read the component's source, not to leave it unguarded; see the pattern in
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
        var branch = Regex.Match(StepEditor(), @"@if \(prop\.LocalizableItems && QuizResults\.Count.*?</table>", RegexOptions.Singleline);
        Assert.True(branch.Success, "StepEditor.razor no longer has the per-result templates table - update this guard.");

        Assert.Contains("foreach (var loc in Locales)", branch.Value, StringComparison.Ordinal);
        Assert.Contains("TemplateFor(prop.Name, result.Id, l)", branch.Value, StringComparison.Ordinal);
        Assert.Contains("SetTemplateFor(prop.Name, result.Id, l, v)", branch.Value, StringComparison.Ordinal);
    }

    // Moving the round trip onto StepModel left the argument wiring behind, and that wiring is where the
    // language dimension actually enters the model - the model tests pass a locale themselves, so they
    // cannot see a component that always passes the first one. Notify() is guarded here for the same
    // reason: SetMemberFor writes ConfigText directly, so unlike the old Set(...) it does not report the
    // change itself, and EditorChangedBindingTests only checks that call sites bind Changed=.
    [Fact]
    public void GivenTheTemplatePickersDelegation_WhenReadingIt_ThenItForwardsTheLanguageAndReportsTheChange()
    {
        var source = StepEditor();

        // Both tails matter. The row's language decides which value is read and written; the language
        // *list* decides which languages exist at all, so pinning one and not the other leaves the table
        // reading empty for every row but the first, with the whole suite green.
        var read = Regex.Match(source, @"private string TemplateFor\(.*?;", RegexOptions.Singleline);
        Assert.True(read.Success, "StepEditor.razor no longer has a TemplateFor method - update this guard.");
        Assert.Contains("locale: locale, Locales)", read.Value, StringComparison.Ordinal);

        var write = Regex.Match(source, @"private void SetTemplateFor\(.*?\n    \}", RegexOptions.Singleline);
        Assert.True(write.Success, "StepEditor.razor no longer has a SetTemplateFor method - update this guard.");
        Assert.Contains("locale: locale", write.Value, StringComparison.Ordinal);
        Assert.Contains("value: template, Locales)", write.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Both per-language writers changed shape in #3: they write the model directly instead of going
    /// through the old <c>Set(name, value)</c>, so each has to report the change itself or the preview
    /// goes stale while the editor looks fine. <c>EditorChangedBindingTests</c> only checks that call
    /// sites bind <c>Changed=</c>, never that a handler invokes it.
    /// </summary>
    [Fact]
    public void GivenAPerLanguageWriter_WhenReadingIt_ThenItReportsTheChange()
    {
        // Discovery plus an expected set, the pairing GivenTheStepCatalog_… uses over the step catalog:
        // discovery sees a writer added later, the set sees one renamed or removed - including a locale
        // parameter spelled differently, which discovery alone cannot notice because it matches on that
        // very name. Both directions fail closed.
        // The body alternation is load-bearing: an expression-bodied writer has no "\n    }" of its own,
        // so a lazy terminator runs into the next method and the writer borrows *its* Notify(). That made
        // the guard positional - it caught the same insertion in one place in the file and missed it in
        // another.
        var writers = Regex.Matches(StepEditor(),
            @"private void (\w+)\([^)]*\bstring locale\b[^)]*\)\s*(?:=>.*?;|\{.*?\n    \})", RegexOptions.Singleline);

        Assert.Equal(new[] { "Set", "SetTemplateFor" },
            writers.Cast<Match>().Select(m => m.Groups[1].Value).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var writer in writers.Cast<Match>())
            // The lookahead sits at the start of the line, not after \s*: written the other way round the
            // regex backtracks over the indentation and matches a commented-out call anyway.
            Assert.Matches(new Regex(@"^(?!\s*//)[^\r\n]*\bNotify\(\);", RegexOptions.Multiline), writer.Value);
    }

    // AC 5 for the map whose members are localizable: with no quiz results there is nothing to put in the
    // table, and the old fall-through handed the operator the raw {"legacy":{"de":…}} object in a textarea.
    [Fact]
    public void GivenAMapOfLocalizableMembersAndNoQuizResults_WhenReadingTheEditor_ThenItOffersAHintNotRawJson()
    {
        var branch = Regex.Match(StepEditor(), @"else if \(prop\.LocalizableItems\)\s*\{.*?\n                \}", RegexOptions.Singleline);
        Assert.True(branch.Success, "StepEditor.razor no longer has an 'else if (prop.LocalizableItems)' branch - update this guard.");

        Assert.DoesNotContain("textarea", branch.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("Control(", branch.Value, StringComparison.Ordinal);
    }

    // The steps are loaded against the language list of the moment, so the one call that keeps them in
    // step with it is invisible from every executing test - FormEditor is a component. BuilderModelLocalizationTests
    // proves AdoptLocales does the right thing; this proves it is actually reached.
    [Fact]
    public void GivenTheFormEditor_WhenALanguageIsToggled_ThenTheStepsFollowTheNewList()
    {
        var source = File.ReadAllText(Path.Combine(AdminDirectory(), "Pages", "FormEditor.razor"));
        var toggle = Regex.Match(source, @"private void ToggleLocale\(.*?\n    \}", RegexOptions.Singleline);
        Assert.True(toggle.Success, "FormEditor.razor no longer has a ToggleLocale method - update this guard.");

        // The lookahead sits at the start of the line, not after \s*: written the other way round the
        // regex backtracks over the indentation and matches a commented-out call anyway.
        Assert.Matches(new Regex(@"^(?!\s*//)[^\r\n]*\bstep\.AdoptLocales\(", RegexOptions.Multiline), toggle.Value);
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
