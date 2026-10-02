using System;

namespace PennyPet
{
    // PetForm remains the thin product integration edge around the runtime owner.
    internal sealed partial class PetForm
    {
        internal void ShowBubble(string text)
        {
            _bubbleCoordinator.Show(PetBubbleRequest.Feedback(text,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent)));
        }

        private void ShowDueReminderBubble(string text, float fontSizePoints)
        {
            _bubbleCoordinator.Show(PetBubbleRequest.ReminderDue(text,
                KeyboardOverlayForm.TextFontFamilyName, fontSizePoints));
        }

        private void ShowNextPendingBubble()
        {
            _bubbleCoordinator.ShowNextPending();
        }

        bool IReminderPresentation.TryShowPreAlert(string text, bool updateCurrent)
        {
            if (_interaction.PointerDown || _exiting || _menu.Visible || IsDisposed) return false;
            if (_bubbleCoordinator.HasCurrent)
            {
                if (_bubbleCoordinator.IsCurrent(PetMessageKind.ReminderPreAlert) &&
                    updateCurrent)
                {
                    _bubbleCoordinator.UpdateCurrentText(text);
                    return true;
                }
                if (!_bubbleCoordinator.IsCurrent(PetMessageKind.Hover)) return false;
            }
            return _bubbleCoordinator.Show(PetBubbleRequest.ReminderPreAlert(text,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(_settings.KeyOverlayScalePercent)));
        }

        private void ShowOrUpdateHoverBubble()
        {
            if (IsDisposed || _exiting ||
                PetHoverStabilityRules.ShouldSuppressHover(
                    _interaction.StableMouseInside, _menu.Visible, _interaction.PointerDown,
                    _settings.SilentMode,
                    _interaction.HoverSuppressed)) return;
            ReminderItem next = _reminders.Next;
            string text = next != null
                ? "距离最近提醒还有" + FormatRemaining(next.Remaining) +
                    "。\n当前共有 " + _reminders.Count + " 条提醒。"
                : "今天想要做些什么呢？";
            if (_bubbleCoordinator.HasCurrent)
            {
                if (_bubbleCoordinator.IsCurrent(PetMessageKind.Hover))
                    _bubbleCoordinator.UpdateCurrentText(text);
                return;
            }
            _bubbleCoordinator.Show(PetBubbleRequest.Hover(text,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent)));
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging)
        {
            return ShouldShowHoverBubble(mouseInside, menuVisible, dragging,
                false);
        }

        internal static bool ShouldShowHoverBubble(bool mouseInside,
            bool menuVisible, bool dragging, bool silentMode)
        {
            return mouseInside && !menuVisible && !dragging && !silentMode;
        }

        private void HideHoverBubble()
        {
            _bubbleCoordinator.CloseIfCurrent(PetMessageKind.Hover);
        }

        private void CloseCurrentBubbleWithoutRestoringHover(
            bool forceProtectedMessage = false)
        {
            _bubbleCoordinator.CloseCurrent(forceProtectedMessage);
        }

        private void BubbleMessageClosed(PetMessageKind kind)
        {
            if (_reminderRuntime != null)
                _reminderRuntime.MessageClosed(kind);
        }

        private void RestoreAmbientBubble()
        {
            if (_interaction.PointerDown || _exiting || IsDisposed) return;
            if (_reminderRuntime != null &&
                _reminderRuntime.RefreshPreAlert(DateTime.UtcNow)) return;
            if (!PetHoverStabilityRules.ShouldSuppressHover(
                _interaction.StableMouseInside, _menu.Visible, _interaction.PointerDown,
                _settings.SilentMode, _interaction.HoverSuppressed))
                ShowOrUpdateHoverBubble();
        }

        private void RepositionCurrentBubble()
        {
            _bubbleCoordinator.Reposition();
        }
    }
}
