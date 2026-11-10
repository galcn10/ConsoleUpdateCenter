using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>
    /// Colores y pinceles comunes (acento verde agua → azul del logo).
    /// </summary>
    internal static class Styles
    {
        public static readonly Color Accent1 = Color.FromRgb(0x2E, 0xD4, 0xAA);
        public static readonly Color Accent2 = Color.FromRgb(0x3A, 0x86, 0xFF);
        public static readonly Color Ink = Color.FromRgb(0x11, 0x13, 0x18);

        public static Brush AccentBrush => Freeze(new LinearGradientBrush(Accent1, Accent2, 0));
        public static Brush AccentBrushDiagonal => Freeze(new LinearGradientBrush(Accent1, Accent2, 45));
        public static Brush InkBrush => Freeze(new SolidColorBrush(Ink));
        public static Brush White(byte alpha) => Freeze(new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)));

        /// <summary>Fondo de panel de cristal oscuro.</summary>
        public static Brush GlassPanel => Freeze(new LinearGradientBrush(
            Color.FromArgb(0xF0, 0x1E, 0x24, 0x38),
            Color.FromArgb(0xF0, 0x0D, 0x0F, 0x14),
            90));

        public static Effect SoftShadow(double blur = 24, double opacity = 0.45) => new DropShadowEffect
        {
            BlurRadius = blur,
            ShadowDepth = 3,
            Direction = 270,
            Opacity = opacity,
            Color = Colors.Black
        };

        public static readonly FontFamily Icons = new FontFamily("Segoe MDL2 Assets");

        public const string GlyphDownload = "\uE896";
        public const string GlyphSync = "\uE895";
        public const string GlyphOpen = "\uE8A7";
        public const string GlyphShield = "\uE83D";
        public const string GlyphCheck = "\uE73E";
        public const string GlyphInfo = "\uE946";

        private static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }

        /// <summary>Círculo con degradado de acento y glifo blanco.</summary>
        public static FrameworkElement AccentIcon(string glyph, double size)
        {
            return new System.Windows.Controls.Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = AccentBrushDiagonal,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = glyph,
                    FontFamily = Icons,
                    FontSize = size * 0.48,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }
    }
}
