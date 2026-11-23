using System;

namespace CopyCommand
{
    /// <summary>
    /// 実際に使った命令1つ分。「技・方向・距離」を保存する。
    /// 敵へコピーされるのはこれ。結果（実際に何マス進めたか等）ではなく、入力した内容をそのまま持つ。
    /// </summary>
    [Serializable]
    public class CommandRecord
    {
        public CommandData Command;
        public Direction Direction;
        public int Distance;

        /// <summary>RUN モード：この命令に使った手札のカード（歩くなら null）。敵へのコピーには含めない。</summary>
        [NonSerialized] public CardInstance SourceCard;

        public CommandRecord(CommandData command, Direction direction, int distance, CardInstance sourceCard = null)
        {
            Command = command;
            Direction = direction;
            Distance = distance;
            SourceCard = sourceCard;
        }

        /// <summary>敵へ渡すための複製（カードの実体は渡さない。渡るのは「技・方向・距離」だけ）。</summary>
        /// <summary>【RC6】まね＋・まね－の札から写した命令：敵のまねのダメージに足す量（写すときに札から移す。札の実体は渡さない）。</summary>
        public int CopyDelta;

        public CommandRecord Clone() => new CommandRecord(Command, Direction, Distance) { CopyDelta = SourceCard != null ? SourceCard.CopyDelta : CopyDelta }; // 【RC6】まね＋・まね－は写しに数だけ渡す

        /// <summary>表示用。例：「突進 →右 2マス」「斬撃 ↑上」</summary>
        public string ToDisplayText()
        {
            if (!Command.UsesDirection) return Command.displayName;
            string dir = Command.type == CommandType.Diagonal ? Direction.ToDiagonalLabel() : Direction.ToLabel();
            string text = $"{Command.displayName} {dir}";
            if (Command.maxDistance > 1) text += $" {Distance}マス";
            return text;
        }
    }
}
