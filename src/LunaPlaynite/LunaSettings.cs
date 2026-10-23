using System;
using System.Collections.Generic;
using System.ComponentModel;
using Playnite.SDK;

namespace LunaPlaynite
{
    public sealed class LunaSettings : ISettings, INotifyPropertyChanged
    {
        private readonly Func<bool> read;
        private readonly Action<bool> save;
        private readonly Func<bool> canSave;
        private bool markGamesInstalled;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool MarkGamesInstalled
        {
            get => markGamesInstalled;
            set { markGamesInstalled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkGamesInstalled))); }
        }
        public LunaSettings(Func<bool> read, Action<bool> save, Func<bool> canSave)
        {
            this.read = read; this.save = save; this.canSave = canSave; BeginEdit();
        }
        public void BeginEdit() => MarkGamesInstalled = read();
        public void CancelEdit() => BeginEdit();
        public void EndEdit() => save(MarkGamesInstalled);
        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (!canSave()) errors.Add("These Luna settings were saved by a newer version. Update the extension before changing them.");
            return errors.Count == 0;
        }
    }
}
