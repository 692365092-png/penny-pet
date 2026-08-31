using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Pet-thread-only hosted membership and transient protocol state.
    internal sealed class StickyHostedRuntime
    {
        private readonly HashSet<string> _noteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _appliedSequences =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _imeComposing =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _inputFocused =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deletePending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal int NoteCount { get { return _noteIds.Count; } }
        internal bool HasImeComposition { get { return _imeComposing.Count > 0; } }
        internal bool HasInputFocus { get { return _inputFocused.Count > 0; } }
        internal bool ExitRequested { get; private set; }
        internal bool CloseAllInFlight { get; private set; }
        internal bool ExitPrepared { get; private set; }

        internal bool AddNote(string noteId)
        {
            if (!_noteIds.Add(noteId ?? String.Empty)) return false;
            _appliedSequences[noteId ?? String.Empty] = 0;
            return true;
        }

        internal bool ContainsNote(string noteId)
        {
            return _noteIds.Contains(noteId ?? String.Empty);
        }

        internal bool CanApplySequence(string noteId, long sequence)
        {
            string id = noteId ?? String.Empty;
            long applied;
            _appliedSequences.TryGetValue(id, out applied);
            return _noteIds.Contains(id) && sequence > applied;
        }

        internal void RecordSequence(string noteId, long sequence)
        {
            _appliedSequences[noteId ?? String.Empty] = sequence;
        }

        internal void SetImeComposition(string noteId, bool active)
        {
            SetMembership(_imeComposing, noteId, active);
        }

        internal void SetInputFocus(string noteId, bool focused)
        {
            SetMembership(_inputFocused, noteId, focused);
        }

        internal bool TryBeginDelete(string noteId)
        {
            return _deletePending.Add(noteId ?? String.Empty);
        }

        internal void EndDelete(string noteId)
        {
            _deletePending.Remove(noteId ?? String.Empty);
        }

        internal void RemoveNote(string noteId)
        {
            string id = noteId ?? String.Empty;
            _noteIds.Remove(id);
            _appliedSequences.Remove(id);
            _imeComposing.Remove(id);
            _inputFocused.Remove(id);
            _deletePending.Remove(id);
        }

        internal void RequestExit()
        {
            ExitRequested = true;
        }

        internal bool TryBeginCloseAll()
        {
            if (!ExitRequested || CloseAllInFlight || HasImeComposition)
                return false;
            CloseAllInFlight = true;
            return true;
        }

        internal void EndCloseAll()
        {
            CloseAllInFlight = false;
        }

        internal void CancelExit()
        {
            ExitRequested = false;
        }

        internal void PrepareExit()
        {
            _imeComposing.Clear();
            _inputFocused.Clear();
            ExitPrepared = true;
        }

        private static void SetMembership(HashSet<string> values,
            string noteId, bool present)
        {
            string id = noteId ?? String.Empty;
            if (present) values.Add(id);
            else values.Remove(id);
        }
    }
}
