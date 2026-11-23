using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 一致テスト：実際のゲーム（TurnManager）と、解析用の進行（TurnRules / BattleSolver が使う）に、
    /// 同じランダムな操作を流して、毎ターンの位置・HP・ログが一致することを確かめる。
    /// これが通る限り、解析結果はゲーム本体と食い違わない。
    /// </summary>
    public class TurnRulesParityTests
    {
        static readonly string[] Maps =
        {
            ".......\n.^..^..\n.P..E..\n...^...\n.......",   // 敵1体・トゲ
            "E......\n..^....\n...P...\n....^..\n......E",   // 敵2体・トゲ
            ".......\n.EPE.^.\n.......",                    // 敵2体・左右
            "P......\n.......\n.EE.^..",                     // 敵2体・隣り合う
            ".###.\n.#^#.\n.#E#.\n..P..",                    // STAGE 3 形
        };

        [Test]
        public void RandomPlaysMatchTurnManager([Values(0, 1, 2, 3, 4)] int mapIndex, [Values(false, true)] bool innate)
        {
            var rng = new System.Random(1000 + mapIndex * 10 + (innate ? 1 : 0));
            for (int game = 0; game < 15; game++)
            {
                var b = new TestBattle(Maps[mapIndex], playerHp: 20, enemyHp: 10, innate: innate);
                var model = BattleModel.FromRows(Maps[mapIndex].Split('\n'), 5, 20, 10);
                var cards = new List<CommandData> { b.Walk, b.Rush, b.Slash, b.Push };
                var sequences = BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });
                for (int turn = 0; turn < 6 && b.Turn.State == BattleState.PlayerInput; turn++)
                {
                    var seq = sequences[rng.Next(sequences.Count)];
                    foreach (var r in seq) Assert.IsTrue(b.Turn.AddCommand(r.Clone()));
                    int logStart = b.Turn.Log.Count;
                    b.RunTurn();
                    var ruleLog = new List<string>();
                    var outcome = TurnRules.PlayTurn(model, seq, innate ? b.AroundSlash : null, null, ruleLog);

                    string where = $"盤面{mapIndex} 試合{game} ターン{turn + 1}";
                    var gameLog = b.Turn.Log.Skip(logStart).Where(l => !l.StartsWith("――") && !l.EndsWith("！ 勝利") && !l.EndsWith("… 敗北")).ToList();
                    CollectionAssert.AreEqual(gameLog, ruleLog, $"{where}：ログが違う");
                    for (int u = 0; u < model.Units.Count; u++)
                    {
                        Assert.AreEqual(b.Grid.Units[u].GridPosition, model.Units[u].Position, $"{where}：{model.Units[u].Name} の位置が違う");
                        Assert.AreEqual(b.Grid.Units[u].Hp, model.Units[u].Hp, $"{where}：{model.Units[u].Name} の HP が違う");
                    }
                    var expected = b.Turn.State == BattleState.Cleared ? TurnOutcome.Cleared
                                 : b.Turn.State == BattleState.GameOver ? TurnOutcome.GameOver : TurnOutcome.Continue;
                    Assert.AreEqual(expected, outcome, $"{where}：勝敗が違う");
                }
                b.Destroy();
            }
        }

        [Test]
        public void CSharpSolverMatchesPythonResultsForStage2And3()
        {
            // Python 版（tools/level_solver）で出していた結果と同じになるか
            var stage2 = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/Stage2.asset");
            var r2 = BattleSolver.Solve(BattleModel.FromStage(stage2), new BattleSolver.Settings { Cards = stage2.availableCommands, MaxSlots = 2, MaxTurns = 6 });
            Assert.AreEqual(2, r2.Turns, "STAGE 2：最短2ターン");
            Assert.AreEqual(12, r2.PlayerHp, "STAGE 2：無傷");

            var stage3 = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/Stage3.asset");
            var r3 = BattleSolver.Solve(BattleModel.FromStage(stage3), new BattleSolver.Settings { Cards = stage3.availableCommands, MaxSlots = 3, MaxTurns = 5 });
            Assert.AreEqual(2, r3.Turns, "STAGE 3：最短2ターン");
            var moves = BattleSolver.Solve(BattleModel.FromStage(stage3), new BattleSolver.Settings
            {
                Cards = stage3.availableCommands, MaxSlots = 3, MaxTurns = 5,
                OnlyTypes = new HashSet<CommandType> { CommandType.Walk, CommandType.Rush },
            });
            Assert.AreEqual(4, moves.Turns, "STAGE 3：移動カードだけだと4ターン");
        }
    }
}
