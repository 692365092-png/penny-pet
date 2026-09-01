namespace PennyPet
{
    // Turns structured daily facts (day part + optional solar term) into the
    // short natural-language DailyGreeting text. It performs no astronomy,
    // clock, settings, or Bubble work.
    internal static class DailyBriefingComposer
    {
        internal static string Compose(DayPart dayPart,
            SolarTermInfo? solarTerm)
        {
            string greeting = DailyContentRules.GreetingFor(dayPart);
            if (!solarTerm.HasValue) return greeting;
            return greeting + "\n今天是" +
                solarTerm.Value.ChineseName + "哦。";
        }
    }
}
