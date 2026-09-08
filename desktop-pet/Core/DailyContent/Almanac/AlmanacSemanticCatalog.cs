using System;
using System.Collections.Generic;

namespace PennyPet
{
    internal static class AlmanacSemanticCatalog
    {
        private static readonly Dictionary<string, AlmanacTopic> Topics =
            new Dictionary<string, AlmanacTopic>(StringComparer.Ordinal)
            {
                { "扫舍", AlmanacTopic.Tidy },
                { "会友", AlmanacTopic.Social },
                { "会亲友", AlmanacTopic.Social },
                { "入学", AlmanacTopic.Learning },
                { "习艺", AlmanacTopic.Learning },
                { "栽种", AlmanacTopic.Plants },
                { "理发", AlmanacTopic.Haircut },
                { "剃头", AlmanacTopic.Haircut },
                { "整手足甲", AlmanacTopic.NailCare },
                { "沐浴", AlmanacTopic.Bath },
                { "出行", AlmanacTopic.Outing },
                { "裁衣", AlmanacTopic.ClothingCraft },
                { "嫁娶", AlmanacTopic.RelationshipCelebration },
                { "订婚", AlmanacTopic.RelationshipCelebration },
                { "纳采", AlmanacTopic.RelationshipCelebration },
                { "订盟", AlmanacTopic.RelationshipCelebration },
                { "入宅", AlmanacTopic.MovingHome },
                { "移徙", AlmanacTopic.MovingHome },
                { "诸事不宜", AlmanacTopic.ConservativeDay },
                { "馀事勿取", AlmanacTopic.ConservativeDay },
                { "余事勿取", AlmanacTopic.ConservativeDay }
            };

        internal static bool TryMap(string rawTerm, out AlmanacTopic topic)
        {
            if (rawTerm == null)
            {
                topic = default(AlmanacTopic);
                return false;
            }
            return Topics.TryGetValue(rawTerm.Trim(), out topic);
        }

        internal static bool IsEverydayYi(AlmanacTopic topic)
        {
            return topic == AlmanacTopic.Tidy ||
                topic == AlmanacTopic.Social ||
                topic == AlmanacTopic.Learning ||
                topic == AlmanacTopic.Plants ||
                topic == AlmanacTopic.Haircut ||
                topic == AlmanacTopic.NailCare ||
                topic == AlmanacTopic.Bath ||
                topic == AlmanacTopic.Outing ||
                topic == AlmanacTopic.ClothingCraft;
        }

        internal static bool IsCultural(AlmanacTopic topic, bool isYi)
        {
            return (isYi && (topic ==
                    AlmanacTopic.RelationshipCelebration ||
                topic == AlmanacTopic.MovingHome)) ||
                (topic == AlmanacTopic.Outing && !isYi);
        }

        internal static bool CanUseLightEnding(AlmanacTopic topic,
            bool isYi)
        {
            return isYi && topic != AlmanacTopic.Outing &&
                IsEverydayYi(topic);
        }
    }
}
