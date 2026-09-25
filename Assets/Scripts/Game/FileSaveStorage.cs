using System.IO;
using Concafe.Simulation.Save;
using UnityEngine;

namespace Concafe.Game
{
    /// <summary>
    /// セーブの実体を置く側（設計書 7.1）。
    ///
    /// Simulation 層はファイルを知らない。ここが `persistentDataPath` に
    /// 書き出す。WebGL では `Application.persistentDataPath` が IndexedDB に
    /// 載るため同じコードで動くが、書き込み後に <see cref="Flush"/> を
    /// 呼ばないとブラウザを閉じた時点で消える。
    /// </summary>
    public sealed class FileSaveStorage : ISaveStorage
    {
        readonly string directory;

        public FileSaveStorage(string subDirectory = "saves")
        {
            directory = Path.Combine(Application.persistentDataPath, subDirectory);
        }

        public string Directory => directory;

        string PathOf(string key) => Path.Combine(directory, key + ".json");

        public string Read(string key)
        {
            string path = PathOf(key);
            if (!File.Exists(path)) return null;

            try
            {
                return File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (IOException e)
            {
                // 読めないセーブが 1 つあっても一覧は出す（設計書 7）。
                Debug.LogWarning($"セーブが読めない ({key}): {e.Message}");
                return null;
            }
        }

        public void Write(string key, string contents)
        {
            System.IO.Directory.CreateDirectory(directory);

            // 書きかけで電源が落ちても、前のセーブは残す。
            // 直接上書きすると、失敗したときに両方失う。
            string path = PathOf(key);
            string temp = path + ".tmp";

            File.WriteAllText(temp, contents, new System.Text.UTF8Encoding(false));

            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);

            Flush();
        }

        public void Delete(string key)
        {
            string path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
            Flush();
        }

        public bool Exists(string key) => File.Exists(PathOf(key));

        /// <summary>WebGL で書き込みを永続化する。他のプラットフォームでは何もしない。</summary>
        static void Flush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLFileSync.Flush();
#endif
        }
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    static class WebGLFileSync
    {
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void ConcafeSyncFs();

        public static void Flush() => ConcafeSyncFs();
    }
#endif
}
