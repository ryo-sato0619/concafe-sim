using System.Collections.Generic;
using Concafe.Simulation.Balance;

namespace Concafe.Simulation.Model
{
    /// <summary>
    /// OB 全員の効果をまとめたもの。
    ///
    /// 要件 6.1 の「永続的に強化する」を一か所に集める。各ルールはここを見るだけで、
    /// 誰がどの進路に進んだかを知らなくてよい。
    ///
    /// 同じ進路の OB は積み上がるが、`careers.json` の caps で頭打ちにしてある。
    /// 10 年で何人も輩出できるため、上限が無いと終盤で効果が暴走する。
    /// </summary>
    public sealed class AlumniEffects
    {
        /// <summary>ドリンク類の仕入れ原価に掛ける倍率。1.0 で効果なし。</summary>
        public double DrinkCostMultiplier { get; private set; } = 1.0;
        /// <summary>店舗全体のルックスへの加算。</summary>
        public double LooksBonus { get; private set; }
        /// <summary>悩み相談の疲労回復量に掛ける倍率。</summary>
        public double CareRecoverMultiplier { get; private set; } = 1.0;
        /// <summary>悪天候時の知名度ペナルティの軽減率。0〜1。</summary>
        public double WeatherRelief { get; private set; }
        /// <summary>求人候補の成長上限への加算。</summary>
        public double RecruitCapBonus { get; private set; }

        /// <summary>週あたりに届く特訓本の期待個数。</summary>
        public double TrainingBooksPerWeek { get; private set; }
        /// <summary>週あたりに届く最高段階ボトルの期待本数。</summary>
        public double PremiumBottlesPerWeek { get; private set; }
        /// <summary>週あたりに届く期間限定商品の個数。</summary>
        public double LimitedGoodsPerWeek { get; private set; }

        /// <summary>1 日店長イベントの発生確率。</summary>
        public double OneDayManagerChance { get; private set; }
        /// <summary>ご祝儀の発生確率と額。</summary>
        public double CongratulatoryChance { get; private set; }
        public long CongratulatoryYen { get; private set; }

        public static readonly AlumniEffects None = new AlumniEffects();

        public static AlumniEffects From(IReadOnlyList<Alumnus> alumni, CareerSet careers)
        {
            var e = new AlumniEffects();
            if (alumni == null || alumni.Count == 0 || careers == null) return e;

            var caps = careers.Caps;

            foreach (var a in alumni)
            {
                var def = careers.Find(a.Career);
                if (def == null) continue;

                switch (def.EffectKind)
                {
                    case CareerEffectKind.DrinkCost:
                        // 掛け算で積む。0.88 が 2 人なら 0.774。
                        e.DrinkCostMultiplier *= def.EffectValue;
                        break;

                    case CareerEffectKind.LooksBonus:
                        e.LooksBonus += def.EffectValue;
                        break;

                    case CareerEffectKind.CareRecover:
                        e.CareRecoverMultiplier *= def.EffectValue;
                        break;

                    case CareerEffectKind.WeatherRelief:
                        // 残りのペナルティを削る形で積む。1 人で 30% なら 2 人で 51%。
                        e.WeatherRelief = 1.0 - (1.0 - e.WeatherRelief) * (1.0 - def.EffectValue);
                        break;

                    case CareerEffectKind.RecruitCap:
                        e.RecruitCapBonus += def.EffectValue;
                        break;

                    case CareerEffectKind.TrainingBook:
                        e.TrainingBooksPerWeek += def.EffectValue;
                        break;

                    case CareerEffectKind.PremiumBottle:
                        e.PremiumBottlesPerWeek += def.EffectValue;
                        break;

                    case CareerEffectKind.LimitedGoods:
                        e.LimitedGoodsPerWeek += def.EffectValue;
                        break;

                    case CareerEffectKind.OneDayManager:
                        e.OneDayManagerChance = 1.0 - (1.0 - e.OneDayManagerChance) * (1.0 - def.EffectValue);
                        break;

                    case CareerEffectKind.Congratulatory:
                        e.CongratulatoryChance =
                            1.0 - (1.0 - e.CongratulatoryChance) * (1.0 - def.EffectValue);
                        if (def.EffectAmountYen > e.CongratulatoryYen)
                            e.CongratulatoryYen = def.EffectAmountYen;
                        break;
                }
            }

            // 頭打ち。
            if (e.DrinkCostMultiplier < caps.DrinkCostMultiplierMin)
                e.DrinkCostMultiplier = caps.DrinkCostMultiplierMin;
            if (e.LooksBonus > caps.LooksBonusMax) e.LooksBonus = caps.LooksBonusMax;
            if (e.CareRecoverMultiplier > caps.CareRecoverMultiplierMax)
                e.CareRecoverMultiplier = caps.CareRecoverMultiplierMax;
            if (e.WeatherRelief > caps.WeatherReliefMax) e.WeatherRelief = caps.WeatherReliefMax;
            if (e.RecruitCapBonus > caps.RecruitCapBonusMax)
                e.RecruitCapBonus = caps.RecruitCapBonusMax;
            if (e.TrainingBooksPerWeek > caps.TrainingBooksPerWeekMax)
                e.TrainingBooksPerWeek = caps.TrainingBooksPerWeekMax;
            if (e.PremiumBottlesPerWeek > caps.PremiumBottlesPerWeekMax)
                e.PremiumBottlesPerWeek = caps.PremiumBottlesPerWeekMax;
            if (e.LimitedGoodsPerWeek > caps.LimitedGoodsPerWeekMax)
                e.LimitedGoodsPerWeek = caps.LimitedGoodsPerWeekMax;

            return e;
        }
    }
}
