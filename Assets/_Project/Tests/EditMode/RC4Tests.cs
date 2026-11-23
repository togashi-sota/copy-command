using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC4・2026-10-23 本人の RC3 プレイ】チュートリアル（8つ・最初の起動・遊び直し・開発版の戻し）・入力の優先順位（向き・敵の選択・キー）・
    /// 敵の自分の動き（兵種ごと）・その予告（TurnForecast）・まねと自分の動きの見分け・転移石（救済の移動）・はじめてのランの階ごとの教え方。
    /// </summary>
    public class RC4Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord R(string card, Direction d) => new CommandRecord(Card(card), d, 1);
        static List<CommandRecord> None => new List<CommandRecord>();
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        (ActRunManager act, TurnManager turn) NewGame()
        {
            var root = new GameObject("RC4");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stages = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            turn.SetupForTest(grid, stages);
            var act = root.AddComponent<ActRunManager>();
            act.SetupForTest(turn, cards, stages);
            return (act, turn);
        }

        StageData V4Stage(string map, string hp, string kinds = "")
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map; s.maxSlots = 3; s.playerMaxHp = 20; s.enemyMaxHp = 12; s.spikeDamage = 3;
            s.enemyHpList = hp; s.enemyCopyTypes = kinds;
            s.combatRoom = true; s.combatV4 = true; s.availableCommands = new List<CommandData>();
            return s;
        }

        static BattleModel NoClock(BattleModel m) { foreach (var e in m.Enemies) e.Clocks.Clear(); return m; }

        // ───── チュートリアル：8つ・最初の起動・遊び直し・開発版の戻し ─────

        [Test]
        public void TutorialHasNineStepsInTheTeachingOrder()
        {
            Assert.AreEqual(9, TutorialContent.Count); // 【RC9】5番「固定と、倒して消す」
            CollectionAssert.AreEqual(new[] { "移動", "攻撃", "まね", "赤い予告とずらし", "固定と、倒して消す", "敵の動き", "戦術札", "大技", "1ランの流れ" }, TutorialContent.Steps.Select(s => s.Name));
            Assert.AreEqual(TutorialGoal.RunLoop, TutorialContent.Steps.Last().Goal, "最後は戦闘をしない「1ランの流れ」");
            foreach (var s in TutorialContent.Steps.Where(s => s.Asset != null)) Assert.IsNotNull(Stage(s.Asset), s.Asset + "（run-setup で作る）");
            Assert.AreEqual(TutorialContent.Steps.Length, TutorialContent.Count);
            Assert.AreEqual(5, TutorialContent.Summary.Length, "あなたが覚えたこと：命令 → まね → 予告 → 敵の力 → 8階のボス");
            Assert.IsTrue(TutorialContent.Summary.Last().Contains("8階"));
            foreach (var n in TutorialContent.RunLoopRewardCards) Assert.IsNotNull(Card(n), "報酬の見本は本物の札：" + n);
        }

        [Test]
        public void TutorialOwnMoveRoomChaserComesNextToYouWhereTheOrangeArrowSays()
        {
            var m = BattleModel.FromStage(Stage("StageTU7"));
            var chaser = m.Enemies.Single();
            Assert.AreEqual(EnemyKind.Chaser, chaser.Kind);
            Assert.AreEqual(CopyKind.None, chaser.CopyKind, "練習用：まねしない（覚えるのは自分の動きだけ）");
            Assert.IsTrue(chaser.Clocks.Count == 0, "大技なし");
            var f = TurnForecast.Compute(m, None);
            var mv = f.OwnMoves.Single();
            Assert.AreEqual(chaser.Id, mv.EnemyId);
            Assert.AreEqual(1, Mathf.Abs(mv.To.x - m.Player.Position.x) + Mathf.Abs(mv.To.y - m.Player.Position.y), "オレンジの矢印の先＝あなたの隣");
            Assert.AreEqual("接近2", mv.Label);
            TurnRules.PlayTurn(m, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(mv.To, chaser.Position, "予告どおりに動く");
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp, "近づいたターンは噛まない（赤はターンの初めに決まる）");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Up), R("Slash", Direction.Up) }, null, new List<BattleEvent>(), null);
            Assert.IsFalse(chaser.IsAlive, "あなたの命令が先：隣に来た追跡兵を、噛まれる前に斬れる（HP 8 ＝ 斬撃2回）");
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp);
        }

        [Test]
        public void TutorialRunLoopCanBeSkippedWithoutPickingAndLeavesNothing()
        {
            var (act, _) = NewGame();
            act.StartTutorial();
            for (int i = 0; i < TutorialContent.Count - 1; i++) act.NextTutorialStep();
            Assert.AreEqual(ActScreen.TutorialRun, act.Screen);
            act.TutorialRunNext(); // 取らずに進む
            Assert.AreEqual(1, act.TutorialRunPage);
            act.TutorialRunPickCard(0);
            Assert.AreEqual(-1, act.TutorialRunCard, "扉の頁では札を選べない");
            act.NextTutorialStep(); // 「できた！」と同じ入口でも進む
            act.NextTutorialStep();
            Assert.AreEqual(ActScreen.TutorialDone, act.Screen);
            Assert.IsNull(act.Run);
        }

        [Test]
        public void FirstLaunchOffersTheTutorialAndDevResetsBringItBack()
        {
            var (act, _) = NewGame();
            Assert.IsFalse(act.SaveEnabled, "テストはファイルに書かない");
            Assert.IsFalse(ActUI.ShowDevTitle, "開発版でも、ふつうに Play するとタイトルは公開版と同じ（研究室は【開発】のボタンから）");
            var p = act.Profile;
            Assert.IsTrue(!p.tutorialOffered && !p.tutorialDone, "はじめての起動：チュートリアルの案内が出る");
            // RC2・RC3 で遊んだ人（記録あり・チュートリアル済みの例）
            p.tutorialOffered = true; p.tutorialDone = true; p.tacticIntroSeen = true; p.runsFinished = 4; p.clears = 2; p.volMaster = 40;
            p.hintsSeen.Add("V0");
            act.DevResetTutorialSeen();
            Assert.IsTrue(!act.Profile.tutorialOffered && !act.Profile.tutorialDone, "【開発】チュートリアル未閲覧：案内がもう一度出る");
            Assert.IsFalse(act.Profile.tacticIntroSeen);
            Assert.IsEmpty(act.Profile.hintsSeen);
            Assert.AreEqual(4, act.Profile.runsFinished, "記録は残す");
            act.DevResetToFirstLaunch();
            Assert.AreEqual(0, act.Profile.runsFinished, "【開発】はじめての状態：記録も消える");
            Assert.AreEqual(0, act.Profile.clears);
            Assert.AreEqual(40, act.Profile.volMaster, "音量は残す");
            Assert.AreEqual(ActScreen.Title, act.Screen);
        }

        [Test]
        public void DeclineThenReplayFromTheTitle()
        {
            var (act, turn) = NewGame();
            act.DeclineTutorial();
            Assert.IsTrue(act.Profile.tutorialOffered, "「すぐに本編」を選んだら案内はもう出さない");
            act.StartTutorial(); // タイトルの「チュートリアル」
            Assert.AreEqual("StageTU1", turn.CurrentStage.name);
            act.SkipTutorial();
            Assert.AreEqual(ActScreen.Title, act.Screen);
        }

        // ───── 入力の優先順位（本人「移動方向を押したいのに UI が邪魔して押せない」） ─────

        [Test]
        public void DirectionCellsWinOverPathCellsAndAnythingBehind()
        {
            var main = new Dictionary<Direction, Rect>
            {
                { Direction.Up, new Rect(100, 0, 100, 100) }, { Direction.Down, new Rect(100, 200, 100, 100) },
                { Direction.Left, new Rect(0, 100, 100, 100) }, { Direction.Right, new Rect(200, 100, 100, 100) },
            };
            // 右の道のマスが上のマスと重なっている（盤面の外の大きな矢印など）→ 隣のマス（主）が先
            var extra = new List<(Direction, Rect)> { (Direction.Right, new Rect(120, 20, 60, 60)), (Direction.Right, new Rect(300, 100, 100, 100)) };
            Assert.IsTrue(InputPriority.PickDirection(main, extra, new Vector2(150, 50), out var d)); Assert.AreEqual(Direction.Up, d, "重なったら隣のマス");
            Assert.IsTrue(InputPriority.PickDirection(main, extra, new Vector2(350, 150), out d)); Assert.AreEqual(Direction.Right, d, "道のマス（2マス先）でも向きになる");
            Assert.IsTrue(InputPriority.PickDirection(main, extra, new Vector2(10, 110), out d)); Assert.AreEqual(Direction.Left, d, "盤面の端でも、隣のマスまるごと");
            Assert.IsFalse(InputPriority.PickDirection(main, extra, new Vector2(150, 150), out _), "あなたのマスは向きではない");
            Assert.IsTrue(InputPriority.DirectionFirst(true, false, false));
            Assert.IsFalse(InputPriority.DirectionFirst(true, true, false), "距離を選ぶ窓が開いている間は、窓が先");
            Assert.IsFalse(InputPriority.DirectionFirst(false, false, false));
        }

        [Test]
        public void EnemyDetailDoesNotStealTheClickWhileChoosingADirection()
        {
            Assert.IsTrue(InputPriority.EnemySelectAllowed(0, false, false, false, false), "ふだんは敵をクリックで詳細");
            Assert.IsFalse(InputPriority.EnemySelectAllowed(0, true, false, false, false), "札を選んでいる間は、敵のマスも向きのクリック");
            Assert.IsFalse(InputPriority.EnemySelectAllowed(1, false, false, false, false), "右クリックは取り消し（敵を選ばない）");
            Assert.IsFalse(InputPriority.EnemySelectAllowed(0, false, true, false, false), "戦術札の対象を選んでいる間は、対象の選択が先");
            Assert.IsTrue(InputPriority.IsCancel(EventType.MouseDown, 1, KeyCode.None));
            Assert.IsTrue(InputPriority.IsCancel(EventType.KeyDown, 0, KeyCode.Escape));
            Assert.IsFalse(InputPriority.IsCancel(EventType.MouseDown, 0, KeyCode.None));
        }

        [Test]
        public void WasdAndArrowKeysChooseTheDirection()
        {
            Assert.AreEqual(Direction.Up, InputPriority.KeyToDirection(KeyCode.W));
            Assert.AreEqual(Direction.Up, InputPriority.KeyToDirection(KeyCode.UpArrow));
            Assert.AreEqual(Direction.Down, InputPriority.KeyToDirection(KeyCode.S));
            Assert.AreEqual(Direction.Down, InputPriority.KeyToDirection(KeyCode.DownArrow));
            Assert.AreEqual(Direction.Left, InputPriority.KeyToDirection(KeyCode.A));
            Assert.AreEqual(Direction.Left, InputPriority.KeyToDirection(KeyCode.LeftArrow));
            Assert.AreEqual(Direction.Right, InputPriority.KeyToDirection(KeyCode.D));
            Assert.AreEqual(Direction.Right, InputPriority.KeyToDirection(KeyCode.RightArrow));
            Assert.IsNull(InputPriority.KeyToDirection(KeyCode.Z), "Z X C は戦術札（向きとぶつからない）");
            Assert.IsNull(InputPriority.KeyToDirection(KeyCode.Return));
        }

        // ───── 敵の自分の動き（兵種ごとの個性。本人「全員が毎ターン近づく・いつ動くのか分からない」） ─────

        [Test]
        public void SwordsmanStepsOnlyWhenFarOrPinnedToTheEdge()
        {
            // 隣（2マス以内・端でない）：自分では動かない
            var near = NoClock(BattleModel.FromStage(V4Stage(".......\n.......\n...S...\n...P...\n.......\n.......", "12", "N")));
            var s1 = near.Enemies.Single(); var p1 = s1.Position;
            Assert.IsEmpty(TurnForecast.Compute(near, None).OwnMoves);
            TurnRules.PlayTurn(near, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(p1, s1.Position, "近くでは自分で近づかない（まねで動く）");
            // 3マス以上：1マス近づく
            var far = NoClock(BattleModel.FromStage(V4Stage(".......\n.......\n.S.....\n.......\n....P..\n.......", "12", "N")));
            var s2 = far.Enemies.Single(); var p2 = s2.Position;
            var mv = TurnForecast.Compute(far, None).OwnMoves.Single();
            TurnRules.PlayTurn(far, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(mv.To, s2.Position);
            Assert.AreEqual(1, Mathf.Abs(s2.Position.x - p2.x) + Mathf.Abs(s2.Position.y - p2.y), "1マスだけ");
            // 端で2マス：1マス近づく（まねで端に張り付いたままにならない）
            var edge = NoClock(BattleModel.FromStage(V4Stage("S......\n.P.....\n.......\n.......\n.......\n.......", "12", "N")));
            Assert.AreEqual(1, TurnForecast.Compute(edge, None).OwnMoves.Count, "端・2マス：近づく");
            var mid = NoClock(BattleModel.FromStage(V4Stage(".......\n.S.....\n.......\n.P.....\n.......\n.......", "12", "N")));
            Assert.IsEmpty(TurnForecast.Compute(mid, None).OwnMoves, "端でない・2マス：近づかない");
        }

        [Test]
        public void SpearmanHoldsAndFacesYouChaserRunsTwoBomberAndShieldStay()
        {
            // 【RC5】槍兵：2マス以内では動かず向く・3マス以上離れると1マス詰める（最後の1体が端で構えたまま片付けが長くなった）
            var spearNear = NoClock(BattleModel.FromStage(V4Stage("Y......\n.......\nP......\n.......\n.......\n.......", "12", "N")));
            var yn = spearNear.Enemies.Single(); var pyn = yn.Position;
            TurnRules.PlayTurn(spearNear, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(pyn, yn.Position, "2マス：槍兵は動かない");
            var spear = NoClock(BattleModel.FromStage(V4Stage("Y......\n.......\n.......\n.......\n......P\n.......", "12", "N")));
            var y = spear.Enemies.Single(); var py = y.Position;
            TurnRules.PlayTurn(spear, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(1, Mathf.Abs(y.Position.x - py.x) + Mathf.Abs(y.Position.y - py.y), "遠い：1マス詰める");
            var chase = NoClock(BattleModel.FromStage(V4Stage("C......\n.......\n.......\n.......\n......P\n.......", "12", "N")));
            var c = chase.Enemies.Single(); var pc = c.Position;
            TurnRules.PlayTurn(chase, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(2, Mathf.Abs(c.Position.x - pc.x) + Mathf.Abs(c.Position.y - pc.y), "追跡兵は2マス近づく");
            var fixedOnes = NoClock(BattleModel.FromStage(V4Stage("O......\n.......\n......T\n.......\n...P...\n.......", "12,12", "NN")));
            var before = fixedOnes.Enemies.Select(e => e.Position).ToList();
            TurnRules.PlayTurn(fixedOnes, None, null, new List<BattleEvent>(), null);
            CollectionAssert.AreEqual(before, fixedOnes.Enemies.Select(e => e.Position).ToList(), "砲兵・盾兵は動かない");
            Assert.IsTrue(PhaseRules.MovesOnItsOwn(y), "【RC5】離れすぎると詰める"); 
            Assert.IsTrue(PhaseRules.MovesOnItsOwn(c));
            foreach (var k in new[] { EnemyKind.Swordsman, EnemyKind.Spearman, EnemyKind.Archer, EnemyKind.Chaser, EnemyKind.Bomber, EnemyKind.Shield, EnemyKind.Mender })
                Assert.IsFalse(string.IsNullOrEmpty(PhaseRules.MoveStyle(k)), "どの兵種も1文で言える：" + k);
        }

        [Test]
        public void ForecastOwnMovesMatchWhatReallyHappensInEveryRoom()
        {
            var rooms = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null && s.combatV4 && s.tutorialStep == 0 && s.name.StartsWith("StageX")).ToList();
            Assert.Greater(rooms.Count, 10);
            int checkedMoves = 0;
            foreach (var s in rooms)
                foreach (var plan in new[] { None, new List<CommandRecord> { R("Walk", Direction.Up) }, new List<CommandRecord> { R("Slash", Direction.Left), R("Walk", Direction.Right) } })
                {
                    var m = BattleModel.FromStage(s);
                    var f = TurnForecast.Compute(m, plan);
                    Assert.IsFalse(f.Failed, s.name);
                    var real = m.Clone();
                    var events = new List<BattleEvent>();
                    TurnRules.PlayTurn(real, plan, null, events, null);
                    foreach (var mv in f.OwnMoves)
                    {
                        var e = real.Units.FirstOrDefault(u => u.Id == mv.EnemyId);
                        if (e == null || !e.IsAlive) continue;
                        // 予告した道の最後＝本当の位置（そのあと床の仕掛けで倒れた・押された物は除く）
                        Assert.AreEqual(mv.To, e.Position, $"{s.name}：{e.Name} の予告");
                        Assert.IsTrue(events.Any(ev => ev.Type == BattleEventType.Moved && ev.Unit != null && ev.Unit.Id == e.Id && TurnForecast.MoveKindOf(ev) == EnemyMoveKind.Own), "自分の動きの出来事");
                        checkedMoves++;
                    }
                    int hpForecast = m.Player.Hp - f.PlayerHits.Sum(h => h.Amount);
                    Assert.AreEqual(System.Math.Max(0, hpForecast), System.Math.Max(0, real.Player.Hp), $"{s.name}：あなたが受けるダメージの予告");
                }
            Assert.Greater(checkedMoves, 5, "自分で動く敵の予告を確かめた");
        }

        [Test]
        public void CopyMovesAndOwnMovesAreTaggedDifferently()
        {
            // 全まねの剣兵（3マス以上離れている）：あなたの「一歩 ↑」をまねて ↑ → そのあと自分で1マス近づく
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n......S\n.......\n.......\nP......\n.......", "12", "A")));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up) }, null, events, null);
            var sw = m.Enemies.Single();
            var moves = events.Where(e => e.Type == BattleEventType.Moved && e.Unit == sw).ToList();
            Assert.IsTrue(moves.Any(e => TurnForecast.MoveKindOf(e) == EnemyMoveKind.Copy), "まねの移動（水色・「まね」）");
            Assert.IsTrue(moves.Any(e => TurnForecast.MoveKindOf(e) == EnemyMoveKind.Own), "自分の動き（オレンジ・「接近」）");
            Assert.AreEqual("まね", TurnForecast.MoveTagText(EnemyMoveKind.Copy, EnemyKind.Swordsman));
            Assert.AreEqual("接近", TurnForecast.MoveTagText(EnemyMoveKind.Own, EnemyKind.Swordsman));
            Assert.AreEqual("位置取り", TurnForecast.MoveTagText(EnemyMoveKind.Own, EnemyKind.Archer));
            Assert.IsTrue(events.Where(e => e.Type == BattleEventType.Moved && e.Unit != null && e.Unit.IsPlayer).All(e => TurnForecast.MoveKindOf(e) == EnemyMoveKind.None), "あなたの動きには付けない");
        }

        // ───── ダメージの原因（本人「何で食らったのか分からない」） ─────

        [Test]
        public void EveryDamageToYouHasAShortCause()
        {
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n...S...\n...P...\n.......", "12", "A")));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null, events, null);
            var hits = events.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0).ToList();
            Assert.IsNotEmpty(hits, "赤の上に残った");
            foreach (var h in hits)
            {
                string t = TurnForecast.CauseText(h);
                Assert.IsFalse(string.IsNullOrEmpty(t));
                Assert.AreNotEqual("ダメージ", t, "原因の分からないダメージは無い");
            }
        }

        // ───── 転移石（救済の移動） ─────

        [Test]
        public void WarpStoneMovesYouToAFreeCellWithinThreeAndIsNotCopied()
        {
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n..#S...\n...P...\n.......\n.......\n.......", "12", "A")));
            var targets = RescueRules.Targets(m);
            var p = m.Player.Position;
            Assert.IsNotEmpty(targets);
            Assert.IsTrue(targets.All(c => Mathf.Abs(c.x - p.x) + Mathf.Abs(c.y - p.y) <= RescueRules.Range), "3マス以内");
            Assert.IsFalse(targets.Contains(m.Enemies.Single().Position), "敵のマスには行けない");
            Assert.IsFalse(targets.Any(c => m.Board.IsWall(c)), "壁・柱には行けない");
            Assert.IsFalse(targets.Contains(p));
            var sw = m.Enemies.Single(); var sp = sw.Position;
            var to = targets.OrderByDescending(c => Mathf.Abs(c.x - sp.x) + Mathf.Abs(c.y - sp.y)).First();
            Assert.AreEqual(0, RescueRules.DamageIfWarped(m, to), "離れたマスは安全（予告）");
            var events = new List<BattleEvent>();
            Assert.IsNotNull(RescueRules.Warp(m, to, events));
            Assert.AreEqual(to, m.Player.Position);
            Assert.AreEqual(sp, sw.Position, "敵はまねない（命令ではない）");
            TurnRules.PlayTurn(m, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp, "赤から出られた");
        }

        [Test]
        public void WarpStoneWarnsOnSpikesAndRefusesLethalOnesAndWarps()
        {
            // 剣兵（まねない）の真下＝赤の上にあなた。右はトゲ・左下はワープ
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n...S...\n...P^..\n..w....\n.......\n......w", "12", "N")));
            var spike = m.Player.Position + Vector2Int.right;
            Assert.IsTrue(RescueRules.CanUse(m), "赤の上：使える");
            Assert.IsTrue(m.Board.HasSpike(spike));
            Assert.IsTrue(RescueRules.IsTarget(m, spike), "トゲにも行ける");
            Assert.AreEqual(m.Board.SpikeDamage, RescueRules.DamageIfWarped(m, spike), "そのマスで受けるダメージを出す");
            Assert.IsFalse(RescueRules.Targets(m).Any(c => m.Board.TryWarp(c, out _)), "ワープの陣には行けない");
            m.Player.Hp = m.Board.SpikeDamage;
            Assert.IsFalse(RescueRules.IsTarget(m, spike), "入ると倒れるトゲは選べない");
        }

        [Test]
        public void WarpStoneIsARescueNotAnAttackTool()
        {
            // 安全なマスにいるときは使えない（1ターン目に盾兵の後ろへ跳ぶ・壁の向こうへ出る、を止める：敵対的レビュー）
            var safe = NoClock(BattleModel.FromStage(V4Stage(".......\n.S.....\n.......\n.......\n....P..\n.......", "12", "N")));
            Assert.IsFalse(RescueRules.CanUse(safe));
            Assert.IsEmpty(RescueRules.Targets(safe));
            Assert.IsNotNull(RescueRules.WhyNot(safe));
            // 壁の向こう（縦横の差は3以内）へは行けない：行き先は壁を通らない3歩以内
            var wall = NoClock(BattleModel.FromStage(V4Stage(".......\n.......\n#######\n...S...\n...P...\n.......", "12", "N")));
            Assert.IsTrue(RescueRules.CanUse(wall), "剣兵の隣＝赤の上");
            Assert.IsNotEmpty(RescueRules.Targets(wall), "こちら側には行ける");
            var wallRow = Enumerable.Range(0, wall.Board.Height).First(y => wall.Board.IsWall(new Vector2Int(0, y)));
            Assert.IsFalse(RescueRules.Targets(wall).Any(c => (c.y - wallRow) * (wall.Player.Position.y - wallRow) < 0), "壁の向こう側のマスは行き先にならない");
        }

        [Test]
        public void RunsStartWithOneWarpStoneAndTheManagerUsesIt()
        {
            var (act, turn) = NewGame();
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 11, true, false, true, true);
            Assert.AreEqual(1, act.Run.Items.Count(i => i == ItemId.WarpStone), "はじめに転移石1つ");
            Assert.IsTrue(act.Run.Items.Count < ActContent.ItemSlots, "もう1つ持てる（商人で買える）");
            Assert.IsNotNull(ActContent.Item(ItemId.WarpStone));
            Assert.IsFalse(act.Run.BoonOptions.Any(o => o.Kind == BoonKind.Supplies && o.Title.Contains("回収の鉤")), "2つ入らない旅立ちは出さない");
        }

        // ───── はじめてのランの教え方（1階＝まね → 2階＝赤と動き → 3階＝緑と大技） ─────

        [Test]
        public void FirstRunTeachesOneThingPerFloor()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run.ForceFirstDoor("v4_i1");
            run.SetFloorForTest(0, V4Content.Room("v4_i1"));
            var f1 = run.MakeBattleSetup();
            Assert.IsTrue(f1.NoTraits && f1.NoClocks && f1.TacticCards.Count == 0, "1階：まねだけ");
            foreach (var id in ActRun.LessonFloor2Rooms)
            {
                run.SetFloorForTest(1, V4Content.Room(id));
                var f2 = run.MakeBattleSetup();
                Assert.IsTrue(!f2.NoTraits && f2.NoClocks && f2.TacticCards.Count == 0, "2階：赤と敵の動き（大技・緑はまだ）");
                // 2階の部屋で自分から動く兵種は1つまで
                var m = BattleModel.FromStage(Stage(V4Content.Room(id).Stage));
                // 自分で動く「動き方」は1種類まで（剣兵と槍兵は同じ「3マス以上離れると詰める」）
                Assert.LessOrEqual(m.Enemies.Where(e => e.IsAlive && !e.IsObject && PhaseRules.MovesOnItsOwn(e)).Select(e => e.Kind == EnemyKind.Spearman ? EnemyKind.Swordsman : e.Kind).Distinct().Count(), 1, id);
                Assert.IsFalse(string.IsNullOrEmpty(V4Content.Room(id).LessonTip) && (V4Content.Room(id).CopyTip.Contains("赤紫") || V4Content.Room(id).CopyTip.Contains("大技")), "2階の扉に大技の攻め方を書かない：" + id);
            }
            run.SetFloorForTest(2, V4Content.Room("v4_t4"));
            var f3 = run.MakeBattleSetup();
            Assert.IsTrue(!f3.NoTraits && f3.NoClocks && f3.TacticCards.Count > 0, "3階（トゲの通路）：緑の札。大技はまだ（3階で一度に増えすぎない）");
            run.SetFloorForTest(2, V4Content.Room("v4_m1"));
            var f3m = run.MakeBattleSetup();
            Assert.IsTrue(!f3m.NoClocks && f3m.TacticCards.Count > 0, "3階の中ボス：大技は中ボスの力");
            run.SetFloorForTest(4, V4Content.Room("v4_n4"));
            Assert.IsFalse(run.MakeBattleSetup().NoClocks, "5階から大技");
            // 2階の扉は、自分で動く敵（離れた剣兵）がいる2部屋。同じ部屋は1ランに1回
            var run2 = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 9, Look, true, false, true, true);
            run2.ForceFirstDoor("v4_i1");
            CollectionAssert.AreEquivalent(new[] { "v4_n1", "v4_n6" }, run2.Map[1].Select(r => r.Id));
            var fights = run2.Map.SelectMany(f => f).Where(r => r.Kind != RoomKind.Rest && r.Kind != RoomKind.Shop).Select(r => r.Id).ToList();
            Assert.AreEqual(fights.Count, fights.Distinct().Count(), "同じ部屋は1ランに1回");
        }

        [Test]
        public void RoomTextsMatchTheNewMovement()
        {
            var text = System.IO.File.ReadAllText("tools/combat_v4_rooms.txt");
            Assert.IsFalse(text.Contains("どちらも、まねのあとで近づく"), "槍兵は近づかない");
            Assert.IsFalse(text.Contains("あなたへ近づいて向き直す"), "槍兵は近づかない");
            foreach (var kind in new[] { EnemyKind.Spearman, EnemyKind.Shield, EnemyKind.Bomber, EnemyKind.Mender })
                Assert.IsFalse(PhaseRules.MoveStyle(kind).Contains("近づく") && !PhaseRules.MoveStyle(kind).Contains("近づかない"), kind + " は自分で近づかない");
        }

        [Test]
        public void TacticCardsShowTheirCategory()
        {
            Assert.AreEqual("攻撃の助け", UiStyles.TacticCategoryName(TacticKind.Focus));
            Assert.AreEqual("守り・回復", UiStyles.TacticCategoryName(TacticKind.Mend));
            Assert.AreEqual("敵を操る", UiStyles.TacticCategoryName(TacticKind.EarlyBell));
            Assert.AreEqual("手札・命令", UiStyles.TacticCategoryName(TacticKind.Breath));
            foreach (var n in V4Content.StartTactics) Assert.AreNotEqual("戦術札", UiStyles.CategoryName(Card(n)), "最初の緑の札は全部、系統が書いてある：" + n);
        }
    }
}
