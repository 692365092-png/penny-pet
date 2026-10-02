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
        public void StickyImportMergePlanner_AddsNewNotesByStableId()
        {
            List<StickyNoteData> backup = new List<StickyNoteData>
            {
                new StickyNoteData { Id = "A", Text = "a" },
                new StickyNoteData { Id = "B", Text = "b" },
                new StickyNoteData { Id = "C", Text = "c" }
            };

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(null, backup);

            Assert.AreEqual(3, result.AddedCount);
            Assert.AreEqual(0, result.SkippedIdenticalCount);
            Assert.AreEqual(3, result.MergedSnapshot.Count);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" },
                result.MergedSnapshot.Select(note => note.Id).ToArray());
            Assert.AreNotSame(backup[0], result.MergedSnapshot[0]);
            Assert.IsFalse(result.MergedSnapshot[0].Visible);
        }

        [TestMethod]
        public void StickyImportMergePlanner_SkipsCanonicalIdenticalNote()
        {
            StickyNoteData current = new StickyNoteData
            {
                Id = "same",
                Text = "body",
                X = 44,
                Height = 310
            };
            StickyNoteData backup = current.CloneForPersistence();

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(
                    new[] { current }, new[] { backup });

            Assert.AreEqual(0, result.AddedCount);
            Assert.AreEqual(1, result.SkippedIdenticalCount);
            Assert.AreEqual(1, result.MergedSnapshot.Count);
            Assert.AreEqual(44, current.X);
            Assert.AreEqual(310, current.Height);
        }

        [TestMethod]
        public void StickyImportMergePlanner_PreservesCurrentVisibilityOnSameContent()
        {
            StickyNoteData current = new StickyNoteData
            {
                Id = "visibility",
                Text = "same",
                Visible = true
            };
            StickyNoteData backup = current.CloneForPersistence();
            backup.Visible = false;

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(
                    new[] { current }, new[] { backup });

            Assert.AreEqual(0, result.AddedCount);
            Assert.AreEqual(1, result.SkippedIdenticalCount);
            Assert.IsTrue(result.MergedSnapshot[0].Visible);
        }

        [TestMethod]
        public void StickyImportMergePlanner_SeparatesSpatialChangesAndAppendsTabs()
        {
            StickyNoteData currentA = new StickyNoteData
            {
                Id = "current-a", Text = "same", X = 900, Y = 500,
                TabOrder = 0, ReminderUtcTicks = 123
            };
            StickyNoteData currentB = new StickyNoteData
            {
                Id = "current-b", Text = "keep", TabOrder = 1
            };
            StickyNoteData backupA = currentA.CloneForPersistence();
            backupA.X = 10;
            backupA.Y = 20;
            backupA.TabOrder = 0;
            StickyNoteData backupX = new StickyNoteData
            {
                Id = "import-x", Text = "x", TabOrder = 0,
                CreatedUtcTicks = 20, ReminderUtcTicks = 999
            };
            StickyNoteData backupY = new StickyNoteData
            {
                Id = "import-y", Text = "y", TabOrder = 1,
                CreatedUtcTicks = 10, ReminderUtcTicks = 999
            };

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(
                    new[] { currentA, currentB },
                    new[] { backupA, backupX, backupY });

            Assert.AreEqual(2, result.AddedCount);
            Assert.AreEqual(1, result.SkippedIdenticalCount);
            Assert.AreEqual(0, result.ConflictCount);
            Assert.AreEqual(900, Find(result.MergedSnapshot, "current-a").X);
            Assert.AreEqual(500, Find(result.MergedSnapshot, "current-a").Y);
            Assert.AreEqual(0, Find(result.MergedSnapshot, "current-a").TabOrder);
            Assert.AreEqual(1, Find(result.MergedSnapshot, "current-b").TabOrder);
            Assert.AreEqual(2, Find(result.MergedSnapshot, "import-x").TabOrder);
            Assert.AreEqual(3, Find(result.MergedSnapshot, "import-y").TabOrder);
            Assert.AreEqual(0, Find(result.MergedSnapshot, "import-x").ReminderUtcTicks);
            Assert.AreEqual(0, Find(result.MergedSnapshot, "import-y").ReminderUtcTicks);
        }

        [TestMethod]
        public void StickyImportMergePlanner_DetectsLogicalDivergenceOnly()
        {
            StickyNoteData current = new StickyNoteData
            {
                Id = "logical", Text = "current", X = 900, Y = 500
            };
            StickyNoteData backup = current.CloneForPersistence();
            backup.Text = "imported";
            backup.X = 10;
            backup.Y = 20;

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(new[] { current },
                    new[] { backup });

            Assert.AreEqual(1, result.ConflictCount);
            Assert.AreEqual("current", Find(result.MergedSnapshot,
                "logical").Text);
            StickyNoteData copy = result.MergedSnapshot.Single(
                note => !String.Equals(note.Id, "logical",
                    StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("imported", copy.Text);
            Assert.AreEqual(0, copy.ReminderUtcTicks);
        }

        [TestMethod]
        public void StickyImportMergePlanner_PreservesDivergentVersionAndIsIdempotent()
        {
            StickyNoteData current = new StickyNoteData
            {
                Id = "same",
                Text = "current",
                X = 101
            };
            StickyNoteData backup = new StickyNoteData
            {
                Id = "same",
                Text = "imported",
                X = 202
            };
            List<StickyNoteData> currentSnapshot = new List<StickyNoteData>
            {
                current
            };

            StickyImportMergeResult first =
                StickyImportMergePlanner.Calculate(currentSnapshot,
                    new[] { backup });
            Assert.AreEqual(1, first.AddedCount);
            Assert.AreEqual(1, first.ConflictCount);
            Assert.AreEqual(2, first.MergedSnapshot.Count);
            Assert.AreEqual("current", Find(first.MergedSnapshot, "same").Text);
            StickyNoteData firstCopy = first.MergedSnapshot.Single(
                note => !String.Equals(note.Id, "same",
                    StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("imported", firstCopy.Text);
            Assert.IsFalse(firstCopy.Visible);

            StickyImportMergeResult second =
                StickyImportMergePlanner.Calculate(first.MergedSnapshot,
                    new[] { backup });
            StickyImportMergeResult third =
                StickyImportMergePlanner.Calculate(second.MergedSnapshot,
                    new[] { backup });
            Assert.AreEqual(2, second.MergedSnapshot.Count);
            Assert.AreEqual(2, third.MergedSnapshot.Count);
            Assert.IsFalse(second.Actions[0].Added);
            Assert.IsFalse(third.Actions[0].Added);
            Assert.AreEqual(firstCopy.Id,
                second.MergedSnapshot.Single(note => note.Text == "imported").Id);
            Assert.AreEqual("current", current.Text);
        }

        [TestMethod]
        public void StickyImportMergePlanner_RetainsCompleteMixedDockGroup()
        {
            StickyNoteData ordinary = new StickyNoteData
            {
                Id = "ordinary", X = 10, Y = 20, Width = 420, Height = 230
            };
            StickyNoteData todo = new StickyNoteData
            {
                Id = "todo", IsTodoList = true, X = 10, Y = 250,
                Width = 420, Height = 280
            };
            StickyNoteData schedule = new StickyNoteData
            {
                Id = "schedule", IsSchedule = true, X = 10, Y = 530,
                Width = 420, Height = 360
            };
            List<StickyNoteData> backup = new List<StickyNoteData>
            {
                ordinary, todo, schedule
            };
            StickyDockGroups.ApplyOrderedGroup(backup);

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(null, backup);

            Assert.AreEqual(3, result.AddedCount);
            List<StickyNoteData> ordered = result.MergedSnapshot.OrderBy(
                note => note.DockGroupOrder).ToList();
            Assert.AreEqual(ordinary.Id, ordered[0].DockGroupId);
            Assert.AreEqual(ordinary.Id, ordered[1].DockGroupId);
            Assert.AreEqual(ordinary.Id, ordered[2].DockGroupId);
            Assert.AreEqual(0, ordered[0].DockGroupOrder);
            Assert.AreEqual(1, ordered[1].DockGroupOrder);
            Assert.AreEqual(2, ordered[2].DockGroupOrder);
            Assert.AreEqual(420, ordered[1].Width);
            Assert.AreEqual(280, ordered[1].Height);
            Assert.IsTrue(ordered[1].IsTodoList);
            Assert.IsTrue(ordered[2].IsSchedule);
        }

        [TestMethod]
        public void StickyImportMergePlanner_DetachesPartialGroupWithoutMovingCurrent()
        {
            StickyNoteData currentA = new StickyNoteData
            {
                Id = "A", X = 900, Y = 700, Height = 410,
                DockGroupId = "current-group", DockGroupOrder = 0
            };
            StickyNoteData backupA = new StickyNoteData
            {
                Id = "A", X = 10, Y = 20, Height = 230
            };
            StickyNoteData backupB = new StickyNoteData
            {
                Id = "B", X = 10, Y = 250, Height = 250
            };
            StickyNoteData backupC = new StickyNoteData
            {
                Id = "C", X = 10, Y = 500, Height = 300
            };
            List<StickyNoteData> backup = new List<StickyNoteData>
            {
                backupA, backupB, backupC
            };
            StickyDockGroups.ApplyOrderedGroup(backup);

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(
                    new[] { currentA }, backup);

            StickyNoteData resultA = Find(result.MergedSnapshot, "A");
            StickyNoteData resultB = Find(result.MergedSnapshot, "B");
            StickyNoteData resultC = Find(result.MergedSnapshot, "C");
            Assert.AreEqual(900, resultA.X);
            Assert.AreEqual(700, resultA.Y);
            Assert.AreEqual(410, resultA.Height);
            Assert.AreEqual("current-group", resultA.DockGroupId);
            Assert.AreEqual(String.Empty, resultB.DockGroupId);
            Assert.AreEqual(String.Empty, resultC.DockGroupId);

            StickyImportMergeResult repeated =
                StickyImportMergePlanner.Calculate(result.MergedSnapshot,
                    backup);
            Assert.AreEqual(result.MergedSnapshot.Count,
                repeated.MergedSnapshot.Count);
        }

        [TestMethod]
        public void StickyImportMergePlanner_CurrentVisibilityWinsOnConflict()
        {
            StickyNoteData current = new StickyNoteData
            {
                Id = "visible", Visible = true, Text = "current"
            };
            StickyNoteData backup = current.CloneForPersistence();
            backup.Visible = false;
            backup.Text = "old";

            StickyImportMergeResult result =
                StickyImportMergePlanner.Calculate(
                    new[] { current }, new[] { backup });

            Assert.IsTrue(Find(result.MergedSnapshot, "visible").Visible);
            Assert.AreEqual(2, result.MergedSnapshot.Count);
            Assert.IsFalse(result.MergedSnapshot.Single(
                note => !String.Equals(note.Id, "visible",
                    StringComparison.OrdinalIgnoreCase)).Visible);
        }

        private static StickyNoteData Find(IList<StickyNoteData> notes,
            string id)
        {
            return notes.Single(note => String.Equals(note.Id, id,
                StringComparison.OrdinalIgnoreCase));
        }
    }
}
