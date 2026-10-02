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
        public void StickyDockGeometry_PetSideSpawnPrefersLeftThenClamps()
        {
            DockRect left = StickyDockGeometry.CalculatePetSideSpawnLocal(
                new DockRect { Left = 400, Top = 200, Width = 192,
                    Height = 208 },
                new DockRect { Left = 0, Top = 0, Width = 1000,
                    Height = 800 },
                new DockSize { Width = 320, Height = 300 }, 12);
            Assert.AreEqual(68, left.Left);
            Assert.AreEqual(200, left.Top);

            DockRect right = StickyDockGeometry.CalculatePetSideSpawnLocal(
                new DockRect { Left = 20, Top = 200, Width = 192,
                    Height = 208 },
                new DockRect { Left = 0, Top = 0, Width = 600,
                    Height = 800 },
                new DockSize { Width = 320, Height = 300 }, 12);
            Assert.AreEqual(224, right.Left);
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
                Id = "middle"
            };
            StickyNoteData tail = new StickyNoteData
            {
                Id = "tail"
            };

            StickyDockGroups.ApplyOrderedGroup(new[] { root, middle, tail });
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
            Assert.AreSame(first, StickyDockGroups.GetVisibleNeighbor(ordered, last, -1));
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
            Assert.AreSame(middle, StickyDockGroups.GetVisibleNeighbor(merged, extracted, -1));
            Assert.AreSame(extracted, StickyDockGroups.GetVisibleNeighbor(merged, last, -1));

            extracted.Visible = false;
            Assert.IsFalse(extracted.Visible);
            Assert.AreEqual(2, extracted.DockGroupOrder);
            Assert.AreSame(middle, StickyDockGroups.GetVisibleNeighbor(merged, last, -1));
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
        }
    }
}
