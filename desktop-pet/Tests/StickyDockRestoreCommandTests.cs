using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class StickyDockRestoreCommandTests
    {
        private static DisplayTopologySnapshot Topology()
        {
            return new DisplayTopologySnapshot(1, new[]
            {
                new DisplaySurfaceSnapshot("screen", "display",
                    new PhysicalRect(0, 0, 1920, 1080),
                    new PhysicalRect(0, 0, 1920, 1040), true, 0,
                    new[] { new DisplayTargetIdentity("mdp:screen", true,
                        "screen-path", "screen", 0, 0, 0) })
            });
        }

        private static DockGroupReprojectPlan Plan()
        {
            return new DockGroupReprojectPlan(1, 2, "screen",
                new DockGroupLogicalState(new LogicalPoint { X = 100, Y = 50 },
                    new[] { new DockLogicalMember("a", 300, 230),
                        new DockLogicalMember("b", 300, 230) }), false);
        }

        [TestMethod]
        public void ReprojectDockGroup_CanRequestShowAfterPlacement()
        {
            DockGroupReprojectPlan plan = Plan();
            DisplayTopologySnapshot topology = Topology();
            StickyUiCommand command = StickyUiCommand.ReprojectDockGroup(plan, topology, true);
            Assert.AreEqual(StickyUiCommandKind.ReprojectDockGroup, command.Kind);
            Assert.IsTrue(command.Flag);
            Assert.AreSame(plan, command.DockGroupReprojectPlan);
            Assert.AreSame(topology, command.Topology);
        }

        [TestMethod]
        public void ReprojectDockGroup_DefaultPreservesVisibility()
        {
            StickyUiCommand command = StickyUiCommand.ReprojectDockGroup(Plan(),
                Topology());
            Assert.IsFalse(command.Flag);
        }
    }
}
