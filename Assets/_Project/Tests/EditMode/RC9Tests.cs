using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC9・本人「本編で答えまで出すのは親切すぎる」】本編はルールだけ（攻撃元・固定／追従・ダメージ・順番）、答え（ずれた後・消える・受けるダメージ・無傷）はチュートリアルだけ。
    /// 5分体験（はじめの2つだけ予告あり）・チュートリアル5番「固定と、倒して消す」・ボス HP 42（段階 80%・40%）・札の短い文。
    /// </summary>
    public class RC9Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            BattleUI.DebugAnswerPreview = false;
        }

        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord Rec(string name, Direction d) { var c = Card(name); return new CommandRecord(c, d, c.minDistance, new CardInstance(c)); }

        (ActRunManager act, TurnManager turn, BattleUI ui) NewGame()
        {
            var root = new GameObject("RC9");
            created.Add(root);
            var grid = root.AddComponent<GridManager>();
            var turn = root.AddComponent<TurnManager>();
            var stages = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            turn.SetupForTest(grid, stages);
            var act = root.AddComponent<ActRunManager>();
            act.SetupForTest(turn, cards, stages);
            var ui = root.AddComponent<BattleUI>();
            ui.SetupForTest(turn, act);
            return (act, turn, ui);
        }

        /// <summary>チュートリアル4と同じ部屋（剣兵が真上・押し出しで赤がずれる）を、本編の戦闘として読み込む。</summary>
        void LoadAsMainBattle(TurnManager turn, string asset)
        {
            var src = Stage(asset);
            var stage = Object.Instantiate(src);
            created.Add(stage);
            stage.name = src.name;
            turn.LoadActBattle(stage, new ActBattleSetup { Hp = 20, MaxHp = 20, Deck = src.handTestDeck.Where(c => c != null).ToList(), BaseHand = 7, Rng = new System.Random(1), Seed = 1, TidyMax = 0, NoClocks = src.noClocks, TacticCards = new List<CommandData>() });
        }

        // ───── 本編：答えは出さない（ずれた後・消える・受けるダメージ・無傷・命令の欄の「！」） ─────

        [Test]
        public void MainRunShowsTheRulesButNotTheAnswer()
        {
            var (act, turn, ui) = NewGame();
            LoadAsMainBattle(turn, "StageTU4");
            Assert.IsFalse(act.InTutorial);
            Assert.IsFalse(act.AnswerPreviewOn, "本編は結果の予告を出さない");
            Assume.That(ui.CombatV2ForTest, "新しい盤面の印の戦闘");
            var r = ui.AnswerPreviewForTest(Rec("Push", Direction.Up));
            Assert.AreEqual(0, r.changes, "「ずれる」「倒すと消える」は出ない");
            Assert.IsFalse(r.shownDiffers, "向きにマウスを乗せても、盤面の危険はターンの初めのまま（ずれた後の完成形を出さない）");
            Assert.IsFalse(r.selfKnown, "「この向きだと あなた Nダメ／無傷」は出ない");
            Assert.IsFalse(r.danger, "命令の欄の「！ このターン あなたに N」は出ない");
            // ルール（攻撃元・種類・ダメージ）は出す
            var th = ui.ThreatsShownForTest();
            var sw = turn.Grid.Model.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            var red = th.Single(t => t.Source == sw && t.Timing == ThreatTiming.Attack);
            Assert.AreEqual(ThreatKind.Follow, red.Kind, "点線＝敵と一緒に動く（ルール）");
            Assert.IsTrue(red.Cells.Contains(turn.Grid.Model.Player.Position), "あなたのマスにも A の攻撃（今の盤面）");
            Assert.Greater(red.Damage, 0, "攻撃1つのダメージは出す");
        }

        // ───── あなたの見え方 ─────

        [Test]
        public void YourCellKeepsYourSpriteAndTheTagCarriesTheAttacksOnYou()
        {
            var (act, turn, ui) = NewGame();
            LoadAsMainBattle(turn, "StageTU4");
            Assume.That(ui.CombatV2ForTest, "新しい盤面の印の戦闘");
            var m = turn.Grid.Model;
            var me = m.Player.Position;
            Assert.IsFalse(ui.DrawsPillsOnForTest(me), "あなたのマスには盤面の札を重ねない（あなたの絵をつぶさない）");
            var sw = m.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            Assert.IsTrue(ui.ThreatsOnYouForTest().Any(t => t.Source == sw && t.Damage > 0), "あなたに来る攻撃は「▼あなた」の札の中に（情報は消えない）");
            var other = ui.ThreatsShownForTest().SelectMany(t => t.Cells).First(c => c != me);
            Assert.IsTrue(ui.DrawsPillsOnForTest(other), "ほかのマスには札を出す");
        }

        [Test]
        public void TutorialShowsTheAnswerBeforeYouTry()
        {
            var (act, turn, ui) = NewGame();
            act.StartTutorial();
            while (act.TutorialSteps[act.TutorialStep].Asset != "StageTU4") act.NextTutorialStep();
            Assert.IsTrue(act.AnswerPreviewOn, "チュートリアルは予告を出す（Before → 操作 → After）");
            var r = ui.AnswerPreviewForTest(Rec("Push", Direction.Up));
            Assert.Greater(r.changes, 0, "押す向きにマウスを乗せると「ずれる」が出る");
            Assert.IsTrue(r.shownDiffers, "ずれた後の攻撃が盤面に出る");
            Assert.IsTrue(r.selfKnown, "受けるダメージ（無傷）も出る");
        }

        [Test]
        public void DebugSwitchCanShowAnswersForComparisonOnly()
        {
            var (act, turn, ui) = NewGame();
            LoadAsMainBattle(turn, "StageTU4");
            BattleUI.DebugAnswerPreview = true;
            Assert.Greater(ui.AnswerPreviewForTest(Rec("Push", Direction.Up)).changes, 0, "撮影・比べる用の切り替え（既定は OFF）");
            BattleUI.DebugAnswerPreview = false;
            Assert.AreEqual(0, ui.AnswerPreviewForTest(Rec("Push", Direction.Up)).changes);
        }

        // ───── 5分体験：はじめの2つだけ予告あり・部屋はどれも書いてあるとおりに解ける ─────

        [Test]
        public void QuickExperienceHasFiveCoreStepsAndPreviewOnlyFirstTwo()
        {
            Assert.AreEqual(3, TutorialContent.QuickSteps.Length, "【通常リリース版】まね → 落石へ送る → まねの連鎖（まねだけの3問）");
            Assert.AreEqual(1, TutorialContent.QuickPreviewSteps);
            CollectionAssert.AreEqual(new[] { TutorialGoal.CopyHit, TutorialGoal.Targets, TutorialGoal.Targets }, TutorialContent.QuickSteps.Select(s => s.Goal)); // 【RC11】3〜5問目は自分で解くパズル（赤と紫・落石・大技と同士討ち）
            foreach (var s in TutorialContent.QuickSteps) Assert.IsNotNull(Stage(s.Asset), s.Asset + "（run-setup で作る）");
            var (act, turn, _) = NewGame();
            act.StartQuick();
            for (int i = 0; i < TutorialContent.QuickSteps.Length; i++)
            {
                Assert.AreEqual(TutorialContent.QuickSteps[i].Asset, turn.CurrentStage.name);
                Assert.AreEqual(i < 1, act.AnswerPreviewOn, $"5分体験の {i + 1} 番：予告は {(i < 1 ? "あり" : "なし")}");
                StringAssert.StartsWith("5分体験", turn.CurrentStage.stageTitle);
                act.NextTutorialStep();
            }
            Assert.AreEqual(ActScreen.TutorialDone, act.Screen);
            Assert.IsTrue(act.QuickMode, "おわりの画面は5分体験の中身");
            Assert.IsNull(act.Run, "ランは作らない（記録もセーブもしない）");
            act.OpenTitle();
            Assert.IsFalse(act.QuickMode);
        }

        [Test]
        public void QuickRoomsWorkAsTheirLinesSay()
        {
            // QX1：剣兵A を → へ押す → A の赤（点線）が兵B に当たる（②・同士討ち）。何もしなければ当たらない
            var idle = BattleModel.FromStage(Stage("StageQX1"));
            var ev0 = new List<BattleEvent>();
            TurnRules.PlayTurn(idle, new List<CommandRecord>(), null, ev0, null);
            Assert.IsFalse(ev0.Any(FriendlyRed), "何もしなければ同士討ちは起きない（押して初めて当たる）");
            var push = BattleModel.FromStage(Stage("StageQX1"));
            var ev1 = new List<BattleEvent>();
            TurnRules.PlayTurn(push, new List<CommandRecord> { Rec("Push", Direction.Right) }, null, ev1, null);
            Assert.IsTrue(ev1.Any(FriendlyRed), "押すと A の赤が B に当たる");
            Assert.AreEqual(push.Player.MaxHp, push.Player.Hp, "あなたは当たらない（A の赤が離れた）");
            // QX2：あなたが ↓ → 全まねの兵も ↓ → 落石の影 → ⑥で岩
            var rock = BattleModel.FromStage(Stage("StageQX2"));
            var ev2 = new List<BattleEvent>();
            TurnRules.PlayTurn(rock, new List<CommandRecord> { Rec("Walk", Direction.Down) }, null, ev2, null);
            Assert.IsTrue(ev2.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Cause != null && e.Cause.Kind == DamageKind.Fire), "兵が落石の影へ歩いて当たる");
            var side = BattleModel.FromStage(Stage("StageQX2"));
            var ev3 = new List<BattleEvent>();
            TurnRules.PlayTurn(side, new List<CommandRecord> { Rec("Walk", Direction.Left) }, null, ev3, null);
            Assert.IsFalse(ev3.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer), "ほかの向きでは当たらない（読む意味がある）");
        }

        static bool FriendlyRed(BattleEvent e) => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && e.Cause.Kind == DamageKind.Trait && !e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit;

        // ───── チュートリアル：A ずらす（4番）・C 固定は動かない ＋ B 倒して消す（5番） ─────

        [Test]
        public void TutorialTeachesShiftFixedAndKill()
        {
            Assert.AreEqual(9, TutorialContent.Count);
            CollectionAssert.AreEqual(new[] { "移動", "攻撃", "まね", "赤い予告とずらし", "固定と、倒して消す", "敵の動き", "戦術札", "大技", "1ランの流れ" }, TutorialContent.Steps.Select(s => s.Name));
            var st = Stage("StageTU8");
            Assert.IsNotNull(st);
            Assert.IsFalse(st.handTestDeck.Any(c => c != null && CopyKinds.IsMoveType(c.type)), "移動の札は無い（よけずに、撃つ敵を倒す）");
            // 固定の照準（チュートリアルの読み込みと同じ：あなたのマスの十字）
            var m = BattleModel.FromStage(st);
            var bomber = m.Enemies.Single(e => e.Kind == EnemyKind.Bomber);
            bomber.BombCells = PhaseRules.BombShape(m, m.Player.Position);
            var before = Threats.Compute(m).Single(t => t.Source == bomber);
            Assert.AreEqual(ThreatKind.Fixed, before.Kind, "実線＝固定");
            var pulled = PlayerPreview.AfterPlanned(m, new List<CommandRecord> { Rec("Pull", Direction.Up) });
            var after = Threats.Compute(pulled).Single(t => t.SourceId == bomber.Id);
            CollectionAssert.AreEquivalent(before.Cells, after.Cells, "引き寄せても固定の赤は動かない");
            Assert.AreNotEqual(bomber.Position, pulled.UnitById(bomber.Id).Position, "砲兵は動いた");
            var killed = PlayerPreview.AfterPlanned(m, new List<CommandRecord> { Rec("Pull", Direction.Up), Rec("Slash", Direction.Up) });
            Assert.IsFalse(Threats.Compute(killed).Any(t => t.SourceId == bomber.Id), "撃つ敵を倒すと、固定の赤も消える");
            var ev = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { Rec("Pull", Direction.Up), Rec("Slash", Direction.Up) }, null, ev, null);
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp, "引き寄せ → 斬撃 で無傷");
            Assert.IsTrue(TutorialContent.Steps.First(s => s.Asset == "StageTU8").Success.Any(l => l.Contains("予告は出ない")), "本編では予告が出ないことを、ここで伝える");
        }

        // ───── ボス：HP 42・段階は 80%（33）・40%（16）。分かりにくい1発で HP の半分は無い ─────

        [Test]
        public void BossHpIsShorterInPhaseOneButPhasesTwoAndThreeKeepTheirLength()
        {
            var st = Stage("StageXC");
            Assert.AreEqual("42,10,10,12", st.enemyHpList);
            Assert.AreEqual(80, CombatBoss.Phase2Pct);
            Assert.AreEqual(40, CombatBoss.Phase3Pct);
            var m = BattleModel.FromStage(st);
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.MirrorLord);
            Assert.AreEqual(33, CombatBoss.Floor(lord, 0), "段階2は HP 33（RC8 と同じ）");
            Assert.AreEqual(16, CombatBoss.Floor(lord, 1), "段階3は HP 16（RC8 と同じ）");
        }

        [Test]
        public void NoSingleThreatTakesHalfYourHpInAnyRoom()
        {
            var rooms = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" }).Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null && s.combatV4 && s.name.StartsWith("StageX")).ToList();
            Assert.Greater(rooms.Count, 10);
            foreach (var st in rooms)
            {
                var m = BattleModel.FromStage(st);
                // 大技を全部「今」にして、1つの攻撃の最大を見る（重ねたときの合計は遊ぶ人が足す）
                foreach (var e in m.Enemies) foreach (var c in e.Clocks) { c.Cd = 1; Clocks.Telegraph(m, e, c); }
                foreach (var t in Threats.Compute(m))
                    Assert.Less(t.Damage, m.Player.MaxHp / 2, $"{st.name}：{t.Name} {t.Damage}（1発で HP の半分以上は無い）");
            }
        }

        // ───── 札：短い文（効果1行・まね1行）。全文は右下の欄 ─────

        [Test]
        public void CardFaceTextIsShort()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToList();
            foreach (var n in ActContent.StartDeck.Distinct())
            {
                var c = cards.First(x => x.name == n);
                Assert.LessOrEqual(UiStyles.ShortText(c).Length, 12, $"{n}：効果は1行（{UiStyles.ShortText(c)}）");
                Assert.LessOrEqual(UiStyles.CopyShort(c).Length, 16, $"{n}：まねは1行（{UiStyles.CopyShort(c)}）");
            }
            foreach (var c in cards) Assert.LessOrEqual((UiStyles.CopyShort(c) ?? "").Length, 17, $"{c.name}：まねは1行（{UiStyles.CopyShort(c)}）");
            var tactics = cards.Where(c => c.type == CommandType.Tactic).ToList();
            Assert.Greater(tactics.Count, 0, "緑の札（戦術札）のアセットがある");
            foreach (var c in tactics) Assert.LessOrEqual(UiStyles.ShortText(c).Length, 16, $"{c.name}：緑の札も札の顔は短く（初見テスト「早鐘の説明文が枠からはみ出す」）：{UiStyles.ShortText(c)}");
            StringAssert.Contains("あなたにも", UiStyles.CopyShort(Card("Slash")), "攻撃のまねはあなたにも当たる（まね返し）を札の顔に残す");
        }

        [Test]
        public void CardDetailFitsTheDetailPanel()
        {
            // 右下の欄（約 500×130px・11px の字で 1行 約 40 字 × 8 行）に、札の全文（自分・まね・説明）が入る
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null && !c.name.StartsWith("Cmd_")).ToList();
            foreach (var c in cards)
            {
                string body = $"自分：{UiStyles.SelfText(c)}\n敵のまね：{UiStyles.CopyText(c)}\n{c.description}" + (string.IsNullOrEmpty(c.copyNote) ? "" : $"\n敵がまねすると：{c.copyNote}");
                int lines = body.Split('\n').Sum(l => Mathf.Max(1, Mathf.CeilToInt(l.Length / 40f)));
                Assert.LessOrEqual(lines, 8, $"{c.name}：右下の欄に入りきらない（{lines} 行）");
            }
        }

        // ───── 順番の帯：実行と同じ順番 ─────

        [Test]
        public void TimelineOrderMatchesTheTurn()
        {
            var order = new[] { TurnManager.TurnStep.You, TurnManager.TurnStep.EnemyAttack, TurnManager.TurnStep.Copy, TurnManager.TurnStep.BigAttack, TurnManager.TurnStep.Move, TurnManager.TurnStep.Floor };
            for (int i = 1; i < order.Length; i++) Assert.Less((int)order[i - 1], (int)order[i], "①あなた → ②攻撃 → ③まね → ④大技 → ⑤移動 → ⑥床");
            Assert.IsTrue(PhaseRules.IntentOrder, "攻撃（赤）はまねの前（本人の判断を変えない）");
        }
    }
}
