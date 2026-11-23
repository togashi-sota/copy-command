using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【Puzzle v2（2026-10-06）】パズル中心 ACT1 v2：敵の攻撃範囲の常時表示・部屋の目標（封印解除・搬送・同期・脱出）・
    /// 封印石・石像・運び像・はじいた理由・手札の上限なし・引く札・やり直し。
    /// </summary>
    public class PuzzleV2Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 1) => new CommandRecord(Card(card), d, dist);
        static BattleModel Board(params string[] rows) => BattleModel.FromRows(rows, 3, 20, 8);
        static UnitModel En(BattleModel m, int i) => m.Enemies.ElementAt(i);

        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            BattleUI.FullPrediction = false;
        }

        StageData NewStage(string map, RoomGoal goal = RoomGoal.Defeat)
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map;
            s.maxSlots = 3;
            s.playerMaxHp = 20;
            s.enemyMaxHp = 8;
            s.spikeDamage = 3;
            s.goal = goal;
            s.availableCommands = new List<CommandData>();
            return s;
        }

        CommandData NewCard(string name, CommandType type, int range = 1, DrawFilter filter = DrawFilter.Any, int planDraw = 0)
        {
            var c = ScriptableObject.CreateInstance<CommandData>();
            created.Add(c);
            c.name = name; c.displayName = name; c.type = type; c.range = range; c.drawFilter = filter; c.planDraw = planDraw;
            return c;
        }

        TurnManager Battle(StageData stage, List<CommandData> deck, out GridManager grid, int seed = 3)
        {
            var root = new GameObject("PuzzleBattle");
            created.Add(root);
            grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            turn.SetupForTest(grid, new List<StageData> { stage });
            turn.LoadActBattle(stage, new ActBattleSetup { Hp = 20, MaxHp = 20, Deck = deck, Rng = new System.Random(seed), Seed = seed });
            return turn;
        }

        // ───── 1. 敵の攻撃範囲：カードを選ぶ前から必ず見える（本人プレイの回帰） ─────

        [Test]
        public void EnemyRangesAreShownBeforeAnyCardAndStayWhilePlanning()
        {
            var stage = NewStage(".......\n...S...\n.......\n.Y.....\n...P...\n.......");
            stage.enemyFacings = "DR";
            var deck = Enumerable.Repeat(Card("Walk"), 6).Concat(Enumerable.Repeat(Card("Slash"), 6)).ToList();
            var turn = Battle(stage, deck, out var grid);
            var ui = turn.gameObject.AddComponent<BattleUI>();
            ui.SetupForTest(turn);
            var model = grid.Model;

            ui.RefreshForecastForTest();
            var start = grid.MarkCells("arrow").ToList();
            Assert.IsNotEmpty(start, "カードを選ぶ前（ターンの始め）から、剣兵・槍兵の範囲が赤く出る");
            CollectionAssert.AreEquivalent(ThreatMap.EnemyAttack(model), start);
            CollectionAssert.Contains(start, new Vector2Int(3, 3), "剣兵の下");
            CollectionAssert.Contains(start, new Vector2Int(2, 2), "槍兵の右");

            var walk = turn.Deck.Hand.First(c => c.Data.type == CommandType.Walk);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(walk.Data, Direction.Up, 1, walk)));
            ui.RefreshForecastForTest();
            CollectionAssert.AreEquivalent(start, grid.MarkCells("arrow"), "命令を組んでも消えない・変わらない（コピーの後は予測しない）");

            BattleUI.FullPrediction = true;
            ui.RefreshForecastForTest();
            CollectionAssert.AreEquivalent(start, grid.MarkCells("arrow"), "完全予測でも、今の範囲は消えない（予測は別の薄い層）");
            BattleUI.FullPrediction = false;

            turn.ClearCommands();
            ui.RefreshForecastForTest();
            CollectionAssert.AreEquivalent(start, grid.MarkCells("arrow"), "取り消しても消えない");

            Assert.IsTrue(turn.AddCommand(new CommandRecord(walk.Data, Direction.Left, 1, walk)));
            turn.ExecuteTurnImmediately();
            ui.RefreshForecastForTest();
            var next = grid.MarkCells("arrow").ToList();
            CollectionAssert.AreEquivalent(ThreatMap.EnemyAttack(model), next, "ラウンドが変わったら、動いた後の位置・向きで作り直す");
            CollectionAssert.AreNotEquivalent(start, next);
        }

        [Test]
        public void BossSlamAndPillarAreTelegraphedInPurple()
        {
            var m = Board(".......", "...L...", ".......", "...P...");
            var lord = En(m, 0);
            lord.SlamCd = 1;
            lord.SlamCells = new List<Vector2Int> { new Vector2Int(2, 2), new Vector2Int(4, 2) };
            lord.BombCells = new List<Vector2Int> { new Vector2Int(3, 0) };
            var tele = ThreatMap.Telegraph(m);
            CollectionAssert.IsSubsetOf(new[] { new Vector2Int(2, 2), new Vector2Int(4, 2), new Vector2Int(3, 0) }, tele, "ボスの大剣・光柱は紫の予告");
            CollectionAssert.DoesNotContain(ThreatMap.EnemyAttack(m), new Vector2Int(3, 0), "ボスは赤の層には入れない（層を分ける）");
        }

        // ───── 2. 部屋の目標 ─────

        [Test]
        public void CarryGoalWinsWhenAGolemStandsOnTheMarkWithoutKillingAnyone()
        {
            // 運び像（移動まね）を運び紋へ。あなたは柱で止まり、運び像だけ上へ2マス（壁ズレ）
            var m = Board("..v....", ".......", "..D.#..", "....P..", "E......");
            m.Goal = RoomGoal.Carry;
            var golem = m.Enemies.First(e => e.Kind == EnemyKind.Golem);
            Assert.AreEqual(CopyKind.Move, golem.CopyKind, "運び像は移動まね");
            var outcome = TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Up, 2) }, null);
            Assert.AreEqual(new Vector2Int(4, 1), m.Player.Position, "柱で止まる");
            Assert.AreEqual(new Vector2Int(2, 4), golem.Position);
            Assert.AreEqual(TurnOutcome.Cleared, outcome, "敵を倒さなくても、運び紋に立てば勝ち");
        }

        [Test]
        public void ObjectGoalsDoNotWinByKillingEveryone()
        {
            var m = Board("..x....", "...s...", ".......", "...PE..");
            m.Goal = RoomGoal.Escape;
            En(m, 0).Hp = 0;
            Assert.AreEqual(TurnOutcome.Continue, TurnRules.Check(m), "脱出の部屋は、全滅しても目標を達成しないと勝ちにならない");
            m.Goal = RoomGoal.Defeat;
            Assert.AreEqual(TurnOutcome.Cleared, TurnRules.Check(m));
        }

        [Test]
        public void SealStonesBreakOnlyFromCopiesAndOpenTheExit()
        {
            // 写し砲（技まね）があなたの斬撃をまねて、上の封印石を撃つ。あなたの斬撃は封印石をはじく
            var m = Board("x.s....", ".......", "..Z....", "...s...", "...P...");
            m.Goal = RoomGoal.Escape;
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Up) }, null, events);
            Assert.IsTrue(m.Board.IsSealStone(new Vector2Int(3, 1)), "あなたの斬撃でははじく");
            var why = events.FirstOrDefault(e => e.Type == BattleEventType.Deflected && e.Text != null);
            Assert.IsNotNull(why, "はじいた理由が出る");
            StringAssert.Contains("まね攻撃", why.Text);
            Assert.IsFalse(m.Board.IsSealStone(new Vector2Int(2, 4)), "写し砲のまねで割れる");
            Assert.IsFalse(PuzzleRules.ExitOpen(m.Board), "まだ1つ残っている");
        }

        [Test]
        public void StatueGazeHitsTheFirstUnitAndCopyHitsTurnIt()
        {
            // 石像（→向き）が右の列を撃つ。反響鎧兵にも効く（仕掛け）
            var m = Board("i..R...", ".......", "..E....", "...P...");
            m.Board.SetStatueFacing(new Vector2Int(0, 3), Direction.Right);
            var echo = m.Enemies.First(e => e.Kind == EnemyKind.EchoKnight);
            var normal = m.Enemies.First(e => e.Kind == EnemyKind.Normal);
            CollectionAssert.AreEqual(new[] { new Vector2Int(1, 3), new Vector2Int(2, 3), new Vector2Int(3, 3) }, PuzzleRules.GazeLine(m, new Vector2Int(0, 3)), "最初のユニットまで");
            CollectionAssert.IsSubsetOf(PuzzleRules.HazardCells(m), ThreatMap.Telegraph(m), "視線は紫の予告にいつも出る");
            PuzzleRules.StatuePhase(m, new List<BattleEvent>(), null);
            Assert.AreEqual(8 - PuzzleRules.StatueDamage, echo.Hp, "反響鎧兵にも効く");

            // あなたが左を斬っても回らない／敵（全まね）が左を斬る → 石像に当たって回る
            var m2 = Board(".......", "i......", "E......", "...P...");
            m2.Board.SetStatueFacing(new Vector2Int(0, 2), Direction.Right);
            TurnRules.PlayTurn(m2, new List<CommandRecord> { R("Slash", Direction.Up) }, null);
            Assert.AreEqual(Direction.Down, m2.Board.StatueFacing(new Vector2Int(0, 2)), "敵のまねた斬撃（上）が石像に当たって時計回り：→ から ↓");
        }

        [Test]
        public void GolemAndCannonAreToolsThatCannotDie()
        {
            var m = Board("..D....", "..P....", ".......");
            var golem = En(m, 0);
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Up) }, null, events);
            Assert.AreEqual(golem.MaxHp, golem.Hp, "運び像は倒れない");
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Deflected && e.Text != null), "理由が出る");
            Assert.IsTrue(m.AllEnemiesDead, "道具は数えない");
        }

        [Test]
        public void SyncGoalNeedsAllCoresInTheSameRound()
        {
            var m = Board("c.....c", ".......", "......E", "P......");
            m.Goal = RoomGoal.Sync;
            // あなたは↑へ駆け足で核の下へ、敵も↑（核の下で止まる）→ 斬撃↑で両方の核
            var o = TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Up, 2), R("Slash", Direction.Up) }, null);
            Assert.AreEqual(TurnOutcome.Cleared, o, "あなたとまねで同じラウンドに2つ");
        }

        [Test]
        public void HowToHurtExplainsEchoArmorAndProgressShowsTheGoal()
        {
            var m = Board("R..E...", ".......", "c.....c", "...P...");
            var how = PuzzleRules.HowToHurt(m, En(m, 0));
            Assert.IsNotNull(how);
            Assert.IsFalse(how.Value.direct, "直接 ×");
            Assert.IsTrue(how.Value.copy, "まね ○");
            Assert.IsNull(PuzzleRules.HowToHurt(m, En(m, 1)), "普通の敵は印なし");
            m.Goal = RoomGoal.Sync;
            StringAssert.Contains("同じターン", PuzzleRules.GoalSentence(m));
            Assert.AreEqual(2, PuzzleRules.Progress(m).Count(i => i.Text.StartsWith("鏡核")));
        }

        // ───── 3. 手札：7枚まで補充・上限なし・引く札 ─────

        [Test]
        public void HandHasNoHardCapAndDrawCardsWorkImmediately()
        {
            var draw2 = NewCard("手繰り寄せ", CommandType.Draw, 2);
            var deck = new List<CommandData> { draw2, draw2 };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 10));
            deck.AddRange(Enumerable.Repeat(Card("Slash"), 10));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            while (!turn.Deck.Hand.Any(c => c.Data == draw2)) Assert.IsTrue(turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.Last(c => c.Data == draw2)));
            int before = turn.Deck.Hand.Count;
            var card = turn.Deck.Hand.First(c => c.Data == draw2);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(draw2, Direction.Up, 1, card)));
            Assert.AreEqual(before + 2, turn.Deck.Hand.Count, "命令に入れた瞬間に2枚（このターンに使える）");
            Assert.AreEqual(1, turn.LockedCommands);
            turn.RemoveLastCommand();
            Assert.AreEqual(1, turn.PlannedCommands.Count, "引いた札の前は取り消せない");
            turn.DrawExtra(30);
            Assert.AreEqual(deck.Count, turn.Deck.Hand.Count + turn.Deck.DrawPile.Count + turn.Deck.DiscardPile.Count, "札は増えも減りもしない");
            Assert.Greater(turn.Deck.Hand.Count, 15, "上限 15 は無くなった（デッキの枚数まで）");
        }

        [Test]
        public void FilteredDrawTakesOnlyMoveCardsAndReshufflesIfNeeded()
        {
            var deckState = new DeckState(new System.Random(1), 7, policy: DrawPolicy.RefillSeven);
            var cards = Enumerable.Repeat(Card("Slash"), 8).Concat(Enumerable.Repeat(Card("Walk"), 3)).ToList();
            deckState.StartBattle(cards);
            var got = deckState.DrawMatching(2, c => CopyKinds.IsMoveType(c.type));
            Assert.IsTrue(got.All(c => c.Data.type == CommandType.Walk), "足取り：移動の札だけ");
            deckState.UseCards(deckState.Hand.Where(c => c.Data.type == CommandType.Walk).ToList());
            int discardWalks = deckState.DiscardPile.Count(c => c.Data.type == CommandType.Walk);
            var more = deckState.DrawMatching(5, c => CopyKinds.IsMoveType(c.type));
            Assert.LessOrEqual(more.Count, 3, "あるだけ（無ければ止まる）");
            Assert.IsTrue(more.All(c => c.Data.type == CommandType.Walk));
            Assert.GreaterOrEqual(deckState.ReshuffleCount + (discardWalks == 0 ? 1 : 0), 1, "山札に無ければ捨て札を混ぜて探す");
        }

        [Test]
        public void RallyWithDrawGivesSlotsAndCardsTogether()
        {
            var rally = NewCard("号令・改", CommandType.Rally, 2, planDraw: 2);
            var deck = new List<CommandData> { rally };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 14));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            if (!turn.Deck.Hand.Any(c => c.Data == rally)) turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.First(c => c.Data == rally));
            int before = turn.Deck.Hand.Count;
            var card = turn.Deck.Hand.First(c => c.Data == rally);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(rally, Direction.Up, 1, card)));
            Assert.AreEqual(5, turn.MaxSlots, "命令 +2（この札も1枠）");
            Assert.AreEqual(before + 2, turn.Deck.Hand.Count, "＋2枚（枠だけ増えて札が無い、を防ぐ）");
        }

        [Test]
        public void InsightOpensThePickerNow()
        {
            var insight = NewCard("深読み", CommandType.Insight);
            var deck = new List<CommandData> { insight };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 12));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            if (!turn.Deck.Hand.Any(c => c.Data == insight)) turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.First(c => c.Data == insight));
            var card = turn.Deck.Hand.First(c => c.Data == insight);
            turn.AddCommand(new CommandRecord(insight, Direction.Up, 1, card));
            Assert.AreEqual(1, turn.PendingForesight, "すぐ選べる");
            Assert.Greater(turn.ForesightChoices.Count, 0);
        }

        // ───── 4. はじいた理由・やり直し ─────

        [Test]
        public void DirectHitOnEchoKnightExplainsWhy()
        {
            var deck = Enumerable.Repeat(Card("Slash"), 8).Concat(Enumerable.Repeat(Card("Walk"), 4)).ToList();
            var turn = Battle(NewStage(".......\n...R...\n...P...\n.E....."), deck, out _);
            var slash = turn.Deck.Hand.First(c => c.Data.type == CommandType.Slash);
            turn.AddCommand(new CommandRecord(slash.Data, Direction.Up, 1, slash));
            turn.ExecuteTurnImmediately();
            Assert.IsNotNull(turn.LastHint);
            StringAssert.Contains("まね攻撃", turn.LastHint, "間違えた瞬間に理由（チュートリアル）");
        }

        [Test]
        public void ShopRemovalTakesExactlyOneCopy()
        {
            var run = new ActRun(ActContent.Roles[0], 5, Card);
            run.AddShards(100);
            int slashes = run.Deck.Count(c => c.name == "Slash");
            Assert.Greater(slashes, 1);
            Assert.IsTrue(run.RemoveCardPaidAt(run.Deck.FindIndex(c => c.name == "Slash")));
            Assert.AreEqual(slashes - 1, run.Deck.Count(c => c.name == "Slash"), "同じ名前の札を全部消さない（選んだ1枚だけ）");
        }
        // ───── 5. ボス「封紋の守護者」：移動 → 攻撃 → 同期。成功で -8 と窓 ─────

        static readonly string[] BossRows = { ".......", ".c.#.c.", "o....A.", "...U...", ".A.....", ".#.o.#.", "...P..." };

        BattleModel BossModel()
        {
            var m = BattleModel.FromRows(BossRows, 3, 20, 8);
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.SealLord);
            lord.OverrideMaxHp(32);
            return m;
        }

        [Test]
        public void SealLordGoesMoveAttackSyncAndSuccessIsDamage()
        {
            var m = BossModel();
            var lord = m.Enemies.First();
            Assert.AreEqual(EnemyKind.SealLord, lord.Kind, "実行順は主が先（敵A）");
            Assert.AreEqual(32, lord.Hp);
            Assert.IsTrue(m.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue).All(e => e.Dormant), "剣の像は段階2まで眠っている");
            var ev = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, lord, 4, DamageKind.Direct, ev);
            Assert.AreEqual(32, lord.Hp, "あなたの直接攻撃は守りにはじかれる");
            StringAssert.Contains("仕掛け", ev.First(e => e.Type == BattleEventType.Deflected).Text);

            // 段階1：駆け足↓（あなたは端で止まり、主だけ光る紋へ）
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Down, 2) }, null);
            Assert.AreEqual(new Vector2Int(3, 1), lord.Position);
            Assert.AreEqual(24, lord.Hp, "成功＝-8");
            Assert.AreEqual(0, lord.Barrier, "窓：守りが無い");
            Assert.IsTrue(CopyCore.Stunned(lord), "窓の間はまねない");
            // 窓で斬撃↑：4 → 20 で境目 → 段階2（主は中央へ帰る）
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Up) }, null);
            Assert.AreEqual(20, lord.Hp);
            Assert.AreEqual(1, lord.BossStage);
            Assert.AreEqual(1, lord.Barrier);
            Assert.AreEqual(new Vector2Int(3, 3), lord.Position, "中央へ帰る");
            Assert.IsTrue(m.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue).All(e => !e.Dormant), "段階2で剣の像が光る（ラウンドの終わり）");

            // 段階2：一歩↑・突き←（主が上へ。右の像が突き←をまねて主を突く）
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up), R("Thrust", Direction.Left) }, null);
            Assert.AreEqual(12, lord.Hp, "像のまね攻撃＝成功 -8");
            Assert.AreEqual(1, m.Enemies.Count(e => e.Kind == EnemyKind.BladeStatue && e.BossStage > 0), "像は1回で光が消える");
            Assert.AreEqual(0, lord.Barrier);
        }

        [Test]
        public void CoresNeedAtLeastOneCopyHit()
        {
            var m = Board("c.c....", ".P.....", ".......");
            m.Board.HitCore(new Vector2Int(0, 2), false);
            Assert.IsFalse(m.Board.HitCore(new Vector2Int(2, 2), false), "あなた1人で両方叩いても割れない（研究：4命令の穴）");
            Assert.IsTrue(m.Board.Cores.Any());
            m.Board.ResetCoreHits();
            m.Board.HitCore(new Vector2Int(0, 2), false);
            Assert.IsTrue(m.Board.HitCore(new Vector2Int(2, 2), true), "1つ以上がまねなら割れる");
        }

        [Test]
        public void LordGoesNextToHomeWhenYouStandOnIt()
        {
            var m = BossModel();
            var lord = m.Enemies.First();
            lord.Position = new Vector2Int(0, 0);
            m.Player.Position = lord.Home;
            PhaseRules.GoHome(m, lord, null);
            Assert.AreEqual(1, Mathf.Abs(lord.Position.x - lord.Home.x) + Mathf.Abs(lord.Position.y - lord.Home.y), "中央がふさがっていたら隣の空きマスへ（前は帰らなかった）");
        }

        [Test]
        public void StoneHandTelegraphsYourCellThenFalls()
        {
            var m = BossModel();
            var lord = m.Enemies.First();
            PuzzleBoss.StoneHand(m, lord, new List<BattleEvent>());
            CollectionAssert.AreEqual(new[] { m.Player.Position }, lord.BombCells, "段階1は1マス");
            CollectionAssert.Contains(ThreatMap.Telegraph(m), m.Player.Position, "紫の予告");
            int hp = m.Player.Hp;
            PuzzleBoss.StoneHand(m, lord, new List<BattleEvent>());
            Assert.AreEqual(hp - PuzzleBoss.HandDamage, m.Player.Hp, "次のラウンドに落ちる");
            Assert.IsEmpty(lord.BombCells);
        }

        // ───── 6. 手を回す札（研究の推し） ─────

        [Test]
        public void FootworkAndRallyDrawTheRightKinds()
        {
            var footwork = NewCard("足取り", CommandType.Draw, 2, DrawFilter.Move);
            var rally = NewCard("号令・改", CommandType.Rally, 2, DrawFilter.MoveAndTechnique, planDraw: 2);
            var deck = new List<CommandData> { footwork, rally };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 6));
            deck.AddRange(Enumerable.Repeat(Card("Slash"), 8));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            void Ensure(CommandData d) { if (!turn.Deck.Hand.Any(c => c.Data == d)) Assert.IsTrue(turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.First(c => c.Data == d))); }
            Ensure(footwork); Ensure(rally);
            int moves = turn.Deck.Hand.Count(c => c.Data.type == CommandType.Walk);
            var f = turn.Deck.Hand.First(c => c.Data == footwork);
            turn.AddCommand(new CommandRecord(footwork, Direction.Up, 1, f));
            Assert.AreEqual(moves + 2, turn.Deck.Hand.Count(c => c.Data.type == CommandType.Walk), "足取り：移動の札だけ2枚");
            var rc = turn.Deck.Hand.First(c => c.Data == rally);
            turn.AddCommand(new CommandRecord(rally, Direction.Up, 1, rc));
            Assert.AreEqual(5, turn.MaxSlots, "号令・改：命令 +2（この札も1枠）");
            Assert.AreEqual(2, turn.LockedCommands, "引いた札の前は取り消せない");
        }

        [Test]
        public void RecycleTakesAMoveCardBackFromTheDiscard()
        {
            var recycle = NewCard("再利用", CommandType.Draw, 1, DrawFilter.Move); recycle.fromDiscard = true;
            var deck = new List<CommandData> { recycle };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 4));
            deck.AddRange(Enumerable.Repeat(Card("Slash"), 8));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk) ?? turn.Deck.DrawPile.First(c => c.Data.type == CommandType.Walk);
            if (!turn.Deck.Hand.Contains(walk)) turn.Deck.TakeFromDrawPile(walk);
            turn.AddCommand(new CommandRecord(walk.Data, Direction.Up, 1, walk));
            turn.ExecuteTurnImmediately();
            Assert.IsTrue(turn.Deck.DiscardPile.Contains(walk));
            if (!turn.Deck.Hand.Any(c => c.Data == recycle)) turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.First(c => c.Data == recycle));
            var r = turn.Deck.Hand.First(c => c.Data == recycle);
            turn.AddCommand(new CommandRecord(recycle, Direction.Up, 1, r));
            Assert.AreEqual(1, turn.PendingRecycle);
            Assert.IsTrue(turn.RecycleChoices.All(c => c.Data.type == CommandType.Walk), "移動の札だけ選べる");
            turn.ChooseRecycle(walk);
            Assert.IsTrue(turn.Deck.Hand.Contains(walk));
        }

        [Test]
        public void ResonantStepDrawsWhenACopyMovesAGimmick()
        {
            var step = NewCard("共鳴の一歩", CommandType.Walk, 1); step.minDistance = 1; step.maxDistance = 1; step.resonateDraw = 2;
            var deck = new List<CommandData> { step };
            deck.AddRange(Enumerable.Repeat(Card("Slash"), 14));
            // 運び像が1マス下へ → 運び紋（まねで仕掛けが動いた）。兵がいるので全滅で勝ちにはならない
            var stage = NewStage("..D....\n..v.E..\n.......\n...P...");
            stage.goal = RoomGoal.Defeat;
            var turn = Battle(stage, deck, out _);
            if (!turn.Deck.Hand.Any(c => c.Data == step)) turn.Deck.TakeFromDrawPile(turn.Deck.DrawPile.First(c => c.Data == step));
            var s0 = turn.Deck.Hand.First(c => c.Data == step);
            int hand = turn.Deck.Hand.Count;
            turn.AddCommand(new CommandRecord(step, Direction.Down, 1, s0));
            turn.ExecuteTurnImmediately();
            Assert.Greater(turn.CopyGimmicksThisBattle, 0, "運び像が運び紋に乗った（まねで仕掛け）");
            Assert.AreEqual(System.Math.Max(7, hand - 1) + 2, turn.Deck.Hand.Count, "7枚まで補充＋共鳴の2枚");
        }

        // ───── 7. パズル中心のラン ─────

        [Test]
        public void PuzzleRunHasEightFloorsAndSavesItsFlag()
        {
            var run = new ActRun(ActContent.Roles[0], 77, Card, false, true);
            Assert.IsTrue(run.PuzzleRun);
            Assert.IsTrue(run.CopyCentric, "コピー中心の札・役職・刻写を使う");
            Assert.AreEqual(8, run.FloorCount);
            Assert.AreEqual("pz_shift", run.Doors[0].Id, "1階は壁ズレ入門");
            Assert.AreEqual(RoomKind.Boss, run.Map.Last().Single().Kind);
            Assert.AreEqual(10, run.MinDeck, "パズル中心の削除の下限");
            var json = JsonUtility.ToJson(ActSave.Capture(run));
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var err), err);
            Assert.IsTrue(back.PuzzleRun);
            CollectionAssert.AreEqual(run.Map.Select(f => string.Join(",", f.Select(r => r.Id))).ToList(), back.Map.Select(f => string.Join(",", f.Select(r => r.Id))).ToList());
            Assert.IsFalse(ActContent.PuzzleRunPool.Any(c => c.Asset == "Sneak" || c.Asset == "Rally" || c.Asset == "DrawTwo"), "忍び足・号令・手繰り寄せは出さない");
        }

        [Test]
        public void HowYouSolvedDecidesTheEngraving()
        {
            var run = new ActRun(ActContent.Roles[0], 78, Card, false, true);
            run.Enter(run.Doors[0]);
            var stats = new BattleStats { Won = true, Turns = 2, HpBefore = 20, HpAfter = 20 };
            stats.CopyPuzzles.Add("運び");
            var slot = run.CopySlotCards(stats);
            Assert.AreEqual("Tailwind", slot[0].Card.name, "搬送 → 追い風");
            StringAssert.Contains("刻写", slot[0].Reason);
        }

        [Test]
        public void EveryPuzzleRoomHasAStageAssetAndAGoalSentence()
        {
            foreach (var room in ActContent.PuzzleRooms)
            {
                var st = AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{room.Stage}.asset");
                Assert.IsNotNull(st, room.Stage);
                Assert.IsTrue(st.puzzleRoom, room.Stage);
                var m = BattleModel.FromStage(st);
                Assert.IsNotEmpty(PuzzleRules.GoalSentence(m, st.goalText), room.Stage);
                Assert.AreEqual(TurnOutcome.Continue, TurnRules.Check(m), room.Stage + "：始めから勝ち・負けになっていない");
            }
        }
        [Test]
        public void BigHandsStackTheSameCardsSoTwoRowsStayReadable()
        {
            var cards = new List<CardInstance>();
            foreach (var n in new[] { "Slash", "Slash", "Slash", "Walk", "Walk", "Dash", "Dash", "Rush", "Thrust", "Push", "Pull", "PushSlash", "Walk", "Slash" })
                cards.Add(new CardInstance(Card(n)));
            var stacks = BattleUI.HandStacks(cards, true);
            Assert.AreEqual(8, stacks.Count, "14枚でも種類は8つ（2段に収まる）");
            Assert.AreEqual(4, stacks.First(st => st[0].Data.name == "Slash").Count, "斬撃×4");
            Assert.AreEqual(14, BattleUI.HandStacks(cards, false).Count, "束ねないときは1枚ずつ");
        }
        // ───── 8. 敵対的レビューで直した物 ─────

        [Test]
        public void CopyMustHitACoreYouDidNotHit()
        {
            var m = Board("c.c....", ".P.....", ".......");
            m.Board.HitCore(new Vector2Int(0, 2), false);
            m.Board.HitCore(new Vector2Int(0, 2), true);
            Assert.IsFalse(m.Board.HitCore(new Vector2Int(2, 2), false), "まねが当たったのが、あなたも叩いた核だけなら割れない（合わせ斬り・多命令の穴）");
        }

        [Test]
        public void StoneHandCannotPushTheLordPastAPhaseLine()
        {
            var m = BossModel();
            var lord = m.Enemies.First();
            lord.Hp = PuzzleBoss.Floor(lord, 0) + 1;
            lord.BombCells = new List<Vector2Int> { lord.Position };
            PuzzleBoss.StoneHand(m, lord, new List<BattleEvent>());
            Assert.AreEqual(0, lord.BossStage, "石手では段階は進まない（仕掛けを解かずに倒すループを塞ぐ）");
            Assert.AreEqual(PuzzleBoss.Floor(lord, 0) + 1, lord.Hp);
        }

        [Test]
        public void WhirlHitsSealStonesAndStatues()
        {
            var m = Board(".s.....", "sEi....", ".......", "...P...");
            m.Board.SetStatueFacing(new Vector2Int(2, 2), Direction.Up);
            var whirl = Card("Whirl");
            TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(whirl, Direction.Up, 0) }, null);
            Assert.IsFalse(m.Board.IsSealStone(new Vector2Int(1, 3)), "まねた回転斬りでも封印石は割れる");
            Assert.IsFalse(m.Board.IsSealStone(new Vector2Int(0, 2)));
            Assert.AreEqual(Direction.Right, m.Board.StatueFacing(new Vector2Int(2, 2)), "石像も回る");
        }

        [Test]
        public void ToolsKeepTheirCopyKindWhateverTheStageLetters()
        {
            var stage = NewStage("..Z.D..\n.......\n...P...");
            stage.enemyCopyTypes = "AA";
            var m = BattleModel.FromStage(stage);
            Assert.AreEqual(CopyKind.Technique, m.Enemies.First(e => e.Kind == EnemyKind.Cannon).CopyKind);
            Assert.AreEqual(CopyKind.Move, m.Enemies.First(e => e.Kind == EnemyKind.Golem).CopyKind, "運び像は移動まねのまま（レビュー：全まねの印が付いた）");
        }

        [Test]
        public void SealsWeakenOnlyAfterEveryKillableEnemyIsDown()
        {
            var m = Board("s......", "E......", "...P...");
            Assert.IsFalse(PuzzleRules.SealsWeak(m));
            En(m, 0).Hp = 0;
            Assert.IsTrue(PuzzleRules.SealsWeak(m), "倒せる敵が全滅したら、あなたの攻撃でも割れる（詰み防止）");
            var tools = Board("s......", "Z......", "...P...");
            Assert.IsFalse(PuzzleRules.SealsWeak(tools), "道具だけの部屋では弱まらない");
        }

        [Test]
        public void GolemsCountOnRedPlatesAndGatesDoNotAutoOpenWithGolems()
        {
            var m = Board("......q", ".......", ".D....g", "#######", "xg.p..P");
            var b = m.Board;
            RulesEngine.CheckPlates(m, new List<BattleEvent>());
            Assert.IsFalse(b.GatesOpen, "運び像がいる部屋は、扉の外に敵がいなくても勝手に開かない");
            m.Enemies.First().Position = new Vector2Int(6, 4);
            m.Player.Position = new Vector2Int(3, 0);
            RulesEngine.CheckPlates(m, new List<BattleEvent>());
            Assert.IsTrue(b.GatesOpen, "運び像も赤い板に乗れる");
        }

        [Test]
        public void RallyDrawIsOnePerTurn()
        {
            var rally = NewCard("号令・改", CommandType.Rally, 2, DrawFilter.MoveAndTechnique, planDraw: 2);
            var deck = new List<CommandData> { rally, rally };
            deck.AddRange(Enumerable.Repeat(Card("Walk"), 10));
            var turn = Battle(NewStage("E......\n.......\n...P..."), deck, out _);
            foreach (var c in turn.Deck.DrawPile.Where(c => c.Data == rally).ToList()) turn.Deck.TakeFromDrawPile(c);
            var two = turn.Deck.Hand.Where(c => c.Data == rally).ToList();
            Assert.AreEqual(2, two.Count);
            Assert.IsTrue(turn.AddCommand(new CommandRecord(rally, Direction.Up, 1, two[0])));
            Assert.IsFalse(turn.CanAddCard(two[1]), "号令・改は1ターン1枚（重ねると7枠：レビュー）");
        }

        // ───── 9. パズルブレイカー（3回目の敵対的レビュー）で直した物 ─────

        [Test]
        public void BladeStatueStrikesOnlyOncePerTurn()
        {
            var m = BossModel();
            var lord = m.Enemies.First();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Down, 2) }, null);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Up), R("Walk", Direction.Left), R("Walk", Direction.Left) }, null);
            Assert.AreEqual(20, lord.Hp);
            Assert.AreEqual(1, lord.BossStage);
            // 段階2：駆け足→で主が右の像の下へ。斬撃↓を2回：1回目は像が主を斬る（成功）。光が消えた像は2回目をまねない
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Dash", Direction.Right, 2), R("Slash", Direction.Down), R("Slash", Direction.Down) }, null);
            Assert.AreEqual(12, lord.Hp, "像は1ターンに1回だけ斬る（レビュー：光が消えた像が2回目で主を斬り、段階2が1ターンで終わった）");
            Assert.AreEqual(1, lord.BossStage, "段階3へは進まない");
            Assert.AreEqual(0, lord.Barrier, "窓は開く");
        }

        [Test]
        public void StatuesTurnToYourAttackOnlyAfterEveryKillableEnemyIsDown()
        {
            var m = Board(".......", ".iP....", "E......");
            var statue = new Vector2Int(1, 1);
            m.Board.SetStatueFacing(statue, Direction.Down);
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Left), false), new List<BattleEvent>());
            Assert.AreEqual(Direction.Down, m.Board.StatueFacing(statue), "敵が残っている間は、あなたの攻撃では回らない");
            En(m, 0).Hp = 0;
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Left), false), new List<BattleEvent>());
            Assert.AreEqual(Direction.Left, m.Board.StatueFacing(statue), "詰み防止：倒せる敵が全滅したら、あなたの攻撃でも回る（視線でしか届かない封印石が残ると詰んだ）");
        }

        [Test]
        public void SealsWeakenedIsAnnouncedOnce()
        {
            var m = Board("s......", "E......", "...P...");
            var ev = new List<BattleEvent>();
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Walk", Direction.Up), false), ev);
            Assert.IsFalse(ev.Any(e => e.Type == BattleEventType.SealsWeakened));
            En(m, 0).Hp = 0;
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Walk", Direction.Down), false), ev);
            Assert.AreEqual(1, ev.Count(e => e.Type == BattleEventType.SealsWeakened), "弱まった瞬間に知らせる（画面に何も出ず、詰んだように見えた）");
            StringAssert.Contains("あなたの攻撃でも", ev.First(e => e.Type == BattleEventType.SealsWeakened).Text);
            RulesEngine.Execute(m, new CommandContext(m.Player, R("Walk", Direction.Up), false), ev);
            Assert.AreEqual(1, ev.Count(e => e.Type == BattleEventType.SealsWeakened), "1回だけ");
            Assert.IsTrue(PuzzleRules.Progress(m).Any(i => i.Text.Contains("弱まった")), "攻略条件の欄にも出る");
        }

        [Test]
        public void TwinEchoKnightsFallOnlyToCopies()
        {
            var stage = NewStage(".R.R...\n.......\n...P...");
            stage.enemyCopyTypes = "AT";
            stage.puzzleRoom = true;
            var m = BattleModel.FromStage(stage);
            var walker = En(m, 0);
            var post = En(m, 1);
            Assert.AreEqual(CopyKind.Technique, post.CopyKind);
            var ev = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, m.Player, Card("Slash"), false, post, 4, DamageKind.Direct, ev);
            Assert.AreEqual(8, post.Hp, "2体とも反響鎧：あなたの攻撃は効かない（技まね兵を素手で倒す近道を塞いだ）");
            walker.Position = new Vector2Int(2, 2);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Right) }, null);
            Assert.AreEqual(4, post.Hp, "全まねの反響鎧兵のまね斬りは効く");
            Assert.AreEqual(8, walker.Hp, "技まね兵のまね斬りは反対側へ（空振り）");
        }

        [Test]
        public void StatueGazeBreaksSealsEvenWithoutALog()
        {
            var m = Board("s......", ".......", "i......", "...P...");
            m.Board.SetStatueFacing(new Vector2Int(0, 1), Direction.Up);
            PuzzleRules.StatuePhase(m, new List<BattleEvent>(), null);
            Assert.IsFalse(m.Board.IsSealStone(new Vector2Int(0, 3)), "記録（log）が無い解析・予測でも視線で割れる（log?.Add の右辺ごと飛んでいた）");
        }

        [Test]
        public void DeflectedHitsAreNotLoggedAsShield()
        {
            var m = Board(".......", ".PR.R..", ".......");
            string text = RulesEngine.Execute(m, new CommandContext(m.Player, R("Slash", Direction.Right), false), new List<BattleEvent>());
            StringAssert.Contains("はじかれた", text);
            StringAssert.DoesNotContain("盾", text, "反響鎧ではじかれたのに「盾で」と出ていた（レビュー）");
        }

        [Test]
        public void RestartedDangerRoomGivesNoExtraRewards()
        {
            foreach (bool restarted in new[] { false, true })
            {
                var run = new ActRun(ActContent.Roles[0], 91, Card, false, true);
                for (int guard = 0; guard < 10 && !run.Doors.Any(r => r.Kind == RoomKind.Danger); guard++)
                {
                    run.Enter(run.Doors[0]);
                    run.Leave();
                }
                var danger = run.Doors.First(r => r.Kind == RoomKind.Danger);
                Assert.IsTrue(run.Enter(danger));
                run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = run.Hp, HpAfter = run.Hp, Restarted = restarted });
                Assert.AreEqual(restarted ? 1 : 2, run.RewardPicksLeft, restarted ? "やり直した危険戦は追加の報酬なし（やり直しが HP の保険になっていた）" : "危険戦は2枚まで");
                Assert.AreEqual(restarted, run.RewardImprints.Count == 0, "刻印の2択も、やり直した危険戦には無い");
            }
        }
    }
}
