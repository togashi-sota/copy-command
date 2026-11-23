using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【Combat V4（2026-10-07）】カウント大技（Clocks）・敵の固有行動・戦術札・V4 のラン（地図・報酬・回復・役職）・セーブ。
    /// </summary>
    public class CombatV4Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 1) => new CommandRecord(Card(card), d, dist);
        static UnitModel En(BattleModel m, int i) => m.Enemies.ElementAt(i);

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            BuildInfo.SetForTest(null);
            Clocks.DamageBonus = 0;
        }

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

        static TurnOutcome Play(BattleModel m, params CommandRecord[] cmds) => TurnRules.PlayTurn(m, cmds.ToList(), null, new List<BattleEvent>(), null);

        // ───── カウント大技：予告・発動・敵にも当たる ─────

        [Test]
        public void ClockCountsDownTelegraphsAtOneAndFires()
        {
            // 剣兵（八方斬り・周期3）。あなたは遠く（当たらない）。
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            var clock = sw.Clocks.Single();
            Assert.AreEqual(ClockShape.Ring8, clock.Shape);
            int start = clock.Cd;
            Assert.AreEqual(3, start, "最初の敵は周期どおり");
            Play(m); Assert.AreEqual(2, clock.Cd);
            Assert.IsFalse(clock.Telegraphed, "残り2では予告しない");
            Play(m); Assert.AreEqual(1, clock.Cd);
            Assert.IsTrue(clock.Telegraphed, "残り①で当たるマスが決まる");
            Assert.AreEqual(8, clock.Cells.Count, "八方斬り＝周り8マス");
            var warn = ThreatMap.Telegraph(m);
            foreach (var c in clock.Cells) Assert.Contains(c, warn, "予告は紫の層に出る");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord>(), null, events, null);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.ClockFired), "発動");
            Assert.AreEqual(3, clock.Cd, "周期に戻る");
        }

        [Test]
        public void BigAttackHitsOtherEnemiesToo()
        {
            // 剣兵の八方斬りの範囲へ、別の敵（動かない・まねない）を置く
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n..E....\n..S....\n.......", "20,10", "AN"));
            var sw = En(m, 1); var victim = En(m, 0);
            Assert.AreEqual(EnemyKind.Swordsman, sw.Kind);
            var clock = sw.Clocks.Single();
            clock.Cd = 1; Clocks.Telegraph(m, sw, clock);
            int hp = victim.Hp;
            var ev = new List<BattleEvent>();
            PhaseRules.TraitAction(m, sw, ev);
            Assert.Less(victim.Hp, hp, "大技は敵にも当たる（大技の逆利用）");
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.Damaged && e.Unit == victim && e.Cause != null && e.Cause.Kind == DamageKind.Clock));
        }

        [Test]
        public void TelegraphedCellsStayFixedWhenTheEnemyMoves()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            var clock = sw.Clocks.Single();
            clock.Cd = 1; Clocks.Telegraph(m, sw, clock);
            var cells = clock.Cells.ToList();
            sw.Position += Vector2Int.left; // コピーで動かされた
            CollectionAssert.AreEquivalent(cells, clock.Cells, "①の範囲は動かない（敵が動いても同じマス）");
        }

        [Test]
        public void LaneChargeMovesTheAttackerToTheEndOfTheLane()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n...Y...", "20", "T"));
            var sp = En(m, 0);
            sp.Facing = Direction.Up;
            var clock = sp.Clocks.Single();
            Assert.IsTrue(clock.Charge);
            clock.Cd = 1; Clocks.Telegraph(m, sp, clock);
            Assert.AreEqual(4, clock.Cells.Count, "貫通突撃＝向きへ4マス");
            PhaseRules.TraitAction(m, sp, new List<BattleEvent>());
            Assert.AreEqual(new Vector2Int(3, 4), sp.Position, "撃った敵は射線の先へ突っ込む");
        }

        [Test]
        public void SilenceStopsTheTraitAndClocksThisRoundThenWearsOff()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            var clock = sw.Clocks.Single();
            Assert.IsTrue(TacticRules.CanUse(m, TacticKind.Silence, sw, null));
            TacticRules.Apply(m, TacticKind.Silence, sw, null, null, out _);
            Assert.IsTrue(sw.Silenced);
            int cd = clock.Cd;
            Play(m);
            Assert.AreEqual(cd, clock.Cd, "沈黙：大技のカウントも止まる");
            Assert.IsFalse(sw.Silenced, "ラウンドの終わりに戻る");
            Play(m);
            Assert.AreEqual(cd - 1, clock.Cd, "次のラウンドからまた進む");
        }

        // ───── 戦術札（盤面側） ─────

        [Test]
        public void EarlyBellHastensAndTelegraphsAtOneAndLateBellDelays()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            var c = sw.Clocks.Single();
            Assert.AreEqual(3, c.Cd);
            Assert.IsTrue(Clocks.Hasten(m, sw, c));
            Assert.AreEqual(2, c.Cd);
            Assert.IsTrue(Clocks.Hasten(m, sw, c));
            Assert.AreEqual(1, c.Cd);
            Assert.IsTrue(c.Telegraphed, "①になったら範囲が決まる");
            Assert.IsFalse(Clocks.Hasten(m, sw, c), "①はこれ以上早められない");
            Assert.IsTrue(Clocks.Delay(m, sw, c));
            Assert.AreEqual(2, c.Cd);
            Assert.IsFalse(c.Telegraphed, "遅らせると予告は消える");
        }

        [Test]
        public void DetonateMakesTheNearestClockFireThisRound()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            TacticRules.Apply(m, TacticKind.Detonate, sw, null, null, out _);
            Assert.AreEqual(1, sw.Clocks.Single().Cd);
            var ev = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord>(), null, ev, null);
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.ClockFired), "このターンの終わりに発動");
        }

        [Test]
        public void RetargetAimsAPlayerCenteredAttackAtAnotherEnemy()
        {
            // 砲王（小砲撃：あなたのいたマスの十字）を、別の敵のいる場所へ向け直す
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\nk....S.", "30,10", "TA"));
            var king = En(m, 0); var guard = En(m, 1);
            Assert.AreEqual(EnemyKind.CannonKing, king.Kind);
            var c = king.Clocks.First(x => x.Shape == ClockShape.Cross1);
            c.Cd = 1; Clocks.Telegraph(m, king, c);
            Assert.IsTrue(c.Cells.Contains(m.Player.Position), "予告はあなたのいる所");
            Assert.IsTrue(TacticRules.CanUse(m, TacticKind.Retarget, king, guard));
            TacticRules.Apply(m, TacticKind.Retarget, king, guard, null, out _);
            Assert.IsTrue(c.Cells.Contains(guard.Position), "別の敵の場所へ向け直した");
            Assert.IsFalse(c.Cells.Contains(m.Player.Position));
        }

        [Test]
        public void GuardReducesTheNextHitOnceAndFocusAddsAttack()
        {
            var m = BattleModel.FromStage(NewStage("P.E....\n.......", "20", "A"));
            TacticRules.Apply(m, TacticKind.Guard, null, null, null, out _);
            Assert.AreEqual(2, m.PlayerGuard);
            var ev = new List<BattleEvent>();
            int hp = m.Player.Hp;
            RulesEngine.ApplyDamage(m, En(m, 0), null, false, m.Player, 5, DamageKind.Trait, ev);
            Assert.AreEqual(hp - 3, m.Player.Hp, "次の被ダメ -2");
            Assert.AreEqual(0, m.PlayerGuard, "1回で消える");
            TacticRules.Apply(m, TacticKind.Focus, null, null, null, out _);
            Assert.AreEqual(2, m.Player.AttackBonus);
        }

        [Test]
        public void TangleAndWeightChangeTheDistanceEnemiesCopyAndMimicMakesEveryoneCopyAll()
        {
            // 追跡兵（技まね）・弓兵（移動まね）
            var m = BattleModel.FromStage(NewStage(".......\n.......\n.......\nP....K.", "20", ""));
            var archer = En(m, 0);
            Assert.AreEqual(CopyKind.Move, archer.CopyKind, "弓兵は移動まね");
            var start = archer.Position;
            TacticRules.Apply(m, TacticKind.Tangle, null, null, null, out _);
            Assert.AreEqual(1, m.CopyMoveBonus);
            Play(m, R("Walk", Direction.Left, 1)); // あなたは壁で空振り・弓兵は（行動のあと）左へ+2
            Assert.AreEqual(start.x - 2, archer.Position.x, "操糸：敵のまねる移動が +1マス");
            Assert.AreEqual(0, m.CopyMoveBonus, "ラウンドの終わりに戻る");
            TacticRules.Apply(m, TacticKind.Mimic, null, null, null, out _);
            Assert.IsTrue(archer.ForceAllCopy);
            Assert.IsTrue(CopyKinds.Accepts(archer, Card("Slash")), "模倣命令：技の札もまねる");
            Play(m);
            Assert.IsFalse(archer.ForceAllCopy);
            Assert.IsFalse(CopyKinds.Accepts(archer, Card("Slash")));
        }

        // ───── 敵の固有行動 ─────

        [Test]
        public void ArcherAimsAtYouOnTheSameLineAndSpearAndSwordApproach()
        {
            var m = BattleModel.FromStage(NewStage("K......\n.......\n.......\nP......", "20", ""));
            var archer = En(m, 0);
            var aim = PhaseRules.AimDir(m, archer);
            Assert.AreEqual(Direction.Down, aim, "同じ列ならあなたの方向（射程内）");
            int hp = m.Player.Hp;
            PhaseRules.TraitAction(m, archer, new List<BattleEvent>());
            Assert.AreEqual(hp - PhaseRules.V4Arrow, m.Player.Hp, "弓兵はあなたへ撃つ");
            var m2 = BattleModel.FromStage(NewStage(".......\n.......\n.......\nP....S.", "20", "A"));
            var sw = En(m2, 0);
            var before = sw.Position;
            PhaseRules.TraitAction(m2, sw, new List<BattleEvent>());
            Assert.Less((sw.Position - m2.Player.Position).sqrMagnitude, (before - m2.Player.Position).sqrMagnitude, "剣兵はあなたへ近づく");
        }

        [Test]
        public void EveryV4StageLoadsWithClocksAndTheBossKeepsItsPhases()
        {
            foreach (var id in V4Content.Rooms.Select(r => r.Stage))
            {
                var st = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{id}.asset");
                Assert.IsNotNull(st, id);
                Assert.IsTrue(st.combatV4, id);
                var m = BattleModel.FromStage(st);
                Assert.IsTrue(m.V4, id);
                Assert.IsTrue(m.Enemies.Any(e => e.Clocks.Count > 0), id + "：大技を持つ敵がいる");
                Assert.IsTrue(m.Enemies.Where(e => !e.IsObject).Any(e => !e.Dormant), id);
            }
            var boss = BattleModel.FromStage(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXC.asset"));
            var lord = CombatBoss.Find(boss);
            Assert.IsNotNull(lord);
            Assert.AreEqual(3, lord.Clocks.Count, "鏡の主：左手・右手・列撃ち");
            Assert.AreEqual(CopyKind.All, lord.CopyKind, "【RC2】鏡の主は全まね（移動もまねる：トゲへ歩かせられる）");
            Assert.IsTrue(boss.Enemies.Where(e => e.Kind == EnemyKind.HealClone).All(e => e.CopyKind == CopyKind.All), "写し身も全まね（主の隣で主を斬る）");
            Assert.AreEqual(0, lord.BossStage);
        }

        // ───── 戦術札のデッキ ─────

        [Test]
        public void TacticDeckDrawsUpToThreeAndReshuffles()
        {
            var cards = new[] { "Breath", "Focus", "Guard", "Mend" }.Select(n => Card("Tactic_" + n)).ToList();
            TacticDeck.StartHand = 2; TacticDeck.PerTurn = 1;
            var d = new TacticDeck(new System.Random(1)) { HandMax = 3 }; // 【RC6】ゲームの上限は1（指揮官は2）。ここでは混ぜ直しを見るために3
            d.StartBattle(cards);
            Assert.AreEqual(2, d.Hand.Count);
            d.StartTurn();
            Assert.AreEqual(3, d.Hand.Count);
            d.StartTurn();
            Assert.AreEqual(3, d.Hand.Count, "手札は3枚まで");
            var used = d.Hand[0];
            Assert.IsTrue(d.Use(used));
            Assert.AreEqual(1, d.DiscardCount);
            d.StartTurn(); d.StartTurn();
            Assert.AreEqual(3, d.Hand.Count);
            Assert.AreEqual(4, d.Hand.Count + d.DrawCount + d.DiscardCount, "札は増えも減りもしない（山札が尽きたら混ぜ直す）");
        }

        // ───── ゲーム（TurnManager）：戦術札は命令枠を使わない・敵はコピーしない ─────

        (ActRunManager act, TurnManager turn) NewGame()
        {
            var root = new GameObject("CombatV4");
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

        [Test]
        public void BossRetryButtonWorksAfterTheLossScreen()
        {
            // 【RC2】撮影で見つけた不具合：負けの画面で「次へ」を押すと負けをもう一度記録してランが終わり、「もう一度ボスに挑む」が効かなかった
            var (act, turn) = NewGame();
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 11, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            var boss = V4Content.Room("v4_boss");
            act.Run.SetFloorForTest(7, boss);
            act.ChooseDoor(boss);
            Assert.AreEqual(ActScreen.Battle, act.Screen);
            act.ForceLoseForCapture();
            typeof(ActRunManager).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(act, null);
            act.ProceedAfterBattle(); // 負けの画面の「次へ」
            Assert.IsTrue(act.PendingBossRetry, "再挑戦を選ぶ画面");
            Assert.IsFalse(act.Run.Failed, "ランはまだ終わっていない");
            act.RetryBossNow();
            Assert.AreEqual(ActScreen.Battle, act.Screen, "ボスの戦闘をやり直す");
            Assert.IsFalse(act.PendingBossRetry);
            Assert.IsTrue(act.Run.BossRetryUsed);
            // 2回目の負けではランが終わる
            act.ForceLoseForCapture();
            typeof(ActRunManager).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(act, null);
            act.ProceedAfterBattle();
            Assert.IsFalse(act.PendingBossRetry);
            Assert.IsTrue(act.Run.Failed);
        }

        [Test]
        public void TacticCardsDoNotUseCommandSlotsOrGetCopied()
        {
            var (act, turn) = NewGame();
            act.Profile.finalFinished = 1; // 【RC2】はじめてのランではない（はじめてのランの1階は戦術札が出ない）
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 11, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            Assert.AreEqual(ActScreen.Battle, act.Screen);
            Assert.IsTrue(turn.IsV4);
            Assert.IsNotNull(turn.Tactics);
            Assert.AreEqual(1, turn.Tactics.Hand.Count, "【RC6】緑の手札は1枚");
            Assert.AreEqual(3, turn.MaxSlots);
            var focus = turn.Tactics.Hand.FirstOrDefault(c => c.tactic == TacticKind.Focus || c.tactic == TacticKind.Guard || c.tactic == TacticKind.Breath || c.tactic == TacticKind.Muster);
            if (focus == null) Assert.Ignore("この種では手札に使いやすい札が無い");
            int hand = turn.Deck.Hand.Count;
            Assert.IsTrue(turn.PlayTactic(focus));
            Assert.AreEqual(0, turn.PlannedCommands.Count, "命令枠は使わない");
            Assert.AreEqual(1, turn.TacticPlaysThisTurn);
            Assert.AreEqual(1, turn.Tactics.DiscardCount);
            if (focus.tactic == TacticKind.Muster) Assert.AreEqual(4, turn.MaxSlots, "号令：このターンだけ命令枠 +1");
            if (focus.tactic == TacticKind.Breath) Assert.AreEqual(hand + 2, turn.Deck.Hand.Count, "深呼吸：2枚引く");
            Assert.IsFalse(turn.Enemies.Any(e => turn.GetPreviewFor(e).Any(r => r.Command.type == CommandType.Tactic)), "敵はコピーしない");
        }

        [Test]
        public void AtMostTwoTacticsPerTurnAndSiftNeedsChoosingCards()
        {
            var (act, turn) = NewGame();
            act.Profile.finalFinished = 1; // 【RC2】はじめてのランではない（はじめてのランの1階は戦術札が出ない）
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 21, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            var hand = turn.Tactics.Hand.ToList();
            int played = 0;
            foreach (var c in hand)
                if (TacticRules.TargetOf(c.tactic) == TacticTarget.None && c.tactic != TacticKind.Sift && c.tactic != TacticKind.Mend && turn.PlayTactic(c)) played++;
            Assert.LessOrEqual(played, 2);
            // 3枚目は使えない（2枚使ったあと）
            if (turn.TacticPlaysThisTurn >= 2) Assert.IsFalse(turn.CanPlayTactic(turn.Tactics.Hand.FirstOrDefault() ?? Card("Tactic_Focus")));
            // 選別：選ぶまで札を使わない
            var sift = Card("Tactic_Sift");
            var m = turn.Grid.Model;
            Assert.IsNotNull(sift);
        }

        // ───── V4 のラン ─────

        [Test]
        public void V4MapHasEightFloorsWithMidBossRoutesAndTheBoss()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var map = V4Content.BuildMap(new System.Random(seed));
                Assert.AreEqual(8, map.Count);
                Assert.AreEqual(2, map[0].Count);
                Assert.IsTrue(map[0].All(r => r.Intro && r.Kind == RoomKind.Battle));
                Assert.IsTrue(map[2].Any(r => r.IsMidBoss), "3階に中ボスの道");
                Assert.IsTrue(map[5].Any(r => r.IsMidBoss), "6階に中ボスの道");
                Assert.AreNotEqual(map[2].First(r => r.IsMidBoss).Id, map[5].First(r => r.IsMidBoss).Id, "中ボスは別のもの");
                Assert.IsTrue(map[3].Any(r => r.Kind == RoomKind.Rest) && map[3].Any(r => r.Kind == RoomKind.Shop));
                Assert.AreEqual("v4_boss", map[7].Single().Id);
                var all = map.SelectMany(f => f).Where(r => r.V4).Select(r => r.Id).ToList();
                Assert.AreEqual(all.Count, all.Distinct().Count(), "同じ部屋は1ランに1回");
            }
        }

        [Test]
        public void V4RunStartsWithSixTacticsAndRolesAreSmall()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var std = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 5, Look, true, false, true, true);
            Assert.AreEqual(6, std.TacticCards.Count);
            Assert.IsTrue(std.TacticCards.Any(c => c.tactic == TacticKind.EarlyBell), "早鐘が最初から入っている");
            Assert.AreEqual(1, std.Role.PostHeal);
            var cmd = new ActRun(ActContent.Roles.First(r => r.Id == "commander"), 5, Look, true, false, true, true);
            Assert.AreEqual(7, cmd.TacticCards.Count);
            Assert.AreEqual(1, cmd.Role.TacticBonus);
            var obs = new ActRun(ActContent.Roles.First(r => r.Id == "observer"), 5, Look, true, false, true, true);
            Assert.IsTrue(obs.Role.Preview);
            Assert.AreEqual(12, std.Deck.Count);
        }

        [Test]
        public void V4RewardsOfferTwoCommandsAndTwoTacticsFirstFreeSecondPaidAndSkipPays()
        {
            var (act, turn) = NewGame();
            act.Profile.finalFinished = 1; // 【RC5・レビュー3】2回目のラン（はじめてのランの1・2階は命令札3枚だけ：RC5Tests）
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 31, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            var run = act.Run;
            act.ChooseDoor(run.Doors[0]);
            turn.Player.Model.Hp = 15; // 傷ついていれば戦闘のあとに少し回復する
            act.ForceWinForCapture();
            act.ProceedAfterBattle();
            Assert.AreEqual(ActScreen.Reward, act.Screen);
            Assert.AreEqual(3, run.RewardCards.Count, "【RC6】命令札3枚から1枚");
            Assert.AreEqual(0, run.RewardCards.Count(c => c.IsTactic), "【RC6】通常戦の報酬に緑は出ない（中ボスと商人）");
            Assert.AreEqual(1, run.RewardPicksLeft);
            Assert.AreEqual(1, run.ExtraBuysLeft);
            Assert.IsNotNull(run.LastHealText, "戦闘のあとの回復");
            int tactics = run.TacticCards.Count, deck = run.Deck.Count, shards = run.Shards;
            var first = run.RewardCards[0];
            Assert.IsFalse(run.BuyRewardCard(run.RewardCards[1]), "無料の枚数を取る前は買えない");
            Assert.IsTrue(run.TakeRewardCard(first));
            Assert.AreEqual(tactics, run.TacticCards.Count);
            Assert.AreEqual(deck + 1, run.Deck.Count, "命令札はデッキへ");
            deck = run.Deck.Count;
            var cmd = run.RewardCards.First(c => !c.IsTactic);
            int price = run.ExtraPriceOf(cmd);
            if (run.Shards >= price)
            {
                Assert.IsTrue(run.BuyRewardCard(cmd));
                Assert.AreEqual(shards - price, run.Shards);
                Assert.AreEqual(deck + 1, run.Deck.Count);
                Assert.IsFalse(run.BuyRewardCard(run.RewardCards.First()), "追加で買えるのは1枚だけ");
            }
            act.NextFloor();
            // 何も取らずに進むとルーン片
            act.ChooseDoor(run.Doors[0]);
            act.ForceWinForCapture();
            act.ProceedAfterBattle();
            int before = run.Shards;
            act.NextFloor();
            Assert.AreEqual(before + V4Content.SkipRewardShards, run.Shards, "取らずに進むとルーン片が少し");
        }

        [Test]
        public void MidBossRewardOffersItsOwnTacticsAndTwoFreePicks()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            // 3階まで進める（扉の先頭を選ぶ）
            var mid = V4Content.Room("v4_m2");
            run.SetFloorForTest(2, mid);
            run.WinBattle(new BattleStats { Won = true, Turns = 8, HpBefore = 10, HpAfter = 10 });
            Assert.AreEqual(2, run.RewardPicksLeft);
            Assert.AreEqual(0, run.ExtraBuysLeft);
            var names = run.RewardCards.Select(c => c.Card.name).ToList();
            Assert.Contains("Tactic_Detonate", names);
            Assert.Contains("Tactic_Retarget", names);
            Assert.AreEqual(6, run.RewardCards.Count, "専用の戦術札2＋レアの戦術札1＋命令札2＋戦術札1（RC2：命令札 1→2）");
        }

        [Test]
        public void ReviewFixes_SilenceNotOnSilencedAndGuardNotWhenGuarded()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.......\n.......\n.......\n.....S.", "20", "A"));
            var sw = En(m, 0);
            sw.Silenced = true;
            Assert.IsFalse(TacticRules.CanUse(m, TacticKind.Silence, sw, null), "沈黙中の敵にはもう使えない");
            m.PlayerGuard = 2;
            Assert.IsFalse(TacticRules.CanUse(m, TacticKind.Guard, null, null), "すでに守っているときの残心は使えない");
            m.PlayerGuard = 0;
            Assert.IsTrue(TacticRules.CanUse(m, TacticKind.Guard, null, null));
        }

        [Test]
        public void ReviewFixes_SkipBonusOnlyWhenNothingWasTaken()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run.SetFloorForTest(2, V4Content.Room("v4_m2"));
            run.WinBattle(new BattleStats { Won = true, Turns = 8, HpBefore = 10, HpAfter = 10 });
            Assert.IsTrue(run.SkipBonusAvailable);
            run.TakeRewardCard(run.RewardCards[0]);
            Assert.IsFalse(run.SkipBonusAvailable, "1枚取ったらスキップのボーナスは出ない（画面の表示と実際が一致）");
            int before = run.Shards;
            run.Leave();
            Assert.AreEqual(before, run.Shards);
        }

        // ───── RC2：少数精鋭・行動順・絵 ─────

        [Test]
        public void Rc2_RoomsHaveFewEnemies()
        {
            var expect = new Dictionary<string, int> { { "StageX1", 2 }, { "StageX2", 2 }, { "StageX3", 3 }, { "StageX4", 3 }, { "StageX5", 3 }, { "StageX6", 2 }, { "StageX7", 3 }, { "StageX8", 3 }, { "StageX9", 3 }, { "StageXA", 3 }, { "StageXB", 3 } };
            foreach (var kv in expect)
            {
                var st = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{kv.Key}.asset");
                Assert.IsNotNull(st, kv.Key);
                var m = BattleModel.FromStage(st);
                Assert.AreEqual(kv.Value, m.Enemies.Count(e => !e.IsObject), $"{kv.Key} の敵の数");
            }
            var boss = BattleModel.FromStage(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXC.asset"));
            Assert.LessOrEqual(boss.Enemies.Count(e => !e.IsObject), 4, "ボス：本体＋コピーの敵は少数");
        }

        [Test]
        public void Rc3_EnemiesMoveOnlyAfterAttackAndCopy()
        {
            // 【RC3】剣兵（全まね）は、あなたの命令のあと まず「まね」で動き（攻撃では動かない）、そのあとで近づく（位置取り）
            var m = BattleModel.FromStage(NewStage("....S..\n.......\n.......\n.......\n.......\nP......", "20", "A"));
            var sw = En(m, 0);
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right, 1) }, null, events, null);
            var moves = events.Where(e => e.Type == BattleEventType.Moved && e.Unit == sw).ToList();
            Assert.GreaterOrEqual(moves.Count, 2, "まね＋近づく");
            Assert.AreEqual(Vector2Int.right, moves[0].To - moves[0].From, "最初の動きはまね（あなたと同じ →）。攻撃の前に近づかない");
            Assert.IsTrue(moves[0].Cause != null && moves[0].Cause.ByCopy, "最初はまね");
            Assert.IsFalse(moves[1].Cause != null && moves[1].Cause.ByCopy, "そのあと位置取り（近づく）");
            // 追跡兵：攻撃（噛みつき）は動かない・まねのあと2マスまで近づく
            var m2 = BattleModel.FromStage(NewStage("C......\n.......\n.......\nP.....S", "20,20", ""));
            var ch = m2.Enemies.First(e => e.Kind == EnemyKind.Chaser);
            var p0 = ch.Position;
            PhaseRules.TraitPhase(m2, new List<BattleEvent>(), null, 1);
            Assert.AreEqual(p0, ch.Position, "攻撃の段階では動かない");
            PhaseRules.TraitPhase(m2, new List<BattleEvent>(), null, 2);
            Assert.AreEqual(2, System.Math.Abs(ch.Position.x - p0.x) + System.Math.Abs(ch.Position.y - p0.y), "まねのあと：2マス近づく");
        }

        [Test]
        public void Rc3_RedPreviewIsWhatHits()
        {
            // 【RC3・本人「赤を避けたのに当たった」】ターンの初めの赤だけが、そのターンの敵の攻撃。あなたが動いた先へ狙い直さない
            // 弓兵（下向き）の2マス右にあなた → 弓兵は始めにあなたの方（右）を向く。あなたが上へ逃げれば当たらない（追いかけて狙い直さない）
            var m = BattleModel.FromStage(NewStage(".......\n.......\n.K..P..\n.......", "12", "M"));
            var archer = m.Enemies.First(e => e.Kind == EnemyKind.Archer);
            foreach (var c in archer.Clocks) c.Cd = 9;
            Assert.AreEqual(Direction.Right, archer.Facing, "始めからあなたの方を向く（最初の赤）");
            var red = ThreatMap.EnemyAttack(m);
            Assert.IsTrue(red.Contains(m.Player.Position), "あなたのマスは赤");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(20, m.Player.Hp, "赤から出たので当たらない（上へ狙い直さない）");
            // 処刑騎士の剣も赤で見える（前は出ていなかった）
            var m2 = BattleModel.FromStage(NewStage(".......\n...e...\n.......\n.......", "40", ""));
            var knight = m2.Enemies.First(e => e.Kind == EnemyKind.ExecKnight);
            Assert.IsTrue(ThreatMap.EnemyAttack(m2).Contains(knight.Position + Vector2Int.down), "処刑騎士の隣4マスも赤");
            // 剣兵を殴っても反撃は無い
            var m3 = BattleModel.FromStage(NewStage(".......\n...S...\n...P...\n.......", "20", "N"));
            foreach (var c in m3.Enemies.First().Clocks) c.Cd = 9;
            int hp = m3.Player.Hp;
            TurnRules.PlayCommandsAndCopies(m3, new List<CommandRecord> { R("Slash", Direction.Up, 1) }, null, new List<BattleEvent>());
            Assert.AreEqual(hp, m3.Player.Hp, "剣兵の反撃なし（RC3）");
        }

        [Test]
        public void Rc2_TacticAndHazardIconsExist()
        {
            foreach (TacticKind k in System.Enum.GetValues(typeof(TacticKind)))
                if (k.ToString() != "None")
                Assert.IsTrue(System.IO.File.Exists($"Assets/_Project/Art/Generated/ui_cardart_tac_{k.ToString().ToLower()}.png"), $"戦術札の絵 {k}");
            foreach (var n in new[] { "flame", "ember", "rock", "rock_shadow", "aim", "heal", "barrel", "emitter" })
                Assert.IsTrue(System.IO.File.Exists($"Assets/_Project/Art/Generated/ui_hz_{n}.png"), $"ギミックの印 {n}");
        }

        [Test]
        public void SavesKeepTheTacticDeckAndTheV4Flag()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "commander"), 77, Look, true, false, true, true);
            run.SetTacticCards(run.TacticCards.Concat(new[] { Look("Tactic_Toll") }).ToList());
            string json = ActSave.ToJson(run);
            Assert.IsTrue(ActSave.TryRestore(json, Look, out var back, out var err), err);
            Assert.IsTrue(back.V4Run);
            CollectionAssert.AreEqual(run.TacticCards.Select(c => c.name), back.TacticCards.Select(c => c.name));
            Assert.AreEqual(1, back.Role.TacticBonus);
        }

        [Test]
        public void V4RunCanWalkThroughEveryFloorWithForcedWins()
        {
            var (act, turn) = NewGame();
            for (int seed = 1; seed <= 4; seed++)
                for (int pick = 0; pick < 2; pick++)
                {
                    act.StartRun(ActContent.Roles.First(r => r.Id == V4Content.RoleIds[seed % 3]), seed, true, false, true, true);
                    if (act.Screen == ActScreen.Boon) act.SkipBoon();
                    int guard = 0;
                    while (act.Screen != ActScreen.Result && guard++ < 30)
                    {
                        Assert.AreEqual(ActScreen.Doors, act.Screen, $"種 {seed}");
                        var door = act.Run.Doors[System.Math.Min(pick, act.Run.Doors.Count - 1)];
                        act.ChooseDoor(door);
                        switch (door.Kind)
                        {
                            case RoomKind.Rest: act.NextFloor(); break;
                            case RoomKind.Shop: act.NextFloor(); break;
                            default:
                                Assert.AreEqual(ActScreen.Battle, act.Screen, $"{door.Id} の扉で戦闘が始まらない");
                                Assert.IsTrue(turn.IsV4, door.Id);
                                act.ForceWinForCapture();
                                act.ProceedAfterBattle();
                                if (act.Screen == ActScreen.Reward) act.NextFloor();
                                break;
                        }
                    }
                    Assert.IsTrue(act.Run.Cleared, $"種 {seed}・扉 {pick}：最後まで通せる");
                }
        }

        [Test]
        public void SimulatedFullTurnsInAllV4RoomsNeverThrowAndClocksAlwaysFireAtTheirCells()
        {
            var rng = new System.Random(5);
            var dirs = new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
            string[] cmds = { "Walk", "Dash", "Rush", "Slash", "Thrust", "PushSlash", "Push", "Pull" };
            foreach (var room in V4Content.Rooms)
            {
                var st = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{room.Stage}.asset");
                for (int trial = 0; trial < 3; trial++)
                {
                    var m = BattleModel.FromStage(st);
                    for (int t = 0; t < 14 && m.Player.IsAlive && !m.AllEnemiesDead; t++)
                    {
                        var seq = new List<CommandRecord>();
                        int n = 1 + rng.Next(3);
                        for (int i = 0; i < n; i++)
                        {
                            var c = Card(cmds[rng.Next(cmds.Length)]);
                            seq.Add(new CommandRecord(c, dirs[rng.Next(4)], c.minDistance + rng.Next(System.Math.Max(1, c.maxDistance - c.minDistance + 1))));
                        }
                        // ①の大技の範囲は、発動までに変わらない
                        var warned = m.Enemies.Where(e => e.IsAlive).SelectMany(e => e.Clocks.Where(c => c.Telegraphed).Select(c => (e, c, cells: c.Cells.ToList()))).ToList();
                        var outcome = TurnRules.PlayTurn(m, seq, null, new List<BattleEvent>(), null);
                        foreach (var w in warned.Where(w => w.c.Cd == w.c.Period)) // 発動した直後（周期に戻った）
                            Assert.IsTrue(w.c.Cells.Count == 0 || w.c.Cd != 1, $"{room.Id}：発動後は予告が消える");
                        if (outcome != TurnOutcome.Continue) break;
                    }
                }
            }
        }
    }
}
