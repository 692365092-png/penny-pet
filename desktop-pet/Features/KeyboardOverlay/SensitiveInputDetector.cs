using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace PennyPet
{
    internal static class SensitiveInputDetector
    {
        private const int GwlStyle = -16;
        private const int EsPassword = 0x0020;

        public static bool IsSensitiveFocus()
        {
            return IsSensitiveFocus(KeyboardFocusSnapshot.Capture());
        }

        public static bool IsSensitiveFocus(KeyboardFocusSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsComplete ||
                !snapshot.StillMatchesCurrentTarget()) return true;
            bool automationPassword = false;
            bool automationInspected = false;
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused != null)
                {
                    object value = focused.GetCurrentPropertyValue(
                        AutomationElement.IsPasswordProperty, true);
                    automationPassword = value is bool && (bool)value;
                    if (!automationPassword)
                    {
                        string name = SafeAutomationName(focused);
                        ControlType type = SafeControlType(focused);
                        if (type == ControlType.Edit &&
                            ContainsSensitiveWord(name))
                            automationPassword = true;
                    }
                    automationInspected = true;
                }
            }
            catch
            {
                // Win32 checks remain available if a UIA provider rejects access.
            }

            bool standardPassword = false;
            bool knownCredentialWindow = false;
            bool nativeInspected = false;
            try
            {
                uint processId = snapshot.ProcessId;
                uint threadId = snapshot.ThreadId;
                GuiThreadInfo info = new GuiThreadInfo();
                info.cbSize = Marshal.SizeOf(typeof(GuiThreadInfo));
                if (threadId != 0 && GetGUIThreadInfo(threadId, ref info) &&
                    info.hwndFocus == snapshot.FocusedWindow)
                {
                    int style = GetWindowLong(info.hwndFocus, GwlStyle);
                    standardPassword = (style & EsPassword) != 0;
                    StringBuilder className = new StringBuilder(256);
                    GetClassName(info.hwndFocus, className, className.Capacity);
                    if (ContainsSensitiveWord(className.ToString()))
                        standardPassword = true;
                    nativeInspected = true;
                }
                if (processId != 0)
                {
                    using (Process process = Process.GetProcessById(
                        (int)processId))
                    {
                        string name = process.ProcessName ?? String.Empty;
                        knownCredentialWindow = name.Equals(
                            "CredentialUIBroker",
                            StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("LogonUI",
                                StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("consent",
                                StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
                // Failure to inspect one fallback must not publish input.
            }
            if (!snapshot.StillMatchesCurrentTarget()) return true;
            return PetKeyboardPrivacyPolicy.ShouldSuppressCapturedInput(
                automationPassword, standardPassword,
                knownCredentialWindow,
                automationInspected || nativeInspected);
        }

        private static string SafeAutomationName(AutomationElement element)
        {
            try { return element.Current.Name ?? String.Empty; }
            catch { return String.Empty; }
        }

        private static ControlType SafeControlType(AutomationElement element)
        {
            try { return element.Current.ControlType; }
            catch { return null; }
        }

        private static bool ContainsSensitiveWord(string value)
        {
            string text = (value ?? String.Empty).ToLowerInvariant();
            return text.Contains("password") || text.Contains("passwd") ||
                text.Contains("credential") || text.Contains("passcode") ||
                text.Contains("密码") || text.Contains("口令");
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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId,
            ref GuiThreadInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr window,
            StringBuilder text, int maximumCount);
    }
}
