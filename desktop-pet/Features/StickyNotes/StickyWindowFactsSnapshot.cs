using System;

namespace PennyPet
{
    // Detached geometry facts for one sticky window, carried by a typed event
    // from the Sticky STA to the Pet runtime. Immutable by construction.
    internal sealed class StickyWindowFactsSnapshot
    {
        internal StickyWindowFactsSnapshot(string noteId, WindowFacts facts,
            bool visible, bool topMost)
        {
            NoteId = noteId ?? String.Empty;
            Facts = facts;
            Visible = visible;
            TopMost = topMost;
        }

        internal string NoteId { get; private set; }
        internal WindowFacts Facts { get; private set; }
        internal bool Visible { get; private set; }
        internal bool TopMost { get; private set; }
    }
}
