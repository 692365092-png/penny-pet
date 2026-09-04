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
