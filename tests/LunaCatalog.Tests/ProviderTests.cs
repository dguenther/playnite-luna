using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class ProviderTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            return Respond(request, token);
        }
    }

    private static LunaCatalogProvider Provider(Handler handler, TimeSpan? timeout = null) =>
        new LunaCatalogProvider(new HttpClient(handler), "TestBrowser/154", "154.0.8037.57", timeout);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    public async Task AuthenticationAndRateLimitErrorsAreNotRetried(int status)
    {
        var handler = new Handler { Respond = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)) };
        var result = await Provider(handler).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(result.ResponseValid);
        Assert.Empty(result.Games);
        Assert.Equal(CatalogFetchFailureKind.Network, result.FailureKind);
        Assert.Equal(status, result.HttpStatus);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ServerFailureHasOnlyOneRetry()
    {
        var handler = new Handler { Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)) };
        var result = await Provider(handler).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(result.ResponseValid);
        Assert.Equal(CatalogFetchFailureKind.Network, result.FailureKind);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task TimeoutIsNotAValidEmptyCatalog()
    {
        var handler = new Handler
        {
            Respond = async (_, token) => { await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(HttpStatusCode.OK); }
        };
        var result = await Provider(handler, TimeSpan.FromMilliseconds(25)).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(result.ResponseValid);
        Assert.Equal(CatalogFetchFailureKind.Network, result.FailureKind);
        Assert.Contains(result.Diagnostics, d => d.Contains("timed out"));
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        var handler = new Handler { Respond = (_, token) => Task.FromCanceled<HttpResponseMessage>(token) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider(handler).FetchQueryAsync(CatalogQuery.UsQueries[0], cancellation.Token));
    }

    [Fact]
    public async Task RequestsUseUsMarketplaceAndCarryNoAccountAuthentication()
    {
        var handler = new Handler
        {
            Respond = async (request, _) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal(new Uri("https://proxy-prod.us-east-1.tempo.digital.a2z.com/getPage"), request.RequestUri);
                Assert.Equal(new[] { "ATVPDKIKX0DER" }, request.Headers.GetValues("x-amz-marketplace-id"));
                Assert.False(request.Headers.Contains("Cookie"));
                Assert.Null(request.Headers.Authorization);
                var body = JObject.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("all_games", (string?)body.SelectToken("searchContext.query"));
                Assert.Equal("text/plain", request.Content.Headers.ContentType!.MediaType);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ParserTests.Fixture("AllGames")) };
            }
        };
        var result = await Provider(handler).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.True(result.ResponseValid);
        Assert.Equal(CatalogFetchFailureKind.None, result.FailureKind);
        Assert.Equal(5, result.Games.Count);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutExposingRawBackendData()
    {
        var handler = new Handler { Respond = (_, _) => throw new HttpRequestException("potentially sensitive transport details") };
        var result = await Provider(handler).FetchQueryAsync(CatalogQuery.UsQueries[0], CancellationToken.None);
        Assert.False(result.ResponseValid);
        Assert.Equal(CatalogFetchFailureKind.Network, result.FailureKind);
        Assert.DoesNotContain("sensitive", string.Join(" ", result.Diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetailTransportFailuresPreserveTheirTypeAndSanitizedDiagnostics(bool timeout)
    {
        var handler = new Handler
        {
            Respond = async (_, token) =>
            {
                if (timeout) await Task.Delay(Timeout.Infinite, token);
                throw new HttpRequestException("sensitive transport details");
            }
        };
        using var client = new HttpClient(handler);
        var provider = new LunaCatalogProvider(client, "TestBrowser", "154", TimeSpan.FromMilliseconds(25));
        var result = await provider.FetchDetailAsync(RuntimeSamples.Snapshot().Games[0], CancellationToken.None);
        Assert.False(result.ResponseValid);
        Assert.Equal(CatalogFetchFailureKind.Network, result.FailureKind);
        Assert.DoesNotContain("sensitive", string.Join(" ", result.Diagnostics));
        if (timeout) Assert.Contains(result.Diagnostics, d => d.Contains("timed out"));
    }

    [Fact]
    public async Task UninvestigatedRegionsAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Provider(new Handler()).FetchAsync("CA", CancellationToken.None));
    }
}
