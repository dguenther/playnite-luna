using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using LunaCatalogBrowser;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class BrowserModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-browser-test-" + Guid.NewGuid().ToString("N"));
    private readonly SampleProvider provider = new();
    private JsonCatalogCache Cache() => new(directory, () => RuntimeSamples.Time);
    private CatalogBrowserModel Model() => new(new CatalogService(provider, Cache(), utcNow: () => RuntimeSamples.Time));

    [Fact]
    public async Task StartupBrowsesCachedRepresentativeDataWithoutContactingTheApi()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var model = Model();
        await model.LoadAsync(CancellationToken.None);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(5, model.TotalCount);
        Assert.Equal(5, model.VisibleCount);
        Assert.Equal(5, model.VisibleGames.Select(g => g.Id).Distinct().Count());
        Assert.Equal(RuntimeSamples.Time, model.LastSuccessfulRefresh);
        Assert.Contains("offline", model.StatusText);
        Assert.Empty(model.ErrorText);
        var alanWake = model.VisibleGames.Single(g => g.Title == "Alan Wake 2");
        Assert.Equal(new[] { "Premium", "Standard" }, alanWake.Memberships);
        Assert.NotNull(alanWake.CoverUrl);
    }

    [Theory]
    [InlineData(CatalogCategory.All, 5)]
    [InlineData(CatalogCategory.Standard, 2)]
    [InlineData(CatalogCategory.Premium, 2)]
    [InlineData(CatalogCategory.GameNight, 2)]
    public async Task LocalCategorySearchAndSortingPreserveTheCatalogAndCache(CatalogCategory category, int count)
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var before = await File.ReadAllBytesAsync(Path.Combine(directory, "catalog-US.json"));
        var model = Model();
        await model.LoadAsync(CancellationToken.None);
        model.Category = category;
        Assert.Equal(count, model.VisibleCount);
        Assert.All(model.VisibleGames, g => Assert.True(category == CatalogCategory.All || g.Memberships.Contains(category.ToString())));
        var titles = model.VisibleGames.Select(g => g.Title).ToArray();
        Assert.Equal(titles.OrderBy(t => t, StringComparer.OrdinalIgnoreCase), titles);
        model.TitleSort = CatalogTitleSort.Descending;
        Assert.Equal(titles.Reverse(), model.VisibleGames.Select(g => g.Title));
        model.SearchText = "  ALAN wake  ";
        Assert.Equal(category == CatalogCategory.GameNight ? 0 : 1, model.VisibleCount);
        model.SearchText = "no matching game";
        Assert.Equal(0, model.VisibleCount);
        Assert.Contains("No games match", model.EmptyText);
        model.Category = CatalogCategory.All; model.SearchText = "";
        Assert.Equal(5, model.VisibleCount);
        Assert.Equal(5, model.TotalCount);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(directory, "catalog-US.json")));
    }

    [Fact]
    public async Task TitleCollisionsRetainDistinctIdsAndHaveDeterministicOrdering()
    {
        var snapshot = RuntimeSamples.Snapshot();
        foreach (var game in snapshot.Games.ToArray()) snapshot = RuntimeSamples.ChangeGame(snapshot, game.Asin!, g => g.Title = "Same title");
        provider.Fetch = _ => Task.FromResult(snapshot);
        var model = Model();
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal(5, model.VisibleCount);
        var ids = model.VisibleGames.Select(g => g.Id).ToArray();
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        model.TitleSort = CatalogTitleSort.Descending;
        Assert.Equal(ids, model.VisibleGames.Select(g => g.Id));
    }

    [Fact]
    public async Task EmptyAndMalformedCacheHaveActionableStatesWithoutAnAutomaticFetch()
    {
        var empty = Model();
        await empty.LoadAsync(CancellationToken.None);
        Assert.Equal(0, empty.VisibleCount);
        Assert.Contains("choose Refresh", empty.StatusText);
        Assert.Empty(empty.ErrorText);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "catalog-US.json"), "invalid cache");
        var malformed = Model();
        await malformed.LoadAsync(CancellationToken.None);
        Assert.NotEmpty(malformed.ErrorText);
        Assert.Contains("unavailable", malformed.StatusText);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SelectionSurvivesARefreshByIdAndClearsWhenFilteredOut()
    {
        var model = Model();
        await model.RefreshAsync(CancellationToken.None);
        model.SelectedGame = model.VisibleGames.Single(g => g.Title == "Alan Wake 2");
        var selected = model.SelectedGame;
        Assert.True(model.CanOpenSelected);
        provider.Fetch = _ => Task.FromResult(RuntimeSamples.ChangeGame(RuntimeSamples.Snapshot(), "B0GFWDM8NP", g => g.Title = "Alan Wake revised"));
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal(selected.Id, model.SelectedGame!.Id);
        Assert.Equal("Alan Wake revised", model.SelectedGame.Title);
        model.Category = CatalogCategory.GameNight;
        Assert.Null(model.SelectedGame);
        Assert.False(model.CanOpenSelected);
        Assert.Throws<ArgumentException>(() => model.SelectedGame = selected);
    }

    [Fact]
    public async Task SlowRefreshKeepsBrowsingAvailableAndCancelRestoresTheSavedView()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var model = Model();
        await model.LoadAsync(CancellationToken.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return RuntimeSamples.Snapshot(); };
        var refresh = model.RefreshAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsBusy);
        Assert.False(model.CanRefresh);
        model.SearchText = "Alan";
        Assert.Equal("Alan Wake 2", Assert.Single(model.VisibleGames).Title);
        await model.RefreshAsync(CancellationToken.None); // A second click cannot enqueue another refresh.
        Assert.Equal(1, provider.Calls);
        model.CancelCurrentOperation();
        await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.IsBusy);
        Assert.True(model.CanRefresh);
        Assert.Equal(RuntimeSamples.Time, model.LastSuccessfulRefresh);
        Assert.Contains("cancelled", model.StatusText);
        Assert.Equal("Alan Wake 2", Assert.Single(model.VisibleGames).Title);
    }

    [Fact]
    public async Task UnexpectedFailureDoesNotExposeRawDetailsOrLoseCachedGames()
    {
        await Cache().SaveAsync(RuntimeSamples.Validated(), CancellationToken.None);
        var reported = "";
        provider.Fetch = _ => throw new InvalidOperationException("sensitive exception details");
        var model = new CatalogBrowserModel(new CatalogService(provider, Cache(), utcNow: () => RuntimeSamples.Time), message => reported = message);
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal(5, model.VisibleCount);
        Assert.False(model.IsBusy);
        Assert.DoesNotContain("sensitive", model.ErrorText + reported);
        Assert.Contains("InvalidOperationException", reported);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal int Calls;
        internal bool Offline;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            if (Offline) throw new HttpRequestException("sensitive network details");
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            var query = CatalogQuery.UsQueries.Single(q => q.Query == (string?)body.SelectToken("searchContext.query"));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ParserTests.Fixture(query.Name)) };
        }
    }

    [Fact]
    public async Task BrowserUsesTheRealHttpProviderAndRetainsTheDownloadedCatalogWhileOffline()
    {
        var handler = new FixtureHandler();
        using var client = new HttpClient(handler);
        var model = new CatalogBrowserModel(new CatalogService(new LunaCatalogProvider(client, "TestBrowser", "154"), new JsonCatalogCache(directory)));
        await model.LoadAsync(CancellationToken.None);
        Assert.Equal(0, handler.Calls);
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal(4, handler.Calls);
        Assert.Equal(5, model.VisibleCount);
        Assert.Empty(model.ErrorText);
        var timestamp = model.LastSuccessfulRefresh;
        handler.Offline = true;
        await model.RefreshAsync(CancellationToken.None);
        Assert.Equal(5, model.VisibleCount);
        Assert.Equal(timestamp, model.LastSuccessfulRefresh);
        Assert.True(model.IsStale);
        Assert.Contains("showing saved catalog", model.StatusText);
        Assert.DoesNotContain("sensitive", model.ErrorText);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
