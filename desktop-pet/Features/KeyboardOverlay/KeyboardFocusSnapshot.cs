using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace PennyPet
{
    // Identity captured synchronously with a low-level keyboard event. Privacy
    // inspection may run later, but it is valid only while this exact target
    // (window, process, GUI thread, focused HWND and UIA element) is unchanged.
    internal sealed class KeyboardFocusSnapshot
    {
        private readonly int[] _automationRuntimeId;

        internal KeyboardFocusSnapshot(IntPtr foregroundWindow,
            uint processId, uint threadId, IntPtr focusedWindow,
            int[] automationRuntimeId)
        {
            ForegroundWindow = foregroundWindow;
            ProcessId = processId;
            ThreadId = threadId;
            FocusedWindow = focusedWindow;
            _automationRuntimeId = automationRuntimeId == null
                ? null : (int[])automationRuntimeId.Clone();
        }

        internal IntPtr ForegroundWindow { get; private set; }
        internal uint ProcessId { get; private set; }
        internal uint ThreadId { get; private set; }
        internal IntPtr FocusedWindow { get; private set; }

        internal bool IsComplete
        {
            get
            {
                return ForegroundWindow != IntPtr.Zero && ProcessId != 0 &&
                    ThreadId != 0 && FocusedWindow != IntPtr.Zero &&
                    _automationRuntimeId != null &&
                    _automationRuntimeId.Length > 0;
            }
        }

        internal static KeyboardFocusSnapshot Capture()
        {
            IntPtr foreground = IntPtr.Zero;
            uint processId = 0;
            uint threadId = 0;
            IntPtr focused = IntPtr.Zero;
            int[] runtimeId = null;
            try
            {
                foreground = GetForegroundWindow();
                threadId = GetWindowThreadProcessId(foreground, out processId);
                GuiThreadInfo info = new GuiThreadInfo();
                info.cbSize = Marshal.SizeOf(typeof(GuiThreadInfo));
                if (threadId != 0 && GetGUIThreadInfo(threadId, ref info))
                    focused = info.hwndFocus;
                AutomationElement element = AutomationElement.FocusedElement;
                if (element != null) runtimeId = element.GetRuntimeId();
            }
            catch
            {
                // An incomplete snapshot deliberately fails closed later.
            }
            return new KeyboardFocusSnapshot(foreground, processId, threadId,
                focused, runtimeId);
        }

        internal bool StillMatchesCurrentTarget()
        {
            return IsSameTarget(this, Capture());
        }

        internal static bool IsSameTarget(KeyboardFocusSnapshot expected,
            KeyboardFocusSnapshot current)
        {
            if (expected == null || current == null || !expected.IsComplete ||
                !current.IsComplete) return false;
            if (expected.ForegroundWindow != current.ForegroundWindow ||
                expected.ProcessId != current.ProcessId ||
                expected.ThreadId != current.ThreadId ||
                expected.FocusedWindow != current.FocusedWindow ||
                expected._automationRuntimeId.Length !=
                    current._automationRuntimeId.Length) return false;
            for (int index = 0; index < expected._automationRuntimeId.Length;
                index++)
                if (expected._automationRuntimeId[index] !=
                    current._automationRuntimeId[index]) return false;
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public NativeRect rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window,
            out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId,
            ref GuiThreadInfo info);
    }
}
