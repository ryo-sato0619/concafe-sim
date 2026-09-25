using System.Collections;
using System.Linq;
using Concafe.Game;
using Concafe.Simulation.Model;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Concafe.Tests.PlayMode
{
    /// <summary>
    /// 生成したシーンが実際に動くことを確かめる（段階 7）。
    ///
    /// EditMode テストは Simulation 層しか触れないので、
    /// **「UI から 1 週間の指示が出せる」を確かめられるのはここだけ**である。
    /// 画面が初手で例外を投げる類の壊れ方は、数式のテストでは絶対に捕まらない。
    /// </summary>
    public class WeekStartScreenTests
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        GameController controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
            yield return null;   // Start() が走る
            yield return null;   // 最初の Refresh() が済む

            controller = Object.FindAnyObjectByType<GameController>();
            Assert.IsNotNull(controller, "シーンに GameController がいない");
        }

        // ---- 起動 -----------------------------------------------------------

        [UnityTest]
        public IEnumerator シーンが例外を出さずに立ち上がる()
        {
            Assert.IsTrue(controller.Data.IsValid,
                "データが読めていない: " + controller.Data.Error);

            Assert.IsNotNull(controller.State, "ゲームが始まっていない");
            Assert.IsNotNull(controller.Shift, "シフトが用意されていない");

            // 前任者から 2 人を引き継いでいる（設計書 14.1）。
            Assert.AreEqual(controller.Data.Balance.Shop.InitialCastNames.Count,
                            controller.State.Shop.Roster.Count);

            yield break;
        }

        [UnityTest]
        public IEnumerator 絵が一枚も無くても画面が成立する()
        {
            // 不変条件 14: パーツ PNG が 0 枚でも通しでプレイできること。
            // いまは絵を一切使っていないので、ここが落ちるなら
            // 知らないうちに画像への依存が入ったということ。
            var images = Object.FindObjectsByType<Image>(FindObjectsSortMode.None);

            foreach (var image in images)
                Assert.IsNull(image.sprite,
                    $"{image.name} がスプライトを要求している。絵が無い状態で崩れる");

            Assert.Greater(images.Length, 0, "画面が組み立てられていない");
            yield break;
        }

        [UnityTest]
        public IEnumerator 日本語が豆腐にならない()
        {
            // **フォントが割り当たっているだけでは足りない。** TMP は既定で
            // LiberationSans を当てるが、あれに日本語のグリフは無い。
            // 「font が null でない」で通してしまうと、全文字が豆腐のまま
            // テストが緑になる。字形を持っているかまで見る。
            var labels = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None);
            Assert.Greater(labels.Length, 0, "文字が 1 つも出ていない");

            var japanese = labels.FirstOrDefault(l => l.text.Contains("週"));
            Assert.IsNotNull(japanese, "日本語のラベルが見つからない");
            Assert.IsNotNull(japanese.font, "フォントが割り当てられていない");

            Assert.IsTrue(japanese.font.HasCharacter('週'),
                $"フォント '{japanese.font.name}' に日本語のグリフが無い。豆腐になる");

            yield break;
        }

        // ---- シフト表 -------------------------------------------------------

        [UnityTest]
        public IEnumerator 枠を押すとシフトが入る()
        {
            var cast = controller.State.Shop.Roster[0];
            Assert.IsFalse(controller.Shift.IsWorking(cast.Id, 0), "最初から出勤が入っている");

            ClickSlot(cast.Id, 0);
            yield return null;

            Assert.IsTrue(controller.Shift.IsWorking(cast.Id, 0), "枠を押しても出勤が入らない");

            var slot = controller.Shift.Get(cast.Id, 0);
            var business = controller.Data.Balance.Business;
            Assert.AreEqual(business.OpenHour(0), slot.StartHour);
            Assert.AreEqual(business.CloseHour, slot.EndHour);
        }

        [UnityTest]
        public IEnumerator 押し続けると休みに戻る()
        {
            // 休 → フル → 4h → 2h → 休 の 4 段階。
            var cast = controller.State.Shop.Roster[0];

            for (int i = 0; i < 4; i++)
            {
                ClickSlot(cast.Id, 1);
                yield return null;
            }

            Assert.IsFalse(controller.Shift.IsWorking(cast.Id, 1),
                "一周しても休みに戻らない。組み間違えを取り消せない");
        }

        [UnityTest]
        public IEnumerator 短いシフトは閉店前に寄る()
        {
            // U-20: 混む時間帯は夜。短く入れるなら遅番を既定にする。
            var cast = controller.State.Shop.Roster[0];

            ClickSlot(cast.Id, 2);   // フル
            ClickSlot(cast.Id, 2);   // 4h
            yield return null;

            var slot = controller.Shift.Get(cast.Id, 2);
            Assert.AreEqual(controller.Data.Balance.Business.CloseHour, slot.EndHour,
                "短いシフトが閉店前に寄っていない");
            Assert.AreEqual(4, slot.Hours);
        }

        // ---- 1 週間を回す ---------------------------------------------------

        [UnityTest]
        public IEnumerator UI_から一週間の指示が出せる()
        {
            // 段階 7 の完了条件そのもの。
            var state = controller.State;
            int before = state.Week.AbsoluteWeek;
            long cashBefore = state.Shop.CashYen;

            foreach (var cast in state.Shop.Roster)
                for (int day = 0; day < 4; day++)
                    ClickSlot(cast.Id, day);

            yield return null;

            Click("Start");
            yield return null;

            Assert.AreEqual(before + 1, controller.State.Week.AbsoluteWeek, "週が進んでいない");
            Assert.AreNotEqual(cashBefore, controller.State.Shop.CashYen,
                "1 週間回したのに資金が動いていない");
        }

        [UnityTest]
        public IEnumerator 週が変わるとシフトは組み直しになる()
        {
            // 前週の写しを残すと、疲れているキャストをそのまま出し続けてしまう。
            var cast = controller.State.Shop.Roster[0];
            ClickSlot(cast.Id, 0);
            yield return null;

            Click("Start");
            yield return null;

            Assert.IsFalse(controller.Shift.IsWorking(cast.Id, 0),
                "前の週のシフトが残っている");
        }

        [UnityTest]
        public IEnumerator 週頭にオートセーブされる()
        {
            var storage = new FileSaveStorage();
            Assert.IsTrue(storage.Exists(Concafe.Simulation.Save.SaveSlots.AutoSaveKey),
                "週頭のオートセーブが出ていない");

            yield break;
        }

        // ---- 操作の補助 -----------------------------------------------------

        /// <summary>シフト表の枠を押す。</summary>
        void ClickSlot(string castId, int day)
        {
            var row = FindByName("Row_" + castId);
            Assert.IsNotNull(row, $"{castId} の行が無い");

            var slot = row.transform.Find("Slot" + day);
            Assert.IsNotNull(slot, $"{castId} の {day} 日目の枠が無い");

            slot.GetComponent<Button>().onClick.Invoke();
        }

        void Click(string name)
        {
            var go = FindByName(name);
            Assert.IsNotNull(go, $"ボタン {name} が無い");
            go.GetComponent<Button>().onClick.Invoke();
        }

        static GameObject FindByName(string name)
        {
            foreach (var t in Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
                if (t.name == name) return t.gameObject;
            return null;
        }
    }
}
