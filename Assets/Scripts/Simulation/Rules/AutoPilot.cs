using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    /// <summary>1 回の通しプレイの結果。</summary>
    public sealed class RunReport
    {
        public ulong Seed;
        public int WeeksSurvived;
        public GameOverReason GameOver;
        public bool Completed;

        public long FinalCashYen;
        public long MinCashYen;
        public long PeakCashYen;

        public int Graduations;
        public int RanAways;
        public int Defections;
        public int Scandals;
        public int Hires;

        /// <summary>最も売上を上げたキャストの生涯売上。殿堂入りの候補。</summary>
        public long BestCastSalesYen;
        public string BestCastName;

        public long TotalRevenueYen;
        public long TotalProfitYen;

        /// <summary>在籍が一番長かったキャストの週数。卒業条件に届くかを見る。</summary>
        public int MaxTenureWeeks;
        /// <summary>到達したモラルの最大値。卒業条件に届くかを見る。</summary>
        public double MaxMorale;
    }

    /// <summary>
    /// 素朴な自動プレイ。最適プレイではなく、**プレイヤーが普通に遊んだときの下限**を
    /// 測るためのもの。段階 5 のバランス調整はこれを何百回も回して分布を見る。
    ///
    /// 方針を Simulation 層に置いているのは、テストからも通しシミュレーションからも
    /// 同じものを回すため。UI を通さずにバランスを測れる状態を保つのが
    /// Simulation 層を純 C# にした目的である（設計書 1.1）。
    /// </summary>
    public static class AutoPilot
    {
        public sealed class Policy
        {
            /// <summary>1 人あたりの週の出勤日数。</summary>
            public int DaysPerCast = 4;
            /// <summary>求人に踏み切る資金の下限。</summary>
            public long RecruitCashFloorYen = 300_000;
            /// <summary>
            /// どの求人ティアを使うか。-1 なら資金に見合う一番良いティアを選ぶ。
            ///
            /// 常に最安を選ぶのは「素朴」ではなく、単に損な戦略である
            /// （求人ティア 1 固定は完走率 45%、ティア 2 は 95%）。
            /// 求人費は家賃 1 週ぶんにも満たないので、普通に遊べば払う。
            /// </summary>
            public int RecruitTier = -1;
            /// <summary>自動選択のとき、求人費に回してよい資金の割合。</summary>
            public double RecruitBudgetRatio = 0.1;
            /// <summary>疲労がこの割合を超えたらケアする。</summary>
            public double CareThresholdRatio = 0.5;
        }

        public static RunReport Run(
            BalanceData balance, EasterEggSet eggs, CareerSet careers,
            ulong seed, Policy policy = null)
        {
            policy = policy ?? new Policy();

            var s = GameFlow.NewGame(balance, eggs, careers, seed);
            var report = new RunReport
            {
                Seed = seed,
                MinCashYen = s.Shop.CashYen,
                PeakCashYen = s.Shop.CashYen,
                Hires = s.Shop.Roster.Count,
            };

            var bestSales = new Dictionary<string, long>();
            var names = new Dictionary<string, string>();

            for (int w = 0; w < balance.Time.TotalWeeks; w++)
            {
                GameFlow.BeginWeek(s);

                if (s.Shop.HasFreeSlot && s.Shop.Applicants.Count == 0
                    && s.Shop.CashYen > policy.RecruitCashFloorYen)
                    ManagerActions.Recruit(s, PickRecruitTier(s, balance, policy));

                while (s.Shop.HasFreeSlot && s.Shop.Applicants.Count > 0)
                {
                    if (ManagerActions.Hire(s, s.Shop.Applicants[0],
                                            "C" + s.TakeCastSerial()) != ActionResult.Ok) break;
                    report.Hires++;
                }

                Order(s, balance);
                SpendAp(s, balance, policy);

                var r = GameFlow.RunWeek(s, Shift.FullDays(s.Shop.Roster, policy.DaysPerCast, balance));

                report.TotalRevenueYen += r.RevenueYen;
                report.TotalProfitYen += r.ProfitYen;
                report.Graduations += r.Graduations.Count;
                report.Scandals += r.Scandals.Count;

                foreach (var d in r.Departures)
                {
                    if (d.Kind == DepartureKind.Defected) report.Defections++;
                    else report.RanAways++;
                }

                foreach (var cs in r.CastStats)
                {
                    names[cs.CastId] = cs.StageName;
                    bestSales[cs.CastId] =
                        bestSales.TryGetValue(cs.CastId, out long v) ? v + cs.SalesYen : cs.SalesYen;
                }

                foreach (var c in s.Shop.Roster)
                {
                    int tenure = s.Week.AbsoluteWeek - c.HiredWeek;
                    if (tenure > report.MaxTenureWeeks) report.MaxTenureWeeks = tenure;
                    if (c.Current.Morale > report.MaxMorale) report.MaxMorale = c.Current.Morale;
                }

                if (s.Shop.CashYen < report.MinCashYen) report.MinCashYen = s.Shop.CashYen;
                if (s.Shop.CashYen > report.PeakCashYen) report.PeakCashYen = s.Shop.CashYen;

                if (s.IsOver) break;
            }

            foreach (var kv in bestSales)
            {
                if (kv.Value <= report.BestCastSalesYen) continue;
                report.BestCastSalesYen = kv.Value;
                report.BestCastName = names.TryGetValue(kv.Key, out string n) ? n : kv.Key;
            }

            report.WeeksSurvived = s.Week.AbsoluteWeek;
            report.GameOver = s.GameOver;
            report.Completed = !s.IsOver && s.Week.AbsoluteWeek >= balance.Time.TotalWeeks;
            report.FinalCashYen = s.Shop.CashYen;
            return report;
        }

        /// <summary>資金の <see cref="Policy.RecruitBudgetRatio"/> までで一番良いティア。</summary>
        static int PickRecruitTier(GameState s, BalanceData balance, Policy policy)
        {
            if (policy.RecruitTier >= 0) return policy.RecruitTier;

            long budget = (long)(s.Shop.CashYen * policy.RecruitBudgetRatio);
            int pick = 0;
            for (int t = 0; t < balance.Recruit.Count; t++)
                if (balance.Recruit[t].FeeYen <= budget) pick = t;

            return pick;
        }

        static void Order(GameState s, BalanceData balance)
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
            TopUp(s, StockKey.Alcohol, (int)(expected * 1.6));
            TopUp(s, StockKey.ChekiFilm, (int)(expected * 0.8));

            for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                TopUp(s, StockKey.Bottle(StockItem.BottleAlcoholic, t), t < maxUnlocked ? 3 : 0);
            TopUp(s, StockKey.Bottle(StockItem.BottleNonAlcoholic, 0), 3);
        }

        static void SpendAp(GameState s, BalanceData balance, Policy policy)
        {
            var byFatigue = new List<Cast>(s.Shop.Roster);
            byFatigue.Sort((a, b) => b.Fatigue.CompareTo(a.Fatigue));

            foreach (var c in byFatigue)
                if (c.Fatigue > c.FatigueLimit(balance) * policy.CareThresholdRatio)
                    ManagerActions.Care(s, c);

            // 特訓本は AP を使わないので先に使い切る。
            foreach (var c in byFatigue)
                while (s.TrainingBooks > 0 &&
                       ManagerActions.TrainWithBook(s, c, StatKind.Talk) == ActionResult.Ok) { }

            foreach (var c in byFatigue) ManagerActions.Train(s, c, StatKind.Talk);
            foreach (var c in byFatigue) ManagerActions.Train(s, c, StatKind.Regulars);
            foreach (var c in byFatigue) ManagerActions.Train(s, c, StatKind.Morale);
        }

        static void TopUp(GameState s, StockKey key, int target)
        {
            int have = s.Shop.Stock[key];
            if (have < target) ManagerActions.Order(s, key, target - have);
        }
    }
}
