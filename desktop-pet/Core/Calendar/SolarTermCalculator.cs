using System;
using System.Collections.Generic;
using System.Globalization;
using CosineKitty;

namespace PennyPet
{
    // Computes the astronomical instants of the twenty-four solar terms and
    // exposes them as civil dates in the caller's local timezone. It contains
    // no Bubble, DailyGreeting, settings, or persistence knowledge.
    internal static class SolarTermCalculator
    {
        internal const int MinSupportedYear = 2000;
        internal const int MaxSupportedYear = 2100;

        private sealed class Definition
        {
            internal readonly SolarTerm Term;
            internal readonly string ChineseName;
            internal readonly int LongitudeDegrees;
            internal readonly int NominalMonth;
            internal readonly int NominalDay;

            internal Definition(SolarTerm term, string chineseName,
                int longitudeDegrees, int nominalMonth, int nominalDay)
            {
                Term = term;
                ChineseName = chineseName;
                LongitudeDegrees = longitudeDegrees;
                NominalMonth = nominalMonth;
                NominalDay = nominalDay;
            }
        }

        private static readonly Definition[] Definitions = new Definition[]
        {
            new Definition(SolarTerm.MinorCold, "小寒", 285, 1, 5),
            new Definition(SolarTerm.MajorCold, "大寒", 300, 1, 20),
            new Definition(SolarTerm.StartOfSpring, "立春", 315, 2, 4),
            new Definition(SolarTerm.RainWater, "雨水", 330, 2, 19),
            new Definition(SolarTerm.AwakeningOfInsects, "惊蛰", 345, 3, 5),
            new Definition(SolarTerm.VernalEquinox, "春分", 0, 3, 20),
            new Definition(SolarTerm.QingMing, "清明", 15, 4, 4),
            new Definition(SolarTerm.GrainRain, "谷雨", 30, 4, 20),
            new Definition(SolarTerm.StartOfSummer, "立夏", 45, 5, 5),
            new Definition(SolarTerm.GrainFull, "小满", 60, 5, 21),
            new Definition(SolarTerm.GrainInEar, "芒种", 75, 6, 5),
            new Definition(SolarTerm.SummerSolstice, "夏至", 90, 6, 21),
            new Definition(SolarTerm.MinorHeat, "小暑", 105, 7, 7),
            new Definition(SolarTerm.MajorHeat, "大暑", 120, 7, 22),
            new Definition(SolarTerm.StartOfAutumn, "立秋", 135, 8, 7),
            new Definition(SolarTerm.EndOfHeat, "处暑", 150, 8, 23),
            new Definition(SolarTerm.WhiteDew, "白露", 165, 9, 7),
            new Definition(SolarTerm.AutumnalEquinox, "秋分", 180, 9, 23),
            new Definition(SolarTerm.ColdDew, "寒露", 195, 10, 8),
            new Definition(SolarTerm.FrostDescent, "霜降", 210, 10, 23),
            new Definition(SolarTerm.StartOfWinter, "立冬", 225, 11, 7),
            new Definition(SolarTerm.MinorSnow, "小雪", 240, 11, 22),
            new Definition(SolarTerm.MajorSnow, "大雪", 255, 12, 7),
            new Definition(SolarTerm.WinterSolstice, "冬至", 270, 12, 21)
        };

        internal static SolarTermInfo[] CalculateYear(int year)
        {
            if (year < MinSupportedYear || year > MaxSupportedYear)
                return new SolarTermInfo[0];

            List<SolarTermInfo> results =
                new List<SolarTermInfo>(Definitions.Length);
            foreach (Definition definition in Definitions)
            {
                DateTimeOffset? instantUtc = FindInstantUtc(definition, year);
                if (!instantUtc.HasValue) continue;
                results.Add(new SolarTermInfo(definition.Term,
                    definition.ChineseName, definition.LongitudeDegrees,
                    instantUtc.Value));
            }
            results.Sort(delegate(SolarTermInfo a, SolarTermInfo b)
            {
                return a.InstantUtc.CompareTo(b.InstantUtc);
            });
            return results.ToArray();
        }

        internal static SolarTermInfo? FindForLocalDate(DateTimeOffset localNow)
        {
            int year = localNow.Year;
            if (year < MinSupportedYear || year > MaxSupportedYear)
                return null;

            string targetDateKey = DateKey(localNow);
            SolarTermInfo[] terms = CalculateYear(year);
            foreach (SolarTermInfo term in terms)
            {
                if (String.Equals(DateKey(term.InstantUtc.ToOffset(
                    localNow.Offset)), targetDateKey,
                    StringComparison.Ordinal)) return term;
            }
            return null;
        }

        private static DateTimeOffset? FindInstantUtc(Definition definition,
            int year)
        {
            try
            {
                AstroTime start = new AstroTime(year,
                    definition.NominalMonth, definition.NominalDay,
                    0, 0, 0.0).AddDays(-3.0);
                AstroTime found = Astronomy.SearchSunLongitude(
                    (double)definition.LongitudeDegrees, start, 7.0);
                DateTime utc = found.ToUtcDateTime();
                return new DateTimeOffset(utc, TimeSpan.Zero);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string DateKey(DateTimeOffset value)
        {
            return value.ToString("yyyyMMdd",
                CultureInfo.InvariantCulture);
        }
    }
}
