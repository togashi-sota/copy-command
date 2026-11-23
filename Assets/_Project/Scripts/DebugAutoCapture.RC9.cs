using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC9】-capturerc9 1：本編は「答え」を出さない画面の見直し用（勝敗は強制しない・開発版だけ）。
    /// 本編の向き選び（答えなし）と、同じ場面の答えあり（比べ用）／押して敵の攻撃を敵へ（実行中：ずれた・同士討ち）→ 次のターンの「前のターンに起きたこと」／
    /// 倒して消す（実行中）／チュートリアル4・5（答えあり）／5分体験の部屋／床の仕掛け／ボスの段階1・2・3の狙い／負けた画面／タイトル。
    /// -capturefixed 0|1|2 で「固定」の文字の出し方（比べ用）。
    /// </summary>
    public partial class DebugAutoCapture
    {
        IEnumerator CaptureRC9(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var fCmd = typeof(BattleUI).GetField("pendingCommand", flags);
            var fCard = typeof(BattleUI).GetField("pendingCard", flags);
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"画面 {Screen.width}x{Screen.height}・固定の文字 {BattleUI.FixedLabelMode}");
            BuildInfo.SetForTest(true);
            ScreenFx.Disabled = true;
            act.Profile.finalFinished = 1;
            act.Profile.tacticIntroSeen = true;
            act.StartRun(ActContent.Roles[0], 8801, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();

            IEnumerator Room(string id, int floor, string shot)
            {
                ActRun.CaptureLayout = 0;
                act.StartRoomForCapture(V4Content.Room(id), floor);
                yield return new WaitForSeconds(id == "v4_boss" || id.StartsWith("v4_m") ? 2.4f : 0.8f);
                if (shot != null) yield return Shot(shot);
            }
            IEnumerator Select(CardInstance card, Direction d, string shot)
            {
                fCmd.SetValue(ui, card.Data);
                fCard.SetValue(ui, card);
                ui.DebugFocus = d;
                yield return null; yield return null; yield return null;
                yield return Shot(shot);
                ui.DebugFocus = null;
                fCmd.SetValue(ui, null);
                fCard.SetValue(ui, null);
                yield return null;
            }
            CardInstance Give(string name)
            {
                var c = turn.Deck.Hand.FirstOrDefault(x => x.Data.name == name || x.Data.name == "Cmd_" + name);
                if (c != null) return c;
                var data = act.Card(name);
                if (data == null) return null;
                c = new CardInstance(data);
                turn.Deck.DebugAddToHand(c);
                return c;
            }
            IEnumerator RunTurn(string tag)
            {
                turn.ExecuteTurn();
                // 【RC9】実行中は 0.3 秒ごとに撮る（段の名前つき）：まねが当たる瞬間・岩が落ちる瞬間を取りこぼさない
                for (int k = 0; k < 30 && turn.State != BattleState.PlayerInput && turn.State != BattleState.GameOver && turn.State != BattleState.Cleared; k++)
                {
                    yield return new WaitForSeconds(0.3f);
                    if (turn.State == BattleState.PlayerInput) break;
                    yield return Shot($"{tag}_{k:00}_{(int)turn.Step}{turn.Step}");
                }
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"{tag}_next_turn");
            }

            // 1) 中ボス（獣王）の始め・敵にマウス
            yield return Room("v4_m3", 3, "r01_midboss_start");
            var m = turn.Grid.Model;
            summary.AppendLine($"獣王：危険 {Threats.Compute(m).Count}・重なり最大 {Threats.MaxStack(Threats.Compute(m))}");
            var foe = m.Enemies.FirstOrDefault(e => e.IsAlive && !e.IsObject && e.Kind != EnemyKind.BeastKing) ?? m.Enemies.First(e => e.IsAlive);
            ui.DebugHoverEnemyId = foe.Id; yield return null; yield return null;
            yield return Shot("r02_hover_enemy");
            ui.DebugHoverEnemyId = -1;

            // 2) 前衛の二人：剣兵の赤の中に立ち、剣兵を押す向きを選ぶ（本編＝答えなし／比べ＝答えあり）→ 実行して、剣兵の赤を槍兵へ
            yield return Room("v4_n1", 1, "r10_normal_start");
            m = turn.Grid.Model;
            var swordsman = m.Enemies.FirstOrDefault(e => e.IsAlive && e.Kind == EnemyKind.Swordsman) ?? m.Enemies.FirstOrDefault(e => e.IsAlive && Threats.Follows(e));
            if (swordsman != null)
            {
                foreach (var d in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
                {
                    var spot = swordsman.Position - d.ToOffset();
                    var beyond = swordsman.Position + d.ToOffset();
                    var near = swordsman.Position + d.ToOffset() * 2;
                    if (!m.Board.IsInside(spot) || m.IsBlocked(spot) || !m.Board.IsInside(beyond) || m.IsBlocked(beyond) || !m.Board.IsInside(near) || m.IsBlocked(near)) continue;
                    m.Player.Position = spot;
                    var other = m.Enemies.FirstOrDefault(e => e.IsAlive && e != swordsman && !e.IsObject);
                    if (other != null) { other.Position = near; other.Facing = d; } // 押した先の隣に槍兵（あなたの方は向かない）
                    yield return new WaitForSeconds(0.6f);
                    var push = Give("PushSlash") ?? Give("Push");
                    yield return Select(push, d, "r11_main_choose_push_no_answer");
                    BattleUI.DebugAnswerPreview = true;
                    yield return Select(push, d, "r12_compare_answer_preview");
                    BattleUI.DebugAnswerPreview = false;
                    turn.AddCommand(new CommandRecord(push.Data, d, push.Data.minDistance, push));
                    yield return null; yield return null;
                    yield return Shot("r13_main_planned");
                    yield return RunTurn("r14_redirect");
                    summary.AppendLine($"押して同士討ち：{PhaseRules.ShortName(swordsman)} を {d} へ → {(other != null ? PhaseRules.ShortName(other) + " HP " + other.Hp : "—")}");
                    break;
                }
            }

            // 3) 倒して消す：剣兵の HP を減らし、赤の中から斬る（実行中に攻撃が消える）
            yield return Room("v4_n1", 1, null);
            m = turn.Grid.Model;
            swordsman = m.Enemies.FirstOrDefault(e => e.IsAlive && e.Kind == EnemyKind.Swordsman) ?? m.Enemies.FirstOrDefault(e => e.IsAlive && Threats.Follows(e));
            if (swordsman != null)
            {
                foreach (var d in new[] { Direction.Up, Direction.Left, Direction.Right, Direction.Down })
                {
                    var spot = swordsman.Position - d.ToOffset();
                    if (!m.Board.IsInside(spot) || m.IsBlocked(spot)) continue;
                    m.Player.Position = spot;
                    swordsman.Hp = 2;
                    yield return new WaitForSeconds(0.6f);
                    var slash = Give("Slash");
                    turn.AddCommand(new CommandRecord(slash.Data, d, slash.Data.minDistance, slash));
                    yield return RunTurn("r20_kill");
                    break;
                }
            }

            // 3b) 固定の赤（砲兵の照準）が多い部屋：「固定」の文字の比べ（-capturefixed 0|1|2）。2ターン目の照準が出るまで1ターン進める
            yield return Room("v4_n3", 1, "r15_fixed_room_start");
            {
                var w0 = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk && turn.CanAddCard(c));
                if (w0 != null) { turn.AddCommand(new CommandRecord(w0.Data, Direction.Left, w0.Data.minDistance, w0)); turn.ExecuteTurn(); }
                for (int k = 0; k < 160 && turn.State != BattleState.PlayerInput && turn.State != BattleState.GameOver; k++) yield return new WaitForSeconds(0.05f);
                yield return new WaitForSeconds(0.6f);
                yield return Shot("r16_fixed_room_turn2");
            }

            // 4) 床の仕掛け
            yield return Room("v4_t2", 3, "r30_rockfall");
            yield return Room("v4_t1", 2, "r31_fire");
            yield return Room("v4_t3", 3, "r32_laser");
            yield return Room("v4_t4", 3, "r33_spikes");

            // 5) ボス：段階1 → 2 → 3（狙いの印）
            yield return Room("v4_boss", 7, "r40_boss_phase1");
            yield return new WaitForSeconds(4.8f); yield return Shot("r40b_boss_phase1_board"); // 【RC11】帯が消えたあと：眠る写し身（石像）が見える
            m = turn.Grid.Model;
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.MirrorLord);
            summary.AppendLine($"ボス：HP {lord.MaxHp}・段階2 {CombatBoss.Floor(lord, 0)}・段階3 {CombatBoss.Floor(lord, 1)}・重なり最大 {Threats.MaxStack(Threats.Compute(m))}");
            CombatBoss.Damage(m, lord, lord.Hp - CombatBoss.Floor(lord, 0), m.Player, null, false, DamageKind.Direct, new List<BattleEvent>());
            foreach (var cl in m.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive)) { cl.Dormant = false; cl.WakingUp = false; }
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r41_boss_phase2_banner");
            yield return new WaitForSeconds(3.2f);
            yield return Shot("r42_boss_phase2");
            // 5b) 見どころ：段階2で、写し身（全まね）を主の真下に置いて、あなたが ↑ に斬る → 写し身のまねの斬撃が主に当たる（-6）
            var clone = m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.HealClone && e.IsAlive && !e.Dormant);
            var under = lord.Position + Vector2Int.down;
            if (clone != null && m.Board.IsInside(under) && (m.GetUnitAt(under) == null || m.GetUnitAt(under) == clone) && !m.Board.IsWall(under))
            {
                clone.Position = under;
                // 【RC10】主の光柱・手の大技が先（②・④）に写し身へ当たると「反射」の場面になる → この場面（写し身のまねで主を斬る）では主の攻撃を休ませる
                lord.BombCells = new List<Vector2Int>(); lord.BlastCd = 0;
                foreach (var ck in lord.Clocks) ck.Cd = Mathf.Max(ck.Cd, 3);
                foreach (var other in m.Enemies.Where(e => e.IsAlive && e != lord && e != clone && !e.IsPlayer && Mathf.Abs(e.Position.x - under.x) + Mathf.Abs(e.Position.y - under.y) <= 2).ToList())
                    if (other.Kind != EnemyKind.HealClone) { var far = new Vector2Int(m.Board.Width - 1, m.Board.Height - 1); if (m.GetUnitAt(far) == null && !m.Board.IsWall(far)) other.Position = far; }
                var spotP = m.Enemies.Where(e => e.IsAlive).Select(e => e.Position).ToList();
                for (int y = 0; y < m.Board.Height; y++)
                    for (int x = 0; x < m.Board.Width; x++)
                    {
                        var c = new Vector2Int(x, y);
                        if (m.IsBlocked(c) || m.Board.IsWall(c) || ThreatMap.EnemyAttack(m).Contains(c)) continue;
                        if (m.GetUnitAt(c + Vector2Int.up) != null) continue; // あなたの斬撃は空振り（まねの斬撃だけが主に当たる）
                        if (Mathf.Abs(c.x - lord.Position.x) < 2) continue;
                        m.Player.Position = c; y = m.Board.Height; break;
                    }
                yield return new WaitForSeconds(0.6f);
                var bs = Give("Slash");
                yield return Select(bs, Direction.Up, "r44_boss_clone_choose");
                turn.AddCommand(new CommandRecord(bs.Data, Direction.Up, bs.Data.minDistance, bs));
                int hp0 = lord.Hp;
                yield return RunTurn("r45_boss_clone_strike");
                summary.AppendLine($"写し身のまねで主へ：{hp0} → {lord.Hp}");
            }
            // 5c) 【RC10・ボスの見せ場】写し身は鏡：光柱（赤い十字）の中に写し身を残して、あなたは外へ → 主の光柱が写し身ではね返って主へ
            var mirror = m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.HealClone && e.IsAlive && !e.Dormant);
            if (mirror != null && lord.IsAlive && lord.BossStage == 1)
            {
                lord.StunRounds = 0;
                bool placed = false;
                for (int y = m.Board.Height - 1; y >= 1 && !placed; y--)
                    for (int x = 1; x < m.Board.Width - 2 && !placed; x++)
                    {
                        var pc = new Vector2Int(x, y);
                        var cc = pc + Vector2Int.left;
                        var r1 = pc + Vector2Int.right; var r2 = pc + Vector2Int.right * 2;
                        bool Free(Vector2Int c) => m.Board.IsInside(c) && !m.Board.IsWall(c) && (m.GetUnitAt(c) == null || m.GetUnitAt(c) == mirror || m.GetUnitAt(c) == m.Player) && !m.Board.Spikes.Contains(c);
                        if (!Free(pc) || !Free(cc) || !Free(r1) || !Free(r2)) continue;
                        if (ThreatMap.EnemyAttack(m).Contains(r2)) continue;
                        mirror.Position = cc; m.Player.Position = pc; placed = true;
                    }
                if (placed)
                {
                    lord.BombCells = PhaseRules.BombShape(m, m.Player.Position);
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("r46_boss_reflect_before");
                    var dash = Give("Dash") ?? Give("Walk");
                    if (dash != null)
                    {
                        yield return Select(dash, Direction.Right, "r46_boss_reflect_choose");
                        turn.AddCommand(new CommandRecord(dash.Data, Direction.Right, dash.Data.minDistance, dash));
                        int hpR = lord.Hp;
                        yield return RunTurn("r47_boss_reflect");
                        summary.AppendLine($"写し身は鏡（光柱をはね返す）：主 {hpR} → {lord.Hp}");
                    }
                }
            }
            CombatBoss.Damage(m, lord, lord.Hp - CombatBoss.Floor(lord, 1), m.Player, null, false, DamageKind.Direct, new List<BattleEvent>());
            yield return new WaitForSeconds(3.6f);
            yield return Shot("r43_boss_phase3");
            // 5d) 【RC11・仕様8】主を倒す＝その場で勝ち：残りの敵は崩れ、予告は消え、入力は止まる（光・揺れ・ヒットストップ・「鏡の主 撃破」）
            if (lord.IsAlive)
            {
                yield return new WaitForSeconds(1.4f);
                // 【RC11・採用担当の最後の見直し「ボスのとどめが ふつうの斬撃（まねる敵：なし）」】とどめは まね で：
                //   あなたは誰もいない方へ斬る → 主の隣の剣兵が同じ向きに斬る（まね）→ 主に当たる。主の HP は ②の赤（剣兵の4方向）では倒れない 5
                bool put = false; Direction kd = Direction.Up;
                var sw = m.Enemies.FirstOrDefault(e => e.IsAlive && e.Kind != EnemyKind.MirrorLord && e.Kind != EnemyKind.HealClone && !e.Dormant && e.CopyKind == CopyKind.All);
                if (sw != null)
                {
                    lord.Hp = Mathf.Min(lord.Hp, 5);
                    foreach (var dv in new[] { Vector2Int.down, Vector2Int.up, Vector2Int.left, Vector2Int.right })
                    {
                        if (put) break;
                        var dc = lord.Position - dv; // 剣兵を主の隣へ（主の方を向いて斬る位置）
                        if (!m.Board.IsInside(dc) || m.Board.IsWall(dc) || m.Board.Spikes.Contains(dc)) continue;
                        var occD = m.GetUnitAt(dc);
                        if (occD != null && occD != sw && occD != m.Player) continue;
                        var dir = CopyCore.Toward(dc, lord.Position, Direction.Up);
                        var step = lord.Position - dc;
                        var oldSw = sw.Position; sw.Position = dc;
                        var redNow = ThreatMap.EnemyAttack(m);
                        for (int yy = 0; yy < m.Board.Height && !put; yy++)
                            for (int xx = 0; xx < m.Board.Width && !put; xx++)
                            {
                                var pp = new Vector2Int(xx, yy);
                                var tgt = pp + step;
                                bool FreeC(Vector2Int c) => m.Board.IsInside(c) && !m.Board.IsWall(c) && !m.Board.Spikes.Contains(c) && (m.GetUnitAt(c) == null || m.GetUnitAt(c) == m.Player);
                                if (!FreeC(pp) || !FreeC(tgt) || redNow.Contains(pp)) continue;
                                if (pp == lord.Position + step || (pp - dc).sqrMagnitude <= 2 || (pp - lord.Position).sqrMagnitude <= 2) continue; // 主・剣兵のまねの斬撃・赤に入らない
                                if (m.Enemies.Any(e => e.IsAlive && e.IsObject && (e.Position == tgt))) continue; // 核を斬らない（空振り）
                                m.Player.Position = pp; kd = dir; put = true;
                            }
                        if (!put) sw.Position = oldSw;
                    }
                }
                if (put)
                {
                    var cs = Give("Slash");
                    if (cs != null)
                    {
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot("r79_boss_defeat_before");
                        turn.AddCommand(new CommandRecord(cs.Data, kd, cs.Data.minDistance, cs));
                        turn.ExecuteTurn();
                        for (int k = 0; k < 24; k++) { yield return new WaitForSeconds(0.25f); yield return Shot($"r79_boss_defeat_{k:00}"); }
                        yield return new WaitForSeconds(1.5f);
                        yield return Shot("r80_boss_defeat_result");
                        summary.AppendLine($"主を倒す（まねの斬撃）：主 {lord.Hp}・残りの敵 {m.Enemies.Count(e => e.IsAlive)}・状態 {turn.State}");
                    }
                }
                if (lord.IsAlive && turn.State == BattleState.PlayerInput)
                {
                lord.Hp = Mathf.Min(lord.Hp, 2);
                put = false; kd = Direction.Up;
                foreach (var dv in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                {
                    var pc = lord.Position - dv;
                    if (!m.Board.IsInside(pc) || m.Board.IsWall(pc) || m.Board.Spikes.Contains(pc)) continue;
                    var occ = m.GetUnitAt(pc);
                    if (occ != null && occ != m.Player) continue;
                    m.Player.Position = pc; kd = CopyCore.Toward(pc, lord.Position, Direction.Up); put = true; break;
                }
                var ks = put ? Give("Slash") : null;
                if (ks != null)
                {
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot("r79_boss_defeat_before");
                    turn.AddCommand(new CommandRecord(ks.Data, kd, ks.Data.minDistance, ks));
                    turn.ExecuteTurn();
                    for (int k = 0; k < 16; k++) { yield return new WaitForSeconds(0.25f); yield return Shot($"r79_boss_defeat_{k:00}"); }
                    yield return new WaitForSeconds(1.5f);
                    yield return Shot("r80_boss_defeat_result");
                    summary.AppendLine($"主を倒す：主 {lord.Hp}・残りの敵 {m.Enemies.Count(e => e.IsAlive)}・状態 {turn.State}");
                }
                else summary.AppendLine("主を倒す：隣に立てるマスが無い");
                }
            }

            // 6) 負けた画面
            yield return Room("v4_n2", 2, null);
            m = turn.Grid.Model;
            var red = Threats.Compute(m).Where(t => t.Timing == ThreatTiming.Attack).SelectMany(t => t.Cells).FirstOrDefault(c => !m.IsBlocked(c));
            m.Player.Position = red;
            m.Player.Hp = 1;
            act.Run?.SetHpForStudy(1); // 【RC11】負けの窓の「入ったときの HP」とそろえる（撮影の書き換え）
            yield return new WaitForSeconds(0.5f);
            var any = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash && turn.CanAddCard(c));
            if (any != null) turn.AddCommand(new CommandRecord(any.Data, Direction.Up, 0, any));
            turn.ExecuteTurn();
            for (int k = 0; k < 120 && turn.State != BattleState.GameOver && turn.State != BattleState.PlayerInput; k++) yield return new WaitForSeconds(0.05f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("r50_loss_result");
            act.AbandonRun();

            // 7) チュートリアル4（押してずらす：答えあり）・5（固定と、倒して消す）
            act.StartTutorial();
            while (act.TutorialSteps[act.TutorialStep].Asset != "StageTU4") act.NextTutorialStep();
            yield return new WaitForSeconds(0.8f);
            var tp = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Push);
            if (tp != null) yield return Select(tp, Direction.Up, "r60_tutorial4_answer_preview");
            act.NextTutorialStep();
            yield return new WaitForSeconds(0.8f);
            yield return Shot("r61_tutorial5_fixed_start");
            var tpull = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Pull);
            if (tpull != null) yield return Select(tpull, Direction.Up, "r62_tutorial5_pull_preview");
            if (tpull != null) turn.AddCommand(new CommandRecord(tpull.Data, Direction.Up, tpull.Data.minDistance, tpull));
            var tsl = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash && turn.CanAddCard(c));
            if (tsl != null) yield return Select(tsl, Direction.Up, "r63_tutorial5_slash_preview");
            act.SkipTutorial();

            // 8) 【RC11】5分体験：MISSION の窓 → 1〜2問目（答えが見える）→ 3〜5問目（パズル：本当の解き方で撮る）
            act.StartQuick();
            yield return new WaitForSeconds(0.8f);
            yield return Shot("r67_quick_intro"); // 【RC12】入口：何のゲームか（絵）
            act.CloseQuickIntro();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("r68_quick_mission1");
            act.StartMission();
            yield return new WaitForSeconds(0.3f);
            var qs = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
            if (qs != null) { turn.AddCommand(new CommandRecord(qs.Data, Direction.Left, qs.Data.minDistance, qs)); yield return RunTurn("r69_quick1_copy"); } // 見どころ：まねで同士討ち
            yield return new WaitForSeconds(1.6f); yield return Shot("r69c_quick1_done"); // 【RC12・初見テスト「1問目が成功したか分からない」】できた！の窓
            act.NextTutorialStep(); // 【通常リリース版】5分体験は3問（ずらす・赤と紫は外した）
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r71_quick3_mission");
            act.StartMission();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("r72_quick3_start");
            act.ShowTutorialHint(); // 【RC11】ヒントの一言がルールの行を隠さないか
            yield return new WaitForSeconds(0.2f);
            yield return Shot("r72b_quick3_hint");
            act.RestartTutorialStep(); act.StartMission(); // ヒントを消してから解く（見どころの絵にヒントが写らないように）
            yield return new WaitForSeconds(0.6f);
            IEnumerator Plan(string tag, params (string t, Direction d)[] cmds) // 【RC11】札の名前で選ぶ（駆け足と突進は同じ CommandType.Rush）
            {
                foreach (var (t, d) in cmds)
                {
                    var ci = turn.Deck.Hand.FirstOrDefault(c => c.Data.name == t && turn.CanAddCard(c));
                    if (ci == null) { summary.AppendLine($"{tag}：{t} が手札に無い"); yield break; }
                    turn.AddCommand(new CommandRecord(ci.Data, d, ci.Data.minDistance, ci));
                }
                yield return RunTurn(tag);
            }
            yield return Plan("r73_quick3_solve", ("Dash", Direction.Up), ("Walk", Direction.Down), ("Walk", Direction.Left)); // 【通常リリース版】3問目＝落石へ送る（QM8） // 【RC12】軽くした3問目（QM11：引き寄せなし） // 【RC11】作り直した3問目（QM6）
            summary.AppendLine($"5分体験3：{(act.TutorialStepDone ? "解けた" : "解けない")}");
            yield return new WaitForSeconds(1.6f); yield return Shot("r73c_quick3_done"); // 【RC12】できた！の窓（答えを見たか・きれいな解か）
            act.NextTutorialStep(); act.StartMission();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r74_quick4_start");
            act.ShowTutorialHint(); act.ShowTutorialHint(); act.ShowTutorialHint(); act.ShowAnswer(); // 【RC12】答えを見る：はじめからにして、次の札が光る
            yield return new WaitForSeconds(1.0f); // 【RC12・初見テスト「札の並びがずれて重なる」】配り直しの動きが終わってから
            yield return Shot("r74b_quick4_answer");
            yield return Plan("r75_quick4_t1", ("Slash", Direction.Up), ("Slash", Direction.Up), ("Walk", Direction.Left)); // 【通常リリース版】4問目＝まねの連鎖（QM10） // 【RC11】作り直した4問目（QM8・1ターン）
            summary.AppendLine($"5分体験4：{(act.TutorialStepDone ? "解けた" : "解けない")}");
            yield return new WaitForSeconds(1.6f); yield return Shot("r75c_quick4_done"); // 【RC12】できた！の窓（答えを見たか・きれいな解か）
            act.NextTutorialStep();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r78_quick_done");
            act.OpenTitle();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r80_title");
            // 【RC10】タイトルの並びの比べ（0＝RC9・1＝案A・2＝案B）
            int keepLayout = ActUI.TitleLayout;
            for (int L = 0; L <= 2; L++) { ActUI.TitleLayout = L; yield return null; yield return null; yield return Shot($"r81_title_layout{L}"); }
            ActUI.TitleLayout = keepLayout;

            File.WriteAllText(Path.Combine(folder, "rc9_summary.txt"), summary.ToString());
            BuildInfo.SetForTest(null);
        }
    }
}
