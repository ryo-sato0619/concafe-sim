using System.Collections.Generic;

namespace Concafe.Simulation.Balance
{
    /// <summary>
    /// balance.json の内容を型付きで保持する。
    ///
    /// 不変条件 10: 数値定数をコードに直接書かない。ここに無い数字を
    /// シミュレーション側に埋め込まないこと。バランス調整のたびに
    /// 再コンパイルが必要になると、調整回数が 1 桁減る。
    ///
    /// すべて読み取り専用。読み込み後に書き換えない。
    /// </summary>
    public sealed class BalanceData
    {
        public int SchemaVersion { get; internal set; }

        public TimeBalance Time { get; internal set; }
        public ShopBalance Shop { get; internal set; }
        public ManagerBalance Manager { get; internal set; }
        public VisitorBalance Visitors { get; internal set; }
        public IReadOnlyDictionary<string, WeatherBalance> Weather { get; internal set; }
        public OrderBalance Orders { get; internal set; }
        public ItemBalance Items { get; internal set; }
        public long CastDrinkPriceYen { get; internal set; }
        public BottleBalance Bottles { get; internal set; }
        public WageBalance Wage { get; internal set; }
        public FatigueBalance Fatigue { get; internal set; }
        public double TrustRelief { get; internal set; }
        public DepartureBalance Departure { get; internal set; }
        public double StockLostRegular { get; internal set; }
        public ScandalBalance Scandal { get; internal set; }
        public IReadOnlyList<RecruitTier> Recruit { get; internal set; }
        public CastGenBalance CastGen { get; internal set; }
    }

    public sealed class TimeBalance
    {
        public int WeeksPerYear { get; internal set; }
        public int WeeksPerMonth { get; internal set; }
        public int YearsPerRun { get; internal set; }

        public int MonthsPerYear => WeeksPerYear / WeeksPerMonth;
        public int TotalWeeks => WeeksPerYear * YearsPerRun;
    }

    public sealed class ShopBalance
    {
        public long InitialCashYen { get; internal set; }
        public long WeeklyFixedCostYen { get; internal set; }
        public int InitialCastSlots { get; internal set; }
        public int MaxCastSlots { get; internal set; }
        public int SlotsPerExpansion { get; internal set; }
        public long FixedCostPerExpansionYen { get; internal set; }
    }

    public sealed class ManagerBalance
    {
        public int ApPerWeek { get; internal set; }
        public int TrainCostAp { get; internal set; }
        public double TrainBaseGain { get; internal set; }
        public int CareCostAp { get; internal set; }
        public double CareRecover { get; internal set; }
        public double TrustPerCare { get; internal set; }
        public int InterviewCostAp { get; internal set; }
        public double DailyWorkGain { get; internal set; }
    }

    public sealed class VisitorBalance
    {
        public double RegularBase { get; internal set; }
        public double FameBase { get; internal set; }
        /// <summary>ルックス 100 のときの上乗せ。1.0 + これ が倍率の上限になる。</summary>
        public double LooksBonusMax { get; internal set; }
    }

    /// <summary>
    /// 天候の係数。FameFactor と StormFactor が分かれているのは意図的である
    /// （設計書 4.1）。雨は知名度由来の新規客だけを削り、固定客は減らさない。
    /// 台風・大雪は StormFactor で固定客ごと潰す。1 つにまとめると
    /// 「集客は天候に影響されない」「台風は客がほぼ来ない」のどちらかが必ず破れる。
    /// </summary>
    public sealed class WeatherBalance
    {
        public double FameFactor { get; internal set; }
        public double StormFactor { get; internal set; }
        public double FatigueMul { get; internal set; }
    }

    public sealed class OrderBalance
    {
        public double TalkFloor { get; internal set; }
        public double TalkSpan { get; internal set; }
        public double ChekiRate { get; internal set; }
        public double CastDrinkRate { get; internal set; }
        public double BottleRate { get; internal set; }
    }

    public sealed class ItemPrice
    {
        public long CostYen { get; internal set; }
        public long PriceYen { get; internal set; }
    }

    public sealed class ItemBalance
    {
        public ItemPrice SoftDrink { get; internal set; }
        public ItemPrice Alcohol { get; internal set; }
        public ItemPrice ChekiFilm { get; internal set; }
        public ItemPrice Limited { get; internal set; }
    }

    /// <summary>ボトル 1 段階の定義。</summary>
    public sealed class BottleTierBalance
    {
        public long PriceYen { get; internal set; }
        public long CostYen { get; internal set; }
        /// <summary>この段階が解禁される tierPower。</summary>
        public double Unlock { get; internal set; }
        /// <summary>疲労の重み。ノンアルコールは 0（酒疲労を生まない）。</summary>
        public int FatigueTier { get; internal set; }
    }

    public sealed class BottleBalance
    {
        public double TierPowerTalkWeight { get; internal set; }
        public double TierPowerAlcoholWeight { get; internal set; }
        public IReadOnlyList<BottleTierBalance> Alcoholic { get; internal set; }
        public IReadOnlyList<BottleTierBalance> NonAlcoholic { get; internal set; }
    }

    public sealed class WageBalance
    {
        public long DailyBaseYen { get; internal set; }
        public long DailyStatCoefYen { get; internal set; }
        public double CommissionRate { get; internal set; }
    }

    public sealed class FatigueBalance
    {
        public double Base { get; internal set; }
        public double PerGuest { get; internal set; }
        public double PerDrink { get; internal set; }
        public double PerBottleTier { get; internal set; }
        public double RestRecover { get; internal set; }

        /// <summary>
        /// 疲労限界。添字 0 が Tier 1。
        /// 不変条件 1: この値は生成時に決まり、以後どんな理由でも変動しない。
        /// 信頼度による延命は蓄積速度（TrustRelief）で行い、ここには触れない。
        /// </summary>
        public IReadOnlyList<double> TierLimits { get; internal set; }

        public int TierCount => TierLimits.Count;

        /// <summary>Tier は 1 起点。</summary>
        public double LimitOf(int staminaTier) => TierLimits[staminaTier - 1];
    }

    public sealed class DepartureBalance
    {
        /// <summary>限界値のこの割合を超えたら予兆を出す。</summary>
        public double WarnRatio { get; internal set; }
        /// <summary>marketValue がこれ以上なら転生、未満なら飛ぶ。</summary>
        public double DefectThreshold { get; internal set; }
        /// <summary>飛ばれたとき、他キャスト全員の集客を削る量。転生では適用しない。</summary>
        public double AntiSpread { get; internal set; }
    }

    public sealed class ScandalBalance
    {
        public double Base { get; internal set; }
        public double PerDrink { get; internal set; }
        public double MoraleGuard { get; internal set; }
    }

    public sealed class RecruitTier
    {
        public long FeeYen { get; internal set; }
        public int Candidates { get; internal set; }
        public double CapMean { get; internal set; }
        public double CapSpread { get; internal set; }
    }

    public sealed class CastGenBalance
    {
        public int MinAge { get; internal set; }
        public int MaxAge { get; internal set; }
        public IReadOnlyList<double> StaminaTierWeights { get; internal set; }
        public double InitialStatMean { get; internal set; }
        public double InitialStatSpread { get; internal set; }
        public double ScandalResistMean { get; internal set; }
        public double ScandalResistSpread { get; internal set; }
        public double GrowthRateMean { get; internal set; }
        public double GrowthRateSpread { get; internal set; }
        public double StatMax { get; internal set; }
    }
}
