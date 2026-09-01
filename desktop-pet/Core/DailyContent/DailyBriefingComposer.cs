using System;

namespace PennyPet
{
    // Turns structured daily facts into the
    // short natural-language DailyGreeting text. It performs no astronomy,
    // clock, settings, or Bubble work.
    internal static class DailyBriefingComposer
    {
        internal static string Compose(DayPart dayPart,
            SolarTermInfo? solarTerm, string zodiacText)
        {
            string text = DailyContentRules.GreetingFor(dayPart);
            if (solarTerm.HasValue)
                text += "\n今天是" + solarTerm.Value.ChineseName + "哦。";
            if (!String.IsNullOrWhiteSpace(zodiacText))
                text += "\n" + zodiacText;
            return text;
        }
    }
}
