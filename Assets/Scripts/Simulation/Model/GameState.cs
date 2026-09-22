using Concafe.Simulation.Balance;
using Concafe.Simulation.Rng;

namespace Concafe.Simulation.Model
{
    /// <summary>
    /// 進行中のゲーム全体。セーブの対象はここに集まる。
    /// </summary>
    public sealed class GameState
    {
        public BalanceData Balance { get; }
        public EasterEggSet Eggs { get; }
        public GameCalendar Calendar { get; }
        public RandomStreams Rng { get; }
        public Shop Shop { get; }

        public GameWeek Week { get; set; }

        /// <summary>今週 7 日分の実際の天候。週頭に確定する。</summary>
        public Weather[] Weather { get; internal set; } = new Weather[Shift.DaysPerWeek];

        /// <summary>
        /// 週頭にプレイヤーへ見せる予報。実際と食い違うことがある（設計書 5.2）。
        /// シミュレーションはこちらを読まない。
        /// </summary>
        public Weather[] Forecast { get; internal set; } = new Weather[Shift.DaysPerWeek];

        public GameOverReason GameOver { get; internal set; } = GameOverReason.None;
        public bool IsOver => GameOver != GameOverReason.None;

        /// <summary>キャスト ID の通し番号。</summary>
        public int NextCastSerial { get; internal set; }

        /// <summary>
        /// 今週の発注額。決算レポートに「仕入れ費」として載せるために積む。
        /// 現金は発注の時点ですでに引かれている。
        /// </summary>
        public long WeekPurchaseYen { get; internal set; }

        public GameState(BalanceData balance, EasterEggSet eggs, ulong seed)
        {
            Balance = balance;
            Eggs = eggs ?? EasterEggSet.Empty;
            Calendar = new GameCalendar(balance.Time);
            Rng = RandomStreams.FromSeed(seed);
            Shop = Shop.NewGame(balance);
            Week = new GameWeek(0);
            NextCastSerial = 1;
        }

        public int TakeCastSerial() => NextCastSerial++;

        /// <summary>10 年目の最終週を終えたか。</summary>
        public bool ReachedEnding => Calendar.IsPastFinalWeek(Week);
    }
}
