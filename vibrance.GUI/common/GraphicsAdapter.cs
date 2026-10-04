using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using vibrance.GUI.AMD.vendor;
using vibrance.GUI.NVIDIA;

namespace vibrance.GUI.common
{
    public enum GraphicsAdapter { Unknown = 0, Nvidia = 1, Amd = 2, Ambiguous = 3 }

    public static class GraphicsAdapterHelper
    {
        public static string LastError { get; private set; } = string.Empty;

        public static GraphicsAdapter GetAdapter()
        {
            // Installed libraries are not evidence of a GPU driving a supported display.
            // A Ryzen iGPU driver may coexist with a discrete NVIDIA GPU.
            using (var nvidia = new NvidiaDisplayBackend())
            {
                if (nvidia.DisplayNames.Count > 0) return GraphicsAdapter.Nvidia;
                LastError = nvidia.InitializationError;
            }
            using (var amd = new AmdAdapter())
            {
                amd.Init();
                if (amd.DisplayNames.Count > 0) return GraphicsAdapter.Amd;
                LastError += Environment.NewLine + amd.InitializationError;
            }
            return GraphicsAdapter.Unknown;
        }
    }

    public interface IDisplayVibranceBackend : IDisposable
    {
        IReadOnlyList<string> DisplayNames { get; }
        string GpuName { get; }
        string InitializationError { get; }
        bool SetLevel(string deviceName, int level);
        void InvalidateCache() { }
    }

    internal sealed class DriverLibrary : SafeHandleZeroOrMinusOneIsInvalid
    {
        private DriverLibrary(IntPtr handle) : base(true) { SetHandle(handle); }

        public static DriverLibrary Load(string fileName) =>
            new DriverLibrary(NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, fileName)));

        public T GetExport<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

        protected override bool ReleaseHandle()
        {
            NativeLibrary.Free(handle);
            return true;
        }
    }
}
