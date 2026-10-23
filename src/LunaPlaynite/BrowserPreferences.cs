using LunaCatalogBrowser;

namespace LunaPlaynite
{
    // Saved through Playnite's plugin-settings API; catalog data uses a separate cache.
    public sealed class BrowserPreferences
    {
        public int SchemaVersion { get; set; } = 1;
        public bool MarkGamesInstalled { get; set; } = true;
        public CatalogCategory Category { get; set; }
        public CatalogTitleSort TitleSort { get; set; }
        public double Width { get; set; } = 1060;
        public double Height { get; set; } = 720;
    }
}
