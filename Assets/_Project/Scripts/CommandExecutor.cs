using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 技の実行の窓口（見た目つき）。ルールは RulesEngine（GameObject を使わない）にあり、
    /// ここは「ルールを実行 → 起きた出来事を演出係に渡す → ログの文章を返す」だけを行う。
    /// </summary>
    public class CommandExecutor
    {
        readonly GridManager grid;

        /// <summary>直前の Execute で起きた出来事。</summary>
        public IReadOnlyList<BattleEvent> LastEvents => lastEvents;
        readonly List<BattleEvent> lastEvents = new List<BattleEvent>();

        public CommandExecutor(GridManager grid)
        {
            this.grid = grid;
        }

        /// <summary>
        /// 命令を1つ実行する。isCopy＝敵がコピーした命令か、isInnate＝敵の固有行動か（ダメージ原因の記録に使う）。
        /// </summary>
        public string Execute(UnitController actor, CommandRecord record, bool isCopy = false, bool isInnate = false)
        {
            lastEvents.Clear();
            string text = RulesEngine.Execute(grid.Model, new CommandContext(actor.Model, record, isCopy, isInnate), lastEvents);
            grid.Presenter.Play(lastEvents);
            return text;
        }

        /// <summary>上下左右の隣4マス（斜めは含まない）。</summary>
        public static Vector2Int[] AroundCells(Vector2Int center) => RulesEngine.AroundCells(center);

        /// <summary>上下左右へ range マスまでの攻撃マス（壁・盤面の外で止まる）。</summary>
        public static List<Vector2Int> CrossCells(GridManager grid, Vector2Int center, int range) =>
            RulesEngine.CrossCells(grid.Model.Board, center, range);
    }
}
