using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LunaCatalog;
using LunaCatalogBrowser;
using LunaPlaynite;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LunaPlayniteSmoke
{
    // A real WPF/dispatcher check on Windows, independent of live Amazon requests.
    // It does not stand in for loading the plugin inside the Playnite host.
    internal static class Program
    {
        private static readonly DateTimeOffset time = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
        [STAThread]
        private static int Main()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var pending = RunAsync();
            pending.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background), TaskScheduler.Default);
            Dispatcher.Run();
            try { pending.GetAwaiter().GetResult(); Console.WriteLine("Windows WPF browser smoke checks passed."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private sealed class Provider : ILunaCatalogProvider
        {
            internal int Calls;
            internal bool Block;
            internal bool ReviseTitle;
            internal readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public async Task<CatalogSnapshot> FetchAsync(string region, CancellationToken token)
            {
                Calls++; Started.TrySetResult(true);
                if (Block) await Task.Delay(Timeout.Infinite, token);
                var snapshot = Snapshot();
                if (ReviseTitle)
                {
                    foreach (var game in snapshot.Queries.SelectMany(q => q.Games).Where(g => g.Title == "Alan Wake 2")) game.Title = "Alan Wake revised";
                    snapshot = CatalogSnapshot.Merge(snapshot.Queries); snapshot.RetrievedAt = time;
                }
                return snapshot;
            }
        }
        private static CatalogSnapshot Snapshot()
        {
            var snapshot = CatalogSnapshot.Merge(CatalogQuery.UsQueries.Select(q => LunaResponseParser.Parse(
                File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", q.Name + ".json")), q)));
            snapshot.RetrievedAt = time;
            return snapshot;
        }
        private sealed class ImageHandler : HttpMessageHandler
        {
            internal byte[] Image = new byte[0];
            internal readonly TaskCompletionSource<HttpResponseMessage> LateFailure = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> OldStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (request.RequestUri!.AbsolutePath == "/late-failure")
                {
                    OldStarted.TrySetResult(true);
                    return LateFailure.Task; // Deliberately ignore cancellation, then fail after the replacement has loaded.
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image) });
            }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF operation did not finish."); await Task.Delay(10); }
        }
        private static T Control<T>(CatalogBrowserView view, string name) where T : FrameworkElement => (T)view.FindName(name);
        private static void Click(CatalogBrowserView view, string name) => Control<Button>(view, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        private static async Task CheckRecycledCoverAsync(CatalogBrowserView view, ImageHandler handler)
        {
            var cover = new CatalogCover();
            var root = (Grid)view.Content;
            root.Children.Add(cover);
            try
            {
                await Until(() => cover.IsLoaded);
                cover.ArtworkUrl = "https://m.media-amazon.com/late-failure";
                await Until(() => handler.OldStarted.Task.IsCompleted);
                // Observe completion of the obsolete async-void handler via its disposed request.
                var old = (CancellationTokenSource)typeof(CatalogCover).GetField("request", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cover);
                cover.ArtworkUrl = "https://m.media-amazon.com/replacement";
                await Until(() => cover.Source != null);
                var replacement = cover.Source;
                handler.LateFailure.SetException(new InvalidOperationException("Late obsolete transport failure."));
                await Until(() =>
                {
                    try { var token = old.Token; return false; }
                    catch (ObjectDisposedException) { return true; }
                });
                Check(ReferenceEquals(cover.Source, replacement), "A late obsolete cover failure cleared the recycled row's replacement image.");
            }
            finally { root.Children.Remove(cover); }
        }

        private static async Task RunAsync()
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SmokeDatabase.InitializeSdkSettings();
            application.Resources["TextBrush"] = Brushes.White;
            application.Resources["TextBrushDark"] = Brushes.Black;
            var itemTheme = new Style(typeof(ListBoxItem));
            itemTheme.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
            var selectedText = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedText.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, new DynamicResourceExtension("TextBrushDark")));
            itemTheme.Triggers.Add(selectedText); application.Resources[typeof(ListBoxItem)] = itemTheme;
            var directory = Path.Combine(Path.GetTempPath(), "luna-wpf-smoke-" + Guid.NewGuid().ToString("N"));
            Window? window = null;
            try
            {
                Check(typeof(LibraryPlugin).IsAssignableFrom(typeof(LunaPlugin)), "Luna does not register as a library plugin.");
                using (var plugin = new LunaPlugin(null!))
                {
                    Check(plugin.Name == "Amazon Luna" && plugin.Id == LunaImportIdentity.PluginId && plugin.Properties.HasCustomizedGameImport,
                        "The Luna library identity or explicit import mode is incorrect.");
                    Check(!plugin.GetGames(new LibraryGetGamesArgs()).Any(), "A library update would automatically import the catalog.");
                }
                CheckLegacyMigration(directory);
                CheckInstallWebsite();
                await CheckInstalledSettingAsync(directory);
                await CheckLibraryUpdateAsync(directory);
                await CheckAutomationAdapterAsync(directory);
                CheckPlayWebsite();
                await CheckSidebarAsync(directory);
                var cache = new JsonCatalogCache(directory, () => time);
                await cache.SaveAsync(CatalogValidator.ValidateSnapshot(Snapshot(), "US", time), CancellationToken.None);
                var originalBytes = File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"));
                var provider = new Provider();
                var uiThread = Thread.CurrentThread.ManagedThreadId;
                var wrongThread = false;
                var bitmap = new WriteableBitmap(2, 3, 96, 96, PixelFormats.Bgra32, null);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                byte[] image;
                using (var bytes = new MemoryStream()) { encoder.Save(bytes); image = bytes.ToArray(); }
                var imageHandler = new ImageHandler { Image = image };
                using (var client = new HttpClient(imageHandler))
                {
                    var artwork = new CatalogArtworkClient(client);
                    var covers = new CoverImageCache(artwork);
                    var library = new SmokeDatabase(Path.Combine(directory, "library"));
                    using var adapter = new PlayniteImportDatabase(library.Api, Dispatcher.CurrentDispatcher, directory);
                    var imports = new CatalogImportService(adapter, new JsonImportMappingStore(directory), artwork.DownloadAsync, utcNow: () => time);
                    var model = new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => time), imports: imports);
                    model.PropertyChanged += (_, __) => wrongThread |= Thread.CurrentThread.ManagedThreadId != uiThread;
                    string? openedId = null;
                    var confirmAll = false; var confirmedCount = 0;
                    var view = new CatalogBrowserView(model, covers, game => openedId = game.Id, count => { confirmedCount = count; return confirmAll; });
                    window = new Window { Content = view, Width = 1060, Height = 720 };
                    window.Show();
                    await Until(() => !model.IsBusy && model.TotalCount == 5);
                    Check(provider.Calls == 0, "Opening a cached browser contacted the provider.");
                    var list = Control<ListBox>(view, "GamesList");
                    list.UpdateLayout();
                    var firstRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                    Check(firstRow.Style.BasedOn == itemTheme && firstRow.Foreground == Brushes.White, "The catalog row lost the host's theme text style.");
                    firstRow.IsSelected = true;
                    Check(firstRow.Foreground == Brushes.Black, "Selected text lost the host's selected-row color.");
                    firstRow.IsSelected = false;
                    application.Resources["TextBrush"] = Brushes.LightGreen;
                    Check(firstRow.Foreground == Brushes.LightGreen, "Row text did not follow a host theme resource change.");
                    application.Resources["TextBrush"] = Brushes.White;
                    var search = Control<TextBox>(view, "SearchBox");
                    var category = Control<ComboBox>(view, "CategoryBox");
                    search.Text = "ALAN";
                    Check(list.Items.Count == 1, "Live title binding did not filter the displayed list.");
                    Check(((CatalogGameItem)list.Items[0]).Memberships.Count == 2, "Overlapping memberships were lost.");
                    category.SelectedItem = CatalogCategory.GameNight;
                    Check(list.Items.Count == 0, "Category binding did not combine with title search.");
                    search.Text = ""; category.SelectedItem = CatalogCategory.All;
                    Check(list.Items.Count == 5, "Clearing filters did not restore the catalog.");
                    Control<ComboBox>(view, "SortBox").SelectedItem = CatalogTitleSort.Descending;
                    Check(((CatalogGameItem)list.Items[0]).Title == "WRC Generations Fully Loaded Edition", "Sort binding did not change the displayed order.");
                    Control<ComboBox>(view, "SortBox").SelectedItem = CatalogTitleSort.Ascending;
                    list.SelectedItem = list.Items[0];
                    Click(view, "OpenButton");
                    Check(openedId == model.SelectedGame?.Id && openedId != null, "Open in Luna did not use the selected identity.");
                    var selectedId = openedId;
                    var cover = await covers.LoadAsync(model.SelectedGame!.CoverUrl, CancellationToken.None);
                    Check(cover != null && cover.IsFrozen, "Artwork did not decode into a thread-safe WPF image.");
                    await CheckRecycledCoverAsync(view, imageHandler);

                    list.SelectedItems.Add(list.Items[1]);
                    Check(model.SelectedGames.Count == 2, "Extended selection did not reach the model.");
                    Click(view, "ImportSelectedButton");
                    await Until(() => !model.IsBusy && library.Games.Values.Count == 2);
                    Check(model.ImportSummaryText.Contains("2 added"), "Selected import summary is incorrect.");
                    foreach (var imported in library.Games.Values)
                    {
                        Check(imported.PluginId == LunaImportIdentity.PluginId && imported.IsInstalled && imported.PlatformIds.Count == 0 && imported.IncludeLibraryPluginAction,
                            "A Luna import has the wrong library, installed default, plugin play action, or platform.");
                        Check(library.Sources.Values.Single(s => s.Id == imported.SourceId).Name == "Amazon Luna", "Imported source is incorrect.");
                        var action = imported.GameActions.Single();
                        Check(action.Type == GameActionType.URL && action.Name == "Open in Luna" && !action.IsPlayAction && action.Path == imported.Links.Single().Url,
                            "Imported URL action or direct link is incorrect.");
                        Check(LunaImportIdentity.TryDecode(imported.GameId, out var identity) && identity.Region == "US", "The recovery marker is missing.");
                        Check(imported.TagIds.All(id => library.Tags.Values.Single(t => t.Id == id).Name.StartsWith("Luna: ")), "Membership tags are missing.");
                        Check(File.Exists(imported.CoverImage), "Optional artwork was not added through host storage.");
                    }
                    var edited = library.Games.Values[0];
                    edited.Name = "User title"; edited.Favorite = true; edited.Description = "User notes"; edited.Playtime = 123;
                    edited.CompletionStatusId = Guid.NewGuid(); edited.CoverImage = "user-artwork";
                    var customTag = library.Tags.Api.Add("User tag").Id; edited.TagIds.Add(customTag);
                    Click(view, "ImportSelectedButton"); await Until(() => !model.IsBusy);
                    Check(model.ImportSummaryText.Contains("2 already imported") && library.ArtworkWrites == 2, "Repeated imports downloaded or overwrote existing artwork.");
                    Check(edited.Name == "User title" && edited.Favorite && edited.Description == "User notes" && edited.Playtime == 123 && edited.CoverImage == "user-artwork" && edited.TagIds.Contains(customTag),
                        "Repeated imports overwrote user metadata.");
                    search.Text = "Alan";
                    Click(view, "ImportAllButton");
                    Check(confirmedCount == 5 && library.Games.Values.Count == 2 && !model.IsBusy, "Import All did not confirm the complete-catalog scope or respect rejection.");
                    confirmAll = true; Click(view, "ImportAllButton");
                    await Until(() => !model.IsBusy && library.Games.Values.Count == 5);
                    Check(model.ImportSummaryText.Contains("3 added") && list.Items.Count == 1, "Confirmed Import All used the filtered scope or changed the filter.");
                    var alanGuid = new JsonImportMappingStore(directory).Load().Document!.Mappings.Single(m => m.LunaId == selectedId).PlayniteId;
                    library.Games.Values.RemoveAll(g => g.Id == alanGuid);
                    await model.ReconcileImportsAsync(CancellationToken.None);
                    Check(model.SelectedGame!.ImportStatus == CatalogImportStatus.NotImported, "Manual deletion did not reconcile imported status.");
                    Click(view, "ImportSelectedButton"); await Until(() => !model.IsBusy && library.Games.Values.Count == 5);

                    provider.Block = true;
                    Click(view, "RefreshButton");
                    await Until(() => provider.Calls == 1);
                    Check(model.IsBusy && !Control<Button>(view, "RefreshButton").IsEnabled, "Refresh did not disable duplicate clicks.");
                    search.Text = "Alan";
                    Check(list.Items.Count == 1, "Browsing stopped while refresh was pending.");
                    Click(view, "CancelButton");
                    await Until(() => !model.IsBusy);
                    Check(model.LastSuccessfulRefresh == time && list.Items.Count == 1, "Cancellation discarded cached data.");
                    Check(originalBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"))), "Cancellation modified the saved cache.");
                    provider.Block = false; provider.ReviseTitle = true;
                    Click(view, "RefreshButton");
                    await Until(() => !model.IsBusy);
                    Check(model.SelectedGame != null && model.SelectedGame.Id == selectedId && model.SelectedGame.Title == "Alan Wake revised", "A committed refresh lost bound selection or displayed stale metadata.");
                    Check(!wrongThread, "Model notifications left the WPF dispatcher thread.");
                    window.Close(); window = null;
                    var restarted = new CatalogBrowserModel(new CatalogService(provider, new JsonCatalogCache(directory, () => time), utcNow: () => time),
                        imports: new CatalogImportService(adapter, new JsonImportMappingStore(directory), utcNow: () => time));
                    await restarted.LoadAsync(CancellationToken.None);
                    Check(provider.Calls == 2 && restarted.VisibleGames.Any(g => g.Title == "Alan Wake revised"), "Restart did not reload the accepted cache without a fetch.");
                    Check(restarted.VisibleGames.All(g => g.ImportStatus == CatalogImportStatus.Imported), "Restart lost library import identity.");
                }
            }
            finally { window?.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        private static void CheckLegacyMigration(string directory)
        {
            var library = new SmokeDatabase(Path.Combine(directory, "legacy-library"));
            var game = Snapshot().Games[0];
            var marker = LunaImportIdentity.Encode(game.Region, game.Id, game.Asin!);
            var legacy = new Game("My edited Luna title")
            {
                PluginId = Guid.Empty, GameId = marker, Favorite = true, Playtime = 123, Description = "My notes",
                SourceId = Guid.NewGuid(), CompletionStatusId = Guid.NewGuid(), CoverImage = "custom-artwork",
                PlatformIds = new System.Collections.Generic.List<Guid> { Guid.NewGuid() },
                TagIds = new System.Collections.Generic.List<Guid> { Guid.NewGuid() },
                Links = new ObservableCollection<Link> { new Link("User link", "https://example.com/") },
                GameActions = new ObservableCollection<GameAction> { new GameAction { Name = "User action", Type = GameActionType.URL, Path = "https://example.com/" } }
            };
            var manual = new Game(game.Title) { PluginId = Guid.Empty, GameId = game.Url };
            var foreign = new Game(game.Title) { PluginId = Guid.NewGuid(), GameId = marker };
            var current = new Game(game.Title) { PluginId = LunaImportIdentity.PluginId, GameId = marker };
            library.Games.Values.AddRange(new[] { legacy, manual, foreign, current });
            using var adapter = new PlayniteImportDatabase(library.Api, Dispatcher.CurrentDispatcher, directory);
            Check(adapter.MigrateLegacyImports() == 1, "Library migration did not select only the marked manual import.");
            var migrated = library.Games.Api.Get(legacy.Id);
            Check(migrated.PluginId == LunaImportIdentity.PluginId && migrated.Id == legacy.Id && migrated.GameId == legacy.GameId,
                "Library migration changed a durable game identity.");
            Check(migrated.Name == legacy.Name && migrated.Favorite && migrated.Playtime == legacy.Playtime && migrated.Description == legacy.Description &&
                migrated.SourceId == legacy.SourceId && migrated.CompletionStatusId == legacy.CompletionStatusId && migrated.CoverImage == legacy.CoverImage &&
                migrated.PlatformIds.SequenceEqual(legacy.PlatformIds) && migrated.TagIds.SequenceEqual(legacy.TagIds) &&
                migrated.Links.Single().Url == legacy.Links.Single().Url && migrated.GameActions.Single().Path == legacy.GameActions.Single().Path,
                "Library migration overwrote user metadata.");
            Check(manual.PluginId == Guid.Empty && foreign.PluginId != LunaImportIdentity.PluginId && adapter.MigrateLegacyImports() == 0 && library.Games.Values.Count == 4,
                "Repeated migration changed unmarked/foreign games or duplicated imports.");
        }

        private static void CheckInstallWebsite()
        {
            const string asin = "B0GFWDM8NP";
            const string url = "https://luna.amazon.com/game/alan-wake-2/" + asin;
            var game = new Game("Edited Luna title")
            {
                PluginId = LunaImportIdentity.PluginId, GameId = LunaImportIdentity.Encode("US", asin, asin),
                Favorite = true, Notes = "User notes", IsInstalled = false,
                GameActions = new ObservableCollection<GameAction>
                {
                    new GameAction { Name = "Open in Luna", Type = GameActionType.URL, Path = url, IsPlayAction = false }
                }
            };
            using var plugin = new LunaPlugin(null!);
            Check(plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Single().Name == "Open in Luna",
                "An existing owned Luna import did not receive a website install action.");
            var opened = new System.Collections.Generic.List<Uri>();
            var cancelled = 0; var installed = 0;
            void Exercise(Action<Uri> open, bool fail)
            {
                using var controller = LunaWebsiteInstallController.ForGame(game, open)!;
                // The isolated host subscribes to SDK events exactly as Playnite's
                // controller factory does; production uses only the public SDK.
                typeof(InstallController).GetEvent("InstallCancelled", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetAddMethod(true)!.Invoke(controller, new object[]
                    {
                        new EventHandler<GameInstallationCancelledEventArgs>((_, __) => { cancelled++; game.IsInstalling = false; })
                    });
                typeof(InstallController).GetEvent("Installed", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetAddMethod(true)!.Invoke(controller, new object[]
                    {
                        new EventHandler<GameInstalledEventArgs>((_, __) => { installed++; game.IsInstalled = true; })
                    });
                game.IsInstalling = true;
                try
                {
                    controller.Install(new InstallActionArgs());
                    Check(!fail, "A browser launch failure was swallowed instead of reaching the host error handler.");
                }
                catch (System.ComponentModel.Win32Exception) when (fail) { }
                Check(!game.IsInstalling && !game.IsInstalled && installed == 0,
                    "The website handoff left an installing state or marked a cloud game installed.");
            }
            Exercise(opened.Add, false); Exercise(opened.Add, false);
            Exercise(_ => throw new System.ComponentModel.Win32Exception("No browser available."), true);
            Check(opened.Count == 2 && opened.All(uri => uri.AbsoluteUri == url) && cancelled == 3 &&
                game.Name == "Edited Luna title" && game.Favorite && game.Notes == "User notes" && !game.GameActions[0].IsPlayAction,
                "Install did not open the stored game URL repeatedly, finish each handoff, or preserve user metadata.");

            var marker = game.GameId;
            game.PluginId = Guid.NewGuid();
            Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(), "Install intercepted another library's game.");
            game.PluginId = Guid.Empty;
            Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(), "Install intercepted an unowned manual game.");
            game.PluginId = LunaImportIdentity.PluginId; game.GameId = "unmarked";
            Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(), "Install accepted an invalid Luna identity.");
            game.GameId = marker;
            foreach (var unsafeUrl in new[]
            {
                "http://luna.amazon.com/game/alan-wake-2/" + asin,
                "https://luna.amazon.com.evil.example/game/alan-wake-2/" + asin,
                "https://user@luna.amazon.com/game/alan-wake-2/" + asin,
                "https://luna.amazon.com:444/game/alan-wake-2/" + asin,
                "https://luna.amazon.com/game/other/B09HFKWMTQ",
                "https://luna.amazon.com/home", "file:///C:/Windows/test.exe"
            })
            {
                game.GameActions[0].Path = unsafeUrl;
                Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(),
                    "Install accepted an unsafe URL or a mismatched game identity: " + unsafeUrl);
            }
            game.GameActions.Clear();
            game.Links = new ObservableCollection<Link> { new Link("Amazon Luna", url) };
            Check(plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Count() == 1,
                "Install could not use the stored Luna link when its additional action was removed.");
            game.GameId = LunaImportIdentity.Encode("US", "B0BTRVRN6M", "B0BTRVRN6M");
            game.Links[0].Url = "https://luna.amazon.com/game/fortnite";
            Check(plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Count() == 1,
                "Install rejected the supported slug-only Luna page format.");
            game.Links.Clear();
            Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(), "Install offered a handoff without a stored Luna URL.");
            game.GameId = marker; game.Links.Add(new Link("Amazon Luna", url)); plugin.Dispose();
            Check(!plugin.GetInstallActions(new GetInstallActionsArgs { Game = game }).Any(), "A disposed extension offered an install action.");
            Console.WriteLine("Windows Luna Install website handoff checks passed (ownership, URLs, repeated clicks, failure cleanup, uninstalled state).");
        }

        private static async Task CheckInstalledSettingAsync(string directory)
        {
            var root = Path.Combine(directory, "installed-setting");
            var library = new SmokeDatabase(Path.Combine(root, "library"));
            var host = new SmokeDatabase.Host(library, root);
            var catalog = CatalogValidator.ValidateSnapshot(Snapshot(), "US", time);
            Game Owned(string name, Guid owner) => new Game(name)
            {
                PluginId = owner, GameId = LunaImportIdentity.Encode("US", catalog.Games[0].Id, catalog.Games[0].Asin!),
                Favorite = true, Notes = "User notes", Playtime = 321,
                Links = new ObservableCollection<Link> { new Link("Amazon Luna", catalog.Games[0].Url) },
                GameActions = new ObservableCollection<GameAction>
                {
                    new GameAction { Name = "Open in Luna", Type = GameActionType.URL, Path = catalog.Games[0].Url }
                }
            };
            var owned = Owned("Edited Luna title", LunaImportIdentity.PluginId);
            var legacy = Owned("Legacy Luna", Guid.Empty);
            var foreign = Owned("Foreign copy", Guid.NewGuid());
            var manual = new Game("Unmarked manual game");
            var unmarked = new Game("Unmarked library game") { PluginId = LunaImportIdentity.PluginId };
            library.Games.Values.AddRange(new[] { owned, legacy, foreign, manual, unmarked });
            using var plugin = new LunaPlugin(host.Api);
            // An older settings file has no installed-state property. Preserve its browser preferences.
            plugin.SavePluginSettings(new { SchemaVersion = 1, Category = CatalogCategory.GameNight, TitleSort = CatalogTitleSort.Descending, Width = 1100, Height = 800 });
            var settings = (LunaSettings)plugin.GetSettings(false);
            Check(settings.MarkGamesInstalled && settings.RefreshOnLibraryUpdate && settings.TagExpiredGames && !settings.ImportNewGames && !settings.RemoveUnplayedGames &&
                plugin.Properties.HasSettings && settings.VerifySettings(out var errors) && errors.Count == 0,
                "The installed setting is not enabled by default for an upgraded preferences file.");
            plugin.OnApplicationStarted(new OnApplicationStartedEventArgs());
            Game Current(Game game) => library.Games.Api.Get(game.Id);
            Check(Current(owned).IsInstalled && Current(owned).IncludeLibraryPluginAction && Current(legacy).IsInstalled &&
                Current(legacy).PluginId == LunaImportIdentity.PluginId && !Current(foreign).IsInstalled && !Current(manual).IsInstalled && !Current(unmarked).IsInstalled,
                "Startup did not update existing/legacy Luna entries exclusively.");
            Check(plugin.GetPlayActions(new GetPlayActionsArgs { Game = Current(owned) }).Single().Name == "Open in Luna",
                "Enabled settings did not supply a native Play controller for an existing import.");
            var view = (LunaSettingsView)plugin.GetSettingsView(false);
            var checkbox = (CheckBox)view.FindName("InstalledBox");
            var window = new Window { Content = view, Width = 640, Height = 400 };
            try
            {
                window.Show(); await Until(() => checkbox.IsChecked == true);
                Check(((CheckBox)view.FindName("RefreshOnLibraryUpdateBox")).IsChecked == true &&
                    ((CheckBox)view.FindName("TagExpiredGamesBox")).IsChecked == true &&
                    ((CheckBox)view.FindName("ImportNewGamesBox")).IsChecked == false &&
                    ((CheckBox)view.FindName("RemoveUnplayedGamesBox")).IsChecked == false,
                    "Automation settings checkboxes do not reflect the requested defaults.");
                settings.BeginEdit(); checkbox.IsChecked = false;
                Check(!settings.MarkGamesInstalled && Current(owned).IsInstalled, "Editing settings changed the library before Save.");
                settings.CancelEdit(); await Until(() => checkbox.IsChecked == true);
                Check(Current(owned).IsInstalled, "Cancelling settings changed installed state.");
                settings.BeginEdit(); settings.RefreshOnLibraryUpdate = false; settings.TagExpiredGames = false;
                settings.ImportNewGames = true; settings.RemoveUnplayedGames = true; settings.CancelEdit();
                Check(settings.RefreshOnLibraryUpdate && settings.TagExpiredGames && !settings.ImportNewGames && !settings.RemoveUnplayedGames,
                    "Cancelling settings did not restore all automation choices.");
                settings.BeginEdit(); settings.RefreshOnLibraryUpdate = false; settings.TagExpiredGames = false;
                var countBeforeSave = library.Games.Values.Count;
                settings.ImportNewGames = true; settings.RemoveUnplayedGames = true; settings.EndEdit();
                using (var reloaded = new LunaPlugin(host.Api))
                {
                    var choices = (LunaSettings)reloaded.GetSettings(false);
                    Check(choices.MarkGamesInstalled && !choices.RefreshOnLibraryUpdate && !choices.TagExpiredGames && choices.ImportNewGames && choices.RemoveUnplayedGames,
                        "Saved automation choices were lost on restart.");
                    Check(library.Games.Values.Count == countBeforeSave, "Saving settings mutated the library.");
                }
                settings.BeginEdit(); settings.RefreshOnLibraryUpdate = true; settings.TagExpiredGames = true;
                settings.ImportNewGames = false; settings.RemoveUnplayedGames = false; settings.EndEdit();
                settings.BeginEdit(); checkbox.IsChecked = false; settings.EndEdit();
                Check(!Current(owned).IsInstalled && !Current(owned).IncludeLibraryPluginAction && !Current(legacy).IsInstalled &&
                    !plugin.GetPlayActions(new GetPlayActionsArgs { Game = Current(owned) }).Any() &&
                    plugin.GetInstallActions(new GetInstallActionsArgs { Game = Current(owned) }).Count() == 1,
                    "Disabling the setting did not switch existing imports to the Install website handoff.");
                var saved = plugin.LoadPluginSettings<BrowserPreferences>()!;
                Check(!saved.MarkGamesInstalled && saved.Category == CatalogCategory.GameNight && saved.TitleSort == CatalogTitleSort.Descending && saved.Width == 1100 && saved.Height == 800,
                    "Saving installed state lost browser preferences.");
                using var restarted = new LunaPlugin(host.Api);
                Check(!((LunaSettings)restarted.GetSettings(false)).MarkGamesInstalled, "Restart forgot the disabled setting.");
                using var adapter = new PlayniteImportDatabase(library.Api, Dispatcher.CurrentDispatcher, root,
                    () => plugin.LoadPluginSettings<BrowserPreferences>()!.MarkGamesInstalled);
                var importer = new CatalogImportService(adapter, new JsonImportMappingStore(root), utcNow: () => time);
                var disabledImport = await importer.ImportAsync(catalog, new[] { catalog.Games[1].Id }, CancellationToken.None);
                Check(disabledImport.Added == 1, "A disabled-mode import failed: " + disabledImport.Summary);
                var created = library.Games.Values.Single(g => LunaImportIdentity.TryDecode(g.GameId, out var identity) && identity.LunaId == catalog.Games[1].Id).Id;
                Check(!library.Games.Api.Get(created).IsInstalled && !library.Games.Api.Get(created).IncludeLibraryPluginAction,
                    "A new import ignored the saved disabled setting.");
                settings.BeginEdit(); checkbox.IsChecked = true; settings.EndEdit();
                Check(library.Games.Api.Get(created).IsInstalled && Current(owned).IsInstalled && Current(owned).IncludeLibraryPluginAction,
                    "Re-enabling the setting did not update all owned imports.");
                var enabledImport = await importer.ImportAsync(catalog, new[] { catalog.Games[2].Id }, CancellationToken.None);
                Check(enabledImport.Added == 1, "An enabled-mode import failed: " + enabledImport.Summary);
                var enabledId = library.Games.Values.Single(g => LunaImportIdentity.TryDecode(g.GameId, out var identity) && identity.LunaId == catalog.Games[2].Id).Id;
                Check(library.Games.Api.Get(enabledId).IsInstalled && library.Games.Api.Get(enabledId).IncludeLibraryPluginAction,
                    "A new import ignored the enabled setting.");
                Check(Current(owned).Name == owned.Name && Current(owned).Favorite && Current(owned).Notes == owned.Notes && Current(owned).Playtime == owned.Playtime &&
                    Current(owned).GameId == owned.GameId && Current(owned).GameActions.Single().Path == owned.GameActions.Single().Path &&
                    !Current(owned).GameActions.Single().IsPlayAction && !Current(foreign).IsInstalled && !Current(unmarked).IsInstalled && !Current(manual).IsInstalled,
                    "Changing installed state overwrote metadata, actions, identity, or unrelated entries.");
                var file = Path.Combine(root, LunaImportIdentity.PluginId.ToString(), "config.json");
                var bytes = File.ReadAllBytes(file);
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    settings.BeginEdit(); settings.MarkGamesInstalled = false;
                    try { settings.EndEdit(); throw new InvalidOperationException("Saving to a locked settings file unexpectedly succeeded."); }
                    catch (IOException) { }
                    settings.CancelEdit();
                    Check(settings.MarkGamesInstalled && Current(owned).IsInstalled, "A failed settings save changed effective behavior or library state.");
                }
                Check(bytes.SequenceEqual(File.ReadAllBytes(file)) && host.Errors == 0, "A failed settings save replaced the original file or caused an unexpected host error.");
            }
            finally { window.Close(); }
            plugin.SavePluginSettings(new { SchemaVersion = 2, MarkGamesInstalled = false });
            using var future = new LunaPlugin(host.Api);
            Check(!future.GetSettings(false).VerifySettings(out var futureErrors) && futureErrors.Count > 0,
                "The settings editor would overwrite a newer settings schema.");
            Console.WriteLine("Windows Luna installed-setting checks passed (default/upgrade, UI Save/Cancel, existing/new imports, restart, metadata, save failure).");
        }

        private sealed class LibraryProvider : ILunaCatalogProvider
        {
            internal int Calls;
            internal bool Fail, Block;
            internal string? MissingAsin;
            public async Task<CatalogSnapshot> FetchAsync(string region, CancellationToken token)
            {
                Calls++;
                if (Block) await Task.Delay(Timeout.Infinite, token);
                if (Fail) throw new HttpRequestException("Offline test");
                var snapshot = Snapshot();
                if (MissingAsin != null)
                {
                    foreach (var query in snapshot.Queries)
                    { query.Games.RemoveAll(g => g.Asin == MissingAsin); query.TilesObserved = query.Games.Count; }
                    snapshot = CatalogSnapshot.Merge(snapshot.Queries);
                }
                snapshot.RetrievedAt = time.AddMinutes(Calls); return snapshot;
            }
        }
        private static async Task CheckLibraryUpdateAsync(string directory)
        {
            var root = Path.Combine(directory, "library-update");
            var library = new SmokeDatabase(Path.Combine(root, "library"));
            var host = new SmokeDatabase.Host(library, root); var provider = new LibraryProvider();
            using var plugin = new LunaPlugin(host.Api, provider);
            var result = await Task.Run(() => plugin.ImportGames(new LibraryImportGamesArgs()).ToArray());
            Check(provider.Calls == 1 && result.Length == 0 && library.Games.Values.Count == 0,
                "A default library update did not refresh or imported games without opt-in.");
            var folder = Path.Combine(root, LunaImportIdentity.PluginId.ToString());
            Check(File.Exists(Path.Combine(folder, "catalog-US.json")), "The library update did not persist its validated catalog.");
            var settings = (LunaSettings)plugin.GetSettings(false);
            settings.BeginEdit(); settings.RefreshOnLibraryUpdate = false; settings.EndEdit();
            await Task.Run(() => plugin.ImportGames(new LibraryImportGamesArgs()).ToArray());
            Check(provider.Calls == 1, "A disabled library refresh made an API request.");
            settings.BeginEdit(); settings.RefreshOnLibraryUpdate = true; settings.EndEdit();
            var bytes = File.ReadAllBytes(Path.Combine(folder, "catalog-US.json"));
            var journal = File.ReadAllBytes(Path.Combine(folder, "catalog-automation-US.json"));
            provider.Fail = true;
            try { await Task.Run(() => plugin.ImportGames(new LibraryImportGamesArgs()).ToArray()); throw new Exception("An offline refresh was not reported to Playnite."); }
            catch (InvalidOperationException) { }
            Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "catalog-US.json"))) &&
                journal.SequenceEqual(File.ReadAllBytes(Path.Combine(folder, "catalog-automation-US.json"))),
                "A failed library refresh changed the accepted catalog or automation observations.");
            provider.Fail = false; provider.Block = true;
            using var cancelled = new CancellationTokenSource();
            var args = new LibraryImportGamesArgs();
            typeof(LibraryImportGamesArgs).GetProperty("CancelToken")!.SetValue(args, cancelled.Token);
            var update = Task.Run(() => plugin.ImportGames(args).ToArray());
            await Until(() => provider.Calls == 3); cancelled.Cancel();
            Check((await update).Length == 0 && library.Games.Values.Count == 0, "Cancelled library update did not finish safely.");
            provider.Block = false;
            using var adapter = new PlayniteImportDatabase(library.Api, Dispatcher.CurrentDispatcher, folder);
            var importer = new CatalogImportService(adapter, new JsonImportMappingStore(folder), utcNow: () => time);
            var catalog = CatalogValidator.ValidateSnapshot(Snapshot(), "US", time);
            Check((await importer.ImportAsync(catalog, new[] { catalog.Games[0].Id }, CancellationToken.None)).Added == 1,
                "The launch-protection fixture could not be imported.");
            var protectedGame = library.Games.Values.Single();
            settings.BeginEdit(); settings.RemoveUnplayedGames = true; settings.EndEdit();
            provider.MissingAsin = catalog.Games[0].Asin;
            var attemptedOpen = false;
            library.Games.BeforeUpdate = game =>
            {
                if (game.Id != protectedGame.Id || !library.Tags.Values.Any(tag => tag.Name == PlayniteImportDatabase.ExpiredTag && game.TagIds.Contains(tag.Id))) return;
                attemptedOpen = true;
                Check(new JsonCatalogAutomationStore(folder).Read().TaggedGameIds.Contains(game.Id),
                    "The SDK expiry-tag write started before managed ownership was saved.");
                // Reproduce a launch during ApplyAsync with a readable journal whose replacement fails.
                using (var locked = new FileStream(Path.Combine(folder, "catalog-automation-US.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var starting = new OnGameStartingEventArgs();
                    typeof(OnGameStartingEventArgs).GetProperty("Game")!.SetValue(starting, game);
                    plugin.OnGameStarting(starting);
                }
            };
            try { await Task.Run(() => plugin.ImportGames(new LibraryImportGamesArgs()).ToArray()); }
            finally { library.Games.BeforeUpdate = null; }
            Check(attemptedOpen && library.Games.Values.Any(game => game.Id == protectedGame.Id) &&
                !new JsonCatalogAutomationStore(folder).Read().OpenedGameIds.Contains(protectedGame.Id),
                "A failed launch-protection save during an active refresh did not block removal.");
            Console.WriteLine("Windows active-update launch-protection failure checks passed (readable journal, failed replacement, opened game retained).");
            Console.WriteLine("Windows Luna library-update checks passed (default refresh, opt-out, background SDK callback, cache/journal failure retention, cancellation).");
        }
        private static async Task CheckAutomationAdapterAsync(string directory)
        {
            var root = Path.Combine(directory, "automation-adapter");
            var library = new SmokeDatabase(Path.Combine(root, "library"));
            using var adapter = new PlayniteImportDatabase(library.Api, Dispatcher.CurrentDispatcher, root);
            var imports = new CatalogImportService(adapter, new JsonImportMappingStore(root), utcNow: () => time);
            var catalog = CatalogValidator.ValidateSnapshot(Snapshot(), "US", time);
            var batch = await imports.ImportAsync(catalog, catalog.Games.Select(g => g.Id), CancellationToken.None);
            Check(batch.Added == 5, "Adapter fixture imports failed.");
            var games = library.Games.Values.ToArray();
            games[0].Playtime = 12; games[1].PlayCount = 1; games[2].LastActivity = DateTime.Now; games[3].IsLaunching = true;
            foreach (var game in games.Take(4))
                Check(!adapter.RemoveUnplayed(game.Id, game.GameId, () => true), "Removal ignored recorded play history or an active operation.");
            var candidate = games[4]; var marker = candidate.GameId;
            Check(!adapter.RemoveUnplayed(candidate.Id, marker, () => false), "Removal ignored launch protection.");
            var title = games[0].Name; var action = games[0].GameActions[0].Path;
            try
            {
                adapter.SetExpired(games[0].Id, games[0].GameId, true, () => throw new IOException("Ownership journal unavailable"));
                throw new InvalidOperationException("A failed ownership save did not abort the SDK tag write.");
            }
            catch (IOException) { }
            Check(!library.Tags.Values.Any(tag => tag.Name == PlayniteImportDatabase.ExpiredTag && library.Games.Api.Get(games[0].Id).TagIds.Contains(tag.Id)),
                "The adapter applied an expiry tag after its ownership save failed.");
            Check(adapter.SetExpired(games[0].Id, games[0].GameId, true), "The adapter did not add an expired tag.");
            var expired = library.Games.Api.Get(games[0].Id);
            Check(expired.Name == title && expired.GameActions[0].Path == action && expired.Playtime == 12 && expired.TagIds.Count == games[0].TagIds.Count + 1,
                "Expiry tagging overwrote existing metadata or membership tags.");
            Check(!adapter.SetExpired(expired.Id, expired.GameId, true, () => throw new InvalidOperationException("An existing tag was claimed.")),
                "The adapter changed or claimed an existing expiry tag.");
            Check(adapter.SetExpired(expired.Id, expired.GameId, false), "The adapter did not clear an expired tag.");
            candidate.PluginId = Guid.NewGuid();
            Check(!adapter.RemoveUnplayed(candidate.Id, marker, () => true) && !adapter.SetExpired(candidate.Id, marker, true), "Automation changed a foreign library game.");
            candidate.PluginId = LunaImportIdentity.PluginId;
            Check(adapter.RemoveUnplayed(candidate.Id, marker, () => true) && library.Games.Values.Count == 4, "Confirmed unplayed removal did not use the SDK collection.");
            Console.WriteLine("Windows Luna automatic tag/removal adapter checks passed (metadata, history, ownership, live guards, host deletion).");
        }

        private static void CheckPlayWebsite()
        {
            var game = new Game("Edited title")
            {
                PluginId = LunaImportIdentity.PluginId, GameId = LunaImportIdentity.Encode("US", "B0GFWDM8NP", "B0GFWDM8NP"),
                IsInstalled = true, IncludeLibraryPluginAction = true, Playtime = 42,
                Links = new ObservableCollection<Link> { new Link("Amazon Luna", "https://luna.amazon.com/game/alan-wake-2/B0GFWDM8NP") }
            };
            var opened = 0; var stopped = 0; var started = 0;
            void Exercise(bool fail)
            {
                using var controller = LunaWebsitePlayController.ForGame(game, uri =>
                {
                    if (fail) throw new System.ComponentModel.Win32Exception("No browser.");
                    Check(uri.AbsoluteUri == game.Links[0].Url, "Play opened a different game URL."); opened++;
                })!;
                typeof(PlayController).GetEvent("Stopped", BindingFlags.NonPublic | BindingFlags.Instance)!.GetAddMethod(true)!.Invoke(controller,
                    new object[] { new EventHandler<GameStoppedEventArgs>((_, args) => { stopped++; game.IsLaunching = false; game.IsRunning = false; Check(args.SessionLength == 0, "Play invented streaming duration."); }) });
                typeof(PlayController).GetEvent("Started", BindingFlags.NonPublic | BindingFlags.Instance)!.GetAddMethod(true)!.Invoke(controller,
                    new object[] { new EventHandler<GameStartedEventArgs>((_, __) => started++) });
                game.IsLaunching = true;
                try { controller.Play(new PlayActionArgs()); Check(!fail, "Play swallowed a browser-launch failure."); }
                catch (System.ComponentModel.Win32Exception) when (fail) { }
                Check(!game.IsLaunching && !game.IsRunning && game.IsInstalled && game.Playtime == 42, "Play left a launch state or changed installed state/playtime.");
            }
            Exercise(false); Exercise(false); Exercise(true);
            Check(opened == 2 && stopped == 3 && started == 0, "Play did not finish each handoff or incorrectly tracked a streaming session.");
            game.PluginId = Guid.NewGuid();
            Check(LunaWebsitePlayController.ForGame(game, _ => { }) == null, "Play intercepted another library.");
            game.PluginId = LunaImportIdentity.PluginId; game.Links[0].Url = "https://example.com/";
            Check(LunaWebsitePlayController.ForGame(game, _ => { }) == null, "Play accepted an unsafe URL.");
            Console.WriteLine("Windows Luna Play handoff checks passed (repeat, shared URL validation, failure cleanup, no session tracking).");
        }

        private static async Task CheckSidebarAsync(string directory)
        {
            var root = Path.Combine(directory, "sidebar-host");
            var library = new SmokeDatabase(Path.Combine(root, "library"));
            var host = new SmokeDatabase.Host(library, root);
            var accepted = CatalogValidator.ValidateSnapshot(Snapshot(), "US", time);
            foreach (var game in accepted.Games) game.CoverUrl = null; // No live artwork requests in this host check.
            var cache = new JsonCatalogCache(Path.Combine(root, LunaImportIdentity.PluginId.ToString()));
            await cache.SaveAsync(accepted, CancellationToken.None);
            var bytes = File.ReadAllBytes(Path.Combine(root, LunaImportIdentity.PluginId.ToString(), "catalog-US.json"));
            using var plugin = new LunaPlugin(host.Api);
            var tab = plugin.GetSidebarItems().Single();
            Check(tab.Type == SiderbarItemType.View && tab.Title == "Browse Amazon Luna Catalog" && tab.Icon != null,
                "Luna did not register an inline sidebar view with an icon.");
            var icon = tab.Icon as System.Windows.Shapes.Path;
            Check(icon?.Data != null && icon.Data.IsFrozen && icon.Width == 22 && icon.Height == 22 &&
                icon.Data.FillContains(new Point(9.64, 3)) && !icon.Data.FillContains(new Point(9.64, 11.5)) &&
                !icon.Data.FillContains(new Point(0, 0)),
                "The sidebar did not load the standalone official Luna vector with transparent surroundings and center.");
            Check(ReferenceEquals(tab, plugin.GetSidebarItems().Single()), "Sidebar enumeration recreated the view registration.");
            host.Mode = ApplicationMode.Fullscreen;
            Check(!plugin.GetSidebarItems().Any(), "The WPF sidebar is exposed in Fullscreen mode.");
            host.Mode = ApplicationMode.Desktop;
            var view = tab.Opened() as CatalogBrowserView;
            Check(view != null, "The sidebar callback did not return the catalog control.");
            Check(host.DialogWindow == null, "The sidebar opened a separate window instead of an inline control.");
            var model = (CatalogBrowserModel)view!.DataContext;
            var frame = new ContentControl { Content = view };
            Application.Current.Resources["GlyphBrush"] = Brushes.Gray;
            var sidebarButton = new Button { Content = icon };
            sidebarButton.SetResourceReference(Button.ForegroundProperty, "GlyphBrush");
            var layout = new DockPanel();
            DockPanel.SetDock(sidebarButton, Dock.Top); layout.Children.Add(sidebarButton); layout.Children.Add(frame);
            var window = new Window { Content = layout, Width = 760, Height = 720 };
            host.MainWindow = window;
            try
            {
                window.Show(); await Until(() => !model.IsBusy && model.TotalCount == 5 && view.IsLoaded);
                await Until(() => ReferenceEquals(icon!.Fill, Brushes.Gray));
                Application.Current.Resources["GlyphBrush"] = Brushes.DodgerBlue;
                await Until(() => ReferenceEquals(icon!.Fill, Brushes.DodgerBlue));
                sidebarButton.Foreground = Brushes.White;
                await Until(() => ReferenceEquals(icon!.Fill, Brushes.White));
                Console.WriteLine("Windows monochrome Luna sidebar icon checks passed (offline vector, inherited theme/selection foreground).");
                Control<TextBox>(view, "SearchBox").Text = "Alan";
                model.Category = CatalogCategory.Premium; model.TitleSort = CatalogTitleSort.Descending;
                model.SelectedGame = model.VisibleGames.Single(); var selectedId = model.SelectedGame.Id;
                tab.Closed(); frame.Content = new TextBlock { Text = "Library" };
                await Until(() => !view.IsLoaded);
                var firstPreferences = plugin.LoadPluginSettings<BrowserPreferences>();
                Check(firstPreferences?.Category == CatalogCategory.Premium && firstPreferences.TitleSort == CatalogTitleSort.Descending,
                    "Leaving the sidebar did not save category and sort preferences.");
                Check(ReferenceEquals(tab.Opened(), view), "Reopening the tab discarded its catalog control.");
                frame.Content = view;
                await Until(() => view.IsLoaded && !model.IsBusy);
                Check(model.SearchText == "Alan" && model.SelectedGame?.Id == selectedId && Control<ListBox>(view, "GamesList").SelectedItems.Count == 1,
                    "Sidebar reopen lost local filters or selection.");

                model.Category = CatalogCategory.All;
                var menu = plugin.GetMainMenuItems(new GetMainMenuItemsArgs()).Single();
                menu.Action(new MainMenuItemActionArgs());
                await Until(() => host.DialogWindow?.IsVisible == true && !model.IsBusy);
                var popout = (CatalogBrowserView)host.DialogWindow!.Content;
                Check(!ReferenceEquals(popout, view) && ReferenceEquals(popout.DataContext, model), "Window and sidebar do not safely share browser state.");
                Control<TextBox>(popout, "SearchBox").Text = "WRC";
                Check(model.VisibleCount == 1 && Control<TextBox>(view, "SearchBox").Text == "WRC", "Window filters did not update the sidebar.");
                model.SearchText = "Alan";
                model.SelectedGame = model.VisibleGames.Single();
                host.DialogWindow.Close();
                Check(model.SelectedGame?.Id == selectedId && Control<ListBox>(view, "GamesList").SelectedItems.Count == 1,
                    "Closing the window cleared the sidebar's shared selection.");
                tab.Closed(); frame.Content = new TextBlock { Text = "Library" };
                var preferences = plugin.LoadPluginSettings<BrowserPreferences>();
                Check(preferences != null && preferences.SchemaVersion == 1, "Sidebar close did not persist browser preferences.");
                var settings = (LunaSettings)plugin.GetSettings(false);
                settings.BeginEdit(); settings.MarkGamesInstalled = false; settings.EndEdit();
                frame.Content = tab.Opened(); await Until(() => view.IsLoaded && !model.IsBusy);
                tab.Closed(); frame.Content = new TextBlock(); await Until(() => !view.IsLoaded);
                Check(plugin.LoadPluginSettings<BrowserPreferences>()?.MarkGamesInstalled == false,
                    "Saving browser preferences overwrote the changed installed setting.");
                Check(bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(root, LunaImportIdentity.PluginId.ToString(), "catalog-US.json"))),
                    "Sidebar/window switching modified the accepted catalog cache.");
                Check(host.Errors == 0 && library.Games.Values.Count == 0, "Opening the browser caused an error or imported games.");
                plugin.Dispose();
                Check(!tab.Visible && !plugin.GetSidebarItems().Any(), "Shutdown left an active sidebar registration.");
            }
            finally { host.DialogWindow?.Close(); window.Close(); }
        }
    }
}
