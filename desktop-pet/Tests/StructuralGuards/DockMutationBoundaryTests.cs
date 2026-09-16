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

        [TestMethod]
        public void FinalScopeIsCapturedBeforeHiddenMembersAreRemovedFromTheNativePlan()
        {
            string final = SliceMethod(SourceGuardText.ReadStickyWorkflowSource(), "private void StartDockFinalization(");
            int scope = final.IndexOf("var affectedMembers = new List<StickyNoteData>(finalMembers)", StringComparison.Ordinal);
            int visible = final.IndexOf("finalMembers.RemoveAll(note => !note.Visible)", StringComparison.Ordinal);
            int begin = final.IndexOf("Interaction.BeginFinalizing(", StringComparison.Ordinal);
            int capture = final.IndexOf("StickyUiCommand.CaptureDockFacts(", StringComparison.Ordinal);
            Assert.IsTrue(scope >= 0 && visible > scope && begin > visible && capture > begin);
            Assert.IsTrue(final.Contains("affectedMembers.AddRange(BuildDockChainOrderIncludingHidden(remainderSeed))"));
            Assert.IsTrue(final.Contains("foreach (StickyNoteData member in affectedMembers) CancelHostedDockRestores(member.Id)"));
            Assert.IsTrue(final.Contains("finalMembers.ConvertAll(note => note.Id), affectedMembers"));
        }

        [TestMethod]
        public void OrdinaryAndTopologyDrivenRestoresWaitBeforePreparingOrMutatingAnything()
        {
            string source = SourceGuardText.ReadStickyWorkflowSource();
            string restore = SliceMethod(source, "internal bool TryRestoreHostedDockComponent(");
            int defer = restore.IndexOf("DeferDockMutation(rootId", StringComparison.Ordinal);
            Assert.IsTrue(defer >= 0 && defer < restore.IndexOf("MigrateDockRestorePreferredIfNeeded", StringComparison.Ordinal));
            Assert.IsTrue(defer < restore.IndexOf("DockRestoreOperation.TryCreate", StringComparison.Ordinal));
            string owner = SliceMethod(source, "private DockMutationQueue FindDockMutationOwner(");
            Assert.IsTrue(owner.Contains("Interaction.Mutations") && owner.Contains("Gestures.Resize.Mutations"));
            Assert.IsFalse(source.Contains("DeferDockResizeMutation"));
            string post = SliceMethod(source, "private void PostDockGroupTopologyReproject(");
            Assert.IsTrue(post.Contains("FindDockMutationOwner(root.Id) != null"));
            Assert.IsTrue(post.LastIndexOf("FindDockMutationOwner(root.Id)", StringComparison.Ordinal) >
                post.IndexOf("delegate(StickyUiCommandResult result)", StringComparison.Ordinal));
        }

        [TestMethod]
        public void OwnersAndMailboxesAreRetiredBeforeQueuedActionsCanReenter()
        {
            string dock = SourceGuardText.ReadStickyWorkflowSource();
            string reset = SliceMethod(dock, "internal void ResetDockDragState(");
            int retire = reset.IndexOf("Gestures.ResetDrag(clearMailbox)", StringComparison.Ordinal);
            int invalidate = reset.IndexOf("SetCurrentDockInteractionEpoch", StringComparison.Ordinal);
            int run = reset.IndexOf("RunDeferredDockMutations(deferred)", StringComparison.Ordinal);
            Assert.IsTrue(retire >= 0 && invalidate > retire && run > invalidate);
            string final = SliceMethod(dock, "private void StartDockFinalization(");
            int finish = final.IndexOf("Interaction.TryFinish(epoch, topology.Generation, out invalidatingEpoch, out deferred)", StringComparison.Ordinal);
            Assert.IsTrue(finish >= 0 && final.IndexOf("RunDeferredDockMutations(deferred)", StringComparison.Ordinal) > finish);
            string clear = SliceMethod(SourceGuardText.ReadStickyWorkflowSource(), "internal void ClearHostedDockResizeSession(");
            Assert.IsTrue(clear.Contains("RunDeferredDockMutations(Gestures.FinishResize(expected))"));
            Assert.IsFalse(ReadSource("Features/StickyNotes/DockResizeSession.cs").Contains("_afterFinal"));
        }

        [TestMethod]
        public void LifecycleFailuresRetireFinalizationAndDeferredActionsResolveCurrentNotes()
        {
            string window = SourceGuardText.ReadStickyWorkflowSource(), dock = SourceGuardText.ReadStickyWorkflowSource();
            Assert.IsTrue(SliceMethod(window, "internal void HostedStickyFaulted(").Contains("ResetDockDragState(true)"));
            Assert.IsTrue(SliceMethod(window, "private void HandleHostedStickyFailure(").Contains(
                "cancelHeaderFinal && ReferenceEquals(Dock.Interaction.Mutations, failedFinal)"));
            string closed = SliceMethod(window, "if (value.Kind == StickyUiEventKind.Closed)");
            Assert.IsTrue(closed.IndexOf("if (!ApplyHostedStickyEvent(value)) return", StringComparison.Ordinal) <
                closed.IndexOf("Hosted.RemoveNote", StringComparison.Ordinal));
            Assert.IsTrue(SliceMethod(window, "private void PostResizeFinal(").Contains("ClearHostedDockResizeSession(session)"));
            Assert.IsTrue(window.Contains("ShowHostedSticky(Notes.Find(note.Id), focusEditor, persistVisibility)"));
            Assert.IsTrue(dock.Contains("DeleteStickyNote(Notes.Find(note.Id), completed)"));
            string exit = SliceMethod(window, "internal bool BeginHostedStickyExitIfNeeded()");
            Assert.IsTrue(exit.IndexOf("DeferDockMutation", StringComparison.Ordinal) < exit.IndexOf("CancelHostedDockRestores", StringComparison.Ordinal));
        }
    }
}
