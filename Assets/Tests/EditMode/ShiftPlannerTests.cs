using System.Linq;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using NUnit.Framework;

namespace Concafe.Tests
{
    /// <summary>
    /// 週頭の画面が使う計算（段階 7）。
    ///
    /// UI を通さずに検証できる形で置いてあること自体が要点である。
    /// MonoBehaviour に計算を書くと、シフトの良し悪しをテストで確かめられなくなる。
    /// </summary>
    public class ShiftPlannerTests
    {
        BalanceData balance;
        EasterEggSet eggs;
        CareerSet careers;

        [SetUp]
        public void SetUp()
        {
            balance = TestBalance.Load();
            eggs = TestEggs.Load(balance);
            careers = TestCareers.Load();
        }

        Cast MakeCast(string id = "c0", double stats = 40, int tier = 3, int age = 22)
        {
            var c = new Cast
            {
                Id = id, StageName = id, Age = age,
                StaminaTier = tier, ScandalResist = 50,
            };
            c.Current = Stats.Uniform(stats);
            c.Cap = Stats.Uniform(100);
            c.GrowthRate = Stats.Uniform(1.0);
            return c;
        }

        // ---- 編集 -----------------------------------------------------------

        [Test]
        public void 営業時間の外は組めない()
        {
            var shift = new Shift();

            Assert.IsFalse(ShiftPlanner.TrySet(shift, "c0", 0, 10, 20, balance, out string why));
            StringAssert.Contains("開店", why);
            Assert.IsFalse(shift.IsWorking("c0", 0), "弾いたのに枠が入っている");

            Assert.IsFalse(ShiftPlanner.TrySet(shift, "c0", 0, 20, 25, balance, out why));
            StringAssert.Contains("閉店", why);
        }

        [Test]
        public void 最短シフト未満は組めない()
        {
            var shift = new Shift();
            int open = balance.Business.OpenHour(0);

            Assert.IsFalse(ShiftPlanner.TrySet(shift, "c0", 0, open, open + 1, balance, out string why));
            StringAssert.Contains("最短", why);
        }

        [Test]
        public void 遅番と早番は同じ長さで時刻が違う()
        {
            var late = ShiftPlanner.LateShift(0, 3, balance);
            var early = ShiftPlanner.EarlyShift(0, 3, balance);

            Assert.AreEqual(3, late.Hours);
            Assert.AreEqual(3, early.Hours);
            Assert.AreEqual(balance.Business.CloseHour, late.EndHour);
            Assert.AreEqual(balance.Business.OpenHour(0), early.StartHour);
        }

        [Test]
        public void 営業時間より長いプリセットは営業時間で止まる()
        {
            // 平日は 6 時間しかない。8 時間を頼まれても収める。
            var late = ShiftPlanner.LateShift(0, 20, balance);
            var early = ShiftPlanner.EarlyShift(0, 20, balance);

            Assert.AreEqual(balance.Business.OpenHours(0), late.Hours);
            Assert.AreEqual(balance.Business.OpenHours(0), early.Hours);
        }

        // ---- 見積り ---------------------------------------------------------

        [Test]
        public void 出勤しない週は疲労が回復する()
        {
            var cast = MakeCast();
            cast.Fatigue = 10;

            var e = ShiftPlanner.Estimate(cast, new Shift(), balance);

            Assert.AreEqual(0, e.WorkDays);
            Assert.AreEqual(0, e.BaseWageYen);
            Assert.Less(e.FatigueAfter, cast.Fatigue, "全休なのに疲労が減らない");
        }

        [Test]
        public void 長く入れるほど給料も疲労も増える()
        {
            var cast = MakeCast();

            ShiftEstimate Days(int days)
            {
                var shift = new Shift();
                for (int d = 0; d < days; d++)
                    shift.Set(cast.Id, d, ShiftPlanner.FullDay(d, balance));
                return ShiftPlanner.Estimate(cast, shift, balance);
            }

            var three = Days(3);
            var five = Days(5);

            Assert.Greater(five.WorkHours, three.WorkHours);
            Assert.Greater(five.BaseWageYen, three.BaseWageYen);
            Assert.Greater(five.FatigueAfter, three.FatigueAfter);
        }

        [Test]
        public void 遅番は同じ長さでも来客と給料が多い()
        {
            // U-20: 夜ほど混む。ピークは深夜割増と重なっている。
            var cast = MakeCast();

            ShiftEstimate With(System.Func<int, ShiftSlot> pick)
            {
                var shift = new Shift();
                for (int d = 0; d < 5; d++) shift.Set(cast.Id, d, pick(d));
                return ShiftPlanner.Estimate(cast, shift, balance);
            }

            var early = With(d => ShiftPlanner.EarlyShift(d, 3, balance));
            var late = With(d => ShiftPlanner.LateShift(d, 3, balance));

            Assert.AreEqual(early.WorkHours, late.WorkHours, "比較の前提が崩れている");
            Assert.Greater(late.VisitorFactor, early.VisitorFactor, "遅番のほうが空いている");
            Assert.Greater(late.BaseWageYen, early.BaseWageYen, "深夜割増が乗っていない");
            Assert.Greater(late.LateNightHours, 0);
            Assert.AreEqual(0, early.LateNightHours);
        }

        [Test]
        public void 信頼度が高いと疲労の見積りが軽くなる()
        {
            // 不変条件 1: 延命は蓄積速度の低下だけで行う。限界値は動かない。
            var plain = MakeCast();
            var trusted = MakeCast();
            trusted.Trust = 100;

            var shift = new Shift();
            for (int d = 0; d < 5; d++) shift.Set("c0", d, ShiftPlanner.FullDay(d, balance));

            var a = ShiftPlanner.Estimate(plain, shift, balance);
            var b = ShiftPlanner.Estimate(trusted, shift, balance);

            Assert.Less(b.FatigueGain, a.FatigueGain, "信頼度が蓄積速度に効いていない");
            Assert.AreEqual(a.FatigueLimit, b.FatigueLimit, "限界値が動いている（不変条件 1）");
        }

        [Test]
        public void 疲労限界の低いキャストほど早く振り切れる()
        {
            var shift = new Shift();
            for (int d = 0; d < 6; d++) shift.Set("c0", d, ShiftPlanner.FullDay(d, balance));

            var weak = ShiftPlanner.Estimate(MakeCast(tier: 1), shift, balance);
            var tough = ShiftPlanner.Estimate(MakeCast(tier: 5), shift, balance);

            Assert.Greater(weak.FatigueRatio, tough.FatigueRatio);
        }

        // ---- 点検 -----------------------------------------------------------

        [Test]
        public void 誰も出勤しない週は指摘される()
        {
            var roster = new[] { MakeCast() };
            var issues = ShiftPlanner.Inspect(roster, new Shift(), balance);

            Assert.IsTrue(issues.Any(i => i.Kind == ShiftIssueKind.NobodyWorks),
                "1 人も出勤しない週が素通りする");
            Assert.AreEqual(Shift.DaysPerWeek,
                issues.Count(i => i.Kind == ShiftIssueKind.ClosedDay),
                "臨時休業の日が全部は挙がっていない");
        }

        [Test]
        public void 限界を超える組み方は週を開始する前に分かる()
        {
            // 「回してみたら飛んでいた」では遅い。組んだ時点で見えること。
            var cast = MakeCast(tier: 1, stats: 60);
            cast.Fatigue = cast.FatigueLimit(balance) * 0.7;

            var shift = new Shift();
            for (int d = 0; d < 7; d++) shift.Set(cast.Id, d, ShiftPlanner.FullDay(d, balance));

            var issues = ShiftPlanner.Inspect(new[] { cast }, shift, balance);

            Assert.IsTrue(issues.Any(i => i.Kind == ShiftIssueKind.WillBurnOut),
                "限界を超える見込みが指摘されない");
        }

        [Test]
        public void 疲れているキャストは休ませる前に警告が出る()
        {
            var cast = MakeCast(tier: 5);
            cast.Fatigue = cast.FatigueLimit(balance) * balance.Departure.WarnRatio;

            // 1 日だけ。限界は超えないが、すでに疲れている。
            var shift = new Shift();
            shift.Set(cast.Id, 0, ShiftPlanner.FullDay(0, balance));

            var issues = ShiftPlanner.Inspect(new[] { cast }, shift, balance);

            Assert.IsTrue(issues.Any(i => i.Kind == ShiftIssueKind.AlreadyTired),
                "予兆の水準を超えているのに何も出ない");
        }

        [Test]
        public void 指摘には対象のキャストと曜日が入っている()
        {
            // 画面で「どこを直せばいいか」が分かる形であること。
            var cast = MakeCast();
            var shift = new Shift();
            shift.Set(cast.Id, 2, ShiftPlanner.FullDay(2, balance));

            var issues = ShiftPlanner.Inspect(new[] { cast }, shift, balance);
            var closed = issues.First(i => i.Kind == ShiftIssueKind.ClosedDay);

            Assert.GreaterOrEqual(closed.Day, 0);
            Assert.IsNotEmpty(closed.Message);
            Assert.AreNotEqual(2, closed.Day, "出勤している日が臨時休業扱いになっている");
        }

        // ---- 週頭のまとめ ---------------------------------------------------

        [Test]
        public void 週頭のまとめが画面に要る値を揃えている()
        {
            var state = GameFlow.NewGame(balance, eggs, careers, 2026);
            GameFlow.BeginWeek(state);

            var b = WeekBriefingBuilder.Build(state);

            Assert.AreEqual(1, b.Year);
            Assert.AreEqual(state.Shop.CashYen, b.CashYen);
            Assert.AreEqual(state.Shop.Roster.Count, b.CastCount);
            Assert.AreEqual(balance.Manager.ApPerWeek, b.ManagerAp);
            Assert.AreEqual(Shift.DaysPerWeek, b.Forecast.Length);
            Assert.AreEqual(state.Shop.FridgeCapacity(balance), b.FridgeCapacity);
            Assert.IsNotEmpty(b.Stock);
        }

        [Test]
        public void 発注一覧に解禁されていない段階も並ぶ()
        {
            // 設計書 8: 発注画面は「今の店で何が売れるか」を映す。
            // 未解禁を隠すと、育成の到達点が見えなくなる。
            var state = GameFlow.NewGame(balance, eggs, careers, 2026);
            GameFlow.BeginWeek(state);

            var b = WeekBriefingBuilder.Build(state);

            int bottleLines = b.Stock.Count(s => StockItems.IsBottle(s.Key.Item));
            Assert.AreEqual(
                balance.Bottles.Alcoholic.Count + balance.Bottles.NonAlcoholic.Count,
                bottleLines, "ボトルの段階が一覧から抜けている");

            Assert.IsTrue(b.Stock.Any(s => !s.Sellable), "未解禁の段階が 1 つも無い");
            foreach (var line in b.Stock.Where(s => !s.Sellable))
                Assert.IsNotEmpty(line.LockedReason, "売れない理由が空");
        }

        [Test]
        public void 育てると発注一覧の解禁段階が増える()
        {
            // 一覧がそのまま育成の進捗計になっていること。
            var state = GameFlow.NewGame(balance, eggs, careers, 2026);
            GameFlow.BeginWeek(state);

            int Sellable() => WeekBriefingBuilder.Build(state).Stock.Count(s => s.Sellable);

            int before = Sellable();

            foreach (var c in state.Shop.Roster)
            {
                c.Current.Talk = 95;
                c.Current.Alcohol = 95;
            }

            Assert.Greater(Sellable(), before, "育てても解禁段階が増えない");
        }

        [Test]
        public void 冷蔵しない品目は冷蔵庫の数に入らない()
        {
            var state = GameFlow.NewGame(balance, eggs, careers, 2026);
            GameFlow.BeginWeek(state);

            var b = WeekBriefingBuilder.Build(state);

            var cheki = b.Stock.First(s => s.Key.Item == StockItem.ChekiFilm);
            var alcohol = b.Stock.First(s => s.Key.Item == StockItem.Alcohol);

            Assert.IsFalse(cheki.Chilled, "チェキフィルムが冷蔵扱いになっている");
            Assert.IsTrue(alcohol.Chilled);
        }
    }
}
