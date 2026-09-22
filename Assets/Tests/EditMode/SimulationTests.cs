using System.Collections.Generic;
using System.Text;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 段階 3 の必須テスト（設計書 10.1・10.3）。
    ///
    /// 来客数を決め打ちにするため、テスト用のキャストは知名度 0 で作る。
    /// 固定客は floor(集客 × regularBase × stormFactor) で決まるので、
    /// 知名度が 0 なら新規客の抽選が走らず、客数が完全に決定的になる。
    /// </summary>
    public class SimulationTests
    {
        BalanceData balance;
        EasterEggSet eggs;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
        }

        // ---- 組み立ての補助 -------------------------------------------------

        GameState NewState(ulong seed = 1)
        {
            var s = new GameState(balance, eggs, seed);
            s.Shop.CastSlots = balance.Shop.MaxCastSlots;
            return s;
        }

        Cast MakeCast(string id, string name, int age,
                      double regulars = 100, double fame = 0, double talk = 50,
                      double alcohol = 0, double looks = 0, double morale = 50,
                      int tier = 5, double cap = 100, int birthWeek = 0)
        {
            var c = new Cast
            {
                Id = id,
                StageName = name,
                Age = age,
                BirthWeekOfYear = birthWeek,
                StaminaTier = tier,
                ScandalResist = 50,
            };
            c.Current = new Stats
            {
                Regulars = regulars, Fame = fame, Talk = talk,
                Alcohol = alcohol, Looks = looks, Morale = morale,
            };
            c.Cap = Stats.Uniform(cap);
            c.GrowthRate = Stats.Uniform(1.0);
            return c;
        }

        static void FillStock(Shop shop, int each = 100000)
        {
            shop.Stock.Add(StockKey.SoftDrink, each);
            shop.Stock.Add(StockKey.Alcohol, each);
            shop.Stock.Add(StockKey.ChekiFilm, each);

            for (int i = 0; i < 6; i++)
                shop.Stock.Add(StockKey.Bottle(StockItem.BottleAlcoholic, i), each);
            for (int i = 0; i < 3; i++)
                shop.Stock.Add(StockKey.Bottle(StockItem.BottleNonAlcoholic, i), each);
        }

        /// <summary>天候を固定する。BeginWeek の後に呼ぶ。</summary>
        static void ForceWeather(GameState s, Weather w)
        {
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = w;
        }

        /// <summary>
        /// 全休の 1 週を回したあと、週末の離脱判定の時点でちょうど target になるよう
        /// 疲労を仕込む。休業 7 日ぶんの回復を見込んで積んでおく。
        /// </summary>
        void SetFatigueForWeekEnd(Cast cast, double target)
        {
            cast.Fatigue = target + balance.Fatigue.RestRecover * Shift.DaysPerWeek;
        }

        WeekResult RunOneWeek(GameState s, Weather weather, int workDays = 7)
        {
            GameFlow.BeginWeek(s);
            ForceWeather(s, weather);
            return GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, workDays, balance));
        }

        // ---- T-01 / T-21: 未成年と酒 ----------------------------------------

        [Test]
        public void T01_未成年の担当客にアルコールボトルは出ない()
        {
            var s = NewState();
            // トークと酒耐性を最大にしても、未成年ならアルコール段階には触れない。
            s.Shop.Roster.Add(MakeCast("c1", "未成年", 19, talk: 100, alcohol: 100));
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            var before = s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 0)];
            for (int w = 0; w < 8; w++) RunOneWeek(s, Weather.Sunny);

            for (int i = 0; i < 6; i++)
                Assert.AreEqual(before, s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, i)],
                    $"アルコールボトル段階 {i + 1} が減っている");
        }

        [Test]
        public void T21_未成年のボトル注文はノンアルコールに変換される()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "未成年", 19, talk: 100));
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.Bottle(StockItem.BottleNonAlcoholic, 0)];
            for (int w = 0; w < 8; w++) RunOneWeek(s, Weather.Sunny);
            int after = s.Shop.Stock[StockKey.Bottle(StockItem.BottleNonAlcoholic, 0)];

            Assert.Less(after, before, "ノンアルコールボトルが売れていない");
        }

        [Test]
        public void T02_未成年のキャストドリンクはソフトドリンクに変換される()
        {
            var s = NewState();
            var minor = MakeCast("c1", "未成年", 19, regulars: 100, fame: 0, talk: 100);
            s.Shop.Roster.Add(minor);
            // ソフトドリンクだけを置く。キャストドリンクが出れば必ずここから減る。
            s.Shop.Stock.Add(StockKey.SoftDrink, 100000);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.SoftDrink];
            var r = RunOneWeek(s, Weather.Sunny);

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            // 客のドリンク（接客時間ぶん）より多く減っていれば、
            // キャストドリンクがソフトドリンクとして出ている。
            Assert.Greater(before - s.Shop.Stock[StockKey.SoftDrink], guestHours,
                "未成年のキャストドリンクがソフトドリンクに変換されていない");
            Assert.AreEqual(0, r.CastStats[0].AlcoholDrinksServed,
                "未成年が酒を飲んでいる（不変条件 3）");
        }

        [Test]
        public void T02b_客のドリンクはキャストの年齢に関係しない()
        {
            // 企画書の年齢制限はキャストドリンクとボトルが対象で、
            // 客が自分で飲むドリンクは対象外である。
            // ここを縛ると、未成年中心のシフトでソフトドリンクを切らすだけで
            // 売上が止まってしまう。
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "未成年", 19, regulars: 100, fame: 0, talk: 0));
            // アルコールだけを置く。
            s.Shop.Stock.Add(StockKey.Alcohol, 100000);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.Alcohol];
            var r = RunOneWeek(s, Weather.Sunny);

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            Assert.AreEqual(guestHours, before - s.Shop.Stock[StockKey.Alcohol],
                "未成年の担当客にアルコールが出ていない");
            Assert.AreEqual(0, r.CastStats[0].AlcoholDrinksServed,
                "キャスト自身は飲んでいないこと");
        }

        [Test]
        public void T02c_ドリンクはどちらかの在庫があれば出せる()
        {
            long Revenue(bool soft, bool alcohol)
            {
                var s = NewState(seed: 8);
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 19, regulars: 100, fame: 0, talk: 0));
                if (soft) s.Shop.Stock.Add(StockKey.SoftDrink, 100000);
                if (alcohol) s.Shop.Stock.Add(StockKey.Alcohol, 100000);
                s.Shop.CashYen = 100_000_000;
                return RunOneWeek(s, Weather.Sunny).RevenueYen;
            }

            long onlyCharge = Revenue(false, false);

            Assert.Greater(Revenue(true, false), onlyCharge, "ソフトドリンクだけでも出せる");
            Assert.Greater(Revenue(false, true), onlyCharge, "アルコールだけでも出せる");
        }

        [Test]
        public void T04_二十歳になると酒が解禁される()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "成人", 20, talk: 100, alcohol: 100));
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            int alcoholBefore = s.Shop.Stock[StockKey.Alcohol];
            int bottleBefore = s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 0)];

            for (int w = 0; w < 8; w++) RunOneWeek(s, Weather.Sunny);

            Assert.Less(s.Shop.Stock[StockKey.Alcohol], alcoholBefore,
                "成人の担当客に酒が出ていない");

            int sold = 0;
            for (int i = 0; i < 6; i++)
                sold += bottleBefore - s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, i)];
            Assert.Greater(sold, 0, "アルコールボトルが 1 本も売れていない");
        }

        // ---- T-03 / T-14: 加齢 ----------------------------------------------

        [Test]
        public void T03_誕生週の境界で加齢が一回だけ起きる()
        {
            const int birthWeek = 10;

            var s = NewState();
            var cast = MakeCast("c1", "テスト", 18, birthWeek: birthWeek);
            s.Shop.Roster.Add(cast);

            var ages = new List<int>();
            for (int w = 0; w < 13; w++)
            {
                s.Week = new GameWeek(w);
                GameFlow.BeginWeek(s);
                ages.Add(cast.Age);
            }

            Assert.AreEqual(18, ages[9], "前週はまだ 18 歳");
            Assert.AreEqual(19, ages[10], "当週に 19 歳になる");
            Assert.AreEqual(19, ages[11], "翌週も 19 歳のまま");
        }

        [Test]
        public void T14_四十八週で一歳_四百八十週で十歳()
        {
            var s = NewState();
            var cast = MakeCast("c1", "テスト", 18, birthWeek: 5);
            s.Shop.Roster.Add(cast);
            FillStock(s.Shop);
            s.Shop.CashYen = long.MaxValue / 4;

            for (int w = 0; w < 48; w++) RunOneWeek(s, Weather.Sunny, workDays: 0);
            Assert.AreEqual(19, cast.Age, "48 週で 1 歳");

            for (int w = 48; w < 480; w++) RunOneWeek(s, Weather.Sunny, workDays: 0);
            Assert.AreEqual(28, cast.Age, "480 週で 10 歳");
        }

        // ---- T-05: ルックスとボトル -----------------------------------------

        [Test]
        public void T05_ルックスはボトルの注文率に影響しない()
        {
            // 不変条件 2。ルックスが全ステータスの上位互換になるのを防ぐ。
            var plain = MakeCast("c1", "地味", 30, talk: 60, alcohol: 60, looks: 0);
            var pretty = MakeCast("c2", "派手", 30, talk: 60, alcohol: 60, looks: 100);

            Assert.AreEqual(OrderRules.BottleRate(plain, balance),
                            OrderRules.BottleRate(pretty, balance), 1e-12,
                            "ルックスがボトルの注文率に効いている");

            // チェキとキャストドリンクには効く。ここが効かないとルックスが無意味になる。
            Assert.Greater(OrderRules.ChekiRate(pretty, balance),
                           OrderRules.ChekiRate(plain, balance));
            Assert.Greater(OrderRules.CastDrinkRate(pretty, balance),
                           OrderRules.CastDrinkRate(plain, balance));

            // 倍率の上限は 1.0 + looksBonusMax。
            Assert.AreEqual(1.0, VisitorRules.LooksMultiplier(plain, balance), 1e-12);
            Assert.AreEqual(1.0 + balance.Visitors.LooksBonusMax,
                            VisitorRules.LooksMultiplier(pretty, balance), 1e-12);
        }

        [Test]
        public void T05b_ルックスは来客数を通じて間接的にボトルを増やす()
        {
            // 注文率には効かないが、知名度経由で客が増えるぶんは売れる。
            // 不変条件 2 が禁じているのは率に掛けることだけである。
            int Visitors(double looks)
            {
                var s = NewState(seed: 4242);
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 30,
                    regulars: 0, fame: 100, talk: 60, looks: looks));
                FillStock(s.Shop);
                s.Shop.CashYen = 100_000_000;
                return RunOneWeek(s, Weather.Sunny).TotalVisitors;
            }

            Assert.Greater(Visitors(100), Visitors(0));
        }

        // ---- T-06 / T-07: 天候 ----------------------------------------------

        [Test]
        public void T06_雨で固定客は減らない()
        {
            int Regulars(Weather w)
            {
                var s = NewState();
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0));
                FillStock(s.Shop);
                var r = RunOneWeek(s, w);
                return r.TotalVisitors;
            }

            Assert.AreEqual(Regulars(Weather.Sunny), Regulars(Weather.Rain),
                "雨で固定客が減っている（設計書 4.1）");
        }

        [Test]
        public void T06b_雨で新規客は激減する()
        {
            int Visitors(Weather w)
            {
                var s = NewState(seed: 99);
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 0, fame: 100));
                FillStock(s.Shop);
                return RunOneWeek(s, w).TotalVisitors;
            }

            int sunny = Visitors(Weather.Sunny);
            int rain = Visitors(Weather.Rain);

            Assert.Greater(sunny, 0);
            Assert.Less(rain, sunny / 2, "雨で新規客が激減していない");
        }

        [Test]
        public void T07_台風では固定客まで激減する()
        {
            int Visitors(Weather w)
            {
                var s = NewState();
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0));
                FillStock(s.Shop);
                return RunOneWeek(s, w).TotalVisitors;
            }

            int sunny = Visitors(Weather.Sunny);
            int storm = Visitors(Weather.Storm);

            Assert.Greater(sunny, 0);
            Assert.Less(storm, sunny / 5, "台風で固定客が残ってしまっている");
        }

        [Test]
        public void T07b_台風では疲労が倍増する()
        {
            // 週末の疲労そのものではなく、出勤日に溜まった分を見る。
            // 休業日の回復で打ち消されて 0 になってしまうため。
            double Gained(Weather w)
            {
                var s = NewState();
                var c = MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 0);
                s.Shop.Roster.Add(c);
                FillStock(s.Shop);
                s.Shop.CashYen = 100_000_000;

                GameFlow.BeginWeek(s);
                ForceWeather(s, w);
                var shift = new Shift();
                shift.Set("c1", 0, new ShiftSlot(17, 23));   // 月曜だけフル出勤
                var r = GameFlow.RunWeek(s, shift);
                return r.CastStats[0].FatigueGained;
            }

            double sunny = Gained(Weather.Sunny);
            double storm = Gained(Weather.Storm);

            Assert.Greater(sunny, 0);
            // 台風は来客が激減するため接客ぶんの疲労は減るが、倍率で上回る。
            Assert.Greater(storm, sunny, "台風で疲労が増えていない");
        }

        [Test]
        public void T07c_疲労の倍率は天候の係数どおり()
        {
            var c = MakeCast("c1", "テスト", 30, alcohol: 0);
            var service = new ServiceResult();

            double sunny = FatigueRules.DailyGain(c, service, balance, Weather.Sunny);
            double storm = FatigueRules.DailyGain(c, service, balance, Weather.Storm);

            Assert.AreEqual(sunny * 2.0, storm, 1e-12);
        }

        // ---- T-08: 離脱 ------------------------------------------------------

        [Test]
        public void T08_疲労限界の超過で離脱し固定客が失われる()
        {
            var s = NewState();
            var rookie = MakeCast("c1", "新人", 20, regulars: 30, fame: 0, talk: 10, tier: 1);
            s.Shop.Roster.Add(rookie);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            SetFatigueForWeekEnd(rookie, rookie.FatigueLimit(balance) + 1);

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(1, r.Departures.Count);
            Assert.IsFalse(s.Shop.Roster.Contains(rookie), "ロスターから外れていない");
            Assert.AreEqual(30.0, r.Departures[0].LostRegulars, 1e-9);
        }

        [Test]
        public void T08b_育つ前の者は飛び_悪評が他の全員に波及する()
        {
            var s = NewState();
            var rookie = MakeCast("c1", "新人", 20, regulars: 20, fame: 0, talk: 10, tier: 1);
            var other1 = MakeCast("c2", "同僚1", 25, regulars: 50, fame: 0);
            var other2 = MakeCast("c3", "同僚2", 25, regulars: 50, fame: 0);
            s.Shop.Roster.Add(rookie);
            s.Shop.Roster.Add(other1);
            s.Shop.Roster.Add(other2);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            SetFatigueForWeekEnd(rookie, rookie.FatigueLimit(balance) + 1);

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(DepartureKind.RanAway, r.Departures[0].Kind,
                "市場価値が低いのに転生している");
            Assert.AreEqual(50.0 - balance.Departure.AntiSpread, other1.Current.Regulars, 1e-9);
            Assert.AreEqual(50.0 - balance.Departure.AntiSpread, other2.Current.Regulars, 1e-9);
        }

        [Test]
        public void T08c_育った者は転生し悪評は波及しない()
        {
            var s = NewState();
            var ace = MakeCast("c1", "エース", 28, regulars: 90, fame: 90, talk: 90, tier: 1);
            var other = MakeCast("c2", "同僚", 25, regulars: 50, fame: 0);
            s.Shop.Roster.Add(ace);
            s.Shop.Roster.Add(other);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            SetFatigueForWeekEnd(ace, ace.FatigueLimit(balance) + 1);

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(DepartureKind.Defected, r.Departures[0].Kind);
            Assert.AreEqual(0.0, r.Departures[0].AntiSpreadTotal, 1e-9,
                "転生で悪評が波及している（不変条件 19）");
            Assert.AreEqual(50.0, other.Current.Regulars, 1e-9);
        }

        [Test]
        public void T08d_予兆は転生しうる者にだけ出る()
        {
            var s = NewState();
            var ace = MakeCast("c1", "エース", 28, regulars: 90, fame: 90, talk: 90, tier: 5);
            var rookie = MakeCast("c2", "新人", 20, regulars: 10, fame: 0, talk: 10, tier: 5);
            s.Shop.Roster.Add(ace);
            s.Shop.Roster.Add(rookie);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            double limit = balance.Fatigue.LimitOf(5);
            SetFatigueForWeekEnd(ace, limit * 0.8);
            SetFatigueForWeekEnd(rookie, limit * 0.8);

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(1, r.Warnings.Count, "予兆の数が合わない");
            Assert.AreEqual("c1", r.Warnings[0].CastId, "飛ぶ側に予兆が出ている（不変条件 20）");
        }

        [Test]
        public void ニシキが燃え尽きると店が焼けてゲームオーバーになる()
        {
            var s = NewState();
            var rng = new GameRandom(7);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            var nishiki = CastFactory.Hire(applicant, "ニシキ", balance, eggs, 0);
            s.Shop.Roster.Add(nishiki);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            SetFatigueForWeekEnd(nishiki, nishiki.FatigueLimit(balance) + 1);

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(GameOverReason.Arson, r.GameOver);
            Assert.AreEqual(DepartureOverride.BurnDown, r.Departures[0].Override);
            Assert.IsTrue(s.IsOver);
        }

        // ---- T-09 / T-10: 成長と疲労限界 ------------------------------------

        [Test]
        public void T09_成長は上限を超えない()
        {
            var s = NewState();
            // 初期値は必ず上限より下から始める。上限を「超えない」ことの検証なので。
            var c = MakeCast("c1", "テスト", 25, regulars: 30, fame: 20, talk: 10,
                             morale: 20, cap: 40);
            s.Shop.Roster.Add(c);
            FillStock(s.Shop);
            s.Shop.CashYen = long.MaxValue / 4;

            for (int w = 0; w < 200; w++)
            {
                RunOneWeek(s, Weather.Sunny);
                if (s.Shop.Roster.Count == 0) break;

                foreach (var kind in Stats.AllKinds)
                    Assert.LessOrEqual(c.Current[kind], c.Cap[kind] + 1e-9,
                        $"{kind} が上限を超えた（週 {w}）");
            }
        }

        [Test]
        public void T09b_特訓を繰り返しても上限を超えない()
        {
            var s = NewState();
            var c = MakeCast("c1", "テスト", 25, talk: 10, cap: 55);
            s.Shop.Roster.Add(c);

            for (int i = 0; i < 500; i++)
            {
                s.Shop.ManagerAp = 100;
                ManagerActions.Train(s, c, StatKind.Talk);
            }

            Assert.LessOrEqual(c.Current.Talk, 55.0 + 1e-9);
            Assert.That(c.Current.Talk, Is.EqualTo(55.0).Within(1e-6), "上限に張り付く");
        }

        [Test]
        public void T10_疲労限界は実行中に変化しない()
        {
            var s = NewState();
            var c = MakeCast("c1", "テスト", 25, regulars: 100, fame: 20, tier: 3);
            s.Shop.Roster.Add(c);
            FillStock(s.Shop);
            s.Shop.CashYen = long.MaxValue / 4;

            double expected = balance.Fatigue.LimitOf(3);

            for (int w = 0; w < 60; w++)
            {
                GameFlow.BeginWeek(s);
                ForceWeather(s, Weather.Sunny);

                // ケアを挟んで信頼度を積む。それでも限界値は動かない。
                ManagerActions.Care(s, c);
                GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 3, balance));

                if (s.Shop.Roster.Count == 0) break;

                Assert.AreEqual(3, c.StaminaTier);
                Assert.AreEqual(expected, c.FatigueLimit(balance),
                    "疲労限界が変動している（不変条件 1）");
            }

            Assert.Greater(c.Trust, 0, "ケアで信頼度が積まれていない");
        }

        // ---- T-11: 在庫切れ --------------------------------------------------

        [Test]
        public void T11_在庫切れは機会損失として記録され売上が立たない()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 80));
            // 在庫を一切置かない。
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            Assert.Greater(r.TotalVisitors, 0, "客が来ていない");
            Assert.Greater(r.LostOrders, 0, "機会損失が記録されていない");
            Assert.Greater(r.LostSalesYen, 0L);

            // 売上はチャージぶんだけ。商品は 1 つも売れていない。
            Assert.AreEqual(guestHours * balance.Visit.ChargePerHourYen, r.RevenueYen,
                "在庫が無いのに商品の売上が立っている");
        }

        [Test]
        public void T11b_基本ドリンクは滞在時間ぶん必ず注文される()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 0));
            s.Shop.Stock.Add(StockKey.Alcohol, 1000000);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.Alcohol];
            var r = RunOneWeek(s, Weather.Sunny);
            int consumed = before - s.Shop.Stock[StockKey.Alcohol];

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            // 滞在 1 時間につき最低 1 杯。キャストドリンクの分だけ上回る。
            Assert.Greater(guestHours, r.TotalVisitors, "延長が一度も起きていない");
            Assert.GreaterOrEqual(consumed, guestHours,
                "滞在時間ぶんの基本ドリンクが出ていない（設計書 4.3）");
        }

        [Test]
        public void チャージは在庫が無くても入る()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 0));
            // 在庫を一切置かない。
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            Assert.AreEqual(guestHours * balance.Visit.ChargePerHourYen, r.RevenueYen,
                "チャージは席と時間の対価なので、在庫に関係なく入る");
            Assert.Greater(r.LostOrders, 0, "ドリンクの機会損失は記録される");
        }

        [Test]
        public void トークが高いと滞在時間が伸びる()
        {
            var quiet = MakeCast("c1", "無口", 30, talk: 0);
            var chatty = MakeCast("c2", "話し上手", 30, talk: 100);

            Assert.AreEqual(balance.Visit.ExtendBase,
                OrderRules.ExtendChance(quiet, balance), 1e-12);
            Assert.AreEqual(balance.Visit.ExtendBase + balance.Visit.ExtendTalk,
                OrderRules.ExtendChance(chatty, balance), 1e-12);

            double quietHours = OrderRules.ExpectedStayHours(quiet, balance);
            double chattyHours = OrderRules.ExpectedStayHours(chatty, balance);

            TestContext.WriteLine($"トーク 0   の期待滞在: {quietHours:F2} 時間");
            TestContext.WriteLine($"トーク 100 の期待滞在: {chattyHours:F2} 時間");

            Assert.Greater(chattyHours, quietHours);
            Assert.LessOrEqual(chattyHours, balance.Visit.MaxStayHours);
        }

        [Test]
        public void 滞在時間は上限を超えない()
        {
            var s = NewState();
            // 延長確率を最大にしても maxStayHours で打ち切られる。
            var c = MakeCast("c1", "話し上手", 30, regulars: 100, fame: 0, talk: 100);
            s.Shop.Roster.Add(c);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            Assert.LessOrEqual(guestHours, r.TotalVisitors * balance.Visit.MaxStayHours);
        }

        // ---- シフトの時間帯 --------------------------------------------------

        [Test]
        public void 短いシフトは客も売上も少ない()
        {
            long Revenue(int startHour)
            {
                var s = NewState(seed: 9001);
                s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 40));
                FillStock(s.Shop);
                s.Shop.CashYen = 100_000_000;

                GameFlow.BeginWeek(s);
                ForceWeather(s, Weather.Sunny);

                var shift = new Shift();
                shift.Set("c1", 0, new ShiftSlot(startHour, balance.Business.CloseHour));
                return GameFlow.RunWeek(s, shift).RevenueYen;
            }

            long full = Revenue(17);   // 6 時間
            long half = Revenue(20);   // 3 時間

            Assert.Greater(full, 0L);
            Assert.Less(half, full, "短く入れても売上が変わっていない");
        }

        [Test]
        public void 深夜帯に掛かると給料に割増が付く()
        {
            var c = MakeCast("c1", "テスト", 30, regulars: 0, fame: 0, talk: 0);
            long hourly = WageRules.HourlyYen(c, balance, 0);

            // 17〜21 時（4 時間、深夜なし）
            long early = WageRules.DailyYen(c, new ShiftSlot(17, 21), balance, 0);
            Assert.AreEqual(hourly * 4, early);

            // 19〜23 時（4 時間、うち 22〜23 時が深夜）
            long late = WageRules.DailyYen(c, new ShiftSlot(19, 23), balance, 0);
            Assert.AreEqual(
                (long)System.Math.Round(hourly * (3 + 1 * balance.Business.LateNightMultiplier)),
                late);

            Assert.Greater(late, early, "同じ 4 時間でも深夜帯を含むほうが高くならない");
        }

        [Test]
        public void 休日のフル出勤は平日より給料が高い()
        {
            var c = MakeCast("c1", "テスト", 30, regulars: 0, fame: 0, talk: 0);
            var b = balance.Business;

            long weekday = WageRules.DailyYen(c, new ShiftSlot(b.OpenHour(0), b.CloseHour), balance, 0);
            long holiday = WageRules.DailyYen(c, new ShiftSlot(b.OpenHour(5), b.CloseHour), balance, 0);

            Assert.Greater(holiday, weekday, "休日は 2 時間長いので日給も高い");
        }

        [Test]
        public void 勤続一年ごとに時給が百円上がる()
        {
            var c = MakeCast("c1", "テスト", 30, regulars: 0, fame: 0, talk: 0);
            c.HiredWeek = 0;

            long y0 = WageRules.HourlyYen(c, balance, 0);
            long y1 = WageRules.HourlyYen(c, balance, balance.Time.WeeksPerYear);
            long y5 = WageRules.HourlyYen(c, balance, balance.Time.WeeksPerYear * 5);

            Assert.AreEqual(y0 + balance.Wage.SeniorityPerYearYen, y1);
            Assert.AreEqual(y0 + balance.Wage.SeniorityPerYearYen * 5, y5);

            // 年の途中では上がらない。
            Assert.AreEqual(y0, WageRules.HourlyYen(c, balance, balance.Time.WeeksPerYear - 1));
        }

        [Test]
        public void バックはチェキとキャストドリンクとボトルにしか付かない()
        {
            // 在庫を空にすると、商品は 1 つも出ずチャージだけが残る。
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 60));
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);

            Assert.Greater(r.RevenueYen, 0L, "チャージが入っていない");
            Assert.AreEqual(0L, r.CastStats[0].BackableSalesYen,
                "チャージにバックが付いている");
            Assert.AreEqual(0L, r.CastStats[0].BackYen);
        }

        [Test]
        public void チャージぶんはバックの対象外になる()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 60));
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);
            var stats = r.CastStats[0];

            int guestHours = 0;
            foreach (var day in r.Days) guestHours += day.GuestHours;

            long nonBackable = stats.SalesYen - stats.BackableSalesYen;

            Assert.GreaterOrEqual(nonBackable, guestHours * balance.Visit.ChargePerHourYen,
                "チャージがバック対象に混ざっている");
        }

        [Test]
        public void チェキが売れるとバックが発生する()
        {
            var s = NewState();
            var c = MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 60);
            s.Shop.Roster.Add(c);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            var r = RunOneWeek(s, Weather.Sunny);
            var stats = r.CastStats[0];

            Assert.Greater(stats.BackableSalesYen, 0L);
            Assert.Less(stats.BackableSalesYen, stats.SalesYen,
                "全売上がバック対象になっている");
            Assert.AreEqual(
                (long)System.Math.Round(stats.BackableSalesYen * balance.Wage.BackRate),
                stats.BackYen);
        }

        [Test]
        public void 客はシフトより長くは居られない()
        {
            var s = NewState();
            // トーク 100 なら延長確率は最大だが、2 時間シフトなら 2 時間で打ち切られる。
            s.Shop.Roster.Add(MakeCast("c1", "話し上手", 30, regulars: 100, fame: 0, talk: 100));
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            GameFlow.BeginWeek(s);
            ForceWeather(s, Weather.Sunny);

            var shift = new Shift();
            shift.Set("c1", 0, new ShiftSlot(21, 23));   // 2 時間
            var r = GameFlow.RunWeek(s, shift);

            Assert.Greater(r.Days[0].Visitors, 0);
            Assert.LessOrEqual(r.Days[0].GuestHours, r.Days[0].Visitors * 2,
                "2 時間シフトなのに 2 時間を超えて滞在している");
        }

        [Test]
        public void 営業時間外のシフトは弾かれる()
        {
            string reason;

            Assert.IsFalse(Shift.IsValid(new ShiftSlot(16, 23), 0, balance, out reason));
            StringAssert.Contains("開店", reason);

            Assert.IsFalse(Shift.IsValid(new ShiftSlot(17, 24), 0, balance, out reason));
            StringAssert.Contains("閉店", reason);

            Assert.IsFalse(Shift.IsValid(new ShiftSlot(22, 23), 0, balance, out reason));
            StringAssert.Contains("最短シフト", reason);

            Assert.IsTrue(Shift.IsValid(new ShiftSlot(17, 23), 0, balance, out reason));
            Assert.IsTrue(Shift.IsValid(new ShiftSlot(15, 23), 5, balance, out reason),
                "休日は 15 時から開けられる");
            Assert.IsFalse(Shift.IsValid(new ShiftSlot(15, 23), 0, balance, out reason),
                "平日は 15 時には開いていない");
            Assert.IsTrue(Shift.IsValid(ShiftSlot.Off, 0, balance, out reason), "休みは常に有効");
        }

        [Test]
        public void 疲労は接客人数ではなく接客時間に比例する()
        {
            var c = MakeCast("c1", "テスト", 30, alcohol: 0);

            double Gain(int guestHours)
            {
                var service = new ServiceResult { GuestHours = guestHours };
                return FatigueRules.DailyGain(c, service, balance, Weather.Sunny);
            }

            double baseline = Gain(0);
            Assert.AreEqual(baseline + balance.Fatigue.PerGuest * 10, Gain(10), 1e-12);
            Assert.AreEqual(baseline + balance.Fatigue.PerGuest * 30, Gain(30), 1e-12);
        }

        // ---- T-12: ゲームオーバー -------------------------------------------

        [Test]
        public void T12_資金がマイナスでゲームオーバーになる()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 0, fame: 0));
            s.Shop.CashYen = 1000;   // 固定費 93,000 を払えない

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(GameOverReason.CashShortage, r.GameOver);
            Assert.IsTrue(s.IsOver);
            Assert.Less(r.CashAfterYen, 0L);
        }

        [Test]
        public void ゲームオーバー後は週を回しても何も起きない()
        {
            var s = NewState();
            s.Shop.CashYen = 1000;
            RunOneWeek(s, Weather.Sunny, workDays: 0);

            long cash = s.Shop.CashYen;
            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(cash, s.Shop.CashYen);
            Assert.AreEqual(GameOverReason.CashShortage, r.GameOver);
        }

        // ---- T-15: 決定性 ----------------------------------------------------

        [Test]
        public void T15_同じ種で十年通した結果が完全に一致する()
        {
            Assert.AreEqual(Digest(20250922UL), Digest(20250922UL));
            Assert.AreNotEqual(Digest(1UL), Digest(2UL));
        }

        string Digest(ulong seed)
        {
            var s = new GameState(balance, eggs, seed);
            s.Shop.CastSlots = balance.Shop.MaxCastSlots;
            s.Shop.CashYen = long.MaxValue / 4;

            long revenue = 0;
            int visitors = 0;
            int departures = 0;

            for (int w = 0; w < balance.Time.TotalWeeks; w++)
            {
                GameFlow.BeginWeek(s);

                // 素朴な自動プレイ。枠が空いていれば求人して採用する。
                if (s.Shop.HasFreeSlot && s.Shop.Applicants.Count == 0)
                    ManagerActions.Recruit(s, 0);

                while (s.Shop.HasFreeSlot && s.Shop.Applicants.Count > 0)
                    ManagerActions.Hire(s, s.Shop.Applicants[0], "キャスト" + s.Shop.Roster.Count);

                ManagerActions.Order(s, StockKey.SoftDrink, 400);
                ManagerActions.Order(s, StockKey.Alcohol, 400);
                ManagerActions.Order(s, StockKey.ChekiFilm, 200);
                ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleAlcoholic, 0), 10);
                ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleNonAlcoholic, 0), 10);

                foreach (var c in s.Shop.Roster) ManagerActions.Care(s, c);

                var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));

                revenue += r.RevenueYen;
                visitors += r.TotalVisitors;
                departures += r.Departures.Count;

                if (s.IsOver) break;
            }

            var sb = new StringBuilder();
            sb.Append(s.Week.AbsoluteWeek).Append('|')
              .Append(revenue).Append('|')
              .Append(visitors).Append('|')
              .Append(departures).Append('|')
              .Append(s.GameOver).Append('|')
              .Append(s.Shop.Roster.Count).Append('|')
              .Append(s.Rng.Orders.State).Append('|')
              .Append(s.Rng.Weather.State).Append('|')
              .Append(s.Rng.CastGen.State);

            foreach (var c in s.Shop.Roster)
                sb.Append('|').Append(c.Id).Append(':')
                  .Append(c.Current.ToString()).Append(':')
                  .Append(c.Fatigue.ToString("0.########"));

            return sb.ToString();
        }

        [Test]
        public void 十年を通しで回せる()
        {
            var digest = Digest(31337UL);
            Assert.IsNotEmpty(digest);
        }

        // ---- T-24 / T-25 / T-26: ボトルの段階 -------------------------------

        [Test]
        public void T24_解禁されていない段階は在庫があっても売れない()
        {
            var s = NewState();
            // tierPower = (30×0.6 + 0×0.4)/100 = 0.18 → 段階 1 だけ解禁。
            var c = MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 30, alcohol: 0);
            s.Shop.Roster.Add(c);

            Assert.AreEqual(1, OrderRules.UnlockedTierCount(c, balance),
                "前提が崩れている");

            // 最上段だけを在庫に置く。
            s.Shop.Stock.Add(StockKey.Bottle(StockItem.BottleAlcoholic, 5), 1000);
            s.Shop.Stock.Add(StockKey.SoftDrink, 100000);
            s.Shop.Stock.Add(StockKey.Alcohol, 100000);
            s.Shop.Stock.Add(StockKey.ChekiFilm, 100000);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 5)];
            for (int w = 0; w < 10; w++) RunOneWeek(s, Weather.Sunny);

            Assert.AreEqual(before, s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 5)],
                "解禁していない段階が売れている（不変条件 5）");
        }

        [Test]
        public void T24b_解禁された段階のうち在庫のあるものが売れる()
        {
            var s = NewState();
            var ace = MakeCast("c1", "エース", 30, regulars: 100, fame: 0, talk: 95, alcohol: 90);
            s.Shop.Roster.Add(ace);

            Assert.AreEqual(6, OrderRules.UnlockedTierCount(ace, balance));

            // 最上段だけ在庫がある。解禁されているので売れる。
            s.Shop.Stock.Add(StockKey.Bottle(StockItem.BottleAlcoholic, 5), 1000);
            s.Shop.Stock.Add(StockKey.SoftDrink, 100000);
            s.Shop.Stock.Add(StockKey.Alcohol, 100000);
            s.Shop.Stock.Add(StockKey.ChekiFilm, 100000);
            s.Shop.CashYen = 100_000_000;

            int before = s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 5)];
            for (int w = 0; w < 10; w++) RunOneWeek(s, Weather.Sunny);

            Assert.Less(s.Shop.Stock[StockKey.Bottle(StockItem.BottleAlcoholic, 5)], before,
                "解禁済みの段階が売れていない");
        }

        [Test]
        public void T25_ボトルの疲労は段階番号に比例する()
        {
            var c = MakeCast("c1", "テスト", 30, alcohol: 0);

            double Fatigue(int tierSum)
            {
                var service = new ServiceResult { BottleFatigueTiers = tierSum };
                return FatigueRules.DailyGain(c, service, balance, Weather.Sunny);
            }

            double baseline = Fatigue(0);
            double tier1 = Fatigue(1) - baseline;
            double tier6 = Fatigue(6) - baseline;

            Assert.Greater(tier1, 0);
            Assert.AreEqual(tier1 * 6, tier6, 1e-9, "段階 6 が段階 1 の 6 倍でない");
        }

        [Test]
        public void T26_ノンアルコールボトルは酒疲労を生まない()
        {
            var s = NewState();
            var minor = MakeCast("c1", "未成年", 19, regulars: 100, fame: 0, talk: 100);
            s.Shop.Roster.Add(minor);
            FillStock(s.Shop);
            s.Shop.CashYen = 100_000_000;

            int bottles = 0;
            int tierSum = 0;

            // ノンアルを売っても BottleFatigueTiers は積まれない。
            for (int day = 0; day < 20; day++)
            {
                var r = OrderRules.Serve(minor, 30, 6, balance, s.Shop.Stock, s.Rng.Orders);
                bottles += r.BottlesSold;
                tierSum += r.BottleFatigueTiers;
            }

            Assert.Greater(bottles, 0, "ノンアルボトルが売れていない");
            Assert.AreEqual(0, tierSum, "ノンアルコールが酒疲労を生んでいる");
        }

        [Test]
        public void 酒耐性が高いほど酒の疲労が軽い()
        {
            double Fatigue(double alcohol)
            {
                var c = MakeCast("c1", "テスト", 30, alcohol: alcohol);
                var service = new ServiceResult { AlcoholDrinks = 10, BottleFatigueTiers = 6 };
                return FatigueRules.DailyGain(c, service, balance, Weather.Sunny);
            }

            Assert.Greater(Fatigue(0), Fatigue(50));
            Assert.Greater(Fatigue(50), Fatigue(90));
            Assert.AreEqual(balance.Fatigue.Base, Fatigue(100), 1e-9,
                "酒耐性 100 なら酒の疲労はゼロ");
        }

        // ---- 信頼度 ----------------------------------------------------------

        [Test]
        public void 信頼度が高いほど疲労が溜まりにくい()
        {
            double Gained(double trust)
            {
                var s = NewState();
                var c = MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 50, tier: 5);
                c.Trust = trust;
                s.Shop.Roster.Add(c);
                FillStock(s.Shop);
                s.Shop.CashYen = 100_000_000;

                var r = RunOneWeek(s, Weather.Sunny, workDays: 4);
                return r.CastStats[0].FatigueGained;
            }

            double none = Gained(0);
            double full = Gained(100);

            Assert.Greater(none, 0);
            Assert.AreEqual(none * (1 - balance.TrustRelief), full, none * 1e-6,
                "信頼度による軽減が効いていない");
        }

        // ---- 決算 ------------------------------------------------------------

        [Test]
        public void 決算が売上から給料と固定費と仕入れを引いた額になる()
        {
            var s = NewState();
            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, talk: 50));
            s.Shop.CashYen = 10_000_000;

            GameFlow.BeginWeek(s);
            ForceWeather(s, Weather.Sunny);
            ManagerActions.Order(s, StockKey.SoftDrink, 500);
            ManagerActions.Order(s, StockKey.Alcohol, 500);
            ManagerActions.Order(s, StockKey.ChekiFilm, 300);

            long cashAfterOrder = s.Shop.CashYen;
            var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));

            Assert.Greater(r.PurchaseYen, 0L, "仕入れ費が記録されていない");
            Assert.AreEqual(10_000_000L - r.PurchaseYen, cashAfterOrder,
                "発注時に現金が引かれていない");
            Assert.AreEqual(cashAfterOrder + r.RevenueYen - r.WageYen - r.FixedCostYen,
                r.CashAfterYen, "決算の計算が合わない");
            Assert.AreEqual(r.RevenueYen - (r.WageYen + r.FixedCostYen + r.PurchaseYen),
                r.ProfitYen);
        }

        [Test]
        public void 枠を広げると固定費が上がる()
        {
            var s = NewState();
            s.Shop.CastSlots = 6;
            s.Shop.CashYen = 10_000_000;

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(93_000L + 30_000L, r.FixedCostYen);
        }

        // ---- 天候の生成 ------------------------------------------------------

        [Test]
        public void 夏に雪は降らない()
        {
            var rng = new GameRandom(123);
            for (int i = 0; i < 2000; i++)
            {
                var days = WeatherGenerator.GenerateWeek(rng, balance, Season.Summer);
                foreach (var d in days)
                    Assert.AreNotEqual(Weather.Snow, d, "夏に雪が降っている");
            }
        }

        [Test]
        public void 冬には雪が降る()
        {
            var rng = new GameRandom(123);
            bool sawSnow = false;
            for (int i = 0; i < 500 && !sawSnow; i++)
                foreach (var d in WeatherGenerator.GenerateWeek(rng, balance, Season.Winter))
                    if (d == Weather.Snow) { sawSnow = true; break; }

            Assert.IsTrue(sawSnow, "冬に雪が降らない");
        }

        [Test]
        public void 予報は月曜が必ず当たり先の日ほど外れる()
        {
            var rng = new GameRandom(555);
            int[] hits = new int[Shift.DaysPerWeek];
            const int trials = 4000;

            for (int i = 0; i < trials; i++)
            {
                var actual = WeatherGenerator.GenerateWeek(rng, balance, Season.Spring);
                var forecast = WeatherGenerator.MakeForecast(rng, balance, Season.Spring, actual);

                for (int d = 0; d < actual.Length; d++)
                    if (actual[d] == forecast[d]) hits[d]++;
            }

            Assert.AreEqual(trials, hits[0], "月曜の予報が外れている");
            Assert.Less(hits[6], hits[1], "先の日ほど当たりにくくなっていない");
        }

        [Test]
        public void シミュレーションは予報ではなく実際の天候を読む()
        {
            var s = NewState(seed: 12345);
            GameFlow.BeginWeek(s);

            // 予報を全部台風に書き換えても結果は変わらない。
            for (int d = 0; d < Shift.DaysPerWeek; d++) s.Forecast[d] = Weather.Storm;
            ForceWeather(s, Weather.Sunny);

            s.Shop.Roster.Add(MakeCast("c1", "テスト", 30, regulars: 100, fame: 0));
            FillStock(s.Shop);

            var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 7, balance));

            foreach (var day in r.Days)
                Assert.AreEqual(Weather.Sunny, day.Weather);
        }

        // ---- 臨時休業 --------------------------------------------------------

        [Test]
        public void 誰も出勤しない日は休業になり全員が回復する()
        {
            var s = NewState();
            var c = MakeCast("c1", "テスト", 30, regulars: 100, fame: 0, tier: 5);
            c.Fatigue = 20;
            s.Shop.Roster.Add(c);
            FillStock(s.Shop);
            s.Shop.CashYen = 10_000_000;

            var r = RunOneWeek(s, Weather.Sunny, workDays: 0);

            foreach (var day in r.Days) Assert.IsTrue(day.Closed);
            Assert.AreEqual(0, r.TotalVisitors);
            Assert.AreEqual(20.0 - balance.Fatigue.RestRecover * 7, c.Fatigue, 1e-9);
        }

        [Test]
        public void 疲労は零を下回らない()
        {
            var s = NewState();
            var c = MakeCast("c1", "テスト", 30);
            s.Shop.Roster.Add(c);
            s.Shop.CashYen = long.MaxValue / 4;

            for (int w = 0; w < 20; w++) RunOneWeek(s, Weather.Sunny, workDays: 0);

            Assert.AreEqual(0.0, c.Fatigue);
        }
    }
}
