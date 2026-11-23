using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>Phase D〜G：カード報酬・HP 持ち越し・休憩・RUN の進行。</summary>
    public class RunTests
    {
        readonly List<Object> created = new List<Object>();
        GameObject root;
        TurnManager turn;
        RunManager run;
        CommandData slash, rush, push, thrust, walk, innate;

        T Make<T>() where T : ScriptableObject { var o = ScriptableObject.CreateInstance<T>(); created.Add(o); return o; }

        CommandData Card(string name, CommandType type, int damage = 0, int min = 0, int max = 0, int range = 1)
        {
            var c = Make<CommandData>();
            c.displayName = name; c.type = type; c.damage = damage; c.minDistance = min; c.maxDistance = max; c.range = range;
            return c;
        }

        StageData Stage(string map, int enemyHp, CommandData innateAction = null)
        {
            var s = Make<StageData>();
            s.map = map; s.enemyMaxHp = enemyHp; s.maxSlots = 3; s.spikeDamage = 2; s.enemyInnateAction = innateAction;
            return s;
        }

        [SetUp]
        public void SetUp()
        {
            slash = Card("斬撃", CommandType.Slash, damage: 4);
            rush = Card("突進", CommandType.Rush, min: 2, max: 3);
            push = Card("押し出し", CommandType.Push);
            thrust = Card("突き", CommandType.Slash, damage: 3, range: 2);
            walk = Card("歩く", CommandType.Walk, min: 1, max: 1);
            innate = Card("周囲斬り", CommandType.AroundSlash, damage: 4);

            root = new GameObject("RunTest");
            var grid = root.AddComponent<GridManager>();
            turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData>());
            run = root.AddComponent<RunManager>();
            // 3戦とも「隣の敵（HP4）を斬撃1回で倒せる」盤面。周囲斬りあり（倒し損ねると斬られる）
            var battles = new List<StageData> { Stage("PE..", 4, innate), Stage(".PE.", 4, innate), Stage("..PE", 4, innate) };
            var deck = Enumerable.Repeat(slash, 8).ToList(); // テストでは手札を確実にするため斬撃だけ
            run.SetupForTest(turn, battles, deck, new List<CommandData> { slash, rush, push, thrust }, walk, hp: 12, heal: 4, randomSeed: 3);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var o in created) Object.DestroyImmediate(o);
        }

        void WinCurrentBattleWithSlash()
        {
            var card = turn.Deck.Hand.First(c => c.Data == slash);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(slash, Direction.Right, 0, card)));
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(BattleState.Cleared, turn.State);
            run.ProceedAfterBattle();
        }

        [Test]
        public void RunStartsWithFullHpAndInitialDeckInBattle1()
        {
            run.OpenRunStart();
            Assert.AreEqual(RunScreen.RunStart, run.Screen);
            run.StartRun();
            Assert.AreEqual(RunScreen.Battle, run.Screen);
            Assert.AreEqual(0, run.Run.BattleIndex);
            Assert.AreEqual(12, run.Run.Hp);
            Assert.AreEqual(8, run.Run.Deck.Count);
            Assert.IsTrue(turn.IsDeckMode);
            Assert.AreEqual(5, turn.Deck.Hand.Count);
            Assert.AreEqual(3, turn.MaxSlots);
        }

        [Test]
        public void WinningGoesToRewardAndTakingAddsCard()
        {
            run.StartRun();
            WinCurrentBattleWithSlash();
            Assert.AreEqual(RunScreen.Reward, run.Screen);
            Assert.AreEqual(3, run.Run.RewardOptions.Count);
            Assert.AreEqual(3, run.Run.RewardOptions.Distinct().Count(), "候補はちがう種類3枚");
            var pick = run.Run.RewardOptions[0];
            run.TakeReward(pick);
            Assert.AreEqual(9, run.Run.Deck.Count);
            Assert.AreEqual(RunScreen.Battle, run.Screen);
            Assert.AreEqual(1, run.Run.BattleIndex);
            Assert.AreEqual(9, turn.Deck.Hand.Count + turn.Deck.DrawPile.Count, "増えたカードは次の戦闘のデッキに入る");
        }

        [Test]
        public void SkippingRewardKeepsDeck()
        {
            run.StartRun();
            WinCurrentBattleWithSlash();
            run.SkipReward();
            Assert.AreEqual(8, run.Run.Deck.Count);
            Assert.AreEqual(RunScreen.Battle, run.Screen);
        }

        [Test]
        public void HpCarriesOverToNextBattle()
        {
            run.StartRun();
            // わざと歩いて周囲斬りを受ける（倒さない）→ HP 8 で次のターン、それから倒す
            Assert.IsTrue(turn.AddCommand(new CommandRecord(walk, Direction.Left, 1)));
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(8, turn.Player.Hp);
            WinCurrentBattleWithSlash();
            Assert.AreEqual(8, run.Run.Hp);
            run.SkipReward();
            Assert.AreEqual(8, turn.Player.Hp, "次の戦闘は HP 8 から");
            Assert.AreEqual(12, turn.Player.MaxHp);
        }

        [Test]
        public void RestHealsFourUpToMax()
        {
            var state = new RunState(12, new List<CommandData>(), new[] { RunNodeType.Battle, RunNodeType.Rest, RunNodeType.Clear }, new System.Random(1));
            state.WinBattle(5, 3);
            Assert.AreEqual(RunNodeType.Rest, state.CurrentNode);
            Assert.AreEqual(4, state.Rest(4));
            Assert.AreEqual(9, state.Hp);
            var full = new RunState(12, new List<CommandData>(), new[] { RunNodeType.Battle, RunNodeType.Rest, RunNodeType.Clear }, new System.Random(1));
            full.WinBattle(11, 1);
            Assert.AreEqual(1, full.Rest(4), "最大 HP を超えない");
        }

        [Test]
        public void FullRunReachesClearThroughRewardsAndRest()
        {
            run.StartRun();
            WinCurrentBattleWithSlash();                 // BATTLE 1
            run.TakeReward(run.Run.RewardOptions[0]);    // 報酬
            WinCurrentBattleWithSlash();                 // BATTLE 2
            run.SkipReward();                            // 報酬（取らない）
            Assert.AreEqual(RunScreen.Rest, run.Screen);
            run.Rest();                                  // 休憩
            Assert.AreEqual(RunScreen.Battle, run.Screen);
            Assert.AreEqual(2, run.Run.BattleIndex);
            WinCurrentBattleWithSlash();                 // BATTLE 3
            Assert.AreEqual(RunScreen.Clear, run.Screen);
            Assert.IsTrue(run.Run.IsCleared);
            Assert.AreEqual(3, run.Run.TurnsPerBattle.Count);
        }

        [Test]
        public void GameOverEndsRun()
        {
            run.StartRun();
            turn.Player.SetHp(4);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(walk, Direction.Left, 1))); // 倒さず隣に残る → 周囲斬り
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(BattleState.GameOver, turn.State);
            run.ProceedAfterBattle();
            Assert.AreEqual(RunScreen.Failed, run.Screen);
            Assert.IsTrue(run.Run.IsFailed);
        }

        [Test]
        public void BattlesUseDifferentStagesInOrder()
        {
            run.StartRun();
            var first = turn.CurrentStage;
            WinCurrentBattleWithSlash();
            run.SkipReward();
            Assert.AreNotSame(first, turn.CurrentStage);
        }

        [Test]
        public void ExitToTutorialLeavesRunMode()
        {
            var tutorial = Stage("P.E", 12);
            turn.SetupForTest(turn.Grid, new List<StageData> { tutorial });
            run.StartRun();
            run.ExitToTutorial();
            Assert.IsFalse(run.InRun);
            Assert.IsFalse(turn.IsDeckMode);
        }
    }
}
