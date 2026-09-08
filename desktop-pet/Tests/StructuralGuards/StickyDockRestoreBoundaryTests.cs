using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // Source-boundary guards do not prove runtime HWND behavior.
    // Human mixed-DPI restore acceptance remains required.
    [TestClass]
    [TestCategory("ArchitectureSourceBoundary")]
    public sealed class StickyDockRestoreBoundaryTests
    {
        private const string Coordinator = "Features/StickyNotes/PetStickyWindowCoordinator.cs";

        [TestMethod]
        public void RestoreRequestsTransactionalVisibilityWithoutOrdinaryShow()
        {
            string source = ReadSource(Coordinator);
            string post = SliceMethod(source, "private bool PostDockGroupRestoreReproject(");
            Assert.IsTrue(post.Contains("showAfterPlacement: true"));
            string complete = SliceMethod(source, "private void CompleteHostedDockRestoreReproject(");
            Assert.IsTrue(complete.Contains("forceVisible: true"));
            Assert.IsFalse(complete.Contains("StickyUiCommand.Show("));
            Assert.IsFalse(source.Contains("ShowRestoredDockGroup"));
            Assert.IsTrue(complete.Contains("StickyUiCommand.FocusPrimaryInput("));
            string focus = SliceMethod(complete, "if (state.FocusEditor && state.Focus != null)");
            Assert.IsFalse(focus.Contains("FailHostedDockRestore("));
            Assert.IsFalse(complete.Contains("StickyUiCommand.SetBounds("));
        }

        [TestMethod]
        public void CurrentPlacementShowHasNoGeometryOrStateEffects()
        {
            string session = ReadSource("StickyWindowSession.cs");
            string show = SliceMethod(session, "internal bool TryShowCurrentPlacement()");
            Assert.IsTrue(show.Contains("_placementExecutor.Show();"));
            foreach (string forbidden in new[] { "ResolvePlacementPlan", "EnsureOnScreen",
                "MoveHiddenToSurface", "SetWindowPosExact", "GetDpiForWindow",
                "Data.Visible", "_sequence++", "Emit", "Focus", "Save(" })
                Assert.IsFalse(show.Contains(forbidden), forbidden);
            string commit = SliceMethod(session, "internal void CommitRestoredVisibleState()");
            Assert.IsTrue(commit.Contains("_window.Data.Visible = true"));
            Assert.IsFalse(commit.Contains("_sequence++"));
            Assert.IsFalse(commit.Contains("SetWindowPos"));
        }

        [TestMethod]
        public void HostShowsAllBeforeCommittingWorkingCopyAndPreservesRollback()
        {
            string host = SliceMethod(ReadSource("StickyUiHost.cs"),
                "private StickyUiCommandResult ApplyDockGroupReproject(");
            int batch = host.IndexOf("WindowsBatchWindowPlacementExecutor.Apply", StringComparison.Ordinal);
            int capture = host.IndexOf("session.CaptureDockMember(topology)", StringComparison.Ordinal);
            int show = host.IndexOf("session.TryShowCurrentPlacement()", StringComparison.Ordinal);
            int visible = host.IndexOf("session.CommitRestoredVisibleState()", StringComparison.Ordinal);
            int commit = host.IndexOf("placementApplied = true", StringComparison.Ordinal);
            Assert.IsTrue(batch >= 0 && capture > batch && show > capture && visible > show && commit > visible);
            Assert.AreEqual(batch, host.LastIndexOf("WindowsBatchWindowPlacementExecutor.Apply", StringComparison.Ordinal));
            Assert.IsTrue(host.Contains("transitions[index], placementApplied, command.Flag"));
            string finish = SliceMethod(ReadSource("StickyWindowSession.cs"),
                "internal void CompleteDockTargetDpi(");
            Assert.IsTrue(finish.Contains("if (!placementApplied)"));
            Assert.IsTrue(finish.Contains("RollbackReproject("));
            Assert.IsTrue(finish.Contains("else if (forceVisibleAfterPlacement)"));
        }

        [TestMethod]
        public void EnsureSessionAcknowledgementSynchronizesLeaseWithoutRemovingState()
        {
            string preparation = SliceMethod(ReadSource(Coordinator),
                "private bool PrepareHostedDockRestoreSessions(");
            Assert.IsTrue(preparation.Contains("SynchronizeSessionLease("));
            Assert.IsTrue(preparation.Contains("memberCopy.Id, result.Sequence"));
            Assert.IsFalse(preparation.Contains("_hostedRuntime.RemoveNote("));
            Assert.IsFalse(preparation.Contains("_hostedRuntime.AddNote("));
        }

        [TestMethod]
        public void DuplicateRestoreIsHandledAndTerminalPathsReleaseGate()
        {
            string source = ReadSource(Coordinator);
            string start = SliceMethod(source, "private bool TryRestoreHostedDockComponent(");
            string duplicate = SliceMethod(start, "if (!_pendingHostedDockRestoreGroups.Add(restoreKey))");
            Assert.IsTrue(duplicate.Contains("return true;"));
            Assert.IsTrue(duplicate.Contains("DockRestoreDuplicateIgnored"));
            Assert.IsTrue(start.Contains("catch"));
            Assert.IsTrue(start.Contains("_pendingHostedDockRestoreGroups.Remove(restoreKey)"));
            foreach (string name in new[] { "CompleteHostedDockRestoreSuccess", "FailHostedDockRestore" })
            {
                string terminal = SliceMethod(source, "private void " + name + "(");
                Assert.IsTrue(terminal.Contains("finally"), name);
                Assert.IsTrue(terminal.Contains("ReleaseHostedDockRestoreGate(state)"), name);
            }
        }
    }
}
