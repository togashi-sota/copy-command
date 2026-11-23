using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>技「斬撃」。</summary>
    public class SlashTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void SlashHitsAdjacentUnitForFour()
        {
            b = new TestBattle(".PE.");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Slash, Direction.Right, 0));
            Assert.AreEqual(8, b.Enemy.Hp);
            StringAssert.Contains("4 ダメージ", log);
        }

        [Test]
        public void SlashEmptyCellIsWhiff()
        {
            b = new TestBattle(".P.E");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Slash, Direction.Right, 0));
            Assert.AreEqual(12, b.Enemy.Hp);
            StringAssert.Contains("空振り", log);
        }

        [Test]
        public void SlashOutsideBoardIsWhiff()
        {
            b = new TestBattle("P..E");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Slash, Direction.Left, 0));
            Assert.AreEqual(12, b.Player.Hp);
            Assert.AreEqual(12, b.Enemy.Hp);
        }

        [Test]
        public void CopiedSlashMissesWhenPlayerAttackedFromTheLeft()
        {
            // STAGE 1 の核：敵の左から「斬撃 →右」→ 命中。敵も「斬撃 →右」だが、プレイヤーは敵の左なので空振り
            b = new TestBattle(".....\n.PE..\n.....");
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(8, b.Enemy.Hp, "プレイヤーの斬撃は命中");
            Assert.AreEqual(12, b.Player.Hp, "敵の斬撃は空振り");
        }

        [Test]
        public void CopiedSlashHitsWhenPlayerSlashedAwayFromEnemy()
        {
            // 敵が左隣にいるのに「斬撃 →右」を使うと、自分は空振り・敵の「斬撃 →右」は自分に当たる
            b = new TestBattle(".....\n.EP..\n.....");
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(12, b.Enemy.Hp);
            Assert.AreEqual(8, b.Player.Hp);
        }

        [Test]
        public void KillingEnemyClearsStageAndSkipsRemainingCommands()
        {
            b = new TestBattle(".PE..", enemyHp: 4);
            b.Add(b.Slash, Direction.Right);
            b.Add(b.Walk, Direction.Left);
            b.RunTurn();
            Assert.AreEqual(BattleState.Cleared, b.Turn.State);
            Assert.AreEqual(new Vector2Int(1, 0), b.Player.GridPosition, "残りの「歩く」は実行されない");
        }

        [Test]
        public void PlayerKilledByCopiedSlashIsGameOver()
        {
            b = new TestBattle(".EP..", playerHp: 4);
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(BattleState.GameOver, b.Turn.State);
        }
    }
}
