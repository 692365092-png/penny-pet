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
            ShowBriefBubble(dataName +
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
            if (!_exiting) ShowBriefBubble("未保存的数据已重新写入磁盘。");
        }

        private bool FlushPersistenceBeforeExit()
        {
            foreach (StickyNoteWindow form in
                new List<StickyNoteWindow>(_noteWindows.Values))
                if (form != null && !form.IsDisposed)
                    form.FlushPendingChanges();

            _notes.WaitForPendingSaves();
            while (true)
            {
                PersistenceResult noteResult = _notes.Save();
                PersistenceResult settingsResult = _settings.Save();
                if (noteResult.Succeeded && settingsResult.Succeeded)
                    return true;
                if (noteResult.Succeeded)
                {
                    DialogResult settingsChoice = MessageBox.Show(this,
                        "程序设置尚未写入磁盘。\n\n" +
                        settingsResult.ErrorMessage +
                        "\n\n选择“是”重试，选择“否”忽略本次设置变更并退出，" +
                        "选择“取消”返回程序。",
                        "Penny pet - 有未保存设置",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Warning);
                    if (settingsChoice == DialogResult.Yes) continue;
                    if (settingsChoice == DialogResult.No) return true;
                    return false;
                }
                DialogResult choice = MessageBox.Show(this,
                    "便利贴尚未写入磁盘。\n\n" + noteResult.ErrorMessage +
                    "\n\n选择“是”重试，选择“否”导出当前内容后退出，" +
                    "选择“取消”返回程序。",
                    "Penny pet - 有未保存内容", MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes) continue;
                if (choice == DialogResult.Cancel) return false;
                return ExportUnsavedStickyNotes();
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
    }
}
