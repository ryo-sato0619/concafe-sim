using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Rules
{
    /// <summary>キャスト 1 人が 1 日で生んだ結果。</summary>
    public struct ServiceResult
    {
        public long SalesYen;
        public int LostOrders;
        public long LostSalesYen;

        /// <summary>酒のキャストドリンクの杯数。疲労の計算に使う。</summary>
        public int AlcoholDrinks;
        /// <summary>開けたアルコールボトルの段階番号（1 起点）の合計。</summary>
        public int BottleFatigueTiers;
        public int BottlesSold;
    }

    public static class OrderRules
    {
        /// <summary>トークの係数。talkFloor 〜 talkFloor + talkSpan。</summary>
        public static double TalkFactor(Cast cast, BalanceData balance)
        {
            double max = balance.CastGen.StatMax;
            if (max <= 0) return balance.Orders.TalkFloor;
            double t = cast.Current.Talk / max;
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return balance.Orders.TalkFloor + t * balance.Orders.TalkSpan;
        }

        public static double ChekiRate(Cast cast, BalanceData balance) =>
            balance.Orders.ChekiRate * TalkFactor(cast, balance)
                                     * VisitorRules.LooksMultiplier(cast, balance);

        public static double CastDrinkRate(Cast cast, BalanceData balance) =>
            balance.Orders.CastDrinkRate * TalkFactor(cast, balance)
                                         * VisitorRules.LooksMultiplier(cast, balance);

        /// <summary>
        /// ボトルの注文率。
        ///
        /// 不変条件 2: ルックス倍率を掛けない。掛けるとルックスが全ステータスの
        /// 上位互換になり、トーク・酒耐性・モラルの存在意義が消える。
        ///
        /// なおルックスは知名度経由で「来客数」を増やすため、売れるボトルの
        /// 本数は間接的に増える。禁じているのは率に掛けることだけである。
        /// </summary>
        public static double BottleRate(Cast cast, BalanceData balance) =>
            balance.Orders.BottleRate * TalkFactor(cast, balance);

        /// <summary>
        /// そのキャストが扱うボトルの一覧。
        /// 不変条件 3: 未成年はノンアルコールのみ。
        /// </summary>
        public static IReadOnlyList<BottleTierBalance> TiersFor(Cast cast, BalanceData balance) =>
            cast.IsMinor ? balance.Bottles.NonAlcoholic : balance.Bottles.Alcoholic;

        /// <summary>
        /// 解禁されている段階の数。
        ///
        /// 検査が unlock の単調増加を保証しているため（BalanceLoader）、
        /// 解禁済みの段階は常に先頭からの連続になる。添字を探す必要はない。
        ///
        /// 不変条件 4: 未成年の tierPower に酒耐性は入らない。
        /// 不変条件 5: 解禁されていない段階は決して売らせない。
        /// </summary>
        public static int UnlockedTierCount(Cast cast, BalanceData balance)
        {
            var tiers = TiersFor(cast, balance);
            double power = cast.TierPower(balance);

            int n = 0;
            while (n < tiers.Count && power >= tiers[n].Unlock) n++;
            return n;
        }

        static StockItem BottleStockItem(Cast cast) =>
            cast.IsMinor ? StockItem.BottleNonAlcoholic : StockItem.BottleAlcoholic;

        /// <summary>
        /// 1 日分の接客。客ごとに注文を判定して在庫を減らす。
        /// </summary>
        public static ServiceResult Serve(
            Cast cast, int guests, BalanceData balance, Inventory stock, GameRandom rng)
        {
            var r = new ServiceResult();
            if (guests <= 0) return r;

            double chekiRate = ChekiRate(cast, balance);
            double drinkRate = CastDrinkRate(cast, balance);
            double bottleRate = BottleRate(cast, balance);

            var tiers = TiersFor(cast, balance);
            int unlockedCount = UnlockedTierCount(cast, balance);
            var bottleItem = BottleStockItem(cast);
            bool minor = cast.IsMinor;

            for (int g = 0; g < guests; g++)
            {
                // 来客は必ず自分の飲み物を 1 杯注文する（設計書 4.3）。確率判定はしない。
                ServeBaseDrink(minor, balance, stock, ref r);

                if (rng.Chance(chekiRate))
                    Consume(StockKey.ChekiFilm, balance.Items.ChekiFilm.PriceYen, stock, ref r);

                if (rng.Chance(drinkRate))
                    ServeCastDrink(minor, balance, stock, ref r);

                if (rng.Chance(bottleRate))
                    ServeBottle(tiers, unlockedCount, bottleItem, stock, rng, ref r);
            }

            return r;
        }

        static void ServeBaseDrink(bool minor, BalanceData balance, Inventory stock, ref ServiceResult r)
        {
            // 未成年の担当客にはアルコール在庫を触らせない（不変条件 3）。
            if (!minor && stock.TryConsume(StockKey.Alcohol))
            {
                r.SalesYen += balance.Items.Alcohol.PriceYen;
                return;
            }

            if (stock.TryConsume(StockKey.SoftDrink))
            {
                r.SalesYen += balance.Items.SoftDrink.PriceYen;
                return;
            }

            // どちらも無い。飲み物を出せなかった。
            r.LostOrders++;
            r.LostSalesYen += minor
                ? balance.Items.SoftDrink.PriceYen
                : balance.Items.Alcohol.PriceYen;
        }

        static void ServeCastDrink(bool minor, BalanceData balance, Inventory stock, ref ServiceResult r)
        {
            long price = balance.CastDrinkPriceYen;

            if (minor)
            {
                // アルコールはソフトドリンクに変換される（要件 4.3）。
                if (stock.TryConsume(StockKey.SoftDrink)) r.SalesYen += price;
                else { r.LostOrders++; r.LostSalesYen += price; }
                return;
            }

            if (stock.TryConsume(StockKey.Alcohol))
            {
                r.SalesYen += price;
                r.AlcoholDrinks++;
                return;
            }

            // 酒が切れていたらソフトドリンクで出す。酒疲労は発生しない。
            if (stock.TryConsume(StockKey.SoftDrink)) r.SalesYen += price;
            else { r.LostOrders++; r.LostSalesYen += price; }
        }

        static void ServeBottle(
            IReadOnlyList<BottleTierBalance> tiers, int unlockedCount, StockItem item,
            Inventory stock, GameRandom rng, ref ServiceResult r)
        {
            if (unlockedCount == 0) return;   // 1 段階も解禁されていない。注文は起きない。

            // 解禁済みのうち、在庫がある段階だけを候補にする。
            //
            // 在庫の無い段階を引いて機会損失にすると、9 段階すべてを常備しないと
            // 損が出続けることになり、発注が「全部買う」以外の手にならなくなる。
            // 在庫のある段階から出すことで、「安い酒しか置いていない日は
            // 安い酒しか売れない」という機会費用の形にしてある。
            var candidates = new List<int>(unlockedCount);
            for (int i = 0; i < unlockedCount; i++)
                if (stock[StockKey.Bottle(item, i)] > 0) candidates.Add(i);

            if (candidates.Count == 0)
            {
                // 解禁済みの段階が 1 本も無い。ここは本当の機会損失。
                r.LostOrders++;
                r.LostSalesYen += tiers[0].PriceYen;
                return;
            }

            // v1 は一様抽選（設計書 4.3.1）。重み付けは段階 5 で実測してから決める。
            int index = candidates[rng.NextInt(0, candidates.Count)];
            if (!stock.TryConsume(StockKey.Bottle(item, index))) return;

            r.SalesYen += tiers[index].PriceYen;
            r.BottlesSold++;
            // ノンアルコールは fatigueTier が 0 なので酒疲労を生まない。
            r.BottleFatigueTiers += tiers[index].FatigueTier;
        }

        static void Consume(StockKey key, long price, Inventory stock, ref ServiceResult r)
        {
            if (stock.TryConsume(key)) r.SalesYen += price;
            else { r.LostOrders++; r.LostSalesYen += price; }
        }
    }
}
