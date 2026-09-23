using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    /// <summary>1 日の営業結果。</summary>
    public struct DayResult
    {
        public Weather Weather;
        /// <summary>その日の営業時間。平日 6 時間、休日 8 時間。</summary>
        public int OpenHours;
        public bool IsHoliday;
        public int Visitors;
        public int GuestHours;
        public long RevenueYen;
        /// <summary>在庫切れで失った注文の数。</summary>
        public int LostOrders;
        /// <summary>在庫切れで失った売上。</summary>
        public long LostSalesYen;
        public bool Closed;
    }

    /// <summary>キャスト 1 人の週間集計。</summary>
    public sealed class CastWeekStats
    {
        public string CastId;
        public string StageName;
        public int WorkDays;
        /// <summary>出勤した延べ時間。</summary>
        public int WorkHours;
        /// <summary>専用イベントで欠勤した日数。</summary>
        public int AbsentDays;
        public int Guests;
        /// <summary>接客した延べ時間。疲労の源。</summary>
        public int GuestHours;
        public long SalesYen;
        /// <summary>バックの対象になった売上（チェキ・キャストドリンク・ボトル）。</summary>
        public long BackableSalesYen;
        public long WageYen;
        /// <summary>そのうちバックの額。</summary>
        public long BackYen;
        public long HourlyYen;
        public double FatigueGained;
        public int BottlesSold;
        public int AlcoholDrinksServed;
    }

    public enum DepartureTrigger
    {
        /// <summary>疲労が限界を超えた。</summary>
        Burnout,
        /// <summary>炎上がそのまま退店につながった。</summary>
        Scandal,
    }

    public sealed class ScandalRecord
    {
        public string CastId;
        public string StageName;
        public double FameLost;
        public double RegularsLost;
        public double MoraleLost;
        /// <summary>そのまま店を去ったか。</summary>
        public bool Departed;
    }

    public sealed class GraduationRecord
    {
        public string CastId;
        public string StageName;
        public CareerKind Career;
        public string CareerName;
        public long LifetimeSalesYen;
        /// <summary>失われた固定客。卒業でも固定客は残らない。</summary>
        public double LostRegulars;
    }

    /// <summary>週頭に起きた専用イベント。</summary>
    public sealed class EggEventRecord
    {
        public string CastId;
        public string StageName;
        public string EventId;
        public string Title;
        public IReadOnlyList<string> Body;
        /// <summary>欠勤日数（該当する場合）。</summary>
        public int AbsentDays;
        /// <summary>店舗全体のモラル低下（該当する場合）。</summary>
        public double ShopMoraleLost;
    }

    /// <summary>OB の差し入れやイベントで、その週に起きたこと。</summary>
    public sealed class AlumniGiftRecord
    {
        public int TrainingBooks;
        public int PremiumBottles;
        public int LimitedGoods;
        public bool OneDayManager;
        public long CongratulatoryYen;
    }

    public sealed class DepartureRecord
    {
        public string CastId;
        public string StageName;
        public DepartureKind Kind;
        public DepartureOverride Override;
        public DepartureTrigger Trigger;
        public string EventId;
        /// <summary>失われた固定客（そのキャストの集客）。</summary>
        public double LostRegulars;
        /// <summary>飛ばれたことで他のキャスト全員から削られた集客の合計。</summary>
        public double AntiSpreadTotal;
    }

    /// <summary>離脱の予兆。転生しうるキャストにのみ出る（不変条件 20）。</summary>
    public sealed class DepartureWarning
    {
        public string CastId;
        public string StageName;
        public double Fatigue;
        public double Limit;
    }

    public sealed class WeekResult
    {
        public GameWeek Week;
        public DayResult[] Days = new DayResult[Shift.DaysPerWeek];

        public long RevenueYen;
        public long WageYen;

        /// <summary>家賃・光熱費・通信費。</summary>
        public long RentYen;
        /// <summary>店長自身の報酬。</summary>
        public long ManagerPayYen;
        /// <summary>決済手数料・消耗品費・広告宣伝費・雑費。売上に比例する。</summary>
        public long VariableCostYen;

        /// <summary>この週に発注で支払った額。決算の前に現金から引かれている。</summary>
        public long PurchaseYen;

        /// <summary>給料以外の経費の合計。</summary>
        public long OverheadYen => RentYen + ManagerPayYen + VariableCostYen;

        /// <summary>売上 ＋ ご祝儀 −（給料 ＋ 仕入れ費 ＋ 諸経費）。</summary>
        public long ProfitYen =>
            RevenueYen + CongratulatoryYen - (WageYen + PurchaseYen + OverheadYen);

        public long CashBeforeYen;
        public long CashAfterYen;

        public int TotalVisitors;
        public int LostOrders;
        public long LostSalesYen;

        /// <summary>1 日店長イベントで上乗せされた売上。</summary>
        public long OneDayManagerBonusYen;
        /// <summary>ご祝儀。決算に入る。</summary>
        public long CongratulatoryYen;

        public List<CastWeekStats> CastStats = new List<CastWeekStats>();
        public List<DepartureRecord> Departures = new List<DepartureRecord>();
        public List<DepartureWarning> Warnings = new List<DepartureWarning>();
        public List<ScandalRecord> Scandals = new List<ScandalRecord>();
        public List<GraduationRecord> Graduations = new List<GraduationRecord>();
        public AlumniGiftRecord Gifts = new AlumniGiftRecord();
        /// <summary>週頭に起きた専用イベント。</summary>
        public List<EggEventRecord> EggEvents = new List<EggEventRecord>();

        public GameOverReason GameOver = GameOverReason.None;
        public bool IsGameOver => GameOver != GameOverReason.None;
    }
}
