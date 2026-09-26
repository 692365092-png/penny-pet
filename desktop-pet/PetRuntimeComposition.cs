using System;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm
    {
        internal bool IsExitingForComposition
        {
            get { return _exiting || IsDisposed || Disposing; }
        }

        internal void AttachPreparedStickyRuntime(StickyLoadResult prepared)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            if (IsExitingForComposition) return;
            if (_notes != null)
                throw new InvalidOperationException(
                    "Sticky runtime has already been published.");

            UnsupportedStickySchemaException future =
                StickyFeature.PreparedFutureSchemaError(prepared);
            if (future != null) throw future;

            WindowsFormsSynchronizationContext ownerContext =
                SynchronizationContext.Current as
                    WindowsFormsSynchronizationContext
                ?? new WindowsFormsSynchronizationContext();

            // Publish the prepared store/model on Pet STA so all subsequent
            // failure notifications and captures retain the real owner context.
            _notes = StickyFeature.PublishPreparedLoad(prepared);
            _persistence = new PetPersistenceRuntime(
                _notes, _settings, ownerContext);
            _persistence.Notice += PersistenceNoticeReceived;

            _stickyWorkspace = AttachStickyWorkspace(ownerContext);
            _reminderRuntime = new ReminderRuntime(
                _reminders, _settings, _notes, this);
            _reminderRuntime.Restore(DateTime.UtcNow);

            _stickyWorkspace.Start();
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (topology != null)
                _stickyWorkspace.Host.SetCurrentTopology(topology);
            _stickyWorkspace.RefreshNoteTabs();
            _stickyWorkspace.PositionNoteTabs();
            _reminderRuntime.Start();

            if (_reminders.Count > 0)
                QueueArtPreload(NotificationRow);
            RefreshMenuText();
        }

        internal void AbortStartupComposition()
        {
            if (_exiting || IsDisposed || Disposing) return;
            _exiting = true;
            StopDeferredStartupWork();
            Close();
        }

        private void RunWhenStickyRuntimeReady(
            Action<StickyWorkspace> action)
        {
            StickyWorkspace workspace = _stickyWorkspace;
            if (workspace == null)
            {
                ShowBubble("便利贴正在后台恢复，请稍候。");
                return;
            }
            if (action != null) action(workspace);
        }

        private void RunWhenReminderRuntimeReady(Action action)
        {
            if (_reminderRuntime == null)
            {
                ShowBubble("提醒功能正在后台恢复，请稍候。");
                return;
            }
            if (action != null) action();
        }
    }
}
