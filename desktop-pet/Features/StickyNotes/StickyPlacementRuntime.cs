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
        private readonly Dictionary<string, WindowFacts> _effective =
            new Dictionary<string, WindowFacts>(
                StringComparer.OrdinalIgnoreCase);

        internal WindowFacts GetEffective(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return null;
            WindowFacts facts;
            return _effective.TryGetValue(noteId, out facts)
                ? facts : null;
        }

        internal void UpdateEffective(string noteId, WindowFacts facts)
        {
            if (String.IsNullOrEmpty(noteId) || facts == null) return;
            _effective[noteId] = facts;
        }

        internal void Remove(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            _effective.Remove(noteId);
        }

        internal void Clear()
        {
            _effective.Clear();
        }

        internal int Count { get { return _effective.Count; } }
    }
}
