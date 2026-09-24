using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PennyPet
{
    internal sealed class StickyLoadResult
    {
        internal readonly StickyStore Store;
        internal readonly StickyModel Model;
        internal StickyLoadResult(StickyStore store, StickyModel model)
        {
            Store = store;
            Model = model;
        }
    }

    internal sealed partial class StickyStore
    {
        private bool _loadSucceeded = true;
        private bool _recoveredFromLoadFailure;
        private bool _recoveredFromPartialSalvage;
        private int _salvagedNoteCount;
        private int _skippedCorruptLineCount;
        private string _recoveryBackupPath = String.Empty;
        private UnsupportedStickySchemaException _futureSchemaError;

        internal bool LoadSucceeded
        {
            get { return _loadSucceeded; }
        }

        internal bool IsFutureSchemaBlocked
        {
            get { return _futureSchemaError != null; }
        }

        internal int DetectedFutureVersion
        {
            get
            {
                return _futureSchemaError == null ? 0 :
                    _futureSchemaError.DetectedVersion;
            }
        }

        internal UnsupportedStickySchemaException FutureSchemaError
        {
            get { return _futureSchemaError; }
        }

        internal bool RecoveredFromLoadFailure
        {
            get { return _recoveredFromLoadFailure; }
        }

        internal bool RecoveredFromPartialSalvage
        {
            get { return _recoveredFromPartialSalvage; }
        }

        internal int SalvagedNoteCount
        {
            get { return _salvagedNoteCount; }
        }

        internal int SkippedCorruptLineCount
        {
            get { return _skippedCorruptLineCount; }
        }

        internal string RecoveryBackupPath
        {
            get { return _recoveryBackupPath; }
        }

        private static string DefaultFilePath
        {
            get
            {
                return Path.Combine(WindowsDataPaths.PennyPetDirectory,
                    "sticky-notes.dat");
            }
        }

        public static StickyLoadResult Load()
        {
            string local = WindowsDataPaths.LocalApplicationDataDirectory;
            return LoadFromFileWithLegacyCandidates(DefaultFilePath,
                new string[] {
                    Path.Combine(local, "FishPet", "sticky-notes.dat"),
                    Path.Combine(local, "ShanYingPet", "sticky-notes.dat")
                });
        }

        internal static StickyLoadResult LoadFromFileWithLegacyCandidates(
            string currentPath, IEnumerable<string> legacyCandidates)
        {
            bool currentExists = File.Exists(currentPath) ||
                File.Exists(currentPath + ".bak");
            StickyLoadResult current = LoadFromFile(currentPath);
            if ((currentExists && !current.Store.RecoveredFromLoadFailure) ||
                !current.Store.LoadSucceeded || current.Model.Count > 0 ||
                legacyCandidates == null) return current;
            foreach (string legacyPath in legacyCandidates)
            {
                if (String.IsNullOrWhiteSpace(legacyPath) ||
                    !File.Exists(legacyPath)) continue;
                StickyLoadResult legacy = LoadFromFile(legacyPath);
                if (!legacy.Store.LoadSucceeded || legacy.Model.Count == 0) continue;
                // LoadFromFile already repaired content, order and file relations.
                current.Model.ReplaceWith(legacy.Model.InStorageOrder);
                SaveLoadedSnapshot(current);
                break;
            }
            return current;
        }

        internal static StickyLoadResult LoadFromFile(string filePath)
        {
            StickyLoadResult repository = new StickyLoadResult(
                new StickyStore(filePath), new StickyModel());
            Exception primaryError;
            if (TryPopulateFromFile(repository, filePath, out primaryError))
                return repository;

            UnsupportedStickySchemaException futurePrimary =
                primaryError as UnsupportedStickySchemaException;
            if (futurePrimary != null)
            {
                BlockFutureSchema(repository, futurePrimary);
                return repository;
            }

            ApplicationDiagnostics.ReportNonFatal("sticky-notes-load", primaryError);
            repository.Model.Clear();
            string backupPath = filePath + ".bak";
            Exception backupError = null;
            bool backupLoaded = File.Exists(backupPath) &&
                TryPopulateFromFile(repository, backupPath, out backupError);
            UnsupportedStickySchemaException futureBackup =
                backupError as UnsupportedStickySchemaException;
            if (!backupLoaded && futureBackup != null)
            {
                BlockFutureSchema(repository, futureBackup);
                return repository;
            }
            if (!backupLoaded && File.Exists(backupPath) && backupError != null)
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-backup-load",
                    backupError);

            if (!backupLoaded)
            {
                int salvagedCount;
                int skippedCount;
                bool salvaged;
                try
                {
                    salvaged = TrySalvageStrict(repository, filePath,
                        out salvagedCount, out skippedCount);
                    if (!salvaged && File.Exists(backupPath))
                        salvaged = TrySalvageStrict(repository, backupPath,
                            out salvagedCount, out skippedCount);
                }
                catch (UnsupportedStickySchemaException futureSchema)
                {
                    BlockFutureSchema(repository, futureSchema);
                    return repository;
                }
                if (salvaged)
                {
                    try
                    {
                        // Preserve both unreadable sources byte-for-byte before
                        // writing any clean recovered primary.
                        repository.Store._recoveryBackupPath =
                            PreserveUnreadableFile(filePath);
                        PreserveUnreadableFile(backupPath);
                        repository.Store._recoveredFromPartialSalvage = true;
                        repository.Store._salvagedNoteCount = salvagedCount;
                        repository.Store._skippedCorruptLineCount = skippedCount;
                        repository.Store._recoveredFromLoadFailure = true;
                        repository.Store._loadSucceeded = true;
                        SaveLoadedSnapshot(repository);
                        return repository;
                    }
                    catch (Exception salvageError)
                    {
                        repository.Model.Clear();
                        repository.Store._loadSucceeded = false;
                        ApplicationDiagnostics.ReportNonFatal(
                            "sticky-notes-salvage", salvageError);
                    }
                }
            }

            try
            {
                // Preserve the unreadable primary byte-for-byte before a clean
                // file is ever written.  A valid .bak can then be promoted; if
                // it is also unusable, the user still gets an empty writable
                // repository while both old files remain available for recovery.
                repository.Store._recoveryBackupPath = PreserveUnreadableFile(filePath);
                repository.Store._recoveredFromLoadFailure = true;
                repository.Store._loadSucceeded = true;
                if (backupLoaded) SaveLoadedSnapshot(repository);
            }
            catch (Exception recoveryError)
            {
                repository.Model.Clear();
                repository.Store._loadSucceeded = false;
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-recovery",
                    recoveryError);
            }
            return repository;
        }

        private static bool TryPopulateFromFile(StickyLoadResult repository,
            string filePath, out Exception error)
        {
            error = null;
            repository.Model.Clear();
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
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                Exception schemaError = InspectSchemaVersions(lines, filePath);
                if (schemaError != null) throw schemaError;
                var legacyParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines)
                    AddParsedLine(repository, line, legacyParents);
                repository.Model.NormalizeTabOrders();
                StickyDockFileRelations.Normalize(
                    new List<StickyNoteData>(repository.Model.InStorageOrder), legacyParents);
                return true;
            }
            catch (Exception caught)
            {
                repository.Model.Clear();
                error = caught;
                return false;
            }
        }

        private static void AddParsedLine(StickyLoadResult repository,
            string line, IDictionary<string, string> legacyParents)
        {
            if (String.IsNullOrWhiteSpace(line)) return;
            string parent;
            StickyNoteData note = StickyNoteCodec.ParseLine(line, out parent);
            if (note == null || String.IsNullOrEmpty(note.Id))
                throw new InvalidDataException("便利贴数据格式不完整。");
            foreach (StickyNoteData existing in repository.Model.InStorageOrder)
            {
                if (String.Equals(existing.Id, note.Id,
                    StringComparison.OrdinalIgnoreCase)) return;
            }
            if (repository.Model.InStorageOrder.Count >= StickyNoteLimits.MaximumNotes)
                throw new InvalidDataException("Too many sticky notes.");
            repository.Model.AddLoaded(note);
            legacyParents.Add(note.Id, parent);
        }

        private static Exception InspectSchemaVersions(
            IEnumerable<string> lines, string sourcePath)
        {
            InvalidDataException firstInvalidVersion = null;
            foreach (string line in lines)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                int separator = line.IndexOf('|');
                string token = separator < 0 ? line :
                    line.Substring(0, separator);
                int version;
                if (!Int32.TryParse(token, NumberStyles.None,
                    CultureInfo.InvariantCulture, out version) || version <= 0)
                {
                    if (firstInvalidVersion == null)
                        firstInvalidVersion = new InvalidDataException(
                            "便利贴数据版本无效。");
                    continue;
                }
                if (version > StickyNoteCodec.CurrentVersion)
                    return new UnsupportedStickySchemaException(version,
                        StickyNoteCodec.CurrentVersion, sourcePath);
            }
            return firstInvalidVersion;
        }

        private static bool TrySalvageStrict(StickyLoadResult repository,
            string filePath, out int salvagedCount, out int skippedCount)
        {
            salvagedCount = 0;
            skippedCount = 0;
            if (String.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;
            if (new FileInfo(filePath).Length >
                StickyNoteLimits.MaximumDataFileBytes) return false;
            List<StickyNoteData> salvaged = new List<StickyNoteData>();
            var legacyParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            UnsupportedStickySchemaException futureSchema =
                InspectSchemaVersions(lines, filePath) as
                    UnsupportedStickySchemaException;
            if (futureSchema != null) throw futureSchema;
            foreach (string line in lines)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                if (salvaged.Count >= StickyNoteLimits.MaximumNotes)
                {
                    skippedCount++;
                    continue;
                }
                try
                {
                    // Strict raw-field validation must run before the codec so
                    // corrupt Base64 cannot be fail-soft-decoded into empty text.
                    StickyImportBackupValidator.ValidateRawLine(line);
                    string parent;
                    StickyNoteData note = StickyNoteCodec.ParseLine(line, out parent);
                    if (note == null || String.IsNullOrWhiteSpace(note.Id) ||
                        !ids.Add(note.Id))
                        throw new InvalidDataException(
                            "Salvage line has an invalid or duplicate NoteId.");
                    salvaged.Add(note);
                    legacyParents.Add(note.Id, parent);
                }
                catch (Exception)
                {
                    skippedCount++;
                }
            }
            if (salvaged.Count == 0) return false;
            repository.Model.Clear();
            foreach (StickyNoteData note in salvaged)
                repository.Model.AddLoaded(note);
            repository.Model.NormalizeTabOrders();
            StickyDockFileRelations.Normalize(
                    new List<StickyNoteData>(repository.Model.InStorageOrder), legacyParents);
            salvagedCount = salvaged.Count;
            return true;
        }

        private static void BlockFutureSchema(StickyLoadResult repository,
            UnsupportedStickySchemaException error)
        {
            repository.Model.Clear();
            repository.Store._loadSucceeded = false;
            repository.Store._futureSchemaError = error;
            repository.Store._recoveredFromLoadFailure = false;
            repository.Store._recoveredFromPartialSalvage = false;
            repository.Store._salvagedNoteCount = 0;
            repository.Store._skippedCorruptLineCount = 0;
            repository.Store._recoveryBackupPath = String.Empty;
            ApplicationDiagnostics.ReportNonFatal(
                "sticky-notes-future-schema", error);
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

        private static void SaveLoadedSnapshot(StickyLoadResult loaded)
        {
            loaded.Store.Save(new StickyWriteRequest(loaded.Model.CaptureSnapshot()))
                .GetAwaiter().GetResult();
        }
    }
}
