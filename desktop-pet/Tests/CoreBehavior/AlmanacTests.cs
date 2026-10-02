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
                    Assert.AreEqual(selected.Text, retry.Text);
                    Assert.IsFalse(selected.Text.Contains("今天一定") ||
                        selected.Text.Contains("必须") ||
                        selected.Text.Contains("千万不要") ||
                        selected.Text.Contains("绝对不能"));
                    Assert.IsFalse(selected.Text.Contains("老黄历"));
                    Assert.IsFalse(selected.Text.Contains("\n") ||
                        selected.Text.Contains("。") ||
                        selected.Text.Contains("！") ||
                        selected.Text.Contains("？"), selected.Text);
                    Assert.IsTrue(selected.Text.Length <= 36,
                        selected.Text);
                    StringAssert.Contains(selected.Text, "黄历");
                    variants.Add(selected.VariantId);
                }
                Assert.IsTrue(variants.Count >= item.Minimum,
                    item.Topic + ": " + variants.Count);
            }
        }

        private static void AssertAlmanacMapping(string raw,
            AlmanacTopic expected)
        {
            AlmanacTopic actual;
            Assert.IsTrue(AlmanacSemanticCatalog.TryMap(raw, out actual), raw);
            Assert.AreEqual(expected, actual, raw);
        }
    }
}
