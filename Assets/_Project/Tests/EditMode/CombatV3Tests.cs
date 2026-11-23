using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】Combat V3：鏡守の騎士V3（攻撃だけ反転コピー＋カウントダウン攻撃）。
    /// </summary>
    public class CombatV3Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        CommandData Make(string name, CommandType type, int min = 0, int max = 0, int damage = 0)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = name;
            c.type = type;
            c.minDistance = min;
            c.maxDistance = max;
            c.damage = damage;
            c.range = 1;
            created.Add(c);
            return c;
        }

        static BattleModel Rows(string map, int enemyHp = 40)
        {
            var m = BattleModel.FromRows(map.Split('\n'), 3, 12, enemyHp);
            m.ApplyStage(null);
            return m;
        }

        static List<CommandRecord> One(CommandData c, Direction d, int dist = 0) => new List<CommandRecord> { new CommandRecord(c, d, dist) };

        [Test]
        public void KnightV3ReflectsAttacksButCopiesMovesNormally()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var m = Rows(".....\n..Q..\n..P..\n.....\n.....");
            Assert.AreEqual(CopyTrait.MirrorAttacks, m.Enemies.First().CopyTrait);
            TurnRules.PlayCommandsAndCopies(m, One(slash, Direction.Up), null);
            Assert.AreEqual(36, m.Enemies.First().Hp, "あなたの斬撃 4");
            Assert.AreEqual(8, m.Player.Hp, "騎士の斬撃は反対（下）＝あなたへ返る");

            var move = Rows(".....\n..Q..\n.....\n..P..\n.....");
            TurnRules.PlayCommandsAndCopies(move, One(walk, Direction.Left, 1), null);
            Assert.AreEqual(new Vector2Int(1, 3), move.Enemies.First().Position, "移動はそのまま（左へ）");
        }

        [Test]
        public void SlamCountsDownTelegraphsThenHits()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            // あなたは騎士の斜め隣。左へ斬る（空振り）→ 騎士は右へ斬る（空振り）。剣のカウントだけが進む
            var m = Rows(".......\n.......\n.......\n...Q...\n..P....\n.......\n.......");
            var k = m.Enemies.First();
            Assert.AreEqual(PhaseRules.SlamPeriod, k.SlamCd, "最初は 3");
            TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
            Assert.AreEqual(2, k.SlamCd);
            TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
            Assert.AreEqual(1, k.SlamCd, "1 になったら予告");
            Assert.AreEqual(8, k.SlamCells.Count, "騎士の周り8マス");
            Assert.AreEqual(12, m.Player.Hp);
            TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
            Assert.AreEqual(8, m.Player.Hp, "大剣 4");
            Assert.AreEqual(PhaseRules.SlamPeriod, k.SlamCd, "周期に戻る");
        }

        [Test]
        public void AllCountdownsAreVisibleFromTheStartAndPhasesSpeedThemUp()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            var m = Rows(".......\n.......\n.......\n...Q...\n...P...\n.......\n.......", 55);
            var k = m.Enemies.First();
            Assert.AreEqual(3, k.SlamCd, "剣3");
            Assert.AreEqual(4, k.BlastCd, "爆4");
            Assert.AreEqual(6, k.WaveCd, "炎6");
            k.Hp = 33; // 60%
            k.SlamCd = 1;
            TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
            Assert.AreEqual(2, k.SlamCd, "60% 以下：大剣の周期 3→2");
            k.Hp = 16; // 30%
            k.BlastCd = 2;
            TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
            Assert.AreEqual(1, k.BlastCd);
            Assert.AreEqual(9, k.BombCells.Count, "30% 以下：爆発は大十字（9マス）");
        }

        [Test]
        public void BossVariantIsAnalysisOnlyAndChangesPeriodsByPhase()
        {
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            try
            {
                // 【研究】最初から全部見える案：段階で新しい攻撃は始まらず、周期が段階で変わる
                PhaseRules.ExperimentBossV3 = new PhaseRules.BossV3Variant { AllFromStart = true, SlamPeriod = new[] { 3, 2, 2 } };
                var m = Rows(".......\n.......\n.......\n...Q...\n.......\nP......\n.......");
                var k = m.Enemies.First();
                k.Hp = 24; // 60%
                k.SlamCd = 2;
                TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
                TurnRules.PlayTurn(m, One(slash, Direction.Left), null);
                Assert.AreEqual(2, k.SlamCd, "第2段階の大剣の周期は 2");
                Assert.AreEqual(PhaseRules.BossV3.BlastStart - 2, k.BlastCd, "最初から全部見える案では、爆発は最初から数えている（段階で始め直さない）");
            }
            finally
            {
                RulesEngine.ResetExperiments();
            }
            Assert.IsNull(PhaseRules.ExperimentBossV3, "ゲームでは毎回今の仕様に戻る");
        }

        [Test]
        public void WaveAlternatesCenterAndOuterRing()
        {
            var m = Rows(".......\n.......\n.......\n...Q...\n.......\nP......\n.......");
            Assert.AreEqual(9, PhaseRules.WaveCells(m, 0).Count, "中央3×3");
            Assert.AreEqual(24, PhaseRules.WaveCells(m, 1).Count, "7×7 の外周");
        }
    }
}
