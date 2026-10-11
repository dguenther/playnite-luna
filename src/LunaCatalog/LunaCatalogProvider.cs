using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LunaCatalog
{
    public interface ILunaCatalogProvider
    {
        Task<CatalogSnapshot> FetchAsync(string region, CancellationToken cancellationToken);
    }

    public sealed class LunaCatalogProvider : ILunaCatalogProvider
    {
        public static readonly Uri Endpoint = new Uri("https://proxy-prod.eu-west-1.tempo.digital.a2z.com/getPage");
        public const int MaximumResponseBytes = 8 * 1024 * 1024;
        private readonly HttpClient client;
        private readonly string userAgent;
        private readonly string browserVersion;
        private readonly TimeSpan requestTimeout;

        public LunaCatalogProvider(HttpClient client, string userAgent, string browserVersion, TimeSpan? requestTimeout = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.userAgent = userAgent;
            this.browserVersion = browserVersion;
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            if (this.requestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        public async Task<CatalogSnapshot> FetchAsync(string region, CancellationToken cancellationToken)
        {
            if (region != "US") throw new ArgumentException("Only US requests are investigated in Phase 0.", nameof(region));
            var results = new System.Collections.Generic.List<QueryResult>();
            foreach (var query in CatalogQuery.UsQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await FetchQueryAsync(query, cancellationToken).ConfigureAwait(false);
                results.Add(result);
                if (result.StopFurtherRequests) break;
                // Sequential requests; no request bursts.
                if (query != CatalogQuery.UsQueries[CatalogQuery.UsQueries.Count - 1])
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            return CatalogSnapshot.Merge(results);
        }

        public async Task<QueryResult> FetchQueryAsync(CatalogQuery query, CancellationToken cancellationToken)
        {
            var page = await FetchPageAsync(() => CreateRequest(query), cancellationToken).ConfigureAwait(false);
            if (page.Error != null) return Failure(query, page.Error, page.HttpStatus, page.StopFurtherRequests);
            var result = LunaResponseParser.Parse(page.Body!, query);
            result.HttpStatus = page.HttpStatus;
            return result;
        }

        public async Task<GameDetailObservation> FetchDetailAsync(LunaGame game, CancellationToken cancellationToken)
        {
            if (game.Region != "US") throw new ArgumentException("Only US requests are investigated in Phase 0.", nameof(game));
            if (!game.Id.StartsWith("amzn1.", StringComparison.Ordinal))
            {
                var unsupported = new GameDetailObservation { RequestedId = game.Id, RequestedUrl = game.Url };
                unsupported.Diagnostics.Add("A native product ID is required for the investigated detail request.");
                return unsupported;
            }
            var page = await FetchPageAsync(() => CreateRequest(null, game), cancellationToken).ConfigureAwait(false);
            if (page.Error != null)
            {
                var failed = new GameDetailObservation { RequestedId = game.Id, RequestedUrl = game.Url, HttpStatus = page.HttpStatus,
                    StopFurtherRequests = page.StopFurtherRequests };
                failed.Diagnostics.Add(page.Error);
                return failed;
            }
            var observation = LunaGameDetailParser.Parse(page.Body!, game);
            observation.HttpStatus = page.HttpStatus;
            return observation;
        }

        private sealed class PageResponse
        {
            public string? Body { get; set; }
            public int? HttpStatus { get; set; }
            public string? Error { get; set; }
            public bool StopFurtherRequests { get; set; }
        }

        private async Task<PageResponse> FetchPageAsync(Func<HttpRequestMessage> createRequest, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(requestTimeout);
                    using (var request = createRequest())
                    {
                        int? status = null;
                        try
                        {
                            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                status = (int)response.StatusCode;
                                // One bounded retry for transient server failures. Never retry 401/403/429.
                                if (attempt == 0 && (status == 500 || status == 502 || status == 503 || status == 504))
                                {
                                    var retryAfter = response.Headers.RetryAfter;
                                    var delay = retryAfter?.Delta ?? (retryAfter?.Date.HasValue == true ?
                                        retryAfter.Date.Value - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1));
                                    if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
                                    if (delay > TimeSpan.FromSeconds(30))
                                        return new PageResponse { Error = "Server requested a longer retry delay; remaining requests stopped.",
                                            HttpStatus = status, StopFurtherRequests = true };
                                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                    continue;
                                }
                                if (!response.IsSuccessStatusCode)
                                    return new PageResponse { Error = "HTTP " + status + "; response not used as catalog data.",
                                        HttpStatus = status, StopFurtherRequests = status == 401 || status == 403 || status == 429 || response.Headers.RetryAfter != null };
                                if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                                    return new PageResponse { Error = "Response exceeds the 8 MiB size limit.", HttpStatus = status };
                                string json;
                                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                using (var bytes = new MemoryStream())
                                {
                                    var buffer = new byte[8192];
                                    int read;
                                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                                    {
                                        if (bytes.Length + read > MaximumResponseBytes)
                                            return new PageResponse { Error = "Response exceeds the 8 MiB size limit.", HttpStatus = status };
                                        bytes.Write(buffer, 0, read);
                                    }
                                    timeout.Token.ThrowIfCancellationRequested();
                                    json = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                                }
                                cancellationToken.ThrowIfCancellationRequested();
                                return new PageResponse { Body = json, HttpStatus = status };
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            return new PageResponse { Error = "Request timed out; no catalog removal can be inferred.", HttpStatus = status };
                        }
                        catch (HttpRequestException)
                        {
                            return new PageResponse { Error = "Network request failed; check network access and TLS trust.", HttpStatus = status };
                        }
                        catch (Exception ex) when (ex is IOException || ex is DecoderFallbackException)
                        {
                            return new PageResponse { Error = "Response could not be read as UTF-8 catalog data.", HttpStatus = status };
                        }
                    }
                }
            }
            throw new InvalidOperationException("Unreachable retry state.");
        }

        private HttpRequestMessage CreateRequest(CatalogQuery? query, LunaGame? game = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.TryAddWithoutValidation("Origin", "https://luna.amazon.com");
            request.Headers.Referrer = new Uri("https://luna.amazon.com/");
            request.Headers.TryAddWithoutValidation("x-amz-locale", "en_US");
            request.Headers.TryAddWithoutValidation("x-amz-marketplace-id", "ATVPDKIKX0DER");
            request.Headers.TryAddWithoutValidation("x-amz-platform", "web");
            request.Headers.TryAddWithoutValidation("x-amz-device-type", "browser");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            var body = new
            {
                timeout = 10000,
                searchContext = query == null ? null : new { query = query.Query, sort = "TITLE_A_TO_Z", filtersList = new object[0] },
                featureScheme = "WEB_V1",
                pageContext = new
                {
                    pageType = game == null ? "multistate_browse_results" : "game_detail",
                    pageId = game?.Id ?? "default",
                    encryptPageId = game == null ? (bool?)null : false,
                    productStage = game == null ? null : "Release"
                },
                clientContext = new
                {
                    browserMetadata = new
                    {
                        browserClientRole = "browser", browserType = "Chrome", browserVersion,
                        deviceModel = "unknown", deviceType = "unknown",
                        osName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "Windows" : "Linux",
                        osVersion = "unknown"
                    }
                },
                inputContext = new { gamepadTypes = new object[0] },
                dynamicFeatures = new object[0]
            };
            request.Content = new StringContent(JsonConvert.SerializeObject(body,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), Encoding.UTF8, "text/plain");
            return request;
        }

        private static QueryResult Failure(CatalogQuery query, string diagnostic, int? httpStatus = null, bool stopFurtherRequests = false)
        {
            var result = new QueryResult { Name = query.Name, HttpStatus = httpStatus, StopFurtherRequests = stopFurtherRequests };
            result.Diagnostics.Add(diagnostic);
            return result;
        }
    }
}
