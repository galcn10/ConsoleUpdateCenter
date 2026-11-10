using ConsoleUpdateCenter.Core;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Aviso flotante de cristal (mismo estilo que las filas de Console Overlay).
    /// Es una ventana propia: funciona con cualquier tema, no toma el foco y no recibe clics.
    /// </summary>
    internal sealed class SelectionToast : Window
    {
        private readonly Border card = new Border();
        private readonly Border badgeHost = new Border();
        private readonly TextBlock title = new TextBlock();
        private readonly TextBlock subtitle = new TextBlock();
        private readonly StackPanel hint = new StackPanel { Orientation = Orientation.Horizontal };
        private readonly Grid progressBar = new Grid();
        private readonly ColumnDefinition progressDone = new ColumnDefinition();
        private readonly ColumnDefinition progressLeft = new ColumnDefinition();
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly DispatcherTimer autoHide = new DispatcherTimer();
        private bool shown;

        /// <summary>Juego del aviso de selección a la vista (null si es otro tipo de aviso o está oculto).</summary>
        public Guid? ShownGameId { get; private set; }

        public SelectionToast()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Topmost = true;
            Opacity = 0;

            title.FontSize = 20;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Look.TextBrush;

            subtitle.FontSize = 16;
            subtitle.Margin = new Thickness(0, 3, 0, 0);
            subtitle.MaxWidth = 360;
            subtitle.TextTrimming = TextTrimming.CharacterEllipsis;

            progressBar.Height = 4;
            progressBar.Margin = new Thickness(0, 9, 0, 0);
            progressBar.ColumnDefinitions.Add(progressDone);
            progressBar.ColumnDefinitions.Add(progressLeft);
            var track = new Border { CornerRadius = new CornerRadius(2), Background = Look.TrackBrush };
            Grid.SetColumnSpan(track, 2);
            progressBar.Children.Add(track);
            progressBar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Brushes.White });

            hint.Margin = new Thickness(0, 10, 0, 0);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(title);
            texts.Children.Add(subtitle);
            texts.Children.Add(progressBar);
            texts.Children.Add(hint);

            badgeHost.Margin = new Thickness(0, 0, 16, 0);
            badgeHost.VerticalAlignment = VerticalAlignment.Top;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(badgeHost);
            row.Children.Add(texts);

            card.Margin = new Thickness(24);
            card.Padding = new Thickness(14, 12, 24, 14);
            card.CornerRadius = new CornerRadius(20);
            card.BorderBrush = Look.GlassEdge;
            card.BorderThickness = new Thickness(1);
            card.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black };
            card.RenderTransform = slide;
            card.Child = row;
            Content = card;

            autoHide.Tick += (_, __) => HideToast();

            SourceInitialized += (_, __) => Native.MakeOverlayWindow(new WindowInteropHelper(this).Handle);
            SizeChanged += (_, __) => Reposition();
        }

        private void ApplyLook(Look l)
        {
            if (l.Font != null)
            {
                FontFamily = l.Font;
            }

            // Cristal opaco: el aviso flota sobre cualquier tema y debe leerse bien
            var td = l.TintDark;
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, (byte)Math.Min(255, td.R + 28), (byte)Math.Min(255, td.G + 32), (byte)Math.Min(255, td.B + 44)), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, td.R, td.G, td.B), 1));
            bg.Freeze();
            card.Background = bg;
            subtitle.Foreground = l.MutedBrush;
        }

        /// <summary>Aviso de un juego seleccionado con actualización.</summary>
        public void ShowForGame(Look l, Guid gameId, string gameName, UpdateEntry entry, string hintKey, string hintText, int seconds)
        {
            var titleText = Format.StatusTitle(entry);
            if (entry.Status == UpdateStatus.Downloading && entry.Progress.HasValue)
            {
                titleText += " · " + Format.Percent(entry.Progress);
            }

            var color = entry.NoSpace ? Look.ActionColor("hard-drive") : Look.ActionColor("download");
            var sub = gameName;
            if (entry.NoSpace)
            {
                sub += " · " + Loc.T("sin espacio en {0}", Format.Drive(entry.LibraryFolder));
            }
            else if (entry.Status == UpdateStatus.Downloading)
            {
                var rem = Format.Remaining(entry.Remaining);
                if (rem != null)
                {
                    sub += " · " + rem;
                }
            }

            var p = entry.Status != UpdateStatus.Pending ? entry.Progress : null;
            ShowCore(l, Format.StatusIcon(entry), color, titleText, sub, p, hintKey, hintText, seconds);
            ShownGameId = gameId;
        }

        /// <summary>Aviso general (actualización completada, sin espacio, etc.).</summary>
        public void ShowMessage(Look l, string icon, string titleText, string sub, int seconds)
        {
            ShowCore(l, icon, Look.ActionColor(icon), titleText, sub, null, null, null, seconds);
            ShownGameId = null;
        }

        private void ShowCore(Look l, string icon, Color? color, string titleText, string sub, double? progress,
                              string hintKey, string hintText, int seconds)
        {
            ApplyLook(l);
            badgeHost.Child = l.Badge(icon, color, 48);
            title.Text = titleText;
            subtitle.Text = sub ?? string.Empty;
            subtitle.Visibility = string.IsNullOrEmpty(sub) ? Visibility.Collapsed : Visibility.Visible;

            progressBar.Visibility = progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (progress.HasValue)
            {
                var v = Math.Max(0, Math.Min(1, progress.Value));
                progressDone.Width = new GridLength(Math.Max(v, 0.0001), GridUnitType.Star);
                progressLeft.Width = new GridLength(Math.Max(1 - v, 0.0001), GridUnitType.Star);
            }

            hint.Children.Clear();
            if (!string.IsNullOrEmpty(hintText))
            {
                if (!string.IsNullOrEmpty(hintKey))
                {
                    hint.Children.Add(new Border
                    {
                        Background = l.PillBrush,
                        CornerRadius = new CornerRadius(12),
                        MinWidth = 26,
                        Height = 26,
                        Padding = new Thickness(9, 0, 9, 0),
                        Margin = new Thickness(0, 0, 9, 0),
                        Child = new TextBlock
                        {
                            Text = hintKey,
                            FontFamily = new FontFamily("Segoe UI Symbol"),
                            FontSize = 14,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    });
                }

                hint.Children.Add(new TextBlock
                {
                    Text = hintText,
                    FontSize = 14,
                    Foreground = l.MutedBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            hint.Visibility = hint.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (!IsVisible)
            {
                Show();
            }

            Reposition();

            if (!shown)
            {
                shown = true;
                Animate(1, 0, 220);
            }

            autoHide.Stop();
            if (seconds > 0)
            {
                autoHide.Interval = TimeSpan.FromSeconds(seconds);
                autoHide.Start();
            }
        }

        public bool IsShowing => shown;

        public void HideToast()
        {
            autoHide.Stop();
            ShownGameId = null;
            if (!shown)
            {
                return;
            }

            shown = false;
            Animate(0, 24, 180, () =>
            {
                if (!shown)
                {
                    Hide();
                }
            });
        }

        private void Animate(double opacity, double offsetX, int ms, Action done = null)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            if (done != null)
            {
                fade.Completed += (_, __) => done();
            }

            var move = new DoubleAnimation(offsetX, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            if (opacity > 0)
            {
                move.From = 24;
            }

            BeginAnimation(OpacityProperty, fade);
            slide.BeginAnimation(TranslateTransform.XProperty, move);
        }

        /// <summary>Esquina superior derecha de la ventana de Playnite (o de la pantalla).</summary>
        private void Reposition()
        {
            double left, top, width;
            var owner = Owner;
            if (owner == null || owner.WindowState == WindowState.Maximized || owner.ActualWidth <= 0)
            {
                var area = SystemParameters.WorkArea;
                left = area.Left;
                top = area.Top;
                width = area.Width;
            }
            else
            {
                left = owner.Left;
                top = owner.Top;
                width = owner.ActualWidth;
            }

            Left = left + width - ActualWidth - 24;
            Top = top + 36;
        }
    }
}
