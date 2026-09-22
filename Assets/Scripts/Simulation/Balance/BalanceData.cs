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
        public WeatherGenBalance WeatherGen { get; internal set; }
        public BusinessBalance Business { get; internal set; }
        public VisitBalance Visit { get; internal set; }
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
        public AppearanceBalance Appearance { get; internal set; }
    }

    /// <summary>
    /// パーツ合成の構成。各レイヤの種類数と色数だけを持つ。
    ///
    /// ここに実ファイル名を書かない。PNG が 1 枚も無い状態でもゲームが動く必要があり
    /// （不変条件 14）、ファイルの有無は描画側の Provider が吸収する。
    /// </summary>
    public sealed class AppearanceBalance
    {
        public int BodyVariants { get; internal set; }
        public int DressVariants { get; internal set; }
        public int HairBackVariants { get; internal set; }
        public int HairFrontVariants { get; internal set; }
        public int EyeVariants { get; internal set; }
        /// <summary>0 は「アクセサリ無し」。</summary>
        public int AccessoryVariants { get; internal set; }

        public int SkinColors { get; internal set; }
        public int DressColors { get; internal set; }
        public int HairColors { get; internal set; }
        public int EyeColors { get; internal set; }
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

    /// <summary>天候の抽選。季節ごとに重みが変わる。</summary>
    public sealed class WeatherGenBalance
    {
        /// <summary>季節名 → 天候名 → 重み。</summary>
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Seasons
        { get; internal set; }

        /// <summary>
        /// 週頭に見せる予報が当たる確率。添字 0 が月曜。
        /// 先の日ほど外れる。予報を確定情報にしないための仕掛け（設計書 5.2）。
        /// </summary>
        public IReadOnlyList<double> ForecastAccuracy { get; internal set; }
    }

    /// <summary>
    /// 営業時間。
    ///
    /// 平日 17:00〜23:00（6 時間）、休日 15:00〜23:00（8 時間）。
    /// 休日のほうが 2 時間長いため来客も多く、**休日は稼ぎ時になる。**
    /// シフトをどの曜日に置くかが判断になる。
    ///
    /// 22 時以降は深夜割増の対象。23 時閉店なので毎日 1 時間分が割増になる。
    /// </summary>
    public sealed class BusinessBalance
    {
        public int WeekdayOpenHour { get; internal set; }
        public int HolidayOpenHour { get; internal set; }
        public int CloseHour { get; internal set; }

        /// <summary>この時刻以降が深夜割増の対象。</summary>
        public int LateNightFromHour { get; internal set; }
        public double LateNightMultiplier { get; internal set; }

        /// <summary>1 回のシフトの最短時間。</summary>
        public int MinShiftHours { get; internal set; }

        /// <summary>休日の曜日番号。0 = 月曜、6 = 日曜。</summary>
        public IReadOnlyList<int> HolidayDays { get; internal set; }

        public bool IsHoliday(int dayOfWeek)
        {
            foreach (int d in HolidayDays) if (d == dayOfWeek) return true;
            return false;
        }

        public int OpenHour(int dayOfWeek) =>
            IsHoliday(dayOfWeek) ? HolidayOpenHour : WeekdayOpenHour;

        public int OpenHours(int dayOfWeek) => CloseHour - OpenHour(dayOfWeek);

        /// <summary>基準となる営業時間。来客係数はこの長さで校正されている。</summary>
        public int BaselineOpenHours => CloseHour - WeekdayOpenHour;

        /// <summary>
        /// 時刻 start〜end のうち、深夜割増の対象になる時間数。
        /// </summary>
        public int LateNightHoursIn(int startHour, int endHour)
        {
            int from = startHour > LateNightFromHour ? startHour : LateNightFromHour;
            int to = endHour;
            return to > from ? to - from : 0;
        }

        /// <summary>
        /// 給料の計算に使う「割増込みの時間数」。
        /// 深夜帯に掛かった分だけ倍率が乗る。
        /// </summary>
        public double PaidHours(int startHour, int endHour)
        {
            int total = endHour - startHour;
            if (total <= 0) return 0;

            int late = LateNightHoursIn(startHour, endHour);
            return (total - late) + late * LateNightMultiplier;
        }
    }

    /// <summary>
    /// 来店とチャージ。
    ///
    /// 売上の土台は「滞在 1 時間あたり チャージ ＋ 客のドリンク 1 杯」で、
    /// 合わせて約 2,000 円になる。来客 1 人につき 1 杯ではない。
    ///
    /// 滞在時間はトークで伸びる。トークは客を呼ぶステータスではなく、
    /// 呼んだ客を長く座らせて金を落とさせるステータスである。
    /// </summary>
    public sealed class VisitBalance
    {
        /// <summary>チャージ。原価ゼロで全額が粗利になる。</summary>
        public long ChargePerHourYen { get; internal set; }

        /// <summary>
        /// 客が自分で飲む 1 杯の売価。ソフトドリンクでもアルコールでも同額で、
        /// 原価だけが品目ごとに違う。キャストへのバックは付かない（不変条件 25）。
        /// </summary>
        public long GuestDrinkPriceYen { get; internal set; }
        public int MaxStayHours { get; internal set; }

        /// <summary>トーク 0 のときの延長確率。</summary>
        public double ExtendBase { get; internal set; }
        /// <summary>トークが最大のときに上乗せされる延長確率。</summary>
        public double ExtendTalk { get; internal set; }
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
        /// <summary>直接の売価。ソフトドリンクとアルコールは 0（用途で売価が決まる）。</summary>
        public long PriceYen { get; internal set; }

        public bool HasOwnPrice => PriceYen > 0;
    }

    public sealed class ItemBalance
    {
        /// <summary>原価のみ。売価は客のドリンク 900 かキャストドリンク 1,600。</summary>
        public ItemPrice SoftDrink { get; internal set; }
        /// <summary>原価のみ。</summary>
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

    /// <summary>
    /// 給料。時給 ＋ バック。
    ///
    /// バックの対象はチェキ・キャストドリンク・ボトルだけで、
    /// チャージと客のドリンクは店の売上として扱う。
    /// この切り分けがあるため、トークを伸ばすとバック対象の比率が上がり、
    /// **エースの人件費が売上に連動して跳ね上がる**。
    /// </summary>
    public sealed class WageBalance
    {
        public long BaseHourlyYen { get; internal set; }
        /// <summary>能力平均 1 あたりの時給上昇。</summary>
        public double HourlyStatCoefYen { get; internal set; }
        /// <summary>勤続 1 年あたりの時給上昇。</summary>
        public long SeniorityPerYearYen { get; internal set; }

        public double BackRate { get; internal set; }
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
