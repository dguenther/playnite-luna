using System;
using System.Linq;
using System.Text.RegularExpressions;
using LunaCatalog;
using Playnite.SDK.Models;

namespace LunaPlaynite
{
    internal static class LunaGamePage
    {
        internal static bool TryResolve(Game game, out Uri page)
        {
            page = null!;
            if (game == null || game.PluginId != LunaImportIdentity.PluginId ||
                !LunaImportIdentity.TryDecode(game.GameId, out var identity)) return false;
            var actions = game.GameActions?.Where(a => a.Type == GameActionType.URL && a.Name == "Open in Luna")
                .Select(a => a.Path) ?? Enumerable.Empty<string>();
            var links = game.Links?.Where(l => l.Name == "Amazon Luna").Select(l => l.Url) ?? Enumerable.Empty<string>();
            foreach (var url in actions.Concat(links))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    uri.Host != "luna.amazon.com" || uri.Port != 443 || uri.UserInfo.Length != 0) continue;
                var path = Regex.Match(uri.AbsolutePath, @"^/game/[^/]+(?:/([A-Z0-9]{10}))?/?$");
                if (path.Success && (!path.Groups[1].Success || path.Groups[1].Value == identity.Asin))
                    { page = uri; return true; }
            }
            return false;
        }
    }
}
