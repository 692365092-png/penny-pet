using System;
using System.Collections.Generic;
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
        private sealed class StickyWindowEntry
        {
            internal StickyNoteWindow Window;
            internal StickyNoteUiSnapshot LastSnapshot;
            internal long Sequence;
            internal bool HideAfterImeComposition;
            internal bool ApplyingBounds;
        }

        private readonly object _gate = new object();
        private Thread _thread;
        private Dispatcher _dispatcher;
        private Exception _startupError;
        private Func<StickyUiCommand, StickyUiCommandResult> _commandHandler;
        private Action<StickyUiEvent> _eventHandler;
        private SynchronizationContext _eventContext;
        // This registry is the only owner of hosted WPF window references.
        // It is read and written only by the sticky STA.
        private readonly Dictionary<string, StickyWindowEntry> _windows =
            new Dictionary<string, StickyWindowEntry>(
                StringComparer.OrdinalIgnoreCase);
        private bool _batchClosing;
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
            StickyWindowEntry entry = null;
            try
            {
                switch (command.Kind)
                {
                    case StickyUiCommandKind.Create:
                        return CreateCanaryWindow(command);
                    case StickyUiCommandKind.Show:
                        if (!TryGetEntry(command.NoteId, out entry))
                            return StickyUiCommandResult.NotHandled();
                        if (command.Flag) entry.Window.ShowAndEdit();
                        else
                        {
                            entry.Window.ShowRestored();
                            EmitSnapshot(entry,
                                StickyUiEventKind.SnapshotChanged);
                        }
                        return CurrentResult(entry);
                    case StickyUiCommandKind.Hide:
                        if (!TryGetEntry(command.NoteId, out entry))
                            return StickyUiCommandResult.NotHandled();
                        if (entry.Window.IsImeCompositionActiveForHost)
                            entry.HideAfterImeComposition = true;
                        else entry.Window.HideNote();
                        return CurrentResult(entry);
                    case StickyUiCommandKind.FocusPrimaryInput:
                        if (!TryGetEntry(command.NoteId, out entry))
                            return StickyUiCommandResult.NotHandled();
                        entry.Window.FocusPrimaryInputForTest();
                        return CurrentResult(entry);
                    case StickyUiCommandKind.SetTopMost:
                        if (!TryGetEntry(command.NoteId, out entry))
                            return StickyUiCommandResult.NotHandled();
                        entry.Window.ApplyTopMostWindowState(command.Flag);
                        return CurrentResult(entry);
                    case StickyUiCommandKind.SetDockResizeRole:
                        if (!TryGetEntry(command.NoteId, out entry) ||
                            command.DockResizeRole == null)
                            return StickyUiCommandResult.NotHandled();
                        StickyUiDockResizeRole role = command.DockResizeRole;
                        entry.Window.SetDockResizeRole(role.Grouped,
                            role.ResizeTop, role.ResizeBottom,
                            role.SplitBottom, role.DividerMinimumHeight,
                            role.DividerMaximumHeight);
                        return CurrentResult(entry);
                    case StickyUiCommandKind.SetBounds:
                        if (!TryGetEntry(command.NoteId, out entry) ||
                            command.Bounds == null)
                            return StickyUiCommandResult.NotHandled();
                        entry.ApplyingBounds = true;
                        try
                        {
                            entry.Window.Left = command.Bounds.X;
                            entry.Window.Top = command.Bounds.Y;
                            entry.Window.Width = command.Bounds.Width;
                            entry.Window.Height = command.Bounds.Height;
                            entry.Window.UpdateLayout();
                        }
                        finally { entry.ApplyingBounds = false; }
                        return CurrentResult(entry);
                    case StickyUiCommandKind.Close:
                        return CloseCanaryWindow(command.NoteId);
                    case StickyUiCommandKind.CloseAll:
                        return CloseAllWindows();
                    default:
                        return StickyUiCommandResult.NotHandled();
                }
            }
            catch
            {
                if (entry != null && entry.Window != null &&
                    !entry.Window.IsDisposed)
                {
                    DetachWindowHandlers(entry.Window);
                    try { entry.Window.CloseForApplicationExit(); }
                    catch { }
                    _windows.Remove(command.NoteId);
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
            StickyWindowEntry existing;
            if (TryGetEntry(command.NoteId, out existing))
                return CurrentResult(existing);

            StickyNoteData workingCopy = command.Snapshot.CreateWorkingCopy();
            StickyNoteWindow window = new StickyNoteWindow(workingCopy);
            StickyWindowEntry entry = new StickyWindowEntry();
            entry.Window = window;
            entry.LastSnapshot = command.Snapshot;
            _windows[command.NoteId] = entry;
            window.NoteChanged += CanaryNoteChanged;
            window.TypingActivity += CanaryTypingActivity;
            window.InputFocusChanged += CanaryInputFocusChanged;
            window.ImeCompositionChanged += CanaryImeCompositionChanged;
            window.Shown += CanaryShown;
            window.LocationChanged += CanaryBoundsChanged;
            window.SizeChanged += CanaryBoundsChanged;
            window.HeaderDragStarted += CanaryHeaderDragStarted;
            window.HeaderDragMoved += CanaryHeaderDragMoved;
            window.HeaderDragCompleted += CanaryHeaderDragCompleted;
            window.DockHorizontalResizing += CanaryDockHorizontalResizing;
            window.CancelReminderRequested += CanaryCancelReminderRequested;
            window.ModifyReminderRequested += CanaryModifyReminderRequested;
            window.DeleteReminderRequested += CanaryDeleteReminderRequested;
            window.CloseRequested += CanaryCloseRequested;
            window.DeleteRequested += CanaryDeleteRequested;
            window.NewNoteRequested += CanaryNewNoteRequested;
            window.NewTodoRequested += CanaryNewTodoRequested;
            window.NewScheduleRequested += CanaryNewScheduleRequested;
            window.FormClosed += CanaryWindowClosed;
            try
            {
                if (command.Flag) window.ShowAndEdit();
                else
                {
                    window.ShowRestored();
                    EmitSnapshot(entry, StickyUiEventKind.SnapshotChanged);
                }
                return CurrentResult(entry);
            }
            catch
            {
                DetachWindowHandlers(window);
                try { window.CloseForApplicationExit(); }
                catch { }
                _windows.Remove(command.NoteId);
                throw;
            }
        }

        private void DetachWindowHandlers(StickyNoteWindow window)
        {
            if (window == null) return;
            window.NoteChanged -= CanaryNoteChanged;
            window.TypingActivity -= CanaryTypingActivity;
            window.InputFocusChanged -= CanaryInputFocusChanged;
            window.ImeCompositionChanged -= CanaryImeCompositionChanged;
            window.Shown -= CanaryShown;
            window.LocationChanged -= CanaryBoundsChanged;
            window.SizeChanged -= CanaryBoundsChanged;
            window.HeaderDragStarted -= CanaryHeaderDragStarted;
            window.HeaderDragMoved -= CanaryHeaderDragMoved;
            window.HeaderDragCompleted -= CanaryHeaderDragCompleted;
            window.DockHorizontalResizing -= CanaryDockHorizontalResizing;
            window.CancelReminderRequested -= CanaryCancelReminderRequested;
            window.ModifyReminderRequested -= CanaryModifyReminderRequested;
            window.DeleteReminderRequested -= CanaryDeleteReminderRequested;
            window.CloseRequested -= CanaryCloseRequested;
            window.DeleteRequested -= CanaryDeleteRequested;
            window.NewNoteRequested -= CanaryNewNoteRequested;
            window.NewTodoRequested -= CanaryNewTodoRequested;
            window.NewScheduleRequested -= CanaryNewScheduleRequested;
            window.FormClosed -= CanaryWindowClosed;
        }

        private bool TryGetEntry(string noteId, out StickyWindowEntry entry)
        {
            return _windows.TryGetValue(noteId ?? String.Empty, out entry) &&
                entry != null && entry.Window != null &&
                !entry.Window.IsDisposed;
        }

        private bool TryGetEntry(StickyNoteWindow window,
            out StickyWindowEntry entry)
        {
            entry = null;
            if (window == null || window.Data == null ||
                !TryGetEntry(window.Data.Id, out entry)) return false;
            return Object.ReferenceEquals(entry.Window, window);
        }

        private void CanaryNoteChanged(object sender, EventArgs e)
        {
            if (_batchClosing) return;
            StickyWindowEntry entry;
            if (TryGetEntry(sender as StickyNoteWindow, out entry))
                EmitSnapshot(entry, StickyUiEventKind.SnapshotChanged);
        }

        private void CanaryTypingActivity(object sender, EventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(StickyUiEventKind.TypingActivity,
                entry.Window.Data.Id, null, true, entry.Sequence));
        }

        private void CanaryInputFocusChanged(object sender, EventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(StickyUiEventKind.InputFocusChanged,
                entry.Window.Data.Id, null,
                entry.Window.HasFocusedTextInput, entry.Sequence));
        }

        private void CanaryImeCompositionChanged(object sender,
            ImeCompositionEventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(
                StickyUiEventKind.ImeCompositionChanged,
                entry.Window.Data.Id, null, e != null && e.Active,
                entry.Sequence));
            if (e != null && !e.Active && entry.HideAfterImeComposition &&
                !entry.Window.IsDisposed)
            {
                entry.HideAfterImeComposition = false;
                EmitSnapshot(entry, StickyUiEventKind.CloseRequested);
            }
        }

        private void CanaryShown(object sender, EventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(StickyUiEventKind.FirstRendered,
                entry.Window.Data.Id, null, true, entry.Sequence));
        }

        private void CanaryBoundsChanged(object sender, EventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            bool dividerInput = !entry.ApplyingBounds &&
                e is System.Windows.SizeChangedEventArgs &&
                entry.Window.DockDividerResizeActive;
            EmitSnapshot(entry, dividerInput
                ? StickyUiEventKind.DockDividerResizing
                : StickyUiEventKind.BoundsChanged);
        }

        private void CanaryHeaderDragStarted(object sender, EventArgs e)
        {
            EmitWindowSnapshot(sender, StickyUiEventKind.HeaderDragStarted);
        }

        private void CanaryHeaderDragMoved(object sender, EventArgs e)
        {
            EmitWindowSnapshot(sender, StickyUiEventKind.HeaderDragMoved);
        }

        private void CanaryHeaderDragCompleted(object sender, EventArgs e)
        {
            EmitWindowSnapshot(sender, StickyUiEventKind.HeaderDragCompleted);
        }

        private void CanaryDockHorizontalResizing(object sender,
            DockHorizontalResizeEventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot(entry);
            entry.LastSnapshot = snapshot;
            entry.Sequence++;
            PostEvent(new StickyUiEvent(
                StickyUiEventKind.DockHorizontalResizing,
                entry.Window.Data.Id, snapshot, false, entry.Sequence,
                null, e == null ? 0 : e.Left,
                e == null ? 0 : e.Width));
        }

        private void CanaryCancelReminderRequested(object sender,
            EventArgs e)
        {
            PostWindowRequest(sender,
                StickyUiEventKind.CancelReminderRequested);
        }

        private void CanaryModifyReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            PostReminderRequest(sender,
                StickyUiEventKind.ModifyReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void CanaryDeleteReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            PostReminderRequest(sender,
                StickyUiEventKind.DeleteReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void CanaryCloseRequested(object sender, EventArgs e)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            if (entry.Window.IsImeCompositionActiveForHost)
                entry.HideAfterImeComposition = true;
            else EmitSnapshot(entry, StickyUiEventKind.CloseRequested);
        }

        private void CanaryDeleteRequested(object sender, EventArgs e)
        {
            PostWindowRequest(sender, StickyUiEventKind.DeleteRequested);
        }

        private void CanaryNewNoteRequested(object sender, EventArgs e)
        {
            PostWindowRequest(sender, StickyUiEventKind.NewNoteRequested);
        }

        private void CanaryNewTodoRequested(object sender, EventArgs e)
        {
            PostWindowRequest(sender, StickyUiEventKind.NewTodoRequested);
        }

        private void CanaryNewScheduleRequested(object sender, EventArgs e)
        {
            PostWindowRequest(sender, StickyUiEventKind.NewScheduleRequested);
        }

        private void PostWindowRequest(object sender, StickyUiEventKind kind)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(kind, entry.Window.Data.Id, null,
                false, entry.Sequence));
        }

        private void EmitWindowSnapshot(object sender, StickyUiEventKind kind)
        {
            StickyWindowEntry entry;
            if (TryGetEntry(sender as StickyNoteWindow, out entry))
                EmitSnapshot(entry, kind);
        }

        private void PostReminderRequest(object sender,
            StickyUiEventKind kind, ReminderItem reminder)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(sender as StickyNoteWindow, out entry)) return;
            PostEvent(new StickyUiEvent(kind, entry.Window.Data.Id, null,
                false, entry.Sequence, reminder));
        }

        private void CanaryWindowClosed(object sender, FormClosedEventArgs e)
        {
            StickyNoteWindow closed = sender as StickyNoteWindow;
            if (closed == null) return;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(closed.Data);
            StickyWindowEntry entry;
            if (_batchClosing || !_windows.TryGetValue(snapshot.NoteId,
                out entry) || entry == null ||
                !Object.ReferenceEquals(entry.Window, closed)) return;
            entry.LastSnapshot = snapshot;
            entry.Sequence++;
            DetachWindowHandlers(closed);
            _windows.Remove(snapshot.NoteId);
            PostEvent(new StickyUiEvent(StickyUiEventKind.InputFocusChanged,
                snapshot.NoteId, null, false, entry.Sequence));
            PostEvent(new StickyUiEvent(StickyUiEventKind.Closed,
                snapshot.NoteId, snapshot, false, entry.Sequence));
        }

        private void EmitSnapshot(StickyWindowEntry entry,
            StickyUiEventKind kind)
        {
            if (entry == null || entry.Window == null ||
                entry.Window.IsDisposed) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot(entry);
            entry.LastSnapshot = snapshot;
            entry.Sequence++;
            PostEvent(new StickyUiEvent(kind, snapshot.NoteId, snapshot,
                snapshot.Visible, entry.Sequence));
        }

        private StickyUiCommandResult CurrentResult(StickyWindowEntry entry)
        {
            if (entry.Window != null && !entry.Window.IsDisposed)
                entry.LastSnapshot = CaptureSnapshot(entry);
            return StickyUiCommandResult.Handled(entry.LastSnapshot,
                entry.Sequence);
        }

        private static StickyNoteUiSnapshot CaptureSnapshot(
            StickyWindowEntry entry)
        {
            StickyNoteWindow window = entry.Window;
            window.Data.X = window.Left;
            window.Data.Y = window.Top;
            window.Data.Width = window.Width;
            window.Data.Height = window.Height;
            return StickyNoteUiSnapshot.FromData(window.Data);
        }

        private StickyUiCommandResult CloseCanaryWindow(string noteId)
        {
            StickyWindowEntry entry;
            if (!TryGetEntry(noteId, out entry))
                return StickyUiCommandResult.NotHandled();
            if (entry.Window.IsImeCompositionActiveForHost)
                return StickyUiCommandResult.NotAccepted();
            entry.Window.FlushPendingChanges();
            StickyNoteUiSnapshot snapshot = CaptureSnapshot(entry);
            entry.LastSnapshot = snapshot;
            entry.Sequence++;
            long sequence = entry.Sequence;
            entry.Window.CloseForApplicationExit();
            return StickyUiCommandResult.Handled(snapshot, sequence);
        }

        private StickyUiCommandResult CloseAllWindows()
        {
            List<StickyWindowEntry> entries =
                new List<StickyWindowEntry>(_windows.Values);
            bool imeActive = false;
            foreach (StickyWindowEntry entry in entries)
            {
                if (entry.Window == null || entry.Window.IsDisposed) continue;
                if (!entry.Window.IsImeCompositionActiveForHost) continue;
                imeActive = true;
                PostEvent(new StickyUiEvent(
                    StickyUiEventKind.ImeCompositionChanged,
                    entry.Window.Data.Id, null, true, entry.Sequence));
            }
            if (imeActive) return StickyUiCommandResult.NotAccepted();

            _batchClosing = true;
            try
            {
                List<StickyUiFinalSnapshot> finalSnapshots =
                    new List<StickyUiFinalSnapshot>();
                foreach (StickyWindowEntry entry in entries)
                {
                    if (entry.Window == null || entry.Window.IsDisposed)
                        continue;
                    entry.Window.FlushPendingChanges();
                    entry.LastSnapshot = CaptureSnapshot(entry);
                    entry.Sequence++;
                    finalSnapshots.Add(new StickyUiFinalSnapshot(
                        entry.LastSnapshot, entry.Sequence));
                }
                foreach (StickyWindowEntry entry in entries)
                {
                    if (entry.Window == null || entry.Window.IsDisposed)
                        continue;
                    DetachWindowHandlers(entry.Window);
                    entry.Window.CloseForApplicationExit();
                }
                _windows.Clear();
                return StickyUiCommandResult.Handled(
                    finalSnapshots.ToArray());
            }
            finally { _batchClosing = false; }
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
                        foreach (StickyWindowEntry entry in
                            new List<StickyWindowEntry>(_windows.Values))
                        {
                            StickyNoteWindow closing = entry.Window;
                            if (closing == null || closing.IsDisposed) continue;
                            if (closing.IsImeCompositionActiveForHost)
                                DetachWindowHandlers(closing);
                            else closing.FlushPendingChanges();
                            closing.CloseForApplicationExit();
                        }
                        _windows.Clear();
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
