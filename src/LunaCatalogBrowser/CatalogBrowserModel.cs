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
    public sealed class CatalogGameItem
    {
        public string Id { get; }
        public string Title { get; }
        public string Url { get; }
        public string? CoverUrl { get; }
        public IReadOnlyList<string> Memberships { get; }
        public string MembershipText => string.Join(" · ", Memberships);
        public string Details { get; }
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

        public CatalogBrowserModel(CatalogService service, Action<string>? reportError = null)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.reportError = reportError; }

        public event PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<CatalogCategory> Categories { get; } = Array.AsReadOnly((CatalogCategory[])Enum.GetValues(typeof(CatalogCategory)));
        public IReadOnlyList<CatalogTitleSort> TitleSorts { get; } = Array.AsReadOnly((CatalogTitleSort[])Enum.GetValues(typeof(CatalogTitleSort)));
        public IReadOnlyList<CatalogGameItem> VisibleGames => visibleGames;
        public int TotalCount => games.Length;
        public int VisibleCount => visibleGames.Count;
        public string CountText => VisibleCount + " of " + TotalCount + " games";
        public bool IsBusy => busy;
        public bool CanRefresh => !busy;
        public bool CanOpenSelected => selectedGame != null;
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
                if (value != null && !visibleGames.Contains(value)) throw new ArgumentException("Select a visible catalog game.", nameof(value));
                if (selectedGame == value) return;
                selectedGame = value; Changed(); Changed(nameof(CanOpenSelected));
            }
        }

        public Task LoadAsync(CancellationToken token) => RunAsync(refresh: false, token);
        public Task RefreshAsync(CancellationToken token) => RunAsync(refresh: true, token);
        public void CancelCurrentOperation() => operation?.Cancel();
        public void UpdateStatus() { if (!busy) { state = service.State; NotifyState(); } }

        private async Task RunAsync(bool refresh, CancellationToken token)
        {
            if (busy) return;
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                operation = cancellation; busy = true; cancelled = false; operationError = null; NotifyState();
                try
                {
                    var result = await Task.Run(() => refresh ? service.RefreshAsync(cancellation.Token) : service.LoadAsync(cancellation.Token));
                    Apply(result);
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
            Filter();
        }

        private void Filter()
        {
            var selectedId = selectedGame?.Id;
            var search = searchText.Trim();
            var matching = games.Where(game => (category == CatalogCategory.All || game.Memberships.Contains(category.ToString())) &&
                game.Title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            var sorted = titleSort == CatalogTitleSort.Ascending ? matching.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase) :
                matching.OrderByDescending(game => game.Title, StringComparer.OrdinalIgnoreCase);
            visibleGames = Array.AsReadOnly(sorted.ThenBy(game => game.Id, StringComparer.Ordinal).ToArray());
            Changed(nameof(VisibleGames)); Changed(nameof(TotalCount)); Changed(nameof(VisibleCount));
            Changed(nameof(CountText)); Changed(nameof(EmptyText));
            // A bound list can clear its old selection when ItemsSource changes.
            // Restore the retained identity after publishing the new items.
            SelectedGame = visibleGames.FirstOrDefault(game => game.Id == selectedId);
        }

        private void NotifyState()
        {
            Changed(nameof(IsBusy)); Changed(nameof(CanRefresh)); Changed(nameof(IsStale));
            Changed(nameof(LastSuccessfulRefresh)); Changed(nameof(LastRefreshText));
            Changed(nameof(ErrorText)); Changed(nameof(StatusText)); Changed(nameof(EmptyText));
        }
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
