using Concafe.Simulation.Balance;

namespace Concafe.Simulation.Model
{
    /// <summary>離脱の種類。飛ぶと転生の差は設計書 4.8 にある。</summary>
    public enum DepartureKind
    {
        /// <summary>飛ぶ（バックレ）。予兆なし。悪評が他キャストの集客まで削る。</summary>
        RanAway,
        /// <summary>転生（移籍）。予兆あり。悪評の波及はない。</summary>
        Defected,
        /// <summary>卒業。モラルが条件を満たせば OB になる。</summary>
        Graduated,
    }

    /// <summary>
    /// パーツ合成で外見を決めるための組み合わせ。
    ///
    /// 保持するのは変種の番号だけで、ファイル名は持たない。PNG が 1 枚も無い状態でも
    /// ゲームが最後まで動く必要があり（不変条件 14）、番号からファイルへの対応と
    /// 不足時の代用は描画側の Provider が引き受ける。
    /// </summary>
    public sealed class Appearance
    {
        public int Body { get; set; }
        public int Dress { get; set; }
        public int HairBack { get; set; }
        public int HairFront { get; set; }
        public int Eye { get; set; }
        /// <summary>0 はアクセサリ無し。</summary>
        public int Accessory { get; set; }

        public int SkinColor { get; set; }
        public int DressColor { get; set; }
        public int HairColor { get; set; }
        public int EyeColor { get; set; }
    }

    public sealed class Cast
    {
        public string Id { get; set; }

        /// <summary>源氏名。採用時にプレイヤーが入力する。イースターエッグの判定対象。</summary>
        public string StageName { get; set; }

        public int Age { get; set; }

        /// <summary>0 〜 WeeksPerYear-1。個体ごとに異なる（設計書 2）。</summary>
        public int BirthWeekOfYear { get; set; }

        public int HiredWeek { get; set; }

        public Stats Current;

        /// <summary>
        /// 個体別の成長上限。不変条件 12: 実行時に書き換えない。
        /// ここが動くと「当たり個体を引く」というゲーム性が消える。
        /// </summary>
        public Stats Cap;

        public Stats GrowthRate;

        /// <summary>
        /// 疲労限界の段階（1 起点）。
        /// 不変条件 1: 生成時に決まり、以後どんな理由でも変動しない。
        /// 信頼度による延命は蓄積速度で行い、この値には触れない。
        /// </summary>
        public int StaminaTier { get; set; }

        public double Fatigue { get; set; }

        /// <summary>隠しパラメータ。生成時に決まる。</summary>
        public int ScandalResist { get; set; }

        /// <summary>
        /// 隠しパラメータ。不変条件 17: 生成時のランダム値にしない。初期はほぼ 0 で、
        /// 悩み相談によってのみ増える。素質ではなく、店長が積んだ関係の記録である。
        /// 不変条件 18: 減衰させない。
        /// </summary>
        public double Trust { get; set; }

        public long LifetimeSalesYen { get; set; }

        /// <summary>該当なしは null。</summary>
        public string EasterEggId { get; set; }

        public Appearance Look { get; set; }

        /// <summary>
        /// 未成年。酒の注文がロックされ、酒耐性が tierPower に寄与しない
        /// （不変条件 3・4）。
        /// </summary>
        public bool IsMinor => Age < 20;

        public double FatigueLimit(BalanceData balance) =>
            balance.Fatigue.LimitOf(StaminaTier);

        /// <summary>
        /// ボトルの段階を解禁する力。0〜1。
        ///
        /// 未成年は酒耐性を算入しない。これが「20 歳の覚醒」の正体であり、
        /// 溜め込んだ酒耐性が 20 歳になった瞬間に乗って解禁段階が跳ね上がる
        /// （設計書 4.3.3、不変条件 4）。
        /// </summary>
        public double TierPower(BalanceData balance)
        {
            double max = balance.CastGen.StatMax;
            if (max <= 0) return 0;

            if (IsMinor) return Clamp01(Current.Talk / max);

            double weighted = Current.Talk * balance.Bottles.TierPowerTalkWeight
                            + Current.Alcohol * balance.Bottles.TierPowerAlcoholWeight;
            return Clamp01(weighted / max);
        }

        /// <summary>
        /// 市場価値。これが閾値以上なら離脱時に転生、未満なら飛ぶ（設計書 4.8）。
        /// プレイヤーが伸ばしたパラメータそのものなので、離脱の質が育成の結果として変わる。
        /// </summary>
        public double MarketValue => (Current.Regulars + Current.Fame + Current.Talk) / 3.0;

        /// <summary>
        /// 酒が抜けにくさ。酒耐性 100 で 0（酒疲労を受けない）。
        /// </summary>
        public double Sober(BalanceData balance)
        {
            double max = balance.CastGen.StatMax;
            if (max <= 0) return 1;
            double v = 1.0 - Current.Alcohol / max;
            return v < 0 ? 0 : v;
        }

        /// <summary>
        /// 信頼度による疲労蓄積の軽減倍率。1.0（信頼なし）〜 1-relief。
        /// 疲労限界そのものには一切触れない（不変条件 1）。
        /// </summary>
        public double TrustRelief(BalanceData balance) =>
            1.0 - Clamp01(Trust / 100.0) * balance.TrustRelief;

        static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        public override string ToString() =>
            $"{StageName}({Age}歳, Tier{StaminaTier}) {Current}";
    }

    /// <summary>求人で届いた候補。採用時に源氏名を付けて Cast になる。</summary>
    public sealed class Applicant
    {
        public string Id { get; set; }
        public int Age { get; set; }
        public int BirthWeekOfYear { get; set; }
        public Stats InitialStats;
        public Stats Cap;
        public Stats GrowthRate;
        public int StaminaTier { get; set; }
        public int ScandalResist { get; set; }
        public Appearance Look { get; set; }

        /// <summary>面接（AP 消費）を済ませると Cap が見えるようになる。</summary>
        public bool Interviewed { get; set; }
    }
}
