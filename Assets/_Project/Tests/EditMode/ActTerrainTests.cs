using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】2026-10-05 本人プレイ後の修正：カードの削除は1枚だけ・モーダル中は背面に入力を渡さない・あなたのカードの範囲プレビュー・
    /// 壁（通れない・直線の攻撃と矢を止める・大十字も止める）・回復マス（一度・敵も）・応急処置（敵もコピーで回復）。
    /// </summary>
    public class ActTerrainTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 0) => new CommandRecord(Card(card), d, dist);

        // ───── カードの削除は「その1枚」だけ ─────

        [Test]
        public void RemovingOneCardKeepsTheOtherCopies()
        {
            var run = new ActRun(ActContent.Roles[0], 7, Card);
            int slashes = run.Deck.Count(c => c.name == "Slash");
            Assert.GreaterOrEqual(slashes, 2, "最初のデッキは斬撃2枚（【RC8】押し斬り2枚）");
            int total = run.Deck.Count;
            int second = run.Deck.Select((c, i) => (c, i)).Where(x => x.c.name == "Slash").Skip(1).First().i;
            Assert.IsTrue(run.RestRemoveAt(second));
            Assert.AreEqual(slashes - 1, run.Deck.Count(c => c.name == "Slash"), "斬撃は1枚だけ減る");
            Assert.AreEqual(total - 1, run.Deck.Count);
            // 商人でも
            while (!run.Doors.Any(r => r.Kind == RoomKind.Shop)) { run.Enter(run.Doors[0]); run.Leave(); }
            run.Enter(run.Doors.First(r => r.Kind == RoomKind.Shop));
            run.AddShards(100);
            int walks = run.Deck.Count(c => c.name == "Walk");
            Assert.IsTrue(run.RemoveCardPaidAt(run.Deck.FindIndex(c => c.name == "Walk")));
            Assert.AreEqual(walks - 1, run.Deck.Count(c => c.name == "Walk"));
            Assert.IsFalse(run.RemoveCardPaidAt(99), "範囲外は何もしない");
            // セーブ／ロードしても枚数はそのまま
            Assert.IsTrue(ActSave.TryRestore(ActSave.ToJson(run), Card, out var back, out var error), error);
            Assert.AreEqual(run.Deck.Count, back.Deck.Count);
            Assert.AreEqual(slashes - 1, back.Deck.Count(c => c.name == "Slash"));
            Assert.AreEqual(walks - 1, back.Deck.Count(c => c.name == "Walk"));
        }

        // ───── モーダル ─────

        [Test]
        public void ModalBlocksClicksAndKeysButNotDrawing()
        {
            foreach (var t in new[] { EventType.MouseDown, EventType.MouseUp, EventType.MouseDrag, EventType.KeyDown, EventType.KeyUp, EventType.ScrollWheel })
            {
                Assert.IsTrue(ModalInput.Blocks(true, t), t.ToString());
                Assert.IsFalse(ModalInput.Blocks(false, t), "モーダルが無ければ通す");
            }
            Assert.IsFalse(ModalInput.Blocks(true, EventType.Repaint), "描画は止めない（後ろも見える）");
            Assert.IsFalse(ModalInput.Blocks(true, EventType.Layout));
        }

        // ───── あなたのカードの範囲プレビュー ─────

        [Test]
        public void PreviewShowsWhereYouCanGoAndHitFromThePlannedPosition()
        {
            var m = BattleModel.FromRows(new[] { ".....", "..E..", ".....", "..P#.", "....." }, 3, 12, 10);
            var walk = PlayerPreview.Compute(m, new List<CommandRecord>(), Card("Walk"));
            Assert.AreEqual(4, walk.Count);
            Assert.IsFalse(walk.First(o => o.Direction == Direction.Right).Moved, "右は壁：動けない");
            Assert.AreEqual(new Vector2Int(2, 2), walk.First(o => o.Direction == Direction.Up).Landing);
            // 1つ目の命令（一歩↑）の後の位置から、斬撃↑が敵に届く
            var planned = new List<CommandRecord> { R("Walk", Direction.Up, 1) };
            Assert.AreEqual(new Vector2Int(2, 2), PlayerPreview.PlannedPosition(m, planned));
            var slash = PlayerPreview.Compute(m, planned, Card("Slash"));
            var up = slash.First(o => o.Direction == Direction.Up);
            CollectionAssert.Contains(up.AttackCells, new Vector2Int(2, 3));
            Assert.AreEqual(1, up.Hits.Count, "斬撃↑は敵に当たる");
            Assert.AreEqual(m.Player.Position, new Vector2Int(2, 1), "プレビューは盤面を変えない");
            // 突き（2マス）は壁の向こうへ届かない
            var thrust = PlayerPreview.Compute(m, new List<CommandRecord>(), Card("Thrust")).First(o => o.Direction == Direction.Right);
            Assert.AreEqual(0, thrust.AttackCells.Count, "隣が壁なら突きは届かない");
            // 敵のコピーは見せない：プレビューで敵は動かない
            Assert.IsTrue(walk.All(o => o.ControlCells.Count == 0));
        }

        // ───── 壁 ─────

        [Test]
        public void WallsStopMovesCopiesAttacksAndArrows()
        {
            // 敵の上に壁：あなたの「一歩↑」をコピーしても敵は動けない → 間合いが詰まる
            var m = BattleModel.FromRows(new[] { ".#.", ".E.", "...", "...", ".P." }, 3, 12, 10);
            var e0 = m.Enemies.First().Position;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up, 1) }, null);
            Assert.AreEqual(e0, m.Enemies.First().Position, "壁で止まったコピー");
            Assert.AreEqual(new Vector2Int(1, 1), m.Player.Position);
            // 斬撃は壁の向こうへ届かない
            var w = BattleModel.FromRows(new[] { "P#E" }, 3, 12, 10);
            TurnRules.PlayTurn(w, new List<CommandRecord> { R("Thrust", Direction.Right) }, null);
            Assert.AreEqual(10, w.Enemies.First().Hp);
            // 矢は壁で止まる（弓兵が上を向いていても、間の壁に隠れれば当たらない）
            var a = BattleModel.FromRows(new[] { ".P.", ".#.", ".K." }, 3, 12, 10);
            a.Enemies.First().Facing = Direction.Up;
            TurnRules.PlayTurn(a, new List<CommandRecord> { R("Walk", Direction.Left, 1), R("Walk", Direction.Right, 1), R("Slash", Direction.Up) }, null);
            Assert.AreEqual(12, a.Player.Hp, "壁の陰に隠れると矢は当たらない");
        }

        [Test]
        public void BigBlastStopsAtWalls()
        {
            var m = BattleModel.FromRows(new[] { "..P..", "..#..", ".....", ".....", ".Q..." }, 3, 12, 40);
            var method = typeof(PhaseRules).GetMethod("BigCross", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cells = (List<Vector2Int>)method.Invoke(null, new object[] { m, new Vector2Int(2, 2) });
            Assert.IsFalse(cells.Contains(new Vector2Int(2, 3)), "壁のマスは含まない");
            Assert.IsFalse(cells.Contains(new Vector2Int(2, 4)), "壁の向こうへは広がらない");
            Assert.IsTrue(cells.Contains(new Vector2Int(2, 0)));
        }

        // ───── 回復マス・応急処置 ─────

        [Test]
        public void HealTileHealsTheFirstUnitOnceAndEnemiesCanTakeIt()
        {
            var m = BattleModel.FromRows(new[] { "h....", ".....", "P...E", "....h" }, 3, 12, 10);
            m.Player.TakeDamage(6);
            m.Enemies.First().TakeDamage(6);
            Assert.AreEqual(2, m.Environment.HealTiles.Count);
            var clone = m.Clone();
            // あなたが上へ2歩：左上の回復マス（あなたが +4）／敵もコピーで上へ → 敵は右下ではなく上へ動くので取れない
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Up, 2) }, null);
            Assert.AreEqual(10, m.Player.Hp, "回復マスで +4");
            Assert.AreEqual(1, m.Environment.HealTiles.Count, "使った回復マスは消える");
            Assert.AreEqual(2, clone.Environment.HealTiles.Count, "複製した盤面には残っている（試算が本物を変えない）");
            // 敵が回復マスを取る：あなたが下へ1歩 → 敵もコピーで下へ（右下の回復マス）
            TurnRules.PlayTurn(clone, new List<CommandRecord> { R("Walk", Direction.Down, 1) }, null);
            Assert.AreEqual(8, clone.Enemies.First().Hp, "敵も回復マスで +4");
            Assert.AreEqual(1, clone.Environment.HealTiles.Count);
        }

        [Test]
        public void FirstAidHealsYouAndTheEnemyCopiesIt()
        {
            var aid = Card("FirstAid");
            Assert.IsNotNull(aid);
            Assert.AreEqual(CommandType.Heal, aid.type);
            Assert.IsFalse(aid.UsesDirection);
            var m = BattleModel.FromRows(new[] { "P...E" }, 3, 12, 10);
            m.Player.TakeDamage(5);
            m.Enemies.First().TakeDamage(3);
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(aid, Direction.Up, 0) }, null);
            Assert.AreEqual(10, m.Player.Hp, "あなたは +3");
            Assert.IsTrue(aid.exhaust, "1戦で1回（消耗）");
            Assert.AreEqual(10, m.Enemies.First().Hp, "敵もコピーで回復（満タンまで）");
            CollectionAssert.Contains(ActContent.CardPool.Select(c => c.Asset).ToList(), "FirstAid");
        }
    }
}
