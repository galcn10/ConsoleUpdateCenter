using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ConsoleUpdateCenter.Providers
{
    /// <summary>
    /// Xbox / Microsoft Store. Las actualizaciones las gestiona la Store; el plugin solo
    /// puede abrir su página "Descargas y actualizaciones".
    /// </summary>
    public class XboxProvider : ILauncherProvider
    {
        public UpdatePlatform Platform => UpdatePlatform.Xbox;
        public string DisplayName => "Xbox / Microsoft Store";
        public Guid LibraryPluginId { get; } = Guid.Parse("7E4FBB5E-2AE3-48D4-8BA0-6B30E7A4E287");
        public bool CanDetect => false;
        public bool CanForceCheck => false;
        public bool IsInstalled => Environment.OSVersion.Version.Major >= 10;

        public IEnumerable<KeyValuePair<string, string>> WatchTargets => Enumerable.Empty<KeyValuePair<string, string>>();

        public List<UpdateEntry> Scan() => new List<UpdateEntry>();

        public bool IsBusy() => false;

        public LauncherActionResult ForceCheck(CancellationToken token) =>
            LauncherActionResult.Skipped(Loc.T("Xbox: se revisa desde la Microsoft Store."));

        public void OpenUpdatesPage() => ProcessTools.OpenUri("ms-windows-store://downloadsandupdates");
    }
}
