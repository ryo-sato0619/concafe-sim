using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    public static class FatigueRules
    {
        /// <summary>
        /// 出勤 1 日で溜まる疲労（設計書 4.5）。
        ///
        /// 接客の負荷は「人数」ではなく「延べ時間」で数える。長く座らせるほど疲れる。
        /// トークを伸ばすと滞在時間が伸びて売上が上がるが、同じだけ寿命を削る。
        /// この二面性が「育てるほど失う」を数式に落としたものである。
        ///
        /// 信頼度は蓄積速度にだけ掛かる。疲労限界値には一切触れない（不変条件 1）。
        /// 信頼を積んだキャストは「限界が上がる」のではなく「限界に届くのが遅くなる」。
        /// </summary>
        public static double DailyGain(
            Cast cast, ServiceResult service, BalanceData balance, Weather weather)
        {
            var f = balance.Fatigue;
            double sober = cast.Sober(balance);

            double alcohol = (service.AlcoholDrinks * f.PerDrink
                            + service.BottleFatigueTiers * f.PerBottleTier) * sober;

            double raw = f.Base + service.GuestHours * f.PerGuest + alcohol;

            double stormMul = WeatherGenerator.Coefficients(balance, weather).FatigueMul;

            return raw * stormMul * cast.TrustRelief(balance);
        }

        /// <summary>休業日の回復。0 を下回らない。</summary>
        public static double DailyRecovery(BalanceData balance) => balance.Fatigue.RestRecover;

        public static void ApplyWork(
            Cast cast, ServiceResult service, BalanceData balance, Weather weather)
        {
            cast.Fatigue += DailyGain(cast, service, balance, weather);
        }

        public static void ApplyRest(Cast cast, BalanceData balance)
        {
            cast.Fatigue -= DailyRecovery(balance);
            if (cast.Fatigue < 0) cast.Fatigue = 0;
        }

        public static bool IsOverLimit(Cast cast, BalanceData balance) =>
            cast.Fatigue > cast.FatigueLimit(balance);

        /// <summary>予兆を出すべきか。限界値の warnRatio を超えたら。</summary>
        public static bool ShouldWarn(Cast cast, BalanceData balance) =>
            cast.Fatigue >= cast.FatigueLimit(balance) * balance.Departure.WarnRatio;
    }

    public static class GrowthRules
    {
        /// <summary>
        /// 成長量（設計書 4.6）。上限に近づくほど鈍り、Cap を超えることは構造的に無い。
        /// </summary>
        public static double Gain(Cast cast, StatKind kind, double baseGain)
        {
            double cap = cast.Cap[kind];
            if (cap <= 0) return 0;

            double room = 1.0 - cast.Current[kind] / cap;
            if (room <= 0) return 0;

            return baseGain * cast.GrowthRate[kind] * room;
        }

        /// <summary>1 パラメータを伸ばす。Cap で必ず止まる。</summary>
        public static void Apply(Cast cast, StatKind kind, double baseGain)
        {
            double next = cast.Current[kind] + Gain(cast, kind, baseGain);
            double cap = cast.Cap[kind];
            cast.Current[kind] = next > cap ? cap : next;
        }

        /// <summary>
        /// 出勤による自然成長（設計書 14.4）。
        ///
        /// 出勤時間に比例させる。長く入れば売上も経験も増えるが、疲労も増える。
        /// 基準は平日の全営業時間で、フル出勤なら dailyWorkGain のままになる。
        ///
        /// トークと知名度が主。モラルはその半分の速さで伸びる
        /// （真面目に働き続ければ真面目さも育つが、遅い）。
        ///
        /// モラルを出勤でも伸ばすのは、そうしないと**卒業が成立しないため**である。
        /// 特訓だけに頼ると、AP が有限なぶんモラルまで手が回らず、
        /// 10 年で一度も OB が出ないことが通しシミュレーションで判明した（設計書 14.9）。
        ///
        /// 集客が接客では伸びないのは意図的である。固定客を増やす手段を特訓に限ることで、
        /// 店長 AP を割く価値が保たれる。
        /// </summary>
        public static void ApplyWorkDay(Cast cast, int shiftHours, BalanceData balance)
        {
            if (shiftHours <= 0) return;

            double g = balance.Manager.DailyWorkGain
                     * shiftHours / balance.Business.BaselineOpenHours;

            Apply(cast, StatKind.Talk, g);
            Apply(cast, StatKind.Fame, g);
            Apply(cast, StatKind.Morale, g * balance.Manager.MoraleWorkGainRatio);
        }
    }
}
