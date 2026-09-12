using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Pet-STA owner of accepted actual geometry and temporary-rehome intent.
    // A capture topology belongs to its facts; newer monitor origins must
    // never reinterpret older physical coordinates. Preferred stays durable.
    internal sealed class StickyPlacementRuntime
    {
        private readonly Dictionary<string, NotePlacementState> _states =
            new Dictionary<string, NotePlacementState>(StringComparer.OrdinalIgnoreCase);

        internal WindowFacts GetEffective(string noteId)
        {
            NotePlacementState state = Find(noteId);
            return state == null ? null : state.Effective;
        }

        internal bool CanAcceptEffective(string noteId, WindowFacts facts)
        {
            if (String.IsNullOrWhiteSpace(noteId) || facts == null) return false;
            WindowFacts current = GetEffective(noteId);
            return current == null || facts.TopologyGeneration > current.TopologyGeneration ||
                (facts.TopologyGeneration == current.TopologyGeneration &&
                    facts.WindowSequence > current.WindowSequence);
        }

        internal bool TryUpdateEffective(string noteId, WindowFacts facts,
            DisplayTopologySnapshot capturedTopology = null)
        {
            if (!CanAcceptEffective(noteId, facts)) return false;
            NotePlacementState state = GetOrCreate(noteId);
            state.Effective = facts;
            state.CapturedTopology = capturedTopology;
            return true;
        }

        internal bool TryGetEffectiveLogical(string noteId, out LogicalRect logical)
        {
            logical = new LogicalRect();
            NotePlacementState state = Find(noteId);
            return state != null && StickyPlacementRules.TryGetLogicalFacts(
                state.Effective, state.CapturedTopology, out logical);
        }

        // Sequence belongs to one HWND/session. Losing that session clears
        // its complete capture frame, but never the user's rehome intent.
        internal bool InvalidateEffective(string noteId)
        {
            NotePlacementState state = Find(noteId);
            if (state == null || state.Effective == null) return false;
            state.Effective = null;
            state.CapturedTopology = null;
            return true;
        }

        internal bool IsTemporaryRehome(string noteId)
        {
            NotePlacementState state = Find(noteId);
            return state != null && state.IsTemporaryRehome;
        }

        internal bool UserMovedSinceRehome(string noteId)
        {
            NotePlacementState state = Find(noteId);
            return state != null && state.UserMovedSinceRehome;
        }

        internal string TemporaryReason(string noteId)
        {
            NotePlacementState state = Find(noteId);
            return state == null ? String.Empty : state.TemporaryReason;
        }

        internal void MarkTemporaryRehome(string noteId, string reason)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            NotePlacementState state = GetOrCreate(noteId);
            state.IsTemporaryRehome = true;
            state.UserMovedSinceRehome = false;
            state.TemporaryReason = reason ?? String.Empty;
        }

        internal void MarkUserPlacementCommit(string noteId)
        {
            NotePlacementState state = Find(noteId);
            if (state == null) return;
            state.UserMovedSinceRehome = state.IsTemporaryRehome || state.UserMovedSinceRehome;
            state.IsTemporaryRehome = false;
            state.TemporaryReason = String.Empty;
        }

        internal void MarkReturnedToPreferred(string noteId)
        {
            ClearTemporaryRehome(noteId);
        }

        internal void ClearTemporaryRehome(string noteId)
        {
            NotePlacementState state = Find(noteId);
            if (state == null) return;
            state.IsTemporaryRehome = false;
            state.UserMovedSinceRehome = false;
            state.TemporaryReason = String.Empty;
        }

        internal void Remove(string noteId)
        {
            if (!String.IsNullOrEmpty(noteId)) _states.Remove(noteId);
        }

        internal void Clear() { _states.Clear(); }
        internal int Count { get { return _states.Count; } }

        private NotePlacementState Find(string noteId)
        {
            NotePlacementState state;
            return !String.IsNullOrEmpty(noteId) && _states.TryGetValue(noteId, out state)
                ? state : null;
        }

        private NotePlacementState GetOrCreate(string noteId)
        {
            NotePlacementState state = Find(noteId);
            if (state == null) _states.Add(noteId, state = new NotePlacementState());
            return state;
        }

        private sealed class NotePlacementState
        {
            internal WindowFacts Effective;
            internal DisplayTopologySnapshot CapturedTopology;
            internal bool IsTemporaryRehome;
            internal bool UserMovedSinceRehome;
            internal string TemporaryReason = String.Empty;
        }
    }
}
