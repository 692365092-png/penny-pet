using System;
using System.Collections.Generic;
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
            if (TryShowAtPhysicalBounds(edit))
            {
                if (!edit) EmitSnapshot(StickyUiEventKind.SnapshotChanged);
                return CurrentResult();
            }
            if (edit)
            {
                _window.ShowAndEdit();
            }
            else
            {
                _window.ShowRestored();
                EmitSnapshot(StickyUiEventKind.SnapshotChanged);
            }
            return CurrentResult();
        }

        private bool TryShowAtPhysicalBounds(bool edit)
        {
            if (!IsAvailable) return false;
            StickyNoteData data = _window.Data;
            // Only the canonical standalone path uses the physical placement
            // executor. A dock member is repositioned by the owner via SetBounds
            // in the same batch, so we leave the dock geometry untouched here.
            if (String.IsNullOrWhiteSpace(data.DisplayId) ||
                data.LocalLogicalWidth <= 0 || data.LocalLogicalHeight <= 0 ||
                !String.IsNullOrEmpty(data.DockGroupId)) return false;
            System.Drawing.Rectangle physical =
                ResolveCanonicalPhysical(data);
            if (physical == System.Drawing.Rectangle.Empty) return false;
            // Suppress the intermediate BoundsChanged echo raised while the
            // HWND is first shown, so the wrong transient position can never
            // leak a corrupt canonical placement before SetWindowPos lands.
            bool previousApplying = _applyingBounds;
            _applyingBounds = true;
            try
            {
                _window.ShowAtPhysicalBounds(physical, edit);
            }
            finally { _applyingBounds = previousApplying; }
            return true;
        }

        // New-contract restore authority: when the canonical contract is valid
        // the DisplayId + LocalLogicalRect are the source of truth. Resolve the
        // display, project the display-local logical rect back to physical
        // pixels with the current display scale, and hand it to the native
        // physical placement executor. The persisted X/Y/Width/Height are a
        // compatibility projection only and are used as a visible fallback only
        // when the canonical contract is invalid or the target display is gone.
        private System.Drawing.Rectangle ResolveCanonicalPhysical(
            StickyNoteData data)
        {
            // The capture records the canonical placement by resolving the real
            // physical window with ResolvePhysicalRect, so that same resolver
            // must be used to project LocalLogicalRect back to physical pixels
            // (otherwise a mixed-DPI monitor can report a scale that skews the
            // projection and lands the note in a far corner). Prefer the
            // persisted physical rect's display when it still matches the saved
            // DisplayId; only then fall back to a DisplayId lookup for a
            // rearranged monitor layout.
            WindowsDisplayMetrics metricsByRect = (data.Width > 0 &&
                data.Height > 0)
                ? WindowsDisplayResolver.ResolvePhysicalRect(
                    data.X, data.Y,
                    data.X + data.Width, data.Y + data.Height)
                : null;
            WindowsDisplayMetrics metricsByDisplay =
                WindowsDisplayResolver.ResolveDisplay(
                    data.DisplayId ?? String.Empty);
            WindowsDisplayMetrics metrics =
                metricsByRect != null &&
                String.Equals(metricsByRect.DisplayId,
                    data.DisplayId ?? String.Empty,
                    StringComparison.OrdinalIgnoreCase)
                    ? metricsByRect : metricsByDisplay;
            if (metrics != null)
            {
                int left = metrics.PhysicalLeft + (int)Math.Round(
                    data.LocalLogicalX * metrics.Scale);
                int top = metrics.PhysicalTop + (int)Math.Round(
                    data.LocalLogicalY * metrics.Scale);
                int width = Math.Max(1, (int)Math.Round(
                    Math.Max(1, data.LocalLogicalWidth) * metrics.Scale));
                int height = Math.Max(1, (int)Math.Round(
                    Math.Max(1, data.LocalLogicalHeight) * metrics.Scale));
                return new System.Drawing.Rectangle(
                    left, top, width, height);
            }

            // Canonical contract invalid, or the saved DisplayId no longer
            // exists: reuse the persisted physical compatibility rect clamped
            // into the nearest work area so the note stays visible.
            if (data.Width > 0 && data.Height > 0)
            {
                System.Drawing.Rectangle persisted =
                    new System.Drawing.Rectangle(
                        data.X, data.Y, data.Width, data.Height);
                WindowsDisplayMetrics nearest =
                    WindowsDisplayResolver.ResolvePhysicalRect(
                        persisted.Left, persisted.Top,
                        persisted.Right, persisted.Bottom);
                if (nearest == null) return persisted;
                int left = Math.Max(nearest.WorkLeft,
                    Math.Min(persisted.Left,
                        nearest.WorkLeft + nearest.WorkWidth -
                            persisted.Width));
                int top = Math.Max(nearest.WorkTop,
                    Math.Min(persisted.Top,
                        nearest.WorkTop + nearest.WorkHeight -
                            persisted.Height));
                return new System.Drawing.Rectangle(
                    left, top, persisted.Width, persisted.Height);
            }
            return System.Drawing.Rectangle.Empty;
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

        internal StickyUiCommandResult UpdateReminders(
            IEnumerable<ReminderItem> reminders)
        {
            if (!IsAvailable) return StickyUiCommandResult.NotHandled();
            _window.UpdateReminderBanner(reminders ??
                new ReminderItem[0]);
            return CurrentResult();
        }

        internal StickyUiCommandResult SetBounds(StickyUiBounds bounds)
        {
            if (bounds == null) return StickyUiCommandResult.NotHandled();
            _applyingBounds = true;
            try
            {
                // Bounds are physical pixels (Dock/restore targets). Position
                // the HWND at physical pixels so per-monitor DPI keeps the note
                // on the correct monitor instead of re-interpreting physical as
                // DIP, which double-scales follower notes on a high-DPI display.
                _window.ShowAtPhysicalBounds(
                    new System.Drawing.Rectangle(
                        bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    false);
            }
            finally { _applyingBounds = false; }
            // Authoritative geometry mutation: publish one strictly newer
            // sequence with the final snapshot so the command result can never
            // collide with (or be overwritten by) a leaked intermediate event.
            _lastSnapshot = CaptureSnapshot();
            _sequence++;
            return StickyUiCommandResult.Handled(_lastSnapshot, _sequence);
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
            Raise(StickyUiEvent.Signal(
                StickyUiEventKind.ImeCompositionChanged, _noteId, true,
                _sequence));
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
            _window.DockDividerResizeStarted += DockDividerResizeStarted;
            _window.DockDividerResizing += DockDividerResizing;
            _window.DockDividerResizeCompleted += DockDividerResizeCompleted;
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
            _window.DockDividerResizeStarted -= DockDividerResizeStarted;
            _window.DockDividerResizing -= DockDividerResizing;
            _window.DockDividerResizeCompleted -= DockDividerResizeCompleted;
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
            Raise(StickyUiEvent.Signal(StickyUiEventKind.TypingActivity,
                _noteId, true, _sequence));
        }

        private void InputFocusChanged(object sender, EventArgs e)
        {
            Raise(StickyUiEvent.Signal(StickyUiEventKind.InputFocusChanged,
                _noteId, _window.HasFocusedTextInput, _sequence));
        }

        private void ImeCompositionChanged(object sender,
            ImeCompositionEventArgs e)
        {
            Raise(StickyUiEvent.Signal(
                StickyUiEventKind.ImeCompositionChanged, _noteId,
                e != null && e.Active, _sequence));
            if (e != null && !e.Active && _hideAfterImeComposition &&
                IsAvailable)
            {
                _hideAfterImeComposition = false;
                EmitSnapshot(StickyUiEventKind.CloseRequested);
            }
        }

        private void Shown(object sender, EventArgs e)
        {
            Raise(StickyUiEvent.Signal(StickyUiEventKind.FirstRendered,
                _noteId, true, _sequence));
        }

        private void BoundsChanged(object sender, EventArgs e)
        {
            if (_applyingBounds) return;
            if (_window.DockDividerResizeActive) return;
            EmitSnapshot(StickyUiEventKind.BoundsChanged);
        }

        private void HeaderDragStarted(object sender, EventArgs e)
        {
            EmitSnapshot(StickyUiEventKind.HeaderDragStarted);
        }

        private void HeaderDragMoved(object sender, EventArgs e)
        {
            // A programmatic bounds mutation is not a user drag. Suppress the
            // WPF LocationChanged echo so canonical state only receives the
            // authoritative final snapshot from SetBounds.
            if (_applyingBounds) return;
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
            Raise(StickyUiEvent.HorizontalResize(snapshot, _sequence,
                e == null ? 0 : e.Left, e == null ? 0 : e.Width));
        }

        private void DockDividerResizeStarted(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(StickyUiEventKind.DockDividerResizeStarted,
                e);
        }

        private void DockDividerResizing(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(StickyUiEventKind.DockDividerResizing, e);
        }

        private void DockDividerResizeCompleted(object sender,
            DockDividerResizeEventArgs e)
        {
            EmitDockDividerResize(
                StickyUiEventKind.DockDividerResizeCompleted, e);
        }

        private void EmitDockDividerResize(StickyUiEventKind kind,
            DockDividerResizeEventArgs e)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(StickyUiEvent.DividerResize(kind, snapshot, _sequence,
                e == null ? snapshot.Height : e.Height));
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
            Raise(StickyUiEvent.Signal(StickyUiEventKind.InputFocusChanged,
                _noteId, false, _sequence));
            Raise(StickyUiEvent.FromSnapshot(StickyUiEventKind.Closed, snapshot,
                _sequence));
        }

        private void RaiseRequest(StickyUiEventKind kind)
        {
            Raise(StickyUiEvent.Signal(kind, _noteId, false, _sequence));
        }

        private void RaiseReminderRequest(StickyUiEventKind kind,
            ReminderItem reminder)
        {
            Raise(StickyUiEvent.ReminderRequest(kind, _noteId, reminder,
                _sequence));
        }

        private void EmitSnapshot(StickyUiEventKind kind)
        {
            if (_eventsSuppressed || !IsAvailable) return;
            StickyNoteUiSnapshot snapshot = CaptureSnapshot();
            _lastSnapshot = snapshot;
            _sequence++;
            Raise(StickyUiEvent.FromSnapshot(kind, snapshot, _sequence));
        }

        private StickyNoteUiSnapshot CaptureSnapshot()
        {
            // Canonical placement is derived from the real physical window
            // bounds so mixed-DPI monitor origins never warp the stored
            // DisplayId + LocalLogicalRect. The compatibility X/Y/Width/Height
            // are the physical projection of the same placement, which feeds
            // the native placement executor and the existing Dock/legacy
            // runtime, never a second independent source of truth.
            CaptureCanonicalPlacement();
            return StickyNoteUiSnapshot.FromData(_window.Data);
        }

        private void CaptureCanonicalPlacement()
        {
            if (!IsAvailable) return;
            try
            {
                System.Drawing.Rectangle physical = _window.PhysicalBounds;
                if (physical == System.Drawing.Rectangle.Empty) return;
                WindowsDisplayMetrics metrics =
                    WindowsDisplayResolver.ResolvePhysicalRect(
                        physical.Left, physical.Top,
                        physical.Right, physical.Bottom);
                if (metrics != null)
                {
                    StickyCanonicalPlacement placement =
                        StickyPlacementMath.FromPhysicalRect(
                            metrics.DisplayId, metrics.PhysicalLeft,
                            metrics.PhysicalTop, metrics.Scale,
                            physical.Left, physical.Top,
                            physical.Width, physical.Height);
                    placement.ApplyTo(_window.Data);
                    return;
                }
            }
            catch
            {
                // A temporary DPI / display query failure must never erase the
                // canonical placement or write DIP into the physical fields.
                // Fall through and preserve the last valid canonical geometry.
            }
            // Only a note that never owned a valid canonical placement may
            // continue on the legacy DIP compatibility path. A note that
            // already has DisplayId + LocalLogicalRect keeps it unchanged.
            if (IsCanonicalValid(_window.Data)) return;
            _window.Data.X = _window.Left;
            _window.Data.Y = _window.Top;
            _window.Data.Width = _window.Width;
            _window.Data.Height = _window.Height;
        }

        private static bool IsCanonicalValid(StickyNoteData note)
        {
            return note != null &&
                !String.IsNullOrWhiteSpace(note.DisplayId) &&
                note.LocalLogicalWidth > 0 && note.LocalLogicalHeight > 0;
        }

        private void Raise(StickyUiEvent value)
        {
            if (_eventsSuppressed || _eventHandler == null) return;
            _eventHandler(this, value);
        }
    }
}
