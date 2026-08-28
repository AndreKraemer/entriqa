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
/// A definition written by hand or imported through the API can leave collections out entirely -
/// the admin always writes them, JSON does not have to. That used to reach the publish check as
/// null and crash it with a NullReferenceException, which turns a reportable problem into an
/// unhandled error. Missing collections are empty collections.
/// </summary>
public class FormDefinitionRobustnessTests
{
    private static PublishCheckService Build()
    {
        var steps = new ISubmissionStep[] { new NotifyMailStep(Substitute.For<ISendTransactionalMailPort>()) };
        var pipeline = new SubmissionPipelineService(steps, Options.Create(TestData.Options()), TestData.Time, NullLogger<SubmissionPipelineService>.Instance);
        return new PublishCheckService(pipeline);
    }

    private static FormDefinition Parse(string json) =>
        JsonSerializer.Deserialize<FormDefinition>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void GivenADefinitionWithoutAPipelineKey_WhenCheckingItBeforePublish_ThenItReportsInsteadOfThrowing()
    {
        var form = Parse("""
        {"slug":"nur-ablegen","name":"Nur ablegen","type":"contact","handling":true,
         "fields":[{"id":"name","type":"text","label":"Name","required":true}],
         "completion":{"mode":"message","message":"Danke"}}
        """);

        Assert.Empty(Build().Check(form));
        Assert.Empty(form.Pipeline);
    }

    [Fact]
    public void GivenADefinitionWithoutAFieldsKey_WhenCheckingItBeforePublish_ThenItReportsInsteadOfThrowing()
    {
        var form = Parse("""
        {"slug":"leer","name":"Leer","type":"contact","handling":false,
         "completion":{"mode":"message","message":"Danke"}}
        """);

        var issues = Build().Check(form);

        Assert.Empty(form.Fields);
        Assert.Empty(form.Pipeline);
        Assert.Empty(issues);
    }

    [Fact]
    public void GivenADefinitionWithoutAPipelineKey_WhenLocalizingIt_ThenItDoesNotThrow()
    {
        var form = Parse("""
        {"slug":"nur-ablegen","name":"Nur ablegen","type":"contact","locales":["de","en"],
         "fields":[{"id":"name","type":"text","label":{"de":"Name","en":"Name"}}],
         "completion":{"mode":"message","message":{"de":"Danke","en":"Thanks"}}}
        """);

        Assert.Empty(form.Localize("en").Pipeline);
    }
}
