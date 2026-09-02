namespace PennyPet
{
    internal sealed class AlmanacWordingVariant
    {
        internal AlmanacWordingVariant(string id, string framingId,
            string text)
        {
            Id = id;
            FramingId = framingId;
            Text = text;
        }

        internal string Id { get; private set; }
        internal string FramingId { get; private set; }
        internal string Text { get; private set; }
    }

    internal static class AlmanacWordingCatalog
    {
        private static readonly DailyLineEntry[] Framings =
        {
            Line("F01-SOURCE-DIRECT", "传统日历今天提到{0}"),
            Line("F02-SOURCE-ITEM", "民俗日历里有一项：{0}"),
            Line("F03-PENNY-FLIP", "看了下今天的民俗提示，里面写着{0}"),
            Line("F04-PENNY-GLANCE", "我刚翻了翻传统日历，它提到{0}"),
            Line("F05-TRADITION", "传统宜忌里有一项：{0}"),
            Line("F06-LIFE-FIRST", "{0}，今天的民俗说法也站这边"),
            Line("F07-SOURCE-LATE", "{0}，传统日历里刚好有这一项"),
            Line("F08-INTERESTING", "今天这条民俗提示有点意思：{0}"),
            Line("F09-OPINION", "这份传统日历在这件事上提到{0}"),
            Line("F10-MODERN", "今日宜忌里有件好理解的事：{0}")
        };

        private static readonly AlmanacWordingVariant[] Tidy =
        {
            Full("TIDY-01", "F01-SOURCE-DIRECT",
                "今天的民俗提示偏整理，有空可以顺手收拾一下"),
            Full("TIDY-02", "F02-SOURCE-ITEM",
                "传统日历今天提到扫舍，翻成现在就是适合收拾"),
            Full("TIDY-03", "F05-TRADITION",
                "按民俗里的说法，今天和整理挺合拍")
        };

        private static readonly AlmanacWordingVariant[] Social =
        {
            Full("SOCIAL-01", "F01-SOURCE-DIRECT",
                "有想聊的人可以顺手联系一下，传统日历也偏向会友"),
            Full("SOCIAL-02", "F05-TRADITION",
                "按今天的民俗说法，和朋友见面聊天挺应景"),
            Full("SOCIAL-03", "F02-SOURCE-ITEM",
                "传统日历今天提到会友，刚好有人想见就联系一下")
        };

        private static readonly AlmanacWordingVariant[] Learning =
        {
            Full("LEARNING-01", "F01-SOURCE-DIRECT",
                "有一直想学的东西可以碰一下，传统日历也偏学习"),
            Full("LEARNING-02", "F05-TRADITION",
                "按今天的民俗说法，学点东西挺应景"),
            Full("LEARNING-03", "F01-SOURCE-DIRECT",
                "传统日历今天把学习放在前面，开个新教程也算")
        };

        private static readonly AlmanacWordingVariant[] Plants =
        {
            Full("PLANTS-01", "F02-SOURCE-ITEM",
                "家里有植物的话可以看看，今天的民俗提示偏栽种"),
            Full("PLANTS-02", "F05-TRADITION",
                "按传统日历的说法，今天和花草挺合拍"),
            Full("PLANTS-03", "F01-SOURCE-DIRECT",
                "民俗日历今天提到栽种，有盆植物等着的话去看看")
        };

        private static readonly AlmanacWordingVariant[] Haircut =
        {
            Full("HAIRCUT-01", "F01-SOURCE-DIRECT",
                "刚好想剪头发的话可以安排，传统日历也提到理发"),
            Full("HAIRCUT-02", "F02-SOURCE-ITEM",
                "民俗提示里有理发，发型当然还是听你的"),
            Full("HAIRCUT-03", "F06-LIFE-FIRST",
                "头发已经有想法的话，传统日历给了个修剪借口")
        };

        private static readonly AlmanacWordingVariant[] NailCare =
        {
            Full("NAILCARE-01", "F01-SOURCE-DIRECT",
                "修整一下指甲挺应景，传统日历也提到了"),
            Full("NAILCARE-02", "F02-SOURCE-ITEM",
                "民俗提示里有修整指甲，做完通常挺清爽"),
            Full("NAILCARE-03", "F06-LIFE-FIRST",
                "今天如果想处理指甲，民俗日历也觉得合适")
        };

        private static readonly AlmanacWordingVariant[] Bath =
        {
            Full("BATH-01", "F01-SOURCE-DIRECT",
                "洗个舒服的澡挺应景，传统日历也提到沐浴"),
            Full("BATH-02", "F02-SOURCE-ITEM",
                "民俗提示今天提到洗澡，这条倒很容易理解"),
            Full("BATH-03", "F01-SOURCE-DIRECT",
                "忙完早点洗个澡，传统日历今天也说到沐浴")
        };

        private static readonly AlmanacWordingVariant[] OutingYi =
        {
            Full("OUTING-YI-01", "F01-SOURCE-DIRECT",
                "如果本来有外出计划，传统日历今天也偏向出行"),
            Full("OUTING-YI-02", "F02-SOURCE-ITEM",
                "传统日历今天提到出行，真要出门还是看天气"),
            Full("OUTING-YI-03", "F05-TRADITION",
                "按民俗说法今天适合走走，实际安排照常优先"),
            Full("OUTING-YI-04", "F06-LIFE-FIRST",
                "想出门走走的话挺应景，天气和现实安排优先"),
            Full("OUTING-YI-05", "F07-SOURCE-LATE",
                "民俗日历今天提到出行，有计划就按实际情况来")
        };

        private static readonly AlmanacWordingVariant[] OutingJi =
        {
            Full("OUTING-JI-01", "F01-SOURCE-DIRECT",
                "传统日历今天对出行较保守，真要出门仍看天气"),
            Full("OUTING-JI-02", "F02-SOURCE-ITEM",
                "民俗提示把出行放在忌里，路线和天气更重要"),
            Full("OUTING-JI-03", "F01-SOURCE-DIRECT",
                "民俗日历今天不鼓励远行，已有安排照现实来")
        };

        private static readonly AlmanacWordingVariant[] Clothing =
        {
            Full("CLOTHING-01", "F02-SOURCE-ITEM",
                "有衣服想改的话挺应景，传统日历也提到裁衣"),
            Full("CLOTHING-02", "F01-SOURCE-DIRECT",
                "民俗日历今天和裁衣合拍，会缝东西的话可以动手"),
            Full("CLOTHING-03", "F02-SOURCE-ITEM",
                "传统日历今天提到裁衣，衣柜里那件可以看看")
        };

        private static readonly AlmanacWordingVariant[] Relationship =
        {
            Full("RELATIONSHIP-01", "F02-SOURCE-ITEM",
                "传统日历今天偏向婚嫁喜事，没安排就当个文化标签"),
            Full("RELATIONSHIP-02", "F01-SOURCE-DIRECT",
                "今天的民俗气氛偏喜事，关系怎么走仍由自己决定"),
            Full("RELATIONSHIP-03", "F02-SOURCE-ITEM",
                "婚嫁这类说法今天显眼，当作传统日历的小彩蛋就好")
        };

        private static readonly AlmanacWordingVariant[] Moving =
        {
            Full("MOVING-01", "F02-SOURCE-ITEM",
                "民俗日历今天提到搬家，真搬仍要看现实安排"),
            Full("MOVING-02", "F01-SOURCE-DIRECT",
                "传统日历今天偏向搬动住处，没计划的话看看就好"),
            Full("MOVING-03", "F02-SOURCE-ITEM",
                "入宅移徙今天比较显眼，搬家仍以现实条件为准")
        };

        private static readonly AlmanacWordingVariant[] Conservative =
        {
            Full("CONSERVATIVE-01", "F02-SOURCE-ITEM",
                "民俗提示今天比较保守，照常安排日子就好"),
            Full("CONSERVATIVE-02", "F01-SOURCE-DIRECT",
                "传统日历今天口气谨慎，不必因此打乱正常计划"),
            Full("CONSERVATIVE-03", "F02-SOURCE-ITEM",
                "这条民俗说法偏少折腾，看看就好")
        };

        internal static AlmanacWordingVariant[] GetFullVariants(
            AlmanacTopic topic, bool isYi)
        {
            switch (topic)
            {
                case AlmanacTopic.Tidy: return Tidy;
                case AlmanacTopic.Social: return Social;
                case AlmanacTopic.Learning: return Learning;
                case AlmanacTopic.Plants: return Plants;
                case AlmanacTopic.Haircut: return Haircut;
                case AlmanacTopic.NailCare: return NailCare;
                case AlmanacTopic.Bath: return Bath;
                case AlmanacTopic.Outing: return isYi ? OutingYi : OutingJi;
                case AlmanacTopic.ClothingCraft: return Clothing;
                case AlmanacTopic.RelationshipCelebration:
                    return Relationship;
                case AlmanacTopic.MovingHome: return Moving;
                case AlmanacTopic.ConservativeDay: return Conservative;
                default: return new AlmanacWordingVariant[0];
            }
        }

        internal static DailyLineEntry[] GetFramings(AlmanacTopic topic,
            bool isYi)
        {
            if (!AlmanacSemanticCatalog.CanUseLightEnding(topic, isYi))
                return new DailyLineEntry[0];
            return Framings;
        }

        internal static DailyLineEntry[] GetCores(AlmanacTopic topic,
            bool isYi)
        {
            if (!isYi) return new DailyLineEntry[0];
            switch (topic)
            {
                case AlmanacTopic.Tidy:
                    return Lines("TIDY-C01", "可以收拾收拾东西",
                        "TIDY-C02", "可以动动拖了很久的小角落");
                case AlmanacTopic.Social:
                    return Lines("SOCIAL-C01", "可以顺手联系一下想聊的人",
                        "SOCIAL-C02", "和朋友碰个面、聊聊天都挺应景");
                case AlmanacTopic.Learning:
                    return Lines("LEARNING-C01", "可以学点东西、练点手艺",
                        "LEARNING-C02", "可以碰一下那个一直想学的东西");
                case AlmanacTopic.Plants:
                    return Lines("PLANTS-C01", "可以去看看家里的花花草草",
                        "PLANTS-C02", "适合给一直等着的植物一点照顾");
                case AlmanacTopic.Haircut:
                    return Lines("HAIRCUT-C01", "刚好想剪头发的话挺应景",
                        "HAIRCUT-C02", "头发有想法的话，可以考虑修一修");
                case AlmanacTopic.NailCare:
                    return Lines("NAILCARE-C01", "可以顺手修整一下指甲",
                        "NAILCARE-C02", "处理一下指甲这种小事挺合拍");
                case AlmanacTopic.Bath:
                    return Lines("BATH-C01", "洗个舒服的澡很应景",
                        "BATH-C02", "忙完早点洗个澡也不错");
                case AlmanacTopic.ClothingCraft:
                    return Lines("CLOTHING-C01", "可以改改或缝补一件衣服",
                        "CLOTHING-C02", "裁衣、改衣这类事挺应景");
                default:
                    return new DailyLineEntry[0];
            }
        }

        private static AlmanacWordingVariant Full(string id,
            string framingId, string text)
        {
            return new AlmanacWordingVariant(id, framingId, text);
        }

        private static DailyLineEntry Line(string id, string text)
        {
            return new DailyLineEntry(id, text);
        }

        private static DailyLineEntry[] Lines(string firstId,
            string firstText, string secondId, string secondText)
        {
            return new[]
            {
                Line(firstId, firstText),
                Line(secondId, secondText)
            };
        }
    }
}
