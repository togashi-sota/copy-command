using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>クリア後の「次のステージへ」。</summary>
    public class StageProgressTests
    {
        [Test]
        public void NextStageSkipsTestStages()
        {
            var root = new GameObject("T");
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var s1 = ScriptableObject.CreateInstance<StageData>();
            var s2 = ScriptableObject.CreateInstance<StageData>();
            var test = ScriptableObject.CreateInstance<StageData>();
            test.isTestStage = true;
            s1.map = s2.map = test.map = "P.E";
            turn.SetupForTest(grid, new List<StageData> { s1, s2, test });

            turn.LoadStage(0);
            Assert.IsTrue(turn.HasNextStage);
            turn.LoadStage(1);
            Assert.IsFalse(turn.HasNextStage, "次がテスト用ステージなら進まない");

            Object.DestroyImmediate(root);
            Object.DestroyImmediate(s1);
            Object.DestroyImmediate(s2);
            Object.DestroyImmediate(test);
        }
    }
}
