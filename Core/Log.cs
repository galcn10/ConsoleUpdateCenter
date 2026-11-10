using Playnite.SDK;
using System;
using System.IO;
using System.Text;

namespace ConsoleUpdateCenter.Core
{
    /// <summary>
    /// Registro del plugin. Todo va al log de Playnite; con el modo diagnóstico activo,
    /// además se escribe un archivo propio (diagnostico.log) en la carpeta de datos del plugin.
    /// </summary>
    internal static class Log
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly object fileLock = new object();
        private const long MaxBytes = 2 * 1024 * 1024;

        public static volatile bool DiagnosticEnabled;
        public static string FilePath { get; private set; }

        public static void Init(string dataFolder)
        {
            try
            {
                Directory.CreateDirectory(dataFolder);
                FilePath = Path.Combine(dataFolder, "diagnostico.log");
            }
            catch (Exception e)
            {
                logger.Error(e, "No se pudo preparar la carpeta del registro.");
            }
        }

        public static void Info(string message)
        {
            logger.Info(message);
            Write("INFO ", message);
        }

        public static void Warn(string message, Exception e = null)
        {
            if (e != null)
            {
                logger.Warn(e, message);
            }
            else
            {
                logger.Warn(message);
            }

            Write("WARN ", e == null ? message : message + " | " + e.GetType().Name + ": " + e.Message);
        }

        public static void Error(string message, Exception e = null)
        {
            if (e != null)
            {
                logger.Error(e, message);
            }
            else
            {
                logger.Error(message);
            }

            Write("ERROR", e == null ? message : message + " | " + e);
        }

        /// <summary>Solo se registra con el modo diagnóstico activo.</summary>
        public static void Debug(string message)
        {
            if (DiagnosticEnabled)
            {
                logger.Info("[Diagnóstico] " + message);
                Write("DEBUG", message);
            }
        }

        private static void Write(string level, string message)
        {
            if (!DiagnosticEnabled || string.IsNullOrEmpty(FilePath))
            {
                return;
            }

            try
            {
                lock (fileLock)
                {
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        var old = FilePath + ".1";
                        if (File.Exists(old))
                        {
                            File.Delete(old);
                        }

                        File.Move(FilePath, old);
                    }

                    File.AppendAllText(
                        FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // El registro nunca debe romper el plugin
            }
        }
    }
}
