using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Aspecto común con Console Overlay: mismos colores, cristal, insignias, iconos y ayudas de botones.
    /// Cada ventana crea uno con el color de acento elegido (tema de Playnite, carátula o neutro).
    /// </summary>
    internal sealed class Look
    {
        public static Brush Make(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static readonly Color BaseDark = (Color)ColorConverter.ConvertFromString("#0D0F14");
        public static readonly Brush TextBrush = Make("#F2F4F8");
        public static readonly Brush SelTextBrush = Make("#0D0F14");
        public static readonly Brush DangerBrush = Make("#FF8A80");
        public static readonly Brush OkBrush = Make("#7DDC9A");
        public static readonly Brush MidBrush = Make("#FFD479");
        public static readonly Brush SelDescBrush = Make("#E9EEFB");
        public static readonly Brush BarWhite = Make("#F2FFFFFF");
        public static readonly Brush TrackBrush = Make("#38FFFFFF");
        public static readonly Brush GlassEdge = MakeEdge();

        public Color? Accent { get; }
        public Color TintDark { get; }
        public Brush BackBrush { get; }
        public Brush RowBrush { get; }
        public Brush SelBrush { get; }
        public Brush MutedBrush { get; }
        public Brush PillBrush { get; }
        public Brush BadgeBrush { get; }
        public Brush BadgeSelBrush { get; }
        public string ButtonStyle { get; }
        public FontFamily Font { get; }

        public Look(Color? accent, string buttonStyle, FontFamily font)
        {
            Accent = accent;
            ButtonStyle = buttonStyle ?? "Xbox";
            Font = font;

            var a = accent ?? Color.FromRgb(0x8A, 0x93, 0xB0);
            double k = accent == null ? 0 : 1;
            TintDark = Palette.Mix(BaseDark, a, 0.11 * k);
            BackBrush = Frozen(TintDark);

            // Cristal: degradado vertical (más claro arriba) teñido con el acento.
            var gTop = Palette.Mix(Colors.White, a, 0.55 * k);
            var gMid = Palette.Mix(Color.FromRgb(0x12, 0x15, 0x1D), a, 0.30 * k);
            var gBot = Palette.Mix(Color.FromRgb(0x12, 0x15, 0x1D), a, 0.45 * k);
            var glass = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(70, gTop.R, gTop.G, gTop.B), 0.0));
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(120, gMid.R, gMid.G, gMid.B), 0.55));
            glass.GradientStops.Add(new GradientStop(Color.FromArgb(140, gBot.R, gBot.G, gBot.B), 1.0));
            glass.Freeze();
            RowBrush = glass;

            MutedBrush = Frozen(Palette.Mix(Color.FromRgb(0xC2, 0xC8, 0xD8), a, 0.20 * k));
            PillBrush = Frozen(Palette.Mix(Color.FromRgb(0x23, 0x28, 0x38), a, 0.22 * k));
            BadgeBrush = Frozen(Palette.Mix(Color.FromRgb(0x26, 0x2C, 0x3D), a, 0.34 * k));
            BadgeSelBrush = Frozen(Palette.Mix(Color.FromRgb(0xE3, 0xE6, 0xF0), a, 0.12 * k));
            SelBrush = Frozen(Palette.Mix(Color.FromRgb(0xF7, 0xF8, 0xFB), a, 0.03 * k));
        }

        private static Brush MakeEdge()
        {
            var e = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            e.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
            e.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5));
            e.GradientStops.Add(new GradientStop(Color.FromArgb(18, 255, 255, 255), 1.0));
            e.Freeze();
            return e;
        }

        #region Degradados de color (como Console Overlay)

        private static readonly Dictionary<Color, Brush> badgeGrad = new Dictionary<Color, Brush>();
        private static readonly Dictionary<Color, Brush> selGrad = new Dictionary<Color, Brush>();

        /// <summary>Recuadro del icono: claro arriba, color en medio y más oscuro abajo.</summary>
        public static Brush BadgeGradient(Color c)
        {
            lock (badgeGrad)
            {
                if (!badgeGrad.TryGetValue(c, out var r))
                {
                    var g = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
                    g.GradientStops.Add(new GradientStop(Palette.Mix(c, Colors.White, 0.32), 0.0));
                    g.GradientStops.Add(new GradientStop(c, 0.55));
                    g.GradientStops.Add(new GradientStop(Palette.Mix(c, Colors.Black, 0.22), 1.0));
                    g.Freeze();
                    r = g;
                    badgeGrad[c] = r;
                }

                return r;
            }
        }

        /// <summary>Fila seleccionada con color: degradado del color de la acción, lo bastante oscuro para texto blanco.</summary>
        public static Brush SelGradient(Color c)
        {
            lock (selGrad)
            {
                if (!selGrad.TryGetValue(c, out var r))
                {
                    var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                    g.GradientStops.Add(new GradientStop(Palette.Mix(c, Colors.Black, 0.28), 0.0));
                    g.GradientStops.Add(new GradientStop(Palette.Mix(c, Color.FromRgb(0x14, 0x17, 0x22), 0.62), 1.0));
                    g.Freeze();
                    r = g;
                    selGrad[c] = r;
                }

                return r;
            }
        }

        public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        /// <summary>Color de cada tipo de acción (misma familia que Console Overlay).</summary>
        public static Color? ActionColor(string icon)
        {
            switch (icon)
            {
                case "download": return Hex("#1FB5A0");
                case "refresh-cw": return Hex("#F59E0B");
                case "shield-check": return Hex("#6366F1");
                case "external-link":
                case "store": return Hex("#64748B");
                case "file-text": return Hex("#3F8CFF");
                case "wrench": return Hex("#EAB308");
                case "power": return Hex("#EC4899");
                case "moon": return Hex("#6366F1");
                case "play": return Hex("#3F8CFF");
                case "x":
                case "circle-x": return Hex("#EF4444");
                case "check":
                case "circle-check": return Hex("#22C55E");
                case "triangle-alert": return Hex("#F97316");
                case "clock": return Hex("#8B5CF6");
                case "hard-drive": return Hex("#F97316");
                case "pause": return Hex("#64748B");
                default: return null;
            }
        }

        public static Color PlatformColor(UpdatePlatform p)
        {
            switch (p)
            {
                case UpdatePlatform.Steam: return Hex("#3F8CFF");
                case UpdatePlatform.Epic: return Hex("#64748B");
                case UpdatePlatform.Gog: return Hex("#8B5CF6");
                default: return Hex("#22C55E");
            }
        }

        #endregion

        #region Piezas

        public static Shapes.Path MakeIcon(string key, Brush stroke)
        {
            var p = new Shapes.Path
            {
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                Stroke = stroke
            };

            if (key != null && IconData.Paths.TryGetValue(key, out var data))
            {
                try
                {
                    p.Data = Geometry.Parse(data);
                }
                catch (Exception e)
                {
                    Log.Warn("Icono no válido: " + key, e);
                }
            }

            return p;
        }

        /// <summary>Icono Lucide dentro de una caja del tamaño indicado.</summary>
        public static FrameworkElement Icon(string key, Brush stroke, double size)
        {
            return new Viewbox
            {
                Width = size,
                Height = size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Canvas { Width = 24, Height = 24, Children = { MakeIcon(key, stroke) } }
            };
        }

        /// <summary>Insignia cuadrada redondeada con degradado de color e icono blanco.</summary>
        public Border Badge(string icon, Color? color, double size = 56)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size * 16 / 56),
                Background = color.HasValue ? BadgeGradient(color.Value) : BadgeBrush,
                Child = Icon(icon, Brushes.White, size * 32 / 56)
            };
        }

        /// <summary>Etiqueta redondeada para datos cortos.</summary>
        public Border Chip(string text, Brush foreground = null)
        {
            return new Border
            {
                Background = PillBrush,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 6, 16, 7),
                Margin = new Thickness(0, 0, 10, 10),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 20,
                    Foreground = foreground ?? TextBrush,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        /// <summary>Ayuda de botón del pie: pastilla con el botón y el texto.</summary>
        public FrameworkElement Hint(string key, string text)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 36, 0) };
            p.Children.Add(new Border
            {
                Background = PillBrush,
                CornerRadius = new CornerRadius(18),
                MinWidth = 36,
                Height = 36,
                Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = key,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = 20,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            p.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 22,
                Foreground = MutedBrush,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return p;
        }

        #endregion

        #region Botones según el mando

        /// <summary>Nombre del botón según el estilo (Xbox, PlayStation o Nintendo). key: A, B, X, Y, GUIDE, L1, R1, R3, L3, SELECT.</summary>
        public string Button(string key) => ButtonName(ButtonStyle, key);

        public static string ButtonName(string style, string key)
        {
            var ps = string.Equals(style, "PlayStation", StringComparison.OrdinalIgnoreCase);
            var nin = string.Equals(style, "Nintendo", StringComparison.OrdinalIgnoreCase);
            switch (key)
            {
                case "A": return ps ? "✕" : nin ? "B" : "A";
                case "B": return ps ? "○" : nin ? "A" : "B";
                case "X": return ps ? "□" : nin ? "Y" : "X";
                case "Y": return ps ? "△" : nin ? "X" : "Y";
                case "GUIDE": return ps ? "PS" : nin ? "Home" : "Xbox";
                case "L1": return ps ? "L1" : nin ? "L" : "LB";
                case "R1": return ps ? "R1" : nin ? "R" : "RB";
                case "L3": return ps ? "L3" : nin ? "L3" : "LS";
                case "R3": return ps ? "R3" : nin ? "R3" : "RS";
                case "SELECT": return ps ? "Create" : nin ? "−" : "View";
                default: return key;
            }
        }

        /// <summary>Sustituye {A} {B} {X} {Y} {GUIDE} {L1} {R1} {L3} {R3} {SELECT} por los botones del estilo.</summary>
        public string Pad(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0)
            {
                return text;
            }

            foreach (var k in new[] { "A", "B", "X", "Y", "GUIDE", "L1", "R1", "L3", "R3", "SELECT" })
            {
                text = text.Replace("{" + k + "}", Button(k));
            }

            return text;
        }

        #endregion
    }
}
