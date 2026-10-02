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

        private sealed class StickyPersistenceCheckResult
        {
            internal string FilePath;
            internal StickyFeature Repository;
            internal StickyNoteData RestoredNote;
            internal bool PersistenceOk;
            internal bool FailureDirtyRetryOk;
            internal bool GenerationMonotonicOk;
            internal bool PendingSaveWaitBoundedOk;
            internal bool CurrentBackupRoundTripOk;
            internal bool ImportMergeCommitOk;
            internal bool FullRestoreCommitOk;
            internal bool MultilingualOk;
            internal bool RichTextOk;
            internal bool RichTextNoSilentTruncationOk;
            internal bool TodoOk;
            internal bool ScheduleOk;
        }

        private static StickyPersistenceCheckResult
            RunStickyPersistenceChecks(string outputPath)
        {
            StickyPersistenceCheckResult result =
                new StickyPersistenceCheckResult
                {
                    FilePath = outputPath + ".sticky-test.dat"
                };
            StickyFeature stickyRepository =
                StickyFeature.LoadFromFile(result.FilePath);
            const string multilingualSample =
                "English line\n日本語 한국어 Русский العربية Français";
            StickyNoteData sticky = stickyRepository.Create(multilingualSample,
                new Point(120, 160));
            sticky.Title = "多语言 Note 日本語";
            sticky.IsTodoList = true;
            sticky.TodoItems.Add(new StickyTodoItem("整理会议记录", false));
            sticky.TodoItems.Add(new StickyTodoItem("给家人回电话", true));
            sticky.ColorArgb = Color.LightBlue.ToArgb();
            sticky.BackgroundOpacityPercent = 60;
            sticky.TextColorArgb = Color.White.ToArgb();
            sticky.AlwaysOnTop = false;
            sticky.Width = 360;
            sticky.Height = 260;
            sticky.ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks;
            using (RichTextBox richSource = new RichTextBox())
            using (Font richFont = new Font("Microsoft YaHei UI", 14F,
                FontStyle.Bold | FontStyle.Italic | FontStyle.Underline))
            {
                richSource.Text = sticky.Text;
                richSource.SelectAll();
                richSource.SelectionFont = richFont;
                sticky.RichTextRtf = richSource.Rtf;
            }
            stickyRepository.SaveToFile(result.FilePath);
            result.Repository = StickyFeature.LoadFromFile(
                result.FilePath);
            List<StickyNoteData> restoredNotes = result.Repository.GetAll();

            string currentBackupPath = outputPath +
                ".sticky-current-backup-test.pennysticky";
            try
            {
                PersistenceResult exported = result.Repository.ExportSnapshot(
                    currentBackupPath);
                StickyImportValidationResult imported =
                    StickyBackupFileReader.Read(currentBackupPath);
                result.CurrentBackupRoundTripOk = exported.Succeeded &&
                    imported.Succeeded && imported.Notes.Count ==
                    restoredNotes.Count && imported.Notes.Count == 1 &&
                    StickyImportMergePlanner.PersistedContentEquals(
                        restoredNotes[0], imported.Notes[0]);
            }
            finally
            {
                if (File.Exists(currentBackupPath))
                    File.Delete(currentBackupPath);
                if (File.Exists(currentBackupPath + ".bak"))
                    File.Delete(currentBackupPath + ".bak");
            }

            using (var entered = new System.Threading.ManualResetEventSlim())
            using (var release = new System.Threading.ManualResetEventSlim())
            {
                var writer = new PersistenceWriter<StickyWriteRequest>(delegate(StickyWriteRequest request)
                {
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5)))
                        return PersistenceResult.Failure(new TimeoutException());
                    return PersistenceResult.Success();
                });
                writer.Enqueue(new StickyWriteRequest(new List<StickyNoteData>()));
                bool started = entered.Wait(TimeSpan.FromSeconds(5));
                Stopwatch waitTimer = Stopwatch.StartNew();
                PersistenceResult timedOut = writer.Flush(TimeSpan.FromMilliseconds(25));
                waitTimer.Stop();
                release.Set();
                result.PendingSaveWaitBoundedOk = started && !timedOut.Succeeded &&
                    timedOut.Error is TimeoutException &&
                    waitTimer.Elapsed < TimeSpan.FromSeconds(1) &&
                    writer.Flush(TimeSpan.FromSeconds(5)).Succeeded;
            }

            string persistenceStatePath = outputPath +
                ".persistence-state-test.dat";
            StickyFeature persistenceStateRepository =
                StickyFeature.LoadFromFile(persistenceStatePath);
            persistenceStateRepository.Create("dirty-state", Point.Empty);
            File.Delete(persistenceStatePath);
            Directory.CreateDirectory(persistenceStatePath);
            PersistenceResult failedSave = persistenceStateRepository.Save();
            result.FailureDirtyRetryOk = !failedSave.Succeeded &&
                persistenceStateRepository.HasUnsavedChanges &&
                persistenceStateRepository.LastSaveError != null;
            Directory.Delete(persistenceStatePath);
            PersistenceResult recoveredSave = persistenceStateRepository.Save();
            result.FailureDirtyRetryOk = result.FailureDirtyRetryOk &&
                recoveredSave.Succeeded &&
                !persistenceStateRepository.HasUnsavedChanges;
            if (File.Exists(persistenceStatePath))
                File.Delete(persistenceStatePath);
            if (File.Exists(persistenceStatePath + ".bak"))
                File.Delete(persistenceStatePath + ".bak");

            string generationPath = outputPath + ".generation-test.dat";
            StickyFeature generationRepository =
                StickyFeature.LoadFromFile(generationPath);
            StickyNoteData generationNote = generationRepository.CreateDraft(
                "older-snapshot", Point.Empty);
            generationRepository.SaveAsync();
            generationNote.Text = "newer-snapshot";
            PersistenceResult finalWrite = generationRepository.Save();
            List<StickyNoteData> generationRestored = StickyFeature
                .LoadFromFile(generationPath).GetAll();
            result.GenerationMonotonicOk = finalWrite.Succeeded &&
                generationRestored.Count == 1 &&
                generationRestored[0].Text == "newer-snapshot" &&
                !generationRepository.HasUnsavedChanges;
            if (File.Exists(generationPath)) File.Delete(generationPath);
            if (File.Exists(generationPath + ".bak"))
                File.Delete(generationPath + ".bak");

            string mergePath = outputPath + ".sticky-import-merge-test.dat";
            string mergeBackupPath = mergePath + ".before-import.pennysticky";
            try
            {
                StickyFeature mergeRepository =
                    StickyFeature.LoadFromFile(mergePath);
                StickyNoteData currentVersion = mergeRepository.Create(
                    "current-version", Point.Empty);
                mergeRepository.SaveToFile(mergePath);
                StickyNoteData importedVersion =
                    currentVersion.CloneForPersistence();
                importedVersion.Text = "imported-version";
                StickyNoteData importedNew = new StickyNoteData
                {
                    Id = "imported-new",
                    Text = "new-note"
                };
                StickyImportMergeResult mergePlan =
                    StickyImportMergePlanner.Calculate(
                        mergeRepository.GetAll(), new[]
                        {
                            importedVersion, importedNew
                        });
                PersistenceResult mergeCommit =
                    mergeRepository.CommitImportedMerge(mergePlan);
                StickyFeature reopenedMerge =
                    StickyFeature.LoadFromFile(mergePath);
                StickyFeature preMergeBackup =
                    StickyFeature.LoadFromFile(mergeBackupPath);
                int conflictCopies = reopenedMerge.GetAll().Count - 2;
                result.ImportMergeCommitOk = mergeCommit.Succeeded &&
                    reopenedMerge.GetAll().Count == 3 && conflictCopies == 1 &&
                    reopenedMerge.Find(currentVersion.Id).Text ==
                        "current-version" &&
                    reopenedMerge.Find("imported-new") != null &&
                    !reopenedMerge.Find("imported-new").Visible &&
                    preMergeBackup.Find(currentVersion.Id) != null &&
                    preMergeBackup.Find(currentVersion.Id).Text ==
                        "current-version";

                string blockedPath = outputPath +
                    ".sticky-import-blocked-directory";
                string blockedBackupPath = blockedPath +
                    ".before-import.pennysticky";
                try
                {
                    if (File.Exists(blockedPath))
                        File.Delete(blockedPath);
                    if (Directory.Exists(blockedPath))
                        Directory.Delete(blockedPath, true);
                    Directory.CreateDirectory(blockedPath);
                    StickyFeature blockedRepository =
                        StickyFeature.LoadFromFile(blockedPath);
                    StickyNoteData blockedCurrent = blockedRepository.Create(
                        "blocked-current", Point.Empty);
                    StickyNoteData blockedIncoming =
                        blockedCurrent.CloneForPersistence();
                    blockedIncoming.Text = "blocked-import";
                    StickyImportMergeResult blockedPlan =
                        StickyImportMergePlanner.Calculate(
                            blockedRepository.GetAll(), new[]
                            {
                                blockedIncoming
                            });
                    PersistenceResult failedMerge = blockedRepository
                        .CommitImportedMerge(blockedPlan);
                    StickyNoteData blockedAfter = blockedRepository.Find(
                        blockedCurrent.Id);
                    result.ImportMergeCommitOk = result.ImportMergeCommitOk &&
                        !failedMerge.Succeeded && blockedAfter != null &&
                        blockedAfter.Text == "blocked-current" &&
                        blockedRepository.Count == 1 &&
                        File.Exists(blockedBackupPath);
                }
                finally
                {
                    foreach (string blockedFile in new string[] {
                        blockedBackupPath, blockedPath + ".tmp" })
                        if (File.Exists(blockedFile)) File.Delete(blockedFile);
                    if (Directory.Exists(blockedPath))
                        Directory.Delete(blockedPath, true);
                    if (File.Exists(blockedPath)) File.Delete(blockedPath);
                }
            }
            finally
            {
                foreach (string mergeFile in new string[] {
                    mergePath, mergePath + ".bak", mergeBackupPath,
                    mergeBackupPath + ".bak" })
                    if (File.Exists(mergeFile)) File.Delete(mergeFile);
            }

            string restorePath = outputPath + ".sticky-full-restore-test.dat";
            string restoreBackupPath = restorePath + ".before-restore.pennysticky";
            try
            {
                StickyFeature restoreRepository =
                    StickyFeature.LoadFromFile(restorePath);
                StickyNoteData restoreCurrent = restoreRepository.Create(
                    "restore-current", Point.Empty);
                restoreRepository.SaveToFile(restorePath);
                StickyNoteData replacement = new StickyNoteData
                {
                    Id = "restored-only",
                    Text = "restored-content"
                };
                PersistenceResult restoreCommit =
                    restoreRepository.CommitFullRestore(
                        new[] { replacement }, restoreBackupPath);
                StickyFeature reopenedRestore =
                    StickyFeature.LoadFromFile(restorePath);
                StickyFeature preRestore =
                    StickyFeature.LoadFromFile(restoreBackupPath);
                result.FullRestoreCommitOk = restoreCommit.Succeeded &&
                    reopenedRestore.Count == 1 &&
                    reopenedRestore.Find("restored-only") != null &&
                    reopenedRestore.Find(restoreCurrent.Id) == null &&
                    preRestore.Find(restoreCurrent.Id) != null &&
                    preRestore.Find(restoreCurrent.Id).Text ==
                        "restore-current";
            }
            catch
            {
                result.FullRestoreCommitOk = false;
            }
            finally
            {
                foreach (string restoreFile in new string[] {
                    restorePath, restorePath + ".bak", restoreBackupPath,
                    restoreBackupPath + ".bak" })
                    if (File.Exists(restoreFile)) File.Delete(restoreFile);
            }

            result.PersistenceOk = restoredNotes.Count == 1 &&
                restoredNotes[0].Text == multilingualSample &&
                restoredNotes[0].Title == "多语言 Note 日本語" &&
                restoredNotes[0].ColorArgb == Color.LightBlue.ToArgb() &&
                restoredNotes[0].BackgroundOpacityPercent == 60 &&
                restoredNotes[0].TextColorArgb == Color.White.ToArgb() &&
                !restoredNotes[0].AlwaysOnTop &&
                restoredNotes[0].Width == 360 &&
                restoredNotes[0].Height == 260 &&
                restoredNotes[0].ReminderUtc.HasValue;
            result.RestoredNote = restoredNotes[0];
            result.MultilingualOk = result.PersistenceOk &&
                result.RestoredNote.SearchText.IndexOf("日本語",
                    StringComparison.Ordinal) >= 0 &&
                result.RestoredNote.SearchText.IndexOf("한국어",
                    StringComparison.Ordinal) >= 0 &&
                result.RestoredNote.SearchText.IndexOf("العربية",
                    StringComparison.Ordinal) >= 0;
            using (RichTextBox richRestored = new RichTextBox())
            {
                try
                {
                    richRestored.Rtf = result.RestoredNote.RichTextRtf;
                    richRestored.Select(0, "English line".Length);
                    Font restoredFont = richRestored.SelectionFont;
                    result.RichTextOk = richRestored.Text ==
                        result.RestoredNote.Text && restoredFont != null &&
                        restoredFont.Bold && restoredFont.Italic &&
                        restoredFont.Underline &&
                        Math.Abs(restoredFont.SizeInPoints - 14F) < 0.2F;
                }
                catch { result.RichTextOk = false; }
            }

            string longStickyPath = outputPath +
                ".sticky-long-content-test.dat";
            if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
            if (File.Exists(longStickyPath + ".bak"))
                File.Delete(longStickyPath + ".bak");
            string longVisibleText = new string('长', 13050) + "结尾保留";
            StickyFeature longRepository =
                StickyFeature.LoadFromFile(longStickyPath);
            StickyNoteData longNote = longRepository.Create(longVisibleText,
                Point.Empty);
            using (RichTextBox longRichText = new RichTextBox())
            {
                longRichText.Text = longVisibleText;
                longNote.RichTextRtf = longRichText.Rtf;
            }
            longRepository.SaveToFile(longStickyPath);
            List<StickyNoteData> longRestored = StickyFeature
                .LoadFromFile(longStickyPath).GetAll();
            string rtfAboveOldLimit = "{\\rtf1\\ansi " +
                new string('x', 350000) + "}";
            result.RichTextNoSilentTruncationOk =
                longRestored.Count == 1 &&
                longRestored[0].Text == longVisibleText &&
                longRestored[0].Text.EndsWith("结尾保留",
                    StringComparison.Ordinal) &&
                StickyNoteCodec.NormalizeRtf(rtfAboveOldLimit) ==
                    rtfAboveOldLimit;
            if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
            if (File.Exists(longStickyPath + ".bak"))
                File.Delete(longStickyPath + ".bak");
            result.TodoOk = result.PersistenceOk &&
                result.RestoredNote.IsTodoList &&
                result.RestoredNote.TodoItems.Count == 2 &&
                result.RestoredNote.TodoItems[0].Text == "整理会议记录" &&
                !result.RestoredNote.TodoItems[0].Completed &&
                result.RestoredNote.TodoItems[1].Text == "给家人回电话" &&
                result.RestoredNote.TodoItems[1].Completed;

            string scheduleTestPath = outputPath + ".schedule-test.dat";
            StickyFeature scheduleRepository =
                StickyFeature.LoadFromFile(scheduleTestPath);
            StickyNoteData scheduleNote = scheduleRepository.Create(
                String.Empty, new Point(210, 180));
            scheduleNote.IsSchedule = true;
            scheduleNote.IsTodoList = false;
            scheduleNote.Title = "日程";
            scheduleNote.FontSizeTwips = 320;
            scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                "参加画展", DateTime.Today.AddDays(6), true));
            scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                "朋友生日", DateTime.Today.AddDays(58)));
            scheduleRepository.SaveToFile(scheduleTestPath);
            List<StickyNoteData> restoredSchedules = StickyFeature
                .LoadFromFile(scheduleTestPath).GetAll();
            result.ScheduleOk = restoredSchedules.Count == 1 &&
                restoredSchedules[0].IsSchedule &&
                !restoredSchedules[0].IsTodoList &&
                restoredSchedules[0].ScheduleItems.Count == 2 &&
                restoredSchedules[0].ScheduleItems[0].Text == "参加画展" &&
                restoredSchedules[0].ScheduleItems[0].IsPinned &&
                !restoredSchedules[0].ScheduleItems[1].IsPinned &&
                restoredSchedules[0].ScheduleItems[0].TargetDate ==
                    DateTime.Today.AddDays(6) &&
                restoredSchedules[0].SearchText.IndexOf("朋友生日",
                    StringComparison.Ordinal) >= 0;
            if (File.Exists(scheduleTestPath)) File.Delete(scheduleTestPath);
            if (File.Exists(scheduleTestPath + ".bak"))
                File.Delete(scheduleTestPath + ".bak");
            return result;
        }

        private static bool RunStickyBackupCleanupCheck(
            StickyPersistenceCheckResult stickyChecks)
        {
            bool backupExists = File.Exists(stickyChecks.FilePath + ".bak");
            if (File.Exists(stickyChecks.FilePath))
                File.Delete(stickyChecks.FilePath);
            if (File.Exists(stickyChecks.FilePath + ".bak"))
                File.Delete(stickyChecks.FilePath + ".bak");
            return backupExists;
        }
    }
}
