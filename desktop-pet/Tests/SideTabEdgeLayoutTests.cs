using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class SideTabEdgeLayoutTests
    {
        [TestMethod]
        public void CenteredPet_KeepsBalancedSplit()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            DockRect pet = new DockRect(800, 100, 200, 200);
            DockRect work = new DockRect(0, 0, 1920, 1080);
            int overlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, metrics);
            Assert.AreEqual(3, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                5, pet, work, metrics.Width, overlap,
                metrics.WindowMarginX));
        }

        [TestMethod]
        public void LeftEdgePet_MovesAllTabsRight()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            DockRect pet = new DockRect(0, 100, 200, 200);
            DockRect work = new DockRect(0, 0, 1920, 1080);
            int overlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, metrics);
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, metrics.Width, overlap,
                metrics.WindowMarginX));
        }

        [TestMethod]
        public void RightEdgePet_MovesAllTabsLeft()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            DockRect pet = new DockRect(1720, 100, 200, 200);
            DockRect work = new DockRect(0, 0, 1920, 1080);
            int overlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, metrics);
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, metrics.Width, overlap,
                metrics.WindowMarginX));
        }

        [TestMethod]
        public void NegativeOriginRightEdge_UsesLeftSide()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            DockRect pet = new DockRect(-200, -100, 200, 200);
            DockRect work = new DockRect(-1920, -200, 1920, 1080);
            int overlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, metrics);
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, metrics.Width, overlap,
                metrics.WindowMarginX));
        }

        [TestMethod]
        public void HighDpiStripWidth_ParticipatesInDecision()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            SideTabPhysicalMetrics highDpi =
                SideTabPhysicalMetrics.ForDpi(192);
            DockRect pet = new DockRect(160, 100, 200, 200);
            DockRect work = new DockRect(0, 0, 1920, 1080);
            int overlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, metrics);
            int highOverlap =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    pet.Width, highDpi);
            Assert.AreEqual(2, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, metrics.Width, overlap,
                metrics.WindowMarginX));
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, highDpi.Width, highOverlap,
                highDpi.WindowMarginX));
        }

        [TestMethod]
        public void BothSidesOverflow_ChoosesLowerOverflow()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);
            DockRect work = new DockRect(0, 0, 300, 400);
            DockRect petA = new DockRect(20, 100, 200, 200);
            DockRect petB = new DockRect(80, 100, 200, 200);
            int overlapA =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    petA.Width, metrics);
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, petA, work, metrics.Width, overlapA,
                metrics.WindowMarginX));
            int overlapB =
                SideTabLayoutPolicy.CalculatePhysicalOverlap(
                    petB.Width, metrics);
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, petB, work, metrics.Width, overlapB,
                metrics.WindowMarginX));
        }

        [TestMethod]
        public void PhysicalOverlap_ProjectsGapOnceWithPetPadding()
        {
            Assert.AreEqual(32, SideTabLayoutPolicy.CalculatePhysicalOverlap(
                192, SideTabPhysicalMetrics.ForDpi(96)));
            Assert.AreEqual(64, SideTabLayoutPolicy.CalculatePhysicalOverlap(
                384, SideTabPhysicalMetrics.ForDpi(192)));
        }
    }
}
