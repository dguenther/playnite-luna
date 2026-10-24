using LunaCatalogBrowser;

namespace LunaPlaynite
{
    // Saved through Playnite's plugin-settings API; catalog data uses a separate cache.
    public sealed class BrowserPreferences
    {
        public int SchemaVersion { get; set; } = 1;
        public bool MarkGamesInstalled { get; set; } = true;
        public bool RefreshOnLibraryUpdate { get; set; } = true;
        public bool TagExpiredGames { get; set; } = true;
        public bool ImportNewGames { get; set; }
        public bool RemoveUnplayedGames { get; set; }
        public CatalogCategory Category { get; set; }
        public CatalogTitleSort TitleSort { get; set; }
        public double Width { get; set; } = 1060;
        public double Height { get; set; } = 720;
        public BrowserPreferences Copy() => (BrowserPreferences)MemberwiseClone();
    }
}
