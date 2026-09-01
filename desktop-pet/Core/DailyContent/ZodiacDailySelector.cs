using System;

namespace PennyPet
{
    internal static class ZodiacDailySelector
    {
        internal static string Select(ZodiacSign sign,
            DateTimeOffset localNow)
        {
            ZodiacSign normalized = PetSettingRules.NormalizeZodiacSign(sign);
            if (normalized == ZodiacSign.None) return null;
            string[] lines = ZodiacDailyCatalog.GetLines(normalized);
            if (lines.Length == 0) return null;
            int dateNumber = localNow.Year * 372 + localNow.Month * 31 +
                localNow.Day;
            int seed = dateNumber * 17 + (int)normalized * 13;
            int index = seed % lines.Length;
            if (index < 0) index += lines.Length;
            return lines[index];
        }
    }
}
