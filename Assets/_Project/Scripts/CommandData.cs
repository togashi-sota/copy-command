using UnityEngine;

namespace CopyCommand
{
    /// <summary>技の種類。実際の動きは CommandExecutor に書いてある。</summary>
    public enum CommandType
    {
        Walk,        // 歩く（RUN では基本命令：カードではなく何回でも使える）
        Rush,        // 突進
        Slash,       // 斬撃（range 2 なら「突き」：2マス先まで届く）
        Push,        // 押し出し
        AroundSlash, // 上下左右への攻撃。敵の固有行動「周囲斬り」「十字斬り」と、カード「回転斬り」で使う
        Pull,        // 引き寄せ：range マス以内の最初のユニットを1マス手前へ
        PlaceSpike,  // 罠設置：隣の空いた床にトゲを置く
        // ───── 【検証中】新しい移動（RulesEngine を参照）─────
        Jump,        // 跳躍：ちょうど2マス先へ着地。間のユニット・トゲは越える（壁は越えない）。着地点がふさがっていれば空振り
        Diagonal,    // 斜め歩：斜めに1マス（方向を時計回りに45度：↑＝右上）
        Slide,       // 滑走：壁・ユニット・端にぶつかるまで進む（距離はルールで決まる）。通ったトゲは毎回踏む
        Blink,       // 転移：ちょうど3マス先へ瞬間移動。間は無視（壁も越える）。着地点がふさがっていれば空振り
        Swap,        // 入れ替え：隣のユニットと位置を入れ替える（どちらも相手のマスに「入る」）
        // ───── 【検証専用】盤面の端・ユニットの位置で結果が変わる移動（デッキには入れない。解析とテストだけ）─────
        WallKick,    // 壁蹴り：背中側が壁・端なら最大3マス、そうでなければ1マス
        Vault,       // 踏み越え：正面の隣のユニットを越えて2マス先へ。正面に誰もいなければ空振り
        // ───── 【検証中】新しい攻撃札（2026-10-04：攻撃の選択肢を増やす）─────
        PushSlash,   // 押し斬り：隣のユニットにダメージ＋1マス押す（押し先がふさがっていればダメージだけ）
        Pierce,      // 貫通：正面の直線 range マス以内の全ユニットにダメージ（壁で止まる）
        Sweep,       // 薙ぎ払い：正面のマスと、その左右のマス（計3マス）の全ユニットにダメージ
        Spirit,      // 闘気：このターン、次の直接攻撃のダメージ＋range（方向なし）。敵もコピーで強くなる
        // ───── 【検証中】ACT1 のビルド用（2026-10-04 夜）：盤面には何も起きない命令。効果はデッキ・命令枠に出る ─────
        Foresight,   // 先読み：次のターンの始めに、山札の上3枚から1枚を選んで手札へ（敵がコピーしても何も起きない）
        Reconsider,  // 再考：次のターン +range 枚引く（敵がコピーしても何も起きない）
        Rally,       // 号令：このターンの命令上限 +range（この札も1枠使う。増えた命令は敵もコピーする）
        ReverseCopy, // 【検証中・コピー操作】逆さ写し：このターン、敵は命令を逆の順番でコピーする（盤面では何もしない）
        Heal,        // 【検証中・2026-10-05】応急処置：自分の HP を range 回復（方向なし）。敵もコピーで自分を回復する
        Aim,         // 【検証中・Copy Core V3】狙い定め：向きだけ変える（盤面では何も動かない）。まねた敵も向きだけ変える（槍・弓・盾の向きを決める）
        // ───── 【Puzzle v2】(2026-10-06) 手札を回す札：命令に入れた瞬間に効く（このターンの命令に使える）。敵がまねても盤面では何も起きない ─────
        Draw,        // 引く札（手繰り寄せ・足取り・武技整理）：命令に入れた瞬間に range 枚引く（CommandData.drawFilter で移動／技だけ）。入れたら取り消せない
        Insight,     // 深読み：命令に入れた瞬間に、山札の上3枚から1枚を選んで手札へ。入れたら取り消せない
        Resonate,    // 共鳴補給：このラウンドに、まね（敵の攻撃・移動）で仕掛けが動いたら、次のターン +range 枚
        // ───── 【Combat V4】(2026-10-07) 戦術札（緑）：別のデッキ・命令枠を使わない・敵はコピーしない ─────
        Tactic,
    }

    /// <summary>【Combat V4】戦術札の種類。効果は TacticRules（盤面側）と TurnManager（手札・命令枠側）。</summary>
    public enum TacticKind
    {
        None,
        // 手札・命令の操作
        Breath,      // 深呼吸：通常札を2枚引く
        Sift,        // 選別：手札を2枚まで捨てて引き直す（手札整理と同じ画面）
        Muster,      // 号令：このターンの命令枠 +1
        GreatMuster, // 大号令：このターンの命令枠 +2
        Recall,      // 再演：最後に使った通常の命令の札を手札へ戻す
        // 敵の操作
        EarlyBell,   // 早鐘：選んだ敵の大技（いちばん近いもの）を1ターン早める（残り2以上）
        LateBell,    // 遅鐘：選んだ敵の大技（いちばん近いもの）を1ターン遅らせる
        Silence,     // 沈黙：選んだ敵の固有行動（大技も）をこのラウンド止める。コピーはする
        Mimic,       // 模倣命令：このラウンド、敵全員が全まねになる
        Tangle,      // 操糸：このラウンド、敵のコピーする移動の距離 +1
        Weight,      // 重し：このラウンド、敵のコピーする移動の距離 -1（最低1）
        // あなたの強化
        Focus,       // 集中：次の直接攻撃 +2
        Guard,       // 残心：次に受けるダメージ -2（1回）
        Mend,        // 応急処置：HP +3
        // コピー連動
        Echo,        // 反響：このラウンド、まねで敵にダメージが入ったら次のターン +1枚
        // 中ボス専用（倒した敵の力を奪う）
        Toll,        // 処刑の鐘：選んだ敵の全部の大技を1ターンずつ早める
        Detonate,    // 即時爆破：選んだ敵の大技（いちばん近いもの）を、このターンの終わりに発動させる
        Retarget,    // 照準替え：選んだ敵の予告中の大技を、別の敵のいる場所へ向け直す（2体を選ぶ）
        Pursuit,     // 追撃命令：このラウンド、敵のコピーする移動の距離 +2
        Roar,        // 咆哮：あなたの隣の敵を外へ1マス押す（1ダメージ）
        Parry,       // 見切り：次に受けるダメージ -4（1回）
    }

    /// <summary>【Puzzle v2】引く札の種類。</summary>
    public enum DrawFilter { Any, Move, Technique, MoveAndTechnique }

    /// <summary>
    /// 技札の基本データ（ScriptableObject）。Inspector で距離やダメージを調整できる。
    /// </summary>
    [CreateAssetMenu(menuName = "CopyCommand/Command Data", fileName = "CommandData")]
    public class CommandData : ScriptableObject
    {
        public string displayName = "技";
        public CommandType type;
        [TextArea(1, 3)] public string description = "";
        [Tooltip("カードに書く「敵にまねされると…」の説明（RUN のカード表示・報酬画面）")]
        [TextArea(1, 3)] public string copyNote = "";

        [Header("距離（最小と最大が違うときは、プレイヤーが選ぶ）")]
        public int minDistance = 1;
        public int maxDistance = 1;

        [Header("ダメージ（斬撃など）")]
        public int damage = 0;

        [Header("届く距離（斬撃・突き・引き寄せ・上下左右攻撃）")]
        public int range = 1;

        [Header("【検証中】まね操作")]
        [Tooltip("この命令は敵にまねされない（例：忍び足）")]
        public bool notCopied = false;
        [Tooltip("【検証中】この命令は、敵には反対向きでまねされる（例：反転斬り）")]
        public bool copyMirrored = false;
        [Tooltip("【検証中】敵がまねしたときだけ、ダメージがこの分だけ少ない（例：欠け斬り）")]
        public int copyDamageCut = 0;
        [Tooltip("【検証中】敵がまねしたときだけ、移動がこのマス数だけ短い（例：踏み込み）")]
        public int copyDistanceCut = 0;
        [Tooltip("【検証中】消耗：使うとこの戦闘では捨て札に戻らない（1戦で1回。例：応急処置）")]
        public bool exhaust = false;
        [Tooltip("【検証中・Copy Core V3】号令の札：あなた自身には何も起きない（敵がまねしたときだけ効く。例：けしかけ・進め）")]
        public bool selfInert = false;

        [Header("【Puzzle v2】手札を回す札（命令に入れた瞬間に効く）")]
        [Tooltip("命令に入れた瞬間に引く枚数（号令・改：上限 +2 と一緒に2枚）。引いたら、その命令から前は取り消せない。種類が Draw の札は range 枚")]
        public int planDraw = 0;
        [Tooltip("引く札の種類（Any 何でも／Move 移動の札だけ／Technique 技の札だけ）")]
        public DrawFilter drawFilter = DrawFilter.Any;
        [Tooltip("【Puzzle v2】再利用：命令に入れた瞬間に、捨て札から（drawFilter の種類の）札を range 枚選んで手札へ")]
        public bool fromDiscard = false;
        [Tooltip("【Puzzle v2】共鳴の一歩：このラウンドに、まねで仕掛けが動いたら次のターン +この枚数")]
        public int resonateDraw = 0;
        [Tooltip("【Puzzle v2】余韻の刃：このラウンドに、まね（敵の攻撃・まねの移動）で敵が倒れたら次のターン +この枚数")]
        public int copyKillDraw = 0;
        [Tooltip("【Puzzle v2】合わせ斬り：まねた敵だけ、正面と左右の3マスを斬る（あなたは正面1マス）")]
        public bool copyWide = false;

        [Header("【Combat V4】戦術札")]
        public TacticKind tactic = TacticKind.None;

        /// <summary>【Puzzle v2】命令に入れた瞬間に引く枚数。</summary>
        public int PlanDrawCount => type == CommandType.Draw && !fromDiscard ? Mathf.Max(1, range) : planDraw;
        /// <summary>【Puzzle v2】命令に入れた瞬間に効く（取り消せない）札か。</summary>
        public bool LocksPlan => PlanDrawCount > 0 || type == CommandType.Insight || fromDiscard;
        /// <summary>【Puzzle v2】次のターンに引く条件つきの枚数（共鳴：まねで仕掛け）。</summary>
        public int ResonateCount => type == CommandType.Resonate ? Mathf.Max(1, range) : resonateDraw;

        /// <summary>距離をプレイヤーが選ぶ技か（例：突進 2〜3）。</summary>
        public bool NeedsDistanceChoice => maxDistance > minDistance;

        /// <summary>方向を使う技か（上下左右への攻撃は全方向なので使わない）。</summary>
        public bool UsesDirection => type != CommandType.AroundSlash && type != CommandType.Spirit && type != CommandType.Heal && !IsNoBoardEffect;

        /// <summary>【検証中】盤面には何も起きない命令（先読み・再考・号令）。</summary>
        public bool IsNoBoardEffect => type == CommandType.Foresight || type == CommandType.Reconsider || type == CommandType.Rally || type == CommandType.ReverseCopy ||
                                       type == CommandType.Draw || type == CommandType.Insight || type == CommandType.Resonate || type == CommandType.Tactic;
    }
}
