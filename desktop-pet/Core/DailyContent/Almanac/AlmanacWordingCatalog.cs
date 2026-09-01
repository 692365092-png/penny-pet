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
            Line("F01-SOURCE-DIRECT", "今天的传统日历提到一件事：{0}。"),
            Line("F02-SOURCE-ITEM", "民俗日历里有一项挺具体：{0}。"),
            Line("F03-PENNY-FLIP", "看了下今天的民俗提示，里面写着：{0}。"),
            Line("F04-PENNY-GLANCE", "我刚翻了翻传统日历，它提到：{0}。"),
            Line("F05-TRADITION", "传统宜忌里，有这么一项：{0}。"),
            Line("F06-LIFE-FIRST", "{0}，今天的民俗说法也站这边。"),
            Line("F07-SOURCE-LATE", "{0}——传统日历里刚好有这一项。"),
            Line("F08-INTERESTING", "今天这条民俗提示有点意思：{0}。"),
            Line("F09-OPINION", "这份传统日历在这件事上有点意见：{0}。"),
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
                "顺手收拾一下，今天挺应景——传统日历也偏向整理。\n那个拖了很久的小角落可以动一动。"),
            Full("TIDY-02", "F02-SOURCE-ITEM",
                "传统日历里有“扫舍”这一项，\n翻成现在的话，大概就是：适合收拾收拾。"),
            Full("TIDY-03", "F05-TRADITION",
                "按民俗里的说法，今天和整理挺合拍。\n桌面那个角落……我什么都没看见。")
        };

        private static readonly AlmanacWordingVariant[] Social =
        {
            Full("SOCIAL-01", "F01-SOURCE-DIRECT",
                "有想聊的人可以顺手联系一下。\n今天的传统日历也偏向见见朋友。"),
            Full("SOCIAL-02", "F05-TRADITION",
                "按今天的民俗说法来看，\n和朋友碰个面、聊聊天都挺应景。"),
            Full("SOCIAL-03", "F02-SOURCE-ITEM",
                "见见朋友这件事，今天挺应景——民俗日历也是这个意思。\n要是刚好有人想见，算是撞上了。")
        };

        private static readonly AlmanacWordingVariant[] Learning =
        {
            Full("LEARNING-01", "F01-SOURCE-DIRECT",
                "有什么一直想学的，可以碰一下。\n传统日历今天也偏学习和练手。"),
            Full("LEARNING-02", "F05-TRADITION",
                "按今天的民俗说法来看，\n学点东西、练点手艺都挺应景。"),
            Full("LEARNING-03", "F01-SOURCE-DIRECT",
                "传统日历今天把学习这类事放在前面。\n开个新教程也算。")
        };

        private static readonly AlmanacWordingVariant[] Plants =
        {
            Full("PLANTS-01", "F02-SOURCE-ITEM",
                "家里有植物的话，可以去看看它们。\n今天的民俗提示偏向种点东西。"),
            Full("PLANTS-02", "F05-TRADITION",
                "按传统日历的说法，\n今天和花花草草挺合拍。"),
            Full("PLANTS-03", "F01-SOURCE-DIRECT",
                "民俗日历今天提到栽种——\n有盆植物一直等你管的话，它等到机会了。")
        };

        private static readonly AlmanacWordingVariant[] Haircut =
        {
            Full("HAIRCUT-01", "F01-SOURCE-DIRECT",
                "刚好想剪头发的话挺应景。\n传统日历今天对理发也挺友好。"),
            Full("HAIRCUT-02", "F02-SOURCE-ITEM",
                "民俗提示里有理发这一项。\n发型当然还是听你的。"),
            Full("HAIRCUT-03", "F06-LIFE-FIRST",
                "如果头发已经开始有自己的想法，\n传统日历倒是给了个剪头发的借口。")
        };

        private static readonly AlmanacWordingVariant[] NailCare =
        {
            Full("NAILCARE-01", "F01-SOURCE-DIRECT",
                "修整一下指甲这种小事，今天挺应景。\n传统日历也把它列在合适的一边。"),
            Full("NAILCARE-02", "F02-SOURCE-ITEM",
                "民俗提示里有修整指甲这一项。\n小事，但做完通常挺清爽。"),
            Full("NAILCARE-03", "F06-LIFE-FIRST",
                "今天如果想处理一下指甲，\n民俗日历也觉得挺合适。")
        };

        private static readonly AlmanacWordingVariant[] Bath =
        {
            Full("BATH-01", "F01-SOURCE-DIRECT",
                "洗个舒服的澡，今天挺应景。\n传统日历也把沐浴列得挺靠前。"),
            Full("BATH-02", "F02-SOURCE-ITEM",
                "民俗提示今天对洗澡这件事挺积极——\n至少这是条很现代也很好懂的内容。"),
            Full("BATH-03", "F01-SOURCE-DIRECT",
                "忙完早点洗个澡，剩下的明天再管。\n传统日历今天也说到沐浴。")
        };

        private static readonly AlmanacWordingVariant[] OutingYi =
        {
            Full("OUTING-YI-01", "F01-SOURCE-DIRECT",
                "如果本来就有外出计划，今天挺应景。\n传统日历也偏向出门走动。"),
            Full("OUTING-YI-02", "F02-SOURCE-ITEM",
                "传统日历里“出行”在宜的一边。\n真要出门还是看天气和自己的安排。"),
            Full("OUTING-YI-03", "F05-TRADITION",
                "按民俗里的说法，今天挺适合往外走走——\n当然，天气说了算。"),
            Full("OUTING-YI-04", "F06-LIFE-FIRST",
                "想出门走走的话，今天倒挺应景——传统宜忌把“出行”放在宜的一边。\n天气和现实安排还是优先。"),
            Full("OUTING-YI-05", "F07-SOURCE-LATE",
                "如果本来有外出计划，民俗日历今天也挺配合。\n不过路线、天气和自己的安排更重要。")
        };

        private static readonly AlmanacWordingVariant[] OutingJi =
        {
            Full("OUTING-JI-01", "F01-SOURCE-DIRECT",
                "真要出门还是看天气和现实安排。\n传统日历今天对出行比较保守。"),
            Full("OUTING-JI-02", "F02-SOURCE-ITEM",
                "民俗提示今天把出行放在“忌”里。\n看看就好，路线和天气比它重要。"),
            Full("OUTING-JI-03", "F01-SOURCE-DIRECT",
                "民俗日历今天不太鼓励折腾远路。\n民俗归民俗，真有安排照现实来。")
        };

        private static readonly AlmanacWordingVariant[] Clothing =
        {
            Full("CLOTHING-01", "F02-SOURCE-ITEM",
                "要是刚好有衣服想改改，今天算是撞上了。\n传统日历也提到裁衣。"),
            Full("CLOTHING-02", "F01-SOURCE-DIRECT",
                "民俗日历今天和裁衣这类事挺合拍。\n会缝点东西的话，倒是挺应景。"),
            Full("CLOTHING-03", "F02-SOURCE-ITEM",
                "传统日历今天有裁衣这一项。\n衣柜里有件一直想改的衣服吗？")
        };

        private static readonly AlmanacWordingVariant[] Relationship =
        {
            Full("RELATIONSHIP-01", "F02-SOURCE-ITEM",
                "民俗日历今天偏向婚嫁、订盟这一类喜事。\n没这安排也没关系，当个民俗标签看看就好。"),
            Full("RELATIONSHIP-02", "F01-SOURCE-DIRECT",
                "传统日历今天的气氛挺偏喜事。\n现实里的关系怎么走，当然还是人自己决定。"),
            Full("RELATIONSHIP-03", "F02-SOURCE-ITEM",
                "婚嫁这类事今天比较显眼。\n当成一点传统日历的小彩蛋就好。")
        };

        private static readonly AlmanacWordingVariant[] Moving =
        {
            Full("MOVING-01", "F02-SOURCE-ITEM",
                "民俗日历今天比较偏搬家、入宅这类事。\n真搬家当然还是看现实安排。"),
            Full("MOVING-02", "F01-SOURCE-DIRECT",
                "传统日历今天在“搬动住处”这件事上挺积极。\n没有搬家计划的话，就当个民俗标签看看。"),
            Full("MOVING-03", "F02-SOURCE-ITEM",
                "入宅、移徙这类民俗说法今天挺显眼。\n不过搬家这种大事，现实条件说了算。")
        };

        private static readonly AlmanacWordingVariant[] Conservative =
        {
            Full("CONSERVATIVE-01", "F02-SOURCE-ITEM",
                "民俗提示今天整体比较保守，\n没给什么特别推荐。照常过日子就好。"),
            Full("CONSERVATIVE-02", "F01-SOURCE-DIRECT",
                "传统日历今天的口气有点谨慎。\n别被它吓到，正常安排自己的日子就行。"),
            Full("CONSERVATIVE-03", "F02-SOURCE-ITEM",
                "这条民俗说法今天属于“少折腾”的那一挂。\n看看就好，不用真的把日程表清空。")
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
