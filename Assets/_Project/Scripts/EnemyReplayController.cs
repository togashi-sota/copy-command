using System.Collections.Generic;

namespace CopyCommand
{
    /// <summary>
    /// 敵の「頭脳」。やることはこれだけ：
    ///   1. PLAYER TURN に使われた命令列（CommandRecord）をそのままコピーして覚える
    ///   2. ENEMY TURN に、その命令を各敵のコピー特性どおりに返す（通常コピーなら同じ順番・同じ方向・同じ距離）
    /// プレイヤーに近づく判断やランダムな選択はしない（予測可能な CPU）。
    /// </summary>
    public class EnemyReplayController
    {
        readonly List<CommandRecord> copiedCommands = new List<CommandRecord>();

        /// <summary>コピーした命令（通常コピーの並び）。</summary>
        public IReadOnlyList<CommandRecord> CopiedCommands => copiedCommands;

        /// <summary>プレイヤーの命令列をコピーする（結果ではなく、入力した内容をそのまま）。</summary>
        public void CopyFrom(IEnumerable<CommandRecord> playerCommands)
        {
            copiedCommands.Clear();
            foreach (var record in playerCommands) copiedCommands.Add(record.Clone());
        }

        public void Clear() => copiedCommands.Clear();

        /// <summary>この敵のコピー特性で並べ替えた命令列。</summary>
        public IReadOnlyList<CommandRecord> GetCommandsFor(CopyTrait trait) => ApplyTrait(copiedCommands, trait);

        /// <summary>
        /// コピー特性を命令列に反映する。Normal：そのまま／Reverse：逆の順番／Mirror：方向を上下・左右逆に。
        /// （今のステージで使うのは Normal だけ。Reverse・Mirror は将来のための準備）
        /// </summary>
        /// <summary>
        /// 【検証中・コピー操作】あなたの命令列から、敵に渡す命令を作る：コピーされない命令（忍び足）を除き、
        /// 反転斬りは反対向きに、逆さ写しがあれば順番を逆にする。skip は「このターンだけ渡さない番号」（空白の石）。
        /// ゲーム（TurnManager）と解析（TurnRules）で同じ物を使う。
        /// </summary>
        public static List<CommandRecord> ToCopied(IReadOnlyList<CommandRecord> planned, ICollection<int> skip = null, CopyMode mode = CopyMode.Current)
        {
            var list = new List<CommandRecord>();
            bool reverse = false;
            for (int i = 0; i < planned.Count; i++)
            {
                var r = planned[i];
                if (r.Command.type == CommandType.ReverseCopy) reverse = true;
                if (r.Command.notCopied || (skip != null && skip.Contains(i))) continue;
                var c = r.Clone();
                if (r.Command.copyMirrored) c.Direction = Opposite(c.Direction);
                if (CopyCore.IsMirror(mode) && r.Command.UsesDirection) c.Direction = Opposite(c.Direction); // 【Copy Core V2】B：反対向き
                list.Add(c);
            }
            if (reverse) list.Reverse();
            return list;
        }

        public static IReadOnlyList<CommandRecord> ApplyTrait(IReadOnlyList<CommandRecord> commands, CopyTrait trait)
        {
            if (trait == CopyTrait.Normal) return commands;
            var result = new List<CommandRecord>();
            foreach (var c in commands)
            {
                var copy = c.Clone();
                if (trait == CopyTrait.Mirror) copy.Direction = Opposite(copy.Direction);
                if (trait == CopyTrait.MirrorAttacks && IsReflected(copy.Command.type)) copy.Direction = Opposite(copy.Direction);
                result.Add(copy);
            }
            if (trait == CopyTrait.Reverse) result.Reverse();
            return result;
        }

        /// <summary>【検証中】攻撃だけ反転のとき、方向を逆にする命令（攻撃・押す・引く）。移動はそのまま。</summary>
        public static bool IsReflected(CommandType t) => t == CommandType.Slash || t == CommandType.PushSlash || t == CommandType.Pierce ||
                                                 t == CommandType.Sweep || t == CommandType.Push || t == CommandType.Pull;

        static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return Direction.Down;
                case Direction.Down: return Direction.Up;
                case Direction.Left: return Direction.Right;
                default: return Direction.Left;
            }
        }
    }
}
