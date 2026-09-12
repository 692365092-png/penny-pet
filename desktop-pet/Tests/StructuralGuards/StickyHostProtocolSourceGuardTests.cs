using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.SourceGuardText;

namespace PennyPet.Tests
{
    public sealed partial class InputAnimationBoundaryTests
    {
        [TestMethod]
        public void OwnNoteTyping_StillTriggersAnimation()
        {
            string source = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string handler = Between(source,
                "if (value.Kind == StickyUiEventKind.TypingActivity)",
                "if (value.Kind == StickyUiEventKind.InputFocusChanged)");

            Assert.IsTrue(handler.Contains("TriggerTypingAnimation();"),
                "Own-note typing should restore the typing animation.");
            Assert.IsFalse(handler.Contains("_typingSession = false"),
                "Own-note typing must not clear the animation session.");
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
        public void StickyUiHosted_UsesDetachedTypedOwnershipBoundary()
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
                coordinator.Contains("ApplyHostedStickyFactsGeometry") &&
                pet.Contains("StickyHostedRuntime _hostedRuntime") &&
                runtime.Contains("Dictionary<string, long> _appliedSequences"),
                "Pet must apply each note using an independent sequence.");
        }

        [TestMethod]
        public void StickyUiHosted_DoesNotSynchronouslyWaitAcrossUiThreads()
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
                "Pet-side hosted coordination must stay fully asynchronous.");
        }

        [TestMethod]
        public void StickyUiHosted_ReportsInputFocusForOverlayPrivacy()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string overlay = ReadSource(
                "Features/KeyboardOverlay/PetKeyboardOverlayCoordinator.cs");
            string hook = ReadSource(
                "Features/KeyboardOverlay/GlobalKeyboardActivity.cs");

            Assert.IsTrue(commands.Contains("InputFocusChanged") &&
                session.Contains("_window.HasFocusedTextInput") &&
                coordinator.Contains(
                    "_hostedRuntime.SetInputFocus(value.NoteId, value.Flag)"),
                "Sticky STA must asynchronously report a plain focus flag.");
            Assert.IsTrue(overlay.Contains(
                    "ShouldSuppressOwnApplicationInput(focusSnapshot)") &&
                overlay.Contains("HasFocusedOwnNoteTextInput() ||") &&
                overlay.Contains("_windowLayers.HasActiveModal") &&
                overlay.Contains("focusSnapshot.ProcessId ==") &&
                overlay.Contains(
                    "PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput"),
                "Only Sticky text input and an active owned modal may allow own-process overlay input.");
            Assert.IsTrue(hook.Contains("ShouldPublishKey(injected)") &&
                !hook.Contains("ownProcessId") &&
                !hook.Contains("foregroundProcessId"),
                "The hook must capture physical own-process keys for policy evaluation.");
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
        public void StickyTypedProtocol_ExposesReminderUpdateCommand()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string host = ReadSource("StickyUiHost.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(
                commands.Contains("UpdateReminders") &&
                commands.Contains("CopyReminders") &&
                host.Contains("StickyUiCommandKind.UpdateReminders") &&
                session.Contains("UpdateReminders("),
                "Hosted reminder parity needs a detached update command.");
        }

        [TestMethod]
        public void Drt5_HostedWindowConstructor_DoesNotOwnDesktopPlacement()
        {
            string wpf = ReadSource(
                "Features/StickyNotes/StickyNoteWpf.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(wpf.Contains("hostedNativePlacement") &&
                wpf.Contains("initialLogicalBounds.Width") &&
                wpf.Contains("initialLogicalBounds.Height"),
                "Hosted construction must size from the logical DIP model.");
            string hostedBranch = Between(wpf,
                "if (hostedNativePlacement)", "else");
            Assert.IsFalse(hostedBranch.Contains("base.Left = data.X") ||
                hostedBranch.Contains("base.Top = data.Y") ||
                hostedBranch.Contains("data.Width") ||
                hostedBranch.Contains("data.Height"),
                "The hosted path must not feed physical fields into WPF placement.");
            Assert.IsTrue(session.Contains(
                    "new StickyNoteWindow(snapshot.CreateWorkingCopy(),") &&
                session.Contains("false, false, true, initialPlacement"),
                "Hosted sessions must use the native-placement constructor.");
        }

        [TestMethod]
        public void Drt5_Session_PlacesExactlyBeforeShowAndVerifiesFacts()
        {
            string session = ReadSource("StickyWindowSession.cs");
            int ensure = session.IndexOf(
                "_placementExecutor.EnsureHandle()",
                StringComparison.Ordinal);
            int setExact = session.IndexOf(
                "_placementExecutor.SetWindowPosExact(requested)",
                StringComparison.Ordinal);
            int show = session.IndexOf("_placementExecutor.Show()",
                StringComparison.Ordinal);
            int capture = session.IndexOf(
                "_placementExecutor.CaptureFacts(",
                StringComparison.Ordinal);

            Assert.IsTrue(ensure >= 0 && show > ensure,
                "The HWND must be created before the window is shown.");
            Assert.IsTrue(setExact > ensure && show > setExact,
                "The exact physical rect must land before Show.");
            Assert.IsTrue(capture > show,
                "Actual WindowFacts must be captured after Show.");
            Assert.IsTrue(session.Contains(
                    "IsWithinPlacementTolerance") &&
                session.Contains("MoveHiddenToSurface(plan.WorkArea)") &&
                session.Contains("GetDpiForWindow()"),
                "The standalone path must run the full hidden bootstrap.");

            string placement = Between(session,
                "private bool PlaceAtNativeBounds(WindowPlacementPlan plan, bool edit)",
                "private void TracePlacementMismatch");
            Assert.IsFalse(placement.Contains("_window.ShowAtPhysicalBounds") ||
                placement.Contains("ShowRestoredAtPhysicalBounds"),
                "The standalone native path must not reuse the legacy show helper.");
        }

        [TestMethod]
        public void DrtCloseout_StickySessionNeverRecapturesTopology()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string host = ReadSource("StickyUiHost.cs");

            Assert.IsFalse(session.Contains(
                    "new WindowsDisplayTopologyProvider()"),
                "The sticky STA must not capture Windows topology itself.");
            Assert.IsTrue(session.Contains("_topology") &&
                session.Contains("topology.FindByRuntimeGdiName("),
                "Placement must resolve against the Pet-owned topology.");
            Assert.IsTrue(host.Contains("SetCurrentTopology(") &&
                !host.Contains("WindowsDisplayTopologyProvider"),
                "The host facade may hold the Pet-published topology but must never capture it.");
        }

        [TestMethod]
        public void Drt6_RestorePolicyIsSelectedBeforeTheStaBoundary()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string coordinator = ReadSource("Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string host = ReadSource("StickyUiHost.cs");
            Assert.IsFalse(session.Contains("data.PreferredDisplayTargetKey") ||
                session.Contains("data.LocalLogicalWidth") || session.Contains("ResolvePlacementPlan"));
            StringAssert.Contains(coordinator, "StickyPlacementRecovery.SelectForShow(note, topology)");
            StringAssert.Contains(host, "command.Placement");
        }

        [TestMethod]
        public void Drt6_PreferredStaysOutsideFullSnapshotApply()
        {
            string commands = ReadSource(
                "Features/StickyNotes/StickyUiCommand.cs");
            string apply = Between(commands,
                "internal void ApplyTo(StickyNoteData target)",
                "internal void ApplyPreferredTo(StickyNoteData target)");

            Assert.IsTrue(commands.Contains(
                    "internal void ApplyPreferredTo(StickyNoteData target)") &&
                commands.Contains("ApplyPreferredTo(copy)"),
                "Working copies must carry the preferred placement separately.");
            Assert.IsFalse(apply.Contains("PreferredDisplayTargetKey"),
                "Full snapshot application must never clobber preferred placement.");
        }

        [TestMethod]
        public void Drt7_TopologyRehomePublishesFreshWindowFacts()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsTrue(coordinator.Contains(
                    "StickyUiCommand.Reproject(rehomedNoteId,") &&
                coordinator.Contains("ApplyReprojectResult(result, rehomedNoteId, snapshot)"),
                "A topology rehome must complete through the actual-facts result path.");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private WindowFacts CorrectReprojectionOnce");
            Assert.IsTrue(reproject.Contains(
                    "StickyUiCommandResult.Handled(_lastSnapshot,") &&
                reproject.Contains("resultSequence,") &&
                reproject.Contains("facts, _topology") &&
                reproject.Contains("RollbackReproject(wasVisible, previousBounds)"),
                "Reproject must return captured facts and roll back on failure.");
        }

        [TestMethod]
        public void Drt7_MissingStartupTargetUsesDetachedTemporarySnapshot()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string start = Between(coordinator,
                "private void StartHostedSticky(",
                "private bool IsHostedSticky(");

            Assert.IsTrue(start.Contains(
                    "TryBuildTemporaryRehomeTarget(note") &&
                start.Contains("StickyUiCommand.Create(") &&
                start.Contains("rehomeTarget, StickyPlacementRecovery.SelectForShow") &&
                start.Contains(
                    "preferred-display-missing-at-restore"),
                "Startup restore must attach a typed rehome target without rewriting the durable preferred target.");
            Assert.IsFalse(start.Contains("temporary.ApplyTo(note)"),
                "Temporary startup geometry must not mutate the canonical note before the hosted result succeeds.");
        }

        [TestMethod]
        public void Drt67Closeout_RehomeUsesNativeReprojectNotMonitorDpi()
        {
            string coordinator = ReadSource(
                "Features/StickyNotes/PetStickyWindowCoordinator.cs");
            string session = ReadSource("StickyWindowSession.cs");

            Assert.IsFalse(coordinator.Contains(
                    "ResolveTemporaryRehomePlacement") ||
                coordinator.Contains("WindowsDisplayResolver.ResolveDisplay"),
                "GetDpiForMonitor must not shape the temporary rehome.");
            Assert.IsTrue(coordinator.Contains(
                    "TryBuildTemporaryRehomeTarget(") &&
                coordinator.Contains("StickyUiCommand.Reproject("),
                "Rehome must flow through the typed native reproject command.");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private WindowFacts CorrectReprojectionOnce");
            Assert.IsTrue(reproject.Contains("GetDpiForWindow()") &&
                reproject.Contains("MoveHiddenToSurface(") &&
                reproject.Contains("SetWindowPosExact(projected)"),
                "The reproject must use the real window DPI on the sticky STA.");
            Assert.IsFalse(reproject.Contains("PreferredDisplayTargetKey"),
                "Reprojection must never modify the durable preferred fields.");
        }

        [TestMethod]
        public void Drt67Closeout_PreferredReturnHidesBeforeBootstrap()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private WindowFacts CorrectReprojectionOnce");
            int hide = reproject.IndexOf("_window.Hide()",
                StringComparison.Ordinal);
            int move = reproject.IndexOf("MoveHiddenToSurface(",
                StringComparison.Ordinal);

            Assert.IsTrue(reproject.Contains("bool wasVisible =") &&
                reproject.Contains("if (wasVisible) _window.Hide();") &&
                hide >= 0 && move > hide,
                "A visible window must be hidden before the target-surface bootstrap.");
            Assert.IsTrue(reproject.Contains(
                    "StickySpawnPolicy.CenterInWorkArea("),
                "The rehome path must center-fit the preferred logical size.");
            string correction = Between(session,
                "private WindowFacts CorrectReprojectionOnce",
                "internal StickyUiCommandResult Close()");
            Assert.IsTrue(correction.Contains("IsWithinPlacementTolerance") &&
                correction.Contains("SetWindowPosExact(requested)"),
                "The reproject must allow one bounded correction.");
        }

        [TestMethod]
        public void ReprojectHandledRequiresNonNullFacts()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private WindowFacts CorrectReprojectionOnce");
            int factsGate = reproject.IndexOf("facts == null || _topology == null",
                StringComparison.Ordinal);
            int handled = reproject.IndexOf(
                "StickyUiCommandResult.Handled(_lastSnapshot,",
                StringComparison.Ordinal);
            Assert.IsTrue(factsGate >= 0 && handled > factsGate);
        }

        [TestMethod]
        public void ReprojectFactsSequenceEqualsResultSequence()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string reproject = Between(session,
                "internal StickyUiCommandResult Reproject(",
                "private WindowFacts CorrectReprojectionOnce");
            Assert.IsTrue(reproject.Contains(
                    "long resultSequence = ++_sequence;") &&
                reproject.Contains(
                    "CorrectReprojectionOnce(projected, resultSequence)") &&
                reproject.Contains(
                    "facts.WindowSequence != resultSequence") &&
                reproject.Contains("resultSequence,\n                facts"));
        }
    }
}
