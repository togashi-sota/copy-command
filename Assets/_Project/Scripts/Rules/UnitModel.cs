using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    public enum Team
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// 敵のコピー特性（コピーした命令列をどう使うか）。今のステージで使うのは Normal だけ。
    /// </summary>
    public enum CopyTrait
    {
        Normal,   // 通常コピー：同じ順番・同じ方向
        Reverse,  // 逆順コピー：順番を逆にする（将来候補）
        Mirror,   // 方向反転コピー：方向を上下・左右逆にする（将来候補）
        MirrorAttacks, // 【検証中】攻撃だけ反転：移動はそのまま、攻撃・押す・引く命令の方向だけ逆（鏡が「攻撃を跳ね返す」）
    }

    /// <summary>
    /// 【検証中】敵の兵種。特徴は1体につき1つ。どの兵種もまずプレイヤーの命令をコピーし、
    /// そのあと「兵種の行動（TRAIT PHASE）」で特徴を使う（PhaseRules）。盤面の記号は BoardModel を参照。
    /// </summary>
    public enum EnemyKind
    {
        Normal,   // E：通常（コピーだけ）
        Archer,   // K：弓兵。コピーのあと、向いている方向へ矢を放つ（4マス・最初のユニットに2）
        Shield,   // T：盾兵。向いている側（正面）からの直接攻撃を -3
        Chaser,   // C：追跡兵。コピーのあと、プレイヤーへ1マス近づく
        Fleer,    // N：逃げ兵。コピーのあと、プレイヤーから1マス離れる
        Knight,   // B：簡易ボス「鏡守の騎士」。HP が半分より上は盾、半分以下は盾を失って矢を放つ
        // ───── 【検証中】Combat V2（2026-10-04）：どの兵種も「コピー＋分かりやすい固有行動1つ」─────
        Swordsman,     // S：剣兵。コピーのあと、自分の上下左右（隣の4マス）を斬る
        Spearman,      // Y：槍兵。コピーのあと、向いている方向へ2マスまっすぐ突く
        Bomber,        // O：砲兵。前のラウンドに予告したマスを爆発させ、あなたのいる場所を中心に次の爆発（十字5マス）を予告する
        MirrorSoldier, // M：鏡兵。方向を反対にしてコピーする（鏡）
        KnightV2,      // V：鏡守の騎士V2（簡易ボス）。鏡コピー＋剣。HP 70% 以下で矢も、40% 以下で炎床も
        KnightV3,      // Q：鏡守の騎士V3（検証ボス）。攻撃だけ反転コピー＋カウントダウン攻撃（大剣・爆発・炎の波）
        // ───── 【検証中】Copy Core V2（2026-10-05）：コピーを使わないと倒しにくい敵 ─────
        EchoKnight,    // R：反響鎧兵。あなたの技（あなたが実行した命令）では傷つかない。敵のコピー・兵種の行動・爆発・鏡晶なら傷つく
        SealGuard,     // G：封印兵。封印（2）がある間は傷つかない。敵のコピー攻撃が当たるたびに封印 -1、0 で解除
        Cannon,        // Z：写し砲。動かない・倒れない（勝ち負けに数えない）。あなたの攻撃の技だけをコピーし、一直線に撃つ
        Warden,        // W：鏡の番人（マイクロボス）。封印が割れた2ラウンドだけ傷つく。割り方は段階で変わる（コピー攻撃→鏡晶→封印の円）
        // ───── 【検証中】Copy Core V3（2026-10-05）：コピーは同じ向きのまま、「何を写すか」と盤面で使い道を作る ─────
        Lord,          // L：写し身の主（ボス）。まるまね。段階1 対の鏡核の守り → 段階2 癒しの写し身に斬らせる（写し身が残る間は次の段階へ進まない）→ 段階3 封印の円
        HealClone,     // H：癒しの写し身。手まね（技写し）。傷つかない。目覚めている間、毎ラウンド主を回復。写した攻撃で主を斬ると砕ける
        // ───── 【Puzzle v2】(2026-10-06) ─────
        Golem,         // D：運び像（道具）。移動だけまねる。倒れない・勝ち負けに数えない・兵種の行動なし。運び紋・踏み板へ運ぶ駒
        SealLord,      // U：封紋の守護者（パズル中心 ACT1 v2 のボス）。全まね。段階1 光る紋へ運ぶ → 段階2 剣の像に斬らせる → 段階3 鏡核を同時に（PuzzleBoss）
        BladeStatue,   // A：剣の像（ボスの部屋の道具）。技まね・動かない・倒れない・数えない。段階2で光り、まね攻撃で主を斬ると光が消える
        // ───── 【戦闘中心 v3】(2026-10-06) ─────
        Mender,        // J：癒し手。技まね。毎ターンの終わりに、いちばん傷ついた仲間を回復する。あなたの技をまねて、隣の仲間を殴ってしまう
        MirrorLord,    // F：鏡の主（戦闘中心 ACT1 v3 のボス）。全まね。殴れば減る。段階2 癒しの写し身（まねで主を斬らせると大ダメージ）→ 段階3 チャージ（鏡核の共鳴で止まる）（CombatBoss）
        // ───── 【Combat V4】(2026-10-07) 中ボス：本体＋護衛。複数のカウント大技（Clocks）を同時に持つ ─────
        ExecKnight,    // e：処刑騎士。全まね。隣を斬って近づく。追撃③・大薙ぎ③・処刑⑤（処刑のあと2ターン動けない：RC2）
        CannonKing,    // k：時計仕掛けの砲王。技まね（動かない）。小砲撃③・一斉砲撃⑤・巨砲⑦
        BeastKing,     // j：獣王。移動まね。積極的に動く。飛びかかり②・咆哮④・暴走突進⑥
    }

    /// <summary>
    /// ユニットのルール上のデータ（GameObject を使わない）。見た目は UnitController。
    /// </summary>
    public class UnitModel
    {
        public int Id { get; }
        public string Name { get; }
        /// <summary>敵の呼び名の記号（"A", "B" …）。プレイヤーと、敵が1体だけのときは空。</summary>
        public string Label { get; }
        public Team Team { get; }
        public CopyTrait CopyTrait { get; set; } = CopyTrait.Normal;
        /// <summary>【検証中・Copy Core V3】何を写すか（全写し・技写し・歩写し）。</summary>
        public CopyKind CopyKind { get; set; } = CopyKind.All;
        public int MaxHp { get; private set; }
        public int Hp { get; set; }
        public Vector2Int Position { get; set; }
        /// <summary>【検証中】兵種（プレイヤーは Normal のまま）。</summary>
        public EnemyKind Kind { get; set; } = EnemyKind.Normal;
        /// <summary>
        /// 向き。方向のある命令（一歩・斬撃・押し出しなど）を実行するたびに、その方向を向く（空振りでも）。
        /// 敵の場合は「最後にコピーした方向」＝プレイヤーの命令列の最後の方向で決まる。弓兵の狙い・盾の正面に使う。
        /// </summary>
        public Direction Facing { get; set; } = Direction.Down;
        /// <summary>【検証中】闘気：このターン、次の直接攻撃に足すダメージ。そのユニットの命令列が終わると 0 に戻る。</summary>
        public int AttackBonus { get; set; }

        /// <summary>【検証中・Copy Core V2】封印：0 より大きい間はダメージを受けない。BarrierRule：0＝コピー起点の攻撃で -1／1＝鏡晶の爆発だけ／2＝コピーで封印の円に入ると割れる。</summary>
        public int Barrier { get; set; }
        public int BarrierRule { get; set; }
        /// <summary>封印が割れてから経ったラウンド（鏡の番人：2 で封印が戻る）。</summary>
        public int BarrierBrokenRounds { get; set; }
        /// <summary>【Copy Core V2】鏡の番人：封印が戻るときに帰る場所（最初の位置）。</summary>
        public Vector2Int Home { get; set; }
        /// <summary>【Copy Core V2】勝ち負けに数えない物（写し砲）。</summary>
        public bool IsObject => Kind == EnemyKind.Cannon || Kind == EnemyKind.Golem || Kind == EnemyKind.BladeStatue; // 【Puzzle v2】運び像・剣の像も道具（倒れない・数えない）
        /// <summary>【Copy Core V2】押し出し・引き寄せ・入れ替えで動かない（写し砲）。</summary>
        public bool IsImmovable => Kind == EnemyKind.Cannon || Kind == EnemyKind.BladeStatue || Dormant;
        /// <summary>【Copy Core V2】反響鎧：あなたの技では傷つかない。【戦闘中心 v3】Soft ルールでは、まね攻撃・仕掛けが当たると鎧が割れて普通の兵になる。</summary>
        public bool EchoArmor => Kind == EnemyKind.EchoKnight && !ArmorBroken;
        /// <summary>【戦闘中心 v3】反響鎧が割れた（Soft ルールで、まね攻撃・仕掛けが当たった）。</summary>
        public bool ArmorBroken { get; set; }
        /// <summary>
        /// 【戦闘中心 v3】動けないラウンド数（鏡核の共鳴）。0 より大きい間は、まねない・兵種の行動をしない。ラウンドの終わりに 1 減る
        /// （共鳴を起こしたラウンドに 1 なら「このターンの残り」、2 なら「次のターンも」）。
        /// </summary>
        public int StunRounds { get; set; }
        /// <summary>【Copy Core V3】眠っている（癒しの写し身は主の段階2まで像のまま）：傷つかない・写さない・何もしない。</summary>
        public bool Dormant { get; set; }
        /// <summary>【Copy Core V3】段階が変わったラウンド：このラウンドの残りはまねない（写し身は終わりに目覚める）。予告と食い違わないように。</summary>
        public bool WakingUp { get; set; }
        /// <summary>【Puzzle v2】封紋の守護者の段階（0〜2）。剣の像は「使用済み」（1）に使う。</summary>
        public int BossStage { get; set; }

        /// <summary>【Copy Core V2】ステージで敵ごとの HP を決める（StageData.enemyHpList）。</summary>
        public void OverrideMaxHp(int hp) { MaxHp = Mathf.Max(1, hp); Hp = MaxHp; }

        public bool IsPlayer => Team == Team.Player;
        public bool IsAlive => Hp > 0;

        /// <summary>盾が有効か（盾兵、または HP が半分より上の騎士）。</summary>
        public bool HasShield => Kind == EnemyKind.Shield || (Kind == EnemyKind.Knight && Hp * 2 > MaxHp);

        /// <summary>矢を放つか（弓兵、または HP が半分以下になった騎士）。</summary>
        public bool ShootsArrows => Kind == EnemyKind.Archer || (Kind == EnemyKind.Knight && Hp * 2 <= MaxHp) || (Kind == EnemyKind.KnightV2 && Hp * 10 <= MaxHp * 7);
        /// <summary>【検証中】砲兵：次の兵種フェーズで爆発するマス（予告済み）。</summary>
        public List<Vector2Int> BombCells { get; set; } = new List<Vector2Int>();
        /// <summary>【検証中】騎士V3 のカウントダウン（0＝まだ使わない）。1 になったラウンドに当たるマスを決めて予告し、次のラウンドに 0 で発動。</summary>
        public int SlamCd { get; set; }
        public int BlastCd { get; set; }
        public int WaveCd { get; set; }
        /// <summary>【検証中】騎士V3：予告済みの大剣のマス／炎の波の回数（偶数＝中央3×3、奇数＝外周）。</summary>
        public List<Vector2Int> SlamCells { get; set; } = new List<Vector2Int>();
        public int WaveCount { get; set; }
        /// <summary>【Combat V4】カウント大技（時計）。残り ① で当たるマスが予告され、そのターンの最後（まねの後・完成版）に発動する。</summary>
        public List<Clock> Clocks { get; set; } = new List<Clock>();
        /// <summary>【Combat V4】沈黙：このラウンドの兵種の行動（大技も）を止める。コピーはする。ラウンドの終わりに戻る。</summary>
        public bool Silenced { get; set; }
        /// <summary>【Combat V4】模倣命令：このラウンドだけ全まねになる。ラウンドの終わりに戻る。</summary>
        public bool ForceAllCopy { get; set; }
        /// <summary>【RC6】孤立（最後の1体）：移動の札はまねない（あなたと同じ向きに歩いて逃げる追いかけっこを無くす）。TurnRules.IsolationNoMove のとき RulesEngine.UpdateIsolation が決める。</summary>
        public bool NoMoveCopy { get; set; }
        /// <summary>【Combat V4 RC2】追撃型：コピーのあとに兵種の行動をする（ほかの敵は 兵種の行動 → コピー）。</summary>
        public bool AfterCopy { get; set; }

        /// <summary>【RC5】画面の呼び名の記号：同じ兵種が2体以上いるときだけ、兵種ごとに A・B…（1体だけの兵種は空）。BattleModel.ApplyStage が決める。</summary>
        public string KindLetter { get; set; } = "";
        /// <summary>【RC5・監査】弓兵の矢が届くマスの数（ターンの初めの赤＝BattleModel.BeginRound で決める。-1＝決めていない）。前の敵があなたの命令で倒れても、矢は赤の先へ伸びない。</summary>
        public int ArrowReach { get; set; } = -1;

        public UnitModel(int id, string name, string label, Team team, int maxHp, Vector2Int position)
        {
            Id = id;
            Name = name;
            Label = label;
            Team = team;
            MaxHp = maxHp;
            Hp = maxHp;
            Position = position;
        }

        public void TakeDamage(int amount) => Hp = Mathf.Max(0, Hp - amount);

        /// <summary>回復（最大 HP まで）。実際に回復した量を返す。</summary>
        public int Heal(int amount)
        {
            // 【Copy Core V3】写し身の主は今の段階の上限までしか回復しない（回復で段階が戻らないように：レビュー）
            int cap = Kind == EnemyKind.Lord ? PhaseRules.LordHealCap(this) : Kind == EnemyKind.SealLord ? PuzzleBoss.HealCap(this) : Kind == EnemyKind.MirrorLord ? CombatBoss.HealCap(this) : MaxHp;
            int healed = Mathf.Clamp(cap - Hp, 0, amount);
            Hp += healed;
            return healed;
        }

        public UnitModel Clone() => new UnitModel(Id, Name, Label, Team, MaxHp, Position) { Hp = Hp, CopyTrait = CopyTrait, Kind = Kind, Facing = Facing, AttackBonus = AttackBonus, BombCells = new List<Vector2Int>(BombCells),
            SlamCd = SlamCd, BlastCd = BlastCd, WaveCd = WaveCd, SlamCells = new List<Vector2Int>(SlamCells), WaveCount = WaveCount,
            Barrier = Barrier, BarrierRule = BarrierRule, BarrierBrokenRounds = BarrierBrokenRounds, Home = Home, CopyKind = CopyKind, Dormant = Dormant, WakingUp = WakingUp, BossStage = BossStage, ArmorBroken = ArmorBroken, StunRounds = StunRounds,
            Clocks = Clocks.Select(c => c.Clone()).ToList(), Silenced = Silenced, ForceAllCopy = ForceAllCopy, NoMoveCopy = NoMoveCopy, AfterCopy = AfterCopy, KindLetter = KindLetter, ArrowReach = ArrowReach };
    }
}
