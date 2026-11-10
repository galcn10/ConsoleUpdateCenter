using ConsoleUpdateCenter.Core;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ConsoleUpdateCenter.Providers
{
    internal static class ProcessTools
    {
        public static bool IsRunning(params string[] names) =>
            names.Any(n => Process.GetProcessesByName(n).Length > 0);

        /// <summary>Espera a que se cierren todos los procesos. False si se agota el tiempo.</summary>
        public static bool WaitForExit(TimeSpan timeout, CancellationToken token, params string[] names)
        {
            var deadline = DateTime.Now + timeout;
            while (IsRunning(names))
            {
                if (DateTime.Now > deadline || token.IsCancellationRequested)
                {
                    return false;
                }

                Thread.Sleep(500);
            }

            return true;
        }

        /// <summary>Intenta cerrar con normalidad y, si no responde, termina los procesos.</summary>
        public static void CloseOrKill(TimeSpan grace, CancellationToken token, params string[] names)
        {
            foreach (var p in names.SelectMany(n => Process.GetProcessesByName(n)))
            {
                try
                {
                    p.CloseMainWindow();
                }
                catch
                {
                }
            }

            if (WaitForExit(grace, token, names))
            {
                return;
            }

            foreach (var p in names.SelectMany(n => Process.GetProcessesByName(n)))
            {
                try
                {
                    Log.Debug($"Terminando proceso {p.ProcessName} ({p.Id}).");
                    p.Kill();
                }
                catch (Exception e)
                {
                    Log.Warn($"No se pudo terminar {p.ProcessName}.", e);
                }
            }

            WaitForExit(TimeSpan.FromSeconds(15), token, names);
        }

        public static bool Start(string exe, string args)
        {
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo(exe, args ?? string.Empty)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });
            return true;
        }

        public static void OpenUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                Log.Warn($"No se pudo abrir {uri}.", e);
            }
        }

        public static string ReadRegistryString(RegistryHive hive, string key, string value)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var k = root.OpenSubKey(key))
                    {
                        if (k?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s))
                        {
                            return s;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>Extrae la ruta del exe de un comando tipo "\"C:\x\y.exe\" %1".</summary>
        public static string ExeFromCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            command = command.Trim();
            if (command.StartsWith("\""))
            {
                var end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }

            var idx = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return idx > 0 ? command.Substring(0, idx + 4) : null;
        }

        public static string FirstExisting(params string[] paths) =>
            paths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
    }
}
