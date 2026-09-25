using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    /// <summary>発注画面の 1 行。</summary>
    public struct StockLine
    {
        public StockKey Key;
        public string DisplayName;
        public int Count;
        public long UnitCostYen;
        /// <summary>売れる状態か。ボトルは解禁されていないと売れない（不変条件 5）。</summary>
        public bool Sellable;
        /// <summary>売れない理由。売れるなら null。</summary>
        public string LockedReason;
        /// <summary>冷蔵庫の保管上限を数える品目か。</summary>
        public bool Chilled;
    }

    /// <summary>週頭にプレイヤーへ見せるひとまとまり。</summary>
    public sealed class WeekBriefing
    {
        public int AbsoluteWeek;
        public int Year;
        public int Month;
        public int WeekOfMonth;

        public long CashYen;
        public int ManagerAp;
        public int CastSlots;
        public int CastCount;
        public int TrainingBooks;

        /// <summary>今週の固定費。売上が立つ前から出ていく額。</summary>
        public long WeeklyRentYen;
        public long WeeklyManagerPayYen;

        /// <summary>週頭に見せる予報。実際と食い違うことがある（設計書 5.2）。</summary>
        public Weather[] Forecast;
        /// <summary>予報が当たる確率。先の日ほど下がる。</summary>
        public IReadOnlyList<double> ForecastAccuracy;

        public int ChilledCount;
        public int FridgeCapacity;

        public List<StockLine> Stock = new List<StockLine>();

        /// <summary>今週起きた専用イベント。シフトを組む前に読める。</summary>
        public List<EggEventRecord> EggEvents = new List<EggEventRecord>();
        public AlumniGiftRecord Gifts;

        /// <summary>この週に誕生日を迎えるキャストの ID。20 歳の覚醒を見逃さないため。</summary>
        public List<string> BirthdayCastIds = new List<string>();
    }

    /// <summary>
    /// 週頭の画面が必要とする情報を 1 か所に集める（段階 7）。
    ///
    /// UI がゲームの状態を歩き回って組み立てると、**同じ計算が画面ごとに
    /// 散らばる。** ボトルの解禁判定（不変条件 5）のように破ると壊れる規則が
    /// UI 側に複製されるのが特に危ない。ここを唯一の出口にしてある。
    /// </summary>
    public static class WeekBriefingBuilder
    {
        public static WeekBriefing Build(GameState state)
        {
            var balance = state.Balance;
            var shop = state.Shop;
            var cal = state.Calendar;

            var b = new WeekBriefing
            {
                AbsoluteWeek = state.Week.AbsoluteWeek,
                Year = cal.YearOf(state.Week) + 1,
                Month = cal.MonthOf(state.Week) + 1,
                WeekOfMonth = state.Week.AbsoluteWeek % cal.WeeksPerMonth + 1,

                CashYen = shop.CashYen,
                ManagerAp = shop.ManagerAp,
                CastSlots = shop.CastSlots,
                CastCount = shop.Roster.Count,
                TrainingBooks = state.TrainingBooks,

                WeeklyRentYen = shop.WeeklyRentYen(balance),
                WeeklyManagerPayYen = balance.Cost.WeeklyManagerPayYen,

                Forecast = (Weather[])state.Forecast.Clone(),
                ForecastAccuracy = balance.WeatherGen.ForecastAccuracy,

                ChilledCount = shop.ChilledCount,
                FridgeCapacity = shop.FridgeCapacity(balance),

                Gifts = state.PendingGifts,
            };

            b.EggEvents.AddRange(state.PendingEggEvents);

            foreach (var c in shop.Roster)
                if (cal.IsBirthdayWeek(state.Week, c.BirthWeekOfYear))
                    b.BirthdayCastIds.Add(c.Id);

            BuildStock(state, b);
            return b;
        }

        /// <summary>
        /// 発注の一覧。
        ///
        /// **「今の店で何が売れるか」を映す**（設計書 8）。解禁されていない段階の
        /// ボトルは仕入れても絶対に売れない（不変条件 5）ので、買える一覧に
        /// 並べるだけにして、売れない旨をここで添える。解禁段階はキャストの
        /// 育成で動くため、この一覧がそのまま育成の進捗計になる。
        /// </summary>
        static void BuildStock(GameState state, WeekBriefing b)
        {
            var balance = state.Balance;
            var shop = state.Shop;

            void Add(StockKey key, string name)
            {
                b.Stock.Add(new StockLine
                {
                    Key = key,
                    DisplayName = name,
                    Count = shop.Stock[key],
                    UnitCostYen = ManagerActions.UnitCostYen(balance, key, state.Alumni),
                    Sellable = true,
                    Chilled = Shop.IsChilled(key.Item),
                });
            }

            Add(StockKey.SoftDrink, "ソフトドリンク");
            Add(StockKey.Alcohol, "アルコール");
            Add(StockKey.ChekiFilm, "チェキフィルム");
            Add(StockKey.Limited, "期間限定商品");

            AddBottles(state, b, StockItem.BottleAlcoholic, balance.Bottles.Alcoholic, "ボトル");
            AddBottles(state, b, StockItem.BottleNonAlcoholic, balance.Bottles.NonAlcoholic, "ノンアル");
        }

        static void AddBottles(
            GameState state, WeekBriefing b, StockItem item,
            IReadOnlyList<BottleTierBalance> tiers, string label)
        {
            var balance = state.Balance;

            // その段階を解禁しているキャストが 1 人でもいれば売れる。
            for (int t = 0; t < tiers.Count; t++)
            {
                string unlockedBy = null;
                foreach (var c in state.Shop.Roster)
                {
                    bool matchesAge = item == StockItem.BottleNonAlcoholic ? c.IsMinor : !c.IsMinor;
                    if (!matchesAge) continue;
                    if (OrderRules.UnlockedTierCount(c, balance) > t) { unlockedBy = c.StageName; break; }
                }

                var key = StockKey.Bottle(item, t);
                b.Stock.Add(new StockLine
                {
                    Key = key,
                    DisplayName = $"{label} {tiers[t].PriceYen:N0}円",
                    Count = state.Shop.Stock[key],
                    UnitCostYen = tiers[t].CostYen,
                    Sellable = unlockedBy != null,
                    LockedReason = unlockedBy != null ? null : LockReason(item, t, tiers, balance),
                    Chilled = true,
                });
            }
        }

        static string LockReason(
            StockItem item, int tier, IReadOnlyList<BottleTierBalance> tiers, BalanceData balance)
        {
            string who = item == StockItem.BottleNonAlcoholic ? "未成年" : "20 歳以上";
            return $"まだ売れない（{who}のキャストで tierPower {tiers[tier].Unlock:0.00} が要る）";
        }
    }
}
