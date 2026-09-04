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
