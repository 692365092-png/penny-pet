using System;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private readonly PetDailyInteractionLedger _dailyLedger =
            new PetDailyInteractionLedger();

        private void InitializeDailyLedger()
        {
            string today = DailyContentRules.DateKey(DateTimeOffset.Now);
            string stored = DailyContentRules.NormalizeDateKey(
                _settings.DailyLedgerDate);
            if (String.Equals(stored, today, StringComparison.Ordinal))
            {
                _dailyLedger.LocalDateKey = today;
                _dailyLedger.ConsumedDaypartsMask = Math.Max(0,
                    _settings.DailyLedgerDaypartsMask);
                foreach (string id in PetDailyInteractionLedger.DecodeUsedIds(
                    _settings.DailyLedgerUsedMeaningfulIds))
                    _dailyLedger.TryUseMeaningful(id);
            }
            else
            {
                _dailyLedger.ResetForDate(today);
            }
            RefreshLedgerOpeningMarker();
        }

        private PetDailyInteractionLedger LedgerSnapshot()
        {
            RefreshLedgerOpeningMarker();
            return _dailyLedger;
        }

        private void RefreshLedgerOpeningMarker()
        {
            _dailyLedger.DailyOpeningConsumed = String.Equals(
                DailyContentRules.NormalizeDateKey(
                    _settings.LastDailyBriefingDate),
                _dailyLedger.LocalDateKey, StringComparison.Ordinal);
        }

        private void PersistDailyLedger()
        {
            _settings.DailyLedgerDate = _dailyLedger.LocalDateKey;
            _settings.DailyLedgerDaypartsMask =
                _dailyLedger.ConsumedDaypartsMask;
            _settings.DailyLedgerUsedMeaningfulIds =
                PetDailyInteractionLedger.EncodeUsedIds(
                    _dailyLedger.UsedMeaningfulIds());
            _settings.Save();
        }
    }
}
