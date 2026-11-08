using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using LunaCatalogBrowser;
using Newtonsoft.Json;
using Xunit;

namespace LunaCatalog.Tests;

internal sealed class AutomationDatabase : ICatalogAutomationDatabase
{
    internal readonly ImportTestDatabase Data = new();
    internal readonly HashSet<Guid> Played = new(), Busy = new(), Expired = new();
    internal Action<Guid>? BeforeRemove;
    internal Action<Guid, bool>? BeforeExpiryCheck, BeforeSetExpired, AfterSetExpired;
    public IReadOnlyList<LibraryGameIdentity> ReadIdentities() => Data.ReadIdentities();
    public string? AddGame(ImportCandidate game, Guid id, byte[]? cover) => Data.AddGame(game, id, cover);
    public IReadOnlyList<CatalogLibraryGame> ReadLibraryGames() => Data.Entries.Select(kv =>
        new CatalogLibraryGame(kv.Value.Identity, Played.Contains(kv.Key), Busy.Contains(kv.Key), Expired.Contains(kv.Key))).ToArray();
    private bool Owned(Guid id, string marker) => Data.Entries.TryGetValue(id, out var entry) &&
        entry.Identity.PluginId == LunaImportIdentity.PluginId && entry.Identity.GameId == marker;
    public bool SetExpired(Guid id, string marker, bool expired, Action? beforeChange = null)
    {
        if (!Owned(id, marker)) return false;
        BeforeExpiryCheck?.Invoke(id, expired);
        if (Expired.Contains(id) == expired) return false;
        beforeChange?.Invoke();
        BeforeSetExpired?.Invoke(id, expired);
        var changed = expired ? Expired.Add(id) : Expired.Remove(id);
        AfterSetExpired?.Invoke(id, expired);
        return changed;
    }
    public bool RemoveUnplayed(Guid id, string marker, Func<bool> mayRemove)
    {
        BeforeRemove?.Invoke(id);
        return Owned(id, marker) && !Played.Contains(id) && !Busy.Contains(id) && mayRemove() && Data.Entries.Remove(id);
    }
}

public sealed class CatalogAutomationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-automation-" + Guid.NewGuid().ToString("N"));
    private readonly AutomationDatabase database = new();
    private readonly DateTimeOffset now = RuntimeSamples.Time.AddDays(1);
    private JsonCatalogAutomationStore Store => new(directory);
    private CatalogAutomation Create() => new(database, new CatalogImportService(database, new JsonImportMappingStore(directory), utcNow: () => now), Store, () => now);
    private static string Alien => RuntimeSamples.Validated().Games.Single(g => g.Title == "Alien: Isolation").Asin!;
    private ValidatedCatalog Catalog(int minute, bool remove = false, bool add = false, bool fallbackId = false, string[]? arrivalMemberships = null) =>
        CatalogValidator.ValidateSnapshot(Snapshot(minute, remove, add, fallbackId, arrivalMemberships), "US", now);
    private CatalogSnapshot Snapshot(int minute, bool remove = false, bool add = false, bool fallbackId = false, string[]? arrivalMemberships = null)
    {
        arrivalMemberships ??= new[] { "Premium" };
        var snapshot = RuntimeSamples.Snapshot();
        foreach (var query in snapshot.Queries)
        {
            if (remove) query.Games.RemoveAll(g => g.Asin == Alien);
            if (fallbackId) foreach (var game in query.Games.Where(g => g.Asin == Alien)) game.Id = game.Asin!;
            if (add && (query.Name == "AllGames" || arrivalMemberships.Contains(query.Name)))
            {
                var game = JsonConvert.DeserializeObject<LunaGame>(JsonConvert.SerializeObject(query.Games[0]))!;
                game.Id = "amzn1.adg.product.00000000-0000-4000-8000-000000000001"; game.Asin = "B000NEW001"; game.Title = "New arrival";
                game.Url = "https://luna.amazon.com/game/new-arrival/" + game.Asin;
                query.Games.Add(game);
            }
            query.TilesObserved = query.Games.Count;
        }
        snapshot = CatalogSnapshot.Merge(snapshot.Queries); snapshot.RetrievedAt = RuntimeSamples.Time.AddMinutes(minute);
        return snapshot;
    }
    private Guid OwnAlien() => database.Data.AddOwned(Catalog(0).Games.Single(g => g.Asin == Alien));

    [Fact]
    public async Task DefaultsTagOnFirstObservationAndSurviveRestartWithoutRemoval()
    {
        var id = OwnAlien(); var options = new CatalogAutomationOptions();
        Assert.Equal(LunaSubscriptionType.Standard, options.SubscriptionType);
        Assert.True(options.TagExpiredGames); Assert.False(options.ImportNewGames); Assert.False(options.RemoveUnplayedGames);
        await Create().ApplyAsync(Catalog(0), Catalog(1, remove: true), options, default);
        Assert.Contains(id, database.Expired); Assert.Contains(id, database.Data.Entries.Keys);
        await Create().ApplyAsync(Catalog(1, remove: true), Catalog(1, remove: true), options, default);
        Assert.Single(database.Expired);
        await Create().ApplyAsync(Catalog(1, remove: true), Catalog(2, remove: true), options, default);
        Assert.Contains(id, database.Expired); Assert.Contains(id, database.Data.Entries.Keys);
        Assert.Equal(0, database.Data.Adds);
    }

    [Fact]
    public async Task ReturnedGamesLoseOnlyExtensionManagedExpiryTags()
    {
        var id = OwnAlien(); var other = database.Data.AddOwned(Catalog(0).Games.Single(g => g.Title == "Alan Wake 2"));
        database.Expired.Add(other);
        await Create().ApplyAsync(Catalog(0), Catalog(1, true), new(), default);
        Assert.Contains(id, database.Expired);
        await Create().ApplyAsync(Catalog(1, true), Catalog(2), new(), default);
        Assert.DoesNotContain(id, database.Expired); Assert.Contains(other, database.Expired);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedExpiryTagWriteRecoversOwnershipAcrossRestart(bool committed)
    {
        var id = OwnAlien();
        Action<Guid, bool> interrupt = (_, expired) => { if (expired) throw new IOException("Interrupted library tag write"); };
        if (committed) database.AfterSetExpired = interrupt; else database.BeforeSetExpired = interrupt;
        await Assert.ThrowsAsync<IOException>(() => Create().ApplyAsync(Catalog(0), Catalog(1, true), new(), default));
        Assert.Equal(committed, database.Expired.Contains(id));
        Assert.Contains(id, Store.Read().TaggedGameIds);
        database.BeforeSetExpired = null; database.AfterSetExpired = null;
        if (!committed)
        {
            await Create().ApplyAsync(Catalog(1, true), Catalog(1, true), new(), default);
            Assert.Contains(id, database.Expired);
        }
        await Create().ApplyAsync(Catalog(1, true), Catalog(2), new(), default);
        Assert.DoesNotContain(id, database.Expired); Assert.DoesNotContain(id, Store.Read().TaggedGameIds);
    }

    [Fact]
    public async Task SkippedExpiryTagWriteDoesNotClaimAUserTagAddedDuringRefresh()
    {
        var id = OwnAlien();
        database.BeforeExpiryCheck = (guid, expired) => { if (expired) database.Expired.Add(guid); };
        await Create().ApplyAsync(Catalog(0), Catalog(1, true), new(), default);
        Assert.Contains(id, database.Expired); Assert.DoesNotContain(id, Store.Read().TaggedGameIds);
        database.BeforeExpiryCheck = null;
        await Create().ApplyAsync(Catalog(1, true), Catalog(2), new(), default);
        Assert.Contains(id, database.Expired);
    }

    [Fact]
    public async Task ReappearanceClearsExpiryAndNextAbsenceTagsImmediately()
    {
        var id = OwnAlien();
        await Create().ApplyAsync(Catalog(0), Catalog(1, true), new(), default);
        Assert.Contains(id, database.Expired);
        await Create().ApplyAsync(Catalog(1, true), Catalog(2), new(), default);
        Assert.DoesNotContain(id, database.Expired);
        Assert.Empty(Store.Read().MissingAsins);
        await Create().ApplyAsync(Catalog(2), Catalog(3, true), new(), default);
        Assert.Contains(id, database.Expired);
    }

    [Fact]
    public async Task RemovalOptInRechecksPlayedBusyAndOpenedEntries()
    {
        var id = OwnAlien(); var options = new CatalogAutomationOptions { RemoveUnplayedGames = true };
        database.Played.Add(id);
        await Create().ApplyAsync(Catalog(0), Catalog(1, true), options, default);
        Assert.Contains(id, database.Data.Entries.Keys);
        database.Played.Clear(); database.Busy.Add(id);
        await Create().ApplyAsync(Catalog(1, true), Catalog(2, true), options, default);
        Assert.Contains(id, database.Data.Entries.Keys);
        database.Busy.Clear();
        database.BeforeRemove = guid => Create().RecordOpened(guid);
        await Create().ApplyAsync(Catalog(2, true), Catalog(3, true), options, default);
        Assert.Contains(id, database.Data.Entries.Keys);
        database.BeforeRemove = null;
        await Create().ApplyAsync(Catalog(3, true), Catalog(4, true), options, default);
        Assert.Contains(id, database.Data.Entries.Keys);
    }

    [Fact]
    public async Task FirstObservationRemovesUnplayedOnlyWhenEnabledAndPreservesOtherLibraries()
    {
        var id = OwnAlien(); var alien = Catalog(0).Games.Single(g => g.Asin == Alien);
        var foreign = database.Data.AddOwned(alien, pluginId: Guid.NewGuid());
        var manual = database.Data.AddOwned(alien, pluginId: Guid.Empty);
        var options = new CatalogAutomationOptions { RemoveUnplayedGames = true };
        var result = await Create().ApplyAsync(Catalog(0), Catalog(1, true), options, default);
        Assert.DoesNotContain(id, database.Data.Entries.Keys);
        Assert.Contains(foreign, database.Data.Entries.Keys); Assert.Contains(manual, database.Data.Entries.Keys);
        Assert.Contains("1 unplayed games removed", result.Summary);
    }

    [Fact]
    public async Task DuplicateOwnedIdentitiesAreNotDeleted()
    {
        OwnAlien(); OwnAlien(); var options = new CatalogAutomationOptions { RemoveUnplayedGames = true };
        await Create().ApplyAsync(Catalog(0), Catalog(1, true), options, default);
        await Create().ApplyAsync(Catalog(1, true), Catalog(2, true), options, default);
        Assert.Equal(2, database.Data.Entries.Count);
    }

    [Fact]
    public async Task AutoImportsOnlyNewArrivalsAndRetriesFailuresWithoutDuplicates()
    {
        var options = new CatalogAutomationOptions { ImportNewGames = true, SubscriptionType = LunaSubscriptionType.Premium };
        await Create().ApplyAsync(null, Catalog(0), options, default);
        Assert.Empty(database.Data.Entries);
        database.Data.Reject = _ => true;
        var failed = await Create().ApplyAsync(Catalog(0), Catalog(1, add: true), options, default);
        Assert.True(failed.HasFailures); Assert.Single(Store.Read().PendingNewAsins);
        database.Data.Reject = null;
        var retried = await Create().ApplyAsync(Catalog(1, add: true), Catalog(2, add: true), options, default);
        Assert.Single(retried.AddedGameIds); Assert.Single(database.Data.Entries); Assert.Empty(Store.Read().PendingNewAsins);
        await Create().ApplyAsync(Catalog(2, add: true), Catalog(3, add: true), options, default);
        Assert.Single(database.Data.Entries);
    }

    [Theory]
    [InlineData(LunaSubscriptionType.Standard, "Standard", true)]
    [InlineData(LunaSubscriptionType.Standard, "Premium", false)]
    [InlineData(LunaSubscriptionType.Premium, "Premium", true)]
    [InlineData(LunaSubscriptionType.Premium, "Standard", false)]
    [InlineData(LunaSubscriptionType.Standard, "GameNight", false)]
    [InlineData(LunaSubscriptionType.Premium, "GameNight", false)]
    public async Task AutoImportsOnlyArrivalsInTheSelectedSubscription(LunaSubscriptionType subscription, string membership, bool expectedImport)
    {
        var current = Catalog(1, add: true, arrivalMemberships: new[] { membership });
        var result = await Create().ApplyAsync(Catalog(0), current, new() { ImportNewGames = true, SubscriptionType = subscription }, default);
        Assert.Equal(expectedImport ? 1 : 0, result.AddedGameIds.Count);
        Assert.Equal(expectedImport ? 1 : 0, database.Data.Entries.Count);
        Assert.Empty(Store.Read().PendingNewAsins);
        Assert.Contains("B000NEW001", Store.Read().KnownAsins);
    }

    [Theory]
    [InlineData(LunaSubscriptionType.Standard)]
    [InlineData(LunaSubscriptionType.Premium)]
    public async Task OverlappingMembershipsImportOnceForEitherSubscription(LunaSubscriptionType subscription)
    {
        var options = new CatalogAutomationOptions { ImportNewGames = true, SubscriptionType = subscription };
        await Create().ApplyAsync(Catalog(0), Catalog(1, add: true, arrivalMemberships: new[] { "Standard", "Premium" }), options, default);
        await Create().ApplyAsync(null, Catalog(2, add: true, arrivalMemberships: new[] { "Standard", "Premium" }), options, default);
        Assert.Single(database.Data.Entries);
        Assert.Empty(Store.Read().PendingNewAsins);
    }

    [Fact]
    public async Task SubscriptionChangesDiscardIneligiblePendingImportsWithoutBackfilling()
    {
        var options = new CatalogAutomationOptions { ImportNewGames = true, SubscriptionType = LunaSubscriptionType.Premium };
        database.Data.Reject = _ => true;
        await Create().ApplyAsync(Catalog(0), Catalog(1, add: true), options, default);
        Assert.Single(Store.Read().PendingNewAsins);
        database.Data.Reject = null;
        options.SubscriptionType = LunaSubscriptionType.Standard;
        await Create().ApplyAsync(null, Catalog(2, add: true), options, default);
        Assert.Empty(database.Data.Entries); Assert.Empty(Store.Read().PendingNewAsins);
        options.SubscriptionType = LunaSubscriptionType.Premium;
        await Create().ApplyAsync(null, Catalog(3, add: true), options, default);
        Assert.Empty(database.Data.Entries);
    }

    [Fact]
    public async Task GamesOutsideTheSubscriptionRemainPresentForExpiryAndRemoval()
    {
        var id = OwnAlien();
        await Create().ApplyAsync(Catalog(0), Catalog(1), new() { SubscriptionType = LunaSubscriptionType.Standard, RemoveUnplayedGames = true }, default);
        Assert.Contains(id, database.Data.Entries.Keys); Assert.Empty(database.Expired); Assert.Empty(Store.Read().MissingAsins);
    }

    [Fact]
    public async Task UnknownSubscriptionCannotMutateTheJournalOrLibrary()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Create().ApplyAsync(Catalog(0), Catalog(1, add: true),
            new() { ImportNewGames = true, SubscriptionType = (LunaSubscriptionType)99 }, default));
        Assert.Empty(database.Data.Entries);
        Assert.False(File.Exists(Path.Combine(directory, "catalog-automation-US.json")));
    }

    [Fact]
    public async Task DisablingAutoImportDoesNotBackfillExistingCatalog()
    {
        await Create().ApplyAsync(Catalog(0), Catalog(1, add: true), new(), default);
        await Create().ApplyAsync(Catalog(1, add: true), Catalog(2, add: true), new() { ImportNewGames = true }, default);
        Assert.Empty(database.Data.Entries);
    }

    [Fact]
    public async Task NativeIdentityMigrationIsNeitherAnArrivalNorExpiry()
    {
        var previous = Catalog(0, fallbackId: true);
        var id = database.Data.AddOwned(previous.Games.Single(g => g.Asin == Alien));
        var result = await Create().ApplyAsync(previous, Catalog(1), new() { ImportNewGames = true, RemoveUnplayedGames = true }, default);
        Assert.Empty(result.AddedGameIds); Assert.Contains(id, database.Data.Entries.Keys); Assert.Empty(database.Expired);
        Assert.Empty(Store.Read().MissingAsins); Assert.Empty(Store.Read().PendingNewAsins);
    }

    [Fact]
    public async Task CorruptOrNewerJournalBlocksLibraryMutations()
    {
        OwnAlien(); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "catalog-automation-US.json");
        foreach (var data in new[] { "{bad", "{\"schemaVersion\":2}", "{\"schemaVersion\":1,\"schemaVersion\":1}" })
        {
            File.WriteAllText(file, data);
            await Assert.ThrowsAnyAsync<Exception>(() => Create().ApplyAsync(Catalog(0), Catalog(1, true, true), new() { ImportNewGames = true, RemoveUnplayedGames = true }, default));
            Assert.Equal(data, File.ReadAllText(file)); Assert.Single(database.Data.Entries); Assert.Empty(database.Expired);
        }
    }

    [Fact]
    public async Task CancelledAndWrongScopeInputsLeaveJournalAndLibraryUnchanged()
    {
        OwnAlien(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().ApplyAsync(Catalog(0), Catalog(1, true), new(), cancelled.Token));
        Assert.False(File.Exists(Path.Combine(directory, "catalog-automation-US.json")));
        var bad = Catalog(1, true); bad.Region = "DE";
        await Assert.ThrowsAsync<CatalogValidationException>(() => Create().ApplyAsync(Catalog(0), bad, new(), default));
        Assert.Empty(database.Expired); Assert.Single(database.Data.Entries);
    }

    [Fact]
    public async Task FailedRefreshPreservesLibraryAndFirstSuccessfulRefreshRemovesMissingGame()
    {
        var id = OwnAlien();
        var cache = new JsonCatalogCache(directory, () => now); await cache.SaveAsync(Catalog(0), default);
        var bytes = File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"));
        var provider = new SampleProvider { Fetch = _ => throw new System.Net.Http.HttpRequestException("offline") };
        var calls = 0;
        var model = new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => now), automation: (previous, current, token) =>
        { calls++; return Create().ApplyAsync(previous, current, new() { RemoveUnplayedGames = true }, token); });
        await model.RefreshAsync(default);
        Assert.Equal(0, calls); Assert.Equal(5, model.TotalCount); Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "catalog-US.json")));
        Assert.Contains(id, database.Data.Entries.Keys); Assert.Empty(database.Expired);
        provider.Fetch = _ => Task.FromResult(Snapshot(1, remove: true));
        await model.RefreshAsync(default);
        Assert.Equal(1, calls); Assert.Equal(4, model.TotalCount); Assert.DoesNotContain(id, database.Data.Entries.Keys);
    }

    [Fact]
    public async Task AutomationFailureLeavesAcceptedCatalogAvailable()
    {
        var provider = new SampleProvider();
        var model = new CatalogBrowserModel(new CatalogService(provider, new JsonCatalogCache(directory, () => now), utcNow: () => now),
            automation: (_, _, _) => throw new IOException("unwritable journal"));
        await model.RefreshAsync(default);
        Assert.Equal(5, model.TotalCount); Assert.Contains("Catalog refreshed", model.ErrorText); Assert.False(model.IsBusy);
        Assert.NotNull((await new JsonCatalogCache(directory, () => now).LoadAsync("US", default)).Catalog);
    }

    [Fact]
    public async Task InvalidRefreshDoesNotAdvanceExpiryObservations()
    {
        var id = OwnAlien();
        await Create().ApplyAsync(null, Catalog(0), new(), default);
        var cache = new JsonCatalogCache(directory, () => now); await cache.SaveAsync(Catalog(0), default);
        var bytes = File.ReadAllBytes(Path.Combine(directory, "catalog-automation-US.json"));
        var provider = new SampleProvider { Fetch = _ =>
        {
            var bad = Snapshot(1, remove: true); bad.Queries[0].ResponseValid = false; return Task.FromResult(bad);
        } };
        var model = new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => now),
            automation: (previous, current, token) => Create().ApplyAsync(previous, current, new() { RemoveUnplayedGames = true }, token));
        await model.RefreshAsync(default);
        Assert.Contains("failed validation", model.ErrorText); Assert.Empty(database.Expired);
        Assert.Contains(id, database.Data.Entries.Keys);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, "catalog-automation-US.json")));
    }

    [Fact]
    public async Task BackgroundRefreshKeepsWorkAliveWhenTheSidebarCloses()
    {
        var provider = new SampleProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token => { started.SetResult(); await release.Task.WaitAsync(token); return RuntimeSamples.Snapshot(); };
        using var session = new CatalogBrowserSession(new CatalogBrowserModel(new CatalogService(provider, new JsonCatalogCache(directory), utcNow: () => now)));
        var sidebar = new object(); await session.AttachAsync(sidebar);
        var update = session.RefreshAsync(new object(), default); await started.Task;
        session.Detach(sidebar); Assert.True(session.HasActiveHosts); Assert.True(session.Model.IsBusy);
        release.SetResult(); await update;
        Assert.False(session.HasActiveHosts); Assert.False(session.Model.IsBusy); Assert.Equal(5, session.Model.TotalCount);
    }

    [Fact]
    public async Task CancellingAWaitingLibraryUpdateDoesNotCancelTheOpenViewsWork()
    {
        var provider = new SampleProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Fetch = async token => { started.TrySetResult(); await release.Task.WaitAsync(token); return RuntimeSamples.Snapshot(); };
        using var session = new CatalogBrowserSession(new CatalogBrowserModel(new CatalogService(provider, new JsonCatalogCache(directory), utcNow: () => now)));
        await session.AttachAsync(new object());
        var manual = session.Model.RefreshAsync(default); await started.Task;
        using var cancel = new CancellationTokenSource();
        var background = session.RefreshAsync(new object(), cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => background);
        Assert.True(session.Model.IsBusy); Assert.False(manual.IsCompleted);
        release.SetResult(); await manual; Assert.Equal(1, provider.Calls);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
