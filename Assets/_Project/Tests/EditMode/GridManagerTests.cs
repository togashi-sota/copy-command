using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    public class GridManagerTests
    {
        GridManager grid;

        [SetUp]
        public void SetUp()
        {
            grid = new GameObject("TestGrid").AddComponent<GridManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(grid.gameObject);
        }

        [Test]
        public void BoardSizeComesFromMapRows()
        {
            grid.BuildFromRows(new[] { ".......", ".......", ".P...E.", ".......", "......." });
            Assert.AreEqual(7, grid.Width);
            Assert.AreEqual(5, grid.Height);
        }

        [Test]
        public void FirstRowIsTopOfBoard()
        {
            grid.BuildFromRows(new[] { "P..", "...", "..E" });
            Assert.AreEqual(new Vector2Int(0, 2), grid.PlayerStart);
            Assert.AreEqual(new Vector2Int(2, 0), grid.EnemyStart);
        }

        [Test]
        public void WallsAndOutsideAreWalls()
        {
            grid.BuildFromRows(new[] { "P#.", "..E" });
            Assert.IsTrue(grid.IsWall(new Vector2Int(1, 1)));
            Assert.IsFalse(grid.IsWall(new Vector2Int(2, 1)));
            Assert.IsTrue(grid.IsWall(new Vector2Int(-1, 0)), "盤面の外は壁扱い");
            Assert.IsTrue(grid.IsWall(new Vector2Int(3, 0)), "盤面の外は壁扱い");
            Assert.IsTrue(grid.IsWall(new Vector2Int(0, 2)), "盤面の外は壁扱い");
        }

        [Test]
        public void ShortRowsArePaddedWithWalls()
        {
            grid.BuildFromRows(new[] { "P...", "E." });
            Assert.AreEqual(4, grid.Width);
            Assert.IsTrue(grid.IsWall(new Vector2Int(3, 0)));
        }

        [Test]
        public void StageDataSplitsMapIgnoringBlankLinesAndSpaces()
        {
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.map = "\r\n  P..  \r\n\r\n..E\n";
            CollectionAssert.AreEqual(new[] { "P..", "..E" }, stage.GetMapRows());
            Object.DestroyImmediate(stage);
        }
    }
}
