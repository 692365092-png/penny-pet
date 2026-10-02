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
        public void PetSmallTalkPolicy_WindowQuotaGapAndSpeakChance()
        {
            DateTime start = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            Assert.IsTrue(PetSmallTalkPolicy.IsWindowExpired(
                default(DateTime), start));
            Assert.IsFalse(PetSmallTalkPolicy.IsWindowExpired(start,
                start.AddMilliseconds(
                    PetSmallTalkPolicy.WindowMilliseconds - 1)));
            Assert.IsTrue(PetSmallTalkPolicy.IsWindowExpired(start,
                start.AddMilliseconds(
                    PetSmallTalkPolicy.WindowMilliseconds)));
            Assert.IsTrue(PetSmallTalkPolicy.HasSuccessfulGapElapsed(
                default(DateTime), start));
            Assert.IsFalse(PetSmallTalkPolicy.HasSuccessfulGapElapsed(start,
                start.AddMilliseconds(
                    PetSmallTalkPolicy.SuccessfulGapMilliseconds - 1)));
            Assert.IsTrue(PetSmallTalkPolicy.HasSuccessfulGapElapsed(start,
                start.AddMilliseconds(
                    PetSmallTalkPolicy.SuccessfulGapMilliseconds)));
            Assert.IsTrue(PetSmallTalkPolicy.ShouldSpeak(0));
            Assert.IsTrue(PetSmallTalkPolicy.ShouldSpeak(
                PetSmallTalkPolicy.SpeakChancePercent - 1));
            Assert.IsFalse(PetSmallTalkPolicy.ShouldSpeak(
                PetSmallTalkPolicy.SpeakChancePercent));
        }

        [TestMethod]
        public void PetHoverStabilityRules_HysteresisAndSuppression()
        {
            DateTime start = new DateTime(2035, 1, 1, 0, 0, 0,
                DateTimeKind.Utc);
            Assert.IsFalse(PetHoverStabilityRules.ShouldCommitEnter(start,
                start.AddMilliseconds(
                    PetHoverStabilityRules.EnterDwellMilliseconds - 1)));
            Assert.IsTrue(PetHoverStabilityRules.ShouldCommitEnter(start,
                start.AddMilliseconds(
                    PetHoverStabilityRules.EnterDwellMilliseconds)));
            Assert.IsFalse(PetHoverStabilityRules.ShouldCommitLeave(start,
                start.AddMilliseconds(
                    PetHoverStabilityRules.LeaveGraceMilliseconds - 1)));
            Assert.IsTrue(PetHoverStabilityRules.ShouldCommitLeave(start,
                start.AddMilliseconds(
                    PetHoverStabilityRules.LeaveGraceMilliseconds)));

            Assert.IsTrue(PetHoverStabilityRules.ShouldSuppressHover(
                false, false, false, false, false));
            Assert.IsTrue(PetHoverStabilityRules.ShouldSuppressHover(
                true, true, false, false, false));
            Assert.IsTrue(PetHoverStabilityRules.ShouldSuppressHover(
                true, false, true, false, false));
            Assert.IsTrue(PetHoverStabilityRules.ShouldSuppressHover(
                true, false, false, true, false));
            Assert.IsTrue(PetHoverStabilityRules.ShouldSuppressHover(
                true, false, false, false, true));
            Assert.IsFalse(PetHoverStabilityRules.ShouldSuppressHover(
                true, false, false, false, false));
        }

        [TestMethod]
        public void PetDaypartRule_ResolvesOneUnifiedHourBoundary()
        {
            Assert.AreEqual(DayPart.LateNight,
                PetDaypartRule.Resolve(new DateTimeOffset(
                    2035, 1, 1, 4, 59, 0, TimeSpan.FromHours(8))));
            Assert.AreEqual(DayPart.Morning,
                PetDaypartRule.Resolve(new DateTimeOffset(
                    2035, 1, 1, 5, 0, 0, TimeSpan.FromHours(8))));
            Assert.AreEqual(DayPart.Midday,
                PetDaypartRule.Resolve(new DateTimeOffset(
                    2035, 1, 1, 11, 0, 0, TimeSpan.FromHours(8))));
            Assert.AreEqual(DayPart.Afternoon,
                PetDaypartRule.Resolve(new DateTimeOffset(
                    2035, 1, 1, 14, 0, 0, TimeSpan.FromHours(8))));
            Assert.AreEqual(DayPart.Evening,
                PetDaypartRule.Resolve(new DateTimeOffset(
                    2035, 1, 1, 18, 0, 0, TimeSpan.FromHours(8))));
            Assert.IsFalse(PetDaypartRule.SupportsLightCheckIn(
                DayPart.LateNight));
            Assert.IsTrue(PetDaypartRule.SupportsLightCheckIn(
                DayPart.Morning));
        }

        [TestMethod]
        public void PetDailyInteractionLedger_ResetsPerLocalDateAndTracksSlots()
        {
            PetDailyInteractionLedger ledger = new PetDailyInteractionLedger(
                "20350101", true,
                PetDaypartRule.ConsumedMask(DayPart.Morning),
                new[] { "MEANINGFUL-EYES" });
            Assert.IsTrue(ledger.IsCurrentDate("20350101"));
            Assert.IsTrue(ledger.DailyOpeningConsumed);
            Assert.IsTrue(ledger.HasConsumedDaypart(DayPart.Morning));
            Assert.IsFalse(ledger.HasConsumedDaypart(DayPart.Midday));
            Assert.IsTrue(ledger.WasMeaningfulUsed("MEANINGFUL-EYES"));
            Assert.IsFalse(ledger.WasMeaningfulUsed("MEANINGFUL-WATER"));
            Assert.IsFalse(ledger.TryConsumeDaypart(DayPart.Morning));
            Assert.IsTrue(ledger.TryConsumeDaypart(DayPart.Midday));
            Assert.IsTrue(ledger.TryUseMeaningful("MEANINGFUL-WATER"));
            Assert.IsFalse(ledger.TryUseMeaningful("MEANINGFUL-WATER"));

            ledger.EnsureDate("20350102");
            Assert.IsFalse(ledger.DailyOpeningConsumed);
            Assert.AreEqual(0, ledger.ConsumedDaypartsMask);
            Assert.IsFalse(ledger.WasMeaningfulUsed("MEANINGFUL-EYES"));

            string encoded = PetDailyInteractionLedger.EncodeUsedIds(
                new[] { "B", "A", "A", String.Empty });
            string[] decoded =
                PetDailyInteractionLedger.DecodeUsedIds(encoded);
            CollectionAssert.AreEqual(new[] { "A", "B" }, decoded);
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
    }
}
