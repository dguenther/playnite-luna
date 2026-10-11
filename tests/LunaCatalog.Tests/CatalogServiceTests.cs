using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-service-test-" + Guid.NewGuid().ToString("N"));
    private string CachePath => Path.Combine(directory, "catalog-US.json");
    private readonly SampleProvider provider = new();
    private DateTimeOffset now = RuntimeSamples.Time;
    private JsonCatalogCache Cache() => new(directory, () => now);
    private CatalogService Service() => new(provider, Cache(), utcNow: () => now);

    [Fact]
    public async Task StartupLoadsOfflineWithoutNetworkAndExposesAgeAndLastRefresh()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var service = Service();
        var state = await service.LoadAsync(CancellationToken.None);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(CatalogDataSource.Cache, state.Source);
        Assert.False(state.IsStale);
        Assert.Equal(RuntimeSamples.Time, state.LastSuccessfulRefresh);
        now = now.AddDays(2);
        Assert.True(service.State.IsStale);
        Assert.Equal(5, service.State.Catalog!.Games.Count);
    }

    [Fact]
    public async Task NoCacheYetAndMalformedCacheAreDistinctStates()
    {
        var empty = await Service().LoadAsync(CancellationToken.None);
        Assert.False(empty.HasCatalog);
        Assert.False(empty.IsStale);
        Assert.Null(empty.LastSuccessfulRefresh);
        Assert.Null(empty.Error);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(CachePath, "bad cache");
        var invalid = await Service().LoadAsync(CancellationToken.None);
        Assert.False(invalid.HasCatalog);
        Assert.Equal(CatalogErrorKind.Cache, invalid.Error!.Kind);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SuccessfulRefreshPersistsBeforePublishingAndSurvivesServiceRestart()
    {
        var service = Service();
        var state = await service.RefreshAsync(CancellationToken.None);
        Assert.Equal(CatalogDataSource.Network, state.Source);
        Assert.Null(state.Error);
        Assert.False(state.IsStale);
        Assert.Null(state.Changes); // No comparison against a missing baseline.
        state.Catalog!.Games.Clear();
        Assert.Equal(5, service.State.Catalog!.Games.Count); // Consumer edits cannot change the baseline.
        var restarted = await Service().LoadAsync(CancellationToken.None);
        Assert.Equal(5, restarted.Catalog!.Games.Count);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData("zero games")]
    [InlineData("partial queries")]
    [InlineData("pagination")]
    [InlineData("region")]
    [InlineData("native conflict")]
    [InlineData("network")]
    public async Task FailedRefreshRetainsCacheBytesTimestampAndGamesAndProducesNoRemovalList(string failure)
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        provider.Fetch = _ =>
        {
            if (failure == "network") throw new HttpRequestException("sensitive transport error");
            var snapshot = RuntimeSamples.Snapshot();
            if (failure == "zero games") snapshot.Games.Clear();
            if (failure == "partial queries") snapshot.Queries.RemoveAt(3);
            if (failure == "pagination") snapshot.Queries[0].PaginationStatus = "UnresolvedContinuation";
            if (failure == "region") snapshot.Region = "CA";
            if (failure == "native conflict") snapshot = RuntimeSamples.ChangeGame(snapshot, "B0GFWDM8NP",
                g => g.Id = "amzn1.adg.product.00000000-0000-0000-0000-000000000099");
            return Task.FromResult(snapshot);
        };
        var state = await Service().RefreshAsync(CancellationToken.None);
        Assert.Equal(5, state.Catalog!.Games.Count);
        Assert.Equal(RuntimeSamples.Time, state.LastSuccessfulRefresh);
        Assert.NotNull(state.Error);
        Assert.DoesNotContain(state.Error.Diagnostics, d => d.Contains("sensitive"));
        Assert.True(state.IsStale);
        Assert.Null(state.Changes);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task SuccessfulRefreshReportsAndPersistsAsinToNativeIdentityMigration()
    {
        var fallback = RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Id = g.Asin!);
        await Cache().SaveAsync(CatalogValidator.ValidateSnapshot(fallback, "US", now), CancellationToken.None);
        var state = await Service().RefreshAsync(CancellationToken.None);
        Assert.Null(state.Error);
        Assert.Empty(state.Changes!.Added);
        Assert.Empty(state.Changes.Removed);
        Assert.Equal("B0GFWDM8NP", Assert.Single(state.Changes.IdentityMigrations).Previous.Id);
        var restarted = await Service().LoadAsync(CancellationToken.None);
        Assert.StartsWith("amzn1.adg.product.", restarted.Catalog!.Games.Single(g => g.Asin == "B0GFWDM8NP").Id);
    }

    private sealed class CancelBeforeSave : ICatalogCache
    {
        private readonly ICatalogCache cache;
        private readonly CancellationTokenSource cancellation;
        internal CancelBeforeSave(ICatalogCache cache, CancellationTokenSource cancellation)
        { this.cache = cache; this.cancellation = cancellation; }
        public Task<CatalogCacheReadResult> LoadAsync(string region, CancellationToken token) => cache.LoadAsync(region, token);
        public Task SaveAsync(ValidatedCatalog catalog, CancellationToken token)
        { cancellation.Cancel(); return cache.SaveAsync(catalog, token); }
    }

    [Fact]
    public async Task CancellationAfterFetchCannotPublishAnUncommittedCandidate()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        provider.Fetch = _ => Task.FromResult(RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Title = "new title"));
        using var cancellation = new CancellationTokenSource();
        var service = new CatalogService(provider, new CancelBeforeSave(Cache(), cancellation), utcNow: () => now);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(cancellation.Token));
        Assert.Contains(service.State.Catalog!.Games, g => g.Title == "Alan Wake 2");
        Assert.Null(service.State.Changes);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task WriteFailureNeverPublishesCandidateAndLaterRefreshRecovers()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        Directory.CreateDirectory(CachePath + ".bak");
        provider.Fetch = _ => Task.FromResult(RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Title = "new title"));
        var service = Service();
        var failed = await service.RefreshAsync(CancellationToken.None);
        Assert.Equal(CatalogErrorKind.Storage, failed.Error!.Kind);
        Assert.Contains(failed.Catalog!.Games, g => g.Title == "Alan Wake 2");
        Assert.Null(failed.Changes);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
        Directory.Delete(CachePath + ".bak");
        var success = await service.RefreshAsync(CancellationToken.None);
        Assert.Null(success.Error);
        Assert.False(success.IsStale);
        Assert.Equal("new title", Assert.Single(success.Changes!.Updated).Current.Title);
        Assert.Contains((await Cache().LoadAsync("US", CancellationToken.None)).Catalog!.Games, g => g.Title == "new title");
    }

    [Fact]
    public async Task CancellationDuringFetchPropagatesAndLeavesPriorCatalogAvailable()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return RuntimeSamples.Snapshot();
        };
        using var cancellation = new CancellationTokenSource();
        var service = Service();
        var refresh = service.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.State.IsRefreshing);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(service.State.IsRefreshing);
        Assert.Equal(CatalogErrorKind.Cancelled, service.State.Error!.Kind);
        Assert.Null(service.State.Changes);
        Assert.Equal(5, service.State.Catalog!.Games.Count);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task OverlappingRefreshesAreSerializedAndEachUsesTheAcceptedBaseline()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async _ => { started.TrySetResult(); await finish.Task; return RuntimeSamples.Snapshot(); };
        var service = Service();
        var first = service.RefreshAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, provider.Calls);
        finish.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, provider.Calls);
        Assert.Empty(service.State.Changes!.Added);
        Assert.Empty(service.State.Changes.Removed);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task RecoveredBackupIsShownAsStaleAndCanBeRefreshed()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        await File.WriteAllTextAsync(CachePath, "bad primary");
        var service = Service();
        var state = await service.LoadAsync(CancellationToken.None);
        Assert.True(state.HasCatalog);
        Assert.True(state.IsStale);
        Assert.NotEmpty(state.CacheDiagnostics);
        Assert.Null(state.Error);
        Assert.Equal(0, provider.Calls);
        Assert.False((await service.RefreshAsync(CancellationToken.None)).IsStale);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = CatalogQuery.UsQueries.Single(q => q.Query == (string?)body.SelectToken("searchContext.query"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ParserTests.Fixture(query.Name)) };
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        internal Func<CancellationToken, Task<HttpResponseMessage>> Respond = _ => throw new HttpRequestException("sensitive transport error");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Respond(token);
    }

    private sealed class FailingBodyStream : MemoryStream
    {
        private readonly bool throwOnRead;
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal FailingBodyStream(bool throwOnRead = false) { this.throwOnRead = throwOnRead; }
        public override bool CanSeek => false;
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        {
            Started.TrySetResult();
            if (throwOnRead) throw new IOException("sensitive response stream error");
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
    }

    [Theory]
    [InlineData("connection", CatalogErrorKind.Network)]
    [InlineData("header timeout", CatalogErrorKind.Network)]
    [InlineData("body timeout", CatalogErrorKind.Network)]
    [InlineData("body read", CatalogErrorKind.Network)]
    [InlineData("401", CatalogErrorKind.Network)]
    [InlineData("403", CatalogErrorKind.Network)]
    [InlineData("429", CatalogErrorKind.Network)]
    [InlineData("500", CatalogErrorKind.Network)]
    [InlineData("503", CatalogErrorKind.Network)]
    [InlineData("retry deferral", CatalogErrorKind.Network)]
    [InlineData("invalid JSON", CatalogErrorKind.Validation)]
    [InlineData("invalid UTF-8", CatalogErrorKind.Validation)]
    [InlineData("oversized body", CatalogErrorKind.Validation)]
    public async Task HttpProviderFailuresAreClassifiedAndPreserveThePreviousCatalog(string failure, CatalogErrorKind expected)
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        var handler = new FailureHandler
        {
            Respond = async token =>
            {
                if (failure == "connection") throw new HttpRequestException("sensitive transport error");
                if (failure == "header timeout") await Task.Delay(Timeout.Infinite, token);
                if (int.TryParse(failure, out var status))
                {
                    var response = new HttpResponseMessage((HttpStatusCode)status);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                    return response;
                }
                if (failure == "retry deferral")
                {
                    var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
                    return response;
                }
                HttpContent content = failure switch
                {
                    "body timeout" => new StreamContent(new FailingBodyStream()),
                    "body read" => new StreamContent(new FailingBodyStream(throwOnRead: true)),
                    "invalid UTF-8" => new ByteArrayContent(new byte[] { 0xc3, 0x28 }),
                    "oversized body" => new ByteArrayContent(new byte[LunaCatalogProvider.MaximumResponseBytes + 1]),
                    _ => new StringContent("invalid JSON")
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
        };
        using var client = new HttpClient(handler);
        var httpProvider = new LunaCatalogProvider(client, "TestBrowser", "154", TimeSpan.FromMilliseconds(50));
        var service = new CatalogService(httpProvider, Cache(), utcNow: () => now);
        var state = await service.RefreshAsync(CancellationToken.None);
        Assert.Equal(expected, state.Error!.Kind);
        Assert.DoesNotContain(state.Error.Diagnostics, d => d.Contains("sensitive"));
        Assert.Equal(5, state.Catalog!.Games.Count);
        Assert.Equal(RuntimeSamples.Time, state.LastSuccessfulRefresh);
        Assert.Equal(CatalogDataSource.Cache, state.Source);
        Assert.True(state.IsStale);
        Assert.False(state.IsRefreshing);
        Assert.Null(state.Changes);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task CallerCancellationThroughHttpProviderRemainsCancelledAndPreservesCache()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(CachePath);
        using var body = new FailingBodyStream();
        var handler = new FailureHandler
        {
            Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) })
        };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var service = new CatalogService(new LunaCatalogProvider(client, "TestBrowser", "154"), Cache(), utcNow: () => now);
        var refresh = service.RefreshAsync(cancellation.Token);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Equal(CatalogErrorKind.Cancelled, service.State.Error!.Kind);
        Assert.False(service.State.IsRefreshing);
        Assert.Null(service.State.Changes);
        Assert.Equal(5, service.State.Catalog!.Games.Count);
        Assert.Equal(RuntimeSamples.Time, service.State.LastSuccessfulRefresh);
        Assert.Equal(before, await File.ReadAllBytesAsync(CachePath));
    }

    [Fact]
    public async Task ExistingHttpProviderDownloadsRepresentativeApiDataIntoProductionCache()
    {
        var handler = new FixtureHandler();
        using var client = new HttpClient(handler);
        var liveProvider = new LunaCatalogProvider(client, "TestBrowser", "154");
        var service = new CatalogService(liveProvider, new JsonCatalogCache(directory));
        var state = await service.RefreshAsync(CancellationToken.None);
        Assert.Null(state.Error);
        Assert.Equal(4, handler.Calls);
        Assert.Equal(5, state.Catalog!.Games.Count);
        Assert.All(state.Catalog.Queries, q => Assert.Equal(200, q.HttpStatus));
        Assert.True((await new JsonCatalogCache(directory).LoadAsync("US", CancellationToken.None)).Catalog!.IsComplete);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
