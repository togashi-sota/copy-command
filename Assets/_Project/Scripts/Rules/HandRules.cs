using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>【RC6】手札の札の役割（色）。移動＝青・攻撃＝赤・操作＝紫（押す・引く・入れ替え…）・そのほか（支援・手札を回す札）。</summary>
    public enum HandRole { Move, Attack, Control, Other }

    /// <summary>【RC6】命令札の手札の作り方（本人の実プレイ「攻撃したいのに攻撃できない・逃げたいのに移動できない」）。</summary>
    public enum HandShape
    {
        /// <summary>RC5 まで：7枚まで補充・使わなかった札は残る（山札の上から引くだけ）。</summary>
        Legacy,
        /// <summary>A 案・足りないときだけ直す：補充の途中で、移動・攻撃が最低枚数に届かなくなりそうなら、その役割の札を山札から（無ければ捨て札から）引く。紫は手札に ControlSoftCap 枚あれば飛ばす。</summary>
        Guarantee,
        /// <summary>B 案・小さな山に分ける：毎ターン、移動・攻撃・操作の山から目標の枚数ずつ（移動3・攻撃3・操作1まで）。持ちすぎた紫はターンの始めに捨て札へ。</summary>
        Pools,
        /// <summary>D＋E 案：戦闘の始めだけ引き直し（移動か攻撃が2枚未満なら）。あとは山札の上から（手札整理で直す）。</summary>
        Mulligan,
    }

    /// <summary>
    /// 【RC6】手札の決まり（命令札の役割の保証・紫の目安・緑の戦術札の上限）。ゲームとシミュレーター（tools/pzsearch）で同じ。
    /// 実験の切り替えは解析だけ。ゲームは TurnManager がステージ読み込みのたびに ResetExperiments() で既定に戻す。
    /// </summary>
    public static class HandRules
    {
        /// <summary>ゲームの既定。A 案（足りないときだけ直す）・緑1枚：比べた結果は docs/final/RC6_DESIGN.md 2章（Logs/rc6/hand）。</summary>
        public const HandShape DefaultMode = HandShape.Guarantee;
        public const int DefaultMinMove = 2, DefaultMinAttack = 2, DefaultControlSoftCap = 1, DefaultTacticHandMax = 1;

        public static HandShape Mode = DefaultMode;
        /// <summary>手札に最低限ほしい移動・攻撃の枚数（デッキにあれば）。</summary>
        public static int MinMove = DefaultMinMove, MinAttack = DefaultMinAttack;
        /// <summary>紫（操作）の目安：手札にこの枚数あれば、引くときに紫を飛ばす（山札に残る）。</summary>
        public static int ControlSoftCap = DefaultControlSoftCap;
        /// <summary>緑の戦術札の手札の上限。</summary>
        public static int TacticHandMax = DefaultTacticHandMax;

        public static void ResetExperiments()
        {
            Mode = DefaultMode;
            MinMove = DefaultMinMove; MinAttack = DefaultMinAttack; ControlSoftCap = DefaultControlSoftCap;
            TacticHandMax = DefaultTacticHandMax;
        }

        /// <summary>札の役割（UiStyles.CategoryOf と同じ分け方：攻撃＝赤・押し引き＝紫・支援と手札＝そのほか・残りは移動）。</summary>
        public static HandRole RoleOf(CommandData d)
        {
            if (d == null) return HandRole.Other;
            switch (d.type)
            {
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Pierce:
                case CommandType.Sweep:
                case CommandType.AroundSlash: return HandRole.Attack;
                case CommandType.Push:
                case CommandType.Pull:
                case CommandType.Swap:
                case CommandType.Aim:
                case CommandType.PlaceSpike: return HandRole.Control;
                case CommandType.Spirit:
                case CommandType.Foresight:
                case CommandType.Reconsider:
                case CommandType.Rally:
                case CommandType.ReverseCopy:
                case CommandType.Draw:
                case CommandType.Insight:
                case CommandType.Resonate:
                case CommandType.Tactic:
                case CommandType.Heal: return HandRole.Other;
                default: return HandRole.Move;
            }
        }

        public static int Count(IEnumerable<CardInstance> cards, HandRole role) => cards.Count(c => RoleOf(c.Data) == role);
    }
}
