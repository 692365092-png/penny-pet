using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PennyPet
{
    internal sealed class StickyLinkMatch
    {
        internal StickyLinkMatch(int start, int length, string text,
            string target, bool fileTarget)
        {
            Start = start;
            Length = length;
            Text = text;
            Target = target;
            IsFileTarget = fileTarget;
        }

        internal int Start { get; private set; }
        internal int Length { get; private set; }
        internal string Text { get; private set; }
        internal string Target { get; private set; }
        internal bool IsFileTarget { get; private set; }
    }

    // Shared HTTP(S) text detection. Each platform adds its own file-target
    // syntax before presenting the combined matches.
    internal static class StickyNoteLinkDetector
    {
        private static readonly Regex WebAddress = new Regex(
            @"(?i)(?:^|(?<=[\s\(\[\{（【《]))(?<value>(?:https?://|www\.)[^\s<>\""']+)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private const string TrailingPunctuation =
            " \t.,;:!?，。；：！？、)]}）】》>\"'";

        internal static IList<StickyLinkMatch> FindWebAddresses(string text)
        {
            List<StickyLinkMatch> result = new List<StickyLinkMatch>();
            if (String.IsNullOrEmpty(text)) return result;
            AddMatches(result, text, WebAddress);
            return result;
        }

        private static void AddMatches(List<StickyLinkMatch> result,
            string text, Regex expression)
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
                if (!TryNormalizeWebTarget(value, out target)) continue;
                result.Add(new StickyLinkMatch(valueGroup.Index, length,
                    value, target, false));
            }
        }

        private static bool TryNormalizeWebTarget(string value,
            out string target)
        {
            target = String.Empty;
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
    }
}
