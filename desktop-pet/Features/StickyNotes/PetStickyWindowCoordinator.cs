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
        private readonly DockRestoreOperations _dockRestores = new DockRestoreOperations();

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
            if (DeferDockResizeMutation(null, ExpandAndTileAllStickyNotesToPetScreen)) return;
            CancelHostedDockRestores();
            ClearHostedDockResizeSession();
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

            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            DisplaySurfaceSnapshot surface = topology == null || metrics == null
                ? null : topology.FindByRuntimeGdiName(metrics.DisplayId);
            // Commit the selected target directly, never a serialization mirror.
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note = _notes.Find(target.NoteId);
                if (note != null) CommitExpandedPreferred(note, target, surface, scale);
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

        private void CommitExpandedPreferred(StickyNoteData note,
            DockLayoutTarget target, DisplaySurfaceSnapshot surface, double scale)
        {
            if (note == null || surface == null) return;
            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, note.PreferredDisplayTargetKey);
            WindowPlacementPreference preference = StickyPlacementMath.PreferenceFromPhysicalRect(
                key, surface.Bounds.Left, surface.Bounds.Top, scale,
                new PhysicalRect(target.X, target.Y, target.Width, target.Height));
            LogicalRect local = preference.LocalLogicalRect;
            if (CommitHostedStickyPreferred(note, key, local.X, local.Y,
                local.Width, local.Height, PlacementReason.ExpandAndTile))
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
            if (IsDisposed || Disposing || !IsHandleCreated ||
                Handle == IntPtr.Zero)
                return null;

            try
            {
                long generation = topology == null ? 0 : topology.Generation;
                long sequence = ++_petWindowSequence;

                WindowFacts facts = WindowsWindowFactsReader.Capture(
                    Handle, PetWindowFactsId,
                    generation, sequence, topology);

                if (facts != null && topology != null &&
                    facts.TopologyGeneration == topology.Generation)
                    _petEffectiveFacts = facts;

                return facts;
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

        private bool IsTopologyCurrent(DisplayTopologySnapshot topology)
        {
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            return topology != null && current != null &&
                topology.Generation == current.Generation;
        }

        private WindowFactsVersionDisposition ClassifyHostedGeometry(
            StickyUiEvent value)
        {
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            return value == null ? WindowFactsVersionDisposition.Invalid :
                WindowFactsVersionRules.Classify(value.NoteId, value.Sequence,
                    value.Facts, value.Topology,
                    current == null ? -1 : current.Generation);
        }

        private bool IsCurrentHostedGeometryEvent(StickyUiEvent value)
        {
            return ClassifyHostedGeometry(value) ==
                WindowFactsVersionDisposition.Current;
        }

        // DRT-7/11: publish the new generation as a hard Dock barrier, resume
        // an active drag from freshly captured source facts, then reconcile
        // standalone windows and whole persisted Dock groups independently.
        private void HandleStickyTopologyChanged(
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || IsDisposed || Disposing) return;
            InvalidateDockPlansForTopologyChange(snapshot);
            RestartHostedDockRestores(snapshot);
            ResumeDockDragAfterTopologyChange(snapshot);
            WindowFacts petFacts = CapturePetWindowFacts(snapshot);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note == null || !note.Visible || !IsHostedSticky(note))
                    continue;
                if (!String.IsNullOrEmpty(note.DockGroupId)) continue;
                ReconcileStandaloneSticky(note, snapshot, petFacts);
            }
            ReconcileDockGroups(snapshot, petFacts);
        }

        private void InvalidateDockPlansForTopologyChange(
            DisplayTopologySnapshot snapshot)
        {
            _dockPlanMailbox.Clear();
            ClearHostedDockResizeSession();
            if (_dockInteraction.IsActive)
            {
                long epoch = _dockInteraction.IsFinalizing
                    ? _dockInteraction.RestartFinalizing(snapshot.Generation)
                    : _dockInteraction.BeginRebase(snapshot.Generation);
                if (epoch > 0) _stickyUiHost.SetCurrentDockInteractionEpoch(epoch);
            }
            DisplayDiagnostics.Trace("DockPlanStale",
                "topology invalidated generation=" + snapshot.Generation);
        }

        private void ResumeDockDragAfterTopologyChange(
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || String.IsNullOrEmpty(_dockInteraction.SourceNoteId) ||
                _dockInteraction.MemberIds.Count == 0 || !_dockInteraction.IsActive)
                return;
            string sourceId = _dockInteraction.SourceNoteId;
            if (_dockInteraction.IsFinalizing)
            {
                StartDockFinalization(_notes.Find(sourceId),
                    _notes.Find(_dockInteraction.RemainderNoteId));
                return;
            }
            long epoch = _dockInteraction.Epoch;
            if (!_dockInteraction.Matches(epoch, snapshot.Generation,
                DockInteractionPhase.Rebasing)) return;
            string[] expectedIds = _dockInteraction.CopyMemberIds();
            PostHostedStickyCommand(StickyUiCommand.CaptureDockFacts(
                expectedIds, snapshot, epoch), delegate(StickyUiCommandResult result)
                {
                    if (!_dockInteraction.Matches(epoch, snapshot.Generation,
                        DockInteractionPhase.Rebasing) ||
                        !IsTopologyCurrent(snapshot))
                        return;
                    WindowFacts sourceFacts;
                    if (!TryApplyDockFactsBarrier(result, expectedIds, snapshot,
                        epoch, sourceId, true, out sourceFacts))
                    {
                        DisplayDiagnostics.Trace("DockFactsBarrierRejected",
                            "phase=Rebasing epoch=" + epoch + " generation=" +
                            snapshot.Generation + " source=" + sourceId);
                        return;
                    }
                    DockWindowFacts sourceRuntime;
                    if (!_dockInteraction.PreviewFacts.TryGetValue(sourceId,
                        out sourceRuntime) || sourceRuntime == null) return;
                    // Rebase cancels this split hold; a fresh mouse-down is
                    // required. Preserve the original gesture provenance.
                    ClearSplitGuide();
                    _dockInteraction.RecordMove(sourceRuntime);
                    if (!_dockInteraction.TryEnterDragging(epoch,
                        snapshot.Generation)) return;
                    StickyNoteData seed = _notes.Find(sourceId);
                    DockPlacementPlan plan = PlanDockPlan(seed, sourceFacts,
                        snapshot, epoch);
                    if (plan != null && plan.WindowTargets.Count > 1)
                    {
                        ApplyLiveDockPlan(plan);
                        RememberActiveDockFacts(PlanToDockTargets(plan));
                    }
                });
        }

        private void ReconcileDockGroups(DisplayTopologySnapshot snapshot,
            WindowFacts petFacts)
        {
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (note == null || !note.Visible || !IsHostedSticky(note) ||
                    String.IsNullOrEmpty(note.DockGroupId) ||
                    !visited.Add(note.DockGroupId) ||
                    _pendingDockTopologyGroups.Contains(note.DockGroupId) ||
                    _dockRestores.ContainsGroup(note.DockGroupId))
                    continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                group.RemoveAll(delegate(StickyNoteData member)
                {
                    return member == null || !member.Visible ||
                        !IsHostedSticky(member);
                });
                if (group.Count < 2) continue;
                if (!String.IsNullOrEmpty(_dockInteraction.SourceNoteId) &&
                    group.Exists(delegate(StickyNoteData member)
                    {
                        return String.Equals(member.Id, _dockInteraction.SourceNoteId,
                            StringComparison.OrdinalIgnoreCase);
                    })) continue;
                ReconcileDockGroup(group, snapshot, petFacts);
            }
        }

        private void ReconcileDockGroup(List<StickyNoteData> group,
            DisplayTopologySnapshot snapshot, WindowFacts petFacts)
        {
            if (group == null || group.Count < 2 || snapshot == null) return;
            DisplaySurfaceSnapshot preferred =
                DockRestoreOperation.FindCommonPreferredSurface(group, snapshot);
            bool temporary = group.Exists(delegate(StickyNoteData member)
            {
                return _placementRuntime.IsTemporaryRehome(member.Id);
            });
            if (preferred != null)
            {
                if (temporary)
                {
                    if (group.Exists(delegate(StickyNoteData member)
                    {
                        return _placementRuntime.UserMovedSinceRehome(member.Id);
                    })) return;
                    PostDockGroupTopologyReproject(group, snapshot, preferred,
                        DockTopologyReprojectReason.PreferredReturn);
                    return;
                }
                PostDockGroupTopologyReproject(group, snapshot, preferred,
                    DockTopologyReprojectReason.CurrentRuntimeRepair);
                return;
            }
            if (temporary) return;

            StickyNoteData root = group[0];
            DisplaySurfaceSnapshot fallback =
                FallbackDisplayPolicy.ResolveFallbackSurface(snapshot,
                    root.PreferredDisplayTargetKey,
                    _placementRuntime.GetEffective(root.Id) == null
                        ? new PhysicalRect() : _placementRuntime.GetEffective(root.Id).PhysicalBounds,
                    petFacts == null ? String.Empty :
                        petFacts.RuntimeGdiName);
            if (fallback != null)
                PostDockGroupTopologyReproject(group, snapshot, fallback,
                    DockTopologyReprojectReason.TemporaryRehome);
        }

        internal static bool TryBuildDockTopologyLogicalState(
            IList<StickyNoteData> group, DockTopologyReprojectReason reason,
            out DockGroupLogicalState state, StickyPlacementRuntime runtime = null)
        {
            state = null;
            if (group == null || group.Count < 2) return false;
            bool usePreferred = reason != DockTopologyReprojectReason.CurrentRuntimeRepair;
            List<DockLogicalMember> members = new List<DockLogicalMember>(group.Count);
            LogicalPoint anchor = new LogicalPoint();
            int unifiedWidth = 0;
            foreach (StickyNoteData member in group)
            {
                if (member == null || String.IsNullOrWhiteSpace(member.Id)) return false;
                LogicalRect local;
                if (usePreferred)
                    local = new LogicalRect {
                        X = member.PreferredLocalLogicalX, Y = member.PreferredLocalLogicalY,
                        Width = member.PreferredLocalLogicalWidth, Height = member.PreferredLocalLogicalHeight };
                else if (runtime == null || !runtime.TryGetEffectiveLogical(member.Id, out local)) return false;
                if (local.Width <= 0 || local.Height <= 0) return false;
                if (members.Count == 0) { anchor = new LogicalPoint { X = local.X, Y = local.Y }; unifiedWidth = local.Width; }
                members.Add(new DockLogicalMember(member.Id, unifiedWidth, local.Height));
            }
            try { state = new DockGroupLogicalState(anchor, members); return true; }
            catch (ArgumentException) { return false; }
        }

        private void PostDockGroupTopologyReproject(
            List<StickyNoteData> group, DisplayTopologySnapshot snapshot,
            DisplaySurfaceSnapshot targetSurface, DockTopologyReprojectReason reason)
        {
            if (group == null || group.Count < 2 || snapshot == null ||
                targetSurface == null) return;
            StickyNoteData root = group[0];
            if (root == null || String.IsNullOrWhiteSpace(root.DockGroupId)) return;
            string groupId = root.DockGroupId;
            if (_dockRestores.ContainsGroup(groupId)) return;
            DockGroupLogicalState logicalState;
            if (!TryBuildDockTopologyLogicalState(group, reason, out logicalState,
                _placementRuntime))
            {
                DisplayDiagnostics.Trace("DockTopologyGeometryRejected",
                    "group=" + groupId + " generation=" + snapshot.Generation +
                    " reason=" + reason);
                return;
            }
            bool centerInWorkArea = reason == DockTopologyReprojectReason.TemporaryRehome;
            DockGroupReprojectPlan plan = new DockGroupReprojectPlan(
                snapshot.Generation, _dockPlanMailbox.NextSequence(),
                targetSurface.RuntimeSurfaceId, logicalState, centerInWorkArea);
            List<string> expectedIds = new List<string>();
            foreach (DockLogicalMember member in logicalState.Members)
                expectedIds.Add(member.NoteId);
            if (!_pendingDockTopologyGroups.Add(groupId)) return;
            DisplayDiagnostics.Trace("DockTopologyReprojectPlan",
                "group=" + groupId + " generation=" + snapshot.Generation +
                " reason=" + reason + " target=" + targetSurface.RuntimeSurfaceId +
                " root=(" + logicalState.RootAnchor.X + "," + logicalState.RootAnchor.Y +
                ") members=" + logicalState.Members.Count);
            PostHostedStickyCommand(StickyUiCommand.ReprojectDockGroup(
                plan, snapshot), delegate(StickyUiCommandResult result)
                {
                    try
                    {
                        if (_dockRestores.ContainsGroup(groupId) || !TryApplyDockTopologyResult(result, snapshot,
                            targetSurface, expectedIds, plan.PlanSequence))
                        {
                            DisplayDiagnostics.Trace("DockReprojectRejected",
                                "group=" + groupId + " generation=" +
                                snapshot.Generation + " reason=" + reason);
                            return;
                        }
                        if (reason == DockTopologyReprojectReason.TemporaryRehome)
                        {
                            foreach (string noteId in expectedIds)
                                _placementRuntime.MarkTemporaryRehome(noteId,
                                    "dock-preferred-display-missing");
                            DisplayDiagnostics.Trace("TemporaryRehome",
                                "dockGroup=" + groupId + " members=" + expectedIds.Count +
                                " target=" + targetSurface.RuntimeSurfaceId);
                            return;
                        }
                        if (reason == DockTopologyReprojectReason.PreferredReturn)
                        {
                            foreach (string noteId in expectedIds)
                                _placementRuntime.MarkReturnedToPreferred(
                                    noteId);
                            DisplayDiagnostics.Trace("PreferredReturned",
                                "dockGroup=" + groupId + " members=" + expectedIds.Count +
                                " target=" + targetSurface.RuntimeSurfaceId);
                            return;
                        }
                        // Runtime repair applies actual facts only; durable
                        // preference and temporary-rehome state stay intact.
                        DisplayDiagnostics.Trace("DockRuntimeRepaired",
                            "dockGroup=" + groupId + " members=" +
                            expectedIds.Count + " target=" +
                            targetSurface.RuntimeSurfaceId + " generation=" + snapshot.Generation);
                    }
                    finally
                    {
                        _pendingDockTopologyGroups.Remove(groupId);
                        DisplayTopologySnapshot current =
                            CurrentTopologySnapshot();
                        StickyNoteData currentRoot = _notes.Find(root.Id);
                        if (current != null && currentRoot != null &&
                            current.Generation != snapshot.Generation)
                        {
                            List<StickyNoteData> currentGroup =
                                BuildDockChainOrderIncludingHidden(
                                    currentRoot);
                            currentGroup.RemoveAll(
                                delegate(StickyNoteData member)
                                {
                                    return member == null || !member.Visible ||
                                        !IsHostedSticky(member);
                                });
                            ReconcileDockGroup(currentGroup, current,
                                CapturePetWindowFacts(current));
                        }
                    }
                });
        }

        private bool TryApplyDockTopologyResult(StickyUiCommandResult result,
            DisplayTopologySnapshot snapshot,
            DisplaySurfaceSnapshot targetSurface,
            IList<string> expectedIds, long expectedPlanSequence,
            bool forceVisible = false, bool persist = true, bool acceptCreatedSessions = false)
        {
            if (result == null ||
                result.Status != StickyUiCommandStatus.Handled ||
                result.DockBatchResult == null || snapshot == null ||
                targetSurface == null || CurrentTopologySnapshot() == null ||
                CurrentTopologySnapshot().Generation != snapshot.Generation)
                return false;
            DockBatchResult batch = result.DockBatchResult;
            if (batch.PlanSequence != expectedPlanSequence ||
                batch.TopologyGeneration != snapshot.Generation ||
                !String.Equals(batch.TargetSurfaceId,
                    targetSurface.RuntimeSurfaceId,
                    StringComparison.OrdinalIgnoreCase) ||
                batch.TargetDpi <= 0 ||
                batch.Members.Count != expectedIds.Count)
                return false;
            HashSet<string> expected = new HashSet<string>(expectedIds,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> actual = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<DockCommitCandidate> candidates =
                new List<DockCommitCandidate>();
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyNoteData canonical = member == null ? null :
                    _notes.Find(member.NoteId);
                bool created = acceptCreatedSessions && member != null && member.SessionCreated;
                if (member == null || member.Snapshot == null ||
                    member.Facts == null || canonical == null ||
                    !expected.Contains(member.NoteId) ||
                    !actual.Add(member.NoteId) ||
                    !IsTopologyCurrent(snapshot) ||
                    WindowFactsVersionRules.Classify(member.NoteId,
                        member.WindowSequence, member.Facts, snapshot,
                        snapshot.Generation) !=
                        WindowFactsVersionDisposition.Current ||
                    member.Facts.TopologyGeneration != snapshot.Generation ||
                    member.Facts.WindowSequence != member.WindowSequence ||
                    member.Facts.Dpi != batch.TargetDpi ||
                    !String.Equals(member.Facts.RuntimeGdiName,
                        targetSurface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !_hostedRuntime.CanApplyBatchSequence(member, acceptCreatedSessions)) return false;
                if (!created && !_placementRuntime.CanAcceptEffective(
                    member.NoteId,
                    member.Facts))
                {
                    DisplayDiagnostics.Trace(
                        "EffectiveAcceptanceRejected",
                        "consumer=DockTopologyResult note=" +
                        member.NoteId);
                    return false;
                }
                candidates.Add(new DockCommitCandidate(member, canonical,
                    null));
            }
            if (actual.Count != expected.Count) return false;
            foreach (DockCommitCandidate candidate in candidates)
            {
                DockBatchMemberResult member = candidate.Member;
                if (acceptCreatedSessions && member.SessionCreated)
                    _placementRuntime.InvalidateEffective(member.NoteId);
                member.Snapshot.ApplyContentTo(candidate.Canonical);
                candidate.Canonical.Visible = forceVisible ? true : member.Snapshot.Visible;
                candidate.Canonical.AlwaysOnTop =
                    member.Snapshot.AlwaysOnTop;
                ApplyHostedStickyFactsGeometry(candidate.Canonical,
                    member.Facts, snapshot);
                if (!_placementRuntime.TryUpdateEffective(member.NoteId,
                    member.Facts, snapshot))
                    throw new InvalidOperationException(
                        "Dock topology Effective acceptance changed after preflight.");
                _hostedRuntime.AcceptBatchSequence(member, acceptCreatedSessions);
            }
            if (persist) _notes.SaveAsync();
            return true;
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
                                StickyUiCommandStatus.Handled &&
                                ApplyReprojectResult(result, noteId, snapshot))
                            {
                                _placementRuntime.
                                    MarkReturnedToPreferred(noteId);
                                DisplayDiagnostics.Trace(
                                    "PreferredReturned",
                                    "note=" + noteId);
                            }
                            else if (IsStaleReprojectResult(result, noteId,
                                snapshot))
                                ScheduleLatestStandaloneReconcile(noteId);
                        });
                }
                return;
            }

            if (_placementRuntime.IsTemporaryRehome(note.Id)) return;
            DisplaySurfaceSnapshot fallback;
            StickyUiReprojectTarget rehomeTarget;
            if (!TryBuildTemporaryRehomeTarget(note, snapshot, petFacts,
                true, out fallback, out rehomeTarget,
                _placementRuntime.GetEffective(note.Id))) return;
            string rehomedNoteId = note.Id;
            PostHostedStickyCommand(StickyUiCommand.Reproject(rehomedNoteId,
                rehomeTarget, snapshot),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled &&
                        ApplyReprojectResult(result, rehomedNoteId, snapshot))
                    {
                        CompleteTemporaryRehome(rehomedNoteId, fallback,
                            "preferred-display-missing", snapshot);
                    }
                    else if (IsStaleReprojectResult(result, rehomedNoteId,
                        snapshot))
                        ScheduleLatestStandaloneReconcile(rehomedNoteId);
                });
        }

        // Builds the typed temporary-rehome intent without guessing the
        // target DPI: the Sticky STA projects the preferred logical size with
        // GetDpiForWindow after bootstrapping the HWND onto the fallback
        // surface. The durable preferred fields are never modified here.
        private static bool TryBuildTemporaryRehomeTarget(StickyNoteData note,
            DisplayTopologySnapshot topology, WindowFacts petFacts,
            bool showAfter, out DisplaySurfaceSnapshot fallback,
            out StickyUiReprojectTarget target, WindowFacts actual = null)
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
                actual == null
                    ? new PhysicalRect(note.X, note.Y, note.Width, note.Height)
                    : actual.PhysicalBounds,
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
                out fallback, out rehomeTarget, _placementRuntime.GetEffective(note.Id));
            StickyNoteUiSnapshot createSnapshot =
                StickyNoteUiSnapshot.FromData(note);
            StickyUiCommand command = StickyUiCommand.Create(
                createSnapshot, focusEditor, _reminders.GetItems(), topology,
                rehomeTarget, StickyPlacementRecovery.SelectForShow(note, topology));
            PostHostedStickyCommand(command,
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (temporaryRehome)
                        {
                            if (!ApplyReprojectResult(result, noteId, topology))
                            {
                                if (IsStaleReprojectResult(result, noteId,
                                    topology))
                                {
                                    ScheduleLatestStandaloneReconcile(noteId);
                                    return;
                                }
                                HandleHostedStickyFailure(
                                    new string[] { noteId },
                                    "sticky-hosted-create-reproject", result);
                                return;
                            }
                        }
                        else
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, true, result.Facts, result.Topology);
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
                out fallback, out rehomeTarget, _placementRuntime.GetEffective(note.Id)))
            {
                PostHostedStickyCommand(StickyUiCommand.Reproject(noteId,
                    rehomeTarget, topology),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled &&
                            ApplyReprojectResult(result, noteId, topology))
                        {
                            CompleteTemporaryRehome(noteId, fallback,
                                "preferred-display-missing-at-reopen",
                                topology);
                            if (focusEditor)
                                PostHostedStickyCommand(
                                    StickyUiCommand.FocusPrimaryInput(noteId),
                                    delegate(StickyUiCommandResult ignored) { });
                            return;
                        }
                        if (IsStaleReprojectResult(result, noteId, topology))
                        {
                            ScheduleLatestStandaloneReconcile(noteId);
                            return;
                        }
                        HandleHostedStickyFailure(new string[] { noteId },
                            "sticky-hosted-show", result);
                    });
                return true;
            }
            PostHostedStickyCommand(StickyUiCommand.Show(noteId,
                focusEditor, topology, StickyPlacementRecovery.SelectForShow(note, topology)),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence, true, result.Facts, result.Topology);
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
            if (note != null) CancelHostedDockRestores(note.Id);
            if (!IsHostedSticky(note)) return false;
            string noteId = note.Id;
            ClearHostedDockResizeSessionIfMember(noteId);
            PostHostedStickyCommand(StickyUiCommand.Hide(noteId),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status == StickyUiCommandStatus.Handled)
                    {
                        if (result.Snapshot != null &&
                            !result.Snapshot.Visible)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, true, result.Facts, result.Topology);
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
            CancelHostedDockRestores();
            ClearHostedDockResizeSession();
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
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value, false) || !geometryCurrent)
                    return;
                StickyNoteData canonical = _notes.Find(value.NoteId);
                if (canonical == null) return;
                DockWindowFacts facts = DockWindowFacts.FromWindowFacts(
                    value.Facts, canonical.Visible, canonical.AlwaysOnTop);
                if (facts == null) return;
                if (value.Kind == StickyUiEventKind.HeaderDragStarted)
                    BeginStickyDockDrag(facts, value.Facts, value.Topology);
                else if (value.Kind == StickyUiEventKind.HeaderDragMoved)
                {
                    MoveStickyDockDrag(facts, value.Facts, value.Topology);
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
            if (value.Kind == StickyUiEventKind.DockDividerResizeStarted ||
                value.Kind == StickyUiEventKind.DockHorizontalResizeStarted)
            {
                if (IsCurrentHostedGeometryEvent(value) && ApplyHostedStickyEvent(value, false))
                    BeginHostedStickyDockResize(value, value.Kind == StickyUiEventKind.DockHorizontalResizeStarted
                        ? DockResizeKind.Horizontal : DockResizeKind.Divider);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizing ||
                value.Kind == StickyUiEventKind.DockHorizontalResizing)
            {
                if (IsCurrentHostedGeometryEvent(value) &&
                    _hostedRuntime.CanApplySequence(value.NoteId, value.Sequence))
                    ResizeHostedStickyDock(value);
                return;
            }
            if (value.Kind == StickyUiEventKind.DockDividerResizeCompleted ||
                value.Kind == StickyUiEventKind.DockHorizontalResizeCompleted)
            {
                CompleteHostedStickyDockResize(value);
                return;
            }
            if (value.Kind == StickyUiEventKind.CloseRequested)
            {
                if (!IsCurrentHostedGeometryEvent(value) ||
                    !ApplyHostedStickyEvent(value, false)) return;
                CloseStickyDockNote(_notes.Find(value.NoteId),
                    DockWindowFacts.FromWindowFacts(value.Facts,
                        value.Snapshot.Visible, value.Snapshot.AlwaysOnTop));
                return;
            }
            if (value.Kind == StickyUiEventKind.SnapshotChanged)
            {
                StickyNoteData canonical = _notes.Find(value.NoteId);
                bool topMostChanged = canonical != null &&
                    value.Snapshot != null && canonical.AlwaysOnTop !=
                    value.Snapshot.AlwaysOnTop;
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value)) return;
                if (geometryCurrent)
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
                bool geometryCurrent = IsCurrentHostedGeometryEvent(value);
                if (!ApplyHostedStickyEvent(value, false)) return;
                if (!geometryCurrent)
                {
                    DisplayDiagnostics.Trace("PreferredCommitRejected",
                        "note=" + value.NoteId + " reason=stale-user-resize");
                    return;
                }
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
                ClearHostedDockResizeSessionIfMember(value.NoteId);
                ApplyHostedStickyEvent(value);
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
            ApplyHostedStickyEvent(value);
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
            return value != null && ApplyHostedStickySnapshot(value.Snapshot,
                value.Sequence, persist, value.Facts, value.Topology);
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
        private bool ApplyReprojectResult(StickyUiCommandResult result,
            string noteId, DisplayTopologySnapshot expectedTopology)
        {
            if (result == null ||
                result.Status != StickyUiCommandStatus.Handled ||
                result.Snapshot == null || result.Facts == null ||
                result.Topology == null || expectedTopology == null ||
                !IsTopologyCurrent(expectedTopology) ||
                result.Topology.Generation != expectedTopology.Generation ||
                WindowFactsVersionRules.Classify(noteId, result.Sequence,
                    result.Facts, expectedTopology,
                    expectedTopology.Generation) !=
                    WindowFactsVersionDisposition.Current ||
                !_hostedRuntime.CanApplySequence(noteId,
                    result.Sequence)) return false;
            StickyNoteData canonical = _notes.Find(noteId);
            if (canonical == null) return false;
            if (!_placementRuntime.CanAcceptEffective(noteId, result.Facts))
            {
                DisplayDiagnostics.Trace(
                    "EffectiveAcceptanceRejected",
                    "consumer=Reproject note=" + noteId);
                return false;
            }
            result.Snapshot.ApplyContentTo(canonical);
            canonical.Visible = result.Snapshot.Visible;
            canonical.AlwaysOnTop = result.Snapshot.AlwaysOnTop;
            ApplyHostedStickyFactsGeometry(canonical, result.Facts,
                result.Topology);
            if (!_placementRuntime.TryUpdateEffective(noteId, result.Facts, result.Topology))
                throw new InvalidOperationException(
                    "Reproject Effective acceptance changed after preflight.");
            _hostedRuntime.RecordSequence(noteId, result.Sequence);
            _notes.SaveAsync();
            RefreshMenuText();
            return true;
        }

        private bool IsStaleReprojectResult(StickyUiCommandResult result,
            string noteId, DisplayTopologySnapshot expectedTopology)
        {
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.Facts == null || result.Topology == null ||
                expectedTopology == null) return false;
            return !IsTopologyCurrent(expectedTopology) ||
                result.Topology.Generation != expectedTopology.Generation ||
                WindowFactsVersionRules.Classify(noteId, result.Sequence,
                    result.Facts, expectedTopology,
                    expectedTopology.Generation) ==
                    WindowFactsVersionDisposition.StaleTopology;
        }

        // One deferred reconcile per note collapses topology churn. An active
        // dock gesture owns its source until its epoch reaches a barrier.
        private void ScheduleLatestStandaloneReconcile(string noteId)
        {
            if (String.IsNullOrWhiteSpace(noteId) || IsDisposed || Disposing ||
                !_pendingStandaloneTopologyNotes.Add(noteId)) return;
            BeginInvoke((MethodInvoker)delegate
            {
                _pendingStandaloneTopologyNotes.Remove(noteId);
                if (IsDisposed || Disposing || String.Equals(noteId,
                    _dockInteraction.SourceNoteId, StringComparison.OrdinalIgnoreCase))
                    return;
                StickyNoteData note = _notes.Find(noteId);
                DisplayTopologySnapshot snapshot = CurrentTopologySnapshot();
                if (note == null || snapshot == null || !IsHostedSticky(note))
                    return;
                ReconcileStandaloneSticky(note, snapshot,
                    CapturePetWindowFacts(snapshot));
            });
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
        // is derived from the captured actual facts plus the finalizing
        // epoch's exact topology, then membership and content are persisted
        // once. The commit uses its captured generation throughout.
        private void CompleteDockDurableCommit(StickyUiCommandResult result,
            DisplayTopologySnapshot expectedTopology, long expectedEpoch,
            StickyNoteData seed,
            StickyNoteData remainderSeed, IList<string> expectedMemberIds,
            long expectedPlanSequence)
        {
            List<DockCommitCandidate> candidates;
            string rejection;
            if (!TryPrepareDockCommit(result, expectedTopology, expectedEpoch,
                expectedMemberIds,
                expectedPlanSequence, out candidates, out rejection))
            {
                TraceDockCommitRejected(rejection);
                return;
            }

            bool merged = _dockInteraction.PendingMerge != null;
            if (merged && !_dockInteraction.PendingMerge.TryCommit(_notes.GetAll()))
            {
                TraceDockCommitRejected("membership changed before final commit");
                return;
            }
            _lastAppliedDockPlanSequence = Math.Max(
                _lastAppliedDockPlanSequence, expectedPlanSequence);
            foreach (DockCommitCandidate candidate in candidates)
            {
                DockBatchMemberResult member = candidate.Member;
                StickyNoteData canonical = candidate.Canonical;
                member.Snapshot.ApplyContentTo(canonical);
                canonical.Visible = member.Snapshot.Visible;
                canonical.AlwaysOnTop = member.Snapshot.AlwaysOnTop;
                ApplyHostedStickyFactsGeometry(canonical, member.Facts,
                    expectedTopology);
                if (!_placementRuntime.TryUpdateEffective(member.NoteId,
                    member.Facts, expectedTopology))
                    throw new InvalidOperationException(
                        "Dock durable Effective acceptance changed after preflight.");
                _hostedRuntime.RecordSequence(member.NoteId,
                    member.WindowSequence);
                LogicalRect local = candidate.Preference.LocalLogicalRect;
                CommitHostedStickyPreferred(canonical,
                    candidate.Preference.PreferredTargetKey,
                    local.X, local.Y, local.Width, local.Height,
                    PlacementReason.DockCommit);
            }
            if (merged)
            {
                List<StickyNoteData> group = BuildDockChainOrderIncludingHidden(seed);
                bool topMost = group[0].AlwaysOnTop;
                foreach (StickyNoteData member in group) member.AlwaysOnTop = topMost;
            }
            _notes.SaveAsync();
            foreach (DockCommitCandidate candidate in candidates)
                _placementRuntime.MarkUserPlacementCommit(
                    candidate.Member.NoteId);
            if (merged) ApplyDockComponentTopMost(seed, seed.AlwaysOnTop, null);
        }

        private bool TryPrepareDockCommit(StickyUiCommandResult result,
            DisplayTopologySnapshot expectedTopology, long expectedEpoch,
            IList<string> expectedMemberIds,
            long expectedPlanSequence,
            out List<DockCommitCandidate> candidates,
            out string rejection)
        {
            candidates = new List<DockCommitCandidate>();
            rejection = String.Empty;
            if (result == null ||
                result.Status != StickyUiCommandStatus.Handled ||
                result.DockBatchResult == null)
            {
                rejection = "final batch was not handled";
                return false;
            }
            if (expectedTopology == null || !IsTopologyCurrent(expectedTopology))
            {
                rejection = "finalizing topology is no longer current";
                return false;
            }
            DockBatchResult batch = result.DockBatchResult;
            if (batch.PlanSequence != expectedPlanSequence ||
                batch.InteractionEpoch != expectedEpoch ||
                batch.TopologyGeneration != expectedTopology.Generation ||
                batch.TargetDpi <= 0)
            {
                rejection = "final batch plan/topology mismatch";
                return false;
            }
            DisplaySurfaceSnapshot targetSurface =
                expectedTopology.FindByRuntimeSurfaceId(
                    batch.TargetSurfaceId);
            if (targetSurface == null)
            {
                rejection = "final batch target surface unavailable";
                return false;
            }
            HashSet<string> expected = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            if (expectedMemberIds != null)
                foreach (string noteId in expectedMemberIds)
                    if (String.IsNullOrWhiteSpace(noteId) ||
                        !expected.Add(noteId))
                    {
                        rejection = "expected member set is invalid";
                        return false;
                    }
            if (expected.Count == 0 || batch.Members.Count != expected.Count)
            {
                rejection = "final batch member count mismatch";
                return false;
            }
            HashSet<string> actual = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (DockBatchMemberResult member in batch.Members)
            {
                if (member == null || member.Snapshot == null ||
                    member.Facts == null ||
                    !expected.Contains(member.NoteId) ||
                    !actual.Add(member.NoteId))
                {
                    rejection = "final batch member missing, duplicate, or incomplete";
                    return false;
                }
                if (WindowFactsVersionRules.Classify(member.NoteId,
                        member.WindowSequence, member.Facts,
                        expectedTopology, expectedTopology.Generation) !=
                        WindowFactsVersionDisposition.Current ||
                    member.Facts.TopologyGeneration !=
                        expectedTopology.Generation ||
                    member.Facts.WindowSequence != member.WindowSequence ||
                    member.Facts.Dpi != batch.TargetDpi ||
                    !String.Equals(member.Facts.RuntimeGdiName,
                        targetSurface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(member.Facts.WindowId, member.NoteId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !_hostedRuntime.CanApplySequence(member.NoteId,
                        member.WindowSequence))
                {
                    rejection = "final batch member facts are stale";
                    return false;
                }
                StickyNoteData canonical = _notes.Find(member.NoteId);
                WindowPlacementPreference preference;
                if (canonical == null ||
                    !StickyPlacementRules.TryBuildPreferredPlacement(
                        member.Facts, expectedTopology,
                        canonical.PreferredDisplayTargetKey,
                        out preference) || preference == null ||
                    !preference.IsValid)
                {
                    rejection = "final batch preference unavailable";
                    return false;
                }
                if (!_placementRuntime.CanAcceptEffective(
                    member.NoteId,
                    member.Facts))
                {
                    rejection = "final batch effective facts rejected";
                    return false;
                }
                candidates.Add(new DockCommitCandidate(member, canonical,
                    preference));
            }
            if (actual.Count != expected.Count)
            {
                rejection = "final batch omitted an expected member";
                return false;
            }
            return true;
        }

        private static void TraceDockCommitRejected(string reason)
        {
            DisplayDiagnostics.Trace("DockCommitRejected",
                reason ?? String.Empty);
        }

        private sealed class DockCommitCandidate
        {
            internal DockCommitCandidate(DockBatchMemberResult member,
                StickyNoteData canonical,
                WindowPlacementPreference preference)
            {
                Member = member;
                Canonical = canonical;
                Preference = preference;
            }

            internal DockBatchMemberResult Member { get; private set; }
            internal StickyNoteData Canonical { get; private set; }
            internal WindowPlacementPreference Preference
                { get; private set; }
        }

        private bool ApplyHostedStickySnapshot(StickyNoteUiSnapshot snapshot,
            long sequence, bool persist = true, WindowFacts facts = null,
            DisplayTopologySnapshot topology = null)
        {
            if (snapshot == null ||
                !_hostedRuntime.CanApplySequence(snapshot.NoteId,
                    sequence)) return false;
            StickyNoteData canonical = _notes.Find(snapshot.NoteId);
            if (canonical == null) return false;
            bool visibilityChanged = canonical.Visible !=
                snapshot.Visible;
            string oldHiddenTitle = canonical.Visible
                ? String.Empty : canonical.DisplayTitle;
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            WindowFactsVersionDisposition geometryDisposition =
                WindowFactsVersionRules.Classify(snapshot.NoteId, sequence,
                    facts, topology, current == null ? -1 : current.Generation);
            snapshot.ApplyContentTo(canonical);
            canonical.Visible = snapshot.Visible;
            canonical.AlwaysOnTop = snapshot.AlwaysOnTop;
            if (geometryDisposition == WindowFactsVersionDisposition.Current)
            {
                if (_placementRuntime.TryUpdateEffective(snapshot.NoteId,
                    facts, topology))
                    ApplyHostedStickyFactsGeometry(canonical, facts,
                        topology);
                else
                    DisplayDiagnostics.Trace("HostedGeometryRejected",
                        "note=" + snapshot.NoteId + " reason=effective-monotonic");
            }
            else if (facts != null)
            {
                DisplayDiagnostics.Trace(geometryDisposition ==
                    WindowFactsVersionDisposition.StaleTopology
                    ? "HostedGeometryStale" : "HostedGeometryRejected",
                    "note=" + snapshot.NoteId + " sequence=" + sequence);
            }
            _hostedRuntime.RecordSequence(snapshot.NoteId, sequence);
            if (persist) _notes.SaveAsync();
            RefreshMenuText();
            if (visibilityChanged || (!canonical.Visible &&
                !String.Equals(oldHiddenTitle, canonical.DisplayTitle,
                    StringComparison.Ordinal))) RefreshNoteTabs();
            return true;
        }

        // Only dependent user actions wait for horizontal final facts; input
        // stays responsive and the queue belongs to this one resize session.
        private bool DeferDockResizeMutation(string noteId, Action action)
        {
            DockResizeSession session = _dockResize;
            if (session == null) return false;
            if (noteId != null && !session.Contains(noteId))
            {
                StickyNoteData note = _notes.Find(noteId), source = _notes.Find(session.SourceNoteId);
                if (note == null || source == null || String.IsNullOrEmpty(source.DockGroupId) ||
                    !String.Equals(source.DockGroupId, note.DockGroupId, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return session.DeferMutation(action);
        }

        private void ClearHostedDockResizeSession(DockResizeSession expected = null)
        {
            if (expected != null && !ReferenceEquals(_dockResize, expected)) return;
            DockResizeSession previous = _dockResize;
            _dockResize = null;
            if (previous != null)
                foreach (Action action in previous.Finish())
                {
                    try { action(); }
                    catch (Exception error) { ShowStickyWindowFailure("尺寸调整后续操作", error); }
                }
        }

        private void ClearHostedDockResizeSessionIfMember(string noteId)
        {
            if (_dockResize != null && _dockResize.Contains(noteId))
                ClearHostedDockResizeSession();
        }

        private void CompleteHostedStickyDockResize(StickyUiEvent value)
        {
            if (value == null || value.Snapshot == null || _dockInteraction.IsActive ||
                !IsCurrentHostedGeometryEvent(value) ||
                !_placementRuntime.CanAcceptEffective(value.NoteId, value.Facts) ||
                !_hostedRuntime.CanApplySequence(value.NoteId, value.Sequence)) return;
            StickyNoteData source = _notes.Find(value.NoteId);
            if (source == null || !source.Visible) return;
            DockResizeKind kind = value.Kind == StickyUiEventKind.DockHorizontalResizeCompleted
                ? DockResizeKind.Horizontal : DockResizeKind.Divider;
            WindowPlacementPreference sourcePreference;
            if (!StickyResizePreferences.TryBuild(source, value.Facts, value.Topology, kind, true, out sourcePreference)) return;
            DockResizeSession session = _dockResize;
            if (session != null && (session.Kind != kind || !session.IsResizing ||
                !String.Equals(session.SourceNoteId, value.NoteId, StringComparison.OrdinalIgnoreCase))) return;
            // A topology barrier retires the old gesture. A current completion
            // may settle from freshly accepted facts, never from its old baseline.
            if (session == null)
            {
                session = CaptureHostedResizeSession(value, kind);
                _dockResize = session;
            }
            if (session == null || !session.MatchesMembers(BuildDockChainOrder(source)))
            {
                CommitResizeSourceFinal(value, source, value.Topology, sourcePreference);
                ClearHostedDockResizeSession(session);
                return;
            }
            DockResizeBatch final = session.BeginFinal(value);
            if (final == null) return;
            DisplayDiagnostics.Trace("DockResizeCompleted", "note=" + value.NoteId + " kind=" + kind +
                " width=" + value.Facts.PhysicalBounds.Width +
                " height=" + value.Facts.PhysicalBounds.Height +
                " top=" + value.Facts.PhysicalBounds.Top + " accepted=true" +
                " followers=" + final.Targets.Count + " seq=" + value.Sequence);
            // Commit the source in this Pet turn before posting followers, so
            // hide/reopen observes the resized preference. Disk I/O queues.
            CommitResizeSourceFinal(value, source, value.Topology, sourcePreference);
            PostResizeFinal(session, final);
        }

        private void PostResizeFinal(DockResizeSession session,
            DockResizeBatch final)
        {
            _stickyUiHost.PostFinalResizeBatch(session.Mailbox, final,
                result => OnResizeFinalBatchApplied(session, final, result), _petUiContext);
        }

        private void OnResizeFinalBatchApplied(DockResizeSession session,
            DockResizeBatch expected, StickyUiCommandResult result)
        {
            if (!ReferenceEquals(_dockResize, session) || !session.IsCurrentFinal(expected)) return;
            DockBatchResult batch = result != null && result.Status == StickyUiCommandStatus.Handled
                ? result.DockBatchResult : null;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (!CanAcceptResizeBatch(session, batch, topology))
            {
                DisplayDiagnostics.Trace("DockResizeFinalRejected", "note=" + session.SourceNoteId + " kind=" + session.Kind);
                ClearHostedDockResizeSession(session);
                return;
            }
            DockResizeBatch correction = session.TryCorrect(expected, batch);
            if (correction != null)
            {
                PostResizeFinal(session, correction);
                return;
            }
            if (!session.LayoutIsExact(batch))
                DisplayDiagnostics.Trace("DockResizeLayoutVerifyFailed", "note=" + session.SourceNoteId + " kind=" + session.Kind);
            try
            {
                if (!ApplyResizeBatchCanonical(batch, topology, true, session.Kind))
                    DisplayDiagnostics.Trace("DockResizeFinalRejected", "reason=preference note=" + session.SourceNoteId);
            }
            finally { ClearHostedDockResizeSession(session); }
        }

        private void CommitResizeSourceFinal(StickyUiEvent value,
            StickyNoteData sourceCanonical,
            DisplayTopologySnapshot topology, WindowPlacementPreference preference)
        {
            if (sourceCanonical == null) return;
            _synchronizingDockLayout = true;
            try
            {
                value.Snapshot.ApplyContentTo(sourceCanonical);
                sourceCanonical.Visible = value.Snapshot.Visible;
                sourceCanonical.AlwaysOnTop = value.Snapshot.AlwaysOnTop;
                // Exact physical facts: never apply the persisted-size clamp.
                ApplyHostedStickyFactsGeometry(sourceCanonical, value.Facts, topology);
                if (!_placementRuntime.TryUpdateEffective(value.NoteId, value.Facts, topology))
                    throw new InvalidOperationException("Resize source acceptance changed after preflight.");
                LogicalRect local = preference.LocalLogicalRect;
                CommitHostedStickyPreferred(sourceCanonical, preference.PreferredTargetKey,
                    local.X, local.Y, local.Width, local.Height, PlacementReason.UserResizeCommit);
                _placementRuntime.MarkUserPlacementCommit(value.NoteId);
                _hostedRuntime.RecordSequence(value.NoteId, value.Sequence);
                _notes.SaveAsync();
            }
            finally { _synchronizingDockLayout = false; }
            RefreshMenuText();
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
                    CancelHostedDockRestores(noteId);
                    ClearHostedDockResizeSessionIfMember(noteId);
                    _hostedRuntime.RemoveNote(noteId);
                    _placementRuntime.InvalidateEffective(noteId);
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
            CancelHostedDockRestores();
            if (_hostedRuntime.NoteCount == 0 ||
                _hostedRuntime.ExitPrepared)
                return false;
            if (DeferDockResizeMutation(null, BeginExitSequence)) return true;
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
                                finalSnapshot.Sequence, false, finalSnapshot.Facts,
                                finalSnapshot.Topology);
                    _hostedRuntime.PrepareExit();
                    _stickyUiHost.BeginShutdown();
                    BeginExitSequence();
                });
        }

        private void CloseHostedStickyRuntimeForReload(
            Action<StickyUiCommandResult> completed)
        {
            if (completed == null) return;
            if (DeferDockResizeMutation(null, () => CloseHostedStickyRuntimeForReload(completed))) return;
            CancelHostedDockRestores();
            ClearHostedDockResizeSession();
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
                                finalSnapshot.Sequence, false, finalSnapshot.Facts,
                                finalSnapshot.Topology);
                            _hostedRuntime.RemoveNote(finalSnapshot.NoteId);
                            _placementRuntime.InvalidateEffective(
                                finalSnapshot.NoteId);
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
            if (DeferDockResizeMutation(note.Id, () => ShowHostedSticky(note, focusEditor, persistVisibility))) return;
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
            if (ordered == null || ordered.Count < 2) return false;
            if (_dockRestores.ContainsGroup(ordered[0].DockGroupId)) return true;
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (topology == null) return false;
            foreach (StickyNoteData member in ordered)
                ClearHostedDockResizeSessionIfMember(member.Id);
            if (MigrateDockRestorePreferredIfNeeded(ordered, topology)) _notes.SaveAsync();
            if (!DockRestoreOperation.HasCompletePreferred(ordered))
                return TryRestoreHostedDockComponentLegacyFallback(ordered, focus, focusEditor, persistVisibility);

            DockRestoreOperation operation = DockRestoreOperation.TryCreate(ordered,
                focus == null ? null : focus.Id, focusEditor, persistVisibility,
                topology, CapturePetWindowFacts(topology), _dockPlanMailbox.NextSequence());
            if (!_dockRestores.TryBegin(operation)) return false;
            try
            {
                PostHostedStickyCommand(StickyUiCommand.RestoreDockGroup(operation, _reminders.GetItems()),
                    result => CompleteHostedDockRestore(operation, result));
                return true;
            }
            catch
            {
                CancelHostedDockRestore(operation);
                throw;
            }
        }

        private bool MigrateDockRestorePreferredIfNeeded(
            IList<StickyNoteData> group, DisplayTopologySnapshot topology)
        {
            bool changed = false;
            foreach (StickyNoteData member in group)
            {
                if (!String.IsNullOrWhiteSpace(member.PreferredDisplayTargetKey) &&
                    member.PreferredLocalLogicalWidth > 0 && member.PreferredLocalLogicalHeight > 0) continue;
                if (StickyPlacementRules.MigrateV10Preferred(member, topology)) changed = true;
            }
            return changed;
        }

        private void CompleteHostedDockRestore(DockRestoreOperation operation, StickyUiCommandResult result)
        {
            if (!_dockRestores.IsCurrent(operation)) return;
            if (!operation.MatchesMembers(BuildDockChainOrderIncludingHidden(_notes.Find(operation.MemberIds[0]))))
            {
                CancelHostedDockRestore(operation);
                return;
            }
            bool accepted;
            try
            {
                accepted = TryApplyDockTopologyResult(result, operation.Topology, operation.Target,
                    new List<string>(operation.MemberIds), operation.Plan.PlanSequence,
                    forceVisible: true, persist: false, acceptCreatedSessions: true);
            }
            catch
            {
                CancelHostedDockRestore(operation);
                throw;
            }
            _dockRestores.Finish(operation);
            if (!accepted)
            {
                HideUncommittedDockRestore(operation);
                ReportHostedStickyCommandFailure("sticky-hosted-dock-restore", result);
                ShowBubble("Dock 便利贴组恢复未完成，未展开的便利贴仍保留在侧边页签中。");
                return;
            }

            bool topMost = _notes.Find(operation.MemberIds[0]).AlwaysOnTop;
            foreach (string noteId in operation.MemberIds)
            {
                _notes.Find(noteId).AlwaysOnTop = topMost;
                if (operation.Reason == DockTopologyReprojectReason.TemporaryRehome)
                    _placementRuntime.MarkTemporaryRehome(noteId, "dock-preferred-display-missing");
                else _placementRuntime.ClearTemporaryRehome(noteId);
            }
            if (operation.PersistVisibility) _notes.SaveAsync();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
            DisplayDiagnostics.Trace("DockRestoreCompleted", "group=" + operation.GroupId +
                " generation=" + operation.Topology.Generation + " members=" + operation.MemberIds.Count);
            // Focus is a post-commit interaction. Its failure cannot undo placement.
            if (operation.FocusEditor)
            {
                try
                {
                    PostHostedStickyCommand(StickyUiCommand.FocusPrimaryInput(operation.FocusId),
                        focusResult => {
                            if (focusResult == null || focusResult.Status != StickyUiCommandStatus.Handled)
                                DisplayDiagnostics.Trace("DockRestoreFocusRejected", "note=" + operation.FocusId);
                        });
                }
                catch (Exception error) { ApplicationDiagnostics.ReportNonFatal("sticky-dock-restore-focus", error); }
            }
        }

        private void HideUncommittedDockRestore(DockRestoreOperation operation)
        {
            // These hides are queued before any replacement restore. They also
            // cover a batch that finished on the STA just before cancellation.
            foreach (StickyNoteUiSnapshot snapshot in operation.Snapshots)
                if (!snapshot.Visible)
                    PostHostedStickyCommand(StickyUiCommand.Hide(snapshot.NoteId), ignored => { });
        }

        private void CancelHostedDockRestore(DockRestoreOperation operation)
        {
            if (_dockRestores.Finish(operation) && !IsDisposed && !Disposing)
                HideUncommittedDockRestore(operation);
        }

        private void CancelHostedDockRestores(string noteId = null)
        {
            foreach (DockRestoreOperation operation in _dockRestores.Snapshot())
                if (noteId == null || operation.ContainsMember(noteId)) CancelHostedDockRestore(operation);
        }

        private void RestartHostedDockRestores(DisplayTopologySnapshot topology)
        {
            foreach (DockRestoreOperation operation in _dockRestores.Snapshot())
            {
                if (operation.Topology.Generation == topology.Generation) continue;
                CancelHostedDockRestore(operation);
                List<StickyNoteData> group = BuildDockChainOrderIncludingHidden(_notes.Find(operation.MemberIds[0]));
                if (group.Count >= 2)
                    TryRestoreHostedDockComponent(group, _notes.Find(operation.FocusId),
                        operation.FocusEditor, operation.PersistVisibility);
            }
        }

        private bool TryRestoreHostedDockComponentLegacyFallback(
            List<StickyNoteData> ordered, StickyNoteData focus,
            bool focusEditor, bool persistVisibility)
        {
            // Compatibility-only restore path for data that cannot yet
            // produce a complete durable v11 Dock preference. Normal
            // current-schema restore must never enter this method.
            // Scheduled for retirement after PC-3/PC-9.
            DisplayDiagnostics.Trace("DockRestoreLegacyFallback",
                "members=" + (ordered == null ? 0 : ordered.Count));
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
                                result.Sequence, true, result.Facts, result.Topology);
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
            if (DeferDockResizeMutation(null, CollapseAllStickyNotes)) return;
            CancelHostedDockRestores();
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(note);
                if (group.Count == 0) group.Add(note);
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

        private bool TryGetPetDerivedDisplayContext(
            out WindowFacts petFacts,
            out DisplaySurfaceSnapshot surface,
            out Rectangle workArea,
            out SideTabPhysicalMetrics metrics)
        {
            petFacts = null;
            surface = null;
            workArea = Rectangle.Empty;
            metrics = null;

            DisplayTopologySnapshot topology = CurrentTopologySnapshot();

            if (topology == null || !IsHandleCreated ||
                Handle == IntPtr.Zero)
                return false;

            petFacts = CapturePetWindowFacts(topology);

            if (petFacts == null ||
                petFacts.TopologyGeneration != topology.Generation ||
                petFacts.Dpi <= 0)
                return false;

            surface = topology.FindByRuntimeGdiName(
                petFacts.RuntimeGdiName);

            if (surface == null) return false;

            workArea = new Rectangle(
                surface.WorkArea.Left,
                surface.WorkArea.Top,
                surface.WorkArea.Width,
                surface.WorkArea.Height);

            metrics = SideTabPhysicalMetrics.ForDpi(petFacts.Dpi);

            return true;
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
            WindowFacts petFacts;
            DisplaySurfaceSnapshot petSurface;
            Rectangle workArea;
            SideTabPhysicalMetrics metrics;

            if (!TryGetPetDerivedDisplayContext(
                out petFacts,
                out petSurface,
                out workArea,
                out metrics))
                return;

            _leftNoteTabs.ApplyPhysicalMetrics(metrics);
            _rightNoteTabs.ApplyPhysicalMetrics(metrics);

            DockRect petRect = new DockRect(
                petFacts.PhysicalBounds.Left,
                petFacts.PhysicalBounds.Top,
                petFacts.PhysicalBounds.Width,
                petFacts.PhysicalBounds.Height);
            DockRect workRect = new DockRect(
                workArea.Left, workArea.Top,
                workArea.Width, workArea.Height);
            int overlap = SideTabLayoutPolicy.CalculatePhysicalOverlap(
                petRect.Width, metrics);
            int leftCount = SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                hidden.Count, petRect, workRect, metrics.Width, overlap,
                metrics.WindowMarginX);

            StringBuilder signatureBuilder = new StringBuilder();
            foreach (SideTabSnapshot note in hidden)
            {
                signatureBuilder.Append(note.NoteId).Append('|')
                    .Append(note.DisplayTitle).Append('|')
                    .Append(note.ColorArgb).Append('\n');
            }
            signatureBuilder.Append("dpi=")
                .Append(metrics.Dpi)
                .Append('\n');
            signatureBuilder.Append("left=")
                .Append(leftCount)
                .Append('\n');
            string signature = signatureBuilder.ToString();
            if (String.Equals(signature, _noteTabsSignature,
                StringComparison.Ordinal))
            {
                PositionNoteTabs();
                ApplyNoteTabZOrder();
                return;
            }
            _noteTabsSignature = signature;

            int logicalWorkHeight =
                SideTabLayoutPolicy.PhysicalWorkHeightToLogical(
                    workArea.Height, metrics.Dpi);

            int logicalCapacity =
                SideTabLayoutPolicy.LogicalScreenCapacity(
                    logicalWorkHeight);

            DisplayDiagnostics.Trace("SideTabsLayout",
                "topology=" + petFacts.TopologyGeneration +
                " dpi=" + metrics.Dpi +
                " total=" + hidden.Count +
                " left=" + leftCount +
                " right=" + (hidden.Count - leftCount) +
                " petLeft=" + petRect.Left +
                " petRight=" + petRect.Right +
                " workLeft=" + workRect.Left +
                " workRight=" + workRect.Right +
                " stripWidth=" + metrics.Width +
                " overlap=" + overlap +
                " logicalCapacity=" + logicalCapacity +
                " surface=" + petSurface.RuntimeSurfaceId);
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
                WindowFacts facts = _placementRuntime.GetEffective(note.Id);
                if (facts == null) continue;
                PhysicalRect actual = facts.PhysicalBounds;
                Rectangle noteBounds = new Rectangle(actual.Left, actual.Top,
                    actual.Width, actual.Height);
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
            if (_leftNoteTabs == null ||
                _rightNoteTabs == null ||
                !IsHandleCreated ||
                IsDisposed ||
                _positioningNoteTabs)
                return;

            WindowFacts petFacts;
            DisplaySurfaceSnapshot surface;
            Rectangle work;
            SideTabPhysicalMetrics metrics;

            if (!TryGetPetDerivedDisplayContext(
                out petFacts,
                out surface,
                out work,
                out metrics))
                return;

            Rectangle petBounds = new Rectangle(
                petFacts.PhysicalBounds.Left,
                petFacts.PhysicalBounds.Top,
                petFacts.PhysicalBounds.Width,
                petFacts.PhysicalBounds.Height);
            DockRect petRect = new DockRect(
                petBounds.Left, petBounds.Top,
                petBounds.Width, petBounds.Height);
            DockRect workRect = new DockRect(
                work.Left, work.Top, work.Width, work.Height);
            int total = _leftNoteTabs.Controls.Count +
                _rightNoteTabs.Controls.Count;
            int overlap = SideTabLayoutPolicy.CalculatePhysicalOverlap(
                petBounds.Width, metrics);
            int desiredLeftCount =
                SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                    total, petRect, workRect, metrics.Width, overlap,
                    metrics.WindowMarginX);

            if (_leftNoteTabs.Controls.Count != desiredLeftCount ||
                _rightNoteTabs.Controls.Count !=
                    total - desiredLeftCount)
            {
                _noteTabsSignature = String.Empty;
                RefreshNoteTabs();
                return;
            }

            _positioningNoteTabs = true;

            try
            {
                _leftNoteTabs.ApplyPhysicalMetrics(metrics);
                _rightNoteTabs.ApplyPhysicalMetrics(metrics);

                _leftNoteTabs.ShowNear(petBounds, work);
                _rightNoteTabs.ShowNear(petBounds, work);

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
                    DeleteNote = delegate(StickyNoteData note,
                        Action<bool> completed)
                    { DeleteStickyNote(note, completed); },
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
