using System;
using System.Globalization;

namespace PennyPet
{
    internal enum PetSentenceRole
    {
        Single,
        Opening,
        Middle,
        Closing
    }

    internal enum PetSentenceIntent
    {
        Statement,
        Question,
        Gentle,
        Cheerful,
        Serious
    }

    internal enum PetSentenceContentKind
    {
        Greeting,
        Solar,
        Weather,
        Almanac,
        Birthday,
        Curated,
        Zodiac,
        SmallTalk
    }

    internal sealed class PetSentenceEndingContext
    {
        internal PetSentenceEndingContext(PetSentenceRole role,
            PetSentenceIntent intent, PetSentenceContentKind contentKind,
            string stableContentId, DateTime localDate)
        {
            Role = role;
            Intent = intent;
            ContentKind = contentKind;
            StableContentId = stableContentId ?? String.Empty;
            LocalDate = localDate.Date;
        }

        internal PetSentenceRole Role { get; private set; }
        internal PetSentenceIntent Intent { get; private set; }
        internal PetSentenceContentKind ContentKind { get; private set; }
        internal string StableContentId { get; private set; }
        internal DateTime LocalDate { get; private set; }
    }

    internal static class PetSentenceEndingPolicy
    {
        private static readonly string[] Period = { "。" };
        private static readonly string[] QuestionMark = { "？" };
        private static readonly string[] ExclamationMark = { "！" };
        private static readonly string[] Question = { "？", "呀？", "呢？" };
        private static readonly string[] CheerfulOpening =
            { "！", "啦～", "呀～" };
        private static readonly string[] CheerfulClosing =
            { "！", "啦～", "呀～", "耶～" };
        private static readonly string[] Serious = { "。", "。", "！" };
        private static readonly string[] SeriousWeather =
            { "。", "。", "！", "喔～" };
        private static readonly string[] GreetingOpening =
            { "。", "～", "呀～" };
        private static readonly string[] Solar = { "。", "喔～", "哦～" };
        private static readonly string[] Weather =
            { "。", "喔～", "哦～", "呢～" };
        private static readonly string[] Almanac =
            { "。", "喔～", "呢～", "啦～" };
        private static readonly string[] Curated =
            { "。", "呢～", "啦～", "喔～", "呀～" };
        private static readonly string[] Zodiac =
            { "。", "呢～", "喔～", "呀～" };
        private static readonly string[] GenericSoft =
            { "。", "呢～", "喔～" };

        internal static string Apply(string body,
            PetSentenceEndingContext context)
        {
            if (context == null)
                throw new ArgumentNullException("context");
            string cleanBody = NormalizeBody(body);
            if (cleanBody.Length == 0) return cleanBody;
            string[] endings = SelectPool(context);
            string seed = context.LocalDate.ToString("yyyyMMdd",
                CultureInfo.InvariantCulture) + "|" + context.Role + "|" +
                context.Intent + "|" + context.ContentKind + "|" +
                context.StableContentId + "|" + cleanBody;
            return ApplyEnding(cleanBody, endings[StableIndex(seed,
                endings.Length)]);
        }

        internal static string ApplyEnding(string body, string ending)
        {
            string cleanBody = NormalizeBody(body);
            if (cleanBody.Length == 0 || String.IsNullOrEmpty(ending))
                return cleanBody;
            if (ending == "啦～" && cleanBody.EndsWith("了",
                StringComparison.Ordinal))
                return cleanBody.Substring(0, cleanBody.Length - 1) + ending;
            if (ending == "啦～" && cleanBody.EndsWith("啦",
                StringComparison.Ordinal))
                return cleanBody + "～";
            return cleanBody + ending;
        }

        private static string[] SelectPool(PetSentenceEndingContext context)
        {
            if (context.Role == PetSentenceRole.Middle)
            {
                if (context.Intent == PetSentenceIntent.Question)
                    return QuestionMark;
                if (context.Intent == PetSentenceIntent.Cheerful)
                    return ExclamationMark;
                return Period;
            }
            if (context.Intent == PetSentenceIntent.Question)
                return Question;
            if (context.Intent == PetSentenceIntent.Serious)
                return context.ContentKind == PetSentenceContentKind.Weather
                    ? SeriousWeather : Serious;
            if (context.Intent == PetSentenceIntent.Cheerful)
                return context.Role == PetSentenceRole.Opening
                    ? CheerfulOpening : CheerfulClosing;
            if (context.Role == PetSentenceRole.Opening &&
                context.ContentKind == PetSentenceContentKind.Greeting)
                return GreetingOpening;
            switch (context.ContentKind)
            {
                case PetSentenceContentKind.Solar: return Solar;
                case PetSentenceContentKind.Weather: return Weather;
                case PetSentenceContentKind.Almanac: return Almanac;
                case PetSentenceContentKind.Curated: return Curated;
                case PetSentenceContentKind.Zodiac: return Zodiac;
                default: return GenericSoft;
            }
        }

        private static string NormalizeBody(string body)
        {
            return (body ?? String.Empty).Trim().TrimEnd(
                '。', '！', '？', '.', '!', '?', '～');
        }

        private static int StableIndex(string seed, int count)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in seed)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)(hash % (uint)count);
            }
        }
    }
}
