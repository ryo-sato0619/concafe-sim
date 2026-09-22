using System;
using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    /// <summary>公開パラメータの種類。疲労はここに含まれない（成長しないため）。</summary>
    public enum StatKind
    {
        /// <summary>集客。固定ファンの数。天候に影響されないベース来店数。</summary>
        Regulars,
        /// <summary>知名度。新規客を呼ぶ SNS 力。悪天候で効果が激減する。</summary>
        Fame,
        /// <summary>トーク。チェキ・キャストドリンク・ボトルの注文発生率を上げる。</summary>
        Talk,
        /// <summary>酒耐性。ボトル段階の解禁と、酒による疲労の軽減を兼ねる。</summary>
        Alcohol,
        /// <summary>ルックス。乗数。ボトル注文率には掛けない（不変条件 2）。</summary>
        Looks,
        /// <summary>モラル。卒業と進路を決める最重要値。</summary>
        Morale,
    }

    public struct Stats
    {
        public double Regulars;
        public double Fame;
        public double Talk;
        public double Alcohol;
        public double Looks;
        public double Morale;

        public static readonly IReadOnlyList<StatKind> AllKinds = new[]
        {
            StatKind.Regulars, StatKind.Fame, StatKind.Talk,
            StatKind.Alcohol, StatKind.Looks, StatKind.Morale,
        };

        public double this[StatKind kind]
        {
            get
            {
                switch (kind)
                {
                    case StatKind.Regulars: return Regulars;
                    case StatKind.Fame: return Fame;
                    case StatKind.Talk: return Talk;
                    case StatKind.Alcohol: return Alcohol;
                    case StatKind.Looks: return Looks;
                    case StatKind.Morale: return Morale;
                    default: throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
            set
            {
                switch (kind)
                {
                    case StatKind.Regulars: Regulars = value; break;
                    case StatKind.Fame: Fame = value; break;
                    case StatKind.Talk: Talk = value; break;
                    case StatKind.Alcohol: Alcohol = value; break;
                    case StatKind.Looks: Looks = value; break;
                    case StatKind.Morale: Morale = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }
        }

        public static Stats Uniform(double value) => new Stats
        {
            Regulars = value,
            Fame = value,
            Talk = value,
            Alcohol = value,
            Looks = value,
            Morale = value,
        };

        public override string ToString() =>
            $"集客{Regulars:0.#} 知名度{Fame:0.#} トーク{Talk:0.#} " +
            $"酒{Alcohol:0.#} ルックス{Looks:0.#} モラル{Morale:0.#}";
    }
}
