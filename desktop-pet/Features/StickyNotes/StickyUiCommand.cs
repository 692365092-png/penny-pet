using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal enum StickyUiCommandKind
    {
        Create,
        Show,
        Hide,
        FocusPrimaryInput,
        SetTopMost,
        SetDockResizeRole,
        SetBounds,
        Close,
        CloseAll,
        UpdateReminders
    }

    internal sealed class StickyUiCommand
    {
        // Kept internal for focused self-tests; production uses named factories.
        internal StickyUiCommand(StickyUiCommandKind kind, string noteId,
            bool flag, StickyNoteUiSnapshot snapshot = null,
            StickyUiBounds bounds = null,
            StickyUiDockResizeRole dockResizeRole = null,
            ReminderItem[] reminders = null)
        {
            Kind = kind;
            NoteId = noteId ?? String.Empty;
            Flag = flag;
            Snapshot = snapshot;
            Bounds = bounds;
            DockResizeRole = dockResizeRole;
            Reminders = CopyReminders(reminders);
        }

        internal static StickyUiCommand Create(StickyNoteUiSnapshot snapshot,
            bool focusEditor, IEnumerable<ReminderItem> reminders = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return new StickyUiCommand(StickyUiCommandKind.Create,
                snapshot.NoteId, focusEditor, snapshot, null, null,
                CopyReminders(reminders));
        }

        internal static StickyUiCommand UpdateReminders(string noteId,
            IEnumerable<ReminderItem> reminders)
        {
            return new StickyUiCommand(StickyUiCommandKind.UpdateReminders,
                noteId, false, null, null, null, CopyReminders(reminders));
        }

        internal static StickyUiCommand Show(string noteId, bool focusEditor)
        {
            return new StickyUiCommand(StickyUiCommandKind.Show, noteId,
                focusEditor);
        }

        internal static StickyUiCommand Hide(string noteId)
        {
            return new StickyUiCommand(StickyUiCommandKind.Hide, noteId,
                false);
        }

        internal static StickyUiCommand FocusPrimaryInput(string noteId)
        {
            return new StickyUiCommand(StickyUiCommandKind.FocusPrimaryInput,
                noteId, false);
        }

        internal static StickyUiCommand SetTopMost(string noteId, bool value)
        {
            return new StickyUiCommand(StickyUiCommandKind.SetTopMost, noteId,
                value);
        }

        internal static StickyUiCommand SetDockResizeRole(string noteId,
            StickyUiDockResizeRole role)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            return new StickyUiCommand(StickyUiCommandKind.SetDockResizeRole,
                noteId, false, null, null, role);
        }

        internal static StickyUiCommand SetBounds(string noteId,
            StickyUiBounds bounds)
        {
            if (bounds == null) throw new ArgumentNullException(nameof(bounds));
            return new StickyUiCommand(StickyUiCommandKind.SetBounds, noteId,
                false, null, bounds);
        }

        internal static StickyUiCommand Close(string noteId)
        {
            return new StickyUiCommand(StickyUiCommandKind.Close, noteId,
                false);
        }

        internal static StickyUiCommand CloseAll()
        {
            return new StickyUiCommand(StickyUiCommandKind.CloseAll,
                String.Empty, false);
        }

        internal StickyUiCommandKind Kind { get; private set; }
        internal string NoteId { get; private set; }
        internal bool Flag { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
        internal StickyUiBounds Bounds { get; private set; }
        internal StickyUiDockResizeRole DockResizeRole { get; private set; }
        internal ReminderItem[] Reminders { get; private set; }

        private static ReminderItem[] CopyReminders(
            IEnumerable<ReminderItem> reminders)
        {
            List<ReminderItem> copy = new List<ReminderItem>();
            if (reminders != null)
            {
                foreach (ReminderItem source in reminders)
                {
                    if (source == null) continue;
                    copy.Add(new ReminderItem(
                        source.DeadlineUtc,
                        source.Text,
                        source.SourceNoteId,
                        source.FontSizeTwips / 20F,
                        source.PreAlertEnabled));
                    if (copy.Count >= 5) break;
                }
            }
            return copy.ToArray();
        }
    }

    // Immutable cross-thread value snapshot. The WPF STA creates its own
    // mutable working copy; the repository-owned model never crosses threads.
    internal sealed class StickyNoteUiSnapshot
    {
        private StickyNoteUiSnapshot(StickyNoteData source)
        {
            NoteId = source.Id ?? String.Empty;
            Title = source.Title ?? String.Empty;
            Text = source.Text ?? String.Empty;
            RichTextRtf = source.RichTextRtf ?? String.Empty;
            FontFamilyName = source.FontFamilyName ?? String.Empty;
            FontSizeTwips = source.FontSizeTwips;
            ColorArgb = source.ColorArgb;
            BackgroundOpacityPercent = source.BackgroundOpacityPercent;
            TextColorArgb = source.TextColorArgb;
            Visible = source.Visible;
            AlwaysOnTop = source.AlwaysOnTop;
            IsTodoList = source.IsTodoList;
            IsSchedule = source.IsSchedule;
            X = source.X;
            Y = source.Y;
            Width = source.Width;
            Height = source.Height;
            CreatedUtcTicks = source.CreatedUtcTicks;
            ModifiedUtcTicks = source.ModifiedUtcTicks;
            ReminderUtcTicks = source.ReminderUtcTicks;
            List<StickyTodoUiSnapshot> todos =
                new List<StickyTodoUiSnapshot>();
            foreach (StickyTodoItem item in source.TodoItems)
                if (item != null) todos.Add(
                    new StickyTodoUiSnapshot(item));
            TodoItems = todos.ToArray();
            List<StickyScheduleUiSnapshot> schedules =
                new List<StickyScheduleUiSnapshot>();
            foreach (StickyScheduleItem item in source.ScheduleItems)
                if (item != null) schedules.Add(
                    new StickyScheduleUiSnapshot(item));
            ScheduleItems = schedules.ToArray();
        }

        internal string NoteId { get; private set; }
        internal string Title { get; private set; }
        internal string Text { get; private set; }
        internal string RichTextRtf { get; private set; }
        internal string FontFamilyName { get; private set; }
        internal int FontSizeTwips { get; private set; }
        internal int ColorArgb { get; private set; }
        internal int BackgroundOpacityPercent { get; private set; }
        internal int TextColorArgb { get; private set; }
        internal bool Visible { get; private set; }
        internal bool AlwaysOnTop { get; private set; }
        internal bool IsTodoList { get; private set; }
        internal bool IsSchedule { get; private set; }
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal long CreatedUtcTicks { get; private set; }
        internal long ModifiedUtcTicks { get; private set; }
        internal long ReminderUtcTicks { get; private set; }
        internal StickyTodoUiSnapshot[] TodoItems { get; private set; }
        internal StickyScheduleUiSnapshot[] ScheduleItems { get; private set; }

        internal static StickyNoteUiSnapshot FromData(StickyNoteData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new StickyNoteUiSnapshot(source);
        }

        internal StickyNoteData CreateWorkingCopy()
        {
            StickyNoteData copy = new StickyNoteData();
            ApplyTo(copy);
            return copy;
        }

        internal void ApplyTo(StickyNoteData target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Id = NoteId;
            target.Title = Title;
            target.Text = Text;
            target.RichTextRtf = RichTextRtf;
            target.FontFamilyName = FontFamilyName;
            target.FontSizeTwips = FontSizeTwips;
            target.ColorArgb = ColorArgb;
            target.BackgroundOpacityPercent = BackgroundOpacityPercent;
            target.TextColorArgb = TextColorArgb;
            target.Visible = Visible;
            target.AlwaysOnTop = AlwaysOnTop;
            target.IsTodoList = IsTodoList;
            target.IsSchedule = IsSchedule;
            target.X = X;
            target.Y = Y;
            target.Width = Width;
            target.Height = Height;
            target.CreatedUtcTicks = CreatedUtcTicks;
            target.ModifiedUtcTicks = ModifiedUtcTicks;
            target.ReminderUtcTicks = ReminderUtcTicks;
            target.TodoItems.Clear();
            if (TodoItems != null)
                foreach (StickyTodoUiSnapshot item in TodoItems)
                    if (item != null) target.TodoItems.Add(
                        new StickyTodoItem(item.Text, item.State,
                            item.IsPinned));
            target.ScheduleItems.Clear();
            if (ScheduleItems != null)
                foreach (StickyScheduleUiSnapshot item in ScheduleItems)
                    if (item != null) target.ScheduleItems.Add(
                        new StickyScheduleItem(item.Text,
                            new DateTime(item.TargetDateTicks),
                            item.IsPinned));
        }
    }

    internal sealed class StickyTodoUiSnapshot
    {
        internal StickyTodoUiSnapshot(StickyTodoItem source)
        {
            Text = source.Text ?? String.Empty;
            State = source.State;
            IsPinned = source.IsPinned;
        }

        internal string Text { get; private set; }
        internal StickyTodoState State { get; private set; }
        internal bool IsPinned { get; private set; }
    }

    internal sealed class StickyScheduleUiSnapshot
    {
        internal StickyScheduleUiSnapshot(StickyScheduleItem source)
        {
            Text = source.Text ?? String.Empty;
            TargetDateTicks = source.TargetDateTicks;
            IsPinned = source.IsPinned;
        }

        internal string Text { get; private set; }
        internal long TargetDateTicks { get; private set; }
        internal bool IsPinned { get; private set; }
    }

    internal sealed class StickyUiBounds
    {
        internal StickyUiBounds(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
    }

    internal sealed class StickyUiDockResizeRole
    {
        internal StickyUiDockResizeRole(bool grouped, bool resizeTop,
            bool resizeBottom, bool splitBottom, int dividerMinimumHeight,
            int dividerMaximumHeight)
        {
            Grouped = grouped;
            ResizeTop = resizeTop;
            ResizeBottom = resizeBottom;
            SplitBottom = splitBottom;
            DividerMinimumHeight = dividerMinimumHeight;
            DividerMaximumHeight = dividerMaximumHeight;
        }

        internal bool Grouped { get; private set; }
        internal bool ResizeTop { get; private set; }
        internal bool ResizeBottom { get; private set; }
        internal bool SplitBottom { get; private set; }
        internal int DividerMinimumHeight { get; private set; }
        internal int DividerMaximumHeight { get; private set; }
    }

    internal enum StickyUiEventKind
    {
        SnapshotChanged,
        TypingActivity,
        InputFocusChanged,
        ImeCompositionChanged,
        FirstRendered,
        BoundsChanged,
        HeaderDragStarted,
        HeaderDragMoved,
        HeaderDragCompleted,
        DockHorizontalResizing,
        DockDividerResizing,
        CancelReminderRequested,
        ModifyReminderRequested,
        DeleteReminderRequested,
        CloseRequested,
        DeleteRequested,
        NewNoteRequested,
        NewTodoRequested,
        NewScheduleRequested,
        Closed
    }

    internal sealed class StickyUiEvent
    {
        // Kept internal for focused self-tests; sessions use payload factories.
        internal StickyUiEvent(StickyUiEventKind kind, string noteId,
            StickyNoteUiSnapshot snapshot, bool flag, long sequence,
            ReminderItem reminder = null, int left = 0, int width = 0)
        {
            Kind = kind;
            NoteId = noteId ?? String.Empty;
            Snapshot = snapshot;
            Flag = flag;
            Sequence = sequence;
            Reminder = reminder;
            Left = left;
            Width = width;
        }

        internal static StickyUiEvent Signal(StickyUiEventKind kind,
            string noteId, bool flag, long sequence)
        {
            return new StickyUiEvent(kind, noteId, null, flag, sequence);
        }

        internal static StickyUiEvent FromSnapshot(StickyUiEventKind kind,
            StickyNoteUiSnapshot snapshot, long sequence)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return new StickyUiEvent(kind, snapshot.NoteId, snapshot,
                snapshot.Visible, sequence);
        }

        internal static StickyUiEvent ReminderRequest(StickyUiEventKind kind,
            string noteId, ReminderItem reminder, long sequence)
        {
            return new StickyUiEvent(kind, noteId, null, false, sequence,
                reminder);
        }

        internal static StickyUiEvent HorizontalResize(
            StickyNoteUiSnapshot snapshot, long sequence, int left, int width)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return new StickyUiEvent(
                StickyUiEventKind.DockHorizontalResizing, snapshot.NoteId,
                snapshot, false, sequence, null, left, width);
        }

        internal StickyUiEventKind Kind { get; private set; }
        internal string NoteId { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
        internal bool Flag { get; private set; }
        internal long Sequence { get; private set; }
        internal ReminderItem Reminder { get; private set; }
        internal int Left { get; private set; }
        internal int Width { get; private set; }
    }

    internal enum StickyUiCommandStatus
    {
        Handled,
        NotHandled,
        NotAccepted,
        Failed
    }

    internal sealed class StickyUiFinalSnapshot
    {
        internal StickyUiFinalSnapshot(StickyNoteUiSnapshot snapshot,
            long sequence)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            NoteId = snapshot.NoteId;
            Snapshot = snapshot;
            Sequence = sequence;
        }

        internal string NoteId { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
        internal long Sequence { get; private set; }
    }

    internal sealed class StickyUiCommandResult
    {
        private StickyUiCommandResult(StickyUiCommandStatus status,
            string error, StickyNoteUiSnapshot snapshot, long sequence,
            StickyUiFinalSnapshot[] finalSnapshots, int ownerThreadId)
        {
            Status = status;
            Error = error ?? String.Empty;
            Snapshot = snapshot;
            Sequence = sequence;
            FinalSnapshots = finalSnapshots;
            OwnerThreadId = ownerThreadId;
        }

        internal StickyUiCommandStatus Status { get; private set; }
        internal string Error { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
        internal long Sequence { get; private set; }
        internal StickyUiFinalSnapshot[] FinalSnapshots { get; private set; }
        internal int OwnerThreadId { get; private set; }

        internal static StickyUiCommandResult Handled()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Handled,
                String.Empty, null, 0, null, ThreadingThreadId());
        }

        internal static StickyUiCommandResult Handled(
            StickyNoteUiSnapshot snapshot, long sequence)
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Handled,
                String.Empty, snapshot, sequence, null, ThreadingThreadId());
        }

        internal static StickyUiCommandResult Handled(
            StickyUiFinalSnapshot[] finalSnapshots)
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Handled,
                String.Empty, null, 0, finalSnapshots, ThreadingThreadId());
        }

        internal static StickyUiCommandResult NotHandled()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.NotHandled,
                String.Empty, null, 0, null, ThreadingThreadId());
        }

        internal static StickyUiCommandResult NotAccepted()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.NotAccepted,
                String.Empty, null, 0, null, ThreadingThreadId());
        }

        internal static StickyUiCommandResult Failed(Exception error)
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Failed,
                error == null ? String.Empty : error.Message, null, 0,
                null, ThreadingThreadId());
        }

        private static int ThreadingThreadId()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId;
        }
    }
}
