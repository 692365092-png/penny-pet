using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PennyPet
{
    // The only Windows-side place that knows per-monitor DPI. Everything else
    // should consume logical geometry and this adapter's projections.
    internal static class WindowsDisplayMetrics
    {
        private const int DefaultDpi = 96;

        internal static DisplayScale ScaleForWindow(IntPtr hwnd)
        {
            int dpi = GetDpiForWindowSafe(hwnd);
            return new DisplayScale(dpi / 96.0, dpi / 96.0);
        }

        internal static LogicalPoint PhysicalToLogical(Point point,
            DisplayScale scale)
        {
            return new LogicalPoint(point.X / scale.X, point.Y / scale.Y);
        }

        internal static LogicalRect PhysicalToLogical(Rectangle rect,
            DisplayScale scale)
        {
            return new LogicalRect(rect.Left / scale.X, rect.Top / scale.Y,
                rect.Width / scale.X, rect.Height / scale.Y);
        }

        // Monitor-aware physical -> DIP conversion for mixed-DPI desktops.
        // PhysicalToLogicalPointForPerMonitorDPI chooses the monitor that owns
        // the point and applies that monitor's own scale and logical origin.
        internal static LogicalPoint PhysicalToLogicalDips(IntPtr hwnd,
            Point point)
        {
            NativePoint value = new NativePoint(point.X, point.Y);
            try
            {
                if (hwnd != IntPtr.Zero &&
                    PhysicalToLogicalPointForPerMonitorDPI(hwnd, ref value))
                    return new LogicalPoint(value.X, value.Y);
            }
            catch { }
            DisplayScale scale = ScaleForWindow(hwnd);
            return new LogicalPoint(point.X / scale.X, point.Y / scale.Y);
        }

        internal static LogicalRect PhysicalToLogicalDips(IntPtr hwnd,
            Rectangle rect)
        {
            LogicalPoint topLeft = PhysicalToLogicalDips(hwnd,
                rect.Location);
            LogicalPoint bottomRight = PhysicalToLogicalDips(hwnd,
                new Point(rect.Right, rect.Bottom));
            return new LogicalRect(topLeft.X, topLeft.Y,
                bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }

        internal static Point LogicalToPhysicalPoint(LogicalPoint point,
            DisplayScale scale)
        {
            return new Point(Round(point.X * scale.X),
                Round(point.Y * scale.Y));
        }

        internal static Rectangle LogicalToPhysicalRect(LogicalRect rect,
            DisplayScale scale)
        {
            int left = Round(rect.Left * scale.X);
            int top = Round(rect.Top * scale.Y);
            int right = Round(rect.Right * scale.X);
            int bottom = Round(rect.Bottom * scale.Y);
            return new Rectangle(left, top, right - left, bottom - top);
        }

        private static int Round(double value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static int GetDpiForWindowSafe(IntPtr hwnd)
        {
            try
            {
                int dpi = GetDpiForWindow(hwnd);
                return dpi > 0 ? dpi : DefaultDpi;
            }
            catch
            {
                // Older Windows without GetDpiForWindow falls back to 96 DPI.
                return DefaultDpi;
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool PhysicalToLogicalPointForPerMonitorDPI(
            IntPtr hwnd, ref NativePoint point);

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
    }
}
