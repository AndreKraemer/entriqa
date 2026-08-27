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
    public void GivenAShippedSeedForm_WhenResolvingEveryDeclaredLocale_ThenTheSlugAndFileNameAgree(string fileName)
    {
        var form = Load(fileName);
        Assert.Equal(Path.GetFileNameWithoutExtension(fileName), form.Slug);
        foreach (var locale in form.EffectiveLocales) Assert.Equal(locale, form.Localize(locale).MatchLocale(locale));
    }

    /// <summary>
    /// The samples exist to demonstrate the product, so the set as a whole has to stay complete:
    /// every field type and every form type appears at least once. Without this the coverage
    /// silently rots the first time someone edits a sample.
    /// </summary>
    [Fact]
    public void GivenAllShippedSeedForms_WhenCollectingTheirFieldTypes_ThenEveryKnownFieldTypeIsDemonstrated()
    {
        var used = Directory.EnumerateFiles(SeedFolder(), "*.json")
            .SelectMany(f => Load(Path.GetFileName(f)).Fields).Select(f => f.Type).ToHashSet();
        Assert.Empty(FieldTypes.All.Except(used));
    }

    [Fact]
    public void GivenAllShippedSeedForms_WhenCollectingTheirFormTypes_ThenContactLeadmagnetAndQuizAreAllPresent()
    {
        var used = Directory.EnumerateFiles(SeedFolder(), "*.json")
            .Select(f => Load(Path.GetFileName(f)).Type).ToHashSet();
        Assert.Empty(new[] { "contact", "leadmagnet", "quiz" }.Except(used));
    }
}
