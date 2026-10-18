using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LunaCatalog
{
    public sealed class CatalogImportService
    {
        private readonly ICatalogImportDatabase database;
        private readonly IImportMappingStore store;
        private readonly Func<string?, CancellationToken, Task<byte[]?>>? artwork;
        private readonly Action<string>? reportError;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private const string StorageError = "Import mappings could not be saved. Check the extension data folder is writable, then retry.";
        public CatalogImportService(ICatalogImportDatabase database, IImportMappingStore store,
            Func<string?, CancellationToken, Task<byte[]?>>? artwork = null, Action<string>? reportError = null, Func<DateTimeOffset>? utcNow = null)
        { this.database = database; this.store = store; this.artwork = artwork; this.reportError = reportError; this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); }

        public async Task<CatalogImportState> ReconcileAsync(ValidatedCatalog catalog, CancellationToken token)
        {
            var copy = CatalogData.Copy(catalog);
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => ReadContext(copy, token).State, token).ConfigureAwait(false); }
            finally { gate.Release(); }
        }
        public async Task<ImportBatchResult> ImportAsync(ValidatedCatalog catalog, IEnumerable<string> lunaIds, CancellationToken token)
        {
            var copy = CatalogData.Copy(catalog); var ids = lunaIds.Distinct(StringComparer.Ordinal).ToArray();
            await gate.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => ImportCoreAsync(copy, ids, token)).ConfigureAwait(false); }
            finally { gate.Release(); }
        }
        private sealed class Context
        {
            internal ImportMappingDocument Document = new ImportMappingDocument();
            internal CatalogImportState State = null!;
        }
        private Context ReadContext(ValidatedCatalog catalog, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); CatalogValidator.ValidateCached(catalog, "US", utcNow());
            var read = store.Load(); var statuses = catalog.Games.ToDictionary(g => g.Id, _ => CatalogImportStatus.Unavailable, StringComparer.Ordinal);
            if (read.Document == null) return new Context { State = new CatalogImportState(false, statuses, read.Diagnostic) };
            var owned = ReadOwned();
            var mappings = read.Document.Mappings.Where(m => owned.Any(o => o.PlayniteId == m.PlayniteId && Matches(o, m.LunaId, m.Asin))).ToList();
            foreach (var game in catalog.Games)
            {
                var possible = owned.Where(o => o.Region == game.Region && (o.LunaId == game.Id || o.Asin == game.Asin)).ToArray();
                var matches = possible.Where(o => Matches(o, game.Id, game.Asin!)).ToArray();
                if (possible.Length > 1 || possible.Any(o => !Matches(o, game.Id, game.Asin!))) statuses[game.Id] = CatalogImportStatus.Conflict;
                else if (matches.Length == 1)
                {
                    statuses[game.Id] = CatalogImportStatus.Imported;
                    mappings.RemoveAll(m => m.PlayniteId == matches[0].PlayniteId || m.LunaId == game.Id);
                    mappings.Add(new ImportMapping { Region = game.Region, LunaId = game.Id, Asin = game.Asin!, PlayniteId = matches[0].PlayniteId });
                }
                else statuses[game.Id] = CatalogImportStatus.NotImported;
            }
            mappings = mappings.OrderBy(m => m.LunaId, StringComparer.Ordinal).ToList();
            var document = new ImportMappingDocument { Mappings = mappings };
            var before = read.Document.Mappings.OrderBy(m => m.LunaId, StringComparer.Ordinal).ToArray();
            var changed = before.Length != mappings.Count || before.Where((m, i) => m.LunaId != mappings[i].LunaId ||
                m.Asin != mappings[i].Asin || m.PlayniteId != mappings[i].PlayniteId).Any();
            var diagnostic = read.Diagnostic; var canImport = true;
            if (changed)
                try { token.ThrowIfCancellationRequested(); store.Save(document); }
                catch (Exception ex) when (!(ex is OperationCanceledException)) { Log(ex); diagnostic = StorageError; canImport = false; }
            return new Context { Document = document, State = new CatalogImportState(canImport, statuses, diagnostic) };
        }

        private List<ImportMapping> ReadOwned()
        {
            var entries = new List<ImportMapping>();
            foreach (var game in database.ReadIdentities())
                if (LunaImportIdentity.IsOwner(game.PluginId) && game.PlayniteId != Guid.Empty && LunaImportIdentity.TryDecode(game.GameId, out var identity))
                { identity.PlayniteId = game.PlayniteId; entries.Add(identity); }
            return entries;
        }
        private static bool Matches(ImportMapping owned, string id, string asin) => owned.Region == "US" && owned.Asin == asin &&
            LunaImportIdentity.Matches(owned, id, asin);

        private async Task<ImportBatchResult> ImportCoreAsync(ValidatedCatalog catalog, string[] ids, CancellationToken token)
        {
            var outcomes = new List<ImportOutcome>(); var games = catalog.Games.ToDictionary(g => g.Id, StringComparer.Ordinal);
            Context context;
            try { context = ReadContext(catalog, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return CancelRemaining(ids, games, outcomes); }
            catch (Exception ex) { Log(ex); return FailRemaining(ids, games, outcomes, "Import status could not be checked. Reopen the browser and retry."); }
            if (!context.State.CanImport) return FailRemaining(ids, games, outcomes, context.State.Diagnostic);
            // Prove mapping storage is writable before adding a game to the host database.
            try { store.Save(context.Document); }
            catch (Exception ex) { Log(ex); return FailRemaining(ids, games, outcomes, StorageError); }
            for (var index = 0; index < ids.Length; index++)
            {
                if (token.IsCancellationRequested) return CancelRemaining(ids.Skip(index), games, outcomes);
                var id = ids[index];
                if (!games.TryGetValue(id, out var game))
                { outcomes.Add(new ImportOutcome(id, id, ImportOutcomeKind.Failed, "This selection is no longer in the validated catalog. Refresh and select again.")); continue; }
                try
                {
                    // Reconcile immediately before every create, including deletions and partial retries.
                    context = ReadContext(catalog, token);
                    if (!context.State.CanImport) return FailRemaining(ids.Skip(index), games, outcomes, context.State.Diagnostic);
                    if (context.State.Games[id] == CatalogImportStatus.Imported)
                    { outcomes.Add(new ImportOutcome(id, game.Title, ImportOutcomeKind.Skipped)); continue; }
                    if (context.State.Games[id] == CatalogImportStatus.Conflict)
                    { outcomes.Add(new ImportOutcome(id, game.Title, ImportOutcomeKind.Failed, "Conflicting Luna identities exist in the library. Resolve those entries before retrying.")); continue; }
                    byte[]? cover = null;
                    if (artwork != null)
                        try { cover = await artwork(game.CoverUrl, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch (Exception ex) { Log(ex); }
                    token.ThrowIfCancellationRequested();
                    var guid = Guid.NewGuid(); string? warning;
                    try { warning = database.AddGame(new ImportCandidate(game), guid, cover); }
                    catch (ImportAlreadyExistsException)
                    { outcomes.Add(new ImportOutcome(id, game.Title, ImportOutcomeKind.Skipped)); continue; }
                    catch (Exception ex)
                    {
                        // A host write may commit and then throw. The durable marker prevents retries from duplicating it.
                        if (!ReadOwned().Any(o => o.PlayniteId == guid && Matches(o, game.Id, game.Asin!))) throw;
                        Log(ex); warning = "The game was added, but Playnite reported a write error. Check it in your library.";
                    }
                    outcomes.Add(new ImportOutcome(id, game.Title, ImportOutcomeKind.Added, warning ?? ""));
                    context.Document.Mappings.RemoveAll(m => m.LunaId == id || m.PlayniteId == guid);
                    context.Document.Mappings.Add(new ImportMapping { Region = game.Region, LunaId = id, Asin = game.Asin!, PlayniteId = guid });
                    try { store.Save(context.Document); }
                    catch (Exception ex)
                    {
                        Log(ex); outcomes[outcomes.Count - 1] = new ImportOutcome(id, game.Title, ImportOutcomeKind.Added, StorageError + " The game is already in your library.");
                        return FailRemaining(ids.Skip(index + 1), games, outcomes, StorageError);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return CancelRemaining(ids.Skip(index), games, outcomes); }
                catch (Exception ex) { Log(ex); outcomes.Add(new ImportOutcome(id, game.Title, ImportOutcomeKind.Failed, "Playnite could not save this game. Check the library is writable, then retry.")); }
            }
            return new ImportBatchResult(outcomes);
        }
        private static ImportBatchResult FailRemaining(IEnumerable<string> ids, Dictionary<string, LunaGame> games, List<ImportOutcome> outcomes, string message)
        { outcomes.AddRange(ids.Select(id => new ImportOutcome(id, games.TryGetValue(id, out var game) ? game.Title : id, ImportOutcomeKind.Failed, message))); return new ImportBatchResult(outcomes); }
        private static ImportBatchResult CancelRemaining(IEnumerable<string> ids, Dictionary<string, LunaGame> games, List<ImportOutcome> outcomes)
        { outcomes.AddRange(ids.Select(id => new ImportOutcome(id, games.TryGetValue(id, out var game) ? game.Title : id, ImportOutcomeKind.Cancelled))); return new ImportBatchResult(outcomes); }
        private void Log(Exception ex) => reportError?.Invoke("Luna import operation failed (" + ex.GetType().Name + ").");
    }
}
