using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>設備投資（U-13、要件 5.4）。</summary>
    public class FacilityTests
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

        GameState Rich()
        {
            var s = new GameState(balance, eggs, careers, 99);
            s.Shop.CashYen = 1_000_000_000;
            return s;
        }

        // ---- 店舗拡張 -------------------------------------------------------

        [Test]
        public void 店舗拡張で枠が増え最大まで届く()
        {
            var s = Rich();
            Assert.AreEqual(balance.Shop.InitialCastSlots, s.Shop.CastSlots);

            int steps = 0;
            while (ManagerActions.Invest(s, FacilityKind.Expansion) == ActionResult.Ok) steps++;

            Assert.AreEqual(balance.Facilities.ExpansionPriceYen.Count, steps,
                "投資できる段数が balance.json と合わない");
            Assert.AreEqual(balance.Shop.MaxCastSlots, s.Shop.CastSlots,
                "全段投資しても最大の枠に届かない");
        }

        [Test]
        public void 枠を広げると家賃が上がる()
        {
            var s = Rich();
            long before = s.Shop.WeeklyRentYen(balance);

            ManagerActions.Invest(s, FacilityKind.Expansion);

            Assert.AreEqual(before + balance.Cost.RentPerExpansionYen,
                            s.Shop.WeeklyRentYen(balance),
                "広げること自体がリスクになっていない（設計書 14.1）");
        }

        [Test]
        public void 設備投資は店長_AP_を使わない()
        {
            // 不変条件 6: 店長自身は育たない。強化は資金だけで行う。
            var s = Rich();
            int ap = s.Shop.ManagerAp;

            ManagerActions.Invest(s, FacilityKind.Expansion);
            ManagerActions.Invest(s, FacilityKind.Fridge);
            ManagerActions.Invest(s, FacilityKind.Espresso);

            Assert.AreEqual(ap, s.Shop.ManagerAp);
        }

        [Test]
        public void 枠を広げても店長_AP_は増えない()
        {
            // 不変条件 7。終盤に全員をケアできなくなることが緊張の源である。
            // ここが破れると、枠の拡張が一方的な強化になってゲームが緩む。
            var s = Rich();

            while (ManagerActions.Invest(s, FacilityKind.Expansion) == ActionResult.Ok) { }
            GameFlow.BeginWeek(s);

            Assert.AreEqual(balance.Manager.ApPerWeek, s.Shop.ManagerAp,
                "枠を広げたら AP が増えた。全員をケアできてしまう");
        }

        [Test]
        public void 設備投資は疲労限界を変えない()
        {
            // 不変条件 1。StaminaTier は生成時に決まり以後不変。
            var s = Rich();
            var c = new Cast { Id = "c0", StageName = "c0", Age = 22, StaminaTier = 2 };
            c.Current = Stats.Uniform(30);
            c.Cap = Stats.Uniform(100);
            s.Shop.Roster.Add(c);

            double limit = c.FatigueLimit(balance);

            foreach (FacilityKind kind in System.Enum.GetValues(typeof(FacilityKind)))
                while (ManagerActions.Invest(s, kind) == ActionResult.Ok) { }

            Assert.AreEqual(limit, c.FatigueLimit(balance),
                "設備投資で疲労限界が動いた（不変条件 1）");
            Assert.AreEqual(2, c.StaminaTier);
        }

        [Test]
        public void 資金が足りなければ投資できない()
        {
            var s = new GameState(balance, eggs, careers, 99);
            s.Shop.CashYen = 1000;

            Assert.AreEqual(ActionResult.NotEnoughCash,
                            ManagerActions.Invest(s, FacilityKind.Expansion));
            Assert.AreEqual(balance.Shop.InitialCastSlots, s.Shop.CastSlots);
            Assert.AreEqual(1000L, s.Shop.CashYen, "失敗したのに資金が減っている");
        }

        [Test]
        public void 最終段に達したらそれ以上投資できない()
        {
            var s = Rich();
            while (ManagerActions.Invest(s, FacilityKind.Espresso) == ActionResult.Ok) { }

            Assert.AreEqual(ActionResult.Invalid,
                            ManagerActions.Invest(s, FacilityKind.Espresso));
            Assert.AreEqual(-1L, ManagerActions.NextInvestmentYen(s, FacilityKind.Espresso));
        }

        // ---- 冷蔵庫 ---------------------------------------------------------

        [Test]
        public void 冷蔵庫の上限を超える発注は通らない()
        {
            var s = Rich();
            int cap = s.Shop.FridgeCapacity(balance);

            Assert.AreEqual(ActionResult.Ok,
                            ManagerActions.Order(s, StockKey.SoftDrink, cap));
            Assert.AreEqual(ActionResult.NoStorage,
                            ManagerActions.Order(s, StockKey.Alcohol, 1),
                "上限を超えて仕入れられてしまう");

            Assert.AreEqual(cap, s.Shop.ChilledCount);
        }

        [Test]
        public void 冷蔵庫を広げれば入る()
        {
            var s = Rich();
            int cap = s.Shop.FridgeCapacity(balance);
            ManagerActions.Order(s, StockKey.SoftDrink, cap);

            Assert.AreEqual(ActionResult.NoStorage,
                            ManagerActions.Order(s, StockKey.Alcohol, 1));

            ManagerActions.Invest(s, FacilityKind.Fridge);

            Assert.Greater(s.Shop.FridgeCapacity(balance), cap);
            Assert.AreEqual(ActionResult.Ok,
                            ManagerActions.Order(s, StockKey.Alcohol, 1));
        }

        [Test]
        public void チェキフィルムは冷蔵しない()
        {
            // 冷蔵庫が一杯でもチェキは仕入れられる。
            var s = Rich();
            ManagerActions.Order(s, StockKey.SoftDrink, s.Shop.FridgeCapacity(balance));

            Assert.AreEqual(ActionResult.Ok,
                            ManagerActions.Order(s, StockKey.ChekiFilm, 500));
        }

        [Test]
        public void ボトルは冷蔵する()
        {
            var s = Rich();
            ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleAlcoholic, 0), 10);

            Assert.AreEqual(10, s.Shop.ChilledCount);
        }

        // ---- エスプレッソマシン ---------------------------------------------

        [Test]
        public void エスプレッソマシンで客のドリンクの単価が上がる()
        {
            var s = Rich();
            Assert.AreEqual(0L, s.Shop.GuestDrinkBonusYen(balance),
                "未投資の状態に効果が付いている");

            ManagerActions.Invest(s, FacilityKind.Espresso);

            Assert.Greater(s.Shop.GuestDrinkBonusYen(balance), 0);
        }

        [Test]
        public void エスプレッソマシンはバックに乗らない()
        {
            // 不変条件 25: バックの対象はチェキ・キャストドリンク・ボトルのみ。
            // 客のドリンクの単価が上がっても人件費には跳ねない。
            // これがこの設備を「売上が伸びても人件費が増えない唯一の投資」にしている。
            var c = new Cast { Id = "c0", StageName = "c0", Age = 22, StaminaTier = 5 };
            c.Current = Stats.Uniform(50);
            c.Cap = Stats.Uniform(100);

            var stock = new Inventory();
            stock.Add(StockKey.SoftDrink, 10_000);

            long bonus = balance.Facilities.EspressoDrinkBonusYen.Last();

            var plain = OrderRules.Serve(c, 200, 6, balance,
                                         Filled(), new Concafe.Simulation.Rng.GameRandom(7));
            var boosted = OrderRules.Serve(c, 200, 6, balance,
                                           Filled(), new Concafe.Simulation.Rng.GameRandom(7),
                                           null, bonus);

            Assert.Greater(boosted.SalesYen, plain.SalesYen, "売上が増えていない");
            Assert.AreEqual(plain.BackableSalesYen, boosted.BackableSalesYen,
                "客のドリンクの単価がバック対象に混ざっている（不変条件 25）");
        }

        Inventory Filled()
        {
            var stock = new Inventory();
            stock.Add(StockKey.SoftDrink, 10_000);
            stock.Add(StockKey.Alcohol, 10_000);
            stock.Add(StockKey.ChekiFilm, 10_000);
            for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                stock.Add(StockKey.Bottle(StockItem.BottleAlcoholic, t), 100);
            return stock;
        }

        [Test]
        public void エスプレッソマシンはチャージを上げない()
        {
            // チャージは席と時間の対価であって商品ではない（設計書 4.3）。
            // ここを設備で動かすと、在庫と無関係な土台という性格が崩れる。
            var c = new Cast { Id = "c0", StageName = "c0", Age = 22, StaminaTier = 5 };
            c.Current = Stats.Uniform(50);
            c.Cap = Stats.Uniform(100);

            // 在庫を空にすると、入るのはチャージだけになる。
            var plain = OrderRules.Serve(c, 300, 6, balance,
                                         new Inventory(), new Concafe.Simulation.Rng.GameRandom(7));
            var boosted = OrderRules.Serve(c, 300, 6, balance,
                                           new Inventory(), new Concafe.Simulation.Rng.GameRandom(7),
                                           null, balance.Facilities.EspressoDrinkBonusYen.Last());

            Assert.AreEqual(plain.SalesYen, boosted.SalesYen,
                "在庫が無いのに売上が増えた。チャージに効いている");
        }

        // ---- 検査 -----------------------------------------------------------

        [Test]
        public void 拡張の段数が最大枠と合わなければ弾く()
        {
            string json = Mutate(@"[3000000, 8000000, 18000000]", @"[3000000, 8000000]");
            AssertRejects(json, "段数が合わない");
        }

        [Test]
        public void 冷蔵庫の容量が増えなければ弾く()
        {
            AssertRejects(Mutate(@"[240, 400, 560, 720]", @"[240, 400, 400, 720]"),
                          "増えていない");
        }

        [Test]
        public void 未投資にエスプレッソの効果が付いていたら弾く()
        {
            AssertRejects(Mutate(@"[0, 100, 220]", @"[50, 100, 220]"),
                          "未投資の状態に効果が付いている");
        }

        [Test]
        public void 後の段のほうが安ければ弾く()
        {
            AssertRejects(Mutate(@"[3000000, 8000000, 18000000]",
                                 @"[3000000, 8000000, 4000000]"),
                          "前の段以下");
        }

        static string Mutate(string from, string to)
        {
            string original = TestBalance.RawJson();
            StringAssert.Contains(from, original, "置換対象が balance.json に無い");
            return original.Replace(from, to);
        }

        static void AssertRejects(string json, string expectedFragment)
        {
            var e = Assert.Throws<BalanceException>(() => BalanceLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains(expectedFragment)),
                $"「{expectedFragment}」を含む指摘が無い。実際: {string.Join(" / ", e.Problems)}");
        }
    }
}
