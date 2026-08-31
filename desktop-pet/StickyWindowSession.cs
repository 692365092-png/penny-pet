using System;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns one hosted WPF window and all of its STA-local lifecycle state.
    internal sealed class StickyWindowSession
    {
        private readonly string _noteId;
        private readonly StickyNoteWindow _window;
        private readonly Action<StickyWindowSession, StickyUiEvent>
            _eventHandler;
        private StickyNoteUiSnapshot _lastSnapshot;
        private long _sequence;
        private bool _hideAfterImeComposition;
        private bool _applyingBounds;
        private bool _eventsSuppressed;

        internal StickyWindowSession(StickyNoteUiSnapshot snapshot,
            Action<StickyWindowSession, StickyUiEvent> eventHandler)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            _noteId = snapshot.NoteId;
            _lastSnapshot = snapshot;
            _eventHandler = eventHandler;
            _window = new StickyNoteWindow(snapshot.CreateWorkingCopy());
            WireEvents();
        }

        internal string NoteId { get { return _noteId; } }
        internal bool IsAvailable
        {
            get { return _window != null && !_window.IsDisposed; }
        }
        internal bool IsImeCompositionActive
        {
            get
            {
                return IsAvailable &&
                    _window.IsImeCompositionActiveForHost;
            }
        }

        internal StickyUiCommandResult Show(bool edit)
        {
            if (edit) _window.ShowAndEdit();
            else
            {
                _window.ShowRestored();
                EmitSnapshot(StickyUiEventKind.SnapshotChanged);
            }
            return CurrentResult();
        }

        internal StickyUiCommandResult Hide()
        {
            if (IsImeCompositionActive) _hideAfterImeComposition = true;
            else _window.HideNote();
            return CurrentResult();
        }

        internal StickyUiCommandResult FocusPrimaryInput()
        {
            _window.FocusPrimaryInputForTest();
            return CurrentResult();
        }

        internal StickyUiCommandResult SetTopMost(bool topMost)
        {
            _window.ApplyTopMostWindowState(topMost);
            return CurrentResult();
        }

        internal StickyUiCommandResult SetDockResizeRole(
            StickyUiDockResizeRole role)
        {
            if (role == null) return StickyUiCommandResult.NotHandled();
            _window.SetDockResizeRole(role.Grouped, role.ResizeTop,
                role.ResizeBottom, role.SplitBottom,
                role.DividerMinimumHeight, role.DividerMaximumHeight);
            return CurrentResult();
        }

        internal StickyUiCommandResult SetBounds(StickyUiBounds bounds)
        {
            if (bounds == null) return StickyUiCommandResult.NotHandled();
            _applyingBounds = true;
            try
            {
                _window.Left = bounds.X;
                _window.Top = bounds.Y;
                _window.Width = bounds.Width;
                _window.Height = bounds.Height;
                _window.UpdateLayout();
            }
            finally { _applyingBounds = false; }
            return CurrentResult();
        }

        internal StickyUiCommandResult Close()
        {
            if (IsImeCompositionActive)
                return StickyUiCommandResult.NotAccepted();
            _window.FlushPendingChanges();
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            long sequence = _sequence;
            _window.CloseForApplicationExit();
            return StickyUiCommandResult.Handled(snapshot, sequence);
        }

        internal StickyUiFinalSnapshot FlushAndCaptureFinal()
        {
            _window.FlushPendingChanges();
            _lastSnapshot = CaptureSnapshot();
            _sequence++;
            return new StickyUiFinalSnapshot(_lastSnapshot, _sequence);
        }

        internal void ReportImeCompositionActive()
        {
            Raise(new StickyUiEvent(StickyUiEventKind.ImeCompositionChanged,
                _noteId, null, true, _sequence));
        }

        internal void SetEventsSuppressed(bool suppressed)
        {
            _eventsSuppressed = suppressed;
        }

        internal void CloseForBatch()
        {
            UnwireEvents();
            if (IsAvailable) _window.CloseForApplicationExit();
        }

        internal void CloseAfterFailure()
        {
            UnwireEvents();
            try
            {
                if (IsAvailable) _window.CloseForApplicationExit();
            }
            catch { }
        }

        internal void CloseForHostShutdown()
        {
            if (!IsAvailable) return;
            if (IsImeCompositionActive) UnwireEvents();
            else _window.FlushPendingChanges();
            _window.CloseForApplicationExit();
        }

        internal StickyUiCommandResult CurrentResult()
        {
            if (IsAvailable) _lastSnapshot = CaptureSnapshot();
            return StickyUiCommandResult.Handled(_lastSnapshot, _sequence);
        }

        private void WireEvents()
        {
            _window.NoteChanged += NoteChanged;
            _window.TypingActivity += TypingActivity;
            _window.InputFocusChanged += InputFocusChanged;
            _window.ImeCompositionChanged += ImeCompositionChanged;
            _window.Shown += Shown;
            _window.LocationChanged += BoundsChanged;
            _window.SizeChanged += BoundsChanged;
            _window.HeaderDragStarted += HeaderDragStarted;
            _window.HeaderDragMoved += HeaderDragMoved;
            _window.HeaderDragCompleted += HeaderDragCompleted;
            _window.DockHorizontalResizing += DockHorizontalResizing;
            _window.CancelReminderRequested += CancelReminderRequested;
            _window.ModifyReminderRequested += ModifyReminderRequested;
            _window.DeleteReminderRequested += DeleteReminderRequested;
            _window.CloseRequested += CloseRequested;
            _window.DeleteRequested += DeleteRequested;
            _window.NewNoteRequested += NewNoteRequested;
            _window.NewTodoRequested += NewTodoRequested;
            _window.NewScheduleRequested += NewScheduleRequested;
            _window.FormClosed += WindowClosed;
        }

        private void UnwireEvents()
        {
            _window.NoteChanged -= NoteChanged;
            _window.TypingActivity -= TypingActivity;
            _window.InputFocusChanged -= InputFocusChanged;
            _window.ImeCompositionChanged -= ImeCompositionChanged;
            _window.Shown -= Shown;
            _window.LocationChanged -= BoundsChanged;
            _window.SizeChanged -= BoundsChanged;
            _window.HeaderDragStarted -= HeaderDragStarted;
            _window.HeaderDragMoved -= HeaderDragMoved;
            _window.HeaderDragCompleted -= HeaderDragCompleted;
            _window.DockHorizontalResizing -= DockHorizontalResizing;
            _window.CancelReminderRequested -= CancelReminderRequested;
            _window.ModifyReminderRequested -= ModifyReminderRequested;
            _window.DeleteReminderRequested -= DeleteReminderRequested;
            _window.CloseRequested -= CloseRequested;
            _window.DeleteRequested -= DeleteRequested;
            _window.NewNoteRequested -= NewNoteRequested;
            _window.NewTodoRequested -= NewTodoRequested;
            _window.NewScheduleRequested -= NewScheduleRequested;
            _window.FormClosed -= WindowClosed;
        }

        private void NoteChanged(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.SnapshotChanged);
        }

        private void TypingActivity(object sender, EventArgs e)
        {
            Raise(new StickyUiEvent(StickyUiEventKind.TypingActivity,
                _noteId, null, true, _sequence));
        }

        private void InputFocusChanged(object sender, EventArgs e)
        {
            Raise(new StickyUiEvent(StickyUiEventKind.InputFocusChanged,
                _noteId, null, _window.HasFocusedTextInput, _sequence));
        }

        private void ImeCompositionChanged(object sender,
            ImeCompositionEventArgs e)
        {
            Raise(new StickyUiEvent(StickyUiEventKind.ImeCompositionChanged,
                _noteId, null, e != null && e.Active, _sequence));
            if (e != null && !e.Active && _hideAfterImeComposition &&
                IsAvailable)
            {
                _hideAfterImeComposition = false;
                EmitSnapshot(StickyUiEventKind.CloseRequested);
            }
        }

        private void Shown(object sender, EventArgs e)
        {
            Raise(new StickyUiEvent(StickyUiEventKind.FirstRendered,
                _noteId, null, true, _sequence));
        }

        private void BoundsChanged(object sender, EventArgs e)
        {
            bool dividerInput = !_applyingBounds &&
                e is System.Windows.SizeChangedEventArgs &&
                _window.DockDividerResizeActive;
            EmitSnapshot(dividerInput
                ? StickyUiEventKind.DockDividerResizing
                : StickyUiEventKind.BoundsChanged);
        }

        private void HeaderDragStarted(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragStarted);
        }

        private void HeaderDragMoved(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragMoved);
        }

        private void HeaderDragCompleted(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragCompleted);
        }

        private void DockHorizontalResizing(object sender,
            DockHorizontalResizeEventArgs e)
        {
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(new StickyUiEvent(
                StickyUiEventKind.DockHorizontalResizing,
                _noteId, snapshot, false, _sequence, null,
                e == null ? 0 : e.Left, e == null ? 0 : e.Width));
        }

        private void CancelReminderRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.CancelReminderRequested);
        }

        private void ModifyReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            RaiseReminderRequest(StickyUiEventKind.ModifyReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void DeleteReminderRequested(object sender,
            ReminderActionEventArgs e)
        {
            RaiseReminderRequest(StickyUiEventKind.DeleteReminderRequested,
                e == null ? null : e.Reminder);
        }

        private void CloseRequested(object sender, EventArgs e)
        {
            if (IsImeCompositionActive) _hideAfterImeComposition = true;
            else EmitSnapshot(StickyUiEventKind.CloseRequested);
        }

        private void DeleteRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.DeleteRequested);
        }

        private void NewNoteRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewNoteRequested);
        }

        private void NewTodoRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewTodoRequested);
        }

        private void NewScheduleRequested(object sender, EventArgs e)
        {
            RaiseRequest(StickyUiEventKind.NewScheduleRequested);
        }

        private void WindowClosed(object sender, FormClosedEventArgs e)
        {
            if (_eventsSuppressed) return;
            StickyNoteUiSnapshot snapshot =
                StickyNoteUiSnapshot.FromData(_window.Data);
            _lastSnapshot = snapshot;
            _sequence++;
            UnwireEvents();
            Raise(new StickyUiEvent(StickyUiEventKind.InputFocusChanged,
                _noteId, null, false, _sequence));
            Raise(new StickyUiEvent(StickyUiEventKind.Closed,
                _noteId, snapshot, false, _sequence));
        }

        private void RaiseRequest(StickyUiEventKind kind)
        {
            Raise(new StickyUiEvent(kind, _noteId, null, false, _sequence));
        }

        private void RaiseReminderRequest(StickyUiEventKind kind,
            ReminderItem reminder)
        {
            Raise(new StickyUiEvent(kind, _noteId, null, false, _sequence,
                reminder));
        }

        private void EmitSnapshot(StickyUiEventKind kind)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(new StickyUiEvent(kind, _noteId, snapshot,
                snapshot.Visible, _sequence));
        }

        private StickyNoteUiSnapshot CaptureSnapshot()
        {
            _window.Data.X = _window.Left;
            _window.Data.Y = _window.Top;
            _window.Data.Width = _window.Width;
            _window.Data.Height = _window.Height;
            return StickyNoteUiSnapshot.FromData(_window.Data);
        }

        private void Raise(StickyUiEvent value)
        {
            if (_eventsSuppressed || _eventHandler == null) return;
            _eventHandler(this, value);
        }
    }
}
