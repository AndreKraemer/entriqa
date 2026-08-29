using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Entriqa.Application;
using Entriqa.Application.Ports;
using Entriqa.Application.UseCases;
using Entriqa.Infrastructure.Brevo;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #22: the admin must know the whole Brevo directory, and must never present an
/// incompletely loaded one as complete. Brevo pages with limit/offset and returns a total
/// "count" next to the array (developers.brevo.com/reference/getlists-1, /getsmtptemplates).
/// </summary>
public class BrevoDirectoryTests
{
    /// <summary>Serves prepared response bodies in order and records what was requested.</summary>
    private sealed class FakeHandler(params string[] pages) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            var body = Requests.Count <= pages.Length ? pages[Requests.Count - 1] : pages[^1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static BrevoAdapter Adapter(FakeHandler handler)
    {
        var options = TestData.Options();
        options.Brevo.ApiKey = "unit-test-key";
        return new BrevoAdapter(new HttpClient(handler) { BaseAddress = new Uri("https://brevo.test/v3/") },
            Options.Create(options));
    }

    /// <summary>One page of the shape Brevo returns: the total in "count", the entries under their array name.</summary>
    private static string Page(string arrayName, int count, int from, int n)
    {
        var entries = Enumerable.Range(from, n)
            .Select(i => $$"""{"id":{{i}},"name":"Eintrag {{i.ToString(CultureInfo.InvariantCulture)}}"}""");
        return $$"""{"count":{{count}},"{{arrayName}}":[{{string.Join(",", entries)}}]}""";
    }

    /// <summary>AC 1: every list of the account, however many pages that takes.</summary>
    [Fact]
    public async Task GivenMoreListsThanFitOnOnePage_WhenLoadingTheLists_ThenEveryListIsReturned()
    {
        var handler = new FakeHandler(
            Page("lists", 120, 1, 50),
            Page("lists", 120, 51, 50),
            Page("lists", 120, 101, 20));

        var lists = await Adapter(handler).GetListsAsync();

        Assert.Equal(120, lists.Count);
        Assert.Equal(120, lists.Select(l => l.Id).Distinct().Count());
    }

    /// <summary>AC 2: the same for templates.</summary>
    [Fact]
    public async Task GivenMoreTemplatesThanFitOnOnePage_WhenLoadingTheTemplates_ThenEveryTemplateIsReturned()
    {
        var handler = new FakeHandler(
            Page("templates", 75, 1, 50),
            Page("templates", 75, 51, 25));

        var templates = await Adapter(handler).GetTemplatesAsync();

        Assert.Equal(75, templates.Count);
    }

    /// <summary>A count that promises more than the account delivers must not spin forever.</summary>
    [Fact]
    public async Task GivenACountLargerThanWhatIsDelivered_WhenLoadingTheLists_ThenPagingStopsAtTheShortPage()
    {
        var handler = new FakeHandler(
            Page("lists", 5000, 1, 50),
            Page("lists", 5000, 51, 10));

        var lists = await Adapter(handler).GetListsAsync();

        Assert.Equal(60, lists.Count);
        Assert.True(handler.Requests.Count <= 3, $"stopped after {handler.Requests.Count} requests");
    }

    private static GetIntegrationDirectoryUseCase Directory(IBrevoDirectoryPort brevo)
    {
        var options = TestData.Options();
        options.Brevo.ApiKey = "unit-test-key";
        var artifacts = Substitute.For<IListArtifactsPort>();
        artifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());
        return new GetIntegrationDirectoryUseCase(brevo, Substitute.For<IListReportTemplatesPort>(), artifacts,
            Options.Create(options), NullLogger<GetIntegrationDirectoryUseCase>.Instance);
    }

    private static IBrevoDirectoryPort BrevoWithFailingTemplates()
    {
        var brevo = Substitute.For<IBrevoDirectoryPort>();
        brevo.GetListsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new BrevoListInfo(7, "Newsletter") });
        brevo.GetTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<BrevoTemplateInfo>>(_ => throw new HttpRequestException("Brevo down"));
        return brevo;
    }

    /// <summary>AC 3: a section that could not be loaded must not look like an empty one.</summary>
    [Fact]
    public async Task GivenTheTemplateFetchFails_WhenLoadingTheDirectory_ThenTheTemplatesAreNotReportedComplete()
    {
        var directory = await Directory(BrevoWithFailingTemplates()).ExecuteAsync();

        Assert.False(directory.BrevoTemplatesComplete);
    }

    /// <summary>AC 3: one failing section must not take the healthy one down with it.</summary>
    [Fact]
    public async Task GivenTheTemplateFetchFails_WhenLoadingTheDirectory_ThenTheListsAreStillCompleteAndReturned()
    {
        var directory = await Directory(BrevoWithFailingTemplates()).ExecuteAsync();

        Assert.Single(directory.BrevoLists);
        Assert.True(directory.BrevoListsComplete);
    }

    /// <summary>The status page only asks whether Brevo answers - it must not pull the whole account.</summary>
    [Fact]
    public async Task GivenTheStatusPage_WhenCheckingWhetherBrevoAnswers_ThenTheDirectoryIsNotLoaded()
    {
        var brevo = Substitute.For<IBrevoDirectoryPort>();
        brevo.IsReachableAsync(Arg.Any<CancellationToken>()).Returns(true);
        var options = TestData.Options();
        options.Brevo.ApiKey = "unit-test-key";

        await new GetAdminStatusUseCase(brevo, Substitute.For<IGetHousekeepingRunQuery>(),
            new Entriqa.Application.Security.LicenseService(Options.Create(options), TestData.Time),
            Options.Create(options)).ExecuteAsync();

        await brevo.Received(1).IsReachableAsync(Arg.Any<CancellationToken>());
        await brevo.DidNotReceive().GetListsAsync(Arg.Any<CancellationToken>());
    }
}
