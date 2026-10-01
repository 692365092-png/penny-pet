using System;

namespace PennyPet
{
    internal static class WeatherWordingCatalog
    {
        private static readonly string[] Snow =
        {
            "外面可能会下雪，走路小心滑",
            "可能会下雪，围巾手套记得带",
            "今天可能会下雪，穿双防滑的鞋"
        };

        private static readonly string[] RainAndWind =
        {
            "今天又刮风又下雨，出门把伞拿稳",
            "外面又下雨又刮风，出门别把时间卡太紧",
            "又下雨又刮风，外套选挡风的"
        };

        private static readonly string[] RainAndCooling =
        {
            "下雨又转凉，外套记得带",
            "下着雨会更冷，出门多穿一层",
            "下雨也会变冷，鞋袜尽量别湿"
        };

        private static readonly string[] HeavyRain =
        {
            "今天雨可能不小，低洼路段尽量绕开",
            "今天雨可能会下得比较大，出门早点",
            "外面的雨可能比较大，穿双不容易湿的鞋"
        };

        private static readonly string[] PersistentRain =
        {
            "今天可能断断续续一直有雨，伞记得带着",
            "雨可能断断续续，要晒东西的话先缓一缓",
            "今天可能下很久的雨，出门记得带伞"
        };

        private static readonly string[] Windy =
        {
            "今天风比较大，出门注意一下",
            "外面风有点大，轻东西记得收好",
            "今天风挺大的，骑车慢一点",
            "外面风不小，帽子记得戴稳",
            "外面风挺大的，风大的地方走慢一点",
            "今天风挺大的，窗边的小东西记得收好"
        };

        private static readonly string[] Cooling =
        {
            "今天会比昨天凉不少，外套记得带",
            "今天凉了不少，早晚多穿一层",
            "今天一下子凉了不少，薄外套先带着",
            "今天会凉不少，开窗通风别太久",
            "比昨天冷不少，出门多穿一点",
            "今天冷了不少，手脚容易冷就加一层"
        };

        private static readonly string[] Warming =
        {
            "会比昨天暖不少，穿方便加减的衣服就行",
            "今天暖和多了，中午觉得热就少穿一层",
            "今天会暖和一些，觉得热就少穿点"
        };

        private static readonly string[] RainLater =
        {
            "晚些时候可能有雨，伞先放包里",
            "后半天更容易下雨，出门记得带伞",
            "晚些时候更容易下雨，回家别把时间卡太紧",
            "晚点可能会下雨，包里放把伞",
            "晚些时候可能下雨，晒的东西早点收",
            "下午到晚上可能有雨，带把轻便伞"
        };

        private static readonly string[] Hot =
        {
            "今天比较热，出门记得带水",
            "今天会很热，下午别在太阳下待太久",
            "今天会很热，运动别太勉强",
            "外面会很热，衣服选透气一点",
            "今天会很热，水记得带",
            "外面挺热的，别在车里待太久"
        };

        private static readonly string[] Cold =
        {
            "今天会很冷，手脚记得保暖",
            "外面会比较冷，多穿点别冻着",
            "外面挺冻的，别在风里站太久"
        };

        private static readonly string[] LargeTemperatureRange =
        {
            "今天早晚温差大，外套先别收",
            "一天里的温差不小，分层穿会方便一点",
            "中午和早晚差得多，穿方便加减的衣服就行"
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
