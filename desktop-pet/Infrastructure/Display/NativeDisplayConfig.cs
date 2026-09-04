using System;
using System.Runtime.InteropServices;

namespace PennyPet
{
    // Raw QueryDisplayConfig / EnumDisplayMonitors interop. Struct layouts
    // follow the official Windows headers; no custom Pack is applied.
    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigLuid
    {
        internal int LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigRational
    {
        internal int Numerator;
        internal int Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfig2DRegion
    {
        internal int Cx;
        internal int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigVideoSignalInfo
    {
        internal long PixelRate;
        internal DisplayConfigRational HSyncFrequency;
        internal DisplayConfigRational VSyncFrequency;
        internal DisplayConfig2DRegion ActiveSize;
        internal DisplayConfig2DRegion TotalSize;
        internal int VideoStandard;
        internal int ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigTargetMode
    {
        internal DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigModeInfo
    {
        internal int InfoType;
        internal int Id;
        internal DisplayConfigLuid AdapterId;
        internal DisplayConfigTargetMode TargetMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathSourceInfo
    {
        internal DisplayConfigLuid AdapterId;
        internal int Id;
        internal int ModeInfoIndex;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathTargetInfo
    {
        internal DisplayConfigLuid AdapterId;
        internal int Id;
        internal int ModeInfoIndex;
        internal int OutputTechnology;
        internal int Rotation;
        internal int Scaling;
        internal DisplayConfigRational RefreshRate;
        internal int ScanLineOrdering;
        internal int TargetAvailable;
        internal uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigPathInfo
    {
        internal DisplayConfigPathSourceInfo SourceInfo;
        internal DisplayConfigPathTargetInfo TargetInfo;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DisplayConfigDeviceInfoHeader
    {
        internal int Type;
        internal int Size;
        internal DisplayConfigLuid AdapterId;
        internal int Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigSourceDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayConfigTargetDeviceName
    {
        internal DisplayConfigDeviceInfoHeader Header;
        internal int Flags;
        internal int OutputTechnology;
        internal short EdidManufactureId;
        internal short EdidProductCodeId;
        internal short ConnectorInstance;
        internal short Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeDisplayRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct NativeDisplayMonitorInfo
    {
        internal int Size;
        internal NativeDisplayRect Monitor;
        internal NativeDisplayRect Work;
        internal int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    internal delegate bool DisplayMonitorEnumProc(IntPtr hMonitor,
        IntPtr hdcMonitor, ref NativeDisplayRect clip, IntPtr data);

    internal static class NativeDisplayConfig
    {
        internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        internal const uint QDC_VIRTUAL_MODE_AWARE = 0x00000010;
        internal const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;

        internal const int ERROR_SUCCESS = 0;
        internal const int ERROR_INSUFFICIENT_BUFFER = 122;

        internal const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
        internal const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

        internal const int MONITORINFOF_PRIMARY = 1;

        [DllImport("user32.dll")]
        internal static extern int GetDisplayConfigBufferSizes(
            uint flags, out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        internal static extern int QueryDisplayConfig(uint flags,
            ref uint numPathArrayElements,
            [In, Out] DisplayConfigPathInfo[] pathArray,
            ref uint numModeInfoArrayElements,
            [In, Out] DisplayConfigModeInfo[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int DisplayConfigGetSourceDeviceName(
            ref DisplayConfigSourceDeviceName requestPacket);

        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
        internal static extern int DisplayConfigGetTargetDeviceName(
            ref DisplayConfigTargetDeviceName requestPacket);

        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc,
            IntPtr clip, DisplayMonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern bool GetMonitorInfo(IntPtr monitor,
            ref NativeDisplayMonitorInfo info);
    }
}
