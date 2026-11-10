using ConsoleUpdateCenter.Core;
using Microsoft.Win32;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ConsoleUpdateCenter.Providers
{
    /// <summary>
    /// Epic Games Launcher. Sin API pública de versiones, solo puede detectar instalaciones
    /// o actualizaciones a medias (bIsIncompleteInstall). Para lo demás, reinicia el launcher
    /// en silencio y deja que él compruebe y descargue.
    /// </summary>
    public class EpicProvider : ILauncherProvider
    {
        private static readonly string[] ProcessNames = { "EpicGamesLauncher", "EpicWebHelper" };

        public UpdatePlatform Platform => UpdatePlatform.Epic;
        public string DisplayName => "Epic Games";
        public Guid LibraryPluginId { get; } = Guid.Parse("00000002-DBD1-46C6-B5D0-B1BA559D10E4");
        public bool CanDetect => true;
        public bool CanForceCheck => true;

        public bool IsInstalled => !string.IsNullOrEmpty(FindExe()) || Directory.Exists(ManifestsFolder);

        public IEnumerable<KeyValuePair<string, string>> WatchTargets
        {
            get
            {
                if (Directory.Exists(ManifestsFolder))
                {
                    yield return new KeyValuePair<string, string>(ManifestsFolder, "*.item");
                }
            }
        }

        private static string ManifestsFolder
        {
            get
            {
                var data = ProcessTools.ReadRegistryString(RegistryHive.LocalMachine,
                    @"SOFTWARE\Epic Games\EpicGamesLauncher", "AppDataPath");
                if (string.IsNullOrEmpty(data))
                {
                    data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        @"Epic\EpicGamesLauncher\Data");
                }

                return Path.Combine(data, "Manifests");
            }
        }

        private static string FindExe()
        {
            var command = ProcessTools.ReadRegistryString(RegistryHive.ClassesRoot,
                @"com.epicgames.launcher\shell\open\command", null);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            return ProcessTools.FirstExisting(
                Native.GetProcessPath("EpicGamesLauncher"),
                ProcessTools.ExeFromCommand(command),
                Path.Combine(pf86, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
                Path.Combine(pf, @"Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"),
                Path.Combine(pf86, @"Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe"));
        }

        public List<UpdateEntry> Scan()
        {
            var result = new List<UpdateEntry>();
            var folder = ManifestsFolder;
            if (!Directory.Exists(folder))
            {
                return result;
            }

            var launcherRunning = ProcessTools.IsRunning("EpicGamesLauncher");
            foreach (var file in Directory.GetFiles(folder, "*.item"))
            {
                try
                {
                    string text;
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs))
                    {
                        text = sr.ReadToEnd();
                    }

                    var item = Serialization.FromJson<EpicManifest>(text);
                    if (item == null || string.IsNullOrEmpty(item.AppName))
                    {
                        continue;
                    }

                    Log.Debug($"Epic {item.AppName} '{item.DisplayName}': incompleto={item.bIsIncompleteInstall} versión={item.AppVersionString}");

                    if (item.bIsIncompleteInstall)
                    {
                        result.Add(new UpdateEntry
                        {
                            Platform = Platform,
                            GameId = item.AppName,
                            Name = item.DisplayName ?? item.AppName,
                            Status = launcherRunning ? UpdateStatus.Downloading : UpdateStatus.Paused
                        });
                    }
                }
                catch (Exception e)
                {
                    Log.Warn($"Epic: no se pudo leer {file}.", e);
                }
            }

            return result;
        }

        public bool IsBusy() => Scan().Any();

        public LauncherActionResult ForceCheck(CancellationToken token)
        {
            var exe = FindExe();
            if (string.IsNullOrEmpty(exe))
            {
                return LauncherActionResult.Skipped(Loc.T("Epic: no se encontró el launcher."));
            }

            if (IsBusy())
            {
                return LauncherActionResult.Skipped(Loc.T("Epic: hay una descarga en curso; no se reinicia."));
            }

            if (ProcessTools.IsRunning(ProcessNames))
            {
                // Al cerrar la ventana Epic se queda en la bandeja: hay que terminarlo
                Log.Info("Epic: cerrando el launcher.");
                ProcessTools.CloseOrKill(TimeSpan.FromSeconds(5), token, ProcessNames);
                Thread.Sleep(2000);
            }

            Log.Info("Epic: iniciando (-silent).");
            ProcessTools.Start(exe, "-silent");
            return LauncherActionResult.Done(Loc.T("Epic: comprobando actualizaciones."));
        }

        public void OpenUpdatesPage()
        {
            var exe = FindExe();
            if (!ProcessTools.Start(exe, null))
            {
                ProcessTools.OpenUri("com.epicgames.launcher://store");
            }
        }

        public class EpicManifest
        {
            public string AppName { get; set; }
            public string DisplayName { get; set; }
            public string AppVersionString { get; set; }
            public bool bIsIncompleteInstall { get; set; }
        }
    }
}
