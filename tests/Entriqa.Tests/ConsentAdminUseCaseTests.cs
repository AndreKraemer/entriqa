using System.Reflection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Consent;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #2: the admin's window onto the consent proofs #1 files away. A proof nobody can find answers
/// no Art. 15 enquiry, and a revoked consent needs a way out that is narrower than erasing the contact.
/// </summary>
public class ConsentAdminUseCaseTests
{
    private const string Address = "eva@example.org";
    private const string Admin = "kim@admin.example";

    private static (IListConsentProofsUseCase List, IDeleteConsentProofUseCase Delete, TestData.ConsentProofPorts Ports) Build()
    {
        var (service, ports) = TestData.ConsentProofs();
        ports.ListByEmail.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConsentProof>());
        return (new ListConsentProofsUseCase(service), new DeleteConsentProofUseCase(service), ports);
    }

    private static void OnFile(TestData.ConsentProofPorts ports, params ConsentProof[] proofs) =>
        ports.ListByEmail.ExecuteAsync(Address, Arg.Any<CancellationToken>()).Returns(proofs);

    // ---- AC 1: the search answers with the evidence ---------------------------------------------

    [Fact]
    public async Task GivenSeveralProofsForAnAddress_WhenItIsSearched_ThenEachOneCarriesItsFormVersionTimesAndWording()
    {
        var (list, _, ports) = Build();
        var stored = TestData.ConsentProofsFor(Address);
        OnFile(ports, [.. stored]);

        var found = await list.ExecuteAsync(Address);

        // The confirmed one, read in full: form, version, both timestamps and the wording. Asserting only
        // the count would pass against a listing that drops half the record on the way out.
        var whitepaper = Assert.Single(found, p => p.SubmissionId == "s02");
        Assert.Equal("whitepaper", whitepaper.Slug);
        Assert.Equal(4, whitepaper.Version);
        Assert.Equal(stored[1].SubmittedAt, whitepaper.SubmittedAt);
        Assert.Equal(stored[1].ConfirmedAt, whitepaper.ConfirmedAt);
        Assert.Equal(stored[1].ConsentText, whitepaper.ConsentText);

        // And the unconfirmed one is not silently dropped - an unconfirmed consent is still evidence.
        var kontakt = Assert.Single(found, p => p.SubmissionId == "s01");
        Assert.Null(kontakt.ConfirmedAt);
        Assert.Equal(stored[0].ConsentText, kontakt.ConsentText);
    }

    [Fact]
    public async Task GivenProofsOfSeveralSubmissions_WhenTheyAreListed_ThenTheNewestSubmissionComesFirst()
    {
        var (list, _, ports) = Build();
        OnFile(ports, [.. TestData.ConsentProofsFor(Address)]);          // handed over oldest first

        var found = await list.ExecuteAsync(Address);

        Assert.Equal(["s02", "s01"], found.Select(p => p.SubmissionId));
    }

    [Fact]
    public async Task GivenNoProofForThatAddress_WhenItIsSearched_ThenTheAnswerIsAnEmptyResultNotAFailure()
    {
        // The reason AC 2 can be answered at all: "there is no consent on file" has to be a result the
        // page can render, not an exception it has to translate.
        var (list, _, _) = Build();

        Assert.Empty(await list.ExecuteAsync("nobody@example.org"));
    }

    // ---- AC 5: the wording is the one that was ticked -------------------------------------------

    [Fact]
    public async Task GivenTheFormsWordingChangedAfterTheSubmission_WhenTheProofIsListed_ThenTheWordingFromTheTimeOfSubmissionIsShown()
    {
        var (list, _, ports) = Build();
        const string asTicked = "Ich willige in die Fassung von 2024 ein.";
        var currentFormWording = TestData.Contact().ConsentField!.Label.ToString();
        OnFile(ports, new ConsentProof
        {
            Email = Address, SubmissionId = "s01", Slug = "kontakt", Version = 1,
            SubmittedAt = TestData.Time.GetUtcNow().AddDays(-400), ConsentText = asTicked,
        });

        var shown = Assert.Single(await list.ExecuteAsync(Address)).ConsentText;

        Assert.Equal(asTicked, shown);
        Assert.NotEqual(currentFormWording, shown);
    }

    /// <summary>
    /// The other half of AC 5, and the half a value assertion cannot reach: the listing must have no way
    /// of consulting the form at all. A form port on this constructor is how the invariant would be lost
    /// quietly - the proof would still be stored correctly and the admin would still show the wrong text.
    /// </summary>
    [Fact]
    public void GivenTheConsentProofListing_WhenItsDependenciesAreInspected_ThenItCannotReachTheFormsCurrentWording()
    {
        var parameters = typeof(ListConsentProofsUseCase).GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(typeof(ITryGetPublishedFormQuery), parameters);
        Assert.DoesNotContain(typeof(ITryGetFormVersionQuery), parameters);
    }

    // ---- AC 3: one proof out, and a record of who took it out ------------------------------------

    [Fact]
    public async Task GivenOneOfSeveralProofsForAnAddress_WhenItIsDeleted_ThenOnlyThatProofIsRemoved()
    {
        var (_, delete, ports) = Build();
        OnFile(ports, [.. TestData.ConsentProofsFor(Address)]);

        Assert.True(await delete.ExecuteAsync(Address, "s01", Admin));

        await ports.Delete.Received(1).ExecuteAsync(
            Arg.Is<ConsentProof>(p => p.SubmissionId == "s01" && p.Email == Address), Arg.Any<CancellationToken>());
        await ports.Delete.DidNotReceive().ExecuteAsync(
            Arg.Is<ConsentProof>(p => p.SubmissionId == "s02"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenASingleProofIsDeleted_WhenTheErasureIsRecorded_ThenItNamesTheTimeTheAdminAndTheProof()
    {
        var (_, delete, ports) = Build();
        OnFile(ports, [.. TestData.ConsentProofsFor(Address)]);
        var expectedHash = new IpHasher(Options.Create(TestData.Options())).Hash(ConsentProof.KeyOf(Address))!;

        await delete.ExecuteAsync(Address, "s01", Admin);

        // The submission id is what makes this row answer its question years later: against a hashed
        // address, "one proof was removed" cannot say which one.
        await ports.RecordDeletion.Received(1).ExecuteAsync(
            TestData.Time.GetUtcNow(), expectedHash, 1, Admin, "s01", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoProofUnderThatSubmissionId_WhenADeletionIsAttempted_ThenNothingIsRemoved()
    {
        var (_, delete, ports) = Build();
        OnFile(ports, [.. TestData.ConsentProofsFor(Address)]);

        Assert.False(await delete.ExecuteAsync(Address, "s99", Admin));

        await ports.Delete.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }
}
