using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>Phase A：デッキ・手札・山札・捨て札・保留。</summary>
    public class DeckStateTests
    {
        CommandData slash, rush, push;
        List<CommandData> initialDeck;

        [SetUp]
        public void SetUp()
        {
            slash = Make("斬撃"); rush = Make("突進"); push = Make("押し出し");
            initialDeck = new List<CommandData> { slash, slash, slash, rush, rush, rush, push, push };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(slash); Object.DestroyImmediate(rush); Object.DestroyImmediate(push);
        }

        static CommandData Make(string name)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = name;
            return c;
        }

        [Test]
        public void StartBattleDrawsFiveFromEightCardDeck()
        {
            var deck = new DeckState(new System.Random(1));
            deck.StartBattle(initialDeck);
            Assert.AreEqual(5, deck.Hand.Count);
            Assert.AreEqual(3, deck.DrawPile.Count);
            Assert.AreEqual(0, deck.DiscardPile.Count);
        }

        [Test]
        public void UsedCardsGoToDiscard()
        {
            var deck = new DeckState(new System.Random(1));
            deck.StartBattle(initialDeck);
            var used = deck.Hand.Take(2).ToList();
            deck.UseCards(used);
            Assert.AreEqual(3, deck.Hand.Count);
            CollectionAssert.AreEquivalent(used, deck.DiscardPile);
        }

        [Test]
        public void EndTurnKeepsAtMostTwoRetainedAndDiscardsTheRest()
        {
            var deck = new DeckState(new System.Random(1));
            deck.StartBattle(initialDeck);
            var keep = deck.Hand.Take(3).ToList(); // 3枚指定しても2枚まで
            deck.EndTurn(keep);
            Assert.AreEqual(2, deck.Hand.Count);
            CollectionAssert.AreEqual(keep.Take(2), deck.Hand);
            Assert.AreEqual(3, deck.DiscardPile.Count);
        }

        [Test]
        public void NextTurnRefillsToFiveKeepingRetained()
        {
            var deck = new DeckState(new System.Random(2));
            deck.StartBattle(initialDeck);
            var keep = deck.Hand.Take(2).ToList();
            deck.UseCards(deck.Hand.Skip(2).Take(1).ToList());
            deck.EndTurn(keep);
            var drawn = deck.DrawToHandSize();
            Assert.AreEqual(5, deck.Hand.Count);
            Assert.AreEqual(3, drawn.Count, "保留2枚＋新しく3枚");
            CollectionAssert.IsSubsetOf(keep, deck.Hand);
        }

        [Test]
        public void EmptyDrawPileReshufflesDiscard()
        {
            var deck = new DeckState(new System.Random(3));
            deck.StartBattle(initialDeck);       // 山札 3
            deck.EndTurn(new CardInstance[0]);   // 5枚捨て札へ
            deck.DrawToHandSize();               // 山札3枚 → 足りないので捨て札5枚をシャッフルして2枚引く
            Assert.AreEqual(5, deck.Hand.Count);
            Assert.AreEqual(1, deck.ReshuffleCount);
            Assert.AreEqual(8, deck.Hand.Count + deck.DrawPile.Count + deck.DiscardPile.Count, "カードは減らない");
        }

        [Test]
        public void SmallDeckDrawsWhatItCan()
        {
            var deck = new DeckState(new System.Random(4));
            deck.StartBattle(new List<CommandData> { slash, rush });
            Assert.AreEqual(2, deck.Hand.Count);
        }

        [Test]
        public void SameNameCardsAreSeparateInstances()
        {
            var deck = new DeckState(new System.Random(5));
            deck.StartBattle(new List<CommandData> { slash, slash, slash });
            Assert.AreEqual(3, deck.CountInHand(slash));
            Assert.AreEqual(3, deck.Hand.Select(c => c.Id).Distinct().Count());
        }

        [Test]
        public void StartBattleResetsPilesButKeepsDeckContents()
        {
            var deck = new DeckState(new System.Random(6));
            deck.StartBattle(initialDeck);
            deck.UseCards(deck.Hand.ToList());
            deck.StartBattle(initialDeck);
            Assert.AreEqual(5, deck.Hand.Count);
            Assert.AreEqual(0, deck.DiscardPile.Count);
            Assert.AreEqual(8, deck.Hand.Count + deck.DrawPile.Count);
        }
    }
}
