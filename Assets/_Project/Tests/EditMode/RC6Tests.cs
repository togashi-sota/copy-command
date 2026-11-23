using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC6・本人の実プレイのあと】手札の欄を描いている途中で緑の札を使うと ArgumentOutOfRangeException（2階・盾と槍の挟撃・TURN 6・緑3枚）。
    /// 手札の形（移動・攻撃が切れない・緑と紫は少なめ）・敵のダメージ・報酬・商人・休息・強化。
    /// </summary>
    public class RC6Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            BuildInfo.SetForTest(null);
            BattleUI.DebugClickTacticIndex = -1;
        }

        (ActRunManager act, TurnManager turn) NewGame()
        {
            var root = new GameObject("RC6");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stages = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            turn.SetupForTest(grid, stages);
            var act = root.AddComponent<ActRunManager>();
            act.SetupForTest(turn, cards, stages);
            return (act, turn);
        }

        // ───── 例外の再現：描いている途中で緑の札を使う ─────

        [Test]
        public void UsingATacticCardWhileTheHandIsBeingDrawnDoesNotShiftTheList()
        {
            var (act, turn) = NewGame();
            act.Profile.finalFinished = 1;
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 11, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            Assert.AreEqual(ActScreen.Battle, act.Screen);
            Assert.IsNotNull(turn.Tactics);
            turn.Tactics.HandMax = 3; turn.Tactics.Draw(2); // 【RC6】緑の手札は1枚（指揮官は2枚）になった。例外が出た形（緑が2枚以上）を作る
            Assume.That(turn.Tactics.Hand.Count >= 2, "緑の札が2枚以上ある種");
            // 描く前に写した手札（UI と同じ関数）
            var drawn = BattleUI.TacticHandForDrawing(turn);
            int before = drawn.Count;
            var first = drawn.FirstOrDefault(c => TacticRules.TargetOf(c.tactic) == TacticTarget.None && c.tactic != TacticKind.Sift && turn.CanPlayTactic(c));
            Assume.That(first != null, "対象のいらない緑の札がある種");
            // 1枚目を使う（描いている途中にクリックしたのと同じ）→ 本当の手札は減る
            Assert.IsTrue(turn.PlayTactic(first));
            Assert.Less(turn.Tactics.Hand.Count, before, "使った札は手札から消える（前はこれで次の札を読みにいって範囲外になった）");
            // 写した手札は最後まで読める（例外なし）
            Assert.DoesNotThrow(() => { for (int j = 0; j < before; j++) Assert.IsNotNull(drawn[j]); });
            Assert.AreEqual(before, drawn.Count, "写しは変わらない");
        }

        // ───── 手札の形：移動・攻撃は2枚ずつ・紫は1枚まで（目安）・緑は1枚（指揮官は2枚） ─────

        CommandData Card(CommandType t, string n)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.type = t; c.displayName = n; c.name = n;
            created.Add(c);
            return c;
        }

        /// <summary>移動3・攻撃3・紫4・ほか2 のデッキ（紫が多く、移動・攻撃が少ない：何も直さないと切れやすい形）。</summary>
        List<CommandData> PurpleHeavyDeck()
        {
            var list = new List<CommandData>();
            for (int i = 0; i < 3; i++) list.Add(Card(CommandType.Rush, "移動" + i));
            for (int i = 0; i < 3; i++) list.Add(Card(CommandType.Slash, "攻撃" + i));
            for (int i = 0; i < 4; i++) list.Add(Card(CommandType.Push, "紫" + i));
            for (int i = 0; i < 2; i++) list.Add(Card(CommandType.Spirit, "ほか" + i));
            return list;
        }

        [Test]
        public void EveryRefillHasTwoMovesAndTwoAttacksWhenTheDeckHasThem()
        {
            HandRules.ResetExperiments();
            Assert.AreEqual(HandShape.Guarantee, HandRules.Mode, "ゲームの既定は A 案（足りないときだけ直す）");
            var cards = PurpleHeavyDeck();
            for (int seed = 0; seed < 200; seed++)
            {
                var rng = new System.Random(seed);
                var deck = new DeckState(rng, handSize: 7, policy: DrawPolicy.RefillSeven) { BaseHand = 7 };
                deck.StartBattle(cards);
                for (int t = 0; t < 8; t++)
                {
                    Assert.GreaterOrEqual(HandRules.Count(deck.Hand, HandRole.Move), 2, $"種 {seed}・{t + 1}ターン目：移動が2枚ない");
                    Assert.GreaterOrEqual(HandRules.Count(deck.Hand, HandRole.Attack), 2, $"種 {seed}・{t + 1}ターン目：攻撃が2枚ない");
                    // 3枚使う（移動・攻撃を優先して使う＝一番切れやすい使い方）
                    var use = deck.Hand.OrderBy(c => HandRules.RoleOf(c.Data) == HandRole.Control ? 1 : 0).ThenBy(_ => rng.Next()).Take(3).ToList();
                    deck.UseCards(use);
                    deck.StartTurnDraw();
                }
            }
        }

        [Test]
        public void PurpleStaysAroundOneWhenOtherCardsCanBeDrawn()
        {
            HandRules.ResetExperiments();
            var cards = PurpleHeavyDeck();
            int over = 0, turns = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var rng = new System.Random(seed);
                var deck = new DeckState(rng, handSize: 7, policy: DrawPolicy.RefillSeven) { BaseHand = 7 };
                deck.StartBattle(cards);
                Assert.LessOrEqual(HandRules.Count(deck.Hand, HandRole.Control), 1, $"種 {seed}：戦闘の始めの紫は1枚まで（ほかの札が引けるとき）");
                for (int t = 0; t < 6; t++)
                {
                    var use = deck.Hand.Where(c => HandRules.RoleOf(c.Data) != HandRole.Control).Take(3).ToList();
                    deck.UseCards(use);
                    deck.StartTurnDraw();
                    turns++;
                    if (HandRules.Count(deck.Hand, HandRole.Control) > 1) over++;
                }
            }
            // 紫を使わない人でも、ターンの始めに多い紫は捨て札へ（手札の紫は1枚まで）
            Assert.AreEqual(0, over, $"紫が2枚以上のターン {over}/{turns}");
        }

        [Test]
        public void LegacyModeStillDrawsFromTheTopOnly()
        {
            HandRules.Mode = HandShape.Legacy;
            try
            {
                var cards = PurpleHeavyDeck();
                var a = new DeckState(new System.Random(5), handSize: 7, policy: DrawPolicy.RefillSeven) { BaseHand = 7 };
                a.StartBattle(cards);
                Assert.AreEqual(0, a.Corrections, "前の方式は順番を曲げない");
            }
            finally { HandRules.ResetExperiments(); }
        }

        [Test]
        public void TacticHandIsOneCardAndTwoForTheCommander()
        {
            foreach (var (role, max) in new[] { ("standard", 1), ("observer", 1), ("commander", 2) })
            {
                var (act, turn) = NewGame();
                act.Profile.finalFinished = 1;
                act.StartRun(ActContent.Roles.First(r => r.Id == role), 21, true, false, true, true);
                if (act.Screen == ActScreen.Boon) act.SkipBoon();
                act.ChooseDoor(act.Run.Doors[0]);
                Assert.AreEqual(ActScreen.Battle, act.Screen);
                Assert.AreEqual(max, turn.Tactics.HandMax, role);
                Assert.LessOrEqual(turn.Tactics.Hand.Count, max, role + "：戦闘の始め");
                turn.Tactics.StartTurn(); turn.Tactics.StartTurn();
                Assert.LessOrEqual(turn.Tactics.Hand.Count, max, role + "：何ターンたっても");
                Assert.AreEqual(0, TurnRules.UnusedSlotTacticDraw, "使わない枠で緑を引く決まりは無し");
            }
        }

        // ───── 強化：自分＋・まね＋・まね－ ─────

        static CommandData Asset(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");

        StageData NewStage(string map, string hp = "", string kinds = "")
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map; s.maxSlots = 3; s.playerMaxHp = 20; s.enemyMaxHp = 8; s.spikeDamage = 3;
            s.enemyHpList = hp; s.enemyCopyTypes = kinds;
            s.combatRoom = true; s.combatV4 = true;
            s.availableCommands = new List<CommandData>();
            return s;
        }

        /// <summary>あなたの右隣に敵A（全まね）、そのさらに右に敵B。斬撃を右へ：あなたは A を、A のまねは B を斬る。</summary>
        (int a, int b) SlashRight(int forge, int copyDelta)
        {
            var m = BattleModel.FromStage(NewStage("PEE....\n.......\n.......", "20,20", "AA"));
            var slash = Asset("Slash");
            var inst = new CardInstance(slash) { Forge = forge, CopyDelta = copyDelta };
            TurnRules.PlayCommandsAndCopies(m, new List<CommandRecord> { new CommandRecord(slash, Direction.Right, 1, inst) }, null, new List<BattleEvent>());
            return (20 - m.Enemies.ElementAt(0).Hp, 20 - m.Enemies.ElementAt(1).Hp);
        }

        [Test]
        public void ThreeUpgradesChangeYouOrTheCopyOnly()
        {
            int d = Asset("Slash").damage;
            Assert.AreEqual((d + DeckState.ForgeBonus, d), SlashRight(DeckState.ForgeBonus, 0), "自分＋：あなただけ +2");
            Assert.AreEqual((d + DeckState.CopyUpSelf, d + DeckState.CopyUpgrade), SlashRight(DeckState.CopyUpSelf, DeckState.CopyUpgrade), "まね＋：敵のまね +3・あなた +1（敵どうしに強い・両刃）");
            Assert.AreEqual((d + DeckState.CopyDownSelf, d - DeckState.CopyDowngrade), SlashRight(DeckState.CopyDownSelf, -DeckState.CopyDowngrade), "まね－：敵のまね -2・あなた +1（安全）");
        }

        static ActRun NewRun(int seed, string role = "standard") => new ActRun(ActContent.Roles.First(x => x.Id == role), seed, Asset, true, false, true, true);

        [Test]
        public void RunUpgradesOnceWithOneKindAndTheBattleAndSaveCarryIt()
        {
            var run = NewRun(5);
            var slash = run.Deck.First(c => c.name == "Slash");
            var walk = run.Deck.First(c => c.name == "Walk");
            Assert.IsFalse(run.UpgradeCard(walk, UpgradeKind.CopyUp), "移動の札は強化できない");
            Assert.IsTrue(run.UpgradeCard(slash, UpgradeKind.CopyUp));
            Assert.IsFalse(run.UpgradeCard(slash, UpgradeKind.Self), "同じ種類は1回（どの形でも）");
            Assert.AreEqual(UpgradeKind.CopyUp, run.UpgradeOf("Slash"));
            var setup = run.MakeBattleSetup();
            Assert.IsTrue(setup.Forged.Contains("Slash@+"));
            var deck = new DeckState(new System.Random(1)) { ForgedNames = new HashSet<string>(setup.Forged) };
            deck.StartBattle(run.Deck);
            var all = deck.Hand.Concat(deck.DrawPile).Where(c => c.Data.name == "Slash").ToList();
            Assert.IsTrue(all.All(c => c.CopyDelta == DeckState.CopyUpgrade && c.Forge == DeckState.CopyUpSelf), "まね＋の札：まね +3・あなた +1");
            // セーブして戻す
            Assert.IsTrue(ActSave.TryRestore(ActSave.ToJson(run), Asset, out var back, out var err), err);
            Assert.AreEqual(UpgradeKind.CopyUp, back.UpgradeOf("Slash"), "セーブに強化の形が残る");
            // 古いセーブ（名前だけ）は自分＋
            var old = NewRun(6);
            old.Forged.Add("Slash");
            Assert.AreEqual(UpgradeKind.Self, old.UpgradeOf("Slash"));
        }

        // ───── 報酬・商人・休息 ─────

        [Test]
        public void NormalRewardIsThreeCommandCardsAndMidBossAddsAFreeUpgrade()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = NewRun(seed);
                run.SetFloorForTest(4, V4Content.Room("v4_n5"));
                run.WinBattle(new BattleStats { Won = true, Turns = 6, HpBefore = 20, HpAfter = 18 });
                Assert.AreEqual(3, run.RewardCards.Count, $"種 {seed}：通常戦は3枚から");
                Assert.IsTrue(run.RewardCards.All(r => !r.IsTactic), "通常戦は命令札（緑は中ボスと商人）");
                Assert.AreEqual(1, run.RewardPicksLeft);
                Assert.GreaterOrEqual(run.LastRewardShards, 15, "ルーン片 15〜");
                Assert.AreEqual(0, run.RewardUpgradesLeft);
                var mid = NewRun(seed);
                mid.SetFloorForTest(5, V4Content.Room("v4_m1"));
                mid.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 20, HpAfter = 12 });
                Assert.AreEqual(2, mid.RewardPicksLeft, "中ボスは2枚");
                Assert.AreEqual(1, mid.RewardUpgradesLeft, "中ボスは無料の強化も");
                Assert.Greater(mid.LastRewardShards, run.LastRewardShards, "中ボスのルーン片は多い");
            }
        }

        [Test]
        public void ShopHasThreeCommandsTwoTacticsAndOnePaidUpgrade()
        {
            var run = NewRun(8);
            run.SetShardsForStudy(200);
            run.SetFloorForTest(3, ActContent.Room("shop"));
            run.ResumeRoom(ActContent.Room("shop"));
            Assert.AreEqual(3, run.ShopCards.Count(c => c.Card.type != CommandType.Tactic), "命令札3");
            Assert.AreEqual(2, run.ShopCards.Count(c => c.Card.type == CommandType.Tactic), "戦術札2");
            Assert.IsTrue(run.ShopCards.Where(c => c.Card.type != CommandType.Tactic).Any(c => run.Pool4Rarity(c.Card) != Rarity.Common), "1枚は珍しい札");
            var slash = run.Deck.First(c => c.name == "Slash");
            Assert.IsTrue(run.BuyUpgrade(slash, UpgradeKind.CopyDown));
            Assert.AreEqual(200 - run.UpgradePrice, run.Shards);
            var thrust = run.Deck.FirstOrDefault(c => c.name != "Slash" && V4Content.CanUpgrade(c, UpgradeKind.Self));
            Assume.That(thrust != null);
            Assert.IsFalse(run.BuyUpgrade(thrust, UpgradeKind.Self), "強化は1回の訪問で1回");
            // 最初の商人で何か1つは買える（3戦ぶんのルーン片）
            var poor = NewRun(9);
            poor.SetShardsForStudy(3 * ActContent.ShardsFor(RoomKind.Battle));
            poor.SetFloorForTest(3, ActContent.Room("shop"));
            poor.ResumeRoom(ActContent.Room("shop"));
            Assert.IsTrue(poor.ShopCards.Any(c => c.Price <= poor.Shards) || poor.ShopItems.Any(i => i.Price <= poor.Shards) || poor.UpgradePrice <= poor.Shards, "3戦で何か買える");
        }

        [Test]
        public void ShopUpgradeAndMidBossFreeUpgradeSurviveSaveAndContinue()
        {
            // 商人：強化を買ったあとで中断 → 続きからでも「強化は済み」
            var run = NewRun(12);
            run.SetShardsForStudy(200);
            run.SetFloorForTest(3, ActContent.Room("shop"));
            run.ResumeRoom(ActContent.Room("shop"));
            var slash = run.Deck.First(c => c.name == "Slash");
            Assert.IsTrue(run.BuyUpgrade(slash, UpgradeKind.Self));
            var json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
            Assert.IsTrue(ActSave.TryRestore(json, Asset, out var back, out var err), err);
            Assert.IsTrue(back.ShopUpgradeUsed, "商人の強化は1回の訪問で1回（続きからでも）");
            Assert.AreEqual(UpgradeKind.Self, back.UpgradeOf("Slash"));
            Assert.AreEqual(run.Shards, back.Shards);
            // 中ボスの報酬：無料の強化が残ったまま中断 → 続きからでも使える
            var mid = NewRun(13);
            mid.SetFloorForTest(5, V4Content.Room("v4_m1"));
            mid.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 20, HpAfter = 12 });
            Assert.AreEqual(1, mid.RewardUpgradesLeft);
            var json2 = JsonUtility.ToJson(ActSave.CaptureRoom(mid, "reward"));
            Assert.IsTrue(ActSave.TryRestore(json2, Asset, out var back2, out var err2), err2);
            Assert.AreEqual(1, back2.RewardUpgradesLeft, "中ボスの無料の強化は続きからでも残る");
            back2.UseRewardUpgrade();
            Assert.AreEqual(0, back2.RewardUpgradesLeft);
        }

        [Test]
        public void HandIsSortedMoveAttackPurpleOther()
        {
            var cards = PurpleHeavyDeck();
            var deck = new DeckState(new System.Random(3), handSize: 7, policy: DrawPolicy.RefillSeven) { BaseHand = 7 };
            deck.StartBattle(cards);
            deck.SortHandByRole();
            var roles = deck.Hand.Select(c => (int)HandRules.RoleOf(c.Data)).ToList();
            CollectionAssert.AreEqual(roles.OrderBy(x => x).ToList(), roles, "移動 → 攻撃 → 紫 → そのほか の順");
        }

        [Test]
        public void RestGivesTwoDifferentChoicesAndSurvivesSave()
        {
            var run = NewRun(14);
            run.SetHpForStudy(10);
            run.SetFloorForTest(3, ActContent.Room("rest"));
            run.ResumeRoom(ActContent.Room("rest"));
            Assert.AreEqual(1, run.RestActions, "【通常リリース版】休息は3つから1つ（前は2つ：ほぼ自動で決まった）");
            Assert.IsTrue(run.CanRest("heal"));
            var json0 = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
            run.RestHeal(); run.MarkRest("heal");
            Assert.IsFalse(run.CanRest("heal"), "同じものは1回");
            Assert.IsFalse(run.CanRest("upgrade"), "1つで終わり");
            Assert.IsTrue(run.RestFinished);
            var json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
            Assert.IsTrue(ActSave.TryRestore(json, Asset, out var back, out var err), err);
            Assert.IsTrue(back.RestDone.Contains("heal"), "続きからでも、回復は済み");
            Assert.IsTrue(back.RestFinished, "続きからでも、休息は終わっている");
            Assert.IsTrue(ActSave.TryRestore(json0, Asset, out var before, out var err0), err0);
            Assert.IsTrue(before.CanRest("remove"), "選ぶ前に中断したら、続きからでも選べる");
        }

        [Test]
        public void NoFreeSwapAfterTakingARewardSoTheDeckGrows()
        {
            Assert.IsFalse(V4Content.FreeSwap);
            var run = NewRun(15);
            run.SetFloorForTest(1, V4Content.Room("v4_n5"));
            run.WinBattle(new BattleStats { Won = true, Turns = 6, HpBefore = 20, HpAfter = 18 });
            int deck = run.Deck.Count;
            Assert.IsTrue(run.TakeRewardCard(run.RewardCards[0]));
            Assert.AreEqual(deck + 1, run.Deck.Count, "デッキが1枚増える");
            Assert.AreEqual(0, run.SwapsLeft, "入れ替えは無い（削除は商人・休息で）");
        }

        [Test]
        public void RemovingTheLastCopyForgetsItsUpgradeAndOldSavesGetNoSwap()
        {
            var run = NewRun(16);
            var slash = run.Deck.First(c => c.name == "Slash");
            Assert.IsTrue(run.UpgradeCard(slash, UpgradeKind.CopyUp));
            int guard = 0;
            while (run.Deck.Any(c => c.name == "Slash") && guard++ < 10) Assert.IsTrue(run.RestRemoveAt(run.Deck.FindIndex(c => c.name == "Slash")));
            Assert.IsNull(run.UpgradeOf("Slash"), "最後の1枚を消したら強化の記録も消える（取り直しても強化なし）");
            run.RestoreSwaps(1, 1);
            Assert.AreEqual(0, run.SwapsLeft + run.TacticSwapsLeft, "古いセーブ（入れ替えの残りあり）から続けても、入れ替えは出ない");
        }

        // ───── 敵の強さ・孤立・ボス ─────

        [Test]
        public void EnemyPowerIsLowerEarlyAndIsolationDoublesTheLastEnemysDamage()
        {
            Assert.AreEqual(2, V4Content.RedByFloor[0], "1階の赤は 2");
            Assert.AreEqual(3, V4Content.RedByFloor[7], "8階の赤は 3");
            Assert.IsTrue(V4Content.LastStandDouble);
            var m = BattleModel.FromStage(NewStage("PE.....\n.......\n......E", "20,20", "AA"));
            m.LastStandDouble = true;
            var a = m.Enemies.ElementAt(0);
            var b = m.Enemies.ElementAt(1);
            RulesEngine.ApplyDamage(m, m.Player, null, false, a, 4, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(16, a.Hp, "2体いるうちは そのまま");
            b.Hp = 0;
            Assert.IsTrue(RulesEngine.IsLastStanding(m, a));
            RulesEngine.ApplyDamage(m, m.Player, null, false, a, 4, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(8, a.Hp, "最後の1体は2倍");
        }

        [Test]
        public void MirrorLordTelegraphsAPillarEveryTurnInPhaseOne()
        {
            Assert.IsTrue(CombatBoss.PillarEveryTurn);
            var stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXC.asset");
            Assume.That(stage != null, "ボスの部屋");
            var m = BattleModel.FromStage(stage);
            var lord = CombatBoss.Find(m);
            Assert.IsNotNull(lord);
            Assert.Greater(lord.BombCells.Count, 0, "1ターン目から光柱が予告されている");
            int telegraphed = 0;
            for (int t = 0; t < 6; t++)
            {
                CombatBoss.Trait(m, lord, new List<BattleEvent>());
                if (lord.BombCells.Count > 0) telegraphed++;
                m.Player.Hp = m.Player.MaxHp;
            }
            Assert.AreEqual(3, telegraphed, "【RC10】光柱は1ターンおき（予告 → 落ちる → 次のターンに予告）。RC6〜RC9 は毎ターン（2マス逃げる作業になった）");
        }
    }
}
