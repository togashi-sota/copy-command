using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】ENEMY COPY PHASE のあとの2つのフェーズ（ゲームの TurnManager と解析の TurnRules で共用）。
    ///   ③ ENEMY TRAIT PHASE：敵A → 敵B … の順に、兵種の行動（弓兵の矢・追跡兵／逃げ兵の1マス移動）
    ///   ④ ENVIRONMENT PHASE：炎床が燃える → ラウンドを進める
    /// どれもランダムを使わず、PLAYER TURN 中に予告できる（ArrowCells / StepTarget / EnvironmentModel.FireCellsNow）。
    /// 兵種の行動も同じ世界ルール：矢は敵にも当たる、移動でトゲは踏まないように避ける（自分からは危険なマスに入らない）。
    /// </summary>
    public static class PhaseRules
    {
        /// <summary>弓兵の射程（仮値）。壁で止まり、最初のユニットに当たる。</summary>
        public const int ArrowRange = 4;
        /// <summary>矢のダメージ（仮値）。盾の正面なら減る。</summary>
        public const int ArrowDamage = 2;
        /// <summary>【検証中】剣兵・槍兵・砲兵のダメージ（仮値）。</summary>
        public const int SwordDamage = 3;
        public const int SpearDamage = 3;
        public const int BombDamage = 3;
        /// <summary>【検証中】騎士V3 のカウントダウン攻撃（周期・ダメージ・段階の HP 割合）。</summary>
        public const int SlamPeriod = 3, SlamDamage = 4;
        public const int BlastPeriod = 3, BlastDamage = 3;
        public const int WavePeriod = 4, WaveDamage = 3;

        /// <summary>
        /// 【研究・解析専用】騎士V3 の数値案（BossV3Study が比べる）。null なら今の仕様。
        /// ゲームでは RulesEngine.ResetExperiments() が毎回 null に戻す（ゲームの挙動は変わらない）。
        /// </summary>
        public static BossV3Variant ExperimentBossV3;

        /// <summary>
        /// 【検証中】騎士V3 の今の仕様（2026-10-04 本人依頼で案 B を採用：BossV3Study で A/B を比べ、雑なプレイへの罰が強く・新しいルールが増えない方）。
        /// 最初から3つのカウントダウン（剣3 爆4 炎6）が見えている。HP の段階は「新しい攻撃」ではなく「既存の攻撃が変わる」条件：
        /// 60% 以下で大剣の周期 3→2、30% 以下で爆発が大十字（9マス）・炎の波の周期 4→3。HP は StageData（VB＝55）。
        /// </summary>
        public static readonly BossV3Variant DefaultBossV3 = new BossV3Variant
        {
            Name = "B：最初から全部見える＋段階で変わる", Hp = 55, AllFromStart = true,
            SlamPeriod = new[] { 3, 2, 2 }, WavePeriod = new[] { 4, 4, 3 }, BigBlast = new[] { false, false, true },
        };

        /// <summary>騎士V3 の数値（解析中の案があればそれ、無ければ今の仕様）。</summary>
        public static BossV3Variant BossV3 => ExperimentBossV3 ?? DefaultBossV3;

        /// <summary>【研究】騎士V3 の数値案。段階＝0：HP 60% より上／1：60% 以下／2：30% 以下。</summary>
        public class BossV3Variant
        {
            public string Name = "";
            public int Hp = 40;
            /// <summary>true：最初から3つのカウントダウンが動いて見えている（段階で新しい攻撃が増えない）。</summary>
            public bool AllFromStart;
            public int SlamStart = 3, BlastStart = 4, WaveStart = 6;
            public int[] SlamPeriod = { 3, 3, 3 }, BlastPeriod = { 3, 3, 3 }, WavePeriod = { 4, 4, 4 };
            public int[] SlamDamage = { 4, 4, 4 }, BlastDamage = { 3, 3, 3 }, WaveDamage = { 3, 3, 3 };
            /// <summary>爆発が大十字（あなたの位置から上下左右2マス・9マス）になる段階。</summary>
            public bool[] BigBlast = { false, false, false };
        }

        /// <summary>【研究】騎士V3 の段階（0 / 1：HP 60% 以下 / 2：30% 以下）。</summary>
        public static int BossPhase(UnitModel e) => e.Hp * 10 <= e.MaxHp * 3 ? 2 : e.Hp * 10 <= e.MaxHp * 6 ? 1 : 0;

        // ───── 【検証中・Copy Core V2】鏡の番人（マイクロボス） ─────
        /// <summary>大剣（まわり8マス）の周期と威力。</summary>
        public const int WardenSlamPeriod = 3;
        public const int WardenSlamDamage = 3;
        /// <summary>封印が割れてから戻るまでのラウンド。</summary>
        public const int WardenOpenRounds = 2;

        /// <summary>段階（0：HP 2/3 より上・1：1/3 より上・2：それ以下）。</summary>
        public static int WardenPhase(UnitModel w) => w.Hp * 3 > w.MaxHp * 2 ? 0 : w.Hp * 3 > w.MaxHp ? 1 : 2;
        /// <summary>段階ごとの封印の数（0：コピー攻撃2回／1：鏡晶の爆発1回／2：封印の円に1回）。</summary>
        public static int WardenBarrier(int phase) => phase == 0 ? 2 : 1;
        public static string WardenRuleText(int rule) => rule == 0 ? "まね攻撃を当てる" : rule == 1 ? "鏡晶の爆発に巻き込む" : "まねで封印の円に入れる";

        // ───── 【検証中・Copy Core V3】写し身の主（ボス）と癒しの写し身 ─────
        // 段階1（HP 70% より上）：対の鏡核の守り。盤面の鏡核を同じラウンドに全部叩くと守りが割れ、2ラウンド傷つく（同期利用）。
        // 段階2（40% より上）：像だった癒しの写し身が目覚め、毎ラウンド主を回復する。写し身は傷つかない。
        //                     写し身は手まね：あなたの攻撃を同じ向きで写すので、主を写し身の隣へ運んで斬らせる（攻撃利用）。斬った写し身は砕ける。
        //                     写し身が残っている間、主は 40% より下へ減らない（コピーで写し身を砕かないと次の段階へ進めない）。
        // 段階3（それ以下）：写し身は主に還る。主はコピーの移動で封印の円に入ると守りが割れ、2ラウンド傷つく（移動利用）。
        // 主は全写し（あなたの移動も攻撃も写す）。3ラウンドごとに大剣（まわり8マス）。段階の境目でダメージは止まり、主は中央へ帰る。
        public const int LordSlamPeriod = 3;
        public const int LordSlamDamage = 3;
        /// <summary>守りが割れてから戻るまでのラウンド（割れたラウンドを含めて3＝その後まる2ラウンド近づいて斬れる）。</summary>
        public const int LordOpenRounds = 3;
        /// <summary>写し身1体が1ラウンドに回復する量（2体で 4）。</summary>
        public const int CloneHeal = 2;

        /// <summary>目覚めている癒しの写し身の数。</summary>
        public static int ClonesLeft(BattleModel model) => model.Enemies.Count(e => e.Kind == EnemyKind.HealClone && e.IsAlive && (!e.Dormant || e.WakingUp));

        /// <summary>写し身が砕ける（主を斬った）。</summary>
        public static void ShatterClone(BattleModel model, UnitModel clone, List<BattleEvent> events)
        {
            clone.Hp = 0;
            events?.Add(new BattleEvent { Type = BattleEventType.CloneShattered, Cell = clone.Position, Unit = clone });
            events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = clone });
            // 写し身が全部砕けたら、主は一気に 40% まで削れて段階3へ（研究 2026-10-05：最後の1ダメージを探す作業にしない）
            var lord = model.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.Lord && e.IsAlive);
            if (lord != null && LordPhase(lord) == 1 && ClonesLeft(model) == 0)
            {
                int floor = LordFloor(lord, 1);
                if (lord.Hp > floor)
                {
                    int amount = lord.Hp - floor;
                    lord.TakeDamage(amount);
                    events?.Add(new BattleEvent { Type = BattleEventType.Damaged, Unit = lord, Amount = amount, HpAfter = lord.Hp,
                        Cause = new DamageCause { Source = clone, Kind = DamageKind.Direct, ByCopy = true } });
                }
                LordNextPhase(model, lord, events, null);
            }
        }

        /// <summary>段階（0：HP 70% より上・1：35% より上・2：それ以下）。</summary>
        public static int LordPhase(UnitModel u) => u.Hp * 100 > u.MaxHp * 70 ? 0 : u.Hp * 100 > u.MaxHp * 40 ? 1 : 2;
        /// <summary>その段階の下限（ここでダメージが止まって次の段階へ）。</summary>
        public static int LordFloor(UnitModel u, int phase) => phase == 0 ? u.MaxHp * 70 / 100 : u.MaxHp * 40 / 100;
        public static string LordRuleText(int phase) => phase == 0 ? "二つの鏡核を同じラウンドに叩く" : phase == 1 ? "癒しの写し身に主を斬らせて、写し身を全部砕く（砕くと主は 40% へ）" : "まねの移動で封印の円に入れる";

        /// <summary>段階が変わった（ダメージが境目で止まったとき）。</summary>
        public static void LordNextPhase(BattleModel model, UnitModel lord, List<BattleEvent> events, List<string> log)
        {
            int ph = LordPhase(lord);
            if (ph == 1)
            {
                lord.Barrier = 0;
                lord.BarrierRule = 4; // 段階2は守りではなく「写し身が残る間は 40% より下へ減らない」
                lord.WakingUp = true; // このラウンドの残りはまねない
                foreach (var c in model.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive))
                    c.WakingUp = true; // ラウンドの終わりに目覚める（このラウンドはまだ像）
                log?.Add($"{lord.Name}：段階2。癒しの写し身が目覚めた（1体ごとに毎ラウンド主を {CloneHeal} 回復）。写し身に主を斬らせると砕ける。全部砕くまで主は次の段階へ進まない");
            }
            else
            {
                foreach (var c in model.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive).ToList())
                {
                    c.Hp = 0;
                    events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = c });
                }
                lord.Barrier = 1;
                lord.BarrierRule = 2;
                lord.BarrierBrokenRounds = 0;
                log?.Add($"{lord.Name}：段階3。写し身は主に還った。まねの移動で封印の円に入れると守りが割れる");
            }
            if (ph != 1) lord.WakingUp = true;
            LordGoHome(model, lord, events);
            events?.Add(new BattleEvent { Type = BattleEventType.PhaseChanged, Unit = lord, Cell = lord.Position, Amount = ph + 1 });
        }

        /// <summary>主の回復の上限（今の段階の上の境目）。</summary>
        public static int LordHealCap(UnitModel u)
        {
            int ph = LordPhase(u);
            return ph == 0 ? u.MaxHp : LordFloor(u, ph - 1);
        }

        /// <summary>【Puzzle v2】ボスを帰る場所へ（封紋の守護者も使う）。</summary>
        public static void GoHome(BattleModel model, UnitModel lord, List<BattleEvent> events) => LordGoHome(model, lord, events);

        /// <summary>【Puzzle v2】鏡核を元の場所に戻す（核の上に立っている人は隣の空きマスへ押し出してから）。</summary>
        public static void RegrowCores(BattleModel model, List<BattleEvent> events)
        {
            foreach (var c in model.Board.CoreHome)
            {
                if (model.Board.IsCore(c)) continue;
                var u = model.GetUnitAt(c);
                if (u != null)
                {
                    var spots = SlamAround(model, c).Where(n => model.GetUnitAt(n) == null && !model.Board.IsCore(n)).ToList();
                    if (spots.Count == 0) continue;
                    var from = u.Position;
                    u.Position = spots[0];
                    events?.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = u, From = from, To = spots[0] });
                }
                model.Board.RegrowCore(c);
            }
        }

        static void LordGoHome(BattleModel model, UnitModel lord, List<BattleEvent> events)
        {
            if (lord.Position == lord.Home || model.Board.IsWall(lord.Home)) return;
            var to = lord.Home;
            if (model.GetUnitAt(to) != null)
            {
                // 【Puzzle v2・研究】中央がふさがっていたら、隣の空きマス（上・右・下・左の順）へ帰る（前は帰らなかった）
                var free = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }.Select(d => lord.Home + d)
                    .FirstOrDefault(c => !model.Board.IsWall(c) && (model.GetUnitAt(c) == null || model.GetUnitAt(c) == lord));
                if (free == default || free == lord.Position) return;
                to = free;
            }
            var from = lord.Position;
            lord.Position = to;
            events?.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = lord, From = from, To = to });
            if (lord.SlamCells.Count > 0) lord.SlamCells = SlamAround(model, lord.Position); // 予告した大剣は、帰った場所の周りに作り直す（レビュー）
        }

        static List<Vector2Int> SlamAround(BattleModel model, Vector2Int center)
        {
            var cells = new List<Vector2Int>();
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var c = center + new Vector2Int(dx, dy);
                    if ((dx != 0 || dy != 0) && !model.Board.IsWall(c)) cells.Add(c);
                }
            return cells;
        }

        /// <summary>主が倒れたら、写し身も消える（勝ち負けを写し身で止めない）。</summary>
        /// <summary>
        /// 【RC11・本人の RC10 プレイ「ボス本体を倒したのに雑魚だけ残るのは意味が分からない。本体を倒したらクリアでいい」】
        /// 部屋の主（ボス・中ボス）が倒れたら、残りの敵（写し身・護衛）も崩れて、その場で勝ち。目的の文「○○を倒せ」と同じ。
        /// pzsearch の比べ用に切り替えを残す（PZ_LEADEREND=0 で RC10）。
        /// </summary>
        public static bool LeaderEndsBattle = true;

        /// <summary>【RC11】部屋の主（倒すと戦闘が終わる敵）：鏡の主・処刑騎士・砲王・獣王。</summary>
        public static bool IsLeader(UnitModel e) =>
            e != null && (e.Kind == EnemyKind.MirrorLord || e.Kind == EnemyKind.ExecKnight || e.Kind == EnemyKind.CannonKing || e.Kind == EnemyKind.BeastKing);

        /// <summary>【RC11】部屋の主が倒れた：残りの敵を崩す（予告も消える）。2回目は何もしない。</summary>
        public static void LeaderFell(BattleModel model, UnitModel leader, List<BattleEvent> events)
        {
            if (!LeaderEndsBattle || model == null || !model.V4 || !IsLeader(leader) || leader.IsAlive) return;
            var rest = model.Enemies.Where(x => x.IsAlive && !x.IsPlayer && x != leader).ToList();
            events?.Add(new BattleEvent { Type = BattleEventType.LeaderDown, Unit = leader, Cell = leader.Position, Amount = rest.Count });
            foreach (var e in rest)
            {
                e.Hp = 0;
                e.BombCells = new List<Vector2Int>();
                events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = e, Text = "崩れた" });
            }
        }

        public static void LordDefeated(BattleModel model, List<BattleEvent> events)
        {
            foreach (var c in model.Enemies.Where(e => e.Kind == EnemyKind.HealClone && e.IsAlive).ToList())
            {
                c.Hp = 0;
                events?.Add(new BattleEvent { Type = BattleEventType.Died, Unit = c });
            }
        }



        static string CloneHealAction(BattleModel model, UnitModel clone, List<BattleEvent> events)
        {
            if (clone.Dormant) return null;
            var lord = model.Enemies.FirstOrDefault(e => (e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord) && e.IsAlive);
            if (lord == null) return null;
            // 回復はその段階の上限（70%）まで。段階が戻らないように（研究 2026-10-05）。【Puzzle v2】封紋の守護者は段階の始めの HP まで
            int cap = lord.Kind == EnemyKind.SealLord ? PuzzleBoss.HealCap(lord) : lord.Kind == EnemyKind.MirrorLord ? CombatBoss.HealCap(lord) : LordFloor(lord, 0);
            int amount = lord.Kind == EnemyKind.MirrorLord ? CombatBoss.CloneHeal : CloneHeal;
            int healed = lord.Hp >= cap ? 0 : lord.Heal(System.Math.Min(amount, cap - lord.Hp));
            if (healed > 0) events.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = lord, Amount = healed, HpAfter = lord.Hp, Cell = lord.Position });
            return $"{clone.Name}（癒しの写し身）：主を {healed} 回復（残り HP {lord.Hp}）";
        }

        /// <summary>【戦闘中心 v3】癒し手が1ターンに回復する量。</summary>
        public static int MenderHeal = 2; // 【RC2】解析で比べるため static（pzsearch の PZ_MENDERHEAL）

        /// <summary>【戦闘中心 v3】癒し手が次に回復する仲間（HP の割合がいちばん低い・傷ついた仲間。同じなら読み順）。いなければ null。</summary>
        public static UnitModel MenderTarget(BattleModel model, UnitModel mender) =>
            model.Enemies.Where(u => u.IsAlive && u != mender && !u.IsObject && !u.Dormant && u.Kind != EnemyKind.HealClone && u.Hp < u.MaxHp)
                .OrderBy(u => u.Hp * 1000 / System.Math.Max(1, u.MaxHp)).ThenBy(u => u.Id).FirstOrDefault();

        static string MenderAction(BattleModel model, UnitModel mender, List<BattleEvent> events)
        {
            var t = MenderTarget(model, mender);
            if (t == null) return $"{mender.Name}（癒し手）：傷ついた仲間がいない";
            int healed = t.Heal(MenderHeal);
            if (healed > 0) events.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = t, Amount = healed, HpAfter = t.Hp, Cell = t.Position });
            return $"{mender.Name}（癒し手）：{t.Name}を {healed} 回復（HP {t.Hp}）";
        }

        /// <summary>光柱：予告 → 次のラウンドに爆発（十字・3）→ 1ラウンド休む。主自身には当たらない（Bomb の中で除く）。</summary>
        static string LordPillar(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            if (e.BombCells.Count > 0)
            {
                string hit = "";
                foreach (var c in e.BombCells)
                {
                    events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Fire });
                    var t = model.GetUnitAt(c);
                    if (t == null || !t.IsAlive || t == e) continue;
                    int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, BombDamage, DamageKind.Trait, events);
                    hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
                }
                e.BombCells = new List<Vector2Int>();
                e.BlastCd = 1; // 1ラウンド休む
                return $"{e.Name}：光柱が落ちた → {(hit.Length > 0 ? hit : "誰もいない")}";
            }
            if (e.BlastCd > 0) { e.BlastCd--; return null; }
            if (model.Player == null || !model.Player.IsAlive) return null;
            e.BombCells = BombShape(model, model.Player.Position);
            return $"{e.Name}：光柱を予告（{model.Player.Position} の十字・次のラウンドに3）";
        }

        static string LordSlam(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = "";
            if (e.SlamCd > 0 && --e.SlamCd == 0)
            {
                text += Hit(model, e, e.SlamCells, LordSlamDamage, "大剣", CellHighlight.Arrow, events);
                e.SlamCells = new List<Vector2Int>();
                e.SlamCd = LordPhase(e) == 0 ? LordSlamPeriod : 2; // 段階2・3は2ラウンドごと（レビュー：危険が少なすぎる）
            }
            if (e.SlamCd == 1)
            {
                e.SlamCells = new List<Vector2Int>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var c = e.Position + new Vector2Int(dx, dy);
                        if ((dx != 0 || dy != 0) && !model.Board.IsWall(c)) e.SlamCells.Add(c);
                    }
                text += "　次のラウンドに大剣（まわり8マス）";
            }
            return text.Length > 0 ? $"{e.Name}：{text.Trim()}" : null;
        }

        /// <summary>ラウンドの終わり：守りが割れて2ラウンド経ったら戻る（段階1は鏡核も戻る）。主は中央へ帰る。</summary>
        static void LordRound(BattleModel model, List<BattleEvent> events, List<string> log)
        {
            // 段階が変わったラウンドの終わり：写し身が目覚め、主はまたまねる
            foreach (var u in model.Enemies.Where(e => e.WakingUp))
            {
                u.WakingUp = false;
                // 【Puzzle v2】剣の像（封紋の守護者の段階2）：ラウンドの終わりに光る（使用済みは光らない）
                if (u.Kind == EnemyKind.BladeStatue && u.IsAlive && u.Dormant && u.BossStage == 0)
                {
                    u.Dormant = false;
                    events?.Add(new BattleEvent { Type = BattleEventType.Awakened, Unit = u, Cell = u.Position });
                    continue;
                }
                if (u.Kind == EnemyKind.HealClone && u.IsAlive && u.Dormant)
                {
                    u.Dormant = false;
                    events?.Add(new BattleEvent { Type = BattleEventType.Awakened, Unit = u, Cell = u.Position });
                }
            }
            foreach (var lord in model.Enemies.Where(e => e.Kind == EnemyKind.Lord && e.IsAlive))
            {
                int ph = LordPhase(lord);
                if (ph == 1 || lord.Barrier > 0) continue;
                // 守りが割れたラウンドの終わりに、主は中央へ帰って動けなくなる（どこで割っても近づいて斬れる：解析で窓に届かなかった）
                if (lord.BarrierBrokenRounds == 0) LordGoHome(model, lord, events);
                if (++lord.BarrierBrokenRounds < LordOpenRounds) continue;
                lord.Barrier = 1;
                lord.BarrierRule = ph == 0 ? 3 : 2;
                lord.BarrierBrokenRounds = 0;
                if (ph == 0)
                    foreach (var c in model.Board.CoreHome)
                    {
                        if (model.Board.IsCore(c)) continue;
                        // 核の場所に立っている人は隣の空きマスへ押し出してから戻す（レビュー：核が戻らず主が永久に無敵になった）
                        var u = model.GetUnitAt(c);
                        if (u != null)
                        {
                            var spots = SlamAround(model, c).Where(n => model.GetUnitAt(n) == null && !model.Board.IsCore(n)).ToList();
                            if (spots.Count == 0) continue;
                            var free = spots[0];
                            var from = u.Position;
                            u.Position = free;
                            events?.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = u, From = from, To = free });
                        }
                        model.Board.RegrowCore(c);
                    }
                LordGoHome(model, lord, events);
                events?.Add(new BattleEvent { Type = BattleEventType.BarrierRestored, Unit = lord, Cell = lord.Position, Amount = lord.Barrier });
                log?.Add($"{lord.Name}の守りが戻った（割り方：{LordRuleText(ph)}）。主は中央へ帰った");
            }
        }

        static string WardenSlam(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = "";
            if (e.SlamCd > 0 && --e.SlamCd == 0)
            {
                text += Hit(model, e, e.SlamCells, WardenSlamDamage, "大剣", CellHighlight.Arrow, events);
                e.SlamCells = new List<Vector2Int>();
                e.SlamCd = WardenSlamPeriod;
            }
            if (e.SlamCd == 1)
            {
                e.SlamCells = new List<Vector2Int>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var c = e.Position + new Vector2Int(dx, dy);
                        if ((dx != 0 || dy != 0) && !model.Board.IsWall(c)) e.SlamCells.Add(c);
                    }
                text += "　次のラウンドに大剣（まわり8マス）";
            }
            return text.Length > 0 ? $"{e.Name}：{text.Trim()}" : null;
        }

        /// <summary>ラウンドの終わり：砕けた鏡晶が戻る。割れた封印は 2 ラウンドで戻る（そのときの段階の割り方で）。</summary>
        static void WardenRound(BattleModel model, List<BattleEvent> events, List<string> log)
        {
            // 番人がいる間、砕けた鏡晶はラウンドの終わりに元の場所へ戻る（空いていれば）。
            // 兵が先に倒れても「コピーで割る」手段が無くならないようにする（CV6 の解析で、鏡晶を使い切ると封印が割れなくなった）
            if (model.Enemies.Any(e => e.Kind == EnemyKind.Warden && e.IsAlive))
                foreach (var c in model.Board.CrystalHome)
                    if (!model.Board.IsCrystal(c) && model.GetUnitAt(c) == null) model.Board.RegrowCrystal(c);
            foreach (var w in model.Enemies.Where(e => e.Kind == EnemyKind.Warden && e.IsAlive))
            {
                if (w.Barrier > 0) { w.BarrierRule = WardenPhase(w); continue; } // 封印がある間に段階が変わっても、割り方は今の段階
                if (++w.BarrierBrokenRounds < WardenOpenRounds) continue;
                WardenRestore(model, w, events, log);
            }
        }

        /// <summary>
        /// 封印を戻す（今の段階の割り方で）。鏡晶も元の場所に戻り、番人は最初の位置へ帰る。
        /// 帰るのは、敵どうしを並べたままの「空振りで殴らせ続ける」を段階ごとに作り直させるため（CV6 の解析で、並べたら一度に倒せてしまった）。
        /// </summary>
        public static void WardenRestore(BattleModel model, UnitModel w, List<BattleEvent> events, List<string> log)
        {
            int ph = WardenPhase(w);
            w.Barrier = WardenBarrier(ph);
            w.BarrierRule = ph;
            w.BarrierBrokenRounds = 0;
            foreach (var c in model.Board.CrystalHome)
                if (!model.Board.IsCrystal(c) && model.GetUnitAt(c) == null) model.Board.RegrowCrystal(c);
            if (w.Position != w.Home && !model.Board.IsWall(w.Home) && model.GetUnitAt(w.Home) == null)
            {
                var from = w.Position;
                w.Position = w.Home;
                events?.Add(new BattleEvent { Type = BattleEventType.Moved, Unit = w, From = from, To = w.Home });
            }
            events?.Add(new BattleEvent { Type = BattleEventType.BarrierRestored, Unit = w, Cell = w.Position, Amount = w.Barrier });
            log?.Add($"{w.Name}の封印が戻った（割り方：{WardenRuleText(ph)}）。番人は中央へ帰った");
        }

        public static string KindName(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Archer: return "弓兵";
                case EnemyKind.Shield: return "盾兵";
                case EnemyKind.Chaser: return "追跡兵";
                case EnemyKind.Fleer: return "逃げ兵";
                case EnemyKind.Knight: return "鏡守の騎士";
                case EnemyKind.Swordsman: return "剣兵";
                case EnemyKind.Spearman: return "槍兵";
                case EnemyKind.Bomber: return "砲兵";
                case EnemyKind.MirrorSoldier: return "鏡兵";
                case EnemyKind.KnightV2: return "鏡守の騎士";
                case EnemyKind.KnightV3: return "鏡守の騎士";
                case EnemyKind.EchoKnight: return "反響鎧兵";
                case EnemyKind.SealGuard: return "封印兵";
                case EnemyKind.Cannon: return "写し砲";
                case EnemyKind.Warden: return "鏡の番人";
                case EnemyKind.Lord: return "写し身の主";
                case EnemyKind.HealClone: return "癒しの写し身";
                case EnemyKind.Golem: return "運び像";
                case EnemyKind.SealLord: return "封紋の守護者";
                case EnemyKind.BladeStatue: return "剣の像";
                case EnemyKind.Mender: return "癒し手";
                case EnemyKind.MirrorLord: return "鏡の主";
                case EnemyKind.ExecKnight: return "処刑騎士";
                case EnemyKind.CannonKing: return "時計仕掛けの砲王";
                case EnemyKind.BeastKing: return "獣王";
                default: return "通常";
            }
        }

        /// <summary>兵種の行動を持つか（このラウンドに TRAIT PHASE で何かするか）。</summary>
        public static bool HasTrait(UnitModel u) => u.StunRounds <= 0 && !u.Silenced && (u.Clocks.Count > 0 || u.ShootsArrows || u.Kind == EnemyKind.Chaser || u.Kind == EnemyKind.Fleer || // 【戦闘中心 v3】共鳴で動けない間は何もしない
                                                    u.Kind == EnemyKind.Mender || u.Kind == EnemyKind.MirrorLord ||
                                                    u.Kind == EnemyKind.Swordsman || u.Kind == EnemyKind.Spearman || u.Kind == EnemyKind.Bomber || u.Kind == EnemyKind.KnightV2 || u.Kind == EnemyKind.KnightV3 || u.Kind == EnemyKind.Warden ||
                                                    u.Kind == EnemyKind.Lord || u.Kind == EnemyKind.SealLord || (u.Kind == EnemyKind.HealClone && !u.Dormant));

        /// <summary>【RC2】コピーのあとの兵種の行動：V4 は追撃型だけ（ほかは コピーの前に済んでいる）。それ以外は全員。
        /// 【完成版】ClockAfterCopy のときは、追撃型の行動のあとに全員の大技（時計）もここで進める。</summary>
        public static TurnOutcome TraitPhaseAfter(BattleModel model, List<BattleEvent> events, List<string> log) => TraitPhase(model, events, log, model.V4 ? 2 : 0);

        /// <summary>
        /// 【完成版・2026-10-19】大技（カウントの時計）を「まねの後」に進める（ターン順：あなた → 敵の行動 → まね → 大技・仕掛け）。
        /// ①で予告された範囲へ、同じターンのまねで敵を送り込める。false なら RC2 の順（敵の行動と大技 → まね）。
        /// </summary>
        public static bool ClockAfterCopy = true;

        /// <summary>
        /// 【RC3・2026-10-21 本人の初プレイ】「表示された未来は信用できる」：敵は動いてから攻撃しない。ターンの順番は
        /// あなたの命令 → ①敵の攻撃（ターンの初めに赤で見えていたマス：今の位置・向きのまま。あなたの位置へ狙い直さない）→ ②まね（見えていた位置から）
        /// → ③大技（赤紫）→ ④敵の位置取り（近づく・向きを変える・砲兵の照準・回復）→ ⑤次の大技の予告。④⑤の結果は次のターンの初めに見える。
        /// 前（RC2）は、敵が近づいて・あなたの方へ向き直ってから攻撃し、そのあとにまねたので、赤い予告とまねの出発点が実行中に変わった。false で RC2 の順（解析の比較用）。
        /// </summary>
        public static bool IntentOrder = true;
        public const int PartAttack = 11, PartFire = 12, PartPosition = 13, PartTelegraph = 14;

        /// <summary>兵種の行動の順番（敵・どの部分）。part 0＝行動と大技／1＝行動だけ／2＝大技（時計）だけ。ゲーム（TurnManager）と解析で同じ並びを使う。</summary>
        public static List<(UnitModel enemy, int part)> TraitSteps(BattleModel model, int pass)
        {
            var steps = new List<(UnitModel, int)>();
            var list = model.Enemies.ToList();
            bool split = model.V4 && ClockAfterCopy;
            if (model.NoTraits)
            {
                // 【RC3】はじめての戦闘：敵は まねだけ（大技があれば大技だけ。はじめての戦闘は大技も無い）
                if (pass == 2 && model.V4 && IntentOrder)
                {
                    foreach (var e in list) if (e.Clocks.Count > 0) steps.Add((e, PartFire));
                    foreach (var e in list) if (e.Clocks.Count > 0) steps.Add((e, PartTelegraph));
                }
                return steps;
            }
            if (pass == 0 || !model.V4) { foreach (var e in list) steps.Add((e, 0)); return steps; }
            if (IntentOrder)
            {
                // 【RC3】予告どおりの順番：①攻撃（見えていた赤）→ まね → ③大技 → ④位置取り → ⑤次の大技の予告
                if (pass == 1) { foreach (var e in list) steps.Add((e, PartAttack)); return steps; }
                foreach (var e in list) if (e.Clocks.Count > 0) steps.Add((e, PartFire));
                foreach (var e in list) steps.Add((e, PartPosition));
                foreach (var e in list) if (e.Clocks.Count > 0) steps.Add((e, PartTelegraph));
                return steps;
            }
            if (pass == 1)
            {
                foreach (var e in list) if (!e.AfterCopy) steps.Add((e, split ? 1 : 0)); // 【RC2】コピーの前の行動（追撃型を除く）
                return steps;
            }
            // pass 2：コピーの後
            foreach (var e in list) if (e.AfterCopy) steps.Add((e, split ? 1 : 0)); // 追撃型の行動
            if (split) foreach (var e in list) if (e.Clocks.Count > 0) steps.Add((e, 2)); // 【完成版】全員の大技
            return steps;
        }

        /// <summary>③ TRAIT PHASE（解析用：全員分まとめて）。pass 0＝全員／1＝コピーの前（追撃型以外）／2＝コピーの後（追撃型・完成版では大技も）。</summary>
        public static TurnOutcome TraitPhase(BattleModel model, List<BattleEvent> events, List<string> log, int pass = 0)
        {
            foreach (var (enemy, part) in TraitSteps(model, pass))
            {
                if (!enemy.IsAlive || !HasTrait(enemy)) continue;
                string text = TraitAction(model, enemy, events, part);
                if (text != null) log?.Add(text);
                RulesEngine.CheckPlates(model, events); // 【Copy Core V2】兵種の移動でも踏み板
                var outcome = TurnRules.Check(model);
                if (outcome != TurnOutcome.Continue) return outcome;
            }
            return TurnOutcome.Continue;
        }

        /// <summary>敵1体の兵種の行動。ログの文を返す（行動が無ければ null）。part 0＝行動と大技／1＝行動だけ／2＝大技だけ。</summary>
        public static string TraitAction(BattleModel model, UnitModel enemy, List<BattleEvent> events, int part = 0)
        {
            events = events ?? new List<BattleEvent>();
            if (!model.V4) return TraitActionBase(model, enemy, events);
            // 【Combat V4】兵種の行動 → カウント大技（時計）。沈黙なら両方止まる
            // 大技を撃つラウンド（残り①）は構えて動かない（撃つ敵は予告した場所から撃つ・突撃は射線の先へ進む）
            if (IntentOrder)
            {
                switch (part)
                {
                    case PartAttack: return V4Attack(model, enemy, events);
                    case PartFire: return Clocks.TickFire(model, enemy, events);
                    case PartPosition: return V4Position(model, enemy, events);
                    case PartTelegraph: return Clocks.TelegraphDue(model, enemy, events);
                }
            }
            bool firing = enemy.Clocks.Any(c => c.Cd == 1);
            string text = part == 2 ? null : V4Action(model, enemy, events, firing);
            if (part != 1 && enemy.IsAlive && model.Player != null && model.Player.IsAlive)
            {
                string big = Clocks.Tick(model, enemy, events);
                if (big != null) text = text == null ? big : text + "　" + big;
            }
            return text;
        }

        // ───── 【Combat V4】敵の固有行動（敵から仕掛けてくる） ─────
        /// <summary>V4 の通常の攻撃の強さ（仮値）：剣・槍・矢・爆発・噛みつき。</summary>
        public static int V4Melee = 3, V4Bomb = 3, V4Arrow = 3; // 研究：pzsearch の --melee --bomb --arrow で比べる
        /// <summary>【RC6】この戦闘の雑魚の通常の攻撃（階ごとの強さ BattleModel.EnemyDamageDelta を足す・最低 1）。</summary>
        public static int Melee(BattleModel m) => System.Math.Max(1, V4Melee + (m?.EnemyDamageDelta ?? 0) + EscalateDelta(m));
        public static int Bomb(BattleModel m) => System.Math.Max(1, V4Bomb + (m?.EnemyDamageDelta ?? 0) + EscalateDelta(m));
        public static int Arrow(BattleModel m) => System.Math.Max(1, V4Arrow + (m?.EnemyDamageDelta ?? 0) + EscalateDelta(m));
        /// <summary>【RC12】激化（目安のターンを過ぎた）の分。</summary>
        public static int EscalateDelta(BattleModel m) => m != null && m.Escalated ? m.EscalateBonus : 0;
        /// <summary>【RC6】中ボス・ボス（本体）か。</summary>
        public static bool IsBossKind(EnemyKind k) => k == EnemyKind.ExecKnight || k == EnemyKind.CannonKing || k == EnemyKind.BeastKing || k == EnemyKind.MirrorLord;

        /// <summary>【RC3】①攻撃：ターンの初めに見えていた赤いマスへ（今の位置・向きのまま。動かない・狙い直さない）。</summary>
        static string V4Attack(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            switch (e.Kind)
            {
                case EnemyKind.Swordsman: return Strike(model, e, SwordCells(model, e), Melee(model), "剣：周囲4マス", events);
                case EnemyKind.ExecKnight: return Strike(model, e, SwordCells(model, e), V4Melee + 1, "剣：周囲4マス", events);
                case EnemyKind.Chaser: return Strike(model, e, SwordCells(model, e), Melee(model), "噛みつき：周囲4マス", events);
                case EnemyKind.Spearman: return Strike(model, e, SpearCells(model, e), Melee(model), "槍：正面2マス", events);
                case EnemyKind.Archer: return Shoot(model, e, events);
                case EnemyKind.Bomber: return BombExplode(model, e, events);
                case EnemyKind.MirrorLord: return CombatBoss.Trait(model, e, events); // 光柱・奔流はどれも予告（【RC5】赤：敵の攻撃の段＝まねの前）どおり
                default: return null;
            }
        }

        /// <summary>【RC3】④位置取り（まねと大技のあと）：近づく・あなたの方を向く・照準・回復。ここで決まった向き・照準が、次のターンの赤い予告になる。</summary>
        static string V4Position(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            var p = model.Player;
            if (p == null || !p.IsAlive || !e.IsAlive || model.NoPositioning) return null;
            switch (e.Kind)
            {
                case EnemyKind.Swordsman:
                    // 【RC4・本人「敵が攻撃のあと毎回1マス近づく」】剣兵は毎ターンは近づかない（あなたの命令を全部まねて動く）。いつ近づくかは SwordsmanSteps
                    return !KindCadence || SwordsmanSteps(model, e) ? Approach(model, e, events, true) : null;
                case EnemyKind.ExecKnight:
                case EnemyKind.BeastKing:
                    return Approach(model, e, events, true);
                case EnemyKind.Chaser:
                {
                    // 追跡兵：速い（2マスまで近づく）。隣に来たら止まる
                    string a = Approach(model, e, events, true);
                    if (e.IsAlive && StepTargetToward(model, e, true).HasValue) a += "・" + Approach(model, e, events, true);
                    return a;
                }
                case EnemyKind.Spearman:
                {
                    // 【RC4】槍兵は位置を保って、あなたの方を向くだけ（向き＝次の赤）
                    string a = !KindCadence || SpearSteps(model, e) ? Approach(model, e, events, true) : "動かない"; // 【RC5】離れすぎたら詰める（SpearMode）
                    FaceToward(e, p.Position);
                    return a + $"・{e.Facing.ToLabel()}を向く";
                }
                case EnemyKind.Archer:
                {
                    string a = AlignStep(model, e, events);
                    var aim = AimDir(model, e);
                    if (aim.HasValue) e.Facing = aim.Value; else FaceToward(e, p.Position);
                    return (a ?? "動かない") + $"・{e.Facing.ToLabel()}を狙う";
                }
                case EnemyKind.Bomber:
                    e.BombCells = BombShape(model, p.Position);
                    return $"{e.Name}（{KindName(e.Kind)}）：次の爆発を予告（あなたのいるマスの十字）";
                case EnemyKind.Shield: return ShieldFace(model, e);
                case EnemyKind.Mender: return MenderAction(model, e, events);
                case EnemyKind.HealClone: return CloneHealAction(model, e, events);
                default: return null;
            }
        }

        /// <summary>
        /// 【RC4】敵の自分の移動は兵種の個性（全員が毎ターン「あなたへ1マス」はやめる）：
        /// 剣兵＝動かない（まねで動く）・槍兵＝動かない（向くだけ）・弓兵＝あなたの行・列へ1マス・追跡兵＝2マスまで近づく・砲兵・盾兵・癒し手＝動かない・中ボス（処刑騎士・獣王）＝1マス近づく。
        /// false にすると RC3（剣兵・槍兵も毎ターン1マス近づく）。解析の比べ（pzsearch の PZ_CADENCE=0）だけ。
        /// </summary>
        public static bool KindCadence = true;

        /// <summary>
        /// 【RC4・検証】剣兵が自分で1マス近づく条件（0＝近づかない・1＝2ターンに1回・2＝3マス以上離れたら・3＝盤面の端に追い込まれたら・4＝2か3）。
        /// まねだけで動く剣兵は、あなたが近づくと同じ向きに逃げて端に張り付いた（素直に追う人の「端」6% → 47%）→ 比べて決める（pzsearch の PZ_SWORDMODE）。
        /// </summary>
        public static int SwordsmanMode = 4;

        /// <summary>
        /// 【RC5・検証】槍兵が自分で1マス詰める条件（0＝動かない（RC4）・N＝あなたとの距離が N マス以上なら1マス）。
        /// RC4 で槍兵を「動かない」にしたら、最後に残った槍兵が盤面の端に構えたままになり、終わりの片付けが長くなった（槍の回廊・癒し手の陣・盾と槍の挟撃）。
        /// </summary>
        public static int SpearMode = 3; // 【RC5】測って決めた：3（槍の回廊・癒し手の陣・盾と槍の挟撃の片付けが 2〜3 ターン短くなった。剣兵と同じ「3マス以上離れると詰める」で1つの動き方として覚えられる。docs/final/RC5_CHANGES.md）

        static bool SpearSteps(BattleModel model, UnitModel e)
        {
            var p = model.Player;
            if (p == null || SpearMode <= 0) return false;
            return System.Math.Abs(p.Position.x - e.Position.x) + System.Math.Abs(p.Position.y - e.Position.y) >= SpearMode;
        }

        static bool OnEdge(BattleModel model, Vector2Int c) => c.x == 0 || c.y == 0 || c.x == model.Board.Width - 1 || c.y == model.Board.Height - 1;

        static bool SwordsmanSteps(BattleModel model, UnitModel e)
        {
            var p = model.Player;
            if (p == null) return false;
            int dist = System.Math.Abs(p.Position.x - e.Position.x) + System.Math.Abs(p.Position.y - e.Position.y);
            switch (SwordsmanMode)
            {
                case 1: return model.Environment.Round % 2 == 0;
                case 2: return dist >= 3;
                case 3: return OnEdge(model, e.Position) && dist >= 2;
                case 4: return dist >= 3 || (OnEdge(model, e.Position) && dist >= 2);
                default: return false;
            }
        }

        /// <summary>【RC4】兵種の自分の動き（1行・画面の説明とはじめて見たときの紹介に使う）。</summary>
        /// <summary>【RC5】画面の呼び名（名札・原因の一言・まねる敵の一覧で同じ）：兵種名＋同じ兵種が2体以上なら A・B…。</summary>
        public static string ShortName(UnitModel u) =>
            u == null ? "敵" : (u.Kind == EnemyKind.HealClone ? "写し身" : u.Kind == EnemyKind.Normal ? "兵" : KindName(u.Kind)) + (u.KindLetter ?? "");

        public static string MoveStyle(EnemyKind k)
        {
            switch (k)
            {
                case EnemyKind.Swordsman: return "ふだんは まねで動く。3マス以上離れると1マス詰める（盤面のふちに追い込まれたときも）";
                case EnemyKind.Spearman: return "ふだんは動かず、あなたの方を向いて構える（向いた方の2マスが次の赤）。3マス以上離れると1マス詰める";
                case EnemyKind.Archer: return "毎ターンの最後に、あなたの行・列へ1マス動いて狙う（並んでいれば動かない）";
                case EnemyKind.Chaser: return "毎ターンの最後に、あなたへ2マスまで近づく（追ってくる）";
                case EnemyKind.Bomber: return "動かない。毎ターンの最後に、あなたのいるマスへ照準（次のターンに爆撃）";
                case EnemyKind.Shield: return "動かない。毎ターンの最後に、あなたの方へ盾を向ける";
                case EnemyKind.Mender: return "動かない。毎ターンの最後に、いちばん傷ついた仲間を回復";
                case EnemyKind.ExecKnight: return "毎ターンの最後に、あなたへ1マス近づく（中ボス）";
                case EnemyKind.BeastKing: return "毎ターンの最後に、あなたへ1マス近づく（中ボス）";
                case EnemyKind.CannonKing: return "動かない（中ボス）";
                case EnemyKind.MirrorLord: return "自分では動かない（あなたの命令をまねて動く）";
                default: return "自分では動かない";
            }
        }

        /// <summary>【RC4】この兵種は自分で動くことがあるか（動く先の予告を出す兵種）。</summary>
        public static bool MovesOnItsOwn(UnitModel e) =>
            e.Kind == EnemyKind.Chaser || e.Kind == EnemyKind.Archer || e.Kind == EnemyKind.ExecKnight || e.Kind == EnemyKind.BeastKing ||
            (e.Kind == EnemyKind.Swordsman && (!KindCadence || SwordsmanMode != 0)) || (e.Kind == EnemyKind.Spearman && (!KindCadence || SpearMode > 0));

        /// <summary>【RC3】戦闘の始め：槍兵・弓兵・盾兵はあなたの方を向く（最初のターンの赤い予告が「あなたを狙っている」形になる）。</summary>
        public static void InitialIntent(BattleModel model)
        {
            if (!IntentOrder || model.Player == null) return;
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive) continue;
                if (e.Kind == EnemyKind.Spearman || e.Kind == EnemyKind.Shield) FaceToward(e, model.Player.Position);
                else if (e.Kind == EnemyKind.Archer) { var aim = AimDir(model, e); if (aim.HasValue) e.Facing = aim.Value; else FaceToward(e, model.Player.Position); }
                if (e.Kind == EnemyKind.MirrorLord && CombatBoss.FirstTurnPillar && CombatBoss.PillarEveryTurn && e.BombCells.Count == 0 && model.V4) e.BombCells = CombatBoss.PillarShape(model, e, model.Player.Position); // 【RC6】1ターン目から光柱
            }
        }

        /// <summary>【RC3】行・列の差が大きい方で、あなたの方を向く（同じなら横）。</summary>
        static void FaceToward(UnitModel e, Vector2Int target)
        {
            var d = target - e.Position;
            if (d == Vector2Int.zero) return;
            e.Facing = System.Math.Abs(d.x) >= System.Math.Abs(d.y) ? (d.x > 0 ? Direction.Right : Direction.Left) : (d.y > 0 ? Direction.Up : Direction.Down);
        }

        /// <summary>【RC3】砲兵の爆発だけ（照準は位置取りで）。</summary>
        static string BombExplode(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            if (e.BombCells.Count == 0) return null;
            string hit = "";
            foreach (var c in e.BombCells)
            {
                events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Fire });
                var t = model.GetUnitAt(c);
                if (t == null || !t.IsAlive) continue;
                int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, Bomb(model), DamageKind.Trait, events);
                hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
            }
            e.BombCells = new List<Vector2Int>();
            return $"{e.Name}（{KindName(e.Kind)}）：爆発 → " + (hit.Length > 0 ? hit : "誰もいない");
        }

        static string V4Action(BattleModel model, UnitModel e, List<BattleEvent> events, bool firing)
        {
            switch (e.Kind)
            {
                case EnemyKind.Swordsman:
                case EnemyKind.ExecKnight:
                {
                    string t = Strike(model, e, SwordCells(model, e), e.Kind == EnemyKind.ExecKnight ? V4Melee + 1 : Melee(model), "剣：周囲4マス", events);
                    return e.IsAlive && !firing ? t + "　" + Approach(model, e, events, true) : t;
                }
                case EnemyKind.Spearman:
                {
                    // 【RC2】あなたが同じ行・列の2マス以内にいれば、突く前にあなたの方を向く（前は、まねで向いた方へしか突かず、敵の方へ斬るだけで槍兵が背を向けた：レビュー）。
                    //        大技（貫通突撃）の向きは今までどおり、まねで決まる（予告はまねの後）
                    var spearAim = SpearAim(model, e);
                    if (spearAim.HasValue) e.Facing = spearAim.Value;
                    string t = Strike(model, e, SpearCells(model, e), Melee(model), "槍：正面2マス", events);
                    return e.IsAlive && !firing ? t + "　" + Approach(model, e, events, true) : t;
                }
                case EnemyKind.Archer:
                {
                    var p = model.Player;
                    // 自動で狙う：あなたと同じ行・列（射程内・間に壁なし）にいるなら、あなたの方向へ撃つ
                    var aim = AimDir(model, e);
                    if (aim.HasValue) e.Facing = aim.Value;
                    string t = Shoot(model, e, events);
                    if (e.IsAlive && !firing && p != null && p.IsAlive) { string a = AlignStep(model, e, events); if (a != null) t += "　" + a; }
                    return t;
                }
                case EnemyKind.Chaser:
                {
                    string t = firing ? $"{e.Name}（{KindName(e.Kind)}）：構える" : Step(model, e, events);
                    var p = model.Player;
                    if (e.IsAlive && p != null && p.IsAlive && System.Math.Abs(p.Position.x - e.Position.x) + System.Math.Abs(p.Position.y - e.Position.y) == 1)
                    {
                        events.Add(new BattleEvent { Type = BattleEventType.Lunge, Unit = e, Direction = p.Position - e.Position });
                        events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = p.Position, Highlight = CellHighlight.Arrow });
                        int dealt = RulesEngine.ApplyDamage(model, e, null, false, p, Melee(model), DamageKind.Trait, events);
                        t += $"　噛みつき → {p.Name}に {dealt}";
                    }
                    return t;
                }
                case EnemyKind.BeastKing: return firing ? null : Approach(model, e, events, true);
                case EnemyKind.Bomber: return BombV4(model, e, events);
                case EnemyKind.Shield: return ShieldFace(model, e);
                case EnemyKind.CannonKing:
                case EnemyKind.Normal: return null;
            }
            return TraitActionBase(model, e, events);
        }

        /// <summary>あなたへ1マス近づく（追跡兵と同じ規則：壁・ユニット・トゲ・燃えるマスには入らない。すでに隣なら動かない）。</summary>
        static string Approach(BattleModel model, UnitModel e, List<BattleEvent> events, bool toward)
        {
            var target = StepTargetToward(model, e, toward);
            if (!target.HasValue) return "動かない";
            var d = target.Value - e.Position;
            string dir = d.x > 0 ? "→右" : d.x < 0 ? "←左" : d.y > 0 ? "↑上" : "↓下";
            RulesEngine.EnterCell(model, e, target.Value, e, null, false, events);
            return $"{dir}へ1マス近づく";
        }

        /// <summary>【RC2】槍兵が突く前に向く向き（あなたが同じ行・列の2マス以内・間に壁が無い）。無ければ null。</summary>
        static Direction? SpearAim(BattleModel model, UnitModel spear)
        {
            var p = model.Player;
            if (p == null || !p.IsAlive) return null;
            var d = p.Position - spear.Position;
            if ((d.x != 0 && d.y != 0) || (d.x == 0 && d.y == 0)) return null;
            int dist = System.Math.Abs(d.x) + System.Math.Abs(d.y);
            if (dist > 2) return null;
            var step = new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y));
            if (dist == 2 && model.Board.IsWall(spear.Position + step)) return null;
            return step.x > 0 ? Direction.Right : step.x < 0 ? Direction.Left : step.y > 0 ? Direction.Up : Direction.Down;
        }

        /// <summary>弓兵が自動で狙える向き（あなたと同じ行・列で、射程内・間に壁が無い）。</summary>
        public static Direction? AimDir(BattleModel model, UnitModel archer)
        {
            var p = model.Player;
            if (p == null || !p.IsAlive) return null;
            var d = p.Position - archer.Position;
            if ((d.x != 0 && d.y != 0) || (d.x == 0 && d.y == 0)) return null;
            int dist = System.Math.Abs(d.x) + System.Math.Abs(d.y);
            if (dist > ArrowRange) return null;
            var step = new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y));
            var c = archer.Position;
            for (int i = 1; i < dist; i++) { c += step; if (model.Board.IsWall(c)) return null; }
            return step.x > 0 ? Direction.Right : step.x < 0 ? Direction.Left : step.y > 0 ? Direction.Up : Direction.Down;
        }

        /// <summary>弓兵：あなたと同じ行・列になるよう、ずれの小さい方へ1マス動く（次のターンに狙える位置を作る）。</summary>
        static string AlignStep(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            var p = model.Player;
            var d = p.Position - e.Position;
            if (d.x == 0 || d.y == 0) return null;
            var opts = System.Math.Abs(d.x) <= System.Math.Abs(d.y)
                ? new[] { new Vector2Int(System.Math.Sign(d.x), 0), new Vector2Int(0, System.Math.Sign(d.y)) }
                : new[] { new Vector2Int(0, System.Math.Sign(d.y)), new Vector2Int(System.Math.Sign(d.x), 0) };
            foreach (var s in opts)
            {
                var cell = e.Position + s;
                if (model.IsBlocked(cell) || IsAvoided(model, cell, e)) continue;
                RulesEngine.EnterCell(model, e, cell, e, null, false, events);
                return "位置取り（あなたの行・列へ）";
            }
            return null;
        }

        /// <summary>
        /// 【RC2・2026-10-20】盾兵：毎ターン（敵の行動で）あなたの方へ盾を向ける。正面からの直接攻撃と矢は -3。
        /// 理由：前は「まねで動いた向き」が正面になり、近づくだけで背中を見せていた（硬い盾が成立しなかった）。
        /// 向きを変えるのは敵の行動のときだけ：あなたが同じターンの命令で横へ回り込めば、横からは通る（遅いが安全な別解）。
        /// </summary>
        static string ShieldFace(BattleModel model, UnitModel e)
        {
            var p = model.Player;
            if (p == null || !p.IsAlive || !e.IsAlive) return null;
            var d = p.Position - e.Position;
            if (d == Vector2Int.zero) return null;
            var f = System.Math.Abs(d.x) >= System.Math.Abs(d.y) ? (d.x > 0 ? Direction.Right : Direction.Left) : (d.y > 0 ? Direction.Up : Direction.Down); // 【RC3】同じ距離なら横（はじめの向き・槍兵と同じ）
            if (f == e.Facing) return null;
            e.Facing = f;
            return $"{e.Name}（{KindName(e.Kind)}）：あなたの方へ盾を向ける";
        }

        static string BombV4(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = $"{e.Name}（{KindName(e.Kind)}）：";
            if (e.BombCells.Count > 0)
            {
                string hit = "";
                foreach (var c in e.BombCells)
                {
                    events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Fire });
                    var t = model.GetUnitAt(c);
                    if (t == null || !t.IsAlive) continue;
                    int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, Bomb(model), DamageKind.Trait, events);
                    hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
                }
                text += "爆発 → " + (hit.Length > 0 ? hit : "誰もいない") + "　";
                e.BombCells = new List<Vector2Int>();
            }
            if (model.Player != null && model.Player.IsAlive && e.IsAlive)
            {
                e.BombCells = BombShape(model, model.Player.Position);
                text += $"次の爆発を予告（{model.Player.Position} の十字）";
            }
            return text;
        }

        static string TraitActionBase(BattleModel model, UnitModel enemy, List<BattleEvent> events)
        {
            switch (enemy.Kind)
            {
                case EnemyKind.Swordsman: return Strike(model, enemy, SwordCells(model, enemy), SwordDamage + model.TraitBonus, "剣：周囲4マス", events);
                case EnemyKind.Spearman: return Strike(model, enemy, SpearCells(model, enemy), SpearDamage + model.TraitBonus, "槍：正面2マス", events);
                case EnemyKind.Bomber: return Bomb(model, enemy, events);
                case EnemyKind.KnightV3: return Countdowns(model, enemy, events);
                case EnemyKind.Warden: return WardenSlam(model, enemy, events);
                case EnemyKind.SealLord: return PuzzleBoss.StoneHand(model, enemy, events); // 【Puzzle v2】巨大石手
                case EnemyKind.Lord:
                {
                    // 大剣（3ラウンドごと・周り8マス）＋光柱（あなたの位置の十字を予告 → 次のラウンドに3 → 1ラウンド休む）
                    string slam = LordSlam(model, enemy, events);
                    string pillar = LordPillar(model, enemy, events);
                    return slam != null && pillar != null ? slam + "　" + pillar : slam ?? pillar;
                }
                case EnemyKind.HealClone: return CloneHealAction(model, enemy, events);
                case EnemyKind.Mender: return MenderAction(model, enemy, events);           // 【戦闘中心 v3】癒し手
                case EnemyKind.MirrorLord: return CombatBoss.Trait(model, enemy, events);   // 【戦闘中心 v3】鏡の主
                case EnemyKind.KnightV2:
                    string text = Strike(model, enemy, SwordCells(model, enemy), SwordDamage, "剣：周囲4マス", events);
                    if (enemy.ShootsArrows && model.Player.IsAlive && enemy.IsAlive) text += "　" + Shoot(model, enemy, events);
                    return text;
            }
            if (enemy.ShootsArrows) return Shoot(model, enemy, events);
            if (enemy.Kind == EnemyKind.Chaser || enemy.Kind == EnemyKind.Fleer) return Step(model, enemy, events);
            return null;
        }

        /// <summary>矢の通るマス（向いている方向へ最大 ArrowRange。壁の手前まで。最初のユニットのマスで止まる）。</summary>
        public static List<Vector2Int> ArrowCells(BattleModel model, UnitModel archer)
        {
            var cells = new List<Vector2Int>();
            var d = archer.Facing.ToOffset();
            var cell = archer.Position;
            for (int i = 0; i < ArrowRange; i++)
            {
                cell += d;
                if (model.Board.IsWall(cell)) break;
                cells.Add(cell);
                if (model.GetUnitAt(cell) != null) break;
            }
            return cells;
        }

        /// <summary>
        /// 【RC3・レビュー】予告用の矢の線：あなたのマスでは止めない（壁と、あなた以外のユニットで止まる・射程まで）。
        /// 前は予告もあなたのマスで止めていた → 線の上を後ろへ下がると、赤ではないマスで矢が当たった（撃つときは今の位置で最初に当たった1体）。
        /// </summary>
        public static List<Vector2Int> ArrowLane(BattleModel model, UnitModel archer)
        {
            var cells = new List<Vector2Int>();
            var d = archer.Facing.ToOffset();
            var cell = archer.Position;
            for (int i = 0; i < ArrowRange; i++)
            {
                cell += d;
                if (model.Board.IsWall(cell)) break;
                cells.Add(cell);
                var u = model.GetUnitAt(cell);
                if (u != null && !u.IsPlayer) break;
            }
            return cells;
        }

        /// <summary>【RC3】敵の通常の攻撃（赤）の1回のダメージ（盤面の数字の合計に使う）。0＝通常の攻撃なし。</summary>
        public static int AttackDamageOf(BattleModel model, UnitModel e)
        {
            switch (e.Kind)
            {
                case EnemyKind.Swordsman:
                case EnemyKind.Chaser:
                case EnemyKind.Spearman: return Melee(model);
                case EnemyKind.ExecKnight: return V4Melee + 1;
                case EnemyKind.Archer: return Arrow(model) + model.TraitBonus;
                default: return 0;
            }
        }

        /// <summary>
        /// 【検証中】剣兵：自分の上下左右（隣の4マス）。壁は除く。
        /// （2026-10-04 解析：向きで決まる前方3マスにすると、正面から斬ったプレイヤーには絶対に当たらない＝雑な殴り合いが無傷になったため、隣4マスに変更）
        /// </summary>
        public static List<Vector2Int> SwordCells(BattleModel model, UnitModel u) =>
            new[] { u.Position + Vector2Int.up, u.Position + Vector2Int.down, u.Position + Vector2Int.left, u.Position + Vector2Int.right }
                .Where(c => !model.Board.IsWall(c)).ToList();

        /// <summary>【検証中】槍兵：向いている方向へ2マス（壁で止まる）。</summary>
        public static List<Vector2Int> SpearCells(BattleModel model, UnitModel u)
        {
            var cells = new List<Vector2Int>();
            var c = u.Position;
            for (int i = 0; i < 2; i++)
            {
                c += u.Facing.ToOffset();
                if (model.Board.IsWall(c)) break;
                cells.Add(c);
            }
            return cells;
        }

        /// <summary>【検証中】砲兵の爆発の形：中心と上下左右（十字5マス）。盤面の外・壁は除く。</summary>
        /// <summary>【研究】大十字：上下左右2マス（9マス）。</summary>
        static List<Vector2Int> BigCross(BattleModel model, Vector2Int center)
        {
            var cells = new List<Vector2Int> { center };
            // 【検証中】2026-10-05：大十字の2マス目は壁で止まる（壁の向こうへは広がらない）
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                for (int k = 1; k <= 2; k++) { if (model.Board.IsWall(center + d * k)) break; cells.Add(center + d * k); }
            return cells.Where(c => model.Board.IsInside(c) && !model.Board.IsWall(c)).ToList();
        }

        public static List<Vector2Int> BombShape(BattleModel model, Vector2Int center) =>
            new[] { center, center + Vector2Int.up, center + Vector2Int.down, center + Vector2Int.left, center + Vector2Int.right }
                .Where(c => !model.Board.IsWall(c)).ToList();

        /// <summary>【予告用】この兵種の行動が今のままで当たるマス（矢・剣・槍・予告済みの爆発）。</summary>
        public static List<Vector2Int> TraitCells(BattleModel model, UnitModel u)
        {
            var cells = new List<Vector2Int>();
            if (model.NoTraits) return cells; // 【RC3】はじめての戦闘：敵は攻撃しない（赤も出ない）
            switch (u.Kind)
            {
                case EnemyKind.Swordsman: cells.AddRange(SwordCells(model, u)); break;
                case EnemyKind.ExecKnight:
                case EnemyKind.Chaser:
                    if (model.V4 && IntentOrder) cells.AddRange(SwordCells(model, u)); // 【RC3】処刑騎士の剣・追跡兵の噛みつきも赤で（前は出ていなかった）
                    break;
                case EnemyKind.Spearman:
                    if (model.V4 && IntentOrder) cells.AddRange(SpearCells(model, u)); // 【RC3】向いている方へ2マスだけ（狙い直さない）
                    else if (model.V4)
                    {
                        // 【RC2】V4：あなたが同じ行・列の2マス以内なら、そちらを向いて突く → 上下左右2マスが危ない（壁で止まる）
                        foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                            for (int k = 1; k <= 2; k++) { var c = u.Position + d * k; if (model.Board.IsWall(c) || !model.Board.IsInside(c)) break; cells.Add(c); }
                    }
                    else cells.AddRange(SpearCells(model, u));
                    break;
                case EnemyKind.Bomber: cells.AddRange(u.BombCells); break;
                case EnemyKind.KnightV2: cells.AddRange(SwordCells(model, u)); break;
                case EnemyKind.KnightV3:
                    // 次の兵種フェーズで発動するもの（カウントが 1）だけ
                    if (u.SlamCd == 1) cells.AddRange(u.SlamCells);
                    if (u.BlastCd == 1) cells.AddRange(u.BombCells);
                    if (u.WaveCd == 1) cells.AddRange(WaveCells(model, u.WaveCount));
                    break;
            }
            if (u.ShootsArrows)
            {
                // 【Combat V4】あなたと同じ行・列なら、あなたの方向へ撃つ（予告もその向きで）
                var aim = model.V4 && !IntentOrder ? AimDir(model, u) : null; // 【RC3】狙い直さない：今の向きのまま
                if (aim.HasValue) { var keep = u.Facing; u.Facing = aim.Value; cells.AddRange(ArrowCells(model, u)); u.Facing = keep; }
                else cells.AddRange(model.V4 && IntentOrder ? ArrowLane(model, u) : ArrowCells(model, u)); // 【RC3】あなたのマスで止めない
            }
            return cells;
        }

        /// <summary>【検証中】騎士V3 の炎の波：回数が偶数なら中央3×3、奇数なら外周1マス（壁は除く）。</summary>
        public static List<Vector2Int> WaveCells(BattleModel model, int count)
        {
            var b = model.Board;
            var cells = new List<Vector2Int>();
            int cx = (b.Width - 1) / 2, cy = (b.Height - 1) / 2;
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (b.IsWall(c)) continue;
                    bool inner = Mathf.Abs(x - cx) <= 1 && Mathf.Abs(y - cy) <= 1;
                    bool outer = x == 0 || y == 0 || x == b.Width - 1 || y == b.Height - 1;
                    if (count % 2 == 0 ? inner : outer) cells.Add(c);
                }
            return cells;
        }

        /// <summary>
        /// 【検証中】騎士V3 のカウントダウン攻撃。毎ラウンド：段階の開始（HP 60% 以下で爆発、30% 以下で炎の波）→ 各カウントを1減らし、
        /// 0 なら予告済みのマスへ発動して周期に戻す。1 になったら次に当たるマスを決めて予告（大剣＝騎士の周り8マス、爆発＝あなたの位置の十字、炎の波＝決まった範囲）。
        /// </summary>
        static string Countdowns(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = $"{e.Name}（{KindName(e.Kind)}）：";
            var v = BossV3; // 今の仕様（解析中は数値案）
            int ph = BossPhase(e);
            if (v == null || !v.AllFromStart)
            {
                if (e.BlastCd == 0 && e.Hp * 10 <= e.MaxHp * 6) { e.BlastCd = (v != null ? v.BlastPeriod[ph] : BlastPeriod) + 1; text += "【第2段階】爆発のカウントダウン開始　"; }
                if (e.WaveCd == 0 && e.Hp * 10 <= e.MaxHp * 3) { e.WaveCd = (v != null ? v.WavePeriod[ph] : WavePeriod) + 1; text += "【第3段階】炎の波のカウントダウン開始　"; }
            }

            if (e.SlamCd > 0 && --e.SlamCd == 0)
            {
                text += Hit(model, e, e.SlamCells, v != null ? v.SlamDamage[ph] : SlamDamage, "大剣", CellHighlight.Arrow, events);
                e.SlamCells = new List<Vector2Int>();
                e.SlamCd = v != null ? v.SlamPeriod[ph] : SlamPeriod;
            }
            if (e.BlastCd > 0 && --e.BlastCd == 0)
            {
                text += Hit(model, e, e.BombCells, v != null ? v.BlastDamage[ph] : BlastDamage, "爆発", CellHighlight.Fire, events);
                e.BombCells = new List<Vector2Int>();
                e.BlastCd = v != null ? v.BlastPeriod[ph] : BlastPeriod;
            }
            if (e.WaveCd > 0 && --e.WaveCd == 0)
            {
                text += Hit(model, e, WaveCells(model, e.WaveCount), v != null ? v.WaveDamage[ph] : WaveDamage, e.WaveCount % 2 == 0 ? "炎の波（中央）" : "炎の波（外周）", CellHighlight.Fire, events);
                e.WaveCount++;
                e.WaveCd = v != null ? v.WavePeriod[ph] : WavePeriod;
            }
            if (!e.IsAlive || model.Player == null || !model.Player.IsAlive) return text;
            // カウントが 1 になったものは、次のラウンドに当たるマスをここで決める（予告）
            if (e.SlamCd == 1)
            {
                e.SlamCells = new List<Vector2Int>();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var c = e.Position + new Vector2Int(dx, dy);
                        if ((dx != 0 || dy != 0) && !model.Board.IsWall(c)) e.SlamCells.Add(c);
                    }
                text += "大剣を予告　";
            }
            if (e.BlastCd == 1)
            {
                e.BombCells = v != null && v.BigBlast[BossPhase(e)] ? BigCross(model, model.Player.Position) : BombShape(model, model.Player.Position);
                text += "爆発を予告　";
            }
            if (e.WaveCd == 1) text += (e.WaveCount % 2 == 0 ? "炎の波（中央）" : "炎の波（外周）") + "を予告　";
            return text;
        }

        static string Hit(BattleModel model, UnitModel e, List<Vector2Int> cells, int damage, string what, CellHighlight hl, List<BattleEvent> events)
        {
            string hit = "";
            foreach (var c in cells)
            {
                events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = hl });
                var t = model.GetUnitAt(c);
                if (t == null || !t.IsAlive || t == e) continue;
                int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, damage, DamageKind.Trait, events);
                hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
            }
            return $"{what} → {(hit.Length > 0 ? hit : "誰もいない")}　";
        }

        /// <summary>剣・槍：マスにいる全員（敵も）にダメージ。</summary>
        static string Strike(BattleModel model, UnitModel e, List<Vector2Int> cells, int damage, string what, List<BattleEvent> events)
        {
            string head = $"{e.Name}（{KindName(e.Kind)}）：{what} {e.Facing.ToLabel()} → ";
            string hit = "";
            foreach (var c in cells)
            {
                events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Arrow });
                var t = model.GetUnitAt(c);
                if (t == null || !t.IsAlive || t == e) continue;
                int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, damage, DamageKind.Trait, events);
                hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}（残り HP {t.Hp}）";
            }
            return head + (hit.Length > 0 ? hit : "誰もいない（空振り）");
        }

        /// <summary>砲兵：前のラウンドに予告したマスが爆発（全員に BombDamage）→ あなたのいる場所を中心に次の爆発を予告。</summary>
        static string Bomb(BattleModel model, UnitModel e, List<BattleEvent> events)
        {
            string text = $"{e.Name}（{KindName(e.Kind)}）：";
            if (e.BombCells.Count > 0)
            {
                string hit = "";
                foreach (var c in e.BombCells)
                {
                    events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = e, Cell = c, Highlight = CellHighlight.Fire });
                    var t = model.GetUnitAt(c);
                    if (t == null || !t.IsAlive || (t == e && e.Kind == EnemyKind.Lord)) continue; // 写し身の主の光柱は主自身には当たらない
                    int dealt = RulesEngine.ApplyDamage(model, e, null, false, t, BombDamage + model.TraitBonus, DamageKind.Trait, events);
                    hit += $"{(hit.Length > 0 ? "・" : "")}{t.Name}に {dealt}";
                }
                text += "爆発 → " + (hit.Length > 0 ? hit : "誰もいない") + "　";
                e.BombCells = new List<Vector2Int>();
            }
            if (model.Player != null && model.Player.IsAlive && e.IsAlive)
            {
                e.BombCells = BombShape(model, model.Player.Position);
                text += $"次の爆発を予告（{model.Player.Position} の十字）";
            }
            return text;
        }

        static string Shoot(BattleModel model, UnitModel archer, List<BattleEvent> events)
        {
            var cells = ArrowCells(model, archer);
            if (model.V4 && IntentOrder && archer.ArrowReach >= 0 && cells.Count > archer.ArrowReach) cells = cells.Take(archer.ArrowReach).ToList(); // 【RC5・監査】ターンの初めの赤の長さまで
            foreach (var c in cells) events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Unit = archer, Cell = c, Highlight = CellHighlight.Arrow });
            string head = $"{archer.Name}（{KindName(archer.Kind)}）：矢 {archer.Facing.ToLabel()} → ";
            var target = cells.Count > 0 ? model.GetUnitAt(cells[cells.Count - 1]) : null;
            if (target == null) return head + "誰もいない（空振り）";
            int arrow = (model.V4 ? Arrow(model) : ArrowDamage) + model.TraitBonus;
            int dealt = RulesEngine.ApplyDamage(model, archer, null, false, target, arrow, DamageKind.Trait, events);
            string shield = dealt < arrow ? $"（盾で -{arrow - dealt}）" : "";
            return head + $"{target.Name}に {dealt} ダメージ{shield}（残り HP {target.Hp}）";
        }

        /// <summary>
        /// 追跡兵・逃げ兵の移動先（動かないなら null）。規則（ランダムなし）：
        /// - 追跡兵はプレイヤーへ、逃げ兵はプレイヤーと反対へ1マス。
        /// - 縦と横のうち、プレイヤーとの距離が大きい方を先に試す。同じなら横を先に。
        /// - 壁・ユニット・トゲ・このラウンドに燃えるマス・回復ルーンには自分から入らない（その方向がだめなら、もう一方を試す）。
        /// - 追跡兵は、すでにプレイヤーの隣（上下左右）なら動かない。
        /// </summary>
        public static Vector2Int? StepTarget(BattleModel model, UnitModel unit) => StepTargetToward(model, unit, unit.Kind == EnemyKind.Chaser);

        public static Vector2Int? StepTargetToward(BattleModel model, UnitModel unit, bool toward)
        {
            var player = model.Player;
            if (player == null || !player.IsAlive) return null;
            var d = player.Position - unit.Position;
            if (toward && Mathf.Abs(d.x) + Mathf.Abs(d.y) <= 1) return null;
            var horizontal = new Vector2Int(System.Math.Sign(d.x), 0);
            var vertical = new Vector2Int(0, System.Math.Sign(d.y));
            if (!toward) { horizontal = -horizontal; vertical = -vertical; }
            var order = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new[] { horizontal, vertical } : new[] { vertical, horizontal };
            foreach (var step in order)
            {
                if (step == Vector2Int.zero) continue;
                var cell = unit.Position + step;
                if (model.IsBlocked(cell) || IsAvoided(model, cell, unit)) continue;
                return cell;
            }
            return null;
        }

        /// <summary>兵種の移動が自分からは入らないマス（トゲ・このラウンドに燃えるマス・回復ルーン）。</summary>
        static bool IsAvoided(BattleModel model, Vector2Int cell, UnitModel unit = null)
        {
            var env = model.Environment;
            if (model.Board.HasSpike(cell) || env.FireCellsNow.Contains(cell) || env.RuneCell == cell) return true;
            // 【RC3・レビュー】ワープ陣にも、自分の歩き（近づく・位置取り）では入らない。ワープで敵を飛ばすのは「まね」の仕事
            //   （前は追跡兵が近づく途中で勝手にワープへ入り、群れが勝手に分かれた。赤い予告にも出ない動きだった）
            if (model.V4 && model.Board.TryWarp(cell, out _)) return true;
            // 【RC2・検証】あなたの移動をまねる敵（全まね・移動まね）は、自分の歩きでは①の大技のマス（赤紫）に入らない → 大技へ送り込むのは「まね」の仕事
            return MovesAvoidTelegraph && model.V4 && unit != null && (unit.CopyKind == CopyKind.All || unit.CopyKind == CopyKind.Move) && (ThreatMap.Telegraph(model).Contains(cell) || ThreatMap.BossAttack(model).Contains(cell)); // 【RC5】光柱を赤に移しても、避け方は前と同じ
        }

        /// <summary>【RC2・検証】移動をまねる敵は①の大技のマスを自分からは避ける（pzsearch の PZ_AVOIDTEL で比べる）。</summary>
        public static bool MovesAvoidTelegraph = false;

        static string Step(BattleModel model, UnitModel unit, List<BattleEvent> events)
        {
            string head = $"{unit.Name}（{KindName(unit.Kind)}）：";
            var target = StepTarget(model, unit);
            if (!target.HasValue) return head + "動かない";
            var d = target.Value - unit.Position;
            string dir = d.x > 0 ? "→右" : d.x < 0 ? "←左" : d.y > 0 ? "↑上" : "↓下";
            RulesEngine.EnterCell(model, unit, target.Value, unit, null, false, events);
            return head + $"{dir}へ1マス";
        }

        /// <summary>
        /// ④ ENVIRONMENT PHASE：このラウンドの炎床が燃え（上にいる全員に FireDamage。盾は効かない）、ラウンドを進める。
        /// 騎士の HP が半分以下なら、次のラウンドから炎が燃え始める（予告してから）。
        /// </summary>
        public static TurnOutcome EnvironmentPhase(BattleModel model, List<BattleEvent> events, List<string> log)
        {
            events = events ?? new List<BattleEvent>();
            var env = model.Environment;
            // 【Puzzle v2】兵種の行動で最後の敵が倒れたときも、封印が弱まったことを知らせる
            var weak = PuzzleRules.CheckWeak(model, events);
            if (weak.Length > 0) log?.Add(weak.TrimStart('／'));
            // 【Puzzle v2】石像の視線（炎の前。予告は紫でいつも見える）
            if (model.Board.StatueOrder.Count > 0)
            {
                PuzzleRules.StatuePhase(model, events, log);
                RulesEngine.CheckPlates(model, events);
                PuzzleRules.CheckMarks(model, events);
                PuzzleRules.CheckWeak(model, events);
                var o = TurnRules.Check(model);
                if (o != TurnOutcome.Continue) { env.Round++; return o; }
            }
            var fire = env.FireCellsNow.ToList();
            if (fire.Count > 0)
            {
                foreach (var c in fire) events.Add(new BattleEvent { Type = BattleEventType.TargetCell, Cell = c, Highlight = CellHighlight.Fire });
                var burned = model.Units.Where(u => u.IsAlive && fire.Contains(u.Position)).ToList();
                if (burned.Count == 0) log?.Add("炎床：燃えたマスには誰もいない");
                foreach (var u in burned)
                {
                    // 環境のダメージは「原因＝本人」（誰かの攻撃ではない。同士討ちには数えない）
                    RulesEngine.ApplyDamage(model, u, null, false, u, env.FireDamage, DamageKind.Fire, events);
                    log?.Add($"炎床：{u.Name}に {env.FireDamage} ダメージ（残り HP {u.Hp}）");
                }
            }
            if (env.FireAfterKnightHalf && !env.FireArmed && model.Enemies.Any(e => e.Kind == EnemyKind.Knight && e.IsAlive && e.Hp * 2 <= e.MaxHp))
            {
                env.FireArmed = true;
                log?.Add("鏡守の騎士の HP が半分を切った：盾を失い、矢を放つようになった。次のラウンドから炎床が燃え始める");
            }
            // 【検証中】騎士V2：HP 40% 以下で、次のラウンドから炎床（第3段階）
            if (env.FireAfterKnightHalf && !env.FireArmed && model.Enemies.Any(e => e.Kind == EnemyKind.KnightV2 && e.IsAlive && e.Hp * 10 <= e.MaxHp * 4))
            {
                env.FireArmed = true;
                log?.Add("鏡守の騎士の HP が 40% を切った：次のラウンドから炎床が燃え始める（第3段階）");
            }
            WardenRound(model, events, log);
            LordRound(model, events, log);
            PuzzleBoss.RoundEnd(model, events, log); // 【Puzzle v2】封紋の守護者：剣の像が光る・窓が閉じる
            // 【戦闘中心 v3】共鳴で動けないラウンドを減らす・割れた鏡核は数ターンで戻る
            foreach (var u in model.Enemies) if (u.StunRounds > 0) u.StunRounds--;
            // 【Combat V4】このラウンドだけの戦術札の効果を戻す（沈黙・模倣命令・操糸／重し）
            foreach (var u in model.Enemies) { u.Silenced = false; u.ForceAllCopy = false; }
            model.CopyMoveBonus = 0;
            if (model.Board.CoreRegrowIn > 0 && --model.Board.CoreRegrowIn == 0 && !model.Board.Cores.Any())
            {
                RegrowCores(model, events);
                log?.Add("鏡核が戻った（もう一度、同じターンに叩くと共鳴する）");
            }
            env.Round++;
            return TurnRules.Check(model);
        }
    }
}
