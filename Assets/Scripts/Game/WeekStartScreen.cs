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
    /// 週頭の画面（段階 7）。設計書 8 の「週頭」。
    ///
    /// 天気予報・求人・採用・在庫発注・シフト表・AP 消費を 1 画面に載せる。
    /// **シフト表が中心**で、他はその周りに置く。
    ///
    /// 判断に要る計算は <see cref="ShiftPlanner"/> と
    /// <see cref="WeekBriefingBuilder"/> が持つ。ここは描くのと、
    /// 押されたボタンを <see cref="ManagerActions"/> に渡すだけ。
    /// </summary>
    public sealed class WeekStartScreen : MonoBehaviour
    {
        GameController controller;
        BalanceData balance;

        RectTransform shiftContent;
        RectTransform sideContent;
        RectTransform issueContent;
        ShiftGridView grid;

        TextMeshProUGUI headerLabel;
        TextMeshProUGUI forecastLabel;
        Button startButton;

        /// <summary>枠を押したときに割り当てる長さ。押すたびに切り替わる。</summary>
        static readonly int[] SlotCycleHours = { 0, -1, 4, 2 };  // -1 = フル出勤
        readonly Dictionary<string, int> cycleIndex = new Dictionary<string, int>();

        public void Init(GameController controller)
        {
            this.controller = controller;
            this.balance = controller.Data.Balance;

            BuildLayout();
        }

        // ---- 組み立て -------------------------------------------------------

        void BuildLayout()
        {
            var root = Ui.Stretch(gameObject);

            var column = Ui.Create("Column", transform);
            Ui.Stretch(column, 12);
            Ui.Column(column, 8);

            BuildHeader(column.transform);
            BuildMiddle(column.transform);
            BuildFooter(column.transform);
        }

        void BuildHeader(Transform parent)
        {
            var bar = Ui.Panel("Header", parent, UiTheme.Panel);
            Ui.Size(bar.gameObject, height: 62);

            var row = Ui.Create("Row", bar.transform);
            Ui.Stretch(row, 8);
            Ui.Row(row, 16);

            headerLabel = Ui.Label("Status", row.transform, "", 18);
            headerLabel.textWrappingMode = TextWrappingModes.Normal;
            Ui.Flexible(headerLabel.gameObject, 2);

            forecastLabel = Ui.Label("Forecast", row.transform, "", 17);
            forecastLabel.textWrappingMode = TextWrappingModes.Normal;
            Ui.Flexible(forecastLabel.gameObject, 3);
        }

        void BuildMiddle(Transform parent)
        {
            var middle = Ui.Create("Middle", parent);
            Ui.Row(middle, 10);
            Ui.Flexible(middle, 1);
            var element = middle.GetComponent<LayoutElement>();
            element.flexibleHeight = 1;

            // シフト表。画面の中心なので広く取る。
            var left = Ui.Panel("ShiftPanel", middle.transform, UiTheme.Panel);
            Ui.Flexible(left.gameObject, 3);

            var leftColumn = Ui.Create("Column", left.transform);
            Ui.Stretch(leftColumn, 8);
            Ui.Column(leftColumn, 6);

            var title = Ui.Label("Title", leftColumn.transform,
                "シフト表　枠を押すと 休 → フル → 4h(遅番) → 2h(遅番) と切り替わる", 16);
            title.color = UiTheme.TextDim;
            title.textWrappingMode = TextWrappingModes.Normal;
            Ui.Size(title.gameObject, height: 20);

            var scrollHost = Ui.Create("ScrollHost", leftColumn.transform);
            Ui.Flexible(scrollHost, 1);
            scrollHost.GetComponent<LayoutElement>().flexibleHeight = 1;
            shiftContent = Ui.ScrollView("Scroll", scrollHost.transform, out _);
            Ui.Stretch(shiftContent.parent.parent.gameObject);

            grid = new ShiftGridView(shiftContent, balance) { OnSlotClicked = CycleSlot };

            // 右は求人・発注・AP。
            var right = Ui.Panel("SidePanel", middle.transform, UiTheme.Panel);
            Ui.Flexible(right.gameObject, 2);

            var rightHost = Ui.Create("Host", right.transform);
            Ui.Stretch(rightHost, 8);
            sideContent = Ui.ScrollView("Scroll", rightHost.transform, out _);
            Ui.Stretch(sideContent.parent.parent.gameObject);
        }

        void BuildFooter(Transform parent)
        {
            var footer = Ui.Panel("Footer", parent, UiTheme.Panel);
            Ui.Size(footer.gameObject, height: 96);

            var row = Ui.Create("Row", footer.transform);
            Ui.Stretch(row, 8);
            Ui.Row(row, 10);

            var issueHost = Ui.Create("Issues", row.transform);
            Ui.Flexible(issueHost, 1);
            issueContent = Ui.ScrollView("Scroll", issueHost.transform, out _);
            Ui.Stretch(issueContent.parent.parent.gameObject);

            startButton = Ui.Button("Start", row.transform, "この週を始める", 20,
                                    () => controller.RunWeek());
            Ui.Size(startButton.gameObject, 210);
            startButton.targetGraphic.GetComponent<Image>().color = UiTheme.ButtonOn;
        }

        // ---- 描画 -----------------------------------------------------------

        public void Refresh()
        {
            var state = controller.State;
            var briefing = WeekBriefingBuilder.Build(state);

            headerLabel.text =
                $"<size=22>{briefing.Year}年目 {briefing.Month}月 {briefing.WeekOfMonth}週目</size>\n" +
                $"資金 <b>{briefing.CashYen:N0}</b> 円　AP <b>{briefing.ManagerAp}</b>　" +
                $"キャスト {briefing.CastCount}/{briefing.CastSlots}　" +
                $"固定費 {briefing.WeeklyRentYen + briefing.WeeklyManagerPayYen:N0} 円/週";

            forecastLabel.text = BuildForecastText(briefing);

            grid.Rebuild(state.Shop.Roster, controller.Shift,
                         state.Week.AbsoluteWeek, state.Alumni);

            BuildSide(briefing);
            BuildIssues();
        }

        string BuildForecastText(WeekBriefing briefing)
        {
            var sb = new System.Text.StringBuilder("週間予報　");

            for (int day = 0; day < Shift.DaysPerWeek; day++)
            {
                double accuracy = day < briefing.ForecastAccuracy.Count
                    ? briefing.ForecastAccuracy[day] : 0;

                // 先の日ほど外れる（設計書 5.2）。確度を添えて、
                // 予報を鵜呑みにしない材料を渡す。
                string mark = accuracy >= 0.95 ? "" : accuracy >= 0.75 ? "?" : "??";

                sb.Append(ShiftPlanner.DayName(day))
                  .Append(WeatherName(briefing.Forecast[day]))
                  .Append(mark)
                  .Append("  ");
            }

            return sb.ToString();
        }

        static string WeatherName(Weather w)
        {
            switch (w)
            {
                case Weather.Sunny: return "晴";
                case Weather.Cloudy: return "曇";
                case Weather.Rain: return "雨";
                case Weather.Storm: return "嵐";
                case Weather.Snow: return "雪";
                default: return "?";
            }
        }

        void BuildSide(WeekBriefing briefing)
        {
            Ui.ClearChildren(sideContent);

            BuildEggEvents(briefing);
            BuildApSection();
            BuildRecruitSection();
            BuildStockSection(briefing);
        }

        void SectionTitle(string text)
        {
            var label = Ui.Label("Section", sideContent, text, 17);
            label.color = UiTheme.Accent;
            Ui.Size(label.gameObject, height: 24);
        }

        void BuildEggEvents(WeekBriefing briefing)
        {
            if (briefing.EggEvents.Count == 0) return;

            SectionTitle("今週の出来事");
            foreach (var e in briefing.EggEvents)
            {
                string body = e.Body != null && e.Body.Count > 0 ? e.Body[0] : "";
                var label = Ui.Label("Event", sideContent,
                                     $"<color=#FFB84D>{e.StageName}</color> {e.Title}\n{body}", 14);
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                Ui.Size(label.gameObject, height: 42);
            }
        }

        void BuildApSection()
        {
            var state = controller.State;

            SectionTitle($"店長 AP  {state.Shop.ManagerAp} / {balance.Manager.ApPerWeek}");

            var note = Ui.Label("ApNote", sideContent,
                $"特訓 {balance.Manager.TrainCostAp}AP　悩み相談 {balance.Manager.CareCostAp}AP" +
                (state.TrainingBooks > 0 ? $"　特訓本 {state.TrainingBooks} 冊（AP 不要）" : ""),
                13);
            note.color = UiTheme.TextDim;
            Ui.Size(note.gameObject, height: 18);

            foreach (var cast in state.Shop.Roster)
            {
                var row = Ui.Create("Ap_" + cast.Id, sideContent);
                Ui.Row(row, 4);
                Ui.Size(row, height: 28);

                var name = Ui.Label("Name", row.transform, cast.StageName, 14);
                Ui.Size(name.gameObject, 90);

                string castId = cast.Id;

                var care = Ui.Button("Care", row.transform, "相談", 13,
                                     () => controller.Care(castId));
                Ui.Flexible(care.gameObject);

                foreach (var kind in new[] { StatKind.Talk, StatKind.Regulars, StatKind.Morale })
                {
                    var k = kind;
                    var train = Ui.Button("Train" + kind, row.transform, StatName(k), 13,
                                          () => controller.Train(castId, k));
                    Ui.Flexible(train.gameObject);
                }
            }
        }

        static string StatName(StatKind kind)
        {
            switch (kind)
            {
                case StatKind.Regulars: return "集客";
                case StatKind.Fame: return "知名";
                case StatKind.Talk: return "トーク";
                case StatKind.Alcohol: return "酒";
                case StatKind.Looks: return "容姿";
                case StatKind.Morale: return "モラル";
                default: return kind.ToString();
            }
        }

        void BuildRecruitSection()
        {
            var state = controller.State;

            SectionTitle("求人");

            var note = Ui.Label("RecruitNote", sideContent,
                "求人費をケチると上限の低いキャストしか来ない。家賃 1 週ぶんにも満たない差である", 13);
            note.color = UiTheme.TextDim;
            note.textWrappingMode = TextWrappingModes.Normal;
            Ui.Size(note.gameObject, height: 32);

            var row = Ui.Create("Tiers", sideContent);
            Ui.Row(row, 4);
            Ui.Size(row, height: 30);

            for (int t = 0; t < balance.Recruit.Count; t++)
            {
                var tier = balance.Recruit[t];
                int index = t;
                var button = Ui.Button("Tier" + t, row.transform,
                    $"ティア{t + 1}\n{tier.FeeYen:N0}円", 12,
                    () => controller.Recruit(index));
                button.interactable = state.Shop.CashYen >= tier.FeeYen;
                Ui.Flexible(button.gameObject);
            }

            foreach (var applicant in state.Shop.Applicants)
            {
                var appRow = Ui.Create("App_" + applicant.Id, sideContent);
                Ui.Row(appRow, 4);
                Ui.Size(appRow, height: 30);

                string caps = applicant.Interviewed
                    ? $"上限 集{applicant.Cap.Regulars:0} 名{applicant.Cap.Fame:0} 話{applicant.Cap.Talk:0}"
                    : "上限は面接するまで分からない";

                var label = Ui.Label("Info", appRow.transform,
                    $"{applicant.Age}歳 Tier{applicant.StaminaTier}　{caps}", 12);
                Ui.Flexible(label.gameObject, 2);

                var a = applicant;
                if (!applicant.Interviewed)
                {
                    var interview = Ui.Button("Interview", appRow.transform,
                        $"面接 {balance.Manager.InterviewCostAp}AP", 12,
                        () => controller.Interview(a));
                    Ui.Size(interview.gameObject, 76);
                }

                var hire = Ui.Button("Hire", appRow.transform, "採用", 12,
                                     () => controller.RequestHire(a));
                hire.interactable = state.Shop.HasFreeSlot;
                Ui.Size(hire.gameObject, 56);
            }
        }

        void BuildStockSection(WeekBriefing briefing)
        {
            SectionTitle($"発注　冷蔵庫 {briefing.ChilledCount}/{briefing.FridgeCapacity}");

            foreach (var line in briefing.Stock)
            {
                var row = Ui.Create("Stock", sideContent);
                Ui.Row(row, 4);
                Ui.Size(row, height: 26);

                var name = Ui.Label("Name", row.transform, line.DisplayName, 13);
                if (!line.Sellable) name.color = UiTheme.TextDim;
                Ui.Flexible(name.gameObject, 2);

                var count = Ui.Label("Count", row.transform, line.Count.ToString(), 13,
                                     TextAlignmentOptions.Right);
                Ui.Size(count.gameObject, 40);

                var key = line.Key;
                foreach (int amount in new[] { 10, 50 })
                {
                    int n = amount;
                    var button = Ui.Button("Buy" + amount, row.transform, "+" + amount, 12,
                                           () => controller.Order(key, n));
                    Ui.Size(button.gameObject, 42);
                }

                // 解禁されていない段階は仕入れても絶対に売れない（不変条件 5）。
                // 買えなくはしない。育成の到達点が見えていること自体に意味がある。
                if (!line.Sellable)
                {
                    var locked = Ui.Label("Locked", row.transform, "未解禁", 11,
                                          TextAlignmentOptions.Right);
                    locked.color = UiTheme.Warn;
                    Ui.Size(locked.gameObject, 48);
                }
            }
        }

        void BuildIssues()
        {
            Ui.ClearChildren(issueContent);

            var issues = ShiftPlanner.Inspect(
                controller.State.Shop.Roster, controller.Shift, balance,
                controller.State.Week.AbsoluteWeek, controller.State.Alumni);

            if (issues.Count == 0)
            {
                var ok = Ui.Label("Ok", issueContent, "問題なし", 15);
                ok.color = UiTheme.Ok;
                Ui.Size(ok.gameObject, height: 20);
                return;
            }

            foreach (var issue in issues)
            {
                var label = Ui.Label("Issue", issueContent, "・" + issue.Message, 14);
                label.color = issue.Kind == ShiftIssueKind.WillBurnOut
                           || issue.Kind == ShiftIssueKind.InvalidSlot
                           || issue.Kind == ShiftIssueKind.NobodyWorks
                    ? UiTheme.Danger : UiTheme.Warn;
                Ui.Size(label.gameObject, height: 20);
            }
        }

        // ---- 操作 -----------------------------------------------------------

        /// <summary>
        /// 枠を押すたびに 休 → フル → 4h(遅番) → 2h(遅番) と回す。
        ///
        /// 遅番を出すのは、混む時間帯（U-20）が閉店前にあるため。
        /// 短く入れるなら遅番が有利だが、深夜割増と疲労が付いてくる。
        /// </summary>
        void CycleSlot(string castId, int day)
        {
            string key = castId + "#" + day;
            int index = cycleIndex.TryGetValue(key, out int v) ? v : 0;
            index = (index + 1) % SlotCycleHours.Length;
            cycleIndex[key] = index;

            int hours = SlotCycleHours[index];

            if (hours == 0)
            {
                ShiftPlanner.Clear(controller.Shift, castId, day);
            }
            else
            {
                var slot = hours < 0
                    ? ShiftPlanner.FullDay(day, balance)
                    : ShiftPlanner.LateShift(day, hours, balance);

                ShiftPlanner.TrySet(controller.Shift, castId, day,
                                    slot.StartHour, slot.EndHour, balance, out _);
            }

            Refresh();
        }
    }
}
