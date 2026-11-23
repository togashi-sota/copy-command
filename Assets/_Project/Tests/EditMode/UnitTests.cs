using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    public class UnitTests
    {
        GameObject root;
        GridManager grid;
        TurnManager turn;
        StageData stage;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TestBattle");
            grid = root.AddComponent<GridManager>();
            turn = root.AddComponent<TurnManager>();
            stage = ScriptableObject.CreateInstance<StageData>();
            stage.map = ".....\n.P#E.\n.....";
            stage.playerMaxHp = 12;
            stage.enemyMaxHp = 9;
            turn.SetupForTest(grid, new List<StageData> { stage });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(stage);
        }

        [Test]
        public void LoadStagePlacesPlayerAndEnemyAtStartCells()
        {
            turn.LoadStage(0);
            Assert.AreEqual(new Vector2Int(1, 1), turn.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(3, 1), turn.Enemy.GridPosition);
            Assert.IsTrue(turn.Player.IsPlayer);
            Assert.IsFalse(turn.Enemy.IsPlayer);
        }

        [Test]
        public void HpComesFromStageData()
        {
            turn.LoadStage(0);
            Assert.AreEqual(12, turn.Player.Hp);
            Assert.AreEqual(9, turn.Enemy.Hp);
        }

        [Test]
        public void UnitCellIsBlockedButEmptyFloorIsNot()
        {
            turn.LoadStage(0);
            Assert.IsTrue(grid.IsBlocked(new Vector2Int(3, 1)), "敵のいるマス");
            Assert.IsTrue(grid.IsBlocked(new Vector2Int(2, 1)), "壁");
            Assert.IsFalse(grid.IsBlocked(new Vector2Int(0, 0)), "空いている床");
            Assert.AreSame(turn.Enemy, grid.GetUnitAt(new Vector2Int(3, 1)));
        }

        [Test]
        public void SetGridPositionMovesUnitOnBoard()
        {
            turn.LoadStage(0);
            turn.Player.SetGridPosition(new Vector2Int(1, 2));
            Assert.AreEqual(new Vector2Int(1, 2), turn.Player.GridPosition);
            Assert.IsNull(grid.GetUnitAt(new Vector2Int(1, 1)), "元のマスは空く");
            Assert.AreSame(turn.Player, grid.GetUnitAt(new Vector2Int(1, 2)));
        }

        [Test]
        public void TakeDamageNeverGoesBelowZero()
        {
            turn.LoadStage(0);
            turn.Enemy.TakeDamage(4);
            Assert.AreEqual(5, turn.Enemy.Hp);
            turn.Enemy.TakeDamage(10);
            Assert.AreEqual(0, turn.Enemy.Hp);
            Assert.IsFalse(turn.Enemy.IsAlive);
        }

        [Test]
        public void ReloadingStageResetsUnits()
        {
            turn.LoadStage(0);
            turn.Enemy.TakeDamage(4);
            turn.LoadStage(0);
            Assert.AreEqual(9, turn.Enemy.Hp);
            Assert.AreEqual(2, grid.Units.Count);
        }
    }
}
