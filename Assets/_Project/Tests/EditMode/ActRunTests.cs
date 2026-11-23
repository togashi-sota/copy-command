using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】ACT1 縦切り：ラン（扉・報酬・ルーン片・商人・休息・特殊部屋・刻印・役職）と、戦闘への持ち込み
    /// （命令の上限・コピーされない命令・7枚補充＋追加ドロー）。
    /// </summary>
    public class ActRunTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static ActRun NewRun(int role = 0, int seed = 7) => new ActRun(ActContent.Roles[role], seed, Card);

        static BattleStats Win(int hp, int turns = 3, params EnemyKind[] defeated) =>
            new BattleStats { Won = true, Turns = turns, HpBefore = hp, HpAfter = hp - 2, Defeated = defeated.ToList() };

        [Test]
        public void ContentAssetsExist()
        {
            foreach (var c in ActContent.CardPool) Assert.IsNotNull(Card(c.Asset), c.Asset);
            foreach (var n in ActContent.StartDeck) Assert.IsNotNull(Card(n), n);
            foreach (var r in ActContent.Rooms.Where(r => r.Stage != null)) Assert.IsNotNull(Stage(r.Stage), r.Stage);
            Assert.IsTrue(Card("Sneak").notCopied, "忍び足はコピーされない");
        }

        [Test]
        public void ARunCanBePlayedFromTheFirstDoorToActClear()
        {
            var run = NewRun();
            Assert.AreEqual(12, run.Deck.Count, "最初のデッキ12枚");
            Assert.AreEqual(7, run.FloorCount);
            int guard = 0;
            while (!run.Cleared && guard++ < 20)
            {
                var room = run.Doors[0];
                Assert.IsTrue(run.Enter(room));
                switch (room.Kind)
                {
                    case RoomKind.Battle:
                    case RoomKind.Danger:
                    case RoomKind.Boss:
                        run.WinBattle(Win(run.Hp));
                        if (run.Cleared) break;
                        Assert.Greater(run.RewardCards.Count, 0);
                        run.TakeRewardCard(run.RewardCards[0]);
                        run.Leave();
                        break;
                    case RoomKind.Rest: run.RestHeal(); run.Leave(); break;
                    case RoomKind.Shop: run.Leave(); break;
                    default: run.Leave(); break;
                }
            }
            Assert.IsTrue(run.Cleared, "ボスに勝つと ACT1 CLEAR");
            Assert.Greater(run.Shards, 0, "戦闘の終わりにルーン片");
            Assert.AreEqual(5, run.BattlesWon, "通常4＋危険1＋ボス… の扉の左側を選ぶと戦闘5回（休息・商人をはさむ）");
        }

        [Test]
        public void MapHasRestShopDangerAndBoss()
        {
            var run = NewRun();
            Assert.IsTrue(run.Map.Take(3).Last().Any(r => r.Kind == RoomKind.Rest) && run.Map[2].Any(r => r.Kind == RoomKind.Shop), "3階は休息か商人");
            Assert.IsTrue(run.Map[3].Any(r => r.Kind == RoomKind.Danger), "4階に危険戦（2026-10-05 地図 v2：もう1つは通常戦）");
            Assert.AreEqual(RoomKind.Boss, run.Map.Last().Single().Kind);
            Assert.IsTrue(run.Map.Take(6).All(f => f.Count >= 2), "ボス以外の階は2つ以上の扉");
        }

        [Test]
        public void NormalRewardsHaveACopySlotAndCanBeSkipped()
        {
            var run = NewRun();
            run.Enter(run.Doors[0]);
            run.WinBattle(Win(run.Hp, 3, EnemyKind.Archer));
            Assert.AreEqual(4, run.RewardCards.Count, "通常戦は4枚（写し取り1＋3）");
            Assert.AreEqual(1, run.RewardCards.Count(c => c.IsCopySlot));
            CollectionAssert.Contains(new[] { "Pierce", "Thrust" }, run.RewardCards.First(c => c.IsCopySlot).Card.name, "弓兵を倒した → 射線の札");
            Assert.AreEqual(1, run.RewardPicksLeft);
            int deck = run.Deck.Count;
            run.Leave(); // 取らずに次へ（スキップ）
            Assert.AreEqual(deck, run.Deck.Count);
        }

        [Test]
        public void DangerRewardsOfferFiveCardsTwoPicksAndAnImprint()
        {
            var run = NewRun();
            while (run.Doors[0].Kind != RoomKind.Danger) { run.Enter(run.Doors[0]); run.Leave(); }
            run.Enter(run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 4, HpBefore = 20, HpAfter = 15, EnvironmentKills = 1, FriendlyFire = 3, FriendlyKills = 1 });
            Assert.AreEqual(5, run.RewardCards.Count);
            Assert.AreEqual(2, run.RewardPicksLeft);
            Assert.GreaterOrEqual(run.RewardCards.Count(c => c.IsCopySlot), 2, "同士討ち・環境でとどめ → 写し取り2枚");
            Assert.AreEqual(2, run.RewardImprints.Count, "刻印2択");
            Assert.IsTrue(run.TakeRewardCard(run.RewardCards[0]));
            Assert.IsTrue(run.TakeRewardCard(run.RewardCards[0]));
            Assert.IsFalse(run.TakeRewardCard(run.RewardCards[0]), "3枚目は取れない");
            Assert.IsTrue(run.TakeRewardImprint(run.RewardImprints[0]));
            Assert.AreEqual(1, run.Imprints.Count);
        }

        [Test]
        public void MissionPaysExtraShardsOnlyWhenDone()
        {
            var run = NewRun();
            while (run.Doors[0].Kind != RoomKind.Danger) { run.Enter(run.Doors[0]); run.Leave(); }
            var room = run.Doors.First(r => r.Mission != null && r.Mission.Kind == MissionKind.EnvironmentKill);
            run.Enter(room);
            int before = run.Shards;
            run.WinBattle(new BattleStats { Won = true, Turns = 4, HpBefore = 20, HpAfter = 20, EnvironmentKills = 2 });
            Assert.AreEqual(before + ActContent.ShardsFor(RoomKind.Danger) + room.Mission.Reward + ActContent.NoDamageBonus, run.Shards, "無傷ボーナス込み");
            Assert.AreEqual(1, run.MissionsDone);
        }

        [Test]
        public void ShopSellsCardsItemsImprintAndRemovesCards()
        {
            var run = NewRun();
            while (!run.Doors.Any(r => r.Kind == RoomKind.Shop)) { run.Enter(run.Doors[0]); run.Leave(); }
            run.Enter(run.Doors.First(r => r.Kind == RoomKind.Shop));
            Assert.AreEqual(4, run.ShopCards.Count);
            Assert.AreEqual(2, run.ShopItems.Count);
            Assert.IsFalse(run.BuyCard(run.ShopCards[0]), "ルーン片が足りない");
            run.AddShards(300);
            int deck = run.Deck.Count;
            Assert.IsTrue(run.BuyCard(run.ShopCards[0]));
            Assert.AreEqual(deck + 1, run.Deck.Count);
            Assert.IsTrue(run.BuyItem(run.ShopItems[0]));
            Assert.IsTrue(run.BuyItem(run.ShopItems[1]));
            Assert.AreEqual(2, run.Items.Count, "アイテムは2つまで");
            int price = run.RemovePrice;
            Assert.IsTrue(run.RemoveCardPaid(run.Deck[0]));
            Assert.AreEqual(price + ActContent.RemovePriceStep, run.RemovePrice, "削除は使うたびに高くなる");
            Assert.IsFalse(run.RemoveCardPaid(run.Deck[0]), "1回の訪問で1回");
            Assert.IsTrue(run.BuyImprint());
        }

        [Test]
        public void ImprintsChangeTheBattleSetup()
        {
            var run = NewRun();
            Assert.AreEqual(0, run.BonusSlots);
            run.Imprints.Add(ImprintId.FourthCommand);
            run.Imprints.Add(ImprintId.ThornGuide);
            var setup = run.MakeBattleSetup();
            Assert.AreEqual(1, setup.BonusSlots, "第四命令：上限 +1");
            Assert.AreEqual(1, setup.SpikeFirstBonus, "棘導 +1（2026-10-05 に +2 から）");
            run.Imprints.Add(ImprintId.Margin);
            run.Imprints.Add(ImprintId.Echo);
            var enemy = new UnitModel(1, "敵", "", Team.Enemy, 10, Vector2Int.zero);
            var copyHit = new List<BattleEvent> { new BattleEvent { Type = BattleEventType.Damaged, Unit = enemy, Amount = 3, Cause = new DamageCause { ByCopy = true, Kind = DamageKind.Spike } } };
            Assert.AreEqual(2, run.RoundEndDraw(copyHit, 2), "反響（コピーで敵が傷ついた）＋余白（2命令以下）");
            Assert.AreEqual(0, run.RoundEndDraw(new List<BattleEvent>(), 3));
        }

        [Test]
        public void RolesStartWithTheirOwnRules()
        {
            var commander = new ActRun(ActContent.Roles.First(r => r.Id == "commander"), 1, Card);
            Assert.AreEqual(ActContent.StartHp - 6, commander.MaxHp);
            Assert.AreEqual(1, commander.BonusSlots, "指揮官は第四命令から");
            var observer = new ActRun(ActContent.Roles.First(r => r.Id == "observer"), 1, Card);
            Assert.AreEqual(7, observer.BaseHand, "観測士の手札は標準と同じ（2026-10-05）");
            Assert.IsTrue(observer.MakeBattleSetup().FullPrediction, "観測士は完全予測");
            Assert.IsFalse(commander.MakeBattleSetup().FullPrediction);
            var mirrorist = new ActRun(ActContent.Roles.First(r => r.Id == "mirrorist"), 1, Card);
            Assert.Contains(ItemId.BlankStone, mirrorist.Items);
            Assert.IsTrue(mirrorist.Deck.Any(c => c.notCopied));
            var standard = NewRun();
            Assert.AreEqual(0, standard.Imprints.Count, "標準は無能力のまま");
        }

        [Test]
        public void BattleStatsFindEnvironmentKillsAndFriendlyFire()
        {
            var a = new UnitModel(1, "敵A", "A", Team.Enemy, 10, Vector2Int.zero) { Kind = EnemyKind.Swordsman };
            var b = new UnitModel(2, "敵B", "B", Team.Enemy, 10, Vector2Int.one) { Kind = EnemyKind.Bomber };
            var events = new List<BattleEvent>
            {
                new BattleEvent { Type = BattleEventType.Damaged, Unit = a, Amount = 3, Cause = new DamageCause { Source = b, Kind = DamageKind.Trait } },
                new BattleEvent { Type = BattleEventType.Died, Unit = a },
                new BattleEvent { Type = BattleEventType.Damaged, Unit = b, Amount = 3, Cause = new DamageCause { Source = b, Kind = DamageKind.Spike, ByCopy = true } },
                new BattleEvent { Type = BattleEventType.Died, Unit = b },
            };
            var s = BattleStats.From(events, true, 3, 20, 18);
            Assert.AreEqual(2, s.EnvironmentKills, "砲兵の爆発とトゲ");
            Assert.AreEqual(1, s.FriendlyFire);
            Assert.AreEqual(1, s.CopyDamage);
            Assert.AreEqual(2, s.DamageTaken);
        }

        // ───── 戦闘への持ち込み（TurnManager） ─────

        TurnManager Battle(ActBattleSetup setup, string stage = "StageV1")
        {
            var root = new GameObject("ActBattle");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var s = Stage(stage);
            turn.SetupForTest(grid, new List<StageData> { s });
            turn.LoadActBattle(s, setup);
            return turn;
        }

        static ActBattleSetup Setup(params string[] deck) => new ActBattleSetup { Hp = 20, MaxHp = 20, Deck = deck.Select(Card).ToList(), Rng = new System.Random(3) };

        /// <summary>初手に無ければ山札から手札へ（テストを決まった形にするため）。</summary>
        static void EnsureInHand(TurnManager turn, System.Func<CardInstance, bool> match)
        {
            if (turn.Deck.Hand.Any(match)) return;
            var c = turn.Deck.DrawPile.First(match);
            Assert.IsTrue(turn.Deck.TakeFromDrawPile(c));
        }

        void Plan(TurnManager turn, CommandType type, Direction dir)
        {
            var card = turn.Deck.Hand.First(c => c.Data.type == type && !turn.IsCardPlanned(c));
            Assert.IsTrue(turn.AddCommand(new CommandRecord(card.Data, dir, card.Data.minDistance, card)), type.ToString());
        }

        [Test]
        public void FourthCommandLetsYouPlanFourAndEnemiesCopyFour()
        {
            var setup = Setup("Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk");
            setup.BonusSlots = 1;
            var turn = Battle(setup);
            Assert.AreEqual(4, turn.MaxSlots);
            for (int i = 0; i < 4; i++) Plan(turn, CommandType.Walk, i % 2 == 0 ? Direction.Up : Direction.Down);
            Assert.IsFalse(turn.CanAddMore);
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(4, turn.EnemyReplay.CopiedCommands.Count, "敵も4命令をコピー");
        }

        [Test]
        public void SneakIsNotCopiedAndTempSlotsLastOneTurn()
        {
            var turn = Battle(Setup("Sneak", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk"));
            EnsureInHand(turn, c => c.Data.notCopied);
            turn.AddTempSlots(1);
            Assert.AreEqual(4, turn.MaxSlots, "命令石：このターンだけ +1");
            Plan(turn, CommandType.Walk, Direction.Up);
            var sneak = turn.Deck.Hand.First(c => c.Data.notCopied);
            turn.AddCommand(new CommandRecord(sneak.Data, Direction.Down, 1, sneak));
            Assert.IsFalse(turn.IsCopied(1));
            Assert.IsTrue(turn.MarkUncopied(0), "空白の石");
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(0, turn.EnemyReplay.CopiedCommands.Count, "どちらもコピーされない");
            Assert.AreEqual(3, turn.MaxSlots, "次のターンは元に戻る");
        }

        [Test]
        public void SevenIsTheMinimumHandAndExtraDrawsCanGoBeyond()
        {
            var deck = Enumerable.Repeat("Walk", 20).ToArray();
            var turn = Battle(Setup(deck));
            Assert.AreEqual(7, turn.Deck.Hand.Count, "初手7");
            Assert.AreEqual(3, turn.DrawExtra(3));
            Assert.AreEqual(10, turn.Deck.Hand.Count, "追加ドローで7枚を超える");
            Plan(turn, CommandType.Walk, Direction.Up);
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(9, turn.Deck.Hand.Count, "7枚以上あれば補充しない（使った1枚だけ減る）");
            Assert.AreEqual(10, turn.DrawExtra(10), "【Puzzle v2】上限なし：山札がある限り引ける");
            Assert.AreEqual(19, turn.Deck.Hand.Count, "15 を超える");
            Assert.AreEqual(1, turn.DrawExtra(10), "デッキの枚数まで（捨て札の1枚を混ぜて引いて終わり）");
        }

        [Test]
        public void ReconsiderAndForesightDrawNextTurn()
        {
            var deck = new[] { "Reconsider", "Foresight", "Walk", "Walk", "Walk", "Walk", "Walk", "Slash", "Slash", "Slash", "Slash", "Slash", "Slash", "Slash" };
            var turn = Battle(Setup(deck));
            EnsureInHand(turn, c => c.Data.type == CommandType.Reconsider);
            EnsureInHand(turn, c => c.Data.type == CommandType.Foresight);
            Plan(turn, CommandType.Reconsider, Direction.Up);
            Plan(turn, CommandType.Foresight, Direction.Up);
            int before = turn.Deck.Hand.Count;
            turn.ExecuteTurnImmediately();
            int expected = System.Math.Max(7, before - 2) + 2;
            Assert.AreEqual(expected, turn.Deck.Hand.Count, "7枚まで補充＋再考の2枚");
            Assert.AreEqual(1, turn.PendingForesight);
            Assert.Greater(turn.ForesightChoices.Count, 0);
            turn.ChooseForesight(turn.ForesightChoices[0]);
            Assert.AreEqual(expected + 1, turn.Deck.Hand.Count);
            Assert.AreEqual(0, turn.PendingForesight);
        }

        [Test]
        public void RallyRaisesTheLimitWhilePlanned()
        {
            var deck = new[] { "Rally", "Walk", "Walk", "Walk", "Walk", "Walk", "Walk" };
            var turn = Battle(Setup(deck));
            Plan(turn, CommandType.Rally, Direction.Up);
            Assert.AreEqual(5, turn.MaxSlots, "号令：上限 +2（この札も1枠）");
            for (int i = 0; i < 4; i++) Plan(turn, CommandType.Walk, i % 2 == 0 ? Direction.Up : Direction.Down);
            Assert.IsFalse(turn.CanAddMore);
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(5, turn.EnemyReplay.CopiedCommands.Count, "号令も含め5命令が敵へ（号令は敵では何もしない）");
        }

        [Test]
        public void ThornGuideAddsDamageOnceARound()
        {
            var m = BattleModel.FromRows(new[] { "P.^E" }, 3, 12, 20);
            m.ApplyStage(null);
            m.SpikeFirstBonus = 2;
            var push = ScriptableObject.CreateInstance<CommandData>();
            created.Add(push);
            push.type = CommandType.Pull; push.range = 3; push.displayName = "引き寄せ";
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(push, Direction.Right, 0) }, null);
            Assert.AreEqual(20 - 3 - 2, m.Enemies.First().Hp, "トゲ 3＋棘導 2");
        }
    }
}
