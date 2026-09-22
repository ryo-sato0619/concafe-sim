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
        public void 客単価が四千円前後()
        {
            // チャージ制（滞在 1 時間あたり チャージ 1,200 ＋ ドリンク約 1,000）に
            // したことで、1 杯だけだった頃の 1,930 円から倍増した。
            var m = Measure(3, EveryDay);
            Assert.That(m.AverageSpendYen, Is.EqualTo(3990).Within(200),
                "客単価が動いた。チャージ・滞在時間・商品価格・注文率のどれかを " +
                "変えたなら設計書 14.8 も直すこと");
        }

        [Test]
        public void 序盤から黒字になっている_U17_要再調整()
        {
            // チャージ制の導入で、赤字 40,601 円/週 が黒字 約198,000 円/週 に反転した。
            // 「赤字寸前のコンカフェ」からは大きく外れているため、
            // U-17 でどの係数を締めるかを決める必要がある。
            var m = Measure(3, EveryDay);

            Assert.Greater(m.ProfitYen, 0L);
            Assert.That(m.ProfitYen, Is.EqualTo(198_000).Within(20_000),
                "収支が動いた。U-17 で締めたなら、この期待値と設計書 14.8 を更新すること");
        }

        [Test]
        public void 出勤を減らすと利益が減る()
        {
            var four = Measure(3, FourDaysStaggered);
            var seven = Measure(3, EveryDay);

            Assert.Less(four.ProfitYen, seven.ProfitYen,
                "出勤を減らすと固定費を売上で割れなくなる");
            Assert.That(four.ProfitYen, Is.EqualTo(72_000).Within(15_000));
        }

        [Test]
        public void 粗利が固定費を大きく上回っている_U17_要再調整()
        {
            var m = Measure(3, EveryDay);

            long grossMargin = m.RevenueYen - m.CogsYen - m.WageYen;

            TestContext.WriteLine($"  粗利（固定費前）: {grossMargin:N0}");
            TestContext.WriteLine($"  固定費との差    : {grossMargin - m.FixedCostYen:N0}");

            Assert.Greater(grossMargin, m.FixedCostYen,
                "固定費を賄えなくなった。U-17 で締めたならこの期待値を更新すること");
        }

        [Test]
        public void トークを伸ばすと客単価が上がる()
        {
            // トークは滞在時間を伸ばし、チャージとドリンクの回数を増やす。
            // 集客・知名度が「何人来るか」、トークが「1 人からいくら取れるか」。
            Measurement WithTalk(double talk)
            {
                var s = new GameState(balance, eggs, 4242);
                s.Shop.CashYen = 1_000_000_000;

                var c = new Cast { Id = "c0", StageName = "c0", Age = 30, StaminaTier = 5 };
                c.Current = Stats.Uniform(20);
                c.Current.Talk = talk;
                c.Cap = Stats.Uniform(100);
                c.GrowthRate = Stats.Uniform(0);
                s.Shop.Roster.Add(c);

                long rev = 0;
                int visits = 0;

                for (int w = 0; w < 20; w++)
                {
                    GameFlow.BeginWeek(s);
                    for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;

                    ManagerActions.Order(s, StockKey.SoftDrink, 400);
                    ManagerActions.Order(s, StockKey.Alcohol, 400);
                    ManagerActions.Order(s, StockKey.ChekiFilm, 300);
                    for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                        ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleAlcoholic, t), 20);

                    var r = GameFlow.RunWeek(s, Shift.Uniform(s.Shop.Roster, 7));
                    rev += r.RevenueYen;
                    visits += r.TotalVisitors;
                    c.Fatigue = 0;
                }

                return new Measurement { AverageSpendYen = rev / (double)visits };
            }

            double low = WithTalk(10).AverageSpendYen;
            double high = WithTalk(90).AverageSpendYen;

            TestContext.WriteLine($"トーク 10 の客単価: {low:N0}");
            TestContext.WriteLine($"トーク 90 の客単価: {high:N0}");

            Assert.Greater(high, low * 1.5, "トークが客単価に十分効いていない");
        }
    }
}
