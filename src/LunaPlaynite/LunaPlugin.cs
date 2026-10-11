using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Windows;
using LunaCatalog;
using LunaCatalogBrowser;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;

namespace LunaPlaynite
{
    public sealed class LunaPlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly HttpClient catalogClient = AnonymousClient();
        private readonly HttpClient artworkClient = AnonymousClient();
        private CatalogService? service;
        private CoverImageCache? covers;
        private Window? browser;
        private CatalogBrowserModel? model;
        private bool disposed;
        public override Guid Id { get; } = Guid.Parse("6f4229ca-70a9-4c65-ae9f-047a367beda9");

        public LunaPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
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
                if (service == null)
                {
                    var provider = new LunaCatalogProvider(catalogClient,
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/154.0.0.0 Safari/537.36", "154.0.0.0");
                    service = new CatalogService(provider, new JsonCatalogCache(GetPluginUserDataPath()));
                    covers = new CoverImageCache(new CatalogArtworkClient(artworkClient));
                }
                BrowserPreferences preferences;
                try { preferences = LoadPluginSettings<BrowserPreferences>() ?? new BrowserPreferences(); }
                catch (Exception) { logger.Warn("Luna browser preferences could not be read; defaults will be used."); preferences = new BrowserPreferences(); }
                var savePreferences = preferences.SchemaVersion == 1;
                if (!savePreferences) preferences = new BrowserPreferences();
                var viewModel = new CatalogBrowserModel(service, message => logger.Error(message));
                if (Enum.IsDefined(typeof(CatalogCategory), preferences.Category)) viewModel.Category = preferences.Category;
                if (Enum.IsDefined(typeof(CatalogTitleSort), preferences.TitleSort)) viewModel.TitleSort = preferences.TitleSort;
                var view = new CatalogBrowserView(viewModel, covers!, OpenInLuna);
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
            catalogClient.Dispose(); artworkClient.Dispose();
            base.Dispose();
        }
    }
}
