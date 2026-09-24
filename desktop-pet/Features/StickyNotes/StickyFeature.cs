using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;

namespace PennyPet
{
    // Owner-side sticky commands and durable dataset publication. Model and Store
    // have independent responsibilities; the Windows partial attaches the UI runtime.
    internal sealed partial class StickyFeature
    {
        private readonly string _filePath;
        internal readonly StickyModel Model;
        private readonly StickyStore _store;
        private readonly SynchronizationContext _uiContext;
        private Exception _blockedSaveError;
        private int _blockedSaveFailures;

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed;

        internal StickyFeature(string filePath,
            Func<StickyWriteRequest, PersistenceResult> write = null)
            : this(new StickyStore(filePath, write), new StickyModel()) { }

        private StickyFeature(StickyStore store, StickyModel model)
        {
            Model = model;
            _filePath = store.FilePath;
            _uiContext = SynchronizationContext.Current;
            _store = store;
            _store.SaveFailed += delegate(object sender, PersistenceFailedEventArgs e)
            {
                NotifySaveFailed(e.Result, e.ConsecutiveFailures);
            };
        }

        private StickyFeature(StickyLoadResult loaded)
            : this(loaded.Store, loaded.Model) { }

        public static StickyFeature Load()
        {
            return new StickyFeature(StickyStore.Load());
        }

        internal static StickyFeature LoadFromFile(string filePath)
        {
            return new StickyFeature(StickyStore.LoadFromFile(filePath));
        }

        internal static StickyFeature LoadFromFileWithLegacyCandidates(
            string currentPath, IEnumerable<string> legacyCandidates)
        {
            return new StickyFeature(StickyStore.LoadFromFileWithLegacyCandidates(
                currentPath, legacyCandidates));
        }

        internal bool LoadSucceeded { get { return _store.LoadSucceeded; } }
        internal bool IsFutureSchemaBlocked { get { return _store.IsFutureSchemaBlocked; } }
        internal int DetectedFutureVersion { get { return _store.DetectedFutureVersion; } }
        internal UnsupportedStickySchemaException FutureSchemaError { get { return _store.FutureSchemaError; } }
        internal bool RecoveredFromLoadFailure { get { return _store.RecoveredFromLoadFailure; } }
        internal bool RecoveredFromPartialSalvage { get { return _store.RecoveredFromPartialSalvage; } }
        internal int SalvagedNoteCount { get { return _store.SalvagedNoteCount; } }
        internal int SkippedCorruptLineCount { get { return _store.SkippedCorruptLineCount; } }
        internal string RecoveryBackupPath { get { return _store.RecoveryBackupPath; } }

        internal int Count
        {
            get { return Model.Count; }
        }

        internal bool HasUnsavedChanges
        {
            get { return _store.HasUnsavedChanges; }
        }

        internal bool HasPendingSaves
        {
            get { return _store.HasPendingSaves; }
        }

        internal Exception LastSaveError
        {
            get { return _blockedSaveError ?? _store.LastSaveError; }
        }

        internal bool CanCreate
        {
            get { return CanCreateAtCount(LoadSucceeded, Model.Count); }
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
            return LoadSucceeded ? Model.CreateDraft(text, location.X, location.Y) : null;
        }

        internal IReadOnlyList<StickyNoteData> InStorageOrder { get { return Model.InStorageOrder; } }
        public List<StickyNoteData> GetAll() { return Model.GetAll(); }
        public List<StickyNoteData> GetHiddenInTabOrder() { return Model.GetHiddenInTabOrder(); }
        public StickyNoteData Find(string id) { return Model.Find(id); }

        public void ReorderHidden(StickyNoteData moved, int destinationIndex)
        {
            if (LoadSucceeded && Model.ReorderHidden(moved, destinationIndex)) SaveAsync();
        }

        public bool Remove(StickyNoteData note)
        {
            if (!LoadSucceeded || !Model.Remove(note)) return false;
            SaveAsync();
            return true;
        }

        public PersistenceResult Save()
        {
            return SaveToFile(_filePath);
        }

        internal void SaveAsync()
        {
            if (!LoadSucceeded)
            {
                RejectBlockedSave("save asynchronously");
                return;
            }
            _store.Save(new StickyWriteRequest(Model.CaptureSnapshot()), coalesce: true);
        }

        internal PersistenceResult WaitForPendingSaves()
        {
            return WaitForPendingSaves(TimeSpan.FromSeconds(5));
        }

        internal PersistenceResult WaitForPendingSaves(TimeSpan timeout)
        {
            return _store.Flush(timeout);
        }

        internal PersistenceResult SaveToFile(string filePath)
        {
            if (!LoadSucceeded) return RejectBlockedSave("save");
            try
            {
                // Saving a copy does not acknowledge the owned primary file.
                if (!String.Equals(Path.GetFullPath(filePath),
                    Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
                    return ExportSnapshot(filePath);
            }
            catch (Exception error) { return PersistenceResult.Failure(error); }
            return _store.Save(new StickyWriteRequest(Model.CaptureSnapshot()))
                .GetAwaiter().GetResult();
        }

        internal PersistenceResult ExportSnapshot(string filePath)
        {
            if (!LoadSucceeded)
                return PersistenceResult.Failure(CreateMutationBlockedError("export"));
            return _store.ExportSnapshot(filePath, Model.CaptureSnapshot());
        }

        internal PersistenceResult CommitImportedMerge(
            StickyImportMergeResult merge, string backupPath)
        {
            if (merge == null || String.IsNullOrWhiteSpace(backupPath))
                return PersistenceResult.Failure(new ArgumentException(
                    "A merge plan and automatic backup path are required."));
            if (!LoadSucceeded)
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
            if (!LoadSucceeded)
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
            PersistenceResult result = _store.Save(new StickyWriteRequest(
                committed, backupPath: automaticBackupPath,
                backupSnapshot: Model.CaptureSnapshot())).GetAwaiter().GetResult();
            if (!result.Succeeded) return result;

            // The model thread publishes the prepared dataset only after the
            // same writer has durably committed both the backup and primary.
            Model.ReplaceWith(committed);
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

        private static List<StickyNoteData> CloneAndValidateMergeSnapshot(
            IEnumerable<StickyNoteData> snapshot)
        {
            List<StickyNoteData> result = StickyModel.CloneSnapshot(snapshot);
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
            StickyDockFileRelations.Normalize(result);
            return result;
        }

    }
}
