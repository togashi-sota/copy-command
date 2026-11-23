using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】Combat V2：どの敵も「コピー＋固有行動1つ」（剣兵・槍兵・砲兵・鏡兵・鏡守の騎士V2）と、ワープ陣。
    /// 固有行動の向き＝最後にコピーした方向（＝あなたの命令列の最後の方向）で決まることを確かめる。
    /// </summary>
    public class CombatV2Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        CommandData Make(string name, CommandType type, int min = 0, int max = 0, int damage = 0, int range = 1)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = name;
            c.type = type;
            c.minDistance = min;
            c.maxDistance = max;
            c.damage = damage;
            c.range = range;
            created.Add(c);
            return c;
        }

        static BattleModel Rows(string map, int enemyHp = 10)
        {
            var m = BattleModel.FromRows(map.Split('\n'), 3, 12, enemyHp);
            m.ApplyStage(null);
            return m;
        }

        static List<CommandRecord> One(CommandData c, Direction d, int dist = 0) => new List<CommandRecord> { new CommandRecord(c, d, dist) };

        [Test]
        public void SwordsmanStrikesAllFourNeighboursAfterTheCopy()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            // 正面から斬っても、ラウンドの終わりに隣にいれば剣で斬られる
            var m = Rows(".....\n..S..\n..P..\n.....\n.....");
            TurnRules.PlayTurn(m, One(slash, Direction.Up), null);
            Assert.AreEqual(6, m.Enemies.First().Hp, "あなたの斬撃 4");
            Assert.AreEqual(9, m.Player.Hp, "剣で 3");
            // 斬ってから下がっても、剣兵もコピーで下がってきて隣のまま
            var back = Rows(".....\n..S..\n..P..\n.....\n.....");
            TurnRules.PlayTurn(back, new List<CommandRecord> { new CommandRecord(slash, Direction.Up, 0), new CommandRecord(walk, Direction.Down, 1) }, null);
            Assert.AreEqual(9, back.Player.Hp, "コピーで追ってくる");
            // 離れて終われば当たらない
            var safe = Rows(".....\n..S..\n.....\n..P..\n.....");
            TurnRules.PlayTurn(safe, One(walk, Direction.Up, 1), null);
            Assert.AreEqual(12, safe.Player.Hp, "あなたが上へ → 剣兵も上へ。2マス離れたまま");
        }

        [Test]
        public void SpearmanThrustsTwoCellsAndHitsEnemiesToo()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            var m = Rows(".....\n..Y..\n.....\n..P..\n.....");
            TurnRules.PlayTurn(m, One(slash, Direction.Down), null);
            Assert.AreEqual(9, m.Player.Hp, "下を向いた槍兵の2マス先にあなた");
            var up = Rows(".....\n..Y..\n.....\n..P..\n.....");
            TurnRules.PlayTurn(up, One(slash, Direction.Up), null);
            Assert.AreEqual(12, up.Player.Hp, "上を向けば届かない");
            // 槍は敵にも当たる：槍兵の正面2マスにいる剣兵
            // 槍は敵にも当たる：あなたが下へ歩く → 槍兵は剣兵にふさがれて空振りし下を向く、剣兵は下へ1マス → 槍の2マスに剣兵
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var ff = Rows("..Y..\n..S..\n.....\nP....\n.....");
            TurnRules.PlayTurn(ff, One(walk, Direction.Down, 1), null);
            Assert.AreEqual(7, ff.Enemies.ToList()[1].Hp, "槍が剣兵に 3");
        }

        [Test]
        public void BomberMarksYourCellThenExplodesNextRound()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            var stay = Rows("O....\n.....\n..P..\n.....\n.....");
            TurnRules.PlayTurn(stay, One(slash, Direction.Right), null);
            var bomber = stay.Enemies.First();
            Assert.AreEqual(5, bomber.BombCells.Count, "あなたの場所を中心に十字5マスを予告");
            Assert.AreEqual(12, stay.Player.Hp, "予告だけ（まだ爆発しない）");
            TurnRules.PlayTurn(stay, One(slash, Direction.Right), null);
            Assert.AreEqual(9, stay.Player.Hp, "止まっていると次のラウンドに爆発で 3");

            var walk = Make("駆け足", CommandType.Rush, 2, 2);
            var move = Rows("O....\n.....\n..P..\n.....\n.....");
            TurnRules.PlayTurn(move, One(slash, Direction.Right), null);
            TurnRules.PlayTurn(move, One(walk, Direction.Right, 2), null);
            Assert.AreEqual(12, move.Player.Hp, "予告から出れば当たらない");
        }

        [Test]
        public void WarpPadsMoveYouToTheOtherPad()
        {
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var m = Rows("Pw..E\n.....\n....w");
            TurnRules.PlayCommandsAndCopies(m, One(walk, Direction.Right, 1), null);
            Assert.AreEqual(new Vector2Int(4, 0), m.Player.Position, "ワープ陣で対の出口へ");
        }

        [Test]
        public void WarpDoesNothingWhenTheExitIsOccupied()
        {
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var m = Rows("Pw..E\n.....\n....w");
            m.Enemies.First().Position = new Vector2Int(4, 0); // 出口の上に敵
            TurnRules.PlayCommandsAndCopies(m, One(walk, Direction.Right, 1), null);
            Assert.AreEqual(new Vector2Int(1, 2), m.Player.Position, "出口がふさがっていればワープ陣の上に止まる");
        }

        [Test]
        public void MirrorSoldierCopiesWithReversedDirections()
        {
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var m = Rows(".....\n..M..\n.....\n..P..\n.....");
            TurnRules.PlayCommandsAndCopies(m, One(walk, Direction.Right, 1), null);
            Assert.AreEqual(CopyTrait.Mirror, m.Enemies.First().CopyTrait);
            Assert.AreEqual(new Vector2Int(1, 3), m.Enemies.First().Position, "あなたが右なら鏡兵は左");
        }

        [Test]
        public void KnightV2GainsArrowsBelowSeventyPercent()
        {
            var m = Rows(".......\n...V...\n.......\n...P...\n.......", 30);
            var knight = m.Enemies.First();
            Assert.AreEqual(CopyTrait.Mirror, knight.CopyTrait);
            Assert.IsFalse(knight.ShootsArrows, "第1段階は剣だけ");
            knight.Hp = 21;
            Assert.IsTrue(knight.ShootsArrows, "70% 以下で矢も");
            Assert.IsTrue(PhaseRules.HasTrait(knight));
        }

        [Test]
        public void TraitCellsMatchWhatTheTraitHits()
        {
            var m = Rows(".....\n..S..\n.....\n.....\n.....");
            var s = m.Enemies.First();
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(2, 4), new Vector2Int(2, 2), new Vector2Int(1, 3), new Vector2Int(3, 3) }, PhaseRules.TraitCells(m, s));
        }
    }
}
