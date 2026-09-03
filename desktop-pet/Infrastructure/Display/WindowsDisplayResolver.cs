using System;
using System.Runtime.InteropServices;

namespace PennyPet
{
    internal static class WindowsDisplayResolver
    {
        private const int MonitorDefaultToNearest = 2;
        private const int MonitorInfoFlagsPrimary = 1;
        private const int EffectiveDpi = 0;

        internal static WindowsDisplayMetrics ResolvePhysicalPoint(
            int physicalX, int physicalY)
        {
            IntPtr monitor = MonitorFromPoint(
                new NativePoint(physicalX, physicalY),
                MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return null;
            return ResolveMonitor(monitor);
        }

        internal static WindowsDisplayMetrics ResolvePhysicalRect(
            int physicalLeft, int physicalTop,
            int physicalRight, int physicalBottom)
        {
            IntPtr monitor = MonitorFromRect(
                new NativeRect(physicalLeft, physicalTop,
                    physicalRight, physicalBottom),
                MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return null;
            return ResolveMonitor(monitor);
        }

        private static WindowsDisplayMetrics ResolveMonitor(IntPtr monitor)
        {
            NativeMonitorInfo info = new NativeMonitorInfo();
            info.cbSize = Marshal.SizeOf(typeof(NativeMonitorInfo));
            if (!GetMonitorInfo(monitor, ref info)) return null;

            int dpiX;
            int dpiY;
            if (GetDpiForMonitor(monitor, EffectiveDpi, out dpiX, out dpiY) != 0)
            {
                dpiX = 96;
                dpiY = 96;
            }
            double scale = dpiX > 0 ? dpiX / 96.0 : 1.0;
            return new WindowsDisplayMetrics(
                info.szDevice,
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top,
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top,
                scale);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;

            internal NativePoint(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal NativeRect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NativeMonitorInfo
        {
            internal int cbSize;
            internal NativeRect rcMonitor;
            internal NativeRect rcWork;
            internal int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string szDevice;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(
            NativePoint point, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(
            NativeRect rect, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor,
            ref NativeMonitorInfo info);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor,
            int dpiType, out int dpiX, out int dpiY);
    }
}
