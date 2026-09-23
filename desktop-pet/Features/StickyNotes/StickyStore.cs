using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PennyPet
{
    internal sealed class StickyStore
    {
        private readonly string _filePath;
        private readonly PersistenceWriter<StickyWriteRequest> _writer;

        internal StickyStore(string filePath,
            Func<StickyWriteRequest, PersistenceResult> write = null)
        {
            _filePath = filePath;
            _writer = new PersistenceWriter<StickyWriteRequest>(write ?? WriteSnapshot);
        }

        internal event EventHandler<PersistenceFailedEventArgs> SaveFailed
        {
            add { _writer.Failed += value; }
            remove { _writer.Failed -= value; }
        }
        internal bool HasUnsavedChanges { get { return _writer.IsDirty; } }
        internal bool HasPendingSaves { get { return _writer.HasPending; } }
        internal Exception LastSaveError { get { return _writer.LastError; } }

        internal Task<PersistenceResult> Save(StickyWriteRequest request, bool coalesce = false)
        {
            return _writer.Enqueue(request, coalesce);
        }

        internal PersistenceResult Flush(TimeSpan timeout) { return _writer.Flush(timeout); }

        internal PersistenceResult ExportSnapshot(string filePath, IReadOnlyList<StickyNoteData> snapshot)
        {
            try
            {
                string destination = Path.GetFullPath(filePath);
                string primary = Path.GetFullPath(_filePath);
                if (String.Equals(destination, primary, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(destination, primary + ".bak", StringComparison.OrdinalIgnoreCase))
                    return PersistenceResult.Failure(new InvalidOperationException(
                        "Export must use a separate file, not the active data file or its backup."));
                // Emergency export never waits for the primary writer.
                AtomicTextFile.WriteAllLines(destination, SerializeSnapshot(snapshot), false);
                return PersistenceResult.Success();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("sticky-notes-export", error);
                return PersistenceResult.Failure(error);
            }
        }

        private static List<string> SerializeSnapshot(
            IEnumerable<StickyNoteData> snapshot)
        {
            List<string> lines = new List<string>();
            Dictionary<string, string> parents = StickyDockFileRelations.BuildLegacyParents(snapshot);
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

    }
}
