using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LunaPlaynite
{
    // A recycled row cancels its previous request and cannot publish an old cover.
    public sealed class CatalogCover : Image
    {
        public static readonly DependencyProperty ArtworkUrlProperty = DependencyProperty.Register(nameof(ArtworkUrl), typeof(string),
            typeof(CatalogCover), new PropertyMetadata(null, (owner, args) => ((CatalogCover)owner).LoadCover()));
        public string? ArtworkUrl { get => (string?)GetValue(ArtworkUrlProperty); set => SetValue(ArtworkUrlProperty, value); }
        private CancellationTokenSource? request;
        public CatalogCover()
        {
            Loaded += (_, __) => LoadCover();
            Unloaded += (_, __) => { request?.Cancel(); Source = null; };
        }
        private async void LoadCover()
        {
            request?.Cancel(); Source = null;
            if (!IsLoaded) return;
            DependencyObject? parent = this;
            while (parent != null && !(parent is CatalogBrowserView)) parent = VisualTreeHelper.GetParent(parent);
            if (!(parent is CatalogBrowserView view)) return;
            var pending = new CancellationTokenSource(); request = pending;
            try
            {
                var cover = await view.Covers.LoadAsync(ArtworkUrl, pending.Token);
                if (!pending.IsCancellationRequested && IsLoaded && request == pending) Source = cover;
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (request == pending) Source = null; } // Optional artwork must never crash a host event handler.
            finally { if (request == pending) request = null; pending.Dispose(); }
        }
    }
}
