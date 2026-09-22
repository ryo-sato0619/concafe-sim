using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using NUnit.Framework;

namespace Concafe.Tests
{
    public class BalanceTests
    {
        [Test]
        public void 実物の_balance_json_が読めて検査を通る()
        {
            var b = TestBalance.Load();

            Assert.AreEqual(BalanceLoader.SupportedSchemaVersion, b.SchemaVersion);
            Assert.AreEqual(3, b.SchemaVersion);
            Assert.AreEqual(48, b.Time.WeeksPerYear);
            Assert.AreEqual(10, b.Time.YearsPerRun);
            Assert.AreEqual(480, b.Time.TotalWeeks);
            Assert.AreEqual(12, b.Time.MonthsPerYear);
        }

        [Test]
        public void 承認済みの数値がそのまま入っている()
        {
            var b = TestBalance.Load();

            Assert.AreEqual(1_500_000L, b.Shop.InitialCashYen);
            Assert.AreEqual(93_000L, b.Shop.WeeklyFixedCostYen);
            Assert.AreEqual(3, b.Shop.InitialCastSlots);
            Assert.AreEqual(12, b.Shop.MaxCastSlots);

            Assert.AreEqual(1_600L, b.Items.ChekiFilm.PriceYen);
            Assert.AreEqual(1_600L, b.CastDrinkPriceYen);

            Assert.AreEqual(10, b.Manager.ApPerWeek);
            Assert.AreEqual(3, b.Manager.TrainCostAp);
            Assert.AreEqual(2, b.Manager.CareCostAp);
        }

        [Test]
        public void ボトルの段階が指定どおりに並んでいる()
        {
            var b = TestBalance.Load();

            var alcoholPrices = b.Bottles.Alcoholic.Select(t => t.PriceYen).ToArray();
            CollectionAssert.AreEqual(
                new long[] { 12_000, 35_000, 70_000, 110_000, 220_000, 400_000 },
                alcoholPrices);

            var softPrices = b.Bottles.NonAlcoholic.Select(t => t.PriceYen).ToArray();
            CollectionAssert.AreEqual(
                new long[] { 12_000, 35_000, 220_000 },
                softPrices);
        }

        [Test]
        public void ノンアルコールは酒疲労を生まない()
        {
            var b = TestBalance.Load();

            foreach (var t in b.Bottles.NonAlcoholic)
                Assert.AreEqual(0, t.FatigueTier, "ノンアルコールの fatigueTier は 0");

            foreach (var t in b.Bottles.Alcoholic)
                Assert.Greater(t.FatigueTier, 0, "アルコールの fatigueTier は 1 以上");
        }

        [Test]
        public void 疲労限界は五段階で単調増加する()
        {
            var b = TestBalance.Load();

            Assert.AreEqual(5, b.Fatigue.TierCount);
            CollectionAssert.AreEqual(
                new[] { 14.0, 20.0, 27.0, 35.0, 45.0 },
                b.Fatigue.TierLimits.ToArray());

            Assert.AreEqual(14.0, b.Fatigue.LimitOf(1));
            Assert.AreEqual(45.0, b.Fatigue.LimitOf(5));
        }

        [Test]
        public void 雨は固定客を減らさず台風は減らす()
        {
            var b = TestBalance.Load();

            // 設計書 4.1: 係数が 2 つに分かれていることの確認。
            Assert.AreEqual(1.0, b.Weather[WeatherNames.Rain].StormFactor,
                "雨で固定客が減ってはいけない");
            Assert.Less(b.Weather[WeatherNames.Rain].FameFactor, 0.5,
                "雨では知名度由来の新規客が激減する");

            Assert.Less(b.Weather[WeatherNames.Storm].StormFactor, 0.5,
                "台風では固定客を含めて客が来ない");
            Assert.AreEqual(2.0, b.Weather[WeatherNames.Storm].FatigueMul);
        }

        [Test]
        public void tierPower_の重みの合計が一になる()
        {
            var b = TestBalance.Load();

            Assert.AreEqual(1.0,
                b.Bottles.TierPowerTalkWeight + b.Bottles.TierPowerAlcoholWeight, 1e-9);
            Assert.Greater(b.Bottles.TierPowerAlcoholWeight, 0,
                "酒耐性の重みが 0 だと 20 歳の覚醒が起きない");
        }

        // ---- 検査が壊れた入力を捕まえること -------------------------------

        static string Mutate(string original, string from, string to)
        {
            StringAssert.Contains(from, original, "置換対象が balance.json に無い");
            return original.Replace(from, to);
        }

        static void AssertRejects(string json, string expectedFragment)
        {
            var e = Assert.Throws<BalanceException>(() => BalanceLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains(expectedFragment)),
                $"「{expectedFragment}」を含む指摘が無い。実際: {string.Join(" / ", e.Problems)}");
        }

        [Test]
        public void 原価が売価以上なら弾く()
        {
            // チェキの原価 120 → 9999。売価 1600 を上回る。
            string json = Mutate(TestBalance.RawJson(),
                @"""costYen"": 120", @"""costYen"": 9999");

            AssertRejects(json, "原価が売価以上");
        }

        [Test]
        public void ボトルの段階が価格順でなければ弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""priceYen"": 70000", @"""priceYen"": 30000");

            AssertRejects(json, "段階が価格順に並んでいない");
        }

        [Test]
        public void ボトルの解禁が逆順なら弾く()
        {
            // アルコール段階 5 の解禁を 0.75 → 0.20 に。直前の段階 4 (0.60) を下回る。
            string json = Mutate(TestBalance.RawJson(),
                @"""unlock"": 0.75", @"""unlock"": 0.20");

            AssertRejects(json, "高い段階ほど解禁が遅い");
        }

        [Test]
        public void ノンアルコールに酒疲労を付けたら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""unlock"": 0.95, ""fatigueTier"": 0",
                @"""unlock"": 0.95, ""fatigueTier"": 2");

            AssertRejects(json, "ノンアルコールは酒疲労を生まない");
        }

        [Test]
        public void 雨で固定客が減る設定なら弾く()
        {
            // fameFactor 0.25 は雨だけが持つ値。
            string json = Mutate(TestBalance.RawJson(),
                @"""fameFactor"": 0.25, ""stormFactor"": 1.0",
                @"""fameFactor"": 0.25, ""stormFactor"": 0.6");

            AssertRejects(json, "雨で固定客が減ってしまう");
        }

        [Test]
        public void 疲労限界が単調増加でなければ弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""tierLimits"": [14.0, 20.0, 27.0, 35.0, 45.0]",
                @"""tierLimits"": [14.0, 20.0, 19.0, 35.0, 45.0]");

            AssertRejects(json, "単調増加でない");
        }

        [Test]
        public void 信頼度の軽減が一以上なら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""relief"": 0.35",
                @"""relief"": 1.0");

            AssertRejects(json, "trust.relief");
        }

        [Test]
        public void 酒耐性の重みが零なら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""tierPowerAlcoholWeight"": 0.4",
                @"""tierPowerAlcoholWeight"": 0.0");

            AssertRejects(json, "20 歳の覚醒が起きなくなる");
        }

        [Test]
        public void 金額が小数なら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""weeklyFixedCostYen"": 93000",
                @"""weeklyFixedCostYen"": 93000.5");

            AssertRejects(json, "金額は整数の円で扱う");
        }

        [Test]
        public void 求人の費用と質が逆転していたら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""capMean"": 75.0", @"""capMean"": 40.0");

            AssertRejects(json, "高い求人費が損になる");
        }

        [Test]
        public void 疲労_Tier_の重みの数が限界値の数と違えば弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""staminaTierWeights"": [1.0, 2.0, 3.0, 2.0, 1.0]",
                @"""staminaTierWeights"": [1.0, 2.0, 3.0]");

            AssertRejects(json, "fatigue.tierLimits の数");
        }

        [Test]
        public void スキーマ版が違えば弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""schemaVersion"": 3",
                @"""schemaVersion"": 99");

            AssertRejects(json, "schemaVersion");
        }

        [Test]
        public void 外見のパーツ数が零なら弾く()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""hairFrontVariants"": 5", @"""hairFrontVariants"": 0");

            AssertRejects(json, "appearance.hairFrontVariants");
        }

        [Test]
        public void アクセサリは零でもよい()
        {
            string json = Mutate(TestBalance.RawJson(),
                @"""accessoryVariants"": 4", @"""accessoryVariants"": 0");

            Assert.DoesNotThrow(() => BalanceLoader.Load(json),
                "アクセサリ無しだけの構成は成立する");
        }

        [Test]
        public void 壊れた_JSON_は_BalanceException_になる()
        {
            AssertRejects("{ this is not json", "JSON として読めない");
        }
    }
}
