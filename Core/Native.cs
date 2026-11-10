using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleUpdateCenter.Core
{
    internal static class Native
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        /// <summary>
        /// Ventana que nunca toma el foco ni recibe clics (para avisos flotantes).
        /// </summary>
        public static void MakeOverlayWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            var ex = GetWindowLong32(hWnd, GWL_EXSTYLE);
            SetWindowLong32(hWnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        /// <summary>Suspende el equipo.</summary>
        public static bool Sleep() => SetSuspendState(false, false, false);

        /// <summary>Apaga el equipo.</summary>
        public static void Shutdown()
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });
        }

        private const int SW_MINIMIZE = 6;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// Minimiza las ventanas visibles con título de los procesos indicados
        /// (para que un launcher no se quede encima de Playnite en modo consola).
        /// </summary>
        public static int MinimizeWindowsOf(params string[] processNames)
        {
            var pids = new HashSet<uint>(processNames
                .SelectMany(n => Process.GetProcessesByName(n))
                .Select(p => (uint)p.Id));

            if (pids.Count == 0)
            {
                return 0;
            }

            int count = 0;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pids.Contains(pid) && IsWindowVisible(hWnd) && GetWindowTextLength(hWnd) > 0)
                {
                    ShowWindow(hWnd, SW_MINIMIZE);
                    count++;
                }

                return true;
            }, IntPtr.Zero);

            return count;
        }

        /// <summary>
        /// Trae una ventana al frente aunque otro proceso tenga el foco.
        /// </summary>
        public static void ForceForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            var fg = GetForegroundWindow();
            var fgThread = GetWindowThreadProcessId(fg, out _);
            var thisThread = GetCurrentThreadId();
            if (fgThread != thisThread)
            {
                AttachThreadInput(thisThread, fgThread, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(thisThread, fgThread, false);
            }
            else
            {
                SetForegroundWindow(hWnd);
            }
        }

        /// <summary>
        /// Ruta del ejecutable de un proceso en marcha (funciona aunque sea de otra arquitectura).
        /// </summary>
        public static string GetProcessPath(string processName)
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (h == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(h);
                }
            }

            return null;
        }
    }
}
