using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PennyPet
{
    // Pure, version-aware codec for sticky-note persistence. File selection,
    // atomic replacement and diagnostics remain platform responsibilities.
    internal static class StickyNoteCodec
    {
        internal const int VersionTen = 10;
        internal const int VersionTenFieldCount = 32;
        internal const int VersionEleven = 11;
        internal const int VersionElevenFieldCount = 37;
        internal const int CurrentVersion = VersionEleven;
        internal const int CurrentFieldCount = VersionElevenFieldCount;
        internal const int MaximumDisplayIdCharacters = 1024;
        internal const int MaximumLocalLogicalValue = 20000;

        private static readonly char[] LineSeparators = new char[] { '\n' };
        // File revisions append fields (v1's text position is the exception).
        // Validate the complete historical layout once before reading fields.
        private static readonly int[] MinimumFieldCounts =
            { 0, 13, 16, 17, 18, 20, 22, 23, 25, 27, 32, 37 };

        internal static string SerializeLine(StickyNoteData note)
        {
            return SerializeLine(note, String.Empty);
        }

        internal static string SerializeLine(StickyNoteData note, string legacyParentId)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));
            WindowPlacementPreference preferred = note.PreferredPlacement;
            LogicalRect local = preferred == null ? new LogicalRect() : preferred.LocalLogicalRect;
            StickyLegacyPlacement legacy = note.LegacyPlacement;
            LogicalRect legacyLocal = legacy == null ? new LogicalRect() : legacy.Logical;
            return String.Join("|", new string[]
            {
                CurrentVersion.ToString(CultureInfo.InvariantCulture),
                note.Id ?? String.Empty,
                note.Visible ? "1" : "0",
                note.AlwaysOnTop ? "1" : "0",
                note.ColorArgb.ToString(CultureInfo.InvariantCulture),
                note.X.ToString(CultureInfo.InvariantCulture),
                note.Y.ToString(CultureInfo.InvariantCulture),
                note.Width.ToString(CultureInfo.InvariantCulture),
                note.Height.ToString(CultureInfo.InvariantCulture),
                note.CreatedUtcTicks.ToString(CultureInfo.InvariantCulture),
                note.ModifiedUtcTicks.ToString(CultureInfo.InvariantCulture),
                note.ReminderUtcTicks.ToString(CultureInfo.InvariantCulture),
                note.IsTodoList ? "1" : "0",
                Encode(note.Title), EncodeTodos(note.TodoItems), Encode(note.Text),
                note.TabOrder.ToString(CultureInfo.InvariantCulture),
                Encode(NormalizeRtf(note.RichTextRtf)),
                Encode(NormalizeFontFamily(note.FontFamilyName)),
                Clamp(note.FontSizeTwips, 120, 1440)
                    .ToString(CultureInfo.InvariantCulture),
                Clamp(note.BackgroundOpacityPercent, 10, 100)
                    .ToString(CultureInfo.InvariantCulture),
                NormalizeTextColor(note.TextColorArgb)
                    .ToString(CultureInfo.InvariantCulture),
                Encode(legacyParentId ?? String.Empty),
                Encode(note.DockGroupId ?? String.Empty),
                Math.Max(-1, note.DockGroupOrder)
                    .ToString(CultureInfo.InvariantCulture),
                note.IsSchedule ? "1" : "0",
                EncodeSchedules(note.ScheduleItems),
                Encode(legacy?.RuntimeGdiName ?? String.Empty),
                legacyLocal.X.ToString(CultureInfo.InvariantCulture),
                legacyLocal.Y.ToString(CultureInfo.InvariantCulture),
                legacyLocal.Width.ToString(CultureInfo.InvariantCulture),
                legacyLocal.Height.ToString(CultureInfo.InvariantCulture),
                Encode(preferred?.PreferredTargetKey ?? String.Empty),
                local.X.ToString(
                    CultureInfo.InvariantCulture),
                local.Y.ToString(
                    CultureInfo.InvariantCulture),
                local.Width.ToString(
                    CultureInfo.InvariantCulture),
                local.Height.ToString(
                    CultureInfo.InvariantCulture)
            });
        }

        internal static StickyNoteData ParseLine(string line)
        {
            return ParseLine(line, out _);
        }

        // The compatibility link belongs to the file being read, never to the
        // live note or its persistence snapshot. Whole-file readers migrate it
        // before returning notes to runtime workflows.
        internal static StickyNoteData ParseLine(string line, out string legacyParentId)
        {
            legacyParentId = String.Empty;
            if (String.IsNullOrWhiteSpace(line)) return null;
            string[] fields = line.Split('|');
            int version;
            if (!Int32.TryParse(fields[0], NumberStyles.None,
                    CultureInfo.InvariantCulture, out version) ||
                version < 1 || version > CurrentVersion ||
                fields[0] != version.ToString(CultureInfo.InvariantCulture) ||
                fields.Length < MinimumFieldCounts[version]) return null;

            int number;
            long ticks;
            StickyNoteData note = new StickyNoteData();
            note.Id = fields[1];
            note.Visible = fields[2] != "0";
            note.AlwaysOnTop = fields[3] != "0";
            if (Int32.TryParse(fields[4], out number)) note.ColorArgb = number;
            if (Int32.TryParse(fields[5], out number)) note.X = number;
            if (Int32.TryParse(fields[6], out number)) note.Y = number;
            if (Int32.TryParse(fields[7], out number))
                note.Width = Clamp(number,
                    StickyNoteLimits.MinimumWindowWidth,
                    StickyNoteLimits.MaximumWindowWidth);
            if (Int32.TryParse(fields[8], out number))
                note.Height = Clamp(number,
                    StickyNoteLimits.MinimumWindowHeight,
                    StickyNoteLimits.MaximumWindowHeight);
            if (Int64.TryParse(fields[9], out ticks) && ticks > 0)
                note.CreatedUtcTicks = ticks;
            if (Int64.TryParse(fields[10], out ticks) && ticks > 0)
                note.ModifiedUtcTicks = ticks;
            if (Int64.TryParse(fields[11], out ticks) && ticks > 0)
                note.ReminderUtcTicks = ticks;
            if (version == 1)
            {
                note.Text = Decode(fields[12]);
            }
            else
            {
                note.IsTodoList = fields[12] == "1";
                note.Title = Decode(fields[13]);
                DecodeTodos(fields[14], note.TodoItems);
                note.Text = Decode(fields[15]);
                if (version >= 3 &&
                    Int32.TryParse(fields[16], out number))
                    note.TabOrder = Math.Max(0, number);
                if (version >= 4)
                    note.RichTextRtf = NormalizeRtf(Decode(fields[17]));
                if (version >= 5)
                {
                    note.FontFamilyName = NormalizeFontFamily(Decode(fields[18]));
                    if (Int32.TryParse(fields[19], out number))
                        note.FontSizeTwips = Clamp(number, 120, 1440);
                }
                if (version >= 6)
                {
                    if (Int32.TryParse(fields[20], out number))
                        note.BackgroundOpacityPercent = Clamp(number, 10, 100);
                    if (Int32.TryParse(fields[21], out number))
                        note.TextColorArgb = NormalizeTextColor(number);
                }
                if (version >= 7)
                    legacyParentId = Decode(fields[22]);
                if (version >= 8)
                {
                    note.DockGroupId = Decode(fields[23]);
                    if (Int32.TryParse(fields[24], out number))
                        note.DockGroupOrder = Math.Max(-1, number);
                }
                if (version >= 9)
                {
                    note.IsSchedule = fields[25] == "1";
                    DecodeSchedules(fields[26], note.ScheduleItems);
                    if (note.IsSchedule) note.IsTodoList = false;
                }
                if (version >= 10)
                    note.LegacyPlacement = ReadLegacyPlacement(fields);
                if (version >= 11)
                    note.PreferredPlacement = ReadPreferredPlacement(fields);
            }

            if (version < 6)
                note.TextColorArgb = IsLightPaper(note.ColorArgb)
                    ? WhiteArgb : BlackArgb;
            if (note.Title.Length > StickyNoteLimits.MaximumTitleCharacters ||
                note.Text.Length > StickyNoteLimits.MaximumBodyCharacters ||
                note.RichTextRtf.Length > StickyNoteLimits.MaximumRichTextCharacters ||
                note.TodoItems.Count > StickyNoteLimits.MaximumTodoItemsPerNote ||
                note.ScheduleItems.Count > StickyNoteLimits.MaximumScheduleItemsPerNote)
                throw new InvalidDataException(
                    "Sticky-note content exceeds safety limits.");
            RepairForDisplay(note, false);
            return note;
        }

        private static StickyLegacyPlacement ReadLegacyPlacement(string[] fields)
        {
            string gdiName = Decode(fields[27]);
            if (String.IsNullOrWhiteSpace(gdiName)) return null;
            int x, y, width, height;
            Int32.TryParse(fields[28], out x);
            Int32.TryParse(fields[29], out y);
            Int32.TryParse(fields[30], out width);
            Int32.TryParse(fields[31], out height);
            return new StickyLegacyPlacement(gdiName, new LogicalRect {
                X = x, Y = y, Width = Clamp(width, 1, MaximumLocalLogicalValue),
                Height = Clamp(height, 1, MaximumLocalLogicalValue) });
        }

        // Invalid or incomplete file fields mean no preference. Input repair
        // and size caps stay here; runtime preferences are complete values.
        private static WindowPlacementPreference ReadPreferredPlacement(string[] fields)
        {
            string key = Decode(fields[32]).Trim();
            int x, y, width, height;
            if (key.Length == 0 || key.Length > MaximumDisplayIdCharacters ||
                !Int32.TryParse(fields[35], out width) || width <= 0 ||
                !Int32.TryParse(fields[36], out height) || height <= 0) return null;
            Int32.TryParse(fields[33], out x);
            Int32.TryParse(fields[34], out y);
            return new WindowPlacementPreference(key, new LogicalRect {
                X = x, Y = y, Width = Math.Min(width, MaximumLocalLogicalValue),
                Height = Math.Min(height, MaximumLocalLogicalValue) });
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
            int width = Clamp(note.Width,
                StickyNoteLimits.MinimumWindowWidth,
                StickyNoteLimits.MaximumWindowWidth);
            int height = Clamp(note.Height,
                StickyNoteLimits.MinimumWindowHeight,
                StickyNoteLimits.MaximumWindowHeight);
            int size = Clamp(note.FontSizeTwips, 120, 1440);
            int opacity = Clamp(note.BackgroundOpacityPercent, 10, 100);
            int textColor = NormalizeTextColor(note.TextColorArgb);
            int paperArgb = unchecked((int)(0xFF000000u |
                ((uint)note.ColorArgb & 0x00FFFFFFu)));
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

        internal static string NormalizeRtf(string value)
        {
            if (String.IsNullOrWhiteSpace(value) ||
                value.Length > StickyNoteLimits.MaximumRichTextCharacters)
                return String.Empty;
            string trimmed = value.TrimStart();
            return trimmed.StartsWith("{\\rtf",
                StringComparison.OrdinalIgnoreCase) ? value : String.Empty;
        }

        internal static string NormalizeFontFamily(string value)
        {
            string name = (value ?? String.Empty).Trim();
            if (name.Length == 0 || name.Length > 100)
                return "Microsoft YaHei UI";
            return name;
        }

        private const int WhiteArgb = -1;
        private const int BlackArgb = unchecked((int)0xFF000000);

        private static bool IsLightPaper(int argb)
        {
            int red = (argb >> 16) & 0xFF;
            int green = (argb >> 8) & 0xFF;
            int blue = argb & 0xFF;
            int maximum = Math.Max(red, Math.Max(green, blue));
            int minimum = Math.Min(red, Math.Min(green, blue));
            return (maximum + minimum) / 510F > 0.52F;
        }

        private static int NormalizeTextColor(int argb)
        {
            return argb == WhiteArgb ? WhiteArgb : BlackArgb;
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
                    builder.Append((int)item.State).Append('\t')
                        .Append(item.IsPinned ? '1' : '0').Append('\t')
                        .Append((item.Text ?? String.Empty).Replace("\r", " ")
                            .Replace("\n", " ").Replace("\t", " "));
                }
            }
            return Encode(builder.ToString());
        }

        private static void DecodeTodos(string value,
            List<StickyTodoItem> output)
        {
            if (output == null) return;
            string decoded = Decode(value);
            foreach (string line in decoded.Split(LineSeparators,
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
                    throw new InvalidDataException(
                        "Todo content exceeds safety limits.");
                int stateValue;
                if (!Int32.TryParse(line.Substring(0, separator),
                    out stateValue) || stateValue < 0 || stateValue > 2)
                    stateValue = 0;
                output.Add(new StickyTodoItem(text,
                    (StickyTodoState)stateValue, isPinned));
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
            List<StickyScheduleItem> output)
        {
            if (output == null) return;
            string decoded = Decode(value);
            foreach (string line in decoded.Split(LineSeparators,
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
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return String.Empty;
            }
        }
    }
}
