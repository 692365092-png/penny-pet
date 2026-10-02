using System;
using System.Windows.Forms;

namespace PennyPet
{
    // Window effects and service access only. The coordinator owns operations.
    internal sealed partial class PetForm : IPetPersistenceHost
    {
        internal void BeginExitSequence() { _persistenceCoordinator.BeginExitSequence(); }

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
        void IPetPersistenceHost.ResumeRuntimeComposition() { _startup.ResumeDeferredRuntimeComposition(); }
        void IPetPersistenceHost.ShowBubble(string text) { ShowBubble(text); }
        void IPetPersistenceHost.CaptureLocationForSave() { CaptureLocationForSave(); }
        void IPetPersistenceHost.FinishExitSequence() { FinishExitSequence(); }
    }
}
