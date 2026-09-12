using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyGeometryAuthorityTests
    {
        internal static DisplayTopologySnapshot Topology(long generation = 7,
            int left = -1920, int top = -1080)
        {
            return new DisplayTopologySnapshot(generation, new[] {
                new DisplaySurfaceSnapshot("surface", "DISPLAY1",
                    new PhysicalRect(left, top, 1920, 1080),
                    new PhysicalRect(left, top + 40, 1920, 1040), true, 0,
                    new[] { new DisplayTargetIdentity("mdp:one", true,
                        "one", "display", 0, 0, 0) }, 1.25) });
        }

        internal static WindowFacts Facts(string id = "note", int dpi = 192,
            int top = -960, int height = 480, long sequence = 10,
            long generation = 7)
        {
            return new WindowFacts(id, "mdp:one", "DISPLAY1",
                new PhysicalRect(-1840, top, 640, height), dpi,
                generation, sequence);
        }

        private static StickyNoteData ConflictingNote()
        {
            return new StickyNoteData { Id = "note", X = 9000, Y = 8000,
                Width = 900, Height = 700, DisplayId = "DISPLAY1",
                LocalLogicalX = 999, LocalLogicalY = 888,
                LocalLogicalWidth = 700, LocalLogicalHeight = 600,
                PreferredDisplayTargetKey = "mdp:one",
                PreferredLocalLogicalX = 40, PreferredLocalLogicalY = 60,
                PreferredLocalLogicalWidth = 320,
                PreferredLocalLogicalHeight = 240 };
        }

        [TestMethod]
        [DataRow(96, 1.0)]
        [DataRow(120, 1.25)]
        [DataRow(144, 1.5)]
        [DataRow(192, 2.0)]
        public void Restore_PrefersDurableIntentAndUsesActualWindowDpi(int dpi,
            double scale)
        {
            StickyNoteData note = ConflictingNote();
            DisplayTopologySnapshot topology = Topology();
            WindowPlacementPlan plan = StickyPlacementRecovery.SelectForShow(note, topology);
            PhysicalRect actualTarget = plan.Resolve(dpi);
            Assert.AreEqual(-1920 + (int)(40 * scale), actualTarget.Left);
            Assert.AreEqual(-1080 + (int)(60 * scale), actualTarget.Top);
            Assert.AreEqual((int)(320 * scale), actualTarget.Width);
            Assert.AreEqual((int)(240 * scale), actualTarget.Height);
            Assert.AreSame(topology, plan.Topology);
            Assert.AreEqual(9000, note.X, "Recovery selection cannot commit geometry.");
            Assert.AreEqual(999, note.LocalLogicalX);
        }

        [TestMethod]
        public void Restore_V10IsOnlyAnInputWhenPreferredIsAbsent()
        {
            StickyNoteData note = ConflictingNote();
            note.PreferredDisplayTargetKey = String.Empty;
            WindowPlacementPlan plan = StickyPlacementRecovery.SelectForShow(note, Topology());
            Assert.AreEqual(999, plan.Logical.X);
            Assert.AreEqual(700, plan.Logical.Width);
            Assert.AreEqual(String.Empty, note.PreferredDisplayTargetKey);
        }

        [TestMethod]
        public void Restore_MissingDisplayClampsPhysicalRecoveryWithoutChangingPreference()
        {
            StickyNoteData note = ConflictingNote();
            note.PreferredDisplayTargetKey = "mdp:missing";
            note.DisplayId = "missing";
            WindowPlacementPlan plan = StickyPlacementRecovery.SelectForShow(note, Topology());
            PhysicalRect target = plan.Resolve(192);
            Assert.IsNull(plan.Surface);
            Assert.AreEqual(-900, target.Left);
            Assert.AreEqual(-700, target.Top);
            Assert.AreEqual(900, target.Width);
            Assert.AreEqual("mdp:missing", note.PreferredDisplayTargetKey);
            Assert.AreEqual(40, note.PreferredLocalLogicalX);
            Assert.AreEqual(9000, note.X);
        }

        [TestMethod]
        public void Restore_GroupPlacementBelongsToItsBatch()
        {
            StickyNoteData note = ConflictingNote();
            note.DockGroupId = "group";
            Assert.IsNull(StickyPlacementRecovery.SelectForShow(note, Topology()));
            note.DockGroupId = String.Empty;
            Assert.IsNull(StickyPlacementRecovery.SelectForShow(note, null));
        }

        [TestMethod]
        public void CapturedFacts_UseCaptureOriginAndHwndDpi()
        {
            LogicalRect local;
            Assert.IsTrue(StickyPlacementRules.TryGetLogicalFacts(Facts(), Topology(), out local));
            Assert.AreEqual(40, local.X);
            Assert.AreEqual(60, local.Y);
            Assert.AreEqual(320, local.Width);
            Assert.AreEqual(240, local.Height);
            Assert.IsFalse(StickyPlacementRules.TryGetLogicalFacts(Facts(),
                Topology(8, 0, 0), out local));
            WindowPlacementPreference preferred;
            Assert.IsFalse(StickyPlacementRules.TryBuildPreferredPlacement(Facts(),
                Topology(8, 0, 0), "mdp:one", out preferred));
            Assert.IsNull(preferred);
        }

        [TestMethod]
        public void Runtime_RejectedFrameAndIntentChangesCannotReplaceCaptureContext()
        {
            StickyPlacementRuntime runtime = new StickyPlacementRuntime();
            WindowFacts accepted = Facts();
            Assert.IsTrue(runtime.TryUpdateEffective("note", accepted, Topology()));
            Assert.IsFalse(runtime.CanAcceptEffective("note", Facts(sequence: 9)));
            Assert.IsFalse(runtime.TryUpdateEffective("note", Facts(sequence: 9),
                Topology(7, 5000, 5000)));
            runtime.MarkTemporaryRehome("note", "removed");
            runtime.MarkUserPlacementCommit("note");
            LogicalRect local;
            Assert.AreSame(accepted, runtime.GetEffective("note"));
            Assert.IsTrue(runtime.TryGetEffectiveLogical("note", out local));
            Assert.AreEqual(40, local.X);
            Assert.AreEqual(60, local.Y);
            Assert.IsTrue(runtime.InvalidateEffective("note"));
            Assert.IsFalse(runtime.TryGetEffectiveLogical("note", out local));
            Assert.IsNull(runtime.GetEffective("note"));
            Assert.IsTrue(runtime.UserMovedSinceRehome("note"));
            Assert.IsTrue(runtime.TryUpdateEffective("note", Facts(sequence: 1), Topology()));
            Assert.IsTrue(runtime.TryGetEffectiveLogical("note", out local));
        }

        [TestMethod]
        public void LiveDock_AnchorsAtDraggedMemberAndPreservesEachHwndLogicalHeight()
        {
            WindowFacts root = Facts("root", dpi: 144, height: 450);
            WindowFacts source = Facts("source", top: -320, height: 480);
            WindowFacts tail = Facts("tail", dpi: 120, height: 500);
            DockGroupLogicalState state;
            Assert.IsTrue(StickyPlacementRules.TryBuildLiveDockState(
                new[] { root, source, tail }, source, Topology(), out state));
            Assert.AreEqual(40, state.RootAnchor.X);
            Assert.AreEqual(80, state.RootAnchor.Y);
            CollectionAssert.AreEqual(new[] { 300, 240, 400 }, new[] {
                state.Members[0].Height, state.Members[1].Height, state.Members[2].Height });
            foreach (DockLogicalMember member in state.Members)
                Assert.AreEqual(320, member.Width);
        }

        [TestMethod]
        public void LiveDock_RequiresACompleteCurrentUnambiguousFrame()
        {
            WindowFacts source = Facts("source");
            WindowFacts tail = Facts("tail");
            WindowFacts[][] invalid = {
                new[] { source, null }, new[] { tail }, new[] { source, source },
                new[] { source, Facts("tail", generation: 6) }, new WindowFacts[0] };
            foreach (WindowFacts[] frame in invalid)
            {
                DockGroupLogicalState state;
                Assert.IsFalse(StickyPlacementRules.TryBuildLiveDockState(
                    frame, source, Topology(), out state));
                Assert.IsNull(state);
            }
        }

        [TestMethod]
        public void DividerAndExit_CarryFactsIndependentlyOfContentOrLiveResizeIntent()
        {
            StickyNoteData note = ConflictingNote();
            StickyNoteUiSnapshot content = StickyNoteUiSnapshot.FromContentData(note);
            WindowFacts facts = Facts(height: 666);
            DisplayTopologySnapshot topology = Topology();
            StickyUiEvent completed = StickyUiEvent.DividerResize(
                StickyUiEventKind.DockDividerResizeCompleted, content, 10, 700,
                facts, topology);
            StickyUiFinalSnapshot exit = new StickyUiFinalSnapshot(content, 10, facts, topology);
            Assert.AreEqual(700, completed.Height, "Live WM_SIZING intent stays explicit.");
            Assert.AreEqual(666, completed.Facts.PhysicalBounds.Height);
            Assert.AreSame(facts, exit.Facts);
            Assert.AreSame(topology, exit.Topology);
            Assert.AreEqual(0, content.Height, "UI content cannot present a second actual height.");
            Assert.AreEqual(10L, exit.Facts.WindowSequence);
            content.ApplyContentTo(note);
            Assert.AreEqual(700, note.Height);
            Assert.AreEqual(240, note.PreferredLocalLogicalHeight);
        }
    }
}
