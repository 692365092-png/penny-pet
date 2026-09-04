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
                note = PrepareStickyNoteDraft(text,
                    new DockSize(320, 300), false, false);
                if (note == null) return;
                _notes.Save();
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
                note = PrepareStickyNoteDraft(String.Empty,
                    new DockSize(320, 300), true, false);
                if (note == null) return;
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
                note = PrepareStickyNoteDraft(String.Empty,
                    new DockSize(320, 360), false, true);
                if (note == null) return;
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
            WindowsDisplayMetrics metrics =
                WindowsDisplayResolver.ResolvePhysicalRect(
                    Bounds.Left, Bounds.Top, Bounds.Right, Bounds.Bottom);
            double scale = metrics != null ? metrics.Scale : 1.0;
            List<DockLayoutTarget> targets =
                PrepareStickyExpandAndTileTargets(_notes.GetAll(), work,
                    scale);
            if (targets.Count == 0)
            {
                ShowBubble("当前没有便利贴。");
                return;
            }

            // Expand-and-tile is an explicit user placement command, so each
            // note's durable preference is committed together with its v10
            // canonical geometry before anything is persisted.
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note = _notes.Find(target.NoteId);
                if (note != null) CommitExpandedPreferred(note);
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

        private void CommitExpandedPreferred(StickyNoteData note)
        {
            if (note == null) return;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot surface = topology != null
                ? topology.FindByRuntimeGdiName(
                    note.DisplayId ?? String.Empty)
                : null;
            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, note.PreferredDisplayTargetKey);
            if (CommitHostedStickyPreferred(note, key,
                note.LocalLogicalX, note.LocalLogicalY,
                note.LocalLogicalWidth, note.LocalLogicalHeight,
                PlacementReason.ExpandAndTile))
                _placementRuntime.MarkUserPlacementCommit(note.Id);
        }

        internal static List<DockLayoutTarget>
            PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,
                Rectangle work, double scale)
        {
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (notes == null) return targets;
            double safeScale = scale > 0.0 ? scale : 1.0;
            // Pack as an overlapping card fan so more notes fit on one screen:
            // every note is reset to its type default logical size and placed
            // with a small offset from the previous one.
            const int cascadeStep = 40;
            const int margin = 24;
            int index = 0;
            foreach (StickyNoteData note in notes)
            {
                if (note == null) continue;
                int logicalWidth = 320;
                int logicalHeight = note.IsSchedule ? 360 : 300;
                int width = Math.Max(1,
                    (int)Math.Round(logicalWidth * safeScale));
                int height = Math.Max(1,
                    (int)Math.Round(logicalHeight * safeScale));
                int maxX = Math.Max(work.Left + 1,
                    work.Right - width - 1);
                int maxY = Math.Max(work.Top + 1,
                    work.Bottom - height - 1);
                int x = Math.Max(work.Left + 1,
                    Math.Min(work.Left + margin + index * cascadeStep,
                        maxX));
                int y = Math.Max(work.Top + 1,
                    Math.Min(work.Top + margin + index * cascadeStep,
                        maxY));
                StickyDockGroups.ClearMembership(note);
                note.Visible = true;
                DockLayoutTarget target = new DockLayoutTarget(note.Id,
                    x, y, width, height, true,
                    note.AlwaysOnTop);
                // P1-C: keep the canonical DisplayId + LocalLogicalRect in
                // lockstep with the physical bounds before the geometry is
                // persisted or shown, so restore never re-reads stale data.
                ApplyDockCanonicalFromPhysical(note, target);
                targets.Add(target);
                index++;
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

        // One creation attempt = one topology snapshot + one in-memory draft
        // fully configured (type, v10 compatibility, v11 preferred, visible)
        // before the caller performs the single first save.
        private StickyNoteData PrepareStickyNoteDraft(string text,
            DockSize logicalSize, bool todo, bool schedule)
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

            // The whole attempt reads one topology snapshot, and Pet facts
            // are captured against that same snapshot (SPAWN-INV-05).
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            WindowFacts petFacts = CapturePetWindowFacts(topology);
            DisplaySurfaceSnapshot targetSurface = topology != null &&
                petFacts != null
                    ? topology.FindByRuntimeGdiName(petFacts.RuntimeGdiName)
                    : null;

            StickyNoteData note = _notes.CreateDraft(text, Point.Empty);
            if (note == null) return null;
            note.IsTodoList = todo;
            note.IsSchedule = schedule;
            if (todo) note.Title = "待办清单";
            if (schedule)
            {
                note.Title = "日程";
                note.FontSizeTwips = 320;
            }

            if (targetSurface != null)
            {
                StickyCanonicalPlacement placement =
                    StickySpawnPolicy.PlanCenteredSpawn(
                        targetSurface.RuntimeGdiName,
                        targetSurface.WorkArea, targetSurface.Bounds.Left,
                        targetSurface.Bounds.Top, petFacts.Scale,
                        Math.Max(1, logicalSize.Width),
                        Math.Max(1, logicalSize.Height));
                placement.ApplyTo(note);
                string preferredKey =
                    DisplayTopologyRules.SelectPreferredTargetKey(
                        targetSurface, null);
                CommitHostedStickyPreferred(note, preferredKey,
                    placement.LocalX, placement.LocalY,
                    placement.LocalWidth, placement.LocalHeight,
                    PlacementReason.Spawn);
                TraceSpawnPlacement(note, petFacts, targetSurface,
                    preferredKey);
            }
            else
            {
                // Fallback keeps the note visible but never fabricates a
                // durable preferred identity without real facts.
                ApplyLegacySpawnFallback(note, logicalSize);
            }
            note.Visible = true;
            return note;
        }

        private void ApplyLegacySpawnFallback(StickyNoteData note,
            DockSize logicalSize)
        {
            // Degraded fallback keeps the same product invariant as the
            // normal path: centered in Penny's current screen WorkArea, with
            // the logical default size. No durable preferred identity is
            // fabricated here.
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            PhysicalRect centered = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(work.Left, work.Top, work.Width,
                    work.Height),
                Math.Max(1, logicalSize.Width),
                Math.Max(1, logicalSize.Height));
            note.X = centered.Left;
            note.Y = centered.Top;
            note.Width = centered.Width;
            note.Height = centered.Height;
        }

        private static void TraceSpawnPlacement(StickyNoteData note,
            WindowFacts petFacts, DisplaySurfaceSnapshot surface,
            string preferredKey)
        {
            DisplayDiagnostics.Trace("PlacementResolved",
                "reason=Spawn note=" + note.Id +
                " topology=" + (petFacts == null
                    ? 0 : petFacts.TopologyGeneration) +
                " petGdi=" + (petFacts == null
                    ? "-" : petFacts.RuntimeGdiName) +
                " targetSurface=" + surface.RuntimeSurfaceId +
                " targetKey=" + (String.IsNullOrEmpty(preferredKey)
                    ? "-" : preferredKey) +
                " dpi=" + (petFacts == null ? 0 : petFacts.Dpi) +
                " work=(" + surface.WorkArea.Left + "," +
                surface.WorkArea.Top + "," + surface.WorkArea.Width + "," +
                surface.WorkArea.Height + ")" +
                " logical=(" + note.LocalLogicalX + "," +
                note.LocalLogicalY + "," + note.LocalLogicalWidth + "," +
                note.LocalLogicalHeight + ")" +
                " physical=(" + note.X + "," + note.Y + "," +
                note.Width + "," + note.Height + ")" +
                " preferredDurable=" +
                !String.IsNullOrEmpty(preferredKey));
        }

        private WindowFacts CapturePetWindowFacts(
            DisplayTopologySnapshot topology)
        {
            if (IsDisposed || Disposing || Handle == IntPtr.Zero) return null;
            try
            {
                long generation = topology == null ? 0 : topology.Generation;
                return WindowsWindowFactsReader.Capture(Handle, "pet",
                    generation, 0, topology);
            }
            catch
            {
                return null;
            }
        }

        private DisplayTopologySnapshot CurrentTopologySnapshot()
        {
            return _displayTopologyRuntime == null
                ? null : _displayTopologyRuntime.Current;
        }

        // DRT-7: after a semantic topology change, every live standalone
        // hosted Sticky is reconciled against its durable preferred target.
        // Dock groups are deliberately excluded until group rehome lands.
        private void HandleStickyTopologyChanged(
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || IsDisposed || Disposing) return;
            WindowFacts petFacts = CapturePetWindowFacts(snapshot);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note == null || !note.Visible || !IsHostedSticky(note))
                    continue;
                if (!String.IsNullOrEmpty(note.DockGroupId)) continue;
                ReconcileStandaloneSticky(note, snapshot, petFacts);
            }
        }

        private void ReconcileStandaloneSticky(StickyNoteData note,
            DisplayTopologySnapshot snapshot, WindowFacts petFacts)
        {
            bool hasPreferred =
                !String.IsNullOrWhiteSpace(note.PreferredDisplayTargetKey) &&
                note.PreferredLocalLogicalWidth > 0 &&
                note.PreferredLocalLogicalHeight > 0;
            DisplaySurfaceSnapshot preferredSurface = hasPreferred
                ? snapshot.FindByTargetKey(note.PreferredDisplayTargetKey)
                : null;
            if (preferredSurface != null)
            {
                // Preferred display is active again. Only a note the user did
                // not manually move away gets pulled back to its preference.
                if (_placementRuntime.IsTemporaryRehome(note.Id) &&
                    !_placementRuntime.UserMovedSinceRehome(note.Id))
                {
                    string noteId = note.Id;
                    StickyUiReprojectTarget returnTarget =
                        new StickyUiReprojectTarget(
                            preferredSurface.RuntimeGdiName,
                            note.PreferredLocalLogicalX,
                            note.PreferredLocalLogicalY,
                            note.PreferredLocalLogicalWidth,
                            note.PreferredLocalLogicalHeight,
                            false, true);
                    PostHostedStickyCommand(StickyUiCommand.Reproject(
                        noteId, returnTarget, snapshot),
                        delegate(StickyUiCommandResult result)
                        {
                            if (result != null && result.Status ==
                                StickyUiCommandStatus.Handled)
                            {
                                ApplyReprojectResult(result, noteId);
                                _placementRuntime.
                                    MarkReturnedToPreferred(noteId);
                                DisplayDiagnostics.Trace(
                                    "PreferredReturned",
                                    "note=" + noteId);
                            }
                        });
                }
                return;
            }

            if (_placementRuntime.IsTemporaryRehome(note.Id)) return;
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            if (!TryBuildTemporaryRehomeTarget(note, snapshot, petFacts,
                true, out fallback, out rehomeTarget)) return;
            string rehomedNoteId = note.Id;
            PostHostedStickyCommand(StickyUiCommand.Reproject(rehomedNoteId,
                rehomeTarget, snapshot),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled)
                    {
                        ApplyReprojectResult(result, rehomedNoteId);
                        CompleteTemporaryRehome(rehomedNoteId, fallback,
                            "preferred-display-missing", snapshot);
                    }
                });
        }

        // Builds the typed temporary-rehome intent without guessing the
        // target DPI: the Sticky STA projects the preferred logical size with
        // GetDpiForWindow after bootstrapping the HWND onto the fallback
        // surface. The durable preferred fields are never modified here.
        private static bool TryBuildTemporaryRehomeTarget(StickyNoteData note,
            DisplayTopologySnapshot topology, WindowFacts petFacts,
            bool showAfter, out DisplaySurfaceSnapshot fallback,
            out StickyUiReprojectTarget target)
        {
            fallback = null;
            target = null;
            if (note == null || topology == null ||
                !String.IsNullOrEmpty(note.DockGroupId) ||
                String.IsNullOrWhiteSpace(
                    note.PreferredDisplayTargetKey) ||
                note.PreferredLocalLogicalWidth <= 0 ||
                note.PreferredLocalLogicalHeight <= 0 ||
                topology.FindByTargetKey(
                    note.PreferredDisplayTargetKey) != null) return false;
            fallback = FallbackDisplayPolicy.ResolveFallbackSurface(topology,
                note.PreferredDisplayTargetKey,
                new PhysicalRect(note.X, note.Y, note.Width, note.Height),
                petFacts == null ? String.Empty : petFacts.RuntimeGdiName);
            if (fallback == null) return false;
            target = new StickyUiReprojectTarget(
                fallback.RuntimeGdiName, 0, 0,
                note.PreferredLocalLogicalWidth,
                note.PreferredLocalLogicalHeight,
                true, showAfter);
            return true;
        }

        private void CompleteTemporaryRehome(string noteId,
            DisplaySurfaceSnapshot fallback, string reason,
            DisplayTopologySnapshot observedTopology)
        {
            _placementRuntime.MarkTemporaryRehome(noteId, reason);
            DisplayDiagnostics.Trace("TemporaryRehome",
                "note=" + noteId +
                " target=" + fallback.RuntimeSurfaceId +
                " work=(" + fallback.WorkArea.Left + "," +
                fallback.WorkArea.Top + "," + fallback.WorkArea.Width + "," +
                fallback.WorkArea.Height + ")");
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            if (current == null || observedTopology == null ||
                current.Generation == observedTopology.Generation) return;
            // The command completed against an older immutable snapshot.
            // Clear that temporary decision and immediately reconcile against
            // the newest topology so an async hotplug race cannot strand it.
            _placementRuntime.ClearTemporaryRehome(noteId);
            StickyNoteData note = _notes.Find(noteId);
            if (note != null && note.Visible &&
                String.IsNullOrEmpty(note.DockGroupId))
                ReconcileStandaloneSticky(note, current,
                    CapturePetWindowFacts(current));
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
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            bool temporaryRehome = TryBuildTemporaryRehomeTarget(note,
                topology, CapturePetWindowFacts(topology), false,
                out fallback, out rehomeTarget);
            StickyNoteUiSnapshot createSnapshot =
                StickyNoteUiSnapshot.FromData(note);
            StickyUiCommand command = StickyUiCommand.Create(
                createSnapshot, focusEditor, _reminders.GetItems(), topology,
                rehomeTarget);
            PostHostedStickyCommand(command,
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (temporaryRehome)
                            ApplyReprojectResult(result, noteId);
                        else
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence);
                        if (temporaryRehome)
                            CompleteTemporaryRehome(noteId, fallback,
                                "preferred-display-missing-at-restore",
                                topology);
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
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            if (TryBuildTemporaryRehomeTarget(note, topology,
                CapturePetWindowFacts(topology), true,
                out fallback, out rehomeTarget))
            {
                PostHostedStickyCommand(StickyUiCommand.Reproject(noteId,
                    rehomeTarget, topology),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled)
                        {
                            ApplyReprojectResult(result, noteId);
                            CompleteTemporaryRehome(noteId, fallback,
                                "preferred-display-missing-at-reopen",
                                topology);
                            if (focusEditor)
                                PostHostedStickyCommand(
                                    StickyUiCommand.FocusPrimaryInput(noteId),
                                    delegate(StickyUiCommandResult ignored) { });
                            return;
                        }
                        HandleHostedStickyFailure(new string[] { noteId },
                            "sticky-hosted-show", result);
                    });
                return true;
            }
            PostHostedStickyCommand(StickyUiCommand.Show(noteId,
                focusEditor, topology),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence);
                        if (_placementRuntime.IsTemporaryRehome(noteId) &&
                            topology != null && topology.FindByTargetKey(
                                note.PreferredDisplayTargetKey) != null)
                            _placementRuntime.MarkReturnedToPreferred(noteId);
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

        private void HostedStickyFaulted(Exception error)
        {
            if (error != null)
                ApplicationDiagnostics.ReportNonFatal(
                    "hosted-sticky-faulted", error);
            // Hosted Sticky windows are degraded, but canonical note data stays
            // untouched and Penny itself can still exit safely.
            if (_exiting || IsDisposed || Disposing) return;
            ShowBubble(
                "便利贴界面遇到问题，已停止使用，数据仍然保留。请重启 Penny 后再试。");
        }

        private void HostedStickyEventReceived(StickyUiEvent value)
        {
            if (value == null || IsDisposed || Disposing ||
                !_hostedRuntime.ContainsNote(value.NoteId)) return;
            TraceHostedWindowFacts(value);
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
                if (!ApplyHostedStickyEvent(value, false)) return;
                StickyNoteData canonical = _notes.Find(value.NoteId);
                if (canonical == null) return;
                DockWindowFacts facts = DockWindowFacts.FromData(canonical);
                if (value.Kind == StickyUiEventKind.HeaderDragStarted)
                    BeginStickyDockDrag(facts);
                else if (value.Kind == StickyUiEventKind.HeaderDragMoved)
                {
                    MoveStickyDockDrag(facts);
                    ApplyNoteTabZOrder();
                }
                else
                {
                    CompleteStickyDockDrag(facts, value);
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.BoundsChanged)
            {
                ApplyHostedStickyEvent(value, false);
                ApplyNoteTabZOrder();
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
                if (!ApplyHostedStickyEvent(value)) return;
                AdoptPreferredIfEmpty(canonical, value.Facts,
                    value.Topology);
                if (topMostChanged)
                {
                    ApplyDockComponentTopMost(canonical,
                        value.Snapshot.AlwaysOnTop, value.NoteId);
                    _notes.SaveAsync();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.UserResizeCompleted)
            {
                if (!ApplyHostedStickyEvent(value, false)) return;
                StickyNoteData canonical = _notes.Find(value.NoteId);
                string targetKey;
                LogicalRect local;
                if (canonical != null && TryBuildPreference(value.Facts,
                    value.Topology, canonical.PreferredDisplayTargetKey,
                    out targetKey, out local) &&
                    CommitHostedStickyPreferred(canonical, targetKey,
                        local.X, local.Y, local.Width, local.Height,
                        PlacementReason.UserResizeCommit))
                {
                    _placementRuntime.MarkUserPlacementCommit(
                        value.NoteId);
                    _notes.SaveAsync();
                }
                return;
            }
            if (value.Kind == StickyUiEventKind.Closed)
            {
                ClearHostedDockResizeSession();
                ApplyHostedStickySnapshot(value.Snapshot, value.Sequence);
                _hostedRuntime.RemoveNote(value.NoteId);
                _placementRuntime.Remove(value.NoteId);
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

        private void TraceHostedWindowFacts(StickyUiEvent value)
        {
            if (value.Facts == null) return;
            WindowFacts facts = value.Facts;
            DisplayDiagnostics.Trace("WindowFacts",
                "note=" + facts.WindowId + " topology=" +
                facts.TopologyGeneration + " target=" +
                facts.ActiveTargetKey + " seq=" + facts.WindowSequence +
                " dpi=" + facts.Dpi + " gdi=" + facts.RuntimeGdiName +
                " physical=(" + facts.PhysicalBounds.Left + "," +
                facts.PhysicalBounds.Top + "," +
                facts.PhysicalBounds.Width + "," +
                facts.PhysicalBounds.Height + ")");
        }

        // Geometry-bearing production events treat WindowFacts as the only
        // geometry truth. Content flows through ApplyContentTo, Visible and
        // AlwaysOnTop are applied explicitly, and the v10 compatibility
        // geometry is derived from the facts plus the capture-time topology
        // surface - never from the snapshot's WPF-derived geometry.
        private bool ApplyHostedStickyEvent(StickyUiEvent value,
            bool persist = true)
        {
            if (value == null || value.Snapshot == null ||
                !_hostedRuntime.CanApplySequence(value.NoteId,
                    value.Sequence)) return false;
            StickyNoteData canonical = _notes.Find(value.NoteId);
            if (canonical == null) return false;
            bool visibilityChanged = canonical.Visible !=
                value.Snapshot.Visible;
            string oldHiddenTitle = canonical.Visible
                ? String.Empty : canonical.DisplayTitle;
            value.Snapshot.ApplyContentTo(canonical);
            canonical.Visible = value.Snapshot.Visible;
            canonical.AlwaysOnTop = value.Snapshot.AlwaysOnTop;
            ApplyHostedStickyFactsGeometry(canonical, value.Facts,
                value.Topology);
            _placementRuntime.UpdateEffective(value.NoteId, value.Facts);
            _hostedRuntime.RecordSequence(value.NoteId, value.Sequence);
            if (persist) _notes.SaveAsync();
            RefreshMenuText();
            if (visibilityChanged || (!canonical.Visible &&
                !String.Equals(oldHiddenTitle, canonical.DisplayTitle,
                    StringComparison.Ordinal))) RefreshNoteTabs();
            return true;
        }

        private static void ApplyHostedStickyFactsGeometry(
            StickyNoteData canonical, WindowFacts facts,
            DisplayTopologySnapshot topology)
        {
            if (canonical == null || facts == null || topology == null)
                return;
            DisplaySurfaceSnapshot surface =
                topology.FindByTargetKey(facts.ActiveTargetKey);
            if (surface == null)
                surface = topology.FindByRuntimeGdiName(
                    facts.RuntimeGdiName);
            if (surface == null) return;
            StickyCanonicalPlacement placement =
                StickyPlacementMath.FromPhysicalRect(
                    surface.RuntimeGdiName, surface.Bounds.Left,
                    surface.Bounds.Top, facts.Scale,
                    facts.PhysicalBounds.Left, facts.PhysicalBounds.Top,
                    facts.PhysicalBounds.Width,
                    facts.PhysicalBounds.Height);
            placement.ApplyTo(canonical);
        }

        // A successful Reproject carries actual HWND facts; the repository is
        // updated from those facts, never from the WPF-derived snapshot
        // geometry, and the runtime Effective advances to the same facts.
        private void ApplyReprojectResult(StickyUiCommandResult result,
            string noteId)
        {
            if (result == null || result.Snapshot == null ||
                !_hostedRuntime.CanApplySequence(noteId,
                    result.Sequence)) return;
            StickyNoteData canonical = _notes.Find(noteId);
            if (canonical == null) return;
            result.Snapshot.ApplyContentTo(canonical);
            canonical.Visible = result.Snapshot.Visible;
            canonical.AlwaysOnTop = result.Snapshot.AlwaysOnTop;
            ApplyHostedStickyFactsGeometry(canonical, result.Facts,
                result.Topology);
            _placementRuntime.UpdateEffective(noteId, result.Facts);
            _hostedRuntime.RecordSequence(noteId, result.Sequence);
            _notes.SaveAsync();
            RefreshMenuText();
        }

        private static bool TryBuildPreference(WindowFacts facts,
            DisplayTopologySnapshot topology, string existingKey,
            out string targetKey, out LogicalRect localRect)
        {
            targetKey = null;
            localRect = new LogicalRect();
            WindowPlacementPreference preference;
            if (!StickyPlacementRules.TryBuildPreferredPlacement(facts,
                topology, existingKey, out preference)) return false;
            targetKey = preference.PreferredTargetKey;
            localRect = preference.LocalLogicalRect;
            return true;
        }

        private bool CommitHostedStickyPreferred(StickyNoteData canonical,
            string targetKey, int localX, int localY, int localWidth,
            int localHeight, PlacementReason reason)
        {
            if (canonical == null ||
                !StickyPlacementRules.CanCommitPreferred(reason)) return false;
            if (String.IsNullOrWhiteSpace(targetKey) ||
                localWidth <= 0 || localHeight <= 0) return false;
            canonical.PreferredDisplayTargetKey = targetKey;
            canonical.PreferredLocalLogicalX = localX;
            canonical.PreferredLocalLogicalY = localY;
            canonical.PreferredLocalLogicalWidth = localWidth;
            canonical.PreferredLocalLogicalHeight = localHeight;
            return true;
        }

        // Fills a missing preference only. v10 migration is attempted first
        // (preserving the persisted display-local intent), then the actual
        // shown WindowFacts when the saved display is not resolvable. An
        // existing preference is never overwritten here.
        private void AdoptPreferredIfEmpty(StickyNoteData canonical,
            WindowFacts facts, DisplayTopologySnapshot topology)
        {
            if (canonical == null ||
                !String.IsNullOrWhiteSpace(
                    canonical.PreferredDisplayTargetKey)) return;
            if (StickyPlacementRules.MigrateV10Preferred(canonical,
                topology))
            {
                _notes.SaveAsync();
                return;
            }
            string targetKey;
            LogicalRect local;
            if (TryBuildPreference(facts, topology, String.Empty,
                out targetKey, out local) &&
                !String.IsNullOrWhiteSpace(targetKey) &&
                local.Width > 0 && local.Height > 0)
            {
                canonical.PreferredDisplayTargetKey = targetKey;
                canonical.PreferredLocalLogicalX = local.X;
                canonical.PreferredLocalLogicalY = local.Y;
                canonical.PreferredLocalLogicalWidth = local.Width;
                canonical.PreferredLocalLogicalHeight = local.Height;
                _notes.SaveAsync();
            }
        }

        // DRT-9/10 durable dock commit continuation: after mouse-up the
        // capture ran on the Sticky STA; every member's preferred placement
        // is derived from the captured actual facts plus the event's
        // capture-time topology, then membership and content are persisted
        // once. No synchronous wait and no Current-generation guessing.
        private void CompleteDockDurableCommit(StickyUiCommandResult result,
            StickyUiEvent value, StickyNoteData seed,
            StickyNoteData remainderSeed)
        {
            if (result != null &&
                result.Status == StickyUiCommandStatus.Handled)
                ApplyDockCommitFacts(result.DockBatchResult, value);
            CommitVisibleDockOrder(seed);
            if (remainderSeed != null)
                CommitVisibleDockOrder(remainderSeed);
            _notes.Save();
        }

        private void ApplyDockCommitFacts(DockBatchResult batch,
            StickyUiEvent value)
        {
            if (batch == null || value == null || value.Topology == null)
                return;
            if (batch.TopologyGeneration != value.Topology.Generation)
                return;
            foreach (DockBatchMemberResult member in batch.Members)
            {
                if (member == null || member.Snapshot == null) continue;
                if (!_hostedRuntime.CanApplySequence(member.NoteId,
                    member.WindowSequence)) continue;
                StickyNoteData canonical = _notes.Find(member.NoteId);
                if (canonical == null) continue;
                member.Snapshot.ApplyContentTo(canonical);
                canonical.Visible = member.Snapshot.Visible;
                canonical.AlwaysOnTop = member.Snapshot.AlwaysOnTop;
                ApplyHostedStickyFactsGeometry(canonical, member.Facts,
                    value.Topology);
                _placementRuntime.UpdateEffective(member.NoteId,
                    member.Facts);
                _hostedRuntime.RecordSequence(member.NoteId,
                    member.WindowSequence);
                string targetKey;
                LogicalRect local;
                if (TryBuildPreference(member.Facts, value.Topology,
                    canonical.PreferredDisplayTargetKey, out targetKey,
                    out local) &&
                    CommitHostedStickyPreferred(canonical, targetKey,
                        local.X, local.Y, local.Width, local.Height,
                        PlacementReason.DockCommit))
                    _placementRuntime.MarkUserPlacementCommit(
                        member.NoteId);
            }
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
                        member), false, _reminders.GetItems(),
                        CurrentTopologySnapshot())
                    : StickyUiCommand.Show(member.Id, false,
                        CurrentTopologySnapshot());
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
                                        StringComparison.OrdinalIgnoreCase),
                                    CurrentTopologySnapshot()),
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
            int leftCount = StickyNoteTabsForm.CalculateLeftCount(
                hidden.Count);
            List<SideTabSnapshot> left = hidden.GetRange(0, leftCount);
            List<SideTabSnapshot> right = hidden.GetRange(leftCount,
                hidden.Count - leftCount);
            _leftNoteTabs.SetNotes(left, 0);
            _rightNoteTabs.SetNotes(right, leftCount);
            PositionNoteTabs();
            ApplyNoteTabZOrder();
        }

        private bool IsStripCoveredByVisibleSticky(StickyNoteTabsForm tabs)
        {
            if (tabs == null || tabs.IsDisposed || !tabs.Visible) return false;
            Rectangle stripBounds = tabs.Bounds;
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note == null || !note.Visible) continue;
                if (note.Width <= 0 || note.Height <= 0) continue;
                Rectangle noteBounds = new Rectangle(note.X, note.Y,
                    note.Width, note.Height);
                if (stripBounds.IntersectsWith(noteBounds)) return true;
            }
            return false;
        }

        private void ApplyNoteTabZOrder()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null || IsDisposed)
                return;
            bool leftCovered = IsStripCoveredByVisibleSticky(_leftNoteTabs);
            bool rightCovered = IsStripCoveredByVisibleSticky(_rightNoteTabs);
            if (!_leftTabsCovered.HasValue || _leftTabsCovered.Value != leftCovered)
            {
                _leftTabsCovered = leftCovered;
                _leftNoteTabs.TopMost =
                    StickyNoteWindowRules.ShouldKeepSideTabsTopMost(leftCovered);
                if (!leftCovered && _leftNoteTabs.Visible)
                    _leftNoteTabs.BringToFront();
                ApplicationDiagnostics.WriteWindowLayerEvent("SideTabsLeft",
                    leftCovered ? "covered" : "clear");
            }
            if (!_rightTabsCovered.HasValue ||
                _rightTabsCovered.Value != rightCovered)
            {
                _rightTabsCovered = rightCovered;
                _rightNoteTabs.TopMost =
                    StickyNoteWindowRules.ShouldKeepSideTabsTopMost(rightCovered);
                if (!rightCovered && _rightNoteTabs.Visible)
                    _rightNoteTabs.BringToFront();
                ApplicationDiagnostics.WriteWindowLayerEvent("SideTabsRight",
                    rightCovered ? "covered" : "clear");
            }
        }

        private void PositionNoteTabs()
        {
            if (_leftNoteTabs == null || _rightNoteTabs == null ||
                !IsHandleCreated || IsDisposed || _positioningNoteTabs) return;
            Rectangle work = Screen.FromRectangle(Bounds).WorkingArea;
            if (!StickyNoteTabsForm.IsLayoutSplitCurrent(
                _leftNoteTabs.Controls.Count, _rightNoteTabs.Controls.Count))
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
                ApplyNoteTabZOrder();
            }
            finally
            {
                _positioningNoteTabs = false;
            }
        }

        private void ShowStickyNotesManager()
        {
            bool createRequested = false;
            bool fullRestoreRequested = false;
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
                fullRestoreRequested = manager.FullRestoreRequested;
            }
            if (fullRestoreRequested)
                RestoreStickyNotesBackup();
            else if (createRequested)
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
