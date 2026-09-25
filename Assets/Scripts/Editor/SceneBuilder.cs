using System.IO;
using Concafe.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Concafe.EditorTools
{
    /// <summary>
    /// `Assets/Scenes/Main.unity` をコードから組み立てる（不変条件 15）。
    ///
    /// シーンを手で編集しない。Inspector 上の手作業は差分としてレビューできず、
    /// 「いつ誰が何を変えたのか」が追えなくなる。UI を変えたいときは
    /// このスクリプトか <see cref="Ui"/> を直して、メニューから作り直す。
    ///
    /// **シーンには入れ物しか置かない。** カメラ・EventSystem・Canvas と
    /// <see cref="GameController"/> だけで、画面の中身は実行時に組み立てる。
    /// シフト表がキャスト数 × 7 日の可変グリッドである以上、静的に置けない。
    ///
    /// <code>
    /// Unity.exe -batchmode -quit -projectPath . \
    ///   -executeMethod Concafe.EditorTools.SceneBuilder.BuildScene
    /// </code>
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>ドット絵を主にするので、基準解像度は固定して整数倍で見せる。</summary>
        static readonly Vector2 ReferenceResolution = new Vector2(1600, 900);

        [MenuItem("Tools/コンカフェ/シーンを生成する")]
        public static void BuildScene()
        {
            PlayerSettings.companyName = "ryo-sato0619";
            PlayerSettings.productName = "concafe-sim";

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();

            var canvas = CreateCanvas();
            canvas.gameObject.AddComponent<GameController>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            RegisterInBuildSettings();

            AssetDatabase.SaveAssets();
            Debug.Log($"シーンを生成した: {ScenePath}");
        }

        static void CreateCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UiTheme.Background;
            camera.orthographic = true;

            go.transform.position = new Vector3(0, 0, -10);
        }

        static void CreateEventSystem()
        {
            // これが無いとボタンが一切押せない。UI だけのゲームなので必須。
            var go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<StandaloneInputModule>();
        }

        static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;

            // 横に広い画面でもシフト表（7 日 ＋ 見積り）が切れないよう、
            // 幅に寄せて合わせる。
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        static void RegisterInBuildSettings()
        {
            // シーンは 1 つだけ（設計書 8）。毎回入れ替えて、増えないようにする。
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
            };
        }
    }
}
