using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【Puzzle v2】部屋の勝ち方（目標）。全滅だけにしない（本人 2026-10-06：「コピーを使って盤面を解き、その途中で戦う」）。
    /// </summary>
    public enum RoomGoal
    {
        /// <summary>A 討伐：敵（写し砲・運び像などの道具を除く）を全員倒す。</summary>
        Defeat,
        /// <summary>B 封印解除：封印石（s）を全部割る（敵のまねた攻撃・石像の視線でだけ割れる）。鏡核があれば鏡核も。</summary>
        Unseal,
        /// <summary>C 搬送：運び紋（v）全部に、敵か運び像を同時に立たせる。</summary>
        Carry,
        /// <summary>D 同期：鏡核（c）を全部、同じラウンドに叩いて割る。</summary>
        Sync,
        /// <summary>F 脱出：出口（x）にあなたが立つ。出口は封印石・鏡核が全部無くなると開く。</summary>
        Escape,
        /// <summary>【RC11】的：兵（E）を全部倒す（ほかの敵は残ってよい）。5分体験のパズル。</summary>
        Targets,
    }

    /// <summary>攻略条件の1行（画面に常時出す。済んだかどうか）。</summary>
    public struct GoalItem
    {
        public string Text;
        public bool Done;
        public GoalItem(string text, bool done) { Text = text; Done = done; }
    }

    /// <summary>
    /// 【Puzzle v2・2026-10-06】パズルの仕掛けと部屋の目標。GameObject を使わない（解析・テストでも同じ）。
    /// 仕掛け：出口（x）・運び紋（v）・封印石（s：敵のまねた攻撃か石像の視線でだけ割れる）・石像（i：毎ラウンド向いている列を撃つ。敵のまねた攻撃で 90 度回る）。
    /// 道具の敵：運び像（D：移動だけまねる・倒れない・数えない）・写し砲（Z：技だけまねる・倒れない・数えない）。
    /// 規則は増やさない：まね方はいつも「同じ札・同じ向き・同じ距離」。違いは盤面（立っている場所・壁・仕掛け）から出る。
    /// </summary>
    public static class PuzzleRules
    {
        /// <summary>石像の視線のダメージ（あなたにも敵にも。運び像・写し砲は倒れない）。</summary>
        public const int StatueDamage = 3;

        /// <summary>勝った（討伐は全滅、ほかは目標の達成）。</summary>
        public static bool Won(BattleModel model) => model.Goal == RoomGoal.Defeat ? model.AllEnemiesDead : GoalReached(model);

        /// <summary>目標を達成したか（討伐以外）。</summary>
        public static bool GoalReached(BattleModel model)
        {
            var b = model.Board;
            switch (model.Goal)
            {
                case RoomGoal.Unseal: return SealsCleared(b) && (b.CoreHome.Count == 0 || !b.Cores.Any());
                case RoomGoal.Sync: return b.CoreHome.Count > 0 && !b.Cores.Any();
                case RoomGoal.Carry: return CarryDone(model);
                case RoomGoal.Escape: return ExitOpen(b) && model.Player != null && model.Player.IsAlive && b.Exits.Contains(model.Player.Position);
                case RoomGoal.Targets: return model.Enemies.Any(e => e.Kind == EnemyKind.Normal) && model.Enemies.Where(e => e.Kind == EnemyKind.Normal).All(e => !e.IsAlive);
                default: return model.AllEnemiesDead;
            }
        }

        public static bool SealsCleared(BoardModel b) => !b.SealStones.Any();
        /// <summary>
        /// 封印が弱まった：倒せる敵が全員倒れたら、残りの封印石はあなたの攻撃でも割れる（詰み防止・「最後の1体なら能力解除」と同じ考え）。
        /// 敵を倒しきる道は遅い（写し砲・まねを使う方が早い）。
        /// </summary>
        public static bool SealsWeak(BattleModel model) => model.Enemies.Any(e => !e.IsObject) && model.Enemies.All(e => !e.IsAlive || e.IsObject); // 倒せる敵がいない部屋（道具だけ）では弱まらない
        /// <summary>
        /// 命令1つごと・環境の段階：封印が弱まった瞬間を1回だけ知らせる（パズルブレイカー：画面に何も出ず、詰んだように見えた）。
        /// </summary>
        public static string CheckWeak(BattleModel model, List<BattleEvent> events)
        {
            var b = model.Board;
            if (b.WeakReported || (!b.SealStones.Any() && b.StatueOrder.Count == 0) || !SealsWeak(model)) return "";
            b.WeakReported = true;
            const string text = "封印が弱まった：倒せる敵がいない。封印石・石像はあなたの攻撃でも動く";
            events?.Add(new BattleEvent { Type = BattleEventType.SealsWeakened, Cell = b.SealStones.Any() ? b.SealStones.First() : b.StatueOrder[0], Text = text });
            return "／" + text;
        }

        /// <summary>出口が開いているか（封印石・鏡核が全部無くなった）。</summary>
        public static bool ExitOpen(BoardModel b) => SealsCleared(b) && !b.Cores.Any();

        /// <summary>運び紋の全部に、あなた以外（敵・運び像）が立っている。</summary>
        public static bool CarryDone(BattleModel model) =>
            model.Board.CarryMarks.Count > 0 && model.Board.CarryMarks.All(c => { var u = model.GetUnitAt(c); return u != null && !u.IsPlayer; });

        /// <summary>命令1つごと：運び紋が全部埋まったら扉が開く（搬送の部屋以外でも、扉があれば）。</summary>
        public static string CheckMarks(BattleModel model, List<BattleEvent> events)
        {
            var b = model.Board;
            if (b.CarryMarks.Count == 0 || b.CarryReported || !CarryDone(model)) return "";
            b.CarryReported = true;
            events?.Add(new BattleEvent { Type = BattleEventType.CarryDone, Cell = b.CarryMarks[0] });
            if (b.GatesOpen || b.Gates.Count == 0) return "／運び紋が全部埋まった";
            b.GatesOpen = true;
            foreach (var g in b.Gates) events?.Add(new BattleEvent { Type = BattleEventType.GateOpened, Cell = g });
            return "／運び紋が全部埋まった：扉が開いた";
        }

        /// <summary>封印石が割れる（敵のまねた攻撃・石像の視線）。全部割れたら扉も開く。</summary>
        public static string BreakSealStone(BattleModel model, Vector2Int cell, List<BattleEvent> events)
        {
            var b = model.Board;
            if (!b.BreakSealStone(cell)) return "";
            events?.Add(new BattleEvent { Type = BattleEventType.SealStoneBroken, Cell = cell, Amount = b.SealStones.Count() });
            string text = $"封印石が割れた（残り {b.SealStones.Count()}）";
            if (SealsCleared(b) && b.Gates.Count > 0 && !b.GatesOpen && b.CarryMarks.Count == 0 && b.PlayerPlates.Count == 0)
            {
                b.GatesOpen = true;
                foreach (var g in b.Gates) events?.Add(new BattleEvent { Type = BattleEventType.GateOpened, Cell = g });
                text += "：封印が全部解けて扉が開いた";
            }
            if (b.Exits.Count > 0 && ExitOpen(b)) { foreach (var x in b.Exits) events?.Add(new BattleEvent { Type = BattleEventType.ExitOpened, Cell = x }); text += "：出口が開いた"; }
            return text;
        }

        /// <summary>石像を時計回りに 90 度回す（敵のまねた攻撃が当たった）。</summary>
        public static string RotateStatue(BattleModel model, Vector2Int cell, List<BattleEvent> events)
        {
            var b = model.Board;
            if (!b.IsStatue(cell)) return "";
            var d = Clockwise(b.StatueFacing(cell));
            b.SetStatueFacing(cell, d);
            events?.Add(new BattleEvent { Type = BattleEventType.StatueTurned, Cell = cell, Direction = d.ToOffset() });
            return $"石像が回った（{d.ToLabel()}）";
        }

        public static Direction Clockwise(Direction d) => d == Direction.Up ? Direction.Right : d == Direction.Right ? Direction.Down : d == Direction.Down ? Direction.Left : Direction.Up;

        /// <summary>石像の視線のマス（向いている方向へ、壁・物・ユニットの手前まで。最初のユニット・封印石のマスは含む）。</summary>
        public static List<Vector2Int> GazeLine(BattleModel model, Vector2Int statue)
        {
            var b = model.Board;
            var cells = new List<Vector2Int>();
            var off = b.StatueFacing(statue).ToOffset();
            var c = statue;
            for (int i = 0; i < 20; i++)
            {
                c += off;
                if (!b.IsInside(c)) break;
                if (b.IsSealStone(c)) { cells.Add(c); break; }
                if (b.IsWall(c)) break;
                cells.Add(c);
                if (model.GetUnitAt(c) != null) break;
            }
            return cells;
        }

        /// <summary>紫の予告：石像の視線（ラウンドの終わりに必ず撃つ）。</summary>
        public static List<Vector2Int> HazardCells(BattleModel model)
        {
            var cells = new List<Vector2Int>();
            foreach (var s in model.Board.StatueOrder) cells.AddRange(GazeLine(model, s).Where(c => !model.Board.IsSealStone(c)));
            return cells;
        }

        /// <summary>
        /// 環境の段階（ラウンドの終わり・兵種の行動の後）：石像が向いている列を撃つ。最初のユニットに StatueDamage、封印石なら割る。
        /// 原因は「仕掛け」（だれの攻撃でもない。反響鎧兵にも効く）。巡回の石像（StageData.statueSpin）は撃った後に時計回りに回る。
        /// </summary>
        public static void StatuePhase(BattleModel model, List<BattleEvent> events, List<string> log)
        {
            var b = model.Board;
            foreach (var s in b.StatueOrder.ToList())
            {
                var line = GazeLine(model, s);
                foreach (var c in line) events?.Add(new BattleEvent { Type = BattleEventType.TargetCell, Cell = c, Highlight = CellHighlight.Arrow });
                if (line.Count > 0)
                {
                    var end = line[line.Count - 1];
                    if (b.IsSealStone(end)) { var broke = BreakSealStone(model, end, events); log?.Add("石像の視線：" + broke); } // log が null でも割る（?. は右辺ごと飛ばす：解析・総当たりで割れていなかった）
                    else
                    {
                        var u = model.GetUnitAt(end);
                        if (u != null && u.IsAlive)
                        {
                            int dealt = RulesEngine.ApplyDamage(model, null, null, false, u, StatueDamage, DamageKind.Gaze, events);
                            log?.Add($"石像の視線：{u.Name}に {dealt}（残り HP {u.Hp}）");
                        }
                    }
                }
                if (b.StatueSpin) RotateStatue(model, s, events);
            }
        }

        /// <summary>仕掛けが動いた出来事か（共鳴補給・刻写・解析）。</summary>
        public static bool IsGimmickEvent(BattleEvent e) =>
            e.Type == BattleEventType.SealStoneBroken || e.Type == BattleEventType.StatueTurned || e.Type == BattleEventType.CoreHit ||
            e.Type == BattleEventType.CoresShattered || e.Type == BattleEventType.GateOpened || e.Type == BattleEventType.ChestOpened ||
            e.Type == BattleEventType.CloneShattered || e.Type == BattleEventType.ExitOpened || e.Type == BattleEventType.CrystalBurst ||
            e.Type == BattleEventType.BarrelBurst || e.Type == BattleEventType.BarrierBroken || e.Type == BattleEventType.CarryDone;

        /// <summary>仕掛けが「進んだ」出来事（共鳴の一歩の条件）：封印石・鏡核が割れた、運び紋・扉・出口・箱・ボスの成功。</summary>
        public static bool IsProgressEvent(BattleEvent e) =>
            e.Type == BattleEventType.SealStoneBroken || e.Type == BattleEventType.CoresShattered || e.Type == BattleEventType.CarryDone ||
            e.Type == BattleEventType.GateOpened || e.Type == BattleEventType.ExitOpened || e.Type == BattleEventType.ChestOpened ||
            e.Type == BattleEventType.PuzzleSolved || e.Type == BattleEventType.CloneShattered;

        // ───── 攻略条件の文（画面に常時出す。答え＝手順は出さない。「何を成立させるか」まで） ─────

        /// <summary>部屋の目標の一文。ステージに文があればそれ。</summary>
        public static string GoalSentence(BattleModel model, string stageText = null)
        {
            if (!string.IsNullOrEmpty(stageText)) return stageText;
            var lord = model.Enemies.FirstOrDefault(e => e.IsAlive && (e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord));
            if (lord != null) return PuzzleBoss.ObjectiveSentence(model, lord);
            var b = model.Board;
            switch (model.Goal)
            {
                case RoomGoal.Unseal: return b.CoreHome.Count > 0 ? "封印石と鏡核を全部割れ" : "封印石を全部割れ（敵のまねた攻撃・石像の視線でだけ割れる）";
                case RoomGoal.Carry: return "運び紋の全部に、敵か運び像を立たせろ";
                case RoomGoal.Sync: return "鏡核を全部、同じターンに叩いて割れ（1つ以上は“まね攻撃”）";
                case RoomGoal.Escape: return b.SealStoneHome.Count > 0 || b.CoreHome.Count > 0 ? "封印を解いて、出口へ" : "出口へたどり着け";
                case RoomGoal.Targets: return "兵を全部倒せ";
                default: return (model.Enemies.Count(e => e.IsAlive && !e.IsObject) == 1 ? "敵を倒せ" : "敵を全員倒せ") + (model.Enemies.Any(e => e.IsAlive && e.IsObject) ? "（写し砲・運び像は数えない）" : ""); // 【RC10・初見テスト2回目「敵が1体なのに全員」】
            }
        }

        /// <summary>進み具合（✓/×）。</summary>
        public static List<GoalItem> Progress(BattleModel model)
        {
            var items = new List<GoalItem>();
            var b = model.Board;
            var lord = model.Enemies.FirstOrDefault(e => e.IsAlive && (e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord));
            if (lord != null) return PuzzleBoss.Progress(model, lord);
            if (b.SealStoneHome.Count > 0)
                for (int i = 0; i < b.SealStoneHome.Count; i++) items.Add(new GoalItem($"封印石{(char)('A' + i)}", !b.IsSealStone(b.SealStoneHome[i])));
            if (b.WeakReported && (b.SealStones.Any() || b.StatueOrder.Count > 0)) items.Add(new GoalItem("封印が弱まった：直接でも動く", true));
            if (b.CoreHome.Count > 0)
            {
                var hits = new HashSet<Vector2Int>(b.CoreHitsThisRound);
                bool gone = !b.Cores.Any();
                for (int i = 0; i < b.CoreHome.Count; i++) items.Add(new GoalItem($"鏡核{(char)('A' + i)}{(gone ? "" : "（このターン）")}", gone || hits.Contains(b.CoreHome[i])));
                if (!gone) items.Add(new GoalItem("まねで1つ以上", b.CoreHitByCopy));
            }
            if (b.CarryMarks.Count > 0)
                for (int i = 0; i < b.CarryMarks.Count; i++) { var u = model.GetUnitAt(b.CarryMarks[i]); items.Add(new GoalItem($"運び紋{(char)('A' + i)}", (u != null && !u.IsPlayer) || (b.Gates.Count > 0 && b.CarryReported))); } // 扉の部屋は一度運べば済み（像が離れても扉は開いたまま）
            if (b.Gates.Count > 0) items.Add(new GoalItem("扉", b.GatesOpen));
            if (b.Exits.Count > 0) items.Add(new GoalItem("出口", ExitOpen(b) && (b.Gates.Count == 0 || b.GatesOpen))); // 扉が閉じている間は未
            if (model.Goal == RoomGoal.Defeat || items.Count == 0)
            {
                int total = model.Enemies.Count(e => !e.IsObject), alive = model.Enemies.Count(e => !e.IsObject && e.IsAlive);
                if (total > 0) items.Add(new GoalItem($"敵 残り {alive}/{total}", alive == 0));
            }
            return items;
        }

        /// <summary>
        /// 敵の倒し方（直接・まね・仕掛け）。普通の敵は null（全部効く）。画面の「倒し方」と敵の頭上の印に使う。
        /// </summary>
        public static (bool direct, bool copy, bool gimmick, string note)? HowToHurt(BattleModel model, UnitModel u)
        {
            if (u == null || !u.IsAlive || u.IsPlayer) return null;
            if (u.Kind == EnemyKind.Cannon || u.Kind == EnemyKind.Golem || u.Kind == EnemyKind.BladeStatue) return (false, false, false, u.Kind == EnemyKind.BladeStatue ? "道具：倒れない（主を斬らせる）" : "道具：倒れない（数えない）");
            if (u.Kind == EnemyKind.HealClone && model.SoftRules && !u.Dormant) return (true, true, false, $"殴って倒せる・まねで主を斬らせると -{CombatBoss.CloneStrikeDamage}"); // 【戦闘中心 v3】
            if (u.Kind == EnemyKind.HealClone) return (false, false, false, u.Dormant ? "像：段階2で目覚める" : "傷つかない：主を斬らせると砕ける");
            if (u.Kind == EnemyKind.Lord || u.Kind == EnemyKind.SealLord || u.Kind == EnemyKind.MirrorLord) return PuzzleBoss.HowToHurt(model, u);
            if (u.Kind == EnemyKind.Mender) return (true, true, true, $"毎ターン傷ついた仲間を +{PhaseRules.MenderHeal}・技まね（隣の仲間を殴らせられる）"); // 【戦闘中心 v3】
            bool gimmick = model.Board.Spikes.Any() || model.Board.StatueOrder.Count > 0 || model.Board.Crystals.Any() || model.Board.Barrels.Any() || model.Enemies.Any(e => e.IsAlive && e.Kind == EnemyKind.Bomber);
            if (u.EchoArmor && model.SoftRules) return (true, true, gimmick, u.ArmorBroken ? "鎧が割れた：普通に傷つく" : $"直接は {RulesEngine.EchoSoftDamage} だけ。まね攻撃・兵の攻撃・仕掛けで鎧が割れる"); // 【戦闘中心 v3】Soft
            if (u.EchoArmor && model.Enemies.Any(e => e != u && e.IsAlive && !e.IsObject)) return (false, true, gimmick, "最後の1体なら直接も効く");
            if (u.Barrier > 0 && u.BarrierRule == 3) return (false, false, true, "鏡核を全部同じターンに叩くまで傷つかない");
            if (u.Barrier > 0 && u.BarrierRule == 2) return (false, false, true, "守り：まねの移動で白い円に入ると割れる");
            if (u.Barrier > 0 && u.BarrierRule == 0) return (false, true, true, $"守り {u.Barrier}：まね攻撃で守りを削る");
            return null;
        }
    }
}
