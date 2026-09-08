using System;

namespace PennyPet
{
    internal static class ZodiacDailySelector
    {
        internal const int EligibilityPercent = 15;

        internal static DailyLineEntry Select(ZodiacSign sign,
            DateTimeOffset localNow)
        {
            ZodiacSign normalized = PetSettingRules.NormalizeZodiacSign(sign);
            if (normalized == ZodiacSign.None) return null;
            int dateNumber = localNow.Year * 372 + localNow.Month * 31 +
                localNow.Day;
            int eligibilitySeed = dateNumber * 31 + (int)normalized * 17;
            if (PositiveModulo(eligibilitySeed, 100) >= EligibilityPercent)
                return null;
            DailyLineEntry[] entries = ZodiacDailyCatalog.GetEntries(
                normalized);
            if (entries.Length == 0) return null;
            int seed = dateNumber * 17 + (int)normalized * 13;
            return entries[PositiveModulo(seed, entries.Length)];
        }

        private static int PositiveModulo(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }
    }
}
