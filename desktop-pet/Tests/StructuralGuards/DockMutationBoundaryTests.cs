using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // These guards verify Windows adapter wiring, not native event timing.
    [TestClass]
    [TestCategory("ArchitectureSourceBoundary")]
    public sealed class DockMutationBoundaryTests
    {
        private const string Dock = "Features/StickyNotes/PetStickyDockCoordinator.cs";
        private const string Window = "Features/StickyNotes/PetStickyWindowCoordinator.cs";

        [TestMethod]
        public void FinalScopeIsCapturedBeforeHiddenMembersAreRemovedFromTheNativePlan()
        {
            string final = SliceMethod(ReadSource(Dock), "private void StartDockFinalization(");
            int scope = final.IndexOf("var affectedMembers = new List<StickyNoteData>(finalMembers)", StringComparison.Ordinal);
            int visible = final.IndexOf("finalMembers.RemoveAll(note => !note.Visible)", StringComparison.Ordinal);
            int begin = final.IndexOf("_dockInteraction.BeginFinalizing(", StringComparison.Ordinal);
            int capture = final.IndexOf("StickyUiCommand.CaptureDockFacts(", StringComparison.Ordinal);
            Assert.IsTrue(scope >= 0 && visible > scope && begin > visible && capture > begin);
            Assert.IsTrue(final.Contains("affectedMembers.AddRange(BuildDockChainOrderIncludingHidden(remainderSeed))"));
            Assert.IsTrue(final.Contains("foreach (StickyNoteData member in affectedMembers) CancelHostedDockRestores(member.Id)"));
            Assert.IsTrue(final.Contains("finalMembers.ConvertAll(note => note.Id), affectedMembers"));
        }

        [TestMethod]
        public void OrdinaryAndTopologyDrivenRestoresWaitBeforePreparingOrMutatingAnything()
        {
            string source = ReadSource(Window);
            string restore = SliceMethod(source, "private bool TryRestoreHostedDockComponent(");
            int defer = restore.IndexOf("DeferDockMutation(rootId", StringComparison.Ordinal);
            Assert.IsTrue(defer >= 0 && defer < restore.IndexOf("MigrateDockRestorePreferredIfNeeded", StringComparison.Ordinal));
            Assert.IsTrue(defer < restore.IndexOf("DockRestoreOperation.TryCreate", StringComparison.Ordinal));
            string owner = SliceMethod(source, "private DockMutationQueue FindDockMutationOwner(");
            Assert.IsTrue(owner.Contains("_dockInteraction.Mutations") && owner.Contains("_dockResize.Mutations"));
            Assert.IsFalse(source.Contains("DeferDockResizeMutation"));
            string post = SliceMethod(source, "private void PostDockGroupTopologyReproject(");
            Assert.IsTrue(post.Contains("FindDockMutationOwner(root.Id) != null"));
            Assert.IsTrue(post.LastIndexOf("FindDockMutationOwner(root.Id)", StringComparison.Ordinal) >
                post.IndexOf("delegate(StickyUiCommandResult result)", StringComparison.Ordinal));
        }

        [TestMethod]
        public void OwnersAndMailboxesAreRetiredBeforeQueuedActionsCanReenter()
        {
            string dock = ReadSource(Dock);
            string reset = SliceMethod(dock, "private void ResetDockDragState(");
            int invalidate = reset.IndexOf("SetCurrentDockInteractionEpoch", StringComparison.Ordinal);
            int mailbox = reset.IndexOf("_dockPlanMailbox.Clear()", StringComparison.Ordinal);
            int run = reset.IndexOf("RunDeferredDockMutations(deferred)", StringComparison.Ordinal);
            Assert.IsTrue(invalidate >= 0 && mailbox > invalidate && run > mailbox);
            string final = SliceMethod(dock, "private void StartDockFinalization(");
            int finish = final.IndexOf("_dockInteraction.TryFinish(epoch, topology.Generation, out invalidatingEpoch, out deferred)", StringComparison.Ordinal);
            Assert.IsTrue(finish >= 0 && final.IndexOf("RunDeferredDockMutations(deferred)", StringComparison.Ordinal) > finish);
            string clear = SliceMethod(ReadSource(Window), "private void ClearHostedDockResizeSession(");
            Assert.IsTrue(clear.IndexOf("_dockResize = null", StringComparison.Ordinal) < clear.IndexOf("previous.Finish()", StringComparison.Ordinal));
            Assert.IsFalse(ReadSource("Features/StickyNotes/DockResizeSession.cs").Contains("_afterFinal"));
        }

        [TestMethod]
        public void LifecycleFailuresRetireFinalizationAndDeferredActionsResolveCurrentNotes()
        {
            string window = ReadSource(Window), dock = ReadSource(Dock);
            Assert.IsTrue(SliceMethod(window, "private void HostedStickyFaulted(").Contains("ResetDockDragState(true)"));
            Assert.IsTrue(SliceMethod(window, "private void HandleHostedStickyFailure(").Contains(
                "cancelHeaderFinal && ReferenceEquals(_dockInteraction.Mutations, failedFinal)"));
            string closed = SliceMethod(window, "if (value.Kind == StickyUiEventKind.Closed)");
            Assert.IsTrue(closed.IndexOf("if (!ApplyHostedStickyEvent(value)) return", StringComparison.Ordinal) <
                closed.IndexOf("_hostedRuntime.RemoveNote", StringComparison.Ordinal));
            Assert.IsTrue(SliceMethod(window, "private void PostResizeFinal(").Contains("ClearHostedDockResizeSession(session)"));
            Assert.IsTrue(window.Contains("ShowHostedSticky(_notes.Find(note.Id), focusEditor, persistVisibility)"));
            Assert.IsTrue(dock.Contains("DeleteStickyNote(_notes.Find(note.Id), completed)"));
            string exit = SliceMethod(window, "private bool BeginHostedStickyExitIfNeeded()");
            Assert.IsTrue(exit.IndexOf("DeferDockMutation", StringComparison.Ordinal) < exit.IndexOf("CancelHostedDockRestores", StringComparison.Ordinal));
        }
    }
}
