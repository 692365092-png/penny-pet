using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Platform-neutral persistence and recovery. The default Windows data
    // path is isolated here; serialization and corruption recovery are reusable.
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
}
