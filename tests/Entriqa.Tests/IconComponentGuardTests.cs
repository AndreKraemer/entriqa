using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The icon component renders, and nothing renders a component in this test host, so its markup is
/// read rather than executed - the pattern <c>StepEditorLocalizationGuardTests</c> and
/// <c>EditorChangedBindingTests</c> use. #18: it takes a name and a size (AC 1), draws the glyph so
/// that colour and size follow the surrounding text (AC 5), and pulls the glyph from the bundled
/// library, never from the network (AC 3). Weaker than executing it; the acceptance run looks at the
/// rendered screen. Comments are stripped first, so the component's own prose cannot satisfy a guard.
/// </summary>
public class IconComponentGuardTests
{
    private static string Icon() => AdminMarkup.Read("Components", "Icon.razor");

    // AC 1 and AC 5 together: the parameters exist, and the drawn <svg> inherits colour (currentColor)
    // and size (1em, unless Size overrides it) from its surroundings, with the glyph coming from the library.
    [Fact]
    public void GivenTheIconComponent_WhenReadingHowItDrawsAGlyph_ThenItTakesNameAndSizeAndInheritsColourAndSizeFromTheText()
    {
        var icon = Icon();

        Assert.Contains("Parameter", icon, StringComparison.Ordinal);
        Assert.Contains("public string Name", icon, StringComparison.Ordinal);
        Assert.Contains("Size", icon, StringComparison.Ordinal);

        Assert.Contains("<svg", icon, StringComparison.Ordinal);
        Assert.Contains("currentColor", icon, StringComparison.Ordinal);
        Assert.Contains("1em", icon, StringComparison.Ordinal);
        Assert.Contains("IconLibrary.Path(", icon, StringComparison.Ordinal);
    }

    // AC 3: the glyph is inline. No <img>, no src=, no http(s) reference anywhere the icon is drawn or stored.
    // Absence-asserting - green against the skeleton; the mutation that must make it fail is a URL added to
    // IconLibrary.Path's data or an <img src> in the component (recorded on the plan comment).
    [Fact]
    public void GivenTheIconComponentAndLibrary_WhenReadingWhereGlyphsComeFrom_ThenNothingIsFetchedFromAnExternalSource()
    {
        var component = Icon();
        var library = File.ReadAllText(Path.Combine(AdminMarkup.Directory(), "Services", "IconLibrary.cs"));

        foreach (var source in new[] { component, library })
        {
            Assert.DoesNotContain("<img", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("src=", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
