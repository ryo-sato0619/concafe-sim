using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 素朴な自動プレイで 10 年を通す。
    ///
    /// 段階 5 の通しシミュレーション（設計書 10.6）の土台であり、
    /// 現時点では「まだ生き残れない」ことを記録するためのテストでもある。
    /// U-17 が解決したらこのテストが落ちるので、そのとき期待値を更新する。
    /// </summary>
    public class AutoPlayTests
    {
        BalanceData balance;
        EasterEggSet eggs;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
        }

        public struct RunSummary
        {
            public int WeeksSurvived;
            public GameOverReason GameOver;
            public long MinCashYen;
            public int FirstProfitableWeek;
            public long FinalCashYen;
        }

        /// <summary>
        /// 求人・発注・シフト・AP を素朴な方針で回す。最適プレイではない。
        /// プレイヤーが普通に遊んだときの下限を測るためのもの。
        /// </summary>
        RunSummary AutoPlay(ulong seed, bool verbose = false)
        {
            var s = new GameState(balance, eggs, seed);

            var summary = new RunSummary
            {
                MinCashYen = s.Shop.CashYen,
                FirstProfitableWeek = -1,
            };

            for (int w = 0; w < balance.Time.TotalWeeks; w++)
            {
                GameFlow.BeginWeek(s);

                if (s.Shop.HasFreeSlot && s.Shop.Applicants.Count == 0
                    && s.Shop.CashYen > 300_000)
                    ManagerActions.Recruit(s, 0);

                while (s.Shop.HasFreeSlot && s.Shop.Applicants.Count > 0)
                    ManagerActions.Hire(s, s.Shop.Applicants[0], "C" + s.TakeCastSerial());

                OrderForWeek(s);
                SpendAp(s);

                var r = GameFlow.RunWeek(s, Shift.Uniform(s.Shop.Roster, 5));

                if (s.Shop.CashYen < summary.MinCashYen) summary.MinCashYen = s.Shop.CashYen;
                if (summary.FirstProfitableWeek < 0 && r.ProfitYen > 0)
                    summary.FirstProfitableWeek = w;

                if (verbose && (w % 24 == 0 || s.IsOver))
                    TestContext.WriteLine(
                        $"w{w,3} 人{s.Shop.Roster.Count,2} 資金{s.Shop.CashYen,12:N0} " +
                        $"利益{r.ProfitYen,10:N0} 来客{r.TotalVisitors,5} 離脱{r.Departures.Count}");

                if (s.IsOver) break;
            }

            summary.WeeksSurvived = s.Week.AbsoluteWeek;
            summary.GameOver = s.GameOver;
            summary.FinalCashYen = s.Shop.CashYen;
            return summary;
        }

        void OrderForWeek(GameState s)
        {
            double reg = 0, fame = 0;
            int maxUnlocked = 0;
            foreach (var c in s.Shop.Roster)
            {
                reg += c.Current.Regulars;
                fame += c.Current.Fame;
                int n = OrderRules.UnlockedTierCount(c, balance);
                if (n > maxUnlocked) maxUnlocked = n;
            }

            int expected = (int)((reg * balance.Visitors.RegularBase
                                + fame * balance.Visitors.FameBase * 1.2)
                                * Shift.DaysPerWeek) + 20;

            TopUp(s, StockKey.SoftDrink, expected / 2);
            TopUp(s, StockKey.Alcohol, (int)(expected * 1.3));
            TopUp(s, StockKey.ChekiFilm, (int)(expected * 0.5));

            for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                TopUp(s, StockKey.Bottle(StockItem.BottleAlcoholic, t), t < maxUnlocked ? 3 : 0);
            TopUp(s, StockKey.Bottle(StockItem.BottleNonAlcoholic, 0), 3);
        }

        void SpendAp(GameState s)
        {
            var byFatigue = new List<Cast>(s.Shop.Roster);
            byFatigue.Sort((a, b) => b.Fatigue.CompareTo(a.Fatigue));

            // 疲労が半分を超えた者を優先してケアする。
            foreach (var c in byFatigue)
                if (c.Fatigue > c.FatigueLimit(balance) * 0.5)
                    ManagerActions.Care(s, c);

            // 残った AP でトークと集客を伸ばす。
            foreach (var c in byFatigue) ManagerActions.Train(s, c, StatKind.Talk);
            foreach (var c in byFatigue) ManagerActions.Train(s, c, StatKind.Regulars);
        }

        static void TopUp(GameState s, StockKey key, int target)
        {
            int have = s.Shop.Stock[key];
            if (have < target) ManagerActions.Order(s, key, target - have);
        }

        [Test]
        public void 自動プレイは決定的である()
        {
            var a = AutoPlay(2026);
            var b = AutoPlay(2026);

            Assert.AreEqual(a.WeeksSurvived, b.WeeksSurvived);
            Assert.AreEqual(a.FinalCashYen, b.FinalCashYen);
            Assert.AreEqual(a.MinCashYen, b.MinCashYen);
            Assert.AreEqual(a.GameOver, b.GameOver);
        }

        [Test]
        public void 育てば黒字化はする()
        {
            var r = AutoPlay(2026);

            Assert.GreaterOrEqual(r.FirstProfitableWeek, 0,
                "一度も黒字の週が無い。価格か原価の問題になる");
            Assert.Less(r.FirstProfitableWeek, 24,
                "黒字化が遅すぎる。キャストの成長が売上に効いていない可能性がある");
        }

        [Test]
        public void まだ十年を通せない_U17_が未解決()
        {
            // 現状の記録。序盤の赤字（設計書 14.8）を埋めていないため、
            // 素朴なプレイでは 1 年もたずに資金が尽きる。
            //
            // U-17 を解決したらこのテストが落ちる。そのとき期待値を
            // 「10 年を通せる」に書き換えること。
            var r = AutoPlay(2026, verbose: true);

            TestContext.WriteLine($"生存週      : {r.WeeksSurvived} / {balance.Time.TotalWeeks}");
            TestContext.WriteLine($"終了理由    : {r.GameOver}");
            TestContext.WriteLine($"最悪の資金  : {r.MinCashYen:N0}");
            TestContext.WriteLine($"初黒字の週  : {r.FirstProfitableWeek}");

            Assert.AreEqual(GameOverReason.CashShortage, r.GameOver,
                "資金ショート以外で終わった。U-17 の状況が変わったなら設計書 14.8 を見直すこと");
            Assert.Less(r.WeeksSurvived, 60,
                "生き残れるようになった。U-17 が解決したなら、このテストと設計書 14.8 を更新すること");
        }
    }
}
