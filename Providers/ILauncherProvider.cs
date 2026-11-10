using ConsoleUpdateCenter.Core;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ConsoleUpdateCenter.Providers
{
    /// <summary>
    /// Una plataforma (Steam, Epic, GOG, Xbox). El plugin nunca descarga parches por su cuenta:
    /// detecta lo que puede y hace que el propio launcher compruebe y descargue.
    /// </summary>
    public interface ILauncherProvider
    {
        UpdatePlatform Platform { get; }
        string DisplayName { get; }

        /// <summary>Id del plugin de biblioteca de Playnite para esta plataforma.</summary>
        Guid LibraryPluginId { get; }

        /// <summary>El launcher está instalado en este PC.</summary>
        bool IsInstalled { get; }

        /// <summary>Puede detectar actualizaciones juego a juego.</summary>
        bool CanDetect { get; }

        /// <summary>Puede forzar una comprobación (reiniciando el launcher en silencio).</summary>
        bool CanForceCheck { get; }

        /// <summary>Carpetas y filtros a vigilar para refrescar al instante.</summary>
        IEnumerable<KeyValuePair<string, string>> WatchTargets { get; }

        List<UpdateEntry> Scan();

        /// <summary>Hay una descarga en curso (no conviene reiniciar el launcher).</summary>
        bool IsBusy();

        LauncherActionResult ForceCheck(CancellationToken token);

        /// <summary>Abre la página de descargas o actualizaciones del launcher.</summary>
        void OpenUpdatesPage();
    }
}
