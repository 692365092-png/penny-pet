using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using WF = System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu,
            IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr window);
        private static void FocusNativePrivacyFixture(IntPtr control)
        {
            SetFocus(control);
            WF.Application.DoEvents();
        }

        private static bool RunKeyboardPrivacyNativeChecks()
        {
            using (var monitor = new KeyboardFocusMonitor(() => { }))
            {
                monitor.Start();
                Pc2Assert(KeyboardFocusMonitor.IsRunning, "focus monitoring must be available in native QA");
                using (var host = new WF.Form())
                {
                    host.Show(); host.Activate(); WF.Application.DoEvents();
                    IntPtr plain = CreateWindowEx(0, "Edit", "", 0x50000000,
                        10, 10, 180, 24, host.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    IntPtr password = CreateWindowEx(0, "Edit", "", 0x50000020,
                        10, 50, 180, 24, host.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    Pc2Assert(plain != IntPtr.Zero && password != IntPtr.Zero, "create native Edit fixtures");
                    FocusNativePrivacyFixture(plain);
                    var first = KeyboardFocusSnapshot.CaptureForForegroundWindow(host.Handle);
                    Pc2Assert(first.FocusedWindow == plain && first.HasNativeInputIdentity,
                        "native Edit identity: expected=" + plain + " focused=" + first.FocusedWindow +
                        " foreground=" + first.ForegroundWindow + " host=" + host.Handle +
                        " proof=" + first.HasNativeInputIdentity + " version=" + first.FocusVersion);
                    FocusNativePrivacyFixture(password);
                    var secret = KeyboardFocusSnapshot.CaptureForForegroundWindow(host.Handle);
                    Pc2Assert(secret.FocusedWindow == password && !secret.HasNativeInputIdentity,
                        "password Edit is rejected before formatting a keyboard label");
                    FocusNativePrivacyFixture(plain);
                    Pc2Assert(!KeyboardFocusSnapshot.IsSameNativeInput(first, KeyboardFocusSnapshot.CaptureCheap()),
                        "leaving and returning to the same HWND cannot revive a previous result");
                    host.Close(); // destroys both native child windows
                }
                // WPF TextBox and PasswordBox have the same owning HWND: neither
                // is granted a native input identity, even when the later UIA target is safe.
                var panel = new StackPanel();
                var text = new TextBox();
                var passwordBox = new PasswordBox();
                panel.Children.Add(text); panel.Children.Add(passwordBox);
                var window = new Window { Content = panel, Width = 320, Height = 200 };
                try
                {
                    window.Show(); window.Activate(); WF.Application.DoEvents();
                    IntPtr wpfHwnd = new WindowInteropHelper(window).Handle;
                    text.Focus(); Keyboard.Focus(text); WF.Application.DoEvents();
                    var ordinary = KeyboardFocusSnapshot.CaptureForForegroundWindow(wpfHwnd);
                    passwordBox.Focus(); Keyboard.Focus(passwordBox); WF.Application.DoEvents();
                    var secret = KeyboardFocusSnapshot.CaptureForForegroundWindow(wpfHwnd);
                    text.Focus(); Keyboard.Focus(text); WF.Application.DoEvents();
                    var returned = KeyboardFocusSnapshot.CaptureForForegroundWindow(wpfHwnd);
                    Pc2Assert(ordinary.IsComplete && secret.IsComplete && returned.IsComplete &&
                        ordinary.FocusedWindow == secret.FocusedWindow && !ordinary.HasNativeInputIdentity &&
                        !secret.HasNativeInputIdentity && !returned.HasNativeInputIdentity,
                        "same-HWND WPF password/plain transitions remain uncorrelated and suppressed");
                }
                finally { window.Close(); }
            }
            return true;
        }
    }
}
