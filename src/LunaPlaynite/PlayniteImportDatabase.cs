using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LunaCatalog;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace LunaPlaynite
{
    public sealed class PlayniteImportDatabase : ICatalogAutomationDatabase, IDisposable
    {
        private readonly IGameDatabaseAPI database;
        private readonly Dispatcher dispatcher;
        private readonly string directory;
        private readonly Func<bool> markGamesInstalled;
        public event Action? LibraryChanged;
        public PlayniteImportDatabase(IGameDatabaseAPI database, Dispatcher dispatcher, string directory, Func<bool>? markGamesInstalled = null)
        {
            this.database = database; this.dispatcher = dispatcher; this.directory = directory;
            this.markGamesInstalled = markGamesInstalled ?? (() => true);
            database.Games.ItemCollectionChanged += GamesChanged;
            database.Games.ItemUpdated += GamesUpdated;
        }
        private void GamesChanged(object sender, ItemCollectionChangedEventArgs<Game> args) => LibraryChanged?.Invoke();
        private void GamesUpdated(object sender, ItemUpdatedEventArgs<Game> args) => LibraryChanged?.Invoke();
        public IReadOnlyList<LibraryGameIdentity> ReadIdentities() => dispatcher.Invoke(() =>
            (IReadOnlyList<LibraryGameIdentity>)database.Games.Select(game => new LibraryGameIdentity(game.Id, game.PluginId, game.GameId)).ToArray());

        public const string ExpiredTag = "Luna: Expired";
        private static bool Owned(Game game) => game != null && game.PluginId == LunaImportIdentity.PluginId && LunaImportIdentity.TryDecode(game.GameId, out _);
        private static bool Busy(Game game) => game.IsRunning || game.IsLaunching || game.IsInstalling || game.IsUninstalling;
        private static bool Played(Game game) => game.Playtime > 0 || game.PlayCount > 0 || game.LastActivity != null;
        public IReadOnlyList<CatalogLibraryGame> ReadLibraryGames() => dispatcher.Invoke(() =>
        {
            var tag = database.Tags.FirstOrDefault(t => t.Name == ExpiredTag);
            return (IReadOnlyList<CatalogLibraryGame>)database.Games.Where(Owned).Select(g => new CatalogLibraryGame(
                new LibraryGameIdentity(g.Id, g.PluginId, g.GameId), Played(g), Busy(g), tag != null && g.TagIds?.Contains(tag.Id) == true)).ToArray();
        });
        public bool SetExpired(Guid id, string expectedIdentity, bool expired, Action? beforeChange = null) => dispatcher.Invoke(() =>
        {
            var game = database.Games.Get(id);
            if (!Owned(game) || game.GameId != expectedIdentity) return false;
            var tag = database.Tags.FirstOrDefault(t => t.Name == ExpiredTag);
            if (expired && tag == null) tag = database.Tags.Add(ExpiredTag);
            if (tag == null || (game.TagIds?.Contains(tag.Id) == true) == expired) return false;
            var updated = game.GetCopy(); updated.TagIds = updated.TagIds ?? new List<Guid>();
            if (expired) updated.TagIds.Add(tag.Id); else updated.TagIds.Remove(tag.Id);
            beforeChange?.Invoke();
            database.Games.Update(updated); return true;
        });
        public bool RemoveUnplayed(Guid id, string expectedIdentity, Func<bool> mayRemove) => dispatcher.Invoke(() =>
        {
            var game = database.Games.Get(id);
            if (!Owned(game) || game.GameId != expectedIdentity || Played(game) || Busy(game) || !mayRemove()) return false;
            return database.Games.Remove(game);
        });

        public int MigrateLegacyImports() => dispatcher.Invoke(() =>
        {
            var legacy = database.Games.Where(game => game.PluginId == Guid.Empty &&
                LunaImportIdentity.TryDecode(game.GameId, out _)).ToArray();
            foreach (var game in legacy)
            {
                var updated = game.GetCopy();
                updated.PluginId = LunaImportIdentity.PluginId;
                database.Games.Update(updated);
            }
            return legacy.Length;
        });

        public int ApplyInstalledState(bool installed) => dispatcher.Invoke(() =>
        {
            var owned = database.Games.Where(game => game.PluginId == LunaImportIdentity.PluginId &&
                LunaImportIdentity.TryDecode(game.GameId, out _) &&
                (game.IsInstalled != installed || game.IncludeLibraryPluginAction != installed)).ToArray();
            foreach (var game in owned)
            {
                var updated = game.GetCopy();
                updated.IsInstalled = installed; updated.IncludeLibraryPluginAction = installed;
                database.Games.Update(updated);
            }
            return owned.Length;
        });

        public string? AddGame(ImportCandidate candidate, Guid id, byte[]? coverBytes)
        {
            // Decode on the calling worker, before touching host SDK objects on the dispatcher.
            var cover = DecodeCover(coverBytes);
            return dispatcher.Invoke(() =>
            {
                if (database.Games.Get(id) != null) throw new InvalidOperationException("The new game GUID already exists.");
                var related = new List<ImportMapping>();
                foreach (var existing in database.Games)
                    if (LunaImportIdentity.IsOwner(existing.PluginId) && LunaImportIdentity.TryDecode(existing.GameId, out var identity) &&
                        (identity.LunaId == candidate.LunaId || identity.Asin == candidate.Asin)) related.Add(identity);
                if (related.Count == 1 && LunaImportIdentity.Matches(related[0], candidate.LunaId, candidate.Asin)) throw new ImportAlreadyExistsException();
                if (related.Count > 0) throw new InvalidOperationException("Conflicting Luna identities exist in the library.");
                var installed = markGamesInstalled();
                var game = new Game(candidate.Title)
                {
                    Id = id, PluginId = LunaImportIdentity.PluginId,
                    GameId = LunaImportIdentity.Encode(candidate.Region, candidate.LunaId, candidate.Asin),
                    SourceId = database.Sources.Add("Amazon Luna").Id,
                    IsInstalled = installed, IncludeLibraryPluginAction = installed, PlatformIds = new List<Guid>(),
                    Added = DateTime.Now,
                    Links = new ObservableCollection<Link> { new Link("Amazon Luna", candidate.Url) },
                    GameActions = new ObservableCollection<GameAction> { new GameAction
                        { Name = "Open in Luna", Type = GameActionType.URL, Path = candidate.Url, IsPlayAction = false } },
                    TagIds = candidate.Memberships.Select(m => database.Tags.Add("Luna: " + m).Id).ToList()
                };
                string? warning = null;
                if (cover != null)
                    try { game.CoverImage = AddCover(cover, id); }
                    catch (Exception) { warning = "The game was imported without its optional cover. You can add artwork in Playnite."; }
                try { database.Games.Add(game); }
                catch
                {
                    // Keep artwork if the game committed before the host reported an error.
                    if (database.Games.Get(id) == null && !string.IsNullOrEmpty(game.CoverImage))
                        try { database.RemoveFile(game.CoverImage); } catch (Exception) { }
                    throw;
                }
                return warning;
            });
        }
        private string AddCover(byte[] bytes, Guid id)
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, "import-cover-" + Guid.NewGuid().ToString("N") + ".png");
            try { File.WriteAllBytes(temporary, bytes); return database.AddFile(temporary, id); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static byte[]? DecodeCover(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 2 * 1024 * 1024) return null;
            try
            {
                using (var input = new MemoryStream(bytes, writable: false))
                using (var output = new MemoryStream())
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 256; image.DecodePixelHeight = 384; image.StreamSource = input; image.EndInit(); image.Freeze();
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output);
                    return output.Length <= 2 * 1024 * 1024 ? output.ToArray() : null;
                }
            }
            catch (Exception) { return null; } // Optional artwork cannot invalidate a library import.
        }
        public void Dispose()
        {
            database.Games.ItemCollectionChanged -= GamesChanged;
            database.Games.ItemUpdated -= GamesUpdated;
        }
    }
}
