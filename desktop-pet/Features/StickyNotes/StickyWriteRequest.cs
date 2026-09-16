using System.Collections.Generic;

namespace PennyPet
{
    internal sealed class StickyWriteRequest
    {
        internal readonly IReadOnlyList<StickyNoteData> Snapshot;
        internal readonly string BackupPath;
        internal readonly IReadOnlyList<StickyNoteData> BackupSnapshot;

        internal StickyWriteRequest(IReadOnlyList<StickyNoteData> snapshot,
            string backupPath = null,
            IReadOnlyList<StickyNoteData> backupSnapshot = null)
        {
            Snapshot = snapshot;
            BackupPath = backupPath;
            BackupSnapshot = backupSnapshot;
        }
    }
}
