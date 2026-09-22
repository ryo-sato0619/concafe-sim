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
        CareerSet careers;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
            careers = TestCareers.Load();
        }

        public struct Measurement
        {
            public double VisitorsPerDay;
            public long RevenueYen;
            public long CogsYen;
            public long WageYen;
            public long OverheadYen;
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
            var s = new GameState(balance, eggs, careers, 777);
            s.Shop.CashYen = 1_000_000_000;

            for (int i = 0; i < castCount; i++)
            {
                // 炎上で数値が削れると収支の測定が乱れるため、耐性を最大にする。
                var c = new Cast { Id = "c" + i, StageName = "c" + i, Age = 22,
                                   StaminaTier = 5, ScandalResist = 100 };
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
                    foreach (int d in pattern[i % pattern.Length])
                        shift.Set("c" + i, d,
                            new ShiftSlot(balance.Business.OpenHour(d), balance.Business.CloseHour));

                var r = GameFlow.RunWeek(s, shift);

                cogs += stockBefore - StockValue(s.Shop, balance);
                rev += r.RevenueYen;
                wage += r.WageYen;
                visits += r.TotalVisitors;

                foreach (var c in s.Shop.Roster) c.Fatigue = 0;
            }

            long overhead = s.Shop.WeeklyRentYen(balance) + balance.Cost.WeeklyManagerPayYen
                          + Shop.VariableCostYen(rev / weeks, balance);

            var m = new Measurement
            {
                VisitorsPerDay = visits / (double)(weeks * Shift.DaysPerWeek),
                RevenueYen = rev / weeks,
                CogsYen = cogs / weeks,
                WageYen = wage / weeks,
                OverheadYen = overhead,
                AverageSpendYen = rev / (double)visits,
            };
            m.ProfitYen = m.RevenueYen - m.CogsYen - m.WageYen - m.OverheadYen;

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
            TestContext.WriteLine($"  諸経費/週     : {m.OverheadYen:N0}");
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
        public void 平日の来客数が設計書_4_1_の式どおり()
        {
            // 設計書 14.8 の「キャスト 3 人・各パラメータ 20 で 18 人/日」は
            // 平日（17〜23 時の 6 時間）に全員がフル出勤した場合の数である。
            // 営業時間とシフトを時刻で持つようにしても、ここは変わらない。
            var s = new GameState(balance, eggs, careers, 777);
            s.Shop.CashYen = 1_000_000_000;

            for (int i = 0; i < 3; i++)
            {
                // 炎上で数値が削れると収支の測定が乱れるため、耐性を最大にする。
                var c = new Cast { Id = "c" + i, StageName = "c" + i, Age = 22,
                                   StaminaTier = 5, ScandalResist = 100 };
                c.Current = Stats.Uniform(20);
                c.Cap = Stats.Uniform(20);
                c.GrowthRate = Stats.Uniform(0);
                s.Shop.Roster.Add(c);
            }

            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            ManagerActions.Order(s, StockKey.SoftDrink, 400);
            ManagerActions.Order(s, StockKey.Alcohol, 400);
            ManagerActions.Order(s, StockKey.ChekiFilm, 300);

            var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance));

            for (int d = 0; d < Shift.DaysPerWeek; d++)
            {
                var day = r.Days[d];
                TestContext.WriteLine(
                    $"曜日{d} {(day.IsHoliday ? "休" : "平")} {day.OpenHours}時間 来客{day.Visitors}");

                if (day.IsHoliday) continue;
                Assert.AreEqual(18, day.Visitors,
                    $"平日（曜日 {d}）の来客が設計書 4.1 から外れている");
            }
        }

        [Test]
        public void 休日は営業時間が長いぶん客が増える()
        {
            var s = new GameState(balance, eggs, careers, 777);
            s.Shop.CashYen = 1_000_000_000;

            var c0 = new Cast { Id = "c0", StageName = "c0", Age = 22,
                               StaminaTier = 5, ScandalResist = 100 };
            c0.Current = Stats.Uniform(20);
            c0.Cap = Stats.Uniform(20);
            c0.GrowthRate = Stats.Uniform(0);
            s.Shop.Roster.Add(c0);

            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            ManagerActions.Order(s, StockKey.SoftDrink, 400);
            ManagerActions.Order(s, StockKey.Alcohol, 400);
            ManagerActions.Order(s, StockKey.ChekiFilm, 300);

            var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance));

            int weekday = r.Days[0].Visitors;   // 月曜
            int holiday = r.Days[5].Visitors;   // 土曜

            Assert.Greater(holiday, weekday, "休日のほうが客が少ない");
            Assert.AreEqual(8, r.Days[5].OpenHours);
            Assert.AreEqual(6, r.Days[0].OpenHours);
        }

        [Test]
        public void 客単価が約三千八百円()
        {
            // チャージ制（滞在 1 時間あたり チャージ 1,100 ＋ 客のドリンク 900 = 2,000）に
            // したことで、1 杯だけだった頃の 1,930 円から倍増した。
            var m = Measure(3, EveryDay);
            Assert.That(m.AverageSpendYen, Is.EqualTo(3790).Within(200),
                "客単価が動いた。チャージ・滞在時間・商品価格・注文率のどれかを " +
                "変えたなら設計書 14.8 も直すこと");
        }

        [Test]
        public void 序盤はギリギリ黒字になった_U17_解決()
        {
            // U-17 の決着。家賃以外の経費（店長報酬 ＋ 変動費 17%）を入れて、
            // 当初承認された「ギリギリ黒字 約16,000 円/週」に着地した。
            // ただしこれは**全員フル出勤**の場合である（週 4 日だと赤字）。
            var m = Measure(3, EveryDay);

            Assert.Greater(m.ProfitYen, 0L);
            Assert.That(m.ProfitYen, Is.EqualTo(16_700).Within(6_000),
                "収支が動いた。経費を触ったなら、この期待値と設計書 14.8 を更新すること");
        }

        [Test]
        public void 出勤を減らすと利益が減る()
        {
            var four = Measure(3, FourDaysStaggered);
            var seven = Measure(3, EveryDay);

            Assert.Less(four.ProfitYen, seven.ProfitYen,
                "出勤を減らすと固定費を売上で割れなくなる");
            Assert.That(four.ProfitYen, Is.EqualTo(-46_000).Within(12_000));
        }

        [Test]
        public void 粗利が諸経費とほぼ釣り合っている()
        {
            var m = Measure(3, EveryDay);

            long grossMargin = m.RevenueYen - m.CogsYen - m.WageYen;

            TestContext.WriteLine($"  粗利（諸経費前）: {grossMargin:N0}");
            TestContext.WriteLine($"  諸経費との差    : {grossMargin - m.OverheadYen:N0}");

            Assert.Greater(grossMargin, m.OverheadYen, "諸経費を賄えていない");
            Assert.Less(grossMargin, m.OverheadYen * 1.1,
                "粗利が諸経費を 10% 以上上回っている。緩すぎる");
        }

        [Test]
        public void トークを伸ばすと客単価が上がる()
        {
            // トークは滞在時間を伸ばし、チャージとドリンクの回数を増やす。
            // 集客・知名度が「何人来るか」、トークが「1 人からいくら取れるか」。
            Measurement WithTalk(double talk)
            {
                var s = new GameState(balance, eggs, careers, 4242);
                s.Shop.CashYen = 1_000_000_000;

                var c = new Cast { Id = "c0", StageName = "c0", Age = 30,
                                   StaminaTier = 5, ScandalResist = 100 };
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

                    var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance));
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
