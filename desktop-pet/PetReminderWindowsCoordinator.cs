using System;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Windows-only reminder orchestration: WinForms dialogs, pet animation,
    // speech bubbles and sticky-note windows. Pure reminder rules stay in
    // PetReminderCoordinator.cs.
    internal sealed partial class PetForm
    {
        private void RestoreReminders()
        {
            try
            {
                DateTime launchedUtc = DateTime.UtcNow;
                System.Collections.Generic.List<ReminderItem> future =
                    new System.Collections.Generic.List<ReminderItem>();
                foreach (ReminderItem item in _settings.Reminders)
                    if (PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                        item, launchedUtc))
                        future.Add(item);
                _reminders.Restore(future);
                if (future.Count != _settings.Reminders.Count)
                {
                    _settings.SetReminders(_reminders.GetItems());
                    _settings.Save();
                }
            }
            catch
            {
                _reminders.Cancel();
                _settings.SetReminders(_reminders.GetItems());
                _settings.Save();
            }
        }

        private void SaveReminders()
        {
            _settings.SetReminders(_reminders.GetItems());
            _settings.Save();
            UpdateAllStickyNoteReminderBanners();
        }

        private void UpdateAllStickyNoteReminderBanners()
        {
            System.Collections.Generic.List<ReminderItem> reminders =
                _reminders.GetItems();
            foreach (StickyNoteWindow form in _noteWindows.Values)
            {
                if (!form.IsDisposed) form.UpdateReminderBanner(reminders);
            }
        }

        private void ReconcileNoteReminders()
        {
            bool changed = false;
            foreach (StickyNoteData note in _notes.GetAll())
            {
                ReminderItem linked = _reminders.FindBySourceNoteId(note.Id);
                long nextTicks = linked == null ? 0 : linked.DeadlineUtc.Ticks;
                if (note.ReminderUtcTicks == nextTicks) continue;
                note.ReminderUtcTicks = nextTicks;
                changed = true;
            }
            if (changed) _notes.Save();
        }

        private void PreviewReminderDraft(StickyNoteWindow form,
            ReminderDialog dialog, string noteId)
        {
            if (form == null || form.IsDisposed || dialog == null) return;
            System.Collections.Generic.List<ReminderItem> preview =
                _reminders.GetItems();
            preview.Add(new ReminderItem(
                dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                noteId, dialog.ReminderFontSizePoints,
                dialog.PreAlertEnabled));
            form.UpdateReminderBanner(preview);
        }

        private void EditReminder(ReminderItem existing)
        {
            if (existing == null || !_reminders.GetItems().Contains(existing)) return;
            using (ReminderDialog dialog = new ReminderDialog(existing.Text,
                existing.FontSizeTwips / 20F, existing.PreAlertEnabled,
                existing.DeadlineUtc.ToLocalTime()))
            {
                StickyNoteWindow linkedForm = null;
                if (!String.IsNullOrEmpty(existing.SourceNoteId))
                    _noteWindows.TryGetValue(existing.SourceNoteId,
                        out linkedForm);
                if (linkedForm != null && !linkedForm.IsDisposed)
                {
                    dialog.ReminderFontSizePreviewChanged += delegate
                    {
                        if (!linkedForm.IsDisposed)
                            linkedForm.PreviewReminderFontSize(existing,
                                dialog.ReminderFontSizePoints);
                    };
                }
                if (dialog.ShowDialog(this) !=
                    System.Windows.Forms.DialogResult.OK)
                {
                    UpdateAllStickyNoteReminderBanners();
                    return;
                }
                if (!_reminders.GetItems().Contains(existing))
                {
                    ShowBubble("这条提醒已经到期或被删除，请重新添加。");
                    return;
                }
                ReminderItem replacement = _reminders.Replace(existing,
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                StickyNoteData note = String.IsNullOrEmpty(
                    replacement.SourceNoteId) ? null :
                    _notes.Find(replacement.SourceNoteId);
                if (note != null)
                {
                    RefreshLinkedNoteReminderState(note);
                    _notes.Save();
                    StickyNoteWindow noteForm;
                    if (_noteWindows.TryGetValue(note.Id, out noteForm) &&
                        !noteForm.IsDisposed) noteForm.RefreshReminderState();
                }
                SaveReminders();
                RefreshMenuText();
                ShowBriefBubble("提醒已修改：" +
                    replacement.DeadlineUtc.ToLocalTime()
                    .ToString("yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        private void CancelReminderForNote(StickyNoteData note, bool announce)
        {
            if (note == null) return;
            bool closePreAlert = _preAlertItem != null && String.Equals(
                _preAlertItem.SourceNoteId, note.Id,
                StringComparison.OrdinalIgnoreCase);
            int removed = _reminders.RemoveBySourceNoteId(note.Id);
            RefreshLinkedNoteReminderState(note);
            if (closePreAlert) CloseCurrentBubbleWithoutRestoringHover(true);
            _notes.Save();
            SaveReminders();
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.RefreshReminderState();
            RefreshMenuText();
            if (announce) ShowBubble(removed == 0
                ? "这张便利贴当前没有提醒。" : "这张便利贴的提醒已经全部取消。");
        }

        private void ReminderTick(object sender, EventArgs e)
        {
            if (!PetReminderCoordinator.ShouldRunReminderClock(_exiting))
                return;
            DateTime now = DateTime.UtcNow;
            ReminderItem due = _reminders.FirstDue(now);
            if (due != null)
            {
                TriggerReminder(due);
                return;
            }

            // Due checks remain at 500 ms. Countdown labels only display whole
            // seconds, so update their existing rows once per second without
            // rebuilding controls or touching the editor/IME focus.
            long currentSecond = now.Ticks / TimeSpan.TicksPerSecond;
            if (PetReminderCoordinator.ShouldRefreshReminderBanner(
                _lastReminderBannerSecond,
                currentSecond))
            {
                _lastReminderBannerSecond = currentSecond;
                UpdateAllStickyNoteReminderBanners();
            }

            ReminderItem next = _reminders.NextPreAlert;
            if (PetReminderCoordinator.ShouldShowPreAlert(next, next == null
                ? TimeSpan.Zero : next.DeadlineUtc - now))
                ShowOrUpdatePreAlert(next);
            else if (_bubbleIsPreAlert)
                CloseCurrentBubbleWithoutRestoringHover(true);

            if (_bubbleIsHover)
                ShowOrUpdateHoverBubble();
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
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                // Pet-menu reminders stay standalone. Every ordinary sticky
                // window already renders the current reminder list, including
                // notes the user creates after this reminder is saved.
                ReminderItem item = _reminders.Add(
                    dialog.DeadlineLocal.ToUniversalTime(), dialog.ReminderText,
                    null,
                    dialog.ReminderFontSizePoints, dialog.PreAlertEnabled);
                QueueArtPreload(NotificationRow);
                SaveReminders();
                RefreshMenuText();
                ShowBriefBubble("提醒已添加：" +
                    item.DeadlineUtc.ToLocalTime().ToString(
                        "yyyy年MM月dd日 HH:mm:ss"));
            }
        }

        private void CancelReminder(ReminderItem item, bool announce)
        {
            if (!_reminders.Remove(item)) return;
            ClearLinkedNoteReminder(item, false);
            if (ReferenceEquals(_preAlertItem, item))
                CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            if (announce) ShowBriefBubble("这条提醒已经取消。");
        }

        private void CancelAllReminders()
        {
            _reminders.Cancel();
            ReconcileNoteReminders();
            CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            ShowBubble("全部提醒已经取消。");
        }

        private void TriggerReminder(ReminderItem item)
        {
            string text = item == null ? String.Empty : item.Text;
            if (item != null) _reminders.Remove(item);
            StickyNoteData linkedNote = ClearLinkedNoteReminder(item, true);
            // A due reminder always replaces hover, confirmation and daily
            // speech instead of waiting behind a long-lived bubble.
            if (_bubble != null && !_bubble.IsDisposed)
                CloseCurrentBubbleWithoutRestoringHover(true);
            SaveReminders();
            RefreshMenuText();
            RequestReminderAttentionAnimation();
            string reminderText = String.IsNullOrWhiteSpace(text)
                ? "到时间啦。" : text;
            ShowBubble(reminderText, KeyboardOverlayForm.TextFontFamilyName,
                DueReminderBubbleFontSizePoints(
                    _settings.KeyOverlayScalePercent),
                PetReminderCoordinator.DueReminderBubbleDurationMilliseconds,
                false, true);
            System.Media.SystemSounds.Asterisk.Play();
            if (linkedNote != null)
                ShowStickyNote(linkedNote, !HasFocusedOwnNoteTextInput());
        }

        private void RequestReminderAttentionAnimation()
        {
            int generation = _reminderCoordinator.NextAnimationGeneration();
            QueueArtPreload(NotificationRow);
            if (_art.IsRowLoaded(NotificationRow))
            {
                BeginReminderAttentionAnimation(generation);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                // Bounded wait: an ordinary lazy decode completes quickly, but
                // damaged art must not create an endless retry loop.
                for (int attempt = 0; attempt < 50 && !_exiting &&
                    !IsDisposed; attempt++)
                {
                    if (_art.IsRowLoaded(NotificationRow)) break;
                    if (attempt == 12) QueueArtPreload(NotificationRow);
                    Thread.Sleep(100);
                }
                if (!_art.IsRowLoaded(NotificationRow) || _exiting ||
                    IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        BeginReminderAttentionAnimation(generation);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void BeginReminderAttentionAnimation(int generation)
        {
            if (_exiting || IsDisposed || generation !=
                _reminderCoordinator.CurrentAnimationGeneration ||
                !_art.IsRowLoaded(NotificationRow)) return;
            _reminderAttentionActive = true;
            if (_row == NotificationRow)
            {
                _frame = 0;
                _nextFrameUtc = DateTime.UtcNow.AddMilliseconds(
                    RuntimeFrameDuration(_row, _frame));
                RenderCurrentFrame();
            }
        }

        private StickyNoteData ClearLinkedNoteReminder(ReminderItem item,
            bool makeVisible)
        {
            if (item == null || String.IsNullOrEmpty(item.SourceNoteId))
                return null;
            StickyNoteData note = _notes.Find(item.SourceNoteId);
            if (note == null) return null;
            RefreshLinkedNoteReminderState(note);
            if (makeVisible) note.Visible = true;
            _notes.Save();
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(note.Id, out form) && !form.IsDisposed)
                form.RefreshReminderState();
            return note;
        }

        private void RefreshLinkedNoteReminderState(StickyNoteData note)
        {
            if (note == null) return;
            ReminderItem next = _reminders.FindBySourceNoteId(note.Id);
            note.ReminderUtcTicks = next == null ? 0 : next.DeadlineUtc.Ticks;
        }
    }
}
