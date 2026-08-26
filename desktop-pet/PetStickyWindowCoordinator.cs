using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Coordinates sticky-note window lifetime and placement from the pet.
    // Dock relationship algorithms remain in StickyDockController.
    internal sealed partial class PetForm
    {
        private void CreateStickyNote(string text)
        {
            StickyNoteData note = null;
            try
            {
                note = CreateStickyNoteData(text);
                if (note == null) return;
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
            StickyNoteForm form;
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

        private static Size StickyPhysicalSize(StickyNoteForm form,
            float scale)
        {
            return new Size(Math.Max(1, (int)Math.Round(form.Width * scale)),
                Math.Max(1, (int)Math.Round(form.Height * scale)));
        }

        private void PlaceNewStickyWindowOnPetScreen(StickyNoteData note)
        {
            StickyNoteForm form;
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
            List<List<StickyNoteForm>> componentForms =
                new List<List<StickyNoteForm>>();
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
                    List<StickyNoteData> component = BuildDockChainOrder(seed);
                    if (component.Count == 0) component.Add(seed);
                    foreach (StickyNoteData note in component)
                        if (note != null) visited.Add(note.Id);

                    List<StickyNoteData> activeNotes =
                        new List<StickyNoteData>();
                    List<StickyNoteForm> activeForms =
                        new List<StickyNoteForm>();
                    int componentWidth = 280;
                    int componentHeight = 0;
                    foreach (StickyNoteData note in component)
                    {
                        if (note == null || !note.Visible) continue;
                        StickyNoteForm form;
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
                    List<StickyNoteForm> forms = componentForms[componentIndex];
                    List<StickyNoteData> notes = components[componentIndex];
                    Rectangle root = roots[componentIndex];
                    List<Size> memberSizes = new List<Size>();
                    foreach (StickyNoteForm form in forms)
                        memberSizes.Add(StickyPhysicalSize(form, targetScale));
                    List<Rectangle> layout = CalculateUnifiedDockLayout(
                        memberSizes, root.Left, root.Top, root.Width,
                        targetScale);
                    for (int memberIndex = 0;
                        memberIndex < forms.Count; memberIndex++)
                    {
                        StickyNoteForm form = forms[memberIndex];
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
            List<Rectangle> result = new List<Rectangle>();
            int count = componentSizes == null ? 0 : componentSizes.Count;
            for (int index = 0; index < count; index++)
                result.Add(Rectangle.Empty);
            if (count == 0) return result;

            const int margin = 24;
            const int gap = 18;
            int scaledMargin = Math.Max(1, (int)Math.Round(margin * scale));
            int scaledGap = Math.Max(1, (int)Math.Round(gap * scale));
            int minimumWidth = Math.Max(1, (int)Math.Round(280 * scale));
            int maximumWidth = Math.Max(minimumWidth,
                (int)Math.Round(900 * scale));
            int minimumHeight = Math.Max(1, (int)Math.Round(220 * scale));
            int rowWidthLimit = Math.Max(minimumWidth,
                work.Width - scaledMargin * 2);
            List<List<int>> rows = new List<List<int>>();
            List<int> normal = new List<int>();
            List<int> oversized = new List<int>();
            for (int index = 0; index < count; index++)
            {
                Size size = componentSizes[index];
                // The height is the sum of every member in a docked group, so
                // a four-note stack is treated as one long component.
                bool isOversized = size.Width >= Math.Max(
                    (int)Math.Round(520 * scale),
                    work.Width * 45 / 100) || size.Height >= Math.Max(
                    (int)Math.Round(520 * scale),
                    work.Height * 50 / 100);
                if (isOversized) oversized.Add(index);
                else normal.Add(index);
            }

            List<int> row = new List<int>();
            int rowWidth = 0;
            foreach (int index in normal)
            {
                int width = Math.Max(minimumWidth, Math.Min(maximumWidth,
                    componentSizes[index].Width));
                int nextWidth = row.Count == 0 ? width :
                    rowWidth + scaledGap + width;
                if (row.Count > 0 && nextWidth > rowWidthLimit)
                {
                    rows.Add(row);
                    row = new List<int>();
                    rowWidth = 0;
                }
                row.Add(index);
                rowWidth = rowWidth == 0 ? width :
                    rowWidth + scaledGap + width;
            }
            if (row.Count > 0) rows.Add(row);
            // Wide/long single notes and whole docked stacks get their own
            // lower rows, horizontally centered below the ordinary notes.
            foreach (int index in oversized)
                rows.Add(new List<int>(new int[] { index }));

            List<int> rowHeights = new List<int>();
            int totalHeight = 0;
            foreach (List<int> recoveryRow in rows)
            {
                int height = minimumHeight;
                foreach (int index in recoveryRow)
                    height = Math.Max(height, Math.Min(componentSizes[index].Height,
                        Math.Max(minimumHeight, work.Height * 58 / 100)));
                rowHeights.Add(height);
                totalHeight += height;
            }
            totalHeight += Math.Max(0, rows.Count - 1) * scaledGap;
            int y = work.Top + Math.Max(scaledMargin,
                (work.Height - totalHeight) / 2);
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                List<int> recoveryRow = rows[rowIndex];
                int width = 0;
                foreach (int index in recoveryRow)
                {
                    if (width > 0) width += scaledGap;
                    width += Math.Max(minimumWidth, Math.Min(maximumWidth,
                        componentSizes[index].Width));
                }
                int x = work.Left + (work.Width - width) / 2;
                foreach (int index in recoveryRow)
                {
                    int itemWidth = Math.Max(minimumWidth,
                        Math.Min(maximumWidth,
                        componentSizes[index].Width));
                    result[index] = new Rectangle(x, y, itemWidth,
                        componentSizes[index].Height);
                    x += itemWidth + scaledGap;
                }
                y += rowHeights[rowIndex] + scaledGap;
            }
            return result;
        }

        internal static Point CalculateStickyRecoveryAnchor(Rectangle work,
            Rectangle pet, Size window, int componentIndex)
        {
            int preferredLeft = pet.Left - window.Width - 12;
            if (preferredLeft < work.Left) preferredLeft = pet.Right + 12;
            int targetLeft = Math.Max(work.Left,
                Math.Min(preferredLeft, work.Right - window.Width));
            int availableTop = Math.Max(1, work.Height - 36);
            int relativeTop = pet.Top - work.Top +
                Math.Max(0, componentIndex) * 34;
            relativeTop %= availableTop;
            if (relativeTop < 0) relativeTop += availableTop;
            int targetTop = Math.Max(work.Top,
                Math.Min(work.Top + relativeTop, work.Bottom - 32));
            return new Point(targetLeft, targetTop);
        }

        private void RollBackFailedStickyCreation(StickyNoteData note)
        {
            if (note == null) return;
            StickyNoteForm form;
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

        private StickyNoteForm GetOrCreateStickyNoteWindow(StickyNoteData note)
        {
            StickyNoteForm existing;
            if (_noteWindows.TryGetValue(note.Id, out existing) && !existing.IsDisposed)
                return existing;
            WpfApplicationHost.Ensure();
            StickyNoteRepository.RepairForDisplay(note, false);
            StickyNoteForm form;
            try { form = new StickyNoteForm(note); }
            catch (Exception firstError)
            {
                ApplicationDiagnostics.ReportNonFatal(
                    "sticky-window-legacy-first-open", firstError);
                // A WPF/native-window failure is not proof that user data is
                // damaged. Retry once without mutating the note; callers can
                // report the second failure while the original data stays safe.
                form = new StickyNoteForm(note);
            }
            form.NoteChanged += delegate { _notes.Save(); RefreshMenuText(); };
            form.NoteChanged += delegate { RefreshNoteTabs(); };
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
                // The pet and note windows share the WinForms UI thread.  Give
                // committed editor text a short uncontested window before the
                // next layered animation frame is rendered.
                _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(260);
                TriggerTypingAnimation();
            };
            form.ImeCompositionChanged += delegate(object sender,
                ImeCompositionEventArgs e)
            {
                _ownNoteImeComposing = e.Active;
                if (e.Active)
                {
                    _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(500);
                }
                else
                {
                    _ownNoteInputQuietUntilUtc = DateTime.UtcNow.AddMilliseconds(260);
                    _nextFrameUtc = _ownNoteInputQuietUntilUtc;
                }
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

        private void RecoverFailedLegacyStickyWindow(StickyNoteData note)
        {
            if (note == null) return;
            StickyNoteForm failed;
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
            if (note == null) return;
            List<StickyNoteData> storedDockOrder =
                BuildDockChainOrderIncludingHidden(note);
            bool anyHiddenDockMember = storedDockOrder.Exists(
                delegate(StickyNoteData member)
                {
                    return !member.Visible;
                });
            if (ShouldRestoreWholeDockComponent(storedDockOrder.Count,
                anyHiddenDockMember))
            {
                RestoreStickyDockComponent(storedDockOrder, note,
                    focusEditor, persistVisibility);
                return;
            }
            StickyNoteForm form = GetOrCreateStickyNoteWindow(note);
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
                    StickyNoteForm form;
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
                StickyNoteForm first;
                if (_noteWindows.TryGetValue(hidden[0].Id, out first) &&
                    first != null && !first.IsDisposed)
                    first.FocusPrimaryInputForTest();
            }
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private void RefreshNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            // Side tabs have their own persistent order.  Sorting them by the
            // note's modified time here used to undo every successful drag.
            List<StickyNoteData> hidden = _notes.GetHiddenInTabOrder();
            StringBuilder signatureBuilder = new StringBuilder();
            foreach (StickyNoteData note in hidden)
            {
                signatureBuilder.Append(note.Id).Append('|')
                    .Append(note.DisplayTitle).Append('|')
                    .Append(note.ColorArgb).Append('\n');
            }
            string signature = signatureBuilder.ToString();
            if (String.Equals(signature, _noteTabsSignature,
                StringComparison.Ordinal))
            {
                PositionNoteTabs();
                return;
            }
            _noteTabsSignature = signature;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(hidden.Count,
                Height, work);
            List<StickyNoteData> left = hidden.GetRange(0, leftCount);
            List<StickyNoteData> right = hidden.GetRange(leftCount,
                hidden.Count - leftCount);
            _leftNoteTabs.SetNotes(left, 0);
            _rightNoteTabs.SetNotes(right, leftCount);
            PositionNoteTabs();
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
