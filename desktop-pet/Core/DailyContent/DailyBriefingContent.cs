namespace PennyPet
{
    internal sealed class DailyBriefingContent
    {
        internal DailyBriefingContent(SolarTermInfo? solarTerm,
            string almanacLine, DailyLineEntry curatedLine,
            DailyLineEntry zodiacLine)
        {
            SolarTerm = solarTerm;
            AlmanacLine = almanacLine;
            CuratedLine = curatedLine;
            ZodiacLine = zodiacLine;
        }

        internal SolarTermInfo? SolarTerm { get; private set; }
        internal string AlmanacLine { get; private set; }
        internal DailyLineEntry CuratedLine { get; private set; }
        internal DailyLineEntry ZodiacLine { get; private set; }
    }
}
