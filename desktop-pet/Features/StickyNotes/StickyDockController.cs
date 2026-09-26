using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Pet-side Dock authority is low-frequency only: canonical relation,
    // durable commit, restore and topology reconciliation. Live pointer
    // gestures and follower HWND motion belong exclusively to Sticky STA.
    internal sealed class StickyDockController : IDisposable
    {
        private readonly StickyWorkspace _workspace;

        internal StickyDockController(StickyWorkspace workspace) { _workspace = workspace; }

        public void Dispose()
        {
            CancelHostedDockRestores();
        }
        internal void HideStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            string noteId = note.Id;
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(note);
            List<string> affected = new List<string>();
            foreach (StickyNoteData member in component)
                if (member != null) affected.Add(member.Id);
            if (affected.Count == 0) affected.Add(noteId);
            _workspace.PrepareDockStructure(affected,
                "sticky-hide-structure",
                delegate
                {
                    StickyNoteData current =
                        _workspace.Notes.Find(noteId);
                    if (current != null)
                        HideStickyNotePrepared(current);
                });
        }

        private void HideStickyNotePrepared(StickyNoteData note)
        {
            if (note == null) return;
            if (_workspace.PostHostedStickyHide(note)) return;
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(note);
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(snapshot);
            DockWindowFacts rootFacts = DockWindowFacts.FromData(note);
            DockWindowFacts capturedRoot;
            if (snapshot.Count > 0 && facts.TryGetValue(snapshot[0].Id,
                out capturedRoot)) rootFacts = capturedRoot;
            note.Visible = false;
            LayoutDockChain(snapshot, facts,
                rootFacts.X, rootFacts.Y, rootFacts.Width);
            _workspace.Notes.SaveAsync();
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
        }

        private long _dockSceneRevision;
        private long _nextDockOperationSequence;
        private readonly Queue<long> _acceptedLocalDockGestureOrder =
            new Queue<long>();
        private readonly HashSet<long> _acceptedLocalDockGestures =
            new HashSet<long>();
        private readonly HashSet<string> _pendingDockTopologyGroups =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private long NextDockOperationSequence()
        {
            _nextDockOperationSequence =
                _nextDockOperationSequence == Int64.MaxValue
                    ? 1 : _nextDockOperationSequence + 1;
            return _nextDockOperationSequence <= 0
                ? _nextDockOperationSequence = 1
                : _nextDockOperationSequence;
        }

        internal void ApplyDockComponentTopMost(StickyNoteData seed,
            bool alwaysOnTop, string alreadyAppliedNoteId)
        {
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(seed);
            foreach (StickyNoteData note in component)
            {
                note.AlwaysOnTop = alwaysOnTop;
                if (String.Equals(note.Id, alreadyAppliedNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (!_workspace.IsHostedSticky(note)) continue;
                _workspace.PostHostedStickyCommand(StickyUiCommand.SetTopMost(
                    note.Id, alwaysOnTop),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled)
                            _workspace.ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, false, result.Facts, result.Topology);
                        else StickyWorkspace.ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-topmost", result);
                    });
            }
        }

        internal void CloseStickyDockNote(StickyNoteData sourceData,
            DockWindowFacts sourceFacts)
        {
            if (sourceData == null || sourceFacts == null) return;
            string noteId = sourceData.Id;
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(sourceData);
            List<string> affected = new List<string>();
            foreach (StickyNoteData member in component)
                if (member != null) affected.Add(member.Id);
            _workspace.PrepareDockStructure(affected,
                "sticky-close-structure",
                delegate
                {
                    StickyNoteData current =
                        _workspace.Notes.Find(noteId);
                    if (current == null) return;
                    CloseStickyDockNotePrepared(current,
                        GetHostedDockFacts(current) ?? sourceFacts);
                });
        }

        private void CloseStickyDockNotePrepared(
            StickyNoteData sourceData,
            DockWindowFacts sourceFacts)
        {
            if (sourceData == null || sourceFacts == null) return;
            CancelHostedDockRestores(sourceData.Id);
            List<StickyNoteData> ordered =
                BuildDockChainOrder(sourceData);
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(sourceData);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, sourceData.Id,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (StickyDockOperations.ShouldCollapseWholeDockGroup(
                sourceIndex, ordered.Count))
            {
                // The top header is the group-level close handle. Preserve the
                // links so expanding all side tabs restores the same stack.
                foreach (StickyNoteData note in snapshot)
                {
                    note.Visible = false;
                    _workspace.PostHostedStickyHide(note);
                }
            }
            else
            {
                // A lower X temporarily hides exactly that member.  Its group
                // identity and slot remain in the snapshot, while the live
                // visible parent chain skips across the hidden window.
                Dictionary<string, DockWindowFacts> facts =
                    CaptureDockFacts(snapshot);
                facts[sourceFacts.NoteId] = sourceFacts;
                DockWindowFacts rootFacts = sourceFacts;
                DockWindowFacts capturedRoot;
                if (ordered.Count > 0 && facts.TryGetValue(ordered[0].Id,
                    out capturedRoot)) rootFacts = capturedRoot;
                sourceData.Visible = false;
                _workspace.PostHostedStickyHide(sourceData);
                LayoutDockChain(snapshot, facts,
                    rootFacts.X, rootFacts.Y, rootFacts.Width);
            }
            _workspace.Notes.SaveAsync();
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
            _workspace.RefreshMenuText();
        }





        // A Dock interaction is allowed to advance only after every expected
        // HWND has yielded exact current-generation facts.  Validate the
        // whole barrier before updating Pet-side mirrors.
        // Preview / split-restore baseline only. Live planning and final commit
        // continue to require actual, current-generation source WindowFacts.
        private Dictionary<string, DockWindowFacts> CaptureDockInteractionBaseline(
            IEnumerable<string> noteIds, DisplayTopologySnapshot topology)
        {
            Dictionary<string, DockWindowFacts> result =
                new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);
            if (noteIds == null) return result;
            foreach (string noteId in noteIds)
            {
                if (String.IsNullOrWhiteSpace(noteId)) continue;
                StickyNoteData note = _workspace.Notes.Find(noteId);
                if (note == null) continue;
                DockWindowFacts runtimeFacts = null;
                WindowFacts effective = _workspace.Placement.GetEffective(noteId);
                if (effective != null && topology != null &&
                    effective.TopologyGeneration == topology.Generation)
                    runtimeFacts = DockWindowFacts.FromWindowFacts(effective,
                        note.Visible, note.AlwaysOnTop);
                if (runtimeFacts != null) result[noteId] = runtimeFacts;
            }
            return result;
        }

        // Semantic Dock-chain order of the visible active members, filtered to
        // the exact active set. A partial group is never sent silently: the
        // Z-order command must cover the whole moving band or nothing.
        // DRT-10: the live drag is driven by the pure planner and the source
        // window's actual facts. The plan is built exactly once with one
        // capture-time topology generation and one mailbox sequence; nothing
        // downstream may re-stamp it against a later Current generation.
        // Runtime-only conversion for preview and drag-state tracking. It
        // never writes repository geometry; canonical updates come from the
        // native batch's actual facts.
        // Mouse-up is an interaction signal, never geometry authority.  A
        // distinct finalizing epoch first captures current HWND facts, then
        // replaces every pending live plan with the one final native frame.
        // P1-D: a narrow latest-wins frame for a live dock drag. A desired
        // plan only enters the mailbox; repository geometry is never written
        // before the native batch succeeds, and canonical/effective updates
        // come from the batch's actual facts in the completion callback.
        // Only same-generation, newest-sequence batch results are accepted.
        // Actual WindowFacts are the effective geometry truth; content and
        // non-geometry state come from the member snapshot.
        private DockWindowFacts GetHostedDockFacts(StickyNoteData note)
        {
            return note == null ? null : DockWindowFacts.FromWindowFacts(
                _workspace.Placement.GetEffective(note.Id), note.Visible, note.AlwaysOnTop);
        }

        internal Dictionary<string, DockWindowFacts>
            CaptureDockFacts(IEnumerable<StickyNoteData> notes)
        {
            var facts = new Dictionary<string, DockWindowFacts>(StringComparer.OrdinalIgnoreCase);
            if (notes == null) return facts;
            foreach (StickyNoteData note in notes)
            {
                DockWindowFacts actual = GetHostedDockFacts(note);
                if (actual != null) facts[note.Id] = actual;
            }
            return facts;
        }

        private List<StickyNoteData> BuildDockChainOrder(StickyNoteData seed)
        {
            return StickyDockGroups.GetVisibleGroup(_workspace.Notes.InStorageOrder, seed);
        }

        internal List<StickyNoteData> BuildDockChainOrderIncludingHidden(StickyNoteData seed)
        {
            return StickyDockGroups.GetOrderedGroup(_workspace.Notes.InStorageOrder, seed);
        }

        internal void LayoutDockChain(List<StickyNoteData> ordered,
            IDictionary<string, DockWindowFacts> factsById,
            int left, int top, int width,
            string alreadyAppliedNoteId = null)
        {
            List<DockWindowFacts> visibleFacts =
                new List<DockWindowFacts>();
            List<Size> sizes = new List<Size>();
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible) continue;
                DockWindowFacts facts;
                if (factsById == null ||
                    !factsById.TryGetValue(note.Id, out facts) || facts == null) return;
                visibleFacts.Add(facts);
                sizes.Add(new Size(facts.Width, facts.Height));
            }
            List<Rectangle> layout = CalculateUnifiedDockLayout(sizes,
                left, top, width);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            for (int index = 0; index < visibleFacts.Count; index++)
            {
                DockWindowFacts facts = visibleFacts[index];
                targets.Add(new DockLayoutTarget(facts.NoteId,
                    layout[index].Left, layout[index].Top,
                    layout[index].Width, layout[index].Height,
                    true, facts.TopMost));
            }
            ApplyDockTargets(targets, alreadyAppliedNoteId);
        }

        // Desired targets cross the STA boundary; only acknowledged actual
        // facts advance runtime geometry and its persistence mirrors.
        private void ApplyDockTargets(
            IEnumerable<DockLayoutTarget> targets,
            string alreadyAppliedNoteId)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                ApplyDockTarget(target, alreadyAppliedNoteId);
        }

        private void ApplyDockTarget(
            DockLayoutTarget target,
            string alreadyAppliedNoteId)
        {
            if (target == null) return;
            StickyNoteData note =
                _workspace.Notes.Find(target.NoteId);
            if (note == null) return;
            note.Visible = target.Visible;
            note.AlwaysOnTop = target.TopMost;
            if (String.Equals(target.NoteId,
                alreadyAppliedNoteId,
                StringComparison.OrdinalIgnoreCase))
                return;
            if (!_workspace.IsHostedSticky(note)) return;
            _workspace.PostHostedStickyCommand(
                StickyUiCommand.SetBounds(
                    target.NoteId,
                    new StickyUiBounds(
                        target.X, target.Y,
                        target.Width, target.Height)),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null &&
                        result.Status ==
                            StickyUiCommandStatus.Handled)
                        _workspace.ApplyHostedStickySnapshot(
                            result.Snapshot,
                            result.Sequence, false,
                            result.Facts, result.Topology);
                    else
                        StickyWorkspace
                            .ReportHostedStickyCommandFailure(
                                "sticky-hosted-dock-bounds",
                                result);
                });
        }

        internal static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width)
        {
            return CalculateUnifiedDockLayout(sizes, left, top, width, 1F);
        }

        private static List<Rectangle> CalculateUnifiedDockLayout(
            IList<Size> sizes, int left, int top, int width, float scale)
        {
            List<DockSize> dockSizes = new List<DockSize>();
            if (sizes != null)
            {
                foreach (Size size in sizes)
                    dockSizes.Add(new DockSize
                    {
                        Width = size.Width,
                        Height = size.Height
                    });
            }
            List<DockRect> dockLayout =
                StickyDockGeometry.CalculateUnifiedDockLayout(dockSizes,
                    left, top, width, scale);
            List<Rectangle> result = new List<Rectangle>();
            foreach (DockRect item in dockLayout)
                result.Add(new Rectangle(item.Left, item.Top,
                    item.Width, item.Height));
            return result;
        }

        private void NormalizeDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(ordered);
            DockWindowFacts root;
            if (!facts.TryGetValue(ordered[0].Id, out root)) return;
            NormalizeDockComponentAt(seed, facts,
                new Point(root.X, root.Y), root.Width);
        }

        private void NormalizeDockComponentAt(StickyNoteData seed,
            IDictionary<string, DockWindowFacts> factsById,
            Point rootAnchor, int rootWidth)
        {
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            LayoutDockChain(ordered, factsById,
                rootAnchor.X, rootAnchor.Y, rootWidth);
            ApplyDockComponentTopMost(seed,
                ordered[0].AlwaysOnTop, null);
        }

        internal void NormalizeAllDockGroups()
        {
            HashSet<string> normalized = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _workspace.Notes.GetAll())
            {
                if (!note.Visible || normalized.Contains(note.Id)) continue;
                List<StickyNoteData> ordered = BuildDockChainOrder(note);
                if (ordered.Count > 1)
                {
                    NormalizeDockComponent(note);
                    foreach (StickyNoteData member in ordered)
                        normalized.Add(member.Id);
                }
            }
            RefreshDockResizeRoles();
        }

        // One preflight for identity, topology, live membership and both facts
        // watermarks. No effect or canonical mutation occurs before it passes.
        internal void RefreshDockResizeRoles()
        {
            List<StickyNoteData> all =
                new List<StickyNoteData>(
                    _workspace.Notes.InStorageOrder);
            PublishDockScene(all);
            HashSet<string> handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in all)
            {
                if (!handled.Add(note.Id)) continue;
                if (!note.Visible)
                {
                    ApplyDockResizeRole(note, false, true, true, false, 220, 700);
                    continue;
                }
                List<StickyNoteData> ordered = StickyDockGroups.GetVisibleGroup(all, note);
                bool grouped = ordered.Count > 1;
                for (int index = 0; index < ordered.Count; index++)
                {
                    handled.Add(ordered[index].Id);
                    ApplyDockResizeRole(ordered[index], grouped, index == 0, true,
                        grouped && index < ordered.Count - 1, 220, 700);
                }
            }
        }

        private void PublishDockScene(
            IList<StickyNoteData> notes)
        {
            _workspace.Host.SetDockScene(
                BuildDockScene(notes));
        }

        private StickyDockSceneProjection BuildDockScene(
            IList<StickyNoteData> notes)
        {
            List<StickyDockSceneMember> members =
                new List<StickyDockSceneMember>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null)
                        members.Add(new StickyDockSceneMember(
                            note.Id, note.DockGroupId,
                            note.DockGroupOrder, note.Visible,
                            StickyDockCommitVersion.Compute(note)));
            return new StickyDockSceneProjection(
                members, ++_dockSceneRevision);
        }

        private void ApplyDockResizeRole(StickyNoteData note, bool grouped,
            bool resizeTop, bool resizeBottom, bool splitBottom,
            int dividerMinimumHeight, int dividerMaximumHeight)
        {
            if (note == null) return;
            if (!_workspace.IsHostedSticky(note)) return;
            StickyUiDockResizeRole hostedRole = new StickyUiDockResizeRole(
                grouped, resizeTop, resizeBottom, splitBottom,
                dividerMinimumHeight, dividerMaximumHeight);
            _workspace.PostHostedStickyCommand(StickyUiCommand.SetDockResizeRole(
                note.Id, hostedRole),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null || result.Status !=
                        StickyUiCommandStatus.Handled)
                        StickyWorkspace.ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-resize-role", result);
                });
        }

        internal static int CalculateDockDividerHeight(
            int requestedUpperHeight)
        {
            return StickyDockGeometry.CalculateDockDividerHeight(
                requestedUpperHeight);
        }

        internal static Point CalculateHeaderReachableTranslation(
            Rectangle header, Rectangle work)
        {
            DockPoint delta = StickyDockGeometry
                .CalculateHeaderReachableTranslation(
                    new DockRect
                    {
                        Left = header.Left,
                        Top = header.Top,
                        Width = header.Width,
                        Height = header.Height
                    },
                    new DockRect
                    {
                        Left = work.Left,
                        Top = work.Top,
                        Width = work.Width,
                        Height = work.Height
                    });
            return new Point(delta.X, delta.Y);
        }

        private readonly DockRestoreOperations _dockRestores = new DockRestoreOperations();

        internal void ExpandAndTileAllStickyNotesToPetScreen()
        {
            List<string> affected = new List<string>();
            foreach (StickyNoteData note in
                _workspace.Notes.GetAll())
                if (note != null) affected.Add(note.Id);
            if (affected.Count == 0)
            {
                _workspace.ShowBubble("当前没有便利贴。");
                return;
            }
            _workspace.PrepareDockStructure(affected,
                "sticky-expand-tile-structure",
                ExpandAndTileAllStickyNotesPrepared);
        }

        private void ExpandAndTileAllStickyNotesPrepared()
        {
            CancelHostedDockRestores();
            Rectangle work = Screen.FromRectangle(_workspace.PetBounds).WorkingArea;
            WindowsDisplayMetrics metrics =
                WindowsDisplayResolver.ResolvePhysicalRect(
                    _workspace.PetBounds.Left, _workspace.PetBounds.Top, _workspace.PetBounds.Right, _workspace.PetBounds.Bottom);
            double scale = metrics != null ? metrics.Scale : 1.0;
            List<DockLayoutTarget> targets =
                PrepareStickyExpandAndTileTargets(_workspace.Notes.GetAll(), work,
                    scale);
            if (targets.Count == 0)
            {
                _workspace.ShowBubble("当前没有便利贴。");
                return;
            }

            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            DisplaySurfaceSnapshot surface = topology == null || metrics == null
                ? null : topology.FindByRuntimeGdiName(metrics.DisplayId);
            // Commit the selected target directly, never a serialization mirror.
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note = _workspace.Notes.Find(target.NoteId);
                if (note != null) CommitExpandedPreferred(note, target, surface, scale);
            }
            // Queue the complete canonical snapshot before asynchronous
            // hosted effects can report their detached snapshots back.
            _workspace.Notes.SaveAsync();
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note =
                    _workspace.Notes.Find(target.NoteId);
                if (note == null) continue;
                _workspace.ShowHostedSticky(
                    note, false, false);
                ApplyDockTarget(target, null);
            }
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
            _workspace.RefreshMenuText();
            _workspace.ShowBubble("已展开并平铺 " + targets.Count +
                " 张便利贴到当前屏幕。");
        }

        private void CommitExpandedPreferred(StickyNoteData note,
            DockLayoutTarget target, DisplaySurfaceSnapshot surface, double scale)
        {
            if (note == null || surface == null) return;
            string key = DisplayTopologyRules.SelectPreferredTargetKey(
                surface, note.PreferredPlacement?.PreferredTargetKey);
            if (String.IsNullOrWhiteSpace(key)) return;
            WindowPlacementPreference preference = StickyPlacementMath.PreferenceFromPhysicalRect(
                key, surface.Bounds.Left, surface.Bounds.Top, scale,
                new PhysicalRect(target.X, target.Y, target.Width, target.Height));
            if (StickyPlacementRules.TryCommitPreferred(note, preference, PlacementReason.ExpandAndTile))
                _workspace.Placement.MarkUserPlacementCommit(note.Id);
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

        internal void ReconcileDockGroups(DisplayTopologySnapshot snapshot,
            WindowFacts petFacts)
        {
            HashSet<string> visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _workspace.Notes.GetAll())
            {
                if (note == null || !note.Visible || !_workspace.IsHostedSticky(note) ||
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
                        !_workspace.IsHostedSticky(member);
                });
                if (group.Count < 2) continue;
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
                return _workspace.Placement.IsTemporaryRehome(member.Id);
            });
            if (preferred != null)
            {
                if (temporary)
                {
                    if (group.Exists(delegate(StickyNoteData member)
                    {
                        return _workspace.Placement.UserMovedSinceRehome(member.Id);
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
                    root.PreferredPlacement?.PreferredTargetKey,
                    _workspace.Placement.GetEffective(root.Id) == null
                        ? new PhysicalRect() : _workspace.Placement.GetEffective(root.Id).PhysicalBounds,
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
                    local = member.PreferredPlacement?.LocalLogicalRect ?? new LogicalRect();
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
                _workspace.Placement))
            {
                DisplayDiagnostics.Trace("DockTopologyGeometryRejected",
                    "group=" + groupId + " generation=" + snapshot.Generation +
                    " reason=" + reason);
                return;
            }
            bool centerInWorkArea = reason == DockTopologyReprojectReason.TemporaryRehome;
            DockGroupReprojectPlan plan = new DockGroupReprojectPlan(
                snapshot.Generation, NextDockOperationSequence(),
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
            _workspace.PostHostedStickyCommand(StickyUiCommand.ReprojectDockGroup(
                plan, snapshot), delegate(StickyUiCommandResult result)
                {
                    try
                    {
                        if (_dockRestores.ContainsGroup(groupId) ||
                            !TryApplyDockTopologyResult(result, snapshot,
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
                                _workspace.Placement.MarkTemporaryRehome(noteId,
                                    "dock-preferred-display-missing");
                            DisplayDiagnostics.Trace("TemporaryRehome",
                                "dockGroup=" + groupId + " members=" + expectedIds.Count +
                                " target=" + targetSurface.RuntimeSurfaceId);
                            return;
                        }
                        if (reason == DockTopologyReprojectReason.PreferredReturn)
                        {
                            foreach (string noteId in expectedIds)
                                _workspace.Placement.MarkReturnedToPreferred(
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
                            _workspace.CurrentTopologySnapshot();
                        StickyNoteData currentRoot = _workspace.Notes.Find(root.Id);
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
                                        !_workspace.IsHostedSticky(member);
                                });
                            ReconcileDockGroup(currentGroup, current,
                                _workspace.CapturePetWindowFacts(current));
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
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.DockBatchResult == null || targetSurface == null || expectedIds == null ||
                !_workspace.IsTopologyCurrent(snapshot)) return false;
            DockBatchResult batch = result.DockBatchResult;
            if (batch.PlanSequence != expectedPlanSequence ||
                batch.TopologyGeneration != snapshot.Generation ||
                !String.Equals(batch.TargetSurfaceId, targetSurface.RuntimeSurfaceId,
                    StringComparison.OrdinalIgnoreCase) || batch.TargetDpi <= 0 ||
                batch.Members.Count != expectedIds.Count) return false;
            var remaining = new HashSet<string>(expectedIds, StringComparer.OrdinalIgnoreCase);
            if (remaining.Count != expectedIds.Count || remaining.Count == 0) return false;
            var updates = new List<StickyFactsReceiver.Update>(batch.Members.Count);
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyFactsReceiver.Update update;
                if (member == null || member.Snapshot == null || !remaining.Remove(member.NoteId) ||
                    !_workspace.Facts.TryPrepare(member, snapshot, out update, acceptCreatedSessions) ||
                    member.Facts.Dpi != batch.TargetDpi ||
                    !String.Equals(member.Facts.RuntimeGdiName, targetSurface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase)) return false;
                updates.Add(update);
            }
            foreach (StickyFactsReceiver.Update update in updates) update.Commit(forceVisible);
            if (persist) _workspace.Notes.SaveAsync();
            return true;
        }

        // DRT-9/10 durable dock commit continuation: after mouse-up the
        // capture ran on the Sticky STA; every member's preferred placement
        // is derived from the captured actual facts plus the finalizing
        // epoch's exact topology, then membership and content are persisted
        // once. The commit uses its captured generation throughout.
        private static void TraceDockCommitRejected(string reason)
        {
            DisplayDiagnostics.Trace("DockCommitRejected",
                reason ?? String.Empty);
        }

        private sealed class DockCommitCandidate
        {
            internal DockCommitCandidate(StickyFactsReceiver.Update update,
                WindowPlacementPreference preference)
            {
                Update = update;
                Preference = preference;
            }
            internal StickyFactsReceiver.Update Update { get; private set; }
            internal WindowPlacementPreference Preference { get; private set; }
        }

        internal void CommitLocalDockGesture(
            StickyDockGestureCommit commit)
        {
            if (commit == null) return;

            bool accepted;
            if (_acceptedLocalDockGestures.Contains(
                commit.GestureId))
                accepted = true;
            else
            {
                accepted =
                    TryApplyLocalDockGestureCommit(commit);
                if (accepted)
                {
                    _acceptedLocalDockGestures.Add(
                        commit.GestureId);
                    _acceptedLocalDockGestureOrder.Enqueue(
                        commit.GestureId);
                    while (_acceptedLocalDockGestureOrder.Count > 32)
                        _acceptedLocalDockGestures.Remove(
                            _acceptedLocalDockGestureOrder.Dequeue());
                }
            }

            StickyDockSceneProjection scene =
                BuildDockScene(new List<StickyNoteData>(
                    _workspace.Notes.InStorageOrder));
            StickyDockCommitAck ack =
                new StickyDockCommitAck(
                    commit.GestureId, accepted, scene,
                    accepted ? null :
                        BuildLocalDockCorrections(commit));
            _workspace.PostHostedStickyCommand(
                StickyUiCommand.AcknowledgeDockCommit(ack),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null ||
                        result.Status !=
                            StickyUiCommandStatus.Handled)
                        StickyWorkspace
                            .ReportHostedStickyCommandFailure(
                                "sticky-dock-commit-ack",
                                result);
                });
        }

        private IReadOnlyList<DockWindowTarget>
            BuildLocalDockCorrections(
                StickyDockGestureCommit commit)
        {
            List<DockWindowTarget> targets =
                new List<DockWindowTarget>();
            if (commit == null) return targets.AsReadOnly();
            foreach (string noteId in
                commit.BaselineVersions.Keys)
            {
                WindowFacts facts =
                    _workspace.Placement.GetEffective(noteId);
                if (facts != null &&
                    facts.PhysicalBounds.IsValid)
                    targets.Add(new DockWindowTarget(
                        noteId, facts.PhysicalBounds));
            }
            return targets.AsReadOnly();
        }

        private bool TryApplyLocalDockGestureCommit(
            StickyDockGestureCommit commit)
        {
            DisplayTopologySnapshot topology =
                _workspace.CurrentTopologySnapshot();
            if (topology == null ||
                topology.Generation !=
                    commit.TopologyGeneration)
            {
                TraceDockCommitRejected(
                    "local commit topology changed");
                return false;
            }

            foreach (KeyValuePair<string, long> baseline
                in commit.BaselineVersions)
            {
                StickyNoteData note =
                    _workspace.Notes.Find(baseline.Key);
                if (note == null ||
                    StickyDockCommitVersion.Compute(note) !=
                        baseline.Value)
                {
                    TraceDockCommitRejected(
                        "local commit model version changed");
                    return false;
                }
            }

            HashSet<string> expected =
                new HashSet<string>(
                    commit.BaselineVersions.Keys,
                    StringComparer.OrdinalIgnoreCase);
            HashSet<string> actual =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            List<DockCommitCandidate> candidates =
                new List<DockCommitCandidate>();
            foreach (DockBatchMemberResult member
                in commit.Members)
            {
                StickyFactsReceiver.Update update;
                WindowPlacementPreference preference;
                if (member == null ||
                    member.Snapshot == null ||
                    member.Facts == null ||
                    !expected.Contains(member.NoteId) ||
                    !actual.Add(member.NoteId) ||
                    member.Facts.TopologyGeneration !=
                        topology.Generation ||
                    !_workspace.Facts.TryPrepare(
                        member, topology, out update) ||
                    update.Canonical == null ||
                    !StickyPlacementRules
                        .TryBuildPreferredPlacement(
                            member.Facts, topology,
                            update.Canonical
                                .PreferredPlacement
                                ?.PreferredTargetKey,
                            out preference))
                {
                    TraceDockCommitRejected(
                        "local commit facts rejected");
                    return false;
                }
                candidates.Add(
                    new DockCommitCandidate(
                        update, preference));
            }
            if (actual.Count == 0 ||
                !actual.Contains(commit.SourceNoteId))
            {
                TraceDockCommitRejected(
                    "local commit source missing");
                return false;
            }

            StickyNoteData source =
                _workspace.Notes.Find(
                    commit.SourceNoteId);
            if (source == null || !source.Visible)
                return false;

            DockMergePlan merge = null;
            if (commit.Intent ==
                StickyDockCommitIntent.MergeAfter)
            {
                StickyNoteData target =
                    _workspace.Notes.Find(
                        commit.TargetNoteId);
                if (target == null || !target.Visible ||
                    String.Equals(target.Id, source.Id,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                merge = StickyDockOperations
                    .PrepareMergeAfterParent(
                        BuildDockChainOrderIncludingHidden(
                            target),
                        target,
                        BuildDockChainOrderIncludingHidden(
                            source));
                List<StickyNoteData> resolved;
                if (merge == null ||
                    !merge.TryResolve(
                        _workspace.Notes.InStorageOrder,
                        out resolved))
                    return false;
            }
            else if (commit.Intent ==
                StickyDockCommitIntent.Detach)
            {
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(
                        source);
                int sourceIndex = group.FindIndex(
                    note => note != null &&
                        String.Equals(note.Id, source.Id,
                            StringComparison
                                .OrdinalIgnoreCase));
                if (sourceIndex <= 0)
                    return false;
            }

            // Every fallible preflight is complete. Relation and geometry now
            // commit in one Pet turn; disk persistence is queued afterwards.
            if (merge != null &&
                !merge.TryCommit(
                    _workspace.Notes.InStorageOrder))
                return false;
            if (commit.Intent ==
                StickyDockCommitIntent.Detach)
                StickyDockOperations.ExtractSingleDockMember(
                    BuildDockChainOrderIncludingHidden(
                        source), source);

            foreach (DockCommitCandidate candidate
                in candidates)
            {
                candidate.Update.Commit();
                StickyPlacementRules.TryCommitPreferred(
                    candidate.Update.Canonical,
                    candidate.Preference,
                    commit.Intent ==
                            StickyDockCommitIntent
                                .HorizontalResize ||
                        commit.Intent ==
                            StickyDockCommitIntent
                                .DividerResize
                        ? PlacementReason.UserResizeCommit
                        : PlacementReason.DockCommit);
                _workspace.Placement
                    .MarkUserPlacementCommit(
                        candidate.Update.Member.NoteId);
            }

            if (merge != null)
            {
                List<StickyNoteData> group =
                    BuildDockChainOrderIncludingHidden(
                        source);
                if (group.Count > 0)
                {
                    bool topMost =
                        group[0].AlwaysOnTop;
                    foreach (StickyNoteData member
                        in group)
                        member.AlwaysOnTop = topMost;
                    ApplyDockComponentTopMost(
                        source, topMost, null);
                }
            }

            _workspace.Notes.SaveAsync();
            _workspace.RefreshMenuText();
            RefreshDockResizeRoles();
            return true;
        }

        // User mutations wait for the final commit that owns their captured
        // member/group scope. Header, horizontal and divider use one policy.
        internal bool TryRestoreHostedDockComponent(
            List<StickyNoteData> ordered, StickyNoteData focus,
            bool focusEditor, bool persistVisibility)
        {
            if (ordered == null || ordered.Count < 2) return false;
            string rootId = ordered[0].Id;
            string focusId = focus == null ? null : focus.Id;
            List<string> affected = new List<string>();
            foreach (StickyNoteData member in ordered)
                if (member != null) affected.Add(member.Id);
            _workspace.PrepareDockStructure(affected,
                "sticky-restore-structure",
                delegate
                {
                    StickyNoteData root =
                        _workspace.Notes.Find(rootId);
                    if (root == null) return;
                    TryRestoreHostedDockComponentPrepared(
                        BuildDockChainOrderIncludingHidden(root),
                        _workspace.Notes.Find(focusId),
                        focusEditor, persistVisibility);
                });
            return true;
        }

        private bool TryRestoreHostedDockComponentPrepared(
            List<StickyNoteData> ordered, StickyNoteData focus,
            bool focusEditor, bool persistVisibility)
        {
            if (ordered == null || ordered.Count < 2) return false;
            if (_dockRestores.ContainsGroup(ordered[0].DockGroupId)) return true;
            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            if (topology == null) return false;
            foreach (StickyNoteData member in ordered)
            if (MigrateDockRestorePreferredIfNeeded(ordered, topology)) _workspace.Notes.SaveAsync();
            DockRestoreOperation operation = DockRestoreOperation.TryCreate(ordered,
                focus == null ? null : focus.Id, focusEditor, persistVisibility,
                topology, _workspace.CapturePetWindowFacts(topology), NextDockOperationSequence());
            if (!_dockRestores.TryBegin(operation)) return false;
            try
            {
                _workspace.PostHostedStickyCommand(StickyUiCommand.RestoreDockGroup(operation, _workspace.ReminderItems),
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
                if (StickyPlacementRules.MigrateV10Preferred(member, topology)) changed = true;
            }
            return changed;
        }

        private void CompleteHostedDockRestore(DockRestoreOperation operation, StickyUiCommandResult result)
        {
            if (!_dockRestores.IsCurrent(operation)) return;
            if (!operation.MatchesMembers(BuildDockChainOrderIncludingHidden(_workspace.Notes.Find(operation.MemberIds[0]))))
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
                StickyWorkspace.ReportHostedStickyCommandFailure("sticky-hosted-dock-restore", result);
                _workspace.ShowBubble("Dock 便利贴组恢复未完成，未展开的便利贴仍保留在侧边页签中。");
                return;
            }

            bool topMost = _workspace.Notes.Find(operation.MemberIds[0]).AlwaysOnTop;
            foreach (string noteId in operation.MemberIds)
            {
                _workspace.Notes.Find(noteId).AlwaysOnTop = topMost;
                if (operation.Reason == DockTopologyReprojectReason.TemporaryRehome)
                    _workspace.Placement.MarkTemporaryRehome(noteId, "dock-preferred-display-missing");
                else _workspace.Placement.ClearTemporaryRehome(noteId);
            }
            if (operation.PersistVisibility) _workspace.Notes.SaveAsync();
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
            _workspace.RefreshMenuText();
            DisplayDiagnostics.Trace("DockRestoreCompleted", "group=" + operation.GroupId +
                " generation=" + operation.Topology.Generation + " members=" + operation.MemberIds.Count);
            // Focus is a post-commit interaction. Its failure cannot undo placement.
            if (operation.FocusEditor)
            {
                try
                {
                    _workspace.PostHostedStickyCommand(StickyUiCommand.FocusPrimaryInput(operation.FocusId),
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
                    _workspace.PostHostedStickyCommand(StickyUiCommand.Hide(snapshot.NoteId), ignored => { });
        }

        private void CancelHostedDockRestore(DockRestoreOperation operation)
        {
            if (_dockRestores.Finish(operation) && !_workspace.IsDisposed)
                HideUncommittedDockRestore(operation);
        }

        internal void CancelHostedDockRestores(string noteId = null)
        {
            foreach (DockRestoreOperation operation in _dockRestores.Snapshot())
                if (noteId == null || operation.ContainsMember(noteId)) CancelHostedDockRestore(operation);
        }

        internal void RestartHostedDockRestores(DisplayTopologySnapshot topology)
        {
            foreach (DockRestoreOperation operation in _dockRestores.Snapshot())
            {
                if (operation.Topology.Generation == topology.Generation) continue;
                CancelHostedDockRestore(operation);
                List<StickyNoteData> group = BuildDockChainOrderIncludingHidden(_workspace.Notes.Find(operation.MemberIds[0]));
                if (group.Count >= 2)
                    TryRestoreHostedDockComponent(group, _workspace.Notes.Find(operation.FocusId),
                        operation.FocusEditor, operation.PersistVisibility);
            }
        }
    }
}
