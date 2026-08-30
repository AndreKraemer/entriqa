namespace Entriqa.Admin.Services;

/// <summary>
/// What the consent search on <c>Pages/Consent.razor</c> is currently saying (#2). It lives here rather
/// than in the markup because AC 2 turns on the difference between "nothing searched yet" and "searched,
/// nothing found" - an empty list means both, and only one of them may be shown as an answer.
/// </summary>
public enum ConsentSearchState
{
    /// <summary>No address has been searched - the page makes no statement about anyone.</summary>
    Idle,

    /// <summary>Proofs are on file for that address.</summary>
    Found,

    /// <summary>Searched, and there is demonstrably no consent on file for that address.</summary>
    NoConsent,
}

public sealed record ConsentSearchResult(ConsentSearchState State, string Address, IReadOnlyList<ConsentProofView> Proofs);

public static class ConsentSearch
{
    /// <summary>Before anything was searched.</summary>
    public static ConsentSearchResult Idle() => throw new NotImplementedException("#2");

    /// <summary>Classifies what the API answered for that address.</summary>
    public static ConsentSearchResult Of(string address, IReadOnlyList<ConsentProofView> proofs)
        => throw new NotImplementedException("#2");
}
