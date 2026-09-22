using System.Collections.Generic;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Balance
{
    /// <summary>OB 効果の種類。careers.json の effect.kind に対応する。</summary>
    public enum CareerEffectKind
    {
        /// <summary>ドリンク類の仕入れ原価に掛かる倍率。</summary>
        DrinkCost,
        /// <summary>店舗全体のルックスへの加算。</summary>
        LooksBonus,
        /// <summary>悩み相談の疲労回復量に掛かる倍率。</summary>
        CareRecover,
        /// <summary>悪天候時の知名度ペナルティの軽減率。</summary>
        WeatherRelief,
        /// <summary>求人候補の成長上限への加算。</summary>
        RecruitCap,
        /// <summary>AP 不要の特訓本。週あたりの期待個数。</summary>
        TrainingBook,
        /// <summary>最高段階ボトルの差し入れ。週あたりの期待本数。</summary>
        PremiumBottle,
        /// <summary>原価 0 の期間限定商品。週あたりの個数。</summary>
        LimitedGoods,
        /// <summary>1 日店長イベントの発生確率。</summary>
        OneDayManager,
        /// <summary>ご祝儀の発生確率。</summary>
        Congratulatory,
    }

    public sealed class CareerDefinition
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public CareerKind Kind { get; internal set; }

        /// <summary>進路の選ばれやすさ。パラメータ / statMax に掛ける。</summary>
        public IReadOnlyDictionary<StatKind, double> Weights { get; internal set; }

        public CareerEffectKind EffectKind { get; internal set; }
        public double EffectValue { get; internal set; }
        /// <summary>ご祝儀の額など、value とは別に額が要る効果で使う。</summary>
        public long EffectAmountYen { get; internal set; }

        public string Note { get; internal set; }

        /// <summary>そのキャストがこの進路に進む重み。</summary>
        public double WeightFor(Cast cast, double statMax)
        {
            if (statMax <= 0) return 0;

            double sum = 0;
            foreach (var kv in Weights)
                sum += kv.Value * (cast.Current[kv.Key] / statMax);

            return sum < 0 ? 0 : sum;
        }
    }

    /// <summary>卒業の条件（U-08）。</summary>
    public sealed class GraduationBalance
    {
        /// <summary>最低の在籍週数。</summary>
        public int MinWeeks { get; internal set; }
        /// <summary>
        /// 最低のモラル。
        /// これを下回ると円満卒業にならず、OB 効果も残らない（不変条件 16）。
        /// </summary>
        public double MinMorale { get; internal set; }

        /// <summary>在籍とモラルを満たしたうえで、この年齢以上なら卒業する。</summary>
        public int MinAge { get; internal set; }
        /// <summary>あるいは全パラメータが上限のこの割合に達していれば卒業する。</summary>
        public double CapReachedRatio { get; internal set; }
    }

    /// <summary>OB 効果が暴走しないための上限。</summary>
    public sealed class CareerCaps
    {
        public double DrinkCostMultiplierMin { get; internal set; }
        public double LooksBonusMax { get; internal set; }
        public double CareRecoverMultiplierMax { get; internal set; }
        public double WeatherReliefMax { get; internal set; }
        public double RecruitCapBonusMax { get; internal set; }
        public int TrainingBooksPerWeekMax { get; internal set; }
        public int PremiumBottlesPerWeekMax { get; internal set; }
        public int LimitedGoodsPerWeekMax { get; internal set; }
    }

    public sealed class CareerSet
    {
        public int SchemaVersion { get; internal set; }
        public GraduationBalance Graduation { get; internal set; }
        public CareerCaps Caps { get; internal set; }
        public IReadOnlyList<CareerDefinition> Careers { get; internal set; }

        public CareerDefinition Find(CareerKind kind)
        {
            foreach (var c in Careers) if (c.Kind == kind) return c;
            return null;
        }
    }
}
