using System;
using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】見た目のテーマ（Visual Vertical Slice 用）。盤面の要素ごとの Prefab・キャラの4方向・カード枠をまとめる。
    /// 中身は Editor の ArtSetup が Art/ の画像から自動で作る（手で編集しなくてよい）。
    /// StageData.visualTheme に入れたステージだけ、この見た目で描く（入れないステージは今までのグレーBOX）。
    /// ルールには一切関係しない（盤面データ → 見た目、の一方通行）。
    /// </summary>
    [CreateAssetMenu(menuName = "CopyCommand/Visual Theme", fileName = "VisualTheme")]
    public class VisualTheme : ScriptableObject
    {
        [Serializable]
        public class Element
        {
            public string key;
            public GameObject prefab;
        }

        [Serializable]
        public class UnitSprites
        {
            public string kind;
            public Sprite down, up, left, right;

            public Sprite For(Direction d)
            {
                switch (d)
                {
                    case Direction.Up: return up;
                    case Direction.Left: return left;
                    case Direction.Right: return right;
                    default: return down;
                }
            }
        }

        [Serializable]
        public class UiTexture
        {
            public string name;
            public Texture2D texture;
            [Tooltip("9 スライスのふち（px）。0 なら伸ばすだけ")]
            public int border;
        }

        [Tooltip("盤面の要素（floor_a / floor_b / wall / wall_shadow / spike / warp_a / warp_b / rune / fire / danger / fire_next / bomb / select / shadow / frame_board / brazier / vfx_* / background）")]
        public List<Element> elements = new List<Element>();
        public List<UnitSprites> units = new List<UnitSprites>();
        [Range(0f, 1f)] public float decoChance = 0.18f;
        [Tooltip("ヒットフラッシュ用（白く光る）")]
        public Material flashMaterial;
        [Header("UI（ui_*.png。カード枠・カードの絵・パネル・ボタン・顔アイコン・兵種バッジ）")]
        public List<UiTexture> ui = new List<UiTexture>();

        Dictionary<string, UiTexture> uiMap;

        /// <summary>UI の絵（名前は ui_ を除いたもの。例：card_attack・cardart_slash・portrait_player）。無ければ null。</summary>
        public Texture2D Ui(string name) => UiEntry(name)?.texture;

        public int UiBorder(string name) => UiEntry(name)?.border ?? 0;

        UiTexture UiEntry(string name)
        {
            if (uiMap == null || uiMap.Count != ui.Count)
            {
                uiMap = new Dictionary<string, UiTexture>();
                foreach (var u in ui) if (u != null && u.texture != null) uiMap[u.name] = u;
            }
            return uiMap.TryGetValue(name, out var e) ? e : null;
        }

        public GameObject Get(string key)
        {
            foreach (var e in elements)
                if (e.key == key) return e.prefab;
            return null;
        }

        /// <summary>【RC11】名前で4方向（例 statue＝眠っている写し身の石の像）。無ければ null。</summary>
        public UnitSprites UnitForKey(string kind)
        {
            foreach (var u in units) if (u.kind == kind) return u;
            return null;
        }

        /// <summary>兵種の4方向。無ければ normal、それも無ければ null（グレーBOX のまま）。</summary>
        public UnitSprites UnitFor(UnitModel model)
        {
            string kind = model.IsPlayer ? "player" : KindKey(model.Kind);
            UnitSprites normal = null;
            foreach (var u in units)
            {
                if (u.kind == kind) return u;
                if (u.kind == "normal") normal = u;
            }
            return model.IsPlayer ? null : normal;
        }

        public static string KindKey(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Swordsman: return "swordsman";
                case EnemyKind.Spearman: return "spearman";
                case EnemyKind.Bomber: return "cannon"; // 【RC10】砲兵は大砲の絵（前は爆弾の絵）
                case EnemyKind.Archer: return "archer";
                case EnemyKind.MirrorSoldier: return "mirror";
                // 【RC10・ChatGPT「敵の見た目の使い回し」】兵種ごとの形（tools/art_gen/enemies_rc10.py）：記号を読まなくても見分けられる
                case EnemyKind.Shield: return "shield";
                case EnemyKind.Chaser: return "chaser";
                case EnemyKind.Mender: return "healer";
                case EnemyKind.HealClone: return "clone";
                case EnemyKind.MirrorLord: return "mirrorlord";
                case EnemyKind.ExecKnight: return "execknight";
                case EnemyKind.BeastKing: return "beastking";
                case EnemyKind.CannonKing: return "cannonking";
                case EnemyKind.Knight:
                case EnemyKind.KnightV2:
                case EnemyKind.KnightV3: return "knight";
                default: return "normal";
            }
        }

        /// <summary>要素を1つ置く（Prefab が無ければ null）。</summary>
        public GameObject Spawn(string key, Transform parent, Vector3 position)
        {
            var prefab = Get(key);
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent);
            go.transform.position = position;
            return go;
        }
    }

    /// <summary>【検証中】Sorting Layer の名前（ArtSetup が TagManager に作る。ART_STYLE_GUIDE 5章）。</summary>
    public static class SortLayers
    {
        public const string Background = "Background";
        public const string Floor = "Floor";
        public const string Hazard = "Hazard";
        public const string Telegraph = "Telegraph";
        public const string Units = "Units";
        public const string Vfx = "VFX";
        public static readonly string[] All = { Background, Floor, Hazard, Telegraph, Units, Vfx };
    }
}
