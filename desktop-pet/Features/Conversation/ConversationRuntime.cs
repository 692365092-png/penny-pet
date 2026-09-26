using System;
using System.Threading.Tasks;

namespace PennyPet
{
    internal enum ConversationAnimation { None, Ordinary, Notification, Guitar, Hover }

    // Pet-STA owner of conversation scheduling and its daily ledger. Existing
    // content selectors keep their domain rules; only this owner commits usage.
    internal sealed class ConversationRuntime
    {
        private readonly PetSettings _settings;
        private readonly Func<DateTimeOffset> _localNow;
        private readonly Func<PetMessageKind, string, bool> _show;
        private readonly PetDailyInteractionLedger _ledger = new PetDailyInteractionLedger();
        private readonly PetDailyContentCoordinator _daily;
        private readonly PetDaypartCheckInCoordinator _daypart;
        private readonly PetSmallTalkCoordinator _smallTalk;
        private bool _active = true;

        internal ConversationRuntime(PetSettings settings,
            Func<WeatherLocation, Task<WeatherForecastWindow>> weather,
            Func<PetMessageKind, string, bool> show,
            Func<DateTimeOffset> localNow = null, Random random = null)
        {
            _settings = settings;
            _show = show;
            _localNow = localNow ?? (() => DateTimeOffset.Now);
            string today = DailyContentRules.DateKey(_localNow());
            _ledger.ResetForDate(today);
            if (String.Equals(DailyContentRules.NormalizeDateKey(settings.DailyLedgerDate),
                today, StringComparison.Ordinal))
            {
                _ledger.ConsumedDaypartsMask = Math.Max(0, settings.DailyLedgerDaypartsMask);
                foreach (string id in PetDailyInteractionLedger.DecodeUsedIds(
                    settings.DailyLedgerUsedMeaningfulIds)) _ledger.TryUseMeaningful(id);
            }
            CurrentLedger();
            _daily = new PetDailyContentCoordinator(CapturePreferences, weather,
                ShowOpening, RecordOpening);
            _daypart = new PetDaypartCheckInCoordinator(CurrentLedger,
                () => _settings.SilentMode, text => _show(PetMessageKind.DailyGreeting, text));
            _smallTalk = new PetSmallTalkCoordinator(() => _settings.SilentMode,
                text => _show(PetMessageKind.SmallTalk, text), CurrentLedger, random);
        }

        internal bool IsOpeningEligible(DateTimeOffset now)
        {
            return _active && _daily.IsOpeningEligible(now);
        }

        internal async Task<ConversationAnimation> HandlePetPokedAsync(DateTimeOffset now)
        {
            if (!_active) return ConversationAnimation.None;
            CurrentLedger();
            if (_daily.IsOpeningEligible(now))
            {
                if (await _daily.HandlePetPokedAsync(now)) return ConversationAnimation.None;
                if (!_active || !SameDaypart(now, _localNow())) return ConversationAnimation.None;
            }
            if (_daypart.HandlePetPoked(now))
            {
                PersistLedger();
                return ConversationAnimation.Notification;
            }
            if (!_smallTalk.HandlePetPoked(now.UtcDateTime)) return ConversationAnimation.Ordinary;
            PersistLedger();
            if (_smallTalk.LastSpokenAnimationKind == PetPersonaAnimationKind.Guitar)
                return ConversationAnimation.Guitar;
            if (_smallTalk.LastSpokenAnimationKind == PetPersonaAnimationKind.Hover)
                return ConversationAnimation.Hover;
            return _smallTalk.LastSpokenRepeatClass == PetPersonaRepeatClass.Meaningful
                ? ConversationAnimation.Notification : ConversationAnimation.Ordinary;
        }

        internal void InvalidatePending()
        {
            _daily.Invalidate();
        }

        internal void Stop()
        {
            _active = false;
            InvalidatePending();
        }

        private DailyContentPreferencesSnapshot CapturePreferences()
        {
            WeatherLocation location;
            WeatherLocation.TryCreate(_settings.WeatherLocationName,
                _settings.WeatherLocationAdmin1, _settings.WeatherLocationCountry,
                _settings.WeatherLatitude, _settings.WeatherLongitude,
                _settings.WeatherTimezone, out location);
            return new DailyContentPreferencesSnapshot(_settings.SilentMode,
                _settings.DailyContentEnabled, _settings.SolarTermEnabled,
                _settings.AlmanacEnabled, _settings.WeatherEnabled, location,
                _settings.ZodiacSign, _settings.UserBirthdayMonth,
                _settings.UserBirthdayDay, _settings.LastDailyBriefingDate);
        }

        private bool ShowOpening(DailyContentPreferencesSnapshot preferences,
            DateTimeOffset requestedAt, string text)
        {
            return _active && SameDaypart(requestedAt, _localNow()) &&
                preferences.HasSamePreferences(CapturePreferences()) &&
                _show(PetMessageKind.DailyGreeting, text);
        }

        private static bool SameDaypart(DateTimeOffset left, DateTimeOffset right)
        {
            return left.Date == right.Date &&
                PetDaypartRule.Resolve(left) == PetDaypartRule.Resolve(right);
        }

        private void RecordOpening(DateTimeOffset requestedAt)
        {
            string date = DailyContentRules.DateKey(requestedAt);
            _settings.LastDailyBriefingDate = date;
            _ledger.EnsureDate(date);
            _ledger.DailyOpeningConsumed = true;
            _ledger.TryConsumeDaypart(PetDaypartRule.Resolve(requestedAt));
            PersistLedger();
        }

        private PetDailyInteractionLedger CurrentLedger()
        {
            _ledger.EnsureDate(DailyContentRules.DateKey(_localNow()));
            _ledger.DailyOpeningConsumed = String.Equals(
                DailyContentRules.NormalizeDateKey(_settings.LastDailyBriefingDate),
                _ledger.LocalDateKey, StringComparison.Ordinal);
            return _ledger;
        }

        private void PersistLedger()
        {
            _settings.DailyLedgerDate = _ledger.LocalDateKey;
            _settings.DailyLedgerDaypartsMask = _ledger.ConsumedDaypartsMask;
            _settings.DailyLedgerUsedMeaningfulIds = PetDailyInteractionLedger.EncodeUsedIds(
                _ledger.UsedMeaningfulIds());
            _settings.SaveAsync();
        }
    }
}
