using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WF = System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        private static bool RunKeyboardPrivacyNativeChecks()
        {
            using (var monitor = new KeyboardFocusMonitor(() => { }))
            {
                monitor.Start();
                Pc2Assert(KeyboardFocusMonitor.IsRunning, "focus monitoring must be available in native QA");
                using (var host = new WF.Form())
                using (var plain = new WF.TextBox())
                using (var password = new WF.TextBox { UseSystemPasswordChar = true, Top = 40 })
                {
                    host.Controls.Add(plain); host.Controls.Add(password);
                    host.Show(); host.Activate(); plain.Focus(); WF.Application.DoEvents();
                    var first = KeyboardFocusSnapshot.CaptureCheap();
                    Pc2Assert(first.FocusedWindow == plain.Handle && first.HasNativeInputIdentity,
                        "standard native edit supplies an event-time control identity");
                    password.Focus(); WF.Application.DoEvents();
                    var secret = KeyboardFocusSnapshot.CaptureCheap();
                    Pc2Assert(secret.FocusedWindow == password.Handle && !secret.HasNativeInputIdentity,
                        "password edit is rejected before formatting a keyboard label");
                    plain.Focus(); WF.Application.DoEvents();
                    Pc2Assert(!KeyboardFocusSnapshot.IsSameNativeInput(first, KeyboardFocusSnapshot.CaptureCheap()),
                        "leaving and returning to the same HWND cannot revive a previous result");
                    host.Close();
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
                    window.Show(); window.Activate(); text.Focus(); Keyboard.Focus(text); WF.Application.DoEvents();
                    var ordinary = KeyboardFocusSnapshot.CaptureCheap();
                    passwordBox.Focus(); Keyboard.Focus(passwordBox); WF.Application.DoEvents();
                    var secret = KeyboardFocusSnapshot.CaptureCheap();
                    text.Focus(); Keyboard.Focus(text); WF.Application.DoEvents();
                    var returned = KeyboardFocusSnapshot.CaptureCheap();
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
