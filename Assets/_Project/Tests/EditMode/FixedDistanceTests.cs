using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 2026-10-03 本人決定：移動距離を自由選択にしない。一歩＝1、駆け足＝2、突進＝3（固定）。
    /// プレイヤーが選ぶのは方向だけ。敵は同じカード・方向・固定距離をそのまま再現する。
    /// </summary>
    public class FixedDistanceTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");

        [TestCase("Walk", "一歩", 1)]
        [TestCase("Dash", "駆け足", 2)]
        [TestCase("Rush", "突進", 3)]
        public void MoveCardsHaveFixedDistance(string asset, string displayName, int distance)
        {
            var c = Card(asset);
            Assert.IsNotNull(c, asset);
            Assert.AreEqual(displayName, c.displayName);
            Assert.AreEqual(distance, c.minDistance);
            Assert.AreEqual(distance, c.maxDistance);
            Assert.IsFalse(c.NeedsDistanceChoice, "距離はプレイヤーが選ばない");
        }

        [TestCase("Stage1"), TestCase("Stage2"), TestCase("Stage3"), TestCase("Sandbox_AllCommands")]
        public void NoCardInStagesAsksForDistance(string stageName)
        {
            var stage = Stage(stageName);
            Assert.IsNotNull(stage, stageName);
            foreach (var c in stage.availableCommands)
                Assert.IsFalse(c.NeedsDistanceChoice, $"{stageName} の {c.displayName} が距離を選ぶカードになっている");
        }

        [Test]
        public void EnemyCopiesTheFixedDistance()
        {
            // 突進（3マス固定）→ 右。自分は壁で1マスしか進めないが、敵には「突進 →右 3マス」が渡り、敵は3マス進む
            var b = new TestBattle("P.#....\n.......\nE......");
            var rush = ScriptableObject.CreateInstance<CommandData>();
            rush.displayName = "突進"; rush.type = CommandType.Rush; rush.minDistance = 3; rush.maxDistance = 3;
            b.Stage.availableCommands.Add(rush);
            Assert.IsTrue(b.Turn.AddCommand(new CommandRecord(rush, Direction.Right, rush.minDistance)));
            b.RunTurn();
            Assert.AreEqual(new Vector2Int(1, 2), b.Player.GridPosition);
            Assert.AreEqual(3, b.Turn.EnemyReplay.CopiedCommands[0].Distance);
            Assert.AreEqual(new Vector2Int(3, 0), b.Enemy.GridPosition);
            Assert.AreEqual("突進 →右 3マス", b.Turn.EnemyReplay.CopiedCommands[0].ToDisplayText());
            b.Destroy();
            Object.DestroyImmediate(rush);
        }

        [Test]
        public void TutorialStageTextsUseIppo()
        {
            foreach (var name in new[] { "Stage1", "Stage2" })
            {
                var s = Stage(name);
                StringAssert.DoesNotContain("歩く", s.tutorialText);
                StringAssert.DoesNotContain("歩く", s.hintAfterFirstTurn);
            }
        }
    }
}
