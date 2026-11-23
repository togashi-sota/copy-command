using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭の進行（ActRunManager の partial）。
    /// 入口（LEVEL 1〜3 を選ぶ）→ 開始の窓（目的・特殊ルール1つ・敵の数｜自分で挑戦／お手本（スタッフ））→ 1つのクエスト（数ターンの戦闘）→ CLEAR → 結果（ランク・タイム・本日のランキング）。
    /// 倒れたら「もう一度挑戦／別の LEVEL へ／次の人へ」。どこでも「最初からやり直す」（盤面・手札・HP・敵・ターン・ヒント・タイムを全部はじめに戻す）。
    /// お手本（スタッフの実演：開始の窓のボタンか F8）の挑戦は記録しない。最初からやり直すと 来場者の番（記録する挑戦）になる。
    /// 5分体験・チュートリアルと同じ仕組み（InTutorial・QuickMode）で戦い、FestivalMode で文と記録だけを変える。本編のラン・プロフィール・セーブには触らない。
    /// </summary>
    public partial class ActRunManager
    {
        public bool FestivalMode { get; private set; }
        public int FestivalLevel { get; private set; }
        /// <summary>今の（または最後の）1回の挑戦の記録。</summary>
        public FestivalRun Festival { get; private set; }
        public FestivalLevelDef FestivalDef => FestivalContent.Levels[Mathf.Clamp(FestivalLevel, 0, FestivalContent.Levels.Length - 1)];
        TutorialStepDef[] festivalSteps;
        int festivalStepsLevel = -1;
        /// <summary>クエストの戦闘（TutorialSteps の中身：1つだけ）。</summary>
        TutorialStepDef[] FestivalSteps
        {
            get
            {
                if (festivalSteps == null || festivalStepsLevel != FestivalLevel) { festivalSteps = new[] { FestivalDef.Battle }; festivalStepsLevel = FestivalLevel; }
                return festivalSteps;
            }
        }
        /// <summary>結果の画面：ランク・PERFECT・本日のランキングの順位（1〜・0＝入らない）・前の1位。</summary>
        public string FestivalRankLetter { get; private set; }
        public bool FestivalPerfect { get; private set; }
        public int FestivalPlace { get; private set; }
        public FestivalStats.Row FestivalPrevBest { get; private set; }
        /// <summary>倒れた（HP 0）の窓：もう一度挑戦／別の LEVEL へ／次の人へ。</summary>
        public bool FestivalGameOverOpen { get; private set; }
        /// <summary>【演出】ターンの終わりの一言（「同士討ち ×2！」「ボスに 11ダメージ！」）と出した時刻。</summary>
        public string FestivalBanner { get; private set; }
        public float FestivalBannerAt { get; private set; } = -99f;
        /// <summary>ボス・ミニボスへの大きな一撃（演出を大きく）。</summary>
        public bool FestivalBannerBig { get; private set; }
        int festivalTurnDamage, festivalTurnCopy, festivalTurnFriendly, festivalTurnHazard, festivalTurnBig, festivalTurnBoss;

        /// <summary>お手本（スタッフの実演・記録しない）中か。</summary>
        public bool FestivalDemo => FestivalMode && Festival != null && Festival.Demo;

        /// <summary>文化祭の入口（LEVEL を選ぶ）。本編のランには触らない。</summary>
        public void OpenFestival()
        {
            ClearFestivalState();
            Screen = ActScreen.FestivalEntry;
        }

        void ClearFestivalState()
        {
            InTutorial = false;
            QuickMode = false;
            FestivalMode = false;
            MissionOpen = false;
            QuickIntroOpen = false;
            TutorialStepDone = false;
            TutorialFeedback = null;
            Message = null;
            PendingItem = PendingItemUse.None;
            BattleMenuOpen = false;
            FestivalIdlePrompt = false;
            FestivalGameOverOpen = false;
            FestivalBanner = null;
            Run = null;
        }

        /// <summary>LEVEL を選んで始める（0＝LEVEL 1）：開始の窓から。はじめての挑戦（1回目）。</summary>
        public void StartFestival(int level)
        {
            ClearFestivalState();
            FestivalLevel = Mathf.Clamp(level, 0, FestivalContent.Levels.Length - 1);
            FestivalMode = true;
            InTutorial = true;
            QuickMode = true;
            BeginFestivalAttempt(1, false, true);
        }

        /// <summary>
        /// 挑戦を はじめの状態から始める：盤面・手札・HP・敵・ターン・ヒント・タイム・記録を全部 はじめに戻す。
        /// briefing＝開始の窓を出す（はじめて・お手本のあと）。出さないときは すぐに時計が進む。
        /// </summary>
        void BeginFestivalAttempt(int attempt, bool demo, bool briefing)
        {
            TutorialStep = 0;
            TutorialRetries = 0;
            TutorialStepDone = false;
            TutorialFeedback = null;
            HintLevel = 0; AnswerShown = false;
            FestivalGameOverOpen = false;
            FestivalIdlePrompt = false;
            FestivalBanner = null;
            BattleMenuOpen = false;
            Festival = new FestivalRun { Level = FestivalLevel, StartedAt = Time.unscaledTime, Attempt = Mathf.Max(1, attempt), Demo = demo };
            FestivalRankLetter = null; FestivalPerfect = false; FestivalPlace = 0; FestivalPrevBest = null;
            LoadTutorialStep();
            MissionOpen = briefing;
            QuickIntroOpen = false;
            stepStartedAt = Time.unscaledTime;
            ResetFestivalTurn();
            lastInputAt = Time.unscaledTime;
        }

        /// <summary>開始の窓の「自分で挑戦」：時計はここから。</summary>
        public void FestivalBeginPlay()
        {
            if (!FestivalMode || Festival == null) return;
            Festival.Demo = false;
            Festival.StartedAt = Time.unscaledTime;
            StartMission();
        }

        /// <summary>開始の窓の「お手本（スタッフ）」：スタッフが操作して見せる（記録しない）。終わったら「最初からやり直す」で来場者の番へ。</summary>
        public void FestivalBeginDemo()
        {
            if (!FestivalMode || Festival == null) return;
            Festival.Demo = true;
            Festival.StartedAt = Time.unscaledTime;
            StartMission();
        }

        /// <summary>開始の窓の「？ 遊び方（絵）」：何のゲームかの絵（閉じると開始の窓に戻る）。</summary>
        public void FestivalShowIntro() { if (FestivalMode && MissionOpen) QuickIntroOpen = true; }

        /// <summary>
        /// 【F8・スタッフ実演モード】お手本の ON／OFF。ON：今の挑戦を記録しない（画面の上に「お手本中」）。
        /// OFF：来場者の番＝はじめの状態からやり直して開始の窓（スタッフが動かした盤面のまま渡さない）。
        /// </summary>
        public void FestivalToggleDemo()
        {
            if (!FestivalMode || Festival == null || Screen != ActScreen.Battle) return;
            if (!Festival.Demo) { Festival.Demo = true; return; }
            BeginFestivalAttempt(1, false, true);
        }

        /// <summary>
        /// 「最初からやり直す」：お手本のあとなら 来場者の番（1回目・開始の窓から）。来場者の挑戦の途中なら 記録（restart）して、次の挑戦をすぐに始める。
        /// </summary>
        public void FestivalRestart()
        {
            if (!FestivalMode || Festival == null) return;
            if (Festival.Demo) { BeginFestivalAttempt(1, false, true); return; }
            bool progressed = turn != null && (turn.TurnNumber > 1 || turn.State != BattleState.PlayerInput) || Festival.EnemyHits > 0 || Festival.Damage > 0;
            if (progressed) EndFestivalAttempt("restart");
            BeginFestivalAttempt(progressed ? Festival.Attempt + 1 : Festival.Attempt, false, false);
        }

        /// <summary>倒れた窓の「もう一度挑戦」：同じ LEVEL を はじめの状態から（何回目の挑戦かは +1）。</summary>
        public void FestivalRetryAfterGameOver()
        {
            if (!FestivalMode || Festival == null) return;
            BeginFestivalAttempt(Festival.Demo ? 1 : Festival.Attempt + 1, Festival.Demo, false);
        }

        /// <summary>挑戦の終わり（クリア以外）：記録の行を書く（お手本は書かない）。</summary>
        void EndFestivalAttempt(string result)
        {
            if (Festival == null || Festival.Finished || Festival.Abandoned) return;
            if (turn != null) Festival.Turns = turn.State == BattleState.GameOver || result == "gameover" ? turn.TurnNumber : Mathf.Max(0, turn.TurnNumber - 1);
            Festival.Abandoned = true;
            Festival.Result = result;
            Festival.EndedAt = Time.unscaledTime;
            FestivalStats.Append(Festival, "-", false, System.DateTime.Now);
        }

        /// <summary>UpdateTutorial から：倒れた。窓を出す（記録は gameover）。</summary>
        void FestivalGameOver()
        {
            if (FestivalGameOverOpen) return;
            EndFestivalAttempt("gameover");
            FestivalGameOverOpen = true;
            Sfx.Play(Sfx.Kind.Lose);
        }

        void ResetFestivalTurn()
        {
            festivalTurnDamage = festivalTurnCopy = festivalTurnFriendly = festivalTurnHazard = festivalTurnBig = festivalTurnBoss = 0;
        }

        /// <summary>今の秒（画面の時計）。開始の窓のあいだは 0。</summary>
        public float FestivalElapsed => Festival == null || MissionOpen ? 0f : TutorialStepDone && Festival.EndedAt > Festival.StartedAt ? Festival.EndedAt - Festival.StartedAt : Festival.SecondsAt(Time.unscaledTime); // 【初見レビュー2回目（採用担当）「CLEAR のあとも時計が進み、結果と 1 秒ずれる」】

        /// <summary>UpdateTutorial から：新しい出来事を数える（被ダメ・敵を利用した攻撃）。ターンの終わりに演出の一言を決める。</summary>
        void FestivalCount(List<BattleEvent> fresh, bool turnEnded)
        {
            if (!FestivalMode || Festival == null) return;
            foreach (var e in fresh)
            {
                if (e.Type != BattleEventType.Damaged || e.Unit == null || e.Amount <= 0) continue;
                if (e.Unit.IsPlayer) { Festival.Damage += e.Amount; continue; }
                if (e.Unit.IsObject) continue;
                if (PhaseRules.IsLeader(e.Unit)) festivalTurnBoss += e.Amount;
                festivalTurnDamage += e.Amount;
                if (FestivalRun.IsPlayerDirect(e.Cause)) continue;
                int c0 = Festival.CopyHits, f0 = Festival.FriendlyHits, h0 = Festival.HazardHits, b0 = Festival.BigHits;
                Festival.CountEnemyHit(e.Cause);
                festivalTurnCopy += Festival.CopyHits - c0; festivalTurnFriendly += Festival.FriendlyHits - f0;
                festivalTurnHazard += Festival.HazardHits - h0; festivalTurnBig += Festival.BigHits - b0;
            }
            if (turnEnded) FinishFestivalTurn();
        }

        /// <summary>ターンの終わり：起きたことを大きく（横から見ている人にも「何か起きた」）。</summary>
        void FinishFestivalTurn()
        {
            if (festivalTurnDamage > Festival.BestTurnDamage) Festival.BestTurnDamage = festivalTurnDamage;
            string text = null; bool big = false;
            var parts = new List<string>();
            int friendly = festivalTurnCopy + festivalTurnFriendly;
            string hazard = turn != null && turn.CurrentStage != null && !string.IsNullOrEmpty(turn.CurrentStage.hazardName) ? turn.CurrentStage.hazardName : "仕掛け";
            int kinds = (festivalTurnCopy > 0 ? 1 : 0) + (festivalTurnFriendly > 0 ? 1 : 0) + (festivalTurnHazard > 0 ? 1 : 0) + (festivalTurnBig > 0 ? 1 : 0);
            if (friendly > 0) parts.Add(friendly >= 2 ? $"同士討ち ×{friendly}" : "同士討ち");
            if (festivalTurnBig > 0) parts.Add(festivalTurnBig >= 2 ? $"大技が敵に ×{festivalTurnBig}" : "大技が敵に");
            if (festivalTurnHazard > 0) parts.Add(festivalTurnHazard >= 2 ? $"{hazard} ×{festivalTurnHazard}" : $"{hazard}が命中");
            if (festivalTurnBoss >= 8) { text = (kinds >= 2 ? "連鎖！ " : "") + $"ボスに {festivalTurnBoss}ダメージ！"; big = true; }
            else if (parts.Count > 0) { text = (kinds >= 2 ? "連鎖！ " : "") + string.Join("＋", parts.Take(2)) + "！"; big = kinds >= 2 && festivalTurnDamage >= 10; }
            if (text != null) { FestivalBanner = text; FestivalBannerAt = Time.unscaledTime; FestivalBannerBig = big; }
            ResetFestivalTurn();
        }

        /// <summary>クエストをクリアした瞬間：ターンと秒を残す（CLEAR! の窓の数字）。</summary>
        void FestivalBattleCleared()
        {
            if (!FestivalMode || Festival == null) return;
            Festival.Turns = turn.TurnNumber;
            Festival.EndedAt = Time.unscaledTime;
        }

        /// <summary>CLEAR! の窓の「結果を見る」：ランク・PERFECT・記録（CSV）・本日のランキング。</summary>
        void FinishFestival()
        {
            InTutorial = false;
            if (Festival == null) { OpenFestival(); return; }
            Festival.Finished = true;
            Festival.Result = "clear";
            if (Festival.EndedAt <= Festival.StartedAt) Festival.EndedAt = Time.unscaledTime;
            var now = System.DateTime.Now;
            FestivalRankLetter = FestivalRank.Rank(FestivalDef, Festival);
            FestivalPerfect = FestivalRank.Perfect(FestivalDef, Festival);
            FestivalPrevBest = FestivalStats.TodayBest(FestivalLevel, now);
            FestivalPlace = Festival.Demo ? 0 : FestivalStats.PlaceOf(FestivalLevel, now, FestivalRankLetter, Festival.Turns, Festival.Seconds);
            FestivalStats.Append(Festival, FestivalRankLetter, FestivalPerfect, now);
            Screen = ActScreen.FestivalResult;
            resultShownAt = Time.unscaledTime;
            Sfx.Play(Sfx.Kind.Clear);
        }

        /// <summary>途中でやめた（入口へ・次の人へ・本編へ・操作が無くて入口へ）：記録は quit／idle。</summary>
        void AbandonFestival(string result = "quit")
        {
            if (!FestivalMode || Festival == null || Festival.Finished || Festival.Abandoned) return;
            if (Screen != ActScreen.Battle || MissionOpen && turn != null && turn.TurnNumber <= 1) { Festival.Abandoned = true; return; } // 始める前にやめた：記録しない
            EndFestivalAttempt(result);
        }

        /// <summary>結果の「もう一度」（同じ LEVEL・開始の窓から・はじめての挑戦として）。</summary>
        public void FestivalAgain() => StartFestival(FestivalLevel);

        /// <summary>結果・倒れた窓の「別の LEVEL へ」：入口へ（本日の記録は残す）。</summary>
        public void FestivalOtherLevel()
        {
            AbandonFestival();
            OpenFestival();
        }

        /// <summary>「次の人へ」：文化祭の一時的な状態だけを消して入口へ（本日の記録とランキングは残す）。展示の PC では本編の中断セーブも消す。</summary>
        public void FestivalNextPerson()
        {
            AbandonFestival();
            if (BuildInfo.FestivalKiosk && SaveEnabled && ActSave.Exists()) ActSave.Delete();
            FestivalResetMainGame(); // 【2026-11-14 本人】次の人の本編は はじめての状態から（文化祭版だけ・通常版では何もしない）
            Festival = null;
            OpenFestival();
        }

        /// <summary>結果の「もっと遊ぶ（本編）」：タイトル（本編）へ。文化祭の記録は本編に入らない。
        /// 【2026-11-14 本人】文化祭版は、本編を はじめての状態にしてから（前の人のランが終わっていても、1階の練習・チュートリアルの案内から）。中断したランがあるときは残す（続きから遊べる）。</summary>
        public void FestivalToMain()
        {
            AbandonFestival();
            ClearFestivalState();
            if (!(SaveEnabled && ActSave.Exists())) FestivalResetMainGame();
            OpenTitle();
        }

        /// <summary>戦闘中の「やめる」：入口へ戻る（記録は途中で終わり）。</summary>
        public void QuitFestivalBattle()
        {
            AbandonFestival();
            OpenFestival();
        }

        /// <summary>
        /// LEVEL 3 の長く止まったときの強いヒント：3分たつか 7ターン目から出せる（それまではヒント3つ）。
        /// </summary>
        public bool FestivalLateHintReady => FestivalMode && Festival != null && !string.IsNullOrEmpty(FestivalDef.LateHint) && !MissionOpen
            && (Festival.SecondsAt(Time.unscaledTime) > 180f || (turn != null && turn.TurnNumber >= 7));

        // ───── 展示の PC（キオスク）：しばらく操作が無いとき ─────

        /// <summary>「まだ遊んでいますか？」の確認が出ている。</summary>
        public bool FestivalIdlePrompt { get; private set; }
        /// <summary>確認が出てから入口へ戻るまでの残り秒。</summary>
        public float FestivalIdleLeft => Mathf.Max(0f, IdleConfirmSeconds - (Time.unscaledTime - idlePromptAt));
        /// <summary>【文化祭クエスト v2】1つのクエストを考える時間は長い（上級は 1ターン 1分もある）→ 戦闘中は 3分で確認。</summary>
        public const float IdleBattleSeconds = 180f, IdleResultSeconds = 75f, IdleConfirmSeconds = 20f;
        float lastInputAt, idlePromptAt, resultShownAt;
        Vector3 lastMouse;

        /// <summary>確認の「続ける」。</summary>
        public void FestivalKeepPlaying() { FestivalIdlePrompt = false; lastInputAt = Time.unscaledTime; }

        void UpdateFestivalIdle()
        {
            DamagePopup.Scale = FestivalMode && InBattle ? 1.5f : 1f; // 【文化祭・横から見る人のレビュー「-4・同士討ち！が小さい」】
            if (FestivalMode && InBattle && Input.GetKeyDown(KeyCode.F8)) FestivalToggleDemo(); // 【文化祭クエスト v2】スタッフ実演モード
            if (!BuildInfo.FestivalKiosk) return;
            bool input = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f || (Input.mousePosition - lastMouse).sqrMagnitude > 4f;
            lastMouse = Input.mousePosition;
            if (input && !FestivalIdlePrompt) lastInputAt = Time.unscaledTime;
            if (Screen == ActScreen.FestivalEntry || Screen == ActScreen.Title) { FestivalIdlePrompt = false; return; } // 入口・タイトルでは何もしない
            if (Screen == ActScreen.FestivalResult)
            {
                // 結果の画面：しばらく操作が無ければ、確認なしで入口へ（記録とランキングは残す）
                if (Time.unscaledTime - Mathf.Max(lastInputAt, resultShownAt) > IdleResultSeconds) FestivalNextPerson();
                return;
            }
            if (FestivalDemo) { FestivalIdlePrompt = false; return; } // お手本中（スタッフが横で話している）は戻さない
            // 遊んでいる途中（文化祭の戦闘・本編）：先に確認を出す。確認のあいだに操作があれば戻らない
            if (!FestivalIdlePrompt && Time.unscaledTime - lastInputAt > IdleBattleSeconds) { FestivalIdlePrompt = true; idlePromptAt = Time.unscaledTime; }
            if (FestivalIdlePrompt && FestivalIdleLeft <= 0f)
            {
                FestivalIdlePrompt = false;
                if (FestivalMode) AbandonFestival("idle");
                else if (Run != null && InBattle) Checkpoint();
                FestivalNextPerson();
            }
        }

        /// <summary>ヒントを使った数を記録へ。</summary>
        void FestivalNoteHint() { if (FestivalMode && Festival != null) Festival.Hints++; }
    }
}
