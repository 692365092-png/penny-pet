using System;
using System.Diagnostics;
using System.Threading;

namespace PennyPet
{
    // One inspection at a time, one replaceable pending key, one UI delivery slot.
    // A provider may hang forever: callers never wait and no replacement worker is spawned.
    internal sealed class KeyboardPrivacyWorker : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<KeyboardFocusSnapshot, bool> _inspectSafe;
        private readonly Func<long> _now;
        private readonly long _maxAge;
        private Func<KeyboardFocusSnapshot, bool> _stillCurrent;
        private Action<Action> _post;
        private Action<KeyboardInputEventArgs> _publish;
        private Thread _thread;
        private KeyboardInputEventArgs _pending, _ready;
        private long _generation, _readyGeneration;
        private bool _enabled, _disposed, _deliveryQueued;

        internal KeyboardPrivacyWorker(Func<KeyboardFocusSnapshot, bool> inspectSafe,
            Func<KeyboardFocusSnapshot, bool> stillCurrent, Action<Action> post,
            Action<KeyboardInputEventArgs> publish, Func<long> now = null, long maxAge = 0)
        {
            _inspectSafe = inspectSafe;
            _stillCurrent = stillCurrent;
            _post = post;
            _publish = publish;
            _now = now ?? Stopwatch.GetTimestamp;
            _maxAge = maxAge > 0 ? maxAge : Stopwatch.Frequency * 3 / 4;
        }
        internal void SetEnabled(bool enabled)
        {
            lock (_gate)
            {
                _enabled = enabled && !_disposed;
                InvalidateLocked();
                if (_enabled && _thread == null)
                {
                    _thread = new Thread(Run) { IsBackground = true, Name = "PennyPet keyboard privacy" };
                    _thread.SetApartmentState(ApartmentState.MTA);
                    _thread.Start();
                }
            }
        }
        internal void Invalidate()
        { lock (_gate) InvalidateLocked(); }
        private void InvalidateLocked()
        {
            _generation++;
            _pending = _ready = null;
        }
        internal void Offer(KeyboardInputEventArgs input)
        {
            lock (_gate)
            {
                InvalidateLocked();
                if (!_enabled || _disposed || input == null || input.FocusSnapshot == null ||
                    !input.FocusSnapshot.HasNativeInputIdentity || String.IsNullOrEmpty(input.DisplayText) ||
                    !Fresh(input)) return;
                _pending = input;
                Monitor.Pulse(_gate);
            }
        }
        private bool Fresh(KeyboardInputEventArgs input)
        {
            long age = _now() - input.FocusSnapshot.CapturedAt;
            return age >= 0 && age <= _maxAge;
        }
        private void Run()
        {
            while (true)
            {
                KeyboardInputEventArgs input;
                long generation;
                lock (_gate)
                {
                    while (!_disposed && _pending == null) Monitor.Wait(_gate);
                    if (_disposed) return;
                    input = _pending;
                    generation = _generation;
                    _pending = null;
                }
                bool safe = false;
                try { if (Fresh(input)) safe = _inspectSafe(input.FocusSnapshot); }
                catch { /* Unknown provider result must never permit display. */ }
                Action<Action> post = null;
                lock (_gate)
                {
                    if (_disposed) return;
                    if (safe && _enabled && generation == _generation && Fresh(input))
                    {
                        _ready = input;
                        _readyGeneration = generation;
                        if (!_deliveryQueued)
                        {
                            _deliveryQueued = true;
                            post = _post;
                        }
                    }
                }
                if (post != null)
                {
                    try { post(Deliver); }
                    catch
                    {
                        lock (_gate) { _deliveryQueued = false; _ready = null; }
                    }
                }
            }
        }
        private void Deliver()
        {
            lock (_gate)
            {
                _deliveryQueued = false;
                var input = _ready;
                _ready = null;
                if (_disposed || !_enabled || input == null || _readyGeneration != _generation || !Fresh(input)) return;
                // Cheap native metadata only. UIA never runs on either UI STA.
                if (!_stillCurrent(input.FocusSnapshot)) return;
                _publish(input);
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _enabled = false;
                InvalidateLocked();
                // A hung provider must not keep PetForm reachable through delivery delegates.
                _post = null;
                _publish = null;
                _stillCurrent = null;
                Monitor.Pulse(_gate);
            }
        }
    }
}
