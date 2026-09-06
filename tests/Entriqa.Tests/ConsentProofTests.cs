using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Consent;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Application.Security;
using Entriqa.Application.UseCases;
using Entriqa.Domain.Consent;
using Entriqa.Domain.Forms;
using Entriqa.Domain.Submissions;
using Entriqa.Domain.UseCases;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The consent proof of #1: it comes into existence with the submission, is amended by the double
/// opt-in, carries nothing but the evidence, survives the retention period of the submission and
/// ends on an event - the GDPR erasure of the contact.
/// </summary>
public class ConsentProofTests
{
    private const string ClientIp = "203.0.113.9";
    private static DateTimeOffset Start => new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

    // ---- harnesses ----------------------------------------------------------------------------

    /// <summary>
    /// The first harness for <see cref="SubmitFormUseCase"/> in this project - nothing constructed it
    /// before #1. Everything it touches is substituted; the clock is local, never TestData.Time,
    /// because the token has to age past MinSubmitSeconds.
    /// </summary>
    private static (ISubmitFormUseCase UseCase, TestData.ConsentProofPorts Proofs, FakeTimeProvider Clock, string Token)
        BuildSubmit(FormDefinition form, EntriqaOptions? options = null)
    {
        var clock = new FakeTimeProvider(Start);
        var opts = Options.Create(options ?? TestData.Options());

        var getPublished = Substitute.For<ITryGetPublishedFormQuery>();
        getPublished.ExecuteAsync(form.Slug, Arg.Any<CancellationToken>())
            .Returns(new FormVersion(form.Slug, 7, form, clock.GetUtcNow(), "test"));
        var nonce = Substitute.For<ITryConsumeNonceCommand>();
        nonce.ExecuteAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(true);
        var rateLimit = Substitute.For<IRegisterRateLimitHitCommand>();
        rateLimit.ExecuteAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(1);

        var tokens = new FormTokenService(opts, clock);
        var pipeline = new SubmissionPipelineService(
            [new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
            opts, clock, NullLogger<SubmissionPipelineService>.Instance);
        var (proofs, ports) = TestData.ConsentProofs(clock, options);

        var useCase = new SubmitFormUseCase(
            getPublished, Substitute.For<IStoreSubmissionCommand>(), Substitute.For<ISaveSubmissionCommand>(),
            nonce, rateLimit, Substitute.For<IStoreArtifactPort>(), tokens, new IpHasher(opts), pipeline,
            proofs, opts, clock, NullLogger<SubmitFormUseCase>.Instance);

        var token = tokens.Issue(FormTokenService.KindForm, form.Slug);
        clock.Advance(TimeSpan.FromSeconds(10));                        // the token has to be older than MinSubmitSeconds
        return (useCase, ports, clock, token);
    }

    private static (IConfirmSubmissionUseCase UseCase, TestData.ConsentProofPorts Proofs, FakeTimeProvider Clock, string Token, Submission Submission)
        BuildConfirm()
    {
        var clock = new FakeTimeProvider(Start);
        var opts = Options.Create(TestData.Options());
        var submission = new Submission
        {
            Id = "kontakt:0001", Slug = "kontakt", Version = 7, CreatedAt = clock.GetUtcNow(),
            Values = new Dictionary<string, string>(), Email = "eva@example.org", Locale = "de",
        };
        var getSubmission = Substitute.For<ITryGetSubmissionQuery>();
        getSubmission.ExecuteAsync(submission.Id, Arg.Any<CancellationToken>()).Returns(submission);
        var getVersion = Substitute.For<ITryGetFormVersionQuery>();
        getVersion.ExecuteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new FormVersion("kontakt", 7, TestData.Contact(), clock.GetUtcNow(), "test"));

        var tokens = new FormTokenService(opts, clock);
        var pipeline = new SubmissionPipelineService(
            [new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
            opts, clock, NullLogger<SubmissionPipelineService>.Instance);
        var (proofs, ports) = TestData.ConsentProofs(clock);

        var useCase = new ConfirmSubmissionUseCase(
            getSubmission, getVersion, Substitute.For<ISaveSubmissionCommand>(), tokens,
            new IpHasher(opts), pipeline, proofs, opts, clock);

        return (useCase, ports, clock, tokens.Issue(FormTokenService.KindConfirm, submission.Id), submission);
    }

    private static (IRunHousekeepingUseCase UseCase, TestData.ConsentProofPorts Proofs, FakeTimeProvider Clock,
        IListHousekeepingSubmissionsQuery List, IDeleteSubmissionCommand DeleteSubmission)
        BuildHousekeeping(EntriqaOptions? options = null)
    {
        var clock = new FakeTimeProvider(Start);
        var opts = Options.Create(options ?? TestData.Options());
        var list = Substitute.For<IListHousekeepingSubmissionsQuery>();
        list.ListUnfinishedAsync(default, default).ReturnsForAnyArgs(Array.Empty<Submission>());
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(Array.Empty<Submission>());
        var listArtifacts = Substitute.For<IListArtifactsPort>();
        listArtifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());
        var deleteSubmission = Substitute.For<IDeleteSubmissionCommand>();
        var pipeline = new SubmissionPipelineService(
            [new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>())],
            opts, clock, NullLogger<SubmissionPipelineService>.Instance);
        var (proofs, ports) = TestData.ConsentProofs(clock, options);

        var useCase = new RunHousekeepingUseCase(
            list, Substitute.For<ITryGetFormVersionQuery>(), Substitute.For<ISaveSubmissionCommand>(),
            deleteSubmission, Substitute.For<IPurgeSecurityEntriesCommand>(),
            Substitute.For<IRecordHousekeepingRunCommand>(), Substitute.For<IStoreArtifactPort>(), listArtifacts,
            pipeline, proofs, opts, clock, NullLogger<RunHousekeepingUseCase>.Instance);

        return (useCase, ports, clock, list, deleteSubmission);
    }

    private static FormDefinition FormWith(params FieldDefinition[] fields) => TestData.Contact() with { Fields = fields };

    private static FieldDefinition Consent(bool required) =>
        new("consent", FieldTypes.Consent, "Einwilligung", Required: required, Text: "Ich stimme zu.");

    private static ConsentProof AnyProof(DateTimeOffset submittedAt) => new()
    {
        Email = "eva@example.org", SubmissionId = "kontakt:0001", Slug = "kontakt", Version = 7,
        SubmittedAt = submittedAt, ConsentText = "Ich stimme zu.",
    };

    // ---- AC 1: the proof comes into existence with the submission -----------------------------

    [Fact]
    public async Task GivenAFormWithATickedConsent_WhenTheSubmissionIsStored_ThenAProofCarriesTheEvidenceOfThatConsent()
    {
        // Bilingual consent text submitted with Lang "en": "im Wortlaut" means the wording this
        // visitor saw, not the one the default locale would have shown. A monolingual fixture cannot
        // tell the two apart, and this repository has shipped that class of defect twice (#41, #49).
        var bilingual = TestData.Contact() with
        {
            Locales = new[] { "de", "en" },
            Fields = TestData.Contact().Fields
                .Select(f => f.Type == FieldTypes.Consent
                    ? f with { Text = new LText(new Dictionary<string, string> { ["de"] = "Ich stimme zu.", ["en"] = "I agree." }) }
                    : f)
                .ToList(),
        };
        var (useCase, proofs, clock, token) = BuildSubmit(bilingual);
        var values = new Dictionary<string, string>
        {
            ["name"] = "Eva", ["email"] = "eva@example.org", ["msg"] = "Hallo", ["consent"] = "true",
        };

        var result = await useCase.ExecuteAsync(new SubmitFormRequest("kontakt", token, values, null, null, ClientIp, "en"));

        await proofs.Store.Received(1).ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
        var written = (ConsentProof)proofs.Store.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Equal("eva@example.org", written.Email);
        Assert.Equal(result.SubmissionId, written.SubmissionId);
        Assert.Equal("kontakt", written.Slug);
        Assert.Equal(7, written.Version);
        Assert.Equal(clock.GetUtcNow(), written.SubmittedAt);
        Assert.Equal("I agree.", written.ConsentText);
        Assert.Equal(new IpHasher(Options.Create(TestData.Options())).Hash(ClientIp), written.IpHash);
        Assert.Null(written.ConfirmedAt);
    }

    [Fact]
    public async Task GivenASubmissionOlderThanTheClock_WhenTheProofIsRecorded_ThenItCarriesTheSubmissionTimeNotTheWriteTime()
    {
        // AC 1 says Einsendungszeitpunkt. In the use-case harness the two coincide, so time.GetUtcNow()
        // would pass there - and SubmittedAt is the field the retention purge of AC 8 filters on, and
        // the dev seed writes a proof for a submission back-dated past RetentionDays.
        var clock = new FakeTimeProvider(Start);
        var (service, proofs) = TestData.ConsentProofs(clock);
        var submitted = Start.AddDays(-200);
        var submission = new Submission
        {
            Id = "kontakt:0001", Slug = "kontakt", Version = 7, CreatedAt = submitted,
            Values = new Dictionary<string, string> { ["consent"] = "true" },
            Email = "eva@example.org", ConsentText = "Ich stimme zu.",
        };

        await service.RecordAsync(FormWith(Consent(required: true)), submission);

        var written = (ConsentProof)proofs.Store.ReceivedCalls().Single().GetArguments()[0]!;
        Assert.Equal(submitted, written.SubmittedAt);
    }

    [Fact]
    public async Task GivenTheProofTableIsUnreachable_WhenTheSubmissionIsStored_ThenTheSubmissionStillSucceeds()
    {
        // The deliberate interpretation the issue does not state: a table timeout must not cost a
        // conversion. Without this, neutering both catch filters leaves the whole suite green.
        var (useCase, proofs, _, token) = BuildSubmit(TestData.Contact());
        proofs.Store.ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TimeoutException("Tabelle nicht erreichbar"));
        var values = new Dictionary<string, string>
        {
            ["name"] = "Eva", ["email"] = "eva@example.org", ["msg"] = "Hallo", ["consent"] = "true",
        };

        var result = await useCase.ExecuteAsync(new SubmitFormRequest("kontakt", token, values, null, null, ClientIp));

        Assert.StartsWith("kontakt:", result.SubmissionId, StringComparison.Ordinal);
    }

    // ---- AC 3: the proof carries nothing else -------------------------------------------------

    [Fact]
    public void GivenTheConsentProof_WhenItsShapeIsInspected_ThenItExposesNothingBeyondTheEvidence()
    {
        // Field values, quiz outcome and attachments must have nowhere to live, not merely go unwritten.
        var expected = new[]
        {
            nameof(ConsentProof.Email), nameof(ConsentProof.SubmissionId), nameof(ConsentProof.Slug),
            nameof(ConsentProof.Version), nameof(ConsentProof.SubmittedAt), nameof(ConsentProof.ConsentText),
            nameof(ConsentProof.IpHash), nameof(ConsentProof.ConfirmedAt), nameof(ConsentProof.ConfirmedIpHash),
        };

        var actual = typeof(ConsentProof)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
    }

    // ---- AC 4: no consent, no proof -----------------------------------------------------------

    [Fact]
    public async Task GivenAnAddressAndAnOptionalConsentLeftUnticked_WhenTheProofIsRecorded_ThenNothingIsWritten()
    {
        // Asked of the rule directly, not through SubmitFormUseCase: the validator rejects an address
        // without the tick (ConsentIfEmail), so that combination never reaches the write path from
        // outside - and a form without an address is turned away by the email precondition anyway,
        // which would mask this rule and let a mutant survive. ConsentText is set for every submission
        // of a form that HAS a consent field; the tick is what counts.
        var (service, proofs) = TestData.ConsentProofs();
        var form = FormWith(new FieldDefinition("email", FieldTypes.Email, "E-Mail"), Consent(required: false));
        var submission = new Submission
        {
            Id = "kontakt:0001", Slug = "kontakt", Version = 7, CreatedAt = Start,
            Values = new Dictionary<string, string> { ["email"] = "eva@example.org" },
            Email = "eva@example.org", ConsentText = "Ich stimme zu.",
        };

        await service.RecordAsync(form, submission);

        await proofs.Store.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenATickedConsentWithoutAnEmailAddress_WhenTheSubmissionIsStored_ThenNoProofIsWritten()
    {
        // A proof nobody can be attributed to is neither usable as evidence nor findable again for erasure.
        var form = FormWith(new FieldDefinition("name", FieldTypes.Text, "Name", Required: true), Consent(required: true));
        var (useCase, proofs, _, token) = BuildSubmit(form);
        var values = new Dictionary<string, string> { ["name"] = "Eva", ["consent"] = "true" };

        await useCase.ExecuteAsync(new SubmitFormRequest("kontakt", token, values, null, null, ClientIp));

        await proofs.Store.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenASubmissionWithoutAProof_WhenTheDoubleOptInIsConfirmed_ThenNoProofIsCreated()
    {
        var (useCase, proofs, _, token, _) = BuildConfirm();

        await useCase.ExecuteAsync(token, ClientIp);

        await proofs.Store.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }

    // ---- AC 2: the confirmation amends the proof ----------------------------------------------

    [Fact]
    public async Task GivenAnUnconfirmedSubmission_WhenTheDoubleOptInIsConfirmed_ThenItsProofIsAmendedWithTimeAndIpHash()
    {
        var (useCase, proofs, clock, token, submission) = BuildConfirm();
        var expectedHash = new IpHasher(Options.Create(TestData.Options())).Hash(ClientIp);

        await useCase.ExecuteAsync(token, ClientIp);

        await proofs.Confirm.Received(1).ExecuteAsync(submission.Id, clock.GetUtcNow(), expectedHash, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAConfirmationRecordedEarlier_WhenTheProofIsAmended_ThenItCarriesTheClickTimeNotTheWriteTime()
    {
        // AC 2 says Bestätigungszeitpunkt. Inside ConfirmSubmissionUseCase the click and the write
        // share one clock tick, so time.GetUtcNow() passes there - the housekeeping sweep that picks
        // up a stalled run hours later is where the two come apart.
        var clock = new FakeTimeProvider(Start);
        var (service, proofs) = TestData.ConsentProofs(clock);
        var clickedAt = Start.AddHours(-3);
        var submission = new Submission
        {
            Id = "kontakt:0001", Slug = "kontakt", Version = 7, CreatedAt = Start.AddHours(-4),
            Values = new Dictionary<string, string>(), Email = "eva@example.org",
            ConfirmedAt = clickedAt, ConfirmedIpHash = "abc123",
        };

        await service.ConfirmAsync(submission);

        await proofs.Confirm.Received(1).ExecuteAsync(submission.Id, clickedAt, "abc123", Arg.Any<CancellationToken>());
    }

    // ---- AC 5 + 6: the housekeeping cannot reach the proof --------------------------------------

    [Fact]
    public async Task GivenTheDefaultRetentionSettings_WhenHousekeepingRuns_ThenNoProofIsDeletedHoweverOldItIs()
    {
        var (useCase, proofs, clock, _, _) = BuildHousekeeping();
        proofs.ListExpired.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { AnyProof(clock.GetUtcNow().AddYears(-5)) });   // an unconditional purge would take this one

        await useCase.ExecuteAsync();

        await proofs.Delete.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAnExpiredSubmissionWhoseProofExists_WhenRetentionRuns_ThenTheSubmissionGoesAndTheProofStays()
    {
        var (useCase, proofs, clock, list, deleteSubmission) = BuildHousekeeping();
        var expired = new Submission
        {
            Id = "kontakt:0001", Slug = "kontakt", Version = 7, CreatedAt = clock.GetUtcNow().AddDays(-200),
            Values = new Dictionary<string, string>(), Email = "eva@example.org",
        };
        list.ListExpiredAsync(default, default, default).ReturnsForAnyArgs(new[] { expired });
        proofs.ListExpired.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { AnyProof(expired.CreatedAt) });

        var result = await useCase.ExecuteAsync();

        Assert.Equal(1, result.Deleted);
        await deleteSubmission.Received(1).ExecuteAsync(expired, Arg.Any<CancellationToken>());
        await proofs.Delete.DidNotReceive().ExecuteAsync(Arg.Any<ConsentProof>(), Arg.Any<CancellationToken>());
    }

    // ---- AC 8: the configured exception --------------------------------------------------------

    [Fact]
    public async Task GivenAConfiguredConsentRetention_WhenHousekeepingRuns_ThenProofsOlderThanItAreDeleted()
    {
        var options = TestData.Options();
        options.ConsentRetentionDays = 30;
        var (useCase, proofs, clock, _, _) = BuildHousekeeping(options);
        var old = AnyProof(clock.GetUtcNow().AddDays(-31));
        proofs.ListExpired.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { old });

        await useCase.ExecuteAsync();

        await proofs.ListExpired.Received(1).ExecuteAsync(clock.GetUtcNow().AddDays(-30), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await proofs.Delete.Received(1).ExecuteAsync(old, Arg.Any<CancellationToken>());
    }

    // ---- AC 7: the erasure ends the proof ------------------------------------------------------

    [Fact]
    public async Task GivenAContactWhoseSubmissionsAreLongGone_WhenTheContactIsDeleted_ThenItsProofsAreDeletedAnyway()
    {
        // The contact list is derived from submissions; after the retention period there are none left,
        // and the proof is exactly what the erasure has to reach.
        var query = Substitute.For<IListContactSubmissionsQuery>();
        query.ListByEmailAsync("eva@example.org", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SubmissionListItem>());
        var (service, proofs) = TestData.ConsentProofs();
        var useCase = new DeleteContactUseCase(query, Substitute.For<IDeleteSubmissionAdminUseCase>(), service);

        await useCase.ExecuteAsync("eva@example.org", "kim@admin.example");

        await proofs.DeleteByEmail.Received(1).ExecuteAsync("eva@example.org", Arg.Any<CancellationToken>());

        // Recorded even though nothing was found: a missing row would be ambiguous between "never
        // had a proof" and "the erasure never ran", which is the question the row exists to answer.
        await proofs.RecordDeletion.Received(1).ExecuteAsync(
            TestData.Time.GetUtcNow(), Arg.Any<string>(), 0, "kim@admin.example", Arg.Is<string?>(x => x == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenDeletedProofs_WhenTheContactIsDeleted_ThenTheErasureIsRecordedWithoutThePlaintextAddress()
    {
        // Mixed case on purpose: deletion matches case-insensitively, so a row filed under the casing
        // the admin happened to pass could never be found again from the normalized address.
        const string asTyped = "Eva@Example.org";
        var query = Substitute.For<IListContactSubmissionsQuery>();
        query.ListByEmailAsync(asTyped, Arg.Any<CancellationToken>()).Returns(Array.Empty<SubmissionListItem>());
        var (service, proofs) = TestData.ConsentProofs();
        proofs.DeleteByEmail.ExecuteAsync(asTyped, Arg.Any<CancellationToken>()).Returns(2);
        var useCase = new DeleteContactUseCase(query, Substitute.For<IDeleteSubmissionAdminUseCase>(), service);
        var expectedHash = new IpHasher(Options.Create(TestData.Options())).Hash("eva@example.org")!;

        await useCase.ExecuteAsync(asTyped, "kim@admin.example");

        // #2: and it names the admin who triggered it. A contact-wide erasure passes no submission id -
        //     it removes whatever was there, and the count is the whole answer.
        await proofs.RecordDeletion.Received(1).ExecuteAsync(
            TestData.Time.GetUtcNow(), expectedHash, 2, "kim@admin.example", Arg.Is<string?>(x => x == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenTheProofTableIsUnreachable_WhenTheDoubleOptInIsConfirmed_ThenTheConfirmationStillSucceeds()
    {
        // The other half of the non-critical stance: the visitor clicked a link in a mail and must
        // see a confirmation, whatever the proof table is doing.
        var (useCase, proofs, _, token, _) = BuildConfirm();
        proofs.Confirm.ExecuteAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TimeoutException("Tabelle nicht erreichbar"));

        var result = await useCase.ExecuteAsync(token, ClientIp);

        Assert.Equal("kontakt", result.Slug);
    }

    [Fact]
    public async Task GivenTheAuditRowCannotBeWritten_WhenTheContactIsDeleted_ThenTheErasureStillReportsWhatItRemoved()
    {
        // The proofs are gone by the time the row is written; losing the row must not report the
        // erasure itself as failed.
        var query = Substitute.For<IListContactSubmissionsQuery>();
        query.ListByEmailAsync("eva@example.org", Arg.Any<CancellationToken>()).Returns(Array.Empty<SubmissionListItem>());
        var (service, proofs) = TestData.ConsentProofs();
        proofs.DeleteByEmail.ExecuteAsync("eva@example.org", Arg.Any<CancellationToken>()).Returns(3);
        proofs.RecordDeletion.ExecuteAsync(Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<int>(),
                                           Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new TimeoutException("Tabelle nicht erreichbar"));

        Assert.Equal(3, await service.DeleteForAsync("eva@example.org", "kim@admin.example"));
    }

    [Fact]
    public void GivenAddressesDifferingOnlyInCaseAndPadding_WhenTheyAreKeyed_ThenTheyResolveToTheSameKey()
    {
        // The one rule the storage key and the erasure audit hash share. If they ever disagree, a
        // contact's proofs are stored under one key and its erasure recorded under another.
        Assert.Equal(ConsentProof.KeyOf("eva@example.org"), ConsentProof.KeyOf("  Eva@Example.ORG "));
    }

    [Fact]
    public void GivenThePartitionKey_WhenReadingItsSource_ThenItNormalizesThroughTheSharedRule()
    {
        // Entriqa.Data has no InternalsVisibleTo, so PartitionOf cannot be called from here - and the
        // rule it must not stop delegating to is invisible from the outside: writes and the erasure
        // both go through PartitionOf, so if it normalized differently every proof would simply be
        // filed under the submitter's casing and become unreachable the moment the admin types
        // another. Guarded by reading the source, the way LocaleSourceGuardTests does.
        var source = File.ReadAllText(Path.Combine(RepositoryDirectory(), "src", "Core", "Entriqa.Data",
                                                   "Mapping", "ConsentProofMapper.cs"));
        var member = Regex.Match(source, @"public static string PartitionOf\(string email\).*?\n    \}", RegexOptions.Singleline);
        Assert.True(member.Success, "ConsentProofMapper no longer has a PartitionOf member - update this guard.");

        Assert.Contains("ConsentProof.KeyOf(", member.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ToLowerInvariant", member.Value, StringComparison.Ordinal);   // not a second copy of the rule
    }

    [Fact]
    public void GivenTheAdminSearch_WhenReadingItsSource_ThenItLooksUpThePartitionThroughTheSharedRule()
    {
        // #2: the same rule, now also on the read side. Handing the raw address to the partition filter
        // compiles, passes every unit test - the port is substituted there - and quietly makes the
        // admin's search miss every proof whose submitter used different casing than the operator types.
        var source = File.ReadAllText(Path.Combine(RepositoryDirectory(), "src", "Core", "Entriqa.Data",
                                                   "Queries", "ConsentProofQueries.cs"));
        var member = Regex.Match(source, @"class ListConsentProofsByEmailQuery.*?
\}", RegexOptions.Singleline);
        Assert.True(member.Success, "ListConsentProofsByEmailQuery is gone or renamed - update this guard.");

        Assert.Contains("ConsentProofMapper.PartitionOf(email)", member.Value, StringComparison.Ordinal);

        // And the filter compares against it. Inverting that one operator turns the search into "every
        // OTHER address's proofs" - a stranger's consent handed to an Art. 15 enquiry, with nothing in
        // the suite to notice, because no test here reaches storage.
        Assert.Contains("e.PartitionKey == partition", member.Value, StringComparison.Ordinal);
    }

    private static string RepositoryDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Core", "Entriqa.Data"))) return dir.FullName;
        throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory + ".");
    }
}
