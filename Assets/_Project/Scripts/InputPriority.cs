using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC4・2026-10-23 本人の RC3 プレイ「移動方向を押したいのに UI が邪魔して押せない」】戦闘画面の入力の優先順位。
    /// IMGUI は先に処理した物がクリックを取るので、順番をここに決めて、テスト（RC4Tests）で守る。
    /// 札を選んで向きを決める間：① 右クリック・Esc＝取り消し ② 隣のマスまるごと（盤面の外でも同じ大きさ）③ その向きの道・攻撃のマス。
    /// 敵の詳細・説明の窓・手札は、そのあと（向きを選んでいる間は敵を選ばない）。
    /// </summary>
    public static class InputPriority
    {
        /// <summary>向きの当たり：隣のマス（主）を先に、その向きの道・攻撃のマス（副）をあとに探す。どれにも当たらなければ false。</summary>
        public static bool PickDirection(IReadOnlyDictionary<Direction, Rect> main, IReadOnlyList<(Direction dir, Rect rect)> extra, Vector2 p, out Direction dir)
        {
            foreach (var kv in main) if (kv.Value.Contains(p)) { dir = kv.Key; return true; }
            if (extra != null) foreach (var (d, r) in extra) if (r.Contains(p)) { dir = d; return true; }
            dir = Direction.Up;
            return false;
        }

        /// <summary>敵のマスのクリックで敵を選ぶのは、左クリックで、札を選んでいないとき（札を選んでいる間は向き・距離の入力が先）。</summary>
        public static bool EnemySelectAllowed(int button, bool cardPending, bool tacticTargeting, bool pickerOpen, bool detailOpen) =>
            button == 0 && !cardPending && !tacticTargeting && !pickerOpen && !detailOpen;

        /// <summary>向きを選んでいる間に、向きの入力を画面の何より先に受けるか（距離・窓の入力の最中は受けない）。</summary>
        public static bool DirectionFirst(bool directionMode, bool pickerOpen, bool detailOpen) => directionMode && !pickerOpen && !detailOpen;

        /// <summary>W A S D・矢印キー → 向き（それ以外は null）。</summary>
        public static Direction? KeyToDirection(KeyCode key) =>
            key == KeyCode.UpArrow || key == KeyCode.W ? Direction.Up
          : key == KeyCode.DownArrow || key == KeyCode.S ? Direction.Down
          : key == KeyCode.LeftArrow || key == KeyCode.A ? Direction.Left
          : key == KeyCode.RightArrow || key == KeyCode.D ? Direction.Right : (Direction?)null;

        /// <summary>取り消し：右クリック（マウスのボタン 1）か Esc。</summary>
        public static bool IsCancel(EventType type, int button, KeyCode key) =>
            (type == EventType.MouseDown && button == 1) || (type == EventType.KeyDown && key == KeyCode.Escape);
    }
}
