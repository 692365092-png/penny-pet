using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PennyPet
{
    internal sealed class GlobalKeyboardActivity : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private IntPtr _hook;
        private HookProc _callback;
        private uint _lastVirtualKeyCode;
        private uint _lastKeyTime;
        private int _repeatCount;
        private readonly HashSet<uint> _pressedKeys = new HashSet<uint>();

        public event EventHandler<KeyboardInputEventArgs> Activity;
        public bool IsRunning { get { return _hook != IntPtr.Zero; } }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _pressedKeys.Clear();
            _callback = HookCallback;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                _hook = SetWindowsHookEx(WhKeyboardLl, _callback,
                    GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _pressedKeys.Clear();
            _callback = null;
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int message = wParam.ToInt32();
                bool keyDown = message == WmKeyDown ||
                    message == WmSysKeyDown;
                bool keyUp = message == WmKeyUp || message == WmSysKeyUp;
                if (code >= 0 && (keyDown || keyUp))
                {
                    KeyboardHookData data = (KeyboardHookData)Marshal.PtrToStructure(
                        lParam, typeof(KeyboardHookData));
                    const uint injectedFlag = 0x10;
                    bool injected = (data.Flags & injectedFlag) != 0;
                    if (ShouldPublishKey(injected))
                    {
                        if (keyUp)
                            _pressedKeys.Remove(data.VirtualKeyCode);
                        else
                        {
                            bool alreadyPressed = !_pressedKeys.Add(
                                data.VirtualKeyCode);
                            // Keep identity capture synchronous: privacy is
                            // fail-closed only if the snapshot belongs to the
                            // exact target that received this key-down event.
                            KeyboardFocusSnapshot focus =
                                KeyboardFocusSnapshot.Capture();
                            if (ShouldPublishKeyDown(alreadyPressed))
                            {
                                string display = KeyboardInputFormatter.Format(
                                    (int)data.VirtualKeyCode);
                                _repeatCount = NextRepeatCount(_lastVirtualKeyCode,
                                    data.VirtualKeyCode, _lastKeyTime, data.Time,
                                    _repeatCount);
                                _lastVirtualKeyCode = data.VirtualKeyCode;
                                _lastKeyTime = data.Time;
                                EventHandler<KeyboardInputEventArgs> handler =
                                    Activity;
                                if (handler != null)
                                    handler(this, new KeyboardInputEventArgs(
                                        (int)data.VirtualKeyCode, display,
                                        _repeatCount, focus));
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // A global hook must always continue the Windows hook chain.
                ApplicationDiagnostics.ReportNonFatal("keyboard-hook", error);
            }
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        internal static bool ShouldPublishKey(bool injected)
        {
            return !injected;
        }

        internal static int NextRepeatCount(uint previousKey, uint currentKey,
            uint previousTime, uint currentTime, int previousCount)
        {
            uint elapsed = unchecked(currentTime - previousTime);
            if (previousKey == currentKey && previousCount > 0 && elapsed <= 1400)
                return previousCount + 1;
            return 1;
        }

        internal static bool ShouldPublishKeyDown(bool alreadyPressed)
        {
            return !alreadyPressed;
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardHookData
        {
            public uint VirtualKeyCode;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback,
            IntPtr moduleHandle, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

    }
}
