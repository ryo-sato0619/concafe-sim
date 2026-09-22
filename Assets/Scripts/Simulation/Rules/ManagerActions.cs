using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    public enum ActionResult
    {
        Ok,
        NotEnoughAp,
        NotEnoughCash,
        NoSlot,
        NotFound,
        Invalid,
    }

    /// <summary>
    /// 週頭のプレイヤー操作。すべて店長 AP か資金を消費する。
    ///
    /// 不変条件 7: 週次 AP の付与量を店舗規模に応じて増やさない。
    /// 終盤に全員をケアできなくなることが「使い潰すか伝説にするか」の圧力源である。
    /// </summary>
    public static class ManagerActions
    {
        /// <summary>特訓。1 パラメータを伸ばす。</summary>
        public static ActionResult Train(GameState state, Cast cast, StatKind kind)
        {
            if (cast == null) return ActionResult.NotFound;

            int cost = state.Balance.Manager.TrainCostAp;
            if (state.Shop.ManagerAp < cost) return ActionResult.NotEnoughAp;

            state.Shop.ManagerAp -= cost;
            GrowthRules.Apply(cast, kind, state.Balance.Manager.TrainBaseGain);
            return ActionResult.Ok;
        }

        /// <summary>
        /// 悩み相談。疲労を抜き、同時に信頼度を積む。
        ///
        /// 二重の効果を持つのが要点である。目先の疲労を抜くだけでなく、
        /// 恒久的な貯金が貯まる（設計書 14.4）。信頼度は減衰しない（不変条件 18）。
        /// </summary>
        public static ActionResult Care(GameState state, Cast cast)
        {
            if (cast == null) return ActionResult.NotFound;

            var m = state.Balance.Manager;
            if (state.Shop.ManagerAp < m.CareCostAp) return ActionResult.NotEnoughAp;

            state.Shop.ManagerAp -= m.CareCostAp;

            cast.Fatigue -= m.CareRecover;
            if (cast.Fatigue < 0) cast.Fatigue = 0;

            cast.Trust += m.TrustPerCare;
            if (cast.Trust > 100) cast.Trust = 100;

            return ActionResult.Ok;
        }

        /// <summary>面接。候補の成長上限が見えるようになる。</summary>
        public static ActionResult Interview(GameState state, Applicant applicant)
        {
            if (applicant == null) return ActionResult.NotFound;

            int cost = state.Balance.Manager.InterviewCostAp;
            if (state.Shop.ManagerAp < cost) return ActionResult.NotEnoughAp;

            state.Shop.ManagerAp -= cost;
            applicant.Interviewed = true;
            return ActionResult.Ok;
        }

        /// <summary>求人。費用を払うと候補が届く。</summary>
        public static ActionResult Recruit(GameState state, int tierIndex)
        {
            if (tierIndex < 0 || tierIndex >= state.Balance.Recruit.Count)
                return ActionResult.Invalid;

            var tier = state.Balance.Recruit[tierIndex];
            if (state.Shop.CashYen < tier.FeeYen) return ActionResult.NotEnoughCash;

            state.Shop.CashYen -= tier.FeeYen;

            for (int i = 0; i < tier.Candidates; i++)
            {
                state.Shop.Applicants.Add(CastFactory.GenerateApplicant(
                    state.Rng.CastGen, state.Balance, tierIndex, state.TakeCastSerial()));
            }
            return ActionResult.Ok;
        }

        /// <summary>採用。ここで源氏名を入力し、イースターエッグが判定される。</summary>
        public static ActionResult Hire(GameState state, Applicant applicant, string stageName)
        {
            if (applicant == null) return ActionResult.NotFound;
            if (string.IsNullOrWhiteSpace(stageName)) return ActionResult.Invalid;
            if (!state.Shop.HasFreeSlot) return ActionResult.NoSlot;

            var cast = CastFactory.Hire(applicant, stageName.Trim(),
                                        state.Balance, state.Eggs, state.Week.AbsoluteWeek);

            state.Shop.Roster.Add(cast);
            state.Shop.Applicants.Remove(applicant);
            return ActionResult.Ok;
        }

        /// <summary>
        /// 発注。現金はこの時点で出る。決算では仕入れ費として集計される。
        /// </summary>
        public static ActionResult Order(GameState state, StockKey key, int amount)
        {
            if (amount <= 0) return ActionResult.Invalid;

            long unit = UnitCostYen(state.Balance, key);
            if (unit < 0) return ActionResult.Invalid;

            long total = unit * amount;
            if (state.Shop.CashYen < total) return ActionResult.NotEnoughCash;

            state.Shop.CashYen -= total;
            state.Shop.Stock.Add(key, amount);
            state.WeekPurchaseYen += total;
            return ActionResult.Ok;
        }

        public static long UnitCostYen(BalanceData balance, StockKey key)
        {
            switch (key.Item)
            {
                case StockItem.SoftDrink: return balance.Items.SoftDrink.CostYen;
                case StockItem.Alcohol: return balance.Items.Alcohol.CostYen;
                case StockItem.ChekiFilm: return balance.Items.ChekiFilm.CostYen;
                case StockItem.Limited: return balance.Items.Limited.CostYen;

                case StockItem.BottleAlcoholic:
                    return InRange(balance.Bottles.Alcoholic.Count, key.TierIndex)
                        ? balance.Bottles.Alcoholic[key.TierIndex].CostYen : -1;

                case StockItem.BottleNonAlcoholic:
                    return InRange(balance.Bottles.NonAlcoholic.Count, key.TierIndex)
                        ? balance.Bottles.NonAlcoholic[key.TierIndex].CostYen : -1;

                default: return -1;
            }
        }

        static bool InRange(int count, int index) => index >= 0 && index < count;
    }
}
