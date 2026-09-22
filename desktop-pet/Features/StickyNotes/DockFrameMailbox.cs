using System;

namespace PennyPet
{
    // One gesture/topology lifetime. Cancelled mailboxes never reopen: a queued
    // STA callback retains its old mailbox and cannot consume a newer gesture.
    // Live frames coalesce; a final frame closes live input until the owner ends
    // the gesture. A corrective final may replace it before acknowledgment.
    internal sealed class DockFrameMailbox<T> where T : class
    {
        private enum Phase { Live, Final, Cancelled }
        private readonly object _gate = new object();
        private T _current;
        private bool _applyQueued;
        private Phase _phase;

        internal bool HasPending
        {
            get { lock (_gate) return _current != null; }
        }

        internal bool QueueLive(T frame)
        {
            bool superseded;
            return QueueLive(frame, out superseded);
        }

        // True asks the caller to post one deferred apply. Replacing an already
        // queued frame needs no additional dispatcher work.
        internal bool QueueLive(T frame, out bool superseded)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            lock (_gate)
            {
                superseded = false;
                if (_phase != Phase.Live) return false;
                superseded = _current != null;
                _current = frame;
                if (_applyQueued) return false;
                _applyQueued = true;
                return true;
            }
        }

        internal T TakeLatest()
        {
            lock (_gate)
            {
                if (_phase != Phase.Live) return null;
                T frame = _current;
                _current = null;
                _applyQueued = false;
                return frame;
            }
        }

        internal bool QueueFinal(T frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            lock (_gate)
            {
                if (_phase == Phase.Cancelled) return false;
                _current = frame;
                _phase = Phase.Final;
                _applyQueued = true;
                return true;
            }
        }

        internal T TakeFinal(T expected)
        {
            lock (_gate)
                return _phase == Phase.Final && ReferenceEquals(_current, expected)
                    ? _current : null;
        }

        internal void CompleteFinal(T expected)
        {
            lock (_gate)
            {
                if (_phase != Phase.Final || !ReferenceEquals(_current, expected)) return;
                _current = null;
                _applyQueued = false;
            }
        }

        internal void Cancel()
        {
            lock (_gate)
            {
                _phase = Phase.Cancelled;
                _current = null;
                _applyQueued = false;
            }
        }
    }
}
