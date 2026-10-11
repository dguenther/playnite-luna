using System;
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
            application.Resources["TextBrush"] = Brushes.Black;
            var directory = Path.Combine(Path.GetTempPath(), "luna-wpf-smoke-" + Guid.NewGuid().ToString("N"));
            Window? window = null;
            try
            {
                var cache = new JsonCatalogCache(directory, () => time);
                await cache.SaveAsync(CatalogValidator.ValidateSnapshot(Snapshot(), "US", time), CancellationToken.None);
                var originalBytes = File.ReadAllBytes(Path.Combine(directory, "catalog-US.json"));
                var provider = new Provider();
                var model = new CatalogBrowserModel(new CatalogService(provider, cache, utcNow: () => time));
                var uiThread = Thread.CurrentThread.ManagedThreadId;
                var wrongThread = false;
                model.PropertyChanged += (_, __) => wrongThread |= Thread.CurrentThread.ManagedThreadId != uiThread;
                var bitmap = new WriteableBitmap(2, 3, 96, 96, PixelFormats.Bgra32, null);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                byte[] image;
                using (var bytes = new MemoryStream()) { encoder.Save(bytes); image = bytes.ToArray(); }
                var imageHandler = new ImageHandler { Image = image };
                using (var client = new HttpClient(imageHandler))
                {
                    var covers = new CoverImageCache(new CatalogArtworkClient(client));
                    string? openedId = null;
                    var view = new CatalogBrowserView(model, covers, game => openedId = game.Id);
                    window = new Window { Content = view, Width = 1060, Height = 720 };
                    window.Show();
                    await Until(() => !model.IsBusy && model.TotalCount == 5);
                    Check(provider.Calls == 0, "Opening a cached browser contacted the provider.");
                    var list = Control<ListBox>(view, "GamesList");
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
                    var restarted = new CatalogBrowserModel(new CatalogService(provider, new JsonCatalogCache(directory, () => time), utcNow: () => time));
                    await restarted.LoadAsync(CancellationToken.None);
                    Check(provider.Calls == 2 && restarted.VisibleGames.Any(g => g.Title == "Alan Wake revised"), "Restart did not reload the accepted cache without a fetch.");
                }
            }
            finally { window?.Close(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
