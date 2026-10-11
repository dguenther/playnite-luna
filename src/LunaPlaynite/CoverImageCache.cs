using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using LunaCatalogBrowser;

namespace LunaPlaynite
{
    public sealed class CoverImageCache
    {
        private readonly CatalogArtworkClient artwork;
        private readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);
        private readonly Queue<string> order = new Queue<string>();
        private readonly object sync = new object();
        public CoverImageCache(CatalogArtworkClient artwork) { this.artwork = artwork; }
        public async Task<BitmapSource?> LoadAsync(string? url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (url == null) return null;
            lock (sync) { if (images.TryGetValue(url, out var cached)) return cached; }
            var bytes = await artwork.DownloadAsync(url, token).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return null;
            var image = await Task.Run(() =>
            {
                try
                {
                    using (var stream = new MemoryStream(bytes, writable: false))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 128; bitmap.StreamSource = stream; bitmap.EndInit();
                        if (bitmap.PixelHeight > 2048) return null;
                        bitmap.Freeze();
                        return (BitmapSource?)bitmap;
                    }
                }
                catch (Exception ex) when (ex is NotSupportedException || ex is IOException || ex is ArgumentException ||
                    ex is InvalidOperationException || ex is FormatException || ex is System.Runtime.InteropServices.COMException)
                { return null; }
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (image == null) return null;
            lock (sync)
            {
                if (!images.ContainsKey(url))
                {
                    if (images.Count >= 96) images.Remove(order.Dequeue());
                    images.Add(url, image); order.Enqueue(url);
                }
                return images[url];
            }
        }
    }
}
