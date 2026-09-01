using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Turns structured daily facts into the
    // short natural-language DailyGreeting text. It performs no astronomy,
    // clock, settings, or Bubble work.
    internal static class DailyBriefingComposer
    {
        internal static string Compose(DayPart dayPart,
            DailyBriefingContent content)
        {
            string text = DailyContentRules.GreetingFor(dayPart);
            foreach (string supplementary in SelectSupplementary(content))
                text += "\n" + supplementary;
            return text;
        }

        internal static string[] SelectSupplementary(
            DailyBriefingContent content)
        {
            if (content == null) return new string[0];
            List<string> selected = new List<string>(2);
            if (content.SolarTerm.HasValue)
            {
                selected.Add("今天是" +
                    content.SolarTerm.Value.ChineseName + "哦。");
                if (!String.IsNullOrWhiteSpace(content.WeatherLine))
                    selected.Add(content.WeatherLine);
                else if (!String.IsNullOrWhiteSpace(content.AlmanacLine))
                    selected.Add(content.AlmanacLine);
                return selected.ToArray();
            }
            if (!String.IsNullOrWhiteSpace(content.WeatherLine))
            {
                selected.Add(content.WeatherLine);
                if (!String.IsNullOrWhiteSpace(content.AlmanacLine))
                    selected.Add(content.AlmanacLine);
                return selected.ToArray();
            }
            if (!String.IsNullOrWhiteSpace(content.AlmanacLine))
            {
                selected.Add(content.AlmanacLine);
                return selected.ToArray();
            }
            if (content.CuratedLine != null)
                selected.Add(content.CuratedLine.Text);
            if (content.ZodiacLine != null)
                selected.Add(content.ZodiacLine.Text);
            return selected.ToArray();
        }
    }
}
