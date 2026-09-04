using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        private DateTime _lastPersistenceWarningUtc = DateTime.MinValue;

        private void PersistenceSaveFailed(object sender,
            PersistenceFailedEventArgs e)
        {
            if (_persistenceRetryTimer != null &&
                !_persistenceRetryTimer.Enabled) _persistenceRetryTimer.Start();
            DateTime now = DateTime.UtcNow;
            if (!IsHandleCreated || IsDisposed || _exiting ||
                now - _lastPersistenceWarningUtc < TimeSpan.FromSeconds(30))
                return;
            _lastPersistenceWarningUtc = now;
            string dataName = Object.ReferenceEquals(sender, _settings)
                ? "设置" : "便利贴";
            ShowBubble(dataName +
                "尚未保存，Penny 会自动重试。请暂时不要退出。");
        }

        private void RetryUnsavedPersistence(object sender, EventArgs e)
        {
            bool hadUnsavedChanges = _notes.HasUnsavedChanges ||
                _settings.HasUnsavedChanges;
            if (!hadUnsavedChanges)
            {
                _persistenceRetryTimer.Stop();
                return;
            }
            if (_notes.HasUnsavedChanges && !_notes.Save().Succeeded) return;
            if (_settings.HasUnsavedChanges && !_settings.Save().Succeeded) return;
            if (_notes.HasUnsavedChanges || _settings.HasUnsavedChanges) return;
            _persistenceRetryTimer.Stop();
            if (!_exiting) ShowBubble("未保存的数据已重新写入磁盘。");
        }

        private bool FlushPersistenceBeforeExit()
        {
            bool notesResolved = false;
            bool settingsResolved = false;
            while (true)
            {
                PersistenceResult pendingSaves = notesResolved
                    ? PersistenceResult.Success()
                    : _notes.WaitForPendingSaves();
                PersistenceResult noteResult = notesResolved
                    ? PersistenceResult.Success()
                    : pendingSaves.Succeeded ? _notes.Save() : pendingSaves;
                PersistenceResult settingsResult = settingsResolved
                    ? PersistenceResult.Success() : _settings.Save();
                if (noteResult.Succeeded && settingsResult.Succeeded)
                    return true;

                if (!noteResult.Succeeded)
                {
                    DialogResult noteChoice = MessageBox.Show(this,
                        "便利贴尚未写入磁盘。\n\n" + noteResult.ErrorMessage +
                        "\n\n选择“是”重试，选择“否”导出当前内容后继续处理" +
                        "其他未保存数据，选择“取消”返回程序。",
                        "Penny pet - 有未保存内容",
                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (noteChoice == DialogResult.Yes) continue;
                    if (noteChoice == DialogResult.Cancel) return false;
                    if (!ExportUnsavedStickyNotes()) return false;
                    notesResolved = true;
                }

                if (!settingsResult.Succeeded)
                {
                    DialogResult settingsChoice = MessageBox.Show(this,
                        "程序设置尚未写入磁盘。\n\n" +
                        settingsResult.ErrorMessage +
                        "\n\n选择“是”重试，选择“否”明确放弃本次设置变更并退出，" +
                        "选择“取消”返回程序。",
                        "Penny pet - 有未保存设置",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);
                    if (settingsChoice == DialogResult.Yes) continue;
                    if (settingsChoice == DialogResult.Cancel) return false;
                    settingsResolved = true;
                }
            }
        }

        private bool ExportUnsavedStickyNotes()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出未保存的便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.dat)|*.dat|所有文件 (*.*)|*.*";
                dialog.FileName = "Penny-sticky-notes-emergency-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dat";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                PersistenceResult result = _notes.ExportSnapshot(dialog.FileName);
                if (result.Succeeded) return true;
                MessageBox.Show(this, "导出失败：" + result.ErrorMessage,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ExportStickyNotesBackup()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出便利贴备份";
                dialog.Filter = "Penny 便利贴备份 (*.pennysticky)|*.pennysticky|" +
                    "所有文件 (*.*)|*.*";
                dialog.FileName = "Penny-Stickies-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".pennysticky";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                PersistenceResult result = _notes.ExportSnapshot(dialog.FileName);
                if (result.Succeeded)
                {
                    ShowBubble("已导出 " + _notes.GetAll().Count +
                        " 张便利贴。");
                    return;
                }
                MessageBox.Show(this, "导出失败：" + result.ErrorMessage,
                    "Penny pet", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private StickyNotesImportPreview PrepareStickyNotesImport()
        {
            if (_exiting || IsDisposed || Disposing) return null;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入并合并便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.pennysticky;*.dat)|" +
                    "*.pennysticky;*.dat|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;

                StickyImportValidationResult validation =
                    StickyBackupFileReader.Read(dialog.FileName);
                if (validation == null || !validation.Succeeded)
                {
                    ShowStickyImportFailure("这个备份无法读取。\n当前便利贴没有被修改。");
                    return null;
                }
                if (validation.Notes.Count == 0)
                {
                    ShowBubble("备份中没有可导入的便利贴，当前内容没有修改。");
                    return null;
                }

                StickyImportMergeResult merge;
                try
                {
                    merge = StickyImportMergePlanner.Calculate(
                        _notes.GetAll(), validation.Notes);
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "sticky-notes-import-plan", error);
                    ShowStickyImportFailure("导入未完成。\n当前便利贴没有被修改。");
                    return null;
                }
                if (merge == null || merge.Actions == null ||
                    merge.Actions.Count == 0)
                {
                    ShowBubble("备份中没有可导入的便利贴，当前内容没有修改。");
                    return null;
                }
                return new StickyNotesImportPreview(merge, validation.Notes);
            }
        }

        private bool CommitStickyNotesImport(StickyNotesImportPreview preview)
        {
            if (preview == null || preview.Merge == null ||
                preview.ImportedNotes == null || preview.ImportedNotes.Count == 0)
                return false;

            StickyImportMergeResult currentPlan;
            try
            {
                currentPlan = StickyImportMergePlanner.Calculate(
                    _notes.GetAll(), preview.ImportedNotes);
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-notes-import-replan", error);
                ShowStickyImportFailure("导入未完成。\n当前便利贴没有被修改。");
                return false;
            }
            if (!ImportPlansMatch(preview.Merge, currentPlan))
            {
                ShowBubble("当前内容已变化，请重新导入。\n当前便利贴没有被修改。");
                return false;
            }
            if (currentPlan.AddedCount == 0)
            {
                ShowBubble("备份中的便利贴都已存在，当前内容没有修改。");
                return false;
            }

            PersistenceResult committed = _notes.CommitImportedMerge(currentPlan);
            if (committed == null || !committed.Succeeded)
            {
                ShowStickyImportFailure("导入未完成。\n当前便利贴没有被修改。");
                return false;
            }

            ReloadImportedStickyRuntime(currentPlan);
            ShowBubble(BuildStickyImportSummary(currentPlan));
            return true;
        }

        private static bool ImportPlansMatch(StickyImportMergeResult left,
            StickyImportMergeResult right)
        {
            if (left == null || right == null || left.Actions == null ||
                right.Actions == null || left.Actions.Count != right.Actions.Count)
                return false;
            for (int i = 0; i < left.Actions.Count; i++)
            {
                StickyImportAction a = left.Actions[i];
                StickyImportAction b = right.Actions[i];
                if (a == null || b == null || a.Kind != b.Kind ||
                    !String.Equals(a.ImportedNoteId, b.ImportedNoteId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(a.ResultNoteId, b.ResultNoteId,
                        StringComparison.OrdinalIgnoreCase) ||
                    a.Added != b.Added) return false;
            }
            return true;
        }

        private void RestoreStickyNotesBackup()
        {
            if (_exiting || IsDisposed || Disposing) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "从备份完整恢复便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.pennysticky;*.dat)|" +
                    "*.pennysticky;*.dat|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                StickyImportValidationResult validation =
                    StickyBackupFileReader.Read(dialog.FileName);
                if (validation == null || !validation.Succeeded)
                {
                    ShowStickyImportFailure("这个备份无法读取。\n当前便利贴没有被修改。");
                    return;
                }
                string warning = validation.Notes.Count == 0
                    ? "这个备份为空，完整恢复会清空当前全部便利贴。"
                    : "完整恢复会替换当前全部便利贴，并先保留一份当前内容。";
                if (MessageBox.Show(this, warning + "\n\n确定继续吗？",
                    "从备份完整恢复", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

                BeginFullStickyRestore(validation.Notes);
            }
        }

        private void BeginFullStickyRestore(
            List<StickyNoteData> restoredSnapshot)
        {
            CloseHostedStickyRuntimeForReload(
                delegate(StickyUiCommandResult closeResult)
                {
                    if (closeResult == null || closeResult.Status !=
                        StickyUiCommandStatus.Handled)
                    {
                        if (closeResult != null && closeResult.Status ==
                            StickyUiCommandStatus.NotAccepted)
                            ShowBubble("请先结束便利贴输入，再进行完整恢复。");
                        else
                            ShowStickyImportFailure(
                                "恢复未完成。\n当前便利贴没有被修改。");
                        return;
                    }

                    PersistenceResult committed = _notes.CommitFullRestore(
                        restoredSnapshot);
                    if (committed == null || !committed.Succeeded)
                    {
                        ReloadAllHostedStickyRuntime();
                        ShowStickyImportFailure(
                            "恢复未完成。\n当前便利贴没有被修改。");
                        return;
                    }
                    // Reminder records are persisted in settings, not in the
                    // portable sticky backup.  Reconcile note-side display
                    // ticks and remove linked reminders whose notes vanished.
                    ReconcileNoteReminders();
                    ReloadAllHostedStickyRuntime();
                    ShowBubble("完整恢复完成，共 " +
                        restoredSnapshot.Count + " 张便利贴。");
                });
        }

        private void ShowStickyImportFailure(string message)
        {
            MessageBox.Show(this,
                (message ?? "导入未完成。\n当前便利贴没有被修改。").Trim() +
                "\n\n" +
                "请把下面的诊断文件发给作者：\n" +
                ApplicationDiagnostics.LogFilePath,
                "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string BuildStickyImportSummary(
            StickyImportMergeResult merge)
        {
            return "导入完成：新增 " + merge.AddedCount + " 张；相同版本跳过 " +
                merge.SkippedIdenticalCount + " 张；不同版本保留副本 " +
                merge.ConflictCount + " 张。";
        }
    }
}
