using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static CopyCommand.EditorTools.LargeHandStudy;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究】ACT1 を手札ボットで通して遊ばせる（2026-10-04 夜）。HP・デッキ・刻印を持ち越し、扉はランダム、報酬は「写し取り」優先。
    /// 見るもの：クリア率・どこで倒れるか・1戦のターン数（人が遊ぶ時間の目安）・HP の推移。近似：
    ///   ・ボットは1ターン先しか読まない。命令は3つまで（第四命令を取っても3つ＝計算量のため）。先読み・再考の効果は使わない。
    ///   ・慎重＝敵へのダメージ − 被弾×2.5／雑＝敵へのダメージだけ。
    /// 結果：Logs/act1_study.txt。バッチ：-executeMethod CopyCommand.EditorTools.Act1Study.Run
    /// </summary>
    public static class Act1Study
    {
        const int Seeds = 6;

        [MenuItem("CopyCommand/解析：ACT1 を通して遊ぶ")]
        public static void Run()
        {
            var sb = new StringBuilder("【研究】ACT1 をボットで通す（近似。ボットは1ターン先読み・命令3つまで）\n\n");
            foreach (var (label, score) in new (string, System.Func<int, int, bool, bool, float>)[]
            {
                ("慎重", (d, t, w, x) => d - t * 2.5f + (w ? 100 : 0) - (x ? 1000 : 0)),
                ("雑", (d, t, w, x) => d + (w ? 100 : 0) - t * 0.01f),
            })
            {
                var results = new List<(bool clear, int floor, int turns, List<string> log)>();
                var turnsByKind = new Dictionary<RoomKind, List<int>>();
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    var r = PlayRun(seed, score, turnsByKind);
                    results.Add(r);
                }
                sb.AppendLine($"=== ボット「{label}」：クリア {results.Count(r => r.clear)}/{Seeds}　平均到達 {results.Average(r => r.floor):0.0}階　平均合計ターン {results.Average(r => r.turns):0.0}");
                foreach (var kv in turnsByKind.OrderBy(k => k.Key))
                    sb.AppendLine($"  {kv.Key}：平均 {kv.Value.Average():0.0} ターン（{kv.Value.Count} 戦）　→ 人なら約 {kv.Value.Average() * 0.6:0.0}〜{kv.Value.Average() * 1.0:0.0} 分");
                foreach (var r in results)
                {
                    sb.AppendLine($"  ・{(r.clear ? "CLEAR" : "敗北")}（{r.floor}階・{r.turns}T）");
                    foreach (var line in r.log) sb.AppendLine("      " + line);
                }
                sb.AppendLine();
                Write(sb, "Logs/act1_study.txt");
                Debug.Log("[Act1Study] " + label + " 完了");
            }
        }

        static (bool, int, int, List<string>) PlayRun(int seed, System.Func<int, int, bool, bool, float> score, Dictionary<RoomKind, List<int>> turnsByKind)
        {
            var run = new ActRun(ActContent.Roles[0], seed, Card);
            var rng = new System.Random(seed * 31);
            var log = new List<string>();
            int guard = 0;
            while (!run.Cleared && !run.Failed && run.Floor < run.FloorCount && guard++ < 20)
            {
                var doors = run.Doors;
                var room = doors[rng.Next(doors.Count)];
                run.Enter(room);
                switch (room.Kind)
                {
                    case RoomKind.Battle:
                    case RoomKind.Danger:
                    case RoomKind.Boss:
                        int hpBefore = run.Hp;
                        var stats = Battle(run, room, score, rng);
                        if (!turnsByKind.TryGetValue(room.Kind, out var list)) turnsByKind[room.Kind] = list = new List<int>();
                        list.Add(stats.Turns);
                        log.Add($"{run.Floor + 1}階 {room.Name}：{(stats.Won ? "勝ち" : "負け")} {stats.Turns}T　HP {hpBefore}→{stats.HpAfter}");
                        if (!stats.Won) { run.LoseBattle(stats); break; }
                        run.WinBattle(stats);
                        if (run.Cleared) break;
                        var copy = run.RewardCards.FirstOrDefault(c => c.IsCopySlot) ?? run.RewardCards.FirstOrDefault();
                        if (copy != null) run.TakeRewardCard(copy);
                        if (run.RewardPicksLeft > 0 && run.RewardCards.Count > 0) run.TakeRewardCard(run.RewardCards[0]);
                        if (run.RewardImprints.Count > 0) run.TakeRewardImprint(run.RewardImprints[0]);
                        run.Leave();
                        break;
                    case RoomKind.Rest:
                        if (run.Hp < run.MaxHp * 0.75f) run.RestHeal();
                        else { var w = run.Deck.FirstOrDefault(c => c.name == "Walk" || c.name == "Dash"); if (w != null) run.RestRemove(w); else run.RestHeal(); }
                        log.Add($"{run.Floor + 1}階 休息：HP {run.Hp}");
                        run.Leave();
                        break;
                    case RoomKind.Shop:
                        var buy = run.ShopCards.Where(c => !c.Sold && c.Price <= run.Shards).OrderByDescending(c => c.Card.damage).FirstOrDefault();
                        if (buy != null) run.BuyCard(buy);
                        log.Add($"{run.Floor + 1}階 商人：ルーン片 {run.Shards}");
                        run.Leave();
                        break;
                    default:
                        if (room.Id == "event_trade") run.AltarShards();
                        run.Leave();
                        break;
                }
            }
            return (run.Cleared, Mathf.Min(run.Floor + 1, run.FloorCount), run.TotalTurns, log);
        }

        /// <summary>1戦：7枚補充の手札（基本の枚数まで補充・使った札は捨て札）で、毎ターン score が最大の手順を選ぶ。</summary>
        static BattleStats Battle(ActRun run, RoomDef room, System.Func<int, int, bool, bool, float> score, System.Random rng)
        {
            var src = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{room.Stage}.asset");
            var stage = Object.Instantiate(src);
            stage.playerMaxHp = run.MaxHp;
            stage.playerStartHp = run.Hp;
            if (room.EnemyHp > 0) stage.enemyMaxHp = room.EnemyHp;
            var m = BattleModel.FromStage(stage);
            Object.DestroyImmediate(stage);
            m.SpikeFirstBonus = run.SpikeFirstBonus;
            var draw = run.Deck.OrderBy(_ => rng.Next()).ToList();
            var discard = new List<CommandData>();
            var hand = new List<CommandData>();
            void Refill()
            {
                while (hand.Count < run.BaseHand)
                {
                    if (draw.Count == 0) { draw = discard.OrderBy(_ => rng.Next()).ToList(); discard.Clear(); if (draw.Count == 0) return; }
                    hand.Add(draw[0]);
                    draw.RemoveAt(0);
                }
            }
            Refill();
            int hp0 = m.Player.Hp;
            var events = new List<BattleEvent>();
            var outcome = TurnOutcome.Continue;
            int t = 0;
            while (outcome == TurnOutcome.Continue && t < 20)
            {
                t++;
                var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = hand.Distinct().ToList(), MaxSlots = 3 });
                List<CommandRecord> pick = null;
                float best = float.MinValue;
                foreach (var seq in seqs)
                {
                    var sim = m.Clone();
                    int php = sim.Player.Hp, ehp = StagnationRule.EnemyHpTotal(sim);
                    var o = TurnRules.PlayTurn(sim, seq, null);
                    float sc = score(ehp - StagnationRule.EnemyHpTotal(sim), php - sim.Player.Hp, o == TurnOutcome.Cleared, o == TurnOutcome.GameOver);
                    if (sc > best) { best = sc; pick = seq; }
                }
                pick = pick ?? new List<CommandRecord>();
                outcome = TurnRules.PlayTurn(m, pick, null, events);
                foreach (var r in pick) { int i = hand.IndexOf(r.Command); if (i >= 0) { discard.Add(hand[i]); hand.RemoveAt(i); } }
                Refill();
            }
            return BattleStats.From(events, outcome == TurnOutcome.Cleared, t, hp0, Mathf.Max(0, m.Player.Hp));
        }
    }
}
