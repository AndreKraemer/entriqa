using Entriqa.Admin.Services;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #2, AC 2: a search that finds nothing has to say so. The trap is that "nothing searched yet"
/// and "searched, nothing found" both arrive as an empty list, and only the second one is an answer -
/// a page that renders one branch for both shows a silent empty table where the operator needs a
/// sentence. That decision lives in <see cref="ConsentSearch"/> rather than in the markup, because
/// nothing here renders a component.
/// </summary>
public class ConsentSearchTests
{
    private static readonly List<ConsentProofView> OneProof =
    [
        new("eva@example.org", "s01", "kontakt", 1, DateTimeOffset.UnixEpoch, null, "Ich willige ein.", null, null),
    ];

    [Fact]
    public void GivenNothingHasBeenSearchedYet_WhenThePageAsksWhatItIsShowing_ThenItMakesNoStatementAboutAnyAddress() =>
        Assert.Equal(ConsentSearchState.Idle, ConsentSearch.Idle().State);

    [Fact]
    public void GivenASearchThatFoundNothing_WhenTheAnswerIsClassified_ThenItStatesThatNoConsentIsOnFile()
    {
        var result = ConsentSearch.Of("nobody@example.org", []);

        Assert.Equal(ConsentSearchState.NoConsent, result.State);
        Assert.Equal("nobody@example.org", result.Address);        // the sentence has to name the address it is about
    }

    [Fact]
    public void GivenASearchThatFoundProofs_WhenTheAnswerIsClassified_ThenTheyAreCarriedThroughForDisplay()
    {
        var result = ConsentSearch.Of("eva@example.org", OneProof);

        Assert.Equal(ConsentSearchState.Found, result.State);
        Assert.Equal(OneProof, result.Proofs);
    }
}
