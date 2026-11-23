using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 実際の STAGE 2 データ（Assets/_Project/Data/Stages/Stage2.asset）が、狙いどおりに遊べるか。
    /// 「3ターン無傷ルート」と「2ターン速攻ルート」の両方が成立し続けることを守る（唯一解のパズルにしない）。
    /// 盤面や数値を Inspector で変えて、このテストが落ちたら「想定ルートが崩れた」という意味。
    /// </summary>
    public class Stage2Tests
    {
        GameObject root;
        TurnManager turn;
        StageData stage;

        [SetUp]
        public void SetUp()
        {
            stage = AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/Stage2.asset");
            Assert.IsNotNull(stage, "Stage2.asset が見つからない");
            root = new GameObject("Stage2Test");
            var grid = root.AddComponent<GridManager>();
            turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData> { stage });
            turn.LoadStage(0);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        CommandData Find(CommandType type) => stage.availableCommands.Find(c => c.type == type);

        void Play(params CommandRecord[] commands)
        {
            foreach (var c in commands) Assert.IsTrue(turn.AddCommand(c), $"登録できない: {c.ToDisplayText()}");
            turn.ExecuteTurnImmediately();
        }

        CommandRecord Walk(Direction d) => new CommandRecord(Find(CommandType.Walk), d, 1);
        // 突進は3マス固定（2026-10-03 本人決定）。距離はカードのデータから取る
        CommandRecord Rush(Direction d) { var c = Find(CommandType.Rush); return new CommandRecord(c, d, c.minDistance); }
        CommandRecord Slash(Direction d) => new CommandRecord(Find(CommandType.Slash), d, 0);

        [Test]
        public void Stage2SettingsMatchSpec()
        {
            Assert.AreEqual(2, stage.maxSlots);
            CollectionAssert.AreEquivalent(new[] { CommandType.Walk, CommandType.Rush, CommandType.Slash },
                stage.availableCommands.ConvertAll(c => c.type));
            Assert.IsNull(stage.enemyInnateAction, "STAGE 1〜3 は敵の固有行動なし（2026-10-03）");
            Assert.AreEqual(12, stage.playerMaxHp);
            Assert.AreEqual(12, stage.enemyMaxHp);
            Assert.AreEqual(5, stage.spikeDamage);
        }

        [Test]
        public void Stage2LayoutMatchesProposal1()
        {
            Assert.AreEqual(new Vector2Int(2, 0), turn.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(2, 2), turn.Enemy.GridPosition);
            Assert.IsNotNull(turn.Grid.GetHazardAt(new Vector2Int(0, 2)));
            Assert.IsNotNull(turn.Grid.GetHazardAt(new Vector2Int(4, 1)));
            Assert.AreEqual(5, turn.Grid.Width);
            Assert.AreEqual(5, turn.Grid.Height);
        }

        [Test]
        public void CleanRouteThreeTurnsNoDamage()
        {
            // T1 歩く↓：自分は端で動けない／敵だけ1マス下へ（端のズレ）
            Play(Walk(Direction.Down));
            Assert.AreEqual(new Vector2Int(2, 0), turn.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(2, 1), turn.Enemy.GridPosition);
            Assert.AreEqual(12, turn.Player.Hp);

            // T2 突進→（3マス固定。盤面の端で2マスで止まる）/ 一歩←：敵は (4,1) のトゲへ
            Play(Rush(Direction.Right), Walk(Direction.Left));
            Assert.AreEqual(new Vector2Int(3, 0), turn.Player.GridPosition);
            Assert.AreEqual(new Vector2Int(3, 1), turn.Enemy.GridPosition);
            Assert.AreEqual(7, turn.Enemy.Hp);
            Assert.AreEqual(12, turn.Player.Hp);

            // T3 斬撃↑ / 歩く→：斬って逃げる。逃げる1歩のコピーで敵がトゲへ → 撃破
            Play(Slash(Direction.Up), Walk(Direction.Right));
            Assert.AreEqual(BattleState.Cleared, turn.State);
            Assert.AreEqual(3, turn.TurnNumber);
            Assert.AreEqual(12, turn.Player.Hp);
            Assert.AreEqual(new Vector2Int(4, 1), turn.Enemy.GridPosition, "敵は自分からトゲへ入って倒れた");
        }

        [Test]
        public void FastRouteTwoTurns()
        {
            // T1 突進←（端で止まる）/ 一歩↓：敵は (0,2) のトゲを踏み、端でずれて真上に来る
            Play(Rush(Direction.Left), Walk(Direction.Down));
            Assert.AreEqual(7, turn.Enemy.Hp);
            Assert.AreEqual(12, turn.Player.Hp);

            // T2 一歩↑ / 斬撃↑：斬撃が命中 → コピーの一歩↑で敵がもう一度トゲへ
            Play(Walk(Direction.Up), Slash(Direction.Up));
            Assert.AreEqual(BattleState.Cleared, turn.State);
            Assert.AreEqual(2, turn.TurnNumber);
            Assert.AreEqual(12, turn.Player.Hp, "固有行動が無いので無傷");
        }

        [Test]
        public void Turn1RushMakesEnemyRunIntoSpike()
        {
            // STAGE 2 のアハ体験：あなたの「突進←（3マス）」を敵も使い、敵だけが (0,2) のトゲへ突っ込む
            Play(Rush(Direction.Left));
            Assert.AreEqual(12, turn.Player.Hp, "あなたの通り道にトゲは無い");
            Assert.AreEqual(new Vector2Int(0, 2), turn.Enemy.GridPosition);
            Assert.AreEqual(7, turn.Enemy.Hp);
        }
    }
}
