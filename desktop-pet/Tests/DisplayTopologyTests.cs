using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DisplayTopologyTests
    {
        [TestMethod]
        public void TopologySnapshot_DoesNotDependOnListIndex()
        {
            DisplaySurfaceSnapshot left = Surface(1, false, -1920,
                Target("mdp:left"));
            DisplaySurfaceSnapshot primary = Surface(2, true, 0,
                Target("mdp:primary"));
            DisplaySurfaceSnapshot right = Surface(3, false, 1920,
                Target("mdp:right"));

            DisplayTopologySnapshot first = new DisplayTopologySnapshot(7,
                new[] { left, primary, right });
            DisplayTopologySnapshot reordered = new DisplayTopologySnapshot(7,
                new[] { right, left, primary });

            Assert.AreEqual("surface-2",
                first.FindByTargetKey("mdp:primary").RuntimeSurfaceId);
            Assert.AreEqual("surface-2",
                reordered.FindByTargetKey("mdp:primary").RuntimeSurfaceId);
            Assert.AreEqual("surface-2",
                first.PrimaryOrFirst().RuntimeSurfaceId);
            Assert.AreEqual("surface-2",
                reordered.PrimaryOrFirst().RuntimeSurfaceId);
        }

        [TestMethod]
        public void FindByTargetKey_ResolvesMirrorTargetAliases()
        {
            DisplaySurfaceSnapshot mirror = Surface(4, true, 0,
                Target("mdp:a"), Target("mdp:b"), Target("ephemeral:c"));
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(1,
                new[] { mirror });

            Assert.AreSame(mirror, topology.FindByTargetKey("MDP:A"));
            Assert.AreSame(mirror, topology.FindByTargetKey("mdp:b"));
            Assert.AreSame(mirror,
                topology.FindByTargetKey("ephemeral:c"));
            Assert.IsNull(topology.FindByTargetKey("mdp:missing"));
        }

        [TestMethod]
        public void FindByRuntimeGdiName_IsCaseInsensitive()
        {
            DisplaySurfaceSnapshot surface = Surface(5, true, 0,
                Target("mdp:only"));
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(2,
                new[] { surface });

            Assert.AreSame(surface,
                topology.FindByRuntimeGdiName("\\\\.\\display5"));
            Assert.IsNull(topology.FindByRuntimeGdiName("\\\\.\\DISPLAY9"));
        }

        [TestMethod]
        public void PrimaryOrFirst_UsesFlagThenSingleSurfaceFallback()
        {
            DisplaySurfaceSnapshot nonPrimary = Surface(1, false, -800,
                Target("mdp:left"));
            DisplaySurfaceSnapshot primary = Surface(2, true, 0,
                Target("mdp:primary"));
            Assert.AreSame(primary, new DisplayTopologySnapshot(1,
                new[] { nonPrimary, primary }).PrimaryOrFirst());
            Assert.AreSame(nonPrimary, new DisplayTopologySnapshot(2,
                new[] { nonPrimary }).PrimaryOrFirst());
        }

        [TestMethod]
        public void TopologySnapshot_SupportsOneAndNineNegativeOriginSurfaces()
        {
            DisplaySurfaceSnapshot single = Surface(1, true, -3840,
                Target("mdp:single"));
            DisplayTopologySnapshot one = new DisplayTopologySnapshot(1,
                new[] { single });
            Assert.AreEqual(1, one.Surfaces.Count);
            Assert.AreEqual(-3840, one.Surfaces[0].Bounds.Left);
            Assert.AreEqual(-1920, one.Surfaces[0].Bounds.Right);

            List<DisplaySurfaceSnapshot> nine =
                new List<DisplaySurfaceSnapshot>();
            for (int i = 0; i < 9; i++)
                nine.Add(Surface(i + 1, i == 4, (i - 4) * 1920,
                    Target("mdp:" + i)));
            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(9, nine);
            Assert.AreEqual(9, topology.Surfaces.Count);
            Assert.AreEqual("surface-5",
                topology.PrimaryOrFirst().RuntimeSurfaceId);
            Assert.AreEqual(-7680, topology.Surfaces[0].Bounds.Left);
        }

        [TestMethod]
        public void TopologySnapshot_RejectsEmptyOrAmbiguousTopology()
        {
            AssertRejectsArgument(delegate
            {
                new DisplayTopologySnapshot(0,
                    new DisplaySurfaceSnapshot[0]);
            });
            AssertRejectsArgument(delegate
            {
                new DisplayTopologySnapshot(0, new[]
                {
                    Surface(1, true, 0, Target("mdp:same")),
                    Surface(2, false, 1920, Target("mdp:same"))
                });
            });
        }

        private static void AssertRejectsArgument(Action action)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            Assert.Fail("Expected ArgumentException.");
        }

        [TestMethod]
        public void TopologySnapshot_CopiesMutableInputCollections()
        {
            DisplayTargetIdentity originalTarget = Target("mdp:original");
            DisplayTargetIdentity[] targets = { originalTarget };
            DisplaySurfaceSnapshot originalSurface = SurfaceWithTargets(1,
                true, 0, targets);
            DisplaySurfaceSnapshot[] surfaces = { originalSurface };
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(1,
                surfaces);

            targets[0] = Target("mdp:changed");
            surfaces[0] = Surface(2, true, 1920, Target("mdp:other"));

            Assert.AreSame(originalTarget, originalSurface.Targets[0]);
            Assert.AreSame(originalSurface, topology.Surfaces[0]);
            Assert.AreSame(originalSurface,
                topology.FindByTargetKey("mdp:original"));
        }

        [TestMethod]
        public void TopologySnapshot_RandomizedLookupIgnoresOrder()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                Random random = new Random(seed);
                int count = random.Next(1, 17);
                int primaryIndex = random.Next(count);
                List<DisplaySurfaceSnapshot> surfaces =
                    new List<DisplaySurfaceSnapshot>(count);
                for (int index = 0; index < count; index++)
                {
                    int left = random.Next(-20000, 20001);
                    surfaces.Add(Surface(index + 1,
                        index == primaryIndex, left,
                        Target("seed:" + seed + ":target:" + index)));
                }
                Shuffle(surfaces, random);
                DisplayTopologySnapshot topology =
                    new DisplayTopologySnapshot(seed, surfaces);

                Assert.AreEqual(count, topology.Surfaces.Count);
                Assert.AreEqual("surface-" + (primaryIndex + 1),
                    topology.PrimaryOrFirst().RuntimeSurfaceId);
                for (int index = 0; index < count; index++)
                {
                    string expected = "surface-" + (index + 1);
                    Assert.AreEqual(expected, topology.FindByTargetKey(
                        "seed:" + seed + ":target:" + index)
                        .RuntimeSurfaceId);
                    Assert.AreEqual(expected, topology.FindByRuntimeGdiName(
                        "\\\\.\\DISPLAY" + (index + 1))
                        .RuntimeSurfaceId);
                }
            }
        }

        [TestMethod]
        public void PlacementModels_KeepPreferredAndEffectiveStateSeparate()
        {
            WindowPlacementPreference preferred =
                new WindowPlacementPreference("mdp:home", new LogicalRect
                {
                    X = 10,
                    Y = 20,
                    Width = 320,
                    Height = 300
                });
            WindowFacts effective = new WindowFacts("note-1", "mdp:fallback",
                "\\\\.\\DISPLAY2", new PhysicalRect(1920, 0, 640, 600),
                192, 4, 8);
            WindowPlacementRuntimeState state =
                new WindowPlacementRuntimeState(preferred, effective, true,
                    false, "preferred-target-missing");

            Assert.IsTrue(preferred.IsValid);
            Assert.AreEqual(2D, effective.Scale, 0.0001D);
            Assert.AreSame(preferred, state.Preferred);
            Assert.AreSame(effective, state.Effective);
            Assert.IsTrue(state.IsTemporaryRehome);
            Assert.IsFalse(state.UserMovedSinceRehome);
            Assert.AreEqual("preferred-target-missing",
                state.TemporaryReason);
        }

        [TestMethod]
        public void ProjectLocalRect_RoundsAwayFromZeroAndClampsOverflow()
        {
            // 150% projection of a 320x300 logical note on a monitor whose
            // physical origin sits at (1920, 0).
            PhysicalRect projected = DisplayGeometry.ProjectLocalRect(
                new LogicalRect
                {
                    X = 100,
                    Y = 50,
                    Width = 320,
                    Height = 300
                }, 1920, 0, 1.5);
            Assert.AreEqual(1920 + 150, projected.Left);
            Assert.AreEqual(75, projected.Top);
            Assert.AreEqual(480, projected.Width);
            Assert.AreEqual(450, projected.Height);

            // Half-pixel boundaries round away from zero in both directions.
            PhysicalRect half = DisplayGeometry.ProjectLocalRect(
                new LogicalRect { X = 1, Y = 1, Width = 3, Height = 3 },
                0, 0, 1.5);
            Assert.AreEqual(2, half.Left);
            Assert.AreEqual(2, half.Top);
            Assert.AreEqual(5, half.Width);
            Assert.AreEqual(5, half.Height);
            PhysicalRect negativeHalf = DisplayGeometry.ProjectLocalRect(
                new LogicalRect { X = -1, Y = -1, Width = 1, Height = 1 },
                0, 0, 1.5);
            Assert.AreEqual(-2, negativeHalf.Left);
            Assert.AreEqual(-2, negativeHalf.Top);

            // Extreme coordinates clamp to Int32 bounds instead of wrapping.
            PhysicalRect extreme = DisplayGeometry.ProjectLocalRect(
                new LogicalRect
                {
                    X = int.MaxValue,
                    Y = int.MinValue,
                    Width = int.MaxValue,
                    Height = int.MaxValue
                }, int.MaxValue, int.MinValue, 8.0);
            Assert.AreEqual(int.MaxValue, extreme.Left);
            Assert.AreEqual(int.MinValue, extreme.Top);
            Assert.AreEqual(int.MaxValue, extreme.Width);
            Assert.AreEqual(int.MaxValue, extreme.Height);
        }

        [TestMethod]
        public void IsWithinPlacementTolerance_ChecksAllFourEdges()
        {
            PhysicalRect requested = new PhysicalRect(100, 200, 480, 450);
            Assert.IsTrue(DisplayGeometry.IsWithinPlacementTolerance(
                requested, new PhysicalRect(102, 202, 480, 450), 2));
            Assert.IsFalse(DisplayGeometry.IsWithinPlacementTolerance(
                requested, new PhysicalRect(103, 200, 480, 450), 2));
            Assert.IsFalse(DisplayGeometry.IsWithinPlacementTolerance(
                requested, new PhysicalRect(100, 200, 483, 450), 2));
            Assert.IsFalse(DisplayGeometry.IsWithinPlacementTolerance(
                requested, new PhysicalRect(100, 200, 480, 453), 2));
            Assert.IsTrue(DisplayGeometry.IsWithinPlacementTolerance(
                requested, requested, -5));
            Assert.IsFalse(DisplayGeometry.IsWithinPlacementTolerance(
                requested, new PhysicalRect(101, 200, 480, 450), -5));
        }

        [TestMethod]
        public void WithGeneration_RebrandsSurfacesWithoutCopyingThem()
        {
            DisplaySurfaceSnapshot surface = Surface(1, true, 0,
                Target("mdp:only"));
            DisplayTopologySnapshot original = new DisplayTopologySnapshot(0,
                new[] { surface });
            DisplayTopologySnapshot rebranded =
                original.WithGeneration(42);

            Assert.AreEqual(0, original.Generation);
            Assert.AreEqual(42, rebranded.Generation);
            Assert.AreSame(surface, original.Surfaces[0]);
            Assert.AreSame(surface, rebranded.Surfaces[0]);
            Assert.AreNotSame(original, rebranded);
        }

        [TestMethod]
        public void SelectPreferredTargetKey_KeepsExistingKeyAndIsDeterministic()
        {
            DisplaySurfaceSnapshot mirror = Surface(6, true, 0,
                Target("ephemeral:z"), Target("mdp:b"), Target("mdp:a"));
            Assert.AreEqual("mdp:a", DisplayTopologyRules.
                SelectPreferredTargetKey(mirror, null));
            Assert.AreEqual("mdp:b", DisplayTopologyRules.
                SelectPreferredTargetKey(mirror, "mdp:b"));
            Assert.AreEqual("ephemeral:z", DisplayTopologyRules.
                SelectPreferredTargetKey(mirror, "ephemeral:z"));

            // Enumeration order must not change the chosen durable key.
            DisplaySurfaceSnapshot reversed = Surface(7, true, 0,
                Target("mdp:a"), Target("mdp:b"), Target("ephemeral:z"));
            Assert.AreEqual("mdp:a", DisplayTopologyRules.
                SelectPreferredTargetKey(reversed, null));

            // Durable targets win over ephemeral targets without a preference.
            DisplaySurfaceSnapshot mixed = Surface(8, true, 0,
                Target("ephemeral:a"), Target("mdp:z"));
            Assert.AreEqual("mdp:z", DisplayTopologyRules.
                SelectPreferredTargetKey(mixed, null));
        }

        [TestMethod]
        public void PreferenceFromPhysicalRect_UsesWindowScale()
        {
            WindowPlacementPreference preference =
                StickyPlacementMath.PreferenceFromPhysicalRect("mdp:home",
                    1920, 0, 1.5,
                    new PhysicalRect(2070, 60, 480, 450));
            Assert.AreEqual("mdp:home", preference.PreferredTargetKey);
            Assert.AreEqual(100, preference.LocalLogicalRect.X);
            Assert.AreEqual(40, preference.LocalLogicalRect.Y);
            Assert.AreEqual(320, preference.LocalLogicalRect.Width);
            Assert.AreEqual(300, preference.LocalLogicalRect.Height);
            Assert.IsTrue(preference.IsValid);
        }

        [TestMethod]
        public void SelectPreferredTargetKey_NeverFabricatesDurableFromEphemeral()
        {
            DisplaySurfaceSnapshot ephemeralOnly = Surface(9, true, 0,
                Target("ephemeral:a"), Target("ephemeral:b"));
            Assert.IsNull(DisplayTopologyRules.SelectPreferredTargetKey(
                ephemeralOnly, null));
            // An existing key that belongs to the surface is still kept.
            Assert.AreEqual("ephemeral:b", DisplayTopologyRules.
                SelectPreferredTargetKey(ephemeralOnly, "ephemeral:b"));

            DisplaySurfaceSnapshot cased = new DisplaySurfaceSnapshot(
                "surface-10", "\\\\.\\DISPLAY10",
                new PhysicalRect(0, 0, 1920, 1080),
                new PhysicalRect(0, 0, 1920, 1040), true, 0,
                new[]
                {
                    new DisplayTargetIdentity("mdp:B", true,
                        String.Empty, String.Empty, 0, 0, 0),
                    new DisplayTargetIdentity("mdp:a", true,
                        String.Empty, String.Empty, 0, 0, 0)
                });
            Assert.AreEqual("mdp:a", DisplayTopologyRules.
                SelectPreferredTargetKey(cased, null));
        }

        [TestMethod]
        public void StickySpawnPolicy_CentersInWorkAreaAcrossOriginsAndScales()
        {
            PhysicalRect plain = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(0, 0, 1920, 1040), 320, 300);
            Assert.AreEqual(800, plain.Left);
            Assert.AreEqual(370, plain.Top);
            Assert.AreEqual(320, plain.Width);
            Assert.AreEqual(300, plain.Height);

            PhysicalRect scaled = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(0, 0, 1920, 1040), 640, 600);
            Assert.AreEqual(640, scaled.Left);
            Assert.AreEqual(220, scaled.Top);

            PhysicalRect negativeX = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(-2560, 0, 2560, 1400), 320, 300);
            Assert.AreEqual(-1440, negativeX.Left);
            Assert.AreEqual(550, negativeX.Top);

            PhysicalRect negativeY = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(0, -200, 1920, 1040), 320, 300);
            Assert.AreEqual(800, negativeY.Left);
            Assert.AreEqual(170, negativeY.Top);

            PhysicalRect tiny = StickySpawnPolicy.CenterInWorkArea(
                new PhysicalRect(0, 0, 200, 150), 320, 300);
            Assert.AreEqual(0, tiny.Left);
            Assert.AreEqual(0, tiny.Top);
            Assert.AreEqual(200, tiny.Width);
            Assert.AreEqual(150, tiny.Height);
        }

        [TestMethod]
        public void StickySpawnPolicy_PlanCenteredSpawn_RoundTripsLocalRect()
        {
            StickyCanonicalPlacement placement =
                StickySpawnPolicy.PlanCenteredSpawn("\\\\.\\DISPLAY2",
                    new PhysicalRect(1920, 0, 1920, 1040), 1920, 0, 1.5,
                    320, 300);
            Assert.AreEqual("\\\\.\\DISPLAY2", placement.DisplayId);
            Assert.AreEqual(480, placement.LocalX);
            Assert.AreEqual(197, placement.LocalY);
            Assert.AreEqual(320, placement.LocalWidth);
            Assert.AreEqual(300, placement.LocalHeight);
            Assert.AreEqual(2640, placement.PhysicalLeft);
            Assert.AreEqual(295, placement.PhysicalTop);
            Assert.AreEqual(480, placement.PhysicalWidth);
            Assert.AreEqual(450, placement.PhysicalHeight);
        }

        [TestMethod]
        public void FallbackDisplayPolicy_FollowsPreferenceThenRectThenPetThenPrimary()
        {
            DisplaySurfaceSnapshot left = Surface(1, false, -1920,
                Target("mdp:left"));
            DisplaySurfaceSnapshot primary = Surface(2, true, 0,
                Target("mdp:primary"));
            DisplaySurfaceSnapshot right = Surface(3, false, 1920,
                Target("mdp:right"));
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(1,
                new[] { left, primary, right });

            // 1. Preferred target active wins.
            Assert.AreSame(right, FallbackDisplayPolicy.
                ResolveFallbackSurface(topology, "mdp:right",
                    new PhysicalRect(), String.Empty));

            // 2. Last physical rect clearly inside an active work area wins.
            Assert.AreSame(left, FallbackDisplayPolicy.
                ResolveFallbackSurface(topology, "mdp:gone",
                    new PhysicalRect(-1800, 100, 320, 300), String.Empty));

            // 3. Rect off-screen: Pet's current surface wins.
            Assert.AreSame(right, FallbackDisplayPolicy.
                ResolveFallbackSurface(topology, "mdp:gone",
                    new PhysicalRect(99999, 99999, 320, 300),
                    "\\\\.\\DISPLAY3"));

            // 4. Pet surface missing too: primary wins.
            Assert.AreSame(primary, FallbackDisplayPolicy.
                ResolveFallbackSurface(topology, "mdp:gone",
                    new PhysicalRect(99999, 99999, 320, 300),
                    "\\\\.\\DISPLAY9"));

            // 5. Always one result as long as at least one surface exists.
            Assert.AreSame(primary, FallbackDisplayPolicy.
                ResolveFallbackSurface(topology, "mdp:gone",
                    new PhysicalRect(), String.Empty));
        }

        [TestMethod]
        public void DockPlacementPlanner_StacksLogicalMembersAt100And200Percent()
        {
            DockGroupLogicalState group = DockGroup(40, 50);
            DisplaySurfaceSnapshot plain = Surface(2, true, 1920,
                Target("mdp:plain"));
            DockPlacementPlan at100 = DockPlacementPlanner.Plan(group,
                Facts("A", plain, 96, 7), plain, 96, 7, 11);

            AssertPlan(at100, 7, 11, "A", "surface-2", 96,
                new PhysicalRect(1960, -50, 320, 300),
                new PhysicalRect(1960, 250, 320, 400),
                new PhysicalRect(1960, 650, 320, 260));

            DisplaySurfaceSnapshot scaled = new DisplaySurfaceSnapshot(
                "surface-7", "\\\\.\\DISPLAY7",
                new PhysicalRect(3840, 0, 3840, 2160),
                new PhysicalRect(3840, 0, 3840, 2080), false, 0,
                new[] { Target("mdp:scaled") });
            DockPlacementPlan at200 = DockPlacementPlanner.Plan(group,
                Facts("B", scaled, 192, 8), scaled, 192, 8, 12);

            AssertPlan(at200, 8, 12, "B", "surface-7", 192,
                new PhysicalRect(3920, 100, 640, 600),
                new PhysicalRect(3920, 700, 640, 800),
                new PhysicalRect(3920, 1500, 640, 520));
        }

        [TestMethod]
        [DataRow(120, 1.25)]
        [DataRow(144, 1.50)]
        [DataRow(168, 1.75)]
        [DataRow(216, 2.25)]
        public void DockPlacementPlanner_UsesOneScaleAndRoundingPolicy(
            int dpi, double scale)
        {
            DisplaySurfaceSnapshot surface = Surface(4, true, -3840,
                Target("mdp:fractional"));
            DockGroupLogicalState group = new DockGroupLogicalState(
                new LogicalPoint { X = 41, Y = 53 },
                new[]
                {
                    new DockLogicalMember("A", 321, 301),
                    new DockLogicalMember("B", 321, 399),
                    new DockLogicalMember("C", 321, 261)
                });
            DockPlacementPlan plan = DockPlacementPlanner.Plan(
                group, Facts("C", surface, dpi, 9),
                surface, dpi, 9, 13);

            Assert.AreEqual(-3840 + (int)Math.Round(41 * scale,
                MidpointRounding.AwayFromZero),
                plan.WindowTargets[0].PhysicalBounds.Left);
            Assert.AreEqual(-100 + (int)Math.Round(53 * scale,
                MidpointRounding.AwayFromZero),
                plan.WindowTargets[0].PhysicalBounds.Top);
            int expectedFirstBottom = -100 + (int)Math.Round(
                (53 + 301) * scale, MidpointRounding.AwayFromZero);
            Assert.AreEqual(expectedFirstBottom -
                plan.WindowTargets[0].PhysicalBounds.Top,
                plan.WindowTargets[0].PhysicalBounds.Height);
            Assert.AreEqual(plan.WindowTargets[0].PhysicalBounds.Bottom,
                plan.WindowTargets[1].PhysicalBounds.Top);
            Assert.AreEqual(plan.WindowTargets[1].PhysicalBounds.Bottom,
                plan.WindowTargets[2].PhysicalBounds.Top);
        }

        [TestMethod]
        public void DockPlacementPlanner_UsesOnlyTheSourceTargetSurface()
        {
            DockGroupLogicalState group = DockGroup(20, 30);
            DisplaySurfaceSnapshot a = Surface(1, true, 0,
                Target("mdp:a"));
            DisplaySurfaceSnapshot g = Surface(7, false, 11520,
                Target("mdp:g"));
            // Extra surfaces and their order never enter the planner.
            DisplayTopologySnapshot many = new DisplayTopologySnapshot(10,
                new[] { g, Surface(3, false, 3840, Target("mdp:c")), a });

            DockPlacementPlan planA = DockPlacementPlanner.Plan(group,
                Facts("A", a, 96, 10), a, 96, 10, 1);
            DockPlacementPlan planG = DockPlacementPlanner.Plan(group,
                Facts("A", many.FindByTargetKey("mdp:g"), 144, 10),
                g, 144, 10, 2);

            Assert.AreEqual("surface-1", planA.TargetSurfaceId);
            Assert.AreEqual("surface-7", planG.TargetSurfaceId);
            foreach (DockWindowTarget target in planG.WindowTargets)
                Assert.IsTrue(target.PhysicalBounds.Left >= g.Bounds.Left);
        }

        [TestMethod]
        public void DockPlacementPlanner_CopiesInputsAndRejectsStaleFacts()
        {
            List<DockLogicalMember> members = new List<DockLogicalMember>
            {
                new DockLogicalMember("A", 320, 300),
                new DockLogicalMember("B", 320, 400)
            };
            DockGroupLogicalState group = new DockGroupLogicalState(
                new LogicalPoint { X = 10, Y = 20 }, members);
            members.Clear();
            DisplaySurfaceSnapshot target = Surface(5, true, 0,
                Target("mdp:target"));
            DockPlacementPlan plan = DockPlacementPlanner.Plan(group,
                Facts("A", target, 96, 12), target, 96, 12, 4);

            Assert.AreEqual(2, group.Members.Count);
            Assert.AreEqual(2, plan.WindowTargets.Count);
            AssertRejectsArgument(delegate
            {
                DockPlacementPlanner.Plan(group,
                    Facts("A", target, 120, 12), target, 96, 12, 5);
            });
            AssertRejectsArgument(delegate
            {
                DockPlacementPlanner.Plan(group,
                    Facts("A", target, 96, 11), target, 96, 12, 5);
            });
            DisplaySurfaceSnapshot other = Surface(6, false, 1920,
                Target("mdp:other"));
            AssertRejectsArgument(delegate
            {
                DockPlacementPlanner.Plan(group,
                    Facts("A", other, 96, 12), target, 96, 12, 5);
            });
        }

        private static DockGroupLogicalState DockGroup(int x, int y)
        {
            return new DockGroupLogicalState(
                new LogicalPoint { X = x, Y = y },
                new[]
                {
                    new DockLogicalMember("A", 320, 300),
                    new DockLogicalMember("B", 320, 400),
                    new DockLogicalMember("C", 320, 260)
                });
        }

        private static WindowFacts Facts(string noteId,
            DisplaySurfaceSnapshot surface, int dpi, long generation)
        {
            return new WindowFacts(noteId,
                surface.Targets.Count == 0
                    ? String.Empty : surface.Targets[0].StableKey,
                surface.RuntimeGdiName, surface.Bounds, dpi, generation, 1);
        }

        private static void AssertPlan(DockPlacementPlan plan,
            long generation, long sequence, string sourceNoteId,
            string surfaceId, int dpi, params PhysicalRect[] expected)
        {
            Assert.AreEqual(generation, plan.TopologyGeneration);
            Assert.AreEqual(sequence, plan.PlanSequence);
            Assert.AreEqual(sourceNoteId, plan.SourceNoteId);
            Assert.AreEqual(surfaceId, plan.TargetSurfaceId);
            Assert.AreEqual(dpi, plan.TargetDpi);
            Assert.AreEqual(expected.Length, plan.WindowTargets.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                PhysicalRect actual =
                    plan.WindowTargets[index].PhysicalBounds;
                Assert.AreEqual(expected[index].Left, actual.Left);
                Assert.AreEqual(expected[index].Top, actual.Top);
                Assert.AreEqual(expected[index].Width, actual.Width);
                Assert.AreEqual(expected[index].Height, actual.Height);
            }
        }

        private static DisplayTargetIdentity Target(string stableKey)
        {
            return new DisplayTargetIdentity(stableKey,
                stableKey.StartsWith("mdp:", StringComparison.Ordinal),
                stableKey, "Display", 0, 0, 0);
        }

        private static DisplaySurfaceSnapshot Surface(int index,
            bool primary, int left,
            params DisplayTargetIdentity[] targets)
        {
            return SurfaceWithTargets(index, primary, left, targets);
        }

        private static DisplaySurfaceSnapshot SurfaceWithTargets(int index,
            bool primary, int left, DisplayTargetIdentity[] targets)
        {
            return new DisplaySurfaceSnapshot("surface-" + index,
                "\\\\.\\DISPLAY" + index,
                new PhysicalRect(left, -100, 1920, 1080),
                new PhysicalRect(left, -100, 1920, 1040), primary, 0,
                targets);
        }

        private static void Shuffle(List<DisplaySurfaceSnapshot> values,
            Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                DisplaySurfaceSnapshot value = values[index];
                values[index] = values[swapIndex];
                values[swapIndex] = value;
            }
        }
    }
}
