using System;
using System.Collections.Generic;
using Concafe.Simulation.Balance;
using Concafe.Simulation.Model;

namespace Concafe.Simulation.Save
{
    /// <summary>
    /// セーブの置き場所。
    ///
    /// Simulation 層は System.IO も PlayerPrefs も使わない（不変条件 9 と
    /// WebGL 対応のため）ので、実際の保存は Presentation 層が実装する。
    /// テストは丸ごと差し替える。
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>無ければ null。</summary>
        string Read(string key);
        void Write(string key, string contents);
        void Delete(string key);
        bool Exists(string key);
    }

    /// <summary>スロット一覧の 1 行。</summary>
    public sealed class SaveSlotInfo
    {
        public int Index;
        /// <summary>オートセーブ枠か。</summary>
        public bool IsAutoSave;
        public bool IsEmpty => Header == null;
        /// <summary>壊れていて読めなかった場合も true。</summary>
        public bool IsBroken;
        public SaveHeader Header;

        public string Key => IsAutoSave ? SaveSlots.AutoSaveKey : SaveSlots.KeyOf(Index);
    }

    /// <summary>
    /// セーブスロットの管理（U-09 決着: 手動 10 枠 ＋ オートセーブ 1 枠）。
    ///
    /// オートセーブは週の切り替わりで上書きする。手動の 10 枠とは別枠にしてあり、
    /// **オートセーブが手動セーブを潰すことはない。**
    /// </summary>
    public static class SaveSlots
    {
        /// <summary>手動セーブの枠数（U-09）。</summary>
        public const int Count = 10;

        public const string AutoSaveKey = "auto";

        public static string KeyOf(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"スロットは 0〜{Count - 1}");
            return "slot" + index.ToString("00");
        }

        // ---- 保存 -----------------------------------------------------------

        public static void Save(ISaveStorage storage, int index, GameState state, string savedAt = null)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            storage.Write(KeyOf(index), SaveCodec.Encode(state, savedAt));
        }

        /// <summary>
        /// オートセーブ。週の切り替わりで呼ぶ。
        ///
        /// リセマラを許す設計（U-10）なので、オートセーブは
        /// 「やり直しの入口」でもある。手動枠を消費させずにそれを提供する。
        /// </summary>
        public static void AutoSave(ISaveStorage storage, GameState state, string savedAt = null)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            storage.Write(AutoSaveKey, SaveCodec.Encode(state, savedAt));
        }

        // ---- 読み込み -------------------------------------------------------

        /// <summary>
        /// 読み込む。<paramref name="freshSeed"/> はロードのたびに違う値を渡すこと
        /// （U-10）。同じセーブから何度でも違う展開を引ける。
        /// </summary>
        public static GameState Load(
            ISaveStorage storage, int index,
            BalanceData balance, EasterEggSet eggs, CareerSet careers, ulong freshSeed) =>
            LoadKey(storage, KeyOf(index), balance, eggs, careers, freshSeed);

        public static GameState LoadAutoSave(
            ISaveStorage storage,
            BalanceData balance, EasterEggSet eggs, CareerSet careers, ulong freshSeed) =>
            LoadKey(storage, AutoSaveKey, balance, eggs, careers, freshSeed);

        static GameState LoadKey(
            ISaveStorage storage, string key,
            BalanceData balance, EasterEggSet eggs, CareerSet careers, ulong freshSeed)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            string json = storage.Read(key);
            if (json == null) throw new SaveException($"セーブが無い: {key}");

            return SaveCodec.Decode(json, balance, eggs, careers, freshSeed);
        }

        // ---- 一覧と削除 -----------------------------------------------------

        /// <summary>
        /// 一覧。空の枠も壊れた枠も行として返す。
        /// 壊れた 1 つで一覧全体が開けなくなると、他のセーブまで救えなくなる。
        /// </summary>
        public static List<SaveSlotInfo> List(ISaveStorage storage, bool includeAutoSave = true)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));

            var list = new List<SaveSlotInfo>(Count + 1);

            if (includeAutoSave)
                list.Add(Describe(storage, AutoSaveKey, -1, isAuto: true));

            for (int i = 0; i < Count; i++)
                list.Add(Describe(storage, KeyOf(i), i, isAuto: false));

            return list;
        }

        static SaveSlotInfo Describe(ISaveStorage storage, string key, int index, bool isAuto)
        {
            var info = new SaveSlotInfo { Index = index, IsAutoSave = isAuto };

            string json = storage.Read(key);
            if (json == null) return info;

            info.Header = SaveCodec.PeekHeader(json);
            info.IsBroken = info.Header == null;
            return info;
        }

        public static void Delete(ISaveStorage storage, int index)
        {
            if (storage == null) throw new ArgumentNullException(nameof(storage));
            storage.Delete(KeyOf(index));
        }

        public static bool Exists(ISaveStorage storage, int index) =>
            storage != null && storage.Exists(KeyOf(index));

        public static bool HasAutoSave(ISaveStorage storage) =>
            storage != null && storage.Exists(AutoSaveKey);
    }

    /// <summary>
    /// メモリ上の保存先。テストと、保存先が用意できていない場面の既定値。
    /// これがあるので Simulation 層だけでセーブの往復を検証できる。
    /// </summary>
    public sealed class InMemorySaveStorage : ISaveStorage
    {
        readonly Dictionary<string, string> files =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string Read(string key) => files.TryGetValue(key, out string v) ? v : null;
        public void Write(string key, string contents) => files[key] = contents;
        public void Delete(string key) => files.Remove(key);
        public bool Exists(string key) => files.ContainsKey(key);

        public int FileCount => files.Count;
    }
}
