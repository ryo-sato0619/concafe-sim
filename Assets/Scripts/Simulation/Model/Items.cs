using System;
using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    /// <summary>
    /// 在庫として持つ品目。
    ///
    /// 企画書はソフトドリンクとアルコールを 1 行で括っているが、未成年ロックの
    /// 対象が違うため内部では別品目として持つ（設計書 3.3）。発注 UI では
    /// 隣り合わせで見せる。
    /// </summary>
    public enum StockItem
    {
        SoftDrink,
        Alcohol,
        ChekiFilm,
        Limited,
        BottleAlcoholic,
        BottleNonAlcoholic,
    }

    public static class StockItems
    {
        public static bool IsBottle(StockItem item) =>
            item == StockItem.BottleAlcoholic || item == StockItem.BottleNonAlcoholic;

        /// <summary>未成年の担当客に出してはならない品目（不変条件 3）。</summary>
        public static bool IsAlcoholic(StockItem item) =>
            item == StockItem.Alcohol || item == StockItem.BottleAlcoholic;
    }

    /// <summary>
    /// 在庫の 1 スロット。ボトルは段階ごとに別在庫なので、段階番号まで含めて
    /// はじめて在庫のキーになる。
    /// </summary>
    public readonly struct StockKey : IEquatable<StockKey>
    {
        public StockItem Item { get; }

        /// <summary>ボトルの段階（0 起点）。ボトル以外は -1。</summary>
        public int TierIndex { get; }

        StockKey(StockItem item, int tierIndex)
        {
            Item = item;
            TierIndex = tierIndex;
        }

        public static StockKey Simple(StockItem item)
        {
            if (StockItems.IsBottle(item))
                throw new ArgumentException("ボトルは段階の指定が要る", nameof(item));
            return new StockKey(item, -1);
        }

        public static StockKey Bottle(StockItem item, int tierIndex)
        {
            if (!StockItems.IsBottle(item))
                throw new ArgumentException("ボトルではない", nameof(item));
            if (tierIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(tierIndex));
            return new StockKey(item, tierIndex);
        }

        public static readonly StockKey SoftDrink = Simple(StockItem.SoftDrink);
        public static readonly StockKey Alcohol = Simple(StockItem.Alcohol);
        public static readonly StockKey ChekiFilm = Simple(StockItem.ChekiFilm);
        public static readonly StockKey Limited = Simple(StockItem.Limited);

        public bool Equals(StockKey other) => Item == other.Item && TierIndex == other.TierIndex;
        public override bool Equals(object obj) => obj is StockKey k && Equals(k);
        public override int GetHashCode() => ((int)Item * 397) ^ TierIndex;

        public override string ToString() =>
            StockItems.IsBottle(Item) ? $"{Item}[段階{TierIndex + 1}]" : Item.ToString();
    }

    /// <summary>在庫。個数のみを持つ。金額は balance.json 側にある。</summary>
    public sealed class Inventory
    {
        readonly Dictionary<StockKey, int> counts = new Dictionary<StockKey, int>();

        public int this[StockKey key] => counts.TryGetValue(key, out int n) ? n : 0;

        public IEnumerable<KeyValuePair<StockKey, int>> Entries() => counts;

        public void Add(StockKey key, int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return;
            counts[key] = this[key] + amount;
        }

        /// <summary>1 つ消費する。在庫が無ければ false（機会損失として記録する）。</summary>
        public bool TryConsume(StockKey key)
        {
            int have = this[key];
            if (have <= 0) return false;

            if (have == 1) counts.Remove(key);
            else counts[key] = have - 1;
            return true;
        }

        public int TotalCount
        {
            get
            {
                int sum = 0;
                foreach (var kv in counts) sum += kv.Value;
                return sum;
            }
        }

        public void Clear() => counts.Clear();
    }
}
