using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中・Copy Core V3（2026-10-05）】敵ごとに「何を写すか」。写し方そのものは今のまま（同じ札・同じ向き・同じ距離）。
    /// 本人：「反転を考えること自体で頭がこんがらがる」→ 規則は簡単なまま、敵の種類でコピーの使い道を変える。
    /// </summary>
    public enum CopyKind
    {
        /// <summary>全写し：あなたの命令を全部写す（今までの敵）。</summary>
        All,
        /// <summary>技写し：移動の札（青）以外だけ写す。歩かないので、その場から攻撃する「装置」になる。</summary>
        Technique,
        /// <summary>歩写し：移動の札（青）だけ写す。攻撃はしないので、踏み板・トゲ・ワープへ運ぶ「駒」になる。</summary>
        Move,
        /// <summary>写さない（ボスの本体など）。</summary>
        None,
    }

    public static class CopyKinds
    {
        /// <summary>移動の札（カードの色が青）。UiStyles.CategoryOf の「移動」と同じ（テストで一致を確かめる）。</summary>
        public static bool IsMoveType(CommandType t)
        {
            switch (t)
            {
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Pierce:
                case CommandType.Sweep:
                case CommandType.AroundSlash:
                case CommandType.Push:
                case CommandType.Pull:
                case CommandType.Swap:
                case CommandType.PlaceSpike:
                case CommandType.Spirit:
                case CommandType.Foresight:
                case CommandType.Reconsider:
                case CommandType.Rally:
                case CommandType.ReverseCopy:
                case CommandType.Heal:
                case CommandType.Draw:
                case CommandType.Insight:
                case CommandType.Resonate:
                case CommandType.Tactic:
                case CommandType.Aim: return false;
                default: return true;
            }
        }

        /// <summary>この敵は、この札を写すか。</summary>
        public static bool Accepts(UnitModel u, CommandData c)
        {
            if (u == null || c == null || u.Dormant) return false;
            if (u.NoMoveCopy && IsMoveType(c.type)) return false; // 【RC6】孤立した敵は移動をまねない
            if (u.ForceAllCopy) return true; // 【Combat V4】模倣命令
            switch (u.CopyKind)
            {
                case CopyKind.None: return false;
                case CopyKind.Technique: return !IsMoveType(c.type);
                case CopyKind.Move: return IsMoveType(c.type);
                default: return true;
            }
        }

        /// <summary>その敵が写す命令だけを残す（順番は変えない）。</summary>
        public static IReadOnlyList<CommandRecord> Filter(UnitModel u, IReadOnlyList<CommandRecord> cmds)
        {
            if (u == null || cmds == null || ((u.CopyKind == CopyKind.All || u.ForceAllCopy) && !u.Dormant && !u.NoMoveCopy)) return cmds;
            return cmds.Where(r => r != null && Accepts(u, r.Command)).ToList();
        }

        /// <summary>盤面・名前の横に出す短い名前。</summary>
        public static string Short(CopyKind k) => k switch
        {
            CopyKind.Technique => "技まね",
            CopyKind.Move => "移動まね",
            CopyKind.None => "まねない",
            _ => "全まね",
        };

        /// <summary>【Puzzle v2】文字の印（画面では剣・足あとの絵を BattleUI.DrawCopyKindIcon で描く。絵文字はフォントに無く □ になった）。</summary>
        public static string Icon(CopyKind k) => k switch
        {
            CopyKind.Technique => "技",
            CopyKind.Move => "移",
            CopyKind.None => "×",
            _ => "∞",
        };

        /// <summary>盤面の印（1文字）。</summary>
        public static string Badge(CopyKind k) => k switch
        {
            CopyKind.Technique => "技",
            CopyKind.Move => "移",
            CopyKind.None => "無",
            _ => "",
        };

        /// <summary>一文の説明（敵の説明・チュートリアル）。</summary>
        public static string OneLine(CopyKind k) => k switch
        {
            CopyKind.Technique => "技まね：歩かない。あなたの技の札（攻撃・押す・引くなど、青以外）だけを、その場から同じ向きでまねる。",
            CopyKind.Move => "移動まね：あなたの移動の札（青い札）だけを、同じ向き・同じ距離でまねる。攻撃はしない。",
            CopyKind.None => "まねない：あなたの命令をまねない。",
            _ => "全まね：あなたの札を全部、同じ向き・同じ距離でまねる。",
        };

        /// <summary>ステージの指定（読み順に1文字：A 全写し／T 技写し／M 歩写し／N 写さない。空白は無視）。</summary>
        public static CopyKind? Parse(char c) => char.ToUpperInvariant(c) switch
        {
            'A' => CopyKind.All,
            'T' => CopyKind.Technique,
            'M' => CopyKind.Move,
            'N' => CopyKind.None,
            _ => (CopyKind?)null,
        };
    }
}
