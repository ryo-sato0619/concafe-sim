using System.IO;
using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Concafe.Tests
{
    public static class TestEggs
    {
        public static string Path =>
            System.IO.Path.Combine(Application.dataPath, "Resources", "Data", "easter_eggs.json");

        public static string RawJson() => File.ReadAllText(Path);

        public static EasterEggSet Load(BalanceData balance) =>
            EasterEggLoader.Load(RawJson(), balance.Fatigue.TierCount);
    }

    public class EasterEggLoadTests
    {
        BalanceData balance;
        EasterEggSet eggs;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
        }

        [Test]
        public void 実物の_easter_eggs_json_が読めて検査を通る()
        {
            Assert.AreEqual(EasterEggLoader.SupportedSchemaVersion, eggs.SchemaVersion);
            Assert.AreEqual(4, eggs.Eggs.Count, "ニシキ + 空き枠 3");
        }

        [Test]
        public void 空き枠は三つあって全て無効()
        {
            var disabled = eggs.Eggs.Where(e => !e.Enabled).ToArray();

            Assert.AreEqual(3, disabled.Length);
            foreach (var e in disabled)
                Assert.IsEmpty(e.Match, "空き枠は match が空のまま止めてある");
        }

        [Test]
        public void 空き枠は名前を入れても発動しない()
        {
            foreach (var e in eggs.Eggs.Where(x => !x.Enabled))
                Assert.IsNull(eggs.Find(e.DisplayName));
        }

        [Test]
        public void ニシキが表記ゆれで引ける()
        {
            Assert.AreEqual("nishiki", eggs.Find("ニシキ")?.Id);
            Assert.AreEqual("nishiki", eggs.Find("にしき")?.Id);
            Assert.AreEqual("nishiki", eggs.Find("錦")?.Id);
        }

        [Test]
        public void 前後の空白は無視される()
        {
            Assert.AreEqual("nishiki", eggs.Find("  ニシキ ")?.Id);
            Assert.AreEqual("nishiki", eggs.Find("　ニシキ　")?.Id, "全角空白も落とす");
        }

        [Test]
        public void 一致しない名前では何も起きない()
        {
            Assert.IsNull(eggs.Find("ハナコ"));
            Assert.IsNull(eggs.Find(""));
            Assert.IsNull(eggs.Find(null));
            Assert.IsNull(eggs.Find("ニシキちゃん"), "部分一致はしない");
        }

        [Test]
        public void ニシキの定義が指定どおり()
        {
            var egg = eggs.Find("ニシキ");

            Assert.AreEqual(10.0, egg.GrowthRateMultiplier.For(StatKind.Talk));
            Assert.AreEqual(10.0, egg.GrowthRateMultiplier.For(StatKind.Morale));
            Assert.AreEqual(0.0, egg.GrowthRateMultiplier.For(StatKind.Alcohol),
                "酒耐性だけは all を上書きして 0");

            Assert.AreEqual(1, egg.FixedStaminaTier);
            Assert.AreEqual(0, egg.FixedScandalResist);
            Assert.AreEqual(DepartureOverride.BurnDown, egg.OnDeparture);
            Assert.AreEqual(BurnDownCondition.Any, egg.BurnDownOn);
        }

        [Test]
        public void 放火イベントが定義されている()
        {
            var ev = eggs.FindEvent("nishiki_burn_down");

            Assert.IsNotNull(ev);
            Assert.AreEqual(GameOverReason.Arson, ev.GameOver);
            Assert.IsNotEmpty(ev.Body);
        }

        [Test]
        public void 放火イベントの本文が指定どおり()
        {
            var ev = eggs.FindEvent("nishiki_burn_down");

            Assert.AreEqual(1, ev.Body.Count);
            Assert.AreEqual(
                "精神に限界をきたしたニシキは店舗に火をつけて炎の中で姿を消した",
                ev.Body[0]);
        }

        [Test]
        public void 未定義のイベント_ID_を引いても落ちない()
        {
            Assert.IsNull(eggs.FindEvent("存在しない"));
            Assert.IsNull(eggs.FindEvent(null));
            Assert.IsNull(eggs.FindEvent(""));
        }

        // ---- 検査 ---------------------------------------------------------

        static void AssertRejects(string json, int tierCount, string fragment)
        {
            var e = Assert.Throws<EasterEggException>(
                () => EasterEggLoader.Load(json, tierCount));
            Assert.IsTrue(e.Problems.Any(p => p.Contains(fragment)),
                $"「{fragment}」を含む指摘が無い。実際: {string.Join(" / ", e.Problems)}");
        }

        [Test]
        public void 有効なのに_match_が空なら弾く()
        {
            string json = TestEggs.RawJson().Replace(
                @"""id"": ""slot_stat_bonus"",
      ""enabled"": false", @"""id"": ""slot_stat_bonus"",
      ""enabled"": true");

            AssertRejects(json, balance.Fatigue.TierCount, "絶対に発動しない");
        }

        [Test]
        public void 疲労_Tier_の指定が範囲外なら弾く()
        {
            string json = TestEggs.RawJson().Replace(
                @"""fixedStaminaTier"": 1,", @"""fixedStaminaTier"": 9,");

            AssertRejects(json, balance.Fatigue.TierCount, "fixedStaminaTier");
        }

        [Test]
        public void 知らないパラメータ名なら弾く()
        {
            string json = TestEggs.RawJson().Replace(
                @"""alcohol"": 0.0", @"""stamina"": 0.0");

            AssertRejects(json, balance.Fatigue.TierCount, "知らないパラメータ名");
        }

        [Test]
        public void 知らない_onDeparture_なら弾く()
        {
            string json = TestEggs.RawJson().Replace(
                @"""onDeparture"": ""burnDown""", @"""onDeparture"": ""explode""");

            AssertRejects(json, balance.Fatigue.TierCount, "知らない値");
        }

        [Test]
        public void 候補が重複していたら弾く()
        {
            string json = TestEggs.RawJson().Replace(
                @"""match"": [""ニシキ"", ""にしき"", ""錦""]",
                @"""match"": [""ニシキ"", ""ニシキ""]");

            AssertRejects(json, balance.Fatigue.TierCount, "重複している");
        }

        [Test]
        public void 壊れた_JSON_は_EasterEggException_になる()
        {
            AssertRejects("{ broken", balance.Fatigue.TierCount, "JSON として読めない");
        }

        [Test]
        public void 空のセットは何にも一致しない()
        {
            Assert.IsNull(EasterEggSet.Empty.Find("ニシキ"));
            Assert.IsNull(EasterEggSet.Empty.FindEvent("nishiki_burn_down"));
        }
    }

    public class NishikiTests
    {
        BalanceData balance;
        EasterEggSet eggs;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
        }

        Cast HireNishiki(ulong seed = 1)
        {
            var rng = new GameRandom(seed);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            return CastFactory.Hire(applicant, "ニシキ", balance, eggs, currentWeek: 0);
        }

        Cast HirePlain(ulong seed = 1)
        {
            var rng = new GameRandom(seed);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            return CastFactory.Hire(applicant, "ハナコ", balance, eggs, currentWeek: 0);
        }

        [Test]
        public void 成長率が全パラメータ十倍になる()
        {
            var nishiki = HireNishiki();
            var plain = HirePlain();

            foreach (var kind in Stats.AllKinds)
            {
                if (kind == StatKind.Alcohol) continue;
                Assert.AreEqual(plain.GrowthRate[kind] * 10.0, nishiki.GrowthRate[kind], 1e-9,
                    $"{kind} の成長率");
            }
        }

        [Test]
        public void 酒耐性の成長率だけが零になる()
        {
            var nishiki = HireNishiki();
            Assert.AreEqual(0.0, nishiki.GrowthRate.Alcohol);
        }

        [Test]
        public void 酒耐性は初期値のまま伸びない()
        {
            var nishiki = HireNishiki();
            double before = nishiki.Current.Alcohol;

            // 成長式（設計書 4.6）を直接当てる。
            for (int i = 0; i < 100; i++)
            {
                double gain = balance.Manager.TrainBaseGain
                            * nishiki.GrowthRate.Alcohol
                            * (1 - nishiki.Current.Alcohol / nishiki.Cap.Alcohol);
                nishiki.Current.Alcohol = System.Math.Min(
                    nishiki.Cap.Alcohol, nishiki.Current.Alcohol + gain);
            }

            Assert.AreEqual(before, nishiki.Current.Alcohol, 1e-12,
                "20 歳になっても伸びない");
        }

        [Test]
        public void 疲労限界が最低に固定される()
        {
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.AreEqual(1, HireNishiki(seed).StaminaTier);

            Assert.AreEqual(14.0, HireNishiki().FatigueLimit(balance));
        }

        [Test]
        public void スキャンダル耐性が零に固定される()
        {
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.AreEqual(0, HireNishiki(seed).ScandalResist);
        }

        [Test]
        public void 二回の特訓で上限に届く()
        {
            var nishiki = HireNishiki();
            double cap = nishiki.Cap.Talk;

            for (int i = 0; i < 2; i++)
            {
                double gain = balance.Manager.TrainBaseGain
                            * nishiki.GrowthRate.Talk
                            * (1 - nishiki.Current.Talk / cap);
                nishiki.Current.Talk = System.Math.Min(cap, nishiki.Current.Talk + gain);
            }

            Assert.That(nishiki.Current.Talk, Is.EqualTo(cap).Within(cap * 0.02),
                "設計書 14.8 の検算どおり、2 回で上限に届く");
        }

        [Test]
        public void イースターエッグの_ID_が記録される()
        {
            Assert.AreEqual("nishiki", HireNishiki().EasterEggId);
            Assert.IsNull(HirePlain().EasterEggId);
        }

        // ---- 放火イベント -------------------------------------------------

        [Test]
        public void 飛ぶ側で燃え尽きると放火する()
        {
            var nishiki = HireNishiki();
            var outcome = EasterEggRules.ResolveBurnout(nishiki, DepartureKind.RanAway, eggs);

            Assert.AreEqual(DepartureOverride.BurnDown, outcome.Override);
            Assert.AreEqual(GameOverReason.Arson, outcome.GameOver);
            Assert.AreEqual("nishiki_burn_down", outcome.EventId);
            Assert.IsTrue(outcome.EndsRun);
        }

        [Test]
        public void 転生側で燃え尽きても放火する()
        {
            // burnDownOn が any のため、市場価値が高くても燃やす。
            var nishiki = HireNishiki();
            var outcome = EasterEggRules.ResolveBurnout(nishiki, DepartureKind.Defected, eggs);

            Assert.AreEqual(DepartureOverride.BurnDown, outcome.Override);
            Assert.AreEqual(GameOverReason.Arson, outcome.GameOver);
        }

        [Test]
        public void 卒業では放火しない()
        {
            var nishiki = HireNishiki();
            var outcome = EasterEggRules.ResolveBurnout(nishiki, DepartureKind.Graduated, eggs);

            Assert.AreEqual(DepartureOverride.None, outcome.Override);
            Assert.AreEqual(GameOverReason.None, outcome.GameOver);
            Assert.IsFalse(outcome.EndsRun);
        }

        [Test]
        public void 普通のキャストは燃え尽きても放火しない()
        {
            var plain = HirePlain();

            foreach (var kind in new[] { DepartureKind.RanAway, DepartureKind.Defected })
            {
                var outcome = EasterEggRules.ResolveBurnout(plain, kind, eggs);
                Assert.AreEqual(DepartureOverride.None, outcome.Override);
                Assert.IsFalse(outcome.EndsRun);
            }
        }

        [Test]
        public void burnDownOn_を_runAway_にすると転生では燃やさない()
        {
            string json = TestEggs.RawJson().Replace(
                @"""burnDownOn"": ""any""", @"""burnDownOn"": ""runAway""");
            var alt = EasterEggLoader.Load(json, balance.Fatigue.TierCount);

            var rng = new GameRandom(1);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            var nishiki = CastFactory.Hire(applicant, "ニシキ", balance, alt, 0);

            Assert.AreEqual(DepartureOverride.BurnDown,
                EasterEggRules.ResolveBurnout(nishiki, DepartureKind.RanAway, alt).Override);
            Assert.AreEqual(DepartureOverride.None,
                EasterEggRules.ResolveBurnout(nishiki, DepartureKind.Defected, alt).Override,
                "育て上げれば穏便に移籍する");
        }

        [Test]
        public void イベントが未実装でも結末は成立しゲームは止まらない()
        {
            // events を空にしても、放火と強制終了は起きる（不変条件 14 と同じ姿勢）。
            string json = TestEggs.RawJson();
            int start = json.IndexOf(@"""events"":");
            json = json.Substring(0, start) + @"""events"": [] }";

            var alt = EasterEggLoader.Load(json, balance.Fatigue.TierCount);
            Assert.IsNull(alt.FindEvent("nishiki_burn_down"));

            var rng = new GameRandom(1);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            var nishiki = CastFactory.Hire(applicant, "ニシキ", balance, alt, 0);

            var outcome = EasterEggRules.ResolveBurnout(nishiki, DepartureKind.RanAway, alt);
            Assert.AreEqual(GameOverReason.Arson, outcome.GameOver);
        }

        [Test]
        public void 採用後に定義を書き換えても既存のキャストは変わらない()
        {
            var nishiki = HireNishiki();

            // 名前を差し替えた別定義で引いても、記録した ID で解決される。
            string json = TestEggs.RawJson().Replace(
                @"""match"": [""ニシキ"", ""にしき"", ""錦""]", @"""match"": [""別の名前""]");
            var alt = EasterEggLoader.Load(json, balance.Fatigue.TierCount);

            Assert.IsNull(alt.Find("ニシキ"));
            Assert.AreEqual("nishiki", EasterEggRules.FindEgg(nishiki, alt)?.Id);
        }
    }
}
