using ConsoleUpdateCenter.Providers;
using ConsoleUpdateCenter.Providers.Steam;
using ConsoleUpdateCenter.SettingsUI;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleUpdateCenter.Core
{
    public enum PowerAction
    {
        None,
        Shutdown,
        Sleep
    }

    /// <summary>
    /// Coordina los proveedores: escaneo, etiquetas, notificaciones, vigilancia de archivos, velocidad,
    /// descargas terminadas, espacio en disco, apagado al terminar y "Buscar en todo".
    /// </summary>
    public class UpdateService : IDisposable
    {
        private const string NotificationId = "ConsoleUpdateCenter_Summary";

        private readonly IPlayniteAPI api;
        private readonly Func<PluginSettings> getSettings;
        private readonly string dataFolder;
        private readonly List<ILauncherProvider> allProviders;
        private readonly SteamProvider steam;
        private readonly SemaphoreSlim scanLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly object stateLock = new object();

        private Timer debounceTimer;
        private Timer periodicTimer;
        private Timer fastTimer;
        private HashSet<string> lastNotifiedKeys = new HashSet<string>();
        private List<UpdateEntry> entries = new List<UpdateEntry>();
        private HashSet<Guid> pendingPlayniteIds = new HashSet<Guid>();
        private List<SteamProvider.LaunchOnlyGame> launchOnly = new List<SteamProvider.LaunchOnlyGame>();
        private volatile bool updatingAll;
        private bool firstScanDone;

        // Velocidad: últimos bytes vistos por juego
        private readonly Dictionary<string, Tuple<long, DateTime, double>> speeds = new Dictionary<string, Tuple<long, DateTime, double>>();
        private readonly HashSet<string> spaceWarned = new HashSet<string>();

        // Apagado al terminar
        private PowerAction armed = PowerAction.None;
        private int idleScansWhileArmed;

        /// <summary>Tras cada escaneo (desde un hilo en segundo plano).</summary>
        public event EventHandler Changed;

        /// <summary>Juegos que han terminado de actualizarse.</summary>
        public event Action<List<UpdateEntry>> Completed;

        /// <summary>Una descarga no cabe en el disco.</summary>
        public event Action<UpdateEntry> SpaceWarning;

        /// <summary>Las descargas han terminado con el apagado programado.</summary>
        public event Action<PowerAction> PowerReady;

        public DateTime? LastScan { get; private set; }
        public bool IsUpdatingAll => updatingAll;
        public bool IsScanning => scanLock.CurrentCount == 0;
        public PowerAction ArmedPower => armed;

        public UpdateService(IPlayniteAPI api, Func<PluginSettings> getSettings, string dataFolder)
        {
            this.api = api;
            this.getSettings = getSettings;
            this.dataFolder = dataFolder;
            steam = new SteamProvider();
            allProviders = new List<ILauncherProvider>
            {
                steam,
                new EpicProvider(),
                new GogProvider(),
                new XboxProvider()
            };
        }

        #region Consultas

        public IReadOnlyList<UpdateEntry> Entries
        {
            get
            {
                lock (stateLock)
                {
                    return entries.ToList();
                }
            }
        }

        public IReadOnlyList<SteamProvider.LaunchOnlyGame> LaunchOnlyGames
        {
            get
            {
                lock (stateLock)
                {
                    return launchOnly.ToList();
                }
            }
        }

        public UpdateEntry GetEntry(Guid playniteGameId)
        {
            lock (stateLock)
            {
                return entries.FirstOrDefault(e => e.PlayniteId == playniteGameId);
            }
        }

        public bool IsLaunchOnly(string steamAppId)
        {
            lock (stateLock)
            {
                return launchOnly.Any(g => g.AppId == steamAppId);
            }
        }

        public ILauncherProvider ProviderForLibrary(Guid libraryPluginId) =>
            ActiveProviders.FirstOrDefault(p => p.LibraryPluginId == libraryPluginId);

        public bool HasPendingUpdate(Guid playniteGameId)
        {
            lock (stateLock)
            {
                return pendingPlayniteIds.Contains(playniteGameId);
            }
        }

        /// <summary>Proveedores activados en ajustes e instalados en el PC.</summary>
        public List<ILauncherProvider> ActiveProviders
        {
            get
            {
                var s = getSettings();
                return allProviders.Where(p => p.IsInstalled && IsEnabled(p, s)).ToList();
            }
        }

        private static bool IsEnabled(ILauncherProvider p, PluginSettings s)
        {
            switch (p.Platform)
            {
                case UpdatePlatform.Steam: return s.EnableSteam;
                case UpdatePlatform.Epic: return s.EnableEpic;
                case UpdatePlatform.Gog: return s.EnableGog;
                case UpdatePlatform.Xbox: return s.EnableXbox;
                default: return false;
            }
        }

        private bool SteamActive => ActiveProviders.Contains(steam);

        #endregion

        #region Arranque y parada

        public void Start()
        {
            var providers = ActiveProviders;
            Log.Info("Plataformas activas: " + (providers.Count == 0 ? "ninguna" : string.Join(", ", providers.Select(p => p.DisplayName))));

            debounceTimer = new Timer(_ => Fire(() => ScanAsync(ScanReason.FileChange)), null, Timeout.Infinite, Timeout.Infinite);
            fastTimer = new Timer(_ => Fire(() => ScanAsync(ScanReason.Progress)), null, Timeout.Infinite, Timeout.Infinite);
            periodicTimer = new Timer(_ => Fire(() => ScanAsync(ScanReason.Periodic)), null, Timeout.Infinite, Timeout.Infinite);

            RestartWatchers();
            ApplyPeriodic();

            if (getSettings().ScanOnStartup)
            {
                Fire(() => ScanAsync(ScanReason.Startup));
            }
        }

        /// <summary>Aplicar cambios de ajustes.</summary>
        public void Reload()
        {
            RestartWatchers();
            ApplyPeriodic();
            Fire(() => ScanAsync(ScanReason.Manual));
        }

        private void ApplyPeriodic()
        {
            var minutes = getSettings().RescanIntervalMinutes;
            var period = minutes > 0 ? TimeSpan.FromMinutes(minutes) : Timeout.InfiniteTimeSpan;
            periodicTimer?.Change(period, period);
        }

        public void Dispose()
        {
            cts.Cancel();
            debounceTimer?.Dispose();
            fastTimer?.Dispose();
            periodicTimer?.Dispose();
            StopWatchers();
        }

        #endregion

        #region Escaneo

        public enum ScanReason
        {
            Startup,
            Manual,
            Periodic,
            FileChange,
            Progress,
            AfterUpdateAll
        }

        public Task ScanAsync(ScanReason reason) => ScanCoreAsync(reason);

        private async Task ScanCoreAsync(ScanReason reason)
        {
            if (cts.IsCancellationRequested)
            {
                return;
            }

            // Un escaneo manual espera al que esté en curso; los automáticos se descartan
            var waitMs = reason == ScanReason.Manual || reason == ScanReason.AfterUpdateAll ? 30000 : 0;
            if (!await scanLock.WaitAsync(waitMs).ConfigureAwait(false))
            {
                return;
            }

            List<UpdateEntry> completed = null;
            List<UpdateEntry> noSpace = null;
            var powerNow = PowerAction.None;
            try
            {
                var started = DateTime.Now;
                var found = new List<UpdateEntry>();
                var detecting = ActiveProviders.Where(p => p.CanDetect).ToList();
                foreach (var provider in detecting)
                {
                    try
                    {
                        found.AddRange(provider.Scan());
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Error escaneando {provider.DisplayName}.", e);
                    }
                }

                var lo = new List<SteamProvider.LaunchOnlyGame>();
                if (detecting.Contains(steam))
                {
                    try
                    {
                        lo = steam.GetLaunchOnlyGames();
                    }
                    catch (Exception e)
                    {
                        Log.Warn("No se pudo leer el ajuste de actualización de Steam.", e);
                    }
                }

                ApplySpeedAndSpace(found);
                var installed = ApplyToLibrary(found);

                List<UpdateEntry> previous;
                lock (stateLock)
                {
                    previous = entries;
                    entries = found;
                    launchOnly = lo;
                    pendingPlayniteIds = new HashSet<Guid>(found.Where(f => f.PlayniteId.HasValue).Select(f => f.PlayniteId.Value));
                }

                // Juegos que estaban pendientes y ya no: actualizados (si siguen instalados)
                if (firstScanDone)
                {
                    var now = new HashSet<string>(found.Select(f => f.Key));
                    var platforms = new HashSet<UpdatePlatform>(detecting.Select(p => p.Platform));
                    completed = previous
                        .Where(p => !now.Contains(p.Key)
                                    && platforms.Contains(p.Platform)
                                    && p.Status != UpdateStatus.LaunchOnly
                                    && p.PlayniteId.HasValue
                                    && installed.Contains(p.PlayniteId.Value))
                        .ToList();
                }

                firstScanDone = true;

                noSpace = found.Where(f => f.NoSpace && spaceWarned.Add(f.Key)).ToList();

                LastScan = DateTime.Now;
                Log.Info($"Escaneo ({reason}): {found.Count} pendientes en {(DateTime.Now - started).TotalMilliseconds:0} ms.");

                // Apagado al terminar: sin descargas activas en dos escaneos seguidos
                var downloading = found.Any(f => f.Status == UpdateStatus.Downloading);
                if (armed != PowerAction.None)
                {
                    idleScansWhileArmed = downloading ? 0 : idleScansWhileArmed + 1;
                    if (idleScansWhileArmed >= 2)
                    {
                        powerNow = armed;
                        armed = PowerAction.None;
                        Log.Info("Descargas terminadas: acción de energía " + powerNow);
                    }
                }

                // Con descargas (o apagado programado), refrescar a menudo para el progreso
                var fast = downloading || armed != PowerAction.None;
                fastTimer?.Change(fast ? TimeSpan.FromSeconds(10) : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                Notify(found, reason);
                WriteStatusFile(found);
            }
            catch (Exception e)
            {
                Log.Error("Error durante el escaneo.", e);
            }
            finally
            {
                scanLock.Release();
            }

            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            if (completed != null && completed.Count > 0)
            {
                Log.Info("Actualizados: " + string.Join(", ", completed.Select(c => c.Name)));
                Raise(() => Completed?.Invoke(completed));
            }

            if (noSpace != null)
            {
                foreach (var e in noSpace)
                {
                    Log.Warn($"Sin espacio para {e.Name}: faltan {e.BytesLeft} bytes, libres {e.FreeBytes}.");
                    Raise(() => SpaceWarning?.Invoke(e));
                }
            }

            if (powerNow != PowerAction.None)
            {
                Raise(() => PowerReady?.Invoke(powerNow));
            }
        }

        private void ApplySpeedAndSpace(List<UpdateEntry> found)
        {
            var now = DateTime.Now;
            var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            lock (speeds)
            {
                foreach (var e in found)
                {
                    if (e.BytesTotal > 0)
                    {
                        if (speeds.TryGetValue(e.Key, out var prev))
                        {
                            var delta = e.BytesDone - prev.Item1;
                            var secs = (now - prev.Item2).TotalSeconds;
                            if (delta > 0 && secs > 0.5)
                            {
                                var inst = delta / secs;
                                var speed = prev.Item3 <= 0 ? inst : prev.Item3 * 0.6 + inst * 0.4;
                                speeds[e.Key] = Tuple.Create(e.BytesDone, now, speed);
                                e.BytesPerSecond = speed;
                            }
                            else if (delta < 0)
                            {
                                speeds[e.Key] = Tuple.Create(e.BytesDone, now, 0.0);
                            }
                            else
                            {
                                // Steam no reescribe el manifiesto a cada momento: se mantiene la última velocidad un rato
                                e.BytesPerSecond = e.Status == UpdateStatus.Downloading && secs < 90 ? prev.Item3 : 0;
                            }
                        }
                        else
                        {
                            speeds[e.Key] = Tuple.Create(e.BytesDone, now, 0.0);
                        }
                    }

                    if (!string.IsNullOrEmpty(e.LibraryFolder))
                    {
                        try
                        {
                            var root = Path.GetPathRoot(e.LibraryFolder);
                            if (!free.TryGetValue(root, out var bytes))
                            {
                                bytes = new DriveInfo(root).AvailableFreeSpace;
                                free[root] = bytes;
                            }

                            e.FreeBytes = bytes;
                        }
                        catch
                        {
                        }
                    }
                }

                var keys = new HashSet<string>(found.Select(f => f.Key));
                foreach (var k in speeds.Keys.Where(k => !keys.Contains(k)).ToList())
                {
                    speeds.Remove(k);
                }
            }
        }

        /// <summary>
        /// Relaciona cada entrada con su juego de Playnite y pone o quita la etiqueta.
        /// Devuelve los Id de los juegos instalados de las plataformas con detección.
        /// </summary>
        private HashSet<Guid> ApplyToLibrary(List<UpdateEntry> found)
        {
            var settings = getSettings();
            var tagName = string.IsNullOrWhiteSpace(settings.TagName) ? Loc.T("Actualización pendiente") : settings.TagName.Trim();
            var detectPlugins = ActiveProviders.Where(p => p.CanDetect).Select(p => p.LibraryPluginId).ToList();
            var installed = new HashSet<Guid>();

            api.MainView.UIDispatcher.Invoke(() =>
            {
                var tag = api.Database.Tags.FirstOrDefault(t => string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase))
                          ?? api.Database.Tags.Add(tagName);

                var games = api.Database.Games.Where(g => detectPlugins.Contains(g.PluginId)).ToList();
                var byKey = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in games)
                {
                    if (g.IsInstalled)
                    {
                        installed.Add(g.Id);
                    }

                    if (!string.IsNullOrEmpty(g.GameId))
                    {
                        byKey[g.PluginId + "|" + g.GameId] = g;
                    }
                }

                var pendingIds = new HashSet<Guid>();
                foreach (var entry in found)
                {
                    var provider = allProviders.First(p => p.Platform == entry.Platform);
                    if (byKey.TryGetValue(provider.LibraryPluginId + "|" + entry.GameId, out var game))
                    {
                        entry.PlayniteId = game.Id;
                        entry.Name = game.Name;
                        if (entry.Status != UpdateStatus.LaunchOnly || settings.TagLaunchOnly)
                        {
                            pendingIds.Add(game.Id);
                        }
                    }
                    else
                    {
                        Log.Debug($"Sin juego en Playnite para {entry.Platform} {entry.GameId} ({entry.Name}).");
                    }
                }

                using (api.Database.BufferedUpdate())
                {
                    foreach (var game in games)
                    {
                        var shouldHave = pendingIds.Contains(game.Id);
                        var has = game.TagIds?.Contains(tag.Id) == true;
                        if (shouldHave && !has)
                        {
                            if (game.TagIds == null)
                            {
                                game.TagIds = new List<Guid>();
                            }

                            game.TagIds.Add(tag.Id);
                            api.Database.Games.Update(game);
                        }
                        else if (!shouldHave && has)
                        {
                            game.TagIds.Remove(tag.Id);
                            api.Database.Games.Update(game);
                        }
                    }
                }
            });

            return installed;
        }

        private void Notify(List<UpdateEntry> found, ScanReason reason)
        {
            var settings = getSettings();
            var keys = new HashSet<string>(found
                .Where(f => f.Status != UpdateStatus.LaunchOnly)
                .Select(f => f.Key));
            var launchOnlyCount = found.Count(f => f.Status == UpdateStatus.LaunchOnly);

            var isNew = keys.Except(lastNotifiedKeys).Any();
            var manual = reason == ScanReason.Manual || reason == ScanReason.AfterUpdateAll;
            lastNotifiedKeys = keys;

            if (!settings.ShowNotifications || !(manual || isNew || reason == ScanReason.Startup))
            {
                return;
            }

            string text;
            if (keys.Count == 0)
            {
                if (!manual && !settings.NotifyWhenUpToDate && launchOnlyCount == 0)
                {
                    api.Notifications.Remove(NotificationId);
                    return;
                }

                text = Loc.T("Todo está actualizado.");
            }
            else
            {
                var names = found.Where(f => f.Status != UpdateStatus.LaunchOnly).Select(f => f.Name).Distinct().ToList();
                text = Loc.T("{0} juego(s) con actualización: {1}", names.Count, string.Join(", ", names.Take(4))) +
                       (names.Count > 4 ? " " + Loc.T("y {0} más.", names.Count - 4) : ".");
            }

            if (launchOnlyCount > 0)
            {
                text += " " + Loc.T("{0} solo se actualizan al iniciarlos.", launchOnlyCount);
            }

            api.Notifications.Remove(NotificationId);
            api.Notifications.Add(new NotificationMessage(NotificationId, "Console Update Center: " + text, NotificationType.Info));
        }

        /// <summary>
        /// Estado para otros plugins (Console Overlay): status.json en la carpeta de datos del plugin.
        /// </summary>
        private void WriteStatusFile(List<UpdateEntry> found)
        {
            if (string.IsNullOrEmpty(dataFolder))
            {
                return;
            }

            try
            {
                var status = new StatusFile
                {
                    Version = 1,
                    Updated = DateTime.Now,
                    Pending = found.Count(f => f.Status != UpdateStatus.LaunchOnly),
                    Items = found
                        .Where(f => f.Status != UpdateStatus.LaunchOnly)
                        .OrderBy(f => f.Status == UpdateStatus.Downloading ? 0 : 1)
                        .Select(f => new StatusItem
                        {
                            Name = f.Name,
                            Platform = f.Platform.ToString(),
                            GameId = f.PlayniteId?.ToString(),
                            Status = f.Status.ToString(),
                            Progress = f.Progress,
                            BytesPerSecond = f.BytesPerSecond,
                            SecondsLeft = f.Remaining?.TotalSeconds
                        })
                        .ToList()
                };

                Directory.CreateDirectory(dataFolder);
                var path = Path.Combine(dataFolder, "status.json");
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialization.ToJson(status, true), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Log.Debug("No se pudo escribir status.json: " + e.Message);
            }
        }

        public class StatusFile
        {
            public int Version { get; set; }
            public DateTime Updated { get; set; }
            public int Pending { get; set; }
            public List<StatusItem> Items { get; set; }
        }

        public class StatusItem
        {
            public string Name { get; set; }
            public string Platform { get; set; }
            public string GameId { get; set; }
            public string Status { get; set; }
            public double? Progress { get; set; }
            public double BytesPerSecond { get; set; }
            public double? SecondsLeft { get; set; }
        }

        #endregion

        #region Apagado al terminar

        /// <summary>Programa la acción. Devuelve false si no hay descargas en curso.</summary>
        public bool ArmPower(PowerAction action)
        {
            if (action == PowerAction.None)
            {
                DisarmPower();
                return true;
            }

            if (!Entries.Any(e => e.Status == UpdateStatus.Downloading || (e.Status == UpdateStatus.Pending && e.BytesLeft > 0)))
            {
                return false;
            }

            armed = action;
            idleScansWhileArmed = 0;
            Log.Info("Acción al terminar las descargas: " + action);
            fastTimer?.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            return true;
        }

        public void DisarmPower()
        {
            if (armed != PowerAction.None)
            {
                Log.Info("Acción al terminar las descargas cancelada.");
            }

            armed = PowerAction.None;
            idleScansWhileArmed = 0;
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
        }

        #endregion

        #region Acciones

        /// <summary>
        /// Hace que cada launcher compruebe actualizaciones (reiniciándolo en silencio).
        /// </summary>
        public async Task<List<LauncherActionResult>> UpdateAllAsync(Action<string> progress, UpdatePlatform? only = null)
        {
            var results = new List<LauncherActionResult>();
            if (updatingAll)
            {
                return results;
            }

            if (IsAnyGameRunning())
            {
                results.Add(LauncherActionResult.Skipped(Loc.T("Hay un juego abierto. Ciérralo antes de buscar actualizaciones.")));
                return results;
            }

            updatingAll = true;
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            try
            {
                foreach (var provider in ActiveProviders.Where(p => p.CanForceCheck && (only == null || p.Platform == only.Value)))
                {
                    progress?.Invoke(Loc.T("Comprobando {0}…", provider.DisplayName));
                    try
                    {
                        var r = await Task.Run(() => provider.ForceCheck(cts.Token)).ConfigureAwait(false);
                        Log.Info(r.Message);
                        results.Add(r);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Error al comprobar {provider.DisplayName}.", e);
                        results.Add(LauncherActionResult.Skipped(provider.DisplayName + ": " + e.Message));
                    }
                }

                // Los launchers han vuelto a arrancar: devolver el foco a Playnite
                FocusPlaynite();
            }
            finally
            {
                updatingAll = false;
            }

            FollowUpScans();
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            return results;
        }

        /// <summary>Cambia los juegos de Steam a «Mantener siempre actualizado». Devuelve cuántos.</summary>
        public async Task<int> KeepSteamGamesUpdatedAsync(IEnumerable<SteamProvider.LaunchOnlyGame> games)
        {
            if (IsAnyGameRunning() || updatingAll)
            {
                return -1;
            }

            updatingAll = true;
            Raise(() => Changed?.Invoke(this, EventArgs.Empty));
            int changed;
            try
            {
                var list = games.ToList();
                changed = await Task.Run(() => steam.KeepUpdated(list, dataFolder, cts.Token)).ConfigureAwait(false);
                FocusPlaynite();
            }
            finally
            {
                updatingAll = false;
            }

            FollowUpScans();
            return changed;
        }

        private void FollowUpScans()
        {
            Fire(async () =>
            {
                await DelaySafe(TimeSpan.FromSeconds(45));
                await ScanAsync(ScanReason.AfterUpdateAll);
                await DelaySafe(TimeSpan.FromMinutes(3));
                await ScanAsync(ScanReason.Periodic);
            });
        }

        public bool IsAnyGameRunning()
        {
            if (SteamProvider.IsGameRunning())
            {
                return true;
            }

            var running = false;
            api.MainView.UIDispatcher.Invoke(() =>
            {
                running = api.Database.Games.Any(g => g.IsRunning || g.IsLaunching);
            });
            return running;
        }

        public void FocusPlaynite()
        {
            try
            {
                api.MainView.UIDispatcher.Invoke(() =>
                {
                    var window = api.Dialogs.GetCurrentAppWindow();
                    if (window != null)
                    {
                        window.Activate();
                        Native.ForceForeground(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                    }
                });
            }
            catch (Exception e)
            {
                Log.Warn("No se pudo devolver el foco a Playnite.", e);
            }
        }

        #endregion

        #region Vigilancia

        private void RestartWatchers()
        {
            StopWatchers();
            foreach (var provider in ActiveProviders)
            {
                foreach (var target in provider.WatchTargets)
                {
                    try
                    {
                        var w = new FileSystemWatcher(target.Key, target.Value)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                        };
                        w.Changed += OnFileChanged;
                        w.Created += OnFileChanged;
                        w.Deleted += OnFileChanged;
                        w.Renamed += OnFileChanged;
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                        Log.Debug($"Vigilando {target.Key}\\{target.Value}");
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"No se pudo vigilar {target.Key}.", e);
                    }
                }
            }
        }

        private void StopWatchers()
        {
            foreach (var w in watchers)
            {
                try
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                catch
                {
                }
            }

            watchers.Clear();
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Los launchers escriben muchas veces seguidas: agrupar en un escaneo
            debounceTimer?.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }

        #endregion

        private async Task DelaySafe(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
        }

        private static void Raise(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Log.Error("Error notificando un evento.", e);
            }
        }

        private static void Fire(Func<Task> action)
        {
            Task.Run(async () =>
            {
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Log.Error("Error en tarea en segundo plano.", e);
                }
            });
        }
    }
}
