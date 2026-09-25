using TMPro;
using UnityEditor;
using UnityEngine;

namespace Concafe.EditorTools
{
    /// <summary>
    /// 手作業でしか入らない下ごしらえをコードから済ませる。
    ///
    /// **クローンした直後の状態からビルドが通ること**を保てるようにする。
    /// 「Unity のメニューからこれを押しておくこと」という手順書は必ず腐る。
    ///
    /// <code>
    /// Unity.exe -batchmode -projectPath . \
    ///   -executeMethod Concafe.EditorTools.ProjectSetup.SetUpBatch
    /// </code>
    ///
    /// batchmode では <b>-quit を付けないこと</b>。取り込みが終わる前に
    /// Unity が落ちる。<see cref="SetUpBatch"/> が完了を見届けてから自分で終了する。
    /// </summary>
    public static class ProjectSetup
    {
        /// <summary>取り込みの完了を待つ上限。これを超えたら失敗として扱う。</summary>
        const double TimeoutSeconds = 300;

        [MenuItem("Tools/コンカフェ/1. プロジェクトの下ごしらえ")]
        public static void SetUp()
        {
            ImportTmpEssentialResources();
        }

        /// <summary>batchmode 用。取り込みの完了を待ってから Unity を終了する。</summary>
        public static void SetUpBatch()
        {
            if (IsReady())
            {
                Debug.Log("下ごしらえは済んでいる");
                EditorApplication.Exit(0);
                return;
            }

            ImportTmpEssentialResources();
            WaitUntilReady();
        }

        /// <summary>
        /// TextMeshPro の必須リソースを入れる。
        ///
        /// これが無いと `TMP Settings` が存在せず、**実行時にフォントを作ろうとした
        /// 時点で NullReferenceException になる。** 画面が 1 文字も出ない壊れ方をする。
        /// 通常は「Window > TextMeshPro > Import TMP Essential Resources」を
        /// 手で押す手順だが、押し忘れると原因の分かりにくい落ち方をするので自動化する。
        /// </summary>
        public static void ImportTmpEssentialResources()
        {
            if (IsReady())
            {
                Debug.Log("TMP の必須リソースは導入済み");
                return;
            }

            Debug.Log("TMP の必須リソースを導入する");

            // TMP 自身の導入 API を使う。interactive: false で確認ダイアログを出さない。
            // **取り込みは非同期である。** この呼び出しが返った時点では
            // まだ 1 つもアセットが入っていない。
            TMP_PackageResourceImporter.ImportResources(
                importEssentials: true, importExamples: false, interactive: false);
        }

        static bool IsReady() => TMP_Settings.instance != null;

        static void WaitUntilReady()
        {
            double deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;

            void Poll()
            {
                if (IsReady())
                {
                    EditorApplication.update -= Poll;
                    AssetDatabase.SaveAssets();
                    Debug.Log("TMP の必須リソースが入った");
                    EditorApplication.Exit(0);
                    return;
                }

                if (EditorApplication.timeSinceStartup > deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError($"TMP の必須リソースが {TimeoutSeconds} 秒経っても入らない");
                    EditorApplication.Exit(1);
                }
            }

            EditorApplication.update += Poll;
        }
    }
}
