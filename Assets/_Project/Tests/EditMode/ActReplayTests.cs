using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】2026-10-05 本人プレイ後の仕上げ：カードの抽選（基本札を出さない）・役職の傾向・旅立ちの選択・地図 v2・
    /// 結果画面の記録とセーブ（版 2）・プロフィール（役職の解放）・罠は回復マス／ワープ陣に置けない・誘い足。
    /// </summary>
    public class ActReplayTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 0) => new CommandRecord(Card(card), d, dist);
        static RoleDef Role(string id) => ActContent.Roles.First(r => r.Id == id);

        [Test]
        public void CardPoolHasNoStarterCardsAndPricesFollowRarity()
        {
            var pool = ActContent.CardPool.Select(c => c.Asset).ToList();
            foreach (var n in ActContent.StartDeck.Distinct()) CollectionAssert.DoesNotContain(pool, n, "基本札は抽選に出さない：" + n);
            CollectionAssert.Contains(pool, "Lure");
            CollectionAssert.Contains(pool, "WallKick");
            CollectionAssert.DoesNotContain(pool, "Sweep", "薙ぎ払いは回転斬りと重なるので外した");
            foreach (var c in ActContent.CardPool)
            {
                Assert.IsNotNull(Card(c.Asset), c.Asset);
                Assert.AreEqual(c.Rarity == Rarity.Common ? 30 : c.Rarity == Rarity.Uncommon ? 45 : 65, c.Price);
            }
            CollectionAssert.DoesNotContain(ActContent.RandomPool.Select(c => c.Asset).ToList(), "MirrorSlash", "反転斬りは写し取り（鏡兵）でだけ");
            Assert.Less(ActContent.CardPool.Min(c => c.Price), ActContent.Item(ItemId.Potion).Price + ActContent.RemovePriceBase, "最初の商人（約50）で何か1つは買える");
        }

        [Test]
        public void RewardsAndShopNeverOfferStarterCards()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var run = new ActRun(ActContent.Roles[seed % ActContent.Roles.Length], seed, Card);
                var room = run.Doors[0];
                run.Enter(room);
                run.WinBattle(new BattleStats { Won = true, Turns = 3, HpBefore = run.Hp, HpAfter = run.Hp });
                foreach (var rc in run.RewardCards.Where(r => !r.IsCopySlot))
                    CollectionAssert.DoesNotContain(ActContent.StartDeck, rc.Card.name, "報酬の抽選に基本札が出た");
            }
        }

        [Test]
        public void EngineerSwapsASlashForATrapAndRolesHaveAFavoredTag()
        {
            var eng = new ActRun(Role("engineer"), 3, Card);
            Assert.AreEqual(12, eng.Deck.Count, "入れ替えなので枚数は同じ");
            Assert.AreEqual(ActContent.StartDeck.Count(n => n == "Slash") - 1, eng.Deck.Count(c => c.name == "Slash")); // 【RC8】最初のデッキの斬撃は2枚（押し斬り2枚）
            Assert.AreEqual(1, eng.Deck.Count(c => c.name == "Trap"));
            foreach (var r in ActContent.Roles.Where(r => r.Id != "standard"))
            {
                Assert.AreNotEqual(CardTag.None, r.Favored, r.Name + " の報酬の傾向");
                Assert.IsFalse(string.IsNullOrEmpty(r.CopyHint), r.Name);
            }
        }

        [Test]
        public void MapV2HasOneDangerOnFloorsFourAndFiveWithASaferDoor()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                var map = ActContent.BuildMap(new System.Random(seed));
                Assert.AreEqual(7, map.Count);
                var normals = map.SelectMany(f => f).Where(r => r.Kind == RoomKind.Battle).ToList();
                CollectionAssert.AreEquivalent(new[] { "thorn", "warp", "spring", "mirror", "hall" }, normals.Select(r => r.Id), "通常戦5部屋が1回ずつ");
                CollectionAssert.IsSubsetOf(map[0].Select(r => r.Id), new[] { "thorn", "spring", "warp" }, "1階に鏡の礼拝堂は出さない");
                Assert.AreEqual(1, map[3].Count(r => r.Kind == RoomKind.Danger));
                Assert.AreEqual(1, map[3].Count(r => r.Kind == RoomKind.Battle), "4階は危険戦を避けられる");
                Assert.AreEqual(1, map[4].Count(r => r.Kind == RoomKind.Danger));
                Assert.AreEqual(1, map[4].Count(r => r.Kind == RoomKind.Event));
                Assert.AreNotEqual(map[3].First(r => r.Kind == RoomKind.Danger).Id, map[4].First(r => r.Kind == RoomKind.Danger).Id, "危険戦は別の部屋");
                CollectionAssert.AreEquivalent(new[] { RoomKind.Rest, RoomKind.Shop }, map[2].Select(r => r.Kind));
                CollectionAssert.AreEquivalent(new[] { RoomKind.Rest, RoomKind.Shop }, map[5].Select(r => r.Kind));
                Assert.AreEqual(RoomKind.Boss, map[6].Single().Kind);
            }
            var a = ActContent.BuildMap(new System.Random(5)).SelectMany(f => f).Select(r => r.Id).ToList();
            var b = ActContent.BuildMap(new System.Random(5)).SelectMany(f => f).Select(r => r.Id).ToList();
            CollectionAssert.AreEqual(a, b, "同じ種なら同じ地図（同じ地図で再挑戦）");
        }

        [Test]
        public void BoonsAreThreeSeededChoicesAndCanBeTakenOnce()
        {
            var run = new ActRun(ActContent.Roles[0], 11, Card);
            run.RollBoons();
            Assert.AreEqual(3, run.BoonOptions.Count);
            var again = new ActRun(ActContent.Roles[0], 11, Card);
            again.RollBoons();
            CollectionAssert.AreEqual(run.BoonOptions.Select(o => o.Kind), again.BoonOptions.Select(o => o.Kind), "種が同じなら同じ候補");
            var mapBefore = again.Map.SelectMany(f => f).Select(r => r.Id).ToList();
            CollectionAssert.AreEqual(mapBefore, new ActRun(ActContent.Roles[0], 11, Card).Map.SelectMany(f => f).Select(r => r.Id), "旅立ちの乱数は地図をずらさない");

            // 種類ごとの効果（いろいろな種で全種類を確かめる）
            var seen = new HashSet<BoonKind>();
            for (int seed = 1; seed < 60 && seen.Count < 5; seed++)
            {
                foreach (var kind in new[] { BoonKind.Card, BoonKind.Imprint, BoonKind.Shards, BoonKind.Supplies, BoonKind.Trim })
                {
                    var r = new ActRun(ActContent.Roles[0], seed, Card);
                    r.RollBoons();
                    var o = r.BoonOptions.FirstOrDefault(x => x.Kind == kind);
                    if (o == null) continue;
                    seen.Add(kind);
                    int deck = r.Deck.Count, shards = r.Shards, maxHp = r.MaxHp;
                    if (kind == BoonKind.Trim) Assert.IsFalse(r.TakeBoon(o), "削除は札を選ばないと受け取れない");
                    Assert.IsTrue(r.TakeBoon(o, 0));
                    Assert.IsTrue(r.BoonTaken);
                    switch (kind)
                    {
                        case BoonKind.Card: Assert.AreEqual(deck + 1, r.Deck.Count); break;
                        case BoonKind.Imprint: Assert.AreEqual(maxHp - ActContent.BoonImprintHpCost, r.MaxHp); Assert.AreEqual(1, r.Imprints.Count); break;
                        case BoonKind.Shards: Assert.AreEqual(shards + ActContent.BoonShards, r.Shards); break;
                        case BoonKind.Supplies: CollectionAssert.AreEquivalent(new[] { ItemId.Potion, ItemId.Hook }, r.Items); break;
                        case BoonKind.Trim: Assert.AreEqual(deck - 1, r.Deck.Count); Assert.AreEqual(shards + ActContent.BoonTrimShards, r.Shards); break;
                    }
                    Assert.IsFalse(r.TakeBoon(o, 0), "2回は受け取れない");
                }
            }
            Assert.AreEqual(5, seen.Count, "5種類とも出る");
            for (int seed = 1; seed < 30; seed++)
            {
                var m = new ActRun(Role("mirrorist"), seed, Card);
                m.RollBoons();
                Assert.IsFalse(m.BoonOptions.Any(o => o.Kind == BoonKind.Supplies), "アイテムを持っている役職には「回復薬と鉤」を出さない（2枠を超える）");
            }
        }

        [Test]
        public void SaveVersion2KeepsTheResultRecordAndRejectsOldSaves()
        {
            var run = new ActRun(ActContent.Roles[1], 21, Card);
            run.RollBoons();
            run.SkipBoon();
            run.Enter(run.Doors[0]);
            run.AddUsedCards(new[] { "斬撃", "斬撃", "一歩" });
            run.AddDamage("反射", 3);
            run.AddDamage("トゲ", 2);
            run.WinBattle(new BattleStats { Won = true, Turns = 3, HpBefore = run.Hp, HpAfter = run.Hp - 5 });
            run.Leave();
            string json = ActSave.ToJson(run);
            Assert.IsTrue(ActSave.TryRestore(json, Card, out var back, out var error), error);
            Assert.AreEqual(2, back.UsedCards["斬撃"]);
            Assert.AreEqual(3, back.DamageBySource["反射"]);
            CollectionAssert.AreEqual(run.HpFlow, back.HpFlow);
            Assert.IsTrue(back.BoonTaken);
            var old = json.Replace($"\"version\": {ActSave.Version}", "\"version\": 1");
            Assert.IsFalse(ActSave.TryRestore(old, Card, out _, out var oldError));
            StringAssert.Contains("前の版", oldError);
            var badItem = json.Replace("\"items\": []", "\"items\": [\"77\"]");
            Assert.IsFalse(ActSave.TryRestore(badItem, Card, out _, out _), "数字だけのアイテム名（定義に無い）は読まない");
        }

        [Test]
        public void ResultTipsPointAtTheMainCauseOfDamage()
        {
            var run = new ActRun(ActContent.Roles[0], 2, Card);
            run.AddDamage("反射", 6);
            run.AddDamage("剣兵の剣", 2);
            var tips = run.ResultTips();
            Assert.LessOrEqual(tips.Count, 2);
            StringAssert.Contains("反射", tips[0]);
            var copyRun = new ActRun(ActContent.Roles[0], 2, Card);
            copyRun.AddDamage("まね", 8);
            copyRun.AddDamage("トゲ", 1);
            StringAssert.Contains("最後の命令", copyRun.ResultTips()[0]);
            Assert.AreEqual(1, new ActRun(ActContent.Roles[0], 2, Card).ResultTips().Count, "何も無くても1行は出す");
        }

        [Test]
        public void ProfileUnlocksRolesWithoutStatBoosts()
        {
            var p = new ActProfileData();
            Assert.IsTrue(ActProfile.IsUnlocked(p, "standard"));
            Assert.IsFalse(ActProfile.IsUnlocked(p, "observer"));
            var run = new ActRun(ActContent.Roles[0], 9, Card);
            var newly = ActProfile.RecordRun(p, run, new[] { "斬撃" });
            CollectionAssert.Contains(newly, Role("observer").Name, "1回終えると観測士");
            Assert.IsFalse(ActProfile.IsUnlocked(p, "commander"));
            Assert.AreEqual(1, p.Record("standard").runs);
            Assert.AreEqual(1, p.Record("standard").bestFloor);
            Assert.IsTrue(ActProfile.TryParse(ActProfile.ToJson(p), out var back));
            CollectionAssert.Contains(back.unlockedRoles, "observer");
            Assert.IsFalse(ActProfile.TryParse("{\"version\": 99}", out _), "知らない版は読まない");
            Assert.IsFalse(ActProfile.TryParse("not json", out _));
            // 同じランを2回記録しても解放は1回
            CollectionAssert.IsEmpty(ActProfile.RecordRun(p, run, null));
        }

        [Test]
        public void TrapCannotBePlacedOnHealTilesOrWarps()
        {
            var heal = BattleModel.FromRows(new[] { "Ph.E." }, 3, 12, 10);
            TurnRules.PlayTurn(heal, new List<CommandRecord> { R("Trap", Direction.Right) }, null);
            Assert.IsFalse(heal.Board.HasSpike(new Vector2Int(1, 0)), "回復マスの上には置けない");
            Assert.IsTrue(heal.Board.HasSpike(new Vector2Int(4, 0)), "敵のコピー（空いた床）には置ける");
            var warp = BattleModel.FromRows(new[] { "Pw.E.", "....w" }, 3, 12, 10);
            var pos = warp.Player.Position;
            TurnRules.PlayTurn(warp, new List<CommandRecord> { R("Trap", Direction.Right) }, null);
            Assert.IsFalse(warp.Board.HasSpike(pos + Vector2Int.right), "ワープ陣の上には置けない");
        }

        [Test]
        public void LureMovesTheEnemyTheOtherWay()
        {
            var lure = Card("Lure");
            Assert.IsNotNull(lure);
            Assert.IsTrue(lure.copyMirrored);
            var m = BattleModel.FromRows(new[] { ".P...E." }, 3, 12, 10);
            var p0 = m.Player.Position; var e0 = m.Enemies.First().Position;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Lure", Direction.Right, 1) }, null);
            Assert.AreEqual(p0 + Vector2Int.right, m.Player.Position);
            Assert.AreEqual(e0 + Vector2Int.left, m.Enemies.First().Position, "敵は反対向き＝寄ってくる");
        }

        [Test]
        public void ReflectMarkMatchesTheRealCopyWhenNothingMovesFirst()
        {
            // 鏡兵：あなたが右へ斬る → 鏡兵は左へ斬り返す（あなたに当たる）。しるしと実際のダメージが同じ
            var m = BattleModel.FromRows(new[] { "PM...", "....." }, 3, 12, 10);
            var slash = R("Slash", Direction.Right);
            var marks = PlayerPreview.Reflections(m, new List<CommandRecord>(), slash);
            Assert.AreEqual(1, marks.Count);
            Assert.IsFalse(marks[0].Uncertain);
            CollectionAssert.Contains(marks[0].Cells, m.Player.Position);
            Assert.Greater(marks[0].DamageToYou, 0);
            int hp = m.Player.Hp;
            TurnRules.PlayTurn(m, new List<CommandRecord> { slash }, null);
            Assert.AreEqual(hp - marks[0].DamageToYou, m.Player.Hp, "反射のしるしと実際の結果が同じ");

            var fresh = BattleModel.FromRows(new[] { "PM...", "....." }, 3, 12, 10);
            CollectionAssert.IsEmpty(PlayerPreview.Reflections(fresh, new List<CommandRecord>(), R("MirrorSlash", Direction.Right)), "反転斬りは反射されない");
            // 先に下へ歩く → 反射の斬撃は元のマス（空）に当たる：しるしは出るがダメージ 0、実際も 0
            var walkFirst = new List<CommandRecord> { R("Walk", Direction.Down, 1) };
            var moved = PlayerPreview.Reflections(fresh, walkFirst, slash);
            Assert.AreEqual(1, moved.Count);
            Assert.AreEqual(0, moved[0].DamageToYou);
            int hp0 = fresh.Player.Hp;
            TurnRules.PlayTurn(fresh, new List<CommandRecord> { walkFirst[0], slash }, null);
            Assert.AreEqual(hp0, fresh.Player.Hp);

            // 鏡守の騎士（攻撃だけ反転）：斬撃は反射、移動は反射しない
            var boss = BattleModel.FromRows(new[] { "PQ...", "....." }, 3, 12, 30);
            var bm = PlayerPreview.Reflections(boss, new List<CommandRecord>(), slash);
            Assert.AreEqual(1, bm.Count);
            CollectionAssert.Contains(bm[0].Cells, boss.Player.Position);
            int bossHp = boss.Player.Hp;
            TurnRules.PlayTurn(boss, new List<CommandRecord> { slash }, null);
            Assert.AreEqual(bossHp - bm[0].DamageToYou, boss.Player.Hp, "騎士の反射も、しるしと実際の結果が同じ（1ターン目は大技のカウントダウン前）");
            var boss2 = BattleModel.FromRows(new[] { "PQ...", "....." }, 3, 12, 30);
            CollectionAssert.IsEmpty(PlayerPreview.Reflections(boss2, new List<CommandRecord>(), R("Walk", Direction.Right, 1)));

            // レビュー（P2）：押し斬りで騎士を押した後の反射・闘気の上乗せ・とどめ も、しるしと実際が同じ
            foreach (var plan in new[]
            {
                new List<CommandRecord>(),
                new List<CommandRecord> { R("Spirit", Direction.Up) },
            })
            foreach (var attack in new[] { R("PushSlash", Direction.Right), R("Slash", Direction.Right) })
            {
                var b = BattleModel.FromRows(new[] { "PQ...", "....." }, 3, 12, 30);
                var marks2 = PlayerPreview.Reflections(b, plan, attack);
                int before = b.Player.Hp;
                var turnCmds = new List<CommandRecord>(plan) { attack };
                TurnRules.PlayCommandsAndCopies(b, turnCmds, null);
                Assert.AreEqual(before - marks2.Sum(x => x.DamageToYou), b.Player.Hp, $"{attack.Command.name}（前に {plan.Count} 命令）");
            }
            var kill = BattleModel.FromRows(new[] { "PM...", "....." }, 3, 12, 3);
            CollectionAssert.IsEmpty(PlayerPreview.Reflections(kill, new List<CommandRecord>(), slash), "とどめを刺した鏡兵は反射しない");
            // 先兵の +1（実行の最初に足される）でちょうど倒せるときも反射しない（レビュー2）
            int slashDamage = Card("Slash").damage;
            var vanguard = BattleModel.FromRows(new[] { "PM...", "....." }, 3, 12, slashDamage + 1);
            Assert.AreEqual(1, PlayerPreview.Reflections(vanguard, new List<CommandRecord>(), slash).Count, "+1 が無ければ生き残って反射する");
            CollectionAssert.IsEmpty(PlayerPreview.Reflections(vanguard, new List<CommandRecord>(), slash, null, 1), "+1 で倒せば反射しない");
        }
    }
}
