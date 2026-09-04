using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Runtime-only placement state kept separate from the repository: the
    // latest effective WindowFacts per hosted note live here in memory, while
    // the durable preferred placement lives on StickyNoteData and persists.
    // No HWND, WPF object or repository reference may enter this class.
    internal sealed class StickyPlacementRuntime
    {
        private readonly Dictionary<string, NotePlacementState> _states =
            new Dictionary<string, NotePlacementState>(
                StringComparer.OrdinalIgnoreCase);

        internal WindowFacts GetEffective(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return null;
            NotePlacementState state;
            return _states.TryGetValue(noteId, out state)
                ? state.Effective : null;
        }

        internal void UpdateEffective(string noteId, WindowFacts facts)
        {
            if (String.IsNullOrEmpty(noteId) || facts == null) return;
            NotePlacementState state;
            if (!_states.TryGetValue(noteId, out state))
            {
                _states[noteId] = new NotePlacementState(facts, false,
                    false, String.Empty);
                return;
            }
            _states[noteId] = new NotePlacementState(facts,
                state.IsTemporaryRehome, state.UserMovedSinceRehome,
                state.TemporaryReason);
        }

        internal bool IsTemporaryRehome(string noteId)
        {
            NotePlacementState state;
            return !String.IsNullOrEmpty(noteId) &&
                _states.TryGetValue(noteId, out state) &&
                state.IsTemporaryRehome;
        }

        internal bool UserMovedSinceRehome(string noteId)
        {
            NotePlacementState state;
            return !String.IsNullOrEmpty(noteId) &&
                _states.TryGetValue(noteId, out state) &&
                state.UserMovedSinceRehome;
        }

        internal string TemporaryReason(string noteId)
        {
            NotePlacementState state;
            return !String.IsNullOrEmpty(noteId) &&
                _states.TryGetValue(noteId, out state)
                    ? state.TemporaryReason : String.Empty;
        }

        // A new temporary rehome starts a fresh intent window: the user has
        // not moved away from the fallback yet.
        internal void MarkTemporaryRehome(string noteId, string reason)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            NotePlacementState state;
            _states.TryGetValue(noteId, out state);
            _states[noteId] = new NotePlacementState(
                state == null ? null : state.Effective,
                true, false, reason ?? String.Empty);
        }

        // A user placement commit ends any temporary rehome. When the commit
        // happened during a temporary stay, the note must not be pulled back
        // when the preferred display returns.
        internal void MarkUserPlacementCommit(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            NotePlacementState state;
            if (!_states.TryGetValue(noteId, out state)) return;
            bool userMoved = state.IsTemporaryRehome ||
                state.UserMovedSinceRehome;
            _states[noteId] = new NotePlacementState(state.Effective,
                false, userMoved, String.Empty);
        }

        internal void MarkReturnedToPreferred(string noteId)
        {
            ClearTemporaryRehome(noteId);
        }

        internal void ClearTemporaryRehome(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            NotePlacementState state;
            if (!_states.TryGetValue(noteId, out state)) return;
            _states[noteId] = new NotePlacementState(state.Effective,
                false, false, String.Empty);
        }

        internal void Remove(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            _states.Remove(noteId);
        }

        internal void Clear()
        {
            _states.Clear();
        }

        internal int Count { get { return _states.Count; } }

        private sealed class NotePlacementState
        {
            internal NotePlacementState(WindowFacts effective,
                bool isTemporaryRehome, bool userMovedSinceRehome,
                string temporaryReason)
            {
                Effective = effective;
                IsTemporaryRehome = isTemporaryRehome;
                UserMovedSinceRehome = userMovedSinceRehome;
                TemporaryReason = temporaryReason ?? String.Empty;
            }

            internal WindowFacts Effective { get; private set; }
            internal bool IsTemporaryRehome { get; private set; }
            internal bool UserMovedSinceRehome { get; private set; }
            internal string TemporaryReason { get; private set; }
        }
    }
}
