using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    public static class DepartureRules
    {
        /// <summary>
        /// 市場価値が閾値以上なら転生、未満なら飛ぶ（設計書 4.8）。
        /// 育てたキャストほど、飛ばれるのではなく引き抜かれるようになる。
        /// </summary>
        public static DepartureKind NaturalKind(Cast cast, BalanceData balance) =>
            cast.MarketValue >= balance.Departure.DefectThreshold
                ? DepartureKind.Defected
                : DepartureKind.RanAway;

        /// <summary>
        /// 予兆を出す対象か。
        ///
        /// 不変条件 20: 予兆は転生しうるキャストにしか出ない。飛ぶ側には出さない。
        /// この非対称が「売上を作るキャストほど目をかけ続ける必要がある」という
        /// 圧力を作っている。
        /// </summary>
        public static bool ShouldWarn(Cast cast, BalanceData balance) =>
            FatigueRules.ShouldWarn(cast, balance)
            && NaturalKind(cast, balance) == DepartureKind.Defected;

        /// <summary>
        /// 疲労限界を超えたキャストを店から外し、損失を店舗に反映する。
        ///
        /// 飛ぶと転生の差は「悪評の波及」だけである（不変条件 19）。
        /// 転生は痛いが店に傷は残らない。飛ばれると悪評が広がり、
        /// 残ったキャスト全員の固定客が削られる。
        /// </summary>
        public static DepartureRecord Depart(
            Shop shop, Cast cast, BalanceData balance, EasterEggSet eggs)
        {
            var natural = NaturalKind(cast, balance);
            var outcome = EasterEggRules.ResolveBurnout(cast, natural, eggs);

            var record = new DepartureRecord
            {
                CastId = cast.Id,
                StageName = cast.StageName,
                Kind = outcome.Kind,
                Override = outcome.Override,
                Trigger = DepartureTrigger.Burnout,
                EventId = outcome.EventId,
                LostRegulars = cast.Current.Regulars,
                AntiSpreadTotal = 0,
            };

            shop.Roster.Remove(cast);

            // 固定客はそのキャストと一緒に失われる。飛ぶ場合も転生する場合も同じ。
            // Regulars を保存していないので、ロスターから外すこと自体が損失になる。

            if (outcome.Kind == DepartureKind.RanAway && outcome.Override == DepartureOverride.None)
                record.AntiSpreadTotal = SpreadBadReputation(shop, balance);

            return record;
        }

        /// <summary>
        /// 炎上してそのまま去る場合。
        ///
        /// 不名誉な去り方なので、飛ぶのと同じく悪評が広がる。
        /// 放火は疲労限界超過のときだけなので、ここでは起きない。
        /// </summary>
        public static DepartureRecord DepartInDisgrace(
            Shop shop, Cast cast, BalanceData balance)
        {
            var record = new DepartureRecord
            {
                CastId = cast.Id,
                StageName = cast.StageName,
                Kind = DepartureKind.RanAway,
                Override = DepartureOverride.None,
                Trigger = DepartureTrigger.Scandal,
                LostRegulars = cast.Current.Regulars,
            };

            shop.Roster.Remove(cast);
            record.AntiSpreadTotal = SpreadBadReputation(shop, balance);
            return record;
        }

        /// <summary>
        /// 飛ばれたときの悪評。残ったキャスト全員の固定客を削る。
        /// 0 を下回らせない。
        /// </summary>
        static double SpreadBadReputation(Shop shop, BalanceData balance)
        {
            double amount = balance.Departure.AntiSpread;
            if (amount <= 0) return 0;

            double total = 0;
            foreach (var other in shop.Roster)
            {
                double before = other.Current.Regulars;
                double after = before - amount;
                if (after < 0) after = 0;

                other.Current.Regulars = after;
                total += before - after;
            }
            return total;
        }
    }
}
