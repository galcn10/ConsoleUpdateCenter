using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>Una fila (acción o juego) o un icono de una fila de iconos.</summary>
    internal sealed class ScreenRow
    {
        public string Key;
        public string Text;
        public string Desc;
        public string Icon;
        public Color? Color;
        public string Right;
        public double? Progress;
        public bool Danger;
        public bool Enabled = true;

        /// <summary>Mostrar la descripción siempre (filas informativas que no se pueden seleccionar).</summary>
        public bool AlwaysDesc;

        public Action Execute;
    }

    /// <summary>Grupo con título: una lista de filas o una fila de iconos.</summary>
    internal sealed class ScreenGroup
    {
        public string Title;
        public bool Horizontal;
        public List<ScreenRow> Rows = new List<ScreenRow>();
    }

    /// <summary>Contenido del panel.</summary>
    internal sealed class ScreenPage
    {
        /// <summary>Miniatura en la cabecera (si no hay, la insignia del icono).</summary>
        public ImageSource Cover;
        public string BigIcon = "download";
        public Color? BigIconColor;
        public string Title;
        public List<KeyValuePair<string, Brush>> Chips = new List<KeyValuePair<string, Brush>>();
        public string Message;

        /// <summary>Lista única (pantallas sencillas). Si hay grupos, se usan los grupos.</summary>
        public List<ScreenRow> Rows = new List<ScreenRow>();
        public List<ScreenGroup> Groups = new List<ScreenGroup>();

        public List<KeyValuePair<string, string>> Hints = new List<KeyValuePair<string, string>>();
        public string FocusKey;

        /// <summary>Texto largo en lugar de la lista (p. ej. novedades del parche). Se desplaza con arriba/abajo.</summary>
        public string LongText;
        public string LongTextNote;

        /// <summary>Panel más ancho (para texto largo).</summary>
        public bool Wide;

        public Action OnBack;
        public Action OnX;
        public Action OnY;

        public void AddChip(string text, Brush brush = null) => Chips.Add(new KeyValuePair<string, Brush>(text, brush));
        public void AddHint(string button, string text) => Hints.Add(new KeyValuePair<string, string>(button, text));
    }

    /// <summary>
    /// Panel lateral de cristal (estilo centro de control) con Playnite oscurecido detrás.
    /// Colores propios del plugin (verde agua → azul) para diferenciarse de Console Overlay;
    /// filas, iconos y tipografía de la misma familia. Se maneja con mando (lo reenvía el plugin) y con teclado.
    /// </summary>
    internal class ConsoleScreen : Window
    {
        private static readonly List<ConsoleScreen> open = new List<ConsoleScreen>();

        /// <summary>Pantalla abierta más reciente (la que recibe el mando).</summary>
        public static ConsoleScreen Current
        {
            get
            {
                lock (open)
                {
                    return open.LastOrDefault();
                }
            }
        }

        // Colores del logo
        public static readonly Color Brand1 = Color.FromRgb(0x2E, 0xD4, 0xAA);
        public static readonly Color Brand2 = Color.FromRgb(0x3A, 0x86, 0xFF);
        private static readonly Brush SelBrandBrush = MakeSel();

        private static Brush MakeSel()
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x7A, 0x68), 0));
            g.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x3A, 0x6A), 1));
            g.Freeze();
            return g;
        }

        private sealed class RowView
        {
            public ScreenRow Row;
            public Border Box;
            public Border Badge;
            public FrameworkElement Icon;
            public TextBlock Label;
            public TextBlock Desc;
            public TextBlock Right;
            public Border Bar;
            public Border Fill;
            public int Group;
            public bool Horizontal;
            public TextBlock Caption;
        }

        protected Look Look { get; private set; }

        private const double PanelWidth = 780;
        private const double WidePanelWidth = 1120;
        private const int RowHeight = 80;
        private const int RowSpacing = 12;

        private readonly Grid root;
        private readonly Border panel;
        private readonly Grid panelHost;
        private readonly TranslateTransform slide = new TranslateTransform();
        private Border dim;
        private Grid glow;
        private bool closingAnimated;
        private bool firstPage = true;
        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private TextBlock clockText;
        private ScrollViewer scroller;
        private StackPanel content;
        private readonly List<RowView> views = new List<RowView>();
        private readonly Dictionary<int, int> lastInGroup = new Dictionary<int, int>();
        private ScreenPage page;
        private int selected = -1;
        private string lastAction;
        private DateTime lastActionAt;

        public string Result { get; set; }

        public ConsoleScreen(Look look)
        {
            Look = look;
            if (look.Font != null)
            {
                FontFamily = look.Font;
            }

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            root = new Grid { Width = 1920, Height = 1080, ClipToBounds = true };

            // Playnite oscurecido detrás; un clic fuera del panel vuelve atrás
            dim = new Border { Background = new SolidColorBrush(Color.FromArgb(0x9E, 0x05, 0x07, 0x0C)) };
            dim.MouseLeftButtonUp += (_, __) => Back();
            root.Children.Add(dim);

            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(0xEE, 0x0E, 0x20, 0x2A), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(0xF5, 0x0A, 0x10, 0x1A), 1));
            bg.Freeze();

            var glowTop = new RadialGradientBrush { Center = new Point(0.85, 0.08), GradientOrigin = new Point(0.85, 0.08), RadiusX = 0.8, RadiusY = 0.4 };
            glowTop.GradientStops.Add(new GradientStop(Color.FromArgb(0x5A, Brand1.R, Brand1.G, Brand1.B), 0));
            glowTop.GradientStops.Add(new GradientStop(Color.FromArgb(0, Brand1.R, Brand1.G, Brand1.B), 1));
            glowTop.Freeze();
            var glowBottom = new RadialGradientBrush { Center = new Point(1, 1), GradientOrigin = new Point(1, 1), RadiusX = 0.75, RadiusY = 0.45 };
            glowBottom.GradientStops.Add(new GradientStop(Color.FromArgb(0x4D, Brand2.R, Brand2.G, Brand2.B), 0));
            glowBottom.GradientStops.Add(new GradientStop(Color.FromArgb(0, Brand2.R, Brand2.G, Brand2.B), 1));
            glowBottom.Freeze();

            panelHost = new Grid();
            var layers = new Grid();
            glow = new Grid { IsHitTestVisible = false };
            glow.Children.Add(new Border { Background = glowTop });
            glow.Children.Add(new Border { Background = glowBottom });
            layers.Children.Add(glow);
            layers.Children.Add(panelHost);

            panel = new Border
            {
                Width = PanelWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = bg,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Effect = new DropShadowEffect { BlurRadius = 80, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
                RenderTransform = slide,
                Child = layers
            };
            root.Children.Add(panel);

            Content = new Viewbox { Stretch = Stretch.Uniform, Child = root };

            // Estado inicial de la animación de entrada
            dim.Opacity = 0;
            panel.Opacity = 0;
            glow.Opacity = 0;
            slide.X = PanelWidth;
            Loaded += (_, __) =>
            {
                lock (open)
                {
                    open.Add(this);
                }

                BlockPlaynite(true);
                Activate();
                Focus();
                Keyboard.Focus(this);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Activate();
                    Keyboard.Focus(this);
                }), DispatcherPriority.Input);
                AnimateOpen();
            };

            Closed += (_, __) =>
            {
                clock.Stop();
                BlockPlaynite(false);
                lock (open)
                {
                    open.Remove(this);
                }
            };

            // Si el foco se escapa a otra ventana de Playnite, se recupera
            Deactivated += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && Application.Current?.Windows.OfType<Window>().Any(w => w.IsActive && !(w is ConsoleScreen)) == true)
                {
                    Activate();
                    Keyboard.Focus(this);
                }
            }), DispatcherPriority.Background);

            PreviewKeyDown += OnKey;
            clock.Tick += (_, __) =>
            {
                if (clockText != null)
                {
                    clockText.Text = DateTime.Now.ToShortTimeString();
                }

                OnTick();
            };
            clock.Start();
        }

        #region Animaciones

        private static readonly IEasingFunction EaseOutExpo = Freeze(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 });
        private static readonly IEasingFunction EaseInExpo = Freeze(new ExponentialEase { EasingMode = EasingMode.EaseIn, Exponent = 4 });
        private static readonly IEasingFunction EaseOutBack = Freeze(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });

        private static IEasingFunction Freeze(EasingFunctionBase e)
        {
            e.Freeze();
            return e;
        }

        private static DoubleAnimation Anim(double? from, double to, int ms, int delay = 0, IEasingFunction ease = null)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(ms),
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = ease ?? EaseOutExpo
            };
            if (from.HasValue)
            {
                a.From = from.Value;
            }

            return a;
        }

        /// <summary>Entrada: se oscurece Playnite, el panel llega desde el borde y se enciende el brillo de color.</summary>
        private void AnimateOpen()
        {
            dim.BeginAnimation(OpacityProperty, Anim(0, 1, 260, 0, EaseOutExpo));
            panel.BeginAnimation(OpacityProperty, Anim(0, 1, 160));
            slide.BeginAnimation(TranslateTransform.XProperty, Anim(panel.Width, 0, 460));
            glow.BeginAnimation(OpacityProperty, Anim(0, 1, 1100, 180, new SineEase { EasingMode = EasingMode.EaseInOut }));
        }

        /// <summary>
        /// Los elementos de la página aparecen en cascada: cabecera, títulos, filas y cada icono de las filas de iconos.
        /// </summary>
        private void AnimateContentIn(Grid panelGrid, bool opening)
        {
            var items = new List<UIElement>();
            foreach (UIElement child in panelGrid.Children)
            {
                if (child is ScrollViewer sv && sv.Content is StackPanel sp)
                {
                    foreach (UIElement c in sp.Children)
                    {
                        if (c is WrapPanel wp)
                        {
                            items.AddRange(wp.Children.Cast<UIElement>());
                        }
                        else
                        {
                            items.Add(c);
                        }
                    }
                }
                else
                {
                    items.Add(child);
                }
            }

            var start = opening ? 140 : 0;
            var step = Math.Max(14, Math.Min(32, 520 / Math.Max(1, items.Count)));
            for (int i = 0; i < items.Count; i++)
            {
                var el = items[i];
                var delay = start + i * step;
                var isTile = el is Border b && b.Width > 0 && b.Width < 200;
                GetTransforms(el, out var scale, out var move);

                el.Opacity = 0;
                el.BeginAnimation(OpacityProperty, Anim(0, 1, 280, delay));
                move.BeginAnimation(TranslateTransform.YProperty, Anim(isTile ? 14 : 22, 0, 420, delay));
                if (isTile)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.82, 1, 480, delay, EaseOutBack));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.82, 1, 480, delay, EaseOutBack));
                }
            }

            // La insignia o miniatura de la cabecera entra con un pequeño rebote
            if (panelGrid.Children.Count > 0 && panelGrid.Children[0] is DockPanel header && header.Children.Count > 0)
            {
                var thumb = header.Children[0];
                GetTransforms(thumb, out var ts, out _);
                ts.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.55, 1, 560, start + 40, EaseOutBack));
                ts.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.55, 1, 560, start + 40, EaseOutBack));
            }
        }

        /// <summary>Escala y desplazamiento propios del elemento (centrados), creados si no existen.</summary>
        private static void GetTransforms(UIElement el, out ScaleTransform scale, out TranslateTransform move)
        {
            if (el.RenderTransform is TransformGroup tg && tg.Children.Count == 2 && tg.Children[0] is ScaleTransform s0 && tg.Children[1] is TranslateTransform t0)
            {
                scale = s0;
                move = t0;
                return;
            }

            scale = new ScaleTransform(1, 1);
            move = new TranslateTransform();
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = new TransformGroup { Children = { scale, move } };
        }

        /// <summary>Al cambiar de fila, la nueva hace un pequeño "pop".</summary>
        private void Pop(int index)
        {
            if (index < 0 || index >= views.Count)
            {
                return;
            }

            GetTransforms(views[index].Box, out var scale, out _);
            var from = views[index].Horizontal ? 0.9 : 0.97;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(from, 1, 260, 0, EaseOutBack));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(from, 1, 260, 0, EaseOutBack));
        }

        /// <summary>Salida: el panel se va por donde vino y Playnite se aclara.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!closingAnimated && IsLoaded)
            {
                e.Cancel = true;
                closingAnimated = true;

                var outAnim = Anim(null, panel.Width + 40, 240, 0, EaseInExpo);
                outAnim.Completed += (_, __) => Dispatcher.BeginInvoke(new Action(Close));
                slide.BeginAnimation(TranslateTransform.XProperty, outAnim);
                dim.BeginAnimation(OpacityProperty, Anim(null, 0, 240, 0, EaseInExpo));
                panel.BeginAnimation(OpacityProperty, Anim(null, 0, 200, 60, EaseInExpo));

                // Seguridad: si la animación no termina, se cierra igualmente
                var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                guard.Tick += (_, __) =>
                {
                    guard.Stop();
                    if (IsVisible)
                    {
                        Close();
                    }
                };
                guard.Start();
                return;
            }

            base.OnClosing(e);
        }

        #endregion

        private Window blocked;

        /// <summary>
        /// Playnite convierte cada botón del mando en una tecla y la envía a la ventana con el foco del teclado.
        /// Mientras el panel está abierto, la ventana principal de Playnite no recibe teclas: la biblioteca no se mueve detrás.
        /// </summary>
        private void BlockPlaynite(bool on)
        {
            try
            {
                if (on)
                {
                    var main = Application.Current?.MainWindow;
                    if (main == null || main == this || main is ConsoleScreen)
                    {
                        return;
                    }

                    blocked = main;
                    blocked.PreviewKeyDown += Swallow;
                    blocked.PreviewKeyUp += Swallow;
                    blocked.PreviewTextInput += SwallowText;
                }
                else if (blocked != null)
                {
                    blocked.PreviewKeyDown -= Swallow;
                    blocked.PreviewKeyUp -= Swallow;
                    blocked.PreviewTextInput -= SwallowText;
                    blocked = null;
                }
            }
            catch (Exception e)
            {
                Log.Warn("No se pudo bloquear la entrada de Playnite.", e);
            }
        }

        private void Swallow(object sender, KeyEventArgs e)
        {
            if (IsVisible)
            {
                e.Handled = true;
            }
        }

        private void SwallowText(object sender, TextCompositionEventArgs e)
        {
            if (IsVisible)
            {
                e.Handled = true;
            }
        }

        /// <summary>Cada segundo (para refrescos de las subclases).</summary>
        protected virtual void OnTick()
        {
        }

        #region Construcción

        /// <summary>Muestra una página. Mantiene el foco en la fila con la misma clave si existe.</summary>
        public void SetPage(ScreenPage newPage)
        {
            var keepKey = selected >= 0 && selected < views.Count ? views[selected].Row.Key : null;
            var keepOffset = scroller?.VerticalOffset ?? 0;
            var samePage = page != null && newPage != null && page.Title == newPage.Title && page.LongText == null && newPage.LongText == null;

            page = newPage;
            if (page.Groups.Count == 0 && page.Rows.Count > 0)
            {
                page.Groups.Add(new ScreenGroup { Rows = page.Rows });
            }

            var width = page.Wide ? WidePanelWidth : PanelWidth;
            if (Math.Abs(panel.Width - width) > 1)
            {
                panel.Width = width;
            }

            views.Clear();
            if (!samePage)
            {
                lastInGroup.Clear();
            }

            panelHost.Children.Clear();
            var built = BuildPanel();
            panelHost.Children.Add(built);
            if (!samePage && built is Grid builtGrid)
            {
                AnimateContentIn(builtGrid, firstPage);
            }

            firstPage = false;

            var key = samePage ? keepKey : (page.FocusKey ?? keepKey);
            var idx = views.FindIndex(v => v.Row.Enabled && v.Row.Key == key);
            if (idx < 0 && page.FocusKey != null)
            {
                idx = views.FindIndex(v => v.Row.Enabled && v.Row.Key == page.FocusKey);
            }

            if (idx < 0)
            {
                idx = views.FindIndex(v => v.Row.Enabled);
            }

            selected = idx;
            Paint();

            if (samePage && scroller != null)
            {
                scroller.ScrollToVerticalOffset(keepOffset);
            }

            Dispatcher.BeginInvoke(new Action(() => EnsureVisible(selected)), DispatcherPriority.Loaded);
        }

        private FrameworkElement BuildPanel()
        {
            var grid = new Grid { Margin = new Thickness(60, 52, 60, 40) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Cabecera: miniatura o insignia, título y hora
            var header = new DockPanel { LastChildFill = true };
            FrameworkElement thumb;
            if (page.Cover != null)
            {
                thumb = new Border
                {
                    Width = 56,
                    Height = 56,
                    CornerRadius = new CornerRadius(14),
                    Background = new ImageBrush(page.Cover) { Stretch = Stretch.UniformToFill },
                    Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.5, Color = Colors.Black }
                };
            }
            else
            {
                thumb = Look.Badge(page.BigIcon, page.BigIconColor ?? Brand1, 52);
            }

            thumb.VerticalAlignment = VerticalAlignment.Center;
            thumb.Margin = new Thickness(0, 0, 18, 0);
            DockPanel.SetDock(thumb, Dock.Left);
            header.Children.Add(thumb);

            clockText = new TextBlock
            {
                Text = DateTime.Now.ToShortTimeString(),
                FontSize = 28,
                Foreground = Look.TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0)
            };
            DockPanel.SetDock(clockText, Dock.Right);
            header.Children.Add(clockText);

            header.Children.Add(new TextBlock
            {
                Text = page.Title ?? string.Empty,
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            grid.Children.Add(header);

            // Datos y mensaje
            var info = new StackPanel { Margin = new Thickness(0, 16, 0, 12) };
            if (page.Chips.Count > 0)
            {
                var chips = new WrapPanel();
                foreach (var c in page.Chips)
                {
                    var chip = Look.Chip(c.Key, c.Value);
                    if (chip.Child is TextBlock tb)
                    {
                        tb.FontSize = 18;
                    }

                    chip.Padding = new Thickness(14, 5, 14, 6);
                    chip.Margin = new Thickness(0, 0, 8, 8);
                    chips.Children.Add(chip);
                }

                info.Children.Add(chips);
            }

            if (!string.IsNullOrEmpty(page.Message))
            {
                var msg = new TextBlock
                {
                    Text = Look.Pad(page.Message),
                    FontSize = 19,
                    Foreground = Look.MutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 8)
                };

                // Junto a las pastillas si es corto (p. ej. "Último escaneo 18:57")
                if (info.Children.Count > 0 && info.Children[0] is WrapPanel chipsPanel && page.Message.Length <= 32)
                {
                    chipsPanel.Children.Add(msg);
                }
                else
                {
                    info.Children.Add(msg);
                }
            }

            Grid.SetRow(info, 1);
            grid.Children.Add(info);

            // Cuerpo
            FrameworkElement body = page.LongText != null ? BuildLongText() : BuildGroups();
            Grid.SetRow(body, 2);
            grid.Children.Add(body);

            // Ayudas
            var hints = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            foreach (var h in page.Hints)
            {
                var hint = Look.Hint(Look.Button(h.Key), h.Value);
                hint.Margin = new Thickness(0, 0, 28, 0);
                hints.Children.Add(hint);
            }

            Grid.SetRow(hints, 3);
            grid.Children.Add(hints);
            return grid;
        }

        private FrameworkElement BuildGroups()
        {
            content = new StackPanel();
            for (int g = 0; g < page.Groups.Count; g++)
            {
                var group = page.Groups[g];
                if (!string.IsNullOrEmpty(group.Title))
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = group.Title.ToUpperInvariant(),
                        FontSize = 17,
                        FontWeight = FontWeights.Bold,
                        Foreground = Look.MutedBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Margin = new Thickness(8, g == 0 ? 0 : 8, 0, 12)
                    });
                }

                if (group.Horizontal)
                {
                    var bar = new WrapPanel();
                    var caption = new TextBlock
                    {
                        FontSize = 17,
                        Foreground = Look.MutedBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Height = 26,
                        Margin = new Thickness(6, 2, 0, 8)
                    };
                    foreach (var row in group.Rows)
                    {
                        bar.Children.Add(BuildTile(row, g, caption));
                    }

                    content.Children.Add(bar);
                    content.Children.Add(caption);
                }
                else
                {
                    foreach (var row in group.Rows)
                    {
                        content.Children.Add(BuildRow(row, g));
                    }
                }
            }

            scroller = new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false
            };
            return scroller;
        }

        private FrameworkElement BuildLongText()
        {
            content = null;
            var stack = new StackPanel();
            if (!string.IsNullOrEmpty(page.LongTextNote))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = page.LongTextNote,
                    FontSize = 18,
                    Foreground = Look.MutedBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16)
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = page.LongText,
                FontSize = 22,
                LineHeight = 32,
                Foreground = Look.TextBrush,
                TextWrapping = TextWrapping.Wrap
            });

            scroller = new ScrollViewer
            {
                Content = new Border
                {
                    Background = Look.RowBrush,
                    BorderBrush = Look.GlassEdge,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(32, 26, 32, 26),
                    Child = stack
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Focusable = false
            };
            return scroller;
        }

        private FrameworkElement BuildRow(ScreenRow row, int group)
        {
            var color = row.Color ?? Look.ActionColor(row.Icon);
            var box = new Border
            {
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(14, 6, 22, 6),
                Margin = new Thickness(0, 0, 0, RowSpacing),
                BorderThickness = new Thickness(1),
                BorderBrush = Look.GlassEdge,
                Height = RowHeight,
                Cursor = Cursors.Hand,
                Opacity = row.Enabled || row.AlwaysDesc ? 1.0 : 0.5
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = Look.Icon(row.Icon, Brushes.White, 30);
            var badge = new Border
            {
                Width = 54,
                Height = 54,
                CornerRadius = new CornerRadius(16),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = color.HasValue ? Look.BadgeGradient(color.Value) : Look.BadgeBrush,
                Child = icon
            };

            var label = new TextBlock
            {
                Text = row.Text ?? string.Empty,
                FontSize = 25,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            var desc = new TextBlock
            {
                Text = Look.Pad(row.Desc ?? string.Empty),
                FontSize = 17,
                Visibility = row.AlwaysDesc ? Visibility.Visible : Visibility.Collapsed,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            };

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(label);
            texts.Children.Add(desc);

            Border fill = null;
            if (row.Progress.HasValue)
            {
                var p = Math.Max(0, Math.Min(1, row.Progress.Value));
                var bar = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(p, 0.0001), GridUnitType.Star) });
                bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - p, 0.0001), GridUnitType.Star) });
                var track = new Border { CornerRadius = new CornerRadius(3), Background = Look.TrackBrush };
                Grid.SetColumnSpan(track, 2);
                fill = new Border { CornerRadius = new CornerRadius(3), Background = Brushes.White };
                bar.Children.Add(track);
                bar.Children.Add(fill);
                texts.Children.Add(bar);
            }

            TextBlock right = null;
            if (!string.IsNullOrEmpty(row.Right))
            {
                right = new TextBlock
                {
                    Text = row.Right,
                    FontSize = 22,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 4, 0)
                };
                Grid.SetColumn(right, 2);
                g.Children.Add(right);
            }

            Grid.SetColumn(texts, 1);
            g.Children.Add(badge);
            g.Children.Add(texts);

            var accentBar = new Border
            {
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(6, 0, 0, 0),
                BorderBrush = Look.BarWhite,
                Margin = new Thickness(-15, -7, -23, -7),
                Opacity = 0,
                IsHitTestVisible = false
            };

            var wrap = new Grid();
            wrap.Children.Add(g);
            wrap.Children.Add(accentBar);
            box.Child = wrap;

            AddView(new RowView
            {
                Row = row,
                Box = box,
                Badge = badge,
                Icon = icon,
                Label = label,
                Desc = string.IsNullOrEmpty(row.Desc) ? null : desc,
                Right = right,
                Bar = accentBar,
                Fill = fill,
                Group = group
            });
            return box;
        }

        private FrameworkElement BuildTile(ScreenRow row, int group, TextBlock caption)
        {
            var color = row.Color ?? Look.ActionColor(row.Icon);
            var icon = Look.Icon(row.Icon, Brushes.White, 28);
            var badge = new Border
            {
                Width = 46,
                Height = 46,
                CornerRadius = new CornerRadius(14),
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = color.HasValue ? Look.BadgeGradient(color.Value) : Look.BadgeBrush,
                Child = icon
            };

            var label = new TextBlock
            {
                Text = row.Text ?? string.Empty,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 20,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                MaxHeight = 40,
                Margin = new Thickness(0, 7, 0, 0)
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(badge);
            stack.Children.Add(label);

            var accentBar = new Border
            {
                CornerRadius = new CornerRadius(20),
                BorderThickness = new Thickness(0, 0, 0, 5),
                BorderBrush = Look.BarWhite,
                Margin = new Thickness(-9),
                Opacity = 0,
                IsHitTestVisible = false
            };

            var wrap = new Grid();
            wrap.Children.Add(stack);
            wrap.Children.Add(accentBar);

            var box = new Border
            {
                Width = 122,
                Height = 122,
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 9, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = Look.GlassEdge,
                Cursor = Cursors.Hand,
                Opacity = row.Enabled ? 1.0 : 0.5,
                Child = wrap
            };

            AddView(new RowView
            {
                Row = row,
                Box = box,
                Badge = badge,
                Icon = icon,
                Label = label,
                Bar = accentBar,
                Group = group,
                Horizontal = true,
                Caption = caption
            });
            return box;
        }

        private void AddView(RowView view)
        {
            var index = views.Count;
            views.Add(view);

            view.Box.MouseLeftButtonUp += (_, __) =>
            {
                if (!view.Row.Enabled)
                {
                    return;
                }

                Select(index);
                Execute(view);
            };
            view.Box.MouseEnter += (_, __) =>
            {
                if (view.Row.Enabled && selected != index)
                {
                    Select(index);
                }
            };
        }

        private void Paint()
        {
            foreach (var v in views.Where(x => x.Caption != null))
            {
                v.Caption.Text = string.Empty;
            }

            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                var sel = i == selected;
                var color = v.Row.Color ?? Look.ActionColor(v.Row.Icon);

                v.Box.Background = sel ? SelBrandBrush : Look.RowBrush;
                if (!color.HasValue)
                {
                    v.Badge.Background = Look.BadgeBrush;
                    SetIconStroke(v.Icon, v.Row.Danger ? Look.DangerBrush : Look.TextBrush);
                }

                v.Label.Foreground = sel ? Brushes.White : (v.Row.Danger ? Look.DangerBrush : Look.TextBrush);
                if (v.Desc != null)
                {
                    v.Desc.Visibility = sel || v.Row.AlwaysDesc ? Visibility.Visible : Visibility.Collapsed;
                    v.Desc.Foreground = v.Row.Danger ? Look.DangerBrush : (sel ? Look.SelDescBrush : Look.MutedBrush);
                }

                if (v.Right != null)
                {
                    v.Right.Foreground = sel ? Brushes.White : Look.MutedBrush;
                }

                v.Bar.Opacity = sel ? 1 : 0;

                // Bajo cada fila de iconos, la descripción del icono activo
                if (sel && v.Caption != null)
                {
                    v.Caption.Text = Look.Pad(v.Row.Desc ?? v.Row.Text ?? string.Empty);
                }
            }
        }

        private static void SetIconStroke(FrameworkElement icon, Brush brush)
        {
            if (icon is Viewbox vb && vb.Child is Canvas c && c.Children.Count > 0 && c.Children[0] is System.Windows.Shapes.Path p)
            {
                p.Stroke = brush;
            }
        }

        private void EnsureVisible(int index)
        {
            if (scroller == null || content == null || index < 0 || index >= views.Count)
            {
                return;
            }

            try
            {
                var box = views[index].Box;
                var top = box.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
                var bottom = top + box.ActualHeight + (views[index].Horizontal ? 40 : RowSpacing);
                var viewTop = scroller.VerticalOffset;
                var viewBottom = viewTop + scroller.ViewportHeight;

                // Que se vea también el título del grupo encima
                const int margin = 44;
                if (top - margin < viewTop)
                {
                    scroller.ScrollToVerticalOffset(Math.Max(0, top - margin));
                }
                else if (bottom > viewBottom)
                {
                    scroller.ScrollToVerticalOffset(bottom - scroller.ViewportHeight);
                }
            }
            catch
            {
            }
        }

        #endregion

        #region Navegación

        private void Select(int index)
        {
            if (index < 0 || index >= views.Count)
            {
                return;
            }

            var changed = selected != index;
            selected = index;
            if (views[index].Horizontal)
            {
                lastInGroup[views[index].Group] = index;
            }

            Paint();
            if (changed)
            {
                Pop(index);
            }

            EnsureVisible(index);
        }

        private List<int> InGroup(int group) =>
            Enumerable.Range(0, views.Count).Where(i => views[i].Group == group && views[i].Row.Enabled).ToList();

        private void MoveVertical(int delta)
        {
            if (page?.LongText != null)
            {
                scroller?.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + delta * 120));
                return;
            }

            var cur = selected >= 0 && selected < views.Count ? views[selected] : null;
            if (cur == null)
            {
                return;
            }

            // Dentro de una lista
            if (!cur.Horizontal)
            {
                var items = InGroup(cur.Group);
                var p = items.IndexOf(selected) + delta;
                if (p >= 0 && p < items.Count)
                {
                    Select(items[p]);
                    return;
                }
            }

            // Al grupo vecino
            for (var g = cur.Group + delta; g >= 0 && g < page.Groups.Count; g += delta)
            {
                var items = InGroup(g);
                if (items.Count == 0)
                {
                    continue;
                }

                int target;
                if (page.Groups[g].Horizontal)
                {
                    target = lastInGroup.TryGetValue(g, out var last) && items.Contains(last) ? last : items[0];
                }
                else
                {
                    target = delta > 0 ? items[0] : items[items.Count - 1];
                }

                Select(target);
                return;
            }

            if (delta < 0)
            {
                scroller?.ScrollToTop();
            }
        }

        private void MoveHorizontal(int delta)
        {
            var cur = selected >= 0 && selected < views.Count ? views[selected] : null;
            if (cur == null || !cur.Horizontal)
            {
                return;
            }

            var items = InGroup(cur.Group);
            var p = items.IndexOf(selected) + delta;
            if (p >= 0 && p < items.Count)
            {
                Select(items[p]);
            }
        }

        private void Execute(RowView v)
        {
            if (v?.Row.Execute == null || !v.Row.Enabled)
            {
                return;
            }

            Log.Debug("Pantalla: seleccionado " + v.Row.Key);
            try
            {
                v.Row.Execute();
            }
            catch (Exception e)
            {
                Log.Error("Error ejecutando " + v.Row.Key, e);
            }
        }

        private void ExecuteSelected() => Execute(selected >= 0 && selected < views.Count ? views[selected] : null);

        private void Back()
        {
            if (page?.OnBack != null)
            {
                page.OnBack();
            }
            else
            {
                Close();
            }
        }

        /// <summary>Botón del mando (nombre de ControllerInput). Lo llama el plugin.</summary>
        public void HandleButton(string name)
        {
            switch (name)
            {
                case "DPadUp":
                case "LeftStickUp":
                    Do("up", () => MoveVertical(-1));
                    break;
                case "DPadDown":
                case "LeftStickDown":
                    Do("down", () => MoveVertical(1));
                    break;
                case "DPadLeft":
                case "LeftStickLeft":
                    Do("left", () => MoveHorizontal(-1));
                    break;
                case "DPadRight":
                case "LeftStickRight":
                    Do("right", () => MoveHorizontal(1));
                    break;
                case "A":
                    Do("ok", ExecuteSelected);
                    break;
                case "B":
                    Do("back", Back);
                    break;
                case "Guide":
                    Do("close", OnGuide);
                    break;
                case "X":
                    Do("x", () => page?.OnX?.Invoke());
                    break;
                case "Y":
                    Do("y", () => page?.OnY?.Invoke());
                    break;
            }
        }

        /// <summary>El botón Guía cierra la pantalla (las subclases pueden cambiarlo).</summary>
        protected virtual void OnGuide() => Close();

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Do("up", () => MoveVertical(-1));
                    break;
                case Key.Down:
                    Do("down", () => MoveVertical(1));
                    break;
                case Key.Left:
                    Do("left", () => MoveHorizontal(-1));
                    break;
                case Key.Right:
                    Do("right", () => MoveHorizontal(1));
                    break;
                case Key.Enter:
                case Key.Space:
                    Do("ok", ExecuteSelected);
                    break;
                case Key.Escape:
                case Key.Back:
                    Do("back", Back);
                    break;
                case Key.F5:
                    Do("x", () => page?.OnX?.Invoke());
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        /// <summary>
        /// Playnite puede entregar la misma pulsación como evento de mando y como tecla:
        /// se ignora la misma acción repetida en menos de 150 ms.
        /// </summary>
        private void Do(string action, Action run)
        {
            var now = DateTime.Now;
            if (action == lastAction && (now - lastActionAt).TotalMilliseconds < 150)
            {
                return;
            }

            lastAction = action;
            lastActionAt = now;
            run();
        }

        #endregion
    }
}
