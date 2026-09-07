using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PennyPet
{
    // HIGH RISK: persisted Dock group snapshots, parent-child links, drag
    // splitting, insertion and layout synchronization. Keep these algorithms
    // behavior-identical and run full Dock regression tests after changes.
    internal sealed partial class PetForm
    {
        private void ApplyDockComponentTopMost(StickyNoteData seed,
            bool alwaysOnTop, string alreadyAppliedNoteId)
        {
            List<StickyNoteData> component =
                BuildDockChainOrderIncludingHidden(seed);
            if (component.Count == 0) component = BuildDockComponent(seed);
            foreach (StickyNoteData note in component)
            {
                note.AlwaysOnTop = alwaysOnTop;
                if (String.Equals(note.Id, alreadyAppliedNoteId,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsHostedSticky(note)) continue;
                PostHostedStickyCommand(StickyUiCommand.SetTopMost(
                    note.Id, alwaysOnTop),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled)
                            ApplyHostedStickySnapshot(result.Snapshot,
                                result.Sequence, false);
                        else ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-topmost", result);
                    });
            }
        }

        private void CloseStickyDockNote(StickyNoteData sourceData,
            DockWindowFacts sourceFacts)
        {
            if (sourceData == null || sourceFacts == null) return;
            List<StickyNoteData> ordered =
                BuildAuthoritativeVisibleDockOrder(sourceData);
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(sourceData);
            snapshot = StickyDockOperations.SelectMoreCompleteDockOrder(
                ordered, snapshot);
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
                StickyDockGroups.ApplyGroupSnapshot(snapshot);
                foreach (StickyNoteData note in snapshot)
                {
                    note.Visible = false;
                    PostHostedStickyHide(note);
                }
                StickyDockGroups.RebuildVisibleParentChain(snapshot);
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
                PostHostedStickyHide(sourceData);
                StickyDockOperations.PreserveDockSlotForHiddenMember(
                    snapshot, sourceData);
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(snapshot, facts, rootFacts.X,
                        rootFacts.Y, rootFacts.Width);
                }
                finally { _synchronizingDockLayout = false; }
            }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
            RefreshMenuText();
        }

        private sealed class DockFactsBarrierCandidate
        {
            internal DockFactsBarrierCandidate(DockBatchMemberResult member,
                StickyNoteData canonical, DockWindowFacts runtimeFacts)
            {
                Member = member;
                Canonical = canonical;
                RuntimeFacts = runtimeFacts;
            }
            internal DockBatchMemberResult Member { get; private set; }
            internal StickyNoteData Canonical { get; private set; }
            internal DockWindowFacts RuntimeFacts { get; private set; }
        }

        // A Dock interaction is allowed to advance only after every expected
        // HWND has yielded exact current-generation facts.  Validate the
        // whole barrier before updating Pet-side mirrors.
        private bool TryApplyDockFactsBarrier(StickyUiCommandResult result,
            IList<string> expectedIds, DisplayTopologySnapshot topology,
            long epoch, string sourceNoteId, bool resetOriginalFacts,
            out WindowFacts sourceFacts)
        {
            sourceFacts = null;
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.DockBatchResult == null || expectedIds == null ||
                expectedIds.Count == 0 || topology == null || epoch <= 0)
                return false;
            DisplayTopologySnapshot current = CurrentTopologySnapshot();
            if (current == null || current.Generation != topology.Generation)
                return false;
            DockBatchResult batch = result.DockBatchResult;
            if (batch.InteractionEpoch != epoch || batch.PlanSequence != 0 ||
                batch.TopologyGeneration != topology.Generation ||
                batch.Members.Count != expectedIds.Count) return false;
            HashSet<string> expected = new HashSet<string>(expectedIds,
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> actual = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            List<DockFactsBarrierCandidate> candidates =
                new List<DockFactsBarrierCandidate>();
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyNoteData canonical = member == null ? null :
                    _notes.Find(member.NoteId);
                if (member == null || member.Snapshot == null ||
                    member.Facts == null || canonical == null ||
                    !expected.Contains(member.NoteId) || !actual.Add(member.NoteId) ||
                    member.WindowSequence != member.Facts.WindowSequence ||
                    member.Facts.TopologyGeneration != topology.Generation ||
                    !String.Equals(member.NoteId, member.Facts.WindowId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !_hostedRuntime.CanApplySequence(member.NoteId,
                        member.WindowSequence)) return false;
                DockWindowFacts runtimeFacts = DockWindowFacts.FromWindowFacts(
                    member.Facts, member.Snapshot.Visible,
                    member.Snapshot.AlwaysOnTop);
                if (runtimeFacts == null) return false;
                if (String.Equals(member.NoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) sourceFacts = member.Facts;
                candidates.Add(new DockFactsBarrierCandidate(member, canonical,
                    runtimeFacts));
            }
            if (actual.Count != expected.Count || sourceFacts == null) return false;
            if (resetOriginalFacts)
            {
                _activeDockOriginalFacts.Clear();
                _activeDockCurrentFacts.Clear();
            }
            foreach (DockFactsBarrierCandidate candidate in candidates)
            {
                DockBatchMemberResult member = candidate.Member;
                member.Snapshot.ApplyContentTo(candidate.Canonical);
                candidate.Canonical.Visible = member.Snapshot.Visible;
                candidate.Canonical.AlwaysOnTop = member.Snapshot.AlwaysOnTop;
                ApplyHostedStickyFactsGeometry(candidate.Canonical, member.Facts,
                    topology);
                if (!_placementRuntime.TryUpdateEffective(member.NoteId,
                    member.Facts)) return false;
                _hostedRuntime.RecordSequence(member.NoteId,
                    member.WindowSequence);
                _activeDockCurrentFacts[member.NoteId] = candidate.RuntimeFacts;
                if (resetOriginalFacts)
                    _activeDockOriginalFacts[member.NoteId] =
                        candidate.RuntimeFacts;
            }
            return true;
        }

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
                StickyNoteData note = _notes.Find(noteId);
                if (note == null) continue;
                DockWindowFacts runtimeFacts = null;
                WindowFacts effective = _placementRuntime.GetEffective(noteId);
                if (effective != null && topology != null &&
                    effective.TopologyGeneration == topology.Generation)
                    runtimeFacts = DockWindowFacts.FromWindowFacts(effective,
                        note.Visible, note.AlwaysOnTop);
                if (runtimeFacts == null) runtimeFacts = DockWindowFacts.FromData(note);
                if (runtimeFacts != null) result[noteId] = runtimeFacts;
            }
            return result;
        }

        // Semantic Dock-chain order of the visible active members, filtered to
        // the exact active set. A partial group is never sent silently: the
        // Z-order command must cover the whole moving band or nothing.
        private string[] BuildActiveDockZOrderIds(StickyNoteData seed)
        {
            if (seed == null || _activeDockGroupIds.Count < 2)
                return new string[0];

            HashSet<string> active = new HashSet<string>(
                _activeDockGroupIds, StringComparer.OrdinalIgnoreCase);
            List<StickyNoteData> ordered =
                BuildAuthoritativeVisibleDockOrder(seed);
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible ||
                    !active.Contains(note.Id) || !seen.Add(note.Id))
                    continue;
                result.Add(note.Id);
            }

            if (result.Count != active.Count)
            {
                DisplayDiagnostics.Trace("DockZOrderRaiseRejected",
                    "reason=pet-order-mismatch source=" +
                    (seed.Id ?? String.Empty) +
                    " active=" + active.Count +
                    " ordered=" + result.Count);
                return new string[0];
            }
            return result.ToArray();
        }

        private void BeginStickyDockDrag(DockWindowFacts facts,
            WindowFacts sourceFacts, DisplayTopologySnapshot topology)
        {
            if (facts == null || sourceFacts == null || topology == null ||
                !DockExecutionRules.IsSameGeneration(sourceFacts, topology)) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            long epoch = _dockInteraction.BeginPreparing(facts.NoteId,
                topology.Generation);
            if (epoch == 0) return;
            _stickyUiHost.SetCurrentDockInteractionEpoch(epoch);
            ClearDockPreview();
            ClearSplitGuide();
            _activeNoteDragId = facts.NoteId;
            // Original gesture position/time: written once until reset.
            _activeNoteDragStartFacts = facts;
            _activeNoteDragLastFacts = facts;
            _activeNoteDragStartedUtc = DateTime.UtcNow;
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderNoteId = null;
            lock (_dockPlanMailbox.Gate)
            {
                _dockPlanMailbox.Current = null;
                _dockPlanMailbox.ApplyQueued = false;
                _dockPlanMailbox.FinalPlanSequence = 0;
            }
            SetActiveDockGroup(BuildDockComponent(seed));
            if (_activeDockGroupIds.Count == 0)
            {
                ResetDockDragState(true);
                return;
            }
            _activeDockOriginalFacts.Clear();
            _activeDockCurrentFacts.Clear();
            Dictionary<string, DockWindowFacts> groupFacts =
                CaptureDockInteractionBaseline(_activeDockGroupIds, topology);
            groupFacts[facts.NoteId] = facts;
            foreach (KeyValuePair<string, DockWindowFacts> item in groupFacts)
            {
                _activeDockOriginalFacts[item.Key] = item.Value;
                _activeDockCurrentFacts[item.Key] = item.Value;
            }
            // The root header is the one unambiguous handle for moving the
            // whole stack.  Only a member that has a parent can be pulled out
            // after a deliberate hold.
            _activeNoteSplitEligible = StickyDockOperations
                .IsDockSplitEligible(seed.DockParentId,
                    _activeDockGroupIds.Count);
            if (_activeNoteSplitEligible)
                ShowSplitGuide(seed, groupFacts);
            // One drag-start Z-order transaction: restore the contiguous
            // moving-group band before the live geometry drag is armed. Only
            // this single request may reorder Z; live batches stay SWP_NOZORDER.
            string[] zOrderIds = BuildActiveDockZOrderIds(seed);
            if (zOrderIds.Length > 1)
            {
                PostHostedStickyCommand(
                    StickyUiCommand.RaiseDockGroupForDrag(zOrderIds,
                        facts.NoteId, topology, epoch),
                    delegate(StickyUiCommandResult result)
                    {
                        if (result != null && result.Status ==
                            StickyUiCommandStatus.Handled) return;
                        // Z-order failure must not corrupt or cancel the
                        // geometry drag; it stays a visible manual-test
                        // failure diagnosed separately from placement.
                        DisplayDiagnostics.Trace("DockZOrderRaiseRejected",
                            "reason=host-result source=" + facts.NoteId +
                            " generation=" + topology.Generation +
                            " epoch=" + epoch);
                    });
            }
            // Arm the first move in this callback; the source HWND is already
            // following the user and cannot wait for a follower capture.
            if (!_dockInteraction.TryEnterDragging(epoch, topology.Generation))
            {
                ResetDockDragState(true);
                return;
            }
            DisplayDiagnostics.Trace("DockDragReady",
                "source=" + facts.NoteId + " epoch=" + epoch +
                " generation=" + topology.Generation + " members=" +
                _activeDockGroupIds.Count + " splitEligible=" + _activeNoteSplitEligible);
        }

        private void MoveStickyDockDrag(DockWindowFacts facts,
            WindowFacts sourceFacts, DisplayTopologySnapshot topology)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            if (_movingDockGroup ||
                !String.Equals(facts.NoteId, _activeNoteDragId,
                    StringComparison.OrdinalIgnoreCase)) return;
            if (topology == null ||
                !_dockInteraction.CanPlan(facts.NoteId, topology.Generation) ||
                !DockExecutionRules.IsSameGeneration(sourceFacts, topology)) return;
            int dx = facts.X - _activeNoteDragLastFacts.X;
            int dy = facts.Y - _activeNoteDragLastFacts.Y;
            if (dx == 0 && dy == 0) return;

            if (!_activeNoteDetached && _activeNoteSplitEligible)
            {
                TimeSpan held = DateTime.UtcNow - _activeNoteDragStartedUtc;
                int totalDx = facts.X - _activeNoteDragStartFacts.X;
                int totalDy = facts.Y - _activeNoteDragStartFacts.Y;
                if (StickyDockOperations.CancelsDockSplitHold(
                    held.TotalMilliseconds, totalDx, totalDy))
                {
                    _activeNoteSplitEligible = false;
                    ClearSplitGuide();
                }
                else if (held.TotalMilliseconds >=
                    StickyDockOperations.SplitHoldMilliseconds)
                {
                    if (!String.IsNullOrEmpty(seed.DockParentId))
                    {
                        string connectedNoteId = seed.DockParentId;
                        List<StickyNoteData> beforeSplit =
                            BuildDockChainOrderIncludingHidden(seed);
                        int splitIndex = beforeSplit.FindIndex(
                            delegate(StickyNoteData note)
                            {
                                return String.Equals(note.Id, facts.NoteId,
                                    StringComparison.OrdinalIgnoreCase);
                            });
                        if (splitIndex > 0)
                            StickyDockOperations.ExtractSingleDockMember(
                                beforeSplit, seed);
                        else StickyDockGroups.ClearMembership(seed);
                        _splitRemainderNoteId = connectedNoteId;
                        _activeNoteDetached = true;
                    }
                    if (_activeNoteDetached)
                    {
                        ClearSplitGuide();
                        RestoreDockOriginalLocations(_splitRemainderNoteId);
                        StickyNoteData remainder =
                            _notes.Find(_splitRemainderNoteId);
                        if (remainder != null)
                            NormalizeDockComponent(remainder);
                        SetActiveDockGroup(BuildDockComponent(seed));
                        RefreshDockResizeRoles();
                    }
                }
            }

            DockPlacementPlan livePlan = PlanLiveDockPlan(seed, sourceFacts,
                topology);
            if (livePlan != null)
            {
                _movingDockGroup = true;
                try { ApplyLiveDockPlan(livePlan); }
                finally { _movingDockGroup = false; }
                RememberActiveDockFacts(PlanToDockTargets(livePlan));
            }
            _activeNoteDragLastFacts = facts;
            if (!_activeNoteDetached && _activeNoteSplitEligible)
                UpdateSplitGuide(seed, _activeDockCurrentFacts);
            Dictionary<string, DockWindowFacts> previewFacts =
                CaptureDockFacts(_notes.GetAll());
            previewFacts[facts.NoteId] = facts;
            UpdateDockPreview(seed, previewFacts);
        }

        // DRT-10: the live drag is driven by the pure planner and the source
        // window's actual facts. Followers never choose a target display;
        // when the source crosses a DPI boundary the next plan naturally
        // re-scales the whole group to the new surface. A stale generation
        // or missing facts drops this frame instead of chasing old coordinates.
        // DRT-10: the live drag is driven by the pure planner and the source
        // window's actual facts. The plan is built exactly once with one
        // capture-time topology generation and one mailbox sequence; nothing
        // downstream may re-stamp it against a later Current generation.
        private DockPlacementPlan PlanLiveDockPlan(
            StickyNoteData seed, WindowFacts sourceFacts,
            DisplayTopologySnapshot topology)
        {
            if (seed == null || sourceFacts == null || topology == null ||
                !_dockInteraction.CanPlan(sourceFacts.WindowId,
                    topology.Generation)) return null;
            return PlanDockPlan(seed, sourceFacts, topology,
                _dockInteraction.Epoch);
        }

        private DockPlacementPlan PlanDockPlan(StickyNoteData seed,
            WindowFacts sourceFacts, DisplayTopologySnapshot topology,
            long interactionEpoch = 0)
        {
            if (seed == null || sourceFacts == null || topology == null)
                return null;
            if (!DockExecutionRules.IsSameGeneration(sourceFacts, topology))
                return null;
            DisplaySurfaceSnapshot surface =
                topology.FindByRuntimeGdiName(sourceFacts.RuntimeGdiName);
            if (surface == null)
                surface = topology.FindByTargetKey(
                    sourceFacts.ActiveTargetKey);
            if (surface == null) return null;

            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            int sourceIndex = ordered.FindIndex(delegate(StickyNoteData note)
            {
                return String.Equals(note.Id, sourceFacts.WindowId,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (sourceIndex < 0) return null;

            // Dock has one width. Derive it from the source HWND facts so a
            // stale or previously mixed-DPI member width cannot fracture the
            // group or become a second geometry authority.
            int unifiedLogicalWidth =
                DisplayGeometry.PhysicalLengthToLogical(
                    sourceFacts.PhysicalBounds.Width, sourceFacts.Scale);
            if (unifiedLogicalWidth <= 0) return null;

            List<DockLogicalMember> members =
                new List<DockLogicalMember>(ordered.Count);
            foreach (StickyNoteData member in ordered)
            {
                if (member.LocalLogicalWidth <= 0 ||
                    member.LocalLogicalHeight <= 0) return null;
                members.Add(new DockLogicalMember(member.Id,
                    unifiedLogicalWidth,
                    member.LocalLogicalHeight));
            }

            LogicalPoint sourceLocal = DisplayGeometry.PhysicalToLocal(
                sourceFacts.PhysicalBounds.Left,
                sourceFacts.PhysicalBounds.Top,
                surface.Bounds.Left, surface.Bounds.Top, sourceFacts.Scale);
            int rootX = sourceLocal.X;
            int rootY = sourceLocal.Y;
            for (int index = 0; index < sourceIndex; index++)
                rootY -= ordered[index].LocalLogicalHeight;
            DockGroupLogicalState group = new DockGroupLogicalState(
                new LogicalPoint { X = rootX, Y = rootY }, members);

            DockPlacementPlan plan;
            try
            {
                plan = DockPlacementPlanner.Plan(group, sourceFacts,
                    surface, sourceFacts.Dpi,
                    sourceFacts.TopologyGeneration,
                    _dockPlanMailbox.NextSequence(), interactionEpoch);
            }
            catch (ArgumentException)
            {
                DisplayDiagnostics.Trace("DockPlanCreated",
                    "stale frame note=" + sourceFacts.WindowId +
                    " generation=" + sourceFacts.TopologyGeneration);
                return null;
            }
            return plan;
        }

        // Runtime-only conversion for preview and drag-state tracking. It
        // never writes repository geometry; canonical updates come from the
        // native batch's actual facts.
        private List<DockLayoutTarget> PlanToDockTargets(
            DockPlacementPlan plan)
        {
            List<DockLayoutTarget> targets =
                new List<DockLayoutTarget>();
            foreach (DockWindowTarget target in plan.WindowTargets)
            {
                StickyNoteData member = _notes.Find(target.NoteId);
                if (member == null) continue;
                targets.Add(new DockLayoutTarget(target.NoteId,
                    target.PhysicalBounds.Left, target.PhysicalBounds.Top,
                    target.PhysicalBounds.Width,
                    target.PhysicalBounds.Height,
                    member.Visible, member.AlwaysOnTop));
            }
            return targets;
        }

        private void CompleteStickyDockDrag(DockWindowFacts facts,
            StickyUiEvent value)
        {
            if (facts == null) return;
            StickyNoteData seed = _notes.Find(facts.NoteId);
            if (seed == null) return;
            if (!String.Equals(facts.NoteId, _activeNoteDragId,
                StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, DockWindowFacts> currentFacts =
                CaptureDockFacts(_notes.GetAll());
            currentFacts[facts.NoteId] = facts;
            DockTarget target = FindDockTarget(seed, currentFacts);
            if (target != null &&
                !String.IsNullOrEmpty(target.ParentNoteId))
            {
                StickyNoteData parent = _notes.Find(target.ParentNoteId);
                if (parent == null) target = null;
            }
            if (target != null &&
                !String.IsNullOrEmpty(target.ParentNoteId))
            {
                StickyNoteData parent = _notes.Find(target.ParentNoteId);
                List<StickyNoteData> targetSnapshot =
                    BuildDockChainOrderIncludingHidden(parent);
                List<StickyNoteData> sourceSnapshot =
                    BuildDockChainOrderIncludingHidden(seed);
                StickyNoteData tail = FindActiveDockTail(seed);
                StickyNoteData tailData = tail ?? seed;
                List<StickyNoteData> mergedSnapshot =
                    StickyDockOperations.MergeDockSnapshotsAfterParent(
                        targetSnapshot, parent, sourceSnapshot);
                bool groupTopMost = targetSnapshot.Count == 0
                    ? parent.AlwaysOnTop : mergedSnapshot[0].AlwaysOnTop;
                ApplyDockComponentTopMost(parent, groupTopMost, null);
                StickyNoteData existingChild =
                    _notes.Find(target.ExistingChildNoteId);
                if (existingChild != null)
                    ShowTransientDockPulse(CalculateDockVisualSeamPhysical(
                        DockWindowFacts.FromData(tailData)),
                        Color.FromArgb(32, 160, 255));
                ShowTransientDockPulse(CalculateDockVisualSeamPhysical(
                    DockWindowFacts.FromData(parent)),
                    Color.FromArgb(32, 160, 255));
            }
            ClearDockPreview();
            ClearSplitGuide();
            RefreshDockResizeRoles();
            StickyNoteData remainderSeed =
                _notes.Find(_splitRemainderNoteId);
            StartDockFinalization(seed, remainderSeed);
        }

        // Mouse-up is an interaction signal, never geometry authority.  A
        // distinct finalizing epoch first captures current HWND facts, then
        // replaces every pending live plan with the one final native frame.
        private void StartDockFinalization(StickyNoteData seed,
            StickyNoteData remainderSeed)
        {
            DisplayTopologySnapshot topology = CurrentTopologySnapshot();
            if (seed == null || topology == null) { ResetDockDragState(true); return; }
            long epoch = _dockInteraction.BeginFinalizing(topology.Generation,
                _splitRemainderNoteId);
            if (epoch == 0) { ResetDockDragState(true); return; }
            _stickyUiHost.SetCurrentDockInteractionEpoch(epoch);
            string sourceId = seed.Id;
            string[] expectedIds = _activeDockGroupIds.ToArray();
            PostHostedStickyCommand(StickyUiCommand.CaptureDockFacts(expectedIds,
                topology, epoch), delegate(StickyUiCommandResult capture)
                {
                    if (!_dockInteraction.Matches(epoch, topology.Generation,
                        DockInteractionPhase.Finalizing)) return;
                    WindowFacts sourceFacts;
                    if (!TryApplyDockFactsBarrier(capture, expectedIds, topology,
                        epoch, sourceId, false, out sourceFacts))
                    {
                        TraceDockCommitRejected("final facts barrier rejected");
                        ResetDockDragState(true);
                        return;
                    }
                    DockPlacementPlan finalPlan = PlanDockPlan(seed, sourceFacts,
                        topology, epoch);
                    if (finalPlan == null) { TraceDockCommitRejected("final capture unavailable"); ResetDockDragState(true); return; }
                    List<string> expectedMemberIds = CollectExpectedPlanMemberIds(finalPlan);
                    _dockPlanMailbox.ReplaceWithFinal(finalPlan);
                    _stickyUiHost.PostFinalDockPlan(_dockPlanMailbox,
                        finalPlan.PlanSequence, delegate(StickyUiCommandResult result)
                        {
                            try
                            {
                                if (_dockInteraction.Matches(epoch,
                                    topology.Generation,
                                    DockInteractionPhase.Finalizing))
                                    CompleteDockDurableCommit(result, topology,
                                        epoch, seed, remainderSeed,
                                        expectedMemberIds, finalPlan.PlanSequence);
                            }
                            finally { _dockPlanMailbox.CompleteFinal(finalPlan.PlanSequence); ResetDockDragState(false); }
                        }, _petUiContext);
                });
        }

        private static List<string> CollectExpectedPlanMemberIds(
            DockPlacementPlan plan)
        {
            List<string> result = new List<string>();
            if (plan == null) return result;
            foreach (DockWindowTarget target in plan.WindowTargets)
                if (target != null) result.Add(target.NoteId);
            return result;
        }

        private void ResetDockDragState(bool clearMailbox)
        {
            long invalidatingEpoch = _dockInteraction.Reset();
            _stickyUiHost.SetCurrentDockInteractionEpoch(invalidatingEpoch);
            _activeNoteDragId = null;
            _activeDockGroupIds.Clear();
            _activeDockOriginalFacts.Clear();
            _activeDockCurrentFacts.Clear();
            _activeNoteDetached = false;
            _activeNoteSplitEligible = false;
            _splitRemainderNoteId = null;
            if (!clearMailbox) return;
            lock (_dockPlanMailbox.Gate)
            {
                _dockPlanMailbox.Current = null;
                _dockPlanMailbox.ApplyQueued = false;
                _dockPlanMailbox.FinalPlanSequence = 0;
            }
        }

        // P1-C: write one consistent canonical + compatibility placement from
        // the explicit physical bounds, never a bare X/Y/Width/Height that
        // waits for an async WPF snapshot to backfill the DisplayId/local.
        private static void ApplyDockCanonicalFromPhysical(StickyNoteData note,
            DockLayoutTarget target)
        {
            if (note == null || target == null) return;
            note.X = target.X;
            note.Y = target.Y;
            note.Width = target.Width;
            note.Height = target.Height;
            note.Visible = target.Visible;
            note.AlwaysOnTop = target.TopMost;
            WindowsDisplayMetrics metrics =
                WindowsDisplayResolver.ResolvePhysicalRect(
                    target.X, target.Y,
                    target.X + target.Width, target.Y + target.Height);
            if (metrics != null)
            {
                StickyCanonicalPlacement placement =
                    StickyPlacementMath.FromPhysicalRect(
                        metrics.DisplayId, metrics.PhysicalLeft,
                        metrics.PhysicalTop, metrics.Scale,
                        target.X, target.Y, target.Width, target.Height);
                note.DisplayId = placement.DisplayId;
                note.LocalLogicalX = placement.LocalX;
                note.LocalLogicalY = placement.LocalY;
                note.LocalLogicalWidth = placement.LocalWidth;
                note.LocalLogicalHeight = placement.LocalHeight;
            }
        }

        // P1-D: a narrow latest-wins frame for a live dock drag. A desired
        // plan only enters the mailbox; repository geometry is never written
        // before the native batch succeeds, and canonical/effective updates
        // come from the batch's actual facts in the completion callback.
        private void ApplyLiveDockPlan(DockPlacementPlan plan)
        {
            if (plan == null || !_dockInteraction.Matches(
                plan.InteractionEpoch, plan.TopologyGeneration,
                DockInteractionPhase.Dragging)) return;
            DockPlanMailbox mailbox = _dockPlanMailbox;
            lock (mailbox.Gate)
            {
                // Diagnostic-only evidence for latest-wins: a newer plan
                // replacing a still-pending one is a supersede transition.
                bool superseded =
                    mailbox.ApplyQueued && mailbox.Current != null;
                mailbox.Current = plan;
                if (superseded)
                    DisplayDiagnostics.Trace("DockPlanSuperseded",
                        "source=" + plan.SourceNoteId +
                        " sequence=" + plan.PlanSequence +
                        " epoch=" + plan.InteractionEpoch);
                if (mailbox.ApplyQueued) return;
                mailbox.ApplyQueued = true;
            }
            _stickyUiHost.PostLatestDockPlan(mailbox,
                delegate(StickyUiCommandResult result)
                {
                    if (result == null) return;
                    if (!_dockInteraction.Matches(plan.InteractionEpoch,
                        plan.TopologyGeneration,
                        DockInteractionPhase.Dragging)) return;
                    if (result.Status != StickyUiCommandStatus.Handled)
                    {
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-bounds-batch", result);
                        return;
                    }
                    ApplyDockBatchResult(result.DockBatchResult);
                }, _petUiContext);
        }

        // Only same-generation, newest-sequence batch results are accepted.
        // Actual WindowFacts are the effective geometry truth; content and
        // non-geometry state come from the member snapshot.
        private void ApplyDockBatchResult(DockBatchResult batch)
        {
            if (batch == null || batch.Members.Count == 0) return;
            if (!_dockInteraction.Matches(batch.InteractionEpoch,
                batch.TopologyGeneration, DockInteractionPhase.Dragging)) return;
            if (_displayTopologyRuntime == null ||
                _displayTopologyRuntime.Current == null ||
                _displayTopologyRuntime.Current.Generation !=
                    batch.TopologyGeneration) return;
            if (batch.PlanSequence < _lastAppliedDockPlanSequence) return;
            _lastAppliedDockPlanSequence = batch.PlanSequence;
            DisplayTopologySnapshot topology =
                _displayTopologyRuntime.Current;
            foreach (DockBatchMemberResult member in batch.Members)
            {
                if (member == null || member.Snapshot == null ||
                    member.Facts == null || member.WindowSequence !=
                    member.Facts.WindowSequence || member.Facts.TopologyGeneration !=
                    batch.TopologyGeneration || !String.Equals(member.NoteId,
                    member.Facts.WindowId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!_hostedRuntime.CanApplySequence(member.NoteId,
                    member.WindowSequence)) continue;
                StickyNoteData canonical = _notes.Find(member.NoteId);
                if (canonical == null) continue;
                member.Snapshot.ApplyContentTo(canonical);
                canonical.Visible = member.Snapshot.Visible;
                canonical.AlwaysOnTop = member.Snapshot.AlwaysOnTop;
                ApplyHostedStickyFactsGeometry(canonical, member.Facts,
                    topology);
                _placementRuntime.TryUpdateEffective(member.NoteId,
                    member.Facts);
                _hostedRuntime.RecordSequence(member.NoteId,
                    member.WindowSequence);
            }
        }

        private void SetActiveDockGroup(List<StickyNoteData> notes)
        {
            _activeDockGroupIds.Clear();
            if (notes == null) return;
            foreach (StickyNoteData note in notes)
                if (note != null) _activeDockGroupIds.Add(note.Id);
        }

        private Dictionary<string, DockWindowFacts>
            CaptureDockFacts(IEnumerable<string> noteIds)
        {
            Dictionary<string, DockWindowFacts> facts =
                new Dictionary<string, DockWindowFacts>(
                    StringComparer.OrdinalIgnoreCase);
            if (noteIds == null) return facts;
            foreach (string noteId in noteIds)
            {
                StickyNoteData note = _notes.Find(noteId);
                if (note == null) continue;
                facts[noteId] = DockWindowFacts.FromData(note);
            }
            return facts;
        }

        private Dictionary<string, DockWindowFacts>
            CaptureDockFacts(IEnumerable<StickyNoteData> notes)
        {
            List<string> noteIds = new List<string>();
            if (notes != null)
                foreach (StickyNoteData note in notes)
                    if (note != null) noteIds.Add(note.Id);
            return CaptureDockFacts(noteIds);
        }

        internal static List<DockLayoutTarget> CalculateDockTranslationTargets(
            IList<string> noteIds,
            IDictionary<string, DockWindowFacts> currentFacts,
            DockWindowFacts movedSource, int dx, int dy)
        {
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (noteIds == null || currentFacts == null) return targets;
            foreach (string noteId in noteIds)
            {
                DockWindowFacts facts;
                if (movedSource != null && String.Equals(noteId,
                    movedSource.NoteId, StringComparison.OrdinalIgnoreCase))
                    facts = movedSource;
                else if (!currentFacts.TryGetValue(noteId, out facts))
                    continue;
                if (facts == null || !facts.Visible) continue;
                targets.Add(facts.ToTarget(
                    movedSource != null && String.Equals(noteId,
                        movedSource.NoteId,
                        StringComparison.OrdinalIgnoreCase)
                        ? facts.X : facts.X + dx,
                    movedSource != null && String.Equals(noteId,
                        movedSource.NoteId,
                        StringComparison.OrdinalIgnoreCase)
                        ? facts.Y : facts.Y + dy));
            }
            return targets;
        }

        private void RememberActiveDockFacts(
            IEnumerable<DockLayoutTarget> targets)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                _activeDockCurrentFacts[target.NoteId] =
                    DockWindowFacts.FromTarget(target);
        }

        private List<StickyNoteData> BuildDockChainOrder(StickyNoteData seed)
        {
            return BuildDockChainOrder(seed, true);
        }

        private List<StickyNoteData> BuildDockChainOrderIncludingHidden(
            StickyNoteData seed)
        {
            return StickyDockGroups.GetOrderedGroup(_notes.GetAll(), seed);
        }

        private List<StickyNoteData> BuildDockChainOrder(StickyNoteData seed,
            bool visibleOnly)
        {
            return StickyDockOperations.BuildDockChainOrderFromNotes(
                _notes.GetAll(), seed, visibleOnly);
        }

        private List<StickyNoteData> BuildAuthoritativeVisibleDockOrder(
            StickyNoteData seed)
        {
            List<StickyNoteData> live = BuildDockChainOrder(seed);
            List<StickyNoteData> stored =
                BuildDockChainOrderIncludingHidden(seed);
            stored.RemoveAll(delegate(StickyNoteData note)
            {
                return !note.Visible;
            });
            return StickyDockOperations.SelectMoreCompleteDockOrder(
                live, stored);
        }

        private void CommitVisibleDockOrder(StickyNoteData seed)
        {
            if (seed == null) return;
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(seed);
            if (snapshot.Count > 1)
            {
                StickyDockGroups.ApplyGroupSnapshot(snapshot);
                StickyDockGroups.RebuildVisibleParentChain(snapshot);
                return;
            }
            StickyDockGroups.ApplyOrderedGroup(
                BuildDockChainOrder(seed));
        }

        private void LayoutDockChain(List<StickyNoteData> ordered,
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
                    !factsById.TryGetValue(note.Id, out facts))
                    facts = DockWindowFacts.FromData(note);
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

        // Hosted effect edge: canonical data is updated first; this boundary
        // schedules the typed Window effect on the Sticky STA.
        private void ApplyDockTargets(IEnumerable<DockLayoutTarget> targets,
            string alreadyAppliedNoteId)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                ApplyDockTarget(target, alreadyAppliedNoteId);
        }

        private void ApplyDockTarget(DockLayoutTarget target,
            string alreadyAppliedNoteId)
        {
            if (target == null) return;
            StickyNoteData note = _notes.Find(target.NoteId);
            if (note == null) return;
            ApplyDockCanonicalFromPhysical(note, target);
            if (String.Equals(target.NoteId, alreadyAppliedNoteId,
                StringComparison.OrdinalIgnoreCase)) return;
            if (!IsHostedSticky(note)) return;
            PostHostedStickyCommand(StickyUiCommand.SetBounds(
                target.NoteId, new StickyUiBounds(target.X, target.Y,
                    target.Width, target.Height)),
                delegate(StickyUiCommandResult result)
                {
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled)
                        ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence, false);
                    else
                    {
                        ClearHostedDockResizeSessionIfMember(target.NoteId);
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-bounds", result);
                    }
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

        private bool CanSafelyCombineDockComponents(DockTarget target,
            StickyNoteData sourceSeed,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (target == null || String.IsNullOrEmpty(target.ParentNoteId) ||
                sourceSeed == null)
                return false;
            StickyNoteData parent = _notes.Find(target.ParentNoteId);
            if (parent == null) return false;
            List<StickyNoteData> targetOrder = BuildDockChainOrder(
                parent);
            if (targetOrder.Count == 0) return false;
            List<int> heights = new List<int>();
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in targetOrder)
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                {
                    DockWindowFacts facts;
                    heights.Add(factsById != null &&
                        factsById.TryGetValue(note.Id, out facts)
                            ? facts.Height : note.Height);
                }
            }
            foreach (StickyNoteData note in BuildDockChainOrder(sourceSeed))
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                {
                    DockWindowFacts facts;
                    heights.Add(factsById != null &&
                        factsById.TryGetValue(note.Id, out facts)
                            ? facts.Height : note.Height);
                }
            }
            DockWindowFacts rootFacts;
            return StickyDockOperations.IsDockCoordinateRangeSafe(
                factsById != null && factsById.TryGetValue(
                    targetOrder[0].Id, out rootFacts)
                    ? rootFacts.Y : targetOrder[0].Y,
                heights, DockCoordinateSafetyLimit);
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
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(ordered, factsById, rootAnchor.X,
                    rootAnchor.Y, rootWidth);
                ApplyDockComponentTopMost(seed,
                    ordered[0].AlwaysOnTop, null);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void NormalizeAllDockGroups()
        {
            HashSet<string> normalized = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
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

        private bool BeginHostedStickyDockDivider(DockWindowFacts snapshot)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || snapshot == null) return false;
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return false;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            int sourceIndex = ordered.FindIndex(
                delegate(StickyNoteData note)
                {
                    return String.Equals(note.Id, snapshot.NoteId,
                        StringComparison.OrdinalIgnoreCase);
                });
            if (sourceIndex < 0 || sourceIndex >= ordered.Count - 1)
                return false;
            List<DockWindowFacts> startFacts =
                new List<DockWindowFacts>();
            foreach (StickyNoteData note in ordered)
            {
                if (note == null || !note.Visible) continue;
                startFacts.Add(String.Equals(note.Id, snapshot.NoteId,
                    StringComparison.OrdinalIgnoreCase)
                    ? snapshot : DockWindowFacts.FromData(note));
            }
            if (startFacts.Count != ordered.Count) return false;
            _activeHostedDockResizeSourceId = snapshot.NoteId;
            _activeHostedDockResizeFacts = startFacts;
            return true;
        }

        private bool ResizeHostedStickyDockDivider(string sourceNoteId,
            int requestedHeight)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null ||
                _activeHostedDockResizeFacts == null ||
                !String.Equals(_activeHostedDockResizeSourceId,
                    sourceNoteId, StringComparison.OrdinalIgnoreCase))
                return false;
            StickyNoteData source = _notes.Find(sourceNoteId);
            if (source == null) return false;
            List<StickyNoteData> ordered = BuildDockChainOrder(source);
            if (!MatchesHostedDockResizeSession(ordered)) return false;
            int sourceHeight;
            List<DockLayoutTarget> targets =
                CalculateDockMemberResizeTargets(
                    _activeHostedDockResizeFacts, sourceNoteId,
                    requestedHeight, out sourceHeight);
            source.Height = sourceHeight;
            List<DockLayoutTarget> changed = new List<DockLayoutTarget>();
            foreach (DockLayoutTarget target in targets)
            {
                StickyNoteData note = _notes.Find(target.NoteId);
                if (note == null) return false;
                if (note.X != target.X || note.Y != target.Y ||
                    note.Width != target.Width || note.Height != target.Height ||
                    note.Visible != target.Visible ||
                    note.AlwaysOnTop != target.TopMost)
                    changed.Add(target);
            }
            _synchronizingDockLayout = true;
            try
            {
                ApplyDockTargets(changed, sourceNoteId);
            }
            finally { _synchronizingDockLayout = false; }
            return true;
        }

        private bool MatchesHostedDockResizeSession(
            List<StickyNoteData> ordered)
        {
            if (ordered == null || _activeHostedDockResizeFacts == null ||
                ordered.Count != _activeHostedDockResizeFacts.Count)
                return false;
            for (int index = 0; index < ordered.Count; index++)
                if (ordered[index] == null || !ordered[index].Visible ||
                    !String.Equals(ordered[index].Id,
                        _activeHostedDockResizeFacts[index].NoteId,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }

        internal static List<DockLayoutTarget> CalculateDockDividerTargets(
            DockWindowFacts upper, DockWindowFacts lower)
        {
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (upper == null || lower == null) return targets;
            int upperHeight = CalculateDockDividerHeight(upper.Height);
            targets.Add(new DockLayoutTarget(upper.NoteId, upper.X, upper.Y,
                upper.Width, upperHeight, upper.Visible, upper.TopMost));
            targets.Add(new DockLayoutTarget(lower.NoteId, lower.X,
                upper.Y + upperHeight, lower.Width, lower.Height,
                lower.Visible, lower.TopMost));
            return targets;
        }

        internal static List<DockLayoutTarget>
            CalculateDockMemberResizeTargets(
                IList<DockWindowFacts> startFacts, string sourceNoteId,
                int requestedSourceHeight, out int sourceHeight)
        {
            sourceHeight = CalculateDockDividerHeight(requestedSourceHeight);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            if (startFacts == null) return targets;
            int sourceIndex = -1;
            List<DockRect> startBounds = new List<DockRect>();
            for (int index = 0; index < startFacts.Count; index++)
            {
                DockWindowFacts facts = startFacts[index];
                if (facts == null) return new List<DockLayoutTarget>();
                startBounds.Add(new DockRect(facts.X, facts.Y,
                    facts.Width, facts.Height));
                if (String.Equals(facts.NoteId, sourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) sourceIndex = index;
            }
            List<DockRect> layout =
                StickyDockGeometry.CalculateDockMemberResizeTargets(
                    startBounds, sourceIndex, requestedSourceHeight,
                    out sourceHeight);
            for (int index = 0; index < layout.Count; index++)
            {
                DockWindowFacts facts = startFacts[sourceIndex + index + 1];
                DockRect bounds = layout[index];
                targets.Add(new DockLayoutTarget(facts.NoteId, bounds.Left,
                    bounds.Top, bounds.Width, bounds.Height, facts.Visible,
                    facts.TopMost));
            }
            return targets;
        }

        private void ResizeStickyDockGroup(DockWindowFacts snapshot,
            int requestedLeft, int requestedWidth)
        {
            if (_synchronizingDockLayout || _movingDockGroup ||
                _activeNoteDragId != null || snapshot == null) return;
            StickyNoteData seed = _notes.Find(snapshot.NoteId);
            if (seed == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(seed);
            if (ordered.Count <= 1) return;
            int left = requestedLeft;
            int width = Math.Max(280, Math.Min(900, requestedWidth));
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(ordered);
            facts[snapshot.NoteId] = snapshot;
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            foreach (StickyNoteData note in ordered)
            {
                DockWindowFacts memberFacts;
                if (!facts.TryGetValue(note.Id, out memberFacts)) continue;
                targets.Add(new DockLayoutTarget(note.Id, left,
                    memberFacts.Y, width, memberFacts.Height,
                    memberFacts.Visible, memberFacts.TopMost));
            }
            _synchronizingDockLayout = true;
            try
            {
                ApplyDockTargets(targets, snapshot.NoteId);
            }
            finally { _synchronizingDockLayout = false; }
        }

        private void RefreshDockResizeRoles()
        {
            foreach (StickyNoteData note in _notes.GetAll())
                ApplyDockResizeRole(note, false, true, true,
                    false, 220, 700);
            HashSet<string> handled = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (!note.Visible || handled.Contains(note.Id)) continue;
                List<StickyNoteData> ordered = BuildDockChainOrder(note);
                if (ordered.Count <= 1) continue;
                for (int index = 0; index < ordered.Count; index++)
                {
                    bool internalDivider = index < ordered.Count - 1;
                    ApplyDockResizeRole(ordered[index], true,
                        index == 0, true, internalDivider,
                        220, 700);
                    handled.Add(ordered[index].Id);
                }
            }
        }

        private void ApplyDockResizeRole(StickyNoteData note, bool grouped,
            bool resizeTop, bool resizeBottom, bool splitBottom,
            int dividerMinimumHeight, int dividerMaximumHeight)
        {
            if (note == null) return;
            if (!IsHostedSticky(note)) return;
            StickyUiDockResizeRole hostedRole = new StickyUiDockResizeRole(
                grouped, resizeTop, resizeBottom, splitBottom,
                dividerMinimumHeight, dividerMaximumHeight);
            PostHostedStickyCommand(StickyUiCommand.SetDockResizeRole(
                note.Id, hostedRole),
                delegate(StickyUiCommandResult result)
                {
                    if (result == null || result.Status !=
                        StickyUiCommandStatus.Handled)
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-resize-role", result);
                });
        }

        internal static int CalculateDockDividerHeight(
            int requestedUpperHeight)
        {
            return StickyDockGeometry.CalculateDockDividerHeight(
                requestedUpperHeight);
        }

        private void RestoreDockOriginalLocations(string seedNoteId)
        {
            StickyNoteData seed = _notes.Find(seedNoteId);
            if (seed == null || _activeDockOriginalFacts.Count == 0)
                return;
            List<StickyNoteData> component = BuildDockComponent(seed);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            foreach (StickyNoteData note in component)
            {
                DockWindowFacts original;
                if (_activeDockOriginalFacts.TryGetValue(note.Id,
                    out original))
                    targets.Add(original.ToTarget(original.X, original.Y));
            }
            _movingDockGroup = true;
            try { ApplyDockTargets(targets, null); }
            finally { _movingDockGroup = false; }
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

        private List<StickyNoteData> BuildDockComponent(StickyNoteData seed)
        {
            List<StickyNoteData> all = _notes.GetAll();
            List<StickyNoteData> result = new List<StickyNoteData>();
            if (seed == null) return result;
            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            ids.Add(seed.Id);
            bool changed;
            do
            {
                changed = false;
                foreach (StickyNoteData note in all)
                {
                    if (!note.Visible) continue;
                    if (ids.Contains(note.Id) ||
                        (!String.IsNullOrEmpty(note.DockParentId) &&
                            ids.Contains(note.DockParentId)))
                    {
                        if (ids.Add(note.Id)) changed = true;
                        if (!String.IsNullOrEmpty(note.DockParentId) &&
                            ids.Add(note.DockParentId)) changed = true;
                    }
                }
            }
            while (changed);
            foreach (StickyNoteData note in all)
                if (note.Visible && ids.Contains(note.Id)) result.Add(note);
            return result;
        }

        private StickyNoteData FindActiveDockTail(StickyNoteData seed)
        {
            List<StickyNoteData> activeNotes = new List<StickyNoteData>();
            foreach (string noteId in _activeDockGroupIds)
            {
                StickyNoteData note = _notes.Find(noteId);
                if (note != null) activeNotes.Add(note);
            }
            return StickyDockOperations.FindActiveDockTail(_notes.GetAll(),
                activeNotes, seed);
        }

        private void ShowSplitGuide(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            ClearSplitGuide();
            if (source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(source.DockParentId, out parentFacts)
                ? CalculateDockVisualSeamPhysical(parentFacts) :
                Rectangle.Empty;
            if (seam.IsEmpty) return;
            _splitGuideIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(255, 151, 62), 0);
            _splitGuideIndicator.ShowSeam(seam);
        }

        private void UpdateSplitGuide(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (_splitGuideIndicator == null ||
                _splitGuideIndicator.IsDisposed || source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(source.DockParentId, out parentFacts)
                ? CalculateDockVisualSeamPhysical(parentFacts) :
                Rectangle.Empty;
            if (!seam.IsEmpty) _splitGuideIndicator.UpdateSeam(seam);
        }

        private void ClearSplitGuide()
        {
            if (_splitGuideIndicator != null &&
                !_splitGuideIndicator.IsDisposed)
                _splitGuideIndicator.Close();
            _splitGuideIndicator = null;
        }

        private void UpdateDockPreview(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            DockTarget target = FindDockTarget(source, factsById);
            StickyNoteData parent = target == null ? null :
                _notes.Find(target.ParentNoteId);
            StickyNoteData child = target == null ? null :
                _notes.Find(target.ExistingChildNoteId);
            if (String.Equals(parent == null ? String.Empty : parent.Id,
                _dockPreviewParentNoteId ?? String.Empty,
                StringComparison.OrdinalIgnoreCase) &&
                String.Equals(child == null ? String.Empty : child.Id,
                _dockPreviewChildNoteId ?? String.Empty,
                StringComparison.OrdinalIgnoreCase)) return;
            ClearDockPreview();
            if (parent == null) return;
            _dockPreviewParentNoteId = parent.Id;
            _dockPreviewChildNoteId = child == null ? String.Empty : child.Id;
            _dockPreviewIndicator = new DockPulseIndicatorForm(
                Color.FromArgb(32, 160, 255), 0);
            DockWindowFacts parentFacts;
            if (factsById != null && factsById.TryGetValue(parent.Id,
                out parentFacts))
                _dockPreviewIndicator.ShowSeam(
                    CalculateDockVisualSeamPhysical(parentFacts));
        }

        internal static Rectangle CalculateDockVisualSeam(
            DockWindowFacts facts)
        {
            return facts == null ? Rectangle.Empty : new Rectangle(facts.X,
                facts.Y + facts.Height - 3, facts.Width, 6);
        }

        private Rectangle CalculateDockVisualSeamPhysical(
            DockWindowFacts facts)
        {
            Rectangle seam = CalculateDockVisualSeam(facts);
            if (seam.IsEmpty) return seam;
            double scale = DeviceDpi / 96.0;
            return new Rectangle(
                (int)Math.Round(seam.X * scale),
                (int)Math.Round(seam.Y * scale),
                Math.Max(1, (int)Math.Round(seam.Width * scale)),
                Math.Max(1, (int)Math.Round(seam.Height * scale)));
        }

        private DockTarget FindDockTarget(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (source == null) return null;
            if (!String.IsNullOrEmpty(source.DockParentId) &&
                !_activeNoteDetached) return null;
            HashSet<string> activeIds = new HashSet<string>(
                _activeDockGroupIds, StringComparer.OrdinalIgnoreCase);
            DockWindowFacts sourceFacts;
            if (factsById == null ||
                !factsById.TryGetValue(source.Id, out sourceFacts))
                return null;
            Rectangle sourceBounds = new Rectangle(sourceFacts.X,
                sourceFacts.Y, sourceFacts.Width, sourceFacts.Height);
            DockTarget best = null;
            int bestScore = Int32.MaxValue;
            foreach (StickyNoteData candidate in _notes.GetAll())
            {
                if (candidate == null || !candidate.Visible ||
                    String.Equals(candidate.Id, source.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    activeIds.Contains(candidate.Id))
                    continue;
                if (!CanUseDockComponents(source, candidate)) continue;
                DockWindowFacts candidateFacts;
                if (!factsById.TryGetValue(candidate.Id,
                    out candidateFacts) || !candidateFacts.Visible) continue;
                Rectangle candidateBounds = new Rectangle(candidateFacts.X,
                    candidateFacts.Y, candidateFacts.Width,
                    candidateFacts.Height);
                if (!CanDockBelow(sourceBounds, candidateBounds, 20)) continue;
                int score = Math.Abs(sourceFacts.Y -
                    candidateBounds.Bottom) * 10 +
                    Math.Min(Math.Abs(sourceFacts.X - candidateFacts.X),
                        Math.Abs(sourceBounds.Right - candidateBounds.Right));
                if (score >= bestScore) continue;
                best = new DockTarget();
                best.ParentNoteId = candidate.Id;
                best.ExistingChildNoteId = FindDockChild(candidate.Id,
                    activeIds);
                bestScore = score;
            }
            if (best != null && !CanSafelyCombineDockComponents(best, source,
                factsById))
                return null;
            return best;
        }

        private bool CanUseDockComponents(StickyNoteData source,
            StickyNoteData target)
        {
            if (!IsDockParticipant(source) ||
                !IsDockParticipant(target)) return false;
            foreach (string noteId in _activeDockGroupIds)
                if (!IsDockParticipant(_notes.Find(noteId)))
                    return false;
            foreach (StickyNoteData note in
                BuildDockChainOrderIncludingHidden(target))
                if (!IsDockParticipant(note)) return false;
            return true;
        }

        private bool IsDockParticipant(StickyNoteData note)
        {
            return note != null;
        }

        private string FindDockChild(string parentId,
            HashSet<string> ignoredIds)
        {
            foreach (StickyNoteData note in _notes.GetAll())
            {
                if (ignoredIds != null && ignoredIds.Contains(note.Id)) continue;
                if (String.Equals(note.DockParentId, parentId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (note.Visible) return note.Id;
                }
            }
            return String.Empty;
        }

        internal static bool CanDockBelow(Rectangle moving, Rectangle target,
            int threshold)
        {
            return StickyDockOperations.CanDockBelow(moving.Left, moving.Top,
                moving.Width, moving.Height, target.Left, target.Top,
                target.Width, target.Height, threshold);
        }

        private void ClearDockPreview()
        {
            if (_dockPreviewIndicator != null &&
                !_dockPreviewIndicator.IsDisposed)
                _dockPreviewIndicator.Close();
            _dockPreviewParentNoteId = null;
            _dockPreviewChildNoteId = null;
            _dockPreviewIndicator = null;
        }

        private static void ShowTransientDockPulse(Rectangle seam, Color color)
        {
            DockPulseIndicatorForm indicator = new DockPulseIndicatorForm(
                color, 720);
            indicator.ShowSeam(seam);
        }

        private void DetachDockRelations(StickyNoteData note)
        {
            if (note == null) return;
            List<StickyNoteData> ordered = BuildDockChainOrder(note);
            int index = ordered.FindIndex(delegate(StickyNoteData candidate)
            {
                return String.Equals(candidate.Id, note.Id,
                    StringComparison.OrdinalIgnoreCase);
            });
            if (index >= 0)
            {
                ordered.RemoveAt(index);
                StickyDockGroups.ApplyOrderedGroup(ordered);
            }
            else
            {
                foreach (StickyNoteData candidate in _notes.GetAll())
                    if (String.Equals(candidate.DockParentId, note.Id,
                        StringComparison.OrdinalIgnoreCase))
                        StickyDockGroups.ClearMembership(candidate);
            }
            StickyDockGroups.ClearMembership(note);
        }

        private void HideStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (PostHostedStickyHide(note)) return;
            List<StickyNoteData> snapshot =
                BuildDockChainOrderIncludingHidden(note);
            Dictionary<string, DockWindowFacts> facts =
                CaptureDockFacts(snapshot);
            DockWindowFacts rootFacts = DockWindowFacts.FromData(note);
            DockWindowFacts capturedRoot;
            if (snapshot.Count > 0 && facts.TryGetValue(snapshot[0].Id,
                out capturedRoot)) rootFacts = capturedRoot;
            note.Visible = false;
            StickyDockOperations.PreserveDockSlotForHiddenMember(
                snapshot, note);
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(snapshot, facts, rootFacts.X, rootFacts.Y,
                    rootFacts.Width);
            }
            finally { _synchronizingDockLayout = false; }
            _notes.Save();
            RefreshDockResizeRoles();
            RefreshNoteTabs();
        }

        private void DeleteStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (IsHostedSticky(note))
            {
                BeginHostedStickyDelete(note);
                return;
            }
            DeleteStickyNoteAfterWindowClosed(note);
        }

        private void BeginHostedStickyDelete(StickyNoteData note)
        {
            string noteId = note.Id;
            if (!_hostedRuntime.TryBeginDelete(noteId)) return;
            PostHostedStickyCommand(StickyUiCommand.Close(noteId),
                delegate(StickyUiCommandResult result)
                {
                    _hostedRuntime.EndDelete(noteId);
                    if (result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                    {
                        ReportHostedStickyCommandFailure(
                            "sticky-hosted-delete", result);
                        ShowBubble("便利贴仍在编辑，删除已取消。");
                        return;
                    }
                    ApplyHostedStickySnapshot(result.Snapshot,
                        result.Sequence, false);
                    _hostedRuntime.RemoveNote(noteId);
                    StickyNoteData canonical = _notes.Find(noteId);
                    if (canonical != null)
                        DeleteStickyNoteAfterWindowClosed(canonical);
                });
        }

        private void DeleteStickyNoteAfterWindowClosed(StickyNoteData note)
        {
            DetachDockRelations(note);
            CancelReminderForNote(note, false);
            _notes.Remove(note);
            RefreshDockResizeRoles();
            RefreshMenuText();
            RefreshNoteTabs();
        }

        private void ConfirmDeleteStickyNote(StickyNoteData note)
        {
            if (note == null) return;
            if (MessageBox.Show(this,
                "确定删除便签“" + note.DisplayTitle + "”吗？此操作无法撤销。",
                "删除侧边页签", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
            DeleteStickyNote(note);
        }
    }
}
