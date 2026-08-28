using System;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;

namespace PennyPet
{
    // One STA thread and Dispatcher for all future sticky-note WPF windows.
    // Existing windows remain on the WinForms UI thread until they are moved
    // incrementally; this host is the stable execution boundary first.
    internal sealed class StickyUiHost : IDisposable
    {
        private readonly object _gate = new object();
        private Thread _thread;
        private Dispatcher _dispatcher;
        private Exception _startupError;
        private Func<StickyUiCommand, StickyUiCommandResult> _commandHandler;
        private Action<StickyUiEvent> _eventHandler;
        private SynchronizationContext _eventContext;
        // This is the only reference to a Canary WPF window outside that
        // window itself. It is read and written only by the sticky STA.
        private StickyNoteWindow _canaryWindow;
        private StickyNoteUiSnapshot _lastCanarySnapshot;
        private long _snapshotSequence;
        private bool _hideAfterImeComposition;
        private bool _acceptingCommands = true;

        internal void Start()
        {
            lock (_gate)
            {
                if (_thread != null) return;
                using (ManualResetEventSlim ready =
                    new ManualResetEventSlim(false))
                {
                    _thread = new Thread(new ThreadStart(delegate
                    {
                        try
                        {
                            _dispatcher = Dispatcher.CurrentDispatcher;
                            ready.Set();
                            Dispatcher.Run();
                        }
                        catch (Exception error)
                        {
                            _startupError = error;
                            ready.Set();
                        }
                    }));
                    _thread.IsBackground = true;
                    _thread.SetApartmentState(ApartmentState.STA);
                    _thread.Name = "Penny sticky UI";
                    _thread.Start();
                    if (!ready.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException(
                            "Sticky UI thread did not start in time.");
                    if (_startupError != null)
                        throw new InvalidOperationException(
                            "Sticky UI thread failed to start.",
                            _startupError);
                }
            }
        }

        internal void SetCommandHandler(
            Func<StickyUiCommand, StickyUiCommandResult> handler)
        {
            lock (_gate) _commandHandler = handler;
        }

        internal void ConfigureCanary(Action<StickyUiEvent> handler,
            SynchronizationContext eventContext)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (eventContext == null)
                throw new ArgumentNullException(nameof(eventContext));
            lock (_gate)
            {
                _eventHandler = handler;
                _eventContext = eventContext;
                _commandHandler = HandleCanaryCommand;
            }
        }

        internal void PostCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed)
        {
            PostCommand(command, completed, SynchronizationContext.Current);
        }

        internal void PostCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed,
            SynchronizationContext completionContext)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Func<StickyUiCommand, StickyUiCommandResult> handler;
            Dispatcher dispatcher;
            bool acceptingCommands;
            lock (_gate)
            {
                acceptingCommands = _acceptingCommands;
                handler = _commandHandler;
                dispatcher = _dispatcher;
            }
            if (!acceptingCommands)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.NotAccepted());
                return;
            }
            if (handler == null || dispatcher == null ||
                dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.NotHandled());
                return;
            }
            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal,
                    new Action(delegate
                    {
                        StickyUiCommandResult result;
                        try
                        {
                            result = handler(command) ??
                                StickyUiCommandResult.NotHandled();
                        }
                        catch (Exception error)
                        {
                            result = StickyUiCommandResult.Failed(error);
                        }
                        PostCompletion(completionContext, completed, result);
                    }));
            }
            catch (Exception error)
            {
                PostCompletion(completionContext, completed,
                    StickyUiCommandResult.Failed(error));
            }
        }

        private StickyUiCommandResult HandleCanaryCommand(
            StickyUiCommand command)
        {
            try
            {
                switch (command.Kind)
                {
                    case StickyUiCommandKind.Create:
                        return CreateCanaryWindow(command);
                    case StickyUiCommandKind.Show:
                        if (!OwnsCanary(command.NoteId))
                            return StickyUiCommandResult.NotHandled();
                        if (command.Flag) _canaryWindow.ShowAndEdit();
                        else
                        {
                            _canaryWindow.ShowRestored();
                            EmitSnapshot(StickyUiEventKind.SnapshotChanged);
                        }
                        return CurrentCanaryResult();
                    case StickyUiCommandKind.Hide:
                        if (!OwnsCanary(command.NoteId))
                            return StickyUiCommandResult.NotHandled();
                        if (_canaryWindow.IsImeCompositionActiveForHost)
                            _hideAfterImeComposition = true;
                        else _canaryWindow.HideNote();
                        return CurrentCanaryResult();
                    case StickyUiCommandKind.FocusPrimaryInput:
                        if (!OwnsCanary(command.NoteId))
                            return StickyUiCommandResult.NotHandled();
                        _canaryWindow.FocusPrimaryInputForTest();
                        return CurrentCanaryResult();
                    case StickyUiCommandKind.SetTopMost:
                        if (!OwnsCanary(command.NoteId))
                            return StickyUiCommandResult.NotHandled();
                        _canaryWindow.ApplyTopMostWindowState(command.Flag);
                        return CurrentCanaryResult();
                    case StickyUiCommandKind.Close:
                        return CloseCanaryWindow(command.NoteId);
                    default:
                        return StickyUiCommandResult.NotHandled();
                }
            }
            catch
            {
                if (_canaryWindow != null && !_canaryWindow.IsDisposed)
                {
                    StickyNoteWindow failed = _canaryWindow;
                    DetachCanaryWindowHandlers(failed);
                    try { failed.CloseForApplicationExit(); }
                    catch { }
                    _canaryWindow = null;
                }
                throw;
            }
        }

        private StickyUiCommandResult CreateCanaryWindow(
            StickyUiCommand command)
        {
            if (command.Snapshot == null ||
                !String.Equals(command.NoteId, command.Snapshot.NoteId,
                    StringComparison.OrdinalIgnoreCase))
                return StickyUiCommandResult.NotHandled();
            if (_canaryWindow != null && !_canaryWindow.IsDisposed)
                return OwnsCanary(command.NoteId)
                    ? CurrentCanaryResult()
                    : StickyUiCommandResult.NotHandled();

            StickyNoteData workingCopy = command.Snapshot.CreateWorkingCopy();
            StickyNoteWindow window = new StickyNoteWindow(workingCopy);
            _canaryWindow = window;
            _lastCanarySnapshot = command.Snapshot;
            window.NoteChanged += CanaryNoteChanged;
            window.TypingActivity += CanaryTypingActivity;
            window.ImeCompositionChanged += CanaryImeCompositionChanged;
            window.CloseRequested += CanaryCloseRequested;
            window.FormClosed += CanaryWindowClosed;
            try
            {
                if (command.Flag) window.ShowAndEdit();
                else
                {
                    window.ShowRestored();
                    EmitSnapshot(StickyUiEventKind.SnapshotChanged);
                }
                return CurrentCanaryResult();
            }
            catch
            {
                DetachCanaryWindowHandlers(window);
                try { window.CloseForApplicationExit(); }
                catch { }
                _canaryWindow = null;
                _lastCanarySnapshot = null;
                throw;
            }
        }

        private void DetachCanaryWindowHandlers(StickyNoteWindow window)
        {
            if (window == null) return;
            window.NoteChanged -= CanaryNoteChanged;
            window.TypingActivity -= CanaryTypingActivity;
            window.ImeCompositionChanged -= CanaryImeCompositionChanged;
            window.CloseRequested -= CanaryCloseRequested;
            window.FormClosed -= CanaryWindowClosed;
        }

        private bool OwnsCanary(string noteId)
        {
            return _canaryWindow != null && !_canaryWindow.IsDisposed &&
                String.Equals(_canaryWindow.Data.Id, noteId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private void CanaryNoteChanged(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.SnapshotChanged);
        }

        private void CanaryTypingActivity(object sender, EventArgs e)
        {
            PostEvent(new StickyUiEvent(StickyUiEventKind.TypingActivity,
                CurrentCanaryId(), null, true, _snapshotSequence));
        }

        private void CanaryImeCompositionChanged(object sender,
            ImeCompositionEventArgs e)
        {
            PostEvent(new StickyUiEvent(
                StickyUiEventKind.ImeCompositionChanged,
                CurrentCanaryId(), null, e != null && e.Active,
                _snapshotSequence));
            if (e != null && !e.Active && _hideAfterImeComposition &&
                _canaryWindow != null && !_canaryWindow.IsDisposed)
            {
                _hideAfterImeComposition = false;
                _canaryWindow.HideNote();
            }
        }

        private void CanaryCloseRequested(object sender, EventArgs e)
        {
            if (_canaryWindow == null || _canaryWindow.IsDisposed) return;
            if (_canaryWindow.IsImeCompositionActiveForHost)
                _hideAfterImeComposition = true;
            else _canaryWindow.HideNote();
        }

        private void CanaryWindowClosed(object sender, FormClosedEventArgs e)
        {
            StickyNoteWindow closed = sender as StickyNoteWindow;
            if (closed == null) return;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(closed.Data);
            _lastCanarySnapshot = snapshot;
            _snapshotSequence++;
            DetachCanaryWindowHandlers(closed);
            _canaryWindow = null;
            PostEvent(new StickyUiEvent(StickyUiEventKind.Closed,
                snapshot.NoteId, snapshot, false, _snapshotSequence));
        }

        private void EmitSnapshot(StickyUiEventKind kind)
        {
            if (_canaryWindow == null || _canaryWindow.IsDisposed) return;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(_canaryWindow.Data);
            _lastCanarySnapshot = snapshot;
            _snapshotSequence++;
            PostEvent(new StickyUiEvent(kind, snapshot.NoteId, snapshot,
                snapshot.Visible, _snapshotSequence));
        }

        private string CurrentCanaryId()
        {
            if (_canaryWindow != null && !_canaryWindow.IsDisposed)
                return _canaryWindow.Data.Id ?? String.Empty;
            return _lastCanarySnapshot == null
                ? String.Empty : _lastCanarySnapshot.NoteId;
        }

        private StickyUiCommandResult CurrentCanaryResult()
        {
            if (_canaryWindow != null && !_canaryWindow.IsDisposed)
                _lastCanarySnapshot =
                    StickyNoteUiSnapshot.FromData(_canaryWindow.Data);
            return StickyUiCommandResult.Handled(_lastCanarySnapshot,
                _snapshotSequence);
        }

        private StickyUiCommandResult CloseCanaryWindow(string noteId)
        {
            if (_canaryWindow == null || _canaryWindow.IsDisposed)
                return _lastCanarySnapshot != null &&
                    String.Equals(_lastCanarySnapshot.NoteId, noteId,
                        StringComparison.OrdinalIgnoreCase)
                    ? StickyUiCommandResult.Handled(_lastCanarySnapshot,
                        _snapshotSequence)
                    : StickyUiCommandResult.NotHandled();
            if (!OwnsCanary(noteId)) return StickyUiCommandResult.NotHandled();
            if (_canaryWindow.IsImeCompositionActiveForHost)
                return StickyUiCommandResult.NotAccepted();
            _canaryWindow.FlushPendingChanges();
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(_canaryWindow.Data);
            long sequence = _snapshotSequence;
            _canaryWindow.CloseForApplicationExit();
            return StickyUiCommandResult.Handled(snapshot, sequence);
        }

        private void PostEvent(StickyUiEvent value)
        {
            Action<StickyUiEvent> handler;
            SynchronizationContext context;
            lock (_gate)
            {
                handler = _eventHandler;
                context = _eventContext;
            }
            if (handler == null) return;
            if (context != null)
            {
                context.Post(delegate { handler(value); }, null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { handler(value); });
        }

        private static void PostCompletion(SynchronizationContext context,
            Action<StickyUiCommandResult> completed,
            StickyUiCommandResult result)
        {
            if (completed == null) return;
            if (context != null)
            {
                context.Post(delegate { completed(result); }, null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate { completed(result); });
        }

        internal void StopAcceptingCommands()
        {
            lock (_gate) _acceptingCommands = false;
        }

        internal void BeginShutdown()
        {
            StopAcceptingCommands();
            Dispatcher dispatcher;
            lock (_gate) dispatcher = _dispatcher;
            if (dispatcher != null && !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
                dispatcher.BeginInvoke(DispatcherPriority.Send,
                    new Action(delegate
                    {
                        if (_canaryWindow != null && !_canaryWindow.IsDisposed)
                        {
                            StickyNoteWindow closing = _canaryWindow;
                            if (closing.IsImeCompositionActiveForHost)
                                DetachCanaryWindowHandlers(closing);
                            else closing.FlushPendingChanges();
                            closing.CloseForApplicationExit();
                            _canaryWindow = null;
                        }
                        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                    }));
        }

        internal bool WaitForExit(int timeoutMilliseconds)
        {
            Thread thread;
            lock (_gate) thread = _thread;
            if (thread != null && thread != Thread.CurrentThread)
                return thread.Join(timeoutMilliseconds);
            return thread == null || thread == Thread.CurrentThread;
        }

        public void Dispose()
        {
            BeginShutdown();
        }
    }
}
