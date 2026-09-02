using System;

namespace PennyPet
{
    // Single owner of the daypart hour boundaries and slot masks. Content
    // selectors use this rule instead of scattering hour comparisons.
    internal static class PetDaypartRule
    {
        internal static DayPart Resolve(DateTimeOffset localTime)
        {
            return ResolveHour(localTime.Hour);
        }

        internal static DayPart Resolve(DateTime localTime)
        {
            return ResolveHour(localTime.Hour);
        }

        internal static bool SupportsLightCheckIn(DayPart dayPart)
        {
            return dayPart != DayPart.LateNight;
        }

        internal static int ConsumedMask(DayPart dayPart)
        {
            return 1 << (int)dayPart;
        }

        internal static bool IsConsumed(int mask, DayPart dayPart)
        {
            return (mask & ConsumedMask(dayPart)) != 0;
        }

        private static DayPart ResolveHour(int hour)
        {
            if (hour < 5) return DayPart.LateNight;
            if (hour < 11) return DayPart.Morning;
            if (hour < 14) return DayPart.Midday;
            if (hour < 18) return DayPart.Afternoon;
            return DayPart.Evening;
        }
    }
}
