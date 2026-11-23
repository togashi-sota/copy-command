using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CopyCommand
{
    /// <summary>
    /// 盤面解析（レベルデザイン補助）。ゲーム本体と同じ C# のルール（RulesEngine / TurnRules）で、
    /// プレイヤーが取りうる全手順を幅優先で探索する。Python 版（tools/level_solver）との食い違いを無くすためのもの。
    /// 手札は「固定の手札」（使えるカードが全部並ぶ・同じカードは1ターン1回）を前提にする。
    /// </summary>
    public class BattleSolver
    {
        public class Settings
        {
            public List<CommandData> Cards = new List<CommandData>();
            public int MaxSlots = 3;
            public int MaxTurns = 5;
            public CommandData Innate;
            /// <summary>プレイヤーの HP がこの値を下回る手順は捨てる（最大 HP を入れると「無傷クリア」だけを探す）。</summary>
            public int MinPlayerHp = 1;
            /// <summary>true なら、プレイヤー自身の直接攻撃（斬撃など）で敵にダメージを与える手順を捨てる（コピー・ギミックだけで勝つ道を調べる）。</summary>
            public bool ForbidPlayerDirectDamage;
            /// <summary>使える技を種類で絞る（null なら全部）。</summary>
            public HashSet<CommandType> OnlyTypes;
            /// <summary>
            /// 1ターンあたりに調べる盤面状態の上限（0 なら無制限）。超えたら、有望な状態（敵の残り HP 合計が少ない・自分の HP が多い・敵に近い）
            /// だけを残して続ける（ビーム探索）。このとき「最短」は保証されない（近似）。
            /// </summary>
            public int MaxStates;
        }

        public class Result
        {
            public int Turns;
            public int PlayerHp;
            public List<List<CommandRecord>> Path = new List<List<CommandRecord>>();
            public List<string> TraceLines = new List<string>();
        }

        /// <summary>最短クリアを探す（同じターン数なら残り HP が多い手順）。見つからなければ null。</summary>
        public static Result Solve(BattleModel start, Settings s) => Solve(start, s, out _);

        /// <summary>truncated：状態数の上限を超え、ビーム探索に切り替えた（結果は近似。「見つからない」も確定ではない）。</summary>
        public static Result Solve(BattleModel start, Settings s, out bool truncated)
        {
            truncated = false;
            var sequences = Sequences(s);
            var frontier = new Dictionary<string, (BattleModel model, List<List<CommandRecord>> path)> { [Key(start)] = (start.Clone(), new List<List<CommandRecord>>()) };
            var seen = new HashSet<string>(frontier.Keys);
            for (int turn = 1; turn <= s.MaxTurns; turn++)
            {
                Result best = null;
                var next = new Dictionary<string, (BattleModel, List<List<CommandRecord>>)>();
                foreach (var (model, path) in frontier.Values)
                {
                    foreach (var seq in sequences)
                    {
                        var m = model.Clone();
                        var events = s.ForbidPlayerDirectDamage ? new List<BattleEvent>() : null;
                        var outcome = TurnRules.PlayTurn(m, seq, s.Innate, events);
                        if (outcome == TurnOutcome.GameOver || m.Player.Hp < s.MinPlayerHp) continue;
                        if (s.ForbidPlayerDirectDamage && events.Any(e => e.Type == BattleEventType.Damaged && e.Cause.Source.IsPlayer
                                                                         && e.Cause.Kind == DamageKind.Direct && !e.Unit.IsPlayer)) continue;
                        var newPath = new List<List<CommandRecord>>(path) { seq };
                        if (outcome == TurnOutcome.Cleared)
                        {
                            if (best == null || m.Player.Hp > best.PlayerHp) best = new Result { Turns = turn, PlayerHp = m.Player.Hp, Path = newPath };
                            continue;
                        }
                        string key = Key(m);
                        if (seen.Add(key)) next[key] = (m, newPath);
                    }
                }
                if (best != null) { best.TraceLines = Trace(start, best.Path, s.Innate); return best; }
                frontier = next;
                if (frontier.Count == 0) break;
                if (s.MaxStates > 0 && frontier.Count > s.MaxStates)
                {
                    truncated = true;
                    frontier = frontier.OrderBy(kv => Promise(kv.Value.Item1)).Take(s.MaxStates).ToDictionary(kv => kv.Key, kv => kv.Value);
                }
            }
            return null;
        }

        /// <summary>
        /// 1ターン目に取りうる全手順のうち、プレイヤーが被弾する／敵同士で当たる／敵がトゲを踏む割合（脅威と同士討ちの起きやすさの目安）。
        /// </summary>
        public static string FirstTurnProfile(BattleModel start, Settings s)
        {
            int total = 0, playerHit = 0, friendlyFire = 0, enemySpike = 0;
            foreach (var seq in Sequences(s))
            {
                var m = start.Clone();
                var events = new List<BattleEvent>();
                TurnRules.PlayTurn(m, seq, s.Innate, events);
                total++;
                var dmg = events.Where(e => e.Type == BattleEventType.Damaged).ToList();
                if (dmg.Any(e => e.Unit.IsPlayer)) playerHit++;
                if (dmg.Any(e => !e.Unit.IsPlayer && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit && e.Cause.Kind == DamageKind.Direct)) friendlyFire++;
                if (dmg.Any(e => !e.Unit.IsPlayer && e.Cause.Kind == DamageKind.Spike)) enemySpike++;
            }
            return $"1ターン目の手順 {total} 通り：プレイヤー被弾 {Pct(playerHit, total)} ／ 敵同士で命中 {Pct(friendlyFire, total)} ／ 敵がトゲを踏む {Pct(enemySpike, total)}";
        }

        static string Pct(int a, int b) => b == 0 ? "-" : $"{a}（{100f * a / b:0}%）";

        /// <summary>固定の手札で作れる命令列（1〜MaxSlots 個、同じカードは1回まで、方向・固定距離つき）。</summary>
        public static List<List<CommandRecord>> Sequences(Settings s)
        {
            var options = new List<List<CommandRecord>>(); // カードごとの選択肢
            foreach (var c in s.Cards)
            {
                if (s.OnlyTypes != null && !s.OnlyTypes.Contains(c.type)) continue;
                var list = new List<CommandRecord>();
                if (!c.UsesDirection) list.Add(new CommandRecord(c, Direction.Up, 0));
                else
                    foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                        for (int dist = c.minDistance; dist <= c.maxDistance; dist++)
                            list.Add(new CommandRecord(c, d, dist));
                options.Add(list);
            }
            var result = new List<List<CommandRecord>>();
            void Rec(List<CommandRecord> current, HashSet<int> used)
            {
                if (current.Count > 0) result.Add(new List<CommandRecord>(current));
                if (current.Count == s.MaxSlots) return;
                for (int i = 0; i < options.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    used.Add(i);
                    foreach (var r in options[i]) { current.Add(r); Rec(current, used); current.RemoveAt(current.Count - 1); }
                    used.Remove(i);
                }
            }
            Rec(new List<CommandRecord>(), new HashSet<int>());
            return result;
        }

        /// <summary>手順を1ターンずつ再生して、位置と HP を文字にする。</summary>
        public static List<string> Trace(BattleModel start, List<List<CommandRecord>> path, CommandData innate)
        {
            var lines = new List<string>();
            var m = start.Clone();
            for (int t = 0; t < path.Count; t++)
            {
                var outcome = TurnRules.PlayTurn(m, path[t], innate);
                var sb = new StringBuilder($"T{t + 1} {string.Join(" / ", path[t].Select(r => r.ToDisplayText()))} →");
                foreach (var u in m.Units) sb.Append($" {u.Name}{u.Position} HP{u.Hp}");
                if (outcome != TurnOutcome.Continue) sb.Append($" {outcome}");
                lines.Add(sb.ToString());
                if (outcome != TurnOutcome.Continue) break;
            }
            return lines;
        }

        /// <summary>ビーム探索で残す順番（小さいほど有望）：敵の残り HP 合計 → 自分の HP → 最も近い敵との距離。</summary>
        static double Promise(BattleModel m)
        {
            var alive = m.Enemies.Where(e => e.IsAlive).ToList();
            double distance = alive.Count == 0 ? 0 : alive.Min(e => System.Math.Abs(e.Position.x - m.Player.Position.x) + System.Math.Abs(e.Position.y - m.Player.Position.y));
            return alive.Sum(e => e.Hp) * 100 - m.Player.Hp * 10 + distance;
        }

        /// <summary>盤面の状態（位置・HP・トゲ）を文字にしたもの。同じ状態かどうかの比較に使う。</summary>
        public static string Key(BattleModel m)
        {
            var sb = new StringBuilder();
            foreach (var u in m.Units)
            {
                sb.Append(u.Position.x).Append(',').Append(u.Position.y).Append(':').Append(u.Hp);
                // 【検証中】向きが結果に関係する兵種（弓兵・盾兵・騎士）だけ、向きも状態に含める
                if (u.Kind != EnemyKind.Normal) sb.Append((int)u.Facing);
                foreach (var b in u.BombCells) sb.Append('b').Append(b.x).Append(',').Append(b.y); // 【検証中】砲兵の予告
                if (u.Kind == EnemyKind.KnightV3)
                {
                    sb.Append('c').Append(u.SlamCd).Append(u.BlastCd).Append(u.WaveCd).Append(u.WaveCount);
                    foreach (var c in u.SlamCells) sb.Append('s').Append(c.x).Append(',').Append(c.y);
                }
                sb.Append(';');
            }
            sb.Append(m.Environment.StateKey());
            foreach (var sp in m.Board.Spikes.OrderBy(p => p.x).ThenBy(p => p.y)) sb.Append(sp.x).Append(',').Append(sp.y).Append('^');
            return sb.ToString();
        }
    }
}
