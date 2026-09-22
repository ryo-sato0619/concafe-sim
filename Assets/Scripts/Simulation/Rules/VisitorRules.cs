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
        /// <summary>そのキャストのシフト。滞在時間の上限に使う。</summary>
        public ShiftSlot Slot;
        /// <summary>固定客。そのキャストに紐づいており、離脱で失われる。</summary>
        public int Regulars;
        /// <summary>知名度で釣った新規客。</summary>
        public int Newcomers;

        public int Total => Regulars + Newcomers;
    }

    public static class VisitorRules
    {
        /// <summary>
        /// ルックスの乗数。1.0 〜 1.0 + looksBonusMax。
        /// 衣装デザイナー OB がいると、素のルックスに加算される（要件 6.1）。
        /// </summary>
        public static double LooksMultiplier(
            Cast cast, BalanceData balance, AlumniEffects alumni = null)
        {
            double max = balance.CastGen.StatMax;
            if (max <= 0) return 1.0;

            double looks = cast.Current.Looks + (alumni?.LooksBonus ?? 0);

            double t = looks / max;
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
        /// <summary>
        /// 出勤キャストとそのシフト。
        /// </summary>
        public struct OnDuty
        {
            public Cast Cast;
            public ShiftSlot Slot;
        }

        public static List<GuestAssignment> Assign(
            IReadOnlyList<OnDuty> working, BalanceData balance, Weather weather,
            AlumniEffects alumni, GameRandom rng)
        {
            var result = new List<GuestAssignment>(working.Count);
            if (working.Count == 0) return result;

            var w = WeatherGenerator.Coefficients(balance, weather);

            // Web マーケター OB は悪天候時の知名度ペナルティを軽減する。
            // stormFactor（台風で客が来ない分）には効かない（要件 6.1）。
            double fameFactor = w.FameFactor;
            double relief = alumni?.WeatherRelief ?? 0;
            if (relief > 0 && fameFactor < 1.0)
                fameFactor += (1.0 - fameFactor) * relief;

            // 来客は「出勤した時間数」に比例する。平日の全時間（開店〜閉店）を
            // 1.0 として比で効かせているので、平日フル出勤の来客数は
            // シフトを時刻で持つ前と変わらない（T-44 を維持する）。
            double baseline = balance.Business.BaselineOpenHours;

            // 固定客。天候のうち stormFactor だけが効く。
            foreach (var d in working)
            {
                double hoursFactor = d.Slot.Hours / baseline;
                int regulars = FloorCount(
                    d.Cast.Current.Regulars * balance.Visitors.RegularBase
                    * hoursFactor * w.StormFactor);

                result.Add(new GuestAssignment
                {
                    Cast = d.Cast, Slot = d.Slot, Regulars = regulars, Newcomers = 0,
                });
            }

            // 新規客。知名度 × ルックス倍率に fameFactor と stormFactor の両方が効く。
            var fameWeights = new double[working.Count];
            double fameRaw = 0;
            for (int i = 0; i < working.Count; i++)
            {
                double v = working[i].Cast.Current.Fame
                         * LooksMultiplier(working[i].Cast, balance, alumni)
                         * (working[i].Slot.Hours / baseline);
                fameWeights[i] = v;
                fameRaw += v;
            }

            int newcomers = FloorCount(
                fameRaw * balance.Visitors.FameBase * fameFactor * w.StormFactor);

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

        /// <summary>
        /// 客数への切り下げ。ごく小さな余裕を足してから切り下げる。
        ///
        /// 係数の掛け算は浮動小数なので、ちょうど整数になるはずの値が
        /// わずかに下回ることがある。たとえば休日の固定客は
        /// 20 × 0.15 × (8/6) = 4.0 だが、実際には 3.9999999999999996 になり、
        /// 素直に切り下げると 3 人になってしまう。
        /// </summary>
        static int FloorCount(double value)
        {
            if (value <= 0) return 0;
            return (int)(value + 1e-9);
        }

        public static int TotalVisitors(List<GuestAssignment> assignments)
        {
            int n = 0;
            foreach (var a in assignments) n += a.Total;
            return n;
        }
    }
}
