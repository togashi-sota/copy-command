using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using static CopyCommand.EditorTools.LargeHandStudy;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究】ACT1 の緊張感（2026-10-05）：通常戦の「コピーが怖い」を数字にして、盤面の候補を比べる。
    ///   RunRooms ：部屋ごと × ボット4種（Expert / Human-like慎重 / Medium / Reckless）→ Logs/act1_rooms_{ACT1_TAG}.txt
    ///              環境変数 ACT1_CANDS＝調べる候補（tools/act1_rooms.txt の [id]、または部屋 Id＝今の盤面）、ACT1_SEEDS＝1部屋1ボットの戦闘数（既定 10）、ACT1_BOTS＝ボットの番号（0〜3）
    ///   RunCopyAB：同じ盤面・同じ手札で「コピーあり／なし」を Expert で比べる（コピー税）→ Logs/act1_copy_ab_{tag}.txt
    ///   RunAct   ：ACT1 を通しで（標準の役職・ボット4種）→ Logs/act1_run_{tag}.txt。ACT1_USE＝差し替える候補
    ///   RunRolesV2：6役職（Human-like慎重）→ Logs/act1_roles_{tag}.txt
    ///   RunMultiV2：命令の上限 3〜5（Human-like・Expert）→ Logs/act1_multi_{tag}.txt
    /// 手札：偶数の種は最初のデッキ（12枚）、奇数の種は最初のデッキ＋報酬3枚（中盤のデッキの代わり）。
    /// </summary>
    public static class Act1Tension
    {
        static string Env(string k, string d) => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)) ? d : Environment.GetEnvironmentVariable(k);
        static string Tag => Env("ACT1_TAG", "now");
        static int Seeds => int.Parse(Env("ACT1_SEEDS", "10"));
        static int SeedBase => int.Parse(Env("ACT1_SEEDBASE", "0"));
        static Act1Sim.Bot[] Bots => Env("ACT1_BOTS", "0,1,2,3").Split(',').Select(s => Act1Sim.FourBots[int.Parse(s)]).ToArray();

        /// <summary>ACT1_USE の候補を部屋に当てる（同じ部屋の候補は後の物が勝つ）。</summary>
        static void ApplyUse()
        {
            Act1Sim.RoomOverrides = new Dictionary<string, Act1Sim.RoomBoard>();
            var boards = Act1Sim.LoadBoards();
            foreach (var id in Env("ACT1_USE", "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                if (boards.TryGetValue(id, out var b)) Act1Sim.RoomOverrides[b.Room] = b;
        }

        /// <summary>調べる対象：(表示名, 部屋, 盤面の候補 or null)。</summary>
        static List<(string label, RoomDef room, Act1Sim.RoomBoard board)> Targets()
        {
            var boards = Act1Sim.LoadBoards();
            var list = new List<(string, RoomDef, Act1Sim.RoomBoard)>();
            string cands = Env("ACT1_CANDS", "thorn,warp,spring,mirror,hall");
            foreach (var id in cands.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (boards.TryGetValue(id, out var b)) list.Add(($"{id}（{ActContent.Room(b.Room).Name}）", ActContent.Room(b.Room), b));
                else list.Add(($"{id}（今の盤面）", ActContent.Room(id), null));
            }
            return list;
        }

        static ActRun MakeRun(int seed)
        {
            var run = new ActRun(ActContent.Roles[0], seed, Act1Sim.C);
            if (seed % 2 == 1)
            {
                var rng = new Random(seed * 31);
                var pool = new[] { "PushSlash", "Thrust", "Sweep", "Pierce", "Pull", "Trap", "Jump", "Rush", "Whirl", "Spirit" };
                for (int i = 0; i < 3; i++) run.Deck.Add(Act1Sim.C(pool[rng.Next(pool.Length)]));
            }
            return run;
        }

        public class Agg
        {
            public readonly List<Act1Sim.BattleOut> B = new List<Act1Sim.BattleOut>();
            public string Line()
            {
                if (B.Count == 0) return "—";
                int turns = B.Sum(b => b.MeasuredTurns);
                string copy = turns == 0 ? "" :
                    $"　コピー圧力：罠 {Act1Sim.Pct(B.Sum(b => b.TrapTurns), turns)}・制約 {Act1Sim.Pct(B.Sum(b => b.ConstraintTurns), turns)}・税 {B.Sum(b => b.CopyTax) / turns:0.0}/T" +
                    $"・被ダメ内訳 直接 {B.Average(b => b.CopyDirect):0.0}/誘発 {B.Average(b => b.CopyInduced):0.0}/危険マス {B.Average(b => b.CopyHazard):0.0}";
                return $"勝ち {Act1Sim.Pct(B.Count(b => b.Stats.Won), B.Count)}・{B.Average(b => b.Turns):0.0}T・被ダメ {B.Average(b => b.GrossTaken):0.0}（最大 {B.Max(b => b.GrossTaken)}）・無傷 {Act1Sim.Pct(B.Count(b => b.GrossTaken == 0), B.Count)}" +
                       $"・使ったマス {B.Average(b => b.Cells.Count):0.0}・壁で止まった敵 {B.Average(b => b.WallBlocks):0.0}・回復マス 自分{B.Sum(b => b.HealTilesMine)}/敵{B.Sum(b => b.HealTilesEnemy)}{copy}";
            }
        }

        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 緊張感（部屋 × ボット4種）")]
        public static void RunRooms()
        {
            var watch = Stopwatch.StartNew();
            var targets = Targets();
            var bots = Bots;
            string path = $"Logs/act1_rooms_{Tag}.txt";
            var sb = new StringBuilder($"【研究】部屋ごと × ボット（{string.Join("・", bots.Select(b => b.Name))}）・1部屋1ボット {Seeds} 戦・HP 20・標準の役職（奇数の種は報酬3枚入りのデッキ）\n" +
                                       "無傷＝その戦闘で1ダメージも受けなかった割合。コピー圧力（Human 系で測る）：罠＝コピーを無視した最善手が実際は被弾するターン／制約＝コピーのせいで最善手を変えたターン／税＝その点数差（与ダメ−被弾×重さ）\n\n");
            var examples = new StringBuilder();
            foreach (var (label, room, board) in targets)
            {
                Act1Sim.RoomOverrides = new Dictionary<string, Act1Sim.RoomBoard>();
                if (board != null) Act1Sim.RoomOverrides[room.Id] = board;
                sb.AppendLine($"=== {label}" + (board?.Note != null ? $"　［{board.Note}］" : ""));
                foreach (var bot in bots)
                {
                    var agg = new Agg();
                    for (int s = SeedBase; s < SeedBase + Seeds; s++)
                    {
                        var run = MakeRun(s);
                        bool measure = bot == Act1Sim.Human || bot == Act1Sim.HumanFast;
                        var b = Act1Sim.Battle(run, room, bot, new Random(1234 + s * 101 + room.Id.GetHashCode()), -1, measure);
                        agg.B.Add(b);
                        if (bot == Act1Sim.Human) foreach (var ex in b.Examples.Take(1)) if (examples.Length < 60000) examples.AppendLine("・" + ex);
                    }
                    sb.AppendLine($"  {bot.Name,-14}：{agg.Line()}" + (room.ParTurns > 0 ? $"・速さの目安 {room.ParTurns}T 以内 {Act1Sim.Pct(agg.B.Count(x => x.Stats.Won && x.Turns <= room.ParTurns), agg.B.Count)}" : ""));
                    Write(sb, path);
                }
                Debug.Log($"[Act1Tension] {label} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            sb.AppendLine($"\n（{watch.Elapsed.TotalMinutes:0.0}分）\n\n=== コピーの罠の例（Human-like慎重が見た場面。最初の1つずつ）\n");
            sb.Append(examples);
            Write(sb, path);
        }

        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 コピーあり／なし")]
        public static void RunCopyAB()
        {
            var watch = Stopwatch.StartNew();
            string path = $"Logs/act1_copy_ab_{Tag}.txt";
            var sb = new StringBuilder($"【研究】コピーあり／なし（同じ盤面・同じ手札・同じ種）。ボット Expert と Human-like慎重・{Seeds} 戦ずつ\n「なし」＝敵はあなたの命令をコピーしない（兵種の行動・環境はある）。差がコピーの重さ（コピー税）\n\n");
            foreach (var (label, room, board) in Targets())
            {
                Act1Sim.RoomOverrides = new Dictionary<string, Act1Sim.RoomBoard>();
                if (board != null) Act1Sim.RoomOverrides[room.Id] = board;
                sb.AppendLine($"=== {label}");
                foreach (var bot in new[] { Act1Sim.Expert, Act1Sim.Human })
                    foreach (bool noCopy in new[] { false, true })
                    {
                        var agg = new Agg();
                        var played = new Dictionary<string, int>();
                        for (int s = 0; s < Seeds; s++)
                        {
                            var b = Act1Sim.Battle(MakeRun(s), room, bot, new Random(1234 + s * 101 + room.Id.GetHashCode()), -1, false, noCopy);
                            agg.B.Add(b);
                            foreach (var kv in b.Played) played[kv.Key] = (played.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
                        }
                        int tot = Math.Max(1, played.Values.Sum());
                        sb.AppendLine($"  {bot.Name,-14} コピー{(noCopy ? "なし" : "あり")}：{agg.Line()}");
                        sb.AppendLine($"      使った札：{string.Join("・", played.OrderByDescending(k => k.Value).Take(8).Select(k => $"{Act1Sim.C(k.Key).displayName} {100 * k.Value / tot}%"))}");
                        Write(sb, path);
                    }
                Debug.Log($"[Act1Tension] AB {label} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            Write(sb, path);
        }

        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 通し（ボット4種）")]
        public static void RunAct()
        {
            ApplyUse();
            int n = int.Parse(Env("ACT1_RUNS", "12"));
            string path = $"Logs/act1_run_{Tag}.txt";
            var sb = new StringBuilder($"【研究】ACT1 を通しで（標準の役職・ボット4種・各 {n} ラン）。差し替え：{Env("ACT1_USE", "なし")}\n\n");
            var watch = Stopwatch.StartNew();
            foreach (var bot in Bots)
            {
                var list = new List<Act1Sim.RunOut>();
                for (int s = 1; s <= n; s++) list.Add(Act1Sim.PlayRun(ActContent.Roles[0], 7000 + s, bot, null, bot == Act1Sim.Human));
                sb.Append(Act1Sim.Summary(bot.Name, list));
                var bs = list.SelectMany(r => r.Battles).Where(x => x.b.MeasuredTurns > 0).ToList();
                if (bs.Count > 0)
                {
                    int t = bs.Sum(x => x.b.MeasuredTurns);
                    sb.AppendLine($"  コピー圧力（通常戦）：罠 {Act1Sim.Pct(bs.Where(x => x.room.Kind == RoomKind.Battle).Sum(x => x.b.TrapTurns), bs.Where(x => x.room.Kind == RoomKind.Battle).Sum(x => x.b.MeasuredTurns))}" +
                                  $"・全体 罠 {Act1Sim.Pct(bs.Sum(x => x.b.TrapTurns), t)}・制約 {Act1Sim.Pct(bs.Sum(x => x.b.ConstraintTurns), t)}・被ダメ内訳 直接 {bs.Sum(x => x.b.CopyDirect)}/誘発 {bs.Sum(x => x.b.CopyInduced)}/危険マス {bs.Sum(x => x.b.CopyHazard)}\n");
                }
                Write(sb, path);
                Debug.Log($"[Act1Tension] run {bot.Name} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }

        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 役職（Human-like）")]
        public static void RunRolesV2()
        {
            ApplyUse();
            int n = int.Parse(Env("ACT1_RUNS", "12"));
            string path = $"Logs/act1_roles_{Tag}.txt";
            var sb = new StringBuilder($"【研究】6役職 × {n} ラン（Human-like慎重）。差し替え：{Env("ACT1_USE", "なし")}\n\n");
            var all = new Dictionary<string, List<Act1Sim.RunOut>>();
            foreach (var role in ActContent.Roles)
            {
                var list = new List<Act1Sim.RunOut>();
                for (int s = 1; s <= n; s++) list.Add(Act1Sim.PlayRun(role, 8000 + s, Act1Sim.Human));
                all[role.Name] = list;
                sb.Append(Act1Sim.Summary(role.Name, list));
                Write(sb, path);
            }
            sb.Append(Act1Sim.CrossSummary(all));
            Write(sb, path);
        }

        /// <summary>
        /// 【研究】各部屋の1ターン目の「コピーの罠」：決まった手札（一歩・駆け足・突進・斬撃・突き・押し斬り・引き寄せ）で、
        /// コピーを無視すると一番良く見える手（実際は被弾）と、被弾しない一番良い手を出す → Logs/act1_traps_{tag}.txt（テストとリプレイ例に使う）。
        /// </summary>
        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 コピーの罠（1ターン目）")]
        public static void RunTraps()
        {
            string path = $"Logs/act1_traps_{Tag}.txt";
            var sb = new StringBuilder("【研究】1ターン目のコピーの罠（手札：一歩・駆け足・突進・斬撃・突き・押し斬り・引き寄せ。HP 20）\n\n");
            var hand = new[] { "Walk", "Dash", "Rush", "Slash", "Thrust", "PushSlash", "Pull" }.Select(Act1Sim.C).ToList();
            var bot = new Act1Sim.Bot { Name = "試算", Penalty = 2.5f };
            foreach (var (label, room, board) in Targets())
            {
                Act1Sim.RoomOverrides = new Dictionary<string, Act1Sim.RoomBoard>();
                if (board != null) Act1Sim.RoomOverrides[room.Id] = board;
                var stage = Act1Sim.StageFor(room, out int hp);
                stage.playerMaxHp = 20; stage.playerStartHp = 20;
                if (hp > 0) stage.enemyMaxHp = hp;
                var m = BattleModel.FromStage(stage);
                UnityEngine.Object.DestroyImmediate(stage);
                var seqs = Act1Sim.Sequences(hand, 3, new Random(1), 100000);
                var all = new HashSet<int>(m.Enemies.Select(e => e.Id));
                var real = Act1Sim.EvalAll(m, seqs, 0, bot, null);
                var naive = Act1Sim.EvalAll(m, seqs, 0, bot, all);
                var order = Enumerable.Range(0, seqs.Count).OrderByDescending(i => naive[i].Score).ToList();
                int trap = order.FirstOrDefault(i => real[i].Taken > 0 && naive[i].Taken == 0);
                var safe = Enumerable.Range(0, seqs.Count).Where(i => real[i].Taken <= 0).OrderByDescending(i => real[i].Dealt).ThenBy(i => seqs[i].Count).FirstOrDefault();
                int safeCount = Enumerable.Range(0, seqs.Count).Count(i => real[i].Taken <= 0);
                int naiveSafe = Enumerable.Range(0, seqs.Count).Count(i => naive[i].Taken <= 0);
                sb.AppendLine($"=== {label}");
                sb.AppendLine(Act1Sim.Render(m));
                sb.AppendLine($"  命令列 {seqs.Count} 本：被弾しない手 {safeCount} 本（コピーを無視すると {naiveSafe} 本に見える）・一番良く見える手の順位で最初の罠＝{order.IndexOf(trap) + 1}位");
                sb.AppendLine($"  罠：「{Act1Sim.Text(seqs[trap])}」→ 被弾 {real[trap].Taken}・与ダメ {real[trap].Dealt}（コピーが無ければ被弾 0・与ダメ {naive[trap].Dealt}）");
                sb.AppendLine($"  安全：「{Act1Sim.Text(seqs[safe])}」→ 被弾 {real[safe].Taken}・与ダメ {real[safe].Dealt}");
                var ev = new List<BattleEvent>(); var log = new List<string>();
                TurnRules.PlayTurn(m.Clone(), seqs[trap], null, ev, log);
                sb.AppendLine("  罠の手のログ：\n    " + string.Join("\n    ", log));
                sb.AppendLine();
                Write(sb, path);
            }
        }

        /// <summary>
        /// 【研究】応急処置（自分を回復・敵もコピーで回復）の回復量 3／4／5 を比べる：最初のデッキ＋応急処置2枚で、通常戦5部屋とボス（Human-like慎重・HP 14 から＝少し削られた状態）。
        /// → Logs/act1_heal_{tag}.txt
        /// </summary>
        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 回復カード")]
        public static void RunHealCard()
        {
            ApplyUse();
            string path = $"Logs/act1_heal_{Tag}.txt";
            var sb = new StringBuilder($"【研究】応急処置の回復量（最初のデッキ＋応急処置2枚・HP 14/20 から・Human-like慎重・{Seeds} 戦ずつ）。なし＝応急処置を入れない\n\n");
            var rooms = ActContent.Rooms.Where(r => r.Kind == RoomKind.Battle || r.Kind == RoomKind.Boss).ToList();
            var baseCard = Act1Sim.C("FirstAid");
            foreach (int amount in new[] { 0, 3, 4, 5 })
            {
                CommandData card = null;
                if (amount > 0) { card = UnityEngine.Object.Instantiate(baseCard); card.name = "FirstAid"; card.range = amount; }
                foreach (var room in rooms)
                {
                    var agg = new Agg();
                    int used = 0;
                    for (int s = SeedBase; s < SeedBase + Seeds; s++)
                    {
                        var run = new ActRun(ActContent.Roles[0], s, Act1Sim.C);
                        run.SetHpForStudy(14);
                        if (card != null) { run.Deck.Add(card); run.Deck.Add(card); }
                        var b = Act1Sim.Battle(run, room, Act1Sim.Human, new Random(4321 + s * 13 + room.Id.GetHashCode()));
                        agg.B.Add(b);
                        used += b.Played.TryGetValue("FirstAid", out var p) ? p : 0;
                    }
                    sb.AppendLine($"  回復 {(amount == 0 ? "なし" : amount.ToString())}　{room.Name,-8}：{agg.Line()}・応急処置を使った回数 {used}・終わりの HP {agg.B.Average(b => b.Stats.HpAfter):0.0}");
                    Write(sb, path);
                }
                sb.AppendLine();
            }
            Write(sb, path);
        }

        [UnityEditor.MenuItem("CopyCommand/解析：ACT1 多命令（Human-like・Expert）")]
        public static void RunMultiV2()
        {
            ApplyUse();
            string path = $"Logs/act1_multi_{Tag}.txt";
            var sb = new StringBuilder($"【研究】多命令（敵も同じ数をコピー）× 再設計の案。通常戦5部屋・{Seeds} 戦ずつ。差し替え：{Env("ACT1_USE", "なし")}\n" +
                                       "案：0＝今／1＝B 4命令以上のラウンドは兵種の行動 +1／2＝C 共鳴（4命令以上のラウンドごとに +1 がたまる）／3＝D 4つ目以降は敵が2回コピー／4＝長期戦：6ラウンド目から兵種の行動 +1\n\n");
            var rooms = ActContent.Rooms.Where(r => r.Kind == RoomKind.Battle).ToList();
            try
            {
                foreach (int rule in Env("ACT1_RULES", "0,1,2,3").Split(',').Select(int.Parse))
                {
                    TurnRules.ExperimentMultiRule = rule;
                    foreach (var bot in new[] { Act1Sim.Human, Act1Sim.Expert })
                        foreach (int bonus in Env("ACT1_BONUS", "0,1").Split(',').Select(int.Parse))
                        {
                            if ((rule >= 1 && rule <= 3 && bonus == 0) || (rule == 4 && bonus > 0)) continue; // 案B〜D は4命令のときだけ・長期戦は3命令で
                            var agg = new Agg();
                            foreach (var room in rooms)
                                for (int s = 0; s < Seeds; s++)
                                    agg.B.Add(Act1Sim.Battle(MakeRun(s), room, bot, new Random(555 + s * 7 + room.Id.GetHashCode()), bonus, bot == Act1Sim.Human));
                            sb.AppendLine($"  案{rule} {bot.Name,-14} 上限 {3 + bonus}：{agg.Line()}・4命令以上を使ったターン {Act1Sim.Pct(agg.B.Sum(b => b.FourCommandTurns), agg.B.Sum(b => b.Turns))}");
                            Write(sb, path);
                        }
                }
            }
            finally { TurnRules.ExperimentMultiRule = 0; }
            Write(sb, path);
        }
    }
}
