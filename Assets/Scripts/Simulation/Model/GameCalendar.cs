using Concafe.Simulation.Balance;

namespace Concafe.Simulation.Model
{
    /// <summary>1 ターン = 1 週間。0 起点の通算週。</summary>
    public readonly struct GameWeek
    {
        public int AbsoluteWeek { get; }

        public GameWeek(int absoluteWeek) { AbsoluteWeek = absoluteWeek; }

        public GameWeek Next => new GameWeek(AbsoluteWeek + 1);

        public static GameWeek operator +(GameWeek w, int weeks) =>
            new GameWeek(w.AbsoluteWeek + weeks);

        public static int operator -(GameWeek a, GameWeek b) =>
            a.AbsoluteWeek - b.AbsoluteWeek;

        public override string ToString() => $"第{AbsoluteWeek}週";
    }

    /// <summary>
    /// 週から年・月を導く。週数はすべて balance.json 由来で、ここに定数を持たない
    /// （不変条件 10）。
    /// </summary>
    public sealed class GameCalendar
    {
        readonly TimeBalance time;

        public GameCalendar(TimeBalance time) { this.time = time; }

        public int WeeksPerYear => time.WeeksPerYear;
        public int WeeksPerMonth => time.WeeksPerMonth;
        public int MonthsPerYear => time.MonthsPerYear;
        public int TotalWeeks => time.TotalWeeks;

        /// <summary>0 起点。ゲーム開始年が 0。</summary>
        public int YearOf(GameWeek w) => w.AbsoluteWeek / WeeksPerYear;

        /// <summary>0 起点。0 が 1 月。</summary>
        public int MonthOf(GameWeek w) => (w.AbsoluteWeek % WeeksPerYear) / WeeksPerMonth;

        /// <summary>0 〜 WeeksPerYear-1。誕生週の判定に使う。</summary>
        public int WeekOfYearOf(GameWeek w) => w.AbsoluteWeek % WeeksPerYear;

        /// <summary>10 年目の最終週を過ぎたか。</summary>
        public bool IsPastFinalWeek(GameWeek w) => w.AbsoluteWeek >= TotalWeeks;

        /// <summary>
        /// この週に誕生日を迎えるか。
        ///
        /// 誕生週を個体ごとにばらすのは意図的である（設計書 2）。全員が同時に
        /// 加齢すると 20 歳の覚醒が一斉に起き、「あと数週で覚醒する子を今
        /// 飛ばすわけにいかない」という判断が消える。
        ///
        /// 開始週（AbsoluteWeek 0）では加齢しない。採用時の年齢がそのまま
        /// 1 週目の年齢になる。
        /// </summary>
        public bool IsBirthdayWeek(GameWeek w, int birthWeekOfYear) =>
            w.AbsoluteWeek > 0 && WeekOfYearOf(w) == birthWeekOfYear;
    }
}
