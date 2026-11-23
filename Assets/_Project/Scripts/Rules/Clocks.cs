using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【Combat V4（2026-10-07）】カウント大技（クロック）の形。敵ごとに 1〜3 個の時計を持ち、
    /// 残りが ① になった時点で当たるマスが決まって予告され（赤紫の枠・技名・①）、次の「兵種の行動」で発動する。
    /// 発動すると、そのマスにいる全員（敵も）に当たる＝プレイヤーがコピー・押し引き・戦術札で、敵の大技を別の敵へ向けられる。
    /// </summary>
    public enum ClockShape
    {
        Ring8,        // 周り8マス（剣兵の八方斬り。命令札の「回転斬り」とは別）
        Ring12,       // 周り8マス＋縦横の2マス目（大薙ぎ）
        LaneFacing,   // 向き（予告したときの向き）へ Length マス。貫通（貫通突撃）
        LaneToward,   // あなたの方向（縦横の遠い方）へ Length マス（飛びかかり・追撃・暴走突進・処刑）
        Fan3,         // 向きへ3列（向きの線＋左右に1列ずつ）Length マス・壁だけで止まる（三連射）
        Cross1,       // あなたのいたマスの十字（5マス）
        BigCross,     // あなたのいたマスの大十字（9マス）
        Bombard,      // 三連爆撃：あなたのいたマスの十字＋左右2マス離れた2つの十字
        Slam3x3,      // あなたのいたマスの3×3（巨大な手）
        RowSweep,     // あなたのいた行まるごと
        ColSweep,     // あなたのいた列まるごと
        Roar,         // 周り8マス＋外へ1マス押し出す（咆哮）
    }

    /// <summary>敵が持つカウント大技1つ。</summary>
    public class Clock
    {
        public string Name = "大技";
        public ClockShape Shape;
        public int Period = 3;
        /// <summary>残りターン。1 になったら予告（そのターンの最後（まねの後・完成版）に発動）。</summary>
        public int Cd = 3;
        public int Damage = 3;
        public int Length = 3;
        /// <summary>発動したら、予告した場所から最後のマスへ突っ込む（貫通突撃・飛びかかり）。</summary>
        public bool Charge;
        /// <summary>当たった相手をレーンの向きへ（または外へ）1マス押す。</summary>
        public bool Shove;
        /// <summary>予告で決まった当たるマス（Cd が 1 の間だけ）。</summary>
        public List<Vector2Int> Cells = new List<Vector2Int>();
        public Direction Dir = Direction.Down;
        public Vector2Int Origin;

        public bool Telegraphed => Cd == 1 && Cells.Count > 0;
        /// <summary>【RC5・レビュー3】この周期で遅鐘を使った（1周期に1回まで：前は毎ターン遅らせると大技が永久に来なかった）。発動で戻る。</summary>
        public bool DelayedThisCycle;

        public Clock Clone() => new Clock { Name = Name, Shape = Shape, Period = Period, Cd = Cd, Damage = Damage, Length = Length, Charge = Charge, Shove = Shove,
            Cells = new List<Vector2Int>(Cells), Dir = Dir, Origin = Origin, DelayedThisCycle = DelayedThisCycle };
    }

    public static class Clocks
    {
        /// <summary>研究：全部の大技のダメージに足す量（pzsearch の --cbonus）。ゲームは 0。</summary>
        public static int DamageBonus;

        /// <summary>兵種ごとの標準のカウント大技（V4）。ステージ側で兵の記号を並べるだけで使える。</summary>
        public static List<Clock> DefaultFor(EnemyKind kind)
        {
            var list = new List<Clock>();
            switch (kind)
            {
                case EnemyKind.Swordsman: list.Add(new Clock { Name = "八方斬り", Shape = ClockShape.Ring8, Period = 3, Damage = 5 }); break;
                case EnemyKind.Spearman: list.Add(new Clock { Name = "貫通突撃", Shape = ClockShape.LaneFacing, Period = 4, Damage = 5, Length = 4, Charge = true }); break;
                case EnemyKind.Archer: list.Add(new Clock { Name = "三連射", Shape = ClockShape.Fan3, Period = 3, Damage = 4, Length = 6 }); break;
                case EnemyKind.Chaser: list.Add(new Clock { Name = "飛びかかり", Shape = ClockShape.LaneToward, Period = 4, Damage = 5, Length = 3, Charge = true }); break;
                case EnemyKind.Bomber: list.Add(new Clock { Name = "三連爆撃", Shape = ClockShape.Bombard, Period = 5, Damage = 4 }); break;
                case EnemyKind.Shield: list.Add(new Clock { Name = "盾突進", Shape = ClockShape.LaneFacing, Period = 3, Damage = 3, Length = 3, Charge = true, Shove = true }); break;
                case EnemyKind.MirrorLord:
                    // 【Combat V4】鏡の主＝巨大な本体と2つの手。あまり動かず、盤面全体へ強い攻撃（段階3の奔流は CombatBoss）
                    list.Add(new Clock { Name = "左手の叩きつけ", Shape = ClockShape.Slam3x3, Period = 3, Damage = LordSlamDamage });
                    list.Add(new Clock { Name = "右手の薙ぎ払い", Shape = ClockShape.RowSweep, Period = 4, Damage = LordSweepDamage });
                    list.Add(new Clock { Name = "鏡の列撃ち", Shape = ClockShape.ColSweep, Period = 5, Damage = LordSweepDamage });
                    break;
                case EnemyKind.ExecKnight:
                    list.Add(new Clock { Name = "追撃", Shape = ClockShape.LaneToward, Period = ExecChasePeriod, Damage = 4, Length = 2, Charge = true });
                    list.Add(new Clock { Name = "大薙ぎ", Shape = ClockShape.Ring12, Period = 3, Damage = 5 }); // 【完成版】6 → 5
                    list.Add(new Clock { Name = "処刑", Shape = ClockShape.LaneToward, Period = 5, Damage = 7, Length = 9 }); // 【完成版】9 → 7（初見で1回読み落とすと即死に近かった）
                    break;
                case EnemyKind.CannonKing:
                    list.Add(new Clock { Name = "小砲撃", Shape = ClockShape.Cross1, Period = CannonSmallPeriod, Damage = CannonSmallDamage });
                    list.Add(new Clock { Name = "一斉砲撃", Shape = ClockShape.Bombard, Period = CannonVolleyPeriod, Damage = CannonVolleyDamage });
                    if (CannonBigPeriod > 0) list.Add(new Clock { Name = "巨砲", Shape = ClockShape.BigCross, Period = CannonBigPeriod, Damage = CannonBigDamage }); // 【完成版】8 → 6
                    break;
                case EnemyKind.BeastKing:
                    list.Add(new Clock { Name = "飛びかかり", Shape = ClockShape.LaneToward, Period = 2, Damage = 4, Length = 3, Charge = true });
                    list.Add(new Clock { Name = "咆哮", Shape = ClockShape.Roar, Period = 3, Damage = 2, Shove = true });
                    list.Add(new Clock { Name = "暴走突進", Shape = ClockShape.LaneToward, Period = 5, Damage = 6, Length = 6, Charge = true }); // 【完成版】7 → 6
                    break;
            }
            if (kind == EnemyKind.MirrorLord && BossPeriodShift != 0) foreach (var c in list) c.Period = System.Math.Max(2, c.Period - BossPeriodShift); // 【RC6】ボスの圧（解析で比べる）
            if (!PhaseRules.IsBossKind(kind) && (MinionPeriodShift != 0 || MinionDamageShift != 0)) // 【RC11】雑魚の大技を「小さく・多く」（本人「ダメージは少ないけど頻度を増やす」）
                foreach (var c in list) { c.Period = System.Math.Max(2, c.Period - MinionPeriodShift); c.Damage = System.Math.Max(1, c.Damage + MinionDamageShift); }
            foreach (var c in list) c.Cd = c.Period;
            return list;
        }

        /// <summary>【RC6・本人「ボスはあまり攻撃してこない」】鏡の主の大技の周期を縮める量（3・4・5 ターン → 2・3・4 など）。</summary>
        public static int BossPeriodShift = 0;
        /// <summary>【RC11】雑魚（ボス・中ボス以外）の大技の周期を縮める量と、ダメージに足す量（解析は PZ_MINCLK="周期,ダメージ"）。</summary>
        public static int MinionPeriodShift = 0, MinionDamageShift = 0;
        /// <summary>
        /// 【RC6】砲王の小砲撃（周期・ダメージ）。解析は PZ_CANNONP・PZ_CANNOND。4 → 3（監査 48 戦：Learner 被ダメ 7.6 → 6.7・Novice の死亡 13 → 9。Logs/rc6/m2）。
        /// 【RC7・本人の RC6 実プレイ：砲王で HP 18 → 0（大技がほとんど）】周期 2 → 3。本人の札（18枚）で Novice の被ダメ 14.0 → 11.9（いちばん効いた。Logs/rc7/m2）。
        /// </summary>
        public static int CannonSmallPeriod = 3, CannonSmallDamage = 3;
        /// <summary>【RC7】砲王の一斉砲撃 4 → 5 ターンごと・巨砲 6 → 7 ターンごと・6 → 5（1ランの通し：Novice の6階の負け 19 → 8・クリア 67 → 81%。Logs/rc7/run/c5）。巨砲の周期 0＝巨砲なし。解析は PZ_CANNONV="周期,ダメージ"・PZ_CANNONB="周期,ダメージ"。</summary>
        public static int CannonVolleyPeriod = 5, CannonVolleyDamage = 4, CannonBigPeriod = 7, CannonBigDamage = 5;
        /// <summary>【RC7】鏡の主の大技のダメージ（叩きつけ 5 → 4・薙ぎ払いと列撃ち 4）。光柱（毎ターン）と合わせて、ボスの被ダメ Learner 9.6 → 8.2（本人の目安 4〜8）。解析は PZ_LORDD="叩きつけ,薙ぎ払い"。</summary>
        public static int LordSlamDamage = 4, LordSweepDamage = 4;

        /// <summary>戦闘の始め：敵ごとに少しずらして（全員が同じターンに撃たないように）、残り 1 のものは予告する。</summary>
        public static void Setup(BattleModel model)
        {
            int idx = 0;
            foreach (var e in model.Enemies)
            {
                if (e.Clocks.Count > 0)
                {
                    int ci = 0;
                    foreach (var c in e.Clocks) { c.Cd = c.Period + (idx % 2) + ci; ci++; if (c.Cd == 1) Telegraph(model, e, c); }
                    idx++;
                }
            }
        }

        // ───── 予告（当たるマスを決める） ─────

        static Vector2Int TowardDir(Vector2Int from, Vector2Int to)
        {
            var d = to - from;
            if (d == Vector2Int.zero) return Vector2Int.down;
            return System.Math.Abs(d.x) >= System.Math.Abs(d.y) ? new Vector2Int(System.Math.Sign(d.x), 0) : new Vector2Int(0, System.Math.Sign(d.y));
        }

        static Direction ToDirection(Vector2Int d) => d.x > 0 ? Direction.Right : d.x < 0 ? Direction.Left : d.y > 0 ? Direction.Up : Direction.Down;

        static List<Vector2Int> Line(BattleModel model, Vector2Int from, Vector2Int d, int len)
        {
            var cells = new List<Vector2Int>();
            var c = from;
            for (int i = 0; i < len; i++)
            {
                c += d;
                if (model.Board.IsWall(c)) break;
                cells.Add(c);
            }
            return cells;
        }

        static IEnumerable<Vector2Int> Cross(Vector2Int center, int radius)
        {
            yield return center;
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                for (int k = 1; k <= radius; k++) yield return center + d * k;
        }

        /// <summary>予告：この時計の当たるマスを今の位置・あなたの位置から決める。</summary>
        public static void Telegraph(BattleModel model, UnitModel e, Clock c, Vector2Int? centerOverride = null)
        {
            var p = model.Player;
            var center = centerOverride ?? (p != null ? p.Position : e.Position);
            c.Origin = e.Position;
            var cells = new List<Vector2Int>();
            switch (c.Shape)
            {
                case ClockShape.Ring8:
                case ClockShape.Roar:
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (dx != 0 || dy != 0) cells.Add(e.Position + new Vector2Int(dx, dy));
                    break;
                case ClockShape.Ring12:
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (dx != 0 || dy != 0) cells.Add(e.Position + new Vector2Int(dx, dy));
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }) cells.Add(e.Position + d * 2);
                    break;
                case ClockShape.LaneFacing:
                    c.Dir = e.Facing;
                    cells.AddRange(Line(model, e.Position, e.Facing.ToOffset(), c.Length));
                    break;
                case ClockShape.LaneToward:
                {
                    var d = TowardDir(e.Position, center);
                    c.Dir = ToDirection(d);
                    cells.AddRange(Line(model, e.Position, d, c.Length));
                    break;
                }
                case ClockShape.Fan3:
                {
                    c.Dir = e.Facing;
                    // 向きは「あなたと同じ行・列ならあなたの方向」、そうでなければ最後にコピーした向き
                    if (p != null && (p.Position.x == e.Position.x || p.Position.y == e.Position.y) && p.Position != e.Position) c.Dir = ToDirection(TowardDir(e.Position, p.Position));
                    var d = c.Dir.ToOffset();
                    var side = new Vector2Int(d.y, d.x);
                    foreach (var off in new[] { Vector2Int.zero, side, -side })
                        cells.AddRange(Line(model, e.Position + off, d, c.Length));
                    break;
                }
                case ClockShape.Cross1: cells.AddRange(Cross(center, 1)); break;
                case ClockShape.BigCross: cells.AddRange(Cross(center, 2)); break;
                case ClockShape.Bombard:
                {
                    var b = model.Board;
                    var left = center + new Vector2Int(-2, 0); var right = center + new Vector2Int(2, 0);
                    if (!b.IsInside(left)) left = center + new Vector2Int(0, 2);
                    if (!b.IsInside(right)) right = center + new Vector2Int(0, -2);
                    cells.AddRange(Cross(center, 1)); cells.AddRange(Cross(left, 1)); cells.AddRange(Cross(right, 1));
                    break;
                }
                case ClockShape.Slam3x3:
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) cells.Add(center + new Vector2Int(dx, dy));
                    break;
                case ClockShape.RowSweep:
                    for (int x = 0; x < model.Board.Width; x++) cells.Add(new Vector2Int(x, center.y));
                    break;
                case ClockShape.ColSweep:
                    for (int y = 0; y < model.Board.Height; y++) cells.Add(new Vector2Int(center.x, y));
                    break;
            }
            // 盤面の外・壁・柱は除く（レーンは壁の手前までで作ってある）。同じマスは1つ
            c.Cells = cells.Where(x => model.Board.IsInside(x) && !model.Board.IsWall(x)).Distinct().ToList();
        }

        // ───── 毎ターンの時計（兵種の行動の中で呼ぶ） ─────

        /// <summary>この敵の時計を1つ進める。発動した技の文を返す（何も無ければ null）。</summary>
        public static string Tick(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            if (e.Clocks.Count == 0) return null;
            string text = "";
            foreach (var c in e.Clocks)
            {
                if (!e.IsAlive || model.Player == null || !model.Player.IsAlive) break;
                if (c.Cd > 0 && --c.Cd == 0)
                {
                    text += Fire(model, e, c, events);
                    c.Cd = c.Period;
                    c.Cells = new List<Vector2Int>();
                    c.DelayedThisCycle = false;
                }
                if (e.IsAlive && c.Cd == 1) Telegraph(model, e, c);
                if (e.IsAlive && c.Cd == 1 && c.Cells.Count > 0) events?.Add(new BattleEvent { Type = BattleEventType.ClockWarn, Unit = e, Text = c.Name, Amount = c.Cells.Count });
            }
            return text.Length > 0 ? text.Trim() : null;
        }

        /// <summary>【RC3】③大技だけ：時計を1つ進め、0 なら予告どおりに発動（予告はここではしない：位置取りのあとの TelegraphDue）。</summary>
        public static string TickFire(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            if (e.Clocks.Count == 0) return null;
            string text = "";
            foreach (var c in e.Clocks)
            {
                if (!e.IsAlive || model.Player == null || !model.Player.IsAlive) break;
                if (c.Cd > 0 && --c.Cd == 0)
                {
                    text += Fire(model, e, c, events);
                    c.Cd = c.Period;
                    c.Cells = new List<Vector2Int>();
                    c.DelayedThisCycle = false;
                }
            }
            return text.Length > 0 ? text.Trim() : null;
        }

        /// <summary>
        /// 【RC8・本人「ボスの火力が高すぎる」（ボスで1マスに 10：叩きつけ4＋薙ぎ払い4＋光柱2）】1体の敵の大技は1ターンに1つまで。
        /// 2つ同時に①になるときは、あとの時計が1ターン待つ（②のまま）。鏡の主は、大技・鏡の奔流のターンに光柱が休む。false で RC7（解析の比べ：pzsearch の PZ_ONEBIG=0）。
        /// </summary>
        public static bool OneAtATime = true;
        /// <summary>
        /// 【RC11・本人「10〜13ダメの即死を無くす」・仕様9】部屋全体で、大技（★）は1ターンに1つまで（敵 A → B → C の順に先の敵が撃ち、ほかは1ターン待つ）。
        /// 1ターンに 10 以上受けたターンの多くが「別々の敵の大技が2つ重なった」だった（Logs/rc11_bighit_base.txt）。false で RC10（pzsearch の PZ_ONEALL=0）。
        /// </summary>
        public static bool OneAcrossEnemies = true;

        /// <summary>【RC3】⑤予告だけ：残り①でまだ当たるマスが決まっていない時計を、位置取りのあとの位置で予告する（次のターンの終わりに発動）。</summary>
        public static string TelegraphDue(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = "";
            bool torrent = e.Kind == EnemyKind.MirrorLord && e.SlamCd == 1 && e.SlamCells.Count > 0; // 【RC8】鏡の奔流（赤）が次のターンに来る
            foreach (var c in e.Clocks)
            {
                if (!e.IsAlive || model.Player == null || !model.Player.IsAlive) break;
                if (c.Cd != 1 || c.Cells.Count > 0) continue;
                if (OneAtATime && (torrent || e.Clocks.Any(o => o != c && o.Telegraphed))) { c.Cd = 2; continue; } // 【RC8】1ターンに大技は1つ：この時計は1ターン待つ
                if (OneAcrossEnemies && model.Enemies.Any(o => o != e && o.IsAlive && o.Clocks.Any(k => k.Telegraphed))) { c.Cd = 2; continue; } // 【RC11】部屋全体で1ターンに大技は1つ（先の敵が先）
                Telegraph(model, e, c);
                if (c.Cells.Count > 0) { events?.Add(new BattleEvent { Type = BattleEventType.ClockWarn, Unit = e, Text = c.Name, Amount = c.Cells.Count }); text += $"{e.Name}の{c.Name}を予告　"; }
            }
            // 【RC8】鏡の主：大技・奔流のターンは光柱が休む（あなたのいたマスに3つ重なっていた）
            if (OneAtATime && e.Kind == EnemyKind.MirrorLord && e.IsAlive && e.BombCells.Count > 0 && (torrent || e.Clocks.Any(o => o.Telegraphed)))
            {
                e.BombCells = new List<Vector2Int>();
                e.BlastCd = 0;
                text += "（大技のターンは光柱が休む）";
            }
            return text.Length > 0 ? text.Trim() : null;
        }

        /// <summary>【RC2】処刑のあと処刑騎士が動けないターン数（0 で無し。解析で比べる）。</summary>
        public static int ExecStunRounds = 2;
        /// <summary>【RC2】処刑騎士の追撃の周期（2 → 3：2ターンごとの突進で近づけず、初見の戦いが 20 ターンを超えていた）。</summary>
        public static int ExecChasePeriod = 3;

        static string Fire(BattleModel model, UnitModel e, Clock c, List<BattleEvent> events)
        {
            events?.Add(new BattleEvent { Type = BattleEventType.ClockFired, Unit = e, Text = c.Name, Amount = c.Damage });
            string hits = "";
            var cells = c.Cells.ToList();
            var hitOnce = new HashSet<UnitModel>(); // 【RC11】1回の大技で同じ相手には1回だけ（押し出しで次のマスへ動いた相手に、もう一度当たっていた：盾突進 3 → 6）
            foreach (var cell in cells)
            {
                events?.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = cell, Highlight = CellHighlight.Fire });
                var t = model.GetUnitAt(cell);
                if (t == null || !t.IsAlive || t == e || !hitOnce.Add(t)) continue;
                int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, c.Damage + DamageBonus, DamageKind.Clock, events ?? new List<BattleEvent>());
                hits += $"{(hits.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
                if (c.Shove && t.IsAlive && !t.IsImmovable) Shove(model, e, c, t, events);
            }
            // 【RC2】処刑騎士：処刑のあとは刃が床に刺さり、2ターン動けない（攻め時・まねもしない）。初見が「近づくと危ない」で長引いていた
            if (e.Kind == EnemyKind.ExecKnight && c.Name == "処刑" && ExecStunRounds > 0 && e.IsAlive)
            {
                e.StunRounds = System.Math.Max(e.StunRounds, ExecStunRounds + 1); // このラウンドの終わりに1つ減るので +1
                hits += (hits.Length > 0 ? "　" : "") + $"刃が床に刺さった：{ExecStunRounds}ターン動けない（攻め時）";
            }
            // 突っ込む：予告した場所にまだいるなら、レーンの最後の空きマスへ
            if (c.Charge && e.IsAlive && e.Position == c.Origin && cells.Count > 0)
            {
                Vector2Int? dest = null;
                foreach (var cell in cells) { if (model.GetUnitAt(cell) != null) continue; dest = cell; }
                if (dest.HasValue && dest.Value != e.Position)
                {
                    var ev = events ?? new List<BattleEvent>();
                    int n0 = ev.Count;
                    RulesEngine.EnterCell(model, e, dest.Value, e, null, false, ev);
                    for (int i = n0; i < ev.Count; i++) if (ev[i].Type == BattleEventType.Moved && ev[i].Unit == e && ev[i].Cause != null) ev[i].Cause.Kind = DamageKind.Clock; // 【RC4】突進（画面で「接近」と出さない）
                }
            }
            return $"{e.Name}の{c.Name}！ → {(hits.Length > 0 ? hits : "誰もいない")}　";
        }

        static void Shove(BattleModel model, UnitModel e, Clock c, UnitModel t, List<BattleEvent> events)
        {
            Vector2Int d = c.Shape == ClockShape.Roar ? TowardDir(e.Position, t.Position) : c.Dir.ToOffset();
            var to = t.Position + d;
            if (model.IsBlocked(to)) return;
            RulesEngine.EnterCell(model, t, to, e, null, false, events ?? new List<BattleEvent>());
        }

        // ───── 戦術札から（早鐘・遅鐘・即時爆破・照準替え・処刑の鐘） ─────

        /// <summary>残り 2 以上の時計を1つ早める（早鐘）。成功したら true。</summary>
        public static bool Hasten(BattleModel model, UnitModel e, Clock c)
        {
            if (!e.IsAlive || c.Cd < 2) return false;
            c.Cd--;
            if (c.Cd == 1) Telegraph(model, e, c);
            return true;
        }

        /// <summary>時計を1つ遅らせる（遅鐘）。予告済みならいったん消える。</summary>
        public static bool Delay(BattleModel model, UnitModel e, Clock c)
        {
            if (!e.IsAlive || c.Cd > c.Period + 1 || c.DelayedThisCycle) return false;
            c.Cd++;
            c.DelayedThisCycle = true;
            if (c.Cd != 1) c.Cells = new List<Vector2Int>();
            return true;
        }

        /// <summary>即時爆破：この時計を「このターンの終わりに発動」にする（予告を今の位置で決める）。</summary>
        public static bool Detonate(BattleModel model, UnitModel e, Clock c)
        {
            if (!e.IsAlive || c.Cd == 1) return false;
            c.Cd = 1;
            Telegraph(model, e, c);
            return true;
        }

        /// <summary>照準替え：予告済みの時計の中心を、別の敵のいるマスへ作り直す。</summary>
        public static bool Retarget(BattleModel model, UnitModel e, Clock c, UnitModel newCenter)
        {
            if (!e.IsAlive || c.Cd != 1 || newCenter == null || !newCenter.IsAlive) return false;
            if (c.Shape == ClockShape.Ring8 || c.Shape == ClockShape.Ring12 || c.Shape == ClockShape.Roar || c.Shape == ClockShape.LaneFacing || c.Shape == ClockShape.Fan3) return false; // あなたの位置で決まる形だけ（【RC5・レビュー3】三連射は弓兵の向きで決まる：前は向け直しても何も変わらなかった）
            Telegraph(model, e, c, newCenter.Position);
            return true;
        }

        /// <summary>観測士：残り②の時計が、今の位置・あなたの位置のまま①になったら当たるマス（薄く見せる。①で確定するので変わりうる）。</summary>
        public static List<Vector2Int> PreviewCells(BattleModel model, UnitModel e, Clock c)
        {
            var copy = c.Clone();
            Telegraph(model, e, copy);
            return copy.Cells;
        }

        /// <summary>画面用：予告中（残り 1）の時計の当たるマス。</summary>
        public static List<Vector2Int> WarnCells(UnitModel e) =>
            e.Clocks.Where(c => c.Telegraphed).SelectMany(c => c.Cells).Distinct().ToList();

        /// <summary>画面用：いちばん近い時計（残りが少ない順）。</summary>
        public static Clock Nearest(UnitModel e) => e.Clocks.OrderBy(c => c.Cd).FirstOrDefault();
    }
}
