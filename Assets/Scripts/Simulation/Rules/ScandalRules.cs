using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    public static class ScandalRules
    {
        /// <summary>
        /// 週末のスキャンダル発生確率（設計書 4.7）。
        ///
        /// 隠しパラメータのスキャンダル耐性が主で、酒を多く出した週ほど上がり、
        /// モラルが高いと下がる。プレイヤーには耐性の数値が見えないため、
        /// 「酒を売らせすぎると危ない」という手触りだけが残る。
        /// </summary>
        public static double Chance(Cast cast, int alcoholDrinksThisWeek, BalanceData balance)
        {
            var s = balance.Scandal;
            double max = balance.CastGen.StatMax;

            double resist = 1.0 - cast.ScandalResist / 100.0;
            if (resist < 0) resist = 0;

            double drinkFactor = 1.0 + alcoholDrinksThisWeek * s.PerDrink;

            double moraleGuard = max <= 0
                ? 1.0
                : 1.0 - (cast.Current.Morale / max) * s.MoraleGuard;
            if (moraleGuard < 0) moraleGuard = 0;

            double p = s.Base * resist * drinkFactor * moraleGuard;
            if (p < 0) return 0;
            if (p > 1) return 1;
            return p;
        }

        /// <summary>
        /// 炎上の結果。
        ///
        /// 知名度と集客が大きく削れる。炎上は「客が引く」出来事なので、
        /// 育てた数値がそのまま失われるのが痛みの本体である。
        /// 一定確率でそのまま店を去る。
        /// </summary>
        public static ScandalRecord Apply(
            Cast cast, BalanceData balance, GameRandom rng)
        {
            var s = balance.Scandal;

            double fameLost = cast.Current.Fame * s.FameLoss;
            double regularsLost = cast.Current.Regulars * s.RegularsLoss;

            cast.Current.Fame -= fameLost;
            cast.Current.Regulars -= regularsLost;
            cast.Current.Morale -= s.MoraleLoss;

            if (cast.Current.Fame < 0) cast.Current.Fame = 0;
            if (cast.Current.Regulars < 0) cast.Current.Regulars = 0;
            if (cast.Current.Morale < 0) cast.Current.Morale = 0;

            bool departs = rng.Chance(s.DepartChance);

            return new ScandalRecord
            {
                CastId = cast.Id,
                StageName = cast.StageName,
                FameLost = fameLost,
                RegularsLost = regularsLost,
                MoraleLost = s.MoraleLoss,
                Departed = departs,
            };
        }
    }

    public static class GraduationRules
    {
        /// <summary>
        /// 卒業の条件（U-08）。
        ///
        /// 在籍年数とモラルを満たしたうえで、年齢か上限到達のどちらかを満たすこと。
        ///
        /// モラルが閾値未満のまま辞める場合は円満卒業にならず、OB 効果も残らない
        /// （不変条件 16）。モラルを「最重要ステータス」たらしめているのはこの分岐である。
        /// </summary>
        public static bool CanGraduate(
            Cast cast, int currentWeek, BalanceData balance, CareerSet careers)
        {
            var g = careers.Graduation;

            if (currentWeek - cast.HiredWeek < g.MinWeeks) return false;
            if (cast.Current.Morale < g.MinMorale) return false;

            if (cast.Age >= g.MinAge) return true;
            return HasReachedCaps(cast, g.CapReachedRatio);
        }

        static bool HasReachedCaps(Cast cast, double ratio)
        {
            foreach (var kind in Stats.AllKinds)
            {
                double cap = cast.Cap[kind];
                if (cap <= 0) continue;
                if (cast.Current[kind] < cap * ratio) return false;
            }
            return true;
        }

        /// <summary>
        /// 進路を決める。パラメータの構成で重み付け抽選する。
        /// プレイヤーが「どう育てたか」が進路に反映される（要件 6.1）。
        /// </summary>
        public static CareerKind PickCareer(
            Cast cast, BalanceData balance, CareerSet careers, GameRandom rng)
        {
            var weights = new double[careers.Careers.Count];
            for (int i = 0; i < careers.Careers.Count; i++)
                weights[i] = careers.Careers[i].WeightFor(cast, balance.CastGen.StatMax);

            int pick = rng.WeightedPick(weights);
            // 全パラメータが 0 のときだけ -1 になる。実家の家業に落とす。
            return pick < 0 ? CareerKind.FamilyBusiness : careers.Careers[pick].Kind;
        }

        /// <summary>円満卒業させ、OB にする。</summary>
        public static Alumnus Graduate(
            Shop shop, Cast cast, int currentWeek,
            BalanceData balance, CareerSet careers, GameRandom rng)
        {
            var alumnus = new Alumnus
            {
                Id = cast.Id,
                StageName = cast.StageName,
                Career = PickCareer(cast, balance, careers, rng),
                GraduatedWeek = currentWeek,
                LifetimeSalesYen = cast.LifetimeSalesYen,
                FinalStats = cast.Current,
            };

            shop.Roster.Remove(cast);
            shop.Alumni.Add(alumnus);

            // 卒業でも固定客は失われる。ただし悪評は広がらない。
            return alumnus;
        }
    }
}
