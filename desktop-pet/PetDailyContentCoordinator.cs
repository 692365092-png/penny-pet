using System;

namespace PennyPet
{
    // Hosts the poke-triggered daily interaction without owning settings or UI.
    internal sealed class PetDailyContentCoordinator
    {
        private readonly Func<string> _lastBriefingDate;
        private readonly Func<bool> _silentMode;
        private readonly Func<string, bool> _showDailyGreeting;
        private readonly Action<string> _recordBriefingDate;

        internal PetDailyContentCoordinator(Func<string> lastBriefingDate,
            Func<bool> silentMode, Func<string, bool> showDailyGreeting,
            Action<string> recordBriefingDate)
        {
            _lastBriefingDate = lastBriefingDate ??
                throw new ArgumentNullException("lastBriefingDate");
            _silentMode = silentMode ??
                throw new ArgumentNullException("silentMode");
            _showDailyGreeting = showDailyGreeting ??
                throw new ArgumentNullException("showDailyGreeting");
            _recordBriefingDate = recordBriefingDate ??
                throw new ArgumentNullException("recordBriefingDate");
        }

        internal bool HandlePetPoked(DateTime localNow)
        {
            if (PetMessagePolicy.ShouldSuppress(PetMessageKind.DailyGreeting,
                _silentMode()) || !DailyContentRules.ShouldShow(
                    _lastBriefingDate(), localNow)) return false;
            string text = DailyContentRules.GreetingFor(
                DailyContentRules.ResolveDayPart(localNow));
            if (!_showDailyGreeting(text)) return false;
            _recordBriefingDate(DailyContentRules.DateKey(localNow));
            return true;
        }
    }
}
