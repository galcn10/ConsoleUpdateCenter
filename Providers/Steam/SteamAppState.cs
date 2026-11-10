using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ConsoleUpdateCenter.Core;

namespace ConsoleUpdateCenter.Providers.Steam
{
    /// <summary>
    /// Estado de un juego leído de su appmanifest_*.acf.
    /// </summary>
    public class SteamAppState
    {
        // Bits de StateFlags (EAppState de Steam)
        private const long FlagUpdateRequired = 2;
        private const long FlagUpdateRunning = 256;
        private const long FlagUpdatePaused = 512;
        private const long FlagUpdateStarted = 1024;
        private const long FlagDownloading = 1048576;

        private static readonly Regex KeyValue = new Regex(
            "^\\s*\"(?<k>[^\"]+)\"\\s+\"(?<v>[^\"]*)\"",
            RegexOptions.Multiline | RegexOptions.Compiled);

        public string AppId { get; private set; }

        /// <summary>Carpeta steamapps donde está el manifiesto.</summary>
        public string LibraryFolder { get; set; }

        /// <summary>Ruta del appmanifest_*.acf.</summary>
        public string ManifestPath { get; set; }
        public string Name { get; private set; }
        public long StateFlags { get; private set; }
        public string BuildId { get; private set; }
        public string TargetBuildId { get; private set; }
        public long BytesToDownload { get; private set; }
        public long BytesDownloaded { get; private set; }

        /// <summary>0 = mantener actualizado, 1 = solo al iniciar, 2 = prioridad alta.</summary>
        public int AutoUpdateBehavior { get; private set; }

        public bool IsDownloading =>
            (StateFlags & (FlagDownloading | FlagUpdateRunning | FlagUpdateStarted)) != 0;

        public bool IsPaused => (StateFlags & FlagUpdatePaused) != 0;

        public bool UpdatesOnLaunchOnly => AutoUpdateBehavior == 1;

        public bool UpdatePending =>
            (StateFlags & FlagUpdateRequired) != 0
            || IsDownloading
            || IsPaused
            || (!string.IsNullOrEmpty(TargetBuildId) && TargetBuildId != "0" && TargetBuildId != BuildId)
            || (BytesToDownload > 0 && BytesDownloaded < BytesToDownload);

        public UpdateStatus Status =>
            IsDownloading ? UpdateStatus.Downloading
            : IsPaused ? UpdateStatus.Paused
            : UpdatesOnLaunchOnly ? UpdateStatus.LaunchOnly
            : UpdateStatus.Pending;

        public static SteamAppState Parse(string acfText)
        {
            // Primera aparición de cada clave: las que usamos solo están en el nivel superior
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in KeyValue.Matches(acfText))
            {
                var key = m.Groups["k"].Value;
                if (!values.ContainsKey(key))
                {
                    values[key] = m.Groups["v"].Value;
                }
            }

            if (!values.TryGetValue("appid", out var appId) || string.IsNullOrEmpty(appId))
            {
                return null;
            }

            return new SteamAppState
            {
                AppId = appId,
                Name = Get(values, "name") ?? appId,
                StateFlags = GetLong(values, "StateFlags"),
                BuildId = Get(values, "buildid"),
                TargetBuildId = Get(values, "TargetBuildID"),
                BytesToDownload = GetLong(values, "BytesToDownload"),
                BytesDownloaded = GetLong(values, "BytesDownloaded"),
                AutoUpdateBehavior = (int)GetLong(values, "AutoUpdateBehavior")
            };
        }

        private static string Get(Dictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) ? v : null;

        private static long GetLong(Dictionary<string, string> d, string key) =>
            d.TryGetValue(key, out var v) && long.TryParse(v, out var n) ? n : 0;
    }
}
