using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中・Copy Core V2】敵のコピーの基本規則（2026-10-05 本人：「コピーを外しても普通の戦術ゲームとして成立してしまう」）。
    /// ACT1 と今までのステージは Current のまま。Copy Core V2 サンドボックスのステージ（StageData.copySandbox）だけで切り替えて比べる。
    /// </summary>
    public enum CopyMode
    {
        /// <summary>A：今と同じ。あなたが全部の命令を終えてから、敵が同じ向き・同じ距離でコピー。</summary>
        Current,
        /// <summary>B：あなたが全部の命令を終えてから、敵が向きを反対にしてコピー。</summary>
        MirrorBatch,
        /// <summary>C：命令1つごとに、すぐ敵が向きを反対にしてコピー（あなた①→敵①→あなた②→敵②…）。</summary>
        MirrorImmediate,
        /// <summary>D（Claude 案）：命令1つごとに、すぐ敵が「あなたの方へ向けて」使う。</summary>
        TowardImmediate,
        /// <summary>E（研究で一番の候補）：命令1つごとに、すぐ敵が同じ札を使う。動く札はまねて（同じ向き）、技（攻撃・押す・引く・入れ替え・罠）ははね返す（反対向き）。</summary>
        MimicReflectImmediate,
    }

    public static class CopyCore
    {
        /// <summary>サンドボックスで選んだ方式（null ならステージの既定）。ACT1・今までのステージには効かない（copySandbox のステージだけ）。</summary>
        public static CopyMode? SandboxMode;
        /// <summary>サンドボックスの COPY OFF（敵がコピーしない）。比較用。</summary>
        public static bool SandboxCopyOff;

        public static readonly CopyMode[] All = { CopyMode.Current, CopyMode.MirrorBatch, CopyMode.MirrorImmediate, CopyMode.TowardImmediate, CopyMode.MimicReflectImmediate };

        public static bool IsImmediate(CopyMode m) => m == CopyMode.MirrorImmediate || m == CopyMode.TowardImmediate || m == CopyMode.MimicReflectImmediate;

        /// <summary>E で「はね返す」技（自分が動く札以外：攻撃・押す・引く・入れ替え・罠）。</summary>
        public static bool IsTechnique(CommandType t) => EnemyReplayController.IsReflected(t) || t == CommandType.Swap || t == CommandType.PlaceSpike;
        public static bool IsMirror(CopyMode m) => m == CopyMode.MirrorBatch || m == CopyMode.MirrorImmediate;

        public static string Letter(CopyMode m) => m switch
        {
            CopyMode.MirrorBatch => "B",
            CopyMode.MirrorImmediate => "C",
            CopyMode.TowardImmediate => "D",
            CopyMode.MimicReflectImmediate => "E",
            _ => "A",
        };

        public static string Name(CopyMode m) => m switch
        {
            CopyMode.MirrorBatch => "B 反転・まとめて",
            CopyMode.MirrorImmediate => "C 反転・すぐ",
            CopyMode.TowardImmediate => "D あなたへ・すぐ",
            CopyMode.MimicReflectImmediate => "E まねて・はね返す",
            _ => "A 今と同じ",
        };

        /// <summary>一文の規則（画面に出す）。</summary>
        public static string OneLine(CopyMode m) => m switch
        {
            CopyMode.MirrorBatch => "あなたが命令を全部終えたあと、敵はそれを反対向きで使う",
            CopyMode.MirrorImmediate => "あなたが命令を1つ使うたびに、すぐ敵がそれを反対向きで使う",
            CopyMode.TowardImmediate => "あなたが命令を1つ使うたびに、すぐ敵がそれをあなたの方へ向けて使う",
            CopyMode.MimicReflectImmediate => "あなたが命令を1つ使うたびに、すぐ敵が使う：動きはまねて、技ははね返す",
            _ => "あなたが命令を全部終えたあと、敵はそれを同じ向きで使う",
        };

        public static Direction Opposite(Direction d) => d switch
        {
            Direction.Up => Direction.Down,
            Direction.Down => Direction.Up,
            Direction.Left => Direction.Right,
            _ => Direction.Left,
        };

        /// <summary>from から to へ向く方向（縦横の差が大きい方。同じなら fallback）。</summary>
        public static Direction Toward(Vector2Int from, Vector2Int to, Direction fallback)
        {
            var d = to - from;
            if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x > 0 ? Direction.Right : Direction.Left;
            if (Mathf.Abs(d.y) > Mathf.Abs(d.x)) return d.y > 0 ? Direction.Up : Direction.Down;
            return fallback;
        }

        /// <summary>
        /// 命令1つを、この敵がコピーしたときの形（null＝コピーしない）。規則：①方式の向き（同じ／反対／あなたへ）→ ②反転斬り・誘い足（copyMirrored）はさらに反対 → ③敵のコピー特性（鏡兵・騎士）。
        /// 「すぐ」の方式は、その瞬間の位置で向きを決める（あなたへ）。
        /// </summary>
        public static CommandRecord CopyOne(CommandRecord r, UnitModel enemy, UnitModel player, CopyMode mode)
        {
            if (r == null || r.Command == null || r.Command.notCopied || Stunned(enemy) || !CopyKinds.Accepts(enemy, r.Command)) return null;
            var c = r.Clone();
            if (r.Command.UsesDirection)
            {
                if (mode == CopyMode.TowardImmediate && player != null) c.Direction = Toward(enemy.Position, player.Position, Opposite(r.Direction));
                else if (IsMirror(mode)) c.Direction = Opposite(c.Direction);
                else if (mode == CopyMode.MimicReflectImmediate && IsTechnique(r.Command.type)) c.Direction = Opposite(c.Direction);
                if (r.Command.copyMirrored) c.Direction = Opposite(c.Direction);
                if (enemy.CopyTrait == CopyTrait.Mirror) c.Direction = Opposite(c.Direction);
                else if (enemy.CopyTrait == CopyTrait.MirrorAttacks && EnemyReplayController.IsReflected(c.Command.type)) c.Direction = Opposite(c.Direction);
            }
            return c;
        }

        /// <summary>このユニットがコピーをするか（鏡晶などの物は盤面のマスなのでユニットではない。写し砲はコピーする）。</summary>
        public static bool Copies(UnitModel u) => u != null && !u.IsPlayer && u.IsAlive && !Stunned(u);

        /// <summary>
        /// 鏡の番人は、封印が割れている間はあなたの命令を写せない（動かない・殴り返さない）。
        /// 割れた窓は「直接殴れる時間」。C・E では殴った技が返ってくるので、写せないことにしないと窓が意味を持たない（CV6 の解析）。
        /// </summary>
        public static bool Stunned(UnitModel u) => u != null && (u.WakingUp || u.StunRounds > 0 || (u.Barrier == 0 && // 【戦闘中心 v3】共鳴で動けない間も
            (u.Kind == EnemyKind.Warden || u.Kind == EnemyKind.SealLord || (u.Kind == EnemyKind.Lord && PhaseRules.LordPhase(u) != 1)))); // 【V3】写し身の主も、守りが割れている間はまねない（近づいて斬れる）

        /// <summary>あなたが自分の命令で直接与えたダメージ（反響鎧・封印に弾かれる）。敵のコピー・兵種の行動・爆発・炎・鏡晶はこれに当たらない。</summary>
        public static bool IsPlayerDirect(UnitModel source, bool byCopy) => source != null && source.IsPlayer && !byCopy;

        /// <summary>【コピー依存度の指標】そのダメージが「コピー起点」か（敵のコピー・鏡晶・写し砲）。</summary>
        public static bool IsCopyOrigin(DamageCause c) => c != null && (c.ByCopy || c.Kind == DamageKind.Crystal || c.Kind == DamageKind.Resonance || (c.Source != null && c.Source.Kind == EnemyKind.Cannon));

        /// <summary>
        /// 敵のコピーを1つ実行するたびに呼ぶ（解析・反射のしるし用）：どの敵が、何番目の命令を、どの形で実行し、何が起きたか。
        /// </summary>
        public delegate void CopyStep(UnitModel enemy, int playerIndex, CommandRecord executed, List<BattleEvent> stepEvents);

        /// <summary>
        /// 「すぐ」の方式の1ラウンド（あなたの命令 → 敵A のコピー → 敵B のコピー → あなたの次の命令 …）。TurnRules と TurnManager の解析・ゲームで同じ順番。
        /// skip は「コピーさせない命令の番号」（空白の石）、noCopy は「コピーしない敵の Id」（解析の読み落とし）。
        /// </summary>
        public static TurnOutcome PlayImmediate(BattleModel model, IReadOnlyList<CommandRecord> cmds, List<BattleEvent> events, List<string> log,
            ICollection<int> noCopy, ICollection<int> skip, CopyStep onStep)
        {
            var player = model.Player;
            var outcome = TurnOutcome.Continue;
            for (int i = 0; i < cmds.Count && outcome == TurnOutcome.Continue; i++)
            {
                string text = RulesEngine.Execute(model, new CommandContext(player, cmds[i], false), events);
                log?.Add(text);
                outcome = TurnRules.Check(model);
                if (outcome != TurnOutcome.Continue || !model.CopyEnabled || (skip != null && skip.Contains(i))) continue;
                foreach (var enemy in model.Enemies.ToList())
                {
                    if (!Copies(enemy) || (noCopy != null && noCopy.Contains(enemy.Id))) continue;
                    var rec = CopyOne(cmds[i], enemy, player, model.CopyMode);
                    if (rec == null) continue;
                    var step = new List<BattleEvent>();
                    string copyText = RulesEngine.Execute(model, new CommandContext(enemy, rec, true), step); // log が null でも必ず実行（?. は右辺ごと飛ばす）
                    log?.Add(copyText);
                    onStep?.Invoke(enemy, i, rec, step);
                    events?.AddRange(step);
                    outcome = TurnRules.Check(model);
                    if (outcome != TurnOutcome.Continue) break;
                }
            }
            player.AttackBonus = 0;
            foreach (var e in model.Enemies) e.AttackBonus = 0;
            return outcome;
        }
    }
}
