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
                apply);
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
