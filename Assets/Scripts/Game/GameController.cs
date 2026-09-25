using Concafe.Simulation.Model;
using Concafe.Simulation.Rules;
using Concafe.Simulation.Save;
using TMPro;
using UnityEngine;

namespace Concafe.Game
{
    /// <summary>
    /// ゲーム全体の進行役。
    ///
    /// **状態を持つのは Simulation 層の <see cref="GameState"/> だけ**で、
    /// ここはそれを保持して画面に繋ぐ。ルールをこちらに書かないこと。
    /// UI を通さないと検証できない規則ができると、EditMode の通し
    /// シミュレーションからその規則が抜け落ちる。
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public GameData Data { get; private set; }
        public GameState State { get; private set; }

        /// <summary>編集中のシフト。週を回すまで確定しない。</summary>
        public Shift Shift { get; private set; }

        ISaveStorage storage;
        WeekStartScreen weekStart;
        TextMeshProUGUI toast;
        float toastUntil;

        /// <summary>採用時に入力する源氏名。イースターエッグの判定対象になる。</summary>
        TMP_InputField nameInput;
        Applicant pendingHire;

        void Start()
        {
            Data = GameData.Load();
            if (!Data.IsValid)
            {
                ShowFatal(Data.Error);
                return;
            }

            storage = new FileSaveStorage();

            BuildScreens();
            NewGame();
        }

        // ---- 進行 -----------------------------------------------------------

        public void NewGame()
        {
            // 種は起動ごとに変える。乱数の状態はセーブに含めないので（U-10）、
            // 非決定性の源はここと、ロード時に渡す種だけである。
            State = GameFlow.NewGame(Data.Balance, Data.Eggs, Data.Careers, FreshSeed());
            BeginWeek();
        }

        public void LoadAutoSave()
        {
            if (!SaveSlots.HasAutoSave(storage)) { Toast("オートセーブが無い"); return; }

            State = SaveSlots.LoadAutoSave(
                storage, Data.Balance, Data.Eggs, Data.Careers, FreshSeed());

            Shift = Shift.FullDays(State.Shop.Roster, 0, Data.Balance);
            weekStart.Refresh();
            Toast("オートセーブから再開した");
        }

        void BeginWeek()
        {
            GameFlow.BeginWeek(State);

            // シフトは毎週組み直す。前週の写しを残すと、疲れているキャストを
            // そのまま出勤させ続けてしまう。
            Shift = new Shift();

            // 週頭の状態をオートセーブする（U-09）。手動枠とは別枠なので潰さない。
            SaveSlots.AutoSave(storage, State, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

            weekStart.Refresh();
        }

        public void RunWeek()
        {
            if (State.IsOver) { Toast("このゲームは終わっている"); return; }

            var result = GameFlow.RunWeek(State, Shift);

            // 段階 8・9 で営業パートと決算画面に差し替える。
            // いまは結果を 1 行で出して次の週へ進む。
            Toast($"売上 {result.RevenueYen:N0} 円 / 利益 {result.ProfitYen:N0} 円" +
                  (result.Departures.Count > 0 ? $" / 離脱 {result.Departures.Count} 人" : "") +
                  (result.Graduations.Count > 0 ? $" / 卒業 {result.Graduations.Count} 人" : ""),
                  6f);

            if (State.IsOver)
            {
                Toast(State.GameOver == GameOverReason.Arson
                    ? "店は燃えた。ゲームオーバー"
                    : "資金が尽きた。ゲームオーバー", 30f);
                weekStart.Refresh();
                return;
            }

            BeginWeek();
        }

        // ---- 週頭の操作 -----------------------------------------------------

        public void Care(string castId) => Do(ManagerActions.Care(State, State.Shop.FindCast(castId)));

        public void Train(string castId, StatKind kind)
        {
            var cast = State.Shop.FindCast(castId);

            // 特訓本があれば AP を使わずに済む（出版関係者 OB）。先に使い切る。
            if (State.TrainingBooks > 0)
            {
                Do(ManagerActions.TrainWithBook(State, cast, kind));
                return;
            }
            Do(ManagerActions.Train(State, cast, kind));
        }

        public void Recruit(int tierIndex) => Do(ManagerActions.Recruit(State, tierIndex));

        public void Interview(Applicant applicant) => Do(ManagerActions.Interview(State, applicant));

        public void Order(StockKey key, int amount) => Do(ManagerActions.Order(State, key, amount));

        /// <summary>
        /// 採用。源氏名の入力を求める。
        /// **ここで入力した名前がイースターエッグの判定対象になる**（設計書 6）。
        /// </summary>
        public void RequestHire(Applicant applicant)
        {
            pendingHire = applicant;
            nameInput.transform.parent.gameObject.SetActive(true);
            nameInput.text = "";
            nameInput.Select();
            nameInput.ActivateInputField();
        }

        void ConfirmHire()
        {
            if (pendingHire == null) return;

            string stageName = nameInput.text;
            if (string.IsNullOrWhiteSpace(stageName)) { Toast("源氏名を入れること"); return; }

            var result = ManagerActions.Hire(State, pendingHire, stageName);
            if (result == ActionResult.Ok)
            {
                var hired = State.Shop.Roster[State.Shop.Roster.Count - 1];
                var egg = EasterEggRules.FindEgg(hired, State.Eggs);

                if (egg != null && egg.RevealOnHire && !string.IsNullOrEmpty(egg.HireMessage))
                    Toast(egg.HireMessage, 10f);
            }

            CancelHire();
            Do(result);
        }

        void CancelHire()
        {
            pendingHire = null;
            nameInput.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>操作の結果を画面に返す。失敗しても黙って飲み込まない。</summary>
        void Do(ActionResult result)
        {
            if (result != ActionResult.Ok) Toast(Describe(result));
            weekStart.Refresh();
        }

        static string Describe(ActionResult result)
        {
            switch (result)
            {
                case ActionResult.NotEnoughAp: return "AP が足りない";
                case ActionResult.NotEnoughCash: return "資金が足りない";
                case ActionResult.NoSlot: return "キャストの枠が埋まっている";
                case ActionResult.NoBook: return "特訓本が無い";
                case ActionResult.NoStorage: return "冷蔵庫に入りきらない。冷蔵庫の拡張が要る";
                case ActionResult.NotFound: return "対象が見つからない";
                default: return "その操作はできない";
            }
        }

        static ulong FreshSeed() =>
            (ulong)System.DateTime.UtcNow.Ticks ^ ((ulong)Random.Range(int.MinValue, int.MaxValue) << 32);

        // ---- 画面 -----------------------------------------------------------

        void BuildScreens()
        {
            var canvas = GetComponentInChildren<Canvas>() ?? GetComponent<Canvas>();
            var parent = canvas != null ? canvas.transform : transform;

            var screen = Ui.Create("WeekStart", parent);
            Ui.Stretch(screen);
            weekStart = screen.AddComponent<WeekStartScreen>();
            weekStart.Init(this);

            BuildNamePrompt(parent);
            BuildToast(parent);
        }

        void BuildNamePrompt(Transform parent)
        {
            var overlay = Ui.Panel("NamePrompt", parent, new Color(0, 0, 0, 0.78f));
            Ui.Stretch(overlay.gameObject);

            var box = Ui.Panel("Box", overlay.transform, UiTheme.Panel);
            var rect = Ui.Rect(box.gameObject);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(520, 200);

            var column = Ui.Create("Column", box.transform);
            Ui.Stretch(column, 16);
            Ui.Column(column, 10);

            var title = Ui.Label("Title", column.transform, "源氏名を決める", 22);
            Ui.Size(title.gameObject, height: 30);

            var hint = Ui.Label("Hint", column.transform,
                "この名前で 10 年を過ごす。あとから変えられない", 14);
            hint.color = UiTheme.TextDim;
            Ui.Size(hint.gameObject, height: 20);

            var field = Ui.Panel("Field", column.transform, UiTheme.PanelDark);
            Ui.Size(field.gameObject, height: 44);

            nameInput = field.gameObject.AddComponent<TMP_InputField>();
            var text = Ui.Label("Text", field.transform, "", 20);
            Ui.Stretch(text.gameObject, 8);
            text.raycastTarget = true;
            nameInput.textComponent = text;
            nameInput.textViewport = Ui.Rect(field.gameObject);
            nameInput.onSubmit.AddListener(_ => ConfirmHire());

            var buttons = Ui.Create("Buttons", column.transform);
            Ui.Row(buttons, 8);
            Ui.Size(buttons, height: 40);

            var ok = Ui.Button("Ok", buttons.transform, "採用する", 17, ConfirmHire);
            Ui.Flexible(ok.gameObject);
            var cancel = Ui.Button("Cancel", buttons.transform, "やめる", 17, CancelHire);
            Ui.Flexible(cancel.gameObject);

            overlay.gameObject.SetActive(false);
        }

        void BuildToast(Transform parent)
        {
            var box = Ui.Panel("Toast", parent, UiTheme.PanelDark);
            var rect = Ui.Rect(box.gameObject);
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 24);
            rect.sizeDelta = new Vector2(900, 44);

            toast = Ui.Label("Text", box.transform, "", 17, TextAlignmentOptions.Center);
            Ui.Stretch(toast.gameObject, 8);

            box.gameObject.SetActive(false);
        }

        void Toast(string message, float seconds = 3.5f)
        {
            if (toast == null) { Debug.Log(message); return; }

            toast.text = message;
            toast.transform.parent.gameObject.SetActive(true);
            toastUntil = Time.time + seconds;
        }

        void ShowFatal(string message)
        {
            var canvas = GetComponentInChildren<Canvas>();
            var parent = canvas != null ? canvas.transform : transform;

            var label = Ui.Label("Fatal", parent,
                "データが読めないので始められない。\n\n" + message, 18);
            Ui.Stretch(label.gameObject, 40);
            label.color = UiTheme.Danger;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
        }

        void Update()
        {
            if (toast != null && toast.transform.parent.gameObject.activeSelf
                && Time.time > toastUntil)
                toast.transform.parent.gameObject.SetActive(false);
        }
    }
}
