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
        public void CoreAssembly_DoesNotReferencePlatformUiOrIntegration()
        {
            HashSet<string> forbidden = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "System.Drawing",
                "System.Drawing.Common",
                "System.Drawing.Primitives",
                "System.Windows.Forms",
                "WindowsBase",
                "PresentationCore",
                "PresentationFramework",
                "System.Xaml",
                "WindowsFormsIntegration",
                "Microsoft.Win32.Registry",
                "UIAutomationClient",
                "UIAutomationTypes"
            };
            foreach (System.Reflection.AssemblyName reference in
                typeof(PetSettingsData).Assembly.GetReferencedAssemblies())
                Assert.IsFalse(forbidden.Contains(reference.Name),
                    "PennyPet.Core must not reference " + reference.Name);
        }

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

            StickyDockOperations.SetGroupAlwaysOnTop(merged, true);
            foreach (StickyNoteData note in merged)
                Assert.IsTrue(note.AlwaysOnTop);
        }

        [TestMethod]
        public void StartupRestorePlanner_QueuesEachVisibleDockComponentOnce()
        {
            StickyNoteData first = new StickyNoteData { Id = "first" };
            StickyNoteData hidden = new StickyNoteData
            {
                Id = "hidden",
                Visible = false
            };
            StickyNoteData last = new StickyNoteData { Id = "last" };
            StickyNoteData standalone = new StickyNoteData
            {
                Id = "standalone"
            };
            List<StickyNoteData> notes = new List<StickyNoteData>
            {
                first, hidden, last, standalone
            };
            StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[]
            {
                first, hidden, last
            });

            List<StickyNoteData> seeds =
                StartupRestorePlanner.BuildVisibleRestoreSeeds(notes);

            CollectionAssert.AreEqual(new StickyNoteData[]
            {
                first, standalone
            }, seeds);
            Assert.IsFalse(StartupRestorePlanner.CanReleaseLoading(true,
                false));
            Assert.IsFalse(StartupRestorePlanner.CanReleaseLoading(false,
                true));
            Assert.IsTrue(StartupRestorePlanner.CanReleaseLoading(true,
                true));
        }

        [TestMethod]
        public void DockGeometry_UsesPlatformNeutralValuesForLayoutAndHits()
        {
            List<DockRect> layout = DockGeometry.CalculateLayout(
                new DockSize[]
                {
                    new DockSize(320, 300),
                    new DockSize(500, 240),
                    new DockSize(380, 260)
                }, 120, 80, 460, 1);
            Assert.AreEqual(3, layout.Count);
            Assert.AreEqual(120, layout[0].Left);
            Assert.AreEqual(80, layout[0].Top);
            Assert.AreEqual(460, layout[0].Width);
            Assert.AreEqual(300, layout[0].Height);
            Assert.AreEqual(380, layout[1].Top);
            Assert.AreEqual(620, layout[2].Top);
            Assert.IsTrue(DockGeometry.CanDockBelow(
                new DockRect(100, 330, 320, 300),
                new DockRect(100, 30, 320, 300), 20));
            Assert.IsTrue(DockGeometry.CanDockBelow(
                new DockRect(80, 400, 900, 300),
                new DockRect(400, 100, 280, 300), 20));
            Assert.IsTrue(DockGeometry.IsCoordinateRangeSafe(100,
                new int[] { 700, 700, 700 }, 30000));
            Assert.IsFalse(DockGeometry.IsCoordinateRangeSafe(29000,
                new int[] { 700, 700 }, 30000));

            DockSize heights = DockGeometry.CalculateDividerHeights(
                300, 250, 300);
            Assert.AreEqual(250, heights.Width);
            Assert.AreEqual(350, heights.Height);
            DockPoint delta = DockGeometry.CalculateHeaderReachableTranslation(
                new DockRect(100, -200, 400, 32),
                new DockRect(0, 0, 1200, 900));
            Assert.AreEqual(0, delta.X);
            Assert.AreEqual(200, delta.Y);
        }

        [TestMethod]
        public void DockGeometry_RecoversStickyComponentsWithoutScreenTypes()
        {
            List<DockRect> layout = DockGeometry.CalculateRecoveryLayout(
                new DockRect(0, 0, 1920, 1040), new DockSize[]
                {
                    new DockSize(320, 300), new DockSize(320, 300),
                    new DockSize(700, 600)
                }, 1);
            Assert.AreEqual(3, layout.Count);
            Assert.AreEqual(631, layout[0].Left);
            Assert.AreEqual(969, layout[1].Left);
            Assert.IsTrue(layout[2].Top > layout[0].Top);

            DockPoint anchor = DockGeometry.CalculateRecoveryAnchor(
                new DockRect(-1920, 0, 1920, 1040),
                new DockRect(-300, 700, 192, 208),
                new DockSize(320, 300), 1);
            Assert.IsTrue(anchor.X >= -1920 && anchor.X <= -320);
            Assert.IsTrue(anchor.Y >= 0 && anchor.Y <= 1008);
        }

        [TestMethod]
        public void DockGeometry_PlacesNewWindowsAndSideTabsWithoutUiTypes()
        {
            DockRect work = new DockRect(0, 0, 1920, 1040);
            DockRect pet = new DockRect(20, 900, 192, 208);
            DockPoint saved = DockGeometry.CalculateCascadedWindowLocation(
                work, pet, new DockSize(320, 300), 8, 12);
            DockPoint shown = DockGeometry.CalculateCascadedWindowLocation(
                work, pet, new DockSize(320, 300), 8, 0);
            Assert.AreEqual(242, saved.X);
            Assert.AreEqual(728, saved.Y);
            Assert.AreEqual(740, shown.Y);

            int overlap = DockGeometry.CalculateSideTabOverlap(192);
            DockPoint left = DockGeometry.CalculateSideTabLocation(
                new DockRect(500, 100, 192, 208), work,
                new DockSize(146, 178), true, overlap, 0);
            DockPoint right = DockGeometry.CalculateSideTabLocation(
                new DockRect(500, 100, 192, 208), work,
                new DockSize(146, 178), false, overlap, 10);
            Assert.AreEqual(386, left.X);
            Assert.AreEqual(650, right.X);
            Assert.AreEqual(115, left.Y);
            Assert.AreEqual(5, DockGeometry.CalculateLeftSideTabCount(
                9, 208, 1080, 34, 2));
            DockPoint petWithTabs = DockGeometry
                .CalculatePetLocationWithSideTabs(
                    new DockRect(0, 900, 192, 208), work, 116, 116);
            Assert.AreEqual(116, petWithTabs.X);
            Assert.AreEqual(832, petWithTabs.Y);

            DockPoint popup = DockGeometry.CalculatePopupLocation(
                new DockRect(300, 100, 320, 300),
                new DockSize(520, 260), new DockRect(0, 0, 1200, 900), 8);
            Assert.AreEqual(200, popup.X);
            Assert.AreEqual(408, popup.Y);
            DockRect recovered = DockGeometry.CalculateRecoveredDragBounds(
                new DockRect(100, 100, 320, 300),
                new DockRect(0, 0, 1920, 1080),
                new DockPoint(500, 400), new DockPoint(20, 10), true);
            Assert.AreEqual(480, recovered.Left);
            Assert.AreEqual(390, recovered.Top);
            Assert.AreEqual(320, recovered.Width);
            Assert.AreEqual(300, recovered.Height);
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
        public void StickyLinkDetector_RecognizesWebAddressesWithoutFileSyntax()
        {
            IList<StickyLinkMatch> links = StickyNoteLinkDetector
                .FindWebAddresses(
                "C:\\Users\\Penny pet\\进度表.xlsx。\r\n" +
                "\\\\server\\share\\项目文件.pdf\r\n" +
                "www.example.com/docs?q=1，\r\n" +
                "(https://openai.com/research).\r\n" +
                "namewww.example.com ftp://example.com");

            Assert.AreEqual(2, links.Count);
            Assert.AreEqual("https://www.example.com/docs?q=1",
                links[0].Target.TrimEnd('/'));
            Assert.IsFalse(links[0].IsFileTarget);
            Assert.AreEqual("https://openai.com/research",
                links[1].Target.TrimEnd('/'));
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

            List<string> serialized = PetSettingsCodec.Serialize(source);
            CollectionAssert.Contains(serialized, "StartWithWindows=0");
            CollectionAssert.DoesNotContain(serialized, "StartAtLogin=0");
            PetSettingsData restored = PetSettingsCodec.Parse(serialized);

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
