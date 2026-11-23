using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>報酬カード（突き・回転斬り・引き寄せ・罠設置）と、敵の固有行動「十字斬り」（射程2）。</summary>
    public class NewCardTests
    {
        TestBattle b;
        CommandData card;

        [TearDown]
        public void TearDown()
        {
            b?.Destroy();
            if (card != null) Object.DestroyImmediate(card);
        }

        CommandData Make(CommandType type, int damage, int range)
        {
            card = ScriptableObject.CreateInstance<CommandData>();
            card.displayName = type.ToString();
            card.type = type;
            card.damage = damage;
            card.range = range;
            return card;
        }

        string Run(UnitController actor, Direction dir) =>
            new CommandExecutor(b.Grid).Execute(actor, new CommandRecord(card, dir, 0));

        [Test]
        public void ThrustReachesTwoCells()
        {
            b = new TestBattle(".P.E.");
            Make(CommandType.Slash, 3, 2);
            Run(b.Player, Direction.Right);
            Assert.AreEqual(9, b.Enemy.Hp);
        }

        [Test]
        public void ThrustStopsAtWall()
        {
            b = new TestBattle(".P#E.");
            Make(CommandType.Slash, 3, 2);
            Run(b.Player, Direction.Right);
            Assert.AreEqual(12, b.Enemy.Hp, "壁の向こうには届かない");
        }

        [Test]
        public void CopiedThrustHitsPlayerTwoCellsAway()
        {
            // 敵が自分の左2マス先にいるのに「突き →右」：自分は空振り、敵のコピーは右2マス先＝自分に当たる
            b = new TestBattle(".E.P..");
            Make(CommandType.Slash, 3, 2);
            b.Stage.availableCommands.Add(card);
            Assert.IsTrue(b.Turn.AddCommand(new CommandRecord(card, Direction.Right, 0)));
            b.RunTurn();
            Assert.AreEqual(12, b.Enemy.Hp);
            Assert.AreEqual(9, b.Player.Hp, "突きは届く距離が長いぶん、コピーされると離れていても当たる");
        }

        [Test]
        public void WhirlHitsAdjacentWithoutDirection()
        {
            b = new TestBattle("...\n.PE\n...");
            Make(CommandType.AroundSlash, 3, 1);
            Run(b.Player, Direction.Up);
            Assert.AreEqual(9, b.Enemy.Hp);
            Assert.AreEqual(12, b.Player.Hp, "自分には当たらない");
        }

        [Test]
        public void PullMovesFirstUnitOneCellCloser()
        {
            b = new TestBattle("P..E.");
            Make(CommandType.Pull, 0, 3);
            Run(b.Player, Direction.Right);
            Assert.AreEqual(new Vector2Int(2, 0), b.Enemy.GridPosition);
        }

        [Test]
        public void PullOntoSpikeDealsDamage()
        {
            b = new TestBattle("P.^E.");
            Make(CommandType.Pull, 0, 3);
            Run(b.Player, Direction.Right);
            Assert.AreEqual(new Vector2Int(2, 0), b.Enemy.GridPosition);
            Assert.AreEqual(12 - b.Grid.SpikeDamage, b.Enemy.Hp);
        }

        [Test]
        public void PullAdjacentOrOutOfRangeIsWhiff()
        {
            b = new TestBattle("PE...");
            Make(CommandType.Pull, 0, 3);
            StringAssert.Contains("空振り", Run(b.Player, Direction.Right));
            b.Destroy();
            b = new TestBattle("P...E");
            StringAssert.Contains("空振り", Run(b.Player, Direction.Right));
        }

        [Test]
        public void PlaceSpikeCreatesSpikeOnEmptyFloor()
        {
            b = new TestBattle(".P.E.");
            Make(CommandType.PlaceSpike, 0, 1);
            Run(b.Player, Direction.Right);
            Assert.IsNotNull(b.Grid.GetHazardAt(new Vector2Int(2, 0)));
            StringAssert.Contains("空振り", Run(b.Player, Direction.Right), "同じマスには置けない");
        }

        [Test]
        public void PlaceSpikeCannotTargetUnitOrWall()
        {
            b = new TestBattle("#PE..");
            Make(CommandType.PlaceSpike, 0, 1);
            StringAssert.Contains("空振り", Run(b.Player, Direction.Right));
            StringAssert.Contains("空振り", Run(b.Player, Direction.Left));
        }

        [Test]
        public void CopiedPlaceSpikeIsPlacedNextToEnemy()
        {
            b = new TestBattle(".....\n.P.E.\n.....");
            Make(CommandType.PlaceSpike, 0, 1);
            b.Stage.availableCommands.Add(card);
            Assert.IsTrue(b.Turn.AddCommand(new CommandRecord(card, Direction.Up, 0)));
            b.RunTurn();
            Assert.IsNotNull(b.Grid.GetHazardAt(new Vector2Int(1, 2)), "自分の上");
            Assert.IsNotNull(b.Grid.GetHazardAt(new Vector2Int(3, 2)), "敵のコピーで敵の上にも");
        }

        [Test]
        public void CrossSlashInnateReachesTwoCellsAndStopsAtWalls()
        {
            b = new TestBattle(".....\n.....\nP.E#.", innate: true);
            b.Stage.enemyInnateAction.range = 2;
            var cells = b.Turn.GetInnateDangerCells();
            CollectionAssert.Contains(cells, new Vector2Int(0, 0), "左2マス");
            CollectionAssert.Contains(cells, new Vector2Int(2, 2), "上2マス");
            CollectionAssert.DoesNotContain(cells, new Vector2Int(3, 0), "壁");
            CollectionAssert.DoesNotContain(cells, new Vector2Int(4, 0), "壁の向こう");
            new CommandExecutor(b.Grid).Execute(b.Enemy, new CommandRecord(b.Stage.enemyInnateAction, Direction.Up, 0));
            Assert.AreEqual(8, b.Player.Hp);
        }
    }
}
