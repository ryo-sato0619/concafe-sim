using System;
using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 段階 5 の通しシミュレーション（設計書 10.7）。
    ///
    /// 480 週を多数のシードで回し、資金ショート率・卒業数・離脱の内訳の分布を出す。
    /// 手動プレイだけでバランスを取るのは 480 ターンのゲームでは不可能である。
    /// </summary>
    public class SweepTests
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

        sealed class Summary
        {
            public int Runs;
            public int Completed;
            public readonly List<long> FinalCash = new List<long>();
            public readonly List<int> Weeks = new List<int>();
            public int Graduations, RanAways, Defections, Scandals, Hires;
            public readonly List<int> MaxTenure = new List<int>();
            public readonly List<long> MaxMorale = new List<long>();
            public readonly Dictionary<GameOverReason, int> Endings =
                new Dictionary<GameOverReason, int>();
        }

        Summary Sweep(int runs, AutoPilot.Policy policy, ulong seedBase = 1000)
        {
            var sum = new Summary { Runs = runs };

            for (int i = 0; i < runs; i++)
            {
                var r = AutoPilot.Run(balance, eggs, careers, seedBase + (ulong)i, policy);

                if (r.Completed) sum.Completed++;
                sum.FinalCash.Add(r.FinalCashYen);
                sum.Weeks.Add(r.WeeksSurvived);
                sum.Graduations += r.Graduations;
                sum.RanAways += r.RanAways;
                sum.Defections += r.Defections;
                sum.Scandals += r.Scandals;
                sum.Hires += r.Hires;
                sum.MaxTenure.Add(r.MaxTenureWeeks);
                sum.MaxMorale.Add((long)r.MaxMorale);

                sum.Endings[r.GameOver] =
                    sum.Endings.TryGetValue(r.GameOver, out int n) ? n + 1 : 1;
            }

            return sum;
        }

        static long Percentile(List<long> values, double p)
        {
            var copy = new List<long>(values);
            copy.Sort();
            int i = (int)Math.Round((copy.Count - 1) * p);
            return copy[i];
        }

        static int Percentile2(List<int> values, double p)
        {
            var copy = new List<int>(values);
            copy.Sort();
            return copy[(int)Math.Round((copy.Count - 1) * p)];
        }

        static double Mean(List<int> values)
        {
            double s = 0;
            foreach (int v in values) s += v;
            return s / values.Count;
        }

        void Report(string label, Summary s)
        {
            TestContext.WriteLine($"--- {label}（{s.Runs} 回） ---");
            TestContext.WriteLine($"  完走率      : {s.Completed * 100.0 / s.Runs:F1}%");
            TestContext.WriteLine($"  平均生存週  : {Mean(s.Weeks):F0} / {balance.Time.TotalWeeks}");
            TestContext.WriteLine($"  最終資金 P10: {Percentile(s.FinalCash, 0.1):N0}");
            TestContext.WriteLine($"  最終資金 中央: {Percentile(s.FinalCash, 0.5):N0}");
            TestContext.WriteLine($"  最終資金 P90: {Percentile(s.FinalCash, 0.9):N0}");
            TestContext.WriteLine($"  採用/回     : {s.Hires / (double)s.Runs:F1}");
            TestContext.WriteLine($"  卒業/回     : {s.Graduations / (double)s.Runs:F1}");
            TestContext.WriteLine($"  転生/回     : {s.Defections / (double)s.Runs:F1}");
            TestContext.WriteLine($"  飛ぶ/回     : {s.RanAways / (double)s.Runs:F1}");
            TestContext.WriteLine($"  炎上/回     : {s.Scandals / (double)s.Runs:F1}");
            TestContext.WriteLine($"  最長在籍(週): 中央 {Percentile2(s.MaxTenure, 0.5)} / 最大 {Percentile2(s.MaxTenure, 1.0)}（卒業は96週〜）");
            TestContext.WriteLine($"  最高モラル  : 中央 {Percentile(s.MaxMorale, 0.5)} / 最大 {Percentile(s.MaxMorale, 1.0)}（卒業は70〜）");

            foreach (var kv in s.Endings)
                TestContext.WriteLine($"  終了 {kv.Key,-13}: {kv.Value * 100.0 / s.Runs:F1}%");
        }

        [Test]
        public void 出勤ペースごとの分布を測る()
        {
            foreach (int days in new[] { 3, 4, 5 })
                Report($"週{days}日", Sweep(60, new AutoPilot.Policy { DaysPerCast = days }));
        }

        [Test]
        public void 求人ティアごとの分布を測る()
        {
            for (int tier = 0; tier < balance.Recruit.Count; tier++)
                Report($"求人ティア{tier + 1}",
                       Sweep(40, new AutoPilot.Policy { DaysPerCast = 4, RecruitTier = tier }));
        }

        [Test]
        public void ケアの閾値ごとの分布を測る()
        {
            foreach (double ratio in new[] { 0.3, 0.5, 0.7 })
                Report($"ケア閾値{ratio:F1}",
                       Sweep(40, new AutoPilot.Policy { DaysPerCast = 4, CareThresholdRatio = ratio }));
        }

        // ---- ピン留め -------------------------------------------------------

        [Test]
        public void 週四日は多くのシードで完走できる()
        {
            var s = Sweep(60, new AutoPilot.Policy { DaysPerCast = 4 });
            Report("週4日", s);

            double rate = s.Completed * 100.0 / s.Runs;
            Assert.Greater(rate, 60.0, "完走率が低すぎる。素朴なプレイで詰む");
            Assert.Less(rate, 100.0, "全シードで完走してしまう。運の要素が効いていない");
        }

        [Test]
        public void 求人をケチると完走できなくなる()
        {
            // 求人費 30,000 円は家賃 1 週ぶん（70,000 円）にも満たない。
            // それでも最安ティア固定は完走率が半分以下に落ちる。
            // 求人は「節約する対象」ではなく「投資する対象」であること。
            var cheap = Sweep(40, new AutoPilot.Policy { DaysPerCast = 4, RecruitTier = 0 });
            var rich = Sweep(40, new AutoPilot.Policy { DaysPerCast = 4, RecruitTier = 2 });

            Report("求人ティア1 固定", cheap);
            Report("求人ティア3 固定", rich);

            Assert.Less(cheap.Completed, rich.Completed,
                "求人費を積んでも完走率が上がらない。求人ティアが選択になっていない");
            Assert.Less(cheap.Completed * 100.0 / cheap.Runs, 60.0,
                "最安ティア固定でも通せてしまう。求人をケチる罰が消えている");
        }

        [Test]
        public void 離脱の内訳が偏っていない()
        {
            // 卒業・転生・飛ぶ・炎上のどれも起きること。1 つでも 0 なら
            // その仕組みが実質的に死んでいる。
            var s = Sweep(40, new AutoPilot.Policy { DaysPerCast = 4 });
            Report("週4日（内訳）", s);

            Assert.Greater(s.Graduations, 0, "卒業が一度も起きない");
            Assert.Greater(s.Scandals, 0, "炎上が一度も起きない");
            Assert.Greater(s.RanAways + s.Defections, 0, "離脱が一度も起きない");
        }

        [Test]
        public void 通しシミュレーションは決定的である()
        {
            var policy = new AutoPilot.Policy { DaysPerCast = 4 };

            var a = AutoPilot.Run(balance, eggs, careers, 4242, policy);
            var b = AutoPilot.Run(balance, eggs, careers, 4242, policy);

            Assert.AreEqual(a.WeeksSurvived, b.WeeksSurvived);
            Assert.AreEqual(a.FinalCashYen, b.FinalCashYen);
            Assert.AreEqual(a.Graduations, b.Graduations);
            Assert.AreEqual(a.Scandals, b.Scandals);
            Assert.AreEqual(a.BestCastSalesYen, b.BestCastSalesYen);
        }
    }
}
