using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    public static class FatigueRules
    {
        /// <summary>
        /// 出勤 1 日で溜まる疲労（設計書 4.5）。
        ///
        /// 信頼度は蓄積速度にだけ掛かる。疲労限界値には一切触れない（不変条件 1）。
        /// 信頼を積んだキャストは「限界が上がる」のではなく「限界に届くのが遅くなる」。
        /// </summary>
        public static double DailyGain(
            Cast cast, int guests, ServiceResult service, BalanceData balance, Weather weather)
        {
            var f = balance.Fatigue;
            double sober = cast.Sober(balance);

            double alcohol = (service.AlcoholDrinks * f.PerDrink
                            + service.BottleFatigueTiers * f.PerBottleTier) * sober;

            double raw = f.Base + guests * f.PerGuest + alcohol;

            double stormMul = WeatherGenerator.Coefficients(balance, weather).FatigueMul;

            return raw * stormMul * cast.TrustRelief(balance);
        }

        /// <summary>休業日の回復。0 を下回らない。</summary>
        public static double DailyRecovery(BalanceData balance) => balance.Fatigue.RestRecover;

        public static void ApplyWork(
            Cast cast, int guests, ServiceResult service, BalanceData balance, Weather weather)
        {
            cast.Fatigue += DailyGain(cast, guests, service, balance, weather);
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
        /// 出勤による自然成長。トークと知名度だけが伸びる（設計書 14.4）。
        ///
        /// 集客が接客では伸びないのは意図的である。固定客を増やす手段を特訓に限ることで、
        /// 店長 AP を割く価値が保たれる。
        /// </summary>
        public static void ApplyWorkDay(Cast cast, BalanceData balance)
        {
            double g = balance.Manager.DailyWorkGain;
            Apply(cast, StatKind.Talk, g);
            Apply(cast, StatKind.Fame, g);
        }
    }
}
