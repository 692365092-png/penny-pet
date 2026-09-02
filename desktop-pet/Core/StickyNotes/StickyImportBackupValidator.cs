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

        private static void ValidateRawLine(string line)
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

            if (version == 1) return;

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
                default: return 0;
            }
        }

        private static void RequireBoolean(string value)
        {
            if (value != "0" && value != "1")
                throw new InvalidDataException("Malformed boolean field.");
        }

        private static void RequireInt32(string[] fields, int index)
        {
            int value;
            if (!Int32.TryParse(fields[index], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value))
                throw new InvalidDataException("Malformed numeric field.");
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
                string[] fields = line.Split('\t');
                int state;
                if (fields.Length != 3 || !Int32.TryParse(fields[0],
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out state) || state < 0 || state > 2 ||
                    (fields[1] != "0" && fields[1] != "1"))
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
