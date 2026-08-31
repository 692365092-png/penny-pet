using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class InputAnimationBoundaryTests
    {
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
            string source = ReadSource("StickyUiHost.cs");

            Assert.IsTrue(source.Contains("PostCommand("),
                "Sticky UI commands must use the asynchronous post boundary.");
            Assert.IsTrue(source.Contains("dispatcher.BeginInvoke("),
                "Sticky UI commands must be dispatched asynchronously.");
            Assert.IsFalse(source.Contains("dispatcher.Invoke("),
                "Pet UI must never synchronously invoke the sticky STA.");
            Assert.IsFalse(source.Contains("SendCommand("),
                "The old synchronous command API must not remain available.");
        }

        [TestMethod]
        public void StickyUiCanary_UsesDetachedTypedOwnershipBoundary()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string pet = ReadSource("PetForm.cs");

            Assert.IsTrue(commands.Contains("StickyNoteUiSnapshot") &&
                commands.Contains("CreateWorkingCopy()") &&
                commands.Contains("StickyUiEventKind"),
                "Hosted notes must exchange typed values and detached copies.");
            Assert.IsTrue(host.Contains(
                "Dictionary<string, StickyWindowEntry> _windows") &&
                host.Contains("internal long Sequence;") &&
                host.Contains("new StickyNoteWindow(workingCopy)"),
                "Only StickyUiHost may own hosted WPF window references.");
            Assert.IsTrue(coordinator.Contains(
                "StickyNoteUiSnapshot.FromData(note)") &&
                coordinator.Contains("snapshot.ApplyTo(canonical)") &&
                pet.Contains("Dictionary<string, long>"),
                "Pet must apply each note using an independent sequence.");
        }

        [TestMethod]
        public void StickyUiCanary_DoesNotSynchronouslyWaitAcrossUiThreads()
        {
            string host = ReadSource("StickyUiHost.cs");
            string posted = Between(host, "internal void PostCommand(",
                "private StickyUiCommandResult HandleCanaryCommand");
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
            string host = ReadSource("StickyUiHost.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string overlay = ReadSource(
                "Features/KeyboardOverlay/PetKeyboardOverlayCoordinator.cs");

            Assert.IsTrue(commands.Contains("InputFocusChanged") &&
                host.Contains("entry.Window.HasFocusedTextInput") &&
                coordinator.Contains("_hostedInputFocused.Add(value.NoteId)"),
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
                "_hostedExitPrepared = true", StringComparison.Ordinal);
            Assert.IsTrue(apply >= 0 && prepared > apply,
                "Final snapshot must reach the canonical owner before close resumes.");
        }

        [TestMethod]
        public void StickyUiRegistry_CloseAllPreflightsImeAndSuppressesEvents()
        {
            string host = ReadSource("StickyUiHost.cs");
            int preflight = host.IndexOf(
                "entry.Window.IsImeCompositionActiveForHost",
                StringComparison.Ordinal);
            int batch = host.IndexOf("_batchClosing = true",
                StringComparison.Ordinal);

            Assert.IsTrue(preflight >= 0 && batch > preflight &&
                host.Contains("if (_batchClosing) return;") &&
                host.Contains("StickyUiFinalSnapshot"),
                "CloseAll must preflight every IME before a quiet final batch.");
        }

        [TestMethod]
        public void StickyUiRegistry_DeletesOnlyAfterHandledCloseAndForwardsRequests()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
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
                host.Contains("window.DeleteRequested +=") &&
                host.Contains("PostWindowRequest("),
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
        public void HostedFirstRendered_UpdatesPetReadiness()
        {
            string host = ReadSource("StickyUiHost.cs");
            string coordinator =
                ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string command = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");

            Assert.IsTrue(command.Contains("FirstRendered"),
                "StickyUiEventKind must include FirstRendered.");
            Assert.IsTrue(host.Contains(
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

            Assert.IsTrue(commands.Contains("SetBounds") &&
                commands.Contains("StickyUiBounds") &&
                commands.Contains("BoundsChanged"),
                "Dock protocol must expose typed bounds command/event data.");
            Assert.IsTrue(host.Contains("StickyUiCommandKind.SetBounds") &&
                host.Contains("StickyUiEventKind.BoundsChanged"),
                "StickyUiHost must execute and report typed bounds changes.");
        }

        [TestMethod]
        public void DockTypedProtocol_ForwardsHeaderDragAndResizeEvents()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");

            Assert.IsTrue(commands.Contains("HeaderDragStarted") &&
                commands.Contains("HeaderDragMoved") &&
                commands.Contains("HeaderDragCompleted") &&
                commands.Contains("DockHorizontalResizing") &&
                commands.Contains("DockDividerResizing") &&
                commands.Contains("SetDockResizeRole") &&
                commands.Contains("CloseRequested"),
                "Dock protocol must expose header drag and resize event kinds.");
            Assert.IsTrue(host.Contains("window.HeaderDragStarted +=") &&
                host.Contains("window.HeaderDragMoved +=") &&
                host.Contains("window.HeaderDragCompleted +=") &&
                host.Contains("window.DockHorizontalResizing +=") &&
                host.Contains("EmitWindowSnapshot(sender") &&
                host.Contains("StickyUiCommandKind.SetDockResizeRole") &&
                host.Contains("role.SplitBottom") &&
                host.Contains("role.DividerMinimumHeight") &&
                host.Contains("role.DividerMaximumHeight") &&
                host.Contains("entry.Window.DockDividerResizeActive") &&
                host.Contains("!entry.ApplyingBounds") &&
                host.Contains("StickyUiEventKind.CloseRequested"),
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
                "StickyUiCommandKind.SetBounds") &&
                dockCoordinator.Contains("StickyUiCommandKind.SetTopMost") &&
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

        private static string ReadSource(string relativePath)
        {
            string root = FindDesktopPetDirectory();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(path);
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
