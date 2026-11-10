using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ConsoleUpdateCenter.SettingsUI
{
    /// <summary>
    /// Vista de ajustes construida por código (sin XAML).
    /// </summary>
    public class SettingsView : UserControl
    {
        private readonly StackPanel root = new StackPanel { Margin = new Thickness(20) };

        public SettingsView()
        {
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root
            };

            Header(Loc.T("Plataformas"));
            Check(Loc.T("Steam (detecta cada juego pendiente)"), "Settings.EnableSteam");
            Check(Loc.T("Epic Games (detecta descargas a medias y reinicia el launcher)"), "Settings.EnableEpic");
            Check(Loc.T("GOG Galaxy (reinicia Galaxy para que compruebe)"), "Settings.EnableGog");
            Check(Loc.T("Xbox / Microsoft Store (abre Descargas y actualizaciones)"), "Settings.EnableXbox");

            Header(Loc.T("Escaneo"));
            Check(Loc.T("Escanear al iniciar Playnite"), "Settings.ScanOnStartup");
            Number(Loc.T("Escaneo periódico en minutos (0 = desactivado)"), "Settings.RescanIntervalMinutes");
            Text(Loc.T("Nombre de la etiqueta (vacío = «Actualización pendiente»)"), "Settings.TagName");
            Check(Loc.T("Etiquetar también los juegos que Steam solo actualiza al iniciarlos"), "Settings.TagLaunchOnly");

            Header(Loc.T("Avisos"));
            Check(Loc.T("Aviso al seleccionar un juego con actualización"), "Settings.ShowSelectionToast");
            Check(Loc.T("Mostrar ese aviso solo en modo Fullscreen"), "Settings.ToastFullscreenOnly");
            Number(Loc.T("Segundos que se muestra el aviso (0 = mientras esté seleccionado)"), "Settings.ToastAutoHideSeconds");
            Check(Loc.T("Avisar cuando un juego termina de actualizarse"), "Settings.NotifyCompleted");
            Check(Loc.T("Preguntar al iniciar un juego con una actualización pendiente"), "Settings.WarnOnLaunch");
            Check(Loc.T("Notificaciones de Playnite con el resumen de cada escaneo"), "Settings.ShowNotifications");
            Check(Loc.T("Avisar también cuando todo está actualizado"), "Settings.NotifyWhenUpToDate");
            Number(Loc.T("Cuenta atrás antes de apagar o suspender (segundos)"), "Settings.PowerCountdownSeconds");

            Header(Loc.T("Mando"));
            EnumCombo(Loc.T("Botón para abrir Descargas (fuera de los juegos)"), "Settings.OpenShortcut", new[]
            {
                Loc.T("Ninguno"),
                Loc.T("Mantener R3 (clic del stick derecho)"),
                Loc.T("Mantener Select / View"),
                Loc.T("Pulsar botón Guía (PS / Xbox) — recomendado"),
                Loc.T("Mantener botón Guía (PS / Xbox)"),
                Loc.T("Mantener L3 (clic del stick izquierdo)"),
                Loc.T("Mantener L1 + R1 a la vez")
            });
            Number(Loc.T("Tiempo de pulsación en milisegundos (opciones «Mantener»)"), "Settings.ShortcutHoldMs");
            Note(Loc.T("Elige un botón que tu tema no use: Playnite entrega cada pulsación al tema y a los plugins a la vez. Con mandos Xbox, Windows abre la Xbox Game Bar con el botón Guía (se desactiva en Configuración › Juegos). Si Steam controla el mando, puede quedarse con ese botón."));
            StringCombo(Loc.T("Iconos de botones"), "Settings.ButtonStyle", new[]
            {
                Opt("Auto", Loc.T("Automático (los de Playnite)")),
                Opt("Xbox", "Xbox"),
                Opt("PlayStation", "PlayStation"),
                Opt("Nintendo", "Nintendo")
            });

            Header(Loc.T("Aspecto"));
            StringCombo(Loc.T("Idioma"), "Settings.Language",
                new[] { Opt("auto", Loc.T("Automático (idioma de Playnite)")) }
                    .Concat(Loc.Languages.Select(l => Opt(l[0], l[1]))).ToArray());
            StringCombo(Loc.T("Tipografía"), "Settings.MenuFont", new[]
            {
                Opt("theme", Loc.T("Tema de Playnite")),
                Opt("inter", "Inter"),
                Opt("nunito", "Nunito"),
                Opt("system", "Segoe UI")
            });
            Check(Loc.T("Insignia en las carátulas sin tocar el tema (experimental)"), "Settings.ExperimentalCoverBadges");
            Check(Loc.T("Traducir las notas del parche a tu idioma (si Steam no las tiene traducidas, se usa Google Translate)"), "Settings.TranslatePatchNotes");
            Note(Loc.T("Los cambios de idioma se aplican al reiniciar Playnite. Los idiomas marcados con (beta) son traducciones sin revisar."));

            Header(Loc.T("Diagnóstico"));
            Check(Loc.T("Modo diagnóstico (registro detallado en diagnostico.log)"), "Settings.DiagnosticMode");
        }

        private static KeyValuePair<string, string> Opt(string value, string label) => new KeyValuePair<string, string>(value, label);

        private void Header(string text)
        {
            root.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, root.Children.Count == 0 ? 0 : 18, 0, 8)
            });
        }

        private void Note(string text)
        {
            root.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(0, 2, 0, 6)
            });
        }

        private void Check(string label, string path)
        {
            var cb = new CheckBox { Content = label, Margin = new Thickness(0, 4, 0, 4) };
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            root.Children.Add(cb);
        }

        private void Row(string label, FrameworkElement control)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            control.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(control, Dock.Right);
            panel.Children.Add(control);
            panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(panel);
        }

        private void Number(string label, string path)
        {
            var tb = new TextBox { Width = 90 };
            tb.SetBinding(TextBox.TextProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Row(label, tb);
        }

        private void Text(string label, string path)
        {
            var tb = new TextBox { Width = 260 };
            tb.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            Row(label, tb);
        }

        private void EnumCombo(string label, string path, string[] items)
        {
            var cb = new ComboBox { Width = 320, ItemsSource = items };
            cb.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                Converter = new EnumIndexConverter()
            });
            Row(label, cb);
        }

        private void StringCombo(string label, string path, KeyValuePair<string, string>[] items)
        {
            var cb = new ComboBox
            {
                Width = 320,
                ItemsSource = items,
                DisplayMemberPath = "Value",
                SelectedValuePath = "Key"
            };
            cb.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(path) { Mode = BindingMode.TwoWay });
            Row(label, cb);
        }

        private class EnumIndexConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is ShortcutButton b ? (int)b : 0;

            public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
                value is int i && i >= 0 ? (ShortcutButton)i : ShortcutButton.None;
        }
    }
}
