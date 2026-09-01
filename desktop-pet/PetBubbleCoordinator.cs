using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed class PetBubbleRequest
    {
        private PetBubbleRequest(PetMessageKind kind, string text,
            string fontFamilyName, float fontSizePoints,
            int autoCloseMilliseconds, bool deferWhileDragging,
            bool closesOnMouseDown)
        {
            Kind = kind;
            Text = text ?? String.Empty;
            FontFamilyName = fontFamilyName ?? "Microsoft YaHei UI";
            FontSizePoints = fontSizePoints;
            AutoCloseMilliseconds = Math.Max(0, autoCloseMilliseconds);
            DeferWhileDragging = deferWhileDragging;
            ClosesOnMouseDown = closesOnMouseDown;
        }

        internal readonly PetMessageKind Kind;
        internal readonly string Text;
        internal readonly string FontFamilyName;
        internal readonly float FontSizePoints;
        internal readonly int AutoCloseMilliseconds;
        internal readonly bool DeferWhileDragging;
        internal readonly bool ClosesOnMouseDown;

        internal static PetBubbleRequest Feedback(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.Feedback, text,
                fontFamilyName, fontSizePoints, 20000, true, true);
        }

        internal static PetBubbleRequest BriefFeedback(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.Feedback, text,
                fontFamilyName, fontSizePoints, 2000, true, true);
        }

        internal static PetBubbleRequest DailyGreeting(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.DailyGreeting, text,
                fontFamilyName, fontSizePoints, 20000, false, true);
        }

        internal static PetBubbleRequest EasterEgg(string fontFamilyName,
            float fontSizePoints)
        {
            return EasterEgg(fontFamilyName, fontSizePoints, 0);
        }

        internal static PetBubbleRequest EasterEgg(string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds)
        {
            return new PetBubbleRequest(PetMessageKind.EasterEgg,
                "你在整我是不是。", fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, false, false);
        }

        internal static PetBubbleRequest Hover(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.Hover, text,
                fontFamilyName, fontSizePoints, 0, false, true);
        }

        internal static PetBubbleRequest ReminderPreAlert(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.ReminderPreAlert, text,
                fontFamilyName, fontSizePoints, 0, false, true);
        }

        internal static PetBubbleRequest ReminderDue(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.ReminderDue, text,
                fontFamilyName, fontSizePoints,
                PetReminderCoordinator.DueReminderBubbleDurationMilliseconds,
                false, true);
        }

        internal PetBubbleRequest WithText(string text)
        {
            return new PetBubbleRequest(Kind, text, FontFamilyName,
                FontSizePoints, AutoCloseMilliseconds, DeferWhileDragging,
                ClosesOnMouseDown);
        }
    }

    // Owns the one active speech bubble and its complete deferred requests.
    internal sealed class PetBubbleCoordinator : IDisposable
    {
        private readonly Form _owner;
        private readonly Func<bool> _isDragging;
        private readonly Func<bool> _isExiting;
        private readonly Action<PetMessageKind> _messageClosed;
        private readonly Action _restoreAmbientMessage;
        private readonly Queue<PetBubbleRequest> _pending =
            new Queue<PetBubbleRequest>();
        private SpeechBubbleForm _bubble;
        private PetBubbleRequest _current;
        private bool _suppressRestore;
        private bool _disposed;

        internal PetBubbleCoordinator(Form owner, Func<bool> isDragging,
            Func<bool> isExiting, Action<PetMessageKind> messageClosed,
            Action restoreAmbientMessage)
        {
            _owner = owner ?? throw new ArgumentNullException("owner");
            _isDragging = isDragging ?? throw new ArgumentNullException(
                "isDragging");
            _isExiting = isExiting ?? throw new ArgumentNullException(
                "isExiting");
            _messageClosed = messageClosed;
            _restoreAmbientMessage = restoreAmbientMessage;
        }

        internal PetMessageKind? CurrentKind
        {
            get { return HasCurrent ? (PetMessageKind?)_current.Kind : null; }
        }

        internal bool HasCurrent
        {
            get
            {
                return _bubble != null && !_bubble.IsDisposed &&
                    _current != null;
            }
        }

        internal bool IsCurrent(PetMessageKind kind)
        {
            return HasCurrent && _current.Kind == kind;
        }

        internal bool Show(PetBubbleRequest request)
        {
            if (_disposed || request == null || _owner.IsDisposed) return false;
            if (HasCurrent && !PetMessagePolicy.ShouldReplace(CurrentKind,
                request.Kind, _isExiting())) return false;
            if (_isDragging() && request.DeferWhileDragging)
            {
                _pending.Enqueue(request);
                return true;
            }
            CloseCurrent(true);
            SpeechBubbleForm bubble = new SpeechBubbleForm(request.Text,
                request.AutoCloseMilliseconds, request.FontFamilyName,
                request.FontSizePoints, request.ClosesOnMouseDown);
            _bubble = bubble;
            _current = request;
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(_owner);
            return true;
        }

        internal void ShowNextPending()
        {
            if (_disposed || _isDragging() || _isExiting() ||
                _pending.Count == 0) return;
            Show(_pending.Dequeue());
        }

        internal void UpdateCurrentText(string text)
        {
            if (!HasCurrent) return;
            _current = _current.WithText(text);
            _bubble.UpdateText(text);
            _bubble.ShowNear(_owner);
        }

        internal void Reposition()
        {
            if (HasCurrent) _bubble.RepositionNear(_owner);
        }

        internal void ShowCurrentNearOwner()
        {
            if (HasCurrent) _bubble.ShowNear(_owner);
        }

        internal void CloseIfCurrent(PetMessageKind kind)
        {
            if (IsCurrent(kind)) _bubble.Close();
        }

        internal void CloseCurrent(bool forceProtectedReminder)
        {
            if (!HasCurrent) return;
            if (PetMessagePolicy.IsProtectedReminder(_current.Kind) &&
                !forceProtectedReminder && !_isExiting()) return;
            _suppressRestore = true;
            _bubble.Close();
            _suppressRestore = false;
        }

        private void BubbleClosed(object sender, FormClosedEventArgs e)
        {
            if (!ReferenceEquals(_bubble, sender)) return;
            PetMessageKind closedKind = _current.Kind;
            _bubble.FormClosed -= BubbleClosed;
            _bubble = null;
            _current = null;
            if (_messageClosed != null) _messageClosed(closedKind);
            if (_suppressRestore || _disposed || _isDragging() ||
                _isExiting() || _owner.IsDisposed) return;
            try
            {
                _owner.BeginInvoke((MethodInvoker)ProcessAfterClose);
            }
            catch (InvalidOperationException) { }
        }

        private void ProcessAfterClose()
        {
            if (_disposed || HasCurrent || _isDragging() || _isExiting() ||
                _owner.IsDisposed) return;
            if (_pending.Count > 0)
            {
                ShowNextPending();
                if (HasCurrent) return;
            }
            if (_restoreAmbientMessage != null) _restoreAmbientMessage();
        }

        internal int PendingCountForTest
        {
            get { return _pending.Count; }
        }

        internal PetBubbleRequest CurrentRequestForTest
        {
            get { return _current; }
        }

        internal SpeechBubbleForm CurrentBubbleForTest
        {
            get { return _bubble; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            if (!HasCurrent) return;
            _suppressRestore = true;
            _bubble.Close();
            _suppressRestore = false;
        }
    }

    // PetForm remains the thin product integration edge around the runtime owner.
    internal sealed partial class PetForm
    {
        private void ShowBubble(string text)
        {
            _bubbleCoordinator.Show(PetBubbleRequest.Feedback(text,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent)));
        }

        private void ShowBriefBubble(string text)
        {
            _bubbleCoordinator.Show(PetBubbleRequest.BriefFeedback(text,
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

        private void ShowOrUpdatePreAlert(ReminderItem item)
        {
            if (item == null || _dragging || _exiting || _menu.Visible ||
                IsDisposed) return;
            int seconds = Math.Max(0, (int)Math.Ceiling(
                (item.DeadlineUtc - DateTime.UtcNow).TotalSeconds));
            string text = "提醒倒计时 " + seconds + " 秒\n" + item.Text;
            if (_bubbleCoordinator.HasCurrent)
            {
                if (_bubbleCoordinator.IsCurrent(
                    PetMessageKind.ReminderPreAlert) &&
                    ReferenceEquals(_preAlertItem, item))
                {
                    _bubbleCoordinator.UpdateCurrentText(text);
                    return;
                }
                if (!_bubbleCoordinator.IsCurrent(PetMessageKind.Hover)) return;
            }
            PetBubbleRequest request = PetBubbleRequest.ReminderPreAlert(text,
                KeyboardOverlayForm.TextFontFamilyName,
                KeyboardOverlayForm.TextFontSizePoints(
                    _settings.KeyOverlayScalePercent));
            if (_bubbleCoordinator.Show(request)) _preAlertItem = item;
        }

        private void ShowOrUpdateHoverBubble()
        {
            if (!ShouldShowHoverBubble(_mouseInside, _menu.Visible, _dragging,
                _settings.SilentMode) || IsDisposed || _exiting) return;
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
            bool forceProtectedReminder = false)
        {
            _bubbleCoordinator.CloseCurrent(forceProtectedReminder);
        }

        private void BubbleMessageClosed(PetMessageKind kind)
        {
            if (kind == PetMessageKind.ReminderPreAlert) _preAlertItem = null;
        }

        private void RestoreAmbientBubble()
        {
            if (_dragging || _exiting || IsDisposed) return;
            ReminderItem next = _reminders.NextPreAlert;
            if (PetReminderCoordinator.ShouldShowPreAlert(next, next == null
                ? TimeSpan.Zero : next.Remaining))
                ShowOrUpdatePreAlert(next);
            else if (_mouseInside && !_menu.Visible)
                ShowOrUpdateHoverBubble();
        }

        private void RepositionCurrentBubble()
        {
            _bubbleCoordinator.Reposition();
        }
    }
}
