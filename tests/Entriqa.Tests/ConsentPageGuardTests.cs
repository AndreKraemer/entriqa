using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #2, the three criteria that live only in <c>Pages/Consent.razor</c>: the sentence a fruitless
/// search has to produce (AC 2), the re-query that makes "it disappears from the search" the server's
/// answer rather than a local list edit (AC 3), and the confirmation in front of an irreversible
/// deletion (AC 4). None of them has a runtime surface - nothing renders a component here - so the
/// markup is what this reads, in the manner of <see cref="SubmissionListRowTests"/>.
/// </summary>
public class ConsentPageGuardTests
{
    private static string Markup() => AdminMarkup.Read("Pages", "Consent.razor");

    /// <summary>The body of one @code member, brace-matched - a scan to the next blank line reads too little.</summary>
    private static string Body(string signature)
    {
        var markup = Markup();
        var start = markup.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Consent.razor no longer has a member matching \"{signature}\" - update this guard.");
        var open = markup.IndexOf('{', start);
        Assert.True(open > start, $"\"{signature}\" has no body - update this guard.");
        var depth = 0;
        for (var i = open; i < markup.Length; i++)
        {
            if (markup[i] == '{') depth++;
            else if (markup[i] == '}' && --depth == 0) return markup[open..(i + 1)];
        }
        Assert.Fail($"the body of \"{signature}\" is not closed - update this guard.");
        return "";
    }

    // ---- AC 4: never without an explicit confirmation ---------------------------------------------

    [Fact]
    public void GivenAProofIsAboutToBeDeleted_WhenTheHandlerRuns_ThenTheAdminIsAskedToConfirmFirst()
    {
        var body = Body("Task DeleteProof");

        // The project's pattern for an irreversible admin action (Contacts, SubmissionDetailPanel).
        Assert.Contains("Js.InvokeAsync<bool>(\"confirm\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheAdminDeclinesTheConfirmation_WhenTheHandlerRuns_ThenItStopsBeforeDeletingAnything()
    {
        var body = Body("Task DeleteProof");
        var confirm = body.IndexOf("Js.InvokeAsync<bool>(\"confirm\"", StringComparison.Ordinal);
        Assert.True(confirm >= 0, "the delete handler does not confirm at all - the AC 4 guard above says why.");

        // Asking and then deleting regardless is exactly the defect AC 4 forbids, and it reads as
        // compliant at a glance. The early return has to sit between the question and the call.
        var declined = body.IndexOf("return", confirm, StringComparison.Ordinal);
        var deleted = body.IndexOf("Api.DeleteConsentProofAsync", StringComparison.Ordinal);
        Assert.True(deleted > confirm, "the deletion does not happen after the confirmation.");
        Assert.True(declined > confirm && declined < deleted,
            "the handler asks for a confirmation but does not return when it is declined.");
    }

    // ---- AC 3: the emptied search is the server's answer -------------------------------------------

    [Fact]
    public void GivenAProofWasDeleted_WhenTheListIsRefreshed_ThenThePageAsksTheApiAgainInsteadOfEditingItsOwnList()
    {
        var body = Body("Task DeleteProof");

        // Dropping the row locally satisfies "it disappears from the search" while hiding a failed delete.
        Assert.Contains("Search()", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Remove(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".RemoveAll(", body, StringComparison.Ordinal);
    }

    // ---- AC 2: a fruitless search says so ---------------------------------------------------------

    [Fact]
    public void GivenASearchThatFoundNothing_WhenThePageRenders_ThenItSaysNoConsentIsOnFileForThatAddress()
    {
        var markup = Markup();

        // The branch has to exist at all - an empty table under a Found-only branch is the wordless
        // empty list AC 2 rules out.
        Assert.Contains("ConsentSearchState.NoConsent", markup, StringComparison.Ordinal);
        Assert.Contains("Zu dieser Adresse liegt keine Einwilligung vor.", markup, StringComparison.Ordinal);
    }
}
