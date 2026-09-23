using System.Collections.Generic;
using Concafe.Simulation.Balance;

namespace Concafe.Simulation.Model
{
    /// <summary>卒業後の進路。円満卒業したキャストだけが OB になる（不変条件 16）。</summary>
    public enum CareerKind
    {
        /// <summary>1 日店長イベントによる売上増、または出戻り再雇用。</summary>
        Idol,
        /// <summary>ドリンク類の仕入れ原価が永続的に低下。</summary>
        DrinkWholesaler,
        /// <summary>AP 消費なしで育成できる特訓本を定期的に差し入れ。</summary>
        Publisher,
        /// <summary>店舗全体のルックスバフ基礎値が永続微増。</summary>
        CostumeDesigner,
        /// <summary>初期上限値が高い優秀なモブを優先的に紹介。</summary>
        Scout,
        /// <summary>店長 AP による疲労回復量が永続アップ。</summary>
        Counselor,
        /// <summary>悪天候時の知名度減少ペナルティを軽減。</summary>
        WebMarketer,
        /// <summary>原価の高い高額ボトルを定期的に無料で差し入れ。</summary>
        Distillery,
        /// <summary>原価 0 の期間限定商品を提供。</summary>
        FamilyBusiness,
        /// <summary>決算時にまとまった資金を寄付し、経営を救済。</summary>
        MarriedRich,
    }

    public sealed class Alumnus
    {
        public string Id { get; set; }
        public string StageName { get; set; }
        public CareerKind Career { get; set; }
        public int GraduatedWeek { get; set; }
        public long LifetimeSalesYen { get; set; }
        public Stats FinalStats;
    }

    /// <summary>
    /// 資金を使った設備投資の到達段階。店長のレベルアップは持たない（不変条件 6）。
    ///
    /// 各設備の効果と価格は `balance.json` の `facilities` にある。
    /// ここが持つのは「どこまで投資したか」だけである。
    /// </summary>
    public sealed class FacilitySet
    {
        readonly int[] levels = new int[3];

        public int this[FacilityKind kind]
        {
            get => levels[(int)kind];
            set => levels[(int)kind] = value;
        }

        /// <summary>セーブ・ロード用（段階 6）。</summary>
        public int[] ToArray() => (int[])levels.Clone();

        public void Restore(int[] saved)
        {
            for (int i = 0; i < levels.Length && i < saved.Length; i++) levels[i] = saved[i];
        }
    }

    public sealed class Shop
    {
        /// <summary>不変条件 8: 金額は整数の円。float / double を使わない。</summary>
        public long CashYen { get; set; }

        public int CastSlots { get; set; }

        public List<Cast> Roster { get; } = new List<Cast>();
        public List<Applicant> Applicants { get; } = new List<Applicant>();
        public List<Alumnus> Alumni { get; } = new List<Alumnus>();
        public FacilitySet Facilities { get; } = new FacilitySet();

        public Inventory Stock { get; } = new Inventory();

        public int ManagerAp { get; set; }

        public bool HasFreeSlot => Roster.Count < CastSlots;

        /// <summary>
        /// 冷蔵庫に入っている数。冷蔵するのはドリンク類とボトルで、
        /// チェキフィルムと期間限定商品は数えない。
        /// </summary>
        public int ChilledCount
        {
            get
            {
                int sum = 0;
                foreach (var kv in Stock.Entries())
                    if (IsChilled(kv.Key.Item)) sum += kv.Value;
                return sum;
            }
        }

        public static bool IsChilled(StockItem item) =>
            item == StockItem.SoftDrink || item == StockItem.Alcohol
            || StockItems.IsBottle(item);

        /// <summary>冷蔵庫の保管上限。設備投資で増える（要件 5.4）。</summary>
        public int FridgeCapacity(BalanceData balance)
        {
            var caps = balance.Facilities.FridgeCapacity;
            int level = Facilities[FacilityKind.Fridge];
            if (level < 0) level = 0;
            if (level >= caps.Count) level = caps.Count - 1;
            return caps[level];
        }

        /// <summary>客のドリンクの単価。エスプレッソマシンで上がる。</summary>
        public long GuestDrinkBonusYen(BalanceData balance)
        {
            var bonus = balance.Facilities.EspressoDrinkBonusYen;
            int level = Facilities[FacilityKind.Espresso];
            if (level < 0) level = 0;
            if (level >= bonus.Count) level = bonus.Count - 1;
            return bonus[level];
        }

        public static Shop NewGame(BalanceData balance)
        {
            var shop = new Shop
            {
                CashYen = balance.Shop.InitialCashYen,
                CastSlots = balance.Shop.InitialCastSlots,
                ManagerAp = balance.Manager.ApPerWeek,
            };
            return shop;
        }

        /// <summary>
        /// 家賃・光熱費・通信費。枠を広げると上がる。
        /// 広げること自体がリスクになり、「広げたが埋められない」という
        /// 失敗が成立する（設計書 14.1）。
        /// </summary>
        public long WeeklyRentYen(BalanceData balance)
        {
            int expansions = (CastSlots - balance.Shop.InitialCastSlots)
                             / balance.Shop.SlotsPerExpansion;
            if (expansions < 0) expansions = 0;
            return balance.Cost.WeeklyRentYen
                 + expansions * balance.Cost.RentPerExpansionYen;
        }

        /// <summary>
        /// 決済手数料・消耗品費・広告宣伝費・雑費。売上に比例する。
        /// これがあることで、規模が大きくなっても利益率が保たれる。
        /// </summary>
        public static long VariableCostYen(long revenueYen, BalanceData balance) =>
            (long)System.Math.Round(revenueYen * balance.Cost.VariableCostRate,
                                    System.MidpointRounding.AwayFromZero);

        public Cast FindCast(string id)
        {
            foreach (var c in Roster)
                if (c.Id == id) return c;
            return null;
        }
    }
}
