using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【完成版 ACT1・2026-10-20】完成版で足した決まり：大技はまねの後・使わない命令枠で戦術札・鍛える・開始配置の別案・挑戦・
    /// ボスの再挑戦・周回の解放（FinalMeta）・セーブ（鍛えた札・挑戦）・報酬の札の抽選・はじめてのランの1階。
    /// </summary>
    public class FinalAct1Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            PhaseRules.ClockAfterCopy = true;
            PhaseRules.IntentOrder = true;
            BuildInfo.SetForTest(null);
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

        // ───── 大技はまねの後 ─────

        BattleModel SwordRingScene()
        {
            // 敵（全まね・HP10）は剣兵の2マス左。剣兵（技まね）の八方斬りは①で予告済み（周り8マス）。
            var m = BattleModel.FromStage(NewStage(".......\n.......\n.......\n.E.S...\n.......\nP......", "10,20", "AT"));
            var sword = m.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            foreach (var c in sword.Clocks) { c.Cd = 1; Clocks.Telegraph(m, sword, c); }
            return m;
        }

        [Test]
        public void BigAttackFiresAfterCopiesSoACopiedMoveCanSendAnEnemyIntoIt()
        {
            PhaseRules.ClockAfterCopy = true;
            var m = SwordRingScene();
            var foe = m.Enemies.First(e => e.Kind == EnemyKind.Normal);
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Right, 1) }, null, new List<BattleEvent>(), null);
            Assert.Less(foe.Hp, 10, "まねで剣兵の隣へ動いた敵に、同じターンの八方斬りが当たる（完成版の順番）");
        }

        [Test]
        public void OldOrderFiredBeforeCopiesAndMissed()
        {
            PhaseRules.ClockAfterCopy = false;
            PhaseRules.IntentOrder = false; // 【RC3】比べ用：RC2 の順番
            var m = SwordRingScene();
            var foe = m.Enemies.First(e => e.Kind == EnemyKind.Normal);
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Right, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(10, foe.Hp, "RC2 の順番：大技が先に出て、まねで入った敵には当たらなかった（比べ用）");
        }

        [Test]
        public void TraitStepsPutActionsBeforeCopiesAndClocksAfter()
        {
            var m = SwordRingScene();
            var before = PhaseRules.TraitSteps(m, 1);
            var after = PhaseRules.TraitSteps(m, 2);
            Assert.IsTrue(before.All(s => s.part == PhaseRules.PartAttack), "まねの前は攻撃だけ（動かない）");
            Assert.IsTrue(after.Any(s => s.part == PhaseRules.PartFire && s.enemy.Kind == EnemyKind.Swordsman), "まねの後に大技");
            int fire = after.FindIndex(s => s.part == PhaseRules.PartFire), pos = after.FindIndex(s => s.part == PhaseRules.PartPosition), tel = after.FindIndex(s => s.part == PhaseRules.PartTelegraph);
            Assert.IsTrue(fire < pos && pos < tel, "大技 → 位置取り → 次の大技の予告（RC3）");
        }

        // ───── 鍛える ─────

        [Test]
        public void ForgedCardHitsHarderOnlyForYouNotForCopies()
        {
            // あなたの右隣に敵A（全まね）。敵A のさらに右に敵B。
            var m = BattleModel.FromStage(NewStage("PEE....\n.......\n.......", "20,20", "AA"));
            var a = m.Enemies.ElementAt(0);
            var b = m.Enemies.ElementAt(1);
            var slash = Card("Slash");
            var forged = new CardInstance(slash) { Forge = DeckState.ForgeBonus };
            TurnRules.PlayCommandsAndCopies(m, new List<CommandRecord> { new CommandRecord(slash, Direction.Right, 1, forged) }, null, new List<BattleEvent>());
            Assert.AreEqual(20 - (slash.damage + DeckState.ForgeBonus), a.Hp, "鍛えた斬撃：あなたは +2");
            Assert.AreEqual(20 - slash.damage, b.Hp, "敵A のまねた斬撃は元の強さ（敵がまねるのは形だけ）");
        }

        [Test]
        public void RunForgesOnlyAttackCardsOnceAndBattleSetupCarriesIt()
        {
            var run = NewRun(5);
            var slash = run.Deck.First(c => c.name == "Slash");
            var walk = run.Deck.First(c => c.name == "Walk");
            Assert.IsFalse(run.ForgeCard(walk), "移動の札は鍛えられない");
            Assert.IsTrue(run.ForgeCard(slash));
            Assert.IsFalse(run.ForgeCard(slash), "同じ種類は1回");
            Assert.IsTrue(run.MakeBattleSetup().Forged.Contains("Slash"));
            var deck = new DeckState(new System.Random(1)) { ForgedNames = new HashSet<string> { "Slash" } };
            deck.StartBattle(run.Deck);
            Assert.IsTrue(deck.Hand.Concat(deck.DrawPile).Where(c => c.Data.name == "Slash").All(c => c.Forge == DeckState.ForgeBonus));
        }

        // ───── 開始配置の別案・挑戦 ─────

        [Test]
        public void AltMapsAreChosenByLayoutAndKeepTheSameEnemies()
        {
            var s = NewStage(".S.\n...\n.P.", "10", "A");
            s.altMaps = new List<string> { "S..\n...\nP..", "..S\n...\n..P" };
            Assert.AreEqual(0, s.LayoutIndex(0));
            Assert.AreEqual(1, s.LayoutIndex(1));
            Assert.AreEqual(2, s.LayoutIndex(2));
            Assert.AreEqual(0, s.LayoutIndex(3));
            var m0 = BattleModel.FromStage(s, 0);
            var m1 = BattleModel.FromStage(s, 1);
            Assert.AreNotEqual(m0.Enemies.First().Position, m1.Enemies.First().Position);
            Assert.AreEqual(m0.Enemies.First().Kind, m1.Enemies.First().Kind);
        }

        [Test]
        public void EveryFinalRoomHasLayoutVariantsExceptTheBoss()
        {
            foreach (var room in V4Content.Rooms)
            {
                var st = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{room.Stage}.asset");
                Assert.IsNotNull(st, room.Stage);
                if (room.Kind == RoomKind.Boss) continue;
                Assert.GreaterOrEqual(st.altMaps.Count, 2, $"{room.Name}：開始配置の別案が2つ以上");
                var kinds0 = BattleModel.FromStage(st, 0).Enemies.Select(e => e.Kind).ToList();
                for (int i = 1; i <= st.altMaps.Count; i++)
                    CollectionAssert.AreEqual(kinds0, BattleModel.FromStage(st, i).Enemies.Select(e => e.Kind).ToList(), $"{room.Name} の別案 {i}：敵の読み順と兵種が同じ（HP の順番がずれない）");
            }
        }

        [Test]
        public void ChallengeRaisesHpExceptTheBossAndStopsPostBattleHeal()
        {
            var m = BattleModel.FromStage(NewStage("P......\n.......\n.....S.", "20", "A"));
            var s = m.Enemies.First();
            int cd = s.Clocks.First().Cd;
            m.ApplyChallenge(1);
            Assert.AreEqual(20, s.MaxHp, "【通常リリース版】深度1 は敵の HP を増やさない（HP を増やすだけ＝このゲームらしくない）");
            Assert.AreEqual(2, m.EchoBack, "深度1以上：まね返し +2");
            Assert.AreEqual(ActContent.RestHeal, NewRun(40).RestHealAmount, "【通常リリース版】深度で休息の回復は変えない（深度は まね返し と 手札だけ）");
            Assert.AreEqual(cd, s.Clocks.First().Cd, "大技の時計は変えない（早めると慣れた人にはむしろ楽だった）");
            // 【通常リリース版】深度2 でも戦闘のあとの回復（旅の剣士の +1）は消さない
            foreach (var (level, expectHeal) in new[] { (1, true), (2, true) })
            {
                var run = NewRun(41);
                run.Challenge = level;
                run.SetHpForStudy(10);
                run.SetFloorForTest(0, run.Doors[0]);
                run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = 10, HpAfter = 10 });
                Assert.AreEqual(expectHeal, run.Hp > 10, $"深度{level}");
            }
        }

        // ───── ボスの再挑戦 ─────

        [Test]
        public void BossRetryIsOncePerRunAndNotOnDepthThree()
        {
            var run = NewRun(9);
            var boss = V4Content.Room("v4_boss");
            run.SetFloorForTest(run.FloorCount - 1, boss);
            run.LoseBattle(new BattleStats { Won = false, HpBefore = 12, HpAfter = 0 });
            Assert.IsTrue(run.CanRetryBoss);
            Assert.IsTrue(run.RetryBoss());
            Assert.IsFalse(run.Failed);
            Assert.Greater(run.Hp, 0);
            run.LoseBattle(new BattleStats { Won = false, HpBefore = 12, HpAfter = 0 });
            Assert.IsFalse(run.CanRetryBoss, "1ランに1回");
            var deep = NewRun(10);
            deep.Challenge = 3;
            deep.SetFloorForTest(deep.FloorCount - 1, boss);
            deep.LoseBattle(new BattleStats { Won = false, HpBefore = 12, HpAfter = 0 });
            Assert.IsFalse(deep.CanRetryBoss, "反響深度3 は再挑戦なし");
        }

        // ───── 周回の解放 ─────

        static ActRun NewRun(int seed, string role = "standard")
        {
            var r = new ActRun(ActContent.Roles.First(x => x.Id == role), seed, Card, true, false, true, true);
            return r;
        }

        static void ClearRun(ActProfileData p, string role, int challenge = 0)
        {
            var run = NewRun(100 + p.finalRuns, role);
            run.Challenge = challenge;
            run.SetFloorForTest(run.FloorCount - 1, V4Content.Room("v4_boss"));
            run.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 10, HpAfter = 10 });
            Assert.IsTrue(run.Cleared);
            p.finalRuns++;
            FinalMeta.RecordRun(p, run, 25);
        }

        [Test]
        public void RolesCardsAndChallengesUnlockHorizontally()
        {
            var p = new ActProfileData();
            Assert.IsTrue(FinalMeta.IsRoleUnlocked(p, "standard"));
            Assert.IsFalse(FinalMeta.IsRoleUnlocked(p, "observer"));
            Assert.IsFalse(FinalMeta.IsRoleUnlocked(p, "commander"));
            Assert.AreEqual(0, FinalMeta.UnlockedCards(p).Count, "最初は号令の札は出ない");
            ClearRun(p, "standard");
            Assert.IsTrue(FinalMeta.IsRoleUnlocked(p, "observer"), "旅の剣士でクリア → 観測士");
            Assert.IsTrue(FinalMeta.IsRoleUnlocked(p, "commander"), "【通常リリース版】1回目のクリアで 観測士と指揮官の両方");
            Assert.IsTrue(FinalMeta.UnlockedCards(p).Contains("Incite"), "剣士の初クリア → けしかけ");
            Assert.AreEqual(1, FinalMeta.MaxChallenge(p), "【RC12】はじめてのクリア → 反響深度1（役職とは別）");
            ClearRun(p, "observer");
            Assert.IsTrue(FinalMeta.IsRoleUnlocked(p, "commander"));
            ClearRun(p, "commander");
            Assert.AreEqual(1, FinalMeta.MaxChallenge(p), "3つの役職でクリアしても、深度は 1 のまま（深度は深度のクリアで上がる）");
            ClearRun(p, "standard", 1);
            Assert.AreEqual(2, FinalMeta.MaxChallenge(p), "深度1でクリア → 深度2");
            var rec = p.FinalRecord("standard");
            Assert.AreEqual(2, rec.clears);
            Assert.AreEqual(2, rec.clearedChallenge, "深度1までクリア");
            Assert.AreEqual(10, rec.bestTurns);
        }

        [Test]
        public void LockedCardsAreNotOfferedUntilUnlocked()
        {
            var locked = NewRun(3);
            for (int i = 0; i < 30; i++)
            {
                locked.SetFloorForTest(1, V4Content.Room("v4_n5"));
                locked.RollRewards(new BattleStats { Won = true });
                Assert.IsFalse(locked.RewardCards.Any(r => V4Content.LockedCards.Contains(r.Card.name)), "解放前の札は報酬に出ない");
                Assert.IsFalse(locked.RewardCards.Any(r => V4Content.FinalTacticExcluded.Contains(r.Card.name)), "外した戦術札は出ない");
            }
        }

        [Test]
        public void SaveKeepsForgedChallengeAndBossRetry()
        {
            var run = NewRun(77);
            run.ForgeCard(run.Deck.First(c => c.name == "Slash"));
            run.Challenge = 2;
            run.UnlockedCards.Add("Incite");
            var json = ActSave.ToJson(run);
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var err), err);
            Assert.IsTrue(back.Forged.Contains("Slash"));
            Assert.AreEqual(2, back.Challenge);
            Assert.IsTrue(back.UnlockedCards.Contains("Incite"));
        }

        [Test]
        public void OldProfileJsonWithoutFinalFieldsStillLoads()
        {
            string old = "{\"version\":1,\"runsStarted\":3,\"runsFinished\":2,\"clears\":1,\"unlockedRoles\":[\"observer\"],\"records\":[],\"usedCards\":[],\"hintsSeen\":[]}";
            Assert.IsTrue(ActProfile.TryParse(old, out var p));
            Assert.IsNotNull(p.finalRecords);
            Assert.IsNotNull(p.finalRoles);
            Assert.IsFalse(FinalMeta.IsRoleUnlocked(p, "observer"), "研究版の解放は完成版の解放とは別");
        }

        [Test]
        public void FirstRunAlwaysOffersTheIntroRoomOnFloorOne()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = NewRun(seed);
                run.ForceFirstDoor("v4_i1");
                Assert.IsTrue(run.Doors.Any(d => d.Id == "v4_i1"), $"種 {seed}");
                Assert.AreEqual(1, run.Doors.Count, "【RC3】はじめてのランの1階は「鏡の歩兵」の扉1つ（敵は まねるだけの戦闘）");
            }
        }

        [Test]
        public void MapUsesAllRoomPoolsAndNeverRepeatsARoom()
        {
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 60; seed++)
            {
                var map = V4Content.BuildMap(new System.Random(seed));
                var ids = map.SelectMany(f => f).Where(r => r.Kind != RoomKind.Rest && r.Kind != RoomKind.Shop).Select(r => r.Id).ToList();
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), "同じ部屋は1ランに1回");
                foreach (var id in ids) seen.Add(id);
            }
            foreach (var r in V4Content.Rooms) Assert.IsTrue(seen.Contains(r.Id), $"{r.Name} が地図に出る");
        }

        // ───── 入れ替え（報酬で札を取ったら、同じ種類の札を1枚外してよい） ─────

        static ActRun RunAtReward(int seed)
        {
            var run = NewRun(seed);
            run.SetFloorForTest(0, run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = 20, HpAfter = 20 });
            Assert.IsTrue(run.RewardCards.Count > 0, "報酬がある");
            return run;
        }

        [Test]
        public void TakingACardAllowsOneOptionalSwapOfTheSameKind()
        {
            V4Content.FreeSwap = true; // 【RC6】完成版では外した（RC2〜RC5 の決まりを確かめる）
            try { TakingACardAllowsOneOptionalSwapOfTheSameKindBody(); } finally { V4Content.FreeSwap = false; }
        }

        void TakingACardAllowsOneOptionalSwapOfTheSameKindBody()
        {
            var run = RunAtReward(31);
            int deck = run.Deck.Count, tactics = run.TacticCards.Count;
            Assert.AreEqual(0, run.SwapsLeft);
            Assert.IsFalse(run.SwapRemoveAt(0), "取る前は外せない");
            var cmd = run.RewardCards.First(r => !r.IsTactic);
            Assert.IsTrue(run.TakeRewardCard(cmd));
            Assert.AreEqual(1, run.SwapsLeft);
            Assert.AreEqual(0, run.TacticSwapsLeft, "命令札を取ったら、外せるのは命令札");
            Assert.IsTrue(run.SwapRemoveAt(run.Deck.FindIndex(c => c.name == "Walk")));
            Assert.AreEqual(deck, run.Deck.Count, "1枚取って1枚外す＝枚数は同じ");
            Assert.IsFalse(run.SwapRemoveAt(0), "1枚取ったら1回だけ");
            // 戦術札でも同じ（【RC6】通常戦の報酬に緑は出ない → 戦術札を直接取ったことにして確かめる）
            var tacCard = Card("Tactic_Guard");
            run.RewardCards.Add(new RewardCard { Card = tacCard });
            run.SetShardsForStudy(200);
            var tac = run.RewardCards.First(r => r.IsTactic);
            Assert.IsTrue(run.BuyRewardCard(tac));
            Assert.AreEqual(1, run.TacticSwapsLeft);
            Assert.IsTrue(run.TacticSwapRemoveAt(0));
            Assert.AreEqual(tactics, run.TacticCards.Count);
            run.Leave();
            Assert.AreEqual(0, run.SwapsLeft + run.TacticSwapsLeft, "部屋を出たら残らない");
        }

        [Test]
        public void SwapLeftIsKeptWhenContinuingFromTheRewardScreen()
        {
            V4Content.FreeSwap = true; // 【RC6】完成版では外した（RC2〜RC5 の決まりを確かめる）
            try { SwapLeftIsKeptBody(); } finally { V4Content.FreeSwap = false; }
        }

        void SwapLeftIsKeptBody()
        {
            var run = RunAtReward(32);
            Assert.IsTrue(run.TakeRewardCard(run.RewardCards.First(r => !r.IsTactic)));
            var json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "reward"));
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var err), err);
            Assert.AreEqual(1, back.SwapsLeft);
        }

        [Test]
        public void AimIsOutOfTheRewardPoolAndSkippingGivesNoShards()
        {
            Assert.IsFalse(V4Content.FinalCommandPool.Any(c => c.Asset == "Aim"), "狙い定めは抽選から外した");
            var run = RunAtReward(33);
            int before = run.Shards;
            Assert.IsTrue(run.SkipBonusAvailable);
            run.Leave();
            Assert.AreEqual(before, run.Shards, "取らずに進んでもルーン片は増えない（完成版）");
        }

        [Test]
        public void UnusedSlotRuleIsOnByDefault()
        {
            Assert.AreEqual(0, TurnRules.UnusedSlotTacticDraw, "【RC6】使わない枠の戦術札は無し（緑の手札は1枚）");
            Assert.IsTrue(PhaseRules.ClockAfterCopy, "大技はまねの後");
        }
    }
}
