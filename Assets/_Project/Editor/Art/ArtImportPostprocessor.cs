using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証中】Assets/_Project/Art/ に置いた PNG の取り込み設定を自動で揃える（docs/ART_STYLE_GUIDE.md 8章）。
    /// Sprite・Pixels Per Unit 128（1マス＝1ユニット）・中心が基準・ミップマップなし・無圧縮・Bilinear。
    /// 「名前_sheet4x1.png」のような名前なら、列×行に自動で切り分ける（切った絵の名前は 名前_0, 名前_1 …、左上から右へ）。
    /// </summary>
    public class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/_Project/Art/";
        public const int PixelsPerUnit = 128;
        static readonly Regex Sheet = new Regex(@"^(.*)_sheet(\d+)x(\d+)$");
        /// <summary>「名前_9s24」＝9スライス（ふち 24px は伸ばさない）。盤面の枠・UI のパネル・カード枠。</summary>
        public static readonly Regex NineSlice = new Regex(@"_9s(\d+)$");

        // 設定を変えたら番号を上げる（Unity が Art の画像を取り込み直す）
        public override uint GetVersion() => 6; // 【RC3】盤面とカードの絵にミップマップ（6：少しくっきり寄りに -0.6）

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(ArtRoot)) return;
            var ti = (TextureImporter)assetImporter;
            Apply(ti, assetPath);
        }

        /// <summary>取り込み設定を決める（ArtSetup の確認でも同じものを使う）。</summary>
        public static void Apply(TextureImporter ti, string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            // 【RC3・本人「画質が少し悪い」】盤面の絵（128px）は 720p で 58px ほどに縮めて描く → ミップマップなしだと縁がざらつく。
            //   縮めて描く物（ユニット・床・仕掛け・カードの絵・顔）だけミップマップ＋トライリニア。枠（9スライス）などは今のまま
            bool mip = WantsMip(name);
            ti.mipmapEnabled = mip;
            if (mip) ti.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            // 【RC3】ミップマップだけだと 720p で顔が少しぼやけた（RC2 と並べて確認）→ 1つ細かい段を少し早めに使う（縁のざらつきは減ったまま）
            ti.mipMapBias = mip ? MipBias : 0f;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = mip ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 2048;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
            var nine = NineSlice.Match(name);
            ti.spriteBorder = nine.Success ? Vector4.one * int.Parse(nine.Groups[1].Value) : Vector4.zero;

            var m = Sheet.Match(name);
            if (!m.Success)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                return;
            }
            int cols = int.Parse(m.Groups[2].Value), rows = int.Parse(m.Groups[3].Value);
            var size = PngSize(path);
            if (cols <= 0 || rows <= 0 || size.x <= 0) return;
            int w = size.x / cols, h = size.y / rows;
            var metas = new List<SpriteMetaData>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    metas.Add(new SpriteMetaData
                    {
                        name = $"{m.Groups[1].Value}_{r * cols + c}",
                        rect = new Rect(c * w, size.y - (r + 1) * h, w, h), // Unity の座標は左下が原点
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                    });
            ti.spriteImportMode = SpriteImportMode.Multiple;
#pragma warning disable 618 // 2D Sprite パッケージ無しで切り分ける手段（古い API だが Unity 6 でも動く）
            ti.spritesheet = metas.ToArray();
#pragma warning restore 618
        }

        /// <summary>取り込み設定が決まりどおりか（違えば ArtSetup が取り込み直す）。</summary>
        /// <summary>【RC3】ミップマップの寄せ（負＝くっきり）。</summary>
        public const float MipBias = -0.6f;

        public static bool IsConfigured(TextureImporter ti) =>
            ti.textureType == TextureImporterType.Sprite && Mathf.Approximately(ti.spritePixelsPerUnit, PixelsPerUnit) && ti.mipmapEnabled == WantsMip(Path.GetFileNameWithoutExtension(ti.assetPath)) && (!ti.mipmapEnabled || Mathf.Approximately(ti.mipMapBias, MipBias));

        /// <summary>【RC3】縮めて描く絵（ミップマップを使う）：ユニット・床・仕掛け・カードの絵・顔・炎などの動き。</summary>
        public static bool WantsMip(string name) =>
            !NineSlice.IsMatch(name) && (name.StartsWith("unit_") || name.StartsWith("tile_") || name.StartsWith("hz_") || name.StartsWith("ui_hz_") ||
                                         name.StartsWith("ui_cardart_") || name.StartsWith("ui_portrait_") || name.StartsWith("loop_") || name.StartsWith("vfx_") || name.StartsWith("boss_"));

        /// <summary>PNG の幅・高さをファイルの先頭から読む（取り込み前でも分かる）。</summary>
        static Vector2Int PngSize(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var b = new byte[24];
                    if (f.Read(b, 0, 24) < 24) return Vector2Int.zero;
                    int w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    int h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    return new Vector2Int(w, h);
                }
            }
            catch (IOException)
            {
                return Vector2Int.zero;
            }
        }
    }
}
