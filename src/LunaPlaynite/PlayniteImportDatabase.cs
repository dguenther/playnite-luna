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
    public sealed class PlayniteImportDatabase : ICatalogImportDatabase, IDisposable
    {
        private readonly IGameDatabaseAPI database;
        private readonly Dispatcher dispatcher;
        private readonly string directory;
        public event Action? LibraryChanged;
        public PlayniteImportDatabase(IGameDatabaseAPI database, Dispatcher dispatcher, string directory)
        {
            this.database = database; this.dispatcher = dispatcher; this.directory = directory;
            database.Games.ItemCollectionChanged += GamesChanged;
            database.Games.ItemUpdated += GamesUpdated;
        }
        private void GamesChanged(object sender, ItemCollectionChangedEventArgs<Game> args) => LibraryChanged?.Invoke();
        private void GamesUpdated(object sender, ItemUpdatedEventArgs<Game> args) => LibraryChanged?.Invoke();
        public IReadOnlyList<LibraryGameIdentity> ReadIdentities() => dispatcher.Invoke(() =>
            (IReadOnlyList<LibraryGameIdentity>)database.Games.Select(game => new LibraryGameIdentity(game.Id, game.PluginId, game.GameId)).ToArray());

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
                var game = new Game(candidate.Title)
                {
                    Id = id, PluginId = LunaImportIdentity.PluginId,
                    GameId = LunaImportIdentity.Encode(candidate.Region, candidate.LunaId, candidate.Asin),
                    SourceId = database.Sources.Add("Amazon Luna").Id,
                    IsInstalled = false, IncludeLibraryPluginAction = false, PlatformIds = new List<Guid>(),
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
