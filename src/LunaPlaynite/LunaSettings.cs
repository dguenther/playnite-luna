using System;
using System.Collections.Generic;
using System.ComponentModel;
using Playnite.SDK;

namespace LunaPlaynite
{
    public sealed class LunaSettings : ISettings, INotifyPropertyChanged
    {
        private readonly Func<BrowserPreferences> read;
        private readonly Action<BrowserPreferences> save;
        private readonly Func<bool> canSave;
        private BrowserPreferences draft = new BrowserPreferences();
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool MarkGamesInstalled { get => draft.MarkGamesInstalled; set { draft.MarkGamesInstalled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkGamesInstalled))); } }
        public bool RefreshOnLibraryUpdate { get => draft.RefreshOnLibraryUpdate; set { draft.RefreshOnLibraryUpdate = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RefreshOnLibraryUpdate))); } }
        public bool TagExpiredGames { get => draft.TagExpiredGames; set { draft.TagExpiredGames = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TagExpiredGames))); } }
        public bool ImportNewGames { get => draft.ImportNewGames; set { draft.ImportNewGames = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImportNewGames))); } }
        public bool RemoveUnplayedGames { get => draft.RemoveUnplayedGames; set { draft.RemoveUnplayedGames = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RemoveUnplayedGames))); } }
        public LunaSettings(Func<BrowserPreferences> read, Action<BrowserPreferences> save, Func<bool> canSave)
        {
            this.read = read; this.save = save; this.canSave = canSave; BeginEdit();
        }
        public void BeginEdit()
        {
            draft = read().Copy();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        }
        public void CancelEdit() => BeginEdit();
        public void EndEdit() => save(draft.Copy());
        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!canSave()) errors.Add("These Luna settings were saved by a newer version. Update the extension before changing them.");
            return errors.Count == 0;
        }
    }
}
