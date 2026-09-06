using System.Text.RegularExpressions;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #2, the criteria that live only in <c>Pages/Consent.razor</c>: what a search renders (AC 1),
/// the sentence a fruitless search has to produce (AC 2), the re-query that makes "it disappears from
/// the search" the server's answer rather than a local list edit (AC 3), and the confirmation in front
/// of an irreversible deletion (AC 4). None of them has a runtime surface - nothing renders a component
/// here - so the markup is what this reads, in the manner of <see cref="SubmissionListRowTests"/>.
///
/// Every assertion here is scoped to the block it is about. Two whole-file <c>Contains</c> calls cannot
/// say that the sentence sits in the branch that needs it: the review killed an earlier version of the
/// AC 2 guard by moving the sentence into the Idle branch and folding NoConsent into the table, which
/// is precisely the wordless empty list AC 2 rules out, and both substrings still existed somewhere.
/// </summary>
public class ConsentPageGuardTests
{
    private static string Markup() => AdminMarkup.Read("Pages", "Consent.razor");

    /// <summary>
    /// The block that opens at <paramref name="anchor"/>, brace-matched. String literals are skipped:
    /// a German UI string carrying an unbalanced brace would otherwise run the scan past the closing
    /// brace to the end of the file, and every assertion made on the result would silently widen to
    /// the whole page while still passing.
    /// </summary>
    private static string Block(string anchor)
    {
        var markup = Markup();
        var start = markup.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Consent.razor no longer has \"{anchor}\" - update this guard.");
        var open = markup.IndexOf('{', start);
        Assert.True(open > start, $"\"{anchor}\" is not followed by a block - update this guard.");

        var depth = 0;
        var quoted = false;
        for (var i = open; i < markup.Length; i++)
        {
            if (markup[i] == '"' && (i == 0 || markup[i - 1] != '\\')) quoted = !quoted;
            else if (quoted) continue;
            else if (markup[i] == '{') depth++;
            else if (markup[i] == '}' && --depth == 0) return markup[open..(i + 1)];
        }
        Assert.Fail($"the block at \"{anchor}\" is not closed - update this guard.");
        return "";
    }

    private static string DeleteHandler() => Block("Task DeleteProof");

    // ---- AC 4: never without an explicit confirmation ---------------------------------------------

    [Fact]
    public void GivenAProofIsAboutToBeDeleted_WhenTheHandlerRuns_ThenTheAdminIsAskedToConfirmFirst() =>
        Assert.Contains("Js.InvokeAsync<bool>(\"confirm\"", DeleteHandler(), StringComparison.Ordinal);

    /// <summary>
    /// The polarity, not just the presence. Dropping the <c>!</c> makes the page delete exactly when the
    /// admin clicks Cancel - the literal inversion of AC 4 - and a guard that only checks that a confirm
    /// call and a return both appear, in that order, passes for it. The review found this by mutation.
    /// </summary>
    [Fact]
    public void GivenTheAdminDeclinesTheConfirmation_WhenTheHandlerRuns_ThenItStopsBeforeDeletingAnything()
    {
        var body = DeleteHandler();

        Assert.Contains("if (!await Js.InvokeAsync<bool>(\"confirm\"", body, StringComparison.Ordinal);

        // And the guarded statement is a bare return, not something that falls through into the delete.
        // "return" as a bare substring would also be satisfied by the word inside a comment.
        var confirm = body.IndexOf("Js.InvokeAsync<bool>(\"confirm\"", StringComparison.Ordinal);
        var declined = Regex.Match(body[confirm..], @"\)\)\)\s*return\s*;");
        var deleted = body.IndexOf("Api.DeleteConsentProofAsync", StringComparison.Ordinal);
        Assert.True(declined.Success, "the declined branch no longer returns outright - update this guard.");
        Assert.True(deleted > confirm, "the deletion does not happen after the confirmation.");
        Assert.True(confirm + declined.Index < deleted, "the early return does not sit before the deletion.");
    }

    // ---- AC 3: the emptied search is the server's answer -------------------------------------------

    [Fact]
    public void GivenAProofWasDeleted_WhenTheListIsRefreshed_ThenThePageAsksTheApiAgainInsteadOfEditingItsOwnList()
    {
        var body = DeleteHandler();

        // Dropping the row locally satisfies "it disappears from the search" while hiding a failed delete.
        Assert.DoesNotContain(".Remove(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".RemoveAll(", body, StringComparison.Ordinal);

        // And it re-queries the address the proof belonged to. The input box is bound on oninput, so a
        // refresh that read it would report about whatever the operator has since started typing - which
        // is the hazard ConsentSearchResult.Address exists to prevent.
        Assert.Contains("Search(proof.Email)", body, StringComparison.Ordinal);
    }

    /// <summary>The news that an erasure failed is the one thing the refresh must not swallow.</summary>
    [Fact]
    public void GivenTheDeletionFailed_WhenTheListIsRefreshed_ThenTheFailureIsStillReported()
    {
        var body = DeleteHandler();
        var refresh = body.IndexOf("Search(proof.Email)", StringComparison.Ordinal);
        Assert.True(refresh >= 0, "the delete handler no longer refreshes - the AC 3 guard above says why.");

        // Search() clears the banners, so an _error assigned before it is wiped unseen.
        Assert.True(body.IndexOf("_error =", refresh, StringComparison.Ordinal) > refresh,
            "the delete handler does not restore its error after the refresh clears it.");
    }

    /// <summary>
    /// And neither swallows the other. Restoring the delete's outcome outright discards an error the
    /// refresh itself just set - the same defect mirrored, which is how it got past the first fix: the
    /// erasure succeeds, the reload fails, and the page reports a clean success over a blank result.
    /// </summary>
    [Fact]
    public void GivenTheRefreshFailedAfterASuccessfulDeletion_WhenTheOutcomeIsShown_ThenTheRefreshErrorSurvives()
    {
        var body = DeleteHandler();

        Assert.Contains("_error = failed ?? _error;", body, StringComparison.Ordinal);
        Assert.Contains("_info = _error is not null ? null", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC 3 again, on the quiet path: a stale id deletes nothing, and the endpoint says so. Discarding
    /// the answer leaves the operator reading "deleted" over a record that is still there.
    /// </summary>
    [Fact]
    public void GivenTheProofWasAlreadyGone_WhenTheDeletionReturns_ThenTheOperatorIsToldNothingWasRemoved()
    {
        var body = DeleteHandler();

        Assert.Contains("removed = await Api.DeleteConsentProofAsync", body, StringComparison.Ordinal);
        Assert.Contains("Dieser Nachweis war bereits gelöscht.", body, StringComparison.Ordinal);
        Assert.Contains("Nachweis gelöscht.", body, StringComparison.Ordinal);
    }

    // ---- AC 2: a fruitless search says so ---------------------------------------------------------

    [Fact]
    public void GivenASearchThatFoundNothing_WhenThePageRenders_ThenItSaysNoConsentIsOnFileForThatAddress()
    {
        // Scoped to the branch, not to the file: the sentence has to be what the NoConsent state renders.
        var branch = Block("ConsentSearchState.NoConsent)");

        // Interpolated, not a fixed sentence - the operator may already be typing the next address by
        // the time this renders, so the answer has to name the address it is actually about.
        Assert.Contains("T.F(\"Zu {0} liegt keine Einwilligung vor.\"", branch, StringComparison.Ordinal);
        Assert.Contains("_result.Address", branch, StringComparison.Ordinal);
    }

    // ---- AC 1: the evidence is on the screen ------------------------------------------------------

    /// <summary>
    /// AC 1 enumerates what a proof has to show. A page that fetched every field and rendered only the
    /// form slug satisfies every other test in this suite, so the row itself needs a guard.
    /// </summary>
    [Fact]
    public void GivenProofsWereFound_WhenTheyAreRendered_ThenEachRowShowsFormVersionBothTimesAndTheWording()
    {
        var found = Block("ConsentSearchState.Found)");

        foreach (var member in new[] { "proof.Slug", "proof.Version", "proof.SubmittedAt", "proof.ConfirmedAt", "proof.ConsentText" })
            Assert.Contains(member, found, StringComparison.Ordinal);
    }
}
