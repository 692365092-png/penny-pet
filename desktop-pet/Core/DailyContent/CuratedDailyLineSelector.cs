using System;

namespace PennyPet
{
    internal static class CuratedDailyLineSelector
    {
        internal static DailyLineEntry Select(DateTimeOffset localNow)
        {
            DailyLineEntry[] entries = CuratedDailyLineCatalog.GetEntries();
            int dateNumber = localNow.Year * 372 + localNow.Month * 31 +
                localNow.Day;
            int index = dateNumber % entries.Length;
            if (index < 0) index += entries.Length;
            return entries[index];
        }
    }
}
