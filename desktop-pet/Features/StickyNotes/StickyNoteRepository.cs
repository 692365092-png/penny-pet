using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private bool _hasUnsavedChanges;
        private Exception _lastSaveError;
        private int _consecutiveSaveFailures;
        private readonly object _saveGate = new object();
        private readonly object _ioGate = new object();
        private readonly SynchronizationContext _uiContext;
        private long _requestedGeneration;
        private long _completedGeneration = -1;
        private long _lastWrittenGeneration = -1;
        private bool _writerRunning;
        private List<StickyNoteData> _latestSnapshot;
        private long _latestGeneration = -1;

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        private StickyNoteRepository(string filePath)
        {
            _filePath = filePath;
            _uiContext = SynchronizationContext.Current;
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
            get { return _hasUnsavedChanges; }
        }

        internal bool HasPendingSaves
        {
            get { lock (_saveGate) return _writerRunning; }
        }

        internal Exception LastSaveError
        {
            get { return _lastSaveError; }
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
            if (!_loadSucceeded) return false;
            bool removed = note != null && _notes.Remove(note);
            if (removed) Save();
            return removed;
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
            bool startWriter;
            lock (_saveGate)
            {
                _latestSnapshot = CloneNotes(_notes);
                _latestGeneration = ++_requestedGeneration;
                _hasUnsavedChanges = true;
                startWriter = !_writerRunning;
                if (startWriter) _writerRunning = true;
            }
            if (startWriter)
                ThreadPool.QueueUserWorkItem(delegate { AsyncWriterLoop(); });
        }

        internal PersistenceResult WaitForPendingSaves()
        {
            return WaitForPendingSaves(TimeSpan.FromSeconds(5));
        }

        internal PersistenceResult WaitForPendingSaves(TimeSpan timeout)
        {
            if (timeout < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            Stopwatch elapsed = Stopwatch.StartNew();
            lock (_saveGate)
            {
                while (_writerRunning)
                {
                    TimeSpan remaining = timeout - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        TimeoutException error = new TimeoutException(
                            "Timed out waiting for pending sticky-note saves.");
                        ApplicationDiagnostics.ReportNonFatal(
                            "sticky-notes-pending-save-timeout", error);
                        return PersistenceResult.Failure(error);
                    }
                    Monitor.Wait(_saveGate, remaining);
                }
            }
            return PersistenceResult.Success();
        }

        private void AsyncWriterLoop()
        {
            while (true)
            {
                List<StickyNoteData> snapshot;
                long generation;
                lock (_saveGate)
                {
                    if (_latestSnapshot == null ||
                        _latestGeneration <= _lastWrittenGeneration)
                    {
                        _writerRunning = false;
                        Monitor.PulseAll(_saveGate);
                        return;
                    }
                    snapshot = _latestSnapshot;
                    generation = _latestGeneration;
                }
                PersistenceResult result = WriteSnapshot(_filePath, snapshot,
                    generation, "sticky-notes-save-async");
                int consecutiveFailures = 0;
                lock (_saveGate)
                {
                    if (result.Succeeded)
                    {
                        _completedGeneration = Math.Max(_completedGeneration,
                            generation);
                        if (generation == _requestedGeneration)
                        {
                            _hasUnsavedChanges = false;
                            _lastSaveError = null;
                            _consecutiveSaveFailures = 0;
                        }
                    }
                    else
                    {
                        _lastSaveError = result.Error;
                        _consecutiveSaveFailures++;
                        consecutiveFailures = _consecutiveSaveFailures;
                    }
                }
                if (!result.Succeeded)
                {
                    NotifySaveFailed(result, consecutiveFailures);
                    lock (_saveGate)
                    {
                        _writerRunning = false;
                        _latestSnapshot = null;
                        Monitor.PulseAll(_saveGate);
                    }
                    return;
                }
            }
        }

        internal PersistenceResult SaveToFile(string filePath)
        {
            // Never create a snapshot or generation for a repository whose
            // on-disk schema this reader cannot safely own.
            if (!_loadSucceeded)
                return RejectBlockedSave("save");
            long generation;
            List<StickyNoteData> snapshot;
            lock (_saveGate)
            {
                generation = ++_requestedGeneration;
                _latestSnapshot = null;
                _latestGeneration = generation;
                _hasUnsavedChanges = true;
                StickyDockGroups.NormalizeAll(_notes);
                snapshot = CloneNotes(_notes);
            }
            PersistenceResult result = WriteSnapshot(filePath, snapshot,
                generation, "sticky-notes-save");
            if (result.Succeeded)
            {
                lock (_saveGate)
                {
                    _completedGeneration = Math.Max(_completedGeneration,
                        generation);
                    if (generation == _requestedGeneration)
                    {
                        _hasUnsavedChanges = false;
                        _lastSaveError = null;
                        _consecutiveSaveFailures = 0;
                    }
                }
                return result;
            }
            return RecordSaveFailure(result.Error);
        }

        internal PersistenceResult ExportSnapshot(string filePath)
        {
            if (!_loadSucceeded)
                return PersistenceResult.Failure(
                    CreateMutationBlockedError("export"));
            try
            {
                // Export is a detached read; normalizing the live repository
                // here could silently change current workspace ownership.
                List<StickyNoteData> snapshot = CloneNotes(_notes);
                StickyDockGroups.NormalizeAll(snapshot);
                List<string> lines = SerializeSnapshot(snapshot);
                AtomicTextFile.WriteAllLines(filePath, lines, false);
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-notes-emergency-export", error);
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
            return CommitPreparedSnapshot(committed, backupPath,
                "sticky-notes-import-merge");
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
            return CommitPreparedSnapshot(committed, backupPath,
                "sticky-notes-full-restore");
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
            List<StickyNoteData> committed, string backupPath,
            string diagnosticContext)
        {
            string primaryPath = Path.GetFullPath(_filePath);
            string automaticBackupPath = Path.GetFullPath(backupPath);
            if (String.Equals(primaryPath, automaticBackupPath,
                StringComparison.OrdinalIgnoreCase))
                return PersistenceResult.Failure(new InvalidOperationException(
                    "Automatic backup path must differ from the data file."));

            PersistenceResult pendingSaves = WaitForPendingSaves();
            if (!pendingSaves.Succeeded) return pendingSaves;

            long generation;
            List<StickyNoteData> currentSnapshot;
            lock (_saveGate)
            {
                generation = ++_requestedGeneration;
                _latestSnapshot = null;
                _latestGeneration = generation;
                _hasUnsavedChanges = true;
                currentSnapshot = CloneNotes(_notes);
            }

            PersistenceResult backupResult;
            lock (_ioGate)
            {
                try
                {
                    // One rolling pre-change backup is deliberate: it protects
                    // the current dataset without accumulating unbounded files.
                    AtomicTextFile.WriteAllLines(automaticBackupPath,
                        SerializeSnapshot(currentSnapshot), false);
                }
                catch (Exception error)
                {
                    backupResult = PersistenceResult.Failure(error);
                    ApplicationDiagnostics.ReportNonFatal(
                        diagnosticContext + "-backup", error);
                    return RecordSaveFailure(backupResult.Error);
                }
                backupResult = WriteSnapshot(_filePath, committed,
                    generation, diagnosticContext);
            }
            if (!backupResult.Succeeded)
                return RecordSaveFailure(backupResult.Error);

            lock (_saveGate)
            {
                _notes.Clear();
                foreach (StickyNoteData note in committed)
                    _notes.Add(note.CloneForPersistence());
                _completedGeneration = Math.Max(_completedGeneration,
                    generation);
                if (generation == _requestedGeneration)
                {
                    _hasUnsavedChanges = false;
                    _lastSaveError = null;
                    _consecutiveSaveFailures = 0;
                }
            }
            return PersistenceResult.Success();
        }

        internal PersistenceResult CommitImportedMerge(
            StickyImportMergeResult merge)
        {
            // Keep one rolling rollback snapshot so repeated imports never
            // create an unbounded trail of automatic backup files.
            return CommitImportedMerge(merge,
                _filePath + ".before-import.pennysticky");
        }

        private PersistenceResult RecordSaveFailure(Exception error)
        {
            _hasUnsavedChanges = true;
            _lastSaveError = error;
            _consecutiveSaveFailures++;
            PersistenceResult result = PersistenceResult.Failure(error);
            NotifySaveFailed(result, _consecutiveSaveFailures);
            return result;
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
            _lastSaveError = error;
            _consecutiveSaveFailures++;
            PersistenceResult result = PersistenceResult.Failure(error);
            NotifySaveFailed(result, _consecutiveSaveFailures);
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
            if (snapshot != null)
                foreach (StickyNoteData note in snapshot)
                    if (note != null) lines.Add(StickyNoteCodec.SerializeLine(note));
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

        private PersistenceResult WriteSnapshot(string filePath,
            List<StickyNoteData> snapshot, long generation,
            string diagnosticContext)
        {
            lock (_ioGate)
            {
                // A newer writer may have won the IO gate after this snapshot
                // was captured. Never let an older generation move disk state
                // backwards.
                lock (_saveGate)
                    if (generation < _lastWrittenGeneration)
                        return PersistenceResult.Success();
                try
                {
                    StickyDockGroups.NormalizeAll(snapshot);
                    List<string> lines = new List<string>();
                    foreach (StickyNoteData note in snapshot)
                        lines.Add(StickyNoteCodec.SerializeLine(note));
                    AtomicTextFile.WriteAllLines(filePath, lines, true);
                    lock (_saveGate)
                        _lastWrittenGeneration = Math.Max(
                            _lastWrittenGeneration, generation);
                    return PersistenceResult.Success();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        diagnosticContext, error);
                    return PersistenceResult.Failure(error);
                }
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
