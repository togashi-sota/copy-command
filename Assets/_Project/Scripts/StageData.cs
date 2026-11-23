using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 1ステージ分の設定。Inspector で盤面を文字で描いて調整する。
    ///   .  床      #  壁      ^  トゲ
    ///   P  プレイヤーの初期位置      E  敵の初期位置
    /// 1行目が盤面のいちばん上。行数・列数がそのまま盤面サイズになる。
    /// </summary>
    [CreateAssetMenu(menuName = "CopyCommand/Stage Data", fileName = "StageData")]
    public class StageData : ScriptableObject
    {
        [Header("表示")]
        public string stageTitle = "STAGE";
        [Tooltip("画面下の「ステージ切替」ボタンに出す短い名前")]
        public string buttonLabel = "?";
        [Tooltip("動作確認用のステージ（クリア後の「次のステージへ」の行き先にしない）")]
        public bool isTestStage = false;
        [TextArea(2, 6)] public string tutorialText = "";
        [Tooltip("1ターン目が終わった後に、説明文の代わりに出すヒント（空なら説明文のまま）")]
        [TextArea(2, 6)] public string hintAfterFirstTurn = "";
        [Tooltip("【検証中】見た目のテーマ（空ならグレーBOX）。ルールには関係しない。ArtSetup が作る RuinsTheme を入れる")]
        public VisualTheme visualTheme;

        [Header("盤面（. 床 / # 壁 / ^ トゲ / P プレイヤー / E 敵）")]
        [TextArea(3, 12)] public string map = ".......\n.......\n.P...E.\n.......\n.......";

        [Header("数値（仮値。プレイテストで調整する）")]
        public int playerMaxHp = 12;
        [Tooltip("【検証中】戦闘開始時の HP（0 なら最大 HP）。回復ルーンの検証用")]
        public int playerStartHp = 0;
        public int enemyMaxHp = 12;
        public int spikeDamage = 5;

        [Header("命令スロット・手札")]
        [Range(1, 3)] public int maxSlots = 3;
        [Tooltip("このステージで使える技札。手札にはこれが全部並ぶ。")]
        public List<CommandData> availableCommands = new List<CommandData>();

        [Header("手札方式の比較用デッキ（手札：A / B のときに使う。空ならカードを2枚ずつ）")]
        public List<CommandData> handTestDeck = new List<CommandData>();
        [Tooltip("【検証中】大きい手札（初手8・上限10）のときに使うデッキ。空なら handTestDeck")]
        public List<CommandData> handTestDeckLarge = new List<CommandData>();
        [Tooltip("【検証用】比較用デッキの先頭から何枚を初手に固定するか（0 なら初手もシャッフル）")]
        [Range(0, 5)] public int handTestOpeningCount = 0;
        [Tooltip("【検証用】初手以外の山札も、リストの順番どおりに引く（1周目だけ。山札が切れた後のシャッフルは A/B 共通の種）")]
        public bool handTestKeepOrder = false;
        [Tooltip("【検証用】ターン開始時に手札に攻撃札が無ければ、手札の1枚と山札の攻撃札を入れ替える（手札の枚数は変わらない）")]
        public bool handTestGuaranteeAttack = false;

        [Header("【検証用・未確定】鏡蝕ゲージ（画面右下の「鏡蝕」ボタンが ON のときだけ有効）")]
        [Tooltip("このステージで鏡蝕を使えるか（STAGE 1〜3 は OFF のまま）")]
        public bool stagnationAllowed = false;
        [Tooltip("敵の HP を減らせないラウンドが何回続くとゲージが満ちるか")]
        [Range(1, 6)] public int stagnationMax = 3;
        [Tooltip("ゲージが満ちたあと、さらに減らせなかったラウンドごとの呪いのダメージ（最後の値をくり返す）")]
        public List<int> stagnationPenalties = new List<int> { 2, 3, 4 };

        [Header("【検証中】兵種・環境（盤面の記号：K 弓兵 / T 盾兵 / C 追跡兵 / N 逃げ兵 / B 騎士 / 1〜9 回復ルーンの地点）")]
        [Tooltip("敵の最初の向き（敵A, 敵B … の順に U/D/L/R。足りない分は D）。向きはまねした命令の方向で変わる")]
        public string enemyFacings = "";
        [Tooltip("炎床のパターン（1要素＝1ラウンド。行は / 区切り、F が燃えるマス）。順番に循環する。空なら炎なし")]
        public List<string> firePatterns = new List<string>();
        public int fireDamage = 3;
        [Tooltip("騎士の HP が半分以下になった次のラウンドから炎が燃え始める（簡易ボス用）")]
        public bool fireAfterKnightHalf = false;
        [Tooltip("回復ルーンを使える回数（永久回復を防ぐ）")]
        public int runeUses = 3;
        public int runeHeal = 3;
        [Tooltip("【検証中】回復マス（盤面の h）で回復する量。一度踏むと消える")]
        public int healTileAmount = 4;

        [Header("【検証中】Copy Core V2（サンドボックスのステージだけ）")]
        [Tooltip("サンドボックスのステージ：タイトル・検証メニューで選んだまねの方式と COPY ON/OFF を使う")]
        public bool copySandbox = false;
        [Tooltip("このステージのまねの方式（ACT1・今までのステージは Current）")]
        public CopyMode copyMode = CopyMode.Current;
        [Tooltip("敵ごとの HP（読み順。例 \"14,14,24\"。空なら enemyMaxHp）")]
        public string enemyHpList = "";
        [Tooltip("サンドボックスの一覧に出す説明（何を学ぶステージか）")]
        [TextArea(2, 4)] public string sandboxGoal = "";

        [Header("【検証中】Copy Core V3（まねの種類）")]
        [Tooltip("敵ごとのまねの種類（読み順に1文字：A 全写し／T 技写し／M 歩写し／N 写さない。空なら全写し。写し砲・癒しの写し身は既定で技写し）")]
        public string enemyCopyTypes = "";
        [Tooltip("Copy Core V3 の検証部屋：COPY ON/OFF の切り替えが効く（方式は A のまま）")]
        public bool copyLab = false;
        [Tooltip("【Copy Core V3】鏡核の守り：盤面の鏡核（c）を同じラウンドに全部叩くまで、敵は傷つかない")]
        public bool coreGuard = false;

        [Header("【Puzzle v2】部屋の目標とパズルの仕掛け（盤面の記号：x 出口 / v 運び紋 / s 封印石 / i 石像 / D 運び像）")]
        [Tooltip("部屋の勝ち方：Defeat 討伐／Unseal 封印解除／Carry 搬送／Sync 同期／Escape 脱出")]
        public RoomGoal goal = RoomGoal.Defeat;
        [Tooltip("画面に常時出す目標の一文（空なら目標から自動）。答え（手順）は書かない")]
        public string goalText = "";
        [Tooltip("石像（i）の最初の向き（読み順に U/D/L/R）")]
        public string statueFacings = "";
        [Tooltip("巡回の石像：撃った後に時計回りに 90 度回る")]
        public bool statueSpin = false;
        [Tooltip("【Puzzle v2】円の守り：兵・反響鎧兵は守りを持つ。まねの移動で白い円（o）に入ると割れる（研究 H08 封印の行進）")]
        public bool circleGuard = false;
        [Tooltip("【Puzzle v2】パズル中心 ACT1 v2 の部屋（攻略条件の欄・凡例・まね方の印を出す）")]
        public bool puzzleRoom = false;

        [Header("【戦闘中心 v3】(2026-10-06) 戦闘が主役・まねは強い武器")]
        [Tooltip("戦闘中心 ACT1 v3 の部屋：Soft ルール（反響鎧は直接でも1・まねで鎧が割れる／鏡核の共鳴で敵全員にダメージ・動けない・数ターンで戻る／癒しの写し身は殴れる）。攻略条件の欄・まね方の印も出す")]
        public bool combatRoom = false;
        [Tooltip("【戦闘中心 v3】試練の部屋：このターン数以内に目標を達成すると成功（0 なら制限なし）。過ぎたら失敗（報酬なし・負けではない）")]
        public int turnLimit = 0;
        [Tooltip("【Combat V4】カウント大技・敵の積極的な動き・戦術札（Soft ルールも含む）")]
        public bool combatV4 = false;
        [Tooltip("【RC3】大技（カウントの時計）を持たせない（チュートリアル・はじめてのランの1階）")]
        public bool noClocks = false;
        [Tooltip("【RC11】敵は自分では動かない（まねでだけ動く）：落石の部屋・5分体験のパズル。部屋ファイルの still=1")]
        public bool stillEnemies = false;
        [Tooltip("【RC11】砲兵の最初の照準（盤面の列,行：行は上から 0）。部屋ファイルの aim=3,2。空なら いつもどおり（あなたへ）")]
        public string bomberAim = "";
        [Tooltip("【RC11】大技を始めから①（範囲が紫で見えている）。部屋ファイルの ready=1（5分体験の大技のパズル）")]
        public bool clocksReady = false;
        [Tooltip("【RC11】ready= に記号（例 ready=B）を書いたとき、その記号の敵の大技だけを始めから①（空なら全員）")]
        public string clocksReadyOnly = "";
        [Tooltip("【文化祭】敵ごとの大技を先頭の N 個だけにする（0＝全部）。中ボスの大技3つは3〜5分の体験には多い。部屋ファイルの bigs=1")]
        public int clockKeep = 0;
        [Tooltip("【RC3】チュートリアルの何番目か（0 ならチュートリアルではない）")]
        public int tutorialStep = 0;
        [Tooltip("【Combat V4】動く盤面の危険（firePatterns）の呼び名：炎床／落石／レーザー など（画面の説明用）")]
        public string hazardName = "";

        [Header("敵の固有行動（空なら無し）")]
        [Tooltip("ENEMY TURN の最初に毎ターン必ず使う（固有行動 → まね命令 の順）。まねされず、プレイヤーは使えない。")]
        public CommandData enemyInnateAction;

        /// <summary>手札方式の比較用デッキ。指定が無ければ、使えるカードを2枚ずつ。</summary>
        public List<CommandData> GetHandTestDeck()
        {
            if (handTestDeck != null && handTestDeck.Count > 0) return handTestDeck;
            var deck = new List<CommandData>();
            foreach (var c in availableCommands) { deck.Add(c); deck.Add(c); }
            return deck;
        }

        /// <summary>盤面文字列を1行ずつに分ける（空行と前後の空白は無視）。</summary>
        [Tooltip("【完成版】開始配置の別案（map と同じ書き方。敵の記号の読み順・兵種は map と同じにする）。ランでは部屋ごとの乱数で map か別案のどれかを使う")]
        [TextArea(3, 12)] public List<string> altMaps = new List<string>();

        /// <summary>【完成版】layout（部屋ごとの乱数）から、使う盤面の番号（0＝map・1 以上＝altMaps）。</summary>
        public int LayoutIndex(int layout) => altMaps == null || altMaps.Count == 0 ? 0 : (layout & 0x7fffffff) % (1 + altMaps.Count);

        /// <summary>【完成版】開始配置の別案も含めた盤面。</summary>
        public string[] GetMapRows(int layout)
        {
            int i = LayoutIndex(layout);
            return i == 0 ? GetMapRows() : Rows(altMaps[i - 1]);
        }

        public string[] GetMapRows() => Rows(map);

        static string[] Rows(string text)
        {
            var rows = new List<string>();
            foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0) rows.Add(trimmed);
            }
            return rows.ToArray();
        }
    }
}
