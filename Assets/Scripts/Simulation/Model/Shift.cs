using System;
using System.Collections.Generic;
using Concafe.Simulation.Balance;

namespace Concafe.Simulation.Model
{
    /// <summary>
    /// 1 人 1 日のシフト。時刻で持つ（17 時〜21 時など）。
    /// 空の場合は休み。
    /// </summary>
    public readonly struct ShiftSlot
    {
        public int StartHour { get; }
        public int EndHour { get; }

        public ShiftSlot(int startHour, int endHour)
        {
            StartHour = startHour;
            EndHour = endHour;
        }

        public static readonly ShiftSlot Off = new ShiftSlot(0, 0);

        public int Hours => EndHour > StartHour ? EndHour - StartHour : 0;
        public bool IsWorking => Hours > 0;

        public override string ToString() => IsWorking ? $"{StartHour}:00-{EndHour}:00" : "休";
    }

    /// <summary>
    /// 1 週間のシフト表。縦にキャスト、横に 7 日。
    /// 各枠は「何時から何時まで」を持つ。
    ///
    /// 短く入れれば疲労と給料が軽く、長く入れれば売上が伸びる。
    /// 深夜帯（22 時以降）に掛けるかどうかも判断になる。
    /// 出勤していない日は休業日として疲労が回復する。
    /// </summary>
    public sealed class Shift
    {
        public const int DaysPerWeek = 7;

        readonly Dictionary<string, ShiftSlot[]> table =
            new Dictionary<string, ShiftSlot[]>(StringComparer.Ordinal);

        public ShiftSlot Get(string castId, int day)
        {
            CheckDay(day);
            return table.TryGetValue(castId, out var days) ? days[day] : ShiftSlot.Off;
        }

        public bool IsWorking(string castId, int day) => Get(castId, day).IsWorking;

        public void Set(string castId, int day, ShiftSlot slot)
        {
            CheckDay(day);
            if (string.IsNullOrEmpty(castId)) throw new ArgumentException("castId が空", nameof(castId));

            if (!table.TryGetValue(castId, out var days))
            {
                days = new ShiftSlot[DaysPerWeek];
                for (int i = 0; i < days.Length; i++) days[i] = ShiftSlot.Off;
                table[castId] = days;
            }
            days[day] = slot;
        }

        public void Clear(string castId, int day) => Set(castId, day, ShiftSlot.Off);

        public int WorkDays(string castId)
        {
            if (!table.TryGetValue(castId, out var days)) return 0;
            int n = 0;
            foreach (var s in days) if (s.IsWorking) n++;
            return n;
        }

        public int WorkHours(string castId)
        {
            if (!table.TryGetValue(castId, out var days)) return 0;
            int h = 0;
            foreach (var s in days) h += s.Hours;
            return h;
        }

        /// <summary>臨時休業。その日は誰も出勤しない。</summary>
        public void CloseDay(int day)
        {
            CheckDay(day);
            foreach (var days in table.Values) days[day] = ShiftSlot.Off;
        }

        /// <summary>
        /// シフトが営業時間の内側に収まっているかを検査する。
        /// UI で組む前にここで弾く。
        /// </summary>
        public static bool IsValid(ShiftSlot slot, int day, BalanceData balance, out string reason)
        {
            var b = balance.Business;

            if (!slot.IsWorking) { reason = null; return true; }

            if (slot.StartHour < b.OpenHour(day))
            {
                reason = $"開店（{b.OpenHour(day)} 時）より早い";
                return false;
            }
            if (slot.EndHour > b.CloseHour)
            {
                reason = $"閉店（{b.CloseHour} 時）より遅い";
                return false;
            }
            if (slot.Hours < b.MinShiftHours)
            {
                reason = $"最短シフト（{b.MinShiftHours} 時間）に足りない";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 全員を「開店から閉店まで」で daysPerCast 日ぶん入れる。月曜から詰める。
        /// テストと素朴な自動プレイ用。
        /// </summary>
        public static Shift FullDays(
            IEnumerable<Cast> roster, int daysPerCast, BalanceData balance)
        {
            if (daysPerCast < 0 || daysPerCast > DaysPerWeek)
                throw new ArgumentOutOfRangeException(nameof(daysPerCast));

            var b = balance.Business;
            var shift = new Shift();

            foreach (var c in roster)
                for (int d = 0; d < daysPerCast; d++)
                    shift.Set(c.Id, d, new ShiftSlot(b.OpenHour(d), b.CloseHour));

            return shift;
        }

        static void CheckDay(int day)
        {
            if (day < 0 || day >= DaysPerWeek)
                throw new ArgumentOutOfRangeException(nameof(day), $"曜日は 0〜{DaysPerWeek - 1}");
        }
    }
}
