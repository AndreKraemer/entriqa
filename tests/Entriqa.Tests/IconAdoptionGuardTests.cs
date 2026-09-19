using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// #18 converts today's icon-shaped spots: the bare <c>✕</c> text character on the editors' remove
/// buttons (which sizes and aligns like a letter, not a glyph) and the Forms list's row actions. This
/// guards that conversion - each spot draws the <c>&lt;Icon&gt;</c> component, and the raw <c>✕</c>
/// is gone from the buttons that carried it. Read, not executed, like the other markup guards; the
/// keyboard and screen-reader walk over the Forms list is the acceptance anchor.
/// </summary>
public class IconAdoptionGuardTests
{
    [Theory]
    [InlineData("Components/FieldEditor.razor")]
    [InlineData("Components/QuizEditor.razor")]
    [InlineData("Components/StepEditor.razor")]
    [InlineData("Components/DirectorySelect.razor")]
    [InlineData("Pages/Forms.razor")]
    public void GivenTodaysIconSpots_WhenReadingTheirMarkup_ThenEachDrawsTheIconComponent(string relativePath)
    {
        var source = AdminMarkup.Read(relativePath.Split('/'));

        Assert.Contains("<Icon", source, StringComparison.Ordinal);
    }

    // The remove buttons used a bare ✕ text character. Absence-asserting, but red now rather than
    // vacuous: the character is in these files today, so the guard fails until the conversion lands.
    [Theory]
    [InlineData("Components/FieldEditor.razor")]
    [InlineData("Components/QuizEditor.razor")]
    [InlineData("Components/StepEditor.razor")]
    [InlineData("Components/DirectorySelect.razor")]
    public void GivenAnEditorRemoveButton_WhenReadingItsMarkup_ThenTheBareTimesCharacterIsGone(string relativePath)
    {
        var source = AdminMarkup.Read(relativePath.Split('/'));

        Assert.DoesNotContain("✕", source, StringComparison.Ordinal);
    }
}
