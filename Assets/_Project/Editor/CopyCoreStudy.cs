using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究・Copy Core V2】コピーの方式（A 今／B 反転まとめて／C 反転すぐ／D あなたへすぐ）× COPY ON/OFF を、同じステージ・同じデッキで比べる。
    ///   run-method.ps1 CopyCommand.EditorTools.CopyCoreStudy.Run → Logs/copy_core_v2.txt
    ///   環境変数 CV2_STAGES（既定：CV1〜CV6 と ACT1 の通常戦 A1・A4）・CV2_SEEDS（既定 4）・CV2_MODES（既定 0,1,2,3）
    /// ボット：
    ///   読み手（Reader）＝コピーを全部読む＋次のターンも少し読む／人寄り（Human）＝敵ごとに 30% でコピーを読み落とす・500 本だけ考える／
    ///   受け身（Passive）＝攻撃の札を使わない（「あなたが何もしなくても敵が勝手に倒れるか」＝同士討ちが強すぎないかを見る）
    /// 指標（docs/research/COPY_CORE_V2.md）：
    ///   Copy Required＝COPY OFF で読み手が勝てない／Copy Kill＝倒した敵のうち、とどめがコピー起点（敵のコピー・鏡晶・写し砲）の割合／
    ///   Copy Interaction＝1ターンあたり「コピー起点で敵に起きたこと」（ダメージ・封印・鏡晶・扉）／Choice Change＝コピーがあるから最善手を変えたターンの割合／
    ///   OFF Difference＝ON と OFF の勝率・ターン数の差
    /// </summary>
    public static class CopyCoreStudy
    {
        /// <summary>CV2_TRACE=1 のとき、読み手（COPY ON）の戦闘を1手ずつ Logs/copy_core_trace.txt へ書く（ボットが解けない理由を見る）。</summary>
        internal static StringBuilder Trace;

        static string Env(string k, string d) => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)) ? d : Environment.GetEnvironmentVariable(k);

        public class Bot
        {
            public string Name; public float Penalty = 2.5f; public float MissRead; public int Width; public bool Lookahead; public bool Passive;
        }
        public static readonly Bot Reader = new Bot { Name = "読み手", Penalty = 3f, Lookahead = true };
        public static readonly Bot Human = new Bot { Name = "人寄り", Penalty = 2.5f, MissRead = 0.3f, Width = 500 };
        public static readonly Bot Passive = new Bot { Name = "受け身", Penalty = 2.5f, Passive = true };
        /// <summary>コピーを全く読まない（敵は全員コピーしない物として選ぶ）。「読むボットとの勝率の差」＝コピーを読む価値。</summary>
        public static readonly Bot Naive = new Bot { Name = "読まない", Penalty = 2.5f, MissRead = 1f };

        public class Out
        {
            public bool Win, Dead; public int Turns, Taken, Kills, CopyKills, CopyEvents, MeasuredTurns, ChoiceChanged, CopyDamageToYou, CopyDamageToEnemies;
            /// <summary>【V3】あなたの直接攻撃で敵に与えた量・敵が受けた全部／同じ手順を続けてコピーで削った最長ターン（無料ループの目安）／進行（扉・封印・鏡核・守り）。</summary>
            public int DirectDamage, EnemyDamage, LoopStreak, ProgressEvents, HpLeft;
            /// <summary>【Puzzle v2】まねで仕掛けが動いた回数（封印石・石像・鏡核・扉・箱…）／固定攻撃（兵種・石像・爆撃）であなたが受けた量。</summary>
            public int CopyGimmicks, FixedHits;
        }

        static readonly string[] DefaultStages = { "StageCV1", "StageCV2", "StageCV3", "StageCV4", "StageCV5", "StageCV6", "StageA1", "StageA4" };
        /// <summary>【Copy Core V3】コピー中心のミニ ACT1 の部屋（方式 A だけ）。</summary>
        static readonly string[] V3Stages = { "StageC0", "StageC1", "StageC2", "StageC3", "StageC4", "StageCD1", "StageCD2", "StageCB", "StageA1", "StageA2", "StageA3", "StageA4" };

        /// <summary>【Copy Core V3】run-method.ps1 CopyCommand.EditorTools.CopyCoreStudy.RunV3 → Logs/copy_core_v3.txt（CV2_STAGES・CV2_SEEDS・CV2_MAXTURNS も効く）。</summary>
        public static void RunV3()
        {
            System.Environment.SetEnvironmentVariable("CV2_MODES", "0");
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CV2_STAGES"))) System.Environment.SetEnvironmentVariable("CV2_STAGES", string.Join(",", V3Stages));
            OutPath = "Logs/copy_core_v3.txt";
            Run();
        }

        static string OutPath = "Logs/copy_core_v2.txt";

        [MenuItem("CopyCommand/解析：Copy Core V2（方式 × COPY ON/OFF）")]
        public static void Run()
        {
            var watch = Stopwatch.StartNew();
            string path = OutPath;
            int seeds = int.Parse(Env("CV2_SEEDS", "3"));
            var stages = Env("CV2_STAGES", string.Join(",", DefaultStages)).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            var modes = Env("CV2_MODES", "0,1,2,3,4").Split(',').Select(s => (CopyMode)int.Parse(s.Trim())).ToList();
            var sb = new StringBuilder($"【研究】Copy Core V2：方式 × COPY ON/OFF（各 {seeds} 戦・HP 20・ACT1 と同じ最初のデッキ・手札7枚補充・3命令・15 ターンまで／COPY OFF は 10 ターンまで）\n");
            sb.AppendLine("勝率・平均ターン・被ダメ／Copy Kill＝とどめがコピー起点の割合／作用＝コピー起点で敵に起きたこと（1ターンあたり）／選択変化＝コピーのせいで最善手を変えたターン／受け身＝攻撃札を使わずに勝てた割合\n");
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            foreach (var name in stages)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                if (stage == null) { sb.AppendLine($"=== {name}：見つからない\n"); continue; }
                sb.AppendLine($"=== {stage.stageTitle}（{name}）");
                foreach (var mode in modes)
                {
                    if (Env("CV2_TRACE", "0") == "1") Trace = new StringBuilder($"=== {name} {CopyCore.Name(mode)}{NL}");
                    var on = Play(stage, mode, true, Reader, seeds);
                    if (Trace != null) { File.AppendAllText("Logs/copy_core_trace.txt", Trace.ToString(), new UTF8Encoding(false)); Trace = null; }
                    var off = Play(stage, mode, false, Reader, seeds);
                    var human = Play(stage, mode, true, Human, seeds);
                    var passive = Play(stage, mode, true, Passive, seeds);
                    var naive = Play(stage, mode, true, Naive, seeds);
                    sb.AppendLine($"  {CopyCore.Name(mode),-12} 読み手 ON {Line(on)}");
                    sb.AppendLine($"  {"",-12} 読み手 OFF {Line(off)}");
                    sb.AppendLine($"  {"",-12} 人寄り ON {Line(human)}");
                    sb.AppendLine($"  {"",-12} 読まない ON {Line(naive)}");
                    sb.AppendLine($"  {"",-12} 受け身 ON 勝ち {Pct(passive.Count(o => o.Win), passive.Count)}　　→ ON/OFF の差：勝率 {Pct(on.Count(o => o.Win), on.Count)} 対 {Pct(off.Count(o => o.Win), off.Count)}" +
                                  $"・ターン {Avg(on.Where(o => o.Win), o => o.Turns)} 対 {Avg(off.Where(o => o.Win), o => o.Turns)}");
                    Write(sb, path);
                    Debug.Log($"[CopyCoreStudy] {name} {CopyCore.Letter(mode)} {watch.Elapsed.TotalMinutes:0.0}分");
                }
                sb.AppendLine();
                Write(sb, path);
            }
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }

        /// <summary>【確認用】決めた手順を1つのステージ・方式で再生して、ターンごとの盤面を書き出す（ボットが解けないときに「解けるか」を確かめる）。
        /// 環境変数 CV2_PROBE_STAGE（既定 StageCV6）・CV2_PROBE_MODE（0〜4、既定 2＝C）・CV2_PROBE（ターンを | で区切り、命令は「札:向き」をカンマで並べる。
        /// 向きは U/D/L/R。例 Dash:R,Dash:R,Slash:D|Slash:D,Slash:D,Slash:D）→ Logs/copy_core_probe.txt</summary>
        public static void RunProbe()
        {
            var stageName = Env("CV2_PROBE_STAGE", "StageCV6");
            var mode = (CopyMode)int.Parse(Env("CV2_PROBE_MODE", "2"));
            var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{stageName}.asset");
            var m = BattleModel.FromStage(stage);
            m.CopyMode = mode;
            var sb = new StringBuilder($"{stage.stageTitle}・{CopyCore.Name(mode)}{NL}{State(m)}{NL}");
            int t = 0;
            foreach (var turn in Env("CV2_PROBE", "").Split('|'))
            {
                t++;
                var seq = new List<CommandRecord>();
                foreach (var tok in turn.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                {
                    var parts = tok.Split(':');
                    var c = AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{parts[0]}.asset");
                    var d = parts.Length < 2 ? Direction.Up : parts[1] == "U" ? Direction.Up : parts[1] == "D" ? Direction.Down : parts[1] == "L" ? Direction.Left : Direction.Right;
                    seq.Add(new CommandRecord(c, d, parts.Length > 2 ? int.Parse(parts[2]) : c.maxDistance));
                }
                var log = new List<string>();
                var o = TurnRules.PlayTurn(m, seq, null, null, log);
                sb.Append($"--- ターン {t}：{turn}{NL}");
                foreach (var l in log) if (!string.IsNullOrEmpty(l)) sb.Append($"    {l}{NL}");
                sb.Append($"{State(m)}{(o != TurnOutcome.Continue ? $"  → {o}" : "")}{NL}");
                if (o != TurnOutcome.Continue) break;
            }
            Write(sb, "Logs/copy_core_probe.txt");
        }

        const string NL = "\n";

        static string State(BattleModel m) => $"  あなた {m.Player.Position} HP{m.Player.Hp}　" +
            string.Join("　", m.Enemies.Select(e => $"{e.Name}{e.Position} HP{e.Hp}{(e.Barrier > 0 ? $" 封印{e.Barrier}(規則{e.BarrierRule})" : "")}"));

        static string Line(List<Out> list)
        {
            int n = list.Count;
            int kills = list.Sum(o => o.Kills), ck = list.Sum(o => o.CopyKills), turns = list.Sum(o => o.Turns), measured = list.Sum(o => o.MeasuredTurns);
            int dd = list.Sum(o => o.DirectDamage), ed = list.Sum(o => o.EnemyDamage);
            return $"勝ち {Pct(list.Count(o => o.Win), n)}・{Avg(list, o => o.Turns)}T・被ダメ {Avg(list, o => o.Taken)}（税：コピーから {Avg(list, o => o.CopyDamageToYou)}／武器：コピーで敵に {Avg(list, o => o.CopyDamageToEnemies)}）・Copy Kill {Pct(ck, kills)}" +
                   $"・直接依存 {Pct(dd, ed)}・ループ最長 {(n == 0 ? 0 : list.Max(o => o.LoopStreak))}・進行 {Avg(list, o => o.ProgressEvents)}" +
                   $"・作用 {(turns == 0 ? 0 : list.Sum(o => o.CopyEvents) / (double)turns):0.0}/T・選択変化 {Pct(list.Sum(o => o.ChoiceChanged), measured)}";
        }

        internal static string Pct(int a, int b) => b == 0 ? "—" : $"{100.0 * a / b:0}%";
        static string Avg<T>(IEnumerable<T> xs, Func<T, int> f) { var l = xs.ToList(); return l.Count == 0 ? "—" : l.Average(f).ToString("0.0"); }

        static void Write(StringBuilder sb, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static List<Out> Play(StageData stage, CopyMode mode, bool copyOn, Bot bot, int seeds)
        {
            var list = new List<Out>();
            for (int s = 1; s <= seeds; s++) list.Add(Battle(stage, mode, copyOn, bot, new Random(s * 7919 + (int)mode * 31 + (copyOn ? 0 : 5))));
            return list;
        }

        /// <summary>そのモデルの「進み具合」の点（与ダメ・封印・扉・鏡晶・踏み板への近さ）。ボットが目的を知っている前提（人も盤面で見える）。</summary>
        static float Progress(BattleModel m)
        {
            float p = 0;
            foreach (var e in m.Enemies)
            {
                if (e.IsObject) continue;
                p += (e.MaxHp - Mathf.Max(0, e.Hp)) * 1f;
                if (e.Kind == EnemyKind.SealGuard) p += (2 - e.Barrier) * 4f;
                if (e.Kind == EnemyKind.Warden && e.IsAlive)
                {
                    p += PhaseRules.WardenPhase(e) * 15f; // 段階が進むこと自体を評価（境目で封印が戻っても損に見えないように）
                    if (e.Barrier == 0) { p += 6f; if (m.Player.IsAlive) p -= Dist(m.Player.Position, e.Position) * 0.6f; } // 割れた窓：番人は写せないので、近づいて殴る
                    else if (e.BarrierRule == 2 && m.Board.Seals.Count > 0) p -= m.Board.Seals.Min(c => Dist(c, e.Position)) * 0.6f;
                    else if (e.BarrierRule == 1 && m.Board.Crystals.Any()) p -= m.Board.Crystals.Min(c => Dist(c, e.Position)) * 0.4f;
                    else if (e.BarrierRule == 0) p += (PhaseRules.WardenBarrier(0) - e.Barrier) * 3f;
                }
            }
            var b = m.Board;
            // 【V3】対の鏡核・鏡核の守り・写し身の主
            p += (b.CoreHome.Count - b.Cores.Count()) * 6f;
            foreach (var e in m.Enemies)
            {
                if (!e.IsAlive) continue;
                if (e.BarrierRule == 3 && e.Kind != EnemyKind.Lord && e.Barrier == 0) p += 4f;
                if (e.Kind != EnemyKind.Lord) continue;
                int ph = PhaseRules.LordPhase(e);
                p += ph * 15f;
                if (ph == 1)
                {
                    var clones = m.Enemies.Where(c => c.Kind == EnemyKind.HealClone && c.IsAlive && !c.Dormant).ToList();
                    p += (m.Enemies.Count(c => c.Kind == EnemyKind.HealClone) - clones.Count) * 8f; // 砕いた写し身
                    if (clones.Count > 0) p -= clones.Min(c => Dist(c.Position, e.Position)) * 0.5f;
                }
                else if (e.Barrier == 0) { p += 6f; if (m.Player.IsAlive) p -= Dist(m.Player.Position, e.Position) * 0.6f; }
                else if (ph == 2 && b.Seals.Count > 0) p -= b.Seals.Min(c => Dist(c, e.Position)) * 0.6f;
            }
            // 【Puzzle v2】封印の主：段階が進むこと・段階の目的に近づくこと
            foreach (var e in m.Enemies)
            {
                if (!e.IsAlive || e.Kind != EnemyKind.SealLord) continue;
                p += e.BossStage * 20f;
                if (e.Barrier == 0) { p += 6f; if (m.Player.IsAlive) p -= Dist(m.Player.Position, e.Position) * 0.6f; }
                else if (e.BossStage == 0 && b.Seals.Any(b.IsLitSeal)) p -= b.Seals.Where(b.IsLitSeal).Min(c => Dist(c, e.Position)) * 1.5f;
                else if (e.BossStage == 1)
                {
                    var st = m.Enemies.Where(c => c.Kind == EnemyKind.BladeStatue && !c.Dormant).ToList();
                    if (st.Count > 0) p -= st.Min(c => Dist(c.Position, e.Position)) * 0.8f;
                }
                else if (e.BossStage == 2) p += b.CoreHitCount * 2f;
            }
            // 【Puzzle v2】円の守り（circle=1）の兵：守りが割れたら良い・割れていなければ円に近いほど良い（PD3 の作り直しでボットが位置取りを見つけられなかった）
            if (b.Seals.Count > 0 && !m.Enemies.Any(e => e.Kind == EnemyKind.SealLord))
                foreach (var e in m.Enemies)
                {
                    if (!e.IsAlive || e.BarrierRule != 2 || e.Kind == EnemyKind.Warden) continue;
                    if (e.Barrier == 0) p += 6f;
                    else p -= b.Seals.Min(c => Dist(c, e.Position)) * 0.8f;
                }
            // 【Puzzle v2】鏡核の同期：割れるまで進み具合が平らで、1手先しか読まないボットは位置取りを見つけられなかった（P5 の作り直しで 0%）。
            // 「あなたが片方の核の空いた面の隣・まねて攻撃できる敵がもう片方の核の空いた面の隣」に近づくほど良い（人も盤面で見える）
            var coresLeft = b.Cores.ToList();
            if (coresLeft.Count >= 2 && m.Player.IsAlive)
            {
                var hitters = m.Enemies.Where(e => e.IsAlive && !e.Dormant && (e.CopyKind == CopyKind.All || e.CopyKind == CopyKind.Technique)).ToList();
                if (hitters.Count > 0)
                {
                    int Face(Vector2Int from, Vector2Int core) => new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right }
                        .Select(d => core + d).Where(c => b.IsInside(c) && !b.IsWall(c)).Select(c => Dist(c, from)).DefaultIfEmpty(99).Min();
                    float best = float.MaxValue;
                    foreach (var c1 in coresLeft)
                        foreach (var c2 in coresLeft)
                        {
                            if (c1 == c2) continue;
                            float d = Face(m.Player.Position, c1) + hitters.Min(e => Face(e.Position, c2));
                            if (d < best) best = d;
                        }
                    if (best < 99) p -= best * 0.7f;
                }
            }
            // 【Puzzle v2】封印石・出口・運び紋（ボットは目的を知っている前提：画面に常時出ている）
            p += (b.SealStoneHome.Count - b.SealStones.Count()) * 8f;
            if (b.CarryMarks.Count > 0)
            {
                var movers = m.Enemies.Where(e => e.IsAlive && e.CopyKind != CopyKind.Technique && e.CopyKind != CopyKind.None && !e.IsImmovable).ToList();
                foreach (var mk in b.CarryMarks)
                {
                    var u = m.GetUnitAt(mk);
                    if (u != null && !u.IsPlayer) p += 8f;
                    else if (movers.Count > 0) p -= movers.Min(e => Dist(e.Position, mk)) * 1.2f;
                }
            }
            if (b.Exits.Count > 0 && m.Player.IsAlive && PuzzleRules.ExitOpen(b) && (b.Gates.Count == 0 || b.GatesOpen)) p += 14f - b.Exits.Min(x => Dist(x, m.Player.Position)) * 1.5f;
            if (b.Gates.Count > 0)
            {
                if (b.GatesOpen) p += 14f;
                else if (m.Player.IsAlive)
                {
                    if (b.PlayerPlates.Count > 0) p -= b.PlayerPlates.Min(c => Dist(c, m.Player.Position)) * 0.5f;
                    var walkers = m.Enemies.Where(e => e.IsAlive && (!e.IsObject || e.Kind == EnemyKind.Golem)).ToList(); // 【Puzzle v2】運び像も板へ運ぶ
                    if (walkers.Count > 0 && b.EnemyPlates.Count > 0) p -= b.EnemyPlates.Min(q => walkers.Min(e => Dist(q, e.Position))) * 0.5f;
                }
            }
            return p;
        }

        static int Dist(Vector2Int a, Vector2Int b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        static float Eval(BattleModel m, List<CommandRecord> seq, Bot bot, ICollection<int> noCopy, bool copyOff, out int taken)
        {
            var sim = m.Clone();
            if (copyOff) sim.CopyEnabled = false;
            int hp = sim.Player.Hp;
            float before = Progress(sim);
            var o = TurnRules.PlayTurn(sim, seq, null, null, null, noCopy);
            taken = hp - Mathf.Max(0, sim.Player.Hp);
            return Progress(sim) - before - taken * bot.Penalty + (o == TurnOutcome.Cleared ? 500 : 0) - (o == TurnOutcome.GameOver ? 1000 : 0);
        }

        internal static Out Battle(StageData stage, CopyMode mode, bool copyOn, Bot bot, Random rng, int startHp = 0)
        {
            var m = BattleModel.FromStage(stage);
            m.CopyMode = mode;
            m.CopyEnabled = copyOn;
            if (startHp > 0) m.Player.Hp = Mathf.Min(startHp, m.Player.MaxHp);
            var deckCards = stage.handTestDeckLarge != null && stage.handTestDeckLarge.Count > 0 ? stage.handTestDeckLarge : stage.GetHandTestDeck();
            var draw = deckCards.OrderBy(_ => rng.Next()).ToList();
            var discard = new List<CommandData>();
            var hand = new List<CommandData>();
            void Refill()
            {
                while (hand.Count < 7)
                {
                    if (draw.Count == 0) { if (discard.Count == 0) return; draw = discard.OrderBy(_ => rng.Next()).ToList(); discard.Clear(); }
                    hand.Add(draw[0]); draw.RemoveAt(0);
                }
            }
            Refill();
            var o = new Out();
            string lastSeqKey = null;
            int streak = 0;
            int hp0 = m.Player.Hp;
            var events = new List<BattleEvent>();
            var outcome = TurnOutcome.Continue;
            int t = 0;
            int maxTurns = copyOn ? int.Parse(Env("CV2_MAXTURNS", "15")) : 10; // COPY OFF は 10 ターンで勝てなければ「勝てない」（解析の時間を減らす）
            while (outcome == TurnOutcome.Continue && t < maxTurns)
            {
                t++;
                var usable = bot.Passive ? hand.Where(c => c.damage <= 0 && !IsAttack(c.type)).ToList() : hand;
                var seqs = Act1Sim.Sequences(usable, 3, rng, 1200);
                if (seqs.Count == 0) seqs.Add(new List<CommandRecord>());
                if (bot.Width > 0 && seqs.Count > bot.Width) seqs = seqs.OrderBy(_ => rng.Next()).Take(bot.Width).ToList();
                var alive = m.Enemies.Where(e => e.IsAlive).Select(e => e.Id).ToList();
                var miss = new HashSet<int>(alive.Where(_ => rng.NextDouble() < bot.MissRead));
                var real = new float[seqs.Count];
                var perceived = new float[seqs.Count];
                Parallel.For(0, seqs.Count, i =>
                {
                    real[i] = Eval(m, seqs[i], bot, null, false, out _);
                    perceived[i] = miss.Count == 0 ? real[i] : Eval(m, seqs[i], bot, miss, false, out _);
                });
                int pick = ArgMax(perceived);
                if (bot.Lookahead && seqs.Count > 1)
                {
                    var top = Enumerable.Range(0, seqs.Count).OrderByDescending(i => real[i]).Take(4).ToList();
                    float best = float.MinValue;
                    foreach (int i in top)
                    {
                        var sim = m.Clone();
                        var oc = TurnRules.PlayTurn(sim, seqs[i], null);
                        float total = real[i];
                        if (oc == TurnOutcome.Continue)
                        {
                            var left = new List<CommandData>(hand);
                            foreach (var r in seqs[i]) { var c = left.FirstOrDefault(x => x == r.Command); if (c != null) left.Remove(c); }
                            foreach (var c in draw) { if (left.Count >= 7) break; left.Add(c); }
                            var next = Act1Sim.Sequences(left, 3, rng, 300);
                            if (next.Count > 0)
                            {
                                var nv = new float[next.Count];
                                Parallel.For(0, next.Count, j => nv[j] = Eval(sim, next[j], bot, null, false, out _));
                                total += 0.8f * nv.Max();
                            }
                        }
                        if (total > best) { best = total; pick = i; }
                    }
                }
                // 選択変化：コピーを無視して選んだ最善手は、本当のルールでは最善より明らかに悪いか（COPY ON のときだけ）
                if (copyOn && seqs.Count > 1)
                {
                    var naive = new float[seqs.Count];
                    Parallel.For(0, seqs.Count, i => naive[i] = Eval(m, seqs[i], bot, null, true, out _));
                    int nb = ArgMax(naive), rb = ArgMax(real);
                    o.MeasuredTurns++;
                    if (real[rb] > real[nb] + 0.5f) o.ChoiceChanged++;
                }
                var seq = seqs[pick];
                string seqKey = string.Join(",", seq.Select(r => $"{r.Command.name}:{r.Direction}"));
                if (Trace != null) Trace.Append($"T{t} {string.Join(",", seq.Select(r => $"{r.Command.type}:{r.Direction.ToString()[0]}{(r.Distance > 1 ? r.Distance.ToString() : "")}"))}　手札 {string.Join("/", hand.Select(c => c.type))}{NL}");
                m.BeginRound();
                int evStart = events.Count;
                outcome = TurnRules.PlayTurn(m, seq, null, events);
                o.CopyGimmicks += CountCopyGimmicks(m, seq, events, evStart);
                // 【V3】同じ手順を続けて、コピーで敵を削ったか（無料ループの目安）
                bool copyHit = events.Skip(evStart).Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && CopyCore.IsCopyOrigin(e.Cause));
                if (copyHit && seqKey == lastSeqKey) streak++; else streak = copyHit ? 1 : 0;
                lastSeqKey = seqKey;
                o.LoopStreak = Math.Max(o.LoopStreak, streak);
                foreach (var r in seq)
                {
                    var c = hand.FirstOrDefault(x => x == r.Command);
                    if (c == null) continue;
                    hand.Remove(c);
                    discard.Add(c);
                }
                Refill();
                if (Trace != null) Trace.Append($"{State(m)}{NL}");
            }
            if (Trace != null) Trace.Append($"→ {outcome}（{t}T）{NL}{NL}");
            // 指標
            var lastCause = new Dictionary<UnitModel, DamageCause>();
            foreach (var e in events)
            {
                if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Cause != null)
                {
                    lastCause[e.Unit] = e.Cause;
                    if (!e.Unit.IsPlayer && e.Amount > 0 && CopyCore.IsCopyOrigin(e.Cause)) { o.CopyEvents++; o.CopyDamageToEnemies += e.Amount; }
                    if (!e.Unit.IsPlayer && e.Amount > 0) { o.EnemyDamage += e.Amount; if (CopyCore.IsPlayerDirect(e.Cause.Source, e.Cause.ByCopy)) o.DirectDamage += e.Amount; }
                    if (e.Unit.IsPlayer && e.Cause.ByCopy) o.CopyDamageToYou += e.Amount;
                    if (e.Unit.IsPlayer) o.Taken += e.Amount;
                }
                if (e.Type == BattleEventType.BarrierHit || e.Type == BattleEventType.CrystalBurst) o.CopyEvents++;
                if (e.Type == BattleEventType.GateOpened) o.CopyEvents++;
                if (e.Type == BattleEventType.GateOpened || e.Type == BattleEventType.CoresShattered || e.Type == BattleEventType.BarrierBroken || e.Type == BattleEventType.BarrierHit ||
                    e.Type == BattleEventType.SealStoneBroken || e.Type == BattleEventType.CarryDone || e.Type == BattleEventType.ExitOpened) o.ProgressEvents++;
                if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null && (e.Cause.Kind == DamageKind.Trait || e.Cause.Kind == DamageKind.Gaze)) o.FixedHits += e.Amount;
                if (e.Type == BattleEventType.Died && e.Unit != null && !e.Unit.IsPlayer && !e.Unit.IsObject)
                {
                    o.Kills++;
                    if (lastCause.TryGetValue(e.Unit, out var c) && CopyCore.IsCopyOrigin(c)) o.CopyKills++;
                }
            }
            o.Win = outcome == TurnOutcome.Cleared;
            o.Dead = outcome == TurnOutcome.GameOver;
            o.Turns = t;
            o.HpLeft = Mathf.Max(0, m.Player.Hp);
            return o;
        }

        /// <summary>
        /// 【Copy Core V3】コピー中心のミニ ACT1 を通しで（報酬・商人は無し＝最初のデッキのまま。HP は持ち越し・休息 +8）。
        /// 道：写しの間 → 砲台の回廊か対の鏡核 → 運び手の間か残り → 休息 → 反響の砦か鏡晶の庭 → 写し身の主。
        /// run-method.ps1 CopyCommand.EditorTools.CopyCoreStudy.RunMiniAct → Logs/copy_core_miniact.txt（CV2_RUNS＝ラン数・既定 8、CV2_OFF=1 で COPY OFF）
        /// </summary>
        public static void RunMiniAct()
        {
            int runs = int.Parse(Env("CV2_RUNS", "8"));
            bool off = Env("CV2_OFF", "0") == "1";
            string path = off ? "Logs/copy_core_miniact_off.txt" : "Logs/copy_core_miniact.txt";
            var sb = new StringBuilder($"【研究】コピー中心のミニ ACT1 を通しで（{runs} ラン・報酬なし＝最初のデッキのまま・HP 持ち越し・休息 +8・ボスは 20 ターンまで{(off ? "・COPY OFF" : "")}）\n\n");
            StageData S(string n) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{n}.asset");
            var watch = Stopwatch.StartNew();
            foreach (var bot in new[] { Reader, Human, Naive })
            {
                int clears = 0, hpAtBossSum = 0, reachedBoss = 0, turnsSum = 0;
                var deaths = new Dictionary<string, int>();
                for (int r = 1; r <= runs; r++)
                {
                    var rng = new Random(9100 + r);
                    var path2 = new List<string> { "StageC1", rng.Next(2) == 0 ? "StageC2" : "StageC3", rng.Next(2) == 0 ? "StageC4" : "StageC2", "REST", rng.Next(2) == 0 ? "StageCD1" : "StageCD2", "StageCB" };
                    if (path2[2] == path2[1]) path2[2] = "StageC4";
                    int hp = 20, turns = 0;
                    bool dead = false;
                    foreach (var n in path2)
                    {
                        if (n == "REST") { hp = Math.Min(20, hp + 8); continue; }
                        if (n == "StageCB") { reachedBoss++; hpAtBossSum += hp; }
                        string prev = Environment.GetEnvironmentVariable("CV2_MAXTURNS");
                        if (n == "StageCB") Environment.SetEnvironmentVariable("CV2_MAXTURNS", "20");
                        var o = Battle(S(n), CopyMode.Current, !off, bot, new Random(r * 131 + n.GetHashCode()), hp);
                        Environment.SetEnvironmentVariable("CV2_MAXTURNS", prev);
                        turns += o.Turns;
                        if (!o.Win) { dead = true; deaths[n] = deaths.TryGetValue(n, out var d) ? d + 1 : 1; break; }
                        hp = o.HpLeft;
                    }
                    if (!dead) clears++;
                    turnsSum += turns;
                }
                sb.AppendLine($"{bot.Name}：クリア {Pct(clears, runs)}・ボス到達 {Pct(reachedBoss, runs)}（到達時 HP 平均 {(reachedBoss == 0 ? 0 : hpAtBossSum / (double)reachedBoss):0.0}）・合計ターン平均 {turnsSum / (double)runs:0.0}" +
                              $"・止まった部屋 {(deaths.Count == 0 ? "なし" : string.Join("／", deaths.Select(kv => $"{kv.Key} {kv.Value}")))}");
                Write(sb, path);
                Debug.Log($"[CopyCoreStudy] miniact {bot.Name} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }

        /// <summary>このラウンドに「まねで」仕掛けが動いた数（あなたの命令の後の出来事のうち、仕掛けの物。あなたの直接の分は数えない）。</summary>
        static int CountCopyGimmicks(BattleModel m, List<CommandRecord> seq, List<BattleEvent> events, int start)
        {
            int n = 0;
            for (int i = start; i < events.Count; i++)
            {
                var e = events[i];
                if (!PuzzleRules.IsGimmickEvent(e) || e.Cause == null || !e.Cause.ByCopy) continue;
                n++;
            }
            return n;
        }

        static bool IsAttack(CommandType t) => t == CommandType.Slash || t == CommandType.PushSlash || t == CommandType.Pierce || t == CommandType.Sweep || t == CommandType.AroundSlash;

        static int ArgMax(float[] v)
        {
            int bi = 0;
            for (int i = 1; i < v.Length; i++) if (v[i] > v[bi]) bi = i;
            return bi;
        }
    }
}
