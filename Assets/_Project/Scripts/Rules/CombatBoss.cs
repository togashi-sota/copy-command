using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【戦闘中心 v3（2026-10-06）】ボス「鏡の主」（盤面 F・全まね）。Combat Boss ＋ Copy Weakness：
    /// 殴れば必ず減る（パズルを解かないと進めない段階は無い）。コピーを使うと大きく楽になる。
    ///   段階1（HP 80% より上・【RC9】前は 70%）：普通の戦闘。大剣（3ターンごと・周り8マス・3）と光柱（あなたの十字を予告 → 次のターンに3 → 1ターン休む）。
    ///   段階2（40% より上・【RC9】前は 35%）：癒しの写し身2体が目覚める（技まね・HP あり＝普通に殴って倒せる）。1体ごとに毎ターン主を +2（70% まで）。
    ///                       写し身の“まね攻撃”が主に当たると、主に -6 で、その写し身は砕ける（コピーの弱点）。
    ///   段階3（それ以下）：写し身は主に還る。大技「鏡の奔流」：3ターンごとにチャージ → 予告（主の縦と横の列＋周り8マス）→ 5。
    ///                       鏡核2つ（段階3で目覚める）を同じターンに叩く（1つ以上はまね）と共鳴：奔流が止まり（2ターン後にまた来る）、主に -6・主はそのターンだけ動けない。
    ///                       鏡核は3ターンで戻る（もう一度使える）。
    ///   段階の境目でダメージは止まり、主は中央へ帰る（1回で段階を飛ばさない）。
    /// </summary>
    public static class CombatBoss
    {
        // 【RC9・ChatGPT「HP が高いだけで長いボスにしない」】HP 48・70%/35% → HP 42・80%/40%（段階2は HP 33・段階3は HP 16 のまま＝写し身と核の段階は同じ長さ。
        //   主を殴るだけの段階1を 15 → 9 に短く：段階1 4.8 → 3.4 ターン・ボス全体 12.6 → 11.7 ターン。段階2・3が戦いの約7割。docs/final/RC9_DESIGN.md）。pzsearch の PZ_BOSSP2・PZ_BOSSP3 で比べる
        public static int Phase2Pct = 80, Phase3Pct = 40;
        public const int SlamPeriod = 3;
        /// <summary>大剣・光柱のダメージ（研究で比べるため static。ゲームでは変えない）。</summary>
        public static int SlamDamage = 3, PillarDamage = 3; // 【RC10】光柱は1ターンおき・3（毎ターン・2 は「2マス逃げる作業」：docs/final/RC10_DESIGN.md）。 【RC7】光柱 3 → 2（毎ターン来るのは同じ。怖さは残し、削り切られにくく：Logs/rc7/boss） // 研究（2026-10-06）：最初のデッキでは 3 だと楽すぎて 4 にした → 【レビュー】育ったデッキ（＋5枚）では 4 だと人寄り 79%・死亡4 → 3（人寄り 100%・被ダメ 8.3）
        /// <summary>
        /// 【研究・ボスの圧力】段階2の大剣の周期と、段階2・3で光柱が休まないか（研究 2026-10-06：Human が HP 10 からでも勝てた → 圧力を上げる）。
        /// static なのは解析で比べるため（ゲームでは変えない）。
        /// </summary>
        public static int SlamPeriodPhase2 = 2;
        public static bool PillarNoRestLater = true;
        /// <summary>
        /// 【RC6・本人「ボスはあまり攻撃してこない」】段階1でも光柱が休まない（毎ターン、あなたの十字を予告）。解析は PZ_BOSSPILLAR=0 で前の形。
        /// 比べた結果（Logs/rc6/boss）：ボスが予告しているターン 71% → 75%・ボスの被ダメ Learner 9.0 → 9.8（大技の周期を縮めるより、毎ターン何かが来る形）。
        /// </summary>
        public static bool PillarEveryTurn = true;
        /// <summary>
        /// 【RC6・レビュー「1ターン目は何も来ない」】戦闘の始めに最初の光柱を予告する。解析は PZ_BOSSFIRST=0 で前の形。
        /// 比べた結果（Logs/rc6/rules2・100 ラン）：ボスが何か予告しているターン 90〜93% → 100%。砲王 HP 38 と合わせて、ボスの被ダメが中ボスより重くなった（Learner ボス 9.4・砲王 4.8／前はボス 7.2 ＜ 砲王 7.7）。
        /// </summary>
        public static bool FirstTurnPillar = true;
        /// <summary>
        /// 【RC10・ChatGPT「光柱が毎ターン2マス逃げる作業」】光柱の出し方（比べるための切り替え。pzsearch PZ_PILLARMODE）。
        /// 0＝RC9（毎ターン・あなたの十字）／1＝隔ターン（落ちた次のターンは休む）／2＝段階で形（段階1 縦の列・段階2 横の行・段階3 十字）／3＝段階2だけ毎ターン・ほかは隔ターン。
        /// </summary>
        public static int PillarMode = 1; // 【RC10】採用＝1（隔ターン）
        /// <summary>【RC10・鏡の主の写し身は鏡】主の攻撃（光柱・手の大技）が目覚めた写し身に当たると、主へはね返る（主 -6・写し身は砕ける）。pzsearch PZ_REFLECT。</summary>
        public static bool CloneReflect = true; // 【RC10】採用
        /// <summary>【RC10】はね返されると主がひるむ：このターンの残りは何もしない（手の大技はカウントが進まず、次の光柱も予告しない）。pzsearch PZ_REFLECTSTUN。</summary>
        public static bool ReflectStagger = true; // 【RC10】採用
        /// <summary>
        /// 【RC10・初見テスト（戦術好き）「段階1は自分中心の十字を逃げる作業」】段階1の眠る像（写し身）も石の鏡：主の攻撃が当たると主へはね返る（この量・像は砕けない）。0＝はね返さない。pzsearch PZ_STATUEREFLECT。
        /// </summary>
        public static int StatueReflectDamage = 0;
        /// <summary>写し身1体が1ターンに回復する量。</summary>
        public static int CloneHeal = 2; // 【通常リリース版・研究】pzsearch の PZ_CLONEHEAL
        /// <summary>写し身のまね攻撃が主に当たったときのダメージ（写し身は砕ける）。</summary>
        public const int CloneStrikeDamage = 6;
        public const int ChargePeriod = 3, ChargeDamage = 5;
        /// <summary>段階3の共鳴で主が受けるダメージ・動けないラウンド（共鳴したラウンド＋次のラウンド）。</summary>
        public const int CoreBossDamage = 6, CoreBossStun = 1; // 【レビュー】2 だと核が戻る3ターンごとの共鳴で主が二度と動けなかった（固め）

        public static bool Is(UnitModel u) => u != null && u.Kind == EnemyKind.MirrorLord;
        public static UnitModel Find(BattleModel m) => m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord && e.IsAlive);

        /// <summary>その段階の下限（ここでダメージが止まって次の段階へ）。</summary>
        public static int Floor(UnitModel u, int stage) => stage == 0 ? u.MaxHp * Phase2Pct / 100 : stage == 1 ? u.MaxHp * Phase3Pct / 100 : 0;
        /// <summary>その段階の上限（回復はここまで：回復で段階が戻らない）。</summary>
        public static int Ceiling(UnitModel u, int stage) => stage == 0 ? u.MaxHp : Floor(u, stage - 1);
        public static int HealCap(UnitModel u) => Ceiling(u, u.BossStage);

        public static void Setup(BattleModel model, UnitModel lord)
        {
            lord.BossStage = 0;
            lord.Home = lord.Position;
            lord.CopyKind = CopyKind.All;
            lord.Barrier = 0;
            lord.SlamCd = SlamPeriod;
        }

        /// <summary>主へのダメージ（全部ここを通る）。段階の境目で止まり、境目に着いたら次の段階へ。実際に与えた量。</summary>
        public static int Damage(BattleModel model, UnitModel lord, int amount, UnitModel source, CommandData command, bool byCopy, DamageKind kind, List<BattleEvent> events, string text = null)
        {
            if (!lord.IsAlive || amount <= 0) return 0;
            int floor = Floor(lord, lord.BossStage);
            amount = Mathf.Clamp(amount, 0, lord.Hp - floor);
            lord.TakeDamage(amount);
            events?.Add(new BattleEvent
            {
                Type = BattleEventType.Damaged, Unit = lord, Amount = amount, HpAfter = lord.Hp, Text = text,
                Cause = new DamageCause { Source = source, Command = command, Kind = kind, ByCopy = byCopy },
            });
            if (!lord.IsAlive)
            {
                events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = lord });
                PhaseRules.LordDefeated(model, events);
                PhaseRules.LeaderFell(model, lord, events); // 【RC11】主が倒れたら、残った剣兵も崩れて勝ち
            }
            else if (lord.Hp <= floor && lord.BossStage < 2) NextStage(model, lord, events);
            return amount;
        }

        /// <summary>癒しの写し身のまね攻撃が主に当たった（段階2）：主に -6、写し身は砕ける。当たらなければ false。</summary>
        public static bool TryCloneStrike(BattleModel model, UnitModel clone, UnitModel lord, CommandData command, List<BattleEvent> events, out int dealt)
        {
            dealt = 0;
            if (clone == null || clone.Kind != EnemyKind.HealClone || clone.Dormant || !clone.IsAlive) return false;
            if (!Is(lord) || !lord.IsAlive || lord.BossStage != 1) return false;
            events?.Add(new BattleEvent { Type = BattleEventType.BossCloneStrike, Unit = lord, Cell = lord.Position, Amount = CloneStrikeDamage });
            clone.Hp = 0;
            events?.Add(new BattleEvent { Type = BattleEventType.CloneShattered, Cell = clone.Position, Unit = clone });
            events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = clone });
            dealt = Damage(model, lord, CloneStrikeDamage, clone, command, true, DamageKind.Seal, events);
            return true;
        }

        /// <summary>鏡核の共鳴（段階3）：奔流が止まり、主に -6・動けない（このターンと次のターン）。</summary>
        public static string OnResonance(BattleModel model, List<BattleEvent> events)
        {
            var lord = Find(model);
            if (lord == null) return "";
            if (lord.BossStage < 2) return "";
            int dealt = Damage(model, lord, CoreBossDamage, null, null, true, DamageKind.Resonance, events);
            if (!lord.IsAlive) return $"：{lord.Name}は崩れ落ちた";
            lord.StunRounds = Mathf.Max(lord.StunRounds, CoreBossStun + (model.V4 ? 1 : 0)); // 【RC2】V4：共鳴はコピー（行動の後）で起きるので、そのラウンドの終わりの回復で消えないよう+1
            lord.SlamCells = new List<Vector2Int>();
            // 【レビュー】奔流は最初からではなく「2ターン後」へ戻すだけ（3ターンごとの共鳴で奔流が一度も撃たれなかった）。核が戻る前にもう一度来る
            lord.SlamCd = ChargePeriod - 1;
            return $"：{lord.Name}に -{dealt}・鏡の奔流が止まった（このターンは動けない・奔流は2ターン後にまた来る）";
        }

        static void NextStage(BattleModel model, UnitModel lord, List<BattleEvent> events)
        {
            if (!lord.IsAlive || lord.BossStage >= 2) return;
            lord.BossStage++;
            lord.WakingUp = true; // このラウンドの残りはまねない（予告と食い違わないように）
            if (lord.BossStage == 1)
                foreach (var c in model.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive && e.Dormant)) c.WakingUp = true; // ラウンドの終わりに目覚める
            if (lord.BossStage == 2)
            {
                foreach (var c in model.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive).ToList())
                {
                    c.Hp = 0; // 主に還る（倒したことにはしない：記録は Died だけ）
                    events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = c });
                }
                lord.SlamCells = new List<Vector2Int>();
                lord.SlamCd = ChargePeriod;
                model.Board.ResetCoreHits();
                model.Board.CoreRegrowIn = 0;
                PhaseRules.RegrowCores(model, events);
            }
            PhaseRules.GoHome(model, lord, events);
            events?.Add(new BattleEvent { Type = BattleEventType.PhaseChanged, Unit = lord, Cell = lord.Position, Amount = lord.BossStage + 1 });
        }

        /// <summary>鏡核が眠っているか（段階3より前）。</summary>
        public static bool CoresAsleep(BattleModel model)
        {
            var lord = Find(model);
            return lord != null && lord.BossStage < 2;
        }

        // ───── 兵種の行動の段階：大剣（段階1・2）／鏡の奔流（段階3）＋光柱 ─────

        public static string Trait(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            // 【Combat V4】段階1・2 の大剣は、手の大技（Clocks）に置き換える。段階3の奔流は残す
            string slam = model.V4 && e.BossStage < 2 ? null : Slam(model, e, events);
            string pillar = Pillar(model, e, events);
            return slam != null && pillar != null ? slam + "　" + pillar : slam ?? pillar;
        }

        /// <summary>鏡の奔流の形：主の縦と横の列（壁で止まる）＋周り8マス。</summary>
        public static List<Vector2Int> ChargeShape(BattleModel model, Vector2Int center)
        {
            var cells = new List<Vector2Int>(Around(model, center));
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                for (var c = center + d; model.Board.IsInside(c) && !model.Board.IsWall(c); c += d) cells.Add(c);
            return cells.Distinct().ToList();
        }

        static List<Vector2Int> Around(BattleModel model, Vector2Int center)
        {
            var cells = new List<Vector2Int>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = center + new Vector2Int(dx, dy);
                    if ((dx != 0 || dy != 0) && model.Board.IsInside(c) && !model.Board.IsWall(c)) cells.Add(c);
                }
            return cells;
        }

        static string Slam(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            bool charge = e.BossStage >= 2;
            string text = "";
            if (e.SlamCd > 0 && --e.SlamCd == 0)
            {
                text += Hit(model, e, e.SlamCells, charge ? ChargeDamage : SlamDamage, charge ? "鏡の奔流" : "大剣", events);
                e.SlamCells = new List<Vector2Int>();
                e.SlamCd = charge ? ChargePeriod : e.BossStage >= 1 ? SlamPeriodPhase2 : SlamPeriod;
            }
            if (e.IsAlive && e.SlamCd == 1)
            {
                e.SlamCells = charge ? ChargeShape(model, e.Position) : Around(model, e.Position);
                text += charge ? "　次のターンに鏡の奔流（縦と横の列・周り・5）" : "　次のターンに大剣（周り8マス）";
            }
            else if (charge && e.SlamCd == 2) text += "　鏡の奔流をチャージ中（2ターン後）";
            return text.Length > 0 ? $"{e.Name}：{text.Trim()}" : null;
        }

        static string Pillar(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            if (PillarMode == 4 && e.BossStage == 0) { e.BombCells = new List<Vector2Int>(); return null; } // 【RC10・比べ】段階1は光柱なし（手の大技と護衛だけ）
            if (e.BombCells.Count > 0)
            {
                string hit = Hit(model, e, e.BombCells, PillarDamage, "光柱", events);
                e.BombCells = new List<Vector2Int>();
                e.BlastCd = 1; // 1ターン休む
                if (PillarRests(e) || e.StunRounds > 0) { e.BlastCd = 0; return $"{e.Name}：{hit.Trim()}"; } // 【RC10】隔ターン：落ちたターンは次を予告しない（次のターンに予告 → その次に落ちる）
                if (PillarNoRestLater && (e.BossStage >= 1 || PillarEveryTurn) && e.IsAlive && model.Player != null && model.Player.IsAlive)
                {
                    // 段階2・3は休まない：落ちたらすぐ次を予告
                    e.BlastCd = 0;
                    e.BombCells = PillarShape(model, e, model.Player.Position);
                    return $"{e.Name}：{hit.Trim()}　次の光柱を予告（{model.Player.Position} の十字）";
                }
                return $"{e.Name}：{hit.Trim()}";
            }
            if (e.BlastCd > 0) { e.BlastCd--; return null; }
            if (model.Player == null || !model.Player.IsAlive) return null;
            e.BombCells = PillarShape(model, e, model.Player.Position);
            return $"{e.Name}：光柱を予告（{model.Player.Position} の十字・次のターンに {PillarDamage}）";
        }

        /// <summary>【RC10】光柱が落ちたターンに次を予告しないか（隔ターン）。</summary>
        static bool PillarRests(UnitModel e) => PillarMode == 1 || PillarMode == 4 || (PillarMode == 3 && e.BossStage != 1);

        /// <summary>【RC10】光柱の形：ふつうはあなたの十字。PillarMode 2 は段階で形が変わる（段階1 縦の列・段階2 横の行・段階3 十字）。壁で止まる。</summary>
        public static List<Vector2Int> PillarShape(BattleModel model, UnitModel e, Vector2Int center)
        {
            if (PillarMode == 4 && e.BossStage == 0) return new List<Vector2Int>();
            if (PillarMode != 2 || e.BossStage >= 2) return PhaseRules.BombShape(model, center);
            var dirs = e.BossStage == 0 ? new[] { Vector2Int.up, Vector2Int.down } : new[] { Vector2Int.left, Vector2Int.right };
            var cells = new List<Vector2Int> { center };
            foreach (var d in dirs)
                for (var c = center + d; model.Board.IsInside(c) && !model.Board.IsWall(c); c += d) cells.Add(c);
            return cells;
        }

        /// <summary>
        /// 【RC10・鏡の主の写し身は鏡】主の攻撃が目覚めた写し身に当たった：主へはね返る（主 -6・写し身は砕ける）。段階2だけ。はね返したら true（写し身へのダメージは 0）。
        /// </summary>
        public static bool TryReflect(BattleModel model, UnitModel clone, UnitModel lord, List<BattleEvent> events)
        {
            if (!CloneReflect || clone == null || clone.Kind != EnemyKind.HealClone || !clone.IsAlive) return false;
            if (!Is(lord) || !lord.IsAlive) return false;
            if (clone.Dormant)
            {
                // 段階1の眠る像：石の鏡（砕けない・量は StatueReflectDamage）
                if (StatueReflectDamage <= 0 || lord.BossStage != 0) return false;
                events?.Add(new BattleEvent { Type = BattleEventType.BossReflect, Unit = lord, Cell = lord.Position, From = clone.Position, To = lord.Position, Amount = StatueReflectDamage });
                Damage(model, lord, StatueReflectDamage, clone, null, false, DamageKind.Seal, events, "反射");
                if (ReflectStagger && lord.IsAlive) lord.StunRounds = Mathf.Max(lord.StunRounds, 1);
                return true;
            }
            if (lord.BossStage != 1) return false;
            events?.Add(new BattleEvent { Type = BattleEventType.BossReflect, Unit = lord, Cell = lord.Position, From = clone.Position, To = lord.Position, Amount = CloneStrikeDamage });
            clone.Hp = 0;
            events?.Add(new BattleEvent { Type = BattleEventType.CloneShattered, Cell = clone.Position, Unit = clone });
            events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = clone });
            Damage(model, lord, CloneStrikeDamage, clone, null, false, DamageKind.Seal, events, "反射");
            if (ReflectStagger && lord.IsAlive) lord.StunRounds = Mathf.Max(lord.StunRounds, 1); // ひるむ：このラウンドの終わりに 0 に戻る
            return true;
        }

        static string Hit(BattleModel model, UnitModel e, List<Vector2Int> cells, int damage, string what, List<BattleEvent> events)
        {
            string hit = "";
            foreach (var c in cells)
            {
                events?.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Fire });
                var t = model.GetUnitAt(c);
                if (t == null || !t.IsAlive || t == e) continue;
                int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, damage, DamageKind.Trait, events);
                hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
            }
            return $"{what} → {(hit.Length > 0 ? hit : "誰もいない")}　";
        }

        // ───── 画面の文（答えの手順は書かない。何をすると得か） ─────

        public static string ObjectiveSentence(BattleModel model, UnitModel lord)
        {
            if (lord.StunRounds > 0) return lord.BossStage == 1 ? "反射！主がひるんだ：このターンはもう動かず、次の十字（光柱）も来ない" : "共鳴！主は動けない：今のうちに殴れ"; // 【RC10・初見テスト3回目「今のうちに、が命令の後に出る」】ひるみはこのターンの残りだけ
            switch (lord.BossStage)
            {
                case 0: return $"主を殴れ（HP {Floor(lord, 0)} で段階2）";
                case 1: return "写し身は鏡：主の攻撃をはね返す・主は鏡の鎧"; // 【RC10】【通常リリース版】鏡の鎧
                default: return "鏡核2つを同じターンに叩く"; // 【RC2】長くて欄から切れていた（効果は下の行）
            }
        }

        /// <summary>【レビュー】左下の部屋の一言（段階2・3）。段階1は部屋の tutorial のまま。</summary>
        public static string PhaseHint(UnitModel lord) => lord.BossStage == 1
            ? $"段階2：主は鏡の鎧（あなたの直接攻撃は -{RulesEngine.LordDirectGuard}。まね・大技・反射・トゲは そのまま）。写し身は鏡：主の攻撃（光柱・大技）が写し身に当たると主へはね返る（主 -{CloneStrikeDamage}・主はひるむ）。写し身のまね攻撃が主に当たっても -{CloneStrikeDamage}。写し身2体は毎ターン主を +{CloneHeal}。"
            : $"段階3：主は鏡の鎧のまま（直接攻撃 -{RulesEngine.LordDirectGuard}）。主は2つの鏡核の間。核を同じターンに叩く（1つ以上はまね）と共鳴：主に -{CoreBossDamage}・そのターンは動けない。核は3ターンで戻る。赤の奔流（縦横の列＋周り・{ChargeDamage}・まねの前に来る）は避けてもよい。";

        /// <summary>【RC2】左下に出す1行（短く）。</summary>
        public static string ShortHint(UnitModel lord) => lord.BossStage == 0 ? "あなたの移動で、主をトゲ・大技の範囲へ歩かせる" : lord.BossStage == 1 ? $"写し身は鏡：主の攻撃が当たると主へはね返る（-{CloneStrikeDamage}）" : $"核を同じターンに叩く＝共鳴で主 -{CoreBossDamage}・動けない"; // 【RC2】

        public static string RuleLine(UnitModel lord)
        {
            switch (lord.BossStage)
            {
                case 0: return "主は全まね（移動もまねる）。大技（手・列）も光柱（十字・1ターンおき）も紫＝床に固定"; // 【RC5】光柱はまねの前に落ちる＝赤
                case 1: return $"写し身は鏡：主の攻撃が当たるとはね返る（主 -{CloneStrikeDamage}）。まねた攻撃でも -{CloneStrikeDamage}";
                default: return $"奔流：主の縦横の列＋周りに {ChargeDamage}（紫）。共鳴で主 -{CoreBossDamage}・動けない";
            }
        }

        public static List<GoalItem> Progress(BattleModel model, UnitModel lord)
        {
            var items = new List<GoalItem> { new GoalItem($"段階{lord.BossStage + 1}/3", false) };
            var b = model.Board;
            if (lord.BossStage == 1)
            {
                var clones = model.Enemies.Where(e => e.Kind == EnemyKind.HealClone).ToList();
                for (int i = 0; i < clones.Count; i++) items.Add(new GoalItem($"写し身{(char)('A' + i)}{(clones[i].IsAlive ? $" HP{clones[i].Hp}" : " 消えた")}", !clones[i].IsAlive));
            }
            else if (lord.BossStage == 2)
            {
                if (lord.StunRounds > 0) items.Add(new GoalItem("共鳴：主は動けない", true));
                else if (!b.Cores.Any()) items.Add(new GoalItem($"鏡核 あと{b.CoreRegrowIn}ターンで戻る", false));
                else
                {
                    var hits = new HashSet<Vector2Int>(b.CoreHitsThisRound);
                    for (int i = 0; i < b.CoreHome.Count; i++)
                        items.Add(new GoalItem(b.CoreHome.Count == 2 ? (b.CoreHome[i].x <= b.Width / 2 ? "左の核" : "右の核") : $"核{(char)('A' + i)}", hits.Contains(b.CoreHome[i])));
                    items.Add(new GoalItem("まねで1つ以上", b.CoreHitByCopy));
                }
            }
            return items;
        }

        public static (bool direct, bool copy, bool gimmick, string note) HowToHurt(BattleModel model, UnitModel lord) =>
            lord.BossStage == 1 ? (true, true, true, $"主の攻撃を写し身ではね返す・写し身のまね攻撃で -{CloneStrikeDamage}")
            : lord.BossStage == 2 ? (true, true, true, $"共鳴（鏡核2つを同時）で -{CoreBossDamage}・奔流が止まる")
            : (true, true, true, "殴れば減る");

        /// <summary>敵の頭上の短い印。</summary>
        public static string Tag(UnitModel lord) =>
            (lord.StunRounds > 0 ? (lord.BossStage == 1 ? "ひるみ！" : "動けない！") : lord.BossStage == 0 ? "段階1/3" : lord.BossStage == 1 ? "段階2/3：写し身" : "段階3/3：核") + (lord.CopyKind == CopyKind.All ? "・全まね" : ""); // 【RC5・レビュー3】ボスにも まね方（体を隠す印の代わりに、この札に）

        /// <summary>【RC2】V4 の鏡の主の説明（大技は画面が別に並べる）。前の Describe は v3 の大剣・全まねのままだった。</summary>
        public static string DescribeV4(UnitModel m) =>
            $"鏡の主（全まね）：殴れば減る。あなたの移動も技もまねる（トゲや大技の範囲へ歩かせられる）。光柱（あなたの十字を予告→次のターンに {PillarDamage}・1ターンおき）。" +
            $"段階2（{Phase2Pct}%）から主は鏡の鎧（あなたの直接攻撃 -{RulesEngine.LordDirectGuard}・まね・大技・反射・トゲはそのまま）。主の斜め下の写し身2体が目覚める（1体ごとに毎ターン主を +{CloneHeal}）。写し身は鏡：主の攻撃（光柱・大技）が当たると主へはね返る（-{CloneStrikeDamage}・主はひるむ）。写し身のまね攻撃が主に当たっても -{CloneStrikeDamage}（写し身は砕ける）。" +
            $"段階3（{Phase3Pct}%）＝鏡の奔流（3ターンごと・縦と横の列＋周り・{ChargeDamage}）。鏡核2つを同じターンに叩く（1つ以上はまね）と止まり、主に -{CoreBossDamage}・そのターンは動けない。段階の境目でダメージは止まる。";

        /// <summary>敵の説明（マウスを乗せたとき）。</summary>
        public static string Describe(UnitModel m) =>
            $"鏡の主（全まね）：殴れば減る。段階1＝大剣（3ターンごと・周り8マス・{SlamDamage}）と光柱（あなたの十字を予告→次のターンに {PillarDamage}）。" +
            $"段階2（{Phase2Pct}%）＝癒しの写し身2体が目覚める（1体ごとに毎ターン主を +{CloneHeal}）。写し身のまね攻撃が主に当たると -{CloneStrikeDamage}（写し身は砕ける）。写し身は殴って倒してもよい。" +
            $"段階3（{Phase3Pct}%）＝鏡の奔流（3ターンごと・縦と横の列＋周り・{ChargeDamage}）。鏡核2つを同じターンに叩く（1つ以上はまね）と止まり（2ターン後にまた来る）、主に -{CoreBossDamage}・そのターンは動けない。段階の境目でダメージは止まる。";
    }
}
