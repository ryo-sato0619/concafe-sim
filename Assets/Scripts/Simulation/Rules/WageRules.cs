using System;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    public static class WageRules
    {
        /// <summary>
        /// 日給（設計書 14.3）。
        ///
        /// 未成年の分岐を持たない。酒が売れず歩合が伸びないため、
        /// 年齢による給料テーブルを作らなくても自動的に安くなる。
        /// </summary>
        public static long DailyYen(Cast cast, BalanceData balance)
        {
            double avg = (cast.Current.Talk + cast.Current.Regulars + cast.Current.Fame) / 3.0;
            double yen = balance.Wage.DailyBaseYen + avg * balance.Wage.DailyStatCoefYen;

            // 不変条件 8: 金額は整数の円。
            return (long)Math.Round(yen, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 週給 = 日給 × 出勤日数 ＋ 歩合。
        ///
        /// 日給の差を小さく、歩合の差を大きくしている。エースの人件費が売上に
        /// 連動して跳ね上がるため、「育てれば育てるほど楽になる」という
        /// 単調な構造にならない。
        /// </summary>
        public static long WeeklyYen(Cast cast, int workDays, long weekSalesYen, BalanceData balance)
        {
            long daily = DailyYen(cast, balance) * workDays;
            long commission = (long)Math.Round(
                weekSalesYen * balance.Wage.CommissionRate, MidpointRounding.AwayFromZero);
            return daily + commission;
        }
    }
}
