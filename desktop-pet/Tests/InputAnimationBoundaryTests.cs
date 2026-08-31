using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    // These tests inspect source structure and architectural boundaries.
    // They do not prove runtime WPF/WinForms/IME/Dock behavior.
    [TestCategory("ArchitectureSourceBoundary")]
    [TestClass]
    public sealed class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void StartupLoading_UsesBootstrapOnlyEmbeddedVisual()
        {
            string loading = ReadSource("StartupLoadingForm.cs");
            string loadingThread = ReadSource(
                "StartupLoadingThreadHost.cs");
            string host = ReadSource("PennyApplicationHost.cs");
            string animation = ReadSource("PetAnimationRuntime.cs");
            string startup = ReadSource("PetStartupCoordinator.cs");

            Assert.IsTrue(loading.Contains("PennyPet.Startup.Loading") &&
                loading.Contains("GetManifestResourceStream(ResourceName)") &&
                loading.Contains("PetSettingRules.NormalizePetScalePercent") &&
                loading.Contains("CalculateImageBounds(source.Size, size)") &&
                loading.Contains("canvas.Height - height") &&
                loading.Contains("graphics.Clear(Color.Transparent)"),
                "Loading must use its embedded asset on a bottom-aligned proportional Pet canvas.");
            Assert.IsFalse(loading.Contains("PetArtPackage") ||
                loading.Contains("StickyUiHost") ||
                loading.Contains("StickyUiThreadHost") ||
                loading.Contains("StickyNoteRepository") ||
                loading.Contains("StickyHostedRuntime") ||
                loading.Contains("WpfApplicationHost") ||
                loading.Contains("PetForm."),
                "Bootstrap loading must not depend on runtime art or sticky state.");
            int showLoading = host.IndexOf("loading.Start(preloadedSettings);",
                StringComparison.Ordinal);
            int constructPet = host.IndexOf("new PetForm(preloadedSettings)",
                StringComparison.Ordinal);
            Assert.IsTrue(showLoading >= 0 && constructPet > showLoading,
                "The loading form must be shown before PetForm construction.");
            Assert.IsTrue(loadingThread.Contains("new Thread(") &&
                loadingThread.Contains(
                    "SetApartmentState(ApartmentState.STA)") &&
                loadingThread.Contains("IsBackground = true") &&
                loadingThread.Contains("Application.Run(form)") &&
                loadingThread.Contains("form.BeginInvoke(") &&
                loadingThread.Contains("_ready.Set()"),
                "Loading must own a responsive message loop on a dedicated STA.");
            Assert.IsFalse(loadingThread.Contains("new PetForm") ||
                loadingThread.Contains("PetArtPackage") ||
                loadingThread.Contains("StickyUiHost") ||
                loadingThread.Contains("StickyNoteRepository"),
                "The loading thread must own only bootstrap presentation.");
            string closeLoading = Between(loadingThread,
                "internal void Close()", "internal void BringToFront()");
            string postLoading = Between(loadingThread,
                "private void Post(", "public void Dispose()");
            Assert.IsTrue(closeLoading.Contains("Post(") &&
                postLoading.Contains("form.BeginInvoke(") &&
                loadingThread.Contains("_exited.WaitOne()"),
                "Close must marshal to loading STA and disposal must await exit.");
            Assert.IsTrue(host.Contains("pet.StartupReady += delegate") &&
                host.Contains("loading.Close();") &&
                host.Contains("loading.BringToFront();") &&
                !host.Contains("Application.DoEvents()"),
                "StartupReady must close loading without DoEvents workarounds.");
            int releaseFrame = startup.IndexOf(
                "_startupDisplaySuppressed = false;", StringComparison.Ordinal);
            int renderFrame = startup.IndexOf("RenderCurrentFrame();",
                StringComparison.Ordinal);
            Assert.IsTrue(animation.Contains(
                    "if (_startupDisplaySuppressed || !IsHandleCreated") &&
                startup.Contains("_startupUiReady, _startupArtReady") &&
                releaseFrame >= 0 && renderFrame > releaseFrame,
                "Normal Pet frames must remain suppressed until startup readiness.");
        }

        [TestMethod]
        public void Autosave_DoesNotRefreshSideTabs()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source, "form.NoteChanged += delegate",
                "form.HeaderDragStarted");

            Assert.IsTrue(handler.Contains("_notes.SaveAsync();"),
                "NoteChanged must persist note data.");
            Assert.IsTrue(handler.Contains("RefreshMenuText();"),
                "NoteChanged must refresh menu text.");
            Assert.IsFalse(handler.Contains("RefreshNoteTabs();"),
                "Content autosave must not refresh side tabs.");
        }

        [TestMethod]
        public void SideTabs_KeepTopMostAndOnlyRebuildForSplitChanges()
        {
            string source = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string zOrder = Between(source, "private void ApplyNoteTabZOrder",
                "private void PositionNoteTabs");
            string position = Between(source, "private void PositionNoteTabs",
                "private void ShowStickyNotesManager");
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(zOrder.Contains(".TopMost =") &&
                zOrder.Contains("BringToFront()") &&
                !zOrder.Contains("RaiseVisibleNotesAboveTabs") &&
                !zOrder.Contains("_noteWindows") &&
                !zOrder.Contains("_notes.GetAll"),
                "Side-tab chrome must own its TopMost policy without legacy Window routing.");
            Assert.IsTrue(tabs.Contains("ShowWithoutActivation") &&
                tabs.Contains("WS_EX_NOACTIVATE"),
                "Stable TopMost tabs must remain non-activating.");
            Assert.IsTrue(position.Contains("IsLayoutSplitCurrent") &&
                position.Contains("_noteTabsSignature = String.Empty") &&
                position.Contains("RefreshNoteTabs();") &&
                position.Contains("ShowNear(Bounds, work)"),
                "Positioning must rebuild only an invalid split and otherwise reposition.");
            Assert.IsTrue(form.Contains("WmSettingChange") &&
                form.Contains("WmDisplayChange") &&
                form.Contains("BeginInvoke(new Action(PositionNoteTabs))") &&
                tabs.Contains("TopMost = true") &&
                tabs.Contains("BringToFront()") &&
                !tabs.Contains("Activate()"),
                "Display/work-area changes must revalidate non-activating tab chrome.");
        }

        [TestMethod]
        public void OwnNoteTyping_StillTriggersAnimation()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source, "form.TypingActivity += delegate",
                "form.CancelReminderRequested");

            Assert.IsTrue(handler.Contains("TriggerTypingAnimation();"),
                "Own-note typing should restore the typing animation.");
            Assert.IsFalse(handler.Contains("_typingSession = false"),
                "Own-note typing must not clear the animation session.");
        }

        [TestMethod]
        public void AnimationTick_DoesNotPauseForOwnNoteFocus()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string tick = Between(source, "private void AnimationTick",
                "private int RuntimeFrameCount");

            Assert.IsFalse(tick.Contains("HasFocusedOwnNoteTextInput()"),
                "Animation must not pause merely because a note editor has focus.");
            Assert.IsFalse(tick.Contains("ShouldPauseOwnNoteAnimation"),
                "Animation must not pause for own-note IME composition.");
        }

        [TestMethod]
        public void AnimationRendering_DoesNotChangeWindowFocusOrActivation()
        {
            string runtime = ReadSource("PetAnimationRuntime.cs");
            string renderer = ReadSource("LayeredSpriteRenderer.cs");

            Assert.IsFalse(runtime.Contains(".Activate(") ||
                runtime.Contains(".Focus(") || runtime.Contains(".BringToFront("),
                "Animation runtime must not activate or focus windows.");
            Assert.IsFalse(renderer.Contains(".Activate(") ||
                renderer.Contains(".Focus(") || renderer.Contains(".BringToFront("),
                "Layered renderer must not activate or focus windows.");
        }

        [TestMethod]
        public void LayeredRenderer_DoesNotKeepGlobalBitmapHandleCache()
        {
            string source = ReadSource("LayeredSpriteRenderer.cs");
            Assert.IsFalse(source.Contains("Dictionary<Bitmap, IntPtr>") ||
                source.Contains("BitmapHandles"),
                "Layered renderer must not keep a global HBITMAP cache.");
        }

        [TestMethod]
        public void StartupPreload_IncludesTypingAnimationRows()
        {
            string source = ReadSource("PetAnimationRuntime.cs");
            string warmup = Between(source, "int[] warmRows =",
                "foreach (int row in warmRows)");

            Assert.IsTrue(warmup.Contains("WaitingRow") &&
                warmup.Contains("ThinkingRow"),
                "Typing animation rows must be preloaded during startup.");
        }

        [TestMethod]
        public void StickyPersistence_AllWritersShareGenerationCheckedIoGate()
        {
            string source = ReadSource(
                "Features/StickyNotes/StickyNoteRepository.cs");
            string synchronous = Between(source,
                "internal PersistenceResult SaveToFile",
                "internal PersistenceResult ExportSnapshot");
            string asynchronous = Between(source, "private void AsyncWriterLoop",
                "internal PersistenceResult SaveToFile");
            string physicalWrite = Between(source,
                "private PersistenceResult WriteSnapshot",
                "internal static bool RepairForDisplay");

            Assert.IsFalse(synchronous.Contains("WaitForPendingSaves();"),
                "Synchronous saves must not rely on a race-prone wait-before-write.");
            Assert.IsTrue(synchronous.Contains("WriteSnapshot(filePath, snapshot,"),
                "Synchronous saves must use the shared physical writer.");
            Assert.IsTrue(asynchronous.Contains("WriteSnapshot(_filePath, snapshot,"),
                "Asynchronous saves must use the shared physical writer.");
            int ioGate = physicalWrite.IndexOf("lock (_ioGate)",
                StringComparison.Ordinal);
            int generationCheck = physicalWrite.IndexOf(
                "generation < _lastWrittenGeneration", StringComparison.Ordinal);
            int diskWrite = physicalWrite.IndexOf("AtomicTextFile.WriteAllLines",
                StringComparison.Ordinal);
            Assert.IsTrue(ioGate >= 0 && generationCheck > ioGate &&
                diskWrite > generationCheck,
                "Generation must be checked after winning the IO gate and before disk write.");
        }

        [TestMethod]
        public void StickyUiHost_CommandBoundaryIsAsynchronous()
        {
            string host = ReadSource("StickyUiHost.cs");
            string threadHost = ReadSource("StickyUiThreadHost.cs");

            Assert.IsTrue(host.Contains("PostCommand(") &&
                host.Contains("_threadHost.Post("),
                "Sticky UI commands must use the asynchronous post boundary.");
            Assert.IsTrue(threadHost.Contains("dispatcher.BeginInvoke("),
                "Sticky UI commands must be dispatched asynchronously.");
            Assert.IsFalse(threadHost.Contains("dispatcher.Invoke("),
                "Pet UI must never synchronously invoke the sticky STA.");
            Assert.IsFalse(host.Contains("SendCommand(") ||
                threadHost.Contains("SendCommand("),
                "The old synchronous command API must not remain available.");
        }

        [TestMethod]
        public void StickyUiCanary_UsesDetachedTypedOwnershipBoundary()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string pet = ReadSource("PetForm.cs");
            string runtime = ReadSource(
                "Features/StickyNotes/StickyHostedRuntime.cs");

            Assert.IsTrue(commands.Contains("StickyNoteUiSnapshot") &&
                commands.Contains("CreateWorkingCopy()") &&
                commands.Contains("StickyUiEventKind"),
                "Hosted notes must exchange typed values and detached copies.");
            Assert.IsTrue(host.Contains(
                "Dictionary<string, StickyWindowSession> _sessions") &&
                session.Contains("private readonly StickyNoteWindow _window") &&
                session.Contains("private long _sequence") &&
                session.Contains("snapshot.CreateWorkingCopy()"),
                "Only sticky STA sessions may own hosted WPF windows.");
            Assert.IsTrue(coordinator.Contains(
                "StickyNoteUiSnapshot.FromData(note)") &&
                coordinator.Contains("snapshot.ApplyTo(canonical)") &&
                pet.Contains("StickyHostedRuntime _hostedRuntime") &&
                runtime.Contains("Dictionary<string, long> _appliedSequences"),
                "Pet must apply each note using an independent sequence.");
        }

        [TestMethod]
        public void StickyUiCanary_DoesNotSynchronouslyWaitAcrossUiThreads()
        {
            string threadHost = ReadSource("StickyUiThreadHost.cs");
            string posted = Between(threadHost, "internal void Post(",
                "internal void StopAcceptingCommands");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsFalse(posted.Contains("Dispatcher.Invoke") ||
                posted.Contains(".Wait(") || posted.Contains(".Result"),
                "Command dispatch must never synchronously wait on the sticky STA.");
            Assert.IsFalse(coordinator.Contains("Control.Invoke") ||
                coordinator.Contains("Dispatcher.Invoke") ||
                coordinator.Contains("Task.Wait") ||
                coordinator.Contains("Task.Result"),
                "Pet-side Canary coordination must stay fully asynchronous.");
        }

        [TestMethod]
        public void StickyUiCanary_ReportsInputFocusForOverlayPrivacy()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string overlay = ReadSource(
                "Features/KeyboardOverlay/PetKeyboardOverlayCoordinator.cs");

            Assert.IsTrue(commands.Contains("InputFocusChanged") &&
                session.Contains("_window.HasFocusedTextInput") &&
                coordinator.Contains(
                    "_hostedRuntime.SetInputFocus(value.NoteId, value.Flag)"),
                "Sticky STA must asynchronously report a plain focus flag.");
            Assert.IsTrue(overlay.Contains(
                "HasFocusedOwnNoteTextInput() ||") &&
                overlay.Contains("IsOwnApplicationInputFocused() || sensitive"),
                "Both keyboard-overlay privacy checks must suppress own input.");
        }

        [TestMethod]
        public void StickyUiCanary_ExternalCloseUsesAsyncFinalSnapshotProtocol()
        {
            string form = ReadSource("PetForm.cs");
            string closing = Between(form,
                "protected override void OnFormClosing",
                "protected override void OnFormClosed");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsTrue(closing.Contains("e.Cancel = true") &&
                closing.Contains("BeginHostedStickyExitIfNeeded()"),
                "External close must pause before PetForm disposal.");
            int apply = coordinator.IndexOf(
                "ApplyHostedStickySnapshot(\n" +
                "                                finalSnapshot.Snapshot",
                StringComparison.Ordinal);
            int prepared = coordinator.IndexOf(
                "_hostedRuntime.PrepareExit()", StringComparison.Ordinal);
            Assert.IsTrue(apply >= 0 && prepared > apply,
                "Final snapshot must reach the canonical owner before close resumes.");
        }

        [TestMethod]
        public void StickyUiRegistry_CloseAllPreflightsImeAndSuppressesEvents()
        {
            string host = ReadSource("StickyUiHost.cs");
            int preflight = host.IndexOf(
                "session.IsImeCompositionActive",
                StringComparison.Ordinal);
            int batch = host.IndexOf("session.SetEventsSuppressed(true)",
                StringComparison.Ordinal);

            Assert.IsTrue(preflight >= 0 && batch > preflight &&
                host.Contains("session.FlushAndCaptureFinal()") &&
                host.Contains("StickyUiFinalSnapshot"),
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
        public void StickyUiCanary_LeavesStartupLoadingAndExcludedTypesOnLegacyPath()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsFalse(startup.Contains("HostedSticky") ||
                startup.Contains("StickyUiHost"),
                "Step 1 must not change startup restore or loading readiness.");
            Assert.IsTrue(coordinator.Contains("String.IsNullOrEmpty(note.DockGroupId)") &&
                coordinator.Contains("String.IsNullOrEmpty(note.DockParentId)") &&
                coordinator.Contains("_noteWindows.TryGetValue(note.Id"),
                "Dock notes must remain on legacy UI.");
        }

        [TestMethod]
        public void LegacyDock_UsesDetachedFactsAndTypedTargetEffectBoundary()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(form.Contains("_activeDockGroupIds") &&
                form.Contains("_activeDockCurrentFacts") &&
                coordinator.Contains("CalculateDockTranslationTargets") &&
                coordinator.Contains("ApplyDockTargets"),
                "Dock session geometry must be note-id/facts based.");
            Assert.IsFalse(coordinator.Contains("Object.ReferenceEquals") ||
                coordinator.Contains("member.Location ="),
                "Dock decisions and group motion must not use Window/model identity.");
        }

        [TestMethod]
        public void StartupRestore_TracksExpectedAndRenderedNoteIds()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string form = ReadSource("PetForm.cs");

            Assert.IsTrue(startup.Contains(
                "_expectedFirstRenderNoteIds.Clear()"),
                "Startup restore must reset expected first-render ids.");
            Assert.IsTrue(startup.Contains(
                "_renderedFirstRenderNoteIds.Clear()"),
                "Startup restore must reset rendered first-render ids.");
            Assert.IsTrue(startup.Contains(
                "_expectedFirstRenderNoteIds.Add(member.Id)"),
                "Startup restore must expect each restored note member.");
            Assert.IsTrue(startup.Contains(
                "AllExpectedNotesHaveFirstRendered()"),
                "Startup completion must wait on the expected set.");
            Assert.IsTrue(form.Contains("_expectedFirstRenderNoteIds") &&
                form.Contains("_renderedFirstRenderNoteIds"),
                "PetForm must own the startup readiness id sets.");
        }

        [TestMethod]
        public void HostedRuntime_ConsolidatesOnlyPetThreadProtocolState()
        {
            string form = ReadSource("PetForm.cs");
            string runtime = ReadSource(
                "Features/StickyNotes/StickyHostedRuntime.cs");

            Assert.IsTrue(form.Contains(
                "StickyHostedRuntime _hostedRuntime") &&
                form.Contains("_expectedFirstRenderNoteIds") &&
                form.Contains("_renderedFirstRenderNoteIds"),
                "Hosted runtime must not absorb shared startup readiness state.");
            Assert.IsFalse(form.Contains("_hostedNoteIds") ||
                form.Contains("_hostedAppliedSequences") ||
                form.Contains("_hostedImeComposing") ||
                form.Contains("_hostedInputFocused") ||
                form.Contains("_hostedDeletePending") ||
                form.Contains("_hostedExitRequested") ||
                form.Contains("_hostedCloseAllInFlight") ||
                form.Contains("_hostedExitPrepared"),
                "PetForm must not scatter hosted protocol state.");
            Assert.IsTrue(runtime.Contains("_noteIds") &&
                runtime.Contains("_appliedSequences") &&
                runtime.Contains("_imeComposing") &&
                runtime.Contains("_inputFocused") &&
                runtime.Contains("_deletePending") &&
                runtime.Contains("ExitRequested") &&
                runtime.Contains("CloseAllInFlight") &&
                runtime.Contains("ExitPrepared"),
                "Runtime must own hosted membership, sequence, input and exit state.");
            Assert.IsFalse(runtime.Contains("StickyNoteWindow") ||
                runtime.Contains("StickyNoteRepository"),
                "Hosted runtime must not own WPF windows or persistence.");
        }

        [TestMethod]
        public void HostedFirstRendered_UpdatesPetReadiness()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator =
                ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string command = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");

            Assert.IsTrue(command.Contains("FirstRendered"),
                "StickyUiEventKind must include FirstRendered.");
            Assert.IsTrue(session.Contains(
                "StickyUiEventKind.FirstRendered"),
                "StickyUiHost must emit FirstRendered.");
            Assert.IsTrue(coordinator.Contains(
                "MarkFirstRendered(value.NoteId)"),
                "Pet coordinator must mark hosted first render.");
        }

        [TestMethod]
        public void HostedCreateFallback_AdjustsReadinessSets()
        {
            string coordinator =
                ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string fallback = Between(coordinator,
                "private void FallBackHostedStickyToLegacy",
                "private static void ReportHostedStickyCommandFailure");

            Assert.IsTrue(fallback.Contains(
                "_renderedFirstRenderNoteIds.Remove(noteId)"),
                "Fallback must remove the failed hosted id from rendered.");
            Assert.IsTrue(fallback.Contains(
                "_expectedFirstRenderNoteIds.Add(noteId)"),
                "Fallback must keep the note expected on the legacy path.");
        }

        [TestMethod]
        public void SideTabs_UseTypedSnapshotAndNoteIdActions()
        {
            string tabs = ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            string snapshots = ReadSource(
                "Features/StickyNotes/SideTabSnapshot.cs");

            Assert.IsTrue(snapshots.Contains("NoteId") &&
                snapshots.Contains("ColorArgb") &&
                snapshots.Contains("Visible"),
                "Side tabs must consume a pure value snapshot.");
            Assert.IsTrue(tabs.Contains("Action<string> _openNote") &&
                tabs.Contains("Action<string> _deleteNote") &&
                tabs.Contains("Action<string, int> _reorderNote"),
                "Side tab user actions must be typed note-id actions.");
            Assert.IsTrue(tabs.Contains(
                "SetNotes(IList<SideTabSnapshot>"),
                "Side tabs must accept snapshot input.");
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
                protocol.Contains("StickyUiEvent HorizontalResize("),
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
                commands.Contains("DockDividerResizing") &&
                commands.Contains("SetDockResizeRole") &&
                commands.Contains("CloseRequested"),
                "Dock protocol must expose header drag and resize event kinds.");
            Assert.IsTrue(session.Contains("_window.HeaderDragStarted +=") &&
                session.Contains("_window.HeaderDragMoved +=") &&
                session.Contains("_window.HeaderDragCompleted +=") &&
                session.Contains("_window.DockHorizontalResizing +=") &&
                session.Contains("EmitSnapshot(") &&
                host.Contains("StickyUiCommandKind.SetDockResizeRole") &&
                session.Contains("role.SplitBottom") &&
                session.Contains("role.DividerMinimumHeight") &&
                session.Contains("role.DividerMaximumHeight") &&
                session.Contains("_window.DockDividerResizeActive") &&
                session.Contains("!_applyingBounds") &&
                session.Contains("StickyUiEventKind.CloseRequested"),
                "StickyUiHost must forward dock drag/resize events.");
        }

        [TestMethod]
        public void HostedDock_ReusesNeutralSessionAndTypedEffectBoundary()
        {
            string windowCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string dockCoordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");

            Assert.IsTrue(windowCoordinator.Contains(
                "BeginStickyDockDrag(facts, null)") &&
                windowCoordinator.Contains("MoveStickyDockDrag(facts, null)") &&
                windowCoordinator.Contains("CompleteStickyDockDrag(facts)"),
                "Hosted drag facts must enter the existing Dock session.");
            Assert.IsTrue(dockCoordinator.Contains(
                "StickyUiCommand.SetBounds(") &&
                dockCoordinator.Contains("StickyUiCommand.SetTopMost(") &&
                dockCoordinator.Contains("ApplyDockTargets") &&
                dockCoordinator.Contains("ResizeStickyDockGroup") &&
                dockCoordinator.Contains("ResizeStickyDockDivider") &&
                dockCoordinator.Contains("CalculateDockDividerTargets") &&
                dockCoordinator.Contains("CloseStickyDockNote") &&
                dockCoordinator.Contains("_noteWindows.TryGetValue"),
                "Hosted and legacy effects must share ApplyDockTarget(s).");
            Assert.IsFalse(windowCoordinator.Contains("DockMergeRequested") ||
                windowCoordinator.Contains("DockAttached") ||
                windowCoordinator.Contains("DockCompleted") ||
                dockCoordinator.Contains("HostedDockCoordinator"),
                "The minimum E2E must not add a second Dock protocol.");
        }

        [TestMethod]
        public void DockVisualFeedback_UsesDetachedFactsForEveryExecutor()
        {
            string form = ReadSource("PetForm.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string begin = Between(coordinator,
                "private void BeginStickyDockDrag",
                "private void StickyNoteHeaderDragMoved");
            string moveVisuals = Between(coordinator,
                "RememberActiveDockFacts(moveTargets);",
                "private void StickyNoteHeaderDragCompleted");
            string mergeVisuals = Between(coordinator,
                "List<StickyNoteData> mergedSnapshot",
                "else if (!_activeNoteDragHosted)");
            string helpers = Between(coordinator,
                "private void ShowSplitGuide",
                "private DockTarget FindDockTarget");

            Assert.IsTrue(begin.Contains("ShowSplitGuide(seed, groupFacts)") &&
                !begin.Contains("_activeNoteSplitEligible && " +
                    "!_activeNoteDragHosted"),
                "Hosted split candidates must receive the same guide.");
            Assert.IsTrue(moveVisuals.Contains(
                    "UpdateSplitGuide(seed, _activeDockCurrentFacts)") &&
                moveVisuals.Contains("UpdateDockPreview(seed, previewFacts)") &&
                !moveVisuals.Contains("_activeNoteDragHosted"),
                "Hosted drag must not be gated out of preview updates.");
            Assert.IsTrue(mergeVisuals.Contains("ShowTransientDockPulse") &&
                !mergeVisuals.Contains("if (!_activeNoteDragHosted)"),
                "Merge pulse must be independent of the source executor.");
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
            string form = ReadSource("PetForm.cs");
            string menu = ReadSource("PetContextMenu.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string action = Between(coordinator,
                "private void ExpandAndTileAllStickyNotesToPetScreen",
                "internal static List<DockLayoutTarget>");
            string preparation = Between(coordinator,
                "PrepareStickyExpandAndTileTargets(IList<StickyNoteData> notes,",
                "internal static List<Rectangle> CalculateStickyRecoveryLayout");

            Assert.IsTrue(menu.Contains("展开全部并平铺到此屏幕") &&
                form.Contains("ExpandAndTileAllStickyNotesToPetScreen"),
                "The menu must expose the new expand-and-tile product action.");
            Assert.IsTrue(action.Contains("IsHostedSticky(note)") &&
                action.Contains("StickyUiCommand.Show(note.Id, false)") &&
                action.Contains("ApplyDockTarget(target, null)") &&
                action.Contains("ShowStickyNote(note, false, false, false)"),
                "Hosted and legacy notes must use their owned effect edges.");
            Assert.IsFalse(action.Contains("GetOrCreateStickyNoteWindow") ||
                action.Contains("seed.Visible"),
                "Recovery must neither skip hidden notes nor use a universal legacy route.");
            Assert.IsTrue(preparation.Contains(
                "StickyDockGroups.ClearMembership(note)") &&
                preparation.Contains("note.Visible = true") &&
                preparation.Contains("CalculateStickyRecoveryLayout"),
                "Preparation must detach, expand, and independently tile every note.");
        }

        [TestMethod]
        public void DockParticipantEligibility_DoesNotDependOnStickySubtypeOrExecutor()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string gate = Between(coordinator,
                "private bool IsDockParticipant",
                "private string FindDockChild");

            Assert.IsTrue(coordinator.Contains(
                "CanUseDockComponents("),
                "Hosted dock gate must be used for hosted drag targets.");
            Assert.IsFalse(gate.Contains("!note.IsTodoList") ||
                gate.Contains("!note.IsSchedule"),
                "Todo and Schedule must be allowed to dock with ordinary notes.");
            Assert.IsFalse(gate.Contains("IsHostedSticky"),
                "Dock participant eligibility must not depend on hosted/legacy ownership.");
            Assert.IsFalse(gate.Contains("ReminderUtcTicks"),
                "Reminder is not a sticky subtype and must not affect Dock eligibility.");
        }

        private static string ReadSource(string relativePath)
        {
            string root = FindDesktopPetDirectory();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
        }

        private static string Between(string text, string startMarker, string endMarker)
        {
            int start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing marker: " + startMarker);
            int end = text.IndexOf(endMarker, start + startMarker.Length,
                StringComparison.Ordinal);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start);
        }

        private static string FindDesktopPetDirectory()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName,
                    "PennyPet.Windows.csproj")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the PennyPet Windows source directory.");
        }
    }
}
