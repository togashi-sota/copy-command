using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>STEP 6：プレイヤーの命令（CommandRecord）が敵へコピーされること。</summary>
    public class CommandCopyTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void CopiesCommandDirectionDistanceAndOrder()
        {
            b = new TestBattle(".......\n.P...E.\n.......");
            b.Add(b.Walk, Direction.Up);
            b.Add(b.Slash, Direction.Left);
            b.Add(b.Rush, Direction.Right, 3);
            b.RunTurn();

            var copied = b.Turn.EnemyReplay.CopiedCommands;
            Assert.AreEqual(3, copied.Count);
            Assert.AreSame(b.Walk, copied[0].Command);
            Assert.AreEqual(Direction.Up, copied[0].Direction);
            Assert.AreSame(b.Slash, copied[1].Command);
            Assert.AreEqual(Direction.Left, copied[1].Direction);
            Assert.AreSame(b.Rush, copied[2].Command);
            Assert.AreEqual(Direction.Right, copied[2].Direction);
            Assert.AreEqual(3, copied[2].Distance);
        }

        [Test]
        public void CopiesInputEvenWhenPlayerCommandFailed()
        {
            // プレイヤーは壁に向かって歩く（空振り）→ それでも敵には「歩く 右」が渡る
            b = new TestBattle("P#..E.");
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(0, 0), b.Player.GridPosition, "プレイヤーは動けていない");
            Assert.AreEqual(1, b.Turn.EnemyReplay.CopiedCommands.Count);
            Assert.AreEqual(Direction.Right, b.Turn.EnemyReplay.CopiedCommands[0].Direction);
        }

        [Test]
        public void CopyIsIndependentFromPlayerSlots()
        {
            b = new TestBattle(".......\n.P...E.\n.......");
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            Assert.AreEqual(0, b.Turn.PlannedCommands.Count, "スロットは空になる");
            Assert.AreEqual(1, b.Turn.EnemyReplay.CopiedCommands.Count, "コピーは残る");
        }

        [Test]
        public void EnemyPreviewShowsSlotsBeingRegistered()
        {
            b = new TestBattle(".......\n.P...E.\n.......");
            Assert.AreEqual(0, b.Turn.EnemyPreviewCommands.Count);
            b.Add(b.Walk, Direction.Up);
            b.Add(b.Slash, Direction.Right);
            Assert.AreEqual(2, b.Turn.EnemyPreviewCommands.Count);
            Assert.AreSame(b.Slash, b.Turn.EnemyPreviewCommands[1].Command);
        }

        [Test]
        public void DisplayTextShowsDistanceOnlyForDistanceCommands()
        {
            b = new TestBattle(".P.E.");
            Assert.AreEqual("歩く →右", new CommandRecord(b.Walk, Direction.Right, 1).ToDisplayText());
            Assert.AreEqual("突進 ↑上 2マス", new CommandRecord(b.Rush, Direction.Up, 2).ToDisplayText());
        }
    }
}
