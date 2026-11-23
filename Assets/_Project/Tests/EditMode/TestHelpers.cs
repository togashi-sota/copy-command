using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>テスト用の戦闘を組み立てる。</summary>
    public class TestBattle
    {
        public readonly GameObject Root;
        public readonly GridManager Grid;
        public readonly TurnManager Turn;
        public readonly StageData Stage;
        public readonly CommandData Walk;
        public readonly CommandData Rush;
        public readonly CommandData Slash;
        public readonly CommandData Push;
        public readonly CommandData AroundSlash;
        readonly List<Object> created = new List<Object>();

        public UnitController Player => Turn.Player;
        public UnitController Enemy => Turn.Enemy;

        /// <param name="innate">true なら敵に固有行動「周囲斬り（4）」を持たせる</param>
        public TestBattle(string map, int maxSlots = 3, int playerHp = 12, int enemyHp = 12, bool innate = false)
        {
            Root = new GameObject("TestBattle");
            Grid = Root.AddComponent<GridManager>();
            Turn = Root.AddComponent<TurnManager>();

            Walk = MakeCommand("歩く", CommandType.Walk, 1, 1, 0);
            Rush = MakeCommand("突進", CommandType.Rush, 2, 3, 0);
            Slash = MakeCommand("斬撃", CommandType.Slash, 0, 0, 4);
            Push = MakeCommand("押し出し", CommandType.Push, 0, 0, 0);
            AroundSlash = MakeCommand("周囲斬り", CommandType.AroundSlash, 0, 0, 4);

            Stage = ScriptableObject.CreateInstance<StageData>();
            created.Add(Stage);
            Stage.map = map;
            Stage.maxSlots = maxSlots;
            Stage.playerMaxHp = playerHp;
            Stage.enemyMaxHp = enemyHp;
            Stage.spikeDamage = 5;
            Stage.availableCommands = new List<CommandData> { Walk, Rush, Slash, Push };
            Stage.enemyInnateAction = innate ? AroundSlash : null;

            Turn.SetupForTest(Grid, new List<StageData> { Stage });
            Turn.LoadStage(0);
        }

        CommandData MakeCommand(string name, CommandType type, int min, int max, int damage)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            c.displayName = name;
            c.type = type;
            c.minDistance = min;
            c.maxDistance = max;
            c.damage = damage;
            created.Add(c);
            return c;
        }

        /// <summary>命令を登録する。登録できたら true。</summary>
        public bool Add(CommandData command, Direction dir, int distance = 1) =>
            Turn.AddCommand(new CommandRecord(command, dir, distance));

        /// <summary>登録済みの命令で1ターン（PLAYER → ENEMY）を待ち時間なしで進める。</summary>
        public void RunTurn() => Turn.ExecuteTurnImmediately();

        public void Destroy()
        {
            Object.DestroyImmediate(Root);
            foreach (var o in created) Object.DestroyImmediate(o);
        }
    }
}
