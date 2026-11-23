using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証中】Art/ の画像から、Unity 側の物を全部自動で作る（本人の手作業をなくすため）。
    ///   1. Sorting Layer（Background / Floor / Hazard / Telegraph / Units / VFX）を TagManager に作る
    ///   2. 画像の取り込み設定を確認（ArtImportPostprocessor と違えば取り込み直す）
    ///   3. 連番（vfx_名前_00… / loop_名前_00…）→ AnimationClip ＋ AnimatorController
    ///   4. 盤面の要素・エフェクトごとに Prefab（SpriteRenderer＋層＋回転・脈動・アニメ）
    ///   5. テーマ（Data/Themes/RuinsTheme.asset）に Prefab・キャラ4方向・カード枠を登録
    /// 画像を差し替えたら、もう一度これを実行するだけ（ProjectSetup の最初にも自動で呼ばれる）。
    /// 置き場所：Art/Generated（Python の自動生成）→ Art/Imported（ChatGPT などの本番候補）。同じ名前なら Imported が勝つ。
    /// </summary>
    public static class ArtSetup
    {
        const string ArtRoot = "Assets/_Project/Art";
        static readonly string[] SourceFolders = { ArtRoot + "/Generated", ArtRoot + "/Imported" };
        const string PrefabDir = ArtRoot + "/Prefabs";
        const string AnimDir = ArtRoot + "/Animations";
        const string MatDir = ArtRoot + "/Materials";
        public const string ThemePath = "Assets/_Project/Data/Themes/RuinsTheme.asset";
        static readonly Regex Numbered = new Regex(@"^(.*)_(\d+)$");

        /// <summary>要素ごとの層と追加の動き。表に無い tile_ は Hazard 層に置く（新しいギミックの画像を足すだけで Prefab になる）。</summary>
        class Rule
        {
            public string Layer = SortLayers.Hazard;
            public int Order = 1;
            public float Spin;
            public bool Pulse;
            public float Fps = 16f;
        }

        static readonly Dictionary<string, Rule> Rules = new Dictionary<string, Rule>
        {
            { "floor_a", new Rule { Layer = SortLayers.Floor, Order = 0 } },
            { "floor_b", new Rule { Layer = SortLayers.Floor, Order = 0 } },
            { "frame_board", new Rule { Layer = SortLayers.Floor, Order = 3 } },
            { "brazier", new Rule { Layer = SortLayers.Hazard, Order = 6, Fps = 8f } },
            { "wall_shadow", new Rule { Layer = SortLayers.Floor, Order = 2 } },
            { "wall", new Rule { Order = 0 } },
            { "warp_a", new Rule { Pulse = true } },
            { "warp_b", new Rule { Pulse = true } },
            { "rune", new Rule { Pulse = true } },
            { "fire", new Rule { Order = 2, Fps = 10f } },
            { "danger", new Rule { Layer = SortLayers.Telegraph, Order = 0, Pulse = true } },
            { "fire_next", new Rule { Layer = SortLayers.Telegraph, Order = 0, Pulse = true } },
            { "select", new Rule { Layer = SortLayers.Telegraph, Order = 0 } },
            { "bomb", new Rule { Layer = SortLayers.Telegraph, Order = 1, Pulse = true } },
            { "shadow", new Rule { Layer = SortLayers.Units, Order = 0 } },
            { "background", new Rule { Layer = SortLayers.Background, Order = 0 } },
            { "vfx_slash", new Rule { Layer = SortLayers.Vfx, Order = 0, Fps = 22f } },
            { "vfx_hit", new Rule { Layer = SortLayers.Vfx, Order = 1, Fps = 20f } },
            { "vfx_explosion", new Rule { Layer = SortLayers.Vfx, Order = 0, Fps = 14f } },
            { "vfx_death", new Rule { Layer = SortLayers.Vfx, Order = 0, Fps = 12f } },
            { "vfx_dust", new Rule { Layer = SortLayers.Units, Order = 1, Fps = 14f } },
        };

        /// <summary>テーマの種類：名前 → 画像のサブフォルダ（空＝標準）。サブフォルダに無い画像は標準の物を使う。</summary>
        static readonly (string theme, string folder, string label)[] Themes =
        {
            ("Ruins", "", "古代遺跡（標準）"),
            ("Crystal", "crystal", "紫晶神殿（ボス）"),
        };

        public static string ThemePathOf(string theme) => $"Assets/_Project/Data/Themes/{theme}Theme.asset";

        [MenuItem("CopyCommand/Art/Build All（画像 → Sprite・Prefab・Animation・テーマ）")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(ArtRoot)) { Debug.Log("[ArtSetup] Art フォルダが無いので何もしない"); return; }
            EnsureSortingLayers();
            ReimportIfNeeded();
            foreach (var dir in new[] { PrefabDir, AnimDir, MatDir, Path.GetDirectoryName(ThemePath).Replace('\\', '/') })
                EnsureFolder(dir);
            var flash = FlashMaterial();
            var baseSprites = LoadSprites("");
            foreach (var (themeName, folder, label) in Themes)
            {
                var sprites = new Dictionary<string, Sprite>(baseSprites);
                if (folder.Length > 0)
                    foreach (var kv in LoadSprites(folder)) sprites[kv.Key] = kv.Value; // テーマの画像が標準より優先
                BuildTheme(ThemePathOf(themeName), folder, sprites, flash, label);
            }
            AssetDatabase.SaveAssets();
        }

        static void BuildTheme(string themePath, string folder, Dictionary<string, Sprite> sprites, Material flash, string label)
        {
            string prefabDir = folder.Length > 0 ? $"{PrefabDir}/{folder}" : PrefabDir;
            string animDir = folder.Length > 0 ? $"{AnimDir}/{folder}" : AnimDir;
            EnsureFolder(prefabDir);
            EnsureFolder(animDir);
            var theme = AssetDatabase.LoadAssetAtPath<VisualTheme>(themePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<VisualTheme>();
                AssetDatabase.CreateAsset(theme, themePath);
            }
            theme.elements.Clear();
            theme.units.Clear();
            theme.ui.Clear();
            theme.flashMaterial = flash;

            // 画像の名前から要素をまとめる：tile_X → X（番号つきは種類違い）、loop_X_00 → X（ループ）、vfx_X_00 → vfx_X（1回）、
            // frame_X_9sNN → frame_X（9スライスの枠）、bg_X → background
            var groups = new SortedDictionary<string, (List<Sprite> frames, string kind)>();
            foreach (var kv in sprites.OrderBy(k => k.Key))
            {
                string name = kv.Key, key, kind;
                if (name.StartsWith("tile_")) { key = StripNumber(name.Substring(5)); kind = "tile"; }
                else if (name.StartsWith("loop_")) { key = StripNumber(name.Substring(5)); kind = "loop"; }
                else if (name.StartsWith("vfx_")) { key = "vfx_" + StripNumber(name.Substring(4)); kind = "vfx"; }
                else if (name.StartsWith("frame_")) { key = ArtImportPostprocessor.NineSlice.Replace(name, ""); kind = "frame"; }
                else if (name.StartsWith("bg_")) { key = "background"; kind = "tile"; }
                else continue;
                if (!groups.TryGetValue(key, out var g)) groups[key] = g = (new List<Sprite>(), kind);
                if (key == "background" && g.frames.Count > 0) continue; // 背景は1枚目だけ
                g.frames.Add(kv.Value);
            }
            int made = 0;
            foreach (var kv in groups)
            {
                var frames = kv.Value.frames;
                // 床は「何も無い石」を多めに（模様・ひびは時々）
                if (kv.Key.StartsWith("floor_") && frames.Count > 1) frames = new List<Sprite> { frames[0], frames[0], frames[0] }.Concat(frames.Skip(1)).ToList();
                var prefab = MakePrefab(kv.Key, frames, kv.Value.kind, prefabDir, animDir);
                if (prefab == null) continue;
                theme.elements.Add(new VisualTheme.Element { key = kv.Key, prefab = prefab });
                made++;
            }

            // キャラ：unit_兵種_down/up/left/right（または unit_兵種_sheet4x1 を切った unit_兵種_0〜3：下・上・左・右の順）
            var kinds = sprites.Keys.Where(n => n.StartsWith("unit_")).Select(n => n.Substring(5)).Select(n => n.Substring(0, Mathf.Max(0, n.LastIndexOf('_')))).Where(n => n.Length > 0).Distinct();
            foreach (var kind in kinds)
            {
                Sprite Pick(string dir, int index) => sprites.TryGetValue($"unit_{kind}_{dir}", out var sp) ? sp : sprites.TryGetValue($"unit_{kind}_{index}", out sp) ? sp : null;
                var set = new VisualTheme.UnitSprites { kind = kind, down = Pick("down", 0), up = Pick("up", 1), left = Pick("left", 2), right = Pick("right", 3) };
                if (set.down == null) continue;
                set.up = set.up ?? set.down;
                set.left = set.left ?? set.down;
                set.right = set.right ?? set.left;
                theme.units.Add(set);
            }

            // UI：ui_名前（_9sNN なら 9 スライスのふち）
            foreach (var kv in sprites.Where(k => k.Key.StartsWith("ui_")).OrderBy(k => k.Key))
            {
                var m = ArtImportPostprocessor.NineSlice.Match(kv.Key);
                theme.ui.Add(new VisualTheme.UiTexture
                {
                    name = ArtImportPostprocessor.NineSlice.Replace(kv.Key.Substring(3), ""),
                    texture = kv.Value.texture,
                    border = m.Success ? int.Parse(m.Groups[1].Value) : 0,
                });
            }
            EditorUtility.SetDirty(theme);
            Debug.Log($"[ArtSetup] {label}：画像 {sprites.Count}・Prefab {made}・キャラ {theme.units.Count} 種・UI {theme.ui.Count} → {themePath}");
        }

        static string StripNumber(string s)
        {
            var m = Numbered.Match(s);
            return m.Success ? m.Groups[1].Value : s;
        }

        /// <summary>名前 → Sprite。sub が空なら Generated・Imported の直下だけ、sub があれば Generated/sub・Imported/sub。同じ名前なら Imported が勝つ。</summary>
        static Dictionary<string, Sprite> LoadSprites(string sub)
        {
            var result = new Dictionary<string, Sprite>();
            foreach (var root in SourceFolders)
            {
                string folder = sub.Length > 0 ? $"{root}/{sub}" : root;
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue; // サブフォルダ（別のテーマ）は含めない
                    foreach (var sp in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                        result[sp.name] = sp;
                }
            }
            return result;
        }

        static void ReimportIfNeeded()
        {
            foreach (var folder in SourceFolders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetImporter.GetAtPath(path) is TextureImporter ti && !ArtImportPostprocessor.IsConfigured(ti))
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        static GameObject MakePrefab(string key, List<Sprite> frames, string kind, string prefabDir, string animDir)
        {
            if (frames.Count == 0) return null;
            if (!Rules.TryGetValue(key, out var rule)) rule = kind == "vfx" ? new Rule { Layer = SortLayers.Vfx, Order = 0 } : new Rule();
            var go = new GameObject(key);
            try
            {
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = frames[0];
                sr.sortingLayerName = rule.Layer;
                sr.sortingOrder = rule.Order;
                bool animated = kind == "loop" || kind == "vfx";
                if (kind == "tile" && frames.Count > 1)
                    go.AddComponent<SpriteVariant>().sprites = frames.ToArray();
                if (kind == "frame")
                {
                    // 盤面の枠：9スライスのふちを繰り返して貼る（大きさは ThemedBoard が盤面に合わせる）
                    sr.drawMode = SpriteDrawMode.Tiled;
                    sr.tileMode = SpriteTileMode.Continuous;
                }
                if (animated && frames.Count > 1)
                {
                    var ctrl = MakeAnimation(key, frames, rule.Fps, kind == "loop", animDir);
                    go.AddComponent<Animator>().runtimeAnimatorController = ctrl;
                }
                if (kind == "vfx")
                    go.AddComponent<OneShotVfx>().lifetime = frames.Count / rule.Fps;
                if (rule.Spin != 0f) go.AddComponent<Spin>().degreesPerSecond = rule.Spin;
                if (rule.Pulse) go.AddComponent<Pulse>();
                return PrefabUtility.SaveAsPrefabAsset(go, $"{prefabDir}/{key}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>連番 → AnimationClip（Sprite を切り替える）＋ それだけを再生する AnimatorController。既にあれば中身だけ更新（GUID を変えない）。</summary>
        static RuntimeAnimatorController MakeAnimation(string key, List<Sprite> frames, float fps, bool loop, string animDir)
        {
            string clipPath = $"{animDir}/{key}.anim", ctrlPath = $"{animDir}/{key}.controller";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            bool isNew = clip == null;
            if (isNew) clip = new AnimationClip();
            clip.frameRate = fps;
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            var keys = frames.Select((s, i) => new ObjectReferenceKeyframe { time = i / fps, value = s }).ToList();
            // 最後の絵も 1 コマ分見せる
            keys.Add(new ObjectReferenceKeyframe { time = frames.Count / fps, value = loop ? frames[0] : frames[frames.Count - 1] });
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (isNew) AssetDatabase.CreateAsset(clip, clipPath);
            else EditorUtility.SetDirty(clip);

            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip(ctrlPath, clip);
            return ctrl;
        }

        static Material FlashMaterial()
        {
            string path = $"{MatDir}/SpriteFlash.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("CopyCommand/SpriteFlash");
            if (shader == null) { Debug.LogWarning("[ArtSetup] SpriteFlash シェーダーが見つからない"); return null; }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Sorting Layer を TagManager に足す（既にあれば何もしない）。Default の後ろに順番どおり並べる。</summary>
        public static void EnsureSortingLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tm = new SerializedObject(assets[0]);
            var layers = tm.FindProperty("m_SortingLayers");
            var existing = new HashSet<string>();
            for (int i = 0; i < layers.arraySize; i++) existing.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            bool changed = false;
            foreach (var name in SortLayers.All)
            {
                if (existing.Contains(name)) continue;
                layers.InsertArrayElementAtIndex(layers.arraySize);
                var el = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                el.FindPropertyRelative("name").stringValue = name;
                el.FindPropertyRelative("uniqueID").intValue = (name.GetHashCode() & 0x7fffffff) | 1;
                changed = true;
            }
            if (changed) tm.ApplyModifiedProperties();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
