namespace PennyPet
{
    internal sealed class DailyBriefingContent
    {
        internal DailyBriefingContent(SolarTermInfo? solarTerm,
            WeatherDailySelection weather,
            AlmanacDailySelection almanac, DailyLineEntry curatedLine,
            DailyLineEntry zodiacLine)
        {
            SolarTerm = solarTerm;
            Weather = weather;
            Almanac = almanac;
            CuratedLine = curatedLine;
            ZodiacLine = zodiacLine;
        }

        internal SolarTermInfo? SolarTerm { get; private set; }
        internal WeatherDailySelection Weather { get; private set; }
        internal AlmanacDailySelection Almanac { get; private set; }
        internal DailyLineEntry CuratedLine { get; private set; }
        internal DailyLineEntry ZodiacLine { get; private set; }
    }

    internal sealed class DailyBriefingSentence
    {
        internal DailyBriefingSentence(string body,
            PetSentenceContentKind kind, PetSentenceIntent intent,
            string stableContentId)
        {
            Body = body;
            Kind = kind;
            Intent = intent;
            StableContentId = stableContentId;
        }

        internal string Body { get; private set; }
        internal PetSentenceContentKind Kind { get; private set; }
        internal PetSentenceIntent Intent { get; private set; }
        internal string StableContentId { get; private set; }
    }
}
