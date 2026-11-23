using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC8・本人「どの攻撃がどの敵か分からない」「固定の攻撃と、敵が動くと位置が変わる攻撃を分けて」】危険の種類。
    /// Follow＝敵と一緒に動く（その敵を押す・引く・倒すと、ずれる・消える）／Fixed＝床に決まっている（撃つ敵を倒せば消えるが、押しても動かない）／
    /// Floor＝床の仕掛け（誰のものでもない。炎・落石・光線）。docs/final/THREAT_TYPES.md。
    /// </summary>
    public enum ThreatKind { Follow, Fixed, Floor }

    /// <summary>【RC8】いつ当たるか（ターンの順番の段）：②敵の攻撃・④大技・⑥床・次のターンの床。</summary>
    public enum ThreatTiming { Attack, BigAttack, Floor, NextFloor }

    /// <summary>【RC8】危険1つ（1体の敵の1つの攻撃、または床の仕掛け）。</summary>
    public class Threat
    {
        /// <summary>撃つ敵（床の仕掛けは null）。</summary>
        public UnitModel Source;
        public ThreatKind Kind;
        public ThreatTiming Timing;
        public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        public int Damage;
        /// <summary>画面の呼び名（剣・槍・矢・爆撃・光柱・大技の名前・炎・落石・光線）。</summary>
        public string Name;
        public int SourceId => Source != null ? Source.Id : -1;
    }

    /// <summary>
    /// 【RC8】今の盤面の危険の一覧（ルールと同じ計算。画面とテストと解析で同じものを使う）。
    /// 赤（②敵の攻撃）は、攻撃する瞬間の敵の位置・向きから出る：剣・噛みつき（隣4マス）・槍（向いた方2マス）・矢（向いた方の線）＝Follow。
    /// 砲兵の爆撃・鏡の主の光柱と奔流は、前のターンに床へ決まる＝Fixed（赤）。大技（赤紫）は①で床へ決まる＝Fixed。床の仕掛け＝Floor。
    /// どれも、撃つ敵が倒れたら起きない（床の仕掛けは除く）。
    /// </summary>
    public static class Threats
    {
        /// <summary>この敵の赤（通常の攻撃）は、敵と一緒に動くか（押す・引くでずれる）。</summary>
        public static bool Follows(UnitModel e) =>
            e != null && (e.Kind == EnemyKind.Swordsman || e.Kind == EnemyKind.Spearman || e.Kind == EnemyKind.Chaser || e.Kind == EnemyKind.ExecKnight || e.ShootsArrows);

        /// <summary>今の盤面の危険（あなたの命令の前）。includeFloor＝床の仕掛けも入れる。</summary>
        public static List<Threat> Compute(BattleModel model, bool includeFloor = true)
        {
            var list = new List<Threat>();
            if (model == null || model.Player == null) return list;
            bool v4 = model.V4 && PhaseRules.IntentOrder;
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || e.IsObject || e.Dormant) continue;
                if (model.V4 && !PhaseRules.HasTrait(e)) continue; // 動けない敵（処刑のあと・沈黙・共鳴）は撃たない
                if (!model.NoTraits)
                {
                    // ②敵の攻撃（赤）
                    if (e.Kind == EnemyKind.Bomber)
                        Add(list, model, e, ThreatKind.Fixed, ThreatTiming.Attack, e.BombCells, PhaseRules.Bomb(model), "爆撃");
                    else if (e.Kind == EnemyKind.MirrorLord)
                    {
                        if (v4) Add(list, model, e, ThreatKind.Fixed, ThreatTiming.Attack, e.BombCells, CombatBoss.PillarDamage, "光柱");
                        if (v4 && e.SlamCd == 1) Add(list, model, e, ThreatKind.Fixed, ThreatTiming.Attack, e.SlamCells, e.BossStage >= 2 ? CombatBoss.ChargeDamage : CombatBoss.SlamDamage, e.BossStage >= 2 ? "鏡の奔流" : "大剣");
                    }
                    else
                    {
                        int d = PhaseRules.AttackDamageOf(model, e);
                        if (d > 0) Add(list, model, e, Follows(e) ? ThreatKind.Follow : ThreatKind.Fixed, ThreatTiming.Attack, PhaseRules.TraitCells(model, e).Where(c => !e.BombCells.Contains(c)), d, AttackName(e));
                    }
                }
                // ④大技（赤紫）：①で床に決まる
                foreach (var c in e.Clocks.Where(c => c.Telegraphed))
                    Add(list, model, e, ThreatKind.Fixed, ThreatTiming.BigAttack, c.Cells, c.Damage + Clocks.DamageBonus, c.Name);
            }
            if (includeFloor)
            {
                var env = model.Environment;
                if (env.HasFire)
                {
                    var now = env.FireCellsNow.ToList();
                    if (now.Count > 0) Add(list, model, null, ThreatKind.Floor, ThreatTiming.Floor, now, env.FireDamage, "床");
                    var next = env.FireCellsNext.ToList(); // 【RC11・初見テスト（戦術好き）「同じマスに2ターン続けて落ちる岩が『今』としか出ず、次のターンも落ちると読めなかった」】今と次の両方に出す
                    if (next.Count > 0) Add(list, model, null, ThreatKind.Floor, ThreatTiming.NextFloor, next, env.FireDamage, "床");
                }
            }
            return list;
        }

        static void Add(List<Threat> list, BattleModel model, UnitModel src, ThreatKind kind, ThreatTiming timing, IEnumerable<Vector2Int> cells, int dmg, string name)
        {
            var t = new Threat { Source = src, Kind = kind, Timing = timing, Damage = dmg, Name = name };
            foreach (var c in cells)
                if (model.Board.IsInside(c) && !model.Board.IsWall(c) && !t.Cells.Contains(c)) t.Cells.Add(c);
            if (t.Cells.Count > 0 && dmg > 0) list.Add(t);
        }

        /// <summary>赤（通常の攻撃）の呼び名。</summary>
        public static string AttackName(UnitModel e)
        {
            switch (e.Kind)
            {
                case EnemyKind.Swordsman:
                case EnemyKind.ExecKnight: return "剣";
                case EnemyKind.Chaser: return "噛みつき";
                case EnemyKind.Spearman: return "槍";
                case EnemyKind.Bomber: return "爆撃";
                default: return e.ShootsArrows ? "矢" : "攻撃";
            }
        }

        /// <summary>このターン、そのマスに当たる危険（次のターンの床は除く）。</summary>
        public static IEnumerable<Threat> At(List<Threat> threats, Vector2Int cell) =>
            threats.Where(t => t.Timing != ThreatTiming.NextFloor && t.Cells.Contains(cell));

        /// <summary>このターン、そのマスに当たる合計（次のターンの床は除く）。</summary>
        public static int DamageAt(List<Threat> threats, Vector2Int cell) => At(threats, cell).Sum(t => t.Damage);

        /// <summary>1マスに重なる危険の一番大きい合計（ボスの火力の検査）。</summary>
        public static int MaxStack(List<Threat> threats) =>
            threats.Where(t => t.Timing != ThreatTiming.NextFloor).SelectMany(t => t.Cells).Distinct().Select(c => DamageAt(threats, c)).DefaultIfEmpty(0).Max();

        /// <summary>
        /// 【RC8】あなたの命令（組んだもの＋試している1つ）のあと、危険がどう変わるか：敵ごとに「ずれた」「消えた」。
        /// 命令の後の盤面は PlayerPreview.AfterPlanned（あなたの命令だけ。まね・敵の行動はまだ）。赤はこのあと②で撃たれるので、ここで決まる。
        /// </summary>
        public class Change
        {
            public UnitModel Source;          // 元の盤面の敵
            public Threat Before;             // 命令の前
            public Threat After;              // 命令の後（消えたら null）
            public bool Gone => After == null;
            public bool Moved => After != null && !(After.Cells.Count == Before.Cells.Count && After.Cells.All(Before.Cells.Contains));
        }

        /// <summary>命令の前と後の危険を、敵と種類ごとに突き合わせる（床の仕掛けは変わらないので入れない）。</summary>
        public static List<Change> Diff(List<Threat> before, List<Threat> after)
        {
            var list = new List<Change>();
            foreach (var b in before)
            {
                if (b.Source == null || b.Timing == ThreatTiming.NextFloor) continue;
                var a = after.FirstOrDefault(x => x.SourceId == b.SourceId && x.Timing == b.Timing && x.Name == b.Name);
                var ch = new Change { Source = b.Source, Before = b, After = a };
                if (ch.Gone || ch.Moved) list.Add(ch);
            }
            return list;
        }
    }
}
