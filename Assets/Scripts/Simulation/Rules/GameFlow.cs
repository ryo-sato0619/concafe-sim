using System.Collections.Generic;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    /// <summary>
    /// 1 週間を回す。
    ///
    /// 呼ぶ順は
    ///   1. BeginWeek     加齢・AP 付与・天候確定・予報生成
    ///   2. （プレイヤー操作: ManagerActions の各メソッド）
    ///   3. RunWeek       営業 7 日 → 決算 → 離脱判定
    /// </summary>
    public static class GameFlow
    {
        public static void BeginWeek(GameState state)
        {
            if (state.IsOver) return;

            var balance = state.Balance;
            var cal = state.Calendar;

            // 誕生週を迎えたキャストが加齢する。全員一斉ではない（設計書 2）。
            foreach (var c in state.Shop.Roster)
                if (cal.IsBirthdayWeek(state.Week, c.BirthWeekOfYear)) c.Age++;

            // 不変条件 7: 店舗規模に関係なく同じ量しか付与しない。
            state.Shop.ManagerAp = balance.Manager.ApPerWeek;

            state.WeekPurchaseYen = 0;

            var season = SeasonNames.Of(cal.MonthOf(state.Week));
            state.Weather = WeatherGenerator.GenerateWeek(state.Rng.Weather, balance, season);
            state.Forecast = WeatherGenerator.MakeForecast(
                state.Rng.Weather, balance, season, state.Weather);
        }

        public static WeekResult RunWeek(GameState state, Shift shift)
        {
            var result = new WeekResult
            {
                Week = state.Week,
                CashBeforeYen = state.Shop.CashYen,
                PurchaseYen = state.WeekPurchaseYen,
            };

            if (state.IsOver)
            {
                result.GameOver = state.GameOver;
                result.CashAfterYen = state.Shop.CashYen;
                return result;
            }

            var stats = BuildStats(state, shift);

            RunBusinessWeek(state, shift, stats, result);
            Settle(state, shift, stats, result);

            // 決算で資金が尽きたらそこで終わる。離脱判定まで進めない。
            if (state.Shop.CashYen < 0)
            {
                state.GameOver = GameOverReason.CashShortage;
                result.GameOver = state.GameOver;
                result.CashAfterYen = state.Shop.CashYen;
                state.Week = state.Week.Next;
                return result;
            }

            ResolveDepartures(state, result);
            CollectWarnings(state, result);

            result.CashAfterYen = state.Shop.CashYen;
            result.GameOver = state.GameOver;

            state.Week = state.Week.Next;
            return result;
        }

        static Dictionary<string, CastWeekStats> BuildStats(GameState state, Shift shift)
        {
            var stats = new Dictionary<string, CastWeekStats>();
            foreach (var c in state.Shop.Roster)
            {
                stats[c.Id] = new CastWeekStats
                {
                    CastId = c.Id,
                    StageName = c.StageName,
                    WorkDays = shift.WorkDays(c.Id),
                    WorkHours = shift.WorkHours(c.Id),
                };
            }
            return stats;
        }

        static void RunBusinessWeek(
            GameState state, Shift shift,
            Dictionary<string, CastWeekStats> stats, WeekResult result)
        {
            var balance = state.Balance;
            var stock = state.Shop.Stock;
            var rng = state.Rng.Orders;

            for (int day = 0; day < Shift.DaysPerWeek; day++)
            {
                var weather = state.Weather[day];
                var dayResult = new DayResult
                {
                    Weather = weather,
                    OpenHours = balance.Business.OpenHours(day),
                    IsHoliday = balance.Business.IsHoliday(day),
                };

                // ロスターの順に走るので、同じ種なら必ず同じ結果になる。
                var working = new List<VisitorRules.OnDuty>();
                foreach (var c in state.Shop.Roster)
                {
                    var slot = shift.Get(c.Id, day);
                    if (slot.IsWorking)
                        working.Add(new VisitorRules.OnDuty { Cast = c, Slot = slot });
                }

                if (working.Count == 0)
                {
                    dayResult.Closed = true;
                    foreach (var c in state.Shop.Roster) FatigueRules.ApplyRest(c, balance);
                    result.Days[day] = dayResult;
                    continue;
                }

                var assignments = VisitorRules.Assign(working, balance, weather, rng);

                foreach (var a in assignments)
                {
                    var service = OrderRules.Serve(
                        a.Cast, a.Total, a.Slot.Hours, balance, stock, rng);

                    dayResult.Visitors += a.Total;
                    dayResult.GuestHours += service.GuestHours;
                    dayResult.RevenueYen += service.SalesYen;
                    dayResult.LostOrders += service.LostOrders;
                    dayResult.LostSalesYen += service.LostSalesYen;

                    a.Cast.LifetimeSalesYen += service.SalesYen;

                    var s = stats[a.Cast.Id];
                    s.Guests += a.Total;
                    s.GuestHours += service.GuestHours;
                    s.SalesYen += service.SalesYen;
                    s.BackableSalesYen += service.BackableSalesYen;
                    s.BottlesSold += service.BottlesSold;
                    s.AlcoholDrinksServed += service.AlcoholDrinks;

                    double before = a.Cast.Fatigue;
                    FatigueRules.ApplyWork(a.Cast, service, balance, weather);
                    s.FatigueGained += a.Cast.Fatigue - before;

                    GrowthRules.ApplyWorkDay(a.Cast, a.Slot.Hours, balance);
                }

                // 休業日のキャストは回復する。
                foreach (var c in state.Shop.Roster)
                    if (!shift.IsWorking(c.Id, day)) FatigueRules.ApplyRest(c, balance);

                result.Days[day] = dayResult;
                result.RevenueYen += dayResult.RevenueYen;
                result.TotalVisitors += dayResult.Visitors;
                result.LostOrders += dayResult.LostOrders;
                result.LostSalesYen += dayResult.LostSalesYen;
            }
        }

        static void Settle(
            GameState state, Shift shift,
            Dictionary<string, CastWeekStats> stats, WeekResult result)
        {
            var balance = state.Balance;
            int week = state.Week.AbsoluteWeek;

            foreach (var c in state.Shop.Roster)
            {
                var s = stats[c.Id];
                s.HourlyYen = WageRules.HourlyYen(c, balance, week);
                s.BackYen = WageRules.BackYen(s.BackableSalesYen, balance);
                s.WageYen = WageRules.WeeklyYen(c, shift, s.BackableSalesYen, balance, week);
                result.WageYen += s.WageYen;
            }

            result.FixedCostYen = state.Shop.WeeklyFixedCostYen(balance);

            state.Shop.CashYen += result.RevenueYen;
            state.Shop.CashYen -= result.WageYen;
            state.Shop.CashYen -= result.FixedCostYen;

            foreach (var c in state.Shop.Roster) result.CastStats.Add(stats[c.Id]);
        }

        static void ResolveDepartures(GameState state, WeekResult result)
        {
            var balance = state.Balance;

            // ロスターを走りながら外すため、対象を先に集める。
            var leaving = new List<Cast>();
            foreach (var c in state.Shop.Roster)
                if (FatigueRules.IsOverLimit(c, balance)) leaving.Add(c);

            foreach (var c in leaving)
            {
                var record = DepartureRules.Depart(state.Shop, c, balance, state.Eggs);
                result.Departures.Add(record);

                if (record.Override == DepartureOverride.BurnDown)
                {
                    // 店が焼けた。この時点で終わり。残りの離脱は処理しない。
                    state.GameOver = GameOverReason.Arson;
                    break;
                }
            }
        }

        static void CollectWarnings(GameState state, WeekResult result)
        {
            if (state.IsOver) return;

            foreach (var c in state.Shop.Roster)
            {
                if (!DepartureRules.ShouldWarn(c, state.Balance)) continue;

                result.Warnings.Add(new DepartureWarning
                {
                    CastId = c.Id,
                    StageName = c.StageName,
                    Fatigue = c.Fatigue,
                    Limit = c.FatigueLimit(state.Balance),
                });
            }
        }
    }
}
