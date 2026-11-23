using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using static CopyCommand.EditorTools.LargeHandStudy;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究】ACT1 の大量シミュレーション（2026-10-04 深夜・10-05 にボット4種とコピー圧力を追加）。
    ///   RunRoles   ：6役職 × N ラン（環境変数 ACT1_SIM_RUNS、既定 30）→ Logs/act1_sim_roles.txt
    ///   RunCards   ：カード1種を2枚足したデッキで各部屋を戦う（死に札・強すぎる札）→ Logs/act1_sim_cards.txt
    ///   RunMulti   ：命令の上限 3〜6 で各部屋を戦う（多命令の危険）→ Logs/act1_sim_multi.txt
    ///   RunImprints：刻印を最初から1つ持たせたラン → Logs/act1_sim_imprints.txt
    /// ボット（Bot）：1ターン先読み。命令列は全部（多すぎるときは無作為に 2500 本）を並列に試算する。
    ///   Penalty＝被弾1あたりの重さ、MissRead＝そのターンに「その敵のコピーを読み落とす」確率（読み落とした敵はコピーしない物として見積もる）、
    ///   Lookahead＝次のターンの手札まで見て、追い詰められる手を避ける（Expert）。
    /// 部屋の盤面は tools/act1_rooms.txt の候補で差し替えられる（Act1Tension）。ゲームの挙動は変えない（ActRun・TurnRules をそのまま使う）。
    /// </summary>
    public static class Act1Sim
    {
        const int SampleCap = 2500;
        static Dictionary<string, CommandData> cardCache;

        internal static CommandData C(string name)
        {
            if (cardCache == null) cardCache = new Dictionary<string, CommandData>();
            if (!cardCache.TryGetValue(name, out var c)) cardCache[name] = c = Card(name);
            return c;
        }

        static int Runs => int.TryParse(Environment.GetEnvironmentVariable("ACT1_SIM_RUNS"), out var n) && n > 0 ? n : 30;

        // ───────────── ボット ─────────────

        public class Bot
        {
            public string Name;
            public float Penalty = 2.5f;
            public float MissRead;
            public bool Lookahead;
            /// <summary>1ターンに見比べる命令列の数（0＝全部）。人は全部の組み合わせを読み切れないので、無作為にこの数だけ考える。</summary>
            public int Width;
            /// <summary>与ダメの重さ（1＝ふつう）。速さの報酬を狙う人は大きい＝早く倒すために被弾を受け入れやすい。</summary>
            public float DealWeight = 1f;
            public float Score(int dealt, int taken, bool win, bool dead) => dealt * DealWeight - taken * Penalty + (win ? 100 : 0) - (dead ? 1000 : 0);
        }

        /// <summary>前回までの「慎重」：全部読み切る・被弾×2.5。</summary>
        public static readonly Bot Careful = new Bot { Name = "慎重（旧）", Penalty = 2.5f };
        /// <summary>深く読む：被弾はほぼ絶対に避ける・コピーを読み落とさない・次のターンまで見る（ノーダメージ解があるかの確認用）。</summary>
        public static readonly Bot Expert = new Bot { Name = "Expert", Penalty = 40f, Lookahead = true };
        /// <summary>人に近い慎重：被弾×2.5。考える手は 500 通りまで、各ターン各敵のコピーを 30% で読み落とす。</summary>
        public static readonly Bot Human = new Bot { Name = "Human-like慎重", Penalty = 2.5f, MissRead = 0.3f, Width = 500 };
        public static readonly Bot Medium = new Bot { Name = "Medium", Penalty = 1.0f, MissRead = 0.5f, Width = 300 };
        public static readonly Bot Reckless = new Bot { Name = "Reckless", Penalty = 0.15f, MissRead = 0.7f, Width = 200 };
        /// <summary>速さの報酬を狙う慎重な人（与ダメを 1.6 倍に重く見る）。</summary>
        public static readonly Bot HumanFast = new Bot { Name = "Human-like速さ狙い", Penalty = 2.5f, MissRead = 0.3f, Width = 500, DealWeight = 1.6f };
        public static readonly Bot[] FourBots = { Expert, Human, Medium, Reckless, HumanFast };

        // ───────────── 盤面の差し替え（tools/act1_rooms.txt） ─────────────

        /// <summary>部屋 Id → 盤面の候補（解析中だけ）。null なら RoomDef.Stage のアセット。</summary>
        public static Dictionary<string, RoomBoard> RoomOverrides = new Dictionary<string, RoomBoard>();

        public class RoomBoard
        {
            public string Id, Room, Base, Facings, Note;
            public string[] Map, Fire;
            public int EnemyHp, Spike = -1, PlayerStartHp = -1, RuneUses = -1, RuneHeal = -1;
        }

        /// <summary>候補ファイルを読む。[id] の下に key=value（map・fire は | で行、fire の模様は ; で区切る）。</summary>
        public static Dictionary<string, RoomBoard> LoadBoards(string path = "tools/act1_rooms.txt")
        {
            var all = new Dictionary<string, RoomBoard>();
            if (!File.Exists(path)) return all;
            RoomBoard cur = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[")) { cur = new RoomBoard { Id = line.Trim('[', ']') }; all[cur.Id] = cur; continue; }
                int eq = line.IndexOf('=');
                if (cur == null || eq < 0) continue;
                string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "room": cur.Room = v; break;
                    case "base": cur.Base = v; break;
                    case "map": cur.Map = v.Split('|'); break;
                    case "facings": cur.Facings = v; break;
                    case "fire": cur.Fire = v.Split(';').Select(x => x.Trim()).ToArray(); break;
                    case "enemyHp": cur.EnemyHp = int.Parse(v); break;
                    case "spike": cur.Spike = int.Parse(v); break;
                    case "playerStartHp": cur.PlayerStartHp = int.Parse(v); break;
                    case "runeUses": cur.RuneUses = int.Parse(v); break;
                    case "runeHeal": cur.RuneHeal = int.Parse(v); break;
                    case "note": cur.Note = v; break;
                }
            }
            return all;
        }

        /// <summary>部屋のステージ（複製）。差し替えがあればそれを当てる。呼んだ側で DestroyImmediate する。</summary>
        public static StageData StageFor(RoomDef room, out int enemyHp)
        {
            enemyHp = room.EnemyHp;
            RoomOverrides.TryGetValue(room.Id, out var b);
            string asset = b?.Base ?? room.Stage;
            var src = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{asset}.asset");
            var stage = UnityEngine.Object.Instantiate(src);
            if (b != null)
            {
                if (b.Map != null) stage.map = string.Join("\n", b.Map);
                if (b.Facings != null) stage.enemyFacings = b.Facings;
                if (b.Fire != null) stage.firePatterns = b.Fire.Where(x => x.Length > 0).ToList();
                if (b.EnemyHp > 0) enemyHp = b.EnemyHp;
                if (b.Spike >= 0) stage.spikeDamage = b.Spike;
                if (b.PlayerStartHp >= 0) stage.playerStartHp = b.PlayerStartHp;
                if (b.RuneUses >= 0) stage.runeUses = b.RuneUses;
                if (b.RuneHeal >= 0) stage.runeHeal = b.RuneHeal;
            }
            return stage;
        }

        // ───────────── 命令列を作る（同じ札は手札にある枚数まで・号令は上限を増やす） ─────────────

        internal static List<List<CommandRecord>> Sequences(List<CommandData> hand, int maxSlots, Random rng, int cap = SampleCap)
        {
            var counts = new Dictionary<CommandData, int>();
            foreach (var c in hand) counts[c] = counts.TryGetValue(c, out var n) ? n + 1 : 1;
            var cards = counts.Keys.ToList();
            List<CommandRecord> Options(CommandData c)
            {
                var list = new List<CommandRecord>();
                if (!c.UsesDirection) { list.Add(new CommandRecord(c, Direction.Up, 0)); return list; }
                foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                    for (int dist = c.minDistance; dist <= c.maxDistance; dist++) list.Add(new CommandRecord(c, d, dist));
                return list;
            }
            var opts = cards.ToDictionary(c => c, Options);
            var result = new List<List<CommandRecord>>();
            bool overflow = false;
            void Rec(List<CommandRecord> cur, int limit)
            {
                if (overflow) return;
                if (cur.Count > 0) { result.Add(new List<CommandRecord>(cur)); if (result.Count > cap * 3) { overflow = true; return; } }
                if (cur.Count >= limit) return;
                foreach (var c in cards)
                {
                    if (counts[c] == 0) continue;
                    counts[c]--;
                    int nextLimit = c.type == CommandType.Rally ? limit + Mathf.Max(1, c.range) : limit;
                    foreach (var o in opts[c]) { cur.Add(o); Rec(cur, nextLimit); cur.RemoveAt(cur.Count - 1); if (overflow) break; }
                    counts[c]++;
                    if (overflow) return;
                }
            }
            Rec(new List<CommandRecord>(), maxSlots);
            if (!overflow) return result;
            // 多すぎる：無作為に cap 本（長い命令列を多めに）
            result.Clear();
            var seen = new HashSet<string>();
            for (int tries = 0; result.Count < cap && tries < cap * 4; tries++)
            {
                var left = new Dictionary<CommandData, int>(counts.ToDictionary(k => k.Key, k => k.Value));
                var seq = new List<CommandRecord>();
                int limit = maxSlots;
                int want = rng.Next(100) < 75 ? limit : 1 + rng.Next(limit);
                while (seq.Count < want)
                {
                    var avail = left.Where(k => k.Value > 0).Select(k => k.Key).ToList();
                    if (avail.Count == 0) break;
                    var c = avail[rng.Next(avail.Count)];
                    left[c]--;
                    var o = opts[c];
                    seq.Add(o[rng.Next(o.Count)]);
                    if (c.type == CommandType.Rally) { limit += Mathf.Max(1, c.range); want = Math.Min(limit, want + Mathf.Max(1, c.range)); }
                }
                string key = string.Join("|", seq.Select(r => cards.IndexOf(r.Command) + ":" + (int)r.Direction + ":" + r.Distance));
                if (seen.Add(key)) result.Add(seq);
            }
            return result;
        }

        // ───────────── 試算 ─────────────

        public struct Ev { public float Score; public int Taken, Dealt; }

        /// <summary>命令列を全部並列に試算（noCopy の敵はコピーしない世界で）。</summary>
        internal static Ev[] EvalAll(BattleModel m, List<List<CommandRecord>> seqs, int attackBonus, Bot bot, ICollection<int> noCopy)
        {
            var res = new Ev[seqs.Count];
            Parallel.For(0, seqs.Count, i =>
            {
                var sim = m.Clone();
                if (attackBonus > 0) sim.Player.AttackBonus += attackBonus;
                int php = sim.Player.Hp, ehp = StagnationRule.EnemyHpTotal(sim);
                var o = TurnRules.PlayTurn(sim, seqs[i], null, null, null, noCopy);
                int taken = php - sim.Player.Hp, dealt = ehp - StagnationRule.EnemyHpTotal(sim);
                res[i] = new Ev { Score = bot.Score(dealt, taken, o == TurnOutcome.Cleared, o == TurnOutcome.GameOver), Taken = taken, Dealt = dealt };
            });
            return res;
        }

        static int ArgMax(Ev[] ev)
        {
            int bi = 0;
            for (int i = 1; i < ev.Length; i++) if (ev[i].Score > ev[bi].Score) bi = i;
            return bi;
        }

        /// <summary>命令列を並列に試算して、score が最大の物（アイテムの判断用：コピーは全部読む）。</summary>
        static (List<CommandRecord> seq, float score, int taken) Best(BattleModel m, List<List<CommandRecord>> seqs, int attackBonus, Bot bot)
        {
            if (seqs.Count == 0) return (new List<CommandRecord>(), float.MinValue, 0);
            var ev = EvalAll(m, seqs, attackBonus, bot, null);
            int bi = ArgMax(ev);
            return (seqs[bi], ev[bi].Score, ev[bi].Taken);
        }

        // ───────────── 1戦 ─────────────

        public class BattleOut
        {
            public BattleStats Stats;
            public readonly Dictionary<string, int> DamageBySource = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Played = new Dictionary<string, int>();
            public readonly Dictionary<string, int> HeldTurns = new Dictionary<string, int>();
            public readonly List<string> ItemsUsed = new List<string>();
            public int FourCommandTurns, MaxCommands, SpikeHitsOnEnemies, HandSizeSum, Turns, MaxDealtInTurn, Sequences;
            /// <summary>実際に受けたダメージの合計（回復で相殺しない）。</summary>
            public int GrossTaken;
            /// <summary>【コピー圧力】直接（敵がコピーした攻撃）・誘発（コピーで動いた結果の兵種の行動など）・危険マス（敵のコピーでトゲへ押された）。</summary>
            public int CopyDirect, CopyInduced, CopyHazard;
            /// <summary>【コピー圧力】測ったターン数・罠のターン（コピーを無視した最善手が、実際は被弾する）・制約のターン（コピーのせいで最善手を変えた）・コピー税（点数の差の合計）。</summary>
            public int MeasuredTurns, TrapTurns, ConstraintTurns;
            public double CopyTax;
            public readonly List<string> Examples = new List<string>();
            /// <summary>【盤面を使ったか】ラウンドの終わりにあなたがいたマスの種類数・壁で止まった敵の移動・回復マス（あなた／敵）。</summary>
            public readonly HashSet<Vector2Int> Cells = new HashSet<Vector2Int>();
            public int WallBlocks, HealTilesMine, HealTilesEnemy, HealCardSelf;
            public long Millis;
        }

        public static BattleOut Battle(ActRun run, RoomDef room, Bot bot, Random rng, int bonusSlots = -1, bool measure = false, bool noCopyWorld = false)
        {
            var watch = Stopwatch.StartNew();
            // 観測士（完全予測）：コピーを読み落とさない
            if (run.FullPrediction && bot.MissRead > 0) bot = new Bot { Name = bot.Name, Penalty = bot.Penalty, Width = bot.Width, Lookahead = bot.Lookahead, DealWeight = bot.DealWeight };
            var stage = StageFor(room, out int enemyHp);
            stage.playerMaxHp = run.MaxHp;
            stage.playerStartHp = run.Hp;
            if (enemyHp > 0) stage.enemyMaxHp = enemyHp;
            var m = BattleModel.FromStage(stage);
            UnityEngine.Object.DestroyImmediate(stage);
            m.SpikeFirstBonus = run.SpikeFirstBonus;
            int slots = 3 + (bonusSlots >= 0 ? bonusSlots : run.BonusSlots);
            // コピーが無い世界（A/B 比較）：全部の札を「コピーされない」複製に置き換える
            Func<CommandData, CommandData> world = c => c;
            if (noCopyWorld)
            {
                var map = new Dictionary<CommandData, CommandData>();
                foreach (var c in run.Deck.Distinct()) { var k = UnityEngine.Object.Instantiate(c); k.name = c.name; k.notCopied = true; map[c] = k; }
                world = c => map.TryGetValue(c, out var k) ? k : c;
            }
            var draw = run.Deck.Select(world).OrderBy(_ => rng.Next()).ToList();
            var discard = new List<CommandData>();
            var hand = new List<CommandData>();
            int pendingDraw = 0, pendingForesight = 0;
            void DrawOne()
            {
                if (hand.Count >= DeckState.SafetyHandLimit) return;
                if (draw.Count == 0) { draw = discard.OrderBy(_ => rng.Next()).ToList(); discard.Clear(); if (draw.Count == 0) return; }
                hand.Add(draw[0]);
                draw.RemoveAt(0);
            }
            void Refill() { while (hand.Count < run.BaseHand && (draw.Count > 0 || discard.Count > 0)) DrawOne(); }
            Refill();
            var outp = new BattleOut();
            int hp0 = m.Player.Hp;
            var events = new List<BattleEvent>();
            var outcome = TurnOutcome.Continue;
            int t = 0;
            bool blankUsed = false;
            while (outcome == TurnOutcome.Continue && t < 25)
            {
                t++;
                for (; pendingForesight > 0; pendingForesight--)
                {
                    if (draw.Count == 0) { draw = discard.OrderBy(_ => rng.Next()).ToList(); discard.Clear(); }
                    var top = draw.Take(3).ToList();
                    if (top.Count == 0) break;
                    var pick = top.OrderByDescending(c => c.damage).First();
                    draw.Remove(pick);
                    if (hand.Count < DeckState.SafetyHandLimit) hand.Add(pick);
                }
                // アイテム（自分の番の始め）
                if (run.Items.Contains(ItemId.Potion) && m.Player.Hp <= run.MaxHp * 0.4f) { m.Player.Heal(6); run.ConsumeItem(ItemId.Potion); outp.ItemsUsed.Add("回復薬"); }
                if (run.Items.Contains(ItemId.Hourglass) && !hand.Any(c => c.damage > 0))
                {
                    int n = hand.Count; discard.AddRange(hand); hand.Clear();
                    for (int i = 0; i < n; i++) DrawOne();
                    run.ConsumeItem(ItemId.Hourglass); outp.ItemsUsed.Add("選別の砂時計");
                }
                if (run.Items.Contains(ItemId.Hook) && !hand.Any(c => c.damage > 0) && discard.Any(c => c.damage > 0))
                {
                    var c = discard.First(x => x.damage > 0); discard.Remove(c); hand.Add(c);
                    run.ConsumeItem(ItemId.Hook); outp.ItemsUsed.Add("回収の鉤");
                }
                foreach (var c in hand.Distinct()) outp.HeldTurns[c.name] = (outp.HeldTurns.TryGetValue(c.name, out var h) ? h : 0) + 1;
                outp.HandSizeSum += hand.Count;
                var seqs = Sequences(hand, slots, rng);
                outp.Sequences += seqs.Count;
                var pickSeq = Choose(m, seqs, run, bot, rng, hand, draw, slots, measure, outp, room, t);
                // 命令石：上限 +1 で明らかに良くなるなら使う（コピーは全部読む前提）
                if (run.Items.Contains(ItemId.CommandStone))
                {
                    var cur = Best(m, new List<List<CommandRecord>> { pickSeq }, run.AttackBonusEachTurn, bot);
                    var more = Best(m, Sequences(hand, slots + 1, rng), run.AttackBonusEachTurn, bot);
                    if (more.score > cur.score + 3) { pickSeq = more.seq; run.ConsumeItem(ItemId.CommandStone); outp.ItemsUsed.Add("命令石"); }
                }
                // 空白の石：この命令列で被弾するなら、1つコピーさせないと良くなるか
                if (!blankUsed && run.Items.Contains(ItemId.BlankStone) && pickSeq.Count > 0)
                {
                    var cur = Best(m, new List<List<CommandRecord>> { pickSeq }, run.AttackBonusEachTurn, bot);
                    if (cur.taken >= 3)
                    {
                        var variants = new List<List<CommandRecord>>();
                        for (int i = 0; i < pickSeq.Count; i++)
                        {
                            var v = pickSeq.Select(r => r.Clone()).ToList();
                            var blank = UnityEngine.Object.Instantiate(v[i].Command);
                            blank.name = v[i].Command.name;
                            blank.notCopied = true;
                            v[i] = new CommandRecord(blank, v[i].Direction, v[i].Distance);
                            variants.Add(v);
                        }
                        var b2 = Best(m, variants, run.AttackBonusEachTurn, bot);
                        if (b2.score > cur.score + 3) { pickSeq = b2.seq; blankUsed = true; run.ConsumeItem(ItemId.BlankStone); outp.ItemsUsed.Add("空白の石"); }
                    }
                }
                if (pickSeq.Count >= 4) outp.FourCommandTurns++;
                outp.MaxCommands = Math.Max(outp.MaxCommands, pickSeq.Count);
                int ehpBefore = StagnationRule.EnemyHpTotal(m);
                if (run.AttackBonusEachTurn > 0) m.Player.AttackBonus += run.AttackBonusEachTurn;
                int evStart = events.Count;
                outcome = TurnRules.PlayTurn(m, pickSeq, null, events);
                outp.MaxDealtInTurn = Math.Max(outp.MaxDealtInTurn, ehpBefore - StagnationRule.EnemyHpTotal(m));
                foreach (var r in pickSeq)
                {
                    var orig = hand.FirstOrDefault(c => c == r.Command || c.name == r.Command.name);
                    if (orig == null) continue;
                    hand.Remove(orig);
                    if (!orig.exhaust) discard.Add(orig); // 消耗の札はこの戦闘から消える
                    outp.Played[orig.name] = (outp.Played.TryGetValue(orig.name, out var p) ? p : 0) + 1;
                    if (orig.type == CommandType.Reconsider) pendingDraw += Mathf.Max(1, orig.range);
                    if (orig.type == CommandType.Foresight) pendingForesight++;
                }
                var roundEvents = events.Skip(evStart).ToList();
                outp.Cells.Add(m.Player.Position);
                foreach (var e in roundEvents)
                {
                    if (e.Type == BattleEventType.Bumped && e.Unit != null && !e.Unit.IsPlayer && m.Board.IsWall(e.To) && m.Board.IsInside(e.To)) outp.WallBlocks++;
                    if (e.Type == BattleEventType.Healed && e.Unit != null)
                    {
                        if (e.Unit.IsPlayer && pickSeq.Any(r => r.Command.type == CommandType.Heal)) outp.HealCardSelf += e.Amount;
                        else if (e.Unit.IsPlayer) outp.HealTilesMine++;
                        else if (!pickSeq.Any(r => r.Command.type == CommandType.Heal)) outp.HealTilesEnemy++;
                    }
                    if (e.Type != BattleEventType.Damaged || e.Unit == null || e.Cause == null) continue;
                    if (e.Unit.IsPlayer)
                    {
                        outp.GrossTaken += e.Amount;
                        string src2 = e.Cause.Kind == DamageKind.Spike || e.Cause.Kind == DamageKind.Fire ? "環境（トゲ・炎）"
                                    : e.Cause.Kind == DamageKind.Trait ? "兵種の行動（剣・槍・弓・爆発・大技）"
                                    : e.Cause.Source != null && e.Cause.Source.IsPlayer ? "自分の命令"
                                    : "敵のコピー攻撃（反射を含む）";
                        outp.DamageBySource[src2] = (outp.DamageBySource.TryGetValue(src2, out var d) ? d : 0) + e.Amount;
                    }
                    else if (e.Cause.Kind == DamageKind.Spike) outp.SpikeHitsOnEnemies++;
                }
                Refill();
                int extra = pendingDraw + run.RoundEndDraw(roundEvents, pickSeq.Count);
                pendingDraw = 0;
                for (int i = 0; i < extra; i++) DrawOne();
            }
            outp.Turns = t;
            outp.Stats = BattleStats.From(events, outcome == TurnOutcome.Cleared, t, hp0, Math.Max(0, m.Player.Hp));
            outp.Millis = watch.ElapsedMilliseconds;
            return outp;
        }

        /// <summary>
        /// ボットの1ターンの選択。読み落とし（MissRead）がある敵は「コピーしない」物として見積もって選ぶ（結果は本当のルールで起きる）。
        /// measure のとき、コピー圧力（罠・制約・コピー税・被ダメの内訳）を記録する。
        /// </summary>
        static List<CommandRecord> Choose(BattleModel m, List<List<CommandRecord>> seqs, ActRun run, Bot bot, Random rng, List<CommandData> hand,
            List<CommandData> draw, int slots, bool measure, BattleOut outp, RoomDef room, int turn)
        {
            if (seqs.Count == 0) return new List<CommandRecord>();
            if (bot.Width > 0 && seqs.Count > bot.Width) seqs = seqs.OrderBy(_ => rng.Next()).Take(bot.Width).ToList();
            int atk = run.AttackBonusEachTurn;
            var alive = m.Enemies.Where(e => e.IsAlive).Select(e => e.Id).ToList();
            var miss = new HashSet<int>(alive.Where(_ => rng.NextDouble() < bot.MissRead));
            Ev[] real = null;
            Ev[] perceived;
            if (miss.Count == 0) perceived = real = EvalAll(m, seqs, atk, bot, null);
            else perceived = EvalAll(m, seqs, atk, bot, miss);
            int pick = ArgMax(perceived);
            if (bot.Lookahead)
            {
                if (real == null) real = EvalAll(m, seqs, atk, bot, null);
                var top = Enumerable.Range(0, seqs.Count).OrderByDescending(i => real[i].Score).Take(8).ToList();
                float bestTotal = float.MinValue;
                foreach (int i in top)
                {
                    var sim = m.Clone();
                    if (atk > 0) sim.Player.AttackBonus += atk;
                    var o = TurnRules.PlayTurn(sim, seqs[i], null);
                    float total = real[i].Score;
                    if (o == TurnOutcome.Continue)
                    {
                        var left = new List<CommandData>(hand);
                        foreach (var r in seqs[i]) { var c = left.FirstOrDefault(x => x == r.Command); if (c != null) left.Remove(c); }
                        foreach (var c in draw) { if (left.Count >= run.BaseHand) break; left.Add(c); }
                        var next = Sequences(left, slots, rng, 600);
                        if (next.Count > 0) total += 0.8f * EvalAll(sim, next, atk, bot, null).Max(e => e.Score);
                    }
                    if (total > bestTotal) { bestTotal = total; pick = i; }
                }
            }
            if (measure)
            {
                if (real == null) real = EvalAll(m, seqs, atk, bot, null);
                var all = new HashSet<int>(alive);
                var naive = EvalAll(m, seqs, atk, bot, all);
                int nb = ArgMax(naive), rb = ArgMax(real);
                outp.MeasuredTurns++;
                if (real[nb].Taken > naive[nb].Taken)
                {
                    outp.TrapTurns++;
                    if (outp.Examples.Count < 2)
                        outp.Examples.Add($"{room.Name} {turn}T目：コピーを無視した最善手「{Text(seqs[nb])}」→ 実際は被弾 {real[nb].Taken}（コピーが無ければ {naive[nb].Taken}）。読み切った手「{Text(seqs[rb])}」は被弾 {real[rb].Taken}・与ダメ {real[rb].Dealt}\n{Render(m)}");
                }
                if (real[rb].Score > real[nb].Score + 0.5f) outp.ConstraintTurns++;
                outp.CopyTax += Math.Min(30, Math.Max(0, real[rb].Score - real[nb].Score));
                // 選んだ手の被ダメの内訳（コピーあり／なし）
                var simR = m.Clone(); var evR = new List<BattleEvent>();
                if (atk > 0) simR.Player.AttackBonus += atk;
                TurnRules.PlayTurn(simR, seqs[pick], null, evR);
                var simN = m.Clone(); var evN = new List<BattleEvent>();
                if (atk > 0) simN.Player.AttackBonus += atk;
                TurnRules.PlayTurn(simN, seqs[pick], null, evN, null, all);
                int gross(List<BattleEvent> ev) => ev.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer).Sum(e => e.Amount);
                int direct = evR.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null && e.Cause.ByCopy && e.Cause.Kind == DamageKind.Direct).Sum(e => e.Amount);
                int hazard = evR.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null && e.Cause.ByCopy && e.Cause.Kind == DamageKind.Spike).Sum(e => e.Amount);
                outp.CopyDirect += direct;
                outp.CopyHazard += hazard;
                outp.CopyInduced += Math.Max(0, gross(evR) - gross(evN) - direct - hazard);
            }
            return seqs[pick];
        }

        internal static string Text(List<CommandRecord> seq) => string.Join(" → ", seq.Select(r => r.Command.displayName + (r.Command.UsesDirection ? r.Direction.ToLabel() : "")));

        /// <summary>盤面の文字（解析の例に貼る）：P あなた・英字＝敵の記号（小文字は向き無し）・^ トゲ・F このラウンドに燃える・w ワープ・# 壁。</summary>
        internal static string Render(BattleModel m)
        {
            var b = m.Board;
            var sb = new StringBuilder();
            var fire = m.Environment.FireCellsNow;
            for (int y = b.Height - 1; y >= 0; y--)
            {
                sb.Append("    ");
                for (int x = 0; x < b.Width; x++)
                {
                    var c = new Vector2Int(x, y);
                    var u = m.GetUnitAt(c);
                    char ch = '.';
                    if (b.IsWall(c)) ch = '#';
                    else if (u != null) ch = u.IsPlayer ? 'P' : Symbol(u);
                    else if (b.HasSpike(c)) ch = '^';
                    else if (fire.Contains(c)) ch = 'F';
                    else if (b.TryWarp(c, out _)) ch = 'w';
                    else if (m.Environment.RuneCell == c) ch = '+';
                    else if (m.Environment.HealTiles.Contains(c)) ch = 'h';
                    sb.Append(ch);
                }
                sb.AppendLine();
            }
            sb.Append("    敵：" + string.Join("・", m.Enemies.Where(e => e.IsAlive).Select(e => $"{e.Name}{PhaseRules.KindName(e.Kind)}({Symbol(e)}) HP{e.Hp} 向き{e.Facing.ToLabel()}")) + $"　あなた HP{m.Player.Hp}");
            return sb.ToString();
        }

        static char Symbol(UnitModel u)
        {
            switch (u.Kind)
            {
                case EnemyKind.Swordsman: return 'S';
                case EnemyKind.Spearman: return 'Y';
                case EnemyKind.Bomber: return 'O';
                case EnemyKind.MirrorSoldier: return 'M';
                case EnemyKind.Archer: return 'K';
                case EnemyKind.Shield: return 'T';
                case EnemyKind.Chaser: return 'C';
                case EnemyKind.Fleer: return 'N';
                case EnemyKind.KnightV3: return 'Q';
                default: return 'E';
            }
        }

        // ───────────── ボットの方針（報酬・商人・休息） ─────────────

        static readonly Dictionary<string, float> CardValue = new Dictionary<string, float>
        {
            { "Slash", 5 }, { "Thrust", 5 }, { "PushSlash", 6 }, { "Sweep", 6 }, { "Pierce", 5 }, { "Whirl", 5 }, { "Spirit", 4 },
            { "Walk", 1 }, { "Dash", 3 }, { "Rush", 3 }, { "Jump", 5 }, { "Push", 4 }, { "Pull", 5 }, { "Trap", 4 },
            { "Foresight", 4 }, { "Reconsider", 4 }, { "Rally", 5 }, { "Sneak", 4 }, { "MirrorSlash", 5 }, { "ReverseCopy", 3 },
            { "ChippedSlash", 6 }, { "Lunge", 4 }, { "FirstAid", 4 }, { "Lure", 4.5f }, { "WallKick", 4.5f }, // RC：新しい札も試すように少し上げた（ボットの好みで一度も取られず、使えるか分からなかったため）
        };

        static float Value(CommandData c, Random rng) => (CardValue.TryGetValue(c.name, out var v) ? v : 3) + (float)(rng.NextDouble() * 3 - 1.5);

        public class RunOut
        {
            public bool Clear; public int Floor, Turns, FinalHp, ShardsEarned, ShardsLeft, CardsTaken, RewardScreens, Skips, Removals, ShopVisits, ShopNothing, MissionsTried, MissionsDone, ParBonuses;
            public int HpAtBoss = -1;
            /// <summary>【検証中】ボスの扉に入ったときのルーン片（使い残し）・旅立ちの選択。</summary>
            public int ShardsAtBoss = -1;
            public string Boon = "なし";
            public bool TookDanger;
            public readonly List<int> HpTrace = new List<int>();
            public readonly List<(RoomDef room, BattleOut b)> Battles = new List<(RoomDef, BattleOut)>();
            public readonly List<string> Bought = new List<string>();
            public readonly List<string> CopySlots = new List<string>();
            public readonly List<string> Taken = new List<string>();
            public readonly List<string> Offered = new List<string>();
            public readonly List<ImprintId> Imprints = new List<ImprintId>();
            public readonly List<(int shards, int[] cardPrices, int[] itemPrices, int imprintPrice, int removePrice)> ShopLog = new List<(int, int[], int[], int, int)>();
            public string DeathRoom;
        }

        public static RunOut PlayRun(RoleDef role, int seed, Bot bot, ImprintId? startImprint = null, bool measure = false)
        {
            var run = new ActRun(role, seed, C);
            if (startImprint.HasValue && !run.Has(startImprint.Value)) run.Imprints.Add(startImprint.Value);
            var rng = new Random(seed * 7919 + role.Id.GetHashCode());
            var o = new RunOut();
            // 【検証中】旅立ちの選択（3つから無作為に1つ。人によって選ぶ物が違うため）。ACT1_NOBOON=1 で無し（比べる用）
            if (Environment.GetEnvironmentVariable("ACT1_NOBOON") != "1")
            {
                run.RollBoons();
                if (run.BoonOptions.Count > 0)
                {
                    var bo = run.BoonOptions[rng.Next(run.BoonOptions.Count)];
                    int idx = bo.Kind == BoonKind.Trim ? run.Deck.IndexOf(run.Deck.OrderBy(c => CardValue.TryGetValue(c.name, out var v) ? v : 3).First()) : -1;
                    if (run.TakeBoon(bo, idx)) o.Boon = bo.Kind.ToString();
                }
            }
            int guard = 0;
            o.HpTrace.Add(run.Hp);
            while (!run.Cleared && !run.Failed && run.Floor < run.FloorCount && guard++ < 20)
            {
                var doors = run.Doors;
                var room = doors[rng.Next(doors.Count)];
                // 扉の選び方（人と同じく HP を見る）：HP 70% 未満なら休息を、50% 未満なら戦闘でない扉（特殊部屋）を選ぶ
                var rest = doors.FirstOrDefault(r => r.Kind == RoomKind.Rest);
                var calm = doors.FirstOrDefault(r => r.Kind == RoomKind.Event);
                if (rest != null && run.Hp < run.MaxHp * 0.7f) room = rest;
                else if (calm != null && run.Hp < run.MaxHp * 0.5f) room = calm;
                // 【検証中】地図 v2：危険戦と通常戦／特殊部屋が並ぶ階では、HP 60% 未満なら危険戦を避ける
                var safe = doors.FirstOrDefault(r => r.Kind != RoomKind.Danger);
                if (room.Kind == RoomKind.Danger && safe != null && run.Hp < run.MaxHp * 0.6f) room = safe;
                if (room.Kind == RoomKind.Danger) o.TookDanger = true;
                if (room.Kind == RoomKind.Boss) o.ShardsAtBoss = run.Shards;
                run.Enter(room);
                switch (room.Kind)
                {
                    case RoomKind.Battle:
                    case RoomKind.Danger:
                    case RoomKind.Boss:
                        if (room.Kind == RoomKind.Boss) o.HpAtBoss = run.Hp;
                        var b = Battle(run, room, bot, rng, -1, measure);
                        o.Battles.Add((room, b));
                        o.Turns += b.Turns;
                        if (room.Mission != null) o.MissionsTried++;
                        if (!b.Stats.Won) { run.LoseBattle(b.Stats); o.DeathRoom = room.Name; break; }
                        int missionsBefore = run.MissionsDone, parBefore = run.ParBonuses;
                        run.WinBattle(b.Stats);
                        o.MissionsDone += run.MissionsDone - missionsBefore;
                        o.ParBonuses += run.ParBonuses - parBefore;
                        if (run.Cleared) break;
                        o.RewardScreens++;
                        foreach (var rc in run.RewardCards) { o.Offered.Add(rc.Card.name); if (rc.IsCopySlot) o.CopySlots.Add(rc.Card.name); }
                        int taken = 0;
                        while (run.RewardPicksLeft > 0 && run.RewardCards.Count > 0)
                        {
                            var best = run.RewardCards.OrderByDescending(rc => Value(rc.Card, rng) + (rc.IsCopySlot ? 1 : 0)).First();
                            float v = Value(best.Card, rng);
                            if (v < 3.5f || (run.Deck.Count >= 20 && v < 5)) break;
                            run.TakeRewardCard(best);
                            o.Taken.Add(best.Card.name);
                            taken++;
                        }
                        o.CardsTaken += taken;
                        if (taken == 0) o.Skips++;
                        if (run.RewardImprints.Count > 0)
                        {
                            var imp = run.RewardImprints[rng.Next(run.RewardImprints.Count)];
                            run.TakeRewardImprint(imp);
                        }
                        run.Leave();
                        break;
                    case RoomKind.Rest:
                        if (run.Hp < run.MaxHp * 0.7f) run.RestHeal();
                        else
                        {
                            var worst = run.Deck.OrderBy(c => CardValue.TryGetValue(c.name, out var v) ? v : 3).First();
                            if (run.RestRemove(worst)) o.Removals++;
                        }
                        run.Leave();
                        break;
                    case RoomKind.Shop:
                        o.ShopVisits++;
                        o.ShopLog.Add((run.Shards, run.ShopCards.Select(c => c.Price).ToArray(), run.ShopItems.Select(i => i.Price).ToArray(),
                            run.ShopImprint.HasValue ? ActContent.Imprint(run.ShopImprint.Value).Price : 0, run.RemovePrice));
                        int boughtBefore = o.Bought.Count;
                        // HP が減っていれば回復薬を先に
                        // 回復薬：HP が減っているか、ボスの前の商人なら（持っていなければ）買う
                        bool beforeBoss = run.Floor == run.FloorCount - 2;
                        if (run.Hp < run.MaxHp * 0.7f || (beforeBoss && !run.Items.Contains(ItemId.Potion)))
                            foreach (var si in run.ShopItems.Where(i => i.Id == ItemId.Potion)) if (run.BuyItem(si)) o.Bought.Add("アイテム:回復薬");
                        if (run.ShopImprint.HasValue && run.BuyImprint()) o.Bought.Add("刻印:" + ActContent.Imprint(run.Imprints.Last()).Name);
                        if (run.Deck.Count > 10)
                        {
                            var worst = run.Deck.OrderBy(c => CardValue.TryGetValue(c.name, out var v) ? v : 3).First();
                            if ((CardValue.TryGetValue(worst.name, out var wv) ? wv : 3) <= 1 && run.RemoveCardPaid(worst)) { o.Removals++; o.Bought.Add("削除"); }
                        }
                        foreach (var sc in run.ShopCards.OrderByDescending(c => Value(c.Card, rng)))
                            if (Value(sc.Card, rng) >= 5 && run.BuyCard(sc)) { o.Bought.Add("カード:" + sc.Card.name); break; }
                        foreach (var si in run.ShopItems)
                            if (run.BuyItem(si)) { o.Bought.Add("アイテム:" + ActContent.Item(si.Id).Name); break; }
                        if (o.Bought.Count == boughtBefore) o.ShopNothing++;
                        run.Leave();
                        break;
                    default:
                        if (room.Id == "event_mirror" && run.EventCards.Count > 0 && run.Hp > 10) run.MirrorTake(run.EventCards.OrderByDescending(c => Value(c, rng)).First());
                        else if (room.Id == "event_trade") { if (run.MaxHp > 16 && run.RewardImprints.Count > 0) run.AltarImprint(run.RewardImprints[0]); else run.AltarShards(); }
                        run.Leave();
                        break;
                }
                o.HpTrace.Add(run.Hp);
            }
            o.Clear = run.Cleared;
            o.Floor = Math.Min(run.Floor + 1, run.FloorCount);
            o.FinalHp = run.Hp;
            o.ShardsEarned = run.ShardsEarned;
            o.ShardsLeft = run.Shards;
            o.Imprints.AddRange(run.Imprints);
            return o;
        }

        // ───────────── 役職ごと ─────────────

        [MenuItem("CopyCommand/解析：ACT1 大量シミュレーション（役職）")]
        public static void RunRoles() => RunRolesWith(Careful, "Logs/act1_sim_roles.txt");

        public static void RunRolesWith(Bot bot, string path, int seedBase = 1000)
        {
            RoomOverrides = new Dictionary<string, RoomBoard>();
            int n = Runs;
            var all = new Dictionary<string, List<RunOut>>();
            var sb = new StringBuilder($"【研究】ACT1 大量シミュレーション（役職ごと {n} ラン・ボット {bot.Name}）\n\n");
            var watch = Stopwatch.StartNew();
            foreach (var role in ActContent.Roles)
            {
                var list = new List<RunOut>();
                for (int s = 1; s <= n; s++) list.Add(PlayRun(role, seedBase + s, bot));
                all[role.Name] = list;
                Debug.Log($"[Act1Sim] {role.Name} 完了 {watch.Elapsed.TotalMinutes:0.0}分");
                sb.Append(Summary(role.Name, list));
                Write(sb, path);
            }
            sb.Append(CrossSummary(all));
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, path);
        }

        internal static string Pct(int a, int b) => b == 0 ? "—" : $"{100.0 * a / b:0}%";

        internal static string Summary(string label, List<RunOut> runs)
        {
            var sb = new StringBuilder($"=== {label}（{runs.Count} ラン）\n");
            var clears = runs.Where(r => r.Clear).ToList();
            sb.AppendLine($"  クリア {clears.Count}/{runs.Count}（{Pct(clears.Count, runs.Count)}）　平均残り HP {(clears.Count > 0 ? clears.Average(r => r.FinalHp) : 0):0.0}　平均ターン {runs.Average(r => r.Turns):0.0}");
            var atBoss = runs.Where(r => r.HpAtBoss >= 0).ToList();
            if (atBoss.Count > 0) sb.AppendLine($"  ボス到達 {atBoss.Count}/{runs.Count}・到達時 HP 平均 {atBoss.Average(r => r.HpAtBoss):0.0}（最小 {atBoss.Min(r => r.HpAtBoss)}・最大 {atBoss.Max(r => r.HpAtBoss)}）");
            var deaths = runs.Where(r => !r.Clear).GroupBy(r => r.DeathRoom ?? "?").OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}");
            sb.AppendLine($"  倒れた場所：{string.Join("、", deaths)}");
            foreach (var kind in new[] { RoomKind.Battle, RoomKind.Danger, RoomKind.Boss })
            {
                var bs = runs.SelectMany(r => r.Battles).Where(x => x.room.Kind == kind).ToList();
                if (bs.Count == 0) continue;
                sb.AppendLine($"  {kind}：{bs.Count}戦　平均 {bs.Average(x => x.b.Turns):0.0}T　平均被ダメ {bs.Average(x => x.b.GrossTaken):0.0}　無傷 {Pct(bs.Count(x => x.b.GrossTaken == 0), bs.Count)}　負け {bs.Count(x => !x.b.Stats.Won)}");
            }
            var src = new Dictionary<string, int>();
            foreach (var b in runs.SelectMany(r => r.Battles)) foreach (var kv in b.b.DamageBySource) src[kv.Key] = (src.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            int tot = Math.Max(1, src.Values.Sum());
            sb.AppendLine($"  被ダメの出どころ：{string.Join("・", src.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {100 * k.Value / tot}%"))}");
            var bsAll = runs.SelectMany(r => r.Battles).ToList();
            sb.AppendLine($"  敵の倒し方：環境キル {bsAll.Sum(x => x.b.Stats.EnvironmentKills)}・同士討ち {bsAll.Sum(x => x.b.Stats.FriendlyFire)} 回・コピーで敵に当たった {bsAll.Sum(x => x.b.Stats.CopyDamage)} 回（{bsAll.Count} 戦）");
            sb.AppendLine($"  ルーン片：稼ぎ {runs.Average(r => r.ShardsEarned):0}・残り {runs.Average(r => r.ShardsLeft):0}　商人 {runs.Sum(r => r.ShopVisits)} 回（何も買えず {runs.Sum(r => r.ShopNothing)}）　削除 {runs.Average(r => r.Removals):0.0}/ラン　速さの報酬 {runs.Sum(r => r.ParBonuses)} 回");
            // 【検証中】2026-10-05 経済の指標（本人「最初の商人で何も買えない」）
            var first = runs.Where(r => r.ShopLog.Count > 0).Select(r => r.ShopLog[0]).ToList();
            if (first.Count > 0)
            {
                var fs = first.Select(s => s.shards).OrderBy(v => v).ToList();
                sb.AppendLine($"  最初の商人（{first.Count}ラン）：ルーン片 中央値 {fs[fs.Count / 2]}（最小 {fs[0]}・最大 {fs[fs.Count - 1]}）　新しいカードを買える {Pct(first.Count(s => s.cardPrices.Length > 0 && s.shards >= s.cardPrices.Min()), first.Count)}" +
                              $"・珍しい(45)以上 {Pct(first.Count(s => s.shards >= 45), first.Count)}・回復薬 {Pct(first.Count(s => s.itemPrices.Length > 0 && s.shards >= s.itemPrices.Min()), first.Count)}・削除 {Pct(first.Count(s => s.shards >= s.removePrice), first.Count)}・どれも無理 {Pct(first.Count(s => s.shards < Math.Min(s.removePrice, s.itemPrices.DefaultIfEmpty(99).Min())), first.Count)}");
            }
            var boss = runs.Where(r => r.ShardsAtBoss >= 0).ToList();
            if (boss.Count > 0) sb.AppendLine($"  ボスの扉で使い残したルーン片：平均 {boss.Average(r => r.ShardsAtBoss):0}・最大 {boss.Max(r => r.ShardsAtBoss)}");
            sb.AppendLine($"  旅立ちの選択：{string.Join("・", runs.GroupBy(r => r.Boon).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}"))}　危険戦に入ったラン {Pct(runs.Count(r => r.TookDanger), runs.Count)}");
            sb.AppendLine($"  カード取得 {runs.Average(r => r.CardsTaken):0.0}/ラン　スキップ率 {Pct(runs.Sum(r => r.Skips), runs.Sum(r => r.RewardScreens))}　任意ミッション {runs.Sum(r => r.MissionsDone)}/{runs.Sum(r => r.MissionsTried)}");
            var items = bsAll.SelectMany(x => x.b.ItemsUsed).GroupBy(x => x).Select(g => $"{g.Key}×{g.Count()}");
            sb.AppendLine($"  アイテム使用：{string.Join("・", items)}　（4命令のターン {bsAll.Sum(x => x.b.FourCommandTurns)}・敵へのトゲ {bsAll.Sum(x => x.b.SpikeHitsOnEnemies)}・平均手札 {bsAll.Sum(x => x.b.HandSizeSum) / (double)Math.Max(1, bsAll.Sum(x => x.b.Turns)):0.0}）");
            var imps = runs.SelectMany(r => r.Imprints).GroupBy(x => x).Select(g => $"{ActContent.Imprint(g.Key).Name}×{g.Count()}");
            sb.AppendLine($"  刻印（最後に持っていた）：{string.Join("・", imps)}");
            var rooms = bsAll.GroupBy(x => x.room.Name).Select(g => $"{g.Key} {g.Average(x => x.b.Turns):0.0}T/被{g.Average(x => x.b.GrossTaken):0.0}/無傷{Pct(g.Count(x => x.b.GrossTaken == 0), g.Count())}/負{g.Count(x => !x.b.Stats.Won)}");
            sb.AppendLine($"  部屋：{string.Join("　", rooms)}");
            var hp = Enumerable.Range(0, 8).Select(i => runs.Where(r => r.HpTrace.Count > i).Select(r => r.HpTrace[i]).DefaultIfEmpty(0).Average());
            sb.AppendLine($"  HP の推移（開始・各部屋の後）：{string.Join(" → ", hp.Select(h => h.ToString("0.0")))}");
            sb.AppendLine($"  計算：1戦平均 {bsAll.Average(x => x.b.Millis) / 1000.0:0.0}秒・命令列 {bsAll.Sum(x => (double)x.b.Sequences) / Math.Max(1, bsAll.Sum(x => x.b.Turns)):0} 本/ターン");
            sb.AppendLine();
            return sb.ToString();
        }

        internal static string CrossSummary(Dictionary<string, List<RunOut>> all)
        {
            var runs = all.Values.SelectMany(x => x).ToList();
            var sb = new StringBuilder("=== 全役職まとめ\n");
            sb.Append(Summary("全体", runs).Replace("=== 全体", "  （全体）"));
            var played = new Dictionary<string, int>();
            var held = new Dictionary<string, int>();
            foreach (var b in runs.SelectMany(r => r.Battles))
            {
                foreach (var kv in b.b.Played) played[kv.Key] = (played.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
                foreach (var kv in b.b.HeldTurns) held[kv.Key] = (held.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            }
            sb.AppendLine("  カード：報酬に出た／取られた／写し取りに出た／使った率（手札にあったターンのうち）");
            foreach (var c in ActContent.CardPool)
            {
                int off = runs.Sum(r => r.Offered.Count(x => x == c.Asset));
                int tak = runs.Sum(r => r.Taken.Count(x => x == c.Asset));
                int cs = runs.Sum(r => r.CopySlots.Count(x => x == c.Asset));
                int pl = played.TryGetValue(c.Asset, out var p) ? p : 0;
                int hd = held.TryGetValue(c.Asset, out var h) ? h : 0;
                sb.AppendLine($"    {C(c.Asset).displayName,-6} 出 {off,4} 取 {tak,4} 写 {cs,4}  使用率 {Pct(pl, hd),4}（{pl}/{hd}）");
            }
            var shops = runs.SelectMany(r => r.ShopLog).ToList();
            if (shops.Count == 0) return sb.ToString();
            sb.AppendLine($"  商人の訪問 {shops.Count} 回の時点のルーン片：平均 {shops.Average(s => s.shards):0}・最小 {shops.Min(s => s.shards)}・最大 {shops.Max(s => s.shards)}");
            int none = 0, cardOk = 0, impOk = 0, remOk = 0, both = 0;
            foreach (var s in shops)
            {
                bool c1 = s.cardPrices.Any(p => p <= s.shards);
                bool i1 = s.imprintPrice > 0 && s.imprintPrice <= s.shards;
                bool r1 = s.removePrice <= s.shards;
                if (!(c1 || i1 || r1 || s.itemPrices.Any(p => p <= s.shards))) none++;
                if (c1) cardOk++;
                if (i1) impOk++;
                if (r1) remOk++;
                if (c1 && r1 && s.cardPrices.Min() + s.removePrice > s.shards) both++;
            }
            sb.AppendLine($"    今の値段：何も買えない {Pct(none, shops.Count)}・カードを買える {Pct(cardOk, shops.Count)}・刻印 {Pct(impOk, shops.Count)}・削除 {Pct(remOk, shops.Count)}・「カードか削除か」の選択 {Pct(both, shops.Count)}");
            return sb.ToString();
        }

        // ───────────── カード1種を足した戦闘 ─────────────

        [MenuItem("CopyCommand/解析：ACT1 カードの強さ")]
        public static void RunCards()
        {
            RoomOverrides = new Dictionary<string, RoomBoard>();
            var sb = new StringBuilder("【研究】カード1種を2枚足したデッキで各部屋を戦う（最初のデッキ12枚＋2枚・HP 20・各部屋2回・慎重ボット）\n" +
                                       "差＝最初のデッキだけのときとの差（ターンは少ないほど強い・被ダメは少ないほど強い）\n\n");
            var rooms = ActContent.Rooms.Where(r => r.Stage != null).ToList();
            var role = ActContent.Roles[0];
            (double turns, double dmg, int loss) Measure(string add)
            {
                var list = new List<BattleOut>();
                foreach (var room in rooms)
                    for (int s = 0; s < 2; s++)
                    {
                        var run = new ActRun(role, 500 + s, C);
                        if (add != null) { run.Deck.Add(C(add)); run.Deck.Add(C(add)); }
                        list.Add(Battle(run, room, Careful, new Random(77 + s * 13 + room.Id.GetHashCode())));
                    }
                return (list.Average(b => b.Turns), list.Average(b => b.GrossTaken), list.Count(b => !b.Stats.Won));
            }
            var watch = Stopwatch.StartNew();
            var baseRes = Measure(null);
            sb.AppendLine($"基準（足さない）：平均 {baseRes.turns:0.00}T・被ダメ {baseRes.dmg:0.00}・負け {baseRes.loss}");
            foreach (var c in ActContent.CardPool)
            {
                var r = Measure(c.Asset);
                sb.AppendLine($"  {C(c.Asset).displayName,-6}：{r.turns:0.00}T（差 {r.turns - baseRes.turns:+0.00;-0.00}）・被ダメ {r.dmg:0.00}（差 {r.dmg - baseRes.dmg:+0.00;-0.00}）・負け {r.loss}");
                Write(sb, "Logs/act1_sim_cards.txt");
                Debug.Log($"[Act1Sim] cards {c.Asset} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            sb.AppendLine($"（{watch.Elapsed.TotalMinutes:0.0}分）");
            Write(sb, "Logs/act1_sim_cards.txt");
        }

        // ───────────── 命令の上限 3〜6 ─────────────

        [MenuItem("CopyCommand/解析：ACT1 多命令")]
        public static void RunMulti()
        {
            RoomOverrides = new Dictionary<string, RoomBoard>();
            var sb = new StringBuilder("【研究】命令の上限 3〜6（敵も同じ数をコピー）。最初のデッキ12枚＋号令なし・HP 20・各部屋3回・慎重ボット。上限 5・6 は命令列を無作為に 2500 本\n\n");
            var rooms = ActContent.Rooms.Where(r => r.Stage != null).ToList();
            var role = ActContent.Roles[0];
            var watch = Stopwatch.StartNew();
            foreach (int bonus in new[] { 0, 1, 2, 3 })
            {
                var list = new List<(RoomDef room, BattleOut b)>();
                foreach (var room in rooms)
                    for (int s = 0; s < 3; s++)
                    {
                        var run = new ActRun(role, 900 + s, C);
                        list.Add((room, Battle(run, room, Careful, new Random(31 + s * 7 + room.Id.GetHashCode()), bonus)));
                    }
                var bs = list.Select(x => x.b).ToList();
                int oneTurn = list.Count(x => x.b.Stats.Won && x.b.Turns == 1);
                sb.AppendLine($"=== 上限 {3 + bonus}：平均 {bs.Average(b => b.Turns):0.00}T・被ダメ {bs.Average(b => b.GrossTaken):0.00}・1ターンの最大与ダメ {bs.Average(b => b.MaxDealtInTurn):0.0}（最大 {bs.Max(b => b.MaxDealtInTurn)}）・" +
                              $"1ターンキル {oneTurn}・負け {bs.Count(b => !b.Stats.Won)}・4命令以上のターン {bs.Sum(b => b.FourCommandTurns)}/{bs.Sum(b => b.Turns)}・計算 {bs.Average(b => b.Millis) / 1000.0:0.0}秒/戦・命令列 {bs.Sum(b => (double)b.Sequences) / bs.Sum(b => b.Turns):0}本/ターン");
                sb.AppendLine("    部屋：" + string.Join("　", list.GroupBy(x => x.room.Name).Select(g => $"{g.Key} {g.Average(x => x.b.Turns):0.0}T/被{g.Average(x => x.b.GrossTaken):0.0}")));
                Write(sb, "Logs/act1_sim_multi.txt");
                Debug.Log($"[Act1Sim] multi {bonus} {watch.Elapsed.TotalMinutes:0.0}分");
            }
            Write(sb, "Logs/act1_sim_multi.txt");
        }

        // ───────────── 人に近いボット（被弾を気にする度合いを下げる） ─────────────

        [MenuItem("CopyCommand/解析：ACT1 慎重さ別")]
        public static void RunCareless()
        {
            RoomOverrides = new Dictionary<string, RoomBoard>();
            int n = 15;
            var sb = new StringBuilder($"【研究】慎重さ別の ACT1（標準の役職・{n} ラン）。中くらい＝敵へのダメージ − 被弾×1.0／雑＝被弾を気にしない\n\n");
            var role = ActContent.Roles[0];
            foreach (var bot in new[] { new Bot { Name = "中くらい", Penalty = 1.0f }, new Bot { Name = "雑", Penalty = 0.01f } })
            {
                var list = new List<RunOut>();
                for (int s = 1; s <= n; s++) list.Add(PlayRun(role, 5000 + s, bot));
                sb.Append(Summary("ボット「" + bot.Name + "」", list));
                Write(sb, "Logs/act1_sim_careless.txt");
            }
        }

        // ───────────── 刻印を最初から ─────────────

        [MenuItem("CopyCommand/解析：ACT1 刻印")]
        public static void RunImprints()
        {
            RoomOverrides = new Dictionary<string, RoomBoard>();
            int n = Math.Max(10, Runs / 2);
            var sb = new StringBuilder($"【研究】刻印を最初から1つ持たせた ACT1（標準の役職・{n} ラン・慎重ボット）\n\n");
            var role = ActContent.Roles[0];
            var watch = Stopwatch.StartNew();
            foreach (var imp in new ImprintId?[] { null, ImprintId.FourthCommand, ImprintId.ThornGuide, ImprintId.Echo, ImprintId.Margin })
            {
                var list = new List<RunOut>();
                for (int s = 1; s <= n; s++) list.Add(PlayRun(role, 3000 + s, Careful, imp));
                sb.Append(Summary(imp.HasValue ? "刻印：" + ActContent.Imprint(imp.Value).Name : "刻印なし（基準）", list));
                Write(sb, "Logs/act1_sim_imprints.txt");
                Debug.Log($"[Act1Sim] imprint {imp} {watch.Elapsed.TotalMinutes:0.0}分");
            }
        }
    }
}
