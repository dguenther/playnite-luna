using System;
using System.Collections.Generic;
using System.Linq;

namespace LunaCatalog
{
    public sealed class CatalogQuery
    {
        public string Name { get; }
        public string Query { get; }
        public string ExpectedPageType { get; }
        public string? Membership { get; }

        public CatalogQuery(string name, string query, string pageType, string? membership)
        {
            Name = name;
            Query = query;
            ExpectedPageType = pageType;
            Membership = membership;
        }

        public static IReadOnlyList<CatalogQuery> UsQueries { get; } = new[]
        {
            new CatalogQuery("AllGames", "", "browse_results_all_games", null),
            new CatalogQuery("Standard", "included_with_prime", "browse_results_included_with_prime", "Standard"),
            new CatalogQuery("Premium", "luna_premium", "browse_results_luna_premium", "Premium"),
            new CatalogQuery("GameNight", "game_night", "browse_results_game_night", "GameNight")
        };
    }

    public sealed class LunaGame
    {
        public string Id { get; set; } = "";
        public string? Asin { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Region { get; set; } = "US";
        public SortedSet<string> Categories { get; set; } = new SortedSet<string>(StringComparer.Ordinal);
        public string? CoverUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        public int? ReleaseYear { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public string? PublishersText { get; set; }
        public bool HasPlayLink { get; set; }
        public bool IdentityConflict { get; set; }
        public bool IsImportable => HasPlayLink && !IdentityConflict &&
            !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Url);
    }

    public sealed class QueryResult
    {
        public string Name { get; set; } = "";
        public string? ObservedPageType { get; set; }
        public int? HttpStatus { get; set; }
        public int PagesRetrieved { get; set; }
        public int TilesObserved { get; set; }
        public bool ResponseValid { get; set; }
        public bool StopFurtherRequests { get; set; }
        public string PaginationStatus { get; set; } = "Unknown";
        public List<string> Diagnostics { get; set; } = new List<string>();
        public List<LunaGame> Games { get; set; } = new List<LunaGame>();
    }

    public sealed class CatalogSnapshot
    {
        public int SchemaVersion { get; set; } = 1;
        public string Region { get; set; } = "US";
        public string MarketplaceId { get; set; } = "ATVPDKIKX0DER";
        public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
        public string CoverageStatus => AllRequestedChecksSucceeded ? "Unverified" : "Incomplete";
        // Phase 0 cannot infer completeness from HTTP success or absent cursors.
        public bool IsComplete { get; } = false;
        public List<LunaGame> Games { get; set; } = new List<LunaGame>();
        public List<QueryResult> Queries { get; set; } = new List<QueryResult>();
        public List<string> Diagnostics { get; set; } = new List<string>();
        public List<GameDetailObservation> DetailChecks { get; set; } = new List<GameDetailObservation>();
        public bool HasIdentityConflicts => Games.Any(g => g.IdentityConflict);
        public bool CatalogSetsAgree => IdentitySet(Queries.Where(q => q.Name == "AllGames"))
            .SetEquals(IdentitySet(Queries.Where(q => q.Name != "AllGames")));
        public bool AllQueriesSucceeded => Queries.Count == CatalogQuery.UsQueries.Count &&
            CatalogQuery.UsQueries.All(expected => Queries.Count(q => q.Name == expected.Name) == 1) &&
            Queries.All(q => q.ResponseValid && q.PaginationStatus == "NoContinuationObserved") &&
            CatalogSetsAgree && !HasIdentityConflicts;
        public bool AllRequestedChecksSucceeded => AllQueriesSucceeded && DetailChecks.All(d => d.ResponseValid);

        public static CatalogSnapshot Merge(IEnumerable<QueryResult> results)
        {
            var snapshot = new CatalogSnapshot { Queries = results.ToList() };
            var games = new Dictionary<string, LunaGame>(StringComparer.Ordinal);
            foreach (var query in snapshot.Queries)
            {
                foreach (var game in query.Games)
                {
                    var key = game.Region + ":" + game.Id;
                    if (games.TryGetValue(key, out var existing))
                    {
                        existing.Categories.UnionWith(game.Categories);
                        if (existing.Asin != null && game.Asin != null && existing.Asin != game.Asin)
                        {
                            existing.IdentityConflict = true;
                            snapshot.Diagnostics.Add("Conflicting ASINs for region-scoped product ID " + key + ".");
                        }
                        existing.IdentityConflict |= game.IdentityConflict;
                        if (existing.Asin == null) existing.Asin = game.Asin;
                        existing.HasPlayLink |= game.HasPlayLink;
                    }
                    else
                    {
                        // Copy so merging does not mutate per-query observations.
                        games.Add(key, new LunaGame
                        {
                            Id = game.Id, Asin = game.Asin, Title = game.Title, Url = game.Url,
                            Region = game.Region, CoverUrl = game.CoverUrl,
                            BackgroundUrl = game.BackgroundUrl, ReleaseYear = game.ReleaseYear,
                            Genres = new List<string>(game.Genres), PublishersText = game.PublishersText,
                            HasPlayLink = game.HasPlayLink, IdentityConflict = game.IdentityConflict,
                            Categories = new SortedSet<string>(game.Categories, StringComparer.Ordinal)
                        });
                    }
                }
            }
            foreach (var collision in games.Values.Where(g => g.Asin != null)
                .GroupBy(g => g.Region + ":" + g.Asin, StringComparer.Ordinal)
                .Where(group => group.Select(g => g.Id).Distinct(StringComparer.Ordinal).Count() > 1))
            {
                foreach (var game in collision) game.IdentityConflict = true;
                snapshot.Diagnostics.Add("Conflicting product IDs for region-scoped ASIN " + collision.Key + ".");
            }
            snapshot.Games = games.Values.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
            var all = new HashSet<string>(snapshot.Queries.Where(q => q.Name == "AllGames")
                .SelectMany(q => q.Games).Select(g => g.Region + ":" + g.Id), StringComparer.Ordinal);
            var categorized = new HashSet<string>(snapshot.Queries.Where(q => q.Name != "AllGames")
                .SelectMany(q => q.Games).Select(g => g.Region + ":" + g.Id), StringComparer.Ordinal);
            snapshot.Diagnostics.Add("AllGames minus category union: " + all.Except(categorized).Count());
            snapshot.Diagnostics.Add("Category union minus AllGames: " + categorized.Except(all).Count());
            snapshot.Diagnostics.Add("Website coverage and scroll behavior remain unverified; this is a research snapshot.");
            return snapshot;
        }

        private static HashSet<string> IdentitySet(IEnumerable<QueryResult> queries) =>
            new HashSet<string>(queries.SelectMany(q => q.Games).Select(g => g.Region + ":" + g.Id), StringComparer.Ordinal);
    }

    public sealed class GameDetailObservation
    {
        public string RequestedId { get; set; } = "";
        public string RequestedUrl { get; set; } = "";
        public int? HttpStatus { get; set; }
        public string? ObservedPageType { get; set; }
        public string? ReturnedId { get; set; }
        public string? Asin { get; set; }
        public string? Title { get; set; }
        public string? CanonicalUrl { get; set; }
        public string? PrimaryButtonText { get; set; }
        public bool IdentityMatches { get; set; }
        public bool UrlMatches { get; set; }
        public bool ResponseValid { get; set; }
        public bool StopFurtherRequests { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
    }
}
