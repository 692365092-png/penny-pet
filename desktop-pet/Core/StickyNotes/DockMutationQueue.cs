using System;
using System.Collections.Generic;

namespace PennyPet
{
    // A finalizing gesture owns its affected groups and dependent user actions.
    // Scope is copied before membership changes; hidden slots and both sides
    // of a merge remain protected until the owner finishes or is cancelled.
    internal sealed class DockMutationQueue
    {
        private readonly HashSet<string> _members;
        private readonly HashSet<string> _groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Action> _actions = new List<Action>();
        private bool _released;

        internal DockMutationQueue(IEnumerable<string> memberIds, IEnumerable<StickyNoteData> scope)
        {
            _members = new HashSet<string>(memberIds, StringComparer.OrdinalIgnoreCase);
            Include(scope);
        }

        internal void Include(IEnumerable<StickyNoteData> scope)
        {
            if (scope == null) return;
            foreach (StickyNoteData note in scope)
            {
                _members.Add(note.Id);
                if (!String.IsNullOrEmpty(note.DockGroupId)) _groups.Add(note.DockGroupId);
            }
        }

        internal bool Contains(string noteId, string groupId)
        {
            return !_released && (noteId == null || _members.Contains(noteId) ||
                (!String.IsNullOrEmpty(groupId) && _groups.Contains(groupId)));
        }

        internal bool Defer(string noteId, string groupId, Action action)
        {
            if (!Contains(noteId, groupId)) return false;
            _actions.Add(action);
            return true;
        }

        internal Action[] Release()
        {
            _released = true;
            Action[] actions = _actions.ToArray();
            _actions.Clear();
            return actions;
        }
    }
}
