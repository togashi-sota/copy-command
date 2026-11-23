using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【通常リリース版 1.0.0（2026-11-16〜）】文化祭のあと、主力作品として仕上げた版の決まり。
    /// 文化祭の物が通常版に出ない・反響深度・反響ランク・報酬の系統・役職の最初の1枚・ボスの鏡の鎧・休息。
    /// </summary>
    public class ReleaseTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");

        static ActRun NewRun(int seed, string role = "standard") => new ActRun(ActContent.Roles.First(x => x.Id == role), seed, Card, true, false, true, true);

        [Test]
        public void TheNormalBuildIsNotTheFestivalBuild()
        {
            Assert.IsFalse(BuildInfo.FestivalBuild, "エディタ・通常版のビルドは文化祭版ではない");
            Assert.IsFalse(BuildInfo.Festival, "起動引数が無ければ文化祭の入口へ行かない");
            Assert.IsFalse(BuildInfo.FestivalKiosk, "展示の PC のタイムアウトは動かない");
            Assert.AreEqual(BuildInfo.ReleaseVersion, BuildInfo.Version, "通常版の版");
            StringAssert.DoesNotContain("仮", BuildInfo.Title, "画面の題名に「仮」を出さない（題名は最後に本人が選ぶ）");
            Assert.AreEqual("COPY COMMAND", BuildInfo.Title, "題名は COPY COMMAND（2026-11-21 本人決定・就活資料と同じ）");
            Assert.AreEqual("自分の命令が、敵の武器になる。", BuildInfo.Catch);
        }

        [Test]
        public void EchoDepthIsACopyRuleNotMoreEnemyHp()
        {
            StringAssert.Contains("まね返し", V4Content.ChallengeText(1));
            StringAssert.DoesNotContain("HP", V4Content.ChallengeText(1), "深度1 は敵の HP を増やさない");
            StringAssert.DoesNotContain("回復", V4Content.ChallengeText(2), "深度は まね返し と 手札だけ（回復の数字はいじらない・役職の能力を消さない）");
            StringAssert.DoesNotContain("回復", V4Content.ChallengeText(3));
            StringAssert.Contains("まね返し +3", V4Content.ChallengeText(3));
        }

        [Test]
        public void DepthThreeEchoesHarderButRestIsTheSame()
        {
            var run = NewRun(7);
            run.Challenge = 3;
            Assert.AreEqual(ActContent.RestHeal, run.RestHealAmount, "深度でも休息は同じ");
            var m = BattleModel.FromStage(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXD.asset"));
            m.ApplyChallenge(3);
            Assert.AreEqual(3, m.EchoBack, "深度3 は まね返し +3");
            var m1 = BattleModel.FromStage(AssetDatabase.LoadAssetAtPath<StageData>("Assets/_Project/Data/Stages/StageXD.asset"));
            m1.ApplyChallenge(1);
            Assert.AreEqual(2, m1.EchoBack, "深度1 は まね返し +2");
        }

        [Test]
        public void UpgradesGiveTheEchoToo()
        {
            Assert.AreEqual(UpgradeKind.CopyUp, V4Content.DefaultUpgrade(Card("Slash")), "【通常リリース版】敵も同じ強さで使う攻撃は、強くすると敵のまねも強くなる");
            Assert.AreEqual(UpgradeKind.Self, V4Content.DefaultUpgrade(Card("ChippedSlash")), "まねが弱い札（欠け斬り）は自分＋（安全の系統）");
        }

        [Test]
        public void CoreTacticsAndImprintsOnly()
        {
            CollectionAssert.DoesNotContain(V4Content.FinalTacticPool.Select(c => c.Asset).ToList(), "Tactic_Focus", "集中（自分で殴るのを強める）は抽選に出ない");
            Assert.AreEqual(1, V4Content.StartTactics.Count(t => t == "Tactic_Focus"), "集中は最初の1枚だけ（チュートリアル7 で教える札）");
            Assert.IsFalse(FinalMeta.UnlockedCards(new ActProfileData()).Contains("Sneak") || V4Content.LockedCards.Contains("Sneak"), "忍び足は最初から");
        }

        [Test]
        public void EchoRankRewardsDamageDoneByEnemies()
        {
            var b = V4Content.EchoRankBounds;
            Assert.AreEqual("S", V4Content.EchoRankOf(b[0]));
            Assert.AreEqual("A", V4Content.EchoRankOf(b[0] - 1));
            Assert.AreEqual("B", V4Content.EchoRankOf(b[2]));
            Assert.AreEqual("C", V4Content.EchoRankOf(b[2] - 1));
            var run = NewRun(11);
            run.SetFloorForTest(run.FloorCount - 1, V4Content.Room("v4_boss"));
            run.WinBattle(new BattleStats { Won = true, Turns = 8, HpBefore = 10, HpAfter = 10, EnemyDamage = 100, EnemyDamageByOthers = 70 });
            Assert.IsTrue(run.Cleared);
            Assert.AreEqual(70, run.EchoSharePct);
            Assert.AreEqual("S", run.EchoRank);
            var p = new ActProfileData();
            FinalMeta.RecordRun(p, run, 25);
            Assert.AreEqual("S", p.finalRecords.First(r => r.roleId == "standard").bestEchoRank, "役職ごとの一番よいランクを残す");
        }

        [Test]
        public void EchoRankSurvivesSaveAndLoad()
        {
            var run = NewRun(12);
            run.SetFloorForTest(0, run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = 10, HpAfter = 10, EnemyDamage = 40, EnemyDamageByOthers = 10 });
            var json = UnityEngine.JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
            Assert.IsTrue(ActSave.TryRestore(json, a => Card(a), out var back, out var err), err);
            Assert.AreEqual(run.EchoSharePct, back.EchoSharePct, "続きからでも反響の割合が残る");
        }

        [Test]
        public void EveryRewardCommandHasAFamily()
        {
            var fams = V4Content.FinalCommandPool.Select(c => V4Content.FamilyOf(c.Asset)).Distinct().OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, fams, "強い攻撃／敵に渡す札／守り・移動 の3系統がそろう");
            Assert.AreEqual(1, V4Content.FamilyOf("CopyBlade"));
            Assert.AreEqual(0, V4Content.FamilyOf("Whirl"));
        }

        [Test]
        public void RolesStartWithADifferentCopyCard()
        {
            var obs = NewRun(3, "observer");
            var com = NewRun(3, "commander");
            var std = NewRun(3, "standard");
            Assert.IsTrue(obs.Deck.Any(c => c.name == "Tailwind"), "観測士：追い風（敵だけ遠くへ）");
            Assert.IsTrue(com.Deck.Any(c => c.name == "Incite"), "指揮官：けしかけ（まねた敵だけが斬る）");
            Assert.AreEqual(std.Deck.Count, obs.Deck.Count, "入れ替え（枚数は同じ）");
            Assert.AreEqual(std.Deck.Count, com.Deck.Count);
        }

        [Test]
        public void TheBossWearsMirrorArmorFromPhaseTwo()
        {
            Assert.Greater(RulesEngine.LordDirectGuard, 0, "段階2・3 の主は、あなたの直接攻撃を減らす（まね・大技・反射・トゲは通る）");
            var lord = new UnitModel(1, "鏡の主", "A", Team.Enemy, 42, UnityEngine.Vector2Int.zero);
            StringAssert.Contains("鏡の鎧", CombatBoss.DescribeV4(lord), "敵の説明に鏡の鎧を書く");
        }

        [Test]
        public void RestIsOneOfThree()
        {
            Assert.AreEqual(1, ActContent.RestPicks, "休息は3つから1つ");
            Assert.AreEqual(1, NewRun(5).RestActions);
        }
    }
}
