using System.Windows.Forms;

namespace PennyPet
{
    // Window effects and service access only. The coordinator owns operations.
    internal sealed partial class PetForm : IPetPersistenceHost
    {
        internal void BeginExitSequence() { _persistenceCoordinator.BeginExitSequence(); }

        Form IPetPersistenceHost.Window { get { return this; } }
        bool IPetPersistenceHost.IsExiting { get { return _exiting; } }
        PetSettings IPetPersistenceHost.Settings { get { return _settings; } }
        StickyFeature IPetPersistenceHost.Notes { get { return _notes; } }
        StickyWorkspace IPetPersistenceHost.Workspace { get { return _stickyWorkspace; } }
        ReminderRuntime IPetPersistenceHost.Reminders { get { return _reminderRuntime; } }
        void IPetPersistenceHost.SetMenuEnabled(bool enabled) { _menu.Enabled = enabled; }
        void IPetPersistenceHost.StopConversation() { _conversation.Stop(); }
        void IPetPersistenceHost.ResumeConversation() { _conversation.ResumeAfterPersistence(); }
        void IPetPersistenceHost.ResumeRuntimeComposition() { ResumeDeferredRuntimeComposition(); }
        void IPetPersistenceHost.ShowBubble(string text) { ShowBubble(text); }
        void IPetPersistenceHost.CaptureLocationForSave() { CaptureLocationForSave(); }
        void IPetPersistenceHost.FinishExitSequence() { FinishExitSequence(); }
    }
}
