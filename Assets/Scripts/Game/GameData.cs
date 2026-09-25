using Concafe.Simulation.Balance;
using UnityEngine;

namespace Concafe.Game
{
    /// <summary>
    /// `Resources/Data` の JSON を読み、検査済みの定数を供給する。
    ///
    /// 読み込みと検査は Simulation 層（<see cref="BalanceLoader"/> 等）が持つ。
    /// ここがやるのは **Unity のファイル読み出しだけ**である。
    /// 検査を Presentation 側に持ち出すと、EditMode テストで通した検査と
    /// 実行時の検査が二重になり、いずれ食い違う。
    ///
    /// **壊れたデータで黙って動き出さない。** 定数が壊れたまま起動すると、
    /// バランスがおかしいのか計算式が間違っているのかを切り分けられなくなる。
    /// </summary>
    public sealed class GameData
    {
        public BalanceData Balance { get; private set; }
        public EasterEggSet Eggs { get; private set; }
        public CareerSet Careers { get; private set; }

        /// <summary>読み込みに失敗した理由。成功なら null。</summary>
        public string Error { get; private set; }

        public bool IsValid => Error == null;

        const string BalancePath = "Data/balance";
        const string EggsPath = "Data/easter_eggs";
        const string CareersPath = "Data/careers";

        public static GameData Load()
        {
            var data = new GameData();

            try
            {
                data.Balance = BalanceLoader.Load(ReadText(BalancePath));
                data.Eggs = EasterEggLoader.Load(
                    ReadText(EggsPath), data.Balance.Fatigue.TierCount);
                data.Careers = CareerLoader.Load(ReadText(CareersPath));
            }
            catch (System.Exception e)
            {
                data.Error = e.Message;
                Debug.LogError("データの読み込みに失敗した:\n" + e.Message);
            }

            return data;
        }

        static string ReadText(string resourcePath)
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
                throw new System.IO.FileNotFoundException(
                    $"Resources/{resourcePath}.json が無い");

            return asset.text;
        }
    }
}
