using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC3・2026-10-21 本人の初プレイ】チュートリアル（進む・やめる・遊び直す）・チュートリアルの部屋が教える物が本当に起きるか・音量の保存。
    /// </summary>
    public class RC3Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord R(string card, Direction d) => new CommandRecord(Card(card), d, 1);
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
        }

        (ActRunManager act, TurnManager turn) NewGame()
        {
            var root = new GameObject("RC3");
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

        // ───── チュートリアル：順に・やめる・遊び直す（【RC4】8つ：7つの部屋＋1ランの流れ） ─────

        [Test]
        public void TutorialGoesThroughTheRoomsAndTheRunLoopThenFinishes()
        {
            var (act, turn) = NewGame();
            Assert.IsFalse(act.Profile.tutorialDone);
            act.StartTutorial();
            for (int i = 0; i < TutorialContent.Count - 1; i++)
            {
                Assert.IsTrue(act.InTutorial);
                Assert.AreEqual(ActScreen.Battle, act.Screen);
                Assert.AreEqual(i, act.TutorialStep);
                Assert.AreEqual(TutorialContent.Steps[i].Asset, turn.CurrentStage.name, "部屋は順番どおり");
                Assert.IsNull(act.Run, "チュートリアルはランではない（記録もセーブもしない）");
                act.NextTutorialStep();
            }
            // 【RC4】最後は戦闘をしない「1ランの流れ」：報酬 → 扉 → 8階の地図
            Assert.AreEqual(ActScreen.TutorialRun, act.Screen);
            Assert.AreEqual(0, act.TutorialRunPage);
            act.TutorialRunPickCard(1);
            Assert.AreEqual(1, act.TutorialRunCard);
            act.TutorialRunNext();
            act.TutorialRunPickDoor(0);
            act.TutorialRunNext();
            Assert.AreEqual(2, act.TutorialRunPage);
            Assert.IsNull(act.Run, "見本の札・扉は本編に何も残さない");
            act.TutorialRunNext();
            Assert.IsFalse(act.InTutorial);
            Assert.AreEqual(ActScreen.TutorialDone, act.Screen, "完了の画面（本編を始める／タイトルへ）");
            Assert.IsTrue(act.Profile.tutorialDone);
            Assert.IsFalse(act.Profile.tacticIntroSeen, "【RC4】本編ではじめて緑の札を持った戦闘で「おさらい」を1回出す");
        }

        [Test]
        public void TutorialCanBeSkippedAndReplayedFromTheTitle()
        {
            var (act, turn) = NewGame();
            act.StartTutorial();
            act.NextTutorialStep();
            act.NextTutorialStep();
            Assert.AreEqual(2, act.TutorialStep);
            act.SkipTutorial();
            Assert.IsFalse(act.InTutorial);
            Assert.AreEqual(ActScreen.Title, act.Screen, "やめたらタイトルへ");
            Assert.IsTrue(act.Profile.tutorialOffered, "はじめての案内はもう出さない");
            Assert.IsFalse(act.Profile.tutorialDone);
            act.StartTutorial(); // タイトルの「チュートリアル」から遊び直す
            Assert.AreEqual(0, act.TutorialStep, "はじめから");
            Assert.AreEqual("StageTU1", turn.CurrentStage.name);
            act.OpenTitle();
            Assert.IsFalse(act.InTutorial, "タイトルへ戻ればチュートリアルの状態は残らない");
        }

        [Test]
        public void TutorialRoomsHaveNoBigAttacksExceptTheLastAndTacticOnlyInStepFive()
        {
            foreach (var def in TutorialContent.Steps.Where(d => d.Asset != null))
            {
                var s = Stage(def.Asset);
                Assert.IsNotNull(s, def.Asset + "（run-setup で作る）");
                var m = BattleModel.FromStage(s);
                bool clocks = m.Enemies.Any(e => e.Clocks.Count > 0);
                Assert.AreEqual(def.Goal == TutorialGoal.BigAttack, clocks, "大技は大技の段階だけ（" + def.Name + "）");
                Assert.AreEqual(def.Goal == TutorialGoal.Tactic, def.Tactics.Length > 0, "戦術札は戦術札の段階だけ（" + def.Name + "）");
            }
        }

        // ───── チュートリアルの部屋が、教える物を本当に起こす（ルールの側で確かめる） ─────

        [Test]
        public void TutorialAttackHasNoRetaliation()
        {
            var m = BattleModel.FromStage(Stage("StageTU2"));
            var e = m.Enemies.Single();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Up), R("Slash", Direction.Up) }, null, new List<BattleEvent>(), null);
            Assert.IsFalse(e.IsAlive, "斬撃 4 で倒れる");
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp, "攻撃しても、反撃も見えないダメージも無い");
        }

        [Test]
        public void TutorialCopyMakesEnemiesHitEachOther()
        {
            var m = BattleModel.FromStage(Stage("StageTU3"));
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null, events, null);
            Assert.IsTrue(events.Any(ev => ev.Type == BattleEventType.Damaged && ev.Unit != null && !ev.Unit.IsPlayer && ev.Amount > 0 && ev.Cause != null && ev.Cause.ByCopy),
                "誰もいない方へ斬る → まねた敵が隣の敵を斬る");
            Assert.AreEqual(m.Player.MaxHp, m.Player.Hp);
        }

        [Test]
        public void TutorialRedPreviewHitsOnlyIfYouStay()
        {
            var stay = BattleModel.FromStage(Stage("StageTU4"));
            Assert.IsTrue(ThreatMap.EnemyAttack(stay).Contains(stay.Player.Position), "はじめは赤の上");
            TurnRules.PlayTurn(stay, new List<CommandRecord> { R("Slash", Direction.Up) }, null, new List<BattleEvent>(), null);
            Assert.Less(stay.Player.Hp, stay.Player.MaxHp, "赤に残れば当たる");

            var move = BattleModel.FromStage(Stage("StageTU4"));
            TurnRules.PlayTurn(move, new List<CommandRecord> { R("Walk", Direction.Left) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(move.Player.MaxHp, move.Player.Hp, "赤くないマスへ動けば当たらない（剣兵がまねで動いても、赤は変わらない）");

            // 【RC8】動かずに、押し出しで剣兵を押す → 剣兵の赤もずれて、あなたに当たらない（チュートリアル4のやること）
            var push = BattleModel.FromStage(Stage("StageTU4"));
            var sw = push.Enemies.First(e => e.IsAlive);
            var before = sw.Position;
            TurnRules.PlayTurn(push, new List<CommandRecord> { R("Push", Direction.Up) }, null, new List<BattleEvent>(), null);
            Assert.AreNotEqual(before, sw.Position, "剣兵は押されて動いた");
            Assert.AreEqual(push.Player.MaxHp, push.Player.Hp, "押して赤をずらせば、動かなくても当たらない");
            Assert.IsFalse(Stage("StageTU4").handTestDeck.Any(c => c != null && CopyKinds.IsMoveType(c.type)), "移動の札は無い（逃げずにずらす練習）");
        }

        [Test]
        public void TutorialBigAttackHitsTheEnemyYouWalkedIntoIt()
        {
            var m = BattleModel.FromStage(Stage("StageTU6"));
            m.NoPositioning = true; // 【RC4】ActRunManager と同じ：大技の段階は敵が自分で動かない
            Assert.IsEmpty(TurnForecast.Compute(m, new List<CommandRecord>()).OwnMoves, "剣兵の「接近」は出ない");
            foreach (var e in m.Enemies) // ActRunManager.ForceTutorialClock と同じ：八方斬りを始めから①
            {
                if (e.Clocks.Count == 0) continue;
                e.Clocks[0].Cd = 1;
                Clocks.Telegraph(m, e, e.Clocks[0]);
            }
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null, events, null);
            Assert.IsTrue(events.Any(ev => ev.Type == BattleEventType.Damaged && ev.Unit != null && !ev.Unit.IsPlayer && ev.Cause != null && ev.Cause.Kind == DamageKind.Clock),
                "あなたが → に歩く → 左の敵もまねて → に歩き、赤紫（八方斬り）に入る");
        }

        // ───── はじめての戦闘：敵は まねるだけ ─────

        [Test]
        public void InTheFirstBattleEnemiesOnlyCopy()
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = ".......\n...S...\n...P...\n.......\n.......\n......."; s.maxSlots = 3; s.playerMaxHp = 20; s.enemyMaxHp = 12; s.spikeDamage = 3;
            s.combatRoom = true; s.combatV4 = true; s.availableCommands = new List<CommandData>();
            var m = BattleModel.FromStage(s);
            foreach (var e in m.Enemies) e.Clocks.Clear();
            m.NoTraits = true;
            Assert.IsEmpty(ThreatMap.EnemyAttack(m), "赤は出ない（敵は攻撃しない）");
            var sw = m.Enemies.Single();
            var pos = sw.Position;
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Left) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(20, m.Player.Hp, "隣にいても斬られない");
            Assert.AreEqual(pos, sw.Position, "近づかない（斬撃のまねは移動しない）");
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null, new List<BattleEvent>(), null);
            Assert.AreEqual(pos + Vector2Int.right, sw.Position, "まねだけで動く（あなたと同じ →）");
            Assert.IsTrue(m.Clone().NoTraits, "解析・予告の複製にも引き継ぐ");
        }

        // ───── 敵対的レビューで見つけた物 ─────

        StageData V4Stage(string map, string hp, string kinds = "")
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map; s.maxSlots = 3; s.playerMaxHp = 20; s.enemyMaxHp = 12; s.spikeDamage = 3;
            s.enemyHpList = hp; s.enemyCopyTypes = kinds;
            s.combatRoom = true; s.combatV4 = true; s.availableCommands = new List<CommandData>();
            return s;
        }

        [Test]
        public void ArcherRedLaneDoesNotStopAtYou()
        {
            // 弓兵（右向き）の2マス右にあなた。線の上を後ろ（右）へ下がっても、そのマスは赤（前は赤があなたのマスで止まり、下がった先で撃たれた）
            var m = BattleModel.FromStage(V4Stage(".......\n.......\n.K..P..\n.......", "12", "M"));
            foreach (var c in m.Enemies.Single().Clocks) c.Cd = 9;
            var red = ThreatMap.EnemyAttack(m);
            var back = m.Player.Position + Vector2Int.right;
            Assert.IsTrue(red.Contains(back), "あなたの後ろのマスも赤");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null, events, null);
            var hit = events.FirstOrDefault(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null && !e.Cause.ByCopy);
            Assert.IsNotNull(hit, "下がっても射程の中なら当たる");
            Assert.IsTrue(red.Contains(hit.Cell), "当たったマスは、ターンの初めに赤だった");
        }

        [Test]
        public void EnemiesDoNotWalkIntoWarpsButCopiesDo()
        {
            // 剣兵の右がワープ。あなたは右（下の行）。剣兵は近づくとき自分からはワープに入らない
            var m = BattleModel.FromStage(V4Stage(".......\n.Sw....\n.......\n...P...\n.......\n......w", "12", "N"));
            var sw = m.Enemies.Single();
            foreach (var c in sw.Clocks) c.Cd = 9;
            var warp = sw.Position + Vector2Int.right;
            Assert.IsTrue(m.Board.TryWarp(warp, out _));
            for (int t = 0; t < 3; t++)
            {
                TurnRules.PlayTurn(m, new List<CommandRecord> { R("Slash", Direction.Down) }, null, new List<BattleEvent>(), null);
                Assert.AreNotEqual(warp, sw.Position);
            }
            // まね（全まね）なら入る：剣兵の右がワープのとき、あなたが右へ一歩 → 剣兵も右へ → ワープで飛ぶ
            var m2 = BattleModel.FromStage(V4Stage(".......\n.Sw....\n.......\nP......\n.......\n......w", "12", "A"));
            var sw2 = m2.Enemies.Single();
            foreach (var c in sw2.Clocks) c.Cd = 9;
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m2, new List<CommandRecord> { R("Walk", Direction.Right) }, null, events, null);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Moved && e.Unit == sw2 && e.Cause != null && e.Cause.ByCopy && System.Math.Abs(e.To.x - e.From.x) + System.Math.Abs(e.To.y - e.From.y) > 1) || sw2.Position.x >= 5,
                "まねの移動でワープへ入り、反対側へ出た");
        }

        [Test]
        public void StunnedOrSilencedEnemiesShowNoRed()
        {
            var m = BattleModel.FromStage(V4Stage(".......\n...S...\n...P...\n.......", "12", "N"));
            var sw = m.Enemies.Single();
            Assert.IsTrue(ThreatMap.EnemyAttack(m).Contains(m.Player.Position));
            sw.StunRounds = 2;
            Assert.IsEmpty(ThreatMap.EnemyAttack(m), "動けない敵は攻撃しないので赤も出さない");
            sw.StunRounds = 0; sw.Silenced = true;
            Assert.IsEmpty(ThreatMap.EnemyAttack(m), "沈黙も同じ");
        }

        [Test]
        public void FirstRunHasOnlyTheLessonDoorAndAlwaysTheWarpRoom()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), seed, Look, true, false, true, true);
                run.ForceFirstDoor("v4_i1");
                Assert.AreEqual(1, run.Map[0].Count, "はじめてのランの1階は扉1つ");
                Assert.AreEqual("v4_i1", run.Map[0][0].Id);
                Assert.IsTrue(run.Map[4].Any(r => r.Id == "v4_n4"), "5階にワープの部屋");
                var all = run.Map.SelectMany(f => f).Where(r => r.V4).Select(r => r.Id).ToList();
                Assert.AreEqual(all.Count, all.Distinct().Count(), "同じ部屋は1ランに1回");
                run.SetFloorForTest(0, run.Map[0][0]);
                Assert.AreEqual(0, run.MakeBattleSetup().Layout, "はじめての戦闘はいつも同じ配置");
            }
        }

        // ───── 盤面のつぶれ（本人「結局上でずっと止まってる」）：素直に追いかけても、敵がふちに張り付かない ─────

        /// <summary>
        /// tools/pzsearch の Collapse.Naive と同じ：隣に敵がいれば斬る・いなければ一番近い敵へ近づく（3マス以上なら駆け足）。10 ターン。
        /// 端＝敵（動ける・道具でない）がふちにいたターンの割合・止＝同じふちのマスに続けていた割合。鏡の主は「上に構える巨体」なので数えない。
        /// </summary>
        static (double edge, double frozen) NaiveChase(StageData s, int layout)
        {
            var m = BattleModel.FromStage(s, layout);
            m.Player.Hp = 999;
            CommandData walk = Card("Walk"), dash = Card("Dash"), slash = Card("Slash");
            int et = 0, edge = 0, frozen = 0;
            var last = new Dictionary<UnitModel, Vector2Int>();
            for (int t = 0; t < 10; t++)
            {
                var p = m.Player;
                var foes = m.Enemies.Where(e => e.IsAlive && !e.IsObject).ToList();
                if (foes.Count == 0) break;
                var near = foes.OrderBy(e => Mathf.Abs(e.Position.x - p.Position.x) + Mathf.Abs(e.Position.y - p.Position.y)).First();
                var d = near.Position - p.Position;
                int dist = Mathf.Abs(d.x) + Mathf.Abs(d.y);
                var dir = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0 ? Direction.Right : Direction.Left) : (d.y > 0 ? Direction.Up : Direction.Down);
                var rec = dist == 1 ? new CommandRecord(slash, dir, 0) : dist >= 3 ? new CommandRecord(dash, dir, dash.minDistance) : new CommandRecord(walk, dir, walk.minDistance);
                var outcome = TurnRules.PlayTurn(m, new List<CommandRecord> { rec }, null, new List<BattleEvent>(), null);
                foreach (var e in m.Enemies.Where(e => e.IsAlive && !e.IsObject && !e.IsImmovable && e.Kind != EnemyKind.MirrorLord))
                {
                    et++;
                    bool onEdge = e.Position.x == 0 || e.Position.y == 0 || e.Position.x == m.Board.Width - 1 || e.Position.y == m.Board.Height - 1;
                    if (onEdge) edge++;
                    if (onEdge && last.TryGetValue(e, out var lp) && lp == e.Position) frozen++;
                    last[e] = e.Position;
                }
                if (outcome != TurnOutcome.Continue) break;
            }
            return (et > 0 ? edge / (double)et : 0, et > 0 ? frozen / (double)et : 0);
        }

        [Test]
        public void EnemiesDoNotCollapseOntoTheEdgeInAnyRoomOrStart()
        {
            var rooms = AssetDatabase.FindAssets("t:StageData", new[] { "Assets/_Project/Data/Stages" })
                .Select(g => AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(s => s != null && s.combatV4 && s.tutorialStep == 0 && s.name.StartsWith("StageX")).ToList();
            Assert.GreaterOrEqual(rooms.Count, 16, "ACT1 の部屋（StageX*）");
            // 【RC11】敵が自分では近づかない部屋（落石の広間・still=1）は除く：追いかけるのではなく、まねで岩の影へ歩かせる部屋（監査：直接だけのボットも勝ち 100%・約 9 ターン）
            rooms = rooms.Where(s => !s.stillEnemies).ToList();
            var bad = new List<string>();
            int layouts = 0;
            foreach (var s in rooms)
                for (int lay = 0; lay <= (s.altMaps?.Count ?? 0); lay++)
                {
                    layouts++;
                    var (edge, frozen) = NaiveChase(s, lay);
                    if (edge >= 0.5 || frozen >= 0.25) bad.Add($"{s.name}（{s.stageTitle}）配置{lay}：端 {edge:P0}・止 {frozen:P0}");
                }
            Assert.IsEmpty(bad, $"素直に追いかけると敵がふちで止まる配置（{bad.Count}/{layouts}）：\n" + string.Join("\n", bad));
        }

        // ───── 階ごとに覚える物：2階は柱と壁・3階は止まった仕掛け・5階はワープか動く仕掛け ─────

        [Test]
        public void FloorsTeachWallsThenStaticHazardThenWarpOrMovingHazard()
        {
            for (int seed = 1; seed <= 30; seed++)
            {
                var map = V4Content.BuildMap(new System.Random(seed));
                Assert.IsTrue(map[1].All(r => r.Kind == RoomKind.Battle && !r.Intro && !V4Content.LateRooms.Contains(r.Id)), "2階：柱と壁の通常戦2つ");
                Assert.AreEqual("v4_t4", map[2].Single(r => r.Kind == RoomKind.Tactical).Id, "3階：トゲの通路（止まった仕掛け）か中ボス");
                Assert.IsTrue(map[2].Any(r => r.IsMidBoss));
                var f5 = map[4].Single(r => r.Kind == RoomKind.Tactical);
                Assert.IsTrue(V4Content.LateTactical.Contains(f5.Id), "5階：炎床・落石・レーザー（" + f5.Id + "）");
                Assert.IsTrue(V4Content.LateRooms.Contains(map[4].Single(r => r.Kind == RoomKind.Battle).Id), "5階：後半の通常戦（ワープ）");
            }
        }

        // ───── 音量：保存して読み直しても同じ・Apply で効く ─────

        [Test]
        public void VolumeSettingsAreSavedAndApplied()
        {
            var p = new ActProfileData();
            Assert.AreEqual(50, p.volMaster, "はじめは全体 50%（RC2 は 100%：本人「音が大きすぎる」）");
            Assert.AreEqual(35, p.volBgm);
            Assert.AreEqual(50, p.volSe);
            p.volMaster = 40; p.volBgm = 20; p.volSe = 65; p.volSet = true;
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rc3_volume_test.json");
            try
            {
                Assert.IsTrue(ActProfile.Save(p, path));
                var back = ActProfile.Load(path);
                Assert.AreEqual(40, back.volMaster);
                Assert.AreEqual(20, back.volBgm);
                Assert.AreEqual(65, back.volSe);
                Assert.IsTrue(back.volSet);
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            float listener = AudioListener.volume, sfx = Sfx.Volume, bgm = Bgm.Volume;
            try
            {
                SoundSettings.Apply(p);
                Assert.AreEqual(0.40f, AudioListener.volume, 0.001f);
                Assert.AreEqual(0.65f, Sfx.Volume, 0.001f);
            }
            finally
            {
                AudioListener.volume = listener; Sfx.Volume = sfx; Bgm.SetVolume(bgm);
            }
        }
    }
}
