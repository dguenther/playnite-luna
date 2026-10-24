using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LunaCatalog
{
    public sealed class CatalogAutomationOptions
    {
        public bool TagExpiredGames { get; set; } = true;
        public bool ImportNewGames { get; set; }
        public bool RemoveUnplayedGames { get; set; }
    }

    public sealed class CatalogLibraryGame
    {
        public LibraryGameIdentity Identity { get; }
        public bool HasPlayHistory { get; }
        public bool IsBusy { get; }
        public bool HasExpiredTag { get; }
        public CatalogLibraryGame(LibraryGameIdentity identity, bool hasPlayHistory, bool busy, bool expired)
        { Identity = identity; HasPlayHistory = hasPlayHistory; IsBusy = busy; HasExpiredTag = expired; }
    }

    public interface ICatalogAutomationDatabase : ICatalogImportDatabase
    {
        IReadOnlyList<CatalogLibraryGame> ReadLibraryGames();
        bool SetExpired(Guid id, string expectedIdentity, bool expired, Action? beforeChange = null);
        bool RemoveUnplayed(Guid id, string expectedIdentity, Func<bool> mayRemove);
    }

    public sealed class CatalogAutomationResult
    {
        public IReadOnlyList<Guid> AddedGameIds { get; }
        public string Summary { get; }
        public bool HasFailures { get; }
        public CatalogAutomationResult(IEnumerable<Guid> added, string summary, bool hasFailures = false)
        { AddedGameIds = Array.AsReadOnly(added.ToArray()); Summary = summary; HasFailures = hasFailures; }
    }

    public sealed class CatalogAutomation
    {
        private readonly ICatalogAutomationDatabase database;
        private readonly CatalogImportService imports;
        private readonly JsonCatalogAutomationStore store;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private volatile bool removalBlocked;
        public CatalogAutomation(ICatalogAutomationDatabase database, CatalogImportService imports,
            JsonCatalogAutomationStore store, Func<DateTimeOffset>? utcNow = null)
        { this.database = database; this.imports = imports; this.store = store; this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); }

        public void RecordOpened(Guid gameId)
        {
            try { store.Update(state => { if (!state.OpenedGameIds.Contains(gameId)) state.OpenedGameIds.Add(gameId); }); }
            catch { removalBlocked = true; throw; }
        }

        public async Task<CatalogAutomationResult> ApplyAsync(ValidatedCatalog? previous, ValidatedCatalog current,
            CatalogAutomationOptions options, CancellationToken token)
        {
            CatalogValidator.ValidateCached(current, "US", utcNow());
            if (previous != null) CatalogComparer.Compare(previous, current, utcNow());
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var games = database.ReadLibraryGames().Where(g => g.Identity.PluginId == LunaImportIdentity.PluginId &&
                    LunaImportIdentity.TryDecode(g.Identity.GameId, out _)).ToArray();
                var present = new HashSet<string>(current.Games.Select(g => g.Asin!), StringComparer.Ordinal);
                var identities = games.ToDictionary(g => g.Identity.PlayniteId, g =>
                { LunaImportIdentity.TryDecode(g.Identity.GameId, out var identity); return identity; });
                CatalogAutomationState plan = null!;
                store.Update(state =>
                {
                    if (state.LastObservation > current.RetrievedAt) throw new CatalogValidationException("Automation cannot accept an older catalog observation.");
                    var known = state.HasBaseline ? state.KnownAsins : previous?.Games.Select(g => g.Asin!).ToList();
                    if (state.LastObservation != current.RetrievedAt)
                    {
                        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
                        foreach (var asin in identities.Values.Select(i => i.Asin).Distinct(StringComparer.Ordinal).Where(a => !present.Contains(a)))
                            missing[asin] = state.MissingAsins.TryGetValue(asin, out var count) ? Math.Min(2, count + 1) : 1;
                        state.MissingAsins = missing;
                        if (options.ImportNewGames && known != null)
                            state.PendingNewAsins = state.PendingNewAsins.Concat(present.Except(known, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
                        state.LastObservation = current.RetrievedAt;
                    }
                    if (!options.ImportNewGames) state.PendingNewAsins.Clear();
                    state.PendingNewAsins.RemoveAll(a => !present.Contains(a));
                    state.HasBaseline = true; state.KnownAsins = present.OrderBy(a => a, StringComparer.Ordinal).ToList();
                    var ownedIds = new HashSet<Guid>(identities.Keys);
                    state.OpenedGameIds.RemoveAll(id => !ownedIds.Contains(id));
                    state.TaggedGameIds.RemoveAll(id => !ownedIds.Contains(id));
                    plan = state;
                }); // Prove the journal is writable before changing the library.
                var tagged = 0; var cleared = 0; var removed = 0;
                var ambiguous = new HashSet<string>(identities.Values.GroupBy(i => i.Asin).Where(g => g.Count() > 1).Select(g => g.Key));
                foreach (var game in games)
                {
                    token.ThrowIfCancellationRequested();
                    var identity = identities[game.Identity.PlayniteId];
                    var expired = plan.MissingAsins.ContainsKey(identity.Asin);
                    if (options.TagExpiredGames && present.Contains(identity.Asin) && plan.TaggedGameIds.Contains(game.Identity.PlayniteId))
                    {
                        if (database.SetExpired(game.Identity.PlayniteId, game.Identity.GameId, false)) cleared++;
                        store.Update(s => s.TaggedGameIds.Remove(game.Identity.PlayniteId));
                    }
                    else if (options.TagExpiredGames && expired && !game.HasExpiredTag)
                    {
                        // Persist intent after the live tag check, before the SDK write, to recover partial commits.
                        if (database.SetExpired(game.Identity.PlayniteId, game.Identity.GameId, true, () =>
                            store.Update(s => { if (!s.TaggedGameIds.Contains(game.Identity.PlayniteId)) s.TaggedGameIds.Add(game.Identity.PlayniteId); }))) tagged++;
                    }
                    if (options.RemoveUnplayedGames && !removalBlocked && expired && !ambiguous.Contains(identity.Asin) && !game.HasPlayHistory && !game.IsBusy &&
                        database.RemoveUnplayed(game.Identity.PlayniteId, game.Identity.GameId, () =>
                        {
                            var latest = store.Read();
                            return !removalBlocked && latest.HasBaseline && latest.LastObservation == current.RetrievedAt &&
                                !latest.OpenedGameIds.Contains(game.Identity.PlayniteId);
                        })) removed++;
                }
                var before = new HashSet<Guid>(games.Select(g => g.Identity.PlayniteId));
                ImportBatchResult? imported = null;
                if (options.ImportNewGames && plan.PendingNewAsins.Count > 0)
                {
                    var byId = current.Games.ToDictionary(g => g.Id, StringComparer.Ordinal);
                    imported = await imports.ImportAsync(current, current.Games.Where(g => plan.PendingNewAsins.Contains(g.Asin!)).Select(g => g.Id), token).ConfigureAwait(false);
                    var completed = imported.Outcomes.Where(o => o.Kind == ImportOutcomeKind.Added || o.Kind == ImportOutcomeKind.Skipped).Select(o => byId[o.LunaId].Asin!).ToArray();
                    store.Update(s => s.PendingNewAsins.RemoveAll(a => completed.Contains(a)));
                }
                await imports.ReconcileAsync(current, token).ConfigureAwait(false);
                var added = database.ReadLibraryGames().Where(g => g.Identity.PluginId == LunaImportIdentity.PluginId &&
                    LunaImportIdentity.TryDecode(g.Identity.GameId, out _)).Select(g => g.Identity.PlayniteId).Where(id => !before.Contains(id)).ToArray();
                return new CatalogAutomationResult(added, "Catalog updated · " + tagged + " tagged expired · " + cleared + " expiry tags cleared · " +
                    removed + " unplayed games removed" + (imported == null ? "" : " · " + imported.Summary), imported?.Failed > 0);
            }
            finally { gate.Release(); }
        }
    }
}
