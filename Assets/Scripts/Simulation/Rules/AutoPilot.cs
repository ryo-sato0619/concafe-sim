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

        /// <summary>初めて黒字になった週。-1 なら一度も黒字にならなかった。</summary>
        public int FirstProfitableWeek = -1;

        /// <summary>到達したキャスト枠。設備投資（U-13）が効いているかを見る。</summary>
        public int FinalCastSlots;
        public int ExpansionLevel, FridgeLevel, EspressoLevel;
        /// <summary>在庫切れで失った注文の総数。冷蔵庫が足りているかを見る。</summary>
        public int LostOrders;
        /// <summary>ケアが行き届かなかった週の延べ人数（疲労が限界の 75% を超えた者）。</summary>
        public int NeglectedCastWeeks;

        /// <summary>
        /// 疲労限界を超えて去ったキャストの市場価値。
        /// `departure.defectThreshold`（U-15）をこの分布から決める。
        /// </summary>
        public readonly List<double> DepartMarketValues = new List<double>();
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
            /// <summary>設備投資をするか。false なら枠 3 人のまま 10 年を過ごす。</summary>
            public bool Invest = true;
            /// <summary>設備投資のあとに残す資金。これを割る投資はしない。</summary>
            public long InvestReserveYen = 1_200_000;
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

                if (policy.Invest) Invest(s, balance, policy);

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
                report.LostOrders += r.LostOrders;

                if (report.FirstProfitableWeek < 0 && r.ProfitYen > 0)
                    report.FirstProfitableWeek = w;

                foreach (var c in s.Shop.Roster)
                    if (c.Fatigue > c.FatigueLimit(balance) * balance.Departure.WarnRatio)
                        report.NeglectedCastWeeks++;

                foreach (var d in r.Departures)
                {
                    if (d.Kind == DepartureKind.Defected) report.Defections++;
                    else report.RanAways++;

                    if (d.Trigger == DepartureTrigger.Burnout)
                        report.DepartMarketValues.Add(d.MarketValue);
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
            report.FinalCastSlots = s.Shop.CastSlots;
            report.ExpansionLevel = s.Shop.Facilities[FacilityKind.Expansion];
            report.FridgeLevel = s.Shop.Facilities[FacilityKind.Fridge];
            report.EspressoLevel = s.Shop.Facilities[FacilityKind.Espresso];
            return report;
        }

        /// <summary>
        /// 設備投資。素朴な優先順位で 1 週に 1 つだけ。
        ///
        /// 冷蔵庫を店舗拡張より先に見るのは、枠だけ広げても仕入れが入らず
        /// 機会損失と固定客の喪失（U-05）を招くため。順序そのものが
        /// この 2 つの設備の関係を示している。
        /// </summary>
        static void Invest(GameState s, BalanceData balance, Policy policy)
        {
            // 1. 冷蔵庫。今の実需を上限が賄えなくなったら上げる。
            int need = WeeklyDrinkNeed(s, balance);
            if (need > s.Shop.FridgeCapacity(balance) * 0.8
                && Affordable(s, FacilityKind.Fridge, policy)
                && ManagerActions.Invest(s, FacilityKind.Fridge) == ActionResult.Ok) return;

            // 2. 店舗拡張。枠が埋まっていて、家賃の上乗せを払えるときだけ。
            if (!s.Shop.HasFreeSlot
                && Affordable(s, FacilityKind.Expansion, policy)
                && ManagerActions.Invest(s, FacilityKind.Expansion) == ActionResult.Ok) return;

            // 3. エスプレッソマシン。余った資金の行き先。
            if (Affordable(s, FacilityKind.Espresso, policy))
                ManagerActions.Invest(s, FacilityKind.Espresso);
        }

        static bool Affordable(GameState s, FacilityKind kind, Policy policy)
        {
            long price = ManagerActions.NextInvestmentYen(s, kind);
            return price >= 0 && s.Shop.CashYen - price >= policy.InvestReserveYen;
        }

        /// <summary>週に要るドリンクの本数の見積もり。発注と冷蔵庫の判断に使う。</summary>
        static int WeeklyDrinkNeed(GameState s, BalanceData balance)
        {
            double reg = 0, fame = 0;
            foreach (var c in s.Shop.Roster)
            {
                reg += c.Current.Regulars;
                fame += c.Current.Fame;
            }

            // 来客数 × 平均滞在時間ぶんのドリンクが出る。余裕を 1.3 倍見る。
            double guests = (reg * balance.Visitors.RegularBase
                           + fame * balance.Visitors.FameBase * 1.2) * Shift.DaysPerWeek;
            return (int)(guests * 2.0 * 1.3) + 20;
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
            int maxUnlocked = 0;
            foreach (var c in s.Shop.Roster)
            {
                int n = OrderRules.UnlockedTierCount(c, balance);
                if (n > maxUnlocked) maxUnlocked = n;
            }

            // ボトルを先に押さえる。単価が桁違いなので、冷蔵庫が狭いときに
            // ドリンクで埋めてしまうと高額ボトルが 1 本も置けなくなる。
            for (int t = 0; t < balance.Bottles.Alcoholic.Count; t++)
                TopUp(s, StockKey.Bottle(StockItem.BottleAlcoholic, t), t < maxUnlocked ? 3 : 0);
            TopUp(s, StockKey.Bottle(StockItem.BottleNonAlcoholic, 0), 3);

            // 残りをドリンクで埋める。冷蔵庫に入る量までしか積めない。
            int need = WeeklyDrinkNeed(s, balance);
            int room = s.Shop.FridgeCapacity(balance) - s.Shop.ChilledCount;
            if (room < 0) room = 0;

            int soft = s.Shop.Stock[StockKey.SoftDrink];
            int alcohol = s.Shop.Stock[StockKey.Alcohol];
            int have = soft + alcohol;
            int buy = need - have;
            if (buy > room) buy = room;

            if (buy > 0)
            {
                // 酒のほうが客単価に効く品目（キャストドリンク・酒疲労）に繋がるので
                // 多めに持つ。3 対 1 で割る。
                int forSoft = buy / 4;
                ManagerActions.Order(s, StockKey.Alcohol, buy - forSoft);
                if (forSoft > 0) ManagerActions.Order(s, StockKey.SoftDrink, forSoft);
            }

            // チェキフィルムは冷蔵しないので上限に関係なく積める。
            TopUp(s, StockKey.ChekiFilm, (int)(need * 0.4));
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
