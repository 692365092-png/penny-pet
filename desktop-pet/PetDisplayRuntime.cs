using System;

namespace PennyPet
{
    // Pet STA native adapter. Display state lives only in PetDisplayRuntime.
    internal sealed partial class PetForm : IPetDisplayWindow
    {
        // Suppresses intermediate follower presentation during one native DPI message.
        private bool _petDpiDragHandoffActive;

        bool IPetDisplayWindow.IsAvailable
        { get { return !IsDisposed && !Disposing && IsHandleCreated && Handle != IntPtr.Zero; } }
        PhysicalRect IPetDisplayWindow.Bounds
        { get { return new PhysicalRect(Left, Top, Width, Height); } }
        int IPetDisplayWindow.ScalePercent { get { return _scalePercent; } }
        bool IPetDisplayWindow.IsUserDragging
        { get { return _interaction != null && _interaction.PointerDown; } }
        int IPetDisplayWindow.GetDpi(int fallbackDpi) { return ActualPetDpi(fallbackDpi); }
        bool IPetDisplayWindow.MoveTopLeft(int x, int y) { return TrySetPetTopLeft(x, y); }
        void IPetDisplayWindow.ApplyScale(int dpi) { ApplyCurrentDisplayScale(dpi); }
        void IPetDisplayWindow.PlacementChanged() { _stickyWorkspace.PositionNoteTabs(); }
        WindowFacts IPetDisplayWindow.CaptureFacts(DisplayTopologySnapshot topology, long sequence)
        {
            return WindowsWindowFactsReader.Capture(Handle, "pet",
                topology.Generation, sequence, topology);
        }

        private int ActualPetDpi(int fallbackDpi = 96)
        {
            if (IsHandleCreated && Handle != IntPtr.Zero)
            {
                int dpi = NativeDisplayConfig.GetDpiForWindow(Handle);
                if (dpi > 0) return dpi;
            }
            return Math.Max(96, fallbackDpi);
        }

        private bool TrySetPetTopLeft(int x, int y)
        {
            if (!IsHandleCreated || Handle == IntPtr.Zero) return false;
            return NativeDisplayConfig.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0,
                NativeDisplayConfig.SWP_NOSIZE | NativeDisplayConfig.SWP_NOZORDER |
                NativeDisplayConfig.SWP_NOACTIVATE);
        }

        internal WindowFacts CapturePetWindowFacts(DisplayTopologySnapshot topology)
        { return _petDisplay.Capture(topology); }

        internal DisplayTopologySnapshot CurrentTopologySnapshot()
        { return _displayTopologyRuntime == null ? null : _displayTopologyRuntime.Current; }
    }
}
