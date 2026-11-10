using Playnite.SDK;
using ConsoleUpdateCenter.Core;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace ConsoleUpdateCenter.SettingsUI
{
    public enum ShortcutButton
    {
        None,
        HoldRightStick,
        HoldBack,
        PressGuide,
        HoldGuide,
        HoldLeftStick,
        HoldBumpers
    }

    public class PluginSettings
    {
        public bool EnableSteam { get; set; } = true;
        public bool EnableEpic { get; set; } = true;
        public bool EnableGog { get; set; } = true;
        public bool EnableXbox { get; set; } = true;

        public bool ScanOnStartup { get; set; } = true;

        /// <summary>Escaneo periódico en segundo plano (0 = desactivado).</summary>
        public int RescanIntervalMinutes { get; set; } = 30;

        /// <summary>Vacío = nombre traducido por defecto («Actualización pendiente»).</summary>
        public string TagName { get; set; } = string.Empty;

        /// <summary>Etiquetar también los juegos que Steam solo actualiza al iniciarlos.</summary>
        public bool TagLaunchOnly { get; set; } = true;
        public bool ShowNotifications { get; set; } = true;
        public bool NotifyWhenUpToDate { get; set; } = false;

        /// <summary>Atajo de mando para abrir la ventana Descargas (fuera de los juegos).</summary>
        public ShortcutButton OpenShortcut { get; set; } = ShortcutButton.PressGuide;
        public int ShortcutHoldMs { get; set; } = 1000;

        /// <summary>Aviso flotante al seleccionar un juego con actualización (funciona con cualquier tema).</summary>
        public bool ShowSelectionToast { get; set; } = true;
        public bool ToastFullscreenOnly { get; set; } = true;
        public int ToastAutoHideSeconds { get; set; } = 6;

        /// <summary>Insignia en las carátulas sin tocar el tema (experimental).</summary>
        public bool ExperimentalCoverBadges { get; set; } = false;

        /// <summary>Aviso cuando un juego termina de actualizarse.</summary>
        public bool NotifyCompleted { get; set; } = true;

        /// <summary>Traducir las novedades del parche al idioma del plugin (Steam oficial o Google Translate).</summary>
        public bool TranslatePatchNotes { get; set; } = true;

        /// <summary>Preguntar al iniciar un juego que tiene una actualización pendiente.</summary>
        public bool WarnOnLaunch { get; set; } = true;

        /// <summary>Segundos de cuenta atrás antes de apagar o suspender al terminar las descargas.</summary>
        public int PowerCountdownSeconds { get; set; } = 60;

        /// <summary>auto (idioma de Playnite) o un código de Loc.Languages.</summary>
        public string Language { get; set; } = "auto";

        /// <summary>Auto, Xbox, PlayStation o Nintendo.</summary>
        public string ButtonStyle { get; set; } = "Auto";

        /// <summary>theme, inter, nunito o system.</summary>
        public string MenuFont { get; set; } = "theme";

        /// <summary>Color de acento: theme (tema de Playnite), game (carátula) o neutral.</summary>
        public string AccentMode { get; set; } = "theme";

        public bool DiagnosticMode { get; set; } = false;
    }

    public class SettingsViewModel : ObservableBase, ISettings
    {
        private readonly ConsoleUpdateCenterPlugin plugin;
        private PluginSettings editingClone;
        private PluginSettings settings;

        public PluginSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public SettingsViewModel(ConsoleUpdateCenterPlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<PluginSettings>() ?? new PluginSettings();
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsChanged();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (Settings.PowerCountdownSeconds < 10)
            {
                errors.Add(Loc.T("La cuenta atrás debe ser de al menos 10 segundos."));
            }

            if (Settings.ToastAutoHideSeconds < 0)
            {
                errors.Add(Loc.T("El tiempo del aviso no puede ser negativo."));
            }

            if (Settings.RescanIntervalMinutes < 0)
            {
                errors.Add(Loc.T("El intervalo de escaneo no puede ser negativo."));
            }

            return errors.Count == 0;
        }
    }
}
