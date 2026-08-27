using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal enum StickyLinkOpenRisk
    {
        None,
        ExecutableOrScript,
        Shortcut,
        NetworkShare
    }

    // Side-effect-free Windows target policy. Keeping this in Core makes the
    // security rules testable without starting a shell or touching a path.
    internal static class StickyLinkPolicy
    {
        private static readonly HashSet<string> ExecutableOrScriptExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1",
                ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
                ".scr", ".msi", ".msp", ".reg", ".hta", ".cpl",
                ".application", ".appref-ms", ".gadget", ".msc",
                ".jar", ".inf", ".ins", ".isp", ".sct"
            };

        private static readonly HashSet<string> ShortcutExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".lnk", ".url"
            };

        internal static StickyLinkOpenRisk Classify(string target,
            bool localPath)
        {
            if (!localPath || String.IsNullOrWhiteSpace(target))
                return StickyLinkOpenRisk.None;

            string normalized = target.Trim().Trim('"').TrimEnd(' ', '.');
            string extension = GetWindowsExtension(normalized);
            if (ExecutableOrScriptExtensions.Contains(extension))
                return StickyLinkOpenRisk.ExecutableOrScript;
            if (ShortcutExtensions.Contains(extension))
                return StickyLinkOpenRisk.Shortcut;
            if (normalized.StartsWith("\\\\", StringComparison.Ordinal))
                return StickyLinkOpenRisk.NetworkShare;
            return StickyLinkOpenRisk.None;
        }

        internal static string ConfirmationMessage(StickyLinkOpenRisk risk,
            string target)
        {
            string description;
            if (risk == StickyLinkOpenRisk.ExecutableOrScript)
                description = "这是可执行文件或脚本，打开后可能运行程序或修改电脑。";
            else if (risk == StickyLinkOpenRisk.Shortcut)
                description = "这是快捷方式，实际打开的位置可能与文字显示不同。";
            else if (risk == StickyLinkOpenRisk.NetworkShare)
                description = "这是网络共享路径，内容来自其他电脑或服务器。";
            else
                return String.Empty;
            return description + "\n\n请只打开你信任的来源：\n" +
                (target ?? String.Empty) + "\n\n确定继续吗？";
        }

        private static string GetWindowsExtension(string target)
        {
            if (String.IsNullOrEmpty(target)) return String.Empty;
            int separator = Math.Max(target.LastIndexOf('\\'),
                target.LastIndexOf('/'));
            int dot = target.LastIndexOf('.');
            return dot <= separator || dot == target.Length - 1
                ? String.Empty : target.Substring(dot);
        }
    }
}
