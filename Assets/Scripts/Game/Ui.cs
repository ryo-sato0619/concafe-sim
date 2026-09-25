using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Concafe.Game
{
    /// <summary>画面の色。1 か所に集める。</summary>
    public static class UiTheme
    {
        public static readonly Color Background = new Color(0.07f, 0.06f, 0.09f, 1f);
        public static readonly Color Panel = new Color(0.12f, 0.11f, 0.15f, 0.96f);
        public static readonly Color PanelDark = new Color(0.09f, 0.08f, 0.11f, 0.96f);
        public static readonly Color Accent = new Color(0.91f, 0.55f, 0.66f, 1f);
        public static readonly Color Text = new Color(0.95f, 0.94f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.63f, 0.61f, 0.68f, 1f);
        public static readonly Color Button = new Color(0.20f, 0.18f, 0.26f, 1f);
        public static readonly Color ButtonOn = new Color(0.44f, 0.28f, 0.42f, 1f);
        public static readonly Color Warn = new Color(0.95f, 0.72f, 0.36f, 1f);
        public static readonly Color Danger = new Color(0.92f, 0.42f, 0.42f, 1f);
        public static readonly Color Ok = new Color(0.50f, 0.82f, 0.62f, 1f);
        public static readonly Color Holiday = new Color(0.30f, 0.22f, 0.30f, 1f);
    }

    /// <summary>
    /// UI をコードから組み立てるための部品。
    ///
    /// **シーンに widget を並べない**（不変条件 15）。シフト表はキャスト数 × 7 日の
    /// 可変グリッドで、在籍が 3 人から 12 人まで動く以上、静的なシーンには置けない。
    /// シーンには Canvas と入れ物だけを置き、中身は実行時にここで作る。
    /// 差分がレビューできる形で残るのも同じ理由による。
    /// </summary>
    public static class Ui
    {
        public static RectTransform Rect(GameObject go) =>
            go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();

        public static GameObject Create(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>親いっぱいに広げる。</summary>
        public static RectTransform Stretch(GameObject go, float padding = 0)
        {
            var rt = Rect(go);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color)
        {
            var go = Create(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Label(
            string name, Transform parent, string text, float size,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var go = Create(name, parent);
            var label = go.AddComponent<TextMeshProUGUI>();

            var font = JapaneseFont.Get();
            if (font != null) label.font = font;

            label.text = text;
            label.fontSize = size;
            label.color = UiTheme.Text;
            label.alignment = align;
            label.raycastTarget = false;

            // 名前は長さがまちまち。溢れたら縮めて、行は折らない。
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            return label;
        }

        public static Button Button(
            string name, Transform parent, string text, float size, Action onClick)
        {
            var image = Panel(name, parent, UiTheme.Button);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = colors;

            var label = Label(name + "Label", image.transform, text, size,
                              TextAlignmentOptions.Center);
            Stretch(label.gameObject, 4);

            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>横並び。</summary>
        public static HorizontalLayoutGroup Row(
            GameObject go, float spacing = 6, int padding = 0)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            return layout;
        }

        /// <summary>縦並び。</summary>
        public static VerticalLayoutGroup Column(
            GameObject go, float spacing = 6, int padding = 0)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return layout;
        }

        public static LayoutElement Size(GameObject go, float width = -1, float height = -1)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0) { element.preferredWidth = width; element.minWidth = width; }
            if (height >= 0) { element.preferredHeight = height; element.minHeight = height; }
            return element;
        }

        public static LayoutElement Flexible(GameObject go, float weight = 1f)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = weight;
            return element;
        }

        /// <summary>
        /// 縦スクロールする領域。中身の入れ物（content）を返す。
        /// シフト表もイベントログも行数が読めないので、素の Rect には置かない。
        /// </summary>
        public static RectTransform ScrollView(
            string name, Transform parent, out ScrollRect scroll)
        {
            var root = Create(name, parent);
            scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewport = Create("Viewport", root.transform);
            Stretch(viewport);
            viewport.AddComponent<RectMask2D>();
            scroll.viewport = Rect(viewport);

            var content = Create("Content", viewport.transform);
            var contentRect = Rect(content);
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.offsetMin = new Vector2(0, 0);
            contentRect.offsetMax = new Vector2(0, 0);

            Column(content, 4);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;
            return contentRect;
        }

        /// <summary>子をすべて捨てる。作り直しのたびに呼ぶ。</summary>
        public static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        /// <summary>疲労バー。限界に近いほど赤くなる。</summary>
        public static Image Bar(string name, Transform parent, out Image fill)
        {
            var back = Panel(name, parent, UiTheme.PanelDark);

            var fillGo = Create("Fill", back.transform);
            fill = fillGo.AddComponent<Image>();
            fill.color = UiTheme.Ok;

            var rt = Rect(fillGo);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            return back;
        }

        /// <summary>バーの割合と色を更新する。1 を超えたぶんは振り切れた表示にする。</summary>
        public static void SetBar(Image fill, double ratio)
        {
            float clamped = Mathf.Clamp01((float)ratio);

            var rt = fill.rectTransform;
            rt.anchorMax = new Vector2(clamped, 1);

            fill.color = ratio >= 1.0 ? UiTheme.Danger
                       : ratio >= 0.75 ? UiTheme.Warn
                       : UiTheme.Ok;
        }

        public static string Yen(long value) => value.ToString("N0") + " 円";
    }
}
