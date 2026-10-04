using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace vibrance.GUI.common
{
    class ResolutionHelper
    {
        private const int EnumCurrentSettings = -1;

        [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref Devmode devMode);


        [DllImport("User32.dll", EntryPoint = "ChangeDisplaySettingsW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static extern int ChangeDisplaySettings(
            [In, Out]
            ref Devmode lpDevMode,
            [param: MarshalAs(UnmanagedType.U4)]
            uint dwflags);

        [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
        private static extern DispChange ChangeDisplaySettingsEx(
            string lpszDeviceName,
            ref Devmode lpDevMode,
            IntPtr hwnd,
            ChangeDisplaySettingsFlags dwflags,
            IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "ChangeDisplaySettingsExW", CharSet = CharSet.Unicode)]
        public static extern DispChange ChangeDisplaySettingsEx(
            string lpszDeviceName, 
            IntPtr lpDevMode, 
            IntPtr hwnd, 
            ChangeDisplaySettingsFlags dwflags, 
            IntPtr lParam);


        public static bool GetCurrentResolutionSettings(out Devmode mode, string lpszDeviceName)
        {
            mode = new Devmode();
            mode.dmSize = (ushort)Marshal.SizeOf(mode);
            mode.dmDriverExtra = 0;

            if (EnumDisplaySettings(lpszDeviceName, EnumCurrentSettings, ref mode) == true)
            {
                return true;
            }

            return false;
        }

        public static List<ResolutionModeWrapper> EnumerateSupportedResolutionModes()
        {
            return EnumerateSupportedResolutionModes(null);
        }

        public static List<ResolutionModeWrapper> EnumerateSupportedResolutionModes(string deviceName)
        {
            List<ResolutionModeWrapper> resolutionList = new List<ResolutionModeWrapper>();
            Devmode mode = new Devmode();
            mode.dmSize = (ushort)Marshal.SizeOf(mode);

            int index = 0;
            while (index < 4096 && EnumDisplaySettings(deviceName, index++, ref mode) == true)
            {
                resolutionList.Add(new ResolutionModeWrapper(mode));
            }

            return resolutionList;
        }

        public static bool ChangeResolution(ResolutionModeWrapper resolutionMode)
        {
            return ChangeResolutionEx(resolutionMode, null);
        }

        internal static bool MatchesRequestedMode(ResolutionModeWrapper requested, Devmode actual) =>
            requested.DmPelsWidth == actual.dmPelsWidth && requested.DmPelsHeight == actual.dmPelsHeight &&
            requested.DmBitsPerPel == actual.dmBitsPerPel && requested.DmDisplayFrequency == actual.dmDisplayFrequency;

        private static readonly HashSet<string> LoggedFailures = new HashSet<string>();

        public static bool ChangeResolutionEx(ResolutionModeWrapper resolutionMode, string lpszDeviceName)
        {
            return ChangeResolutionEx(resolutionMode, lpszDeviceName, out _);
        }

        public static bool ChangeResolutionEx(ResolutionModeWrapper resolutionMode, string lpszDeviceName, out bool modeSetAttempted)
        {
            modeSetAttempted = false;
            if (resolutionMode != null && GetCurrentResolutionSettings(out Devmode mode, lpszDeviceName))
            {
                if (MatchesRequestedMode(resolutionMode, mode)) return true;
                // Monitor mode buttons can change the supported modes while we are running.
                if (!EnumerateSupportedResolutionModes(lpszDeviceName).Any(x =>
                    x.DmPelsWidth == resolutionMode.DmPelsWidth && x.DmPelsHeight == resolutionMode.DmPelsHeight &&
                    x.DmBitsPerPel == resolutionMode.DmBitsPerPel && x.DmDisplayFrequency == resolutionMode.DmDisplayFrequency))
                    return ReportFailure(lpszDeviceName, resolutionMode, DispChange.DispChangeBadmode);
                mode.dmPelsWidth = resolutionMode.DmPelsWidth;
                mode.dmPelsHeight = resolutionMode.DmPelsHeight;
                mode.dmBitsPerPel = resolutionMode.DmBitsPerPel;
                mode.dmDisplayFrequency = resolutionMode.DmDisplayFrequency;
                mode.dmDisplayFixedOutput = resolutionMode.DmDisplayFixedOutput;
                mode.dmFields |= 0x00040000 | 0x00080000 | 0x00100000 | 0x00400000 | 0x20000000;
                DispChange test = ChangeDisplaySettingsEx(lpszDeviceName, ref mode, IntPtr.Zero, ChangeDisplaySettingsFlags.CdsTest, IntPtr.Zero);
                if (test != DispChange.DispChangeSuccessful)
                {
                    // Some modern drivers reject the old fixed-output scaling field.
                    mode.dmFields &= ~0x20000000u;
                    test = ChangeDisplaySettingsEx(lpszDeviceName, ref mode, IntPtr.Zero, ChangeDisplaySettingsFlags.CdsTest, IntPtr.Zero);
                }
                if (test != DispChange.DispChangeSuccessful) return ReportFailure(lpszDeviceName, resolutionMode, test);
                // Game modes are temporary: do not persist a rejected/temporary mode in the registry.
                modeSetAttempted = true;
                DispChange changed = ChangeDisplaySettingsEx(lpszDeviceName, ref mode, IntPtr.Zero, ChangeDisplaySettingsFlags.CdsFullscreen, IntPtr.Zero);
                if (changed != DispChange.DispChangeSuccessful) return ReportFailure(lpszDeviceName, resolutionMode, changed);
                if (GetCurrentResolutionSettings(out Devmode achieved, lpszDeviceName) && MatchesRequestedMode(resolutionMode, achieved)) return true;
                return ReportFailure(lpszDeviceName, resolutionMode, DispChange.DispChangeFailed);
            }
            return false;
        }

        private static bool ReportFailure(string device, ResolutionModeWrapper mode, DispChange result)
        {
            string message = $"Resolution change on {device}: {mode.DmPelsWidth}x{mode.DmPelsHeight}@{mode.DmDisplayFrequency} failed ({result}).";
            lock (LoggedFailures)
                if (LoggedFailures.Add(message)) VibranceGUI.Log(new InvalidOperationException(message));
            return false;
        }
    }

    public enum DispChange : int
    {
        DispChangeSuccessful = 0,
        DispChangeRestart = 1,
        DispChangeFailed = -1,
        DispChangeBadmode = -2,
        DispChangeNotupdated = -3,
        DispChangeBadflags = -4,
        DispChangeBadparam = -5
    };

    public enum Dmdfo : int
    {
        Default = 0,
        Stretch = 1,
        Center = 2
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Devmode
    {
        // You can define the following constant
        // but OUTSIDE the structure because you know
        // that size and layout of the structure
        // is very important
        // CCHDEVICENAME = 32 = 0x50
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        // In addition you can define the last character array
        // as following:
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        //public Char[] dmDeviceName;

        // After the 32-bytes array
        [MarshalAs(UnmanagedType.U2)]
        public UInt16 dmSpecVersion;

        [MarshalAs(UnmanagedType.U2)]
        public UInt16 dmDriverVersion;

        [MarshalAs(UnmanagedType.U2)]
        public UInt16 dmSize;

        [MarshalAs(UnmanagedType.U2)]
        public UInt16 dmDriverExtra;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmFields;

        public Pointl dmPosition;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmDisplayOrientation;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmDisplayFixedOutput;

        [MarshalAs(UnmanagedType.I2)]
        public Int16 dmColor;

        [MarshalAs(UnmanagedType.I2)]
        public Int16 dmDuplex;

        [MarshalAs(UnmanagedType.I2)]
        public Int16 dmYResolution;

        [MarshalAs(UnmanagedType.I2)]
        public Int16 dmTTOption;

        [MarshalAs(UnmanagedType.I2)]
        public Int16 dmCollate;

        // CCHDEVICENAME = 32 = 0x50
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        // Also can be defined as
        //[MarshalAs(UnmanagedType.ByValArray,
        //    SizeConst = 32, ArraySubType = UnmanagedType.U1)]
        //public Byte[] dmFormName;

        [MarshalAs(UnmanagedType.U2)]
        public UInt16 dmLogPixels;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmBitsPerPel;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmPelsWidth;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmPelsHeight;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmDisplayFlags;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmDisplayFrequency;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmICMMethod;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmICMIntent;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmMediaType;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmDitherType;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmReserved1;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmReserved2;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmPanningWidth;

        [MarshalAs(UnmanagedType.U4)]
        public UInt32 dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Pointl
    {
        [MarshalAs(UnmanagedType.I4)]
        public int x;
        [MarshalAs(UnmanagedType.I4)]
        public int y;
    }

    [Flags()]
    public enum ChangeDisplaySettingsFlags : uint
    {
        CdsNone = 0,
        CdsUpdateregistry = 0x00000001,
        CdsTest = 0x00000002,
        CdsFullscreen = 0x00000004,
        CdsGlobal = 0x00000008,
        CdsSetPrimary = 0x00000010,
        CdsVideoparameters = 0x00000020,
        CdsEnableUnsafeModes = 0x00000100,
        CdsDisableUnsafeModes = 0x00000200,
        CdsReset = 0x40000000,
        CdsResetEx = 0x20000000,
        CdsNoreset = 0x10000000
    }

}
