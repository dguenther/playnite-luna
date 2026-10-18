using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using LunaCatalogBrowser;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class BrowserSessionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-session-" + Guid.NewGuid().ToString("N"));
    private readonly SampleProvider provider = new();
    private readonly ImportTestDatabase database = new();

    private async Task<CatalogBrowserSession> Session(Func<string?, CancellationToken, Task<byte[]?>>? artwork = null)
    {
        var cache = new JsonCatalogCache(directory, () => RuntimeSamples.Time);
        await cache.SaveAsync(RuntimeSamples.Validated(), default);
        var imports = new CatalogImportService(database, new JsonImportMappingStore(directory), artwork, utcNow: () => RuntimeSamples.Time);
        return new CatalogBrowserSession(new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => RuntimeSamples.Time), imports: imports));
    }

    [Fact]
    public async Task ReopeningRetainsLocalFiltersSelectionAndCacheWithoutApiCalls()
    {
        using var session = await Session(); var host = new object(); await session.AttachAsync(host);
        var bytes = File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"));
        session.Model.SearchText = "Alan"; session.Model.Category = CatalogCategory.Premium;
        session.Model.TitleSort = CatalogTitleSort.Descending; session.Model.SelectedGame = session.Model.VisibleGames.Single();
        var id = session.Model.SelectedGame.Id;
        session.Detach(host); await session.AttachAsync(host);
        Assert.Equal("Alan", session.Model.SearchText); Assert.Equal(CatalogCategory.Premium, session.Model.Category);
        Assert.Equal(CatalogTitleSort.Descending, session.Model.TitleSort); Assert.Equal(id, session.Model.SelectedGame!.Id);
        Assert.Equal(0, provider.Calls); Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "catalog-US.json")));
    }

    [Fact]
    public async Task ClosingOneHostRetainsWorkUntilTheLastHostLeaves()
    {
        using var session = await Session(); var sidebar = new object(); var window = new object();
        await session.AttachAsync(sidebar); await session.AttachAsync(window);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return RuntimeSamples.Snapshot(); };
        var refresh = session.Model.RefreshAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Detach(sidebar); Assert.True(session.HasActiveHosts); Assert.True(session.Model.IsBusy); Assert.False(refresh.IsCompleted);
        session.Detach(window); await refresh.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(session.HasActiveHosts); Assert.False(session.Model.IsBusy); Assert.Equal(5, session.Model.TotalCount);
    }

    [Fact]
    public async Task RapidReopenWaitsForCancelledWorkAndRestoresOfflineState()
    {
        using var session = await Session(); var host = new object(); await session.AttachAsync(host);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token => { started.SetResult(); await release.Task; token.ThrowIfCancellationRequested(); return RuntimeSamples.Snapshot(); };
        var refresh = session.Model.RefreshAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Detach(host); var reopen = session.AttachAsync(host); Assert.False(reopen.IsCompleted);
        release.SetResult(); await Task.WhenAll(refresh, reopen).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.HasActiveHosts); Assert.False(session.Model.IsBusy); Assert.Equal(5, session.Model.TotalCount);
        Assert.True(session.Model.CanRefresh); Assert.Equal(RuntimeSamples.Time, session.Model.LastSuccessfulRefresh); Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task TwoHostsCannotQueueDuplicateImportsAndLastDetachCancelsTheBatch()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var session = await Session(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return null; });
        var sidebar = new object(); var window = new object(); await session.AttachAsync(sidebar); await session.AttachAsync(window);
        var import = session.Model.ImportAllAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.Model.ImportAllAsync(default); session.Detach(window); Assert.True(session.Model.IsBusy);
        session.Detach(sidebar); await import.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("5 cancelled", session.Model.ImportSummaryText); Assert.Equal(0, database.Adds);
    }

    [Fact]
    public async Task ShutdownRetainsCommittedImportsAndRejectsNewHosts()
    {
        var session = await Session(); await session.AttachAsync(new object());
        database.AfterAdd = session.Dispose;
        await session.Model.ImportAllAsync(default);
        Assert.Single(database.Entries); Assert.Equal(1, database.Adds);
        Assert.Contains("1 added", session.Model.ImportSummaryText); Assert.Contains("4 cancelled", session.Model.ImportSummaryText);
        Assert.False(session.HasActiveHosts); session.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.AttachAsync(new object()));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
