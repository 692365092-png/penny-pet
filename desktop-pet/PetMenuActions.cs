using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns user commands invoked by the pet context menu and tray.
    internal sealed partial class PetForm
    {
        internal const string WindowsKeyboardFirstUseNotice =
            "按键显示会使用 Windows 全局键盘活动监听，在桌宠旁显示按键名称。\n\n" +
            "Penny 不会保存或上传按键内容，并会尽力识别密码框和敏感输入。" +
            "但第三方、自绘、跨权限或远程窗口可能无法被完全识别。\n\n" +
            "由于此功能会使用 Windows 全局键盘监听，部分杀毒软件或安全软件" +
            "可能会将它误报为风险行为或进行拦截。\n\n" +
            "处理密码、验证码、支付或其他高敏感信息时，请先关闭按键显示。" +
            "是否确认开启？";

        private void RefreshMenuText()
        {
            _cancelItem.DropDownItems.Clear();
            List<ReminderItem> items = _reminders.GetItems();
            if (items.Count > 0)
            {
                _statusItem.Text = "共 " + items.Count + " 条提醒，最近：" +
                    items[0].DeadlineUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                _cancelItem.Enabled = true;
                for (int i = 0; i < items.Count; i++)
                {
                    ReminderItem target = items[i];
                    string shortText = target.Text.Length > 16
                        ? target.Text.Substring(0, 16) + "…" : target.Text;
                    ToolStripMenuItem cancelOne = new ToolStripMenuItem(
                        (i + 1) + "．" + target.DeadlineUtc.ToLocalTime().ToString(
                        "MM-dd HH:mm:ss") + "  " + shortText);
                    cancelOne.Click += delegate { CancelReminder(target, true); };
                    _cancelItem.DropDownItems.Add(cancelOne);
                }
                _cancelItem.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem cancelAll = new ToolStripMenuItem("取消全部提醒");
                cancelAll.Click += delegate { CancelAllReminders(); };
                _cancelItem.DropDownItems.Add(cancelAll);
            }
            else
            {
                _statusItem.Text = "当前没有提醒";
                _cancelItem.Enabled = false;
            }
            _setReminderItem.Enabled = items.Count < ReminderSchedule.MaximumItems;
            _setReminderItem.Text = "添加提醒…（" + items.Count + "/" +
                ReminderSchedule.MaximumItems + "）";
            _manageNotesItem.Text = "便利贴管理…（" + _notes.GetAll().Count + "张）";
            _silentItem.Checked = _settings.SilentMode;
            int visibleNotes = 0;
            int hiddenNotes = 0;
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note.Visible) visibleNotes++;
                else hiddenNotes++;
            }
            _collapseNotesItem.Text = "收起全部便利贴到页签（" + visibleNotes + "张）";
            _collapseNotesItem.Enabled = visibleNotes > 0;
            _expandTabsItem.Text = "展开全部侧边页签（" + hiddenNotes + "张）";
            _expandTabsItem.Enabled = hiddenNotes > 0;
            _scaleItem.Text = "调整大小…（桌宠 " + _scalePercent + "% / 按键" +
                KeyTextSizeName(_settings.KeyOverlayScalePercent) + "）";
            RefreshKeyboardMenuText();
        }

        private void ShowScaleDialog()
        {
            using (ScaleDialog dialog = new ScaleDialog(_scalePercent,
                _settings.KeyOverlayScalePercent))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyScale(dialog.SelectedPercent, dialog.SelectedKeyTextPercent);
            }
        }

        private void ApplyScale(int percent, int keyTextPercent)
        {
            int next = NormalizeScalePercent(percent);
            int nextKeyText = KeyboardOverlayForm.NormalizeTextScalePercent(
                keyTextPercent);
            bool petChanged = next != _scalePercent;
            bool keyTextChanged = nextKeyText != _settings.KeyOverlayScalePercent;
            if (!petChanged && !keyTextChanged) return;
            _keyOverlay.HideImmediately();
            if (petChanged)
            {
                int centerX = Left + Width / 2;
                int bottom = Bottom;
                DisposeRenderedFrameCache();
                _scalePercent = next;
                ClientSize = ScaledPetSize(_scalePercent);
                BuildRenderedFrameCache();
                Location = new Point(centerX - Width / 2, bottom - Height);
                KeepFullyVisible();
                RenderCurrentFrame();
                if (_bubble != null && !_bubble.IsDisposed) _bubble.ShowNear(this);
            }
            _keyOverlay.SetTextScale(nextKeyText);
            _settings.ScalePercent = _scalePercent;
            _settings.KeyOverlayScalePercent = nextKeyText;
            SaveLocation();
            RefreshMenuText();
            ShowBubble("大小已更新：桌宠 " + _scalePercent + "% ，按键文字" +
                KeyTextSizeName(nextKeyText) + "。");
        }

        private static string KeyTextSizeName(int percent)
        {
            int value = KeyboardOverlayForm.NormalizeTextScalePercent(percent);
            if (value == 60) return "小";
            if (value == 150) return "大";
            return "中";
        }

        private void KeyboardItemClick(object sender, EventArgs e)
        {
            bool desired = _keyboardItem.Checked;
            if (PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(desired,
                _settings.KeyboardPrivacyNoticeAccepted))
            {
                DialogResult notice = MessageBox.Show(this,
                    WindowsKeyboardFirstUseNotice,
                    "开启按键显示前请确认",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (notice != DialogResult.OK)
                {
                    _keyboardItem.Checked = false;
                    _settings.ShowKeyOverlay = false;
                    _settings.Save();
                    RefreshKeyboardMenuText();
                    return;
                }
                _settings.KeyboardPrivacyNoticeAccepted = true;
            }
            if (desired && !_keyboard.IsRunning)
            {
                try
                {
                    _keyboard.Start();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "keyboard-user-start", error);
                }
                desired = _keyboard.IsRunning;
            }
            else if (!desired && _keyboard.IsRunning)
                _keyboard.Dispose();
            _keyboardItem.Checked = desired;
            _settings.ShowKeyOverlay = desired;
            _settings.Save();
            if (!_settings.ShowKeyOverlay) _keyOverlay.HideImmediately();
            RefreshKeyboardMenuText();
        }

        private void SilentItemClick(object sender, EventArgs e)
        {
            _settings.SilentMode = _silentItem.Checked;
            _settings.Save();
            if (_settings.SilentMode) HideHoverBubble();
        }

        private void RefreshKeyboardMenuText()
        {
            if (_keyboard == null)
            {
                _keyboardItem.Text = "按键显示：正在检查";
                _keyboardItem.Enabled = false;
                return;
            }
            _keyboardItem.Enabled = true;
            _keyboardItem.Checked = _settings.ShowKeyOverlay;
            if (!_settings.ShowKeyOverlay)
                _keyboardItem.Text = "按键显示：已关闭";
            else if (_keyboard.IsRunning)
                _keyboardItem.Text = "按键显示：已开启（密码框自动隐藏）";
            else
                _keyboardItem.Text = "按键显示：当前不可用";
        }

        private void StartupItemClick(object sender, EventArgs e)
        {
            bool desired = _startupItem.Checked;
            string error;
            if (!StartupRegistration.Apply(desired, out error))
            {
                _startupItem.Checked = !desired;
                MessageBox.Show(this, "开机自启设置失败：" + error,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _settings.StartupPreferenceInitialized = true;
            _settings.StartAtLogin = desired;
            _settings.Save();
            ShowBubble(desired ? "开机自动启动已开启。" : "开机自动启动已关闭。");
        }

        private void BeginExitSequence()
        {
            if (_exiting) return;
            if (!FlushPersistenceBeforeExit()) return;
            _exiting = true;
            _reminderTimer.Stop();
            _dragging = false;
            Capture = false;
            _typingSession = false;
            _manualAnimationActive = false;
            _keyOverlay.HideImmediately();
            _mouseInside = false;
            if (_menu.Visible) _menu.Close();
            CloseCurrentBubbleWithoutRestoringHover();
            _row = WavingRow;
            _frame = 0;
            _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                RuntimeFrameDuration(_row, _frame));
            RenderCurrentFrame();
            if (!ShouldSuppressDailyBubble(_settings.SilentMode, false))
                ShowBubble("再见啦，照顾好自己！");
        }

        private bool HasFocusedOwnNoteTextInput()
        {
            foreach (StickyNoteWindow form in _noteWindows.Values)
            {
                if (form != null && !form.IsDisposed &&
                    form.HasFocusedTextInput) return true;
            }
            return false;
        }

        internal static string FormatRemaining(TimeSpan value)
        {
            if (value < TimeSpan.Zero) value = TimeSpan.Zero;
            if (value.TotalDays >= 1)
            {
                int days = (int)value.TotalDays;
                return days + "天" + (value.Hours > 0 ? value.Hours + "小时" : "");
            }
            if (value.TotalHours >= 1)
            {
                int hours = (int)value.TotalHours;
                return hours + "小时" + (value.Minutes > 0 ? value.Minutes + "分钟" : "");
            }
            if (value.TotalMinutes >= 1)
            {
                int minutes = (int)value.TotalMinutes;
                return minutes + "分" + (value.Seconds > 0 ? value.Seconds + "秒" : "");
            }
            return Math.Max(0, (int)Math.Ceiling(value.TotalSeconds)) + "秒";
        }
    }
}
