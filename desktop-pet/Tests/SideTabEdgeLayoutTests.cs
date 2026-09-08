using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class SideTabEdgeLayoutTests
    {
        [TestMethod]
        public void CenteredPet_KeepsBalancedSplit()
        {
            Assert.AreEqual(3, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                5, new DockRect(800, 100, 200, 200),
                new DockRect(0, 0, 1920, 1080), 146, 30, 2));
        }

        [TestMethod]
        public void LeftEdgePet_MovesAllTabsRight()
        {
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, new DockRect(0, 100, 200, 200),
                new DockRect(0, 0, 1920, 1080), 146, 30, 2));
        }

        [TestMethod]
        public void RightEdgePet_MovesAllTabsLeft()
        {
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, new DockRect(1720, 100, 200, 200),
                new DockRect(0, 0, 1920, 1080), 146, 30, 2));
        }

        [TestMethod]
        public void NegativeOriginRightEdge_UsesLeftSide()
        {
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, new DockRect(-200, -100, 200, 200),
                new DockRect(-1920, -200, 1920, 1080), 146, 30, 2));
        }

        [TestMethod]
        public void HighDpiStripWidth_ParticipatesInDecision()
        {
            DockRect pet = new DockRect(160, 100, 200, 200);
            DockRect work = new DockRect(0, 0, 1920, 1080);
            Assert.AreEqual(2, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, 146, 30, 2));
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, pet, work, 292, 30, 4));
        }

        [TestMethod]
        public void BothSidesOverflow_ChoosesLowerOverflow()
        {
            Assert.AreEqual(0, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, new DockRect(20, 100, 200, 200),
                new DockRect(0, 0, 300, 400), 146, 30, 2));
            Assert.AreEqual(4, SideTabLayoutPolicy.CalculateEdgeAwareLeftCount(
                4, new DockRect(80, 100, 200, 200),
                new DockRect(0, 0, 300, 400), 146, 30, 2));
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
