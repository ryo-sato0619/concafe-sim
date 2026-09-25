using TMPro;
using UnityEngine;

namespace Concafe.Game
{
    /// <summary>
    /// 日本語が出せる TMP フォントを供給する。
    ///
    /// TMP の既定フォント（LiberationSans）に日本語グリフは無く、そのままだと
    /// 全文字が豆腐になる。次の順で解決する。
    ///
    ///   1. `Resources/Fonts/NotoSansJP SDF`（同梱フォント）
    ///   2. OS にインストールされているフォント
    ///
    /// 1 を先に見るのは、WebGL では 2 が使えないため。開発機では 2 で足りるが、
    /// **配布ビルドでは同梱フォントが無いと日本語が出ない。**
    ///
    /// どちらも見つからなくても落とさない。フォントが無いこと自体は
    /// 進行を止める理由にならない（不変条件 14 と同じ姿勢）。
    /// </summary>
    public static class JapaneseFont
    {
        const string BundledPath = "Fonts/NotoSansJP SDF";

        static readonly string[] Families =
        {
            "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic",
            "Hiragino Sans", "Hiragino Kaku Gothic ProN",
            "Noto Sans JP", "Noto Sans CJK JP", "IPAGothic",
        };

        static TMP_FontAsset cached;
        static bool resolved;

        /// <summary>見つからなければ null。呼び出し側は null を許すこと。</summary>
        public static TMP_FontAsset Get()
        {
            if (resolved) return cached;
            resolved = true;

            cached = Resources.Load<TMP_FontAsset>(BundledPath);
            if (cached != null) return cached;

            // フォントの解決で画面ごと落とさない。TMP の必須リソースが
            // 入っていないと TMP_Settings が無く、内部で例外が飛ぶことがある。
            // 文字が出ないのは困るが、そこで進行が止まる理由にはならない
            // （不変条件 14 と同じ姿勢）。
            try
            {
                cached = FromOperatingSystem();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("OS のフォントからフォントアセットを作れない: " + e.Message);
                cached = null;
            }

            if (cached == null)
                Debug.LogWarning(
                    "日本語フォントが見つからない。Resources/Fonts に NotoSansJP SDF を置くこと。" +
                    "配布ビルドでは OS のフォントに頼れない");

            return cached;
        }

        static readonly string[] Styles = { "Regular", "Medium", "Book", "" };

        static TMP_FontAsset FromOperatingSystem()
        {
            string[] installed = Font.GetOSInstalledFontNames();
            if (installed == null || installed.Length == 0) return null;

            foreach (string family in Families)
            {
                bool present = false;
                foreach (string name in installed)
                    if (name != null &&
                        name.StartsWith(family, System.StringComparison.OrdinalIgnoreCase))
                    { present = true; break; }

                if (!present) continue;

                foreach (string style in Styles)
                {
                    // ファミリ名と字体名から作る。
                    //
                    // Font.CreateDynamicFontFromOSFont() 経由では作れない。
                    // あれが返す Font はフォントの実体を抱えておらず、TMP が
                    // 「Unable to load font face」で弾く。TMP に OS のフォントを
                    // 直接開かせる必要がある。
                    var asset = TMP_FontAsset.CreateFontAsset(family, style, 90);
                    if (asset == null) continue;

                    // 動的に字形を足せるようにしておく。源氏名は 480 週ぶん
                    // 何が来るか分からないので、固定のアトラスでは足りない。
                    asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                    asset.name = $"{family} {style}".Trim();
                    return asset;
                }
            }
            return null;
        }

        /// <summary>テストや差し替えのために解決をやり直す。</summary>
        public static void Reset()
        {
            cached = null;
            resolved = false;
        }
    }
}
