using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LunaCatalog
{
    public static class CatalogValidator
    {
        public const string UsMarketplaceId = "ATVPDKIKX0DER";

        public static ValidatedCatalog ValidateSnapshot(CatalogSnapshot snapshot, string region, DateTimeOffset now)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Require(region == "US" && snapshot.Region == region && snapshot.MarketplaceId == UsMarketplaceId,
                "Catalog region or marketplace does not match the requested US scope.");
            if (snapshot.Queries == null || snapshot.Games == null || snapshot.DetailChecks == null || snapshot.Diagnostics == null)
                throw new CatalogValidationException("Invalid source snapshot structure.");
            Require(snapshot.SchemaVersion == 1, "Unsupported source snapshot schema.");
            Require(snapshot.Queries.Count == CatalogQuery.UsQueries.Count, "All four browse queries are required.");
            foreach (var expected in CatalogQuery.UsQueries)
            {
                var matches = snapshot.Queries.Where(q => q != null && q.Name == expected.Name).ToList();
                Require(matches.Count == 1, "Missing or duplicate required browse query.");
                var query = matches[0];
                if (query.Games == null || query.Diagnostics == null)
                    throw new CatalogValidationException("Malformed browse query observations.");
                if (!query.ResponseValid || query.StopFurtherRequests || query.FailureKind != CatalogFetchFailureKind.None)
                    throw new CatalogValidationException(new[] { expected.Name + " browse response failed validation (HTTP " +
                        (query.HttpStatus?.ToString() ?? "unavailable") + ")." }.Concat(query.Diagnostics));
                ValidateQuery(expected, query.ObservedPageType, query.PaginationStatus, query.PagesRetrieved,
                    query.TilesObserved, query.HttpStatus, query.Games.Count);
                foreach (var game in query.Games)
                {
                    ValidateGame(game, region);
                    Require(game.Categories.SetEquals(expected.Membership == null ? new string[0] : new[] { expected.Membership }),
                        "Query memberships do not match the requested browse category.");
                }
            }
            Require(snapshot.DetailChecks.All(d => d != null && d.ResponseValid && d.FailureKind == CatalogFetchFailureKind.None),
                "A requested detail check failed.");
            var merged = CatalogSnapshot.Merge(snapshot.Queries);
            Require(merged.AllQueriesSucceeded, "All Games and the category union disagree, or identities conflict.");
            Require(snapshot.Games.Count == merged.Games.Count, "Normalized games do not match the browse observations.");
            var observed = merged.Games.ToDictionary(g => g.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in snapshot.Games)
            {
                ValidateGame(game, region);
                Require(seen.Add(game.Id) && observed.TryGetValue(game.Id, out var expected) && CatalogData.GamesEqual(game, expected),
                    "Normalized games do not match the browse observations.");
            }
            var catalog = new ValidatedCatalog
            {
                SchemaVersion = ValidatedCatalog.CurrentSchemaVersion,
                ValidationContract = ValidatedCatalog.CurrentContract,
                Region = region, MarketplaceId = snapshot.MarketplaceId,
                RetrievedAt = snapshot.RetrievedAt, ValidatedAt = now,
                Games = snapshot.Games.Select(CatalogData.CopyGame).ToList(),
                Diagnostics = snapshot.Diagnostics.Where(d => d != CatalogSnapshot.ResearchCoverageDiagnostic).ToList(),
                Queries = CatalogQuery.UsQueries.Select(expected =>
                {
                    var query = snapshot.Queries.Single(q => q.Name == expected.Name);
                    return new CatalogQuerySummary
                    {
                        Name = expected.Name, Query = expected.Query, ObservedPageType = query.ObservedPageType!,
                        HttpStatus = query.HttpStatus, PagesRetrieved = query.PagesRetrieved,
                        TilesObserved = query.TilesObserved, PaginationStatus = query.PaginationStatus,
                        GameIds = query.Games.Select(g => g.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList(),
                        Diagnostics = new List<string>(query.Diagnostics)
                    };
                }).ToList()
            };
            ValidateCached(catalog, region, now);
            return catalog;
        }

        // Validate the normalized cache again; serialized status flags are not trusted.
        public static void ValidateCached(ValidatedCatalog catalog, string region, DateTimeOffset now)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            Require(catalog.SchemaVersion == ValidatedCatalog.CurrentSchemaVersion &&
                catalog.ValidationContract == ValidatedCatalog.CurrentContract, "Unsupported cache schema or validation contract.");
            Require(region == "US" && catalog.Region == region && catalog.MarketplaceId == UsMarketplaceId,
                "Cache region or marketplace does not match the requested US scope.");
            Require(catalog.RetrievedAt != default && catalog.ValidatedAt >= catalog.RetrievedAt &&
                catalog.ValidatedAt <= now.AddMinutes(5), "Invalid catalog retrieval or validation time.");
            if (catalog.Games == null || catalog.Queries == null || catalog.Diagnostics == null)
                throw new CatalogValidationException("Malformed cached catalog structure.");
            Require(catalog.Games.Count > 0, "An empty catalog cannot replace cached data.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var asins = new HashSet<string>(StringComparer.Ordinal);
            foreach (var game in catalog.Games)
            {
                ValidateGame(game, region);
                Require(ids.Add(game.Id) && asins.Add(game.Asin!), "Duplicate or conflicting region-scoped game identities.");
                Require(game.Categories.Count > 0 && game.Categories.All(c => CatalogQuery.UsQueries.Any(q => q.Membership == c)),
                    "Unsupported or missing catalog membership.");
            }
            Require(catalog.Queries.Count == CatalogQuery.UsQueries.Count, "All four browse query summaries are required.");
            foreach (var expected in CatalogQuery.UsQueries)
            {
                var matches = catalog.Queries.Where(q => q != null && q.Name == expected.Name).ToList();
                Require(matches.Count == 1, "Missing or duplicate required browse query summary.");
                var query = matches[0];
                if (query.GameIds == null || query.Diagnostics == null)
                    throw new CatalogValidationException("Malformed browse query summary.");
                Require(query.Query == expected.Query, "Invalid browse query summary.");
                var queryIds = new HashSet<string>(query.GameIds, StringComparer.Ordinal);
                Require(queryIds.Count == query.GameIds.Count, "Duplicate game identities in a browse query summary.");
                ValidateQuery(expected, query.ObservedPageType, query.PaginationStatus, query.PagesRetrieved,
                    query.TilesObserved, query.HttpStatus, queryIds.Count);
                var membershipIds = catalog.Games.Where(g => expected.Membership == null || g.Categories.Contains(expected.Membership))
                    .Select(g => g.Id);
                Require(queryIds.SetEquals(membershipIds), "Browse query identities disagree with normalized catalog memberships.");
            }
        }

        private static void ValidateQuery(CatalogQuery expected, string? pageType, string pagination, int pages, int tiles, int? status, int games)
        {
            Require(pageType == expected.ExpectedPageType && pagination == "NoContinuationObserved" &&
                pages == 1 && games > 0 && tiles >= games && (!status.HasValue || (status >= 200 && status < 300)),
                "Browse page type, HTTP status, totals, or continuation do not satisfy the established contract.");
        }

        private static void ValidateGame(LunaGame game, string region)
        {
            if (game == null || game.Categories == null || game.Genres == null)
                throw new CatalogValidationException("Malformed game record.");
            Require(game.Region == region && !string.IsNullOrWhiteSpace(game.Title) && game.HasPlayLink && !game.IdentityConflict,
                "Unreliable or malformed game record.");
            Require(game.Asin != null && Regex.IsMatch(game.Asin, @"^[A-Z0-9]{10}$") &&
                (game.Id == game.Asin || IsNativeId(game.Id)), "Unsupported or conflicting game identity.");
            Require(Uri.TryCreate(game.Url, UriKind.Absolute, out var uri) && SafeAuthority(uri, "luna.amazon.com"),
                "Unsafe Luna game-page URL.");
            var match = Regex.Match(uri!.AbsolutePath, @"^/game/[^/]+(?:/([A-Z0-9]{10}))?/?$");
            Require(match.Success && (!match.Groups[1].Success || match.Groups[1].Value == game.Asin),
                "Luna game-page URL does not match the game identity.");
            Require(SafeImage(game.CoverUrl) && SafeImage(game.BackgroundUrl) && game.Genres.All(g => g != null),
                "Unsafe or malformed game metadata.");
        }

        internal static bool IsNativeId(string? id)
        {
            var match = Regex.Match(id ?? "", @"^amzn1\.(?:adg|luna)\.product\.([a-fA-F0-9-]+)$");
            return match.Success && Guid.TryParse(match.Groups[1].Value, out _);
        }

        private static bool SafeAuthority(Uri uri, string host) =>
            uri.Scheme == "https" && uri.Host == host && uri.Port == 443 && uri.UserInfo.Length == 0;
        private static bool SafeImage(string? url) => url == null ||
            (Uri.TryCreate(url, UriKind.Absolute, out var uri) && SafeAuthority(uri, "m.media-amazon.com"));
        private static void Require(bool condition, string diagnostic)
        {
            if (!condition) throw new CatalogValidationException(diagnostic);
        }
    }
}
