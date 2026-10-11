using System;
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
        private readonly DispatcherTimer ageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        internal CoverImageCache Covers { get; }
        public CatalogBrowserView(CatalogBrowserModel model, CoverImageCache covers, Action<CatalogGameItem> openGame)
        {
            this.model = model; Covers = covers; this.openGame = openGame;
            InitializeComponent(); DataContext = model;
            ageTimer.Tick += (_, __) => model.UpdateStatus();
            Loaded += async (_, __) =>
            {
                ageTimer.Start();
                if (started) return; started = true;
                await model.LoadAsync(CancellationToken.None);
            };
            Unloaded += (_, __) => { ageTimer.Stop(); model.CancelCurrentOperation(); };
        }
        private async void RefreshClick(object sender, RoutedEventArgs e) => await model.RefreshAsync(CancellationToken.None);
        private void CancelClick(object sender, RoutedEventArgs e) => model.CancelCurrentOperation();
        private void OpenClick(object sender, RoutedEventArgs e) { if (model.SelectedGame != null) openGame(model.SelectedGame); }
        private void GameDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Ignore double-clicks in empty list space and on the scrollbar.
            if (ItemsControl.ContainerFromElement(GamesList, e.OriginalSource as DependencyObject) is ListBoxItem && model.SelectedGame != null)
                openGame(model.SelectedGame);
        }
    }
}
