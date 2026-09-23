using System.Collections.Generic;
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
        public CareerSet Careers { get; }
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

        /// <summary>
        /// 出版関係者 OB から届いた特訓本の在庫。
        /// AP を消費せずに特訓できる（要件 6.1）。週を跨いで持ち越す。
        /// </summary>
        public int TrainingBooks { get; internal set; }

        /// <summary>今週の OB 差し入れ。週頭に決まり、決算のレポートに載る。</summary>
        public AlumniGiftRecord PendingGifts { get; internal set; } = new AlumniGiftRecord();

        /// <summary>
        /// 今週、週頭に起きた専用イベント。シフトを組む前に参照できる。
        /// </summary>
        public List<EggEventRecord> PendingEggEvents { get; internal set; }
            = new List<EggEventRecord>();

        /// <summary>
        /// 新規開始。前任者から引き継ぐキャストもここで用意する。
        /// 引き継ぎのキャストを作るのは呼び出し側（Rules 層）なので、
        /// ここではロスターを空のまま返す。<see cref="Rules.GameFlow.NewGame"/> を使うこと。
        /// </summary>
        public GameState(BalanceData balance, EasterEggSet eggs, CareerSet careers, ulong seed)
        {
            Balance = balance;
            Eggs = eggs ?? EasterEggSet.Empty;
            Careers = careers;
            Calendar = new GameCalendar(balance.Time);
            Rng = RandomStreams.FromSeed(seed);
            Shop = Shop.NewGame(balance);
            Week = new GameWeek(0);
            NextCastSerial = 1;
            RefreshAlumniEffects();
        }

        /// <summary>
        /// OB 効果。卒業のたびに作り直す。
        /// 毎回集計し直すと 480 週 × キャスト数ぶんの無駄になるため、
        /// 変わったときだけ更新する。
        /// </summary>
        public AlumniEffects Alumni { get; private set; } = AlumniEffects.None;

        public void RefreshAlumniEffects()
        {
            Alumni = Careers == null
                ? AlumniEffects.None
                : AlumniEffects.From(Shop.Alumni, Careers);
        }

        public int TakeCastSerial() => NextCastSerial++;

        /// <summary>10 年目の最終週を終えたか。</summary>
        public bool ReachedEnding => Calendar.IsPastFinalWeek(Week);
    }
}
