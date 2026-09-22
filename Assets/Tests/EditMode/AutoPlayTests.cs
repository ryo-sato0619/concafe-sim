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
        RunSummary AutoPlay(ulong seed, bool verbose = false, int daysPerCast = 5)
        {
            var s = GameFlow.NewGame(balance, eggs, seed);

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

                var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, daysPerCast, balance));

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
        public void 出勤日数ごとの生存を測る()
        {
            // U-17 で経費を入れた結果、出勤の厚さが生死を分けるようになった。
            // どのペースなら成立するのかを記録する。
            for (int days = 3; days <= 7; days++)
            {
                var r = AutoPlay(2026, daysPerCast: days);
                TestContext.WriteLine(
                    $"週{days}日: 生存 {r.WeeksSurvived,3}/480  " +
                    $"最終資金 {r.FinalCashYen,12:N0}  最悪 {r.MinCashYen,12:N0}  " +
                    $"初黒字 w{r.FirstProfitableWeek}  {r.GameOver}");
            }
        }

        [Test]
        public void 黒字の週が存在する()
        {
            // どの出勤ペースかに関係なく、黒字の週が 1 度も無いなら
            // 価格か原価の問題になる。
            bool any = false;
            for (int days = 3; days <= 7 && !any; days++)
                if (AutoPlay(2026, daysPerCast: days).FirstProfitableWeek >= 0) any = true;

            Assert.IsTrue(any, "どのペースでも黒字の週が無い");
        }

        [Test]
        public void 週三日から四日なら十年を通せる()
        {
            // 素朴なプレイでも、無理のないシフトなら完走できる。
            foreach (int days in new[] { 3, 4 })
            {
                var r = AutoPlay(2026, daysPerCast: days);

                Assert.AreEqual(balance.Time.TotalWeeks, r.WeeksSurvived,
                    $"週 {days} 日で 10 年を通せていない");
                Assert.AreEqual(GameOverReason.None, r.GameOver);
                Assert.Greater(r.MinCashYen, 0L);
            }
        }

        [Test]
        public void 働かせすぎると崩壊する()
        {
            // 週 5 日以上のフル出勤は疲労で離脱を招き、悪評の波及で立て直せない。
            // 「使い潰すか、伝説にするか」がそのまま数字に出ている。
            //
            // 疲労・離脱・経費のどれかを緩めたらこのテストが落ちる。
            // そのとき、働かせすぎが罰せられる構造が残っているかを確認すること。
            foreach (int days in new[] { 5, 6, 7 })
            {
                var r = AutoPlay(2026, daysPerCast: days);

                TestContext.WriteLine(
                    $"週{days}日: 生存 {r.WeeksSurvived}/480  {r.GameOver}");

                Assert.Less(r.WeeksSurvived, balance.Time.TotalWeeks,
                    $"週 {days} 日のフル出勤で完走できてしまう。働かせすぎが罰せられていない");
            }
        }

        [Test]
        public void 開幕で詰まない()
        {
            // 前任者から引き継ぐキャストがいるため、初週から売上が立つ。
            // ここが落ちたら shop.initialCastNames が空になっている。
            var s = GameFlow.NewGame(balance, eggs, 2026);

            Assert.AreEqual(balance.Shop.InitialCastNames.Count, s.Shop.Roster.Count,
                "引き継ぐキャストが在籍していない");
            Assert.IsNotEmpty(balance.Shop.InitialCastNames, "引き継ぎが空だと開幕で詰む");

            foreach (var c in s.Shop.Roster)
                Assert.Greater(c.Current.Talk, balance.CastGen.InitialStatMean,
                    "引き継ぐキャストが前任者に育てられていない");
        }
    }
}
