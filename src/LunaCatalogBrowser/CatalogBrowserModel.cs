using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LunaCatalog;

namespace LunaCatalogBrowser
{
    public enum CatalogCategory { All, Standard, Premium, GameNight }
    public enum CatalogTitleSort { Ascending, Descending }

    // Read-only display values keep filtering and UI edits away from the accepted snapshot.
    public sealed class CatalogGameItem : INotifyPropertyChanged
    {
        public string Id { get; }
        public string Title { get; }
        public string Url { get; }
        public string? CoverUrl { get; }
        public IReadOnlyList<string> Memberships { get; }
        public string MembershipText => string.Join(" · ", Memberships);
        public string Details { get; }
        private CatalogImportStatus importStatus;
        public CatalogImportStatus ImportStatus => importStatus;
        public string ImportStatusText => importStatus == CatalogImportStatus.Imported ? "Imported" :
            importStatus == CatalogImportStatus.NotImported ? "Not imported" : importStatus == CatalogImportStatus.Conflict ? "Import conflict" : "Import status unavailable";
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void SetImportStatus(CatalogImportStatus value)
        { if (importStatus == value) return; importStatus = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportStatus))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportStatusText))); }
        internal CatalogGameItem(LunaGame game)
        {
            Id = game.Id; Title = game.Title; Url = game.Url; CoverUrl = game.CoverUrl;
            Memberships = Array.AsReadOnly(game.Categories.ToArray());
            Details = string.Join(" · ", new[] { game.ReleaseYear?.ToString(), string.Join(", ", game.Genres) }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    // Call from the UI context. Await continuations publish properties on that context;
    // service I/O, JSON parsing, and validation run on a worker, including cached startup.
    public sealed class CatalogBrowserModel : INotifyPropertyChanged
    {
        private readonly CatalogService service;
        private readonly Action<string>? reportError;
        private readonly CatalogImportService? imports;
        private CatalogGameItem[] games = new CatalogGameItem[0];
        private IReadOnlyList<CatalogGameItem> visibleGames = Array.AsReadOnly(new CatalogGameItem[0]);
        private CancellationTokenSource? operation;
        private string searchText = "";
        private CatalogCategory category;
        private CatalogTitleSort titleSort;
        private CatalogGameItem? selectedGame;
        private CatalogState? state;
        private bool busy;
        private bool cancelled;
        private string? operationError;
        private readonly HashSet<string> selectedIds = new HashSet<string>(StringComparer.Ordinal);
        private bool filtering;
        private bool importing;
        private bool checkingImports;
        private Task? importCheck;
        private CatalogImportState? importState;
        private string importSummary = "";
        private string importDetails = "";

        public CatalogBrowserModel(CatalogService service, Action<string>? reportError = null, CatalogImportService? imports = null)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.reportError = reportError; this.imports = imports; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<CatalogCategory> Categories { get; } = Array.AsReadOnly((CatalogCategory[])Enum.GetValues(typeof(CatalogCategory)));
        public IReadOnlyList<CatalogTitleSort> TitleSorts { get; } = Array.AsReadOnly((CatalogTitleSort[])Enum.GetValues(typeof(CatalogTitleSort)));
        public IReadOnlyList<CatalogGameItem> VisibleGames => visibleGames;
        public int TotalCount => games.Length;
        public int VisibleCount => visibleGames.Count;
        public string CountText => VisibleCount + " of " + TotalCount + " games";
        public bool IsBusy => busy || importing || checkingImports;
        public bool CanRefresh => !IsBusy;
        public bool CanCancel => operation != null;
        public bool CanOpenSelected => selectedGame != null;
        public IReadOnlyList<CatalogGameItem> SelectedGames => Array.AsReadOnly(visibleGames.Where(g => selectedIds.Contains(g.Id)).ToArray());
        public string SelectionText => selectedIds.Count + " selected · Ctrl/Shift-click to select multiple games";
        public bool CanImportSelected => !IsBusy && importState?.CanImport == true && selectedIds.Count > 0;
        public bool CanImportAll => !IsBusy && importState?.CanImport == true && games.Length > 0;
        public string ImportAllText => "Import All (" + TotalCount + ")";
        public string ImportSummaryText => importing ? "Importing selected Luna games…" : importSummary;
        public string ImportDetailsText => importDetails;
        public string ImportErrorText => importState?.Diagnostic ?? "";
        public bool IsStale => games.Length > 0 && ((state?.IsStale ?? false) || operationError != null);
        public DateTimeOffset? LastSuccessfulRefresh => state?.LastSuccessfulRefresh;
        public string LastRefreshText => LastSuccessfulRefresh.HasValue ?
            "Last refreshed " + LastSuccessfulRefresh.Value.ToLocalTime().ToString("g") : "No successful refresh yet";
        public string ErrorText => operationError ?? (state?.Error == null ? "" : string.Join(" ", state.Error.Diagnostics));
        public string StatusText => busy ? (games.Length == 0 ? "Loading catalog…" : "Loading · saved games remain available") :
            cancelled ? (games.Length == 0 ? "Loading cancelled · choose Refresh to try again" : "Refresh cancelled · showing saved catalog") :
            state?.Error != null || operationError != null ? (games.Length == 0 ? "Catalog unavailable · try Refresh" : "Refresh failed · showing saved catalog") :
            games.Length == 0 ? "No saved catalog · choose Refresh to download US streaming games" :
            IsStale ? "Saved catalog · refresh recommended" :
            state?.Source == CatalogDataSource.Cache ? "Saved catalog · available offline" : "Catalog up to date";
        public string EmptyText => busy ? "Loading catalog…" : games.Length == 0 ?
            "Choose Refresh to download the public US streaming catalog." : "No games match your search and category.";

        public string SearchText
        {
            get => searchText;
            set { if (searchText == (value ?? "")) return; searchText = value ?? ""; Changed(); Filter(); }
        }
        public CatalogCategory Category
        {
            get => category;
            set
            {
                if (!Enum.IsDefined(typeof(CatalogCategory), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (category == value) return; category = value; Changed(); Filter();
            }
        }
        public CatalogTitleSort TitleSort
        {
            get => titleSort;
            set
            {
                if (!Enum.IsDefined(typeof(CatalogTitleSort), value)) throw new ArgumentOutOfRangeException(nameof(value));
                if (titleSort == value) return; titleSort = value; Changed(); Filter();
            }
        }
        public CatalogGameItem? SelectedGame
        {
            get => selectedGame;
            set
            {
                if (filtering) return;
                if (value != null && !visibleGames.Contains(value)) throw new ArgumentException("Select a visible catalog game.", nameof(value));
                if (selectedGame == value) return;
                selectedIds.Clear(); if (value != null) selectedIds.Add(value.Id);
                selectedGame = value; NotifySelection();
            }
        }

        public void SetSelectedGames(IEnumerable<CatalogGameItem> selection)
        {
            if (filtering) return;
            var items = selection.Distinct().ToArray();
            if (items.Any(item => !visibleGames.Contains(item))) throw new ArgumentException("Select visible catalog games.", nameof(selection));
            selectedIds.Clear(); foreach (var item in items) selectedIds.Add(item.Id);
            if (selectedGame == null || !selectedIds.Contains(selectedGame.Id)) selectedGame = items.FirstOrDefault();
            NotifySelection();
        }

        public Task LoadAsync(CancellationToken token) => RunAsync(refresh: false, token);
        public Task RefreshAsync(CancellationToken token) => RunAsync(refresh: true, token);
        public void CancelCurrentOperation() => operation?.Cancel();
        public void UpdateStatus() { if (!IsBusy) { state = service.State; NotifyState(); } }
        public Task ImportSelectedAsync(CancellationToken token) => ImportAsync(selectedIds.ToArray(), token);
        public Task ImportAllAsync(CancellationToken token) => ImportAsync(games.Select(g => g.Id).ToArray(), token);
        public Task ReconcileImportsAsync(CancellationToken token)
        {
            if (IsBusy || imports == null || state?.Catalog == null) return Task.CompletedTask;
            importCheck = CheckImportsAsync(token); return importCheck;
        }
        private async Task CheckImportsAsync(CancellationToken token)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                operation = cancellation; checkingImports = true; NotifyState();
                try { await ReadImportStateAsync(cancellation.Token); }
                finally { operation = null; checkingImports = false; NotifyState(); }
            }
        }

        private async Task ReadImportStateAsync(CancellationToken token)
        {
            if (imports == null || state?.Catalog == null) return;
            try { importState = await imports.ReconcileAsync(state.Catalog, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                reportError?.Invoke("Luna import status failed (" + ex.GetType().Name + ").");
                importState = new CatalogImportState(false, games.ToDictionary(g => g.Id, _ => CatalogImportStatus.Unavailable),
                    "Import status could not be checked. Reopen the browser and retry.");
            }
            foreach (var game in games) game.SetImportStatus(importState!.Games.TryGetValue(game.Id, out var status) ? status : CatalogImportStatus.Unavailable);
            NotifyImportState();
        }

        private async Task ImportAsync(string[] ids, CancellationToken token)
        {
            // A host confirmation dialog can reactivate the browser and start reconciliation.
            // Retain the confirmed import intent until that check completes.
            if (checkingImports && importCheck != null)
            {
                var pendingToken = operation?.Token ?? CancellationToken.None;
                await importCheck;
                // Closing the view cancels this check and must also discard its pending import.
                if (pendingToken.IsCancellationRequested || token.IsCancellationRequested) return;
            }
            if (IsBusy || imports == null || importState?.CanImport != true || state?.Catalog == null || ids.Length == 0) return;
            var catalog = state.Catalog;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                operation = cancellation; importing = true; importSummary = ""; importDetails = ""; NotifyState();
                try
                {
                    var result = await imports.ImportAsync(catalog, ids, cancellation.Token);
                    importSummary = result.Summary;
                    importDetails = string.Join("\n", result.Outcomes.Where(o => o.Message.Length > 0).Select(o => o.Title + ": " + o.Message));
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { importSummary = "Import cancelled. Previously added games remain in your library."; }
                catch (Exception ex)
                { reportError?.Invoke("Luna library import failed (" + ex.GetType().Name + ")."); importSummary = "Import could not finish. Check the library and extension data folder, then retry."; }
                finally
                {
                    await ReadImportStateAsync(CancellationToken.None);
                    operation = null; importing = false; NotifyState();
                }
            }
        }

        private async Task RunAsync(bool refresh, CancellationToken token)
        {
            if (IsBusy) return;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                operation = cancellation; busy = true; cancelled = false; operationError = null; NotifyState();
                try
                {
                    var result = await Task.Run(() => refresh ? service.RefreshAsync(cancellation.Token) : service.LoadAsync(cancellation.Token));
                    Apply(result);
                    await ReadImportStateAsync(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    Apply(service.State);
                    cancelled = true;
                    operationError = "Catalog operation cancelled; previous games were retained.";
                }
                catch (Exception ex)
                {
                    Apply(service.State);
                    operationError = "The catalog could not be loaded. Try Refresh again.";
                    reportError?.Invoke("Luna catalog operation failed (" + ex.GetType().Name + ").");
                }
                finally { operation = null; busy = false; NotifyState(); }
            }
        }

        private void Apply(CatalogState result)
        {
            state = result;
            games = result.Catalog?.Games.Select(game => new CatalogGameItem(game)).ToArray() ?? new CatalogGameItem[0];
            foreach (var game in games) game.SetImportStatus(importState != null && importState.Games.TryGetValue(game.Id, out var status) ? status : CatalogImportStatus.Unavailable);
            Filter();
        }

        private void Filter()
        {
            var selectedId = selectedGame?.Id;
            var retainedIds = selectedIds.ToArray();
            var search = searchText.Trim();
            var matching = games.Where(game => (category == CatalogCategory.All || game.Memberships.Contains(category.ToString())) &&
                game.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            var sorted = titleSort == CatalogTitleSort.Ascending ? matching.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase) :
                matching.OrderByDescending(game => game.Title, StringComparer.OrdinalIgnoreCase);
            visibleGames = Array.AsReadOnly(sorted.ThenBy(game => game.Id, StringComparer.Ordinal).ToArray());
            filtering = true;
            try
            {
                Changed(nameof(VisibleGames)); Changed(nameof(TotalCount)); Changed(nameof(VisibleCount));
                Changed(nameof(CountText)); Changed(nameof(EmptyText)); Changed(nameof(ImportAllText));
                selectedIds.Clear(); foreach (var game in visibleGames.Where(g => retainedIds.Contains(g.Id))) selectedIds.Add(game.Id);
                selectedGame = visibleGames.FirstOrDefault(game => game.Id == selectedId) ?? visibleGames.FirstOrDefault(g => selectedIds.Contains(g.Id));
            }
            finally { filtering = false; }
            NotifySelection();
        }

        private void NotifyState()
        {
            Changed(nameof(IsBusy)); Changed(nameof(CanRefresh)); Changed(nameof(IsStale));
            Changed(nameof(CanCancel));
            Changed(nameof(LastSuccessfulRefresh)); Changed(nameof(LastRefreshText));
            Changed(nameof(ErrorText)); Changed(nameof(StatusText)); Changed(nameof(EmptyText));
            NotifyImportState();
        }
        private void NotifySelection()
        { Changed(nameof(SelectedGame)); Changed(nameof(SelectedGames)); Changed(nameof(CanOpenSelected)); Changed(nameof(SelectionText)); Changed(nameof(CanImportSelected)); }
        private void NotifyImportState()
        { Changed(nameof(CanImportSelected)); Changed(nameof(CanImportAll)); Changed(nameof(ImportSummaryText)); Changed(nameof(ImportDetailsText)); Changed(nameof(ImportErrorText)); }
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
