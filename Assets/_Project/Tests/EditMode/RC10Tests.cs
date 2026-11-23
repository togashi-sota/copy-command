using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CopyCommand.Tests
{
    /// <summary>
    /// 【RC10・ChatGPT「ボスが『2マス逃げる作業』」「札の文字」「敵の見た目の使い回し」「5分体験の入口」】
    /// ボス：光柱は1ターンおき（3）・写し身は鏡（主の攻撃が当たると主へはね返る -6・主はひるむ）。札の顔はアイコン＋短い文字。兵種ごとの絵。
    /// </summary>
    public class RC10Tests
    {
        static CommandData Card(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"Assets/_Project/Data/Commands/{name}.asset");
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");

        [TearDown]
        public void Cleanup() => RulesEngine.ResetExperiments();

        static BattleModel BossRoom() => BattleModel.FromStage(Stage("StageXC"));

        /// <summary>段階2にして写し身を目覚めさせる。</summary>
        static UnitModel ToPhase2(BattleModel m)
        {
            var lord = CombatBoss.Find(m);
            CombatBoss.Damage(m, lord, lord.Hp - CombatBoss.Floor(lord, 0), m.Player, null, false, DamageKind.Direct, new List<BattleEvent>());
            foreach (var c in m.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive)) { c.Dormant = false; c.WakingUp = false; }
            lord.WakingUp = false;
            lord.StunRounds = 0;
            return lord;
        }

        static bool Free(BattleModel m, Vector2Int c, params UnitModel[] ok) =>
            m.Board.IsInside(c) && !m.Board.IsWall(c) && !m.Board.Spikes.Contains(c) && !m.Board.IsCore(c) && (m.GetUnitAt(c) == null || ok.Contains(m.GetUnitAt(c)));

        // ───── ボス：光柱 ─────

        [Test]
        public void BossPillarFallsEveryOtherTurnAndHitsHarder()
        {
            Assert.AreEqual(1, CombatBoss.PillarMode, "光柱は1ターンおき（毎ターン2マス逃げる作業にしない）");
            Assert.AreEqual(3, CombatBoss.PillarDamage, "そのかわり1回は 3");
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            foreach (var c in lord.Clocks) c.Cd = 99; // 手の大技は数えない
            var fired = new List<bool>();
            for (int k = 0; k < 8; k++)
            {
                fired.Add(lord.BombCells.Count > 0);
                CombatBoss.Trait(m, lord, new List<BattleEvent>());
            }
            Assert.GreaterOrEqual(fired.Count(f => f), 3, "光柱は落ち続ける");
            for (int k = 1; k < fired.Count; k++) Assert.IsFalse(fired[k] && fired[k - 1], $"2ターン続けて落ちない（{k}）");
        }

        // ───── ボス：写し身は鏡（主の攻撃をはね返す） ─────

        [Test]
        public void LordsOwnPillarReflectsOffACloneAndStaggersTheLord()
        {
            var m = BossRoom();
            var lord = ToPhase2(m);
            var clone = m.Enemies.First(e => e.Kind == EnemyKind.HealClone && e.IsAlive);
            // 写し身のいるマスを十字に入れ、あなたは十字の外
            bool placed = false;
            for (int y = 1; y < m.Board.Height && !placed; y++)
                for (int x = 1; x < m.Board.Width - 1 && !placed; x++)
                {
                    var center = new Vector2Int(x, y);
                    if (!Free(m, center, clone) || !Free(m, center + Vector2Int.right, clone)) continue;
                    clone.Position = center + Vector2Int.right;
                    lord.BombCells = PhaseRules.BombShape(m, center);
                    foreach (var p in new[] { new Vector2Int(0, m.Board.Height - 1), new Vector2Int(m.Board.Width - 1, m.Board.Height - 1), new Vector2Int(0, 0) })
                        if (Free(m, p, m.Player) && !lord.BombCells.Contains(p)) { m.Player.Position = p; placed = true; break; }
                }
            Assume.That(placed, "置ける場所がある");
            int hp0 = lord.Hp;
            var ev = new List<BattleEvent>();
            CombatBoss.Trait(m, lord, ev);
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.BossReflect), "主の光柱が写し身ではね返った");
            Assert.AreEqual(hp0 - CombatBoss.CloneStrikeDamage, lord.Hp, "主に -6");
            Assert.IsFalse(clone.IsAlive, "写し身は砕ける");
            Assert.Greater(lord.StunRounds, 0, "はね返されて主はひるむ");
            Assert.IsFalse(PhaseRules.HasTrait(lord), "ひるんだ主は、このターンの残りは何もしない（手の大技も進まない）");
            Assert.AreEqual(0, lord.BombCells.Count, "ひるんだので次の光柱は予告しない");
        }

        [Test]
        public void LordsBigAttackAlsoReflectsOffAClone()
        {
            var m = BossRoom();
            var lord = ToPhase2(m);
            var clone = m.Enemies.First(e => e.Kind == EnemyKind.HealClone && e.IsAlive);
            int hp0 = lord.Hp;
            var ev = new List<BattleEvent>();
            int dealt = RulesEngine.ApplyDamage(m, lord, null, false, clone, 4, DamageKind.Clock, ev);
            Assert.AreEqual(0, dealt, "写し身は傷つかない（鏡）");
            Assert.AreEqual(hp0 - CombatBoss.CloneStrikeDamage, lord.Hp, "手の大技でもはね返る");
            Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.BossReflect));
        }

        [Test]
        public void ReflectionOnlyInPhaseTwoAndOnlyFromTheLord()
        {
            var m = BossRoom();
            var lord = CombatBoss.Find(m);
            var clone = m.Enemies.First(e => e.Kind == EnemyKind.HealClone && e.IsAlive);
            Assert.IsTrue(clone.Dormant);
            Assert.IsFalse(CombatBoss.TryReflect(m, clone, lord, new List<BattleEvent>()), "段階1の眠る像ははね返さない");
            ToPhase2(m);
            var guard = m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.Swordsman && e.IsAlive);
            if (guard != null)
            {
                int hp0 = lord.Hp;
                RulesEngine.ApplyDamage(m, guard, null, false, clone, 3, DamageKind.Trait, new List<BattleEvent>());
                Assert.AreEqual(hp0, lord.Hp, "主以外の攻撃ははね返らない");
            }
        }

        /// <summary>ボスでも「脅威を操る」：あなたが写し身を押して光柱（赤い十字・固定）の中へ入れる → 主へはね返る。</summary>
        [Test]
        public void PushingACloneIntoThePillarTurnsTheBossAttackAgainstTheBoss()
        {
            var m = BossRoom();
            var lord = ToPhase2(m);
            foreach (var c in lord.Clocks) c.Cd = 99;
            var clone = m.Enemies.First(e => e.Kind == EnemyKind.HealClone && e.IsAlive);
            var other = m.Enemies.Where(e => e != lord && e != clone && e.IsAlive && !e.IsObject).ToList();
            bool placed = false;
            foreach (var d in new[] { Direction.Right, Direction.Left, Direction.Down, Direction.Up })
            {
                var off = d.ToOffset();
                for (int y = 1; y < m.Board.Height && !placed; y++)
                    for (int x = 0; x < m.Board.Width && !placed; x++)
                    {
                        var p = new Vector2Int(x, y);
                        var c1 = p + off; var c2 = p + off * 2;
                        if (!Free(m, p, m.Player) || !Free(m, c1, clone) || !Free(m, c2, clone)) continue;
                        var cross = PhaseRules.BombShape(m, c2 + off);
                        if (cross.Contains(p) || cross.Contains(c1) || !cross.Contains(c2)) continue;
                        if (other.Any(o => cross.Contains(o.Position) || o.Position == c1 || o.Position == c2)) continue;
                        if (ThreatMap.EnemyAttack(m).Contains(p)) continue;
                        m.Player.Position = p; clone.Position = c1; lord.BombCells = cross; placed = true;
                        var ev = new List<BattleEvent>();
                        TurnRules.PlayTurn(m, new List<CommandRecord> { new CommandRecord(Card("Push"), d, 1) }, null, ev, null);
                        Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.BossReflect), "押した写し身に主の光柱が落ちて、はね返った");
                        Assert.IsTrue(ev.Any(e => e.Type == BattleEventType.Damaged && e.Unit == lord && e.Amount == CombatBoss.CloneStrikeDamage && e.Text == "反射"), "主に -6（ターンの終わりに残りの写し身が回復するので HP では比べない）");
                    }
                if (placed) break;
            }
            Assume.That(placed, "置ける場所がある");
        }

        [Test]
        public void BossObjectiveTellsTheMirrorRuleNotTheSteps()
        {
            var m = BossRoom();
            var lord = ToPhase2(m);
            StringAssert.Contains("鏡", CombatBoss.RuleLine(lord), "段階2のルール：写し身は鏡");
            StringAssert.Contains("はね返", CombatBoss.ObjectiveSentence(m, lord));
        }

        // ───── 札の顔（アイコン＋短い文字） ─────

        [Test]
        public void CardFaceIsIconPlusShortText()
        {
            var cards = AssetDatabase.FindAssets("t:CommandData", new[] { "Assets/_Project/Data/Commands" }).Select(g => AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).Where(c => c != null).ToList();
            foreach (var c in cards)
            {
                string copy = UiStyles.FaceCopy(c, 0, out var icon, out var selfHit);
                Assert.LessOrEqual(copy.Length, 8, $"{c.name}：まねは とても短く（{copy}）");
                Assert.IsTrue(File.Exists($"Assets/_Project/Art/Generated/ui_ic_{icon}.png"), $"{c.name}：まねのアイコン ic_{icon}");
                Assert.IsTrue(File.Exists($"Assets/_Project/Art/Generated/ui_ic_{UiStyles.FaceIconKey(c)}.png"), $"{c.name}：種類のアイコン ic_{UiStyles.FaceIconKey(c)}");
            }
            Assert.IsTrue(UiStyles.FaceSelfHit(Card("Slash")), "攻撃のまねはあなたにも当たる（⚠ 自分にも）");
            Assert.IsFalse(UiStyles.FaceSelfHit(Card("Walk")), "移動は自分に当たらない");
            Assert.IsTrue(UiStyles.FaceSelfHit(Card("Push")) && UiStyles.FaceSelfHit(Card("Pull")), "押す・引くのまねで、あなたも動かされる（初見テスト3回目）");
            StringAssert.Contains("1マス引く", UiStyles.ShortText(Card("Pull")), "引き寄せは範囲の中の1体を1マス（札の顔と全文とルールが同じ）");
            Assert.AreEqual("敵も 4", UiStyles.FaceCopy(Card("Slash"), 0, out _, out _));
            Assert.AreEqual("attack", UiStyles.FaceIconKey(Card("Slash")));
            Assert.AreEqual("push", UiStyles.FaceIconKey(Card("Push")));
            Assert.AreEqual("pull", UiStyles.FaceIconKey(Card("Pull")));
            var tac = cards.First(c => c.type == CommandType.Tactic);
            Assert.AreEqual("まねされない", UiStyles.FaceCopy(tac, 0, out var tIcon, out var tHit));
            Assert.AreEqual("nocopy", tIcon);
            Assert.IsFalse(tHit);
        }

        // ───── 兵種ごとの絵（同じ絵を使い回さない） ─────

        [Test]
        public void EveryAct1EnemyKindHasItsOwnSilhouette()
        {
            var kinds = new[] { EnemyKind.Swordsman, EnemyKind.Spearman, EnemyKind.Archer, EnemyKind.Bomber, EnemyKind.Shield, EnemyKind.Chaser, EnemyKind.Mender,
                                EnemyKind.HealClone, EnemyKind.MirrorLord, EnemyKind.ExecKnight, EnemyKind.BeastKing, EnemyKind.CannonKing, EnemyKind.Normal };
            var keys = kinds.Select(VisualTheme.KindKey).ToList();
            Assert.AreEqual(keys.Count, keys.Distinct().Count(), "ACT1 の兵種はどれも違う絵：" + string.Join(",", keys));
            foreach (var k in keys)
                foreach (var f in new[] { "down", "up", "left", "right" })
                    Assert.IsTrue(File.Exists($"Assets/_Project/Art/Generated/unit_{k}_{f}.png"), $"unit_{k}_{f}.png");
        }

        /// <summary>【RC10・初見テスト2回目（5人とも）「青い槍兵に黄の A・赤い剣兵に青の B」】ふつうの兵の体の色は、記号の色（A 黄・B 空色・C 桃）と重ならない。</summary>
        [Test]
        public void SoldierBodiesDoNotWearTheLetterColours()
        {
            float Frac(string kind, float lo, float hi)
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes($"Assets/_Project/Art/Generated/unit_{kind}_down.png"));
                int n = 0, m = 0;
                foreach (var p in tex.GetPixels32())
                {
                    if (p.a < 128) continue;
                    n++;
                    Color.RGBToHSV(p, out var h, out var s, out var v);
                    if (s > 0.45f && v > 0.4f && h * 360f >= lo && h * 360f <= hi) m++;
                }
                Object.DestroyImmediate(tex);
                return n == 0 ? 0f : m / (float)n;
            }
            foreach (var k in new[] { "swordsman", "spearman", "archer", "shield", "cannon", "chaser", "healer" })
            {
                Assert.Less(Frac(k, 40f, 60f), 0.1f, $"{k} の体が A の黄色");
                Assert.Less(Frac(k, 190f, 230f), 0.1f, $"{k} の体が B の空色（RC9 までの槍兵は 32%）");
                Assert.Less(Frac(k, 315f, 350f), 0.1f, $"{k} の体が C の桃色");
            }
        }

        [Test]
        public void GoalSaysDefeatTheEnemyWhenOnlyOneRemains()
        {
            var m = BattleModel.FromStage(Stage("StageX1"));
            var foes = m.Enemies.Where(e => e.IsAlive && !e.IsObject).ToList();
            Assume.That(foes.Count >= 2, "敵が2体以上の部屋");
            StringAssert.StartsWith("敵を全員倒せ", PuzzleRules.GoalSentence(m));
            foreach (var e in foes.Skip(1)) e.Hp = 0;
            StringAssert.StartsWith("敵を倒せ", PuzzleRules.GoalSentence(m), "敵が1体なのに「全員」と言わない（初見テスト2回目）");
        }

        // ───── 5分体験の入口 ─────

        [Test]
        public void TitleShowsTheFiveMinuteExperienceProminently()
        {
            Assert.AreNotEqual(0, ActUI.TitleLayout, "RC9 の並び（5分体験が小さいボタン）ではない");
            Assert.AreEqual(3, TutorialContent.QuickSteps.Length, "5分体験は3部屋（通常リリース版）");
        }
    }
}
