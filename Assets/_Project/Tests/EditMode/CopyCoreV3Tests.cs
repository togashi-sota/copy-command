using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中・Copy Core V3（2026-10-05）】コピーは同じ向きのまま。敵ごとに「何を写すか」（全写し・技写し・歩写し）、
    /// 対の鏡核（同じラウンドに全部叩く）、写し身の主（段階ごとに一文）と癒しの写し身。
    /// </summary>
    public class CopyCoreV3Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 1) => new CommandRecord(Card(card), d, dist);
        static BattleModel Board(params string[] rows) => BattleModel.FromRows(rows, 3, 20, 8);
        static UnitModel En(BattleModel m, int i) => m.Enemies.ElementAt(i);

        [Test]
        public void MoveCardsAreTheBlueCards()
        {
            foreach (CommandType t in System.Enum.GetValues(typeof(CommandType)))
            {
                var d = ScriptableObject.CreateInstance<CommandData>();
                d.type = t;
                Assert.AreEqual(UiStyles.CategoryOf(d) == UiStyles.CardCategory.Move, CopyKinds.IsMoveType(t), t + "：歩写しが写す札＝青い札");
                Object.DestroyImmediate(d);
            }
        }

        [Test]
        public void TechniqueCopierAttacksInPlaceAndMoveCopierOnlyWalks()
        {
            // 上の段：A（全写し）B（技写し）…C（歩写し）。あなたは下の段
            var m = Board("EE..E", ".....", "...P.");
            var a = En(m, 0); var b = En(m, 1); var c = En(m, 2);
            b.CopyKind = CopyKind.Technique;
            c.CopyKind = CopyKind.Move;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Left), R("Slash", Direction.Left) }, null);
            Assert.AreEqual(new Vector2Int(0, 2), a.Position, "全写し：左は壁で動けない（空振り）");
            Assert.AreEqual(new Vector2Int(1, 2), b.Position, "技写し：歩かない");
            Assert.AreEqual(new Vector2Int(3, 2), c.Position, "歩写し：歩く");
            Assert.AreEqual(4, a.Hp, "技写しがその場で左を斬って A に 4（同士討ち）");
            Assert.AreEqual(8, c.Hp);
        }

        [Test]
        public void FilterKeepsOrderAndDormantCopiesNothing()
        {
            var m = Board("E....", "...P.");
            var e = En(m, 0);
            var list = new List<CommandRecord> { R("Walk", Direction.Up), R("Slash", Direction.Left), R("Dash", Direction.Down, 2), R("Push", Direction.Right) };
            e.CopyKind = CopyKind.Technique;
            CollectionAssert.AreEqual(new[] { "Slash", "Push" }, CopyKinds.Filter(e, list).Select(r => r.Command.name).ToArray());
            e.CopyKind = CopyKind.Move;
            CollectionAssert.AreEqual(new[] { "Walk", "Dash" }, CopyKinds.Filter(e, list).Select(r => r.Command.name).ToArray());
            e.CopyKind = CopyKind.All;
            Assert.AreEqual(4, CopyKinds.Filter(e, list).Count);
            e.Dormant = true;
            Assert.AreEqual(0, CopyKinds.Filter(e, list).Count, "眠っている写し身は何も写さない");
        }

        [Test]
        public void StageCopyTypesAreReadInOrder()
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            s.enemyCopyTypes = "T M A";
            var m = Board("EEE..", "...P.");
            m.ApplyStage(s);
            Assert.AreEqual(CopyKind.Technique, En(m, 0).CopyKind);
            Assert.AreEqual(CopyKind.Move, En(m, 1).CopyKind);
            Assert.AreEqual(CopyKind.All, En(m, 2).CopyKind);
            Object.DestroyImmediate(s);
        }

        [Test]
        public void WallShiftMovesOnlyTheEnemy()
        {
            // あなたは右の壁で止まり、敵だけ右へ進む（正式な技術）
            var m = Board(".E...", "....P");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null);
            Assert.AreEqual(new Vector2Int(4, 0), m.Player.Position);
            Assert.AreEqual(new Vector2Int(2, 1), En(m, 0).Position);
        }

        [Test]
        public void PairedCoresBreakOnlyWhenAllAreHitInTheSameRound()
        {
            // 行1：核 P . 核 E ・ 行0：扉。あなたが←を斬ると左の核、敵のコピー←が右の核 → 同じラウンドなので割れて扉が開く
            var m = Board("cP.cE..", "...g...");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null);
            Assert.AreEqual(0, m.Board.Cores.Count(), "あなた＋コピーで同時に叩いた");
            Assert.IsTrue(m.Board.GatesOpen);

            // COPY OFF：あなた1人では片方だけ。次のラウンドには当たった印が消える
            var off = Board("cP.cE..", "...g...");
            off.CopyEnabled = false;
            TurnRules.PlayTurn(off, new List<CommandRecord> { R("Slash", Direction.Left) }, null);
            Assert.AreEqual(2, off.Board.Cores.Count());
            Assert.AreEqual(1, off.Board.CoreHitCount);
            off.BeginRound();
            Assert.AreEqual(0, off.Board.CoreHitCount, "ラウンドが変わると当たった印は消える");
            Assert.IsFalse(off.Board.GatesOpen);
        }

        [Test]
        public void BarrelExplodesForAnyAttackAndChestOpensOnlyForCopies()
        {
            // 行2：箱 敵A 樽 敵B ・ 行0：あなた。斬撃← を空振り → 敵A が箱を、敵B が樽を叩く
            var m = Board(".$EbE..", ".......", "...P...");
            var a = En(m, 0); var b = En(m, 1);
            var ev = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null, ev);
            Assert.IsFalse(m.Board.Chests.Any(), "敵のまねた攻撃で写し箱が開く");
            Assert.IsFalse(m.Board.Barrels.Any(), "樽は爆発して消える");
            Assert.AreEqual(8 - RulesEngine.BarrelDamage, a.Hp);
            Assert.AreEqual(8 - RulesEngine.BarrelDamage, b.Hp);
            Assert.AreEqual(1, BattleStats.From(ev, true, 1, 20, 20).ChestsOpened);
            // あなた自身の攻撃では写し箱は開かない
            var m2 = Board("..$P...", ".......");
            TurnRules.PlayTurn(m2, new List<CommandRecord> { R("Slash", Direction.Left) }, null);
            Assert.IsTrue(m2.Board.Chests.Any());
        }

        [Test]
        public void LordPhasesOneSentenceEach()
        {
            var m = Board("c..L..c", "H.....H", ".......", "o..P..o");
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.Lord);
            var clones = m.Enemies.Where(e => e.Kind == EnemyKind.HealClone).ToList();
            lord.OverrideMaxHp(36);
            Assert.AreEqual(1, lord.Barrier);
            Assert.AreEqual(3, lord.BarrierRule, "段階1：鏡核の守り");
            Assert.IsTrue(clones.All(c => c.Dormant), "写し身は像のまま");
            Assert.AreEqual(CopyKind.Technique, clones[0].CopyKind);
            var ev = new List<BattleEvent>();
            Assert.AreEqual(0, RulesEngine.ApplyDamage(m, m.Player, null, false, lord, 4, DamageKind.Direct, ev), "守りがある間は傷つかない");
            Assert.AreEqual(0, RulesEngine.ApplyDamage(m, m.Player, null, false, clones[0], 4, DamageKind.Direct, ev), "像は傷つかない");

            // 鏡核を全部割る → 守りが割れる
            foreach (var c in m.Board.CoreHome.ToList()) if (m.Board.HitCore(c)) RulesEngine.ShatterCores(m, c, ev);
            Assert.AreEqual(0, lord.Barrier);
            // 段階の境目（70%＝25）でダメージが止まり、段階2へ。写し身が目覚める（傷つかない）
            Assert.AreEqual(11, RulesEngine.ApplyDamage(m, m.Player, null, false, lord, 20, DamageKind.Direct, ev));
            Assert.AreEqual(25, lord.Hp);
            Assert.AreEqual(1, PhaseRules.LordPhase(lord));
            Assert.IsTrue(clones.All(c => c.Dormant && c.WakingUp), "このラウンドはまだ像（予告と食い違わないように）");
            Assert.IsTrue(CopyCore.Stunned(lord), "段階が変わったラウンドの残りはまねない");
            Assert.AreEqual(2, PhaseRules.ClonesLeft(m));
            PhaseRules.EnvironmentPhase(m, ev, null); // ラウンドの終わり：写し身が目覚める
            Assert.IsTrue(clones.All(c => !c.Dormant));
            Assert.IsFalse(CopyCore.Stunned(lord));
            Assert.AreEqual(0, RulesEngine.ApplyDamage(m, m.Player, null, false, clones[0], 4, DamageKind.Direct, ev), "写し身は傷つかない");
            // 写し身が残っている間は 40%（14）より下へ減らない（15 で止まる）
            RulesEngine.ApplyDamage(m, m.Player, null, false, lord, 20, DamageKind.Direct, ev);
            Assert.AreEqual(15, lord.Hp, "写し身が残る間は次の段階へ進まない");
            Assert.AreEqual(1, PhaseRules.LordPhase(lord));
            // 写し身は毎ラウンド主を回復
            PhaseRules.TraitAction(m, clones[0], ev);
            Assert.AreEqual(15 + PhaseRules.CloneHeal, lord.Hp);
            // 写し身が写した攻撃で主を斬る → その写し身は砕ける
            RulesEngine.ApplyDamage(m, clones[0], Card("Slash"), true, lord, 4, DamageKind.Direct, ev);
            Assert.IsFalse(clones[0].IsAlive, "斬った写し身は砕ける");
            Assert.AreEqual(15, lord.Hp, "まだ1体残っているので 15 で止まる");
            RulesEngine.ApplyDamage(m, clones[1], Card("Slash"), true, lord, 4, DamageKind.Direct, ev);
            Assert.AreEqual(0, PhaseRules.ClonesLeft(m));
            // 写し身が全部砕けたら、主は一気に 40%（14）まで削れて段階3：封印の円の守り
            Assert.AreEqual(14, lord.Hp);
            Assert.AreEqual(2, PhaseRules.LordPhase(lord));
            Assert.AreEqual(1, lord.Barrier);
            Assert.AreEqual(2, lord.BarrierRule);
            // コピーの移動で円に入ると割れる
            RulesEngine.EnterCell(m, lord, m.Board.Seals[0], lord, Card("Walk"), true, ev);
            Assert.AreEqual(0, lord.Barrier);
        }

        [Test]
        public void ReviewFixesNoSoftlocks()
        {
            // 扉の外に敵がいなくなったら扉は開く（足まね兵を倒しても詰まない）
            var g = Board("##.K.##", "##ggg##", ".......", "...E..q", ".......", "p..P...");
            En(g, 1).Hp = 0;
            RulesEngine.CheckPlates(g, new List<BattleEvent>());
            Assert.IsTrue(g.Board.GatesOpen, "扉の外の敵がいない");
            // 反響鎧兵は最後の1体になると、あなたの技が効く
            var r = Board(".R.E...", ".......", "...P...");
            var knight = En(r, 0);
            Assert.AreEqual(0, RulesEngine.ApplyDamage(r, r.Player, null, false, knight, 4, DamageKind.Direct, new List<BattleEvent>()));
            En(r, 1).Hp = 0;
            Assert.AreEqual(4, RulesEngine.ApplyDamage(r, r.Player, null, false, knight, 4, DamageKind.Direct, new List<BattleEvent>()), "最後の1体");
            // 写し身の主の回復は今の段階の上限まで（まねた応急処置でも段階が戻らない）
            var m = Board("...L...", "H.....H", "...P...");
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.Lord);
            lord.OverrideMaxHp(30);
            lord.Hp = 21; // 段階2の入口
            lord.Heal(5);
            Assert.AreEqual(21, lord.Hp, "70% を越えない");
            // 核の場所に立っていても、守りが戻るときに押し出されて核が戻る
            var b = Board(".c.L...", ".......", ".c.P...");
            var lb = b.Enemies.First();
            lb.OverrideMaxHp(30);
            var ev = new List<BattleEvent>();
            foreach (var c in b.Board.CoreHome.ToList()) if (b.Board.HitCore(c)) RulesEngine.ShatterCores(b, c, ev);
            Assert.AreEqual(0, lb.Barrier);
            b.Player.Position = new Vector2Int(1, 0); // 下の核の場所に立つ
            for (int i = 0; i < PhaseRules.LordOpenRounds; i++) PhaseRules.EnvironmentPhase(b, ev, null);
            Assert.AreEqual(1, lb.Barrier, "守りが戻った");
            Assert.AreEqual(2, b.Board.Cores.Count(), "核が2つとも戻る");
            Assert.AreNotEqual(new Vector2Int(1, 0), b.Player.Position, "立っていた人は押し出される");
        }

        [Test]
        public void CopyRunFourthFloorOffersTheRoomNotChosenOnTheSecond()
        {
            var lookup = new System.Func<string, CommandData>(n => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{n}.asset"));
            var run = new ActRun(ActContent.Roles[0], 77, lookup, true);
            run.Enter(run.Doors[0]); run.Leave();
            var chosen = run.Doors[0];
            var other = run.Doors[1];
            run.Enter(chosen);
            Assert.IsTrue(run.Map[3].Contains(other), "4階に2階で選ばなかった部屋");
            Assert.IsFalse(run.Map[3].Contains(chosen));
        }

        [Test]
        public void LordDefeatEndsTheBattleEvenWithClonesAround()
        {
            var m = Board("...L...", "H.....H", "...P...");
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.Lord);
            lord.OverrideMaxHp(36);
            lord.Hp = 3;
            lord.Barrier = 0;
            lord.BarrierRule = 2; // 段階3
            var ev = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, m.Player, null, false, lord, 4, DamageKind.Direct, ev);
            Assert.IsFalse(lord.IsAlive);
            Assert.IsTrue(m.AllEnemiesDead, "主が倒れたら写し身も消えて勝ち");
        }
    }
}
