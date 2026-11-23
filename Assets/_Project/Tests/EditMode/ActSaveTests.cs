using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>【検証中】ACT1 の中断セーブ（JSON）・コピー操作の検証カード・プレイ記録。</summary>
    public class ActSaveTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");

        static ActRun AdvancedRun()
        {
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "mirrorist"), 4242, Card);
            run.Enter(run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 4, HpBefore = run.Hp, HpAfter = run.Hp - 5 });
            run.TakeRewardCard(run.RewardCards[0]);
            run.Leave();
            run.Imprints.Add(ImprintId.Echo);
            run.AddShards(40);
            return run;
        }

        [Test]
        public void SaveRoundTripKeepsTheRun()
        {
            var run = AdvancedRun();
            string json = ActSave.ToJson(run);
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var error), error);
            Assert.AreEqual(run.Role.Id, back.Role.Id);
            Assert.AreEqual(run.Seed, back.Seed);
            Assert.AreEqual(run.Floor, back.Floor);
            Assert.AreEqual(run.Hp, back.Hp);
            Assert.AreEqual(run.MaxHp, back.MaxHp);
            Assert.AreEqual(run.Shards, back.Shards);
            Assert.AreEqual(run.RemovePrice, back.RemovePrice);
            CollectionAssert.AreEqual(run.Deck.Select(c => c.name), back.Deck.Select(c => c.name));
            CollectionAssert.AreEqual(run.Items, back.Items);
            CollectionAssert.AreEqual(run.Imprints, back.Imprints);
            CollectionAssert.AreEqual(run.Map.Select(f => string.Join(",", f.Select(r => r.Id))), back.Map.Select(f => string.Join(",", f.Select(r => r.Id))), "地図は種から同じ");
            Assert.AreEqual(run.BattlesWon, back.BattlesWon);
        }

        [Test]
        public void BrokenSavesAreRejected()
        {
            Assert.IsFalse(ActSave.TryRestore("{ 壊れた", Card, out _, out var e1));
            Assert.IsNotNull(e1);
            var run = AdvancedRun();
            string ok = ActSave.ToJson(run);
            Assert.IsFalse(ActSave.TryRestore(ok.Replace("\"Slash\"", "\"NoSuchCard\""), Card, out _, out var e2), "知らないカード");
            StringAssert.Contains("知らないカード", e2);
            Assert.IsFalse(ActSave.TryRestore(ok.Replace($"\"version\": {ActSave.Version}", "\"version\": 99"), Card, out _, out _), "版が違う");
            var d = JsonUtility.FromJson<ActSaveData>(ok);
            d.hp = d.maxHp + 5;
            Assert.IsFalse(ActSave.TryRestore(JsonUtility.ToJson(d), Card, out _, out _), "HP がおかしい");
            d = JsonUtility.FromJson<ActSaveData>(ok);
            d.floor = 99;
            Assert.IsFalse(ActSave.TryRestore(JsonUtility.ToJson(d), Card, out _, out _), "階がおかしい");
            d = JsonUtility.FromJson<ActSaveData>(ok);
            d.items.Add("Potion"); d.items.Add("Potion"); d.items.Add("Potion");
            Assert.IsFalse(ActSave.TryRestore(JsonUtility.ToJson(d), Card, out _, out _), "アイテムが多すぎる");
        }

        [Test]
        public void SaveFileIsWrittenAndABrokenFileIsMovedAside()
        {
            string dir = Path.Combine(Path.GetTempPath(), "copycommand_save_test");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "act1_save.json");
            try
            {
                var run = AdvancedRun();
                Assert.IsTrue(ActSave.Save(run, path));
                Assert.IsTrue(ActSave.TryLoad(Card, out var back, out var error, path), error);
                Assert.AreEqual(run.Hp, back.Hp);
                Assert.IsNotNull(ActSave.Describe(path));
                File.WriteAllText(path, "壊れたファイル");
                Assert.IsFalse(ActSave.TryLoad(Card, out _, out _, path));
                Assert.IsFalse(File.Exists(path), "壊れたセーブは元の場所に残さない");
                Assert.IsTrue(File.Exists(path + ".broken"), "退避する（消さない）");
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void JournalRecordsDoorsRewardsAndSkips()
        {
            var run = AdvancedRun();
            Assert.IsTrue(run.Journal.Any(l => l.Contains("避けた扉")));
            Assert.IsTrue(run.Journal.Any(l => l.StartsWith("- 報酬")));
            run.Enter(run.Doors[0]);
            if (run.Current.Kind == RoomKind.Battle || run.Current.Kind == RoomKind.Danger)
            {
                run.WinBattle(new BattleStats { Won = true, Turns = 3, HpBefore = run.Hp, HpAfter = run.Hp });
                run.Leave();
                Assert.IsTrue(run.Journal.Any(l => l.Contains("スキップ")));
            }
        }

        [Test]
        public void MirrorSlashIsCopiedBackwardsAndReverseCopyFlipsTheOrder()
        {
            var walk = Card("Walk");
            var mirror = Card("MirrorSlash");
            var rev = Card("ReverseCopy");
            Assert.IsTrue(mirror.copyMirrored);
            var copied = EnemyReplayController.ToCopied(new[] { new CommandRecord(mirror, Direction.Up, 0) });
            Assert.AreEqual(Direction.Down, copied[0].Direction, "反転斬り：敵は反対向き");
            copied = EnemyReplayController.ToCopied(new[] { new CommandRecord(walk, Direction.Up, 1), new CommandRecord(rev, Direction.Up, 0), new CommandRecord(walk, Direction.Left, 1) });
            Assert.AreEqual(Direction.Left, copied[0].Direction, "逆さ写し：最後の命令から");
            Assert.AreEqual(Direction.Up, copied[2].Direction);
            // ルール：背後（下）の敵の斬撃は反対（下）へ＝あなたに当たらない。普通の斬撃なら上へ＝あなたに当たる
            var m = BattleModel.FromRows(new[] { "...", ".P.", ".E." }, 3, 12, 10);
            m.ApplyStage(null);
            TurnRules.PlayTurn(m, new[] { new CommandRecord(mirror, Direction.Up, 0) }.ToList(), null);
            Assert.AreEqual(12, m.Player.Hp, "背後の敵の斬撃は外れる");
            var normal = BattleModel.FromRows(new[] { "...", ".P.", ".E." }, 3, 12, 10);
            normal.ApplyStage(null);
            TurnRules.PlayTurn(normal, new[] { new CommandRecord(Card("Slash"), Direction.Up, 0) }.ToList(), null);
            Assert.Less(normal.Player.Hp, 12, "普通の斬撃なら背後の敵に斬られる");
            // 正面の敵を斬ると、反転した斬撃が返ってくる（本作の「方向攻撃の法則」の逆）
            var front = BattleModel.FromRows(new[] { ".E.", ".P.", "..." }, 3, 12, 10);
            front.ApplyStage(null);
            TurnRules.PlayTurn(front, new[] { new CommandRecord(mirror, Direction.Up, 0) }.ToList(), null);
            Assert.AreEqual(7, front.Enemies.First().Hp);
            Assert.AreEqual(9, front.Player.Hp, "正面の敵は斬り返す");
        }
    }
}
