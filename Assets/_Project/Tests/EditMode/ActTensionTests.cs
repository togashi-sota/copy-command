using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【検証中】ACT1 の緊張感（2026-10-05）：ACT1 専用の通常戦 A1〜A4 で「コピーを読まないと被弾する／読めば無傷」の代表的な場面が成り立つこと、
    /// 部屋の並び（易しい部屋から）、速さの報酬、写し取りの「とどめ」、多命令の研究用の切替がゲームでは OFF のこと。
    /// 場面は Act1Tension.RunTraps（1ターン目のコピーの罠）の出力から。
    /// </summary>
    public class ActTensionTests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord R(string card, Direction d, int dist = 0) => new CommandRecord(Card(card), d, dist);

        /// <summary>1ターンだけ遊んで (受けたダメージ, 与えたダメージ)。</summary>
        static (int taken, int dealt) Play(string stage, params CommandRecord[] seq)
        {
            var m = BattleModel.FromStage(Stage(stage));
            int hp = m.Player.Hp, ehp = m.Enemies.Sum(e => e.Hp);
            TurnRules.PlayTurn(m, seq.ToList(), null);
            return (hp - m.Player.Hp, ehp - m.Enemies.Sum(e => e.Hp));
        }

        [Test]
        public void ThornHall_SwordsmanFollowsYourRetreat()
        {
            // 下の剣兵は盤面の端で止まる → あなたの「一歩↑」をコピーして隣に戻ってくる
            var trap = Play("StageA1", R("Dash", Direction.Down, 2), R("Slash", Direction.Right), R("Walk", Direction.Up, 1));
            Assert.AreEqual(3, trap.taken, "斬ったあとの一歩もコピーされ、剣兵が隣に来る");
            var safe = Play("StageA1", R("Dash", Direction.Down, 2), R("Rush", Direction.Right, 3), R("Slash", Direction.Right));
            Assert.AreEqual(0, safe.taken);
            Assert.Greater(safe.dealt, 0);
        }

        [Test]
        public void WarpHall_ArcherBehindShootsWhenYouEndFacingUp()
        {
            Assert.AreEqual(2, Play("StageA2", R("Walk", Direction.Up, 1)).taken, "最後に上を向く → 真後ろの弓兵も上を向いて撃つ");
            Assert.AreEqual(0, Play("StageA2", R("Walk", Direction.Left, 1)).taken, "横を向いて終われば撃たれない");
        }

        [Test]
        public void Spring_LeftSpearmanStabsWhenYouEndFacingRight()
        {
            // 左の槍兵は同じ行の2マス左：右を向いて終わると、左の槍兵も右を向いて突く
            Assert.AreEqual(3, Play("StageA3", R("Walk", Direction.Right, 1)).taken, "最後に右を向く → 左の槍兵が背中を刺す");
            Assert.AreEqual(0, Play("StageA3", R("Walk", Direction.Up, 1)).taken, "縦に終われば届かない");
            var trap = Play("StageA3", R("Walk", Direction.Left, 1), R("Slash", Direction.Left), R("Dash", Direction.Right, 2));
            Assert.AreEqual(3, trap.taken, "左の槍兵を斬ってから右へ逃げると、逃げた向きで刺される");
            var safe = Play("StageA3", R("Walk", Direction.Left, 1), R("Slash", Direction.Left), R("Thrust", Direction.Left));
            Assert.AreEqual(0, safe.taken);
            Assert.AreEqual(7, safe.dealt);
        }

        [Test]
        public void MirrorChapel_CopiedPushSlashComesBack()
        {
            // 2026-10-05 柱2本の盤面（mirror_w2）：右へ突進して下りると、剣兵はあなたの移動を追い、同じ「押し斬り←」を横から返す
            var trap = Play("StageA4", R("Rush", Direction.Right, 3), R("Dash", Direction.Down, 2), R("PushSlash", Direction.Left));
            Assert.AreEqual(3, trap.taken);
            var safe = Play("StageA4", R("Rush", Direction.Left, 3), R("Dash", Direction.Up, 2), R("Slash", Direction.Up));
            Assert.AreEqual(0, safe.taken);
            Assert.Greater(safe.dealt, 0);
        }

        [Test]
        public void Boss_ReflectedThrustUnlessYouStepAside()
        {
            // ACT1 のボス（柱2本＋回復陣＋トゲ）：上へ斬って最後も上へ突くと、騎士の反射（下向き）が返る。斬ったら横へ逃げれば無傷
            var trap = Play("StageAB", R("Walk", Direction.Up, 1), R("Slash", Direction.Up), R("Thrust", Direction.Up));
            Assert.AreEqual(3, trap.taken, "反射された突き");
            var safe = Play("StageAB", R("Walk", Direction.Up, 1), R("Slash", Direction.Up), R("Dash", Direction.Left, 2));
            Assert.AreEqual(0, safe.taken);
            Assert.Greater(safe.dealt, 0);
        }

        [Test]
        public void NormalRoomsUseTheActStagesAndGoFromEasyToHard()
        {
            foreach (var room in ActContent.Rooms.Where(r => r.Stage != null))
                Assert.IsNotNull(Stage(room.Stage), room.Stage);
            for (int seed = 0; seed < 30; seed++)
            {
                var map = ActContent.BuildMap(new System.Random(seed));
                var normals = map.SelectMany(f => f).Where(r => r.Kind == RoomKind.Battle).ToList();
                CollectionAssert.AreEquivalent(new[] { "thorn", "warp", "spring", "mirror", "hall" }, normals.Select(r => r.Id), "通常戦5部屋が1回ずつ");
                // 2026-10-05 地図 v2：1階は {棘・泉・転移}（鏡の礼拝堂は1階に出さない）。細かい形は ActReplayTests
                CollectionAssert.IsSubsetOf(map[0].Select(r => r.Id), new[] { "thorn", "spring", "warp" }, "1階は易しい部屋");
            }
        }

        [Test]
        public void ParTurnsPayASmallBonusOnlyWhenFast()
        {
            var run = new ActRun(ActContent.Roles[0], 3, Card);
            var room = run.Doors[0];
            Assert.Greater(room.ParTurns, 0);
            run.Enter(room);
            int before = run.Shards;
            run.WinBattle(new BattleStats { Won = true, Turns = room.ParTurns, HpBefore = 20, HpAfter = 20 });
            Assert.AreEqual(before + ActContent.ShardsFor(RoomKind.Battle) + ActContent.ParBonus + ActContent.NoDamageBonus, run.Shards);
            Assert.AreEqual(1, run.ParBonuses);
            run.Leave();
            var slow = run.Doors[0];
            run.Enter(slow);
            before = run.Shards;
            run.WinBattle(new BattleStats { Won = true, Turns = slow.ParTurns + 1, HpBefore = 20, HpAfter = 20 });
            Assert.AreEqual(before + ActContent.ShardsFor(RoomKind.Battle) + ActContent.NoDamageBonus, run.Shards, "遅くても損はしない（報酬が増えないだけ）");
        }

        [Test]
        public void FriendlyKillsCountOnlyFinishingBlowsByEnemies()
        {
            var m = BattleModel.FromRows(new[] { "E.E", "...", ".P." }, 3, 12, 3);
            var a = m.Enemies.First(); var b = m.Enemies.Last();
            var events = new List<BattleEvent>();
            RulesEngine.ApplyDamage(m, b, null, true, a, 2, DamageKind.Direct, events);   // 同士討ち（とどめではない）
            RulesEngine.ApplyDamage(m, m.Player, null, false, a, 1, DamageKind.Direct, events); // あなたがとどめ
            RulesEngine.ApplyDamage(m, a, null, true, b, 3, DamageKind.Trait, events);   // 敵がとどめ
            var s = BattleStats.From(events, true, 3, 12, 12);
            Assert.AreEqual(2, s.FriendlyFire);
            Assert.AreEqual(1, s.FriendlyKills);
            var run = new ActRun(ActContent.Roles[0], 5, Card);
            run.Enter(run.Doors[0]);
            run.WinBattle(new BattleStats { Won = true, Turns = 3, HpBefore = 20, HpAfter = 20, FriendlyFire = 4 });
            Assert.IsFalse(run.RewardCards.Any(c => c.IsCopySlot && c.Reason.Contains("同士討ち")), "同士討ちしただけでは写し取りに出ない");
        }

        [Test]
        public void ChippedSlashIsWeakWhenCopiedAndLungeIsShortWhenCopied()
        {
            // 欠け斬り：あなたは 4、背後の敵のコピーは 1（普通の斬撃なら 4）
            var m = BattleModel.FromRows(new[] { ".E.", ".P.", ".E." }, 3, 12, 10);
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("ChippedSlash", Direction.Up) }, null);
            Assert.AreEqual(6, m.Enemies.First().Hp, "あなたの欠け斬りは 4");
            Assert.AreEqual(11, m.Player.Hp, "背後の敵のコピーは威力 1");
            // 踏み込み：あなたは2マス、敵は1マス → 間合いが1マス詰まる
            var k = BattleModel.FromRows(new[] { "...", ".E.", "...", "...", "...", ".P." }, 3, 12, 10);
            var e0 = k.Enemies.First().Position;
            var p0 = k.Player.Position;
            TurnRules.PlayTurn(k, new List<CommandRecord> { R("Lunge", Direction.Up, 2) }, null);
            Assert.AreEqual(p0.y + 2, k.Player.Position.y, "あなたは2マス");
            Assert.AreEqual(e0.y + 1, k.Enemies.First().Position.y, "敵は1マスだけ");
            Assert.AreEqual((e0.y - p0.y) - 1, k.Enemies.First().Position.y - k.Player.Position.y, "間合いが1マス詰まる");
            CollectionAssert.Contains(ActContent.CardPool.Select(c => c.Asset).ToList(), "ChippedSlash");
            CollectionAssert.DoesNotContain(ActContent.CardPool.Select(c => c.Asset).ToList(), "ReverseCopy", "逆さ写しは報酬から外した");
        }

        [Test]
        public void ShopAlwaysSellsAPotionAndRestHealsMore()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var run = new ActRun(ActContent.Roles[0], seed, Card);
                while (!run.Doors.Any(r => r.Kind == RoomKind.Shop)) { run.Enter(run.Doors[0]); run.Leave(); }
                run.Enter(run.Doors.First(r => r.Kind == RoomKind.Shop));
                Assert.AreEqual(2, run.ShopItems.Count);
                Assert.IsTrue(run.ShopItems.Any(i => i.Id == ItemId.Potion), "回復薬は必ず並ぶ");
            }
            Assert.AreEqual(6, ActContent.RestHeal); // 【通常リリース版】3つから1つにしたので 5 → 6 // 【RC12】6 → 5（最大 HP 17）【RC11・本人「簡単すぎた」】8 → 6（敵の HP ×1.15・戦闘のあとの回復 0 と合わせて）
            Assert.AreEqual(44, ActContent.Room("boss").EnemyHp, "ACT1 のボスは HP 44（2026-10-05 RC：38→44。ステージ VB は 55 のまま）");
        }

        [Test]
        public void MultiCommandExperimentsAreOffInTheGame()
        {
            TurnRules.ExperimentMultiRule = 2;
            RulesEngine.ResetExperiments();
            Assert.AreEqual(0, TurnRules.ExperimentMultiRule);
            Assert.IsFalse(ActContent.RewardImprints.Contains(ImprintId.FourthCommand), "第四命令は報酬・商人に出ない（指揮官だけ）");
            // 研究用の切替（案B）：4命令のラウンドだけ兵種の行動 +1
            try
            {
                TurnRules.ExperimentMultiRule = 1;
                var m = BattleModel.FromRows(new[] { ".S.", ".P.", "..." }, 3, 12, 12);
                m.ApplyStage(null);
                var w = R("Walk", Direction.Left, 1);
                TurnRules.PlayTurn(m, new List<CommandRecord> { w, R("Walk", Direction.Right, 1), R("Walk", Direction.Left, 1), R("Walk", Direction.Right, 1) }, null);
                Assert.AreEqual(12 - 4, m.Player.Hp, "剣 3 ＋ 案B の +1");
            }
            finally { RulesEngine.ResetExperiments(); }
        }
    }
}
