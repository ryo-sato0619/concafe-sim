using System.Collections.Generic;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Balance
{
    /// <summary>
    /// 源氏名イースターエッグの定義。
    ///
    /// 不変条件 13: 名前をコードに書かない。追加・調整は easter_eggs.json だけで完結する。
    /// </summary>
    public sealed class EasterEgg
    {
        public string Id { get; internal set; }
        public bool Enabled { get; internal set; }
        public string DisplayName { get; internal set; }

        /// <summary>完全一致の候補。表記ゆれを全部並べる。</summary>
        public IReadOnlyList<string> Match { get; internal set; }

        /// <summary>採用直後に「何かが起きた」と見せるか。</summary>
        public bool RevealOnHire { get; internal set; }

        public string HireMessage { get; internal set; }

        /// <summary>初期値への加算。null は無指定。</summary>
        public StatModifier StatBonus { get; internal set; }
        /// <summary>初期値への倍率。</summary>
        public StatModifier StatMultiplier { get; internal set; }
        /// <summary>成長上限への加算。</summary>
        public StatModifier CapBonus { get; internal set; }
        /// <summary>成長上限への倍率。</summary>
        public StatModifier CapMultiplier { get; internal set; }
        /// <summary>成長率への倍率。ニシキはここで all=10, alcohol=0 を指定する。</summary>
        public StatModifier GrowthRateMultiplier { get; internal set; }

        /// <summary>疲労限界の段階を固定する。0 は無指定。生成時にのみ効く。</summary>
        public int FixedStaminaTier { get; internal set; }
        /// <summary>隠しパラメータを固定する。負は無指定。</summary>
        public int FixedScandalResist { get; internal set; }

        public DepartureOverride OnDeparture { get; internal set; }
        public BurnDownCondition BurnDownOn { get; internal set; }

        public string EventId { get; internal set; }

        /// <summary>在籍中に毎週判定される専用イベント。</summary>
        public IReadOnlyList<WeeklyEggEvent> WeeklyEvents { get; internal set; }
            = new WeeklyEggEvent[0];

        public string Note { get; internal set; }

        public bool HasFixedStaminaTier => FixedStaminaTier > 0;
        public bool HasFixedScandalResist => FixedScandalResist >= 0;
    }

    /// <summary>
    /// パラメータごとの係数。all で全体を指定し、個別キーで上書きする。
    /// 指定が無いパラメータには既定値（加算なら 0、倍率なら 1）が使われる。
    /// </summary>
    public sealed class StatModifier
    {
        readonly Dictionary<StatKind, double> values = new Dictionary<StatKind, double>();
        readonly double fallback;
        readonly bool hasAll;
        readonly double all;

        internal StatModifier(double fallback, bool hasAll, double all,
                              Dictionary<StatKind, double> values)
        {
            this.fallback = fallback;
            this.hasAll = hasAll;
            this.all = all;
            if (values != null) this.values = values;
        }

        /// <summary>何も指定しない（加算 0 / 倍率 1）係数。</summary>
        public static StatModifier Identity(double fallback) =>
            new StatModifier(fallback, false, fallback, null);

        public double For(StatKind kind)
        {
            if (values.TryGetValue(kind, out double v)) return v;
            if (hasAll) return all;
            return fallback;
        }

        public bool IsIdentity
        {
            get
            {
                if (hasAll && all != fallback) return false;
                foreach (var kv in values) if (kv.Value != fallback) return false;
                return true;
            }
        }
    }

    /// <summary>専用イベントの効果。</summary>
    public enum EggEventEffect
    {
        /// <summary>テキストだけ。</summary>
        None,
        /// <summary>その週、指定日数だけ出勤できなくなる。</summary>
        Absence,
        /// <summary>店舗にいる全員のモラルを下げる。</summary>
        ShopMorale,
    }

    /// <summary>専用イベント。未実装の ID を参照してもゲームは止まらない（不変条件 14 と同じ姿勢）。</summary>
    public sealed class EasterEggEvent
    {
        public string Id { get; internal set; }
        public string Title { get; internal set; }
        public IReadOnlyList<string> Body { get; internal set; }
        public GameOverReason GameOver { get; internal set; }

        public EggEventEffect Effect { get; internal set; }
        /// <summary>Absence のときの欠勤日数。</summary>
        public int Days { get; internal set; }
        /// <summary>ShopMorale のときの低下量。</summary>
        public double Value { get; internal set; }
    }

    /// <summary>
    /// 在籍中に毎週判定される専用イベントの参照。
    /// 週頭に起きるので、プレイヤーはシフトを組む前に結果を見られる。
    /// </summary>
    public sealed class WeeklyEggEvent
    {
        public string EventId { get; internal set; }
        public double Chance { get; internal set; }
        /// <summary>前の週に酒を口にしていないと起きない（酒由来のイベント用）。</summary>
        public bool RequiresAlcoholServed { get; internal set; }
    }

    public sealed class EasterEggSet
    {
        public int SchemaVersion { get; internal set; }
        public IReadOnlyList<EasterEgg> Eggs { get; internal set; }
        public IReadOnlyDictionary<string, EasterEggEvent> Events { get; internal set; }

        /// <summary>空のセット。イースターエッグを使わないテストや、ファイルが無い場合に使う。</summary>
        public static readonly EasterEggSet Empty = new EasterEggSet
        {
            SchemaVersion = 0,
            Eggs = new EasterEgg[0],
            Events = new Dictionary<string, EasterEggEvent>(),
        };

        /// <summary>
        /// 源氏名に対応するイースターエッグ。無ければ null。
        /// 前後の空白（全角を含む）は無視する。
        /// </summary>
        public EasterEgg Find(string stageName)
        {
            string key = Normalize(stageName);
            if (key.Length == 0) return null;

            foreach (var egg in Eggs)
            {
                if (!egg.Enabled) continue;
                foreach (var m in egg.Match)
                    if (string.Equals(Normalize(m), key, System.StringComparison.Ordinal))
                        return egg;
            }
            return null;
        }

        public EasterEggEvent FindEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            return Events.TryGetValue(eventId, out var e) ? e : null;
        }

        internal static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Trim(' ', '\t', '\r', '\n', '　');
        }
    }
}
