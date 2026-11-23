using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static CopyCommand.EditorTools.LargeHandStudy;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究】騎士V3（VB）の数値案の比較（2026-10-04 本人依頼）。ゲームの挙動は変えない（PhaseRules.ExperimentBossV3 を解析中だけ入れる）。
    /// 案：今の仕組み（段階で新しい攻撃が増える）／最初から3つのカウントが見える（剣3 爆4 炎6）／それに「段階で既存の攻撃が変わる」を足したもの。
    /// ボット：全札（毎ターン9種を1回ずつ使える＝実際より強い）の雑・慎重、手札（中:7/9、VB の検証デッキから引く・種4つ）の雑・慎重。
    /// 見るもの：勝敗・ターン・被ダメ（大剣など兵種／反射／トゲ）・各攻撃の発動回数・段階に入ったターン・段階の飛ばし（1ラウンドで2段階進む）。
    /// 結果：Logs/boss_v3_study.txt。バッチ：-executeMethod CopyCommand.EditorTools.BossV3Study.Run
    /// </summary>
    public static class BossV3Study
    {
        static readonly string[] AllCards = { "Walk", "Dash", "Rush", "Slash", "Thrust", "PushSlash", "Sweep", "Spirit", "Push" };
        const int MaxTurns = 16;
        static readonly int[] Seeds = { 1, 2, 3, 4 };

        static List<PhaseRules.BossV3Variant> Variants()
        {
            PhaseRules.BossV3Variant Current(int hp) => new PhaseRules.BossV3Variant { Name = $"旧仕様 HP{hp}（段階で爆発・炎が増える）", Hp = hp };
            PhaseRules.BossV3Variant Visible(int hp) => new PhaseRules.BossV3Variant
            {
                Name = $"A：最初から全部見える HP{hp}（剣3 爆4 炎6・段階の変化なし）", Hp = hp, AllFromStart = true,
            };
            PhaseRules.BossV3Variant Changing(int hp) => new PhaseRules.BossV3Variant
            {
                Name = $"B：最初から全部見える＋段階で変わる HP{hp}（60%以下：剣の周期3→2／30%以下：爆発が大十字・炎の周期4→3）", Hp = hp, AllFromStart = true,
                SlamPeriod = new[] { 3, 2, 2 }, WavePeriod = new[] { 4, 4, 3 }, BigBlast = new[] { false, false, true },
            };
            return new List<PhaseRules.BossV3Variant> { Current(40), Current(60), Visible(55), Visible(60), Changing(55), Changing(60) };
        }

        [MenuItem("CopyCommand/解析：ボスV3 の数値案")]
        public static void Run()
        {
            var stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageVB.asset");
            var sb = new StringBuilder("【研究】騎士V3（VB）の数値案の比較（近似。ゲームの挙動は変えていない）\n");
            sb.AppendLine("ボット：全札＝毎ターン9種を1回ずつ使える（実際より強い）／手札＝中:7/9 で VB の検証デッキから引く（種 " + string.Join(",", Seeds) + " の平均と内訳）");
            sb.AppendLine("雑＝敵へのダメージ最大（被弾を気にしない）／慎重＝敵へのダメージ − 被弾×2.5。最大 " + MaxTurns + " ターン。");
            sb.AppendLine("被ダメ：兵種＝大剣・爆発・炎の波／反射＝騎士の反転コピーの攻撃／環境＝トゲ。発動＝剣/爆/炎 の回数。段階＝60%以下・30%以下に入ったターン。飛ばし＝1ラウンドで2段階進んだ・または30%以下の段階を見ずに倒れた回数\n");
            var allSeqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = AllCards.Select(Card).ToList(), MaxSlots = 3 });
            var deck = stage.handTestDeckLarge.ToList();
            try
            {
                foreach (var v in Variants())
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    PhaseRules.ExperimentBossV3 = v;
                    sb.AppendLine("=== " + v.Name);
                    sb.AppendLine("  全札・雑：" + Show(Play(stage, v, t => allSeqs, Careless)));
                    sb.AppendLine("  全札・慎重：" + Show(Play(stage, v, t => allSeqs, Careful)));
                    foreach (var (label, score) in new (string, System.Func<int, int, bool, bool, float>)[] { ("手札・雑", Careless), ("手札・慎重", Careful) })
                    {
                        var results = Seeds.Select(seed => PlayHand(stage, v, deck, seed, score)).ToList();
                        sb.AppendLine($"  {label}：勝ち {results.Count(r => r.Win)}/{results.Count}・平均 {results.Average(r => r.Turns):0.0}T・平均被ダメ {results.Average(r => r.Taken):0.0}・30%以下に届いた {results.Count(r => r.Phase2Turn > 0)}/{results.Count}・飛ばし {results.Sum(r => r.Skips)}");
                        foreach (var r in results) sb.AppendLine("      " + Show(r));
                    }
                    sb.AppendLine($"  （{watch.Elapsed.TotalMinutes:0.0}分）\n");
                    Write(sb, "Logs/boss_v3_study.txt");
                    Debug.Log("[BossV3Study] " + v.Name + " 完了");
                }
            }
            finally
            {
                PhaseRules.ExperimentBossV3 = null;
            }
        }

        static float Careless(int dealt, int taken, bool win, bool dead) => dealt + (win ? 100 : 0) - taken * 0.01f;
        static float Careful(int dealt, int taken, bool win, bool dead) => dealt - taken * 2.5f + (win ? 100 : 0) - (dead ? 1000 : 0);

        class Result
        {
            public bool Win, Dead;
            public int Turns, Taken, TraitTaken, ReflectTaken, EnvTaken, Slams, Blasts, Waves, Phase1Turn, Phase2Turn, Skips, BossHpLeft, FirstHitTurn;
        }

        static string Show(Result r) =>
            $"{(r.Win ? "勝利" : r.Dead ? "敗北" : "時間切れ")} {r.Turns}T・被ダメ {r.Taken}（兵種 {r.TraitTaken}・反射 {r.ReflectTaken}・環境 {r.EnvTaken}）・発動 剣{r.Slams}/爆{r.Blasts}/炎{r.Waves}" +
            $"・段階 60%:{(r.Phase1Turn > 0 ? r.Phase1Turn + "T" : "—")} 30%:{(r.Phase2Turn > 0 ? r.Phase2Turn + "T" : "—")}・飛ばし {r.Skips}・初被弾 {(r.FirstHitTurn > 0 ? r.FirstHitTurn + "T" : "—")}" +
            (r.Win ? "" : $"・騎士の残り HP {r.BossHpLeft}");

        static BattleModel Start(StageData stage, PhaseRules.BossV3Variant v)
        {
            var copy = Object.Instantiate(stage);
            copy.enemyMaxHp = v.Hp;
            var m = BattleModel.FromStage(copy);
            Object.DestroyImmediate(copy);
            foreach (var e in m.Enemies.Where(e => e.Kind == EnemyKind.KnightV3))
            {
                e.SlamCd = v.AllFromStart ? v.SlamStart : v.SlamPeriod[0];
                e.BlastCd = v.AllFromStart ? v.BlastStart : 0;
                e.WaveCd = v.AllFromStart ? v.WaveStart : 0;
            }
            return m;
        }

        /// <summary>手札ボット：中:7/9（初手7・毎ターン3枚・上限9。使った札は捨て札、山札が切れたら混ぜ直す）。</summary>
        static Result PlayHand(StageData stage, PhaseRules.BossV3Variant v, List<CommandData> deck, int seed, System.Func<int, int, bool, bool, float> score)
        {
            var rng = new System.Random(seed);
            var draw = deck.OrderBy(_ => rng.Next()).ToList();
            var discard = new List<CommandData>();
            var hand = new List<CommandData>();
            void Draw(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    if (draw.Count == 0)
                    {
                        draw = discard.OrderBy(_ => rng.Next()).ToList();
                        discard.Clear();
                        if (draw.Count == 0) return;
                    }
                    hand.Add(draw[0]);
                    draw.RemoveAt(0);
                }
            }
            Draw(7);
            return Play(stage, v, t =>
            {
                if (t > 1) Draw(Mathf.Min(3, 9 - hand.Count));
                return BattleSolver.Sequences(new BattleSolver.Settings { Cards = hand.ToList(), MaxSlots = 3 });
            }, score, used =>
            {
                foreach (var r in used)
                {
                    int i = hand.IndexOf(r.Command);
                    if (i < 0) continue;
                    discard.Add(hand[i]);
                    hand.RemoveAt(i);
                }
            });
        }

        static Result Play(StageData stage, PhaseRules.BossV3Variant v, System.Func<int, List<List<CommandRecord>>> seqsFor,
            System.Func<int, int, bool, bool, float> score, System.Action<List<CommandRecord>> onPlayed = null)
        {
            var m = Start(stage, v);
            var boss = m.Enemies.First(e => e.Kind == EnemyKind.KnightV3);
            var r = new Result();
            int hp0 = m.Player.Hp;
            var outcome = TurnOutcome.Continue;
            for (int t = 1; t <= MaxTurns && outcome == TurnOutcome.Continue; t++)
            {
                List<CommandRecord> pick = null;
                float best = float.MinValue;
                foreach (var seq in seqsFor(t))
                {
                    var sim = m.Clone();
                    int php = sim.Player.Hp, ehp = StagnationRule.EnemyHpTotal(sim);
                    var o = TurnRules.PlayTurn(sim, seq, null, new List<BattleEvent>());
                    float sc = score(ehp - StagnationRule.EnemyHpTotal(sim), php - sim.Player.Hp, o == TurnOutcome.Cleared, o == TurnOutcome.GameOver);
                    if (sc > best) { best = sc; pick = seq; }
                }
                if (pick == null) pick = new List<CommandRecord>();
                int slam = boss.SlamCd, blast = boss.BlastCd, wave = boss.WaveCd, phase = PhaseRules.BossPhase(boss);
                var events = new List<BattleEvent>();
                outcome = TurnRules.PlayTurn(m, pick, null, events);
                onPlayed?.Invoke(pick);
                r.Turns = t;
                foreach (var e in events)
                {
                    if (e.Type != BattleEventType.Damaged || e.Unit == null || !e.Unit.IsPlayer || e.Cause == null) continue;
                    if (r.FirstHitTurn == 0) r.FirstHitTurn = t;
                    if (e.Cause.Kind == DamageKind.Trait) r.TraitTaken += e.Amount;
                    else if (e.Cause.Kind == DamageKind.Spike || e.Cause.Kind == DamageKind.Fire) r.EnvTaken += e.Amount;
                    else if (e.Cause.Source == boss) r.ReflectTaken += e.Amount;
                }
                if (boss.IsAlive)
                {
                    if (slam == 1) r.Slams++;
                    if (blast == 1) r.Blasts++;
                    if (wave == 1) r.Waves++;
                }
                if (boss.IsAlive)
                {
                    int now = PhaseRules.BossPhase(boss);
                    if (now >= 1 && r.Phase1Turn == 0) r.Phase1Turn = t;
                    if (now >= 2 && r.Phase2Turn == 0) r.Phase2Turn = t;
                    if (now - phase >= 2) r.Skips++;
                }
                else if (phase < 2) r.Skips++; // 30% 以下の段階を見ないまま倒れた（段階を飛ばして終わった）
            }
            r.Win = outcome == TurnOutcome.Cleared;
            r.Dead = outcome == TurnOutcome.GameOver;
            r.Taken = hp0 - m.Player.Hp;
            r.BossHpLeft = boss.Hp;
            return r;
        }
    }
}
