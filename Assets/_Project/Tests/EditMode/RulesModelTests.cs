using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>構造整理で作った「純粋なルールモデル」のテスト（GameObject を使わない）。</summary>
    public class RulesModelTests
    {
        readonly List<Object> created = new List<Object>();
        CommandData walk, slash, push;

        [SetUp]
        public void SetUp()
        {
            walk = Make("一歩", CommandType.Walk, 1);
            slash = Make("斬撃", CommandType.Slash, 0, 4);
            push = Make("押し出し", CommandType.Push, 0);
        }

        [TearDown]
        public void TearDown() { foreach (var o in created) Object.DestroyImmediate(o); }

        CommandData Make(string name, CommandType type, int dist, int damage = 0)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = name; c.type = type; c.minDistance = dist; c.maxDistance = dist; c.damage = damage;
            created.Add(c);
            return c;
        }

        static BattleModel Model(string map, int spike = 5)
        {
            var board = BoardModel.Parse(map.Split('\n'), spike);
            var m = new BattleModel(board);
            m.AddUnit("プレイヤー", "", Team.Player, 12, board.PlayerStart);
            for (int i = 0; i < board.EnemyStarts.Count; i++)
                m.AddUnit("敵" + (board.EnemyStarts.Count == 1 ? "" : ((char)('A' + i)).ToString()), "", Team.Enemy, 12, board.EnemyStarts[i]);
            return m;
        }

        [Test]
        public void RulesRunWithoutGameObjects()
        {
            var m = Model(".P.E.");
            var events = new List<BattleEvent>();
            string log = RulesEngine.Execute(m, new CommandContext(m.Player, new CommandRecord(walk, Direction.Right, 1)), events);
            Assert.AreEqual(new Vector2Int(2, 0), m.Player.Position);
            StringAssert.Contains("1マス移動", log);
            Assert.AreEqual(BattleEventType.Moved, events.Single().Type);
        }

        [Test]
        public void DamageCauseRecordsWhoAndWhyForCopiedSpike()
        {
            // 敵がコピーした「一歩→」でトゲに入る → 原因：敵・一歩・トゲ・コピー
            var m = Model("P...E^");
            var events = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Enemies.First(), new CommandRecord(walk, Direction.Right, 1), isCopy: true), events);
            var dmg = events.Single(e => e.Type == BattleEventType.Damaged);
            Assert.AreEqual(5, dmg.Amount);
            Assert.AreSame(m.Enemies.First(), dmg.Cause.Source);
            Assert.AreSame(walk, dmg.Cause.Command);
            Assert.AreEqual(DamageKind.Spike, dmg.Cause.Kind);
            Assert.IsTrue(dmg.Cause.ByCopy);
        }

        [Test]
        public void DamageCauseForPlayerPushIntoSpike()
        {
            var m = Model(".PE^.");
            var events = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Player, new CommandRecord(push, Direction.Right, 0)), events);
            var dmg = events.Single(e => e.Type == BattleEventType.Damaged);
            Assert.AreSame(m.Player, dmg.Cause.Source, "押したのはプレイヤー");
            Assert.AreEqual(DamageKind.Spike, dmg.Cause.Kind);
            Assert.IsFalse(dmg.Cause.ByCopy);
        }

        [Test]
        public void DirectHitAndDeathEvents()
        {
            var m = Model(".PE..");
            m.Enemies.First().Hp = 4;
            var events = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Player, new CommandRecord(slash, Direction.Right, 0)), events);
            Assert.AreEqual(DamageKind.Direct, events.Single(e => e.Type == BattleEventType.Damaged).Cause.Kind);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Died));
            Assert.IsNull(m.GetUnitAt(new Vector2Int(2, 0)), "倒れたユニットは盤面から消える扱い（通り抜けられる）");
        }

        [Test]
        public void CloneIsIndependent()
        {
            var m = Model(".P.E.");
            var copy = m.Clone();
            RulesEngine.Execute(copy, new CommandContext(copy.Player, new CommandRecord(walk, Direction.Right, 1)), null);
            Assert.AreEqual(new Vector2Int(1, 0), m.Player.Position, "元のモデルは変わらない");
            Assert.AreEqual(new Vector2Int(2, 0), copy.Player.Position);
        }

        [Test]
        public void CopyTraitReverseAndMirror()
        {
            var cmds = new List<CommandRecord> { new CommandRecord(walk, Direction.Up, 1), new CommandRecord(slash, Direction.Right, 0) };
            var rev = EnemyReplayController.ApplyTrait(cmds, CopyTrait.Reverse);
            Assert.AreSame(slash, rev[0].Command);
            Assert.AreSame(walk, rev[1].Command);
            var mir = EnemyReplayController.ApplyTrait(cmds, CopyTrait.Mirror);
            Assert.AreEqual(Direction.Down, mir[0].Direction);
            Assert.AreEqual(Direction.Left, mir[1].Direction);
            Assert.AreSame(cmds, EnemyReplayController.ApplyTrait(cmds, CopyTrait.Normal), "通常コピーはそのまま");
        }
    }
}
