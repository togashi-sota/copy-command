using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>技「突進」（距離 2〜3）。</summary>
    public class RushTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void RushMovesChosenDistance()
        {
            b = new TestBattle("P.....\n......\n.....E");
            var ex = new CommandExecutor(b.Grid);
            ex.Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 2));
            Assert.AreEqual(new Vector2Int(2, 2), b.Player.GridPosition);
            ex.Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 3));
            Assert.AreEqual(new Vector2Int(5, 2), b.Player.GridPosition);
        }

        [Test]
        public void RushStopsBeforeWall()
        {
            b = new TestBattle("P.#...\n.....E");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 3));
            Assert.AreEqual(new Vector2Int(1, 1), b.Player.GridPosition);
            StringAssert.Contains("1マス進んで停止", log);
        }

        [Test]
        public void RushStopsBeforeUnit()
        {
            b = new TestBattle("P..E..");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 3));
            Assert.AreEqual(new Vector2Int(2, 0), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition, "ぶつかられた側は動かない");
            Assert.AreEqual(12, b.Enemy.Hp, "衝突ダメージなし");
        }

        [Test]
        public void RushStopsAtBoardEdge()
        {
            b = new TestBattle(".P\n.E");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 2));
            Assert.AreEqual(new Vector2Int(1, 1), b.Player.GridPosition);
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void EnemyGetsInputDistanceEvenIfPlayerWasStoppedEarly()
        {
            // プレイヤーは突進3を壁で1マスしか進めない。敵には「突進3」が渡り、敵は3マス進む
            b = new TestBattle("P.#....\n.......\nE......");
            b.Add(b.Rush, Direction.Right, 3);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 2), b.Player.GridPosition);
            Assert.AreEqual(3, b.Turn.EnemyReplay.CopiedCommands[0].Distance);
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
        }
    }
}
