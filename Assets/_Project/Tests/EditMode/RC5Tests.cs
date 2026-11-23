using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC5・最終完成候補】槍兵が離れると詰める・処刑騎士のトゲ・ボスの盤面3通り・古いプロフィールとセーブ・負けたときの一言・部屋の一言（1部屋1つの見どころ）。
    /// </summary>
    public class RC5Tests
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

        // ───── 槍兵：2マス以内は構える・3マス以上離れると詰める（片付けの空ターンを減らす） ─────

        [Test]
        public void SpearmanStepsInOnlyWhenThreeOrMoreAway()
        {
            Assert.AreEqual(3, PhaseRules.SpearMode);
            var near = NoClock(BattleModel.FromStage(V4Stage(".......\n...Y...\n.......\n...P...\n.......\n.......", "12", "N")));
            var yn = near.Enemies.Single(); var pn = yn.Position;
            Assert.IsEmpty(TurnForecast.Compute(near, None).OwnMoves, "2マス：動かない");
            TurnRules.PlayTurn(near, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(pn, yn.Position);
            var far = NoClock(BattleModel.FromStage(V4Stage("...Y...\n.......\n.......\n...P...\n.......\n.......", "12", "N")));
            var yf = far.Enemies.Single(); var pf = yf.Position;
            var mv = TurnForecast.Compute(far, None).OwnMoves.Single();
            TurnRules.PlayTurn(far, None, null, new List<BattleEvent>(), null);
            Assert.AreEqual(mv.To, yf.Position, "予告どおり");
            Assert.AreEqual(1, Mathf.Abs(yf.Position.x - pf.x) + Mathf.Abs(yf.Position.y - pf.y), "1マスだけ詰める");
            StringAssert.Contains("3マス以上離れると", PhaseRules.MoveStyle(EnemyKind.Spearman));
            StringAssert.Contains("3マス以上離れると", PhaseRules.MoveStyle(EnemyKind.Swordsman), "剣兵と槍兵は同じ言い方");
        }

        // ───── 処刑騎士：トゲ（全まねの騎士を、まねでトゲへ） ─────

        [Test]
        public void ExecKnightRoomHasSpikesNextToTheKnightInEveryLayout()
        {
            var s = Stage("StageX9");
            Assert.IsNotNull(s);
            var maps = new List<string> { s.map };
            maps.AddRange(s.altMaps);
            Assert.AreEqual(3, maps.Count);
            foreach (var map in maps)
            {
                Assert.GreaterOrEqual(map.Count(ch => ch == '^'), 2, "トゲ2つ：" + map);
                var m = BattleModel.FromStage(V4Stage(map, "40,12,10"));
                var knight = m.Enemies.First(e => e.Kind == EnemyKind.ExecKnight);
                Assert.AreEqual(CopyKind.All, knight.CopyKind, "全まね（あなたの移動をまねてトゲへ入る）");
                Assert.IsTrue(m.Board.Spikes.Any(c => Mathf.Abs(c.x - knight.Position.x) + Mathf.Abs(c.y - knight.Position.y) <= 2), "騎士のそばにトゲ");
            }
            var v4 = V4Content.Room("v4_m1");
            StringAssert.Contains("トゲ", v4.CopyTip);
        }

        [Test]
        public void ExecKnightWalksIntoASpikeWhenItCopiesYourStep()
        {
            // 騎士の右にトゲ。あなたが → に一歩 → 騎士も → へ（まね）→ トゲで -3
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n.......\n..e^...\n.......\n.......\nP......", "40")));
            var k = m.Enemies.Single();
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Walk", Direction.Right) }, null, events, null);
            Assert.IsTrue(events.Any(e => e.Type == BattleEventType.Damaged && e.Unit == k && e.Cause != null && e.Cause.Kind == DamageKind.Spike), "まねでトゲへ");
        }

        // ───── ボス：盤面3通り（主・鏡核・写し身の位置は同じ） ─────

        [Test]
        public void BossHasThreeTerrainsWithTheSameLordCoresAndClones()
        {
            var s = Stage("StageXC");
            var maps = new List<string> { s.map };
            maps.AddRange(s.altMaps);
            Assert.AreEqual(3, maps.Count);
            var terrains = maps.Select(mp => string.Join("\n", mp.Split('\n').Skip(2).Select(r => r.Replace('P', '.').Replace('S', '.')))).Distinct().ToList();
            Assert.AreEqual(3, terrains.Count, "壁・柱・トゲの並びが3通り");
            foreach (var mp in maps)
            {
                var rows = mp.Split('\n');
                Assert.AreEqual("..cFc..", rows[0].Trim(), "主と鏡核は同じ場所");
                StringAssert.StartsWith("..H.H", rows[1].Trim(), "写し身は同じ場所");
                Assert.Greater(mp.Count(ch => ch == '^'), 0, "主を送り込めるトゲ");
            }
        }

        [Test]
        public void BossPillarIsRedBecauseItFallsBeforeCopies()
        {
            // 【RC5・レビュー】光柱は「敵の攻撃」の段（まねの前）に落ちる → 赤。前は赤紫（大技＝まねの後）で描いていた
            var m = BattleModel.FromStage(Stage("StageXC"));
            var lord = m.Enemies.First(e => e.Kind == EnemyKind.MirrorLord);
            foreach (var e in m.Enemies) e.Clocks.Clear();
            var cell = m.Player.Position;
            lord.BombCells = new List<Vector2Int> { cell };
            CollectionAssert.Contains(ThreatMap.EnemyAttack(m), cell, "赤");
            CollectionAssert.DoesNotContain(ThreatMap.Telegraph(m), cell, "赤紫ではない");
            Assert.IsTrue(ThreatMap.IsThreatened(m, cell), "転移石が使える（危ないマス）のは前と同じ");
        }

        // ───── 古いプロフィール・セーブ（RC2〜RC4）で落ちない ─────

        [Test]
        public void OldProfilesLoadWithSafeDefaults()
        {
            // RC2 の頃のプロフィール（音量・チュートリアルの項目が無い）
            string rc2 = "{\"version\":1,\"runsStarted\":3,\"runsFinished\":2,\"clears\":0,\"unlockedRoles\":[],\"records\":[],\"usedCards\":[],\"hintsSeen\":[\"V0\"],\"finalRuns\":2,\"finalFinished\":2}";
            Assert.IsTrue(ActProfile.TryParse(rc2, out var p));
            Assert.AreEqual(2, p.finalFinished);
            Assert.IsFalse(p.tutorialOffered, "チュートリアルの案内がもう一度出る");
            Assert.IsFalse(p.tutorialDone);
            Assert.AreEqual(50, p.volMaster, "音量は小さめの初期値");
            Assert.AreEqual(35, p.volBgm);
            Assert.AreEqual(50, p.volSe);
            Assert.IsNotNull(p.finalRoles);
            Assert.IsNotNull(p.finalCards);
            Assert.IsFalse(ActProfile.TryParse("{ 壊れた", out _), "壊れたファイルは新しく始める（Load が .broken へ退避）");
        }

        [Test]
        public void SavesWithAndWithoutTheWarpStoneRestore()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 77, Look, true, false, true, true);
            Assert.IsTrue(run.Items.Contains(ItemId.WarpStone));
            string json = ActSave.ToJson(run);
            Assert.IsTrue(ActSave.TryRestore(json, Look, out var back, out var err), err);
            Assert.IsTrue(back.Items.Contains(ItemId.WarpStone), "RC4 以降のセーブ：転移石が戻る");
            // RC3 のセーブ（転移石を持っていない）
            var d = JsonUtility.FromJson<ActSaveData>(json);
            d.items.Clear();
            Assert.IsTrue(ActSave.TryRestore(JsonUtility.ToJson(d), Look, out var rc3, out err), err);
            Assert.IsFalse(rc3.Items.Contains(ItemId.WarpStone), "RC3 のセーブ：持っていない物は増えない");
        }

        [Test]
        public void FirstRunMidRoomSavesRestoreOnTheLessonFloors()
        {
            // 【RC5・でたらめ通しで見つけた】はじめてのランの2階（癒し手の陣）の途中で閉じると、再開で「知らない部屋」になっていた
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            foreach (var seed in new[] { 101, 202, 303, 404, 505 })
            {
                var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), seed, Look, true, false, true, true);
                run.ForceFirstDoor("v4_i1");
                foreach (var id in ActRun.LessonFloor2Rooms)
                {
                    run.SetFloorForTest(1, V4Content.Room(id));
                    var d = ActSave.CaptureRoom(run, "room", 15, 2);
                    Assert.IsTrue(d.firstLesson);
                    Assert.IsTrue(ActSave.TryRestore(JsonUtility.ToJson(d), Look, out var back, out var err), $"種 {seed}・{id}：{err}");
                    Assert.IsTrue(back.FirstLesson, "はじめてのランのまま（2階・3階の教え方が続く）");
                    Assert.AreEqual(id, back.Current.Id);
                    // RC4 までのセーブ（firstLesson が無い）も読める
                    d.firstLesson = false;
                    Assert.IsTrue(ActSave.TryRestore(JsonUtility.ToJson(d), Look, out var old, out err), $"古いセーブ 種 {seed}・{id}：{err}");
                }
            }
        }

        // ───── 負けたときの一言：倒れた部屋のコツ・大技の割合 ─────

        [Test]
        public void LossTipsNameTheRoomAndTheCause()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 78, Look, true, false, true, true);
            run.SetFloorForTest(5, V4Content.Room("v4_m1"));
            run.AddDamage("大技（紫）", 12);
            run.AddDamage("まね", 4);
            run.LoseBattle(new BattleStats { Won = false, Turns = 9, HpBefore = 10, HpAfter = 0 });
            var tips = run.ResultTips();
            Assert.IsTrue(tips.Any(t => t.Contains("処刑騎士") && t.Contains("トゲ")), "倒れた部屋のコツ：" + string.Join(" / ", tips));
            Assert.IsTrue(tips.Any(t => t.Contains("大技")), "大技で多く削られた");
        }

        // ───── 指揮官：大号令（被ダメが増える札）→ 模倣命令（まねを強める札） ─────

        [Test]
        public void CommanderStartsWithMimicInsteadOfGreatMuster()
        {
            var role = V4Content.Role(ActContent.Roles.First(r => r.Id == "commander"));
            CollectionAssert.AreEqual(new[] { "Mimic" }, role.AddTactics);
            Assert.AreEqual(1, role.TacticBonus, "戦術札は1ターン3枚まで（変えない）");
            StringAssert.Contains("模倣命令", role.Description);
            Assert.IsNotNull(Card("Tactic_Mimic"), "札のアセットがある");
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "commander"), 79, n => cards.FirstOrDefault(c => c.name == n), true, false, true, true);
            Assert.IsTrue(run.TacticCards.Any(c => c.name == "Tactic_Mimic"));
            Assert.IsFalse(run.TacticCards.Any(c => c.name == "Tactic_GreatMuster"), "大号令は最初の札に無い（報酬のレアには残る）");
        }

        // ───── 札の文が本当の動きと合う（ブラインドレビューで見つけた） ─────

        [Test]
        public void CardTextsMatchWhatTheCardsDo()
        {
            var joint = Card("JointSlash");
            Assert.IsTrue(joint.copyWide);
            StringAssert.Contains("左右", UiStyles.CopyText(joint), "合わせ斬り：まねた敵は正面と左右の3マス（前は「敵も同じ攻撃」）");
            var tail = Card("Tailwind");
            Assert.AreEqual(1, tail.minDistance);
            StringAssert.DoesNotContain("止まれない", UiStyles.SelfText(tail), "1マスの札に「止まれない」は要らない");
            CollectionAssert.AreEqual(new[] { "Whirl", "Lunge", "Tailwind" }, TutorialContent.RunLoopRewardCards, "チュートリアルの報酬の見本も本編と同じ形（【RC6】命令札3枚）");
            foreach (var n in TutorialContent.RunLoopRewardCards) Assert.IsNotNull(Card(n), n);
            StringAssert.Contains("まねの後", TutorialContent.Steps.First(s => s.Name == "大技").Lines[0], "大技は「ターンの最後」ではなく「まねの後」（敵が自分で動く前）");
        }

        // ───── 完成版の商人は刻印を売らない・同じ札は2枚まで ─────

        [Test]
        public void FinalShopSellsNoImprintsAndAtMostTwoCopies()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            for (int seed = 300; seed < 340; seed++)
            {
                var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), seed, Look, true, false, true, true);
                int f = run.Map.FindIndex(fl => fl.Any(r => r.Kind == RoomKind.Shop));
                var shop = run.Map[f].First(r => r.Kind == RoomKind.Shop);
                run.Deck.Add(Look("Lunge")); run.Deck.Add(Look("Lunge")); // もう2枚持っている札は並ばない
                run.SetFloorForTest(f, shop);
                Assert.IsTrue(run.Enter(shop));
                if (run.ShopImprint != null) CollectionAssert.Contains(V4Content.ImprintPool, run.ShopImprint.Value, "【RC7】完成版の刻印だけ（前の版の刻印は売らない）");
                Assert.IsFalse(run.ShopCards.Any(c => c.Card.name == "Lunge"), "同じ札は2枚まで");
                foreach (var sc in run.ShopCards.Where(c => c.Card.type != CommandType.Tactic))
                    Assert.Less(run.Deck.Count(d => d == sc.Card), 2, "同じ札は2枚まで：" + sc.Card.name);
            }
        }

        // ───── 始めの配置で、敵が別の敵の赤の上に立っていない（何もしなくても敵どうしが傷つく＝まねの見せ場を配ってしまう） ─────

        [Test]
        public void NoEnemyStartsInsideAnotherEnemysRed()
        {
            var bad = new List<string>();
            foreach (var room in V4Content.Rooms.Where(r => r.V4 && r.Kind != RoomKind.Rest && r.Kind != RoomKind.Shop))
            {
                bool lessonOnly = room.Id == "v4_i1"; // 鏡の歩兵の配置0：はじめての戦闘（敵は攻撃しない）だけで使う（ActRun の Layout）
                var s = Stage(room.Stage);
                if (s == null) continue;
                var maps = new List<string> { s.map };
                maps.AddRange(s.altMaps);
                for (int lay = 0; lay < maps.Count; lay++)
                {
                    if (lessonOnly && lay == 0) continue;
                    var m = BattleModel.FromStage(s, lay);
                    foreach (var e in m.Enemies.Where(x => x.IsAlive && !x.IsObject && PhaseRules.HasTrait(x)))
                        foreach (var c in PhaseRules.TraitCells(m, e))
                        {
                            var hit = m.Enemies.FirstOrDefault(o => o != e && o.IsAlive && !o.IsObject && !o.Dormant && o.Position == c);
                            if (hit != null) bad.Add($"{room.Name} 配置{lay}：{PhaseRules.ShortName(e)} の赤に {PhaseRules.ShortName(hit)}");
                        }
                }
            }
            Assert.IsEmpty(bad, string.Join(" / ", bad));
        }

        // ───── 【RC8】呼び名の記号は敵全体の並び順＝動く順番（名札・攻撃のマスの札・原因の一言が同じ） ─────

        [Test]
        public void EnemyLettersFollowTheActingOrderAndMatchTheCauseText()
        {
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n.Y.S.Y.\n.......\n...P...\n.......\n.......", "12,12,12")));
            var ys = m.Enemies.Where(e => e.Kind == EnemyKind.Spearman).ToList();
            var s = m.Enemies.Single(e => e.Kind == EnemyKind.Swordsman);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, m.Enemies.Select(e => e.KindLetter).ToArray(), "並び順（動く順番）に A・B・C");
            Assert.AreEqual("B", s.KindLetter, "1体だけの兵種にも記号（攻撃のマスの札で誰か分かるように）");
            Assert.AreEqual("剣兵B", PhaseRules.ShortName(s));
            Assert.AreEqual("槍兵C", PhaseRules.ShortName(ys[1]));
            var ev = new BattleEvent { Type = BattleEventType.Damaged, Unit = m.Player, Amount = 3, Cause = new DamageCause { Kind = DamageKind.Trait, Source = ys[1] } };
            Assert.AreEqual("槍兵Cの攻撃（赤）", TurnForecast.CauseText(ev));
            Assert.AreEqual("C", m.Clone().Enemies.Where(e => e.Kind == EnemyKind.Spearman).ElementAt(1).KindLetter, "盤面の写し（予告の計算）でも同じ");
        }

        // ───── 1部屋1つの見どころ（扉に書くコツ）・全部屋の説明が新しい動きと合う ─────

        [Test]
        public void ChallengeDepthsCutTheHandAndSayIt()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            for (int level = 0; level <= V4Content.MaxChallenge; level++)
            {
                var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 80, Look, true, false, true, true) { Challenge = level };
                int want = level >= 3 ? 5 : level >= 2 ? 6 : 7;
                Assert.AreEqual(want, run.BaseHand, "深度" + level);
                run.Enter(run.Doors[0]);
                Assert.AreEqual(want, run.MakeBattleSetup(1).BaseHand, "戦闘に持ち込む手札も同じ：深度" + level);
                if (level >= 2) StringAssert.Contains($"手札 {want} 枚", V4Content.ChallengeText(level));
            }
        }

        [Test]
        public void FirstRunFloorsOneAndTwoOfferOnlyCommandCards()
        {
            // 緑の札（戦術札）は はじめてのランの3階で教える：1・2階の報酬は命令札だけ（前は教える前に報酬に出ていた）
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 81, Look, true, false, true, true);
            run.ForceFirstDoor("v4_i1");
            for (int floor = 0; floor < 2; floor++)
            {
                run.Enter(run.Doors[0]);
                run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = 20, HpAfter = 20 });
                Assert.AreEqual(3, run.RewardCards.Count, "階" + (floor + 1));
                Assert.IsFalse(run.RewardCards.Any(c => c.IsTactic), "緑の札は3階から：階" + (floor + 1));
                run.Leave();
            }
            run.Enter(run.Doors.First(d => !d.IsMidBoss));
            run.WinBattle(new BattleStats { Won = true, Turns = 5, HpBefore = 20, HpAfter = 20 });
            Assert.AreEqual(3, run.RewardCards.Count(c => !c.IsTactic), "【RC6】3階からも通常戦は命令札3枚（緑は中ボスと商人）");
        }

        [Test]
        public void ArcherArrowStopsAtTheStartOfTurnRedWhenYouKillTheBlocker()
        {
            // 弓兵 → 剣兵（線をふさぐ）→ あなた。あなたが剣兵を突きで倒しても、矢は赤（剣兵のマスまで）の先へ伸びない（レーザー回廊で見つけた予告外）
            var m = NoClock(BattleModel.FromStage(V4Stage(".......\n.......\nK.S.P..\n.......\n.......\n.......", "12,3")));
            var archer = m.Enemies.Single(e => e.Kind == EnemyKind.Archer);
            Assert.AreEqual(Direction.Right, archer.Facing);
            var red = ThreatMap.EnemyAttack(m);
            Assert.IsFalse(red.Contains(m.Player.Position), "初めの赤はあなたまで届いていない");
            var events = new List<BattleEvent>();
            TurnRules.PlayTurn(m, new List<CommandRecord> { R("Thrust", Direction.Left) }, null, events, null);
            Assert.IsFalse(m.Enemies.Any(e => e.Kind == EnemyKind.Swordsman && e.IsAlive), "剣兵は倒れた");
            Assert.IsFalse(events.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Cause != null && e.Cause.Source == archer), "矢は赤の外のあなたに当たらない");
        }

        [Test]
        public void ForgedCardTextShowsTheForgedNumberOnEveryAttackCard()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            foreach (var n in V4Content.FinalCommandPool.Select(ci => ci.Asset).Concat(ActContent.StartDeck).Distinct())
            {
                var c = cards.FirstOrDefault(x => x.name == n);
                if (c == null || !V4Content.CanForge(c)) continue;
                string forged = UiStyles.SelfText(c, DeckState.ForgeBonus);
                StringAssert.Contains((c.damage + DeckState.ForgeBonus).ToString(), forged, "鍛えた数字が札の文に出る：" + n);
                Assert.AreNotEqual(UiStyles.SelfText(c), forged, n);
            }
        }

        [Test]
        public void OldSavesRebuildTheOldMapAndNewMapsKeepTheCannonKingOffFloorThree()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var role = ActContent.Roles.First(r => r.Id == "standard");
            int legacyKingOn3 = 0;
            for (int seed = 1; seed <= 40; seed++)
            {
                var now = new ActRun(role, seed, Look, true, false, true, true);
                Assert.IsFalse(now.Map[2].Any(r => r.Id == "v4_m2"), "3階の中ボスは砲王以外：種" + seed);
                var old = new ActRun(role, seed, Look, true, false, true, true, 0);
                if (old.Map[2].Any(r => r.Id == "v4_m2")) legacyKingOn3++;
                // 古いセーブ（mapRev の無い JSON）は古い作り方で同じ地図に戻る（3階・6階の部屋の途中から続けられる）
                var json = ActSave.ToJson(old).Replace("\"mapRev\": 0,", "");
                StringAssert.DoesNotContain("mapRev", json);
                Assert.IsTrue(ActSave.TryRestore(json, Look, out var back, out var err), err);
                CollectionAssert.AreEqual(old.Map.SelectMany(f => f).Select(r => r.Id).ToList(), back.Map.SelectMany(f => f).Select(r => r.Id).ToList(), "種" + seed);
                Assert.IsTrue(ActSave.TryRestore(ActSave.ToJson(now), Look, out var back2, out err), err);
                CollectionAssert.AreEqual(now.Map.SelectMany(f => f).Select(r => r.Id).ToList(), back2.Map.SelectMany(f => f).Select(r => r.Id).ToList(), "今のセーブ：種" + seed);
            }
            Assert.Greater(legacyKingOn3, 0, "前の作り方では3階に砲王が出る種がある（この検査が意味を持つ）");
        }

        [Test]
        public void EveryRoomHasOneHookLine()
        {
            foreach (var r in V4Content.Rooms)
            {
                Assert.IsFalse(string.IsNullOrEmpty(r.CopyTip), "まねの使いどころ：" + r.Id);
                Assert.IsFalse(string.IsNullOrEmpty(r.Flavor), "一言：" + r.Id);
            }
            var text = System.IO.File.ReadAllText("tools/combat_v4_rooms.txt");
            Assert.IsFalse(text.Contains("槍兵は動かず、あなたの方を向く。"), "槍兵は離れると詰める（古い説明が残っていない）");
            Assert.AreEqual("大技（紫）", UiStyles.DamageCauseLabel(new BattleEvent { Type = BattleEventType.Damaged, Cause = new DamageCause { Kind = DamageKind.Clock } }));
        }

        // ───── 【最終ゲート】中断 → 続きから：商人の品ぞろえは変わらない・ボスの途中からの再開は再挑戦を使う・回数は残る ─────

        [Test]
        public void ShopStockSurvivesSuspendAndResume()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            int tested = 0;
            for (int seed = 400; seed < 420; seed++)
            {
                var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), seed, Look, true, false, true, true);
                int f = run.Map.FindIndex(fl => fl.Any(r => r.Kind == RoomKind.Shop));
                var shop = run.Map[f].First(r => r.Kind == RoomKind.Shop);
                run.SetFloorForTest(f, shop);
                Assert.IsTrue(run.Enter(shop));
                var first = run.ShopCards.FirstOrDefault(c => c.Card.type != CommandType.Tactic);
                if (first == null) continue;
                run.Deck.Add(first.Card); // 1枚持っている札をもう1枚買う → 作り直すと「同じ札は2枚まで」で候補から消える（前は引き直しになった）
                run.AddShards(300);
                Assert.IsTrue(run.BuyCard(first));
                var before = run.ShopCards.Select(c => $"{c.Card.name}:{c.Price}:{c.Sold}").Concat(run.ShopItems.Select(i => $"{i.Id}:{i.Price}:{i.Sold}")).ToList();
                var json = JsonUtility.ToJson(ActSave.CaptureRoom(run, "room"));
                Assert.IsTrue(ActSave.TryRestore(json, Look, out var back, out var err), err);
                var after = back.ShopCards.Select(c => $"{c.Card.name}:{c.Price}:{c.Sold}").Concat(back.ShopItems.Select(i => $"{i.Id}:{i.Price}:{i.Sold}")).ToList();
                CollectionAssert.AreEqual(before, after, "続きからでも同じ品ぞろえ（種" + seed + "）");
                tested++;
            }
            Assert.Greater(tested, 10);
        }

        [Test]
        public void ResumingTheBossMidBattleUsesTheBossRetry()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 81, Look, true, false, true, true);
            run.SetFloorForTest(run.FloorCount - 1, V4Content.Room("v4_boss"));
            Assert.IsTrue(run.UseBossRetryForResume(), "途中からの再開＝再挑戦（1ランに1回）");
            Assert.IsTrue(run.BossRetryUsed);
            Assert.IsFalse(run.UseBossRetryForResume(), "2回目は無い");
            var deep = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 82, Look, true, false, true, true) { Challenge = 3 };
            deep.SetFloorForTest(deep.FloorCount - 1, V4Content.Room("v4_boss"));
            Assert.IsFalse(deep.UseBossRetryForResume(), "反響深度3 は再挑戦なし");
            var normal = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 83, Look, true, false, true, true);
            normal.SetFloorForTest(1, V4Content.Room("v4_n1"));
            Assert.IsFalse(normal.UseBossRetryForResume(), "ボス以外は関係ない");
            // 回数はセーブに残る
            normal.MidBattleResumes = 2;
            Assert.IsTrue(ActSave.TryRestore(ActSave.ToJson(normal), Look, out var back, out var err), err);
            Assert.AreEqual(2, back.MidBattleResumes);
        }

        [Test]
        public void FinalShopDoorShowsTheFinalPrices()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CommandData Look(string n) => cards.FirstOrDefault(c => c.name == n);
            var run = new ActRun(ActContent.Roles.First(r => r.Id == "standard"), 84, Look, true, false, true, true);
            int cheapest = V4Content.FinalTacticPool.Select(V4Content.TacticPrice).Min();
            run.AddShards(cheapest - run.Shards);
            string t = ActUI.ShopDoorTextV4("商人", run);
            StringAssert.DoesNotContain("刻印", t, "完成版の商人は刻印を売らない");
            StringAssert.Contains("戦術札 " + cheapest, t);
            StringAssert.Contains("強化 " + V4Content.UpgradePrice, t, "【RC6】強化の値段も");
            StringAssert.Contains("戦術札", t.Substring(t.IndexOf("買える物：")), $"ルーン片 {cheapest} で戦術札が買える");
        }
    }
}
