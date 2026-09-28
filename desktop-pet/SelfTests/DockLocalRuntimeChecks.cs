using System;
using System.Collections.Generic;
using System.Drawing;

namespace PennyPet
{
    internal static partial class SelfTest
    {
        // Test-side platform adapters call the surviving production rules.
        // They do not revive the deleted Pet live planner or transport.
        private static bool CanDockBelowForCheck(Rectangle moving,
            Rectangle target, int threshold)
        {
            return StickyDockOperations.CanDockBelow(moving.Left, moving.Top,
                moving.Width, moving.Height, target.Left, target.Top,
                target.Width, target.Height, threshold);
        }

        private static List<DockLayoutTarget> CalculateDividerTargetsForCheck(
            DockWindowFacts upper, DockWindowFacts lower)
        {
            var targets = new List<DockLayoutTarget>();
            if (upper == null || lower == null) return targets;
            int height = StickyDockController.CalculateDockDividerHeight(upper.Height);
            List<DockRect> followers = StickyDockGeometry.CalculateDockDividerFinalTargets(
                new[] { new DockRect(upper.X, upper.Y, upper.Width, upper.Height),
                    new DockRect(lower.X, lower.Y, lower.Width, lower.Height) },
                0, upper.Y, height);
            targets.Add(new DockLayoutTarget(upper.NoteId, upper.X, upper.Y,
                upper.Width, height, upper.Visible, upper.TopMost));
            DockRect follower = followers[0];
            targets.Add(new DockLayoutTarget(lower.NoteId, follower.Left,
                follower.Top, follower.Width, follower.Height,
                lower.Visible, lower.TopMost));
            return targets;
        }

        private static bool RunLocalDividerCycleCheck()
        {
            DisplaySurfaceSnapshot surface = FakeSurface(1, 0, true,
                1080, 1040, FakeTarget("mdp:divider"));
            var facts = new Dictionary<string, WindowFacts>();
            var members = new List<StickyDockSceneMember>();
            string[] ids = { "a", "b", "c", "d" };
            for (int index = 0; index < ids.Length; index++)
            {
                facts.Add(ids[index], new WindowFacts(ids[index], "mdp:divider",
                    surface.RuntimeGdiName,
                    new PhysicalRect(100, 100 + 300 * index, 420, 300), 96, 1, 1));
                members.Add(new StickyDockSceneMember(ids[index], "group", index, true));
            }
            IReadOnlyList<DockWindowTarget> applied = null;
            int applyCount = 0;
            var runtime = new StickyDockLocalGestureRuntime(
                id => facts.ContainsKey(id) ? facts[id] : null,
                (targets, source) =>
                {
                    if (source != "b") return false;
                    applied = targets;
                    applyCount++;
                    return true;
                });
            runtime.SetTopology(new DisplayTopologySnapshot(1, new[] { surface }));
            runtime.SetScene(new StickyDockSceneProjection(members, 1));
            if (runtime.TryBegin(StickyDockLocalGestureKind.DividerResize, "b") == 0)
                return false;
            int[] cycle = { 450, 250, 600, 300 };
            for (int repeat = 0; repeat < 50; repeat++)
                foreach (int requested in cycle)
                    if (!runtime.ResizeDivider(requested) || applied == null ||
                        applied.Count != 2 || applied[0].NoteId != "c" ||
                        applied[1].NoteId != "d" ||
                        applied[0].PhysicalBounds.Top != 400 + requested ||
                        applied[1].PhysicalBounds.Top != 700 + requested ||
                        applied[0].PhysicalBounds.Height != 300 ||
                        applied[1].PhysicalBounds.Height != 300 ||
                        applied[0].PhysicalBounds.Width != 420 ||
                        applied[1].PhysicalBounds.Width != 420)
                        return false;
            return applyCount == 200 && runtime.Complete() != null;
        }
    }
}
