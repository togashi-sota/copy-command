using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>【RC4】敵が動いた出来事の種類：まね（水色）・自分の動き（オレンジ）・どちらでもない（あなたの押し・引き・ワープ・ボスの帰還など）。</summary>
    public enum EnemyMoveKind { None, Copy, Own }

    /// <summary>
    /// 【RC4・本人「敵がいつ近づくのか分からない」】組んだ命令でこのターンがどうなるかを、盤面の写しで試す（RC3 の順番は全部決まっている）。
    /// 画面に出すのは2つだけ：①あなたが受けるダメージ（原因ごと）②敵が「自分で」動く先（接近・位置取り：オレンジの矢印と影）。
    /// 敵のまねの結果（敵どうしの当たり）は出さない（まねを読むのは遊ぶ人の仕事）。
    /// </summary>
    public class TurnForecast
    {
        /// <summary>敵が自分で動く（まねではない）：まねのあとの位置から、位置取りで動いた道。</summary>
        public class OwnMove
        {
            public int EnemyId;
            public EnemyKind Kind;
            /// <summary>まねのあとの位置（動き出し）。</summary>
            public Vector2Int From;
            /// <summary>通るマス（From を除く・最後が行き先）。</summary>
            public readonly List<Vector2Int> Path = new List<Vector2Int>();
            public Vector2Int To => Path.Count > 0 ? Path[Path.Count - 1] : From;
            /// <summary>画面の短い語：接近（あなたへ）・位置取り（弓兵：行・列へ）。</summary>
            public string Label => Kind == EnemyKind.Archer ? "位置取り" : Path.Count >= 2 ? $"接近{Path.Count}" : "接近";
        }

        public readonly List<OwnMove> OwnMoves = new List<OwnMove>();
        /// <summary>敵ごとの、まね（とあなたの押し・引き）のあとの位置。</summary>
        public readonly Dictionary<int, Vector2Int> AfterCopy = new Dictionary<int, Vector2Int>();
        /// <summary>あなたが受けるダメージの出来事（このターンの全部）。</summary>
        public readonly List<BattleEvent> PlayerHits = new List<BattleEvent>();
        public bool PlayerDies;
        /// <summary>【文化祭】敵ごとの受けるダメージ（はじめてだけ：矢印にマウスを乗せると「-4 たおせる！」）・倒れる敵。</summary>
        public readonly Dictionary<int, int> EnemyDamage = new Dictionary<int, int>();
        public readonly HashSet<int> EnemyDies = new HashSet<int>();
        public bool Failed;

        /// <summary>写しで1ターンを試す（元の盤面は変えない）。</summary>
        public static TurnForecast Compute(BattleModel model, IReadOnlyList<CommandRecord> plan, ICollection<int> skipSlots = null)
        {
            var f = new TurnForecast();
            try
            {
                var sim = model.Clone();
                var events = new List<BattleEvent>();
                sim.BeginRound();
                var outcome = TurnRules.PlayCommandsAndCopies(sim, plan, null, events, null, null, null, skipSlots, sim.V4); // 【RC4・レビュー】空白の石の命令はまねない
                foreach (var e in sim.Enemies) if (e.IsAlive) f.AfterCopy[e.Id] = e.Position;
                if (outcome == TurnOutcome.Continue && sim.V4 && PhaseRules.IntentOrder)
                {
                    foreach (var (enemy, part) in PhaseRules.TraitSteps(sim, 2))
                    {
                        if (!enemy.IsAlive || !PhaseRules.HasTrait(enemy)) continue;
                        int before = events.Count;
                        var from = enemy.Position;
                        PhaseRules.TraitAction(sim, enemy, events, part);
                        RulesEngine.CheckPlates(sim, events);
                        if (part == PhaseRules.PartPosition && enemy.Position != from)
                        {
                            var mv = new OwnMove { EnemyId = enemy.Id, Kind = enemy.Kind, From = from };
                            for (int i = before; i < events.Count; i++)
                                if (events[i].Type == BattleEventType.Moved && events[i].Unit == enemy) mv.Path.Add(events[i].To);
                            if (mv.Path.Count == 0) mv.Path.Add(enemy.Position);
                            f.OwnMoves.Add(mv);
                        }
                        outcome = TurnRules.Check(sim);
                        if (outcome != TurnOutcome.Continue) break;
                    }
                }
                else if (outcome == TurnOutcome.Continue) outcome = PhaseRules.TraitPhaseAfter(sim, events, null);
                if (outcome == TurnOutcome.Continue) PhaseRules.EnvironmentPhase(sim, events, null);
                f.PlayerHits.AddRange(events.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0));
                f.PlayerDies = !sim.Player.IsAlive;
                foreach (var e in events)
                    if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && !e.Unit.IsObject && e.Amount > 0)
                        f.EnemyDamage[e.Unit.Id] = (f.EnemyDamage.TryGetValue(e.Unit.Id, out var d0) ? d0 : 0) + e.Amount;
                foreach (var e in sim.Enemies) if (!e.IsAlive && f.EnemyDamage.ContainsKey(e.Id)) f.EnemyDies.Add(e.Id);
            }
            catch (System.Exception)
            {
                f.Failed = true;
            }
            return f;
        }

        /// <summary>【RC4・本人「まねと敵自身の移動が混ざる」】敵が動いた出来事が、まねか・自分の動きか（画面の色と札を分ける）。</summary>
        public static EnemyMoveKind MoveKindOf(BattleEvent e)
        {
            var u = e.Unit;
            var c = e.Cause;
            if (e.Type != BattleEventType.Moved || u == null || u.IsPlayer || u.IsObject || c == null || c.Source != u || e.From == e.To || c.Kind == DamageKind.Clock) return EnemyMoveKind.None; // 【RC4・レビュー】大技の突進は「接近」ではない
            if (c.ByCopy) return EnemyMoveKind.Copy;
            return c.Command == null ? EnemyMoveKind.Own : EnemyMoveKind.None;
        }

        /// <summary>動いた敵の上に出す語：まね・接近・位置取り（弓兵）。</summary>
        public static string MoveTagText(EnemyMoveKind kind, EnemyKind enemy) =>
            kind == EnemyMoveKind.Copy ? "まね" : kind == EnemyMoveKind.Own ? (enemy == EnemyKind.Archer ? "位置取り" : "接近") : null;

        /// <summary>ダメージの原因の短い語（「剣兵Aのまね（斬撃）」「弓兵の攻撃（赤）」「大技（赤紫）」…）。画面の一言とターンのあとの振り返りで同じ言い方。</summary>
        public static string CauseText(BattleEvent e)
        {
            string Who(UnitModel u) => u == null ? "敵" : u.IsPlayer ? "あなた" : PhaseRules.ShortName(u); // 【RC5】名札と同じ呼び名
            var c = e.Cause;
            if (c == null) return "ダメージ";
            if (c.ByCopy && c.Source != null && !c.Source.IsPlayer) return $"{Who(c.Source)}のまね（{(c.Command != null ? c.Command.displayName : "攻撃")}）";
            switch (c.Kind)
            {
                case DamageKind.Clock: return c.Source != null && !c.Source.IsPlayer ? $"{Who(c.Source)}の大技（紫）" : "大技（紫）";
                case DamageKind.Trait: return $"{Who(c.Source)}の攻撃（{(Threats.Follows(c.Source) ? "赤" : "紫")}）"; // 【RC11】色＝種類
                case DamageKind.Spike: return "トゲ";
                case DamageKind.Fire: return "床の危険";
                default: return c.Source != null && c.Source.IsPlayer ? "あなたの命令" : Who(c.Source);
            }
        }

        /// <summary>命令の欄の見出しの一言（受けないなら null）。</summary>
        public string DangerText()
        {
            if (Failed || PlayerHits.Count == 0) return null;
            int total = PlayerHits.Sum(e => e.Amount);
            var groups = PlayerHits.GroupBy(CauseText).ToList();
            if (PlayerDies) return "！ この命令で、あなたが倒れる：" + string.Join("・", groups.Select(g => $"{g.Key} {g.Sum(e => e.Amount)}ダメ"));
            if (groups.Count == 1) return $"！ このターン、{groups[0].Key}で あなたに {total}ダメージ";
            return $"！ このターン あなたに {total}ダメージ：" + string.Join("・", groups.Select(g => $"{g.Key} {g.Sum(e => e.Amount)}"));
        }
    }
}
