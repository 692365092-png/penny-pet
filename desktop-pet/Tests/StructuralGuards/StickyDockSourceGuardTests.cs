using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void Autosave_DoesNotRefreshSideTabs()
        {
            string source = SourceGuardText.ReadStickyWorkflowSource();
            string handler = Between(source,
                "if (value.Kind == StickyUiEventKind.SnapshotChanged)",
                "if (value.Kind == StickyUiEventKind.Closed)");
            string apply = RawSource.SliceMethod(source,
                "private bool ApplyHostedStickyEvent");

            Assert.IsTrue(handler.Contains("ApplyHostedStickyEvent(") &&
                apply.Contains("if (persist) Notes.SaveAsync();"),
                "NoteChanged must persist note data.");
            Assert.IsTrue(apply.Contains("RefreshMenuText();"),
                "NoteChanged must refresh menu text.");
            Assert.IsTrue(apply.Contains("if (tabsChanged) RefreshNoteTabs();") &&
                apply.Contains("RefreshNoteTabs();"),
                "Content autosave must refresh tabs only for visibility or hidden-title changes.");
        }

        [TestMethod]
        public void StickyUiRegistry_CloseAllPreflightsImeAndSuppressesEvents()
        {
            string host = ReadSource("StickyUiHost.cs");
            string closeAll = RawSource.SliceMethod(host,
                "private StickyUiCommandResult CloseAllSessions()");
            int preflight = closeAll.IndexOf(
                "session.IsImeCompositionActive",
                StringComparison.Ordinal);
            int batch = closeAll.IndexOf("session.SetEventsSuppressed(true)",
                StringComparison.Ordinal);

            Assert.IsTrue(preflight >= 0 && batch > preflight &&
                closeAll.Contains("session.FlushAndCaptureFinal()") &&
                closeAll.Contains("StickyUiFinalSnapshot"),
                "CloseAll must preflight every IME before a quiet final batch.");
        }

        [TestMethod]
        public void StickyUiRegistry_DeletesOnlyAfterHandledCloseAndForwardsRequests()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string dock = SourceGuardText.ReadStickyWorkflowSource();
            int handled = dock.IndexOf(
                "result.Status != StickyUiCommandStatus.Handled",
                StringComparison.Ordinal);
            int remove = dock.IndexOf("Notes.Remove(note)",
                StringComparison.Ordinal);

            Assert.IsTrue(commands.Contains("DeleteRequested") &&
                commands.Contains("NewNoteRequested") &&
                commands.Contains("NewTodoRequested") &&
                commands.Contains("NewScheduleRequested") &&
                session.Contains("_window.DeleteRequested +=") &&
                session.Contains("RaiseRequest("),
                "Window-level application requests must cross as typed events.");
            Assert.IsTrue(handled >= 0 && remove > handled,
                "Canonical deletion must happen only after handled close.");
        }

        [TestMethod]
        public void StickyUiHosted_FinalArchitectureHasSingleWindowExecutor()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string form = ReadSource("PetForm.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string dock = SourceGuardText.ReadStickyWorkflowSource();
            string persistence = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string reminder = ReadSource("PetReminderWindowsCoordinator.cs");
            string menu = ReadSource("PetMenuActions.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string petOwned = startup + form + coordinator + dock +
                persistence + reminder + menu;

            Assert.IsTrue(startup.Contains("QueueStartupStickyRestore(note)") &&
                coordinator.Contains("StartHostedSticky(") &&
                coordinator.Contains("Host.PostStartupRestore(command") &&
                host.Contains("Dictionary<string, StickyWindowSession> _sessions") &&
                host.Contains("handler(command)") &&
                session.Contains("new StickyNoteWindow("),
                "Startup and creation must route through the hosted session executor; startup may use its dedicated budgeted transport.");
            Assert.IsFalse(petOwned.Contains("_noteWindows") ||
                petOwned.Contains("GetOrCreateStickyNoteWindow") ||
                petOwned.Contains("FallBackHostedStickyToLegacy") ||
                petOwned.Contains("RestoreStickyDockComponent") ||
                petOwned.Contains("new StickyNoteWindow("),
                "PetForm must not retain a legacy Sticky Window executor.");
            Assert.IsFalse(host.Contains("StickyFeature") ||
                host.Contains("IsTodoList") || host.Contains("IsSchedule"),
                "The host must own sessions, not canonical persistence or content modes.");
        }

        [TestMethod]
        public void DockTypedProtocol_HasBoundsCommandAndEvents()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(commands.Contains("SetBounds") &&
                commands.Contains("StickyUiBounds") &&
                commands.Contains("BoundsChanged"),
                "Dock protocol must expose typed bounds command/event data.");
            Assert.IsTrue(host.Contains("StickyUiCommandKind.SetBounds") &&
                session.Contains("StickyUiEventKind.BoundsChanged"),
                "StickyUiHost must execute and report typed bounds changes.");
        }

        [TestMethod]
        public void StickyTypedProtocol_ProductionUsesNamedPayloadFactories()
        {
            string protocol = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string windowCoordinator = SourceGuardText.ReadStickyWorkflowSource();
            string dockCoordinator = ReadSource("Features/StickyNotes/StickyDockController.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(protocol.Contains("StickyUiCommand Create(") &&
                protocol.Contains("StickyUiCommand SetBounds(") &&
                protocol.Contains("StickyUiCommand SetDockResizeRole(") &&
                protocol.Contains("StickyUiEvent FromSnapshot(") &&
                protocol.Contains("StickyUiEvent Signal(") &&
                protocol.Contains("StickyUiEvent HorizontalResize(") &&
                protocol.Contains("StickyUiEvent DividerResize("),
                "Protocol must expose factories for its payload shapes.");
            Assert.IsFalse(windowCoordinator.Contains(
                    "new StickyUiCommand(") ||
                dockCoordinator.Contains("new StickyUiCommand(") ||
                session.Contains("new StickyUiEvent("),
                "Production call sites must not guess long protocol payloads.");
        }

        [TestMethod]
        public void DockTypedProtocol_ForwardsHeaderDragAndResizeEvents()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(commands.Contains("HeaderDragStarted") &&
                commands.Contains("HeaderDragMoved") &&
                commands.Contains("HeaderDragCompleted") &&
                commands.Contains("DockHorizontalResizing") &&
                commands.Contains("DockDividerResizeStarted") &&
                commands.Contains("DockDividerResizing") &&
                commands.Contains("DockDividerResizeCompleted") &&
                commands.Contains("SetDockResizeRole") &&
                commands.Contains("CloseRequested"),
                "Dock protocol must expose header drag and resize event kinds.");
            Assert.IsTrue(session.Contains("_window.HeaderDragStarted +=") &&
                session.Contains("_window.HeaderDragMoved +=") &&
                session.Contains("_window.HeaderDragCompleted +=") &&
                session.Contains("_window.DockHorizontalResizing +=") &&
                session.Contains("_window.DockDividerResizeStarted +=") &&
                session.Contains("_window.DockDividerResizing +=") &&
                session.Contains("_window.DockDividerResizeCompleted +=") &&
                session.Contains("EmitSnapshot(") &&
                host.Contains("StickyUiCommandKind.SetDockResizeRole") &&
                session.Contains("role.SplitBottom") &&
                session.Contains("role.DividerMinimumHeight") &&
                session.Contains("role.DividerMaximumHeight") &&
                session.Contains("_window.DockDividerResizeActive") &&
                session.Contains("if (_applyingBounds) return;") &&
                session.Contains("StickyUiEventKind.CloseRequested"),
                "StickyUiHost must forward dock drag/resize events.");
        }

        [TestMethod]
        public void StickyRecovery_ExpandsAllThroughOwnedEffectBoundaries()
        {
            string menu = ReadSource("PetContextMenu.cs");
            string manager = ReadSource(
                "Features/StickyNotes/StickyNotes.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string action = Between(coordinator,
                "internal void ExpandAndTileAllStickyNotesToPetScreen",
                "internal static List<DockLayoutTarget>");
            string preparation = Between(coordinator,
                "PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,",
                "private static List<Rectangle> CalculateStickyRecoveryLayout");

            Assert.IsTrue(manager.Contains("桌面整理") &&
                manager.Contains("收起全部") &&
                manager.Contains("展开全部") &&
                manager.Contains("平铺到当前屏幕") &&
                coordinator.Contains("ExpandAndTileAllStickyNotesToPetScreen") &&
                !menu.Contains("Menu.Items.Add(RecoverWindowsItem)") &&
                !menu.Contains("Menu.Items.Add(BackupNotesItem)") &&
                !menu.Contains("Menu.Items.Add(ImportNotesItem)") &&
                !menu.Contains("Menu.Items.Add(RestoreNotesItem)"),
                "Desktop recovery actions must live in the management console.");
            Assert.IsTrue(action.Contains("ShowHostedSticky(note, false, false)") &&
                action.Contains("ApplyDockTarget(target, null)") &&
                !action.Contains("ShowStickyNote("),
                "Every note must use the hosted effect edge.");
            Assert.IsFalse(action.Contains("GetOrCreateStickyNoteWindow") ||
                action.Contains("seed.Visible"),
                "Recovery must neither skip hidden notes nor use a universal legacy route.");
            Assert.IsTrue(preparation.Contains(
                "StickyDockGroups.ClearMembership(note)") &&
                preparation.Contains("note.Visible = true") &&
                preparation.Contains("cascadeStep"),
                "Preparation must detach, expand, and independently cascade every note.");
        }

        [TestMethod]
        public void DockParticipantEligibility_DoesNotDependOnStickySubtypeOrExecutor()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string operations = ReadSource(
                "Core/StickyNotes/StickyDockOperations.cs");
            string geometry = ReadSource(
                "Core/StickyNotes/StickyDockGeometry.cs");
            string gate = Between(coordinator,
                "private DockTarget FindDockTarget",
                "private string FindDockChild");

            Assert.IsTrue(gate.Contains("activeIds.IsSubsetOf(existingIds)"),
                "Removed active members must invalidate target selection.");
            Assert.IsFalse(gate.Contains("!note.IsTodoList") ||
                gate.Contains("!note.IsSchedule"),
                "Todo and Schedule must be allowed to dock with ordinary notes.");
            Assert.IsFalse(gate.Contains("IsHostedSticky"),
                "Dock participant eligibility must not depend on live session membership.");
            Assert.IsFalse(gate.Contains("ReminderUtcTicks"),
                "Reminder is not a sticky subtype and must not affect Dock eligibility.");
            Assert.IsFalse(operations.Contains("IsTodoList") ||
                operations.Contains("IsSchedule") ||
                geometry.Contains("IsTodoList") ||
                geometry.Contains("IsSchedule"),
                "Core Dock rules must remain independent of Sticky content mode.");
        }

        [TestMethod]
        public void DrtCloseout_GeometryEventsUseFactsAsGeometryTruth()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();

            Assert.IsTrue(session.Contains(
                    "WindowsWindowFactsReader.Capture(hwnd, _noteId,") &&
                session.Contains("_topology == null ? 0 : _topology.Generation") &&
                session.Contains("facts, _topology)"),
                "Facts must be captured with the Pet-owned topology generation.");
            string receiver = ReadSource("Features/StickyNotes/StickyFactsReceiver.cs");
            Assert.IsTrue(coordinator.Contains("Facts.TryApplySnapshot(") &&
                receiver.Contains("canonical.X = facts.PhysicalBounds.Left") &&
                !receiver.Contains("LegacyPlacement") &&
                receiver.Contains("snapshot.ApplyContentTo(canonical)"),
                "Geometry events preserve actual pixels without rewriting v10 file inputs.");

            string dragHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.HeaderDragStarted ||",
                "if (value.Kind == StickyUiEventKind.BoundsChanged)");
            Assert.IsTrue(dragHandler.Contains(
                    "DockWindowFacts.FromWindowFacts(") &&
                !dragHandler.Contains("ApplyHostedStickySnapshot"),
                "Drag geometry must flow from facts-derived canonical state.");
            string boundsHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.BoundsChanged)",
                "if (value.Kind == StickyUiEventKind.DockDividerResizeStarted ||");
            Assert.IsTrue(boundsHandler.Contains(
                    "ApplyHostedStickyEvent(value, false)") &&
                !boundsHandler.Contains("ApplyHostedStickySnapshot"),
                "BoundsChanged must not apply the full snapshot.");
        }

        [TestMethod]
        public void Drt6_OnlyUserReasonsCommitPreferred()
        {
            string rules = ReadSource(
                "Core/StickyNotes/StickyPlacementRules.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();

            Assert.IsTrue(rules.Contains("internal enum PlacementReason") &&
                rules.Contains("CanCommitPreferred(PlacementReason reason)") &&
                rules.Contains("case PlacementReason.UserMoveCommit:") &&
                rules.Contains("case PlacementReason.DockCommit:"),
                "Preferred commit must be gated by placement reason.");
            Assert.IsTrue(coordinator.Contains(
                    "PlacementReason.UserResizeCommit") &&
                coordinator.Contains("PlacementReason.Spawn") &&
                coordinator.Contains("PlacementReason.ExpandAndTile"),
                "Pet must only commit preferred at user-gesture call sites.");
        }

        [TestMethod]
        public void Drt7_RehomeSkipsDockMembersAndUsesCorePolicy()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string reconcile = Between(coordinator,
                "internal void HandleStickyTopologyChanged",
                "private void CompleteTemporaryRehome");

            Assert.IsTrue(reconcile.Contains(
                    "if (!String.IsNullOrEmpty(note.DockGroupId)) continue;") &&
                reconcile.Contains(
                    "FallbackDisplayPolicy.ResolveFallbackSurface("),
                "DRT-7 must rehome standalone notes only through the Core fallback policy.");
            Assert.IsFalse(reconcile.Contains(
                    "TryCommitPreferred") ||
                reconcile.Contains("PreferredPlacement ="),
                "Temporary rehome must never commit or rewrite the durable preferred.");
        }

        [TestMethod]
        public void DrtCloseout_ReprojectIsTransactionalAndReturnsFacts()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();

            string rollback = Between(session,
                "private void RollbackReproject",
                "internal DockBatchMemberResult CaptureDockMember");
            Assert.IsTrue(rollback.Contains(
                    "SetWindowPosExact(new PhysicalRect(") &&
                rollback.Contains("previousBounds") &&
                rollback.Contains("if (wasVisible) _placementExecutor.Show()"),
                "A failed reproject must restore previous bounds and visibility.");
            string apply = Between(coordinator,
                "private bool ApplyReprojectResult",
                "private static bool TryBuildPreference");
            Assert.IsTrue(apply.Contains("Facts.TryPrepare(") && apply.Contains("update.Commit()"),
                "A reproject result must update geometry and Effective from actual facts.");
        }

        [TestMethod]
        public void CaptureDockMemberDoesNotCallCaptureCanonicalPlacement()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string capture = Between(session,
                "internal DockBatchMemberResult CaptureDockMember(",
                "private WindowFacts CaptureFactsWith");
            string helper = Between(session,
                "private StickyNoteUiSnapshot CaptureSnapshot",
                "private void Raise");
            Assert.IsTrue(capture.Contains(
                    "CaptureSnapshot()") &&
                helper.Contains("StickyNoteUiSnapshot.Capture("));
            Assert.IsFalse(capture.Contains("CaptureCanonicalPlacement") ||
                helper.Contains("CaptureCanonicalPlacement") ||
                helper.Contains("WindowsDisplayResolver"));
        }

        [TestMethod]
        public void ReprojectRollbackRestoresHiddenState()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string rollback = Between(session,
                "private void RollbackReproject",
                "internal DockBatchMemberResult CaptureDockMember");
            Assert.IsTrue(rollback.Contains(
                    "if (wasVisible) _placementExecutor.Show();") &&
                rollback.Contains("else _window.Hide();"));
        }

        [TestMethod]
        public void Drt910_DockPlanUsesSourceActualWidthForEveryMember()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string plan = Between(coordinator,
                "private DockPlacementPlan PlanDockPlan",
                "private List<DockLayoutTarget> PlanToDockTargets");

            StringAssert.Contains(plan, "StickyPlacementRules.TryBuildLiveDockState(");
            StringAssert.Contains(plan, "Placement.GetEffective(");
            Assert.IsFalse(plan.Contains("LegacyPlacement") || plan.Contains("member.Height"),
                "Live Dock geometry must not fall back to persisted coordinates.");
        }

        [TestMethod]
        public void Drt11_GroupReprojectUsesOneAtomicNativeBatch()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = RawSource.SliceMethod(host,
                "private StickyUiCommandResult ApplyDockGroupReproject(");

            Assert.IsTrue(apply.Contains(
                    "session.TryPrepareDockTargetSurface(") &&
                apply.Contains("DockPlacementPlanner.PlanReproject(") &&
                apply.Contains(
                    "WindowsBatchWindowPlacementExecutor.Apply(handles,") &&
                apply.Contains("CompleteDockTargetDpi(") &&
                apply.Contains("placementApplied"));
            Assert.IsFalse(apply.Contains("SetBounds("),
                "Group topology placement must not degrade to partial moves.");
        }

        [TestMethod]
        public void Drt11_GroupRehomePreservesDurablePreferred()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string apply = RawSource.SliceMethod(coordinator, "private bool TryApplyDockTopologyResult(");

            Assert.IsTrue(apply.Contains("Facts.TryPrepare(") && apply.Contains("update.Commit(forceVisible)") &&
                apply.Contains("Notes.SaveAsync()"));
            Assert.IsFalse(apply.Contains("TryCommitPreferred(") ||
                apply.Contains("PreferredPlacement"),
                "Temporary group geometry must never overwrite preference.");
        }

        [TestMethod]
        public void Drt11_GroupReturnRequiresNoUserMove()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string reconcile = Between(coordinator,
                "private void ReconcileDockGroup(",
                "private static DisplaySurfaceSnapshot FindCommonDockPreferredSurface(");
            string complete = Between(coordinator,
                "private void PostDockGroupTopologyReproject(",
                "private bool TryApplyDockTopologyResult(");

            Assert.IsTrue(reconcile.Contains(
                    "Placement.UserMovedSinceRehome(member.Id)") &&
                reconcile.Contains(
                    "PostDockGroupTopologyReproject(group, snapshot, preferred,") &&
                complete.Contains("MarkReturnedToPreferred(") &&
                complete.Contains("MarkTemporaryRehome("));
        }

        [TestMethod]
        public void Drt11_GroupResultRejectsStaleOrIncompleteGeneration()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string apply = RawSource.SliceMethod(coordinator, "private bool TryApplyDockTopologyResult(");

            Assert.IsTrue(apply.Contains(
                    "!_workspace.IsTopologyCurrent(snapshot)") &&
                apply.Contains("batch.Members.Count != expectedIds.Count") &&
                apply.Contains(
                    "Facts.TryPrepare(member, snapshot,") &&
                apply.Contains("remaining.Count != expectedIds.Count") && apply.Contains("!remaining.Remove(member.NoteId)"));
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void R24_LowFrequencyStructureUsesTypedStickyBarrier()
        {
            string protocol = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string workspace =
                SourceGuardText.ReadStickyWorkflowSource();
            string dock = ReadSource(
                "Features/StickyNotes/StickyDockController.cs");

            Assert.IsTrue(protocol.Contains(
                    "PrepareDockStructure") &&
                workspace.Contains(
                    "StickyUiCommand.PrepareDockStructure("));
            foreach (string signature in new[]
            {
                "private void DeleteStickyNote(StickyNoteData note,",
                "private void CollapseAllStickyNotes()",
                "internal bool BeginHostedStickyExitIfNeeded()",
                "internal void CloseHostedStickyRuntimeForReload("
            })
                Assert.IsTrue(
                    RawSource.SliceMethod(workspace, signature)
                        .Contains("PrepareDockStructure("),
                    signature);
            foreach (string signature in new[]
            {
                "internal void HideStickyNote(",
                "internal void CloseStickyDockNote(",
                "internal void ExpandAndTileAllStickyNotesToPetScreen()",
                "internal bool TryRestoreHostedDockComponent("
            })
                Assert.IsTrue(
                    RawSource.SliceMethod(dock, signature)
                        .Contains("PrepareDockStructure("),
                    signature);

            Assert.IsFalse(workspace.Contains(
                    "DeferDockMutation(") ||
                dock.Contains("DeferDockMutation(") ||
                dock.Contains("DockMutationQueue"));
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void R24_LiveDockHasOneStickyOwnerAndNoPetMailboxFallback()
        {
            string workspace =
                SourceGuardText.ReadStickyWorkflowSource();
            string dock = ReadSource(
                "Features/StickyNotes/StickyDockController.cs");
            string host = ReadSource("StickyUiHost.cs");
            string project = ReadSource(
                "PennyPet.Tests.csproj");

            Assert.IsTrue(host.Contains(
                    "private bool TryHandleLocalDockEvent(") &&
                host.Contains(
                    "StickyDockLocalGestureRuntime"));
            foreach (string retired in new[]
            {
                "BeginStickyDockDrag(",
                "MoveStickyDockDrag(",
                "CompleteStickyDockDrag(",
                "PostLatestDockPlan(",
                "PostFinalDockPlan(",
                "PostLatestResizeBatch(",
                "PostFinalResizeBatch(",
                "DockFrameMailbox",
                "DockGestureOwner",
                "DockInteractionSession",
                "DockResizeSession"
            })
            {
                Assert.IsFalse(workspace.Contains(retired),
                    retired + " workspace");
                Assert.IsFalse(dock.Contains(retired),
                    retired + " controller");
                Assert.IsFalse(host.Contains(retired),
                    retired + " host");
                Assert.IsFalse(project.Contains(retired),
                    retired + " project");
            }
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void R24_NativeFollowerFailureIsBoundedAndVerified()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = RawSource.SliceMethod(host,
                "private bool ApplyLocalDockFollowers(");
            string local = RawSource.SliceMethod(host,
                "private bool TryHandleLocalDockEvent(");

            Assert.AreEqual(1,
                apply.Split(new[]
                {
                    "WindowsBatchWindowPlacementExecutor.Apply("
                }, StringSplitOptions.None).Length - 1);
            Assert.IsTrue(apply.Contains(
                    "LocalDockPlacementMismatches(") &&
                apply.Contains("!corrected") &&
                apply.Contains("session.SetBounds("),
                "One bounded correction must be followed by actual-facts verification.");
            Assert.IsTrue(local.Contains(
                    "_pendingLocalDockRollback =") &&
                local.Contains(
                    "_localDockGestures.CancelAndRestore()") &&
                local.Contains(
                    "ApplyLocalDockCorrections("),
                "A failed native live frame must converge by restoring the captured baseline at completion.");
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void R24_TopologyRebaseLivesOnStickySta()
        {
            string host = ReadSource("StickyUiHost.cs");
            string runtime = ReadSource(
                "Features/StickyNotes/StickyDockLocalGestureRuntime.cs");
            string workspace =
                SourceGuardText.ReadStickyWorkflowSource();

            string setTopology = RawSource.SliceMethod(host,
                "internal void SetCurrentTopology(");
            string rebase = RawSource.SliceMethod(runtime,
                "internal bool TryRebaseTopology(");
            Assert.IsTrue(setTopology.Contains(
                    "_localDockGestures.TryRebaseTopology(snapshot)") &&
                setTopology.Contains(
                    "_localDockGestures.Cancel()"));
            Assert.IsTrue(rebase.Contains(
                    "_captureFacts(ids[index])") &&
                rebase.Contains(
                    "topology.Generation"));
            Assert.IsFalse(workspace.Contains(
                    "InvalidateDockPlansForTopologyChange(") ||
                workspace.Contains(
                    "ResumeDockDragAfterTopologyChange("));
        }

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void R24_OrdinarySessionSequenceRemainsTheStaleCallbackGate()
        {
            string workspace =
                SourceGuardText.ReadStickyWorkflowSource();
            string hosted = ReadSource(
                "Features/StickyNotes/StickyHostedRuntime.cs");
            string receiver = ReadSource(
                "Features/StickyNotes/StickyFactsReceiver.cs");

            Assert.IsTrue(workspace.Contains(
                    "Hosted.CanApplySequence(") ||
                receiver.Contains("CanApplySequence("));
            Assert.IsTrue(hosted.Contains(
                    "CanApplySequence(") &&
                receiver.Contains(
                    "WindowSequence"),
                "Deleting the live Dock fallback must not delete ordinary session sequence validity.");
        }

        [TestMethod]
        public void R22_LocalGestureRuntime_HasStickyOwnedSceneAndExecutor()
        {
            string dock = SourceGuardText.ReadSource(
                "Features/StickyNotes/StickyDockController.cs");
            string host = SourceGuardText.ReadSource("StickyUiHost.cs");
            string runtime = SourceGuardText.ReadSource(
                "Features/StickyNotes/StickyDockLocalGestureRuntime.cs");

            Assert.IsTrue(dock.Contains("PublishDockScene(all)") &&
                dock.Contains("_workspace.Host.SetDockScene("),
                "Dock model changes must publish a detached scene before local gestures.");
            Assert.IsTrue(host.Contains(
                    "new StickyDockLocalGestureRuntime(") &&
                host.Contains("ApplyLocalDockFollowers(") &&
                host.Contains("WindowsBatchWindowPlacementExecutor.Apply(") &&
                host.Contains("session.SetEventsSuppressed(true)"),
                "Sticky host must own the same-STA follower executor.");
            Assert.IsFalse(runtime.Contains("StickyNoteRepository") ||
                runtime.Contains("SaveAsync(") ||
                runtime.Contains("SynchronizationContext") ||
                runtime.Contains("DockFrameMailbox"),
                "The local live runtime must have no Pet, persistence or transport channel.");
            Assert.IsTrue(runtime.Contains(
                    "StickyDockGeometry.CalculateHorizontalResizeTargets(") &&
                runtime.Contains(
                    "CalculateDockMemberResizeTargetsExact(") &&
                runtime.Contains("DockPlacementPlanner.Plan("),
                "All three R13 geometry paths must be available locally.");
        }


        [TestMethod]
        public void R23_DockLiveEventsStopAtStickyStaAndOnlyCompletionCrosses()
        {
            string host = ReadSource("StickyUiHost.cs");
            string local = Between(host,
                "private bool TryHandleLocalDockEvent(",
                "private StickyDockGestureCommit CaptureLocalDockCommit(");
            string session = Between(host,
                "private void SessionEventRaised(",
                "private StickyUiCommandResult CloseAllSessions()");
            string workspace =
                SourceGuardText.ReadStickyWorkflowSource();
            string controller = SourceGuardText.ReadSource(
                "Features/StickyNotes/StickyDockController.cs");

            Assert.IsTrue(local.Contains(
                    "StickyUiEventKind.HeaderDragMoved") &&
                local.Contains(
                    "StickyUiEventKind.DockHorizontalResizing") &&
                local.Contains(
                    "StickyUiEventKind.DockDividerResizing"));
            Assert.IsFalse(local.Contains("PostEvent(") ||
                local.Contains("SaveAsync("),
                "MouseMove/WM_SIZING must remain entirely on Sticky STA.");
            Assert.IsTrue(session.IndexOf(
                    "TryHandleLocalDockEvent(session, value)",
                    StringComparison.Ordinal) <
                session.IndexOf("PostEvent(value)",
                    StringComparison.Ordinal),
                "Local Dock input must be consumed before generic Pet dispatch.");
            Assert.IsTrue(host.Contains(
                    "StickyUiEvent.DockCommitRequested(") &&
                workspace.Contains(
                    "StickyUiEventKind.DockGestureCommitRequested") &&
                workspace.Contains(
                    "Dock.CommitLocalDockGesture("));
            Assert.IsTrue(controller.Contains(
                    "TryApplyLocalDockGestureCommit(") &&
                controller.Contains(
                    "StickyUiCommand.AcknowledgeDockCommit(ack)"));

            string stickySession =
                ReadSource("StickyWindowSession.cs");
            string localGeometry = Between(stickySession,
                "private void EmitLocalDockGeometry(",
                "private void CancelReminderRequested(");
            string boundsChanged = Between(stickySession,
                "private void BoundsChanged(",
                "private void HeaderDragStarted(");
            Assert.IsFalse(localGeometry.Contains(
                    "CaptureSnapshot(") ||
                localGeometry.Contains("EmitSnapshot("),
                "Live Dock input must not capture note content.");
            Assert.IsTrue(boundsChanged.Contains(
                    "_headerDragActive"),
                "LocationChanged must not leak a parallel Pet live stream during header drag.");
        }

    }
}
