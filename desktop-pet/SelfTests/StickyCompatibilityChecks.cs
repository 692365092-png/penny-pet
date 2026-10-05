using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private sealed class StickyCompatibilityCheckResult
        {
            internal bool LegacyMigrationOk;
            internal bool OldestFolderCacheImportOk;
            internal bool VersionFourMigrationOk;
            internal bool AncientCacheDisplayRepairOk;
            internal bool FailedLoadNeverOverwritesOk;
            internal bool BackupRecoveryOk;
            internal bool FuturePrimaryBlocksStartupOk;
            internal bool FutureFailureClassificationOk;
            internal bool FutureNoSalvageOk;
            internal bool FutureOlderBackupNotLoadedOk;
            internal bool FutureRepositoryReadOnlyOk;
            internal bool FutureSyncSaveRejectedOk;
            internal bool FutureAsyncSaveRejectedOk;
            internal bool FutureMutationsRejectedOk;
            internal bool FuturePrimaryBytesUnchangedOk;
            internal bool FutureBackupBytesUnchangedOk;
            internal bool FutureNoRecoveryArtifactsOk;
            internal bool HistoricalStartupMatrixOk;
            internal bool CurrentStartupRoundTripOk;
            internal bool FutureUserMessageOk;
        }

        private static StickyCompatibilityCheckResult
            RunStickyCompatibilityChecks(string outputPath)
        {
            StickyCompatibilityCheckResult result =
                new StickyCompatibilityCheckResult();
            string legacyChinese = "旧版中文便利贴";
            string legacyLine = String.Join("|", new string[] {
                "1", "legacy-note", "1", "1", Color.LightYellow.ToArgb().ToString(),
                "10", "20", "280", "230", DateTime.UtcNow.Ticks.ToString(),
                DateTime.UtcNow.Ticks.ToString(), "0",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyChinese))
            });
            string legacyStickyPath = outputPath + ".sticky-v1-test.dat";
            File.WriteAllText(legacyStickyPath, legacyLine,
                new UTF8Encoding(false));
            List<StickyNoteData> legacyNotes =
                StickyFeature.LoadFromFile(legacyStickyPath).GetAll();
            result.LegacyMigrationOk = legacyNotes.Count == 1 &&
                legacyNotes[0].Text == legacyChinese &&
                !legacyNotes[0].IsTodoList &&
                String.IsNullOrEmpty(legacyNotes[0].RichTextRtf);
            if (File.Exists(legacyStickyPath)) File.Delete(legacyStickyPath);

            string legacyImportCurrent = outputPath +
                ".legacy-import-current.dat";
            string legacyImportSource = outputPath +
                ".legacy-import-source.dat";
            File.WriteAllText(legacyImportSource, legacyLine,
                new UTF8Encoding(false));
            StickyFeature legacyImported = StickyFeature
                .LoadFromFileWithLegacyCandidates(legacyImportCurrent,
                    new string[] { legacyImportSource });
            result.OldestFolderCacheImportOk = legacyImported.Count == 1 &&
                legacyImported.GetAll()[0].Text == legacyChinese &&
                File.Exists(legacyImportCurrent) &&
                File.Exists(legacyImportSource);
            foreach (string legacyImportFile in new string[] {
                legacyImportCurrent, legacyImportCurrent + ".bak",
                legacyImportSource, legacyImportSource + ".bak" })
                if (File.Exists(legacyImportFile))
                    File.Delete(legacyImportFile);

            string versionFourStickyPath = outputPath +
                ".sticky-v4-test.dat";
            string versionFourText = "第四版便签兼容测试";
            string versionFourLine = String.Join("|", new string[] {
                "4", "legacy-v4-note", "1", "1",
                Color.LightYellow.ToArgb().ToString(), "10", "20",
                "280", "230", DateTime.UtcNow.Ticks.ToString(),
                DateTime.UtcNow.Ticks.ToString(), "0", "0",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("旧便签")),
                String.Empty,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(versionFourText)),
                "0", String.Empty
            });
            File.WriteAllText(versionFourStickyPath, versionFourLine,
                new UTF8Encoding(false));
            StickyFeature versionFourRepository =
                StickyFeature.LoadFromFile(versionFourStickyPath);
            List<StickyNoteData> versionFourNotes =
                versionFourRepository.GetAll();
            result.VersionFourMigrationOk = versionFourNotes.Count == 1 &&
                versionFourNotes[0].Text == versionFourText &&
                versionFourNotes[0].FontFamilyName == "Microsoft YaHei UI" &&
                versionFourNotes[0].FontSizeTwips == 210;
            StickyNoteData ancientDisplayData = new StickyNoteData();
            ancientDisplayData.Width = -100;
            ancientDisplayData.Height = Int32.MaxValue;
            ancientDisplayData.FontFamilyName = null;
            ancientDisplayData.RichTextRtf = "not rtf";
            ancientDisplayData.BackgroundOpacityPercent = -1;
            ancientDisplayData.IsTodoList = true;
            ancientDisplayData.IsSchedule = true;
            result.AncientCacheDisplayRepairOk =
                StickyNoteCodec.RepairForDisplay(
                    ancientDisplayData, true) &&
                ancientDisplayData.Width == 280 &&
                ancientDisplayData.Height == 700 &&
                ancientDisplayData.FontFamilyName == "Microsoft YaHei UI" &&
                String.IsNullOrEmpty(ancientDisplayData.RichTextRtf) &&
                ancientDisplayData.BackgroundOpacityPercent == 10 &&
                ancientDisplayData.IsSchedule &&
                !ancientDisplayData.IsTodoList &&
                !ancientDisplayData.Visible;
            versionFourRepository.SaveToFile(versionFourStickyPath);
            result.VersionFourMigrationOk = result.VersionFourMigrationOk &&
                File.ReadAllText(versionFourStickyPath, Encoding.UTF8)
                    .StartsWith(StickyNoteCodec.CurrentVersion + "|");
            if (File.Exists(versionFourStickyPath))
                File.Delete(versionFourStickyPath);
            if (File.Exists(versionFourStickyPath + ".bak"))
                File.Delete(versionFourStickyPath + ".bak");

            string corruptStickyPath = outputPath +
                ".sticky-corrupt-test.dat";
            File.WriteAllText(corruptStickyPath, "this-is-not-a-note",
                new UTF8Encoding(false));
            StickyFeature corruptRepository =
                StickyFeature.LoadFromFile(corruptStickyPath);
            string preservedCorruptPath = corruptRepository.RecoveryBackupPath;
            StickyNoteData recoveredCreate = corruptRepository.Create(
                "损坏数据恢复后仍可新建", Point.Empty);
            result.FailedLoadNeverOverwritesOk =
                corruptRepository.LoadSucceeded &&
                corruptRepository.RecoveredFromLoadFailure &&
                recoveredCreate != null &&
                !String.IsNullOrEmpty(preservedCorruptPath) &&
                File.Exists(preservedCorruptPath) &&
                File.ReadAllText(preservedCorruptPath, Encoding.UTF8) ==
                    "this-is-not-a-note" &&
                File.ReadAllText(corruptStickyPath, Encoding.UTF8)
                    .StartsWith(StickyNoteCodec.CurrentVersion + "|");
            if (File.Exists(corruptStickyPath)) File.Delete(corruptStickyPath);
            if (File.Exists(corruptStickyPath + ".bak"))
                File.Delete(corruptStickyPath + ".bak");
            if (!String.IsNullOrEmpty(preservedCorruptPath) &&
                File.Exists(preservedCorruptPath))
                File.Delete(preservedCorruptPath);

            string backupRecoveryPath = outputPath +
                ".sticky-backup-recovery-test.dat";
            File.WriteAllText(backupRecoveryPath, "broken-primary",
                new UTF8Encoding(false));
            File.WriteAllText(backupRecoveryPath + ".bak", legacyLine,
                new UTF8Encoding(false));
            StickyFeature backupRecoveryRepository =
                StickyFeature.LoadFromFile(backupRecoveryPath);
            result.BackupRecoveryOk =
                backupRecoveryRepository.LoadSucceeded &&
                backupRecoveryRepository.RecoveredFromLoadFailure &&
                backupRecoveryRepository.GetAll().Count == 1 &&
                backupRecoveryRepository.GetAll()[0].Text == legacyChinese &&
                File.ReadAllText(backupRecoveryRepository.RecoveryBackupPath,
                    Encoding.UTF8) == "broken-primary" &&
                File.ReadAllText(backupRecoveryPath, Encoding.UTF8)
                    .StartsWith(StickyNoteCodec.CurrentVersion + "|");
            string preservedBackupPrimary =
                backupRecoveryRepository.RecoveryBackupPath;
            if (File.Exists(backupRecoveryPath))
                File.Delete(backupRecoveryPath);
            if (File.Exists(backupRecoveryPath + ".bak"))
                File.Delete(backupRecoveryPath + ".bak");
            if (!String.IsNullOrEmpty(preservedBackupPrimary) &&
                File.Exists(preservedBackupPrimary))
                File.Delete(preservedBackupPrimary);
            RunStickyFutureSchemaChecks(result, outputPath);
            return result;
        }

        private static void RunStickyFutureSchemaChecks(
            StickyCompatibilityCheckResult result, string outputPath)
        {
            result.HistoricalStartupMatrixOk = true;
            for (int version = 1; version <= StickyNoteCodec.CurrentVersion;
                version++)
            {
                string path = outputPath + ".sticky-startup-v" + version +
                    "-test.dat";
                try
                {
                    File.WriteAllText(path, ReadStickyFixture(
                        "sticky-v" + version + ".txt"),
                        new UTF8Encoding(false));
                    StickyFeature historical =
                        StickyFeature.LoadFromFile(path);
                    result.HistoricalStartupMatrixOk =
                        result.HistoricalStartupMatrixOk &&
                        historical.LoadSucceeded &&
                        !historical.IsFutureSchemaBlocked &&
                        historical.Count == 1;
                }
                finally
                {
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(path + ".bak"))
                        File.Delete(path + ".bak");
                }
            }

            string currentPath = outputPath +
                ".sticky-current-startup-test.dat";
            try
            {
                StickyNoteData current = new StickyNoteData
                {
                    Id = "current-startup",
                    Text = "current schema"
                };
                File.WriteAllText(currentPath,
                    StickyNoteCodec.SerializeLine(current),
                    new UTF8Encoding(false));
                StickyFeature currentRepository =
                    StickyFeature.LoadFromFile(currentPath);
                result.CurrentStartupRoundTripOk =
                    currentRepository.LoadSucceeded &&
                    !currentRepository.IsFutureSchemaBlocked &&
                    currentRepository.Find(current.Id) != null;
            }
            finally
            {
                if (File.Exists(currentPath)) File.Delete(currentPath);
                if (File.Exists(currentPath + ".bak"))
                    File.Delete(currentPath + ".bak");
            }

            string futurePath = outputPath +
                ".sticky-future-schema-test.dat";
            string backupPath = futurePath + ".bak";
            string exportPath = futurePath + ".export";
            int futureVersion = StickyNoteCodec.CurrentVersion + 1;
            try
            {
                string olderBackup = ReadStickyFixture("sticky-v10.txt");
                string futureLine = ReadStickyFixture("sticky-vFuture.txt")
                    .Replace("{VERSION}", futureVersion.ToString());
                // The valid older line ahead of the future line proves that
                // no partially parsed primary data can escape the preflight.
                File.WriteAllText(futurePath, olderBackup +
                    Environment.NewLine + futureLine,
                    new UTF8Encoding(false));
                File.WriteAllText(backupPath, olderBackup,
                    new UTF8Encoding(false));
                string primaryHash = CalculateSha256(futurePath);
                string backupHash = CalculateSha256(backupPath);

                StickyFeature blocked =
                    StickyFeature.LoadFromFile(futurePath);
                int rejectedSaveEvents = 0;
                blocked.SaveFailed += delegate { rejectedSaveEvents++; };
                UnsupportedStickySchemaException schemaError =
                    blocked.FutureSchemaError;
                result.FuturePrimaryBlocksStartupOk =
                    blocked.IsFutureSchemaBlocked &&
                    !blocked.LoadSucceeded && blocked.Count == 0 &&
                    blocked.DetectedFutureVersion == futureVersion;
                result.FutureFailureClassificationOk = schemaError != null &&
                    schemaError.DetectedVersion == futureVersion &&
                    schemaError.MaximumSupportedVersion ==
                        StickyNoteCodec.CurrentVersion &&
                    String.Equals(schemaError.SourcePath, futurePath,
                        StringComparison.OrdinalIgnoreCase);
                result.FutureNoSalvageOk =
                    !blocked.RecoveredFromLoadFailure &&
                    !blocked.RecoveredFromPartialSalvage &&
                    blocked.SalvagedNoteCount == 0 &&
                    blocked.SkippedCorruptLineCount == 0 &&
                    String.IsNullOrEmpty(blocked.RecoveryBackupPath);
                result.FutureOlderBackupNotLoadedOk =
                    blocked.Find("legacy-v10") == null && blocked.Count == 0;

                StickyNoteData rejectedCreate = blocked.Create(
                    "must not exist", Point.Empty);
                result.FutureRepositoryReadOnlyOk = rejectedCreate == null &&
                    !blocked.CanCreate && !blocked.Remove(new StickyNoteData()) &&
                    !blocked.HasUnsavedChanges;
                PersistenceResult syncSave = blocked.Save();
                result.FutureSyncSaveRejectedOk = !syncSave.Succeeded &&
                    syncSave.Error is InvalidOperationException &&
                    !blocked.HasUnsavedChanges && rejectedSaveEvents == 1;
                Exception syncSaveError = blocked.LastSaveError;
                blocked.SaveAsync();
                result.FutureAsyncSaveRejectedOk =
                    blocked.LastSaveError is InvalidOperationException &&
                    !Object.ReferenceEquals(syncSaveError,
                        blocked.LastSaveError) && rejectedSaveEvents == 2 &&
                    !blocked.HasPendingSaves && !blocked.HasUnsavedChanges;

                StickyNoteData incoming = new StickyNoteData
                {
                    Id = "blocked-import",
                    Text = "blocked"
                };
                StickyImportMergeResult merge = StickyImportMergePlanner
                    .Calculate(blocked.GetAll(), new[] { incoming });
                PersistenceResult mergeResult = WaitForPersistenceReceipt(blocked.CommitImportedMergeAsync(
                    merge, futurePath + ".before-import"));
                PersistenceResult restoreResult = WaitForPersistenceReceipt(blocked.CommitFullRestoreAsync(
                    new[] { incoming }, futurePath + ".before-restore"));
                PersistenceResult exportResult = blocked.ExportSnapshot(
                    exportPath);
                result.FutureMutationsRejectedOk =
                    !mergeResult.Succeeded && !restoreResult.Succeeded &&
                    !exportResult.Succeeded && !File.Exists(exportPath);

                result.FuturePrimaryBytesUnchangedOk =
                    primaryHash == CalculateSha256(futurePath);
                result.FutureBackupBytesUnchangedOk =
                    backupHash == CalculateSha256(backupPath);
                string directory = Path.GetDirectoryName(
                    Path.GetFullPath(futurePath));
                string name = Path.GetFileName(futurePath);
                result.FutureNoRecoveryArtifactsOk =
                    Directory.GetFiles(directory,
                        name + ".unreadable-*").Length == 0 &&
                    !File.Exists(futurePath + ".tmp") &&
                    !File.Exists(futurePath + ".before-import") &&
                    !File.Exists(futurePath + ".before-restore");

                string message = PennyApplicationHost
                    .BuildFutureSchemaBlockedMessage(schemaError);
                result.FutureUserMessageOk =
                    message.Contains("最多支持数据版本 v" +
                        StickyNoteCodec.CurrentVersion) &&
                    message.Contains("检测到的数据版本为 v" +
                        futureVersion) &&
                    message.Contains("不会读取或修改这些数据") &&
                    message.Contains("请关闭此版本并使用更新版本的 Penny");
            }
            finally
            {
                foreach (string path in new[] { futurePath, backupPath,
                    exportPath, futurePath + ".tmp",
                    futurePath + ".before-import",
                    futurePath + ".before-restore" })
                    if (File.Exists(path)) File.Delete(path);
                string directory = Path.GetDirectoryName(
                    Path.GetFullPath(futurePath));
                string name = Path.GetFileName(futurePath);
                foreach (string path in Directory.GetFiles(directory,
                    name + ".unreadable-*")) File.Delete(path);
            }
        }

        private static string ReadStickyFixture(string fileName)
        {
            string resourceName = "PennyPet.Tests.Fixtures." + fileName;
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException(
                        "Missing sticky fixture: " + resourceName);
                using (StreamReader reader = new StreamReader(stream,
                    Encoding.UTF8)) return reader.ReadToEnd().Trim();
            }
        }

        private static string CalculateSha256(string path)
        {
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(
                    hash.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}
