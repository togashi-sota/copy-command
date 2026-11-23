using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC12・本人の RC11 実プレイのあと】5分体験の入口（何のゲームか）・段階ヒントと答え・4／5問目は2ターンまで・激化（目安のターンを過ぎると敵の攻撃 +1）。
    /// </summary>
    public class RC12Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord Rec(string name, Direction d) { var c = Card(name); return new CommandRecord(c, d, c.minDistance, new CardInstance(c)); }

        [TearDown]
        public void Cleanup() => RulesEngine.ResetExperiments();

        static Direction Dir(char c) => c == 'U' ? Direction.Up : c == 'D' ? Direction.Down : c == 'L' ? Direction.Left : Direction.Right;

        /// <summary>答え（"Push:R,Walk:D|..."）をそのまま実行する。copy=false ならまね無しの世界。</summary>
        static (TurnOutcome outcome, int hurt, int turns) PlayAnswer(string stage, string answer, bool copy = true)
        {
            var m = BattleModel.FromStage(Stage(stage));
            if (!copy) m.CopyEnabled = false;
            int hp = m.Player.Hp, n = 0;
            var o = TurnOutcome.Continue;
            foreach (var t in answer.Split('|'))
            {
                var list = t.Split(',').Select(x => { var p = x.Trim().Split(':'); return Rec(p[0], Dir(p[1][0])); }).ToList();
                o = TurnRules.PlayTurn(m, list, null, new List<BattleEvent>(), null);
                n++;
                if (o != TurnOutcome.Continue) break;
            }
            return (o, hp - m.Player.Hp, n);
        }

        // ───── 5分体験 ─────

        [Test]
        public void TheFirstTwoMissionsTeachByDoingWithoutJargon()
        {
            // 【RC12・本人「いきなり全まね…初めての人には分からない」】1・2問目は名前を出さない（何をするかだけ）。名前は3問目で
            var q = TutorialContent.QuickSteps;
            foreach (var d in q.Take(1))
                foreach (var text in new[] { d.Mission, d.MissionRule }.Concat(d.Lines))
                    foreach (var word in new[] { "全まね", "技まね", "移動まね" })
                        Assert.IsFalse((text ?? "").Contains(word), $"{d.Name}：「{word}」は2問目から（{text}）");
            Assert.IsTrue(q[1].MissionRule.Contains("全まね"), "【通常リリース版】3問になったので、2問目で名前を教える");
        }

        [Test]
        public void QuickIntroComesBeforeTheFirstMission()
        {
            var src = File.ReadAllText("Assets/_Project/Scripts/Act1/ActRunManager.cs");
            int at = src.IndexOf("public void StartQuick()");
            Assert.Greater(at, 0);
            Assert.Greater(src.IndexOf("QuickIntroOpen = true", at), at, "5分体験の入口の窓（何のゲームか）を MISSION 1 の前に");
            var ui = File.ReadAllText("Assets/_Project/Scripts/BattleUITutorial.cs");
            StringAssert.Contains("あなたの命令を、敵もまねる。", ui);
        }

        [Test]
        public void MissionsThreeToFiveHaveStagedHintsAndAnAnswerThatWorks()
        {
            var q = TutorialContent.QuickSteps;
            for (int i = 1; i < q.Length; i++) // 【通常リリース版】2・3問目（落石・まねの連鎖）
            {
                var d = q[i];
                Assert.AreEqual(3, d.Hints?.Length ?? 0, $"{d.Name}：ヒントは 仕組み → 使う敵 → 最初の1手 の3段");
                Assert.IsFalse(string.IsNullOrEmpty(d.Answer), $"{d.Name}：答えを見られる（詰まって閉じない）");
                var a = PlayAnswer(d.Asset, d.Answer);
                Assert.AreEqual(TurnOutcome.Cleared, a.outcome, $"{d.Name}：答えの手順で解ける");
                Assert.LessOrEqual(a.turns, d.TurnLimit, d.Name);
                if (d.NoDamage) Assert.AreEqual(0, a.hurt, d.Name);
                var off = PlayAnswer(d.Asset, d.Answer, copy: false);
                Assert.AreNotEqual(TurnOutcome.Cleared, off.outcome, $"{d.Name}：まねが無いと同じ手では解けない（まねが答えの中心）");
            }
        }

        [Test]
        public void MissionsThreeToFiveAreForgivingButHaveACleanSolution()
        {
            // 【RC12・本人「4問目で詰まってやめた」】2ターンまで・ダメージあり。1ターン・無傷は「きれいな解」（答えの手順がそれ）
            var q = TutorialContent.QuickSteps;
            foreach (var d in q.Skip(1)) // 【通常リリース版】2・3問目
            {
                Assert.AreEqual(2, d.TurnLimit, d.Name);
                Assert.IsFalse(d.NoDamage, d.Name);
                Assert.AreEqual(1, d.BestTurns, d.Name);
                var a = PlayAnswer(d.Asset, d.Answer);
                Assert.AreEqual(1, a.turns, $"{d.Name}：答えは1ターンのきれいな解");
                Assert.AreEqual(0, a.hurt, d.Name);
            }
        }

        [Test]
        public void TheFinalMissionHasNoBigAttackThatNeverFires()
        {
            // 【RC12・初見テスト「5問目の大技（★）が撃たれないまま終わる：見るだけの物が増える」】5問目は大技なし
            var m = BattleModel.FromStage(Stage(TutorialContent.QuickSteps.First(q => q.Name == "まねの連鎖").Asset)); // 【通常リリース版】順番を変えた（連鎖は4問目）
            Assert.AreEqual(0, m.Enemies.Sum(e => e.Clocks.Count), "5問目の敵は大技を持たない");
        }

        [Test]
        public void TheIntroHasNoCircledNumbersAndTheAnswerHeadingIsGone()
        {
            // 【RC12・初見テスト】入口の3コマの ①②③ は戦闘の順番の帯と食い違う／「（答えは出ない）」は「答えを見る」と食い違う
            var ui = File.ReadAllText("Assets/_Project/Scripts/BattleUITutorial.cs");
            Assert.IsFalse(ui.Contains("\"① あなた"), "入口の絵に①②③を付けない");
            Assert.IsFalse(ui.Contains("（答えは出ない）\")"), "見出しに「答えは出ない」を出さない");
        }

        [Test]
        public void QuickExperienceTextUsesOnlyGlyphsTheFontHas()
        {
            // 【RC12・見直し】「⚠」は字体に無く「□」になった（1問目の行・5問目の窓）。札の顔の⚠は絵で描いている
            foreach (var d in TutorialContent.QuickSteps)
            {
                var all = new List<string> { d.Mission, d.MissionRule, d.Hint, d.Why };
                all.AddRange(d.Lines ?? new string[0]); all.AddRange(d.Success ?? new string[0]); all.AddRange(d.Hints ?? new string[0]);
                foreach (var t in all.Where(x => !string.IsNullOrEmpty(x))) Assert.IsFalse(t.Contains("⚠"), $"{d.Name}：{t}");
            }
        }

        // ───── 難しさと役職 ─────

        [Test]
        public void RolesTradeStabilityForTheirAbilityInsteadOfBeingStrictlyStronger()
        {
            // 【RC12・本人「観測士の方が簡単に感じる」「役職は縦の強さではなく遊び方の違いに」】旅の剣士＝安定・観測士＝情報だが不安定・指揮官＝戦術札が多いが難しい
            CommandData Look(string n) => Card(n) ?? AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{n}.asset");
            ActRun Run(string id) => new ActRun(ActContent.Roles.First(r => r.Id == id), 5, Look, true, false, true, true);
            var std = Run("standard"); var obs = Run("observer"); var cmd = Run("commander");
            Assert.AreEqual(16, V4Content.StartHp, "完成版の最大 HP は 16（RC11 は 20・RC12 は 17・通常リリース版は敵の HP を戻して 16）");
            Assert.AreEqual(16, std.MaxHp);
            Assert.AreEqual(13, obs.MaxHp, "観測士：先が見えるかわりに最大 HP -3");
            Assert.AreEqual(14, cmd.MaxHp, "指揮官：緑の札 2枚のかわりに最大 HP -2");
            Assert.AreEqual(1, std.Role.PostHeal, "旅の剣士だけ、戦闘のあと HP +1");
            Assert.AreEqual(0, obs.Role.PostHeal); Assert.AreEqual(0, cmd.Role.PostHeal);
            StringAssert.Contains("最大 HP -3", obs.Role.Description);
            StringAssert.Contains("最大 HP -2", cmd.Role.Description);
            Assert.AreEqual(6, ActContent.RestHeal, "休息 6 → 5 → 6（通常リリース版：3つから1つ）");
        }

        // ───── 激化 ─────

        [Test]
        public void EscalationAddsToEnemyAttacksOnlyAfterTheParTurn()
        {
            var m = BattleModel.FromStage(Stage("StageX1"));
            int baseMelee = PhaseRules.Melee(m);
            m.EscalateAfter = 3;
            m.Environment.Round = 3;
            Assert.AreEqual(baseMelee, PhaseRules.Melee(m), "目安のターンまでは同じ");
            m.Environment.Round = 4;
            Assert.AreEqual(baseMelee + m.EscalateBonus, PhaseRules.Melee(m), "目安を過ぎると +1");
            var c = m.Clone();
            Assert.AreEqual(m.EscalateAfter, c.EscalateAfter, "読み（複製）でも同じ");
        }

        [Test]
        public void EscalationIsNeverUsedInTheBossRoom()
        {
            bool keep = V4Content.Escalation;
            try
            {
                V4Content.Escalation = true;
                var boss = V4Content.Rooms.First(r => r.Kind == RoomKind.Boss);
                Assert.AreEqual(0, V4Content.EscalateAfterFor(boss), "ボスは自分の段階で強くなる");
                var normal = V4Content.Rooms.First(r => r.Kind == RoomKind.Battle && r.ParTurns > 0);
                Assert.Greater(V4Content.EscalateAfterFor(normal), 0);
            }
            finally { V4Content.Escalation = keep; }
        }
    }
}
