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
    }
}
