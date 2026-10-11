using System;
using System.Collections.Generic;
using System.Linq;

namespace LunaCatalog
{
    // A runtime validation result, distinct from the Python research audit receipt.
    public sealed class ValidatedCatalog
    {
        public const int CurrentSchemaVersion = 1;
        public const string CurrentContract = "PublicUSStreamingBrowseV1";
        public int SchemaVersion { get; set; }
        public string ValidationContract { get; set; } = "";
        public string Region { get; set; } = "";
        public string MarketplaceId { get; set; } = "";
        public DateTimeOffset RetrievedAt { get; set; }
        public DateTimeOffset ValidatedAt { get; set; }
        public string CoverageStatus => "Validated";
        public bool IsComplete => true;
        public List<LunaGame> Games { get; set; } = new List<LunaGame>();
        public List<CatalogQuerySummary> Queries { get; set; } = new List<CatalogQuerySummary>();
        public List<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class CatalogQuerySummary
    {
        public string Name { get; set; } = "";
        public string Query { get; set; } = "";
        public string ObservedPageType { get; set; } = "";
        public int? HttpStatus { get; set; }
        public int PagesRetrieved { get; set; }
        public int TilesObserved { get; set; }
        public string PaginationStatus { get; set; } = "";
        public List<string> GameIds { get; set; } = new List<string>();
        public List<string> Diagnostics { get; set; } = new List<string>();
    }

    public sealed class CatalogValidationException : Exception
    {
        public IReadOnlyList<string> Diagnostics { get; }
        public CatalogValidationException(string diagnostic) : this(new[] { diagnostic }) { }
        public CatalogValidationException(IEnumerable<string> diagnostics) : base("Catalog validation failed.")
        {
            Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        }
    }

    internal static class CatalogData
    {
        internal static LunaGame CopyGame(LunaGame game) => new LunaGame
        {
            Id = game.Id, Asin = game.Asin, Title = game.Title, Url = game.Url,
            Region = game.Region, CoverUrl = game.CoverUrl, BackgroundUrl = game.BackgroundUrl,
            ReleaseYear = game.ReleaseYear, Genres = new List<string>(game.Genres),
            PublishersText = game.PublishersText, HasPlayLink = game.HasPlayLink,
            IdentityConflict = game.IdentityConflict,
            Categories = new SortedSet<string>(game.Categories, StringComparer.Ordinal)
        };

        internal static bool GamesEqual(LunaGame a, LunaGame b) =>
            a.Id == b.Id && a.Asin == b.Asin && a.Region == b.Region && a.Title == b.Title &&
            a.Url == b.Url && a.CoverUrl == b.CoverUrl && a.BackgroundUrl == b.BackgroundUrl &&
            a.ReleaseYear == b.ReleaseYear && a.PublishersText == b.PublishersText &&
            a.HasPlayLink == b.HasPlayLink && a.IdentityConflict == b.IdentityConflict &&
            a.Categories.SetEquals(b.Categories) && a.Genres.SequenceEqual(b.Genres);

        internal static ValidatedCatalog Copy(ValidatedCatalog catalog) => new ValidatedCatalog
        {
            SchemaVersion = catalog.SchemaVersion, ValidationContract = catalog.ValidationContract,
            Region = catalog.Region, MarketplaceId = catalog.MarketplaceId,
            RetrievedAt = catalog.RetrievedAt, ValidatedAt = catalog.ValidatedAt,
            Games = catalog.Games.Select(CopyGame).ToList(),
            Diagnostics = new List<string>(catalog.Diagnostics),
            Queries = catalog.Queries.Select(q => new CatalogQuerySummary
            {
                Name = q.Name, Query = q.Query, ObservedPageType = q.ObservedPageType,
                HttpStatus = q.HttpStatus, PagesRetrieved = q.PagesRetrieved,
                TilesObserved = q.TilesObserved, PaginationStatus = q.PaginationStatus,
                GameIds = new List<string>(q.GameIds), Diagnostics = new List<string>(q.Diagnostics)
            }).ToList()
        };
    }
}
