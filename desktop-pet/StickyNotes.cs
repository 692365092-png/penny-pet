using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PennyPet
{
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
    }

    internal sealed class StickyTodoItem
    {
        public StickyTodoItem(string text, bool completed)
            : this(text, completed, false)
        {
        }

        public StickyTodoItem(string text, bool completed, bool isPinned)
        {
            Text = ShortItemText.NormalizeAndTruncate(text);
            Completed = completed;
            IsPinned = isPinned;
        }

        public string Text;
        public bool Completed;
        public bool IsPinned;
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
        public int ColorArgb = Color.FromArgb(255, 255, 239, 156).ToArgb();
        public int BackgroundOpacityPercent = 100;
        public int TextColorArgb = Color.Black.ToArgb();
        public bool Visible = true;
        public bool AlwaysOnTop = true;
        public int X;
        public int Y;
        public int Width = 280;
        public int Height = 230;
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
                        .Append(item.TargetDate.ToString("yyyy-MM-dd"));
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

    internal sealed class StickyNoteRepository
    {
        private readonly string _filePath;
        private readonly List<StickyNoteData> _notes = new List<StickyNoteData>();
        private bool _loadSucceeded = true;
        private bool _recoveredFromLoadFailure;
        private string _recoveryBackupPath = String.Empty;

        private StickyNoteRepository(string filePath)
        {
            _filePath = filePath;
        }

        private static string DefaultFilePath
        {
            get
            {
                string directory = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "PennyPet");
                return Path.Combine(directory, "sticky-notes.dat");
            }
        }

        public static StickyNoteRepository Load()
        {
            string local = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            return LoadFromFileWithLegacyCandidates(DefaultFilePath,
                new string[] {
                    Path.Combine(local, "FishPet", "sticky-notes.dat"),
                    Path.Combine(local, "ShanYingPet", "sticky-notes.dat")
                });
        }

        internal static StickyNoteRepository LoadFromFileWithLegacyCandidates(
            string currentPath, IEnumerable<string> legacyCandidates)
        {
            bool currentExists = File.Exists(currentPath) ||
                File.Exists(currentPath + ".bak");
            StickyNoteRepository current = LoadFromFile(currentPath);
            if ((currentExists && !current.RecoveredFromLoadFailure) ||
                !current.LoadSucceeded || current.Count > 0 ||
                legacyCandidates == null) return current;
            foreach (string legacyPath in legacyCandidates)
            {
                if (String.IsNullOrWhiteSpace(legacyPath) ||
                    !File.Exists(legacyPath)) continue;
                StickyNoteRepository legacy = LoadFromFile(legacyPath);
                if (!legacy.LoadSucceeded || legacy.Count == 0) continue;
                current._notes.Clear();
                foreach (StickyNoteData note in legacy._notes)
                {
                    RepairForDisplay(note, false);
                    current._notes.Add(note);
                }
                current.NormalizeTabOrders();
                StickyDockGroups.NormalizeAll(current._notes);
                current.SaveToFile(currentPath);
                break;
            }
            return current;
        }

        internal static StickyNoteRepository LoadFromFile(string filePath)
        {
            StickyNoteRepository repository = new StickyNoteRepository(filePath);
            Exception primaryError;
            if (TryPopulateFromFile(repository, filePath, out primaryError))
                return repository;

            ApplicationDiagnostics.ReportNonFatal("sticky-notes-load", primaryError);
            repository._notes.Clear();
            string backupPath = filePath + ".bak";
            Exception backupError = null;
            bool backupLoaded = File.Exists(backupPath) &&
                TryPopulateFromFile(repository, backupPath, out backupError);
            if (!backupLoaded && File.Exists(backupPath) && backupError != null)
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-backup-load",
                    backupError);

            try
            {
                // Preserve the unreadable primary byte-for-byte before a clean
                // file is ever written.  A valid .bak can then be promoted; if
                // it is also unusable, the user still gets an empty writable
                // repository while both old files remain available for recovery.
                repository._recoveryBackupPath = PreserveUnreadableFile(filePath);
                repository._recoveredFromLoadFailure = true;
                repository._loadSucceeded = true;
                if (backupLoaded) repository.SaveToFile(filePath);
            }
            catch (Exception recoveryError)
            {
                repository._notes.Clear();
                repository._loadSucceeded = false;
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-recovery",
                    recoveryError);
            }
            return repository;
        }

        private static bool TryPopulateFromFile(StickyNoteRepository repository,
            string filePath, out Exception error)
        {
            error = null;
            repository._notes.Clear();
            try
            {
                if (!File.Exists(filePath))
                {
                    // If an atomic replacement was interrupted after the old
                    // file became .bak, recover that backup on the next launch.
                    string orphanedBackup = filePath + ".bak";
                    if (File.Exists(orphanedBackup))
                        return TryPopulateFromFile(repository, orphanedBackup,
                            out error);
                    return true;
                }
                if (new FileInfo(filePath).Length >
                    StickyNoteLimits.MaximumDataFileBytes)
                    throw new InvalidDataException(
                        "Sticky-note data file is too large.");
                foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
                    AddParsedLine(repository, line);
                repository.NormalizeTabOrders();
                StickyDockGroups.NormalizeAll(repository._notes);
                return true;
            }
            catch (Exception caught)
            {
                repository._notes.Clear();
                error = caught;
                return false;
            }
        }

        private static void AddParsedLine(StickyNoteRepository repository,
            string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            StickyNoteData note = ParseLine(line);
            if (note == null || String.IsNullOrEmpty(note.Id))
                throw new InvalidDataException("便利贴数据格式不完整。");
            foreach (StickyNoteData existing in repository._notes)
            {
                if (String.Equals(existing.Id, note.Id,
                    StringComparison.OrdinalIgnoreCase)) return;
            }
            if (repository._notes.Count >= StickyNoteLimits.MaximumNotes)
                throw new InvalidDataException("Too many sticky notes.");
            repository._notes.Add(note);
        }

        private static string PreserveUnreadableFile(string filePath)
        {
            if (!File.Exists(filePath)) return String.Empty;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string preserved = filePath + ".unreadable-" + stamp + ".bak";
            int suffix = 1;
            while (File.Exists(preserved))
                preserved = filePath + ".unreadable-" + stamp + "-" +
                    (suffix++).ToString() + ".bak";
            File.Move(filePath, preserved);
            return preserved;
        }

        internal bool LoadSucceeded
        {
            get { return _loadSucceeded; }
        }

        internal bool RecoveredFromLoadFailure
        {
            get { return _recoveredFromLoadFailure; }
        }

        internal string RecoveryBackupPath
        {
            get { return _recoveryBackupPath; }
        }

        internal int Count
        {
            get { return _notes.Count; }
        }

        internal bool CanCreate
        {
            get { return CanCreateAtCount(_loadSucceeded, _notes.Count); }
        }

        internal static bool CanCreateAtCount(bool loadSucceeded, int noteCount)
        {
            return loadSucceeded && noteCount >= 0 &&
                noteCount < StickyNoteLimits.MaximumNotes;
        }

        public StickyNoteData Create(string text, Point location)
        {
            if (!CanCreate) return null;
            StickyNoteData note = new StickyNoteData();
            string body = text ?? String.Empty;
            note.Text = body.Length <= StickyNoteLimits.MaximumBodyCharacters
                ? body : body.Substring(0, StickyNoteLimits.MaximumBodyCharacters);
            note.X = location.X;
            note.Y = location.Y;
            note.TabOrder = NextTabOrder();
            _notes.Add(note);
            Save();
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

        public void ReorderHidden(StickyNoteData moved, int destinationIndex)
        {
            if (moved == null || moved.Visible) return;
            List<StickyNoteData> all = GetInTabOrder();
            List<StickyNoteData> hidden = new List<StickyNoteData>();
            foreach (StickyNoteData note in all)
            {
                if (!note.Visible) hidden.Add(note);
            }
            int original = hidden.IndexOf(moved);
            if (original < 0) return;
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
            Save();
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
            bool removed = note != null && _notes.Remove(note);
            if (removed) Save();
            return removed;
        }

        public void Save()
        {
            SaveToFile(_filePath);
        }

        internal void SaveToFile(string filePath)
        {
            // A temporary read/parse failure must never turn an existing note file
            // into an empty one. The next clean launch can read it again.
            if (!_loadSucceeded) return;
            try
            {
                StickyDockGroups.NormalizeAll(_notes);
                List<string> lines = new List<string>();
                foreach (StickyNoteData note in _notes) lines.Add(SerializeLine(note));
                AtomicTextFile.WriteAllLines(filePath, lines, true);
            }
            catch (Exception error)
            {
                // Notes remain usable in memory if the disk is temporarily unavailable.
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-save", error);
            }
        }

        private static string SerializeLine(StickyNoteData note)
        {
            return String.Join("|", new string[]
            {
                "9", note.Id ?? String.Empty,
                note.Visible ? "1" : "0",
                note.AlwaysOnTop ? "1" : "0",
                note.ColorArgb.ToString(), note.X.ToString(), note.Y.ToString(),
                note.Width.ToString(), note.Height.ToString(),
                note.CreatedUtcTicks.ToString(), note.ModifiedUtcTicks.ToString(),
                note.ReminderUtcTicks.ToString(), note.IsTodoList ? "1" : "0",
                Encode(note.Title), EncodeTodos(note.TodoItems), Encode(note.Text),
                note.TabOrder.ToString(), Encode(NormalizeRtf(note.RichTextRtf)),
                Encode(NormalizeFontFamily(note.FontFamilyName)),
                Clamp(note.FontSizeTwips, 120, 1440).ToString(),
                Clamp(note.BackgroundOpacityPercent, 10, 100).ToString(),
                NormalizeTextColor(note.TextColorArgb).ToString(),
                Encode(note.DockParentId ?? String.Empty),
                Encode(note.DockGroupId ?? String.Empty),
                Math.Max(-1, note.DockGroupOrder).ToString(),
                note.IsSchedule ? "1" : "0",
                EncodeSchedules(note.ScheduleItems)
            });
        }

        private static StickyNoteData ParseLine(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) return null;
            string[] fields = line.Split('|');
            bool versionOne = fields.Length >= 13 && fields[0] == "1";
            bool versionTwo = fields.Length >= 16 && fields[0] == "2";
            bool versionThree = fields.Length >= 17 && fields[0] == "3";
            bool versionFour = fields.Length >= 18 && fields[0] == "4";
            bool versionFive = fields.Length >= 20 && fields[0] == "5";
            bool versionSix = fields.Length >= 22 && fields[0] == "6";
            bool versionSeven = fields.Length >= 23 && fields[0] == "7";
            bool versionEight = fields.Length >= 25 && fields[0] == "8";
            bool versionNine = fields.Length >= 27 && fields[0] == "9";
            if (!versionOne && !versionTwo && !versionThree && !versionFour &&
                !versionFive && !versionSix && !versionSeven && !versionEight &&
                !versionNine)
                return null;
            int number;
            long ticks;
            StickyNoteData note = new StickyNoteData();
            note.Id = fields[1];
            note.Visible = fields[2] != "0";
            note.AlwaysOnTop = fields[3] != "0";
            if (Int32.TryParse(fields[4], out number)) note.ColorArgb = number;
            if (Int32.TryParse(fields[5], out number)) note.X = number;
            if (Int32.TryParse(fields[6], out number)) note.Y = number;
            if (Int32.TryParse(fields[7], out number)) note.Width = Clamp(number, 200, 900);
            if (Int32.TryParse(fields[8], out number)) note.Height = Clamp(number, 140, 700);
            if (Int64.TryParse(fields[9], out ticks) && ticks > 0)
                note.CreatedUtcTicks = ticks;
            if (Int64.TryParse(fields[10], out ticks) && ticks > 0)
                note.ModifiedUtcTicks = ticks;
            if (Int64.TryParse(fields[11], out ticks) && ticks > 0)
                note.ReminderUtcTicks = ticks;
            if (versionOne)
            {
                note.Text = Decode(fields[12]);
            }
            else
            {
                note.IsTodoList = fields[12] == "1";
                note.Title = Decode(fields[13]);
                DecodeTodos(fields[14], note.TodoItems);
                note.Text = Decode(fields[15]);
                if ((versionThree || versionFour || versionFive || versionSix ||
                    versionSeven || versionEight || versionNine) &&
                    Int32.TryParse(fields[16], out number))
                    note.TabOrder = Math.Max(0, number);
                if (versionFour || versionFive || versionSix || versionSeven ||
                    versionEight || versionNine)
                    note.RichTextRtf = NormalizeRtf(Decode(fields[17]));
                if (versionFive || versionSix || versionSeven || versionEight ||
                    versionNine)
                {
                    note.FontFamilyName = NormalizeFontFamily(Decode(fields[18]));
                    if (Int32.TryParse(fields[19], out number))
                        note.FontSizeTwips = Clamp(number, 120, 1440);
                }
                if (versionSix || versionSeven || versionEight || versionNine)
                {
                    if (Int32.TryParse(fields[20], out number))
                        note.BackgroundOpacityPercent = Clamp(number, 10, 100);
                    if (Int32.TryParse(fields[21], out number))
                        note.TextColorArgb = NormalizeTextColor(number);
                }
                if (versionSeven || versionEight || versionNine)
                    note.DockParentId = Decode(fields[22]);
                if (versionEight || versionNine)
                {
                    note.DockGroupId = Decode(fields[23]);
                    if (Int32.TryParse(fields[24], out number))
                        note.DockGroupOrder = Math.Max(-1, number);
                }
                if (versionNine)
                {
                    note.IsSchedule = fields[25] == "1";
                    DecodeSchedules(fields[26], note.ScheduleItems);
                    if (note.IsSchedule) note.IsTodoList = false;
                }
            }
            if (!versionSix && !versionSeven && !versionEight && !versionNine)
            {
                Color oldPaper = Color.FromArgb(note.ColorArgb);
                note.TextColorArgb = oldPaper.GetBrightness() > 0.52F
                    ? Color.Black.ToArgb()
                    : Color.White.ToArgb();
            }
            if (note.Title.Length > StickyNoteLimits.MaximumTitleCharacters ||
                note.Text.Length > StickyNoteLimits.MaximumBodyCharacters ||
                note.RichTextRtf.Length > StickyNoteLimits.MaximumRichTextCharacters ||
                note.TodoItems.Count > StickyNoteLimits.MaximumTodoItemsPerNote ||
                note.ScheduleItems.Count > StickyNoteLimits.MaximumScheduleItemsPerNote)
                throw new InvalidDataException("Sticky-note content exceeds safety limits.");
            RepairForDisplay(note, false);
            return note;
        }

        internal static bool RepairForDisplay(StickyNoteData note,
            bool aggressive)
        {
            if (note == null) return false;
            bool changed = false;
            if (String.IsNullOrWhiteSpace(note.Id))
            {
                note.Id = Guid.NewGuid().ToString("N");
                changed = true;
            }
            if (note.Title == null) { note.Title = String.Empty; changed = true; }
            if (note.Text == null) { note.Text = String.Empty; changed = true; }
            if (note.FontFamilyName == null)
            {
                note.FontFamilyName = "Microsoft YaHei UI";
                changed = true;
            }
            string family = NormalizeFontFamily(note.FontFamilyName);
            if (!String.Equals(family, note.FontFamilyName,
                StringComparison.Ordinal))
            {
                note.FontFamilyName = family;
                changed = true;
            }
            int width = Clamp(note.Width, 280, 900);
            int height = Clamp(note.Height, 220, 700);
            int size = Clamp(note.FontSizeTwips, 120, 1440);
            int opacity = Clamp(note.BackgroundOpacityPercent, 10, 100);
            int textColor = NormalizeTextColor(note.TextColorArgb);
            Color paper = Color.FromArgb(note.ColorArgb);
            int paperArgb = Color.FromArgb(255, paper.R, paper.G,
                paper.B).ToArgb();
            if (note.Width != width) { note.Width = width; changed = true; }
            if (note.Height != height) { note.Height = height; changed = true; }
            if (note.FontSizeTwips != size)
            {
                note.FontSizeTwips = size;
                changed = true;
            }
            if (note.BackgroundOpacityPercent != opacity)
            {
                note.BackgroundOpacityPercent = opacity;
                changed = true;
            }
            if (note.TextColorArgb != textColor)
            {
                note.TextColorArgb = textColor;
                changed = true;
            }
            if (note.ColorArgb != paperArgb)
            {
                note.ColorArgb = paperArgb;
                changed = true;
            }
            if (note.IsSchedule && note.IsTodoList)
            {
                note.IsTodoList = false;
                changed = true;
            }
            string normalizedRtf = NormalizeRtf(note.RichTextRtf);
            if (aggressive) normalizedRtf = String.Empty;
            if (!String.Equals(normalizedRtf, note.RichTextRtf,
                StringComparison.Ordinal))
            {
                note.RichTextRtf = normalizedRtf;
                changed = true;
            }
            if (aggressive)
            {
                note.FontFamilyName = "Microsoft YaHei UI";
                StickyDockGroups.ClearMembership(note);
                note.Visible = false;
                changed = true;
            }
            return changed;
        }

        private static int NormalizeTextColor(int argb)
        {
            return argb == Color.White.ToArgb()
                ? Color.White.ToArgb()
                : Color.Black.ToArgb();
        }

        internal static string NormalizeRtf(string value)
        {
            if (String.IsNullOrWhiteSpace(value) ||
                value.Length > StickyNoteLimits.MaximumRichTextCharacters)
                return String.Empty;
            string trimmed = value.TrimStart();
            return trimmed.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase)
                ? value : String.Empty;
        }

        internal static string NormalizeFontFamily(string value)
        {
            string name = (value ?? String.Empty).Trim();
            if (name.Length == 0 || name.Length > 100)
                return "Microsoft YaHei UI";
            return name;
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

        private void NormalizeTabOrders()
        {
            List<StickyNoteData> ordered = GetInTabOrder();
            for (int i = 0; i < ordered.Count; i++) ordered[i].TabOrder = i;
        }

        private static string EncodeTodos(IEnumerable<StickyTodoItem> items)
        {
            StringBuilder builder = new StringBuilder();
            if (items != null)
            {
                foreach (StickyTodoItem item in items)
                {
                    if (item == null) continue;
                    if (builder.Length > 0) builder.Append('\n');
                    builder.Append(item.Completed ? '1' : '0').Append('\t')
                        .Append(item.IsPinned ? '1' : '0').Append('\t')
                        .Append((item.Text ?? String.Empty).Replace("\r", " ")
                            .Replace("\n", " "));
                }
            }
            return Encode(builder.ToString());
        }

        private static void DecodeTodos(string value, IList<StickyTodoItem> output)
        {
            if (output == null) return;
            string decoded = Decode(value);
            foreach (string line in decoded.Split(new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf('\t');
                if (separator < 0) continue;
                string remainder = line.Substring(separator + 1);
                bool isPinned = false;
                string text = remainder;
                int pinSeparator = remainder.IndexOf('\t');
                if (pinSeparator == 1 &&
                    (remainder[0] == '0' || remainder[0] == '1'))
                {
                    isPinned = remainder[0] == '1';
                    text = remainder.Substring(pinSeparator + 1);
                }
                if (output.Count >= StickyNoteLimits.MaximumTodoItemsPerNote)
                    throw new InvalidDataException("Todo content exceeds safety limits.");
                output.Add(new StickyTodoItem(text,
                    line.Substring(0, separator) == "1", isPinned));
            }
        }

        private static string EncodeSchedules(
            IEnumerable<StickyScheduleItem> items)
        {
            StringBuilder builder = new StringBuilder();
            if (items != null)
            {
                foreach (StickyScheduleItem item in items)
                {
                    if (item == null) continue;
                    if (builder.Length > 0) builder.Append('\n');
                    builder.Append(item.TargetDate.Date.Ticks).Append('\t')
                        .Append(item.IsPinned ? '1' : '0').Append('\t')
                        .Append((item.Text ?? String.Empty).Replace("\r", " ")
                            .Replace("\n", " ").Replace("\t", " "));
                }
            }
            return Encode(builder.ToString());
        }

        private static void DecodeSchedules(string value,
            IList<StickyScheduleItem> output)
        {
            if (output == null) return;
            string decoded = Decode(value);
            foreach (string line in decoded.Split(new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = line.IndexOf('\t');
                long ticks;
                if (separator <= 0 || !Int64.TryParse(
                    line.Substring(0, separator), out ticks)) continue;
                DateTime date;
                try { date = new DateTime(ticks).Date; }
                catch { continue; }
                if (output.Count >= StickyNoteLimits.MaximumScheduleItemsPerNote)
                    throw new InvalidDataException(
                        "Schedule content exceeds safety limits.");
                int secondSeparator = line.IndexOf('\t', separator + 1);
                bool pinned = secondSeparator > separator &&
                    line.Substring(separator + 1,
                        secondSeparator - separator - 1) == "1";
                int textStart = secondSeparator > separator
                    ? secondSeparator + 1 : separator + 1;
                string text = ShortItemText.NormalizeAndTruncate(
                    line.Substring(textStart));
                if (!String.IsNullOrWhiteSpace(text))
                    output.Add(new StickyScheduleItem(text, date, pinned));
            }
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return String.Empty; }
        }
    }

    internal sealed class ImeFriendlyRichTextBox : RichTextBox
    {
        private const int WmImeStartComposition = 0x010D;
        private const int WmImeEndComposition = 0x010E;
        private const int WmImeComposition = 0x010F;
        private bool _compositionActive;

        public event EventHandler CompositionStarted;
        public event EventHandler CompositionCommitted;

        internal bool IsImeComposing
        {
            get { return _compositionActive; }
        }

        internal static bool StartsOrUpdatesComposition(int message)
        {
            return message == WmImeStartComposition ||
                message == WmImeComposition;
        }

        protected override void WndProc(ref Message message)
        {
            // Tell the pet to stop layered-frame rendering before Windows lets
            // the IME process the composition message.  Doing this after
            // base.WndProc leaves a short race in which pinyin can be committed
            // as literal Latin text on slower machines.
            if (StartsOrUpdatesComposition(message.Msg) && !_compositionActive)
            {
                _compositionActive = true;
                EventHandler started = CompositionStarted;
                if (started != null) started(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
            if (IsDisposed || !IsHandleCreated) return;
            if (message.Msg != WmImeEndComposition) return;
            _compositionActive = false;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    // A fast typist may already have started the next IME
                    // composition before this queued callback runs.  Never let
                    // an old END message cancel that newer composition.
                    if (_compositionActive) return;
                    EventHandler handler = CompositionCommitted;
                    if (handler != null) handler(this, EventArgs.Empty);
                });
            }
            catch { }
        }
    }

    internal sealed class ImeFriendlyTextBox : TextBox
    {
        private const int WmImeStartComposition = 0x010D;
        private const int WmImeEndComposition = 0x010E;
        private const int WmImeComposition = 0x010F;
        private bool _compositionActive;

        public event EventHandler CompositionStarted;
        public event EventHandler CompositionCommitted;

        internal bool IsImeComposing
        {
            get { return _compositionActive; }
        }

        protected override void WndProc(ref Message message)
        {
            bool composingMessage = message.Msg == WmImeStartComposition ||
                message.Msg == WmImeComposition;
            if (composingMessage && !_compositionActive)
            {
                _compositionActive = true;
                EventHandler started = CompositionStarted;
                if (started != null) started(this, EventArgs.Empty);
            }
            base.WndProc(ref message);
            if (IsDisposed || !IsHandleCreated ||
                message.Msg != WmImeEndComposition) return;
            _compositionActive = false;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_compositionActive) return;
                    EventHandler committed = CompositionCommitted;
                    if (committed != null) committed(this, EventArgs.Empty);
                });
            }
            catch { }
        }
    }

    internal sealed class ImeCompositionEventArgs : EventArgs
    {
        public ImeCompositionEventArgs(bool active)
        {
            Active = active;
        }

        public bool Active { get; private set; }
    }

    internal sealed class DockHorizontalResizeEventArgs : EventArgs
    {
        public DockHorizontalResizeEventArgs(int left, int width)
        {
            Left = left;
            Width = width;
        }

        public int Left { get; private set; }
        public int Width { get; private set; }
    }

    internal sealed class NoteTitleDialog : Form
    {
        private readonly TextBox _title;

        public NoteTitleDialog(string currentTitle)
        {
            Text = "命名便利贴";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(390, 150);
            Font = StickyNoteForm.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);
            ImeMode = ImeMode.NoControl;

            Label hint = new Label();
            hint.Text = "便利贴名称（支持多语言；留空时使用内容摘要）：";
            hint.AutoSize = true;
            hint.Location = new Point(20, 18);
            _title = new TextBox();
            _title.ImeMode = ImeMode.NoControl;
            _title.MaxLength = StickyNoteLimits.MaximumTitleCharacters;
            _title.Text = currentTitle ?? String.Empty;
            _title.Location = new Point(20, 50);
            _title.Size = new Size(350, 28);

            Button ok = new Button();
            ok.Text = "保存";
            ok.DialogResult = DialogResult.OK;
            ok.Location = new Point(204, 98);
            ok.Size = new Size(78, 32);
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Location = new Point(292, 98);
            cancel.Size = new Size(78, 32);
            Controls.Add(hint);
            Controls.Add(_title);
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            ActiveControl = _title;
            Shown += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    Activate();
                    ActiveControl = _title;
                    _title.Select();
                    _title.Focus();
                    _title.SelectAll();
                });
            };
        }

        public string NoteTitle
        {
            get { return (_title.Text ?? String.Empty).Trim(); }
        }

        internal bool TitleInputIsInitialActive
        {
            get { return Object.ReferenceEquals(ActiveControl, _title); }
        }

        internal bool UsesUnforcedMultilingualIme
        {
            get { return ImeMode == ImeMode.NoControl &&
                _title.ImeMode == ImeMode.NoControl; }
        }
    }

    internal sealed class MarqueeListView : ListView
    {
        private bool _marqueeSelecting;
        private Point _marqueeStart;
        private Rectangle _reversibleFrame = Rectangle.Empty;
        private readonly HashSet<ListViewItem> _initialSelection =
            new HashSet<ListViewItem>();

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || HitTest(e.Location).Item != null)
                return;
            _marqueeSelecting = true;
            _marqueeStart = e.Location;
            _initialSelection.Clear();
            if ((ModifierKeys & Keys.Control) != 0)
            {
                foreach (ListViewItem item in SelectedItems)
                    _initialSelection.Add(item);
            }
            else
            {
                foreach (ListViewItem item in Items) item.Selected = false;
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_marqueeSelecting || (MouseButtons & MouseButtons.Left) == 0)
            {
                base.OnMouseMove(e);
                return;
            }
            EraseReversibleFrame();
            Rectangle clientFrame = NormalizeRectangle(_marqueeStart, e.Location);
            _reversibleFrame = RectangleToScreen(clientFrame);
            if (clientFrame.Width > 2 || clientFrame.Height > 2)
                ControlPaint.DrawReversibleFrame(_reversibleFrame,
                    Color.FromArgb(70, 110, 170), FrameStyle.Dashed);
            foreach (ListViewItem item in Items)
            {
                Rectangle fullRow = new Rectangle(0, item.Bounds.Top,
                    Math.Max(ClientSize.Width, item.Bounds.Width), item.Bounds.Height);
                bool hit = clientFrame.IntersectsWith(fullRow);
                item.Selected = hit || _initialSelection.Contains(item);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            EndMarqueeSelection();
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture) EndMarqueeSelection();
            base.OnMouseCaptureChanged(e);
        }

        private void EndMarqueeSelection()
        {
            if (!_marqueeSelecting) return;
            EraseReversibleFrame();
            _marqueeSelecting = false;
            Capture = false;
            _initialSelection.Clear();
        }

        private void EraseReversibleFrame()
        {
            if (_reversibleFrame == Rectangle.Empty) return;
            ControlPaint.DrawReversibleFrame(_reversibleFrame,
                Color.FromArgb(70, 110, 170), FrameStyle.Dashed);
            _reversibleFrame = Rectangle.Empty;
        }

        private static Rectangle NormalizeRectangle(Point first, Point second)
        {
            return Rectangle.FromLTRB(Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y), Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) EndMarqueeSelection();
            base.Dispose(disposing);
        }
    }

    internal sealed class StickyNotesManagerForm : Form
    {
        private readonly Func<List<StickyNoteData>> _getNotes;
        private readonly Action _createNote;
        private readonly Action<StickyNoteData> _showNote;
        private readonly Action<StickyNoteData> _hideNote;
        private readonly Action<StickyNoteData> _deleteNote;
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Button _deleteButton;

        internal bool CreateRequested { get; private set; }
        internal StickyNoteData ShowRequested { get; private set; }

        public StickyNotesManagerForm(Func<List<StickyNoteData>> getNotes,
            Action createNote, Action<StickyNoteData> showNote,
            Action<StickyNoteData> hideNote, Action<StickyNoteData> deleteNote)
        {
            _getNotes = getNotes;
            _createNote = createNote;
            _showNote = showNote;
            _hideNote = hideNote;
            _deleteNote = deleteNote;
            Text = "便利贴管理";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            MinimumSize = new Size(620, 420);
            ClientSize = new Size(700, 470);
            Font = StickyNoteForm.CreateSafeFont("Microsoft YaHei UI", 9F,
                FontStyle.Regular);

            Label searchLabel = new Label();
            searchLabel.Text = "搜索：";
            searchLabel.AutoSize = true;
            searchLabel.Location = new Point(16, 18);
            _search = new TextBox();
            _search.ImeMode = ImeMode.NoControl;
            _search.Location = new Point(70, 14);
            _search.Size = new Size(360, 28);
            _search.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _search.TextChanged += delegate { RefreshList(); };

            Button create = Button("新建", 446, delegate
            {
                CreateRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            });
            create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button show = Button("显示/编辑", 526, delegate
            {
                StickyNoteData note = SelectedNote();
                if (note == null) return;
                ShowRequested = note;
                DialogResult = DialogResult.OK;
                Close();
            });
            show.Width = 88;
            show.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Button hide = Button("收起", 620, delegate
            {
                StickyNoteData note = SelectedNote();
                if (note != null) _hideNote(note);
                RefreshList();
            });
            hide.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _list = new MarqueeListView();
            _list.View = View.Details;
            _list.MultiSelect = true;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.Location = new Point(16, 54);
            _list.Size = new Size(668, 350);
            _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                AnchorStyles.Left | AnchorStyles.Right;
            _list.Columns.Add("内容摘要", 310);
            _list.Columns.Add("状态", 80);
            _list.Columns.Add("提醒", 150);
            _list.Columns.Add("修改时间", 120);
            _list.DoubleClick += delegate
            {
                StickyNoteData note = SelectedNote();
                if (note == null) return;
                ShowRequested = note;
                DialogResult = DialogResult.OK;
                Close();
            };
            _list.SelectedIndexChanged += delegate { RefreshSelectionState(); };
            _list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Delete)
                {
                    DeleteSelectedNotes();
                    e.SuppressKeyPress = true;
                }
            };

            _deleteButton = Button("删除所选", 16, delegate
            {
                DeleteSelectedNotes();
            });
            _deleteButton.Width = 100;
            _deleteButton.Top = 420;
            _deleteButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Button selectAll = Button("全选", 124, delegate
            {
                foreach (ListViewItem item in _list.Items) item.Selected = true;
            });
            selectAll.Top = 420;
            selectAll.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            Label multiHint = new Label();
            multiHint.Text = "在空白处按住鼠标拖拽可框选多张；也支持 Ctrl/Shift 多选和 Delete。";
            multiHint.AutoSize = false;
            multiHint.AutoEllipsis = true;
            multiHint.Location = new Point(210, 429);
            multiHint.Size = new Size(380, 24);
            multiHint.Anchor = AnchorStyles.Left | AnchorStyles.Right |
                AnchorStyles.Bottom;
            Button close = Button("关闭", 604, delegate { Close(); });
            close.Top = 420;
            close.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

            Controls.Add(searchLabel);
            Controls.Add(_search);
            Controls.Add(create);
            Controls.Add(show);
            Controls.Add(hide);
            Controls.Add(_list);
            Controls.Add(_deleteButton);
            Controls.Add(selectAll);
            Controls.Add(multiHint);
            Controls.Add(close);
            Shown += delegate { RefreshList(); };
        }

        private Button Button(string text, int left, EventHandler click)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(left, 12);
            button.Size = new Size(74, 32);
            button.Click += click;
            return button;
        }

        private void RefreshList()
        {
            string query = (_search.Text ?? String.Empty).Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (StickyNoteData note in _getNotes())
            {
                if (query.Length > 0 && note.SearchText.IndexOf(query,
                    StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                DateTime? reminder = note.ReminderUtc;
                string reminderText = reminder.HasValue && reminder.Value > DateTime.UtcNow
                    ? reminder.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
                ListViewItem item = new ListViewItem(note.Summary);
                string status = note.Visible ? "显示中" : "已收起";
                if (note.IsTodoList)
                {
                    int completed = 0;
                    foreach (StickyTodoItem todo in note.TodoItems)
                    {
                        if (todo.Completed) completed++;
                    }
                    status = "待办 " + completed + "/" + note.TodoItems.Count;
                }
                else if (note.IsSchedule)
                    status = "日程 " + note.ScheduleItems.Count + "项";
                item.SubItems.Add(status);
                item.SubItems.Add(reminderText);
                item.SubItems.Add(note.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm"));
                item.Tag = note;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            RefreshSelectionState();
        }

        private void DeleteSelectedNotes()
        {
            List<StickyNoteData> selected = SelectedNotes();
            if (selected.Count == 0) return;
            string message = selected.Count == 1
                ? "确定删除这张便利贴吗？此操作无法撤销。"
                : "确定一次删除选中的 " + selected.Count +
                    " 张便利贴吗？此操作无法撤销。";
            if (MessageBox.Show(this, message, "批量删除便利贴",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) !=
                DialogResult.Yes) return;
            foreach (StickyNoteData note in selected) _deleteNote(note);
            RefreshList();
        }

        private List<StickyNoteData> SelectedNotes()
        {
            List<StickyNoteData> selected = new List<StickyNoteData>();
            foreach (ListViewItem item in _list.SelectedItems)
            {
                StickyNoteData note = item.Tag as StickyNoteData;
                if (note != null) selected.Add(note);
            }
            return selected;
        }

        private void RefreshSelectionState()
        {
            if (_deleteButton == null) return;
            int count = _list.SelectedItems.Count;
            _deleteButton.Enabled = count > 0;
            _deleteButton.Text = count > 1
                ? "删除所选（" + count + "）" : "删除所选";
        }

        private StickyNoteData SelectedNote()
        {
            return _list.SelectedItems.Count == 0
                ? null : _list.SelectedItems[0].Tag as StickyNoteData;
        }

        internal bool SupportsMarqueeBatchDelete
        {
            get
            {
                return _list is MarqueeListView && _list.MultiSelect &&
                    _deleteButton != null;
            }
        }
    }
}
