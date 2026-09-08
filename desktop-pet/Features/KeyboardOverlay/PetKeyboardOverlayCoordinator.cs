using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Coordinates keyboard hook events, privacy scans and overlay delivery.
    internal sealed partial class PetForm
    {
        private static readonly uint OwnKeyboardProcessId =
            (uint)Process.GetCurrentProcess().Id;

        private void KeyboardActivity(object sender, KeyboardInputEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            bool shouldQueue;
            lock (_keyboardQueueGate)
            {
                if (_latestKeyboardEvent != null &&
                    String.Equals(_latestKeyboardEvent.DisplayText, e.DisplayText,
                        StringComparison.Ordinal))
                    _pendingKeyboardOccurrences = Math.Max(
                        _pendingKeyboardOccurrences, e.RepeatCount);
                else
                    _pendingKeyboardOccurrences = e.RepeatCount;
                _latestKeyboardEvent = e;
                shouldQueue = !_keyboardUiDispatchQueued;
                if (shouldQueue) _keyboardUiDispatchQueued = true;
            }
            if (!shouldQueue) return;
            try
            {
                BeginInvoke((MethodInvoker)ProcessPendingKeyboardActivity);
            }
            catch
            {
                lock (_keyboardQueueGate) _keyboardUiDispatchQueued = false;
            }
        }

        private void ProcessPendingKeyboardActivity()
        {
            KeyboardInputEventArgs keyboardEvent;
            int occurrences;
            lock (_keyboardQueueGate)
            {
                keyboardEvent = _latestKeyboardEvent;
                occurrences = Math.Max(1, _pendingKeyboardOccurrences);
                _latestKeyboardEvent = null;
                _pendingKeyboardOccurrences = 0;
                _keyboardUiDispatchQueued = false;
            }
            if (keyboardEvent == null || _dragging || _exiting) return;
            if (ShouldSuppressOwnApplicationInput(
                keyboardEvent.FocusSnapshot))
            {
                _keyOverlay.HideImmediately();
                return;
            }
            TriggerTypingAnimation();
            if (!_settings.ShowKeyOverlay ||
                String.IsNullOrEmpty(keyboardEvent.DisplayText)) return;
            QueuePrivacyCheckedOverlay(keyboardEvent.DisplayText, occurrences,
                keyboardEvent.VirtualKeyCode, keyboardEvent.FocusSnapshot);
        }

        private void QueuePrivacyCheckedOverlay(string displayText, int occurrences,
            int virtualKeyCode, KeyboardFocusSnapshot focusSnapshot)
        {
            bool startWorker;
            lock (_keyboardQueueGate)
            {
                string next = displayText ?? String.Empty;
                if (String.Equals(_pendingOverlayText, next,
                    StringComparison.Ordinal))
                    _pendingOverlayOccurrences = Math.Max(
                        _pendingOverlayOccurrences, Math.Max(1, occurrences));
                else
                {
                    _pendingOverlayText = next;
                    _pendingOverlayOccurrences = Math.Max(1, occurrences);
                }
                _pendingOverlayVirtualKeyCode = virtualKeyCode;
                _pendingOverlayFocusSnapshot = focusSnapshot;
                _pendingOverlayGeneration++;
                startWorker = !_privacyScanRunning;
                if (startWorker) _privacyScanRunning = true;
            }
            if (startWorker)
                ThreadPool.QueueUserWorkItem(PrivacyCheckedOverlayWorker);
        }

        private void PrivacyCheckedOverlayWorker(object state)
        {
            string displayText;
            long generation;
            KeyboardFocusSnapshot focusSnapshot;
            lock (_keyboardQueueGate)
            {
                displayText = _pendingOverlayText;
                generation = _pendingOverlayGeneration;
                focusSnapshot = _pendingOverlayFocusSnapshot;
            }
            bool sensitive = SensitiveInputDetector.IsSensitiveFocus(
                focusSnapshot);
            int occurrences;
            int virtualKeyCode;
            bool restart;
            lock (_keyboardQueueGate)
            {
                restart = !IsCurrentPrivacyScan(generation,
                    _pendingOverlayGeneration);
                if (restart)
                {
                    // The focus check belongs to an older key event. Keep the
                    // worker ownership and check the newest event separately.
                    occurrences = 0;
                    virtualKeyCode = 0;
                }
                else
                {
                // The hook provides an absolute repeat count. Keep the latest
                // value that arrived during the privacy scan instead of adding
                // cumulative counts (1+2+3) or losing them to worker timing.
                    displayText = _pendingOverlayText;
                    occurrences = Math.Max(1, _pendingOverlayOccurrences);
                    virtualKeyCode = _pendingOverlayVirtualKeyCode;
                    _pendingOverlayOccurrences = 0;
                    _privacyScanRunning = false;
                }
            }
            if (restart)
            {
                ThreadPool.QueueUserWorkItem(PrivacyCheckedOverlayWorker);
                return;
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    lock (_keyboardQueueGate)
                    {
                        if (!IsCurrentPrivacyScan(generation,
                            _pendingOverlayGeneration)) return;
                    }
                    if (_dragging || _exiting || !_settings.ShowKeyOverlay ||
                        ShouldSuppressOwnApplicationInput(focusSnapshot) ||
                        sensitive)
                    {
                        _keyOverlay.HideImmediately();
                        return;
                    }
                    _keyOverlay.ShowKeyRepeatCount(this, displayText,
                        occurrences, virtualKeyCode);
                    _windowLayers.KeepTransientBelowModal(_keyOverlay);
                });
            }
            catch { }
        }

        private bool ShouldSuppressOwnApplicationInput(
            KeyboardFocusSnapshot focusSnapshot)
        {
            bool ownApplicationInput = focusSnapshot != null &&
                focusSnapshot.ProcessId == OwnKeyboardProcessId;
            return PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                ownApplicationInput, HasFocusedOwnNoteTextInput() ||
                    _windowLayers.HasActiveModal);
        }

        private void PetWindowLayerChanged(object sender, EventArgs e)
        {
            _keyOverlay.UpdatePosition(this);
            _windowLayers.KeepTransientBelowModal(_keyOverlay);
            _windowLayers.KeepTransientBelowModal(_leftNoteTabs);
            _windowLayers.KeepTransientBelowModal(_rightNoteTabs);
            _bubbleCoordinator.ApplyWindowLayer();
        }

        internal static bool IsCurrentPrivacyScan(long capturedGeneration,
            long currentGeneration)
        {
            return capturedGeneration == currentGeneration;
        }

    }
}
