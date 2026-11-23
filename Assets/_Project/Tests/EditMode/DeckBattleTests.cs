using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>Phase B：デッキと命令スロットの統合（RUN モードの戦闘）。</summary>
    public class DeckBattleTests
    {
        TestBattle b;
        CommandData walk;

        [TearDown]
        public void TearDown()
        {
            b?.Destroy();
            if (walk != null) UnityEngine.Object.DestroyImmediate(walk);
        }

        /// <summary>RUN モードで戦闘を始める。deck は TestBattle の技から作る。</summary>
        void StartRun(string map, Func<TestBattle, List<CommandData>> deck, int playerHp = 12, bool innate = false, int enemyHp = 12, int seed = 1)
        {
            b = new TestBattle(map, maxSlots: 3, innate: innate, enemyHp: enemyHp);
            walk = ScriptableObject.CreateInstance<CommandData>();
            walk.displayName = "歩く";
            walk.type = CommandType.Walk;
            b.Turn.LoadRunBattle(b.Stage, playerHp, 12, deck(b), walk, new System.Random(seed));
        }

        static List<CommandData> Deck8(TestBattle t) =>
            new List<CommandData> { t.Slash, t.Slash, t.Slash, t.Rush, t.Rush, t.Rush, t.Push, t.Push };
        static List<CommandData> NoCards(TestBattle t) => new List<CommandData>();

        CardInstance HandCard(CommandData data) => b.Turn.Deck.Hand.First(c => c.Data == data && !b.Turn.IsCardPlanned(c));
        bool AddCard(CommandData data, Direction dir, int dist = 0) =>
            b.Turn.AddCommand(new CommandRecord(data, dir, dist, HandCard(data)));
        bool AddWalk(Direction dir) => b.Turn.AddCommand(new CommandRecord(walk, dir, 1));

        [Test]
        public void DrawsFiveAtBattleStart()
        {
            StartRun(".P.E.", Deck8);
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count);
            Assert.AreEqual(3, b.Turn.Deck.DrawPile.Count);
        }

        [Test]
        public void WalkCanBeUsedManyTimesWithoutCards()
        {
            StartRun(".....\n.P.E.\n.....", NoCards);
            Assert.AreEqual(0, b.Turn.Deck.Hand.Count, "カードが無くても");
            Assert.IsTrue(AddWalk(Direction.Up));
            Assert.IsTrue(AddWalk(Direction.Right));
            Assert.IsTrue(AddWalk(Direction.Down));
            Assert.IsFalse(AddWalk(Direction.Down), "命令は3枠まで");
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(2, 1), b.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(4, 1), b.Enemy.GridPosition, "歩くもコピーされる");
        }

        [Test]
        public void SameNameCardUsableAsManyTimesAsInHand()
        {
            StartRun(".PE..", t => new List<CommandData> { t.Slash, t.Slash }, enemyHp: 20);
            Assert.IsTrue(AddCard(b.Slash, Direction.Right));
            Assert.IsTrue(AddCard(b.Slash, Direction.Right), "2枚あれば2回使える");
            Assert.IsFalse(b.Turn.CanAddCard(b.Turn.Deck.Hand[0]), "同じカードを2回は入れられない");
            b.RunTurn();
            Assert.AreEqual(12, b.Enemy.Hp);
        }

        [Test]
        public void CardsGoToDiscardOnlyWhenExecuted()
        {
            // 斬撃10枚のデッキ：手札5・山札5なので、1ターン後に捨て札がシャッフルされず確認できる
            StartRun(".....\n.P.E.\n.....", t => Enumerable.Repeat(t.Slash, 10).ToList());
            Assert.IsTrue(AddCard(b.Slash, Direction.Up));
            b.Turn.RemoveLastCommand();
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count, "1つ戻すとカードは手札のまま");
            Assert.IsTrue(AddCard(b.Slash, Direction.Up));
            b.Turn.ClearCommands();
            Assert.AreEqual(0, b.Turn.Deck.DiscardPile.Count, "全部消すでもカードは失われない");
            Assert.IsTrue(AddCard(b.Slash, Direction.Up));
            b.RunTurn();
            Assert.IsTrue(b.Turn.Deck.DiscardPile.Any(c => c.Data == b.Slash), "実行したカードは捨て札へ");
        }

        [Test]
        public void RetainKeepsUpToTwoCardsAndRefillsToFive()
        {
            StartRun(".....\n.P.E.\n.....", Deck8, seed: 7);
            var hand = b.Turn.Deck.Hand.ToList();
            Assert.IsTrue(b.Turn.ToggleRetain(hand[0]));
            Assert.IsTrue(b.Turn.ToggleRetain(hand[1]));
            Assert.IsFalse(b.Turn.ToggleRetain(hand[2]), "保留は2枚まで");
            Assert.IsTrue(AddWalk(Direction.Up));
            b.RunTurn();
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count, "保留2枚＋補充3枚で5枚");
            CollectionAssert.IsSubsetOf(new[] { hand[0], hand[1] }, b.Turn.Deck.Hand.ToList());
            Assert.AreEqual(3, b.Turn.Deck.DiscardPile.Count, "保留しなかった3枚は捨て札");
            Assert.AreEqual(0, b.Turn.RetainedCards.Count, "保留の指定は毎ターンやり直し");
        }

        [Test]
        public void EmptyDrawPileReshufflesDuringBattle()
        {
            StartRun(".....\n.P.E.\n.....", Deck8);
            Assert.IsTrue(AddWalk(Direction.Up));
            b.RunTurn(); // 5枚捨て → 山札3枚＋捨て札シャッフル
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count);
            Assert.AreEqual(1, b.Turn.Deck.ReshuffleCount);
        }

        [Test]
        public void PlannedCardCannotBeRetained()
        {
            StartRun(".PE..", t => new List<CommandData> { t.Slash, t.Rush });
            var slash = HandCard(b.Slash);
            Assert.IsTrue(b.Turn.ToggleRetain(slash));
            Assert.IsTrue(AddCard(b.Slash, Direction.Right));
            Assert.IsFalse(b.Turn.RetainedCards.Contains(slash), "スロットに入れたら保留は外れる");
        }

        [Test]
        public void HitAndAwayWithTwoWalksAndOneSlash()
        {
            StartRun("......\n.P.E..\n......", t => new List<CommandData> { t.Slash }, innate: true);
            Assert.IsTrue(AddWalk(Direction.Right));
            Assert.IsTrue(AddCard(b.Slash, Direction.Right));
            Assert.IsTrue(AddWalk(Direction.Left));
            b.RunTurn();
            Assert.AreEqual(8, b.Enemy.Hp);
            Assert.AreEqual(12, b.Player.Hp, "近づく→斬る→逃げる で周囲斬りを避けられる");
        }

        [Test]
        public void EnemyCopiesCardCommandsButNotCards()
        {
            StartRun(".....\n.P.E.\n.....", t => new List<CommandData> { t.Rush });
            Assert.IsTrue(AddCard(b.Rush, Direction.Up, 2));
            Assert.IsTrue(AddWalk(Direction.Down));
            b.RunTurn();
            var copied = b.Turn.EnemyReplay.CopiedCommands;
            Assert.AreEqual(2, copied.Count);
            Assert.AreSame(b.Rush, copied[0].Command);
            Assert.AreEqual(2, copied[0].Distance);
            Assert.IsNull(copied[0].SourceCard, "敵に渡るのは技・方向・距離だけ（カードの実体は渡らない）");
            Assert.AreEqual(new Vector2Int(3, 1), b.Enemy.GridPosition, "突進↑2（端で1マス）→ 歩く↓");
        }

        [Test]
        public void InnateStillRunsBeforeCopyInRunMode()
        {
            StartRun(".P.E..", NoCards, innate: true);
            Assert.IsTrue(AddWalk(Direction.Right));
            b.RunTurn();
            Assert.AreEqual(8, b.Player.Hp, "隣でターンを終えたので周囲斬り");
        }

        [Test]
        public void PlayerStartsWithCarriedOverHp()
        {
            StartRun(".P.E.", NoCards, playerHp: 7);
            Assert.AreEqual(7, b.Player.Hp);
            Assert.AreEqual(12, b.Player.MaxHp);
        }

        [Test]
        public void TutorialModeIsUnchanged()
        {
            b = new TestBattle(".....\n.P.E.\n.....");
            Assert.IsFalse(b.Turn.IsDeckMode);
            Assert.IsTrue(b.Add(b.Walk, Direction.Up));
            Assert.IsFalse(b.Add(b.Walk, Direction.Up), "チュートリアルは同じ技1ターン1回のまま");
        }
    }
}
