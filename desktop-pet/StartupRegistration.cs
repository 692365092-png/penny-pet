using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PennyPet
{
    // Windows startup registration is kept separate from reminder UI so this
    // operating-system integration can be found and reviewed independently.
    internal static class StartupRegistration
    {
        private const string RunKeyPath =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "PennyPet";

        internal static string BuildCommand(string executablePath)
        {
            return "\"" + (executablePath ?? String.Empty)
                .Replace("\"", String.Empty) + "\"";
        }

        public static bool Apply(bool enabled, out string error)
        {
            error = String.Empty;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    RunKeyPath))
                {
                    if (key == null)
                        throw new InvalidOperationException(
                            "无法打开当前用户启动项。");
                    object existing = key.GetValue(ValueName, null,
                        RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (enabled)
                    {
                        string desired = BuildCommand(
                            Application.ExecutablePath);
                        if (!String.Equals(Convert.ToString(existing), desired,
                            StringComparison.Ordinal))
                            key.SetValue(ValueName, desired,
                                RegistryValueKind.String);
                    }
                    else if (existing != null)
                        key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
