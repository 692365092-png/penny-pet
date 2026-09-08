using System;

namespace PennyPet
{
    internal static class WeatherWordingCatalog
    {
        private static readonly string[] Snow =
        {
            "外面可能下雪，走路留意湿滑",
            "有下雪迹象，围巾手套记得带",
            "可能会见到雪，鞋底尽量选防滑的"
        };

        private static readonly string[] RainAndWind =
        {
            "风雨会一起出现，出门把伞拿稳",
            "外面风雨一起，行程留一点余量",
            "又下雨又刮风，外套选挡风的"
        };

        private static readonly string[] RainAndCooling =
        {
            "下雨又转凉，外套记得带",
            "雨天体感更凉，出门多穿一层",
            "有雨也会降温，鞋袜尽量别湿"
        };

        private static readonly string[] HeavyRain =
        {
            "今天雨可能不小，低洼路段尽量绕开",
            "雨量偏多，赶路多留一点时间",
            "外面的雨会比较大，鞋子尽量选耐湿的"
        };

        private static readonly string[] PersistentRain =
        {
            "不少时段会有雨，伞先一直带着",
            "雨可能断断续续，晾晒安排可以缓缓",
            "这场雨可能待很久，出门前记得带伞"
        };

        private static readonly string[] Windy =
        {
            "今天风比较大，出门注意一下",
            "外面风有点大，轻东西记得收好",
            "风会比较明显，骑行慢一点",
            "外面风劲不小，帽子记得戴稳",
            "风有点强，空旷处别走太急",
            "风会持续一阵，窗边小物件收稳"
        };

        private static readonly string[] Cooling =
        {
            "今天会比昨天凉不少，外套记得带",
            "气温明显回落，早晚多穿一层",
            "降温比较直接，薄外套先带着",
            "体感会凉一截，开窗通风别太久",
            "比昨天冷不少，出门别少带衣服",
            "温度明显下降，手脚容易冷就加一层"
        };

        private static readonly string[] Warming =
        {
            "会比昨天暖不少，衣服方便穿脱就好",
            "今天明显回暖，中午可以少穿一层",
            "温度正在往上走，按体感增减衣服"
        };

        private static readonly string[] RainLater =
        {
            "晚些时候可能有雨，伞先放包里",
            "雨更可能后半天来，出门记得带伞",
            "白天越往后越容易下雨，回程留点余量",
            "现在没下也别大意，包里放把伞",
            "今天的雨来得偏晚，晾晒早点收",
            "午后到晚上可能有雨，带把轻便伞"
        };

        private static readonly string[] Hot =
        {
            "今天比较热，出门记得带水",
            "体感会很热，午后少在太阳下久留",
            "高温体感明显，运动别太勉强",
            "外面容易闷热，衣服选透气一点",
            "热意会持续一阵，水杯记得带",
            "气温偏高，车里不要久留"
        };

        private static readonly string[] Cold =
        {
            "今天体感接近冰点，手脚注意保暖",
            "外面会比较冷，保暖层记得穿够",
            "容易觉得冻，别在风里久站"
        };

        private static readonly string[] LargeTemperatureRange =
        {
            "今天早晚温差大，外套先别收",
            "一天里的温差不小，叠穿更方便",
            "中午和早晚差得多，衣服方便增减就好"
        };

        internal static WeatherDailySelection Select(WeatherMeaning meaning,
            DateTime localDate, string locationStableKey)
        {
            string[] variants = GetVariants(meaning);
            int index = StableIndex(localDate.Date, locationStableKey,
                meaning, variants.Length);
            return new WeatherDailySelection(meaning,
                "WEATHER-" + meaning + "-" + index, variants[index]);
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
