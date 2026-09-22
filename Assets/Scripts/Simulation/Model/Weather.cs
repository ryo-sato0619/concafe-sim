using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    public enum Weather
    {
        Sunny,
        Cloudy,
        Rain,
        Storm,
        Snow,
    }

    /// <summary>
    /// balance.json の weather セクションのキー。
    /// enum と文字列の対応を 1 か所に閉じ込める。
    /// </summary>
    public static class WeatherNames
    {
        public const string Sunny = "sunny";
        public const string Cloudy = "cloudy";
        public const string Rain = "rain";
        public const string Storm = "storm";
        public const string Snow = "snow";

        public static readonly IReadOnlyList<string> All =
            new[] { Sunny, Cloudy, Rain, Storm, Snow };

        public static string Of(Weather w)
        {
            switch (w)
            {
                case Weather.Sunny: return Sunny;
                case Weather.Cloudy: return Cloudy;
                case Weather.Rain: return Rain;
                case Weather.Storm: return Storm;
                case Weather.Snow: return Snow;
                default: return Sunny;
            }
        }

        /// <summary>
        /// 客がほぼ来ず、疲労が倍増する天候。臨時休業を検討する対象。
        /// </summary>
        public static bool IsSevere(Weather w) => w == Weather.Storm || w == Weather.Snow;

        public static bool TryParse(string name, out Weather w)
        {
            switch (name)
            {
                case Sunny: w = Weather.Sunny; return true;
                case Cloudy: w = Weather.Cloudy; return true;
                case Rain: w = Weather.Rain; return true;
                case Storm: w = Weather.Storm; return true;
                case Snow: w = Weather.Snow; return true;
                default: w = Weather.Sunny; return false;
            }
        }
    }

    public enum Season { Spring, Summer, Autumn, Winter }

    public static class SeasonNames
    {
        public const string Spring = "spring";
        public const string Summer = "summer";
        public const string Autumn = "autumn";
        public const string Winter = "winter";

        public static readonly IReadOnlyList<string> All =
            new[] { Spring, Summer, Autumn, Winter };

        public static string Of(Season s)
        {
            switch (s)
            {
                case Season.Spring: return Spring;
                case Season.Summer: return Summer;
                case Season.Autumn: return Autumn;
                default: return Winter;
            }
        }

        /// <summary>月は 0 起点（0 = 1 月）。12・1・2 月を冬とする。</summary>
        public static Season Of(int month)
        {
            switch (month)
            {
                case 2: case 3: case 4: return Season.Spring;   // 3〜5 月
                case 5: case 6: case 7: return Season.Summer;   // 6〜8 月
                case 8: case 9: case 10: return Season.Autumn;  // 9〜11 月
                default: return Season.Winter;                  // 12・1・2 月
            }
        }
    }
}
