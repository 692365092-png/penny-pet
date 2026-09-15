using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PennyPet.Tests.StickySessionTopologyContractTests;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class DockTopologyReprojectPolicyTests
    {

        [TestMethod]
        [TestCategory("ArchitectureSourceBoundary")]
        public void ReprojectReasons_OwnTheirStateTransitions()
        {
            string source = SourceGuardText.ReadStickyWorkflowSource();
            string reconcile = SliceMethod(source, "private void ReconcileDockGroup(");
            string temporary = SliceMethod(reconcile, "if (temporary)");
            Assert.IsTrue(temporary.Contains("UserMovedSinceRehome("));
            Assert.IsTrue(temporary.Contains("DockTopologyReprojectReason.PreferredReturn"));
            Assert.IsFalse(temporary.Contains("DockTopologyReprojectReason.CurrentRuntimeRepair"));
            string apply = SliceMethod(source, "private void PostDockGroupTopologyReproject(");
            string returned = SliceMethod(apply,
                "if (reason == DockTopologyReprojectReason.PreferredReturn)");
            string rehomed = SliceMethod(apply,
                "if (reason == DockTopologyReprojectReason.TemporaryRehome)");
            Assert.IsTrue(returned.Contains("MarkReturnedToPreferred("));
            Assert.IsTrue(rehomed.Contains("MarkTemporaryRehome("));
            string runtimeRepair = apply.Replace(returned, "").Replace(rehomed, "");
            Assert.IsFalse(runtimeRepair.Contains("MarkReturnedToPreferred("));
            Assert.IsFalse(runtimeRepair.Contains("MarkTemporaryRehome("));
            Assert.IsFalse(apply.Contains("TryCommitPreferred("));
            Assert.IsFalse(apply.Contains("MarkUserPlacementCommit("));
            Assert.IsTrue(apply.Contains("bool centerInWorkArea = reason == DockTopologyReprojectReason.TemporaryRehome"));
        }
    }
}
