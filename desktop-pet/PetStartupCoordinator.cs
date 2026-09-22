using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PennyPet
{
    // Coordinates deferred Windows startup without changing phase ordering.
    internal sealed partial class PetForm
    {
        private enum StartupWorkPhase
        {
            StartInputs,
            ApplyStartupPreferences,
            RestoreNotes
        }

        private void BeginDeferredStartupWork()
        {
            if (_startupWorkTimer != null) return;
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
            if (_exiting || IsDisposed)
            {
                StopDeferredStartupWork();
                return;
            }
            if (_startupWorkPhase == StartupWorkPhase.StartInputs)
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
                _startupWorkPhase = StartupWorkPhase.ApplyStartupPreferences;
                return;
            }
            if (_startupWorkPhase == StartupWorkPhase.ApplyStartupPreferences)
            {
                try
                {
                    if (!StartupRegistration.Apply(_settings.StartAtLogin,
                        out string startupError))
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-startup-registration",
                            new InvalidOperationException(startupError));
                    }
                    _settings.SaveAsync();
                    ReminderTick(null, EventArgs.Empty);
                    _startupVisibleNotes = BuildStartupRestoreQueue();
                }
                catch (Exception error)
                {
                    ApplicationDiagnostics.ReportNonFatal(
                        "deferred-secondary-startup", error);
                    _startupVisibleNotes = new Queue<StickyNoteData>();
                }
                _startupWorkPhase = StartupWorkPhase.RestoreNotes;
                return;
            }
            if (_startupVisibleNotes != null &&
                _startupVisibleNotes.Count > 0)
            {
                Stopwatch budget = Stopwatch.StartNew();
                while (_startupVisibleNotes.Count > 0 &&
                    budget.ElapsedMilliseconds < 6)
                {
                    StickyNoteData note = _startupVisibleNotes.Dequeue();
                    try
                    {
                        _stickyWorkspace.ShowHostedSticky(note, false, false);
                    }
                    catch (Exception error)
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "deferred-sticky-restore", error);
                        _stickyWorkspace.RecoverFailedHostedStickyWindow(note);
                    }
                }
                return;
            }
            if (!AllExpectedNotesHaveFirstRendered()) return;
            try
            {
                _stickyWorkspace.Dock.NormalizeAllDockGroups();
                RefreshMenuText();
                _stickyWorkspace.RefreshNoteTabs();
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
            if (_startupReadyRaised || !PetStartupRules.CanReleaseStartupLoading(
                _startupUiReady, _startupArtReady) ||
                IsDisposed || _exiting) return;
            _startupDisplaySuppressed = false;
            _startupReadyRaised = true;
            RenderCurrentFrame();
            EventHandler ready = StartupReady;
            if (ready != null) ready(this, EventArgs.Empty);
        }

        private Queue<StickyNoteData> BuildStartupRestoreQueue()
        {
            _expectedFirstRenderNoteIds.Clear();
            _renderedFirstRenderNoteIds.Clear();
            Queue<StickyNoteData> result = new Queue<StickyNoteData>();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    _stickyWorkspace.Dock.BuildDockChainOrderIncludingHidden(note);
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
