using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PennyPet
{
    // File-system persistence and recovery adapter. Data serialization stays
    // in Core; the default Windows directory comes from WindowsDataPaths.
    internal sealed class StickyNoteRepository
    {
        private readonly string _filePath;
        private readonly List<StickyNoteData> _notes = new List<StickyNoteData>();
        private bool _loadSucceeded = true;
        private bool _recoveredFromLoadFailure;
        private bool _recoveredFromPartialSalvage;
        private int _salvagedNoteCount;
        private int _skippedCorruptLineCount;
        private string _recoveryBackupPath = String.Empty;
        private UnsupportedStickySchemaException _futureSchemaError;
        private readonly StickyNoteWriter _writer;
        private readonly SynchronizationContext _uiContext;
        private Exception _blockedSaveError;
        private int _blockedSaveFailures;

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        internal StickyNoteRepository(string filePath,
            Func<StickyWriteRequest, PersistenceResult> write = null)
        {
            _filePath = filePath;
            _uiContext = SynchronizationContext.Current;
            _writer = new StickyNoteWriter(write ?? WriteSnapshot);
            _writer.Failed += delegate(object sender, PersistenceFailedEventArgs e)
            {
                NotifySaveFailed(e.Result, e.ConsecutiveFailures);
            };
        }

        private static string DefaultFilePath
        {
            get
            {
                return Path.Combine(WindowsDataPaths.PennyPetDirectory,
                    "sticky-notes.dat");
            }
        }

        public static StickyNoteRepository Load()
        {
            string local = WindowsDataPaths.LocalApplicationDataDirectory;
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

            UnsupportedStickySchemaException futurePrimary =
                primaryError as UnsupportedStickySchemaException;
            if (futurePrimary != null)
            {
                repository.BlockFutureSchema(futurePrimary);
                return repository;
            }

            ApplicationDiagnostics.ReportNonFatal("sticky-notes-load", primaryError);
            repository._notes.Clear();
            string backupPath = filePath + ".bak";
            Exception backupError = null;
            bool backupLoaded = File.Exists(backupPath) &&
                TryPopulateFromFile(repository, backupPath, out backupError);
            UnsupportedStickySchemaException futureBackup =
                backupError as UnsupportedStickySchemaException;
            if (!backupLoaded && futureBackup != null)
            {
                repository.BlockFutureSchema(futureBackup);
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
                    repository.BlockFutureSchema(futureSchema);
                    return repository;
                }
                if (salvaged)
                {
                    try
                    {
                        // Preserve both unreadable sources byte-for-byte before
                        // writing any clean recovered primary.
                        repository._recoveryBackupPath =
                            PreserveUnreadableFile(filePath);
                        PreserveUnreadableFile(backupPath);
                        repository._recoveredFromPartialSalvage = true;
                        repository._salvagedNoteCount = salvagedCount;
                        repository._skippedCorruptLineCount = skippedCount;
                        repository._recoveredFromLoadFailure = true;
                        repository._loadSucceeded = true;
                        repository.SaveToFile(filePath);
                        return repository;
                    }
                    catch (Exception salvageError)
                    {
                        repository._notes.Clear();
                        repository._loadSucceeded = false;
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
                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                Exception schemaError = InspectSchemaVersions(lines, filePath);
                if (schemaError != null) throw schemaError;
                foreach (string line in lines)
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
            StickyNoteData note = StickyNoteCodec.ParseLine(line);
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

        private static bool TrySalvageStrict(StickyNoteRepository repository,
            string filePath, out int salvagedCount, out int skippedCount)
        {
            salvagedCount = 0;
            skippedCount = 0;
            if (String.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;
            if (new FileInfo(filePath).Length >
                StickyNoteLimits.MaximumDataFileBytes) return false;
            List<StickyNoteData> salvaged = new List<StickyNoteData>();
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
                    StickyNoteData note = StickyNoteCodec.ParseLine(line);
                    if (note == null || String.IsNullOrWhiteSpace(note.Id) ||
                        !ids.Add(note.Id))
                        throw new InvalidDataException(
                            "Salvage line has an invalid or duplicate NoteId.");
                    salvaged.Add(note);
                }
                catch (Exception)
                {
                    skippedCount++;
                }
            }
            if (salvaged.Count == 0) return false;
            repository._notes.Clear();
            foreach (StickyNoteData note in salvaged)
                repository._notes.Add(note);
            repository.NormalizeTabOrders();
            StickyDockGroups.NormalizeAll(repository._notes);
            salvagedCount = salvaged.Count;
            return true;
        }

        private void BlockFutureSchema(
            UnsupportedStickySchemaException error)
        {
            _notes.Clear();
            _loadSucceeded = false;
            _futureSchemaError = error;
            _recoveredFromLoadFailure = false;
            _recoveredFromPartialSalvage = false;
            _salvagedNoteCount = 0;
            _skippedCorruptLineCount = 0;
            _recoveryBackupPath = String.Empty;
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

        internal int Count
        {
            get { return _notes.Count; }
        }

        internal bool HasUnsavedChanges
        {
            get { return _writer.IsDirty; }
        }

        internal bool HasPendingSaves
        {
            get { return _writer.HasPending; }
        }

        internal Exception LastSaveError
        {
            get { return _blockedSaveError ?? _writer.LastError; }
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
            StickyNoteData note = CreateDraft(text, location);
            if (note != null) Save();
            return note;
        }

        // Prepares a live in-memory draft without persisting it, so one
        // creation attempt can fully configure type, v10 compatibility
        // placement, v11 preferred placement and visibility before the single
        // first disk write. The first persisted state must never be an
        // intermediate model.
        internal StickyNoteData CreateDraft(string text, Point location)
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
            if (!_loadSucceeded) return;
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
            if (!_loadSucceeded || note == null || !_notes.Contains(note)) return false;
            StickyDockOperations.ExtractSingleDockMember(
                StickyDockGroups.GetOrderedGroup(_notes, note), note);
            _notes.Remove(note);
            Save();
            return true;
        }

        public PersistenceResult Save()
        {
            return SaveToFile(_filePath);
        }

        internal void SaveAsync()
        {
            if (!_loadSucceeded)
            {
                RejectBlockedSave("save asynchronously");
                return;
            }
            _writer.Enqueue(new StickyWriteRequest(CloneNotes(_notes)), coalesce: true);
        }

        internal PersistenceResult WaitForPendingSaves()
        {
            return WaitForPendingSaves(TimeSpan.FromSeconds(5));
        }

        internal PersistenceResult WaitForPendingSaves(TimeSpan timeout)
        {
            return _writer.Flush(timeout);
        }

        internal PersistenceResult SaveToFile(string filePath)
        {
            if (!_loadSucceeded) return RejectBlockedSave("save");
            try
            {
                // Saving a copy does not acknowledge the owned primary file.
                if (!String.Equals(Path.GetFullPath(filePath),
                    Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                    return ExportSnapshot(filePath);
            }
            catch (Exception error) { return PersistenceResult.Failure(error); }
            return _writer.Enqueue(new StickyWriteRequest(CloneNotes(_notes)))
                .GetAwaiter().GetResult();
        }

        internal PersistenceResult ExportSnapshot(string filePath)
        {
            if (!_loadSucceeded)
                return PersistenceResult.Failure(CreateMutationBlockedError("export"));
            try
            {
                string destination = Path.GetFullPath(filePath);
                string primary = Path.GetFullPath(_filePath);
                if (String.Equals(destination, primary, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(destination, primary + ".bak", StringComparison.OrdinalIgnoreCase))
                    return PersistenceResult.Failure(new InvalidOperationException(
                        "Export must use a separate file, not the active data file or its backup."));
                // Independent export remains available if the primary writer stalls.
                AtomicTextFile.WriteAllLines(destination,
                    SerializeSnapshot(CloneNotes(_notes)), false);
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-export", error);
                return PersistenceResult.Failure(error);
            }
        }

        internal PersistenceResult CommitImportedMerge(
            StickyImportMergeResult merge, string backupPath)
        {
            if (merge == null || String.IsNullOrWhiteSpace(backupPath))
                return PersistenceResult.Failure(new ArgumentException(
                    "A merge plan and automatic backup path are required."));
            if (!_loadSucceeded)
                return PersistenceResult.Failure(
                    CreateMutationBlockedError("merge"));

            List<StickyNoteData> committed;
            try
            {
                committed = CloneAndValidateMergeSnapshot(
                    merge.MergedSnapshot);
            }
            catch (Exception error)
            {
                return PersistenceResult.Failure(error);
            }
            return CommitPreparedSnapshot(committed, backupPath);
        }

        internal PersistenceResult CommitFullRestore(
            IEnumerable<StickyNoteData> restoredSnapshot, string backupPath)
        {
            if (restoredSnapshot == null || String.IsNullOrWhiteSpace(backupPath))
                return PersistenceResult.Failure(new ArgumentException(
                    "A restore snapshot and automatic backup path are required."));
            if (!_loadSucceeded)
                return PersistenceResult.Failure(
                    CreateMutationBlockedError("restore"));

            List<StickyNoteData> committed;
            try
            {
                committed = CloneAndValidateMergeSnapshot(restoredSnapshot);
            }
            catch (Exception error)
            {
                return PersistenceResult.Failure(error);
            }
            return CommitPreparedSnapshot(committed, backupPath);
        }

        internal PersistenceResult CommitFullRestore(
            IEnumerable<StickyNoteData> restoredSnapshot)
        {
            // Keep one rolling rollback snapshot so repeated restores do not
            // create an unbounded trail of automatic backup files.
            return CommitFullRestore(restoredSnapshot,
                _filePath + ".before-restore.pennysticky");
        }

        private PersistenceResult CommitPreparedSnapshot(
            List<StickyNoteData> committed, string backupPath)
        {
            string primaryPath = Path.GetFullPath(_filePath);
            string automaticBackupPath = Path.GetFullPath(backupPath);
            if (String.Equals(primaryPath, automaticBackupPath,
                StringComparison.OrdinalIgnoreCase))
                return PersistenceResult.Failure(new InvalidOperationException(
                    "Automatic backup path must differ from the data file."));

            // Do not queue a dataset replacement behind stalled I/O: the
            // caller must remain free to cancel while the old model is active.
            PersistenceResult pending = WaitForPendingSaves();
            if (pending.Error is TimeoutException) return pending;
            PersistenceResult result = _writer.Enqueue(new StickyWriteRequest(
                committed, backupPath: automaticBackupPath,
                backupSnapshot: CloneNotes(_notes))).GetAwaiter().GetResult();
            if (!result.Succeeded) return result;

            // The model thread publishes the prepared dataset only after the
            // same writer has durably committed both the backup and primary.
            _notes.Clear();
            _notes.AddRange(committed);
            return result;
        }

        internal PersistenceResult CommitImportedMerge(
            StickyImportMergeResult merge)
        {
            // Keep one rolling rollback snapshot so repeated imports never
            // create an unbounded trail of automatic backup files.
            return CommitImportedMerge(merge,
                _filePath + ".before-import.pennysticky");
        }

        private void NotifySaveFailed(PersistenceResult result,
            int consecutiveFailures)
        {
            EventHandler<PersistenceFailedEventArgs> handler = SaveFailed;
            if (handler == null) return;
            PersistenceFailedEventArgs args = new PersistenceFailedEventArgs(
                result, consecutiveFailures);
            if (_uiContext == null ||
                SynchronizationContext.Current == _uiContext)
                handler(this, args);
            else
                _uiContext.Post(delegate { handler(this, args); }, null);
        }

        private static List<StickyNoteData> CloneNotes(
            IEnumerable<StickyNoteData> notes)
        {
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null) result.Add(note.CloneForPersistence());
            return result;
        }

        private PersistenceResult RejectBlockedSave(string operation)
        {
            Exception error = CreateMutationBlockedError(operation);
            _blockedSaveError = error;
            _blockedSaveFailures++;
            PersistenceResult result = PersistenceResult.Failure(error);
            NotifySaveFailed(result, _blockedSaveFailures);
            return result;
        }

        private Exception CreateMutationBlockedError(string operation)
        {
            string reason = IsFutureSchemaBlocked
                ? "a newer sticky-note schema was detected"
                : "sticky-note data was not loaded safely";
            return new InvalidOperationException(
                "Cannot " + operation + " because " + reason + ".");
        }

        private static List<string> SerializeSnapshot(
            IEnumerable<StickyNoteData> snapshot)
        {
            List<string> lines = new List<string>();
            Dictionary<string, string> parents = StickyDockGroups.BuildLegacyParents(snapshot);
            if (snapshot != null)
                foreach (StickyNoteData note in snapshot)
                {
                    if (note == null) continue;
                    string parent;
                    parents.TryGetValue(note.Id, out parent);
                    lines.Add(StickyNoteCodec.SerializeLine(note, parent ?? String.Empty));
                }
            return lines;
        }

        private static List<StickyNoteData> CloneAndValidateMergeSnapshot(
            IEnumerable<StickyNoteData> snapshot)
        {
            List<StickyNoteData> result = CloneNotes(snapshot);
            if (result.Count > StickyNoteLimits.MaximumNotes)
                throw new InvalidDataException("Too many sticky notes.");
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in result)
            {
                if (String.IsNullOrWhiteSpace(note.Id) || !ids.Add(note.Id))
                    throw new InvalidDataException(
                        "Merged sticky-note data contains invalid NoteIds.");
            }
            StickyDockGroups.NormalizeAll(result);
            return result;
        }

        private PersistenceResult WriteSnapshot(StickyWriteRequest request)
        {
            try
            {
                if (request.BackupPath != null)
                    AtomicTextFile.WriteAllLines(request.BackupPath,
                        SerializeSnapshot(request.BackupSnapshot), false);
                AtomicTextFile.WriteAllLines(_filePath,
                    SerializeSnapshot(request.Snapshot), true);
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-write", error);
                return PersistenceResult.Failure(error);
            }
        }

        internal static bool RepairForDisplay(StickyNoteData note,
            bool aggressive)
        {
            return StickyNoteCodec.RepairForDisplay(note, aggressive);
        }

        internal static string NormalizeRtf(string value)
        {
            return StickyNoteCodec.NormalizeRtf(value);
        }

        internal static string NormalizeFontFamily(string value)
        {
            return StickyNoteCodec.NormalizeFontFamily(value);
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

    }
}
