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
            public readonly List<int> Slots = new List<int>();
            public readonly List<double> DepartValues = new List<double>();
            public int LostOrders, Neglected;
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
                sum.Slots.Add(r.FinalCastSlots);
                sum.DepartValues.AddRange(r.DepartMarketValues);
                sum.LostOrders += r.LostOrders;
                sum.Neglected += r.NeglectedCastWeeks;

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
            TestContext.WriteLine($"  最高モラル  : 中央 {Percentile(s.MaxMorale, 0.5)} / 最大 {Percentile(s.MaxMorale, 1.0)}（卒業は45〜）");
            TestContext.WriteLine($"  到達枠      : 中央 {Percentile2(s.Slots, 0.5)} / 最大 {Percentile2(s.Slots, 1.0)} 人");
            TestContext.WriteLine($"  ケア不足    : {s.Neglected / (double)s.Runs:F0} 人週/回（疲労が限界の75%超）");
            TestContext.WriteLine($"  在庫切れ    : {s.LostOrders / (double)s.Runs:F0} 件/回");

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
            Assert.Greater(s.RanAways, 0, "飛ぶが一度も起きない");
            Assert.Greater(s.Defections, 0,
                "転生が一度も起きない。「育てた者は引き抜かれる」が成立していない");

            // 飛ぶほうが多いこと。転生が主になると「育つ前の者は黙って消える」が消える。
            Assert.Greater(s.RanAways, s.Defections,
                "転生が飛ぶより多い。departure.defectThreshold が低すぎる");
        }

        [Test]
        public void 離脱時の市場価値の分布を測る()
        {
            // departure.defectThreshold（U-15）をこの分布から決める。
            // 閾値が分布の端に寄っていると、飛ぶ／転生のどちらかが死ぬ。
            var s = Sweep(40, new AutoPilot.Policy { DaysPerCast = 4 });

            var v = new List<long>();
            foreach (double d in s.DepartValues) v.Add((long)System.Math.Round(d));
            Assert.IsNotEmpty(v, "疲労離脱が一度も起きない");

            double threshold = balance.Departure.DefectThreshold;
            int above = 0;
            foreach (double d in s.DepartValues) if (d >= threshold) above++;

            TestContext.WriteLine(
                $"疲労離脱 {v.Count} 件の市場価値: P10 {Percentile(v, 0.1)} / " +
                $"中央 {Percentile(v, 0.5)} / P75 {Percentile(v, 0.75)} / " +
                $"P90 {Percentile(v, 0.9)} / 最大 {Percentile(v, 1.0)}");
            TestContext.WriteLine(
                $"閾値 {threshold} 以上（＝転生）: {above * 100.0 / v.Count:F1}%");

            // 閾値が中央値と最大値の間にあること。中央値を下回ると転生が主流になり、
            // 最大値に近いと転生が起きなくなる（段階 5 で 55 がこれだった）。
            Assert.Greater(threshold, Percentile(v, 0.5),
                "閾値が中央値以下。ほとんどの離脱が転生になる");
            Assert.Less(threshold, Percentile(v, 0.95),
                "閾値が分布の端。転生がほぼ起きない");
        }

        [Test]
        public void 設備投資で枠が伸び店長_AP_が足りなくなる()
        {
            // U-13 を入れた理由そのもの。枠 3 人のままだと AP が余り、
            // 「誰に信頼を積むか」という終盤の判断が発生しない（設計書 14.6.1）。
            var without = Sweep(30, new AutoPilot.Policy { DaysPerCast = 4, Invest = false });
            var with = Sweep(30, new AutoPilot.Policy { DaysPerCast = 4, Invest = true });

            Report("設備投資なし", without);
            Report("設備投資あり", with);

            Assert.AreEqual(balance.Shop.InitialCastSlots, Percentile2(without.Slots, 0.5),
                "投資しない方針なのに枠が増えている");
            Assert.Greater(Percentile2(with.Slots, 0.5), balance.Shop.InitialCastSlots * 2,
                "投資しても枠が伸びていない");

            Assert.Greater(with.Neglected, without.Neglected * 3,
                "枠を広げても AP が逼迫しない。終盤の判断が生まれていない");
            Assert.Greater(with.Graduations, without.Graduations,
                "枠を広げても卒業が増えない。世代交代が回っていない");
        }

        [Test]
        public void 設備投資は割に合うが劇的ではない()
        {
            var without = Sweep(30, new AutoPilot.Policy { DaysPerCast = 4, Invest = false });
            var with = Sweep(30, new AutoPilot.Policy { DaysPerCast = 4, Invest = true });

            long a = Percentile(without.FinalCash, 0.5);
            long b = Percentile(with.FinalCash, 0.5);
            TestContext.WriteLine($"最終資金の中央値: 投資なし {a:N0} → 投資あり {b:N0}");

            Assert.Greater(b, a, "設備投資が損になっている");
            Assert.Less(b, a * 4,
                "設備投資が強すぎる。家賃の上乗せか価格が軽い（設計書 14.1）");
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
