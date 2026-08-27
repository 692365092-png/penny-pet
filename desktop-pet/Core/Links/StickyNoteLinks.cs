using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PennyPet
{
    internal sealed class StickyLinkMatch
    {
        internal StickyLinkMatch(int start, int length, string text,
            string target, bool localPath)
        {
            Start = start;
            Length = length;
            Text = text;
            Target = target;
            IsLocalPath = localPath;
        }

        internal int Start { get; private set; }
        internal int Length { get; private set; }
        internal string Text { get; private set; }
        internal string Target { get; private set; }
        internal bool IsLocalPath { get; private set; }
    }

    // Pure text detection shared by the desktop adapter and standard tests.
    // It deliberately does not use Path.IsPathRooted because that method
    // interprets Windows paths differently on non-Windows hosts.
    internal static class StickyNoteLinkDetector
    {
        private static readonly Regex WebAddress = new Regex(
            @"(?i)(?:^|(?<=[\s\(\[\{（【《]))(?<value>(?:https?://|www\.)[^\s<>\""']+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex LocalPath = new Regex(
            @"(?im)(?:^|(?<=[\s\(\[\{（【《]))(?<value>(?:[a-z]:\\|\\\\)[^\r\n]+)",
            RegexOptions.Compiled);

        private const string TrailingPunctuation =
            " \t.,;:!?，。；：！？、)]}）】》>\"'";

        internal static IList<StickyLinkMatch> Find(string text)
        {
            List<StickyLinkMatch> result = new List<StickyLinkMatch>();
            if (String.IsNullOrEmpty(text)) return result;
            AddMatches(result, text, WebAddress, false);
            AddMatches(result, text, LocalPath, true);
            result.Sort(delegate(StickyLinkMatch left, StickyLinkMatch right)
            {
                return left.Start.CompareTo(right.Start);
            });
            return result;
        }

        private static void AddMatches(List<StickyLinkMatch> result,
            string text, Regex expression, bool localPath)
        {
            foreach (Match match in expression.Matches(text))
            {
                Group valueGroup = match.Groups["value"];
                int length = valueGroup.Length;
                while (length > 0 && TrailingPunctuation.IndexOf(
                    valueGroup.Value[length - 1]) >= 0)
                    length--;
                if (length <= 0) continue;
                string value = valueGroup.Value.Substring(0, length);
                string target;
                if (!TryNormalizeTarget(value, localPath, out target)) continue;
                if (Overlaps(result, valueGroup.Index, length)) continue;
                result.Add(new StickyLinkMatch(valueGroup.Index, length,
                    value, target, localPath));
            }
        }

        private static bool TryNormalizeTarget(string value, bool localPath,
            out string target)
        {
            target = String.Empty;
            if (localPath)
            {
                string candidate = value.Trim().Trim('"');
                if (!IsWindowsRootedPath(candidate)) return false;
                target = candidate;
                return true;
            }

            string candidateUrl = value.StartsWith("www.",
                StringComparison.OrdinalIgnoreCase) ? "https://" + value : value;
            Uri uri;
            if (!Uri.TryCreate(candidateUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps))
                return false;
            target = uri.AbsoluteUri;
            return true;
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
            {
                int matchEnd = match.Start + match.Length;
                if (start < matchEnd && end > match.Start) return true;
            }
            return false;
        }
    }
}
