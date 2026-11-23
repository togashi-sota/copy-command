using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【Puzzle v2・2026-10-06 本人プレイ】「今決まっている」敵の攻撃の範囲（ターン開始時から常に見せる）。
    /// 組んだ命令・選んだ札とは無関係（引数に取らない）。コピーの後の未来は予測しない。
    /// 表示の層：赤＝兵の攻撃（剣・槍・弓・砲兵の爆撃）／紫＝ボス・仕掛けの予告（光柱・大剣・石像の視線など）。
    /// 青＝あなたの札の範囲・水色＝この札をまねる敵・金＝選んだ方向 は画面側（BattleUI）。
    /// </summary>
    public static class ThreatMap
    {
        /// <summary>ボス（攻撃を紫の予告で出す敵）。</summary>
        public static bool IsBoss(UnitModel u) =>
            u.Kind == EnemyKind.Lord || u.Kind == EnemyKind.SealLord || u.Kind == EnemyKind.MirrorLord || u.Kind == EnemyKind.Warden || u.Kind == EnemyKind.KnightV3 || u.Kind == EnemyKind.Knight;

        /// <summary>赤：兵の攻撃（今の位置・向きのまま兵種の行動をしたら当たるマス）。砲兵の爆撃（予告済み）は Bombs。</summary>
        public static List<Vector2Int> EnemyAttack(BattleModel model)
        {
            var cells = new List<Vector2Int>();
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || IsBoss(e)) continue;
                if (model.V4 && !PhaseRules.HasTrait(e)) continue; // 【RC3・レビュー】動けない敵（処刑のあと・沈黙）は攻撃しないので赤も出さない
                if ((e.Kind == EnemyKind.Chaser && !(model.V4 && PhaseRules.IntentOrder)) || e.Kind == EnemyKind.Fleer || e.Kind == EnemyKind.Bomber) continue; // 【RC3】追跡兵の噛みつきも赤で
                cells.AddRange(PhaseRules.TraitCells(model, e).Where(c => !e.BombCells.Contains(c)));
            }
            cells.AddRange(BossAttack(model)); // 【RC5】鏡の主の光柱・奔流も赤
            return Distinct(model, cells);
        }

        /// <summary>
        /// 【RC5・レビュー】赤（ボス）：鏡の主の光柱・鏡の奔流。前のターンに決まり、このターンの「敵の攻撃」の段（まねの前）に落ちる＝赤の約束と同じ。
        /// 前は赤紫（大技＝まねの後）で描いていて、まねで写し身を光柱へ送っても当たらなかった（表示とずれていた）。
        /// </summary>
        public static List<Vector2Int> BossAttack(BattleModel model)
        {
            var cells = new List<Vector2Int>();
            if (!model.V4) return cells;
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || e.Kind != EnemyKind.MirrorLord || !PhaseRules.HasTrait(e)) continue;
                cells.AddRange(e.BombCells);
                if (e.SlamCd == 1) cells.AddRange(e.SlamCells);
            }
            return Distinct(model, cells);
        }

        /// <summary>赤（爆撃の印）：砲兵が予告した爆撃のマス（次の兵種の行動で必ず落ちる）。</summary>
        public static List<Vector2Int> Bombs(BattleModel model) =>
            Distinct(model, model.Enemies.Where(e => e.IsAlive && !IsBoss(e) && (!model.V4 || PhaseRules.HasTrait(e))).SelectMany(e => e.BombCells).ToList());

        /// <summary>紫：ボス・仕掛けの予告（光柱・大剣・炎の波・石像の視線）。</summary>
        public static List<Vector2Int> Telegraph(BattleModel model)
        {
            var cells = new List<Vector2Int>();
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || !IsBoss(e)) continue;
                if (model.V4 && e.Kind == EnemyKind.MirrorLord) continue; // 【RC5】鏡の主の光柱・奔流は赤（BossAttack）。赤紫は①の大技だけ
                cells.AddRange(e.BombCells);
                if (e.SlamCd == 1 || (e.SlamCells.Count > 0 && e.Kind != EnemyKind.KnightV3)) cells.AddRange(e.SlamCells);
                if (e.Kind == EnemyKind.KnightV3 && e.WaveCd == 1) cells.AddRange(PhaseRules.WaveCells(model, e.WaveCount));
                if (e.Kind == EnemyKind.Knight || e.Kind == EnemyKind.KnightV2) cells.AddRange(PhaseRules.TraitCells(model, e));
            }
            // 【Combat V4】カウント大技の予告（残り①：そのターンの最後（まねの後・完成版）に発動）。敵の種類を問わず紫で
            foreach (var e in model.Enemies) if (e.IsAlive && (!model.V4 || PhaseRules.HasTrait(e))) cells.AddRange(Clocks.WarnCells(e)); // 【RC3】動けない間は撃たない
            cells.AddRange(PuzzleRules.HazardCells(model));
            return Distinct(model, cells);
        }

        /// <summary>このマスは今の予告（赤・紫）のどれかに入っているか。</summary>
        public static bool IsThreatened(BattleModel model, Vector2Int cell) =>
            EnemyAttack(model).Contains(cell) || Bombs(model).Contains(cell) || Telegraph(model).Contains(cell);

        static List<Vector2Int> Distinct(BattleModel model, List<Vector2Int> cells) =>
            cells.Where(model.Board.IsInside).Distinct().ToList();
    }
}
