using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 上下左右。盤面に対する絶対方向（ユニットに「向き」は無い）。
    /// </summary>
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right,
    }

    public static class DirectionExtensions
    {
        /// <summary>1マス進んだときのずれ。例：Right = (1, 0)</summary>
        public static Vector2Int ToOffset(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return Vector2Int.up;
                case Direction.Down: return Vector2Int.down;
                case Direction.Left: return Vector2Int.left;
                default: return Vector2Int.right;
            }
        }

        /// <summary>
        /// 【検証中】斜め移動のずれ。方向を時計回りに45度回した斜め：↑＝右上、→＝右下、↓＝左下、←＝左上。
        /// （方向は4つのまま。敵も同じ斜めをコピーする）
        /// </summary>
        public static Vector2Int ToDiagonalOffset(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return new Vector2Int(1, 1);
                case Direction.Right: return new Vector2Int(1, -1);
                case Direction.Down: return new Vector2Int(-1, -1);
                default: return new Vector2Int(-1, 1);
            }
        }

        /// <summary>斜めの表示用。例：Up → 「↗右上」</summary>
        public static string ToDiagonalLabel(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return "↗右上";
                case Direction.Right: return "↘右下";
                case Direction.Down: return "↙左下";
                default: return "↖左上";
            }
        }

        /// <summary>表示用。例：Right → 「→右」</summary>
        public static string ToLabel(this Direction d)
        {
            switch (d)
            {
                case Direction.Up: return "↑上";
                case Direction.Down: return "↓下";
                case Direction.Left: return "←左";
                default: return "→右";
            }
        }
    }
}
