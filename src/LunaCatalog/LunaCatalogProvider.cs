using System;
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
        private readonly HttpClient client;
        private readonly string userAgent;
        private readonly string browserVersion;
        private readonly TimeSpan requestTimeout;

        public LunaCatalogProvider(HttpClient client, string userAgent, string browserVersion, TimeSpan? requestTimeout = null)
        {
            this.client = client;
            this.userAgent = userAgent;
            this.browserVersion = browserVersion;
            this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        }

        public async Task<CatalogSnapshot> FetchAsync(string region, CancellationToken cancellationToken)
        {
            if (region != "US") throw new ArgumentException("Only US requests are investigated in Phase 0.", nameof(region));
            var results = new System.Collections.Generic.List<QueryResult>();
            foreach (var query in CatalogQuery.UsQueries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await FetchQueryAsync(query, cancellationToken).ConfigureAwait(false));
                // Sequential requests; no request bursts.
                if (query != CatalogQuery.UsQueries[CatalogQuery.UsQueries.Count - 1])
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            return CatalogSnapshot.Merge(results);
        }

        public async Task<QueryResult> FetchQueryAsync(CatalogQuery query, CancellationToken cancellationToken)
        {
            var page = await FetchPageAsync(() => CreateRequest(query), cancellationToken).ConfigureAwait(false);
            if (page.Error != null) return Failure(query, page.Error, page.HttpStatus);
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
                var failed = new GameDetailObservation { RequestedId = game.Id, RequestedUrl = game.Url, HttpStatus = page.HttpStatus };
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
                        try
                        {
                            using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                            {
                                // One bounded retry for transient server failures. Never retry 401/403/429.
                                if (attempt == 0 && (int)response.StatusCode >= 500)
                                {
                                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                                    continue;
                                }
                                if (!response.IsSuccessStatusCode)
                                    return new PageResponse { Error = "HTTP " + (int)response.StatusCode + "; response not used as catalog data.", HttpStatus = (int)response.StatusCode };
                                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                cancellationToken.ThrowIfCancellationRequested();
                                return new PageResponse { Body = json, HttpStatus = (int)response.StatusCode };
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            return new PageResponse { Error = "Request timed out; no catalog removal can be inferred." };
                        }
                        catch (HttpRequestException)
                        {
                            return new PageResponse { Error = "Network request failed; check network access and TLS trust." };
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

        private static QueryResult Failure(CatalogQuery query, string diagnostic, int? httpStatus = null)
        {
            var result = new QueryResult { Name = query.Name, HttpStatus = httpStatus };
            result.Diagnostics.Add(diagnostic);
            return result;
        }
    }
}
