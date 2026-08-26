using System;
using System.Collections.Generic;

namespace PennyPet
{
    // Coordinates deferred Windows startup without changing phase ordering.
    internal sealed partial class PetForm
    {
        private void BeginDeferredStartupWork()
        {
            if (_startupWorkTimer != null) return;
            _startupWorkPhase = 0;
            _startupWorkTimer = new System.Windows.Forms.Timer();
            _startupWorkTimer.Interval = 90;
            _startupWorkTimer.Tick += DeferredStartupTick;
            _startupWorkTimer.Start();
        }

        private void DeferredStartupTick(object sender, EventArgs e)
        {
            if (_exiting || IsDisposed)
            {
                StopDeferredStartupWork();
                return;
            }
            if (_startupWorkPhase == 0)
            {
                if (PetKeyboardPrivacyPolicy.ShouldStartHook(
                    _settings.ShowKeyOverlay,
                    _settings.KeyboardPrivacyNoticeAccepted))
                {
                    try
                    {
                        _keyboard.Start();
                    }
                    catch (Exception error)
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-keyboard-start", error);
                    }
                }
                RefreshKeyboardMenuText();
                _startupWorkPhase++;
                return;
            }
            if (_startupWorkPhase == 1)
            {
                try
                {
                    string startupError;
                    StartupRegistration.Apply(_settings.StartWithWindows,
                        out startupError);
                    _settings.Save();
                    ReminderTick(null, EventArgs.Empty);
                    _startupVisibleNotes = BuildStartupRestoreQueue();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-secondary-startup", error);
                    _startupVisibleNotes = new Queue<StickyNoteData>();
                }
                _startupWorkPhase++;
                return;
            }
            if (_startupVisibleNotes != null &&
                _startupVisibleNotes.Count > 0)
            {
                StickyNoteData note = _startupVisibleNotes.Dequeue();
                try
                {
                    ShowStickyNote(note, false, false);
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-sticky-restore", error);
                    RecoverFailedLegacyStickyWindow(note);
                }
                return;
            }
            foreach (StickyNoteForm startupNote in _noteWindows.Values)
            {
                if (startupNote != null && !startupNote.IsDisposed &&
                    startupNote.IsVisible &&
                    !startupNote.HasCompletedFirstRender) return;
            }
            try
            {
                NormalizeAllDockGroups();
                RefreshMenuText();
                RefreshNoteTabs();
                if (_notes.RecoveredFromLoadFailure)
                    ShowBubble("检测到旧便利贴数据异常，原文件已经保留备份，" +
                        "新建功能已自动恢复。");
            }
            catch (Exception error)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "deferred-startup-finalize", error);
            }
            _startupUiReady = true;
            TryRaiseStartupReady();
            StopDeferredStartupWork();
        }

        private void TryRaiseStartupReady()
        {
            if (_startupReadyRaised || !CanReleaseStartupLoading(
                _startupUiReady, _startupArtReady) ||
                IsDisposed || _exiting) return;
            _startupDisplaySuppressed = false;
            _startupReadyRaised = true;
            RenderCurrentFrame();
            EventHandler ready = StartupReady;
            if (ready != null) ready(this, EventArgs.Empty);
        }

        internal static bool CanReleaseStartupLoading(bool uiReady,
            bool artReady)
        {
            return uiReady && artReady;
        }

        private Queue<StickyNoteData> BuildStartupRestoreQueue()
        {
            Queue<StickyNoteData> result = new Queue<StickyNoteData>();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                    restored.Add(member.Id);
                result.Enqueue(note);
            }
            return result;
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
