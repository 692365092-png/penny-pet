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
            Line("F01-SOURCE-DIRECT", "我翻了下黄历，今天提到{0}"),
            Line("F02-SOURCE-ITEM", "今天黄历里有{0}这一项"),
            Line("F07-SOURCE-LATE", "{0}，黄历里也刚好提到了"),
            Line("F08-INTERESTING", "黄历今天这么写，看看就好：{0}")
        };

        private static readonly AlmanacWordingVariant[] Tidy =
        {
            Full("TIDY-01", "F01-SOURCE-DIRECT",
                "黄历今天提到“扫舍”，有空的话顺手收拾一下也行"),
            Full("TIDY-02", "F02-SOURCE-ITEM",
                "黄历里写了“扫舍”，说白了就是收拾收拾东西"),
            Full("TIDY-03", "F07-SOURCE-LATE",
                "今天黄历里刚好提到整理")
        };

        private static readonly AlmanacWordingVariant[] Social =
        {
            Full("SOCIAL-01", "F01-SOURCE-DIRECT",
                "想找朋友聊聊的话，黄历里也刚好有会友这一项"),
            Full("SOCIAL-02", "F02-SOURCE-ITEM",
                "我翻了下黄历，今天提到和朋友见面"),
            Full("SOCIAL-03", "F07-SOURCE-LATE",
                "黄历今天提到会友，刚好有人想见就联系一下")
        };

        private static readonly AlmanacWordingVariant[] Learning =
        {
            Full("LEARNING-01", "F01-SOURCE-DIRECT",
                "黄历今天提到学习，有一直想学的东西可以试试"),
            Full("LEARNING-02", "F02-SOURCE-ITEM",
                "学点东西，黄历里也刚好提到了"),
            Full("LEARNING-03", "F07-SOURCE-LATE",
                "今天黄历里有学习这一项，找个教程看看也行")
        };

        private static readonly AlmanacWordingVariant[] Plants =
        {
            Full("PLANTS-01", "F01-SOURCE-DIRECT",
                "黄历今天提到栽种，家里有花草的话可以看看"),
            Full("PLANTS-02", "F02-SOURCE-ITEM",
                "我翻了下黄历，今天提到种花种草"),
            Full("PLANTS-03", "F07-SOURCE-LATE",
                "今天黄历里有栽种这一项，正好看看家里的花草")
        };

        private static readonly AlmanacWordingVariant[] Haircut =
        {
            Full("HAIRCUT-01", "F01-SOURCE-DIRECT",
                "正好想剪头发的话，今天黄历里也提到了"),
            Full("HAIRCUT-02", "F02-SOURCE-ITEM",
                "今天黄历里有理发这一项"),
            Full("HAIRCUT-03", "F07-SOURCE-LATE",
                "我翻了下黄历，今天提到剪头发")
        };

        private static readonly AlmanacWordingVariant[] NailCare =
        {
            Full("NAILCARE-01", "F01-SOURCE-DIRECT",
                "正好想修指甲的话，今天黄历里也提到了"),
            Full("NAILCARE-02", "F02-SOURCE-ITEM",
                "黄历里写了“整手足甲”，就是修修指甲"),
            Full("NAILCARE-03", "F07-SOURCE-LATE",
                "修修指甲，黄历里也刚好提到了")
        };

        private static readonly AlmanacWordingVariant[] Bath =
        {
            Full("BATH-01", "F01-SOURCE-DIRECT",
                "洗个舒服的澡，黄历里也刚好提到了"),
            Full("BATH-02", "F02-SOURCE-ITEM",
                "黄历里写了“沐浴”，就是洗澡"),
            Full("BATH-03", "F07-SOURCE-LATE",
                "今天黄历里有沐浴这一项，忙完洗个澡也舒服")
        };

        private static readonly AlmanacWordingVariant[] OutingYi =
        {
            Full("OUTING-YI-01", "F01-SOURCE-DIRECT",
                "黄历今天提到出行，有出门的打算就看看天气"),
            Full("OUTING-YI-02", "F02-SOURCE-ITEM",
                "今天黄历里有出行这一项，出门前记得看天气"),
            Full("OUTING-YI-03", "F07-SOURCE-LATE",
                "我翻了下黄历，今天提到出行，按自己的计划来就好"),
            Full("OUTING-YI-04", "F08-INTERESTING",
                "想出门走走的话，黄历里也提到了，先看看天气吧"),
            Full("OUTING-YI-05", "F01-SOURCE-DIRECT",
                "黄历今天提到出行，没这个打算的话看看就好")
        };

        private static readonly AlmanacWordingVariant[] OutingJi =
        {
            Full("OUTING-JI-01", "F01-SOURCE-DIRECT",
                "黄历今天写着“忌出行”，不过要不要出门还是看天气"),
            Full("OUTING-JI-02", "F02-SOURCE-ITEM",
                "今天黄历里有“忌出行”这一项，照常安排就好"),
            Full("OUTING-JI-03", "F07-SOURCE-LATE",
                "黄历今天不建议出门，看看就好，不用为这改计划")
        };

        private static readonly AlmanacWordingVariant[] Clothing =
        {
            Full("CLOTHING-01", "F01-SOURCE-DIRECT",
                "正好有衣服想改的话，黄历里也刚好有这一项"),
            Full("CLOTHING-02", "F02-SOURCE-ITEM",
                "黄历里写了“裁衣”，就是做衣服"),
            Full("CLOTHING-03", "F07-SOURCE-LATE",
                "改改衣服，黄历里也刚好提到了")
        };

        private static readonly AlmanacWordingVariant[] Relationship =
        {
            Full("RELATIONSHIP-01", "F01-SOURCE-DIRECT",
                "黄历今天提到婚嫁喜事，没这个打算的话看看就好"),
            Full("RELATIONSHIP-02", "F02-SOURCE-ITEM",
                "今天黄历里有婚嫁这一项，按自己的打算来就好"),
            Full("RELATIONSHIP-03", "F07-SOURCE-LATE",
                "我翻了下黄历，今天提到婚嫁，看看就好")
        };

        private static readonly AlmanacWordingVariant[] Moving =
        {
            Full("MOVING-01", "F01-SOURCE-DIRECT",
                "黄历今天提到搬家，还是按自己的计划来就好"),
            Full("MOVING-02", "F02-SOURCE-ITEM",
                "今天黄历里有搬家这一项，没这个打算的话看看就好"),
            Full("MOVING-03", "F07-SOURCE-LATE",
                "我翻了下黄历，今天提到搬家，不用为这改计划")
        };

        private static readonly AlmanacWordingVariant[] Conservative =
        {
            Full("CONSERVATIVE-01", "F01-SOURCE-DIRECT",
                "黄历今天大概就是“少折腾”的意思，看看就好"),
            Full("CONSERVATIVE-02", "F02-SOURCE-ITEM",
                "黄历今天写得挺谨慎，不过正常安排就好"),
            Full("CONSERVATIVE-03", "F07-SOURCE-LATE",
                "我翻了下黄历，大概是让人少折腾，照常过就好")
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
                    return Lines("TIDY-C01", "收拾东西",
                        "TIDY-C02", "打扫屋子");
                case AlmanacTopic.Social:
                    return Lines("SOCIAL-C01", "和朋友见面",
                        "SOCIAL-C02", "找朋友聊聊天");
                case AlmanacTopic.Learning:
                    return Lines("LEARNING-C01", "学点东西",
                        "LEARNING-C02", "练练手艺");
                case AlmanacTopic.Plants:
                    return Lines("PLANTS-C01", "种花种草",
                        "PLANTS-C02", "照顾花草");
                case AlmanacTopic.Haircut:
                    return Lines("HAIRCUT-C01", "剪头发",
                        "HAIRCUT-C02", "修修头发");
                case AlmanacTopic.NailCare:
                    return Lines("NAILCARE-C01", "修指甲",
                        "NAILCARE-C02", "修修手脚的指甲");
                case AlmanacTopic.Bath:
                    return Lines("BATH-C01", "洗澡",
                        "BATH-C02", "洗个舒服的澡");
                case AlmanacTopic.ClothingCraft:
                    return Lines("CLOTHING-C01", "做衣服",
                        "CLOTHING-C02", "改改衣服");
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
