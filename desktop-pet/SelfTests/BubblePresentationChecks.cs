using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PennyPet
{
    internal static partial class SelfTest
    {

        private sealed class BubbleCheckResult
        {
            internal bool HoverCopyOk;
            internal bool ManualPositionOk;
            internal bool StyledReminderOk;
            internal bool ThemeAndKeyboardFontOk;
            internal bool DragSuppressionOk;
            internal bool SilentModeOk;
            internal bool PositionMathOk;
            internal bool SingleMessageKindOk;
            internal bool ReplacementClosesOldFormOk;
            internal bool ProtectedMessageOk;
            internal bool DeferredMessageSemanticsOk;
            internal bool PendingRetryOk;
            internal bool SmallTalkFeedbackLifecycleOk;
            internal bool ReminderPriorityRegressionOk;
            internal bool SingleRestoreAfterCloseOk;
            internal bool AdaptiveSizingOk;
            internal bool UpdateTextRelayoutOk;
            internal bool DailyFirstPokeOk;
            internal bool DailyRejectedRetryOk;
            internal bool DailyGreetingRequestOk;
            internal bool EasterEggRequestOk;
            internal bool MinimumReadableOk;
            internal bool ReadabilityBypassOk;
            internal bool SmallTalkRequestOk;
            internal bool SmallTalkCoordinatorCooldownOk;
            internal bool SmallTalkCoordinatorRejectedRetryOk;
            internal bool SmallTalkCoordinatorSilentModeOk;
            internal bool SmallTalkCoordinatorReminderRetryOk;
            internal bool SolarTermOk;
            internal bool DailyContentPreferencesOk;
            internal bool CuratedCatalogOk;
            internal bool DailySelectorBudgetOk;
            internal bool DailyBriefingBudgetOk;
            internal bool SentenceEndingPolicyOk;
            internal bool DailyBriefingCoordinatorOk;
            internal bool DailyBriefingRejectedRetryOk;
            internal bool DailyBriefingSameDaySwitchOk;
            internal bool AlmanacCalculatorOk;
            internal bool AlmanacSemanticOk;
            internal bool AlmanacWordingOk;
        }

        private static BubbleCheckResult RunBubbleChecks()
        {
            BubbleCheckResult result = new BubbleCheckResult();
            DateTime smallTalkStart = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            bool smallTalkSilent = false;
            int smallTalkShowCount = 0;
            string firstSmallTalk = null;
            string secondSmallTalk = null;
            PetSmallTalkCoordinator smallTalk = new PetSmallTalkCoordinator(
                delegate { return smallTalkSilent; },
                delegate(string text)
                {
                    smallTalkShowCount++;
                    if (firstSmallTalk == null) firstSmallTalk = text;
                    else secondSmallTalk = text;
                    return true;
                }, null, new SequenceRandom(0, 0, 0, 1));
            bool firstSmallTalkShown = smallTalk.HandlePetPoked(
                smallTalkStart);
            bool gapBlocked = !smallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(
                    PetSmallTalkPolicy.SuccessfulGapMilliseconds - 1));
            bool gapElapsed = smallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(
                    PetSmallTalkPolicy.SuccessfulGapMilliseconds));
            result.SmallTalkCoordinatorCooldownOk = firstSmallTalkShown &&
                gapBlocked && gapElapsed &&
                smallTalkShowCount == 2 && firstSmallTalk != secondSmallTalk;

            int rejectedShowCount = 0;
            PetSmallTalkCoordinator rejectedSmallTalk =
                new PetSmallTalkCoordinator(delegate { return false; },
                    delegate
                    {
                        rejectedShowCount++;
                        return rejectedShowCount > 1;
                    }, null, new SequenceRandom(0, 0, 0, 0));
            bool rejectedFirst = !rejectedSmallTalk.HandlePetPoked(
                smallTalkStart);
            bool rejectedRetry = rejectedSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorRejectedRetryOk = rejectedFirst &&
                rejectedRetry && rejectedShowCount == 2;

            int silentShowCount = 0;
            smallTalkSilent = true;
            PetSmallTalkCoordinator silentSmallTalk =
                new PetSmallTalkCoordinator(
                    delegate { return smallTalkSilent; },
                    delegate
                    {
                        silentShowCount++;
                        return true;
                    }, null, new SequenceRandom(0, 0));
            bool silentBlocked = !silentSmallTalk.HandlePetPoked(
                smallTalkStart);
            smallTalkSilent = false;
            bool silentRetry = silentSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorSilentModeOk = silentBlocked &&
                silentRetry && silentShowCount == 1;

            bool reminderDue = true;
            int reminderRejectedCount = 0;
            PetSmallTalkCoordinator reminderRejectedSmallTalk =
                new PetSmallTalkCoordinator(delegate { return false; },
                    delegate
                    {
                        reminderRejectedCount++;
                        return !reminderDue;
                    }, null, new SequenceRandom(0, 0, 0, 0));
            bool reminderRejected = !reminderRejectedSmallTalk.HandlePetPoked(
                smallTalkStart);
            reminderDue = false;
            bool afterReminderShown = reminderRejectedSmallTalk.HandlePetPoked(
                smallTalkStart.AddMilliseconds(1));
            result.SmallTalkCoordinatorReminderRetryOk = reminderRejected &&
                afterReminderShown && reminderRejectedCount == 2;

            using (SpeechBubbleForm bubble = new SpeechBubbleForm("初始", 0))
            using (SpeechBubbleForm styled = new SpeechBubbleForm(
                "样式提醒", 0, "Microsoft YaHei UI", 24F))
            {
                bubble.UpdateText("今天想要做些什么呢？");
                result.HoverCopyOk =
                    bubble.DisplayText == "今天想要做些什么呢？" &&
                    PetForm.FormatRemaining(TimeSpan.FromSeconds(65)) ==
                        "1分5秒";
                result.ManualPositionOk = bubble.StartPosition ==
                    System.Windows.Forms.FormStartPosition.Manual;
                result.StyledReminderOk = styled.Font != null &&
                    Math.Abs(styled.Font.SizeInPoints - 24F) < 0.2F &&
                    styled.Font.Bold &&
                    SpeechBubbleForm.BubbleTextColor == Color.White;
                result.ThemeAndKeyboardFontOk = bubble.Font != null &&
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
            result.DragSuppressionOk =
                !PetForm.ShouldShowHoverBubble(true, false, true) &&
                PetForm.ShouldShowHoverBubble(true, false, false);
            result.SilentModeOk =
                !PetForm.ShouldShowHoverBubble(true, false, false, true) &&
                PetForm.ShouldShowHoverBubble(true, false, false, false) &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.DailyGreeting, true) &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.Discovery, true) &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.Feedback, true) &&
                !PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.ReminderDue, true);
            Point position = SpeechBubbleForm.CalculateNearLocation(
                new Rectangle(1400, 800, 192, 208), new Size(330, 138),
                new Rectangle(0, 0, 1920, 1080));
            result.PositionMathOk = position.X > 1000 && position.Y > 500 &&
                position != Point.Empty;
            bool dragging = false;
            bool exiting = false;
            int restoreCount = 0;
            int closeCount = 0;
            DateTime bubbleNow = DateTime.UtcNow;
            using (Form owner = new Form())
            using (PetBubbleCoordinator coordinator = new PetBubbleCoordinator(
                owner, delegate { return dragging; },
                delegate { return exiting; },
                delegate(PetMessageKind kind) { closeCount++; },
                delegate { restoreCount++; },
                delegate { return bubbleNow; }))
            {
                IntPtr ownerHandle = owner.Handle;
                PetBubbleRequest firstRequest = PetBubbleRequest.Feedback(
                    "第一条", KeyboardOverlayForm.TextFontFamilyName, 18F);
                coordinator.Show(firstRequest);
                SpeechBubbleForm first = coordinator.CurrentBubbleForTest;
                result.SingleMessageKindOk = coordinator.CurrentKind ==
                    PetMessageKind.Feedback &&
                    coordinator.CurrentRequestForTest.Kind ==
                        PetMessageKind.Feedback;
                bubbleNow = bubbleNow.AddMilliseconds(1500);
                coordinator.Show(PetBubbleRequest.Feedback("第二条",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                Application.DoEvents();
                result.ReplacementClosesOldFormOk = first.IsDisposed &&
                    coordinator.CurrentRequestForTest.Text == "第二条" &&
                    closeCount == 1;
                coordinator.Show(PetBubbleRequest.ReminderPreAlert(
                    "提醒倒计时", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                SpeechBubbleForm protectedBubble =
                    coordinator.CurrentBubbleForTest;
                bool feedbackAccepted = coordinator.Show(
                    PetBubbleRequest.Feedback("不能覆盖",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ProtectedMessageOk = !feedbackAccepted &&
                    ReferenceEquals(protectedBubble,
                        coordinator.CurrentBubbleForTest) &&
                    !protectedBubble.IsDisposed;
                coordinator.CloseCurrent(true);
                coordinator.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool smallTalkBlocked = !coordinator.Show(
                    PetBubbleRequest.SmallTalk("需要我帮什么忙吗？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bubbleNow = bubbleNow.AddMilliseconds(3000);
                bool smallTalkAllowed = coordinator.Show(
                    PetBubbleRequest.SmallTalk("怎么啦？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.MinimumReadableOk = smallTalkBlocked &&
                    smallTalkAllowed &&
                    coordinator.CurrentKind == PetMessageKind.SmallTalk;
                coordinator.CloseCurrent(true);
                dragging = true;
                coordinator.Show(PetBubbleRequest.Feedback(
                    "拖拽后显示", "Microsoft YaHei UI", 19F));
                bool queued = coordinator.PendingCountForTest == 1 &&
                    !coordinator.HasCurrent;
                dragging = false;
                coordinator.ShowNextPending();
                PetBubbleRequest restored = coordinator.CurrentRequestForTest;
                result.DeferredMessageSemanticsOk = queued && restored != null &&
                    restored.Kind == PetMessageKind.Feedback &&
                    restored.Text == "拖拽后显示" &&
                    restored.AutoCloseMilliseconds ==
                        BubbleReadingDurationRules.AutoCloseMilliseconds(
                            "拖拽后显示") &&
                    restored.DeferWhileDragging &&
                    Math.Abs(restored.FontSizePoints - 19F) < 0.2F;
                coordinator.CurrentBubbleForTest.Close();
                Application.DoEvents();
                Application.DoEvents();
                result.SingleRestoreAfterCloseOk = restoreCount == 1 &&
                    !coordinator.HasCurrent;
            }
            DateTime pendingNow = DateTime.UtcNow;
            bool pendingDragging = true;
            using (Form pendingOwner = new Form())
            using (PetBubbleCoordinator pending = new PetBubbleCoordinator(
                pendingOwner, delegate { return pendingDragging; },
                delegate { return false; }, delegate { }, delegate { },
                delegate { return pendingNow; }))
            {
                IntPtr pendingOwnerHandle = pendingOwner.Handle;
                pending.Show(PetBubbleRequest.Feedback("稍后反馈",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pending.Show(PetBubbleRequest.DailyGreeting("早上好～",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pendingDragging = false;
                pending.ShowNextPending();
                bool minimumRetained = pending.PendingCountForTest == 1 &&
                    pending.CurrentKind == PetMessageKind.DailyGreeting;
                pending.ShowNextPending();
                bool retryNotDuplicated = pending.PendingCountForTest == 1;
                pendingNow = pendingNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        "早上好～") + 1);
                pending.ShowNextPending();
                bool minimumEventuallyShown = pending.PendingCountForTest == 0 &&
                    pending.CurrentKind == PetMessageKind.Feedback;
                pending.CloseCurrent(true);

                pendingDragging = true;
                pending.Show(PetBubbleRequest.Feedback("提醒后反馈",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pending.Show(PetBubbleRequest.ReminderDue("提醒到了",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                pendingDragging = false;
                pending.ShowNextPending();
                bool policyRetained = pending.PendingCountForTest == 1 &&
                    pending.CurrentKind == PetMessageKind.ReminderDue;
                pending.CurrentBubbleForTest.Close();
                Application.DoEvents();
                Application.DoEvents();
                bool policyEventuallyShown = pending.PendingCountForTest == 0 &&
                    pending.CurrentKind == PetMessageKind.Feedback;
                result.PendingRetryOk = minimumRetained &&
                    retryNotDuplicated && minimumEventuallyShown &&
                    policyRetained && policyEventuallyShown;
            }
            DateTime lifecycleNow = DateTime.UtcNow;
            using (Form lifecycleOwner = new Form())
            using (PetBubbleCoordinator lifecycle = new PetBubbleCoordinator(
                lifecycleOwner, delegate { return false; },
                delegate { return false; }, delegate { }, delegate { },
                delegate { return lifecycleNow; }))
            {
                IntPtr lifecycleOwnerHandle = lifecycleOwner.Handle;
                const string smallTalkText = "怎么啦？";
                lifecycle.Show(PetBubbleRequest.SmallTalk(smallTalkText,
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool feedbackBlocked = !lifecycle.Show(
                    PetBubbleRequest.Feedback("设置完成",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycleNow = lifecycleNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        smallTalkText) + 1);
                bool feedbackAllowed = lifecycle.Show(
                    PetBubbleRequest.Feedback("设置完成",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.SmallTalk(smallTalkText,
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycleNow = lifecycleNow.AddMilliseconds(
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        smallTalkText) + 1);
                bool hoverBlocked = !lifecycle.Show(PetBubbleRequest.Hover(
                    "今天想做什么？",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.SmallTalkFeedbackLifecycleOk = feedbackBlocked &&
                    feedbackAllowed && hoverBlocked;

                bool reminderFromSmallTalk = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒一",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool feedbackCannotReplaceDue = !lifecycle.Show(
                    PetBubbleRequest.Feedback("不能覆盖",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.DailyGreeting("下午好～",
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool reminderFromDaily = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒二",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.EasterEgg(
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool preAlertFromEaster = lifecycle.Show(
                    PetBubbleRequest.ReminderPreAlert("提醒倒计时",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                lifecycle.CloseCurrent(true);
                lifecycle.Show(PetBubbleRequest.EasterEgg(
                    KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool reminderFromEaster = lifecycle.Show(
                    PetBubbleRequest.ReminderDue("提醒三",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ReminderPriorityRegressionOk =
                    reminderFromSmallTalk && feedbackCannotReplaceDue &&
                    reminderFromDaily && preAlertFromEaster &&
                    reminderFromEaster;
            }
            DateTime bypassNow = DateTime.UtcNow;
            using (Form bypassOwner = new Form())
            using (PetBubbleCoordinator bypass = new PetBubbleCoordinator(
                bypassOwner, delegate { return false; },
                delegate { return false; }, delegate { },
                delegate { }, delegate { return bypassNow; }))
            {
                bypass.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool smallTalkBlocked = !bypass.Show(
                    PetBubbleRequest.SmallTalk("需要我帮什么忙吗？",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bool easterAccepted = bypass.Show(
                    PetBubbleRequest.EasterEgg(
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                bypass.CloseCurrent(true);
                bypass.Show(PetBubbleRequest.DailyGreeting(
                    "早上好～", KeyboardOverlayForm.TextFontFamilyName,
                    18F));
                bool reminderAccepted = bypass.Show(
                    PetBubbleRequest.ReminderDue("提醒到了",
                        KeyboardOverlayForm.TextFontFamilyName, 18F));
                result.ReadabilityBypassOk = smallTalkBlocked &&
                    easterAccepted && reminderAccepted &&
                    bypass.CurrentKind == PetMessageKind.ReminderDue;
            }
            using (SpeechBubbleForm empty = new SpeechBubbleForm("", 0))
            using (SpeechBubbleForm shortChinese = new SpeechBubbleForm(
                "嗯。", 0))
            using (SpeechBubbleForm greeting = new SpeechBubbleForm(
                "早上好～", 0))
            using (SpeechBubbleForm medium = new SpeechBubbleForm(
                "今天想要做些什么呢？", 0))
            using (SpeechBubbleForm english = new SpeechBubbleForm(
                "What would you like to work on today?", 0))
            using (SpeechBubbleForm countdown = new SpeechBubbleForm(
                "提醒倒计时 19 秒", 0))
            using (SpeechBubbleForm multiline = new SpeechBubbleForm(
                "第一行提醒\n第二行提醒\n第三行提醒", 0))
            using (SpeechBubbleForm veryLong = new SpeechBubbleForm(
                new String('长', 300), 0))
            {
                SpeechBubbleForm[] samples = new SpeechBubbleForm[]
                {
                    empty, shortChinese, greeting, medium, english,
                    countdown, multiline, veryLong
                };
                bool valid = true;
                foreach (SpeechBubbleForm sample in samples)
                {
                    valid &= sample.Width >=
                        SpeechBubbleForm.MinimumBubbleSize.Width &&
                        sample.Height >=
                            SpeechBubbleForm.MinimumBubbleSize.Height &&
                        sample.Width <=
                            SpeechBubbleForm.MaximumBubbleSize.Width &&
                        sample.Height <=
                            SpeechBubbleForm.MaximumBubbleSize.Height;
                }
                result.AdaptiveSizingOk = valid &&
                    shortChinese.Width * shortChinese.Height <
                        medium.Width * medium.Height &&
                    multiline.Height > shortChinese.Height &&
                    veryLong.Width ==
                        SpeechBubbleForm.MaximumBubbleSize.Width &&
                    veryLong.Height <=
                        SpeechBubbleForm.MaximumBubbleSize.Height;
                Size shortSize = shortChinese.ClientSize;
                shortChinese.UpdateText(new String('长', 300));
                Size longSize = shortChinese.ClientSize;
                shortChinese.UpdateText("嗯。");
                result.UpdateTextRelayoutOk = longSize != shortSize &&
                    shortChinese.ClientSize == shortSize;
            }
            string lastBriefingDate = String.Empty;
            bool silent = false;
            bool acceptGreeting = true;
            bool dailyContentEnabled = true;
            bool solarTermEnabled = true;
            ZodiacSign zodiacSign = ZodiacSign.None;
            int greetingCount = 0;
            int recordCount = 0;
            string greetingText = null;
            PetDailyContentCoordinator daily =
                CreateDailyCoordinator(
                    delegate { return lastBriefingDate; },
                    delegate { return silent; },
                    delegate { return dailyContentEnabled; },
                    delegate { return solarTermEnabled; },
                    delegate { return true; },
                    delegate { return false; },
                    delegate { return null; },
                    delegate
                    {
                        return Task.FromResult<WeatherForecastWindow>(null);
                    },
                    delegate { return zodiacSign; },
                    delegate { return 0; },
                    delegate { return 0; },
                    delegate(string text)
                    {
                        greetingCount++;
                        greetingText = text;
                        return acceptGreeting;
                    },
                    delegate(string date)
                    {
                        recordCount++;
                        lastBriefingDate = date;
                    });
            DateTimeOffset morning = new DateTimeOffset(2035, 6, 15, 8, 30, 0,
                TimeSpan.FromHours(8));
            bool firstPoke = RunDaily(daily, morning);
            bool secondPoke = RunDaily(daily, morning.AddHours(1));
            lastBriefingDate = "20350614";
            bool nextDay = RunDaily(daily, morning.AddHours(6.5));
            result.DailyFirstPokeOk = firstPoke && !secondPoke && nextDay &&
                greetingCount == 2 && recordCount == 2 &&
                greetingText.StartsWith("下午好，今天过得怎么样",
                    StringComparison.Ordinal) &&
                lastBriefingDate == "20350615";
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            silent = true;
            bool silentPoke = RunDaily(daily, morning);
            silent = false;
            acceptGreeting = false;
            bool rejectedPoke = RunDaily(daily, morning);
            acceptGreeting = true;
            bool retriedPoke = RunDaily(daily, morning);
            result.DailyRejectedRetryOk = !silentPoke && !rejectedPoke &&
                retriedPoke && greetingCount == 2 && recordCount == 1 &&
                lastBriefingDate == "20350615";
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            dailyContentEnabled = false;
            bool disabledPoke = RunDaily(daily, morning);
            dailyContentEnabled = true;
            bool enabledLaterPoke = RunDaily(daily, morning);
            bool enabledSameDayPoke = RunDaily(daily,
                morning.AddHours(1));
            lastBriefingDate = String.Empty;
            greetingCount = 0;
            recordCount = 0;
            solarTermEnabled = false;
            DateTimeOffset whiteDewDate = new DateTimeOffset(
                2026, 9, 7, 12, 0, 0, TimeSpan.FromHours(8));
            bool solarOffPoke = RunDaily(daily, whiteDewDate);
            bool plainGreeting = greetingText.IndexOf("白露",
                StringComparison.Ordinal) < 0;
            solarTermEnabled = true;
            bool solarEnabledSameDayPoke = RunDaily(daily,
                whiteDewDate.AddHours(1));
            lastBriefingDate = String.Empty;
            bool solarOnPoke = RunDaily(daily, whiteDewDate);
            result.DailyContentPreferencesOk = !disabledPoke &&
                enabledLaterPoke && !enabledSameDayPoke && solarOffPoke &&
                plainGreeting && !solarEnabledSameDayPoke && solarOnPoke &&
                greetingText.IndexOf("今天是白露",
                    StringComparison.Ordinal) >= 0;

            DailyLineEntry[] curatedEntries =
                CuratedDailyLineCatalog.GetEntries();
            HashSet<string> curatedIds = new HashSet<string>();
            HashSet<string> curatedTexts = new HashSet<string>();
            bool curatedCatalogValid = curatedEntries.Length == 96;
            foreach (DailyLineEntry entry in curatedEntries)
                curatedCatalogValid &=
                    !String.IsNullOrWhiteSpace(entry.Id) &&
                    !String.IsNullOrWhiteSpace(entry.Text) &&
                    curatedIds.Add(entry.Id) && curatedTexts.Add(entry.Text);
            bool zodiacCatalogValid = ZodiacDailyCatalog.GetEntries(
                ZodiacSign.None).Length == 0;
            HashSet<string> zodiacIds = new HashSet<string>();
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                DailyLineEntry[] entries = ZodiacDailyCatalog.GetEntries(
                    (ZodiacSign)value);
                HashSet<string> uniqueTexts = new HashSet<string>();
                zodiacCatalogValid &= entries.Length == 6;
                foreach (DailyLineEntry entry in entries)
                    zodiacCatalogValid &=
                        !String.IsNullOrWhiteSpace(entry.Id) &&
                        !String.IsNullOrWhiteSpace(entry.Text) &&
                        zodiacIds.Add(entry.Id) &&
                        uniqueTexts.Add(entry.Text);
            }
            result.CuratedCatalogOk = curatedCatalogValid &&
                curatedIds.Count == 96 && curatedTexts.Count == 96 &&
                zodiacCatalogValid && zodiacIds.Count == 72;

            DateTimeOffset briefingDate = new DateTimeOffset(2026, 9, 3,
                12, 0, 0, TimeSpan.FromHours(8));
            DailyLineEntry selectedCurated = CuratedDailyLineSelector.Select(
                briefingDate);
            DailyLineEntry selectedScorpio = ZodiacDailySelector.Select(
                ZodiacSign.Scorpio, briefingDate);
            bool deterministic = selectedCurated != null &&
                selectedScorpio != null && selectedCurated.Id ==
                    CuratedDailyLineSelector.Select(briefingDate).Id &&
                selectedScorpio.Id == ZodiacDailySelector.Select(
                    ZodiacSign.Scorpio, briefingDate).Id;
            DateTimeOffset rangeStart = new DateTimeOffset(2026, 1, 1,
                12, 0, 0, TimeSpan.FromHours(8));
            bool eligibilityBounded = true;
            for (int value = (int)ZodiacSign.Aries;
                value <= (int)ZodiacSign.Pisces; value++)
            {
                int eligibleDays = 0;
                for (int day = 0; day < 3650; day++)
                    if (ZodiacDailySelector.Select((ZodiacSign)value,
                        rangeStart.AddDays(day)) != null) eligibleDays++;
                double percent = eligibleDays * 100D / 3650D;
                eligibilityBounded &= percent >= 10D && percent <= 20D;
            }
            DateTimeOffset sameInstant = new DateTimeOffset(2026, 9, 1,
                16, 30, 0, TimeSpan.Zero);
            bool selectedInCatalog = false;
            foreach (DailyLineEntry entry in ZodiacDailyCatalog.GetEntries(
                ZodiacSign.Scorpio))
                selectedInCatalog |= entry.Id == selectedScorpio.Id;
            result.DailySelectorBudgetOk = deterministic &&
                eligibilityBounded &&
                ZodiacDailySelector.Select(ZodiacSign.None, briefingDate) ==
                    null && ZodiacDailySelector.Select((ZodiacSign)999,
                        briefingDate) == null &&
                selectedInCatalog &&
                CuratedDailyLineSelector.Select(sameInstant.ToOffset(
                    TimeSpan.FromHours(8))).Id !=
                CuratedDailyLineSelector.Select(sameInstant.ToOffset(
                    TimeSpan.FromHours(-8))).Id;

            AlmanacDayInfo actualAlmanacDay = AlmanacCalculator.Calculate(
                briefingDate);
            AlmanacDailySelection actualAlmanac = actualAlmanacDay == null
                ? null : AlmanacDailySelector.Select(actualAlmanacDay,
                    briefingDate);
            result.AlmanacCalculatorOk = AlmanacCalculator.Sect == 1 &&
                actualAlmanacDay != null &&
                actualAlmanacDay.Year == briefingDate.Year &&
                actualAlmanacDay.Month == briefingDate.Month &&
                actualAlmanacDay.Day == briefingDate.Day &&
                actualAlmanacDay.Yi.Count > 0 &&
                actualAlmanacDay.Ji.Count > 0;
            AlmanacDailySelection dedupedSocial =
                AlmanacDailySelector.Select(new AlmanacDayInfo(2026, 9, 3,
                    new[] { "会友", "会亲友" }, new string[0]),
                    briefingDate);
            AlmanacDailySelection conflictedOuting =
                AlmanacDailySelector.Select(new AlmanacDayInfo(2026, 9, 3,
                    new[] { "出行" }, new[] { "出行" }), briefingDate);
            AlmanacDailySelection restricted = AlmanacDailySelector.Select(
                new AlmanacDayInfo(2026, 9, 3,
                    new[] { "求医", "纳财", "祭祀", "动土" },
                    new string[0]), briefingDate);
            result.AlmanacSemanticOk = dedupedSocial != null &&
                dedupedSocial.Topic == AlmanacTopic.Social &&
                conflictedOuting == null && restricted == null;
            HashSet<string> tidyVariants = new HashSet<string>();
            bool wordingStable = true;
            for (int day = 0; day < 730; day++)
            {
                DateTimeOffset date = rangeStart.AddDays(day);
                AlmanacDayInfo tidyDay = new AlmanacDayInfo(date.Year,
                    date.Month, date.Day, new[] { "扫舍" }, new string[0]);
                AlmanacDailySelection first = AlmanacDailySelector.Select(
                    tidyDay, date);
                AlmanacDailySelection retry = AlmanacDailySelector.Select(
                    tidyDay, date);
                wordingStable &= first != null && retry != null &&
                    first.VariantId == retry.VariantId &&
                    first.Text == retry.Text &&
                    !first.Text.Contains("今天一定") &&
                    !first.Text.Contains("必须") &&
                    !first.Text.Contains("千万不要") &&
                    !first.Text.Contains("绝对不能") &&
                    !first.Text.Contains("\n") &&
                    !first.Text.Contains("。") &&
                    !first.Text.Contains("！") &&
                    !first.Text.Contains("？");
                if (first != null) tidyVariants.Add(first.VariantId);
            }
            result.AlmanacWordingOk = wordingStable &&
                tidyVariants.Count >= 5;

            SolarTermInfo? whiteDew = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 7, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            SolarTermInfo? nonTerm = SolarTermCalculator.FindForLocalDate(
                new DateTimeOffset(2026, 9, 6, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            result.SolarTermOk = whiteDew.HasValue &&
                whiteDew.Value.Term == SolarTerm.WhiteDew &&
                whiteDew.Value.ChineseName == "白露" &&
                whiteDew.Value.LongitudeDegrees == 165 &&
                !nonTerm.HasValue;
            WeatherDailySelection weatherText = new WeatherDailySelection(
                WeatherMeaning.Windy, "WEATHER-WINDY-TEST",
                "今天风比较大，出门注意一下");
            AlmanacDailySelection almanacText = new AlmanacDailySelection(
                AlmanacTopic.MovingHome, "入宅", true, "MOVING-TEST",
                "F-TEST", "W-TEST",
                "传统日历今天提到搬家，没计划的话看看就好");
            DailyBriefingContent caseA = new DailyBriefingContent(whiteDew,
                null, almanacText, selectedCurated, selectedScorpio);
            DailyBriefingContent caseB = new DailyBriefingContent(whiteDew,
                null, null, selectedCurated, selectedScorpio);
            DailyBriefingContent caseC = new DailyBriefingContent(null,
                null, almanacText, selectedCurated, selectedScorpio);
            DailyBriefingContent caseD = new DailyBriefingContent(null,
                null, null, selectedCurated, null);
            DailyBriefingContent caseE = new DailyBriefingContent(null,
                null, null, selectedCurated, selectedScorpio);
            DailyBriefingContent solarWeatherAlmanac =
                new DailyBriefingContent(whiteDew, weatherText, almanacText,
                    selectedCurated, selectedScorpio);
            DailyBriefingContent weatherAlmanac =
                new DailyBriefingContent(null, weatherText, almanacText,
                    selectedCurated, selectedScorpio);
            DailyBriefingContent weatherOnly = new DailyBriefingContent(null,
                weatherText, null, selectedCurated, selectedScorpio);
            DateTime briefingLocalDate = briefingDate.Date;
            DailyBriefingSentence[] solarWeatherSentences =
                DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                    solarWeatherAlmanac);
            DailyBriefingSentence[] weatherOnlySentences =
                DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                    weatherOnly);
            result.DailyBriefingBudgetOk = whiteDew.HasValue &&
                solarWeatherSentences.Length == 3 &&
                solarWeatherSentences[1].Kind ==
                    PetSentenceContentKind.Solar &&
                solarWeatherSentences[2].Kind ==
                    PetSentenceContentKind.Weather &&
                weatherOnlySentences.Length == 2 &&
                weatherOnlySentences[1].Kind ==
                    PetSentenceContentKind.Weather &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    briefingLocalDate, caseA).Split('\n').Length <= 3 &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    briefingLocalDate, caseB).Split('\n').Length <= 3 &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    briefingLocalDate, caseC).Split('\n').Length <= 3 &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    briefingLocalDate, caseD).Split('\n').Length <= 3 &&
                DailyBriefingComposer.Compose(DayPart.Afternoon,
                    briefingLocalDate, caseE).Split('\n').Length <= 3 &&
                DailyBriefingComposer.SelectSupplementary(caseA).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseB).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseC).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseD).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(caseE).Length <= 2;
            result.DailyBriefingBudgetOk = result.DailyBriefingBudgetOk &&
                DailyBriefingComposer.SelectSupplementary(
                    solarWeatherAlmanac).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(
                    weatherAlmanac).Length <= 2 &&
                DailyBriefingComposer.SelectSupplementary(
                    weatherOnly).Length <= 2;
            PetSentenceEndingContext endingContext =
                new PetSentenceEndingContext(PetSentenceRole.Closing,
                    PetSentenceIntent.Gentle,
                    PetSentenceContentKind.Almanac,
                    "ALMANAC-BATH-03", briefingLocalDate);
            string ending = PetSentenceEndingPolicy.Apply(
                "传统日历今天也说到沐浴", endingContext);
            result.SentenceEndingPolicyOk =
                ending == "传统日历今天也说到沐浴啦～" &&
                ending == PetSentenceEndingPolicy.Apply(
                    "传统日历今天也说到沐浴", endingContext) &&
                PetSentenceEndingPolicy.Apply(
                    "忙完早点洗个澡，剩下的明天再管",
                    new PetSentenceEndingContext(PetSentenceRole.Middle,
                        PetSentenceIntent.Gentle,
                        PetSentenceContentKind.Almanac, "BATH-MIDDLE",
                        briefingLocalDate)) ==
                    "忙完早点洗个澡，剩下的明天再管。" &&
                PetSentenceEndingPolicy.ApplyEnding("今天辛苦了", "啦～") ==
                    "今天辛苦啦～";

            lastBriefingDate = String.Empty;
            silent = false;
            dailyContentEnabled = true;
            solarTermEnabled = false;
            zodiacSign = ZodiacSign.Scorpio;
            acceptGreeting = true;
            greetingCount = 0;
            recordCount = 0;
            bool zodiacShown = RunDaily(daily, briefingDate);
            string expectedZodiacText = DailyBriefingComposer.Compose(
                DailyContentRules.ResolveDayPart(briefingDate),
                briefingDate.Date,
                new DailyBriefingContent(null,
                    null, actualAlmanac, selectedCurated, selectedScorpio));
            bool zodiacTextOk = greetingText == expectedZodiacText &&
                recordCount == 1;
            zodiacSign = ZodiacSign.Pisces;
            bool changedSignSameDay = RunDaily(daily,
                briefingDate.AddHours(1));
            result.DailyBriefingSameDaySwitchOk = zodiacShown &&
                !changedSignSameDay && recordCount == 1;

            lastBriefingDate = String.Empty;
            solarTermEnabled = true;
            zodiacSign = ZodiacSign.Scorpio;
            greetingCount = 0;
            recordCount = 0;
            bool solarZodiacShown = RunDaily(daily, whiteDewDate);
            AlmanacDayInfo whiteDewAlmanacDay = AlmanacCalculator.Calculate(
                whiteDewDate);
            AlmanacDailySelection whiteDewAlmanac = whiteDewAlmanacDay == null
                ? null : AlmanacDailySelector.Select(whiteDewAlmanacDay,
                    whiteDewDate);
            string solarZodiacExpected = DailyBriefingComposer.Compose(
                DailyContentRules.ResolveDayPart(whiteDewDate),
                whiteDewDate.Date,
                new DailyBriefingContent(whiteDew,
                    null, whiteDewAlmanac,
                    CuratedDailyLineSelector.Select(whiteDewDate),
                    ZodiacDailySelector.Select(ZodiacSign.Scorpio,
                        whiteDewDate)));
            result.DailyBriefingCoordinatorOk = zodiacTextOk &&
                solarZodiacShown && greetingText == solarZodiacExpected &&
                recordCount == 1;

            lastBriefingDate = String.Empty;
            solarTermEnabled = false;
            acceptGreeting = false;
            greetingCount = 0;
            recordCount = 0;
            bool zodiacRejected = !RunDaily(daily, briefingDate);
            string rejectedZodiacText = greetingText;
            acceptGreeting = true;
            bool zodiacRetried = RunDaily(daily,
                briefingDate.AddMinutes(1));
            result.DailyBriefingRejectedRetryOk = zodiacRejected &&
                zodiacRetried &&
                greetingCount == 2 && recordCount == 1 &&
                greetingText == rejectedZodiacText;
            PetBubbleRequest dailyRequest = PetBubbleRequest.DailyGreeting(
                "早上好", KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.DailyGreetingRequestOk = dailyRequest.Kind ==
                PetMessageKind.DailyGreeting &&
                dailyRequest.AutoCloseMilliseconds ==
                    BubbleReadingDurationRules.AutoCloseMilliseconds(
                        "早上好") &&
                !dailyRequest.DeferWhileDragging;
            PetBubbleRequest easterEggRequest = PetBubbleRequest.EasterEgg(
                KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.EasterEggRequestOk = easterEggRequest.Kind ==
                PetMessageKind.EasterEgg &&
                easterEggRequest.Text == "你在整我是不是。" &&
                !easterEggRequest.DeferWhileDragging &&
                easterEggRequest.AutoCloseMilliseconds == 2800 &&
                easterEggRequest.MinimumReadableMilliseconds == 1000 &&
                !easterEggRequest.ClosesOnMouseDown &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.ReminderDue,
                    PetMessageKind.EasterEgg, false) &&
                !PetMessagePolicy.ShouldReplace(
                    PetMessageKind.ReminderPreAlert,
                    PetMessageKind.EasterEgg, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.EasterEgg,
                    PetMessageKind.DailyGreeting, false) &&
                !PetMessagePolicy.ShouldReplace(PetMessageKind.EasterEgg,
                    PetMessageKind.Feedback, false) &&
                PetMessagePolicy.ShouldReplace(PetMessageKind.DailyGreeting,
                    PetMessageKind.EasterEgg, false);
            PetBubbleRequest smallTalkRequest = PetBubbleRequest.SmallTalk(
                "怎么啦？", KeyboardOverlayForm.TextFontFamilyName, 15F);
            result.SmallTalkRequestOk = smallTalkRequest.Kind ==
                PetMessageKind.SmallTalk &&
                smallTalkRequest.MinimumReadableMilliseconds ==
                    BubbleReadingDurationRules.MinimumReadableMilliseconds(
                        "怎么啦？") &&
                smallTalkRequest.AutoCloseMilliseconds ==
                    BubbleReadingDurationRules.AutoCloseMilliseconds(
                        "怎么啦？") &&
                PetMessagePolicy.ShouldSuppress(
                    PetMessageKind.SmallTalk, true);
            return result;
        }

        private static bool RunDaily(PetDailyContentCoordinator coordinator,
            DateTimeOffset localNow)
        {
            return coordinator.HandlePetPokedAsync(localNow).GetAwaiter()
                .GetResult();
        }
    }
}
