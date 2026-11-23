using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>複数 Enemy のコピー処理（敵A が全部 → 敵B が全部、同士討ち、衝突、途中で倒れたら残りを実行しない）。</summary>
    public class MultiEnemyTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void EnemiesAreNamedInReadingOrder()
        {
            b = new TestBattle("E....\n..P..\n....E");
            Assert.AreEqual(2, b.Turn.Enemies.Count);
            Assert.AreEqual("敵A", b.Turn.Enemies[0].DisplayName);
            Assert.AreEqual(new Vector2Int(0, 2), b.Turn.Enemies[0].GridPosition, "上の行の敵が A");
            Assert.AreEqual("敵B", b.Turn.Enemies[1].DisplayName);
        }

        [Test]
        public void SameSlashMissesForOneEnemyAndHitsPlayerForTheOther()
        {
            // 敵A はあなたの右隣、敵B はあなたの左隣。あなたが「斬撃→」で敵A を斬ると、
            // 敵A のコピーは右へ空振り、敵B のコピー「斬撃→」はあなたに当たる
            b = new TestBattle(".EPE.");
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            var a = b.Turn.Enemies.First(e => e.GridPosition == new Vector2Int(3, 0));
            Assert.AreEqual(8, a.Hp, "あなたの斬撃は右の敵に命中");
            Assert.AreEqual(8, b.Player.Hp, "左の敵のコピーがあなたに命中");
        }

        [Test]
        public void EnemyCopyCanHitAnotherEnemy()
        {
            // 敵A の右隣に敵B。あなたが「斬撃→」（空振り）→ 敵A のコピーが敵B に当たる（同士討ち）
            b = new TestBattle("P....\n.....\n.EE..");
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(12, b.Turn.Enemies[0].Hp);
            Assert.AreEqual(8, b.Turn.Enemies[1].Hp, "敵A の斬撃が敵B に当たる");
            var dmg = b.Turn.BattleEvents.Single(e => e.Type == BattleEventType.Damaged);
            Assert.AreEqual("敵A", dmg.Cause.Source.Name);
            Assert.IsTrue(dmg.Cause.ByCopy);
        }

        [Test]
        public void EnemiesBlockEachOther()
        {
            // 敵A の右に敵B。あなたが「一歩→」→ 敵A は敵B にぶつかって動けない、敵B は動ける（A が先）
            b = new TestBattle("P....\n.....\n.EE..");
            b.Add(b.Walk, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 0), b.Turn.Enemies[0].GridPosition, "敵A は敵B にぶつかって止まる");
            Assert.AreEqual(new Vector2Int(3, 0), b.Turn.Enemies[1].GridPosition);
        }

        [Test]
        public void ExecutionOrderIsAAllThenBAll()
        {
            b = new TestBattle("E...\n....\nP..E");
            b.Add(b.Walk, Direction.Up);
            b.Add(b.Slash, Direction.Left);
            b.RunTurn();
            var log = b.Turn.Log.Where(l => l.StartsWith("敵")).ToList();
            Assert.AreEqual(4, log.Count);
            StringAssert.StartsWith("敵A：歩く", log[0]);
            StringAssert.StartsWith("敵A：斬撃", log[1]);
            StringAssert.StartsWith("敵B：歩く", log[2]);
            StringAssert.StartsWith("敵B：斬撃", log[3]);
        }

        [Test]
        public void EnemyKilledMidSequenceStopsAndOthersContinue()
        {
            // 敵B（敵A の右隣、HP 4）が敵A の斬撃で倒れる → 敵B は残りの命令を実行しない。戦闘は敵A が残るので続く
            b = new TestBattle("P....\n.....\n.EE..", enemyHp: 4);
            b.Turn.Enemies[0].SetHp(12);
            b.Add(b.Slash, Direction.Right);
            b.Add(b.Walk, Direction.Up);
            b.RunTurn();
            Assert.IsFalse(b.Turn.Enemies[1].IsAlive);
            Assert.AreEqual(new Vector2Int(2, 0), b.Turn.Enemies[1].GridPosition, "倒れた敵Bは動かない");
            Assert.AreEqual(BattleState.PlayerInput, b.Turn.State, "敵A が残っているので続く");
        }

        [Test]
        public void ClearedOnlyWhenAllEnemiesDie()
        {
            b = new TestBattle(".EPE.", enemyHp: 4);
            b.Add(b.Slash, Direction.Right);
            b.RunTurn();
            Assert.AreEqual(BattleState.PlayerInput, b.Turn.State, "片方だけ倒しても終わらない");
            Assert.AreEqual(1, b.Turn.Enemies.Count(e => e.IsAlive));
        }

        [Test]
        public void PreviewPerEnemyFollowsCopyTrait()
        {
            b = new TestBattle("E...\n....\nP..E");
            b.Turn.Enemies[1].Model.CopyTrait = CopyTrait.Reverse;
            b.Add(b.Walk, Direction.Up);
            b.Add(b.Slash, Direction.Left);
            Assert.AreSame(b.Walk, b.Turn.GetPreviewFor(b.Turn.Enemies[0])[0].Command);
            Assert.AreSame(b.Slash, b.Turn.GetPreviewFor(b.Turn.Enemies[1])[0].Command, "逆順コピーの敵は逆の並び");
        }

        [Test]
        public void EnemiesDoNothingOnTheirOwn()
        {
            // 敵同士が隣り合っていても、プレイヤーが斬撃を渡さなければ殴り合わない（放置で勝てない）
            b = new TestBattle("P....\n.....\n.EE..");
            for (int t = 0; t < 5; t++) { b.Add(b.Walk, Direction.Up); b.RunTurn(); }
            Assert.IsTrue(b.Turn.Enemies.All(e => e.Hp == 12));
        }
    }
}
