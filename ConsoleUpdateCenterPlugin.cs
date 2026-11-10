using ConsoleUpdateCenter.Core;
using ConsoleUpdateCenter.Providers.Steam;
using ConsoleUpdateCenter.SettingsUI;
using ConsoleUpdateCenter.UI;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ConsoleUpdateCenter
{
    public class ConsoleUpdateCenterPlugin : GenericPlugin
    {
        private const string SourceName = "ConsoleUpdateCenter";
        private const string Version = "1.0.0";

        private readonly SettingsViewModel settingsVm;
        private readonly UpdateService service;
        private readonly HashSet<Guid> runningGames = new HashSet<Guid>();
        private readonly object holdLock = new object();
        private readonly HashSet<string> heldButtons = new HashSet<string>();
        private readonly List<Tuple<string, string, string>> queuedToasts = new List<Tuple<string, string, string>>();
        private Timer holdTimer;

        private SelectionToast toast;
        private DispatcherTimer selectionDebounce;
        private CoverBadgeInjector badgeInjector;
        private Game selectedGame;

        public override Guid Id { get; } = Guid.Parse("6f3c2a7e-4b1d-4e8a-9c55-2d7f0b9a1e63");

        /// <summary>Raíz de datos para el tema ({PluginSettings Plugin=ConsoleUpdateCenter, Path=...}).</summary>
        public ThemeBridge Theme { get; }

        private PluginSettings Settings => settingsVm.Settings;

        private bool IsFullscreen => PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen;

        public ConsoleUpdateCenterPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };

            settingsVm = new SettingsViewModel(this);
            var data = GetPluginUserDataPath();
            Log.Init(data);
            Log.DiagnosticEnabled = Settings.DiagnosticMode;

            try
            {
                Loc.HostLanguage = api.ApplicationSettings.Language;
            }
            catch
            {
            }

            Loc.Init(data, Settings.Language);

            service = new UpdateService(api, () => settingsVm.Settings, data);
            Theme = new ThemeBridge(() => OpenDownloads(false), UpdateAll);

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = SourceName,
                SettingsRoot = nameof(Theme)
            });

            AddCustomElementSupport(new AddCustomElementSupportArgs
            {
                SourceName = SourceName,
                ElementList = new List<string> { "UpdateBadge" }
            });
        }

        #region Ciclo de vida

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            Log.Info($"Console Update Center {Version} iniciado (modo {PlayniteApi.ApplicationInfo.Mode}, idioma {Loc.Current}).");

            var ui = PlayniteApi.MainView.UIDispatcher;
            selectionDebounce = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(350) };
            selectionDebounce.Tick += (_, __) =>
            {
                selectionDebounce.Stop();
                UpdateToast(fromSelection: true);
            };

            badgeInjector = new CoverBadgeInjector(service, () => PlayniteApi.Dialogs.GetCurrentAppWindow(), ui);
            if (Settings.ExperimentalCoverBadges)
            {
                badgeInjector.Start();
            }

            service.Changed += (_, __) => ui.BeginInvoke(new Action(() =>
            {
                Theme.Refresh(service);
                UpdateToast(fromSelection: false);
                badgeInjector?.Refresh();
            }));

            service.Completed += OnCompleted;
            service.SpaceWarning += e => ShowToast("hard-drive", Loc.T("Sin espacio para actualizar"),
                Loc.T("{0} · faltan {1} en {2}", e.Name, Format.Bytes(e.BytesLeft), Format.Drive(e.LibraryFolder)));
            service.PowerReady += OnPowerReady;

            service.Start();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            holdTimer?.Dispose();
            badgeInjector?.Stop();
            service.Dispose();
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            // Al entrar en un juego se retira todo, como en una consola
            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                toast?.HideToast();
                DownloadsWindow.Open?.Close();
            });

            if (service.ArmedPower != PowerAction.None)
            {
                service.DisarmPower();
                Log.Info("Se inicia un juego: se cancela el apagado al terminar las descargas.");
            }

            try
            {
                if (Settings.WarnOnLaunch && !AskBeforeLaunch(args.Game))
                {
                    args.CancelStartup = true;
                }
            }
            catch (Exception e)
            {
                Log.Error("Error en el aviso al iniciar el juego.", e);
            }
        }

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            lock (runningGames)
            {
                runningGames.Add(args.Game.Id);
            }
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            lock (runningGames)
            {
                runningGames.Remove(args.Game.Id);
            }

            // Avisos que llegaron mientras se jugaba
            Task.Delay(3000).ContinueWith(_ => FlushQueuedToasts());
        }

        private bool GameRunningFast()
        {
            lock (runningGames)
            {
                if (runningGames.Count > 0)
                {
                    return true;
                }
            }

            if (SteamProvider.IsGameRunning())
            {
                return true;
            }

            // También los que Playnite marca como abiertos o arrancando (p. ej. tras volver a Playnite desde Console Overlay)
            try
            {
                return PlayniteApi.Database.Games.Any(g => g.IsRunning || g.IsLaunching);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Ajustes

        public override ISettings GetSettings(bool firstRunSettings) => settingsVm;

        public override UserControl GetSettingsView(bool firstRunView) => new SettingsView();

        internal void OnSettingsChanged()
        {
            Log.DiagnosticEnabled = Settings.DiagnosticMode;
            Log.Info("Ajustes guardados.");

            PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
            {
                if (Settings.ExperimentalCoverBadges)
                {
                    badgeInjector?.Start();
                }
                else
                {
                    badgeInjector?.Stop();
                }

                UpdateToast(fromSelection: false);
            }));

            service.Reload();
        }

        #endregion

        #region Aspecto (igual que Console Overlay)

        /// <summary>Estilo de las pantallas: color de acento, iconos de botones y tipografía. En el hilo de la interfaz.</summary>
        private Look CreateLook(ImageSource cover = null, ImageSource backdrop = null)
        {
            // Colores propios del plugin (los del logo): se distingue de Console Overlay, que usa los del tema
            FontFamily font = null;
            try
            {
                font = Palette.ChosenFont(Settings.MenuFont, out var fi);
                if (font == null && !string.Equals(Settings.MenuFont, "system", StringComparison.OrdinalIgnoreCase))
                {
                    font = Palette.ThemeFont(out fi);
                }
            }
            catch
            {
            }

            return new Look(ConsoleScreen.Brand1, ResolveButtonStyle(), font);
        }

        /// <summary>"Auto": los iconos elegidos en Playnite Fullscreen (Xbox o PlayStation).</summary>
        private string ResolveButtonStyle()
        {
            var st = Settings.ButtonStyle;
            if (!string.IsNullOrEmpty(st) && !string.Equals(st, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return st;
            }

            try
            {
                var file = Path.Combine(PlayniteApi.Paths.ConfigurationPath, "fullscreenConfig.json");
                if (File.Exists(file))
                {
                    var m = Regex.Match(File.ReadAllText(file), "\"ButtonPrompts\"\\s*:\\s*\"?(\\w+)\"?");
                    if (m.Success && (m.Groups[1].Value == "1" || string.Equals(m.Groups[1].Value, "PlayStation", StringComparison.OrdinalIgnoreCase)))
                    {
                        return "PlayStation";
                    }
                }
            }
            catch
            {
            }

            return "Xbox";
        }

        private ImageSource LoadImage(string dbFile, int decodeWidth)
        {
            if (string.IsNullOrEmpty(dbFile))
            {
                return null;
            }

            try
            {
                var path = dbFile.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : PlayniteApi.Database.GetFullFilePath(dbFile);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    return null;
                }

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception e)
            {
                Log.Debug("No se pudo cargar la imagen: " + e.Message);
                return null;
            }
        }

        #endregion

        #region Tema (opcional)

        public override Control GetGameViewControl(GetGameViewControlArgs args)
        {
            if (args.Name == "UpdateBadge")
            {
                return new UpdateBadge(service);
            }

            return null;
        }

        #endregion

        #region Avisos

        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            selectedGame = args.NewValue?.Count == 1 ? args.NewValue[0] : null;
            Log.Debug("Seleccionado: " + (selectedGame?.Name ?? "(ninguno)"));

            // Se espera a que la selección se asiente para no parpadear al desplazarse
            selectionDebounce?.Stop();
            selectionDebounce?.Start();
        }

        /// <summary>
        /// Juego sobre el que está el foco en Playnite (una carátula de la lista, la ficha del juego…).
        /// Null si el foco está en el hub, los menús o los filtros, aunque Playnite tenga un juego seleccionado.
        /// En el hilo de la interfaz.
        /// </summary>
        private Game FocusedGame()
        {
            try
            {
                var window = PlayniteApi.Dialogs.GetCurrentAppWindow();
                var focused = Keyboard.FocusedElement as DependencyObject;
                if ((focused == null || Window.GetWindow(focused) != window) && window != null)
                {
                    focused = FocusManager.GetFocusedElement(window) as DependencyObject;
                }

                var steps = 0;
                for (var d = focused; d != null && steps < 25; steps++)
                {
                    if (d is FrameworkElement fe)
                    {
                        var id = CoverBadgeInjector.ResolveGameId(fe.DataContext);
                        if (id.HasValue)
                        {
                            var game = PlayniteApi.Database.Games.Get(id.Value);
                            Log.Debug($"Foco en el juego {game?.Name} ({fe.GetType().Name}).");
                            return game;
                        }
                    }

                    if (d is Window)
                    {
                        break;
                    }

                    d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
                        ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d)
                        : LogicalTreeHelper.GetParent(d);
                }

                Log.Debug("Foco fuera de un juego: " + (focused?.GetType().Name ?? "(ninguno)") + ".");
            }
            catch (Exception e)
            {
                Log.Debug("No se pudo saber qué tiene el foco: " + e.Message);
            }

            return null;
        }

        private void UpdateToast(bool fromSelection)
        {
            try
            {
                var game = FocusedGame();
                var allowed = Settings.ShowSelectionToast
                              && (!Settings.ToastFullscreenOnly || IsFullscreen)
                              && ConsoleScreen.Current == null
                              && !GameRunningFast();

                var entry = game != null ? service.GetEntry(game.Id) : null;
                if (!allowed || entry == null)
                {
                    // Un aviso general (actualización completada…) no se retira al cambiar de juego
                    if (toast?.ShownGameId != null)
                    {
                        toast.HideToast();
                    }

                    return;
                }

                // Tras un escaneo solo se refresca un aviso que ya está a la vista
                if (!fromSelection && toast?.ShownGameId != game.Id)
                {
                    return;
                }

                EnsureToast();
                var look = CreateLook();
                GetHint(look, out var key, out var hint);
                toast.ShowForGame(look, game.Id, game.Name, entry, key, hint, Settings.ToastAutoHideSeconds);
            }
            catch (Exception e)
            {
                Log.Warn("Error mostrando el aviso.", e);
            }
        }

        /// <summary>Aviso general. Si hay un juego abierto, se guarda para después.</summary>
        private void ShowToast(string icon, string title, string subtitle)
        {
            if (GameRunningFast())
            {
                lock (queuedToasts)
                {
                    queuedToasts.Add(Tuple.Create(icon, title, subtitle));
                }

                return;
            }

            PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    EnsureToast();
                    toast.ShowMessage(CreateLook(), icon, title, subtitle, 7);
                }
                catch (Exception e)
                {
                    Log.Warn("Error mostrando un aviso.", e);
                }
            }));
        }

        private void FlushQueuedToasts()
        {
            List<Tuple<string, string, string>> list;
            lock (queuedToasts)
            {
                list = queuedToasts.ToList();
                queuedToasts.Clear();
            }

            if (list.Count == 0 || GameRunningFast())
            {
                lock (queuedToasts)
                {
                    queuedToasts.InsertRange(0, list);
                }

                return;
            }

            // Uno tras otro
            Task.Run(async () =>
            {
                foreach (var t in list)
                {
                    ShowToast(t.Item1, t.Item2, t.Item3);
                    await Task.Delay(7500).ConfigureAwait(false);
                }
            });
        }

        private void OnCompleted(List<UpdateEntry> done)
        {
            if (!Settings.NotifyCompleted || done.Count == 0)
            {
                return;
            }

            if (done.Count == 1)
            {
                ShowToast("circle-check", Loc.T("Actualización completada"), Loc.T("{0} · Listo para jugar", done[0].Name));
            }
            else
            {
                ShowToast("circle-check", Loc.T("{0} juegos actualizados", done.Count),
                    string.Join(", ", done.Take(3).Select(d => d.Name)) + (done.Count > 3 ? "…" : string.Empty));
            }
        }

        private void EnsureToast()
        {
            if (toast != null)
            {
                return;
            }

            toast = new SelectionToast();
            try
            {
                var owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                if (owner != null && owner.IsVisible)
                {
                    toast.Owner = owner;
                }
            }
            catch (Exception e)
            {
                Log.Debug("Aviso sin ventana propietaria: " + e.Message);
            }
        }

        private void GetHint(Look look, out string key, out string hint)
        {
            var hold = Loc.T("Mantén para ver opciones de actualización");
            switch (Settings.OpenShortcut)
            {
                case ShortcutButton.PressGuide:
                    key = look.Button("GUIDE");
                    hint = Loc.T("Pulsa para ver opciones de actualización");
                    break;
                case ShortcutButton.HoldGuide:
                    key = look.Button("GUIDE");
                    hint = hold;
                    break;
                case ShortcutButton.HoldRightStick:
                    key = look.Button("R3");
                    hint = hold;
                    break;
                case ShortcutButton.HoldLeftStick:
                    key = look.Button("L3");
                    hint = hold;
                    break;
                case ShortcutButton.HoldBack:
                    key = look.Button("SELECT");
                    hint = hold;
                    break;
                case ShortcutButton.HoldBumpers:
                    key = look.Button("L1") + " + " + look.Button("R1");
                    hint = hold;
                    break;
                default:
                    key = null;
                    hint = Loc.T("Opciones del juego › Console Update Center");
                    break;
            }
        }

        #endregion

        #region Aviso al iniciar un juego con actualización

        /// <summary>Devuelve true para seguir con el inicio del juego.</summary>
        private bool AskBeforeLaunch(Game game)
        {
            var entry = service.GetEntry(game.Id);
            if (entry == null || entry.Status == UpdateStatus.LaunchOnly)
            {
                return true;
            }

            string result = null;
            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                var cover = LoadImage(game.CoverImage, 240);
                var look = CreateLook(cover);

                var page = new ScreenPage
                {
                    Cover = cover,
                    Title = game.Name,
                    Message = entry.BytesLeft > 0
                        ? Loc.T("Hay una actualización de {0} pendiente.", Format.Bytes(entry.BytesLeft))
                        : Loc.T("Hay una actualización pendiente."),
                    FocusKey = "update"
                };
                page.AddChip(Format.StatusTitle(entry), entry.NoSpace ? Look.DangerBrush : Look.MidBrush);
                if (entry.Status == UpdateStatus.Downloading)
                {
                    var rem = Format.Remaining(entry.Remaining);
                    if (rem != null)
                    {
                        page.AddChip(rem);
                    }
                }

                var screen = new ConsoleScreen(look) { Title = "Console Update Center" };
                page.Rows.Add(new ScreenRow
                {
                    Key = "update",
                    Icon = "download",
                    Text = Loc.T("Actualizar primero"),
                    Desc = entry.Status == UpdateStatus.Downloading
                        ? Loc.T("Ver el progreso de la descarga")
                        : Loc.T("El launcher descarga la actualización y juegas después"),
                    Execute = () => { screen.Result = "update"; screen.Close(); }
                });
                page.Rows.Add(new ScreenRow
                {
                    Key = "play",
                    Icon = "play",
                    Text = Loc.T("Jugar igualmente"),
                    Desc = Loc.T("Algunos juegos online no dejan jugar sin actualizar"),
                    Execute = () => { screen.Result = "play"; screen.Close(); }
                });
                page.Rows.Add(new ScreenRow
                {
                    Key = "cancel",
                    Icon = "x",
                    Text = Loc.T("Cancelar"),
                    Execute = () => { screen.Result = "cancel"; screen.Close(); }
                });
                page.AddHint("A", Loc.T("Seleccionar"));
                page.AddHint("B", Loc.T("Cancelar"));

                screen.Loaded += (_, __) => screen.SetPage(page);
                screen.ShowDialog();
                result = screen.Result;
            });

            Log.Info($"Aviso al iniciar {game.Name}: {result ?? "cerrado"}.");
            if (result == "play")
            {
                return true;
            }

            if (result == "update")
            {
                var platform = entry.Platform;
                var downloading = entry.Status == UpdateStatus.Downloading;
                PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                {
                    OpenDownloads(true, game);
                    if (!downloading)
                    {
                        CheckPlatform(platform);
                    }
                }));
            }

            return false;
        }

        #endregion

        #region Apagar al terminar las descargas

        private void OnPowerReady(PowerAction action)
        {
            PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    DownloadsWindow.Open?.Close();
                    var screen = new CountdownScreen(CreateLook(), action, Settings.PowerCountdownSeconds, () =>
                    {
                        if (action == PowerAction.Sleep)
                        {
                            Native.Sleep();
                        }
                        else
                        {
                            Native.Shutdown();
                        }
                    });
                    screen.Show();
                }
                catch (Exception e)
                {
                    Log.Error("No se pudo mostrar la cuenta atrás.", e);
                }
            }));
        }

        #endregion

        #region Menús

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            const string section = "@Console Update Center";

            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = Loc.T("Abrir Descargas"),
                Action = a => OpenDownloads(false)
            };

            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = Loc.T("Buscar actualizaciones en todo"),
                Action = a => UpdateAll()
            };

            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = Loc.T("Volver a escanear"),
                Action = a => { Task.Run(() => service.ScanAsync(UpdateService.ScanReason.Manual)); }
            };

            yield return new MainMenuItem
            {
                MenuSection = section,
                Description = Loc.T("Abrir registro de diagnóstico"),
                Action = a => OpenLog()
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count != 1)
            {
                yield break;
            }

            var game = args.Games[0];
            var entry = service.GetEntry(game.Id);

            yield return new GameMenuItem
            {
                MenuSection = "Console Update Center",
                Description = entry != null ? Loc.T("Ver actualización de este juego") : Loc.T("Comprobar actualizaciones"),
                Action = a => OpenDownloads(true, game)
            };

            if (game.IsInstalled && game.PluginId == SteamProvider.SteamPluginId)
            {
                yield return new GameMenuItem
                {
                    MenuSection = "Console Update Center",
                    Description = Loc.T("Validar archivos en Steam"),
                    Action = a => SteamProvider.Validate(game.GameId)
                };
            }
        }

        private void OpenLog()
        {
            if (!string.IsNullOrEmpty(Log.FilePath) && File.Exists(Log.FilePath))
            {
                Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
            }
            else
            {
                PlayniteApi.Dialogs.ShowMessage(
                    Loc.T("Todavía no hay registro. Activa el modo diagnóstico en los ajustes del plugin."),
                    "Console Update Center");
            }
        }

        #endregion

        #region API para otros plugins (Console Overlay)

        /// <summary>Versión de esta API. Console Overlay la comprueba antes de usarla.</summary>
        public int IntegrationApiVersion => 1;

        /// <summary>Hay un panel de Console Update Center abierto (mientras, el otro plugin no debe reaccionar al mando).</summary>
        public bool IsPanelOpen => ConsoleScreen.Current != null;

        /// <summary>Ruta de status.json con el estado de las descargas.</summary>
        public string StatusFilePath => Path.Combine(GetPluginUserDataPath(), "status.json");

        /// <summary>
        /// Abre el panel Descargas. Con gameId, empieza por ese juego. onClosed se llama al cerrarse
        /// (en el hilo de la interfaz), para que el otro plugin recupere el foco.
        /// Funciona aunque haya un juego abierto.
        /// </summary>
        public void OpenPanel(Guid? gameId, Action onClosed)
        {
            Game game = null;
            if (gameId.HasValue)
            {
                try
                {
                    game = PlayniteApi.Database.Games.Get(gameId.Value);
                }
                catch
                {
                }
            }

            Log.Info("Panel abierto por otro plugin" + (game != null ? " con " + game.Name : "") + ".");
            OpenDownloads(false, game, false, onClosed);
        }

        /// <summary>Cierra el panel si está abierto.</summary>
        public void ClosePanel()
        {
            PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() => DownloadsWindow.Open?.Close()));
        }

        #endregion

        #region Pantalla Descargas y acciones

        /// <param name="withSelectedGame">Abrir con la ficha del juego sobre el que está el foco (si lo hay).</param>
        /// <param name="explicitGame">Juego concreto (menú del juego, aviso al iniciar).</param>
        /// <param name="requireGame">Solo abrir si el foco está sobre un juego (atajo de mando).</param>
        private void OpenDownloads(bool withSelectedGame, Game explicitGame = null, bool requireGame = false, Action onClosed = null)
        {
            PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (DownloadsWindow.Open != null)
                    {
                        DownloadsWindow.Open.Activate();
                        return;
                    }

                    if (ConsoleScreen.Current != null)
                    {
                        return;
                    }

                    toast?.HideToast();

                    GameFocus focus = null;
                    var game = explicitGame ?? (withSelectedGame ? FocusedGame() : null);
                    if (requireGame && game == null)
                    {
                        Log.Debug("Atajo de mando sin juego con el foco: no se abre Descargas.");
                        return;
                    }

                    // Con un juego abierto (aunque estés en Playnite), el botón Guía es de Console Overlay
                    if (requireGame && GameRunningFast())
                    {
                        Log.Debug("Hay un juego abierto: el atajo no abre Descargas.");
                        return;
                    }

                    if (game != null)
                    {
                        focus = new GameFocus
                        {
                            Id = game.Id,
                            Name = game.Name,
                            PluginId = game.PluginId,
                            GameId = game.GameId,
                            IsInstalled = game.IsInstalled,
                            Cover = LoadImage(game.CoverImage, 240)
                        };
                    }

                    var look = CreateLook(focus?.Cover, focus?.Backdrop);
                    var actions = new DownloadsActions
                    {
                        UpdateAll = UpdateAll,
                        CheckPlatform = CheckPlatform,
                        Toast = ShowToast,
                        TranslateNotes = () => Settings.TranslatePatchNotes
                    };

                    var window = new DownloadsWindow(look, service, actions, focus);
                    window.Closed += (_, __) =>
                    {
                        UpdateToast(fromSelection: true);
                        try
                        {
                            onClosed?.Invoke();
                        }
                        catch (Exception e)
                        {
                            Log.Warn("Error al avisar del cierre del panel.", e);
                        }
                    };
                    window.Show();
                }
                catch (Exception e)
                {
                    Log.Error("No se pudo abrir la pantalla Descargas.", e);
                }
            }));
        }

        private void UpdateAll() => RunCheck(null);

        private void CheckPlatform(UpdatePlatform platform) => RunCheck(platform);

        private void RunCheck(UpdatePlatform? only)
        {
            Task.Run(async () =>
            {
                try
                {
                    var results = await service.UpdateAllAsync(msg => DownloadsWindow.Open?.SetProgressText(msg), only);

                    if (results.Count > 0 && results.Any(r => !r.Ok))
                    {
                        ShowToast("triangle-alert", Loc.T("No se pudo comprobar todo"),
                            string.Join(" ", results.Where(r => !r.Ok).Select(r => r.Message)));
                    }
                    else if (results.Count > 0)
                    {
                        ShowToast("refresh-cw", Loc.T("Buscando actualizaciones"),
                            Loc.T("Los launchers están comprobando. Las descargas aparecerán en unos segundos."));
                    }

                    // Si la pantalla Descargas está abierta, que vuelva al frente
                    _ = PlayniteApi.MainView.UIDispatcher.BeginInvoke(new Action(() =>
                    {
                        var w = DownloadsWindow.Open;
                        if (w != null)
                        {
                            w.Activate();
                            Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(w).Handle);
                        }
                    }));
                }
                catch (Exception e)
                {
                    Log.Error("Error al buscar actualizaciones.", e);
                }
            });
        }

        #endregion

        #region Mando

        public override void OnControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            try
            {
                var name = args.Button.ToString();
                var pressed = args.State == ControllerInputState.Pressed;

                lock (holdLock)
                {
                    if (pressed)
                    {
                        heldButtons.Add(name);
                    }
                    else
                    {
                        heldButtons.Remove(name);
                    }
                }

                // Con una pantalla abierta, el mando la controla
                var screen = ConsoleScreen.Current;
                if (screen != null)
                {
                    if (pressed)
                    {
                        Log.Debug("Mando (pantalla): " + name);
                        screen.Dispatcher.BeginInvoke(new Action(() => screen.HandleButton(name)));
                    }

                    CancelHold();
                    return;
                }

                // Solo fuera de los juegos, como en una consola
                if (GameRunningFast())
                {
                    CancelHold();
                    return;
                }

                var shortcut = Settings.OpenShortcut;
                if (shortcut == ShortcutButton.None)
                {
                    return;
                }

                if (shortcut == ShortcutButton.PressGuide)
                {
                    if (pressed && name == "Guide")
                    {
                        Log.Debug("Atajo de mando: Guía pulsado.");
                        OpenDownloads(true, requireGame: true);
                    }

                    return;
                }

                var required = RequiredButtons(shortcut);
                if (!required.Contains(name))
                {
                    return;
                }

                lock (holdLock)
                {
                    var all = required.All(heldButtons.Contains);
                    if (pressed && all)
                    {
                        holdTimer?.Dispose();
                        var ms = Math.Max(300, Settings.ShortcutHoldMs);
                        holdTimer = new Timer(_ =>
                        {
                            bool still;
                            lock (holdLock)
                            {
                                still = required.All(heldButtons.Contains);
                            }

                            if (still && ConsoleScreen.Current == null && !GameRunningFast())
                            {
                                Log.Debug($"Atajo de mando: {string.Join("+", required)} mantenido {ms} ms.");
                                OpenDownloads(true, requireGame: true);
                            }
                        }, null, ms, Timeout.Infinite);
                    }
                    else if (!pressed)
                    {
                        holdTimer?.Dispose();
                        holdTimer = null;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("Error procesando el mando.", e);
            }
        }

        private void CancelHold()
        {
            lock (holdLock)
            {
                holdTimer?.Dispose();
                holdTimer = null;
            }
        }

        private static string[] RequiredButtons(ShortcutButton b)
        {
            switch (b)
            {
                case ShortcutButton.HoldRightStick: return new[] { "RightStick" };
                case ShortcutButton.HoldLeftStick: return new[] { "LeftStick" };
                case ShortcutButton.HoldBack: return new[] { "Back" };
                case ShortcutButton.HoldGuide: return new[] { "Guide" };
                case ShortcutButton.HoldBumpers: return new[] { "LeftShoulder", "RightShoulder" };
                default: return new string[0];
            }
        }

        #endregion
    }
}
