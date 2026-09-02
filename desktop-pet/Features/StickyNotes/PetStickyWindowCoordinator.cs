using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Coordinates sticky-note window lifetime and placement from the pet.
    // Dock relationship algorithms remain in PetStickyDockCoordinator.
    internal sealed partial class PetForm
    {
        private void CreateStickyNote(string text)
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(text);
                if (note == null) return;
                StartHostedSticky(note, true);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("便利贴", error);
            }
        }

        private void CreateTodoStickyNote()
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(String.Empty);
                if (note == null) return;
                note.IsTodoList = true;
                note.Title = "待办清单";
                _notes.Save();
                StartHostedSticky(note, true);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("待办清单", error);
            }
        }

        private void CreateScheduleStickyNote()
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(String.Empty);
                if (note == null) return;
                note.IsTodoList = false;
                note.IsSchedule = true;
                note.Title = "日程";
                note.FontSizeTwips = 320;
                note.Height = 360;
                _notes.Save();
                StartHostedSticky(note, true);
                RefreshMenuText();
            }
            catch (Exception error)
            {
                RollBackFailedStickyCreation(note);
                ShowStickyWindowFailure("日程", error);
            }
        }

        private void QueueStickyWindowAction(Action action, string context)
        {
            if (action == null || IsDisposed || Disposing) return;
            if (_menu != null && _menu.Visible) _menu.Close();
            BeginInvoke((MethodInvoker)delegate
            {
                try { action(); }
                catch (Exception error) { ShowStickyWindowFailure(context, error); }
            });
        }

        private void ExpandAndTileAllStickyNotesToPetScreen()
        {
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            List<DockLayoutTarget> targets =
                PrepareStickyExpandAndTileTargets(_notes.GetAll(), work);
            if (targets.Count == 0)
            {
                ShowBubble("当前没有便利贴。");
                return;
            }

            // Persist the complete canonical transition before asynchronous
            // hosted effects can report their detached snapshots back.
            _notes.Save();
            _movingDockGroup = true;
            try
            {
                foreach (DockLayoutTarget target in targets)
                {
                    StickyNoteData note = _notes.Find(target.NoteId);
                    if (note == null) continue;
                    ShowHostedSticky(note, false, false);
                    ApplyDockTarget(target, null);
                }
            }
            finally { _movingDockGroup = false; }
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
            ShowBubble("已展开并平铺 " + targets.Count +
                " 张便利贴到当前屏幕。");
        }

        internal static List<DockLayoutTarget>
            PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,
                Rectangle work)
        {
            List<StickyNoteData> active = new List<StickyNoteData>();
            List<Size> sizes = new List<Size>();
            if (notes != null)
            {
                foreach (StickyNoteData note in notes)
                {
                    if (note == null) continue;
                    active.Add(note);
                    sizes.Add(new Size(
                        Math.Min(Math.Max(1, work.Width),
                            Math.Max(280, Math.Min(900, note.Width))),
                        Math.Min(Math.Max(1, work.Height),
                            Math.Max(220, Math.Min(700, note.Height)))));
                }
            }
            List<Rectangle> layout = CalculateStickyRecoveryLayout(work,
                sizes);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            for (int index = 0; index < active.Count; index++)
            {
                StickyNoteData note = active[index];
                Rectangle bounds = layout[index];
                bounds.Size = sizes[index];
                Point delta = CalculateHeaderReachableTranslation(
                    new Rectangle(bounds.Left, bounds.Top, bounds.Width, 32),
                    work);
                bounds.Offset(delta);
                StickyDockGroups.ClearMembership(note);
                note.Visible = true;
                note.X = bounds.X;
                note.Y = bounds.Y;
                note.Width = bounds.Width;
                note.Height = bounds.Height;
                targets.Add(new DockLayoutTarget(note.Id, bounds.X, bounds.Y,
                    bounds.Width, bounds.Height, true, note.AlwaysOnTop));
            }
            return targets;
        }

        internal static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes)
        {
            return CalculateStickyRecoveryLayout(work, componentSizes, 1F);
        }

        private static List<Rectangle> CalculateStickyRecoveryLayout(
            Rectangle work, IList<Size> componentSizes, float scale)
        {
            List<DockSize> dockSizes = new List<DockSize>();
            if (componentSizes != null)
            {
                foreach (Size size in componentSizes)
                    dockSizes.Add(new DockSize
                    {
                        Width = size.Width,
                        Height = size.Height
                    });
            }
            List<DockRect> dockLayout =
                StickyDockGeometry.CalculateStickyRecoveryLayout(
                    new DockRect
                    {
                        Left = work.Left,
                        Top = work.Top,
                        Width = work.Width,
                        Height = work.Height
                    },
                    dockSizes,
                    scale);
            List<Rectangle> result = new List<Rectangle>();
            foreach (DockRect item in dockLayout)
                result.Add(new Rectangle(item.Left, item.Top,
                    item.Width, item.Height));
            return result;
        }

        internal static Point CalculateStickyRecoveryAnchor(Rectangle work,
            Rectangle pet, Size window, int componentIndex)
        {
            DockPoint anchor = StickyDockGeometry.CalculateStickyRecoveryAnchor(
                new DockRect
                {
                    Left = work.Left,
                    Top = work.Top,
                    Width = work.Width,
                    Height = work.Height
                },
                new DockRect
                {
                    Left = pet.Left,
                    Top = pet.Top,
                    Width = pet.Width,
                    Height = pet.Height
                },
                new DockSize
                {
                    Width = window.Width,
                    Height = window.Height
                },
                componentIndex);
            return new Point(anchor.X, anchor.Y);
        }

        private void RollBackFailedStickyCreation(StickyNoteData note)
        {
            if (note == null) return;
            _notes.Remove(note);
            RefreshMenuText();
            RefreshNoteTabs();
        }

        private void ShowStickyWindowFailure(string kind, Exception error)
        {
            ApplicationDiagnostics.ReportNonFatal(kind ?? "sticky-window", error);
            MessageBox.Show(this,
                "未能显示" + (String.IsNullOrEmpty(kind) ? "便利贴" : kind) +
                "。程序没有保留不可见的空白项目。\n\n" +
                "请把下面的诊断文件发给作者：\n" +
                ApplicationDiagnostics.LogFilePath,
                "Penny pet", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private StickyNoteData CreateStickyNoteData(string text)
        {
            if (!_notes.CanCreate)
            {
                if (!_notes.LoadSucceeded)
                    ShowBubble("旧便利贴数据暂时无法安全恢复，请查看诊断记录。" +
                        "程序没有覆盖原文件。");
                else
                    ShowBubble("便利贴最多可以保存 " +
                        StickyNoteLimits.MaximumNotes +
                        " 张，请先删除不需要的便利贴。");
                return null;
            }
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int offset = (_notes.GetAll().Count % 7) * 18;
            int x = Left - 332 - offset;
            if (x < work.Left) x = Math.Min(work.Right - 332, Right + 12 + offset);
            int y = Math.Max(work.Top, Math.Min(Top + offset, work.Bottom - 312));
            StickyNoteData note = _notes.Create(text, new Point(x, y));
            if (note == null)
            {
                ShowBubble("便利贴创建失败，原有数据没有被修改。请查看诊断记录。");
                return null;
            }
            note.Width = 320;
            note.Height = 300;
            note.Visible = true;
            _notes.Save();
            return note;
        }

        private void StartHostedSticky(StickyNoteData note,
            bool focusEditor)
        {
            if (note == null) return;
            string noteId = note.Id;
            if (!_hostedRuntime.AddNote(noteId))
            {
                PostHostedStickyShow(note, focusEditor);
                return;
            }
            HostedStickyWindowCreatedCount++;
            StickyUiCommand command = StickyUiCommand.Create(
                StickyNoteUiSnapshot.FromData(note), focusEditor,
                _reminders.GetItems());
            PostHostedStickyCommand(command,
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence);
                        return;
                    }
                    HandleHostedStickyFailure(new string[] { noteId },
                        "sticky-hosted-create", result);
                });
        }

        private bool IsHostedSticky(StickyNoteData note)
        {
            return note != null && _hostedRuntime.ContainsNote(note.Id);
        }

        private bool PostHostedStickyShow(StickyNoteData note,
            bool focusEditor)
        {
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            PostHostedStickyCommand(StickyUiCommand.Show(noteId, focusEditor),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence);
                        return;
                    }
                    HandleHostedStickyFailure(new string[] { noteId },
                        "sticky-hosted-show", result);
                });
            return true;
        }

        private bool PostHostedStickyHide(StickyNoteData note)
        {
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            PostHostedStickyCommand(StickyUiCommand.Hide(noteId),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (result.Snapshot != null &&
                            !result.Snapshot.Visible)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence);
                        return;
                    }
                    ReportHostedStickyCommandFailure(
                        "sticky-hosted-hide", result);
                });
            return true;
        }

        private void PostHostedStickyCommand(StickyUiCommand command,
            Action<StickyUiCommandResult> completed)
        {
            _stickyUiHost.PostCommand(command, completed, _petUiContext);
        }

        private void HostedStickyEventReceived(StickyUiEvent value)
        {
            if (value == null || IsDisposed || Disposing ||
                !_hostedRuntime.ContainsNote(value.NoteId)) return;
            if (value.Kind == StickyUiEventKind.TypingActivity)
            {
                if (!_exiting) TriggerTypingAnimation();
                return;
            }
            if (value.Kind == StickyUiEventKind.InputFocusChanged)
            {
                _hostedRuntime.SetInputFocus(value.NoteId, value.Flag);
                return;
            }
            if (value.Kind == StickyUiEventKind.ImeCompositionChanged)
            {
                _hostedRuntime.SetImeComposition(value.NoteId, value.Flag);
                if (!value.Flag)
                {
                    if (_hostedRuntime.ExitRequested &&
                        !_hostedRuntime.HasImeComposition)
                        TryCloseAllHostedStickies();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.FirstRendered)
            {
                MarkFirstRendered(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.HeaderDragStarted ||
                value.Kind == StickyUiEventKind.HeaderDragMoved ||
                value.Kind == StickyUiEventKind.HeaderDragCompleted)
            {
                if (!ApplyHostedStickySnapshot(value.Snapshot,
                    value.Sequence, false)) return;
                DockWindowFacts facts =
                    DockWindowFacts.FromSnapshot(value.Snapshot);
                if (value.Kind == StickyUiEventKind.HeaderDragStarted)
                    BeginStickyDockDrag(facts);
                else if (value.Kind == StickyUiEventKind.HeaderDragMoved)
                    MoveStickyDockDrag(facts);
                else CompleteStickyDockDrag(facts);
                return;
            }
            if (value.Kind == StickyUiEventKind.BoundsChanged)
            {
                ApplyHostedStickySnapshot(value.Snapshot, value.Sequence, false);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizeStarted)
            {
                ClearHostedDockResizeSession();
                if (!ApplyHostedStickySnapshot(value.Snapshot,
                    value.Sequence, false) ||
                    !BeginHostedStickyDockDivider(
                        DockWindowFacts.FromSnapshot(value.Snapshot)))
                    ClearHostedDockResizeSession();
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizing)
            {
                if (!_hostedRuntime.CanApplySequence(value.NoteId,
                    value.Sequence)) return;
                if (!ResizeHostedStickyDockDivider(value.NoteId,
                    value.Height))
                    ClearHostedDockResizeSession();
                else _hostedRuntime.RecordSequence(value.NoteId,
                    value.Sequence);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizeCompleted)
            {
                CompleteHostedStickyDockDivider(value);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockHorizontalResizing)
            {
                if (!ApplyHostedStickySnapshot(value.Snapshot,
                    value.Sequence, false)) return;
                ResizeStickyDockGroup(
                    DockWindowFacts.FromSnapshot(value.Snapshot),
                    value.Left, value.Width);
                return;
            }
            if (value.Kind == StickyUiEventKind.CloseRequested)
            {
                if (!ApplyHostedStickySnapshot(value.Snapshot,
                    value.Sequence, false)) return;
                CloseStickyDockNote(_notes.Find(value.NoteId),
                    DockWindowFacts.FromSnapshot(value.Snapshot));
                return;
            }
            if (value.Kind == StickyUiEventKind.SnapshotChanged)
            {
                StickyNoteData canonical = _notes.Find(value.NoteId);
                bool topMostChanged = canonical != null &&
                    value.Snapshot != null && canonical.AlwaysOnTop !=
                    value.Snapshot.AlwaysOnTop;
                if (!ApplyHostedStickySnapshot(value.Snapshot,
                    value.Sequence)) return;
                if (topMostChanged)
                {
                    ApplyDockComponentTopMost(canonical,
                        value.Snapshot.AlwaysOnTop, value.NoteId);
                    _notes.SaveAsync();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.Closed)
            {
                ClearHostedDockResizeSession();
                ApplyHostedStickySnapshot(value.Snapshot, value.Sequence);
                _hostedRuntime.RemoveNote(value.NoteId);
                _renderedFirstRenderNoteIds.Remove(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.CancelReminderRequested)
            {
                StickyNoteData note = _notes.Find(value.NoteId);
                if (note != null) CancelReminderForNote(note, true);
                return;
            }
            if (value.Kind == StickyUiEventKind.ModifyReminderRequested)
            {
                if (value.Reminder != null) EditReminder(value.Reminder);
                return;
            }
            if (value.Kind == StickyUiEventKind.DeleteReminderRequested)
            {
                if (value.Reminder != null) CancelReminder(value.Reminder, true);
                return;
            }
            if (value.Kind == StickyUiEventKind.DeleteRequested)
            {
                ConfirmHostedStickyDelete(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.NewNoteRequested)
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-hosted-create-note");
                return;
            }
            if (value.Kind == StickyUiEventKind.NewTodoRequested)
            {
                QueueStickyWindowAction(CreateTodoStickyNote,
                    "sticky-hosted-create-todo");
                return;
            }
            if (value.Kind == StickyUiEventKind.NewScheduleRequested)
            {
                QueueStickyWindowAction(CreateScheduleStickyNote,
                    "sticky-hosted-create-schedule");
                return;
            }
            ApplyHostedStickySnapshot(value.Snapshot, value.Sequence);
        }

        private bool ApplyHostedStickySnapshot(StickyNoteUiSnapshot snapshot,
            long sequence, bool persist = true)
        {
            if (snapshot == null ||
                !_hostedRuntime.CanApplySequence(snapshot.NoteId, sequence))
                return false;
            StickyNoteData canonical = _notes.Find(snapshot.NoteId);
            if (canonical == null) return false;
            bool visibilityChanged = canonical.Visible != snapshot.Visible;
            string oldHiddenTitle = canonical.Visible
                ? String.Empty : canonical.DisplayTitle;
            snapshot.ApplyTo(canonical);
            _hostedRuntime.RecordSequence(snapshot.NoteId, sequence);
            if (persist) _notes.SaveAsync();
            RefreshMenuText();
            if (visibilityChanged || (!canonical.Visible &&
                !String.Equals(oldHiddenTitle, canonical.DisplayTitle,
                    StringComparison.Ordinal))) RefreshNoteTabs();
            return true;
        }

        private void ClearHostedDockResizeSession()
        {
            _activeHostedDockResizeFacts = null;
            _activeHostedDockResizeSourceId = null;
        }

        private void ClearHostedDockResizeSessionIfMember(string noteId)
        {
            if (_activeHostedDockResizeFacts == null) return;
            if (_activeHostedDockResizeFacts.Exists(
                delegate(DockWindowFacts facts)
                {
                    return facts != null && String.Equals(facts.NoteId,
                        noteId, StringComparison.OrdinalIgnoreCase);
                })) ClearHostedDockResizeSession();
        }

        private void CompleteHostedStickyDockDivider(StickyUiEvent value)
        {
            try
            {
                if (value == null || value.Snapshot == null ||
                    !_hostedRuntime.CanApplySequence(value.NoteId,
                        value.Sequence)) return;
                if (!ResizeHostedStickyDockDivider(value.NoteId,
                    value.Height))
                {
                    ApplyHostedStickySnapshot(value.Snapshot,
                        value.Sequence);
                    return;
                }
                StickyNoteData canonical = _notes.Find(value.NoteId);
                if (canonical == null) return;
                value.Snapshot.ApplyTo(canonical);
                canonical.Height = CalculateDockDividerHeight(value.Height);
                _hostedRuntime.RecordSequence(value.NoteId, value.Sequence);
                _notes.SaveAsync();
                RefreshMenuText();
            }
            finally { ClearHostedDockResizeSession(); }
        }

        internal static bool ShouldApplyHostedSequence(long sequence,
            long appliedSequence)
        {
            return sequence > appliedSequence;
        }

        private void HandleHostedStickyFailure(IEnumerable<string> noteIds,
            string context, StickyUiCommandResult result)
        {
            ReportHostedStickyCommandFailure(context, result);
            if (noteIds != null)
            {
                foreach (string noteId in noteIds)
                {
                    if (String.IsNullOrEmpty(noteId)) continue;
                    ClearHostedDockResizeSessionIfMember(noteId);
                    _hostedRuntime.RemoveNote(noteId);
                    _renderedFirstRenderNoteIds.Remove(noteId);
                    _expectedFirstRenderNoteIds.Remove(noteId);
                    StickyNoteData note = _notes.Find(noteId);
                    if (note != null) note.Visible = false;
                    PostHostedStickyCommand(StickyUiCommand.Close(noteId),
                        delegate(StickyUiCommandResult closeResult) { });
                }
            }
            _notes.SaveAsync();
            RefreshNoteTabs();
            RefreshMenuText();
            ShowBubble("便利贴窗口暂时无法显示，内容已保留在侧边页签中。");
        }

        private static void ReportHostedStickyCommandFailure(string context,
            StickyUiCommandResult result)
        {
            string detail = result == null ? "No command result." :
                result.Status + ": " + result.Error;
            ApplicationDiagnostics.ReportNonFatal(context,
                new InvalidOperationException(detail));
        }

        private bool BeginHostedStickyExitIfNeeded()
        {
            if (_hostedRuntime.NoteCount == 0 ||
                _hostedRuntime.ExitPrepared)
                return false;
            ClearHostedDockResizeSession();
            _hostedRuntime.RequestExit();
            TryCloseAllHostedStickies();
            return true;
        }

        private void TryCloseAllHostedStickies()
        {
            if (!_hostedRuntime.TryBeginCloseAll()) return;
            PostHostedStickyCommand(StickyUiCommand.CloseAll(),
                delegate(StickyUiCommandResult result)
                {
                    _hostedRuntime.EndCloseAll();
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.NotAccepted)
                        return;
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        _hostedRuntime.CancelExit();
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-exit", result);
                        ShowBubble("便利贴仍在收尾，退出已取消，请稍后重试。");
                        return;
                    }
                    if (result.FinalSnapshots != null)
                        foreach (StickyUiFinalSnapshot finalSnapshot in
                            result.FinalSnapshots)
                            ApplyHostedStickySnapshot(
                                finalSnapshot.Snapshot,
                                finalSnapshot.Sequence, false);
                    _hostedRuntime.PrepareExit();
                    _stickyUiHost.BeginShutdown();
                    BeginExitSequence();
                });
        }

        private void CloseHostedStickyRuntimeForReload(
            Action<StickyUiCommandResult> completed)
        {
            if (completed == null) return;
            if (_hostedRuntime.NoteCount == 0)
            {
                completed(StickyUiCommandResult.Handled(
                    new StickyUiFinalSnapshot[0]));
                return;
            }
            PostHostedStickyCommand(StickyUiCommand.CloseAll(),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        completed(result);
                        return;
                    }
                    if (result.FinalSnapshots != null)
                        foreach (StickyUiFinalSnapshot finalSnapshot in
                            result.FinalSnapshots)
                        {
                            if (finalSnapshot == null) continue;
                            ApplyHostedStickySnapshot(
                                finalSnapshot.Snapshot,
                                finalSnapshot.Sequence, false);
                            _hostedRuntime.RemoveNote(finalSnapshot.NoteId);
                        }
                    ClearHostedDockResizeSession();
                    completed(result);
                });
        }

        private void ReloadAllHostedStickyRuntime()
        {
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note == null || !note.Visible ||
                    restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
                foreach (StickyNoteData member in group)
                    if (member != null) restored.Add(member.Id);
                ShowHostedSticky(note, false, false);
            }
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ConfirmHostedStickyDelete(string noteId)
        {
            StickyNoteData note = _notes.Find(noteId);
            if (note == null || !IsHostedSticky(note)) return;
            if (MessageBox.Show(this,
                "确定删除这张便利贴吗？此操作无法撤销。", "删除便利贴",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                DialogResult.Yes) DeleteStickyNote(note);
        }

        private void RecoverFailedHostedStickyWindow(StickyNoteData note)
        {
            if (note == null) return;
            HandleHostedStickyFailure(new string[] { note.Id },
                "deferred-sticky-restore",
                StickyUiCommandResult.Failed(new InvalidOperationException(
                    "Hosted sticky restore did not complete.")));
        }

        private void ShowHostedSticky(StickyNoteData note, bool focusEditor)
        {
            ShowHostedSticky(note, focusEditor, true);
        }

        private void ShowHostedSticky(StickyNoteData note, bool focusEditor,
            bool persistVisibility)
        {
            if (note == null) return;
            List<StickyNoteData> storedDockOrder =
                BuildDockChainOrderIncludingHidden(note);
            bool anyHiddenDockMember = storedDockOrder.Exists(
                delegate(StickyNoteData member)
                {
                    return !member.Visible;
                });
            if (StickyDockOperations.ShouldRestoreWholeDockComponent(
                storedDockOrder.Count, anyHiddenDockMember))
            {
                if (TryRestoreHostedDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility))
                    return;
                return;
            }
            if (PostHostedStickyShow(note, focusEditor)) return;
            StartHostedSticky(note, focusEditor);
            if (!focusEditor && persistVisibility) _notes.Save();
            RefreshNoteTabs();
        }

        private void ReloadImportedStickyRuntime(
            StickyImportMergeResult merge)
        {
            if (merge == null || merge.Actions == null) return;
            HashSet<string> requested = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyImportAction action in merge.Actions)
            {
                if (action == null ||
                    String.IsNullOrEmpty(action.ResultNoteId) ||
                    !requested.Add(action.ResultNoteId)) continue;
                StickyNoteData note = _notes.Find(action.ResultNoteId);
                if (note == null || !note.Visible || IsHostedSticky(note))
                    continue;
                // Imported windows use the same restore path as startup and
                // reopen. Existing hosted sessions remain untouched because
                // merge planning never replaces current NoteIds.
                ShowHostedSticky(note, false, false);
            }
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private bool TryRestoreHostedDockComponent(
            List<StickyNoteData> ordered, StickyNoteData focus,
            bool focusEditor, bool persistVisibility)
        {
            if (ordered == null || ordered.Count == 0) return false;
            StickyNoteData rootData = ordered[0];
            int rootWidth = Math.Max(280, Math.Min(900, rootData.Width));
            Rectangle rootHeader = new Rectangle(rootData.X, rootData.Y,
                rootWidth, 32);
            Rectangle work = Screen.FromRectangle(rootHeader).WorkingArea;
            Point translation = CalculateHeaderReachableTranslation(
                rootHeader, work);
            int rootLeft = rootData.X + translation.X;
            int rootTop = rootData.Y + translation.Y;
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData member in ordered)
            {
                sizes.Add(new Size(member.Width, member.Height));
                member.Visible = true;
                member.AlwaysOnTop = rootData.AlwaysOnTop;
            }
            StickyDockGroups.ApplyOrderedGroup(ordered);
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                rootLeft, rootTop, rootWidth);

            List<string> componentIds = new List<string>();
            foreach (StickyNoteData member in ordered)
                if (member != null) componentIds.Add(member.Id);
            if (componentIds.Count != ordered.Count) return false;

            int pending = ordered.Count;
            bool createFailed = false;
            StickyUiCommandResult failureResult = null;
            for (int index = 0; index < ordered.Count; index++)
            {
                int memberIndex = index;
                StickyNoteData member = ordered[index];
                Rectangle bounds = layout[index];
                bool create = !_hostedRuntime.ContainsNote(member.Id);
                if (create)
                {
                    create = _hostedRuntime.AddNote(member.Id);
                    if (create) HostedStickyWindowCreatedCount++;
                }
                int dividerMinimum = 220;
                int dividerMaximum = 700;
                StickyUiCommand initialCommand = create
                    ? StickyUiCommand.Create(StickyNoteUiSnapshot.FromData(
                        member), false, _reminders.GetItems())
                    : StickyUiCommand.Show(member.Id, false);
                PostHostedStickyCommand(
                    initialCommand,
                    delegate(StickyUiCommandResult result)
                    {
                        if (result == null ||
                            result.Status != StickyUiCommandStatus.Handled)
                        {
                            createFailed = true;
                            if (failureResult == null) failureResult = result;
                        }
                        else
                        {
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence);
                            PostHostedStickyCommand(
                                StickyUiCommand.SetBounds(member.Id,
                                    new StickyUiBounds(bounds.Left, bounds.Top,
                                        bounds.Width, bounds.Height)),
                                delegate(StickyUiCommandResult boundsResult) { });
                            PostHostedStickyCommand(
                                StickyUiCommand.SetDockResizeRole(member.Id,
                                    new StickyUiDockResizeRole(true,
                                        memberIndex == 0, true,
                                        memberIndex < ordered.Count - 1,
                                        dividerMinimum, dividerMaximum)),
                                delegate(StickyUiCommandResult roleResult) { });
                            PostHostedStickyCommand(
                                StickyUiCommand.Show(member.Id, focusEditor &&
                                    focus != null && String.Equals(member.Id,
                                        focus.Id,
                                        StringComparison.OrdinalIgnoreCase)),
                                delegate(StickyUiCommandResult showResult) { });
                        }
                        if (Interlocked.Decrement(ref pending) == 0 &&
                            createFailed)
                            HandleHostedStickyFailure(componentIds,
                                "sticky-hosted-dock-create", failureResult);
                    });
            }

            if (persistVisibility) _notes.Save();
            RefreshMenuText();
            RefreshNoteTabs();
            return true;
        }


        private void ReorderStickyNoteTab(StickyNoteData note,
            int destinationIndex)
        {
            _notes.ReorderHidden(note, destinationIndex);
            _noteTabsSignature = String.Empty;
            RefreshNoteTabs();
        }

        private void CollapseAllStickyNotes()
        {
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
                StickyDockGroups.ApplyOrderedGroup(group);
                foreach (StickyNoteData member in group)
                {
                    handled.Add(member.Id);
                    member.Visible = false;
                    PostHostedStickyHide(member);
                }
            }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ExpandAllStickyNoteTabs()
        {
            List<StickyNoteData> hidden = _notes.GetHiddenInTabOrder();
            HashSet<string> restored = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in hidden)
            {
                if (restored.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                foreach (StickyNoteData member in group)
                    restored.Add(member.Id);
                ShowHostedSticky(note, false);
            }
            if (hidden.Count > 0)
                PostHostedStickyCommand(StickyUiCommand.FocusPrimaryInput(
                    hidden[0].Id),
                    delegate(StickyUiCommandResult result) { });
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void RefreshNoteTabs()
        {
            ApplicationDiagnostics.WriteWindowLayerEvent("RefreshNoteTabs",
                "structural");
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            // Side tabs have their own persistent order.  Sorting them by the
            // note's modified time here used to undo every successful drag.
            List<StickyNoteData> hiddenData = _notes.GetHiddenInTabOrder();
            List<SideTabSnapshot> hidden = new List<SideTabSnapshot>();
            foreach (StickyNoteData note in hiddenData)
                hidden.Add(SideTabSnapshot.FromData(note));
            StringBuilder signatureBuilder = new StringBuilder();
            foreach (SideTabSnapshot note in hidden)
            {
                signatureBuilder.Append(note.NoteId).Append('|')
                    .Append(note.DisplayTitle).Append('|')
                    .Append(note.ColorArgb).Append('\n');
            }
            string signature = signatureBuilder.ToString();
            if (String.Equals(signature, _noteTabsSignature,
                StringComparison.Ordinal))
            {
                PositionNoteTabs();
                ApplyNoteTabZOrder();
                return;
            }
            _noteTabsSignature = signature;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(hidden.Count,
                Height, work);
            List<SideTabSnapshot> left = hidden.GetRange(0, leftCount);
            List<SideTabSnapshot> right = hidden.GetRange(leftCount,
                hidden.Count - leftCount);
            _leftNoteTabs.SetNotes(left, 0);
            _rightNoteTabs.SetNotes(right, leftCount);
            PositionNoteTabs();
            ApplyNoteTabZOrder();
        }

        private void ApplyNoteTabZOrder()
        {
            ApplicationDiagnostics.WriteWindowLayerEvent("ApplyNoteTabZOrder",
                "structural");
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            _leftNoteTabs.TopMost =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true);
            _rightNoteTabs.TopMost =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true);
            if (_leftNoteTabs.Visible) _leftNoteTabs.BringToFront();
            if (_rightNoteTabs.Visible) _rightNoteTabs.BringToFront();
        }

        private void PositionNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null ||
                !IsHandleCreated || IsDisposed || _positioningNoteTabs) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            if (!StickyNoteTabsForm.IsLayoutSplitCurrent(
                _leftNoteTabs.Controls.Count, _rightNoteTabs.Controls.Count,
                Height, work))
            {
                _noteTabsSignature = String.Empty;
                RefreshNoteTabs();
                return;
            }
            _positioningNoteTabs = true;
            try
            {
                int reserveLeft = _leftNoteTabs.Controls.Count > 0
                    ? StickyNoteTabsForm.TabWidth -
                        StickyNoteTabsForm.PetOverlapForWidth(Width) + 2 : 0;
                int reserveRight = _rightNoteTabs.Controls.Count > 0
                    ? StickyNoteTabsForm.TabWidth -
                        StickyNoteTabsForm.PetOverlapForWidth(Width) + 2 : 0;
                int minimumLeft = work.Left + reserveLeft;
                int maximumLeft = work.Right - reserveRight - Width;
                if (maximumLeft >= minimumLeft)
                {
                    int adjustedX = Math.Max(minimumLeft,
                        Math.Min(Left, maximumLeft));
                    int adjustedY = Math.Max(work.Top,
                        Math.Min(Top, work.Bottom - Height));
                    if (adjustedX != Left || adjustedY != Top)
                        Location = new Point(adjustedX, adjustedY);
                }
                _leftNoteTabs.ShowNear(Bounds, work);
                _rightNoteTabs.ShowNear(Bounds, work);
            }
            finally
            {
                _positioningNoteTabs = false;
            }
        }

        private void ShowStickyNotesManager()
        {
            bool createRequested = false;
            StickyNoteData showRequested = null;
            using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                delegate { return _notes.GetAll(); },
                new StickyNotesManagerCommands
                {
                    HideNote = delegate(StickyNoteData note)
                    { HideStickyNote(note); },
                    DeleteNote = delegate(StickyNoteData note)
                    { DeleteStickyNote(note); },
                    CollapseAll = CollapseAllStickyNotes,
                    ExpandAll = ExpandAllStickyNoteTabs,
                    TileAll = delegate
                    {
                        QueueStickyWindowAction(
                            ExpandAndTileAllStickyNotesToPetScreen,
                            "sticky-manager-expand-and-tile");
                    },
                    ExportBackup = ExportStickyNotesBackup,
                    PrepareImport = PrepareStickyNotesImport,
                    ConfirmImport = CommitStickyNotesImport,
                    FullRestore = RestoreStickyNotesBackup
                }))
            {
                _windowLayers.ShowModal(this, manager);
                createRequested = manager.CreateRequested;
                showRequested = manager.ShowRequested;
            }
            if (createRequested)
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-manager-create");
            else if (showRequested != null)
                QueueStickyWindowAction(delegate
                {
                    ShowHostedSticky(showRequested, true);
                }, "sticky-manager-show");
            RefreshMenuText();
        }

    }
}
