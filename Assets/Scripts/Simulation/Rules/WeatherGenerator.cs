using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    public static class WeatherGenerator
    {
        /// <summary>今週 7 日分の実際の天候を決める。季節で重みが変わる。</summary>
        public static Weather[] GenerateWeek(
            GameRandom rng, BalanceData balance, Season season)
        {
            var weights = BuildWeights(balance, season);
            var days = new Weather[Shift.DaysPerWeek];

            for (int d = 0; d < days.Length; d++)
            {
                int index = rng.WeightedPick(weights);
                days[d] = (Weather)(index < 0 ? 0 : index);
            }
            return days;
        }

        /// <summary>
        /// 週頭に見せる予報。先の日ほど外れる（設計書 5.2）。
        ///
        /// 予報を確定情報にしないのは、シフトと発注を「賭け」にするためである。
        /// 台風の予報を見て臨時休業を決めたのに晴れた、という展開が起きる。
        /// </summary>
        public static Weather[] MakeForecast(
            GameRandom rng, BalanceData balance, Season season, Weather[] actual)
        {
            var weights = BuildWeights(balance, season);
            var accuracy = balance.WeatherGen.ForecastAccuracy;
            var forecast = new Weather[actual.Length];

            for (int d = 0; d < actual.Length; d++)
            {
                double hit = d < accuracy.Count ? accuracy[d] : accuracy[accuracy.Count - 1];
                if (rng.Chance(hit))
                {
                    forecast[d] = actual[d];
                    continue;
                }

                // 外れる場合は、その季節にありそうな別の天候を出す。
                // 真夏に雪の予報が出ると外れ方が不自然になるため、同じ重みから引く。
                int index = rng.WeightedPick(weights);
                forecast[d] = (Weather)(index < 0 ? 0 : index);
            }
            return forecast;
        }

        static double[] BuildWeights(BalanceData balance, Season season)
        {
            var table = balance.WeatherGen.Seasons[SeasonNames.Of(season)];
            var all = WeatherNames.All;
            var weights = new double[all.Count];

            for (int i = 0; i < all.Count; i++)
                weights[i] = table.TryGetValue(all[i], out double w) ? w : 0;

            return weights;
        }

        /// <summary>天候ごとの係数。</summary>
        public static WeatherBalance Coefficients(BalanceData balance, Weather weather) =>
            balance.Weather[WeatherNames.Of(weather)];
    }
}
