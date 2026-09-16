using System;
using System.Collections.Generic;
using System.Text;

namespace PennyPet
{
    internal static class ShortItemText
    {
        // A full-width character occupies roughly twice the horizontal space of
        // a Latin character. One shared budget gives reminders and todo items
        // a practical limit of about 50 Chinese or 100 English characters.
        public const int MaximumDisplayUnits = 100;
        public const int MaximumInputCharacters = 100;

        public static int MeasureDisplayUnits(string value)
        {
            int units = 0;
            string text = value ?? String.Empty;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (Char.IsHighSurrogate(character) &&
                    index + 1 < text.Length &&
                    Char.IsLowSurrogate(text[index + 1]))
                {
                    units += 2;
                    index++;
                    continue;
                }
                units += IsFullWidth(character) ? 2 : 1;
            }
            return units;
        }

        public static bool Fits(string value)
        {
            return MeasureDisplayUnits(value) <= MaximumDisplayUnits;
        }

        public static string NormalizeAndTruncate(string value)
        {
            string text = Normalize(value);
            int units = 0;
            int length = 0;
            while (length < text.Length)
            {
                char character = text[length];
                int characterLength = 1;
                int nextUnits;
                if (Char.IsHighSurrogate(character) &&
                    length + 1 < text.Length &&
                    Char.IsLowSurrogate(text[length + 1]))
                {
                    characterLength = 2;
                    nextUnits = 2;
                }
                else nextUnits = IsFullWidth(character) ? 2 : 1;
                if (units + nextUnits > MaximumDisplayUnits) break;
                units += nextUnits;
                length += characterLength;
            }
            return text.Substring(0, length).Trim();
        }

        public static string Normalize(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            StringBuilder result = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char character in value)
            {
                if (Char.IsWhiteSpace(character))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }
                if (pendingSpace) result.Append(' ');
                result.Append(character);
                pendingSpace = false;
            }
            return result.ToString().Trim();
        }

        private static bool IsFullWidth(char character)
        {
            int value = character;
            return (value >= 0x1100 && value <= 0x11FF) ||
                (value >= 0x2E80 && value <= 0xA4CF) ||
                (value >= 0xAC00 && value <= 0xD7AF) ||
                (value >= 0xF900 && value <= 0xFAFF) ||
                (value >= 0xFE10 && value <= 0xFE6F) ||
                (value >= 0xFF01 && value <= 0xFF60) ||
                (value >= 0xFFE0 && value <= 0xFFE6);
        }
    }

    internal sealed class ReminderItem
    {
        internal const int MaximumTextCharacters =
            ShortItemText.MaximumInputCharacters;
        private const int MaximumSourceNoteIdCharacters = 100;

        public ReminderItem(DateTime deadlineUtc, string text)
            : this(deadlineUtc, text, null)
        {
        }

        public ReminderItem(DateTime deadlineUtc, string text,
            string sourceNoteId)
            : this(deadlineUtc, text, sourceNoteId, 10.5F, false)
        {
        }

        public ReminderItem(DateTime deadlineUtc, string text,
            string sourceNoteId, float fontSizePoints, bool preAlertEnabled)
        {
            DeadlineUtc = deadlineUtc.Kind == DateTimeKind.Utc
                ? deadlineUtc : deadlineUtc.ToUniversalTime();
            Text = ShortItemText.NormalizeAndTruncate(text);
            string noteId = sourceNoteId ?? String.Empty;
            SourceNoteId = noteId.Length <= MaximumSourceNoteIdCharacters
                ? noteId : noteId.Substring(0, MaximumSourceNoteIdCharacters);
            FontSizeTwips = Math.Max(120, Math.Min(1440,
                (int)Math.Round(fontSizePoints * 20F)));
            PreAlertEnabled = preAlertEnabled;
        }

        public DateTime DeadlineUtc { get; private set; }
        public string Text { get; private set; }
        public string SourceNoteId { get; private set; }
        public int FontSizeTwips { get; private set; }
        public bool PreAlertEnabled { get; private set; }

        public TimeSpan Remaining
        {
            get { return DeadlineUtc - DateTime.UtcNow; }
        }

        public override string ToString()
        {
            return Text ?? String.Empty;
        }
    }

    internal sealed class ReminderSchedule
    {
        public const int MaximumItems = 5;
        private readonly List<ReminderItem> _items =
            new List<ReminderItem>();

        public int Count { get { return _items.Count; } }
        public bool Active { get { return _items.Count > 0; } }
        public DateTime DeadlineUtc
        {
            get { return Next == null ? DateTime.MinValue : Next.DeadlineUtc; }
        }
        public string Text
        {
            get { return Next == null ? String.Empty : Next.Text; }
        }
        public TimeSpan Remaining
        {
            get { return Next == null ? TimeSpan.Zero : Next.Remaining; }
        }
        public ReminderItem Next
        {
            get
            {
                ReminderItem result = null;
                foreach (ReminderItem item in _items)
                {
                    if (result == null ||
                        item.DeadlineUtc < result.DeadlineUtc)
                        result = item;
                }
                return result;
            }
        }

        public ReminderItem NextPreAlert
        {
            get
            {
                ReminderItem result = null;
                foreach (ReminderItem item in _items)
                {
                    if (!item.PreAlertEnabled) continue;
                    if (result == null ||
                        item.DeadlineUtc < result.DeadlineUtc)
                        result = item;
                }
                return result;
            }
        }

        public ReminderItem Set(TimeSpan delay, string text)
        {
            if (delay <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay));
            return Add(DateTime.UtcNow.Add(delay), text);
        }

        public ReminderItem Add(DateTime deadlineUtc, string text)
        {
            return Add(deadlineUtc, text, null);
        }

        public ReminderItem Add(DateTime deadlineUtc, string text,
            string sourceNoteId)
        {
            return Add(deadlineUtc, text, sourceNoteId, 10.5F, false);
        }

        public ReminderItem Add(DateTime deadlineUtc, string text,
            string sourceNoteId, float fontSizePoints, bool preAlertEnabled)
        {
            if (_items.Count >= MaximumItems)
                throw new InvalidOperationException(
                    "最多只能设置五条提醒。");
            DateTime utc = deadlineUtc.Kind == DateTimeKind.Utc
                ? deadlineUtc : deadlineUtc.ToUniversalTime();
            if (utc <= DateTime.UtcNow)
                throw new ArgumentOutOfRangeException(nameof(deadlineUtc));
            ReminderItem item = new ReminderItem(utc, text, sourceNoteId,
                fontSizePoints, preAlertEnabled);
            _items.Add(item);
            return item;
        }

        public ReminderItem Replace(ReminderItem existing,
            DateTime deadlineUtc, string text, float fontSizePoints,
            bool preAlertEnabled)
        {
            int index = existing == null ? -1 : _items.IndexOf(existing);
            if (index < 0)
                throw new ArgumentException(
                    "Reminder is no longer active.", nameof(existing));
            DateTime utc = deadlineUtc.Kind == DateTimeKind.Utc
                ? deadlineUtc : deadlineUtc.ToUniversalTime();
            if (utc <= DateTime.UtcNow)
                throw new ArgumentOutOfRangeException(nameof(deadlineUtc));
            ReminderItem replacement = new ReminderItem(utc, text,
                existing.SourceNoteId, fontSizePoints, preAlertEnabled);
            _items[index] = replacement;
            return replacement;
        }

        public void Restore(IEnumerable<ReminderItem> items)
        {
            _items.Clear();
            if (items == null) return;
            foreach (ReminderItem item in items)
            {
                if (item == null || _items.Count >= MaximumItems) continue;
                _items.Add(new ReminderItem(item.DeadlineUtc, item.Text,
                    item.SourceNoteId, item.FontSizeTwips / 20F,
                    item.PreAlertEnabled));
            }
        }

        public List<ReminderItem> GetItems()
        {
            List<ReminderItem> result = new List<ReminderItem>(_items);
            result.Sort(delegate(ReminderItem left, ReminderItem right)
            {
                return left.DeadlineUtc.CompareTo(right.DeadlineUtc);
            });
            return result;
        }

        public ReminderItem FirstDue(DateTime utcNow)
        {
            ReminderItem next = Next;
            return next != null && next.DeadlineUtc <= utcNow ? next : null;
        }

        public bool Remove(ReminderItem item)
        {
            return item != null && _items.Remove(item);
        }

        public ReminderItem FindBySourceNoteId(string sourceNoteId)
        {
            if (String.IsNullOrEmpty(sourceNoteId)) return null;
            ReminderItem result = null;
            foreach (ReminderItem item in _items)
            {
                if (String.Equals(item.SourceNoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase) &&
                    (result == null ||
                    item.DeadlineUtc < result.DeadlineUtc))
                    result = item;
            }
            return result;
        }

        public int RemoveBySourceNoteId(string sourceNoteId)
        {
            if (String.IsNullOrEmpty(sourceNoteId)) return 0;
            int removed = 0;
            for (int index = _items.Count - 1; index >= 0; index--)
            {
                if (!String.Equals(_items[index].SourceNoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                _items.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        public int RemoveLinkedNotesNotIn(ISet<string> existingNoteIds)
        {
            int removed = 0;
            for (int index = _items.Count - 1; index >= 0; index--)
            {
                string sourceNoteId = _items[index].SourceNoteId;
                if (String.IsNullOrEmpty(sourceNoteId) ||
                    (existingNoteIds != null &&
                    existingNoteIds.Contains(sourceNoteId))) continue;
                _items.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        public void Cancel()
        {
            _items.Clear();
        }
    }
}
