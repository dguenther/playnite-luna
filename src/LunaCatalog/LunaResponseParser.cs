using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LunaCatalog
{
    public static class LunaResponseParser
    {
        private static readonly HashSet<string> ContinuationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "nextToken", "nextPageToken", "continuationToken", "continuation", "nextCursor", "cursor", "nextPage", "nextPageUrl", "hasMore", "hasNextPage", "pagination", "paginationContext" };

        public static QueryResult Parse(string json, CatalogQuery query, string region = "US")
        {
            var result = new QueryResult { Name = query.Name, PagesRetrieved = 1 };
            JObject root;
            try { root = JObject.Parse(json); }
            catch (JsonException)
            {
                result.Diagnostics.Add("Response is not a JSON object.");
                return result;
            }

            result.ObservedPageType = Text(root.SelectToken("pageContext.pageType"));
            if (result.ObservedPageType != query.ExpectedPageType)
            {
                result.Diagnostics.Add("Unexpected page type; the query may have been ignored or redirected.");
                return result;
            }

            var pending = new Stack<JToken>();
            pending.Push(root);
            var incomplete = false;
            while (pending.Count > 0)
            {
                var token = pending.Pop();
                if (token is JObject obj)
                {
                    foreach (var property in obj.Properties())
                    {
                        if (ContinuationKeys.Contains(property.Name) && IsNonEmpty(property.Value))
                        {
                            incomplete = true;
                            result.Diagnostics.Add("Unresolved pagination marker at " + property.Path + ".");
                        }
                    }
                    if (Text(obj["type"]) == "GAME_TILE")
                    {
                        result.TilesObserved++;
                        try
                        {
                            var game = ParseTile(obj, query, region);
                            result.Games.Add(game);
                        }
                        catch (Exception ex) when (ex is JsonException || ex is FormatException)
                        {
                            // Do not include raw backend data in logs.
                            result.Diagnostics.Add("Rejected GAME_TILE at " + obj.Path + ": " +
                                (ex is JsonException ? "malformed presentationData." : ex.Message));
                        }
                    }
                }
                if (token is JContainer container)
                {
                    foreach (var child in container.Children().Reverse()) pending.Push(child);
                }
            }
            result.PaginationStatus = incomplete ? "UnresolvedContinuation" : "NoContinuationObserved";
            result.ResponseValid = result.TilesObserved > 0 && result.Games.Count == result.TilesObserved && !incomplete;
            if (result.TilesObserved == 0) result.Diagnostics.Add("No game tiles; an empty response is not a validated empty catalog.");
            result.Games = result.Games.GroupBy(g => g.Region + ":" + g.Id, StringComparer.Ordinal)
                .Select(g => g.First()).ToList();
            return result;
        }

        private static bool IsNonEmpty(JToken value) => value.Type != JTokenType.Null &&
            value.Type != JTokenType.Undefined &&
            !(value.Type == JTokenType.Boolean && !(bool)value) &&
            !(value.Type == JTokenType.String && string.IsNullOrWhiteSpace((string?)value));

        private static string? Text(JToken? value) => value?.Type == JTokenType.String ? value.Value<string>() : null;

        private static LunaGame ParseTile(JObject tile, CatalogQuery query, string region)
        {
            var raw = tile["presentationData"];
            var data = raw is JObject obj ? obj :
                raw?.Type == JTokenType.String ? JObject.Parse(raw.Value<string>()!) :
                throw new FormatException("missing presentationData.");
            var title = Text(data["title"]);
            if (string.IsNullOrWhiteSpace(title)) throw new FormatException("missing title.");

            string? url = null;
            string? asin = null;
            foreach (var action in (tile["actions"] as JArray ?? new JArray()).OfType<JObject>())
            {
                var target = Text(action["target"]);
                if (string.IsNullOrWhiteSpace(target) || !Uri.TryCreate(new Uri("https://luna.amazon.com/"), target, out var uri)) continue;
                if (uri.Scheme != "https" || uri.Host != "luna.amazon.com") continue;
                var match = Regex.Match(uri.AbsolutePath, @"^/game/[^/]+(?:/([A-Z0-9]{10}))?/?$");
                if (!match.Success) continue;
                url = uri.AbsoluteUri;
                asin = match.Groups[1].Success ? match.Groups[1].Value : null;
                break;
            }
            if (url == null) throw new FormatException("missing verified Luna game-page action.");
            // Fortnite's observed action is /game/fortnite; its ASIN is in productUrl.
            if (asin == null && Uri.TryCreate(Text(data.SelectToken("hoverDetails.productUrl")), UriKind.Absolute, out var productUri) &&
                productUri.Scheme == "https" && (productUri.Host == "play.amazon.com" || productUri.Host == "luna.amazon.com"))
            {
                var productAsin = Regex.Match(productUri.Query.TrimStart('?'), @"(?:^|&)asin=([A-Z0-9]{10})(?:&|$)");
                if (productAsin.Success) asin = productAsin.Groups[1].Value;
            }
            var nativeId = Text(data["gameId"]);
            var nativeMatch = Regex.Match(nativeId ?? "", @"^amzn1\.(?:adg|luna)\.product\.([a-fA-F0-9-]+)$");
            var id = nativeMatch.Success && Guid.TryParse(nativeMatch.Groups[1].Value, out _) ? nativeId : asin;
            if (string.IsNullOrWhiteSpace(id)) throw new FormatException("missing supported stable identifier.");
            var game = new LunaGame
            {
                Id = id!, Asin = asin, Title = title!, Url = url, Region = region,
                CoverUrl = SafeImageUrl(Text(data["imagePortrait"])),
                BackgroundUrl = SafeImageUrl(Text(data["imageLandscape"])),
                ReleaseYear = int.TryParse(Text(data["releaseYear"]), out var year) ? (int?)year : null,
                Genres = (data["genreTags"] as JArray ?? new JArray()).Select(Text).Where(v => v != null).Select(v => v!).ToList(),
                PublishersText = Text(data.SelectToken("hoverDetails.publishersText"))
            };
            if (query.Membership != null) game.Categories.Add(query.Membership);
            return game;
        }

        private static string? SafeImageUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == "https" && uri.Host == "m.media-amazon.com" ? uri.AbsoluteUri : null;
    }
}
