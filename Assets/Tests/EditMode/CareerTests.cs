using System;
using System.Collections.Generic;
using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>段階 4 の必須テスト（T-13・T-27〜T-43 のうち卒業・OB に関わるもの）。</summary>
    public class CareerTests
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

        Cast MakeCast(string id, double morale, int age, int hiredWeek = 0, double stats = 50)
        {
            var c = new Cast
            {
                Id = id, StageName = id, Age = age,
                StaminaTier = 5, HiredWeek = hiredWeek, ScandalResist = 50,
            };
            c.Current = Stats.Uniform(stats);
            c.Current.Morale = morale;
            c.Cap = Stats.Uniform(100);
            c.GrowthRate = Stats.Uniform(1.0);
            return c;
        }

        // ---- 読み込みと検査 -------------------------------------------------

        [Test]
        public void 実物の_careers_json_が読めて検査を通る()
        {
            Assert.AreEqual(CareerLoader.SupportedSchemaVersion, careers.SchemaVersion);
            Assert.AreEqual(10, careers.Careers.Count, "企画書の進路は 10 種");
        }

        [Test]
        public void 企画書の進路十種がすべて揃っている()
        {
            foreach (CareerKind kind in Enum.GetValues(typeof(CareerKind)))
                Assert.IsNotNull(careers.Find(kind), $"進路 {kind} が無い");
        }

        [Test]
        public void 進路が一つでも欠けていたら弾く()
        {
            // 玉の輿婚の id を別物にすると、その進路が欠けた状態になる。
            string json = TestCareers.RawJson()
                .Replace(@"""id"": ""marriedRich""", @"""id"": ""somethingElse""");

            var e = Assert.Throws<CareerException>(() => CareerLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains("MarriedRich")),
                $"欠けた進路が指摘されない。実際: {string.Join(" / ", e.Problems)}");
        }

        [Test]
        public void 知らない効果の種類なら弾く()
        {
            string json = TestCareers.RawJson()
                .Replace(@"""kind"": ""looksBonus""", @"""kind"": ""teleport""");

            var e = Assert.Throws<CareerException>(() => CareerLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains("知らない効果")));
        }

        [Test]
        public void 原価が下がらない設定なら弾く()
        {
            string json = TestCareers.RawJson()
                .Replace(@"""kind"": ""drinkCost"", ""value"": 0.88",
                         @"""kind"": ""drinkCost"", ""value"": 1.2");

            var e = Assert.Throws<CareerException>(() => CareerLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains("原価が下がらない")));
        }

        [Test]
        public void 重みが空の進路があれば弾く()
        {
            string json = TestCareers.RawJson()
                .Replace(@"""weights"": { ""looks"": 1.4 }", @"""weights"": { }");

            var e = Assert.Throws<CareerException>(() => CareerLoader.Load(json));
            Assert.IsTrue(e.Problems.Any(p => p.Contains("絶対に選ばれない")));
        }

        // ---- 卒業条件（U-08） -----------------------------------------------

        [Test]
        public void 在籍が足りなければ卒業しない()
        {
            var c = MakeCast("c1", morale: 90, age: 26, hiredWeek: 0);
            int justShort = careers.Graduation.MinWeeks - 1;

            Assert.IsFalse(GraduationRules.CanGraduate(c, justShort, balance, careers));
            Assert.IsTrue(GraduationRules.CanGraduate(
                c, careers.Graduation.MinWeeks, balance, careers));
        }

        [Test]
        public void T13_モラルが閾値未満なら卒業しない()
        {
            // 不変条件 16。モラルを「最重要ステータス」たらしめている分岐。
            double threshold = careers.Graduation.MinMorale;
            int weeks = careers.Graduation.MinWeeks;

            var low = MakeCast("low", morale: threshold - 1, age: 26);
            var high = MakeCast("high", morale: threshold, age: 26);

            Assert.IsFalse(GraduationRules.CanGraduate(low, weeks, balance, careers));
            Assert.IsTrue(GraduationRules.CanGraduate(high, weeks, balance, careers));
        }

        [Test]
        public void T13b_モラル不足で辞めても_OB_効果は残らない()
        {
            var s = new GameState(balance, eggs, careers, 1);
            s.Shop.CashYen = 100_000_000;

            // モラルは低いが在籍は長い。疲労限界を超えて離脱させる。
            var c = MakeCast("c1", morale: 10, age: 26, hiredWeek: 0, stats: 20);
            c.Fatigue = c.FatigueLimit(balance) + 100;
            s.Shop.Roster.Add(c);

            s.Week = new GameWeek(careers.Graduation.MinWeeks);
            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            var r = GameFlow.RunWeek(s, new Shift());

            Assert.IsEmpty(r.Graduations, "モラル不足なのに卒業している");
            Assert.AreEqual(1, r.Departures.Count, "離脱していない");
            Assert.IsEmpty(s.Shop.Alumni, "OB が残ってしまっている（不変条件 16）");
        }

        [Test]
        public void 年齢が足りなくても上限到達なら卒業する()
        {
            int weeks = careers.Graduation.MinWeeks;

            var young = MakeCast("young", morale: 90, age: 20, stats: 50);
            Assert.IsFalse(GraduationRules.CanGraduate(young, weeks, balance, careers),
                "年齢も上限到達も満たしていないのに卒業する");

            var maxed = MakeCast("maxed", morale: 90, age: 20, stats: 50);
            maxed.Current = Stats.Uniform(100);   // 全パラメータが上限
            Assert.IsTrue(GraduationRules.CanGraduate(maxed, weeks, balance, careers),
                "上限に届いているのに卒業しない");
        }

        [Test]
        public void 卒業すると固定客を失い_OB_になる()
        {
            var s = new GameState(balance, eggs, careers, 1);
            s.Shop.CashYen = 100_000_000;

            var c = MakeCast("c1", morale: 95, age: 26, hiredWeek: 0, stats: 60);
            s.Shop.Roster.Add(c);

            s.Week = new GameWeek(careers.Graduation.MinWeeks);
            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            var r = GameFlow.RunWeek(s, new Shift());

            Assert.AreEqual(1, r.Graduations.Count);
            Assert.AreEqual(60.0, r.Graduations[0].LostRegulars, 1e-9);
            Assert.AreEqual(1, s.Shop.Alumni.Count);
            Assert.IsEmpty(s.Shop.Roster);
            Assert.IsNotEmpty(r.Graduations[0].CareerName);
        }

        [Test]
        public void 疲労限界を超えていても卒業が優先される()
        {
            // 設計書 3.3 の順序を入れ替えた理由。2 年勤めてモラルも高い相手を
            // 「飛んだ」扱いにするのは不自然。
            var s = new GameState(balance, eggs, careers, 1);
            s.Shop.CashYen = 100_000_000;

            var c = MakeCast("c1", morale: 95, age: 26, hiredWeek: 0, stats: 60);
            c.Fatigue = c.FatigueLimit(balance) + 100;
            s.Shop.Roster.Add(c);

            s.Week = new GameWeek(careers.Graduation.MinWeeks);
            GameFlow.BeginWeek(s);
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
            var r = GameFlow.RunWeek(s, new Shift());

            Assert.AreEqual(1, r.Graduations.Count, "卒業していない");
            Assert.IsEmpty(r.Departures, "卒業したのに離脱として数えられている");
        }

        // ---- 進路の決まり方 -------------------------------------------------

        [Test]
        public void 育て方が進路に反映される()
        {
            // 要件 6.1: プレイヤーが「どう育てたか」が進路に出る。
            CareerKind Dominant(StatKind kind)
            {
                var c = MakeCast("c1", morale: 80, age: 26, stats: 5);
                c.Current[kind] = 100;
                c.Current.Morale = 80;

                var counts = new Dictionary<CareerKind, int>();
                var rng = new GameRandom(99);

                for (int i = 0; i < 2000; i++)
                {
                    var pick = GraduationRules.PickCareer(c, balance, careers, rng);
                    counts[pick] = counts.TryGetValue(pick, out int n) ? n + 1 : 1;
                }

                var best = CareerKind.Idol;
                int bestCount = -1;
                foreach (var kv in counts)
                    if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }

                TestContext.WriteLine($"{kind} を伸ばすと最多は {best}（{bestCount}/2000）");
                return best;
            }

            Assert.AreEqual(CareerKind.Distillery, Dominant(StatKind.Alcohol),
                "酒耐性を伸ばしたのに酒造メーカーが最多でない");
            Assert.AreEqual(CareerKind.CostumeDesigner, Dominant(StatKind.Looks),
                "ルックスを伸ばしたのに衣装デザイナーが最多でない");
            Assert.AreEqual(CareerKind.WebMarketer, Dominant(StatKind.Fame),
                "知名度を伸ばしたのに Web マーケターが最多でない");
        }

        [Test]
        public void 全パラメータが零でも進路は決まる()
        {
            var c = MakeCast("c1", morale: 0, age: 26, stats: 0);
            var kind = GraduationRules.PickCareer(c, balance, careers, new GameRandom(1));
            Assert.AreEqual(CareerKind.FamilyBusiness, kind);
        }

        // ---- OB 効果 --------------------------------------------------------

        Alumnus Alum(CareerKind kind, string id = "a") =>
            new Alumnus { Id = id, StageName = id, Career = kind };

        [Test]
        public void OB_がいなければ効果は素のまま()
        {
            var e = AlumniEffects.From(new List<Alumnus>(), careers);

            Assert.AreEqual(1.0, e.DrinkCostMultiplier);
            Assert.AreEqual(0.0, e.LooksBonus);
            Assert.AreEqual(1.0, e.CareRecoverMultiplier);
            Assert.AreEqual(0.0, e.WeatherRelief);
            Assert.AreEqual(0.0, e.RecruitCapBonus);
        }

        [Test]
        public void 飲料卸売業者でドリンクの原価が下がる()
        {
            var e = AlumniEffects.From(
                new List<Alumnus> { Alum(CareerKind.DrinkWholesaler) }, careers);

            long plain = ManagerActions.UnitCostYen(balance, StockKey.Alcohol);
            long cheap = ManagerActions.UnitCostYen(balance, StockKey.Alcohol, e);

            Assert.Less(cheap, plain);

            // チェキは対象外。
            Assert.AreEqual(
                ManagerActions.UnitCostYen(balance, StockKey.ChekiFilm),
                ManagerActions.UnitCostYen(balance, StockKey.ChekiFilm, e));
        }

        [Test]
        public void 衣装デザイナーでルックス倍率が上がる()
        {
            var c = MakeCast("c1", morale: 50, age: 25, stats: 0);
            var e = AlumniEffects.From(
                new List<Alumnus> { Alum(CareerKind.CostumeDesigner) }, careers);

            Assert.Greater(VisitorRules.LooksMultiplier(c, balance, e),
                           VisitorRules.LooksMultiplier(c, balance));
        }

        [Test]
        public void 心理カウンセラーで疲労回復量が増える()
        {
            double Recover(CareerSet set, List<Alumnus> alumni)
            {
                var s = new GameState(balance, eggs, set, 1);
                foreach (var a in alumni) s.Shop.Alumni.Add(a);
                s.RefreshAlumniEffects();

                var c = MakeCast("c1", morale: 50, age: 25);
                c.Fatigue = 20;
                s.Shop.Roster.Add(c);
                s.Shop.ManagerAp = 100;

                ManagerActions.Care(s, c);
                return 20 - c.Fatigue;
            }

            double plain = Recover(careers, new List<Alumnus>());
            double boosted = Recover(careers,
                new List<Alumnus> { Alum(CareerKind.Counselor) });

            Assert.Greater(boosted, plain);
        }

        [Test]
        public void Web_マーケターで悪天候の知名度ペナルティが軽くなる()
        {
            int Visitors(List<Alumnus> alumni)
            {
                var s = new GameState(balance, eggs, careers, 55);
                foreach (var a in alumni) s.Shop.Alumni.Add(a);
                s.RefreshAlumniEffects();
                s.Shop.CashYen = 100_000_000;

                var c = MakeCast("c1", morale: 50, age: 25, stats: 0);
                c.Current.Fame = 100;   // 知名度だけを持たせる
                s.Shop.Roster.Add(c);

                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Rain;
                return GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance))
                    .TotalVisitors;
            }

            int plain = Visitors(new List<Alumnus>());
            int relieved = Visitors(new List<Alumnus> { Alum(CareerKind.WebMarketer) });

            Assert.Greater(relieved, plain, "雨のペナルティが軽くなっていない");
        }

        [Test]
        public void Web_マーケターは台風の全体減には効かない()
        {
            // 要件 6.1: 軽減するのは知名度ペナルティだけ。
            // stormFactor に効くと「台風でも客が来る」ことになり、
            // 天候の係数を 2 つに分けた意味が崩れる。
            var e = AlumniEffects.From(
                new List<Alumnus> { Alum(CareerKind.WebMarketer) }, careers);

            var storm = balance.Weather[WeatherNames.Storm];
            Assert.AreEqual(0.1, storm.StormFactor, 1e-12,
                "OB 効果が stormFactor を書き換えていない（定数なので当然だが念のため）");
            Assert.Greater(e.WeatherRelief, 0);
        }

        [Test]
        public void スカウトマンで候補の上限が上がる()
        {
            var e = AlumniEffects.From(
                new List<Alumnus> { Alum(CareerKind.Scout) }, careers);

            double Mean(double bonus)
            {
                double sum = 0;
                int n = 0;
                for (ulong seed = 1; seed <= 300; seed++)
                {
                    var a = CastFactory.GenerateApplicant(
                        new GameRandom(seed), balance, 0, 1, bonus);
                    foreach (var kind in Stats.AllKinds) { sum += a.Cap[kind]; n++; }
                }
                return sum / n;
            }

            Assert.Greater(Mean(e.RecruitCapBonus), Mean(0));
        }

        [Test]
        public void 出版関係者から特訓本が届き_AP_を使わずに特訓できる()
        {
            var s = new GameState(balance, eggs, careers, 7);
            // 期待値 0.5 なので、確実に届くまで OB を積む。
            s.Shop.Alumni.Add(Alum(CareerKind.Publisher, "a1"));
            s.Shop.Alumni.Add(Alum(CareerKind.Publisher, "a2"));
            s.RefreshAlumniEffects();

            var c = MakeCast("c1", morale: 50, age: 25, stats: 10);
            s.Shop.Roster.Add(c);

            GameFlow.BeginWeek(s);
            Assert.Greater(s.TrainingBooks, 0, "特訓本が届いていない");

            int apBefore = s.Shop.ManagerAp;
            double talkBefore = c.Current.Talk;

            Assert.AreEqual(ActionResult.Ok,
                ManagerActions.TrainWithBook(s, c, StatKind.Talk));

            Assert.AreEqual(apBefore, s.Shop.ManagerAp, "AP を消費している");
            Assert.Greater(c.Current.Talk, talkBefore, "伸びていない");
        }

        [Test]
        public void 特訓本が無ければ使えない()
        {
            var s = new GameState(balance, eggs, careers, 1);
            var c = MakeCast("c1", morale: 50, age: 25);
            s.Shop.Roster.Add(c);

            Assert.AreEqual(ActionResult.NoBook,
                ManagerActions.TrainWithBook(s, c, StatKind.Talk));
        }

        [Test]
        public void 酒造メーカーから最高段階のボトルが原価零で届く()
        {
            var s = new GameState(balance, eggs, careers, 3);
            for (int i = 0; i < 4; i++)
                s.Shop.Alumni.Add(Alum(CareerKind.Distillery, "a" + i));
            s.RefreshAlumniEffects();

            int top = balance.Bottles.Alcoholic.Count - 1;
            var key = StockKey.Bottle(StockItem.BottleAlcoholic, top);

            long cashBefore = s.Shop.CashYen;
            GameFlow.BeginWeek(s);

            Assert.Greater(s.Shop.Stock[key], 0, "最高段階のボトルが届いていない");
            Assert.AreEqual(cashBefore, s.Shop.CashYen, "差し入れに原価が掛かっている");
        }

        [Test]
        public void 実家の家業から期間限定商品が届く()
        {
            var s = new GameState(balance, eggs, careers, 3);
            s.Shop.Alumni.Add(Alum(CareerKind.FamilyBusiness));
            s.RefreshAlumniEffects();

            long cashBefore = s.Shop.CashYen;
            GameFlow.BeginWeek(s);

            Assert.Greater(s.Shop.Stock[StockKey.Limited], 0);
            Assert.AreEqual(cashBefore, s.Shop.CashYen, "差し入れに原価が掛かっている");
        }

        [Test]
        public void 玉の輿婚のご祝儀が決算に入る()
        {
            var s = new GameState(balance, eggs, careers, 11);
            // 確率 4% なので、当たるまで週を回す。
            for (int i = 0; i < 6; i++)
                s.Shop.Alumni.Add(Alum(CareerKind.MarriedRich, "a" + i));
            s.RefreshAlumniEffects();
            s.Shop.CashYen = 100_000_000;

            bool sawGift = false;
            for (int w = 0; w < 200 && !sawGift; w++)
            {
                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                var r = GameFlow.RunWeek(s, new Shift());

                if (r.CongratulatoryYen > 0)
                {
                    sawGift = true;
                    Assert.AreEqual(
                        r.CashBeforeYen + r.CongratulatoryYen - r.WageYen - r.OverheadYen,
                        r.CashAfterYen, "ご祝儀が資金に入っていない");
                }
            }

            Assert.IsTrue(sawGift, "200 週回してもご祝儀が一度も来ない");
        }

        [Test]
        public void アイドルの一日店長で週の売上が上乗せされる()
        {
            var s = new GameState(balance, eggs, careers, 21);
            for (int i = 0; i < 5; i++)
                s.Shop.Alumni.Add(Alum(CareerKind.Idol, "a" + i));
            s.RefreshAlumniEffects();
            s.Shop.CashYen = 100_000_000;

            var c = MakeCast("c1", morale: 50, age: 25, stats: 40);
            s.Shop.Roster.Add(c);
            s.Shop.Stock.Add(StockKey.Alcohol, 1000000);
            s.Shop.Stock.Add(StockKey.ChekiFilm, 1000000);

            bool sawEvent = false;
            for (int w = 0; w < 200 && !sawEvent; w++)
            {
                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 3, balance));
                c.Fatigue = 0;

                if (r.Gifts.OneDayManager)
                {
                    sawEvent = true;
                    Assert.Greater(r.OneDayManagerBonusYen, 0L, "上乗せが無い");
                }
            }

            Assert.IsTrue(sawEvent, "200 週回しても 1 日店長が一度も来ない");
        }

        [Test]
        public void OB_の効果には上限がある()
        {
            // 10 年で何人も輩出できるため、上限が無いと終盤で暴走する。
            var many = new List<Alumnus>();
            for (int i = 0; i < 50; i++)
            {
                many.Add(Alum(CareerKind.DrinkWholesaler, "w" + i));
                many.Add(Alum(CareerKind.CostumeDesigner, "d" + i));
                many.Add(Alum(CareerKind.Counselor, "c" + i));
                many.Add(Alum(CareerKind.WebMarketer, "m" + i));
                many.Add(Alum(CareerKind.Scout, "s" + i));
                many.Add(Alum(CareerKind.Publisher, "p" + i));
            }

            var e = AlumniEffects.From(many, careers);
            var caps = careers.Caps;

            Assert.AreEqual(caps.DrinkCostMultiplierMin, e.DrinkCostMultiplier, 1e-12);
            Assert.AreEqual(caps.LooksBonusMax, e.LooksBonus, 1e-12);
            Assert.AreEqual(caps.CareRecoverMultiplierMax, e.CareRecoverMultiplier, 1e-12);
            Assert.AreEqual(caps.WeatherReliefMax, e.WeatherRelief, 1e-12);
            Assert.AreEqual(caps.RecruitCapBonusMax, e.RecruitCapBonus, 1e-12);
            Assert.AreEqual(caps.TrainingBooksPerWeekMax, e.TrainingBooksPerWeek, 1e-12);
        }

        [Test]
        public void 同じ進路の_OB_は積み上がる()
        {
            var one = AlumniEffects.From(
                new List<Alumnus> { Alum(CareerKind.CostumeDesigner, "a") }, careers);
            var two = AlumniEffects.From(
                new List<Alumnus>
                {
                    Alum(CareerKind.CostumeDesigner, "a"),
                    Alum(CareerKind.CostumeDesigner, "b"),
                }, careers);

            Assert.AreEqual(one.LooksBonus * 2, two.LooksBonus, 1e-12);
        }
    }
}
