using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    public sealed partial class PetDisplayPlacementTests
    {
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

        [TestMethod]
        public void OnDpiChanged_ActiveDragRebasesWithoutCommittingPreference()
        {
            // Temporary PC-0.5 runtime contract, eligible for retirement
            // after the future PetDisplayRuntime object extraction.
            string form = StickySessionTopologyContractTests.ReadSource(
                "PetForm.cs");
            string dpi = StickySessionTopologyContractTests.SliceMethod(
                form, "protected override void OnDpiChanged");

            Assert.IsTrue(dpi.Contains("ActualPetDpi(e.DeviceDpiNew)"));
            Assert.IsTrue(dpi.Contains(
                "RebaseActiveDragTopLeft("));
            Assert.IsTrue(dpi.Contains(
                "_dragMouseOrigin ="));
            Assert.IsTrue(dpi.Contains(
                "_dragWindowOrigin ="));
            Assert.IsTrue(dpi.Contains(
                "_petDpiDragHandoffActive"));
            Assert.IsFalse(dpi.Contains(
                "CommitPetPreferredFromFacts("));
            Assert.IsFalse(dpi.Contains(
                "_settings.PetPreferredTargetKey ="));
        }
    }
}
