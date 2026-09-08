using System;
using System.Windows;
using System.Windows.Interop;

namespace PennyPet
{
    // Thin typed Windows primitive for one WPF top-level window. It owns the
    // desktop-placement bootstrap sequence (hidden HWND move -> real window
    // DPI -> exact physical rect -> show -> capture) and nothing else. Dock
    // layout, persistence and rehome policy must never live here.
    internal sealed class WindowsWindowPlacementExecutor
    {
        internal const int PlacementTolerancePixels = 2;

        private readonly Window _window;
        private IntPtr _handle;

        internal WindowsWindowPlacementExecutor(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
        }

        internal IntPtr EnsureHandle()
        {
            _handle = new WindowInteropHelper(_window).EnsureHandle();
            return _handle;
        }

        // Hidden move only: park the not-yet-shown HWND inside the target work
        // area so GetDpiForWindow reports the target monitor DPI. The window
        // must stay hidden, so SWP_SHOWWINDOW is deliberately absent and size
        // is untouched (NOSIZE). A wrong-position first frame is forbidden.
        internal bool MoveHiddenToSurface(PhysicalRect workArea)
        {
            if (_handle == IntPtr.Zero || !workArea.IsValid) return false;
            int insetX = Math.Min(16, Math.Max(0, workArea.Width / 4));
            int insetY = Math.Min(16, Math.Max(0, workArea.Height / 4));
            int x = workArea.Left + insetX;
            int y = workArea.Top + insetY;
            return NativeDisplayConfig.SetWindowPos(_handle, IntPtr.Zero,
                x, y, 0, 0,
                NativeDisplayConfig.SWP_NOACTIVATE |
                NativeDisplayConfig.SWP_NOZORDER |
                NativeDisplayConfig.SWP_NOSIZE);
        }

        internal int GetDpiForWindow()
        {
            return _handle == IntPtr.Zero
                ? 0 : NativeDisplayConfig.GetDpiForWindow(_handle);
        }

        internal bool SetWindowPosExact(PhysicalRect rect)
        {
            if (_handle == IntPtr.Zero || !rect.IsValid) return false;
            return NativeDisplayConfig.SetWindowPos(_handle, IntPtr.Zero,
                rect.Left, rect.Top, rect.Width, rect.Height,
                NativeDisplayConfig.SWP_NOACTIVATE |
                NativeDisplayConfig.SWP_NOZORDER);
        }

        internal void Show()
        {
            if (_window == null || _window.IsVisible) return;
            // The HWND already exists and is positioned; WPF Show() only makes
            // it visible at its current bounds without re-applying Left/Top.
            _window.Show();
        }

        internal WindowFacts CaptureFacts(string windowId,
            long topologyGeneration, long windowSequence,
            DisplayTopologySnapshot topology = null)
        {
            return WindowsWindowFactsReader.Capture(_handle, windowId,
                topologyGeneration, windowSequence, topology);
        }
    }
}
