using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using vibrance.GUI.common;

namespace vibrance.GUI.AMD.vendor
{
    // ABI and ownership follow AMD's include/adl_structures.h and ADL2 color APIs.
    // ADL2 gives this application its own context rather than a process-global ADL session.
    public class AmdAdapter : IAmdAdapter
    {
        private const int Saturation = 1 << 2;
        private readonly bool _use64Bit;
        private readonly Dictionary<string, List<Display>> _displays = new Dictionary<string, List<Display>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Allocate MemoryAllocator = size => Marshal.AllocCoTaskMem(size);
        private DriverLibrary _library;
        private IntPtr _context;
        private Destroy _destroy;
        private ColorGet _colorGet;
        private ColorSet _colorSet;
        private bool _attempted;

        public AmdAdapter() : this(Environment.Is64BitProcess) { }
        protected AmdAdapter(bool use64Bit) { _use64Bit = use64Bit; }
        public IReadOnlyList<string> DisplayNames => _displays.Keys.ToArray();
        public string GpuName { get; private set; } = "AMD";
        public string InitializationError { get; private set; } = string.Empty;

        public void Init()
        {
            if (_attempted) return;
            _attempted = true;
            try
            {
                if (_use64Bit != Environment.Is64BitProcess) throw new InvalidOperationException("The AMD API must match the application's process architecture.");
                _library = DriverLibrary.Load(_use64Bit ? "atiadlxx.dll" : "atiadlxy.dll");
                var create = _library.GetExport<Create>("ADL2_Main_Control_Create");
                _destroy = _library.GetExport<Destroy>("ADL2_Main_Control_Destroy");
                var getCount = _library.GetExport<AdapterCount>("ADL2_Adapter_NumberOfAdapters_Get");
                var getAdapters = _library.GetExport<AdapterInfoGet>("ADL2_Adapter_AdapterInfo_Get");
                var getActive = _library.GetExport<AdapterActive>("ADL2_Adapter_Active_Get");
                var getDisplays = _library.GetExport<DisplayInfoGet>("ADL2_Display_DisplayInfo_Get");
                _colorGet = _library.GetExport<ColorGet>("ADL2_Display_Color_Get");
                _colorSet = _library.GetExport<ColorSet>("ADL2_Display_Color_Set");
                Check(create(MemoryAllocator, 1, out _context), "initialize ADL2");
                Check(getCount(_context, out int count), "enumerate AMD adapters");
                if (count < 0 || count > 256) throw new InvalidOperationException("AMD returned an invalid adapter count.");
                if (count == 0) throw new NotSupportedException("No connected AMD display adapter was found.");

                int stride = Marshal.SizeOf<AdapterInfo>();
                IntPtr buffer = Marshal.AllocCoTaskMem(checked(stride * count));
                try
                {
                    // AdapterInfo.iSize is an input field in the native SDK examples.
                    for (int index = 0; index < count; index++)
                        Marshal.StructureToPtr(new AdapterInfo { Size = stride }, IntPtr.Add(buffer, index * stride), false);
                    Check(getAdapters(_context, buffer, stride * count), "read AMD adapters");
                    for (int index = 0; index < count; index++)
                    {
                        AdapterInfo adapter = Marshal.PtrToStructure<AdapterInfo>(IntPtr.Add(buffer, index * stride));
                        if (adapter.Present != 1 || string.IsNullOrEmpty(adapter.DisplayName) ||
                            getActive(_context, adapter.AdapterIndex, out int active) != 0 || active != 1) continue;
                        IntPtr displayBuffer = IntPtr.Zero;
                        try
                        {
                            if (getDisplays(_context, adapter.AdapterIndex, out int displayCount, out displayBuffer, 0) != 0 ||
                                displayCount < 0 || displayCount > 256 || displayBuffer == IntPtr.Zero) continue;
                            int displayStride = Marshal.SizeOf<DisplayInfo>();
                            for (int displayIndex = 0; displayIndex < displayCount; displayIndex++)
                            {
                                DisplayInfo info = Marshal.PtrToStructure<DisplayInfo>(IntPtr.Add(displayBuffer, displayIndex * displayStride));
                                if ((info.Value & 3) != 3 || info.Id.LogicalAdapterIndex != adapter.AdapterIndex) continue;
                                var display = new Display { Adapter = adapter.AdapterIndex, Index = info.Id.LogicalIndex };
                                if (!ReadColor(display, out _, out _, out _, out _)) continue;
                                if (!_displays.TryGetValue(adapter.DisplayName, out var entries))
                                    _displays[adapter.DisplayName] = entries = new List<Display>();
                                if (!entries.Any(x => x.Adapter == display.Adapter && x.Index == display.Index)) entries.Add(display);
                                GpuName = adapter.AdapterName;
                            }
                        }
                        finally { if (displayBuffer != IntPtr.Zero) Marshal.FreeCoTaskMem(displayBuffer); }
                    }
                }
                finally { Marshal.FreeCoTaskMem(buffer); }
                if (_displays.Count == 0) InitializationError = "AMD: no attached display supports saturation control. Check that the display is connected to the AMD GPU and that driver color controls are available.";
            }
            catch (Exception ex)
            {
                InitializationError = "AMD: " + ex.Message;
                _displays.Clear();
                Dispose();
            }
        }

        public bool IsAvailable() { Init(); return _displays.Count > 0; }
        public void SetSaturationOnAllDisplays(int level) { foreach (string name in DisplayNames) SetLevel(name, level); }
        public void SetSaturationOnDisplay(int level, string name)
        {
            if (name == null) SetSaturationOnAllDisplays(level); else SetLevel(name, level);
        }

        public bool SetLevel(string deviceName, int level)
        {
            if (_context == IntPtr.Zero || !_displays.TryGetValue(deviceName, out var entries)) return false;
            bool success = true;
            foreach (Display display in entries)
            {
                if (display.LastRequested == level) continue;
                if (!ReadColor(display, out int current, out int minimum, out int maximum, out int step)) { success = false; continue; }
                int target = Math.Clamp(level, minimum, maximum);
                if (step > 1) target = Math.Clamp(minimum + (int)Math.Round((target - minimum) / (double)step) * step, minimum, maximum);
                if (target != current && _colorSet(_context, display.Adapter, display.Index, Saturation, target) != 0)
                {
                    VibranceGUI.Log(new InvalidOperationException($"AMD saturation control on {deviceName} failed."));
                    success = false;
                    continue;
                }
                display.LastRequested = level;
            }
            return success;
        }

        private bool ReadColor(Display display, out int current, out int minimum, out int maximum, out int step)
        {
            int status = _colorGet(_context, display.Adapter, display.Index, Saturation, out current, out _, out minimum, out maximum, out step);
            return status == 0 && minimum <= maximum;
        }

        public void InvalidateCache() { foreach (Display display in _displays.Values.SelectMany(x => x)) display.LastRequested = null; }

        private static void Check(int status, string operation) { if (status != 0) throw new InvalidOperationException($"Failed to {operation} (ADL {status})."); }
        public void Dispose()
        {
            if (_context != IntPtr.Zero) { _destroy?.Invoke(_context); _context = IntPtr.Zero; }
            _library?.Dispose();
            _library = null;
        }

        private sealed class Display { public int Adapter; public int Index; public int? LastRequested; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Allocate(int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(Allocate allocator, int connected, out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Destroy(IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterCount(IntPtr context, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterInfoGet(IntPtr context, IntPtr buffer, int size);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AdapterActive(IntPtr context, int adapter, out int active);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DisplayInfoGet(IntPtr context, int adapter, out int count, out IntPtr buffer, int forceDetect);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ColorGet(IntPtr context, int adapter, int display, int type, out int current, out int defaultValue, out int minimum, out int maximum, out int step);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ColorSet(IntPtr context, int adapter, int display, int type, int value);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct AdapterInfo
        {
            public int Size, AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Udid;
            public int Bus, Device, Function, Vendor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
            public int Present, Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string PnpString;
            public int OsDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DisplayId { public int LogicalIndex, PhysicalIndex, LogicalAdapterIndex, PhysicalAdapterIndex; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct DisplayInfo
        {
            public DisplayId Id;
            public int Controller;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
            public int Type, OutputType, Connector, Mask, Value;
        }
    }
}
