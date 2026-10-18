using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
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
        private CatalogBrowserSession? session;
        private CatalogBrowserView? sidebarView;
        private SidebarItem? sidebarItem;
        private BrowserPreferences preferences = new BrowserPreferences();
        private bool savePreferences;
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

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            if (disposed || PlayniteApi.ApplicationInfo.Mode != ApplicationMode.Desktop) yield break;
            if (sidebarItem == null)
            {
                Geometry logo;
                var resource = Application.GetResourceStream(new Uri("pack://application:,,,/LunaPlaynite;component/Resources/luna-symbol.path", UriKind.Absolute));
                using (var reader = new StreamReader(resource.Stream)) logo = Geometry.Parse(reader.ReadToEnd());
                logo.Freeze();
                var icon = new System.Windows.Shapes.Path
                {
                    Data = logo, Width = 22, Height = 22, Stretch = Stretch.Uniform
                };
                // Inherit the host sidebar's normal/selected/theme foreground, like its native glyphs.
                icon.SetBinding(Shape.FillProperty, new Binding
                {
                    Path = new PropertyPath(TextElement.ForegroundProperty),
                    RelativeSource = new RelativeSource(RelativeSourceMode.Self)
                });
                sidebarItem = new SidebarItem
                {
                    Type = SiderbarItemType.View, Title = "Browse Amazon Luna Catalog", Icon = icon,
                    Opened = OpenSidebar, Closed = () => sidebarView?.Deactivate()
                };
            }
            yield return sidebarItem;
        }

        private Control OpenSidebar()
        {
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(LunaPlugin));
                if (sidebarView != null) { MigrateLegacyImports(); return sidebarView; }
                return sidebarView = CreateBrowserView();
            }
            catch (Exception)
            {
                logger.Error("Luna catalog sidebar could not be opened.");
                var error = new TextBlock
                {
                    Text = "The Luna catalog could not be opened. Check the extension installation and data folder, then select the tab again to retry.",
                    Margin = new Thickness(20), TextWrapping = TextWrapping.Wrap
                };
                error.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                return new UserControl { Content = error };
            }
        }

        private CatalogBrowserSession EnsureBrowserSession()
        {
            if (session != null) return session;
            var dispatcher = Dispatcher.CurrentDispatcher;
            if (importChanges == null)
            {
                // Plugin construction can happen off the UI thread; initialize on first view opening.
                importChanges = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                importChanges.Tick += async (_, __) =>
                {
                    if (session == null || !session.HasActiveHosts) { importChanges.Stop(); return; }
                    if (session.Model.IsBusy || !importsChanged) return;
                    importsChanged = false;
                    await session.Model.ReconcileImportsAsync(CancellationToken.None);
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
                importDatabase = new PlayniteImportDatabase(PlayniteApi.Database, dispatcher, GetPluginUserDataPath());
                importDatabase.LibraryChanged += () =>
                {
                    if (disposed || dispatcher.HasShutdownStarted) return;
                    dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (disposed || session == null || !session.HasActiveHosts) return;
                        importsChanged = true; importChanges.Start();
                    }));
                };
                imports = new CatalogImportService(importDatabase, new JsonImportMappingStore(GetPluginUserDataPath()),
                    artwork.DownloadAsync, message => logger.Error(message));
                service = catalogService;
            }
            try { preferences = LoadPluginSettings<BrowserPreferences>() ?? new BrowserPreferences(); }
            catch (Exception) { logger.Warn("Luna browser preferences could not be read; defaults will be used."); preferences = new BrowserPreferences(); }
            savePreferences = preferences.SchemaVersion == 1;
            if (!savePreferences) preferences = new BrowserPreferences();
            var model = new CatalogBrowserModel(service, message => logger.Error(message), imports);
            if (Enum.IsDefined(typeof(CatalogCategory), preferences.Category)) model.Category = preferences.Category;
            if (Enum.IsDefined(typeof(CatalogTitleSort), preferences.TitleSort)) model.TitleSort = preferences.TitleSort;
            session = new CatalogBrowserSession(model);
            session.ActivityChanged += (_, __) =>
            {
                if (session.HasActiveHosts) return;
                importChanges.Stop(); SavePreferences();
            };
            return session;
        }

        private CatalogBrowserView CreateBrowserView()
        {
            var activeSession = EnsureBrowserSession();
            MigrateLegacyImports();
            return new CatalogBrowserView(activeSession, covers!, OpenInLuna, count =>
                PlayniteApi.Dialogs.ShowMessage("Import all " + count + " games from the complete US Luna catalog?\n\n" +
                    "Search and category filters are ignored. Existing Luna imports will be skipped. Games will be added as uninstalled entries with an Open in Luna action.",
                    "Import all Luna games", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        }

        private void SavePreferences()
        {
            if (!savePreferences || session == null) return;
            preferences.Category = session.Model.Category; preferences.TitleSort = session.Model.TitleSort;
            try { SavePluginSettings(preferences); }
            catch (Exception) { logger.Warn("Luna browser preferences could not be saved."); }
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
                var view = CreateBrowserView();
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
                    view.Deactivate();
                    var size = window.WindowState == WindowState.Normal ? new Size(window.ActualWidth, window.ActualHeight) : window.RestoreBounds.Size;
                    preferences.Width = size.Width; preferences.Height = size.Height;
                    SavePreferences(); browser = null;
                };
                browser = window;
                window.Activated += async (_, __) => await session!.Model.ReconcileImportsAsync(CancellationToken.None);
                window.Show();
            }
            catch (Exception)
            {
                browser = null;
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
            session?.Dispose();
            sidebarView?.Deactivate();
            if (sidebarItem != null) sidebarItem.Visible = false;
            browser?.Close();
            importChanges?.Stop(); importDatabase?.Dispose();
            catalogClient.Dispose(); artworkClient.Dispose();
            base.Dispose();
        }
    }
}
