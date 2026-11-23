using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 実際の STAGE 1 データ（Assets/_Project/Data/Stages/Stage1.asset）が、狙いどおりに遊べるか。
    /// 盤面を Inspector で変えたら、このテストが「狙いが崩れていないか」を教えてくれる。
    /// </summary>
    public class Stage1Tests
    {
        GameObject root;
        TurnManager turn;
        StageData stage;

        [SetUp]
        public void SetUp()
        {
            stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/Stage1.asset");
            Assert.IsNotNull(stage, "Stage1.asset が見つからない");
            root = new GameObject("Stage1Test");
            var grid = root.AddComponent<GridManager>();
            turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData> { stage });
            turn.LoadStage(0);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        CommandData Find(CommandType type) => stage.availableCommands.Find(c => c.type == type);

        [Test]
        public void Stage1UsesOnlyWalkAndSlashWithTwoSlots()
        {
            Assert.AreEqual(2, stage.maxSlots);
            CollectionAssert.AreEquivalent(new[] { CommandType.Walk, CommandType.Slash },
                stage.availableCommands.ConvertAll(c => c.type));
        }

        [Test]
        public void Stage1HasNoWallsOrSpikes()
        {
            foreach (var row in stage.GetMapRows())
            {
                StringAssert.DoesNotContain("#", row);
                StringAssert.DoesNotContain("^", row);
            }
        }

        [Test]
        public void PlayerStartsLeftOfEnemyOnTheSameRow()
        {
            Assert.AreEqual(turn.Player.GridPosition.y, turn.Enemy.GridPosition.y);
            Assert.Less(turn.Player.GridPosition.x, turn.Enemy.GridPosition.x);
        }

        [Test]
        public void IntendedSolutionWalkRightSlashRightClearsInThreeTurnsWithoutDamage()
        {
            for (int t = 0; t < 3; t++)
            {
                Assert.IsTrue(turn.AddCommand(new CommandRecord(Find(CommandType.Walk), Direction.Right, 1)));
                Assert.IsTrue(turn.AddCommand(new CommandRecord(Find(CommandType.Slash), Direction.Right, 0)));
                turn.ExecuteTurnImmediately();
            }
            Assert.AreEqual(BattleState.Cleared, turn.State);
            Assert.AreEqual(3, turn.TurnNumber);
            Assert.AreEqual(turn.Player.MaxHp, turn.Player.Hp, "敵の斬撃はすべて空振り");
        }

        [Test]
        public void EnemySlashWhiffCellIsVisibleOnBoardInTurnOne()
        {
            // 1ターン目、敵の斬撃が空振りしたマスが盤面の中にある（光って見える）こと
            turn.AddCommand(new CommandRecord(Find(CommandType.Walk), Direction.Right, 1));
            turn.AddCommand(new CommandRecord(Find(CommandType.Slash), Direction.Right, 0));
            turn.ExecuteTurnImmediately();
            Vector2Int whiffCell = turn.Enemy.GridPosition + Vector2Int.right;
            Assert.IsTrue(turn.Grid.IsInside(whiffCell));
        }
    }
}
