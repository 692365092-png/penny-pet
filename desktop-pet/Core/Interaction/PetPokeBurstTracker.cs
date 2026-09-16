using System;

namespace PennyPet
{
    // Process-local burst state. A pause starts a fresh sequence; once the
    // target fires, continued pokes cannot retrigger it until that pause.
    internal sealed class PetPokeBurstTracker
    {
        internal const int TargetCount = 50;
        internal const int MaxGapMilliseconds = 400;

        private int _count;
        private DateTime _lastPokeUtc;
        private bool _triggered;

        internal bool RegisterPoke(DateTime nowUtc)
        {
            bool startsNewBurst = _count == 0 || nowUtc < _lastPokeUtc ||
                nowUtc - _lastPokeUtc >
                    TimeSpan.FromMilliseconds(MaxGapMilliseconds);
            if (startsNewBurst)
            {
                _count = 1;
                _triggered = false;
            }
            else if (!_triggered)
            {
                _count++;
            }
            _lastPokeUtc = nowUtc;
            if (_triggered || _count != TargetCount) return false;
            _triggered = true;
            return true;
        }
    }
}
