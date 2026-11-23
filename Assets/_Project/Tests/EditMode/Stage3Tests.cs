using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 実際の STAGE 3 データ（Assets/_Project/Data/Stages/Stage3.asset）が狙いどおりか。
    /// 狙い：何のカードを・何枚・どの順番で使うか、その命令を敵に使われたらどうなるか。
    /// </summary>
    public class Stage3Tests
    {
        GameObject root;
        TurnManager turn;
        StageData stage;

        [SetUp]
        public void SetUp()
        {
            stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/Stage3.asset");
            Assert.IsNotNull(stage, "Stage3.asset が見つからない");
            root = new GameObject("Stage3Test");
            var grid = root.AddComponent<GridManager>();
            turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData> { stage });
            turn.LoadStage(0);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        CommandData Find(CommandType type) => stage.availableCommands.Find(c => c.type == type);
        CommandRecord Walk(Direction d) => new CommandRecord(Find(CommandType.Walk), d, 1);
        CommandRecord Dash(Direction d)
        {
            var c = stage.availableCommands.Find(x => x.type == CommandType.Rush && x.minDistance == 2);
            return new CommandRecord(c, d, c.minDistance);
        }
        CommandRecord Slash(Direction d) => new CommandRecord(Find(CommandType.Slash), d, 0);
        CommandRecord Push(Direction d) => new CommandRecord(Find(CommandType.Push), d, 0);

        void Play(params CommandRecord[] commands)
        {
            foreach (var c in commands) Assert.IsTrue(turn.AddCommand(c), $"登録できない: {c.ToDisplayText()}");
            turn.ExecuteTurnImmediately();
        }

        [Test]
        public void Stage3Settings()
        {
            Assert.AreEqual(3, stage.maxSlots);
            Assert.IsNull(stage.enemyInnateAction, "STAGE 1〜3 は固有行動なし");
            Assert.AreEqual(18, stage.enemyMaxHp);
            Assert.AreEqual(5, stage.spikeDamage);
            var names = stage.availableCommands.ConvertAll(c => c.displayName);
            CollectionAssert.AreEquivalent(new[] { "一歩", "駆け足", "斬撃", "押し出し" }, names, "STAGE 3 は突進なし");
        }

        [Test]
        public void Stage3Layout()
        {
            Assert.AreEqual(new Vector2Int(2, 0), turn.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(2, 1), turn.Enemy.GridPosition);
            Assert.IsNotNull(turn.Grid.GetHazardAt(new Vector2Int(2, 2)), "敵の真上にトゲ");
            Assert.IsTrue(turn.Grid.IsWall(new Vector2Int(2, 3)), "トゲの上は壁");
            Assert.IsTrue(turn.Grid.IsWall(new Vector2Int(1, 1)) && turn.Grid.IsWall(new Vector2Int(3, 1)), "敵は小部屋の中");
        }

        [Test]
        public void IntendedRouteSlashPushThenStepDownKillsInTwoTurns()
        {
            // ① 斬撃↑ → 押し出し↑ → 一歩↓：斬撃4＋トゲ5。一歩↓はあなたには空振り（下は端）だが、敵がコピーしてトゲから下りる
            Play(Slash(Direction.Up), Push(Direction.Up), Walk(Direction.Down));
            Assert.AreEqual(9, turn.Enemy.Hp);
            Assert.AreEqual(new Vector2Int(2, 1), turn.Enemy.GridPosition, "敵は元の位置に戻ってくる");
            Assert.AreEqual(new Vector2Int(2, 0), turn.Player.GridPosition);

            // ② 斬撃↑ → 押し出し↑：もう一度 9 → 撃破
            Play(Slash(Direction.Up), Push(Direction.Up));
            Assert.AreEqual(BattleState.Cleared, turn.State);
            Assert.AreEqual(2, turn.TurnNumber);
            Assert.AreEqual(12, turn.Player.Hp);
        }

        [Test]
        public void ReverseOrderPushThenSlashMissesTheSlash()
        {
            Play(Push(Direction.Up), Slash(Direction.Up), Walk(Direction.Down));
            Assert.AreEqual(13, turn.Enemy.Hp, "押し出しで敵が離れ、斬撃は空振り（トゲの5だけ）");
        }

        [Test]
        public void WithoutTheThirdCommandEnemyStaysOnTheSpike()
        {
            // 2命令だけだと敵はトゲの上に残り、2ターン目に同じコンボができない
            Play(Slash(Direction.Up), Push(Direction.Up));
            Assert.AreEqual(9, turn.Enemy.Hp);
            Assert.AreEqual(new Vector2Int(2, 2), turn.Enemy.GridPosition);
            Play(Walk(Direction.Up), Slash(Direction.Up));
            Assert.AreEqual(BattleState.PlayerInput, turn.State, "2ターンでは倒しきれない");
            Assert.AreEqual(5, turn.Enemy.Hp);
        }

        [Test]
        public void AlternativeCopyTrickRouteAlsoTwoTurns()
        {
            // 別解：あなたは動けない移動（上は敵・下は端）を敵にだけ使わせ、トゲに入れて戻す
            Play(Walk(Direction.Up), Dash(Direction.Down), Slash(Direction.Up));
            Assert.AreEqual(9, turn.Enemy.Hp);
            Play(Walk(Direction.Up), Slash(Direction.Up));
            Assert.AreEqual(BattleState.Cleared, turn.State);
            Assert.AreEqual(2, turn.TurnNumber);
        }
    }
}
