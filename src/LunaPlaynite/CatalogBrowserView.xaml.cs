using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LunaCatalogBrowser;

namespace LunaPlaynite
{
    public partial class CatalogBrowserView : UserControl
    {
        private readonly CatalogBrowserModel model;
        private readonly Action<CatalogGameItem> openGame;
        private bool started;
        private bool synchronizingSelection;
        private readonly Func<int, bool>? confirmImportAll;
        private readonly DispatcherTimer ageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        internal CoverImageCache Covers { get; }
        public CatalogBrowserView(CatalogBrowserModel model, CoverImageCache covers, Action<CatalogGameItem> openGame, Func<int, bool>? confirmImportAll = null)
        {
            this.model = model; Covers = covers; this.openGame = openGame; this.confirmImportAll = confirmImportAll;
            InitializeComponent(); DataContext = model;
            ageTimer.Tick += async (_, __) => { model.UpdateStatus(); await model.ReconcileImportsAsync(CancellationToken.None); };
            Loaded += async (_, __) =>
            {
                model.PropertyChanged -= ModelChanged; model.PropertyChanged += ModelChanged;
                ageTimer.Start();
                if (started) return; started = true;
                await model.LoadAsync(CancellationToken.None);
            };
            Unloaded += (_, __) => { ageTimer.Stop(); model.CancelCurrentOperation(); model.PropertyChanged -= ModelChanged; };
        }
        private void ModelChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(CatalogBrowserModel.SelectedGames) || synchronizingSelection) return;
            synchronizingSelection = true;
            try { GamesList.SelectedItems.Clear(); foreach (var item in model.SelectedGames) GamesList.SelectedItems.Add(item); }
            finally { synchronizingSelection = false; }
        }
        private void GamesSelectionChanged(object sender, SelectionChangedEventArgs args)
        { if (!synchronizingSelection) model.SetSelectedGames(GamesList.SelectedItems.Cast<CatalogGameItem>()); }
        private async void RefreshClick(object sender, RoutedEventArgs e) => await model.RefreshAsync(CancellationToken.None);
        private async void ImportSelectedClick(object sender, RoutedEventArgs e) => await model.ImportSelectedAsync(CancellationToken.None);
        private async void ImportAllClick(object sender, RoutedEventArgs e)
        { if (model.CanImportAll && confirmImportAll?.Invoke(model.TotalCount) == true) await model.ImportAllAsync(CancellationToken.None); }
        private void CancelClick(object sender, RoutedEventArgs e) => model.CancelCurrentOperation();
        private void OpenClick(object sender, RoutedEventArgs e) { if (model.SelectedGame != null) openGame(model.SelectedGame); }
        private void GameDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ignore double-clicks in empty list space and on the scrollbar.
            if (ItemsControl.ContainerFromElement(GamesList, e.OriginalSource as DependencyObject) is ListBoxItem item && item.Content is CatalogGameItem game)
                openGame(game);
        }
    }
}
