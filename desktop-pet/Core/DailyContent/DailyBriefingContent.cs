namespace PennyPet
{
    internal sealed class DailyBriefingContent
    {
        internal DailyBriefingContent(SolarTermInfo? solarTerm,
            DailyLineEntry curatedLine, DailyLineEntry zodiacLine)
        {
            SolarTerm = solarTerm;
            CuratedLine = curatedLine;
            ZodiacLine = zodiacLine;
        }

        internal SolarTermInfo? SolarTerm { get; private set; }
        internal DailyLineEntry CuratedLine { get; private set; }
        internal DailyLineEntry ZodiacLine { get; private set; }
    }
}
