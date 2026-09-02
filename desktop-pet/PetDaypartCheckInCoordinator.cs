using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Light per-daypart check-in. It never duplicates the full Daily Opening
    // and never fetches weather; it only composes a greeting plus at most one
    // unused Meaningful persona line.
    internal sealed class PetDaypartCheckInCoordinator
    {
        private readonly Func<PetDailyInteractionLedger> _ledger;
        private readonly Func<bool> _silentMode;
        private readonly Func<string, bool> _show;

        internal PetDaypartCheckInCoordinator(
            Func<PetDailyInteractionLedger> ledger,
            Func<bool> silentMode, Func<string, bool> show)
        {
            _ledger = ledger ??
                throw new ArgumentNullException(nameof(ledger));
            _silentMode = silentMode ??
                throw new ArgumentNullException(nameof(silentMode));
            _show = show ?? throw new ArgumentNullException(nameof(show));
        }

        internal bool HandlePetPoked(DateTimeOffset localNow)
        {
            if (PetMessagePolicy.ShouldSuppress(
                PetMessageKind.DailyGreeting, _silentMode())) return false;
            PetDailyInteractionLedger ledger = _ledger();
            if (ledger == null) return false;
            ledger.EnsureDate(DailyContentRules.DateKey(localNow));
            if (!ledger.DailyOpeningConsumed) return false;

            DayPart dayPart = PetDaypartRule.Resolve(localNow);
            if (!PetDaypartRule.SupportsLightCheckIn(dayPart) ||
                ledger.HasConsumedDaypart(dayPart)) return false;

            List<DailyBriefingSentence> sentences =
                new List<DailyBriefingSentence>(2);
            sentences.Add(new DailyBriefingSentence(
                DailyContentRules.GreetingBodyFor(dayPart),
                PetSentenceContentKind.Greeting,
                DailyContentRules.GreetingIntentFor(dayPart),
                "GREETING-" + dayPart));
            PetPersonaEntry extra =
                PetPersonaTempCatalog.SelectUnusedMeaningful(
                    ContextFor(dayPart),
                    delegate(string id) { return ledger.WasMeaningfulUsed(id); });
            if (extra != null)
                sentences.Add(new DailyBriefingSentence(extra.CanonicalBody,
                    PetSentenceContentKind.SmallTalk, extra.Intent,
                    extra.StableContentId));

            string text = DailyBriefingComposer.ComposeSentences(
                localNow.Date, sentences.ToArray());
            if (!_show(text)) return false;
            ledger.TryConsumeDaypart(dayPart);
            if (extra != null) ledger.TryUseMeaningful(extra.StableContentId);
            return true;
        }

        private static PetPersonaContext ContextFor(DayPart dayPart)
        {
            switch (dayPart)
            {
                case DayPart.Morning: return PetPersonaContext.Morning;
                case DayPart.Midday: return PetPersonaContext.Noon;
                case DayPart.Afternoon: return PetPersonaContext.Afternoon;
                case DayPart.Evening: return PetPersonaContext.Evening;
                default: return PetPersonaContext.LateNight;
            }
        }
    }
}
