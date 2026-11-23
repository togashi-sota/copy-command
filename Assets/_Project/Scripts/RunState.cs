using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>RUN の1マス（進行の1段階）。</summary>
    public enum RunNodeType
    {
        Battle,  // RUN BATTLE
        Reward,  // カード報酬（3枚から1枚 or 取らない）
        Rest,    // 休憩（HP 回復）
        Clear,   // RUN CLEAR
    }

    /// <summary>
    /// 1回の RUN の状態（ゲームの見た目とは独立したデータ。テストしやすいように MonoBehaviour にしない）。
    /// RUN をまたいで持ち越すもの：HP、デッキ内容。戦闘ごとにリセットするもの：山札・手札・捨て札（DeckState 側）。
    /// </summary>
    public class RunState
    {
        /// <summary>縦切り版の進行：BATTLE 1 → 報酬 → BATTLE 2 → 報酬 → 休憩 → BATTLE 3 → CLEAR。</summary>
        public static readonly RunNodeType[] DefaultRoute =
        {
            RunNodeType.Battle, RunNodeType.Reward,
            RunNodeType.Battle, RunNodeType.Reward,
            RunNodeType.Rest,
            RunNodeType.Battle,
            RunNodeType.Clear,
        };

        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public IReadOnlyList<CommandData> Deck => deck;
        public IReadOnlyList<RunNodeType> Route => route;
        public int NodeIndex { get; private set; }
        public RunNodeType CurrentNode => route[NodeIndex];
        /// <summary>今（または次）の戦闘が何戦目か（0始まり）。</summary>
        public int BattleIndex => route.Take(NodeIndex + 1).Count(n => n == RunNodeType.Battle) - 1;
        public int BattleCount => route.Count(n => n == RunNodeType.Battle);
        public bool IsFailed { get; private set; }
        public bool IsCleared => !IsFailed && CurrentNode == RunNodeType.Clear;
        public IReadOnlyList<CommandData> RewardOptions => rewardOptions;
        public System.Random Rng { get; }

        /// <summary>記録（RUN 終了画面・プレイテスト用）。</summary>
        public readonly List<int> TurnsPerBattle = new List<int>();
        public readonly List<int> DamagePerBattle = new List<int>();
        public readonly List<string> RewardHistory = new List<string>();

        readonly List<CommandData> deck = new List<CommandData>();
        readonly List<RunNodeType> route = new List<RunNodeType>();
        readonly List<CommandData> rewardOptions = new List<CommandData>();

        public RunState(int maxHp, IEnumerable<CommandData> initialDeck, IEnumerable<RunNodeType> routeNodes, System.Random rng)
        {
            MaxHp = maxHp;
            Hp = maxHp;               // RUN 開始時は HP 全快
            deck.AddRange(initialDeck);
            route.AddRange(routeNodes);
            Rng = rng;
            NodeIndex = 0;
        }

        /// <summary>戦闘に勝った：残り HP を持ち越して次へ。</summary>
        public void WinBattle(int hpLeft, int turns)
        {
            int before = Hp;
            Hp = System.Math.Max(0, System.Math.Min(MaxHp, hpLeft));
            TurnsPerBattle.Add(turns);
            DamagePerBattle.Add(before - Hp);
            Advance();
        }

        /// <summary>戦闘に負けた：RUN 終了。</summary>
        public void LoseBattle(int turns)
        {
            TurnsPerBattle.Add(turns);
            DamagePerBattle.Add(Hp);
            Hp = 0;
            IsFailed = true;
        }

        /// <summary>報酬候補を作る：候補の中から違う種類を count 枚ランダムに。</summary>
        public IReadOnlyList<CommandData> RollRewards(IList<CommandData> pool, int count = 3)
        {
            rewardOptions.Clear();
            var candidates = pool.Distinct().ToList();
            while (rewardOptions.Count < count && candidates.Count > 0)
            {
                int i = Rng.Next(candidates.Count);
                rewardOptions.Add(candidates[i]);
                candidates.RemoveAt(i);
            }
            return rewardOptions;
        }

        /// <summary>報酬を1枚取ってデッキに加える。</summary>
        public bool TakeReward(CommandData card)
        {
            if (CurrentNode != RunNodeType.Reward || !rewardOptions.Contains(card)) return false;
            deck.Add(card);
            RewardHistory.Add($"{card.displayName} を追加");
            rewardOptions.Clear();
            Advance();
            return true;
        }

        /// <summary>報酬を取らない（デッキを太らせないため）。</summary>
        public bool SkipReward()
        {
            if (CurrentNode != RunNodeType.Reward) return false;
            RewardHistory.Add("取らなかった");
            rewardOptions.Clear();
            Advance();
            return true;
        }

        /// <summary>休憩：HP を heal 回復（最大 HP まで）。</summary>
        public int Rest(int heal)
        {
            if (CurrentNode != RunNodeType.Rest) return 0;
            int before = Hp;
            Hp = System.Math.Min(MaxHp, Hp + heal);
            Advance();
            return Hp - before;
        }

        void Advance()
        {
            if (NodeIndex < route.Count - 1) NodeIndex++;
        }
    }
}
