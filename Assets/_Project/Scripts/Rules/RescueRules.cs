using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC4・2026-10-23 本人の案「追い詰められたときの救済」】転移石（アイテム）：あなたを近くの空いたマスへ移す。
    /// 命令ではない（命令の枠を使わない・敵はまねない）。命令を組む前に使う。行けるのは、あなたから3マス以内（縦横の歩数）の空いた床。
    /// 壁・柱・敵・物（鏡核・樽…）のマスとワープの陣には行けない。トゲ・炎の上には行ける（画面で「このマスで -N」と出す）。入ると倒れるトゲは選べない。
    /// 【RC4・敵対的レビュー】使えるのは、ターンの初めにあなたが危ないマス（赤・赤紫・爆撃・床の予告）にいるときだけ。行き先は壁・柱を通らない3歩以内
    /// （前は縦横の差だけで、1ターン目に壁の向こう・盾兵の真後ろへ跳べた＝救済より攻めに強かった）。
    /// 3つの案（A 緑の札「転位」／B 決まった救済の札／C アイテム）を比べて C にした（docs/final/RC4_DESIGN.md 1-7）：
    /// 緑の札にすると毎ターンの選択肢に混ざって「強い移動」になり、まねを読む遊びを飛ばせる。アイテムなら1ランに数回まで（はじめに1つ・商人で買える・アイテムの枠は2つ）。
    /// </summary>
    public static class RescueRules
    {
        public const int Range = 3;

        /// <summary>今、転移石を使えるか（あなたが危ないマスにいる＝追い詰められている）。</summary>
        public static bool CanUse(BattleModel m) => m.Player != null && m.Player.IsAlive && (ThreatMap.IsThreatened(m, m.Player.Position) || m.Environment.FireCellsNow.Contains(m.Player.Position)); // 【RC5・レビュー】床の危険（今ターン）も：上の説明どおり

        /// <summary>使えないときの一言（使えるなら null）。</summary>
        public static string WhyNot(BattleModel m) =>
            !CanUse(m) ? "転移石は、あなたが赤・紫・床の危険など危ないマスにいるときだけ使える（追い詰められたときの救済）" : Targets(m).Count == 0 ? "転移できる空いたマスが無い" : null;

        /// <summary>このマスへ転移できるか。</summary>
        public static bool IsTarget(BattleModel m, Vector2Int c) => CanUse(m) && Reach(m).Contains(c) && Free(m, c);

        static bool Free(BattleModel m, Vector2Int c)
        {
            var p = m.Player;
            if (c == p.Position || !m.Board.IsInside(c) || m.IsBlocked(c) || m.Board.TryWarp(c, out _)) return false;
            if (m.Board.HasSpike(c) && m.Board.SpikeDamage >= p.Hp) return false; // 入ると倒れるトゲは選べない
            return true;
        }

        /// <summary>壁・柱・物を通らない3歩以内のマス（敵のいるマスは通れない・トゲは通れる）。</summary>
        static HashSet<Vector2Int> Reach(BattleModel m)
        {
            var start = m.Player.Position;
            var seen = new HashSet<Vector2Int> { start };
            var frontier = new List<Vector2Int> { start };
            for (int step = 0; step < Range; step++)
            {
                var next = new List<Vector2Int>();
                foreach (var c in frontier)
                    foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                    {
                        var n = c + d;
                        if (seen.Contains(n) || !m.Board.IsInside(n) || m.Board.IsWall(n) || m.GetUnitAt(n) != null) continue;
                        seen.Add(n);
                        next.Add(n);
                    }
                frontier = next;
            }
            seen.Remove(start);
            return seen;
        }

        /// <summary>転移できるマス（近い順）。</summary>
        public static List<Vector2Int> Targets(BattleModel m)
        {
            if (!CanUse(m)) return new List<Vector2Int>();
            var p = m.Player.Position;
            return Reach(m).Where(c => Free(m, c)).OrderBy(c => Mathf.Abs(c.x - p.x) + Mathf.Abs(c.y - p.y)).ThenBy(c => c.y).ThenBy(c => c.x).ToList();
        }

        /// <summary>転移する（トゲなら入ったときのダメージ・踏み板も踏む）。できなければ null。</summary>
        public static string Warp(BattleModel m, Vector2Int c, List<BattleEvent> events)
        {
            if (!IsTarget(m, c)) return null;
            var p = m.Player;
            var from = p.Position;
            string text = RulesEngine.EnterCell(m, p, c, p, null, false, events);
            RulesEngine.CheckPlates(m, events);
            return $"転移石：({from.x},{from.y}) → ({c.x},{c.y}){text}";
        }

        /// <summary>転移せずに何もしなかったら、このターンにあなたが受けるダメージ（ボットが使うかどうかを決める）。</summary>
        public static int DamageIfWarpedStay(BattleModel m)
        {
            var f = TurnForecast.Compute(m, new List<CommandRecord>());
            int sum = 0;
            foreach (var e in f.PlayerHits) sum += e.Amount;
            return sum;
        }

        /// <summary>このマスへ転移して何もしなかったら、このターンにあなたが受けるダメージ（画面の警告に使う）。</summary>
        public static int DamageIfWarped(BattleModel m, Vector2Int c)
        {
            var sim = m.Clone();
            var ev = new List<BattleEvent>();
            int hp = sim.Player.Hp;
            if (Warp(sim, c, ev) == null) return 0;
            int spike = hp - sim.Player.Hp;
            var f = TurnForecast.Compute(sim, new List<CommandRecord>());
            int later = 0;
            foreach (var e in f.PlayerHits) later += e.Amount;
            return spike + later;
        }
    }
}
