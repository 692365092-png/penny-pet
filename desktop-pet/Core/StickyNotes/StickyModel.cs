using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Pet-STA-owned memory only. Dock membership and preferred placement stay
    // on the canonical notes; persistence and HWND state have separate owners.
    internal sealed class StickyModel
    {
        private readonly List<StickyNoteData> _notes = new List<StickyNoteData>();
        private readonly IReadOnlyList<StickyNoteData> _view;

        internal StickyModel() { _view = _notes.AsReadOnly(); }
        internal int Count { get { return _notes.Count; } }
        internal bool CanCreate { get { return Count < StickyNoteLimits.MaximumNotes; } }
        internal IReadOnlyList<StickyNoteData> InStorageOrder { get { return _view; } }

        internal StickyNoteData CreateDraft(string text, int x, int y)
        {
            if (!CanCreate) return null;
            StickyNoteData note = new StickyNoteData();
            string body = text ?? String.Empty;
            note.Text = body.Length <= StickyNoteLimits.MaximumBodyCharacters
                ? body : body.Substring(0, StickyNoteLimits.MaximumBodyCharacters);
            note.X = x;
            note.Y = y;
            note.TabOrder = NextTabOrder();
            _notes.Add(note);
            return note;
        }

        public List<StickyNoteData> GetAll()
        {
            List<StickyNoteData> result = new List<StickyNoteData>(_notes);
            result.Sort(delegate(StickyNoteData left, StickyNoteData right)
            {
                return right.ModifiedUtcTicks.CompareTo(left.ModifiedUtcTicks);
            });
            return result;
        }

        public List<StickyNoteData> GetHiddenInTabOrder()
        {
            List<StickyNoteData> result = GetInTabOrder();
            result.RemoveAll(delegate(StickyNoteData note) { return note.Visible; });
            return result;
        }

        internal bool ReorderHidden(StickyNoteData moved, int destinationIndex)
        {
            if (moved == null || moved.Visible) return false;
            List<StickyNoteData> all = GetInTabOrder();
            List<StickyNoteData> hidden = new List<StickyNoteData>();
            foreach (StickyNoteData note in all)
            {
                if (!note.Visible) hidden.Add(note);
            }
            int original = hidden.IndexOf(moved);
            if (original < 0) return false;
            hidden.RemoveAt(original);
            int adjusted = destinationIndex;
            if (original < adjusted) adjusted--;
            adjusted = Math.Max(0, Math.Min(hidden.Count, adjusted));
            hidden.Insert(adjusted, moved);
            int hiddenIndex = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].Visible) all[i] = hidden[hiddenIndex++];
            }
            for (int i = 0; i < all.Count; i++) all[i].TabOrder = i;
            return true;
        }

        public StickyNoteData Find(string id)
        {
            foreach (StickyNoteData note in _notes)
            {
                if (String.Equals(note.Id, id, StringComparison.OrdinalIgnoreCase))
                    return note;
            }
            return null;
        }

        public bool Remove(StickyNoteData note)
        {
            if (note == null || !_notes.Contains(note)) return false;
            StickyDockOperations.ExtractSingleDockMember(
                StickyDockGroups.GetOrderedGroup(_notes, note), note);
            _notes.Remove(note);
            return true;
        }

        private List<StickyNoteData> GetInTabOrder()
        {
            List<StickyNoteData> result = new List<StickyNoteData>(_notes);
            result.Sort(delegate(StickyNoteData left, StickyNoteData right)
            {
                int order = left.TabOrder.CompareTo(right.TabOrder);
                if (order != 0) return order;
                return left.CreatedUtcTicks.CompareTo(right.CreatedUtcTicks);
            });
            return result;
        }

        private int NextTabOrder()
        {
            int maximum = -1;
            foreach (StickyNoteData note in _notes)
                maximum = Math.Max(maximum, note.TabOrder);
            return maximum + 1;
        }

        internal void NormalizeTabOrders()
        {
            List<StickyNoteData> ordered = GetInTabOrder();
            for (int i = 0; i < ordered.Count; i++) ordered[i].TabOrder = i;
        }
        // Load and dataset replacement run on the owner before UI publication.
        internal void AddLoaded(StickyNoteData note) { _notes.Add(note); }
        internal void Clear() { _notes.Clear(); }
        internal void ReplaceWith(IEnumerable<StickyNoteData> notes)
        {
            List<StickyNoteData> replacement = new List<StickyNoteData>(notes);
            _notes.Clear();
            _notes.AddRange(replacement);
        }

        internal List<StickyNoteData> CaptureSnapshot()
        {
            return CloneSnapshot(_notes);
        }

        internal static List<StickyNoteData> CloneSnapshot(IEnumerable<StickyNoteData> notes)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null) result.Add(note.CloneForPersistence());
            return result;
        }
    }
}
