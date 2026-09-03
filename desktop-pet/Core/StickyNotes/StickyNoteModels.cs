using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PennyPet
{
    // Platform-neutral note, Todo, Schedule and persisted Dock relationship
    // models. Colors are stored as raw ARGB values so this layer has no
    // desktop drawing-library dependency.
    internal static class StickyNoteLimits
    {
        // Each visible note owns a native window and several child controls.  Keep
        // generous user-facing limits while preventing accidental or hostile
        // input from allocating an unbounded number of handles and list rows.
        public const int MaximumNotes = 100;
        // These limits are intentionally well above normal note sizes. WPF's
        // RichTextBox has no reliable IME-safe MaxLength; keeping the persisted
        // limits generous prevents text that was visible in the editor from
        // being silently truncated during save.
        public const int MaximumBodyCharacters = 4000000;
        public const int MaximumRichTextCharacters = 16000000;
        public const int MaximumTodoItemsPerNote = 500;
        public const int MaximumScheduleItemsPerNote = 200;
        public const int MaximumTodoItemCharacters =
            ShortItemText.MaximumInputCharacters;
        public const int MaximumTitleCharacters = 50;
        public const long MaximumDataFileBytes = 32L * 1024L * 1024L;
        public const int MinimumWindowWidth = 280;
        public const int MaximumWindowWidth = 900;
        public const int MinimumWindowHeight = 220;
        public const int MaximumWindowHeight = 700;
    }

    internal static class StickyNoteWindowRules
    {
        internal static bool ShouldKeepSideTabsTopMost(
            bool overlapsVisibleSticky)
        {
            return !overlapsVisibleSticky;
        }
    }

    internal enum StickyTodoState
    {
        Pending = 0,
        Completed = 1,
        InProgress = 2
    }

    internal sealed class StickyTodoItem
    {
        public StickyTodoItem(string text, bool completed)
            : this(text, completed, false)
        {
        }

        public StickyTodoItem(string text, bool completed, bool isPinned)
            : this(text, completed ? StickyTodoState.Completed :
                StickyTodoState.Pending, isPinned)
        {
        }

        public StickyTodoItem(string text, StickyTodoState state,
            bool isPinned = false)
        {
            Text = ShortItemText.NormalizeAndTruncate(text);
            State = state;
            IsPinned = isPinned;
        }

        internal StickyTodoItem CloneForPersistence()
        {
            return new StickyTodoItem(Text ?? String.Empty, State, IsPinned);
        }

        public string Text;
        public StickyTodoState State;
        public bool IsPinned;

        public bool Completed
        {
            get { return State == StickyTodoState.Completed; }
            set { State = value ? StickyTodoState.Completed :
                StickyTodoState.Pending; }
        }
    }

    internal sealed class StickyScheduleItem
    {
        public StickyScheduleItem(string text, DateTime targetDate)
            : this(text, targetDate, false)
        {
        }

        public StickyScheduleItem(string text, DateTime targetDate,
            bool isPinned)
        {
            Text = ShortItemText.NormalizeAndTruncate(text);
            TargetDateTicks = targetDate.Date.Ticks;
            IsPinned = isPinned;
        }

        public string Text;
        public long TargetDateTicks;
        public bool IsPinned;

        public DateTime TargetDate
        {
            get
            {
                try { return new DateTime(TargetDateTicks).Date; }
                catch { return DateTime.Today; }
            }
        }

        internal StickyScheduleItem CloneForPersistence()
        {
            return new StickyScheduleItem(Text ?? String.Empty,
                new DateTime(TargetDateTicks).Date, IsPinned);
        }
    }

    internal sealed class ReminderActionEventArgs : EventArgs
    {
        public ReminderActionEventArgs(ReminderItem reminder)
        {
            Reminder = reminder;
        }

        public ReminderItem Reminder { get; private set; }
    }

    internal sealed class StickyNoteData
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = String.Empty;
        public string Text = String.Empty;
        // RTF is stored separately from Text so old notes, search, reminders and
        // todo conversion continue to use safe plain text.
        public string RichTextRtf = String.Empty;
        // Uniform formatting used by todo mode and when converting between
        // ordinary text and a checklist. Rich text keeps per-range styles.
        public string FontFamilyName = "Microsoft YaHei UI";
        public int FontSizeTwips = 210;
        public bool IsTodoList;
        public readonly List<StickyTodoItem> TodoItems = new List<StickyTodoItem>();
        public bool IsSchedule;
        public readonly List<StickyScheduleItem> ScheduleItems =
            new List<StickyScheduleItem>();
        public int ColorArgb = unchecked((int)0xFFFFEF9C);
        public int BackgroundOpacityPercent = 100;
        public int TextColorArgb = unchecked((int)0xFF000000);
        public bool Visible = true;
        public bool AlwaysOnTop = true;
        public int X;
        public int Y;
        public int Width = 280;
        public int Height = 230;
        public string DisplayId = String.Empty;
        public int LocalLogicalX;
        public int LocalLogicalY;
        public int LocalLogicalWidth;
        public int LocalLogicalHeight;
        // Optional parent relationship for vertically docked notes.  Keeping
        // this in the data model (rather than merging windows) lets every note
        // retain its own editor, reminder state and transparency settings.
        public string DockParentId = String.Empty;
        // A docked stack is persisted as an explicit group and order.  The
        // parent link remains useful for live insertion/splitting, but it is
        // no longer the only source of truth when a hidden stack is restored.
        public string DockGroupId = String.Empty;
        public int DockGroupOrder = -1;
        public int TabOrder = -1;
        public long CreatedUtcTicks = DateTime.UtcNow.Ticks;
        public long ModifiedUtcTicks = DateTime.UtcNow.Ticks;
        public long ReminderUtcTicks;

        public DateTime ModifiedUtc
        {
            get
            {
                try { return new DateTime(ModifiedUtcTicks, DateTimeKind.Utc); }
                catch { return DateTime.MinValue; }
            }
        }

        public DateTime? ReminderUtc
        {
            get
            {
                if (ReminderUtcTicks <= 0) return null;
                try { return new DateTime(ReminderUtcTicks, DateTimeKind.Utc); }
                catch { return null; }
            }
        }

        internal StickyNoteData CloneForPersistence()
        {
            StickyNoteData copy = new StickyNoteData();
            copy.Id = Id;
            copy.Title = Title;
            copy.Text = Text;
            copy.RichTextRtf = RichTextRtf;
            copy.FontFamilyName = FontFamilyName;
            copy.FontSizeTwips = FontSizeTwips;
            copy.IsTodoList = IsTodoList;
            copy.IsSchedule = IsSchedule;
            copy.ColorArgb = ColorArgb;
            copy.BackgroundOpacityPercent = BackgroundOpacityPercent;
            copy.TextColorArgb = TextColorArgb;
            copy.Visible = Visible;
            copy.AlwaysOnTop = AlwaysOnTop;
            copy.X = X;
            copy.Y = Y;
            copy.Width = Width;
            copy.Height = Height;
            copy.DockParentId = DockParentId;
            copy.DockGroupId = DockGroupId;
            copy.DockGroupOrder = DockGroupOrder;
            copy.TabOrder = TabOrder;
            copy.CreatedUtcTicks = CreatedUtcTicks;
            copy.ModifiedUtcTicks = ModifiedUtcTicks;
            copy.ReminderUtcTicks = ReminderUtcTicks;
            foreach (StickyTodoItem item in TodoItems)
                if (item != null) copy.TodoItems.Add(item.CloneForPersistence());
            foreach (StickyScheduleItem item in ScheduleItems)
                if (item != null) copy.ScheduleItems.Add(
                    item.CloneForPersistence());
            return copy;
        }

        public string Summary
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(Title)) return Title.Trim();
                if (IsSchedule && ScheduleItems.Count > 0)
                {
                    string schedule = ScheduleItems[0].Text ?? String.Empty;
                    return schedule.Length <= 28 ? schedule :
                        schedule.Substring(0, 28) + "…";
                }
                if (IsTodoList && TodoItems.Count > 0)
                {
                    string todo = TodoItems[0].Text ?? String.Empty;
                    return todo.Length <= 28 ? todo : todo.Substring(0, 28) + "…";
                }
                string value = (Text ?? String.Empty).Replace("\r", " ")
                    .Replace("\n", " ").Trim();
                if (value.Length == 0) return "（空白便利贴）";
                return value.Length <= 28 ? value : value.Substring(0, 28) + "…";
            }
        }

        public string DisplayTitle
        {
            get { return String.IsNullOrWhiteSpace(Title) ? Summary : Title.Trim(); }
        }

        public string SearchText
        {
            get
            {
                StringBuilder builder = new StringBuilder();
                builder.Append(Title).Append(' ').Append(Text);
                foreach (StickyTodoItem item in TodoItems)
                    builder.Append(' ').Append(item.Text);
                foreach (StickyScheduleItem item in ScheduleItems)
                    builder.Append(' ').Append(item.Text).Append(' ')
                        .Append(item.TargetDate.ToString("yyyy-MM-dd",
                            CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }

    // Canonical persistent model for a docked stack.  Live windows may use
    // DockParentId while the user inserts or removes a member, but reopening a
    // hidden stack is driven by DockGroupId + DockGroupOrder so it never has to
    // guess a chain from dictionary iteration order.
    internal static class StickyDockGroups
    {
        internal static void NormalizeAll(IList<StickyNoteData> notes)
        {
            if (notes == null || notes.Count == 0) return;
            Dictionary<string, StickyNoteData> byId = BuildIndex(notes);
            HashSet<string> remaining = new HashSet<string>(byId.Keys,
                StringComparer.OrdinalIgnoreCase);
            while (remaining.Count > 0)
            {
                string first = null;
                foreach (string id in remaining) { first = id; break; }
                List<StickyNoteData> component = new List<StickyNoteData>();
                HashSet<string> componentIds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                componentIds.Add(first);
                bool changed;
                do
                {
                    changed = false;
                    foreach (StickyNoteData candidate in notes)
                    {
                        if (candidate == null || componentIds.Contains(
                            candidate.Id)) continue;
                        foreach (StickyNoteData member in notes)
                        {
                            if (member == null || !componentIds.Contains(
                                member.Id)) continue;
                            if (ArePersistedNeighbors(member, candidate))
                            {
                                componentIds.Add(candidate.Id);
                                changed = true;
                                break;
                            }
                        }
                    }
                }
                while (changed);
                foreach (StickyNoteData note in notes)
                    if (note != null && componentIds.Contains(note.Id))
                        component.Add(note);
                foreach (string id in componentIds) remaining.Remove(id);
                ApplyOrderedGroup(OrderComponent(component));
            }
        }

        internal static List<StickyNoteData> GetOrderedGroup(
            IList<StickyNoteData> notes, StickyNoteData seed)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes == null || seed == null) return result;
            if (!String.IsNullOrEmpty(seed.DockGroupId))
            {
                foreach (StickyNoteData note in notes)
                    if (note != null && String.Equals(note.DockGroupId,
                        seed.DockGroupId, StringComparison.OrdinalIgnoreCase))
                        result.Add(note);
                result.Sort(CompareStoredOrder);
                if (result.Count > 0) return result;
            }
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            ids.Add(seed.Id);
            bool changed;
            do
            {
                changed = false;
                foreach (StickyNoteData note in notes)
                {
                    if (note == null || ids.Contains(note.Id)) continue;
                    bool connected = !String.IsNullOrEmpty(
                        note.DockParentId) && ids.Contains(note.DockParentId);
                    if (!connected)
                    {
                        foreach (StickyNoteData member in notes)
                        {
                            if (member != null && ids.Contains(member.Id) &&
                                String.Equals(member.DockParentId, note.Id,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                connected = true;
                                break;
                            }
                        }
                    }
                    if (connected && ids.Add(note.Id)) changed = true;
                }
            }
            while (changed);
            foreach (StickyNoteData note in notes)
                if (note != null && ids.Contains(note.Id)) result.Add(note);
            return OrderComponent(result);
        }

        internal static void ApplyOrderedGroup(IList<StickyNoteData> ordered)
        {
            if (ordered == null || ordered.Count == 0) return;
            List<StickyNoteData> unique = new List<StickyNoteData>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in ordered)
                if (note != null && !String.IsNullOrEmpty(note.Id) &&
                    seen.Add(note.Id)) unique.Add(note);
            if (unique.Count <= 1)
            {
                if (unique.Count == 1) ClearMembership(unique[0]);
                return;
            }
            ApplyGroupSnapshot(unique);
            RebuildVisibleParentChain(unique);
        }

        internal static void ApplyGroupSnapshot(
            IList<StickyNoteData> ordered)
        {
            if (ordered == null || ordered.Count == 0) return;
            List<StickyNoteData> unique = new List<StickyNoteData>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in ordered)
                if (note != null && !String.IsNullOrEmpty(note.Id) &&
                    seen.Add(note.Id)) unique.Add(note);
            if (unique.Count <= 1)
            {
                if (unique.Count == 1) ClearMembership(unique[0]);
                return;
            }
            string groupId = unique[0].Id;
            for (int index = 0; index < unique.Count; index++)
            {
                StickyNoteData note = unique[index];
                note.DockGroupId = groupId;
                note.DockGroupOrder = index;
            }
        }

        internal static void RebuildVisibleParentChain(
            IList<StickyNoteData> ordered)
        {
            if (ordered == null) return;
            StickyNoteData previousVisible = null;
            foreach (StickyNoteData note in ordered)
            {
                if (note == null) continue;
                if (!note.Visible)
                {
                    note.DockParentId = String.Empty;
                    continue;
                }
                note.DockParentId = previousVisible == null
                    ? String.Empty : previousVisible.Id;
                previousVisible = note;
            }
        }

        internal static void ClearMembership(StickyNoteData note)
        {
            if (note == null) return;
            note.DockParentId = String.Empty;
            note.DockGroupId = String.Empty;
            note.DockGroupOrder = -1;
        }

        private static Dictionary<string, StickyNoteData> BuildIndex(
            IList<StickyNoteData> notes)
        {
            Dictionary<string, StickyNoteData> result =
                new Dictionary<string, StickyNoteData>(
                    StringComparer.OrdinalIgnoreCase);
            if (notes == null) return result;
            foreach (StickyNoteData note in notes)
                if (note != null && !String.IsNullOrEmpty(note.Id))
                    result[note.Id] = note;
            return result;
        }

        private static bool ArePersistedNeighbors(StickyNoteData left,
            StickyNoteData right)
        {
            if (left == null || right == null) return false;
            if (!String.IsNullOrEmpty(left.DockGroupId) && String.Equals(
                left.DockGroupId, right.DockGroupId,
                StringComparison.OrdinalIgnoreCase)) return true;
            return String.Equals(left.DockParentId, right.Id,
                    StringComparison.OrdinalIgnoreCase) ||
                String.Equals(right.DockParentId, left.Id,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static List<StickyNoteData> OrderComponent(
            List<StickyNoteData> component)
        {
            if (component == null) return new List<StickyNoteData>();
            if (component.Count <= 1) return component;
            string groupId = component[0].DockGroupId;
            bool validStoredOrder = !String.IsNullOrEmpty(groupId);
            HashSet<int> orders = new HashSet<int>();
            foreach (StickyNoteData note in component)
            {
                if (!String.Equals(note.DockGroupId, groupId,
                    StringComparison.OrdinalIgnoreCase) ||
                    note.DockGroupOrder < 0 ||
                    !orders.Add(note.DockGroupOrder)) validStoredOrder = false;
            }
            if (validStoredOrder)
            {
                component.Sort(CompareStoredOrder);
                return component;
            }

            HashSet<string> componentIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in component)
                componentIds.Add(note.Id);
            List<StickyNoteData> roots = new List<StickyNoteData>();
            Dictionary<string, StickyNoteData> children =
                new Dictionary<string, StickyNoteData>(
                    StringComparer.OrdinalIgnoreCase);
            bool validChain = true;
            foreach (StickyNoteData note in component)
            {
                if (String.IsNullOrEmpty(note.DockParentId) ||
                    !componentIds.Contains(note.DockParentId)) roots.Add(note);
                else if (children.ContainsKey(note.DockParentId))
                    validChain = false;
                else children[note.DockParentId] = note;
            }
            if (validChain && roots.Count == 1)
            {
                List<StickyNoteData> chain = new List<StickyNoteData>();
                HashSet<string> seen = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                StickyNoteData current = roots[0];
                while (current != null && seen.Add(current.Id))
                {
                    chain.Add(current);
                    StickyNoteData child;
                    children.TryGetValue(current.Id, out child);
                    current = child;
                }
                if (chain.Count == component.Count) return chain;
            }

            component.Sort(delegate(StickyNoteData left, StickyNoteData right)
            {
                int value = left.Y.CompareTo(right.Y);
                if (value != 0) return value;
                value = left.X.CompareTo(right.X);
                if (value != 0) return value;
                value = left.TabOrder.CompareTo(right.TabOrder);
                if (value != 0) return value;
                value = left.CreatedUtcTicks.CompareTo(right.CreatedUtcTicks);
                return value != 0 ? value : String.Compare(left.Id, right.Id,
                    StringComparison.OrdinalIgnoreCase);
            });
            return component;
        }

        private static int CompareStoredOrder(StickyNoteData left,
            StickyNoteData right)
        {
            int value = left.DockGroupOrder.CompareTo(right.DockGroupOrder);
            if (value != 0) return value;
            value = left.Y.CompareTo(right.Y);
            if (value != 0) return value;
            return String.Compare(left.Id, right.Id,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
