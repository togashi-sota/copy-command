using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証用】鏡蝕ゲージの解析（固定の手札で全手順を探索。ゲーム本体と同じ RulesEngine / TurnRules / StagnationRule）。
    /// 各ステージ × ゲージの最大（1〜4）について：
    ///  - 呪いを一度も受けずにクリアできる最短ターン（＝上手なプレイが罰せられないか）
    ///  - 呪いありでの最短
    ///  - 今のルール（鏡蝕なし）の最短ルートで、敵の HP が減らないラウンドが最大何回続くか
    /// 結果は Logs/stagnation_report.txt。バッチ：-executeMethod CopyCommand.EditorTools.StagnationStudy.Run
    /// </summary>
    public static class StagnationStudy
    {
        const int MaxStates = 600;
        static readonly int[] Penalties = { 2, 3, 4 };

        [MenuItem("CopyCommand/解析：鏡蝕ゲージ")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("【検証用】鏡蝕ゲージの解析（固定の手札・全手順の探索。敵が2体のステージは状態が多いので近似あり）");
            sb.AppendLine($"呪い：ゲージが満ちた後、敵の HP を減らせなかったラウンドごとに {string.Join("→", Penalties)}（最後の値をくり返す）");
            sb.AppendLine("待機だけを続けた場合（HP 12）：ゲージ最大 K なら K ラウンドは無傷、その後 2+3+4+4=13 で4ラウンド目に倒れる → K+4 ラウンドで GAME OVER");
            sb.AppendLine();
            foreach (var name in new[] { "Stage2", "Stage3", "StageM1", "StageM2", "StageM3" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = stage.maxSlots });
                sb.AppendLine($"=== {name} {stage.stageTitle}");
                var plain = Search(stage, seqs, 0, false);
                sb.AppendLine($"  鏡蝕なし：最短 {Show(plain)}／最短ルートで敵の HP が減らないラウンドの最長連続 {LongestStall(stage, plain.path)} 回");
                foreach (int max in new[] { 1, 2, 3, 4 })
                {
                    var noCurse = Search(stage, seqs, max, true);
                    var withCurse = Search(stage, seqs, max, false);
                    sb.AppendLine($"  ゲージ最大 {max}：呪いなしでクリア {Show(noCurse)} ／ 呪いありの最短 {Show(withCurse)}");
                }
                sb.AppendLine();
                Debug.Log($"[StagnationStudy] {name} 完了");
                File.WriteAllText("Logs/stagnation_report.txt", sb.ToString(), Encoding.UTF8);
            }
            Debug.Log("[StagnationStudy] Logs/stagnation_report.txt に書き出しました");
        }

        struct Result { public int turns, hp; public bool approx; public List<List<CommandRecord>> path; }

        static string Show(Result r) => r.path == null ? "見つからず" : $"{r.turns}T（残りHP {r.hp}）{(r.approx ? "（近似）" : "")}";

        static int LongestStall(StageData stage, List<List<CommandRecord>> path)
        {
            if (path == null) return 0;
            var m = BattleModel.FromStage(stage);
            int run = 0, best = 0;
            foreach (var turn in path)
            {
                int before = StagnationRule.EnemyHpTotal(m);
                TurnRules.PlayTurn(m, turn, null);
                run = StagnationRule.EnemyHpTotal(m) < before ? 0 : run + 1;
                best = Mathf.Max(best, run);
            }
            return best;
        }

        /// <summary>幅優先（ビーム）探索。max=0 なら鏡蝕なし。forbidCurse なら呪いを受ける手順を捨てる。</summary>
        static Result Search(StageData stage, List<List<CommandRecord>> seqs, int max, bool forbidCurse, int maxTurns = 7)
        {
            var start = BattleModel.FromStage(stage);
            var frontier = new List<(BattleModel m, StagnationRule g, List<List<CommandRecord>> path)>
            {
                (start, max > 0 ? new StagnationRule(max, Penalties) : null, new List<List<CommandRecord>>()),
            };
            var seen = new HashSet<string>();
            bool approx = false;
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                Result best = default;
                var next = new List<(BattleModel, StagnationRule, List<List<CommandRecord>>)>();
                foreach (var (model, gauge, path) in frontier)
                    foreach (var seq in seqs)
                    {
                        var m = model.Clone();
                        int before = StagnationRule.EnemyHpTotal(m);
                        var outcome = TurnRules.PlayTurn(m, seq, null);
                        if (outcome == TurnOutcome.GameOver) continue;
                        var np = new List<List<CommandRecord>>(path) { seq };
                        if (outcome == TurnOutcome.Cleared)
                        {
                            if (best.path == null || m.Player.Hp > best.hp) best = new Result { turns = turn, hp = m.Player.Hp, path = np, approx = approx };
                            continue;
                        }
                        StagnationRule g = null;
                        if (gauge != null)
                        {
                            g = gauge.Clone();
                            int penalty = g.EndRound(StagnationRule.EnemyHpTotal(m) < before);
                            if (penalty > 0)
                            {
                                if (forbidCurse) continue;
                                m.Player.TakeDamage(penalty);
                                if (!m.Player.IsAlive) continue;
                            }
                        }
                        string key = BattleSolver.Key(m) + (g == null ? "" : $"|{g.Gauge},{g.Overflows}");
                        if (seen.Add(key)) next.Add((m, g, np));
                    }
                if (best.path != null) return best;
                if (next.Count == 0) break;
                if (next.Count > MaxStates)
                {
                    approx = true;
                    next = next.OrderBy(x => StagnationRule.EnemyHpTotal(x.Item1) * 100 - x.Item1.Player.Hp * 10 + (x.Item2?.Gauge ?? 0)).Take(MaxStates).ToList();
                }
                frontier = next;
            }
            return new Result { approx = approx };
        }
    }
}
