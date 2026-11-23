using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>STEP 7：敵がコピーした命令を、自分の位置から再生すること。</summary>
    public class EnemyReplayTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void EnemyWalksSameDirectionFromItsOwnPosition()
        {
            b = new TestBattle(".......\n.P...E.\n.......");
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 2), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(5, 2), b.Enemy.GridPosition, "敵も上へ1マス");
        }

        [Test]
        public void EnemyDoesNotChaseThePlayer()
        {
            // プレイヤーが敵から離れる方向へ歩くと、敵も同じ方向（プレイヤーから離れない＝追いかけない）
            b = new TestBattle(".......\n..P.E..\n.......");
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(3, 1), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(5, 1), b.Enemy.GridPosition, "敵も右へ。プレイヤーへ近づく判断はしない");
        }

        [Test]
        public void EnemyCopyCanFailWhereThePlayerSucceeded()
        {
            // 敵の上は壁。プレイヤーは上へ歩けるが、敵は同じ命令で壁にぶつかる
            b = new TestBattle("....#\n.P..E");
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 1), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(4, 0), b.Enemy.GridPosition, "敵は空振り");
            StringAssert.Contains("空振り", b.Turn.Log[b.Turn.Log.Count - 1]);
        }

        [Test]
        public void ReplayRepeatsEveryTurn()
        {
            b = new TestBattle(".......\n.......\n.P...E.\n.......\n.......");
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            b.Add(b.Walk, Direction.Left);
            b.RunTurn();
            Assert.AreEqual(3, b.Turn.TurnNumber);
            Assert.AreEqual(new Vector2Int(0, 3), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(4, 3), b.Enemy.GridPosition);
        }
    }
}
