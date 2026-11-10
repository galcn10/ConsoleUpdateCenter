using ConsoleUpdateCenter.Core;
using System;
using System.IO;

namespace ConsoleUpdateCenter.UI
{
    /// <summary>Textos de tamaños, velocidades, tiempos y estados.</summary>
    internal static class Format
    {
        public static string Bytes(long bytes)
        {
            if (bytes >= 1L << 30)
            {
                return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
            }

            if (bytes >= 1L << 20)
            {
                return (bytes / (double)(1L << 20)).ToString("0") + " MB";
            }

            return Math.Max(0, bytes / 1024).ToString("0") + " KB";
        }

        public static string Speed(double bytesPerSecond)
        {
            if (bytesPerSecond <= 0)
            {
                return null;
            }

            return bytesPerSecond >= 1 << 20
                ? (bytesPerSecond / (1 << 20)).ToString("0.0") + " MB/s"
                : (bytesPerSecond / 1024).ToString("0") + " KB/s";
        }

        public static string Remaining(TimeSpan? t)
        {
            if (!t.HasValue)
            {
                return null;
            }

            var v = t.Value;
            if (v.TotalMinutes < 1)
            {
                return Loc.T("menos de 1 min");
            }

            if (v.TotalHours < 1)
            {
                return Loc.T("quedan {0} min", Math.Ceiling(v.TotalMinutes));
            }

            return Loc.T("quedan {0} h {1} min", (int)v.TotalHours, v.Minutes);
        }

        public static string Drive(string folder)
        {
            try
            {
                return Path.GetPathRoot(folder)?.TrimEnd('\\');
            }
            catch
            {
                return null;
            }
        }

        public static string Percent(double? p) => p.HasValue ? (p.Value * 100).ToString("0") + " %" : null;

        public static string StatusTitle(UpdateEntry e)
        {
            switch (e.Status)
            {
                case UpdateStatus.Downloading: return Loc.T("Descargando");
                case UpdateStatus.Paused: return Loc.T("En pausa");
                case UpdateStatus.LaunchOnly: return Loc.T("Se actualizará al iniciarlo");
                default: return Loc.T("Actualización disponible");
            }
        }

        /// <summary>Línea de detalle: estado · tamaño · velocidad · tiempo.</summary>
        public static string StatusLine(UpdateEntry e)
        {
            var parts = new System.Collections.Generic.List<string> { StatusTitle(e) };
            if (e.NoSpace)
            {
                parts.Add(Loc.T("sin espacio en {0}", Drive(e.LibraryFolder)));
            }

            if (e.BytesTotal > 0)
            {
                parts.Add(Loc.T("{0} de {1}", Bytes(e.BytesDone), Bytes(e.BytesTotal)));
            }

            if (e.Status == UpdateStatus.Downloading)
            {
                var sp = Speed(e.BytesPerSecond);
                if (sp != null)
                {
                    parts.Add(sp);
                }

                var rem = Remaining(e.Remaining);
                if (rem != null)
                {
                    parts.Add(rem);
                }
            }

            return string.Join(" · ", parts);
        }

        public static string StatusIcon(UpdateEntry e)
        {
            if (e.NoSpace)
            {
                return "hard-drive";
            }

            switch (e.Status)
            {
                case UpdateStatus.Paused: return "pause";
                case UpdateStatus.LaunchOnly: return "clock";
                default: return "download";
            }
        }
    }
}
