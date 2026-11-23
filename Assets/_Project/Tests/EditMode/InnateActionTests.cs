using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>敵の固有行動「周囲斬り」と、ENEMY TURN の順番（固有行動 → コピー命令）。</summary>
    public class InnateActionTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [TestCase("...\n.E.\n.P.", TestName = "周囲斬り_下の隣に当たる")]
        [TestCase(".P.\n.E.\n...", TestName = "周囲斬り_上の隣に当たる")]
        [TestCase("...\nPE.\n...", TestName = "周囲斬り_左の隣に当たる")]
        [TestCase("...\n.EP\n...", TestName = "周囲斬り_右の隣に当たる")]
        public void AroundSlashHitsAllFourNeighbors(string map)
        {
            b = new TestBattle(map, innate: true);
            new CommandExecutor(b.Grid).Execute(b.Enemy, new CommandRecord(b.AroundSlash, Direction.Up, 0));
            Assert.AreEqual(8, b.Player.Hp);
        }

        [Test]
        public void AroundSlashMissesDiagonal()
        {
            b = new TestBattle("P..\n.E.\n...", innate: true);
            string log = new CommandExecutor(b.Grid).Execute(b.Enemy, new CommandRecord(b.AroundSlash, Direction.Up, 0));
            Assert.AreEqual(12, b.Player.Hp, "斜めには当たらない");
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void InnateHitsWhenPlayerEndsTurnNextToEnemy()
        {
            // 固有行動はコピーより先：敵の隣でターンを終えると、敵がコピーで離れる前に斬られる
            b = new TestBattle(".P.E..", innate: true);
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(8, b.Player.Hp);
            Assert.AreEqual(new Vector2Int(4, 0), b.Enemy.GridPosition, "そのあとコピーの「歩く →右」を実行");
        }

        [Test]
        public void HitAndAwayAvoidsInnate()
        {
            // 斬ってから逃げる（ヒット＆アウェイ）：周囲斬りの時点で隣にいないので当たらない
            b = new TestBattle("..PE..", innate: true);
            b.Add(b.Slash, Direction.Right);
            b.Add(b.Walk, Direction.Left);
            b.RunTurn();
            Assert.AreEqual(8, b.Enemy.Hp);
            Assert.AreEqual(12, b.Player.Hp);
            Assert.AreEqual(new Vector2Int(2, 0), b.Enemy.GridPosition, "コピーの「歩く ←」で敵は付いてくる");
        }

        [Test]
        public void InnateIsNotCopiedAndNotInHand()
        {
            b = new TestBattle("..PE..", innate: true);
            Assert.IsFalse(b.Turn.CanAddCommand(b.AroundSlash), "プレイヤーは使えない");
            b.Add(b.Walk, Direction.Left);
            b.RunTurn();
            Assert.AreEqual(1, b.Turn.EnemyReplay.CopiedCommands.Count, "コピーされるのはプレイヤーの命令だけ");
        }

        [Test]
        public void InnateKillEndsBattleBeforeCopy()
        {
            b = new TestBattle(".P.E..", playerHp: 4, innate: true);
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(BattleState.GameOver, b.Turn.State);
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition, "コピー命令は実行されない");
        }

        [Test]
        public void DangerCellsAreFourNeighborsInsideBoard()
        {
            b = new TestBattle("E#.\n...\n..P", innate: true);
            var cells = b.Turn.GetInnateDangerCells();
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(0, 1) }, cells, "盤面外と壁は除く");
            Assert.IsTrue(b.Turn.InnatePending);
        }

        [Test]
        public void NoInnateMeansNoDangerCells()
        {
            b = new TestBattle("..PE..");
            Assert.AreEqual(0, b.Turn.GetInnateDangerCells().Count);
            Assert.IsFalse(b.Turn.InnatePending);
        }
    }
}
