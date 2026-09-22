using System.Globalization;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    /// <summary>
    /// 候補の生成と、採用によるキャスト化。
    ///
    /// 源氏名はプレイヤーが採用時に入力するため、候補の段階では名前を持たない。
    /// イースターエッグは名前を見て発動するので、Hire でのみ適用される（企画書 4）。
    /// </summary>
    public static class CastFactory
    {
        /// <summary>
        /// 求人ティアから候補を 1 人生成する。
        /// serial は ID の通し番号。同じ種と同じ serial なら必ず同じ候補になる。
        /// </summary>
        public static Applicant GenerateApplicant(
            GameRandom rng, BalanceData balance, int recruitTierIndex, int serial,
            double capBonus = 0)
        {
            var gen = balance.CastGen;
            var tier = balance.Recruit[recruitTierIndex];
            double statMax = gen.StatMax;

            var a = new Applicant
            {
                Id = "cast-" + serial.ToString("D4", CultureInfo.InvariantCulture),
                Age = rng.NextInt(gen.MinAge, gen.MaxAge + 1),
                BirthWeekOfYear = rng.NextInt(0, balance.Time.WeeksPerYear),
                StaminaTier = rng.WeightedPick(gen.StaminaTierWeights) + 1,
                ScandalResist = (int)System.Math.Round(
                    rng.NextGaussianClamped(gen.ScandalResistMean, gen.ScandalResistSpread, 0, 100)),
            };

            foreach (var kind in Stats.AllKinds)
            {
                // 上限が個体差の本体。求人ティアが高いほど平均が上がる。
                // 腕利きのスカウトマン OB がいれば平均がさらに上がる。
                double cap = rng.NextGaussianClamped(
                    tier.CapMean + capBonus, tier.CapSpread, 1, statMax);
                a.Cap[kind] = cap;

                // 初期値は上限を超えない。
                double initial = rng.NextGaussianClamped(
                    gen.InitialStatMean, gen.InitialStatSpread, 0, statMax);
                a.InitialStats[kind] = initial > cap ? cap : initial;

                // 成長率が 0 だと永久に伸びないため、下限を置く。
                double rate = rng.NextGaussian(gen.GrowthRateMean, gen.GrowthRateSpread);
                a.GrowthRate[kind] = rate < 0.05 ? 0.05 : rate;
            }

            a.Look = GenerateAppearance(rng, balance.Appearance);
            return a;
        }

        public static Appearance GenerateAppearance(GameRandom rng, AppearanceBalance app)
        {
            return new Appearance
            {
                Body = rng.NextInt(0, app.BodyVariants),
                Dress = rng.NextInt(0, app.DressVariants),
                HairBack = rng.NextInt(0, app.HairBackVariants),
                HairFront = rng.NextInt(0, app.HairFrontVariants),
                Eye = rng.NextInt(0, app.EyeVariants),
                // 0 は「アクセサリ無し」。
                Accessory = app.AccessoryVariants > 0 ? rng.NextInt(0, app.AccessoryVariants + 1) : 0,

                SkinColor = rng.NextInt(0, app.SkinColors),
                DressColor = rng.NextInt(0, app.DressColors),
                HairColor = rng.NextInt(0, app.HairColors),
                EyeColor = rng.NextInt(0, app.EyeColors),
            };
        }

        /// <summary>
        /// 候補を採用してキャストにする。源氏名がイースターエッグに一致すれば、
        /// ここで初期パラメータ・成長率・疲労限界・隠しパラメータが差し替わる。
        ///
        /// 疲労限界と隠しパラメータの固定は「生成時に決まる」の範囲内であり、
        /// 不変条件 1 に反しない。以後は誰も触らない。
        /// </summary>
        public static Cast Hire(Applicant applicant, string stageName,
                                BalanceData balance, EasterEggSet eggs, int currentWeek)
        {
            var cast = new Cast
            {
                Id = applicant.Id,
                StageName = stageName,
                Age = applicant.Age,
                BirthWeekOfYear = applicant.BirthWeekOfYear,
                HiredWeek = currentWeek,
                Current = applicant.InitialStats,
                Cap = applicant.Cap,
                GrowthRate = applicant.GrowthRate,
                StaminaTier = applicant.StaminaTier,
                ScandalResist = applicant.ScandalResist,
                Fatigue = 0,
                // 不変条件 17: 信頼度は素質ではない。必ず 0 から始める。
                Trust = 0,
                LifetimeSalesYen = 0,
                Look = applicant.Look,
                EasterEggId = null,
            };

            var egg = eggs?.Find(stageName);
            if (egg != null) Apply(cast, egg, balance);

            return cast;
        }

        static void Apply(Cast cast, EasterEgg egg, BalanceData balance)
        {
            cast.EasterEggId = egg.Id;
            double statMax = balance.CastGen.StatMax;

            foreach (var kind in Stats.AllKinds)
            {
                // 上限を先に決める。初期値は上限を超えられないため。
                double cap = (cast.Cap[kind] + egg.CapBonus.For(kind)) * egg.CapMultiplier.For(kind);
                cap = Clamp(cap, 1, statMax);
                cast.Cap[kind] = cap;

                double cur = (cast.Current[kind] + egg.StatBonus.For(kind)) * egg.StatMultiplier.For(kind);
                cast.Current[kind] = Clamp(cur, 0, cap);

                double rate = cast.GrowthRate[kind] * egg.GrowthRateMultiplier.For(kind);
                cast.GrowthRate[kind] = rate < 0 ? 0 : rate;
            }

            if (egg.HasFixedStaminaTier) cast.StaminaTier = egg.FixedStaminaTier;
            if (egg.HasFixedScandalResist) cast.ScandalResist = egg.FixedScandalResist;
        }

        static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
