using System.Collections.Generic;
using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>「ニシキ」の週次専用イベント（U-14）。</summary>
    public class NishikiEventTests
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

        GameState NewShop(ulong seed = 1)
        {
            var s = new GameState(balance, eggs, careers, seed);
            s.Shop.CashYen = 1_000_000_000;
            s.Shop.CastSlots = 12;
            s.Shop.Stock.Add(StockKey.Alcohol, 1000000);
            s.Shop.Stock.Add(StockKey.SoftDrink, 1000000);
            s.Shop.Stock.Add(StockKey.ChekiFilm, 1000000);
            return s;
        }

        Cast AddNishiki(GameState s, ulong seed = 5)
        {
            var applicant = CastFactory.GenerateApplicant(new GameRandom(seed), balance, 0, 1);
            var c = CastFactory.Hire(applicant, "ニシキ", balance, eggs, 0);
            c.Age = 25;                      // 酒を出せる年齢にする
            c.Current = Stats.Uniform(50);
            c.Cap = Stats.Uniform(100);
            s.Shop.Roster.Add(c);
            return c;
        }

        Cast AddPlain(GameState s, string id)
        {
            var c = new Cast
            {
                Id = id, StageName = id, Age = 25,
                StaminaTier = 5, ScandalResist = 100,
            };
            c.Current = Stats.Uniform(50);
            c.Cap = Stats.Uniform(100);
            c.GrowthRate = Stats.Uniform(1.0);
            s.Shop.Roster.Add(c);
            return c;
        }

        // ---- 定義 -----------------------------------------------------------

        [Test]
        public void ニシキに週次イベントが三つ定義されている()
        {
            var egg = eggs.Find("ニシキ");

            Assert.AreEqual(3, egg.WeeklyEvents.Count);
            CollectionAssert.AreEquivalent(
                new[] { "nishiki_slump", "nishiki_sns", "nishiki_allergy" },
                egg.WeeklyEvents.Select(w => w.EventId).ToArray());
        }

        [Test]
        public void 三つのイベントの本文が指定どおり()
        {
            Assert.AreEqual("メンタルが落ちて仕事ができない状態だ",
                eggs.FindEvent("nishiki_slump").Body[0]);

            Assert.AreEqual("ニシキのSNSでの投稿をきっかけに店舗全体のモラルが下がった",
                eggs.FindEvent("nishiki_sns").Body[0]);

            Assert.AreEqual(
                "ニシキは許容量を超えたアルコールを摂取してしまいアレルギー反応がでて2日間出勤できなくなった",
                eggs.FindEvent("nishiki_allergy").Body[0]);
        }

        [Test]
        public void 欠勤イベントは二日_SNS_はモラル低下()
        {
            var slump = eggs.FindEvent("nishiki_slump");
            Assert.AreEqual(EggEventEffect.Absence, slump.Effect);
            Assert.AreEqual(2, slump.Days);

            var allergy = eggs.FindEvent("nishiki_allergy");
            Assert.AreEqual(EggEventEffect.Absence, allergy.Effect);
            Assert.AreEqual(2, allergy.Days);

            var sns = eggs.FindEvent("nishiki_sns");
            Assert.AreEqual(EggEventEffect.ShopMorale, sns.Effect);
            Assert.Greater(sns.Value, 0);
        }

        [Test]
        public void アレルギーだけが酒を飲んだ週を条件にしている()
        {
            var egg = eggs.Find("ニシキ");

            foreach (var w in egg.WeeklyEvents)
                Assert.AreEqual(w.EventId == "nishiki_allergy", w.RequiresAlcoholServed,
                    $"{w.EventId} の条件が合わない");
        }

        // ---- 発生と効果 -----------------------------------------------------

        [Test]
        public void 週頭にイベントが起きる()
        {
            var s = NewShop(seed: 12);
            AddNishiki(s);

            bool sawAny = false;
            for (int w = 0; w < 200 && !sawAny; w++)
            {
                GameFlow.BeginWeek(s);
                if (s.PendingEggEvents.Count > 0) sawAny = true;

                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));

                foreach (var c in s.Shop.Roster) c.Fatigue = 0;
                if (s.Shop.Roster.Count == 0) break;
            }

            Assert.IsTrue(sawAny, "200 週回しても専用イベントが一度も起きない");
        }

        [Test]
        public void 欠勤すると出勤日数が減り給料も減る()
        {
            var s = NewShop(seed: 3);
            var nishiki = AddNishiki(s);

            // 欠勤を強制する。
            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            nishiki.AbsentDays = 2;

            var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));
            var stats = r.CastStats[0];

            Assert.AreEqual(2, stats.WorkDays, "4 日入れて 2 日欠勤なら 2 日");
            Assert.AreEqual(2, stats.AbsentDays);

            // 欠勤なしの週と比べる。
            var s2 = NewShop(seed: 3);
            var n2 = AddNishiki(s2);
            GameFlow.BeginWeek(s2);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s2.Weather[d] = Weather.Sunny;
            n2.AbsentDays = 0;
            var r2 = GameFlow.RunWeek(s2, Shift.FullDays(s2.Shop.Roster, 4, balance));

            Assert.Less(stats.WageYen, r2.CastStats[0].WageYen, "欠勤しても給料が減っていない");
            Assert.Less(r.RevenueYen, r2.RevenueYen, "欠勤しても売上が減っていない");
        }

        [Test]
        public void 欠勤した日は休みとして疲労が回復する()
        {
            var s = NewShop(seed: 3);
            var nishiki = AddNishiki(s);
            nishiki.Fatigue = 10;

            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            nishiki.AbsentDays = Shift.DaysPerWeek;   // 全日欠勤

            GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance));

            Assert.AreEqual(10 - balance.Fatigue.RestRecover * 7, nishiki.Fatigue, 1e-9,
                "全日欠勤なのに回復していない");
        }

        [Test]
        public void SNS_イベントは店舗全員のモラルを下げる()
        {
            var s = NewShop(seed: 4);
            var nishiki = AddNishiki(s);
            var other = AddPlain(s, "c2");

            double sns = eggs.FindEvent("nishiki_sns").Value;

            double nishikiBefore = nishiki.Current.Morale;
            double otherBefore = other.Current.Morale;

            // 起きるまで週頭を回す。
            EggEventRecord record = null;
            for (int w = 0; w < 400 && record == null; w++)
            {
                s.Week = new GameWeek(w);
                GameFlow.BeginWeek(s);
                record = s.PendingEggEvents.Find(e => e.EventId == "nishiki_sns");
            }

            Assert.IsNotNull(record, "400 週回しても SNS イベントが起きない");
            Assert.Greater(record.ShopMoraleLost, 0);

            // 自分も含めて全員が下がっている。
            Assert.Less(nishiki.Current.Morale, nishikiBefore);
            Assert.Less(other.Current.Morale, otherBefore);
            Assert.AreEqual(sns, otherBefore - other.Current.Morale, 1e-9);
        }

        [Test]
        public void アレルギーは酒を飲んでいない週には起きない()
        {
            var s = NewShop(seed: 6);
            var nishiki = AddNishiki(s);

            // 先週は一滴も飲んでいない。
            nishiki.LastWeekAlcoholDrinks = 0;

            for (int w = 0; w < 400; w++)
            {
                s.Week = new GameWeek(w);
                GameFlow.BeginWeek(s);

                Assert.IsNull(s.PendingEggEvents.Find(e => e.EventId == "nishiki_allergy"),
                    "酒を飲んでいないのにアレルギーが起きた");

                nishiki.LastWeekAlcoholDrinks = 0;
            }
        }

        [Test]
        public void アレルギーは酒を飲んだ週には起きうる()
        {
            var s = NewShop(seed: 6);
            var nishiki = AddNishiki(s);

            bool sawAllergy = false;
            for (int w = 0; w < 400 && !sawAllergy; w++)
            {
                s.Week = new GameWeek(w);
                nishiki.LastWeekAlcoholDrinks = 20;
                GameFlow.BeginWeek(s);

                if (s.PendingEggEvents.Exists(e => e.EventId == "nishiki_allergy"))
                    sawAllergy = true;
            }

            Assert.IsTrue(sawAllergy, "400 週回してもアレルギーが起きない");
        }

        [Test]
        public void 普通のキャストには専用イベントが起きない()
        {
            var s = NewShop(seed: 8);
            var plain = AddPlain(s, "c1");
            plain.LastWeekAlcoholDrinks = 20;

            for (int w = 0; w < 300; w++)
            {
                s.Week = new GameWeek(w);
                GameFlow.BeginWeek(s);
                Assert.IsEmpty(s.PendingEggEvents);
                Assert.AreEqual(0, plain.AbsentDays);
            }
        }

        [Test]
        public void 欠勤日数は週ごとに消える()
        {
            var s = NewShop(seed: 3);
            var nishiki = AddNishiki(s);

            nishiki.AbsentDays = 5;
            s.Week = new GameWeek(1);
            GameFlow.BeginWeek(s);

            // BeginWeek で必ずリセットされ、その週の抽選だけが乗る。
            Assert.LessOrEqual(nishiki.AbsentDays, 4,
                "前の週の欠勤が持ち越されている");
        }

        [Test]
        public void 未実装のイベント_ID_を参照してもゲームは止まらない()
        {
            string json = TestEggs.RawJson()
                .Replace(@"""eventId"": ""nishiki_slump""", @"""eventId"": ""does_not_exist""");
            var alt = EasterEggLoader.Load(json, balance.Fatigue.TierCount);

            var s = new GameState(balance, alt, careers, 3);
            s.Shop.CashYen = 100_000_000;
            var applicant = CastFactory.GenerateApplicant(new GameRandom(5), balance, 0, 1);
            s.Shop.Roster.Add(CastFactory.Hire(applicant, "ニシキ", balance, alt, 0));

            Assert.DoesNotThrow(() =>
            {
                for (int w = 0; w < 100; w++)
                {
                    s.Week = new GameWeek(w);
                    GameFlow.BeginWeek(s);
                }
            });
        }
    }
}
