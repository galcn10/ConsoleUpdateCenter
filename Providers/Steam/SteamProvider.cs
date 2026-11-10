using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ConsoleUpdateCenter.Providers.Steam
{
    public class SteamProvider : ILauncherProvider
    {
        private readonly SteamLibrary library = new SteamLibrary();

        public UpdatePlatform Platform => UpdatePlatform.Steam;
        public string DisplayName => "Steam";
        public Guid LibraryPluginId { get; } = SteamPluginId;
        public static readonly Guid SteamPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");
        public bool IsInstalled => library.IsAvailable;
        public bool CanDetect => true;
        public bool CanForceCheck => true;

        public IEnumerable<KeyValuePair<string, string>> WatchTargets =>
            library.GetSteamAppsFolders().Select(f => new KeyValuePair<string, string>(f, "appmanifest_*.acf"));

        public List<UpdateEntry> Scan()
        {
            var result = new List<UpdateEntry>();
            foreach (var state in library.ReadAllManifests().Values)
            {
                if (!state.UpdatePending)
                {
                    continue;
                }

                result.Add(new UpdateEntry
                {
                    Platform = Platform,
                    GameId = state.AppId,
                    Name = state.Name,
                    Status = state.Status,
                    BytesTotal = state.BytesToDownload,
                    BytesDone = state.BytesDownloaded,
                    LibraryFolder = state.LibraryFolder
                });

                Log.Debug($"Steam {state.AppId} '{state.Name}': flags={state.StateFlags} build={state.BuildId}->{state.TargetBuildId} " +
                          $"bytes={state.BytesDownloaded}/{state.BytesToDownload} autoupdate={state.AutoUpdateBehavior}");
            }

            return result;
        }

        public bool IsBusy() => Scan().Any(e => e.Status == UpdateStatus.Downloading);

        public LauncherActionResult ForceCheck(CancellationToken token)
        {
            if (SteamLibrary.GetRunningAppId() != 0)
            {
                return LauncherActionResult.Skipped(Loc.T("Steam: hay un juego abierto."));
            }

            if (!Restart(token, null))
            {
                return LauncherActionResult.Skipped(Loc.T("Steam: no se pudo reiniciar."));
            }

            return LauncherActionResult.Done(Loc.T("Steam: comprobando actualizaciones."));
        }

        /// <summary>
        /// Cierra Steam, ejecuta una acción con Steam cerrado (opcional) y lo vuelve a abrir en silencio.
        /// Steam reanuda las descargas al volver.
        /// </summary>
        private bool Restart(CancellationToken token, Action whileClosed)
        {
            if (string.IsNullOrEmpty(library.SteamExe) || !File.Exists(library.SteamExe))
            {
                Log.Warn("Steam: no se encontró steam.exe.");
                return false;
            }

            if (ProcessTools.IsRunning("steam"))
            {
                Log.Info("Steam: cerrando (-shutdown).");
                ProcessTools.Start(library.SteamExe, "-shutdown");
                if (!ProcessTools.WaitForExit(TimeSpan.FromSeconds(60), token, "steam"))
                {
                    Log.Warn("Steam: no se cerró a tiempo.");
                    return false;
                }

                Thread.Sleep(3000);
            }

            try
            {
                whileClosed?.Invoke();
            }
            catch (Exception e)
            {
                Log.Error("Steam: error con Steam cerrado.", e);
            }

            Log.Info("Steam: iniciando (-silent).");
            ProcessTools.Start(library.SteamExe, "-silent");
            return true;
        }

        public void OpenUpdatesPage() => ProcessTools.OpenUri("steam://nav/downloads");

        public static void Validate(string appId) => ProcessTools.OpenUri("steam://validate/" + appId);

        public static bool IsGameRunning() => SteamLibrary.GetRunningAppId() != 0;

        #region Juegos que solo se actualizan al iniciarlos

        public class LaunchOnlyGame
        {
            public string AppId { get; set; }
            public string Name { get; set; }
            public string ManifestPath { get; set; }
        }

        /// <summary>Juegos instalados con «Actualizar solo al iniciar el juego».</summary>
        public List<LaunchOnlyGame> GetLaunchOnlyGames()
        {
            return library.ReadAllManifests().Values
                .Where(s => s.UpdatesOnLaunchOnly)
                .Select(s => new LaunchOnlyGame { AppId = s.AppId, Name = s.Name, ManifestPath = s.ManifestPath })
                .OrderBy(g => g.Name)
                .ToList();
        }

        private static readonly Regex AutoUpdateLine = new Regex(
            "(\"AutoUpdateBehavior\"\\s+\")1(\")", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Cambia los juegos indicados a «Mantener siempre actualizado». Steam tiene que estar cerrado:
        /// se cierra, se editan los manifiestos (con copia de seguridad) y se vuelve a abrir.
        /// Devuelve cuántos juegos se cambiaron.
        /// </summary>
        public int KeepUpdated(IEnumerable<LaunchOnlyGame> games, string backupRoot, CancellationToken token)
        {
            var list = games.ToList();
            if (list.Count == 0)
            {
                return 0;
            }

            if (SteamLibrary.GetRunningAppId() != 0)
            {
                Log.Warn("Steam: hay un juego abierto; no se cambian los ajustes de actualización.");
                return 0;
            }

            var changed = 0;
            var ok = Restart(token, () =>
            {
                var backupDir = Path.Combine(backupRoot, "steam-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backupDir);

                foreach (var g in list)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(g.ManifestPath) || !File.Exists(g.ManifestPath))
                        {
                            continue;
                        }

                        var text = File.ReadAllText(g.ManifestPath, Encoding.UTF8);
                        if (!AutoUpdateLine.IsMatch(text))
                        {
                            continue;
                        }

                        File.Copy(g.ManifestPath, Path.Combine(backupDir, Path.GetFileName(g.ManifestPath)), true);
                        var updated = AutoUpdateLine.Replace(text, "${1}0${2}", 1);
                        File.WriteAllText(g.ManifestPath, updated, new UTF8Encoding(false));
                        changed++;
                        Log.Info($"Steam: {g.Name} ({g.AppId}) ahora se mantiene actualizado.");
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Steam: no se pudo cambiar {g.Name}.", e);
                    }
                }

                Log.Info($"Steam: copia de seguridad de los manifiestos en {backupDir}");
            });

            return ok ? changed : 0;
        }

        #endregion
    }
}
