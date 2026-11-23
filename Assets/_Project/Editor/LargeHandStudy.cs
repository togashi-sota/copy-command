using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証中】手札方式（A・B・大:3枚・大:使用+1）と新しい移動札の解析。結果は Logs/large_hand_report.txt。
    ///  第1部：同じ20枚の検証デッキ（M4 以降の大きい検証デッキ）で、手札方式ごとのカードの流れ
    ///    - 手札の枚数／手札にある移動札の種類数／攻撃札がある割合
    ///    - 「行きたいマスへ行けるか」：空の盤面で、手札の移動札を2枚まで（各1回）使って届くマスが、
    ///      自分から距離3以内の24マスのうち何マスあるか（到達率）
    ///  第2部：盤面ごとに、基本の4枚（一歩・駆け足・斬撃・押し出し）に新しい移動札を1種類ずつ足したとき、最短・中央だけの攻略がどう変わるか
    ///  第3部：系統の違う攻略が2つ以上あるか（基本4枚＋跳躍＋斜め歩）：直接攻撃だけで勝つ／直接攻撃なし（同士討ち・矢・炎・トゲ）で勝つ
    /// バッチ：-executeMethod CopyCommand.EditorTools.LargeHandStudy.Run
    /// </summary>
    public static class LargeHandStudy
    {
        const string CommandFolder = "Assets/_Project/Data/Commands";
        const int Seeds = 5000;
        const int Turns = 8;
        const int MaxStates = 80; // 250 だと1盤面に数十分かかるため、近似を強めて 80

        internal static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"{CommandFolder}/{name}.asset");

        [MenuItem("CopyCommand/解析：大きい手札と新しい移動札")]
        public static void Run()
        {
            var sb = new StringBuilder("【検証中】大きい手札と新しい移動札の解析\n\n");
            var m4 = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageM4.asset");
            Flow(sb, m4.handTestDeckLarge);
            Write(sb);
            foreach (var name in new[] { "StageM4", "StageM7", "StageM8", "StageM10", "StageMB1" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Boards(sb, stage);
                sb.AppendLine($"  （解析時間 {watch.Elapsed.TotalMinutes:0.0} 分）");
                sb.AppendLine();
                Write(sb);
                Debug.Log($"[LargeHandStudy] {name} 完了");
            }
            Debug.Log("[LargeHandStudy] Logs/large_hand_report.txt に書き出しました");
        }

        internal static void Write(StringBuilder sb, string path = "Logs/large_hand_report.txt") => File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        // ───────── 第4部：新しい移動札を全部使えるときの危険な攻略（M4 以降の全ステージ）─────────

        static readonly string[] NewStageNames = { "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageMB1" };

        /// <summary>
        /// 20枚の検証デッキに入っている全種類（一歩・駆け足・突進・可変突進・跳躍・斜め歩・滑走・転移・入れ替え・斬撃・押し出し）を使えるとき、
        /// 1枚の繰り返しで勝てるか／6ラウンド誰も傷つかない待機があるか／1ターンで削れる量／2ターン以内に勝てるか、を基本4枚と比べる。
        /// 結果は Logs/large_hand_exploits.txt。バッチ：-executeMethod CopyCommand.EditorTools.LargeHandStudy.RunExploits
        /// </summary>
        public static void RunExploits()
        {
            var sb = new StringBuilder("【検証中】新しい移動札を全部使えるときの危険な攻略（各1ターン1回・最大3命令。実際の手札は8〜10枚なので、これは「全部そろった最悪の場合」）\n\n");
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            var full = new List<CommandData>(basic);
            foreach (var n in new[] { "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" }) full.Add(Card(n));
            var basicSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = basic, MaxSlots = 3 });
            var fullSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = full, MaxSlots = 3 });
            sb.AppendLine($"手順の数：基本4枚 {basicSeqs.Count} 通り／全種類 {fullSeqs.Count} 通り\n");
            foreach (var name in NewStageNames)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var start = BattleModel.FromStage(stage);
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（敵HP合計 {StagnationRule.EnemyHpTotal(start)}・あなたHP {start.Player.Hp}）");
                foreach (var (label, seqs) in new[] { ("基本4枚", basicSeqs), ("全種類", fullSeqs) })
                {
                    sb.AppendLine($"  ○ {label}");
                    sb.AppendLine("    " + Exploits(start, seqs));
                    sb.AppendLine("    " + QuickWins(start, seqs));
                }
                sb.AppendLine();
                Debug.Log($"[LargeHandStudy.RunExploits] {name} 完了");
                Write(sb, "Logs/large_hand_exploits.txt");
            }
        }

        /// <summary>
        /// 第5部：中央3×3だけの攻略の中身（なぜ新しい移動札で中央が強くなったか）。手順を1ターンずつ再生し、
        /// あなた・敵の移動量、敵の空振り（壁・端・ユニットにぶつかった回数）、ダメージの内訳を出す。結果は Logs/center_trace.txt。
        /// バッチ：-executeMethod CopyCommand.EditorTools.LargeHandStudy.RunCenterTrace
        /// </summary>
        public static void RunCenterTrace()
        {
            var sb = new StringBuilder("【検証中】中央3×3だけの攻略の中身（基本4枚＋1種類、近似のビーム探索）\n座標は (x, y)：x＝左から0、y＝下から0。\n\n");
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            foreach (var name in new[] { "StageM4", "StageM7", "StageM8", "StageM10" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var start = BattleModel.FromStage(stage);
                var center = Center(start.Board);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（盤面 {start.Board.Width}×{start.Board.Height}、中央3×3＝{9 * 100 / (start.Board.Width * start.Board.Height)}%）");
                foreach (var extra in new[] { (string)null, "Jump", "Diagonal", "Slide", "Blink", "Swap", "VarRush", "WallKick", "Vault" })
                {
                    var cards = new List<CommandData>(basic);
                    if (extra != null) cards.Add(Card(extra));
                    var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                    var r = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                    var best = extra == "WallKick" || extra == "Vault" ? Search(start, seqs, (m, ev, cells) => true) : default;
                    sb.AppendLine($"  ○ {(extra == null ? "基本4枚だけ" : "＋" + Card(extra).displayName)}：中央だけ {Show(r)}{(best.path != null || extra == "WallKick" || extra == "Vault" ? "／最短 " + Show(best) : "")}");
                    Write(sb, "Logs/center_trace.txt");
                    if (r.path == null) continue;
                    var m = start.Clone();
                    int playerMoved = 0, enemyMoved = 0, enemyBumps = 0, playerBumps = 0;
                    for (int t = 0; t < r.path.Count; t++)
                    {
                        var events = new List<BattleEvent>();
                        TurnRules.PlayTurn(m, r.path[t], null, events);
                        foreach (var e in events)
                        {
                            int d = Mathf.Abs(e.To.x - e.From.x) + Mathf.Abs(e.To.y - e.From.y);
                            if (e.Type == BattleEventType.Moved) { if (e.Unit.IsPlayer) playerMoved += d; else enemyMoved += d; }
                            if (e.Type == BattleEventType.Bumped) { if (e.Unit.IsPlayer) playerBumps++; else enemyBumps++; }
                        }
                        var dmg = events.Where(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer).GroupBy(e => e.Cause.Kind == DamageKind.Direct ? (e.Cause.Source != null && e.Cause.Source.IsPlayer ? "あなたの攻撃" : "同士討ち") : e.Cause.Kind.ToString())
                            .Select(g => $"{g.Key}{g.Sum(e => e.Amount)}");
                        sb.AppendLine($"     T{t + 1}：{Seq(r.path[t])}");
                        sb.AppendLine($"         → あなた {m.Player.Position} HP{m.Player.Hp}／敵 {string.Join("・", m.Enemies.Select(e => $"{e.Name}{e.Position} HP{e.Hp}"))}／敵へのダメージ：{string.Join("・", dmg)}");
                    }
                    sb.AppendLine($"     合計：あなたの移動 {playerMoved} マス・空振り {playerBumps} 回／敵の移動 {enemyMoved} マス・空振り {enemyBumps} 回");
                }
                sb.AppendLine($"  （解析時間 {watch.Elapsed.TotalMinutes:0.0} 分）");
                sb.AppendLine();
                Write(sb, "Logs/center_trace.txt");
                Debug.Log($"[LargeHandStudy.RunCenterTrace] {name} 完了");
            }
        }

        /// <summary>第4部で見つかった「移動だけで大きく削れる手順」を1ターンずつ、ログつきで再生する（原因の確認用）。結果は Logs/exploit_trace.txt。</summary>
        public static void RunExploitTrace()
        {
            var sb = new StringBuilder("【検証中】移動だけで大きく削れる手順の再生（第4部で見つかったもの）\n\n");
            CommandRecord R(string card, Direction d, int dist = 0) => new CommandRecord(Card(card), d, dist);
            var cases = new (string stage, int repeat, List<CommandRecord> seq)[]
            {
                ("StageM6", 1, new List<CommandRecord> { R("Blink", Direction.Up, 3), R("Slide", Direction.Left), R("Walk", Direction.Right, 1) }),
                ("StageM6", 1, new List<CommandRecord> { R("Walk", Direction.Up, 1), R("Rush", Direction.Up, 3), R("Slide", Direction.Down) }),
                ("StageM5", 2, new List<CommandRecord> { R("Dash", Direction.Up, 2), R("VarRush", Direction.Down, 2), R("Jump", Direction.Left, 2) }),
                ("StageM5", 1, new List<CommandRecord> { R("Walk", Direction.Down, 1), R("Jump", Direction.Left, 2), R("Swap", Direction.Down) }),
                ("StageM4", 1, new List<CommandRecord> { R("Blink", Direction.Right, 3), R("Slash", Direction.Left), R("Walk", Direction.Right, 1) }),
            };
            foreach (var (name, repeat, seq) in cases)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var m = BattleModel.FromStage(stage);
                sb.AppendLine($"=== {name}：「{Seq(seq)}」×{repeat}");
                sb.AppendLine($"  開始：あなた {m.Player.Position}／敵 {string.Join("・", m.Enemies.Select(e => $"{e.Name}{e.Position} HP{e.Hp}"))}");
                for (int t = 0; t < repeat; t++)
                {
                    var log = new List<string>();
                    var outcome = TurnRules.PlayTurn(m, seq, null, null, log);
                    foreach (var line in log) sb.AppendLine("    " + line);
                    sb.AppendLine($"  → T{t + 1} 後：あなた {m.Player.Position} HP{m.Player.Hp}／敵 {string.Join("・", m.Enemies.Select(e => $"{e.Name}{e.Position} HP{e.Hp}"))}／{outcome}");
                }
                sb.AppendLine();
            }
            Write(sb, "Logs/exploit_trace.txt");
        }

        /// <summary>【検証専用】M11（中央が燃える）・M12（中央の柱）の解析：第2部・第3部（中央だけ・系統）と第4部（危険な簡単攻略）。結果は Logs/center_stages.txt。</summary>
        public static void RunCenterStages()
        {
            var sb = new StringBuilder("【検証専用】中央固定を壊すステージ M11・M12 の解析（基本4枚＋1種類の中央攻略、全種類での危険な攻略）\n\n");
            var full = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            foreach (var n in new[] { "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" }) full.Add(Card(n));
            var fullSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = full, MaxSlots = 3 });
            foreach (var name in new[] { "StageM11", "StageM12" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Boards(sb, stage, "Logs/center_stages.txt");
                var start = BattleModel.FromStage(stage);
                sb.AppendLine("  ■ 第4部：全種類（20枚デッキの全種類を各1回）");
                sb.AppendLine("    " + Exploits(start, fullSeqs));
                sb.AppendLine("    " + QuickWins(start, fullSeqs));
                sb.AppendLine($"  （解析時間 {watch.Elapsed.TotalMinutes:0.0} 分）");
                sb.AppendLine();
                Write(sb, "Logs/center_stages.txt");
            }
        }

        /// <summary>第1部（カードの流れ）だけ。結果は Logs/large_hand_flow.txt。</summary>
        public static void RunFlowOnly()
        {
            var sb = new StringBuilder();
            var m4 = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageM4.asset");
            Flow(sb, m4.handTestDeckLarge);
            Write(sb, "Logs/large_hand_flow.txt");
        }

        /// <summary>
        /// 【研究・実験】「あなたが空振りした移動命令は、敵にコピーされない」というルールにしたら、中央だけの攻略と最短がどう変わるか。
        /// ゲーム本体には入れていない（TurnRules.ExperimentSkipWhiffedMoves を解析中だけ true）。結果は Logs/whiff_rule_experiment.txt。
        /// </summary>
        public static void RunWhiffRuleExperiment()
        {
            var sb = new StringBuilder("【研究・実験】空振りした移動はコピーされないルール（解析だけ。ゲームには入れていない）\n");
            sb.AppendLine("左＝今のルール（第2部の結果を再計算）／右＝実験ルール。基本4枚＋1種類、近似のビーム探索。\n");
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            try
            {
                foreach (var name in new[] { "StageM4", "StageM10", "StageM6" })
                {
                    var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                    var start = BattleModel.FromStage(stage);
                    var center = Center(start.Board);
                    sb.AppendLine($"=== {stage.name} {stage.stageTitle}");
                    foreach (var extra in new[] { (string)null, "Jump", "Blink", "Swap", "Slide" })
                    {
                        var cards = new List<CommandData>(basic);
                        if (extra != null) cards.Add(Card(extra));
                        var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                        var line = $"    {(extra == null ? "基本4枚だけ" : "＋" + Card(extra).displayName),-8}：";
                        foreach (bool experiment in new[] { false, true })
                        {
                            TurnRules.ExperimentSkipWhiffedMoves = experiment;
                            var best = Search(start, seqs, (m, ev, cells) => true);
                            var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                            line += $"{(experiment ? "　→　実験：" : "今：")}最短 {Show(best)}／中央だけ {Show(centerOnly)}";
                        }
                        TurnRules.ExperimentSkipWhiffedMoves = false;
                        sb.AppendLine(line);
                        Write(sb, "Logs/whiff_rule_experiment.txt");
                    }
                    sb.AppendLine();
                }
            }
            finally { TurnRules.ExperimentSkipWhiffedMoves = false; }
            Write(sb, "Logs/whiff_rule_experiment.txt");
        }

        /// <summary>
        /// 【研究・実験】対策案の効果を解析で確かめる（ゲームには入れていない）。結果は Logs/fix_experiments.txt。
        ///  ① 跳躍・転移の弱点「着地点がふさがっていたら手前へ（空振りしない）」で、中央だけの攻略がどう変わるか（M4・M10・M7）
        ///  ② トゲ 5 → 3 で、移動だけの1ターン勝利（M5・M6）が消えるか（20枚デッキの全種類）
        /// </summary>
        public static void RunFixExperiments()
        {
            var sb = new StringBuilder("【研究・実験】対策案の効果（解析だけ。ゲームには入れていない）" + "\n\n");
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            sb.AppendLine("■ ① 跳躍・転移の着地点がふさがっていたら手前へ（空振りしない）");
            try
            {
                foreach (var name in new[] { "StageM4", "StageM10", "StageM7" })
                {
                    var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                    var start = BattleModel.FromStage(stage);
                    var center = Center(start.Board);
                    sb.AppendLine($"=== {stage.name} {stage.stageTitle}");
                    foreach (var extra in new[] { "Jump", "Blink" })
                    {
                        var cards = new List<CommandData>(basic) { Card(extra) };
                        var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                        var line = $"    ＋{Card(extra).displayName}：";
                        foreach (bool on in new[] { false, true })
                        {
                            RulesEngine.ExperimentLeapLandsShort = on;
                            var best = Search(start, seqs, (m, ev, cells) => true);
                            var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                            line += $"{(on ? "　→　手前に着地：" : "今：")}最短 {Show(best)}／中央だけ {Show(centerOnly)}";
                        }
                        RulesEngine.ExperimentLeapLandsShort = false;
                        sb.AppendLine(line);
                        Write(sb, "Logs/fix_experiments.txt");
                    }
                }
            }
            finally { RulesEngine.ExperimentLeapLandsShort = false; }

            sb.AppendLine();
            sb.AppendLine("■ ② トゲ 5 → 3（20枚デッキの全種類を各1回）");
            var full = new List<CommandData>(basic);
            foreach (var n in new[] { "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" }) full.Add(Card(n));
            var fullSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = full, MaxSlots = 3 });
            foreach (var name in new[] { "StageM5", "StageM6" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                foreach (int spike in new[] { 5, 3 })
                {
                    var copy = Object.Instantiate(stage);
                    copy.spikeDamage = spike;
                    var start = BattleModel.FromStage(copy);
                    sb.AppendLine($"=== {stage.name} トゲ {spike}");
                    sb.AppendLine("    " + Exploits(start, fullSeqs));
                    sb.AppendLine("    " + QuickWins(start, fullSeqs));
                    Object.DestroyImmediate(copy);
                    Write(sb, "Logs/fix_experiments.txt");
                }
            }
        }

        /// <summary>【研究】壁蹴りと M11（中央が燃える）・M12（中央の柱：柱も「壁」なので中央で壁蹴りが3マスになる）の組み合わせ。結果は Logs/wallkick_check.txt。</summary>
        public static void RunWallKickCheck()
        {
            var sb = new StringBuilder("【研究】壁蹴り（検証専用）× 中央対策ステージ" + "\n\n");
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            foreach (var name in new[] { "StageM12", "StageM11" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var start = BattleModel.FromStage(stage);
                var center = Center(start.Board);
                foreach (var extra in new[] { "WallKick", "Jump" })
                {
                    var cards = new List<CommandData>(basic) { Card(extra) };
                    var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                    var best = Search(start, seqs, (m, ev, cells) => true);
                    var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                    sb.AppendLine($"{stage.name} ＋{Card(extra).displayName}：最短 {Show(best)}／中央だけ {Show(centerOnly)}");
                    Write(sb, "Logs/wallkick_check.txt");
                }
            }
        }

        /// <summary>手札の流れ・空振り還元・M11/M12 をまとめて実行（バッチ1回で済ませる）。</summary>
        public static void RunNextBatch()
        {
            RunFlowOnly();
            RunWhiff();
            RunCenterStages();
        }

        /// <summary>中央トレースと手順の再生をまとめて実行（バッチ1回で済ませる）。</summary>
        public static void RunTraces()
        {
            RunExploitTrace();
            RunCenterTrace();
        }

        /// <summary>
        /// 【研究】空振り還元（空振りした命令の数だけ次ターンに追加ドロー）の悪用の確認。M4〜MB1 で、ランダムに進めた局面から
        /// 20枚デッキの全種類の手順をランダムに選び、あなたの空振りの数と、そのとき敵側で何が起きたかを数える。結果は Logs/whiff_report.txt。
        /// </summary>
        public static void RunWhiff()
        {
            var sb = new StringBuilder("【研究】空振り還元の悪用チェック（20枚デッキの全種類・ランダムな局面30個×ランダムな手順1500個）\n");
            sb.AppendLine("A＝あなたの空振り1つにつき+1／B＝空振りし、かつ敵のコピーで敵が動いた・傷ついた／C＝空振りし、かつ敵がダメージを受けた");
            sb.AppendLine("安全な空振り連打＝空振り2つ以上で、誰の HP も変わらない手順。その手順がある局面の割合（＝毎ターン無傷で手札を回せる）\n");
            var full = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            foreach (var n in new[] { "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" }) full.Add(Card(n));
            var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = full, MaxSlots = 3 }).Where(q => q.Count == 3).ToList();
            foreach (var name in NewStageNames)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var start = BattleModel.FromStage(stage);
                var rng = new System.Random(name.GetHashCode() & 0xffff);
                int samples = 0, w1 = 0, w2 = 0, w3 = 0, bOk = 0, cOk = 0, statesWithSafe2 = 0, statesWithSafe3 = 0, states = 0;
                double whiffSum = 0;
                for (int s = 0; s < 30; s++)
                {
                    var state = start.Clone();
                    int pre = rng.Next(0, 4);
                    bool alive = true;
                    for (int t = 0; t < pre && alive; t++) alive = TurnRules.PlayTurn(state, seqs[rng.Next(seqs.Count)], null) == TurnOutcome.Continue;
                    if (!alive) continue;
                    states++;
                    bool safe2 = false, safe3 = false;
                    for (int k = 0; k < 1500; k++)
                    {
                        var seq = seqs[rng.Next(seqs.Count)];
                        var m = state.Clone();
                        var log = new List<string>();
                        var events = new List<BattleEvent>();
                        int php = m.Player.Hp, ehp = StagnationRule.EnemyHpTotal(m);
                        TurnRules.PlayTurn(m, seq, null, events, log);
                        string me = m.Player.Name + "：";
                        int w = log.Count(l => l.StartsWith(me) && l.Contains("（空振り）"));
                        samples++;
                        whiffSum += w;
                        if (w >= 1) w1++;
                        if (w >= 2) w2++;
                        if (w >= 3) w3++;
                        bool enemyMovedOrHurt = events.Any(e => (e.Type == BattleEventType.Moved || e.Type == BattleEventType.Damaged) && !e.Unit.IsPlayer);
                        bool enemyHurt = events.Any(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer);
                        if (w >= 1 && enemyMovedOrHurt) bOk++;
                        if (w >= 1 && enemyHurt) cOk++;
                        bool nobodyHurt = m.Player.Hp == php && StagnationRule.EnemyHpTotal(m) == ehp;
                        if (w >= 2 && nobodyHurt) safe2 = true;
                        if (w >= 3 && nobodyHurt) safe3 = true;
                    }
                    if (safe2) statesWithSafe2++;
                    if (safe3) statesWithSafe3++;
                }
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（局面 {states}）");
                sb.AppendLine($"  1手順あたりの空振り {whiffSum / samples:0.00}／空振り1以上 {100.0 * w1 / samples:0}%・2以上 {100.0 * w2 / samples:0}%・3つ全部 {100.0 * w3 / samples:0}%");
                sb.AppendLine($"  B 条件を満たす {100.0 * bOk / samples:0}%／C 条件を満たす {100.0 * cOk / samples:0}%");
                sb.AppendLine($"  安全な空振り連打（2つ以上・無傷）がある局面 {statesWithSafe2}/{states}・（3つ全部・無傷）{statesWithSafe3}/{states}");
                Write(sb, "Logs/whiff_report.txt");
            }
        }

        internal static string Seq(List<CommandRecord> seq) => string.Join(" / ", seq.Select(c => c.ToDisplayText()));

        /// <summary>1枚の繰り返しで勝てるか／直接攻撃なしで倒れるか／6ラウンド誰も傷つかない待機。</summary>
        internal static string Exploits(BattleModel start, List<List<CommandRecord>> seqs)
        {
            var singleWins = new List<string>();
            foreach (var seq in seqs.Where(q => q.Count == 1))
            {
                var m = start.Clone();
                for (int t = 0; t < 10; t++)
                {
                    var o = TurnRules.PlayTurn(m, seq, null);
                    if (o == TurnOutcome.GameOver) break;
                    if (o == TurnOutcome.Cleared) { singleWins.Add($"{seq[0].ToDisplayText()}×{t + 1}（HP {m.Player.Hp}）"); break; }
                }
            }
            // 同じ手順を繰り返して勝てる（2〜3枚）：最も早いもの
            string repeatWin = null;
            int repeatTurns = 99;
            int stalls = 0;
            string stallExample = null;
            foreach (var seq in seqs)
            {
                var m = start.Clone();
                int php = m.Player.Hp, ehp = StagnationRule.EnemyHpTotal(m);
                bool quiet = true;
                for (int t = 0; t < 10; t++)
                {
                    var o = TurnRules.PlayTurn(m, seq, null);
                    if (t < 6 && (o != TurnOutcome.Continue || m.Player.Hp != php || StagnationRule.EnemyHpTotal(m) != ehp)) quiet = false;
                    if (o == TurnOutcome.GameOver) break;
                    if (o == TurnOutcome.Cleared)
                    {
                        if (t + 1 < repeatTurns) { repeatTurns = t + 1; repeatWin = $"「{Seq(seq)}」×{t + 1}（HP {m.Player.Hp}）"; }
                        break;
                    }
                    if (!quiet && t >= 5 && repeatTurns <= t + 1) break;
                }
                if (quiet) { stalls++; stallExample = stallExample ?? Seq(seq); }
            }
            return $"1枚の繰り返しで勝てる：{(singleWins.Count == 0 ? "なし" : string.Join("、", singleWins))}" +
                   $"／同じ手順の繰り返しで最速：{repeatWin ?? "なし（10T以内）"}" +
                   $"／6ラウンド誰も傷つかない待機：{(stalls == 0 ? "なし" : $"{stalls} 通り（例：{stallExample}）")}";
        }

        /// <summary>1ターン目に無傷で削れる最大量と、2ターン以内に勝てるか（2ターン目は有望な40状態だけ調べる近似）。</summary>
        internal static string QuickWins(BattleModel start, List<List<CommandRecord>> seqs)
        {
            int ehp0 = StagnationRule.EnemyHpTotal(start);
            int bestDmg = -1;
            string bestSeq = null;
            string win1 = null;
            var after = new List<(BattleModel m, List<CommandRecord> seq)>();
            foreach (var seq in seqs)
            {
                var m = start.Clone();
                var o = TurnRules.PlayTurn(m, seq, null);
                if (o == TurnOutcome.GameOver) continue;
                if (o == TurnOutcome.Cleared) { win1 = win1 ?? $"{Seq(seq)}（HP {m.Player.Hp}）"; continue; }
                int dmg = ehp0 - StagnationRule.EnemyHpTotal(m);
                if (m.Player.Hp == start.Player.Hp && dmg > bestDmg) { bestDmg = dmg; bestSeq = Seq(seq); }
                after.Add((m, seq));
            }
            string win2 = null;
            if (win1 == null)
            {
                var seen = new HashSet<string>();
                var top = after.OrderBy(x => StagnationRule.EnemyHpTotal(x.m) * 100 - x.m.Player.Hp * 10).Where(x => seen.Add(BattleSolver.Key(x.m))).Take(40).ToList();
                foreach (var (m0, s0) in top)
                {
                    foreach (var seq in seqs)
                    {
                        var m = m0.Clone();
                        if (TurnRules.PlayTurn(m, seq, null) == TurnOutcome.Cleared) { win2 = $"{Seq(s0)} → {Seq(seq)}（HP {m.Player.Hp}）"; break; }
                    }
                    if (win2 != null) break;
                }
            }
            return $"1ターン目に無傷で削れる最大：{bestDmg}/{ehp0}（{bestSeq}）" +
                   $"／1ターンで勝てる：{win1 ?? "なし"}／2ターンで勝てる：{(win1 != null ? "—" : win2 ?? "なし（近似）")}";
        }

        // ───────── 第1部：カードの流れ ─────────

        static readonly (string name, DrawPolicy policy, int opening)[] Modes =
        {
            ("A（初手5・毎ターン2・上限7）", DrawPolicy.DrawTwo, DeckState.DefaultHandSize),
            ("B（初手5・最低3＋1・上限7）", DrawPolicy.FloorThreePlusOne, DeckState.DefaultHandSize),
            ("大:3枚（初手8・毎ターン3・上限10）", DrawPolicy.LargeDrawThree, DeckState.LargeOpening),
            ("大:使用+1（初手8・使った分＋1・上限10）", DrawPolicy.LargeUsedPlusOne, DeckState.LargeOpening),
            ("中:7/9（初手7・毎ターン3・上限9）", DrawPolicy.MidDrawThree, DeckState.MidOpening),
        };

        static bool IsMove(CommandData c) => c.type == CommandType.Walk || c.type == CommandType.Rush || c.type == CommandType.Jump ||
                                             c.type == CommandType.Diagonal || c.type == CommandType.Slide || c.type == CommandType.Blink;

        static void Flow(StringBuilder sb, List<CommandData> deck)
        {
            sb.AppendLine($"■ 第1部：カードの流れ（同じ20枚のデッキ：{string.Join("・", deck.GroupBy(c => c.displayName).Select(g => g.Key + g.Count()))}、{Seeds} 回の平均）");
            sb.AppendLine("  到達率＝自分から距離3以内の24マスのうち、手札の移動札2枚まで（各1回）で届くマスの割合（空の盤面）");
            foreach (int usage in new[] { 3, 2 })
            {
                sb.AppendLine($"  ○ 毎ターン{usage}枚使う（使う札はランダム）");
                sb.AppendLine("     方式                                  | 手札(2〜8T平均) | 移動の種類 | 攻撃札なし | 到達率 | 3命令を組める");
                foreach (var mode in Modes)
                {
                    double hand = 0, kinds = 0, noAttack = 0, reach = 0, three = 0;
                    int samples = 0;
                    for (int seed = 0; seed < Seeds; seed++)
                    {
                        var d = new DeckState(new System.Random(seed), handSize: mode.opening, policy: mode.policy);
                        d.StartBattle(deck);
                        var rng = new System.Random(seed * 31 + 7);
                        for (int t = 0; t < Turns; t++)
                        {
                            if (t > 0) d.StartTurnDraw();
                            if (t > 0)
                            {
                                samples++;
                                hand += d.Hand.Count;
                                kinds += d.Hand.Where(c => IsMove(c.Data)).Select(c => c.Data).Distinct().Count();
                                if (!d.Hand.Any(c => c.Data.damage > 0)) noAttack++;
                                reach += Reach(d.Hand.Select(c => c.Data).ToList());
                                if (d.Hand.Count >= 3) three++;
                            }
                            d.UseCards(d.Hand.OrderBy(_ => rng.Next()).Take(System.Math.Min(usage, d.Hand.Count)).ToList());
                            d.EndTurn(new CardInstance[0]);
                        }
                    }
                    sb.AppendLine($"     {mode.name,-38}| {hand / samples,6:0.0}         | {kinds / samples,5:0.0}      | {100 * noAttack / samples,6:0}%   | {100 * reach / samples,4:0}%  | {100 * three / samples,4:0}%");
                }
            }
            sb.AppendLine();
        }

        /// <summary>手札の移動札2枚まで（各1回・順番自由）で届く、距離1〜3のマスの割合（空の盤面）。</summary>
        static double Reach(List<CommandData> hand)
        {
            var moves = hand.Where(IsMove).ToList();
            var single = new List<HashSet<Vector2Int>>();
            foreach (var c in moves) single.Add(Offsets(c));
            var reach = new HashSet<Vector2Int>();
            for (int i = 0; i < single.Count; i++)
            {
                reach.UnionWith(single[i]);
                for (int j = 0; j < single.Count; j++)
                    if (i != j)
                        foreach (var a in single[i])
                            foreach (var b in single[j])
                                reach.Add(a + b);
            }
            int targets = 0, hit = 0;
            for (int x = -3; x <= 3; x++)
                for (int y = -3; y <= 3; y++)
                {
                    int dist = Mathf.Abs(x) + Mathf.Abs(y);
                    if (dist == 0 || dist > 3) continue;
                    targets++;
                    if (reach.Contains(new Vector2Int(x, y))) hit++;
                }
            return (double)hit / targets;
        }

        static HashSet<Vector2Int> Offsets(CommandData c)
        {
            var set = new HashSet<Vector2Int>();
            foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
            {
                switch (c.type)
                {
                    case CommandType.Diagonal: set.Add(d.ToDiagonalOffset()); break;
                    case CommandType.Slide: for (int k = 1; k <= 3; k++) set.Add(d.ToOffset() * k); break; // 実際は障害物しだい（近似）
                    default: for (int k = Mathf.Max(1, c.minDistance); k <= Mathf.Max(1, c.maxDistance); k++) set.Add(d.ToOffset() * k); break;
                }
            }
            return set;
        }

        // ───────── 第2部・第3部：盤面 ─────────

        static void Boards(StringBuilder sb, StageData stage, string path = "Logs/large_hand_report.txt")
        {
            var basic = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            var start = BattleModel.FromStage(stage);
            var center = Center(start.Board);
            sb.AppendLine($"=== {stage.name} {stage.stageTitle}");
            sb.AppendLine("  ■ 第2部：基本4枚に新しい移動札を1種類足したとき（固定の手札・各1ターン1回・最大3命令）");
            foreach (var extra in new[] { (string)null, "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" })
            {
                var cards = new List<CommandData>(basic);
                if (extra != null) cards.Add(Card(extra));
                var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                var best = Search(start, seqs, (m, ev, cells) => true);
                var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                sb.AppendLine($"    {(extra == null ? "基本4枚だけ" : "＋" + Card(extra).displayName),-8}：最短 {Show(best)}／中央3×3だけ {Show(centerOnly)}");
                Write(sb, path);
            }

            sb.AppendLine("  ■ 第3部：系統の違う攻略（基本4枚＋跳躍＋斜め歩）");
            var richCards = new List<CommandData>(basic) { Card("Jump"), Card("Diagonal") };
            var rich = BattleSolver.Sequences(new BattleSolver.Settings { Cards = richCards, MaxSlots = 3 });
            var any = Search(start, rich, (m, ev, cells) => true);
            var directOnly = Search(start, rich, (m, ev, cells) => !ev.Any(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer && !(e.Cause.Source != null && e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct)));
            var indirect = Search(start, rich, (m, ev, cells) => !ev.Any(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer && e.Cause.Source != null && e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct));
            sb.AppendLine($"    制限なし：{Show(any)}{Mix(start, any)}");
            sb.AppendLine($"    直接攻撃だけ（同士討ち・矢・炎・トゲで敵を傷つけない）：{Show(directOnly)}");
            sb.AppendLine($"    直接攻撃なし（同士討ち・矢・炎・トゲ・押し出しだけ）：{Show(indirect)}{Mix(start, indirect)}");
            int families = (directOnly.path != null ? 1 : 0) + (indirect.path != null ? 1 : 0);
            sb.AppendLine($"    → 系統の違う勝ち方：{families} 種類（直接攻撃型／間接型）");
            sb.AppendLine();
        }

        internal static HashSet<Vector2Int> Center(BoardModel board)
        {
            int cx = (board.Width - 1) / 2, cy = (board.Height - 1) / 2;
            var set = new HashSet<Vector2Int>();
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    set.Add(new Vector2Int(x, y));
            return set;
        }

        internal struct Result { public int turns, hp; public bool approx; public List<List<CommandRecord>> path; }

        internal static string Show(Result r) => r.path == null ? "見つからず" + (r.approx ? "（近似）" : "") : $"{r.turns}T（HP {r.hp}）{(r.approx ? "（近似）" : "")}";

        /// <summary>最短ルートで、敵へのダメージの内訳（直接・同士討ち・矢・炎・トゲ）。</summary>
        static string Mix(BattleModel start, Result r)
        {
            if (r.path == null) return "";
            var m = start.Clone();
            var events = new List<BattleEvent>();
            foreach (var turn in r.path) if (TurnRules.PlayTurn(m, turn, null, events) != TurnOutcome.Continue) break;
            var dmg = events.Where(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer).ToList();
            int direct = dmg.Where(e => e.Cause.Source != null && e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct).Sum(e => e.Amount);
            int ff = dmg.Where(e => e.Cause.Kind == DamageKind.Direct && e.Cause.Source != null && !e.Cause.Source.IsPlayer).Sum(e => e.Amount);
            int arrow = dmg.Where(e => e.Cause.Kind == DamageKind.Trait).Sum(e => e.Amount);
            int fire = dmg.Where(e => e.Cause.Kind == DamageKind.Fire).Sum(e => e.Amount);
            int spike = dmg.Where(e => e.Cause.Kind == DamageKind.Spike).Sum(e => e.Amount);
            var moves = r.path.SelectMany(t => t).Where(c => IsMove(c.Command) || c.Command.type == CommandType.Swap).Select(c => c.Command.displayName).Distinct();
            return $"　内訳：直接 {direct}・同士討ち {ff}・矢 {arrow}・炎 {fire}・トゲ {spike}／使った移動：{string.Join("・", moves)}";
        }

        /// <summary>幅優先（ビーム）探索。ok：そのラウンドのモデル・出来事・あなたが入ったマスを見て、許すか決める。</summary>
        internal static Result Search(BattleModel start, List<List<CommandRecord>> seqs, System.Func<BattleModel, List<BattleEvent>, List<Vector2Int>, bool> ok)
        {
            var frontier = new List<(BattleModel m, List<List<CommandRecord>> path)> { (start.Clone(), new List<List<CommandRecord>>()) };
            var seen = new HashSet<string> { BattleSolver.Key(start) };
            bool approx = false;
            for (int turn = 1; turn <= 7; turn++)
            {
                Result best = default;
                var next = new List<(BattleModel, List<List<CommandRecord>>)>();
                foreach (var (model, path) in frontier)
                    foreach (var seq in seqs)
                    {
                        var m = model.Clone();
                        var events = new List<BattleEvent>();
                        var outcome = TurnRules.PlayTurn(m, seq, null, events);
                        if (outcome == TurnOutcome.GameOver) continue;
                        var cells = events.Where(e => e.Type == BattleEventType.Moved && e.Unit.IsPlayer).Select(e => e.To).ToList();
                        cells.Add(model.Player.Position);
                        if (!ok(m, events, cells)) continue;
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
    }
}
