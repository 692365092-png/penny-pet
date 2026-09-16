using System;

namespace PennyPet
{
    // OLE DoDragDrop runs a nested message loop. A drop is accepted during
    // that loop but committed only after the source returns from DoDragDrop.
    // The source is an opaque identity token so Core does not depend on Forms.
    internal sealed class StickyTabDropSession
    {
        private string _activeNoteId;
        private object _source;
        private Action _pendingCommit;

        internal void Begin(string noteId, object source)
        {
            _activeNoteId = noteId ?? String.Empty;
            _source = source;
            _pendingCommit = null;
        }

        internal bool IsActiveNote(string noteId)
        {
            return !String.IsNullOrEmpty(_activeNoteId) &&
                !String.IsNullOrEmpty(noteId) &&
                String.Equals(_activeNoteId, noteId,
                    StringComparison.OrdinalIgnoreCase);
        }

        internal string ActiveNoteId
        {
            get { return _activeNoteId; }
        }

        internal bool IsSource(object source)
        {
            return source != null && Object.ReferenceEquals(_source, source);
        }

        internal object Source
        {
            get { return _source; }
        }

        internal bool QueueCommit(string noteId, Action commit)
        {
            if (!IsActiveNote(noteId) || commit == null)
                return false;
            _pendingCommit = commit;
            return true;
        }

        internal bool Complete(string noteId)
        {
            if (!IsActiveNote(noteId)) return false;
            Action commit = _pendingCommit;
            _pendingCommit = null;
            _activeNoteId = null;
            _source = null;
            if (commit != null) commit();
            return true;
        }
    }
}
