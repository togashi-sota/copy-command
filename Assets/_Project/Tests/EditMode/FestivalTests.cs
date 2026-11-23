using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭クエスト（LEVEL 1〜3 の1クエスト）：部屋・きれいな解（数ターンの戦闘で S）・まねの必要・ランク（3つ＋PERFECT）・
    /// 記録（お手本は書かない・個人の情報なし・LEVEL ごとのランキング＝ランク → タイム）・本編と分かれていること・最初からやり直すと全部はじめに戻ること。
    /// </summary>
    public class FestivalTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord Rec(string name, Direction d) { var c = Card(name); return new CommandRecord(c, d, c.minDistance, new CardInstance(c)); }
        static Direction Dir(char c) => c == 'U' ? Direction.Up : c == 'D' ? Direction.Down : c == 'L' ? Direction.Left : Direction.Right;

        [TearDown]
        public void Cleanup() { RulesEngine.ResetExperiments(); FestivalStats.DirOverride = null; FestivalStats.Enabled = true; FestivalStats.Invalidate(); }

        /// <summary>解をそのまま実行（ゲームと同じ TurnRules）。敵を利用した攻撃を FestivalRun と同じ数え方で。</summary>
        static (TurnOutcome outcome, int hurt, int turns, FestivalRun run) Play(string stage, string solution, int hp, bool copy = true)
        {
            var s = Object.Instantiate(Stage(stage));
            s.playerMaxHp = hp;
            var m = BattleModel.FromStage(s);
            if (!copy) m.CopyEnabled = false;
            int start = m.Player.Hp, n = 0;
            var run = new FestivalRun();
            var o = TurnOutcome.Continue;
            foreach (var t in solution.Split('|'))
            {
                var list = t.Split(',').Select(x => { var p = x.Trim().Split(':'); return Rec(p[0], Dir(p[1][0])); }).ToList();
                var ev = new List<BattleEvent>();
                o = TurnRules.PlayTurn(m, list, null, ev, null);
                foreach (var e in ev)
                    if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && !e.Unit.IsObject && e.Amount > 0) run.CountEnemyHit(e.Cause);
                n++;
                if (o != TurnOutcome.Continue) break;
            }
            Object.DestroyImmediate(s);
            run.Turns = n; run.Damage = start - m.Player.Hp; run.Finished = o == TurnOutcome.Cleared;
            return (o, start - m.Player.Hp, n, run);
        }

        [Test]
        public void ThreeLevelsEachIsOneQuestWithSeveralEnemies()
        {
            Assert.AreEqual(3, FestivalContent.Levels.Length, "LEVEL 1〜3");
            for (int i = 0; i < 3; i++)
            {
                var lv = FestivalContent.Levels[i];
                Assert.AreEqual($"LEVEL {i + 1}", lv.Label);
                Assert.IsNotNull(lv.Battle, lv.Label);
                var st = Stage(lv.Battle.Asset);
                Assert.IsNotNull(st, $"{lv.Battle.Asset} が無い（run-setup が必要）");
                var m = BattleModel.FromStage(st);
                Assert.GreaterOrEqual(m.Enemies.Count(e => !e.IsObject), 3, $"{lv.Label}：敵は3体以上（普通の戦闘）");
            }
            Assert.IsTrue(FestivalContent.Levels[2].Battle.Boss, "LEVEL 3 はミニボス");
        }

        [Test]
        public void CleanSolutionIsASeveralTurnBattleThatReachesRankS()
        {
            // きれいな解（敵を利用する）は 数ターンかかる普通の戦闘で、ランク S に届く（1〜2ターンで終わる問題ではない）
            foreach (var lv in FestivalContent.Levels)
            {
                var b = lv.Battle;
                Assert.IsFalse(string.IsNullOrEmpty(b.Solution), lv.Label);
                var r = Play(b.Asset, b.Solution, lv.Hp);
                Assert.AreEqual(TurnOutcome.Cleared, r.outcome, $"{lv.Label}：きれいな解で勝てる");
                Assert.GreaterOrEqual(r.turns, 3, $"{lv.Label}：1〜2ターンで終わる問題ではない");
                Assert.AreEqual("S", FestivalRank.Rank(lv, r.run), $"{lv.Label}：敵を利用 {r.run.EnemyHits}（目標 {lv.EnemyUseGoal}）・被ダメ {r.hurt}（目安 {lv.DamageLimit}）・{r.turns}ターン（目安 {lv.ParTurns}）");
                Assert.Greater(r.run.EnemyHits, r.turns, $"{lv.Label}：毎ターン1回以上 敵を利用している");
            }
        }

        [Test]
        public void WithoutCopyTheSameMovesDoNotWin()
        {
            // まね（コピー）が無い世界では、同じ手順では勝てない（＝まねが勝ち筋の中心）
            foreach (var lv in FestivalContent.Levels)
            {
                var r = Play(lv.Battle.Asset, lv.Battle.Solution, lv.Hp, copy: false);
                Assert.AreNotEqual(TurnOutcome.Cleared, r.outcome, $"{lv.Label}：まね無しでは同じ手で勝てない");
            }
        }

        [Test]
        public void RankCountsThreeThingsPerfectNeedsNoDamageAndFirstTry()
        {
            var def = FestivalContent.Levels[0];
            var r = new FestivalRun { Finished = true, Turns = def.ParTurns, Damage = 0, CopyHits = def.EnemyUseGoal, Attempt = 1 };
            Assert.AreEqual("S", FestivalRank.Rank(def, r));
            Assert.IsTrue(FestivalRank.Perfect(def, r), "S・ノーダメージ・1回目");
            r.Attempt = 2; Assert.IsFalse(FestivalRank.Perfect(def, r), "倒れた・やり直したあとは PERFECT にならない");
            r.Attempt = 1; r.Damage = 1; Assert.IsFalse(FestivalRank.Perfect(def, r), "ダメージを受けたら PERFECT にならない");
            r.Damage = def.DamageLimit + 1; Assert.AreEqual("A", FestivalRank.Rank(def, r));
            r.Turns = def.ParTurns + 1; Assert.AreEqual("B", FestivalRank.Rank(def, r));
            r.CopyHits = 0; Assert.AreEqual("C", FestivalRank.Rank(def, r));
            // あなたの直接の斬撃は「敵を利用した攻撃」に入らない。まね・敵の攻撃・落石・大技は入る
            var player = new UnitModel(0, "あなた", "P", Team.Player, 20, Vector2Int.zero);
            var enemy = new UnitModel(1, "兵", "E", Team.Enemy, 4, Vector2Int.one);
            var run = new FestivalRun();
            run.CountEnemyHit(new DamageCause { Source = player, Kind = DamageKind.Direct });
            Assert.AreEqual(0, run.EnemyHits);
            run.CountEnemyHit(new DamageCause { Source = enemy, Kind = DamageKind.Direct, ByCopy = true });
            run.CountEnemyHit(new DamageCause { Source = enemy, Kind = DamageKind.Trait });
            run.CountEnemyHit(new DamageCause { Source = null, Kind = DamageKind.Fire });
            run.CountEnemyHit(new DamageCause { Source = enemy, Kind = DamageKind.Clock });
            Assert.AreEqual(1, run.CopyHits); Assert.AreEqual(1, run.FriendlyHits); Assert.AreEqual(1, run.HazardHits); Assert.AreEqual(1, run.BigHits);
        }

        static FestivalRun Done(int level, float sec, string result = "clear", bool demo = false, int attempt = 1) =>
            new FestivalRun { Level = level, StartedAt = 0f, EndedAt = sec, Finished = result == "clear", Abandoned = result != "clear", Result = result, Demo = demo, Attempt = attempt, Turns = 5 };

        [Test]
        public void StatsCsvHasNoPersonalDataAndRankingIsPerLevelByRankThenTurnsThenTime()
        {
            string dir = Path.Combine(Path.GetTempPath(), "festival_test_" + System.Guid.NewGuid().ToString("N"));
            FestivalStats.DirOverride = dir;
            FestivalStats.Enabled = true;
            FestivalStats.Invalidate();
            var now = new System.DateTime(2026, 11, 20, 13, 0, 0);
            FestivalStats.Append(Done(0, 200f), "A", false, now);
            FestivalStats.Append(Done(0, 250f), "S", false, now.AddMinutes(5));
            FestivalStats.Append(Done(0, 150f), "B", false, now.AddMinutes(6));
            FestivalStats.Append(Done(0, 30f, "gameover"), "-", false, now.AddMinutes(9));
            FestivalStats.Append(Done(0, 20f, "restart"), "-", false, now.AddMinutes(10));
            FestivalStats.Append(Done(1, 100f), "S", true, now.AddDays(-1));
            FestivalStats.Append(Done(1, 90f), "S", false, now);
            FestivalStats.Invalidate(); // 読み直しでも同じ
            var top = FestivalStats.TodayTop(0, now, 3);
            Assert.AreEqual(3, top.Count, "クリアだけ（倒れた・やり直しは入らない）");
            Assert.AreEqual("S", top[0].Rank, "ランクが上が先");
            Assert.AreEqual("A", top[1].Rank);
            Assert.AreEqual("B", top[2].Rank, "B は速くても A の後");
            Assert.AreEqual(1, FestivalStats.TodayTop(1, now, 3).Count, "LEVEL ごと・昨日の記録は入らない");
            Assert.AreEqual(1, FestivalStats.PlaceOf(0, now, "S", 5, 100f), "S・同じターンで一番速ければ 1位");
            Assert.AreEqual(1, FestivalStats.PlaceOf(0, now, "S", 4, 900f), "ターンが少なければ 遅くても上（手順を覚えた速さより、敵を使った少ないターン）");
            Assert.AreEqual(3, FestivalStats.PlaceOf(0, now, "A", 5, 300f));
            var lines = File.ReadAllLines(FestivalStats.CsvPath);
            Assert.AreEqual(8, lines.Length, "見出し＋7回");
            Assert.AreEqual(FestivalStats.Header, lines[0]);
            foreach (var bad in new[] { "name", "名前", "user", "mail" }) StringAssert.DoesNotContain(bad, lines[0], "個人の情報の列は無い");
            StringAssert.Contains(",gameover,", lines[4]);
            StringAssert.Contains(",restart,", lines[5]);
            // スタッフの「本日のランキングを消す」：記録の行は残し、ランキングだけ新しく
            FestivalStats.ResetTodayBest(now.AddMinutes(11));
            Assert.IsNull(FestivalStats.TodayBest(0, now.AddMinutes(12)));
            Assert.AreEqual(8, File.ReadAllLines(FestivalStats.CsvPath).Length);
            var sum = FestivalStats.Summary(now, true);
            StringAssert.Contains("挑戦 6回", sum[0]);
            Directory.Delete(dir, true);
        }

        [Test]
        public void DemoAttemptsAreNeverRecorded()
        {
            string dir = Path.Combine(Path.GetTempPath(), "festival_test_" + System.Guid.NewGuid().ToString("N"));
            FestivalStats.DirOverride = dir; FestivalStats.Enabled = true; FestivalStats.Invalidate();
            var now = new System.DateTime(2026, 11, 21, 10, 0, 0);
            FestivalStats.Append(Done(2, 60f, demo: true), "S", true, now);
            FestivalStats.Append(Done(2, 10f, "gameover", demo: true), "-", false, now);
            Assert.IsFalse(File.Exists(FestivalStats.CsvPath), "お手本は CSV に書かない");
            Assert.AreEqual(0, FestivalStats.TodayTop(2, now, 3).Count, "お手本はランキングに入らない");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        [Test]
        public void FestivalTextsAreShortAndNeverShowTheAnswer()
        {
            foreach (var lv in FestivalContent.Levels)
            {
                var b = lv.Battle;
                Assert.IsTrue(string.IsNullOrEmpty(b.Answer), $"{lv.Label}：答えのボタンは出さない（Solution は撮影とテストだけ）");
                Assert.IsFalse(b.Preview, $"{lv.Label}：結果の予告は出さない（本編と同じ）");
                Assert.LessOrEqual(lv.Rule.Length, 36, $"{lv.Label}：特殊ルールは1つ・短く（{lv.Rule}）");
                Assert.LessOrEqual(lv.Goal.Length, 12, $"{lv.Label}：目的は短く（{lv.Goal}）");
                Assert.AreEqual(3, b.Hints.Length, $"{lv.Label}：ヒントは3段");
                Assert.LessOrEqual(b.Lines.Length, 3, lv.Label);
                foreach (var text in new[] { lv.Rule, lv.Goal, lv.Quest, b.Mission }.Concat(b.Lines).Concat(b.Hints).Concat(b.Success ?? new string[0]))
                    foreach (var glyph in new[] { "⚠", "⏱", "①②③" })
                        Assert.IsFalse(text != null && text.Contains(glyph), $"{lv.Label}：字が □ になる記号（{glyph}）");
            }
            Assert.IsNull(FestivalContent.Levels[0].LateHint, "強いヒントは LEVEL 3 だけ");
            Assert.IsFalse(string.IsNullOrEmpty(FestivalContent.Levels[2].LateHint), "LEVEL 3 は長く止まったら強いヒント");
        }

        [Test]
        public void FestivalIsSeparateFromTheMainGameAndRestartResetsEverything()
        {
            var src = File.ReadAllText("Assets/_Project/Scripts/Act1/ActRunManager.Festival.cs");
            // 文化祭は本編のラン・プロフィールに書かない（Run は null のまま・SaveProfile を呼ばない）
            StringAssert.DoesNotContain("SaveProfile", src);
            StringAssert.DoesNotContain("RecordRun", src);
            StringAssert.Contains("Run = null;", src);
            // 「次の人へ」は文化祭の状態を消して入口へ（記録は残す＝FestivalStats を消さない）
            int at = src.IndexOf("public void FestivalNextPerson()");
            Assert.Greater(at, 0);
            StringAssert.DoesNotContain("File.Delete", src);
            Assert.Greater(src.IndexOf("OpenFestival();", at), at);
            // 最初からやり直す：盤面（LoadTutorialStep）・記録（新しい FestivalRun＝ターン・被ダメ・タイム）・ヒント を全部はじめに
            int b = src.IndexOf("void BeginFestivalAttempt(");
            Assert.Greater(b, 0);
            string body = src.Substring(b, src.IndexOf("public void FestivalBeginPlay()") - b);
            foreach (var must in new[] { "LoadTutorialStep();", "HintLevel = 0", "Festival = new FestivalRun", "StartedAt = Time.unscaledTime", "FestivalGameOverOpen = false" })
                StringAssert.Contains(must, body, $"最初からやり直すと {must}");
            // 戦闘中に勝手に戻らない：先に確認（まだ遊んでいますか？）・お手本中は戻さない
            StringAssert.Contains("FestivalIdlePrompt = true", src);
            StringAssert.Contains("if (FestivalDemo) { FestivalIdlePrompt = false; return; }", src);
            Assert.Greater(ActRunManager.IdleBattleSeconds, 60f);
        }

        [Test]
        public void ClockKeepTrimsBossBigAttacks()
        {
            // bigs=1：中ボスの大技は先頭の1つだけ（旧版の砲王＝小砲撃。部屋は残している）
            var m = BattleModel.FromStage(Stage("StageFN3"));
            var king = m.Enemies.First(e => e.Kind == EnemyKind.CannonKing);
            Assert.AreEqual(1, king.Clocks.Count);
            Assert.AreEqual("小砲撃", king.Clocks[0].Name);
        }

        [Test]
        public void NextPersonStartsTheMainGameAsFirstTimeOnlyInTheFestivalBuild()
        {
            // 【2026-11-14 本人「リセットしたら本編も最初からに。1人クリアしたら、次の人が最初からできないのはかわいそう」】
            // はじめての状態：ラン・クリアの記録・チュートリアルの既読・ヒントを消す。設定（音量・画面の揺れ）は残す
            var old = new ActProfileData { finalRuns = 3, finalFinished = 2, finalClears = 1, tutorialOffered = true, tutorialDone = true, volMaster = 80, volBgm = 10, volSe = 70, volSet = true, shakeOff = true };
            old.hintsSeen.Add("x");
            var p = ActRunManager.FirstLaunchProfile(old);
            Assert.AreEqual(0, p.finalRuns); Assert.AreEqual(0, p.finalFinished); Assert.AreEqual(0, p.finalClears);
            Assert.IsFalse(p.tutorialOffered); Assert.IsFalse(p.tutorialDone); Assert.AreEqual(0, p.hintsSeen.Count);
            Assert.AreEqual(80, p.volMaster); Assert.AreEqual(10, p.volBgm); Assert.AreEqual(70, p.volSe); Assert.IsTrue(p.volSet); Assert.IsTrue(p.shakeOff);
            // 呼ぶ所：次の人へ（操作が無くて入口へ も同じ）・本編へ進むとき・本編のランが終わってタイトルへ
            var fest = File.ReadAllText("Assets/_Project/Scripts/Act1/ActRunManager.Festival.cs");
            int a = fest.IndexOf("public void FestivalNextPerson()");
            Assert.Greater(fest.IndexOf("FestivalResetMainGame();", a), a);
            int b = fest.IndexOf("public void FestivalToMain()");
            Assert.Greater(fest.IndexOf("FestivalResetMainGame();", b), b);
            var main = File.ReadAllText("Assets/_Project/Scripts/Act1/ActRunManager.cs");
            int c = main.IndexOf("public void FestivalResetMainGame()");
            Assert.Greater(c, 0);
            StringAssert.Contains("if (!BuildInfo.Festival) return;", main.Substring(c, 160), "通常版（Unity・公開版）では本編のプロフィールを消さない");
            StringAssert.Contains("if (BuildInfo.Festival) festivalFreshMainPending = true;", main);
            Assert.IsFalse(BuildInfo.Festival, "テスト（Editor）は通常版");
        }
    }
}
