using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    // Form supplies live runtime services; operation ownership stays here even
    // when the Sticky runtime has not yet been attached.
    internal interface IPetPersistenceHost
    {
        Form Window { get; }
        bool IsExiting { get; }
        PetSettings Settings { get; }
        StickyFeature Notes { get; }
        StickyWorkspace Workspace { get; }
        ReminderRuntime Reminders { get; }
        void SetMenuEnabled(bool enabled);
        void StopConversation();
        void ResumeConversation();
        void ResumeRuntimeComposition();
        void ShowBubble(string text);
        void CaptureLocationForSave();
        void FinishExitSequence();
    }

    internal sealed class PetPersistenceCoordinator
    {
        private readonly IPetPersistenceHost _host;

        internal PetPersistenceCoordinator(IPetPersistenceHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        internal bool IsActive { get { return _persistenceOperation; } }

        private bool _persistenceOperation;
        private bool _persistenceOwnerEnabled;
        private bool _resumeRemindersAfterPersistence;

        private async Task<bool> PreparePersistenceOperationAsync()
        {
            if (_persistenceOperation || _host.IsExiting || _host.Window.IsDisposed) return false;
            _persistenceOperation = true;
            _persistenceOwnerEnabled = _host.Window.Enabled;
            _resumeRemindersAfterPersistence = _host.Reminders != null && _host.Reminders.IsRunning;
            _host.Window.Enabled = false;
            _host.SetMenuEnabled(false);
            _host.StopConversation();
            if (_host.Reminders != null) _host.Reminders.Stop();
            if (_host.Workspace == null) return true;
            StickyUiCommandResult prepared = await _host.Workspace.PreparePersistenceAsync();
            if (prepared != null && prepared.Status == StickyUiCommandStatus.Handled)
                return true;
            await ResumePersistenceOperationAsync();
            _host.ShowBubble("请先结束便利贴输入或拖动，再重试。");
            return false;
        }

        private async Task ResumePersistenceOperationAsync()
        {
            if (_host.Workspace != null)
                await _host.Workspace.PersistenceCommandAsync(
                    StickyUiCommand.ResumeAfterPersistence());
            _persistenceOperation = false;
            if (_host.Window.IsDisposed || _host.Window.Disposing) return;
            _host.Window.Enabled = _persistenceOwnerEnabled;
            _host.SetMenuEnabled(true);
            if (!_host.IsExiting) _host.ResumeConversation();
            if (_host.Reminders != null && _resumeRemindersAfterPersistence && !_host.IsExiting)
                _host.Reminders.Start();
            _host.ResumeRuntimeComposition();
        }

        internal async void BeginExitSequence()
        {
            if (_host.IsExiting || _persistenceOperation) return;
            if (!await PreparePersistenceOperationAsync()) return;
            try
            {
                _host.CaptureLocationForSave();
                if (!await FlushPersistenceBeforeExit()) return;
                _host.FinishExitSequence();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal("persistence-exit", error);
                _host.ShowBubble("保存未完成，已取消退出，请重试。");
            }
            finally
            {
                if (!_host.IsExiting) await ResumePersistenceOperationAsync();
            }
        }

        private static async Task<PersistenceResult> WaitForSaveReceiptAsync(
            Task<PersistenceResult> receipt)
        {
            if (await Task.WhenAny(receipt, Task.Delay(TimeSpan.FromSeconds(5))) != receipt)
                return PersistenceResult.Failure(new TimeoutException(
                    "磁盘仍在写入。停止等待不会取消已排队的保存。"));
            return await receipt;
        }

        internal void PersistenceNoticeReceived(object sender,
            PersistenceNoticeEventArgs e)
        {
            if (_host.Window.IsDisposed || _host.Window.Disposing || _host.IsExiting || _persistenceOperation || e == null) return;
            if (e.Kind == PersistenceNoticeKind.Warning)
            {
                string dataName = String.IsNullOrEmpty(e.DataName)
                    ? "数据" : e.DataName;
                _host.ShowBubble(dataName +
                    "尚未保存，Penny 会自动重试。请暂时不要退出。");
                return;
            }
            _host.ShowBubble("未保存的数据已重新写入磁盘。");
        }

        private async Task<bool> FlushPersistenceBeforeExit()
        {
            bool notesResolved = _host.Notes == null;
            bool settingsResolved = false;
            Task<PersistenceResult> notesReceipt = null;
            Task<PersistenceResult> settingsReceipt = null;
            while (true)
            {
                if (!notesResolved && (notesReceipt == null || notesReceipt.IsCompleted))
                    notesReceipt = _host.Notes.SaveBarrierAsync();
                if (!settingsResolved && (settingsReceipt == null || settingsReceipt.IsCompleted))
                    settingsReceipt = _host.Settings.SaveBarrierAsync();
                PersistenceResult[] results = await Task.WhenAll(
                    notesResolved ? Task.FromResult(PersistenceResult.Success()) : WaitForSaveReceiptAsync(notesReceipt),
                    settingsResolved ? Task.FromResult(PersistenceResult.Success()) : WaitForSaveReceiptAsync(settingsReceipt));
                PersistenceResult noteResult = results[0];
                PersistenceResult settingsResult = results[1];
                if (noteResult.Succeeded && settingsResult.Succeeded)
                    return true;

                if (!noteResult.Succeeded)
                {
                    DialogResult noteChoice = MessageBox.Show(_host.Window,
                        "便利贴尚未写入磁盘。\n\n" + noteResult.ErrorMessage +
                        "\n\n选择“是”重试，选择“否”导出当前内容后继续处理" +
                        "其他未保存数据，选择“取消”返回程序。",
                        "Penny pet - 有未保存内容",
                        MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (noteChoice == DialogResult.Yes) continue;
                    if (noteChoice == DialogResult.Cancel) return false;
                    if (!await ExportUnsavedStickyNotes()) return false;
                    notesResolved = true;
                }

                if (!settingsResult.Succeeded)
                {
                    DialogResult settingsChoice = MessageBox.Show(_host.Window,
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

        private async Task<bool> ExportUnsavedStickyNotes()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出未保存的便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.dat)|*.dat|所有文件 (*.*)|*.*";
                dialog.FileName = "Penny-sticky-notes-emergency-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".dat";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(_host.Window) != DialogResult.OK) return false;
                PersistenceResult result = await _host.Notes.ExportSnapshotAsync(dialog.FileName);
                if (result.Succeeded) return true;
                MessageBox.Show(_host.Window, "导出失败：" + result.ErrorMessage,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        internal async Task ExportStickyNotesBackup()
        {
            if (!await PreparePersistenceOperationAsync()) return;
            try
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
                    if (dialog.ShowDialog(_host.Window) != DialogResult.OK) return;

                    PersistenceResult result = await _host.Notes.ExportSnapshotAsync(dialog.FileName);
                    if (result.Succeeded)
                    {
                        _host.ShowBubble("已导出 " + _host.Notes.Count +
                            " 张便利贴。");
                        return;
                    }
                    MessageBox.Show(_host.Window, "导出失败：" + result.ErrorMessage,
                        "Penny pet", MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally { await ResumePersistenceOperationAsync(); }
        }

        internal async Task<StickyNotesImportPreview> PrepareStickyNotesImport()
        {
            if (_host.IsExiting || _host.Window.IsDisposed || _host.Window.Disposing) return null;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入并合并便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.pennysticky;*.dat)|" +
                    "*.pennysticky;*.dat|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(_host.Window) != DialogResult.OK) return null;

                StickyImportValidationResult validation =
                    await Task.Run(() => StickyBackupFileReader.Read(dialog.FileName));
                if (_host.IsExiting || _host.Window.IsDisposed || _host.Window.Disposing) return null;
                if (validation == null || !validation.Succeeded)
                {
                    ShowStickyImportFailure("这个备份无法读取。\n当前便利贴没有被修改。");
                    return null;
                }
                if (validation.Notes.Count == 0)
                {
                    _host.ShowBubble("备份中没有可导入的便利贴，当前内容没有修改。");
                    return null;
                }

                StickyImportMergeResult merge;
                try
                {
                    merge = StickyImportMergePlanner.Calculate(
                        _host.Notes.GetAll(), validation.Notes);
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
                    _host.ShowBubble("备份中没有可导入的便利贴，当前内容没有修改。");
                    return null;
                }
                return new StickyNotesImportPreview(merge, validation.Notes);
            }
        }

        internal async Task<bool> CommitStickyNotesImport(StickyNotesImportPreview preview)
        {
            if (preview == null || preview.Merge == null ||
                preview.ImportedNotes == null || preview.ImportedNotes.Count == 0)
                return false;

            if (!await PreparePersistenceOperationAsync()) return false;
            try
            {
                StickyImportMergeResult currentPlan;
                try
                {
                    currentPlan = StickyImportMergePlanner.Calculate(
                        _host.Notes.GetAll(), preview.ImportedNotes);
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
                    _host.ShowBubble("当前内容已变化，请重新导入。\n当前便利贴没有被修改。");
                    return false;
                }
                if (currentPlan.AddedCount == 0)
                {
                    _host.ShowBubble("备份中的便利贴都已存在，当前内容没有修改。");
                    return false;
                }

                PersistenceResult committed = await _host.Notes.CommitImportedMergeAsync(currentPlan);
                if (committed == null || !committed.Succeeded)
                {
                    ShowStickyImportFailure("导入未完成。\n当前便利贴没有被修改。");
                    return false;
                }

                _host.Workspace.ReloadImportedStickyRuntime(currentPlan);
                _host.ShowBubble(BuildStickyImportSummary(currentPlan));
                return true;
            }
            finally { await ResumePersistenceOperationAsync(); }
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

        internal async void RestoreStickyNotesBackup()
        {
            if (_host.IsExiting || _persistenceOperation || _host.Window.IsDisposed || _host.Window.Disposing) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "从备份完整恢复便利贴";
                dialog.Filter = "Penny 便利贴备份 (*.pennysticky;*.dat)|" +
                    "*.pennysticky;*.dat|所有文件 (*.*)|*.*";
                dialog.InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.DesktopDirectory);
                if (dialog.ShowDialog(_host.Window) != DialogResult.OK) return;

                StickyImportValidationResult validation =
                    await Task.Run(() => StickyBackupFileReader.Read(dialog.FileName));
                if (_host.IsExiting || _persistenceOperation || _host.Window.IsDisposed || _host.Window.Disposing) return;
                if (validation == null || !validation.Succeeded)
                {
                    ShowStickyImportFailure("这个备份无法读取。\n当前便利贴没有被修改。");
                    return;
                }
                string warning = validation.Notes.Count == 0
                    ? "这个备份为空，完整恢复会清空当前全部便利贴。"
                    : "完整恢复会替换当前全部便利贴，并先保留一份当前内容。";
                if (MessageBox.Show(_host.Window, warning + "\n\n确定继续吗？",
                    "从备份完整恢复", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

                await BeginFullStickyRestore(validation.Notes);
            }
        }

        private async Task BeginFullStickyRestore(List<StickyNoteData> restoredSnapshot)
        {
            if (!await PreparePersistenceOperationAsync()) return;
            try
            {
                PersistenceResult committed = await _host.Notes.CommitFullRestoreAsync(restoredSnapshot);
                if (!committed.Succeeded)
                {
                    ShowStickyImportFailure("恢复未完成。\n当前便利贴没有被修改。");
                    return;
                }
                // Notes have committed. Settings are a separate durable file;
                // report that boundary instead of claiming a multi-file transaction.
                _host.Reminders.ReconcileNoteLinks();
                PersistenceResult settings = await WaitForSaveReceiptAsync(_host.Settings.SaveBarrierAsync());
                StickyUiCommandResult closed = await _host.Workspace.RetirePersistenceWindowsAsync();
                if (closed == null || closed.Status != StickyUiCommandStatus.Handled)
                {
                    ShowStickyImportFailure("便利贴文件已恢复，但窗口刷新失败，请重启 Penny。");
                    return;
                }
                _host.Workspace.ReloadAllHostedStickyRuntime();
                if (!settings.Succeeded)
                    ShowStickyImportFailure("便利贴已恢复；提醒设置尚未保存，将自动重试。请勿重复导入。");
                else _host.ShowBubble("完整恢复完成，共 " + restoredSnapshot.Count + " 张便利贴。");
            }
            finally { await ResumePersistenceOperationAsync(); }
        }

        private void ShowStickyImportFailure(string message)
        {
            MessageBox.Show(_host.Window,
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
