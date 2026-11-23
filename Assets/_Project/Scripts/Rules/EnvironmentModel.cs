using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】盤面の環境：炎床と回復ルーン。ランダムは使わない（必ず予告できる規則で動く）。
    /// - 炎床：ラウンドごとに決まった順番（FirePatterns）で燃えるマスが変わる。燃えるマスは PLAYER TURN の間ずっと予告され、
    ///   ENVIRONMENT PHASE（コピー → 兵種の行動 のあと）に、その上にいるユニット全員（プレイヤーも敵も）に FireDamage。
    ///   FireAfterKnightHalf なら、騎士（簡易ボス）の HP が半分以下になった「次のラウンド」から燃え始める（不意打ちにしない）。
    /// - 回復ルーン：ユニット（プレイヤーも敵も）がルーンのマスに入ると RuneHeal 回復し、ルーンは次の出現地点へ移る
    ///   （盤面の数字 1→2→3… の順に循環、次の地点も予告）。使えるのは RuneUses 回まで（永久回復を防ぐ）。
    /// </summary>
    public class EnvironmentModel
    {
        public List<HashSet<Vector2Int>> FirePatterns { get; private set; } = new List<HashSet<Vector2Int>>();
        public int FireDamage { get; set; } = 3;
        public bool FireAfterKnightHalf { get; set; }
        /// <summary>FireAfterKnightHalf のとき、炎が燃え始めたか（ラウンドの終わりに判定）。</summary>
        public bool FireArmed { get; set; }
        /// <summary>今のラウンド番号（1から）。ENVIRONMENT PHASE の最後に1つ進む。</summary>
        public int Round { get; set; } = 1;

        public List<Vector2Int> RuneCandidates { get; private set; } = new List<Vector2Int>();
        public int RuneIndex { get; set; }
        public int RuneUsesLeft { get; set; } = 3;
        public int RuneHeal { get; set; } = 3;
        /// <summary>【検証中】回復マス（一度踏むと回復して消える。あなたも敵も使える）。残っているマス。</summary>
        public HashSet<Vector2Int> HealTiles { get; private set; } = new HashSet<Vector2Int>();
        public int HealTileAmount { get; set; } = 4;

        public bool HasFire => FirePatterns.Count > 0;
        public bool HasRune => RuneCandidates.Count > 0;

        public bool FireActive => HasFire && (!FireAfterKnightHalf || FireArmed);

        /// <summary>そのラウンドに燃えるマス（炎が無効なら空）。</summary>
        public IReadOnlyCollection<Vector2Int> FireCells(int round) =>
            FireActive ? FirePatterns[(round - 1) % FirePatterns.Count] : (IReadOnlyCollection<Vector2Int>)new HashSet<Vector2Int>();

        /// <summary>このラウンドに燃えるマス。</summary>
        public IReadOnlyCollection<Vector2Int> FireCellsNow => FireCells(Round);

        /// <summary>次のラウンドに燃えるマス（予告の予告。薄く表示）。</summary>
        public IReadOnlyCollection<Vector2Int> FireCellsNext => FireCells(Round + 1);

        public Vector2Int? RuneCell => HasRune && RuneUsesLeft > 0 ? RuneCandidates[RuneIndex % RuneCandidates.Count] : (Vector2Int?)null;
        public Vector2Int? NextRuneCell => HasRune && RuneUsesLeft > 1 ? RuneCandidates[(RuneIndex + 1) % RuneCandidates.Count] : (Vector2Int?)null;

        /// <summary>ルーンを使った：次の地点へ移り、残り回数を1減らす。</summary>
        public void ConsumeRune()
        {
            RuneIndex++;
            RuneUsesLeft--;
        }

        public EnvironmentModel Clone() => new EnvironmentModel
        {
            FirePatterns = FirePatterns, // 変更されないので共有
            FireDamage = FireDamage,
            FireAfterKnightHalf = FireAfterKnightHalf,
            FireArmed = FireArmed,
            Round = Round,
            RuneCandidates = RuneCandidates,
            RuneIndex = RuneIndex,
            RuneUsesLeft = RuneUsesLeft,
            RuneHeal = RuneHeal,
            HealTiles = new HashSet<Vector2Int>(HealTiles),
            HealTileAmount = HealTileAmount,
        };

        /// <summary>解析の状態キー用（結果に関係する部分だけ）。</summary>
        public string StateKey()
        {
            if (!HasFire && !HasRune && HealTiles.Count == 0) return "";
            var sb = new StringBuilder("|");
            foreach (var h in HealTiles.OrderBy(c => c.x * 100 + c.y)) sb.Append('h').Append(h.x).Append(',').Append(h.y);
            if (HasFire) sb.Append((Round - 1) % FirePatterns.Count).Append(FireArmed ? "a" : "");
            if (HasRune) sb.Append('r').Append(RuneIndex % RuneCandidates.Count).Append(',').Append(RuneUsesLeft);
            return sb.ToString();
        }

        /// <summary>
        /// 炎のパターンを読む。1要素＝1ラウンド分。行は '/' で区切り、1行目が盤面のいちばん上、'F' が燃えるマス
        /// （盤面と同じ大きさで書く）。例："..F../..F../.....",
        /// </summary>
        public static List<HashSet<Vector2Int>> ParseFirePatterns(IEnumerable<string> patterns, int height)
        {
            var list = new List<HashSet<Vector2Int>>();
            if (patterns == null) return list;
            foreach (var p in patterns)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var rows = p.Split('/').Select(r => r.Trim()).ToArray();
                var cells = new HashSet<Vector2Int>();
                for (int i = 0; i < rows.Length; i++)
                    for (int x = 0; x < rows[i].Length; x++)
                        if (rows[i][x] == 'F') cells.Add(new Vector2Int(x, height - 1 - i));
                list.Add(cells);
            }
            return list;
        }

        public static EnvironmentModel Create(BoardModel board, StageData stage)
        {
            var env = new EnvironmentModel();
            env.RuneCandidates = board.RuneCandidates.ToList();
            env.HealTiles = new HashSet<Vector2Int>(board.HealTiles);
            if (stage == null) return env;
            env.HealTileAmount = stage.healTileAmount;
            env.FirePatterns = ParseFirePatterns(stage.firePatterns, board.Height);
            env.FireDamage = stage.fireDamage;
            env.FireAfterKnightHalf = stage.fireAfterKnightHalf;
            env.RuneUsesLeft = stage.runeUses;
            env.RuneHeal = stage.runeHeal;
            return env;
        }
    }
}
