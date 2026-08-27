using System;

namespace PennyPet
{
    // OLE DoDragDrop runs a nested message loop. A drop is accepted during
    // that loop but committed only after the source returns from DoDragDrop.
    // The source is an opaque identity token so Core does not depend on Forms.
    internal sealed class StickyTabDropSession
    {
        private StickyNoteData _activeNote;
        private object _source;
        private Action _pendingCommit;

        internal void Begin(StickyNoteData note, object source)
        {
            _activeNote = note;
            _source = source;
            _pendingCommit = null;
        }

        internal StickyNoteData ActiveNote(string id)
        {
            if (_activeNote == null || String.IsNullOrEmpty(id) ||
                !String.Equals(_activeNote.Id, id,
                    StringComparison.OrdinalIgnoreCase)) return null;
            return _activeNote;
        }

        internal StickyNoteData CurrentNote
        {
            get { return _activeNote; }
        }

        internal bool IsSource(object source)
        {
            return source != null && Object.ReferenceEquals(_source, source);
        }

        internal object Source
        {
            get { return _source; }
        }

        internal bool QueueCommit(StickyNoteData note, Action commit)
        {
            if (!Object.ReferenceEquals(_activeNote, note) || commit == null)
                return false;
            _pendingCommit = commit;
            return true;
        }

        internal bool Complete(StickyNoteData note)
        {
            if (!Object.ReferenceEquals(_activeNote, note)) return false;
            Action commit = _pendingCommit;
            _pendingCommit = null;
            _activeNote = null;
            _source = null;
            if (commit != null) commit();
            return true;
        }
    }
}
