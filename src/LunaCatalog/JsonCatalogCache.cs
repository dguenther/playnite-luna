using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace LunaCatalog
{
    public interface ICatalogCache
    {
        Task<CatalogCacheReadResult> LoadAsync(string region, CancellationToken cancellationToken);
        Task SaveAsync(ValidatedCatalog catalog, CancellationToken cancellationToken);
    }

    public sealed class CatalogCacheReadResult
    {
        public ValidatedCatalog? Catalog { get; }
        public bool RecoveredFromBackup { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        public CatalogCacheReadResult(ValidatedCatalog? catalog, bool recoveredFromBackup, IReadOnlyList<string> diagnostics)
        {
            Catalog = catalog; RecoveredFromBackup = recoveredFromBackup; Diagnostics = diagnostics;
        }
    }

    public sealed class JsonCatalogCache : ICatalogCache
    {
        public const int MaximumCacheBytes = 8 * 1024 * 1024;
        private readonly string directory;
        private readonly Func<DateTimeOffset> utcNow;
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(), Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 64
        };

        // The Playnite host supplies its plugin data directory; this library has no SDK dependency.
        public JsonCatalogCache(string directory, Func<DateTimeOffset>? utcNow = null)
        {
            this.directory = Path.GetFullPath(directory ?? throw new ArgumentNullException(nameof(directory)));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        private string CachePath(string region)
        {
            if (region != "US") throw new ArgumentException("Only the US catalog is supported.", nameof(region));
            return Path.Combine(directory, "catalog-US.json");
        }

        public async Task<CatalogCacheReadResult> LoadAsync(string region, CancellationToken cancellationToken)
        {
            var path = CachePath(region);
            var diagnostics = new List<string>();
            for (var attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var catalog = await ReadAsync(attempt == 0 ? path : path + ".bak", region, cancellationToken).ConfigureAwait(false);
                    if (attempt == 1) diagnostics.Add("Recovered the previous validated catalog from backup.");
                    return new CatalogCacheReadResult(catalog, attempt == 1, diagnostics.AsReadOnly());
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException) { }
                catch (Exception ex) when (IsCacheFailure(ex))
                {
                    diagnostics.Add(attempt == 0 ? "The primary cache is invalid or unavailable." : "The backup cache is invalid or unavailable.");
                }
            }
            return new CatalogCacheReadResult(null, false, diagnostics.AsReadOnly());
        }

        public async Task SaveAsync(ValidatedCatalog catalog, CancellationToken cancellationToken)
        {
            // Take a copy before any await, so callers cannot change the serialized candidate in flight.
            CatalogValidator.ValidateCached(catalog, catalog.Region, utcNow());
            var candidate = CatalogData.Copy(catalog);
            var path = CachePath(candidate.Region);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = new UTF8Encoding(false, true).GetBytes(JsonConvert.SerializeObject(candidate, Settings) + "\n");
            if (bytes.Length > MaximumCacheBytes) throw new IOException("Normalized cache exceeds the size limit.");
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                {
                    await output.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                // Only a valid primary becomes the backup. Never replace a good backup with a corrupt primary.
                var validPrimary = false;
                try
                {
                    await ReadAsync(path, candidate.Region, cancellationToken).ConfigureAwait(false);
                    validPrimary = true;
                }
                catch (Exception ex) when (IsCacheFailure(ex)) { }
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(path)) File.Replace(temporary, path, validPrimary ? path + ".bak" : null);
                else File.Move(temporary, path);
                // Replacement is the commit point; cancellation after it cannot roll back a successful save.
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private async Task<ValidatedCatalog> ReadAsync(string path, string region, CancellationToken token)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 8192, useAsync: true))
            using (var bytes = new MemoryStream())
            {
                if (input.Length > MaximumCacheBytes) throw new IOException("Cache exceeds the size limit.");
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + read > MaximumCacheBytes) throw new IOException("Cache exceeds the size limit.");
                    bytes.Write(buffer, 0, read);
                }
                token.ThrowIfCancellationRequested();
                var text = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                var root = LunaJson.ParseObject(text, rejectDuplicateProperties: true);
                var catalog = root.ToObject<ValidatedCatalog>(JsonSerializer.Create(Settings))!;
                CatalogValidator.ValidateCached(catalog, region, utcNow());
                return catalog;
            }
        }

        private static bool IsCacheFailure(Exception ex) => ex is IOException || ex is UnauthorizedAccessException ||
            ex is JsonException || ex is CatalogValidationException || ex is DecoderFallbackException;
    }
}
