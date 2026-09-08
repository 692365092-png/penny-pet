using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StartupPetPlacementTests
    {
        private static DisplaySurfaceSnapshot Surface(string id,
            string gdi, PhysicalRect bounds, PhysicalRect workArea,
            bool primary, double scale, string targetKey)
        {
            return new DisplaySurfaceSnapshot(id, gdi, bounds, workArea,
                primary, 0,
                new[] { new DisplayTargetIdentity(targetKey, true,
                    targetKey, id, 0, 0, 0) }, scale);
        }

        private static DisplaySurfaceSnapshot Surface(string id,
            string gdi, PhysicalRect bounds, bool primary, double scale,
            string targetKey)
        {
            return Surface(id, gdi, bounds, bounds, primary, scale,
                targetKey);
        }

        private static DisplayTopologySnapshot Topology(
            params DisplaySurfaceSnapshot[] surfaces)
        {
            return new DisplayTopologySnapshot(0, surfaces);
        }

        private static StartupPetPlacementSnapshot Resolve(
            string key, int preferredX, int preferredY,
            DisplayTopologySnapshot topology,
            bool hasLegacy = false, int legacyX = 0, int legacyY = 0)
        {
            return PetPlacementPolicy.ResolveStartupPetPlacement(
                key, new LogicalPoint { X = preferredX, Y = preferredY },
                hasLegacy, legacyX, legacyY, 192, 208, topology);
        }

        [TestMethod]
        public void PreferredTarget_ProjectsLocalPointAndSizeAt150Percent()
        {
            DisplaySurfaceSnapshot surface = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(1920, 0, 1920, 1080),
                true, 1.5, "mdp:left");
            StartupPetPlacementSnapshot placement = Resolve(
                "mdp:left", 100, 80, Topology(surface));
            Assert.IsNotNull(placement);
            Assert.AreEqual(144, placement.TargetDpi);
            Assert.AreEqual(288, placement.PhysicalBounds.Width);
            Assert.AreEqual(312, placement.PhysicalBounds.Height);
            Assert.AreEqual(1920 + 150, placement.PhysicalBounds.Left);
            Assert.AreEqual(120, placement.PhysicalBounds.Top);
        }

        [TestMethod]
        public void PreferredTarget_UsesSecondaryMixedDpiSurface()
        {
            DisplaySurfaceSnapshot primary = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080),
                true, 1.0, "mdp:left");
            DisplaySurfaceSnapshot secondary = Surface("surface-2",
                "\\\\.\\DISPLAY2", new PhysicalRect(1920, 0, 1920, 1080),
                false, 2.0, "mdp:right");
            StartupPetPlacementSnapshot placement = Resolve(
                "mdp:right", 40, 30, Topology(primary, secondary));
            Assert.IsNotNull(placement);
            Assert.AreEqual(192, placement.TargetDpi);
            Assert.AreEqual(384, placement.PhysicalBounds.Width);
            Assert.AreEqual(416, placement.PhysicalBounds.Height);
            Assert.AreEqual(1920 + 80, placement.PhysicalBounds.Left);
            Assert.AreEqual(60, placement.PhysicalBounds.Top);
        }

        [TestMethod]
        public void PreferredTarget_ClampsIntoWorkArea()
        {
            DisplaySurfaceSnapshot surface = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080),
                new PhysicalRect(0, 0, 1920, 900), true, 1.0, "mdp:left");
            StartupPetPlacementSnapshot placement = Resolve(
                "mdp:left", 100, 2000, Topology(surface));
            Assert.IsNotNull(placement);
            Assert.AreEqual(192, placement.PhysicalBounds.Width);
            Assert.AreEqual(208, placement.PhysicalBounds.Height);
            Assert.AreEqual(900 - 208, placement.PhysicalBounds.Top);
        }

        [TestMethod]
        public void PreferredMonitorMissing_FallsBackToPrimaryBottomRight()
        {
            DisplaySurfaceSnapshot primary = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080),
                true, 1.0, "mdp:left");
            DisplaySurfaceSnapshot secondary = Surface("surface-2",
                "\\\\.\\DISPLAY2", new PhysicalRect(1920, 0, 1920, 1080),
                false, 2.0, "mdp:right");
            StartupPetPlacementSnapshot placement = Resolve(
                "mdp:missing", 10, 10, Topology(primary, secondary));
            Assert.IsNotNull(placement);
            Assert.AreEqual(96, placement.TargetDpi);
            Assert.AreEqual(192, placement.PhysicalBounds.Width);
            Assert.AreEqual(208, placement.PhysicalBounds.Height);
            Assert.AreEqual(1920 - 192 - 24, placement.PhysicalBounds.Left);
            Assert.AreEqual(1080 - 208 - 24, placement.PhysicalBounds.Top);
        }

        [TestMethod]
        public void FirstLaunch_NoHistory_UsesPrimaryBottomRight()
        {
            DisplaySurfaceSnapshot primary = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 2560, 1440),
                true, 1.5, "mdp:left");
            StartupPetPlacementSnapshot placement = Resolve(
                String.Empty, 0, 0, Topology(primary));
            Assert.IsNotNull(placement);
            Assert.AreEqual(144, placement.TargetDpi);
            Assert.AreEqual(288, placement.PhysicalBounds.Width);
            Assert.AreEqual(312, placement.PhysicalBounds.Height);
            Assert.AreEqual(2560 - 288 - 36, placement.PhysicalBounds.Left);
            Assert.AreEqual(1440 - 312 - 36, placement.PhysicalBounds.Top);
        }

        [TestMethod]
        public void LegacyLocation_MigratesWithinContainingSurface()
        {
            DisplaySurfaceSnapshot primary = Surface("surface-1",
                "\\\\.\\DISPLAY1", new PhysicalRect(0, 0, 1920, 1080),
                true, 1.0, "mdp:left");
            DisplaySurfaceSnapshot secondary = Surface("surface-2",
                "\\\\.\\DISPLAY2", new PhysicalRect(1920, 0, 1920, 1080),
                false, 1.5, "mdp:right");
            StartupPetPlacementSnapshot placement = Resolve(
                String.Empty, 0, 0, Topology(primary, secondary),
                true, 2100, 300);
            Assert.IsNotNull(placement);
            Assert.AreEqual(144, placement.TargetDpi);
            Assert.AreEqual(288, placement.PhysicalBounds.Width);
            Assert.AreEqual(312, placement.PhysicalBounds.Height);
            Assert.AreEqual(2100, placement.PhysicalBounds.Left);
            Assert.AreEqual(300, placement.PhysicalBounds.Top);
        }
    }
}
