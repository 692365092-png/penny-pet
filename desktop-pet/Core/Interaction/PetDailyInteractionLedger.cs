using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Current-local-day interaction ledger. It never keeps history; a date
    // change resets every slot and every used Meaningful content id.
    internal sealed class PetDailyInteractionLedger
    {
        private readonly HashSet<string> _usedMeaningfulIds =
            new HashSet<string>(StringComparer.Ordinal);

        internal string LocalDateKey;
        internal bool DailyOpeningConsumed;
        internal int ConsumedDaypartsMask;

        internal PetDailyInteractionLedger()
        {
        }

        internal PetDailyInteractionLedger(string localDateKey,
            bool dailyOpeningConsumed, int consumedDaypartsMask,
            IEnumerable<string> usedMeaningfulIds)
        {
            LocalDateKey = localDateKey ?? String.Empty;
            DailyOpeningConsumed = dailyOpeningConsumed;
            ConsumedDaypartsMask = Math.Max(0, consumedDaypartsMask);
            if (usedMeaningfulIds != null)
                foreach (string id in usedMeaningfulIds)
                    if (!String.IsNullOrWhiteSpace(id))
                        _usedMeaningfulIds.Add(id.Trim());
        }

        internal bool IsCurrentDate(string localDateKey)
        {
            return String.Equals(LocalDateKey, localDateKey,
                StringComparison.Ordinal);
        }

        internal void ResetForDate(string localDateKey)
        {
            LocalDateKey = localDateKey ?? String.Empty;
            DailyOpeningConsumed = false;
            ConsumedDaypartsMask = 0;
            _usedMeaningfulIds.Clear();
        }

        internal void EnsureDate(string localDateKey)
        {
            if (!IsCurrentDate(localDateKey)) ResetForDate(localDateKey);
        }

        internal bool HasConsumedDaypart(DayPart dayPart)
        {
            return PetDaypartRule.IsConsumed(ConsumedDaypartsMask, dayPart);
        }

        internal bool TryConsumeDaypart(DayPart dayPart)
        {
            if (HasConsumedDaypart(dayPart)) return false;
            ConsumedDaypartsMask |= PetDaypartRule.ConsumedMask(dayPart);
            return true;
        }

        internal bool WasMeaningfulUsed(string stableContentId)
        {
            return !String.IsNullOrWhiteSpace(stableContentId) &&
                _usedMeaningfulIds.Contains(stableContentId.Trim());
        }

        internal bool TryUseMeaningful(string stableContentId)
        {
            if (String.IsNullOrWhiteSpace(stableContentId)) return false;
            return _usedMeaningfulIds.Add(stableContentId.Trim());
        }

        internal string[] UsedMeaningfulIds()
        {
            List<string> ids = new List<string>(_usedMeaningfulIds);
            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        internal static string EncodeUsedIds(IEnumerable<string> ids)
        {
            if (ids == null) return String.Empty;
            HashSet<string> seen = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (string id in ids)
                if (!String.IsNullOrWhiteSpace(id)) seen.Add(id.Trim());
            List<string> values = new List<string>(seen);
            values.Sort(StringComparer.Ordinal);
            return String.Join("|", values.ToArray());
        }

        internal static string[] DecodeUsedIds(string encoded)
        {
            if (String.IsNullOrWhiteSpace(encoded)) return new string[0];
            List<string> values = new List<string>();
            foreach (string id in encoded.Split('|'))
                if (!String.IsNullOrWhiteSpace(id)) values.Add(id.Trim());
            return values.ToArray();
        }
    }
}
