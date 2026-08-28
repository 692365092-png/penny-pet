using System;

namespace PennyPet
{
    internal enum StickyUiCommandKind
    {
        Show,
        Hide,
        FocusPrimaryInput,
        SetTopMost,
        Close
    }

    internal sealed class StickyUiCommand
    {
        internal StickyUiCommand(StickyUiCommandKind kind, string noteId,
            bool flag)
        {
            Kind = kind;
            NoteId = noteId ?? String.Empty;
            Flag = flag;
        }

        internal StickyUiCommandKind Kind { get; private set; }
        internal string NoteId { get; private set; }
        internal bool Flag { get; private set; }
    }

    internal enum StickyUiCommandStatus
    {
        Handled,
        NotHandled,
        NotAccepted,
        Failed
    }

    internal sealed class StickyUiCommandResult
    {
        private StickyUiCommandResult(StickyUiCommandStatus status,
            string error)
        {
            Status = status;
            Error = error ?? String.Empty;
        }

        internal StickyUiCommandStatus Status { get; private set; }
        internal string Error { get; private set; }

        internal static StickyUiCommandResult Handled()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Handled,
                String.Empty);
        }

        internal static StickyUiCommandResult NotHandled()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.NotHandled,
                String.Empty);
        }

        internal static StickyUiCommandResult NotAccepted()
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.NotAccepted,
                String.Empty);
        }

        internal static StickyUiCommandResult Failed(Exception error)
        {
            return new StickyUiCommandResult(StickyUiCommandStatus.Failed,
                error == null ? String.Empty : error.Message);
        }
    }
}
