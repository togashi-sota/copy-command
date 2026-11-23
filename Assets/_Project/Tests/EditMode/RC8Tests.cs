using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC8・Combat UI V2（本人の RC7 実プレイのあと）】危険の一覧（Rules/ThreatModel）：撃つ敵・種類（敵と一緒に動く／床に決まった／床の仕掛け）・
    /// 押す・引くでずれる・倒せば消える・ターンの初めに見えない攻撃は無い・敵の記号は動く順番・ボスの大技は1ターンに1つ（光柱は休む）。
    /// </summary>
    public class RC8Tests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in created) if (o != null) Object.DestroyImmediate(o);
            created.Clear();
            Clocks.OneAtATime = true;
        }

        static CommandData Asset(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData StageAsset(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static CommandRecord Rec(string name, Direction d) { var c = Asset(name); return new CommandRecord(c, d, c.minDistance, new CardInstance(c)); }

        StageData NewStage(string map, string hp = "", bool clocks = false)
        {
            var s = ScriptableObject.CreateInstance<StageData>();
            created.Add(s);
            s.map = map; s.maxSlots = 3; s.playerMaxHp = 20; s.enemyMaxHp = 8; s.spikeDamage = 3;
            s.enemyHpList = hp; s.combatRoom = true; s.combatV4 = true; s.noClocks = !clocks;
            s.availableCommands = new List<CommandData>();
            return s;
        }

        // ───── 種類：剣兵の赤＝敵と一緒に動く／砲兵の照準＝床に決まった／大技＝床に決まった（赤紫） ─────

        [Test]
        public void ThreatKindsFollowFixedAndBigAttack()
        {
            var m = BattleModel.FromStage(NewStage(".......\n...S...\n...P...\n.......\n...O...\n.......", "12,12"));
            var bomber = m.Enemies.First(e => e.Kind == EnemyKind.Bomber);
            bomber.BombCells = PhaseRules.BombShape(m, m.Player.Position);
            var sw = m.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            var th = Threats.Compute(m);
            var red = th.Single(t => t.Source == sw);
            Assert.AreEqual(ThreatKind.Follow, red.Kind, "剣兵の赤は敵と一緒に動く");
            Assert.AreEqual(ThreatTiming.Attack, red.Timing);
            CollectionAssert.AreEquivalent(PhaseRules.SwordCells(m, sw), red.Cells, "ルールと同じマス（隣4マス）");
            var bomb = th.Single(t => t.Source == bomber);
            Assert.AreEqual(ThreatKind.Fixed, bomb.Kind, "砲兵の照準は床に決まっている");
            Assert.AreEqual(ThreatTiming.Attack, bomb.Timing, "爆撃は②敵の攻撃の段（赤）");
            Assert.IsTrue(Threats.Follows(sw) && !Threats.Follows(bomber));
            Assert.AreEqual(PhaseRules.Melee(m) + PhaseRules.Bomb(m), Threats.DamageAt(th, m.Player.Position), "あなたのマスは剣と爆撃の合計");
        }

        // ───── 押す：一緒に動く赤はずれる（あなたは動かずに外せる）・床に決まった照準はずれない ─────

        [Test]
        public void PushingTheSourceShiftsItsRedButNotAFixedAim()
        {
            var m = BattleModel.FromStage(NewStage(".......\n.......\n...S...\n...P...\n.......\nO......", "14,12"));
            var sw = m.Enemies.First(e => e.Kind == EnemyKind.Swordsman);
            var bomber = m.Enemies.First(e => e.Kind == EnemyKind.Bomber);
            bomber.BombCells = PhaseRules.BombShape(m, new Vector2Int(0, 2));
            var before = Threats.Compute(m);
            Assert.IsTrue(before.Any(t => t.Source == sw && t.Cells.Contains(m.Player.Position)), "はじめは剣兵の赤の上");
            var plan = new List<CommandRecord> { Rec("Push", Direction.Up) };
            var after = Threats.Compute(PlayerPreview.AfterPlanned(m, plan));
            var diff = Threats.Diff(before, after);
            var swCh = diff.Single(c => c.Source.Id == sw.Id);
            Assert.IsTrue(swCh.Moved && !swCh.Gone, "剣兵の赤は「ずれる」");
            Assert.IsFalse(swCh.After.Cells.Contains(m.Player.Position), "ずれた赤はあなたのマスに無い");
            Assert.IsFalse(diff.Any(c => c.Source.Id == bomber.Id), "床に決まった照準は変わらない");
            int hp = m.Player.Hp;
            TurnRules.PlayTurn(m, plan, null, new List<BattleEvent>(), null);
            Assert.AreEqual(hp, m.Player.Hp, "動かずに、押してずらせば当たらない（本物のターンでも）");
        }

        // ───── 倒す：その敵の攻撃は消える（予告の「×」と同じ） ─────

        [Test]
        public void KillingTheSourceRemovesItsThreat()
        {
            var m = BattleModel.FromStage(NewStage(".......\n.......\n...S...\n...P...\n.......\n.......", "3"));
            var sw = m.Enemies.First();
            var plan = new List<CommandRecord> { Rec("Slash", Direction.Up) };
            var diff = Threats.Diff(Threats.Compute(m), Threats.Compute(PlayerPreview.AfterPlanned(m, plan)));
            Assert.IsTrue(diff.Single(c => c.Source.Id == sw.Id).Gone, "倒すと消える");
            int hp = m.Player.Hp;
            TurnRules.PlayTurn(m, plan, null, new List<BattleEvent>(), null);
            Assert.AreEqual(hp, m.Player.Hp, "倒した敵の赤は来ない");
        }

        // ───── 予告外は無い：完成版の全部屋で、あなたが受ける敵の攻撃・大技・床は、ターンの初めの危険の一覧にある（まね返しとあなたが動かした敵の赤は除く） ─────

        [Test]
        public void EveryHitOnYouWasInTheThreatListAtTurnStart()
        {
            var rooms = V4Content.Rooms.Where(r => r.V4 && r.Stage != null).Select(r => StageAsset(r.Stage)).Where(s => s != null).ToList();
            Assume.That(rooms.Count >= 12, "完成版の部屋（run-setup で作る）");
            var bad = new List<string>();
            var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
            foreach (var st in rooms)
            {
                var m = BattleModel.FromStage(st);
                var rng = new System.Random(st.name.GetHashCode());
                for (int t = 0; t < 6 && m.Player.IsAlive && m.Enemies.Any(e => e.IsAlive && !e.IsObject); t++)
                {
                    var th = Threats.Compute(m);
                    var plan = new List<CommandRecord> { Rec("Walk", dirs[rng.Next(4)]) };
                    var moved = new HashSet<int>();
                    var ev = new List<BattleEvent>();
                    TurnRules.PlayTurn(m, plan, null, ev, null);
                    foreach (var e in ev)
                    {
                        if (e.Type != BattleEventType.Damaged || e.Unit == null || !e.Unit.IsPlayer || e.Amount <= 0 || e.Cause == null || e.Cause.ByCopy) continue;
                        if (e.Cause.Kind == DamageKind.Spike) continue; // トゲは盤面の絵（入るたび）
                        var src = e.Cause.Source;
                        bool listed = th.Any(x => x.Cells.Contains(e.Cell) && (x.Source == null ? e.Cause.Kind == DamageKind.Fire : src != null && x.SourceId == src.Id));
                        bool shifted = src != null && ev.Any(x => x.Type == BattleEventType.Moved && x.Unit == src && ev.IndexOf(x) < ev.IndexOf(e));
                        if (!listed && !shifted) bad.Add($"{st.name} T{t + 1}：{TurnForecast.CauseText(e)} {e.Amount}（{e.Cell}）");
                    }
                }
            }
            Assert.IsEmpty(bad, string.Join(" / ", bad.Take(8)));
        }

        // ───── 敵の記号＝動く順番 ─────

        [Test]
        public void LettersAreTheActingOrder()
        {
            var m = BattleModel.FromStage(NewStage(".......\n.S...Y.\n.......\n...P...\n.K.....\n.......", "10,10,10"));
            var order = m.Enemies.Where(e => !e.IsObject).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(0, order.Count).Select(i => ((char)('A' + i)).ToString()).ToArray(), order.Select(e => e.KindLetter).ToArray(), "A → B → C は兵種の行動の順番（PhaseRules.TraitSteps も同じ並び）");
            var steps = PhaseRules.TraitSteps(m, 1).Select(s => s.enemy).Where(e => !e.IsObject).ToList();
            CollectionAssert.AreEqual(order, steps, "②敵の攻撃は A → B → C");
        }

        // ───── ボス：大技は1ターンに1つ・大技のターンは光柱が休む（1マスに 10 が重ならない） ─────

        [Test]
        public void MirrorLordNeverStacksTwoBigAttacksOrAPillarOnTheSameTurn()
        {
            var st = StageAsset("StageXC");
            Assume.That(st != null);
            int worstLord = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var m = BattleModel.FromStage(st);
                var rng = new System.Random(seed);
                var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
                for (int t = 0; t < 14 && m.Player.IsAlive; t++)
                {
                    var lord = m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord && e.IsAlive);
                    if (lord == null) break;
                    var lordThreats = Threats.Compute(m).Where(x => x.SourceId == lord.Id).ToList();
                    Assert.LessOrEqual(lord.Clocks.Count(c => c.Telegraphed), 1, $"種{seed} T{t + 1}：大技は1ターンに1つ");
                    if (lord.Clocks.Any(c => c.Telegraphed)) Assert.IsEmpty(lord.BombCells, $"種{seed} T{t + 1}：大技のターンは光柱が休む");
                    worstLord = Mathf.Max(worstLord, Threats.MaxStack(lordThreats));
                    m.Player.Hp = m.Player.MaxHp; // 長く見るため
                    TurnRules.PlayTurn(m, new List<CommandRecord> { Rec("Walk", dirs[rng.Next(4)]) }, null, new List<BattleEvent>(), null);
                }
            }
            Assert.LessOrEqual(worstLord, System.Math.Max(Clocks.LordSlamDamage, CombatBoss.ChargeDamage), "鏡の主だけの重なり（1マス）は大技1つ分まで（RC7 は叩きつけ＋薙ぎ払い＋光柱で 10）");
        }

        [Test]
        public void OneBigAttackPerEnemyAlsoForMidBosses()
        {
            foreach (var asset in new[] { "StageX9", "StageXA", "StageXB" })
            {
                var st = StageAsset(asset);
                Assume.That(st != null);
                var m = BattleModel.FromStage(st);
                var boss = m.Enemies.First(e => PhaseRules.IsBossKind(e.Kind));
                var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
                for (int t = 0; t < 16 && m.Player.IsAlive && boss.IsAlive; t++)
                {
                    Assert.LessOrEqual(boss.Clocks.Count(c => c.Telegraphed), 1, $"{asset} T{t + 1}：中ボスの大技も1ターンに1つ");
                    m.Player.Hp = m.Player.MaxHp;
                    TurnRules.PlayTurn(m, new List<CommandRecord> { Rec("Walk", dirs[t % 4]) }, null, new List<BattleEvent>(), null);
                }
            }
        }

        // ───── チュートリアル4：移動の札は無い・押してずらす ─────

        [Test]
        public void TutorialFourTeachesShiftingTheRedWithoutWalking()
        {
            var st = StageAsset("StageTU4");
            Assume.That(st != null);
            Assert.IsTrue(st.handTestDeck.Any(c => c != null && c.type == CommandType.Push), "押し出しがある");
            Assert.IsFalse(st.handTestDeck.Any(c => c != null && CopyKinds.IsMoveType(c.type)), "移動の札は無い");
            StringAssert.Contains("ずら", st.goalText);
            var step = TutorialContent.Steps.First(s => s.Asset == "StageTU4");
            Assert.IsTrue(step.Lines.Any(l => l.Contains("押し出し")) && step.Success.Any(l => l.Contains("固定")), "やること＝押してずらす・できた＝次は固定の攻撃（【RC9】5番で体験する）");
        }
    }
}
