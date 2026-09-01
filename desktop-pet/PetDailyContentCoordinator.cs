using System;

namespace PennyPet
{
    // Hosts the poke-triggered daily interaction without owning settings or UI.
    internal sealed class PetDailyContentCoordinator
    {
        private readonly Func<string> _lastBriefingDate;
        private readonly Func<bool> _silentMode;
        private readonly Func<bool> _dailyContentEnabled;
        private readonly Func<bool> _solarTermEnabled;
        private readonly Func<ZodiacSign> _zodiacSign;
        private readonly Func<string, bool> _showDailyGreeting;
        private readonly Action<string> _recordBriefingDate;

        internal PetDailyContentCoordinator(Func<string> lastBriefingDate,
            Func<bool> silentMode, Func<bool> dailyContentEnabled,
            Func<bool> solarTermEnabled, Func<ZodiacSign> zodiacSign,
            Func<string, bool> showDailyGreeting,
            Action<string> recordBriefingDate)
        {
            _lastBriefingDate = lastBriefingDate ??
                throw new ArgumentNullException("lastBriefingDate");
            _silentMode = silentMode ??
                throw new ArgumentNullException("silentMode");
            _dailyContentEnabled = dailyContentEnabled ??
                throw new ArgumentNullException("dailyContentEnabled");
            _solarTermEnabled = solarTermEnabled ??
                throw new ArgumentNullException("solarTermEnabled");
            _zodiacSign = zodiacSign ??
                throw new ArgumentNullException("zodiacSign");
            _showDailyGreeting = showDailyGreeting ??
                throw new ArgumentNullException("showDailyGreeting");
            _recordBriefingDate = recordBriefingDate ??
                throw new ArgumentNullException("recordBriefingDate");
        }

        internal bool HandlePetPoked(DateTimeOffset localNow)
        {
            if (!_dailyContentEnabled() ||
                PetMessagePolicy.ShouldSuppress(PetMessageKind.DailyGreeting,
                _silentMode()) || !DailyContentRules.ShouldShow(
                    _lastBriefingDate(), localNow)) return false;
            DayPart dayPart = DailyContentRules.ResolveDayPart(localNow);
            SolarTermInfo? solarTerm = _solarTermEnabled()
                ? SolarTermCalculator.FindForLocalDate(localNow)
                : (SolarTermInfo?)null;
            AlmanacDayInfo almanacDay = AlmanacCalculator.Calculate(localNow);
            AlmanacDailySelection almanac = almanacDay == null ? null :
                AlmanacDailySelector.Select(almanacDay, localNow);
            DailyLineEntry curatedLine = CuratedDailyLineSelector.Select(
                localNow);
            DailyLineEntry zodiacLine = ZodiacDailySelector.Select(
                _zodiacSign(), localNow);
            DailyBriefingContent content = new DailyBriefingContent(solarTerm,
                almanac == null ? null : almanac.Text, curatedLine,
                zodiacLine);
            string text = DailyBriefingComposer.Compose(dayPart, content);
            if (!_showDailyGreeting(text)) return false;
            _recordBriefingDate(DailyContentRules.DateKey(localNow));
            return true;
        }
    }
}
