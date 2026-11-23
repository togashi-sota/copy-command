using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 盤面の見た目と、ルールモデル（BattleModel）の入れ物。マス(x, y) は左下が (0, 0)、右が x+、上が y+。
    /// ルール上の判定（壁か・ユニットがいるか・トゲか）は BattleModel / BoardModel に任せ、ここは
    /// 「マス⇔画面座標の変換」「床・壁・トゲ・ユニットの見た目」「演出係（BattlePresenter）」を担当する。
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public static readonly Color FloorColor = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color WallColor = new Color(0.16f, 0.16f, 0.19f);
        public static readonly Color WallTopColor = new Color(0.27f, 0.27f, 0.31f);
        public static readonly Color FloorColorAlt = new Color(0.5f, 0.5f, 0.52f);
        public static readonly Color WarpColor = new Color(0.4f, 0.5f, 1f, 0.85f); // 【RC5・レビュー】水色（まね）と分ける：青（移動の札と同じ系統）
        public static readonly Color WarpColor2 = new Color(1f, 0.45f, 0.9f, 0.85f);

        /// <summary>ルール上の状態（盤面＋ユニット）。</summary>
        public BattleModel Model { get; private set; }
        /// <summary>出来事を見た目に反映する演出係。</summary>
        public BattlePresenter Presenter { get; private set; }
        /// <summary>【検証中】見た目のテーマ（null ならグレーBOX）。ルールには関係しない。</summary>
        public VisualTheme Theme { get; private set; }

        public int Width => Model.Board.Width;
        public int Height => Model.Board.Height;
        public Vector2Int PlayerStart => Model.Board.PlayerStart;
        /// <summary>1体目の敵の初期位置（敵が1体のステージ用）。</summary>
        public Vector2Int EnemyStart => Model.Board.EnemyStarts.Count > 0 ? Model.Board.EnemyStarts[0] : Vector2Int.zero;
        public IReadOnlyList<Vector2Int> EnemyStarts => Model.Board.EnemyStarts;
        public int SpikeDamage => Model.Board.SpikeDamage;
        /// <summary>ユニットの見た目（作った順：プレイヤー → 敵A → 敵B …）。</summary>
        public IReadOnlyList<UnitController> Units => units;

        readonly List<UnitController> units = new List<UnitController>();
        readonly Dictionary<UnitModel, UnitController> views = new Dictionary<UnitModel, UnitController>();
        readonly Dictionary<Vector2Int, HazardTile> spikeViews = new Dictionary<Vector2Int, HazardTile>();
        Transform tileRoot;
        Transform unitRoot;

        /// <summary>盤面の文字列（1行目が上）から盤面を作る。spikeDamage はトゲ1つのダメージ。</summary>
        public void BuildFromRows(string[] rows, int spikeDamage = 5, VisualTheme theme = null)
        {
            Theme = theme;
            ClearBoard();
            var warnings = new List<string>();
            Model = new BattleModel(BoardModel.Parse(rows, spikeDamage, warnings));
            Presenter = new BattlePresenter(this);
            foreach (var w in warnings) Debug.LogWarning(w);
            if (!Model.Board.HasPlayerStart) Debug.LogError("盤面にプレイヤー(P)がありません");
            if (Model.Board.EnemyStarts.Count == 0 && Model.Board.Exits.Count == 0) Debug.LogError("盤面に敵(E)がありません");

            CreateTileVisuals();
            foreach (var cell in Model.Board.Spikes) EnsureSpikeView(cell);
        }

        /// <summary>トゲを置く（盤面の読み込み時と、カード「罠設置」で使う）。</summary>
        public void AddSpike(Vector2Int cell)
        {
            Model.Board.AddSpike(cell);
            EnsureSpikeView(cell);
        }

        /// <summary>モデル上のトゲに見た目が無ければ作る。</summary>
        public void EnsureSpikeView(Vector2Int cell)
        {
            if (spikeViews.ContainsKey(cell) || tileRoot == null) return;
            var hazard = new GameObject($"Spike {cell.x},{cell.y}").AddComponent<HazardTile>();
            hazard.transform.SetParent(tileRoot, false);
            hazard.Setup(cell, Model.Board.SpikeDamage, GridToWorld(cell), Theme);
            spikeViews[cell] = hazard;
        }

        public bool IsInside(Vector2Int cell) => Model.Board.IsInside(cell);

        /// <summary>壁、または盤面の外なら true。盤面の外は壁と同じ扱い。</summary>
        public bool IsWall(Vector2Int cell) => Model.Board.IsWall(cell);

        /// <summary>そのマスにいる（生きている）ユニットの見た目。いなければ null。</summary>
        public UnitController GetUnitAt(Vector2Int cell)
        {
            var unit = Model.GetUnitAt(cell);
            return unit != null ? ViewOf(unit) : null;
        }

        /// <summary>そのマスのトゲ（見た目）。無ければ null。</summary>
        public HazardTile GetHazardAt(Vector2Int cell)
        {
            if (!Model.Board.HasSpike(cell)) return null;
            EnsureSpikeView(cell);
            return spikeViews.TryGetValue(cell, out var hazard) ? hazard : null;
        }

        /// <summary>壁・盤面の外・ユニットのどれかがあって入れないマスなら true。</summary>
        public bool IsBlocked(Vector2Int cell) => Model.IsBlocked(cell);

        /// <summary>ユニットをルールモデルに加え、見た目を作る。label は敵の呼び名（"A" など。1体だけなら空）。</summary>
        public UnitController SpawnUnit(string displayName, bool isPlayer, int maxHp, Vector2Int cell, string label = "", Color? color = null)
        {
            var model = Model.AddUnit(displayName, label, isPlayer ? Team.Player : Team.Enemy, maxHp, cell);
            var go = new GameObject(isPlayer ? "Player" : $"Enemy{label}");
            go.transform.SetParent(unitRoot, false);
            var view = go.AddComponent<UnitController>();
            view.Bind(this, model, color ?? (isPlayer ? UnitController.PlayerColor : UnitController.EnemyColor));
            units.Add(view);
            views[model] = view;
            return view;
        }

        /// <summary>ルール上のユニットに対応する見た目。</summary>
        public UnitController ViewOf(UnitModel model) => views.TryGetValue(model, out var v) ? v : null;

        static readonly Color DangerColor = new Color(1f, 0.1f, 0.1f, 0.35f);
        readonly List<SpriteRenderer> dangerMarks = new List<SpriteRenderer>();

        /// <summary>危険マス（次の固有行動が当たるマス）を赤く塗る。渡したマス以外は消す。仮表示。</summary>
        public void ShowDangerCells(List<Vector2Int> cells)
        {
            dangerMarks.RemoveAll(m => m == null); // ステージを作り直すと消えるので掃除
            while (dangerMarks.Count < cells.Count && tileRoot != null)
                dangerMarks.Add(NewMark("danger", DangerColor, 0.94f));
            for (int i = 0; i < dangerMarks.Count; i++)
            {
                bool show = i < cells.Count && IsInside(cells[i]);
                dangerMarks[i].gameObject.SetActive(show);
                if (show) dangerMarks[i].transform.position = GridToWorld(cells[i]);
            }
        }

        readonly Dictionary<string, List<SpriteRenderer>> markPools = new Dictionary<string, List<SpriteRenderer>>();
        readonly Dictionary<string, List<Vector2Int>> lastMarkCells = new Dictionary<string, List<Vector2Int>>();
        /// <summary>【Puzzle v2】その予告の層に最後に塗ったマス（無ければ空）。回帰テスト用。</summary>
        public IReadOnlyList<Vector2Int> MarkCells(string key) => lastMarkCells.TryGetValue(key, out var l) ? l : new List<Vector2Int>();

        /// <summary>
        /// 【検証中】予告の表示（矢の線・移動先・炎・回復ルーンなど）。key ごとに、渡したマスだけを指定の色で塗る（それ以外は消す）。仮表示。
        /// </summary>
        public void ShowMarks(string key, IList<Vector2Int> cells, Color color, float size = 0.9f)
        {
            lastMarkCells[key] = cells.Where(IsInside).ToList(); // 【Puzzle v2】テスト用：最後に塗ったマス
            if (!markPools.TryGetValue(key, out var pool)) markPools[key] = pool = new List<SpriteRenderer>();
            pool.RemoveAll(m => m == null); // ステージを作り直すと消えるので掃除
            while (pool.Count < cells.Count && tileRoot != null)
                pool.Add(NewMark(key, color, size));
            bool themed = Theme != null && ThemedBoard.MarkElement(key, out _) != null;
            for (int i = 0; i < pool.Count; i++)
            {
                bool show = i < cells.Count && IsInside(cells[i]);
                pool[i].gameObject.SetActive(show);
                if (!show) continue;
                pool[i].transform.position = GridToWorld(cells[i]);
                if (!themed) pool[i].color = color; // テーマの絵は自分の色のまま
            }
        }

        /// <summary>【RC8】予告の塗りを、世界の座標で置く（実行中は敵の絵と一緒に滑る）。key ごとに渡した位置だけ。</summary>
        public void ShowMarksAt(string key, IList<Vector3> positions, Color color, float size = 0.9f)
        {
            if (!markPools.TryGetValue(key, out var pool)) markPools[key] = pool = new List<SpriteRenderer>();
            pool.RemoveAll(m => m == null);
            while (pool.Count < positions.Count && tileRoot != null)
                pool.Add(NewMark(key, color, size));
            bool themed = Theme != null && ThemedBoard.MarkElement(key, out _) != null;
            var cells = new List<Vector2Int>();
            for (int i = 0; i < pool.Count; i++)
            {
                bool show = i < positions.Count;
                pool[i].gameObject.SetActive(show);
                if (!show) continue;
                pool[i].transform.position = new Vector3(positions[i].x, positions[i].y, 0f);
                if (!themed) pool[i].color = color;
                cells.Add(new Vector2Int(Mathf.RoundToInt(positions[i].x + (Width - 1) * 0.5f), Mathf.RoundToInt(positions[i].y + (Height - 1) * 0.5f)));
            }
            lastMarkCells[key] = cells;
        }

        /// <summary>予告のマスを1つ作る。テーマに絵があればそれ（Telegraph 層）、無ければ色つきの四角。</summary>
        SpriteRenderer NewMark(string key, Color color, float size)
        {
            if (Theme != null)
            {
                string element = ThemedBoard.MarkElement(key, out float alpha);
                var go = element != null ? Theme.Spawn(element, tileRoot, Vector3.zero) : null;
                var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                if (sr != null)
                {
                    go.name = "Mark_" + key;
                    var c = sr.color;
                    c.a *= alpha;
                    sr.color = c;
                    var pulse = go.GetComponent<Pulse>();
                    if (pulse != null) pulse.SetBaseAlpha(c.a);
                    return sr;
                }
            }
            var box = GreyboxSprites.CreateShape(tileRoot, "Mark_" + key, GreyboxSprites.Square, color, Vector3.zero, size, 2);
            if (Theme != null) { box.sortingLayerName = SortLayers.Telegraph; box.sortingOrder = 0; }
            return box;
        }

        /// <summary>マスを一瞬光らせる（盤面の外なら何もしない）。</summary>
        public void FlashCell(Vector2Int cell, Color color)
        {
            if (IsInside(cell) && tileRoot != null) CellFlash.Spawn(tileRoot, GridToWorld(cell), color, Theme != null);
        }

        /// <summary>マスの中心の画面座標。盤面の中心が (0, 0) になるように並べる。</summary>
        public Vector3 GridToWorld(Vector2Int cell) =>
            new Vector3(cell.x - (Width - 1) * 0.5f, cell.y - (Height - 1) * 0.5f, 0f);

        /// <summary>盤面全体がカメラの描画範囲に収まるようにする。</summary>
        public void FitCamera(Camera cam, float margin = 0.6f)
        {
            if (cam == null || Model == null) return;
            cam.orthographic = true;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            if (Theme != null) margin = Mathf.Max(margin, 0.95f); // 【検証中】石の枠と四角の燭台まで入れる
            float halfHeight = Height * 0.5f + margin;
            float halfWidth = Width * 0.5f + margin;
            cam.orthographicSize = Mathf.Max(halfHeight, halfWidth / Mathf.Max(0.01f, cam.aspect));
            if (Theme != null) ThemedBoard.FitBackground(tileRoot, cam);
        }

        void ClearBoard()
        {
            if (tileRoot != null) DestroySafe(tileRoot.gameObject);
            if (unitRoot != null) DestroySafe(unitRoot.gameObject);
            units.Clear();
            views.Clear();
            spikeViews.Clear();
            tileRoot = new GameObject("Tiles").transform;
            tileRoot.SetParent(transform, false);
            unitRoot = new GameObject("Units").transform;
            unitRoot.SetParent(transform, false);
        }

        void CreateTileVisuals()
        {
            if (Theme != null)
            {
                ThemedBoard.Build(this, Theme, tileRoot);
                return;
            }
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    bool wall = Model.Board.IsBaseWall(cell); // 扉・鏡晶は上に別の絵（Copy Core V2）
                    // 【仮ビジュアル】床は市松模様（マスを数えやすく）、壁は暗く縁取り
                    var floor = (x + y) % 2 == 0 ? FloorColor : FloorColorAlt;
                    GreyboxSprites.CreateShape(tileRoot, wall ? $"Wall {x},{y}" : $"Floor {x},{y}",
                        GreyboxSprites.Square, wall ? WallColor : floor, GridToWorld(cell), wall ? 1f : 0.94f, 0);
                    if (wall) GreyboxSprites.CreateShape(tileRoot, $"WallTop {x},{y}", GreyboxSprites.Square, WallTopColor, GridToWorld(cell), 0.7f, 0);
                }
            }
            // 【検証中】ワープ陣：水色（2組目は桃色）のひし形。同じ色の2つが1組
            int pair = 0;
            var done = new HashSet<Vector2Int>();
            foreach (var kv in Model.Board.Warps)
            {
                if (done.Contains(kv.Key)) continue;
                done.Add(kv.Key);
                done.Add(kv.Value);
                var color = pair % 2 == 0 ? WarpColor : WarpColor2;
                foreach (var c in new[] { kv.Key, kv.Value })
                {
                    var pad = GreyboxSprites.CreateShape(tileRoot, $"Warp {c.x},{c.y}", GreyboxSprites.Square, color, GridToWorld(c), 0.55f, 1);
                    pad.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    GreyboxSprites.CreateShape(tileRoot, $"WarpCore {c.x},{c.y}", GreyboxSprites.Square, new Color(1f, 1f, 1f, 0.5f), GridToWorld(c), 0.18f, 1);
                }
                pair++;
            }
        }

        static void DestroySafe(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
