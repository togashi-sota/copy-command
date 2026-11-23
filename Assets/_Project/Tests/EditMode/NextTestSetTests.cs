using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】次期検証セット：兵種（弓兵・盾兵・追跡兵・逃げ兵・鏡守の騎士）、向き、炎床、回復ルーン、フェーズの順番
    /// （コピー → 兵種の行動 → 環境）。ゲーム（TurnManager）と解析（TurnRules）が同じ結果になることも確かめる。
    /// </summary>
    public class NextTestSetTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");

        static CommandRecord C(string card, Direction dir)
        {
            var data = Card(card);
            return new CommandRecord(data, dir, Mathf.Max(0, data.minDistance));
        }

        static BattleModel Rows(string map, int enemyHp = 8) => BattleModel.FromRows(map.Split('\n'), 5, 12, enemyHp);

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        StageData MakeStage(string map, List<string> fire = null, int enemyHp = 8)
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map;
            s.enemyMaxHp = enemyHp;
            s.firePatterns = fire ?? new List<string>();
            s.availableCommands = new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
            return s;
        }

        // ───────── 向き・弓兵 ─────────

        [Test]
        public void FacingFollowsTheLastDirectionalCommandCopied()
        {
            var m = Rows("P....\n.....\n..K..");
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Walk", Direction.Up), C("Slash", Direction.Right) }, null);
            Assert.AreEqual(Direction.Right, m.Enemies.First().Facing, "最後にコピーした命令の方向を向く（空振りでも）");
            Assert.AreEqual(Direction.Right, m.Player.Facing);
        }

        [Test]
        public void ArcherShootsAlongItsFacingAfterTheCopyAndCanHitAnotherEnemy()
        {
            var m = Rows(".....\n.K..E\n..P..");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Slash", Direction.Right) }, null, events);
            var enemyB = m.Enemies.ElementAt(1);
            Assert.AreEqual(6, enemyB.Hp, "弓兵の矢（2）が別の敵に当たる");
            var hit = events.Single(e => e.Type == BattleEventType.Damaged && e.Unit == enemyB);
            Assert.AreEqual(DamageKind.Trait, hit.Cause.Kind);
            Assert.AreEqual(m.Enemies.First(), hit.Cause.Source);
        }

        [Test]
        public void ArrowHitsThePlayerAndStopsAtWalls()
        {
            var m = Rows(".K.P.");
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Slash", Direction.Right) }, null);
            Assert.AreEqual(10, m.Player.Hp, "矢はプレイヤーにも当たる");

            var walled = Rows(".K#P.");
            TurnRules.PlayTurn(walled, new List<CommandRecord> { C("Slash", Direction.Right) }, null);
            Assert.AreEqual(12, walled.Player.Hp, "壁の向こうには届かない");
        }

        [Test]
        public void ArrowRangeIsFour()
        {
            var m = Rows("K.....P");
            m.Enemies.First().Facing = Direction.Right;
            Assert.AreEqual(4, PhaseRules.ArrowCells(m, m.Enemies.First()).Count);
        }

        // ───────── 盾兵 ─────────

        [Test]
        public void ShieldReducesOnlyFrontalDirectAttacks()
        {
            var front = Rows(".PT..");
            front.Enemies.First().Facing = Direction.Left; // あなたの方を向いている
            TurnRules.PlayCommandsAndCopies(front, new List<CommandRecord> { C("Slash", Direction.Right) }, null);
            Assert.AreEqual(7, front.Enemies.First().Hp, "正面からの斬撃は 4-3=1");

            var side = Rows(".PT..");
            side.Enemies.First().Facing = Direction.Up;
            TurnRules.PlayCommandsAndCopies(side, new List<CommandRecord> { C("Slash", Direction.Right) }, null);
            Assert.AreEqual(4, side.Enemies.First().Hp, "横からは盾が効かない");

            var spike = Rows(".PT^.");
            spike.Enemies.First().Facing = Direction.Left;
            TurnRules.PlayCommandsAndCopies(spike, new List<CommandRecord> { C("Push", Direction.Right) }, null);
            Assert.AreEqual(3, spike.Enemies.First().Hp, "トゲには盾が効かない");
        }

        // ───────── 追跡兵・逃げ兵 ─────────

        [Test]
        public void ChaserStepsTowardThePlayerWithAFixedTieBreak()
        {
            var m = Rows("C....\n.....\n..P..");
            Assert.AreEqual(new Vector2Int(1, 2), PhaseRules.StepTarget(m, m.Enemies.First()), "縦横の距離が同じなら横を先に");
            var spike = Rows("C^...\n.....\n..P..");
            Assert.AreEqual(new Vector2Int(0, 1), PhaseRules.StepTarget(spike, spike.Enemies.First()), "トゲには自分から入らない（もう一方へ）");
            var adjacent = Rows(".CP..");
            Assert.IsNull(PhaseRules.StepTarget(adjacent, adjacent.Enemies.First()), "隣まで来たら止まる");
        }

        [Test]
        public void FleerStepsAwayAndStopsWhenCornered()
        {
            var m = Rows(".....\n..N..\n..P..");
            Assert.AreEqual(new Vector2Int(2, 2), PhaseRules.StepTarget(m, m.Enemies.First()));
            var cornered = Rows("..N..\n..P..");
            Assert.IsNull(PhaseRules.StepTarget(cornered, cornered.Enemies.First()), "端で逃げられなければ止まる");
        }

        [Test]
        public void TraitPhaseComesAfterTheCopy()
        {
            // 追跡兵は、コピーで動いた後の位置から近づく
            var m = Rows("C....\n.....\n.....\n....P");
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Walk", Direction.Up) }, null);
            Assert.AreEqual(new Vector2Int(1, 3), m.Enemies.First().Position, "コピーの一歩↑は上の端で止まり、そのあと右へ1マス近づく");
        }

        // ───────── 炎床・騎士 ─────────

        [Test]
        public void FireBurnsEveryoneOnItAndFollowsItsCycle()
        {
            var s = MakeStage("P.E\n...", new List<string> { "F.F/...", ".../..." });
            var m = BattleModel.FromStage(s);
            var stall = new List<CommandRecord> { C("Slash", Direction.Up) };
            TurnRules.PlayTurn(m, stall, null);
            Assert.AreEqual(9, m.Player.Hp, "ラウンド1：あなたも");
            Assert.AreEqual(5, m.Enemies.First().Hp, "敵も燃える");
            TurnRules.PlayTurn(m, stall, null);
            Assert.AreEqual(9, m.Player.Hp, "ラウンド2：燃えない");
            TurnRules.PlayTurn(m, stall, null);
            Assert.AreEqual(6, m.Player.Hp, "ラウンド3：また燃える（循環）");
        }

        [Test]
        public void KnightLosesItsShieldAndStartsShootingAtHalfHp()
        {
            var m = Rows("..B..\n.....\n..P..", enemyHp: 20);
            var knight = m.Enemies.First();
            Assert.IsTrue(knight.HasShield);
            Assert.IsFalse(knight.ShootsArrows);
            knight.Hp = 10;
            Assert.IsFalse(knight.HasShield);
            Assert.IsTrue(knight.ShootsArrows);
        }

        [Test]
        public void KnightFireStartsTheRoundAfterItFallsToHalf()
        {
            var s = MakeStage("..B..\n.....\n..P..", new List<string> { "...../FFFFF/....." }, enemyHp: 20);
            s.fireAfterKnightHalf = true;
            var m = BattleModel.FromStage(s);
            Assert.AreEqual(0, m.Environment.FireCellsNow.Count, "最初は燃えない");
            m.Enemies.First().Hp = 10;
            PhaseRules.EnvironmentPhase(m, null, null);
            Assert.AreEqual(12, m.Player.Hp, "半分になったラウンドはまだ燃えない（予告してから）");
            Assert.AreEqual(5, m.Environment.FireCellsNow.Count, "次のラウンドから燃える");
        }

        // ───────── 回復ルーン ─────────

        [Test]
        public void RuneHealsWhoeverEntersThenMovesAndRunsOut()
        {
            var s = MakeStage("P1E2\n....");
            var m = BattleModel.FromStage(s);
            m.Player.Hp = 6;
            m.Enemies.First().Hp = 4;
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Walk", Direction.Right) }, null);
            Assert.AreEqual(9, m.Player.Hp, "あなたが1に入って回復");
            Assert.AreEqual(7, m.Enemies.First().Hp, "コピーで敵が2に入って回復");
            Assert.AreEqual(1, m.Environment.RuneUsesLeft);
            Assert.AreEqual(new Vector2Int(1, 1), m.Environment.RuneCell, "次は1に戻る（循環）");
            m.Player.Position = new Vector2Int(0, 1);
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Slash", Direction.Down) }, null);
            m.Player.Hp = 6;
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Walk", Direction.Right) }, null);
            Assert.IsNull(m.Environment.RuneCell, "3回使うと消える（永久回復しない）");
        }

        [Test]
        public void HealIsCappedAtMaxHp()
        {
            var s = MakeStage("P1.E\n....");
            var m = BattleModel.FromStage(s);
            m.Player.Hp = 11;
            TurnRules.PlayTurn(m, new List<CommandRecord> { C("Walk", Direction.Right) }, null);
            Assert.AreEqual(12, m.Player.Hp);
        }

        // ───────── ステージとゲームの一致 ─────────

        static readonly string[] NewStages = { "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageMB1", "StageM11", "StageM12", "StageV1", "StageV2", "StageV3", "StageV4", "StageV5", "StageV6", "StageVB", "StageVS", "StageA1", "StageA2", "StageA3", "StageA4", "StageAB" };

        [Test]
        public void NewStagesAreTestStagesWithTheValidationDeck([ValueSource(nameof(NewStages))] string name)
        {
            var s = Stage(name);
            Assert.IsNotNull(s, name);
            Assert.IsTrue(s.isTestStage);
            Assert.IsTrue(s.handTestGuaranteeAttack);
            var m = BattleModel.FromStage(s);
            Assert.IsTrue(m.Enemies.Count() >= 1);
            // 炎は敵の最初の位置を焼かない（あなたが何もしなくても敵が燃え尽きる、を防ぐ）
            foreach (var pattern in m.Environment.FirePatterns)
                foreach (var e in m.Enemies)
                    Assert.IsFalse(pattern.Contains(e.Position), $"{name}：{e.Name} の最初の位置が燃える");
        }

        [Test]
        public void NoSingleCardFarming([ValueSource(nameof(NewStages))] string name)
        {
            var s = Stage(name);
            foreach (var seq in BattleSolver.Sequences(new BattleSolver.Settings { Cards = s.availableCommands, MaxSlots = 1 }))
            {
                var m = BattleModel.FromStage(s);
                for (int t = 0; t < 10; t++)
                {
                    var o = TurnRules.PlayTurn(m, seq, null);
                    Assert.AreNotEqual(TurnOutcome.Cleared, o, $"{name}：{seq[0].ToDisplayText()} の繰り返しで勝てる");
                    if (o == TurnOutcome.GameOver) break;
                }
            }
        }

        [Test]
        public void GameAndRulesAgreeOnRandomPlays([ValueSource(nameof(NewStages))] string name)
        {
            var stage = Stage(name);
            var rng = new System.Random(name.GetHashCode() & 0x7fffffff);
            var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = 3 });
            for (int game = 0; game < 6; game++)
            {
                var root = new GameObject("Parity");
                var grid = root.AddComponent<GridManager>();
                var turn = root.AddComponent<TurnManager>();
                turn.SetupForTest(grid, new List<StageData> { stage });
                turn.LoadStage(0);
                var model = BattleModel.FromStage(stage);
                for (int t = 0; t < 8 && turn.State == BattleState.PlayerInput; t++)
                {
                    var seq = seqs[rng.Next(seqs.Count)];
                    foreach (var r in seq) Assert.IsTrue(turn.AddCommand(r.Clone()));
                    turn.ExecuteTurnImmediately();
                    var outcome = TurnRules.PlayTurn(model, seq, null);
                    string where = $"{name} 試合{game} ターン{t + 1}";
                    for (int u = 0; u < model.Units.Count; u++)
                    {
                        Assert.AreEqual(model.Units[u].Position, grid.Model.Units[u].Position, $"{where}：{model.Units[u].Name} の位置");
                        Assert.AreEqual(model.Units[u].Hp, grid.Model.Units[u].Hp, $"{where}：{model.Units[u].Name} の HP");
                        Assert.AreEqual(model.Units[u].Facing, grid.Model.Units[u].Facing, $"{where}：{model.Units[u].Name} の向き");
                    }
                    Assert.AreEqual(model.Environment.StateKey(), grid.Model.Environment.StateKey(), $"{where}：環境");
                    var expected = turn.State == BattleState.Cleared ? TurnOutcome.Cleared : turn.State == BattleState.GameOver ? TurnOutcome.GameOver : TurnOutcome.Continue;
                    Assert.AreEqual(expected, outcome, $"{where}：勝敗");
                }
                Object.DestroyImmediate(root);
            }
        }
    }
}
