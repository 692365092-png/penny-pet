using System;

namespace PennyPet
{
    internal static class WeatherWordingCatalog
    {
        private static readonly string[] Snow =
        {
            "外面有下雪迹象，鞋底和路面都可能有点滑，出门慢一点。",
            "今天可能会见到雪，围巾手套带上，走路也别太急。",
            "雪意挺明显，保暖之外也留意湿滑的台阶和路口。"
        };

        private static readonly string[] RainAndWind =
        {
            "雨和风今天凑到一起了，伞要拿稳，外套也选挡风一点的。",
            "外面既有雨又有风，出门带伞，也给行程留一点余量。",
            "风雨会一起出现，关好窗，路上也别让伞和风较劲。"
        };

        private static readonly string[] RainAndCooling =
        {
            "下雨还会带来降温，伞和稍厚一点的外套都用得上。",
            "雨水会把体感拉低一些，出门别只顾带伞，也添件衣服。",
            "今天有雨又转凉，鞋袜尽量别湿，晚上也记得加一层。"
        };

        private static readonly string[] HeavyRain =
        {
            "雨势可能比较认真，出门尽量避开积水，行程也放宽一点。",
            "今天的雨量偏多，伞要带，赶路时也给自己多留些时间。",
            "外面的雨可能不小，低洼路段绕一绕，鞋子也选耐湿的。"
        };

        private static readonly string[] PersistentRain =
        {
            "雨可能断断续续陪很久，伞别嫌麻烦，全天都先带着。",
            "今天不少时段都有雨，晾晒和长时间户外安排可以缓缓。",
            "这场雨可能待得比较久，出门前检查一下伞，也关好窗。"
        };

        private static readonly string[] Windy =
        {
            "风会比较有存在感，帽子和轻东西收好，骑行也慢一点。",
            "外面风劲不小，出门把外套拉好，窗边的小物件也看看。",
            "今天适合把容易吹跑的东西收稳，走到空旷处也别太急。",
            "风有点认真，关窗时留意夹手，路上也和招牌树枝保持距离。",
            "出门会明显感觉到风，围巾帽子戴稳，伞也别硬撑。",
            "空气今天跑得挺快，阳台先检查一遍，外出注意脚下。"
        };

        private static readonly string[] Cooling =
        {
            "气温比昨天掉得明显，出门前多拿一件，晚些时候会用上。",
            "今天会比昨天凉不少，别被早上的短暂暖意骗到。",
            "降温来得挺直接，薄外套先带着，手脚冷的话再加一层。",
            "体感会往下走一截，开窗通风别太久，外出也注意保暖。",
            "昨天那套衣服今天可能不够，添件外套会更从容。",
            "温度明显回落，早晚尤其容易凉，出门别少带衣服。"
        };

        private static readonly string[] Warming =
        {
            "今天会比昨天暖不少，中午可能用不上厚外套，方便穿脱就好。",
            "温度正在往上走，早上别穿得太死，中午好减一层。",
            "明显回暖的一天，室内外来回时记得按体感增减衣服。"
        };

        private static readonly string[] RainLater =
        {
            "前半天可能还算平静，晚些时候更容易下雨，伞先放包里。",
            "雨更可能在后半天出现，早上出门也别把伞留在家。",
            "白天越往后越要留意雨，回程安排可以稍微宽松一点。",
            "出门时没下雨也别大意，后面有机会用上那把伞。",
            "今天的雨来得偏晚，晾晒记得早点收，回家路上留意天气。",
            "午后到晚上更可能遇到雨，包里带把轻便伞会省事。"
        };

        private static readonly string[] Hot =
        {
            "体感会很热，水要及时喝，长时间晒太阳的安排尽量避开。",
            "今天热得比较实在，出门找阴凉走，别等口渴才喝水。",
            "高温体感会很明显，午后少在太阳下久留，也照顾好宠物。",
            "外面容易闷热，衣服选透气一点，运动强度也别硬撑。",
            "热意会持续一阵，水杯带上，能挪到凉快时段的事就挪一挪。",
            "今天适合慢一点，补水和休息都别省，车内也别久留。"
        };

        private static readonly string[] Cold =
        {
            "体感会到冰点附近，手脚和耳朵都照顾一下，路面也多看一眼。",
            "外面冷得比较认真，保暖层穿够，久坐后起身也活动活动。",
            "今天容易觉得冻，围巾手套有就带上，早晚别在风里久站。"
        };

        private static readonly string[] LargeTemperatureRange =
        {
            "早晚和中午差得有点多，方便穿脱的叠穿会比较省心。",
            "一天里的温差不小，早出晚归的话，外套先别嫌占地方。",
            "中午和早晚像两个季节，分层穿衣比一次穿厚更好调整。"
        };

        internal static WeatherDailySelection Select(WeatherMeaning meaning,
            DateTime localDate, string locationStableKey)
        {
            string[] variants = GetVariants(meaning);
            int index = StableIndex(localDate.Date, locationStableKey,
                meaning, variants.Length);
            return new WeatherDailySelection(meaning, variants[index]);
        }

        internal static string[] GetVariantsForTest(WeatherMeaning meaning)
        {
            return (string[])GetVariants(meaning).Clone();
        }

        private static string[] GetVariants(WeatherMeaning meaning)
        {
            switch (meaning)
            {
                case WeatherMeaning.Snow: return Snow;
                case WeatherMeaning.RainAndWind: return RainAndWind;
                case WeatherMeaning.RainAndCooling: return RainAndCooling;
                case WeatherMeaning.HeavyRain: return HeavyRain;
                case WeatherMeaning.PersistentRain: return PersistentRain;
                case WeatherMeaning.Windy: return Windy;
                case WeatherMeaning.Cooling: return Cooling;
                case WeatherMeaning.Warming: return Warming;
                case WeatherMeaning.RainLater: return RainLater;
                case WeatherMeaning.Hot: return Hot;
                case WeatherMeaning.Cold: return Cold;
                case WeatherMeaning.LargeTemperatureRange:
                    return LargeTemperatureRange;
                default: throw new ArgumentOutOfRangeException(nameof(meaning));
            }
        }

        private static int StableIndex(DateTime date, string locationKey,
            WeatherMeaning meaning, int count)
        {
            string seed = date.ToString("yyyyMMdd") + "|" +
                (locationKey ?? String.Empty) + "|" + meaning;
            unchecked
            {
                uint hash = 2166136261;
                foreach (char character in seed)
                {
                    hash ^= character;
                    hash *= 16777619;
                }
                return (int)(hash % (uint)count);
            }
        }
    }
}
