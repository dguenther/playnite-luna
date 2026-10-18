using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LunaCatalog;
using LunaCatalogBrowser;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LunaPlaynite
{
    public sealed class LunaPlugin : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly HttpClient catalogClient = AnonymousClient();
        private readonly HttpClient artworkClient = AnonymousClient();
        private CatalogService? service;
        private CoverImageCache? covers;
        private Window? browser;
        private CatalogBrowserModel? model;
        private CatalogImportService? imports;
        private PlayniteImportDatabase? importDatabase;
        private DispatcherTimer? importChanges;
        private bool importsChanged;
        private bool disposed;
        public override Guid Id => LunaImportIdentity.PluginId;
        public override string Name => "Amazon Luna";

        public LunaPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = false, HasCustomizedGameImport = true };
        }

        // Library registration supplies the Amazon Luna group; importing remains an explicit browser action.
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args) => Array.Empty<GameMetadata>();
        public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
        {
            if (!disposed && !args.CancelToken.IsCancellationRequested) MigrateLegacyImports();
            return Array.Empty<Game>();
        }
        public override void OnApplicationStarted(OnApplicationStartedEventArgs args) => MigrateLegacyImports();
        private void MigrateLegacyImports()
        {
            if (disposed) return;
            try
            {
                if (importDatabase != null) { importDatabase.MigrateLegacyImports(); return; }
                using (var database = new PlayniteImportDatabase(PlayniteApi.Database, PlayniteApi.MainView.UIDispatcher, GetPluginUserDataPath()))
                    database.MigrateLegacyImports();
            }
            catch (Exception) { logger.Warn("Legacy Luna library assignment could not be saved. Reopen the catalog to retry."); }
        }

        private static HttpClient AnonymousClient() => new HttpClient(new HttpClientHandler
            { UseCookies = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            if (PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop) yield break;
            yield return new MainMenuItem { Description = "Amazon Luna Catalog Browser", Action = _ => OpenBrowser() };
        }

        private void OpenBrowser()
        {
            if (disposed) return;
            if (browser != null)
            {
                if (browser.WindowState == WindowState.Minimized) browser.WindowState = WindowState.Normal;
                browser.Activate();
                return;
            }
            try
            {
                if (importChanges == null)
                {
                    // Plugin construction can happen off the UI thread; attach the timer when the menu opens.
                    importChanges = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    importChanges.Tick += async (_, __) =>
                    {
                        if (model == null) { importChanges.Stop(); return; }
                        if (model.IsBusy || !importsChanged) return;
                        importsChanged = false;
                        await model.ReconcileImportsAsync(CancellationToken.None);
                        if (!importsChanged) importChanges.Stop();
                    };
                }
                if (service == null)
                {
                    var provider = new LunaCatalogProvider(catalogClient,
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36", "154.0.0.0");
                    var catalogService = new CatalogService(provider, new JsonCatalogCache(GetPluginUserDataPath()));
                    var artwork = new CatalogArtworkClient(artworkClient);
                    covers = new CoverImageCache(artwork);
                    importDatabase = new PlayniteImportDatabase(PlayniteApi.Database, Dispatcher.CurrentDispatcher, GetPluginUserDataPath());
                    importDatabase.LibraryChanged += () =>
                    {
                        browser?.Dispatcher.BeginInvoke(new Action(() => { importsChanged = true; importChanges.Start(); }));
                    };
                    imports = new CatalogImportService(importDatabase, new JsonImportMappingStore(GetPluginUserDataPath()),
                        artwork.DownloadAsync, message => logger.Error(message));
                    service = catalogService;
                }
                MigrateLegacyImports();
                BrowserPreferences preferences;
                try { preferences = LoadPluginSettings<BrowserPreferences>() ?? new BrowserPreferences(); }
                catch (Exception) { logger.Warn("Luna browser preferences could not be read; defaults will be used."); preferences = new BrowserPreferences(); }
                var savePreferences = preferences.SchemaVersion == 1;
                if (!savePreferences) preferences = new BrowserPreferences();
                var viewModel = new CatalogBrowserModel(service, message => logger.Error(message), imports);
                if (Enum.IsDefined(typeof(CatalogCategory), preferences.Category)) viewModel.Category = preferences.Category;
                if (Enum.IsDefined(typeof(CatalogTitleSort), preferences.TitleSort)) viewModel.TitleSort = preferences.TitleSort;
                var view = new CatalogBrowserView(viewModel, covers!, OpenInLuna, count =>
                    PlayniteApi.Dialogs.ShowMessage("Import all " + count + " games from the complete US Luna catalog?\n\n" +
                        "Search and category filters are ignored. Existing Luna imports will be skipped. Games will be added as uninstalled entries with an Open in Luna action.",
                        "Import all Luna games", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions());
                window.Title = "Amazon Luna Catalog Browser";
                window.Content = view;
                window.Width = ValidSize(preferences.Width, 1060, 800, 2400);
                window.Height = ValidSize(preferences.Height, 720, 500, 1600);
                window.MinWidth = 800; window.MinHeight = 500;
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Closed += (_, __) =>
                {
                    viewModel.CancelCurrentOperation();
                    importChanges.Stop();
                    if (savePreferences)
                    {
                        var size = window.WindowState == WindowState.Normal ? new Size(window.ActualWidth, window.ActualHeight) : window.RestoreBounds.Size;
                        try { SavePluginSettings(new BrowserPreferences { Category = viewModel.Category, TitleSort = viewModel.TitleSort,
                            Width = size.Width, Height = size.Height }); }
                        catch (Exception) { logger.Warn("Luna browser preferences could not be saved."); }
                    }
                    browser = null; model = null;
                };
                browser = window; model = viewModel;
                window.Activated += async (_, __) => await viewModel.ReconcileImportsAsync(CancellationToken.None);
                window.Show();
            }
            catch (Exception)
            {
                browser = null; model = null;
                logger.Error("Luna catalog browser could not be opened.");
                PlayniteApi.Dialogs.ShowErrorMessage("The Luna catalog browser could not be opened. Check that the extension is installed completely and its data folder is writable.", "Amazon Luna");
            }
        }

        private static double ValidSize(double value, double fallback, double minimum, double maximum) =>
            double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));

        private void OpenInLuna(CatalogGameItem game)
        {
            if (!Uri.TryCreate(game.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "luna.amazon.com" ||
                uri.Port != 443 || uri.UserInfo.Length != 0 || !uri.AbsolutePath.StartsWith("/game/", StringComparison.Ordinal)) return;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            { PlayniteApi.Dialogs.ShowErrorMessage("The Luna page could not be opened. Check your default browser.", "Amazon Luna"); }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args) => Dispose();
        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;
            model?.CancelCurrentOperation();
            browser?.Close();
            importChanges?.Stop(); importDatabase?.Dispose();
            catalogClient.Dispose(); artworkClient.Dispose();
            base.Dispose();
        }
    }
}
