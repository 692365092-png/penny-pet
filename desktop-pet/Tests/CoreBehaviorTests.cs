using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PennyPet.Tests
{
    [TestClass]
    public sealed class CoreBehaviorTests
    {
        [TestMethod]
        public void SettingRules_NormalizePersistedValuesWithoutUiTypes()
        {
            Assert.AreEqual(50, PetSettingRules.NormalizePetScalePercent(47));
            Assert.AreEqual(100, PetSettingRules.NormalizePetScalePercent(104));
            Assert.AreEqual(160, PetSettingRules.NormalizePetScalePercent(156));
            Assert.AreEqual(200, PetSettingRules.NormalizePetScalePercent(207));

            Assert.AreEqual(60,
                PetSettingRules.NormalizeKeyboardTextScalePercent(55));
            Assert.AreEqual(100,
                PetSettingRules.NormalizeKeyboardTextScalePercent(100));
            Assert.AreEqual(150,
                PetSettingRules.NormalizeKeyboardTextScalePercent(140));
        }

        [TestMethod]
        public void StickyNoteWindowRules_KeepTabsTopMostWithOrWithoutVisibleNotes()
        {
            Assert.IsTrue(
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(false));
            Assert.IsTrue(
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(true));
        }

        [TestMethod]
        public void StickyNoteData_CloneForPersistenceCopiesNestedState()
        {
            StickyNoteData source = new StickyNoteData();
            source.Title = "source";
            source.Text = "body";
            source.IsTodoList = true;
            source.TodoItems.Add(new StickyTodoItem("todo",
                StickyTodoState.InProgress, true));
            source.IsSchedule = true;
            source.ScheduleItems.Add(new StickyScheduleItem("schedule",
                new DateTime(2026, 8, 28), true));

            StickyNoteData copy = source.CloneForPersistence();

            Assert.AreEqual(source.Id, copy.Id);
            Assert.AreEqual(source.Title, copy.Title);
            Assert.AreEqual(source.Text, copy.Text);
            Assert.AreEqual(1, copy.TodoItems.Count);
            Assert.AreEqual(StickyTodoState.InProgress,
                copy.TodoItems[0].State);
            Assert.IsTrue(copy.TodoItems[0].IsPinned);
            Assert.AreEqual(1, copy.ScheduleItems.Count);
            Assert.AreEqual(new DateTime(2026, 8, 28),
                copy.ScheduleItems[0].TargetDate);

            copy.TodoItems[0].Text = "changed";
            copy.ScheduleItems[0].Text = "changed";
            Assert.AreEqual("todo", source.TodoItems[0].Text);
            Assert.AreEqual("schedule", source.ScheduleItems[0].Text);
        }

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
            Assert.AreEqual(String.Empty, resultB.DockParentId);
            Assert.AreEqual(String.Empty, resultC.DockGroupId);
            Assert.AreEqual(String.Empty, resultC.DockParentId);

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

        [TestMethod]
        public void PetStartupRules_ReleaseLoadingOnlyWhenReady()
        {
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(false, false));
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(true, false));
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(false, true));
            Assert.IsTrue(PetStartupRules.CanReleaseStartupLoading(true, true));
        }

        [TestMethod]
        public void StickyDockOperations_CoordinateGuardUsesClampedHeights()
        {
            Assert.IsTrue(
                StickyDockOperations.IsDockCoordinateRangeSafe(100,
                    new[] { 700, 700, 700 }, 30000));
            Assert.IsFalse(
                StickyDockOperations.IsDockCoordinateRangeSafe(29000,
                    new[] { 700, 700 }, 30000));
            Assert.IsFalse(
                StickyDockOperations.IsDockCoordinateRangeSafe(-31000,
                    null, 30000));
        }

        [TestMethod]
        public void StickyDockOperations_FindActiveDockTailWalksActiveGroup()
        {
            StickyNoteData root = new StickyNoteData { Id = "root" };
            StickyNoteData middle = new StickyNoteData
            {
                Id = "middle",
                DockParentId = "root"
            };
            StickyNoteData tail = new StickyNoteData
            {
                Id = "tail",
                DockParentId = "middle"
            };

            StickyNoteData found = StickyDockOperations.FindActiveDockTail(
                new[] { root, middle, tail },
                new[] { root, middle, tail },
                root);

            Assert.AreEqual(tail, found);
            Assert.AreEqual(root,
                StickyDockOperations.FindActiveDockTail(
                    new[] { root, middle, tail },
                    new[] { root },
                    root));
        }

        [TestMethod]
        public void StickyDockOperations_CanDockBelowMatchesWindowRule()
        {
            Assert.IsTrue(
                StickyDockOperations.CanDockBelow(80, 400, 900, 300,
                    400, 100, 280, 300, 20));
            Assert.IsTrue(
                StickyDockOperations.CanDockBelow(400, 400, 280, 300,
                    80, 100, 900, 300, 20));
            Assert.IsFalse(
                StickyDockOperations.CanDockBelow(0, 500, 280, 300,
                    0, 0, 280, 300, 20));
            Assert.IsFalse(
                StickyDockOperations.CanDockBelow(900, 400, 280, 300,
                    0, 100, 280, 300, 20));
        }

        [TestMethod]
        public void StickyDockGeometry_UnifiedLayoutMatchesWindowsResults()
        {
            List<DockSize> sizes = new List<DockSize>
            {
                new DockSize { Width = 320, Height = 300 },
                new DockSize { Width = 500, Height = 240 },
                new DockSize { Width = 380, Height = 260 }
            };
            List<DockRect> layout =
                StickyDockGeometry.CalculateUnifiedDockLayout(sizes,
                    120, 80, 460, 1F);

            Assert.AreEqual(3, layout.Count);
            Assert.AreEqual(120, layout[0].Left);
            Assert.AreEqual(80, layout[0].Top);
            Assert.AreEqual(460, layout[0].Width);
            Assert.AreEqual(300, layout[0].Height);
            Assert.AreEqual(380, layout[1].Top);
            Assert.AreEqual(620, layout[2].Top);
        }

        [TestMethod]
        public void StickyDockGeometry_DividerHeightUsesIndependentNoteRange()
        {
            Assert.AreEqual(220,
                StickyDockGeometry.CalculateDockDividerHeight(50));
            Assert.AreEqual(500,
                StickyDockGeometry.CalculateDockDividerHeight(500));
            Assert.AreEqual(700,
                StickyDockGeometry.CalculateDockDividerHeight(900));
        }

        [TestMethod]
        public void StickyDockGeometry_LiveMemberResizeUsesStableStartBounds()
        {
            List<DockRect> start = new List<DockRect>
            {
                new DockRect(100, 100, 420, 300),
                new DockRect(100, 400, 420, 300),
                new DockRect(100, 700, 420, 300),
                new DockRect(100, 1000, 420, 300)
            };
            int sourceHeight;
            List<DockRect> firstGrow = StickyDockGeometry
                .CalculateDockMemberResizeTargets(start, 0, 350,
                    out sourceHeight);
            Assert.AreEqual(350, sourceHeight);
            Assert.AreEqual(3, firstGrow.Count);
            Assert.AreEqual(450, firstGrow[0].Top);
            Assert.AreEqual(750, firstGrow[1].Top);
            Assert.AreEqual(1050, firstGrow[2].Top);
            Assert.IsTrue(firstGrow.All(bounds => bounds.Height == 300));

            List<DockRect> middleGrow = StickyDockGeometry
                .CalculateDockMemberResizeTargets(start, 1, 380,
                    out sourceHeight);
            Assert.AreEqual(380, sourceHeight);
            Assert.AreEqual(2, middleGrow.Count);
            Assert.AreEqual(780, middleGrow[0].Top);
            Assert.AreEqual(1080, middleGrow[1].Top);
            Assert.IsTrue(middleGrow.All(bounds => bounds.Height == 300));

            List<DockRect> middleShrink = StickyDockGeometry
                .CalculateDockMemberResizeTargets(start, 1, 240,
                    out sourceHeight);
            Assert.AreEqual(240, sourceHeight);
            Assert.AreEqual(640, middleShrink[0].Top);
            Assert.AreEqual(940, middleShrink[1].Top);
            StickyDockGeometry.CalculateDockMemberResizeTargets(start, 1, 50,
                out sourceHeight);
            Assert.AreEqual(220, sourceHeight);
            StickyDockGeometry.CalculateDockMemberResizeTargets(start, 1, 900,
                out sourceHeight);
            Assert.AreEqual(700, sourceHeight);

            List<DockRect> final = null;
            int[] cycle = { 450, 250, 600, 300 };
            for (int repeat = 0; repeat < 50; repeat++)
                foreach (int requested in cycle)
                    final = StickyDockGeometry.CalculateDockMemberResizeTargets(
                        start, 1, requested, out sourceHeight);
            Assert.AreEqual(300, sourceHeight);
            Assert.AreEqual(700, final[0].Top);
            Assert.AreEqual(1000, final[1].Top);
            Assert.IsTrue(final.All(bounds => bounds.Height == 300));
        }

        [TestMethod]
        public void StickyDockGeometry_HeaderTranslationMatchesWindowsResults()
        {
            DockPoint delta = StickyDockGeometry
                .CalculateHeaderReachableTranslation(
                    new DockRect
                    {
                        Left = 100,
                        Top = -200,
                        Width = 400,
                        Height = 32
                    },
                    new DockRect
                    {
                        Left = 0,
                        Top = 0,
                        Width = 1200,
                        Height = 900
                    });

            Assert.AreEqual(0, delta.X);
            Assert.AreEqual(200, delta.Y);
        }

        [TestMethod]
        public void StickyDockGeometry_RecoveryLayoutUsesWorkAreaCentering()
        {
            List<DockSize> sizes = new List<DockSize>
            {
                new DockSize { Width = 320, Height = 300 },
                new DockSize { Width = 280, Height = 220 }
            };
            List<DockRect> layout =
                StickyDockGeometry.CalculateStickyRecoveryLayout(
                    new DockRect
                    {
                        Left = 0,
                        Top = 0,
                        Width = 1920,
                        Height = 1040
                    },
                    sizes,
                    1F);

            Assert.AreEqual(2, layout.Count);
            Assert.AreEqual(651, layout[0].Left);
            Assert.AreEqual(370, layout[0].Top);
            Assert.AreEqual(320, layout[0].Width);
            Assert.AreEqual(300, layout[0].Height);
            Assert.AreEqual(989, layout[1].Left);
            Assert.AreEqual(370, layout[1].Top);
            Assert.AreEqual(280, layout[1].Width);
            Assert.AreEqual(220, layout[1].Height);
        }

        [TestMethod]
        public void StickyDockGeometry_RecoveredHeaderDragUsesPointerOffset()
        {
            DockRect recovered =
                StickyDockGeometry.CalculateRecoveredHeaderDragBounds(
                    new DockRect
                    {
                        Left = 100,
                        Top = 100,
                        Width = 320,
                        Height = 300
                    },
                    new DockRect
                    {
                        Left = 120,
                        Top = 130,
                        Width = 640,
                        Height = 600
                    },
                    new DockPoint { X = 500, Y = 400 },
                    new DockPoint { X = 20, Y = 10 },
                    true);

            Assert.AreEqual(480, recovered.Left);
            Assert.AreEqual(390, recovered.Top);
            Assert.AreEqual(320, recovered.Width);
            Assert.AreEqual(300, recovered.Height);
        }

        [TestMethod]
        public void ShortItemText_UsesOneSharedDisplayBudget()
        {
            Assert.IsTrue(ShortItemText.Fits(new string('中', 50)));
            Assert.IsFalse(ShortItemText.Fits(new string('中', 51)));
            Assert.IsTrue(ShortItemText.Fits(new string('W', 100)));
            Assert.AreEqual(50,
                ShortItemText.NormalizeAndTruncate(new string('中', 51)).Length);
            Assert.AreEqual("one two",
                ShortItemText.Normalize("  one\r\n\t two  "));
        }

        [TestMethod]
        public void ReminderSchedule_SortsAndReplacesWithoutDesktopState()
        {
            DateTime baseline = DateTime.UtcNow.AddHours(1);
            ReminderSchedule schedule = new ReminderSchedule();
            ReminderItem later = schedule.Add(baseline.AddMinutes(10),
                "later", "note-a", 12F, true);
            ReminderItem earlier = schedule.Add(baseline, "earlier",
                "note-b", 10.5F, false);

            Assert.AreSame(earlier, schedule.Next);
            Assert.AreSame(later, schedule.NextPreAlert);
            Assert.AreSame(later, schedule.FindBySourceNoteId("NOTE-A"));

            ReminderItem replacement = schedule.Replace(later,
                baseline.AddMinutes(-10), "replacement", 14F, false);
            Assert.AreSame(replacement, schedule.Next);
            Assert.AreEqual("note-a", replacement.SourceNoteId);
            Assert.IsNull(schedule.NextPreAlert);
        }

        [TestMethod]
        public void ReminderCoordinator_ExpressesTimingRulesAsPureFunctions()
        {
            ReminderItem enabled = new ReminderItem(
                DateTime.UtcNow.AddMinutes(2), "enabled", null, 10.5F, true);
            Assert.IsTrue(PetReminderCoordinator.IsPreAlertWindow(
                TimeSpan.FromSeconds(20)));
            Assert.IsFalse(PetReminderCoordinator.IsPreAlertWindow(
                TimeSpan.FromSeconds(21)));
            Assert.IsTrue(PetReminderCoordinator.ShouldShowPreAlert(enabled,
                TimeSpan.FromSeconds(5)));
            Assert.IsFalse(PetReminderCoordinator.ShouldRunReminderClock(true));
        }

        [TestMethod]
        public void StickyDockGroups_PersistOrderAndSkipHiddenLinks()
        {
            StickyNoteData first = new StickyNoteData { Id = "first" };
            StickyNoteData hidden = new StickyNoteData
            {
                Id = "hidden",
                Visible = false
            };
            StickyNoteData last = new StickyNoteData { Id = "last" };
            List<StickyNoteData> ordered = new List<StickyNoteData>
            {
                first,
                hidden,
                last
            };

            StickyDockGroups.ApplyOrderedGroup(ordered);

            Assert.AreEqual("first", first.DockGroupId);
            Assert.AreEqual(0, first.DockGroupOrder);
            Assert.AreEqual(1, hidden.DockGroupOrder);
            Assert.AreEqual(2, last.DockGroupOrder);
            Assert.AreEqual(String.Empty, hidden.DockParentId);
            Assert.AreEqual("first", last.DockParentId);
            CollectionAssert.AreEqual(ordered,
                StickyDockGroups.GetOrderedGroup(ordered, last));
        }

        [TestMethod]
        public void StickyDockOperations_OwnMembershipChangesOutsideWindowsUi()
        {
            StickyNoteData first = new StickyNoteData { Id = "first" };
            StickyNoteData extracted = new StickyNoteData { Id = "extracted" };
            StickyNoteData middle = new StickyNoteData { Id = "middle" };
            StickyNoteData last = new StickyNoteData { Id = "last" };
            List<StickyNoteData> original = new List<StickyNoteData>
            {
                first, extracted, middle, last
            };
            StickyDockGroups.ApplyOrderedGroup(original);

            List<StickyNoteData> remainder =
                StickyDockOperations.ExtractSingleDockMember(original,
                    extracted);
            CollectionAssert.AreEqual(new StickyNoteData[]
            {
                first, middle, last
            }, remainder);
            Assert.AreEqual(String.Empty, extracted.DockGroupId);

            List<StickyNoteData> merged =
                StickyDockOperations.MergeDockSnapshotsAfterParent(
                    remainder, middle,
                    new StickyNoteData[] { extracted });
            CollectionAssert.AreEqual(new StickyNoteData[]
            {
                first, middle, extracted, last
            }, merged);
            Assert.AreEqual(middle.Id, extracted.DockParentId);
            Assert.AreEqual(extracted.Id, last.DockParentId);

            StickyDockOperations.PreserveDockSlotForHiddenMember(merged,
                extracted);
            Assert.IsFalse(extracted.Visible);
            Assert.AreEqual(2, extracted.DockGroupOrder);
            Assert.AreEqual(middle.Id, last.DockParentId);
        }

        [TestMethod]
        public void StickyDockOperations_ExtractsByNoteIdNotObjectIdentity()
        {
            StickyNoteData first = new StickyNoteData { Id = "first" };
            StickyNoteData canonical = new StickyNoteData { Id = "middle" };
            StickyNoteData last = new StickyNoteData { Id = "last" };
            List<StickyNoteData> ordered = new List<StickyNoteData>
            {
                first, canonical, last
            };
            StickyDockGroups.ApplyOrderedGroup(ordered);

            List<StickyNoteData> remaining =
                StickyDockOperations.ExtractSingleDockMember(ordered,
                    new StickyNoteData { Id = "MIDDLE" });

            CollectionAssert.AreEqual(new StickyNoteData[] { first, last },
                remaining);
            Assert.AreEqual(String.Empty, canonical.DockGroupId);
            Assert.AreEqual(-1, canonical.DockGroupOrder);
        }

        [TestMethod]
        public void StickyDockOperations_MixedTypesMergeKeepsOrderAndMembership()
        {
            StickyNoteData ordinary = new StickyNoteData { Id = "ordinary" };
            StickyNoteData todo = new StickyNoteData
            {
                Id = "todo",
                IsTodoList = true
            };
            StickyNoteData schedule = new StickyNoteData
            {
                Id = "schedule",
                IsSchedule = true
            };
            StickyNoteData ordinaryTwo = new StickyNoteData { Id = "ordinary-two" };
            StickyNoteData scheduleTwo = new StickyNoteData
            {
                Id = "schedule-two",
                IsSchedule = true
            };

            List<StickyNoteData> target = new List<StickyNoteData>
            {
                ordinary, todo, schedule
            };
            StickyDockGroups.ApplyOrderedGroup(target);
            List<StickyNoteData> merged =
                StickyDockOperations.MergeDockSnapshotsAfterParent(
                    target, todo, new StickyNoteData[]
                    {
                        ordinaryTwo, scheduleTwo
                    });

            Assert.AreEqual(5, merged.Count);
            Assert.AreEqual(5, new HashSet<string>(new[]
            {
                ordinary.Id, todo.Id, schedule.Id,
                ordinaryTwo.Id, scheduleTwo.Id
            }).Count);
            for (int index = 0; index < merged.Count; index++)
            {
                Assert.AreEqual(ordinary.Id, merged[index].DockGroupId);
                Assert.AreEqual(index, merged[index].DockGroupOrder);
            }
            Assert.AreEqual(ordinary.Id, ordinary.DockGroupId);
            Assert.AreEqual(String.Empty, ordinary.DockParentId);
        }

        [TestMethod]
        public void StickyTabDropSession_DefersCommitAndUsesOpaqueSourceIdentity()
        {
            StickyTabDropSession session = new StickyTabDropSession();
            StickyNoteData note = new StickyNoteData { Id = "drag-note" };
            object source = new object();
            int commits = 0;

            session.Begin(note, source);
            Assert.AreSame(note, session.ActiveNote("DRAG-NOTE"));
            Assert.IsTrue(session.IsSource(source));
            Assert.IsFalse(session.IsSource(new object()));
            Assert.IsTrue(session.QueueCommit(note,
                delegate { commits++; }));
            Assert.AreEqual(0, commits);
            Assert.IsTrue(session.Complete(note));
            Assert.AreEqual(1, commits);
            Assert.IsNull(session.CurrentNote);
            Assert.IsNull(session.Source);
            Assert.IsFalse(session.Complete(note));
        }

        [TestMethod]
        public void StickyNoteCodec_RoundTripsVersionNineWithoutWindowsTypes()
        {
            StickyNoteData source = new StickyNoteData
            {
                Id = "codec-note",
                Title = "旅行清单",
                Text = "证件与充电器",
                RichTextRtf = "{\\rtf1\\ansi test}",
                FontFamilyName = "Microsoft YaHei UI",
                FontSizeTwips = 320,
                ColorArgb = unchecked((int)0xFF112233),
                TextColorArgb = unchecked((int)0xFFFFFFFF),
                BackgroundOpacityPercent = 75,
                DockGroupId = "codec-note",
                DockGroupOrder = 0,
                IsSchedule = true,
                IsTodoList = true
            };
            source.ScheduleItems.Add(new StickyScheduleItem("出发",
                new DateTime(2030, 5, 4), true));

            StickyNoteData restored = StickyNoteCodec.ParseLine(
                StickyNoteCodec.SerializeLine(source));

            Assert.IsNotNull(restored);
            Assert.AreEqual(source.Id, restored.Id);
            Assert.AreEqual(source.Title, restored.Title);
            Assert.AreEqual(source.RichTextRtf, restored.RichTextRtf);
            Assert.AreEqual(source.ColorArgb, restored.ColorArgb);
            Assert.AreEqual(source.TextColorArgb, restored.TextColorArgb);
            Assert.IsTrue(restored.IsSchedule);
            Assert.IsFalse(restored.IsTodoList);
            Assert.AreEqual(1, restored.ScheduleItems.Count);
            Assert.AreEqual("出发", restored.ScheduleItems[0].Text);
            Assert.IsTrue(restored.ScheduleItems[0].IsPinned);
        }

        [TestMethod]
        public void StickyNoteCodec_RoundTripsThreeTodoStates()
        {
            StickyNoteData source = new StickyNoteData { IsTodoList = true };
            source.TodoItems.Add(new StickyTodoItem("未完成",
                StickyTodoState.Pending));
            source.TodoItems.Add(new StickyTodoItem("进行中",
                StickyTodoState.InProgress));
            source.TodoItems.Add(new StickyTodoItem("已完成",
                StickyTodoState.Completed));

            StickyNoteData restored = StickyNoteCodec.ParseLine(
                StickyNoteCodec.SerializeLine(source));

            Assert.AreEqual(StickyTodoState.Pending,
                restored.TodoItems[0].State);
            Assert.AreEqual(StickyTodoState.InProgress,
                restored.TodoItems[1].State);
            Assert.AreEqual(StickyTodoState.Completed,
                restored.TodoItems[2].State);
        }

        [TestMethod]
        [DataRow(1)]
        [DataRow(2)]
        [DataRow(3)]
        [DataRow(4)]
        [DataRow(5)]
        [DataRow(6)]
        [DataRow(7)]
        [DataRow(8)]
        [DataRow(9)]
        public void StickyNoteCodec_LoadsEveryHistoricalGoldenFixture(
            int version)
        {
            string fixture = Path.Combine(AppContext.BaseDirectory,
                "Tests", "Fixtures", "sticky-v" + version + ".txt");
            StickyNoteData restored = StickyNoteCodec.ParseLine(
                File.ReadAllText(fixture, Encoding.UTF8).Trim());

            Assert.IsNotNull(restored, "Fixture v" + version +
                " must remain readable.");
            Assert.AreEqual("legacy-v" + version, restored.Id);
            Assert.AreEqual("Legacy body", restored.Text);
            if (version >= 2)
            {
                Assert.AreEqual("Legacy title", restored.Title);
                Assert.AreEqual(1, restored.TodoItems.Count);
                Assert.AreEqual("done", restored.TodoItems[0].Text);
            }
            if (version >= 4)
                Assert.AreEqual("{\\rtf1\\ansi legacy}",
                    restored.RichTextRtf);
            if (version >= 5)
                Assert.AreEqual("Arial", restored.FontFamilyName);
            if (version >= 7)
                Assert.AreEqual("group-root", restored.DockParentId);
            if (version >= 8)
            {
                Assert.AreEqual("group-root", restored.DockGroupId);
                Assert.AreEqual(3, restored.DockGroupOrder);
            }
            if (version == 9)
            {
                Assert.IsTrue(restored.IsSchedule);
                Assert.IsFalse(restored.IsTodoList);
                Assert.AreEqual(1, restored.ScheduleItems.Count);
                Assert.AreEqual("2030 schedule",
                    restored.ScheduleItems[0].Text);
            }
        }

        [TestMethod]
        public void StickyImportBackupValidator_AcceptsHistoricalCodecFixtures()
        {
            for (int version = 1; version <= 9; version++)
            {
                string fixture = Path.Combine(AppContext.BaseDirectory,
                    "Tests", "Fixtures", "sticky-v" + version + ".txt");
                StickyImportValidationResult result =
                    StickyImportBackupValidator.Validate(new[]
                    {
                        File.ReadAllText(fixture, Encoding.UTF8).Trim()
                    });
                Assert.IsTrue(result.Succeeded,
                    "Fixture v" + version + " should validate: " +
                    result.ErrorMessage);
                Assert.AreEqual(1, result.Notes.Count);
            }
        }

        [TestMethod]
        public void StickyImportBackupValidator_RejectsMalformedAndDuplicateBackup()
        {
            StickyNoteData note = new StickyNoteData { Id = "backup-note" };
            string valid = StickyNoteCodec.SerializeLine(note);
            string[] fields = valid.Split('|');

            string missingId = String.Join("|", fields.Select(
                (value, index) => index == 1 ? String.Empty : value));
            StickyImportValidationResult missing =
                StickyImportBackupValidator.Validate(new[] { missingId });
            Assert.IsFalse(missing.Succeeded);
            Assert.AreEqual(0, missing.Notes.Count);

            string badNumber = String.Join("|", fields.Select(
                (value, index) => index == 7 ? "not-a-number" : value));
            StickyImportValidationResult malformed =
                StickyImportBackupValidator.Validate(new[] { badNumber });
            Assert.IsFalse(malformed.Succeeded);
            Assert.AreEqual(0, malformed.Notes.Count);

            StickyImportValidationResult duplicate =
                StickyImportBackupValidator.Validate(new[] { valid, valid });
            Assert.IsFalse(duplicate.Succeeded);
            Assert.AreEqual(0, duplicate.Notes.Count);

            string[] unsupportedFields = (string[])fields.Clone();
            unsupportedFields[0] = "42";
            StickyImportValidationResult unsupported =
                StickyImportBackupValidator.Validate(new[]
                {
                    String.Join("|", unsupportedFields)
                });
            Assert.IsFalse(unsupported.Succeeded);

            string[] encodedFields = (string[])fields.Clone();
            encodedFields[26] = "%%%";
            StickyImportValidationResult badEncoding =
                StickyImportBackupValidator.Validate(new[]
                {
                    String.Join("|", encodedFields)
                });
            Assert.IsFalse(badEncoding.Succeeded);
        }

        [TestMethod]
        public void AnimationController_ResolvesStatePriorityWithoutAWindow()
        {
            PetAnimationController controller = new PetAnimationController();
            Func<int, bool> allRowsLoaded = delegate { return true; };

            Assert.AreEqual(PetAnimationController.WavingRow,
                controller.ChooseRow(true, true, true, false, allRowsLoaded));
            Assert.AreEqual(PetAnimationController.FailedRow,
                controller.ChooseRow(false, true, true, false, allRowsLoaded));

            controller.TypingSession = true;
            controller.TypingRow = PetAnimationController.ThinkingRow;
            Assert.AreEqual(PetAnimationController.ThinkingRow,
                controller.ChooseRow(false, false, true, false, allRowsLoaded));

            Assert.IsTrue(controller.TryStartOrdinaryPoke(
                PetAnimationController.HoverRow));
            Assert.IsFalse(controller.TryStartOrdinaryPoke(
                PetAnimationController.WaitingRow));
            Assert.AreEqual(PetAnimationController.HoverRow,
                controller.ChooseRow(false, false, true, false, allRowsLoaded));
            Assert.IsTrue(controller.TryStartEasterEgg(
                PetAnimationController.FailedRow));
            Assert.AreEqual(PetInteractionAnimationKind.EasterEgg,
                controller.InteractionAnimationKind);
            Assert.AreEqual(PetAnimationController.FailedRow,
                controller.ChooseRow(false, false, true, false, allRowsLoaded));

            controller.ReminderAttentionActive = true;
            Assert.AreEqual(PetAnimationController.NotificationRow,
                controller.ChooseRow(false, true, true, false, allRowsLoaded));
            controller.CancelInteractionAnimation();
            Assert.IsFalse(controller.TryStartOrdinaryPoke(
                PetAnimationController.HoverRow));
            controller.ReminderAttentionActive = false;
            Assert.IsTrue(controller.TryStartOrdinaryPoke(
                PetAnimationController.HoverRow));
            controller.CompleteInteractionAnimation();
            Assert.IsTrue(controller.TryStartOrdinaryPoke(
                PetAnimationController.WaitingRow));
            Assert.IsFalse(PetAnimationController.MovementStartsDrag(4, 4));
            Assert.IsTrue(PetAnimationController.MovementStartsDrag(6, 0));
        }

        [TestMethod]
        public void PokeBurstTracker_TriggersOnlyAtFiftyUntilAPause()
        {
            DateTime start = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            PetPokeBurstTracker tracker = new PetPokeBurstTracker();
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                Assert.IsFalse(tracker.RegisterPoke(
                    start.AddMilliseconds((poke - 1) * 100)));
            Assert.IsTrue(tracker.RegisterPoke(start.AddMilliseconds(4900)));
            Assert.IsFalse(tracker.RegisterPoke(start.AddMilliseconds(5000)));
            Assert.IsFalse(tracker.RegisterPoke(start.AddMilliseconds(5100)));

            PetPokeBurstTracker reset = new PetPokeBurstTracker();
            for (int poke = 1; poke < PetPokeBurstTracker.TargetCount; poke++)
                Assert.IsFalse(reset.RegisterPoke(
                    start.AddMilliseconds((poke - 1) * 100)));
            Assert.IsFalse(reset.RegisterPoke(start.AddMilliseconds(
                (PetPokeBurstTracker.TargetCount - 2) * 100 +
                PetPokeBurstTracker.MaxGapMilliseconds + 1)));
        }

        [TestMethod]
        public void ArtPreloadReservations_RetryOnlyAfterBackoff()
        {
            ArtPreloadReservations reservations = new ArtPreloadReservations();
            DateTime now = new DateTime(2030, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);

            Assert.IsTrue(reservations.TryReserve(4, false, now));
            Assert.IsFalse(reservations.TryReserve(4, false, now));
            reservations.Complete(4, false, now);
            Assert.IsFalse(reservations.TryReserve(4, false,
                now.AddMilliseconds(999)));
            Assert.IsTrue(reservations.TryReserve(4, false,
                now.AddSeconds(1)));
            reservations.Complete(4, true, now.AddSeconds(1));
            Assert.IsFalse(reservations.TryReserve(4, true,
                now.AddSeconds(2)));
        }

        [TestMethod]
        public void PetArtRules_ResolveAliasesAndNormalizeTimingWithoutBitmaps()
        {
            PetArtManifest manifest = new PetArtManifest
            {
                fallbackState = "idle",
                states = new Dictionary<string, PetArtStateDefinition>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "idle", new PetArtStateDefinition() },
                    { "waiting", new PetArtStateDefinition { alias = "idle" } }
                }
            };
            Assert.AreEqual("idle",
                PetArtRules.ResolveTerminalStateName(manifest, "waiting"));
            Assert.AreEqual("idle",
                PetArtRules.ResolveTerminalStateName(manifest, "missing"));

            PetArtRenderSettings render =
                PetArtRules.NormalizeRenderSettings(new PetArtRenderSettings
                {
                    anchorX = Double.NaN,
                    anchorY = 2.0,
                    minimumFrameMs = 25,
                    maximumFrameMs = 100
                });
            Assert.AreEqual("contain", render.fit);
            Assert.AreEqual(0.5, render.anchorX);
            Assert.AreEqual(1.0, render.anchorY);
            Assert.AreEqual(50, PetArtRules.NormalizeFrameDuration(100,
                new PetArtStateDefinition { speed = 2.0 }, render));
            Assert.AreEqual(25, PetArtRules.NormalizeFrameDuration(5,
                null, render));
        }

        [TestMethod]
        public void KeyboardPrivacyPolicy_RequiresExplicitAcknowledgement()
        {
            Assert.IsTrue(PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(
                true, false));
            Assert.IsFalse(PetKeyboardPrivacyPolicy.ShouldStartHook(true, false));
            Assert.IsTrue(PetKeyboardPrivacyPolicy.ShouldStartHook(true, true));
            Assert.IsTrue(
                PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                    true, false));
            Assert.IsTrue(
                PetKeyboardPrivacyPolicy.ShouldSuppressCapturedInput(
                    true, true));
            Assert.IsTrue(
                PetKeyboardPrivacyPolicy.ShouldSuppressCapturedInput(
                    false, false));
            Assert.IsFalse(
                PetKeyboardPrivacyPolicy.ShouldSuppressCapturedInput(
                    false, true));
            Assert.IsFalse(
                PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    false, false));
            Assert.IsFalse(
                PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, true));
            Assert.IsTrue(
                PetKeyboardPrivacyPolicy.ShouldSuppressOwnApplicationInput(
                    true, false));
        }

        [TestMethod]
        public void PetMessagePolicy_PreservesReminderPriorityAndSilentMode()
        {
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.Hover, PetMessageKind.Feedback, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderPreAlert,
                PetMessageKind.Feedback, false));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderPreAlert,
                PetMessageKind.ReminderDue, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderDue,
                PetMessageKind.Feedback, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderDue,
                PetMessageKind.DailyGreeting, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderDue,
                PetMessageKind.Discovery, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderDue,
                PetMessageKind.Hover, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderDue,
                PetMessageKind.EasterEgg, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.ReminderPreAlert,
                PetMessageKind.EasterEgg, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.EasterEgg,
                PetMessageKind.DailyGreeting, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.EasterEgg,
                PetMessageKind.Feedback, false));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.EasterEgg,
                PetMessageKind.ReminderPreAlert, false));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.DailyGreeting,
                PetMessageKind.EasterEgg, false));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.EasterEgg,
                PetMessageKind.ReminderDue, false));
            Assert.IsTrue(PetMessagePolicy.ShouldSuppress(
                PetMessageKind.DailyGreeting, true));
            Assert.IsFalse(PetMessagePolicy.ShouldSuppress(
                PetMessageKind.Feedback, true));
            Assert.IsTrue(PetMessagePolicy.ShouldSuppress(
                PetMessageKind.SmallTalk, true));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.SmallTalk, PetMessageKind.Feedback, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.SmallTalk, PetMessageKind.Hover, false));
            Assert.IsFalse(PetMessagePolicy.ShouldReplace(
                PetMessageKind.SmallTalk, PetMessageKind.Discovery, false));
            Assert.IsTrue(PetMessagePolicy.ShouldReplace(
                PetMessageKind.SmallTalk, PetMessageKind.ReminderDue, false));
        }

        [TestMethod]
        public void PetSmallTalkPolicy_CooldownChanceAndPhraseRotation()
        {
            DateTime start = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            Assert.IsTrue(PetSmallTalkPolicy.ShouldAttempt(
                DateTime.MinValue, start, 24));
            Assert.IsFalse(PetSmallTalkPolicy.ShouldAttempt(
                start, start.AddMilliseconds(
                    PetSmallTalkPolicy.CooldownMilliseconds - 1), 24));
            Assert.IsTrue(PetSmallTalkPolicy.ShouldAttempt(
                start, start.AddMilliseconds(
                    PetSmallTalkPolicy.CooldownMilliseconds), 24));
            Assert.IsFalse(PetSmallTalkPolicy.ShouldAttempt(
                start, start.AddMilliseconds(
                    PetSmallTalkPolicy.CooldownMilliseconds), 25));
            Assert.AreEqual(1, PetSmallTalkPolicy.NextPhraseIndex(
                0, 1, 3));
            Assert.AreEqual(2, PetSmallTalkPolicy.NextPhraseIndex(
                1, 1, 3));
        }

        [TestMethod]
        public void BubbleReadingDurationRules_AreStableAndCapped()
        {
            string shortText = "我在呢～";
            string mediumText = "需要我帮什么忙吗？";
            string longText = new String('字', 80);

            Assert.AreEqual(
                BubbleReadingDurationRules.MinimumReadableMilliseconds(
                    shortText),
                BubbleReadingDurationRules.MinimumReadableMilliseconds(
                    shortText));
            Assert.IsTrue(BubbleReadingDurationRules.AutoCloseMilliseconds(
                shortText) < 3000);
            Assert.IsTrue(BubbleReadingDurationRules.AutoCloseMilliseconds(
                mediumText) >= 2400);
            Assert.IsTrue(BubbleReadingDurationRules.AutoCloseMilliseconds(
                longText) <= 7000);
            Assert.IsTrue(BubbleReadingDurationRules.AutoCloseMilliseconds(
                longText) >
                BubbleReadingDurationRules.MinimumReadableMilliseconds(
                    longText));
        }

        [TestMethod]
        public void DailyContentRules_ShowOncePerLocalDate()
        {
            DateTime today = new DateTime(2035, 9, 8, 14, 30, 0,
                DateTimeKind.Local);
            Assert.IsTrue(DailyContentRules.ShouldShow(String.Empty, today));
            Assert.IsFalse(DailyContentRules.ShouldShow("20350908", today));
            Assert.IsTrue(DailyContentRules.ShouldShow("20350907", today));
            Assert.IsTrue(DailyContentRules.ShouldShow("invalid", today));
            Assert.AreEqual("20350908", DailyContentRules.DateKey(today));
        }

        [TestMethod]
        public void DailyContentRules_ResolveEveryDayPartBoundary()
        {
            Assert.AreEqual(DayPart.LateNight, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 4, 59, 0)));
            Assert.AreEqual(DayPart.Morning, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 5, 0, 0)));
            Assert.AreEqual(DayPart.Morning, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 10, 59, 0)));
            Assert.AreEqual(DayPart.Midday, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 11, 0, 0)));
            Assert.AreEqual(DayPart.Midday, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 13, 59, 0)));
            Assert.AreEqual(DayPart.Afternoon,
                DailyContentRules.ResolveDayPart(
                    new DateTime(2035, 1, 1, 14, 0, 0)));
            Assert.AreEqual(DayPart.Afternoon,
                DailyContentRules.ResolveDayPart(
                    new DateTime(2035, 1, 1, 17, 59, 0)));
            Assert.AreEqual(DayPart.Evening, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 18, 0, 0)));
            Assert.AreEqual(DayPart.Evening, DailyContentRules.ResolveDayPart(
                new DateTime(2035, 1, 1, 23, 59, 0)));
            Assert.AreEqual("下午好～今天过得怎么样？",
                DailyContentRules.GreetingFor(DayPart.Afternoon));
        }

        [TestMethod]
        public void SolarTermCalculator_Year2000To2100_Produces24UniqueSortedTerms()
        {
            int[] expectedLongitudes = Enumerable.Range(0, 24)
                .Select(step => step * 15).ToArray();
            for (int year = SolarTermCalculator.MinSupportedYear;
                year <= SolarTermCalculator.MaxSupportedYear; year++)
            {
                SolarTermInfo[] terms = SolarTermCalculator.CalculateYear(year);
                Assert.AreEqual(24, terms.Length, "term count " + year);
                Assert.AreEqual(24, terms.Select(t => t.Term).Distinct().Count(),
                    "unique terms " + year);

                int[] longitudes = terms.Select(t => t.LongitudeDegrees)
                    .OrderBy(l => ((l % 360) + 360) % 360).ToArray();
                CollectionAssert.AreEqual(expectedLongitudes, longitudes,
                    "longitude set " + year);

                for (int i = 1; i < terms.Length; i++)
                    Assert.IsTrue(terms[i - 1].InstantUtc < terms[i].InstantUtc,
                        "chronological order " + year);
                Assert.AreEqual(SolarTerm.MinorCold, terms[0].Term,
                    "first term " + year);
                Assert.AreEqual(SolarTerm.WinterSolstice, terms[23].Term,
                    "last term " + year);

                for (int i = 0; i < terms.Length; i++)
                    for (int j = i + 1; j < terms.Length; j++)
                        Assert.AreNotEqual(terms[i].InstantUtc,
                            terms[j].InstantUtc, "duplicate instant " + year);
            }
        }

        [TestMethod]
        public void SolarTermCalculator_MatchesHongKongObservatoryOracleDates()
        {
            TimeSpan hkt = TimeSpan.FromHours(8);
            AssertOracleTerm(2016, 2, 4, SolarTerm.StartOfSpring, 315, hkt);
            AssertOracleTerm(2016, 3, 20, SolarTerm.VernalEquinox, 0, hkt);
            AssertOracleTerm(2016, 6, 21, SolarTerm.SummerSolstice, 90, hkt);
            AssertOracleTerm(2016, 9, 7, SolarTerm.WhiteDew, 165, hkt);
            AssertOracleTerm(2016, 12, 21, SolarTerm.WinterSolstice, 270, hkt);
            AssertOracleTerm(2026, 2, 4, SolarTerm.StartOfSpring, 315, hkt);
            AssertOracleTerm(2026, 2, 18, SolarTerm.RainWater, 330, hkt);
            AssertOracleTerm(2026, 9, 7, SolarTerm.WhiteDew, 165, hkt);
            AssertOracleTerm(2026, 9, 23, SolarTerm.AutumnalEquinox, 180, hkt);
            AssertOracleTerm(2026, 12, 7, SolarTerm.MajorSnow, 255, hkt);
            AssertOracleTerm(2026, 12, 22, SolarTerm.WinterSolstice, 270, hkt);
        }

        [TestMethod]
        public void SolarTermCalculator_LocalDateSemanticsAndOutOfRange()
        {
            TimeSpan hkt = TimeSpan.FromHours(8);
            Assert.IsNull(SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 6, 12, 0, 0, hkt)));
            Assert.IsNull(SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 8, 12, 0, 0, hkt)));

            SolarTermInfo? whiteDew = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 7, 0, 1, 0, hkt));
            Assert.IsTrue(whiteDew.HasValue);
            Assert.AreEqual(SolarTerm.WhiteDew, whiteDew.Value.Term);
            Assert.AreEqual(SolarTerm.WhiteDew,
                SolarTermCalculator.FindForLocalDate(
                    new DateTimeOffset(2026, 9, 7, 23, 59, 0, hkt)).Value.Term);

            SolarTermInfo whiteDew2016 = SolarTermCalculator.CalculateYear(2016)
                .Single(t => t.Term == SolarTerm.WhiteDew);
            Assert.AreEqual(new DateTime(2016, 9, 7),
                whiteDew2016.InstantUtc.ToOffset(hkt).Date);
            Assert.AreEqual(new DateTime(2016, 9, 6),
                whiteDew2016.InstantUtc.ToOffset(TimeSpan.FromHours(-8)).Date);
            Assert.AreEqual(SolarTerm.WhiteDew,
                SolarTermCalculator.FindForLocalDate(
                    new DateTimeOffset(2016, 9, 6, 20, 0, 0,
                        TimeSpan.FromHours(-8))).Value.Term);
            Assert.IsNull(SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2016, 9, 7, 8, 0, 0,
                    TimeSpan.FromHours(-8))));

            Assert.IsNull(SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(1999, 6, 21, 12, 0, 0, hkt)));
            Assert.IsNull(SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2101, 6, 21, 12, 0, 0, hkt)));
        }

        [TestMethod]
        public void CuratedDailyLineCatalog_HasCompleteUniqueBundledCopy()
        {
            DailyLineEntry[] entries = CuratedDailyLineCatalog.GetEntries();
            Assert.AreEqual(96, entries.Length);
            Assert.AreEqual(96, entries.Select(entry => entry.Id)
                .Distinct().Count());
            Assert.AreEqual(96, entries.Select(entry => entry.Text)
                .Distinct().Count());
            Assert.IsTrue(entries.All(entry =>
                !String.IsNullOrWhiteSpace(entry.Id) &&
                !String.IsNullOrWhiteSpace(entry.Text)));
        }

        [TestMethod]
        public void ZodiacDailyCatalog_RetainsStableSignSpecificEntries()
        {
            Assert.AreEqual(0,
                ZodiacDailyCatalog.GetEntries(ZodiacSign.None).Length);
            HashSet<string> allIds = new HashSet<string>();
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                ZodiacSign sign = (ZodiacSign)value;
                DailyLineEntry[] entries = ZodiacDailyCatalog.GetEntries(sign);
                Assert.AreEqual(6, entries.Length, sign.ToString());
                Assert.IsTrue(entries.All(entry =>
                    !String.IsNullOrWhiteSpace(entry.Id) &&
                    !String.IsNullOrWhiteSpace(entry.Text) &&
                    allIds.Add(entry.Id)), sign.ToString());
                Assert.AreEqual(entries.Length, entries.Select(entry =>
                    entry.Text).Distinct().Count(),
                    sign.ToString());
            }
            Assert.AreEqual(72, allIds.Count);
        }

        [TestMethod]
        public void DailyLineSelectors_AreDeterministicAndBounded()
        {
            DateTimeOffset localNow = new DateTimeOffset(2026, 9, 3,
                12, 0, 0, TimeSpan.FromHours(8));
            DailyLineEntry curated = CuratedDailyLineSelector.Select(localNow);
            Assert.IsNotNull(curated);
            Assert.AreEqual(curated.Id,
                CuratedDailyLineSelector.Select(localNow).Id);
            Assert.IsNull(ZodiacDailySelector.Select(ZodiacSign.None,
                localNow));
            Assert.IsNull(ZodiacDailySelector.Select((ZodiacSign)999,
                localNow));

            DailyLineEntry scorpio = ZodiacDailySelector.Select(
                ZodiacSign.Scorpio,
                localNow);
            Assert.IsNotNull(scorpio);
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(scorpio.Id, ZodiacDailySelector.Select(
                    ZodiacSign.Scorpio, localNow).Id);
            Assert.IsTrue(ZodiacDailyCatalog.GetEntries(ZodiacSign.Scorpio)
                .Any(entry => entry.Id == scorpio.Id));

            DateTimeOffset start = new DateTimeOffset(2026, 1, 1,
                12, 0, 0, TimeSpan.FromHours(8));
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                ZodiacSign sign = (ZodiacSign)value;
                int eligibleDays = 0;
                for (int day = 0; day < 3650; day++)
                    if (ZodiacDailySelector.Select(sign,
                        start.AddDays(day)) != null) eligibleDays++;
                double percent = eligibleDays * 100D / 3650D;
                Assert.IsTrue(percent >= 10D && percent <= 20D,
                    sign + ": " + percent);
            }
        }

        [TestMethod]
        public void DailyLineSelectors_UseLocalCivilDateAcrossOffsets()
        {
            DateTimeOffset sameInstant = new DateTimeOffset(2026, 9, 1,
                16, 30, 0, TimeSpan.Zero);
            DateTimeOffset hongKong = sameInstant.ToOffset(
                TimeSpan.FromHours(8));
            DateTimeOffset pacific = sameInstant.ToOffset(
                TimeSpan.FromHours(-8));
            Assert.AreEqual(2, hongKong.Day);
            Assert.AreEqual(1, pacific.Day);
            Assert.AreNotEqual(CuratedDailyLineSelector.Select(hongKong).Id,
                CuratedDailyLineSelector.Select(pacific).Id);
            Assert.AreEqual(CuratedDailyLineSelector.Select(
                    new DateTimeOffset(2026, 9, 1, 1, 0, 0,
                        TimeSpan.FromHours(8))).Id,
                CuratedDailyLineSelector.Select(
                    new DateTimeOffset(2026, 9, 1, 23, 0, 0,
                        TimeSpan.FromHours(-5))).Id);
        }

        [TestMethod]
        public void AlmanacCalculator_UsesDetachedLocalCivilDayYiJi()
        {
            DateTimeOffset sameInstant = new DateTimeOffset(2026, 9, 1,
                16, 30, 0, TimeSpan.Zero);
            AlmanacDayInfo hongKong = AlmanacCalculator.Calculate(
                sameInstant.ToOffset(TimeSpan.FromHours(8)));
            AlmanacDayInfo pacific = AlmanacCalculator.Calculate(
                sameInstant.ToOffset(TimeSpan.FromHours(-8)));
            Assert.IsNotNull(hongKong);
            Assert.IsNotNull(pacific);
            Assert.AreEqual(2, hongKong.Day);
            Assert.AreEqual(1, pacific.Day);
            Assert.IsNotNull(hongKong.Yi);
            Assert.IsNotNull(hongKong.Ji);
            Assert.IsTrue(hongKong.Yi.Count > 0);
            Assert.IsTrue(hongKong.Ji.Count > 0);
        }

        [TestMethod]
        public void AlmanacSemanticCatalog_MapsOnlyConservativeWhitelist()
        {
            AssertAlmanacMapping("扫舍", AlmanacTopic.Tidy);
            AssertAlmanacMapping("会友", AlmanacTopic.Social);
            AssertAlmanacMapping("会亲友", AlmanacTopic.Social);
            AssertAlmanacMapping("入学", AlmanacTopic.Learning);
            AssertAlmanacMapping("习艺", AlmanacTopic.Learning);
            AssertAlmanacMapping("栽种", AlmanacTopic.Plants);
            AssertAlmanacMapping("理发", AlmanacTopic.Haircut);
            AssertAlmanacMapping("剃头", AlmanacTopic.Haircut);
            AssertAlmanacMapping("整手足甲", AlmanacTopic.NailCare);
            AssertAlmanacMapping("沐浴", AlmanacTopic.Bath);
            AssertAlmanacMapping("出行", AlmanacTopic.Outing);
            AssertAlmanacMapping("裁衣", AlmanacTopic.ClothingCraft);
            foreach (string term in new[] { "求医", "治病", "针灸",
                "纳财", "求财", "置产", "词讼", "立券", "交易",
                "安葬", "入殓", "祭祀", "祈福", "动土", "修造" })
            {
                AlmanacTopic ignored;
                Assert.IsFalse(AlmanacSemanticCatalog.TryMap(term,
                    out ignored), term);
            }
        }

        [TestMethod]
        public void AlmanacSelector_DeduplicatesConflictsAndUsesTiers()
        {
            DateTimeOffset date = new DateTimeOffset(2026, 9, 3,
                12, 0, 0, TimeSpan.FromHours(8));
            AlmanacDailySelection social = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "会友", "会亲友" }, new string[0]), date);
            Assert.IsNotNull(social);
            Assert.AreEqual(AlmanacTopic.Social, social.Topic);
            AlmanacDailySelection orderedA = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "扫舍", "会友" }, new string[0]), date);
            AlmanacDailySelection orderedB = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "会友", "扫舍" }, new string[0]), date);
            Assert.AreEqual(orderedA.Topic, orderedB.Topic);
            Assert.AreEqual(orderedA.SourceTerm, orderedB.SourceTerm);
            Assert.AreEqual(orderedA.VariantId, orderedB.VariantId);

            Assert.IsNull(AlmanacDailySelector.Select(new AlmanacDayInfo(
                2026, 9, 3, new[] { "出行" }, new[] { "出行" }), date));
            Assert.IsNull(AlmanacDailySelector.Select(new AlmanacDayInfo(
                2026, 9, 3, new[] { "求医", "纳财", "祭祀" },
                new string[0]), date));

            AlmanacDailySelection everyday = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "扫舍", "嫁娶" }, new string[0]), date);
            Assert.AreEqual(AlmanacTopic.Tidy, everyday.Topic);
            AlmanacDailySelection cultural = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3, new[] { "嫁娶" },
                    new string[0]), date);
            Assert.AreEqual(AlmanacTopic.RelationshipCelebration,
                cultural.Topic);
            AlmanacDailySelection outingJi = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3, new string[0],
                    new[] { "出行" }), date);
            Assert.AreEqual(AlmanacTopic.Outing, outingJi.Topic);
            Assert.IsFalse(outingJi.IsYi);
            Assert.IsTrue(outingJi.Text.Contains("天气") ||
                outingJi.Text.Contains("现实"));
            AlmanacDailySelection conservative =
                AlmanacDailySelector.Select(new AlmanacDayInfo(2026, 9, 3,
                    new string[0], new[] { "诸事不宜" }), date);
            Assert.AreEqual(AlmanacTopic.ConservativeDay,
                conservative.Topic);
            Assert.IsFalse(conservative.Text.Contains("今天一定") ||
                conservative.Text.Contains("千万") ||
                conservative.Text.Contains("不能出门"));
        }

        [TestMethod]
        public void AlmanacWording_IsDeterministicVariedAndSafe()
        {
            var cases = new[]
            {
                new { Topic = AlmanacTopic.Tidy, Term = "扫舍", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Social, Term = "会友", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Learning, Term = "入学", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Plants, Term = "栽种", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Haircut, Term = "理发", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.NailCare, Term = "整手足甲", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Bath, Term = "沐浴", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.Outing, Term = "出行", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.ClothingCraft, Term = "裁衣", Yi = true,
                    Minimum = 5 },
                new { Topic = AlmanacTopic.RelationshipCelebration,
                    Term = "嫁娶", Yi = true, Minimum = 3 },
                new { Topic = AlmanacTopic.MovingHome, Term = "入宅",
                    Yi = true, Minimum = 3 },
                new { Topic = AlmanacTopic.ConservativeDay, Term = "诸事不宜",
                    Yi = false, Minimum = 3 }
            };
            Dictionary<string, int> prefixes = new Dictionary<string, int>();
            int textCount = 0;
            int todayPrefix = 0;
            int yiJiTermCount = 0;
            bool sawTraditionalCalendar = false;
            bool sawFolkWording = false;
            bool sawLifeFirst = false;
            bool sawSourceLate = false;
            foreach (var item in cases)
            {
                HashSet<string> variants = new HashSet<string>();
                for (int day = 0; day < 730; day++)
                {
                    DateTimeOffset date = new DateTimeOffset(2026, 1, 1,
                        12, 0, 0, TimeSpan.FromHours(8)).AddDays(day);
                    AlmanacDayInfo info = new AlmanacDayInfo(date.Year,
                        date.Month, date.Day,
                        item.Yi ? new[] { item.Term } : new string[0],
                        item.Yi ? new string[0] : new[] { item.Term });
                    AlmanacDailySelection selected =
                        AlmanacDailySelector.Select(info, date);
                    AlmanacDailySelection retry =
                        AlmanacDailySelector.Select(info, date);
                    Assert.IsNotNull(selected);
                    Assert.AreEqual(item.Topic, selected.Topic);
                    Assert.AreEqual(selected.VariantId, retry.VariantId);
                    Assert.AreEqual(selected.FramingId, retry.FramingId);
                    Assert.AreEqual(selected.WordingId, retry.WordingId);
                    Assert.AreEqual(selected.EndingId, retry.EndingId);
                    Assert.AreEqual(selected.Text, retry.Text);
                    Assert.IsFalse(selected.Text.Contains("今天一定") ||
                        selected.Text.Contains("必须") ||
                        selected.Text.Contains("千万不要") ||
                        selected.Text.Contains("绝对不能"));
                    Assert.IsFalse(selected.Text.Contains("老黄历"));
                    if (selected.Text.Contains("宜忌")) yiJiTermCount++;
                    sawTraditionalCalendar |= selected.Text.Contains(
                        "传统日历");
                    sawFolkWording |= selected.Text.Contains("民俗");
                    sawLifeFirst |= selected.FramingId == "F06-LIFE-FIRST";
                    sawSourceLate |= selected.FramingId == "F07-SOURCE-LATE";
                    variants.Add(selected.VariantId);
                    string compact = selected.Text.Replace("\n", "");
                    if (compact.StartsWith("今天",
                        StringComparison.Ordinal)) todayPrefix++;
                    string prefix = compact.Substring(0,
                        Math.Min(6, compact.Length));
                    int count;
                    prefixes.TryGetValue(prefix, out count);
                    prefixes[prefix] = count + 1;
                    textCount++;
                }
                Assert.IsTrue(variants.Count >= item.Minimum,
                    item.Topic + ": " + variants.Count);
            }
            Assert.IsTrue(prefixes.Values.Max() * 100D / textCount < 25D);
            Assert.IsTrue(todayPrefix * 100D / textCount < 25D);
            Assert.IsTrue(yiJiTermCount * 100D / textCount < 35D);
            Assert.IsTrue(sawTraditionalCalendar);
            Assert.IsTrue(sawFolkWording);
            Assert.IsTrue(sawLifeFirst);
            Assert.IsTrue(sawSourceLate);
        }

        [TestMethod]
        public void WeatherMeaningRules_UseExplicitPriorityAndMundaneFallback()
        {
            WeatherDaySummary yesterday = WeatherDay(15, 30, 15, 30,
                0, 0, 0, null, 15, false);
            Assert.AreEqual(WeatherMeaning.Snow, SelectWeather(yesterday,
                WeatherDay(-4, 1, -8, 0, 5, 90, 8, 15, 60, true)));
            Assert.AreEqual(WeatherMeaning.RainAndWind, SelectWeather(
                yesterday, WeatherDay(18, 22, 17, 21, 5, 80, 3, 9, 55,
                    false)));
            Assert.AreEqual(WeatherMeaning.RainAndCooling, SelectWeather(
                yesterday, WeatherDay(16, 23, 15, 22, 5, 80, 3, 9, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.HeavyRain, SelectWeather(
                WeatherDay(15, 25, 15, 25, 0, 0, 0, null, 10, false),
                WeatherDay(17, 24, 17, 24, 16, 80, 3, 8, 20, false)));
            Assert.AreEqual(WeatherMeaning.PersistentRain, SelectWeather(
                yesterday, WeatherDay(18, 28, 18, 28, 5, 80, 6, 7, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.Windy, SelectWeather(yesterday,
                WeatherDay(18, 28, 18, 28, 0, 10, 0, null, 55, false)));
            Assert.AreEqual(WeatherMeaning.Cooling, SelectWeather(yesterday,
                WeatherDay(15, 23, 15, 23, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.Warming, SelectWeather(
                WeatherDay(10, 20, 10, 20, 0, 0, 0, null, 10, false),
                WeatherDay(15, 27, 15, 27, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.RainLater, SelectWeather(
                yesterday, WeatherDay(18, 28, 18, 28, 2, 80, 1, 15, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.Hot, SelectWeather(yesterday,
                WeatherDay(25, 33, 26, 36, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.Cold, SelectWeather(
                WeatherDay(1, 8, 1, 8, 0, 0, 0, null, 10, false),
                WeatherDay(1, 8, -1, 7, 0, 10, 0, null, 20, false)));
            Assert.AreEqual(WeatherMeaning.LargeTemperatureRange,
                SelectWeather(WeatherDay(10, 22, 10, 22, 0, 0, 0, null,
                    10, false), WeatherDay(10, 22, 5, 25, 0, 10, 0, null,
                    20, false)));
            Assert.IsNull(SelectWeather(WeatherDay(18, 26, 18, 27, 0, 0,
                0, null, 10, false), WeatherDay(18, 26, 18, 27, 0, 20, 0,
                null, 20, false)));
        }

        [TestMethod]
        public void WeatherMeaningRules_HeavyRainRequiresPrecipitationNotProbability()
        {
            WeatherDaySummary yesterday = WeatherDay(15, 25, 15, 25,
                0, 0, 0, null, 10, false);
            Assert.AreNotEqual(WeatherMeaning.HeavyRain, SelectWeather(
                yesterday, WeatherDay(17, 24, 17, 24, 3, 95, 2, 8, 20,
                    false)));
            Assert.AreEqual(WeatherMeaning.HeavyRain, SelectWeather(
                yesterday, WeatherDay(17, 24, 17, 24, 16, 80, 3, 8, 20,
                    false)));
        }

        [TestMethod]
        public void WeatherWording_IsDeterministicVariedAndCautious()
        {
            DateTime start = new DateTime(2026, 1, 1);
            Dictionary<string, int> prefixes =
                new Dictionary<string, int>();
            int todayPrefixes = 0;
            int textCount = 0;
            foreach (WeatherMeaning meaning in Enum.GetValues(
                typeof(WeatherMeaning)))
            {
                string[] catalog = WeatherWordingCatalog.GetVariantsForTest(
                    meaning);
                int required = meaning == WeatherMeaning.RainLater ||
                    meaning == WeatherMeaning.Cooling ||
                    meaning == WeatherMeaning.Windy ||
                    meaning == WeatherMeaning.Hot ? 5 : 3;
                Assert.IsTrue(catalog.Length >= required, meaning.ToString());
                HashSet<string> selected = new HashSet<string>();
                for (int day = 0; day < 365; day++)
                {
                    DateTime date = start.AddDays(day);
                    WeatherDailySelection first = WeatherWordingCatalog.Select(
                        meaning, date, "30.5928,114.3055|Asia/Shanghai");
                    WeatherDailySelection retry = WeatherWordingCatalog.Select(
                        meaning, date, "30.5928,114.3055|Asia/Shanghai");
                    Assert.AreEqual(first.Text, retry.Text);
                    Assert.AreEqual(meaning, first.Meaning);
                    Assert.IsTrue(first.Text.Length >= 20 &&
                        first.Text.Length <= 60, first.Text);
                    Assert.IsFalse(first.Text.Contains("预警"));
                    Assert.IsFalse(first.Text.Contains("一定"));
                    Assert.IsFalse(first.Text.Contains("保证"));
                    selected.Add(first.Text);
                    string compact = first.Text.Replace("\n", "");
                    if (compact.StartsWith("今天",
                        StringComparison.Ordinal)) todayPrefixes++;
                    string prefix = compact.Substring(0,
                        Math.Min(6, compact.Length));
                    int count;
                    prefixes.TryGetValue(prefix, out count);
                    prefixes[prefix] = count + 1;
                    textCount++;
                }
                Assert.IsTrue(selected.Count >= required,
                    meaning + ": " + selected.Count);
            }
            Assert.IsTrue(todayPrefixes * 100D / textCount <= 25D);
            Assert.IsTrue(prefixes.Values.Max() * 100D / textCount < 25D);
        }

        [TestMethod]
        public void DailyBriefingComposer_EnforcesSupplementaryBudget()
        {
            string greeting = DailyContentRules.GreetingFor(
                DayPart.Afternoon);
            DailyLineEntry curated = new DailyLineEntry("C-TEST", "精选。");
            DailyLineEntry zodiac = new DailyLineEntry("Z-TEST", "星座。");

            SolarTermInfo? whiteDew = new SolarTermInfo(SolarTerm.WhiteDew,
                "白露", 165, new DateTimeOffset(2026, 9, 7, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            const string almanac = "黄历内容。";
            const string weather = "天气内容。";
            DailyBriefingContent solarWeatherAlmanac =
                new DailyBriefingContent(whiteDew, weather, almanac,
                    curated, zodiac);
            DailyBriefingContent solarWeather = new DailyBriefingContent(
                whiteDew, weather, null, curated, zodiac);
            DailyBriefingContent solarAlmanac = new DailyBriefingContent(
                whiteDew, null, almanac, curated, zodiac);
            DailyBriefingContent solarOnly = new DailyBriefingContent(
                whiteDew, null, null, curated, zodiac);
            DailyBriefingContent weatherAlmanac = new DailyBriefingContent(
                null, weather, almanac, curated, zodiac);
            DailyBriefingContent weatherOnly = new DailyBriefingContent(
                null, weather, null, curated, zodiac);
            DailyBriefingContent almanacOnly = new DailyBriefingContent(
                null, null, almanac, curated, zodiac);
            DailyBriefingContent fallback = new DailyBriefingContent(null,
                null, null, curated, zodiac);
            Assert.AreEqual(greeting + "\n今天是白露哦。\n天气内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    solarWeatherAlmanac));
            Assert.AreEqual(greeting + "\n今天是白露哦。\n天气内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    solarWeather));
            Assert.AreEqual(greeting + "\n今天是白露哦。\n黄历内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    solarAlmanac));
            Assert.AreEqual(greeting + "\n今天是白露哦。",
                DailyBriefingComposer.Compose(DayPart.Afternoon, solarOnly));
            Assert.AreEqual(greeting + "\n天气内容。\n黄历内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    weatherAlmanac));
            Assert.AreEqual(greeting + "\n天气内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    weatherOnly));
            Assert.AreEqual(greeting + "\n黄历内容。",
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    almanacOnly));
            Assert.AreEqual(greeting + "\n精选。\n星座。",
                DailyBriefingComposer.Compose(DayPart.Afternoon, fallback));
            Assert.IsTrue(new[] { solarWeatherAlmanac, solarWeather,
                solarAlmanac, solarOnly, weatherAlmanac, weatherOnly,
                almanacOnly, fallback }.All(
                content =>
                DailyBriefingComposer.SelectSupplementary(content).Length <=
                    2));
        }

        private static WeatherMeaning? SelectWeather(
            WeatherDaySummary yesterday, WeatherDaySummary today)
        {
            return WeatherMeaningRules.Select(new WeatherForecastWindow(
                yesterday, today, null));
        }

        private static WeatherDaySummary WeatherDay(double minimumTemperature,
            double maximumTemperature, double minimumApparent,
            double maximumApparent, double precipitation,
            double precipitationProbability, int likelyHours,
            int? firstLikelyHour, double maximumWindGust, bool hasSnow)
        {
            return new WeatherDaySummary(new DateTime(2026, 9, 1),
                minimumTemperature, maximumTemperature, minimumApparent,
                maximumApparent, precipitationProbability, precipitation,
                hasSnow ? 1D : 0D, Math.Min(30D, maximumWindGust),
                maximumWindGust, firstLikelyHour, firstLikelyHour,
                likelyHours, hasSnow);
        }

        private static void AssertAlmanacMapping(string raw,
            AlmanacTopic expected)
        {
            AlmanacTopic actual;
            Assert.IsTrue(AlmanacSemanticCatalog.TryMap(raw, out actual), raw);
            Assert.AreEqual(expected, actual, raw);
        }

        private static void AssertOracleTerm(int year, int month, int day,
            SolarTerm term, int longitude, TimeSpan offset)
        {
            SolarTermInfo? info = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(year, month, day, 12, 0, 0, offset));
            Assert.IsTrue(info.HasValue,
                year + "-" + month + "-" + day);
            Assert.AreEqual(term, info.Value.Term);
            Assert.AreEqual(longitude, info.Value.LongitudeDegrees);
            DateTimeOffset localInstant = info.Value.InstantUtc.ToOffset(offset);
            Assert.AreEqual(year, localInstant.Year);
            Assert.AreEqual(month, localInstant.Month);
            Assert.AreEqual(day, localInstant.Day);
        }

        [TestMethod]
        public void KeyDisplayAccumulator_AggregatesOnlyWithinItsTimeWindows()
        {
            KeyDisplayAccumulator accumulator = new KeyDisplayAccumulator();
            DateTime now = new DateTime(2030, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);

            Assert.AreEqual("A", accumulator.Register("A", now));
            Assert.AreEqual("A*3", accumulator.Register("A",
                now.AddMilliseconds(200), 2));
            Assert.AreEqual("A", accumulator.Register("A",
                now.AddSeconds(2)));
            Assert.AreEqual("A*8", accumulator.RegisterAbsolute("A",
                now.AddMilliseconds(2200), 8));
            Assert.AreEqual("B*4", accumulator.RegisterAbsolute("B",
                now.AddMilliseconds(2300), 4));
            accumulator.Reset();
            Assert.AreEqual("B", accumulator.Register("B",
                now.AddMilliseconds(2400)));
        }

        [TestMethod]
        public void SettingsCodec_RoundTripsCurrentFormatAndReminders()
        {
            PetSettingsData source = new PetSettingsData
            {
                HasLocation = true,
                X = -120,
                Y = 340,
                StartupPreferenceInitialized = true,
                StartAtLogin = false,
                ScalePercent = 170,
                ShowKeyOverlay = true,
                KeyboardPrivacyNoticeAccepted = true,
                KeyOverlayScalePercent = 150,
                SilentMode = true,
                DailyContentEnabled = false,
                SolarTermEnabled = false,
                WeatherEnabled = true,
                WeatherLocationName = "武汉",
                WeatherLocationAdmin1 = "湖北",
                WeatherLocationCountry = "中国",
                WeatherLatitude = 30.5928,
                WeatherLongitude = 114.3055,
                WeatherTimezone = "Asia/Shanghai",
                ZodiacSign = ZodiacSign.Scorpio,
                LastDailyBriefingDate = "20350405"
            };
            source.Reminders.Add(new ReminderItem(
                new DateTime(2035, 4, 5, 6, 7, 8, DateTimeKind.Utc),
                "喝水", "note-42", 24F, true));

            List<string> serialized = PetSettingsCodec.Serialize(source);
            PetSettingsData restored = PetSettingsCodec.Parse(serialized);
            int dailyDateLines = 0;
            int zodiacLines = 0;
            foreach (string line in serialized)
            {
                if (line.StartsWith("LastDailyBriefingDate=",
                    StringComparison.Ordinal)) dailyDateLines++;
                if (line.StartsWith("ZodiacSign=",
                    StringComparison.Ordinal)) zodiacLines++;
            }

            Assert.IsTrue(restored.HasLocation);
            Assert.AreEqual(-120, restored.X);
            Assert.AreEqual(340, restored.Y);
            Assert.IsFalse(restored.StartAtLogin);
            Assert.AreEqual(170, restored.ScalePercent);
            Assert.IsTrue(restored.ShowKeyOverlay);
            Assert.IsTrue(restored.KeyboardPrivacyNoticeAccepted);
            Assert.AreEqual(150, restored.KeyOverlayScalePercent);
            Assert.IsTrue(restored.SilentMode);
            Assert.IsFalse(restored.DailyContentEnabled);
            Assert.IsFalse(restored.SolarTermEnabled);
            Assert.IsTrue(restored.WeatherEnabled);
            Assert.AreEqual("武汉", restored.WeatherLocationName);
            Assert.AreEqual("湖北", restored.WeatherLocationAdmin1);
            Assert.AreEqual("中国", restored.WeatherLocationCountry);
            Assert.AreEqual(30.5928, restored.WeatherLatitude, 0.000001);
            Assert.AreEqual(114.3055, restored.WeatherLongitude, 0.000001);
            Assert.AreEqual("Asia/Shanghai", restored.WeatherTimezone);
            Assert.AreEqual(ZodiacSign.Scorpio, restored.ZodiacSign);
            Assert.AreEqual("20350405", restored.LastDailyBriefingDate);
            Assert.AreEqual(1, dailyDateLines);
            Assert.AreEqual(1, zodiacLines);
            Assert.AreEqual(1, restored.Reminders.Count);
            Assert.AreEqual("喝水", restored.Reminders[0].Text);
            Assert.AreEqual("note-42", restored.Reminders[0].SourceNoteId);
            Assert.AreEqual(480, restored.Reminders[0].FontSizeTwips);
            Assert.IsTrue(restored.Reminders[0].PreAlertEnabled);
        }

        [TestMethod]
        public void SettingsCodec_LoadsLegacyReminderAndNormalizesScales()
        {
            DateTime deadline = new DateTime(2036, 2, 3, 4, 5, 6,
                DateTimeKind.Utc);
            string encodedText = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("旧提醒"));

            PetSettingsData restored = PetSettingsCodec.Parse(new string[]
            {
                "StartWithWindows=0",
                "ScalePercent=47",
                "KeyOverlayScalePercent=140",
                "ReminderUtcTicks=" + deadline.Ticks,
                "ReminderTextBase64=" + encodedText
            });

            Assert.IsFalse(restored.StartAtLogin);
            Assert.AreEqual(50, restored.ScalePercent);
            Assert.AreEqual(150, restored.KeyOverlayScalePercent);
            Assert.IsTrue(restored.DailyContentEnabled);
            Assert.IsTrue(restored.SolarTermEnabled);
            Assert.IsFalse(restored.WeatherEnabled);
            Assert.AreEqual(ZodiacSign.None, restored.ZodiacSign);
            Assert.AreEqual(1, restored.Reminders.Count);
            Assert.AreEqual("旧提醒", restored.Reminders[0].Text);
            Assert.AreEqual(deadline, restored.Reminders[0].DeadlineUtc);
        }

        [TestMethod]
        public void SettingsCodec_RejectsContentWithNoKnownFields()
        {
            bool rejected = false;
            try
            {
                PetSettingsCodec.Parse(new string[] { "unknown=value" });
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Assert.IsTrue(rejected);
            Assert.IsFalse(new PetSettingsData().StartAtLogin);
            Assert.IsTrue(new PetSettingsData().DailyContentEnabled);
            Assert.IsTrue(new PetSettingsData().SolarTermEnabled);
            Assert.IsFalse(new PetSettingsData().WeatherEnabled);
            Assert.AreEqual(ZodiacSign.None,
                new PetSettingsData().ZodiacSign);
        }

        [TestMethod]
        public void SettingsData_CopyFromPreservesDailyContentPreferences()
        {
            PetSettingsData source = new PetSettingsData
            {
                DailyContentEnabled = false,
                SolarTermEnabled = false,
                WeatherEnabled = true,
                WeatherLocationName = "香港",
                WeatherLocationAdmin1 = "香港",
                WeatherLocationCountry = "中国",
                WeatherLatitude = 22.3193,
                WeatherLongitude = 114.1694,
                WeatherTimezone = "Asia/Hong_Kong",
                ZodiacSign = ZodiacSign.Taurus,
                LastDailyBriefingDate = "20350908"
            };
            PetSettingsData target = new PetSettingsData();

            target.CopyFrom(source);

            Assert.IsFalse(target.DailyContentEnabled);
            Assert.IsFalse(target.SolarTermEnabled);
            Assert.IsTrue(target.WeatherEnabled);
            Assert.AreEqual("香港", target.WeatherLocationName);
            Assert.AreEqual(22.3193, target.WeatherLatitude, 0.000001);
            Assert.AreEqual("Asia/Hong_Kong", target.WeatherTimezone);
            Assert.AreEqual(ZodiacSign.Taurus, target.ZodiacSign);
            Assert.AreEqual("20350908", target.LastDailyBriefingDate);
        }

        [TestMethod]
        public void SettingsCodec_AlmanacDefaultsTrueAndRoundTripsFalse()
        {
            Assert.IsTrue(new PetSettingsData().AlmanacEnabled);
            Assert.IsTrue(PetSettingsCodec.Parse(new[] { "DailyContentEnabled=1" })
                .AlmanacEnabled);

            PetSettingsData disabled = new PetSettingsData
            {
                AlmanacEnabled = false
            };
            Assert.IsFalse(PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(disabled)).AlmanacEnabled);

            PetSettingsData target = new PetSettingsData();
            target.CopyFrom(disabled);
            Assert.IsFalse(target.AlmanacEnabled);
        }

        [TestMethod]
        public void SettingsCodec_NormalizesInvalidAndRoundTripsPisces()
        {
            PetSettingsData pisces = new PetSettingsData
            {
                ZodiacSign = ZodiacSign.Pisces
            };
            Assert.AreEqual(ZodiacSign.Pisces, PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(pisces)).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None, PetSettingsCodec.Parse(
                new string[] { "ZodiacSign=999" }).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None, PetSettingsCodec.Parse(
                new string[] { "ZodiacSign=Scorpio" }).ZodiacSign);
            Assert.AreEqual(ZodiacSign.None,
                PetSettingRules.NormalizeZodiacSign((ZodiacSign)(-1)));
        }

        [TestMethod]
        public void WeatherLocation_ValidatesCoordinatesAndBuildsStableDisplay()
        {
            WeatherLocation location;
            Assert.IsTrue(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30.5928, 114.3055, "Asia/Shanghai", out location));
            Assert.AreEqual("武汉 · 湖北 · 中国", location.DisplayName);
            Assert.IsTrue(location.StableKey.Contains("Asia/Shanghai"));

            WeatherLocation invalid;
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                91D, 114D, "Asia/Shanghai", out invalid));
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30D, -181D, "Asia/Shanghai", out invalid));
            Assert.IsFalse(WeatherLocation.TryCreate("武汉", "湖北", "中国",
                30D, 114D, "", out invalid));
        }

        [TestMethod]
        public void SettingsCodec_InvalidWeatherLocationFailsClosed()
        {
            string encodedName = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("武汉"));
            string encodedTimezone = Convert.ToBase64String(
                Encoding.UTF8.GetBytes("Asia/Shanghai"));
            PetSettingsData restored = PetSettingsCodec.Parse(new[]
            {
                "WeatherEnabled=1",
                "WeatherLocationNameBase64=" + encodedName,
                "WeatherLatitude=200",
                "WeatherLongitude=114.3055",
                "WeatherTimezoneBase64=" + encodedTimezone
            });

            Assert.IsFalse(restored.WeatherEnabled);
        }
    }
}
