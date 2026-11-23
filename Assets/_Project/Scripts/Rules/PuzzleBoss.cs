using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【Puzzle v2（2026-10-06）】ボスの「今の目的」（画面に常時出す。答えの手順は出さない）と、パズル中心 ACT1 v2 のボス「封紋の守護者」。
    ///
    /// 封紋の守護者（盤面 U・HP 32・全まね・研究 docs/research/PUZZLE_V2.md）：段階ごとに「何を成立させるか」が1文。
    ///   段階1 移動：主を光る紋（o）に乗せろ（主が紋のマスに入れば成功：通り過ぎるだけでも・あなたの押し出し・引き寄せでもよい）。紋は1回で消える（予備にもう1つ）
    ///   段階2 攻撃：剣の像（A：技まね・動かない・倒れない）に主を斬らせろ。像は1回で光が消える（2体）
    ///   段階3 同期：二つの鏡核を同じラウンドに叩け（うち1つ以上は“まね”の攻撃。あなた1人では割れない）
    ///   成功＝主に -8。主はそのラウンドの残りと次のラウンド動けない（守りが無い＝直接攻撃が効く「窓」）。その後、守りが戻って主は中央へ帰る。
    ///   HP 32 → 20（段階2）→ 8（段階3）→ 0。段階の境目でダメージは止まる（窓で殴っても次の段階へは飛ばない）。段階3は成功1回で倒れる。
    ///   危険：巨大石手（ラウンドの終わりに、あなたの今のマスを予告（紫）→ 次のラウンドの終わりに落ちる（3）→ 1ラウンド休む。段階1は1マス、段階2・3は十字。主にも当たる）。
    /// 写し身の主（Copy Core V3 の CB）は文の表示だけ（ルールは PhaseRules）。
    /// </summary>
    public static class PuzzleBoss
    {
        /// <summary>成功1回のダメージ（研究の推し 8。解析で比べるため static：ゲームでは変えない）。</summary>
        public static int SuccessDamage = 8;
        public const int HandDamage = 3;
        /// <summary>窓の長さ（成功したラウンドの残り＋次のラウンド）。</summary>
        public const int WindowRounds = 2;

        public static bool IsSealLord(UnitModel u) => u != null && u.Kind == EnemyKind.SealLord;
        public static UnitModel FindSealLord(BattleModel m) => m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.SealLord && e.IsAlive);

        /// <summary>今の段階（1〜3）。</summary>
        public static int PhaseNumber(UnitModel lord) => IsSealLord(lord) || CombatBoss.Is(lord) ? lord.BossStage + 1 : PhaseRules.LordPhase(lord) + 1;

        /// <summary>その段階の下限（ここでダメージが止まって次の段階へ）。HP 32 なら 20・8・0。</summary>
        public static int Floor(UnitModel u, int stage) => stage == 0 ? (u.MaxHp * 5 + 4) / 8 : stage == 1 ? u.MaxHp / 4 : 0;

        /// <summary>
        /// 回復の上限：封紋の守護者は回復しない（パズルブレイカー：応急処置をまねさせて回復させると、紋・像を使い切っても段階の境目に届かず詰んだ）。
        /// 成功（-8）2回で必ず境目に届く（32→24→20／20→12→8）ので、予備の紋・像があれば詰まない。
        /// </summary>
        public static int HealCap(UnitModel u) => u.Hp;

        /// <summary>今の目的の一文。</summary>
        public static string ObjectiveSentence(BattleModel model, UnitModel lord)
        {
            if (CombatBoss.Is(lord)) return CombatBoss.ObjectiveSentence(model, lord); // 【戦闘中心 v3】
            if (IsSealLord(lord))
            {
                if (lord.Barrier == 0) return $"成功！主は動けない：あと{Mathf.Max(1, WindowRounds - lord.BarrierBrokenRounds)}ターン直接攻撃が効く（あと {Mathf.Max(0, lord.Hp - Floor(lord, lord.BossStage))} で次の段階）";
                switch (lord.BossStage)
                {
                    case 0: return "主を光る紋に乗せろ";
                    case 1: return "剣の像に主を斬らせろ";
                    default: return "二つの核を、同じターンに叩け";
                }
            }
            int ph = PhaseRules.LordPhase(lord);
            if (ph == 1) return "癒しの写し身に「写し身の主」を攻撃させろ（写し身は砕ける）";
            if (lord.Barrier == 0) return $"守りが割れた！あと {Mathf.Max(1, PhaseRules.LordOpenRounds - lord.BarrierBrokenRounds)} ラウンド、直接攻撃が効く";
            return ph == 0 ? "二つの鏡核を、同じラウンド中に攻撃せよ" : "主を白い円へ運べ（あなたの押し出しでは割れない）";
        }

        /// <summary>2行目（規則の確認。答えは書かない）。</summary>
        public static string RuleLine(UnitModel lord)
        {
            if (CombatBoss.Is(lord)) return CombatBoss.RuleLine(lord); // 【戦闘中心 v3】
            if (!IsSealLord(lord)) return null;
            if (lord.Barrier == 0) return $"窓が終わると守りが戻り、主（敵A）は中央へ帰る。HP {Floor(lord, lord.BossStage)} でダメージは止まる";
            switch (lord.BossStage)
            {
                case 0: return "主（敵A）は全部まねる。止まるのは当たった人だけ。紋は通るだけでよい";
                case 1: return "剣の像は動かず、あなたの技だけをまねる（1体1回）。石手は十字";
                default: return "主（敵A）も攻撃をまねる。あなた1人では割れない。石手は十字";
            }
        }

        /// <summary>段階の進み具合（✓/×）。</summary>
        public static List<GoalItem> Progress(BattleModel model, UnitModel lord)
        {
            if (CombatBoss.Is(lord)) return CombatBoss.Progress(model, lord); // 【戦闘中心 v3】
            var items = new List<GoalItem>();
            var b = model.Board;
            items.Add(new GoalItem($"段階{PhaseNumber(lord)}/3", false));
            if (IsSealLord(lord))
            {
                if (lord.Barrier == 0) { items.Add(new GoalItem($"窓 あと{Mathf.Max(1, WindowRounds - lord.BarrierBrokenRounds)}ターン", true)); return items; }
                if (lord.BossStage == 0)
                    for (int i = 0; i < b.Seals.Count; i++) items.Add(new GoalItem($"紋{(char)('A' + i)}{(b.IsLitSeal(b.Seals[i]) ? "" : " 使用済み")}", !b.IsLitSeal(b.Seals[i])));
                else if (lord.BossStage == 1)
                {
                    var st = model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue).ToList();
                    for (int i = 0; i < st.Count; i++) items.Add(new GoalItem($"剣の像{st[i].Label}{(st[i].BossStage > 0 ? " 使用済み" : "")}", st[i].BossStage > 0));
                }
                else
                {
                    var hits = new HashSet<Vector2Int>(b.CoreHitsThisRound);
                    for (int i = 0; i < b.CoreHome.Count; i++)
                        items.Add(new GoalItem(b.CoreHome.Count == 2 ? (b.CoreHome[i].x <= b.Width / 2 ? "左の核" : "右の核") : $"核{(char)('A' + i)}", hits.Contains(b.CoreHome[i])));
                    items.Add(new GoalItem("まねで1つ以上", b.CoreHitByCopy));
                }
                return items;
            }
            int stage = PhaseRules.LordPhase(lord);
            if (stage == 0 && lord.Barrier > 0)
            {
                var hits = new HashSet<Vector2Int>(b.CoreHitsThisRound);
                for (int i = 0; i < b.CoreHome.Count; i++) items.Add(new GoalItem($"核{(char)('A' + i)}", hits.Contains(b.CoreHome[i])));
            }
            else if (stage == 1)
            {
                var clones = model.Enemies.Where(e => e.Kind == EnemyKind.HealClone).ToList();
                for (int i = 0; i < clones.Count; i++) items.Add(new GoalItem($"写し身{(char)('A' + i)}{(clones[i].IsAlive ? "" : " 砕けた")}", !clones[i].IsAlive));
            }
            else if (lord.Barrier > 0) items.Add(new GoalItem("主が円の上", b.IsSeal(lord.Position)));
            if (lord.Barrier == 0 && stage != 1) items.Add(new GoalItem("守り 割れた", true));
            return items;
        }

        /// <summary>主の倒し方（直接・まね・仕掛け）。</summary>
        public static (bool direct, bool copy, bool gimmick, string note) HowToHurt(BattleModel model, UnitModel lord)
        {
            if (CombatBoss.Is(lord)) return CombatBoss.HowToHurt(model, lord); // 【戦闘中心 v3】
            if (IsSealLord(lord))
            {
                if (lord.Barrier == 0) return (true, true, true, "窓：今だけ直接攻撃が効く");
                return lord.BossStage == 0 ? (false, false, true, $"光る紋に乗せると -{SuccessDamage}")
                     : lord.BossStage == 1 ? (false, true, false, $"剣の像のまね攻撃で -{SuccessDamage}")
                     : (false, false, true, $"核2つを同じターンに -{SuccessDamage}");
            }
            int ph = PhaseRules.LordPhase(lord);
            if (ph == 1) return (true, true, false, "写し身が残る間は 40% より下へ減らない");
            if (lord.Barrier == 0) return (true, true, true, "守りが割れている：今が攻めどき");
            return (false, false, true, ph == 0 ? "守り：鏡核を同時に叩くと割れる" : "守り：白い円へ運ぶと割れる");
        }

        /// <summary>あなたの直接攻撃をはじいたときの一言。</summary>
        public static string DeflectText(BattleModel model, UnitModel lord)
        {
            if (CombatBoss.Is(lord)) return null; // 【戦闘中心 v3】鏡の主ははじかない
            if (lord.Barrier == 0) return null;
            if (IsSealLord(lord))
                return lord.BossStage == 0 ? "守り：仕掛けで崩せ（主を光る紋へ）"
                     : lord.BossStage == 1 ? "守り：仕掛けで崩せ（剣の像に主を斬らせる）"
                     : "守り：仕掛けで崩せ（核2つを同じターンに）";
            int ph = PhaseRules.LordPhase(lord);
            return ph == 0 ? "守りが残っている！二つの鏡核を同じラウンドに" : ph == 2 ? "守りが残っている！主を白い円へ運べ" : null;
        }

        // ───── 封紋の守護者のルール ─────

        /// <summary>ステージの読み込み：守り・石手・帰る場所。剣の像は段階2まで眠っている。</summary>
        public static void Setup(BattleModel model, UnitModel lord)
        {
            lord.Barrier = 1;
            lord.BarrierRule = 5; // 封紋の守護者：守りは段階の仕掛けでだけ崩れる
            lord.BossStage = 0;
            lord.Home = lord.Position;
            lord.CopyKind = CopyKind.All;
        }

        /// <summary>
        /// 主へのダメージ（窓の直接攻撃・石手）。段階の境目で止まり、境目に着いたら次の段階へ。実際に与えた量。
        /// </summary>
        public static int Damage(BattleModel model, UnitModel lord, int amount, UnitModel source, CommandData command, bool byCopy, DamageKind kind, List<BattleEvent> events)
        {
            if (!lord.IsAlive) return 0;
            int floor = Floor(lord, lord.BossStage);
            amount = Mathf.Clamp(amount, 0, lord.Hp - floor);
            lord.TakeDamage(amount);
            events?.Add(new BattleEvent
            {
                Type = BattleEventType.Damaged, Unit = lord, Amount = amount, HpAfter = lord.Hp,
                Cause = new DamageCause { Source = source, Command = command, Kind = kind, ByCopy = byCopy },
            });
            if (!lord.IsAlive)
            {
                events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = lord });
                PhaseRules.LordDefeated(model, events);
            }
            else if (lord.Hp <= floor) NextStage(model, lord, events);
            return amount;
        }

        /// <summary>段階の仕掛けが解けた：-8・窓（守りが無い・主は動けない）。戻り値＝本当に与えたダメージ（境目で止まると -8 より少なく、窓は開かない。記録の文に使う：レビュー）。</summary>
        static int Success(BattleModel model, UnitModel lord, int stageSolved, UnitModel source, List<BattleEvent> events)
        {
            int stage = lord.BossStage;
            events?.Add(new BattleEvent { Type = BattleEventType.PuzzleSolved, Unit = lord, Cell = lord.Position, Amount = stageSolved });
            int dealt = Damage(model, lord, SuccessDamage, source, null, source != null, DamageKind.Seal, events);
            if (!lord.IsAlive || lord.BossStage != stage) return dealt; // 倒れた・境目に着いて次の段階へ（窓なし）
            lord.Barrier = 0;
            lord.BarrierBrokenRounds = 0;
            events?.Add(new BattleEvent { Type = BattleEventType.BarrierBroken, Unit = lord, Cell = lord.Position });
            return dealt;
        }

        static string SuccessText(UnitModel lord, int stageBefore, int dealt) =>
            !lord.IsAlive ? $"：{lord.Name}は崩れ落ちた" : lord.BossStage != stageBefore ? $"：{lord.Name}に -{dealt}（境目に届いた：次の段階へ）" : $"：{lord.Name}に -{dealt}（窓が開いた）";

        /// <summary>主がマスに入った：段階1で光る紋なら成功（紋は1回で消える）。</summary>
        public static string OnEnter(BattleModel model, UnitModel unit, Vector2Int cell, UnitModel cause, bool byCopy, List<BattleEvent> events)
        {
            if (!IsSealLord(unit) || unit.BossStage != 0 || unit.Barrier == 0 || !model.Board.IsLitSeal(cell)) return "";
            model.Board.UseSeal(cell);
            int before = unit.BossStage;
            int dealt = Success(model, unit, 1, cause != null && !cause.IsPlayer ? cause : null, events);
            return $"／{unit.Name}が光る紋に乗った" + SuccessText(unit, before, dealt);
        }

        /// <summary>剣の像のまね攻撃が主に当たった（段階2）。像は光が消える。</summary>
        public static bool TryStatueStrike(BattleModel model, UnitModel statue, UnitModel lord, List<BattleEvent> events)
        {
            if (statue == null || statue.Kind != EnemyKind.BladeStatue || statue.Dormant || statue.BossStage > 0) return false;
            if (!IsSealLord(lord) || lord.BossStage != 1 || lord.Barrier == 0 || !lord.IsAlive) return false;
            statue.BossStage = 1; // 使用済み
            statue.Dormant = true;
            Success(model, lord, 2, statue, events);
            return true;
        }

        /// <summary>鏡核が全部割れた：段階3なら成功（-8＝撃破）。</summary>
        public static string OnCoresShattered(BattleModel model, List<BattleEvent> events)
        {
            var lord = FindSealLord(model);
            if (lord == null || lord.BossStage != 2 || lord.Barrier == 0) return "";
            int dealt = Success(model, lord, 3, null, events);
            return SuccessText(lord, 2, dealt);
        }

        static void NextStage(BattleModel model, UnitModel lord, List<BattleEvent> events)
        {
            if (!lord.IsAlive || lord.BossStage >= 2) return;
            lord.BossStage++;
            lord.Barrier = 1; // 窓は閉じる（境目を飛ばさない）
            lord.BarrierBrokenRounds = 0;
            lord.WakingUp = true; // このラウンドの残りはまねない（予告と食い違わないように）
            if (lord.BossStage == 1)
                foreach (var s in model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue && e.IsAlive && e.BossStage == 0)) s.WakingUp = true; // ラウンドの終わりに光る
            if (lord.BossStage == 2)
            {
                foreach (var s in model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue)) { s.Dormant = true; s.WakingUp = false; }
                model.Board.ResetCoreHits();
                PhaseRules.RegrowCores(model, events); // 段階1・2で割れた鏡核も戻す（詰み防止）
            }
            PhaseRules.GoHome(model, lord, events);
            events?.Add(new BattleEvent { Type = BattleEventType.PhaseChanged, Unit = lord, Cell = lord.Position, Amount = lord.BossStage + 1 });
        }

        /// <summary>
        /// 兵種の行動の段階：巨大石手（予告 → 次のラウンドに落ちる → 1ラウンド休む）。主も潰れる（守りを通る・成功ではない）。
        /// </summary>
        public static string StoneHand(BattleModel model, UnitModel lord, List<BattleEvent> events)
        {
            if (lord.BombCells.Count > 0)
            {
                string hit = "";
                foreach (var c in lord.BombCells)
                {
                    events?.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = lord, Cell = c, Highlight = CellHighlight.Fire });
                    var t = model.GetUnitAt(c);
                    if (t == null || !t.IsAlive) continue;
                    int dealt = t == lord ? Damage(model, lord, Mathf.Min(HandDamage, Mathf.Max(0, lord.Hp - Floor(lord, lord.BossStage) - 1)), lord, null, false, DamageKind.Trait, events) // 境目の1つ上で止まる（段階は仕掛けでだけ進む：レビュー）
                                          : RulesEngine.ApplyDamage(model, lord, null, false, t, HandDamage, DamageKind.Trait, events);
                    hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
                }
                lord.BombCells = new List<Vector2Int>();
                lord.BlastCd = 1; // 1ラウンド休む
                return $"{lord.Name}：巨大石手が落ちた → {(hit.Length > 0 ? hit : "誰もいない")}";
            }
            if (lord.BlastCd > 0) { lord.BlastCd--; return null; }
            if (model.Player == null || !model.Player.IsAlive) return null;
            lord.BombCells = lord.BossStage == 0 ? new List<Vector2Int> { model.Player.Position } : PhaseRules.BombShape(model, model.Player.Position);
            return $"{lord.Name}：巨大石手を予告（{model.Player.Position}{(lord.BossStage == 0 ? "" : " の十字")}・次のターンに {HandDamage}）";
        }

        /// <summary>ラウンドの終わり：剣の像が光る・窓が閉じたら守りが戻って主は中央へ。</summary>
        public static void RoundEnd(BattleModel model, List<BattleEvent> events, List<string> log)
        {
            foreach (var s in model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue && e.WakingUp).ToList())
            {
                s.WakingUp = false;
                if (s.BossStage == 0) { s.Dormant = false; events?.Add(new BattleEvent { Type = BattleEventType.Awakened, Unit = s, Cell = s.Position }); }
            }
            var lord = FindSealLord(model);
            if (lord == null || lord.Barrier > 0) return;
            if (++lord.BarrierBrokenRounds < WindowRounds) return;
            lord.Barrier = 1;
            lord.BarrierBrokenRounds = 0;
            PhaseRules.GoHome(model, lord, events);
            events?.Add(new BattleEvent { Type = BattleEventType.BarrierRestored, Unit = lord, Cell = lord.Position, Amount = 1 });
            log?.Add($"{lord.Name}の守りが戻った（主は中央へ帰った）");
        }
    }
}
