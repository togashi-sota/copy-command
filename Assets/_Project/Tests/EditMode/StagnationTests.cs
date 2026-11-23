using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証用・未確定】鏡蝕ゲージ：敵の HP を減らせないラウンドが続くとゲージが満ち、その後も減らせなければ呪いのダメージ。
    /// OFF（既定）では今までと何も変わらないこと、STAGE 1〜3 では使えないことも確認する。
    /// </summary>
    public class StagnationTests
    {
        TestBattle b;

        [TearDown]
        public void TearDown() => b?.Destroy();

        [Test]
        public void GaugeFillsThenPenaltiesEscalate()
        {
            var rule = new StagnationRule(3, new[] { 2, 3, 4 });
            Assert.AreEqual(0, rule.EndRound(false)); Assert.AreEqual(1, rule.Gauge);
            Assert.AreEqual(0, rule.EndRound(false)); Assert.AreEqual(2, rule.Gauge);
            Assert.AreEqual(0, rule.EndRound(false)); Assert.AreEqual(3, rule.Gauge, "3回目で満ちる（まだ呪いはない）");
            Assert.IsTrue(rule.AtMax);
            Assert.AreEqual(2, rule.EndRound(false), "満ちた後、さらに減らせなかったら -2");
            Assert.AreEqual(3, rule.EndRound(false));
            Assert.AreEqual(4, rule.EndRound(false));
            Assert.AreEqual(4, rule.EndRound(false), "最後の値をくり返す");
        }

        [Test]
        public void ProgressResetsGaugeAndEscalation()
        {
            var rule = new StagnationRule(3, new[] { 2, 3, 4 });
            for (int i = 0; i < 5; i++) rule.EndRound(false); // 呪いを2回受けた状態
            Assert.AreEqual(4, rule.NextPenalty);
            Assert.AreEqual(0, rule.EndRound(true), "敵の HP が減ったラウンドは呪いなし");
            Assert.AreEqual(0, rule.Gauge);
            Assert.AreEqual(2, rule.NextPenalty, "呪いの段階も最初に戻る");
        }

        [Test]
        public void MaxIsConfigurable([Values(1, 2, 4)] int max)
        {
            var rule = new StagnationRule(max, new[] { 5 });
            for (int i = 0; i < max; i++) Assert.AreEqual(0, rule.EndRound(false));
            Assert.AreEqual(5, rule.EndRound(false));
        }

        // ───────── ゲームでの動き ─────────

        void Setup(string map, bool on, bool allowed = true, int playerHp = 12)
        {
            b = new TestBattle(map, playerHp: playerHp, enemyHp: 12);
            b.Stage.stagnationAllowed = allowed;
            b.Turn.StagnationDebugOn = on;
            b.Turn.LoadStage(0);
        }

        void Whiff()
        {
            // 斬撃↑：あなたも敵も、上には誰もいない（盤面は何も変わらない）
            Assert.IsTrue(b.Add(b.Slash, Direction.Up, 0));
            b.RunTurn();
        }

        [Test]
        public void StallingCostsHpOnlyAfterTheGaugeIsFull()
        {
            Setup(".....\n.P.E.\n.....", on: true);
            Assert.IsNotNull(b.Turn.Stagnation);
            Whiff(); Whiff(); Whiff();
            Assert.AreEqual(12, b.Player.Hp, "3ラウンドまでは準備として許される");
            Assert.AreEqual(3, b.Turn.Stagnation.Gauge);
            Whiff();
            Assert.AreEqual(10, b.Player.Hp, "4ラウンド目：-2");
            Whiff();
            Assert.AreEqual(7, b.Player.Hp, "5ラウンド目：-3");
            var curse = b.Turn.BattleEvents.Last(e => e.Type == BattleEventType.Damaged);
            Assert.AreEqual(DamageKind.Curse, curse.Cause.Kind);
        }

        [Test]
        public void DamagingAnEnemyResetsTheGauge()
        {
            Setup(".....\n.PE..\n.....", on: true);
            Whiff(); Whiff();
            Assert.AreEqual(2, b.Turn.Stagnation.Gauge);
            Assert.IsTrue(b.Add(b.Slash, Direction.Right, 0)); // 敵に命中（敵のコピーは右へ空振り）
            b.RunTurn();
            Assert.AreEqual(0, b.Turn.Stagnation.Gauge);
        }

        [Test]
        public void SpikeAndFriendlyFireCountAsProgress()
        {
            // トゲ：あなたの「一歩→」を敵がコピーしてトゲへ（あなたは壁で止まる）
            Setup(".....\n.P#E^\n.....", on: true);
            Whiff();
            Assert.IsTrue(b.Add(b.Walk, Direction.Right));
            b.RunTurn();
            Assert.AreEqual(0, b.Turn.Stagnation.Gauge, "敵がトゲに入った＝敵の HP が減った");
            b.Destroy();

            // 同士討ち：敵A の右隣に敵B。あなたの空振り「斬撃→」を敵A がコピーして敵B へ
            Setup("P....\n.....\n.EE..", on: true);
            Whiff();
            Assert.IsTrue(b.Add(b.Slash, Direction.Right, 0));
            b.RunTurn();
            Assert.AreEqual(0, b.Turn.Stagnation.Gauge);
        }

        [Test]
        public void CurseCanEndTheBattle()
        {
            Setup(".....\n.P.E.\n.....", on: true, playerHp: 2);
            for (int i = 0; i < 4; i++) Whiff();
            Assert.AreEqual(BattleState.GameOver, b.Turn.State);
        }

        [Test]
        public void OffChangesNothing()
        {
            Setup(".....\n.P.E.\n.....", on: false);
            Assert.IsNull(b.Turn.Stagnation);
            for (int i = 0; i < 8; i++) Whiff();
            Assert.AreEqual(12, b.Player.Hp);
            Assert.IsFalse(b.Turn.Log.Any(l => l.Contains("鏡蝕")));
        }

        [Test]
        public void StagesThatDoNotAllowItIgnoreTheDebugSwitch()
        {
            Setup(".....\n.P.E.\n.....", on: true, allowed: false);
            Assert.IsNull(b.Turn.Stagnation);
            for (int i = 0; i < 6; i++) Whiff();
            Assert.AreEqual(12, b.Player.Hp);
        }

        [Test]
        public void TutorialAndStage2And3StayOff([Values("Stage1", "Stage2", "Stage3")] string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
            Assert.IsFalse(s.stagnationAllowed, "正式採用までは STAGE 1〜3 の挙動を変えない（STAGE 1 はチュートリアルなので常に OFF）");
        }

        [Test]
        public void MultiTestStagesAllowIt([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
            Assert.IsTrue(s.stagnationAllowed);
            Assert.AreEqual(3, s.stagnationMax);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, s.stagnationPenalties);
        }
    }

    /// <summary>【検証用】M1〜M3 の手札方式 A/B 用デッキ：攻撃札が来ない偶然を減らし、A と B を同じ初手・同じ山札順で比べる。</summary>
    public class ValidationDeckTests
    {
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");

        [Test]
        public void DeckIsTenCardsWithThreeSlashes([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            var deck = Stage(name).GetHandTestDeck();
            Assert.AreEqual(10, deck.Count);
            var counts = deck.GroupBy(c => c.displayName).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(3, counts["斬撃"]);
            Assert.AreEqual(2, counts["押し出し"]);
            Assert.AreEqual(3, counts["一歩"]);
            Assert.AreEqual(2, counts["駆け足"]);
        }

        [Test]
        public void OpeningIsFixedAndSameForAAndB([Values("StageM1", "StageM2", "StageM3")] string name,
            [Values(HandMode.DeckA, HandMode.DeckB)] HandMode mode)
        {
            var root = new GameObject("DeckTest");
            var turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(root.AddComponent<GridManager>(), new List<StageData> { Stage(name) });
            turn.CurrentHandMode = mode;
            turn.LoadStage(0);
            CollectionAssert.AreEqual(new[] { "斬撃", "斬撃", "一歩", "駆け足", "押し出し" }, turn.Deck.Hand.Select(c => c.Data.displayName).ToArray());
            // 山札は決まった順番（一番上から 斬撃・一歩・押し出し・駆け足・一歩）
            CollectionAssert.AreEqual(new[] { "斬撃", "一歩", "押し出し", "駆け足", "一歩" },
                turn.Deck.DrawPile.Reverse().Select(c => c.Data.displayName).ToArray());
            Object.DestroyImmediate(root);
        }

        [Test]
        public void KeepOrderDrawsInListedOrder()
        {
            var cards = new[] { "a", "b", "c", "d", "e", "f", "g" }.Select(n =>
            {
                var c = ScriptableObject.CreateInstance<CommandData>();
                c.displayName = n;
                return c;
            }).ToList();
            var deck = new DeckState(new System.Random(1), policy: DrawPolicy.DrawTwo);
            deck.StartBattle(cards, 2, true);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, deck.Hand.Select(c => c.Data.displayName).ToArray(), "初手2枚固定＋足りない分は順番どおり");
            deck.UseCards(deck.Hand.ToList());
            deck.StartTurnDraw();
            CollectionAssert.AreEqual(new[] { "f", "g" }, deck.Hand.Select(c => c.Data.displayName).ToArray());
            foreach (var c in cards) Object.DestroyImmediate(c);
        }

        static List<CommandData> Cards(params (string name, int damage)[] items) => items.Select(i =>
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = i.name;
            c.damage = i.damage;
            return c;
        }).ToList();

        [Test]
        public void GuaranteeSwapsInAnAttackWithoutChangingHandSize()
        {
            var cards = Cards(("一歩", 0), ("駆け足", 0), ("押し出し", 0), ("一歩2", 0), ("斬撃", 4));
            var deck = new DeckState(new System.Random(1), handSize: 3, policy: DrawPolicy.DrawTwo);
            deck.StartBattle(cards, 0, true); // 手札：一歩・駆け足・押し出し／山札：一歩2 → 斬撃
            var swapped = deck.EnsureAttackInHand();
            Assert.AreEqual("斬撃", swapped.Data.displayName);
            Assert.AreEqual(3, deck.Hand.Count, "枚数は変わらない");
            Assert.IsTrue(deck.Hand.Any(c => c.Data.displayName == "斬撃"));
            Assert.AreEqual("押し出し", deck.DrawPile[0].Data.displayName, "手札から抜いた札は山札の一番下へ");
            Assert.IsNull(deck.EnsureAttackInHand(), "攻撃札があれば何もしない");
            foreach (var c in cards) Object.DestroyImmediate(c);
        }

        [Test]
        public void GuaranteeUsesTheDiscardPileWhenTheDrawPileHasNoAttack()
        {
            var cards = Cards(("斬撃", 4), ("一歩", 0), ("駆け足", 0), ("押し出し", 0));
            var deck = new DeckState(new System.Random(1), handSize: 1, policy: DrawPolicy.DrawTwo);
            deck.StartBattle(cards, 0, true); // 手札：斬撃
            deck.UseCards(deck.Hand.ToList()); // 斬撃は捨て札へ
            deck.Draw(1); // 一歩
            var swapped = deck.EnsureAttackInHand();
            Assert.AreEqual("斬撃", swapped.Data.displayName);
            Assert.IsTrue(deck.DiscardPile.Any(c => c.Data.displayName == "一歩"));
            foreach (var c in cards) Object.DestroyImmediate(c);
        }

        [Test]
        public void MultiTestStagesUseTheGuarantee([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            Assert.IsTrue(Stage(name).handTestGuaranteeAttack);
        }

        [Test]
        public void LongBattleDoublesEnemyHpOnlyOnTestStages([Values(false, true)] bool testStage)
        {
            var b = new TestBattle(".....\n.P.E.\n.....", enemyHp: 8);
            b.Stage.isTestStage = testStage;
            b.Turn.LongBattleDebugOn = true;
            b.Turn.LoadStage(0);
            Assert.AreEqual(testStage ? 16 : 8, b.Enemy.MaxHp);
            Assert.AreEqual(testStage ? 16 : 8, b.Enemy.Hp);
            b.Destroy();
        }

        [Test]
        public void WithoutTheSettingsTheShuffleIsUnchanged()
        {
            // 指定なし（初手0・順番固定なし）なら、従来の StartBattle と同じ並びになる（RUN・既存の比較に影響しない）
            var cards = Enumerable.Range(0, 8).Select(i =>
            {
                var c = ScriptableObject.CreateInstance<CommandData>();
                c.displayName = "c" + i;
                return c;
            }).ToList();
            var a = new DeckState(new System.Random(42));
            a.StartBattle(cards);
            var b = new DeckState(new System.Random(42));
            b.StartBattle(cards, 0, false);
            CollectionAssert.AreEqual(a.Hand.Select(c => c.Data).ToList(), b.Hand.Select(c => c.Data).ToList());
            CollectionAssert.AreEqual(a.DrawPile.Select(c => c.Data).ToList(), b.DrawPile.Select(c => c.Data).ToList());
            foreach (var c in cards) Object.DestroyImmediate(c);
        }
    }
}
