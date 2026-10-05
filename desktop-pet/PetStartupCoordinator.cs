using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal interface IPetStartupHost
    {
        Form Window { get; }
        bool IsExiting { get; }
        bool PersistenceActive { get; }
        PetSettings Settings { get; }
        GlobalKeyboardActivity Keyboard { get; }
        StickyFeature Notes { get; }
        StickyWorkspace Workspace { get; }
        ReminderRuntime Reminders { get; }
        void RefreshKeyboardMenu();
        void RefreshMenu();
        void RenderFrame();
        void ShowBubble(string text);
        void PublishPreparedStickyRuntime(StickyLoadResult prepared);
        void AbortStartupComposition();
    }

    // Owns shell readiness, deferred restoration and runtime publication on Pet STA.
    internal sealed class PetStartupCoordinator : IDisposable
    {
        private readonly IPetStartupHost _host;
        private readonly DateTime _launchedUtc;
        private System.Windows.Forms.Timer _startupWorkTimer;
        private StartupWorkPhase _startupWorkPhase;
        private Queue<StickyNoteData> _startupVisibleNotes;
        private readonly HashSet<string> _expectedFirstRenderNoteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _renderedFirstRenderNoteIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _startupArtReady;
        private bool _shellReadyRaised;
        private bool _startupDisplaySuppressed = true;
        private bool _runtimeCompositionEnabled;
        private bool _runtimeLoadStarted;
        private bool _disposed;
        private Action _deferredRuntimeComposition;

        internal PetStartupCoordinator(IPetStartupHost host, DateTime launchedUtc)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _launchedUtc = launchedUtc;
        }

        internal DateTime LaunchedUtc { get { return _launchedUtc; } }
        internal bool DisplaySuppressed { get { return _startupDisplaySuppressed; } }
        internal event EventHandler ShellReady;
        internal event EventHandler StartupBackgroundReady;

        private bool IsStopping
        {
            get { return _disposed || _host.IsExiting ||
                _host.Window.IsDisposed || _host.Window.Disposing; }
        }

        internal void EnableRuntimeComposition()
        {
            if (IsStopping) return;
            _runtimeCompositionEnabled = true;
            TryBeginRuntimeComposition();
        }

        internal void ArtReady(bool ready)
        {
            _startupArtReady = ready;
            TryRaiseShellReady();
        }

        private void TryBeginRuntimeComposition()
        {
            if (IsStopping || !_shellReadyRaised || !_runtimeCompositionEnabled ||
                _runtimeLoadStarted) return;
            _runtimeLoadStarted = true;
            BeginRuntimeComposition();
        }

        internal bool DeferRuntimeCompositionWhilePersisting(Action complete)
        {
            if (IsStopping) return true;
            if (!_host.PersistenceActive) return false;
            _deferredRuntimeComposition = complete;
            return true;
        }

        internal void ResumeDeferredRuntimeComposition()
        {
            if (_host.PersistenceActive) return;
            Action complete = _deferredRuntimeComposition;
            _deferredRuntimeComposition = null;
            if (!IsStopping) complete?.Invoke();
        }

        internal void ForgetFirstRendered(string noteId, bool forgetExpected)
        {
            _renderedFirstRenderNoteIds.Remove(noteId);
            if (forgetExpected) _expectedFirstRenderNoteIds.Remove(noteId);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _deferredRuntimeComposition = null;
            StopDeferredStartupWork();
            _expectedFirstRenderNoteIds.Clear();
            _renderedFirstRenderNoteIds.Clear();
        }

        private void BeginRuntimeComposition()
        {
            Task.Run(delegate
            {
                return StickyFeature.PrepareLoad();
            }).ContinueWith(delegate(Task<StickyLoadResult> task)
            {
                Exception failure = task.IsFaulted && task.Exception != null
                    ? task.Exception.GetBaseException() : null;
                StickyLoadResult prepared = task.Status ==
                    TaskStatus.RanToCompletion ? task.Result : null;

                if (IsStopping) return;
                try
                {
                    _host.Window.BeginInvoke((MethodInvoker)delegate
                    {
                        CompleteRuntimeComposition(prepared, failure);
                    });
                }
                catch (InvalidOperationException)
                {
                    // The shell closed while disk preparation was completing.
                    // Never recreate windows or publish late runtime state.
                }
            }, TaskScheduler.Default);
        }

        private void CompleteRuntimeComposition(
            StickyLoadResult prepared, Exception failure)
        {
            if (IsStopping) return;

            if (DeferRuntimeCompositionWhilePersisting(
                () => CompleteRuntimeComposition(prepared, failure))) return;

            UnsupportedStickySchemaException future =
                failure as UnsupportedStickySchemaException ??
                StickyFeature.PreparedFutureSchemaError(prepared);
            if (future != null)
            {
                MessageBox.Show(_host.Window, PennyApplicationHost.BuildFutureSchemaBlockedMessage(future),
                    "Penny pet - 便利贴数据版本不兼容",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _host.AbortStartupComposition();
                return;
            }

            if (failure != null)
            {
                ApplicationDiagnostics.ReportFatal(
                    "runtime-composition-load", failure);
                MessageBox.Show(_host.Window,
                    "Penny pet 无法安全恢复便利贴数据。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _host.AbortStartupComposition();
                return;
            }

            try
            {
                _host.PublishPreparedStickyRuntime(prepared);
            }
            catch (UnsupportedStickySchemaException error)
            {
                MessageBox.Show(_host.Window, PennyApplicationHost.BuildFutureSchemaBlockedMessage(error),
                    "Penny pet - 便利贴数据版本不兼容",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _host.AbortStartupComposition();
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportFatal(
                    "runtime-composition-publish", error);
                MessageBox.Show(_host.Window,
                    "Penny pet 无法完成后台功能初始化。诊断记录已保存到：\n" +
                    ApplicationDiagnostics.LogFilePath,
                    "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _host.AbortStartupComposition();
            }
        }

        private enum StartupWorkPhase
        {
            StartInputs,
            ApplyStartupPreferences,
            WaitForStickyRuntime,
            RestoreNotes
        }

        internal void BeginDeferredStartupWork()
        {
            if (IsStopping || _startupWorkTimer != null) return;
            _startupWorkPhase = StartupWorkPhase.StartInputs;
            _startupWorkTimer = new System.Windows.Forms.Timer();
            // Run often enough to keep the shell responsive, but restore as
            // many notes as fit in one small UI budget instead of imposing a
            // fixed 90 ms delay per note.
            _startupWorkTimer.Interval = 16;
            _startupWorkTimer.Tick += DeferredStartupTick;
            _startupWorkTimer.Start();
        }

        private void DeferredStartupTick(object sender, EventArgs e)
        {
            if (_host.PersistenceActive) return;
            if (IsStopping)
            {
                StopDeferredStartupWork();
                return;
            }
            if (_startupWorkPhase == StartupWorkPhase.StartInputs)
            {
                if (PetKeyboardPrivacyPolicy.ShouldStartHook(
                    _host.Settings.ShowKeyOverlay,
                    _host.Settings.KeyboardPrivacyNoticeAccepted))
                {
                    try
                    {
                        _host.Keyboard.Start();
                    }
                    catch (Exception error)
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-keyboard-start", error);
                    }
                }
                _host.RefreshKeyboardMenu();
                _startupWorkPhase = StartupWorkPhase.ApplyStartupPreferences;
                return;
            }
            if (_startupWorkPhase == StartupWorkPhase.ApplyStartupPreferences)
            {
                try
                {
                    if (!StartupRegistration.Apply(_host.Settings.StartAtLogin,
                        out string startupError))
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-startup-registration",
                            new InvalidOperationException(startupError));
                    }
                    _host.Settings.SaveIfChangedAsync();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-secondary-startup", error);
                }
                _startupWorkPhase = StartupWorkPhase.WaitForStickyRuntime;
                return;
            }
            if (_startupWorkPhase == StartupWorkPhase.WaitForStickyRuntime)
            {
                if (_host.Notes == null || _host.Workspace == null ||
                    _host.Reminders == null) return;
                try
                {
                    _host.Reminders.Tick(DateTime.UtcNow);
                    _startupVisibleNotes = BuildStartupRestoreQueue();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-sticky-startup", error);
                    _startupVisibleNotes = new Queue<StickyNoteData>();
                }
                _startupWorkPhase = StartupWorkPhase.RestoreNotes;
                return;
            }
            if (_startupVisibleNotes != null &&
                _startupVisibleNotes.Count > 0)
            {
                // Pet STA only feeds immutable restore work. The Sticky STA owns
                // the real 6 ms construction budget.
                StickyNoteData note = _startupVisibleNotes.Dequeue();
                try
                {
                    _host.Workspace.QueueStartupStickyRestore(note);
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-sticky-restore", error);
                    _host.Workspace.RecoverFailedHostedStickyWindow(note);
                }
                return;
            }
            if (!AllExpectedNotesHaveFirstRendered()) return;
            try
            {
                _host.Workspace.Dock.NormalizeAllDockGroups();
                _host.RefreshMenu();
                _host.Workspace.RefreshNoteTabs();
                if (_host.Notes.RecoveredFromLoadFailure)
                    _host.ShowBubble("检测到旧便利贴数据异常，原文件已经保留备份，" +
                        "新建功能已自动恢复。");
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "deferred-startup-finalize", error);
            }
            EventHandler backgroundReady = StartupBackgroundReady;
            if (backgroundReady != null)
                backgroundReady(_host.Window, EventArgs.Empty);
            StopDeferredStartupWork();
        }

        private void TryRaiseShellReady()
        {
            if (_shellReadyRaised || !_startupArtReady ||
                IsStopping) return;
            _startupDisplaySuppressed = false;
            _shellReadyRaised = true;
            _host.RenderFrame();
            EventHandler ready = ShellReady;
            if (ready != null) ready(_host.Window, EventArgs.Empty);
            TryBeginRuntimeComposition();
        }

        private Queue<StickyNoteData> BuildStartupRestoreQueue()
        {
            _expectedFirstRenderNoteIds.Clear();
            _renderedFirstRenderNoteIds.Clear();
            Queue<StickyNoteData> result = new Queue<StickyNoteData>();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _host.Notes.GetAll())
            {
                if (!note.Visible || restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    _host.Workspace.Dock.BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                {
                    restored.Add(member.Id);
                    _expectedFirstRenderNoteIds.Add(member.Id);
                }
                result.Enqueue(note);
            }
            return result;
        }

        private bool AllExpectedNotesHaveFirstRendered()
        {
            foreach (string noteId in _expectedFirstRenderNoteIds)
                if (!_renderedFirstRenderNoteIds.Contains(noteId)) return false;
            return true;
        }

        internal void MarkFirstRendered(string noteId)
        {
            if (String.IsNullOrEmpty(noteId)) return;
            _renderedFirstRenderNoteIds.Add(noteId);
        }

        private void StopDeferredStartupWork()
        {
            if (_startupWorkTimer == null) return;
            _startupWorkTimer.Stop();
            _startupWorkTimer.Tick -= DeferredStartupTick;
            _startupWorkTimer.Dispose();
            _startupWorkTimer = null;
            _startupVisibleNotes = null;
        }

    }
}
