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

        /// <summary>
        /// バックの対象になる売上。チェキ・キャストドリンク・ボトルのみ。
        /// チャージと客のドリンクは店の売上なので含めない（設計書 14.3）。
        /// </summary>
        public long BackableSalesYen;

        public int LostOrders;
        public long LostSalesYen;

        /// <summary>接客した延べ時間。疲労はこれに比例する。</summary>
        public int GuestHours;

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
        /// 滞在を 1 時間延ばす確率。トークで伸びる。
        ///
        /// トークは客を呼ぶステータスではなく、呼んだ客を長く座らせる
        /// ステータスである。集客・知名度が「何人来るか」を決め、
        /// トークが「1 人からいくら取れるか」を決める。
        /// </summary>
        public static double ExtendChance(Cast cast, BalanceData balance)
        {
            double max = balance.CastGen.StatMax;
            double t = max <= 0 ? 0 : cast.Current.Talk / max;
            if (t < 0) t = 0;
            if (t > 1) t = 1;

            double p = balance.Visit.ExtendBase + t * balance.Visit.ExtendTalk;
            if (p < 0) return 0;
            if (p > 1) return 1;
            return p;
        }

        /// <summary>期待滞在時間。表示とバランス確認用。</summary>
        public static double ExpectedStayHours(Cast cast, BalanceData balance)
        {
            double p = ExtendChance(cast, balance);
            int max = balance.Visit.MaxStayHours;

            double sum = 0, weight = 1;
            for (int h = 1; h <= max; h++)
            {
                sum += weight;
                weight *= p;
            }
            return sum;
        }

        /// <summary>
        /// 1 日分の接客。客ごとに滞在時間を決め、1 時間ごとに注文を判定する。
        /// </summary>
        public static ServiceResult Serve(
            Cast cast, int guests, int shiftHours,
            BalanceData balance, Inventory stock, GameRandom rng)
        {
            var r = new ServiceResult();
            if (guests <= 0 || shiftHours <= 0) return r;

            double chekiRate = ChekiRate(cast, balance);
            double drinkRate = CastDrinkRate(cast, balance);
            double bottleRate = BottleRate(cast, balance);
            double extend = ExtendChance(cast, balance);

            var tiers = TiersFor(cast, balance);
            int unlockedCount = UnlockedTierCount(cast, balance);
            var bottleItem = BottleStockItem(cast);
            bool minor = cast.IsMinor;

            // 客はそのキャストのシフトより長くは居られない。
            int maxStay = balance.Visit.MaxStayHours;
            if (shiftHours < maxStay) maxStay = shiftHours;

            long charge = balance.Visit.ChargePerHourYen;

            for (int g = 0; g < guests; g++)
            {
                // 滞在時間を決める。1 時間から始め、延長判定で伸ばす。
                int hours = 1;
                while (hours < maxStay && rng.Chance(extend)) hours++;

                r.GuestHours += hours;

                for (int h = 0; h < hours; h++)
                {
                    // チャージは席と時間の対価なので、在庫に関係なく必ず入る。
                    r.SalesYen += charge;

                    // 客のドリンク。1 時間あたり 1 杯（設計書 4.3）。在庫を消費する。
                    ServeBaseDrink(balance, stock, ref r);

                    if (rng.Chance(chekiRate))
                        Consume(StockKey.ChekiFilm, balance.Items.ChekiFilm.PriceYen, stock, ref r);

                    if (rng.Chance(drinkRate))
                        ServeCastDrink(minor, balance, stock, ref r);

                    if (rng.Chance(bottleRate))
                        ServeBottle(tiers, unlockedCount, bottleItem, stock, rng, ref r);
                }
            }

            return r;
        }

        /// <summary>
        /// 客が自分で飲む 1 杯。
        ///
        /// **キャストの年齢は関係ない。** 年齢制限の対象はキャストが口にする
        /// キャストドリンクと、キャストが付くボトルだけである（企画書 3.1）。
        /// 客が酒を飲むことを未成年キャストが止める理由はない。
        ///
        /// アルコールとソフトドリンクのどちらでもよく、あるものから出す。
        /// 品目を分けて持っているのはキャストドリンクとボトルの年齢判定のためで、
        /// 客のドリンクでまで分けると、未成年中心のシフトのときに
        /// ソフトドリンクを切らしただけで売上が止まってしまう。
        /// </summary>
        static void ServeBaseDrink(BalanceData balance, Inventory stock, ref ServiceResult r)
        {
            long price = balance.Visit.GuestDrinkPriceYen;

            // 売価は一律。違うのは原価だけなので、在庫のあるほうから出す。
            if (stock.TryConsume(StockKey.Alcohol) || stock.TryConsume(StockKey.SoftDrink))
            {
                r.SalesYen += price;
                return;
            }

            // どちらも無い。飲み物を出せなかった。
            r.LostOrders++;
            r.LostSalesYen += price;
        }

        static void ServeCastDrink(bool minor, BalanceData balance, Inventory stock, ref ServiceResult r)
        {
            long price = balance.CastDrinkPriceYen;

            if (minor)
            {
                // アルコールはソフトドリンクに変換される（要件 4.3）。
                if (stock.TryConsume(StockKey.SoftDrink)) Earn(price, ref r);
                else { r.LostOrders++; r.LostSalesYen += price; }
                return;
            }

            if (stock.TryConsume(StockKey.Alcohol))
            {
                Earn(price, ref r);
                r.AlcoholDrinks++;
                return;
            }

            // 酒が切れていたらソフトドリンクで出す。酒疲労は発生しない。
            if (stock.TryConsume(StockKey.SoftDrink)) Earn(price, ref r);
            else { r.LostOrders++; r.LostSalesYen += price; }
        }

        /// <summary>バック対象の売上を計上する。</summary>
        static void Earn(long price, ref ServiceResult r)
        {
            r.SalesYen += price;
            r.BackableSalesYen += price;
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

            // v1 は一様抽選（設計書 4.3.3）。重み付けは段階 5 で実測してから決める。
            int index = candidates[rng.NextInt(0, candidates.Count)];
            if (!stock.TryConsume(StockKey.Bottle(item, index))) return;

            Earn(tiers[index].PriceYen, ref r);
            r.BottlesSold++;
            // ノンアルコールは fatigueTier が 0 なので酒疲労を生まない。
            r.BottleFatigueTiers += tiers[index].FatigueTier;
        }

        /// <summary>チェキ用。バック対象。</summary>
        static void Consume(StockKey key, long price, Inventory stock, ref ServiceResult r)
        {
            if (stock.TryConsume(key)) Earn(price, ref r);
            else { r.LostOrders++; r.LostSalesYen += price; }
        }
    }
}
