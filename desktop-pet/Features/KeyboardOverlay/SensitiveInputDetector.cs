using System;
using System.Diagnostics;
using System.Windows.Automation;

namespace PennyPet
{
    // Called only by the single no-window MTA worker. Unknown evidence denies display.
    internal static class SensitiveInputDetector
    {
        internal static bool IsSensitiveFocus(KeyboardFocusSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasNativeInputIdentity || !snapshot.StillMatchesCurrentTarget())
                return true;
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                if (focused == null) return true;
                int[] runtimeId = focused.GetRuntimeId();
                if (runtimeId == null || runtimeId.Length == 0) return true;
                object password = focused.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true);
                bool matches = focused.Current.NativeWindowHandle == snapshot.FocusedWindow.ToInt64() &&
                    focused.Current.ProcessId == snapshot.ProcessId && focused.Current.HasKeyboardFocus &&
                    focused.Current.ControlType == ControlType.Edit;
                bool credential;
                using (Process process = Process.GetProcessById((int)snapshot.ProcessId))
                {
                    string name = process.ProcessName;
                    credential = name.Equals("CredentialUIBroker", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("LogonUI", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("consent", StringComparison.OrdinalIgnoreCase);
                }
                string label = focused.Current.Name ?? String.Empty;
                credential |= ContainsSensitiveWord(label);
                AutomationElement after = AutomationElement.FocusedElement;
                bool sameElement = after != null && KeyboardFocusSnapshot.SameRuntimeId(runtimeId, after.GetRuntimeId());
                return !PetKeyboardPrivacyPolicy.CanPublishNativeInput(snapshot.HasNativeInputIdentity,
                    snapshot.StillMatchesCurrentTarget(), password as bool?, matches && sameElement, credential);
            }
            catch
            {
                // Provider errors, missing properties, exited processes and access failures are unknown.
                return true;
            }
        }
        private static bool ContainsSensitiveWord(string value)
        {
            string text = (value ?? String.Empty).ToLowerInvariant();
            return text.Contains("password") || text.Contains("passwd") || text.Contains("credential") ||
                text.Contains("passcode") || text.Contains("密码") || text.Contains("口令");
        }
    }
}
