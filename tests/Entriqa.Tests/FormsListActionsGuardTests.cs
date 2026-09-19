using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// #19 tidies the forms list, the admin's home page: the row's three text buttons collapse into a
/// split button (primary "Bearbeiten" plus a chevron menu holding "Einsendungen" and "Einbetten"), and
/// creating a form moves from an inline block below the pager into a header button that opens a dialog.
///
/// Nothing renders a Razor component in this test host, so this reads the markup and the stylesheet the
/// way <c>IconAdoptionGuardTests</c> and <c>SubmissionAddressReactionTests</c> do. It can prove the
/// pieces exist and that the tokens this story removes are gone; it cannot prove the menu opens, closes
/// on Escape or outside-click, stays keyboard-reachable, or that rows keep one height on a narrow
/// window. Those are AC 2, 5, 6 and 8 - acceptance-only, driven at the running admin. Comments are
/// stripped first, so the page's own prose cannot satisfy a guard.
/// </summary>
public class FormsListActionsGuardTests
{
    private static string Forms() => AdminMarkup.Read("Pages", "Forms.razor");

    private static string Css() =>
        File.ReadAllText(Path.Combine(AdminMarkup.Directory(), "wwwroot", "css", "app.css"));

    // AC 4: the secondary actions leave the row and move into a dedicated overlay menu, opened by a
    // chevron toggle. Red now - neither the chevron glyph nor the menu exists on the row today.
    [Fact]
    public void GivenARowsActions_WhenReadingThem_ThenTheyAreASplitButtonWithAChevronMenu()
    {
        var forms = Forms();

        Assert.Contains("chevron-down", forms, StringComparison.Ordinal);
        Assert.Contains("row-menu", forms, StringComparison.Ordinal);
    }

    // AC 7: creating a form is a dialog, not a block that permanently occupies the list. Red now - the
    // inline ".new-form" block is present below the pager and no create dialog state exists yet.
    [Fact]
    public void GivenTheCreateForm_WhenReadingThePage_ThenItIsADialogAndNoInlineBlockRemains()
    {
        var forms = Forms();

        Assert.DoesNotContain("new-form", forms, StringComparison.Ordinal);
        Assert.Contains("_createOpen", forms, StringComparison.Ordinal);
    }

    // AC 6 (structural proxy; the above-the-fold property itself is acceptance-only): the button that
    // opens the create dialog sits before the table, so it is reachable without scrolling past the
    // forms. Red now - the trigger does not exist, so it is found before nothing.
    [Fact]
    public void GivenTheCreateTrigger_WhenReadingThePage_ThenItSitsAboveTheTable()
    {
        var forms = Forms();

        var triggerAt = forms.IndexOf("_createOpen = true", StringComparison.Ordinal);
        var tableAt = forms.IndexOf("<table", StringComparison.Ordinal);

        Assert.True(triggerAt >= 0, "the header carries a button that opens the create dialog");
        Assert.True(triggerAt < tableAt, "the create trigger sits above the table, not below it");
    }

    // AC 1: the row's actions never wrap. Red now - the ".row-actions" rule sets flex-wrap: wrap, which
    // is exactly what makes a narrow row grow three lines tall today. Absence-asserting but red now, so
    // it bites immediately; once green, the mutation that must fail it is re-adding flex-wrap: wrap.
    [Fact]
    public void GivenTheRowActionsRule_WhenReadingTheStylesheet_ThenItDoesNotWrap()
    {
        var rule = SourceText.Block(Css(), ".row-actions", "the .row-actions rule in app.css");

        Assert.DoesNotContain("flex-wrap: wrap", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("flex-wrap:wrap", rule, StringComparison.Ordinal);
    }

    // AC 3: the split button's primary is the edit link - the main action opens the editor. Green now
    // and a regression guard through the refactor; the mutation that must fail it is changing the
    // primary away from the forms/{slug} link.
    [Fact]
    public void GivenTheRowsPrimaryAction_WhenReadingIt_ThenItOpensTheEditor()
    {
        var forms = Forms();

        Assert.Contains("href=\"forms/@f.Slug\"", forms, StringComparison.Ordinal);
    }

    // AC 4 content: the two secondary actions survive into the menu, and Einbetten stays disabled for a
    // draft. Green now and a regression guard; the mutation that must fail it is dropping either action
    // from the menu or the draft guard from Einbetten.
    [Fact]
    public void GivenTheMenu_WhenReadingItsActions_ThenItStillOffersEinsendungenAndEinbetten()
    {
        var forms = Forms();

        Assert.Contains("einsendungen/@f.Slug", forms, StringComparison.Ordinal);
        Assert.Contains("_snippetSlug = f.Slug", forms, StringComparison.Ordinal);
        Assert.Contains("f.Status != \"published\"", forms, StringComparison.Ordinal);
    }
}
