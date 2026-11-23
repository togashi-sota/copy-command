using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static CopyCommand.EditorTools.LargeHandStudy;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【検証中】Combat V2 の解析：敵が強くなったか・盤面を動くようになったか。結果は Logs/combat_v2.txt。
    ///  ボット3種（同じ「各札1ターン1回・最大3命令」の手札で、1ターンずつ貪欲に選ぶ）：
    ///   雑＝敵へのダメージだけを最大化（自分の被弾を気にしない）／中央＝中央3×3から出ない範囲で雑と同じ／慎重＝敵へのダメージ − 被弾×2.5
    ///  各ボットの：勝敗・ターン・被ダメージ（うち兵種の行動・環境）・使ったマス・ラウンド終了時に中央3×3にいた割合・兵種の行動が当たった回数
    ///  ほかに：最短・ノーダメージ攻略があるか・攻略の系統（VariantStudy.Families）。近似。
    /// バッチ：-executeMethod CopyCommand.EditorTools.CombatV2Study.Run
    /// </summary>
    public static class CombatV2Study
    {
        static readonly string[] StageNames = { "StageM4", "StageM10", "StageMB1", "StageV1", "StageV2", "StageV3", "StageV4", "StageV5", "StageV6", "StageVB" };
        static readonly string[] BotCards = { "Walk", "Dash", "Rush", "Slash", "Thrust", "PushSlash", "Sweep", "Spirit", "Push" };
        static readonly string[] SearchCards = { "Walk", "Dash", "Slash", "Thrust", "Sweep", "Push" };

        public static void Run() => RunFor(StageNames);

        public static void RunV() => RunFor(StageNames.Where(n => n.StartsWith("StageV")).ToArray());

        public static void RunBoss() => RunFor(new[] { "StageVB" });

        /// <summary>【検証中】Visual Vertical Slice（VS）の盤面が遊べるか。</summary>
        public static void RunVS() => RunFor(new[] { "StageVS" });

        /// <summary>-studystages V1,V4 のように一部だけ。</summary>
        public static void RunSelected()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-studystages");
            RunFor(i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',').Select(n => "Stage" + n.Trim()).ToArray() : StageNames);
        }

        static void RunFor(string[] names)
        {
            var sb = new StringBuilder("【検証中】Combat V2 の解析（近似）。手札＝一歩・駆け足・突進・斬撃・突き・押し斬り・薙ぎ払い・闘気・押し出し（各1ターン1回）" + "\n");
            sb.AppendLine("被ダメ＝あなたが受けたダメージ（兵種＝剣・槍・矢・爆発、環境＝トゲ・炎）／マス＝あなたが入ったマス／中央＝ラウンド終了時に中央3×3にいた割合\n");
            var botSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = BotCards.Select(Card).ToList(), MaxSlots = 3 });
            var searchSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = SearchCards.Select(Card).ToList(), MaxSlots = 3 });
            foreach (var name in names)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                if (stage == null) continue;
                var start = BattleModel.FromStage(stage);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（あなた HP {start.Player.Hp}・敵 HP 合計 {StagnationRule.EnemyHpTotal(start)}）");
                foreach (var (label, score) in new (string, System.Func<int, int, bool, bool, float>)[]
                {
                    ("雑", (dealt, taken, win, dead) => dealt + (win ? 100 : 0) - taken * 0.01f),
                    ("中央", (dealt, taken, win, dead) => dealt + (win ? 100 : 0) - taken * 0.01f),
                    ("慎重", (dealt, taken, win, dead) => dealt - taken * 2.5f + (win ? 100 : 0) - (dead ? 1000 : 0)),
                })
                    sb.AppendLine($"  ボット「{label}」：{Bot(start, botSeqs, score, label == "中央")}");
                var center = Center(start.Board);
                var best = Search(start, searchSeqs, (m, ev, cells) => true);
                int hp0 = start.Player.Hp;
                var noDamage = Search(start, searchSeqs, (m, ev, cells) => m.Player.Hp >= hp0);
                var centerOnly = Search(start, searchSeqs, (m, ev, cells) => cells.All(center.Contains));
                sb.AppendLine($"  最短 {Show(best)}／ノーダメージ攻略 {Show(noDamage)}／中央3×3だけ {Show(centerOnly)}");
                var fam = VariantStudy.Families(start, searchSeqs);
                var rounded = fam.Keys.Select(k => k.StartsWith("混合") ? "混合" : k == "トゲ" || k == "炎" || k == "矢" ? "環境" : k).Distinct().ToList();
                sb.AppendLine($"  系統 {rounded.Count}（{string.Join("・", rounded)}）：{string.Join("／", fam.OrderBy(k => k.Value.turns).Select(k => $"{k.Key} {k.Value.turns}T HP{k.Value.hp}"))}");
                sb.AppendLine($"  （{watch.Elapsed.TotalMinutes:0.0}分）");
                sb.AppendLine();
                Write(sb, "Logs/combat_v2.txt");
                Debug.Log($"[CombatV2Study] {name} 完了");
            }
        }

        /// <summary>【研究】敵 HP の比較（V 系：10 と 14、ボス：30 と 40）。ボット3種だけ。結果は Logs/combat_v2_hp.txt。</summary>
        public static void RunHpCompare()
        {
            var sb = new StringBuilder("【研究】Combat V2 の敵 HP 比較（ボットだけ・近似）" + "\n\n");
            var botSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = BotCards.Select(Card).ToList(), MaxSlots = 3 });
            foreach (var name in new[] { "StageV1", "StageV2", "StageV3", "StageV4", "StageV5", "StageV6", "StageVB" })
            {
                var src = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
                foreach (int hp in name == "StageVB" ? new[] { 30, 40 } : new[] { 10, 14 })
                {
                    var copy = Object.Instantiate(src);
                    copy.enemyMaxHp = hp;
                    var start = BattleModel.FromStage(copy);
                    sb.AppendLine($"=== {src.name} {src.stageTitle} 敵HP {hp}");
                    sb.AppendLine($"  雑：{Bot(start, botSeqs, (d, t, w, x) => d + (w ? 100 : 0) - t * 0.01f, false)}");
                    sb.AppendLine($"  中央：{Bot(start, botSeqs, (d, t, w, x) => d + (w ? 100 : 0) - t * 0.01f, true)}");
                    sb.AppendLine($"  慎重：{Bot(start, botSeqs, (d, t, w, x) => d - t * 2.5f + (w ? 100 : 0) - (x ? 1000 : 0), false)}");
                    Object.DestroyImmediate(copy);
                    Write(sb, "Logs/combat_v2_hp.txt");
                }
            }
        }

        /// <summary>1ターンずつ score が最大の手順を選ぶボット。centerOnly なら、中央3×3から出ない手順だけ（無ければ全部）。</summary>
        static string Bot(BattleModel start, List<List<CommandRecord>> seqs, System.Func<int, int, bool, bool, float> score, bool centerOnly)
        {
            var m = start.Clone();
            var center = Center(m.Board);
            var cells = new HashSet<Vector2Int> { m.Player.Position };
            int hp0 = m.Player.Hp, traitTaken = 0, envTaken = 0, traitHits = 0, centerEnds = 0, turns = 0;
            var outcome = TurnOutcome.Continue;
            for (int t = 1; t <= 12 && outcome == TurnOutcome.Continue; t++)
            {
                List<CommandRecord> pick = null;
                float bestScore = float.MinValue;
                bool anyCenter = false;
                foreach (bool restrict in centerOnly ? new[] { true, false } : new[] { false })
                {
                    foreach (var seq in seqs)
                    {
                        var sim = m.Clone();
                        var ev = new List<BattleEvent>();
                        int php = sim.Player.Hp, ehp = StagnationRule.EnemyHpTotal(sim);
                        var o = TurnRules.PlayTurn(sim, seq, null, ev);
                        if (restrict)
                        {
                            var moved = ev.Where(e => e.Type == BattleEventType.Moved && e.Unit != null && e.Unit.IsPlayer).Select(e => e.To);
                            if (!moved.All(center.Contains) || !center.Contains(m.Player.Position)) continue;
                            anyCenter = true;
                        }
                        float sc = score(ehp - StagnationRule.EnemyHpTotal(sim), php - sim.Player.Hp, o == TurnOutcome.Cleared, o == TurnOutcome.GameOver);
                        if (sc > bestScore) { bestScore = sc; pick = seq; }
                    }
                    if (anyCenter) break;
                }
                var events = new List<BattleEvent>();
                outcome = TurnRules.PlayTurn(m, pick, null, events);
                turns = t;
                foreach (var e in events)
                {
                    if (e.Type == BattleEventType.Moved && e.Unit != null && e.Unit.IsPlayer) cells.Add(e.To);
                    if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null)
                    {
                        if (e.Cause.Kind == DamageKind.Trait) { traitTaken += e.Amount; traitHits++; }
                        if (e.Cause.Kind == DamageKind.Spike || e.Cause.Kind == DamageKind.Fire) envTaken += e.Amount;
                    }
                }
                if (center.Contains(m.Player.Position)) centerEnds++;
            }
            string result = outcome == TurnOutcome.Cleared ? $"{turns}T で勝利" : outcome == TurnOutcome.GameOver ? $"{turns}T で敗北" : "12T で終わらず";
            int floor = 0;
            for (int x = 0; x < m.Board.Width; x++) for (int y = 0; y < m.Board.Height; y++) if (!m.Board.IsWall(new Vector2Int(x, y))) floor++;
            return $"{result}・被ダメ {hp0 - Mathf.Max(0, m.Player.Hp)}（兵種 {traitTaken}・環境 {envTaken}・兵種の命中 {traitHits} 回）・マス {cells.Count}/{floor}・中央 {centerEnds}/{turns}";
        }
    }
}
