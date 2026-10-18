using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace LunaCatalog
{
    public enum CatalogImportStatus { Unavailable, NotImported, Imported, Conflict }
    public enum ImportOutcomeKind { Added, Skipped, Failed, Cancelled }
    public sealed class ImportAlreadyExistsException : InvalidOperationException { }

    public sealed class ImportCandidate
    {
        public string Region { get; }
        public string LunaId { get; }
        public string Asin { get; }
        public string Title { get; }
        public string Url { get; }
        public string? CoverUrl { get; }
        public IReadOnlyList<string> Memberships { get; }
        internal ImportCandidate(LunaGame game)
        {
            Region = game.Region; LunaId = game.Id; Asin = game.Asin!; Title = game.Title;
            Url = game.Url; CoverUrl = game.CoverUrl; Memberships = Array.AsReadOnly(game.Categories.ToArray());
        }
    }

    public sealed class LibraryGameIdentity
    {
        public Guid PlayniteId { get; }
        public Guid PluginId { get; }
        public string GameId { get; }
        public LibraryGameIdentity(Guid playniteId, Guid pluginId, string gameId)
        { PlayniteId = playniteId; PluginId = pluginId; GameId = gameId; }
    }

    // Implementations use the SDK, never the host's underlying database files.
    public interface ICatalogImportDatabase
    {
        IReadOnlyList<LibraryGameIdentity> ReadIdentities();
        string? AddGame(ImportCandidate game, Guid playniteId, byte[]? coverBytes);
    }

    public sealed class ImportMapping
    {
        public string Region { get; set; } = "";
        public string LunaId { get; set; } = "";
        public string Asin { get; set; } = "";
        public Guid PlayniteId { get; set; }
    }
    public sealed class ImportMappingDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ImportMapping> Mappings { get; set; } = new List<ImportMapping>();
    }
    public sealed class ImportMappingReadResult
    {
        public ImportMappingDocument? Document { get; }
        public string Diagnostic { get; }
        public ImportMappingReadResult(ImportMappingDocument? document, string diagnostic = "")
        { Document = document; Diagnostic = diagnostic; }
    }
    public interface IImportMappingStore
    {
        ImportMappingReadResult Load();
        void Save(ImportMappingDocument document);
    }

    public sealed class CatalogImportState
    {
        public bool CanImport { get; }
        public IReadOnlyDictionary<string, CatalogImportStatus> Games { get; }
        public string Diagnostic { get; }
        public CatalogImportState(bool canImport, IDictionary<string, CatalogImportStatus> games, string diagnostic = "")
        { CanImport = canImport; Games = new ReadOnlyDictionary<string, CatalogImportStatus>(new Dictionary<string, CatalogImportStatus>(games, StringComparer.Ordinal)); Diagnostic = diagnostic; }
    }
    public sealed class ImportOutcome
    {
        public string LunaId { get; }
        public string Title { get; }
        public ImportOutcomeKind Kind { get; }
        public string Message { get; }
        public ImportOutcome(string lunaId, string title, ImportOutcomeKind kind, string message = "")
        { LunaId = lunaId; Title = title; Kind = kind; Message = message; }
    }
    public sealed class ImportBatchResult
    {
        public IReadOnlyList<ImportOutcome> Outcomes { get; }
        public int Added => Outcomes.Count(o => o.Kind == ImportOutcomeKind.Added);
        public int Skipped => Outcomes.Count(o => o.Kind == ImportOutcomeKind.Skipped);
        public int Failed => Outcomes.Count(o => o.Kind == ImportOutcomeKind.Failed);
        public int Cancelled => Outcomes.Count(o => o.Kind == ImportOutcomeKind.Cancelled);
        public string Summary => Added + " added · " + Skipped + " already imported · " + Failed + " failed" +
            (Cancelled > 0 ? " · " + Cancelled + " cancelled" : "");
        public ImportBatchResult(IEnumerable<ImportOutcome> outcomes) { Outcomes = Array.AsReadOnly(outcomes.ToArray()); }
    }

    public static class LunaImportIdentity
    {
        public static Guid PluginId { get; } = Guid.Parse("6f4229ca-70a9-4c65-ae9f-047a367beda9");
        // Older previews created manual games. Keep recognizing their durable markers during migration.
        public static bool IsOwner(Guid pluginId) => pluginId == PluginId || pluginId == Guid.Empty;
        private const string Marker = "LunaPlaynite:1";
        public static bool IsValid(string region, string lunaId, string asin) => region == "US" &&
            Regex.IsMatch(asin ?? "", "^[A-Z0-9]{10}$") && (lunaId == asin || CatalogValidator.IsNativeId(lunaId));
        public static string Encode(string region, string lunaId, string asin)
        {
            if (!IsValid(region, lunaId, asin)) throw new ArgumentException("Invalid Luna import identity.");
            return Marker + "|" + region + "|" + lunaId + "|" + asin;
        }
        public static bool TryDecode(string? marker, out ImportMapping identity)
        {
            identity = new ImportMapping();
            var parts = (marker ?? "").Split('|');
            if (parts.Length != 4 || parts[0] != Marker || !IsValid(parts[1], parts[2], parts[3])) return false;
            identity.Region = parts[1]; identity.LunaId = parts[2]; identity.Asin = parts[3]; return true;
        }
        public static bool Matches(ImportMapping owned, string id, string asin) => owned.Region == "US" && owned.Asin == asin &&
            (owned.LunaId == id || (owned.LunaId == asin && !CatalogValidator.IsNativeId(owned.LunaId)));
    }
}
