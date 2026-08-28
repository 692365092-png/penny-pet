using System;

namespace PennyPet
{
    internal enum StickyUiCommandKind
    {
        Create,
        Show,
        Hide,
        FocusPrimaryInput,
        SetTopMost,
        Close,
        CloseAll
    }

    internal sealed class StickyUiCommand
    {
        internal StickyUiCommand(StickyUiCommandKind kind, string noteId,
            bool flag, StickyNoteUiSnapshot snapshot = null)
        {
            Kind = kind;
            NoteId = noteId ?? String.Empty;
            Flag = flag;
            Snapshot = snapshot;
        }

        internal StickyUiCommandKind Kind { get; private set; }
        internal string NoteId { get; private set; }
        internal bool Flag { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
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
            X = source.X;
            Y = source.Y;
            Width = source.Width;
            Height = source.Height;
            CreatedUtcTicks = source.CreatedUtcTicks;
            ModifiedUtcTicks = source.ModifiedUtcTicks;
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
        internal int X { get; private set; }
        internal int Y { get; private set; }
        internal int Width { get; private set; }
        internal int Height { get; private set; }
        internal long CreatedUtcTicks { get; private set; }
        internal long ModifiedUtcTicks { get; private set; }

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
            target.X = X;
            target.Y = Y;
            target.Width = Width;
            target.Height = Height;
            target.CreatedUtcTicks = CreatedUtcTicks;
            target.ModifiedUtcTicks = ModifiedUtcTicks;
        }
    }

    internal enum StickyUiEventKind
    {
        SnapshotChanged,
        TypingActivity,
        InputFocusChanged,
        ImeCompositionChanged,
        DeleteRequested,
        NewNoteRequested,
        NewTodoRequested,
        NewScheduleRequested,
        Closed
    }

    internal sealed class StickyUiEvent
    {
        internal StickyUiEvent(StickyUiEventKind kind, string noteId,
            StickyNoteUiSnapshot snapshot, bool flag, long sequence)
        {
            Kind = kind;
            NoteId = noteId ?? String.Empty;
            Snapshot = snapshot;
            Flag = flag;
            Sequence = sequence;
        }

        internal StickyUiEventKind Kind { get; private set; }
        internal string NoteId { get; private set; }
        internal StickyNoteUiSnapshot Snapshot { get; private set; }
        internal bool Flag { get; private set; }
        internal long Sequence { get; private set; }
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
