using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 序盤の収支を測り、設計書 14.8 の検算と実装が食い違わないよう固定する。
    ///
    /// 検算を文章だけで持つと、実装との乖離に気づけない。実際 v1.0 の検算は
    /// 「7 日ぶんの売上」と「週 4 日ぶんの給料」を混ぜていて、
    /// 存在しない黒字（+16,750 円/週）を報告していた。
    /// </summary>
    public class EconomyTests
    {
        BalanceData balance;
        EasterEggSet eggs;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
        }

        public struct Measurement
        {
            public double VisitorsPerDay;
            public long RevenueYen;
            public long CogsYen;
            public long WageYen;
            public long FixedCostYen;
            public long ProfitYen;
            public double AverageSpendYen;
        }

        static long StockValue(Shop shop, BalanceData balance)
        {
            long v = 0;
            foreach (var kv in shop.Stock.Entries())
                v += ManagerActions.UnitCostYen(balance, kv.Key) * kv.Value;
            return v;
        }

        /// <summary>
        /// 各パラメータ 20 のキャストを castCount 人置き、pattern のシフトで回す。
        /// 疲労は毎週リセットする（離脱を挟まず収支だけを見たいため）。
        /// </summary>
        Measurement Measure(int castCount, int[][] pattern, int weeks = 20)
        {
            var s = new GameState(balance, eggs, 777);
            s.Shop.CashYen = 1_000_000_000;

            for (int i = 0; i < castCount; i++)
            {
                var c = new Cast { Id = "c" + i, StageName = "c" + i, Age = 22, StaminaTier = 5 };
                c.Current = Stats.Uniform(20);
                c.Cap = Stats.Uniform(20);
                c.GrowthRate = Stats.Uniform(0);   // 成長させず、同じ実力で測る
                s.Shop.Roster.Add(c);
            }

            long rev = 0, wage = 0, cogs = 0;
            int visits = 0;

            for (int w = 0; w < weeks; w++)
            {
                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;

                ManagerActions.Order(s, StockKey.SoftDrink, 300);
                ManagerActions.Order(s, StockKey.Alcohol, 300);
                ManagerActions.Order(s, StockKey.ChekiFilm, 200);
                for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                    ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleAlcoholic, t), 20);

                long stockBefore = StockValue(s.Shop, balance);

                var shift = new Shift();
                for (int i = 0; i < castCount; i++)
                    foreach (int d in pattern[i % pattern.Length]) shift.Set("c" + i, d, true);

                var r = GameFlow.RunWeek(s, shift);

                cogs += stockBefore - StockValue(s.Shop, balance);
                rev += r.RevenueYen;
                wage += r.WageYen;
                visits += r.TotalVisitors;

                foreach (var c in s.Shop.Roster) c.Fatigue = 0;
            }

            long fixedCost = s.Shop.WeeklyFixedCostYen(balance);

            var m = new Measurement
            {
                VisitorsPerDay = visits / (double)(weeks * Shift.DaysPerWeek),
                RevenueYen = rev / weeks,
                CogsYen = cogs / weeks,
                WageYen = wage / weeks,
                FixedCostYen = fixedCost,
                AverageSpendYen = rev / (double)visits,
            };
            m.ProfitYen = m.RevenueYen - m.CogsYen - m.WageYen - m.FixedCostYen;

            Report("キャスト" + castCount + "人", m);
            return m;
        }

        static void Report(string label, Measurement m)
        {
            TestContext.WriteLine($"--- {label} ---");
            TestContext.WriteLine($"  来客/日       : {m.VisitorsPerDay:F1}");
            TestContext.WriteLine($"  客単価        : {m.AverageSpendYen:N0}");
            TestContext.WriteLine($"  売上/週       : {m.RevenueYen:N0}");
            TestContext.WriteLine($"  仕入れ原価/週 : {m.CogsYen:N0}");
            TestContext.WriteLine($"  給料/週       : {m.WageYen:N0}");
            TestContext.WriteLine($"  固定費/週     : {m.FixedCostYen:N0}");
            TestContext.WriteLine($"  純利益/週     : {m.ProfitYen:N0}");
        }

        static readonly int[][] EveryDay = { new[] { 0, 1, 2, 3, 4, 5, 6 } };

        static readonly int[][] FourDaysStaggered =
        {
            new[] { 0, 1, 2, 3 },
            new[] { 2, 3, 4, 5 },
            new[] { 4, 5, 6, 0 },
        };

        [Test]
        public void 来客数が設計書_4_1_の式どおり()
        {
            // 設計書 14.8 は「キャスト 3 人・各パラメータ 20 で 18 人/日」としている。
            // これは全員が毎日出勤した場合の数であり、来客の式自体は正しい。
            var m = Measure(3, EveryDay);
            Assert.AreEqual(18.0, m.VisitorsPerDay, 0.1,
                "来客の式が設計書 4.1 から外れている");
        }

        [Test]
        public void 客単価が二千円前後()
        {
            var m = Measure(3, EveryDay);
            Assert.That(m.AverageSpendYen, Is.EqualTo(1930).Within(150),
                "客単価が動いた。商品価格か注文率を変えたなら設計書 14.8 も直すこと");
        }

        [Test]
        public void 序盤は全員毎日出勤しても赤字である()
        {
            // 設計書 v1.0 の検算は +16,750 円/週 の黒字としていたが、
            // それは 7 日ぶんの売上に 4 日ぶんの給料を当てた誤りだった。
            // 実際は全員毎日出勤でも赤字になる。
            var m = Measure(3, EveryDay);

            Assert.Less(m.ProfitYen, 0L, "赤字でなくなった。バランスを変えたなら設計書 14.8 を直すこと");
            Assert.That(m.ProfitYen, Is.EqualTo(-40_000).Within(8_000));
        }

        [Test]
        public void 週四日のシフトではさらに赤字が深くなる()
        {
            var four = Measure(3, FourDaysStaggered);
            var seven = Measure(3, EveryDay);

            Assert.Less(four.ProfitYen, seven.ProfitYen,
                "出勤を減らすと固定費を売上で割れなくなる");
            Assert.That(four.ProfitYen, Is.EqualTo(-60_000).Within(10_000));
        }

        [Test]
        public void 固定費が黒字化の主な壁になっている()
        {
            var m = Measure(3, EveryDay);

            long grossMargin = m.RevenueYen - m.CogsYen - m.WageYen;

            TestContext.WriteLine($"  粗利（固定費前）: {grossMargin:N0}");
            TestContext.WriteLine($"  固定費との差    : {grossMargin - m.FixedCostYen:N0}");

            Assert.Greater(grossMargin, 0L, "固定費の前で既に赤字なら、価格か原価の問題になる");
            Assert.Less(grossMargin, m.FixedCostYen, "固定費を賄えている");
        }
    }
}
