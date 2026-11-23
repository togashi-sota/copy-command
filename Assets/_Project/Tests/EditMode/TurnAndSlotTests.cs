using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>ターンの切り替えと命令スロットのルール。</summary>
    public class TurnAndSlotTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void BattleStartsOnPlayerTurnOne()
        {
            b = new TestBattle(".P.E.");
            Assert.AreEqual(BattleState.PlayerInput, b.Turn.State);
            Assert.AreEqual(1, b.Turn.TurnNumber);
        }

        [Test]
        public void CannotExecuteWithZeroCommands()
        {
            b = new TestBattle(".P.E.");
            Assert.IsFalse(b.Turn.CanExecute);
            b.RunTurn();
            Assert.AreEqual(1, b.Turn.TurnNumber, "0命令では実行されない");
        }

        [Test]
        public void SameCommandOnlyOncePerTurn()
        {
            b = new TestBattle(".P.E.");
            Assert.IsTrue(b.Add(b.Walk, Direction.Up));
            Assert.IsFalse(b.Add(b.Walk, Direction.Down), "同じ技は1ターン1回まで");
            Assert.IsTrue(b.Add(b.Slash, Direction.Right), "違う技なら追加できる");
            Assert.AreEqual(2, b.Turn.PlannedCommands.Count);
        }

        [Test]
        public void CannotExceedMaxSlots()
        {
            b = new TestBattle(".P.E.", maxSlots: 2);
            Assert.IsTrue(b.Add(b.Walk, Direction.Up));
            Assert.IsTrue(b.Add(b.Slash, Direction.Up));
            Assert.IsFalse(b.Add(b.Push, Direction.Up), "最大スロット数を超えられない");
        }

        [Test]
        public void CannotUseCommandNotInStage()
        {
            b = new TestBattle(".P.E.");
            b.Stage.availableCommands.Remove(b.Rush);
            Assert.IsFalse(b.Add(b.Rush, Direction.Right, 2));
        }

        [Test]
        public void RemoveLastAndClearWork()
        {
            b = new TestBattle(".P.E.");
            b.Add(b.Walk, Direction.Up);
            b.Add(b.Slash, Direction.Up);
            b.Turn.RemoveLastCommand();
            Assert.AreEqual(1, b.Turn.PlannedCommands.Count);
            Assert.AreSame(b.Walk, b.Turn.PlannedCommands[0].Command);
            b.Turn.ClearCommands();
            Assert.AreEqual(0, b.Turn.PlannedCommands.Count);
        }

        [Test]
        public void AfterTurnItIsPlayerTurnAgainWithEmptySlots()
        {
            b = new TestBattle(".....\n.P.E.\n.....");
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            Assert.AreEqual(BattleState.PlayerInput, b.Turn.State);
            Assert.AreEqual(2, b.Turn.TurnNumber);
            Assert.AreEqual(0, b.Turn.PlannedCommands.Count);
        }
    }
}
