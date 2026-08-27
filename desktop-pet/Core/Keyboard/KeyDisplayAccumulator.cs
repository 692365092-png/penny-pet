using System;

namespace PennyPet
{
    // Pure state machine for presenting repeated key events. It has no hook,
    // focus, clock, drawing or window dependency; callers supply UTC time.
    internal sealed class KeyDisplayAccumulator
    {
        private string _lastKey = String.Empty;
        private int _count;
        private DateTime _lastUtc = DateTime.MinValue;

        public string Register(string key, DateTime utcNow)
        {
            return Register(key, utcNow, 1);
        }

        public string Register(string key, DateTime utcNow, int occurrences)
        {
            string value = key ?? String.Empty;
            int increment = Math.Max(1, occurrences);
            if (String.Equals(value, _lastKey, StringComparison.Ordinal) &&
                utcNow - _lastUtc <= TimeSpan.FromMilliseconds(700))
                _count += increment;
            else
            {
                _lastKey = value;
                _count = increment;
            }
            _lastUtc = utcNow;
            return _count <= 1 ? value : value + "*" + _count;
        }

        public string RegisterAbsolute(string key, DateTime utcNow,
            int repeatCount)
        {
            string value = key ?? String.Empty;
            int absolute = Math.Max(1, repeatCount);
            if (String.Equals(value, _lastKey, StringComparison.Ordinal) &&
                utcNow - _lastUtc <= TimeSpan.FromMilliseconds(1400))
                _count = Math.Max(_count, absolute);
            else
            {
                _lastKey = value;
                _count = absolute;
            }
            _lastUtc = utcNow;
            return _count <= 1 ? value : value + "*" + _count;
        }

        public void Reset()
        {
            _lastKey = String.Empty;
            _count = 0;
            _lastUtc = DateTime.MinValue;
        }
    }
}
