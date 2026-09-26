using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PennyPet
{
    // Only a real native edit HWND gives the cheap hook a control identity.
    // A browser/WPF host HWND cannot identify its virtual focused descendant.
    internal sealed class KeyboardFocusSnapshot
    {
        private readonly int[] _automationRuntimeId;
        internal KeyboardFocusSnapshot(IntPtr foregroundWindow, uint processId,
            uint threadId, IntPtr focusedWindow, int[] automationRuntimeId)
            : this(foregroundWindow, processId, threadId, focusedWindow,
                automationRuntimeId, false, 0, 0) { }

        internal KeyboardFocusSnapshot(IntPtr foregroundWindow, uint processId,
            uint threadId, IntPtr focusedWindow, int[] automationRuntimeId,
            bool plainNativeEdit, long focusVersion, long capturedAt)
        {
            ForegroundWindow = foregroundWindow;
            ProcessId = processId;
            ThreadId = threadId;
            FocusedWindow = focusedWindow;
            _automationRuntimeId = automationRuntimeId == null ? null : (int[])automationRuntimeId.Clone();
            HasNativeInputIdentity = plainNativeEdit && IsComplete;
            FocusVersion = focusVersion;
            CapturedAt = capturedAt;
        }
        internal IntPtr ForegroundWindow { get; private set; }
        internal uint ProcessId { get; private set; }
        internal uint ThreadId { get; private set; }
        internal IntPtr FocusedWindow { get; private set; }
        internal bool HasNativeInputIdentity { get; private set; }
        internal long FocusVersion { get; private set; }
        internal long CapturedAt { get; private set; }
        internal bool IsComplete
        { get { return ForegroundWindow != IntPtr.Zero && ProcessId != 0 && ThreadId != 0 && FocusedWindow != IntPtr.Zero; } }

        internal static KeyboardFocusSnapshot CaptureCheap()
        {
            return CaptureForForegroundWindow(GetForegroundWindow());
        }

        // Capture the focused control from the GUI thread that owns a known top-level
        // window. Production always supplies GetForegroundWindow(); native QA supplies
        // its own host HWND so hosted CI does not need permission to steal OS foreground.
        internal static KeyboardFocusSnapshot CaptureForForegroundWindow(IntPtr foreground)
        {
            long version = KeyboardFocusMonitor.Version;
            long capturedAt = Stopwatch.GetTimestamp();
            uint processId;
            uint threadId = GetWindowThreadProcessId(foreground, out processId);
            var info = new GuiThreadInfo { cbSize = Marshal.SizeOf(typeof(GuiThreadInfo)) };
            IntPtr focused = threadId != 0 && GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
            bool plain = false;
            if (focused != IntPtr.Zero)
            {
                var name = new StringBuilder(128);
                uint focusedProcess;
                uint focusedThread = GetWindowThreadProcessId(focused, out focusedProcess);
                // Accept only an exact registered native edit class. Superclass aliases
                // stay unknown; do not ask another window to identify its underlying type.
                int length = GetClassName(focused, name, name.Capacity);
                int style = GetWindowLong(focused, -16);
                plain = length > 0 && IsNativeEditClass(name.ToString()) && style != 0 &&
                    (style & 0x0020) == 0 && focusedProcess == processId && focusedThread == threadId;
            }
            plain = plain && KeyboardFocusMonitor.IsRunning && version == KeyboardFocusMonitor.Version;
            return new KeyboardFocusSnapshot(foreground, processId, threadId, focused,
                null, plain, version, capturedAt);
        }

        internal static bool IsNativeEditClass(string name)
        {
            return String.Equals(name, "Edit", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "RichEdit20A", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "RICHEDIT50W", StringComparison.OrdinalIgnoreCase);
        }
        internal bool StillMatchesCurrentTarget()
        { return IsSameNativeInput(this, CaptureCheap()); }

        internal static bool IsSameNativeInput(KeyboardFocusSnapshot expected, KeyboardFocusSnapshot current)
        {
            return expected != null && current != null && expected.HasNativeInputIdentity &&
                current.HasNativeInputIdentity && expected.FocusVersion == current.FocusVersion &&
                IsSameTarget(expected, current);
        }
        internal static bool IsSameTarget(KeyboardFocusSnapshot expected, KeyboardFocusSnapshot current)
        {
            if (expected == null || current == null || !expected.IsComplete || !current.IsComplete) return false;
            if (expected.ForegroundWindow != current.ForegroundWindow || expected.ProcessId != current.ProcessId ||
                expected.ThreadId != current.ThreadId || expected.FocusedWindow != current.FocusedWindow) return false;
            return SameRuntimeId(expected._automationRuntimeId, current._automationRuntimeId);
        }
        internal static bool SameRuntimeId(int[] expected, int[] current)
        {
            if (expected == null || current == null) return expected == null && current == null;
            if (expected.Length == 0 || expected.Length != current.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (expected[i] != current[i]) return false;
            return true;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int left, top, right, bottom;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);
    }
}
