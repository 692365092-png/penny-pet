using System;
using System.Runtime.InteropServices;

namespace PennyPet
{
    // Captures actual window geometry facts from a live HWND. Must be called
    // on the thread that owns the HWND so GetWindowRect reflects the real
    // per-monitor physical bounds instead of a DPI-virtualized projection.
    internal static class WindowsWindowFactsReader
    {
        private const int MonitorDefaultToNearest = 2;

        internal static WindowFacts Capture(IntPtr hwnd, string windowId,
            long topologyGeneration, long windowSequence,
            DisplayTopologySnapshot topology = null)
        {
            if (hwnd == IntPtr.Zero) return null;
            int dpi = NativeDisplayConfig.GetDpiForWindow(hwnd);
            if (dpi <= 0) return null;

            NativeDisplayRect rect;
            if (!NativeDisplayConfig.GetWindowRect(hwnd, out rect)) return null;
            PhysicalRect physicalBounds = new PhysicalRect(rect.Left,
                rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

            IntPtr monitor = NativeDisplayConfig.MonitorFromWindow(hwnd,
                MonitorDefaultToNearest);
            string gdiName = String.Empty;
            if (monitor != IntPtr.Zero)
            {
                NativeDisplayMonitorInfo info = new NativeDisplayMonitorInfo();
                info.Size = Marshal.SizeOf(typeof(NativeDisplayMonitorInfo));
                if (NativeDisplayConfig.GetMonitorInfo(monitor, ref info))
                    gdiName = info.DeviceName ?? String.Empty;
            }

            string activeTargetKey = String.Empty;
            if (topology != null)
            {
                DisplaySurfaceSnapshot surface =
                    topology.FindByRuntimeGdiName(gdiName);
                // Active-target hint only: Targets[0] is NOT a durable
                // preferred-identity selection rule. A mirrored surface can
                // expose several targets, and DRT-6 must choose
                // deterministically (prefer an existing preferred key that
                // belongs to this surface) without relying on
                // QueryDisplayConfig enumeration order.
                if (surface != null && surface.Targets.Count > 0)
                    activeTargetKey = surface.Targets[0].StableKey;
            }

            return new WindowFacts(windowId, activeTargetKey, gdiName,
                physicalBounds, dpi, topologyGeneration, windowSequence);
        }
    }
}
