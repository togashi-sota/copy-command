using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中・Copy Core V2】コピーの方式（A 今／B 反転まとめて／C 反転すぐ／D あなたへすぐ）・COPY OFF と、
    /// コピー必須のギミック（反響鎧・封印・二つの踏み板・鏡晶・写し砲・鏡の番人）。
    /// </summary>
    public class CopyCoreV2Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 1) => new CommandRecord(Card(card), d, dist);
        static BattleModel Board(CopyMode mode, params string[] rows)
        {
            var m = BattleModel.FromRows(rows, 3, 20, 8);
            m.CopyMode = mode;
            return m;
        }
        static Vector2Int E(BattleModel m, int i = 0) => m.Enemies.ElementAt(i).Position;

        [Test]
        public void CopyOneFollowsTheModeRule()
        {
            var m = Board(CopyMode.Current, "P...E", ".....");
            var e = m.Enemies.First();
            var walk = R("Walk", Direction.Right);
            Assert.AreEqual(Direction.Right, CopyCore.CopyOne(walk, e, m.Player, CopyMode.Current).Direction, "A：同じ向き");
            Assert.AreEqual(Direction.Left, CopyCore.CopyOne(walk, e, m.Player, CopyMode.MirrorBatch).Direction, "B：反対向き");
            Assert.AreEqual(Direction.Left, CopyCore.CopyOne(walk, e, m.Player, CopyMode.MirrorImmediate).Direction, "C：反対向き");
            Assert.AreEqual(Direction.Left, CopyCore.CopyOne(R("Walk", Direction.Up), e, m.Player, CopyMode.TowardImmediate).Direction, "D：あなたの方へ（あなたは左）");
            Assert.IsNull(CopyCore.CopyOne(R("Sneak", Direction.Right), e, m.Player, CopyMode.MirrorImmediate), "忍び足はコピーされない");
            Assert.AreEqual(Direction.Right, CopyCore.CopyOne(R("MirrorSlash", Direction.Right), e, m.Player, CopyMode.MirrorImmediate).Direction, "反転斬りは反対の反対＝同じ向き");
        }

        [Test]
        public void MimicReflectCopiesMovesAndReflectsTechniques()
        {
            // E：動く札はまねる（同じ向き）、技（攻撃・押す・引く・入れ替え・罠）ははね返す（反対向き）。1枚ごと
            var m = Board(CopyMode.MimicReflectImmediate, "P....E", "......");
            var e = m.Enemies.First();
            Assert.AreEqual(Direction.Right, CopyCore.CopyOne(R("Walk", Direction.Right), e, m.Player, m.CopyMode).Direction);
            Assert.AreEqual(Direction.Left, CopyCore.CopyOne(R("Slash", Direction.Right), e, m.Player, m.CopyMode).Direction);
            Assert.AreEqual(Direction.Left, CopyCore.CopyOne(R("Push", Direction.Right), e, m.Player, m.CopyMode).Direction);
            Assert.AreEqual(Direction.Right, CopyCore.CopyOne(R("MirrorSlash", Direction.Right), e, m.Player, m.CopyMode).Direction, "反転斬りははね返らない");
            Assert.IsTrue(CopyCore.IsImmediate(m.CopyMode));
            // 歩いても間合いは変わらない／隣の敵を斬ると返ってくる
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null);
            Assert.AreEqual(new Vector2Int(1, 1), m.Player.Position);
            Assert.AreEqual(new Vector2Int(5, 1), e.Position, "敵も右へ（端で止まる）");
            var s = Board(CopyMode.MimicReflectImmediate, ".PE..", ".....");
            TurnRules.PlayTurn(s, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(16, s.Player.Hp, "当てた敵からは返る");
            var k = Board(CopyMode.MimicReflectImmediate, ".PE..", ".....");
            k.Enemies.First().Hp = 4;
            TurnRules.PlayTurn(k, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(20, k.Player.Hp, "とどめの一撃は返らない");
        }

        [Test]
        public void MirrorImmediateClosesTheDistanceStepByStep()
        {
            // あなた（左）と敵（右）の間 4 マス。C：一歩→ ごとに敵も一歩←（すぐ）。2回で間は 0
            var m = Board(CopyMode.MirrorImmediate, "P....E", "......");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right), R("Walk", Direction.Right) }, null, events);
            Assert.AreEqual(new Vector2Int(2, 1), m.Player.Position);
            Assert.AreEqual(new Vector2Int(3, 1), E(m), "敵は2回とも左へ");
            // 順番：あなた① → 敵① → あなた② → 敵②
            var movers = events.Where(e => e.Type == BattleEventType.Moved).Select(e => e.Unit.IsPlayer).ToList();
            CollectionAssert.AreEqual(new[] { true, false, true, false }, movers);
        }

        [Test]
        public void MirrorImmediateReturnsAFrontalSlash()
        {
            // 隣の敵を斬る → 敵もすぐ反対向き（あなたの方）へ斬り返す
            var m = Board(CopyMode.MirrorImmediate, "PE...", ".....");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(4, m.Enemies.First().Hp);
            Assert.AreEqual(16, m.Player.Hp, "斬り返された");
            // 押し斬りなら押した後なので返りは届かない
            var p = Board(CopyMode.MirrorImmediate, "PE...", ".....");
            TurnRules.PlayTurn(p, new List<CommandRecord> { R("PushSlash", Direction.Right) }, null);
            Assert.AreEqual(20, p.Player.Hp, "押し斬り：押した相手の返しは空振り");
        }

        [Test]
        public void MirrorBatchCopiesAfterAllCommandsAndCopyOffDoesNothing()
        {
            var b = Board(CopyMode.MirrorBatch, "P....E", "......");
            TurnRules.PlayTurn(b, new List<CommandRecord> { R("Walk", Direction.Right), R("Walk", Direction.Up) }, null);
            Assert.AreEqual(new Vector2Int(4, 0), E(b), "B：右→上 を、左→下 で");
            var off = Board(CopyMode.MirrorImmediate, "P....E", "......");
            off.CopyEnabled = false;
            TurnRules.PlayTurn(off, new List<CommandRecord> { R("Walk", Direction.Right) }, null);
            Assert.AreEqual(new Vector2Int(5, 1), E(off), "COPY OFF：敵は動かない");
        }

        [Test]
        public void EchoArmorIgnoresYourTechniquesButNotCopies()
        {
            var m = Board(CopyMode.MirrorImmediate, "PR...", "....E"); // 反響鎧は最後の1体だと外れるので、もう1体置く
            var knight = m.Enemies.First();
            Assert.AreEqual(EnemyKind.EchoKnight, knight.Kind);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(8, knight.Hp, "あなたの斬撃は弾かれる");
            // コピーの移動でトゲに入る → 傷つく
            var s = Board(CopyMode.MirrorImmediate, "P..^R", ".....");
            TurnRules.PlayTurn(s, new List<CommandRecord> { R("Walk", Direction.Down) }, null);
            Assert.AreEqual(8, s.Enemies.First().Hp);
            var s2 = Board(CopyMode.MirrorImmediate, "P..^R", ".....");
            TurnRules.PlayTurn(s2, new List<CommandRecord> { R("Walk", Direction.Right) }, null);
            Assert.AreEqual(5, s2.Enemies.First().Hp, "敵は反対向き（左）に歩いてトゲへ：3");
            // あなたの押し出しでトゲへ → 弾く（あなたの技）
            var push = Board(CopyMode.Current, "....E", "PR^..");
            push.CopyEnabled = false;
            TurnRules.PlayTurn(push, new List<CommandRecord> { R("Push", Direction.Right) }, null);
            Assert.AreEqual(8, push.Enemies.First().Hp, "あなたの押し出しによるトゲは弾く");
        }

        [Test]
        public void SealBreaksOnlyWithCopiedHits()
        {
            // 敵A（通常）が封印兵（G）の左隣。あなたが左へ斬ると、敵A は反対（右）＝封印兵を斬る
            var m = Board(CopyMode.MirrorImmediate, ".EG..", ".....", "...P.");
            var guard = m.Enemies.ElementAt(1);
            Assert.AreEqual(2, guard.Barrier);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left), R("Slash", Direction.Left) }, null);
            Assert.AreEqual(0, guard.Barrier, "コピー攻撃2回で封印が割れた");
            Assert.AreEqual(8, guard.Hp, "封印のあるうちは傷つかない");
            var direct = Board(CopyMode.MirrorImmediate, "PG...", ".....");
            TurnRules.PlayTurn(direct, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(2, direct.Enemies.First().Barrier, "あなたの斬撃では封印は減らない");
        }

        [Test]
        public void TwoPlatesOpenTheGateWhenBothAreStepped()
        {
            // あなたが右へ1歩で p、敵は反対（左）へ1歩で q → 扉が開く
            var m = Board(CopyMode.MirrorImmediate, "P.p....q.E", "#####g####", ".........E");
            Assert.IsTrue(m.Board.IsWall(m.Board.Gates[0]), "最初は閉じている");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right), R("Walk", Direction.Right) }, null, events);
            Assert.IsTrue(m.Board.GatesOpen);
            Assert.IsFalse(m.Board.IsWall(m.Board.Gates[0]));
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.GateOpened));
            var off = Board(CopyMode.MirrorImmediate, "P.p....q.E", "#####g####", ".........E");
            off.CopyEnabled = false;
            TurnRules.PlayTurn(off, new List<CommandRecord> { R("Walk", Direction.Right), R("Walk", Direction.Right) }, null);
            Assert.IsFalse(off.Board.GatesOpen, "COPY OFF：敵が来ないので開かない");
            var clone = m.Clone();
            Assert.IsTrue(clone.Board.GatesOpen, "複製も開いたまま");
        }

        [Test]
        public void CrystalBurstsOnlyFromACopiedAttack()
        {
            // 敵の右隣に鏡晶。あなたが左へ斬る → 敵は反対（右）＝鏡晶を斬る → 砕けて、まわりの敵に 4
            var m = Board(CopyMode.MirrorImmediate, ".EX..", ".....", "...P.");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null);
            Assert.IsFalse(m.Board.IsCrystal(new Vector2Int(2, 2)));
            Assert.AreEqual(4, m.Enemies.First().Hp, "鏡晶の爆発 4");
            // あなたの攻撃でははじく
            var p = Board(CopyMode.MirrorImmediate, "PX...", ".....", "....E");
            TurnRules.PlayTurn(p, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.IsTrue(p.Board.IsCrystal(new Vector2Int(1, 2)), "あなたの斬撃では砕けない");
        }

        [Test]
        public void CannonShootsCopiedAttacksAndIsNotATarget()
        {
            // 写し砲（Z）は左下、反響鎧兵（R）はその列の上。あなたが下へ斬る → 写し砲は反対（上）へ撃つ → 反響鎧兵に 4
            var m = Board(CopyMode.MirrorImmediate, "R....", ".....", "....P", "Z....");
            var knight = m.Enemies.First(e => e.Kind == EnemyKind.EchoKnight);
            var cannon = m.Enemies.First(e => e.Kind == EnemyKind.Cannon);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Down), R("Walk", Direction.Left) }, null);
            Assert.AreEqual(4, knight.Hp, "写し砲の弾は反響鎧を通る");
            Assert.AreEqual(new Vector2Int(0, 0), cannon.Position, "写し砲は動かない");
            knight.Hp = 0;
            Assert.IsTrue(m.AllEnemiesDead, "写し砲は勝ち負けに数えない");
        }

        [Test]
        public void WardenBarrierChangesHowItBreaks()
        {
            var m = Board(CopyMode.MirrorImmediate, ".EW..", ".....", "...P.");
            var w = m.Enemies.First(e => e.Kind == EnemyKind.Warden);
            w.OverrideMaxHp(24);
            w.Barrier = PhaseRules.WardenBarrier(0);
            Assert.AreEqual(0, w.BarrierRule);
            // 段階0：コピー攻撃2回（敵A が右を斬る）
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left), R("Slash", Direction.Left) }, null);
            Assert.AreEqual(0, w.Barrier);
            // 2ラウンドで戻る（そのときの段階の割り方）
            w.Hp = 12; // 段階1（2/3〜1/3）
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up) }, null);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Down) }, null);
            Assert.AreEqual(PhaseRules.WardenBarrier(1), w.Barrier, "封印が戻った");
            Assert.AreEqual(1, w.BarrierRule, "段階1：鏡晶の爆発だけ");
            var before = w.Barrier;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null);
            Assert.AreEqual(before, w.Barrier, "段階1ではコピー攻撃では割れない");
            // 段階2：封印の円にコピーで入ると割れる
            var s = Board(CopyMode.MirrorImmediate, "..o.W", ".....", "...P.");
            var w2 = s.Enemies.First();
            w2.OverrideMaxHp(24); w2.Hp = 6; w2.Barrier = 1; w2.BarrierRule = 2;
            TurnRules.PlayTurn(s, new List<CommandRecord> { R("Dash", Direction.Right, 2) }, null);
            Assert.AreEqual(0, w2.Barrier, "敵は反対（左）へ2マス＝封印の円");
        }

        [Test]
        public void WardenDamageStopsAtPhaseLineAndBarrierReturnsAtHome()
        {
            // 段階の境目（24 の 2/3＝16）をまたぐダメージはそこで止まり、すぐ次の段階の封印が戻る。番人は最初の位置へ帰る
            var m = Board(CopyMode.MirrorImmediate, "..W..", ".....", "P....");
            var w = m.Enemies.First();
            var home = w.Position;
            w.OverrideMaxHp(24);
            w.Barrier = 0;
            w.Hp = 18;
            w.Position = new Vector2Int(4, 0);
            Assert.IsNull(CopyCore.CopyOne(R("Slash", Direction.Left), w, m.Player, CopyMode.MirrorImmediate), "封印が割れている間は写せない（殴り返さない）");
            var ev = new List<BattleEvent>();
            int dealt = RulesEngine.ApplyDamage(m, w, null, true, w, 4, DamageKind.Direct, ev);
            Assert.AreEqual(2, dealt, "16 で止まる");
            Assert.AreEqual(16, w.Hp);
            Assert.AreEqual(1, w.BarrierRule, "次の段階（鏡晶）");
            Assert.AreEqual(PhaseRules.WardenBarrier(1), w.Barrier, "封印がすぐ戻る");
            Assert.AreEqual(home, w.Position, "中央へ帰る");
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.BarrierRestored));
            Assert.IsNotNull(CopyCore.CopyOne(R("Slash", Direction.Left), w, m.Player, CopyMode.MirrorImmediate), "封印が戻ればまた写す");
        }

        [Test]
        public void CrystalsRegrowEachRoundWhileWardenLives()
        {
            // 兵が先に倒れても「コピーで割る」手段が無くならないように、番人がいる間は砕けた鏡晶がラウンドの終わりに戻る
            var m = Board(CopyMode.MirrorImmediate, "X.W..", ".....", "...P.");
            var x = new Vector2Int(0, 2);
            Assert.IsTrue(m.Board.IsCrystal(x));
            m.Board.BreakCrystal(x);
            Assert.IsFalse(m.Board.IsCrystal(x));
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up) }, null);
            Assert.IsTrue(m.Board.IsCrystal(x), "ラウンドの終わりに戻る");
        }

        [Test]
        public void SandboxStagesExistAndAreCopyRequiredBoards()
        {
            foreach (var n in new[] { "StageCV1", "StageCV2", "StageCV3", "StageCV4", "StageCV5", "StageCV6" })
            {
                var s = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{n}.asset");
                Assert.IsNotNull(s, n);
                Assert.IsTrue(s.copySandbox, n);
                var m = BattleModel.FromStage(s);
                Assert.AreEqual(CopyMode.MirrorImmediate, m.CopyMode, n + "：既定は C");
                Assert.IsTrue(m.Enemies.Any(e => e.EchoArmor || e.Barrier > 0 || e.Kind == EnemyKind.Cannon) || m.Board.Gates.Count > 0, n + "：コピー必須の仕掛けがある");
            }
            // ACT1 のステージは今のまま（A）
            var act = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageA1.asset");
            Assert.IsFalse(act.copySandbox);
            CopyCore.SandboxMode = CopyMode.MirrorImmediate;
            CopyCore.SandboxCopyOff = true;
            var am = BattleModel.FromStage(act);
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            Assert.AreEqual(CopyMode.Current, am.CopyMode, "サンドボックスの切替は ACT1 に効かない");
            Assert.IsTrue(am.CopyEnabled);
        }
    }
}
