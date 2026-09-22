using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    /// <summary>その日、どのキャストに何人の客が付いたか。</summary>
    public struct GuestAssignment
    {
        public Cast Cast;
        /// <summary>固定客。そのキャストに紐づいており、離脱で失われる。</summary>
        public int Regulars;
        /// <summary>知名度で釣った新規客。</summary>
        public int Newcomers;

        public int Total => Regulars + Newcomers;
    }

    public static class VisitorRules
    {
        /// <summary>ルックスの乗数。1.0 〜 1.0 + looksBonusMax。</summary>
        public static double LooksMultiplier(Cast cast, BalanceData balance)
        {
            double max = balance.CastGen.StatMax;
            if (max <= 0) return 1.0;
            double t = cast.Current.Looks / max;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return 1.0 + t * balance.Visitors.LooksBonusMax;
        }

        /// <summary>
        /// その日の来客を出勤キャストに割り振る。
        ///
        /// 固定客と新規客を分けて数えるのは、固定客の指名先が固定されているためである
        /// （設計書 4.2）。ここが曖昧だと、離脱で何を失うのかが決まらない。
        ///
        /// 天候の係数が 2 つに分かれている点に注意（設計書 4.1）。
        /// 雨は fameFactor だけを下げるので固定客は減らない。
        /// 台風・大雪は stormFactor が固定客ごと潰す。
        /// </summary>
        public static List<GuestAssignment> Assign(
            IReadOnlyList<Cast> working, BalanceData balance, Weather weather, GameRandom rng)
        {
            var result = new List<GuestAssignment>(working.Count);
            if (working.Count == 0) return result;

            var w = WeatherGenerator.Coefficients(balance, weather);
            double regularBase = balance.Visitors.RegularBase;
            double fameBase = balance.Visitors.FameBase;

            // 固定客。天候のうち stormFactor だけが効く。
            foreach (var c in working)
            {
                int regulars = (int)(c.Current.Regulars * regularBase * w.StormFactor);
                result.Add(new GuestAssignment { Cast = c, Regulars = regulars, Newcomers = 0 });
            }

            // 新規客。知名度 × ルックス倍率に fameFactor と stormFactor の両方が効く。
            var fameWeights = new double[working.Count];
            double fameRaw = 0;
            for (int i = 0; i < working.Count; i++)
            {
                double v = working[i].Current.Fame * LooksMultiplier(working[i], balance);
                fameWeights[i] = v;
                fameRaw += v;
            }

            int newcomers = (int)(fameRaw * fameBase * w.FameFactor * w.StormFactor);

            for (int n = 0; n < newcomers; n++)
            {
                int pick = rng.WeightedPick(fameWeights);
                if (pick < 0) break;

                var a = result[pick];
                a.Newcomers++;
                result[pick] = a;
            }

            return result;
        }

        public static int TotalVisitors(List<GuestAssignment> assignments)
        {
            int n = 0;
            foreach (var a in assignments) n += a.Total;
            return n;
        }
    }
}
