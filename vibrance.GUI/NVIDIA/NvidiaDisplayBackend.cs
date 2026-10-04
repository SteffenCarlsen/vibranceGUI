using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using vibrance.GUI.common;

namespace vibrance.GUI.NVIDIA
{
    internal sealed class NvidiaDisplayBackend : IDisplayVibranceBackend
    {
        // Public interface IDs: NVIDIA/nvapi/nvapi_interface.h.
        // DVC is a private driver API, independently documented by NvAPIWrapper's
        // Native/Delegates/Display.cs and PrivateDisplayDVCInfo(Ex) structures.
        // Keep the legacy API fallback and treat unsupported drivers as a capability failure.
        internal const int MaximumDisplays = 256; // NVAPI_SYSTEM_MAX_DISPLAYS (64 * 4).
        private readonly Dictionary<string, Display> _displays = new Dictionary<string, Display>(StringComparer.OrdinalIgnoreCase);
        private DriverLibrary _library;
        private ApiCall _unload;
        private GetDvc _getDvc;
        private SetDvc _setDvc;
        private GetDvcEx _getDvcEx;
        private SetDvcEx _setDvcEx;
        private GetDisplayHandle _getDisplayHandle;
        private bool _initialized;

        public IReadOnlyList<string> DisplayNames => _displays.Keys.ToArray();
        public string GpuName { get; private set; } = "NVIDIA";
        public string InitializationError { get; private set; } = string.Empty;

        public NvidiaDisplayBackend()
        {
            try
            {
                _library = DriverLibrary.Load(Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll");
                var query = _library.GetExport<QueryInterface>("nvapi_QueryInterface");
                T Resolve<T>(uint id, bool required = true) where T : Delegate
                {
                    IntPtr address = query(id);
                    if (address == IntPtr.Zero)
                    {
                        if (required) throw new NotSupportedException($"NVIDIA driver interface 0x{id:X8} is unavailable.");
                        return null;
                    }
                    return Marshal.GetDelegateForFunctionPointer<T>(address);
                }

                var initialize = Resolve<ApiCall>(0x0150e828);
                _unload = Resolve<ApiCall>(0xd22bdd7e);
                Check(initialize(), "initialize NVAPI");
                _initialized = true;
                var enumerate = Resolve<EnumDisplay>(0x9abdd40d);
                var getName = Resolve<GetName>(0x22a78b05);
                _getDisplayHandle = Resolve<GetDisplayHandle>(0x35c29134);
                _getDvc = Resolve<GetDvc>(0x4085de45, false);
                _setDvc = Resolve<SetDvc>(0x172409b4, false);
                _getDvcEx = Resolve<GetDvcEx>(0x0e45002d, false);
                _setDvcEx = Resolve<SetDvcEx>(0x4a82c2b1, false);
                if ((_getDvc == null || _setDvc == null) && (_getDvcEx == null || _setDvcEx == null))
                    throw new NotSupportedException("The NVIDIA driver does not expose Digital Vibrance control.");

                var seen = new HashSet<IntPtr>();
                for (uint index = 0; index < MaximumDisplays; index++)
                {
                    int status = enumerate(index, out IntPtr handle);
                    if (status == -7) break; // NVAPI_END_ENUMERATION, not an invented handle sentinel.
                    if (status != 0) break;
                    if (handle == IntPtr.Zero || !seen.Add(handle)) continue;
                    var buffer = new byte[64]; // NvAPI_ShortString.
                    if (getName(handle, buffer) != 0) continue;
                    string name = ReadString(buffer);
                    if (string.IsNullOrEmpty(name)) continue;
                    var display = new Display { Handle = handle };
                    if (TryRead(display, out _, out _, out _, out _)) _displays.TryAdd(name, display);
                }
                if (_displays.Count == 0)
                    InitializationError = "NVIDIA: no attached display supports Digital Vibrance. Connect the display to the NVIDIA GPU; check HDR and the NVIDIA Control Panel's Reference Mode if color controls are unavailable.";

                var enumGpus = Resolve<EnumGpus>(0xe5ac921f, false);
                var gpuName = Resolve<GetName>(0xceee8e9f, false);
                if (enumGpus != null && gpuName != null)
                {
                    var handles = new IntPtr[64];
                    if (enumGpus(handles, out uint count) == 0 && count > 0 && count <= handles.Length)
                    {
                        var buffer = new byte[64];
                        if (gpuName(handles[0], buffer) == 0) GpuName = ReadString(buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                InitializationError = "NVIDIA: " + ex.Message;
                _displays.Clear();
                Dispose();
            }
        }

        public bool SetLevel(string deviceName, int level)
        {
            if (!_initialized || !_displays.TryGetValue(deviceName, out Display display)) return false;
            level = Math.Clamp(level, 0, NvidiaDynamicVibranceProxy.NvapiMaxLevel);
            // Duplicate foreground events must not make even a driver readback call (#156).
            if (display.LastRequested == level) return true;
            if (!TryRead(display, out int current, out int minimum, out int maximum, out int neutral))
            {
                if (_getDisplayHandle(deviceName, out IntPtr handle) != 0) return false;
                display.Handle = handle; // Recover handles invalidated by a modeset/driver restart.
                if (!TryRead(display, out current, out minimum, out maximum, out neutral)) return false;
            }
            int target = MapLevel(level, neutral, maximum);
            target = Math.Clamp(target, minimum, maximum);
            if (target != current)
            {
                int status = WriteNative(display, target);
                if (status != 0)
                {
                    VibranceGUI.Log(new InvalidOperationException($"NVIDIA Digital Vibrance on {deviceName} failed (NVAPI {status}). Check Reference Mode/HDR if driver color controls are disabled."));
                    return false;
                }
            }
            display.LastRequested = level;
            return true;
        }

        internal static int MapLevel(int legacyLevel, int neutral, int maximum) =>
            neutral + (int)Math.Round((maximum - neutral) * (Math.Clamp(legacyLevel, 0, 63) / 63.0), MidpointRounding.AwayFromZero);

        public void InvalidateCache() { foreach (var display in _displays.Values) display.LastRequested = null; }

        internal NativeVibranceSmokeResult TestWriteAndRestore(string deviceName)
        {
            if (!_displays.TryGetValue(deviceName, out Display display) ||
                !TryRead(display, out int original, out int minimum, out int maximum, out _))
                throw new InvalidOperationException("The display's native vibrance could not be read.");
            int target = original < maximum ? original + 1 : original - 1;
            if (target < minimum) throw new InvalidOperationException("The display has no adjustable vibrance range.");
            var result = new NativeVibranceSmokeResult { Display = deviceName, Original = original, Target = target };
            try
            {
                result.SetStatus = WriteNative(display, target);
                result.ReadSucceeded = TryRead(display, out int observed, out _, out _, out _);
                result.Observed = observed;
            }
            finally
            {
                // Restore the exact native value, including levels below neutral. Never round
                // a baseline through the legacy 0..63 UI scale to perform this cleanup.
                result.RestoreStatus = WriteNative(display, original);
                result.RestoreReadSucceeded = TryRead(display, out int restored, out _, out _, out _);
                result.Restored = restored;
                InvalidateCache();
            }
            return result;
        }

        private int WriteNative(Display display, int target)
        {
            if (display.UseExtended)
            {
                var info = NewDvcEx();
                info.currentLevel = target;
                return _setDvcEx(display.Handle, 0, ref info);
            }
            return _setDvc(display.Handle, 0, target);
        }

        private bool TryRead(Display display, out int current, out int minimum, out int maximum, out int neutral)
        {
            if (_getDvcEx != null && _setDvcEx != null)
            {
                var info = NewDvcEx();
                if (_getDvcEx(display.Handle, 0, ref info) == 0 && info.maxLevel > info.defaultLevel && info.defaultLevel >= info.minLevel)
                {
                    display.UseExtended = true;
                    current = info.currentLevel; minimum = info.minLevel; maximum = info.maxLevel; neutral = info.defaultLevel;
                    return true;
                }
            }
            if (_getDvc != null && _setDvc != null)
            {
                var info = new NvDisplayDvcInfo { version = (uint)Marshal.SizeOf<NvDisplayDvcInfo>() | 0x10000 };
                if (_getDvc(display.Handle, 0, ref info) == 0 && info.maxLevel > info.minLevel)
                {
                    display.UseExtended = false;
                    current = info.currentLevel; minimum = info.minLevel; maximum = info.maxLevel; neutral = info.minLevel;
                    return true;
                }
            }
            current = minimum = maximum = neutral = 0;
            return false;
        }

        private static NvDisplayDvcInfoEx NewDvcEx() => new NvDisplayDvcInfoEx { version = (uint)Marshal.SizeOf<NvDisplayDvcInfoEx>() | 0x10000 };
        private static string ReadString(byte[] buffer)
        {
            int end = Array.IndexOf(buffer, (byte)0);
            return Encoding.ASCII.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
        }
        private static void Check(int status, string operation) { if (status != 0) throw new InvalidOperationException($"Failed to {operation} (NVAPI {status})."); }

        public void Dispose()
        {
            if (_initialized) { _unload(); _initialized = false; }
            _library?.Dispose();
            _library = null;
        }

        private sealed class Display { public IntPtr Handle; public bool UseExtended; public int? LastRequested; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr QueryInterface(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ApiCall();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumDisplay(uint index, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetName(IntPtr handle, [Out] byte[] name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDisplayHandle([MarshalAs(UnmanagedType.LPStr)] string name, out IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumGpus([Out] IntPtr[] handles, out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDvc(IntPtr display, uint output, ref NvDisplayDvcInfo info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDvc(IntPtr display, uint output, int level);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDvcEx(IntPtr display, uint output, ref NvDisplayDvcInfoEx info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDvcEx(IntPtr display, uint output, ref NvDisplayDvcInfoEx info);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    internal struct NvDisplayDvcInfoEx
    {
        public uint version;
        public int currentLevel;
        public int minLevel;
        public int maxLevel;
        public int defaultLevel;
    }

    internal sealed class NativeVibranceSmokeResult
    {
        public string Display { get; set; }
        public int Original { get; set; }
        public int Target { get; set; }
        public int SetStatus { get; set; }
        public bool ReadSucceeded { get; set; }
        public int Observed { get; set; }
        public int RestoreStatus { get; set; }
        public bool RestoreReadSucceeded { get; set; }
        public int Restored { get; set; }
        public bool Passed => SetStatus == 0 && ReadSucceeded && Observed == Target && RestoreStatus == 0 && RestoreReadSucceeded && Restored == Original;
    }
}
