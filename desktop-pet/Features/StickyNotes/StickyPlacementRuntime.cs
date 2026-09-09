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

        // Read-only acceptance preflight. It mirrors the monotonic rule of
        // TryUpdateEffective without mutating runtime state, so geometry
        // consumers can validate a whole set before writing anything.
        internal bool CanAcceptEffective(
            string noteId,
            WindowFacts facts)
        {
            if (String.IsNullOrWhiteSpace(noteId) ||
                facts == null)
                return false;

            WindowFacts current = GetEffective(noteId);
            if (current == null) return true;

            if (facts.TopologyGeneration <
                current.TopologyGeneration)
                return false;

            if (facts.TopologyGeneration ==
                    current.TopologyGeneration &&
                facts.WindowSequence <=
                    current.WindowSequence)
                return false;

            return true;
        }

        internal bool TryUpdateEffective(string noteId, WindowFacts facts)
        {
            if (!CanAcceptEffective(noteId, facts)) return false;
            GetOrCreateState(noteId).Effective = facts;
            return true;
        }

        // WindowSequence is monotonic only within one StickyWindowSession.
        // When that HWND/session is known to be gone, invalidate its Effective
        // watermark without erasing temporary-rehome intent bookkeeping.
        internal bool InvalidateEffective(
            string noteId)
        {
            if (String.IsNullOrWhiteSpace(noteId))
                return false;

            NotePlacementState state;

            if (!_states.TryGetValue(noteId, out state) ||
                state.Effective == null)
                return false;

            state.Effective = null;

            return true;
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
            NotePlacementState state = GetOrCreateState(noteId);
            state.IsTemporaryRehome = true;
            state.UserMovedSinceRehome = false;
            state.TemporaryReason = reason ?? String.Empty;
        }

        // A user placement commit ends any temporary rehome. When the commit
        // happened during a temporary stay, the note must not be pulled back
        // when the preferred display returns.
        internal void MarkUserPlacementCommit(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            NotePlacementState state;
            if (!_states.TryGetValue(noteId, out state)) return;
            state.UserMovedSinceRehome = state.IsTemporaryRehome ||
                state.UserMovedSinceRehome;
            state.IsTemporaryRehome = false;
            state.TemporaryReason = String.Empty;
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
            state.IsTemporaryRehome = false;
            state.UserMovedSinceRehome = false;
            state.TemporaryReason = String.Empty;
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

        private NotePlacementState GetOrCreateState(string noteId)
        {
            NotePlacementState state;
            if (!_states.TryGetValue(noteId, out state))
            {
                state = new NotePlacementState();
                _states.Add(noteId, state);
            }
            return state;
        }

        // Private Pet-STA state never crosses threads. Published WindowFacts
        // remain immutable; accepting a new value need not clone this owner.
        private sealed class NotePlacementState
        {
            internal WindowFacts Effective;
            internal bool IsTemporaryRehome;
            internal bool UserMovedSinceRehome;
            internal string TemporaryReason = String.Empty;
        }
    }
}
