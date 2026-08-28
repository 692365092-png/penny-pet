using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PennyPet
{
    internal static class WindowsStickyNoteLinkDetector
    {
        private static readonly Regex LocalPath = new Regex(
            @"(?im)(?:^|(?<=[\s\(\[\{（【《]))(?<value>(?:[a-z]:\\|\\\\)[^\r\n]+)",
            RegexOptions.Compiled);

        private const string TrailingPunctuation =
            " \t.,;:!?，。；：！？、)]}）】》>\"'";

        internal static IList<StickyLinkMatch> Find(string text)
        {
            List<StickyLinkMatch> result = new List<StickyLinkMatch>(
                StickyNoteLinkDetector.FindWebAddresses(text));
            if (!String.IsNullOrEmpty(text)) AddWindowsPaths(result, text);
            result.Sort(delegate(StickyLinkMatch left, StickyLinkMatch right)
            {
                return left.Start.CompareTo(right.Start);
            });
            return result;
        }

        private static void AddWindowsPaths(List<StickyLinkMatch> result,
            string text)
        {
            foreach (Match match in LocalPath.Matches(text))
            {
                Group group = match.Groups["value"];
                int length = group.Length;
                while (length > 0 && TrailingPunctuation.IndexOf(
                    group.Value[length - 1]) >= 0) length--;
                if (length <= 0) continue;
                string value = group.Value.Substring(0, length);
                string target = value.Trim().Trim('"');
                if (!IsWindowsRootedPath(target) ||
                    Overlaps(result, group.Index, length)) continue;
                result.Add(new StickyLinkMatch(group.Index, length, value,
                    target, true));
            }
        }

        private static bool IsWindowsRootedPath(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            if (value.StartsWith("\\\\", StringComparison.Ordinal))
                return true;
            return value.Length >= 3 &&
                ((value[0] >= 'A' && value[0] <= 'Z') ||
                 (value[0] >= 'a' && value[0] <= 'z')) &&
                value[1] == ':' && value[2] == '\\';
        }

        private static bool Overlaps(List<StickyLinkMatch> matches,
            int start, int length)
        {
            int end = start + length;
            foreach (StickyLinkMatch match in matches)
                if (start < match.Start + match.Length &&
                    end > match.Start) return true;
            return false;
        }
    }

    internal enum StickyLinkOpenResult
    {
        Opened,
        Missing,
        Cancelled,
        Failed
    }

    // Windows detection, filesystem probing and Shell launch remain here;
    // risk classification lives beside it in StickyLinkPolicy.
    internal static class StickyLinkService
    {
        internal static StickyLinkOpenResult Open(string target,
            bool fileTarget,
            Func<StickyLinkOpenRisk, string, bool> confirm,
            out Exception error)
        {
            error = null;
            if (String.IsNullOrWhiteSpace(target))
                return StickyLinkOpenResult.Missing;

            // Confirm before probing the filesystem. This is especially
            // important for UNC paths, whose existence check can contact a
            // remote machine and disclose activity before user consent.
            StickyLinkOpenRisk risk = StickyLinkPolicy.Classify(target,
                fileTarget);
            if (risk != StickyLinkOpenRisk.None &&
                (confirm == null || !confirm(risk, target)))
                return StickyLinkOpenResult.Cancelled;
            if (fileTarget && !File.Exists(target) &&
                !Directory.Exists(target))
                return StickyLinkOpenResult.Missing;
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

        internal static bool WindowsPolicyIsSafeForTest()
        {
            IList<StickyLinkMatch> links = WindowsStickyNoteLinkDetector.Find(
                "C:\\Tools\\setup.EXE。\r\n" +
                "\\\\server\\share\\report.pdf\r\n" +
                "https://example.com/app.exe");
            return links.Count == 3 && links[0].IsFileTarget &&
                links[1].IsFileTarget && !links[2].IsFileTarget &&
                StickyLinkPolicy.Classify(links[0].Target, true) ==
                    StickyLinkOpenRisk.ExecutableOrScript &&
                StickyLinkPolicy.Classify(links[1].Target, true) ==
                    StickyLinkOpenRisk.NetworkShare &&
                StickyLinkPolicy.Classify("C:\\Docs\\target.lnk", true) ==
                    StickyLinkOpenRisk.Shortcut &&
                StickyLinkPolicy.Classify(links[2].Target, false) ==
                    StickyLinkOpenRisk.None &&
                StickyLinkPolicy.ConfirmationMessage(
                    StickyLinkOpenRisk.ExecutableOrScript,
                    links[0].Target).IndexOf("确定继续",
                    StringComparison.Ordinal) >= 0;
        }
    }
}
