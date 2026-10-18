using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;
using Newtonsoft.Json;
using Xunit;

namespace LunaCatalog.Tests;

internal sealed class ImportTestDatabase : ICatalogImportDatabase
{
    internal sealed class Entry
    {
        public LibraryGameIdentity Identity = null!;
        public string Title = "";
        public string UserMetadata = "";
    }
    internal readonly Dictionary<Guid, Entry> Entries = new();
    internal Func<ImportCandidate, bool>? Reject;
    internal Action? AfterAdd;
    internal int Adds;
    internal byte[]? LastCover;
    internal Action? BeforeRead;
    public IReadOnlyList<LibraryGameIdentity> ReadIdentities() { BeforeRead?.Invoke(); return Entries.Values.Select(e => e.Identity).ToArray(); }
    public string? AddGame(ImportCandidate game, Guid id, byte[]? cover)
    {
        if (Reject?.Invoke(game) == true) throw new IOException("sensitive host exception");
        if (Entries.Values.Any(e => LunaImportIdentity.IsOwner(e.Identity.PluginId) && LunaImportIdentity.TryDecode(e.Identity.GameId, out var owned) &&
            LunaImportIdentity.Matches(owned, game.LunaId, game.Asin))) throw new ImportAlreadyExistsException();
        Entries.Add(id, new Entry { Title = game.Title, Identity = new LibraryGameIdentity(id, LunaImportIdentity.PluginId,
            LunaImportIdentity.Encode(game.Region, game.LunaId, game.Asin)) });
        Adds++; LastCover = cover; AfterAdd?.Invoke(); return null;
    }
    internal Guid AddOwned(LunaGame game, string? id = null, Guid? pluginId = null)
    {
        var guid = Guid.NewGuid();
        Entries.Add(guid, new Entry { Title = game.Title, Identity = new LibraryGameIdentity(guid, pluginId ?? LunaImportIdentity.PluginId,
            LunaImportIdentity.Encode(game.Region, id ?? game.Id, game.Asin!)) }); return guid;
    }
}
internal sealed class ImportTestStore : IImportMappingStore
{
    internal readonly JsonImportMappingStore Store;
    internal int Saves;
    internal int FailAt;
    internal ImportTestStore(string directory) { Store = new(directory); }
    public ImportMappingReadResult Load() => Store.Load();
    public void Save(ImportMappingDocument document)
    { if (++Saves == FailAt) throw new IOException("sensitive mapping exception"); Store.Save(document); }
}

public sealed class ImportServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "luna-import-test-" + Guid.NewGuid().ToString("N"));
    private readonly ImportTestDatabase database = new();
    private ValidatedCatalog Catalog() => RuntimeSamples.Validated();
    private CatalogImportService Service(IImportMappingStore? store = null,
        Func<string?, CancellationToken, Task<byte[]?>>? artwork = null) => new(database, store ?? new JsonImportMappingStore(directory), artwork, utcNow: () => RuntimeSamples.Time);
    private Task<ImportBatchResult> Import(CatalogImportService service, IEnumerable<string>? ids = null, CancellationToken token = default) =>
        service.ImportAsync(Catalog(), ids ?? Catalog().Games.Select(g => g.Id), token);

    [Fact]
    public async Task RepresentativeBulkImportPersistsAndRepeatedImportsSkipAfterRestart()
    {
        var first = await Import(Service());
        Assert.Equal(5, first.Added); Assert.Equal(0, first.Failed); Assert.Equal(5, database.Entries.Count);
        var mappings = new JsonImportMappingStore(directory).Load().Document!.Mappings;
        Assert.Equal(5, mappings.Count); Assert.All(mappings, m => Assert.True(database.Entries.ContainsKey(m.PlayniteId)));
        var restarted = Service();
        Assert.All((await restarted.ReconcileAsync(Catalog(), default)).Games.Values, s => Assert.Equal(CatalogImportStatus.Imported, s));
        var repeated = await Import(restarted);
        Assert.Equal(5, repeated.Skipped); Assert.Equal(0, repeated.Added); Assert.Equal(5, database.Adds);
    }

    [Fact]
    public async Task SameTitleForeignLibrariesAndUserEditsAreUntouched()
    {
        var game = Catalog().Games[0]; var foreign = Guid.NewGuid();
        database.Entries.Add(foreign, new() { Title = game.Title, UserMetadata = "Steam favorite notes artwork tags playtime", Identity = new(foreign, Guid.NewGuid(), game.Id) });
        await Import(Service(), new[] { game.Id });
        var owned = database.Entries.Single(e => e.Key != foreign);
        owned.Value.Title = "My title override"; owned.Value.UserMetadata = "favorite completion playtime notes cover categories tags";
        var before = JsonConvert.SerializeObject(database.Entries);
        Assert.Equal(1, (await Import(Service(), new[] { game.Id })).Skipped);
        await Service().ReconcileAsync(Catalog(), default);
        Assert.Equal(before, JsonConvert.SerializeObject(database.Entries));
        Assert.Equal(2, database.Entries.Count);
    }

    [Fact]
    public async Task DeletedGamesAreReconciledAndCanBeExplicitlyImportedAgain()
    {
        var id = Catalog().Games[0].Id;
        await Import(Service(), new[] { id }); var deleted = database.Entries.Keys.Single(); database.Entries.Remove(deleted);
        Assert.Equal(CatalogImportStatus.NotImported, (await Service().ReconcileAsync(Catalog(), default)).Games[id]);
        Assert.Empty(new JsonImportMappingStore(directory).Load().Document!.Mappings);
        Assert.Equal(1, (await Import(Service(), new[] { id })).Added); Assert.DoesNotContain(deleted, database.Entries.Keys);
    }

    [Fact]
    public async Task MissingMappingFileRecoversFromDurableMarkersWithoutWritingGames()
    {
        var game = Catalog().Games[0]; var owned = database.AddOwned(game);
        var state = await Service().ReconcileAsync(Catalog(), default);
        Assert.Equal(CatalogImportStatus.Imported, state.Games[game.Id]);
        Assert.Equal(owned, new JsonImportMappingStore(directory).Load().Document!.Mappings.Single().PlayniteId);
        Assert.Equal(0, database.Adds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CurrentLibraryAndLegacyManualMarkersBothRemainImported(bool legacy)
    {
        var game = Catalog().Games[0];
        var owner = legacy ? Guid.Empty : LunaImportIdentity.PluginId;
        var id = database.AddOwned(game, pluginId: owner);
        var state = await Service().ReconcileAsync(Catalog(), default);
        Assert.Equal(CatalogImportStatus.Imported, state.Games[game.Id]);
        Assert.Equal(id, new JsonImportMappingStore(directory).Load().Document!.Mappings.Single().PlayniteId);
        Assert.Equal(1, (await Import(Service(), new[] { game.Id })).Skipped);
        Assert.Equal(owner, database.Entries[id].Identity.PluginId); Assert.Equal(0, database.Adds);
    }

    [Fact]
    public async Task AsinFallbackMigratesToNativeIdentityWithoutChangingHostMetadata()
    {
        var game = Catalog().Games.First(g => g.Id != g.Asin); var owned = database.AddOwned(game, game.Asin);
        var marker = database.Entries[owned].Identity.GameId;
        Assert.Equal(1, (await Import(Service(), new[] { game.Id })).Skipped);
        var mapping = new JsonImportMappingStore(directory).Load().Document!.Mappings.Single();
        Assert.Equal(game.Id, mapping.LunaId); Assert.Equal(owned, mapping.PlayniteId);
        Assert.Equal(marker, database.Entries[owned].Identity.GameId); Assert.Equal(0, database.Adds);
    }

    [Theory]
    [InlineData("duplicate owned")]
    [InlineData("native conflict")]
    [InlineData("same id different asin")]
    public async Task ConflictingOwnedIdentitiesAreReportedWithoutChoosingOrCreating(string conflict)
    {
        var game = Catalog().Games.First(g => g.Id != g.Asin); database.AddOwned(game);
        if (conflict == "duplicate owned") database.AddOwned(game);
        else
        {
            var id = Guid.NewGuid();
            var marker = conflict == "native conflict" ? LunaImportIdentity.Encode("US", "amzn1.adg.product.11111111-1111-1111-1111-111111111111", game.Asin!) :
                LunaImportIdentity.Encode("US", game.Id, "B000000001");
            database.Entries.Add(id, new() { Identity = new(id, Guid.Empty, marker) });
        }
        Assert.Equal(CatalogImportStatus.Conflict, (await Service().ReconcileAsync(Catalog(), default)).Games[game.Id]);
        Assert.Equal(1, (await Import(Service(), new[] { game.Id })).Failed); Assert.Equal(0, database.Adds);
    }

    [Fact]
    public async Task MappingToForeignGuidIsPrunedWithoutAdoptingOrChangingTheForeignGame()
    {
        var game = Catalog().Games[0]; var foreign = Guid.NewGuid();
        database.Entries.Add(foreign, new() { Title = "foreign", Identity = new(foreign, Guid.NewGuid(), "steam-id") });
        new JsonImportMappingStore(directory).Save(new() { Mappings = new() { new() { Region = "US", LunaId = game.Id, Asin = game.Asin!, PlayniteId = foreign } } });
        Assert.Equal(CatalogImportStatus.NotImported, (await Service().ReconcileAsync(Catalog(), default)).Games[game.Id]);
        Assert.Equal(1, (await Import(Service(), new[] { game.Id })).Added); Assert.Equal("foreign", database.Entries[foreign].Title);
    }

    [Fact]
    public async Task PartialHostFailureRetriesOnlyTheFailedEntryAndSanitizesErrors()
    {
        var failed = Catalog().Games[1].Id; database.Reject = g => g.LunaId == failed;
        var result = await Import(Service()); Assert.Equal(4, result.Added); Assert.Equal(1, result.Failed);
        Assert.DoesNotContain(result.Outcomes, o => o.Message.Contains("sensitive"));
        database.Reject = null;
        result = await Import(Service()); Assert.Equal(1, result.Added); Assert.Equal(4, result.Skipped); Assert.Equal(5, database.Entries.Count);
    }

    [Fact]
    public async Task StoragePreflightFailureCannotCreateAnyHostEntries()
    {
        var result = await Import(Service(new ImportTestStore(directory) { FailAt = 1 }));
        Assert.Equal(5, result.Failed); Assert.Equal(0, database.Adds); Assert.All(result.Outcomes, o => Assert.Contains("writable", o.Message));
    }

    [Fact]
    public async Task MappingFailureAfterCommitRetainsGameAndRecoversWithoutDuplicates()
    {
        var result = await Import(Service(new ImportTestStore(directory) { FailAt = 2 }));
        Assert.Equal(1, result.Added); Assert.Equal(4, result.Failed); Assert.Contains("already in your library", result.Outcomes[0].Message);
        result = await Import(Service()); Assert.Equal(4, result.Added); Assert.Equal(1, result.Skipped); Assert.Equal(5, database.Entries.Count);
    }

    [Fact]
    public async Task HostExceptionAfterCommitIsRecoverableAndDoesNotCountAsANewRetry()
    {
        database.AfterAdd = () => throw new IOException("sensitive committed write error");
        var game = Catalog().Games[0]; var result = await Import(Service(), new[] { game.Id });
        Assert.Equal(1, result.Added); Assert.Contains("already", (await Import(Service(), new[] { game.Id })).Summary);
        Assert.Equal(1, database.Adds);
    }

    [Fact]
    public async Task ConcurrentBatchRetriesAreSerializedAndIdempotent()
    {
        var service = Service(); var results = await Task.WhenAll(Import(service), Import(service));
        Assert.Equal(5, results.Sum(r => r.Added)); Assert.Equal(5, results.Sum(r => r.Skipped)); Assert.Equal(5, database.Adds);
    }

    [Fact]
    public async Task CancellationAfterACommitPersistsItAndCancelsRemainingEntries()
    {
        using var cancellation = new CancellationTokenSource(); database.AfterAdd = cancellation.Cancel;
        var result = await Import(Service(), token: cancellation.Token);
        Assert.Equal(1, result.Added); Assert.Equal(4, result.Cancelled);
        Assert.Single(new JsonImportMappingStore(directory).Load().Document!.Mappings);
        database.AfterAdd = null;
        result = await Import(Service()); Assert.Equal(4, result.Added); Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task DuplicateSelectionsAndMissingIdsProduceOneOutcomePerIdentity()
    {
        var id = Catalog().Games[0].Id; var result = await Import(Service(), new[] { id, id, "no-longer-present" });
        Assert.Equal(1, result.Added); Assert.Equal(1, result.Failed); Assert.Equal(2, result.Outcomes.Count);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("exception")]
    [InlineData("valid")]
    public async Task ArtworkIsOptionalAndOnlyUsedForNewEntries(string mode)
    {
        var calls = 0;
        var service = Service(artwork: (_, _) => { calls++; return mode == "exception" ? throw new IOException("sensitive image") : Task.FromResult<byte[]?>(mode == "valid" ? new byte[] { 1 } : null); });
        var id = Catalog().Games[0].Id;
        Assert.Equal(1, (await Import(service, new[] { id })).Added); Assert.Equal(1, (await Import(service, new[] { id })).Skipped);
        Assert.Equal(1, calls); Assert.Equal(mode == "valid", database.LastCover != null);
    }

    [Fact]
    public async Task CancellingArtworkCreatesNoGameAndRetainsTheCatalog()
    {
        using var cancellation = new CancellationTokenSource(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service(artwork: async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return null; });
        var pending = Import(service, token: cancellation.Token); await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var result = await pending; Assert.Equal(5, result.Cancelled); Assert.Equal(0, database.Adds); Assert.Equal(5, Catalog().Games.Count);
    }

    [Fact]
    public async Task UnvalidatedCatalogCannotReachHostWrites()
    {
        var invalid = Catalog(); invalid.Games.Clear();
        var result = await Service().ImportAsync(invalid, new[] { "id" }, default);
        Assert.Equal(1, result.Failed); Assert.Empty(database.Entries); Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task CatalogRemovalsNeverDeleteImportedGamesOrTheirMappings()
    {
        await Import(Service()); var reduced = Catalog(); var removed = reduced.Games[0]; reduced.Games.RemoveAt(0);
        foreach (var query in reduced.Queries) query.GameIds.Remove(removed.Id);
        await Service().ReconcileAsync(reduced, default);
        Assert.Equal(5, database.Entries.Count); Assert.Equal(5, new JsonImportMappingStore(directory).Load().Document!.Mappings.Count);
        Assert.Equal(4, (await Service().ImportAsync(reduced, reduced.Games.Select(g => g.Id), default)).Skipped);
    }

    [Fact]
    public async Task EntryAddedDuringArtworkIsSkippedByTheAtomicHostCreateCheck()
    {
        var game = Catalog().Games[0];
        var service = Service(artwork: (_, _) => { database.AddOwned(game); return Task.FromResult<byte[]?>(null); });
        Assert.Equal(1, (await Import(service, new[] { game.Id })).Skipped); Assert.Single(database.Entries); Assert.Equal(0, database.Adds);
        Assert.Equal(CatalogImportStatus.Imported, (await Service().ReconcileAsync(Catalog(), default)).Games[game.Id]);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
