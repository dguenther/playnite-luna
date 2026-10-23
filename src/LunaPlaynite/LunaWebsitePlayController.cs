using System;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LunaPlaynite
{
    public sealed class LunaWebsitePlayController : PlayController
    {
        private readonly Uri page;
        private readonly Action<Uri> open;
        private LunaWebsitePlayController(Game game, Uri page, Action<Uri> open) : base(game)
        {
            this.page = page; this.open = open; Name = "Open in Luna";
        }
        public static PlayController? ForGame(Game game, Action<Uri> open) =>
            LunaGamePage.TryResolve(game, out var page) ? new LunaWebsitePlayController(game, page, open) : null;
        public override void Play(PlayActionArgs args)
        {
            try { open(page); }
            finally
            {
                // A browser handoff does not establish a streaming session or its duration.
                // Stopping without a Started event clears the host's launching state.
                InvokeOnStopped(new GameStoppedEventArgs(0));
            }
        }
    }
}
