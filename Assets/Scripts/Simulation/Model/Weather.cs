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
    }
}
