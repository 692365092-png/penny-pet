using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PennyPet
{
    internal sealed class StickyImportValidationResult
    {
        private StickyImportValidationResult(bool succeeded,
            List<StickyNoteData> notes, string errorMessage)
        {
            Succeeded = succeeded;
            Notes = notes ?? new List<StickyNoteData>();
            ErrorMessage = errorMessage ?? String.Empty;
        }

        internal bool Succeeded { get; private set; }
        internal List<StickyNoteData> Notes { get; private set; }
        internal string ErrorMessage { get; private set; }

        internal static StickyImportValidationResult Success(
            List<StickyNoteData> notes)
        {
            return new StickyImportValidationResult(true, notes,
                String.Empty);
        }

        internal static StickyImportValidationResult Failure(string message)
        {
            return new StickyImportValidationResult(false,
                new List<StickyNoteData>(), message);
        }
    }

    // Validates a complete backup before any repository or window mutation.
    // The input is bounded by MaximumNotes; filesystem size checks stay in the
    // Windows adapter because Core must not own paths or file IO.
    internal static class StickyImportBackupValidator
    {
        internal static StickyImportValidationResult Validate(
            IEnumerable<string> lines)
        {
            List<StickyNoteData> notes = new List<StickyNoteData>();
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                if (lines == null) return StickyImportValidationResult.Success(notes);
                foreach (string line in lines)
                {
                    if (String.IsNullOrWhiteSpace(line)) continue;
                    if (notes.Count >= StickyNoteLimits.MaximumNotes)
                        throw new InvalidDataException("Too many sticky notes.");
                    ValidateRawLine(line);
                    StickyNoteData note = StickyNoteCodec.ParseLine(line);
                    if (note == null || String.IsNullOrWhiteSpace(note.Id))
                        throw new InvalidDataException(
                            "Sticky-note data is missing a NoteId.");
                    if (!ids.Add(note.Id))
                        throw new InvalidDataException(
                            "Backup contains duplicate NoteIds.");
                    notes.Add(note.CloneForPersistence());
                }
                return StickyImportValidationResult.Success(notes);
            }
            catch (Exception error)
            {
                return StickyImportValidationResult.Failure(error.Message);
            }
        }

        internal static void ValidateRawLine(string line)
        {
            string[] fields = line.Split('|');
            int version;
            if (fields.Length == 0 || !Int32.TryParse(fields[0],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                throw new InvalidDataException("Unsupported sticky-note version.");

            int expectedFields = ExpectedFieldCount(version);
            if (expectedFields == 0 || fields.Length != expectedFields)
                throw new InvalidDataException("Malformed sticky-note fields.");
            if (String.IsNullOrWhiteSpace(fields[1]))
                throw new InvalidDataException("Sticky-note data is missing a NoteId.");
            RequireBoolean(fields[2]);
            RequireBoolean(fields[3]);
            RequireInt32(fields, 4);
            RequireInt32(fields, 5);
            RequireInt32(fields, 6);
            RequireInt32(fields, 7);
            RequireInt32(fields, 8);
            RequirePositiveInt64(fields, 9);
            RequirePositiveInt64(fields, 10);
            RequireNonNegativeInt64(fields, 11);

            if (version == 1)
            {
                // v1 stores the body in the final Base64 field too.  Do not
                // let the codec's fail-soft decoder turn corrupted content
                // into an apparently valid empty note.
                RequireBase64(fields[12]);
                return;
            }

            RequireBoolean(fields[12]);
            RequireBase64(fields[13]);
            ValidateTodoPayload(RequireBase64(fields[14]));
            RequireBase64(fields[15]);
            if (version >= 3) RequireInt32(fields, 16);
            if (version >= 4)
            {
                string rtf = RequireBase64(fields[17]);
                if (rtf.Length > 0 && !rtf.TrimStart().StartsWith(
                    "{\\rtf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Malformed RTF field.");
            }
            if (version >= 5)
            {
                RequireBase64(fields[18]);
                RequireInt32(fields, 19);
            }
            if (version >= 6)
            {
                RequireInt32(fields, 20);
                RequireInt32(fields, 21);
            }
            if (version >= 7) RequireBase64(fields[22]);
            if (version >= 8)
            {
                RequireBase64(fields[23]);
                RequireInt32(fields, 24);
            }
            if (version >= 9)
            {
                RequireBoolean(fields[25]);
                ValidateSchedulePayload(RequireBase64(fields[26]));
            }
            if (version >= 10)
            {
                string displayId = RequireBase64(fields[27]);
                int localX = RequireInt32(fields, 28);
                int localY = RequireInt32(fields, 29);
                int localWidth = RequireInt32(fields, 30);
                int localHeight = RequireInt32(fields, 31);
                ValidateCanonicalPlacement(displayId, localX, localY,
                    localWidth, localHeight);
            }
            if (version >= 11)
            {
                string preferredKey = RequireBase64(fields[32]);
                int preferredX = RequireInt32(fields, 33);
                int preferredY = RequireInt32(fields, 34);
                int preferredWidth = RequireInt32(fields, 35);
                int preferredHeight = RequireInt32(fields, 36);
                ValidateCanonicalPlacement(preferredKey, preferredX,
                    preferredY, preferredWidth, preferredHeight);
            }
        }

        private static int ExpectedFieldCount(int version)
        {
            switch (version)
            {
                case 1: return 13;
                case 2: return 16;
                case 3: return 17;
                case 4: return 18;
                case 5: return 20;
                case 6: return 22;
                case 7: return 23;
                case 8: return 25;
                case 9: return 27;
                case StickyNoteCodec.VersionTen:
                    return StickyNoteCodec.VersionTenFieldCount;
                case StickyNoteCodec.VersionEleven:
                    return StickyNoteCodec.VersionElevenFieldCount;
                default: return 0;
            }
        }

        private static void RequireBoolean(string value)
        {
            if (value != "0" && value != "1")
                throw new InvalidDataException("Malformed boolean field.");
        }

        private static int RequireInt32(string[] fields, int index)
        {
            int value;
            if (!Int32.TryParse(fields[index], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("Malformed numeric field.");
            return value;
        }

        private static void ValidateCanonicalPlacement(string displayId,
            int localX, int localY, int localWidth, int localHeight)
        {
            if (displayId.Length > StickyNoteCodec.MaximumDisplayIdCharacters)
                throw new InvalidDataException("Display identity is too long.");
            if (displayId.Length == 0)
            {
                if (localX != 0 || localY != 0 || localWidth != 0 ||
                    localHeight != 0)
                    throw new InvalidDataException(
                        "Incomplete canonical placement.");
                return;
            }
            if (String.IsNullOrWhiteSpace(displayId))
                throw new InvalidDataException("Invalid display identity.");

            int limit = StickyNoteCodec.MaximumLocalLogicalValue;
            if (localX < -limit || localX > limit || localY < -limit ||
                localY > limit || localWidth <= 0 || localWidth > limit ||
                localHeight <= 0 || localHeight > limit)
                throw new InvalidDataException("Invalid canonical placement.");
        }

        private static void RequirePositiveInt64(string[] fields, int index)
        {
            long value;
            if (!Int64.TryParse(fields[index], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) || value <= 0)
                throw new InvalidDataException("Malformed timestamp field.");
        }

        private static void RequireNonNegativeInt64(string[] fields, int index)
        {
            long value;
            if (!Int64.TryParse(fields[index], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) || value < 0)
                throw new InvalidDataException("Malformed reminder field.");
        }

        private static string RequireBase64(string value)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(value); }
            catch (FormatException)
            {
                throw new InvalidDataException("Malformed encoded text field.");
            }
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException("Malformed UTF-8 text field.");
            }
        }

        private static void ValidateTodoPayload(string payload)
        {
            if (String.IsNullOrEmpty(payload)) return;
            int count = 0;
            foreach (string line in payload.Split('\n'))
            {
                if (line.Length == 0) continue;
                int first = line.IndexOf('\t');
                if (first < 0)
                    throw new InvalidDataException("Malformed Todo field.");
                int second = line.IndexOf('\t', first + 1);
                if (second < 0)
                    throw new InvalidDataException("Malformed Todo field.");
                string stateField = line.Substring(0, first);
                string pinnedField = line.Substring(first + 1,
                    second - first - 1);
                int state;
                if (!Int32.TryParse(stateField,
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out state) || state < 0 || state > 2 ||
                    (pinnedField != "0" && pinnedField != "1"))
                    throw new InvalidDataException("Malformed Todo field.");
                if (++count > StickyNoteLimits.MaximumTodoItemsPerNote)
                    throw new InvalidDataException("Todo content exceeds safety limits.");
            }
        }

        private static void ValidateSchedulePayload(string payload)
        {
            if (String.IsNullOrEmpty(payload)) return;
            int count = 0;
            foreach (string line in payload.Split('\n'))
            {
                if (line.Length == 0) continue;
                string[] fields = line.Split('\t');
                long ticks;
                if (fields.Length != 3 || !Int64.TryParse(fields[0],
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out ticks) || ticks <= 0 || (fields[1] != "0" &&
                    fields[1] != "1"))
                    throw new InvalidDataException("Malformed Schedule field.");
                try { new DateTime(ticks); }
                catch (ArgumentOutOfRangeException)
                {
                    throw new InvalidDataException("Malformed Schedule date.");
                }
                if (++count > StickyNoteLimits.MaximumScheduleItemsPerNote)
                    throw new InvalidDataException(
                        "Schedule content exceeds safety limits.");
            }
        }
    }
}
