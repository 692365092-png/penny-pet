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
                if (TryStartHostedSticky(note, true))
                {
                    RefreshMenuText();
                    return;
                }
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
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
                if (TryStartHostedSticky(note, true))
                {
                    RefreshMenuText();
                    return;
                }
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
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
                if (TryStartHostedSticky(note, true))
                {
                    RefreshMenuText();
                    return;
                }
                ShowStickyNote(note, true);
                PlaceNewStickyWindowOnPetScreen(note);
                EnsureCreatedStickyWindowVisible(note);
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

        private void EnsureCreatedStickyWindowVisible(StickyNoteData note)
        {
            if (note == null) throw new ArgumentNullException("note");
            if (IsHostedSticky(note)) return;
            StickyNoteWindow form;
            if (!_noteWindows.TryGetValue(note.Id, out form) || form == null ||
                form.IsDisposed)
                throw new InvalidOperationException("便利贴窗口没有创建成功。");
            if (!form.Visible)
            {
                form.ShowAndEdit();
                form.EnableWinFormsKeyboardInterop();
            }
            if (!form.Visible)
                throw new InvalidOperationException("便利贴窗口创建后仍不可见。");
        }

        private float PetScreenScale()
        {
            try
            {
                using (Graphics graphics = CreateGraphics())
                {
                    float scale = graphics.DpiX / 96F;
                    if (scale >= 0.75F && scale <= 4F) return scale;
                }
            }
            catch { }
            return 1F;
        }

        private static Size StickyPhysicalSize(StickyNoteWindow form,
            float scale)
        {
            return new Size(Math.Max(1, (int)Math.Round(form.Width * scale)),
                Math.Max(1, (int)Math.Round(form.Height * scale)));
        }

        private void PlaceNewStickyWindowOnPetScreen(StickyNoteData note)
        {
            if (IsHostedSticky(note)) return;
            StickyNoteWindow form;
            if (note == null || !_noteWindows.TryGetValue(note.Id, out form) ||
                form == null || form.IsDisposed) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            float scale = PetScreenScale();
            Size size = StickyPhysicalSize(form, scale);
            int offset = (_notes.GetAll().Count % 7) * 18;
            int x = Left - size.Width - 12 - offset;
            if (x < work.Left)
                x = Math.Min(work.Right - size.Width, Right + 12 + offset);
            int y = Top + offset;
            x = Math.Max(work.Left, Math.Min(x, work.Right - size.Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - size.Height));
            form.ShowRestoredAtPhysicalBounds(new Rectangle(x, y,
                size.Width, size.Height));
            form.EnableWinFormsKeyboardInterop();
            form.BringToFront();
            form.FocusPrimaryInputForTest();
            note.X = form.Left;
            note.Y = form.Top;
            note.Width = form.Width;
            note.Height = form.Height;
            _notes.Save();
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
                    if (IsHostedSticky(note))
                    {
                        PostHostedStickyCommand(
                            StickyUiCommand.Show(note.Id, false),
                            delegate(StickyUiCommandResult result)
                            {
                                if (result != null && result.Status ==
                                    StickyUiCommandStatus.Handled)
                                {
                                    ApplyHostedStickySnapshot(result.Snapshot,
                                        result.Sequence, false);
                                    ApplyDockTarget(target, null);
                                }
                                else ReportHostedStickyCommandFailure(
                                    "sticky-hosted-expand-and-tile", result);
                            });
                        continue;
                    }
                    try
                    {
                        ShowStickyNote(note, false, false, false);
                        ApplyDockTarget(target, null);
                        StickyNoteWindow form;
                        if (_noteWindows.TryGetValue(note.Id, out form) &&
                            form != null && !form.IsDisposed)
                        {
                            form.EnableWinFormsKeyboardInterop();
                            form.BringToFront();
                        }
                    }
                    catch (Exception error)
                    {
                        ApplicationDiagnostics.ReportNonFatal(
                            "sticky-legacy-expand-and-tile", error);
                    }
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
            StickyNoteWindow form;
            if (_noteWindows.TryGetValue(note.Id, out form) && form != null &&
                !form.IsDisposed)
                form.CloseForApplicationExit();
            _noteWindows.Remove(note.Id);
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

        private StickyNoteWindow GetOrCreateStickyNoteWindow(StickyNoteData note)
        {
            StickyNoteWindow existing;
            if (_noteWindows.TryGetValue(note.Id, out existing) && !existing.IsDisposed)
                return existing;
            WpfApplicationHost.Ensure();
            StickyNoteRepository.RepairForDisplay(note, false);
            StickyNoteWindow form;
            try { form = new StickyNoteWindow(note); }
            catch (Exception firstError)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-window-legacy-first-open", firstError);
                // A WPF/native-window failure is not proof that user data is
                // damaged. Retry once without mutating the note; callers can
                // report the second failure while the original data stays safe.
                form = new StickyNoteWindow(note);
            }
            LegacyStickyWindowCreatedCount++;
            form.NoteChanged += delegate
            {
                _notes.SaveAsync();
                RefreshMenuText();
            };
            form.Shown += delegate { MarkFirstRendered(note.Id); };
            form.HeaderDragStarted += StickyNoteHeaderDragStarted;
            form.HeaderDragMoved += StickyNoteHeaderDragMoved;
            form.HeaderDragCompleted += StickyNoteHeaderDragCompleted;
            form.CloseRequested += StickyNoteCloseRequested;
            form.PinStateChanged += StickyNotePinStateChanged;
            form.SizeChanged += StickyNoteSizeChanged;
            form.LocationChanged += StickyNoteLocationChanged;
            form.DockHorizontalResizing += StickyNoteDockHorizontalResizing;
            form.NewNoteRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateStickyNote(String.Empty);
                }, "sticky-note-window-create");
            };
            form.NewTodoRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateTodoStickyNote();
                }, "sticky-todo-window-create");
            };
            form.NewScheduleRequested += delegate
            {
                QueueStickyWindowAction(delegate
                {
                    CreateScheduleStickyNote();
                }, "sticky-schedule-window-create");
            };
            form.TypingActivity += delegate
            {
                TriggerTypingAnimation();
            };
            form.CancelReminderRequested += delegate { CancelReminderForNote(note, true); };
            form.ModifyReminderRequested += delegate(object sender,
                ReminderActionEventArgs e)
            {
                EditReminder(e.Reminder);
            };
            form.DeleteReminderRequested += delegate(object sender,
                ReminderActionEventArgs e)
            {
                CancelReminder(e.Reminder, true);
            };
            form.DeleteRequested += delegate
            {
                if (MessageBox.Show(form, "确定删除这张便利贴吗？此操作无法撤销。",
                    "删除便利贴", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
                    DeleteStickyNote(note);
            };
            form.FormClosed += delegate { _noteWindows.Remove(note.Id); };
            _noteWindows[note.Id] = form;
            form.UpdateReminderBanner(_reminders.GetItems());
            return form;
        }

        private bool TryStartHostedSticky(StickyNoteData note,
            bool focusEditor)
        {
            if (!IsHostedStickyEligible(note)) return false;
            StickyNoteWindow legacy;
            if (_noteWindows.TryGetValue(note.Id, out legacy) &&
                legacy != null && !legacy.IsDisposed) return false;
            string noteId = note.Id;
            if (!_hostedRuntime.AddNote(noteId)) return true;
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
                    FallBackHostedStickyToLegacy(noteId, focusEditor,
                        "sticky-hosted-create", result);
                });
            return true;
        }

        private static bool IsHostedStickyEligible(StickyNoteData note)
        {
            return note != null &&
                String.IsNullOrEmpty(note.DockParentId) &&
                String.IsNullOrEmpty(note.DockGroupId);
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
                    FallBackHostedStickyToLegacy(noteId, focusEditor,
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
                    BeginStickyDockDrag(facts, null);
                else if (value.Kind == StickyUiEventKind.HeaderDragMoved)
                    MoveStickyDockDrag(facts, null);
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

        private void FallBackHostedStickyToLegacy(string noteId,
            bool focusEditor, string context, StickyUiCommandResult result)
        {
            if (!_hostedRuntime.ContainsNote(noteId)) return;
            ClearHostedDockResizeSessionIfMember(noteId);
            _hostedRuntime.RemoveNote(noteId);
            ReportHostedStickyCommandFailure(context, result);
            _renderedFirstRenderNoteIds.Remove(noteId);
            _expectedFirstRenderNoteIds.Add(noteId);
            StickyNoteData note = _notes.Find(noteId);
            if (note == null) return;
            try
            {
                ShowStickyNote(note, focusEditor, true, false);
                EnsureCreatedStickyWindowVisible(note);
            }
            catch (Exception error)
            {
                ShowStickyWindowFailure("便利贴", error);
            }
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

        private void ConfirmHostedStickyDelete(string noteId)
        {
            StickyNoteData note = _notes.Find(noteId);
            if (note == null || !IsHostedSticky(note)) return;
            if (MessageBox.Show(this,
                "确定删除这张便利贴吗？此操作无法撤销。", "删除便利贴",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
                DialogResult.Yes) DeleteStickyNote(note);
        }

        private void RecoverFailedLegacyStickyWindow(StickyNoteData note)
        {
            if (note == null) return;
            StickyNoteWindow failed;
            if (_noteWindows.TryGetValue(note.Id, out failed))
            {
                _noteWindows.Remove(note.Id);
                if (failed != null && !failed.IsDisposed)
                {
                    try { failed.CloseForApplicationExit(); }
                    catch { }
                }
            }
            // Do not apply destructive data repair for a temporary UI failure.
            // The note remains in the repository and can be retried later.
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void ShowStickyNote(StickyNoteData note, bool focusEditor)
        {
            ShowStickyNote(note, focusEditor, true);
        }

        private void ShowStickyNote(StickyNoteData note, bool focusEditor,
            bool persistVisibility)
        {
            ShowStickyNote(note, focusEditor, persistVisibility, true);
        }

        private void ShowStickyNote(StickyNoteData note, bool focusEditor,
            bool persistVisibility, bool allowHosted)
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
                RestoreStickyDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility);
                return;
            }
            if (PostHostedStickyShow(note, focusEditor)) return;
            if (allowHosted && TryStartHostedSticky(note, focusEditor)) return;
            StickyNoteWindow form = GetOrCreateStickyNoteWindow(note);
            if (focusEditor) form.ShowAndEdit();
            else form.ShowRestored();
            form.EnableWinFormsKeyboardInterop();
            if (!focusEditor && persistVisibility) _notes.Save();
            RefreshNoteTabs();
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

            List<string> createdHostedIds = new List<string>();
            foreach (StickyNoteData member in ordered)
            {
                if (member == null || _hostedRuntime.ContainsNote(member.Id) ||
                    !_hostedRuntime.AddNote(member.Id))
                {
                    foreach (string createdId in createdHostedIds)
                        _hostedRuntime.RemoveNote(createdId);
                    return false;
                }
                createdHostedIds.Add(member.Id);
                HostedStickyWindowCreatedCount++;
            }

            int pending = ordered.Count;
            bool createFailed = false;
            for (int index = 0; index < ordered.Count; index++)
            {
                StickyNoteData member = ordered[index];
                Rectangle bounds = layout[index];
                int dividerMinimum = 220;
                int dividerMaximum = 700;
                PostHostedStickyCommand(
                    StickyUiCommand.Create(StickyNoteUiSnapshot.FromData(
                        member), false, _reminders.GetItems()),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result == null ||
                            result.Status != StickyUiCommandStatus.Handled)
                        {
                            createFailed = true;
                            ReportHostedStickyCommandFailure(
                                "sticky-hosted-dock-create", result);
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
                                        index == 0, true, index < ordered.Count - 1,
                                        dividerMinimum, dividerMaximum)),
                                delegate(StickyUiCommandResult roleResult) { });
                            PostHostedStickyCommand(
                                StickyUiCommand.Show(member.Id, false),
                                delegate(StickyUiCommandResult showResult) { });
                        }
                        if (Interlocked.Decrement(ref pending) == 0 &&
                            createFailed)
                        {
                            foreach (string createdId in createdHostedIds)
                            {
                                _hostedRuntime.RemoveNote(createdId);
                                PostHostedStickyCommand(
                                    StickyUiCommand.Close(createdId),
                                    delegate(StickyUiCommandResult closeResult) { });
                            }
                            RestoreStickyDockComponent(ordered, focus,
                                focusEditor, persistVisibility);
                        }
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
                    if (PostHostedStickyHide(member)) continue;
                    StickyNoteWindow form;
                    if (_noteWindows.TryGetValue(member.Id, out form) &&
                        form != null && !form.IsDisposed)
                        form.HideAsDockGroupMember();
                    else member.Visible = false;
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
                ShowStickyNote(note, false);
            }
            if (hidden.Count > 0)
            {
                StickyNoteWindow first;
                if (_noteWindows.TryGetValue(hidden[0].Id, out first) &&
                    first != null && !first.IsDisposed)
                    first.FocusPrimaryInputForTest();
            }
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
                delegate { CreateStickyNote(String.Empty); },
                delegate(StickyNoteData note) { ShowStickyNote(note, true); },
                delegate(StickyNoteData note) { HideStickyNote(note); },
                delegate(StickyNoteData note) { DeleteStickyNote(note); }))
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
                    ShowStickyNote(showRequested, true);
                    EnsureCreatedStickyWindowVisible(showRequested);
                }, "sticky-manager-show");
            RefreshMenuText();
        }

    }
}
