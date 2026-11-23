using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>トゲ：入った瞬間に5ダメージ。Player / Enemy 共通。</summary>
    public class SpikeTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void MapSymbolCreatesSpike()
        {
            b = new TestBattle("P^.E");
            Assert.IsNotNull(b.Grid.GetHazardAt(new Vector2Int(1, 0)));
            Assert.IsFalse(b.Grid.IsBlocked(new Vector2Int(1, 0)), "トゲは床なので入れる");
        }

        [Test]
        public void WalkingOntoSpikeDealsFive()
        {
            b = new TestBattle("P^.E");
            string log = new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1));
            Assert.AreEqual(7, b.Player.Hp);
            StringAssert.Contains("トゲ", log);
        }

        [Test]
        public void StandingOnSpikeDoesNotHurtAgain()
        {
            b = new TestBattle("P^...\n....E");
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(7, b.Player.Hp);
            b.Add(b.Slash, Direction.Up); // その場で空振り。移動しない
            b.RunTurn();
            Assert.AreEqual(7, b.Player.Hp, "立っているだけでは追加ダメージなし");
        }

        [Test]
        public void RushThroughSpikesTakesDamageForEachAndKeepsMoving()
        {
            b = new TestBattle("P^^..\n....E");
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Rush, Direction.Right, 3));
            Assert.AreEqual(new Vector2Int(3, 1), b.Player.GridPosition, "移動は続く");
            Assert.AreEqual(2, b.Player.Hp, "トゲ2つで 5×2 ダメージ");
        }

        [Test]
        public void DyingMidRushStopsOnTheSpike()
        {
            b = new TestBattle("P^^..\n....E", playerHp: 5);
            b.Add(b.Rush, Direction.Right, 3);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 1), b.Player.GridPosition, "その場で死亡");
            Assert.AreEqual(BattleState.GameOver, b.Turn.State);
        }

        [Test]
        public void EnemyRushesIntoSpikeByCopyingPlayer()
        {
            // STAGE 2 の核：プレイヤーの「突進 →右 2」を敵がコピーし、敵の進路にあるトゲへ自分から入る
            b = new TestBattle("P......\n.......\n...E.^.");
            b.Add(b.Rush, Direction.Right, 2);
            b.RunTurn();
            Assert.AreEqual(12, b.Player.Hp, "プレイヤーの進路にトゲは無い");
            Assert.AreEqual(new Vector2Int(5, 0), b.Enemy.GridPosition);
            Assert.AreEqual(7, b.Enemy.Hp, "敵はトゲで5ダメージ");
        }

        [Test]
        public void ReenteringTheSameSpikeHurtsEveryTime()
        {
            // 2026-10-03 本人決定：トゲは入るたびに毎回ダメージ（同じトゲ・同じターンでも）
            b = new TestBattle("P^..\n...E", playerHp: 20);
            var ex = new CommandExecutor(b.Grid);
            ex.Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1)); // 入る
            ex.Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1)); // 出る
            ex.Execute(b.Player, new CommandRecord(b.Walk, Direction.Left, 1));  // もう一度入る
            Assert.AreEqual(10, b.Player.Hp, "5＋5");
        }

        [Test]
        public void SpikeDamageComesFromStageData()
        {
            b = new TestBattle("P^.E");
            b.Stage.spikeDamage = 3;
            b.Turn.LoadStage(0);
            new CommandExecutor(b.Grid).Execute(b.Player, new CommandRecord(b.Walk, Direction.Right, 1));
            Assert.AreEqual(9, b.Player.Hp);
        }
    }
}
