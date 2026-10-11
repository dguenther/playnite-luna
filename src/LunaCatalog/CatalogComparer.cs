using System;
using System.Collections.Generic;
using System.Linq;

namespace LunaCatalog
{
    public sealed class CatalogGameChange
    {
        public LunaGame Previous { get; }
        public LunaGame Current { get; }
        internal CatalogGameChange(LunaGame previous, LunaGame current)
        {
            Previous = CatalogData.CopyGame(previous);
            Current = CatalogData.CopyGame(current);
        }
    }

    public sealed class CatalogChanges
    {
        public IReadOnlyList<LunaGame> Added { get; }
        public IReadOnlyList<LunaGame> Removed { get; }
        public IReadOnlyList<CatalogGameChange> Updated { get; }
        public IReadOnlyList<CatalogGameChange> IdentityMigrations { get; }
        internal CatalogChanges(List<LunaGame> added, List<LunaGame> removed,
            List<CatalogGameChange> updated, List<CatalogGameChange> migrations)
        {
            Added = added.AsReadOnly(); Removed = removed.AsReadOnly();
            Updated = updated.AsReadOnly(); IdentityMigrations = migrations.AsReadOnly();
        }
    }

    public static class CatalogComparer
    {
        // Observed differences only: these must never delete imported Playnite games.
        public static CatalogChanges Compare(ValidatedCatalog previous, ValidatedCatalog current, DateTimeOffset now)
        {
            CatalogValidator.ValidateCached(previous, current.Region, now);
            CatalogValidator.ValidateCached(current, previous.Region, now);
            if (current.RetrievedAt < previous.RetrievedAt)
                throw new CatalogValidationException("A refresh cannot replace a newer cached catalog.");
            var byId = previous.Games.ToDictionary(g => g.Id, StringComparer.Ordinal);
            var byAsin = previous.Games.ToDictionary(g => g.Asin!, StringComparer.Ordinal);
            var retained = new HashSet<string>(StringComparer.Ordinal);
            var added = new List<LunaGame>();
            var updated = new List<CatalogGameChange>();
            var migrations = new List<CatalogGameChange>();
            foreach (var game in current.Games)
            {
                if (byId.TryGetValue(game.Id, out var old))
                {
                    if (old.Asin != game.Asin)
                        throw new CatalogValidationException("A stable product ID changed its ASIN across refreshes.");
                }
                else if (byAsin.TryGetValue(game.Asin!, out old))
                {
                    if (old.Id != old.Asin || !CatalogValidator.IsNativeId(game.Id))
                        throw new CatalogValidationException("Conflicting native IDs or an identity downgrade across refreshes.");
                    migrations.Add(new CatalogGameChange(old, game));
                }
                else
                {
                    added.Add(CatalogData.CopyGame(game));
                    continue;
                }
                retained.Add(old.Id);
                if (!CatalogData.GamesEqual(old, game)) updated.Add(new CatalogGameChange(old, game));
            }
            var removed = previous.Games.Where(g => !retained.Contains(g.Id)).Select(CatalogData.CopyGame).ToList();
            return new CatalogChanges(added, removed, updated, migrations);
        }
    }
}
