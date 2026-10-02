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
        public void StickyPlacementRules_MigratesV10WhenDisplayResolves()
        {
            StickyNoteData note = new StickyNoteData
            {
                Id = "migrate",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY2",
                new LogicalRect { X = -150, Y = 40, Width = 320, Height = 300 })};
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(0,
                new[]
                {
                    new DisplaySurfaceSnapshot("surface-2",
                        "\\\\.\\DISPLAY2",
                        new PhysicalRect(1920, 0, 1920, 1080),
                        new PhysicalRect(1920, 0, 1920, 1040), false, 0,
                        new[]
                        {
                            new DisplayTargetIdentity("mdp:screen-b", true,
                                String.Empty, String.Empty, 0, 0, 0)
                        })
                });

            Assert.IsTrue(
                StickyPlacementRules.MigrateV10Preferred(note, topology));
            Assert.AreEqual("mdp:screen-b",
                note.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(-150, note.PreferredPlacement.LocalLogicalRect.X);
            Assert.AreEqual(40, note.PreferredPlacement.LocalLogicalRect.Y);
            Assert.AreEqual(320, note.PreferredPlacement.LocalLogicalRect.Width);
            Assert.AreEqual(300, note.PreferredPlacement.LocalLogicalRect.Height);
        }

        [TestMethod]
        public void StickyPlacementRules_DoesNotMigrateMissingDisplayOrExistingPreference()
        {
            DisplayTopologySnapshot topology = new DisplayTopologySnapshot(0,
                new[]
                {
                    new DisplaySurfaceSnapshot("surface-1",
                        "\\\\.\\DISPLAY1",
                        new PhysicalRect(0, 0, 1920, 1080),
                        new PhysicalRect(0, 0, 1920, 1040), true, 0,
                        new[]
                        {
                            new DisplayTargetIdentity("mdp:a", true,
                                String.Empty, String.Empty, 0, 0, 0)
                        })
                });

            StickyNoteData missing = new StickyNoteData
            {
                Id = "missing",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY9",
                new LogicalRect { X = 5, Y = 6, Width = 320, Height = 300 })};
            Assert.IsFalse(
                StickyPlacementRules.MigrateV10Preferred(missing, topology));
            Assert.IsNull(missing.PreferredPlacement);

            StickyNoteData existing = new StickyNoteData
            {
                Id = "existing",
                LegacyPlacement = new StickyLegacyPlacement("\\\\.\\DISPLAY1",
                new LogicalRect { X = 5, Y = 6, Width = 320, Height = 300 }),
                PreferredPlacement = new WindowPlacementPreference("mdp:keep",
                    new LogicalRect { X = 0, Y = 0, Width = 280, Height = 260 })
            };
            Assert.IsFalse(
                StickyPlacementRules.MigrateV10Preferred(existing, topology));
            Assert.AreEqual("mdp:keep",
                existing.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(280, existing.PreferredPlacement.LocalLogicalRect.Width);
        }

        [TestMethod]
        [DataRow((int)PlacementReason.UserMoveCommit, true)]
        [DataRow((int)PlacementReason.UserResizeCommit, true)]
        [DataRow((int)PlacementReason.Spawn, true)]
        [DataRow((int)PlacementReason.DockCommit, true)]
        [DataRow((int)PlacementReason.ExpandAndTile, true)]
        [DataRow((int)PlacementReason.Restore, false)]
        [DataRow((int)PlacementReason.TemporaryRehome, false)]
        [DataRow((int)PlacementReason.PreferredDisplayReturned, false)]
        [DataRow((int)PlacementReason.DockLiveFollower, false)]
        [DataRow((int)PlacementReason.Recovery, false)]
        public void StickyPlacementRules_OnlyUserReasonsCommitPreferred(int reason, bool accepted)
        {
            var previous = new WindowPlacementPreference("mdp:home",
                new LogicalRect { X = 100, Y = 200, Width = 320, Height = 300 });
            var next = new WindowPlacementPreference("mdp:other",
                new LogicalRect { X = 10, Y = 20, Width = 600, Height = 450 });
            var note = new StickyNoteData { PreferredPlacement = previous, X = 9000 };
            Assert.AreEqual(accepted, StickyPlacementRules.TryCommitPreferred(note, next, (PlacementReason)reason));
            Assert.AreSame(accepted ? next : previous, note.PreferredPlacement);
            Assert.AreEqual(9000, note.X);
        }

        [TestMethod]
        public void StickyNote_PersistenceCopyRetainsItsCompletePreferenceAcrossLaterMoves()
        {
            var local = new LogicalRect { X = -10, Y = 20, Width = 320, Height = 300 };
            var original = new WindowPlacementPreference("mdp:home", local);
            var note = new StickyNoteData { PreferredPlacement = original };
            var captured = note.CloneForPersistence();
            local.Width = 600;
            var detachedRect = original.LocalLogicalRect;
            detachedRect.Height = 900;
            note.PreferredPlacement = new WindowPlacementPreference("mdp:other", local);
            Assert.AreSame(original, captured.PreferredPlacement);
            var saved = StickyNoteCodec.ParseLine(StickyNoteCodec.SerializeLine(captured));
            Assert.AreEqual("mdp:home", saved.PreferredPlacement.PreferredTargetKey);
            Assert.AreEqual(-10, saved.PreferredPlacement.LocalLogicalRect.X);
            Assert.AreEqual(320, saved.PreferredPlacement.LocalLogicalRect.Width);
            Assert.AreEqual(300, saved.PreferredPlacement.LocalLogicalRect.Height);
            Assert.AreEqual(600, note.PreferredPlacement.LocalLogicalRect.Width);
        }
    }
}
