using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PennyPet
{
    // Owns the lifetime of Dock gestures, restores, mailboxes and deferred mutations.
    // All HWND and shell effects go through the owning sticky workspace.
    internal sealed class StickyDockController : IDisposable
    {
        private readonly StickyWorkspace _workspace;

        internal StickyDockController(StickyWorkspace workspace) { _workspace = workspace; }

        public void Dispose()
        {
            ClearHostedDockResizeSession();
            CancelHostedDockRestores();
            ResetDockDragState();
            ClearDockPreview();
            ClearSplitGuide();
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
            _synchronizingDockLayout = true;
            try
            {
                LayoutDockChain(snapshot, facts, rootFacts.X, rootFacts.Y,
                    rootFacts.Width);
            }
            finally { _synchronizingDockLayout = false; }
            _workspace.Notes.SaveAsync();
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
        }

        private sealed class DockTarget
        {
            public string ParentNoteId;
            public string ExistingChildNoteId;
        }
        // Keep every member inside a coordinate range that Win32 mouse
        // messages can address reliably. This is a Windows platform limit, not
        // a Penny business rule, so Core receives it as a parameter.
        private const int DockCoordinateSafetyLimit = 30000;
        private bool _movingDockGroup;
        private bool _synchronizingDockLayout;
        internal readonly DockGestureOwner Gestures = new DockGestureOwner();
        internal DockInteractionSession Interaction { get { return Gestures.Drag; } }
        private long _lastAppliedDockPlanSequence = -1;
        private long _dockSceneRevision;
        private long _nextDockOperationSequence;
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
            ClearHostedDockResizeSessionIfMember(sourceData.Id);
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
                _synchronizingDockLayout = true;
                try
                {
                    LayoutDockChain(snapshot, facts, rootFacts.X,
                        rootFacts.Y, rootFacts.Width);
                }
                finally { _synchronizingDockLayout = false; }
            }
            _workspace.Notes.SaveAsync();
            RefreshDockResizeRoles();
            _workspace.RefreshNoteTabs();
            _workspace.RefreshMenuText();
        }





        // A Dock interaction is allowed to advance only after every expected
        // HWND has yielded exact current-generation facts.  Validate the
        // whole barrier before updating Pet-side mirrors.
        private bool TryApplyDockFactsBarrier(StickyUiCommandResult result,
            IList<string> expectedIds, DisplayTopologySnapshot topology,
            long epoch, string sourceNoteId, bool resetBaselineFacts,
            out WindowFacts sourceFacts)
        {
            sourceFacts = null;
            if (result == null || result.Status != StickyUiCommandStatus.Handled ||
                result.DockBatchResult == null || expectedIds == null ||
                expectedIds.Count == 0 || epoch <= 0 || !_workspace.IsTopologyCurrent(topology)) return false;
            DockBatchResult batch = result.DockBatchResult;
            if (batch.InteractionEpoch != epoch || batch.PlanSequence != 0 ||
                batch.TopologyGeneration != topology.Generation ||
                batch.Members.Count != expectedIds.Count) return false;
            var remaining = new HashSet<string>(expectedIds, StringComparer.OrdinalIgnoreCase);
            if (remaining.Count != expectedIds.Count) return false;
            var updates = new List<StickyFactsReceiver.Update>(batch.Members.Count);
            var runtimeFacts = new List<DockWindowFacts>(batch.Members.Count);
            WindowFacts capturedSource = null;
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyFactsReceiver.Update update;
                if (member == null || member.Snapshot == null || !remaining.Remove(member.NoteId) ||
                    !_workspace.Facts.TryPrepare(member, topology, out update)) return false;
                DockWindowFacts facts = DockWindowFacts.FromWindowFacts(member.Facts,
                    member.Snapshot.Visible, member.Snapshot.AlwaysOnTop);
                if (facts == null) return false;
                if (String.Equals(member.NoteId, sourceNoteId, StringComparison.OrdinalIgnoreCase))
                    capturedSource = member.Facts;
                updates.Add(update);
                runtimeFacts.Add(facts);
            }
            if (capturedSource == null) return false;
            foreach (StickyFactsReceiver.Update update in updates) update.Commit();
            Interaction.AcceptCapturedFacts(runtimeFacts, resetBaselineFacts);
            sourceFacts = capturedSource;
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
        private string[] BuildActiveDockZOrderIds(StickyNoteData seed)
        {
            if (seed == null || Interaction.MemberIds.Count < 2)
                return new string[0];

            HashSet<string> active = new HashSet<string>(
                Interaction.MemberIds, StringComparer.OrdinalIgnoreCase);
            List<StickyNoteData> ordered =
                BuildDockChainOrder(seed);
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

        internal void BeginDockInput(DockInput input)
        {
            Action[] deferred = Gestures.BeginInput(input);
            _workspace.Host.SetCurrentDockInteractionEpoch(Interaction.Epoch);
            ClearDockPreview();
            ClearSplitGuide();
            RunDeferredDockMutations(deferred);
        }

        internal void BeginStickyDockDrag(DockWindowFacts facts,
            WindowFacts sourceFacts, DisplayTopologySnapshot topology)
        {
            if (facts == null || sourceFacts == null || topology == null ||
                !DockExecutionRules.IsSameGeneration(sourceFacts, topology)) return;
            CancelHostedDockRestores(facts.NoteId);
            StickyNoteData seed = _workspace.Notes.Find(facts.NoteId);
            if (seed == null || !seed.Visible) return;
            List<string> memberIds = BuildDockChainOrder(seed).ConvertAll(note => note.Id);
            Dictionary<string, DockWindowFacts> groupFacts = CaptureDockInteractionBaseline(memberIds, topology);
            groupFacts[facts.NoteId] = facts;
            long epoch = Gestures.BeginDrag(facts, memberIds,
                groupFacts, topology.Generation, DateTime.UtcNow);
            if (epoch == 0) return;
            _workspace.Host.SetCurrentDockInteractionEpoch(epoch);
            if (Interaction.SplitEligible) ShowSplitGuide(seed, groupFacts);
            // One drag-start Z-order transaction: restore the contiguous
            // moving-group band before the live geometry drag is armed. Only
            // this single request may reorder Z; live batches stay SWP_NOZORDER.
            string[] zOrderIds = BuildActiveDockZOrderIds(seed);
            if (zOrderIds.Length > 1)
            {
                _workspace.PostHostedStickyCommand(
                    StickyUiCommand.RaiseDockGroupForDrag(zOrderIds,
                        facts.NoteId, topology, epoch, Gestures.Input),
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
            if (!Interaction.TryEnterDragging(epoch, topology.Generation))
            {
                ResetDockDragState();
                return;
            }
            DisplayDiagnostics.Trace("DockDragReady",
                "source=" + facts.NoteId + " epoch=" + epoch +
                " generation=" + topology.Generation + " members=" +
                Interaction.MemberIds.Count + " splitEligible=" + Interaction.SplitEligible);
        }

        internal void MoveStickyDockDrag(DockWindowFacts facts,
            WindowFacts sourceFacts, DisplayTopologySnapshot topology)
        {
            if (facts == null) return;
            StickyNoteData seed = _workspace.Notes.Find(facts.NoteId);
            if (seed == null) return;
            if (_movingDockGroup ||
                !String.Equals(facts.NoteId, Interaction.SourceNoteId,
                    StringComparison.OrdinalIgnoreCase)) return;
            if (topology == null ||
                !Interaction.CanPlan(facts.NoteId, topology.Generation) ||
                !DockExecutionRules.IsSameGeneration(sourceFacts, topology)) return;
            if (!Interaction.HasMoved(facts)) return;
            DockSplitDecision split = Interaction.EvaluateSplit(facts, DateTime.UtcNow);
            if (split == DockSplitDecision.Cancelled) ClearSplitGuide();
            if (split == DockSplitDecision.Detach)
            {
                string connectedNoteId = FindVisibleDockParentId(seed);
                if (!String.IsNullOrEmpty(connectedNoteId))
                {
                    StickyDockOperations.ExtractSingleDockMember(BuildDockChainOrderIncludingHidden(seed), seed);
                    Interaction.Detach(connectedNoteId);
                    ClearSplitGuide();
                    RestoreDockOriginalLocations(connectedNoteId);
                    StickyNoteData remainder = _workspace.Notes.Find(connectedNoteId);
                    if (remainder != null) NormalizeDockComponent(remainder);
                    RefreshDockResizeRoles();
                }
            }

            DockPlacementPlan livePlan = PlanLiveDockPlan(seed, sourceFacts,
                topology);
            if (livePlan != null)
            {
                _movingDockGroup = true;
                try { ApplyLiveDockPlan(livePlan); }
                finally { _movingDockGroup = false; }
                Interaction.RememberTargets(PlanToDockTargets(livePlan));
            }
            Interaction.RecordMove(facts);
            if (!Interaction.Detached && Interaction.SplitEligible)
                UpdateSplitGuide(seed, Interaction.PreviewFacts);
            Dictionary<string, DockWindowFacts> previewFacts =
                CaptureDockFacts(_workspace.Notes.InStorageOrder);
            previewFacts[facts.NoteId] = facts;
            UpdateDockPreview(seed, previewFacts);
        }

        // DRT-10: the live drag is driven by the pure planner and the source
        // window's actual facts. The plan is built exactly once with one
        // capture-time topology generation and one mailbox sequence; nothing
        // downstream may re-stamp it against a later Current generation.
        private DockPlacementPlan PlanLiveDockPlan(
            StickyNoteData seed, WindowFacts sourceFacts,
            DisplayTopologySnapshot topology)
        {
            if (seed == null || sourceFacts == null || topology == null ||
                !Interaction.CanPlan(sourceFacts.WindowId,
                    topology.Generation)) return null;
            return PlanDockPlan(seed, sourceFacts, topology,
                Interaction.Epoch);
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

            List<WindowFacts> orderedFacts = new List<WindowFacts>();
            List<StickyNoteData> members = Interaction.IsFinalizing
                ? new List<string>(Interaction.MemberIds).ConvertAll(id => _workspace.Notes.Find(id))
                : BuildDockChainOrder(seed);
            foreach (StickyNoteData member in members)
            {
                if (member == null) return null;
                orderedFacts.Add(String.Equals(member.Id, sourceFacts.WindowId,
                    StringComparison.OrdinalIgnoreCase) ? sourceFacts :
                    _workspace.Placement.GetEffective(member.Id));
            }
            DockGroupLogicalState group;
            if (!StickyPlacementRules.TryBuildLiveDockState(orderedFacts,
                sourceFacts, topology, out group)) return null;

            DockPlacementPlan plan;
            try
            {
                plan = DockPlacementPlanner.Plan(group, sourceFacts,
                    surface, sourceFacts.Dpi,
                    sourceFacts.TopologyGeneration,
                    Gestures.NextPlanSequence(), interactionEpoch, Gestures.Input);
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
                StickyNoteData member = _workspace.Notes.Find(target.NoteId);
                if (member == null) continue;
                targets.Add(new DockLayoutTarget(target.NoteId,
                    target.PhysicalBounds.Left, target.PhysicalBounds.Top,
                    target.PhysicalBounds.Width,
                    target.PhysicalBounds.Height,
                    member.Visible, member.AlwaysOnTop));
            }
            return targets;
        }

        internal void CompleteStickyDockDrag(DockWindowFacts facts,
            StickyUiEvent value)
        {
            if (facts == null) return;
            StickyNoteData seed = _workspace.Notes.Find(facts.NoteId);
            if (seed == null) return;
            if (!String.Equals(facts.NoteId, Interaction.SourceNoteId,
                StringComparison.OrdinalIgnoreCase)) return;
            Dictionary<string, DockWindowFacts> currentFacts =
                CaptureDockFacts(_workspace.Notes.InStorageOrder);
            currentFacts[facts.NoteId] = facts;
            if (Interaction.IsFinalizing) return;
            DockTarget target = FindDockTarget(seed, currentFacts);
            StickyNoteData parent = target == null ? null : _workspace.Notes.Find(target.ParentNoteId);
            if (parent != null)
            {
                StickyNoteData tailData = FindActiveDockTail(seed) ?? seed;
                Interaction.StageMerge(StickyDockOperations.PrepareMergeAfterParent(
                    BuildDockChainOrderIncludingHidden(parent), parent,
                    BuildDockChainOrderIncludingHidden(seed)));
                StickyNoteData existingChild =
                    _workspace.Notes.Find(target.ExistingChildNoteId);
                if (existingChild != null)
                    ShowTransientDockPulse(CalculateDockVisualSeam(
                        GetHostedDockFacts(tailData)),
                        Color.FromArgb(32, 160, 255));
                ShowTransientDockPulse(CalculateDockVisualSeam(
                    GetHostedDockFacts(parent)),
                    Color.FromArgb(32, 160, 255));
            }
            ClearDockPreview();
            ClearSplitGuide();
            RefreshDockResizeRoles();
            StickyNoteData remainderSeed =
                _workspace.Notes.Find(Interaction.RemainderNoteId);
            StartDockFinalization(seed, remainderSeed);
        }

        // Mouse-up is an interaction signal, never geometry authority.  A
        // distinct finalizing epoch first captures current HWND facts, then
        // replaces every pending live plan with the one final native frame.
        private void StartDockFinalization(StickyNoteData seed,
            StickyNoteData remainderSeed)
        {
            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            if (seed == null || topology == null) { ResetDockDragState(); return; }
            List<StickyNoteData> finalMembers;
            if (Interaction.PendingMerge != null)
            {
                if (!Interaction.PendingMerge.TryResolve(_workspace.Notes.InStorageOrder, out finalMembers))
                {
                    TraceDockCommitRejected("membership changed before final capture");
                    ResetDockDragState();
                    return;
                }
            }
            else finalMembers = BuildDockChainOrderIncludingHidden(seed);
            var affectedMembers = new List<StickyNoteData>(finalMembers);
            if (remainderSeed != null) affectedMembers.AddRange(BuildDockChainOrderIncludingHidden(remainderSeed));
            foreach (StickyNoteData member in affectedMembers) CancelHostedDockRestores(member.Id);
            finalMembers.RemoveAll(note => !note.Visible);
            long epoch = Interaction.BeginFinalizing(topology.Generation,
                Interaction.RemainderNoteId, finalMembers.ConvertAll(note => note.Id), affectedMembers);
            if (epoch == 0) { ResetDockDragState(); return; }
            _workspace.Host.SetCurrentDockInteractionEpoch(epoch);
            string sourceId = seed.Id;
            string[] expectedIds = Interaction.CopyMemberIds();
            try
            {
                _workspace.PostHostedStickyCommand(StickyUiCommand.CaptureDockFacts(expectedIds,
                    topology, epoch, Gestures.Input), delegate(StickyUiCommandResult capture)
                    {
                        if (!Interaction.Matches(epoch, topology.Generation,
                            DockInteractionPhase.Finalizing)) return;
                        try
                        {
                            WindowFacts sourceFacts;
                            if (!TryApplyDockFactsBarrier(capture, expectedIds, topology,
                                epoch, sourceId, false, out sourceFacts))
                            {
                                TraceDockCommitRejected("final facts barrier rejected");
                                ResetDockDragState();
                                return;
                            }
                            DockPlacementPlan finalPlan = PlanDockPlan(seed, sourceFacts,
                                topology, epoch);
                            if (finalPlan == null) { TraceDockCommitRejected("final capture unavailable"); ResetDockDragState(); return; }
                            List<string> expectedMemberIds = CollectExpectedPlanMemberIds(finalPlan);
                            DockFrameMailbox<DockPlacementPlan> finalMailbox = Gestures.Plans;
                            finalMailbox.QueueFinal(finalPlan);
                            _workspace.Host.PostFinalDockPlan(finalMailbox,
                                finalPlan, delegate(StickyUiCommandResult result)
                                {
                                    try
                                    {
                                        if (Interaction.Matches(epoch,
                                            topology.Generation,
                                            DockInteractionPhase.Finalizing))
                                            CompleteDockDurableCommit(result, topology,
                                                epoch, seed, remainderSeed,
                                                expectedMemberIds, finalPlan.PlanSequence);
                                    }
                                    finally
                                    {
                                        finalMailbox.CompleteFinal(finalPlan);
                                        long invalidatingEpoch;
                                        Action[] deferred;
                                        if (Interaction.TryFinish(epoch, topology.Generation, out invalidatingEpoch, out deferred))
                                        {
                                            _workspace.Host.SetCurrentDockInteractionEpoch(invalidatingEpoch);
                                            RunDeferredDockMutations(deferred);
                                        }
                                    }
                                }, _workspace.Context);
                        }
                        catch
                        {
                            if (Interaction.Matches(epoch, topology.Generation, DockInteractionPhase.Finalizing))
                                ResetDockDragState();
                            throw;
                        }
                    });
            }
            catch
            {
                if (Interaction.Matches(epoch, topology.Generation, DockInteractionPhase.Finalizing))
                    ResetDockDragState();
                throw;
            }
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

        internal void ResetDockDragState()
        {
            Action[] deferred = Gestures.ResetDrag();
            _workspace.Host.SetCurrentDockInteractionEpoch(Interaction.Epoch);
            RunDeferredDockMutations(deferred);
        }

        // P1-D: a narrow latest-wins frame for a live dock drag. A desired
        // plan only enters the mailbox; repository geometry is never written
        // before the native batch succeeds, and canonical/effective updates
        // come from the batch's actual facts in the completion callback.
        private void ApplyLiveDockPlan(DockPlacementPlan plan)
        {
            if (plan == null || !Interaction.Matches(
                plan.InteractionEpoch, plan.TopologyGeneration,
                DockInteractionPhase.Dragging)) return;
            DockFrameMailbox<DockPlacementPlan> mailbox = Gestures.Plans;
            bool superseded;
            bool post = mailbox.QueueLive(plan, out superseded);
            if (superseded && DisplayDiagnostics.Enabled)
                DisplayDiagnostics.Trace("DockPlanSuperseded",
                    "source=" + plan.SourceNoteId +
                    " sequence=" + plan.PlanSequence +
                    " epoch=" + plan.InteractionEpoch);
            if (!post) return;
            _workspace.Host.PostLatestDockPlan(mailbox,
                delegate(StickyUiCommandResult result)
                {
                    if (result == null) return;
                    if (!Interaction.Matches(plan.InteractionEpoch,
                        plan.TopologyGeneration,
                        DockInteractionPhase.Dragging)) return;
                    if (result.Status != StickyUiCommandStatus.Handled)
                    {
                        StickyWorkspace.ReportHostedStickyCommandFailure(
                            "sticky-hosted-dock-bounds-batch", result);
                        return;
                    }
                    ApplyDockBatchResult(result.DockBatchResult);
                }, _workspace.Context);
        }

        // Only same-generation, newest-sequence batch results are accepted.
        // Actual WindowFacts are the effective geometry truth; content and
        // non-geometry state come from the member snapshot.
        private void ApplyDockBatchResult(DockBatchResult batch)
        {
            if (batch == null || batch.Members.Count == 0 ||
                !Interaction.Matches(batch.InteractionEpoch,
                    batch.TopologyGeneration, DockInteractionPhase.Dragging) ||
                batch.PlanSequence < _lastAppliedDockPlanSequence) return;
            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            if (topology == null || topology.Generation != batch.TopologyGeneration) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updates = new List<StickyFactsReceiver.Update>(batch.Members.Count);
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyFactsReceiver.Update update;
                if (member == null || member.Snapshot == null || !seen.Add(member.NoteId) ||
                    !_workspace.Facts.TryPrepare(member, topology, out update)) return;
                updates.Add(update);
            }
            _lastAppliedDockPlanSequence = batch.PlanSequence;
            foreach (StickyFactsReceiver.Update update in updates) update.Commit();
        }

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

        private string FindVisibleDockParentId(StickyNoteData seed)
        {
            StickyNoteData parent = StickyDockGroups.GetVisibleNeighbor(_workspace.Notes.InStorageOrder, seed, -1);
            return parent == null ? String.Empty : parent.Id;
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
        private void ApplyDockTargets(IEnumerable<DockLayoutTarget> targets,
            string alreadyAppliedNoteId, DockInput input = null)
        {
            if (targets == null) return;
            foreach (DockLayoutTarget target in targets)
                ApplyDockTarget(target, alreadyAppliedNoteId, input);
        }

        private void ApplyDockTarget(DockLayoutTarget target,
            string alreadyAppliedNoteId, DockInput input = null)
        {
            if (target == null) return;
            StickyNoteData note = _workspace.Notes.Find(target.NoteId);
            if (note == null) return;
            bool traceResize = Gestures.Resize != null;
            note.Visible = target.Visible;
            note.AlwaysOnTop = target.TopMost;
            if (String.Equals(target.NoteId, alreadyAppliedNoteId,
                StringComparison.OrdinalIgnoreCase)) return;
            if (!_workspace.IsHostedSticky(note)) return;
            if (traceResize)
                DisplayDiagnostics.Trace("DockTargetPosted",
                    "note=" + target.NoteId +
                    " rect=(" + target.X + "," + target.Y + "," +
                    target.Width + "," + target.Height + ")");
            _workspace.PostHostedStickyCommand(StickyUiCommand.SetBounds(
                target.NoteId, new StickyUiBounds(target.X, target.Y,
                    target.Width, target.Height), input: input),
                delegate(StickyUiCommandResult result)
                {
                    if (input != null && !Gestures.Matches(input)) return;
                    if (traceResize || result == null ||
                        result.Status != StickyUiCommandStatus.Handled)
                        DisplayDiagnostics.Trace("DockTargetCompleted",
                            "note=" + target.NoteId +
                            " status=" + (result == null ? "null" :
                                result.Status.ToString()) +
                            " seq=" + (result == null ? "-" :
                                result.Sequence.ToString()));
                    if (result != null && result.Status ==
                        StickyUiCommandStatus.Handled)
                        _workspace.ApplyHostedStickySnapshot(result.Snapshot,
                            result.Sequence, false, result.Facts, result.Topology);
                    else
                    {
                        ClearHostedDockResizeSessionIfMember(target.NoteId);
                        StickyWorkspace.ReportHostedStickyCommandFailure(
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
            StickyNoteData parent = _workspace.Notes.Find(target.ParentNoteId);
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
                    if (factsById == null || !factsById.TryGetValue(note.Id, out facts)) return false;
                    heights.Add(facts.Height);
                }
            }
            foreach (StickyNoteData note in BuildDockChainOrder(sourceSeed))
            {
                if (note != null && note.Visible && seen.Add(note.Id))
                {
                    DockWindowFacts facts;
                    if (factsById == null || !factsById.TryGetValue(note.Id, out facts)) return false;
                    heights.Add(facts.Height);
                }
            }
            DockWindowFacts rootFacts;
            return factsById != null && factsById.TryGetValue(targetOrder[0].Id, out rootFacts) &&
                StickyDockOperations.IsDockCoordinateRangeSafe(rootFacts.Y,
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

        private DockResizeSession CaptureHostedResizeSession(StickyUiEvent value, DockResizeKind kind)
        {
            if (_synchronizingDockLayout || _movingDockGroup || Interaction.IsActive ||
                value == null || !_workspace.IsCurrentHostedGeometryEvent(value)) return null;
            StickyNoteData seed = _workspace.Notes.Find(value.NoteId);
            if (seed == null || !seed.Visible) return null;
            List<WindowFacts> facts = new List<WindowFacts>();
            foreach (StickyNoteData note in BuildDockChainOrder(seed))
                facts.Add(String.Equals(note.Id, value.NoteId, StringComparison.OrdinalIgnoreCase)
                    ? value.Facts : _workspace.Placement.GetEffective(note.Id));
            return DockResizeSession.TryStart(kind, value.NoteId, facts, BuildDockChainOrderIncludingHidden(seed), value.Input);
        }

        internal void BeginHostedStickyDockResize(StickyUiEvent value, DockResizeKind kind)
        {
            DockResizeSession next = CaptureHostedResizeSession(value, kind);
            if (next == null) return;
            CancelHostedDockRestores(value.NoteId);
            if (next.MatchesMembers(BuildDockChainOrder(_workspace.Notes.Find(value.NoteId)))) Gestures.TryBeginResize(next);
        }

        internal void ResizeHostedStickyDock(StickyUiEvent value)
        {
            DockResizeSession session = Gestures.Resize;
            if (session == null || !session.IsResizing ||
                !String.Equals(session.SourceNoteId, value.NoteId, StringComparison.OrdinalIgnoreCase)) return;
            if (_synchronizingDockLayout || _movingDockGroup || Interaction.IsActive ||
                !session.MatchesMembers(BuildDockChainOrder(_workspace.Notes.Find(value.NoteId))))
            {
                ClearHostedDockResizeSession(session);
                return;
            }
            bool superseded = session.Mailbox.HasPending;
            bool post;
            StickyFactsReceiver.Update update;
            if (!_workspace.Facts.TryPrepare(new DockBatchMemberResult(value.NoteId, value.Sequence,
                    value.Facts, null), value.Topology, out update) ||
                !session.QueueLive(value, out post)) return;
            // WM_SIZING's requested rectangle is a target; accept actual HWND facts.
            update.Commit();
            if (superseded)
                DisplayDiagnostics.Trace("DockResizeLiveSuperseded", "note=" + value.NoteId + " kind=" + session.Kind);
            if (post)
            {
                DisplayDiagnostics.Trace("DockResizeFrame",
                    "note=" + value.NoteId + " kind=" + session.Kind + " width=" + value.Width + " height=" + value.Height);
                _workspace.Host.PostLatestResizeBatch(session.Mailbox,
                    result => OnResizeLiveBatchApplied(session, result), _workspace.Context);
            }
        }

        private void OnResizeLiveBatchApplied(DockResizeSession session,
            StickyUiCommandResult result)
        {
            if (!ReferenceEquals(Gestures.Resize, session) || !session.IsResizing) return;
            DockBatchResult batch = result != null && result.Status == StickyUiCommandStatus.Handled
                ? result.DockBatchResult : null;
            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            List<StickyFactsReceiver.Update> updates;
            if (CanAcceptResizeBatch(session, batch, topology, out updates))
                ApplyResizeBatchCanonical(batch, topology, false, session.Kind, updates);
        }

        // One preflight for identity, topology, live membership and both facts
        // watermarks. No effect or canonical mutation occurs before it passes.
        private bool CanAcceptResizeBatch(DockResizeSession session,
            DockBatchResult batch, DisplayTopologySnapshot topology,
            out List<StickyFactsReceiver.Update> updates)
        {
            updates = null;
            if (topology == null || session.TopologyGeneration != topology.Generation ||
                !session.HasExpectedFollowers(batch) ||
                !session.MatchesMembers(BuildDockChainOrder(_workspace.Notes.Find(session.SourceNoteId)))) return false;
            updates = new List<StickyFactsReceiver.Update>(batch.Members.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DockBatchMemberResult member in batch.Members)
            {
                StickyFactsReceiver.Update update;
                if (member == null || !seen.Add(member.NoteId) ||
                    !_workspace.Facts.TryPrepare(member, topology, out update)) return false;
                updates.Add(update);
            }
            return true;
        }

        private bool ApplyResizeBatchCanonical(DockBatchResult batch,
            DisplayTopologySnapshot topology, bool persist, DockResizeKind kind,
            List<StickyFactsReceiver.Update> updates)
        {
            List<WindowPlacementPreference> preferences = null;
            if (persist && kind == DockResizeKind.Horizontal)
            {
                preferences = new List<WindowPlacementPreference>(batch.Members.Count);
                foreach (DockBatchMemberResult member in batch.Members)
                {
                    WindowPlacementPreference preference;
                    if (!StickyResizePreferences.TryBuild(_workspace.Notes.Find(member.NoteId), member.Facts,
                        topology, kind, false, out preference)) return false;
                    preferences.Add(preference);
                }
            }
            int index = 0;
            foreach (StickyFactsReceiver.Update update in updates)
            {
                DockBatchMemberResult member = update.Member;
                StickyNoteData canonical = update.Canonical;
                update.CommitGeometry();
                if (preferences != null)
                {
                    WindowPlacementPreference preference = preferences[index];
                    StickyPlacementRules.TryCommitPreferred(canonical, preference,
                        PlacementReason.UserResizeCommit);
                    _workspace.Placement.MarkUserPlacementCommit(member.NoteId);
                }
                index++;
            }
            if (persist) _workspace.Notes.SaveAsync();
            _workspace.RefreshMenuText();
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

        private void RestoreDockOriginalLocations(string seedNoteId)
        {
            StickyNoteData seed = _workspace.Notes.Find(seedNoteId);
            if (seed == null || Interaction.BaselineFacts.Count == 0)
                return;
            List<StickyNoteData> component = BuildDockChainOrder(seed);
            List<DockLayoutTarget> targets = new List<DockLayoutTarget>();
            foreach (StickyNoteData note in component)
            {
                DockWindowFacts original;
                if (Interaction.BaselineFacts.TryGetValue(note.Id,
                    out original))
                    targets.Add(original.ToTarget(original.X, original.Y));
            }
            _movingDockGroup = true;
            try { ApplyDockTargets(targets, null, Gestures.Input); }
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

        private StickyNoteData FindActiveDockTail(StickyNoteData seed)
        {
            List<StickyNoteData> activeNotes = new List<StickyNoteData>();
            foreach (string noteId in Interaction.MemberIds)
            {
                StickyNoteData note = _workspace.Notes.Find(noteId);
                if (note != null) activeNotes.Add(note);
            }
            return StickyDockOperations.FindActiveDockTail(_workspace.Notes.InStorageOrder,
                activeNotes, seed);
        }

        private void ShowSplitGuide(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            ClearSplitGuide();
            if (source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(
                    FindVisibleDockParentId(source), out parentFacts)
                ? CalculateDockVisualSeam(parentFacts)
                : Rectangle.Empty;
            if (!seam.IsEmpty)
                _workspace.Host.ShowSplitGuide(seam);
        }

        private void UpdateSplitGuide(StickyNoteData source,
            IReadOnlyDictionary<string, DockWindowFacts> factsById)
        {
            if (source == null) return;
            DockWindowFacts parentFacts;
            Rectangle seam = factsById != null &&
                factsById.TryGetValue(
                    FindVisibleDockParentId(source), out parentFacts)
                ? CalculateDockVisualSeam(parentFacts)
                : Rectangle.Empty;
            if (!seam.IsEmpty)
                _workspace.Host.UpdateSplitGuide(seam);
        }

        internal void ClearSplitGuide()
        {
            _workspace.Host.ClearSplitGuide();
        }

        private void UpdateDockPreview(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            DockTarget target = FindDockTarget(source, factsById);
            StickyNoteData parent = target == null ? null :
                _workspace.Notes.Find(target.ParentNoteId);
            StickyNoteData child = target == null ? null :
                _workspace.Notes.Find(target.ExistingChildNoteId);
            DockWindowFacts parentFacts;
            Rectangle seam = parent != null && factsById != null &&
                factsById.TryGetValue(parent.Id, out parentFacts)
                ? CalculateDockVisualSeam(parentFacts)
                : Rectangle.Empty;

            _workspace.Host.UpdateDockPreview(
                parent == null ? String.Empty : parent.Id,
                child == null ? String.Empty : child.Id,
                seam);
        }

        internal static Rectangle CalculateDockVisualSeam(
            DockWindowFacts facts)
        {
            return facts == null ? Rectangle.Empty : new Rectangle(facts.X,
                facts.Y + facts.Height - 3, facts.Width, 6);
        }

        private DockTarget FindDockTarget(StickyNoteData source,
            IDictionary<string, DockWindowFacts> factsById)
        {
            if (source == null) return null;
            if (!String.IsNullOrEmpty(FindVisibleDockParentId(source)) &&
                !Interaction.Detached) return null;
            HashSet<string> activeIds = new HashSet<string>(
                Interaction.MemberIds, StringComparer.OrdinalIgnoreCase);
            List<StickyNoteData> all = _workspace.Notes.GetAll();
            HashSet<string> existingIds = new HashSet<string>(all.ConvertAll(note => note.Id),
                StringComparer.OrdinalIgnoreCase);
            if (!activeIds.IsSubsetOf(existingIds)) return null;
            DockWindowFacts sourceFacts;
            if (factsById == null ||
                !factsById.TryGetValue(source.Id, out sourceFacts))
                return null;
            var candidates = new List<DockWindowTarget>();
            foreach (StickyNoteData candidate in all)
            {
                if (candidate == null || !candidate.Visible ||
                    String.Equals(candidate.Id, source.Id,
                        StringComparison.OrdinalIgnoreCase) ||
                    activeIds.Contains(candidate.Id))
                    continue;
                DockWindowFacts candidateFacts;
                if (!factsById.TryGetValue(candidate.Id,
                    out candidateFacts) || !candidateFacts.Visible) continue;
                candidates.Add(new DockWindowTarget(candidate.Id, new PhysicalRect(candidateFacts.X,
                    candidateFacts.Y, candidateFacts.Width, candidateFacts.Height)));
            }
            string parentId = StickyDockOperations.FindSnapTarget(new DockWindowTarget(source.Id,
                new PhysicalRect(sourceFacts.X, sourceFacts.Y, sourceFacts.Width, sourceFacts.Height)), candidates, 20);
            DockTarget best = parentId == null ? null : new DockTarget {
                ParentNoteId = parentId, ExistingChildNoteId = FindDockChild(parentId, activeIds) };
            if (best != null && !CanSafelyCombineDockComponents(best, source,
                factsById))
                return null;
            return best;
        }

        private string FindDockChild(string parentId, HashSet<string> ignoredIds)
        {
            StickyNoteData child = StickyDockGroups.GetVisibleNeighbor(_workspace.Notes.InStorageOrder, _workspace.Notes.Find(parentId), 1);
            return child == null || (ignoredIds != null && ignoredIds.Contains(child.Id))
                ? String.Empty : child.Id;
        }

        internal static bool CanDockBelow(Rectangle moving, Rectangle target,
            int threshold)
        {
            return StickyDockOperations.CanDockBelow(moving.Left, moving.Top,
                moving.Width, moving.Height, target.Left, target.Top,
                target.Width, target.Height, threshold);
        }

        internal void ClearDockPreview()
        {
            _workspace.Host.ClearDockPreview();
        }

        private void ShowTransientDockPulse(
            Rectangle seam, Color color)
        {
            _workspace.Host.ShowTransientDockPulse(seam, color);
        }

        private readonly DockRestoreOperations _dockRestores = new DockRestoreOperations();

        internal void ExpandAndTileAllStickyNotesToPetScreen()
        {
            if (DeferDockMutation(null, ExpandAndTileAllStickyNotesToPetScreen)) return;
            CancelHostedDockRestores();
            ClearHostedDockResizeSession();
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
            _movingDockGroup = true;
            try
            {
                foreach (DockLayoutTarget target in targets)
                {
                    StickyNoteData note = _workspace.Notes.Find(target.NoteId);
                    if (note == null) continue;
                    _workspace.ShowHostedSticky(note, false, false);
                    ApplyDockTarget(target, null);
                }
            }
            finally { _movingDockGroup = false; }
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

        internal void InvalidateDockPlansForTopologyChange(
            DisplayTopologySnapshot snapshot)
        {
            Gestures.RenewPlans();
            ClearHostedDockResizeSession();
            if (Interaction.IsActive)
            {
                long epoch = Interaction.IsFinalizing
                    ? Interaction.RestartFinalizing(snapshot.Generation)
                    : Interaction.BeginRebase(snapshot.Generation);
                if (epoch > 0) _workspace.Host.SetCurrentDockInteractionEpoch(epoch);
            }
            DisplayDiagnostics.Trace("DockPlanStale",
                "topology invalidated generation=" + snapshot.Generation);
        }

        internal void ResumeDockDragAfterTopologyChange(
            DisplayTopologySnapshot snapshot)
        {
            if (snapshot == null || String.IsNullOrEmpty(Interaction.SourceNoteId) ||
                Interaction.MemberIds.Count == 0 || !Interaction.IsActive)
                return;
            string sourceId = Interaction.SourceNoteId;
            if (Interaction.IsFinalizing)
            {
                StartDockFinalization(_workspace.Notes.Find(sourceId),
                    _workspace.Notes.Find(Interaction.RemainderNoteId));
                return;
            }
            long epoch = Interaction.Epoch;
            if (!Interaction.Matches(epoch, snapshot.Generation,
                DockInteractionPhase.Rebasing)) return;
            string[] expectedIds = Interaction.CopyMemberIds();
            _workspace.PostHostedStickyCommand(StickyUiCommand.CaptureDockFacts(
                expectedIds, snapshot, epoch, Gestures.Input), delegate(StickyUiCommandResult result)
                {
                    if (!Interaction.Matches(epoch, snapshot.Generation,
                        DockInteractionPhase.Rebasing) ||
                        !_workspace.IsTopologyCurrent(snapshot))
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
                    if (!Interaction.PreviewFacts.TryGetValue(sourceId,
                        out sourceRuntime) || sourceRuntime == null) return;
                    // Rebase cancels this split hold; a fresh mouse-down is
                    // required. Preserve the original gesture provenance.
                    ClearSplitGuide();
                    Interaction.RecordMove(sourceRuntime);
                    if (!Interaction.TryEnterDragging(epoch,
                        snapshot.Generation)) return;
                    StickyNoteData seed = _workspace.Notes.Find(sourceId);
                    DockPlacementPlan plan = PlanDockPlan(seed, sourceFacts,
                        snapshot, epoch);
                    if (plan != null && plan.WindowTargets.Count > 1)
                    {
                        ApplyLiveDockPlan(plan);
                        Interaction.RememberTargets(PlanToDockTargets(plan));
                    }
                });
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
                if (!String.IsNullOrEmpty(Interaction.SourceNoteId) &&
                    group.Exists(delegate(StickyNoteData member)
                    {
                        return String.Equals(member.Id, Interaction.SourceNoteId,
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

            bool merged = Interaction.PendingMerge != null;
            if (merged && !Interaction.PendingMerge.TryCommit(_workspace.Notes.InStorageOrder))
            {
                TraceDockCommitRejected("membership changed before final commit");
                return;
            }
            _lastAppliedDockPlanSequence = Math.Max(
                _lastAppliedDockPlanSequence, expectedPlanSequence);
            foreach (DockCommitCandidate candidate in candidates)
            {
                StickyNoteData canonical = candidate.Update.Canonical;
                candidate.Update.Commit();
                StickyPlacementRules.TryCommitPreferred(canonical, candidate.Preference,
                    PlacementReason.DockCommit);
            }
            if (merged)
            {
                List<StickyNoteData> group = BuildDockChainOrderIncludingHidden(seed);
                bool topMost = group[0].AlwaysOnTop;
                foreach (StickyNoteData member in group) member.AlwaysOnTop = topMost;
            }
            _workspace.Notes.SaveAsync();
            foreach (DockCommitCandidate candidate in candidates)
                _workspace.Placement.MarkUserPlacementCommit(
                    candidate.Update.Member.NoteId);
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
            if (expectedTopology == null || !_workspace.IsTopologyCurrent(expectedTopology))
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
                StickyFactsReceiver.Update update;
                if (!_workspace.Facts.TryPrepare(member, expectedTopology, out update) ||
                    member.Facts.Dpi != batch.TargetDpi ||
                    !String.Equals(member.Facts.RuntimeGdiName, targetSurface.RuntimeGdiName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    rejection = "final batch member facts are stale or mismatched";
                    return false;
                }
                StickyNoteData canonical = update.Canonical;
                WindowPlacementPreference preference;
                if (canonical == null ||
                    !StickyPlacementRules.TryBuildPreferredPlacement(
                        member.Facts, expectedTopology,
                        canonical.PreferredPlacement?.PreferredTargetKey,
                        out preference))
                {
                    rejection = "final batch preference unavailable";
                    return false;
                }
                candidates.Add(new DockCommitCandidate(update, preference));
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
                    _acceptedLocalDockGestures.Add(
                        commit.GestureId);
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
        internal bool DeferDockMutation(string noteId, Action action)
        {
            DockMutationQueue owner = FindDockMutationOwner(noteId);
            if (owner == null) return false;
            return owner.Defer(null, null, action);
        }

        private DockMutationQueue FindDockMutationOwner(string noteId)
        {
            StickyNoteData note = noteId == null ? null : _workspace.Notes.Find(noteId);
            string groupId = note == null ? null : note.DockGroupId;
            DockMutationQueue header = Interaction.Mutations;
            if (header != null && header.Contains(noteId, groupId)) return header;
            DockMutationQueue resize = Gestures.Resize == null ? null : Gestures.Resize.Mutations;
            return resize != null && resize.Contains(noteId, groupId) ? resize : null;
        }

        private void RunDeferredDockMutations(Action[] actions)
        {
            // The caller retires its owner and mailbox before these actions
            // can re-enter Pet code or begin another restore/gesture.
            foreach (Action action in actions)
            {
                if (_workspace.IsDisposed) return;
                try { action(); }
                catch (Exception error) { _workspace.ShowStickyWindowFailure("Dock 后续操作", error); }
            }
        }

        internal void CancelDockFinalizationIfMember(string noteId)
        {
            StickyNoteData note = _workspace.Notes.Find(noteId);
            DockMutationQueue final = Interaction.Mutations;
            if (final != null && final.Contains(noteId, note == null ? null : note.DockGroupId))
                ResetDockDragState();
        }

        internal void ClearHostedDockResizeSession(DockResizeSession expected = null)
        {
            RunDeferredDockMutations(Gestures.FinishResize(expected));
        }

        internal void ClearHostedDockResizeSessionIfMember(string noteId)
        {
            if (Gestures.Resize != null && Gestures.Resize.Contains(noteId))
                ClearHostedDockResizeSession();
        }

        internal void CompleteHostedStickyDockResize(StickyUiEvent value)
        {
            if (value == null || value.Snapshot == null || Interaction.IsActive ||
                !_workspace.IsCurrentHostedGeometryEvent(value)) return;
            StickyFactsReceiver.Update update;
            if (!_workspace.Facts.TryPrepare(new DockBatchMemberResult(value.NoteId, value.Sequence,
                value.Facts, value.Snapshot), value.Topology, out update)) return;
            StickyNoteData source = update.Canonical;
            if (source == null || !source.Visible) return;
            DockResizeKind kind = value.Kind == StickyUiEventKind.DockHorizontalResizeCompleted
                ? DockResizeKind.Horizontal : DockResizeKind.Divider;
            WindowPlacementPreference sourcePreference;
            if (!StickyResizePreferences.TryBuild(source, value.Facts, value.Topology, kind, true, out sourcePreference)) return;
            DockResizeSession session = Gestures.Resize;
            if (session != null && (session.Kind != kind || !session.IsResizing ||
                !String.Equals(session.SourceNoteId, value.NoteId, StringComparison.OrdinalIgnoreCase))) return;
            // A topology barrier retires the old gesture. A current completion
            // may settle from freshly accepted facts, never from its old baseline.
            if (session == null)
            {
                session = CaptureHostedResizeSession(value, kind);
                if (session != null && !Gestures.TryBeginResize(session)) return;
            }
            if (session == null || !session.MatchesMembers(BuildDockChainOrder(source)))
            {
                CommitResizeSourceFinal(update, sourcePreference);
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
            CommitResizeSourceFinal(update, sourcePreference);
            PostResizeFinal(session, final);
        }

        private void PostResizeFinal(DockResizeSession session,
            DockResizeBatch final)
        {
            try
            {
                _workspace.Host.PostFinalResizeBatch(session.Mailbox, final,
                    result => OnResizeFinalBatchApplied(session, final, result), _workspace.Context);
            }
            catch
            {
                ClearHostedDockResizeSession(session);
                throw;
            }
        }

        private void OnResizeFinalBatchApplied(DockResizeSession session,
            DockResizeBatch expected, StickyUiCommandResult result)
        {
            if (!ReferenceEquals(Gestures.Resize, session) || !session.IsCurrentFinal(expected)) return;
            DockBatchResult batch = result != null && result.Status == StickyUiCommandStatus.Handled
                ? result.DockBatchResult : null;
            DisplayTopologySnapshot topology = _workspace.CurrentTopologySnapshot();
            List<StickyFactsReceiver.Update> updates;
            if (!CanAcceptResizeBatch(session, batch, topology, out updates))
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
                if (!ApplyResizeBatchCanonical(batch, topology, true, session.Kind, updates))
                    DisplayDiagnostics.Trace("DockResizeFinalRejected", "reason=preference note=" + session.SourceNoteId);
            }
            finally { ClearHostedDockResizeSession(session); }
        }

        private void CommitResizeSourceFinal(StickyFactsReceiver.Update update,
            WindowPlacementPreference preference)
        {
            _synchronizingDockLayout = true;
            try
            {
                update.Commit();
                StickyPlacementRules.TryCommitPreferred(update.Canonical, preference,
                    PlacementReason.UserResizeCommit);
                _workspace.Placement.MarkUserPlacementCommit(update.Member.NoteId);
                _workspace.Notes.SaveAsync();
            }
            finally { _synchronizingDockLayout = false; }
            _workspace.RefreshMenuText();
        }

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
                ClearHostedDockResizeSessionIfMember(member.Id);
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
