using System;

namespace PennyPet
{
    // SmallTalk rhythm rules. Speaking windows and budgets are interaction
    // rules only; they must never be expressed as corpus size limits.
    internal static class PetSmallTalkPolicy
    {
        internal const int WindowMilliseconds = 60000;
        // Loopable reflections are high-frequency; meaningful lines stay rare.
        internal const int LoopableQuota = 7;
        internal const int MeaningfulBudget = 1;
        internal const int SuccessfulGapMilliseconds = 3500;
        internal const int SpeakChancePercent = 60;

        internal static bool IsWindowExpired(DateTime windowStartUtc,
            DateTime nowUtc)
        {
            if (windowStartUtc == default(DateTime)) return true;
            if (nowUtc < windowStartUtc) return true;
            return nowUtc - windowStartUtc >=
                TimeSpan.FromMilliseconds(WindowMilliseconds);
        }

        internal static bool HasSuccessfulGapElapsed(
            DateTime lastSuccessfulUtc, DateTime nowUtc)
        {
            if (lastSuccessfulUtc == default(DateTime)) return true;
            if (nowUtc < lastSuccessfulUtc) return false;
            return nowUtc - lastSuccessfulUtc >=
                TimeSpan.FromMilliseconds(SuccessfulGapMilliseconds);
        }

        internal static bool ShouldSpeak(int randomPercent)
        {
            return randomPercent >= 0 &&
                randomPercent < SpeakChancePercent;
        }
    }
}
