using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】-capturefestival 1：文化祭クエストの画面一式
    ///   （入口・スタッフ用の集計・各 LEVEL の開始の窓・お手本 → 最初からやり直す・きれいな解で攻略・ターンごとの一言・CLEAR!・結果・倒れた窓・タイトルの入口）。
    /// -capturefestival 2：でたらめ通し（でたらめな命令・ヒント・最初からやり直す・お手本の切り替え・倒れた → もう一度・やめる・次の人へ）＝例外が出ないか。
    /// 撮影では集計を書かない（FestivalStats.Enabled＝false）。1ターンの実行にかかる秒（演出）も残す（目安の時間の見積もり用）。
    /// </summary>
    public partial class DebugAutoCapture
    {
        IEnumerator CaptureFestival(TurnManager turn, bool smoke)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"画面 {Screen.width}x{Screen.height}");
            FestivalStats.Enabled = false;
            ScreenFx.Disabled = false;
            var execSeconds = new List<float>();
            // 【2026-11-14 チラシ】-captureevery で実行中を 0.4 秒ごとに全部撮る（ふだんは 1.2 秒ごと）
            int captureEvery = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-captureevery") >= 0 ? 1 : 3;

            IEnumerator RunTurn(string tag, bool shots)
            {
                float t0 = Time.unscaledTime;
                turn.ExecuteTurn();
                // 実時間で待つ（でたらめ通しは Time.timeScale を上げるので、WaitForSeconds だけだと演出の途中で次の命令を入れていた）
                for (int k = 0; Time.unscaledTime - t0 < 40f && turn.State != BattleState.PlayerInput && turn.State != BattleState.GameOver && turn.State != BattleState.Cleared; k++)
                {
                    if (shots) { yield return new WaitForSecondsRealtime(0.4f); if (k % captureEvery == (captureEvery == 1 ? 0 : 1) && turn.State != BattleState.PlayerInput && turn.State != BattleState.Cleared) yield return Shot($"{tag}_{k:00}"); }
                    else yield return null;
                }
                execSeconds.Add(Time.unscaledTime - t0);
                yield return new WaitForSecondsRealtime(shots ? 0.45f : 0.05f); // ターンの終わりの一言（同士討ち ×2！）が出る
                if (shots) yield return Shot($"{tag}_banner");
            }
            bool Plan(string turnText)
            {
                foreach (var part in turnText.Split(','))
                {
                    var kv = part.Trim().Split(':');
                    if (kv.Length < 2) continue;
                    var d = kv[1][0] == 'U' ? Direction.Up : kv[1][0] == 'D' ? Direction.Down : kv[1][0] == 'L' ? Direction.Left : Direction.Right;
                    var ci = turn.Deck.Hand.FirstOrDefault(c => c.Data.name == kv[0] && turn.CanAddCard(c));
                    if (ci == null) { summary.AppendLine($"  {kv[0]} が手札に無い"); return false; }
                    turn.AddCommand(new CommandRecord(ci.Data, d, ci.Data.minDistance, ci));
                }
                return true;
            }

            if (!smoke)
            {
                // 入口・スタッフ用（F9）
                act.OpenFestival();
                yield return new WaitForSeconds(0.8f);
                yield return Shot("q00_entry");
                var fStaff = typeof(ActUI).GetField("festivalStaffOpen", flags);
                fStaff.SetValue(actUi, true); yield return null; yield return null;
                yield return Shot("q01_staff");
                fStaff.SetValue(actUi, false);

                for (int L = 0; L < FestivalContent.Levels.Length; L++)
                {
                    var lv = FestivalContent.Levels[L];
                    var def = lv.Battle;
                    act.StartFestival(L);
                    yield return new WaitForSeconds(1.0f);
                    yield return Shot($"q{L + 1}0_brief");
                    if (L == 0)
                    {
                        act.FestivalShowIntro();
                        yield return new WaitForSeconds(0.4f);
                        yield return Shot("q10b_intro");
                        act.CloseQuickIntro();
                        // お手本：スタッフが1ターン見せる → 最初からやり直す（来場者の番）
                        act.FestivalBeginDemo();
                        yield return new WaitForSeconds(0.6f);
                        Plan(def.Solution.Split('|')[0]);
                        yield return null; yield return null;
                        yield return Shot("q10c_demo_planned");
                        yield return RunTurn("q10d_demo_t1", false);
                        yield return Shot("q10e_demo_after");
                        act.FestivalRestart();
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot("q10f_after_restart_brief");
                    }
                    act.FestivalBeginPlay();
                    yield return new WaitForSeconds(def.Boss ? 2.4f : 0.8f);
                    yield return Shot($"q{L + 1}1_start");
                    if (L == 1) { act.ShowTutorialHint(); yield return new WaitForSeconds(0.3f); yield return Shot("q21b_hint1"); }
                    var turns = def.Solution.Split('|');
                    for (int t = 0; t < turns.Length; t++)
                    {
                        if (!Plan(turns[t])) break;
                        yield return null; yield return null;
                        yield return Shot($"q{L + 1}2_t{t + 1}_planned");
                        yield return RunTurn($"q{L + 1}3_t{t + 1}", true);
                        if (act.TutorialStepDone || turn.State == BattleState.GameOver) break;
                    }
                    var f = act.Festival;
                    summary.AppendLine($"{lv.Label} {lv.Quest}：{(act.TutorialStepDone ? "クリア" : "クリアできない")}・{f.Turns}T・被ダメ {f.Damage}・敵を利用 {f.EnemyHits}（まね {f.CopyHits}・敵の攻撃 {f.FriendlyHits}・罠 {f.HazardHits}・大技 {f.BigHits}）・1ターンの最大 {f.BestTurnDamage}");
                    yield return new WaitForSeconds(1.8f);
                    yield return Shot($"q{L + 1}4_clear");
                    act.NextTutorialStep();
                    yield return new WaitForSeconds(1.6f);
                    yield return Shot($"q{L + 1}5_result");
                    summary.AppendLine($"{lv.Label}：ランク {act.FestivalRankLetter}{(act.FestivalPerfect ? "・PERFECT" : "")}・{FestivalRank.Time(f.Seconds)}");
                }
                // 倒れた窓（HP を 0 にして見せる）
                act.StartFestival(1);
                yield return new WaitForSeconds(0.6f);
                act.FestivalBeginPlay();
                yield return new WaitForSeconds(0.6f);
                turn.Grid.Model.Player.Hp = 0;
                typeof(ActRunManager).GetMethod("FestivalGameOver", flags).Invoke(act, null);
                yield return new WaitForSeconds(0.5f);
                yield return Shot("q80_gameover");
                act.FestivalRetryAfterGameOver();
                yield return new WaitForSeconds(0.6f);
                yield return Shot("q81_retry");
                act.FestivalNextPerson();
                yield return new WaitForSeconds(0.6f);
                yield return Shot("q90_next_person");
                act.FestivalToMain();
                yield return new WaitForSeconds(0.6f);
                yield return Shot("q91_title_with_festival");
            }
            else
            {
                // でたらめ通し：LEVEL ごとに数回。でたらめな命令（向きも）・ときどきヒント・最初からやり直す・お手本・やめる。例外は Application.logMessageReceived で数える
                int errors = 0;
                Application.LogCallback cb = (msg, st, type) => { if (type == LogType.Exception || type == LogType.Error) errors++; };
                Application.logMessageReceived += cb;
                var rng = new System.Random(20261113);
                Time.timeScale = 8f; // 確認だけなので演出の待ち時間を縮める（1ターン 8 秒 × 9回 × 最大 40 ターン）
                int plays = 0, clears = 0, overs = 0, turnsPlayed = 0;
                for (int round = 0; round < 9; round++)
                {
                    int L = round % 3;
                    act.StartFestival(L);
                    yield return null;
                    if (round % 4 == 1) act.FestivalBeginDemo(); else act.FestivalBeginPlay();
                    plays++;
                    for (int guard = 0; guard < 30 && act.Screen == ActScreen.Battle && act.FestivalMode; guard++)
                    {
                        if (act.MissionOpen) { act.FestivalBeginPlay(); continue; }
                        if (act.TutorialStepDone) { yield return new WaitForSeconds(0.9f); act.NextTutorialStep(); continue; }
                        if (act.FestivalGameOverOpen) { overs++; if (rng.NextDouble() < 0.5) act.FestivalRetryAfterGameOver(); else { act.FestivalOtherLevel(); break; } continue; }
                        if (round == 8 && guard == 5) { act.QuitFestivalBattle(); break; } // やめる
                        if (rng.NextDouble() < 0.1) act.ShowTutorialHint();
                        if (rng.NextDouble() < 0.04) { act.FestivalRestart(); continue; }
                        if (rng.NextDouble() < 0.03) act.FestivalToggleDemo();
                        // 3回に1回は きれいな解のそのターン（先へ進む）・ほかは でたらめ
                        var sol = act.FestivalDef.Battle.Solution.Split('|');
                        int ti = turn.TurnNumber - 1;
                        if (rng.NextDouble() < 0.35 && ti < sol.Length) Plan(sol[ti]);
                        else
                        {
                            int n = 1 + rng.Next(3);
                            for (int k = 0; k < n; k++)
                            {
                                var hand = turn.Deck.Hand.Where(c => turn.CanAddCard(c)).ToList();
                                if (hand.Count == 0) break;
                                var ci = hand[rng.Next(hand.Count)];
                                turn.AddCommand(new CommandRecord(ci.Data, (Direction)rng.Next(4), ci.Data.minDistance, ci));
                            }
                        }
                        yield return RunTurn("smoke", false);
                        turnsPlayed++;
                    }
                    if (act.Screen == ActScreen.FestivalResult) clears++;
                    yield return new WaitForSeconds(0.3f);
                    if (round % 3 == 0) act.FestivalNextPerson(); else if (act.Screen == ActScreen.FestivalResult) act.FestivalAgain(); else act.OpenFestival();
                    yield return null;
                }
                Application.logMessageReceived -= cb;
                Time.timeScale = 1f;
                summary.AppendLine($"でたらめ通し：{plays}回・{turnsPlayed}ターン（結果まで {clears}・倒れた {overs}）・例外とエラー {errors}");
            }
            if (execSeconds.Count > 0) summary.AppendLine($"1ターンの実行（演出）：平均 {execSeconds.Average():0.0}秒・最大 {execSeconds.Max():0.0}秒（{execSeconds.Count}ターン）");
            File.WriteAllText(Path.Combine(folder, smoke ? "festival_smoke.txt" : "festival_summary.txt"), summary.ToString());
            FestivalStats.Enabled = true;
        }
    }
}
