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
                "internal bool ApplyHostedStickySnapshot");

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

            Assert.IsTrue(protocol.Contains("StickyUiCommand Create(") &&
                protocol.Contains("StickyUiCommand SetBounds(") &&
                protocol.Contains("StickyUiCommand SetDockResizeRole(") &&
                protocol.Contains("StickyUiEvent FromSnapshot(") &&
                protocol.Contains("StickyUiEvent Signal(") &&
                protocol.Contains("StickyUiEvent HorizontalResize(") &&
                protocol.Contains("StickyUiEvent DividerResize("),
                "Protocol must expose factories for its payload shapes.");
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
                session.Contains("if (_applyingBounds || !_headerDragActive) return;") &&
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
            Assert.IsTrue(action.Contains("_workspace.ShowHostedSticky(") &&
                action.Contains("note, false, false)") &&
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
            string runtime = ReadSource("Features/StickyNotes/StickyDockLocalGestureRuntime.cs");
            string operations = ReadSource(
                "Core/StickyNotes/StickyDockOperations.cs");
            string geometry = ReadSource(
                "Core/StickyNotes/StickyDockGeometry.cs");
            string gate = RawSource.SliceMethod(runtime,
                "private string FindSnapTarget(");
            string begin = RawSource.SliceMethod(runtime,
                "internal long TryBegin(");
            Assert.IsTrue(begin.Contains("_captureFacts(ids[index])") &&
                begin.Contains("current == null") && begin.Contains("return 0;"),
                "Missing active windows must prevent gesture acquisition.");
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

            string host = ReadSource("StickyUiHost.cs");
            string local = RawSource.SliceMethod(host,
                "private bool TryHandleLocalDockEvent(");
            Assert.IsTrue(local.Contains("MoveLocalDockHeader(value.Facts)") &&
                !local.Contains("ApplyHostedStickySnapshot"),
                "Live drag reads actual facts locally, without content/model publication.");
            string boundsHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.BoundsChanged)",
                "if (value.Kind == StickyUiEventKind.CloseRequested)");
            Assert.IsTrue(boundsHandler.Contains("ApplyHostedStickyEvent(value, false)") &&
                !boundsHandler.Contains("ApplyHostedStickySnapshot"),
                "Ordinary BoundsChanged must use the validated event boundary.");
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
            string runtime = ReadSource("Features/StickyNotes/StickyDockLocalGestureRuntime.cs");
            string plan = RawSource.SliceMethod(runtime, "internal bool MoveHeader(");
            StringAssert.Contains(plan, "StickyPlacementRules.TryBuildLiveDockState(");
            StringAssert.Contains(plan, "gesture.Baseline, sourceFacts, _topology");
            StringAssert.Contains(plan, "DockPlacementPlanner.Plan(group, sourceFacts,");
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

    }
}
