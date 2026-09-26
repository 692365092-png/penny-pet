using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class SideTabProjectionTests
    {
        [TestMethod]
        public void DerivedPlacementContract_DoesNotMovePetOrUseAnotherDpiOwner()
        {
            string coordinator = SourceGuardText.ReadStickyWorkflowSource();
            string context = StickySessionTopologyContractTests.SliceMethod(
                coordinator, "private bool TryGetPetDerivedDisplayContext");
            Assert.IsTrue(context.Contains("CapturePetWindowFacts(topology)"));
            Assert.IsTrue(context.Contains("FindByRuntimeGdiName("));
            Assert.IsTrue(context.Contains("SideTabPhysicalMetrics.ForDpi(petFacts.Dpi)"));
            string position = StickySessionTopologyContractTests.SliceMethod(
                coordinator, "internal void PositionNoteTabs()");
            Assert.IsTrue(position.Contains("TryGetPetDerivedDisplayContext("));
            Assert.IsTrue(position.Contains(
                "Host.UpdateSideTabs(new StickySideTabsProjection("));
            Assert.IsFalse(position.Contains("Location ="));
            Assert.IsFalse(position.Contains("Screen."));

            string host = StickySessionTopologyContractTests.ReadSource(
                "StickyUiHost.cs");
            string apply = StickySessionTopologyContractTests.SliceMethod(
                host, "private void ApplySideTabsProjection(");
            Assert.IsTrue(apply.Contains(
                "_leftNoteTabs.ApplyPhysicalMetrics(metrics)") &&
                apply.Contains("_rightNoteTabs.ApplyPhysicalMetrics(metrics)") &&
                apply.Contains("ShowNear("));
            Assert.IsFalse(apply.Contains("Screen."));
            Assert.IsTrue(host.Contains(
                "value.Kind == StickyUiEventKind.HeaderDragMoved") &&
                host.Contains("ApplySideTabZOrder();"),
                "Sticky STA must update tab overlap directly from live Sticky events.");

            string tabs = StickySessionTopologyContractTests.ReadSource(
                "Features/StickyNotes/StickyNoteTabs.cs");
            Assert.IsTrue(tabs.Contains("AutoScaleMode = AutoScaleMode.None"));
            Assert.IsFalse(tabs.Contains("AutoScaleMode = AutoScaleMode.Dpi"));
            foreach (string method in new[] { "private void TabsDragOver",
                "private void LayoutAnimationTick", "private void ShowBoundaryRollover",
                "private void ApplySourceHorizontalOffset" })
            {
                string body = StickySessionTopologyContractTests.SliceMethod(tabs, method);
                Assert.IsTrue(body.Contains("_metrics"), method);
                Assert.IsFalse(body.Contains("(TabHeight + TabGap)"), method);
            }
        }
    }
}
