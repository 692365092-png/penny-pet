using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    public sealed partial class DockMixedDpiResizeTests
    {
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

    }
}
