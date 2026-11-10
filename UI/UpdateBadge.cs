using ConsoleUpdateCenter.Core;
using Playnite.SDK.Controls;
using Playnite.SDK.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Insignia de descarga para la carátula. El tema la coloca con:
    /// &lt;ContentControl x:Name="ConsoleUpdateCenter_UpdateBadge" /&gt;
    /// </summary>
    public class UpdateBadge : PluginUserControl
    {
        private readonly UpdateService service;
        private readonly Border pill;

        public UpdateBadge(UpdateService service)
        {
            this.service = service;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            Visibility = Visibility.Collapsed;

            pill = new Border
            {
                CornerRadius = new CornerRadius(17),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Margin = new Thickness(6),
                Child = Styles.AccentIcon(Styles.GlyphDownload, 30),
                Effect = Styles.SoftShadow(10, 0.55)
            };
            Content = pill;

            Loaded += (_, __) => { service.Changed += OnServiceChanged; UpdateVisibility(); };
            Unloaded += (_, __) => service.Changed -= OnServiceChanged;
        }

        public override void GameContextChanged(Game oldContext, Game newContext)
        {
            UpdateVisibility();
        }

        private void OnServiceChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateVisibility));
        }

        private void UpdateVisibility()
        {
            var game = GameContext;
            Visibility = game != null && service.HasPendingUpdate(game.Id) ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
