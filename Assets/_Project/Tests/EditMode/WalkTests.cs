using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>技「歩く」。</summary>
    public class WalkTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void WalkMovesOneCell()
        {
            b = new TestBattle(".....\n.P.E.\n.....");
            var executor = new CommandExecutor(b.Grid);
            executor.Execute(b.Player, new CommandRecord(b.Walk, Direction.Up, 1));
            Assert.AreEqual(new Vector2Int(1, 2), b.Player.GridPosition);
        }

        [Test]
        public void WalkIntoWallDoesNothing()
        {
            b = new TestBattle("P#.E");
            var executor = new CommandExecutor(b.Grid);
            string log = executor.Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1));
            Assert.AreEqual(new Vector2Int(0, 0), b.Player.GridPosition);
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void WalkOffBoardDoesNothing()
        {
            b = new TestBattle("P..E");
            var executor = new CommandExecutor(b.Grid);
            executor.Execute(b.Player, new CommandRecord(b.Walk, Direction.Left, 1));
            Assert.AreEqual(new Vector2Int(0, 0), b.Player.GridPosition);
        }

        [Test]
        public void WalkIntoUnitDoesNothing()
        {
            b = new TestBattle(".PE.");
            var executor = new CommandExecutor(b.Grid);
            executor.Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1));
            Assert.AreEqual(new Vector2Int(1, 0), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(2, 0), b.Enemy.GridPosition);
        }

        [Test]
        public void PlayerWalksDuringPlayerTurn()
        {
            b = new TestBattle(".....\n.P.E.\n.....");
            b.Add(b.Walk, Direction.Down);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 0), b.Player.GridPosition);
        }
    }
}
