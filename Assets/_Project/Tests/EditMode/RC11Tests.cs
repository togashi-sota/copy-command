using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC11・本人の RC10 実プレイのあと】例外・ボス撃破＝勝ち・赤と紫・5分体験のパズル・緑の札の使いどき・強化は札ごとに1つ・眠る写し身は石の像・無傷の記録。
    /// </summary>
    public class RC11Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord Rec(string name, Direction d) { var c = Card(name); return new CommandRecord(c, d, c.minDistance, new CardInstance(c)); }

        [TearDown]
        public void Cleanup() => RulesEngine.ResetExperiments();

        // ───── 例外（本人の RC10 プレイで 70 回）─────

        [Test]
        public void DirectionArrowLoopNeverReadsTheCommandAfterChoosing()
        {
            // 矢印のボタンを押すと ChooseDirection が pendingCommand を null にする。同じループの続きで pendingCommand を読むと落ちた
            Assert.IsFalse(BattleUI.WhiffMoveCard(null), "押した直後（null）でも落ちない");
            Assert.IsTrue(BattleUI.WhiffMoveCard(Card("Walk")));
            Assert.IsFalse(BattleUI.WhiffMoveCard(Card("Slash")));
            string src = File.ReadAllText("Assets/_Project/Scripts/BattleUIShowcase.cs");
            int a = src.IndexOf("var cmdNow = pendingCommand;");
            int b = src.IndexOf("if (pressed) { ChooseDirection(d); break; }");
            int c = src.IndexOf("DrawPlayerDangerV2(CellRect, ToGui, true)");
            Assert.That(a >= 0 && b > a && c > b, "矢印のループは、押す前に手元へ写し・押したら抜ける");
            Assert.IsFalse(src.Substring(a + 30, c - a - 30).Contains("pendingCommand."), "ループの中で pendingCommand を読まない");
        }

        [Test]
        public void TutorialAndQuickModeNeverShowTheVictoryPanel()
        {
            // 【RC11・撮影で見つけた】MISSION の窓の行を挟んだせいで else がずれ、5分体験の「できた！」の横に「勝利！ 報酬へ」が出ていた
            string src = File.ReadAllText("Assets/_Project/Scripts/BattleUIShowcase.cs");
            int a = src.IndexOf("if (InTutorialUi) DrawTutorialDoneModal(bw, W);");
            Assert.GreaterOrEqual(a, 0);
            string next = src.Substring(src.IndexOf('\n', a) + 1).TrimStart();
            StringAssert.StartsWith("else DrawResult(", next, "チュートリアルの「できた！」と勝ちの窓は、どちらか一方");
        }

        // ───── ボス・中ボス：本体を倒したら勝ち ─────

        static void Kill(BattleModel m, UnitModel u) => RulesEngine.ApplyDamage(m, m.Player, null, false, u, 999, DamageKind.Direct, new List<BattleEvent>());

        [Test]
        public void DefeatingTheMirrorLordEndsTheBattleAndCollapsesEveryoneElse()
        {
            var m = BattleModel.FromStage(Stage("StageXC"));
            var lord = CombatBoss.Find(m);
            lord.BossStage = 2; lord.Hp = 3;
            var ev = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, m.Player, null, false, lord, 10, DamageKind.Direct, ev);
            Assert.IsFalse(lord.IsAlive);
            Assert.IsTrue(m.AllEnemiesDead, "剣兵・写し身も崩れる（雑魚の片付けは無い）");
            Assert.IsTrue(PuzzleRules.Won(m));
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.LeaderDown), "撃破の演出の合図");
        }

        [Test]
        public void DefeatingAMidBossAlsoEndsTheBattle()
        {
            foreach (var st in new[] { "StageX9", "StageXA", "StageXB" })
            {
                var m = BattleModel.FromStage(Stage(st));
                var leader = m.Enemies.First(e => PhaseRules.IsLeader(e));
                Assume.That(m.Enemies.Count(e => e.IsAlive && !e.IsObject) >= 2, st);
                var ev = new List<BattleEvent>();
                RulesEngine.ApplyDamage(m, m.Player, null, false, leader, 999, DamageKind.Direct, ev);
                Assert.IsTrue(m.AllEnemiesDead, $"{st}：「○○を倒せ」の○○を倒したら、護衛も逃げて勝ち");
                Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.Died && e.Text == "崩れた"), st);
            }
        }

        [Test]
        public void OrdinaryRoomsStillNeedEveryEnemy()
        {
            var m = BattleModel.FromStage(Stage("StageX1"));
            var first = m.Enemies.First(e => e.IsAlive && !e.IsObject);
            Kill(m, first);
            Assert.IsFalse(m.AllEnemiesDead, "ふつうの部屋は全員を倒す");
        }

        // ───── 赤と紫：色は種類（赤＝敵と動く・紫＝床に固定）─────

        [Test]
        public void RedMovesWithTheEnemyAndPurpleIsFixed()
        {
            var m = BattleModel.FromStage(Stage("StageX3")); // 盾の陰の砲兵（砲兵2・盾兵）
            var bomber = m.Enemies.First(e => e.Kind == EnemyKind.Bomber);
            bomber.BombCells = PhaseRules.BombShape(m, m.Player.Position); // 照準はターンの終わりに決まる：ここでは あなたへ
            var bomb = Threats.Compute(m).First(t => t.Source == bomber && t.Timing == ThreatTiming.Attack);
            var q3 = BattleModel.FromStage(Stage("StageQM6")); // 5分体験3問目：剣兵の赤と槍兵の紫★
            var sword = Threats.Compute(q3).First(t => t.Source != null && t.Source.Kind == EnemyKind.Swordsman && t.Timing == ThreatTiming.Attack);
            var lance = Threats.Compute(q3).First(t => t.Source != null && t.Source.Kind == EnemyKind.Spearman && t.Timing == ThreatTiming.BigAttack);
            Assert.AreEqual(ThreatKind.Fixed, lance.Kind, "大技は床に固定（紫）");
            Assert.AreEqual(ThreatKind.Fixed, bomb.Kind, "砲兵の爆撃は床に固定（紫）");
            Assert.AreEqual(ThreatKind.Follow, sword.Kind, "剣兵の攻撃は敵と動く（赤）");
            Assert.IsTrue(BattleUI.IsPurple(bomb) && !BattleUI.IsPurple(sword), "色は種類で決める");
            var boss = BattleModel.FromStage(Stage("StageXC"));
            foreach (var t in Threats.Compute(boss).Where(t => t.Source != null && t.Source.Kind == EnemyKind.MirrorLord)) Assert.IsTrue(BattleUI.IsPurple(t), "ボスの光柱・大技も紫（固定）");
            string src = File.ReadAllText("Assets/_Project/Scripts/BattleUICombat.cs");
            Assert.IsTrue(src.Contains("var list = t.Kind == ThreatKind.Fixed ?"), "盤面の塗りも種類で");
        }

        // ───── 5分体験 ─────

        static (TurnOutcome outcome, int hurt, BattleModel m) Play(string stage, params string[][] turns)
        {
            var m = BattleModel.FromStage(Stage(stage));
            int hp = m.Player.Hp;
            var o = TurnOutcome.Continue;
            foreach (var t in turns)
            {
                var list = t.Select(x => { var p = x.Split(':'); return Rec(p[0], p[1] == "U" ? Direction.Up : p[1] == "D" ? Direction.Down : p[1] == "L" ? Direction.Left : Direction.Right); }).ToList();
                o = TurnRules.PlayTurn(m, list, null, new List<BattleEvent>(), null);
                if (o != TurnOutcome.Continue) break;
            }
            return (o, hp - m.Player.Hp, m);
        }

        [Test]
        public void FiveMinuteExperienceGetsHarderAndShowsAMissionFirst()
        {
            var q = TutorialContent.QuickSteps;
            Assert.AreEqual(3, q.Length, "【通常リリース版】まねだけの3問");
            for (int i = 0; i < q.Length; i++)
            {
                Assert.AreEqual(i + 1, q[i].Stars, $"{i + 1}問目は ★{i + 1}");
                Assert.IsFalse(string.IsNullOrEmpty(q[i].Mission), "始める前に MISSION（画面の真ん中）");
                Assert.IsNotNull(Stage(q[i].Asset), q[i].Asset);
            }
            for (int i = 1; i < q.Length; i++)
            {
                Assert.AreEqual(TutorialGoal.Targets, q[i].Goal, "2〜3問目は自分で解くパズル（【通常リリース版】3問）");
                Assert.IsTrue(q[i].TurnLimit > 0); // 【RC12】4・5問目は2ターンまで・ダメージあり（1ターン・無傷は「きれいな解」）
                Assert.IsFalse(string.IsNullOrEmpty(q[i].Hint), "2回失敗したらヒント");
            }
            Assert.AreEqual(1, TutorialContent.QuickPreviewSteps, "答えが見えるのは1問目だけ");
        }

        [Test]
        public void QuickPuzzlesHaveTheIntendedSolutionsAndTheNaiveTryFails()
        {
            // 3問目（【RC11・初見テストで作り直し】まねが答えの中心）：剣兵を押して赤を兵D へ・兵A（全まね）は まねの一歩で槍の紫★の線へ
            // 【RC12・初見テスト「3問目が崖」】引き寄せを外した QM11：↓← と歩いて剣兵A を ← へ押す（赤が兵B へ）・兵C（全まね）は ↓← をなぞって紫★の線へ
            var a = Play("StageQM11", new[] { "Walk:D", "Walk:L", "Push:L" });
            Assert.AreEqual(TurnOutcome.Cleared, a.outcome); Assert.AreEqual(0, a.hurt);
            var a2 = Play("StageQM11", new[] { "Push:L", "Walk:D", "Walk:L" });
            Assert.AreNotEqual(TurnOutcome.Cleared, a2.outcome, "順番が違うと剣兵A を押せない（兵B が赤に入らない）");
            var ma = BattleModel.FromStage(Stage("StageQM11"));
            Assert.AreEqual(1, ma.Enemies.Count(e => e.Clocks.Any(k => k.Telegraphed)), "ready=D：槍兵の大技だけが始めから①");            // 4問目（【RC11・2回目の初見テストで小さく】1ターン・兵2体）：あなたは兵A にぶつかって止まり、兵A は端で止まる → 2体が別々の岩の影へ
            var b = Play("StageQM8", new[] { "Dash:U", "Walk:D", "Walk:L" });
            Assert.AreEqual(TurnOutcome.Cleared, b.outcome); Assert.AreEqual(0, b.hurt);
            var b2 = Play("StageQM8", new[] { "Walk:D", "Walk:L", "Dash:U" });
            Assert.AreNotEqual(TurnOutcome.Cleared, b2.outcome, "順番が違うと止まる場所が変わる");
            // 5問目（【RC12】まねの連鎖 QM10）：あなたの攻撃は全部空振り。兵B のまねの斬撃が兵A を、まねの一歩で剣兵C の真上へ → 剣兵C のまねの斬撃2回で兵B
            var c = Play("StageQM10", new[] { "Slash:U", "Slash:U", "Walk:L" });
            Assert.AreEqual(TurnOutcome.Cleared, c.outcome); Assert.AreEqual(0, c.hurt);
            var c2 = Play("StageQM10", new[] { "Walk:L", "Slash:U", "Slash:U" });
            Assert.AreNotEqual(TurnOutcome.Cleared, c2.outcome, "順番が違うと連鎖しない");
        }

        [Test]
        public void StillEnemiesOnlyMoveByCopying()
        {
            var m = BattleModel.FromStage(Stage("StageQM8"));
            Assert.IsTrue(m.NoPositioning, "落石のパズル：敵は自分では近づかない");
        }

        // ───── 緑の札：使いどき ─────

        [Test]
        public void EveryTacticTellsWhatItDoesAndWhenToUseIt()
        {
            foreach (TacticKind k in System.Enum.GetValues(typeof(TacticKind)))
            {
                if (k == TacticKind.None) continue;
                Assert.IsFalse(string.IsNullOrEmpty(TacticRules.Short(k)), $"{k}：効果");
                Assert.IsFalse(string.IsNullOrEmpty(TacticRules.When(k)), $"{k}：使いどき");
                Assert.LessOrEqual(TacticRules.When(k).Length, 9, $"{k}：使いどきは短く（{TacticRules.When(k)}）");
            }
            StringAssert.Contains("まねで動く敵", TacticRules.Short(TacticKind.Pursuit), "「追撃命令」のような名前だけでは分からなかった → 効果の言葉を同じ形に");
        }

        // ───── 強化：札ごとに1つ・前と後が見える ─────

        [Test]
        public void EachCardHasOneUpgradeShownBeforeAndAfter()
        {
            Assert.IsTrue(V4Content.SingleUpgrade);
            var slash = Card("Slash");
            Assert.AreEqual(UpgradeKind.CopyUp, V4Content.DefaultUpgrade(slash), "【通常リリース版】敵も同じ強さで使う攻撃は まね＋（強くするほど敵にも渡る）");
            StringAssert.Contains($"あなた {slash.damage}→{slash.damage + DeckState.CopyUpSelf}", V4Content.UpgradePreview(slash), "強化の前と後を見せる");
            var chipped = Card("ChippedSlash");
            Assert.AreEqual(UpgradeKind.Self, V4Content.DefaultUpgrade(chipped), "まねが弱い札（欠け斬り）は自分＋");
            StringAssert.Contains($"{chipped.damage}ダメ → {chipped.damage + DeckState.ForgeBonus}ダメ", V4Content.UpgradePreview(chipped));
            Assert.IsFalse(V4Content.CanUpgradeAny(Card("Walk")), "移動の札は強化しない");
        }

        // ───── 見た目 ─────

        [Test]
        public void SleepingClonesAreSolidStoneStatues()
        {
            foreach (var f in new[] { "down", "up", "left", "right" })
                Assert.IsTrue(File.Exists($"Assets/_Project/Art/Generated/unit_statue_{f}.png"), f);
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/_Project/Art/Generated/unit_statue_down.png"));
            int solid = tex.GetPixels32().Count(p => p.a > 240), any = tex.GetPixels32().Count(p => p.a > 20);
            Object.DestroyImmediate(tex);
            Assert.Greater(solid, any * 0.85f, "半透明ではない（本人「半透明だから通れると思った」）");
        }

        [Test]
        public void BossPhasesAnnounceTheirRuleWithoutTheAnswer()
        {
            var titles = Enumerable.Range(0, 3).Select(BattleUI.PhaseIntroTitle).ToList();
            Assert.AreEqual(3, titles.Distinct().Count());
            var m = BattleModel.FromStage(Stage("StageXC"));
            var lord = CombatBoss.Find(m);
            for (int s = 0; s < 3; s++)
            {
                lord.BossStage = s;
                string rule = BattleUI.PhaseIntroRule(lord);
                Assert.IsFalse(string.IsNullOrEmpty(rule));
                Assert.IsFalse(rule.Contains("マスへ") || rule.Contains("を使え"), "答え（どこへ・何を使う）は書かない");
            }
        }

        // ───── 難しさ：危険の量を増やす（一撃の重さは上げない）─────

        [Test]
        public void OnlyOneBigAttackPerTurnInTheWholeRoom()
        {
            // 1ターンに 10 以上受けたターンの多くが「別々の敵の大技が2つ重なった」だった（Logs/rc11_bighit_base.txt）
            Assert.IsTrue(Clocks.OneAcrossEnemies);
            var m = BattleModel.FromStage(Stage("StageXD")); // 鏡の歩兵（剣兵2）
            var two = m.Enemies.Where(e => e.Clocks.Count > 0).Take(2).ToList();
            Assert.AreEqual(2, two.Count);
            foreach (var e in two) { e.Clocks[0].Cd = 1; e.Clocks[0].Cells = new List<Vector2Int>(); }
            foreach (var e in two) Clocks.TelegraphDue(m, e, new List<BattleEvent>());
            Assert.AreEqual(1, two.Count(e => e.Clocks[0].Telegraphed), "2体目は1ターン待つ");
            Assert.AreEqual(2, two[1].Clocks[0].Cd);
        }

        [Test]
        public void OneTurnNeverDealsMoreThanEight()
        {
            // 仕様9「10〜13ダメの即死を無くす」：どんなに重なっても 1ターン 8 まで（あふれた分は防いだと記録に出る）
            Assert.AreEqual(8, RulesEngine.PlayerTurnCap);
            var m = BattleModel.FromStage(Stage("StageX9"));
            m.BeginRound();
            var ev = new List<BattleEvent>();
            var knight = m.Enemies.First();
            int a = RulesEngine.ApplyDamage(m, knight, null, false, m.Player, 5, DamageKind.Clock, ev);
            int b = RulesEngine.ApplyDamage(m, knight, null, false, m.Player, 5, DamageKind.Direct, ev);
            Assert.AreEqual(5, a); Assert.AreEqual(3, b, "8 をこえる分は防ぐ");
            Assert.AreEqual(12, m.Player.Hp);
            Assert.IsTrue(ev.Any(e => e.Text != null && e.Text.Contains("上限")), "防いだことが記録に出る");
            m.BeginRound();
            Assert.AreEqual(5, RulesEngine.ApplyDamage(m, knight, null, false, m.Player, 5, DamageKind.Clock, ev), "次のターンは また 8 まで");
        }

        [Test]
        public void DifficultyComesFromDangerQuantityNotBurst()
        {
            Assert.AreEqual(6, ActContent.RestHeal, "休息 8 → 6 → 5（RC12）→ 6（通常リリース版：3つから1つ）");
            Assert.AreEqual(0, V4Content.PostBattleHeal, "戦闘のあとの回復 1 → 0（旅の剣士の +1 は残る）");
            Assert.AreEqual(40, BattleModel.FromStage(Stage("StageX9")).Enemies.Max(e => e.MaxHp), "処刑騎士 40 → 46（RC11 ×1.15）→ 52（RC12 ×1.13）→ 40（通常リリース版：敵の HP の水増しを戻し、難しさは あなたの最大 HP で合わせる）");
            Assert.AreEqual(42, BattleModel.FromStage(Stage("StageXC")).Enemies.Max(e => e.MaxHp), "ボスは HP 42 のまま（長くなるだけ：RC10）");
        }

        // ───── 無傷の記録 ─────

        [Test]
        public void NoDamageWinsAreCounted()
        {
            var src = File.ReadAllText("Assets/_Project/Scripts/Act1/ActRun.cs");
            StringAssert.Contains("NoDamageWins++", src);
            StringAssert.Contains("noDamageWins", File.ReadAllText("Assets/_Project/Scripts/Act1/ActSaveData.cs"), "途中のセーブにも残る");
        }

        // ───── 【RC11・最後の見直し（画像だけ・初見／採用担当／QA／5分体験）】 ─────

        [Test]
        public void HowToTurnOrderSaysPurpleAlsoFiresInStepTwo()
        {
            var mi = typeof(ActUI).GetMethod("FinalHowToLines", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mi);
            var lines = (string[])mi.Invoke(null, null);
            var order = lines.First(l => l.Contains("1ターンの順番"));
            StringAssert.Contains("②敵の攻撃（赤＝敵と動く・紫＝床に固定", order, "紫の爆撃・光柱は②で起きる（QA：紫＝④と読めた）");
            StringAssert.Contains("④★の大技", order);
            Assert.IsTrue(lines.Any(l => l.Contains("紫（斜線）は床に固定（押しても動かない・撃つ敵を倒せば消える）")), "倒せば消えるのは紫の全部");
        }

        [Test]
        public void BossStageTwoObjectiveShowsTheNextThresholdAndNoStaleClones()
        {
            var mi = typeof(BattleUI).GetMethod("BossObjective", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(mi);
            var m = BattleModel.FromStage(Stage("StageXC"));
            var lord = CombatBoss.Find(m);
            lord.BossStage = 1; lord.StunRounds = 0;
            int floor = CombatBoss.Floor(lord, 1);
            foreach (var c in m.Enemies.Where(e => e.Kind == EnemyKind.HealClone)) { c.Dormant = false; c.Hp = 5; }
            string withClones = (string)mi.Invoke(null, new object[] { m, lord });
            StringAssert.Contains($"HP {floor} で段階3", withClones);
            foreach (var c in m.Enemies.Where(e => e.Kind == EnemyKind.HealClone)) c.Hp = 0;
            string noClones = (string)mi.Invoke(null, new object[] { m, lord });
            StringAssert.Contains("写し身はもういない", noClones, "写し身がいないのに「写し身は鏡…」のままだった");
            StringAssert.Contains($"HP {floor} で段階3", noClones);
        }

        [Test]
        public void WinningInTheMiddleOfYourCommandsClearsTheCommandSlots()
        {
            var src = File.ReadAllText("Assets/_Project/Scripts/TurnManager.cs");
            int at = src.IndexOf("bool CheckBattleEnd()");
            Assert.Greater(at, 0);
            int lost = src.IndexOf("return true;", at);
            Assert.Greater(src.IndexOf("plannedCommands.Clear();", at), 0);
            Assert.Less(src.IndexOf("plannedCommands.Clear();", at), lost, "勝ったら命令の欄を空にする（主を倒した勝利の窓の横に「斬撃↑」が残っていた）");
        }

        [Test]
        public void TitleButtonsNeverHideUnderTheRoleRecords()
        {
            // 【RC11・ブラウザ版の確認】中断のセーブがあると「設定（音量）」「ゲームを終了」が役職の記録の欄（h − 168）の下に隠れていた
            float h = UiStyles.RefHeight;
            foreach (bool save in new[] { false, true })
            {
                float top = ActUI.TitleButtonsTop(h, save);
                Assert.LessOrEqual(top + ActUI.TitleButtonsHeight(save), h - 168f, $"セーブ {save}：ボタンの列が記録の欄に届く");
                Assert.GreaterOrEqual(top, h * 0.10f + 88f + 48f, $"セーブ {save}：説明の2行と重なる");
            }
            Assert.AreEqual(h * 0.38f, ActUI.TitleButtonsTop(h, false), 0.01f, "セーブが無いときは前と同じ位置");
        }

        [Test]
        public void QuickMissionsNameTheTargetsAndTheLastOneHasATwoStepHint()
        {
            var q = TutorialContent.QuickSteps;
            foreach (var d in q.Where(d => d.Goal == TutorialGoal.Targets))
                Assert.IsFalse((d.Mission ?? "").Contains("兵2体"), $"{d.Name}：槍兵・剣兵も「兵」に見える → 記号で（兵A・兵D）");
            Assert.IsFalse(q.Any(d => (d.Hint ?? "").Contains("先に動くと届かない")), "3問目のヒントが正しい手順と逆に読めた");
            Assert.GreaterOrEqual(q[q.Length - 1].Hints?.Length ?? 0, 2, "5問目は崖：ヒントを段階に（【RC12】3段＋答え）");
        }

    }
}
