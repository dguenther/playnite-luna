using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace LunaCatalogBrowser
{
    // Call on the UI context. Hosts share one model without sharing a WPF control.
    public sealed class CatalogBrowserSession : IDisposable
    {
        private readonly HashSet<object> hosts = new HashSet<object>();
        private bool disposed;
        public CatalogBrowserModel Model { get; }
        public bool HasActiveHosts => hosts.Count > 0;
        public event EventHandler? ActivityChanged;

        public CatalogBrowserSession(CatalogBrowserModel model) { Model = model ?? throw new ArgumentNullException(nameof(model)); }

        public async Task AttachAsync(object host)
        {
            if (disposed) throw new ObjectDisposedException(nameof(CatalogBrowserSession));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!hosts.Add(host)) return;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
            if (hosts.Count > 1) return;
            // Reopening can race the cancellation of the last host's work. Let it settle
            // before restoring cached data and reconciling actual library state.
            await WhenIdleAsync();
            if (!disposed && HasActiveHosts) await Model.LoadAsync(CancellationToken.None);
        }

        public void Detach(object host)
        {
            if (!hosts.Remove(host)) return;
            if (!HasActiveHosts) Model.CancelCurrentOperation();
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task RefreshAsync(object host, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await AttachAsync(host);
                await WhenIdleAsync(token);
                token.ThrowIfCancellationRequested();
                await Model.RefreshAsync(token);
            }
            finally { Detach(host); }
        }

        private async Task WhenIdleAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (!Model.IsBusy) return;
            var idle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            PropertyChangedEventHandler? changed = null;
            changed = (_, args) =>
            {
                if (args.PropertyName != nameof(Model.IsBusy) || Model.IsBusy) return;
                Model.PropertyChanged -= changed;
                idle.TrySetResult(true);
            };
            using (token.Register(() => idle.TrySetCanceled()))
            {
                Model.PropertyChanged += changed;
                changed(this, new PropertyChangedEventArgs(nameof(Model.IsBusy)));
                try { await idle.Task; }
                finally { Model.PropertyChanged -= changed; }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; hosts.Clear(); Model.CancelCurrentOperation();
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
