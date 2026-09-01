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
            Line("F01-SOURCE-DIRECT", "今日宜忌里提到一件事：{0}。"),
            Line("F02-SOURCE-ITEM", "传统宜忌里有一项挺具体：{0}。"),
            Line("F03-PENNY-FLIP", "看了下今天的宜忌，里面写着：{0}。"),
            Line("F04-PENNY-GLANCE", "我刚看了一眼今日宜忌，它提到：{0}。"),
            Line("F05-TRADITION", "传统宜忌里，有这么一项：{0}。"),
            Line("F06-LIFE-FIRST", "{0}，今日宜忌也站这边。"),
            Line("F07-SOURCE-LATE", "{0}——今日宜忌里刚好有这一项。"),
            Line("F08-INTERESTING", "今日宜忌有点意思，它说：{0}。"),
            Line("F09-OPINION", "这份传统宜忌在这件事上有点意见：{0}。"),
            Line("F10-MODERN", "今日宜忌难得说了件挺好理解的事：{0}。")
        };

        private static readonly DailyLineEntry[] Endings =
        {
            Line("E01-NO-RUSH", "有空再碰就好，不用赶。"),
            Line("E02-EASY", "至少这条挺好执行。"),
            Line("E03-MODERN", "这条放到现代，倒没什么翻译难度。")
        };

        private static readonly AlmanacWordingVariant[] Tidy =
        {
            Full("TIDY-01", "F01-SOURCE-DIRECT",
                "今日宜忌挺偏向收拾整理，\n那个拖了很久的小角落可以顺手动一动。"),
            Full("TIDY-02", "F02-SOURCE-ITEM",
                "今日宜忌里有“扫舍”这一项，\n翻成现在的话，大概就是：适合收拾收拾。"),
            Full("TIDY-03", "F05-TRADITION",
                "按传统宜忌的说法，今天和整理挺合拍。\n桌面那个角落……我什么都没看见。")
        };

        private static readonly AlmanacWordingVariant[] Social =
        {
            Full("SOCIAL-01", "F01-SOURCE-DIRECT",
                "今日宜忌挺偏向见见朋友，\n有想聊的人可以顺手联系一下。"),
            Full("SOCIAL-02", "F05-TRADITION",
                "按今天的宜忌来看，\n和朋友碰个面、聊聊天都挺应景。"),
            Full("SOCIAL-03", "F02-SOURCE-ITEM",
                "今日宜忌对见朋友这件事挺友好。\n要是刚好有人想见，算是撞上了。")
        };

        private static readonly AlmanacWordingVariant[] Learning =
        {
            Full("LEARNING-01", "F01-SOURCE-DIRECT",
                "今日宜忌挺偏学习和练手，\n有什么一直想学的，可以碰一下。"),
            Full("LEARNING-02", "F05-TRADITION",
                "按今天的宜忌来看，\n学点东西、练点手艺都挺应景。"),
            Full("LEARNING-03", "F01-SOURCE-DIRECT",
                "今日宜忌把学习这类事放在前面。\n开个新教程也算。")
        };

        private static readonly AlmanacWordingVariant[] Plants =
        {
            Full("PLANTS-01", "F02-SOURCE-ITEM",
                "今天的宜忌挺偏向种点东西。\n家里有植物的话，可以去看看它们。"),
            Full("PLANTS-02", "F05-TRADITION",
                "按传统宜忌的说法，\n今天和花花草草挺合拍。"),
            Full("PLANTS-03", "F01-SOURCE-DIRECT",
                "今日宜忌提到栽种——\n有盆植物一直等你管的话，它等到机会了。")
        };

        private static readonly AlmanacWordingVariant[] Haircut =
        {
            Full("HAIRCUT-01", "F01-SOURCE-DIRECT",
                "今日宜忌对理发这件事挺友好。\n刚好想剪的话，算是挺应景。"),
            Full("HAIRCUT-02", "F02-SOURCE-ITEM",
                "今日宜忌里有理发这一项。\n发型当然还是听你的。"),
            Full("HAIRCUT-03", "F06-LIFE-FIRST",
                "如果头发已经开始有自己的想法，\n今日宜忌倒是给了个剪头发的借口。")
        };

        private static readonly AlmanacWordingVariant[] NailCare =
        {
            Full("NAILCARE-01", "F01-SOURCE-DIRECT",
                "今日宜忌有点偏向修整指甲这种小事，\n属于很容易完成的一项。"),
            Full("NAILCARE-02", "F02-SOURCE-ITEM",
                "今日宜忌里有修整指甲这一项。\n小事，但做完通常挺清爽。"),
            Full("NAILCARE-03", "F06-LIFE-FIRST",
                "今天如果想处理一下指甲，\n传统宜忌也有意见：它觉得挺合适。")
        };

        private static readonly AlmanacWordingVariant[] Bath =
        {
            Full("BATH-01", "F01-SOURCE-DIRECT",
                "今日宜忌把沐浴列得挺靠前。\n洗个舒服的澡，这条倒很好执行。"),
            Full("BATH-02", "F02-SOURCE-ITEM",
                "今日宜忌对洗澡这件事挺积极——\n至少这是条很现代也很好懂的内容。"),
            Full("BATH-03", "F01-SOURCE-DIRECT",
                "今日宜忌说到沐浴。\n忙完早点洗个澡，剩下的明天再管。")
        };

        private static readonly AlmanacWordingVariant[] OutingYi =
        {
            Full("OUTING-YI-01", "F01-SOURCE-DIRECT",
                "今日宜忌偏向出门走动。\n如果本来就有外出计划，倒挺应景。"),
            Full("OUTING-YI-02", "F02-SOURCE-ITEM",
                "今日宜忌里“出行”在宜的一边。\n真要出门还是看天气和自己的安排。"),
            Full("OUTING-YI-03", "F05-TRADITION",
                "按传统宜忌的说法，今天挺适合往外走走——\n当然，天气说了算。"),
            Full("OUTING-YI-04", "F06-LIFE-FIRST",
                "想出门走走的话，今天倒挺应景——传统宜忌把“出行”放在宜的一边。\n天气和现实安排还是优先。"),
            Full("OUTING-YI-05", "F07-SOURCE-LATE",
                "如果本来有外出计划，今日宜忌也挺配合。\n不过路线、天气和自己的安排更重要。")
        };

        private static readonly AlmanacWordingVariant[] OutingJi =
        {
            Full("OUTING-JI-01", "F01-SOURCE-DIRECT",
                "今日宜忌对出行比较保守。\n真要出门还是看天气和现实安排。"),
            Full("OUTING-JI-02", "F02-SOURCE-ITEM",
                "今日宜忌把出行放在“忌”里。\n看看就好，路线和天气比它重要。"),
            Full("OUTING-JI-03", "F01-SOURCE-DIRECT",
                "今日宜忌不太鼓励折腾远路。\n民俗归民俗，真有安排照现实来。")
        };

        private static readonly AlmanacWordingVariant[] Clothing =
        {
            Full("CLOTHING-01", "F02-SOURCE-ITEM",
                "今天的宜忌提到裁衣。\n要是刚好有衣服想改改，算是撞上了。"),
            Full("CLOTHING-02", "F01-SOURCE-DIRECT",
                "今日宜忌和裁衣这类事挺合拍。\n会缝点东西的话，倒是挺应景。"),
            Full("CLOTHING-03", "F02-SOURCE-ITEM",
                "今日宜忌里有裁衣这一项。\n衣柜里有件一直想改的衣服吗？")
        };

        private static readonly AlmanacWordingVariant[] Relationship =
        {
            Full("RELATIONSHIP-01", "F02-SOURCE-ITEM",
                "今天的宜忌偏向婚嫁、订盟这一类喜事。\n没这安排也没关系，当个民俗标签看看就好。"),
            Full("RELATIONSHIP-02", "F01-SOURCE-DIRECT",
                "今日宜忌的气氛挺偏喜事。\n现实里的关系怎么走，当然还是人自己决定。"),
            Full("RELATIONSHIP-03", "F02-SOURCE-ITEM",
                "今日宜忌里婚嫁这类事比较显眼。\n当成一点传统日历的小彩蛋就好。")
        };

        private static readonly AlmanacWordingVariant[] Moving =
        {
            Full("MOVING-01", "F02-SOURCE-ITEM",
                "今天的宜忌比较偏搬家、入宅这类事。\n真搬家当然还是看现实安排。"),
            Full("MOVING-02", "F01-SOURCE-DIRECT",
                "今日宜忌在“搬动住处”这件事上挺积极。\n没有搬家计划的话，就当个民俗标签看看。"),
            Full("MOVING-03", "F02-SOURCE-ITEM",
                "今日宜忌里入宅、移徙这类内容挺显眼。\n不过搬家这种大事，现实条件说了算。")
        };

        private static readonly AlmanacWordingVariant[] Conservative =
        {
            Full("CONSERVATIVE-01", "F02-SOURCE-ITEM",
                "今天的宜忌整体比较保守，\n没给什么特别推荐。照常过日子就好。"),
            Full("CONSERVATIVE-02", "F01-SOURCE-DIRECT",
                "今日宜忌的口气有点谨慎。\n别被它吓到，正常安排自己的日子就行。"),
            Full("CONSERVATIVE-03", "F02-SOURCE-ITEM",
                "今天的宜忌属于“少折腾”的那一挂。\n民俗看看就好，不用真的把日程表清空。")
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

        internal static DailyLineEntry[] GetEndings()
        {
            return Endings;
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
