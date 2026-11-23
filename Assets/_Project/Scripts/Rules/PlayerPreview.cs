using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】あなた自身のカードの範囲プレビュー（2026-10-05 本人プレイ：どの方向へ行ける／攻撃できるかが分かりにくい）。
    /// 「このカードをあなたが使うと、どこへ行けるか・どこを攻撃するか」だけを出す。敵のコピーの結果（未来）は出さない（完全予測は標準 OFF のまま）。
    /// すでに組んだ命令があれば、それを「あなただけ」実行した後の位置から出す（敵はあなたの命令をすべて実行した後に動くので、あなたの位置は確定している）。
    /// </summary>
    public static class PlayerPreview
    {
        public class Option
        {
            public Direction? Direction;
            public int Distance;
            /// <summary>移動した後のあなたの位置（動けなければ元の位置）。</summary>
            public Vector2Int Landing;
            public bool Moved;
            /// <summary>通ったマス（着地を含む）。</summary>
            public readonly List<Vector2Int> Path = new List<Vector2Int>();
            /// <summary>攻撃が届くマス（壁の手前まで）。</summary>
            public readonly List<Vector2Int> AttackCells = new List<Vector2Int>();
            /// <summary>押し出し・引き寄せなどで動く相手の移動先。</summary>
            public readonly List<Vector2Int> ControlCells = new List<Vector2Int>();
            /// <summary>当たる相手と、与えるダメージ（あなたの命令だけで見た値）。</summary>
            public readonly List<(Vector2Int cell, int damage)> Hits = new List<(Vector2Int, int)>();
            public int Healed;
            /// <summary>【検証中】あなた自身が受けるダメージ（通ったトゲなど。敵のコピーは入らない）。</summary>
            public int SelfDamage;
        }

        /// <summary>組んだ命令を、あなただけ実行した後の盤面（敵のコピー・兵種の行動・環境は起きない）。</summary>
        public static BattleModel AfterPlanned(BattleModel model, IReadOnlyList<CommandRecord> planned, int attackBonus = 0)
        {
            var sim = StartOfRound(model, attackBonus);
            if (planned == null) return sim;
            foreach (var r in planned)
            {
                if (!sim.Player.IsAlive || PuzzleRules.Won(sim)) break;
                RulesEngine.Execute(sim, new CommandContext(sim.Player, r, false), new List<BattleEvent>());
            }
            return sim;
        }

        /// <summary>【検証中】反射のしるし（鏡兵・鏡守の騎士が、この攻撃を逆向きにコピーしたときに当たるマス）。</summary>
        public class Reflect
        {
            /// <summary>反射する敵の位置（あなたの命令の後）。</summary>
            public Vector2Int Enemy;
            public readonly List<Vector2Int> Cells = new List<Vector2Int>();
            /// <summary>あなたの予定の位置に当たるダメージ（確定のときだけ意味がある）。</summary>
            public int DamageToYou;
            /// <summary>（予備）確定できないときに「反射？」と出す。今の計算は本物と同じ順番で試すので、常に false。</summary>
            public bool Uncertain;
        }

        /// <summary>
        /// 【検証中】2026-10-05 本人：ボスの反射が分かりにくい → 攻撃を選んだとき、反射されるマスだけを出す（完全予測ではない：ほかの敵の動き・兵種の行動は出さない）。
        /// 計算は本物と同じ順番で1回試す：あなたの命令を全部（この攻撃も）→ 敵A がコピーを全部 → 敵B …。その途中で、鏡兵・騎士が「この攻撃のコピー（逆向き）」を
        /// 実行した瞬間のマスと、あなたへのダメージだけを取り出す（レビュー：前の版は自分の押し出し・とどめ・闘気・前の敵の押しを入れておらず、しるしと実際がずれた）。
        /// 反転斬りは二重に反転して反射されないので出さない。skip は空白の石で「コピーさせない」番号。
        /// </summary>
        public static List<Reflect> Reflections(BattleModel model, IReadOnlyList<CommandRecord> planned, CommandRecord record, ICollection<int> skip = null, int attackBonus = 0)
        {
            var list = new List<Reflect>();
            if (model == null || record?.Command == null || record.Command.notCopied || !record.Command.UsesDirection) return list;
            // 【Copy Core V2】反対向きの方式（B/C/D）では全部の敵が「返してくる」ので、しるしは出さない（向きの矢印だけ。結果は自分で読む）
            if (model.CopyMode != CopyMode.Current || !model.CopyEnabled) return list;
            if (!model.Enemies.Any(e => e.IsAlive && (e.CopyTrait == CopyTrait.Mirror || e.CopyTrait == CopyTrait.MirrorAttacks))) return list;
            var all = (planned ?? new List<CommandRecord>()).ToList();
            int index = all.Count;
            all.Add(record);
            if (skip != null && skip.Contains(index)) return list;
            var sim = StartOfRound(model, attackBonus);
            // 本物と同じ処理（TurnRules）で1回試し、鏡兵・騎士が「この攻撃のコピー（逆向き）」を実行した瞬間だけを取り出す
            TurnRules.PlayCommandsAndCopies(sim, all, null, null, null, null, (enemy, playerIndex, executed, step) =>
            {
                if (playerIndex != index) return;
                if (enemy.CopyTrait != CopyTrait.Mirror && enemy.CopyTrait != CopyTrait.MirrorAttacks) return;
                if (executed.Direction == record.Direction) return; // 反転斬りなど：反射されない
                var rf = new Reflect { Enemy = enemy.Position };
                foreach (var ev in step)
                {
                    if (ev.Type == BattleEventType.TargetCell && !rf.Cells.Contains(ev.Cell)) rf.Cells.Add(ev.Cell);
                    else if (ev.Type == BattleEventType.Damaged && ev.Unit != null && ev.Unit.IsPlayer) rf.DamageToYou += ev.Amount;
                }
                if (rf.Cells.Count > 0 || rf.DamageToYou > 0) list.Add(rf);
            }, skip);
            return list;
        }

        /// <summary>組んだ命令の後のあなたの位置。</summary>
        public static Vector2Int PlannedPosition(BattleModel model, IReadOnlyList<CommandRecord> planned, int attackBonus = 0) => AfterPlanned(model, planned, attackBonus).Player.Position;

        /// <summary>
        /// 実行が始まる瞬間の盤面（複製）：ラウンドの始まり（棘導の「最初の1回」を戻す）と、先兵の +1（attackBonus）。
        /// TurnManager の実行と同じ順番（レビュー：前はこれが無く、先兵・棘導でとどめの有無がずれた）。
        /// </summary>
        static BattleModel StartOfRound(BattleModel model, int attackBonus)
        {
            var sim = model.Clone();
            sim.BeginRound();
            if (attackBonus > 0) sim.Player.AttackBonus += attackBonus;
            return sim;
        }

        /// <summary>このカードを、組んだ命令の後に使ったときの範囲（方向・距離ごと）。</summary>
        public static List<Option> Compute(BattleModel model, IReadOnlyList<CommandRecord> planned, CommandData card, int attackBonus = 0, CardInstance instance = null)
        {
            var list = new List<Option>();
            if (card == null || model == null || model.Player == null) return list;
            var start = AfterPlanned(model, planned, attackBonus);
            // 【RC7・コードレビュー】札の実体（強化：自分＋の +2）も入れる（前は強化した札でも元のダメージを出していた）
            if (!card.UsesDirection) { list.Add(Run(start, new CommandRecord(card, Direction.Up, 0, instance), null)); return list; }
            foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                for (int dist = card.minDistance; dist <= card.maxDistance; dist++)
                    list.Add(Run(start, new CommandRecord(card, d, dist, instance), d));
            return list;
        }

        static Option Run(BattleModel start, CommandRecord record, Direction? dir)
        {
            var sim = start.Clone();
            var from = sim.Player.Position;
            var events = new List<BattleEvent>();
            RulesEngine.Execute(sim, new CommandContext(sim.Player, record, false), events);
            var o = new Option { Direction = dir, Distance = record.Distance, Landing = sim.Player.Position, Moved = sim.Player.Position != from };
            foreach (var e in events)
            {
                if (e.Type == BattleEventType.Moved && e.Unit != null && e.Unit.IsPlayer && !o.Path.Contains(e.To)) o.Path.Add(e.To);
                else if (e.Type == BattleEventType.Moved && e.Unit != null && !e.Unit.IsPlayer && !o.ControlCells.Contains(e.To)) o.ControlCells.Add(e.To);
                else if (e.Type == BattleEventType.TargetCell && !o.AttackCells.Contains(e.Cell)) o.AttackCells.Add(e.Cell);
                else if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer) o.Hits.Add((start.UnitById(e.Unit.Id).Position, e.Amount));
                else if (e.Type == BattleEventType.Healed && e.Unit != null && e.Unit.IsPlayer) o.Healed += e.Amount;
                else if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer) o.SelfDamage += e.Amount;
            }
            return o;
        }
    }
}
