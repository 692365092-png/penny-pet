using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PennyPet
{
    // Owns write ordering and dirty state. Callers capture a detached snapshot
    // on the model thread; this worker never reads or repairs the live model.
    internal sealed class StickyNoteWriter
    {
        private readonly object _gate = new object();
        private readonly LinkedList<PendingWrite> _pending =
            new LinkedList<PendingWrite>();
        private readonly Func<StickyWriteRequest, PersistenceResult> _write;
        private bool _running;
        private long _requestedRevision;
        private long _savedRevision;
        private int _consecutiveFailures;
        private PersistenceResult _lastResult = PersistenceResult.Success();

        internal StickyNoteWriter(Func<StickyWriteRequest, PersistenceResult> write)
        {
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        internal event EventHandler<PersistenceFailedEventArgs> Failed;
        internal bool HasPending { get { lock (_gate) return _running; } }
        internal bool IsDirty
        {
            get { lock (_gate) return _savedRevision < _requestedRevision; }
        }
        internal Exception LastError
        {
            get { lock (_gate) return _lastResult.Error; }
        }

        internal Task<PersistenceResult> Enqueue(StickyWriteRequest request,
            bool coalesce = false)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            lock (_gate)
            {
                long revision = ++_requestedRevision;
                PendingWrite tail = _pending.Last == null ? null : _pending.Last.Value;
                // Only adjacent pending autosaves may be replaced. Explicit
                // save/import requests are barriers with their own receipt.
                if (coalesce && tail != null && tail.Coalesce)
                {
                    tail.Request = request;
                    tail.Revision = revision;
                    return tail.Completion.Task;
                }
                PendingWrite entry = new PendingWrite(request, revision,
                    coalesce);
                _pending.AddLast(entry);
                if (!_running)
                {
                    _running = true;
                    ThreadPool.QueueUserWorkItem(delegate { Drain(); });
                }
                return entry.Completion.Task;
            }
        }

        internal PersistenceResult Flush(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            Stopwatch elapsed = Stopwatch.StartNew();
            lock (_gate)
            {
                while (_running)
                {
                    TimeSpan remaining = timeout - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                        return PersistenceResult.Failure(new TimeoutException(
                            "Timed out waiting for pending sticky-note saves."));
                    Monitor.Wait(_gate, remaining);
                }
                return _lastResult;
            }
        }

        private void Drain()
        {
            while (true)
            {
                PendingWrite entry;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _running = false;
                        Monitor.PulseAll(_gate);
                        return;
                    }
                    entry = _pending.First.Value;
                    _pending.RemoveFirst();
                }

                PersistenceResult result;
                try { result = _write(entry.Request); }
                catch (Exception error) { result = PersistenceResult.Failure(error); }

                int failureCount = 0;
                lock (_gate)
                {
                    _lastResult = result;
                    if (result.Succeeded)
                    {
                        _savedRevision = entry.Revision;
                        _consecutiveFailures = 0;
                    }
                    else failureCount = ++_consecutiveFailures;
                    entry.Completion.SetResult(result);
                }
                if (failureCount > 0)
                {
                    try
                    {
                        Failed?.Invoke(this,
                            new PersistenceFailedEventArgs(result, failureCount));
                    }
                    catch (Exception error)
                    {
                        // A notification failure must not abandon queued saves.
                        Trace.TraceError("Sticky save notification failed: {0}", error);
                    }
                }
            }
        }

        private sealed class PendingWrite
        {
            internal StickyWriteRequest Request;
            internal long Revision;
            internal readonly bool Coalesce;
            internal readonly TaskCompletionSource<PersistenceResult> Completion =
                new TaskCompletionSource<PersistenceResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

            internal PendingWrite(StickyWriteRequest request, long revision,
                bool coalesce)
            {
                Request = request;
                Revision = revision;
                Coalesce = coalesce;
            }
        }
    }

    internal sealed class StickyWriteRequest
    {
        internal readonly IReadOnlyList<StickyNoteData> Snapshot;
        internal readonly string BackupPath;
        internal readonly IReadOnlyList<StickyNoteData> BackupSnapshot;

        internal StickyWriteRequest(IReadOnlyList<StickyNoteData> snapshot,
            string backupPath = null,
            IReadOnlyList<StickyNoteData> backupSnapshot = null)
        {
            Snapshot = snapshot;
            BackupPath = backupPath;
            BackupSnapshot = backupSnapshot;
        }
    }
}
