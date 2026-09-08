using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    // Physical-pixel contract for the hosted Dock resize path. These tests
    // freeze the PC-0.5 BUG-A fix until the future PC-6 Dock single-owner
    // rework may retire them.
    [TestClass]
    public sealed class DockMixedDpiResizeTests
    {
        [TestMethod]
        public void HorizontalResize_150Percent_KeepsPhysicalWidth()
        {
            DockRect result =
                StickyDockGeometry.CalculatePhysicalHorizontalResizeTarget(
                    800, 2000, false, 420, 1350);

            Assert.AreEqual(800, result.Left);
            Assert.AreEqual(1200, result.Width);
        }

        [TestMethod]
        public void HorizontalResize_FromLeft_PreservesPhysicalRightEdge()
        {
            DockRect result =
                StickyDockGeometry.CalculatePhysicalHorizontalResizeTarget(
                    600, 2000, true, 420, 1350);

            Assert.AreEqual(650, result.Left);
            Assert.AreEqual(1350, result.Width);
            Assert.AreEqual(2000, result.Left + result.Width);
        }

        [TestMethod]
        public void HorizontalResize_200Percent_UsesScaledMinimum()
        {
            DockRect result =
                StickyDockGeometry.CalculatePhysicalHorizontalResizeTarget(
                    100, 300, false, 560, 1800);

            Assert.AreEqual(560, result.Width);
        }

        [TestMethod]
        public void DividerResize_UsesExactPhysicalHeight()
        {
            List<DockRect> start = new List<DockRect>
            {
                new DockRect(100, 100, 630, 330),
                new DockRect(100, 430, 630, 450)
            };

            List<DockRect> result =
                StickyDockGeometry.CalculateDockMemberResizeTargetsExact(
                    start, 0, 450);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(550, result[0].Top);
            Assert.AreEqual(450, result[0].Height);
        }

        [TestMethod]
        public void GroupedHorizontalWmSizing_EmitsNativePhysicalRect()
        {
            string source = ReadSource(
                "Features/StickyNotes/StickyNativeWindowBehavior.cs");
            int start = source.IndexOf(
                "message == WmSizing && _dockGrouped",
                StringComparison.Ordinal);
            Assert.IsTrue(start >= 0);
            string block = source.Substring(start,
                Math.Min(3000, source.Length - start));

            Assert.IsTrue(block.Contains(
                "CalculatePhysicalHorizontalResizeTarget("));
            Assert.IsTrue(block.Contains(
                "new DockHorizontalResizeEventArgs("));
            Assert.IsFalse(block.Contains(
                "(sizing.Right - sizing.Left) / scale"),
                "Grouped horizontal WM_SIZING must not divide native pixels by scale.");
            Assert.IsFalse(block.Contains(
                "_resizeStartLeft + _resizeStartWidth"),
                "The DIP-based left-edge reconstruction must not remain.");
        }

        [TestMethod]
        public void PetGroupResize_DoesNotReclampPhysicalWidth()
        {
            string source = ReadSource(
                "Features/StickyNotes/PetStickyDockCoordinator.cs");
            string method = SliceMethod(source,
                "private void ResizeStickyDockGroup(");
            Assert.IsFalse(method.Contains("Math.Max(280") ||
                method.Contains("Math.Min(900"),
                "The Pet must not apply a second 280..900 logical clamp.");
        }
    }
}
