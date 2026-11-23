using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【Puzzle v2（2026-10-06）】パズル中心 ACT1 v2 の部屋をボットで確かめる（面白さは測れない。設計ミスを見つける）。
    ///   RunCandidates：tools/puzzle_rooms.txt の候補（再コンパイル不要）→ Logs/puzzle_rooms.txt。PZ_CANDS＝部屋の Id（カンマ）・PZ_SEEDS（既定 4）
    ///   RunStages：アセットの部屋（StageP1〜StagePB）→ Logs/puzzle_stages.txt
    ///   RunAct：通し（最初のデッキのまま・HP 持ち越し・休息 +8。PZ_RUNS 既定 10・PZ_OFF=1 で COPY OFF）→ Logs/puzzle_act.txt
    /// 指標：勝率・ターン・被ダメ／直接依存（敵へのダメージのうち、あなたの直接攻撃の割合）／まね仕掛け（まねで仕掛けが動いた回数）／
    ///   進行（封印・扉・出口・運び紋）／固定攻撃（兵種・石像であなたが受けた量）／ループ最長／COPY OFF の差。
    /// </summary>
    public static class PuzzleStudy
    {
        static string Env(string k, string d) => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)) ? d : Environment.GetEnvironmentVariable(k);
        static readonly string[] StartDeck = { "Walk", "Walk", "Dash", "Dash", "Rush", "Slash", "Slash", "Slash", "Thrust", "PushSlash", "Push", "Pull" };
        static CommandData Command(string n) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{n}.asset");
        static StageData Asset(string n) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{n}.asset");

        public static readonly string[] Stages = { "StageP1", "StageP2", "StageP2b", "StageP3", "StageP3b", "StageP4", "StageP5", "StagePD1", "StagePD2", "StagePD3", "StagePB" };

        /// <summary>候補ファイルの1部屋（=== Id | goal=Carry | hp=6,6 | kinds=AM | facings=DD | statues=R | spin=1 | guard=1 | turns=20 の次の行から盤面）。</summary>
        public class Candidate
        {
            public string id, asset, label;
            public StageData stage;
            public int maxTurns = 15;
            /// <summary>総当たり（PuzzleSearch）の BAN：この出来事を起こさずに勝てるか（「新しい物を使わずに解ける」の確認）。</summary>
            public string ban;
        }

        /// <summary>候補ファイルを読む（asset= ラベル= title= tutorial=（\n で改行）goaltext= も）。</summary>
        public static List<Candidate> ParseCandidates(string path)
        {
            var list = new List<Candidate>();
            if (!File.Exists(path)) return list;
            bool combatFile = Path.GetFileName(path).StartsWith("combat"); // 【戦闘中心 v3】tools/combat_rooms.txt は Soft ルールの部屋
            Candidate cur = null; var rows = new List<string>();
            var blocks = new List<string>(); // 【完成版】"---" の行で区切った開始配置（最初＝map・残り＝altMaps）
            void Flush()
            {
                if (cur == null) return;
                blocks.Add(string.Join("\n", rows));
                cur.stage.map = blocks[0];
                cur.stage.altMaps = blocks.Skip(1).ToList();
                list.Add(cur);
                cur = null; rows.Clear(); blocks.Clear();
            }
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("# ") || line == "#" || line.StartsWith("//") || line.Length == 0) continue; // 盤面の行は # で始まることがある（柱）
                if (line.StartsWith("==="))
                {
                    Flush();
                    var parts = line.Substring(3).Split('|').Select(p => p.Trim()).ToList();
                    cur = new Candidate { id = parts[0] };
                    var s = ScriptableObject.CreateInstance<StageData>();
                    cur.stage = s;
                    string id = cur.id;
                    s.name = "Cand_" + id;
                    s.stageTitle = id;
                    s.playerMaxHp = 20; s.enemyMaxHp = 8; s.spikeDamage = 3; s.maxSlots = 3; s.puzzleRoom = !combatFile; s.combatRoom = combatFile;
                    if (Path.GetFileName(path).Contains("v4")) { s.combatV4 = true; s.combatRoom = true; s.puzzleRoom = false; } // 【Combat V4】
                    var deck = StartDeck.Select(Command).ToList();
                    s.handTestDeckLarge = deck; s.handTestDeck = new List<CommandData>(deck);
                    foreach (var kv in parts.Skip(1))
                    {
                        int eq = kv.IndexOf('=');
                        if (eq < 0) continue;
                        string k = kv.Substring(0, eq).Trim(), v = kv.Substring(eq + 1).Trim();
                        switch (k)
                        {
                            case "goal": s.goal = (RoomGoal)Enum.Parse(typeof(RoomGoal), v); break;
                            case "hp": s.enemyHpList = v; break;
                            case "kinds": s.enemyCopyTypes = v; break;
                            case "facings": s.enemyFacings = v; break;
                            case "statues": s.statueFacings = v; break;
                            case "spin": s.statueSpin = v == "1"; break;
                            case "guard": s.coreGuard = v == "1"; break;
                            case "circle": s.circleGuard = v == "1"; break;
                            case "turns": cur.maxTurns = int.Parse(v); break;
                            case "asset": cur.asset = v; break;
                            case "ban": cur.ban = v; break;
                            case "label": cur.label = v; break;
                            case "title": s.stageTitle = v; break;
                            case "tutorial": s.tutorialText = v.Replace("\\n", "\n"); break;
                            case "goaltext": s.goalText = v; break;
                            case "limit": s.turnLimit = int.Parse(v); break;
                            case "noclocks": s.noClocks = v == "1"; break;            // 【RC3】大技なし
                            case "still": s.stillEnemies = v == "1"; break;          // 【RC11】敵は自分では動かない
                            case "bigs": s.clockKeep = int.Parse(v); break; // 【文化祭】大技は先頭の N 個だけ
                            case "aim": s.bomberAim = v; break;                      // 【RC11】砲兵の最初の照準（列,行）
                            case "ready": s.clocksReady = v != "0" && v.Length > 0; s.clocksReadyOnly = v == "1" ? "" : v; break;           // 【RC11】大技を始めから①
                            case "tutorial_step": s.tutorialStep = int.Parse(v); break; // 【RC3】チュートリアルの番号
                            case "deckcards": var dc = v.Split(',').Select(x => Command(x.Trim())).Where(x => x != null).ToList(); s.availableCommands = dc; break; // 【RC3】チュートリアルの手札            // 【戦闘中心 v3】試練のターン数
                            case "v4": s.combatV4 = v == "1"; if (s.combatV4) { s.combatRoom = true; s.puzzleRoom = false; } break; // 【Combat V4】
                            case "fire": s.firePatterns = v.Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); break;  // 動く危険：1ラウンド分＝行を / で区切る・F が危険のマス
                            case "firedmg": s.fireDamage = int.Parse(v); break;
                            case "hazard": s.hazardName = v; break;
                            case "combat": s.combatRoom = v == "1"; s.puzzleRoom = v != "1"; break;
                            case "start": s.playerStartHp = int.Parse(v); break;
                            case "lab": s.sandboxGoal = v; break;                      // 研究室の一覧に出す一言
                            case "spike": s.spikeDamage = int.Parse(v); break;
                            case "deck": var d = v.Split(',').Select(x => Command(x.Trim())).Where(x => x != null).ToList(); s.handTestDeckLarge = d; s.handTestDeck = new List<CommandData>(d); break;
                        }
                    }
                    continue;
                }
                if (cur != null && line.Trim() == "---") { blocks.Add(string.Join("\n", rows)); rows.Clear(); continue; } // 【完成版】開始配置の別案
                if (cur != null) rows.Add(line.Trim());
            }
            Flush();
            return list;
        }

        public static void RunCandidates()
        {
            if (int.TryParse(Env("PZ_BOSSDMG", ""), out var bd)) PuzzleBoss.SuccessDamage = bd; // 比較用（終わったら戻す）
            var cands = ParseCandidates(Env("PZ_ROOMS", "tools/puzzle_rooms.txt")); // PZ_ROOMS＝別の候補ファイル（直す前の比べ用）
            var only = Env("PZ_CANDS", "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (only.Count > 0) cands = cands.Where(c => only.Contains(c.id)).ToList();
            Report(cands.Select(c => (c.id, c.stage, c.maxTurns)).ToList(), Env("PZ_OUT", "Logs/puzzle_rooms.txt"));
        }

        /// <summary>
        /// 手順の再生：PZ_PROBE＝「部屋Id;札:向き[:距離],…|次のターン…」→ Logs/puzzle_probe.txt（盤面とログ）。
        /// PZ_PROBE_FILE＝1行1手順のファイル（# で始まる行は飛ばす。「名前@@手順」で名前つき）→ 全部を1回で再生（レビューの指摘の確認用）。
        /// 札は手札に関係なく使える（手札の偶然は別に考える）。勝ち負けが決まったら残りのターンは流さない。
        /// </summary>
        public static void RunProbe()
        {
            var specs = new List<(string name, string spec)>();
            var file = Env("PZ_PROBE_FILE", "");
            if (file.Length > 0 && File.Exists(file))
            {
                foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int at = line.IndexOf("@@");
                    specs.Add(at >= 0 ? (line.Substring(0, at).Trim(), line.Substring(at + 2).Trim()) : (line, line));
                }
            }
            else specs.Add(("probe", Env("PZ_PROBE", "P1;Dash:D,Dash:D")));
            var cands = ParseCandidates(Env("PZ_ROOMS", "tools/puzzle_rooms.txt"));
            var sb = new StringBuilder();
            var summary = new StringBuilder("【まとめ】名前 → 結果・ターン・あなたの HP・敵の HP\n");
            foreach (var (name, spec) in specs)
            {
                try { ProbeOne(name, spec, cands, sb, summary); }
                catch (Exception ex) { sb.AppendLine($"=== {name}：読めない手順（{ex.Message}）\n"); summary.AppendLine($"{name} → 読めない手順（{ex.Message}）"); }
            }
            Write(new StringBuilder(summary + "\n" + sb), Env("PZ_PROBE_OUT", "Logs/puzzle_probe.txt"));
        }

        static void ProbeOne(string name, string spec, List<Candidate> cands, StringBuilder sb, StringBuilder summary)
        {
            var parts = spec.Split(';');
            var cand = cands.FirstOrDefault(c => c.id == parts[0].Trim());
            if (cand == null) { sb.AppendLine($"=== {name}：部屋 {parts[0]} が見つからない\n"); summary.AppendLine($"{name} → 部屋が見つからない"); return; }
            var m = BattleModel.FromStage(cand.stage);
            sb.AppendLine($"=== {name}（{cand.id} goal={m.Goal}）手順 {parts[1]}");
            sb.AppendLine(Dump(m));
            var o = TurnOutcome.Continue;
            int t = 0;
            foreach (var turnSpec in parts[1].Split('|'))
            {
                if (o != TurnOutcome.Continue) break;
                t++;
                var seq = new List<CommandRecord>();
                foreach (var c in turnSpec.Split(','))
                {
                    var f = c.Trim().Split(':');
                    var d = f[1] == "U" ? Direction.Up : f[1] == "D" ? Direction.Down : f[1] == "L" ? Direction.Left : Direction.Right;
                    var cmd = Command(f[0]);
                    if (cmd == null) throw new Exception("札が無い：" + f[0]);
                    seq.Add(new CommandRecord(cmd, d, f.Length > 2 ? int.Parse(f[2]) : cmd.minDistance));
                }
                var log = new List<string>();
                o = TurnRules.PlayTurn(m, seq, null, new List<BattleEvent>(), log);
                sb.AppendLine($"T{t}：{turnSpec}");
                foreach (var l in log) sb.AppendLine("  " + l);
                sb.AppendLine($"→ {o}");
                sb.AppendLine(Dump(m));
            }
            summary.AppendLine($"{name} → {o}・{t}T・あなた HP {m.Player.Hp}・敵 {string.Join(" ", m.Enemies.Select(e => $"{e.Name}{(e.IsAlive ? e.Hp.ToString() : "×")}{(e.Kind == EnemyKind.SealLord ? $"(段階{e.BossStage + 1}守り{e.Barrier})" : "")}"))}");
        }

        static string Dump(BattleModel m)
        {
            var sb = new StringBuilder();
            for (int y = m.Board.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < m.Board.Width; x++)
                {
                    var c = new Vector2Int(x, y);
                    var u = m.GetUnitAt(c);
                    char ch = u != null ? (u.IsPlayer ? 'P' : u.Kind == EnemyKind.Golem ? 'D' : u.Kind == EnemyKind.Cannon ? 'Z' : (u.Label.Length > 0 ? u.Label[0] : 'E'))
                            : m.Board.IsSealStone(c) ? 's' : m.Board.IsStatue(c) ? 'i' : m.Board.IsCore(c) ? 'c' : m.Board.IsWall(c) ? '#' : m.Board.Exits.Contains(c) ? 'x' : m.Board.CarryMarks.Contains(c) ? 'v' : m.Board.HasSpike(c) ? '^' : m.Board.IsSeal(c) ? 'o' : '.';
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            sb.AppendLine("  " + string.Join("　", m.Units.Where(u => u.IsAlive).Select(u => $"{u.Name}{u.Position} HP{u.Hp}{(u.Kind == EnemyKind.SealLord ? $" 段階{u.BossStage + 1} 守り{u.Barrier}" : "")}")));
            return sb.ToString();
        }

        public static void RunStages()
        {
            var only = Env("PZ_CANDS", "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            var list = Stages.Where(n => only.Count == 0 || only.Contains(n)).Select(n => (n, Asset(n), n == "StagePB" ? 22 : 15)).Where(x => x.Item2 != null).ToList();
            Report(list, "Logs/puzzle_stages.txt");
        }

        static void Report(List<(string id, StageData stage, int maxTurns)> rooms, string path)
        {
            int seeds = int.Parse(Env("PZ_SEEDS", "4"));
            var watch = Stopwatch.StartNew();
            var sb = new StringBuilder($"【Puzzle v2】部屋のボット解析（各 {seeds} 戦・HP 20・最初のデッキ・手札7・3命令）\n");
            sb.AppendLine("列：勝率・平均ターン・被ダメ／直接＝敵へのダメージのうちあなたの直接攻撃の割合／まね仕掛け＝まねで仕掛けが動いた回数（1戦平均）／進行＝封印・扉・出口・運び紋／固定＝兵種・石像で受けた量／ループ＝同じ手順でまねが削り続けた最長\n");
            CopyCore.SandboxCopyOff = false; CopyCore.SandboxMode = null;
            foreach (var (id, stage, maxTurns) in rooms)
            {
                Environment.SetEnvironmentVariable("CV2_MAXTURNS", maxTurns.ToString());
                sb.AppendLine($"=== {id}（{stage.goal}）");
                foreach (var row in new[] { ("読み手 ON ", CopyCoreStudy.Reader, true), ("読み手 OFF", CopyCoreStudy.Reader, false), ("人寄り ON ", CopyCoreStudy.Human, true), ("読まない ON", CopyCoreStudy.Naive, true) })
                {
                    var outs = new List<CopyCoreStudy.Out>();
                    bool trace = Env("PZ_TRACE", "0") == "1" && row.Item2 == CopyCoreStudy.Reader && row.Item3;
                    if (trace) CopyCoreStudy.Trace = new StringBuilder($"=== {id}\n");
                    for (int s = 1; s <= seeds; s++) outs.Add(CopyCoreStudy.Battle(stage, CopyMode.Current, row.Item3, row.Item2, new Random(s * 7919 + 17)));
                    if (trace) { File.WriteAllText("Logs/puzzle_trace.txt", CopyCoreStudy.Trace.ToString(), new UTF8Encoding(false)); CopyCoreStudy.Trace = null; }
                    sb.AppendLine($"  {row.Item1}：{Line(outs)}");
                    Write(sb, path);
                }
                sb.AppendLine();
                Debug.Log($"[PuzzleStudy] {id} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            Environment.SetEnvironmentVariable("CV2_MAXTURNS", null);
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }

        static string Line(List<CopyCoreStudy.Out> o)
        {
            int n = o.Count;
            var wins = o.Where(x => x.Win).ToList();
            int enemyDmg = o.Sum(x => x.EnemyDamage), direct = o.Sum(x => x.DirectDamage);
            return $"勝ち {CopyCoreStudy.Pct(wins.Count, n)}・{(wins.Count > 0 ? wins.Average(x => x.Turns).ToString("0.0") : "—")}T・被ダメ {o.Average(x => x.Taken):0.0}" +
                   $"・直接 {(enemyDmg == 0 ? "—" : CopyCoreStudy.Pct(direct, enemyDmg))}・まね仕掛け {o.Average(x => x.CopyGimmicks):0.0}・進行 {o.Average(x => x.ProgressEvents):0.0}" +
                   $"・固定 {o.Average(x => x.FixedHits):0.0}・ループ {o.Max(x => x.LoopStreak)}・死亡 {o.Count(x => x.Dead)}";
        }

        static void Write(StringBuilder sb, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>通し（地図の道順を種で決める。報酬なし＝最初のデッキのまま・HP 持ち越し・休息 +8）。</summary>
        public static void RunAct()
        {
            int runs = int.Parse(Env("PZ_RUNS", "10"));
            bool off = Env("PZ_OFF", "0") == "1";
            string path = off ? "Logs/puzzle_act_off.txt" : "Logs/puzzle_act.txt";
            var sb = new StringBuilder($"【Puzzle v2】パズル中心 ACT1 v2 を通しで（{runs} ラン・報酬なし＝最初のデッキのまま・HP 持ち越し・休息 +8{(off ? "・COPY OFF" : "")}）\n\n");
            var watch = Stopwatch.StartNew();
            foreach (var bot in new[] { CopyCoreStudy.Reader, CopyCoreStudy.Human, CopyCoreStudy.Naive })
            {
                int clears = 0, reachedBoss = 0, hpAtBoss = 0, turnsSum = 0;
                var stops = new Dictionary<string, int>();
                var roomTurns = new Dictionary<string, List<int>>();
                for (int r = 1; r <= runs; r++)
                {
                    var rng = new Random(5100 + r);
                    var route = new List<string>
                    {
                        "StageP1", rng.Next(2) == 0 ? "StageP2" : "StageP2b", rng.Next(2) == 0 ? "StageP3" : "StageP3b", "REST",
                        rng.Next(2) == 0 ? "StageP4" : rng.Next(2) == 0 ? "StagePD1" : "StagePD3", rng.Next(2) == 0 ? "StageP5" : "StagePD2", "StagePB",
                    };
                    int hp = 20, turns = 0;
                    bool dead = false;
                    foreach (var n in route)
                    {
                        if (n == "REST") { hp = Math.Min(20, hp + 8); continue; }
                        var st = Asset(n);
                        if (st == null) continue;
                        if (n == "StagePB") { reachedBoss++; hpAtBoss += hp; }
                        Environment.SetEnvironmentVariable("CV2_MAXTURNS", n == "StagePB" ? "22" : "15");
                        var o = CopyCoreStudy.Battle(st, CopyMode.Current, !off, bot, new Random(r * 131 + n.GetHashCode()), hp);
                        turns += o.Turns;
                        if (!roomTurns.TryGetValue(n, out var l)) roomTurns[n] = l = new List<int>();
                        l.Add(o.Turns);
                        if (!o.Win) { dead = true; stops[n] = stops.TryGetValue(n, out var d) ? d + 1 : 1; break; }
                        hp = o.HpLeft;
                    }
                    if (!dead) clears++;
                    turnsSum += turns;
                }
                Environment.SetEnvironmentVariable("CV2_MAXTURNS", null);
                sb.AppendLine($"{bot.Name}：クリア {CopyCoreStudy.Pct(clears, runs)}・ボス到達 {CopyCoreStudy.Pct(reachedBoss, runs)}（到達時 HP 平均 {(reachedBoss == 0 ? 0 : hpAtBoss / (double)reachedBoss):0.0}）・合計ターン平均 {turnsSum / (double)runs:0.0}" +
                              $"・止まった部屋 {(stops.Count == 0 ? "なし" : string.Join("／", stops.Select(kv => $"{kv.Key} {kv.Value}")))}");
                sb.AppendLine("  部屋ごとのターン：" + string.Join("・", roomTurns.Select(kv => $"{kv.Key} {kv.Value.Average():0.0}")));
                Write(sb, path);
                Debug.Log($"[PuzzleStudy] act {bot.Name} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }
    }
}
