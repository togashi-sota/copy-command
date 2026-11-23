using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>ルールの実行で起きた出来事の種類。演出係（BattlePresenter）が順番に再生する。</summary>
    public enum BattleEventType
    {
        Moved,        // ユニットが1マス動いた（歩く・突進の1歩ごと・押し出し・引き寄せ）
        Bumped,       // 壁・端・ユニットにぶつかって止まった
        Lunge,        // 攻撃の踏み込み（方向つき）
        Spin,         // 上下左右攻撃の回転
        TargetCell,   // 技の対象マス（光らせる）
        Damaged,      // ダメージを受けた（原因つき）
        Died,         // HP が 0 になった
        SpikePlaced,  // トゲが置かれた
        Healed,       // 【検証中】回復した（回復ルーン）
        // ───── 【検証中】Copy Core V2 ─────
        Deflected,       // 反響鎧・封印・写し砲・鏡晶が、あなたの技をはじいた（Unit か Cell）
        BarrierHit,      // 封印が1つ減った（Amount＝残り）
        BarrierBroken,   // 封印が割れた
        BarrierRestored, // 封印が戻った（鏡の番人）
        GateOpened,      // 扉が開いた（Cell＝扉のマス。1マスに1つ）
        CrystalBurst,    // 鏡晶が砕けて爆発した（Cell）
        CoreHit,         // 【Copy Core V3】鏡核に攻撃が当たった（Cell・Amount＝このラウンドに当たった数・Value は使わない）
        CoresShattered,  // 【Copy Core V3】対の鏡核が全部割れた（Cell＝割れた鏡核の1つ）
        Awakened,        // 【Copy Core V3】癒しの写し身が目覚めた（Unit）
        CloneShattered,  // 【Copy Core V3】癒しの写し身が主を斬って砕けた（Unit・Cell）
        PhaseChanged,    // 【Copy Core V3】写し身の主の段階が変わった（Unit・Amount＝新しい段階 1〜3）
        BarrelBurst,     // 【Copy Core V3】爆薬樽が爆発した（Cell）
        ChestOpened,     // 【Copy Core V3】写し箱が開いた（Cell。敵のまねた攻撃で）
        SealStoneBroken, // 【Puzzle v2】封印石が割れた（Cell・Amount＝残り）
        StatueTurned,    // 【Puzzle v2】石像が回った（Cell・Direction＝新しい向き）
        ExitOpened,      // 【Puzzle v2】出口が開いた（Cell）
        CarryDone,       // 【Puzzle v2】運び紋が全部埋まった（Cell＝最初の運び紋）
        PuzzleSolved,    // 【Puzzle v2】ボスの段階のパズルが解けた（Unit＝ボス・Amount＝解けた段階 1〜3）
        SealsWeakened,   // 【Puzzle v2】倒せる敵が全滅して、封印石・石像があなたの攻撃でも動くようになった（Text＝知らせる文）
        // ───── 【戦闘中心 v3】 ─────
        ArmorBroken,     // 反響鎧が割れた（Unit・Cell。まね攻撃・仕掛けが当たった）
        Resonance,       // 鏡核の共鳴（Cell＝割れた鏡核の1つ・Text＝知らせる文）：敵全員にダメージ・動けない
        BossCloneStrike, // 癒しの写し身のまね攻撃が主に当たった（Unit＝主・Amount＝ダメージ）
        // ───── 【Combat V4】 ─────
        ClockWarn,       // 大技の予告が出た（残り①。Unit＝撃つ敵・Text＝技名・Amount＝当たるマスの数）
        ClockFired,      // 大技が発動した（Unit＝撃つ敵・Text＝技名・Amount＝ダメージ）
        TacticPlayed,    // 戦術札を使った（Text＝札の名前）
        // ───── 【RC10】 ─────
        BossReflect,     // 主の攻撃が写し身に当たり、主へはね返った（Unit＝主・From＝写し身のマス・To＝主のマス・Amount＝主へのダメージ）
        // ───── 【RC11】 ─────
        LeaderDown,      // ボス・中ボス（部屋の主）が倒れた → 残りの敵は崩れ、戦闘はここで勝ち（Unit＝倒れた主）
    }

    public enum DamageKind
    {
        Direct,  // 斬撃・突き・回転斬りなどの直接攻撃
        Spike,   // トゲ（盤面ギミック）
        Innate,  // 敵の固有行動
        Curse,   // 【検証用】鏡蝕の呪い（敵の HP を減らせないラウンドが続いたとき）
        Trait,   // 【検証中】兵種の行動（弓兵の矢）。盾で減る
        Fire,    // 【検証中】炎床（環境）。盾では減らない
        Crystal, // 【Copy Core V2】鏡晶の爆発（敵のコピー攻撃で砕けたとき）。コピー起点
        Gaze,    // 【Puzzle v2】石像の視線（仕掛け）
        Seal,    // 【Puzzle v2】ボスのパズルが解けた（封印紋・写し身・鏡核）
        Resonance, // 【戦闘中心 v3】鏡核の共鳴（あなたとまねで同じターンに鏡核を叩いた）。コピー起点
        Clock,     // 【Combat V4】カウント大技（敵も巻き込む）
    }

    /// <summary>対象マスの光らせ方（色は演出係が決める）。</summary>
    public enum CellHighlight
    {
        Attack,
        Innate,
        Utility,
        Arrow,   // 【検証中】弓兵の矢
        Fire,    // 【検証中】炎床
        Heal,    // 【検証中】回復ルーン
    }

    /// <summary>
    /// ダメージの原因。将来のルーン片ボーナス・任意ミッション・ログに使う。
    /// 例：敵がコピーした「歩く」でトゲに入った → Source=敵, Kind=Spike, ByCopy=true
    ///     プレイヤーの「押し出し」で敵がトゲに入った → Source=プレイヤー, Kind=Spike, ByCopy=false
    /// </summary>
    public class DamageCause
    {
        /// <summary>その命令を実行したユニット（誰の命令か）。</summary>
        public UnitModel Source;
        /// <summary>原因の命令（カード）。</summary>
        public CommandData Command;
        public DamageKind Kind;
        /// <summary>敵がコピーした命令によるものか。</summary>
        public bool ByCopy;

        public override string ToString() =>
            $"{Source?.Name}の{Command?.displayName}（{Kind}{(ByCopy ? "・コピー" : "")}）";
    }

    /// <summary>ルールの実行で起きた出来事1つ。</summary>
    public class BattleEvent
    {
        public BattleEventType Type;
        /// <summary>主役のユニット（動いた・攻撃した・ダメージを受けた）。</summary>
        public UnitModel Unit;
        public Vector2Int From;
        public Vector2Int To;
        public Vector2Int Direction;
        public Vector2Int Cell;
        public CellHighlight Highlight;
        public int Amount;
        public int HpAfter;
        public DamageCause Cause;
        /// <summary>【Puzzle v2】はじいた理由など、その場に出す短い文（無ければ null）。</summary>
        public string Text;

        public override string ToString()
        {
            switch (Type)
            {
                case BattleEventType.Moved: return $"Moved {Unit?.Name} {From}->{To}";
                case BattleEventType.Damaged: return $"Damaged {Unit?.Name} -{Amount} hp={HpAfter} by {Cause}";
                default: return $"{Type} {Unit?.Name} {Cell}";
            }
        }
    }

    /// <summary>命令を実行するときの文脈（誰が・どの命令を・コピーか・固有行動か）。</summary>
    public struct CommandContext
    {
        public UnitModel Actor;
        public CommandRecord Record;
        public bool IsCopy;
        public bool IsInnate;

        public CommandContext(UnitModel actor, CommandRecord record, bool isCopy = false, bool isInnate = false)
        {
            Actor = actor;
            Record = record;
            IsCopy = isCopy;
            IsInnate = isInnate;
        }
    }
}
