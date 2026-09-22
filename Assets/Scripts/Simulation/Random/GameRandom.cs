using System;
using System.Collections.Generic;

namespace Concafe.Simulation.Rng
{
    /// <summary>
    /// SplitMix64。状態が ulong 1 つなのでセーブに含められる。
    ///
    /// 不変条件 11: UnityEngine.Random を使わない。理由は 3 つ。
    ///   1. 静的状態のため、テストの並列実行で結果が汚染される
    ///   2. 状態をセーブできない
    ///   3. Simulation 層が UnityEngine を参照しない方針に反する
    /// </summary>
    public sealed class GameRandom
    {
        const ulong Gamma = 0x9E3779B97F4A7C15UL;

        ulong state;

        // 正規分布は 1 回の計算で 2 つ得られる。余りを持ち越す。
        double spareGaussian;
        bool hasSpareGaussian;

        public GameRandom(ulong seed) { state = seed; }

        /// <summary>セーブから復元する。</summary>
        public static GameRandom Restore(ulong state) => new GameRandom(state);

        /// <summary>セーブに書き出す値。</summary>
        public ulong State => state;

        public ulong NextULong()
        {
            state += Gamma;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1)。</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>[minInclusive, maxExclusive)。</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentException("max が min 以下", nameof(maxExclusive));

            ulong span = (ulong)((long)maxExclusive - minInclusive);

            // 剰余の偏りを落とす。span が 2 のべき乗でないとき、
            // 上位の端数を捨てて一様性を保つ。
            ulong limit = ulong.MaxValue - (ulong.MaxValue % span);
            ulong v;
            do { v = NextULong(); } while (v >= limit);

            return minInclusive + (int)(v % span);
        }

        public bool Chance(double probability)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            return NextDouble() < probability;
        }

        /// <summary>重み付き抽選。重みの合計が 0 以下なら -1。</summary>
        public int WeightedPick(IReadOnlyList<double> weights)
        {
            if (weights == null || weights.Count == 0) return -1;

            double total = 0;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0) total += weights[i];

            if (total <= 0) return -1;

            double roll = NextDouble() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0) continue;
                roll -= weights[i];
                if (roll < 0) return i;
            }

            // 浮動小数の誤差で末尾に落ちた場合。最後の正の重みを返す。
            for (int i = weights.Count - 1; i >= 0; i--)
                if (weights[i] > 0) return i;

            return -1;
        }

        /// <summary>候補から 1 つ選ぶ。空なら例外。</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("候補が空", nameof(items));
            return items[NextInt(0, items.Count)];
        }

        /// <summary>Box-Muller 法。平均 mean、標準偏差 stdDev。</summary>
        public double NextGaussian(double mean, double stdDev)
        {
            if (hasSpareGaussian)
            {
                hasSpareGaussian = false;
                return mean + stdDev * spareGaussian;
            }

            double u1, u2, s;
            do
            {
                u1 = NextDouble() * 2.0 - 1.0;
                u2 = NextDouble() * 2.0 - 1.0;
                s = u1 * u1 + u2 * u2;
            } while (s >= 1.0 || s == 0.0);

            double factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
            spareGaussian = u2 * factor;
            hasSpareGaussian = true;
            return mean + stdDev * (u1 * factor);
        }

        /// <summary>正規分布を範囲で切る。キャストのパラメータ生成に使う。</summary>
        public double NextGaussianClamped(double mean, double stdDev, double min, double max)
        {
            double v = NextGaussian(mean, stdDev);
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }

    /// <summary>
    /// 用途ごとに独立した乱数列。
    ///
    /// 1 本を共有すると、注文処理の呼び出し回数がわずかに変わっただけで天候まで
    /// 変わり、バランス調整時に前後の比較ができなくなる（設計書 9）。
    /// </summary>
    public sealed class RandomStreams
    {
        public GameRandom CastGen { get; private set; }
        public GameRandom Weather { get; private set; }
        public GameRandom Orders { get; private set; }
        public GameRandom Events { get; private set; }

        RandomStreams() { }

        public static RandomStreams FromSeed(ulong seed)
        {
            // 同じ種から派生させつつ、各列が相関しないよう別の定数で撹拌する。
            return new RandomStreams
            {
                CastGen = new GameRandom(Mix(seed, 0xC2B2AE3D27D4EB4FUL)),
                Weather = new GameRandom(Mix(seed, 0x165667B19E3779F9UL)),
                Orders = new GameRandom(Mix(seed, 0x27D4EB2F165667C5UL)),
                Events = new GameRandom(Mix(seed, 0x9E3779B185EBCA87UL)),
            };
        }

        public static RandomStreams Restore(ulong castGen, ulong weather, ulong orders, ulong events)
        {
            return new RandomStreams
            {
                CastGen = GameRandom.Restore(castGen),
                Weather = GameRandom.Restore(weather),
                Orders = GameRandom.Restore(orders),
                Events = GameRandom.Restore(events),
            };
        }

        static ulong Mix(ulong seed, ulong salt)
        {
            ulong z = seed ^ salt;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
