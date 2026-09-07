using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DynamicNDisplayPropertyTests
    {
        private const int SeedCount = 1000;

        [TestMethod]
        public void DynamicNDisplay_1000Seeds_PreserveCoreInvariants()
        {
            for (int seed = 0; seed < SeedCount; seed++)
                VerifySeed(seed);
        }

        private static void VerifySeed(int seed)
        {
            long generation = seed + 10L;
            SyntheticDisplayScenario scenario =
                DynamicNDisplayScenarioFactory.Create(seed, generation);
            try
            {
                VerifyTopologyLookups(seed, scenario);
                VerifyRoundTrip(seed, scenario);
                VerifyPreferredFallback(seed, scenario);
                VerifyUserMovePreference(seed, scenario);
                VerifyDockPlan(seed, scenario);
            }
            catch (Exception error)
            {
                Assert.Fail("Synthetic seed " + seed +
                    " failed: " + error);
            }
        }

        private static void VerifyTopologyLookups(
            int seed, SyntheticDisplayScenario scenario)
        {
            DisplayTopologySnapshot topology = scenario.Topology;

            Assert.IsTrue(topology.Surfaces.Count >= 1 &&
                topology.Surfaces.Count <= 16, "seed=" + seed);

            DisplaySurfaceSnapshot primary = topology.PrimaryOrFirst();
            Assert.IsNotNull(primary, "seed=" + seed);

            HashSet<string> ids = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
            {
                Assert.IsTrue(ids.Add(surface.RuntimeSurfaceId),
                    "duplicate surface seed=" + seed);
                Assert.AreSame(surface,
                    topology.FindByRuntimeSurfaceId(
                        surface.RuntimeSurfaceId),
                    "surface lookup seed=" + seed);
                Assert.AreSame(surface,
                    topology.FindByRuntimeGdiName(surface.RuntimeGdiName),
                    "gdi lookup seed=" + seed);
                foreach (DisplayTargetIdentity target in surface.Targets)
                    Assert.AreSame(surface,
                        topology.FindByTargetKey(target.StableKey),
                        "target lookup seed=" + seed);
            }

            DisplayTopologySnapshot reordered =
                DynamicNDisplayScenarioFactory.ReorderOnly(scenario,
                    seed ^ unchecked((int)0x5A5A5A5A),
                    topology.Generation + 1);
            foreach (DisplaySurfaceSnapshot surface in topology.Surfaces)
                Assert.AreEqual(surface.RuntimeSurfaceId,
                    reordered.FindByRuntimeGdiName(
                        surface.RuntimeGdiName).RuntimeSurfaceId,
                    "order-dependent lookup seed=" + seed);
        }

        private static void VerifyRoundTrip(
            int seed, SyntheticDisplayScenario scenario)
        {
            Random random = new Random(
                seed ^ unchecked((int)0x71B3D42F));

            foreach (DisplaySurfaceSnapshot surface
                in scenario.Topology.Surfaces)
            {
                int dpi = scenario.DpiFor(surface);
                double scale = dpi / 96.0;

                for (int sample = 0; sample < 4; sample++)
                {
                    LogicalRect local =
                        DynamicNDisplayScenarioFactory
                            .RandomLocalLogicalRect(random);
                    PhysicalRect physical =
                        DisplayGeometry.ProjectLocalRect(local,
                            surface.Bounds.Left, surface.Bounds.Top,
                            scale);

                    string preferredKey =
                        DisplayTopologyRules.SelectPreferredTargetKey(
                            surface, null);
                    Assert.IsFalse(
                        String.IsNullOrWhiteSpace(preferredKey),
                        "seed=" + seed);

                    WindowPlacementPreference roundTripped =
                        StickyPlacementMath.PreferenceFromPhysicalRect(
                            preferredKey, surface.Bounds.Left,
                            surface.Bounds.Top, scale, physical);
                    LogicalRect recovered =
                        roundTripped.LocalLogicalRect;
                    AssertLogicalNear(seed, local, recovered, 1);

                    PhysicalRect projectedAgain =
                        DisplayGeometry.ProjectLocalRect(recovered,
                            surface.Bounds.Left, surface.Bounds.Top,
                            scale);
                    AssertPhysicalNear(seed, physical, projectedAgain, 2);
                }
            }
        }

        private static void AssertLogicalNear(
            int seed, LogicalRect expected, LogicalRect actual,
            int tolerance)
        {
            Assert.IsTrue(
                Math.Abs((long)actual.X - expected.X) <= tolerance &&
                Math.Abs((long)actual.Y - expected.Y) <= tolerance &&
                Math.Abs((long)actual.Width - expected.Width) <= tolerance &&
                Math.Abs((long)actual.Height - expected.Height) <= tolerance,
                "logical drift seed=" + seed);
        }

        private static void AssertPhysicalNear(
            int seed, PhysicalRect expected, PhysicalRect actual,
            int tolerance)
        {
            Assert.IsTrue(
                Math.Abs((long)actual.Left - expected.Left) <= tolerance &&
                Math.Abs((long)actual.Top - expected.Top) <= tolerance &&
                Math.Abs((long)actual.Width - expected.Width) <= tolerance &&
                Math.Abs((long)actual.Height - expected.Height) <= tolerance,
                "physical drift seed=" + seed);
        }

        private static void VerifyPreferredFallback(
            int seed, SyntheticDisplayScenario scenario)
        {
            if (scenario.Topology.Surfaces.Count < 2) return;

            DisplaySurfaceSnapshot home = scenario.Topology.Surfaces[0];
            string homeKey =
                DisplayTopologyRules.SelectPreferredTargetKey(home, null);
            Assert.IsFalse(String.IsNullOrWhiteSpace(homeKey));

            WindowPlacementPreference preferred =
                new WindowPlacementPreference(homeKey,
                    new LogicalRect
                    {
                        X = 40,
                        Y = 50,
                        Width = 320,
                        Height = 300
                    });

            DisplayTopologySnapshot after =
                DynamicNDisplayScenarioFactory.RemoveSurface(scenario,
                    home, scenario.Topology.Generation + 1);
            DisplaySurfaceSnapshot fallback =
                FallbackDisplayPolicy.ResolveFallbackSurface(after,
                    preferred.PreferredTargetKey, home.WorkArea,
                    String.Empty);

            Assert.IsNotNull(fallback, "fallback missing seed=" + seed);
            Assert.AreEqual(homeKey, preferred.PreferredTargetKey,
                "temporary fallback overwrote preference seed=" + seed);
            Assert.AreEqual(40, preferred.LocalLogicalRect.X);
            Assert.AreEqual(50, preferred.LocalLogicalRect.Y);
        }

        private static void VerifyUserMovePreference(
            int seed, SyntheticDisplayScenario scenario)
        {
            Random random = new Random(
                seed ^ unchecked((int)0x1367A95D));
            int index = random.Next(scenario.Topology.Surfaces.Count);
            DisplaySurfaceSnapshot surface =
                scenario.Topology.Surfaces[index];
            int dpi = scenario.DpiFor(surface);

            int offsetX = random.Next(0, Math.Max(1,
                Math.Min(400, surface.WorkArea.Width)));
            int offsetY = random.Next(0, Math.Max(1,
                Math.Min(300, surface.WorkArea.Height)));

            PhysicalRect actualRect = new PhysicalRect(
                surface.WorkArea.Left + offsetX,
                surface.WorkArea.Top + offsetY,
                Math.Max(1, (int)Math.Round(320 * dpi / 96.0,
                    MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(300 * dpi / 96.0,
                    MidpointRounding.AwayFromZero)));

            string key =
                DisplayTopologyRules.SelectPreferredTargetKey(
                    surface, null);
            WindowPlacementPreference moved =
                StickyPlacementMath.PreferenceFromPhysicalRect(key,
                    surface.Bounds.Left, surface.Bounds.Top,
                    dpi / 96.0, actualRect);

            Assert.AreEqual(key, moved.PreferredTargetKey,
                "user move target seed=" + seed);
            Assert.IsTrue(moved.IsValid,
                "invalid moved preference seed=" + seed);

            PhysicalRect projected = DisplayGeometry.ProjectLocalRect(
                moved.LocalLogicalRect, surface.Bounds.Left,
                surface.Bounds.Top, dpi / 96.0);
            AssertPhysicalNear(seed, actualRect, projected, 2);
        }

        private static void VerifyDockPlan(
            int seed, SyntheticDisplayScenario scenario)
        {
            Random random = new Random(
                seed ^ unchecked((int)0x4C12B8E3));
            DisplaySurfaceSnapshot surface =
                scenario.Topology.Surfaces[random.Next(
                    scenario.Topology.Surfaces.Count)];
            int dpi = scenario.DpiFor(surface);

            int memberCount = random.Next(1, 9);
            List<DockLogicalMember> members =
                new List<DockLogicalMember>(memberCount);
            for (int index = 0; index < memberCount; index++)
                members.Add(new DockLogicalMember(
                    "seed-" + seed + "-member-" + index,
                    random.Next(220, 481), random.Next(160, 521)));

            DockGroupLogicalState group = new DockGroupLogicalState(
                new LogicalPoint
                {
                    X = random.Next(-800, 1201),
                    Y = random.Next(-800, 1201)
                },
                members);

            int sourceIndex = random.Next(memberCount);
            DockLogicalMember source = members[sourceIndex];
            string key =
                DisplayTopologyRules.SelectPreferredTargetKey(
                    surface, null);
            WindowFacts sourceFacts = new WindowFacts(source.NoteId, key,
                surface.RuntimeGdiName, surface.WorkArea, dpi,
                scenario.Topology.Generation, seed + 1L);
            long epoch = seed + 100L;

            DockPlacementPlan plan = DockPlacementPlanner.Plan(group,
                sourceFacts, surface, dpi,
                scenario.Topology.Generation, seed + 500L, epoch);

            Assert.AreEqual(surface.RuntimeSurfaceId,
                plan.TargetSurfaceId, "Dock target seed=" + seed);
            Assert.AreEqual(dpi, plan.TargetDpi, "Dock dpi seed=" + seed);
            Assert.AreEqual(members.Count, plan.WindowTargets.Count,
                "Dock count seed=" + seed);

            for (int index = 0; index < members.Count; index++)
            {
                Assert.AreEqual(members[index].NoteId,
                    plan.WindowTargets[index].NoteId,
                    "Dock order seed=" + seed);
                Assert.IsTrue(plan.WindowTargets[index]
                    .PhysicalBounds.IsValid, "Dock rect seed=" + seed);
                if (index > 0)
                    Assert.AreEqual(
                        plan.WindowTargets[index - 1]
                            .PhysicalBounds.Bottom,
                        plan.WindowTargets[index].PhysicalBounds.Top,
                        "Dock gap/overlap seed=" + seed);
            }

            Assert.IsTrue(DockExecutionRules.CanExecute(plan,
                scenario.Topology.Generation, epoch),
                "current plan rejected seed=" + seed);
            Assert.IsFalse(DockExecutionRules.CanExecute(plan,
                scenario.Topology.Generation + 1, epoch),
                "stale generation executed seed=" + seed);
            Assert.IsFalse(DockExecutionRules.CanExecute(plan,
                scenario.Topology.Generation, epoch + 1),
                "stale epoch executed seed=" + seed);
        }

        [TestMethod]
        public void DisplayGeometry_ExtremeCoordinatesNeverWrap()
        {
            int[] origins =
            {
                Int32.MinValue, -1000000000, -100000, 0,
                100000, 1000000000, Int32.MaxValue
            };
            double[] scales =
            {
                1.0, 1.25, 1.5, 1.75, 2.0, 2.25
            };

            foreach (int originX in origins)
                foreach (int originY in origins)
                    foreach (double scale in scales)
                    {
                        PhysicalRect physical =
                            DisplayGeometry.ProjectLocalRect(
                                new LogicalRect
                                {
                                    X = originX,
                                    Y = originY,
                                    Width = Int32.MaxValue,
                                    Height = Int32.MaxValue
                                },
                                originX, originY, scale);
                        Assert.IsTrue(physical.Width > 0);
                        Assert.IsTrue(physical.Height > 0);
                    }
        }

        [TestMethod]
        public void DynamicNDisplayFactory_IsDeterministicPerSeed()
        {
            for (int seed = 0; seed < 100; seed++)
            {
                SyntheticDisplayScenario a =
                    DynamicNDisplayScenarioFactory.Create(seed, 5);
                SyntheticDisplayScenario b =
                    DynamicNDisplayScenarioFactory.Create(seed, 5);

                Assert.AreEqual(a.Topology.Surfaces.Count,
                    b.Topology.Surfaces.Count);
                foreach (DisplaySurfaceSnapshot surface
                    in a.Topology.Surfaces)
                {
                    DisplaySurfaceSnapshot other =
                        b.Topology.FindByRuntimeSurfaceId(
                            surface.RuntimeSurfaceId);
                    Assert.IsNotNull(other);
                    Assert.AreEqual(surface.Bounds.Left, other.Bounds.Left);
                    Assert.AreEqual(surface.Bounds.Top, other.Bounds.Top);
                    Assert.AreEqual(surface.Bounds.Width, other.Bounds.Width);
                    Assert.AreEqual(surface.Bounds.Height,
                        other.Bounds.Height);
                    Assert.AreEqual(a.DpiFor(surface),
                        b.DpiFor(other));
                }
            }
        }
    }
}
