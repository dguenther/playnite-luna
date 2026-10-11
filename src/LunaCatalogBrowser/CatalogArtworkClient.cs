using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LunaCatalogBrowser
{
    // Artwork is optional: failures never invalidate a catalog or block browsing.
    public sealed class CatalogArtworkClient
    {
        public const int MaximumImageBytes = 2 * 1024 * 1024;
        private readonly HttpClient client;
        private readonly TimeSpan requestTimeout;
        private readonly SemaphoreSlim downloads = new SemaphoreSlim(4, 4);
        public CatalogArtworkClient(HttpClient client, TimeSpan? requestTimeout = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
            if (this.requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        public async Task<byte[]?> DownloadAsync(string? url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.Host != "m.media-amazon.com" || uri.Port != 443 || uri.UserInfo.Length != 0) return null;
            await downloads.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // A released slot can win the race with a queued waiter's cancellation.
                cancellationToken.ThrowIfCancellationRequested();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(requestTimeout);
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumImageBytes) return null;
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var bytes = new MemoryStream())
                            {
                                var buffer = new byte[8192];
                                int read;
                                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                {
                                    if (bytes.Length + read > MaximumImageBytes) return null;
                                    bytes.Write(buffer, 0, read);
                                }
                                timeout.Token.ThrowIfCancellationRequested();
                                return bytes.ToArray();
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
                    catch (HttpRequestException) { return null; }
                    catch (IOException) { return null; }
                }
            }
            finally { downloads.Release(); }
        }
    }
}
