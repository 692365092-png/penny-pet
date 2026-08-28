using System;
using System.Collections.Generic;
using System.IO;
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
        public void StickyNoteWindowRules_KeepTabsTopMostOnlyWithoutVisibleNotes()
        {
            Assert.IsTrue(
                StickyNoteWindowRules.ShouldKeepSideTabsTopMost(false));
            Assert.IsFalse(
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
        public void StickyDockGeometry_DividerRulesMatchWindowsResults()
        {
            DockSize middle =
                StickyDockGeometry.CalculateDockDividerHeights(300, 250, 300);
            DockSize minimum =
                StickyDockGeometry.CalculateDockDividerHeights(300, 430, 300);
            DockSize range =
                StickyDockGeometry.CalculateDockDividerRange(300, 300);
            DockSize tallRange =
                StickyDockGeometry.CalculateDockDividerRange(500, 500);

            Assert.AreEqual(250, middle.Width);
            Assert.AreEqual(350, middle.Height);
            Assert.AreEqual(380, minimum.Width);
            Assert.AreEqual(220, minimum.Height);
            Assert.AreEqual(220, range.Width);
            Assert.AreEqual(380, range.Height);
            Assert.AreEqual(300, tallRange.Width);
            Assert.AreEqual(700, tallRange.Height);
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
            Assert.IsFalse(PetAnimationController.MovementStartsDrag(4, 4));
            Assert.IsTrue(PetAnimationController.MovementStartsDrag(6, 0));
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
                SilentMode = true
            };
            source.Reminders.Add(new ReminderItem(
                new DateTime(2035, 4, 5, 6, 7, 8, DateTimeKind.Utc),
                "喝水", "note-42", 24F, true));

            PetSettingsData restored = PetSettingsCodec.Parse(
                PetSettingsCodec.Serialize(source));

            Assert.IsTrue(restored.HasLocation);
            Assert.AreEqual(-120, restored.X);
            Assert.AreEqual(340, restored.Y);
            Assert.IsFalse(restored.StartAtLogin);
            Assert.AreEqual(170, restored.ScalePercent);
            Assert.IsTrue(restored.ShowKeyOverlay);
            Assert.IsTrue(restored.KeyboardPrivacyNoticeAccepted);
            Assert.AreEqual(150, restored.KeyOverlayScalePercent);
            Assert.IsTrue(restored.SilentMode);
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
        }
    }
}
