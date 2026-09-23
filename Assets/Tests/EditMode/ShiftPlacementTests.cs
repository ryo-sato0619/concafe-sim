using System.Collections.Generic;
using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 時間帯ごとの来客の偏り（U-20）と期間限定商品（U-18）。
    ///
    /// ピークタイムを入れた狙いは、シフトを「何時間入れるか」だけでなく
    /// 「どこに置くか」の選択にすることである。
    /// </summary>
    public class ShiftPlacementTests
    {
        BalanceData balance;
        EasterEggSet eggs;
        CareerSet careers;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
            careers = TestCareers.Load();
        }

        const int Weekday = 0;   // 月曜
        const int Holiday = 5;   // 土曜

        // ---- ピークタイム（U-20）-------------------------------------------

        [Test]
        public void フル出勤の来客数はピークタイムを入れても変わらない()
        {
            // 重みはその日の合計で正規化してあるので、開店から閉店までの
            // 係数はちょうど「営業時間 / 平日の営業時間」になる。
            // ここが崩れると平日 18 人/日・休日 24 人/日の校正（T-44）が壊れる。
            var b = balance.Business;

            Assert.AreEqual(1.0,
                b.VisitorHoursFactor(b.OpenHour(Weekday), b.CloseHour, Weekday), 1e-9,
                "平日フル出勤が 1.0 でない");

            Assert.AreEqual(b.OpenHours(Holiday) / (double)b.BaselineOpenHours,
                b.VisitorHoursFactor(b.OpenHour(Holiday), b.CloseHour, Holiday), 1e-9,
                "休日フル出勤が営業時間の比になっていない");
        }

        [Test]
        public void 同じ長さでも遅い時間帯のほうが客が付く()
        {
            var b = balance.Business;
            int hours = b.MinShiftHours;

            double early = b.VisitorHoursFactor(
                b.OpenHour(Weekday), b.OpenHour(Weekday) + hours, Weekday);
            double late = b.VisitorHoursFactor(b.CloseHour - hours, b.CloseHour, Weekday);

            TestContext.WriteLine($"平日 {hours} 時間: 開店直後 {early:F3} / 閉店前 {late:F3}");

            Assert.Greater(late, early,
                "時間帯による差が無い。シフトを『どこに置くか』が選択になっていない");
        }

        [Test]
        public void ピークは深夜割増と重なっている()
        {
            // ピークを取りにいくと人件費（深夜割増）も疲労も増える、という
            // 引き換えが成立していること。重なっていないなら、
            // 遅い時間に入れるのが一方的に得になる。
            var b = balance.Business;

            int peakHour = -1;
            double best = 0;
            for (int h = b.OpenHour(Weekday); h < b.CloseHour; h++)
                if (b.HourWeight(h) > best) { best = b.HourWeight(h); peakHour = h; }

            TestContext.WriteLine($"平日のピークは {peakHour} 時（重み {best}）");

            Assert.GreaterOrEqual(peakHour + 1, b.LateNightFromHour,
                "ピークが深夜割増の手前で終わっている。遅番が一方的に得になる");
        }

        [Test]
        public void 閉店前に寄せたシフトのほうが売上も人件費も高い()
        {
            long Revenue(int start, int end, out long wage)
            {
                var s = new GameState(balance, eggs, careers, 4242);
                s.Shop.CashYen = 1_000_000_000;
                s.Shop.Facilities[FacilityKind.Fridge] =
                    balance.Facilities.FridgeCapacity.Count - 1;

                var c = new Cast { Id = "c0", StageName = "c0", Age = 22,
                                   StaminaTier = 5, ScandalResist = 100 };
                c.Current = Stats.Uniform(40);
                c.Cap = Stats.Uniform(40);
                c.GrowthRate = Stats.Uniform(0);
                s.Shop.Roster.Add(c);

                long rev = 0, w = 0;
                for (int week = 0; week < 30; week++)
                {
                    GameFlow.BeginWeek(s);
                    for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;

                    ManagerActions.Order(s, StockKey.SoftDrink,
                        300 - s.Shop.Stock[StockKey.SoftDrink]);
                    ManagerActions.Order(s, StockKey.ChekiFilm,
                        300 - s.Shop.Stock[StockKey.ChekiFilm]);

                    var shift = new Shift();
                    for (int d = 0; d < 5; d++) shift.Set("c0", d, new ShiftSlot(start, end));

                    var r = GameFlow.RunWeek(s, shift);
                    rev += r.RevenueYen;
                    w += r.WageYen;
                    c.Fatigue = 0;
                }

                wage = w;
                return rev;
            }

            // 平日の 6 時間営業（17〜23 時）のうち 3 時間ぶん。
            long earlyRev = Revenue(17, 20, out long earlyWage);
            long lateRev = Revenue(20, 23, out long lateWage);

            TestContext.WriteLine($"17-20 時: 売上 {earlyRev:N0} / 給料 {earlyWage:N0}");
            TestContext.WriteLine($"20-23 時: 売上 {lateRev:N0} / 給料 {lateWage:N0}");

            Assert.Greater(lateRev, earlyRev, "遅番のほうが売上が低い");
            Assert.Greater(lateWage, earlyWage, "遅番のほうが給料が安い。深夜割増が効いていない");
        }

        [Test]
        public void 混む時間帯に置くと疲労も速い()
        {
            // 遅番は売上が高いが、疲労は接客した延べ時間に比例する（不変条件 26）ので
            // そのぶん速く削れる。これが「ピークを取りにいく」ことの代償であり、
            // 疲れたキャストを早番に回して延命する、という手を成立させている。
            double Fatigue(int start, int end)
            {
                var s = new GameState(balance, eggs, careers, 4242);
                s.Shop.CashYen = 1_000_000_000;
                s.Shop.Facilities[FacilityKind.Fridge] =
                    balance.Facilities.FridgeCapacity.Count - 1;

                var c = new Cast { Id = "c0", StageName = "c0", Age = 22,
                                   StaminaTier = 5, ScandalResist = 100 };
                c.Current = Stats.Uniform(40);
                c.Cap = Stats.Uniform(40);
                c.GrowthRate = Stats.Uniform(0);
                s.Shop.Roster.Add(c);

                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                ManagerActions.Order(s, StockKey.SoftDrink, 300);
                ManagerActions.Order(s, StockKey.ChekiFilm, 300);

                var shift = new Shift();
                for (int d = 0; d < 5; d++) shift.Set("c0", d, new ShiftSlot(start, end));

                GameFlow.RunWeek(s, shift);
                return c.Fatigue;
            }

            double early = Fatigue(17, 20);
            double late = Fatigue(20, 23);

            TestContext.WriteLine($"1 週間の疲労: 17-20 時 {early:F2} / 20-23 時 {late:F2}");

            Assert.Greater(late, early,
                "遅番のほうが疲れない。ピークを取る代償が無い");
        }

        [Test]
        public void 時間帯の重みが営業時間と合わなければ弾く()
        {
            string json = TestBalance.RawJson()
                .Replace(@"[0.5, 0.6, 0.7, 0.8, 0.9, 1.1, 1.3, 1.2]",
                         @"[0.5, 0.6, 0.7, 0.8]");

            var e = Assert.Throws<BalanceException>(() => BalanceLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains("hourWeights")),
                string.Join(" / ", e.Problems));
        }

        // ---- 期間限定商品（U-18）-------------------------------------------

        Cast Talker()
        {
            var c = new Cast { Id = "c0", StageName = "c0", Age = 22, StaminaTier = 5 };
            c.Current = Stats.Uniform(50);
            c.Cap = Stats.Uniform(100);
            return c;
        }

        /// <summary>期間限定商品の有無だけが違う在庫。</summary>
        Inventory Filled(bool withLimited)
        {
            var stock = new Inventory();
            stock.Add(StockKey.SoftDrink, 10_000);
            stock.Add(StockKey.Alcohol, 10_000);
            stock.Add(StockKey.ChekiFilm, 10_000);
            for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                stock.Add(StockKey.Bottle(StockItem.BottleAlcoholic, t), 500);
            if (withLimited) stock.Add(StockKey.Limited, 10_000);
            return stock;
        }

        [Test]
        public void 期間限定商品は在庫があれば売れる()
        {
            var stock = new Inventory();
            stock.Add(StockKey.Limited, 1000);

            var r = OrderRules.Serve(Talker(), 200, 6, balance, stock, new GameRandom(11));

            Assert.Less(stock[StockKey.Limited], 1000, "期間限定商品が消費されていない");
            Assert.Greater(r.SalesYen, 0);
        }

        [Test]
        public void 期間限定商品は切らしても機会損失にならない()
        {
            // これだけが他の品目と違う。常備を強いると「毎週必ず仕入れる商品」に
            // なり、限定という性格が消える（設計書 4.3）。
            //
            // 他の品目は在庫を揃えたうえで、期間限定商品の有無だけを変えて比べる。
            var a = OrderRules.Serve(Talker(), 300, 6, balance, Filled(true), new GameRandom(11));
            var b = OrderRules.Serve(Talker(), 300, 6, balance, Filled(false), new GameRandom(11));

            Assert.Greater(a.SalesYen, b.SalesYen, "在庫があっても売上が増えない");
            Assert.AreEqual(a.LostOrders, b.LostOrders,
                "期間限定商品を切らして機会損失が増えている");
            Assert.AreEqual(a.LostSalesYen, b.LostSalesYen);
        }

        [Test]
        public void 期間限定商品はバックに乗らない()
        {
            // 不変条件 25: バックの対象はチェキ・キャストドリンク・ボトルのみ。
            var a = OrderRules.Serve(Talker(), 300, 6, balance, Filled(true), new GameRandom(11));
            var b = OrderRules.Serve(Talker(), 300, 6, balance, Filled(false), new GameRandom(11));

            Assert.AreEqual(b.BackableSalesYen, a.BackableSalesYen,
                "期間限定商品がバック対象に混ざっている");
        }

        [Test]
        public void 実家の家業_OB_の差し入れがそのまま売上になる()
        {
            // 原価 0 で届き、注文が発生して初めて売上になる。
            // 段階 4 の時点では在庫が積み上がるだけだった（U-18）。
            long without = OneWeekRevenue(limited: 0);
            long with = OneWeekRevenue(limited: 200);

            TestContext.WriteLine($"期間限定なし {without:N0} / あり {with:N0}");
            Assert.Greater(with, without, "期間限定商品が売上に反映されていない");
        }

        /// <summary>期間限定商品の在庫だけを変えて 1 週間回す。他はすべて同じ。</summary>
        long OneWeekRevenue(int limited)
        {
            var s = new GameState(balance, eggs, careers, 7);
            s.Shop.CashYen = 10_000_000;
            s.Shop.Facilities[FacilityKind.Fridge] =
                balance.Facilities.FridgeCapacity.Count - 1;

            var c = Talker();
            c.GrowthRate = Stats.Uniform(0);
            s.Shop.Roster.Add(c);

            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;

            ManagerActions.Order(s, StockKey.SoftDrink, 400);
            ManagerActions.Order(s, StockKey.ChekiFilm, 400);
            if (limited > 0) ManagerActions.Order(s, StockKey.Limited, limited);

            return GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance)).RevenueYen;
        }

        // ---- 集客の自然成長（U-19）-----------------------------------------

        [Test]
        public void 集客は出勤では伸びない()
        {
            // U-19 決着: 伸ばさない。集客を特訓に限ることが店長 AP の価値の柱で、
            // ここを開けると AP の逼迫が緩んで終盤の判断が薄くなる。
            Assert.AreEqual(0.0, balance.Manager.RegularsWorkGainRatio,
                "manager.regularsWorkGainRatio が 0 でない。U-19 を変えたなら設計書 14.4 も直すこと");

            var c = new Cast { Id = "c0", StageName = "c0", Age = 22, StaminaTier = 5 };
            c.Current = Stats.Uniform(20);
            c.Cap = Stats.Uniform(100);
            c.GrowthRate = Stats.Uniform(1.0);

            double before = c.Current.Regulars;
            for (int d = 0; d < 100; d++)
                GrowthRules.ApplyWorkDay(c, balance.Business.BaselineOpenHours, balance);

            Assert.AreEqual(before, c.Current.Regulars, 1e-9, "集客が接客で伸びている");
            Assert.Greater(c.Current.Talk, before, "トークは伸びるはず");
        }

        [Test]
        public void 集客を出勤で伸ばすと難易度が消える()
        {
            // U-19 の判断根拠。ratio を開けた場合との差を測って記録する。
            //
            // 当初は「AP が余って緩む」と見ていたが、実測は逆だった。
            // 集客がただで伸びるぶん店が速く育ち、枠が埋まって AP はむしろ
            // 逼迫する。問題はそこではなく、**難易度そのものが消える**ことである。
            void Measure(string label, BalanceData b, EasterEggSet e)
            {
                int completed = 0, grad = 0, neglect = 0;
                long cash = 0;

                for (ulong seed = 1000; seed < 1030; seed++)
                {
                    var r = AutoPilot.Run(b, e, careers, seed,
                                          new AutoPilot.Policy { DaysPerCast = 3 });
                    if (r.Completed) completed++;
                    grad += r.Graduations;
                    neglect += r.NeglectedCastWeeks;
                    cash += r.FinalCashYen;
                }

                TestContext.WriteLine(
                    $"{label}: 完走 {completed * 100.0 / 30:F1}%  平均資金 {cash / 30:N0}  " +
                    $"卒業 {grad / 30.0:F1}  ケア不足 {neglect / 30.0:F0}人週");
            }

            var opened = BalanceLoader.Load(TestBalance.RawJson()
                .Replace(@"""regularsWorkGainRatio"": 0.0",
                         @"""regularsWorkGainRatio"": 1.0"));

            // 週 3 日で測る。ここが「薄利で店が育たない」罰の効くペースであり、
            // 集客がただで伸びるとその罰が真っ先に消える。
            Measure("特訓のみ（採用）", balance, eggs);
            Measure("出勤でも伸びる  ", opened, TestEggs.Load(opened));

            int Completed(BalanceData b, EasterEggSet e)
            {
                int n = 0;
                for (ulong seed = 1000; seed < 1030; seed++)
                    if (AutoPilot.Run(b, e, careers, seed,
                                      new AutoPilot.Policy { DaysPerCast = 3 }).Completed) n++;
                return n;
            }

            Assert.Less(Completed(balance, eggs), Completed(opened, TestEggs.Load(opened)),
                "集客を出勤で伸ばしても週 3 日の完走率が変わらない。U-19 の判断根拠が消えている");
        }
    }
}
