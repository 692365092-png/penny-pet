using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockDividerSettlementTests
    {
        [TestMethod]
        public void FinalTargets_ReanchorToFinalSourceRectAfterTopDrift()
        {
            // Start: the source is partially off-screen (top=-170, h=452) and
            // the followers are seamed below it. During the gesture the source
            // top drifts to 0 while its height settles at 330 physical px.
            List<DockRect> start = new List<DockRect>
            {
                new DockRect(2133, -170, 480, 452),
                new DockRect(2133, 282, 480, 450),
                new DockRect(2133, 732, 480, 450),
            };
            List<DockRect> targets =
                StickyDockGeometry.CalculateDockDividerFinalTargets(
                    start, 0, 0, 330);
            Assert.AreEqual(2, targets.Count);
            Assert.AreEqual(2133, targets[0].Left);
            Assert.AreEqual(480, targets[0].Width);
            Assert.AreEqual(450, targets[0].Height);
            Assert.AreEqual(330, targets[0].Top);
            Assert.AreEqual(780, targets[1].Top);
            Assert.IsTrue(targets[0].Top >= 330);
        }

        [TestMethod]
        public void FinalTargets_NoFollowersWhenSourceIsLast()
        {
            List<DockRect> start = new List<DockRect>
            {
                new DockRect(0, 0, 400, 300),
                new DockRect(0, 300, 400, 300),
            };
            Assert.AreEqual(0,
                StickyDockGeometry.CalculateDockDividerFinalTargets(
                    start, 1, 0, 500).Count);
        }

        [TestMethod]
        public void SeamVerify_AllowsTwoPixelNativeTolerance()
        {
            List<PhysicalRect> rects = new List<PhysicalRect>
            {
                new PhysicalRect(10, 332, 400, 300),
                new PhysicalRect(10, 632, 400, 300),
            };
            Assert.IsTrue(StickyDockGeometry.DividerStackSeamIsExact(
                0, 330, rects, 2));
            Assert.IsFalse(StickyDockGeometry.DividerStackSeamIsExact(
                0, 330, new List<PhysicalRect>
                {
                    new PhysicalRect(10, 333, 400, 300),
                }, 2));
        }

        [TestMethod]
        public void SeamVerify_RejectsGapAndInvalidRects()
        {
            Assert.IsFalse(StickyDockGeometry.DividerStackSeamIsExact(
                0, 330, new List<PhysicalRect>
                {
                    new PhysicalRect(10, 350, 400, 300),
                }, 2));
            Assert.IsFalse(StickyDockGeometry.DividerStackSeamIsExact(
                0, 330, new List<PhysicalRect>
                {
                    new PhysicalRect(10, 330, 0, 300),
                }, 2));
        }
    }
}
