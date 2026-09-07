using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class PetDisplayPlacementTests
    {
        [TestMethod]
        public void StaleFacts_CannotCommitPreferredPoint()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "s", "display", "mdp:a", 0, 0, 1920, 1080, true);
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(
                2, new[] { surface });
            WindowFacts facts = new WindowFacts("pet", "mdp:a", "display",
                new PhysicalRect(100, 100, 192, 208), 96, 1, 10);
            Assert.IsFalse(PetPlacementPolicy.TryBuildPreferredPoint(
                facts, topology, null, out _, out _));
        }

        [TestMethod]
        public void RuntimeContract_ActualDpiAndPreferredCommitHaveExplicitOwners()
        {
            string runtime = StickySessionTopologyContractTests.ReadSource(
                "PetDisplayRuntime.cs");
            string form = StickySessionTopologyContractTests.ReadSource("PetForm.cs");
            string dpi = StickySessionTopologyContractTests.SliceMethod(
                form, "protected override void OnDpiChanged");
            Assert.IsTrue(dpi.Contains("ActualPetDpi(e.DeviceDpiNew)"));
            Assert.IsTrue(runtime.Contains("NativeDisplayConfig.GetDpiForWindow(Handle)"));
            Assert.IsFalse(form.Contains("Screen.PrimaryScreen"));
            string compatibility = StickySessionTopologyContractTests.SliceMethod(
                form, "private void SaveLocation()");
            Assert.IsFalse(compatibility.Contains("PetPreferredTargetKey ="));
            string reconcile = StickySessionTopologyContractTests.SliceMethod(
                runtime, "private void ReconcilePetDisplayPlacement");
            Assert.IsFalse(reconcile.Contains("CommitPetPreferredFromFacts("));
            Assert.IsTrue(reconcile.Contains("if (_dragging)"));
            Assert.IsTrue(reconcile.Contains("_petUserMovedSinceTemporaryRehome"));
        }

        private static DisplaySurfaceSnapshot Surface(
            string id, string gdi, string key,
            int left, int top, int width, int height, bool primary)
        {
            return new DisplaySurfaceSnapshot(
                id, gdi,
                new PhysicalRect(left, top, width, height),
                new PhysicalRect(left, top, width, height - 40),
                primary, 0,
                new[]
                {
                    new DisplayTargetIdentity(
                        key, true, "path-" + key, key, 0, 0, 0)
                });
        }

        [TestMethod]
        public void PreferredPoint_UsesActualDpiAndOrigin()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "s2", "\\\\.\\DISPLAY2", "mdp:b",
                1920, 0, 2560, 1440, false);

            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(8, new[] { surface });

            WindowFacts facts = new WindowFacts(
                "pet", "mdp:b", "\\\\.\\DISPLAY2",
                new PhysicalRect(2020, 100, 384, 416),
                192, 8, 10);

            string key;
            LogicalPoint local;

            Assert.IsTrue(PetPlacementPolicy.TryBuildPreferredPoint(
                facts, topology, null, out key, out local));

            Assert.AreEqual("mdp:b", key);
            Assert.AreEqual(50, local.X);
            Assert.AreEqual(50, local.Y);
        }

        [TestMethod]
        public void PreferredPoint_WorksOnNegativeOrigin()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "left", "\\\\.\\DISPLAY3", "mdp:left",
                -2560, -200, 2560, 1440, true);

            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(3, new[] { surface });

            WindowFacts facts = new WindowFacts(
                "pet", "mdp:left", "\\\\.\\DISPLAY3",
                new PhysicalRect(-2460, -100, 240, 260),
                192, 3, 1);

            string key;
            LogicalPoint local;

            Assert.IsTrue(PetPlacementPolicy.TryBuildPreferredPoint(
                facts, topology, null, out key, out local));

            Assert.AreEqual(50, local.X);
            Assert.AreEqual(50, local.Y);
        }

        [TestMethod]
        public void EphemeralSurface_DoesNotCreateDurablePreference()
        {
            DisplaySurfaceSnapshot surface =
                new DisplaySurfaceSnapshot(
                    "s", "\\\\.\\DISPLAY1",
                    new PhysicalRect(0, 0, 1920, 1080),
                    new PhysicalRect(0, 0, 1920, 1040),
                    true, 0,
                    new[]
                    {
                        new DisplayTargetIdentity(
                            "ephemeral:1:2", false, "", "", 0, 0, 0)
                    });

            DisplayTopologySnapshot topology =
                new DisplayTopologySnapshot(1, new[] { surface });

            WindowFacts facts = new WindowFacts(
                "pet", "", "\\\\.\\DISPLAY1",
                new PhysicalRect(100, 100, 192, 208),
                96, 1, 1);

            string key;
            LogicalPoint local;

            Assert.IsFalse(PetPlacementPolicy.TryBuildPreferredPoint(
                facts, topology, null, out key, out local));
        }

        [TestMethod]
        public void LocalProjection_UsesActualDpi()
        {
            DisplaySurfaceSnapshot surface = Surface(
                "s", "\\\\.\\DISPLAY1", "mdp:a",
                -1920, 0, 1920, 1080, true);

            PhysicalPoint point = PetPlacementPolicy.ProjectLocalPoint(
                new LogicalPoint { X = 100, Y = 50 },
                surface, 144);

            Assert.AreEqual(-1770, point.X);
            Assert.AreEqual(75, point.Y);
        }

        [TestMethod]
        public void DefaultAnchor_ScalesLogicalMargin()
        {
            PhysicalPoint point = PetPlacementPolicy.DefaultBottomRight(
                new PhysicalRect(0, 0, 1920, 1040),
                384, 416, 192);

            Assert.AreEqual(1488, point.X);
            Assert.AreEqual(576, point.Y);
        }
    }
}
