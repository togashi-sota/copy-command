using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証中】「中央の同じ数マスで殴り続ける」問題の解析と、次期検証セット（M4〜M10・M6b・MB1）の安全確認。
    /// ゲーム本体と同じルール（TurnRules＝コピー → 兵種の行動 → 環境）で、固定の手札（各カード1ターン1回）として調べる。
    /// 指標：
    ///  - 最短ルート／簡易ボットのプレイで、プレイヤー・敵が入ったマスの数と盤面（床）に占める割合、
    ///    プレイヤーが同じ位置でラウンドを終えた最長の連続数
    ///  - 中央3×3から一度も出ずにクリアできるか（できるなら「中央で殴り続ける」で勝てる）
    ///  - 危険な簡単攻略：1枚の繰り返しで勝てる／盤面も HP も変わらない安全な待機がある（＝自然な時間圧力が無い）／
    ///    あなたが直接攻撃しないのに敵が倒れる（1つの命令をくり返すだけで）
    /// 結果は Logs/board_usage_report.txt。バッチ：-executeMethod CopyCommand.EditorTools.BoardUsageStudy.Run
    /// </summary>
    public static class BoardUsageStudy
    {
        const int MaxStates = 400;
        const int MaxTurns = 8;

        static readonly string[] StageNames =
        {
            "StageM1", "StageM2", "StageM3",
            "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageMB1",
        };

        [MenuItem("CopyCommand/解析：盤面の使い方（次期検証セット）")]
        public static void Run() => RunFor(StageNames);

        /// <summary>-studystages M4,M5 のように、一部だけ解析する（盤面の調整用）。</summary>
        public static void RunSelected()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-studystages");
            var names = i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',').Select(n => "Stage" + n.Trim()).ToArray() : StageNames;
            RunFor(names);
        }

        static void RunFor(IEnumerable<string> names)
        {
            var sb = new StringBuilder();
            sb.AppendLine("【検証中】盤面の使い方と、簡単すぎる攻略の確認（固定の手札・同じカードは1ターン1回・最大3命令）");
            sb.AppendLine("・使用マス＝プレイヤー（敵）が入ったマスの数／盤面の床の数。中央3×3＝盤面の中心の3×3マス");
            sb.AppendLine();
            foreach (var name in names)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                if (stage == null) { sb.AppendLine($"=== {name}：見つからない"); continue; }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Analyze(sb, stage);
                sb.AppendLine($"    （解析 {watch.Elapsed.TotalSeconds:0} 秒）");
                sb.AppendLine();
                File.WriteAllText("Logs/board_usage_report.txt", sb.ToString(), Encoding.UTF8);
                Debug.Log($"[BoardUsageStudy] {name} 完了");
            }
            Debug.Log("[BoardUsageStudy] Logs/board_usage_report.txt に書き出しました");
        }

        static void Analyze(StringBuilder sb, StageData stage)
        {
            var start = BattleModel.FromStage(stage);
            var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = stage.maxSlots });
            int floor = 0;
            for (int x = 0; x < start.Board.Width; x++)
                for (int y = 0; y < start.Board.Height; y++)
                    if (!start.Board.IsWall(new Vector2Int(x, y))) floor++;
            var center = CenterCells(start.Board);

            sb.AppendLine($"=== {stage.name} {stage.stageTitle}");
            foreach (var row in stage.GetMapRows()) sb.AppendLine("    " + row);
            sb.AppendLine($"    敵 {string.Join("・", start.Enemies.Select(e => $"{e.Name}={PhaseRules.KindName(e.Kind)}(向き{e.Facing.ToLabel()})"))}／HP あなた{start.Player.MaxHp} 敵{start.Enemies.First().MaxHp}" +
                          (start.Environment.HasFire ? $"／炎床 {start.Environment.FirePatterns.Count} パターン循環" : "") +
                          (start.Environment.HasRune ? $"／回復ルーン {start.Environment.RuneCandidates.Count} 地点・{start.Environment.RuneUsesLeft} 回" : ""));

            sb.AppendLine("  " + SolverRunner.ThreatProfile(start, new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = stage.maxSlots }));
            var best = Search(start, seqs, s => true, 1);
            var clean = Search(start, seqs, s => true, start.Player.MaxHp);
            var centerOnly = Search(start, seqs, cells => cells.All(center.Contains), 1);
            sb.AppendLine($"  最短：{Show(best)} ／ 無傷：{Show(clean)} ／ 中央3×3から出ずにクリア：{Show(centerOnly)}");
            if (best.path != null)
            {
                var u = Usage(start, best.path, floor);
                sb.AppendLine($"  最短ルートの盤面の使い方：{u}");
                foreach (var line in BattleSolver.Trace(start, best.path, null)) sb.AppendLine("      " + line);
            }

            var bot = BotPlay(start, stage, seqs, floor);
            sb.AppendLine($"  簡易ボット（1ターン先読み）：{bot}");
            // 長い戦闘（敵 HP ×2）でも中央に留まるか（「中央で殴り続ける」は長い戦闘で目立つため）
            var longStage = Object.Instantiate(stage);
            longStage.enemyMaxHp = stage.enemyMaxHp * 2;
            var longStart = BattleModel.FromStage(longStage);
            Object.DestroyImmediate(longStage);
            sb.AppendLine($"  簡易ボット（敵 HP ×2）：{BotPlay(longStart, stage, seqs, floor)}");
            sb.AppendLine("  " + Exploits(start, seqs));
        }

        static HashSet<Vector2Int> CenterCells(BoardModel board)
        {
            int cx = (board.Width - 1) / 2, cy = (board.Height - 1) / 2;
            var set = new HashSet<Vector2Int>();
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    set.Add(new Vector2Int(x, y));
            return set;
        }

        struct Result { public int turns, hp; public bool approx; public List<List<CommandRecord>> path; }

        static string Show(Result r) => r.path == null ? "見つからず" + (r.approx ? "（近似）" : "") : $"{r.turns}T（残りHP {r.hp}）{(r.approx ? "（近似）" : "")}";

        /// <summary>幅優先（ビーム）探索。playerCellsOk：そのラウンドにプレイヤーがいた全マス（入ったマス＋最初の位置）を見て、許すか決める。</summary>
        static Result Search(BattleModel start, List<List<CommandRecord>> seqs, System.Func<List<Vector2Int>, bool> playerCellsOk, int minHp)
        {
            var frontier = new List<(BattleModel m, List<List<CommandRecord>> path)> { (start.Clone(), new List<List<CommandRecord>>()) };
            var seen = new HashSet<string> { BattleSolver.Key(start) };
            bool approx = false;
            for (int turn = 1; turn <= MaxTurns; turn++)
            {
                Result best = default;
                var next = new List<(BattleModel, List<List<CommandRecord>>)>();
                foreach (var (model, path) in frontier)
                    foreach (var seq in seqs)
                    {
                        var m = model.Clone();
                        var events = new List<BattleEvent>();
                        var outcome = TurnRules.PlayTurn(m, seq, null, events);
                        if (outcome == TurnOutcome.GameOver || m.Player.Hp < minHp) continue;
                        var cells = events.Where(e => e.Type == BattleEventType.Moved && e.Unit.IsPlayer).Select(e => e.To).ToList();
                        cells.Add(model.Player.Position);
                        if (!playerCellsOk(cells)) continue;
                        var np = new List<List<CommandRecord>>(path) { seq };
                        if (outcome == TurnOutcome.Cleared)
                        {
                            if (best.path == null || m.Player.Hp > best.hp) best = new Result { turns = turn, hp = m.Player.Hp, path = np, approx = approx };
                            continue;
                        }
                        if (seen.Add(BattleSolver.Key(m))) next.Add((m, np));
                    }
                if (best.path != null) return best;
                if (next.Count == 0) break;
                if (next.Count > MaxStates)
                {
                    approx = true;
                    next = next.OrderBy(x => StagnationRule.EnemyHpTotal(x.Item1) * 100 - x.Item1.Player.Hp * 10).Take(MaxStates).ToList();
                }
                frontier = next;
            }
            return new Result { approx = approx };
        }

        /// <summary>手順を再生して、盤面の使い方を文字にする。</summary>
        static string Usage(BattleModel start, List<List<CommandRecord>> path, int floor)
        {
            var m = start.Clone();
            var player = new HashSet<Vector2Int> { m.Player.Position };
            var enemy = new HashSet<Vector2Int>(m.Enemies.Select(e => e.Position));
            int stay = 0, maxStay = 0;
            int ff = 0, trait = 0, fire = 0, heal = 0;
            foreach (var turn in path)
            {
                var before = m.Player.Position;
                var events = new List<BattleEvent>();
                var outcome = TurnRules.PlayTurn(m, turn, null, events);
                Collect(events, player, enemy, ref ff, ref trait, ref fire, ref heal);
                stay = m.Player.Position == before ? stay + 1 : 0;
                maxStay = Mathf.Max(maxStay, stay);
                if (outcome != TurnOutcome.Continue) break;
            }
            return $"あなた {player.Count} マス（{100.0 * player.Count / floor:0}%）・敵 {enemy.Count} マス（{100.0 * enemy.Count / floor:0}%）・同じ位置で終えた最長 {maxStay} ラウンド・同士討ち {ff}・矢の命中 {trait}・炎の命中 {fire}・回復 {heal}";
        }

        static void Collect(List<BattleEvent> events, HashSet<Vector2Int> player, HashSet<Vector2Int> enemy, ref int ff, ref int trait, ref int fire, ref int heal)
        {
            foreach (var e in events)
            {
                if (e.Type == BattleEventType.Moved) (e.Unit.IsPlayer ? player : enemy).Add(e.To);
                if (e.Type == BattleEventType.Healed) heal++;
                if (e.Type != BattleEventType.Damaged) continue;
                if (e.Cause.Kind == DamageKind.Fire) fire++;
                else if (e.Cause.Kind == DamageKind.Trait) trait++;
                else if (e.Cause.Kind == DamageKind.Direct && !e.Unit.IsPlayer && e.Cause.Source != null && !e.Cause.Source.IsPlayer) ff++;
            }
        }

        /// <summary>簡易ボット（毎ターン、全手順を試して1ターン後の盤面がいちばん良いものを選ぶ）で最大15ターン遊ばせる。</summary>
        static string BotPlay(BattleModel start, StageData stage, List<List<CommandRecord>> seqs, int floor)
        {
            var m = start.Clone();
            var player = new HashSet<Vector2Int> { m.Player.Position };
            var enemy = new HashSet<Vector2Int>(m.Enemies.Select(e => e.Position));
            int stay = 0, maxStay = 0, ff = 0, trait = 0, fire = 0, heal = 0, turns = 0;
            var center = CenterCells(m.Board);
            int centerEnds = 0;
            var outcome = TurnOutcome.Continue;
            while (outcome == TurnOutcome.Continue && turns < 15)
            {
                turns++;
                List<CommandRecord> bestSeq = null;
                double bestScore = double.MinValue;
                foreach (var seq in seqs)
                {
                    var c = m.Clone();
                    var o = TurnRules.PlayTurn(c, seq, null);
                    double score = o == TurnOutcome.GameOver ? -10000 : 0;
                    if (o == TurnOutcome.Cleared) score += 1000;
                    score += (StagnationRule.EnemyHpTotal(m) - StagnationRule.EnemyHpTotal(c)) * 10 - (m.Player.Hp - c.Player.Hp) * 12;
                    var alive = c.Enemies.Where(e => e.IsAlive).ToList();
                    if (alive.Count > 0) score -= 0.5 * alive.Min(e => Mathf.Abs(e.Position.x - c.Player.Position.x) + Mathf.Abs(e.Position.y - c.Player.Position.y));
                    score -= 0.2 * seq.Count;
                    if (score > bestScore) { bestScore = score; bestSeq = seq; }
                }
                var before = m.Player.Position;
                var events = new List<BattleEvent>();
                outcome = TurnRules.PlayTurn(m, bestSeq, null, events);
                Collect(events, player, enemy, ref ff, ref trait, ref fire, ref heal);
                stay = m.Player.Position == before ? stay + 1 : 0;
                maxStay = Mathf.Max(maxStay, stay);
                if (center.Contains(m.Player.Position)) centerEnds++;
            }
            string result = outcome == TurnOutcome.Cleared ? $"{turns}T でクリア（残りHP {m.Player.Hp}）" : outcome == TurnOutcome.GameOver ? $"{turns}T で敗北" : "15T で終わらず";
            return $"{result}／あなた {player.Count} マス（{100.0 * player.Count / floor:0}%）・敵 {enemy.Count} マス（{100.0 * enemy.Count / floor:0}%）・" +
                   $"ラウンド終了時に中央3×3にいた {centerEnds}/{turns}・同じ位置の最長 {maxStay}・同士討ち {ff}・矢の命中 {trait}・炎の命中 {fire}・回復 {heal}";
        }

        /// <summary>危険な簡単攻略の確認。</summary>
        static string Exploits(BattleModel start, List<List<CommandRecord>> seqs)
        {
            // 1枚の繰り返しで勝てるか／あなたが直接攻撃しないのに敵が倒れるか
            var singleWins = new List<string>();
            var noDirectKills = new List<string>();
            foreach (var seq in seqs.Where(q => q.Count == 1))
            {
                var m = start.Clone();
                bool direct = false;
                for (int t = 0; t < 10; t++)
                {
                    var events = new List<BattleEvent>();
                    var o = TurnRules.PlayTurn(m, seq, null, events);
                    direct |= events.Any(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer && e.Cause.Source != null && e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct);
                    if (o == TurnOutcome.GameOver) break;
                    if (o == TurnOutcome.Cleared) { singleWins.Add($"{seq[0].ToDisplayText()}×{t + 1}"); break; }
                }
                if (!direct && m.Enemies.Any(e => !e.IsAlive)) noDirectKills.Add(seq[0].ToDisplayText());
            }
            // 安全な待機：同じ手順を6ラウンド続けても、誰の HP も変わらない手順があるか（＝自然な時間圧力が無い）
            int safeStalls = 0;
            string example = null;
            foreach (var seq in seqs)
            {
                var m = start.Clone();
                int php = m.Player.Hp, ehp = StagnationRule.EnemyHpTotal(m);
                bool ok = true;
                for (int t = 0; t < 6 && ok; t++)
                {
                    var o = TurnRules.PlayTurn(m, seq, null);
                    ok = o == TurnOutcome.Continue && m.Player.Hp == php && StagnationRule.EnemyHpTotal(m) == ehp;
                }
                if (!ok) continue;
                safeStalls++;
                example = example ?? string.Join(" / ", seq.Select(c => c.ToDisplayText()));
            }
            return $"1枚の繰り返しで勝てる：{(singleWins.Count == 0 ? "なし" : string.Join("、", singleWins))}" +
                   $"／直接攻撃なしで敵が倒れる1枚：{(noDirectKills.Count == 0 ? "なし" : string.Join("、", noDirectKills))}" +
                   $"／6ラウンド誰も傷つかない待機：{(safeStalls == 0 ? "なし（自然な時間圧力あり）" : $"{safeStalls} 通り（例：{example}）")}";
        }
    }
}
