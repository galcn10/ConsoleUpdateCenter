using ConsoleUpdateCenter.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ConsoleUpdateCenter.Providers
{
    /// <summary>
    /// GOG Galaxy. No expone el estado de actualizaciones, así que no hay detección por juego:
    /// solo puede reiniciar Galaxy para que compruebe y descargue (si tiene las
    /// actualizaciones automáticas activadas en sus ajustes).
    /// </summary>
    public class GogProvider : ILauncherProvider
    {
        // El servicio GalaxyClientService no se toca
        private static readonly string[] ProcessNames = { "GalaxyClient", "GalaxyClient Helper", "GOG Galaxy Notifications Renderer" };

        public UpdatePlatform Platform => UpdatePlatform.Gog;
        public string DisplayName => "GOG Galaxy";
        public Guid LibraryPluginId { get; } = Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E");
        public bool CanDetect => false;
        public bool CanForceCheck => true;
        public bool IsInstalled => !string.IsNullOrEmpty(FindExe());

        public IEnumerable<KeyValuePair<string, string>> WatchTargets => Enumerable.Empty<KeyValuePair<string, string>>();

        private static string FindExe()
        {
            var clientDir = ProcessTools.ReadRegistryString(RegistryHive.LocalMachine,
                @"SOFTWARE\GOG.com\GalaxyClient\paths", "client");
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return ProcessTools.FirstExisting(
                Native.GetProcessPath("GalaxyClient"),
                string.IsNullOrEmpty(clientDir) ? null : Path.Combine(clientDir, "GalaxyClient.exe"),
                Path.Combine(pf86, @"GOG Galaxy\GalaxyClient.exe"));
        }

        public List<UpdateEntry> Scan() => new List<UpdateEntry>();

        public bool IsBusy() => false;

        public LauncherActionResult ForceCheck(CancellationToken token)
        {
            var exe = FindExe();
            if (string.IsNullOrEmpty(exe))
            {
                return LauncherActionResult.Skipped(Loc.T("GOG: no se encontró GOG Galaxy."));
            }

            if (ProcessTools.IsRunning(ProcessNames))
            {
                Log.Info("GOG: cerrando Galaxy.");
                ProcessTools.CloseOrKill(TimeSpan.FromSeconds(8), token, ProcessNames);
                Thread.Sleep(2000);
            }

            Log.Info("GOG: iniciando Galaxy.");
            ProcessTools.Start(exe, null);

            // Galaxy abre su ventana al arrancar: se minimiza en cuanto aparece
            for (int i = 0; i < 30 && !token.IsCancellationRequested; i++)
            {
                Thread.Sleep(1000);
                if (Native.MinimizeWindowsOf("GalaxyClient") > 0)
                {
                    Log.Debug("GOG: ventana minimizada.");
                    break;
                }
            }

            return LauncherActionResult.Done(Loc.T("GOG: Galaxy reiniciado para comprobar actualizaciones."));
        }

        public void OpenUpdatesPage()
        {
            if (!ProcessTools.Start(FindExe(), null))
            {
                ProcessTools.OpenUri("goggalaxy://openGalaxyClient");
            }
        }
    }
}
