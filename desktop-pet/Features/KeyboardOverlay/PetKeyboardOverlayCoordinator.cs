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
            if (IsDisposed || !IsHandleCreated || _exiting) return;
            // Offer at the event boundary, before any Pet UI queue delay.
            _keyboardPrivacy.Offer(e);
            bool shouldQueue;
            lock (_keyboardQueueGate)
            {
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
            lock (_keyboardQueueGate)
            {
                keyboardEvent = _latestKeyboardEvent;
                _latestKeyboardEvent = null;
                _keyboardUiDispatchQueued = false;
            }
            if (keyboardEvent == null || _interaction.PointerDown || _exiting) return;
            if (ShouldSuppressOwnApplicationInput(
                keyboardEvent.FocusSnapshot))
            {
                _keyOverlay.HideImmediately();
                return;
            }
            if (keyboardEvent.FocusSnapshot == null || !keyboardEvent.FocusSnapshot.HasNativeInputIdentity)
                _keyOverlay.HideImmediately();
            TriggerTypingAnimation();
        }

        private void KeyboardFocusChanged(object sender, EventArgs e)
        {
            _keyboardPrivacy.Invalidate();
            if (!IsDisposed && !_keyOverlay.IsDisposed) _keyOverlay.HideImmediately();
        }

        private void PublishCheckedKeyboardInput(KeyboardInputEventArgs input)
        {
            if (IsDisposed || _interaction.PointerDown || _exiting || !_settings.ShowKeyOverlay ||
                ShouldSuppressOwnApplicationInput(input.FocusSnapshot))
            {
                _keyOverlay.HideImmediately();
                return;
            }
            _keyOverlay.ShowKeyRepeatCount(this, input.DisplayText, input.RepeatCount, input.VirtualKeyCode);
            _windowLayers.KeepTransientBelowModal(_keyOverlay);
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
            _stickyWorkspace.ApplyWindowLayer();
            _bubbleCoordinator.ApplyWindowLayer();
        }



    }
}
