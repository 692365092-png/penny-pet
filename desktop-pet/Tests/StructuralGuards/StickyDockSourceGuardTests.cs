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
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source,
                "if (value.Kind == StickyUiEventKind.SnapshotChanged)",
                "if (value.Kind == StickyUiEventKind.Closed)");
            string apply = Between(source,
                "private bool ApplyHostedStickyEvent",
                "private void ClearHostedDockResizeSession");

            Assert.IsTrue(handler.Contains("ApplyHostedStickyEvent(") &&
                apply.Contains("if (persist) _notes.SaveAsync();"),
                "NoteChanged must persist note data.");
            Assert.IsTrue(apply.Contains("RefreshMenuText();"),
                "NoteChanged must refresh menu text.");
            Assert.IsTrue(apply.Contains("if (visibilityChanged ||") &&
                apply.Contains("RefreshNoteTabs();"),
                "Content autosave must refresh tabs only for visibility or hidden-title changes.");
        }

        [TestMethod]
        public void StickyUiRegistry_CloseAllPreflightsImeAndSuppressesEvents()
        {
            string host = ReadSource("StickyUiHost.cs");
            string closeAll = Between(host,
                "private StickyUiCommandResult CloseAllSessions()",
                "private StickyUiCommandResult ApplyLatestDockPlan");
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
            string dock = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            int handled = dock.IndexOf(
                "result.Status != StickyUiCommandStatus.Handled",
                StringComparison.Ordinal);
            int remove = dock.IndexOf("_notes.Remove(note)",
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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dock = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string persistence = ReadSource(
                "Features/StickyNotes/PetPersistenceCoordinator.cs");
            string reminder = ReadSource("PetReminderWindowsCoordinator.cs");
            string menu = ReadSource("PetMenuActions.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string codec = ReadSource("Core/StickyNotes/StickyNoteCodec.cs");
            string petOwned = startup + form + coordinator + dock +
                persistence + reminder + menu;

            Assert.IsTrue(startup.Contains("ShowHostedSticky(") &&
                coordinator.Contains("StartHostedSticky(") &&
                host.Contains("Dictionary<string, StickyWindowSession> _sessions") &&
                session.Contains("new StickyNoteWindow("),
                "Startup and creation must route through the hosted session executor.");
            Assert.IsFalse(petOwned.Contains("_noteWindows") ||
                petOwned.Contains("GetOrCreateStickyNoteWindow") ||
                petOwned.Contains("FallBackHostedStickyToLegacy") ||
                petOwned.Contains("RestoreStickyDockComponent") ||
                petOwned.Contains("new StickyNoteWindow("),
                "PetForm must not retain a legacy Sticky Window executor.");
            Assert.IsFalse(host.Contains("StickyNoteRepository") ||
                host.Contains("IsTodoList") || host.Contains("IsSchedule"),
                "The host must own sessions, not canonical persistence or content modes.");
            Assert.IsTrue(codec.Contains("versionOne") &&
                codec.Contains("versionNine"),
                "Removing the executor must retain legacy persistence readers.");
        }

        [TestMethod]
        public void StickyDock_UsesDetachedFactsAndTypedHostedEffectBoundary()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(form.Contains("DockInteractionSession _dockInteraction") &&
                !form.Contains("_activeDockGroupIds") &&
                !form.Contains("_activeDockCurrentFacts") &&
                coordinator.Contains("_dockInteraction.MemberIds") &&
                coordinator.Contains("CalculateDockTranslationTargets") &&
                coordinator.Contains("ApplyDockTargets"),
                "Dock session geometry must be note-id/facts based.");
            Assert.IsFalse(coordinator.Contains("Object.ReferenceEquals") ||
                coordinator.Contains("member.Location ="),
                "Dock decisions and group motion must not use Window/model identity.");
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
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
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
        public void HostedDividerLiveResize_UsesExplicitLeanLifecycle()
        {
            string native = ReadSource(
                "Features/StickyNotes/StickyNativeWindowBehavior.cs");
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string liveResize = Between(dockCoordinator,
                "private bool ResizeHostedStickyDockDivider",
                "private void OnDividerLiveBatchApplied");
            string progress = Between(windowCoordinator,
                "if (value.Kind == StickyUiEventKind.DockDividerResizing)",
                "if (value.Kind == StickyUiEventKind.DockDividerResizeCompleted)");

            Assert.IsTrue(native.Contains("WmEnterSizeMove") &&
                native.Contains("WmSizing") &&
                native.Contains("WmExitSizeMove") &&
                native.Contains("DockDividerResizeStarted") &&
                native.Contains("DockDividerResizing") &&
                native.Contains("DockDividerResizeCompleted"),
                "Native sizing must publish an explicit divider lifecycle.");
            Assert.IsTrue(liveResize.Contains(
                    "CalculateDockMemberResizeTargets") &&
                liveResize.Contains("QueueLive") &&
                liveResize.Contains("PostLatestDividerBatch") &&
                !liveResize.Contains("LayoutDockChain") &&
                !liveResize.Contains("RefreshDockResizeRoles") &&
                !liveResize.Contains("SaveAsync") &&
                !liveResize.Contains("ApplyDockCanonicalFromPhysical"),
                "Live ticks must coalesce follower frames through the latest-wins divider mailbox without writing canonical state.");
            Assert.IsFalse(progress.Contains("SaveAsync") ||
                progress.Contains("RefreshDockResizeRoles"),
                "Live progress must not save or refresh resize roles.");
            Assert.IsTrue(windowCoordinator.Contains(
                "CompleteHostedStickyDockDivider(value)") &&
                windowCoordinator.Contains("PostFinalDividerBatch") &&
                windowCoordinator.Contains("DividerStackSeamIsExact") &&
                windowCoordinator.Contains("ClearHostedDockResizeSession()") &&
                windowCoordinator.Contains("_notes.SaveAsync();"),
                "Completion must post one re-anchored final batch, verify the seam, save once, and clear the session only after it resolves.");
            int sourceCommit = windowCoordinator.IndexOf(
                "CommitDividerSourceFinal", StringComparison.Ordinal);
            int finalBatch = windowCoordinator.IndexOf(
                "PostFinalDividerBatch", StringComparison.Ordinal);
            Assert.IsTrue(sourceCommit >= 0 && finalBatch > sourceCommit &&
                windowCoordinator.Contains(
                    "PlacementReason.UserResizeCommit"),
                "The resized source must persist its final geometry and durable preferred height synchronously before the async follower batch is posted.");
        }

        [TestMethod]
        public void DockResizeCompletionsCommitDurablePreferenceWithoutDrift()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string divider = Between(coordinator,
                "private void CommitDividerPreferred",
                "private void CommitDockGroupResizePreferred");
            Assert.IsTrue(divider.Contains("hasPreferred") &&
                divider.Contains("PreferredLocalLogicalHeight") &&
                divider.Contains("local.Height") &&
                divider.Contains("TryBuildPreferredPlacement(facts, topology") &&
                !divider.Contains("canonical.LocalLogicalHeight"),
                "A vertical divider must advance only the durable height and keep the established preferred position and width.");
            string groupResize = Between(coordinator,
                "private void CommitDockGroupResizePreferred",
                "internal static bool ShouldApplyHostedSequence");
            Assert.IsTrue(groupResize.Contains(
                    "BuildDockChainOrderIncludingHidden") &&
                groupResize.Contains("sourceLocal.Width") &&
                groupResize.Contains("PlacementReason.UserResizeCommit"),
                "A group horizontal resize must propagate the new left/width to every visible member's durable preference.");
        }

        [TestMethod]
        public void HostedDock_ReusesNeutralSessionAndTypedEffectBoundary()
        {
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            Assert.IsTrue(windowCoordinator.Contains(
                "BeginStickyDockDrag(facts, value.Facts, value.Topology)") &&
                windowCoordinator.Contains("MoveStickyDockDrag(facts, value.Facts, value.Topology)") &&
                windowCoordinator.Contains(
                    "CompleteStickyDockDrag(facts, value)"),
                "Hosted drag facts must enter the existing Dock session.");
            Assert.IsTrue(dockCoordinator.Contains(
                "StickyUiCommand.SetBounds(") &&
                dockCoordinator.Contains("StickyUiCommand.SetTopMost(") &&
                dockCoordinator.Contains("ApplyDockTargets") &&
                dockCoordinator.Contains("ResizeStickyDockGroup") &&
                dockCoordinator.Contains("ResizeHostedStickyDockDivider") &&
                dockCoordinator.Contains("CalculateDockDividerTargets") &&
                dockCoordinator.Contains("CloseStickyDockNote") &&
                !dockCoordinator.Contains("_noteWindows") &&
                !dockCoordinator.Contains("StickyNoteWindow"),
                "Dock effects must terminate at the hosted typed boundary.");
            Assert.IsFalse(windowCoordinator.Contains("DockMergeRequested") ||
                windowCoordinator.Contains("DockAttached") ||
                windowCoordinator.Contains("DockCompleted") ||
                dockCoordinator.Contains("HostedDockCoordinator"),
                "The minimum E2E must not add a second Dock protocol.");
        }

        [TestMethod]
        public void DockVisualFeedback_UsesDetachedFactsOnHostedPath()
        {
            string form = ReadSource("PetForm.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string begin = Between(coordinator,
                "private void BeginStickyDockDrag",
                "private void MoveStickyDockDrag");
            string moveVisuals = Between(coordinator,
                "RememberActiveDockFacts(PlanToDockTargets(livePlan));",
                "private void CompleteStickyDockDrag");
            string mergeVisuals = Between(coordinator,
                "private void CompleteStickyDockDrag",
                "private void StartDockFinalization");
            string helpers = Between(coordinator,
                "private void ShowSplitGuide",
                "private DockTarget FindDockTarget");

            Assert.IsTrue(begin.Contains("ShowSplitGuide(seed, groupFacts)") &&
                !begin.Contains("StickyNoteWindow"),
                "Hosted split candidates must receive a detached guide.");
            Assert.IsTrue(moveVisuals.Contains(
                    "UpdateSplitGuide(seed, _dockInteraction.PreviewFacts)") &&
                moveVisuals.Contains("UpdateDockPreview(seed, previewFacts)") &&
                !moveVisuals.Contains("_activeNoteDragHosted"),
                "Hosted drag must update previews from detached facts.");
            Assert.IsTrue(mergeVisuals.Contains("ShowTransientDockPulse") &&
                !mergeVisuals.Contains("if (!_activeNoteDragHosted)"),
                "Hosted merge must publish the detached seam pulse.");
            Assert.IsTrue(helpers.Contains(
                    "CalculateDockVisualSeam(parentFacts)") &&
                helpers.Contains("IDictionary<string, DockWindowFacts>") &&
                !helpers.Contains("parent.Bounds") &&
                !helpers.Contains("StickyDockOperations"),
                "Visual helpers must use detached geometry without changing Dock rules.");
            Assert.IsTrue(form.Contains(
                    "private string _dockPreviewParentNoteId") &&
                form.Contains("private string _dockPreviewChildNoteId") &&
                !form.Contains("StickyNoteWindow _dockPreviewParent") &&
                !form.Contains("StickyNoteWindow _dockPreviewChild"),
                "Preview identity must be note-id based, not Window based.");
        }

        [TestMethod]
        public void StickyRecovery_ExpandsAllThroughOwnedEffectBoundaries()
        {
            string menu = ReadSource("PetContextMenu.cs");
            string manager = ReadSource(
                "Features/StickyNotes/StickyNotes.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string action = Between(coordinator,
                "private void ExpandAndTileAllStickyNotesToPetScreen",
                "internal static List<DockLayoutTarget>");
            string preparation = Between(coordinator,
                "PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,",
                "internal static List<Rectangle> CalculateStickyRecoveryLayout");

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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(session.Contains(
                    "WindowsWindowFactsReader.Capture(hwnd, _noteId,") &&
                session.Contains("_topology == null ? 0 : _topology.Generation") &&
                session.Contains("facts, _topology)"),
                "Facts must be captured with the Pet-owned topology generation.");
            Assert.IsTrue(coordinator.Contains(
                    "ApplyHostedStickyFactsGeometry") &&
                coordinator.Contains("StickyPlacementMath.FromPhysicalRect(") &&
                coordinator.Contains(
                    "snapshot.ApplyContentTo(canonical)"),
                "Geometry events must derive v10 geometry from facts, never snapshot.ApplyTo.");

            string dragHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.HeaderDragStarted ||",
                "if (value.Kind == StickyUiEventKind.BoundsChanged)");
            Assert.IsTrue(dragHandler.Contains(
                    "DockWindowFacts.FromWindowFacts(") &&
                !dragHandler.Contains("ApplyHostedStickySnapshot"),
                "Drag geometry must flow from facts-derived canonical state.");
            string boundsHandler = Between(coordinator,
                "if (value.Kind == StickyUiEventKind.BoundsChanged)",
                "if (value.Kind == StickyUiEventKind.DockDividerResizeStarted)");
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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string reconcile = Between(coordinator,
                "private void HandleStickyTopologyChanged",
                "private void CompleteTemporaryRehome");

            Assert.IsTrue(reconcile.Contains(
                    "if (!String.IsNullOrEmpty(note.DockGroupId)) continue;") &&
                reconcile.Contains(
                    "FallbackDisplayPolicy.ResolveFallbackSurface("),
                "DRT-7 must rehome standalone notes only through the Core fallback policy.");
            Assert.IsFalse(reconcile.Contains(
                    "CommitHostedStickyPreferred") ||
                reconcile.Contains("PreferredDisplayTargetKey ="),
                "Temporary rehome must never commit or rewrite the durable preferred.");
        }

        [TestMethod]
        public void Drt67Closeout_StandaloneDragCommitUsesEventAuthority()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string commit = Between(coordinator,
                "private void CompleteDockDurableCommit",
                "private bool ApplyHostedStickySnapshot");

            Assert.IsTrue(commit.Contains(
                    "TryPrepareDockCommit(result, expectedTopology, expectedEpoch,") &&
                commit.Contains("result.DockBatchResult"),
                "The dock durable commit must consume the captured actual-facts result.");
            Assert.IsTrue(commit.Contains(
                    "TryBuildPreferredPlacement(") &&
                commit.Contains("PlacementReason.DockCommit") &&
                commit.Contains("_dockInteraction.PendingMerge.TryCommit(") &&
                commit.Contains("_notes.Save()"),
                "Every member preferred must derive from captured facts plus the finalizing topology, then persist once.");
            Assert.IsFalse(commit.Contains("CurrentTopologySnapshot("),
                "A G-generation dock commit must never read a later Current generation.");
        }

        [TestMethod]
        public void Drt9_DockUsesImmutableMailboxAndNativeDeferBatch()
        {
            string host = ReadSource("StickyUiHost.cs");
            string batch = ReadSource(
                "Infrastructure/Display/WindowsBatchWindowPlacementExecutor.cs");
            string native = ReadSource(
                "Infrastructure/Display/NativeDisplayConfig.cs");
            string dock = ReadSource(
                "Features/StickyNotes/DockWindowFacts.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            Assert.IsTrue(dock.Contains(
                    "internal sealed class DockPlanMailbox") &&
                dock.Contains("TakeLatest()") &&
                !dock.Contains("DockBatchLayout"),
                "The mutable DockBatchLayout must be retired for the immutable mailbox.");
            Assert.IsTrue(host.Contains("PostLatestDockPlan(") &&
                host.Contains("mailbox.TakeLatest()") &&
                host.Contains("WindowsBatchWindowPlacementExecutor.Apply("),
                "The host must apply the newest plan through the native batch executor.");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyLatestDockPlan(",
                "private void PostEvent");
            Assert.IsTrue(apply.Contains(
                    "plan.TopologyGeneration != topology.Generation") &&
                apply.Contains("WindowsBatchWindowPlacementExecutor.Apply("),
                "The batch must be gated on the host-owned current generation.");
            Assert.IsTrue(batch.Contains("BeginDeferWindowPos(") &&
                batch.Contains("DeferWindowPos(") &&
                batch.Contains("EndDeferWindowPos(") &&
                native.Contains(
                    "static extern IntPtr BeginDeferWindowPos("),
                "Followers must move in one native deferred batch.");
            Assert.IsTrue(coordinator.Contains(
                    "_stickyUiHost.PostLatestDockPlan(") &&
                coordinator.Contains("DockPlacementPlanner.Plan("),
                "The drag coordinator must post immutable plans into the mailbox.");
        }

        [TestMethod]
        public void Drt9_LiveBatchNeverWritesDurablePreferred()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string batch = Between(coordinator,
                "private void ApplyLiveDockPlan",
                "private void ApplyDockBatchResult");
            Assert.IsFalse(batch.Contains("PreferredDisplayTargetKey") ||
                batch.Contains("CommitHostedStickyPreferred"),
                "A live drag batch must never commit the durable preferred placement.");

            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyLatestDockPlan(",
                "private void PostEvent");
            Assert.IsFalse(apply.Contains("Preferred"),
                "The STA batch executor must not touch durable preferred fields.");
        }

        [TestMethod]
        public void Drt10_LiveDragUsesPlannerDrivenBySourceFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string plannerPath = Between(coordinator,
                "private DockPlacementPlan PlanLiveDockPlan",
                "private void CompleteStickyDockDrag");

            Assert.IsTrue(plannerPath.Contains(
                    "WindowFacts sourceFacts") &&
                plannerPath.Contains("DockPlacementPlanner.Plan(") &&
                plannerPath.Contains("StickyPlacementRules.TryBuildLiveDockState(") &&
                plannerPath.Contains("BuildDockChainOrder(seed)"),
                "The live drag must be planned from the source window's actual facts.");
            Assert.IsFalse(plannerPath.Contains("WindowsDisplayResolver") ||
                plannerPath.Contains("Screen.FromRectangle") ||
                plannerPath.Contains("CalculateDockTranslationTargets"),
                "Followers must never pick a target display or translate old coordinates.");
            string move = Between(coordinator,
                "private void MoveStickyDockDrag",
                "private DockPlacementPlan PlanLiveDockPlan");
            Assert.IsTrue(move.Contains("PlanLiveDockPlan(seed, sourceFacts,") &&
                !move.Contains("CalculateDockTranslationTargets("),
                "The live move path must route through the planner.");
        }

        [TestMethod]
        public void Drt10_PlanSurfaceAndDpiComeFromSourceFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string batch = Between(coordinator,
                "private DockPlacementPlan PlanLiveDockPlan",
                "private void CompleteStickyDockDrag");

            Assert.IsTrue(batch.Contains(
                    "WindowFacts sourceFacts") &&
                batch.Contains("DockPlacementPlanner.Plan(") &&
                batch.Contains("sourceFacts.Dpi") &&
                batch.Contains("_dockInteraction.CanPlan(") &&
                batch.Contains("_dockPlanMailbox.NextSequence()"),
                "One plan must carry one capture-time generation, surface, DPI and sequence.");
            Assert.IsFalse(batch.Contains("WindowsDisplayResolver") ||
                batch.Contains("MonitorFromRect"),
                "The plan must never consult the legacy resolver.");
            string post = Between(coordinator,
                "private void ApplyLiveDockPlan",
                "private void ApplyDockBatchResult");
            Assert.IsFalse(post.Contains("CurrentTopologySnapshot(") ||
                post.Contains("new DockPlacementPlan("),
                "The plan must not be re-stamped against a later generation after creation.");
        }

        [TestMethod]
        public void DrtCloseout_ReprojectIsTransactionalAndReturnsFacts()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

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
            Assert.IsTrue(apply.Contains(
                    "ApplyHostedStickyFactsGeometry(canonical, result.Facts,") &&
                apply.Contains("_placementRuntime.TryUpdateEffective("),
                "A reproject result must update geometry and Effective from actual facts.");
        }

        [TestMethod]
        public void DrtCloseout_DockBatchReturnsFactsWithBoundedFallback()
        {
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            string apply = Between(host,
                "private StickyUiCommandResult ApplyLatestDockPlan(",
                "private void PostEvent");
            Assert.IsTrue(apply.Contains(
                    "session.CaptureDockMember(topology)") &&
                apply.Contains("new DockBatchResult(") &&
                apply.Contains("SetBounds(new StickyUiBounds"),
                "The batch must capture actual facts and keep one bounded fallback.");
            string member = Between(session,
                "internal DockBatchMemberResult CaptureDockMember(",
                "private WindowFacts CaptureFactsWith");
            Assert.IsTrue(member.Contains("AdoptTopology(topology)") &&
                member.Contains("CaptureFactsWith(_topology)") &&
                member.Contains("new DockBatchMemberResult("),
                "The member result must carry facts plus a content snapshot.");

            string result = Between(coordinator,
                "private void ApplyDockBatchResult",
                "private DockWindowFacts GetHostedDockFacts");
            Assert.IsTrue(result.Contains("member.Facts") &&
                result.Contains(
                    "ApplyHostedStickyFactsGeometry(candidate.Canonical, member.Facts,") &&
                result.Contains("_lastAppliedDockPlanSequence"),
                "Only same-generation newest-sequence facts may update the repository.");
        }

        [TestMethod]
        public void DrtCloseout_LiveDockNeverPreWritesRepository()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string live = Between(coordinator,
                "private void ApplyLiveDockPlan",
                "private void ApplyDockBatchResult");

            Assert.IsFalse(live.Contains("ApplyDockCanonicalFromPhysical") ||
                live.Contains("PreferredDisplayTargetKey") ||
                live.Contains("WindowsDisplayResolver"),
                "A live frame must only deposit the desired plan into the mailbox.");
            string result = Between(coordinator,
                "private void ApplyDockBatchResult",
                "private DockWindowFacts GetHostedDockFacts");
            Assert.IsTrue(result.Contains(
                    "ApplyHostedStickyFactsGeometry(candidate.Canonical, member.Facts,") &&
                !result.Contains("WindowsDisplayResolver"),
                "Only actual facts derived from the same-generation topology may update geometry.");
            Assert.IsTrue(
                result.IndexOf("_placementRuntime.CanAcceptEffective(") >= 0 &&
                result.IndexOf("_placementRuntime.CanAcceptEffective(") <
                    result.IndexOf("_lastAppliedDockPlanSequence = batch.PlanSequence"),
                "The whole live batch must pass acceptance preflight before any plan-sequence advance.");
        }

        [TestMethod]
        public void FinalMouseUpPlanCannotBeClearedBeforeApply()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string mailbox = ReadSource(
                "Features/StickyNotes/DockWindowFacts.cs");
            string complete = Between(coordinator,
                "private void CompleteStickyDockDrag",
                "private static List<string> CollectExpectedPlanMemberIds");
            string takeFinal = Between(mailbox,
                "internal DockPlacementPlan TakeFinal(",
                "internal void CompleteFinal(");
            string takeLatest = Between(mailbox,
                "internal DockPlacementPlan TakeLatest()",
                "internal void ReplaceWithFinal(");

            Assert.IsTrue(complete.Contains(
                    "_dockPlanMailbox.ReplaceWithFinal(finalPlan)") &&
                complete.Contains("_stickyUiHost.PostFinalDockPlan("),
                "Mouse-up must replace pending live work with a final plan.");
            Assert.IsFalse(takeFinal.Contains("Current = null") ||
                takeFinal.Contains("ApplyQueued = false"),
                "The final plan must remain owned until its apply completes.");
            string finalBranch = Between(takeLatest,
                "Current.PlanSequence == FinalPlanSequence)",
                "DockPlacementPlan plan = Current;");
            Assert.IsFalse(finalBranch.Contains("Current = null") ||
                finalBranch.Contains("ApplyQueued = false"),
                "A stale live callback must not release the final barrier.");
        }

        [TestMethod]
        public void FinalMouseUpUsesHeaderDragCompletedFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string complete = Between(coordinator,
                "private void CompleteStickyDockDrag",
                "private static List<string> CollectExpectedPlanMemberIds");
            Assert.IsTrue(complete.Contains(
                "StartDockFinalization(seed, remainderSeed)") &&
                complete.Contains("CaptureDockFacts(expectedIds,") &&
                complete.Contains("PostFinalDockPlan"));
            Assert.IsFalse(complete.Contains("value.Facts") ||
                complete.Contains("_placementRuntime.GetEffective(") ||
                complete.Contains("LayoutDockChain(") ||
                complete.Contains("ApplyDockTarget("));
        }

        [TestMethod]
        public void StandaloneDragUsesFinalStaCaptureBarrier()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string finalization = Between(coordinator,
                "private void StartDockFinalization",
                "private static List<string> CollectExpectedPlanMemberIds");
            Assert.IsTrue(finalization.Contains("CaptureDockFacts(expectedIds,") &&
                finalization.Contains("PostFinalDockPlan") &&
                finalization.Contains("CompleteDockDurableCommit("));
            Assert.IsFalse(coordinator.Contains("CompleteStandaloneDragCommit"));
        }

        [TestMethod]
        public void DockCommitRejectsMissingMember()
        {
            string validation = DockCommitValidationSource();
            Assert.IsTrue(validation.Contains(
                    "batch.Members.Count != expected.Count") &&
                validation.Contains("actual.Count != expected.Count"));
        }

        [TestMethod]
        public void DockCommitRejectsNullFacts()
        {
            Assert.IsTrue(DockCommitValidationSource().Contains(
                "member.Facts == null"));
        }

        [TestMethod]
        public void DockCommitRejectsGenerationMismatch()
        {
            string validation = DockCommitValidationSource();
            Assert.IsTrue(validation.Contains(
                    "batch.TopologyGeneration != expectedTopology.Generation") &&
                validation.Contains(
                    "member.Facts.TopologyGeneration !="));
        }

        [TestMethod]
        public void DockCommitFailureDoesNotSaveOrCommitMembership()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string commit = Between(coordinator,
                "private void CompleteDockDurableCommit",
                "private bool TryPrepareDockCommit");
            int rejectionReturn = commit.IndexOf(
                "TraceDockCommitRejected(rejection);", StringComparison.Ordinal);
            int membership = commit.IndexOf("_dockInteraction.PendingMerge.TryCommit(",
                StringComparison.Ordinal);
            int save = commit.IndexOf("_notes.Save()", StringComparison.Ordinal);
            Assert.IsTrue(rejectionReturn >= 0 && membership > rejectionReturn &&
                save > membership);
        }

        [TestMethod]
        public void LiveDockCaptureDoesNotReachWindowsDisplayResolver()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string live = Between(coordinator,
                "private DockPlacementPlan PlanLiveDockPlan",
                "private void CompleteStickyDockDrag");
            Assert.IsFalse(live.Contains("WindowsDisplayResolver") ||
                live.Contains("ApplyDockTarget("));
        }

        [TestMethod]
        public void CaptureDockMemberDoesNotCallCaptureCanonicalPlacement()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string capture = Between(session,
                "internal DockBatchMemberResult CaptureDockMember(",
                "private WindowFacts CaptureFactsWith");
            string helper = Between(session,
                "private StickyNoteUiSnapshot CaptureContentSnapshotForNativeResult",
                "private WindowFacts CaptureFactsWith");
            Assert.IsTrue(capture.Contains(
                    "CaptureContentSnapshotForNativeResult()") &&
                helper.Contains("StickyNoteUiSnapshot.FromContentData("));
            Assert.IsFalse(capture.Contains("CaptureSnapshot()") ||
                capture.Contains("CaptureCanonicalPlacement") ||
                helper.Contains("StickyNoteUiSnapshot.FromData(") ||
                helper.Contains("CaptureCanonicalPlacement") ||
                helper.Contains("WindowsDisplayResolver"));
        }

        [TestMethod]
        public void NativeBatchRejectsPartialExpectedFollowers()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyDockPlan(",
                "private StickyUiCommandResult CaptureDockFactsForCommit");
            int missing = apply.IndexOf(
                "!TryGetSession(target.NoteId, out session)",
                StringComparison.Ordinal);
            int zero = apply.IndexOf("handle == IntPtr.Zero",
                StringComparison.Ordinal);
            int nativeApply = apply.IndexOf(
                "WindowsBatchWindowPlacementExecutor.Apply(",
                StringComparison.Ordinal);
            Assert.IsTrue(missing >= 0 && zero > missing &&
                nativeApply > zero);
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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string plan = Between(coordinator,
                "private DockPlacementPlan PlanDockPlan",
                "private List<DockLayoutTarget> PlanToDockTargets");

            StringAssert.Contains(plan, "StickyPlacementRules.TryBuildLiveDockState(");
            StringAssert.Contains(plan, "_placementRuntime.GetEffective(");
            Assert.IsFalse(plan.Contains("LocalLogicalWidth") ||
                plan.Contains("LocalLogicalHeight") || plan.Contains("member.Height"),
                "Live Dock geometry must not fall back to persisted coordinates.");
        }

        [TestMethod]
        public void Drt10_NativeBatchBootstrapsFollowersToPlanDpi()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyDockPlan(",
                "private StickyUiCommandResult CaptureDockFactsForCommit");
            string session = ReadSource("StickyWindowSession.cs");
            string prepare = Between(session,
                "internal bool TryPrepareDockTargetDpi(",
                "internal void CompleteDockTargetDpi(");

            int bootstrap = apply.IndexOf("TryPrepareDockTargetDpi(",
                StringComparison.Ordinal);
            int batch = apply.IndexOf(
                "WindowsBatchWindowPlacementExecutor.Apply(",
                StringComparison.Ordinal);
            Assert.IsTrue(bootstrap >= 0 && batch > bootstrap &&
                apply.Contains("topology.FindByRuntimeSurfaceId(") &&
                apply.Contains("member.Facts.Dpi != plan.TargetDpi") &&
                apply.Contains("targetSurface.RuntimeGdiName"),
                "Followers must acquire the one plan DPI before the native batch, then be verified.");
            Assert.IsTrue(prepare.Contains("_window.Hide()") &&
                prepare.Contains("MoveHiddenToSurface(") &&
                prepare.Contains("GetDpiForWindow() != targetDpi"),
                "A cross-DPI follower must use the hidden target-surface bootstrap.");
        }

        [TestMethod]
        public void Drt10_DpiBootstrapFailureRollsBackWholeFrame()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyDockPlan(",
                "private StickyUiCommandResult CaptureDockFactsForCommit");
            string session = ReadSource("StickyWindowSession.cs");
            string complete = Between(session,
                "internal void CompleteDockTargetDpi(",
                "private void RollbackReproject(");

            Assert.IsTrue(apply.Contains("bool placementApplied = false;") &&
                apply.Contains("CompleteDockTargetDpi(\n                        transitions[index], placementApplied)") &&
                complete.Contains("if (!placementApplied)") &&
                complete.Contains("RollbackReproject("),
                "A rejected frame must restore every follower's prior bounds and visibility.");
        }

        [TestMethod]
        public void Drt10_DurableCommitRejectsMixedTargetFacts()
        {
            string validation = DockCommitValidationSource();

            Assert.IsTrue(validation.Contains("batch.TargetDpi <= 0") &&
                validation.Contains("FindByRuntimeSurfaceId(") &&
                validation.Contains("member.Facts.Dpi != batch.TargetDpi") &&
                validation.Contains("targetSurface.RuntimeGdiName"),
                "A mixed-DPI or wrong-surface result must never become durable.");
        }

        [TestMethod]
        public void Drt11_TopologyChangeInvalidatesMailboxBeforeReconcile()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string changed = Between(coordinator,
                "private void HandleStickyTopologyChanged(",
                "private void InvalidateDockPlansForTopologyChange(");
            string invalidate = Between(coordinator,
                "private void InvalidateDockPlansForTopologyChange(",
                "private void ResumeDockDragAfterTopologyChange(");

            Assert.IsTrue(changed.IndexOf(
                    "InvalidateDockPlansForTopologyChange(snapshot)",
                    StringComparison.Ordinal) < changed.IndexOf(
                    "ReconcileDockGroups(snapshot, petFacts)",
                    StringComparison.Ordinal));
            Assert.IsTrue(invalidate.Contains("_dockPlanMailbox.Clear()") &&
                invalidate.Contains("_dockInteraction.BeginRebase("));
        }

        [TestMethod]
        public void Drt11_ActiveDragRecapturesSourceAfterSettledTopology()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string resume = Between(coordinator,
                "private void ResumeDockDragAfterTopologyChange(",
                "private void ReconcileDockGroups(");

            Assert.IsTrue(resume.Contains(
                    "StickyUiCommand.CaptureDockFacts(") &&
                resume.Contains("TryApplyDockFactsBarrier(result, expectedIds") &&
                resume.Contains("_dockInteraction.TryEnterDragging(epoch,"));
            Assert.IsFalse(resume.Contains("StickyDockGroups.") ||
                resume.Contains("CommitVisibleDockOrder("),
                "A topology barrier must preserve membership during a drag.");
        }

        [TestMethod]
        public void Drt11_GroupReprojectUsesOneAtomicNativeBatch()
        {
            string host = ReadSource("StickyUiHost.cs");
            string apply = Between(host,
                "private StickyUiCommandResult ApplyDockGroupReproject(",
                "private StickyUiCommandResult CaptureDockFactsForCommit");

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
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string apply = Between(coordinator,
                "private bool TryApplyDockTopologyResult(",
                "private void ReconcileStandaloneSticky(");

            Assert.IsTrue(apply.Contains(
                    "ApplyHostedStickyFactsGeometry(") &&
                apply.Contains("_placementRuntime.TryUpdateEffective(") &&
                apply.Contains("_notes.SaveAsync()"));
            Assert.IsFalse(apply.Contains("CommitHostedStickyPreferred(") ||
                apply.Contains("PreferredLocalLogical") ||
                apply.Contains("PreferredDisplayTargetKey"),
                "Temporary group geometry must never overwrite preference.");
        }

        [TestMethod]
        public void Drt11_GroupReturnRequiresNoUserMove()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string reconcile = Between(coordinator,
                "private void ReconcileDockGroup(",
                "private static DisplaySurfaceSnapshot FindCommonDockPreferredSurface(");
            string complete = Between(coordinator,
                "private void PostDockGroupTopologyReproject(",
                "private bool TryApplyDockTopologyResult(");

            Assert.IsTrue(reconcile.Contains(
                    "_placementRuntime.UserMovedSinceRehome(member.Id)") &&
                reconcile.Contains(
                    "PostDockGroupTopologyReproject(group, snapshot, preferred,") &&
                complete.Contains("MarkReturnedToPreferred(") &&
                complete.Contains("MarkTemporaryRehome("));
        }

        [TestMethod]
        public void Drt11_GroupResultRejectsStaleOrIncompleteGeneration()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string apply = Between(coordinator,
                "private bool TryApplyDockTopologyResult(",
                "private void ReconcileStandaloneSticky(");

            Assert.IsTrue(apply.Contains(
                    "CurrentTopologySnapshot().Generation != snapshot.Generation") &&
                apply.Contains("batch.Members.Count != expectedIds.Count") &&
                apply.Contains(
                    "member.Facts.TopologyGeneration != snapshot.Generation") &&
                apply.Contains("actual.Count != expected.Count"));
        }
    }
}
