using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 素朴な自動プレイで 10 年を通す（設計書 10.6）。
    ///
    /// 1 シードごとの挙動を見るためのもの。完走率などの分布は
    /// <see cref="SweepTests"/> が多数のシードから測る。
    /// **単一シードの結果でバランスを語らないこと。**
    /// </summary>
    public class AutoPlayTests
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

        RunReport AutoPlay(ulong seed, int daysPerCast = 4) =>
            AutoPilot.Run(balance, eggs, careers, seed,
                          new AutoPilot.Policy { DaysPerCast = daysPerCast });

        [Test]
        public void 自動プレイは決定的である()
        {
            var a = AutoPlay(2026);
            var b = AutoPlay(2026);

            Assert.AreEqual(a.WeeksSurvived, b.WeeksSurvived);
            Assert.AreEqual(a.FinalCashYen, b.FinalCashYen);
            Assert.AreEqual(a.MinCashYen, b.MinCashYen);
            Assert.AreEqual(a.GameOver, b.GameOver);
            Assert.AreEqual(a.FinalCastSlots, b.FinalCastSlots);
        }

        [Test]
        public void 出勤日数ごとの生存を測る()
        {
            for (int days = 3; days <= 7; days++)
            {
                var r = AutoPlay(2026, daysPerCast: days);
                TestContext.WriteLine(
                    $"週{days}日: 生存 {r.WeeksSurvived,3}/480  " +
                    $"最終資金 {r.FinalCashYen,12:N0}  最悪 {r.MinCashYen,12:N0}  " +
                    $"枠 {r.FinalCastSlots,2}人  初黒字 w{r.FirstProfitableWeek}  {r.GameOver}");
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
        public void 出勤を削るとシードによって結果が割れる()
        {
            // 単一シードの結果は運に大きく左右されるため、ここでは
            // 「週 3 日はシードによって割れ、週 4 日より不利である」ことだけを見る。
            // 完走率そのものは SweepTests が多数のシードから測る。
            int Completed(int days)
            {
                int n = 0;
                for (ulong seed = 2026; seed < 2046; seed++)
                    if (AutoPlay(seed, daysPerCast: days).Completed) n++;
                return n;
            }

            int three = Completed(3);
            int four = Completed(4);

            TestContext.WriteLine($"20 シード中の完走: 週3日 {three} / 週4日 {four}");

            Assert.Greater(three, 0, "週 3 日でどのシードも完走できない");
            Assert.Less(three, 20, "週 3 日で全シードが完走してしまう。薄利の罰が消えている");
            Assert.Greater(four, three, "出勤を削っても不利にならない");
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
                    $"週{days}日: 生存 {r.WeeksSurvived}/480  飛ぶ {r.RanAways}  {r.GameOver}");

                Assert.IsFalse(r.Completed,
                    $"週 {days} 日のフル出勤で完走できてしまう。働かせすぎが罰せられていない");
            }
        }

        [Test]
        public void 開幕で詰まない()
        {
            // 前任者から引き継ぐキャストがいるため、初週から売上が立つ。
            // ここが落ちたら shop.initialCastNames が空になっている。
            var s = GameFlow.NewGame(balance, eggs, careers, 2026);

            Assert.AreEqual(balance.Shop.InitialCastNames.Count, s.Shop.Roster.Count,
                "引き継ぐキャストが在籍していない");
            Assert.IsNotEmpty(balance.Shop.InitialCastNames, "引き継ぎが空だと開幕で詰む");

            foreach (var c in s.Shop.Roster)
                Assert.Greater(c.Current.Talk, balance.CastGen.InitialStatMean,
                    "引き継ぐキャストが前任者に育てられていない");
        }
    }
}
