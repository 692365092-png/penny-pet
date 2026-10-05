using System;
using System.Windows.Forms;

namespace PennyPet
{
    // Window effects and service access only. The coordinator owns operations.
    internal sealed partial class PetForm : IPetPersistenceHost
    {
        private bool _exitLocationCaptureActive;
        private bool _exitLocationHasLocation;
        private int _exitLocationX;
        private int _exitLocationY;
        private int _exitLocationScalePercent;

        internal void BeginExitSequence() { _persistenceCoordinator.BeginExitSequence(); }

        private void CaptureExitLocationForSave()
        {
            if (!_exitLocationCaptureActive)
            {
                _exitLocationCaptureActive = true;
                _exitLocationHasLocation = _settings.HasLocation;
                _exitLocationX = _settings.X;
                _exitLocationY = _settings.Y;
                _exitLocationScalePercent = _settings.ScalePercent;
            }
            CaptureLocationForSave();
        }

        private void CommitExitLocationCapture()
        {
            _exitLocationCaptureActive = false;
        }

        private void RollbackExitLocationCapture()
        {
            if (!_exitLocationCaptureActive) return;
            _exitLocationCaptureActive = false;
            _settings.HasLocation = _exitLocationHasLocation;
            _settings.X = _exitLocationX;
            _settings.Y = _exitLocationY;
            _settings.ScalePercent = _exitLocationScalePercent;

            // Settings may already have reached disk before another persistence
            // stream caused exit to be cancelled. Queue the compensating snapshot
            // before runtime composition is released again.
            _settings.SaveAsync();
        }

        private void FinishExitSequence()
        {
            _exiting = true;
            _startup.Dispose();
            _keyboardPrivacy.SetEnabled(false);
            if (_reminderRuntime != null) _reminderRuntime.Stop();
            _conversation.Stop();
            if (_persistence != null) _persistence.Dispose();
            _interaction.BeginExit(DateTime.UtcNow);
            Capture = false;
            _keyOverlay.HideImmediately();
            if (_menu.Visible) _menu.Close();
            CloseCurrentBubbleWithoutRestoringHover();
            if (!_settings.SilentMode)
                ShowBubble("再见啦，照顾好自己！");
        }

        Form IPetPersistenceHost.Window { get { return this; } }
        bool IPetPersistenceHost.IsExiting { get { return _exiting; } }
        PetSettings IPetPersistenceHost.Settings { get { return _settings; } }
        StickyFeature IPetPersistenceHost.Notes { get { return _notes; } }
        StickyWorkspace IPetPersistenceHost.Workspace { get { return _stickyWorkspace; } }
        ReminderRuntime IPetPersistenceHost.Reminders { get { return _reminderRuntime; } }
        void IPetPersistenceHost.SetMenuEnabled(bool enabled) { _menu.Enabled = enabled; }
        void IPetPersistenceHost.StopConversation() { _conversation.Stop(); }
        void IPetPersistenceHost.ResumeConversation() { _conversation.ResumeAfterPersistence(); }
        void IPetPersistenceHost.ResumeRuntimeComposition()
        {
            RollbackExitLocationCapture();
            _startup.ResumeDeferredRuntimeComposition();
        }
        void IPetPersistenceHost.ShowBubble(string text) { ShowBubble(text); }
        void IPetPersistenceHost.StickyDatasetReplaced() { _startup.StickyDatasetReplaced(); }
        void IPetPersistenceHost.CaptureLocationForSave() { CaptureExitLocationForSave(); }
        void IPetPersistenceHost.FinishExitSequence()
        {
            CommitExitLocationCapture();
            FinishExitSequence();
        }
    }
}
