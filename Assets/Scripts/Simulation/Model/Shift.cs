using System;
using System.Collections.Generic;

namespace Concafe.Simulation.Model
{
    /// <summary>
    /// 1 週間のシフト表。縦にキャスト、横に 7 日。
    /// 出勤していない日は休業日として疲労が回復する。
    /// </summary>
    public sealed class Shift
    {
        public const int DaysPerWeek = 7;

        readonly Dictionary<string, bool[]> table = new Dictionary<string, bool[]>(StringComparer.Ordinal);

        public bool IsWorking(string castId, int day)
        {
            CheckDay(day);
            return table.TryGetValue(castId, out var days) && days[day];
        }

        public void Set(string castId, int day, bool working)
        {
            CheckDay(day);
            if (string.IsNullOrEmpty(castId)) throw new ArgumentException("castId が空", nameof(castId));

            if (!table.TryGetValue(castId, out var days))
            {
                days = new bool[DaysPerWeek];
                table[castId] = days;
            }
            days[day] = working;
        }

        public int WorkDays(string castId)
        {
            if (!table.TryGetValue(castId, out var days)) return 0;
            int n = 0;
            foreach (bool b in days) if (b) n++;
            return n;
        }

        /// <summary>週あたりの出勤日数を全員分まとめて設定する。月曜から詰める。</summary>
        public static Shift Uniform(IEnumerable<Cast> roster, int daysPerCast)
        {
            if (daysPerCast < 0 || daysPerCast > DaysPerWeek)
                throw new ArgumentOutOfRangeException(nameof(daysPerCast));

            var shift = new Shift();
            foreach (var c in roster)
                for (int d = 0; d < daysPerCast; d++)
                    shift.Set(c.Id, d, true);
            return shift;
        }

        /// <summary>臨時休業。その日は誰も出勤しない。</summary>
        public void CloseDay(int day)
        {
            CheckDay(day);
            foreach (var days in table.Values) days[day] = false;
        }

        static void CheckDay(int day)
        {
            if (day < 0 || day >= DaysPerWeek)
                throw new ArgumentOutOfRangeException(nameof(day), $"曜日は 0〜{DaysPerWeek - 1}");
        }
    }
}
