using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【戦闘中心 v3（2026-10-06）】戦闘中心 ACT1 v3：Soft ルール（反響鎧・共鳴・写し身）・癒し手・ボス「鏡の主」・手札整理・
    /// v3 のラン（地図・役職・試練・部屋ごとの乱数）・部屋の途中のセーブ・外部テスト版の印。
    /// </summary>
    public class CombatActTests
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
        }

        StageData NewStage(string map, string hp = "", string kinds = "", bool combat = true)
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map;
            s.maxSlots = 3;
            s.playerMaxHp = 20;
            s.enemyMaxHp = 8;
            s.spikeDamage = 3;
            s.enemyHpList = hp;
            s.enemyCopyTypes = kinds;
            s.combatRoom = combat;
            s.availableCommands = new List<CommandData>();
            return s;
        }

        static TurnOutcome Play(BattleModel m, params CommandRecord[] cmds) => TurnRules.PlayTurn(m, cmds.ToList(), null, new List<BattleEvent>(), null);

        // ───── Soft ルール：反響鎧 ─────

        [Test]
        public void SoftEchoArmorTakesOneFromYouAndBreaksOnCopyHit()
        {
            // あなた（下）が上へ斬る → 反響鎧兵（あなたの真上）に 1 だけ（ほかの敵が残っている＝最後の1体ではない）
            var m = BattleModel.FromStage(NewStage(".....E.\n...R...\n...P...\n.......", "8,8", "AA"));
            Assert.IsTrue(m.SoftRules);
            var r = m.Enemies.First(e => e.Kind == EnemyKind.EchoKnight);
            var ev = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Up)), ev);
            Assert.AreEqual(7, r.Hp, "Soft：あなたの技は 1 だけ通る（はじかない）");
            Assert.IsFalse(r.ArmorBroken);
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.Damaged && e.Text != null && e.Text.Contains("反響鎧")), "一言（はじかれた理由の帯）が出る");
            // まね攻撃（敵の命令・コピー）が当たると鎧が割れる
            var foe = m.AddUnit("敵X", "X", Team.Enemy, 8, new Vector2Int(2, 2));
            var ev2 = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(foe, R("Slash", Direction.Right), true), ev2);
            Assert.AreEqual(3, r.Hp, "まね攻撃は普通のダメージ（4）");
            Assert.IsTrue(r.ArmorBroken, "鎧が割れた");
            Assert.IsTrue(ev2.Any(e => e.Type == BattleEventType.ArmorBroken));
            // 割れた後は、あなたの技も普通に通る
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Up)), new List<BattleEvent>());
            Assert.IsFalse(r.IsAlive, "割れた鎧にはあなたの斬撃 4 が通る");
        }

        [Test]
        public void HardEchoArmorStillDeflectsOutsideCombatRooms()
        {
            var m = BattleModel.FromStage(NewStage(".......\n...R.E.\n...P...\n.......", "8,8", "AA", combat: false));
            Assert.IsFalse(m.SoftRules);
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Up)), new List<BattleEvent>());
            Assert.AreEqual(8, En(m, 0).Hp, "パズル v2・今までの部屋は今のまま（はじく）");
        }

        // ───── Soft ルール：鏡核の共鳴 ─────

        [Test]
        public void ResonanceDamagesAllEnemiesStopsTraitsAndCoresRegrow()
        {
            // 左の核の右にあなた、右の核の左に全まねの兵。左へ斬る → あなたは左の核、兵（まね）は…同じ向き＝左。兵の左に核がある配置
            // c P . . E c → あなた(1,y) 左＝核(0)。兵(4,y) 左＝(3) は床 … 兵が核を叩けるように兵を右の核の右に置き、右へ… 同じ向きにするため両方「左に核」
            var m = BattleModel.FromStage(NewStage("cP..cE.\n.......\n...S...\n.......", "8,8", "AA"));
            var sword = m.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            int hpE = En(m, 0).Hp, hpS = sword.Hp;
            var ev = new List<BattleEvent>();
            var o = TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null, ev, null);
            Assert.AreEqual(TurnOutcome.Continue, o);
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.Resonance), "あなたが左の核・兵のまねが右の核を同じターンに → 共鳴");
            Assert.AreEqual(hpE - RulesEngine.ResonanceDamage, m.Enemies.First(e => e.Kind == EnemyKind.Normal).Hp);
            Assert.AreEqual(hpS - RulesEngine.ResonanceDamage, sword.Hp, "敵全員に 3");
            Assert.IsFalse(ev.Any(e => e.Type == BattleEventType.Damaged && e.Unit == m.Player), "共鳴で動けない剣兵は斬らない（隣にいても）");
            Assert.IsFalse(m.Board.Cores.Any(), "核は割れた");
            Assert.AreEqual(RulesEngine.CoreRegrowRounds - 1, m.Board.CoreRegrowIn, "ラウンドの終わりに1つ減った");
            // 待つと戻る
            for (int i = 0; i < RulesEngine.CoreRegrowRounds; i++) Play(m, R("Walk", Direction.Down));
            Assert.AreEqual(2, m.Board.Cores.Count(), "3ターンで鏡核が戻る（もう一度使える）");
        }

        [Test]
        public void YouAloneCannotResonateWhileCopiersRemain()
        {
            var m = BattleModel.FromStage(NewStage("c.....c\n.......\n.......\n...P...\n...E...", "8", "A"));
            // あなたが左右を叩いても（4命令で）、まねでない → 割れない（パズル v2 と同じ規則）
            var b = m.Board;
            b.HitCore(new Vector2Int(0, 4), false);
            Assert.IsFalse(b.HitCore(new Vector2Int(6, 4), false));
        }

        // ───── 癒し手 ─────

        [Test]
        public void MenderHealsMostHurtAllyAndCopiesOnlyTechniques()
        {
            var m = BattleModel.FromStage(NewStage(".......\n..EJE..\n.......\n...P...", "8,6,8"));
            var a = En(m, 0); var j = En(m, 1); var c = En(m, 2);
            Assert.AreEqual(EnemyKind.Mender, j.Kind);
            Assert.AreEqual(CopyKind.Technique, j.CopyKind, "癒し手は既定で技まね");
            a.Hp = 5; c.Hp = 3;
            PhaseRules.TraitAction(m, j, new List<BattleEvent>());
            Assert.AreEqual(5, c.Hp, "いちばん傷ついた（割合が低い）仲間を +2");
            Assert.AreEqual(5, a.Hp);
            // 技まね：あなたの左斬りをまねて、隣の兵（左）を斬る
            var ev = new List<BattleEvent>();
            Play(m, R("Slash", Direction.Left));
            Assert.Less(a.Hp, 5, "癒し手のまね攻撃が仲間に当たる");
        }

        // ───── ボス「鏡の主」 ─────

        BattleModel BossRoom() => BattleModel.FromStage(NewStage(".......\n.c...c.\n...F...\n.H...H.\n.......\n.......\n...P...", "40,6,6"));

        [Test]
        public void MirrorLordStopsAtPhaseGatesAndWakesClones()
        {
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            Assert.IsNotNull(lord);
            Assert.AreEqual(lord, En(m, 0), "主が敵A");
            var clones = m.Enemies.Where(e => e.Kind == EnemyKind.HealClone).ToList();
            Assert.IsTrue(clones.All(c => c.Dormant), "写し身は段階1は像");
            // 大きなダメージでも段階の境目（【RC9】80%）で止まり、段階2へ
            int gate = CombatBoss.Floor(lord, 0);
            int dealt = RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 30, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(lord.MaxHp - gate, dealt);
            Assert.AreEqual(gate, lord.Hp);
            Assert.AreEqual(1, lord.BossStage);
            Assert.IsTrue(clones.All(c => c.WakingUp), "ラウンドの終わりに目覚める");
            PhaseRules.EnvironmentPhase(m, new List<BattleEvent>(), null);
            Assert.IsTrue(clones.All(c => !c.Dormant));
            // 写し身は Soft で殴れる
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, clones[0], 4, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(2, clones[0].Hp, "目覚めた写し身は殴れる（遅い道）");
        }

        [Test]
        public void BossPhaseObjectsMatchEachPhase()
        {
            // 【RC9】段階ごとに盤面の物が変わる：段階1＝写し身は眠る像・核は眠る／段階2＝写し身が目覚める・核は眠る／段階3＝写し身は主に還り、核2つが目覚める
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            var clones = m.Enemies.Where(e => e.Kind == EnemyKind.HealClone).ToList();
            Assert.AreEqual(0, lord.BossStage);
            Assert.AreEqual(2, clones.Count);
            Assert.IsTrue(clones.All(c => c.Dormant), "段階1：写し身2体は眠る像");
            Assert.IsTrue(CombatBoss.CoresAsleep(m), "段階1：核は眠る");
            Assert.AreEqual(2, m.Board.CoreHome.Count(), "核は2つ");
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 30, DamageKind.Direct, new List<BattleEvent>());
            PhaseRules.EnvironmentPhase(m, new List<BattleEvent>(), null);
            Assert.AreEqual(1, lord.BossStage);
            Assert.IsTrue(clones.All(c => c.IsAlive && !c.Dormant), "段階2：写し身が目覚める");
            Assert.IsTrue(CombatBoss.CoresAsleep(m), "段階2：核はまだ眠る");
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 30, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(2, lord.BossStage);
            Assert.IsTrue(clones.All(c => !c.IsAlive), "段階3：写し身は主に還る");
            Assert.IsFalse(CombatBoss.CoresAsleep(m), "段階3：核2つが目覚める（同じターンに叩く）");
        }

        [Test]
        public void CloneCopyStrikeHitsLordForSixAndShatters()
        {
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 12, DamageKind.Direct, new List<BattleEvent>());
            PhaseRules.EnvironmentPhase(m, new List<BattleEvent>(), null);
            var clone = m.Enemies.First(e => e.Kind == EnemyKind.HealClone);
            var ev = new List<BattleEvent>();
            int dealt = RulesEngine.ApplyDamage(m, clone, Card("Slash"), true, lord, 4, DamageKind.Direct, ev);
            Assert.AreEqual(CombatBoss.CloneStrikeDamage, dealt, "写し身のまね攻撃は -6");
            Assert.IsFalse(clone.IsAlive, "写し身は砕ける");
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.BossCloneStrike));
        }

        [Test]
        public void PhaseThreeChargeIsCanceledByResonance()
        {
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 12, DamageKind.Direct, new List<BattleEvent>());
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 30, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(2, lord.BossStage, "段階の境目（【RC9】40%）で段階3");
            int gate3 = CombatBoss.Floor(lord, 1);
            Assert.AreEqual(gate3, lord.Hp);
            Assert.IsTrue(m.Enemies.Where(e => e.Kind == EnemyKind.HealClone).All(c => !c.IsAlive), "写し身は主に還る");
            Assert.AreEqual(CombatBoss.ChargePeriod, lord.SlamCd);
            // 核は段階3で目覚める：共鳴
            var ev = new List<BattleEvent>();
            m.Board.HitCore(m.Board.CoreHome[0], false);
            Assert.IsTrue(m.Board.HitCore(m.Board.CoreHome[1], true));
            RulesEngine.ShatterCores(m, m.Board.CoreHome[0], ev);
            Assert.AreEqual(gate3 - CombatBoss.CoreBossDamage, lord.Hp, "共鳴で主に -6");
            Assert.AreEqual(CombatBoss.CoreBossStun, lord.StunRounds, "主はそのターン動けない");
            Assert.AreEqual(CombatBoss.ChargePeriod - 1, lord.SlamCd, "奔流は2ターン後にまた来る（最初からには戻さない）");
            Assert.IsTrue(CopyCore.Stunned(lord));
            Assert.IsFalse(PhaseRules.HasTrait(lord), "動けない間は奔流も光柱も無し");
        }

        [Test]
        public void CoresSleepBeforePhaseThree()
        {
            var m = BossRoom();
            var ev = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Walk", Direction.Up)), ev);
            Assert.IsTrue(CombatBoss.CoresAsleep(m));
        }

        // ───── 手札整理 ─────

        [Test]
        public void HandTidyDiscardsUpToTwoUnplannedCardsOncePerBattle()
        {
            var stage = NewStage(".......\n...E...\n.......\n...P...");
            var root = new GameObject("Tidy");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData> { stage });
            var deck = Enumerable.Repeat(Card("Walk"), 7).Concat(Enumerable.Repeat(Card("Slash"), 7)).ToList();
            turn.LoadActBattle(stage, new ActBattleSetup { Hp = 20, MaxHp = 20, Deck = deck, Rng = new System.Random(5), Seed = 5, TidyMax = 2 });
            Assert.IsTrue(turn.CanTidy);
            int before = turn.Deck.Hand.Count;
            var three = turn.Deck.Hand.Take(3).ToList();
            Assert.AreEqual(2, turn.Tidy(three), "2枚まで");
            Assert.AreEqual(before, turn.Deck.Hand.Count, "同じ枚数引く");
            Assert.IsFalse(turn.CanTidy, "1戦1回");
            Assert.AreEqual(0, turn.Tidy(turn.Deck.Hand.Take(1)));
        }

        // ───── v3 のラン ─────

        static ActRun NewRun(string role = "standard", int seed = 11) => new ActRun(ActContent.Roles.First(r => r.Id == role), seed, Card, false, false, true);

        [Test]
        public void CombatRunHasEightFloorsWithCategoriesAndThreeRoles()
        {
            var run = NewRun();
            Assert.IsTrue(run.CombatRun);
            Assert.IsFalse(run.PuzzleRun);
            Assert.AreEqual(8, run.FloorCount);
            Assert.AreEqual(RoomKind.Boss, run.Map[7][0].Kind);
            Assert.IsTrue(run.Map[0].All(r => r.Kind == RoomKind.Battle), "1階は通常戦");
            Assert.IsTrue(run.Map[4].All(r => r.Kind == RoomKind.Tactical || r.Kind == RoomKind.Battle), "5階は戦術戦");
            Assert.IsTrue(run.Map[5].Any(r => r.Kind == RoomKind.Danger), "6階に危険戦");
            Assert.IsTrue(run.Map.SelectMany(f => f).Where(r => r.CombatRun).GroupBy(r => r.Id).All(g => g.Count() == 1), "同じ部屋は1ランに1回");
            Assert.AreEqual(8, run.MinDeck);
            Assert.AreEqual(3, ActContent.CombatRoleIds.Length);
            var vanguard = NewRun("vanguard");
            Assert.IsTrue(vanguard.Has(ImprintId.Vanguard), "先兵は元の能力（直接攻撃 +1）");
            var observer = NewRun("observer");
            Assert.IsTrue(observer.FullPrediction);
            Assert.IsTrue(observer.Deck.Any(c => c.name == "Footwork"), "観測士の初期札は足取り");
            Assert.AreEqual(ActContent.TidyMax, run.MakeBattleSetup().TidyMax, "v3 の戦闘は手札整理あり");
        }

        [Test]
        public void CombatRunCanWalkThroughEveryFloor()
        {
            // でたらめ通しで落ちた（2階の扉でコピー中心 ACT1 の入れ替えが動いた）：扉を全部の階で選べる
            for (int seed = 1; seed <= 20; seed++)
                for (int pick = 0; pick < 2; pick++)
                {
                    var run = NewRun(seed: seed);
                    while (run.Floor < run.FloorCount)
                    {
                        var doors = run.Doors;
                        Assert.IsTrue(run.Enter(doors[System.Math.Min(pick, doors.Count - 1)]), $"種 {seed}・{run.Floor + 1}階");
                        run.Leave();
                    }
                }
        }

        /// <summary>扉を押すと本当に部屋に入る（でたらめ通しで、戦術戦・試練の扉が何もしなかった）。勝ちを強制して最後まで通す。試練はあきらめる道も。</summary>
        [Test]
        public void CombatRunDoorsLeadIntoRoomsUntilTheEnd()
        {
            var root = new GameObject("CombatActRun");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stages = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            turn.SetupForTest(grid, stages);
            var act = root.AddComponent<ActRunManager>();
            act.SetupForTest(turn, cards, stages);
            var kinds = new HashSet<RoomKind>();
            for (int seed = 1; seed <= 4; seed++)
                for (int pick = 0; pick < 2; pick++)
                {
                    act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), seed, true, false, true);
                    if (act.Screen == ActScreen.Boon) act.SkipBoon();
                    int guard = 0;
                    while (act.Screen != ActScreen.Result && guard++ < 30)
                    {
                        Assert.AreEqual(ActScreen.Doors, act.Screen, $"種 {seed}");
                        var door = act.Run.Doors[System.Math.Min(pick, act.Run.Doors.Count - 1)];
                        kinds.Add(door.Kind);
                        act.ChooseDoor(door);
                        switch (door.Kind)
                        {
                            case RoomKind.Rest: Assert.AreEqual(ActScreen.Rest, act.Screen); act.NextFloor(); break;
                            case RoomKind.Shop: Assert.AreEqual(ActScreen.Shop, act.Screen); act.NextFloor(); break;
                            case RoomKind.Event: Assert.AreEqual(ActScreen.Event, act.Screen); act.NextFloor(); break;
                            default:
                                Assert.AreEqual(ActScreen.Battle, act.Screen, $"{door.Id}（{door.Kind}）の扉で戦闘が始まらない");
                                if (door.Kind == RoomKind.Trial && pick == 1)
                                {
                                    act.GiveUpTrial();
                                    Assert.AreEqual(ActScreen.Reward, act.Screen, "試練をあきらめると報酬の画面（空）へ");
                                    Assert.IsFalse(act.Run.Failed, "負けではない");
                                }
                                else
                                {
                                    act.ForceWinForCapture();
                                    act.ProceedAfterBattle();
                                }
                                if (act.Screen == ActScreen.Reward) act.NextFloor();
                                break;
                        }
                    }
                    Assert.IsTrue(act.Run.Cleared, $"種 {seed}・扉 {pick}：最後まで通せる");
                }
            Assert.IsTrue(kinds.Contains(RoomKind.Tactical) && kinds.Contains(RoomKind.Boss), "戦術戦・ボスを通った");
        }

        /// <summary>【レビュー】勝利の画面で閉じても、勝ちと報酬が先に記録される（Update で。前はボスだけ）。</summary>
        [Test]
        public void CombatRunRecordsWinBeforeTheRewardButton()
        {
            var root = new GameObject("CombatActWin");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stages = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            turn.SetupForTest(grid, stages);
            var act = root.AddComponent<ActRunManager>();
            act.SetupForTest(turn, cards, stages);
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 3, true, false, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            Assert.AreEqual(ActScreen.Battle, act.Screen);
            int shards = act.Run.Shards;
            act.ForceWinForCapture();
            typeof(ActRunManager).GetMethod("Update", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(act, null);
            Assert.AreEqual(ActScreen.Battle, act.Screen, "まだ勝利の画面");
            Assert.Greater(act.Run.Shards, shards, "ルーン片はもう入っている（閉じても消えない）");
            Assert.Greater(act.Run.RewardCards.Count, 0, "報酬はもう抽選済み");
        }

        /// <summary>【レビュー】戦闘の途中のセーブはターンも持つ（続きから＝ボーナス無し・試練は残りのターン）。</summary>
        [Test]
        public void RoomSaveKeepsTheBattleTurn()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.CombatRole(ActContent.Roles.First(r => r.Id == "standard")), 5, Look, true, false, true);
            var room = run.Doors.First(d => d.Kind == RoomKind.Battle);
            Assert.IsTrue(run.Enter(room));
            string json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room", 15, 3));
            Assert.IsTrue(ActSave.TryRestore(json, Look, out var back, out var err), err);
            Assert.AreEqual(3, back.ResumedBattleTurn);
            Assert.AreEqual(15, back.Hp);
        }

        /// <summary>【レビュー】再利用は、戻せる札が捨て札に無いと命令に入れられない。</summary>
        [Test]
        public void RecycleNeedsSomethingInTheDiscardPile()
        {
            var recycle = Card("Recycle");
            Assume.That(recycle != null && recycle.fromDiscard, "再利用の札がある");
            var root = new GameObject("RecycleTest");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageK1.asset");
            turn.SetupForTest(grid, new List<StageData> { stage });
            var setup = new ActBattleSetup { Hp = 20, MaxHp = 20, BaseHand = 7, Rng = new System.Random(1), Deck = new List<CommandData> { recycle, Card("Walk"), Card("Walk"), Card("Slash"), Card("Slash"), Card("Dash"), Card("Dash") } };
            turn.LoadActBattle(stage, setup);
            var rc = turn.Deck.Hand.First(c => c.Data == recycle);
            Assert.IsFalse(turn.CanAddCard(rc), "1ターン目（捨て札が空）は入れられない");
        }

        [Test]
        public void EveryCombatRoomHasAStageAsset()
        {
            foreach (var r in ActContent.CombatRooms.Where(r => r.Stage != null))
            {
                var s = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{r.Stage}.asset");
                Assert.IsNotNull(s, r.Stage);
                Assert.IsTrue(s.combatRoom, r.Stage + " は Soft ルール");
                if (r.Kind == RoomKind.Trial) Assert.Greater(s.turnLimit, 0, r.Stage + " の試練にはターン数");
            }
        }

        [Test]
        public void RoomRngMakesRewardsTheSameAfterResume()
        {
            var a = NewRun(seed: 21);
            var b = NewRun(seed: 21);
            var room = a.Doors[0];
            a.Enter(room);
            b.Enter(b.Doors[0]);
            Assert.AreEqual(a.MakeBattleSetup().Seed, b.MakeBattleSetup().Seed, "同じ部屋の山札の順番は同じ");
            var stats = new BattleStats { Won = true, Turns = 3, HpBefore = 20, HpAfter = 18 };
            a.WinBattle(stats);
            b.WinBattle(stats);
            CollectionAssert.AreEqual(a.RewardCards.Select(r => r.Card.name).ToList(), b.RewardCards.Select(r => r.Card.name).ToList(), "報酬も同じ（中断しても引き直せない）");
        }

        [Test]
        public void TrialFailureIsNotALossAndGivesNoRewards()
        {
            var run = NewRun(seed: 5);
            // 3階に試練が無い地図もあるので、試練の部屋を直接置く
            var trial = ActContent.CombatRooms.First(r => r.Kind == RoomKind.Trial);
            run.ResumeRoom(trial);
            run.FailTrial(new BattleStats { Won = false, Turns = 3, HpBefore = 20, HpAfter = 20 });
            Assert.IsFalse(run.Failed, "負けではない");
            Assert.AreEqual(20, run.Hp);
            Assert.AreEqual(0, run.RewardCards.Count);
            Assert.AreEqual(1, run.TrialsFailed);
            Assert.IsTrue(run.LastTrialFailed);
            var win = NewRun(seed: 6);
            win.ResumeRoom(trial);
            win.WinBattle(new BattleStats { Won = true, Turns = 2, HpBefore = 20, HpAfter = 20 });
            Assert.AreEqual(3, win.RewardCards.Count, "成功：3枚から1枚");
            Assert.IsTrue(win.RewardCards.All(r => ActContent.Info(r.Card.name).Rarity != Rarity.Common), "珍しい札だけ");
            Assert.AreEqual(ActContent.TrialShards, win.Shards);
        }

        // ───── 部屋の途中のセーブ ─────

        [Test]
        public void SaveInsideRoomResumesSameRoomWithLowerHp()
        {
            var run = NewRun(seed: 33);
            var room = run.Doors[1];
            run.Enter(room);
            int seed1 = run.MakeBattleSetup().Seed;
            string json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room", 9));
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var error), error);
            Assert.IsTrue(back.CombatRun);
            Assert.AreEqual(room.Id, back.Current.Id, "同じ部屋から");
            Assert.AreEqual(9, back.Hp, "戦闘の途中で閉じた：HP は低い方");
            Assert.AreEqual("room", back.ResumedPhase);
            Assert.AreEqual(seed1, back.MakeBattleSetup().Seed, "山札の順番も同じ（引き直せない）");
        }

        [Test]
        public void SaveOnRewardScreenKeepsTheSameCards()
        {
            var run = NewRun(seed: 34);
            run.Enter(run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 3, HpBefore = 20, HpAfter = 17 });
            var names = run.RewardCards.Select(r => r.Card.name).ToList();
            string json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "reward"));
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var error), error);
            CollectionAssert.AreEqual(names, back.RewardCards.Select(r => r.Card.name).ToList());
            Assert.AreEqual(run.RewardPicksLeft, back.RewardPicksLeft);
            Assert.AreEqual(17, back.Hp);
            Assert.AreEqual("reward", back.ResumedPhase);
        }

        [Test]
        public void ExternalPlaytestFlagIsOffInTestsAndCanBeForced()
        {
            BuildInfo.SetForTest(null);
            Assert.IsFalse(BuildInfo.ExternalPlaytest, "Editor・テストでは外部テスト版ではない");
            BuildInfo.SetForTest(true);
            Assert.IsTrue(BuildInfo.ExternalPlaytest);
        }

        // ───── 刻写 v2（戦闘中のコピー利用） ─────

        [Test]
        public void CarryKillAndArmorBreakFeedEngravings()
        {
            var m = BattleModel.FromStage(NewStage(".......\n.R.E...\n...^...\n...P...", "8,3", "AA"));
            var echo = En(m, 0); var e = En(m, 1);
            var ev = new List<BattleEvent>();
            // 兵（全まね）をまねの移動でトゲへ → 搬送ではなく「写しの移動でトゲ」
            e.Hp = 3;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Down) }, null, ev, null);
            var stats = BattleStats.From(ev, true, 1, 20, 20);
            Assert.IsTrue(stats.CopyKills.Any(k => k.how == "写しの移動でトゲ"));
            Assert.Greater(stats.CopyUses, 0, "意味のあるコピー利用に数える");
            // 鎧割り
            var ev2 = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, e, Card("Slash"), true, echo, 4, DamageKind.Direct, ev2);
            var s2 = BattleStats.From(ev2, true, 1, 20, 20);
            Assert.IsTrue(s2.CopyPuzzles.Contains("鎧割り"));
        }
    }
}
