using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>今どの画面を出しているか（RUN モード）。</summary>
    public enum RunScreen
    {
        None,     // RUN ではない（チュートリアル）
        RunStart, // RUN の説明と開始ボタン
        Battle,   // RUN BATTLE（勝敗が決まったら結果を出して「次へ」）
        Reward,   // カード報酬
        Rest,     // 休憩
        Clear,    // RUN CLEAR
        Failed,   // RUN 失敗（ゲームオーバー）
    }

    /// <summary>
    /// RUN（連戦）の進行役。RunState（データ）を持ち、戦闘は TurnManager に任せる。
    /// 進行：RUN START → BATTLE 1 → 報酬 → BATTLE 2 → 報酬 → 休憩 → BATTLE 3 → RUN CLEAR
    /// </summary>
    public class RunManager : MonoBehaviour
    {
        [SerializeField] TurnManager turn;

        [Header("RUN の設定（仮値。docs/RUN_DESIGN.md）")]
        [SerializeField] List<StageData> runBattles = new List<StageData>();
        [SerializeField] List<CommandData> initialDeck = new List<CommandData>();
        [Tooltip("報酬の候補。ここから違う種類を3枚ランダムに出す")]
        [SerializeField] List<CommandData> rewardPool = new List<CommandData>();
        [Tooltip("基本命令「歩く」（カードではない）")]
        [SerializeField] CommandData walkCommand;
        [SerializeField] int maxHp = 12;
        [Tooltip("休憩で回復する HP（最大 HP 12 の 1/3）")]
        [SerializeField] int restHeal = 4;
        [Tooltip("乱数の種。0 なら毎回ちがう RUN")]
        [SerializeField] int seed = 0;

        public RunState Run { get; private set; }
        public RunScreen Screen { get; private set; } = RunScreen.None;
        public bool InRun => Screen != RunScreen.None;
        public int RestHeal => restHeal;
        public int MaxHp => maxHp;
        public IReadOnlyList<CommandData> InitialDeck => initialDeck;
        public IReadOnlyList<StageData> RunBattles => runBattles;
        public TurnManager Turn => turn;

        /// <summary>RUN の説明画面を開く（チュートリアルの後、または「RUN」ボタン）。</summary>
        public void OpenRunStart()
        {
            Screen = RunScreen.RunStart;
        }

        /// <summary>RUN 開始：HP 全快、初期デッキを用意して最初の戦闘へ。</summary>
        public void StartRun()
        {
            var rng = seed == 0 ? new System.Random() : new System.Random(seed);
            Run = new RunState(maxHp, initialDeck, RunState.DefaultRoute, rng);
            EnterCurrentNode();
        }

        void EnterCurrentNode()
        {
            switch (Run.CurrentNode)
            {
                case RunNodeType.Battle:
                    Screen = RunScreen.Battle;
                    turn.LoadRunBattle(runBattles[Run.BattleIndex], Run.Hp, Run.MaxHp, Run.Deck, walkCommand, Run.Rng);
                    break;
                case RunNodeType.Reward:
                    Run.RollRewards(rewardPool);
                    Screen = RunScreen.Reward;
                    break;
                case RunNodeType.Rest:
                    Screen = RunScreen.Rest;
                    break;
                case RunNodeType.Clear:
                    Screen = RunScreen.Clear;
                    break;
            }
        }

        /// <summary>戦闘の結果画面（BattleUI）で「次へ」：勝ったら HP を持ち越して次へ、負けたら RUN 終了。</summary>
        public void ProceedAfterBattle()
        {
            if (Screen != RunScreen.Battle) return;
            if (turn.State == BattleState.Cleared)
            {
                Run.WinBattle(turn.Player.Hp, turn.TurnNumber);
                EnterCurrentNode();
            }
            else if (turn.State == BattleState.GameOver)
            {
                Run.LoseBattle(turn.TurnNumber);
                Screen = RunScreen.Failed;
            }
        }

        public void TakeReward(CommandData card)
        {
            if (Run.TakeReward(card)) EnterCurrentNode();
        }

        public void SkipReward()
        {
            if (Run.SkipReward()) EnterCurrentNode();
        }

        public void Rest()
        {
            Run.Rest(restHeal);
            EnterCurrentNode();
        }

        /// <summary>チュートリアルへ戻る。</summary>
        public void ExitToTutorial()
        {
            Screen = RunScreen.None;
            Run = null;
            turn.LoadStage(0);
        }

        /// <summary>テスト用。</summary>
        public void SetupForTest(TurnManager turnManager, List<StageData> battles, List<CommandData> deck, List<CommandData> pool,
            CommandData walk, int hp = 12, int heal = 4, int randomSeed = 1)
        {
            turn = turnManager;
            runBattles = battles;
            initialDeck = deck;
            rewardPool = pool;
            walkCommand = walk;
            maxHp = hp;
            restHeal = heal;
            seed = randomSeed;
        }
    }
}
