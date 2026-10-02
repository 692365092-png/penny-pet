using System;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    internal sealed partial class PetForm : IPetStartupHost
    {
        internal void EnableRuntimeComposition() { _startup.EnableRuntimeComposition(); }

        internal event EventHandler ShellReady
        {
            add { _startup.ShellReady += value; }
            remove { _startup.ShellReady -= value; }
        }
        internal event EventHandler StartupBackgroundReady
        {
            add { _startup.StartupBackgroundReady += value; }
            remove { _startup.StartupBackgroundReady -= value; }
        }

        Form IPetStartupHost.Window { get { return this; } }
        bool IPetStartupHost.IsExiting { get { return _exiting; } }
        bool IPetStartupHost.PersistenceActive { get { return _persistenceCoordinator.IsActive; } }
        PetSettings IPetStartupHost.Settings { get { return _settings; } }
        GlobalKeyboardActivity IPetStartupHost.Keyboard { get { return _keyboard; } }
        StickyFeature IPetStartupHost.Notes { get { return _notes; } }
        StickyWorkspace IPetStartupHost.Workspace { get { return _stickyWorkspace; } }
        ReminderRuntime IPetStartupHost.Reminders { get { return _reminderRuntime; } }
        void IPetStartupHost.RefreshKeyboardMenu() { RefreshKeyboardMenuText(); }
        void IPetStartupHost.RefreshMenu() { RefreshMenuText(); }
        void IPetStartupHost.RenderFrame() { RenderCurrentFrame(); }
        void IPetStartupHost.ShowBubble(string text) { ShowBubble(text); }

        void IPetStartupHost.PublishPreparedStickyRuntime(StickyLoadResult prepared)
        {
            if (prepared == null)
                throw new ArgumentNullException(nameof(prepared));
            if (_exiting || IsDisposed || Disposing) return;
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
            _persistence.Notice += _persistenceCoordinator.PersistenceNoticeReceived;

            _stickyWorkspace = AttachStickyWorkspace(ownerContext);
            _reminderRuntime = new ReminderRuntime(
                _reminders, _settings, _notes, this);
            _reminderRuntime.Restore(_startup.LaunchedUtc);

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

        void IPetStartupHost.AbortStartupComposition()
        {
            if (_exiting || IsDisposed || Disposing) return;
            _exiting = true;
            _startup.Dispose();
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
