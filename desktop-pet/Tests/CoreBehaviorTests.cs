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
        public void PetStartupRules_ReleaseLoadingOnlyWhenReady()
        {
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(false, false));
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(true, false));
            Assert.IsFalse(PetStartupRules.CanReleaseStartupLoading(false, true));
            Assert.IsTrue(PetStartupRules.CanReleaseStartupLoading(true, true));
        }

        [TestMethod]
        public void DailyNoteFeature_ProgressesDaysAndDetectsMissedDay()
        {
            DailyNoteEntry entry = new DailyNoteEntry("day", "body");
            DateTime firstDate = new DateTime(2026, 8, 28);
            DailyNoteProgress progress = new DailyNoteProgress();

            DailyNoteAction first = DailyNoteFeature.Decide(firstDate,
                progress, entry);
            Assert.AreEqual(DailyNoteActionKind.Create, first.Kind);
            Assert.AreEqual(1, first.DayNumber);

            DailyNoteProgress issued = DailyNoteFeature.MarkIssued(
                progress, firstDate, first.DayNumber);
            DailyNoteAction sameDay = DailyNoteFeature.Decide(firstDate,
                issued, entry);
            Assert.AreEqual(DailyNoteActionKind.AlreadyIssued, sameDay.Kind);

            DailyNoteAction next = DailyNoteFeature.Decide(
                firstDate.AddDays(1), issued, entry);
            Assert.AreEqual(DailyNoteActionKind.Create, next.Kind);
            Assert.AreEqual(2, next.DayNumber);

            DailyNoteAction missed = DailyNoteFeature.Decide(
                firstDate.AddDays(3), issued, entry);
            Assert.AreEqual(DailyNoteActionKind.MissedDay, missed.Kind);
            Assert.AreEqual(2, missed.DayNumber);
        }

        [TestMethod]
        public void DailyNoteFeature_CompletesAfterThirtyDays()
        {
            DailyNoteProgress progress = new DailyNoteProgress
            {
                IssuedDay = 30,
                LastIssuedLocalDate = new DateTime(2026, 9, 26),
                Completed = true
            };
            DailyNoteAction action = DailyNoteFeature.Decide(
                new DateTime(2026, 9, 27), progress, null);

            Assert.AreEqual(DailyNoteActionKind.ProgramComplete, action.Kind);
            Assert.AreEqual(30, action.DayNumber);
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
        public void ZodiacDailyCatalog_HasCompleteUniqueBundledCopy()
        {
            Assert.AreEqual(0,
                ZodiacDailyCatalog.GetLines(ZodiacSign.None).Length);
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                ZodiacSign sign = (ZodiacSign)value;
                string[] lines = ZodiacDailyCatalog.GetLines(sign);
                Assert.AreEqual(6, lines.Length, sign.ToString());
                Assert.IsTrue(lines.All(line =>
                    !String.IsNullOrWhiteSpace(line)), sign.ToString());
                Assert.AreEqual(lines.Length, lines.Distinct().Count(),
                    sign.ToString());
            }
        }

        [TestMethod]
        public void ZodiacDailySelector_IsDeterministicAndUsesOwnCatalog()
        {
            DateTimeOffset localNow = new DateTimeOffset(2026, 9, 1,
                12, 0, 0, TimeSpan.FromHours(8));
            Assert.IsNull(ZodiacDailySelector.Select(ZodiacSign.None,
                localNow));
            Assert.IsNull(ZodiacDailySelector.Select((ZodiacSign)999,
                localNow));

            string scorpio = ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                localNow);
            for (int i = 0; i < 10; i++)
                Assert.AreEqual(scorpio, ZodiacDailySelector.Select(
                    ZodiacSign.Scorpio, localNow));
            Assert.IsTrue(ZodiacDailyCatalog.GetLines(ZodiacSign.Scorpio)
                .Contains(scorpio));

            HashSet<string> month = new HashSet<string>();
            for (int day = 0; day < 30; day++)
                month.Add(ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                    localNow.AddDays(day)));
            Assert.IsTrue(month.Count > 1);

            foreach (ZodiacSign sign in new[] { ZodiacSign.Aries,
                ZodiacSign.Scorpio, ZodiacSign.Pisces })
                Assert.IsTrue(ZodiacDailyCatalog.GetLines(sign).Contains(
                    ZodiacDailySelector.Select(sign, localNow)));
        }

        [TestMethod]
        public void ZodiacDailySelector_UsesLocalCivilDateAcrossOffsets()
        {
            DateTimeOffset sameInstant = new DateTimeOffset(2026, 9, 1,
                16, 30, 0, TimeSpan.Zero);
            DateTimeOffset hongKong = sameInstant.ToOffset(
                TimeSpan.FromHours(8));
            DateTimeOffset pacific = sameInstant.ToOffset(
                TimeSpan.FromHours(-8));
            Assert.AreEqual(2, hongKong.Day);
            Assert.AreEqual(1, pacific.Day);
            Assert.AreNotEqual(ZodiacDailySelector.Select(
                    ZodiacSign.Scorpio, hongKong),
                ZodiacDailySelector.Select(ZodiacSign.Scorpio, pacific));
            Assert.AreEqual(ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                    new DateTimeOffset(2026, 9, 1, 1, 0, 0,
                        TimeSpan.FromHours(8))),
                ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                    new DateTimeOffset(2026, 9, 1, 23, 0, 0,
                        TimeSpan.FromHours(-5))));
        }

        [TestMethod]
        public void DailyBriefingComposer_ComposesOptionalFactsInOrder()
        {
            string greeting = DailyContentRules.GreetingFor(
                DayPart.Afternoon);
            const string zodiac =
                "天蝎座今天的小提示：先把最重要的一件事处理好。";
            Assert.AreEqual(greeting, DailyBriefingComposer.Compose(
                DayPart.Afternoon, null, null));

            SolarTermInfo? whiteDew = new SolarTermInfo(SolarTerm.WhiteDew,
                "白露", 165, new DateTimeOffset(2026, 9, 7, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            Assert.AreEqual(greeting + "\n今天是白露哦。",
                DailyBriefingComposer.Compose(DayPart.Afternoon, whiteDew,
                    null));
            Assert.AreEqual(greeting + "\n" + zodiac,
                DailyBriefingComposer.Compose(DayPart.Afternoon, null,
                    zodiac));
            Assert.AreEqual(greeting + "\n今天是白露哦。\n" + zodiac,
                DailyBriefingComposer.Compose(DayPart.Afternoon, whiteDew,
                    zodiac));
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
                ZodiacSign = ZodiacSign.Taurus,
                LastDailyBriefingDate = "20350908"
            };
            PetSettingsData target = new PetSettingsData();

            target.CopyFrom(source);

            Assert.IsFalse(target.DailyContentEnabled);
            Assert.IsFalse(target.SolarTermEnabled);
            Assert.AreEqual(ZodiacSign.Taurus, target.ZodiacSign);
            Assert.AreEqual("20350908", target.LastDailyBriefingDate);
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
    }
}
