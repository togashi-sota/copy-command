using System;
using System.Collections.Generic;

namespace CopyCommand
{
    /// <summary>【検証中】ACT1 の中断セーブの中身（JSON）。カードはアセット名で持つ（参照で持たない＝バージョンが変わっても読める）。</summary>
    [Serializable]
    public class ActSaveData
    {
        /// <summary>セーブの版（ActSave.Version と同じ。Unity に依存しないようにここへ置く：pzsearch も ActRun をコンパイルする）。</summary>
        public const int CurrentVersion = 4; // 2026-10-07：4（Combat V4：戦術札のデッキ・V4 の地図）
        public int version = CurrentVersion;
        public string roleId;
        public int seed;
        public int floor;
        public int hp, maxHp, shards, removePrice;
        public List<string> deck = new List<string>();
        public List<string> items = new List<string>();
        public List<string> imprints = new List<string>();
        public List<string> history = new List<string>();
        public List<string> journal = new List<string>();
        public int battlesWon, totalTurns, totalDamage, shardsEarned, missionsDone;
        public string savedAt;
        // 版 2（2026-10-05）：結果画面の記録
        public int parBonuses;
        public int noDamageWins; // 【RC11】
        public List<string> usedKeys = new List<string>();
        public List<int> usedValues = new List<int>();
        public List<string> damageKeys = new List<string>();
        public List<int> damageValues = new List<int>();
        public List<int> hpFlow = new List<int>();
        // 【検証中・Copy Core V3】コピー中心のミニ ACT1（古いセーブは false）
        public bool copyCentric;
        // 【Puzzle v2】パズル中心 ACT1 v2（古いセーブは false）
        public bool puzzle;
        // ───── 版 3（2026-10-06）【戦闘中心 v3】：部屋の途中から続ける（中断で引き直し・逃げを防ぐ） ─────
        public bool combat;
        /// <summary>今いる部屋（空なら扉の画面）。</summary>
        public string roomId = "";
        /// <summary>【RC5】はじめてのラン（1階・2階・5階の扉を ActRun.ForceFirstDoor で決めた）。前は保存せず、2階の部屋の途中から再開すると「知らない部屋」で読めなかった。</summary>
        public bool firstLesson;
        /// <summary>【RC5・コードレビュー】地図の作り方の版（V4Content.MapRev）。無い（0）＝前の作り方：同じ種でも3階・6階の中ボスが違うので、古いセーブは古い作り方で読む。</summary>
        public int mapRev;
        /// <summary>"room"＝部屋に入った（戦闘なら最初から・HP は battleHp との低い方）／"reward"＝勝った後の報酬の画面。</summary>
        public string roomPhase = "";
        /// <summary>戦闘中の HP（ターンの始めに更新。続きからは入ったときとの低い方で最初から）。0 なら使わない。</summary>
        public int battleHp;
        /// <summary>【レビュー】戦闘中のターン（ターンの始めに更新）。続きからで 2 以上なら「途中から」＝ボーナス無し・試練は残りのターンだけ。</summary>
        public int battleTurn;
        public List<string> rewardCards = new List<string>();
        public List<string> rewardReasons = new List<string>();
        public int rewardPicks;
        public List<string> rewardImprints = new List<string>();
        public string rewardText = "";
        public List<int> shopSold = new List<int>();
        public bool shopImprintSold, shopRemoveUsed;
        /// <summary>【RC7】商人の刻印（品ぞろえの一部。"-"＝無し・空＝古いセーブ）。</summary>
        public string shopImprint = "";
        public double playSeconds;
        public int trialsCleared, trialsFailed, copyUses, copyKills;
        // ───── 版 4（2026-10-07）【Combat V4】戦術札のデッキ・報酬の追加購入の残り ─────
        public bool v4;
        public List<string> tactics = new List<string>();
        public int rewardExtra;
        public int bigHits, tacticUses;
        /// <summary>【通常リリース版】反響ランク（敵へのダメージの合計・あなたの直接攻撃でない分）。古いセーブは 0。</summary>
        public int enemyDamage, enemyDamageByOthers;
        // ───── 【完成版・2026-10-20】（版は 4 のまま：無い項目は既定値で読める） ─────
        public List<string> forged = new List<string>();
        /// <summary>入れ替えの残り（報酬・商人の画面から続けたとき）。</summary>
        public int swapsLeft, tacticSwapsLeft;
        public List<string> unlockedCards = new List<string>();
        public int challenge;
        public bool bossRetryUsed;
        public int bossEntryHp;
        // 【RC5・最終ゲート】商人の品ぞろえ（入ったときの物。続きからで作り直すと、買った札で候補が変わって引き直しになっていた）
        public bool shopStock;
        public List<string> shopCards = new List<string>();
        public List<int> shopCardPrices = new List<int>();
        public List<string> shopItems = new List<string>();
        public List<int> shopItemPrices = new List<int>();
        /// <summary>【RC5・最終ゲート】戦闘の途中（2ターン目以降）から「続きから」で再開した回数（結果と感想用のコピーに出す）。</summary>
        public int midBattleResumes;
        // 【RC6】強化（商人で買ったか・中ボスの報酬の無料の強化の残り）
        public bool shopUpgradeUsed;
        public int rewardUpgrades;
        /// <summary>【RC6】休息で選んだ物（heal／upgrade／remove）。</summary>
        public List<string> restDone = new List<string>();
    }
}
