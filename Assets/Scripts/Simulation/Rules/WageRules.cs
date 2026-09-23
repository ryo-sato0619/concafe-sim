using System.Collections.Generic;
using System;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    public static class WageRules
    {
        /// <summary>勤続年数。切り捨て。</summary>
        public static int SeniorityYears(Cast cast, BalanceData balance, int currentWeek)
        {
            int weeks = currentWeek - cast.HiredWeek;
            if (weeks < 0) weeks = 0;
            return weeks / balance.Time.WeeksPerYear;
        }

        /// <summary>
        /// 時給（設計書 14.3）。
        ///
        ///   時給 = 基本 ＋ 能力平均 × 係数 ＋ 勤続年数 × 昇給
        ///
        /// 未成年の分岐を持たない。酒が売れずバックが伸びないため、
        /// 年齢による給料テーブルを作らなくても自動的に安くなる。
        /// </summary>
        public static long HourlyYen(Cast cast, BalanceData balance, int currentWeek)
        {
            var w = balance.Wage;
            double avg = (cast.Current.Talk + cast.Current.Regulars + cast.Current.Fame) / 3.0;

            double yen = w.BaseHourlyYen
                       + avg * w.HourlyStatCoefYen
                       + SeniorityYears(cast, balance, currentWeek) * w.SeniorityPerYearYen;

            // 不変条件 8: 金額は整数の円。
            return (long)Math.Round(yen, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// そのシフトに対する日給。
        /// 長く入れば高く、深夜帯（22 時以降）に掛かった分には割増が乗る。
        /// </summary>
        public static long DailyYen(Cast cast, ShiftSlot slot, BalanceData balance, int currentWeek)
        {
            if (!slot.IsWorking) return 0;

            double yen = HourlyYen(cast, balance, currentWeek)
                       * balance.Business.PaidHours(slot.StartHour, slot.EndHour);
            return (long)Math.Round(yen, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// バック。チェキ・キャストドリンク・ボトルの売上にだけ掛かる。
        /// チャージと客のドリンクは店の売上で、対象外。
        /// </summary>
        public static long BackYen(long backableSalesYen, BalanceData balance) =>
            (long)Math.Round(backableSalesYen * balance.Wage.BackRate, MidpointRounding.AwayFromZero);

        /// <summary>
        /// 週給 = シフトごとの日給の合計 ＋ バック。
        ///
        /// 「日給 × 出勤日数」では計算できない。何時から何時まで入れたかで
        /// 時間数が変わり、深夜帯に掛かれば割増も付くため。
        ///
        /// バックの対象をチャージとドリンクから外しているのが要点である。
        /// トークを伸ばすとチェキ・キャストドリンク・ボトルの比率が上がるため、
        /// **バック対象の割合そのものが増えていく。**
        /// エースの人件費は売上に連動して跳ね上がり、
        /// 「育てれば育てるほど楽になる」という単調な構造にならない。
        /// </summary>
        public static long WeeklyYen(
            Cast cast, Shift shift, IReadOnlyDictionary<string, HashSet<int>> absent,
            long backableSalesYen, BalanceData balance, int currentWeek)
        {
            long hourly = HourlyYen(cast, balance, currentWeek);

            absent.TryGetValue(cast.Id, out var absentDays);

            double paidHours = 0;
            for (int d = 0; d < Shift.DaysPerWeek; d++)
            {
                var slot = shift.Get(cast.Id, d);
                if (!slot.IsWorking) continue;

                // 欠勤した日は給料が出ない。
                if (absentDays != null && absentDays.Contains(d)) continue;

                paidHours += balance.Business.PaidHours(slot.StartHour, slot.EndHour);
            }

            long basePay = (long)Math.Round(hourly * paidHours, MidpointRounding.AwayFromZero);
            return basePay + BackYen(backableSalesYen, balance);
        }
    }
}
