using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC7・本人の RC6 実プレイ（6階の砲王で HP 18 → 0）のあと】中ボスの山・盤面の「このターン受けるダメージ」・ターンごとの記録・
    /// 刻印（完成版の5つ：まねの刃・鏡の盾・時計の針・罠師の刻印・同士討ちの戦利品）。
    /// </summary>
    public class RC7Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        static CommandData Asset(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static ActRun NewRun(int seed, string role = "standard") => new ActRun(ActContent.Roles.First(x => x.Id == role), seed, Asset, true, false, true, true);

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

        static CommandRecord Slash(Direction d) { var c = Asset("Slash"); return new CommandRecord(c, d, 1, new CardInstance(c)); }

        // ───── 中ボスの山（本人：6階の砲王で HP 18 → 0。大技がほとんど）─────

        [Test]
        public void CannonKingFiresLessOftenAndFloorSixRedIsTwo()
        {
            var clocks = Clocks.DefaultFor(EnemyKind.CannonKing);
            Assert.AreEqual(3, clocks.Count, "砲王の大技は3つのまま（小砲撃・一斉砲撃・巨砲）");
            Assert.AreEqual(new[] { 3, 5, 7 }, clocks.Select(c => c.Period).ToArray(), "3・5・7 ターンごと（前は 2・4・6）");
            Assert.AreEqual(5, clocks.Last().Damage, "巨砲 6 → 5");
            Assert.AreEqual(2, V4Content.RedByFloor[5], "6階の赤は 2（3 は 7〜8階だけ）");
            Assert.AreEqual(3, CombatBoss.PillarDamage, "【RC10】ボスの光柱は1ターンおき・3（RC7〜RC9 は毎ターン・2）");
            var stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXA.asset");
            Assume.That(stage != null);
            StringAssert.Contains("3ターンごと", stage.tutorialText, "部屋の説明も 3・5・7 に");
            StringAssert.DoesNotContain("2ターンごと", stage.tutorialText);
        }

        // ───── 盤面の「このターン受けるダメージ」：まね返しも同じ計算に入る ─────

        [Test]
        public void ForecastCountsCopyBackAsDamageToYou()
        {
            // 敵 あなた 敵：右へ斬ると、左の敵のまね（右へ斬る）があなたに当たる＝まね返し
            var m = BattleModel.FromStage(NewStage("EPE....\n.......\n.......", "20,20", "AA"));
            var f = TurnForecast.Compute(m, new List<CommandRecord> { Slash(Direction.Right) });
            Assert.IsFalse(f.Failed);
            int back = f.PlayerHits.Where(e => e.Cause != null && e.Cause.ByCopy).Sum(e => e.Amount);
            Assert.AreEqual(Asset("Slash").damage, back, "まね返しの分が、受けるダメージに入る");
            StringAssert.Contains("ダメージ", f.DangerText(), "「-3」ではなく「3ダメージ」");
            StringAssert.DoesNotContain(" -", f.DangerText());
            // 何もしなければ受けない（まね返しは命令しだい）
            Assert.AreEqual(0, TurnForecast.Compute(m, new List<CommandRecord>()).PlayerHits.Sum(e => e.Amount));
            Assert.AreEqual(20, m.Player.Hp, "試しても元の盤面は変わらない");
        }

        // ───── 刻印 ─────

        [Test]
        public void ImprintPoolIsFiveSmallCorePassives()
        {
            Assert.AreEqual(3, V4Content.ImprintPool.Length, "【通常リリース版】まねの刃・時計の針・罠師の3つ（鏡の盾・余白は外した）");
            foreach (var id in V4Content.ImprintPool)
            {
                var d = ActContent.Imprint(id);
                Assert.IsFalse(string.IsNullOrEmpty(d.Name));
                Assert.IsFalse(string.IsNullOrEmpty(d.Description));
                Assert.That(d.Price, Is.InRange(40, 70), "商人の値段");
            }
            CollectionAssert.DoesNotContain(V4Content.ImprintPool, ImprintId.FourthCommand, "第四命令は核（まねの数）を崩すので出さない");
            CollectionAssert.DoesNotContain(V4Content.ImprintPool, ImprintId.Observer, "完全予測は出さない（本人の判断：親切すぎる）");
        }

        [Test]
        public void MirrorGuardReducesCopyBack()
        {
            int Back(int guard)
            {
                var m = BattleModel.FromStage(NewStage("EPE....\n.......\n.......", "20,20", "AA"));
                m.MirrorGuard = guard;
                TurnRules.PlayCommandsAndCopies(m, new List<CommandRecord> { Slash(Direction.Right) }, null, new List<BattleEvent>());
                return 20 - m.Player.Hp;
            }
            int d = Asset("Slash").damage;
            Assert.AreEqual(d, Back(0), "まね返し");
            Assert.AreEqual(d - 1, Back(1), "鏡の盾：まね返し -1");
        }

        [Test]
        public void CopyEdgeClockHandAndTrapperOnlyBoostEnemyOnEnemy()
        {
            int d = Asset("Slash").damage;
            // まねの刃：あなた 敵A 敵B、右へ斬る → あなたは A を（そのまま）、A のまねは B を（+1）
            var m = BattleModel.FromStage(NewStage("PEE....\n.......\n.......", "20,20", "AA"));
            m.CopyEdgeBonus = 1;
            TurnRules.PlayCommandsAndCopies(m, new List<CommandRecord> { Slash(Direction.Right) }, null, new List<BattleEvent>());
            Assert.AreEqual(20 - d, m.Enemies.ElementAt(0).Hp, "あなたの直接攻撃には乗らない");
            Assert.AreEqual(20 - d - 1, m.Enemies.ElementAt(1).Hp, "敵のまねが敵に当たると +1");

            // 時計の針：敵の大技が敵に +2（あなたへは変わらない）／罠師：トゲ・床が敵に +2
            var m2 = BattleModel.FromStage(NewStage("PE.E...\n.......\n.......", "20,20", "AA"));
            m2.ClockHandBonus = 2; m2.TrapBonus = 2;
            var a = m2.Enemies.ElementAt(0); var b = m2.Enemies.ElementAt(1);
            RulesEngine.ApplyDamage(m2, a, null, false, b, 4, DamageKind.Clock, new List<BattleEvent>());
            Assert.AreEqual(14, b.Hp, "敵の大技が敵に：4 + 2");
            RulesEngine.ApplyDamage(m2, a, null, false, m2.Player, 4, DamageKind.Clock, new List<BattleEvent>());
            Assert.AreEqual(16, m2.Player.Hp, "あなたへの大技は変わらない");
            RulesEngine.ApplyDamage(m2, m2.Player, null, false, a, 3, DamageKind.Spike, new List<BattleEvent>());
            Assert.AreEqual(15, a.Hp, "あなたが押してトゲへ：3 + 2");
            RulesEngine.ApplyDamage(m2, m2.Player, null, false, a, 3, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(12, a.Hp, "あなたの直接攻撃は変わらない");
            var clone = m2.Clone();
            Assert.AreEqual(2, clone.ClockHandBonus, "予告の試し（写し）にも刻印が入る");
            Assert.AreEqual(2, clone.TrapBonus);
        }

        [Test]
        public void ShopSellsOneImprintAndSaveKeepsTheSameOne()
        {
            for (int seed = 21; seed <= 26; seed++)
            {
                var run = NewRun(seed);
                run.SetShardsForStudy(200);
                run.SetFloorForTest(3, ActContent.Room("shop"));
                run.ResumeRoom(ActContent.Room("shop"));
                Assert.IsNotNull(run.ShopImprint, $"種 {seed}：商人に刻印が1つ");
                CollectionAssert.Contains(V4Content.ImprintPool, run.ShopImprint.Value);
                var id = run.ShopImprint.Value;
                int price = ActContent.Imprint(id).Price;
                Assert.IsTrue(run.BuyImprint());
                Assert.AreEqual(200 - price, run.Shards);
                Assert.IsTrue(run.Has(id));
                Assert.IsFalse(run.BuyImprint(), "1回の訪問で1つ");
                var json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
                Assert.IsTrue(ActSave.TryRestore(json, Asset, out var back, out var err), err);
                Assert.AreEqual(id, back.ShopImprint, "続きからでも同じ刻印（買った後に作り直すと別の刻印が「購入済み」になっていた）");
                Assert.IsTrue(back.ShopImprintSold);
                Assert.IsTrue(back.Has(id), "持っている刻印も残る");
                var setup = back.MakeBattleSetup();
                if (id == ImprintId.CopyEdge) Assert.AreEqual(V4Content.ImprintCopyEdge, setup.CopyEdgeBonus);
                if (id == ImprintId.MirrorGuard) Assert.AreEqual(V4Content.ImprintMirrorGuard, setup.MirrorGuard);
                if (id == ImprintId.ClockHand) Assert.AreEqual(V4Content.ImprintClockHand, setup.ClockHandBonus);
                if (id == ImprintId.Trapper) Assert.AreEqual(V4Content.ImprintTrap, setup.TrapBonus);
            }
        }

        [Test]
        public void MidBossOffersTwoImprintsNormalBattleNone()
        {
            var run = NewRun(13);
            run.SetFloorForTest(4, V4Content.Room("v4_n5"));
            run.WinBattle(new BattleStats { Won = true, Turns = 6, HpBefore = 20, HpAfter = 18 });
            Assert.AreEqual(0, run.RewardImprints.Count, "通常戦は刻印なし");
            var mid = NewRun(13);
            mid.SetFloorForTest(5, V4Content.Room("v4_m1"));
            mid.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 20, HpAfter = 12 });
            Assert.AreEqual(2, mid.RewardImprints.Count, "中ボスは刻印を2つから1つ");
            Assert.AreEqual(2, mid.RewardImprints.Distinct().Count());
            var json = JsonUtility.ToJson(ActSave.CaptureRoom(mid, "reward"));
            Assert.IsTrue(ActSave.TryRestore(json, Asset, out var back, out var err), err);
            CollectionAssert.AreEquivalent(mid.RewardImprints, back.RewardImprints, "続きからでも同じ2つ");
            var pick = back.RewardImprints[1];
            Assert.IsTrue(back.TakeRewardImprint(pick));
            Assert.IsTrue(back.Has(pick));
            Assert.AreEqual(0, back.RewardImprints.Count, "1つだけ");
            Assert.AreEqual(2, back.RewardPicksLeft, "札の2枚とは別");
        }

        [Test]
        public void SpoilsPaysForEnemiesNotKilledByYou()
        {
            int Shards(bool spoils)
            {
                var run = NewRun(30);
                if (spoils) run.Imprints.Add(ImprintId.Spoils);
                run.SetFloorForTest(4, V4Content.Room("v4_n5"));
                run.WinBattle(new BattleStats { Won = true, Turns = 6, HpBefore = 20, HpAfter = 18, FriendlyKills = 2, EnvironmentKills = 1 });
                return run.LastRewardShards;
            }
            Assert.AreEqual(3 * V4Content.SpoilsShards, Shards(true) - Shards(false), "まね・大技・仕掛けで倒れた3体 × 5");
        }

        [Test]
        public void BoonAlwaysOffersAnImprintForTwoMaxHp()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = NewRun(seed);
                run.RollBoons();
                Assert.AreEqual(3, run.BoonOptions.Count, $"種 {seed}：3つ");
                var b = run.BoonOptions.FirstOrDefault(o => o.Kind == BoonKind.Imprint);
                Assert.IsNotNull(b, $"種 {seed}：完成版の旅立ちには刻印がいつも1つ（設計レビュー：刻印が来るのが遅かった）");
                CollectionAssert.Contains(V4Content.ImprintPool, b.Imprint);
                int max = run.MaxHp;
                Assert.IsTrue(run.TakeBoon(b));
                Assert.AreEqual(max - V4Content.BoonImprintHpCost, run.MaxHp, "最大 HP -2");
                Assert.IsTrue(run.Has(b.Imprint));
            }
        }

        [Test]
        public void MidBossRewardIsImprintOrFreeUpgradeNotBoth()
        {
            var a = NewRun(13);
            a.SetFloorForTest(5, V4Content.Room("v4_m1"));
            a.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 20, HpAfter = 12 });
            Assert.AreEqual(2, a.RewardImprints.Count);
            Assert.AreEqual(1, a.RewardUpgradesLeft);
            Assert.IsTrue(a.TakeRewardImprint(a.RewardImprints[0]));
            Assert.AreEqual(0, a.RewardUpgradesLeft, "刻印を取ったら無料の強化は無し");
            var b = NewRun(13);
            b.SetFloorForTest(5, V4Content.Room("v4_m1"));
            b.WinBattle(new BattleStats { Won = true, Turns = 10, HpBefore = 20, HpAfter = 12 });
            b.UseRewardUpgrade();
            Assert.AreEqual(0, b.RewardImprints.Count, "強化を使ったら刻印は無し");
            Assert.AreEqual(2, b.RewardPicksLeft, "札の2枚はそのまま");
        }

        [Test]
        public void RetryIsSharedBetweenMidBossAndBoss()
        {
            var run = NewRun(9);
            var mid = V4Content.Room("v4_m2");
            run.SetFloorForTest(5, mid);
            run.LoseBattle(new BattleStats { Won = false, HpBefore = 18, HpAfter = 0 });
            Assert.IsTrue(run.CanRetryBoss, "中ボスでもやり直せる（本人は6階の中ボスでランが終わった）");
            Assert.IsTrue(run.RetryBoss());
            Assert.IsFalse(run.Failed);
            Assert.Greater(run.Hp, 0);
            var boss = V4Content.Room("v4_boss");
            run.SetFloorForTest(run.FloorCount - 1, boss);
            run.LoseBattle(new BattleStats { Won = false, HpBefore = 12, HpAfter = 0 });
            Assert.IsFalse(run.CanRetryBoss, "やり直しは中ボスとボスで1ランに1回");
            var normal = NewRun(11);
            normal.SetFloorForTest(4, V4Content.Room("v4_n5"));
            normal.LoseBattle(new BattleStats { Won = false, HpBefore = 10, HpAfter = 0 });
            Assert.IsFalse(normal.CanRetryBoss, "通常戦はやり直せない");
        }

        [Test]
        public void MarginImprintDrawsOneWhenTwoOrFewerCommands()
        {
            CollectionAssert.DoesNotContain(V4Content.ImprintPool, ImprintId.Margin, "【通常リリース版】余白は抽選から外した（どのボットでも損・核と無関係）。効き目のコードは残す");
            CollectionAssert.DoesNotContain(V4Content.ImprintPool, ImprintId.MirrorGuard, "【通常リリース版】鏡の盾は反響深度1 を打ち消すので外した");
            CollectionAssert.DoesNotContain(V4Content.ImprintPool, ImprintId.Spoils, "同士討ちの戦利品は外した（ルーン片は余っていた）");
            var run = NewRun(3);
            run.Imprints.Add(ImprintId.Margin);
            var none = new List<BattleEvent>();
            Assert.AreEqual(1, run.RoundEndDraw(none, 2));
            Assert.AreEqual(1, run.RoundEndDraw(none, 0));
            Assert.AreEqual(0, run.RoundEndDraw(none, 3));
            Assert.AreEqual(0, NewRun(3).RoundEndDraw(none, 1), "持っていなければ無し");
        }

        // ───── ターンごとの記録（本人の実プレイを後から読む） ─────

        [Test]
        public void TurnTraceRecordsCommandsHpAndCause()
        {
            var root = new GameObject("RC7");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stage = NewStage("EPE....\n.......\n.......", "20,20", "AA");
            turn.SetupForTest(grid, new List<StageData> { stage });
            turn.LoadActBattle(stage, new ActBattleSetup { Hp = 20, MaxHp = 20, Deck = Enumerable.Repeat(Asset("Slash"), 8).ToList(), Rng = new System.Random(3) });
            var card = turn.Deck.Hand.First(c => c.Data.name == "Slash");
            Assert.IsTrue(turn.AddCommand(new CommandRecord(card.Data, Direction.Right, 1, card)));
            turn.ExecuteTurnImmediately();
            Assert.AreEqual(1, turn.TurnTrace.Count);
            var line = turn.TurnTrace[0];
            StringAssert.StartsWith("T1：斬撃→", line);
            StringAssert.Contains("HP 20→", line);
            StringAssert.Contains("まね", line, "原因（まね返し）も書く");
        }
    }
}
