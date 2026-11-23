using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 挙動の記録テスト（ゴールデンテスト）。
    /// 決まった操作を実行し、「ログの文章・全ユニットの位置・HP」を1ターンごとに記録して、保存済みの記録と一字一句比べる。
    /// 構造整理（リファクタリング）の前後で、ゲームの挙動がまったく変わっていないことを確かめるためのもの。
    /// 記録ファイル：Assets/_Project/Tests/EditMode/Golden/*.txt（無ければ今の結果で作る）
    /// </summary>
    public class GoldenTraceTests
    {
        static string GoldenDir => Path.Combine(Application.dataPath, "_Project/Tests/EditMode/Golden");

        static string Trace(TestBattle b, List<Func<TestBattle, CommandRecord[]>> turns)
        {
            var sb = new StringBuilder();
            void Snapshot(string label)
            {
                sb.Append(label).Append(" | state=").Append(b.Turn.State).Append(" turn=").Append(b.Turn.TurnNumber);
                foreach (var u in b.Grid.Units) sb.Append(" | ").Append(u.DisplayName).Append(u.GridPosition).Append(" hp=").Append(u.Hp);
                sb.AppendLine();
            }
            Snapshot("start");
            int logStart = b.Turn.Log.Count;
            for (int t = 0; t < turns.Count; t++)
            {
                if (b.Turn.State != BattleState.PlayerInput) break;
                foreach (var r in turns[t](b)) sb.Append("add ").Append(r.ToDisplayText()).Append(" -> ").AppendLine(b.Turn.AddCommand(r).ToString());
                b.RunTurn();
                for (int i = logStart; i < b.Turn.Log.Count; i++) sb.Append("  log: ").AppendLine(b.Turn.Log[i]);
                logStart = b.Turn.Log.Count;
                Snapshot($"after T{t + 1}");
            }
            return sb.ToString();
        }

        static void Check(string name, string actual)
        {
            Directory.CreateDirectory(GoldenDir);
            string path = Path.Combine(GoldenDir, name + ".txt");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, actual, Encoding.UTF8);
                Assert.Inconclusive($"記録ファイルを作成しました: {name}.txt（次回から比較します）");
            }
            string expected = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
            Assert.AreEqual(expected, actual.Replace("\r\n", "\n"), $"{name} の挙動が記録と違う");
        }

        static CommandRecord R(CommandData c, Direction d, int dist = -1) => new CommandRecord(c, d, dist < 0 ? Math.Max(c.minDistance, 0) : dist);

        [Test]
        public void MovesSpikesAndCopies()
        {
            var b = new TestBattle(".......\n.^..^..\n.P..E..\n...^...\n.......", playerHp: 30, enemyHp: 30);
            var turns = new List<Func<TestBattle, CommandRecord[]>>
            {
                t => new[] { R(t.Walk, Direction.Up), R(t.Rush, Direction.Right, 3), R(t.Slash, Direction.Down) },
                t => new[] { R(t.Rush, Direction.Left, 2), R(t.Walk, Direction.Down) },
                t => new[] { R(t.Push, Direction.Right), R(t.Walk, Direction.Right), R(t.Slash, Direction.Right) },
                t => new[] { R(t.Rush, Direction.Down, 3), R(t.Push, Direction.Up) },
                t => new[] { R(t.Slash, Direction.Left), R(t.Walk, Direction.Left), R(t.Rush, Direction.Up, 2) },
            };
            Check("moves_spikes_copies", Trace(b, turns));
            b.Destroy();
        }

        [Test]
        public void PushIntoSpikeAndKill()
        {
            var b = new TestBattle(".....\n.PE^.\n.....", enemyHp: 9);
            var turns = new List<Func<TestBattle, CommandRecord[]>>
            {
                t => new[] { R(t.Slash, Direction.Right), R(t.Push, Direction.Right), R(t.Walk, Direction.Up) },
                t => new[] { R(t.Walk, Direction.Down), R(t.Rush, Direction.Right, 2) },
            };
            Check("push_spike_kill", Trace(b, turns));
            b.Destroy();
        }

        [Test]
        public void InnateAndHitAndAway()
        {
            var b = new TestBattle("......\n..PE..\n......", innate: true, playerHp: 20, enemyHp: 20);
            var turns = new List<Func<TestBattle, CommandRecord[]>>
            {
                t => new[] { R(t.Slash, Direction.Right), R(t.Walk, Direction.Left) },
                t => new[] { R(t.Walk, Direction.Right) },
                t => new[] { R(t.Push, Direction.Right), R(t.Slash, Direction.Up) },
                t => new[] { R(t.Rush, Direction.Right, 3), R(t.Slash, Direction.Right) },
            };
            Check("innate_hit_away", Trace(b, turns));
            b.Destroy();
        }

        [Test]
        public void SpecialCards()
        {
            var b = new TestBattle(".......\n.......\nP...E..\n.......\n.......", playerHp: 30, enemyHp: 30);
            var thrust = ScriptableObject.CreateInstance<CommandData>(); thrust.displayName = "突き"; thrust.type = CommandType.Slash; thrust.damage = 3; thrust.range = 2;
            var whirl = ScriptableObject.CreateInstance<CommandData>(); whirl.displayName = "回転斬り"; whirl.type = CommandType.AroundSlash; whirl.damage = 3; whirl.range = 1;
            var pull = ScriptableObject.CreateInstance<CommandData>(); pull.displayName = "引き寄せ"; pull.type = CommandType.Pull; pull.range = 3;
            var trap = ScriptableObject.CreateInstance<CommandData>(); trap.displayName = "罠設置"; trap.type = CommandType.PlaceSpike;
            b.Stage.availableCommands.AddRange(new[] { thrust, whirl, pull, trap });
            var turns = new List<Func<TestBattle, CommandRecord[]>>
            {
                t => new[] { R(pull, Direction.Right, 0), R(thrust, Direction.Right, 0), R(trap, Direction.Up, 0) },
                t => new[] { R(t.Walk, Direction.Right), R(whirl, Direction.Up, 0), R(trap, Direction.Down, 0) },
                t => new[] { R(pull, Direction.Right, 0), R(t.Walk, Direction.Up), R(thrust, Direction.Right, 0) },
            };
            Check("special_cards", Trace(b, turns));
            b.Destroy();
            foreach (var o in new UnityEngine.Object[] { thrust, whirl, pull, trap }) UnityEngine.Object.DestroyImmediate(o);
        }

        [Test]
        public void DeckModeRetainAndReshuffle()
        {
            var b = new TestBattle(".....\n.P.E.\n.....", maxSlots: 3, playerHp: 20, enemyHp: 20);
            var walk = ScriptableObject.CreateInstance<CommandData>(); walk.displayName = "歩く"; walk.type = CommandType.Walk; walk.minDistance = 1; walk.maxDistance = 1;
            var deck = new List<CommandData> { b.Slash, b.Slash, b.Rush, b.Push, b.Slash, b.Rush, b.Push, b.Slash };
            b.Turn.LoadRunBattle(b.Stage, 20, 20, deck, walk, new System.Random(42));
            var sb = new StringBuilder();
            for (int t = 0; t < 4 && b.Turn.State == BattleState.PlayerInput; t++)
            {
                sb.Append($"T{t + 1} hand:");
                foreach (var c in b.Turn.Deck.Hand) sb.Append(' ').Append(c.Data.displayName);
                sb.Append($" draw={b.Turn.Deck.DrawPile.Count} discard={b.Turn.Deck.DiscardPile.Count}").AppendLine();
                var hand = new List<CardInstance>(b.Turn.Deck.Hand);
                b.Turn.AddCommand(new CommandRecord(walk, Direction.Right, 1));
                if (hand.Count > 0) b.Turn.AddCommand(new CommandRecord(hand[0].Data, Direction.Right, Math.Max(hand[0].Data.minDistance, 0), hand[0]));
                if (hand.Count > 2) b.Turn.ToggleRetain(hand[2]);
                b.RunTurn();
                foreach (var u in b.Grid.Units) sb.Append($"  {u.DisplayName}{u.GridPosition} hp={u.Hp}").AppendLine();
            }
            Check("deck_mode", sb.ToString());
            b.Destroy();
            UnityEngine.Object.DestroyImmediate(walk);
        }
    }
}
