using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 盤面のルール上のデータ（GameObject を使わない）。マス(x, y) は左下が (0, 0)、右が x+、上が y+。
    /// 盤面の外は壁と同じ扱い。見た目は GridManager が作る。
    /// </summary>
    public class BoardModel
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int SpikeDamage { get; private set; }
        public Vector2Int PlayerStart { get; private set; }
        /// <summary>敵の初期位置。盤面の文字の読み順（上の行から、左から）。1つ目が敵A。</summary>
        public IReadOnlyList<Vector2Int> EnemyStarts => enemyStarts;
        /// <summary>【検証中】敵の兵種（EnemyStarts と同じ順番）。</summary>
        public IReadOnlyList<EnemyKind> EnemyKinds => enemyKinds;
        /// <summary>【検証中】回復ルーンの出現地点（盤面の数字 1〜9 の順。1 が最初の位置）。</summary>
        public IReadOnlyList<Vector2Int> RuneCandidates => runeCandidates;
        /// <summary>【検証中】回復マス（盤面の h。一度踏むと消える。消えた状態は EnvironmentModel が持つ）。</summary>
        public IReadOnlyList<Vector2Int> HealTiles => healTiles;
        public bool HasPlayerStart { get; private set; }
        public IEnumerable<Vector2Int> Spikes => spikes;

        bool[,] walls = new bool[0, 0];
        // 【検証中・Copy Core V2】踏み板（p＝あなた用・q＝敵用）・扉（g：両方の踏み板が同時に踏まれると開く壁）・鏡晶（X：敵のコピー攻撃で砕ける）・封印の円（o）
        List<Vector2Int> playerPlates = new List<Vector2Int>(), enemyPlates = new List<Vector2Int>(), gates = new List<Vector2Int>(), crystalHome = new List<Vector2Int>(), seals = new List<Vector2Int>();
        HashSet<Vector2Int> gateSet = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> crystals = new HashSet<Vector2Int>();
        // 【検証中・Copy Core V3】対の鏡核（c）：壁のような的。盤面の鏡核が全部「同じラウンドに」攻撃を受けると割れる（同期利用）
        List<Vector2Int> coreHome = new List<Vector2Int>();
        readonly HashSet<Vector2Int> cores = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> coreHits = new HashSet<Vector2Int>();
        public IReadOnlyList<Vector2Int> CoreHome => coreHome;
        // 【検証中・Copy Core V3】爆薬樽（b：誰の攻撃でも爆発して周り8マスに3・1回で消える）・写し箱（$：敵のまねた攻撃でだけ開く・ルーン片）
        readonly HashSet<Vector2Int> barrels = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> chests = new HashSet<Vector2Int>();
        public IEnumerable<Vector2Int> Barrels => barrels;
        public IEnumerable<Vector2Int> Chests => chests;
        public bool IsBarrel(Vector2Int cell) => barrels.Contains(cell);
        public bool IsChest(Vector2Int cell) => chests.Contains(cell);
        public bool RemoveBarrel(Vector2Int cell) => barrels.Remove(cell);
        public bool OpenChest(Vector2Int cell) => chests.Remove(cell);
        public IEnumerable<Vector2Int> Cores => cores;
        public IEnumerable<Vector2Int> CoreHitsThisRound => coreHits;
        public bool IsCore(Vector2Int cell) => cores.Contains(cell);
        /// <summary>鏡核に攻撃が当たった。残っている鏡核が全部このラウンドに当たったら、全部割れて true。</summary>
        public bool HitCore(Vector2Int cell, bool byCopy = true)
        {
            if (!cores.Contains(cell)) return false;
            coreHits.Add(cell);
            if (byCopy) coreHitsCopy.Add(cell); else coreHitsSelf.Add(cell);
            // 【Puzzle v2】研究：4命令ならあなた1人で核2つを叩けた → 同じラウンドに、あなたが叩いていない核に「まね」が当たったときだけ割れる（レビュー）
            if (!cores.All(coreHits.Contains) || !CoreHitByCopy) return false;
            cores.Clear();
            ResetCoreHits();
            return true;
        }
        readonly HashSet<Vector2Int> coreHitsCopy = new HashSet<Vector2Int>(), coreHitsSelf = new HashSet<Vector2Int>();
        /// <summary>このラウンドに、あなたが叩いていない鏡核に「まね」の攻撃が当たったか。</summary>
        public bool CoreHitByCopy => coreHitsCopy.Any(c => !coreHitsSelf.Contains(c));
        /// <summary>このラウンドに当たった数（残っている鏡核のうち）。</summary>
        public int CoreHitCount => cores.Count(coreHits.Contains);
        /// <summary>ラウンドの始まり：当たった印を消す（同じラウンドに全部当てる必要がある）。</summary>
        public void ResetCoreHits() { coreHits.Clear(); coreHitsCopy.Clear(); coreHitsSelf.Clear(); }
        /// <summary>鏡核を元の場所に戻す（ふさがっていないマスだけ。ボスの守りが戻るとき）。</summary>
        public void RegrowCore(Vector2Int cell) { cores.Add(cell); }
        // 【Puzzle v2】出口（x）・運び紋（v）・封印石（s：敵のまねた攻撃か石像の視線でだけ割れる）・石像（i：向いている列を毎ラウンド撃つ。まねた攻撃で 90 度回る）
        List<Vector2Int> exits = new List<Vector2Int>(), carryMarks = new List<Vector2Int>(), sealStoneHome = new List<Vector2Int>(), statueOrder = new List<Vector2Int>();
        readonly HashSet<Vector2Int> sealStones = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, Direction> statues = new Dictionary<Vector2Int, Direction>();
        public IReadOnlyList<Vector2Int> Exits => exits;
        public IReadOnlyList<Vector2Int> CarryMarks => carryMarks;
        public IReadOnlyList<Vector2Int> SealStoneHome => sealStoneHome;
        public IEnumerable<Vector2Int> SealStones => sealStones;
        public bool IsSealStone(Vector2Int cell) => sealStones.Contains(cell);
        public bool BreakSealStone(Vector2Int cell) => sealStones.Remove(cell);
        /// <summary>石像（盤面の読み順）。</summary>
        public IReadOnlyList<Vector2Int> StatueOrder => statueOrder;
        public bool IsStatue(Vector2Int cell) => statues.ContainsKey(cell);
        public Direction StatueFacing(Vector2Int cell) => statues.TryGetValue(cell, out var d) ? d : Direction.Down;
        public void SetStatueFacing(Vector2Int cell, Direction d) { if (statues.ContainsKey(cell)) statues[cell] = d; }
        /// <summary>巡回の石像：撃った後に時計回りに回る（ステージの指定）。</summary>
        public bool StatueSpin { get; set; }
        /// <summary>運び紋が一度全部埋まった（出来事は1回だけ）。</summary>
        public bool CarryReported { get; set; }
        /// <summary>【Puzzle v2】「封印が弱まった」を知らせたか（1回だけ）。</summary>
        public bool WeakReported { get; set; }
        /// <summary>【戦闘中心 v3】鏡核が戻るまでのラウンド（共鳴で割れた後。0 なら戻らない／戻った）。</summary>
        public int CoreRegrowIn { get; set; }
        public IReadOnlyList<Vector2Int> PlayerPlates => playerPlates;
        public IReadOnlyList<Vector2Int> EnemyPlates => enemyPlates;
        public IReadOnlyList<Vector2Int> Gates => gates;
        public IReadOnlyList<Vector2Int> Seals => seals;
        public IEnumerable<Vector2Int> Crystals => crystals;
        public IReadOnlyList<Vector2Int> CrystalHome => crystalHome;
        /// <summary>扉が開いたか（一度開いたら閉じない）。</summary>
        public bool GatesOpen { get; set; }
        public bool IsCrystal(Vector2Int cell) => crystals.Contains(cell);
        public bool IsSeal(Vector2Int cell) => seals.Contains(cell);
        // 【Puzzle v2】封紋の守護者の「光る紋」：紋（o）は1回で消える
        readonly HashSet<Vector2Int> usedSeals = new HashSet<Vector2Int>();
        public bool IsLitSeal(Vector2Int cell) => seals.Contains(cell) && !usedSeals.Contains(cell);
        public void UseSeal(Vector2Int cell) => usedSeals.Add(cell);
        public bool IsClosedGate(Vector2Int cell) => !GatesOpen && gateSet.Contains(cell);
        /// <summary>鏡晶を砕く（そのマスは床になる）。</summary>
        public bool BreakCrystal(Vector2Int cell) => crystals.Remove(cell);
        /// <summary>鏡晶をもとの場所に戻す（ふさがっていないマスだけ。鏡の番人の封印が戻るとき）。</summary>
        public void RegrowCrystal(Vector2Int cell) => crystals.Add(cell);
        /// <summary>見た目用：もとからの壁（#）だけ。扉・鏡晶は上に別の絵を描く。</summary>
        public bool IsBaseWall(Vector2Int cell) => !IsInside(cell) || walls[cell.x, cell.y];
        readonly HashSet<Vector2Int> spikes = new HashSet<Vector2Int>();
        readonly List<Vector2Int> enemyStarts = new List<Vector2Int>();
        readonly List<EnemyKind> enemyKinds = new List<EnemyKind>();
        readonly List<Vector2Int> runeCandidates = new List<Vector2Int>();
        readonly List<Vector2Int> healTiles = new List<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> warps = new Dictionary<Vector2Int, Vector2Int>();

        /// <summary>【検証中】ワープ陣（入口 → 出口）。盤面の w・u の2マスが1組。</summary>
        public IReadOnlyDictionary<Vector2Int, Vector2Int> Warps => warps;

        public bool TryWarp(Vector2Int cell, out Vector2Int destination) => warps.TryGetValue(cell, out destination);

        /// <summary>
        /// 盤面の文字列（1行目がいちばん上）から作る。記号：. 床 / # 壁 / ^ トゲ / P プレイヤー / E 敵（複数可）。
        /// 【検証中】兵種：K 弓兵 / T 盾兵 / C 追跡兵 / N 逃げ兵 / B 騎士（簡易ボス）。数字 1〜9：回復ルーンの出現地点（床）。
        /// 短い行の右側は壁で埋める。未対応の記号は床として扱い、warnings に書く。
        /// </summary>
        public static BoardModel Parse(string[] rows, int spikeDamage, List<string> warnings = null)
        {
            var b = new BoardModel { SpikeDamage = spikeDamage, Height = rows.Length };
            var runes = new SortedDictionary<char, Vector2Int>();
            var warpCells = new Dictionary<char, List<Vector2Int>>();
            foreach (var row in rows) b.Width = Mathf.Max(b.Width, row.Length);
            b.walls = new bool[b.Width, b.Height];
            for (int rowIndex = 0; rowIndex < b.Height; rowIndex++)
            {
                int y = b.Height - 1 - rowIndex;
                string row = rows[rowIndex];
                for (int x = 0; x < b.Width; x++)
                {
                    char c = x < row.Length ? row[x] : '#';
                    var cell = new Vector2Int(x, y);
                    switch (c)
                    {
                        case '#': b.walls[x, y] = true; break;
                        case '^': b.spikes.Add(cell); break;
                        case 'P': b.PlayerStart = cell; b.HasPlayerStart = true; break;
                        case 'E': b.AddEnemy(cell, EnemyKind.Normal); break;
                        case 'K': b.AddEnemy(cell, EnemyKind.Archer); break;
                        case 'T': b.AddEnemy(cell, EnemyKind.Shield); break;
                        case 'C': b.AddEnemy(cell, EnemyKind.Chaser); break;
                        case 'N': b.AddEnemy(cell, EnemyKind.Fleer); break;
                        case 'B': b.AddEnemy(cell, EnemyKind.Knight); break;
                        case 'S': b.AddEnemy(cell, EnemyKind.Swordsman); break;
                        case 'Y': b.AddEnemy(cell, EnemyKind.Spearman); break;
                        case 'O': b.AddEnemy(cell, EnemyKind.Bomber); break;
                        case 'M': b.AddEnemy(cell, EnemyKind.MirrorSoldier); break;
                        case 'V': b.AddEnemy(cell, EnemyKind.KnightV2); break;
                        case 'Q': b.AddEnemy(cell, EnemyKind.KnightV3); break;
                        case 'w':
                        case 'u':
                            if (!warpCells.TryGetValue(c, out var list)) warpCells[c] = list = new List<Vector2Int>();
                            list.Add(cell);
                            break;
                        case 'h': b.healTiles.Add(cell); break; // 【検証中】回復マス（一度だけ）
                        case '.': break;
                        // 【検証中・Copy Core V2】
                        case 'R': b.AddEnemy(cell, EnemyKind.EchoKnight); break;
                        case 'G': b.AddEnemy(cell, EnemyKind.SealGuard); break;
                        case 'Z': b.AddEnemy(cell, EnemyKind.Cannon); break;
                        case 'W': b.AddEnemy(cell, EnemyKind.Warden); break;
                        case 'p': b.playerPlates.Add(cell); break;
                        case 'q': b.enemyPlates.Add(cell); break;
                        case 'g': b.gates.Add(cell); b.gateSet.Add(cell); break;
                        case 'X': b.crystalHome.Add(cell); b.crystals.Add(cell); break;
                        case 'o': b.seals.Add(cell); break;
                        case 'c': b.coreHome.Add(cell); b.cores.Add(cell); break; // 【Copy Core V3】対の鏡核
                        case 'L': b.AddEnemy(cell, EnemyKind.Lord); break;        // 【Copy Core V3】写し身の主（ボス）
                        case 'b': b.barrels.Add(cell); break;                    // 【Copy Core V3】爆薬樽
                        case '$': b.chests.Add(cell); break;                     // 【Copy Core V3】写し箱
                        case 'H': b.AddEnemy(cell, EnemyKind.HealClone); break;   // 【Copy Core V3】癒しの写し身
                        case 'x': b.exits.Add(cell); break;                      // 【Puzzle v2】出口
                        case 'v': b.carryMarks.Add(cell); break;                 // 【Puzzle v2】運び紋
                        case 's': b.sealStoneHome.Add(cell); b.sealStones.Add(cell); break; // 【Puzzle v2】封印石
                        case 'i': b.statueOrder.Add(cell); b.statues[cell] = Direction.Down; break; // 【Puzzle v2】石像（向きはステージの指定）
                        case 'D': b.AddEnemy(cell, EnemyKind.Golem); break;      // 【Puzzle v2】運び像
                        case 'U': b.enemyStarts.Insert(0, cell); b.enemyKinds.Insert(0, EnemyKind.SealLord); break; // 【Puzzle v2】封紋の守護者（ボス）：実行順は主が先（敵A）
                        case 'A': b.AddEnemy(cell, EnemyKind.BladeStatue); break; // 【Puzzle v2】剣の像
                        case 'e': b.enemyStarts.Insert(0, cell); b.enemyKinds.Insert(0, EnemyKind.ExecKnight); break; // 【Combat V4】処刑騎士（中ボス・敵Aにする）
                        case 'k': b.enemyStarts.Insert(0, cell); b.enemyKinds.Insert(0, EnemyKind.CannonKing); break;  // 【Combat V4】時計仕掛けの砲王
                        case 'j': b.enemyStarts.Insert(0, cell); b.enemyKinds.Insert(0, EnemyKind.BeastKing); break;  // 【Combat V4】獣王
                        case 'J': b.AddEnemy(cell, EnemyKind.Mender); break;      // 【戦闘中心 v3】癒し手
                        case 'F': b.enemyStarts.Insert(0, cell); b.enemyKinds.Insert(0, EnemyKind.MirrorLord); break; // 【戦闘中心 v3】鏡の主（ボス）：実行順は主が先（敵A）
                        default:
                            if (c >= '1' && c <= '9') runes[c] = cell;
                            else warnings?.Add($"盤面の記号 '{c}' は未対応なので床として扱います ({x},{y})");
                            break;
                    }
                }
            }
            b.runeCandidates.AddRange(runes.Values);
            // 【検証中】ワープ陣：同じ文字（w・u）の2マスが対になる
            foreach (var kv in warpCells)
            {
                if (kv.Value.Count != 2) { warnings?.Add($"ワープ陣 '{kv.Key}' は2マスで1組（{kv.Value.Count} マスある）"); continue; }
                b.warps[kv.Value[0]] = kv.Value[1];
                b.warps[kv.Value[1]] = kv.Value[0];
            }
            return b;
        }

        void AddEnemy(Vector2Int cell, EnemyKind kind)
        {
            enemyStarts.Add(cell);
            enemyKinds.Add(kind);
        }

        public bool IsInside(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        /// <summary>壁、または盤面の外なら true。</summary>
        public bool IsWall(Vector2Int cell) => !IsInside(cell) || walls[cell.x, cell.y] || crystals.Contains(cell) || cores.Contains(cell) || barrels.Contains(cell) || chests.Contains(cell) || sealStones.Contains(cell) || statues.ContainsKey(cell) || (!GatesOpen && gateSet.Contains(cell));

        public bool HasSpike(Vector2Int cell) => spikes.Contains(cell);

        /// <summary>トゲを置く。新しく置けたら true。</summary>
        public bool AddSpike(Vector2Int cell) => spikes.Add(cell);

        public BoardModel Clone()
        {
            // walls は変更されないので共有してよい。トゲは「罠設置」で増えるので複製する
            var copy = new BoardModel
            {
                Width = Width, Height = Height, SpikeDamage = SpikeDamage,
                PlayerStart = PlayerStart, HasPlayerStart = HasPlayerStart, walls = walls,
                warps = warps, // 変更されないので共有
                playerPlates = playerPlates, enemyPlates = enemyPlates, gates = gates, gateSet = gateSet, crystalHome = crystalHome, seals = seals, coreHome = coreHome, // 変更されないので共有
                exits = exits, carryMarks = carryMarks, sealStoneHome = sealStoneHome, statueOrder = statueOrder,
                GatesOpen = GatesOpen, StatueSpin = StatueSpin, CarryReported = CarryReported, WeakReported = WeakReported, CoreRegrowIn = CoreRegrowIn,
            };
            copy.sealStones.UnionWith(sealStones);
            copy.usedSeals.UnionWith(usedSeals);
            copy.coreHitsCopy.UnionWith(coreHitsCopy);
            copy.coreHitsSelf.UnionWith(coreHitsSelf);
            foreach (var kv in statues) copy.statues[kv.Key] = kv.Value;
            copy.crystals.UnionWith(crystals);
            copy.cores.UnionWith(cores);
            copy.barrels.UnionWith(barrels);
            copy.chests.UnionWith(chests);
            copy.coreHits.UnionWith(coreHits);
            copy.spikes.UnionWith(spikes);
            copy.enemyStarts.AddRange(enemyStarts);
            copy.enemyKinds.AddRange(enemyKinds);
            copy.runeCandidates.AddRange(runeCandidates);
            copy.healTiles.AddRange(healTiles);
            return copy;
        }
    }
}
