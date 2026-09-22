using System.Collections.Generic;
using Concafe.Simulation.Rng;
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
        /// <summary>
        /// 新規開始。前任者から引き継ぐキャストを在籍させた状態で始める。
        ///
        /// 空の店から始めると、売上が無いまま家賃と店長報酬だけが出ていき、
        /// 求人・採用・育成が回り出す前に資金が尽きる。
        /// 企画書の「赤字寸前のコンカフェ店長となり」は、
        /// すでに動いている店を引き継ぐという意味である。
        /// </summary>
        public static GameState NewGame(
            Concafe.Simulation.Balance.BalanceData balance,
            Concafe.Simulation.Balance.EasterEggSet eggs,
            Concafe.Simulation.Balance.CareerSet careers, ulong seed)
        {
            var state = new GameState(balance, eggs, careers, seed);

            double bonus = balance.Shop.InitialCastStatBonus;

            foreach (var name in balance.Shop.InitialCastNames)
            {
                var applicant = CastFactory.GenerateApplicant(
                    state.Rng.CastGen, balance,
                    balance.Shop.InitialCastRecruitTier, state.TakeCastSerial());

                // 前任者がある程度育てている。上限は超えない。
                foreach (var kind in Stats.AllKinds)
                {
                    double v = applicant.InitialStats[kind] + bonus;
                    double cap = applicant.Cap[kind];
                    applicant.InitialStats[kind] = v > cap ? cap : v;
                }

                state.Shop.Roster.Add(
                    CastFactory.Hire(applicant, name, balance, state.Eggs, 0));
            }

            return state;
        }

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

            state.PendingGifts = ReceiveAlumniGifts(state);
        }

        /// <summary>
        /// OB からの差し入れ。週頭に届く。
        ///
        /// 期待値が 1 未満のものは確率として扱う。0.4 本なら 40% で 1 本。
        /// 在庫はその場で積まれ、原価は掛からない（要件 6.1）。
        /// </summary>
        static AlumniGiftRecord ReceiveAlumniGifts(GameState state)
        {
            var a = state.Alumni;
            var rng = state.Rng.Events;
            var gifts = new AlumniGiftRecord();

            gifts.TrainingBooks = DrawCount(a.TrainingBooksPerWeek, rng);
            state.TrainingBooks += gifts.TrainingBooks;

            gifts.PremiumBottles = DrawCount(a.PremiumBottlesPerWeek, rng);
            if (gifts.PremiumBottles > 0)
            {
                // 最高段階のボトルが原価 0 で届く。
                int top = state.Balance.Bottles.Alcoholic.Count - 1;
                state.Shop.Stock.Add(
                    StockKey.Bottle(StockItem.BottleAlcoholic, top), gifts.PremiumBottles);
            }

            gifts.LimitedGoods = DrawCount(a.LimitedGoodsPerWeek, rng);
            if (gifts.LimitedGoods > 0)
                state.Shop.Stock.Add(StockKey.Limited, gifts.LimitedGoods);

            gifts.OneDayManager = rng.Chance(a.OneDayManagerChance);

            if (a.CongratulatoryYen > 0 && rng.Chance(a.CongratulatoryChance))
                gifts.CongratulatoryYen = a.CongratulatoryYen;

            return gifts;
        }

        /// <summary>期待値から個数を引く。整数部はそのまま、端数は確率として扱う。</summary>
        static int DrawCount(double expected, GameRandom rng)
        {
            if (expected <= 0) return 0;

            int whole = (int)expected;
            double fraction = expected - whole;
            if (fraction > 0 && rng.Chance(fraction)) whole++;
            return whole;
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

            // 週末の順序は「卒業 → 離脱 → スキャンダル」。
            //
            // 設計書 3.3 は「離職 → スキャンダル → 卒業」としていたが、それだと
            // 2 年勤めてモラルも高いキャストが疲労限界を超えた瞬間に「飛んだ」
            // 扱いになる。育て上げた相手の結末を先に見るのが自然なので入れ替えた。
            ResolveGraduations(state, stats, result);
            ResolveDepartures(state, result);
            ResolveScandals(state, stats, result);
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

                var assignments = VisitorRules.Assign(working, balance, weather, state.Alumni, rng);

                foreach (var a in assignments)
                {
                    var service = OrderRules.Serve(
                        a.Cast, a.Total, a.Slot.Hours, balance, stock, rng, state.Alumni);

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

            result.Gifts = state.PendingGifts;

            // アイドル OB の 1 日店長イベント。その週の売上に上乗せされる。
            if (result.Gifts.OneDayManager)
            {
                result.OneDayManagerBonusYen =
                    (long)System.Math.Round(result.RevenueYen * balance.Cost.OneDayManagerBonusRate,
                                            System.MidpointRounding.AwayFromZero);
                result.RevenueYen += result.OneDayManagerBonusYen;
            }

            // 玉の輿婚 OB のご祝儀。経営の救済枠（要件 6.1）。
            result.CongratulatoryYen = result.Gifts.CongratulatoryYen;

            result.RentYen = state.Shop.WeeklyRentYen(balance);
            result.ManagerPayYen = balance.Cost.WeeklyManagerPayYen;
            result.VariableCostYen = Shop.VariableCostYen(result.RevenueYen, balance);

            state.Shop.CashYen += result.RevenueYen;
            state.Shop.CashYen += result.CongratulatoryYen;
            state.Shop.CashYen -= result.WageYen;
            state.Shop.CashYen -= result.OverheadYen;

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

        static void ResolveGraduations(
            GameState state, Dictionary<string, CastWeekStats> stats, WeekResult result)
        {
            if (state.Careers == null) return;

            int week = state.Week.AbsoluteWeek;

            var leaving = new List<Cast>();
            foreach (var c in state.Shop.Roster)
                if (GraduationRules.CanGraduate(c, week, state.Balance, state.Careers))
                    leaving.Add(c);

            if (leaving.Count == 0) return;

            foreach (var c in leaving)
            {
                double lostRegulars = c.Current.Regulars;
                var alumnus = GraduationRules.Graduate(
                    state.Shop, c, week, state.Balance, state.Careers, state.Rng.Events);

                var def = state.Careers.Find(alumnus.Career);

                result.Graduations.Add(new GraduationRecord
                {
                    CastId = c.Id,
                    StageName = c.StageName,
                    Career = alumnus.Career,
                    CareerName = def?.Name ?? alumnus.Career.ToString(),
                    LifetimeSalesYen = c.LifetimeSalesYen,
                    LostRegulars = lostRegulars,
                });
            }

            // OB が増えたので効果を作り直す。
            state.RefreshAlumniEffects();
        }

        static void ResolveScandals(
            GameState state, Dictionary<string, CastWeekStats> stats, WeekResult result)
        {
            if (state.IsOver) return;

            var balance = state.Balance;
            var rng = state.Rng.Events;

            var departing = new List<Cast>();

            foreach (var c in state.Shop.Roster)
            {
                int drinks = stats.TryGetValue(c.Id, out var s) ? s.AlcoholDrinksServed : 0;

                if (!rng.Chance(ScandalRules.Chance(c, drinks, balance))) continue;

                var record = ScandalRules.Apply(c, balance, rng);
                result.Scandals.Add(record);

                if (record.Departed) departing.Add(c);
            }

            foreach (var c in departing)
            {
                // 炎上して去るのは不名誉なので、悪評は広がる（飛ぶ側と同じ扱い）。
                // 放火は疲労限界超過のときだけなので、ここでは起きない。
                var record = DepartureRules.DepartInDisgrace(state.Shop, c, balance);
                result.Departures.Add(record);
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
