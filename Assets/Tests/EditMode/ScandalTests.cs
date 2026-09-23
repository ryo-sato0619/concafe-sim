using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>段階 4 の必須テスト（T-19 とスキャンダル周り）。</summary>
    public class ScandalTests
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

        // 年齢を 20 にし、上限にも届かせないことで卒業しないようにする。
        // 卒業するとロスターから外れて炎上の検証ができない。
        Cast MakeCast(int scandalResist, double morale = 50, double stats = 50)
        {
            var c = new Cast
            {
                Id = "c1", StageName = "テスト", Age = 20,
                StaminaTier = 5, ScandalResist = scandalResist,
            };
            c.Current = Stats.Uniform(stats);
            c.Current.Morale = morale;
            c.Cap = Stats.Uniform(100);
            c.GrowthRate = Stats.Uniform(1.0);
            return c;
        }

        // ---- 確率 -----------------------------------------------------------

        [Test]
        public void 耐性が高いほど炎上しにくい()
        {
            double high = ScandalRules.Chance(MakeCast(90), 0, balance);
            double low = ScandalRules.Chance(MakeCast(10), 0, balance);

            Assert.Less(high, low);
            Assert.AreEqual(0.0, ScandalRules.Chance(MakeCast(100), 0, balance), 1e-12,
                "耐性 100 なら炎上しない");
        }

        [Test]
        public void 酒を出した週ほど炎上しやすい()
        {
            var c = MakeCast(50);

            Assert.Greater(ScandalRules.Chance(c, 50, balance),
                           ScandalRules.Chance(c, 0, balance));
        }

        [Test]
        public void モラルが高いと炎上しにくい()
        {
            Assert.Less(ScandalRules.Chance(MakeCast(50, morale: 100), 0, balance),
                        ScandalRules.Chance(MakeCast(50, morale: 0), 0, balance));
        }

        [Test]
        public void T19_ニシキは耐性零なので判定が常に最悪側に寄る()
        {
            var rng = new GameRandom(5);
            var applicant = CastFactory.GenerateApplicant(rng, balance, 0, 1);
            var nishiki = CastFactory.Hire(applicant, "ニシキ", balance, eggs, 0);

            Assert.AreEqual(0, nishiki.ScandalResist);

            // 耐性 0 は、同じ条件のどんなキャストよりも炎上しやすい。
            double nishikiChance = ScandalRules.Chance(nishiki, 10, balance);

            var same = MakeCast(1, nishiki.Current.Morale);
            same.Current = nishiki.Current;
            double otherChance = ScandalRules.Chance(same, 10, balance);

            Assert.Greater(nishikiChance, otherChance);
        }

        // ---- 影響 -----------------------------------------------------------

        [Test]
        public void 炎上で知名度と集客とモラルが削れる()
        {
            var c = MakeCast(50, morale: 60, stats: 80);
            double fame = c.Current.Fame;
            double regulars = c.Current.Regulars;
            double morale = c.Current.Morale;

            var record = ScandalRules.Apply(c, balance, new GameRandom(1));

            Assert.Less(c.Current.Fame, fame, "知名度が削れていない");
            Assert.Less(c.Current.Regulars, regulars, "集客が削れていない");
            Assert.Less(c.Current.Morale, morale, "モラルが下がっていない");

            Assert.AreEqual(fame * balance.Scandal.FameLoss, record.FameLost, 1e-9);
            Assert.AreEqual(regulars * balance.Scandal.RegularsLoss, record.RegularsLost, 1e-9);
        }

        [Test]
        public void 炎上でトークと酒耐性は削れない()
        {
            // 炎上は「客が引く」出来事なので、接客の技量そのものは失われない。
            var c = MakeCast(50, stats: 80);
            double talk = c.Current.Talk;
            double alcohol = c.Current.Alcohol;

            ScandalRules.Apply(c, balance, new GameRandom(1));

            Assert.AreEqual(talk, c.Current.Talk);
            Assert.AreEqual(alcohol, c.Current.Alcohol);
        }

        [Test]
        public void 炎上の損失は零を下回らない()
        {
            var c = MakeCast(50, morale: 1, stats: 0.5);
            ScandalRules.Apply(c, balance, new GameRandom(1));

            Assert.GreaterOrEqual(c.Current.Fame, 0);
            Assert.GreaterOrEqual(c.Current.Regulars, 0);
            Assert.GreaterOrEqual(c.Current.Morale, 0);
        }

        // ---- 退店 -----------------------------------------------------------

        [Test]
        public void 炎上で退店すると悪評が波及する()
        {
            var s = new GameState(balance, eggs, careers, 1);
            s.Shop.CashYen = 100_000_000;
            s.Shop.CastSlots = 12;

            var burning = MakeCast(0, morale: 0, stats: 40);
            burning.Id = "c1";
            var other = MakeCast(100, morale: 40, stats: 50);
            other.Id = "c2";

            s.Shop.Roster.Add(burning);
            s.Shop.Roster.Add(other);

            double otherRegularsBefore = other.Current.Regulars;

            // 耐性 0・モラル 0 なら確率が最大。数十週回せば必ず退店まで行く。
            bool sawDeparture = false;
            for (int w = 0; w < 400 && !sawDeparture; w++)
            {
                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                var r = GameFlow.RunWeek(s, new Shift());

                foreach (var dep in r.Departures)
                {
                    if (dep.Trigger != DepartureTrigger.Scandal) continue;

                    sawDeparture = true;
                    Assert.AreEqual(DepartureKind.RanAway, dep.Kind,
                        "炎上での退店は不名誉な去り方として扱う");
                    Assert.Greater(dep.AntiSpreadTotal, 0, "悪評が波及していない");
                    Assert.AreEqual(DepartureOverride.None, dep.Override,
                        "炎上で放火が起きている");
                }
            }

            Assert.IsTrue(sawDeparture, "400 週回しても炎上での退店が起きない");
            Assert.Less(other.Current.Regulars, otherRegularsBefore,
                "同僚の固定客が削られていない");
        }

        [Test]
        public void ニシキは炎上で退店しても放火しない()
        {
            // 放火は疲労限界を超えたときだけ（設計書 6.3）。
            var nishiki = CastFactory.Hire(
                CastFactory.GenerateApplicant(new GameRandom(9), balance, 0, 1),
                "ニシキ", balance, eggs, 0);

            var record = DepartureRules.DepartInDisgrace(
                new Shop { CashYen = 0 }, nishiki, balance);

            Assert.AreEqual(DepartureOverride.None, record.Override);
            Assert.AreEqual(DepartureTrigger.Scandal, record.Trigger);
        }

        [Test]
        public void 耐性が最大なら何百週回しても炎上しない()
        {
            var s = new GameState(balance, eggs, careers, 3);
            s.Shop.CashYen = 1_000_000_000;

            var c = MakeCast(100, morale: 50, stats: 50);
            s.Shop.Roster.Add(c);
            s.Shop.Stock.Add(StockKey.Alcohol, 1000000);
            s.Shop.Stock.Add(StockKey.ChekiFilm, 1000000);

            int scandals = 0;
            for (int w = 0; w < 300; w++)
            {
                GameFlow.BeginWeek(s);
                for (int d = 0; d < Shift.DaysPerWeek; d++) s.Weather[d] = Weather.Sunny;
                var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, 3, balance));
                scandals += r.Scandals.Count;
                c.Fatigue = 0;
            }

            Assert.AreEqual(0, scandals);
        }

    }
}
