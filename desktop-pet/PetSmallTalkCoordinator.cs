using System;

namespace PennyPet
{
    // Owns the live SmallTalk rhythm: a 60s interaction window with a small
    // speaking quota and an even smaller meaningful budget. Loopable lines may
    // repeat; meaningful lines are recorded in the daily ledger once spoken.
    internal sealed class PetSmallTalkCoordinator
    {
        private readonly Func<bool> _silentMode;
        private readonly Func<string, bool> _show;
        private readonly Func<PetDailyInteractionLedger> _ledger;
        private readonly Random _random;
        private DateTime _windowStartUtc;
        private int _loopableQuotaRemaining;
        private int _meaningfulRemaining;
        private DateTime _lastSuccessfulUtc;

        internal PetPersonaRepeatClass? LastSpokenRepeatClass
        {
            get;
            private set;
        }

        internal PetSmallTalkCoordinator(Func<bool> silentMode,
            Func<string, bool> show,
            Func<PetDailyInteractionLedger> ledger = null,
            Random random = null)
        {
            _silentMode = silentMode ??
                throw new ArgumentNullException("silentMode");
            _show = show ?? throw new ArgumentNullException("show");
            _ledger = ledger;
            _random = random ?? new Random();
        }

        internal bool HandlePetPoked(DateTime nowUtc)
        {
            LastSpokenRepeatClass = null;
            if (PetMessagePolicy.ShouldSuppress(PetMessageKind.SmallTalk,
                _silentMode())) return false;
            if (PetSmallTalkPolicy.IsWindowExpired(_windowStartUtc, nowUtc))
            {
                _windowStartUtc = nowUtc;
                _loopableQuotaRemaining = PetSmallTalkPolicy.LoopableQuota;
                _meaningfulRemaining = PetSmallTalkPolicy.MeaningfulBudget;
            }
            if (_loopableQuotaRemaining <= 0 &&
                _meaningfulRemaining <= 0) return false;
            if (!PetSmallTalkPolicy.HasSuccessfulGapElapsed(
                _lastSuccessfulUtc, nowUtc)) return false;
            // A light random gate keeps animation-only pokes possible even
            // while quota remains; do not reintroduce a long cooldown.
            if (!PetSmallTalkPolicy.ShouldSpeak(_random.Next(100)))
                return false;

            PetPersonaEntry entry = SelectEntry();
            if (entry == null) return false;
            string text = FinalizeText(entry, nowUtc);
            if (!_show(text)) return false;

            _lastSuccessfulUtc = nowUtc;
            LastSpokenRepeatClass = entry.RepeatClass;
            if (entry.RepeatClass == PetPersonaRepeatClass.Meaningful)
            {
                _meaningfulRemaining--;
                PetDailyInteractionLedger ledger = _ledger != null
                    ? _ledger() : null;
                if (ledger != null)
                    ledger.TryUseMeaningful(entry.StableContentId);
            }
            else
            {
                _loopableQuotaRemaining--;
            }
            return true;
        }

        private PetPersonaEntry SelectEntry()
        {
            PetDailyInteractionLedger ledger = _ledger != null
                ? _ledger() : null;
            if (_meaningfulRemaining > 0 && ledger != null)
            {
                PetPersonaEntry meaningful =
                    PetPersonaRuntimeCatalog.SelectUnusedMeaningful(
                        PetPersonaContext.SmallTalk,
                        delegate(string id)
                        {
                            return ledger.WasMeaningfulUsed(id);
                        });
                if (meaningful != null) return meaningful;
            }
            if (_loopableQuotaRemaining > 0)
                return PetPersonaRuntimeCatalog.SelectLoopable(_random);
            return null;
        }

        private static string FinalizeText(PetPersonaEntry entry,
            DateTime nowUtc)
        {
            if (entry == null) return String.Empty;
            if (entry.PreserveEnding) return entry.CanonicalBody;
            return PetSentenceEndingPolicy.Apply(entry.CanonicalBody,
                new PetSentenceEndingContext(PetSentenceRole.Single,
                    entry.Intent, PetSentenceContentKind.SmallTalk,
                    entry.StableContentId, nowUtc.ToLocalTime().Date));
        }
    }
}
