using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC2・2026-10-20】盾兵はあなたの方を向く・砲兵の爆風は盾を通る・処刑騎士は処刑のあと2ターン動けない・追撃は3ターンごと。
    /// </summary>
    public class RC2Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
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

        [Test]
        public void ShieldTurnsTowardYouEachTurn()
        {
            // 盾兵（下向きで始まる）の右にあなた。1ターン（盾兵がまねしない斬撃）のあと、盾兵は右（あなたの方）を向く
            var m = BattleModel.FromStage(NewStage(".......\n...T..P\n.......", "20", "M"));
            var shield = m.Enemies.First(e => e.Kind == EnemyKind.Shield);
            Assert.AreEqual(Direction.Right, shield.Facing, "【RC3】始めからあなたの方を向く");
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Down, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(Direction.Right, shield.Facing, "まねで下へ動いても、向きはまねで変わらない／位置取りで、あなたの方（右）を向く");
        }

        [Test]
        public void ShieldBlocksFrontButNotSideOrBomberBlast()
        {
            var m = BattleModel.FromStage(NewStage(".......\n.O.T..P\n.......", "10,20", "TM"));
            var shield = m.Enemies.First(e => e.Kind == EnemyKind.Shield);
            var bomber = m.Enemies.First(e => e.Kind == EnemyKind.Bomber);
            shield.Facing = Direction.Left; // 砲兵の方が正面
            int hp = shield.Hp;
            RulesEngine.ApplyDamage(m, bomber, null, false, shield, 3, DamageKind.Trait, new List<BattleEvent>());
            Assert.AreEqual(hp - 3, shield.Hp, "砲兵の爆風は、正面からでも盾で減らない");
            shield.Facing = Direction.Right; // あなたの方が正面
            hp = shield.Hp;
            RulesEngine.ApplyDamage(m, m.Player, null, false, shield, 4, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(hp - (4 - RulesEngine.ShieldBlock), shield.Hp, "正面からの直接攻撃は -3");
            shield.Facing = Direction.Up; // あなたは横
            hp = shield.Hp;
            RulesEngine.ApplyDamage(m, m.Player, null, false, shield, 4, DamageKind.Direct, new List<BattleEvent>());
            Assert.AreEqual(hp - 4, shield.Hp, "横からは減らない");
        }

        [Test]
        public void ExecKnightRestsTwoTurnsAfterExecuteAndChasesEveryThird()
        {
            Assert.AreEqual(3, Clocks.DefaultFor(EnemyKind.ExecKnight).First(c => c.Name == "追撃").Period, "追撃は3ターンごと");
            var m = BattleModel.FromStage(NewStage("...e...\n.......\n.......\n.......\n...P...", "40", "A"));
            var knight = m.Enemies.First(e => e.Kind == EnemyKind.ExecKnight);
            foreach (var c in knight.Clocks) c.Cd = 9;
            var exec = knight.Clocks.First(c => c.Name == "処刑");
            exec.Cd = 1; Clocks.Telegraph(m, knight, exec);
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Left, 1) }, null, events, null);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.ClockFired && e.Unit == knight && e.Text == "処刑"), "処刑が出た");
            Assert.AreEqual(Clocks.ExecStunRounds, knight.StunRounds, "刃が床に刺さって、次の2ターン動けない");
            var pos = knight.Position;
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Right, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(pos, knight.Position, "動けない間は近づかず、まねもしない");
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Right, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(pos, knight.Position, "2ターン目も動けない");
            Assert.AreEqual(0, knight.StunRounds);
        }

        [Test]
        public void SpearTurnsToYouWithinTwoAndStrikes()
        {
            // 【RC3】槍兵は始めからあなたの方を向く（赤＝正面2マス）。赤の上に残れば突かれ、赤から出れば当たらない（狙い直さない）
            var m = BattleModel.FromStage(NewStage(".......\n.Y.P...\n.......", "12", "T"));
            var spear = m.Enemies.First(e => e.Kind == EnemyKind.Spearman);
            foreach (var c in spear.Clocks) c.Cd = 9;
            Assert.AreEqual(Direction.Right, spear.Facing, "始めからあなたの方");
            Assert.IsTrue(ThreatMap.EnemyAttack(m).Contains(m.Player.Position));
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Slash"), Direction.Down, 1) }, null, new List<BattleEvent>(), null);
            Assert.Less(m.Player.Hp, 20, "赤の上に残ったので突かれた");

            var m2 = BattleModel.FromStage(NewStage(".......\n.Y.P...\n.......", "12", "T"));
            foreach (var c in m2.Enemies.First().Clocks) c.Cd = 9;
            TurnRules.PlayTurn(m2, new List<CommandRecord> { new CommandRecord(Card("Walk"), Direction.Up, 1) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(20, m2.Player.Hp, "赤から出れば当たらない（槍兵は上へ狙い直さない）");
            Assert.AreEqual(Direction.Right, m2.Enemies.First().Facing, "まねのあとの位置取りで近づき、あなたの方（行・列の差が大きい方・同じなら横）を向く＝次の赤");
        }

        [Test]
        public void MendIsOncePerBattle()
        {
            var mend = Card("Tactic_Mend");
            Assert.IsNotNull(mend);
            Assert.IsTrue(mend.exhaust, "手当は1戦に1回（無害な敵を残して回復し続ける止め方を防ぐ）");
            var deck = new TacticDeck(new System.Random(1));
            deck.StartBattle(new[] { mend });
            Assert.IsTrue(deck.Hand.Contains(mend));
            Assert.IsTrue(deck.Use(mend));
            for (int i = 0; i < 5; i++) deck.StartTurn();
            Assert.IsFalse(deck.Hand.Contains(mend), "使った手当は、この戦闘ではもう引かない");
            Assert.AreEqual(0, deck.DiscardCount);
        }

        [Test]
        public void FirstRunFloorOneShowsOnlyMoveAttackAndCopy()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run.ForceFirstDoor("v4_i1");
            run.SetFloorForTest(0, V4Content.Room("v4_i1"));
            var first = run.MakeBattleSetup();
            Assert.AreEqual(0, first.TacticCards.Count, "はじめてのランの1階：戦術札は出さない");
            Assert.AreEqual(0, first.TidyMax, "手札整理も出さない");
            Assert.IsTrue(first.NoClocks && first.NoTraits, "【RC3】最初の戦闘：大技なし・敵は まねるだけ（本人「固有行動は初戦から出す必要はない」）");
            run.SetFloorForTest(1, V4Content.Room("v4_n5"));
            var second = run.MakeBattleSetup();
            // 【RC4】2階は敵の攻撃（赤）と動きだけ。大技と戦術札は3階から（本人「初心者にはゲーム全体の進み方が分からない」）
            Assert.AreEqual(0, second.TacticCards.Count, "【RC4】2階：戦術札はまだ");
            Assert.AreEqual(0, second.TidyMax, "【通常リリース版】完成版では手札整理なし");
            Assert.IsFalse(second.NoTraits, "2階から敵の攻撃・動き");
            Assert.IsTrue(second.NoClocks, "【RC4】2階：大技はまだ");
            run.SetFloorForTest(2, V4Content.Room("v4_m1"));
            var third = run.MakeBattleSetup();
            Assert.Greater(third.TacticCards.Count, 0, "3階から戦術札");
            Assert.IsFalse(third.NoTraits || third.NoClocks, "3階の中ボスは大技も");
            // 2回目以降のラン（ForceFirstDoor なし）は1階から全部
            var run2 = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run2.SetFloorForTest(0, V4Content.Room("v4_i1"));
            Assert.Greater(run2.MakeBattleSetup().TacticCards.Count, 0);
        }

        [Test]
        public void GivingUpBossRetryKeepsTheRetryUnused()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run.SetFloorForTest(7, V4Content.Room("v4_boss"));
            run.LoseBattle(new BattleStats { Won = false, Turns = 12, HpBefore = 10, HpAfter = 0 });
            Assert.IsTrue(run.CanRetryBoss);
            // 負けた時点で再挑戦を使ったことにして保存する（閉じて開き直しても、もう1回はない）
            Assert.IsTrue(run.RetryBoss());
            Assert.IsTrue(run.BossRetryUsed);
            Assert.IsFalse(run.Failed);
            // 「やめる」を選んだら、負けに戻す（再挑戦は使わなかったことに）
            run.GiveUpBossRetry(0);
            Assert.IsTrue(run.Failed);
            Assert.IsFalse(run.BossRetryUsed);
            Assert.AreEqual(0, run.Hp);
        }
    }
}
