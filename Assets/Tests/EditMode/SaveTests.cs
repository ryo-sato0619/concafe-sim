using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Json;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using Concafe.Simulation.Save;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// セーブとロード（段階 6。U-09 / U-10）。
    ///
    /// U-09: 手動 10 枠 ＋ オートセーブ 1 枠。
    /// U-10: 乱数の状態は保存しない。ロードのたびに展開が変わる（リセマラ許容）。
    /// </summary>
    public class SaveTests
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

        /// <summary>何週か進めて、中身のある状態を作る。</summary>
        GameState Played(ulong seed = 2026, int weeks = 40)
        {
            var s = GameFlow.NewGame(balance, eggs, careers, seed);

            for (int w = 0; w < weeks && !s.IsOver; w++)
            {
                GameFlow.BeginWeek(s);

                if (s.Shop.HasFreeSlot && s.Shop.Applicants.Count == 0
                    && s.Shop.CashYen > 300_000)
                    ManagerActions.Recruit(s, 1);
                while (s.Shop.HasFreeSlot && s.Shop.Applicants.Count > 0)
                    if (ManagerActions.Hire(s, s.Shop.Applicants[0],
                                            "C" + s.TakeCastSerial()) != ActionResult.Ok) break;

                ManagerActions.Order(s, StockKey.SoftDrink,
                    150 - s.Shop.Stock[StockKey.SoftDrink]);
                ManagerActions.Order(s, StockKey.ChekiFilm,
                    80 - s.Shop.Stock[StockKey.ChekiFilm]);

                foreach (var c in s.Shop.Roster) ManagerActions.Care(s, c);

                GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));
            }

            return s;
        }

        // ---- 往復 -----------------------------------------------------------

        [Test]
        public void セーブして読み直すと状態が一致する()
        {
            var a = Played();
            var b = SaveCodec.Decode(SaveCodec.Encode(a), balance, eggs, careers, 999);

            Assert.AreEqual(a.Week.AbsoluteWeek, b.Week.AbsoluteWeek);
            Assert.AreEqual(a.GameOver, b.GameOver);
            Assert.AreEqual(a.NextCastSerial, b.NextCastSerial);
            Assert.AreEqual(a.TrainingBooks, b.TrainingBooks);
            Assert.AreEqual(a.WeekPurchaseYen, b.WeekPurchaseYen);

            Assert.AreEqual(a.Shop.CashYen, b.Shop.CashYen);
            Assert.AreEqual(a.Shop.CastSlots, b.Shop.CastSlots);
            Assert.AreEqual(a.Shop.ManagerAp, b.Shop.ManagerAp);
            Assert.AreEqual(a.Shop.Roster.Count, b.Shop.Roster.Count);
            Assert.AreEqual(a.Shop.Applicants.Count, b.Shop.Applicants.Count);
            Assert.AreEqual(a.Shop.Alumni.Count, b.Shop.Alumni.Count);

            CollectionAssert.AreEqual(a.Weather, b.Weather);
            CollectionAssert.AreEqual(a.Forecast, b.Forecast);
        }

        [Test]
        public void キャストの値が丸められずに往復する()
        {
            // 疲労・信頼度・各パラメータは小数。書式で丸めると、セーブした
            // だけでパラメータが動く。480 週を跨ぐと誤差が積もる。
            var a = Played();
            Assert.IsNotEmpty(a.Shop.Roster, "検証に使うキャストがいない");

            var b = SaveCodec.Decode(SaveCodec.Encode(a), balance, eggs, careers, 999);

            for (int i = 0; i < a.Shop.Roster.Count; i++)
            {
                var x = a.Shop.Roster[i];
                var y = b.Shop.Roster[i];

                Assert.AreEqual(x.Id, y.Id);
                Assert.AreEqual(x.StageName, y.StageName);
                Assert.AreEqual(x.Age, y.Age);
                Assert.AreEqual(x.BirthWeekOfYear, y.BirthWeekOfYear);
                Assert.AreEqual(x.HiredWeek, y.HiredWeek);
                Assert.AreEqual(x.StaminaTier, y.StaminaTier);
                Assert.AreEqual(x.ScandalResist, y.ScandalResist);
                Assert.AreEqual(x.LifetimeSalesYen, y.LifetimeSalesYen);
                Assert.AreEqual(x.EasterEggId, y.EasterEggId);

                // 完全一致を求める。誤差の許容を入れない。
                Assert.AreEqual(x.Fatigue, y.Fatigue, "疲労が往復で動いた");
                Assert.AreEqual(x.Trust, y.Trust, "信頼度が往復で動いた");

                foreach (var kind in Stats.AllKinds)
                {
                    Assert.AreEqual(x.Current[kind], y.Current[kind], $"{kind} の現在値が動いた");
                    Assert.AreEqual(x.Cap[kind], y.Cap[kind], $"{kind} の上限が動いた");
                    Assert.AreEqual(x.GrowthRate[kind], y.GrowthRate[kind], $"{kind} の成長率が動いた");
                }
            }
        }

        [Test]
        public void 在庫と設備が往復する()
        {
            var s = Played();
            ManagerActions.Invest(s, FacilityKind.Fridge);
            s.Shop.CashYen = 100_000_000;
            ManagerActions.Invest(s, FacilityKind.Expansion);
            ManagerActions.Order(s, StockKey.Bottle(StockItem.BottleAlcoholic, 0), 5);

            var b = SaveCodec.Decode(SaveCodec.Encode(s), balance, eggs, careers, 999);

            Assert.AreEqual(s.Shop.Facilities[FacilityKind.Fridge],
                            b.Shop.Facilities[FacilityKind.Fridge]);
            Assert.AreEqual(s.Shop.Facilities[FacilityKind.Expansion],
                            b.Shop.Facilities[FacilityKind.Expansion]);
            Assert.AreEqual(s.Shop.CastSlots, b.Shop.CastSlots);
            Assert.AreEqual(s.Shop.FridgeCapacity(balance), b.Shop.FridgeCapacity(balance));

            foreach (var kv in s.Shop.Stock.Entries())
                Assert.AreEqual(kv.Value, b.Shop.Stock[kv.Key], $"{kv.Key} の在庫が合わない");

            Assert.AreEqual(s.Shop.ChilledCount, b.Shop.ChilledCount);
        }

        [Test]
        public void OB_の効果が読み直しても効いている()
        {
            // AlumniEffects は集計結果なので、復元後に作り直さないと
            // 「OB はいるのに効果が無い」状態になる。
            var s = Played(weeks: 200);
            if (s.Shop.Alumni.Count == 0)
            {
                s.Shop.Alumni.Add(new Alumnus
                {
                    Id = "a1", StageName = "テスト", Career = CareerKind.DrinkWholesaler,
                    GraduatedWeek = 100,
                });
                s.RefreshAlumniEffects();
            }

            var b = SaveCodec.Decode(SaveCodec.Encode(s), balance, eggs, careers, 999);

            Assert.AreEqual(s.Shop.Alumni.Count, b.Shop.Alumni.Count);
            Assert.AreEqual(s.Alumni.DrinkCostMultiplier, b.Alumni.DrinkCostMultiplier,
                "OB 効果が作り直されていない");
            Assert.AreEqual(s.Alumni.TrainingBooksPerWeek, b.Alumni.TrainingBooksPerWeek);
        }

        [Test]
        public void 読み直したところから続きを回せる()
        {
            var a = Played();
            var b = SaveCodec.Decode(SaveCodec.Encode(a), balance, eggs, careers, 12345);

            for (int w = 0; w < 20 && !b.IsOver; w++)
            {
                GameFlow.BeginWeek(b);
                GameFlow.RunWeek(b, Shift.FullDays(b.Shop.Roster, 4, balance));
            }

            Assert.Greater(b.Week.AbsoluteWeek, a.Week.AbsoluteWeek, "週が進んでいない");
        }

        // ---- U-10: リロードで結果が変わる -----------------------------------

        [Test]
        public void 同じセーブでも読み直すたびに展開が変わる()
        {
            // U-10 決着: 乱数の状態を保存しない。リセマラを許容する。
            var saved = SaveCodec.Encode(Played());

            long Play(ulong seed)
            {
                var s = SaveCodec.Decode(saved, balance, eggs, careers, seed);
                for (int w = 0; w < 12 && !s.IsOver; w++)
                {
                    GameFlow.BeginWeek(s);
                    ManagerActions.Order(s, StockKey.SoftDrink,
                        150 - s.Shop.Stock[StockKey.SoftDrink]);
                    GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));
                }
                return s.Shop.CashYen;
            }

            long a = Play(1);
            long b = Play(2);

            TestContext.WriteLine($"種 1 で {a:N0} / 種 2 で {b:N0}");
            Assert.AreNotEqual(a, b, "違う種を渡しても展開が同じ。乱数の状態が保存されている");
        }

        [Test]
        public void 同じ種を渡せば同じ展開になる()
        {
            // 乱数を保存しないだけで、決定性そのものは失っていない。
            // ここが崩れるとテストも通しシミュレーションも成り立たなくなる。
            var saved = SaveCodec.Encode(Played());

            long Play()
            {
                var s = SaveCodec.Decode(saved, balance, eggs, careers, 777);
                for (int w = 0; w < 12 && !s.IsOver; w++)
                {
                    GameFlow.BeginWeek(s);
                    GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 4, balance));
                }
                return s.Shop.CashYen;
            }

            Assert.AreEqual(Play(), Play());
        }

        [Test]
        public void セーブに乱数の状態が書かれていない()
        {
            // U-10 を実装で崩さないための歯止め。状態を書き始めたら
            // リセマラができなくなるので、その変更はここで落ちる。
            string json = SaveCodec.Encode(Played());

            StringAssert.DoesNotContain("rng", json);
            StringAssert.DoesNotContain("castGenState", json);
            StringAssert.DoesNotContain("seed", json);
        }

        [Test]
        public void 週の天候と予報は読み直しても変わらない()
        {
            // 予報を見せてからシフトを組ませているので、ロードで天候まで
            // 引き直すと、プレイヤーが見た予報が後出しの嘘になる。
            var a = Played();

            for (ulong seed = 1; seed <= 5; seed++)
            {
                var b = SaveCodec.Decode(SaveCodec.Encode(a), balance, eggs, careers, seed);
                CollectionAssert.AreEqual(a.Weather, b.Weather, "天候が引き直されている");
                CollectionAssert.AreEqual(a.Forecast, b.Forecast, "予報が引き直されている");
            }
        }

        // ---- U-09: スロット ---------------------------------------------------

        [Test]
        public void 手動スロットは十枠ある()
        {
            Assert.AreEqual(10, SaveSlots.Count);

            var storage = new InMemorySaveStorage();
            var list = SaveSlots.List(storage);

            Assert.AreEqual(11, list.Count, "オートセーブを含めて 11 行");
            Assert.IsTrue(list[0].IsAutoSave);
            foreach (var row in list) Assert.IsTrue(row.IsEmpty, "初期状態は全部空");
        }

        [Test]
        public void スロットの範囲外は例外になる()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SaveSlots.KeyOf(-1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SaveSlots.KeyOf(10));
        }

        [Test]
        public void 保存した枠だけが埋まる()
        {
            var storage = new InMemorySaveStorage();
            var s = Played();

            SaveSlots.Save(storage, 3, s, "2026-09-23 12:00");

            var list = SaveSlots.List(storage);
            foreach (var row in list)
            {
                bool expectFilled = !row.IsAutoSave && row.Index == 3;
                Assert.AreEqual(expectFilled, !row.IsEmpty,
                    $"スロット {(row.IsAutoSave ? "auto" : row.Index.ToString())} の状態が違う");
            }

            var filled = list.Find(r => r.Index == 3);
            Assert.AreEqual(s.Shop.CashYen, filled.Header.CashYen);
            Assert.AreEqual(s.Shop.Roster.Count, filled.Header.CastCount);
            Assert.AreEqual(s.Week.AbsoluteWeek, filled.Header.AbsoluteWeek);
            Assert.AreEqual("2026-09-23 12:00", filled.Header.SavedAt);
        }

        [Test]
        public void オートセーブは手動セーブを潰さない()
        {
            var storage = new InMemorySaveStorage();

            var manual = Played(seed: 111, weeks: 10);
            SaveSlots.Save(storage, 0, manual);

            var later = Played(seed: 222, weeks: 60);
            SaveSlots.AutoSave(storage, later);

            var reloaded = SaveSlots.Load(storage, 0, balance, eggs, careers, 1);

            Assert.AreEqual(manual.Week.AbsoluteWeek, reloaded.Week.AbsoluteWeek,
                "オートセーブが手動の枠を上書きしている");
            Assert.IsTrue(SaveSlots.HasAutoSave(storage));
        }

        [Test]
        public void オートセーブは毎回上書きされる()
        {
            var storage = new InMemorySaveStorage();

            SaveSlots.AutoSave(storage, Played(weeks: 10));
            SaveSlots.AutoSave(storage, Played(weeks: 30));

            Assert.AreEqual(1, storage.FileCount, "オートセーブが増えている");

            var s = SaveSlots.LoadAutoSave(storage, balance, eggs, careers, 1);
            Assert.AreEqual(30, s.Week.AbsoluteWeek);
        }

        [Test]
        public void 消した枠は空になる()
        {
            var storage = new InMemorySaveStorage();
            SaveSlots.Save(storage, 5, Played());

            Assert.IsTrue(SaveSlots.Exists(storage, 5));
            SaveSlots.Delete(storage, 5);
            Assert.IsFalse(SaveSlots.Exists(storage, 5));

            Assert.Throws<SaveException>(
                () => SaveSlots.Load(storage, 5, balance, eggs, careers, 1));
        }

        // ---- 壊れた入力 -----------------------------------------------------

        [Test]
        public void 壊れた枠があっても一覧は出る()
        {
            // 1 つ壊れただけで一覧が開けなくなると、他のセーブまで救えない。
            var storage = new InMemorySaveStorage();
            SaveSlots.Save(storage, 0, Played());
            storage.Write(SaveSlots.KeyOf(1), "{ これは JSON ではない");

            var list = SaveSlots.List(storage);

            var ok = list.Find(r => r.Index == 0);
            var broken = list.Find(r => r.Index == 1);

            Assert.IsFalse(ok.IsBroken, "正常な枠が壊れ扱いになっている");
            Assert.IsTrue(broken.IsBroken, "壊れた枠が検出されていない");
            Assert.IsTrue(broken.IsEmpty);
        }

        [Test]
        public void 壊れたセーブの読み込みは_SaveException_になる()
        {
            Assert.Throws<SaveException>(
                () => SaveCodec.Decode("{ 壊れている", balance, eggs, careers, 1));
            Assert.Throws<SaveException>(
                () => SaveCodec.Decode("", balance, eggs, careers, 1));
        }

        [Test]
        public void スキーマ版が違うセーブは弾く()
        {
            string json = SaveCodec.Encode(Played())
                .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");

            var e = Assert.Throws<SaveException>(
                () => SaveCodec.Decode(json, balance, eggs, careers, 1));
            StringAssert.Contains("schemaVersion", e.Message);
        }

        // ---- JSON の書き出し -------------------------------------------------

        [Test]
        public void 書き出した_JSON_が読み直せる()
        {
            string json = SaveCodec.Encode(Played());
            Assert.DoesNotThrow(() => JsonParser.Parse(json));
        }

        [Test]
        public void 日本語の源氏名がそのまま往復する()
        {
            var s = GameFlow.NewGame(balance, eggs, careers, 1);
            var applicant = CastFactory.GenerateApplicant(
                new Concafe.Simulation.Rng.GameRandom(3), balance, 0, 99);
            s.Shop.Roster.Add(CastFactory.Hire(applicant, "ニシキ", balance, eggs, 0));

            var b = SaveCodec.Decode(SaveCodec.Encode(s), balance, eggs, careers, 1);

            var nishiki = b.Shop.Roster.Find(c => c.StageName == "ニシキ");
            Assert.IsNotNull(nishiki, "日本語の源氏名が壊れている");
            Assert.AreEqual("nishiki", nishiki.EasterEggId,
                "イースターエッグの結び付きが失われている（不変条件 23）");
        }

        [Test]
        public void 引用符やバックスラッシュを含む名前も往復する()
        {
            var s = GameFlow.NewGame(balance, eggs, careers, 1);
            var applicant = CastFactory.GenerateApplicant(
                new Concafe.Simulation.Rng.GameRandom(3), balance, 0, 99);

            const string tricky = "\"ア\\イ\"\n\tウ";
            s.Shop.Roster.Add(CastFactory.Hire(applicant, tricky, balance, eggs, 0));

            var b = SaveCodec.Decode(SaveCodec.Encode(s), balance, eggs, careers, 1);

            Assert.AreEqual(tricky.Trim(), b.Shop.Roster[b.Shop.Roster.Count - 1].StageName);
        }

        [Test]
        public void 小数が往復で完全に一致する()
        {
            var w = new JsonWriter();
            var values = new List<double>
            {
                0.1, 1.0 / 3.0, 1e-8, 123456789.123456789, -0.000001234,
                double.MaxValue, double.Epsilon, 0.0,
            };

            w.BeginObject();
            w.BeginArray("v");
            foreach (double v in values) w.Item(v);
            w.EndArray();
            w.EndObject();

            var parsed = JsonParser.Parse(w.ToString())["v"];

            for (int i = 0; i < values.Count; i++)
                Assert.AreEqual(values[i], parsed[i].AsDouble(), $"{values[i]} が往復しない");
        }
    }
}
