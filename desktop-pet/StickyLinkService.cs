using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PennyPet
{
    internal enum StickyLinkOpenRisk
    {
        None,
        ExecutableOrScript,
        Shortcut,
        NetworkShare
    }

    internal enum StickyLinkOpenResult
    {
        Opened,
        Missing,
        Cancelled,
        Failed
    }

    // Windows shell-opening policy is kept out of the WPF editor. Detection
    // stays in StickyNoteLinkDetector; this service classifies risky targets,
    // requests confirmation and performs the final ShellExecute operation.
    internal static class StickyLinkService
    {
        private static readonly HashSet<string> ExecutableOrScriptExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1",
                ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
                ".scr", ".msi", ".msp", ".reg", ".hta", ".cpl"
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
            string extension;
            try { extension = Path.GetExtension(target) ?? String.Empty; }
            catch { extension = String.Empty; }
            if (ExecutableOrScriptExtensions.Contains(extension))
                return StickyLinkOpenRisk.ExecutableOrScript;
            if (ShortcutExtensions.Contains(extension))
                return StickyLinkOpenRisk.Shortcut;
            if (target.StartsWith("\\\\", StringComparison.Ordinal))
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
            else
                description = "这是网络共享路径，内容来自其他电脑或服务器。";
            return description + "\n\n请只打开你信任的来源：\n" +
                (target ?? String.Empty) + "\n\n确定继续吗？";
        }

        internal static StickyLinkOpenResult Open(string target,
            bool localPath,
            Func<StickyLinkOpenRisk, string, bool> confirm,
            out Exception error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(target))
                return StickyLinkOpenResult.Missing;
            if (localPath && !File.Exists(target) &&
                !Directory.Exists(target))
                return StickyLinkOpenResult.Missing;

            StickyLinkOpenRisk risk = Classify(target, localPath);
            if (risk != StickyLinkOpenRisk.None &&
                (confirm == null || !confirm(risk, target)))
                return StickyLinkOpenResult.Cancelled;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
                return StickyLinkOpenResult.Opened;
            }
            catch (Exception caught)
            {
                error = caught;
                return StickyLinkOpenResult.Failed;
            }
        }
    }
}
