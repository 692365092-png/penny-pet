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

    // Immutable command DTO for the future WinForms pet thread -> dedicated
    // WPF sticky UI thread boundary. It intentionally carries only data.
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
}
