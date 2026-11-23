using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>手札方式 A（毎ターン2枚）と B（3枚未満なら3枚まで＋1枚）、手札上限 7。</summary>
    public class HandPolicyTests
    {
        readonly List<Object> created = new List<Object>();
        CommandData card;

        [SetUp]
        public void SetUp()
        {
            card = ScriptableObject.CreateInstance<CommandData>();
            card.displayName = "斬撃";
            created.Add(card);
        }

        [TearDown]
        public void TearDown() { foreach (var o in created) Object.DestroyImmediate(o); }

        DeckState Deck(DrawPolicy policy, int cards = 12)
        {
            var d = new DeckState(new System.Random(1), policy: policy);
            d.StartBattle(Enumerable.Repeat(card, cards));
            return d;
        }

        [Test]
        public void BothStartWithFive([Values(DrawPolicy.DrawTwo, DrawPolicy.FloorThreePlusOne)] DrawPolicy policy)
        {
            Assert.AreEqual(5, Deck(policy).Hand.Count);
        }

        [Test]
        public void ADrawsTwoAndKeepsUnused()
        {
            var d = Deck(DrawPolicy.DrawTwo);
            d.UseCards(d.Hand.Take(3).ToList()); // 3枚使う → 残り2枚
            d.EndTurn(new CardInstance[0]);       // A/B は未使用カードを捨てない
            Assert.AreEqual(2, d.Hand.Count);
            d.StartTurnDraw();
            Assert.AreEqual(4, d.Hand.Count, "2枚引く");
        }

        [Test]
        public void BRefillsToThreeThenPlusOne()
        {
            var d = Deck(DrawPolicy.FloorThreePlusOne);
            d.UseCards(d.Hand.Take(4).ToList()); // 残り1枚
            d.EndTurn(new CardInstance[0]);
            d.StartTurnDraw();
            Assert.AreEqual(4, d.Hand.Count, "1枚 → 3枚まで補充 → さらに1枚");
            d.UseCards(d.Hand.Take(1).ToList()); // 残り3枚
            d.StartTurnDraw();
            Assert.AreEqual(4, d.Hand.Count, "3枚以上なら1枚だけ");
        }

        [Test]
        public void HandLimitSevenSkipsDraws([Values(DrawPolicy.DrawTwo, DrawPolicy.FloorThreePlusOne)] DrawPolicy policy)
        {
            var d = Deck(policy, 20);
            for (int t = 0; t < 5; t++) d.StartTurnDraw(); // 1枚も使わない
            Assert.AreEqual(7, d.Hand.Count, "上限 7 を超えない");
            Assert.Greater(d.LastSkippedDraws, 0, "上限のときのドローは行わない");
        }

        [Test]
        public void StageCanBePlayedWithDeckModes()
        {
            var b = new TestBattle(".....\n.P.E.\n.....", maxSlots: 3);
            b.Turn.CurrentHandMode = HandMode.DeckA;
            b.Turn.LoadStage(0);
            Assert.IsTrue(b.Turn.IsDeckMode);
            Assert.IsNull(b.Turn.BasicWalk, "自由移動なし：歩くはカードとして山札から引く");
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count);
            Assert.AreEqual(8, b.Turn.Deck.Hand.Count + b.Turn.Deck.DrawPile.Count, "比較用デッキ（指定なしならカード2枚ずつ）");
            var first = b.Turn.Deck.Hand[0];
            Assert.IsTrue(b.Turn.AddCommand(new CommandRecord(first.Data, Direction.Up, System.Math.Max(0, first.Data.minDistance), first)));
            b.RunTurn();
            Assert.AreEqual(6, b.Turn.Deck.Hand.Count, "4枚残り＋2枚");
            b.Turn.CurrentHandMode = HandMode.Fixed;
            b.Turn.LoadStage(0);
            Assert.IsFalse(b.Turn.IsDeckMode, "固定に戻せば今までどおり");
            b.Destroy();
        }

        [Test]
        public void SameStageSameShuffleForAAndB()
        {
            var b = new TestBattle(".....\n.P.E.\n.....");
            b.Turn.CurrentHandMode = HandMode.DeckA;
            b.Turn.LoadStage(0);
            var handA = b.Turn.Deck.Hand.Select(c => c.Data).ToList();
            b.Turn.CurrentHandMode = HandMode.DeckB;
            b.Turn.LoadStage(0);
            CollectionAssert.AreEqual(handA, b.Turn.Deck.Hand.Select(c => c.Data).ToList(), "A と B を同じ条件で比べられる");
            b.Destroy();
        }
    }
}
