using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class SideTabCompactMetricsTests
    {
        [TestMethod]
        public void CompactMetrics_96DpiMatchLogicalBaseline()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(96);

            Assert.AreEqual(128, metrics.Width);
            Assert.AreEqual(30, metrics.Height);
            Assert.AreEqual(2, metrics.Gap);
            Assert.AreEqual(20, metrics.IconSize);
            Assert.AreEqual(7, metrics.IconMargin);
            Assert.AreEqual(10, metrics.PreviewInsertionGap);
            Assert.AreEqual(8, metrics.DragSourceVisualOffset);
        }

        [TestMethod]
        public void CompactMetrics_120DpiScaleExactlyOnce()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(120);

            Assert.AreEqual(160, metrics.Width);
            Assert.AreEqual(38, metrics.Height);
            Assert.AreEqual(25, metrics.IconSize);
            Assert.AreEqual(9, metrics.IconMargin);
            Assert.AreEqual(13, metrics.PreviewInsertionGap);
            Assert.AreEqual(10, metrics.DragSourceVisualOffset);
        }

        [TestMethod]
        public void CompactMetrics_144DpiScaleExactlyOnce()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(144);

            Assert.AreEqual(192, metrics.Width);
            Assert.AreEqual(45, metrics.Height);
            Assert.AreEqual(30, metrics.IconSize);
            Assert.AreEqual(11, metrics.IconMargin);
            Assert.AreEqual(15, metrics.PreviewInsertionGap);
            Assert.AreEqual(12, metrics.DragSourceVisualOffset);
        }

        [TestMethod]
        public void CompactMetrics_192DpiScaleExactlyOnce()
        {
            SideTabPhysicalMetrics metrics =
                SideTabPhysicalMetrics.ForDpi(192);

            Assert.AreEqual(256, metrics.Width);
            Assert.AreEqual(60, metrics.Height);
            Assert.AreEqual(40, metrics.IconSize);
            Assert.AreEqual(14, metrics.IconMargin);
            Assert.AreEqual(20, metrics.PreviewInsertionGap);
            Assert.AreEqual(16, metrics.DragSourceVisualOffset);
        }

        [TestMethod]
        public void CompactMetrics_FontBaselineRemains85Point()
        {
            SideTabPhysicalMetrics dpi96 =
                SideTabPhysicalMetrics.ForDpi(96);
            SideTabPhysicalMetrics dpi192 =
                SideTabPhysicalMetrics.ForDpi(192);

            Assert.AreEqual(
                8.5F * 96F / 72F,
                dpi96.FontPixels,
                0.01F);
            Assert.AreEqual(
                8.5F * 192F / 72F,
                dpi192.FontPixels,
                0.01F);
            Assert.AreEqual(
                dpi96.FontPixels * 2F,
                dpi192.FontPixels,
                0.01F);
        }
    }
}
