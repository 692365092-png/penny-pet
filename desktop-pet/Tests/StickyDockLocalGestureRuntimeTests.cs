using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyDockLocalGestureRuntimeTests
    {
        [TestMethod]
        public void HeaderDrag_ProjectsFollowersFromLogicalBaseline()
        {
            DisplaySurfaceSnapshot left = Surface(
                "left", "\\\\.\\DISPLAY1", "mdp:left",
                new PhysicalRect(0, 0, 1920, 1080), true);
            DisplaySurfaceSnapshot right = Surface(
                "right", "\\\\.\\DISPLAY2", "mdp:right",
                new PhysicalRect(1920, 0, 2560, 1440), false);
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(9,
                    new[] { left, right });

            Dictionary<string, WindowFacts> facts =
                new Dictionary<string, WindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = Facts("A", left, 96,
                        new PhysicalRect(100, 100, 320, 300), 9),
                    ["B"] = Facts("B", left, 96,
                        new PhysicalRect(100, 400, 320, 400), 9),
                    ["C"] = Facts("C", left, 96,
                        new PhysicalRect(100, 800, 320, 260), 9)
                };
            List<DockWindowTarget> applied = null;
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) =>
                {
                    applied = new List<DockWindowTarget>(targets);
                    Assert.AreEqual("B", source);
                });
            runtime.SetTopology(topology);
            runtime.SetScene(Scene(3, "A", "B", "C"));

            Assert.AreNotEqual(0,
                runtime.TryBegin(
                    StickyDockLocalGestureKind.HeaderDrag, "B"));

            WindowFacts moved = Facts("B", right, 192,
                new PhysicalRect(2320, 500, 640, 800), 9);
            Assert.IsTrue(runtime.MoveHeader(moved));
            Assert.IsNotNull(applied);
            Assert.AreEqual(2, applied.Count);
            Assert.AreEqual("A", applied[0].NoteId);
            Assert.AreEqual("C", applied[1].NoteId);

            // Source local=(200,250) on DISPLAY2. A precedes B by 300
            // logical px, so root local Y=-50. Projection is rebuilt from
            // that logical baseline at 200%, never from the previous frame.
            AssertRect(applied[0].PhysicalBounds,
                2320, -100, 640, 600);
            AssertRect(applied[1].PhysicalBounds,
                2320, 1300, 640, 520);
        }

        [TestMethod]
        public void HorizontalResize_UsesNativeProposedRectAndStartBounds()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(4, new[] { surface });
            Dictionary<string, WindowFacts> facts = GroupFacts(
                surface, 4);
            List<DockWindowTarget> applied = null;
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) =>
                    applied = new List<DockWindowTarget>(targets));
            runtime.SetTopology(topology);
            runtime.SetScene(Scene(8, "A", "B", "C"));

            Assert.AreNotEqual(0,
                runtime.TryBegin(
                    StickyDockLocalGestureKind.HorizontalResize, "B"));
            Assert.IsTrue(runtime.ResizeHorizontal(40, 500));
            Assert.AreEqual(2, applied.Count);
            AssertRect(applied[0].PhysicalBounds,
                40, 100, 500, 300);
            AssertRect(applied[1].PhysicalBounds,
                40, 800, 500, 260);
        }

        [TestMethod]
        public void DividerResize_UsesProposedHeightWithoutSizeChangedChase()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(5, new[] { surface });
            Dictionary<string, WindowFacts> facts = GroupFacts(
                surface, 5);
            List<DockWindowTarget> applied = null;
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) =>
                    applied = new List<DockWindowTarget>(targets));
            runtime.SetTopology(topology);
            runtime.SetScene(Scene(9, "A", "B", "C"));

            Assert.AreNotEqual(0,
                runtime.TryBegin(
                    StickyDockLocalGestureKind.DividerResize, "A"));
            Assert.IsTrue(runtime.ResizeDivider(420));
            Assert.AreEqual(2, applied.Count);
            AssertRect(applied[0].PhysicalBounds,
                100, 520, 320, 400);
            AssertRect(applied[1].PhysicalBounds,
                100, 920, 320, 260);
        }

        [TestMethod]
        public void HeaderDrag_DetectsSnapCandidateLocallyAtLegacyThreshold()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(7, new[] { surface });
            Dictionary<string, WindowFacts> facts = GroupFacts(
                surface, 7);
            // Legacy snap rule attaches the moving TOP to the
            // candidate BOTTOM (within 20 physical px).
            facts["X"] = Facts("X", surface, 96,
                new PhysicalRect(100, 480, 320, 300), 7);
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) => { });
            runtime.SetTopology(topology);
            runtime.SetScene(new StickyDockSceneProjection(
                new[]
                {
                    new StickyDockSceneMember("A", "group", 0, true),
                    new StickyDockSceneMember("B", "group", 1, true),
                    new StickyDockSceneMember("C", "group", 2, true),
                    new StickyDockSceneMember("X", "other", 0, true)
                }, 12));

            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));
            Assert.IsTrue(runtime.MoveHeader(Facts(
                "A", surface, 96,
                new PhysicalRect(100, 780, 320, 300), 7)));
            Assert.AreEqual("X", runtime.LastSnapTargetNoteId);
            Assert.AreEqual(String.Empty,
                runtime.SplitGuideParentNoteId);

            runtime.Cancel();
            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "B"));
            Assert.AreEqual("A", runtime.SplitGuideParentNoteId);
        }

        [TestMethod]
        public void HeaderDrag_StandaloneCanBecomeMergeCommit()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            Dictionary<string, WindowFacts> facts =
                new Dictionary<string, WindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = Facts("A", surface, 96,
                        new PhysicalRect(100, 500, 320, 300), 13),
                    ["X"] = Facts("X", surface, 96,
                        new PhysicalRect(100, 200, 320, 300), 13)
                };
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) => { });
            runtime.SetTopology(new DisplayTopologySnapshot(
                13, new[] { surface }));
            runtime.SetScene(new StickyDockSceneProjection(
                new[]
                {
                    new StickyDockSceneMember(
                        "A", String.Empty, -1, true, 101),
                    new StickyDockSceneMember(
                        "X", "target", 0, true, 202)
                }, 14));

            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));
            Assert.IsTrue(runtime.MoveHeader(facts["A"]));
            StickyDockLocalGestureCompletion completion =
                runtime.Complete();

            Assert.AreEqual(
                StickyDockCommitIntent.MergeAfter,
                completion.Intent);
            Assert.AreEqual("X", completion.TargetNoteId);
            Assert.AreEqual(2,
                completion.BaselineVersions.Count);
            Assert.AreEqual(101,
                completion.BaselineVersions["A"]);
            Assert.AreEqual(202,
                completion.BaselineVersions["X"]);

            runtime.ApplyProvisional(completion);
            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"),
                "A second gesture may begin before the first merge ACK.");
            Assert.AreEqual("X",
                runtime.SplitGuideParentNoteId,
                "The second gesture must see the provisional merge relation.");
            runtime.Cancel();
        }

        [TestMethod]
        public void DockCommitVersion_IgnoresContentButTracksGeometry()
        {
            StickyNoteData note = new StickyNoteData
            {
                Id = "A",
                DockGroupId = "g",
                DockGroupOrder = 1,
                X = 10,
                Y = 20,
                Width = 300,
                Height = 400,
                Text = "before"
            };
            long baseline =
                StickyDockCommitVersion.Compute(
                    note);
            note.Text = "after";
            note.ModifiedUtcTicks++;
            Assert.AreEqual(baseline,
                StickyDockCommitVersion.Compute(
                    note));

            note.X++;
            Assert.AreNotEqual(baseline,
                StickyDockCommitVersion.Compute(
                    note));
        }

        [TestMethod]
        public void StructuralChange_CancelsOnlyAffectedGestureAndRestoresBaseline()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            Dictionary<string, WindowFacts> facts =
                new Dictionary<string, WindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = Facts("A", surface, 96,
                        new PhysicalRect(100, 100, 320, 300), 31),
                    ["B"] = Facts("B", surface, 96,
                        new PhysicalRect(100, 400, 320, 300), 31)
                };
            List<DockWindowTarget> applied =
                new List<DockWindowTarget>();
            StickyDockLocalGestureRuntime runtime =
                Runtime(facts, (targets, source) =>
                {
                    applied.Clear();
                    applied.AddRange(targets);
                });
            runtime.SetTopology(new DisplayTopologySnapshot(
                31, new[] { surface }));
            runtime.SetScene(new StickyDockSceneProjection(
                new[]
                {
                    new StickyDockSceneMember("A", "g", 0, true, 1),
                    new StickyDockSceneMember("B", "g", 1, true, 2)
                }, 8));

            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));
            Assert.IsFalse(runtime.AffectsStructure(
                new[] { "other" }));
            Assert.IsTrue(runtime.AffectsStructure(
                new[] { "B" }));

            IReadOnlyList<DockWindowTarget> restore =
                runtime.CancelAndRestore();
            Assert.IsFalse(runtime.IsActive);
            Assert.AreEqual(2, restore.Count);
            Assert.AreEqual(new PhysicalRect(
                100, 100, 320, 300),
                restore[0].PhysicalBounds);
            Assert.AreEqual(new PhysicalRect(
                100, 400, 320, 300),
                restore[1].PhysicalBounds);
        }

        [TestMethod]
        public void TopologyChange_RebasesFromNewGenerationFacts()
        {
            DisplaySurfaceSnapshot first = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            DisplaySurfaceSnapshot second = Surface(
                "two", "\\\\.\\DISPLAY2", "mdp:two",
                new PhysicalRect(1920, 0, 1920, 1080), false);
            Dictionary<string, WindowFacts> facts =
                new Dictionary<string, WindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = Facts("A", first, 96,
                        new PhysicalRect(100, 100, 320, 300), 41),
                    ["B"] = Facts("B", first, 96,
                        new PhysicalRect(100, 400, 320, 300), 41)
                };
            StickyDockLocalGestureRuntime runtime =
                Runtime(facts, (targets, source) => { });
            runtime.SetTopology(new DisplayTopologySnapshot(
                41, new[] { first, second }));
            runtime.SetScene(new StickyDockSceneProjection(
                new[]
                {
                    new StickyDockSceneMember("A", "g", 0, true, 1),
                    new StickyDockSceneMember("B", "g", 1, true, 2)
                }, 9));
            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));

            facts["A"] = Facts("A", second, 120,
                new PhysicalRect(2100, 120, 400, 375), 42);
            facts["B"] = Facts("B", second, 120,
                new PhysicalRect(2100, 495, 400, 375), 42);
            Assert.IsTrue(runtime.TryRebaseTopology(
                new DisplayTopologySnapshot(
                    42, new[] { first, second })));
            Assert.IsTrue(runtime.MoveHeader(facts["A"]));
            StickyDockLocalGestureCompletion completion =
                runtime.Complete();
            Assert.AreEqual(42,
                completion.TopologyGeneration);
        }

        [TestMethod]
        public void TopologyChange_MissingSourceFailsClosed()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            Dictionary<string, WindowFacts> facts =
                new Dictionary<string, WindowFacts>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = Facts("A", surface, 96,
                        new PhysicalRect(100, 100, 320, 300), 51),
                    ["B"] = Facts("B", surface, 96,
                        new PhysicalRect(100, 400, 320, 300), 51)
                };
            StickyDockLocalGestureRuntime runtime =
                Runtime(facts, (targets, source) => { });
            runtime.SetTopology(new DisplayTopologySnapshot(
                51, new[] { surface }));
            runtime.SetScene(new StickyDockSceneProjection(
                new[]
                {
                    new StickyDockSceneMember("A", "g", 0, true, 1),
                    new StickyDockSceneMember("B", "g", 1, true, 2)
                }, 10));
            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));
            facts.Remove("A");

            Assert.IsFalse(runtime.TryRebaseTopology(
                new DisplayTopologySnapshot(
                    52, new[] { surface })));
            runtime.Cancel();
            Assert.IsFalse(runtime.IsActive);
        }

        [TestMethod]
        public void NativeFollowerFailure_IsReportedToGestureOwner()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            Dictionary<string, WindowFacts> facts =
                GroupFacts(surface, 61);
            StickyDockLocalGestureRuntime runtime =
                new StickyDockLocalGestureRuntime(
                    id => facts.ContainsKey(id)
                        ? facts[id] : null,
                    (targets, source) => false);
            runtime.SetTopology(new DisplayTopologySnapshot(
                61, new[] { surface }));
            runtime.SetScene(Scene(13, "A", "B", "C"));

            Assert.AreNotEqual(0, runtime.TryBegin(
                StickyDockLocalGestureKind.HeaderDrag, "A"));
            Assert.IsFalse(runtime.MoveHeader(facts["A"]));
            Assert.IsTrue(runtime.IsActive,
                "The host owns the rollback timing after a native failure.");
            Assert.AreEqual(3,
                runtime.CancelAndRestore().Count);
            Assert.IsFalse(runtime.IsActive);
        }

        [TestMethod]
        public void Runtime_HasNoPetCallbackRepositoryOrSaveChannel()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "one", "\\\\.\\DISPLAY1", "mdp:one",
                new PhysicalRect(0, 0, 1920, 1080));
            Dictionary<string, WindowFacts> facts = GroupFacts(
                surface, 6);
            int applies = 0;
            StickyDockLocalGestureRuntime runtime = Runtime(
                facts, (targets, source) => applies++);
            runtime.SetTopology(new DisplayTopologySnapshot(
                6, new[] { surface }));
            runtime.SetScene(Scene(11, "A", "B", "C"));

            long gesture = runtime.TryBegin(
                StickyDockLocalGestureKind.HorizontalResize, "A");
            Assert.AreNotEqual(0, gesture);
            for (int index = 0; index < 100; index++)
                Assert.IsTrue(runtime.ResizeHorizontal(
                    100 + index, 320 + index));

            StickyDockLocalGestureCompletion completion =
                runtime.Complete();
            Assert.AreEqual(100, applies);
            Assert.AreEqual(gesture, completion.GestureId);
            Assert.AreEqual(11, completion.SceneRevision);
            Assert.AreEqual(6, completion.TopologyGeneration);
            Assert.IsFalse(runtime.IsActive);
        }

        private static StickyDockLocalGestureRuntime Runtime(
            IDictionary<string, WindowFacts> facts,
            Action<IReadOnlyList<DockWindowTarget>, string> apply)
        {
            return new StickyDockLocalGestureRuntime(
                id => facts.ContainsKey(id) ? facts[id] : null,
                (targets, source) =>
                {
                    apply(targets, source);
                    return true;
                });
        }

        private static StickyDockSceneProjection Scene(
            long revision, params string[] ids)
        {
            List<StickyDockSceneMember> members =
                new List<StickyDockSceneMember>();
            for (int index = 0; index < ids.Length; index++)
                members.Add(new StickyDockSceneMember(
                    ids[index], "group", index, true));
            return new StickyDockSceneProjection(members, revision);
        }

        private static Dictionary<string, WindowFacts> GroupFacts(
            DisplaySurfaceSnapshot surface, long generation)
        {
            return new Dictionary<string, WindowFacts>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = Facts("A", surface, 96,
                    new PhysicalRect(100, 100, 320, 300),
                    generation),
                ["B"] = Facts("B", surface, 96,
                    new PhysicalRect(100, 400, 320, 400),
                    generation),
                ["C"] = Facts("C", surface, 96,
                    new PhysicalRect(100, 800, 320, 260),
                    generation)
            };
        }

        private static WindowFacts Facts(string id,
            DisplaySurfaceSnapshot surface, int dpi,
            PhysicalRect rect, long generation)
        {
            return new WindowFacts(id,
                surface.Targets[0].StableKey,
                surface.RuntimeGdiName, rect, dpi,
                generation, 1);
        }

        private static DisplaySurfaceSnapshot Surface(
            string id, string gdi, string targetKey,
            PhysicalRect bounds, bool primary = true)
        {
            return new DisplaySurfaceSnapshot(id, gdi,
                bounds, bounds, primary, 0,
                new[]
                {
                    new DisplayTargetIdentity(targetKey, true,
                        targetKey, "Display", 0, 0, 0)
                });
        }

        private static void AssertRect(PhysicalRect actual,
            int left, int top, int width, int height)
        {
            Assert.AreEqual(left, actual.Left);
            Assert.AreEqual(top, actual.Top);
            Assert.AreEqual(width, actual.Width);
            Assert.AreEqual(height, actual.Height);
        }
    }
}
