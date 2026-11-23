using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>技「押し出し」。</summary>
    public class PushTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void PushMovesAdjacentUnitOneCellAndPusherStays()
        {
            b = new TestBattle(".PE..");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Push, Direction.Right, 0));
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
            Assert.AreEqual(new Vector2Int(1, 0), b.Player.GridPosition, "押した側は動かない");
            Assert.AreEqual(12, b.Enemy.Hp);
        }

        [Test]
        public void PushIntoWallFails()
        {
            b = new TestBattle(".PE#.");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Push, Direction.Right, 0));
            Assert.AreEqual(new Vector2Int(2, 0), b.Enemy.GridPosition);
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void PushOffBoardFails()
        {
            b = new TestBattle("..PE");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Push, Direction.Right, 0));
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
        }

        [Test]
        public void PushWithNobodyAdjacentIsWhiff()
        {
            b = new TestBattle(".P.E.");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Push, Direction.Right, 0));
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void PushOntoSpikeDealsFive()
        {
            b = new TestBattle(".PE^.");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Push, Direction.Right, 0));
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
            Assert.AreEqual(7, b.Enemy.Hp);
        }

        [Test]
        public void CopiedPushCanHitThePlayer()
        {
            // 敵の右隣にいる状態で「押し出し →右」を使う → 自分は空振り、敵の「押し出し →右」で自分がトゲへ押される
            b = new TestBattle(".EP^.");
            b.Add(b.Push, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(3, 0), b.Player.GridPosition);
            Assert.AreEqual(7, b.Player.Hp);
        }

        [Test]
        public void PushKillOnSpikeClearsStage()
        {
            b = new TestBattle(".PE^.", enemyHp: 5);
            b.Add(b.Push, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(BattleState.Cleared, b.Turn.State);
        }
    }
}
