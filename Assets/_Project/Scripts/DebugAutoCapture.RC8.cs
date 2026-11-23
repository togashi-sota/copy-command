using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC8・Combat UI V2】-capturerc8 1：新しい戦闘画面の見直し用の場面（勝敗は強制しない・開発版だけ）。
    /// 中ボス（獣王：本人のスクショと同じ部屋）の始め／敵にマウス／順番の帯にマウス／押してずらす予告／倒して消す予告／実行中の段（①②③④⑤）／
    /// 落石・炎・レーザー／ボスの段階1・2・3（段階の帯）／負けた画面（最後のターンに受けた）。-capturelayout 0 で RC7 の配置（比べ用）。
    /// </summary>
    public partial class DebugAutoCapture
    {
        IEnumerator CaptureRC8(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var fCmd = typeof(BattleUI).GetField("pendingCommand", flags);
            var fCard = typeof(BattleUI).GetField("pendingCard", flags);
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"画面 {Screen.width}x{Screen.height}・配置 {BattleUI.LayoutVariant}");
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
                yield return Shot(shot);
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

            // 1) 獣王（本人のスクショ1と同じ部屋）
            yield return Room("v4_m3", 3, "c01_midboss_start");
            var m = turn.Grid.Model;
            summary.AppendLine($"獣王：危険 {Threats.Compute(m).Count}・重なり最大 {Threats.MaxStack(Threats.Compute(m))}");
            var foe = m.Enemies.FirstOrDefault(e => e.IsAlive && !e.IsObject && e.Kind != EnemyKind.BeastKing) ?? m.Enemies.First(e => e.IsAlive);
            ui.DebugHoverEnemyId = foe.Id; yield return null; yield return null;
            yield return Shot("c02_hover_enemy");
            ui.DebugHoverEnemyId = -1;
            ui.DebugHoverStep = 1; yield return null; yield return null;
            yield return Shot("c03_hover_step_attack");
            ui.DebugHoverStep = 3; yield return null; yield return null;
            yield return Shot("c03b_hover_step_big");
            ui.DebugHoverStep = -1;

            // 2) 押してずらす：一緒に動く赤を持つ敵の隣（その赤の中）にあなたを置き、押し斬りでその敵を押す
            yield return Room("v4_n1", 1, "c10_normal_start");
            m = turn.Grid.Model;
            var swordsman = m.Enemies.FirstOrDefault(e => e.IsAlive && Threats.Follows(e));
            if (swordsman != null)
            {
                foreach (var d in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
                {
                    var spot = swordsman.Position - d.ToOffset(); // 敵の反対側からその向きへ押す
                    if (!m.Board.IsInside(spot) || m.IsBlocked(spot)) continue;
                    var beyond = swordsman.Position + d.ToOffset();
                    if (!m.Board.IsInside(beyond) || m.IsBlocked(beyond)) continue;
                    m.Player.Position = spot;
                    yield return new WaitForSeconds(0.5f);
                    var push = Give("PushSlash") ?? Give("Push");
                    if (push != null) yield return Select(push, d, "c11_preview_push_shift");
                    summary.AppendLine($"ずらす：{PhaseRules.ShortName(swordsman)}（{swordsman.Position}）を {d} へ・変化 {Threats.Diff(Threats.Compute(m), Threats.Compute(PlayerPreview.AfterPlanned(m, new List<CommandRecord> { new CommandRecord(push.Data, d, 0, push) }))).Count}");
                    // 2b) 向け直し：押した先の隣に別の敵を置く → 押した剣兵の赤がその敵にかかる（実行して、②で同士討ち）
                    var other = m.Enemies.FirstOrDefault(e => e.IsAlive && e != swordsman && !e.IsObject);
                    var near = swordsman.Position + d.ToOffset() * 2;
                    if (other != null && m.Board.IsInside(near) && !m.IsBlocked(near))
                    {
                        var keepOther = other.Position;
                        other.Position = near;
                        yield return new WaitForSeconds(0.5f);
                        if (push != null) yield return Select(push, d, "c13_preview_push_redirect");
                        turn.AddCommand(new CommandRecord(push.Data, d, 0, push));
                        turn.ExecuteTurn();
                        var seenR = new HashSet<TurnManager.TurnStep>();
                        for (int k = 0; k < 80 && turn.State != BattleState.PlayerInput && turn.State != BattleState.GameOver && turn.State != BattleState.Cleared; k++)
                        {
                            if (!seenR.Contains(turn.Step)) { seenR.Add(turn.Step); yield return new WaitForSeconds(0.15f); yield return Shot($"c14_redirect_exec_{(int)turn.Step}_{turn.Step}"); }
                            yield return new WaitForSeconds(0.05f);
                        }
                        summary.AppendLine($"向け直し：{PhaseRules.ShortName(other)} HP {other.Hp}（同士討ち）");
                        yield return Room("v4_n1", 1, "c10b_normal_start_again");
                        m = turn.Grid.Model;
                        swordsman = m.Enemies.First(e => e.IsAlive && Threats.Follows(e));
                        m.Player.Position = swordsman.Position - d.ToOffset();
                        yield return new WaitForSeconds(0.4f);
                    }
                    // 3) 倒して消す：同じ敵の HP を減らして斬撃で
                    swordsman.Hp = 2;
                    yield return new WaitForSeconds(0.3f);
                    var slash = Give("Slash");
                    if (slash != null) yield return Select(slash, d, "c12_preview_kill_gone");
                    break;
                }
            }

            // 4) 実行中の段：押し斬り → 何もしない、を実行して段ごとに撮る
            yield return Room("v4_n4", 2, "c20_chase_start");
            m = turn.Grid.Model;
            var a1 = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
            if (a1 != null) turn.AddCommand(new CommandRecord(a1.Data, Direction.Up, 0, a1));
            var w1 = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk && turn.CanAddCard(c));
            if (w1 != null) turn.AddCommand(new CommandRecord(w1.Data, Direction.Left, w1.Data.minDistance, w1));
            yield return null; yield return null;
            yield return Shot("c21_planned");
            turn.ExecuteTurn();
            var seen = new HashSet<TurnManager.TurnStep>();
            for (int k = 0; k < 80 && turn.State != BattleState.PlayerInput && turn.State != BattleState.GameOver && turn.State != BattleState.Cleared; k++)
            {
                if (!seen.Contains(turn.Step)) { seen.Add(turn.Step); yield return new WaitForSeconds(0.12f); yield return Shot($"c22_exec_{(int)turn.Step}_{turn.Step}"); }
                yield return new WaitForSeconds(0.05f);
            }
            summary.AppendLine($"実行中に見た段：{string.Join("・", seen)}");

            // 5) 床の仕掛け
            yield return Room("v4_t2", 3, "c30_rockfall");
            yield return Room("v4_t1", 2, "c31_fire");
            yield return Room("v4_t3", 3, "c32_laser");

            // 6) ボス：段階1 → 2 → 3
            yield return Room("v4_boss", 7, "c40_boss_phase1");
            m = turn.Grid.Model;
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.MirrorLord);
            summary.AppendLine($"ボス 段階1：重なり最大 {Threats.MaxStack(Threats.Compute(m))}");
            CombatBoss.Damage(m, lord, lord.Hp - CombatBoss.Floor(lord, 0), m.Player, null, false, DamageKind.Direct, new List<BattleEvent>());
            foreach (var cl in m.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive)) { cl.Dormant = false; cl.WakingUp = false; } // ラウンドの終わりに目覚める分を先に
            yield return new WaitForSeconds(0.6f);
            yield return Shot("c41_boss_phase2_banner");
            yield return new WaitForSeconds(3.2f);
            yield return Shot("c42_boss_phase2");
            CombatBoss.Damage(m, lord, lord.Hp - CombatBoss.Floor(lord, 1), m.Player, null, false, DamageKind.Direct, new List<BattleEvent>());
            yield return new WaitForSeconds(3.6f);
            yield return Shot("c43_boss_phase3");

            // 7) 負けた画面：HP 1 で赤の中に立って、何もしないで1ターン
            yield return Room("v4_n2", 2, "c50_before_loss");
            m = turn.Grid.Model;
            var red = Threats.Compute(m).Where(t => t.Timing == ThreatTiming.Attack).SelectMany(t => t.Cells).FirstOrDefault(c => !m.IsBlocked(c));
            m.Player.Position = red;
            m.Player.Hp = 1;
            yield return new WaitForSeconds(0.5f);
            var any = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash && turn.CanAddCard(c));
            if (any != null) turn.AddCommand(new CommandRecord(any.Data, Direction.Up, 0, any));
            turn.ExecuteTurn();
            for (int k = 0; k < 120 && turn.State != BattleState.GameOver && turn.State != BattleState.PlayerInput; k++) yield return new WaitForSeconds(0.05f);
            yield return new WaitForSeconds(0.5f);
            yield return Shot("c51_loss_result");
            summary.AppendLine($"負けの画面：{turn.State}");

            act.AbandonRun();
            File.WriteAllText(Path.Combine(folder, "rc8_summary.txt"), summary.ToString());
            BuildInfo.SetForTest(null);
        }
    }
}
