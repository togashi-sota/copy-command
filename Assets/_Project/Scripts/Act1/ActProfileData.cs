using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>【検証中】役職ごとの記録（最高到達・クリア・挑戦回数）。</summary>
    [Serializable]
    public class RoleRecord
    {
        public string roleId;
        public int runs, clears, bestFloor;
        /// <summary>【完成版】クリアしたときの最少ターン（0＝なし）・最短の分（0＝なし）・クリアした挑戦の段階 +1（0＝クリアなし・1＝ふつうでクリア…）・再挑戦なしのクリア回数。</summary>
        public int bestTurns, bestMinutes, clearedChallenge, cleanClears;
        /// <summary>【通常リリース版】クリアしたランの一番よい反響ランク（S・A・B・C。空＝まだ無い）。</summary>
        public string bestEchoRank;
    }

    /// <summary>
    /// 【検証中】プロフィール（ランをまたいで残る物）：役職の解放・役職ごとの記録・今までに使ったカード・見たヒント。
    /// 恒久的な強化（HP や攻撃力が増える等）は入れない（ゲーム性を壊さないため）。解放されるのは「遊び方」だけ。
    /// 2026-10-05 本人プレイ：「1回やったらもういいかな」→ 次のランの目標（解放・記録・初めて使うカード）を作る。
    /// </summary>
    [Serializable]
    public class ActProfileData
    {
        public int version = CurrentVersion;
        /// <summary>版（ActProfile.Version と同じ。Unity に依存しないようにここへ置く）。</summary>
        public const int CurrentVersion = 1;
        public int runsStarted, runsFinished, clears;
        public List<string> unlockedRoles = new List<string>();
        public List<RoleRecord> records = new List<RoleRecord>();
        public List<string> usedCards = new List<string>();
        public List<string> hintsSeen = new List<string>();
        public bool allRolesUnlocked;
        /// <summary>ヒント（1行の帯）を出さない（タイトルで切り替え）。</summary>
        public bool hintsOff;
        /// <summary>【RC2】最初の起動で「遊び方」を自動で開いた（1回だけ）。</summary>
        public bool howToSeen;

        // ───── 【完成版・2026-10-20】完成版 ACT1 の記録と解放（古いプロフィールには無い＝既定値で読める） ─────
        /// <summary>完成版の役職ごとの記録（今までの研究版の records とは別）。</summary>
        public List<RoleRecord> finalRecords = new List<RoleRecord>();
        /// <summary>完成版で解放した役職（旅の剣士は最初から）。</summary>
        public List<string> finalRoles = new List<string>();
        /// <summary>完成版で解放した札（V4Content.LockedCards のうち）。</summary>
        public List<string> finalCards = new List<string>();
        /// <summary>選べる挑戦の段階の最大（0＝ふつうだけ）。</summary>
        public int challengeUnlocked;
        /// <summary>完成版のランを始めた・終えた・クリアした回数。</summary>
        public int finalRuns, finalFinished, finalClears;
        /// <summary>最後に選んだ挑戦の段階（役職の画面の初期値）。</summary>
        public int lastChallenge;
        /// <summary>【RC2】音を消したか（ブラウザで再読み込みしても残るように）。</summary>
        public bool soundOff;
        /// <summary>【RC3】チュートリアルを最後までした／はじめての案内（チュートリアルを遊ぶ？）を見た。</summary>
        public bool tutorialDone, tutorialOffered;
        /// <summary>【RC3】音量（0〜100）。初めての起動は今までより小さく。</summary>
        public int volMaster = 50, volBgm = 35, volSe = 50;
        public bool volSet;
        /// <summary>【RC5】画面の揺れを止める（設定）。古いプロフィールには無い＝false（揺れる）。</summary>
        public bool shakeOff;
        /// <summary>【RC3】戦術札の説明（はじめて緑の札を引いたとき）を見た。</summary>
        public bool tacticIntroSeen;
        /// <summary>【RC11】刻印と強化の説明を見た（はじめて刻印が出たときだけ出す）。</summary>
        public bool imprintIntroSeen;

        public RoleRecord FinalRecord(string roleId)
        {
            var r = finalRecords.FirstOrDefault(x => x.roleId == roleId);
            if (r == null) { r = new RoleRecord { roleId = roleId }; finalRecords.Add(r); }
            return r;
        }

        public RoleRecord Record(string roleId)
        {
            var r = records.FirstOrDefault(x => x.roleId == roleId);
            if (r == null) { r = new RoleRecord { roleId = roleId }; records.Add(r); }
            return r;
        }
    }
}
