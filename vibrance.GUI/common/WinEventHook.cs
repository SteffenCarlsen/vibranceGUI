using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    internal sealed class WinEventHook
    {
        private const uint ForegroundEvent = 0x0003;
        private static WinEventHook _instance;
        private readonly WinEventDelegate _callback;
        private IntPtr _handle;
        private readonly Timer _reconcileTimer;
        private IntPtr _lastWindow;
        private uint _lastProcess;
        private string _lastDisplay;
        private bool _lastMinimized;
        private WinEventHookEventArgs _lastSnapshot;
        private static readonly System.Collections.Generic.HashSet<uint> DeniedImageQueries = new System.Collections.Generic.HashSet<uint>();
        public event EventHandler<WinEventHookEventArgs> WinEventHookHandler;

        private WinEventHook()
        {
            _callback = OnWinEvent;
            // Subscribe to one exact event. A range through MINIMIZEEND includes mouse
            // CAPTURESTART/END and causes driver calls on every click (upstream #156).
            _handle = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero, _callback, 0, 0, 0);
            if (_handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _reconcileTimer = new Timer { Interval = 1000 };
            _reconcileTimer.Tick += ReconcileForeground;
            _reconcileTimer.Start();
        }

        public static WinEventHook GetInstance() => _instance ??= new WinEventHook();

        public void RemoveWinEventHook()
        {
            if (WinEventHookHandler != null || _handle == IntPtr.Zero) return;
            _reconcileTimer.Stop();
            _reconcileTimer.Dispose();
            // WinEvent hooks must be removed on their installing (UI) thread, so an owning
            // SafeHandle finalizer on the GC thread cannot correctly release this resource.
            if (!UnhookWinEvent(_handle)) VibranceGUI.Log(new Win32Exception(Marshal.GetLastWin32Error()));
            _handle = IntPtr.Zero;
            _instance = null;
            GC.KeepAlive(_callback);
        }

        private void ReconcileForeground(object sender, EventArgs args)
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                GetWindowThreadProcessId(window, out uint process);
                bool minimized = window != IntPtr.Zero && IsIconic(window);
                string display = window == IntPtr.Zero ? null : Screen.FromHandle(window).DeviceName;
                if (_lastSnapshot == null || window != _lastWindow || process != _lastProcess || display != _lastDisplay || minimized != _lastMinimized)
                {
                    _lastWindow = window; _lastProcess = process; _lastDisplay = display; _lastMinimized = minimized;
                    _lastSnapshot = ReadForeground();
                }
                // Successful transitions are skipped by the controller. Pending failed restores
                // are retried once a second, using the cached process snapshot instead of reopening it.
                WinEventHookHandler?.Invoke(this, _lastSnapshot);
            }
            catch (Exception ex) { VibranceGUI.Log(ex); }
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
        {
            if (eventType != ForegroundEvent || hwnd != GetForegroundWindow()) return;
            try { WinEventHookHandler?.Invoke(this, ReadForeground()); }
            catch (Exception ex) { VibranceGUI.Log(ex); } // Exceptions must never cross a native callback.
        }

        internal static WinEventHookEventArgs ReadForeground()
        {
            IntPtr window = GetForegroundWindow();
            var result = new WinEventHookEventArgs { Handle = window, ProcessName = string.Empty, WindowText = string.Empty };
            if (window == IntPtr.Zero || IsIconic(window)) return result;
            GetWindowThreadProcessId(window, out uint processId);
            result.ProcessId = processId;
            if (processId == 0) return result;
            try
            {
                using (Process process = Process.GetProcessById((int)processId)) result.ProcessName = process.ProcessName;
                using (SafeProcessHandle handle = OpenProcess(0x1000, 0, processId)) // QUERY_LIMITED_INFORMATION.
                {
                    if (!handle.IsInvalid)
                    {
                        var path = new char[32768];
                        uint size = (uint)path.Length;
                        if (QueryFullProcessImageNameW(handle, 0, path, ref size)) result.ExecutablePath = new string(path, 0, (int)size);
                    }
                }
                if (result.ExecutablePath == null)
                    lock (DeniedImageQueries)
                        if (DeniedImageQueries.Count < 256 && DeniedImageQueries.Add(processId))
                            VibranceGUI.Log(new InvalidOperationException($"Windows denied the executable path for {result.ProcessName} (PID {processId}); profile matching falls back to its process name."));
                int length = Math.Min(GetWindowTextLengthW(window), 65535);
                var title = new char[length + 1];
                int copied = GetWindowTextW(window, title, title.Length);
                result.WindowText = new string(title, 0, copied);
                result.MainWindowTitle = result.WindowText;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
            return result;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetWindowTextLengthW(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int GetWindowTextW(IntPtr window, [Out] char[] text, int count);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, int inherit, uint process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] name, ref uint size);
    }
}
