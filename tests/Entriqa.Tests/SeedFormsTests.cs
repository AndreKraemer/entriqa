using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application.Pipeline;
using Entriqa.Application.Pipeline.Steps;
using Entriqa.Application.Ports;
using Entriqa.Domain.Forms;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// The definitions in seed/forms are shipped samples: DevSeedHostedService publishes them on
/// every local start, and samples/site embeds them. A broken sample therefore breaks the first
/// five minutes of anyone trying the product out. These tests run the real publish check over
/// them - the same gate the admin applies before a form goes live.
/// </summary>
public class SeedFormsTests
{
    private static PublishCheckService Build()
    {
        var mail = Substitute.For<ISendTransactionalMailPort>();
        var webhook = Substitute.For<IPostWebhookPort>();
        var steps = new ISubmissionStep[]
        {
            new NotifyMailStep(mail),
            new DoiRequestStep(mail, TestData.Tokens()),
            new BrevoContactStep(Substitute.For<IUpsertBrevoContactPort>(), TestData.Time),
            new BrevoCompanyStep(Substitute.For<IUpsertBrevoCompanyPort>()),
            new BrevoMailStep(mail, Substitute.For<IStoreArtifactPort>(), Substitute.For<ICreateDownloadLinkPort>()),
            new LeadMagnetLinkStep(Substitute.For<ICreateDownloadLinkPort>()),
            new ReportingCloudPdfStep(Substitute.For<IMergeDocumentPort>(), Substitute.For<IStoreArtifactPort>(), TestData.Time),
            new TeamsNotifyStep(webhook),
            new WebhookCallStep(webhook),
        };
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        return new PublishCheckService(pipeline);
    }

    /// <summary>Walk upwards from the test output directory, the same way DevSeedHostedService resolves the folder.</summary>
    private static string SeedFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "seed", "forms");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"seed/forms not found above {AppContext.BaseDirectory}");
    }

    public static TheoryData<string> SeedFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(SeedFolder(), "*.json")) data.Add(Path.GetFileName(file));
        return data;
    }

    private static FormDefinition Load(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(SeedFolder(), fileName));
        return JsonSerializer.Deserialize<FormDefinition>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException($"{fileName} deserialized to null");
    }

    [Theory]
    [MemberData(nameof(SeedFiles))]
    public void GivenAShippedSeedForm_WhenCheckingItBeforePublish_ThenNoIssuesAreReported(string fileName)
    {
        var issues = Build().Check(Load(fileName));
        Assert.True(issues.Count == 0, $"{fileName}: {string.Join(" | ", issues)}");
    }

    [Theory]
    [MemberData(nameof(SeedFiles))]
    public void GivenAShippedSeedForm_WhenReadingItsSlug_ThenItAgreesWithTheFileName(string fileName)
    {
        // DevSeedHostedService keys on the slug, the sample site embeds by slug, and a mismatch
        // would publish a form nobody can find under the name of the file that defines it.
        Assert.Equal(Path.GetFileNameWithoutExtension(fileName), Load(fileName).Slug);
    }

    [Theory]
    [MemberData(nameof(SeedFiles))]
    public void GivenAShippedSeedForm_WhenReadingItsLocales_ThenItIsBilingual(string fileName)
    {
        // The samples exist to demonstrate a bilingual product; a form that quietly dropped its
        // English texts would still pass the publish check, which only validates DECLARED locales.
        Assert.Equal(new[] { "de", "en" }, Load(fileName).EffectiveLocales);
    }

    /// <summary>
    /// The samples exist to demonstrate the product, so the set as a whole has to stay complete:
    /// every field type and every form type appears at least once. Without this the coverage
    /// silently rots the first time someone edits a sample.
    /// </summary>
    /// <summary>
    /// Pinned to beratung.json, not to the union over all samples: the criterion is about ONE form
    /// showing every type. A union stays green while the types are redistributed across samples,
    /// which is exactly the regression this is meant to catch.
    /// </summary>
    [Fact]
    public void GivenTheConsultingSample_WhenCollectingItsFieldTypes_ThenEveryKnownFieldTypeIsDemonstrated()
    {
        var used = Load("beratung.json").Fields.Select(f => f.Type).ToHashSet();
        Assert.Empty(FieldTypes.All.Except(used));
    }

    [Fact]
    public void GivenAllShippedSeedForms_WhenCollectingTheirStepKeys_ThenTheLeadMagnetChainIsDemonstrated()
    {
        // Without this, deleting the leadmagnet.link step from the whitepaper sample leaves the
        // whole suite green - including SeedLeadMagnetTests, which then iterates an empty set.
        var used = Directory.EnumerateFiles(SeedFolder(), "*.json")
            .SelectMany(f => Load(Path.GetFileName(f)).Pipeline).Select(s => s.Step).ToHashSet();
        Assert.Empty(new[] { "notify.mail", "doi.request", "brevo.contact", "leadmagnet.link" }.Except(used));
    }

    [Fact]
    public void GivenTheSelfCheckSample_WhenLookingAtItsAddressBlock_ThenBothTheAddressAndTheConsentAreOptional()
    {
        // The only shipped form where a consent can be left unticked, which is the sole route by which
        // #1's "no tick, no proof" can be driven at the acceptance gate. Making either field required
        // removes that route while every test here stays green - samples/site/README.md says so too.
        var fields = Load("selbsttest.json").Fields;
        var email = Assert.Single(fields, f => f.Type == FieldTypes.Email);
        var consent = Assert.Single(fields, f => f.Type == FieldTypes.Consent);
        Assert.False(email.Required);
        Assert.False(consent.Required);
    }

    [Fact]
    public void GivenAllShippedSeedForms_WhenCollectingTheirFormTypes_ThenContactLeadmagnetAndQuizAreAllPresent()
    {
        var used = Directory.EnumerateFiles(SeedFolder(), "*.json")
            .Select(f => Load(Path.GetFileName(f)).Type).ToHashSet();
        Assert.Empty(new[] { "contact", "leadmagnet", "quiz" }.Except(used));
    }
}
