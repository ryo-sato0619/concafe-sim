using System;
using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Concafe.Game
{
    /// <summary>
    /// シフト表。縦にキャスト、横に 7 日。
    ///
    /// **設計書 8 の「1 画面でシフト判断が完結する」を担うのがここ。**
    /// 疲労バー・年齢・未成年フラグ・給料と来客の見積りを同じ行に並べる。
    /// 別画面を見ないと判断できない作りにすると、480 ターンが苦行になる。
    ///
    /// 計算は持たない。<see cref="ShiftPlanner"/> が出した値を描くだけである。
    /// </summary>
    public sealed class ShiftGridView
    {
        const float NameWidth = 150f;
        const float DayWidth = 76f;
        const float SummaryWidth = 190f;
        const float RowHeight = 46f;

        readonly RectTransform content;
        readonly BalanceData balance;

        /// <summary>枠を押したときに呼ぶ。キャスト ID と曜日を渡す。</summary>
        public Action<string, int> OnSlotClicked;

        public ShiftGridView(RectTransform content, BalanceData balance)
        {
            this.content = content;
            this.balance = balance;
        }

        public void Rebuild(
            IReadOnlyList<Cast> roster, Shift shift, int currentWeek, AlumniEffects alumni)
        {
            Ui.ClearChildren(content);

            BuildHeader();

            if (roster.Count == 0)
            {
                var empty = Ui.Label("Empty", content, "キャストがいない。求人から採用する", 20);
                empty.color = UiTheme.TextDim;
                Ui.Size(empty.gameObject, height: RowHeight);
                return;
            }

            foreach (var cast in roster)
                BuildRow(cast, shift, currentWeek, alumni);
        }

        void BuildHeader()
        {
            var row = Ui.Create("Header", content);
            Ui.Row(row, 2);
            Ui.Size(row, height: 30);

            var spacer = Ui.Label("Spacer", row.transform, "キャスト", 16);
            spacer.color = UiTheme.TextDim;
            Ui.Size(spacer.gameObject, NameWidth);

            for (int day = 0; day < Shift.DaysPerWeek; day++)
            {
                bool holiday = balance.Business.IsHoliday(day);
                string text = ShiftPlanner.DayName(day)
                            + (holiday ? $"\n{balance.Business.OpenHour(day)}時〜" : "");

                var label = Ui.Label("Day" + day, row.transform, text, 15,
                                     TextAlignmentOptions.Center);
                label.color = holiday ? UiTheme.Accent : UiTheme.TextDim;
                label.textWrappingMode = TextWrappingModes.Normal;
                Ui.Size(label.gameObject, DayWidth);
            }

            var summary = Ui.Label("SummaryHead", row.transform, "疲労 / 給料の見積り", 15,
                                   TextAlignmentOptions.Center);
            summary.color = UiTheme.TextDim;
            Ui.Size(summary.gameObject, SummaryWidth);
        }

        void BuildRow(Cast cast, Shift shift, int currentWeek, AlumniEffects alumni)
        {
            var estimate = ShiftPlanner.Estimate(cast, shift, balance, currentWeek, alumni);

            var row = Ui.Create("Row_" + cast.Id, content);
            Ui.Row(row, 2);
            Ui.Size(row, height: RowHeight);

            BuildNameCell(row.transform, cast);

            for (int day = 0; day < Shift.DaysPerWeek; day++)
                BuildSlotCell(row.transform, cast, shift.Get(cast.Id, day), day);

            BuildSummaryCell(row.transform, estimate);
        }

        void BuildNameCell(Transform parent, Cast cast)
        {
            var cell = Ui.Panel("Name", parent, UiTheme.PanelDark);
            Ui.Size(cell.gameObject, NameWidth);

            var column = Ui.Create("Lines", cell.transform);
            Ui.Stretch(column, 4);
            Ui.Column(column, 1);

            // 未成年は酒が売れず tierPower も伸びない（不変条件 3・4）。
            // シフトを組む時点で見えていないと、酒の在庫判断を誤る。
            string minor = cast.IsMinor ? "  <color=#FFB84D>未成年</color>" : "";
            var name = Ui.Label("StageName", column.transform,
                                cast.StageName + minor, 17);
            Ui.Size(name.gameObject, height: 20);

            var sub = Ui.Label("Sub", column.transform,
                               $"{cast.Age}歳  限界Tier{cast.StaminaTier}", 13);
            sub.color = UiTheme.TextDim;
            Ui.Size(sub.gameObject, height: 15);
        }

        void BuildSlotCell(Transform parent, Cast cast, ShiftSlot slot, int day)
        {
            bool holiday = balance.Business.IsHoliday(day);
            Color color = slot.IsWorking ? UiTheme.ButtonOn
                        : holiday ? UiTheme.Holiday
                        : UiTheme.Button;

            string castId = cast.Id;
            var button = Ui.Button("Slot" + day, parent,
                                   slot.IsWorking ? SlotText(slot) : "休",
                                   slot.IsWorking ? 14 : 16,
                                   () => OnSlotClicked?.Invoke(castId, day));

            button.targetGraphic.GetComponent<Image>().color = color;
            Ui.Size(button.gameObject, DayWidth);

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.textWrappingMode = TextWrappingModes.Normal;

            // 深夜帯に掛かっている枠は印を付ける。割増が乗り、
            // 同時に混む時間帯（U-20）でもある。
            if (slot.IsWorking
                && balance.Business.LateNightHoursIn(slot.StartHour, slot.EndHour) > 0)
                label.color = UiTheme.Warn;
        }

        static string SlotText(ShiftSlot slot) => $"{slot.StartHour}-{slot.EndHour}";

        void BuildSummaryCell(Transform parent, ShiftEstimate estimate)
        {
            var cell = Ui.Panel("Summary", parent, UiTheme.PanelDark);
            Ui.Size(cell.gameObject, SummaryWidth);

            var column = Ui.Create("Lines", cell.transform);
            Ui.Stretch(column, 5);
            Ui.Column(column, 2);

            var bar = Ui.Bar("Fatigue", column.transform, out Image fill);
            Ui.Size(bar.gameObject, height: 8);
            Ui.SetBar(fill, estimate.FatigueRatio);

            string warning = estimate.WillExceedLimit ? "  <color=#EB6B6B>限界超過</color>" : "";
            var text = Ui.Label("Numbers", column.transform,
                $"疲労 {estimate.FatigueAfter:0.0}/{estimate.FatigueLimit:0}{warning}\n" +
                $"{estimate.WorkDays}日 {estimate.WorkHours}h  給料 {estimate.BaseWageYen:N0}",
                13);
            text.textWrappingMode = TextWrappingModes.Normal;
            Ui.Size(text.gameObject, height: 30);
        }
    }
}
