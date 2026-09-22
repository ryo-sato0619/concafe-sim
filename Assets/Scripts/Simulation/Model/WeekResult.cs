using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    /// <summary>1 日の営業結果。</summary>
    public struct DayResult
    {
        public Weather Weather;
        public int Visitors;
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
        public int Guests;
        public long SalesYen;
        public long WageYen;
        public double FatigueGained;
        public int BottlesSold;
        public int AlcoholDrinksServed;
    }

    public enum DepartureTrigger
    {
        /// <summary>疲労が限界を超えた。</summary>
        Burnout,
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
        public long FixedCostYen;
        /// <summary>この週に発注で支払った額。決算の前に現金から引かれている。</summary>
        public long PurchaseYen;

        /// <summary>売上 −（給料 ＋ 仕入れ費 ＋ 固定費）。</summary>
        public long ProfitYen => RevenueYen - (WageYen + FixedCostYen + PurchaseYen);

        public long CashBeforeYen;
        public long CashAfterYen;

        public int TotalVisitors;
        public int LostOrders;
        public long LostSalesYen;

        public List<CastWeekStats> CastStats = new List<CastWeekStats>();
        public List<DepartureRecord> Departures = new List<DepartureRecord>();
        public List<DepartureWarning> Warnings = new List<DepartureWarning>();

        public GameOverReason GameOver = GameOverReason.None;
        public bool IsGameOver => GameOver != GameOverReason.None;
    }
}
