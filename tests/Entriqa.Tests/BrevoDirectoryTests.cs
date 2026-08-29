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
using Entriqa.Infrastructure.Dev;
using Xunit;

namespace Entriqa.Tests;

/// <summary>
/// Issue #22: the admin must know the whole Brevo directory, and must never present an
/// incompletely loaded one as complete. Brevo pages with limit/offset and returns a total
/// "count" next to the array (developers.brevo.com/reference/getlists-1, /getsmtptemplates).
/// </summary>
public class BrevoDirectoryTests
{
    /// <summary>
    /// Models the endpoint rather than a script of pages: it slices the account by the limit and offset it
    /// was asked for, and reports whatever total it was told to claim. Serving prepared pages by call order
    /// instead would hide the very thing paging is about - a client that never advances its offset would
    /// still walk through them and look correct. "Claimed" can exceed what exists, which is how a stale
    /// count is reproduced.
    /// </summary>
    private sealed class FakeHandler(string arrayName, long claimedCount, int available) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = request.RequestUri!.Query;
            Requests.Add(request.RequestUri.PathAndQuery);

            var offset = Number(query, "offset=") ?? 0;
            var limit = Number(query, "limit=") ?? 10;
            var entries = Enumerable.Range(offset + 1, Math.Max(0, Math.Min(limit, available - offset)))
                .Select(i => $$"""{"id":{{i}},"name":"Eintrag {{i.ToString(CultureInfo.InvariantCulture)}}"}""");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"count":{{claimedCount}},"{{arrayName}}":[{{string.Join(",", entries)}}]}""",
                    Encoding.UTF8, "application/json"),
            });
        }

        private static int? Number(string query, string key)
        {
            var marker = query.IndexOf(key, StringComparison.Ordinal);
            return marker < 0 ? null : int.Parse(query[(marker + key.Length)..].Split('&')[0], CultureInfo.InvariantCulture);
        }
    }

    private static BrevoAdapter Adapter(FakeHandler handler)
    {
        var options = TestData.Options();
        options.Brevo.ApiKey = "unit-test-key";
        return new BrevoAdapter(new HttpClient(handler) { BaseAddress = new Uri("https://brevo.test/v3/") },
            Options.Create(options));
    }

    /// <summary>AC 1: every list of the account, however many pages that takes - and the offset must advance.</summary>
    [Fact]
    public async Task GivenMoreListsThanFitOnOnePage_WhenLoadingTheLists_ThenEveryListIsReturned()
    {
        var handler = new FakeHandler("lists", claimedCount: 120, available: 120);

        var lists = await Adapter(handler).GetListsAsync();

        Assert.Equal(120, lists.Count);
        Assert.Equal(120, lists.Select(l => l.Id).Distinct().Count());
        Assert.Equal(
            new[] { "/v3/contacts/lists?limit=50&offset=0", "/v3/contacts/lists?limit=50&offset=50", "/v3/contacts/lists?limit=50&offset=100" },
            handler.Requests);
    }

    /// <summary>AC 2: the same for templates - on the one path that already carries a query string.</summary>
    [Fact]
    public async Task GivenMoreTemplatesThanFitOnOnePage_WhenLoadingTheTemplates_ThenEveryTemplateIsReturned()
    {
        var handler = new FakeHandler("templates", claimedCount: 75, available: 75);

        var templates = await Adapter(handler).GetTemplatesAsync();

        Assert.Equal(75, templates.Count);
        Assert.All(handler.Requests, r => Assert.Contains("?templateStatus=true&limit=50&offset=", r, StringComparison.Ordinal));
    }

    /// <summary>The account total ends the walk even when every page comes back full.</summary>
    [Fact]
    public async Task GivenTheAccountTotalIsReachedByFullPages_WhenLoadingTheLists_ThenNoFurtherPageIsRequested()
    {
        var handler = new FakeHandler("lists", claimedCount: 100, available: 100);

        var lists = await Adapter(handler).GetListsAsync();

        Assert.Equal(100, lists.Count);
        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>A count that promises more than the account holds must not spin the loop.</summary>
    [Fact]
    public async Task GivenACountLargerThanWhatIsDelivered_WhenLoadingTheLists_ThenPagingStopsAtTheEmptyPage()
    {
        var handler = new FakeHandler("lists", claimedCount: 5000, available: 60);

        var lists = await Adapter(handler).GetListsAsync();

        Assert.Equal(60, lists.Count);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>The status page asks whether Brevo answers - one request, not the account.</summary>
    [Fact]
    public async Task GivenTheAdapter_WhenCheckingReachability_ThenASingleSmallRequestIsSent()
    {
        var handler = new FakeHandler("lists", claimedCount: 120, available: 120);

        var reachable = await Adapter(handler).IsReachableAsync();

        Assert.True(reachable);
        Assert.Equal("/v3/contacts/lists?limit=1", Assert.Single(handler.Requests));
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

    private static IBrevoDirectoryPort BrevoWhere(bool listsFail)
    {
        var brevo = Substitute.For<IBrevoDirectoryPort>();
        if (listsFail)
            brevo.GetListsAsync(Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<BrevoListInfo>>(_ => throw new HttpRequestException("Brevo down"));
        else
            brevo.GetListsAsync(Arg.Any<CancellationToken>()).Returns(new[] { new BrevoListInfo(7, "Newsletter") });

        if (listsFail)
            brevo.GetTemplatesAsync(Arg.Any<CancellationToken>()).Returns(new[] { new BrevoTemplateInfo(3, "Bestätigung") });
        else
            brevo.GetTemplatesAsync(Arg.Any<CancellationToken>())
                .Returns<IReadOnlyList<BrevoTemplateInfo>>(_ => throw new HttpRequestException("Brevo down"));
        return brevo;
    }

    /// <summary>AC 3: a section that could not be loaded must not look like an empty one.</summary>
    [Fact]
    public async Task GivenTheTemplateFetchFails_WhenLoadingTheDirectory_ThenTheTemplatesAreNotReportedComplete()
    {
        var directory = await Directory(BrevoWhere(listsFail: false)).ExecuteAsync();

        Assert.False(directory.BrevoTemplatesComplete);
    }

    /// <summary>AC 3: one failing section must not take the healthy one down with it.</summary>
    [Fact]
    public async Task GivenTheTemplateFetchFails_WhenLoadingTheDirectory_ThenTheListsAreStillCompleteAndReturned()
    {
        var directory = await Directory(BrevoWhere(listsFail: false)).ExecuteAsync();

        Assert.Single(directory.BrevoLists);
        Assert.True(directory.BrevoListsComplete);
    }

    /// <summary>AC 3, the mirror case - the flags must be per section, not one flag wearing two hats.</summary>
    [Fact]
    public async Task GivenTheListFetchFails_WhenLoadingTheDirectory_ThenOnlyTheListsAreReportedIncomplete()
    {
        var directory = await Directory(BrevoWhere(listsFail: true)).ExecuteAsync();

        Assert.False(directory.BrevoListsComplete);
        Assert.True(directory.BrevoTemplatesComplete);
        Assert.Single(directory.BrevoTemplates);
    }

    /// <summary>The dev directory exists to make an unreachable section observable locally - so it must be.</summary>
    [Fact]
    public async Task GivenTheDevDirectoryIsSetToFailTemplates_WhenLoadingTheDirectory_ThenOnlyTheTemplatesAreIncomplete()
    {
        var options = TestData.Options();
        options.Dev.BrevoDirectorySize = 60;
        options.Dev.BrevoDirectoryFailure = "templates";
        var dev = new DevBrevoDirectoryAdapter(Options.Create(options));
        var artifacts = Substitute.For<IListArtifactsPort>();
        artifacts.ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ArtifactInfo>());

        var directory = await new GetIntegrationDirectoryUseCase(dev, Substitute.For<IListReportTemplatesPort>(), artifacts,
            Options.Create(options), NullLogger<GetIntegrationDirectoryUseCase>.Instance).ExecuteAsync();

        Assert.True(directory.BrevoConfigured);
        Assert.Equal(60, directory.BrevoLists.Count);
        Assert.True(directory.BrevoListsComplete);
        Assert.False(directory.BrevoTemplatesComplete);
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
