using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace PennyPet
{
    // Full regression orchestration and report generation. Individual probes
    // and preview renderers remain in PennySelfTests.cs.
    internal static partial class SelfTest
    {
        public static void Run(string outputPath)
        {
            try
            {
                int width = 0;
                int height = 0;
                bool innerOutlineOk = false;
                bool greenHaloOk = false;
                int externalOutlinePixels = 0;
                bool atlasOk = false;
                bool startupLazyArtLoadOk = false;
                bool interactionArtPreloadOk = false;
                bool notificationTriggerPlaybackOk = false;
                int[] actualAnimationCycleDurations = new int[
                    PetArtPackage.RuntimeStateNames.Length];
                bool animationTimingFromArtPackageOk = true;
                bool startupCacheEmbeddedOk =
                    PetArtPackage.HasEmbeddedStartupCacheForTest;
                using (PetArtPackage art = PetArtPackage.Load(192, 208))
                {
                    Bitmap rendered = art.GetFrame(0, 0);
                    startupLazyArtLoadOk = art.LoadedRuntimeStateCount == 1;
                    interactionArtPreloadOk = !art.IsRowLoaded(4);
                    art.PreloadRow(4);
                    interactionArtPreloadOk = interactionArtPreloadOk &&
                        art.IsRowLoaded(4) && art.LoadedRuntimeStateCount == 2;
                    art.PreloadRow(9);
                    notificationTriggerPlaybackOk = art.IsRowLoaded(9) &&
                        art.GetFrame(9, 0) != null &&
                        PetAnimationController.AttentionAnimationRow(true) == 9 &&
                        PetAnimationController.AttentionAnimationRow(false) == 0;
                    width = rendered.Width;
                    height = rendered.Height;
                    atlasOk = width == 192 && height == 208;
                    for (int row = 0; row < PetArtPackage.RuntimeStateNames.Length;
                        row++)
                    {
                        actualAnimationCycleDurations[row] =
                            art.CycleDuration(row);
                        animationTimingFromArtPackageOk =
                            animationTimingFromArtPackageOk &&
                            actualAnimationCycleDurations[row] > 0;
                        atlasOk = atlasOk && art.FrameCount(row) > 0 &&
                            actualAnimationCycleDurations[row] > 0;
                    }
                    int greenPixels = 0;
                    for (int y = 0; y < rendered.Height; y++)
                    {
                        for (int x = 0; x < rendered.Width; x++)
                        {
                            Color pixel = rendered.GetPixel(x, y);
                            if (pixel.A >= 16 &&
                                TouchesTransparency(rendered, x, y) &&
                                pixel.G > pixel.R + 40 &&
                                pixel.G > pixel.B + 40)
                                greenPixels++;
                        }
                    }
                    innerOutlineOk = rendered.PixelFormat ==
                        PixelFormat.Format32bppPArgb;
                    greenHaloOk = greenPixels == 0;
                }
                ArtPreloadReservations preloadReservations =
                    new ArtPreloadReservations();
                DateTime preloadNow = DateTime.UtcNow;
                bool failedArtPreloadRetryOk =
                    preloadReservations.TryReserve(7, false, preloadNow);
                preloadReservations.Complete(7, false, preloadNow);
                failedArtPreloadRetryOk = failedArtPreloadRetryOk &&
                    !preloadReservations.TryReserve(7, false,
                        preloadNow.AddMilliseconds(200)) &&
                    preloadReservations.TryReserve(7, false,
                        preloadNow.AddSeconds(2));
                preloadReservations.Complete(7, true,
                    preloadNow.AddSeconds(2));
                bool applicationIconEmbeddedOk = false;
                using (Icon applicationIcon = Icon.ExtractAssociatedIcon(
                    Assembly.GetExecutingAssembly().Location))
                {
                    applicationIconEmbeddedOk = applicationIcon != null &&
                        applicationIcon.Width >= 16 && applicationIcon.Height >= 16;
                }
                bool startupLoadingFrameEmbeddedOk =
                    StartupLoadingForm.HasEmbeddedFrame;
                bool startupLoadingUsesSavedScaleOk;
                PetSettings loadingScaleSettings = new PetSettings();
                loadingScaleSettings.ScalePercent = 150;
                using (StartupLoadingForm loadingScaleForm =
                    new StartupLoadingForm(loadingScaleSettings))
                    startupLoadingUsesSavedScaleOk =
                        loadingScaleForm.UsesPetScaleForTest(150);
                bool contactArtworkEmbeddedOk;
                using (Stream contactArtwork = typeof(ContactAuthorForm).Assembly
                    .GetManifestResourceStream("PennyPet.ContactAuthor.Image"))
                    contactArtworkEmbeddedOk = contactArtwork != null;
                bool contactAuthorFeatureOk;
                using (ContactAuthorForm contact = new ContactAuthorForm())
                {
                    contactAuthorFeatureOk = contactArtworkEmbeddedOk &&
                        contact.CopyAndArtworkBehaviorConfigured &&
                        contact.DisplayedXiaohongshuNumber ==
                            ContactAuthorForm.XiaohongshuNumber &&
                        ContactAuthorForm.XiaohongshuProfileUrl ==
                            "https://www.xiaohongshu.com/user/profile/" +
                            "59bd4b0b51783a7612f6fc43" &&
                        ContactAuthorForm.XiaohongshuProfileUrl.IndexOf('?') < 0 &&
                        contact.XiaohongshuOnlyLayoutForTest;
                }
                ReminderSchedule schedule = new ReminderSchedule();
                schedule.Set(TimeSpan.FromMinutes(1), "test");
                bool minuteOk = schedule.Active &&
                    schedule.DeadlineUtc > DateTime.UtcNow.AddSeconds(55);
                schedule.Cancel();
                bool cancelOk = !schedule.Active && schedule.Text == String.Empty;
                DateTime reminderBaseUtc = DateTime.UtcNow.AddDays(1);
                for (int i = 0; i < ReminderSchedule.MaximumItems; i++)
                {
                    if (i == 0)
                        schedule.Add(reminderBaseUtc.AddMinutes(i),
                            "reminder-" + i, "note-0", 24F, true);
                    else
                        schedule.Add(reminderBaseUtc.AddMinutes(i),
                            "reminder-" + i, null);
                }
                bool fiveRemindersOk = schedule.Count == 5 &&
                    schedule.GetItems()[0].Text == "reminder-0";
                bool sixthReminderBlocked = false;
                try
                {
                    schedule.Add(reminderBaseUtc.AddHours(1), "sixth");
                }
                catch (InvalidOperationException)
                {
                    sixthReminderBlocked = true;
                }
                PetSettings memorySettings = new PetSettings();
                memorySettings.SetReminders(schedule.GetItems());
                ReminderSchedule restored = new ReminderSchedule();
                restored.Restore(memorySettings.Reminders);
                bool reminderMemoryOk = restored.Count == 5 &&
                    restored.GetItems()[4].Text == "reminder-4";
                ReminderItem editable = restored.GetItems()[0];
                ReminderItem edited = restored.Replace(editable,
                    reminderBaseUtc.AddHours(2), "edited reminder", 26F, false);
                bool reminderReplaceOk = restored.Count == 5 &&
                    edited.Text == "edited reminder" &&
                    edited.SourceNoteId == "note-0" &&
                    edited.FontSizeTwips == 520 &&
                    !restored.GetItems().Contains(editable);
                string persistenceTestPath = outputPath + ".settings-test.ini";
                memorySettings.StartupPreferenceInitialized = true;
                memorySettings.StartWithWindows = false;
                memorySettings.ScalePercent = 170;
                memorySettings.ShowKeyOverlay = false;
                memorySettings.KeyboardPrivacyNoticeAccepted = true;
                memorySettings.KeyOverlayScalePercent = 150;
                memorySettings.SilentMode = true;
                memorySettings.SaveToFile(persistenceTestPath);
                PetSettings diskSettings = PetSettings.LoadFromFile(persistenceTestPath);
                reminderMemoryOk = reminderMemoryOk && diskSettings.Reminders.Count == 5 &&
                    diskSettings.Reminders[0].Text == "reminder-0" &&
                    diskSettings.Reminders[0].SourceNoteId == "note-0" &&
                    diskSettings.Reminders[0].FontSizeTwips == 480 &&
                    diskSettings.Reminders[0].PreAlertEnabled &&
                    diskSettings.StartupPreferenceInitialized &&
                    !diskSettings.StartWithWindows &&
                    diskSettings.ScalePercent == 170 &&
                    !diskSettings.ShowKeyOverlay &&
                    diskSettings.KeyboardPrivacyNoticeAccepted &&
                    diskSettings.KeyOverlayScalePercent == 150;
                bool keyboardPrivacyNoticePersistenceOk =
                    diskSettings.KeyboardPrivacyNoticeAccepted;
                bool silentModePersistenceOk = diskSettings.SilentMode;
                File.Copy(persistenceTestPath, persistenceTestPath + ".bak", true);
                const string corruptSettingsPayload = "not-a-penny-setting";
                File.WriteAllText(persistenceTestPath, corruptSettingsPayload,
                    new UTF8Encoding(false));
                PetSettings recoveredSettings = PetSettings.LoadFromFile(
                    persistenceTestPath);
                recoveredSettings.SaveToFile(persistenceTestPath);
                string settingsDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(persistenceTestPath));
                string settingsName = Path.GetFileName(persistenceTestPath);
                string[] preservedSettings = Directory.GetFiles(settingsDirectory,
                    settingsName + ".corrupt-*");
                bool settingsBackupRecoveryOk =
                    recoveredSettings.ScalePercent == 170 &&
                    recoveredSettings.SilentMode &&
                    recoveredSettings.KeyboardPrivacyNoticeAccepted &&
                    preservedSettings.Length > 0 &&
                    File.ReadAllText(preservedSettings[0], Encoding.UTF8) ==
                        corruptSettingsPayload;
                if (File.Exists(persistenceTestPath)) File.Delete(persistenceTestPath);
                if (File.Exists(persistenceTestPath + ".bak"))
                    File.Delete(persistenceTestPath + ".bak");
                foreach (string preservedSetting in preservedSettings)
                    if (File.Exists(preservedSetting)) File.Delete(preservedSetting);
                bool linkedReminderOk = restored.FindBySourceNoteId("note-0") != null &&
                    restored.FindBySourceNoteId("missing") == null;
                ReminderSchedule sameNoteSchedule = new ReminderSchedule();
                ReminderItem laterLinked = sameNoteSchedule.Add(
                    reminderBaseUtc.AddMinutes(30), "later", "shared-note");
                ReminderItem earlierLinked = sameNoteSchedule.Add(
                    reminderBaseUtc.AddMinutes(10), "earlier", "shared-note");
                sameNoteSchedule.Add(reminderBaseUtc.AddMinutes(20),
                    "unrelated", "other-note");
                bool multipleLinkedReminderOk = Object.ReferenceEquals(
                    sameNoteSchedule.FindBySourceNoteId("shared-note"), earlierLinked) &&
                    sameNoteSchedule.RemoveBySourceNoteId("shared-note") == 2 &&
                    sameNoteSchedule.Count == 1 &&
                    sameNoteSchedule.FindBySourceNoteId("shared-note") == null &&
                    sameNoteSchedule.FindBySourceNoteId("other-note") != null;
                ReminderSchedule concreteSchedule = new ReminderSchedule();
                DateTime concreteLocal = DateTime.Now.AddMinutes(10);
                ReminderItem concrete = concreteSchedule.Add(
                    concreteLocal.ToUniversalTime(), "concrete");
                bool concreteDateTimeOk = Math.Abs(
                    (concrete.DeadlineUtc.ToLocalTime() - concreteLocal).TotalSeconds) < 1;
                ReminderItem enabledPreAlert = new ReminderItem(
                    DateTime.UtcNow.AddSeconds(20), "enabled", null, 10.5F, true);
                ReminderItem disabledPreAlert = new ReminderItem(
                    DateTime.UtcNow.AddSeconds(20), "disabled", null, 10.5F, false);
                ReminderSchedule preAlertGateSchedule = new ReminderSchedule();
                preAlertGateSchedule.Add(DateTime.UtcNow.AddMinutes(1),
                    "earlier-disabled", null, 10.5F, false);
                preAlertGateSchedule.Add(DateTime.UtcNow.AddMinutes(2),
                    "later-enabled", null, 10.5F, true);
                bool preAlertWindowOk =
                    PetReminderCoordinator.IsPreAlertWindow(
                        TimeSpan.FromSeconds(20)) &&
                    PetReminderCoordinator.IsPreAlertWindow(
                        TimeSpan.FromSeconds(1)) &&
                    !PetReminderCoordinator.IsPreAlertWindow(
                        TimeSpan.FromSeconds(21)) &&
                    !PetReminderCoordinator.IsPreAlertWindow(TimeSpan.Zero) &&
                    PetReminderCoordinator.ShouldShowPreAlert(enabledPreAlert,
                        TimeSpan.FromSeconds(20)) &&
                    !PetReminderCoordinator.ShouldShowPreAlert(disabledPreAlert,
                        TimeSpan.FromSeconds(20)) &&
                    preAlertGateSchedule.NextPreAlert != null &&
                    preAlertGateSchedule.NextPreAlert.Text == "later-enabled";
                bool reminderClockWhileEditingOk =
                    PetReminderCoordinator.ShouldRunReminderClock(false) &&
                    !PetReminderCoordinator.ShouldRunReminderClock(true);
                long reminderSecond = DateTime.UtcNow.Ticks /
                    TimeSpan.TicksPerSecond;
                bool reminderBannerTickThrottleOk =
                    PetReminderCoordinator.ShouldRefreshReminderBanner(
                        Int64.MinValue,
                        reminderSecond) &&
                    !PetReminderCoordinator.ShouldRefreshReminderBanner(
                        reminderSecond,
                        reminderSecond) &&
                    PetReminderCoordinator.ShouldRefreshReminderBanner(
                        reminderSecond,
                        reminderSecond + 1);
                bool startupDefaultOk = new PetSettings().StartWithWindows &&
                    StartupRegistration.BuildCommand("C:\\Program Files\\Penny pet.exe") ==
                    "\"C:\\Program Files\\Penny pet.exe\"";
                bool keyboardHookOptInDefaultOk =
                    !new PetSettings().ShowKeyOverlay &&
                    !new PetSettings().KeyboardPrivacyNoticeAccepted &&
                    !PetKeyboardPrivacyPolicy.ShouldStartHook(false, false) &&
                    !PetKeyboardPrivacyPolicy.ShouldStartHook(true, false) &&
                    PetKeyboardPrivacyPolicy.ShouldStartHook(true, true) &&
                    PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, false) &&
                    !PetKeyboardPrivacyPolicy.RequiresFirstUseNotice(true, true) &&
                    PetKeyboardPrivacyPolicy.ShouldDisableUnacknowledgedLegacyOptIn(
                        true, false) &&
                    PetKeyboardPrivacyPolicy.FirstUseNotice.IndexOf("杀毒软件",
                        StringComparison.Ordinal) >= 0 &&
                    PetKeyboardPrivacyPolicy.FirstUseNotice.IndexOf("误报",
                        StringComparison.Ordinal) >= 0;
                bool startupLoadingReadinessGateOk =
                    !PetForm.CanReleaseStartupLoading(false, false) &&
                    !PetForm.CanReleaseStartupLoading(true, false) &&
                    !PetForm.CanReleaseStartupLoading(false, true) &&
                    PetForm.CanReleaseStartupLoading(true, true);
                int idleCycleMilliseconds = actualAnimationCycleDurations[0];
                int failedCycleMilliseconds = actualAnimationCycleDurations[5];
                int waitingCycleMilliseconds = actualAnimationCycleDurations[6];
                int thinkingCycleMilliseconds = actualAnimationCycleDurations[7];
                int reviewCycleMilliseconds = actualAnimationCycleDurations[8];
                int goodbyeCycleMilliseconds = actualAnimationCycleDurations[3];
                int notificationCycleMilliseconds =
                    actualAnimationCycleDurations[9];
                bool smoothTimingOk = idleCycleMilliseconds >= 2000 &&
                    failedCycleMilliseconds >= 2200 &&
                    waitingCycleMilliseconds >= 1800 &&
                    thinkingCycleMilliseconds >= 2400 &&
                    reviewCycleMilliseconds >= 2000;
                bool goodbyeAnimationOk = goodbyeCycleMilliseconds >= 1200;
                bool notificationAnimationOk =
                    notificationCycleMilliseconds >= 1200;
                bool dueReminderBubblePersistentOk =
                    PetReminderCoordinator.DueReminderBubbleDurationMilliseconds == 0;
                bool dueReminderBubbleUsesOwnSizeOk = Math.Abs(
                    PetForm.DueReminderBubbleFontSizePoints(100) -
                    KeyboardOverlayForm.TextFontSizePoints(100)) < 0.2F;
                bool dueReminderBubbleReplacementOk =
                    PetReminderCoordinator.ShouldReplaceBubble(
                        true, false, false, false) &&
                    PetReminderCoordinator.ShouldReplaceBubble(
                        true, false, true, false) &&
                    PetReminderCoordinator.ShouldReplaceBubble(
                        true, false, false, true) &&
                    PetReminderCoordinator.ShouldReplaceBubble(
                        false, false, false, false);
                bool preAlertBubbleProtectionOk =
                    !PetReminderCoordinator.ShouldReplaceBubble(
                        false, true, false, false) &&
                    PetReminderCoordinator.ShouldReplaceBubble(
                        false, true, true, false) &&
                    PetReminderCoordinator.ShouldReplaceBubble(
                        false, true, false, true);
                bool notificationAnimationSingleCycleOk =
                    !PetAnimationController.ReminderAnimationCycleComplete(
                        true, 9, 1, 4) &&
                    PetAnimationController.ReminderAnimationCycleComplete(
                        true, 9, 3, 4) &&
                    !PetAnimationController.ReminderAnimationCycleComplete(
                        false, 9, 3, 4) &&
                    !PetAnimationController.ReminderAnimationCycleComplete(
                        true, 0, 3, 4);
                bool dragUsesSecondIdleRowOk =
                    PetAnimationController.FailedRow == 5;
                Random fixedRandom = new Random(20260810);
                HashSet<int> idleChoices = new HashSet<int>();
                HashSet<int> typingChoices = new HashSet<int>();
                int idleAnimationRow = -1;
                bool idleThoughtNoImmediateRepeat = true;
                for (int i = 0; i < 256; i++)
                {
                    int nextIdleRow =
                        PetAnimationController.PickRandomIdleAnimationRow(
                        fixedRandom, idleAnimationRow);
                    idleThoughtNoImmediateRepeat =
                        idleThoughtNoImmediateRepeat &&
                        (nextIdleRow == 0 || nextIdleRow != idleAnimationRow);
                    idleChoices.Add(nextIdleRow);
                    idleAnimationRow = nextIdleRow;
                    typingChoices.Add(
                        PetAnimationController.PickRandomTypingAnimationRow(
                            fixedRandom));
                }
                bool idleRandomRowsOk = idleThoughtNoImmediateRepeat &&
                    idleChoices.Count == 3 &&
                    PetAnimationController.IsIdleAnimationRow(0) &&
                    PetAnimationController.IsIdleAnimationRow(5) &&
                    PetAnimationController.IsIdleAnimationRow(8) &&
                    !PetAnimationController.IsIdleAnimationRow(7);
                bool typingRandomRowsOk = typingChoices.Count == 2 &&
                    PetAnimationController.IsTypingAnimationRow(6) &&
                    PetAnimationController.IsTypingAnimationRow(7) &&
                    !PetAnimationController.IsTypingAnimationRow(8);
                Random probabilityRandom = new Random(20260820);
                int firstThoughtSelections = 0;
                int secondThoughtSelections = 0;
                for (int i = 0; i < 100000; i++)
                {
                    int selected =
                        PetAnimationController.PickRandomIdleAnimationRow(
                        probabilityRandom, -1);
                    if (selected == 5) firstThoughtSelections++;
                    if (selected == 8) secondThoughtSelections++;
                }
                bool idleThoughtProbabilityReducedOk =
                    PetAnimationController.IdleThoughtProbabilityDenominator == 20 &&
                    firstThoughtSelections >= 4300 &&
                    firstThoughtSelections <= 5700 &&
                    secondThoughtSelections >= 4300 &&
                    secondThoughtSelections <= 5700;
                int failedGuitarSelections = 0;
                for (int i = 0; i < 60000; i++)
                    if (PetAnimationController.PickRandomTypingAnimationRow(
                        probabilityRandom) == 7) failedGuitarSelections++;
                bool guitarFailureProbabilityReducedOk =
                    PetAnimationController.GuitarFailureProbabilityDenominator == 6 &&
                    failedGuitarSelections >= 9500 &&
                    failedGuitarSelections <= 10500;
                bool hoverBubbleCopyOk;
                bool bubbleManualPositionOk;
                bool styledReminderBubbleOk;
                bool bubbleThemeAndKeyboardFontOk;
                using (SpeechBubbleForm bubble = new SpeechBubbleForm("初始", 0))
                using (SpeechBubbleForm styledBubble = new SpeechBubbleForm(
                    "样式提醒", 0, "Microsoft YaHei UI", 24F))
                {
                    bubble.UpdateText("今天想要做些什么呢？");
                    hoverBubbleCopyOk = bubble.DisplayText == "今天想要做些什么呢？" &&
                        PetForm.FormatRemaining(TimeSpan.FromSeconds(65)) == "1分5秒";
                    bubbleManualPositionOk =
                        bubble.StartPosition == System.Windows.Forms.FormStartPosition.Manual;
                    styledReminderBubbleOk = styledBubble.Font != null &&
                        Math.Abs(styledBubble.Font.SizeInPoints - 24F) < 0.2F &&
                        styledBubble.Font.Bold &&
                        SpeechBubbleForm.BubbleTextColor == Color.White;
                    bubbleThemeAndKeyboardFontOk = bubble.Font != null &&
                        String.Equals(bubble.Font.FontFamily.Name,
                            KeyboardOverlayForm.TextFontFamilyName,
                            StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(bubble.Font.SizeInPoints -
                            KeyboardOverlayForm.TextFontSizePoints(100)) < 0.2F &&
                        bubble.Font.Bold &&
                        SpeechBubbleForm.BubbleFillColor ==
                            Color.FromArgb(255, 73, 74, 40) &&
                        SpeechBubbleForm.BubbleTextColor == Color.White;
                }
                bool dragBubbleSuppressionOk =
                    !PetForm.ShouldShowHoverBubble(true, false, true) &&
                    PetForm.ShouldShowHoverBubble(true, false, false);
                bool silentModeBehaviorOk =
                    !PetForm.ShouldShowHoverBubble(true, false, false, true) &&
                    PetForm.ShouldShowHoverBubble(true, false, false, false) &&
                    PetForm.ShouldSuppressDailyBubble(true, false) &&
                    !PetForm.ShouldSuppressDailyBubble(true, true) &&
                    !PetForm.ShouldSuppressDailyBubble(false, false);
                Random manualRandom = new Random(20260811);
                HashSet<int> manualAnimationRows = new HashSet<int>();
                int manualAnimationRow = -1;
                bool manualAnimationNoImmediateRepeat = true;
                for (int i = 0; i < 256; i++)
                {
                    int nextManualRow =
                        PetAnimationController.PickRandomManualAnimationRow(
                        manualRandom, manualAnimationRow);
                    manualAnimationNoImmediateRepeat =
                        manualAnimationNoImmediateRepeat &&
                        nextManualRow != manualAnimationRow;
                    manualAnimationRows.Add(nextManualRow);
                    manualAnimationRow = nextManualRow;
                }
                bool manualAnimationRandomPoolOk =
                    manualAnimationNoImmediateRepeat &&
                    manualAnimationRows.Count == 7 &&
                    PetAnimationController.IsManualAnimationRow(0) &&
                    PetAnimationController.IsManualAnimationRow(4) &&
                    PetAnimationController.IsManualAnimationRow(5) &&
                    PetAnimationController.IsManualAnimationRow(6) &&
                    PetAnimationController.IsManualAnimationRow(7) &&
                    PetAnimationController.IsManualAnimationRow(8) &&
                    PetAnimationController.IsManualAnimationRow(9) &&
                    !PetAnimationController.IsManualAnimationRow(1) &&
                    !PetAnimationController.IsManualAnimationRow(2) &&
                    !PetAnimationController.IsManualAnimationRow(3);
                Random manualProbabilityRandom = new Random(20260821);
                int manualFirstThought = 0;
                int manualFailedGuitar = 0;
                int manualSecondThought = 0;
                for (int i = 0; i < 42000; i++)
                {
                    int selected =
                        PetAnimationController.PickRandomManualAnimationRow(
                        manualProbabilityRandom, -1);
                    if (selected == 5) manualFirstThought++;
                    if (selected == 7) manualFailedGuitar++;
                    if (selected == 8) manualSecondThought++;
                }
                bool manualSpecialAnimationProbabilityReducedOk =
                    manualFirstThought >= 1700 && manualFirstThought <= 2300 &&
                    manualFailedGuitar >= 1700 && manualFailedGuitar <= 2300 &&
                    manualSecondThought >= 1700 && manualSecondThought <= 2300;
                DateTime manualClickNow = DateTime.UtcNow;
                bool manualAnimationCooldownOk =
                    PetAnimationController.ManualAnimationCooldownMilliseconds == 600 &&
                    PetAnimationController.ManualAnimationClickReady(manualClickNow,
                        DateTime.MinValue) &&
                    !PetAnimationController.ManualAnimationClickReady(manualClickNow,
                        manualClickNow.AddMilliseconds(600)) &&
                    PetAnimationController.ManualAnimationClickReady(
                        manualClickNow.AddMilliseconds(600),
                        manualClickNow.AddMilliseconds(600));
                bool clickDragThresholdOk =
                    !PetAnimationController.MovementStartsDrag(5, 0) &&
                    !PetAnimationController.MovementStartsDrag(4, 4) &&
                    PetAnimationController.MovementStartsDrag(6, 0) &&
                    PetAnimationController.MovementStartsDrag(5, 4);
                Point bubblePosition = SpeechBubbleForm.CalculateNearLocation(
                    new Rectangle(1400, 800, 192, 208), new Size(330, 138),
                    new Rectangle(0, 0, 1920, 1080));
                bool bubblePositionMathOk = bubblePosition.X > 1000 &&
                    bubblePosition.Y > 500 && bubblePosition != Point.Empty;
                bool scaleRangeOk =
                    PetForm.NormalizeScalePercent(47) == 50 &&
                    PetForm.NormalizeScalePercent(104) == 100 &&
                    PetForm.NormalizeScalePercent(156) == 160 &&
                    PetForm.NormalizeScalePercent(207) == 200 &&
                    PetForm.ScaledPetSize(50) == new Size(96, 104) &&
                    PetForm.ScaledPetSize(200) == new Size(384, 416);
                bool keyboardTextScaleChoicesOk =
                    KeyboardOverlayForm.NormalizeTextScalePercent(55) == 60 &&
                    KeyboardOverlayForm.NormalizeTextScalePercent(100) == 100 &&
                    KeyboardOverlayForm.NormalizeTextScalePercent(140) == 150 &&
                    Math.Abs(KeyboardOverlayForm.TextFontSizePoints(60) - 9F) < 0.01F &&
                    Math.Abs(KeyboardOverlayForm.TextFontSizePoints(100) - 15F) < 0.01F &&
                    Math.Abs(KeyboardOverlayForm.TextFontSizePoints(150) - 22.5F) < 0.01F;
                string shortcut = KeyboardInputFormatter.ComposeKeyName(
                    (int)Keys.W, true, false, false, false);
                string modifierChord = KeyboardInputFormatter.ComposeKeyName(
                    (int)Keys.LShiftKey, true, true, false, false);
                KeyDisplayAccumulator accumulator = new KeyDisplayAccumulator();
                DateTime keyBase = DateTime.UtcNow;
                string keyOne = accumulator.Register("W", keyBase);
                string keyTwo = accumulator.Register("W", keyBase.AddMilliseconds(100));
                string keyThree = accumulator.Register("W", keyBase.AddMilliseconds(200));
                string keyReset = accumulator.Register("W", keyBase.AddSeconds(2));
                KeyDisplayAccumulator heldAccumulator = new KeyDisplayAccumulator();
                string heldThree = heldAccumulator.Register("W", keyBase, 3);
                string heldFive = heldAccumulator.Register("W",
                    keyBase.AddMilliseconds(120), 2);
                KeyDisplayAccumulator absoluteAccumulator = new KeyDisplayAccumulator();
                string absoluteOne = absoluteAccumulator.RegisterAbsolute("W",
                    keyBase, 1);
                string absoluteThree = absoluteAccumulator.RegisterAbsolute("W",
                    keyBase.AddMilliseconds(520), 3);
                string absoluteStale = absoluteAccumulator.RegisterAbsolute("W",
                    keyBase.AddMilliseconds(560), 1);
                int hookRepeatOne = GlobalKeyboardActivity.NextRepeatCount(
                    0, (uint)Keys.W, 0, 1000, 0);
                int hookRepeatTwo = GlobalKeyboardActivity.NextRepeatCount(
                    (uint)Keys.W, (uint)Keys.W, 1000, 1800, hookRepeatOne);
                int hookRepeatReset = GlobalKeyboardActivity.NextRepeatCount(
                    (uint)Keys.W, (uint)Keys.A, 1800, 1850, hookRepeatTwo);
                bool keyDisplayOk = shortcut == "CTRL+W" &&
                    modifierChord == "CTRL+SHIFT" && keyOne == "W" &&
                    keyTwo == "W*2" && keyThree == "W*3" && keyReset == "W" &&
                    heldThree == "W*3" && heldFive == "W*5" &&
                    absoluteOne == "W" && absoluteThree == "W*3" &&
                    absoluteStale == "W*3" && hookRepeatOne == 1 &&
                    hookRepeatTwo == 2 && hookRepeatReset == 1;
                bool heldKeyOverlayStableOk =
                    GlobalKeyboardActivity.ShouldPublishKeyDown(false) &&
                    !GlobalKeyboardActivity.ShouldPublishKeyDown(true);
                bool ownProcessHookIsolationOk =
                    !GlobalKeyboardActivity.ShouldPublishKey(false, 42, 42) &&
                    GlobalKeyboardActivity.ShouldPublishKey(false, 42, 43) &&
                    !GlobalKeyboardActivity.ShouldPublishKey(true, 42, 43);
                bool reverseReminderStepOk =
                    ReverseStepDateTimePicker.ReverseVirtualKey(0x26) == 0x28 &&
                    ReverseStepDateTimePicker.ReverseVirtualKey(0x28) == 0x26;
                DateTime imeGuardNow = DateTime.UtcNow;
                bool imeAnimationGuardOk =
                    ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010D) &&
                    ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010F) &&
                    !ImeFriendlyRichTextBox.StartsOrUpdatesComposition(0x010E) &&
                    PetAnimationController.ShouldPauseOwnNoteAnimation(
                        true, DateTime.MinValue,
                        imeGuardNow) &&
                    PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                        imeGuardNow.AddMilliseconds(1), imeGuardNow) &&
                    !PetAnimationController.ShouldPauseOwnNoteAnimation(false,
                        imeGuardNow.AddMilliseconds(-1), imeGuardNow);
                bool imeAutoSaveGuardOk =
                    StickyNoteForm.ShouldDeferAutoSave(true,
                        DateTime.MinValue, imeGuardNow) &&
                    StickyNoteForm.ShouldDeferAutoSave(false,
                        imeGuardNow.AddMilliseconds(-200), imeGuardNow) &&
                    !StickyNoteForm.ShouldDeferAutoSave(false,
                        imeGuardNow.AddSeconds(-2), imeGuardNow);
                bool passwordSuppressionOk =
                    SensitiveInputDetector.ShouldSuppress(true, false, false) &&
                    SensitiveInputDetector.ShouldSuppress(false, true, false) &&
                    SensitiveInputDetector.ShouldSuppress(false, false, true) &&
                    !SensitiveInputDetector.ShouldSuppress(false, false, false) &&
                    SensitiveInputDetector.ShouldSuppress(false, false, false,
                        false) &&
                    !SensitiveInputDetector.ShouldSuppress(false, false, false,
                        true);
                bool keyboardPrivacyGenerationOk =
                    PetForm.IsCurrentPrivacyScan(12, 12) &&
                    !PetForm.IsCurrentPrivacyScan(12, 13);
                bool adaptiveContrastOk =
                    KeyboardOverlayForm.ChooseTextColorFromLuminance(0.8) == Color.Black &&
                    KeyboardOverlayForm.ChooseTextColorFromLuminance(0.2) == Color.White;
                string stickyTestPath = outputPath + ".sticky-test.dat";
                StickyNoteRepository stickyRepository =
                    StickyNoteRepository.LoadFromFile(stickyTestPath);
                const string multilingualSample =
                    "English line\n日本語 한국어 Русский العربية Français";
                StickyNoteData sticky = stickyRepository.Create(multilingualSample,
                    new Point(120, 160));
                sticky.Title = "多语言 Note 日本語";
                sticky.IsTodoList = true;
                sticky.TodoItems.Add(new StickyTodoItem("整理会议记录", false));
                sticky.TodoItems.Add(new StickyTodoItem("给家人回电话", true));
                sticky.ColorArgb = Color.LightBlue.ToArgb();
                sticky.BackgroundOpacityPercent = 60;
                sticky.TextColorArgb = Color.White.ToArgb();
                sticky.AlwaysOnTop = false;
                sticky.Width = 360;
                sticky.Height = 260;
                sticky.ReminderUtcTicks = DateTime.UtcNow.AddHours(2).Ticks;
                using (RichTextBox richSource = new RichTextBox())
                using (Font richFont = new Font("Microsoft YaHei UI", 14F,
                    FontStyle.Bold | FontStyle.Italic | FontStyle.Underline))
                {
                    richSource.Text = sticky.Text;
                    richSource.SelectAll();
                    richSource.SelectionFont = richFont;
                    sticky.RichTextRtf = richSource.Rtf;
                }
                stickyRepository.SaveToFile(stickyTestPath);
                StickyNoteRepository restoredStickyRepository =
                    StickyNoteRepository.LoadFromFile(stickyTestPath);
                List<StickyNoteData> restoredNotes = restoredStickyRepository.GetAll();
                bool stickyPersistenceOk = restoredNotes.Count == 1 &&
                    restoredNotes[0].Text == multilingualSample &&
                    restoredNotes[0].Title == "多语言 Note 日本語" &&
                    restoredNotes[0].ColorArgb == Color.LightBlue.ToArgb() &&
                    restoredNotes[0].BackgroundOpacityPercent == 60 &&
                    restoredNotes[0].TextColorArgb == Color.White.ToArgb() &&
                    !restoredNotes[0].AlwaysOnTop &&
                    restoredNotes[0].Width == 360 && restoredNotes[0].Height == 260 &&
                    restoredNotes[0].ReminderUtc.HasValue;
                bool multilingualNotePersistenceOk = stickyPersistenceOk &&
                    restoredNotes[0].SearchText.IndexOf("日本語",
                        StringComparison.Ordinal) >= 0 &&
                    restoredNotes[0].SearchText.IndexOf("한국어",
                        StringComparison.Ordinal) >= 0 &&
                    restoredNotes[0].SearchText.IndexOf("العربية",
                        StringComparison.Ordinal) >= 0;
                bool pinActionTextOk =
                    StickyNoteForm.PinActionText(false) == "置顶" &&
                    StickyNoteForm.PinActionText(true) == "取消置顶";
                StickyNoteData todoPinData = new StickyNoteData();
                todoPinData.IsTodoList = true;
                todoPinData.AlwaysOnTop = true;
                bool todoPinActionTextOk;
                using (StickyNoteForm todoPinNote = new StickyNoteForm(todoPinData))
                    todoPinActionTextOk = todoPinNote.CurrentPinActionText ==
                        "取消置顶" && todoPinNote.HeaderTypeIconVisibleForTest;
                bool richTextPersistenceOk = false;
                using (RichTextBox richRestored = new RichTextBox())
                {
                    try
                    {
                        richRestored.Rtf = restoredNotes[0].RichTextRtf;
                        richRestored.Select(0, "English line".Length);
                        Font restoredFont = richRestored.SelectionFont;
                        richTextPersistenceOk = richRestored.Text ==
                            restoredNotes[0].Text && restoredFont != null &&
                            restoredFont.Bold && restoredFont.Italic &&
                            restoredFont.Underline &&
                            Math.Abs(restoredFont.SizeInPoints - 14F) < 0.2F;
                    }
                    catch { richTextPersistenceOk = false; }
                }
                string longStickyPath = outputPath +
                    ".sticky-long-content-test.dat";
                if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
                if (File.Exists(longStickyPath + ".bak"))
                    File.Delete(longStickyPath + ".bak");
                string longVisibleText = new string('长', 13050) + "结尾保留";
                StickyNoteRepository longRepository =
                    StickyNoteRepository.LoadFromFile(longStickyPath);
                StickyNoteData longNote = longRepository.Create(longVisibleText,
                    Point.Empty);
                using (RichTextBox longRichText = new RichTextBox())
                {
                    longRichText.Text = longVisibleText;
                    longNote.RichTextRtf = longRichText.Rtf;
                }
                longRepository.SaveToFile(longStickyPath);
                List<StickyNoteData> longRestored = StickyNoteRepository
                    .LoadFromFile(longStickyPath).GetAll();
                string rtfAboveOldLimit = "{\\rtf1\\ansi " +
                    new string('x', 350000) + "}";
                bool richTextNoSilentTruncationOk = longRestored.Count == 1 &&
                    longRestored[0].Text == longVisibleText &&
                    longRestored[0].Text.EndsWith("结尾保留",
                        StringComparison.Ordinal) &&
                    StickyNoteRepository.NormalizeRtf(rtfAboveOldLimit) ==
                        rtfAboveOldLimit;
                if (File.Exists(longStickyPath)) File.Delete(longStickyPath);
                if (File.Exists(longStickyPath + ".bak"))
                    File.Delete(longStickyPath + ".bak");
                bool todoPersistenceOk = stickyPersistenceOk &&
                    restoredNotes[0].IsTodoList && restoredNotes[0].TodoItems.Count == 2 &&
                    restoredNotes[0].TodoItems[0].Text == "整理会议记录" &&
                    !restoredNotes[0].TodoItems[0].Completed &&
                    restoredNotes[0].TodoItems[1].Text == "给家人回电话" &&
                    restoredNotes[0].TodoItems[1].Completed;
                string scheduleTestPath = outputPath + ".schedule-test.dat";
                StickyNoteRepository scheduleRepository =
                    StickyNoteRepository.LoadFromFile(scheduleTestPath);
                StickyNoteData scheduleNote = scheduleRepository.Create(
                    String.Empty, new Point(210, 180));
                scheduleNote.IsSchedule = true;
                scheduleNote.IsTodoList = false;
                scheduleNote.Title = "日程";
                scheduleNote.FontSizeTwips = 320;
                scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                    "参加画展", DateTime.Today.AddDays(6), true));
                scheduleNote.ScheduleItems.Add(new StickyScheduleItem(
                    "朋友生日", DateTime.Today.AddDays(58)));
                scheduleRepository.SaveToFile(scheduleTestPath);
                StickyNoteRepository restoredScheduleRepository =
                    StickyNoteRepository.LoadFromFile(scheduleTestPath);
                List<StickyNoteData> restoredSchedules =
                    restoredScheduleRepository.GetAll();
                bool schedulePersistenceOk = restoredSchedules.Count == 1 &&
                    restoredSchedules[0].IsSchedule &&
                    !restoredSchedules[0].IsTodoList &&
                    restoredSchedules[0].ScheduleItems.Count == 2 &&
                    restoredSchedules[0].ScheduleItems[0].Text == "参加画展" &&
                    restoredSchedules[0].ScheduleItems[0].IsPinned &&
                    !restoredSchedules[0].ScheduleItems[1].IsPinned &&
                    restoredSchedules[0].ScheduleItems[0].TargetDate ==
                        DateTime.Today.AddDays(6) &&
                    restoredSchedules[0].SearchText.IndexOf("朋友生日",
                        StringComparison.Ordinal) >= 0;
                bool scheduleCountdownOk =
                    StickyNoteForm.FormatScheduleCountdown(
                        DateTime.Today.AddDays(6), DateTime.Today) == "6天" &&
                    StickyNoteForm.FormatScheduleCountdown(DateTime.Today,
                        DateTime.Today) == "今天" &&
                    StickyNoteForm.FormatScheduleCountdown(
                        DateTime.Today.AddDays(-2), DateTime.Today) == "已过2天";
                bool scheduleFontChoicesOk =
                    StickyNoteForm.ScheduleFontSizeLabel(9F) == "特小 9" &&
                    StickyNoteForm.ScheduleFontSizeLabel(10.5F) == "小 10.5" &&
                    StickyNoteForm.ScheduleFontSizeLabel(12F) == "小 10.5" &&
                    StickyNoteForm.ScheduleFontSizeLabel(16F) == "中 16" &&
                    StickyNoteForm.ScheduleFontSizeLabel(22F) == "大 22" &&
                    StickyNoteForm.ScheduleFontSizeLabel(48F) == "特大 48";
                bool scheduleDateMouseWheelOk =
                    ScheduleItemDialog.StepDateWithMouseWheel(
                        DateTime.Today, 120) == DateTime.Today.AddDays(-1) &&
                    ScheduleItemDialog.StepDateWithMouseWheel(
                        DateTime.Today, -240) == DateTime.Today.AddDays(2);
                ReminderItem expiredAtLaunch = new ReminderItem(
                    DateTime.UtcNow.AddMinutes(-1), "已错过");
                ReminderItem futureAtLaunch = new ReminderItem(
                    DateTime.UtcNow.AddMinutes(1), "仍有效");
                DateTime launchGate = DateTime.UtcNow;
                bool expiredReminderDiscardedOnLaunchOk =
                    !PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                        expiredAtLaunch,
                        launchGate) &&
                    PetReminderCoordinator.ShouldRestoreReminderAfterLaunch(
                        futureAtLaunch,
                        launchGate);
                if (File.Exists(scheduleTestPath)) File.Delete(scheduleTestPath);
                if (File.Exists(scheduleTestPath + ".bak"))
                    File.Delete(scheduleTestPath + ".bak");
                bool imeCompatibleEditorOk;
                bool singleWindowStickyInputOk;
                bool todoGroupingOk;
                bool reminderBannerCountdownOk;
                bool todoMarkerRoundTripOk;
                bool todoPlainTextProjectionOk;
                bool stickyResizePaintingOk;
                bool richTextToolbarOk;
                bool smoothFormatInteractionOk;
                bool deferredInitialFocusSafeOk =
                    StickyNoteForm.ShouldApplyDeferredInitialFocus(0, 0,
                        false) &&
                    !StickyNoteForm.ShouldApplyDeferredInitialFocus(0, 1,
                        false) &&
                    !StickyNoteForm.ShouldApplyDeferredInitialFocus(0, 0,
                        true);
                bool stableFormatSelectorModelOk;
                bool firstFormatCommitOk;
                bool fixedNoteTypeActionsOk;
                bool multilingualEditorInputOk;
                bool tabSwitchContentPreservedOk;
                bool reminderCompactBannerOk;
                bool reminderSelectionActionsOk;
                bool inlineCreationActionsRemovedOk;
                bool reminderFirstClickStableOk;
                bool reminderBlankAreaClearOk;
                bool reminderBannerRefreshInPlaceOk;
                bool todoWrapAndInlineEditOk;
                bool todoOverallFontSizeOk;
                bool formatToolbarFocusOk;
                bool formatSelectorsAlwaysBlackOk;
                bool bodyTextColorSwitchOk;
                bool dockResizeRoleOk;
                bool nativeWindowStyleAppliedOk;
                bool groupTopMostSyncOk;
                bool dedicatedRowContextMenusOk;
                bool emptyNoteFormattingOk;
                bool caretTypingFormatSwitchOk;
                bool singleNativeImeCommitAfterFormatOk;
                bool unifiedNoteContextMenusOk;
                bool schedulePinMarkerToggleOk;
                using (StickyNoteForm testNote = new StickyNoteForm(restoredNotes[0]))
                {
                    List<ReminderItem> bannerItems = new List<ReminderItem>();
                    bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddSeconds(65),
                        "中文提醒项目", null, 24F, true));
                    bannerItems.Add(new ReminderItem(DateTime.UtcNow.AddHours(2),
                        "第二条提醒"));
                    testNote.UpdateReminderBanner(bannerItems);
                    imeCompatibleEditorOk = testNote.UsesImeCompatibleEditor;
                    singleWindowStickyInputOk =
                        !testNote.UsesLegacyInputProxyForTest &&
                        testNote.LegacyInputProxyHandleForTest == IntPtr.Zero;
                    todoGroupingOk = testNote.VisibleTodoItemCount == 2 &&
                        testNote.TodoGroupCount == 2;
                    reminderBannerCountdownOk =
                        testNote.ReminderBannerLineCount == 2 &&
                        testNote.ReminderBannerText.Contains("中文提醒项目") &&
                        testNote.ReminderBannerText.Contains("第二条提醒") &&
                        StickyNoteForm.FormatCountdown(TimeSpan.FromSeconds(65)) ==
                            "1分5秒" &&
                        StickyNoteForm.FormatCountdown(TimeSpan.Zero) == "现在";
                    reminderCompactBannerOk = Math.Abs(
                        testNote.ReminderBannerFirstFontSize - 24F) < 0.2F;
                    reminderSelectionActionsOk =
                        testNote.ExerciseReminderSelectionActionsForTest();
                    inlineCreationActionsRemovedOk =
                        testNote.ExerciseInlineCreationActionsRemovedForTest();
                    reminderFirstClickStableOk =
                        testNote.ExerciseReminderFirstClickStabilityForTest(
                            out reminderBlankAreaClearOk,
                            out reminderBannerRefreshInPlaceOk);
                    bool completedMarker;
                    string cleaned = StickyNoteForm.ParseTodoTextLine(
                        "[ ][ ][x] 最推荐的修改方法", out completedMarker);
                    bool pendingMarker;
                    string pending = StickyNoteForm.ParseTodoTextLine(
                        "[ ] 普通项目", out pendingMarker);
                    todoMarkerRoundTripOk = cleaned == "最推荐的修改方法" &&
                        completedMarker && pending == "普通项目" && !pendingMarker;
                    List<StickyTodoItem> switchItems = new List<StickyTodoItem>();
                    switchItems.Add(new StickyTodoItem("[ ] 第一项", false));
                    switchItems.Add(new StickyTodoItem("[x] 第二项", true));
                    todoPlainTextProjectionOk =
                        StickyNoteForm.BuildPlainTextFromTodos(switchItems) ==
                        "第一项" + Environment.NewLine + "第二项";
                    stickyResizePaintingOk = testNote.UsesBufferedResizePainting;
                }
                StickyNoteData richToolbarData = new StickyNoteData();
                richToolbarData.Text = "格式工具栏测试";
                richToolbarData.Width = 360;
                richToolbarData.Height = 260;
                using (StickyNoteForm richToolbarNote =
                    new StickyNoteForm(richToolbarData))
                {
                    richTextToolbarOk =
                        richToolbarNote.HasRichTextFormattingToolbar &&
                        richToolbarNote.HeaderTypeIconVisibleForTest &&
                        richToolbarNote.ExerciseRichTextFormattingForTest();
                    smoothFormatInteractionOk =
                        richToolbarNote.ExerciseSmoothFormatInteractionForTest();
                    stableFormatSelectorModelOk =
                        richToolbarNote.UsesStableListFormatSelectors;
                    formatToolbarFocusOk =
                        richToolbarNote.FormatControlsPreserveSelectionForTest;
                    formatSelectorsAlwaysBlackOk =
                        richToolbarNote.FormatSelectorsAlwaysBlackForTest;
                    bodyTextColorSwitchOk =
                        richToolbarNote.ExerciseBodyTextColorSwitchForTest();
                    dockResizeRoleOk =
                        richToolbarNote.ExerciseDockResizeRoleForTest();
                    nativeWindowStyleAppliedOk =
                        richToolbarNote.NativeMaximizeStyleDisabledForTest;
                    groupTopMostSyncOk =
                        richToolbarNote.ExerciseGroupTopMostForTest();
                    multilingualEditorInputOk =
                        richToolbarNote.ExerciseMultilingualInputForTest();
                    tabSwitchContentPreservedOk =
                        richToolbarNote.ExerciseReminderSwitchContentPreservationForTest();
                }
                StickyNoteData firstFormatData = new StickyNoteData();
                using (StickyNoteForm firstFormatNote =
                    new StickyNoteForm(firstFormatData))
                {
                    firstFormatCommitOk =
                        firstFormatNote.ExerciseFirstFormatCommitForTest();
                    emptyNoteFormattingOk =
                        firstFormatNote.ExerciseEmptyNoteFormattingForTest();
                    caretTypingFormatSwitchOk =
                        firstFormatNote.ExerciseCaretTypingFormatSwitchForTest();
                    singleNativeImeCommitAfterFormatOk = firstFormatNote
                        .ExerciseSingleNativeImeCommitAfterFormatForTest();
                    unifiedNoteContextMenusOk =
                        firstFormatNote.ExerciseUnifiedNoteContextMenusForTest();
                }
                StickyNoteData scheduleInteractionData = new StickyNoteData();
                scheduleInteractionData.IsSchedule = true;
                using (StickyNoteForm scheduleInteractionNote =
                    new StickyNoteForm(scheduleInteractionData))
                    schedulePinMarkerToggleOk =
                        scheduleInteractionNote.HeaderTypeIconVisibleForTest &&
                        scheduleInteractionNote.ExerciseSchedulePinMarkerForTest();
                StickyNoteData todoStressData = new StickyNoteData();
                using (StickyNoteForm todoStressNote =
                    new StickyNoteForm(todoStressData))
                {
                    fixedNoteTypeActionsOk =
                        todoStressNote.ExerciseFixedNoteTypeActionsForTest();
                    todoWrapAndInlineEditOk =
                        todoStressNote.ExerciseTodoWrapAndInlineEditForTest();
                    todoOverallFontSizeOk =
                        todoStressNote.ExerciseTodoOverallFontSizeForTest();
                    dedicatedRowContextMenusOk =
                        todoStressNote.ExerciseDedicatedRowContextMenusForTest();
                }
                bool shortItemWeightedLimitOk =
                    ShortItemText.Fits(new string('中', 50)) &&
                    !ShortItemText.Fits(new string('中', 51)) &&
                    ShortItemText.Fits(new string('W', 100)) &&
                    !ShortItemText.Fits(new string('W', 101)) &&
                    ShortItemText.NormalizeAndTruncate(new string('中', 51)).Length == 50;
                float parsedFive;
                float parsedNumeric;
                bool fontSizeParsingOk =
                    StickyNoteForm.TryParseFontSize("五号", out parsedFive) &&
                    Math.Abs(parsedFive - 10.5F) < 0.01F &&
                    StickyNoteForm.TryParseFontSize("18 磅", out parsedNumeric) &&
                    Math.Abs(parsedNumeric - 18F) < 0.01F &&
                    !StickyNoteForm.TryParseFontSize("100", out parsedNumeric);
                bool chineseFontsFirstOk =
                    StickyNoteForm.IsChineseFontNameForTest("微软雅黑") &&
                    StickyNoteForm.IsChineseFontNameForTest("Noto Serif SC SemiBold") &&
                    StickyNoteForm.IsChineseFontNameForTest("Microsoft YaHei UI") &&
                    !StickyNoteForm.IsChineseFontNameForTest("Arial") &&
                    !StickyNoteForm.IsChineseFontNameForTest("Noto Sans JP") &&
                    StickyNoteForm.FontNameSortsBeforeForTest(
                        "Noto Sans SC", "Arial");
                bool installedFontListCacheOk =
                    StickyNoteForm.InstalledFontNamesCachedForTest();
                Font sharedFontFirst = StickyNoteForm.CreateSafeFont(
                    "Microsoft YaHei UI", 18F, FontStyle.Regular);
                Font sharedFontSecond = StickyNoteForm.CreateSafeFont(
                    "Microsoft YaHei UI", 18F, FontStyle.Regular);
                bool sharedFontUsable;
                try
                {
                    byte ignoredCharacterSet = sharedFontSecond.GdiCharSet;
                    sharedFontUsable = ignoredCharacterSet == sharedFontSecond.GdiCharSet;
                }
                catch
                {
                    sharedFontUsable = false;
                }
                bool sharedFontLifetimeOk = Object.ReferenceEquals(
                    sharedFontFirst, sharedFontSecond) && sharedFontUsable;
                bool reminderSizePreviewOk;
                bool reminderLiveSizePreviewOk;
                bool unforcedMultilingualImeOk;
                bool standaloneReminderNoAutoStickyOptionOk = true;
                using (ReminderDialog previewDialog = new ReminderDialog(
                    "预览", 10.5F, false))
                {
                    reminderSizePreviewOk =
                        previewDialog.ExerciseSizePreviewForTest();
                    unforcedMultilingualImeOk =
                        previewDialog.UsesUnforcedMultilingualIme;
                    foreach (Control control in previewDialog.Controls)
                    {
                        if (control is CheckBox && control.Text.IndexOf(
                            "创建桌面便利贴",
                            StringComparison.Ordinal) >= 0)
                            standaloneReminderNoAutoStickyOptionOk = false;
                    }
                    standaloneReminderNoAutoStickyOptionOk =
                        standaloneReminderNoAutoStickyOptionOk &&
                        previewDialog.ClientSize.Height == 487;
                }
                using (StickyNoteForm reminderPreviewNote =
                    new StickyNoteForm(new StickyNoteData()))
                    reminderLiveSizePreviewOk = reminderPreviewNote
                        .ExerciseReminderLiveSizePreviewForTest();
                bool highDpiStickyLayoutOk =
                    StickyNoteForm.MinimumNoteSizeForDpi(96) ==
                        new Size(280, 220) &&
                    StickyNoteForm.MinimumNoteSizeForDpi(192) ==
                        new Size(560, 440) &&
                    StickyNoteForm.HeaderRowHeightForDpi(192) >=
                        StickyNoteForm.HeaderRowHeightForDpi(96) * 2 - 1;
                bool stickyResourceLimitsOk =
                    StickyNoteLimits.MaximumNotes == 100 &&
                    StickyNoteLimits.MaximumTodoItemsPerNote == 500 &&
                    StickyNoteLimits.MaximumBodyCharacters == 4000000 &&
                    StickyNoteLimits.MaximumRichTextCharacters == 16000000 &&
                    StickyNoteRepository.CanCreateAtCount(true, 0) &&
                    StickyNoteRepository.CanCreateAtCount(true,
                        StickyNoteLimits.MaximumNotes - 1) &&
                    !StickyNoteRepository.CanCreateAtCount(true,
                        StickyNoteLimits.MaximumNotes) &&
                    !StickyNoteRepository.CanCreateAtCount(false, 0);
                bool softPaletteOk =
                    StickyNoteForm.PaletteColorForTest(0).ToArgb() ==
                        Color.FromArgb(255, 255, 117, 112).ToArgb() &&
                    StickyNoteForm.PaletteColorForTest(32).ToArgb() ==
                        Color.FromArgb(255, 239, 240, 241).ToArgb();
                bool fullWidthLatinNormalizationOk =
                    StickyNoteForm.NormalizeFullWidthLatin(
                        "中文ｃｔｒｌＥｎｇｌｉｓｈ１２３") ==
                        "中文ctrlEnglish123";
                bool renameInitialFocusOk;
                using (NoteTitleDialog titleDialog = new NoteTitleDialog("周计划"))
                {
                    renameInitialFocusOk = titleDialog.TitleInputIsInitialActive;
                    unforcedMultilingualImeOk &=
                        titleDialog.UsesUnforcedMultilingualIme;
                }
                Rectangle tabWorkArea = new Rectangle(0, 0, 1920, 1080);
                int leftTabCount = StickyNoteTabsForm.CalculateLeftCount(9, 208,
                    tabWorkArea);
                bool sideTabOverflowOk = leftTabCount >= 4 && leftTabCount < 9 &&
                    9 - leftTabCount > 0 &&
                    StickyNoteTabsForm.ScreenCapacity(tabWorkArea) >= 9 - leftTabCount;
                bool sideTabDragPreviewOk =
                    StickyNoteTabsForm.PreviewInsertionGap >= 12 &&
                    StickyNoteTabsForm.DragSourceVisualOffset >= 6 &&
                    StickyNoteTabsForm.DragSourceVisualOffset <= 12 &&
                    StickyNoteTabsForm.PetGap == -20 &&
                    !String.IsNullOrEmpty(StickyNoteTabsForm.DragDataFormat) &&
                    StickyNoteTabsForm.CalculateDropIndex(0, 4) == 0 &&
                    StickyNoteTabsForm.CalculateDropIndex(95, 4) == 3 &&
                    StickyNoteTabsForm.PreviewTargetTop(0, -1, 1) == 0 &&
                    StickyNoteTabsForm.PreviewTargetTop(1, -1, 1) ==
                        StickyNoteTabsForm.TabHeight +
                        StickyNoteTabsForm.TabGap +
                        StickyNoteTabsForm.PreviewInsertionGap &&
                    StickyNoteTabsForm.PreviewTargetTop(0, 2, 0) ==
                        StickyNoteTabsForm.PreviewInsertionGap;
                bool sideTabDeferredDropCommitOk =
                    StickyTabDropSession.DefersCommitUntilCompletionForTest();
                bool sideTabPreviewClearsBothSidesOk;
                bool sideTabExplicitSourceKeepsTargetFirstOk = false;
                bool sideTabTargetNeverMarkedAsSourceOk = false;
                bool sideTabExclusiveCanvasStateOk = false;
                StickyNoteData previewClearNote = new StickyNoteData();
                StickyNoteData previewTargetNote = new StickyNoteData();
                using (StickyNoteTabsForm previewClearLeft =
                    new StickyNoteTabsForm(StickyTabSide.Left,
                        delegate(StickyNoteData note) { }))
                using (StickyNoteTabsForm previewClearRight =
                    new StickyNoteTabsForm(StickyTabSide.Right,
                        delegate(StickyNoteData note) { }))
                {
                    previewClearLeft.SetNotes(new List<StickyNoteData>
                        { previewClearNote }, 0);
                    previewClearRight.SetNotes(new List<StickyNoteData>
                        { previewClearNote, previewTargetNote }, 1);
                    previewClearLeft.Hide();
                    previewClearRight.Hide();
                    StickyNoteTabsForm.BeginDragSession(previewClearNote,
                        previewClearLeft);
                    previewClearLeft.ShowDropPreviewForTest(previewClearNote, 0);
                    bool leftWasTarget = previewClearLeft.HasDropPreviewForTest;
                    previewClearRight.ShowDropPreviewForTest(previewClearNote, 1);
                    sideTabExplicitSourceKeepsTargetFirstOk =
                        previewClearRight.TabTopForTest(previewClearNote) == 0;
                    sideTabTargetNeverMarkedAsSourceOk =
                        !previewClearRight.HasDragSourceVisualForTest(
                            previewClearNote);
                    sideTabExclusiveCanvasStateOk =
                        previewClearLeft.HasStableDragCanvasForTest &&
                        previewClearRight.HasStableDragCanvasForTest;
                    bool targetIsExclusive = leftWasTarget &&
                        !previewClearLeft.HasDropPreviewForTest &&
                        previewClearRight.HasDropPreviewForTest &&
                        sideTabExplicitSourceKeepsTargetFirstOk &&
                        sideTabTargetNeverMarkedAsSourceOk &&
                        sideTabExclusiveCanvasStateOk &&
                        previewClearLeft.HasDragSourceVisualForTest(
                            previewClearNote);
                    StickyNoteTabsForm.EndDragSession(previewClearNote);
                    sideTabPreviewClearsBothSidesOk = targetIsExclusive &&
                        !previewClearLeft.HasDropPreviewForTest &&
                        !previewClearRight.HasDropPreviewForTest &&
                        previewClearLeft.HasStableDragCanvasForTest &&
                        previewClearRight.HasStableDragCanvasForTest &&
                        !previewClearLeft.HasDragSourceVisualForTest(
                            previewClearNote);
                }
                int fullOverlap = StickyNoteTabsForm.PetOverlapForWidth(192);
                int doubleOverlap = StickyNoteTabsForm.PetOverlapForWidth(384);
                bool sideTabScaledGapOk =
                    Math.Abs((44 - fullOverlap) - (44 - 20) / 2.0) < 1.0 &&
                    Math.Abs((88 - doubleOverlap) - (88 - 20) / 2.0) < 1.0 &&
                    doubleOverlap > fullOverlap;
                Color iconPaper = Color.FromArgb(255, 118, 169, 242);
                Color iconInk = StickyNoteTabControl.TypeIconColor(iconPaper);
                bool sideTabVectorIconColorOk = iconInk.ToArgb() !=
                    Color.Black.ToArgb() && iconInk.GetBrightness() <
                    iconPaper.GetBrightness();
                bool sideTabDeleteCommandOk;
                using (StickyNoteTabControl tabControl = new StickyNoteTabControl(
                    restoredNotes[0], StickyTabSide.Left,
                    delegate(StickyNoteData note) { },
                    delegate(StickyNoteData note) { }))
                    sideTabDeleteCommandOk = tabControl.HasDeleteCommand;
                bool managerMarqueeBatchDeleteOk;
                using (StickyNotesManagerForm manager = new StickyNotesManagerForm(
                    delegate { return restoredStickyRepository.GetAll(); },
                    delegate { }, delegate(StickyNoteData note) { },
                    delegate(StickyNoteData note) { },
                    delegate(StickyNoteData note) { }))
                    managerMarqueeBatchDeleteOk = manager.SupportsMarqueeBatchDelete;
                bool automaticNoteBackupOk = File.Exists(stickyTestPath + ".bak");
                if (File.Exists(stickyTestPath)) File.Delete(stickyTestPath);
                if (File.Exists(stickyTestPath + ".bak"))
                    File.Delete(stickyTestPath + ".bak");
                string legacyStickyPath = outputPath + ".sticky-v1-test.dat";
                string legacyChinese = "旧版中文便利贴";
                string legacyLine = String.Join("|", new string[] {
                    "1", "legacy-note", "1", "1", Color.LightYellow.ToArgb().ToString(),
                    "10", "20", "280", "230", DateTime.UtcNow.Ticks.ToString(),
                    DateTime.UtcNow.Ticks.ToString(), "0",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(legacyChinese))
                });
                File.WriteAllText(legacyStickyPath, legacyLine,
                    new UTF8Encoding(false));
                List<StickyNoteData> legacyNotes =
                    StickyNoteRepository.LoadFromFile(legacyStickyPath).GetAll();
                bool legacyNoteMigrationOk = legacyNotes.Count == 1 &&
                    legacyNotes[0].Text == legacyChinese &&
                    !legacyNotes[0].IsTodoList &&
                    String.IsNullOrEmpty(legacyNotes[0].RichTextRtf);
                if (File.Exists(legacyStickyPath)) File.Delete(legacyStickyPath);
                string legacyImportCurrent = outputPath +
                    ".legacy-import-current.dat";
                string legacyImportSource = outputPath +
                    ".legacy-import-source.dat";
                File.WriteAllText(legacyImportSource, legacyLine,
                    new UTF8Encoding(false));
                StickyNoteRepository legacyImported = StickyNoteRepository
                    .LoadFromFileWithLegacyCandidates(legacyImportCurrent,
                        new string[] { legacyImportSource });
                bool oldestFolderCacheImportOk = legacyImported.Count == 1 &&
                    legacyImported.GetAll()[0].Text == legacyChinese &&
                    File.Exists(legacyImportCurrent) &&
                    File.Exists(legacyImportSource);
                foreach (string legacyImportFile in new string[] {
                    legacyImportCurrent, legacyImportCurrent + ".bak",
                    legacyImportSource, legacyImportSource + ".bak" })
                    if (File.Exists(legacyImportFile))
                        File.Delete(legacyImportFile);
                string versionFourStickyPath = outputPath +
                    ".sticky-v4-test.dat";
                string versionFourText = "第四版便签兼容测试";
                string versionFourLine = String.Join("|", new string[] {
                    "4", "legacy-v4-note", "1", "1",
                    Color.LightYellow.ToArgb().ToString(), "10", "20",
                    "280", "230", DateTime.UtcNow.Ticks.ToString(),
                    DateTime.UtcNow.Ticks.ToString(), "0", "0",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("旧便签")),
                    String.Empty,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(versionFourText)),
                    "0", String.Empty
                });
                File.WriteAllText(versionFourStickyPath, versionFourLine,
                    new UTF8Encoding(false));
                StickyNoteRepository versionFourRepository =
                    StickyNoteRepository.LoadFromFile(versionFourStickyPath);
                List<StickyNoteData> versionFourNotes =
                    versionFourRepository.GetAll();
                bool versionFourNoteMigrationOk = versionFourNotes.Count == 1 &&
                    versionFourNotes[0].Text == versionFourText &&
                    versionFourNotes[0].FontFamilyName == "Microsoft YaHei UI" &&
                    versionFourNotes[0].FontSizeTwips == 210;
                StickyNoteData ancientDisplayData = new StickyNoteData();
                ancientDisplayData.Width = -100;
                ancientDisplayData.Height = Int32.MaxValue;
                ancientDisplayData.FontFamilyName = null;
                ancientDisplayData.RichTextRtf = "not rtf";
                ancientDisplayData.BackgroundOpacityPercent = -1;
                ancientDisplayData.IsTodoList = true;
                ancientDisplayData.IsSchedule = true;
                ancientDisplayData.DockParentId = ancientDisplayData.Id;
                bool ancientCacheDisplayRepairOk =
                    StickyNoteRepository.RepairForDisplay(
                        ancientDisplayData, true) &&
                    ancientDisplayData.Width == 280 &&
                    ancientDisplayData.Height == 700 &&
                    ancientDisplayData.FontFamilyName == "Microsoft YaHei UI" &&
                    String.IsNullOrEmpty(ancientDisplayData.RichTextRtf) &&
                    ancientDisplayData.BackgroundOpacityPercent == 10 &&
                    ancientDisplayData.IsSchedule &&
                    !ancientDisplayData.IsTodoList &&
                    String.IsNullOrEmpty(ancientDisplayData.DockParentId) &&
                    !ancientDisplayData.Visible;
                versionFourRepository.SaveToFile(versionFourStickyPath);
                versionFourNoteMigrationOk = versionFourNoteMigrationOk &&
                    File.ReadAllText(versionFourStickyPath, Encoding.UTF8)
                        .StartsWith("9|");
                if (File.Exists(versionFourStickyPath))
                    File.Delete(versionFourStickyPath);
                if (File.Exists(versionFourStickyPath + ".bak"))
                    File.Delete(versionFourStickyPath + ".bak");
                string corruptStickyPath = outputPath + ".sticky-corrupt-test.dat";
                File.WriteAllText(corruptStickyPath, "this-is-not-a-note",
                    new UTF8Encoding(false));
                StickyNoteRepository corruptRepository =
                    StickyNoteRepository.LoadFromFile(corruptStickyPath);
                string preservedCorruptPath =
                    corruptRepository.RecoveryBackupPath;
                StickyNoteData recoveredCreate = corruptRepository.Create(
                    "损坏数据恢复后仍可新建", Point.Empty);
                bool failedLoadNeverOverwritesOk =
                    corruptRepository.LoadSucceeded &&
                    corruptRepository.RecoveredFromLoadFailure &&
                    recoveredCreate != null &&
                    !String.IsNullOrEmpty(preservedCorruptPath) &&
                    File.Exists(preservedCorruptPath) &&
                    File.ReadAllText(preservedCorruptPath, Encoding.UTF8) ==
                        "this-is-not-a-note" &&
                    File.ReadAllText(corruptStickyPath, Encoding.UTF8)
                        .StartsWith("9|");
                if (File.Exists(corruptStickyPath)) File.Delete(corruptStickyPath);
                if (File.Exists(corruptStickyPath + ".bak"))
                    File.Delete(corruptStickyPath + ".bak");
                if (!String.IsNullOrEmpty(preservedCorruptPath) &&
                    File.Exists(preservedCorruptPath))
                    File.Delete(preservedCorruptPath);

                string backupRecoveryPath = outputPath +
                    ".sticky-backup-recovery-test.dat";
                File.WriteAllText(backupRecoveryPath, "broken-primary",
                    new UTF8Encoding(false));
                File.WriteAllText(backupRecoveryPath + ".bak", legacyLine,
                    new UTF8Encoding(false));
                StickyNoteRepository backupRecoveryRepository =
                    StickyNoteRepository.LoadFromFile(backupRecoveryPath);
                bool stickyBackupRecoveryOk =
                    backupRecoveryRepository.LoadSucceeded &&
                    backupRecoveryRepository.RecoveredFromLoadFailure &&
                    backupRecoveryRepository.GetAll().Count == 1 &&
                    backupRecoveryRepository.GetAll()[0].Text == legacyChinese &&
                    File.ReadAllText(backupRecoveryRepository.RecoveryBackupPath,
                        Encoding.UTF8) == "broken-primary" &&
                    File.ReadAllText(backupRecoveryPath, Encoding.UTF8)
                        .StartsWith("9|");
                string preservedBackupPrimary =
                    backupRecoveryRepository.RecoveryBackupPath;
                if (File.Exists(backupRecoveryPath))
                    File.Delete(backupRecoveryPath);
                if (File.Exists(backupRecoveryPath + ".bak"))
                    File.Delete(backupRecoveryPath + ".bak");
                if (!String.IsNullOrEmpty(preservedBackupPrimary) &&
                    File.Exists(preservedBackupPrimary))
                    File.Delete(preservedBackupPrimary);
                string tabOrderPath = outputPath + ".tab-order-test.dat";
                StickyNoteRepository tabOrderRepository =
                    StickyNoteRepository.LoadFromFile(tabOrderPath);
                StickyNoteData tabA = tabOrderRepository.Create("A", Point.Empty);
                StickyNoteData tabB = tabOrderRepository.Create("B", Point.Empty);
                StickyNoteData tabC = tabOrderRepository.Create("C", Point.Empty);
                tabA.Visible = false;
                tabB.Visible = false;
                tabC.Visible = false;
                tabOrderRepository.SaveToFile(tabOrderPath);
                tabOrderRepository.ReorderHidden(tabA, 3);
                StickyNoteRepository restoredTabOrder =
                    StickyNoteRepository.LoadFromFile(tabOrderPath);
                List<StickyNoteData> orderedTabs =
                    restoredTabOrder.GetHiddenInTabOrder();
                bool sideTabOrderPersistenceOk = orderedTabs.Count == 3 &&
                    orderedTabs[0].Text == "B" && orderedTabs[1].Text == "C" &&
                    orderedTabs[2].Text == "A" &&
                    File.ReadAllText(tabOrderPath, Encoding.UTF8).StartsWith("9|");
                if (File.Exists(tabOrderPath)) File.Delete(tabOrderPath);
                if (File.Exists(tabOrderPath + ".bak"))
                    File.Delete(tabOrderPath + ".bak");
                string dockPath = outputPath + ".dock-test.dat";
                StickyNoteRepository dockRepository =
                    StickyNoteRepository.LoadFromFile(dockPath);
                StickyNoteData dockParent = dockRepository.Create("上层",
                    new Point(100, 100));
                StickyNoteData dockChild = dockRepository.Create("下层",
                    new Point(100, 330));
                dockChild.DockParentId = dockParent.Id;
                dockRepository.SaveToFile(dockPath);
                List<StickyNoteData> restoredDockNotes =
                    StickyNoteRepository.LoadFromFile(dockPath).GetAll();
                bool dockPersistenceAndGeometryOk =
                    restoredDockNotes.Count == 2 &&
                    restoredDockNotes.Exists(delegate(StickyNoteData value)
                    {
                        return !String.IsNullOrEmpty(value.DockParentId);
                    }) &&
                    PetForm.CanDockBelow(new Rectangle(100, 330, 320, 300),
                        new Rectangle(100, 30, 320, 300), 20) &&
                    !PetForm.CanDockBelow(new Rectangle(170, 330, 320, 300),
                        new Rectangle(100, 30, 320, 300), 20);
                StickyNoteData dockInsertedTodo = dockRepository.Create(
                    "中间待办", new Point(100, 330));
                dockInsertedTodo.IsTodoList = true;
                PetForm.RewireDockChainForInsertion(dockParent,
                    dockInsertedTodo, dockInsertedTodo, dockChild);
                StickyDockGroups.NormalizeAll(new StickyNoteData[] {
                    dockParent, dockInsertedTodo, dockChild });
                bool mixedDockInsertionOk =
                    dockInsertedTodo.DockParentId == dockParent.Id &&
                    dockChild.DockParentId == dockInsertedTodo.Id &&
                    dockInsertedTodo.IsTodoList && !dockParent.IsTodoList;
                StickyNoteData mixedOrdinary = new StickyNoteData();
                StickyNoteData mixedTodo = new StickyNoteData();
                mixedTodo.IsTodoList = true;
                StickyNoteData mixedSchedule = new StickyNoteData();
                mixedSchedule.IsSchedule = true;
                StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                    mixedOrdinary, mixedTodo, mixedSchedule });
                List<StickyNoteData> mixedScheduleOrder = StickyDockGroups
                    .GetOrderedGroup(new StickyNoteData[] { mixedSchedule,
                        mixedOrdinary, mixedTodo }, mixedSchedule);
                bool scheduleMixedDockingOk =
                    mixedScheduleOrder.Count == 3 &&
                    Object.ReferenceEquals(mixedScheduleOrder[0], mixedOrdinary) &&
                    Object.ReferenceEquals(mixedScheduleOrder[1], mixedTodo) &&
                    Object.ReferenceEquals(mixedScheduleOrder[2], mixedSchedule) &&
                    mixedTodo.IsTodoList && !mixedTodo.IsSchedule &&
                    mixedSchedule.IsSchedule && !mixedSchedule.IsTodoList;
                dockParent.Visible = false;
                dockInsertedTodo.Visible = false;
                dockChild.Visible = false;
                List<StickyNoteData> storedDockOrder = PetForm
                    .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                        dockChild, dockParent, dockInsertedTodo }, dockChild,
                        false);
                bool wholeDockComponentRestoreOk =
                    PetForm.ShouldRestoreWholeDockComponent(
                        storedDockOrder.Count, true) &&
                    storedDockOrder.Count == 3 &&
                    Object.ReferenceEquals(storedDockOrder[0], dockParent) &&
                    Object.ReferenceEquals(storedDockOrder[1],
                        dockInsertedTodo) &&
                    Object.ReferenceEquals(storedDockOrder[2], dockChild);
                string savedMiddleParent = dockInsertedTodo.DockParentId;
                string savedChildParent = dockChild.DockParentId;
                dockInsertedTodo.DockParentId = "broken-parent";
                dockChild.DockParentId = String.Empty;
                List<StickyNoteData> snapshotOrder = StickyDockGroups
                    .GetOrderedGroup(new StickyNoteData[] { dockChild,
                        dockInsertedTodo, dockParent }, dockInsertedTodo);
                bool dockSnapshotSurvivesBrokenParentLinksOk =
                    snapshotOrder.Count == 3 &&
                    Object.ReferenceEquals(snapshotOrder[0], dockParent) &&
                    Object.ReferenceEquals(snapshotOrder[1],
                        dockInsertedTodo) &&
                    Object.ReferenceEquals(snapshotOrder[2], dockChild);
                dockInsertedTodo.DockParentId = savedMiddleParent;
                dockChild.DockParentId = savedChildParent;
                dockRepository.SaveToFile(dockPath);
                StickyNoteRepository persistedDockRepository =
                    StickyNoteRepository.LoadFromFile(dockPath);
                StickyNoteData persistedDockMember =
                    persistedDockRepository.Find(dockInsertedTodo.Id);
                List<StickyNoteData> persistedDockOrder = StickyDockGroups
                    .GetOrderedGroup(persistedDockRepository.GetAll(),
                        persistedDockMember);
                bool dockGroupSnapshotRoundTripOk =
                    persistedDockOrder.Count == 3 &&
                    persistedDockOrder[0].Id == dockParent.Id &&
                    persistedDockOrder[1].Id == dockInsertedTodo.Id &&
                    persistedDockOrder[2].Id == dockChild.Id &&
                    persistedDockOrder[0].DockGroupOrder == 0 &&
                    persistedDockOrder[1].DockGroupOrder == 1 &&
                    persistedDockOrder[2].DockGroupOrder == 2 &&
                    File.ReadAllText(dockPath, Encoding.UTF8)
                        .StartsWith("9|");
                bool groupRequestAlwaysRestoresAtomicallyOk =
                    PetForm.ShouldRestoreWholeDockComponent(3, false) &&
                    PetForm.ShouldRestoreWholeDockComponent(3, true) &&
                    !PetForm.ShouldRestoreWholeDockComponent(1, true);
                StickyNoteData extractA = new StickyNoteData();
                StickyNoteData extractB = new StickyNoteData();
                StickyNoteData extractC = new StickyNoteData();
                StickyNoteData extractD = new StickyNoteData();
                StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                    extractA, extractB, extractC, extractD });
                List<StickyNoteData> afterMiddleExtraction = PetForm
                    .ExtractSingleDockMember(new StickyNoteData[] { extractA,
                        extractB, extractC, extractD }, extractB);
                bool middleDockExtractionKeepsNeighborsJoinedOk =
                    afterMiddleExtraction.Count == 3 &&
                    Object.ReferenceEquals(afterMiddleExtraction[0], extractA) &&
                    Object.ReferenceEquals(afterMiddleExtraction[1], extractC) &&
                    Object.ReferenceEquals(afterMiddleExtraction[2], extractD) &&
                    extractC.DockParentId == extractA.Id &&
                    extractD.DockParentId == extractC.Id &&
                    String.IsNullOrEmpty(extractB.DockParentId) &&
                    String.IsNullOrEmpty(extractB.DockGroupId) &&
                    extractB.DockGroupOrder == -1;
                StickyNoteData hideA = new StickyNoteData();
                StickyNoteData hideB = new StickyNoteData();
                StickyNoteData hideC = new StickyNoteData();
                StickyNoteData hideD = new StickyNoteData();
                List<StickyNoteData> hideSnapshot =
                    new List<StickyNoteData>(new StickyNoteData[] {
                        hideA, hideB, hideC, hideD });
                StickyDockGroups.ApplyOrderedGroup(hideSnapshot);
                string hiddenGroupId = hideB.DockGroupId;
                PetForm.PreserveDockSlotForHiddenMember(hideSnapshot, hideB);
                bool middleXPreservesHiddenGroupSlotOk =
                    hideB.DockGroupId == hiddenGroupId &&
                    hideB.DockGroupOrder == 1 &&
                    String.IsNullOrEmpty(hideB.DockParentId) &&
                    hideC.DockParentId == hideA.Id &&
                    hideD.DockParentId == hideC.Id;
                List<StickyNoteData> hiddenMemberOpenOrder =
                    StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                        hideD, hideB, hideA, hideC }, hideB);
                foreach (StickyNoteData member in hiddenMemberOpenOrder)
                    member.Visible = true;
                StickyDockGroups.ApplyOrderedGroup(hiddenMemberOpenOrder);
                bool middleXReopenReinsertsOriginalSlotOk =
                    hiddenMemberOpenOrder.Count == 4 &&
                    Object.ReferenceEquals(hiddenMemberOpenOrder[0], hideA) &&
                    Object.ReferenceEquals(hiddenMemberOpenOrder[1], hideB) &&
                    Object.ReferenceEquals(hiddenMemberOpenOrder[2], hideC) &&
                    Object.ReferenceEquals(hiddenMemberOpenOrder[3], hideD) &&
                    hideB.DockParentId == hideA.Id &&
                    hideC.DockParentId == hideB.Id &&
                    hideD.DockParentId == hideC.Id;
                string hiddenSlotPath = outputPath +
                    ".hidden-slot-test.dat";
                StickyNoteRepository hiddenSlotRepository =
                    StickyNoteRepository.LoadFromFile(hiddenSlotPath);
                StickyNoteData persistedHideA = hiddenSlotRepository.Create(
                    "A", Point.Empty);
                StickyNoteData persistedHideB = hiddenSlotRepository.Create(
                    "B", Point.Empty);
                StickyNoteData persistedHideC = hiddenSlotRepository.Create(
                    "C", Point.Empty);
                StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                    persistedHideA, persistedHideB, persistedHideC });
                PetForm.PreserveDockSlotForHiddenMember(
                    new StickyNoteData[] { persistedHideA, persistedHideB,
                        persistedHideC }, persistedHideB);
                hiddenSlotRepository.SaveToFile(hiddenSlotPath);
                StickyNoteRepository restoredHiddenSlotRepository =
                    StickyNoteRepository.LoadFromFile(hiddenSlotPath);
                StickyNoteData restoredHiddenMiddle =
                    restoredHiddenSlotRepository.Find(persistedHideB.Id);
                List<StickyNoteData> restoredHiddenSlotOrder =
                    StickyDockGroups.GetOrderedGroup(
                        restoredHiddenSlotRepository.GetAll(),
                        restoredHiddenMiddle);
                bool middleXHiddenSlotRestartPersistenceOk =
                    restoredHiddenSlotOrder.Count == 3 &&
                    !restoredHiddenSlotOrder[1].Visible &&
                    restoredHiddenSlotOrder[1].Id == persistedHideB.Id &&
                    restoredHiddenSlotOrder[1].DockGroupOrder == 1 &&
                    restoredHiddenSlotOrder[2].DockParentId ==
                        restoredHiddenSlotOrder[0].Id;
                if (File.Exists(hiddenSlotPath)) File.Delete(hiddenSlotPath);
                if (File.Exists(hiddenSlotPath + ".bak"))
                    File.Delete(hiddenSlotPath + ".bak");
                StickyNoteData mergeA = new StickyNoteData();
                StickyNoteData mergeB = new StickyNoteData();
                StickyNoteData mergeC = new StickyNoteData();
                StickyNoteData mergeD = new StickyNoteData();
                StickyNoteData mergeE = new StickyNoteData();
                List<StickyNoteData> mergeTarget =
                    new List<StickyNoteData>(new StickyNoteData[] {
                        mergeA, mergeB, mergeC });
                List<StickyNoteData> mergeSource =
                    new List<StickyNoteData>(new StickyNoteData[] {
                        mergeD, mergeE });
                StickyDockGroups.ApplyOrderedGroup(mergeTarget);
                StickyDockGroups.ApplyOrderedGroup(mergeSource);
                mergeB.Visible = false;
                mergeE.Visible = false;
                StickyDockGroups.ApplyOrderedGroup(mergeTarget);
                StickyDockGroups.ApplyOrderedGroup(mergeSource);
                List<StickyNoteData> mergedPartialSnapshots = PetForm
                    .MergeDockSnapshotsAfterParent(mergeTarget, mergeA,
                        mergeSource);
                bool partialHiddenGroupsMergeWithoutLosingSlotsOk =
                    mergedPartialSnapshots.Count == 5 &&
                    Object.ReferenceEquals(mergedPartialSnapshots[0], mergeA) &&
                    Object.ReferenceEquals(mergedPartialSnapshots[1], mergeD) &&
                    Object.ReferenceEquals(mergedPartialSnapshots[2], mergeE) &&
                    Object.ReferenceEquals(mergedPartialSnapshots[3], mergeB) &&
                    Object.ReferenceEquals(mergedPartialSnapshots[4], mergeC) &&
                    mergeD.DockParentId == mergeA.Id &&
                    mergeC.DockParentId == mergeD.Id &&
                    String.IsNullOrEmpty(mergeE.DockParentId) &&
                    String.IsNullOrEmpty(mergeB.DockParentId);
                // Full second-cycle regression: restore a stack, extract one
                // middle member, insert it at another seam, then damage one
                // live parent link just before group close.  The persisted
                // snapshot must still win and repair the complete order.
                PetForm.RewireDockChainForInsertion(extractA, extractB,
                    extractB, extractC);
                List<StickyNoteData> secondCycleLive = PetForm
                    .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                        extractD, extractC, extractA, extractB },
                        extractA, true);
                List<StickyNoteData> secondCycleStoredBeforeCommit =
                    StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                        extractD, extractC, extractA, extractB }, extractA);
                List<StickyNoteData> secondCycleCommit = PetForm
                    .SelectMoreCompleteDockOrder(secondCycleLive,
                        secondCycleStoredBeforeCommit);
                StickyDockGroups.ApplyOrderedGroup(secondCycleCommit);
                extractC.DockParentId = String.Empty;
                List<StickyNoteData> brokenSecondCycleLive = PetForm
                    .BuildDockChainOrderFromNotes(new StickyNoteData[] {
                        extractD, extractC, extractA, extractB }, extractA,
                        true);
                List<StickyNoteData> completeSecondCycleSnapshot =
                    StickyDockGroups.GetOrderedGroup(new StickyNoteData[] {
                        extractD, extractC, extractA, extractB }, extractD);
                List<StickyNoteData> closeSecondCycleOrder = PetForm
                    .SelectMoreCompleteDockOrder(brokenSecondCycleLive,
                        completeSecondCycleSnapshot);
                StickyDockGroups.ApplyOrderedGroup(closeSecondCycleOrder);
                bool rearrangedGroupSecondRestoreCycleOk =
                    closeSecondCycleOrder.Count == 4 &&
                    Object.ReferenceEquals(closeSecondCycleOrder[0], extractA) &&
                    Object.ReferenceEquals(closeSecondCycleOrder[1], extractB) &&
                    Object.ReferenceEquals(closeSecondCycleOrder[2], extractC) &&
                    Object.ReferenceEquals(closeSecondCycleOrder[3], extractD) &&
                    extractB.DockParentId == extractA.Id &&
                    extractC.DockParentId == extractB.Id &&
                    extractD.DockParentId == extractC.Id;
                List<StickyNoteData> repeatedMembers =
                    new List<StickyNoteData>();
                for (int index = 0; index < 6; index++)
                    repeatedMembers.Add(new StickyNoteData());
                StickyDockGroups.ApplyOrderedGroup(repeatedMembers);
                bool repeatedRearrangeRestoreCyclesOk = true;
                for (int cycle = 0; cycle < 18; cycle++)
                {
                    List<StickyNoteData> current = StickyDockGroups
                        .GetOrderedGroup(repeatedMembers, repeatedMembers[0]);
                    if (current.Count != repeatedMembers.Count)
                    {
                        repeatedRearrangeRestoreCyclesOk = false;
                        break;
                    }
                    int extractIndex = 1 + cycle % (current.Count - 1);
                    StickyNoteData moved = current[extractIndex];
                    List<StickyNoteData> remainder = PetForm
                        .ExtractSingleDockMember(current, moved);
                    int targetIndex = cycle % remainder.Count;
                    StickyNoteData cycleParent = remainder[targetIndex];
                    StickyNoteData previousChild = targetIndex + 1 <
                        remainder.Count ? remainder[targetIndex + 1] : null;
                    PetForm.RewireDockChainForInsertion(cycleParent, moved, moved,
                        previousChild);
                    List<StickyNoteData> liveCycle = PetForm
                        .BuildDockChainOrderFromNotes(repeatedMembers,
                            remainder[0], true);
                    List<StickyNoteData> storedCycle = StickyDockGroups
                        .GetOrderedGroup(repeatedMembers, cycleParent);
                    List<StickyNoteData> committedCycle = PetForm
                        .SelectMoreCompleteDockOrder(liveCycle, storedCycle);
                    StickyDockGroups.ApplyOrderedGroup(committedCycle);
                    List<StickyNoteData> randomOpenOrder = StickyDockGroups
                        .GetOrderedGroup(new StickyNoteData[] {
                            repeatedMembers[5], repeatedMembers[2],
                            repeatedMembers[0], repeatedMembers[4],
                            repeatedMembers[1], repeatedMembers[3] },
                            moved);
                    if (committedCycle.Count != repeatedMembers.Count ||
                        randomOpenOrder.Count != repeatedMembers.Count)
                    {
                        repeatedRearrangeRestoreCyclesOk = false;
                        break;
                    }
                    for (int position = 0; position < randomOpenOrder.Count;
                        position++)
                    {
                        StickyNoteData member = randomOpenOrder[position];
                        string expectedParent = position == 0 ? String.Empty :
                            randomOpenOrder[position - 1].Id;
                        if (member.DockGroupOrder != position ||
                            !String.Equals(member.DockParentId, expectedParent,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            repeatedRearrangeRestoreCyclesOk = false;
                            break;
                        }
                    }
                    if (!repeatedRearrangeRestoreCyclesOk) break;
                }
                dockParent.Visible = true;
                dockInsertedTodo.Visible = true;
                dockChild.Visible = true;
                StickyDockGroups.ApplyOrderedGroup(new StickyNoteData[] {
                    dockParent, dockInsertedTodo, dockChild });
                PetForm.RewireDockChainAfterMemberClose(dockInsertedTodo,
                    dockChild);
                bool lowerDockCloseRewiresNeighborsOk =
                    String.IsNullOrEmpty(dockInsertedTodo.DockParentId) &&
                    dockChild.DockParentId == dockParent.Id;
                Point dialogBelow = StickyNoteForm
                    .CalculateAppearanceDialogLocation(
                        new Rectangle(300, 100, 320, 300),
                        new Size(520, 260),
                        new Rectangle(0, 0, 1200, 900));
                Point dialogAbove = StickyNoteForm
                    .CalculateAppearanceDialogLocation(
                        new Rectangle(850, 700, 320, 180),
                        new Size(520, 260),
                        new Rectangle(0, 0, 1200, 900));
                bool appearanceDialogBelowAndBoundedOk =
                    dialogBelow.Y == 408 && dialogBelow.X >= 0 &&
                    dialogAbove.Y < 700 && dialogAbove.X >= 0 &&
                    dialogAbove.X + 520 <= 1200;
                List<Rectangle> unifiedLayout = PetForm
                    .CalculateUnifiedDockLayout(new Size[] {
                        new Size(320, 300), new Size(500, 240),
                        new Size(380, 260) }, 120, 80, 460);
                bool unifiedDockGroupResizeOk = unifiedLayout.Count == 3 &&
                    unifiedLayout[0] == new Rectangle(120, 80, 460, 300) &&
                    unifiedLayout[1] == new Rectangle(120, 380, 460, 240) &&
                    unifiedLayout[2] == new Rectangle(120, 620, 460, 260);
                bool dockRootAnchorPreservedOk = unifiedLayout.Count == 3 &&
                    unifiedLayout[0].Location == new Point(120, 80);
                Size dividerMiddle = PetForm.CalculateDockDividerHeights(
                    300, 250, 300);
                Size dividerMinimum = PetForm.CalculateDockDividerHeights(
                    300, 430, 300);
                bool dockDividerPreservesTotalHeightOk =
                    dividerMiddle == new Size(250, 350) &&
                    dividerMinimum == new Size(380, 220) &&
                    dividerMiddle.Width + dividerMiddle.Height == 600 &&
                    dividerMinimum.Width + dividerMinimum.Height == 600;
                Size dividerRange = PetForm.CalculateDockDividerRange(
                    300, 300);
                Size tallDividerRange = PetForm.CalculateDockDividerRange(
                    500, 500);
                bool dockDividerCannotCrossNeighborOk =
                    dividerRange == new Size(220, 380) &&
                    tallDividerRange == new Size(300, 700);
                bool wideNarrowDockingOk = PetForm.CanDockBelow(
                    new Rectangle(80, 400, 900, 300),
                    new Rectangle(400, 100, 280, 300), 20) &&
                    PetForm.CanDockBelow(
                        new Rectangle(400, 400, 280, 300),
                        new Rectangle(80, 100, 900, 300), 20);
                bool longDockCoordinateGuardOk =
                    PetForm.IsDockCoordinateRangeSafe(100,
                        new int[] { 700, 700, 700 }) &&
                    !PetForm.IsDockCoordinateRangeSafe(29000,
                        new int[] { 700, 700 });
                bool dockCloseHierarchyOk =
                    PetForm.ShouldCollapseWholeDockGroup(0, 3) &&
                    !PetForm.ShouldCollapseWholeDockGroup(1, 3) &&
                    !PetForm.ShouldCollapseWholeDockGroup(0, 1);
                long styleWithMaximize = 0x00040000L | 0x00010000L;
                bool stickyNativeSnapDisabledOk =
                    StickyNoteForm.RemoveMaximizeStyle(styleWithMaximize) ==
                        0x00040000L;
                Rectangle recoveredDrag = StickyNoteForm
                    .CalculateRecoveredHeaderDragBounds(
                        new Rectangle(100, 100, 320, 300),
                        new Rectangle(0, 0, 1920, 1080),
                        new Point(500, 400), new Point(20, 10), true);
                bool firstDragGeometryRecoveryOk = recoveredDrag ==
                    new Rectangle(480, 390, 320, 300);
                Point reachable = PetForm.CalculateHeaderReachableTranslation(
                    new Rectangle(100, -200, 400, 32),
                    new Rectangle(0, 0, 1200, 900));
                bool detachedDockReturnsOnScreenOk = reachable.Y == 200;
                Point recoveredPrimary = PetForm.CalculateStickyRecoveryAnchor(
                    new Rectangle(0, 0, 1920, 1040),
                    new Rectangle(20, 700, 192, 208),
                    new Size(320, 300), 0);
                Point recoveredSecondary = PetForm.CalculateStickyRecoveryAnchor(
                    new Rectangle(-1920, 0, 1920, 1040),
                    new Rectangle(-300, 700, 192, 208),
                    new Size(320, 300), 1);
                bool stickyScreenRecoveryAnchorOk =
                    recoveredPrimary.X >= 0 && recoveredPrimary.Y >= 0 &&
                    recoveredPrimary.Y <= 1008 &&
                    recoveredSecondary.X >= -1920 &&
                    recoveredSecondary.X <= -320 &&
                    recoveredSecondary.Y >= 0 &&
                    recoveredSecondary.Y <= 1008;
                bool deliberateSplitGestureOk =
                    PetForm.CancelsDockSplitHold(120, 20, 0) &&
                    !PetForm.CancelsDockSplitHold(600, 20, 0) &&
                    !PetForm.CancelsDockSplitHold(120, 3, 3);
                bool rootGroupDragNeverSplitsOk =
                    !PetForm.IsDockSplitEligible(String.Empty, 3) &&
                    PetForm.IsDockSplitEligible("parent-note", 3) &&
                    !PetForm.IsDockSplitEligible("parent-note", 1);
                bool steadyDockGuideOk;
                using (DockPulseIndicatorForm guide =
                    new DockPulseIndicatorForm(Color.DeepSkyBlue, 0))
                    steadyDockGuideOk = guide.UsesSteadyOpacityForTest;
                if (File.Exists(dockPath)) File.Delete(dockPath);
                if (File.Exists(dockPath + ".bak")) File.Delete(dockPath + ".bak");
                bool reminderDefaultCurrentTimeOk = Math.Abs(
                    (ReminderDialog.DefaultSuggestedLocal() - DateTime.Now).TotalSeconds) < 3;
                IList<StickyLinkMatch> detectedLinks =
                    StickyNoteLinkDetector.Find(
                        "C:\\Users\\Penny pet\\进度表.xlsx\r\n" +
                        "https://www.baidu.com/\r\n普通文字");
                bool ordinaryStickyLinkDetectionOk = detectedLinks.Count == 2 &&
                    detectedLinks[0].IsLocalPath &&
                    detectedLinks[0].Text ==
                        "C:\\Users\\Penny pet\\进度表.xlsx" &&
                    !detectedLinks[1].IsLocalPath &&
                    detectedLinks[1].Target.StartsWith(
                        "https://www.baidu.com/", StringComparison.Ordinal);
                bool dangerousLocalLinkPolicyOk =
                    StickyLinkService.Classify("C:\\Tools\\setup.EXE", true) ==
                        StickyLinkOpenRisk.ExecutableOrScript &&
                    StickyLinkService.Classify("C:\\Tools\\run.ps1", true) ==
                        StickyLinkOpenRisk.ExecutableOrScript &&
                    StickyLinkService.Classify("C:\\Docs\\target.lnk", true) ==
                        StickyLinkOpenRisk.Shortcut &&
                    StickyLinkService.Classify("\\\\server\\share\\report.pdf",
                        true) == StickyLinkOpenRisk.NetworkShare &&
                    StickyLinkService.Classify("C:\\Docs\\report.xlsx", true) ==
                        StickyLinkOpenRisk.None &&
                    StickyLinkService.Classify("https://www.baidu.com/", false) ==
                        StickyLinkOpenRisk.None &&
                    StickyLinkService.ConfirmationMessage(
                        StickyLinkOpenRisk.ExecutableOrScript,
                        "C:\\Tools\\setup.exe").Contains("确定继续");
                StickyNoteData linkNoteData = new StickyNoteData();
                linkNoteData.Text =
                    "C:\\Users\\Penny pet\\进度表.xlsx\r\n" +
                    "https://www.baidu.com/";
                using (StickyNoteForm linkNote = new StickyNoteForm(
                    linkNoteData, true))
                    ordinaryStickyLinkDetectionOk =
                        ordinaryStickyLinkDetectionOk &&
                        linkNote.ExerciseOrdinaryLinkRefreshForTest();
                bool ok = atlasOk && animationTimingFromArtPackageOk &&
                    applicationIconEmbeddedOk &&
                    startupLoadingFrameEmbeddedOk &&
                    startupLoadingUsesSavedScaleOk &&
                    contactAuthorFeatureOk &&
                    minuteOk && cancelOk && fiveRemindersOk &&
                    sixthReminderBlocked && reminderMemoryOk &&
                    settingsBackupRecoveryOk && reminderReplaceOk &&
                    concreteDateTimeOk &&
                    linkedReminderOk && multipleLinkedReminderOk &&
                    stickyPersistenceOk && pinActionTextOk && todoPinActionTextOk &&
                    richTextPersistenceOk && richTextNoSilentTruncationOk &&
                    unforcedMultilingualImeOk &&
                    multilingualNotePersistenceOk && imeCompatibleEditorOk &&
                    singleWindowStickyInputOk &&
                    legacyNoteMigrationOk && oldestFolderCacheImportOk &&
                    versionFourNoteMigrationOk &&
                    todoPersistenceOk && schedulePersistenceOk &&
                    scheduleCountdownOk && scheduleFontChoicesOk &&
                    scheduleDateMouseWheelOk && schedulePinMarkerToggleOk &&
                    expiredReminderDiscardedOnLaunchOk && todoGroupingOk &&
                    reminderBannerCountdownOk && reminderCompactBannerOk &&
                    reminderSelectionActionsOk && inlineCreationActionsRemovedOk &&
                    reminderFirstClickStableOk &&
                    reminderBlankAreaClearOk &&
                    reminderBannerRefreshInPlaceOk &&
                    todoWrapAndInlineEditOk &&
                    todoOverallFontSizeOk && dedicatedRowContextMenusOk &&
                    shortItemWeightedLimitOk &&
                    todoMarkerRoundTripOk &&
                    todoPlainTextProjectionOk && multilingualEditorInputOk &&
                    tabSwitchContentPreservedOk &&
                    stickyResourceLimitsOk &&
                    stickyResizePaintingOk && richTextToolbarOk &&
                    smoothFormatInteractionOk && deferredInitialFocusSafeOk &&
                    formatToolbarFocusOk &&
                    formatSelectorsAlwaysBlackOk && bodyTextColorSwitchOk &&
                    dockResizeRoleOk && groupTopMostSyncOk &&
                    nativeWindowStyleAppliedOk &&
                    firstFormatCommitOk && emptyNoteFormattingOk &&
                    caretTypingFormatSwitchOk &&
                    singleNativeImeCommitAfterFormatOk &&
                    unifiedNoteContextMenusOk &&
                    fixedNoteTypeActionsOk && ancientCacheDisplayRepairOk &&
                    fontSizeParsingOk && chineseFontsFirstOk &&
                    installedFontListCacheOk &&
                    stableFormatSelectorModelOk && sharedFontLifetimeOk &&
                    reminderSizePreviewOk && reminderLiveSizePreviewOk &&
                    highDpiStickyLayoutOk &&
                    reminderDefaultCurrentTimeOk &&
                    ordinaryStickyLinkDetectionOk &&
                    dangerousLocalLinkPolicyOk &&
                    softPaletteOk && automaticNoteBackupOk &&
                    fullWidthLatinNormalizationOk && renameInitialFocusOk &&
                    sideTabOverflowOk && sideTabDeleteCommandOk &&
                    sideTabDragPreviewOk && sideTabDeferredDropCommitOk &&
                    sideTabPreviewClearsBothSidesOk &&
                    sideTabScaledGapOk &&
                    sideTabVectorIconColorOk && imeAnimationGuardOk &&
                    imeAutoSaveGuardOk &&
                    sideTabOrderPersistenceOk && dockPersistenceAndGeometryOk &&
                    mixedDockInsertionOk && scheduleMixedDockingOk &&
                    appearanceDialogBelowAndBoundedOk &&
                    lowerDockCloseRewiresNeighborsOk &&
                    wholeDockComponentRestoreOk &&
                    dockSnapshotSurvivesBrokenParentLinksOk &&
                    dockGroupSnapshotRoundTripOk &&
                    groupRequestAlwaysRestoresAtomicallyOk &&
                    middleDockExtractionKeepsNeighborsJoinedOk &&
                    middleXPreservesHiddenGroupSlotOk &&
                    middleXReopenReinsertsOriginalSlotOk &&
                    middleXHiddenSlotRestartPersistenceOk &&
                    partialHiddenGroupsMergeWithoutLosingSlotsOk &&
                    rearrangedGroupSecondRestoreCycleOk &&
                    repeatedRearrangeRestoreCyclesOk &&
                    wideNarrowDockingOk &&
                    unifiedDockGroupResizeOk && dockRootAnchorPreservedOk &&
                    dockDividerPreservesTotalHeightOk &&
                    dockDividerCannotCrossNeighborOk &&
                    longDockCoordinateGuardOk &&
                    dockCloseHierarchyOk && stickyNativeSnapDisabledOk &&
                    firstDragGeometryRecoveryOk &&
                    detachedDockReturnsOnScreenOk && deliberateSplitGestureOk &&
                    rootGroupDragNeverSplitsOk && steadyDockGuideOk &&
                    managerMarqueeBatchDeleteOk &&
                    ownProcessHookIsolationOk && reverseReminderStepOk &&
                    failedLoadNeverOverwritesOk && stickyBackupRecoveryOk &&
                    keyboardTextScaleChoicesOk &&
                    preAlertWindowOk && reminderClockWhileEditingOk &&
                    reminderBannerTickThrottleOk &&
                    startupDefaultOk && keyboardHookOptInDefaultOk &&
                    keyboardPrivacyNoticePersistenceOk &&
                    startupLoadingReadinessGateOk &&
                    goodbyeAnimationOk &&
                    notificationAnimationOk && notificationTriggerPlaybackOk &&
                    dueReminderBubblePersistentOk &&
                    dueReminderBubbleUsesOwnSizeOk &&
                    dueReminderBubbleReplacementOk &&
                    preAlertBubbleProtectionOk &&
                    notificationAnimationSingleCycleOk &&
                    dragUsesSecondIdleRowOk && idleRandomRowsOk &&
                    typingRandomRowsOk && idleThoughtProbabilityReducedOk &&
                    guitarFailureProbabilityReducedOk && startupLazyArtLoadOk &&
                    interactionArtPreloadOk && failedArtPreloadRetryOk &&
                    startupCacheEmbeddedOk &&
                    innerOutlineOk &&
                    greenHaloOk && smoothTimingOk && hoverBubbleCopyOk &&
                    styledReminderBubbleOk && bubbleThemeAndKeyboardFontOk &&
                    bubbleManualPositionOk && dragBubbleSuppressionOk &&
                    silentModePersistenceOk && silentModeBehaviorOk &&
                    manualAnimationRandomPoolOk &&
                    manualSpecialAnimationProbabilityReducedOk &&
                    manualAnimationCooldownOk &&
                    clickDragThresholdOk &&
                    bubblePositionMathOk && scaleRangeOk && keyDisplayOk &&
                    heldKeyOverlayStableOk &&
                    passwordSuppressionOk && keyboardPrivacyGenerationOk &&
                    adaptiveContrastOk;
                string json = "{\n" +
                    "  \"ok\": " + Bool(ok) + ",\n" +
                    "  \"art_package\": {\"width\": " + width + ", \"height\": " + height +
                    ", \"ok\": " + Bool(atlasOk) + "},\n" +
                    "  \"animation_timing_from_art_package_ok\": " + Bool(
                        animationTimingFromArtPackageOk) + ",\n" +
                    "  \"application_icon_embedded_ok\": " + Bool(
                        applicationIconEmbeddedOk) + ",\n" +
                    "  \"startup_loading_frame_embedded_ok\": " + Bool(
                        startupLoadingFrameEmbeddedOk) + ",\n" +
                    "  \"startup_loading_uses_saved_pet_scale_ok\": " + Bool(
                        startupLoadingUsesSavedScaleOk) + ",\n" +
                    "  \"contact_author_feature_ok\": " + Bool(
                        contactAuthorFeatureOk) + ",\n" +
                    "  \"contact_author_xiaohongshu_only_ok\": " + Bool(
                        contactAuthorFeatureOk) + ",\n" +
                    "  \"minute_timer_ok\": " + Bool(minuteOk) + ",\n" +
                    "  \"cancel_ok\": " + Bool(cancelOk) + ",\n" +
                    "  \"five_reminders_ok\": " + Bool(fiveRemindersOk) + ",\n" +
                    "  \"sixth_reminder_blocked\": " + Bool(sixthReminderBlocked) + ",\n" +
                    "  \"reminder_memory_ok\": " + Bool(reminderMemoryOk) + ",\n" +
                    "  \"settings_backup_recovery_ok\": " + Bool(
                        settingsBackupRecoveryOk) + ",\n" +
                    "  \"reminder_replace_preserves_link_ok\": " + Bool(
                        reminderReplaceOk) + ",\n" +
                    "  \"linked_reminder_note_ok\": " + Bool(linkedReminderOk) + ",\n" +
                    "  \"multiple_reminders_per_note_ok\": " + Bool(
                        multipleLinkedReminderOk) + ",\n" +
                    "  \"sticky_note_persistence_ok\": " + Bool(stickyPersistenceOk) + ",\n" +
                    "  \"sticky_pin_action_text_ok\": " + Bool(pinActionTextOk) + ",\n" +
                    "  \"todo_sticky_pin_action_text_ok\": " + Bool(
                        todoPinActionTextOk) + ",\n" +
                    "  \"sticky_rich_text_persistence_ok\": " + Bool(
                        richTextPersistenceOk) + ",\n" +
                    "  \"sticky_rich_text_no_silent_truncation_ok\": " + Bool(
                        richTextNoSilentTruncationOk) + ",\n" +
                    "  \"multilingual_note_persistence_ok\": " + Bool(multilingualNotePersistenceOk) + ",\n" +
                    "  \"ime_compatible_editor_ok\": " + Bool(imeCompatibleEditorOk) + ",\n" +
                    "  \"sticky_single_window_input_ok\": " + Bool(
                        singleWindowStickyInputOk) + ",\n" +
                    "  \"legacy_note_migration_ok\": " + Bool(legacyNoteMigrationOk) + ",\n" +
                    "  \"old_fish_shanying_cache_import_ok\": " + Bool(
                        oldestFolderCacheImportOk) + ",\n" +
                    "  \"version4_note_font_migration_ok\": " + Bool(
                        versionFourNoteMigrationOk) + ",\n" +
                    "  \"ancient_cache_display_repair_ok\": " + Bool(
                        ancientCacheDisplayRepairOk) + ",\n" +
                    "  \"todo_persistence_ok\": " + Bool(todoPersistenceOk) + ",\n" +
                    "  \"schedule_persistence_ok\": " + Bool(
                        schedulePersistenceOk) + ",\n" +
                    "  \"schedule_countdown_ok\": " + Bool(
                        scheduleCountdownOk) + ",\n" +
                    "  \"schedule_five_tier_font_ok\": " + Bool(
                        scheduleFontChoicesOk) + ",\n" +
                    "  \"schedule_date_mouse_wheel_ok\": " + Bool(
                        scheduleDateMouseWheelOk) + ",\n" +
                    "  \"schedule_pin_marker_toggle_idempotent_ok\": " + Bool(
                        schedulePinMarkerToggleOk) + ",\n" +
                    "  \"expired_reminder_discarded_after_closed_app_ok\": " + Bool(
                        expiredReminderDiscardedOnLaunchOk) + ",\n" +
                    "  \"todo_pending_completed_groups_ok\": " + Bool(todoGroupingOk) + ",\n" +
                    "  \"reminder_banner_countdown_ok\": " + Bool(reminderBannerCountdownOk) + ",\n" +
                    "  \"reminder_banner_compact_font_ok\": " + Bool(
                        reminderCompactBannerOk) + ",\n" +
                    "  \"reminder_selection_actions_ok\": " + Bool(
                        reminderSelectionActionsOk) + ",\n" +
                    "  \"inline_new_reminder_and_list_removed_ok\": " + Bool(
                        inlineCreationActionsRemovedOk) + ",\n" +
                    "  \"reminder_first_click_survives_refresh_ok\": " + Bool(
                        reminderFirstClickStableOk) + ",\n" +
                    "  \"reminder_banner_refreshes_in_place_ok\": " + Bool(
                        reminderBannerRefreshInPlaceOk) + ",\n" +
                    "  \"reminder_blank_area_clears_selection_ok\": " + Bool(
                        reminderBlankAreaClearOk) + ",\n" +
                    "  \"reminder_content_wraps_without_ellipsis_ok\": " + Bool(
                        reminderSelectionActionsOk) + ",\n" +
                    "  \"todo_double_click_inline_edit_ok\": " + Bool(
                        todoWrapAndInlineEditOk) + ",\n" +
                    "  \"todo_content_wraps_without_ellipsis_ok\": " + Bool(
                        todoWrapAndInlineEditOk) + ",\n" +
                    "  \"todo_overall_font_size_ok\": " + Bool(
                        todoOverallFontSizeOk) + ",\n" +
                    "  \"dedicated_reminder_todo_context_menus_ok\": " + Bool(
                        dedicatedRowContextMenusOk) + ",\n" +
                    "  \"reminder_todo_weighted_50_cjk_limit_ok\": " + Bool(
                        shortItemWeightedLimitOk) + ",\n" +
                    "  \"todo_marker_round_trip_ok\": " + Bool(todoMarkerRoundTripOk) + ",\n" +
                    "  \"todo_plain_text_projection_ok\": " + Bool(
                        todoPlainTextProjectionOk) + ",\n" +
                    "  \"multilingual_text_input_ok\": " + Bool(
                        multilingualEditorInputOk) + ",\n" +
                    "  \"input_method_not_forced_to_chinese_ok\": " + Bool(
                        unforcedMultilingualImeOk) + ",\n" +
                    "  \"format_tab_switch_content_preserved_ok\": " + Bool(
                        tabSwitchContentPreservedOk) + ",\n" +
                    "  \"sticky_resource_limits_ok\": " + Bool(stickyResourceLimitsOk) + ",\n" +
                    "  \"maximum_sticky_notes\": " + StickyNoteLimits.MaximumNotes + ",\n" +
                    "  \"maximum_todos_per_note\": " +
                        StickyNoteLimits.MaximumTodoItemsPerNote + ",\n" +
                    "  \"sticky_resize_buffered_painting_ok\": " + Bool(stickyResizePaintingOk) + ",\n" +
                    "  \"sticky_rich_text_toolbar_ok\": " + Bool(
                        richTextToolbarOk) + ",\n" +
                    "  \"sticky_format_interaction_smooth_ok\": " + Bool(
                        smoothFormatInteractionOk) + ",\n" +
                    "  \"sticky_first_dropdown_focus_not_stolen_ok\": " + Bool(
                        deferredInitialFocusSafeOk) + ",\n" +
                    "  \"sticky_format_toolbar_preserves_selection_focus_ok\": " + Bool(
                        formatToolbarFocusOk) + ",\n" +
                    "  \"sticky_format_selectors_always_black_ok\": " + Bool(
                        formatSelectorsAlwaysBlackOk) + ",\n" +
                    "  \"sticky_body_text_color_switch_ok\": " + Bool(
                        bodyTextColorSwitchOk) + ",\n" +
                    "  \"sticky_group_outer_resize_roles_ok\": " + Bool(
                        dockResizeRoleOk) + ",\n" +
                    "  \"sticky_group_topmost_sync_ok\": " + Bool(
                        groupTopMostSyncOk) + ",\n" +
                    "  \"sticky_first_format_commit_ok\": " + Bool(
                        firstFormatCommitOk) + ",\n" +
                    "  \"empty_sticky_font_and_size_before_typing_ok\": " + Bool(
                        emptyNoteFormattingOk) + ",\n" +
                    "  \"sticky_existing_text_caret_format_switch_ok\": " + Bool(
                        caretTypingFormatSwitchOk) + ",\n" +
                    "  \"sticky_native_ime_single_commit_after_format_ok\": " + Bool(
                        singleNativeImeCommitAfterFormatOk) + ",\n" +
                    "  \"sticky_editor_and_window_context_actions_ok\": " + Bool(
                        unifiedNoteContextMenusOk) + ",\n" +
                    "  \"sticky_note_types_never_convert_ok\": " + Bool(
                        fixedNoteTypeActionsOk) + ",\n" +
                    "  \"sticky_font_size_parsing_ok\": " + Bool(
                        fontSizeParsingOk) + ",\n" +
                    "  \"sticky_chinese_fonts_first_ok\": " + Bool(
                        chineseFontsFirstOk) + ",\n" +
                    "  \"sticky_installed_font_list_cached_ok\": " + Bool(
                        installedFontListCacheOk) + ",\n" +
                    "  \"sticky_format_selector_single_event_model_ok\": " + Bool(
                        stableFormatSelectorModelOk) + ",\n" +
                    "  \"shared_font_lifetime_ok\": " + Bool(
                        sharedFontLifetimeOk) + ",\n" +
                    "  \"reminder_size_preview_ok\": " + Bool(
                        reminderSizePreviewOk) + ",\n" +
                    "  \"standalone_reminder_no_auto_sticky_option_ok\": " +
                        Bool(standaloneReminderNoAutoStickyOptionOk) + ",\n" +
                    "  \"reminder_live_note_size_preview_ok\": " + Bool(
                        reminderLiveSizePreviewOk) + ",\n" +
                    "  \"sticky_high_dpi_layout_ok\": " + Bool(
                        highDpiStickyLayoutOk) + ",\n" +
                    "  \"reminder_default_current_time_ok\": " + Bool(reminderDefaultCurrentTimeOk) + ",\n" +
                    "  \"ordinary_sticky_web_and_local_links_ok\": " + Bool(
                        ordinaryStickyLinkDetectionOk) + ",\n" +
                    "  \"dangerous_local_link_confirmation_policy_ok\": " + Bool(
                        dangerousLocalLinkPolicyOk) + ",\n" +
                    "  \"soft_sticky_palette_ok\": " + Bool(softPaletteOk) + ",\n" +
                    "  \"full_width_latin_normalization_ok\": " + Bool(fullWidthLatinNormalizationOk) + ",\n" +
                    "  \"rename_initial_focus_ok\": " + Bool(renameInitialFocusOk) + ",\n" +
                    "  \"side_tab_left_then_right_overflow_ok\": " + Bool(sideTabOverflowOk) + ",\n" +
                    "  \"side_tab_delete_command_ok\": " + Bool(sideTabDeleteCommandOk) + ",\n" +
                    "  \"side_tab_drag_preview_ok\": " + Bool(sideTabDragPreviewOk) + ",\n" +
                    "  \"side_tab_drop_commit_after_drag_loop_ok\": " + Bool(
                        sideTabDeferredDropCommitOk) + ",\n" +
                    "  \"side_tab_preview_clears_both_sides_ok\": " + Bool(
                        sideTabPreviewClearsBothSidesOk) + ",\n" +
                    "  \"side_tab_explicit_source_keeps_target_first_ok\": " + Bool(
                        sideTabExplicitSourceKeepsTargetFirstOk) + ",\n" +
                    "  \"side_tab_target_never_marked_as_source_ok\": " + Bool(
                        sideTabTargetNeverMarkedAsSourceOk) + ",\n" +
                    "  \"side_tab_exclusive_canvas_state_ok\": " + Bool(
                        sideTabExclusiveCanvasStateOk) + ",\n" +
                    "  \"side_tab_scaled_visual_gap_halved_ok\": " + Bool(
                        sideTabScaledGapOk) + ",\n" +
                    "  \"side_tab_vector_icon_uses_darker_tab_color_ok\": " + Bool(
                        sideTabVectorIconColorOk) + ",\n" +
                    "  \"side_tab_order_persistence_ok\": " + Bool(sideTabOrderPersistenceOk) + ",\n" +
                    "  \"sticky_bottom_dock_persistence_and_geometry_ok\": " + Bool(
                        dockPersistenceAndGeometryOk) + ",\n" +
                    "  \"sticky_mixed_type_middle_insertion_ok\": " + Bool(
                        mixedDockInsertionOk) + ",\n" +
                    "  \"schedule_mixed_with_note_and_todo_docking_ok\": " + Bool(
                        scheduleMixedDockingOk) + ",\n" +
                    "  \"sticky_lower_close_rewires_neighbors_ok\": " + Bool(
                        lowerDockCloseRewiresNeighborsOk) + ",\n" +
                    "  \"sticky_hidden_group_restores_together_in_dock_order_ok\": " +
                        Bool(wholeDockComponentRestoreOk) + ",\n" +
                    "  \"sticky_group_snapshot_survives_broken_parent_links_ok\": " +
                        Bool(dockSnapshotSurvivesBrokenParentLinksOk) + ",\n" +
                    "  \"sticky_group_snapshot_round_trip_ok\": " + Bool(
                        dockGroupSnapshotRoundTripOk) + ",\n" +
                    "  \"sticky_group_requests_restore_atomically_ok\": " + Bool(
                        groupRequestAlwaysRestoresAtomicallyOk) + ",\n" +
                    "  \"sticky_middle_member_extraction_keeps_neighbors_joined_ok\": " +
                        Bool(middleDockExtractionKeepsNeighborsJoinedOk) + ",\n" +
                    "  \"sticky_middle_x_preserves_hidden_group_slot_ok\": " +
                        Bool(middleXPreservesHiddenGroupSlotOk) + ",\n" +
                    "  \"sticky_middle_x_reopen_reinserts_original_slot_ok\": " +
                        Bool(middleXReopenReinsertsOriginalSlotOk) + ",\n" +
                    "  \"sticky_middle_x_hidden_slot_restart_persistence_ok\": " +
                        Bool(middleXHiddenSlotRestartPersistenceOk) + ",\n" +
                    "  \"sticky_partial_hidden_groups_merge_without_losing_slots_ok\": " +
                        Bool(partialHiddenGroupsMergeWithoutLosingSlotsOk) + ",\n" +
                    "  \"sticky_rearranged_group_second_restore_cycle_ok\": " +
                        Bool(rearrangedGroupSecondRestoreCycleOk) + ",\n" +
                    "  \"sticky_repeated_rearrange_restore_cycles_ok\": " +
                        Bool(repeatedRearrangeRestoreCyclesOk) + ",\n" +
                    "  \"sticky_wide_narrow_docking_ok\": " + Bool(
                        wideNarrowDockingOk) + ",\n" +
                    "  \"appearance_dialog_below_and_bounded_ok\": " + Bool(
                        appearanceDialogBelowAndBoundedOk) + ",\n" +
                    "  \"sticky_group_unified_width_layout_ok\": " + Bool(
                        unifiedDockGroupResizeOk) + ",\n" +
                    "  \"sticky_group_root_anchor_preserved_ok\": " + Bool(
                        dockRootAnchorPreservedOk) + ",\n" +
                    "  \"sticky_internal_divider_preserves_total_height_ok\": " +
                        Bool(dockDividerPreservesTotalHeightOk) + ",\n" +
                    "  \"sticky_internal_divider_cannot_cross_neighbors_ok\": " +
                        Bool(dockDividerCannotCrossNeighborOk) + ",\n" +
                    "  \"sticky_long_group_coordinate_guard_ok\": " + Bool(
                        longDockCoordinateGuardOk) + ",\n" +
                    "  \"sticky_root_close_collapses_group_ok\": " + Bool(
                        dockCloseHierarchyOk) + ",\n" +
                    "  \"sticky_native_aero_snap_disabled_ok\": " + Bool(
                        stickyNativeSnapDisabledOk &&
                        nativeWindowStyleAppliedOk) + ",\n" +
                    "  \"sticky_first_drag_geometry_recovery_ok\": " + Bool(
                        firstDragGeometryRecoveryOk) + ",\n" +
                    "  \"detached_group_returns_on_screen_ok\": " + Bool(
                        detachedDockReturnsOnScreenOk) + ",\n" +
                    "  \"sticky_screen_recovery_anchor_ok\": " + Bool(
                        stickyScreenRecoveryAnchorOk) + ",\n" +
                    "  \"ordinary_drag_cannot_accidentally_split_ok\": " + Bool(
                        deliberateSplitGestureOk) + ",\n" +
                    "  \"root_drag_always_moves_whole_group_ok\": " + Bool(
                        rootGroupDragNeverSplitsOk) + ",\n" +
                    "  \"dock_guides_do_not_flash_ok\": " + Bool(
                        steadyDockGuideOk) + ",\n" +
                    "  \"manager_marquee_batch_delete_ok\": " + Bool(managerMarqueeBatchDeleteOk) + ",\n" +
                    "  \"held_key_repeat_count_ok\": " + Bool(
                        absoluteThree == "W*3" && absoluteStale == "W*3" &&
                        hookRepeatTwo == 2) + ",\n" +
                    "  \"held_key_overlay_stays_constant_ok\": " + Bool(
                        heldKeyOverlayStableOk) + ",\n" +
                    "  \"own_process_hook_isolation_ok\": " + Bool(ownProcessHookIsolationOk) + ",\n" +
                    "  \"ime_animation_guard_ok\": " + Bool(imeAnimationGuardOk) + ",\n" +
                    "  \"ime_autosave_guard_ok\": " + Bool(imeAutoSaveGuardOk) + ",\n" +
                    "  \"reverse_reminder_step_ok\": " + Bool(reverseReminderStepOk) + ",\n" +
                    "  \"automatic_note_backup_ok\": " + Bool(automaticNoteBackupOk) + ",\n" +
                    "  \"failed_load_never_overwrites_ok\": " + Bool(failedLoadNeverOverwritesOk) + ",\n" +
                    "  \"sticky_backup_recovery_allows_create_ok\": " + Bool(
                        stickyBackupRecoveryOk) + ",\n" +
                    "  \"concrete_date_time_ok\": " + Bool(concreteDateTimeOk) + ",\n" +
                    "  \"prealert_opt_in_twenty_seconds_ok\": " + Bool(
                        preAlertWindowOk) + ",\n" +
                    "  \"reminder_clock_while_editing_ok\": " + Bool(reminderClockWhileEditingOk) + ",\n" +
                    "  \"reminder_banner_tick_throttled_ok\": " + Bool(
                        reminderBannerTickThrottleOk) + ",\n" +
                    "  \"startup_default_ok\": " + Bool(startupDefaultOk) + ",\n" +
                    "  \"keyboard_hook_opt_in_and_default_off_ok\": " + Bool(
                        keyboardHookOptInDefaultOk) + ",\n" +
                    "  \"keyboard_privacy_notice_persistence_ok\": " + Bool(
                        keyboardPrivacyNoticePersistenceOk) + ",\n" +
                    "  \"startup_loading_waits_for_ui_and_art_ok\": " + Bool(
                        startupLoadingReadinessGateOk) + ",\n" +
                    "  \"goodbye_animation_ok\": " + Bool(goodbyeAnimationOk) + ",\n" +
                    "  \"notification_animation_ok\": " + Bool(
                        notificationAnimationOk) + ",\n" +
                    "  \"notification_trigger_playback_route_ok\": " + Bool(
                        notificationTriggerPlaybackOk) + ",\n" +
                    "  \"due_reminder_bubble_persists_until_clicked_ok\": " + Bool(
                        dueReminderBubblePersistentOk) + ",\n" +
                    "  \"due_reminder_bubble_uses_own_size_ok\": " + Bool(
                        dueReminderBubbleUsesOwnSizeOk) + ",\n" +
                    "  \"due_reminder_bubble_replaced_by_later_feedback_ok\": " +
                        Bool(dueReminderBubbleReplacementOk) + ",\n" +
                    "  \"prealert_countdown_bubble_not_replaced_by_note_feedback_ok\": " +
                        Bool(preAlertBubbleProtectionOk) + ",\n" +
                    "  \"notification_animation_single_cycle_ok\": " + Bool(
                        notificationAnimationSingleCycleOk) + ",\n" +
                    "  \"drag_uses_second_idle_row_ok\": " + Bool(
                        dragUsesSecondIdleRowOk) + ",\n" +
                    "  \"idle_random_rows_ok\": " + Bool(idleRandomRowsOk) + ",\n" +
                    "  \"typing_random_rows_ok\": " + Bool(typingRandomRowsOk) + ",\n" +
                    "  \"idle_thought_combined_ten_percent_ok\": " + Bool(
                        idleThoughtProbabilityReducedOk) + ",\n" +
                    "  \"failed_guitar_probability_one_third_ok\": " + Bool(
                        guitarFailureProbabilityReducedOk) + ",\n" +
                    "  \"startup_lazy_art_load_ok\": " + Bool(
                        startupLazyArtLoadOk) + ",\n" +
                    "  \"startup_interaction_art_preload_ok\": " + Bool(
                        interactionArtPreloadOk) + ",\n" +
                    "  \"failed_art_preload_retry_ok\": " + Bool(
                        failedArtPreloadRetryOk) + ",\n" +
                    "  \"startup_idle_frame_cache_embedded_ok\": " + Bool(
                        startupCacheEmbeddedOk) + ",\n" +
                    "  \"per_pixel_alpha_renderer\": true,\n" +
                    "  \"inner_outline_ok\": " + Bool(innerOutlineOk) + ",\n" +
                    "  \"external_outline_pixels\": " + externalOutlinePixels + ",\n" +
                    "  \"green_halo_absent\": " + Bool(greenHaloOk) + ",\n" +
                    "  \"idle_cycle_milliseconds\": " + idleCycleMilliseconds + ",\n" +
                    "  \"failed_cycle_milliseconds\": " + failedCycleMilliseconds + ",\n" +
                    "  \"waiting_cycle_milliseconds\": " + waitingCycleMilliseconds + ",\n" +
                    "  \"thinking_cycle_milliseconds\": " + thinkingCycleMilliseconds + ",\n" +
                    "  \"review_cycle_milliseconds\": " + reviewCycleMilliseconds + ",\n" +
                    "  \"goodbye_cycle_milliseconds\": " + goodbyeCycleMilliseconds + ",\n" +
                    "  \"notification_cycle_milliseconds\": " +
                        notificationCycleMilliseconds + ",\n" +
                    "  \"smooth_timing_ok\": " + Bool(smoothTimingOk) + ",\n" +
                    "  \"hover_bubble_copy_ok\": " + Bool(hoverBubbleCopyOk) + ",\n" +
                    "  \"styled_reminder_bubble_ok\": " + Bool(
                        styledReminderBubbleOk) + ",\n" +
                    "  \"bubble_green_white_keyboard_font_ok\": " + Bool(
                        bubbleThemeAndKeyboardFontOk) + ",\n" +
                    "  \"reminder_bubble_uses_configured_size_ok\": " + Bool(
                        styledReminderBubbleOk) + ",\n" +
                    "  \"bubble_manual_position_ok\": " + Bool(bubbleManualPositionOk) + ",\n" +
                    "  \"drag_bubble_suppression_ok\": " + Bool(dragBubbleSuppressionOk) + ",\n" +
                    "  \"silent_mode_persistence_ok\": " + Bool(silentModePersistenceOk) + ",\n" +
                    "  \"silent_mode_daily_bubbles_suppressed_ok\": " + Bool(silentModeBehaviorOk) + ",\n" +
                    "  \"silent_mode_reminder_bubbles_preserved_ok\": " + Bool(
                        !PetForm.ShouldSuppressDailyBubble(true, true)) + ",\n" +
                    "  \"manual_animation_random_pool_excludes_running_rows_ok\": " + Bool(
                        manualAnimationRandomPoolOk) + ",\n" +
                    "  \"manual_special_animation_probability_reduced_ok\": " + Bool(
                        manualSpecialAnimationProbabilityReducedOk) + ",\n" +
                    "  \"manual_animation_cooldown_600ms_ok\": " + Bool(manualAnimationCooldownOk) + ",\n" +
                    "  \"left_click_drag_threshold_ok\": " + Bool(clickDragThresholdOk) + ",\n" +
                    "  \"bubble_position_math_ok\": " + Bool(bubblePositionMathOk) + ",\n" +
                    "  \"scale_50_to_200_step_10_ok\": " + Bool(scaleRangeOk) + ",\n" +
                    "  \"keyboard_text_scale_choices_ok\": " + Bool(keyboardTextScaleChoicesOk) + ",\n" +
                    "  \"keyboard_shortcut_and_repeat_ok\": " + Bool(keyDisplayOk) + ",\n" +
                    "  \"password_field_suppression_logic_ok\": " + Bool(passwordSuppressionOk) + ",\n" +
                    "  \"keyboard_privacy_generation_ok\": " + Bool(
                        keyboardPrivacyGenerationOk) + ",\n" +
                    "  \"adaptive_black_white_text_ok\": " + Bool(adaptiveContrastOk) + ",\n" +
                    "  \"thinking_row_registered\": true,\n" +
                    "  \"typing_random_rows_registered\": true,\n" +
                    "  \"idle_random_rows_registered\": true,\n" +
                    "  \"goodbye_row_registered\": true,\n" +
                    "  \"notification_row_registered\": true,\n" +
                    "  \"typing_moves_pet\": false,\n" +
                    "  \"look_follow_registered\": false,\n" +
                    "  \"keyboard_content_recorded\": false\n" +
                    "}\n";
                string parent = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                if (!String.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                string message = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
                File.WriteAllText(outputPath,
                    "{\"ok\":false,\"error\":\"" + message + "\"}",
                    new UTF8Encoding(false));
            }
        }
    }
}
