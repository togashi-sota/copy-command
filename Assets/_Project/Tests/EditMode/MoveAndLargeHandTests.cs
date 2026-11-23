using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】新しい移動札（跳躍・斜め歩・滑走・転移・入れ替え・可変突進）と、大きい手札の方式（初手8・上限10）。
    /// どの移動も、敵が同じ命令を自分の位置からコピーする（ズレが出る）ことも確かめる。
    /// </summary>
    public class MoveAndLargeHandTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        CommandData Make(string name, CommandType type, int min = 0, int max = 0, int damage = 0)
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

        static BattleModel Rows(string map) => BattleModel.FromRows(map.Split('\n'), 5, 12, 8);

        static List<CommandRecord> One(CommandData c, Direction d, int dist = 0) => new List<CommandRecord> { new CommandRecord(c, d, dist) };

        // ───────── 跳躍・転移 ─────────

        [Test]
        public void JumpLeapsOverUnitsSpikesAndWalls()
        {
            var jump = Make("跳躍", CommandType.Jump, 2, 2);
            var m = Rows("P^...\n.....\n....E");
            TurnRules.PlayCommandsAndCopies(m, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(2, 2), m.Player.Position);
            Assert.AreEqual(12, m.Player.Hp, "間のトゲは踏まない");

            var overUnit = Rows("PE...\n.....\n.....");
            TurnRules.PlayCommandsAndCopies(overUnit, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(2, 2), overUnit.Player.Position, "敵を飛び越える");
            Assert.AreEqual(new Vector2Int(3, 2), overUnit.Enemies.First().Position, "敵もコピーで2マス跳ぶ");

            var wall = Rows("P#...\n.....\n....E");
            TurnRules.PlayCommandsAndCopies(wall, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(2, 2), wall.Player.Position, "間の壁・柱は越える（2026-10-05 本人プレイ）");
            var landOnWall = Rows("P.#..\n.....\n....E");
            TurnRules.PlayCommandsAndCopies(landOnWall, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(0, 2), landOnWall.Player.Position, "着地点が壁なら跳べない");
            var edge = Rows(".....\n.....\n...EP");
            TurnRules.PlayCommandsAndCopies(edge, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(4, 0), edge.Player.Position, "盤面の外には跳べない");

            // 2026-10-05 RC：着地点のトゲ・回復マスは効く／間の回復マスは使わない／敵のコピーも同じ（壁を越える）
            var spikeLand = Rows("P.^..\n.....\n....E");
            TurnRules.PlayCommandsAndCopies(spikeLand, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(7, spikeLand.Player.Hp, "着地点のトゲは踏む");
            var healLand = Rows("P.h..\n.....\n....E");
            healLand.Player.TakeDamage(6);
            TurnRules.PlayCommandsAndCopies(healLand, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(10, healLand.Player.Hp, "着地点の回復マスで +4");
            Assert.AreEqual(0, healLand.Environment.HealTiles.Count);
            var healMid = Rows("Ph...\n.....\n....E");
            healMid.Player.TakeDamage(6);
            TurnRules.PlayCommandsAndCopies(healMid, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(6, healMid.Player.Hp, "間の回復マスは使わない");
            Assert.AreEqual(1, healMid.Environment.HealTiles.Count);
            var enemyWall = Rows("P....\n.....\nE#...");
            TurnRules.PlayCommandsAndCopies(enemyWall, One(jump, Direction.Right, 2), null);
            Assert.AreEqual(new Vector2Int(2, 0), enemyWall.Enemies.First().Position, "敵のコピーも間の壁を越える");
        }

        [Test]
        public void BlinkGoesThroughWallsAndFailsOnBlockedLanding()
        {
            var blink = Make("転移", CommandType.Blink, 3, 3);
            var m = Rows("P##..\n.....\n....E");
            TurnRules.PlayCommandsAndCopies(m, One(blink, Direction.Right, 3), null);
            Assert.AreEqual(new Vector2Int(3, 2), m.Player.Position, "壁の向こうへ");
            var blocked = Rows("P..E.\n.....\n.....");
            TurnRules.PlayCommandsAndCopies(blocked, One(blink, Direction.Right, 3), null);
            Assert.AreEqual(new Vector2Int(0, 2), blocked.Player.Position, "着地点がふさがっていれば失敗");
        }

        // ───────── 【検証専用】壁蹴り・踏み越え（中央固定の対策案。デッキには入れない）─────────

        [Test]
        public void WallKickMovesThreeOnlyWithTheBackToAWall()
        {
            var kick = Make("壁蹴り", CommandType.WallKick);
            var m = Rows("P....E.\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(m, One(kick, Direction.Right), null);
            Assert.AreEqual(new Vector2Int(3, 2), m.Player.Position, "背中が盤面の端 → 3マス");
            Assert.AreEqual(new Vector2Int(6, 2), m.Enemies.First().Position, "背中が空いている敵は1マスだけ");

            var center = Rows(".......\n...P...\n......E");
            TurnRules.PlayCommandsAndCopies(center, One(kick, Direction.Left), null);
            Assert.AreEqual(new Vector2Int(2, 1), center.Player.Position, "中央では1マスだけ");
            Assert.AreEqual(new Vector2Int(3, 0), center.Enemies.First().Position, "右端を背にした敵は3マス");
        }

        [Test]
        public void VaultNeedsAUnitInFrontAndIsCopiedOverYou()
        {
            var vault = Make("踏み越え", CommandType.Vault);
            var m = Rows("PE.....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(m, One(vault, Direction.Right), null);
            Assert.AreEqual(new Vector2Int(2, 2), m.Player.Position, "敵を越えて2マス先へ");
            Assert.AreEqual(new Vector2Int(3, 2), m.Enemies.First().Position, "敵のコピー：正面のあなたを越えて前に出る");

            var empty = Rows("P.E....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(empty, One(vault, Direction.Right), null);
            Assert.AreEqual(new Vector2Int(0, 2), empty.Player.Position, "正面に誰もいなければ空振り");
            Assert.AreEqual(new Vector2Int(2, 2), empty.Enemies.First().Position);
        }

        // ───────── 【検証中】新しい攻撃札（押し斬り・薙ぎ払い・貫通・闘気） ─────────

        [Test]
        public void PushSlashDamagesThenPushes()
        {
            var c = Make("押し斬り", CommandType.PushSlash, damage: 3);
            var m = Rows("PE.....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(m, One(c, Direction.Right), null);
            var e = m.Enemies.First();
            Assert.AreEqual(5, e.Hp, "3ダメージ");
            Assert.AreEqual(new Vector2Int(2, 2), e.Position, "1マス押した（敵のコピーは正面に誰もいないので空振り）");

            var wall = Rows("PE#....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(wall, One(c, Direction.Right), null);
            Assert.AreEqual(5, wall.Enemies.First().Hp);
            Assert.AreEqual(new Vector2Int(1, 2), wall.Enemies.First().Position, "押し先が壁ならダメージだけ");
        }

        [Test]
        public void SweepHitsFrontAndBothSides()
        {
            var c = Make("薙ぎ払い", CommandType.Sweep, damage: 2);
            var m = Rows(".E.\nP..\n.E.");
            TurnRules.PlayCommandsAndCopies(m, One(c, Direction.Right), null);
            Assert.IsTrue(m.Enemies.All(e => e.Hp == 6), "正面の左右の2体に2ずつ");
            Assert.AreEqual(12, m.Player.Hp, "敵のコピーの薙ぎ払いは誰にも当たらない");
        }

        [Test]
        public void PierceHitsEveryoneInLineAndTheCopyHitsTheEnemyBehind()
        {
            var c = Make("貫通", CommandType.Pierce, damage: 2);
            c.range = 3;
            var m = Rows("PEE....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(m, One(c, Direction.Right), null);
            var es = m.Enemies.ToList();
            Assert.AreEqual(6, es[0].Hp, "あなたの貫通：前の敵に2");
            Assert.AreEqual(4, es[1].Hp, "あなたの貫通で2＋前の敵のコピーの貫通で2（同士討ち）");
        }

        [Test]
        public void SpiritBoostsTheNextAttackAndTheCopyIsBoostedToo()
        {
            var spirit = Make("闘気", CommandType.Spirit);
            spirit.range = 3;
            var slash = Make("斬撃", CommandType.Slash, damage: 4);
            slash.range = 1;
            // あなた：闘気 → 斬撃← （左は盤面の端で空振り＝ボーナスは使い切る）。敵のコピー：闘気 → 斬撃← が、あなたに 4＋3
            var m = Rows("PE.....\n.......\n.......");
            var seq = new List<CommandRecord> { new CommandRecord(spirit, Direction.Up, 0), new CommandRecord(slash, Direction.Left, 0) };
            TurnRules.PlayCommandsAndCopies(m, seq, null);
            Assert.AreEqual(5, m.Player.Hp, "敵も闘気で強くなる（-7）");
            Assert.AreEqual(0, m.Player.AttackBonus, "ボーナスは命令列が終わると消える");
            Assert.AreEqual(0, m.Enemies.First().AttackBonus);

            var hit = Rows("EP.....\n.......\n.......");
            TurnRules.PlayCommandsAndCopies(hit, seq, null);
            Assert.AreEqual(1, hit.Enemies.First().Hp, "あなたの斬撃 4＋3");
        }

        // ───────── 斜め歩 ─────────

        [Test]
        public void DiagonalStepsClockwiseAndIsCopied()
        {
            var diag = Make("斜め歩", CommandType.Diagonal, 1, 1);
            var m = Rows(".....\n.....\nP...E");
            TurnRules.PlayCommandsAndCopies(m, One(diag, Direction.Up, 1), null);
            Assert.AreEqual(new Vector2Int(1, 1), m.Player.Position, "↑＝右上");
            Assert.AreEqual(new Vector2Int(4, 0), m.Enemies.First().Position, "敵は右端で斜めに動けない（ズレ）");
            Assert.AreEqual("斜め歩 ↗右上", new CommandRecord(diag, Direction.Up, 1).ToDisplayText());
        }

        // ───────── 滑走 ─────────

        [Test]
        public void SlideStopsBeforeObstaclesAndHitsEverySpike()
        {
            var slide = Make("滑走", CommandType.Slide);
            var m = Rows("P.^.#\n.....\nE^^..");
            TurnRules.PlayCommandsAndCopies(m, One(slide, Direction.Right), null);
            Assert.AreEqual(new Vector2Int(3, 2), m.Player.Position, "壁の手前で止まる");
            Assert.AreEqual(7, m.Player.Hp, "通ったトゲで5");
            Assert.AreEqual(0, m.Enemies.First().Hp, "敵は同じ滑走でトゲを2つ通って倒れる（5+5）＝止まる位置もダメージも敵ごとに違う");
            Assert.AreEqual(new Vector2Int(2, 0), m.Enemies.First().Position, "倒れたところで止まる");
        }

        // ───────── 入れ替え ─────────

        [Test]
        public void SwapExchangesPlacesAndTheCopyCanMoveYou()
        {
            var swap = Make("入れ替え", CommandType.Swap);
            // 隣同士だと：あなたが敵と入れ替わる → 敵のコピー「入れ替え←」で、敵の左にいるあなたとまた入れ替わる＝元に戻る
            var m = Rows("EP...\n.....\n.....");
            TurnRules.PlayCommandsAndCopies(m, One(swap, Direction.Left), null);
            Assert.AreEqual(new Vector2Int(1, 2), m.Player.Position, "敵のコピーで、あなたの位置が戻される");
            Assert.AreEqual(new Vector2Int(0, 2), m.Enemies.First().Position);

            // 入れ替えてから下へ逃げれば、敵のコピーの入れ替えは空振り（あなたはもう隣にいない）
            var walk = Make("一歩", CommandType.Walk, 1, 1);
            var escape = Rows("EP...\n.....\n.....");
            TurnRules.PlayCommandsAndCopies(escape, new List<CommandRecord> { new CommandRecord(swap, Direction.Left, 0), new CommandRecord(walk, Direction.Down, 1) }, null);
            Assert.AreEqual(new Vector2Int(0, 1), escape.Player.Position, "入れ替え → 下へ。コピーで戻されない");
        }

        [Test]
        public void SwapTriggersSpikesForBoth()
        {
            var swap = Make("入れ替え", CommandType.Swap);
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = "PE...\n.....";
            var m = BattleModel.FromStage(s);
            m.Board.AddSpike(new Vector2Int(1, 1)); // 敵のいるマス
            TurnRules.PlayCommandsAndCopies(m, One(swap, Direction.Right), null);
            Assert.AreEqual(7, m.Player.Hp, "敵のいたマス（トゲ）に入る");
        }

        // ───────── 可変突進 ─────────

        [Test]
        public void VariableRushCopiesTheChosenDistance()
        {
            var vr = Make("可変突進", CommandType.Rush, 2, 3);
            var m = Rows("P....E.....\n...........");
            TurnRules.PlayCommandsAndCopies(m, One(vr, Direction.Right, 3), null);
            Assert.AreEqual(new Vector2Int(3, 1), m.Player.Position);
            Assert.AreEqual(new Vector2Int(8, 1), m.Enemies.First().Position, "敵は選ばれた3マスをそのまま");
        }

        // ───────── 大きい手札 ─────────

        DeckState LargeDeck(DrawPolicy policy, int cards = 30)
        {
            var c = Make("斬撃", CommandType.Slash, damage: 4);
            var d = new DeckState(new System.Random(1), handSize: DeckState.LargeOpening, policy: policy);
            d.StartBattle(Enumerable.Repeat(c, cards));
            return d;
        }

        [Test]
        public void MidDrawThreeOpensWithSevenAndCapsAtNine()
        {
            var c = Make("斬撃", CommandType.Slash, damage: 4);
            var d = new DeckState(new System.Random(1), handSize: DeckState.MidOpening, policy: DrawPolicy.MidDrawThree);
            d.StartBattle(Enumerable.Repeat(c, 20));
            Assert.AreEqual(7, d.Hand.Count);
            Assert.AreEqual(9, d.HandLimit);
            d.UseCards(d.Hand.Take(3).ToList());
            d.StartTurnDraw();
            Assert.AreEqual(7, d.Hand.Count, "3枚使って3枚引く");
            d.StartTurnDraw();
            Assert.AreEqual(9, d.Hand.Count, "上限9");
            Assert.AreEqual(1, d.LastSkippedDraws);
        }

        [Test]
        public void LargeDrawThreeOpensWithEightAndCapsAtTen()
        {
            var d = LargeDeck(DrawPolicy.LargeDrawThree);
            Assert.AreEqual(8, d.Hand.Count);
            Assert.AreEqual(10, d.HandLimit);
            d.UseCards(d.Hand.Take(3).ToList());
            d.StartTurnDraw();
            Assert.AreEqual(8, d.Hand.Count, "3枚使って3枚引く");
            d.StartTurnDraw();
            Assert.AreEqual(10, d.Hand.Count, "上限10");
            Assert.AreEqual(1, d.LastSkippedDraws);
        }

        [Test]
        public void LargeUsedPlusOneDrawsUsedCountPlusOne()
        {
            var d = LargeDeck(DrawPolicy.LargeUsedPlusOne);
            d.UseCards(d.Hand.Take(3).ToList());
            d.StartTurnDraw();
            Assert.AreEqual(9, d.Hand.Count, "3枚使うと4枚引く（手札は1枚増える）");
            d.UseCards(d.Hand.Take(1).ToList());
            d.StartTurnDraw();
            Assert.AreEqual(10, d.Hand.Count, "1枚使うと2枚引く");
        }

        // ───────── 実際のデータ（Setup で作るカードと M4 以降のステージ）─────────

        static CommandData Asset(string name) => UnityEditor.AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData StageAsset(string name) => UnityEditor.AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");

        static readonly string[] NewStages = { "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageMB1", "StageM11", "StageM12", "StageV1", "StageV2", "StageV3", "StageV4", "StageV5", "StageV6", "StageVB", "StageVS", "StageA1", "StageA2", "StageA3", "StageA4" };

        [Test]
        public void NewStagesLoadTheTwentyCardDeckInLargeModes([ValueSource(nameof(NewStages))] string name,
            [Values(HandMode.DeckLarge3, HandMode.DeckLargeUsed, HandMode.DeckMid3, HandMode.DeckLarge12)] HandMode mode)
        {
            var stage = StageAsset(name);
            // 2026-10-04 作り直した検証デッキ：移動9・攻撃8（直接攻撃7＋闘気）・位置操作3
            var deck = stage.handTestDeckLarge;
            Assert.AreEqual(20, deck.Count);
            Assert.AreEqual(9, deck.Count(c => UiStyles.CategoryOf(c) == UiStyles.CardCategory.Move), "移動札9枚");
            Assert.AreEqual(7, deck.Count(c => UiStyles.CategoryOf(c) == UiStyles.CardCategory.Attack), "直接攻撃7枚");
            Assert.AreEqual(1, deck.Count(c => UiStyles.CategoryOf(c) == UiStyles.CardCategory.Buff), "闘気1枚");
            Assert.AreEqual(3, deck.Count(c => UiStyles.CategoryOf(c) == UiStyles.CardCategory.Support), "位置操作3枚");
            var root = new GameObject("LargeDeck");
            var turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(root.AddComponent<GridManager>(), new List<StageData> { stage });
            turn.CurrentHandMode = mode;
            turn.LoadStage(0);
            bool mid = mode == HandMode.DeckMid3;
            Assert.AreEqual(mid ? 7 : 8, turn.Deck.Hand.Count);
            Assert.AreEqual(mid ? 9 : mode == HandMode.DeckLarge12 ? 12 : 10, turn.Deck.HandLimit);
            Assert.AreEqual(20, turn.Deck.Hand.Count + turn.Deck.DrawPile.Count);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void NewMovesGameAndRulesAgree([Values("StageM4", "StageM7", "StageM8", "StageM10", "StageMB1", "StageM11", "StageM12", "StageV1", "StageV2", "StageV4", "StageV6", "StageVB")] string name)
        {
            // 新しい移動札6種＋斬撃・押し出しを固定の手札にしたステージで、ゲーム（TurnManager）と解析（TurnRules）を同じ手順で動かし、毎ターン一致すること
            var src = StageAsset(name);
            var stage = Object.Instantiate(src);
            created.Add(stage);
            stage.availableCommands = new List<CommandData>
            {
                Asset("Jump"), Asset("Diagonal"), Asset("Slide"), Asset("Blink"), Asset("Swap"), Asset("VarRush"), Asset("Slash"), Asset("Push"),
            };
            var seqs = BattleSolver.Sequences(new BattleSolver.Settings { Cards = stage.availableCommands, MaxSlots = 3 });
            var rng = new System.Random(name.Length * 977);
            for (int game = 0; game < 8; game++)
            {
                var root = new GameObject("Parity");
                var grid = root.AddComponent<GridManager>();
                var turn = root.AddComponent<TurnManager>();
                turn.SetupForTest(grid, new List<StageData> { stage });
                turn.LoadStage(0);
                var model = BattleModel.FromStage(stage);
                for (int t = 0; t < 8 && turn.State == BattleState.PlayerInput; t++)
                {
                    var seq = seqs[rng.Next(seqs.Count)];
                    foreach (var r in seq) Assert.IsTrue(turn.AddCommand(r.Clone()), r.ToDisplayText());
                    turn.ExecuteTurnImmediately();
                    var outcome = TurnRules.PlayTurn(model, seq, null);
                    string where = $"{name} 試合{game} ターン{t + 1}（{string.Join(" / ", seq.Select(c => c.ToDisplayText()))}）";
                    for (int u = 0; u < model.Units.Count; u++)
                    {
                        Assert.AreEqual(model.Units[u].Position, grid.Model.Units[u].Position, $"{where}：{model.Units[u].Name} の位置");
                        Assert.AreEqual(model.Units[u].Hp, grid.Model.Units[u].Hp, $"{where}：{model.Units[u].Name} の HP");
                        Assert.AreEqual(model.Units[u].Facing, grid.Model.Units[u].Facing, $"{where}：{model.Units[u].Name} の向き");
                    }
                    Assert.AreEqual(model.Environment.StateKey(), grid.Model.Environment.StateKey(), $"{where}：環境");
                    var expected = turn.State == BattleState.Cleared ? TurnOutcome.Cleared : turn.State == BattleState.GameOver ? TurnOutcome.GameOver : TurnOutcome.Continue;
                    Assert.AreEqual(expected, outcome, $"{where}：勝敗");
                }
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GameResetsResearchExperimentsOnLoad()
        {
            // 研究用の実験の切替（解析だけで使う）が ON のままでも、ゲームはステージを読み込むと今のルールに戻す
            RulesEngine.ExperimentBlinkMode = 2;
            RulesEngine.ExperimentSlideMode = 1;
            RulesEngine.ExperimentJumpMode = 1;
            RulesEngine.ExperimentLeapLandsShort = true;
            TurnRules.ExperimentSkipWhiffedMoves = true;
            var stage = StageAsset("StageM4");
            var root = new GameObject("Reset");
            var turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(root.AddComponent<GridManager>(), new List<StageData> { stage });
            turn.LoadStage(0);
            Assert.AreEqual(0, RulesEngine.ExperimentBlinkMode);
            Assert.AreEqual(0, RulesEngine.ExperimentSlideMode);
            Assert.AreEqual(0, RulesEngine.ExperimentJumpMode);
            Assert.IsFalse(RulesEngine.ExperimentLeapLandsShort);
            Assert.IsFalse(TurnRules.ExperimentSkipWhiffedMoves);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void NewMoveCardsHaveCopyNotes([Values("Jump", "Diagonal", "Slide", "Blink", "Swap", "VarRush")] string name)
        {
            var c = Asset(name);
            Assert.IsNotNull(c);
            Assert.IsFalse(string.IsNullOrEmpty(c.description));
            Assert.IsFalse(string.IsNullOrEmpty(c.copyNote), "敵にコピーされたときの説明");
        }

        [Test]
        public void LargeModesUseTheLargeTestDeck()
        {
            var b = new TestBattle(".....\n.P.E.\n.....");
            var big = Enumerable.Repeat(b.Walk, 12).Concat(Enumerable.Repeat(b.Slash, 8)).ToList();
            b.Stage.handTestDeckLarge = big;
            b.Turn.CurrentHandMode = HandMode.DeckLarge3;
            b.Turn.LoadStage(0);
            Assert.AreEqual(8, b.Turn.Deck.Hand.Count);
            Assert.AreEqual(20, b.Turn.Deck.Hand.Count + b.Turn.Deck.DrawPile.Count);
            b.Turn.CurrentHandMode = HandMode.DeckB;
            b.Turn.LoadStage(0);
            Assert.AreEqual(5, b.Turn.Deck.Hand.Count, "A/B は今までどおり");
            b.Destroy();
        }
    }
}
