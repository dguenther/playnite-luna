using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using LunaCatalogBrowser;
using Xunit;

namespace LunaCatalog.Tests;

public sealed class BrowserImportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-browser-import-" + Guid.NewGuid().ToString("N"));
    private readonly ImportTestDatabase database = new();
    private readonly SampleProvider provider = new();
    private async Task<CatalogBrowserModel> Model(Func<string?, CancellationToken, Task<byte[]?>>? artwork = null)
    {
        var cache = new JsonCatalogCache(directory, () => RuntimeSamples.Time); await cache.SaveAsync(RuntimeSamples.Validated(), default);
        var imports = new CatalogImportService(database, new JsonImportMappingStore(directory), artwork, utcNow: () => RuntimeSamples.Time);
        var model = new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => RuntimeSamples.Time), imports: imports);
        await model.LoadAsync(default); return model;
    }

    [Fact]
    public async Task SelectedImportUsesMultipleIdentitiesAndUpdatesStatusesWithoutFetching()
    {
        var model = await Model(); Assert.All(model.VisibleGames, g => Assert.Equal(CatalogImportStatus.NotImported, g.ImportStatus));
        model.SetSelectedGames(model.VisibleGames.Take(2)); Assert.True(model.CanImportSelected);
        await model.ImportSelectedAsync(default);
        Assert.Equal(2, database.Adds); Assert.Equal(2, model.VisibleGames.Count(g => g.ImportStatus == CatalogImportStatus.Imported));
        Assert.Equal(0, provider.Calls); Assert.Contains("2 added", model.ImportSummaryText);
        await model.ImportSelectedAsync(default); Assert.Equal(2, database.Adds); Assert.Contains("2 already imported", model.ImportSummaryText);
    }

    [Fact]
    public async Task ImportAllIncludesHiddenCatalogGamesAndRetainsCatalogCacheBytes()
    {
        var model = await Model(); var original = File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"));
        model.SearchText = "Alan"; Assert.Equal(1, model.VisibleCount); Assert.Equal("Import All (5)", model.ImportAllText);
        await model.ImportAllAsync(default); Assert.Equal(5, database.Adds); Assert.Equal(1, model.VisibleCount);
        Assert.Equal(CatalogImportStatus.Imported, model.VisibleGames[0].ImportStatus);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"))); Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task SelectionSurvivesSortAndRefreshAndDropsHiddenSelections()
    {
        var model = await Model(); model.SetSelectedGames(model.VisibleGames.Take(2)); var ids = model.SelectedGames.Select(g => g.Id).ToArray();
        model.TitleSort = CatalogTitleSort.Descending; Assert.Equal(ids.OrderBy(x => x), model.SelectedGames.Select(g => g.Id).OrderBy(x => x));
        await model.RefreshAsync(default); Assert.Equal(ids.OrderBy(x => x), model.SelectedGames.Select(g => g.Id).OrderBy(x => x));
        model.SearchText = "Alan"; Assert.Single(model.SelectedGames); Assert.Contains("1 selected", model.SelectionText);
        model.SearchText = "absent"; Assert.Empty(model.SelectedGames); Assert.False(model.CanImportSelected);
    }

    [Fact]
    public async Task MalformedMappingsDisableImportsWhileBrowsingAndRefreshRemainAvailable()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "imports-US.json"), "bad state");
        var model = await Model(); Assert.Equal(5, model.VisibleCount); Assert.True(model.CanRefresh); Assert.False(model.CanImportAll);
        Assert.Contains("Restore", model.ImportErrorText); Assert.All(model.VisibleGames, g => Assert.Equal(CatalogImportStatus.Unavailable, g.ImportStatus));
        await model.ImportAllAsync(default); Assert.Equal(0, database.Adds);
    }

    [Fact]
    public async Task SlowImportsAllowLocalBrowsingAndCancelWithoutWritingOrQueuingDuplicates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = await Model(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return null; });
        var pending = model.ImportAllAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsBusy); Assert.False(model.CanRefresh); Assert.False(model.CanImportAll);
        model.SearchText = "Alan"; Assert.Single(model.VisibleGames);
        await model.ImportAllAsync(default); model.CancelCurrentOperation(); await pending;
        Assert.False(model.IsBusy); Assert.Equal(0, database.Adds); Assert.Contains("5 cancelled", model.ImportSummaryText); Assert.Single(model.VisibleGames);
    }

    [Fact]
    public async Task ManualDeletionChangesStatusOnReconciliationAndAllowsReimport()
    {
        var model = await Model(); model.SelectedGame = model.VisibleGames[0]; await model.ImportSelectedAsync(default);
        database.Entries.Clear(); await model.ReconcileImportsAsync(default);
        Assert.Equal(CatalogImportStatus.NotImported, model.SelectedGame!.ImportStatus);
        await model.ImportSelectedAsync(default); Assert.Equal(2, database.Adds); Assert.Single(database.Entries);
    }

    [Fact]
    public async Task ConfirmedImportIntentWaitsForHostActivationReconciliation()
    {
        var model = await Model(); using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        database.BeforeRead = () => { started.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var check = model.ReconcileImportsAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var import = model.ImportAllAsync(default); Assert.False(import.IsCompleted);
        release.Set(); await Task.WhenAll(check, import); Assert.Equal(5, database.Adds);
    }

    [Fact]
    public async Task ClosingOrCancellingDuringActivationCheckDoesNotStartPendingImport()
    {
        var model = await Model(); using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        database.BeforeRead = () => { started.TrySetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        var check = model.ReconcileImportsAsync(default); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var import = model.ImportAllAsync(default); Assert.False(import.IsCompleted);
        model.CancelCurrentOperation(); release.Set(); await Task.WhenAll(check, import);
        Assert.Equal(0, database.Adds); Assert.Empty(database.Entries); Assert.False(model.IsBusy);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
