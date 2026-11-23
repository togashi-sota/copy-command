using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 技の実行（ルールの中心）。Player も Enemy も同じこのコードで動く。GameObject を使わない。
    /// Execute はルール上の状態（BattleModel）を書き換え、ログの文章を返し、起きた出来事を events に足す。
    /// 見た目（光る・踏み込む・音）はここでは一切しない。出来事を受け取った BattlePresenter が再生する。
    /// 実行できない命令は「空振り」になり、何も起きない。
    /// </summary>
    public static class RulesEngine
    {
        /// <summary>【研究・解析専用の実験】true のとき、跳躍・転移の着地点がふさがっていたら手前の空きマスへ着地する（空振りしない）。ゲームは使わない。</summary>
        public static bool ExperimentLeapLandsShort;
        /// <summary>【研究・解析専用の実験】転移の修正案：0＝今／1＝壁越し不可（3マス）／2＝2マス（壁越し可）。ゲームは使わない。</summary>
        public static int ExperimentBlinkMode;
        /// <summary>【研究・解析専用の実験】跳躍の修正案：0＝今／1＝間のマスにユニットかトゲがあるときだけ跳べる。ゲームは使わない。</summary>
        public static int ExperimentJumpMode;
        /// <summary>【研究・解析専用の実験】滑走の修正案：0＝今／1＝トゲは1回の滑走で1回だけ／2＝床（トゲ・ルーン）は止まったマスだけ／3＝最大3マス。ゲームは使わない。</summary>
        public static int ExperimentSlideMode;

        /// <summary>研究用の実験の切替をすべて既定（今のルール）に戻す。ゲーム本体はステージを読み込むたびに呼ぶ（切替の消し忘れでゲームのルールが変わらないように）。</summary>
        public static void ResetExperiments()
        {
            ExperimentLeapLandsShort = false;
            ExperimentBlinkMode = 0;
            ExperimentJumpMode = 0;
            ExperimentSlideMode = 0;
            TurnRules.ExperimentSkipWhiffedMoves = false;
            TurnRules.ExperimentMultiRule = 0;
            PhaseRules.ExperimentBossV3 = null;
            ExperimentLastStandDouble = false;
        }

        /// <summary>【RC6・解析だけ】最後の1体が受けるダメージを2倍にする案（PZ_LASTDOUBLE）。</summary>
        public static bool ExperimentLastStandDouble;

        public static string Execute(BattleModel model, CommandContext ctx, List<BattleEvent> events)
        {
            events = events ?? new List<BattleEvent>();
            int start = events.Count;
            var run = new Run(model, ctx, events);
            string text = run.Execute();
            text += CheckPlates(model, events); // 【Copy Core V2】命令1つごとに踏み板を確かめる
            text += PuzzleRules.CheckMarks(model, events); // 【Puzzle v2】運び紋
            text += PuzzleRules.CheckWeak(model, events); // 【Puzzle v2】封印が弱まった（1回だけ知らせる）
            // 【Puzzle v2】仕掛けが動いた出来事に「誰の命令か（まねか）」を付ける（共鳴補給・刻写・解析の Copy Use）
            for (int i = start; i < events.Count; i++)
                if (events[i].Cause == null && PuzzleRules.IsGimmickEvent(events[i]))
                    events[i].Cause = new DamageCause { Source = ctx.Actor, Command = ctx.Record.Command, ByCopy = ctx.IsCopy, Kind = DamageKind.Direct };
            return text;
        }

        /// <summary>【検証中・Copy Core V2】鏡晶の爆発のダメージ（まわり8マス）。</summary>
        public const int CrystalDamage = 4;

        /// <summary>
        /// 【Copy Core V2】二つの踏み板：あなたが「あなた用の踏み板（p）」に、敵が「敵用の踏み板（q）」に同時に乗っていたら、扉（g）が全部開く（一度だけ）。
        /// </summary>
        public static string CheckPlates(BattleModel model, List<BattleEvent> events)
        {
            var b = model.Board;
            if (b.GatesOpen || b.Gates.Count == 0 || b.PlayerPlates.Count == 0 || b.EnemyPlates.Count == 0) return "";
            // 【Copy Core V3】詰み防止：扉の外（あなたが歩いて届く所）に生きている敵がいなければ、扉は開く（レビュー：足まね兵を倒すと扉が開かず詰んだ）
            // （運び像がいる部屋は運び像が倒れないので、この助けは要らない：研究 R10 で扉が最初から開いてしまった）
            if (!EnemyOutsideGates(model) && !model.Enemies.Any(e => e.IsAlive && e.Kind == EnemyKind.Golem))
            {
                b.GatesOpen = true;
                foreach (var g in b.Gates) events?.Add(new BattleEvent { Type = BattleEventType.GateOpened, Cell = g });
                return "／扉の外の敵がいなくなった：扉が開いた";
            }
            var p = model.Player;
            if (p == null || !p.IsAlive || !b.PlayerPlates.Contains(p.Position)) return "";
            if (!model.Enemies.Any(e => e.IsAlive && (!e.IsObject || e.Kind == EnemyKind.Golem) && b.EnemyPlates.Contains(e.Position))) return ""; // 【Puzzle v2】運び像も赤い板に乗れる
            b.GatesOpen = true;
            foreach (var g in b.Gates) events?.Add(new BattleEvent { Type = BattleEventType.GateOpened, Cell = g });
            return "／二つの踏み板が同時に踏まれた：扉が開いた";
        }

        /// <summary>あなたから歩いて届く所（閉じた扉・壁を通らない）に、生きている敵がいるか。</summary>
        static bool EnemyOutsideGates(BattleModel model)
        {
            var p = model.Player;
            if (p == null || !p.IsAlive) return true;
            var seen = new HashSet<Vector2Int> { p.Position };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(p.Position);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in AroundCells(c))
                {
                    if (seen.Contains(n) || model.Board.IsWall(n)) continue;
                    seen.Add(n);
                    var u = model.GetUnitAt(n);
                    if (u != null && u.IsAlive && !u.IsPlayer && !u.IsObject) return true;
                    queue.Enqueue(n);
                }
            }
            return false;
        }

        /// <summary>【Copy Core V3】爆薬樽の爆発（周り8マス）。</summary>
        public const int BarrelDamage = 3;

        /// <summary>【Copy Core V3】対の鏡核が全部割れた：扉が開き、鏡核の守り（BarrierRule 3：写し身の主の段階1）が割れる。</summary>
        public static string ShatterCores(BattleModel model, Vector2Int cell, List<BattleEvent> events)
        {
            string text = "／鏡核が全部割れた";
            foreach (var c in model.Board.CoreHome) events.Add(new BattleEvent { Type = BattleEventType.CoresShattered, Cell = c });
            if (model.Board.Gates.Count > 0 && !model.Board.GatesOpen)
            {
                model.Board.GatesOpen = true;
                foreach (var g in model.Board.Gates) events.Add(new BattleEvent { Type = BattleEventType.GateOpened, Cell = g });
                text += "：扉が開いた";
            }
            text += PuzzleBoss.OnCoresShattered(model, events); // 【Puzzle v2】封紋の守護者の段階3
            foreach (var u in model.Enemies.Where(e => e.IsAlive && e.Barrier > 0 && e.BarrierRule == 3).ToList())
            {
                u.Barrier = 0;
                u.BarrierBrokenRounds = 0;
                events.Add(new BattleEvent { Type = BattleEventType.BarrierBroken, Unit = u, Cell = u.Position });
                text += u.Kind == EnemyKind.Lord ? $"：{u.Name}の守りが割れた（2ラウンド傷つく）" : $"：{u.Name}の守りが割れた（もう戻らない）"; // 【Puzzle v2】守りが戻るのは写し身の主だけ（レビュー：普通の兵にも「2ラウンド」と出て急かしていた）
            }
            if (model.SoftRules) text += Resonate(model, cell, events); // 【戦闘中心 v3】共鳴（守りが割れた後に）
            return text;
        }

        /// <summary>
        /// 【戦闘中心 v3】鏡核の共鳴（Soft ルール）：倒せる敵全員に ResonanceDamage、このターンの残りは動けない（まねない・兵種の行動をしない）。
        /// 鏡の主は段階3なら -6・そのターンは動けない（CombatBoss）。鏡核は CoreRegrowRounds ラウンドの終わりに戻る。
        /// </summary>
        static string Resonate(BattleModel model, Vector2Int cell, List<BattleEvent> events)
        {
            model.Board.CoreRegrowIn = CoreRegrowRounds;
            const string head = "共鳴！敵全員に 3・このターンは動けない";
            events.Add(new BattleEvent { Type = BattleEventType.Resonance, Cell = cell, Text = head });
            string text = "／" + head;
            foreach (var u in model.Enemies.Where(e => e.IsAlive && !e.IsObject && e.Kind != EnemyKind.MirrorLord && !e.Dormant).ToList())
            {
                int dealt = ApplyDamage(model, null, null, true, u, ResonanceDamage, DamageKind.Resonance, events);
                text += $"・{u.Name}に {dealt}";
                if (u.IsAlive) u.StunRounds = Mathf.Max(u.StunRounds, 1);
            }
            text += CombatBoss.OnResonance(model, events);
            return text;
        }

        /// <summary>【Copy Core V2】鏡晶が砕けて爆発する（まわり8マスのユニットに CrystalDamage。コピー起点）。</summary>
        public static string BurstCrystal(BattleModel model, Vector2Int cell, List<BattleEvent> events)
        {
            if (!model.Board.BreakCrystal(cell)) return "";
            events.Add(new BattleEvent { Type = BattleEventType.CrystalBurst, Cell = cell });
            string text = $"／{cell} の鏡晶が砕けて爆発";
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var c = cell + new Vector2Int(dx, dy);
                    events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Cell = c, Highlight = CellHighlight.Fire });
                    var u = model.GetUnitAt(c);
                    if (u == null) continue;
                    int dealt = ApplyDamage(model, null, null, true, u, CrystalDamage, DamageKind.Crystal, events);
                    text += $"・{u.Name}に {dealt}";
                }
            return text;
        }

        /// <summary>上下左右の隣4マス（斜めは含まない）。</summary>
        public static Vector2Int[] AroundCells(Vector2Int center) => new[]
        {
            center + Vector2Int.up, center + Vector2Int.down, center + Vector2Int.left, center + Vector2Int.right,
        };

        /// <summary>上下左右へ range マスまでの攻撃マス（壁・盤面の外で止まる）。</summary>
        public static List<Vector2Int> CrossCells(BoardModel board, Vector2Int center, int range)
        {
            var cells = new List<Vector2Int>();
            foreach (var first in AroundCells(center))
            {
                Vector2Int step = first - center;
                Vector2Int cell = center;
                for (int i = 0; i < range; i++)
                {
                    cell += step;
                    if (board.IsWall(cell)) break;
                    cells.Add(cell);
                }
            }
            return cells;
        }

        /// <summary>【検証中】盾の正面からの直接攻撃・矢を減らす量（仮値）。</summary>
        public const int ShieldBlock = 3;
        /// <summary>【戦闘中心 v3】Soft ルールで、あなたの技が反響鎧兵に通る量。</summary>
        public const int EchoSoftDamage = 1;
        /// <summary>【戦闘中心 v3】鏡核の共鳴：敵全員へのダメージと、鏡核が戻るまでのラウンド。</summary>
        public const int ResonanceDamage = 3, CoreRegrowRounds = 3;
        /// <summary>
        /// 【戦闘中心 v3・検証中】剣兵の反撃（Soft ルール）：隣からあなたが直接斬ると、生き残った剣兵がその場で斬り返す（この量）。隣からなら札の種類を問わない（突き・貫通でも）。まね攻撃と、2マス以上離れた所からの突き・貫通では反撃しない（レビューで説明を直した）。
        /// 研究（2026-10-06・部屋のデザイナー）：「踏み込んで斬って離れる」で剣がだれにも当たらず、上手い人もコピーの無い世界も無傷になった → コピーを「安全に攻める手段」にする。0 で無し。
        /// </summary>
        public static int SwordCounter = 0; // 【RC3・2026-10-21 本人の初プレイ】「赤い敵を殴ったら自分も減った・理由が分からない」→ 見えない反撃は ACT1 では使わない（0）

        /// <summary>減った理由（盾・はじかれた・段階の境目で止まった）。【Puzzle v2】反響鎧ではじかれても「盾で」と出ていた（レビュー）。</summary>
        static string ShieldNote(UnitModel unit, int dealt, int raw) =>
            dealt >= raw ? "" : unit != null && unit.HasShield ? $"（盾で -{raw - dealt}）" : dealt == 0 ? "（はじかれた）" : $"（{raw - dealt} は止まった）";

        /// <summary>source が target の正面（target の向いている側の一直線上）にいるか。</summary>
        public static bool IsInFront(UnitModel target, Vector2Int sourcePos)
        {
            var d = sourcePos - target.Position;
            if (d.x != 0 && d.y != 0) return false;
            var dir = new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y));
            return dir != Vector2Int.zero && dir == target.Facing.ToOffset();
        }

        /// <summary>
        /// ダメージの共通処理（命令・兵種の行動・環境で共用）。盾：直接攻撃と矢は、正面からなら ShieldBlock 減る
        /// （トゲ・炎・呪いには効かない）。実際に与えた量を返す。
        /// </summary>
        /// <summary>【RC6】孤立：この敵が、物（樽・像など）を除いて最後の1体か（ボス・中ボスの本体は除く）。</summary>
        /// <summary>【RC6】孤立した敵の「移動をまねない」を決め直す（ラウンドの始め・まねの前）。</summary>
        public static void UpdateIsolation(BattleModel model)
        {
            foreach (var e in model.Enemies) e.NoMoveCopy = TurnRules.IsolationNoMove && model.LastStandDouble && IsLastStanding(model, e);
        }

        public static bool IsLastStanding(BattleModel model, UnitModel target) =>
            target != null && target.IsAlive && !target.IsObject && !PhaseRules.IsBossKind(target.Kind) && model.Enemies.All(e => e == target || !e.IsAlive || e.IsObject);

        /// <summary>
        /// 【RC7】刻印の上乗せ（敵が受けるダメージだけ・あなたの直接攻撃には乗らない）：まねの刃＝敵のまねた攻撃が敵に／時計の針＝敵の大技が敵に／罠師＝トゲ・床の危険が敵に。
        /// まねの刃は「攻撃」だけ（まねで歩いてトゲに入った分は罠師の方：コードレビュー「両方が重なっていた」）。
        /// </summary>
        public static int ImprintBonus(BattleModel model, UnitModel source, bool byCopy, UnitModel target, int amount, DamageKind kind)
        {
            if (amount <= 0 || target == null || target.IsPlayer) return 0;
            if (kind == DamageKind.Spike || kind == DamageKind.Fire) return model.TrapBonus;
            if (source == null || source.IsPlayer) return 0;
            if (kind == DamageKind.Clock) return model.ClockHandBonus;
            if (byCopy && (kind == DamageKind.Direct || kind == DamageKind.Trait)) return model.CopyEdgeBonus + BaseCopyEdge;
            return 0;
        }

        /// <summary>【通常リリース版・研究】敵のまねた攻撃が敵に当たると +N（基本のルール。pzsearch の PZ_COPYEDGE）。</summary>
        public static int BaseCopyEdge = 0;

        /// <summary>【通常リリース版・研究】敵のまねた攻撃があなたに当たると +N（基本のルール。pzsearch の PZ_ECHOBACK）。</summary>
        public static int BaseEchoBack = 0;

        /// <summary>【通常リリース版・研究】鏡の主の段階2・3：あなたの直接攻撃を -N（まね・大技・反射・トゲは通る。pzsearch の PZ_LORDGUARD）。</summary>
        /// <summary>【通常リリース版・研究】中ボスの本体（処刑騎士・砲王・獣王）：あなたの直接攻撃を -N（まね・大技・トゲは通る。pzsearch の PZ_MIDGUARD）。</summary>
        public static int MidBossDirectGuard = 1; // 【通常リリース版・第3巡】中ボスの本体も鎧 -1（HP の壁をやめ、護衛のまね・大技・トゲで削る）。敵の HP の水増し（×1.30）を戻したのと一緒に：Logs/rel/p5/b_hp130_p16.txt
        public static int LordDirectGuard = 2; // 【通常リリース版】-2：殴るだけ ×1.34 → ×1.59・Fan 94 → 82%（Logs/rel/core/boss_g2_h2.txt）。-3 は Fan 75%・Novice 65% できつい

        /// <summary>
        /// 【RC11・仕様9「10〜13ダメの即死を無くす」】1ターンにあなたが受けるダメージの上限（0＝無し）。V4 の戦闘だけ。pzsearch の PZ_TURNCAP。
        /// 8：初見寄りの勝ちはほぼ同じ（Novice 75 → 80%・N-Self 63 → 61%）で、10 以上のターンが 0 になる（docs/final/RC11_DESIGN.md 9章）。
        /// </summary>
        public static int PlayerTurnCap = 8;

        public static int ApplyDamage(BattleModel model, UnitModel source, CommandData command, bool byCopy, UnitModel target, int amount,
            DamageKind kind, List<BattleEvent> events)
        {
            // 【検証中・Copy Core V2】反響鎧：あなたの技では傷つかない／写し砲：倒れない／封印：割れるまで傷つかない
            bool playerDirect = CopyCore.IsPlayerDirect(source, byCopy);
            // 【Puzzle v2】封紋の守護者の段階2：剣の像のまね攻撃が主に当たると成功（-8・像の光が消える）
            if (target.Kind == EnemyKind.SealLord && byCopy && source != null && PuzzleBoss.TryStatueStrike(model, source, target, events))
                return PuzzleBoss.SuccessDamage;
            // 【Puzzle v2】封紋の守護者の窓（守りが無い間）：直接攻撃が効く。段階の境目で止まる
            if (target.Kind == EnemyKind.SealLord && target.Barrier == 0)
                return PuzzleBoss.Damage(model, target, amount, source, command, byCopy, kind, events);
            // 【戦闘中心 v3】鏡の主：癒しの写し身のまね攻撃なら -6（写し身は砕ける）。ほかは普通に減る（段階の境目で止まる）
            // 【RC10】写し身は鏡：主の攻撃（光柱・手の大技）が目覚めた写し身に当たると主へはね返る
            if (target.Kind == EnemyKind.HealClone && source != null && source.Kind == EnemyKind.MirrorLord && CombatBoss.TryReflect(model, target, source, events)) return 0;
            if (target.Kind == EnemyKind.MirrorLord)
            {
                if (byCopy && source != null && CombatBoss.TryCloneStrike(model, source, target, command, events, out var strike)) return strike;
                int lordAmount = amount + ImprintBonus(model, source, byCopy, target, amount, kind);
                string guardText = null;
                if (LordDirectGuard > 0 && playerDirect && kind == DamageKind.Direct && target.BossStage >= 1 && lordAmount > 0) { guardText = $"鏡の鎧 -{Mathf.Min(LordDirectGuard, lordAmount)}"; lordAmount = Mathf.Max(0, lordAmount - LordDirectGuard); } // 【通常リリース版・デザイナーのレビュー「ボスは殴るだけで勝てる」】段階2・3 の主は、あなたの直接攻撃を -2（まね・大技・反射・トゲは通る）
                if (guardText != null && lordAmount <= 0) { events?.Add(new BattleEvent { Type = BattleEventType.Deflected, Unit = target, Cell = target.Position, Amount = 0, Text = "鏡の鎧：あなたの攻撃は届かない（まね・大技・反射・トゲは通る）" }); return 0; } // 【通常リリース版・QA】0 になると何も出なかった
                return CombatBoss.Damage(model, target, lordAmount, source, command, byCopy, kind, events, guardText); // 【RC7・コードレビュー】刻印はボスにも乗る
            }
            // 反響鎧は「最後の1体」になると外れる（レビュー：まね以外の手段が無くなると詰んだ）
            bool echo = target.EchoArmor && playerDirect && model.Enemies.Any(e => e != target && e.IsAlive && !e.IsObject);
            // 【戦闘中心 v3】Soft ルール：反響鎧はあなたの技でも 1 だけ通る（はじかない。時間をかければ倒せる）
            bool softEcho = echo && model.SoftRules;
            if (softEcho) echo = false;
            // 【戦闘中心 v3】Soft ルール：目覚めた癒しの写し身は殴れる（遅い道）
            bool cloneImmune = target.Kind == EnemyKind.HealClone && !(model.SoftRules && !target.Dormant);
            if (echo || target.Kind == EnemyKind.Cannon || target.Kind == EnemyKind.Golem || target.Kind == EnemyKind.BladeStatue || target.Dormant || cloneImmune)
            {
                string why = !playerDirect ? null
                    : echo ? "無効！反響鎧兵には“まね攻撃”か仕掛けが必要"
                    : target.Dormant && target.Kind == EnemyKind.HealClone && CombatBoss.Find(model) != null ? "まだ像（主の段階2で目覚める）"
                    : target.Kind == EnemyKind.HealClone ? "無効！写し身は傷つかない。主を斬らせよう"
                    : target.Kind == EnemyKind.BladeStatue ? "剣の像は倒れない（主を斬らせる道具）"
                    : target.Dormant ? "まだ像（眠っている）"
                    : "道具は倒れない（数えない）";
                events.Add(new BattleEvent { Type = BattleEventType.Deflected, Unit = target, Cell = target.Position, Amount = amount, Text = why });
                return 0;
            }
            if (target.Barrier > 0)
            {
                bool counts = target.BarrierRule == 0 ? !playerDirect : target.BarrierRule == 1 && kind == DamageKind.Crystal; // 規則2（円）・3（鏡核）は攻撃では割れない
                if (counts)
                {
                    target.Barrier--;
                    events.Add(new BattleEvent { Type = BattleEventType.BarrierHit, Unit = target, Cell = target.Position, Amount = target.Barrier });
                    if (target.Barrier == 0) { target.BarrierBrokenRounds = 0; events.Add(new BattleEvent { Type = BattleEventType.BarrierBroken, Unit = target, Cell = target.Position }); }
                }
                else
                {
                    string why = !playerDirect ? null
                        : target.BarrierRule == 0 ? "守りが残っている！まね攻撃で守りを削ろう"
                        : target.BarrierRule == 3 ? "守り：鏡核を全部、同じターンに叩け"
                        : target.BarrierRule == 2 ? "守り：兵が“まね”で白い円に入ると割れる（あなたの押し出しは×）"
                        : target.BarrierRule == 1 ? "守り：鏡晶の爆発で割れる"
                        : "守り：直接は効かない";
                    if ((target.Kind == EnemyKind.Lord || target.Kind == EnemyKind.SealLord) && playerDirect) why = PuzzleBoss.DeflectText(model, target) ?? why;
                    events.Add(new BattleEvent { Type = BattleEventType.Deflected, Unit = target, Cell = target.Position, Amount = amount, Text = why });
                }
                return 0;
            }
            bool isolated2x = model.LastStandDouble || ExperimentLastStandDouble;
            if (amount > 0 && !target.IsPlayer && (model.LastStandBonus > 0 || isolated2x) && IsLastStanding(model, target)) amount = isolated2x ? amount * 2 : amount + model.LastStandBonus; // 【RC6】孤立：最後の1体は2倍 // 【RC6】孤立：最後の1体（ボス以外）は受けるダメージ +（片付けが作業にならないように）
            amount += ImprintBonus(model, source, byCopy, target, amount, kind); // 【RC7】刻印
            if (MidBossDirectGuard > 0 && amount > 0 && playerDirect && kind == DamageKind.Direct && (target.Kind == EnemyKind.ExecKnight || target.Kind == EnemyKind.CannonKing || target.Kind == EnemyKind.BeastKing)) amount = Mathf.Max(1, amount - MidBossDirectGuard); // 【通常リリース版・研究】中ボスの鎧（1 は必ず通る）
            if (amount > 0 && target.IsPlayer && byCopy && source != null && !source.IsPlayer && model.EchoBack + BaseEchoBack > 0) amount += model.EchoBack + BaseEchoBack; // 【通常リリース版】反響深度：まね返し +N
            if (amount > 0 && target.IsPlayer && byCopy && source != null && !source.IsPlayer && model.MirrorGuard > 0) amount = Mathf.Max(0, amount - model.MirrorGuard); // 鏡の盾（まね返し）
            if (target.HasShield && (kind == DamageKind.Direct || kind == DamageKind.Trait) && source != null && !(source.Kind == EnemyKind.Bomber && kind == DamageKind.Trait) && IsInFront(target, source.Position)) // 【RC2】砲兵の爆風は向きに関係なく通る（砲兵のまねた斬撃は盾で減る）
                amount = Mathf.Max(0, amount - ShieldBlock);
            // 鏡の番人：段階の境目（HP 2/3・1/3）をまたぐダメージはそこで止まり、すぐ次の段階の封印が戻る（1回の窓で段階を飛ばさない）
            bool wardenGate = false, lordGate = false;
            if (target.Kind == EnemyKind.Warden && target.IsAlive)
            {
                int ph = PhaseRules.WardenPhase(target);
                int floor = ph == 0 ? target.MaxHp * 2 / 3 : ph == 1 ? target.MaxHp / 3 : 0;
                if (ph < 2 && target.Hp - amount <= floor) { amount = target.Hp - floor; wardenGate = true; }
            }
            if (target.Kind == EnemyKind.Lord && target.IsAlive)
            {
                // 写し身の主：段階の境目（70%・35%）でダメージが止まり、すぐ次の段階へ（1回で段階を飛ばせない）
                int ph = PhaseRules.LordPhase(target);
                int floor = ph < 2 ? PhaseRules.LordFloor(target, ph) : 0;
                // 段階2：癒しの写し身が残っている間は、境目より下へ減らない（写し身に斬らせるまで次の段階へ進まない）
                if (ph == 1 && PhaseRules.ClonesLeft(model) > 0) { if (target.Hp - amount <= floor) amount = Mathf.Max(0, target.Hp - floor - 1); }
                else if (ph < 2 && target.Hp - amount <= floor) { amount = Mathf.Max(0, target.Hp - floor); lordGate = true; }
            }
            // 【Combat V4】残心：あなたが次に受けるダメージを1回だけ減らす
            if (target.IsPlayer && model.PlayerGuard > 0 && amount > 0)
            {
                amount = Mathf.Max(0, amount - model.PlayerGuard);
                model.PlayerGuard = 0;
            }
            string note = null;
            // 【RC11・仕様9「10〜13ダメの即死を無くす」】1ターンにあなたが受けるダメージは PlayerTurnCap まで（重なっても）。
            if (target.IsPlayer && model.V4 && PlayerTurnCap > 0 && amount > 0)
            {
                int room = Mathf.Max(0, PlayerTurnCap - model.PlayerTakenThisRound);
                if (amount > room) { note = $"1ターンの上限 {PlayerTurnCap}：{amount - room} 防いだ"; amount = room; }
            }
            if (softEcho && amount > EchoSoftDamage)
            {
                amount = EchoSoftDamage;
                note = $"反響鎧：あなたの技は {EchoSoftDamage} だけ。“まね攻撃”か仕掛けが当たると鎧が割れる";
            }
            target.TakeDamage(amount);
            if (target.IsPlayer) model.PlayerTakenThisRound += amount; // 【RC11】
            events.Add(new BattleEvent
            {
                Type = BattleEventType.Damaged, Unit = target, Amount = amount, HpAfter = target.Hp, Text = note, Cell = target.Position, // 【RC3】当たったマス（予告の検査に使う）
                Cause = new DamageCause { Source = source, Command = command, Kind = kind, ByCopy = byCopy },
            });
            // 【戦闘中心 v3】Soft ルール：まね攻撃・仕掛け（あなたの直接以外）が反響鎧兵に当たったら、鎧が割れて普通の兵になる
            if (model.SoftRules && target.Kind == EnemyKind.EchoKnight && !target.ArmorBroken && !playerDirect && amount > 0 && target.IsAlive)
            {
                target.ArmorBroken = true;
                events.Add(new BattleEvent { Type = BattleEventType.ArmorBroken, Unit = target, Cell = target.Position, Cause = new DamageCause { Source = source, Command = command, Kind = kind, ByCopy = byCopy } });
            }
            if (!target.IsAlive) { events.Add(new BattleEvent { Type = BattleEventType.Died, Unit = target }); PhaseRules.LeaderFell(model, target, events); } // 【RC11】部屋の主が倒れたら勝ち
            // 【戦闘中心 v3】剣兵の反撃（隣からの直接攻撃だけ・生き残ったら）
            if (model.SoftRules && SwordCounter > 0 && playerDirect && amount > 0 && target.Kind == EnemyKind.Swordsman && target.IsAlive && !CopyCore.Stunned(target) &&
                source.IsAlive && System.Math.Abs(source.Position.x - target.Position.x) + System.Math.Abs(source.Position.y - target.Position.y) == 1)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = target, Direction = source.Position - target.Position });
                events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = target, Cell = source.Position, Highlight = CellHighlight.Arrow });
                ApplyDamage(model, target, null, false, source, SwordCounter, DamageKind.Trait, events);
            }
            if (wardenGate) PhaseRules.WardenRestore(model, target, events, null);
            if (lordGate) PhaseRules.LordNextPhase(model, target, events, null);
            if (!target.IsAlive && (target.Kind == EnemyKind.Lord || target.Kind == EnemyKind.SealLord)) PhaseRules.LordDefeated(model, events);
            // 癒しの写し身：写した攻撃で主を斬ったら、その写し身は砕ける（1回で消える＝無料ループにならない）
            if (byCopy && source != null && source.Kind == EnemyKind.HealClone && source.IsAlive && target.Kind == EnemyKind.Lord)
                PhaseRules.ShatterClone(model, source, events);
            return amount;
        }

        /// <summary>
        /// ユニットをマスへ入れる共通処理（命令・追跡兵の移動で共用）。トゲなら毎回ダメージ、回復ルーンなら回復してルーンが移動。
        /// 戻り値はログに足す文。
        /// </summary>
        public static string EnterCell(BattleModel model, UnitModel unit, Vector2Int cell, UnitModel cause, CommandData command, bool byCopy,
            List<BattleEvent> events)
        {
            var from = unit.Position;
            unit.Position = cell;
            events.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = unit, From = from, To = cell, Cause = new DamageCause { Source = cause, Command = command, ByCopy = byCopy } }); // 【戦闘中心 v3】誰の命令で動いたか（搬送キルの記録）
            string text = "";
            // 【Copy Core V2】封印の円：封印の割り方が「円」（鏡の番人の第3段階）のとき、コピーで円に入ると封印が割れる（あなたの押し出しでは割れない）
            if (unit.Barrier > 0 && unit.BarrierRule == 2 && model.Board.IsSeal(cell) && !CopyCore.IsPlayerDirect(cause, byCopy))
            {
                unit.Barrier = 0;
                unit.BarrierBrokenRounds = 0;
                events.Add(new BattleEvent { Type = BattleEventType.BarrierBroken, Unit = unit, Cell = cell });
                text += $"／{unit.Name}が封印の円に入って封印が割れた";
            }
            // 【Puzzle v2】封紋の守護者：主が光る紋に入ると段階1の成功
            text += PuzzleBoss.OnEnter(model, unit, cell, cause, byCopy, events);
            // 【検証中】ワープ陣：入ったら対の出口へ（出口がふさがっていれば移らない）。プレイヤーも敵も同じ
            if (model.Board.TryWarp(cell, out var exit) && !model.IsBlocked(exit))
            {
                unit.Position = exit;
                events.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = unit, From = cell, To = exit });
                text += $"／{unit.Name}がワープ陣で {exit} へ";
                cell = exit;
            }
            if (model.Board.HasSpike(cell))
            {
                int damage = model.Board.SpikeDamage;
                // 【検証中】刻印「棘導」：1ラウンドで最初に敵がトゲを踏んだとき +SpikeFirstBonus
                if (!unit.IsPlayer && model.SpikeFirstBonus > 0 && !model.SpikeBonusUsed) { damage += model.SpikeFirstBonus; model.SpikeBonusUsed = true; }
                ApplyDamage(model, cause, command, byCopy, unit, damage, DamageKind.Spike, events);
                text += $"／{unit.Name}がトゲを踏んで {damage} ダメージ（残り HP {unit.Hp}）";
            }
            var env = model.Environment;
            // 【検証中】回復マス：最初に入ったユニット（あなたでも敵でも）だけ回復し、消えて普通の床になる
            if (unit.IsAlive && env.HealTiles.Contains(cell))
            {
                int healed = unit.Heal(env.HealTileAmount);
                env.HealTiles.Remove(cell);
                events.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = unit, Amount = healed, HpAfter = unit.Hp, Cell = cell });
                text += $"／{unit.Name}が回復マスで {healed} 回復（HP {unit.Hp}）。回復マスは消えた";
            }
            if (unit.IsAlive && env.RuneCell == cell)
            {
                int healed = unit.Heal(env.RuneHeal);
                env.ConsumeRune();
                events.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = unit, Amount = healed, HpAfter = unit.Hp, Cell = cell });
                var next = env.RuneCell;
                text += $"／{unit.Name}が回復ルーンで {healed} 回復（HP {unit.Hp}）" +
                        (next.HasValue ? $"。ルーンは {next.Value} へ移った（残り {env.RuneUsesLeft} 回）" : "。ルーンは消えた");
            }
            return text;
        }

        /// <summary>1回の命令の実行（状態をまとめて持つための小さなクラス）。</summary>
        class Run
        {
            readonly BattleModel model;
            readonly CommandContext ctx;
            readonly List<BattleEvent> events;
            UnitModel Actor => ctx.Actor;

            public Run(BattleModel model, CommandContext ctx, List<BattleEvent> events)
            {
                this.model = model;
                this.ctx = ctx;
                this.events = events;
            }

            /// <summary>【検証中】欠けた写し：敵のコピーだけダメージが copyDamageCut 少ない。</summary>
            /// 【完成版】鍛えた札：あなたが使うときだけ +Forge（敵のまねは元の強さ）。
            /// 【RC6】まね＋・まね－の札：敵のまねだけ ±CopyDelta（あなたが使うときは元のまま）。
            int Dmg(CommandData d) => ctx.IsCopy ? Mathf.Max(0, d.damage - d.copyDamageCut + ctx.Record.CopyDelta) : d.damage + (Actor.IsPlayer && ctx.Record.SourceCard != null ? ctx.Record.SourceCard.Forge : 0);

            public string Execute()
            {
                var record = ctx.Record;
                var data = record.Command;
                string head = $"{Actor.Name}：{record.ToDisplayText()} → ";
                // 向き：方向のある命令を実行すると、その方向を向く（空振りでも）。弓兵の狙い・盾の正面に使う
                if (data.UsesDirection && !(ctx.IsCopy && Actor.Kind == EnemyKind.Shield)) Actor.Facing = record.Direction; // 【RC2】盾兵はまねで向きを変えない（敵の行動で向けた盾が、まねの移動で上書きされていた：レビュー）
                // 【検証中】闘気のボーナスは、次の直接攻撃の命令（当たっても外れても）で使い切る
                if (IsDirectAttack(data.type) && !ctx.IsInnate && Actor.AttackBonus > 0) { bonus = Actor.AttackBonus; Actor.AttackBonus = 0; }
                // 【Copy Core V2】写し砲：動かない。攻撃の技だけを、一直線に撃つ（最初に当たった相手へ、その技のダメージ）
                if (Actor.Kind == EnemyKind.Cannon) return head + CannonShot(record, data);
                // 【Copy Core V3】号令の札（けしかけ・進め）：あなたは何もしない。写した敵だけが動く・斬る
                if (Actor.IsPlayer && !ctx.IsCopy && data.selfInert) return head + "号令だけ（あなたは動かない・攻撃しない。写した敵が使う）";
                switch (data.type)
                {
                    case CommandType.Walk: return head + (ctx.IsCopy && data.copyDistanceCut > 0 ? "まねは短い：動かない" : Walk(record.Direction));
                    case CommandType.Rush: return head + Rush(record.Direction, ctx.IsCopy ? Mathf.Max(0, record.Distance - data.copyDistanceCut) : record.Distance);
                    case CommandType.Slash:
                        // 【Puzzle v2】合わせ斬り：まねた敵だけ、正面と左右の3マス
                        if (ctx.IsCopy && data.copyWide) return head + Sweep(record.Direction, Dmg(data));
                        return head + Slash(record.Direction, Dmg(data), Mathf.Max(1, data.range));
                    case CommandType.Push: return head + Push(record.Direction);
                    case CommandType.AroundSlash: return head + AroundSlash(Dmg(data), Mathf.Max(1, data.range));
                    case CommandType.Pull: return head + Pull(record.Direction, Mathf.Max(2, data.range));
                    case CommandType.PlaceSpike: return head + PlaceSpike(record.Direction);
                    case CommandType.Jump:
                        if (ExperimentJumpMode == 1)
                        {
                            var middle = Actor.Position + record.Direction.ToOffset();
                            if (model.GetUnitAt(middle) == null && !model.Board.HasSpike(middle)) { Bump(Actor, middle); return head + "越えるものがない（空振り）"; }
                        }
                        // 2026-10-05 本人プレイ：「跳躍なら壁や柱も越えるのが自然」→ 間の1マスは何でも越える（壁・柱・トゲ・ユニット・回復マス：
                        // 間のマスでは何も起きない）。着地点だけを判定（壁・柱・盤面の外・ユニットなら空振り。トゲ・回復マス・ワープは着地で発動）。敵のコピーも同じ
                        return head + Leap(record.Direction.ToOffset() * 2, true, "跳躍");
                    case CommandType.Blink:
                        if (ExperimentBlinkMode == 1) return head + Leap(record.Direction.ToOffset() * 3, false, "転移");
                        if (ExperimentBlinkMode == 2) return head + Leap(record.Direction.ToOffset() * 2, true, "転移");
                        return head + Leap(record.Direction.ToOffset() * 3, true, "転移");
                    case CommandType.Diagonal: return head + Diagonal(record.Direction);
                    case CommandType.Slide: return head + Slide(record.Direction);
                    case CommandType.Swap: return head + Swap(record.Direction);
                    case CommandType.WallKick: return head + WallKick(record.Direction);
                    case CommandType.PushSlash: return head + PushSlash(record.Direction, Dmg(data));
                    case CommandType.Sweep: return head + Sweep(record.Direction, Dmg(data));
                    case CommandType.Heal:
                    {
                        // 【検証中】応急処置：自分を回復。敵がコピーすると、敵も自分を回復する（瀕死の敵に渡すと延命させる）
                        int healed = Actor.Heal(Mathf.Max(1, data.range));
                        events.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = Actor, Amount = healed, HpAfter = Actor.Hp, Cell = Actor.Position });
                        return head + $"HP を {healed} 回復（HP {Actor.Hp}）";
                    }
                    case CommandType.Aim: return head + $"{record.Direction.ToLabel()}を向いた";
                    case CommandType.Spirit:
                        Actor.AttackBonus += Mathf.Max(1, data.range);
                        return head + $"力をためた（このターン、次の直接攻撃 +{Actor.AttackBonus}）";
                    case CommandType.Pierce: return head + Pierce(record.Direction, Dmg(data), Mathf.Max(1, data.range));
                    case CommandType.Vault: return head + Vault(record.Direction);
                    // 【検証中】ACT1：盤面には何も起きない（効果は TurnManager がデッキ・命令枠に出す。敵がコピーしても何もしない）
                    case CommandType.Foresight: return head + (ctx.IsCopy ? "何も起きない" : "先読み（次のターンの始めに山札の上3枚から1枚）");
                    case CommandType.Reconsider: return head + (ctx.IsCopy ? "何も起きない" : $"再考（次のターン +{Mathf.Max(1, data.range)} 枚）");
                    case CommandType.Rally: return head + (ctx.IsCopy ? "何も起きない" : "号令");
                    case CommandType.Draw: return head + (ctx.IsCopy ? "何も起きない" : $"{data.displayName}（命令に入れたときに引いた）");
                    case CommandType.Insight: return head + (ctx.IsCopy ? "何も起きない" : "深読み（命令に入れたときに選んだ）");
                    case CommandType.Resonate: return head + (ctx.IsCopy ? "何も起きない" : "共鳴補給（まねで仕掛けが動けば、次のターン +枚）");
                    case CommandType.ReverseCopy: return head + (ctx.IsCopy ? "何も起きない" : "逆さ写し（このターン、敵は逆の順番でまね）");
                    default: return head + "（未対応の技）";
                }
            }

            /// <summary>歩く：指定方向へ1マス。壁・ユニット・盤面の外なら動かない。</summary>
            string Walk(Direction dir)
            {
                Vector2Int next = Actor.Position + dir.ToOffset();
                if (model.IsBlocked(next)) { Bump(Actor, next); return $"{BlockReason(next)}にぶつかって動けない（空振り）"; }
                return "1マス移動" + EnterCell(Actor, next);
            }

            /// <summary>突進・駆け足：指定方向へ1マスずつ、最大 distance マス。壁・ユニット・盤面の外の手前で止まる（衝突ダメージなし）。</summary>
            string Rush(Direction dir, int distance)
            {
                int moved = 0;
                string text = "";
                for (int step = 0; step < distance; step++)
                {
                    Vector2Int next = Actor.Position + dir.ToOffset();
                    if (model.IsBlocked(next))
                    {
                        Bump(Actor, next);
                        string where = moved == 0 ? "動けない（空振り）" : $"{moved}マス進んで停止";
                        return $"{BlockReason(next)}にぶつかって{where}" + text;
                    }
                    text += EnterCell(Actor, next);
                    moved++;
                    if (!Actor.IsAlive) return $"{moved}マス進んだところで倒れた" + text;
                    // 【Puzzle v2】封紋の守護者：紋で成功した・段階が変わって中央へ帰ったら、残りの歩数は進まない（パズルブレイカー：帰った後も突進が続いた）
                    if ((Actor.Kind == EnemyKind.SealLord || Actor.Kind == EnemyKind.MirrorLord) && CopyCore.Stunned(Actor)) return $"{moved}マス進んで止まった" + text;
                }
                return $"{moved}マス移動" + text;
            }

            /// <summary>斬撃・突き：指定方向の range マス以内にいる最初のユニットにダメージ（壁の向こうには届かない）。</summary>
            string Slash(Direction dir, int damage, int range)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                Vector2Int cell = Actor.Position;
                for (int i = 0; i < range; i++)
                {
                    cell += dir.ToOffset();
                    if (model.Board.IsWall(cell)) return HitObstacle(cell) ?? "誰もいない（空振り）";
                    Target(cell, CellHighlight.Attack);
                    var unit = model.GetUnitAt(cell);
                    if (unit == null) continue;
                    int dealt = Damage(unit, damage, DamageKind.Direct);
                    return $"{unit.Name}に {dealt} ダメージ{ShieldNote(unit, dealt, damage)}（残り HP {unit.Hp}）";
                }
                return "誰もいない（空振り）";
            }

            /// <summary>【検証中】押し斬り：隣のユニットにダメージを与え、倒れなければ1マス押す（押し先がふさがっていればダメージだけ）。トゲへは押せる。</summary>
            string PushSlash(Direction dir, int damage)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                Vector2Int target = Actor.Position + dir.ToOffset();
                if (model.Board.IsWall(target)) return HitObstacle(target) ?? "誰もいない（空振り）";
                Target(target, CellHighlight.Attack);
                var unit = model.GetUnitAt(target);
                if (unit == null) return "誰もいない（空振り）";
                int dealt = Damage(unit, damage, DamageKind.Direct);
                string text = $"{unit.Name}に {dealt} ダメージ{ShieldNote(unit, dealt, damage)}（残り HP {unit.Hp}）";
                if (!unit.IsAlive || unit.IsImmovable || unit.Position != target) return text; // 段階の境目で中央へ帰ったら押さない
                Vector2Int destination = target + dir.ToOffset();
                if (model.IsBlocked(destination)) { Bump(unit, destination); return text + $"／押し先が{BlockReason(destination)}で押せない"; }
                return text + "／1マス押した" + EnterCell(unit, destination);
            }

            /// <summary>【検証中】薙ぎ払い：正面のマスと、その左右のマス（計3マス）にいる全ユニットにダメージ。</summary>
            string Sweep(Direction dir, int damage)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                var front = Actor.Position + dir.ToOffset();
                var side = new Vector2Int(dir.ToOffset().y, -dir.ToOffset().x);
                string result = "";
                foreach (var cell in new[] { front + side, front, front - side })
                {
                    if (model.Board.IsWall(cell)) { var hit = HitObstacle(cell); if (hit != null) result += hit; continue; }
                    Target(cell, CellHighlight.Attack);
                    var unit = model.GetUnitAt(cell);
                    if (unit == null || !unit.IsAlive) continue;
                    int dealt = Damage(unit, damage, DamageKind.Direct);
                    result += $"{(result.Length > 0 ? "・" : "")}{unit.Name}に {dealt}{ShieldNote(unit, dealt, damage)}（残り HP {unit.Hp}）";
                }
                return result.Length > 0 ? "薙ぎ払い：" + result : "誰もいない（空振り）";
            }

            /// <summary>【検証中】貫通：正面の直線 range マス以内にいる全ユニットにダメージ（壁で止まる。味方・敵の区別なし）。</summary>
            string Pierce(Direction dir, int damage, int range)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                Vector2Int cell = Actor.Position;
                string result = "";
                for (int i = 0; i < range; i++)
                {
                    cell += dir.ToOffset();
                    if (model.Board.IsWall(cell)) { var hit = HitObstacle(cell); if (hit != null) result += hit; break; }
                    Target(cell, CellHighlight.Attack);
                    var unit = model.GetUnitAt(cell);
                    if (unit == null || !unit.IsAlive) continue;
                    int dealt = Damage(unit, damage, DamageKind.Direct);
                    result += $"{(result.Length > 0 ? "・" : "")}{unit.Name}に {dealt}{ShieldNote(unit, dealt, damage)}（残り HP {unit.Hp}）";
                }
                return result.Length > 0 ? "貫通：" + result : "誰もいない（空振り）";
            }

            /// <summary>上下左右への攻撃：周囲斬り・十字斬り（敵の固有行動）、回転斬り（カード）。方向は無い。</summary>
            string AroundSlash(int damage, int range)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Spin, Unit = Actor });
                string result = "";
                foreach (var adj in AroundCells(Actor.Position)) if (model.Board.IsCrystal(adj) || model.Board.IsCore(adj) || model.Board.IsBarrel(adj) || model.Board.IsChest(adj) || model.Board.IsSealStone(adj) || model.Board.IsStatue(adj)) { var hit = HitObstacle(adj); if (hit != null) result += hit; }
                foreach (var cell in CrossCells(model.Board, Actor.Position, range))
                {
                    Target(cell, Actor.IsPlayer ? CellHighlight.Attack : CellHighlight.Innate);
                    var unit = model.GetUnitAt(cell);
                    if (unit == null || unit == Actor) continue;
                    int dealt = Damage(unit, damage, ctx.IsInnate ? DamageKind.Innate : DamageKind.Direct);
                    result += $"{unit.Name}に {dealt} ダメージ{ShieldNote(unit, dealt, damage)}（残り HP {unit.Hp}）";
                }
                return result.Length > 0 ? result : "誰もいない（空振り）";
            }

            /// <summary>押し出し：隣のユニットを1マス押す。押した側は動かない。押し先がふさがっていたら失敗。トゲへは押せる。</summary>
            string Push(Direction dir)
            {
                events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                Vector2Int target = Actor.Position + dir.ToOffset();
                Target(target, CellHighlight.Attack);
                var unit = model.GetUnitAt(target);
                if (unit == null) return "押す相手がいない（空振り）";
                if (unit.IsImmovable) { Bump(unit, target); return $"{unit.Name}は動かない（空振り）"; }
                Vector2Int destination = target + dir.ToOffset();
                if (model.IsBlocked(destination)) { Bump(unit, destination); return $"押し先が{BlockReason(destination)}でふさがっていて失敗（空振り）"; }
                return $"{unit.Name}を1マス押した" + EnterCell(unit, destination);
            }

            /// <summary>引き寄せ：range マス以内の最初のユニットを1マス手前へ。隣にいる相手は引き寄せられない。</summary>
            string Pull(Direction dir, int range)
            {
                Vector2Int cell = Actor.Position;
                for (int i = 1; i <= range; i++)
                {
                    cell += dir.ToOffset();
                    if (model.Board.IsWall(cell)) break;
                    Target(cell, CellHighlight.Utility);
                    var unit = model.GetUnitAt(cell);
                    if (unit == null) continue;
                    if (i == 1) return $"{unit.Name}は隣にいるので引き寄せられない（空振り）";
                    if (unit.IsImmovable) return $"{unit.Name}は動かない（空振り）";
                    return $"{unit.Name}を1マス引き寄せた" + EnterCell(unit, cell - dir.ToOffset());
                }
                return "引き寄せる相手がいない（空振り）";
            }

            /// <summary>罠設置：隣の空いた床にトゲを置く（その戦闘の間ずっと残る）。</summary>
            string PlaceSpike(Direction dir)
            {
                Vector2Int target = Actor.Position + dir.ToOffset();
                if (model.IsBlocked(target)) return $"{BlockReason(target)}があって置けない（空振り）";
                if (model.Board.HasSpike(target)) return "すでにトゲがある（空振り）";
                // 2026-10-05 監査 TRAP-1：回復マス・ワープ陣の上には置けない（回復マスを潰す・ワープ先を罠にする、が強すぎた）
                if (model.Environment != null && model.Environment.HealTiles.Contains(target)) return "回復マスの上には置けない（空振り）";
                if (model.Board.Warps.ContainsKey(target)) return "ワープ陣の上には置けない（空振り）";
                model.Board.AddSpike(target);
                events.Add(new BattleEvent { Type = BattleEventType.SpikePlaced, Unit = Actor, Cell = target });
                Target(target, CellHighlight.Utility);
                return $"{target} にトゲを置いた";
            }

            /// <summary>
            /// 【検証中】跳躍（2マス先）と転移（3マス先）。2026-10-05 から跳躍も間の壁・柱を越える。間のユニット・トゲ・回復マスは無視し、
            /// 着地点だけに「入る」（トゲ・ルーンは着地点だけ）。着地点が壁・ユニット・盤面の外なら空振り。
            /// </summary>
            string Leap(Vector2Int offset, bool throughWalls, string name)
            {
                var target = Actor.Position + offset;
                if (!throughWalls)
                {
                    var step = new Vector2Int(System.Math.Sign(offset.x), System.Math.Sign(offset.y));
                    for (var c = Actor.Position + step; c != target; c += step)
                        if (model.Board.IsWall(c)) { Bump(Actor, c); return "壁があって跳べない（空振り）"; }
                }
                if (model.IsBlocked(target) && ExperimentLeapLandsShort)
                {
                    // 【研究・解析専用の実験】着地点がふさがっていたら、手前の空きマスへ（空振りしない）
                    var back = new Vector2Int(System.Math.Sign(offset.x), System.Math.Sign(offset.y));
                    for (var c = target - back; c != Actor.Position; c -= back)
                        if (!model.IsBlocked(c)) return $"{c} へ{name}（手前に着地）" + EnterCell(Actor, c);
                }
                if (model.IsBlocked(target)) { Bump(Actor, target); return $"着地点が{BlockReason(target)}でふさがっていて{name}できない（空振り）"; }
                return $"{target} へ{name}" + EnterCell(Actor, target);
            }

            /// <summary>【検証中】斜め歩：斜めに1マス（↑＝右上、→＝右下、↓＝左下、←＝左上）。行き先がふさがっていれば空振り。</summary>
            string Diagonal(Direction dir)
            {
                var target = Actor.Position + dir.ToDiagonalOffset();
                if (model.IsBlocked(target)) { Bump(Actor, target); return $"{BlockReason(target)}にぶつかって動けない（空振り）"; }
                return "斜めに1マス移動" + EnterCell(Actor, target);
            }

            /// <summary>【検証中】滑走：壁・ユニット・盤面の端の手前まで進む。通ったトゲは毎回踏む。</summary>
            string Slide(Direction dir)
            {
                if (ExperimentSlideMode != 0) return SlideExperiment(dir);
                int moved = 0;
                string text = "";
                while (true)
                {
                    var next = Actor.Position + dir.ToOffset();
                    if (model.IsBlocked(next))
                    {
                        Bump(Actor, next);
                        return (moved == 0 ? $"{BlockReason(next)}にぶつかって動けない（空振り）" : $"{moved}マス滑って{BlockReason(next)}の手前で停止") + text;
                    }
                    text += EnterCell(Actor, next);
                    moved++;
                    if (!Actor.IsAlive) return $"{moved}マス滑ったところで倒れた" + text;
                    if (moved > 40) return $"{moved}マス滑って停止（ワープの往復を止めた）" + text; // ワープ陣で同じ所を回り続けないように
                }
            }

            /// <summary>【研究・解析専用】滑走の修正案（ExperimentSlideMode 1〜3）。</summary>
            string SlideExperiment(Direction dir)
            {
                var path = new List<Vector2Int>();
                var c = Actor.Position + dir.ToOffset();
                while (!model.IsBlocked(c) && (ExperimentSlideMode != 3 || path.Count < 3)) { path.Add(c); c += dir.ToOffset(); }
                if (path.Count == 0) { Bump(Actor, c); return "動けない（空振り）"; }
                string text = "";
                bool spiked = false;
                for (int i = 0; i < path.Count; i++)
                {
                    bool last = i == path.Count - 1;
                    bool floor = ExperimentSlideMode == 2 ? last : ExperimentSlideMode == 1 ? !(spiked && model.Board.HasSpike(path[i])) : true;
                    if (model.Board.HasSpike(path[i]) && floor) spiked = true;
                    if (floor) text += EnterCell(Actor, path[i]);
                    else
                    {
                        var from = Actor.Position;
                        Actor.Position = path[i];
                        events.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = Actor, From = from, To = path[i] });
                    }
                    if (!Actor.IsAlive) return $"{i + 1}マス滑ったところで倒れた" + text;
                }
                if (!last3(path.Count)) Bump(Actor, c);
                return $"{path.Count}マス滑って停止" + text;
            }

            bool last3(int count) => ExperimentSlideMode == 3 && count == 3;

            /// <summary>【検証中】入れ替え：隣のユニットと位置を入れ替える。どちらも相手のマスに「入る」（トゲ・ルーン）。</summary>
            string Swap(Direction dir)
            {
                var target = Actor.Position + dir.ToOffset();
                Target(target, CellHighlight.Utility);
                var unit = model.GetUnitAt(target);
                if (unit == null) return "入れ替える相手がいない（空振り）";
                if (unit.IsImmovable) return $"{unit.Name}は動かない（空振り）";
                var from = Actor.Position;
                string text = RulesEngine.EnterCell(model, unit, from, Actor, ctx.Record.Command, ctx.IsCopy, events);
                text += EnterCell(Actor, target);
                return $"{unit.Name}と位置を入れ替えた" + text;
            }

            /// <summary>【検証専用】壁蹴り：背中側（選んだ方向の反対）が壁か盤面の端なら、選んだ方向へ最大3マス。背中が空いていれば1マスだけ。</summary>
            string WallKick(Direction dir)
            {
                bool braced = model.Board.IsWall(Actor.Position - dir.ToOffset());
                return (braced ? "壁を蹴って" : "蹴る壁がなく") + Rush(dir, braced ? 3 : 1);
            }

            /// <summary>【検証専用】踏み越え：正面の隣にユニットがいれば、それを越えて2マス先へ着地。正面にユニットがいなければ空振り。</summary>
            string Vault(Direction dir)
            {
                var front = Actor.Position + dir.ToOffset();
                if (model.GetUnitAt(front) == null) { Bump(Actor, front); return "越える相手がいない（空振り）"; }
                var target = front + dir.ToOffset();
                if (model.IsBlocked(target)) { Bump(Actor, target); return $"着地点が{BlockReason(target)}でふさがっていて踏み越えられない（空振り）"; }
                return $"{model.GetUnitAt(front).Name}を踏み越えて {target} へ" + EnterCell(Actor, target);
            }

            /// <summary>
            /// 【Copy Core V2】攻撃が壁のマスで止まったとき：鏡晶なら、敵のコピー（写し砲を含む）のときだけ砕けて爆発する。あなたの攻撃でははじかれる。
            /// 何も起きなければ null。
            /// </summary>
            string HitObstacle(Vector2Int cell)
            {
                if (model.Board.IsBarrel(cell))
                {
                    // 【Copy Core V3】爆薬樽：誰の攻撃でも爆発（叩いた人のダメージとして数える。敵のまねた攻撃ならコピー起点）
                    Target(cell, CellHighlight.Attack);
                    model.Board.RemoveBarrel(cell);
                    events.Add(new BattleEvent { Type = BattleEventType.BarrelBurst, Cell = cell });
                    string t = "爆薬樽が爆発";
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var c = cell + new Vector2Int(dx, dy);
                            events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Cell = c, Highlight = CellHighlight.Fire });
                            var u = model.GetUnitAt(c);
                            if (u == null || !u.IsAlive) continue;
                            int dealt = ApplyDamage(model, Actor, ctx.Record.Command, ctx.IsCopy, u, BarrelDamage, DamageKind.Fire, events);
                            t += $"・{u.Name}に {dealt}";
                        }
                    return t;
                }
                if (model.Board.IsChest(cell))
                {
                    // 【Copy Core V3】写し箱：敵のまねた攻撃でだけ開く（あなたの攻撃ははじく）
                    Target(cell, CellHighlight.Attack);
                    if (ctx.IsCopy && !Actor.IsPlayer)
                    {
                        model.Board.OpenChest(cell);
                        events.Add(new BattleEvent { Type = BattleEventType.ChestOpened, Cell = cell });
                        return "写し箱が開いた（ルーン片）";
                    }
                    events.Add(new BattleEvent { Type = BattleEventType.Deflected, Cell = cell, Text = Actor.IsPlayer ? "写し箱は“まね攻撃”でだけ開く" : null });
                    return "写し箱がはじいた（敵のまねた攻撃でだけ開く）";
                }
                if (model.Board.IsSealStone(cell))
                {
                    // 【Puzzle v2】封印石：敵のまねた攻撃でだけ割れる（写し砲も。あなたの攻撃ははじく）
                    Target(cell, CellHighlight.Attack);
                    if ((ctx.IsCopy && !Actor.IsPlayer) || PuzzleRules.SealsWeak(model)) return PuzzleRules.BreakSealStone(model, cell, events);
                    events.Add(new BattleEvent { Type = BattleEventType.Deflected, Cell = cell, Text = Actor.IsPlayer ? "封印石は“まね攻撃”でだけ割れる" : null });
                    return "封印石がはじいた（敵のまねた攻撃でだけ割れる）";
                }
                if (model.Board.IsStatue(cell))
                {
                    // 【Puzzle v2】石像：敵のまねた攻撃が当たると時計回りに 90 度回る（あなたの攻撃では回らない）
                    // 詰み防止：倒せる敵が全滅したら、封印石と同じく、あなたの攻撃でも回る（パズルブレイカー：視線でしか届かない封印石が残ると詰んだ）
                    Target(cell, CellHighlight.Attack);
                    if ((ctx.IsCopy && !Actor.IsPlayer) || PuzzleRules.SealsWeak(model)) return PuzzleRules.RotateStatue(model, cell, events);
                    events.Add(new BattleEvent { Type = BattleEventType.Deflected, Cell = cell, Text = Actor.IsPlayer ? "石像は“まね攻撃”で回る" : null });
                    return "石像は動かない（敵のまねた攻撃で回る）";
                }
                if (model.Board.IsCore(cell))
                {
                    // 【Copy Core V3】対の鏡核：誰の攻撃でも当たる（あなたとコピーで同じラウンドに全部当てる）
                    Target(cell, CellHighlight.Attack);
                    // 【Puzzle v2】封紋の守護者の部屋：核は段階3まで眠っている（パズルブレイカー：段階1・2で像のまねが核を割っていた）
                    var sealLord = PuzzleBoss.FindSealLord(model);
                    if ((sealLord != null && sealLord.BossStage < 2) || CombatBoss.CoresAsleep(model)) // 【戦闘中心 v3】鏡の主も段階3まで
                    {
                        events.Add(new BattleEvent { Type = BattleEventType.Deflected, Cell = cell, Text = Actor.IsPlayer ? "鏡核は段階3で目覚める" : null });
                        return "鏡核は眠っている（段階3で目覚める）";
                    }
                    int total = model.Board.Cores.Count();
                    // 「1つ以上はまね」：まねで攻撃できる敵（全まね・技まね）が1体も残っていなければ、あなただけでも割れる（詰み防止）
                    bool copierLeft = model.Enemies.Any(e => e.IsAlive && !e.Dormant && (e.CopyKind == CopyKind.All || e.CopyKind == CopyKind.Technique));
                    bool shattered = model.Board.HitCore(cell, (ctx.IsCopy && !Actor.IsPlayer) || !copierLeft);
                    if (shattered) return "鏡核に当たった" + ShatterCores(model, cell, events);
                    int hit = model.Board.CoreHitCount;
                    events.Add(new BattleEvent { Type = BattleEventType.CoreHit, Cell = cell, Amount = hit, HpAfter = total });
                    return $"鏡核に当たった（このターン {hit}/{total}。同じターンに全部当てると割れる）";
                }
                if (!model.Board.IsCrystal(cell)) return null;
                Target(cell, CellHighlight.Attack);
                if (ctx.IsCopy && !Actor.IsPlayer) return "鏡晶に当たった" + BurstCrystal(model, cell, events);
                events.Add(new BattleEvent { Type = BattleEventType.Deflected, Cell = cell });
                return "鏡晶がはじいた（あなたの攻撃では砕けない）";
            }

            /// <summary>【Copy Core V2】写し砲の弾：攻撃の技なら一直線に撃つ（壁・鏡晶・最初のユニットで止まる）。</summary>
            string CannonShot(CommandRecord record, CommandData data)
            {
                bool attack = IsDirectAttack(data.type) || data.damage > 0;
                if (!attack) return "写し砲は動かない（攻撃だけを撃つ）";
                int damage = Mathf.Max(1, Dmg(data)) + bonus;
                var dirs = data.type == CommandType.AroundSlash ? new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right } : new[] { record.Direction };
                string result = "";
                foreach (var dir in dirs)
                {
                    events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = Actor, Direction = dir.ToOffset() });
                    var cell = Actor.Position;
                    for (int i = 0; i < 30; i++)
                    {
                        cell += dir.ToOffset();
                        if (model.Board.IsWall(cell)) { var hit = HitObstacle(cell); if (hit != null) result += hit; break; }
                        Target(cell, CellHighlight.Arrow);
                        var unit = model.GetUnitAt(cell);
                        if (unit == null) continue;
                        int dealt = Damage(unit, damage, DamageKind.Direct);
                        result += $"{unit.Name}に {dealt}（残り HP {unit.Hp}）";
                        break;
                    }
                }
                return "写し砲が撃った：" + (result.Length > 0 ? result : "何にも当たらない");
            }

            /// <summary>ユニットをマスへ入れる（歩く・突進・押し出し・引き寄せで共通）。トゲなら入った瞬間に毎回ダメージ。</summary>
            string EnterCell(UnitModel unit, Vector2Int cell) =>
                RulesEngine.EnterCell(model, unit, cell, Actor, ctx.Record.Command, ctx.IsCopy, events);

            /// <summary>ダメージを与え、実際の量を返す（盾の正面なら減る）。</summary>
            int Damage(UnitModel target, int amount, DamageKind kind) =>
                ApplyDamage(model, Actor, ctx.Record.Command, ctx.IsCopy, target, amount + (kind == DamageKind.Direct ? bonus : 0), kind, events);

            int bonus; // 【検証中】闘気で上乗せするダメージ（この命令の間だけ）

            static bool IsDirectAttack(CommandType t) => t == CommandType.Slash || t == CommandType.PushSlash || t == CommandType.Pierce ||
                                                        t == CommandType.Sweep || t == CommandType.AroundSlash;

            void Bump(UnitModel unit, Vector2Int at) =>
                events.Add(new BattleEvent { Type = BattleEventType.Bumped, Unit = unit, From = unit.Position, To = at });

            void Target(Vector2Int cell, CellHighlight highlight) =>
                events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = Actor, Cell = cell, Highlight = highlight });

            string BlockReason(Vector2Int cell)
            {
                if (!model.Board.IsInside(cell)) return "盤面の端";
                if (model.Board.IsWall(cell)) return "壁";
                var unit = model.GetUnitAt(cell);
                return unit != null ? unit.Name : "障害物";
            }
        }
    }
}
