using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// C# 版の盤面解析をまとめて実行し、Logs/solver_report.txt に書き出す（ゲーム本体と同じルールで動く）。
    /// - RunStages：Assets/_Project/Data/Stages の全ステージ
    /// - RunCandidates：tools/solver_candidates.txt に書いた盤面候補（レベルデザインの試行錯誤用）
    /// バッチ実行：-executeMethod CopyCommand.EditorTools.SolverRunner.RunStages （または RunCandidates）
    /// </summary>
    public static class SolverRunner
    {
        const string CommandFolder = "Assets/_Project/Data/Commands";

        [MenuItem("CopyCommand/解析：全ステージ")]
        public static void RunStages()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }))
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(guid));
                if (stage.availableCommands.Count == 0) continue; // RUN の戦闘（デッキ）は対象外
                Analyze(sb, stage.name + " " + stage.stageTitle, stage.GetMapRows(), stage.availableCommands, stage.maxSlots,
                    stage.playerMaxHp, stage.enemyMaxHp, stage.spikeDamage, stage.enemyInnateAction, 5);
            }
            Write("solver_report.txt", sb);
        }

        /// <summary>
        /// tools/solver_candidates.txt の書式：
        ///   == 名前
        ///   cards: Walk,Dash,Slash,Push   slots: 3   php: 12   ehp: 8   turns: 5
        ///   （盤面の行）
        ///   （空行で区切る）
        /// </summary>
        [MenuItem("CopyCommand/解析：盤面候補")]
        public static void RunCandidates()
        {
            var sb = new StringBuilder();
            var lines = File.ReadAllLines("tools/solver_candidates.txt");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("==")) continue;
                string name = lines[i].Substring(2).Trim();
                var opts = Parse(lines[++i]);
                var rows = new List<string>();
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !lines[i + 1].StartsWith("==")) rows.Add(lines[++i].Trim());
                var cards = opts["cards"].Split(',').Select(n => AssetDatabase.LoadAssetAtPath<CommandData>($"{CommandFolder}/{n.Trim()}.asset")).ToList();
                // traits: Normal,Mirror のように敵A・敵B… のコピー特性を指定できる（研究用。省略なら全員 通常）
                var traits = opts.ContainsKey("traits") ? opts["traits"].Split(',').Select(t => (CopyTrait)System.Enum.Parse(typeof(CopyTrait), t.Trim())).ToArray() : null;
                Analyze(sb, name, rows.ToArray(), cards, int.Parse(opts["slots"]), int.Parse(opts["php"]), int.Parse(opts["ehp"]),
                    opts.ContainsKey("spike") ? int.Parse(opts["spike"]) : 5, null, int.Parse(opts["turns"]), traits);
                Write("solver_candidates_report.txt", sb); // 途中経過も読めるように1盤面ごとに書き出す
            }
            Write("solver_candidates_report.txt", sb);
        }

        /// <summary>
        /// 複数Enemyテストステージの候補探し：ランダムな小盤面を多数作り、軽い指標（1ターン目の全手順）でふるいにかけ、
        /// 目的ごとの上位を tools/solver_candidates.txt の書式で Logs/board_search.txt に書き出す（詳しい解析は RunCandidates で行う）。
        /// </summary>
        [MenuItem("CopyCommand/解析：複数Enemy盤面の候補探し")]
        public static void RunBoardSearch()
        {
            var cards = new[] { "Walk", "Dash", "Slash", "Push" }.Select(n => AssetDatabase.LoadAssetAtPath<CommandData>($"{CommandFolder}/{n}.asset")).ToList();
            var settings = new BattleSolver.Settings { Cards = cards, MaxSlots = 3 };
            var seqs = BattleSolver.Sequences(settings);
            var rng = new System.Random(20261003);
            var found = new Dictionary<string, List<(double score, string text)>> { ["A"] = new List<(double, string)>(), ["B"] = new List<(double, string)>(), ["C"] = new List<(double, string)>() };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int tried = 0;
            while (watch.Elapsed.TotalSeconds < SearchSeconds)
            {
                string kind = new[] { "A", "B", "C" }[tried % 3];
                tried++;
                var rows = RandomBoard(rng, kind == "C");
                var start = BattleModel.FromRows(rows, 5, 12, 8);
                var m = Measure(start, seqs);
                double score;
                switch (kind)
                {
                    case "A": // 同じ斬撃が片方では空振り、もう片方ではプレイヤーに当たる（考えずに殴ると痛い）
                        if (m.Single > 0 || m.Attacks < 20 || m.FriendlyFire > 0.05) continue;
                        score = m.Punished * 2 + m.Zure;
                        break;
                    case "B": // 狙える同士討ち（偶然では起きにくいが、起こせる）
                        if (m.Single > 0 || m.FriendlyFire < 0.01 || m.FriendlyFire > 0.08 || m.Attacks < 20) continue;
                        score = m.Punished + m.Zure + m.FriendlyFire;
                        break;
                    default: // トゲ＋2体：片方を有利に動かすと、もう片方のコピーが危険になる
                        if (m.Single > 0 || m.EnemySpike < 0.05 || m.EnemySpike > 0.35 || m.Attacks < 20) continue;
                        score = m.Punished * 1.5 + m.Zure + m.PlayerSpike;
                        break;
                }
                found[kind].Add((score, $"{kind} 被弾率{m.Punished:P0} ズレ{m.Zure:P0} 同士討ち{m.FriendlyFire:P0} 敵トゲ{m.EnemySpike:P0} 自分トゲ{m.PlayerSpike:P0}\n" + string.Join("\n", rows)));
            }
            var sb = new StringBuilder($"盤面候補探し：{tried} 盤面を試行（{watch.Elapsed.TotalSeconds:0} 秒）\n\n");
            foreach (var kv in found)
            {
                sb.AppendLine($"######## {kv.Key}（条件を満たした {kv.Value.Count} 盤面の上位）");
                var seen = new HashSet<string>();
                foreach (var c in kv.Value.OrderByDescending(c => c.score).Where(c => seen.Add(c.text)).Take(8))
                {
                    var lines = c.text.Split('\n');
                    sb.AppendLine($"== {lines[0]}");
                    sb.AppendLine("cards: Walk,Dash,Slash,Push slots: 3 php: 12 ehp: 8 turns: 5");
                    foreach (var row in lines.Skip(1)) sb.AppendLine(row);
                    sb.AppendLine();
                }
            }
            Write("board_search.txt", sb);
        }

        const double SearchSeconds = 600;

        class Metrics { public int Attacks, Single; public double Punished, Zure, FriendlyFire, EnemySpike, PlayerSpike; }

        static Metrics Measure(BattleModel start, List<List<CommandRecord>> seqs)
        {
            int attacks = 0, punished = 0, zure = 0, ff = 0, enemySpike = 0, playerSpike = 0, single = 0;
            foreach (var seq in seqs)
            {
                var m = start.Clone();
                var events = new List<BattleEvent>();
                TurnRules.PlayTurn(m, seq, null, events);
                var dmg = events.Where(e => e.Type == BattleEventType.Damaged).ToList();
                bool playerHit = dmg.Any(e => e.Unit.IsPlayer);
                if (dmg.Any(e => e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct && !e.Unit.IsPlayer)) { attacks++; if (playerHit) punished++; }
                if (dmg.Any(e => !e.Unit.IsPlayer && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit && e.Cause.Kind == DamageKind.Direct)) ff++;
                if (dmg.Any(e => !e.Unit.IsPlayer && e.Cause.Kind == DamageKind.Spike)) enemySpike++;
                if (dmg.Any(e => e.Unit.IsPlayer && e.Cause.Kind == DamageKind.Spike && !e.Cause.Source.IsPlayer)) playerSpike++;
                var outcomes = start.Enemies.Select(enemy =>
                {
                    var mine = dmg.Where(e => e.Cause.ByCopy && e.Cause.Source.Id == enemy.Id).ToList();
                    if (mine.Any(e => e.Unit.IsPlayer)) return 1;
                    if (mine.Any(e => !e.Unit.IsPlayer && e.Unit.Id != enemy.Id)) return 2;
                    if (dmg.Any(e => e.Unit.Id == enemy.Id && e.Cause.Kind == DamageKind.Spike && e.Cause.Source.Id == enemy.Id)) return 3;
                    return 0;
                }).ToList();
                if (outcomes.Distinct().Count() > 1 && outcomes.Any(o => o != 0)) zure++;
                if (seq.Count == 1)
                {
                    var r = start.Clone();
                    for (int t = 1; t <= 10; t++)
                    {
                        var o = TurnRules.PlayTurn(r, seq, null);
                        if (o == TurnOutcome.GameOver) break;
                        if (o == TurnOutcome.Cleared) { single++; break; }
                    }
                }
            }
            double n = seqs.Count;
            return new Metrics
            {
                Attacks = attacks, Single = single, Punished = attacks == 0 ? 0 : (double)punished / attacks,
                Zure = zure / n, FriendlyFire = ff / n, EnemySpike = enemySpike / n, PlayerSpike = playerSpike / n,
            };
        }

        /// <summary>5×4〜6×5 の盤面。壁 0〜2、トゲ（C のみ）1〜3、プレイヤー1、敵2（敵同士は最初から隣り合わない）。</summary>
        static string[] RandomBoard(System.Random rng, bool spikes)
        {
            while (true)
            {
                int w = rng.Next(5, 7), h = rng.Next(4, 6);
                var g = new char[h, w];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) g[y, x] = '.';
                (int, int) Free()
                {
                    while (true) { int y = rng.Next(h), x = rng.Next(w); if (g[y, x] == '.') return (y, x); }
                }
                int walls = rng.Next(0, 3);
                for (int i = 0; i < walls; i++) { var (y, x) = Free(); g[y, x] = '#'; }
                if (spikes) { int n = rng.Next(1, 4); for (int i = 0; i < n; i++) { var (y, x) = Free(); g[y, x] = '^'; } }
                var (py, px) = Free(); g[py, px] = 'P';
                var (ay, ax) = Free(); g[ay, ax] = 'E';
                var (by, bx) = Free(); g[by, bx] = 'E';
                if (System.Math.Abs(ay - by) + System.Math.Abs(ax - bx) <= 1) continue;
                var rows = new string[h];
                for (int y = 0; y < h; y++) { var line = new StringBuilder(); for (int x = 0; x < w; x++) line.Append(g[y, x]); rows[y] = line.ToString(); }
                return rows;
            }
        }

        /// <summary>
        /// 研究用の比較（ゲーム本体のルールは変えない）：「敵が1体だけになったら、その敵は方向反転コピーになる」案を、
        /// 解析の中だけで試す。M1〜M3 について、最初の1体を倒した直後の盤面で「攻撃手順の被弾率」がどう変わるか、
        /// 最短・無傷クリアのターン数がどう変わるかを Logs/last_stand_report.txt に書き出す。
        /// </summary>
        [MenuItem("CopyCommand/解析：最後の1体を反転にする案")]
        public static void RunLastStandStudy()
        {
            var sb = new StringBuilder("研究：「最後の1体は方向反転コピーになる」案の比較（解析の中だけで試した。ゲーム本体は変えていない）\n\n");
            foreach (var name in new[] { "StageM1", "StageM2", "StageM3" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                var settings = new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = stage.maxSlots };
                var seqs = BattleSolver.Sequences(settings);
                sb.AppendLine($"=== {name} {stage.stageTitle}");
                foreach (bool flip in new[] { false, true })
                {
                    var r = Bfs(BattleModel.FromStage(stage), seqs, flip, 6, 600, stage.playerMaxHp);
                    var clean = Bfs(BattleModel.FromStage(stage), seqs, flip, 6, 600, stage.playerMaxHp, cleanOnly: true);
                    sb.AppendLine($"  {(flip ? "案：最後の1体は反転" : "今のルール        ")}：最短 {Show(r)} ／ 無傷 {Show(clean)}");
                }
                // 最初の1体を倒した直後の盤面（今のルールの最短ルート上）で、残り1体に対する攻撃手順の被弾率
                var route = Bfs(BattleModel.FromStage(stage), seqs, false, 6, 600, stage.playerMaxHp);
                if (route.path != null)
                {
                    var m = BattleModel.FromStage(stage);
                    foreach (var turn in route.path)
                    {
                        TurnRules.PlayTurn(m, turn, null);
                        if (m.Enemies.Count(e => e.IsAlive) == 1) break;
                    }
                    if (m.Enemies.Count(e => e.IsAlive) == 1)
                    {
                        var last = m.Enemies.First(e => e.IsAlive);
                        foreach (var trait in new[] { CopyTrait.Normal, CopyTrait.Mirror })
                        {
                            last.CopyTrait = trait;
                            int attacks = 0, punished = 0;
                            foreach (var seq in seqs)
                            {
                                var c = m.Clone();
                                var ev = new List<BattleEvent>();
                                TurnRules.PlayTurn(c, seq, null, ev);
                                var dmg = ev.Where(e => e.Type == BattleEventType.Damaged).ToList();
                                if (!dmg.Any(e => e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct && !e.Unit.IsPlayer)) continue;
                                attacks++;
                                if (dmg.Any(e => e.Unit.IsPlayer)) punished++;
                            }
                            sb.AppendLine($"  1体目を倒した直後（{last.Name} が残る）：残りが{(trait == CopyTrait.Normal ? "通常" : "反転")}なら 攻撃手順の被弾率 {punished}/{attacks}（{(attacks == 0 ? 0 : 100.0 * punished / attacks):0}%）");
                        }
                        last.CopyTrait = CopyTrait.Normal;
                    }
                }
                sb.AppendLine();
                Debug.Log($"[SolverRunner] {name} 最後の1体の研究 完了");
            }
            Write("last_stand_report.txt", sb);
        }

        static string Show((int turns, int hp, List<List<CommandRecord>> path, bool approx) r) =>
            r.path == null ? "見つからず" : $"{r.turns}ターン（残りHP {r.hp}）{(r.approx ? "（近似）" : "")}";

        /// <summary>BattleSolver.Solve と同じ幅優先探索（ビーム）。flip なら、各ターンの開始時に敵が1体だけなら方向反転にする。</summary>
        static (int turns, int hp, List<List<CommandRecord>> path, bool approx) Bfs(BattleModel start, List<List<CommandRecord>> seqs, bool flip,
            int maxTurns, int maxStates, int maxHp, bool cleanOnly = false)
        {
            void Apply(BattleModel m)
            {
                if (!flip) return;
                var alive = m.Enemies.Where(e => e.IsAlive).ToList();
                if (alive.Count == 1) alive[0].CopyTrait = CopyTrait.Mirror;
            }
            var frontier = new List<(BattleModel m, List<List<CommandRecord>> path)> { (start.Clone(), new List<List<CommandRecord>>()) };
            var seen = new HashSet<string> { BattleSolver.Key(start) };
            bool approx = false;
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                (int, int, List<List<CommandRecord>>, bool) best = (0, -1, null, false);
                var next = new List<(BattleModel, List<List<CommandRecord>>)>();
                foreach (var (model, path) in frontier)
                    foreach (var seq in seqs)
                    {
                        var m = model.Clone();
                        Apply(m);
                        var o = TurnRules.PlayTurn(m, seq, null);
                        if (o == TurnOutcome.GameOver || (cleanOnly && m.Player.Hp < maxHp)) continue;
                        var np = new List<List<CommandRecord>>(path) { seq };
                        if (o == TurnOutcome.Cleared) { if (m.Player.Hp > best.Item2) best = (turn, m.Player.Hp, np, approx); continue; }
                        string key = BattleSolver.Key(m) + string.Join("", m.Enemies.Select(e => (int)e.CopyTrait));
                        if (seen.Add(key)) next.Add((m, np));
                    }
                if (best.Item3 != null) return best;
                if (next.Count == 0) break;
                if (next.Count > maxStates)
                {
                    approx = true;
                    next = next.OrderBy(x => x.Item1.Enemies.Where(e => e.IsAlive).Sum(e => e.Hp) * 100 - x.Item1.Player.Hp * 10).Take(maxStates).ToList();
                }
                frontier = next;
            }
            return (0, 0, null, approx);
        }

        static Dictionary<string, string> Parse(string line)
        {
            var dict = new Dictionary<string, string>();
            var parts = line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2) dict[parts[i].TrimEnd(':')] = parts[i + 1];
            return dict;
        }

        static void Analyze(StringBuilder sb, string name, string[] rows, List<CommandData> cards, int slots, int php, int ehp, int spike,
            CommandData innate, int turns, CopyTrait[] traits = null)
        {
            var start = BattleModel.FromRows(rows, spike, php, ehp);
            if (traits != null)
            {
                var enemies = start.Enemies.ToList();
                for (int i = 0; i < enemies.Count && i < traits.Length; i++) enemies[i].CopyTrait = traits[i];
                sb.AppendLine($"=== （コピー特性：{string.Join("・", enemies.Select(e => e.Name + "=" + e.CopyTrait))}）");
            }
            sb.AppendLine($"=== {name}");
            foreach (var r in rows) sb.AppendLine("    " + r);
            sb.AppendLine($"    カード {string.Join("・", cards.Select(c => c.displayName))}／最大{slots}命令／HP あなた{php} 敵{ehp}×{start.Enemies.Count()}／トゲ{spike}");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var basic = new BattleSolver.Settings { Cards = cards, MaxSlots = slots, MaxTurns = turns, Innate = innate, MaxStates = MaxStates };
            sb.AppendLine("    " + BattleSolver.FirstTurnProfile(start, basic));
            sb.AppendLine("    " + StallAndRepeat(start, basic, 8));
            sb.AppendLine("    " + ThreatProfile(start, basic));
            Report(sb, "最短（被弾あり）", start, basic, turns);
            Report(sb, "無傷", start, With(basic, s => s.MinPlayerHp = php), turns);
            Report(sb, "直接攻撃なし（コピー・同士討ち・トゲだけ）", start, With(basic, s => s.ForbidPlayerDirectDamage = true), turns);
            Report(sb, "移動カードだけ", start, With(basic, s => s.OnlyTypes = new HashSet<CommandType> { CommandType.Walk, CommandType.Rush }), turns);
            sb.AppendLine($"    （解析 {watch.Elapsed.TotalSeconds:0} 秒）");
            sb.AppendLine();
            Debug.Log($"[SolverRunner] {name} 完了 {watch.Elapsed.TotalSeconds:0}秒");
        }

        /// <summary>
        /// 永久待機と「同じ手の繰り返し」の確認。
        /// - 待機手：1ターン目に、盤面が何も変わらない（誰も動かず誰も傷つかない）手順の数。1つでもあれば永久待機できる。
        /// - 繰り返し：1ターン目の手順を毎ターンそのまま繰り返すだけでクリアできる手順の数と、最短の例。
        /// </summary>
        static string StallAndRepeat(BattleModel start, BattleSolver.Settings s, int turns)
        {
            var seqs = BattleSolver.Sequences(s);
            string key0 = BattleSolver.Key(start);
            int stall = 0, repeatClears = 0, bestTurns = int.MaxValue, bestHp = 0;
            List<CommandRecord> best = null;
            foreach (var seq in seqs)
            {
                var m = start.Clone();
                if (TurnRules.PlayTurn(m, seq, s.Innate) == TurnOutcome.Continue && BattleSolver.Key(m) == key0) stall++;
                var r = start.Clone();
                for (int t = 1; t <= turns; t++)
                {
                    var outcome = TurnRules.PlayTurn(r, seq, s.Innate);
                    if (outcome == TurnOutcome.GameOver) break;
                    if (outcome != TurnOutcome.Cleared) continue;
                    repeatClears++;
                    if (t < bestTurns || (t == bestTurns && r.Player.Hp > bestHp)) { bestTurns = t; bestHp = r.Player.Hp; best = seq; }
                    break;
                }
            }
            string repeat = best == null ? $"同じ手の繰り返しでは{turns}ターン以内にクリアできない"
                : $"同じ手の繰り返しでクリア {repeatClears} 通り（最短 {bestTurns}ターン・残りHP {bestHp}：{string.Join(" / ", best.Select(c => c.ToDisplayText()))}）";
            return $"待機手（盤面が変わらない手順）{stall} 通り ／ {repeat}";
        }

        /// <summary>
        /// 脅威とズレの目安（1ターン目の全手順から）。
        /// - 攻撃手順の被弾率：自分の直接攻撃が敵に当たった手順のうち、そのターンに自分が被弾した割合（「考えずに殴ると痛い」か）
        /// - ズレ発生率：コピーの結果が敵ごとに違う（片方は空振り・片方は誰かに命中 など）手順の割合
        /// - 1枚だけの繰り返しでクリア：同じ1命令を毎ターン繰り返すだけで勝てる命令の数（単純作業の検出）
        /// </summary>
        public static string ThreatProfile(BattleModel start, BattleSolver.Settings s)
        {
            int attacks = 0, punished = 0, total = 0, zure = 0, single = 0;
            var singles = new List<string>();
            foreach (var seq in BattleSolver.Sequences(s))
            {
                var m = start.Clone();
                var events = new List<BattleEvent>();
                TurnRules.PlayTurn(m, seq, s.Innate, events);
                total++;
                var dmg = events.Where(e => e.Type == BattleEventType.Damaged).ToList();
                bool playerHit = dmg.Any(e => e.Unit.IsPlayer);
                if (dmg.Any(e => e.Cause.Source.IsPlayer && e.Cause.Kind == DamageKind.Direct && !e.Unit.IsPlayer))
                {
                    attacks++;
                    if (playerHit) punished++;
                }
                // 敵ごとのコピー結果：0=何も当たらない 1=プレイヤーに当たる 2=敵に当たる 3=トゲを踏む
                var outcomes = start.Enemies.Select(enemy =>
                {
                    var mine = dmg.Where(e => e.Cause.ByCopy && e.Cause.Source.Id == enemy.Id).ToList();
                    if (mine.Any(e => e.Unit.IsPlayer)) return 1;
                    if (mine.Any(e => !e.Unit.IsPlayer && e.Unit.Id != enemy.Id)) return 2;
                    if (dmg.Any(e => e.Unit.Id == enemy.Id && e.Cause.Kind == DamageKind.Spike && e.Cause.Source.Id == enemy.Id)) return 3;
                    return 0;
                }).ToList();
                if (outcomes.Distinct().Count() > 1 && outcomes.Any(o => o != 0)) zure++;
                if (seq.Count == 1)
                {
                    var r = start.Clone();
                    for (int t = 1; t <= 10; t++)
                    {
                        var o = TurnRules.PlayTurn(r, seq, s.Innate);
                        if (o == TurnOutcome.GameOver) break;
                        if (o == TurnOutcome.Cleared) { single++; singles.Add($"{seq[0].ToDisplayText()}×{t}"); break; }
                    }
                }
            }
            string singleText = single == 0 ? "1枚だけの繰り返しでは勝てない" : $"1枚だけの繰り返しでクリア {single} 通り（{string.Join("、", singles)}）";
            return $"攻撃手順の被弾率 {punished}/{attacks}（{(attacks == 0 ? 0 : 100.0 * punished / attacks):0}%） ／ ズレ発生率 {zure}/{total}（{100.0 * zure / total:0}%） ／ {singleText}";
        }

        static BattleSolver.Settings With(BattleSolver.Settings b, System.Action<BattleSolver.Settings> change)
        {
            var s = new BattleSolver.Settings { Cards = b.Cards, MaxSlots = b.MaxSlots, MaxTurns = b.MaxTurns, Innate = b.Innate, MaxStates = b.MaxStates };
            change(s);
            return s;
        }

        /// <summary>1ターンに調べる状態数の上限（これを超えたら「打ち切り」と書く）。</summary>
        const int MaxStates = 600;

        static void Report(StringBuilder sb, string label, BattleModel start, BattleSolver.Settings s, int turns)
        {
            var r = BattleSolver.Solve(start, s, out bool truncated);
            string approx = truncated ? "（近似：状態が多いので有望な状態だけ残して探索）" : "";
            if (r == null)
            {
                sb.AppendLine($"  {label}：{turns}ターン以内では見つからず{approx}");
                return;
            }
            sb.AppendLine($"  {label}：{r.Turns}ターン（残りHP {r.PlayerHp}）{approx}");
            foreach (var line in r.TraceLines) sb.AppendLine("      " + line);
        }

        static void Write(string file, StringBuilder sb)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", file), sb.ToString(), Encoding.UTF8);
            Debug.Log($"[SolverRunner] Logs/{file} に書き出しました");
        }
    }
}
