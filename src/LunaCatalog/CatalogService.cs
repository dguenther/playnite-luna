using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LunaCatalog
{
    public enum CatalogDataSource { None, Cache, Network }
    public enum CatalogErrorKind { Cache, Validation, Network, Storage, Cancelled }

    public sealed class CatalogError
    {
        public CatalogErrorKind Kind { get; }
        public IReadOnlyList<string> Diagnostics { get; }
        internal CatalogError(CatalogErrorKind kind, IReadOnlyList<string> diagnostics)
        {
            Kind = kind; Diagnostics = diagnostics;
        }
    }

    public sealed class CatalogState
    {
        public ValidatedCatalog? Catalog { get; }
        public CatalogDataSource Source { get; }
        public bool IsRefreshing { get; }
        public bool IsStale { get; }
        public bool HasCatalog => Catalog != null;
        public DateTimeOffset? LastSuccessfulRefresh => Catalog?.RetrievedAt;
        public CatalogError? Error { get; }
        public IReadOnlyList<string> CacheDiagnostics { get; }
        public CatalogChanges? Changes { get; }
        internal CatalogState(ValidatedCatalog? catalog, CatalogDataSource source, bool refreshing, bool stale,
            CatalogError? error, IReadOnlyList<string> cacheDiagnostics, CatalogChanges? changes)
        {
            Catalog = catalog; Source = source; IsRefreshing = refreshing; IsStale = stale;
            Error = error; CacheDiagnostics = cacheDiagnostics; Changes = changes;
        }
    }

    public sealed class CatalogService
    {
        private readonly ILunaCatalogProvider provider;
        private readonly ICatalogCache cache;
        private readonly string region;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan staleAfter;
        private readonly SemaphoreSlim operations = new SemaphoreSlim(1, 1);
        private readonly object stateLock = new object();
        private ValidatedCatalog? current;
        private CatalogDataSource source;
        private bool loaded, refreshing, recovered;
        private CatalogError? error;
        private IReadOnlyList<string> cacheDiagnostics = new string[0];
        private CatalogChanges? changes;

        public CatalogService(ILunaCatalogProvider provider, ICatalogCache cache, string region = "US",
            TimeSpan? staleAfter = null, Func<DateTimeOffset>? utcNow = null)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
            if (region != "US") throw new ArgumentException("Only the US catalog is supported.", nameof(region));
            this.region = region;
            this.staleAfter = staleAfter ?? TimeSpan.FromHours(24);
            if (this.staleAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        // State includes a copy of the catalog: UI callers cannot mutate the accepted baseline.
        public CatalogState State
        {
            get
            {
                lock (stateLock)
                    return new CatalogState(current == null ? null : CatalogData.Copy(current), source, refreshing,
                        current != null && (recovered || error != null || utcNow() - current.RetrievedAt >= staleAfter),
                        error, cacheDiagnostics, changes);
            }
        }

        // Startup can show cached data without contacting Amazon. Refresh is an explicit operation.
        public async Task<CatalogState> LoadAsync(CancellationToken cancellationToken)
        {
            await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await LoadOnceAsync(cancellationToken).ConfigureAwait(false); return State; }
            finally { operations.Release(); }
        }

        public async Task<CatalogState> RefreshAsync(CancellationToken cancellationToken)
        {
            await operations.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadOnceAsync(cancellationToken).ConfigureAwait(false);
                lock (stateLock) { refreshing = true; changes = null; }
                var snapshot = await provider.FetchAsync(region, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var networkDiagnostics = NetworkDiagnostics(snapshot);
                if (networkDiagnostics.Count > 0) SetError(CatalogErrorKind.Network, networkDiagnostics);
                else
                {
                    var candidate = CatalogValidator.ValidateSnapshot(snapshot, region, utcNow());
                    var comparison = current == null ? null : CatalogComparer.Compare(current, candidate, utcNow());
                    await cache.SaveAsync(CatalogData.Copy(candidate), cancellationToken).ConfigureAwait(false);
                    lock (stateLock)
                    {
                        current = candidate; source = CatalogDataSource.Network;
                        error = null; recovered = false; changes = comparison;
                        cacheDiagnostics = new string[0];
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SetError(CatalogErrorKind.Cancelled, new[] { "Catalog refresh was cancelled; previous data was retained." });
                throw;
            }
            catch (CatalogValidationException ex) { SetError(CatalogErrorKind.Validation, ex.Diagnostics); }
            catch (HttpRequestException) { SetError(CatalogErrorKind.Network, new[] { "Catalog network request failed; previous data was retained." }); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { SetError(CatalogErrorKind.Storage, new[] { "Catalog storage operation failed; previous data was retained." }); }
            finally
            {
                lock (stateLock) refreshing = false;
                operations.Release();
            }
            return State;
        }

        private async Task LoadOnceAsync(CancellationToken token)
        {
            if (loaded) return;
            var result = await cache.LoadAsync(region, token).ConfigureAwait(false);
            if (result.Catalog != null) CatalogValidator.ValidateCached(result.Catalog, region, utcNow());
            lock (stateLock)
            {
                current = result.Catalog == null ? null : CatalogData.Copy(result.Catalog);
                source = current == null ? CatalogDataSource.None : CatalogDataSource.Cache;
                recovered = result.RecoveredFromBackup;
                cacheDiagnostics = result.Diagnostics;
                if (current == null && result.Diagnostics.Count > 0)
                    error = new CatalogError(CatalogErrorKind.Cache, result.Diagnostics);
                loaded = true;
            }
        }

        private void SetError(CatalogErrorKind kind, IReadOnlyList<string> diagnostics)
        {
            lock (stateLock) { error = new CatalogError(kind, diagnostics); changes = null; }
        }

        private static IReadOnlyList<string> NetworkDiagnostics(CatalogSnapshot snapshot)
        {
            var diagnostics = new List<string>();
            foreach (var query in snapshot.Queries?.Where(q => q != null && q.FailureKind == CatalogFetchFailureKind.Network)
                ?? Enumerable.Empty<QueryResult>())
            {
                diagnostics.Add(query.Name + " browse request failed (HTTP " + (query.HttpStatus?.ToString() ?? "unavailable") + ").");
                if (query.Diagnostics != null) diagnostics.AddRange(query.Diagnostics);
            }
            foreach (var detail in snapshot.DetailChecks?.Where(d => d != null && d.FailureKind == CatalogFetchFailureKind.Network)
                ?? Enumerable.Empty<GameDetailObservation>())
            {
                diagnostics.Add("A requested detail network check failed (HTTP " + (detail.HttpStatus?.ToString() ?? "unavailable") + ").");
                if (detail.Diagnostics != null) diagnostics.AddRange(detail.Diagnostics);
            }
            return diagnostics.AsReadOnly();
        }
    }
}
