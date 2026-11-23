using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 手札方式 A（毎ターン2枚）と B（3枚未満なら3枚まで＋1枚）の比較シミュレーション（解析専用。ゲーム本体は変えない）。
    /// 同じデッキ・同じシャッフルの種（seed）で A と B を並べて、Logs/hand_ab_report.txt に書き出す。
    ///  第1部：カードの流れだけ（使う枚数の方針ごとに、10ターンの手札枚数・3命令を組める割合・上限到達など）
    ///  第2部：実際の盤面を、1ターン先読みの簡易ボット（手札から最善と思う命令列を選ぶ）で A と B それぞれ遊ばせる
    /// バッチ実行：-executeMethod CopyCommand.EditorTools.HandPolicySimulator.Run
    /// </summary>
    public static class HandPolicySimulator
    {
        const string CommandFolder = "Assets/_Project/Data/Commands";
        const int Turns = 10;
        const int FlowSeeds = 10000;
        const int BoardSeeds = 60;

        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"{CommandFolder}/{name}.asset");

        [MenuItem("CopyCommand/解析：手札方式A/B比較")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("手札方式 A/B 比較（HandPolicySimulator）");
            sb.AppendLine("A：2ターン目以降、毎ターン2枚引く ／ B：手札3枚未満なら3枚まで補充＋1枚、3枚以上なら1枚 ／ 共通：初手5枚・上限7（上限のときは引かない）・未使用は手札に残る");
            sb.AppendLine();

            var m1 = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageM1.asset");
            var decks = new (string name, List<CommandData> cards, int opening, bool keepOrder, bool guarantee)[]
            {
                ("8枚（前回の M1〜M3：一歩・駆け足・斬撃・押し出し 各2、全部シャッフル）", Repeat(("Walk", 2), ("Dash", 2), ("Slash", 2), ("Push", 2)), 0, false, false),
                ("10枚（斬撃3・押し出し2・一歩3・駆け足2、全部シャッフル）", Repeat(("Walk", 3), ("Dash", 2), ("Slash", 3), ("Push", 2)), 0, false, false),
                ("10枚・初手5枚と山札の順番を固定（本人案のみ）", m1.GetHandTestDeck(), 5, true, false),
                ("【今回の検証用】10枚・初手と山札の順番を固定＋攻撃札の保証（M1〜M3）", m1.GetHandTestDeck(), m1.handTestOpeningCount, m1.handTestKeepOrder, m1.handTestGuaranteeAttack),
                ("10枚・初手5枚だけ固定、残りはシャッフル（別案）", m1.GetHandTestDeck(), 5, false, false),
            };

            sb.AppendLine("■ 第1部：カードの流れ（盤面なし、" + FlowSeeds + " 回の平均）");
            sb.AppendLine("  使い方の方針：全力＝毎ターン手札から最大3枚使う／2枚ずつ＝最大2枚／1枚ずつ＝1枚だけ（温存）／ランダム＝1〜3枚を毎ターンランダム");
            foreach (var deck in decks)
            {
                sb.AppendLine();
                sb.AppendLine("● デッキ " + deck.name);
                foreach (var usage in new[] { 3, 2, 1, 0 })
                    FlowReport(sb, deck.cards, usage, deck.opening, deck.keepOrder, deck.guarantee);
            }

            sb.AppendLine();
            sb.AppendLine("■ 第2部：同じ盤面を簡易ボットで遊ばせた比較（" + BoardSeeds + " 回、A と B は同じ seed）");
            sb.AppendLine("  ボット：毎ターン、手札で作れる命令列（1〜3枚・全方向）を全部試し、1ターン後の盤面を点数化して最善を選ぶ（敵へのダメージ＋、自分の被弾−、敵との距離−、使ったカード1枚ごとにわずかに−＝必要なければ温存）。");
            sb.AppendLine("  ※ ボットは人間より読みが浅い。差の「傾向」を見るためのもの。");
            var stagePaths = new[] { "Assets/_Project/Data/Stages/Stage3.asset", "Assets/_Project/Data/Stages/StageM1.asset", "Assets/_Project/Data/Stages/StageM2.asset", "Assets/_Project/Data/Stages/StageM3.asset" };
            foreach (var path in stagePaths)
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
                if (stage == null) continue;
                BoardReport(sb, stage);
                // M1〜M3：敵 HP を上げた場合（戦闘が長くなると A/B の差が出るか）。解析の中だけで HP を変える（データは変えない）
                if (stage.isTestStage)
                    foreach (int hp in new[] { 12, 16 })
                    {
                        var copy = Object.Instantiate(stage);
                        copy.name = stage.name;
                        copy.stageTitle = stage.stageTitle + $"（解析だけ：敵 HP {hp}）";
                        copy.enemyMaxHp = hp;
                        BoardReport(sb, copy);
                        Object.DestroyImmediate(copy);
                    }
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/hand_ab_report.txt", sb.ToString(), Encoding.UTF8);
            Debug.Log("[HandPolicySimulator] Logs/hand_ab_report.txt に書き出しました");
        }

        static List<CommandData> Repeat(params (string name, int count)[] items)
        {
            var list = new List<CommandData>();
            foreach (var (name, count) in items) for (int i = 0; i < count; i++) list.Add(Card(name));
            return list;
        }

        // ───────── 第1部：カードの流れ ─────────

        static void FlowReport(StringBuilder sb, List<CommandData> deck, int usage, int opening, bool keepOrder, bool guarantee)
        {
            string usageName = usage == 3 ? "全力（最大3枚）" : usage == 2 ? "2枚ずつ" : usage == 1 ? "1枚ずつ（温存）" : "ランダム1〜3枚";
            sb.AppendLine($"  ○ 使い方：{usageName}");
            var stats = new Dictionary<DrawPolicy, FlowStats>();
            foreach (var policy in new[] { DrawPolicy.DrawTwo, DrawPolicy.FloorThreePlusOne })
            {
                var st = new FlowStats();
                for (int seed = 0; seed < FlowSeeds; seed++)
                {
                    var deckState = new DeckState(new System.Random(seed), policy: policy);
                    deckState.StartBattle(deck, opening, keepOrder);
                    var usageRng = new System.Random(seed * 7919 + 1);
                    var pickRng = new System.Random(seed * 104729 + 3);
                    for (int t = 0; t < Turns; t++)
                    {
                        if (t > 0)
                        {
                            deckState.StartTurnDraw();
                            st.Skipped[t] += deckState.LastSkippedDraws;
                            if (guarantee) deckState.EnsureAttackInHand();
                        }
                        var hand = deckState.Hand;
                        st.HandSize[t] += hand.Count;
                        if (hand.Count >= 2) st.Two[t]++;
                        if (hand.Count >= 3) st.Three[t]++;
                        if (hand.Count >= deckState.HandLimit) st.AtLimit[t]++;
                        bool move = hand.Any(c => c.Data.type == CommandType.Walk || c.Data.type == CommandType.Rush);
                        bool attack = hand.Any(c => c.Data.damage > 0);
                        if (move && attack) st.MoveAndAttack[t]++;
                        if (attack) st.Attack[t]++;
                        int want = usage == 0 ? usageRng.Next(1, 4) : usage;
                        int use = Mathf.Min(want, hand.Count);
                        st.Used[t] += use;
                        var chosen = hand.OrderBy(_ => pickRng.Next()).Take(use).ToList();
                        deckState.UseCards(chosen);
                        deckState.EndTurn(new CardInstance[0]);
                    }
                }
                stats[policy] = st;
            }

            var a = stats[DrawPolicy.DrawTwo];
            var b = stats[DrawPolicy.FloorThreePlusOne];
            sb.AppendLine("     ターン            : " + string.Join(" ", Enumerable.Range(1, Turns).Select(t => $"{t,5}")));
            Row(sb, "A 手札（平均）", a.HandSize, v => $"{v / FlowSeeds,5:0.0}");
            Row(sb, "B 手札（平均）", b.HandSize, v => $"{v / FlowSeeds,5:0.0}");
            Row(sb, "A 3枚以上ある%", a.Three, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "B 3枚以上ある%", b.Three, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "A 2枚未満%    ", a.Two, v => $"{100 - 100.0 * v / FlowSeeds,5:0}");
            Row(sb, "B 2枚未満%    ", b.Two, v => $"{100 - 100.0 * v / FlowSeeds,5:0}");
            Row(sb, "A 上限7%      ", a.AtLimit, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "B 上限7%      ", b.AtLimit, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "A 攻撃札あり% ", a.Attack, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "B 攻撃札あり% ", b.Attack, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "A 移動+攻撃%  ", a.MoveAndAttack, v => $"{100.0 * v / FlowSeeds,5:0}");
            Row(sb, "B 移動+攻撃%  ", b.MoveAndAttack, v => $"{100.0 * v / FlowSeeds,5:0}");
            sb.AppendLine($"     2〜10ターン目で「攻撃札（斬撃など）が手札に1枚もない」割合：A {100 - 100.0 * a.Attack.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}% ／ B {100 - 100.0 * b.Attack.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}%");
            sb.AppendLine($"     10ターンの合計使用枚数（平均）：A {a.Used.Sum() / FlowSeeds:0.0} 枚 ／ B {b.Used.Sum() / FlowSeeds:0.0} 枚");
            sb.AppendLine($"     上限で引けなかった枚数（10ターン合計の平均）：A {a.Skipped.Sum() / FlowSeeds:0.00} 枚 ／ B {b.Skipped.Sum() / FlowSeeds:0.00} 枚");
            sb.AppendLine($"     2〜10ターン目で「3命令を組める」割合：A {100.0 * a.Three.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}% ／ B {100.0 * b.Three.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}%");
            sb.AppendLine($"     2〜10ターン目で「2命令以上を組めない」割合：A {100 - 100.0 * a.Two.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}% ／ B {100 - 100.0 * b.Two.Skip(1).Sum() / (FlowSeeds * (Turns - 1)):0}%");
        }

        class FlowStats
        {
            public double[] HandSize = new double[Turns], Two = new double[Turns], Three = new double[Turns], AtLimit = new double[Turns];
            public double[] MoveAndAttack = new double[Turns], Used = new double[Turns], Skipped = new double[Turns], Attack = new double[Turns];
        }

        static void Row(StringBuilder sb, string label, double[] values, System.Func<double, string> format) =>
            sb.AppendLine($"     {label}: " + string.Join(" ", values.Select(format)));

        // ───────── 第2部：同じ盤面で簡易ボット ─────────

        static void BoardReport(StringBuilder sb, StageData stage)
        {
            var deck = stage.GetHandTestDeck();
            sb.AppendLine();
            sb.AppendLine($"● {stage.name}「{stage.stageTitle}」 デッキ {deck.Count} 枚（{string.Join("・", deck.GroupBy(c => c.displayName).Select(g => $"{g.Key}{g.Count()}"))}）・最大{stage.maxSlots}命令");
            foreach (bool curseOn in new[] { false, true })
            foreach (var policy in new[] { DrawPolicy.DrawTwo, DrawPolicy.FloorThreePlusOne })
            {
                if (curseOn && !stage.stagnationAllowed) continue;
                int attackTurns = 0, curseDamage = 0;
                int clears = 0, turnsSum = 0, hpSum = 0, deaths = 0, timeouts = 0, cardsUsed = 0, turnsPlayed = 0, threeCmdTurns = 0, oneCmdTurns = 0, starvedTurns = 0;
                for (int seed = 0; seed < BoardSeeds; seed++)
                {
                    var model = BattleModel.FromStage(stage);
                    var deckState = new DeckState(new System.Random(seed), policy: policy);
                    deckState.StartBattle(deck, stage.handTestOpeningCount, stage.handTestKeepOrder);
                    var gauge = curseOn ? new StagnationRule(stage.stagnationMax, stage.stagnationPenalties) : null;
                    var outcome = TurnOutcome.Continue;
                    int t;
                    for (t = 1; t <= 12 && outcome == TurnOutcome.Continue; t++)
                    {
                        if (t > 1)
                        {
                            deckState.StartTurnDraw();
                            if (stage.handTestGuaranteeAttack) deckState.EnsureAttackInHand();
                        }
                        if (deckState.Hand.Count == 0) { starvedTurns++; continue; }
                        if (deckState.Hand.Any(c => c.Data.damage > 0)) attackTurns++;
                        var plan = BestPlan(model, deckState.Hand, stage.maxSlots, stage.enemyInnateAction, gauge);
                        deckState.UseCards(plan.Select(r => r.SourceCard));
                        deckState.EndTurn(new CardInstance[0]);
                        cardsUsed += plan.Count;
                        turnsPlayed++;
                        if (plan.Count >= 3) threeCmdTurns++;
                        if (plan.Count <= 1) oneCmdTurns++;
                        int hpBefore = StagnationRule.EnemyHpTotal(model);
                        outcome = TurnRules.PlayTurn(model, plan.Select(r => r.Clone()).ToList(), stage.enemyInnateAction);
                        if (gauge != null && outcome == TurnOutcome.Continue)
                        {
                            int penalty = gauge.EndRound(StagnationRule.EnemyHpTotal(model) < hpBefore);
                            if (penalty > 0)
                            {
                                model.Player.TakeDamage(penalty);
                                curseDamage += penalty;
                                if (!model.Player.IsAlive) outcome = TurnOutcome.GameOver;
                            }
                        }
                    }
                    if (outcome == TurnOutcome.Cleared) { clears++; turnsSum += t - 1; hpSum += model.Player.Hp; }
                    else if (outcome == TurnOutcome.GameOver) deaths++;
                    else timeouts++;
                }
                string name = (policy == DrawPolicy.DrawTwo ? "A" : "B") + (curseOn ? "＋鏡蝕" : "　　　");
                sb.AppendLine($"  {name}：クリア {clears}/{BoardSeeds}（平均 {(clears > 0 ? (double)turnsSum / clears : 0):0.0} ターン・残りHP平均 {(clears > 0 ? (double)hpSum / clears : 0):0.0}）／敗北 {deaths}／12ターンで終わらず {timeouts}"
                              + $"／1ターン平均 {(turnsPlayed > 0 ? (double)cardsUsed / turnsPlayed : 0):0.00} 枚使用／3命令ターン {Pct(threeCmdTurns, turnsPlayed)}／1命令以下ターン {Pct(oneCmdTurns, turnsPlayed)}／攻撃札がある手番 {Pct(attackTurns, turnsPlayed + starvedTurns)}" + (curseOn ? $"／呪いのダメージ合計 {curseDamage}（{BoardSeeds}回）" : ""));
            }
        }

        static string Pct(int a, int b) => b == 0 ? "-" : $"{100.0 * a / b:0}%";

        /// <summary>手札で作れる命令列（1〜maxSlots 枚、同じカード実体は1回）を全部試し、1ターン後の点数が最も高いものを返す。</summary>
        static List<CommandRecord> BestPlan(BattleModel model, IReadOnlyList<CardInstance> hand, int maxSlots, CommandData innate, StagnationRule gauge = null)
        {
            // 同じ種類のカードは1枚目だけを代表にして、組み合わせの重複を減らす
            var cards = hand.ToList();
            List<CommandRecord> best = null;
            double bestScore = double.MinValue;
            var current = new List<CommandRecord>();
            var used = new HashSet<CardInstance>();
            void Rec()
            {
                if (current.Count > 0)
                {
                    double score = Score(model, current, innate, gauge);
                    if (score > bestScore) { bestScore = score; best = new List<CommandRecord>(current); }
                }
                if (current.Count == maxSlots) return;
                var triedTypes = new HashSet<CommandData>();
                foreach (var card in cards)
                {
                    if (used.Contains(card) || !triedTypes.Add(card.Data)) continue;
                    used.Add(card);
                    var dirs = card.Data.UsesDirection ? new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right } : new[] { Direction.Up };
                    foreach (var d in dirs)
                    {
                        current.Add(new CommandRecord(card.Data, d, Mathf.Max(0, card.Data.minDistance), card));
                        Rec();
                        current.RemoveAt(current.Count - 1);
                    }
                    used.Remove(card);
                }
            }
            Rec();
            return best;
        }

        static double Score(BattleModel model, List<CommandRecord> plan, CommandData innate, StagnationRule gauge = null)
        {
            var m = model.Clone();
            var outcome = TurnRules.PlayTurn(m, plan.Select(r => r.Clone()).ToList(), innate);
            if (outcome == TurnOutcome.GameOver) return -10000;
            double score = 0;
            if (outcome == TurnOutcome.Cleared) score += 1000 + m.Player.Hp * 10;
            int enemyLost = model.Enemies.Sum(e => e.Hp) - m.Enemies.Sum(e => Mathf.Max(0, e.Hp));
            int playerLost = model.Player.Hp - m.Player.Hp;
            score += enemyLost * 10 - playerLost * 12;
            if (gauge != null && enemyLost <= 0)
                score -= gauge.AtMax ? gauge.NextPenalty * 12 : 1 + gauge.Gauge; // 鏡蝕：満ちていれば次の呪いを被弾として数える
            var alive = m.Enemies.Where(e => e.IsAlive).ToList();
            if (alive.Count > 0)
                score -= 0.5 * alive.Min(e => Mathf.Abs(e.Position.x - m.Player.Position.x) + Mathf.Abs(e.Position.y - m.Player.Position.y));
            score -= 0.3 * plan.Count; // 必要がなければカードを温存する
            return score;
        }
    }
}
