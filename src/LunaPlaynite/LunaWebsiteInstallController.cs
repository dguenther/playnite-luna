using System;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace LunaPlaynite
{
    // Install is a website handoff for cloud games, not a local installation.
    public sealed class LunaWebsiteInstallController : InstallController
    {
        private readonly Uri page;
        private readonly Action<Uri> open;

        private LunaWebsiteInstallController(Game game, Uri page, Action<Uri> open) : base(game)
        {
            this.page = page; this.open = open;
            Name = "Open in Luna";
        }

        public static InstallController? ForGame(Game game, Action<Uri> open)
        {
            return LunaGamePage.TryResolve(game, out var page) ? new LunaWebsiteInstallController(game, page, open) : null;
        }

        public override void Install(InstallActionArgs args)
        {
            try { open(page); }
            finally
            {
                // Playnite's Installed event would set IsInstalled=true. Cancellation is
                // the SDK completion path that clears IsInstalling without changing it.
                InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
            }
        }
    }
}
