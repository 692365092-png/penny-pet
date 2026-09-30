using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PennyPet
{
    // Out-of-context WinEvent callbacks are delivered on the registering Pet STA.
    // No UIA work occurs here. Every focus transition invalidates queued output.
    internal sealed class KeyboardFocusMonitor : IDisposable
    {
        private static long _version;
        private static int _running;
        private readonly Action _changed;
        private readonly WinEventProc _callback;
        private readonly Func<uint, IntPtr> _register;
        private readonly Action<IntPtr> _unregister;
        private readonly IntPtr[] _hooks = new IntPtr[3];
        internal static long Version { get { return Interlocked.Read(ref _version); } }
        internal static bool IsRunning { get { return Volatile.Read(ref _running) != 0; } }
        internal bool IsActive { get { return _hooks[2] != IntPtr.Zero; } }
        internal KeyboardFocusMonitor(Action changed,
            Func<uint, IntPtr> register = null, Action<IntPtr> unregister = null)
        {
            _changed = changed;
            _callback = Changed;
            _register = register ?? (eventId => SetWinEventHook(eventId, eventId,
                IntPtr.Zero, _callback, 0, 0, 0));
            _unregister = unregister ?? (hook => { UnhookWinEvent(hook); });
        }
        internal bool Start()
        {
            if (IsActive) return true;
            uint[] events = { 3, 0x8005, 0x800A }; // foreground, focus, control state
            for (int i = 0; i < events.Length; i++)
            {
                _hooks[i] = _register(events[i]);
                if (_hooks[i] == IntPtr.Zero) { Dispose(); return false; }
            }
            Interlocked.Increment(ref _version);
            Volatile.Write(ref _running, 1);
            return true;
        }
        private void Changed(IntPtr hook, uint eventType, IntPtr hwnd, int objectId,
            int childId, uint thread, uint time)
        {
            if (eventType == 0x800A && hwnd != KeyboardFocusSnapshot.CaptureCheap().FocusedWindow) return;
            Interlocked.Increment(ref _version);
            _changed();
        }
        public void Dispose()
        {
            Volatile.Write(ref _running, 0);
            Interlocked.Increment(ref _version);
            for (int i = 0; i < _hooks.Length; i++)
                if (_hooks[i] != IntPtr.Zero) { _unregister(_hooks[i]); _hooks[i] = IntPtr.Zero; }
        }
        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd,
            int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
            WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);
    }
}
