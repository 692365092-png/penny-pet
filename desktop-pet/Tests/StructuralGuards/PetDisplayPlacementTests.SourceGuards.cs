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
            Assert.IsTrue(dpi.Contains("_petDisplay.MoveForDpiHandoff("));
            Assert.IsTrue(runtime.Contains("NativeDisplayConfig.GetDpiForWindow(Handle)"));
            Assert.IsFalse(form.Contains("Screen.PrimaryScreen"));
            string compatibility = StickySessionTopologyContractTests.SliceMethod(
                form, "private void SaveLocation()");
            Assert.IsFalse(compatibility.Contains("PetPreferredTargetKey ="));
            string reconcile = StickySessionTopologyContractTests.SliceMethod(
                StickySessionTopologyContractTests.ReadSource(
                    "Features/Display/PetDisplayRuntime.cs"), "internal void Reconcile");
            Assert.IsFalse(reconcile.Contains("CommitPetPreferredFromFacts("));
            Assert.IsTrue(reconcile.Contains("if (_window.IsUserDragging)"));
            Assert.IsTrue(reconcile.Contains("_userMovedSinceTemporaryRehome"));
        }

        [TestMethod]
        public void OnDpiChanged_ActiveDragRebasesWithoutCommittingPreference()
        {
            // Native DPI ordering stays in the owning window adapter.
            string form = StickySessionTopologyContractTests.ReadSource(
                "PetForm.cs");
            string dpi = StickySessionTopologyContractTests.SliceMethod(
                form, "protected override void OnDpiChanged");

            Assert.IsTrue(dpi.Contains("ActualPetDpi(e.DeviceDpiNew)"));
            Assert.IsTrue(dpi.Contains("_petDisplay.MoveForDpiHandoff("));
            Assert.IsTrue(dpi.Contains(
                "RebaseActiveDragTopLeft("));
            Assert.IsTrue(dpi.Contains("_interaction.RebasePointer(new Point("));
            Assert.IsTrue(dpi.Contains(
                "_petDpiDragHandoffActive"));
            Assert.IsFalse(dpi.Contains(
                "CommitPetPreferredFromFacts("));
            Assert.IsFalse(dpi.Contains(
                "_settings.PetPreferredTargetKey ="));
        }
    }
}
