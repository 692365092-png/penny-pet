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

            Assert.IsTrue(commands.Contains("StickyNoteUiSnapshot") &&
                commands.Contains("CreateWorkingCopy()") &&
                commands.Contains("StickyUiEventKind"),
                "Canary must exchange typed values and create a detached working copy.");
            Assert.IsTrue(host.Contains(
                "private StickyNoteWindow _canaryWindow;") &&
                host.Contains("new StickyNoteWindow(workingCopy)"),
                "Only StickyUiHost may own the Canary WPF window reference.");
            Assert.IsTrue(coordinator.Contains(
                "StickyNoteUiSnapshot.FromData(note)") &&
                coordinator.Contains("snapshot.ApplyTo(canonical)"),
                "Pet must send snapshots and apply updates to its canonical model.");
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
                host.Contains("window.HasFocusedTextInput") &&
                coordinator.Contains("_canaryInputFocused = value.Flag"),
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
                closing.Contains("BeginStickyCanaryExitIfNeeded()"),
                "External close must pause before PetForm disposal.");
            int apply = coordinator.IndexOf(
                "ApplyStickyCanarySnapshot(result.Snapshot",
                StringComparison.Ordinal);
            int prepared = coordinator.IndexOf(
                "_canaryExitPrepared = true", StringComparison.Ordinal);
            Assert.IsTrue(apply >= 0 && prepared > apply,
                "Final snapshot must reach the canonical owner before close resumes.");
        }

        [TestMethod]
        public void StickyUiCanary_LeavesStartupLoadingAndExcludedTypesOnLegacyPath()
        {
            string startup = ReadSource("PetStartupCoordinator.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");

            Assert.IsFalse(startup.Contains("Canary") ||
                startup.Contains("StickyUiHost"),
                "Canary must not change startup restore or loading readiness.");
            Assert.IsTrue(coordinator.Contains("!note.IsTodoList") &&
                coordinator.Contains("!note.IsSchedule") &&
                coordinator.Contains("note.ReminderUtcTicks <= 0") &&
                coordinator.Contains("String.IsNullOrEmpty(note.DockGroupId)"),
                "Todo, Schedule, Reminder and Dock notes must remain on legacy UI.");
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
