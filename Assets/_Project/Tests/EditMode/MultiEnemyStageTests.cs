using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 複数Enemyテストステージ M1〜M3 の設計意図を固定するテスト（盤面を変えたら、ここが壊れて気づける）。
    /// ルートは C# 解析（SolverRunner）で見つけたもの。理由は docs/LEVEL_DESIGN.md 9章。
    /// </summary>
    public class MultiEnemyStageTests
    {
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");

        static CommandRecord C(string card, Direction dir)
        {
            var data = Card(card);
            return new CommandRecord(data, dir, Mathf.Max(0, data.minDistance));
        }

        /// <summary>想定ルート（SolverRunner の結果を、意味の分かる最小の形にしたもの）。</summary>
        static readonly Dictionary<string, List<CommandRecord>[]> Routes = new Dictionary<string, List<CommandRecord>[]>
        {
            // M1：敵A を斬ってから下へ逃げる → 空いたマスに敵B が入り、同じ「斬撃←」で敵A を斬る。残った敵B は上へ斬れば安全
            ["StageM1"] = new[]
            {
                new List<CommandRecord> { C("Walk", Direction.Left), C("Slash", Direction.Left), C("Dash", Direction.Down) },
                new List<CommandRecord> { C("Slash", Direction.Up) },
                new List<CommandRecord> { C("Slash", Direction.Up) },
            },
            // M2：敵A を敵B の真下へ運んで「斬撃↑」を渡す（同士討ち）→ 次は敵A を挟んで、自分と敵B の両方から「斬撃←」
            ["StageM2"] = new[]
            {
                new List<CommandRecord> { C("Walk", Direction.Down), C("Dash", Direction.Right), C("Slash", Direction.Up) },
                new List<CommandRecord> { C("Walk", Direction.Up), C("Dash", Direction.Up), C("Slash", Direction.Left) },
                new List<CommandRecord> { C("Slash", Direction.Up) },
            },
            // M3：「一歩←」は自分には位置調整、敵A には「トゲへ入る」命令。右の敵B は斬撃→で削る → 次に「駆け足←」で敵A をもう一度トゲへ
            ["StageM3"] = new[]
            {
                new List<CommandRecord> { C("Walk", Direction.Left), C("Dash", Direction.Right), C("Slash", Direction.Right) },
                new List<CommandRecord> { C("Dash", Direction.Left), C("Walk", Direction.Right), C("Slash", Direction.Right) },
            },
        };

        [Test]
        public void MultiTestStagesAreSetUp([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            var s = Stage(name);
            Assert.IsNotNull(s, name);
            Assert.IsTrue(s.isTestStage, "正式ステージの「次へ」の流れには入れない");
            Assert.AreEqual(3, s.maxSlots);
            Assert.IsNull(s.enemyInnateAction, "敵は渡された命令でしか動かない（固有行動なし）");
            var model = BattleModel.FromStage(s);
            Assert.AreEqual(2, model.Enemies.Count(), "通常コピーの敵2体");
            Assert.IsTrue(model.Enemies.All(e => e.CopyTrait == CopyTrait.Normal && e.MaxHp == 8));
            CollectionAssert.AreEquivalent(new[] { "一歩", "駆け足", "斬撃", "押し出し" }, s.availableCommands.Select(c => c.displayName).ToArray());
        }

        [Test]
        public void IntendedRouteClearsWithoutDamage([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            var model = BattleModel.FromStage(Stage(name));
            var outcome = TurnOutcome.Continue;
            foreach (var turn in Routes[name])
            {
                Assert.AreEqual(TurnOutcome.Continue, outcome, "ルートの途中で終わった");
                outcome = TurnRules.PlayTurn(model, turn, null);
            }
            Assert.AreEqual(TurnOutcome.Cleared, outcome);
            Assert.AreEqual(12, model.Player.Hp, "考えれば無傷で勝てる");
        }

        [Test]
        public void IntendedRouteMatchesTheGame([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            // ゲーム本体（TurnManager）で同じルートを入力し、解析（TurnRules）と位置・HP・勝敗が一致すること
            var root = new GameObject("MultiStageTest");
            var grid = root.AddComponent<GridManager>();
            var turnManager = root.AddComponent<TurnManager>();
            turnManager.SetupForTest(grid, new List<StageData> { Stage(name) });
            turnManager.LoadStage(0);
            var model = BattleModel.FromStage(Stage(name));
            foreach (var turn in Routes[name])
            {
                foreach (var r in turn) Assert.IsTrue(turnManager.AddCommand(r.Clone()), $"{r.ToDisplayText()} を登録できない");
                turnManager.ExecuteTurnImmediately();
                TurnRules.PlayTurn(model, turn, null);
                for (int u = 0; u < model.Units.Count; u++)
                {
                    Assert.AreEqual(grid.Units[u].GridPosition, model.Units[u].Position, model.Units[u].Name);
                    Assert.AreEqual(grid.Units[u].Hp, model.Units[u].Hp, model.Units[u].Name);
                }
            }
            Assert.AreEqual(BattleState.Cleared, turnManager.State);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void M1_SameSlashHitsYouFromTheOtherSide([Values(Direction.Left, Direction.Right)] Direction dir)
        {
            // 隣の敵を斬ると、反対側の敵のコピーがあなたを斬る（敵1体のときの「方向攻撃の法則」が崩れる）
            var model = BattleModel.FromStage(Stage("StageM1"));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(model, new List<CommandRecord> { C("Slash", dir) }, null, events);
            var hitMe = events.Single(e => e.Type == BattleEventType.Damaged && e.Unit.IsPlayer);
            Assert.IsTrue(hitMe.Cause.ByCopy);
            Assert.AreEqual(dir == Direction.Right ? "敵A" : "敵B", hitMe.Cause.Source.Name, "反対側の敵");
            Assert.AreEqual(8, model.Player.Hp);
        }

        [Test]
        public void M1_EnemyBStepsIntoYourPlaceAndFinishesEnemyA()
        {
            var model = BattleModel.FromStage(Stage("StageM1"));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(model, Routes["StageM1"][0], null, events);
            var ff = events.Single(e => e.Type == BattleEventType.Damaged && e.Cause.Source.Name == "敵B");
            Assert.AreEqual("敵A", ff.Unit.Name, "敵B のコピー「斬撃←」が敵A に当たる（同士討ち）");
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Died && e.Unit.Name == "敵A"));
            Assert.AreEqual(12, model.Player.Hp);
        }

        [Test]
        public void M2_FriendlyFireHappensOnlyWhenPlanned()
        {
            // 想定ルートでは、同士討ちが2回起きる（敵A→敵B、敵B→敵A）
            var model = BattleModel.FromStage(Stage("StageM2"));
            var events = new List<BattleEvent>();
            foreach (var turn in Routes["StageM2"]) TurnRules.PlayTurn(model, turn, null, events);
            var ff = events.Where(e => e.Type == BattleEventType.Damaged && !e.Unit.IsPlayer && !e.Cause.Source.IsPlayer).ToList();
            Assert.AreEqual(2, ff.Count);
            Assert.IsTrue(ff.All(e => e.Cause.ByCopy && e.Cause.Source != e.Unit));

            // 最初の配置のまま、移動しない命令（斬撃・押し出し）だけでは、敵同士は一度も当たらない（勝手に殴り合わない）
            foreach (var card in new[] { "Slash", "Push" })
                foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                {
                    var m = BattleModel.FromStage(Stage("StageM2"));
                    var ev = new List<BattleEvent>();
                    TurnRules.PlayTurn(m, new List<CommandRecord> { C(card, d) }, null, ev);
                    Assert.IsFalse(ev.Any(e => e.Type == BattleEventType.Damaged), $"{card} {d}");
                }
        }

        [Test]
        public void M3_LuringEnemyAOntoTheSpikeLetsEnemyBHitYou()
        {
            // 「駆け足← / 斬撃←」：敵A はトゲ＋あなたの斬撃で倒れるが、同じ駆け足で隣に来た敵B の「斬撃←」があなたに当たる
            var model = BattleModel.FromStage(Stage("StageM3"));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(model, new List<CommandRecord> { C("Dash", Direction.Left), C("Slash", Direction.Left) }, null, events);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Damaged && e.Unit.Name == "敵A" && e.Cause.Kind == DamageKind.Spike));
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Died && e.Unit.Name == "敵A"));
            var hitMe = events.Single(e => e.Type == BattleEventType.Damaged && e.Unit.IsPlayer);
            Assert.AreEqual("敵B", hitMe.Cause.Source.Name);
            Assert.AreEqual(8, model.Player.Hp);
        }

        [Test]
        public void M3_RouteUsesTheSpikeThroughCopies()
        {
            var model = BattleModel.FromStage(Stage("StageM3"));
            var events = new List<BattleEvent>();
            foreach (var turn in Routes["StageM3"]) TurnRules.PlayTurn(model, turn, null, events);
            var spikeHits = events.Where(e => e.Type == BattleEventType.Damaged && e.Cause.Kind == DamageKind.Spike).ToList();
            Assert.AreEqual(2, spikeHits.Count, "敵A がコピーで2回トゲに入る");
            Assert.IsTrue(spikeHits.All(e => e.Unit.Name == "敵A" && e.Cause.ByCopy));
        }

        [Test]
        public void NoSingleCardFarming([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            // 同じ1命令を毎ターン繰り返すだけでは勝てない（単純作業のハメが無い）
            var s = Stage(name);
            var settings = new BattleSolver.Settings { Cards = s.availableCommands, MaxSlots = 1 };
            foreach (var seq in BattleSolver.Sequences(settings))
            {
                var model = BattleModel.FromStage(s);
                for (int t = 0; t < 10; t++)
                {
                    var outcome = TurnRules.PlayTurn(model, seq, null);
                    Assert.AreNotEqual(TurnOutcome.Cleared, outcome, $"{seq[0].ToDisplayText()} の繰り返しで勝てる");
                    if (outcome == TurnOutcome.GameOver) break;
                }
            }
        }

        [Test]
        public void EnemiesNeverActWithoutYourCommands([Values("StageM1", "StageM2", "StageM3")] string name)
        {
            // 敵は渡された命令でしか動かない：盤面が変わらない「待機手」なら、何ターン続けても誰も傷つかない
            // （＝敵同士が勝手に戦って終わることはない。逆に、永久待機は成立する。docs/research/MULTI_ENEMY_ANALYSIS.md 参照）
            var s = Stage(name);
            var model = BattleModel.FromStage(s);
            var stall = BattleSolver.Sequences(new BattleSolver.Settings { Cards = s.availableCommands, MaxSlots = 1 })
                .First(seq => { var m = model.Clone(); TurnRules.PlayTurn(m, seq, null); return BattleSolver.Key(m) == BattleSolver.Key(model); });
            string before = BattleSolver.Key(model);
            for (int t = 0; t < 20; t++) Assert.AreEqual(TurnOutcome.Continue, TurnRules.PlayTurn(model, stall, null));
            Assert.AreEqual(before, BattleSolver.Key(model));
        }
    }
}
