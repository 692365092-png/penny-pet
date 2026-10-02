using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class CoreBehaviorTests
    {

        [TestMethod]
        public void DisplayGeometry_ConvertsAcrossMixedMonitorOrigins()
        {
            LogicalPoint localA = DisplayGeometry.PhysicalToLocal(
                100, 50, 0, 0, 1.0);
            Assert.AreEqual(100, localA.X);
            Assert.AreEqual(50, localA.Y);

            LogicalPoint localB = DisplayGeometry.PhysicalToLocal(
                2020, 100, 1920, 0, 2.0);
            Assert.AreEqual(50, localB.X);
            Assert.AreEqual(50, localB.Y);

            PhysicalPoint physicalNegative =
                DisplayGeometry.LocalToPhysical(-30, 40, -1920, 0, 2.0);
            Assert.AreEqual(-1980, physicalNegative.X);
            Assert.AreEqual(80, physicalNegative.Y);
        }

        [TestMethod]
        public void StickyPlacementMath_RoundTrip_100Percent()
        {
            LogicalRect placement = StickyPlacementMath.ToLocalRect(0, 0, 1.0,
                new PhysicalRect(100, 50, 320, 300));
            Assert.AreEqual(100, placement.X);
            Assert.AreEqual(50, placement.Y);
            Assert.AreEqual(320, placement.Width);
            Assert.AreEqual(300, placement.Height);

            LogicalPoint local = DisplayGeometry.PhysicalToLocal(
                100, 50, 0, 0, 1.0);
            PhysicalPoint round = DisplayGeometry.LocalToPhysical(
                local.X, local.Y, 0, 0, 1.0);
            Assert.AreEqual(100, round.X);
            Assert.AreEqual(50, round.Y);
        }

        [TestMethod]
        public void StickyPlacementMath_RoundTrip_200PercentNonZeroOrigin()
        {
            LogicalRect placement = StickyPlacementMath.ToLocalRect(1920, 0, 2.0,
                new PhysicalRect(2020, 100, 640, 600));
            Assert.AreEqual(50, placement.X);
            Assert.AreEqual(50, placement.Y);
            Assert.AreEqual(320, placement.Width);
            Assert.AreEqual(300, placement.Height);

            LogicalPoint local = DisplayGeometry.PhysicalToLocal(
                2020, 100, 1920, 0, 2.0);
            PhysicalPoint round = DisplayGeometry.LocalToPhysical(
                local.X, local.Y, 1920, 0, 2.0);
            Assert.AreEqual(2020, round.X);
            Assert.AreEqual(100, round.Y);
        }

        [TestMethod]
        public void StickyPlacementMath_RoundTrip_NegativeOrigin()
        {
            LogicalRect placement = StickyPlacementMath.ToLocalRect(-1920, 0, 2.0,
                new PhysicalRect(-2010, 80, 640, 600));
            Assert.AreEqual(-45, placement.X);
            Assert.AreEqual(40, placement.Y);
            Assert.AreEqual(320, placement.Width);
            Assert.AreEqual(300, placement.Height);

            LogicalPoint local = DisplayGeometry.PhysicalToLocal(
                -2010, 80, -1920, 0, 2.0);
            PhysicalPoint round = DisplayGeometry.LocalToPhysical(
                local.X, local.Y, -1920, 0, 2.0);
            Assert.AreEqual(-2010, round.X);
            Assert.AreEqual(80, round.Y);
        }

        [TestMethod]
        public void StickyPlacementMath_MoveAcrossDisplaysBuildsDistinctPreferredTargets()
        {
            WindowPlacementPreference before = StickyPlacementMath.PreferenceFromPhysicalRect(
                "mdp:one", 0, 0, 1.0, new PhysicalRect(100, 50, 320, 300));
            WindowPlacementPreference after = StickyPlacementMath.PreferenceFromPhysicalRect(
                "mdp:two", 1920, 0, 2.0, new PhysicalRect(2020, 100, 640, 600));
            Assert.AreNotEqual(before.PreferredTargetKey, after.PreferredTargetKey);
            Assert.AreEqual("mdp:two", after.PreferredTargetKey);
            Assert.AreEqual(50, after.LocalLogicalRect.X);
            Assert.AreEqual(50, after.LocalLogicalRect.Y);
        }

        [TestMethod]
        public void WindowFacts_ScaleUsesWindowDpiAndStaysImmutable()
        {
            WindowFacts facts = new WindowFacts("w", "mdp:x",
                "\\\\.\\DISPLAY1", new PhysicalRect(2020, 100, 640, 600),
                192, 7, 318);
            Assert.AreEqual(2.0, facts.Scale, 0.0001);
            WindowFacts baseFacts = new WindowFacts("w", String.Empty,
                String.Empty, new PhysicalRect(0, 0, 320, 300), 96, 0, 0);
            Assert.AreEqual(1.0, baseFacts.Scale, 0.0001);
            foreach (PropertyInfo property in
                typeof(WindowFacts).GetProperties())
                Assert.IsFalse(property.CanWrite,
                    property.Name + " must stay read-only.");
        }
    }
}
