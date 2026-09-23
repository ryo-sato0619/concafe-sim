using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using NUnit.Framework;

namespace Concafe.Tests
{
    public class CalendarTests
    {
        GameCalendar cal;
        BalanceData balance;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            cal = new GameCalendar(balance.Time);
        }

        [Test]
        public void 四十八週で一年が進む()
        {
            Assert.AreEqual(0, cal.YearOf(new GameWeek(0)));
            Assert.AreEqual(0, cal.YearOf(new GameWeek(47)));
            Assert.AreEqual(1, cal.YearOf(new GameWeek(48)));
            Assert.AreEqual(9, cal.YearOf(new GameWeek(479)));
        }

        [Test]
        public void 四週で一月が進む()
        {
            Assert.AreEqual(0, cal.MonthOf(new GameWeek(0)));
            Assert.AreEqual(0, cal.MonthOf(new GameWeek(3)));
            Assert.AreEqual(1, cal.MonthOf(new GameWeek(4)));
            Assert.AreEqual(11, cal.MonthOf(new GameWeek(47)));
            Assert.AreEqual(0, cal.MonthOf(new GameWeek(48)));
        }

        [Test]
        public void 十年の最終週を過ぎたことが判る()
        {
            Assert.IsFalse(cal.IsPastFinalWeek(new GameWeek(479)));
            Assert.IsTrue(cal.IsPastFinalWeek(new GameWeek(480)));
        }

        [Test]
        public void 誕生週にだけ加齢する()
        {
            const int birthWeek = 20;

            Assert.IsFalse(cal.IsBirthdayWeek(new GameWeek(19), birthWeek));
            Assert.IsTrue(cal.IsBirthdayWeek(new GameWeek(20), birthWeek));
            Assert.IsFalse(cal.IsBirthdayWeek(new GameWeek(21), birthWeek));

            // 翌年の同じ週
            Assert.IsTrue(cal.IsBirthdayWeek(new GameWeek(68), birthWeek));
        }

        [Test]
        public void 開始週では加齢しない()
        {
            Assert.IsFalse(cal.IsBirthdayWeek(new GameWeek(0), 0),
                "採用時の年齢がそのまま 1 週目の年齢になる");
        }

        [Test]
        public void 十年通すと誕生日はちょうど十回来る()
        {
            const int birthWeek = 7;
            int count = 0;

            for (int w = 0; w < cal.TotalWeeks; w++)
                if (cal.IsBirthdayWeek(new GameWeek(w), birthWeek)) count++;

            Assert.AreEqual(10, count);
        }

        [Test]
        public void 誕生週が違えば加齢の週も違う()
        {
            // 全員一斉に誕生日を迎えないこと（設計書 2）。
            Assert.IsTrue(cal.IsBirthdayWeek(new GameWeek(50), 2));
            Assert.IsFalse(cal.IsBirthdayWeek(new GameWeek(50), 3));
        }
    }

    public class CastTests
    {
        BalanceData balance;

        [SetUp]
        public void SetUp() => balance = TestBalance.Load();

        static Cast Make(int age, double talk, double alcohol, int tier = 3)
        {
            var c = new Cast { Age = age, StaminaTier = tier, StageName = "テスト" };
            c.Current.Talk = talk;
            c.Current.Alcohol = alcohol;
            return c;
        }

        [Test]
        public void 二十歳未満は未成年()
        {
            Assert.IsTrue(Make(18, 0, 0).IsMinor);
            Assert.IsTrue(Make(19, 0, 0).IsMinor);
            Assert.IsFalse(Make(20, 0, 0).IsMinor);
        }

        [Test]
        public void 未成年の_tierPower_に酒耐性は寄与しない()
        {
            // 不変条件 4。ここを入れると 20 歳の覚醒が消える。
            var noAlcohol = Make(19, 80, 0);
            var fullAlcohol = Make(19, 80, 100);

            Assert.AreEqual(noAlcohol.TierPower(balance), fullAlcohol.TierPower(balance), 1e-12);
            Assert.AreEqual(0.80, noAlcohol.TierPower(balance), 1e-12);
        }

        [Test]
        public void 二十歳になると酒耐性が_tierPower_に乗る()
        {
            var minor = Make(19, 80, 60);
            var adult = Make(20, 80, 60);

            Assert.AreEqual(0.80, minor.TierPower(balance), 1e-12);
            Assert.AreEqual(0.72, adult.TierPower(balance), 1e-12, "80×0.6 + 60×0.4 = 72");
        }

        [Test]
        public void 覚醒でアルコールの解禁段階が増える()
        {
            // 設計書 4.3.3 の検算。tierPower の値は下がるのに、
            // アルコール側は段階が密なため解禁数が増える。
            var minor = Make(19, 80, 60);
            var adult = Make(20, 80, 60);

            int minorTiers = CountUnlocked(balance.Bottles.NonAlcoholic, minor.TierPower(balance));
            int adultTiers = CountUnlocked(balance.Bottles.Alcoholic, adult.TierPower(balance));

            Assert.AreEqual(2, minorTiers, "ノンアルは 12,000 と 35,000 の 2 段階");
            Assert.AreEqual(4, adultTiers, "アルコールは 110,000 までの 4 段階");
        }

        [Test]
        public void ノンアルの最上段はトーク九十五以上でしか解禁されない()
        {
            var talk94 = Make(19, 94, 0);
            var talk95 = Make(19, 95, 0);

            Assert.AreEqual(2, CountUnlocked(balance.Bottles.NonAlcoholic, talk94.TierPower(balance)));
            Assert.AreEqual(3, CountUnlocked(balance.Bottles.NonAlcoholic, talk95.TierPower(balance)));
        }

        [Test]
        public void 最高額ボトルは_tierPower_が_0_9_以上で解禁される()
        {
            var ace = Make(30, 95, 90);
            Assert.AreEqual(0.93, ace.TierPower(balance), 1e-12);
            Assert.AreEqual(6, CountUnlocked(balance.Bottles.Alcoholic, ace.TierPower(balance)));

            var almost = Make(30, 95, 80);
            Assert.AreEqual(0.89, almost.TierPower(balance), 1e-12);
            Assert.AreEqual(5, CountUnlocked(balance.Bottles.Alcoholic, almost.TierPower(balance)),
                "400,000 円は届かない");
        }

        static int CountUnlocked(
            System.Collections.Generic.IReadOnlyList<BottleTierBalance> tiers, double power)
        {
            int n = 0;
            foreach (var t in tiers) if (power >= t.Unlock) n++;
            return n;
        }

        [Test]
        public void 疲労限界は_Tier_から一意に決まる()
        {
            for (int tier = 1; tier <= 5; tier++)
            {
                var c = Make(25, 0, 0, tier);
                Assert.AreEqual(balance.Fatigue.TierLimits[tier - 1], c.FatigueLimit(balance));
            }
        }

        [Test]
        public void 信頼度は疲労限界を変えない()
        {
            // 不変条件 1。延命は蓄積速度だけで行う。
            var c = Make(25, 0, 0, tier: 5);
            double before = c.FatigueLimit(balance);

            c.Trust = 100;

            Assert.AreEqual(before, c.FatigueLimit(balance),
                "信頼度で疲労限界が動いてはいけない");
        }

        [Test]
        public void 信頼度は疲労の蓄積を最大三十五パーセント抑える()
        {
            var c = Make(25, 0, 0);

            c.Trust = 0;
            Assert.AreEqual(1.00, c.TrustRelief(balance), 1e-12);

            c.Trust = 50;
            Assert.AreEqual(0.825, c.TrustRelief(balance), 1e-12);

            c.Trust = 100;
            Assert.AreEqual(0.65, c.TrustRelief(balance), 1e-12);
        }

        [Test]
        public void 生成直後の信頼度は零()
        {
            // 不変条件 17: 素質ではなく、店長が積んだ関係の記録である。
            Assert.AreEqual(0.0, new Cast().Trust);
        }

        [Test]
        public void 市場価値は集客と知名度とトークの平均()
        {
            var c = new Cast();
            c.Current.Regulars = 60;
            c.Current.Fame = 50;
            c.Current.Talk = 70;
            c.Current.Looks = 100;  // 市場価値には効かない
            c.Current.Morale = 100;

            Assert.AreEqual(60.0, c.MarketValue, 1e-12);
        }

        [Test]
        public void 育った者は転生し育つ前の者は飛ぶ()
        {
            double threshold = balance.Departure.DefectThreshold;

            var rookie = new Cast();
            rookie.Current = Stats.Uniform(20);
            Assert.Less(rookie.MarketValue, threshold, "新人は飛ぶ側");

            var ace = new Cast();
            ace.Current = Stats.Uniform(85);
            Assert.GreaterOrEqual(ace.MarketValue, threshold, "エースは転生する側");
        }

        [Test]
        public void 酒耐性が高いほど酒が抜けやすい()
        {
            Assert.AreEqual(1.0, Make(25, 0, 0).Sober(balance), 1e-12);
            Assert.AreEqual(0.1, Make(25, 0, 90).Sober(balance), 1e-12);
            Assert.AreEqual(0.0, Make(25, 0, 100).Sober(balance), 1e-12);
        }
    }

    public class StatsTests
    {
        [Test]
        public void 添字で全ての種類を読み書きできる()
        {
            var s = new Stats();

            foreach (var kind in Stats.AllKinds)
            {
                s[kind] = 42;
                Assert.AreEqual(42.0, s[kind]);
            }
        }

        [Test]
        public void Uniform_は全てを同じ値にする()
        {
            var s = Stats.Uniform(33);

            foreach (var kind in Stats.AllKinds)
                Assert.AreEqual(33.0, s[kind]);
        }

        [Test]
        public void 種類は六つで疲労を含まない()
        {
            Assert.AreEqual(6, Stats.AllKinds.Count);
            CollectionAssert.DoesNotContain(
                System.Enum.GetNames(typeof(StatKind)), "Stamina");
        }
    }

    public class InventoryTests
    {
        [Test]
        public void 追加して消費できる()
        {
            var inv = new Inventory();
            inv.Add(StockKey.ChekiFilm, 3);

            Assert.AreEqual(3, inv[StockKey.ChekiFilm]);
            Assert.IsTrue(inv.TryConsume(StockKey.ChekiFilm));
            Assert.AreEqual(2, inv[StockKey.ChekiFilm]);
        }

        [Test]
        public void 在庫切れは消費に失敗する()
        {
            var inv = new Inventory();
            inv.Add(StockKey.ChekiFilm, 1);

            Assert.IsTrue(inv.TryConsume(StockKey.ChekiFilm));
            Assert.IsFalse(inv.TryConsume(StockKey.ChekiFilm), "機会損失として扱う");
            Assert.AreEqual(0, inv[StockKey.ChekiFilm]);
        }

        [Test]
        public void ボトルは段階ごとに別在庫()
        {
            var inv = new Inventory();
            var tier1 = StockKey.Bottle(StockItem.BottleAlcoholic, 0);
            var tier5 = StockKey.Bottle(StockItem.BottleAlcoholic, 4);

            inv.Add(tier1, 2);
            inv.Add(tier5, 1);

            Assert.AreEqual(2, inv[tier1]);
            Assert.AreEqual(1, inv[tier5]);

            inv.TryConsume(tier5);
            Assert.AreEqual(2, inv[tier1], "別段階の在庫は減らない");
        }

        [Test]
        public void アルコールとノンアルコールは別在庫()
        {
            var inv = new Inventory();
            var alc = StockKey.Bottle(StockItem.BottleAlcoholic, 0);
            var non = StockKey.Bottle(StockItem.BottleNonAlcoholic, 0);

            inv.Add(alc, 1);
            Assert.AreEqual(0, inv[non]);
        }

        [Test]
        public void ボトルに段階を付け忘れたら例外()
        {
            Assert.Throws<System.ArgumentException>(
                () => StockKey.Simple(StockItem.BottleAlcoholic));
            Assert.Throws<System.ArgumentException>(
                () => StockKey.Bottle(StockItem.ChekiFilm, 0));
        }

        [Test]
        public void 未成年に出せない品目が判る()
        {
            Assert.IsTrue(StockItems.IsAlcoholic(StockItem.Alcohol));
            Assert.IsTrue(StockItems.IsAlcoholic(StockItem.BottleAlcoholic));
            Assert.IsFalse(StockItems.IsAlcoholic(StockItem.SoftDrink));
            Assert.IsFalse(StockItems.IsAlcoholic(StockItem.BottleNonAlcoholic));
        }
    }

    public class ShopTests
    {
        BalanceData balance;

        [SetUp]
        public void SetUp() => balance = TestBalance.Load();

        [Test]
        public void 新規開始の状態が_balance_json_に従う()
        {
            var shop = Shop.NewGame(balance);

            Assert.AreEqual(1_500_000L, shop.CashYen);
            Assert.AreEqual(3, shop.CastSlots);
            Assert.AreEqual(10, shop.ManagerAp);
            Assert.AreEqual(0, shop.Roster.Count);
            Assert.IsTrue(shop.HasFreeSlot);
        }

        [Test]
        public void 枠を広げると週次固定費が上がる()
        {
            var shop = Shop.NewGame(balance);
            Assert.AreEqual(70_000L, shop.WeeklyRentYen(balance));

            shop.CastSlots = 6;
            Assert.AreEqual(160_000L, shop.WeeklyRentYen(balance));

            // 最大まで広げると家賃は 4 倍を超える。広げること自体がリスクである
            // ことがここで担保されている（設計書 14.1）。
            shop.CastSlots = 12;
            Assert.AreEqual(340_000L, shop.WeeklyRentYen(balance));
        }

        [Test]
        public void 枠が埋まると空きが無くなる()
        {
            var shop = Shop.NewGame(balance);
            for (int i = 0; i < 3; i++) shop.Roster.Add(new Cast { Id = i.ToString() });

            Assert.IsFalse(shop.HasFreeSlot);
        }
    }
}
