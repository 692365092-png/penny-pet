using System;
using System.Collections.Generic;
using System.Drawing;
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
        private string _recoveryBackupPath = String.Empty;
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

        public PersistenceResult Save()
        {
            return SaveToFile(_filePath);
        }

        internal void SaveAsync()
        {
            if (!_loadSucceeded)
            {
                NotifySaveFailed(PersistenceResult.Failure(
                    new InvalidOperationException(
                        "Sticky-note data was not loaded safely; refusing to overwrite it.")),
                    _consecutiveSaveFailures + 1);
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

        internal void WaitForPendingSaves()
        {
            while (HasPendingSaves) Thread.Sleep(10);
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
                    }
                    return;
                }
            }
        }

        internal PersistenceResult SaveToFile(string filePath)
        {
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
            // A temporary read/parse failure must never turn an existing note file
            // into an empty one. The next clean launch can read it again.
            if (!_loadSucceeded)
                return RecordSaveFailure(new InvalidOperationException(
                    "Sticky-note data was not loaded safely; refusing to overwrite it."));
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
            try
            {
                StickyDockGroups.NormalizeAll(_notes);
                List<string> lines = new List<string>();
                foreach (StickyNoteData note in _notes)
                    lines.Add(StickyNoteCodec.SerializeLine(note));
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
