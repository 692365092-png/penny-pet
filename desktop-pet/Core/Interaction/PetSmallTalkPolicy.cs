using System;

namespace PennyPet
{
    internal static class PetSmallTalkPolicy
    {
        internal const int CooldownMilliseconds = 30000;
        internal const int ChancePercent = 25;

        internal static bool ShouldAttempt(DateTime lastShownUtc,
            DateTime nowUtc, int randomPercent)
        {
            if (nowUtc < lastShownUtc) return false;
            if (nowUtc - lastShownUtc <
                TimeSpan.FromMilliseconds(CooldownMilliseconds)) return false;
            return randomPercent >= 0 && randomPercent < ChancePercent;
        }

        internal static int NextPhraseIndex(int previousIndex,
            int randomPick, int phraseCount)
        {
            if (phraseCount <= 0) return 0;
            int next = randomPick % phraseCount;
            if (next < 0) next += phraseCount;
            if (phraseCount > 1 && next == previousIndex)
                next = (next + 1) % phraseCount;
            return next;
        }
    }
}
