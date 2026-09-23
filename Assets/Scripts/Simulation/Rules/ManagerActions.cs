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
        /// <summary>特訓本の在庫が無い。</summary>
        NoBook,
        /// <summary>冷蔵庫の保管上限を超える。</summary>
        NoStorage,
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
        /// 特訓本を使う。AP を消費しない（出版関係者 OB の効果）。
        ///
        /// AP は店舗規模が大きくなっても増えないため（不変条件 7）、
        /// これが終盤の AP 不足を緩和する唯一の手段になる。
        /// </summary>
        public static ActionResult TrainWithBook(GameState state, Cast cast, StatKind kind)
        {
            if (cast == null) return ActionResult.NotFound;
            if (state.TrainingBooks <= 0) return ActionResult.NoBook;

            state.TrainingBooks--;
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

            // 心理カウンセラー OB は回復量を増やす。信頼度の増え方は変えない。
            cast.Fatigue -= m.CareRecover * state.Alumni.CareRecoverMultiplier;
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
                // 腕利きのスカウトマン OB がいると、上限値の高い候補が来る。
                state.Shop.Applicants.Add(CastFactory.GenerateApplicant(
                    state.Rng.CastGen, state.Balance, tierIndex, state.TakeCastSerial(),
                    state.Alumni.RecruitCapBonus));
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

            long unit = UnitCostYen(state.Balance, key, state.Alumni);
            if (unit < 0) return ActionResult.Invalid;

            // 冷蔵庫に入りきらない発注は通さない。
            //
            // 黙って切り詰めると、枠を広げたのに仕入れが足りない状態に
            // プレイヤーが気づけない。冷蔵庫の拡張（要件 5.4）が
            // 店舗拡張とセットで要る、という設計をここで見せる。
            if (Shop.IsChilled(key.Item)
                && state.Shop.ChilledCount + amount > state.Shop.FridgeCapacity(state.Balance))
                return ActionResult.NoStorage;

            long total = unit * amount;
            if (state.Shop.CashYen < total) return ActionResult.NotEnoughCash;

            state.Shop.CashYen -= total;
            state.Shop.Stock.Add(key, amount);
            state.WeekPurchaseYen += total;
            return ActionResult.Ok;
        }

        /// <summary>
        /// 設備投資（U-13）。資金だけを使い、AP は使わない。
        ///
        /// 不変条件 6: 店長自身は育たない。店舗の強化はここだけである。
        /// 不変条件 1 / 7: 疲労限界にも店長 AP の付与量にも効かせない。
        /// 枠を広げても AP が増えないことが、終盤に「誰に信頼を積むか」を
        /// 選ばせる圧力になっている。
        /// </summary>
        public static ActionResult Invest(GameState state, FacilityKind kind)
        {
            int level = state.Shop.Facilities[kind];
            long price = state.Balance.Facilities.UpgradePriceYen(kind, level);

            if (price < 0) return ActionResult.Invalid;          // 最終段に達している
            if (state.Shop.CashYen < price) return ActionResult.NotEnoughCash;

            state.Shop.CashYen -= price;
            state.Shop.Facilities[kind] = level + 1;

            if (kind == FacilityKind.Expansion)
            {
                // 枠が増えると家賃も上がる（Shop.WeeklyRentYen）。
                int slots = state.Shop.CastSlots + state.Balance.Shop.SlotsPerExpansion;
                if (slots > state.Balance.Shop.MaxCastSlots)
                    slots = state.Balance.Shop.MaxCastSlots;
                state.Shop.CastSlots = slots;
            }

            return ActionResult.Ok;
        }

        /// <summary>次の段に上げる費用。最終段に達していれば -1。</summary>
        public static long NextInvestmentYen(GameState state, FacilityKind kind) =>
            state.Balance.Facilities.UpgradePriceYen(kind, state.Shop.Facilities[kind]);

        /// <summary>
        /// 1 個あたりの仕入れ原価。
        /// 飲料卸売業者 OB がいるとドリンク類だけが安くなる（要件 6.1）。
        /// </summary>
        public static long UnitCostYen(
            BalanceData balance, StockKey key, AlumniEffects alumni = null)
        {
            double drinkMul = alumni?.DrinkCostMultiplier ?? 1.0;

            switch (key.Item)
            {
                case StockItem.SoftDrink:
                    return Discounted(balance.Items.SoftDrink.CostYen, drinkMul);
                case StockItem.Alcohol:
                    return Discounted(balance.Items.Alcohol.CostYen, drinkMul);
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

        static long Discounted(long costYen, double multiplier) =>
            (long)System.Math.Round(costYen * multiplier, System.MidpointRounding.AwayFromZero);

        static bool InRange(int count, int index) => index >= 0 && index < count;
    }
}
