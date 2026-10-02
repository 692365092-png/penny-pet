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
        public void PetBirthdayRule_ResolvesExclusiveBirthdayKinds()
        {
            Assert.AreEqual(PetBirthdayKind.Penny,
                PetBirthdayRule.Resolve(4, 22, 0, 0));
            Assert.AreEqual(PetBirthdayKind.User,
                PetBirthdayRule.Resolve(9, 10, 9, 10));
            Assert.AreEqual(PetBirthdayKind.Shared,
                PetBirthdayRule.Resolve(4, 22, 4, 22));
            Assert.AreEqual(PetBirthdayKind.None,
                PetBirthdayRule.Resolve(5, 5, 9, 10));
        }

        [TestMethod]
        public void PetBirthdayRule_ValidatesAndDerivesZodiac()
        {
            Assert.IsFalse(PetBirthdayRule.IsValidBirthday(0, 0));
            Assert.IsFalse(PetBirthdayRule.IsValidBirthday(13, 1));
            Assert.IsTrue(PetBirthdayRule.IsValidBirthday(2, 29));

            ZodiacSign sign;
            Assert.IsTrue(PetBirthdayRule.TryDeriveZodiac(4, 22,
                out sign));
            Assert.AreEqual(ZodiacSign.Taurus, sign);
            Assert.IsTrue(PetBirthdayRule.TryDeriveZodiac(12, 22,
                out sign));
            Assert.AreEqual(ZodiacSign.Capricorn, sign);
            Assert.IsFalse(PetBirthdayRule.TryDeriveZodiac(2, 30,
                out sign));
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
            Assert.AreEqual("下午好，今天过得怎么样",
                DailyContentRules.GreetingBodyFor(DayPart.Afternoon));
            Assert.AreEqual(PetSentenceIntent.Question,
                DailyContentRules.GreetingIntentFor(DayPart.Afternoon));
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
        public void SentenceEndingPolicy_IsRoleAwareDeterministicAndSafe()
        {
            DateTime date = new DateTime(2026, 9, 3);
            string middle = PetSentenceEndingPolicy.Apply(
                "忙完早点洗个澡，剩下的明天再管",
                new PetSentenceEndingContext(PetSentenceRole.Middle,
                    PetSentenceIntent.Gentle,
                    PetSentenceContentKind.Almanac, "BATH-MIDDLE", date));
            Assert.AreEqual("忙完早点洗个澡，剩下的明天再管。", middle);
            string closing = PetSentenceEndingPolicy.Apply(
                "传统日历今天也说到沐浴",
                new PetSentenceEndingContext(PetSentenceRole.Closing,
                    PetSentenceIntent.Gentle,
                    PetSentenceContentKind.Almanac,
                    "ALMANAC-BATH-03", date));
            Assert.AreEqual("传统日历今天也说到沐浴啦～", closing);
            string question = PetSentenceEndingPolicy.Apply(
                "今天过得怎么样",
                new PetSentenceEndingContext(PetSentenceRole.Single,
                    PetSentenceIntent.Question,
                    PetSentenceContentKind.SmallTalk, "QUESTION", date));
            Assert.IsTrue(question.EndsWith("？", StringComparison.Ordinal));
            string cheerful = PetSentenceEndingPolicy.Apply(
                "终于做完了",
                new PetSentenceEndingContext(PetSentenceRole.Closing,
                    PetSentenceIntent.Cheerful,
                    PetSentenceContentKind.Curated, "CHEERFUL", date));
            Assert.IsFalse(cheerful.EndsWith("喔～",
                StringComparison.Ordinal));
            string seriousWeather = PetSentenceEndingPolicy.Apply(
                "低洼路段尽量绕开",
                new PetSentenceEndingContext(PetSentenceRole.Closing,
                    PetSentenceIntent.Serious,
                    PetSentenceContentKind.Weather, "HEAVY-RAIN", date));
            Assert.IsFalse(seriousWeather.EndsWith("耶～",
                StringComparison.Ordinal) || seriousWeather.EndsWith("呀～",
                StringComparison.Ordinal));
            for (int i = 0; i < 100; i++)
                Assert.AreEqual(closing, PetSentenceEndingPolicy.Apply(
                    "传统日历今天也说到沐浴",
                    new PetSentenceEndingContext(PetSentenceRole.Closing,
                        PetSentenceIntent.Gentle,
                        PetSentenceContentKind.Almanac,
                        "ALMANAC-BATH-03", date)));
            Assert.AreEqual("今天辛苦啦～",
                PetSentenceEndingPolicy.ApplyEnding("今天辛苦了", "啦～"));
        }

        [TestMethod]
        public void DailyBriefingComposer_EnforcesSemanticSentenceBudget()
        {
            DateTime date = new DateTime(2026, 9, 7);
            DailyLineEntry curated = new DailyLineEntry("C-TEST", "精选。 ");
            DailyLineEntry zodiac = new DailyLineEntry("Z-TEST", "星座。 ");
            SolarTermInfo? whiteDew = new SolarTermInfo(SolarTerm.WhiteDew,
                "白露", 165, new DateTimeOffset(2026, 9, 7, 12, 0, 0,
                    TimeSpan.FromHours(8)));
            WeatherDailySelection weather = new WeatherDailySelection(
                WeatherMeaning.Windy, "WEATHER-WINDY-TEST",
                "今天风比较大，出门注意一下");
            AlmanacDailySelection almanac = new AlmanacDailySelection(
                AlmanacTopic.MovingHome, "入宅", true, "MOVING-TEST",
                "F-TEST", "W-TEST",
                "传统日历今天提到搬家，没计划的话看看就好");
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
            CollectionAssert.AreEqual(new[]
            {
                PetSentenceContentKind.Greeting,
                PetSentenceContentKind.Solar,
                PetSentenceContentKind.Weather
            }, DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                solarWeatherAlmanac).Select(sentence => sentence.Kind)
                .ToArray());
            CollectionAssert.AreEqual(new[]
            {
                PetSentenceContentKind.Greeting,
                PetSentenceContentKind.Weather
            }, DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                weatherOnly).Select(sentence => sentence.Kind).ToArray());
            CollectionAssert.AreEqual(new[]
            {
                PetSentenceContentKind.Greeting,
                PetSentenceContentKind.Almanac
            }, DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                almanacOnly).Select(sentence => sentence.Kind).ToArray());

            DailyLineEntry birthday = new DailyLineEntry(
                "BIRTHDAY-TEST", "生日快乐。 ");
            DailyBriefingContent birthdaySolarWeather =
                new DailyBriefingContent(whiteDew, weather, null,
                    curated, zodiac, birthday, PetBirthdayKind.User);
            CollectionAssert.AreEqual(new[]
            {
                PetSentenceContentKind.Greeting,
                PetSentenceContentKind.Birthday,
                PetSentenceContentKind.Solar
            }, DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                birthdaySolarWeather).Select(sentence => sentence.Kind)
                .ToArray());
            CollectionAssert.AreEqual(new[]
            {
                PetSentenceContentKind.Greeting,
                PetSentenceContentKind.Curated,
                PetSentenceContentKind.Zodiac
            }, DailyBriefingComposer.SelectSentences(DayPart.Afternoon,
                fallback).Select(sentence => sentence.Kind).ToArray());
            string[] cases =
            {
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    solarWeatherAlmanac),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    solarWeather),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    solarAlmanac),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    solarOnly),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    weatherAlmanac),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    weatherOnly),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    almanacOnly),
                DailyBriefingComposer.Compose(DayPart.Afternoon, date,
                    fallback)
            };
            Assert.IsTrue(cases.All(text => text.Split('\n').Length <= 3));
            Assert.IsTrue(new[] { solarWeatherAlmanac, solarWeather,
                solarAlmanac, solarOnly, weatherAlmanac, weatherOnly,
                almanacOnly, fallback }.All(
                content =>
                DailyBriefingComposer.SelectSupplementary(content).Length <=
                    2));
            string single = DailyBriefingComposer.ComposeSentences(date,
                new[]
                {
                    new DailyBriefingSentence("今天是白露",
                        PetSentenceContentKind.Solar,
                        PetSentenceIntent.Gentle, "SOLAR-WHITE-DEW")
                });
            Assert.IsFalse(single.Contains("\n"));
            string two = DailyBriefingComposer.ComposeSentences(date,
                new[]
                {
                    new DailyBriefingSentence("早上好",
                        PetSentenceContentKind.Greeting,
                        PetSentenceIntent.Gentle, "GREETING"),
                    new DailyBriefingSentence("今天风比较大",
                        PetSentenceContentKind.Weather,
                        PetSentenceIntent.Gentle, "WEATHER")
                });
            Assert.AreEqual(2, two.Split('\n').Length);
            string three = DailyBriefingComposer.ComposeSentences(date,
                new[]
                {
                    new DailyBriefingSentence("晚上好",
                        PetSentenceContentKind.Greeting,
                        PetSentenceIntent.Gentle, "GREETING"),
                    new DailyBriefingSentence("忙完早点洗个澡",
                        PetSentenceContentKind.Almanac,
                        PetSentenceIntent.Gentle, "BATH-1"),
                    new DailyBriefingSentence("传统日历今天也说到沐浴",
                        PetSentenceContentKind.Almanac,
                        PetSentenceIntent.Gentle, "BATH-2")
                });
            Assert.IsTrue(three.Split('\n')[1].EndsWith("。",
                StringComparison.Ordinal));
        }
    }
}
