using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    // Dialogs and presentation only. ReminderRuntime owns schedule changes,
    // ticking, pre-alert identity and asynchronous notification validity.
    internal sealed partial class PetForm : IReminderPresentation
    {
        internal void EditReminder(ReminderItem existing)
        {
            if (existing == null || !_reminders.GetItems().Contains(existing)) return;
            using (ReminderDialog dialog = new ReminderDialog(existing.Text,
                existing.FontSizeTwips / 20F, existing.PreAlertEnabled,
                existing.DeadlineUtc.ToLocalTime()))
            {
                if (!String.IsNullOrEmpty(existing.SourceNoteId) &&
                    _stickyWorkspace.Hosted.ContainsNote(existing.SourceNoteId))
                {
                    dialog.ReminderFontSizePreviewChanged += delegate
                    {
                        _stickyWorkspace.PreviewHostedReminderFontSize(existing,
                            dialog.ReminderFontSizePoints);
                    };
                }
                if (_windowLayers.ShowModal(this, dialog) !=
                    System.Windows.Forms.DialogResult.OK)
                {
                    _stickyWorkspace.UpdateAllStickyNoteReminderBanners();
                    return;
                }
                ReminderItem replacement = _reminderRuntime.Edit(existing,
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                if (replacement == null)
                {
                    ShowBubble("这条提醒已经到期或被删除，请重新添加。");
                    return;
                }
                ShowBubble("提醒已修改：" +
                    replacement.DeadlineUtc.ToLocalTime()
                    .ToString("yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        internal void CancelReminderForNote(StickyNoteData note, bool announce)
        {
            if (note == null) return;
            int removed = _reminderRuntime.CancelForNote(note);
            if (announce) ShowBubble(removed == 0
                ? "这张便利贴当前没有提醒。" : "这张便利贴的提醒已经全部取消。");
        }

        private void ShowReminderDialog()
        {
            if (_reminders.Count >= ReminderSchedule.MaximumItems)
            {
                ShowBubble("最多可以保存五条提醒，请先取消一条。");
                return;
            }
            using (ReminderDialog dialog = new ReminderDialog())
            {
                if (_windowLayers.ShowModal(this, dialog) != DialogResult.OK)
                    return;
                // Pet-menu reminders stay standalone. Every ordinary sticky
                // window already renders the current reminder list, including
                // notes the user creates after this reminder is saved.
                ReminderItem item = _reminderRuntime.Add(
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                QueueArtPreload(NotificationRow);
                ShowBubble("提醒已添加：" +
                    item.DeadlineUtc.ToLocalTime().ToString(
                        "yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        internal void CancelReminder(ReminderItem item, bool announce)
        {
            if (!_reminderRuntime.Cancel(item)) return;
            if (announce) ShowBubble("这条提醒已经取消。");
        }

        private void CancelAllReminders()
        {
            _reminderRuntime.CancelAll();
            ShowBubble("全部提醒已经取消。");
        }

        void IReminderPresentation.RemindersChanged()
        {
            _stickyWorkspace.UpdateAllStickyNoteReminderBanners();
            RefreshMenuText();
        }

        void IReminderPresentation.ClosePreAlert()
        {
            if (_bubbleCoordinator.IsCurrent(PetMessageKind.ReminderPreAlert))
                CloseCurrentBubbleWithoutRestoringHover(true);
        }

        void IReminderPresentation.CloseCurrentMessage()
        {
            CloseCurrentBubbleWithoutRestoringHover(true);
        }

        void IReminderPresentation.RefreshHover()
        {
            if (_bubbleCoordinator.IsCurrent(PetMessageKind.Hover))
                ShowOrUpdateHoverBubble();
        }

        void IReminderPresentation.ShowDue(ReminderItem item, StickyNoteData linkedNote)
        {
            _animation.CancelInteractionAnimation();
            ShowDueReminderBubble(String.IsNullOrWhiteSpace(item.Text)
                ? "到时间啦。" : item.Text, DueReminderBubbleFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            System.Media.SystemSounds.Asterisk.Play();
            if (linkedNote != null)
                _stickyWorkspace.ShowHostedSticky(linkedNote, !HasFocusedOwnNoteTextInput());
        }

        Task IReminderPresentation.PrepareAttentionAsync()
        {
            if (_art.IsRowLoaded(NotificationRow)) return Task.FromResult(0);
            return Task.Run(() => _art.PreloadRow(NotificationRow));
        }

        void IReminderPresentation.BeginAttention()
        {
            _animation.CancelInteractionAnimation();
            _reminderAttentionActive = true;
            if (_row == NotificationRow)
            {
                _frame = 0;
                _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
            }
        }
    }
}
