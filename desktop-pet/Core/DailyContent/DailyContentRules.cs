using System;
using System.Globalization;

namespace PennyPet
{
    internal static class DailyContentRules
    {
        internal static bool ShouldShow(string lastBriefingDate,
            DateTime localNow)
        {
            return !String.Equals(NormalizeDateKey(lastBriefingDate),
                DateKey(localNow), StringComparison.Ordinal);
        }

        internal static string DateKey(DateTime localDate)
        {
            return localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        internal static bool ShouldShow(string lastBriefingDate,
            DateTimeOffset localNow)
        {
            return !String.Equals(NormalizeDateKey(lastBriefingDate),
                DateKey(localNow), StringComparison.Ordinal);
        }

        internal static string DateKey(DateTimeOffset localDate)
        {
            return localDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        internal static string NormalizeDateKey(string value)
        {
            DateTime parsed;
            return value != null && DateTime.TryParseExact(value,
                "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed) ? value : String.Empty;
        }

        internal static DayPart ResolveDayPart(DateTime localTime)
        {
            int hour = localTime.Hour;
            if (hour < 5) return DayPart.LateNight;
            if (hour < 11) return DayPart.Morning;
            if (hour < 14) return DayPart.Midday;
            if (hour < 18) return DayPart.Afternoon;
            return DayPart.Evening;
        }

        internal static DayPart ResolveDayPart(DateTimeOffset localTime)
        {
            int hour = localTime.Hour;
            if (hour < 5) return DayPart.LateNight;
            if (hour < 11) return DayPart.Morning;
            if (hour < 14) return DayPart.Midday;
            if (hour < 18) return DayPart.Afternoon;
            return DayPart.Evening;
        }

        internal static string GreetingFor(DayPart dayPart)
        {
            switch (dayPart)
            {
                case DayPart.Morning:
                    return "早上好～今天也要加油呀。";
                case DayPart.Midday:
                    return "中午好～休息一下再继续吧。";
                case DayPart.Afternoon:
                    return "下午好～今天过得怎么样？";
                case DayPart.Evening:
                    return "晚上好～今天辛苦啦。";
                default:
                    return "这么晚还没睡呀～";
            }
        }
    }
}
