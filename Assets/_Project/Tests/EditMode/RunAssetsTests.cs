using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 実際の RUN データ（RunBattle1〜3、カード、Battle シーンの RunManager 設定）が仕様どおりか。
    /// </summary>
    public class RunAssetsTests
    {
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");

        [TestCase("RunBattle1", 12, 1)]
        [TestCase("RunBattle2", 14, 1)]
        [TestCase("RunBattle3", 16, 2)]
        public void RunBattleSettings(string name, int enemyHp, int innateRange)
        {
            var s = Stage(name);
            Assert.IsNotNull(s);
            Assert.AreEqual(3, s.maxSlots, "RUN は最大3命令");
            Assert.AreEqual(2, s.spikeDamage, "RUN のトゲは仮値 2");
            Assert.AreEqual(enemyHp, s.enemyMaxHp);
            Assert.IsNotNull(s.enemyInnateAction);
            Assert.AreEqual(innateRange, s.enemyInnateAction.range);
        }

        [Test]
        public void ThreeRunBattlesHaveDifferentBoards()
        {
            var maps = new[] { "RunBattle1", "RunBattle2", "RunBattle3" }.Select(n => Stage(n).map).ToList();
            Assert.AreEqual(3, maps.Distinct().Count());
        }

        [Test]
        public void TutorialStagesAreUnchanged()
        {
            Assert.AreEqual(2, Stage("Stage1").maxSlots);
            Assert.AreEqual(2, Stage("Stage2").maxSlots);
            Assert.AreEqual(5, Stage("Stage2").spikeDamage, "チュートリアルのトゲは 5 のまま");
        }

        [TestCase("Slash"), TestCase("Rush"), TestCase("Push"), TestCase("Thrust"), TestCase("Whirl"), TestCase("Pull"), TestCase("Trap")]
        public void CardsHaveDescriptionAndCopyNote(string name)
        {
            var c = Card(name);
            Assert.IsNotNull(c);
            Assert.IsFalse(string.IsNullOrEmpty(c.description));
            Assert.IsFalse(string.IsNullOrEmpty(c.copyNote), "カードには「敵にコピーされると…」を書く");
        }

        [Test]
        public void SceneRunManagerHasInitialDeckAndRewardPool()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Battle.unity");
            var run = Object.FindAnyObjectByType<RunManager>();
            Assert.IsNotNull(run);
            var deck = run.InitialDeck;
            Assert.AreEqual(8, deck.Count);
            Assert.AreEqual(3, deck.Count(c => c.type == CommandType.Slash));
            Assert.AreEqual(3, deck.Count(c => c.type == CommandType.Rush));
            Assert.AreEqual(2, deck.Count(c => c.type == CommandType.Push));
            Assert.IsFalse(deck.Any(c => c.type == CommandType.Walk), "歩くはデッキに入らない");
            Assert.AreEqual(3, run.RunBattles.Count);
            Assert.AreEqual(4, run.RestHeal);
            Assert.AreEqual(12, run.MaxHp);
        }
    }
}
