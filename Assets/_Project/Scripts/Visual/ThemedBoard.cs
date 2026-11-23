using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】テーマつきの盤面を層ごとに組み立てる（ART_STYLE_GUIDE 5章）。
    /// 背景 → 床（＋平たい装飾・壁の影）→ 壁・ワープ（Hazard）の順。トゲは HazardTile、予告は GridManager.ShowMarks が置く。
    /// すべて盤面データ（BoardModel）から置くので、絵を差し替えてもルールは変わらない。
    /// </summary>
    public static class ThemedBoard
    {
        public static void Build(GridManager grid, VisualTheme theme, Transform root)
        {
            var board = grid.Model.Board;
            var bg = theme.Spawn("background", root, Vector3.zero);
            if (bg != null) bg.name = "Background";

            // 盤面の縁（盤面の形を背景から浮かせる）
            var rim = GreyboxSprites.CreateShape(root, "BoardRim", GreyboxSprites.Square, new Color(0.02f, 0.025f, 0.05f), Vector3.zero, 1f, -10);
            rim.transform.localScale = new Vector3(grid.Width + 0.3f, grid.Height + 0.3f, 1f);
            rim.sortingLayerName = SortLayers.Floor;
            // 石の枠（9スライスのふちを繰り返す。盤面の外側に 0.5 マス）と、四隅の燭台（盤面の外。当たり判定なし）
            var frame = theme.Spawn("frame_board", root, Vector3.zero);
            if (frame != null)
            {
                frame.name = "BoardFrame";
                frame.GetComponent<SpriteRenderer>().size = new Vector2(grid.Width + 1f, grid.Height + 1f);
            }
            float hx = grid.Width * 0.5f + 0.5f, hy = grid.Height * 0.5f + 0.5f;
            foreach (var corner in new[] { new Vector2(-hx, hy), new Vector2(hx, hy), new Vector2(-hx, -hy), new Vector2(hx, -hy) })
            {
                var b = theme.Spawn("brazier", root, new Vector3(corner.x, corner.y + 0.28f, 0f));
                if (b != null) b.name = "Brazier";
            }

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    var pos = grid.GridToWorld(cell);
                    if (board.IsBaseWall(cell))
                    {
                        var w = Place(theme, "wall", root, pos, cell);
                        // 【RC3・本人「画質」・盤面の作り直し】壁・柱の上面が床と同じ青灰色で「明るい床」に見えることがあった → 少し暖かい石の色に
                        var sr = w != null ? w.GetComponentInChildren<SpriteRenderer>() : null;
                        if (sr != null) sr.color = theme != null && theme.name.Contains("Crystal") ? CrystalWallTint : WallTint; // 【RC11・最後の見直し（初見）「ボスの部屋の柱が紫（固定）に近い」】紫晶の柱も紫を抑える
                        continue;
                    }
                    var fl = Place(theme, (x + y) % 2 == 0 ? "floor_a" : "floor_b", root, pos, cell);
                    // 【RC11・UI/UX「ボスの部屋は床全体が紫で、紫（床に固定）の攻撃が溶ける」】紫晶の床は紫を抑える（緑を少し残して灰色寄りに）
                    var fsr = fl != null && theme != null && theme.name.Contains("Crystal") ? fl.GetComponentInChildren<SpriteRenderer>() : null;
                    if (fsr != null) fsr.color = CrystalFloorTint;
                    // 装飾は平たい物だけ（当たり判定なし）。ギミックのマスには置かない
                    bool gimmick = board.HasSpike(cell) || board.TryWarp(cell, out _);
                    if (!gimmick && Hash01(cell) < theme.decoChance) Place(theme, "deco", root, pos, cell + new Vector2Int(17, 5));
                    var above = cell + Vector2Int.up;
                    if (board.IsInside(above) && board.IsBaseWall(above)) Place(theme, "wall_shadow", root, pos, cell);
                }
            }

            int pair = 0;
            var done = new HashSet<Vector2Int>();
            foreach (var kv in board.Warps)
            {
                if (done.Contains(kv.Key)) continue;
                done.Add(kv.Key);
                done.Add(kv.Value);
                foreach (var c in new[] { kv.Key, kv.Value })
                    Place(theme, pair % 2 == 0 ? "warp_a" : "warp_b", root, grid.GridToWorld(c), c);
                pair++;
            }
        }

        /// <summary>【RC11】紫晶（ボス）の床の色：紫を抑える。【通常リリース版・画面の直し 6「ボスの床の紫と固定の攻撃の紫（斜線）がかすむ」】灰色がかった青へ（赤と青を下げる：平均 (74,66,106) → (55,62,78)）。壁・燭台の紫は残す。</summary>
        public static readonly Color CrystalFloorTint = new Color(0.74f, 0.94f, 0.74f);

        /// <summary>【RC11】紫晶（ボス）の壁・柱の色：紫を抑えて灰色寄りに（紫＝床に固定の攻撃とぶつからない）。</summary>
        public static readonly Color CrystalWallTint = new Color(0.8f, 0.86f, 0.8f);

        /// <summary>【RC3】壁・柱の色（床の青灰色と分ける）。</summary>
        public static readonly Color WallTint = new Color(1.0f, 0.88f, 0.74f);

        static GameObject Place(VisualTheme theme, string key, Transform root, Vector3 pos, Vector2Int cell)
        {
            var go = theme.Spawn(key, root, pos);
            if (go == null) return null;
            go.name = $"{key} {cell.x},{cell.y}";
            var v = go.GetComponent<SpriteVariant>();
            if (v != null) v.Pick(cell);
            return go;
        }

        static float Hash01(Vector2Int c)
        {
            int h = (c.x * 92837111) ^ (c.y * 689287499) ^ 0x5bd1e995;
            h ^= h >> 13;
            return (Mathf.Abs(h) % 1000) / 1000f;
        }

        /// <summary>予告の種類（BattleUI の key）→ テーマの要素と濃さ。テーマに無ければ null（グレーBOX のまま）。</summary>
        public static string MarkElement(string key, out float alpha)
        {
            alpha = 1f;
            switch (key)
            {
                case "danger":
                case "arrow": return "danger";
                case "arrowDim": alpha = 0.35f; return "danger"; // 【RC8】マウスを乗せた敵以外の赤
                case "arrowGone": alpha = 0.28f; return "danger"; // 【RC8】倒すと消える赤（前の場所）
                case "telegraphDim": return null;
                case "bomb": return "bomb";
                case "telegraph": return null; // 【Puzzle v2】ボス・仕掛けの予告は紫の四角（赤の兵の攻撃と分ける）
                case "bombNext": alpha = 0.45f; return "bomb";
                case "wave":
                case "fireNext": return "fire_next";
                case "step": return "select";
                case "fire": return "fire";
                case "rune": return "rune";
                case "runeNext": alpha = 0.35f; return "rune";
                default: return null;
            }
        }

        /// <summary>背景を画面いっぱいに広げる。</summary>
        public static void FitBackground(Transform root, Camera cam)
        {
            if (root == null || cam == null) return;
            var bg = root.Find("Background");
            if (bg == null) return;
            var sr = bg.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            float s = Mathf.Max(w / size.x, h / size.y) * 1.02f;
            bg.localScale = new Vector3(s, s, 1f);
            bg.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        }

        /// <summary>エフェクトを1つ出す（angle は右向きからの回転）。</summary>
        public static void Vfx(VisualTheme theme, string key, Vector3 pos, float angle = 0f, float scale = 1f)
        {
            if (theme == null) return;
            var go = theme.Spawn(key, null, pos);
            if (go == null) return;
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }

        public static float Angle(Vector2Int dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
    }
}
