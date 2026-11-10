using ConsoleUpdateCenter.Core;
using Playnite.SDK.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// EXPERIMENTAL. Dibuja la insignia de descarga sobre las carátulas sin modificar el tema:
    /// busca los elementos de la lista de juegos de la ventana de Playnite y les añade un adorno.
    /// Depende de cómo construya cada tema su cuadrícula, por eso va desactivado por defecto.
    /// </summary>
    internal sealed class CoverBadgeInjector
    {
        private readonly UpdateService service;
        private readonly Func<Window> getWindow;
        private readonly DispatcherTimer timer;
        private readonly List<KeyValuePair<WeakReference<UIElement>, BadgeAdorner>> attached =
            new List<KeyValuePair<WeakReference<UIElement>, BadgeAdorner>>();
        private static readonly ConcurrentDictionary<Type, PropertyInfo> gameProps = new ConcurrentDictionary<Type, PropertyInfo>();
        private bool loggedFirstPass;

        public CoverBadgeInjector(UpdateService service, Func<Window> getWindow, Dispatcher dispatcher)
        {
            this.service = service;
            this.getWindow = getWindow;
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, __) => Pass();
        }

        public void Start()
        {
            timer.Start();
            Log.Info("Insignias en carátulas (experimental) activadas.");
        }

        public void Stop()
        {
            timer.Stop();
            foreach (var pair in attached)
            {
                if (pair.Key.TryGetTarget(out var element))
                {
                    AdornerLayer.GetAdornerLayer(element)?.Remove(pair.Value);
                }
            }

            attached.Clear();
        }

        /// <summary>Revisión inmediata (tras un escaneo).</summary>
        public void Refresh()
        {
            if (timer.IsEnabled)
            {
                Pass();
            }
        }

        private void Pass()
        {
            try
            {
                var window = getWindow();
                if (window == null || !window.IsVisible || window.WindowState == WindowState.Minimized)
                {
                    return;
                }

                attached.RemoveAll(p => !p.Key.TryGetTarget(out _));

                int items = 0, badges = 0;
                foreach (var item in FindItems(window))
                {
                    items++;
                    var gameId = ResolveGameId(item.DataContext);
                    var show = gameId.HasValue && service.HasPendingUpdate(gameId.Value);

                    var existing = attached.Find(p => p.Key.TryGetTarget(out var el) && ReferenceEquals(el, item));
                    if (existing.Value != null)
                    {
                        existing.Value.SetVisible(show);
                    }
                    else if (show)
                    {
                        var layer = AdornerLayer.GetAdornerLayer(item);
                        if (layer != null)
                        {
                            var adorner = new BadgeAdorner(item);
                            layer.Add(adorner);
                            attached.Add(new KeyValuePair<WeakReference<UIElement>, BadgeAdorner>(new WeakReference<UIElement>(item), adorner));
                        }
                    }

                    if (show)
                    {
                        badges++;
                    }
                }

                if (!loggedFirstPass)
                {
                    loggedFirstPass = true;
                    Log.Debug($"Insignias: {items} elementos de juego encontrados en la ventana, {badges} con insignia.");
                }
            }
            catch (Exception e)
            {
                Log.Warn("Insignias: error en la pasada.", e);
            }
        }

        private static IEnumerable<ListBoxItem> FindItems(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current is ListBoxItem item)
                {
                    if (item.IsVisible)
                    {
                        yield return item;
                    }

                    // No hace falta bajar dentro de un elemento de la lista
                    continue;
                }

                int count;
                try
                {
                    count = VisualTreeHelper.GetChildrenCount(current);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    stack.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        /// <summary>
        /// El DataContext de cada elemento es un Game o un envoltorio de Playnite con propiedad Game.
        /// </summary>
        internal static Guid? ResolveGameId(object context)
        {
            if (context == null)
            {
                return null;
            }

            if (context is Game game)
            {
                return game.Id;
            }

            var prop = gameProps.GetOrAdd(context.GetType(), t => t.GetProperty("Game", BindingFlags.Public | BindingFlags.Instance));
            if (prop != null && typeof(Game).IsAssignableFrom(prop.PropertyType))
            {
                return (prop.GetValue(context) as Game)?.Id;
            }

            return null;
        }

        private sealed class BadgeAdorner : Adorner
        {
            private bool visible = true;

            public BadgeAdorner(UIElement adorned) : base(adorned)
            {
                IsHitTestVisible = false;
            }

            public void SetVisible(bool value)
            {
                if (visible != value)
                {
                    visible = value;
                    InvalidateVisual();
                }
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (!visible)
                {
                    return;
                }

                var size = AdornedElement.RenderSize;
                const double d = 30;
                var center = new Point(size.Width - d / 2 - 10, d / 2 + 10);

                dc.PushOpacity(0.35);
                dc.DrawEllipse(Brushes.Black, null, new Point(center.X, center.Y + 2), d / 2 + 3, d / 2 + 3);
                dc.Pop();

                dc.DrawEllipse(Styles.AccentBrushDiagonal, new Pen(Brushes.White, 2), center, d / 2, d / 2);

                var text = new FormattedText(
                    Styles.GlyphDownload,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(Styles.Icons, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    14,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
            }
        }
    }
}
