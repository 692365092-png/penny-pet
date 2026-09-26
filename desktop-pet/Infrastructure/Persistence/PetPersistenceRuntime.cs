using System;
using System.Diagnostics;
using System.Threading;

namespace PennyPet
{
    // Narrow contract used by the application-level retry owner. The concrete
    // stores still own serialization, queue ordering and detached snapshots.
    internal interface IPersistenceRetryTarget
    {
        event EventHandler<PersistenceFailedEventArgs> SaveFailed;
        bool HasUnsavedChanges { get; }
        bool HasPendingSaves { get; }
        void RequestAutosave();
    }

    internal enum PersistenceNoticeKind
    {
        Warning,
        Recovered
    }

    internal sealed class PersistenceNoticeEventArgs : EventArgs
    {
        internal PersistenceNoticeEventArgs(PersistenceNoticeKind kind,
            string dataName, string errorMessage)
        {
            Kind = kind;
            DataName = dataName ?? String.Empty;
            ErrorMessage = errorMessage ?? String.Empty;
        }

        internal PersistenceNoticeKind Kind { get; private set; }
        internal string DataName { get; private set; }
        internal string ErrorMessage { get; private set; }
    }

    internal interface IPersistenceRetryScheduler : IDisposable
    {
        void Schedule(TimeSpan delay, Action due);
        void Cancel();
    }

    internal sealed class ThreadingPersistenceRetryScheduler :
        IPersistenceRetryScheduler
    {
        private readonly object _gate = new object();
        private readonly Timer _timer;
        private Action _due;
        private bool _disposed;

        internal ThreadingPersistenceRetryScheduler()
        {
            _timer = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Schedule(TimeSpan delay, Action due)
        {
            if (due == null) throw new ArgumentNullException(nameof(due));
            int milliseconds = (int)Math.Min(Int32.MaxValue,
                Math.Max(1.0, delay.TotalMilliseconds));
            lock (_gate)
            {
                if (_disposed) return;
                _due = due;
                _timer.Change(milliseconds, Timeout.Infinite);
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _due = null;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void Fire(object state)
        {
            Action due;
            lock (_gate)
            {
                if (_disposed) return;
                due = _due;
                _due = null;
            }
            if (due != null) due();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _due = null;
                _timer.Dispose();
            }
        }
    }

    // Owns retry cadence and unresolved-save presentation state for the whole
    // application. It never serializes a model: retries call the target on the
    // owner context so each target captures the newest detached snapshot.
    internal sealed class PetPersistenceRuntime : IDisposable
    {
        private static readonly TimeSpan DefaultRetryDelay =
            TimeSpan.FromSeconds(5);

        private readonly IPersistenceRetryTarget _notes;
        private readonly IPersistenceRetryTarget _settings;
        private readonly SynchronizationContext _ownerContext;
        private readonly IPersistenceRetryScheduler _scheduler;
        private readonly TimeSpan _retryDelay;
        private bool _retryArmed;
        private bool _failureEpisode;
        private bool _warningShown;
        private int _disposed;

        internal PetPersistenceRuntime(IPersistenceRetryTarget notes,
            IPersistenceRetryTarget settings, SynchronizationContext ownerContext)
            : this(notes, settings, ownerContext,
                new ThreadingPersistenceRetryScheduler(), DefaultRetryDelay)
        {
        }

        internal PetPersistenceRuntime(IPersistenceRetryTarget notes,
            IPersistenceRetryTarget settings, SynchronizationContext ownerContext,
            IPersistenceRetryScheduler scheduler, TimeSpan retryDelay)
        {
            _notes = notes ?? throw new ArgumentNullException(nameof(notes));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ownerContext = ownerContext ??
                throw new ArgumentNullException(nameof(ownerContext));
            _scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler));
            if (retryDelay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retryDelay));
            _retryDelay = retryDelay;

            _notes.SaveFailed += SaveFailed;
            _settings.SaveFailed += SaveFailed;
            if (_notes.HasUnsavedChanges || _settings.HasUnsavedChanges)
            {
                _failureEpisode = true;
                ArmRetry();
            }
        }

        internal event EventHandler<PersistenceNoticeEventArgs> Notice;

        private bool IsDisposed
        {
            get { return Volatile.Read(ref _disposed) != 0; }
        }

        private void SaveFailed(object sender, PersistenceFailedEventArgs e)
        {
            RunOnOwner(delegate
            {
                if (IsDisposed) return;
                _failureEpisode = true;
                if (!_warningShown)
                {
                    _warningShown = true;
                    Publish(new PersistenceNoticeEventArgs(
                        PersistenceNoticeKind.Warning,
                        Object.ReferenceEquals(sender, _settings)
                            ? "设置" : "便利贴",
                        e == null || e.Result == null
                            ? String.Empty : e.Result.ErrorMessage));
                }
                ArmRetry();
            });
        }

        private void ArmRetry()
        {
            if (IsDisposed || _retryArmed) return;
            _retryArmed = true;
            _scheduler.Schedule(_retryDelay, delegate
            {
                if (IsDisposed) return;
                try
                {
                    _ownerContext.Post(delegate
                    {
                        RetryDueOnOwner();
                    }, null);
                }
                catch (Exception error)
                {
                    Trace.TraceError("Persistence retry post failed: {0}", error);
                }
            });
        }

        private void RetryDueOnOwner()
        {
            if (IsDisposed) return;
            _retryArmed = false;

            bool notesDirty = _notes.HasUnsavedChanges;
            bool settingsDirty = _settings.HasUnsavedChanges;
            if (!notesDirty && !settingsDirty)
            {
                ResolveFailureEpisode();
                return;
            }

            // Capture only when the writer is idle. A newer queued autosave is
            // already the best retry candidate and must not be displaced by an
            // older snapshot or crossed by an explicit barrier.
            if (notesDirty && !_notes.HasPendingSaves)
                _notes.RequestAutosave();
            if (settingsDirty && !_settings.HasPendingSaves)
                _settings.RequestAutosave();

            ArmRetry();
        }

        private void ResolveFailureEpisode()
        {
            _scheduler.Cancel();
            _retryArmed = false;
            if (_failureEpisode && _warningShown)
                Publish(new PersistenceNoticeEventArgs(
                    PersistenceNoticeKind.Recovered, String.Empty, String.Empty));
            _failureEpisode = false;
            _warningShown = false;
        }

        private void RunOnOwner(Action action)
        {
            if (IsDisposed || action == null) return;
            if (SynchronizationContext.Current == _ownerContext)
            {
                action();
                return;
            }
            try { _ownerContext.Post(delegate { action(); }, null); }
            catch (Exception error)
            {
                Trace.TraceError("Persistence owner post failed: {0}", error);
            }
        }

        private void Publish(PersistenceNoticeEventArgs e)
        {
            EventHandler<PersistenceNoticeEventArgs> handler = Notice;
            if (handler == null) return;
            try { handler(this, e); }
            catch (Exception error)
            {
                Trace.TraceError("Persistence notice failed: {0}", error);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _notes.SaveFailed -= SaveFailed;
            _settings.SaveFailed -= SaveFailed;
            _retryArmed = false;
            _scheduler.Cancel();
            _scheduler.Dispose();
            Notice = null;
        }
    }
}
