using System;
using System.Windows.Forms;

namespace PennyPet
{
    // Coordinates speech-bubble lifetime and presentation near the pet.
    internal sealed partial class PetForm
    {
        private void ShowBubble(string text)
        {
            ShowBubble(text, KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
        }

        private void ShowBriefBubble(string text)
        {
            ShowBubble(text, KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent), 2000);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints, 20000);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, true);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds,
            bool deferWhileDragging)
        {
            ShowBubble(text, fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, deferWhileDragging, false);
        }

        private void ShowBubble(string text, string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds,
            bool deferWhileDragging, bool isDueReminder)
        {
            if (_bubble != null && !_bubble.IsDisposed &&
                !PetReminderCoordinator.ShouldReplaceBubble(
                    _bubbleIsDueReminder,
                    _bubbleIsPreAlert, isDueReminder, _exiting))
                return;
            if (_dragging && deferWhileDragging)
            {
                _pendingBubbleTexts.Enqueue(new BubbleMessage(text,
                    fontFamilyName, fontSizePoints));
                return;
            }
            // ShouldReplaceBubble has already protected pre-alerts where
            // appropriate. Force-close here so a later ordinary message can
            // replace a persistent due reminder.
            CloseCurrentBubbleWithoutRestoringHover(true);
            SpeechBubbleForm bubble = new SpeechBubbleForm(text,
                Math.Max(0, autoCloseMilliseconds),
                fontFamilyName, fontSizePoints);
            _bubble = bubble;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = isDueReminder;
            _preAlertItem = null;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        private void ShowNextPendingBubble()
        {
            if (_dragging || _exiting || _pendingBubbleTexts.Count == 0) return;
            BubbleMessage message = _pendingBubbleTexts.Dequeue();
            ShowBubble(message.Text, message.FontFamilyName,
                message.FontSizePoints);
        }

        private void ShowOrUpdatePreAlert(ReminderItem item)
        {
            if (item == null || _dragging || _exiting || _menu.Visible || IsDisposed)
                return;
            int seconds = Math.Max(0, (int)Math.Ceiling(
                (item.DeadlineUtc - DateTime.UtcNow).TotalSeconds));
            string text = "提醒倒计时 " + seconds + " 秒\n" + item.Text;
            if (_bubble != null && !_bubble.IsDisposed)
            {
                if (_bubbleIsPreAlert && ReferenceEquals(_preAlertItem, item))
                {
                    _bubble.UpdateText(text);
                    _bubble.ShowNear(this);
                    return;
                }
                if (!_bubbleIsHover) return;
                CloseCurrentBubbleWithoutRestoringHover();
            }
            // The optional pre-alert is deliberately compact.  The selected
            // reminder size is reserved for the actual due-time bubble.
            SpeechBubbleForm bubble = new SpeechBubbleForm(text, 0,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            _bubble = bubble;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = true;
            _bubbleIsDueReminder = false;
            _preAlertItem = item;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        private void ShowOrUpdateHoverBubble()
        {
            if (!ShouldShowHoverBubble(_mouseInside, _menu.Visible, _dragging,
                _settings.SilentMode) ||
                IsDisposed || _exiting) return;
            ReminderItem next = _reminders.Next;
            string text = next != null
                ? "距离最近提醒还有" + FormatRemaining(next.Remaining) +
                    "。\n当前共有 " + _reminders.Count + " 条提醒。"
                : "今天想要做些什么呢？";

            if (_bubble != null && !_bubble.IsDisposed)
            {
                if (_bubbleIsHover)
                {
                    _bubble.UpdateText(text);
                    _bubble.ShowNear(this);
                }
                return;
            }

            SpeechBubbleForm bubble = new SpeechBubbleForm(text, 0,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            _bubble = bubble;
            _bubbleIsHover = true;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = false;
            _preAlertItem = null;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(this);
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging)
        {
            return ShouldShowHoverBubble(mouseInside, menuVisible, dragging, false);
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging, bool silentMode)
        {
            return mouseInside && !menuVisible && !dragging && !silentMode;
        }

        internal static bool ShouldSuppressDailyBubble(bool silentMode,
            bool isReminderBubble)
        {
            return silentMode && !isReminderBubble;
        }

        private void HideHoverBubble()
        {
            if (!_bubbleIsHover || _bubble == null || _bubble.IsDisposed) return;
            _bubbleIsHover = false;
            _bubble.Close();
        }

        private void CloseCurrentBubbleWithoutRestoringHover(
            bool forceProtectedReminder = false)
        {
            if (_bubble == null || _bubble.IsDisposed) return;
            if ((_bubbleIsDueReminder || _bubbleIsPreAlert) &&
                !forceProtectedReminder && !_exiting) return;
            _suppressHoverRestore = true;
            _bubbleIsHover = false;
            _bubbleIsPreAlert = false;
            _bubbleIsDueReminder = false;
            _preAlertItem = null;
            _bubble.Close();
            _suppressHoverRestore = false;
            _bubble = null;
        }

        private void BubbleClosed(object sender, FormClosedEventArgs e)
        {
            if (ReferenceEquals(_bubble, sender))
            {
                _bubble = null;
                _bubbleIsHover = false;
                _bubbleIsPreAlert = false;
                _bubbleIsDueReminder = false;
                _preAlertItem = null;
            }
            if (_suppressHoverRestore || _dragging || _exiting || IsDisposed) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (_pendingBubbleTexts.Count > 0)
                {
                    ShowNextPendingBubble();
                    return;
                }
                ReminderItem next = _reminders.NextPreAlert;
                if (PetReminderCoordinator.ShouldShowPreAlert(next, next == null
                    ? TimeSpan.Zero : next.Remaining))
                    ShowOrUpdatePreAlert(next);
                else if (_mouseInside && !_menu.Visible)
                    ShowOrUpdateHoverBubble();
            });
        }

        private void RepositionCurrentBubble()
        {
            if (_bubble == null || _bubble.IsDisposed) return;
            _bubble.RepositionNear(this);
        }

    }
}
