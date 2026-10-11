using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LunaCatalog
{
    public static class LunaGameDetailParser
    {
        public static GameDetailObservation Parse(string json, LunaGame expected)
        {
            var observation = new GameDetailObservation { RequestedId = expected.Id, RequestedUrl = expected.Url };
            try
            {
                var root = LunaJson.ParseObject(json);
                observation.ObservedPageType = Text(root.SelectToken("pageContext.pageType"));
                if (observation.ObservedPageType != "game_detail_anonymous" && observation.ObservedPageType != "game_detail")
                {
                    observation.Diagnostics.Add("Unexpected game-detail page type.");
                    return observation;
                }
                var widgets = Objects(root).ToList();
                var products = widgets.Where(w => Text(w["type"]) == "PRODUCT_DETAIL").ToList();
                if (products.Count != 1)
                {
                    observation.Diagnostics.Add("Expected exactly one main PRODUCT_DETAIL; recommendations are not the requested game.");
                    return observation;
                }
                var product = Presentation(products[0]);
                observation.ReturnedId = Text(product["productId"]);
                observation.Asin = Text(product["asin"]);
                observation.Title = Text(product["title"]);
                var seo = widgets.Where(w => Text(w["type"]) == "SEO_DETAIL").ToList();
                if (seo.Count == 1) observation.CanonicalUrl = Text(Presentation(seo[0])["canonicalUrl"]);
                var buttons = widgets.Where(w => Text(w["type"]) == "PRODUCT_DETAIL_PRIMARY_BUTTON").ToList();
                if (buttons.Count == 1) observation.PrimaryButtonText = Text(Presentation(buttons[0])["primaryText"]);
                observation.IdentityMatches = observation.ReturnedId == expected.Id &&
                    (expected.Asin == null || expected.Asin == observation.Asin);
                observation.UrlMatches = Uri.TryCreate(observation.CanonicalUrl, UriKind.Absolute, out var actual) &&
                    Uri.TryCreate(expected.Url, UriKind.Absolute, out var requested) &&
                    actual.Scheme == "https" && actual.Host == "luna.amazon.com" && actual.Port == 443 && actual.UserInfo.Length == 0 &&
                    actual.AbsoluteUri == requested.AbsoluteUri;
                if (!observation.IdentityMatches) observation.Diagnostics.Add("Returned product identity differs from the catalog tile.");
                if (!observation.UrlMatches) observation.Diagnostics.Add("Canonical game URL differs from the catalog tile.");
                observation.ResponseValid = observation.IdentityMatches && observation.UrlMatches && !string.IsNullOrWhiteSpace(observation.Title);
                observation.Diagnostics.Add("Button text is an observation of this anonymous request, not verified entitlement, playability, or removal status.");
            }
            catch (JsonException)
            {
                observation.Diagnostics.Add("Malformed game-detail response or presentationData.");
            }
            return observation;
        }

        private static string? Text(JToken? token) => token?.Type == JTokenType.String ? token.Value<string>() : null;

        private static JObject Presentation(JObject widget) => widget["presentationData"] is JObject obj ? obj :
            LunaJson.ParseObject(Text(widget["presentationData"]) ?? "null");

        private static IEnumerable<JObject> Objects(JToken root)
        {
            var pending = new Stack<JToken>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var token = pending.Pop();
                if (token is JObject obj) yield return obj;
                if (token is JContainer container)
                    foreach (var child in container.Children().Reverse()) pending.Push(child);
            }
        }
    }
}
