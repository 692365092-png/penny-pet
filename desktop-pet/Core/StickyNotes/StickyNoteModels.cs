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
        // v11 durable placement preference: a target identity plus its
        // display-local logical rect. DisplayId/LocalLogical* remain v10
        // migration/legacy fields and X/Y/Width/Height are the last known
        // physical fallback, never the durable authority.
        public string PreferredDisplayTargetKey = String.Empty;
        public int PreferredLocalLogicalX;
        public int PreferredLocalLogicalY;
        public int PreferredLocalLogicalWidth;
        public int PreferredLocalLogicalHeight;
        // Legacy file input only; runtime never follows this link.
        public string DockParentId = String.Empty;
        // The sole ordered Dock relation, including hidden slots.
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
            copy.DisplayId = DisplayId;
            copy.LocalLogicalX = LocalLogicalX;
            copy.LocalLogicalY = LocalLogicalY;
            copy.LocalLogicalWidth = LocalLogicalWidth;
            copy.LocalLogicalHeight = LocalLogicalHeight;
            copy.PreferredDisplayTargetKey = PreferredDisplayTargetKey;
            copy.PreferredLocalLogicalX = PreferredLocalLogicalX;
            copy.PreferredLocalLogicalY = PreferredLocalLogicalY;
            copy.PreferredLocalLogicalWidth = PreferredLocalLogicalWidth;
            copy.PreferredLocalLogicalHeight = PreferredLocalLogicalHeight;
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

}
