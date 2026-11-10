using System;

namespace ConsoleUpdateCenter.Core
{
    public enum UpdatePlatform
    {
        Steam,
        Epic,
        Gog,
        Xbox
    }

    public enum UpdateStatus
    {
        /// <summary>Hay versión nueva y el launcher aún no la está descargando.</summary>
        Pending,
        /// <summary>Descargando o instalando.</summary>
        Downloading,
        /// <summary>Descarga en pausa.</summary>
        Paused,
        /// <summary>Hay versión nueva, pero el juego está configurado para actualizarse solo al iniciarlo.</summary>
        LaunchOnly
    }

    /// <summary>
    /// Un juego con actualización pendiente o en curso.
    /// </summary>
    public class UpdateEntry
    {
        public UpdatePlatform Platform { get; set; }

        /// <summary>Id del juego en su plataforma (AppID de Steam, AppName de Epic...).</summary>
        public string GameId { get; set; }

        public string Name { get; set; }
        public UpdateStatus Status { get; set; }
        public long BytesTotal { get; set; }
        public long BytesDone { get; set; }

        /// <summary>Id del juego en la base de datos de Playnite (si se ha encontrado).</summary>
        public Guid? PlayniteId { get; set; }

        /// <summary>Carpeta de la biblioteca donde está instalado (para comprobar el espacio libre).</summary>
        public string LibraryFolder { get; set; }

        /// <summary>Velocidad de descarga estimada (bytes/s), 0 si no se conoce.</summary>
        public double BytesPerSecond { get; set; }

        /// <summary>Espacio libre en el disco del juego (bytes), -1 si no se conoce.</summary>
        public long FreeBytes { get; set; } = -1;

        public long BytesLeft => BytesTotal > BytesDone ? BytesTotal - BytesDone : 0;

        /// <summary>La descarga no cabe en el disco del juego.</summary>
        public bool NoSpace => FreeBytes >= 0 && BytesLeft > 0 && BytesLeft > FreeBytes;

        /// <summary>Tiempo restante estimado, o null si no se conoce.</summary>
        public TimeSpan? Remaining => BytesPerSecond > 1024 && BytesLeft > 0
            ? TimeSpan.FromSeconds(BytesLeft / BytesPerSecond)
            : (TimeSpan?)null;

        public string Key => Platform + "|" + GameId;

        /// <summary>Progreso 0..1, o null si no se conoce.</summary>
        public double? Progress => BytesTotal > 0 ? Math.Min(1.0, (double)BytesDone / BytesTotal) : (double?)null;
    }

    /// <summary>
    /// Resultado de una acción sobre un launcher.
    /// </summary>
    public class LauncherActionResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; }

        public static LauncherActionResult Done(string message) => new LauncherActionResult { Ok = true, Message = message };
        public static LauncherActionResult Skipped(string message) => new LauncherActionResult { Ok = false, Message = message };
    }
}
