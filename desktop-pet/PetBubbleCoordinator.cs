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
            bool closesOnMouseDown, int minimumReadableMilliseconds)
        {
            Kind = kind;
            Text = text ?? String.Empty;
            FontFamilyName = fontFamilyName ?? "Microsoft YaHei UI";
            FontSizePoints = fontSizePoints;
            AutoCloseMilliseconds = Math.Max(0, autoCloseMilliseconds);
            DeferWhileDragging = deferWhileDragging;
            ClosesOnMouseDown = closesOnMouseDown;
            MinimumReadableMilliseconds = Math.Max(0,
                minimumReadableMilliseconds);
        }

        internal readonly PetMessageKind Kind;
        internal readonly string Text;
        internal readonly string FontFamilyName;
        internal readonly float FontSizePoints;
        internal readonly int AutoCloseMilliseconds;
        internal readonly bool DeferWhileDragging;
        internal readonly bool ClosesOnMouseDown;
        internal readonly int MinimumReadableMilliseconds;

        internal static PetBubbleRequest Feedback(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.Feedback, text,
                fontFamilyName, fontSizePoints,
                BubbleReadingDurationRules.AutoCloseMilliseconds(text),
                true, true,
                BubbleReadingDurationRules.MinimumReadableMilliseconds(text));
        }

        internal static PetBubbleRequest DailyGreeting(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.DailyGreeting, text,
                fontFamilyName, fontSizePoints,
                BubbleReadingDurationRules.AutoCloseMilliseconds(text),
                false, true,
                BubbleReadingDurationRules.MinimumReadableMilliseconds(text));
        }

        internal static PetBubbleRequest EasterEgg(string fontFamilyName,
            float fontSizePoints)
        {
            return EasterEgg(fontFamilyName, fontSizePoints, 2800);
        }

        internal static PetBubbleRequest EasterEgg(string fontFamilyName,
            float fontSizePoints, int autoCloseMilliseconds)
        {
            return new PetBubbleRequest(PetMessageKind.EasterEgg,
                "你在整我是不是。", fontFamilyName, fontSizePoints,
                autoCloseMilliseconds, false, false, 1000);
        }

        internal static PetBubbleRequest SmallTalk(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.SmallTalk, text,
                fontFamilyName, fontSizePoints,
                BubbleReadingDurationRules.AutoCloseMilliseconds(text),
                false, true,
                BubbleReadingDurationRules.MinimumReadableMilliseconds(text));
        }

        internal static PetBubbleRequest Hover(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.Hover, text,
                fontFamilyName, fontSizePoints, 0, false, true, 0);
        }

        internal static PetBubbleRequest ReminderPreAlert(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.ReminderPreAlert, text,
                fontFamilyName, fontSizePoints, 0, false, true, 0);
        }

        internal static PetBubbleRequest ReminderDue(string text,
            string fontFamilyName, float fontSizePoints)
        {
            return new PetBubbleRequest(PetMessageKind.ReminderDue, text,
                fontFamilyName, fontSizePoints,
                PetReminderCoordinator.DueReminderBubbleDurationMilliseconds,
                false, true, 0);
        }

        internal PetBubbleRequest WithText(string text)
        {
            return new PetBubbleRequest(Kind, text, FontFamilyName,
                FontSizePoints, AutoCloseMilliseconds, DeferWhileDragging,
                ClosesOnMouseDown, MinimumReadableMilliseconds);
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
        private readonly PetWindowLayerCoordinator _windowLayers;
        private readonly Queue<PetBubbleRequest> _pending =
            new Queue<PetBubbleRequest>();
        private SpeechBubbleForm _bubble;
        private PetBubbleRequest _current;
        private DateTime _minimumReadableUntilUtc;
        private readonly Func<DateTime> _clock;
        private bool _suppressRestore;
        private bool _disposed;

        internal PetBubbleCoordinator(Form owner, Func<bool> isDragging,
            Func<bool> isExiting, Action<PetMessageKind> messageClosed,
            Action restoreAmbientMessage, Func<DateTime> clock = null,
            PetWindowLayerCoordinator windowLayers = null)
        {
            _owner = owner ?? throw new ArgumentNullException("owner");
            _isDragging = isDragging ?? throw new ArgumentNullException(
                "isDragging");
            _isExiting = isExiting ?? throw new ArgumentNullException(
                "isExiting");
            _messageClosed = messageClosed;
            _restoreAmbientMessage = restoreAmbientMessage;
            _clock = clock ?? (() => DateTime.UtcNow);
            _windowLayers = windowLayers;
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
            DateTime now = _clock();
            if (HasCurrent && now < _minimumReadableUntilUtc &&
                !PetMessagePolicy.CanBreakReadability(request.Kind))
                return false;
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
            _minimumReadableUntilUtc = now.AddMilliseconds(
                request.MinimumReadableMilliseconds);
            bubble.FormClosed += BubbleClosed;
            bubble.ShowNear(_owner);
            ApplyWindowLayer();
            return true;
        }

        internal void ShowNextPending()
        {
            if (_disposed || _isDragging() || _isExiting() ||
                _pending.Count == 0) return;
            if (Show(_pending.Peek())) _pending.Dequeue();
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
            if (!HasCurrent) return;
            _bubble.RepositionNear(_owner);
            ApplyWindowLayer();
        }

        internal void ShowCurrentNearOwner()
        {
            if (!HasCurrent) return;
            _bubble.ShowNear(_owner);
            ApplyWindowLayer();
        }

        internal void ApplyWindowLayer()
        {
            if (HasCurrent && _windowLayers != null)
                _windowLayers.KeepTransientBelowModal(_bubble);
        }

        internal void CloseIfCurrent(PetMessageKind kind)
        {
            if (IsCurrent(kind)) _bubble.Close();
        }

        internal void CloseCurrent(bool forceProtectedMessage)
        {
            if (!HasCurrent) return;
            if (PetMessagePolicy.IsProtectedForegroundMessage(
                _current.Kind) && !forceProtectedMessage && !_isExiting())
                return;
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
            if (IsDisposed || _exiting ||
                PetHoverStabilityRules.ShouldSuppressHover(
                    _stableMouseInside, _menu.Visible, _dragging,
                    _settings.SilentMode,
                    _hoverSuppressedUntilStableLeave)) return;
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
            if (kind == PetMessageKind.ReminderPreAlert) _preAlertItem = null;
        }

        private void RestoreAmbientBubble()
        {
            if (_dragging || _exiting || IsDisposed) return;
            ReminderItem next = _reminders.NextPreAlert;
            if (PetReminderCoordinator.ShouldShowPreAlert(next, next == null
                ? TimeSpan.Zero : next.Remaining))
                ShowOrUpdatePreAlert(next);
            else if (!PetHoverStabilityRules.ShouldSuppressHover(
                _stableMouseInside, _menu.Visible, _dragging,
                _settings.SilentMode, _hoverSuppressedUntilStableLeave))
                ShowOrUpdateHoverBubble();
        }

        private void RepositionCurrentBubble()
        {
            _bubbleCoordinator.Reposition();
        }
    }
}
