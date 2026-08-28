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

        private void MoveVisibleStickyNotesToPetScreen()
        {
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            float targetScale = PetScreenScale();
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<List<StickyNoteData>> components =
                new List<List<StickyNoteData>>();
            List<List<StickyNoteWindow>> componentForms =
                new List<List<StickyNoteWindow>>();
            List<Size> componentSizes = new List<Size>();
            int attemptedWindows = 0;
            int verifiedWindows = 0;
            _movingDockGroup = true;
            try
            {
                foreach (StickyNoteData seed in _notes.GetAll())
                {
                    if (seed == null || !seed.Visible || visited.Contains(seed.Id))
                        continue;
                    if (IsHostedSticky(seed))
                    {
                        visited.Add(seed.Id);
                        continue;
                    }
                    List<StickyNoteData> component = BuildDockChainOrder(seed);
                    if (component.Count == 0) component.Add(seed);
                    foreach (StickyNoteData note in component)
                        if (note != null) visited.Add(note.Id);

                    List<StickyNoteData> activeNotes =
                        new List<StickyNoteData>();
                    List<StickyNoteWindow> activeForms =
                        new List<StickyNoteWindow>();
                    int componentWidth = 280;
                    int componentHeight = 0;
                    foreach (StickyNoteData note in component)
                    {
                        if (note == null || !note.Visible) continue;
                        StickyNoteWindow form;
                        try
                        {
                            form = GetOrCreateStickyNoteWindow(note);
                            if (!form.Visible) form.ShowRestored();
                            form.EnableWinFormsKeyboardInterop();
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "compat-sticky-recover-create", error);
                            continue;
                        }
                        if (form == null || form.IsDisposed) continue;
                        activeNotes.Add(note);
                        activeForms.Add(form);
                        Size physical = StickyPhysicalSize(form, targetScale);
                        componentWidth = Math.Max(componentWidth,
                            physical.Width);
                        componentHeight += Math.Max(
                            (int)Math.Round(220 * targetScale),
                            physical.Height);
                    }
                    if (activeForms.Count == 0) continue;
                    components.Add(activeNotes);
                    componentForms.Add(activeForms);
                    componentSizes.Add(new Size(componentWidth,
                        Math.Max(220, componentHeight)));
                }

                List<Rectangle> roots = CalculateStickyRecoveryLayout(work,
                    componentSizes, targetScale);
                for (int componentIndex = 0;
                    componentIndex < componentForms.Count; componentIndex++)
                {
                    List<StickyNoteWindow> forms = componentForms[componentIndex];
                    List<StickyNoteData> notes = components[componentIndex];
                    Rectangle root = roots[componentIndex];
                    List<Size> memberSizes = new List<Size>();
                    foreach (StickyNoteWindow form in forms)
                        memberSizes.Add(StickyPhysicalSize(form, targetScale));
                    List<Rectangle> layout = CalculateUnifiedDockLayout(
                        memberSizes, root.Left, root.Top, root.Width,
                        targetScale);
                    for (int memberIndex = 0;
                        memberIndex < forms.Count; memberIndex++)
                    {
                        StickyNoteWindow form = forms[memberIndex];
                        StickyNoteData note = notes[memberIndex];
                        attemptedWindows++;
                        try
                        {
                            form.ShowRestoredAtPhysicalBounds(
                                layout[memberIndex]);
                            form.EnableWinFormsKeyboardInterop();
                            form.BringToFront();
                            Rectangle visiblePart = Rectangle.Intersect(
                                form.PhysicalBounds, work);
                            if (form.Visible &&
                                form.WindowState == FormWindowState.Normal &&
                                visiblePart.Width > 0 && visiblePart.Height > 0)
                                verifiedWindows++;
                            note.X = form.Left;
                            note.Y = form.Top;
                        }
                        catch (Exception error)
                        {
                            ApplicationDiagnostics.ReportNonFatal(
                                "compat-sticky-recover-show", error);
                        }
                    }
                }
            }
            finally { _movingDockGroup = false; }
            if (attemptedWindows > 0)
            {
                _notes.Save();
                ShowBriefBubble("已尝试将 " + attemptedWindows +
                    " 张已展开的便利贴集中到此屏幕；系统确认 " +
                    verifiedWindows + " 张处于可见范围。");
            }
            else ShowBriefBubble("当前没有已展开的便利贴。");
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
            if (!_hostedNoteIds.Add(noteId)) return true;
            _hostedAppliedSequences[noteId] = 0;
            StickyUiCommand command = new StickyUiCommand(
                StickyUiCommandKind.Create, noteId, focusEditor,
                StickyNoteUiSnapshot.FromData(note));
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
            return note != null && _hostedNoteIds.Contains(note.Id);
        }

        private bool PostHostedStickyShow(StickyNoteData note,
            bool focusEditor)
        {
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            PostHostedStickyCommand(new StickyUiCommand(
                StickyUiCommandKind.Show, noteId, focusEditor),
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
            PostHostedStickyCommand(new StickyUiCommand(
                StickyUiCommandKind.Hide, noteId, false),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
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
                !_hostedNoteIds.Contains(value.NoteId)) return;
            if (value.Kind == StickyUiEventKind.TypingActivity)
            {
                if (!_exiting) TriggerTypingAnimation();
                return;
            }
            if (value.Kind == StickyUiEventKind.InputFocusChanged)
            {
                if (value.Flag) _hostedInputFocused.Add(value.NoteId);
                else _hostedInputFocused.Remove(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.ImeCompositionChanged)
            {
                if (value.Flag) _hostedImeComposing.Add(value.NoteId);
                else
                {
                    _hostedImeComposing.Remove(value.NoteId);
                    if (_hostedExitRequested &&
                        _hostedImeComposing.Count == 0)
                        TryCloseAllHostedStickies();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.FirstRendered)
            {
                MarkFirstRendered(value.NoteId);
                return;
            }
            if (value.Kind == StickyUiEventKind.BoundsChanged)
            {
                ApplyHostedStickySnapshot(value.Snapshot, value.Sequence, false);
                return;
            }
            if (value.Kind == StickyUiEventKind.Closed)
            {
                ApplyHostedStickySnapshot(value.Snapshot, value.Sequence);
                _hostedNoteIds.Remove(value.NoteId);
                ForgetHostedStickyState(value.NoteId);
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

        private void ApplyHostedStickySnapshot(StickyNoteUiSnapshot snapshot,
            long sequence, bool persist = true)
        {
            long applied;
            _hostedAppliedSequences.TryGetValue(
                snapshot == null ? String.Empty : snapshot.NoteId, out applied);
            if (snapshot == null || sequence <= applied ||
                !_hostedNoteIds.Contains(snapshot.NoteId)) return;
            StickyNoteData canonical = _notes.Find(snapshot.NoteId);
            if (canonical == null) return;
            bool visibilityChanged = canonical.Visible != snapshot.Visible;
            string oldHiddenTitle = canonical.Visible
                ? String.Empty : canonical.DisplayTitle;
            snapshot.ApplyTo(canonical);
            _hostedAppliedSequences[snapshot.NoteId] = sequence;
            if (persist) _notes.SaveAsync();
            RefreshMenuText();
            if (visibilityChanged || (!canonical.Visible &&
                !String.Equals(oldHiddenTitle, canonical.DisplayTitle,
                    StringComparison.Ordinal))) RefreshNoteTabs();
        }

        private void FallBackHostedStickyToLegacy(string noteId,
            bool focusEditor, string context, StickyUiCommandResult result)
        {
            if (!_hostedNoteIds.Remove(noteId)) return;
            ReportHostedStickyCommandFailure(context, result);
            ForgetHostedStickyState(noteId);
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
            if (_hostedNoteIds.Count == 0 || _hostedExitPrepared)
                return false;
            _hostedExitRequested = true;
            TryCloseAllHostedStickies();
            return true;
        }

        private void TryCloseAllHostedStickies()
        {
            if (!_hostedExitRequested || _hostedCloseAllInFlight ||
                _hostedImeComposing.Count > 0) return;
            _hostedCloseAllInFlight = true;
            PostHostedStickyCommand(new StickyUiCommand(
                StickyUiCommandKind.CloseAll, String.Empty, false),
                delegate(StickyUiCommandResult result)
                {
                    _hostedCloseAllInFlight = false;
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.NotAccepted)
                        return;
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        _hostedExitRequested = false;
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-exit", result);
                        ShowBriefBubble("便利贴仍在收尾，退出已取消，请稍后重试。");
                        return;
                    }
                    if (result.FinalSnapshots != null)
                        foreach (StickyUiFinalSnapshot finalSnapshot in
                            result.FinalSnapshots)
                            ApplyHostedStickySnapshot(
                                finalSnapshot.Snapshot,
                                finalSnapshot.Sequence, false);
                    _hostedImeComposing.Clear();
                    _hostedInputFocused.Clear();
                    _hostedExitPrepared = true;
                    _stickyUiHost.BeginShutdown();
                    BeginExitSequence();
                });
        }

        private void ForgetHostedStickyState(string noteId)
        {
            _hostedAppliedSequences.Remove(noteId);
            _hostedImeComposing.Remove(noteId);
            _hostedInputFocused.Remove(noteId);
            _hostedDeletePending.Remove(noteId);
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
            if (PostHostedStickyShow(note, focusEditor)) return;
            if (allowHosted && TryStartHostedSticky(note, focusEditor)) return;
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
                RestoreStickyDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility);
                return;
            }
            StickyNoteWindow form = GetOrCreateStickyNoteWindow(note);
            if (focusEditor) form.ShowAndEdit();
            else form.ShowRestored();
            form.EnableWinFormsKeyboardInterop();
            if (!focusEditor && persistVisibility) _notes.Save();
            RefreshNoteTabs();
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
            bool hasVisibleNotes = _notes.GetAll().Exists(
                delegate(StickyNoteData note) { return note.Visible; });
            _leftNoteTabs.TopMost =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(hasVisibleNotes);
            _rightNoteTabs.TopMost =
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(hasVisibleNotes);
            if (!hasVisibleNotes)
            {
                if (_leftNoteTabs.Visible) _leftNoteTabs.BringToFront();
                if (_rightNoteTabs.Visible) _rightNoteTabs.BringToFront();
                return;
            }
            RaiseVisibleNotesAboveTabs();
        }

        private void RaiseVisibleNotesAboveTabs()
        {
            ApplicationDiagnostics.WriteWindowLayerEvent(
                "RaiseVisibleNotesAboveTabs", "structural");
            foreach (StickyNoteWindow form in
                new List<StickyNoteWindow>(_noteWindows.Values))
            {
                if (form == null || form.IsDisposed || !form.Visible) continue;
                form.RaiseForDockDragWithoutActivation();
            }
        }

        private void PositionNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null ||
                !IsHandleCreated || IsDisposed || _positioningNoteTabs) return;
            _positioningNoteTabs = true;
            try
            {
                Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
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
                manager.ShowDialog(this);
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
