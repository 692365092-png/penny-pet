using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal sealed class ArtPreloadReservations
    {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private readonly HashSet<int> _active = new HashSet<int>();
        private readonly Dictionary<int, DateTime> _retryAfterUtc =
            new Dictionary<int, DateTime>();

        internal bool TryReserve(int row, bool alreadyLoaded, DateTime nowUtc)
        {
            lock (_active)
            {
                if (alreadyLoaded)
                {
                    _active.Remove(row);
                    _retryAfterUtc.Remove(row);
                    return false;
                }
                DateTime retryAfter;
                if (_active.Contains(row) ||
                    (_retryAfterUtc.TryGetValue(row, out retryAfter) &&
                    nowUtc < retryAfter)) return false;
                _active.Add(row);
                return true;
            }
        }

        internal void Complete(int row, bool loaded, DateTime nowUtc)
        {
            lock (_active)
            {
                _active.Remove(row);
                if (loaded) _retryAfterUtc.Remove(row);
                else _retryAfterUtc[row] = nowUtc.Add(RetryDelay);
            }
        }
    }
}

