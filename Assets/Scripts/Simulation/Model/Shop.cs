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

    /// <summary>資金を使った設備投資。店長のレベルアップは持たない（不変条件 6）。</summary>
    public sealed class Facility
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public int Level { get; set; }
    }

    public sealed class Shop
    {
        /// <summary>不変条件 8: 金額は整数の円。float / double を使わない。</summary>
        public long CashYen { get; set; }

        public int CastSlots { get; set; }

        public List<Cast> Roster { get; } = new List<Cast>();
        public List<Applicant> Applicants { get; } = new List<Applicant>();
        public List<Alumnus> Alumni { get; } = new List<Alumnus>();
        public List<Facility> Facilities { get; } = new List<Facility>();

        public Inventory Stock { get; } = new Inventory();

        public int ManagerAp { get; set; }

        public bool HasFreeSlot => Roster.Count < CastSlots;

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
        /// 枠を広げると週次固定費も上がる。広げること自体がリスクになり、
        /// 「広げたが埋められない」という失敗が成立する（設計書 14.1）。
        /// </summary>
        public long WeeklyFixedCostYen(BalanceData balance)
        {
            int expansions = (CastSlots - balance.Shop.InitialCastSlots)
                             / balance.Shop.SlotsPerExpansion;
            if (expansions < 0) expansions = 0;
            return balance.Shop.WeeklyFixedCostYen
                 + expansions * balance.Shop.FixedCostPerExpansionYen;
        }

        public Cast FindCast(string id)
        {
            foreach (var c in Roster)
                if (c.Id == id) return c;
            return null;
        }
    }
}
