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
        public void LegacyGeometryUsesTheSameRestoreTransactionWithoutPerWindowCallbacks()
        {
            string source = ReadSource(Coordinator);
            Assert.IsFalse(source.Contains("TryRestoreHostedDockComponentLegacyFallback"));
            string post = SliceMethod(source, "private bool TryRestoreHostedDockComponent(");
            foreach (string forbidden in new[] { "member.Visible =", "_hostedRuntime.AddNote(",
                "Screen.FromRectangle", "StickyUiCommand.Create(", "StickyUiCommand.Show(", "_notes.Save()" })
                Assert.IsFalse(post.Contains(forbidden), forbidden);
            string host = SliceMethod(ReadSource("StickyUiHost.cs"), "private StickyUiCommandResult ApplyDockGroupReproject(");
            Assert.IsTrue(host.Contains("request.MemberIds"));
            Assert.IsFalse(host.Contains("RecoveryTargets") || host.Contains("LegacyRecovery"),
                "The native effect pipeline cannot branch on storage format.");
        }

        [TestMethod]
        public void RestoreRequestsTransactionalVisibilityWithoutOrdinaryShow()
        {
            string source = ReadSource(Coordinator);
            string post = SliceMethod(source, "private bool TryRestoreHostedDockComponent(");
            Assert.IsTrue(post.Contains("StickyUiCommand.RestoreDockGroup("));
            Assert.IsFalse(post.Contains("StickyUiCommand.EnsureSession("));
            string complete = SliceMethod(source, "private void CompleteHostedDockRestore(");
            Assert.IsTrue(complete.Contains("forceVisible: true"));
            Assert.IsFalse(complete.Contains("StickyUiCommand.Show("));
            Assert.IsFalse(source.Contains("ShowRestoredDockGroup"));
            Assert.IsTrue(complete.Contains("StickyUiCommand.FocusPrimaryInput("));
            string focus = SliceMethod(complete, "if (operation.FocusEditor)");
            Assert.IsFalse(focus.Contains("CancelHostedDockRestore("));
            Assert.IsTrue(complete.IndexOf("_dockRestores.Finish(operation)", StringComparison.Ordinal) <
                complete.IndexOf("StickyUiCommand.FocusPrimaryInput(", StringComparison.Ordinal));
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
        public void RestoreBatchRegistersSessionsOnlyAfterWholeBatchPreflight()
        {
            string apply = SliceMethod(ReadSource(Coordinator), "private bool TryApplyDockTopologyResult(");
            int preflight = apply.IndexOf("_factsReceiver.TryPrepare(member, snapshot, out update, acceptCreatedSessions)", StringComparison.Ordinal);
            int commit = apply.IndexOf("update.Commit(forceVisible)", StringComparison.Ordinal);
            Assert.IsTrue(preflight >= 0 && commit > preflight);
            Assert.IsTrue(apply.Contains("!remaining.Remove(member.NoteId)"));
            string receiver = ReadSource("Features/StickyNotes/StickyFactsReceiver.cs");
            string accept = SliceMethod(receiver, "internal void CommitGeometry()");
            Assert.IsTrue(accept.IndexOf("AcceptEffective", StringComparison.Ordinal) <
                accept.IndexOf("AcceptBatchSequence", StringComparison.Ordinal));
            Assert.IsFalse(apply.Contains("_hostedRuntime.RemoveNote("));
            string host = SliceMethod(ReadSource("StickyUiHost.cs"), "private StickyUiCommandResult RestoreDockGroup(");
            Assert.IsTrue(host.Contains("if (ensured.SessionCreated) created.Add("));
            Assert.IsTrue(host.Contains("created.ContainsKey(member.NoteId)"));
            Assert.IsTrue(host.Contains("if (!completed)") && host.Contains("in created)"));
            string completion = SliceMethod(ReadSource(Coordinator), "private void CompleteHostedDockRestore(");
            Assert.IsFalse(completion.Contains("StickyUiCommand.Close("));
        }

        [TestMethod]
        public void TopologyAndUserMutationsRetireRestoreBeforeReplacementEffects()
        {
            string source = ReadSource(Coordinator);
            string restart = SliceMethod(source, "private void RestartHostedDockRestores(");
            int cancel = restart.IndexOf("CancelHostedDockRestore(operation)", StringComparison.Ordinal);
            int start = restart.IndexOf("TryRestoreHostedDockComponent(", StringComparison.Ordinal);
            Assert.IsTrue(cancel >= 0 && start > cancel);
            Assert.IsTrue(SliceMethod(source, "private void ReconcileDockGroups(").Contains("_dockRestores.ContainsGroup("));
            foreach (string signature in new[] { "private bool BeginHostedStickyExitIfNeeded()",
                "private void CollapseAllStickyNotes()", "private void ExpandAndTileAllStickyNotesToPetScreen()",
                "private void CloseHostedStickyRuntimeForReload(" })
                Assert.IsTrue(SliceMethod(source, signature).Contains("CancelHostedDockRestores()"), signature);
            string complete = SliceMethod(source, "private void CompleteHostedDockRestore(");
            int owner = complete.IndexOf("_dockRestores.IsCurrent(operation)", StringComparison.Ordinal);
            int membership = complete.IndexOf("operation.MatchesMembers(", StringComparison.Ordinal);
            int apply = complete.IndexOf("TryApplyDockTopologyResult(", StringComparison.Ordinal);
            Assert.IsTrue(owner >= 0 && membership > owner && apply > membership);
            string host = SliceMethod(ReadSource("StickyUiHost.cs"), "private StickyUiCommandResult ApplyDockGroupReproject(");
            int cancelled = host.LastIndexOf("command.DockRestore.Cancellation.IsCancellationRequested", StringComparison.Ordinal);
            Assert.IsTrue(cancelled >= 0 && cancelled < host.IndexOf("session.CommitRestoredVisibleState()", StringComparison.Ordinal));
        }

        [TestMethod]
        public void DeleteWaitsForNativeCloseEvenBeforeRestoreLeaseRegistration()
        {
            string dock = ReadSource("Features/StickyNotes/PetStickyDockCoordinator.cs");
            string delete = SliceMethod(dock, "private void DeleteStickyNote(StickyNoteData note,");
            Assert.IsTrue(delete.Contains("CancelHostedDockRestores(note.Id)"));
            Assert.IsTrue(delete.Contains("BeginHostedStickyDelete(note, completed)"));
            Assert.IsFalse(delete.Contains("IsHostedSticky(note)") || delete.Contains("DeleteStickyNoteAfterWindowClosed("));
            string close = SliceMethod(ReadSource("StickyWindowSession.cs"), "internal StickyUiCommandResult Close()");
            int ime = close.IndexOf("IsImeCompositionActive", StringComparison.Ordinal);
            Assert.IsTrue(ime >= 0 && close.IndexOf("StickyUiCommandResult.NotAccepted()", StringComparison.Ordinal) > ime);
            string host = ReadSource("StickyUiHost.cs");
            string handler = host.Substring(host.IndexOf("case StickyUiCommandKind.Close:", StringComparison.Ordinal));
            handler = handler.Substring(0, handler.IndexOf("case StickyUiCommandKind.CloseAll:", StringComparison.Ordinal));
            Assert.IsTrue(handler.Contains(": StickyUiCommandResult.Handled()"));
            string thread = SliceMethod(ReadSource("StickyUiThreadHost.cs"), "internal void Post(StickyUiCommand command,");
            Assert.IsTrue(thread.Contains("_thread != null && !_thread.IsAlive"));
        }
    }
}
