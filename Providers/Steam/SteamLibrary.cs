using Microsoft.Win32;
using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConsoleUpdateCenter.Providers.Steam
{
    /// <summary>
    /// Localiza la instalación de Steam y sus bibliotecas, y lee los appmanifest.
    /// </summary>
    public class SteamLibrary
    {
        private static readonly Regex PathEntry = new Regex(
            "^\\s*\"path\"\\s+\"(?<p>[^\"]+)\"",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string SteamKey = @"Software\Valve\Steam";

        public string SteamPath { get; }
        public string SteamExe { get; }

        public bool IsAvailable => !string.IsNullOrEmpty(SteamPath) && Directory.Exists(SteamPath);

        public SteamLibrary()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SteamKey))
                {
                    SteamPath = NormalizePath(key?.GetValue("SteamPath") as string);
                    SteamExe = NormalizePath(key?.GetValue("SteamExe") as string);
                }
            }
            catch (Exception e)
            {
                Log.Error("No se pudo leer la ruta de Steam del registro.", e);
            }

            if (string.IsNullOrEmpty(SteamExe) && !string.IsNullOrEmpty(SteamPath))
            {
                SteamExe = Path.Combine(SteamPath, "steam.exe");
            }
        }

        /// <summary>
        /// AppID del juego de Steam en ejecución (0 si no hay ninguno).
        /// </summary>
        public static int GetRunningAppId()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SteamKey))
                {
                    var value = key?.GetValue("RunningAppID");
                    return value is int i ? i : 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Carpetas steamapps de todas las bibliotecas (puede haber varios discos).
        /// </summary>
        public List<string> GetSteamAppsFolders()
        {
            var result = new List<string>();
            if (!IsAvailable)
            {
                return result;
            }

            var roots = new List<string> { SteamPath };
            var vdf = Path.Combine(SteamPath, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                {
                    var text = File.ReadAllText(vdf);
                    foreach (Match m in PathEntry.Matches(text))
                    {
                        roots.Add(NormalizePath(m.Groups["p"].Value.Replace("\\\\", "\\")));
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("No se pudo leer libraryfolders.vdf.", e);
            }

            foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var steamApps = Path.Combine(root, "steamapps");
                if (Directory.Exists(steamApps)
                    && !result.Contains(steamApps, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(steamApps);
                }
            }

            return result;
        }

        /// <summary>
        /// Lee todos los appmanifest. Clave: AppID.
        /// </summary>
        public Dictionary<string, SteamAppState> ReadAllManifests()
        {
            var states = new Dictionary<string, SteamAppState>();
            foreach (var folder in GetSteamAppsFolders())
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder, "appmanifest_*.acf");
                }
                catch (Exception e)
                {
                    Log.Error($"No se pudo listar {folder}.", e);
                    continue;
                }

                foreach (var file in files)
                {
                    try
                    {
                        // Steam puede estar escribiendo el archivo: abrir con lectura compartida
                        string text;
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs))
                        {
                            text = sr.ReadToEnd();
                        }

                        var state = SteamAppState.Parse(text);
                        if (state != null)
                        {
                            state.LibraryFolder = folder;
                            state.ManifestPath = file;
                            states[state.AppId] = state;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"No se pudo leer {file}.", e);
                    }
                }
            }

            return states;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path.Replace('/', '\\'));
            }
            catch
            {
                return path;
            }
        }
    }
}
