using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed partial class SideTabProjectionTests
    {
        [TestMethod]
        public void SideTabFontPixels_96DpiMatches85PointBaseline()
        {
            Assert.AreEqual(8.5F * 96F / 72F,
                SideTabPhysicalMetrics.ForDpi(96).FontPixels, 0.0001F);
        }

        [TestMethod]
        public void SideTabFontPixels_144DpiScalesOnce()
        {
            Assert.AreEqual(17F,
                SideTabPhysicalMetrics.ForDpi(144).FontPixels, 0.0001F);
        }

        [TestMethod]
        public void SideTabFontPixels_192DpiScalesOnce()
        {
            Assert.AreEqual(8.5F * 96F / 72F * 2F,
                SideTabPhysicalMetrics.ForDpi(192).FontPixels, 0.0001F);
        }

        [TestMethod]
        public void SideTabFontRebuildDoesNotCompoundAcrossRoundTrips()
        {
            for (int round = 0; round < 20; round++)
            foreach (int dpi in new[] { 96, 144, 192, 144, 96 })
                Assert.AreEqual(8.5F * dpi / 72F,
                    SideTabPhysicalMetrics.ForDpi(dpi).FontPixels, 0.0001F);
        }

        [TestMethod]
        [DataRow(120, 160, 38, 3, 13)]
        [DataRow(144, 192, 45, 3, 15)]
        public void FractionalMetrics_RoundFromLogicalReference(
            int dpi, int width, int height, int gap, int previewGap)
        {
            SideTabPhysicalMetrics metrics = SideTabPhysicalMetrics.ForDpi(dpi);
            Assert.AreEqual(width, metrics.Width);
            Assert.AreEqual(height, metrics.Height);
            Assert.AreEqual(gap, metrics.Gap);
            Assert.AreEqual(previewGap, metrics.PreviewInsertionGap);
        }



        [TestMethod]
        public void Metrics_96DpiMatchReference()
        {
            SideTabPhysicalMetrics m =
                SideTabPhysicalMetrics.ForDpi(96);

            Assert.AreEqual(128, m.Width);
            Assert.AreEqual(30, m.Height);
            Assert.AreEqual(2, m.Gap);
            Assert.AreEqual(10, m.PreviewInsertionGap);
            Assert.AreEqual(8, m.DragSourceVisualOffset);
            Assert.AreEqual(20, m.IconSize);
        }

        [TestMethod]
        public void Metrics_192DpiProjectExactlyOnce()
        {
            SideTabPhysicalMetrics m =
                SideTabPhysicalMetrics.ForDpi(192);

            Assert.AreEqual(256, m.Width);
            Assert.AreEqual(60, m.Height);
            Assert.AreEqual(4, m.Gap);
            Assert.AreEqual(20, m.PreviewInsertionGap);
            Assert.AreEqual(16, m.DragSourceVisualOffset);
            Assert.AreEqual(40, m.IconSize);
        }

        [TestMethod]
        public void SameLogicalWorkHeightHasSameCapacity()
        {
            int a = SideTabLayoutPolicy.PhysicalWorkHeightToLogical(
                1040, 96);

            int b = SideTabLayoutPolicy.PhysicalWorkHeightToLogical(
                2080, 192);

            Assert.AreEqual(a, b);

            Assert.AreEqual(
                SideTabLayoutPolicy.LogicalScreenCapacity(a),
                SideTabLayoutPolicy.LogicalScreenCapacity(b));
        }

        [TestMethod]
        public void BalancedSplitIsDpiIndependent()
        {
            int left =
                SideTabLayoutPolicy.CalculateBalancedLeftCount(11);

            Assert.AreEqual(6, left);
            Assert.AreEqual(5, 11 - left);
        }

        [TestMethod]
        public void LocationSupportsNegativeOrigin()
        {
            SideTabPhysicalMetrics m =
                SideTabPhysicalMetrics.ForDpi(192);

            DockPoint p =
                StickyDockGeometry.CalculateSideTabLocation(
                    new DockRect(-1800, 100, 384, 416),
                    new DockRect(-1920, 0, 1920, 2080),
                    new DockSize(m.Width,
                        m.Height * 3 + m.Gap * 2),
                    true, 80, 0,
                    m.WindowMarginX, m.WindowMarginY);

            Assert.IsTrue(
                p.X >= -1920 + m.WindowMarginX);
        }
    }
}
