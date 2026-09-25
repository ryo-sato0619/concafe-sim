using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Rules
{
    /// <summary>シフトを組んだ結果の見積り。1 人 1 週ぶん。</summary>
    public struct ShiftEstimate
    {
        public int WorkDays;
        public int WorkHours;
        /// <summary>深夜割増（22 時以降）に掛かった時間。</summary>
        public int LateNightHours;

        /// <summary>給料の見積り。バックは売れてみないと分からないので含まない。</summary>
        public long BaseWageYen;

        /// <summary>来客の係数の週合計。平日フル出勤 1 日が 1.0。</summary>
        public double VisitorFactor;

        /// <summary>疲労の見積り。接客の中身は実際に回すまで分からないので目安。</summary>
        public double FatigueGain;
        public double FatigueAfter;
        public double FatigueLimit;

        /// <summary>この組み方だと限界を超える見込み。</summary>
        public bool WillExceedLimit => FatigueAfter > FatigueLimit;

        /// <summary>限界に対する割合。疲労バーの表示に使う。</summary>
        public double FatigueRatio => FatigueLimit <= 0 ? 0 : FatigueAfter / FatigueLimit;
    }

    /// <summary>シフト表に出す警告。</summary>
    public enum ShiftIssueKind
    {
        /// <summary>営業時間の外、または最短シフト未満。</summary>
        InvalidSlot,
        /// <summary>この組み方だと疲労限界を超える見込み。</summary>
        WillBurnOut,
        /// <summary>すでに限界の warnRatio を超えている。</summary>
        AlreadyTired,
        /// <summary>誰も出勤しない日。臨時休業になる。</summary>
        ClosedDay,
        /// <summary>1 人も出勤しない週。</summary>
        NobodyWorks,
    }

    public struct ShiftIssue
    {
        public ShiftIssueKind Kind;
        /// <summary>該当するキャスト。日単位の指摘では null。</summary>
        public string CastId;
        /// <summary>該当する曜日。週単位の指摘では -1。</summary>
        public int Day;
        public string Message;
    }

    /// <summary>
    /// 週頭のシフト編集を支える計算（段階 7）。
    ///
    /// **UI に計算を持たせない。** 設計書 8 は「1 画面でシフト判断が完結する」ことを
    /// 求めており、そのためには給料・疲労・来客の見積りを画面に出す必要がある。
    /// それを MonoBehaviour 側に書くと、シフトの良し悪しを EditMode テストで
    /// 確かめられなくなる。ここは純 C# なのでテストから直接叩ける。
    ///
    /// 見積りは**目安である**。疲労は接客した延べ時間に比例し（不変条件 26）、
    /// 何人来て何時間居座るかは実際に回すまで決まらない。ここでは期待値で出す。
    /// </summary>
    public static class ShiftPlanner
    {
        // ---- 編集 -----------------------------------------------------------

        /// <summary>
        /// 1 枠を設定する。営業時間の外や最短シフト未満は弾く。
        /// UI から呼ぶ唯一の入口にして、不正なシフトが組まれない状態を保つ。
        /// </summary>
        public static bool TrySet(
            Shift shift, string castId, int day, int startHour, int endHour,
            BalanceData balance, out string reason)
        {
            var slot = new ShiftSlot(startHour, endHour);
            if (!Shift.IsValid(slot, day, balance, out reason)) return false;

            shift.Set(castId, day, slot);
            return true;
        }

        public static void Clear(Shift shift, string castId, int day) =>
            shift.Clear(castId, day);

        /// <summary>開店から閉店まで。</summary>
        public static ShiftSlot FullDay(int day, BalanceData balance) =>
            new ShiftSlot(balance.Business.OpenHour(day), balance.Business.CloseHour);

        /// <summary>
        /// 閉店前に寄せた <paramref name="hours"/> 時間。混む時間帯（U-20）を取る。
        /// 売上は伸びるが、深夜割増と疲労も付いてくる。
        /// </summary>
        public static ShiftSlot LateShift(int day, int hours, BalanceData balance)
        {
            var b = balance.Business;
            int start = b.CloseHour - hours;
            if (start < b.OpenHour(day)) start = b.OpenHour(day);
            return new ShiftSlot(start, b.CloseHour);
        }

        /// <summary>
        /// 開店から <paramref name="hours"/> 時間。空いている時間帯なので
        /// 売上は落ちるが、疲れたキャストを働かせ続けられる。
        /// </summary>
        public static ShiftSlot EarlyShift(int day, int hours, BalanceData balance)
        {
            var b = balance.Business;
            int end = b.OpenHour(day) + hours;
            if (end > b.CloseHour) end = b.CloseHour;
            return new ShiftSlot(b.OpenHour(day), end);
        }

        // ---- 見積り ---------------------------------------------------------

        /// <summary>
        /// 1 人 1 週ぶんの見積り。
        ///
        /// 疲労は「基礎 ＋ 接客の延べ時間」までを見る。酒由来の疲労は
        /// キャストドリンクとボトルが何本出るか次第なので含めない。
        /// **実際はこれより重く出る**ことを UI 側で断ること。
        /// </summary>
        public static ShiftEstimate Estimate(
            Cast cast, Shift shift, BalanceData balance, int currentWeek = 0,
            AlumniEffects alumni = null)
        {
            var e = new ShiftEstimate
            {
                FatigueLimit = cast.FatigueLimit(balance),
            };

            var b = balance.Business;
            double expectedStay = OrderRules.ExpectedStayHours(cast, balance);
            double guestHours = 0;

            for (int day = 0; day < Shift.DaysPerWeek; day++)
            {
                var slot = shift.Get(cast.Id, day);
                if (!slot.IsWorking) continue;

                e.WorkDays++;
                e.WorkHours += slot.Hours;
                e.LateNightHours += b.LateNightHoursIn(slot.StartHour, slot.EndHour);

                double factor = b.VisitorHoursFactor(slot.StartHour, slot.EndHour, day);
                e.VisitorFactor += factor;

                // 期待来客数。晴れの日を前提に置く。天候は予報でしか分からない。
                double guests = cast.Current.Regulars * balance.Visitors.RegularBase * factor
                              + cast.Current.Fame * balance.Visitors.FameBase * factor
                                * VisitorRules.LooksMultiplier(cast, balance, alumni);

                // 客はシフトより長くは居られない。
                double stay = expectedStay;
                if (stay > slot.Hours) stay = slot.Hours;

                guestHours += guests * stay;
            }

            e.BaseWageYen = WageRules.WeeklyBaseYen(cast, shift, balance, currentWeek);

            if (e.WorkDays > 0)
            {
                var f = balance.Fatigue;
                double raw = f.Base * e.WorkDays + guestHours * f.PerGuest;
                e.FatigueGain = raw * cast.TrustRelief(balance);
            }

            // 出勤しない日は回復する。
            double recovery = (Shift.DaysPerWeek - e.WorkDays) * FatigueRules.DailyRecovery(balance);

            e.FatigueAfter = cast.Fatigue + e.FatigueGain - recovery;
            if (e.FatigueAfter < 0) e.FatigueAfter = 0;

            return e;
        }

        // ---- 点検 -----------------------------------------------------------

        /// <summary>
        /// シフト表に出す警告を集める。
        ///
        /// **ここで集めた指摘がそのまま画面に出る。** 週を開始する前に
        /// 気づけるものだけを出し、回してみないと分からないことは出さない。
        /// </summary>
        public static List<ShiftIssue> Inspect(
            IReadOnlyList<Cast> roster, Shift shift, BalanceData balance,
            int currentWeek = 0, AlumniEffects alumni = null)
        {
            var issues = new List<ShiftIssue>();
            if (roster == null || roster.Count == 0) return issues;

            int workingAnywhere = 0;

            foreach (var cast in roster)
            {
                for (int day = 0; day < Shift.DaysPerWeek; day++)
                {
                    var slot = shift.Get(cast.Id, day);
                    if (!slot.IsWorking) continue;

                    if (!Shift.IsValid(slot, day, balance, out string reason))
                        issues.Add(new ShiftIssue
                        {
                            Kind = ShiftIssueKind.InvalidSlot,
                            CastId = cast.Id, Day = day,
                            Message = $"{cast.StageName} {DayName(day)}: {reason}",
                        });
                }

                var e = Estimate(cast, shift, balance, currentWeek, alumni);
                if (e.WorkDays > 0) workingAnywhere++;

                if (e.WillExceedLimit)
                    issues.Add(new ShiftIssue
                    {
                        Kind = ShiftIssueKind.WillBurnOut,
                        CastId = cast.Id, Day = -1,
                        Message = $"{cast.StageName} はこの組み方だと限界を超える見込み",
                    });
                else if (FatigueRules.ShouldWarn(cast, balance))
                    issues.Add(new ShiftIssue
                    {
                        Kind = ShiftIssueKind.AlreadyTired,
                        CastId = cast.Id, Day = -1,
                        Message = $"{cast.StageName} は疲れている。悩み相談か、休ませるか",
                    });
            }

            for (int day = 0; day < Shift.DaysPerWeek; day++)
            {
                bool anyone = false;
                foreach (var cast in roster)
                    if (shift.Get(cast.Id, day).IsWorking) { anyone = true; break; }

                if (!anyone)
                    issues.Add(new ShiftIssue
                    {
                        Kind = ShiftIssueKind.ClosedDay,
                        CastId = null, Day = day,
                        Message = $"{DayName(day)} は誰も出勤しない。臨時休業になる",
                    });
            }

            if (workingAnywhere == 0)
                issues.Add(new ShiftIssue
                {
                    Kind = ShiftIssueKind.NobodyWorks,
                    CastId = null, Day = -1,
                    Message = "今週は 1 人も出勤しない。売上が立たないまま経費だけが出る",
                });

            return issues;
        }

        static readonly string[] DayNames = { "月", "火", "水", "木", "金", "土", "日" };

        public static string DayName(int day) =>
            day >= 0 && day < DayNames.Length ? DayNames[day] : "?";
    }
}
