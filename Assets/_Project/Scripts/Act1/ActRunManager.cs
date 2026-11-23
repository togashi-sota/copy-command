using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】ACT1 の画面。</summary>
    public enum ActScreen { None, Title, Role, Boon, Doors, Battle, Reward, Shop, Rest, Event, Result, CopyLab, CopyLabV3, TutorialDone, TutorialRun, FestivalEntry, FestivalResult } // 【文化祭】入口・結果

    /// <summary>戦闘中に「選んでから効く」アイテムの待ち状態。</summary>
    public enum PendingItemUse { None, BlankStone, Hook, WarpStone }

    /// <summary>
    /// 【検証中】ACT1 縦切りの進行役（2026-10-04 夜）。ActRun（データ）を持ち、戦闘は TurnManager に任せる。
    /// 進行：タイトル → 役職 → 扉を選ぶ → 部屋（戦闘→報酬／休息／商人／特殊）→ 次の階 … → ボス → ACT1 CLEAR → 結果。
    /// 前の「RUN（3連戦の縦切り版）」とは別物（RunManager は残す）。切替：タイトルの「見本ステージ」「検証ステージ」で今までの遊び方へ。
    /// </summary>
    public partial class ActRunManager : MonoBehaviour
    {
        [SerializeField] TurnManager turn;
        [Tooltip("報酬・ショップに出るカード（アセット名で探す）")]
        [SerializeField] List<CommandData> cards = new List<CommandData>();
        [Tooltip("部屋の戦闘に使うステージ（アセット名で探す）")]
        [SerializeField] List<StageData> stages = new List<StageData>();
        [SerializeField] VisualTheme ruinsTheme;
        [SerializeField] VisualTheme crystalTheme;
        [Tooltip("起動したらタイトル（ACT1）を出す")]
        [SerializeField] bool openTitleOnStart = true;

        public ActRun Run { get; private set; }
        public ActScreen Screen { get; private set; } = ActScreen.None;
        public bool InAct => Screen != ActScreen.None;
        public bool InBattle => Screen == ActScreen.Battle;
        /// <summary>【RC5・レビュー3】戦闘中のメニュー（設定・遊び方・中断してタイトルへ）が開いている。開いている間、戦闘の画面は入力を受けない。</summary>
        public bool BattleMenuOpen;

        /// <summary>【RC5・レビュー3】戦闘の途中でタイトルへ（中断）。セーブは部屋の最初から（HP は今と入ったときの低い方：Checkpoint と同じ）。チュートリアルなら やめる。</summary>
        public void SuspendToTitle()
        {
            BattleMenuOpen = false;
            if (InTutorial) { SkipTutorial(); return; }
            if (InBattle && Run != null && turn != null)
            {
                // 【通常リリース版・QA】勝ったあと（勝利の窓）に中断すると、部屋の最初からで上書きされ、勝った戦闘をもう一度戦えた（報酬の取り直しも）
                //   → 勝ちを記録して（報酬の画面で保存済み）タイトルへ。続きからは報酬の画面から。
                if (turn.State == BattleState.Cleared) { if (!winHandled && (Run.Current?.Kind == RoomKind.Boss || Run.CombatRun)) RecordWin(); OpenTitle(); return; }
                // 【通常リリース版・QA】負けた（HP 0 の演出の間も）・ターンの実行中は中断しない（負けが消えた・タイトルの裏で戦闘が進んだ）
                if (!CanSuspendNow) return;
                Checkpoint();
            }
            OpenTitle();
        }
        /// <summary>【通常リリース版・QA】今「中断してタイトルへ」ができるか（命令を選んでいる間と、勝ったあとだけ。実行中・HP 0 の演出の間はできない）。</summary>
        public bool CanSuspendNow => InTutorial || !InBattle || turn == null || turn.State == BattleState.Cleared || (turn.State == BattleState.PlayerInput && (turn.Player == null || turn.Player.Hp > 0));
        public PendingItemUse PendingItem { get; private set; }
        public string Message { get => message; private set { message = value; MessageSerial++; } }
        string message;
        /// <summary>Message を入れるたびに増える（同じ文でも出し直せるように）。</summary>
        public int MessageSerial { get; private set; }
        public BattleStats LastStats { get; private set; }
        public TurnManager Turn => turn;
        int battleStartHp;
        /// <summary>【通常リリース版・QA】この戦闘に入ったときの HP（負けの窓。Run.Hp は負けた次のフレームで 0 になる）。</summary>
        public int BattleStartHp => battleStartHp;
        StageData battleStage;

        void Start()
        {
            // 自動撮影のときは、撮影の指定（-captureact）が無ければタイトルを出さない
            var args = System.Environment.GetCommandLineArgs();
            bool capture = !BuildInfo.PublicBuild && System.Array.IndexOf(args, "-autocapture") >= 0; // 【通常リリース版・QA】公開版は -autocapture でもタイトルから
            if (openTitleOnStart && !capture) { if (BuildInfo.Festival) OpenFestival(); else OpenTitle(); } // 【文化祭】文化祭版は入口（難易度を選ぶ）から
        }

        public CommandData Card(string asset) => cards.FirstOrDefault(c => c != null && c.name == asset);
        StageData Stage(string asset) => stages.FirstOrDefault(s => s != null && s.name == asset);

        public void OpenTitle()
        {
            Screen = ActScreen.Title; Run = null; Message = null; PendingItem = PendingItemUse.None; InTutorial = false; QuickMode = false; FestivalMode = false;
            if (festivalFreshMainPending) { festivalFreshMainPending = false; FestivalResetMainGame(); } // 【文化祭版だけ】本編のランが終わってタイトルへ戻ったら、次の人は はじめての状態から
        }

        /// <summary>【文化祭版だけ】本編のランが終わった（次にタイトルを開いたら、本編を はじめての状態に戻す）。</summary>
        bool festivalFreshMainPending;

        /// <summary>
        /// 【文化祭版だけ・2026-11-14 本人「リセットしたら本編も最初からに。1人クリアしたら、次の人が最初からできないのはかわいそう」】
        /// 展示の PC：本編を はじめての状態に戻す（ラン・クリアの記録・解放・ヒント・チュートリアルの既読・中断セーブを消す。音量・画面の揺れの設定は残す）。
        /// 呼ぶのは「次の人へ」・操作が無くて入口へ・本編へ進むとき（中断セーブが無いとき）・本編のランが終わってタイトルへ・スタッフ（F9）。通常版（Unity・公開版）では呼ばない。
        /// </summary>
        public void FestivalResetMainGame()
        {
            if (!BuildInfo.Festival) return;
            if (SaveEnabled && ActSave.Exists()) ActSave.Delete();
            Profile = FirstLaunchProfile(Profile);
            SaveProfile();
        }

        /// <summary>はじめて起動した状態のプロフィール（設定＝音量・画面の揺れだけ前のものを残す）。</summary>
        public static ActProfileData FirstLaunchProfile(ActProfileData old) => new ActProfileData
        {
            volMaster = old?.volMaster ?? 50, volBgm = old?.volBgm ?? 35, volSe = old?.volSe ?? 50, volSet = old?.volSet ?? false, soundOff = old?.soundOff ?? false, shakeOff = old?.shakeOff ?? false,
        };

        // ───── 【RC3・2026-10-21 本人の初プレイ】チュートリアル（RC4 で 8つ・5〜8分）。ランとは別（Run は無い・記録もセーブもしない） ─────

        public bool InTutorial { get; private set; }
        /// <summary>【RC9・本人「本編で答えまで出すのは親切すぎる」】結果の予告（ずれた後の攻撃・倒すと消える・受けるダメージ・無傷）を出すか：チュートリアルだけ（5分体験は初めの段階だけ）。本編は出さない。</summary>
        public bool AnswerPreviewOn => InTutorial && !FestivalMode && (!QuickMode || TutorialStep < TutorialContent.QuickPreviewSteps); // 【文化祭クエスト v2】文化祭は本編と同じ（答えの予告なし）
        public int TutorialStep { get; private set; }
        /// <summary>【RC9】5分体験（チュートリアルの仕組みで、核だけを5つ）。</summary>
        public bool QuickMode { get; private set; }
        /// <summary>今の段階の一覧（チュートリアル／5分体験）。</summary>
        public TutorialStepDef[] TutorialSteps => FestivalMode ? FestivalSteps : QuickMode ? TutorialContent.QuickSteps : TutorialContent.Steps; // 【文化祭】
        /// <summary>この段階の目標を達成した（「できた！」の画面）。</summary>
        public bool TutorialStepDone { get; private set; }
        /// <summary>うまくいかなかったときの一言（もう一度）。</summary>
        public string TutorialFeedback { get; private set; }
        int tutorialEventsSeen, tutorialTurnSeen;

        /// <summary>チュートリアルを始める（タイトルの「チュートリアル」・はじめての案内）。</summary>
        public void StartTutorial()
        {
            Run = null;
            Message = null;
            PendingItem = PendingItemUse.None;
            InTutorial = true;
            QuickMode = false;
            TutorialStep = 0;
            TutorialRetries = 0; HintLevel = 0; AnswerShown = false; MissionOpen = false; QuickIntroOpen = false; stepStartedAt = Time.unscaledTime; // 【通常リリース版・QA】5分体験のあとに入ると、前のやり直しの回数・窓が残っていた
            LoadTutorialStep();
        }

        /// <summary>【RC9】5分体験を始める（タイトルの「5分体験」）。ランとは別・記録もセーブもしない。</summary>
        public void StartQuick()
        {
            Run = null;
            Message = null;
            PendingItem = PendingItemUse.None;
            InTutorial = true;
            QuickMode = true;
            TutorialStep = 0;
            TutorialRetries = 0;
            HintLevel = 0; AnswerShown = false; stepStartedAt = Time.unscaledTime; HintsUsedTotal = 0; AnswersUsedTotal = 0; quickStartedAt = Time.unscaledTime; // 【RC12】
            LoadTutorialStep();
            MissionOpen = !string.IsNullOrEmpty(TutorialSteps[0].Mission);
            QuickIntroOpen = true; // 【RC12】MISSION 1 の前に「何のゲームか」を絵で見せる
        }

        /// <summary>【RC12・本人の RC11 実プレイ「いきなり MISSION・全まね…初めての人には何のゲームか分からない」】5分体験の入口の窓（絵で：あなたの斬撃 → 敵もまねる → 敵どうしで斬り合う）。</summary>
        public bool QuickIntroOpen { get; private set; }
        public void CloseQuickIntro() { QuickIntroOpen = false; }
        /// <summary>【RC12】5分体験を始めた時刻（記録の「通算」：本当に5分で終わるかを人のテストで見る）。</summary>
        float quickStartedAt;

        /// <summary>【RC11】5分体験：問題の前の MISSION の窓が開いている（閉じるまで盤面は触れない）。</summary>
        public bool MissionOpen { get; private set; }
        /// <summary>【RC11】MISSION の窓の「開始」。</summary>
        public void StartMission() { MissionOpen = false; stepStartedAt = Time.unscaledTime; }
        /// <summary>【RC11】この問題のやり直しの回数（2回でヒント）。</summary>
        public int TutorialRetries { get; private set; }

        void LoadTutorialStep()
        {
            var def = TutorialSteps[TutorialStep];
            if (def.Goal == TutorialGoal.RunLoop) { OpenTutorialRunLoop(); return; } // 【RC4】戦闘をしない最後の段階
            var src = Stage(def.Asset);
            if (src == null) { Message = "チュートリアルの部屋が見つからない（run-setup が必要）"; InTutorial = false; Screen = ActScreen.Title; return; }
            if (battleStage != null) { if (Application.isPlaying) Destroy(battleStage); else DestroyImmediate(battleStage); }
            var stage = Instantiate(src);
            stage.name = src.name;
            if (QuickMode) stage.stageTitle = $"5分体験 {TutorialStep + 1}/{TutorialSteps.Length}　{def.Name}"; // 【RC9】
            if (FestivalMode) stage.stageTitle = $"文化祭 {FestivalDef.Label}　{FestivalDef.Quest}"; // 【文化祭クエスト v2】
            if (stage.visualTheme == null) stage.visualTheme = ruinsTheme;
            battleStage = stage;
            var deck = (src.handTestDeck != null && src.handTestDeck.Count > 0 ? src.handTestDeck : new List<CommandData> { Card("Walk"), Card("Slash") }).Where(c => c != null).ToList();
            var setup = new ActBattleSetup
            {
                Hp = FestivalMode ? FestivalDef.Hp : 20, MaxHp = FestivalMode ? FestivalDef.Hp : 20, Deck = deck, BaseHand = 7, Rng = new System.Random(TutorialStep + 1), Seed = TutorialStep + 1, // 【文化祭クエスト v2】HP は LEVEL ごと
                TidyMax = 0, NoClocks = src.noClocks,
                TacticCards = def.Tactics.Select(Card).Where(c => c != null).ToList(),
            };
            winHandled = true; lossHandled = true; // ランの勝ち負けの記録はしない
            turn.LoadActBattle(stage, setup);
            if (def.Goal == TutorialGoal.FixedKill) // 【RC9】砲兵の照準（固定の赤）を、はじめからあなたのマスの十字に
                foreach (var e in turn.Grid.Model.Enemies) if (e.IsAlive && e.Kind == EnemyKind.Bomber) e.BombCells = PhaseRules.BombShape(turn.Grid.Model, turn.Grid.Model.Player.Position);
            if (def.Goal == TutorialGoal.FriendlyRed || def.Goal == TutorialGoal.HazardHit || def.Goal == TutorialGoal.FixedKill) turn.Grid.Model.NoPositioning = true; // 【RC9】覚える物を1つに（敵が自分で動かない）
            if (def.Goal == TutorialGoal.BigAttack) { ForceTutorialClock(); turn.Grid.Model.NoPositioning = true; } // 【RC4・レビュー】大技の段階で剣兵が「接近」していた（覚える物は大技だけ）
            TutorialStepDone = false;
            tutorialEventsSeen = 0;
            tutorialTurnSeen = turn.TurnNumber;
            Screen = ActScreen.Battle;
        }

        /// <summary>大技の段階：剣兵の八方斬りを始めから①（赤紫が見えている）にする。</summary>
        void ForceTutorialClock()
        {
            var m = turn.Grid.Model;
            foreach (var e in m.Enemies)
            {
                if (!e.IsAlive || e.Clocks.Count == 0) continue;
                var c = e.Clocks[0];
                c.Cd = 1;
                Clocks.Telegraph(m, e, c);
            }
        }

        /// <summary>毎フレーム：目標を見る（ターンの実行中は見ない）。</summary>
        void UpdateTutorial()
        {
            if (!InTutorial || Screen != ActScreen.Battle || turn == null || TutorialStepDone || MissionOpen) return;
            if (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) return;
            var ev = turn.BattleEvents;
            var fresh = ev.Skip(tutorialEventsSeen).ToList();
            tutorialEventsSeen = ev.Count;
            bool turnEnded = turn.TurnNumber != tutorialTurnSeen || turn.State != BattleState.PlayerInput;
            FestivalCount(fresh, turnEnded && fresh.Count > 0); // 【文化祭】被ダメ・敵にやらせた攻撃・ターンの終わりの演出
            if (turn.State == BattleState.GameOver && FestivalMode) { FestivalGameOver(); return; } // 【文化祭クエスト v2】倒れた窓（もう一度挑戦／別の LEVEL へ／次の人へ）
            if (turn.State == BattleState.GameOver) { RetryTutorialStep(FestivalMode ? "倒れた。もう一度（HP は全回復）" : "倒れた。もう一度"); return; }
            bool done = false;
            switch (TutorialSteps[TutorialStep].Goal)
            {
                case TutorialGoal.FixedKill: // 【RC9】固定の赤は動かない → 撃つ敵を倒すと消える
                    if (turnEnded)
                    {
                        bool hurt = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0);
                        bool alive = turn.Grid.Model.Enemies.Any(e => e.IsAlive && !e.IsObject);
                        if (!hurt && !alive) done = true;
                        else if (alive) { RetryTutorialStep(hurt ? "紫（固定）は、敵を引き寄せても動かなかった。引き寄せ ↑ → 斬撃 ↑ で、撃つ砲兵を倒そう" : "砲兵がまだ残っている：引き寄せ ↑ → 斬撃 ↑"); return; }
                        else done = true;
                    }
                    break;
                case TutorialGoal.FriendlyRed: // 【RC9】押した敵の赤（点線）が、別の敵に当たる
                    done = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && e.Cause.Kind == DamageKind.Trait && !e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit);
                    if (!done && turnEnded && turn.State != BattleState.GameOver) { RetryTutorialStep("まだ当たっていない：剣兵Aを → へ押すと、A の赤も → へずれる（B は A の右）"); return; }
                    break;
                case TutorialGoal.HazardHit: // 【RC9】まねで敵を床の仕掛けへ
                    done = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && (e.Cause.Kind == DamageKind.Fire || e.Cause.Kind == DamageKind.Spike));
                    if (!done && turnEnded && turn.State == BattleState.PlayerInput) TutorialFeedback = "まだ当たっていない：兵はあなたと同じ向きに動く。影のマスは兵のどちら？";
                    break;
                case TutorialGoal.Clear: done = turn.State == BattleState.Cleared; break;
                case TutorialGoal.Targets: // 【RC11】5分体験のパズル：兵を全部倒す（ターン数・無傷）
                {
                    var td = TutorialSteps[TutorialStep];
                    bool hurt = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0);
                    if (td.NoDamage && hurt && turnEnded) { RetryTutorialStep("ダメージを受けた：無傷で倒せる手がある。もう一度"); return; }
                    if (turn.State == BattleState.Cleared) { done = true; TutorialSolveTurns = turn.TurnNumber; TutorialSolveHurt = turn.Player != null && turn.Player.Hp < turn.Player.MaxHp; break; }
                    if (turnEnded && turn.State == BattleState.PlayerInput && td.TurnLimit > 0 && turn.TurnNumber > td.TurnLimit) { RetryTutorialStep($"{td.TurnLimit}ターンでは倒しきれなかった。もう一度"); return; }
                    break;
                }
                case TutorialGoal.CopyHit:
                    done = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer);
                    if (!done && turnEnded && turn.State == BattleState.PlayerInput) TutorialFeedback = "まだ当たっていない：斬撃を横向き（← か →）に。敵どうしが隣り合っている向き";
                    if (!done && turn.State == BattleState.Cleared) { RetryTutorialStep("あなたが倒してしまった：誰もいない方へ斬ってみよう"); return; }
                    if (!done && turn.State == BattleState.PlayerInput && turn.Grid.Model.Enemies.Count(e => e.IsAlive) < 2) { RetryTutorialStep("敵が1体だけになった（敵どうしで斬り合えない）：もう一度。誰もいない方へ斬ってみよう"); return; }
                    // 【RC4・レビュー】歩いて敵どうしが離れると、横に斬っても当たらず先へ進めなかった → 自動でやり直す
                    if (!done && turnEnded && turn.State == BattleState.PlayerInput)
                    {
                        var al = turn.Grid.Model.Enemies.Where(e => e.IsAlive).ToList();
                        if (al.Count == 2 && Mathf.Abs(al[0].Position.x - al[1].Position.x) + Mathf.Abs(al[0].Position.y - al[1].Position.y) != 1) { RetryTutorialStep("敵どうしが離れた（まねで動いた）：もう一度。歩かずに、横へ斬ってみよう"); return; }
                    }
                    break;
                case TutorialGoal.AvoidRed:
                    if (turnEnded)
                    {
                        var hurts = fresh.Where(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0).ToList();
                        // 【RC3・レビュー】まねた斬撃（あなたの攻撃のまね）で当たったのに「赤に残っていた」と言っていた
                        if (hurts.Any(e => e.Cause != null && !e.Cause.ByCopy)) TutorialFeedback = "剣兵の赤に残っていた：「押し出し」を剣兵の方（↑）へ。札を選んで ↑ にマウスを乗せると、ずれた後が見える"; // 【RC8】
                        else if (hurts.Count > 0) TutorialFeedback = "赤はずらせた！ でも、あなたの命令を剣兵がまねて、あなたに当たった（あなたの攻撃も敵がまねる）。今度は「押し出し」だけで";
                        else done = true;
                    }
                    break;
                case TutorialGoal.OwnMove: // 【RC4】追跡兵がオレンジの矢印どおり隣に来る → 先に斬る
                    done = turn.State == BattleState.Cleared;
                    if (!done && turnEnded && turn.State == BattleState.PlayerInput)
                    {
                        var m = turn.Grid.Model;
                        var chaser = m.Enemies.FirstOrDefault(e => e.IsAlive);
                        bool hurt = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0);
                        if (chaser != null && Mathf.Abs(chaser.Position.x - m.Player.Position.x) + Mathf.Abs(chaser.Position.y - m.Player.Position.y) == 1)
                            TutorialFeedback = hurt ? "噛まれた：隣に来たら、そのターンのうちに先に斬ろう（あなたの命令が先）"
                                                    : "オレンジの矢印どおり、追跡兵が隣に来た。周りの赤＝噛みつき。先に「斬撃」で倒そう";
                        else if (chaser != null) TutorialFeedback = "追跡兵はオレンジの矢印の先へ動いた。次の矢印も見てみよう";
                    }
                    break;
                case TutorialGoal.Tactic:
                    if (turn.State == BattleState.Cleared)
                    {
                        if (turn.TacticsUsedThisBattle > 0) done = true;
                        else { RetryTutorialStep("緑の「集中」を使ってから斬ってみよう"); return; }
                    }
                    break;
                case TutorialGoal.BigAttack:
                    done = fresh.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && e.Cause.Kind == DamageKind.Clock);
                    if (!done && fresh.Any(e => e.Type == BattleEventType.ClockFired)) { RetryTutorialStep("大技が敵に当たらなかった：あなたが → に動くと、左の敵も → に動く"); return; }
                    break;
            }
            if (turnEnded) tutorialTurnSeen = turn.TurnNumber;
            if (done)
            {
                TutorialStepDone = true;
                TutorialFeedback = null;
                FestivalBattleCleared(); // 【文化祭】この戦いのターンと秒
                turn.PauseForTutorial();
                Sfx.Play(Sfx.Kind.Clear);
            }
        }

        /// <summary>【RC4・レビュー】この段階をはじめから（チュートリアルの欄の「やり直す」）。</summary>
        public void RestartTutorialStep()
        {
            if (!InTutorial || TutorialSteps[TutorialStep].Goal == TutorialGoal.RunLoop) return;
            if (FestivalMode) { FestivalRestart(); return; } // 【文化祭クエスト v2】最初からやり直す（全部はじめに戻す）
            RetryTutorialStep(null);
        }

        void RetryTutorialStep(string why)
        {
            LoadTutorialStep();
            if (why != null) TutorialRetries++;
            var hint = TutorialSteps[TutorialStep].Hint;
            TutorialFeedback = why != null && TutorialRetries >= 2 && !string.IsNullOrEmpty(hint) ? why + "　ヒント：" + hint : why; // 【RC11】2回失敗したらヒント（答えではなく見る所）
        }

        /// <summary>【RC11・5分体験の初見テスト（ゲームをしない人）「答えが出ない問題にヒントのボタンが無い」】いつでもヒント（答えではなく、見る所）を出す。</summary>
        public bool HasTutorialHint => InTutorial && QuickMode && HintCount > 0;
        /// <summary>【RC12】段階ヒント（1 仕組み → 2 使う敵 → 3 最初の1手）。今いくつ見たか（同じ問題をやり直しても残る）。</summary>
        public int HintLevel { get; private set; }
        /// <summary>【RC12】ヒントを出した時刻（右下の欄を2秒光らせる）。</summary>
        public float HintShownAt { get; private set; } = -99f;
        /// <summary>【RC12】答えを見た（光る札を順に選べば解ける。問題は続く）。</summary>
        public bool AnswerShown { get; private set; }
        float stepStartedAt;
        static string[] HintsOf(TutorialStepDef d) => d.Hints != null && d.Hints.Length > 0 ? d.Hints
            : string.IsNullOrEmpty(d.Hint) ? new string[0] : string.IsNullOrEmpty(d.Hint2) ? new[] { d.Hint } : new[] { d.Hint, d.Hint2 };
        public int HintCount => InTutorial && QuickMode && TutorialStep < TutorialSteps.Length ? HintsOf(TutorialSteps[TutorialStep]).Length + (FestivalLateHintReady ? 1 : 0) : 0; // 【文化祭クエスト v2】LEVEL 3 は長く止まると強いヒントが1つ増える
        public void ShowTutorialHint()
        {
            if (!HasTutorialHint) return;
            var hs = HintsOf(TutorialSteps[TutorialStep]);
            if (FestivalLateHintReady) hs = hs.Concat(new[] { FestivalDef.LateHint }).ToArray(); // 【文化祭クエスト v2】
            if (HintLevel < hs.Length) HintLevel++;
            TutorialFeedback = string.Join("\n", System.Linq.Enumerable.Range(0, HintLevel).Select(k => $"ヒント {k + 1}：{hs[k]}")); // 【RC12・初見テスト3回目「2つ目を押すと1つ目が消える」】見たヒントを全部
            HintsUsedTotal++;
            FestivalNoteHint(); // 【文化祭】
            HintShownAt = Time.unscaledTime; // 【RC12・初見テスト4回目「ヒントがボタンから遠い右の欄に出る」】右の欄を少し光らせる
        }
        /// <summary>【RC12】「答えを見る」が出せるか：ヒントを全部見た・2回やり直した・2分半たった（ずっと詰まらない）。</summary>
        public bool CanShowAnswer => InTutorial && QuickMode && !AnswerShown && !TutorialStepDone && !string.IsNullOrEmpty(TutorialSteps[TutorialStep].Answer)
            && (HintLevel >= HintCount || TutorialRetries >= 2 || Time.unscaledTime - stepStartedAt > 150f);
        /// <summary>【RC12】答えを見る：はじめからにして、光る札を順に選べば解ける（答えを見ても、自分で操作して成功させる）。</summary>
        public void ShowAnswer()
        {
            if (!InTutorial || string.IsNullOrEmpty(TutorialSteps[TutorialStep].Answer)) return;
            AnswerShown = true;
            AnswersUsedTotal++;
            RetryTutorialStep(null);
            TutorialFeedback = "答え：" + AnswerText();
        }
        /// <summary>【RC12】答えの手順（ターンごと）。"Push:R,Walk:D" → [[("Push",Right),("Walk",Down)]]。</summary>
        public List<List<(string card, Direction dir)>> AnswerSteps()
        {
            var list = new List<List<(string, Direction)>>();
            var a = InTutorial && TutorialStep < TutorialSteps.Length ? TutorialSteps[TutorialStep].Answer : null;
            if (string.IsNullOrEmpty(a)) return list;
            foreach (var t in a.Split('|'))
            {
                var turnSteps = new List<(string, Direction)>();
                foreach (var part in t.Split(','))
                {
                    var kv = part.Trim().Split(':');
                    if (kv.Length < 1 || kv[0].Length == 0) continue;
                    char d = kv.Length > 1 && kv[1].Length > 0 ? kv[1][0] : 'U';
                    turnSteps.Add((kv[0], d == 'D' ? Direction.Down : d == 'L' ? Direction.Left : d == 'R' ? Direction.Right : Direction.Up));
                }
                list.Add(turnSteps);
            }
            return list;
        }
        public static string DirGlyph(Direction d) => d == Direction.Up ? "↑" : d == Direction.Down ? "↓" : d == Direction.Left ? "←" : "→";
        /// <summary>札の名前（Push）→ 画面の名前（押し出し）。手札から引く。</summary>
        public string CardDisplayName(string asset)
        {
            var c = turn != null && turn.Deck != null ? turn.Deck.Hand.FirstOrDefault(x => x.Data != null && x.Data.name == asset) : null;
            return c != null ? c.Data.displayName : asset;
        }
        string AnswerText()
        {
            var steps = AnswerSteps();
            var sb = new System.Text.StringBuilder();
            for (int t = 0; t < steps.Count; t++)
            {
                if (steps.Count > 1) sb.Append(t == 0 ? "1ターン目 " : "　2ターン目 ");
                for (int i = 0; i < steps[t].Count; i++) sb.Append($"{(i > 0 ? " → " : "")}「{CardDisplayName(steps[t][i].card)}」{DirGlyph(steps[t][i].dir)}"); // 【RC12・見直し】①②③は上の順番の帯（①あなた ③まね）と食い違う → 「→」でつなぐ
                sb.Append("　");
            }
            return sb.ToString().TrimEnd() + "（光っている札を順に選んで、同じ向きへ）";
        }
        /// <summary>【RC12】答えの「次の1枚」（今のターン・組んだ数から）。無ければ null。</summary>
        public (string card, Direction dir)? NextAnswerStep()
        {
            if (!AnswerShown || turn == null) return null;
            var steps = AnswerSteps();
            int t = Mathf.Clamp(turn.TurnNumber - 1, 0, 99);
            if (t >= steps.Count) return null;
            int k = turn.PlannedCommands.Count;
            if (k >= steps[t].Count) return null;
            return steps[t][k];
        }
        /// <summary>【RC12】解いたときのターン数・被ダメ（「★ きれいに解けた」の判定）。</summary>
        public int TutorialSolveTurns { get; private set; }
        public bool TutorialSolveHurt { get; private set; }
        /// <summary>【RC12】5分体験で見たヒント・答えの数（記録用。画面では責めない）。</summary>
        public int HintsUsedTotal { get; private set; }
        public int AnswersUsedTotal { get; private set; }

        /// <summary>「次へ」：次の段階・最後なら完了の画面。</summary>
        public void NextTutorialStep()
        {
            if (!InTutorial) return;
            var goal = TutorialSteps[TutorialStep].Goal;
            if (goal == TutorialGoal.RunLoop) { TutorialRunNext(); return; }
            // 【RC4】チュートリアルで緑の札を使っても、本編ではじめて持った戦闘で「おさらい」を1回出す（前は出さなかった）
            if (TutorialStep + 1 >= TutorialSteps.Length) { FinishTutorial(); return; }
            TutorialStep++;
            TutorialFeedback = null;
            TutorialRetries = 0;
            HintLevel = 0; AnswerShown = false; // 【RC12】
            LoadTutorialStep(); // ここで前の戦いの記録が書かれる（PlaytestRecorder.Extra が前の秒を読む）
            stepStartedAt = Time.unscaledTime; // 【文化祭・RC12 の記録の直し】前は読み込みの前に戻していて「この問題 0秒」になっていた
            MissionOpen = QuickMode && !string.IsNullOrEmpty(TutorialSteps[TutorialStep].Mission);
        }

        // ───── 【RC4】チュートリアルの最後「1ランの流れ」：報酬を選ぶ → 扉を選ぶ → 8階の地図（触って覚える・本編には何も残らない） ─────

        /// <summary>0＝報酬・1＝扉・2＝8階の地図。</summary>
        public int TutorialRunPage { get; private set; }
        /// <summary>報酬の見本で選んだ札（-1＝まだ）。</summary>
        public int TutorialRunCard { get; private set; } = -1;
        /// <summary>扉の見本で選んだ扉（-1＝まだ）。</summary>
        public int TutorialRunDoor { get; private set; } = -1;

        void OpenTutorialRunLoop()
        {
            TutorialRunPage = 0;
            TutorialRunCard = -1;
            TutorialRunDoor = -1;
            TutorialStepDone = false;
            Screen = ActScreen.TutorialRun;
        }

        public void TutorialRunPickCard(int i) { if (Screen == ActScreen.TutorialRun && TutorialRunPage == 0) TutorialRunCard = i; }
        public void TutorialRunPickDoor(int i) { if (Screen == ActScreen.TutorialRun && TutorialRunPage == 1) TutorialRunDoor = i; }

        /// <summary>次の頁。地図の頁の次は完了の画面。</summary>
        public void TutorialRunNext()
        {
            if (!InTutorial || Screen != ActScreen.TutorialRun) return;
            if (TutorialRunPage < 2) { TutorialRunPage++; return; }
            FinishTutorial();
        }

        void FinishTutorial()
        {
            InTutorial = false;
            if (FestivalMode) { FinishFestival(); return; } // 【文化祭】結果（ランク・本日のベスト）
            if (QuickMode) { Screen = ActScreen.TutorialDone; return; } // 【RC9】5分体験の終わり（QuickMode のまま：終わりの画面が5分体験の中身を出す）
            Profile.tutorialDone = true;
            Profile.tutorialOffered = true;
            SaveProfile();
            Screen = ActScreen.TutorialDone;
        }

        /// <summary>チュートリアルをやめる（途中でも）：タイトルへ。</summary>
        public void SkipTutorial()
        {
            if (FestivalMode) { QuitFestivalBattle(); return; } // 【文化祭】やめる＝文化祭の入口へ（本編のプロフィールには触らない）
            InTutorial = false;
            QuickMode = false;
            Profile.tutorialOffered = true;
            SaveProfile();
            OpenTitle();
        }

        /// <summary>はじめての案内で「すぐに始める」。</summary>
        public void DeclineTutorial()
        {
            Profile.tutorialOffered = true;
            SaveProfile();
        }

        /// <summary>完了の画面の「旅の剣士で始める」。</summary>
        public void StartFirstRunAfterTutorial()
        {
            OpenV4RunRoleSelect();
            if (SaveEnabled && ActSave.Exists()) { Message = "中断したランがあります：新しく始めると消えます（タイトルの「続きから」で再開もできます）"; return; } // 【RC4・レビュー】前は黙って消していた
            var role = ActContent.Roles.FirstOrDefault(r => r.Id == "standard");
            NextChallenge = 0; // 【通常リリース版・QA】チュートリアルのあとの「旅の剣士で1階から」は深度なし（前に選んだ深度が残っていた）
            if (role != null) StartRun(role);
        }
        public void OpenRoleSelect() { NextRunCopyCentric = false; NextRunPuzzle = false; NextRunCombat = false; NextRunV4 = false; Screen = ActScreen.Role; }
        /// <summary>【Combat V4】Combat V4 / External Playtest RC1 の役職選び（外部テスト版の「はじめる」）。</summary>
        public void OpenV4RunRoleSelect() { NextRunCopyCentric = true; NextRunPuzzle = false; NextRunCombat = true; NextRunV4 = true; Screen = ActScreen.Role; }
        /// <summary>次に始めるランが Combat V4 か。</summary>
        public bool NextRunV4 { get; private set; }
        /// <summary>【検証中・Copy Core V3】コピー中心のミニ ACT1 の役職選び。</summary>
        public void OpenCopyRunRoleSelect() { NextRunCopyCentric = true; NextRunPuzzle = false; NextRunCombat = false; NextRunV4 = false; Screen = ActScreen.Role; }
        /// <summary>【Puzzle v2】パズル中心 ACT1 v2 の役職選び。</summary>
        public void OpenPuzzleRunRoleSelect() { NextRunCopyCentric = true; NextRunPuzzle = true; NextRunCombat = false; NextRunV4 = false; Screen = ActScreen.Role; }
        /// <summary>【戦闘中心 v3】戦闘中心 ACT1 v3 の役職選び（外部テスト版はこれだけ）。</summary>
        public void OpenCombatRunRoleSelect() { NextRunCopyCentric = true; NextRunPuzzle = false; NextRunCombat = true; NextRunV4 = false; Screen = ActScreen.Role; }
        /// <summary>次に始めるランが戦闘中心 ACT1 v3 か。</summary>
        public bool NextRunCombat { get; private set; }
        /// <summary>次に始めるランがパズル中心 ACT1 v2 か。</summary>
        public bool NextRunPuzzle { get; private set; }
        /// <summary>次に始めるランがコピー中心のミニ ACT1 か（役職の画面から StartRun するとき）。</summary>
        public bool NextRunCopyCentric { get; private set; }

        /// <summary>【検証中・Copy Core V2】コピーの再設計のサンドボックス（一覧：ステージ・方式・COPY ON/OFF）。</summary>
        public void OpenCopyLab() { Screen = ActScreen.CopyLab; Run = null; Message = null; PendingItem = PendingItemUse.None; }

        /// <summary>【検証中・Copy Core V3】コピーの種類の研究室（部屋を1つずつ遊ぶ・COPY ON/OFF）。</summary>
        public void OpenCopyLabV3() { LabPuzzle = false; LabCombat = false; Screen = ActScreen.CopyLabV3; Run = null; Message = null; PendingItem = PendingItemUse.None; }
        /// <summary>【Puzzle v2】パズルの部屋を1つずつ遊ぶ研究室（COPY ON/OFF で比べる）。</summary>
        public void OpenPuzzleLab() { LabPuzzle = true; LabCombat = false; Screen = ActScreen.CopyLabV3; Run = null; Message = null; PendingItem = PendingItemUse.None; }
        /// <summary>【戦闘中心 v3】戦闘中心 v3 の部屋を1つずつ遊ぶ研究室（COPY ON/OFF で比べる）。</summary>
        public void OpenCombatLab() { LabPuzzle = false; LabCombat = true; LabV4 = false; Screen = ActScreen.CopyLabV3; Run = null; Message = null; PendingItem = PendingItemUse.None; }
        /// <summary>【Combat V4】Combat V4 研究室（部屋を1つずつ遊ぶ。最初の戦術札つき）。</summary>
        public void OpenV4Lab() { LabPuzzle = false; LabCombat = true; LabV4 = true; Screen = ActScreen.CopyLabV3; Run = null; Message = null; PendingItem = PendingItemUse.None; }
        /// <summary>研究室が Combat V4 の部屋の一覧か。</summary>
        public bool LabV4 { get; private set; }

        /// <summary>【Combat V4】研究室の部屋を、最初のデッキ・最初の戦術札で始める（ランの外）。</summary>
        public void StartLabV4(int stageIndex)
        {
            Screen = ActScreen.None;
            Run = null;
            var stage = turn.Stages[stageIndex];
            var setup = new ActBattleSetup { Hp = V4Content.StartHp, MaxHp = V4Content.StartHp, BaseHand = 7, Seed = 12345, Rng = new System.Random(12345), TidyMax = 0 }; // 【就活資料 2026-11-18】研究室（開発用）も本編と同じ最大 HP 16・手札整理なし（前は 20・手札整理ありで、資料の撮影が本編と違った）
            // 【就活資料 2026-11-18・事実確認「撮影の雑魚の赤が 3（本編の1〜6階は 2）」】研究室でも、その部屋がふつう出る階の敵の強さ（赤・雑魚の大技・最後の1体）を本編と同じに
            ApplyLabPower(setup, stage.name);
            foreach (var n in ActContent.StartDeck) { var c = Card(n); if (c != null) setup.Deck.Add(c); }
            foreach (var n in V4Content.StartTactics) { var c = Card(n); if (c != null) setup.TacticCards.Add(c); }
            turn.LoadActBattle(stage, setup);
        }
        /// <summary>【就活資料】研究室・撮影の戦闘に、その部屋がふつう出る階の敵の強さを入れる（本編の部屋でなければ何もしない）。</summary>
        public static void ApplyLabPower(ActBattleSetup setup, string stageName)
        {
            var labRoom = V4Content.Rooms.FirstOrDefault(r => r.Stage == stageName);
            if (labRoom == null) return;
            var pw = V4Content.PowerFor(LabFloorFor(labRoom), labRoom);
            setup.RedDelta = pw.red; setup.ClockDelta = pw.clock; setup.LastStand = V4Content.LastStandBonus; setup.LastStandDouble = V4Content.LastStandDouble;
        }
        /// <summary>【就活資料】研究室の部屋がふつう出る階（0 始まり）：1階＝入門・2階＝通常・3階＝トゲの通路・5階＝後半と仕掛け・6階＝中ボス・8階＝ボス。</summary>
        static int LabFloorFor(RoomDef room)
        {
            if (room.Kind == RoomKind.Boss) return 7;
            if (room.IsMidBoss) return 5;
            switch (room.Id)
            {
                case "v4_i1": case "v4_n1": case "v4_n2": return 0;
                case "v4_n5": case "v4_n6": case "v4_n7": return 1;
                case "v4_t4": return 2;
                default: return 4;
            }
        }
        /// <summary>研究室がパズルの部屋の一覧か。</summary>
        public bool LabPuzzle { get; private set; }
        /// <summary>研究室が戦闘中心 v3 の部屋の一覧か。</summary>
        public bool LabCombat { get; private set; }

        /// <summary>Copy Core V3 の部屋（StageData.copyLab）と、その番号。</summary>
        public List<(int index, StageData stage)> LabStagesV3()
        {
            var list = new List<(int, StageData)>();
            var all = turn.Stages.ToList();
            for (int i = 0; i < all.Count; i++) if (all[i] != null && (LabV4 ? all[i].combatV4 : LabCombat ? all[i].combatRoom && !all[i].combatV4 : LabPuzzle ? all[i].puzzleRoom : all[i].copyLab)) list.Add((i, all[i]));
            return list;
        }

        /// <summary>サンドボックスのステージ（StageData.copySandbox）と、その番号（TurnManager のステージ一覧の中）。</summary>
        public List<(int index, StageData stage)> SandboxStages()
        {
            var list = new List<(int, StageData)>();
            var all = turn.Stages.ToList();
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].copySandbox) list.Add((i, all[i]));
            return list;
        }

        /// <summary>タイトルから抜けて、今までの遊び方（見本・検証ステージ）へ。</summary>
        public void ExitToStages(int stageIndex)
        {
            Screen = ActScreen.None;
            Run = null;
            turn.LoadStage(stageIndex);
        }

        public void StartRun(RoleDef role, int seed = 0) => StartRun(role, seed, NextRunCopyCentric, NextRunPuzzle, NextRunCombat, NextRunV4);

        public void StartRun(RoleDef role, int seed, bool copyCentric, bool puzzle = false, bool combat = false, bool v4 = false)
        {
            NextRunPuzzle = puzzle;
            NextRunCombat = combat || v4;
            NextRunV4 = v4;
            if (SaveEnabled && ActSave.Exists()) ActSave.Delete(); // 新しいランを始めたら前の中断セーブは消える（タイトルで注意を出す）
            NextRunCopyCentric = copyCentric;
            CopyCore.SandboxCopyOff = false; // レビュー：研究室の COPY OFF がランに残って、コピー中心 ACT1 が勝てなくなった
            CopyCore.SandboxMode = null;
            Run = new ActRun(role, seed != 0 ? seed : System.Environment.TickCount & 0x7fffffff, Card, copyCentric, puzzle, combat, v4);
            if (v4)
            {
                // 【完成版】解放済みの札・挑戦の段階
                Run.UnlockedCards.UnionWith(FinalMeta.UnlockedCards(Profile));
                Run.Challenge = Mathf.Clamp(NextChallenge, 0, FinalMeta.MaxChallenge(Profile));
                rolesOpenAtStart.Clear();
                foreach (var r in ActContent.Roles) if (FinalMeta.IsRoleUnlocked(Profile, r.Id)) rolesOpenAtStart.Add(r.Id);
                if (Profile.finalFinished == 0) Run.ForceFirstDoor("v4_i1"); // 【完成版】はじめてのランは、1階に「鏡の歩兵」（同士討ちの基本）
                Profile.finalRuns++;
                Profile.lastChallenge = Run.Challenge;
            }
            PendingBossRetry = false;
            FinalNews.Clear();
            BattleUI.FullPrediction = false; // 検証用の完全予測は ACT1 に持ち込まない（観測士は役職の効果で見える）
            lossHandled = false;
            runRecorded = false;
            NewlyUnlocked.Clear();
            FirstTimeCards.Clear();
            Run.Note($"# ACT1 プレイ記録{(v4 ? "（Combat V4 RC2）" : combat ? "（戦闘中心 ACT1 v3）" : puzzle ? "（パズル中心 v2）" : "")}　役職：{Run.Role.Name}　種：{Run.Seed}　版：{BuildInfo.Version}　開始：{System.DateTime.Now:yyyy-MM-dd HH:mm}");
            Message = null;
            Profile.runsStarted++;
            SaveProfile();
            // 【検証中】旅立ちの選択（3つから1つ・取らなくてもよい）→ 扉
            Run.RollBoons();
            if (v4 && Profile.finalRuns <= 1) Run.BoonOptions.Clear(); // 【RC2】はじめてのランは旅立ちの選択を出さない（1戦目の前に、アイテム・戦術札・ルーン片まで出て多すぎた：初見のレビュー）
            if (Run.BoonOptions.Count > 0) Screen = ActScreen.Boon;
            else EnterDoors();
        }

        /// <summary>旅立ちの選択を受け取る（削除は removeIndex の1枚）。</summary>
        public void ChooseBoon(BoonOption b, int removeIndex = -1)
        {
            if (Screen != ActScreen.Boon || Run == null) return;
            if (Run.TakeBoon(b, removeIndex)) EnterDoors();
        }

        public void SkipBoon()
        {
            if (Screen != ActScreen.Boon || Run == null) return;
            Run.SkipBoon();
            EnterDoors();
        }

        // ───── 【検証中】プロフィール（役職の解放・記録）。恒久的な強化は入れない ─────

        public ActProfileData Profile { get; private set; } = new ActProfileData();
        /// <summary>このランで新しく解放された役職の名前（結果画面）。</summary>
        public List<string> NewlyUnlocked { get; } = new List<string>();
        /// <summary>このランで初めて使ったカード（結果画面）。</summary>
        public List<string> FirstTimeCards { get; } = new List<string>();
        /// <summary>このランの前の、この役職の最高到達階（結果画面の「自己ベスト」）。</summary>
        public int PreviousBest { get; private set; }
        bool runRecorded;

        public bool IsRoleUnlocked(RoleDef role) => NextRunV4 || (Run != null && Run.V4Run) ? FinalMeta.IsRoleUnlocked(Profile, role.Id) : ActProfile.IsUnlocked(Profile, role.Id);

        // ───── 【完成版・2026-10-20】周回：挑戦の段階・このランで起きた解放・ボスの再挑戦 ─────
        /// <summary>次のランの挑戦の段階（役職の画面で選ぶ）。</summary>
        public int NextChallenge { get; set; }
        /// <summary>このランの終わりに起きたこと（初クリア・解放・自己ベスト）。結果画面に出す。</summary>
        public List<string> FinalNews { get; } = new List<string>();
        /// <summary>ボスに負けて、再挑戦するかを選んでいる（ランはまだ終わっていない）。</summary>
        public bool PendingBossRetry { get; private set; }
        int hpAfterBossLoss;
        /// <summary>【RC2】ランの始めに解放されていた役職（結果の画面で「このランで新しく解放された役職」を出すため）。</summary>
        readonly HashSet<string> rolesOpenAtStart = new HashSet<string>();
        /// <summary>【RC2】このランで新しく解放された役職（無ければ null）。</summary>
        public RoleDef NewRoleThisRun => Run != null && Run.V4Run && (Run.Cleared || Run.Failed)
            ? ActContent.Roles.FirstOrDefault(r => V4Content.RoleIds.Contains(r.Id) && !rolesOpenAtStart.Contains(r.Id) && FinalMeta.IsRoleUnlocked(Profile, r.Id)) : null;

        /// <summary>ボスの直前（入ったときの HP）からもう一度（1ランに1回）。</summary>
        public void RetryBossNow()
        {
            if (Run == null || !PendingBossRetry || !Run.BossRetryUsed || Run.Failed) return; // 再挑戦は負けた瞬間に使用済み（RecordLoss）
            PendingBossRetry = false;
            lossHandled = false;
            Message = $"{Run.Current?.Name}に再挑戦（HP {Run.Hp}・やり直しは中ボスとボスで1ランに1回）"; // 【RC7】
            StartBattle(Run.Current);
        }

        /// <summary>再挑戦しない：ランを終える（記録・セーブの削除）。</summary>
        public void GiveUpAfterBossLoss()
        {
            if (!PendingBossRetry) return;
            PendingBossRetry = false;
            Run.GiveUpBossRetry(hpAfterBossLoss);
            FinishRun();
        }

        /// <summary>検証用：全部の役職を解放（タイトル・役職の画面の小さなボタン）。</summary>
        public void UnlockAllRolesForTesting()
        {
            Profile.allRolesUnlocked = true;
            SaveProfile();
        }

        public void SaveProfile() { if (SaveEnabled) ActProfile.Save(Profile); }

        /// <summary>【RC4・開発版だけ】チュートリアル・はじめての説明を「まだ見ていない」に戻す（記録・解放は残す）。</summary>
        public void DevResetTutorialSeen()
        {
            Profile.tutorialOffered = false;
            Profile.tutorialDone = false;
            Profile.tacticIntroSeen = false;
            Profile.hintsSeen.Clear();
            SaveProfile();
            Message = "【開発】チュートリアルを未閲覧にした（はじめての案内が出る）";
        }

        /// <summary>
        /// 【RC4・開発版だけ】はじめて起動した状態に戻す（ラン・クリアの記録・解放・ヒント・中断セーブを消す。音量は残す）。
        /// 今のプロフィールと中断セーブは「.backup」として同じ場所に残す（戻したいときは名前を戻す）。
        /// </summary>
        public void DevResetToFirstLaunch()
        {
            if (SaveEnabled)
            {
                try
                {
                    string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss"); // 【RC4・レビュー】2回押すと空のプロフィールで .backup を上書きしていた
                    foreach (var path in new[] { ActProfile.DefaultPath, ActSave.DefaultPath })
                        if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".backup_" + stamp, true);
                }
                catch (System.Exception ex) { Debug.LogWarning("【開発】バックアップに失敗：" + ex.Message); }
                ActSave.Delete();
            }
            var fresh = new ActProfileData
            {
                volMaster = Profile.volMaster, volBgm = Profile.volBgm, volSe = Profile.volSe, volSet = Profile.volSet, soundOff = Profile.soundOff,
            };
            Profile = fresh;
            SaveProfile();
            OpenTitle();
            Message = "【開発】はじめての状態に戻した（前の記録は act1_profile.json.backup_日時）";
        }

        // ───── 【検証中】ヒント（2026-10-05 本人の依頼：長いチュートリアルではなく、その場で1行）。一度閉じたら二度と出ない ─────

        /// <summary>今の画面で出すヒント（id と文）。無ければ null。</summary>
        public KeyValuePair<string, string>? CurrentHint()
        {
            if (Profile.hintsOff || Run == null) return null;
            var list = new List<KeyValuePair<string, string>>();
            void Add(string id, string text) => list.Add(new KeyValuePair<string, string>(id, text));
            switch (Screen)
            {
                case ActScreen.Boon: Add("D0", "旅立ちの選択：ランごとに候補が変わる。迷ったら「選ばずに進む」でもよい"); break;
                case ActScreen.Doors:
                    if (Run.Doors.Any(d => d.Kind == RoomKind.Danger) && Run.Hp * 2 <= Run.MaxHp) Add("D6", Run.V4Run ? $"HP {Run.Hp}/{Run.MaxHp}：HP は次の戦闘へ持ち越す。中ボスを避けて、もう1つの扉も選べる" : $"HP {Run.Hp}/{Run.MaxHp}：HP は次の戦闘へ持ち越す。危険戦を避けて、通常戦・特殊部屋も選べる");
                    if (Run.Doors.Any(d => d.Kind == RoomKind.Danger)) Add("D4", Run.V4Run ? "中ボス：報酬が多い（2枚まで選べる・中ボスの力の戦術札・刻印か無料の強化）。でも HP は持ち越す。もう1つの扉は安全寄り" : "危険戦：報酬が多い（カード2枚まで＋刻印）。でも HP は持ち越す。もう1つの扉は安全寄り");
                    if (Run.CombatRun && Run.Doors.Any(d => d.Kind == RoomKind.Trial)) Add("D8", "試練：短いパズル（決まったターン数以内）。時間切れ・あきらめは負けではない（報酬が無いだけ）。写し砲の射線はあなたにも当たる");
                    if (Run.CombatRun && Run.Doors.Any(d => d.Kind == RoomKind.Tactical)) Add("D9", "仕掛けの部屋：仕掛けが1つある戦闘。普通に倒してもいいが、敵のまねで仕掛けを使うと一気に楽になる");
                    Add("D1", "扉の先が少し見える（敵・仕掛け・難しさ。中ボスは報酬が多い）。上の帯で先の階も分かる");
                    break;
                case ActScreen.Reward:
                    if (Run.V4Run && Run.RewardImprints.Count > 0) Add("I1", "刻印＝ランの間ずっと効く小さな力（上の帯にマウスで説明）。どれも、敵どうし・仕掛けで戦うほど得をする"); // 【RC7】
                    if (!Run.V4Run) Add("D2", "金の札＝この戦闘でしたことから出たカード。取らなくてもいい（デッキが薄いと欲しい札を引きやすい）");
                    else Add("F1", "毎戦、命令札3枚から1枚。札の上の「まね：…」を見て選ぶ（強い攻撃ほど敵も使う）。デッキは育つ：減らすのは商人・休息の「削除」"); // 【RC6】入れ替えは外した // 【完成版】【RC5・最終検証】戦術札が出ない報酬（はじめてのランの1・2階）では戦術札の話をしない
                    break;
                case ActScreen.Shop:
                    // ボット解析：ボスの扉で平均 60〜70 のルーン片が余っていた（ボスの後は使えない）
                    if (Run.Floor == Run.FloorCount - 2 && Run.Shards >= 25) Add("D7", Run.V4Run ? $"次はボス。ルーン片 {Run.Shards} はボスの後には使えない。回復薬・札・削除に使おう" : $"次はボス。ルーン片 {Run.Shards} はボスの後には使えない。回復薬・削除・刻印に");
                    Add("D3", Run.V4Run ? $"商人：札・アイテム・刻印・強化（{Run.UpgradePrice}）・削除（{Run.RemovePrice}）。刻印＝ランの間ずっと効く小さな力。売ることはできない" : "購入＝ルーン片で手に入れる／削除＝デッキから1枚なくす。カードを売ることはできない"); // 【RC7】
                    break;
                case ActScreen.Rest: Add("D5", Run.V4Run ? $"回復＝HP +{Run.RestHealAmount}／強化＝攻撃の札1種類をずっと強く（例：斬撃 4 → 6・選ぶ前に前→後が出る）／削除＝1枚なくす。2つ選べる" : $"休む＝HP +{ActContent.RestHeal}／祈る＝デッキのカードを1枚削除（無料）。どちらか1つ"); break;
                case ActScreen.Battle:
                    if (turn == null || turn.Player == null) break;
                    // 【RC4・レビュー：後ろだと大技・緑の札のヒントに押されて出なかった → 先頭】救済の移動：追い詰められたら（HP が半分以下で、敵の隣か赤の上）
                    if (turn.IsV4 && Run.Items.Contains(ItemId.WarpStone) && turn.Player.Hp * 2 <= turn.Player.MaxHp && turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count == 0 &&
                        ThreatMap.EnemyAttack(turn.Grid.Model).Contains(turn.Grid.Model.Player.Position))
                        Add("W1", "追い詰められたら：上の「転移石」で3マス以内の空いたマスへ移れる（命令を組む前に・敵はまねない・使うと無くなる）");
                    if (turn.Enemies.Any(e => e.Model.CopyTrait == CopyTrait.MirrorAttacks)) Add("B4", "騎士は攻撃だけ逆向きにまね（反射）。攻撃の向きを選ぶと、紫の点線＝反射される範囲");
                    if (turn.Enemies.Any(e => e.Model.CopyTrait == CopyTrait.Mirror)) Add("B3", "鏡兵は向きを逆にまねする：あなたが右へ斬れば、鏡兵は左へ斬る");
                    if (turn.IsV4)
                    {
                        // 【Combat V4】まね → 大技のカウント → ①の予告 → 逆利用 → 戦術札（出会った順に1回ずつ）
                        // 【RC2】最初は「まね」から（初見のレビュー：1戦目の最初の説明が大技だった）。大技の説明は、数字が②以下になったか2ターン目から
                        bool lesson = turn.Grid != null && turn.Grid.Model != null && turn.Grid.Model.NoTraits;
                        // 【RC3・レビュー】はじめての戦闘（敵は まねるだけ・近づかない）：最後の1体は、壁・柱・端で止めてから近づく
                        if (lesson && turn.Enemies.Count(e => e.IsAlive) == 1) Add("L1", "残り1体：まねる敵は、あなたと同じ向きに動いて逃げる。壁・柱・盤面の端で止まるので、止まった敵に近づいて斬ろう");
                        // 【RC5】はじめての戦闘の後も、最後の1体が離れたまま まねで逃げる追いかけっこ（片付けの空ターン）に1回だけ
                        var lastOne = turn.Enemies.Count(e => e.IsAlive) == 1 && turn.Enemies.Count(e => !e.Model.IsObject) >= 2 ? turn.Enemies.First(e => e.IsAlive).Model : null; // 【RC5・レビュー】はじめから1体の部屋（落石の広間）では出さない
                        if (!lesson && lastOne != null && !lastOne.IsObject && (lastOne.CopyKind == CopyKind.All || lastOne.CopyKind == CopyKind.Move) &&
                            Mathf.Abs(lastOne.Position.x - turn.Grid.Model.Player.Position.x) + Mathf.Abs(lastOne.Position.y - turn.Grid.Model.Player.Position.y) >= 2)
                            Add("L2", "残り1体：まねる敵は、あなたと同じ向きに歩いて離れる。「突き」（2マス先）・「引き寄せ」で届かせるか、壁・柱・端で止まった所へ近づこう");
                        // 【RC6・レビュー「トゲの通路で詰まる」】4ターン続けて敵に1も入らなかったら、その部屋のコツ（まねで動かす）
                        if (!lesson && turn.TurnsSinceEnemyHurt >= 4 && Run.Current != null && !string.IsNullOrEmpty(Run.Current.CopyTip)) Add("S1_" + Run.Current.Id, "敵に届かないときは：" + Run.Current.CopyTip);
                        // 【RC9・本人「本編は答えを出さない」】本編の最初に1回：予告は出ないこと・何を見て読むか・実行の後に何が出るか
                        if (!lesson) Add("V9", "本編では、まね・敵の攻撃の結果は前もって出ない（実行の後に右下へ出る）。誰の攻撃か・赤（敵と動く）か紫（固定）か・順番を見て読もう"); // 【RC10・初見テスト2回目（3人）「予告は出ないのに -3・まね↑が出る」】 // 【RC10・初見テスト「ヒントが長い」】 // 【RC9・3回目「-3・まね↑が出るのに『結果は出ない』と矛盾」】 // 【RC9・初見テスト「予告は出ないのに 3ダメ が出る」】
                        Add("V0", "あなたの命令を、敵もそれぞれの位置からまねる（まね方は上の敵の欄の水色の字）。誰もいない方へ斬っても、まねた敵の隣に仲間がいれば当たる"); // 【RC8】名札は無くなった
                        // 【RC4・レビュー】赤は兵種の紹介より先（前は2階で5ターン目に出た）。あなたが赤の上にいるときは真っ先に
                        // 【RC7・本人の RC6 実プレイ：砲王の赤紫の上に立ったまま HP 18 → 0】今いるマスが「今」の危険（赤・赤紫・床）なら、盤面の「！ 〇ダメ」の読み方を一番先に
                        if (!lesson && turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count == 0 && (ThreatMap.EnemyAttack(turn.Grid.Model).Contains(turn.Grid.Model.Player.Position) || ThreatMap.Telegraph(turn.Grid.Model).Contains(turn.Grid.Model.Player.Position)))
                            Add("V7", "あなたのマスに攻撃が来る（マスの左上の札：記号＝撃つ敵・数字＝その攻撃のダメージ）。よける・撃つ敵を動かす（赤だけ）・倒す、のどれかを自分で選ぼう"); // 【RC9】「！ 〇ダメ」は本編で出さない
                        // 【RC8・本人「攻撃をかわす手段が移動するしかない」】あなたが「敵と一緒に動く赤」の中にいたら：逃げる以外に、押す・引く・倒す
                        if (!lesson && turn.State == BattleState.PlayerInput && Threats.Compute(turn.Grid.Model).Any(t => t.Kind == ThreatKind.Follow && t.Timing == ThreatTiming.Attack && t.Cells.Contains(turn.Grid.Model.Player.Position)))
                            Add("V8", "逃げるだけじゃない：赤（点線）は撃つ敵と一緒に動く。押す・引くと攻撃もずれる／倒せば消える。紫（斜線）は床に固定で動かない（倒せば消える）"); // 【RC9】ずれた後の予告は本編では出ない
                        if (!lesson && ThreatMap.EnemyAttack(turn.Grid.Model).Contains(turn.Grid.Model.Player.Position)) Add("V5", "赤いマス＝このターンに敵が攻撃するマス。左上の記号（A・B…）＝撃つ敵。敵が自分で動く先はオレンジの矢印（攻撃とまねのあと）"); // 【RC8】「変わらない」は誤り：押す・引く・倒すで変わる
                        // 【RC4】初めて会う兵種は1行だけ紹介（一度見たら出ない）。自分で動く兵種は「いつ・どこへ」まで
                        if (!lesson)
                            foreach (var kind in turn.Enemies.Where(e => e.IsAlive && !e.Model.IsObject).Select(e => e.Model.Kind).Distinct())
                                if (kind != EnemyKind.Normal && kind != EnemyKind.HealClone && kind != EnemyKind.MirrorLord)
                                    Add("K_" + kind, $"初めての敵：{PhaseRules.KindName(kind)}　{PhaseRules.MoveStyle(kind)}" + (kind == EnemyKind.Chaser || kind == EnemyKind.Archer || kind == EnemyKind.ExecKnight || kind == EnemyKind.BeastKing || kind == EnemyKind.Swordsman || kind == EnemyKind.Spearman ? "（動く先はオレンジの矢印）" : ""));
                        if (!lesson) Add("V5", "赤いマス＝このターンに敵が攻撃するマス。左上の記号（A・B…）＝撃つ敵。敵が自分で動く先はオレンジの矢印（攻撃とまねのあと）"); // 【RC3】【RC4】【RC8】
                        if (!lesson && turn.Enemies.Any(e => e.IsAlive && e.Model.CopyKind != CopyKind.All)) Add("V6", "まね方は敵ごとに違う（上の敵の欄の水色の字）：全まね＝全部／技まね＝攻撃・押す・引くだけ／移動まね＝移動だけ"); // 【RC8】 // 【RC3・レビュー】チュートリアルは全まねだけ
                        bool anyClock = turn.Enemies.Any(e => e.IsAlive && e.Model.Clocks.Count > 0); // 【RC4・レビュー】大技の無い1・2階で大技の説明が出ていた
                        if (anyClock && (turn.TurnNumber >= 2 || turn.Enemies.Any(e => e.IsAlive && e.Model.Clocks.Any(c => c.Cd <= 2))))
                            Add("V1", "★の数字＝その敵の大技まであと何ターン（敵のマスの右下と上の欄）。「今」になると当たるマスが紫で決まり、そのターンのまねの後（敵が自分で動く前）に発動する。敵にも当たる"); // 【RC8】
                        if (turn.Enemies.Any(e => e.IsAlive && e.Model.Clocks.Any(c => c.Cd == 1))) Add("V2", "紫で★の敵のマス＝このターンの大技。そこから避けるだけでなく、敵を押す・引く・まねで動かして、大技に巻き込ませよう");
                        if (turn.Tactics != null && turn.TurnNumber >= 1) Add("V3", "緑の札＝戦術札（Z キー）。命令の枠を使わず、敵もまねしない。使えば次のターンにまた1枚。「早鐘」は敵の大技を1ターン早める＝敵どうしを並べてから撃たせよう");
                        if (turn.Enemies.Any(e => e.IsAlive && e.Model.Kind == EnemyKind.Archer)) Add("V4", "弓兵は赤い線の向きへ撃つ（狙い直さない）。柱が矢を止める。ターンの最後に、あなたの行・列へ1マス動いて次の狙いを決める（オレンジの矢印）");
                    }
                    if (!turn.IsV4) Add("B1", "あなたの命令は、敵が自分の位置からそのまままねする。赤＝敵の今の攻撃範囲（動く前）");
                    if (turn.CanTidy && turn.TurnNumber >= 2) Add("B6", $"欲しい種類の札が無いときは「手札整理」：1戦1回、{turn.TidyMax}枚まで捨てて引き直せる（命令の枠は使わない）");
                    if (turn.Player.Hp * 2 <= turn.Player.MaxHp) Add("B2", "HP は次の戦闘へ持ち越す。休息・回復薬・応急処置で戻せる");
                    break;
            }
            foreach (var h in list)
            {
                if (Profile.hintsSeen.Contains(h.Key)) continue;
                // 【RC3・レビュー】戦闘のヒントは2ターン見せたら次へ（前は × を押すまで最初の「まね」が出続け、赤・大技・緑の札のヒントが一度も出なかった）
                if (Screen == ActScreen.Battle && turn != null)
                {
                    if (hintShownId != h.Key || turn.TurnNumber < hintShownTurn) { hintShownId = h.Key; hintShownTurn = turn.TurnNumber; }
                    else if (turn.TurnNumber - hintShownTurn >= 2) { DismissHint(h.Key); continue; }
                }
                return h;
            }
            return null;
        }

        string hintShownId;
        int hintShownTurn;

        public void DismissHint(string id)
        {
            if (string.IsNullOrEmpty(id) || Profile.hintsSeen.Contains(id)) return;
            Profile.hintsSeen.Add(id);
            SaveProfile();
        }

        public void SetHintsOff(bool off)
        {
            Profile.hintsOff = off;
            if (!off) Profile.hintsSeen.Clear(); // ON に戻したら最初から
            SaveProfile();
        }

        /// <summary>ランが終わったら1回だけ：プロフィールに記録・プレイ記録を書く・中断セーブを消す。</summary>
        void FinishRun()
        {
            if (Run == null || runRecorded) return;
            runRecorded = true;
            if (BuildInfo.Festival) festivalFreshMainPending = true; // 【文化祭版だけ】次にタイトルを開いたら、本編を はじめての状態に
            var used = Run.UsedCards.Keys.ToList();
            FirstTimeCards.Clear();
            FirstTimeCards.AddRange(used.Where(c => !Profile.usedCards.Contains(c)));
            PreviousBest = Profile.Record(Run.Role.Id).bestFloor;
            NewlyUnlocked.Clear();
            NewlyUnlocked.AddRange(ActProfile.RecordRun(Profile, Run, used));
            if (Run.V4Run) { FinalNews.Clear(); FinalNews.AddRange(FinalMeta.RecordRun(Profile, Run, Mathf.CeilToInt((float)(Run.PlaySeconds / 60.0)))); } // 【完成版】
            SaveProfile();
            WriteJournal();
            if (SaveEnabled) ActSave.Delete();
        }

        // ───── 【検証中】中断セーブ（1枠）：扉を選ぶ画面に来たら自動で保存。クリア・敗北で消す ─────

        /// <summary>自動撮影やバッチ（テスト・解析）では保存しない（本人のセーブを上書きしないため）。</summary>
        public bool SaveEnabled { get; set; }

        void Awake()
        {
            // 【RC12】5分体験：記録に「ヒント・答え・かかった秒」（人のテストで、5分に収まるか・どこで詰まるかを見る）
            PlaytestRecorder.Extra = () => FestivalMode && Festival != null ? $"文化祭 {FestivalDef.Label} {FestivalDef.Quest}：{Festival.Attempt}回目の挑戦{(Festival.Demo ? "（お手本）" : "")}・ヒント {HintLevel}・{Festival.SecondsAt(Time.unscaledTime):0}秒・敵を利用した攻撃 {Festival.EnemyHits}（まね {Festival.CopyHits}・敵の攻撃 {Festival.FriendlyHits}・仕掛け {Festival.HazardHits}・大技 {Festival.BigHits}）・被ダメ {Festival.Damage}" : InTutorial && QuickMode ? $"5分体験：ヒント {HintLevel}/{HintCount}・答え {(AnswerShown ? "見た" : "見ない")}・この問題 {Time.unscaledTime - stepStartedAt:0}秒・ここまでのヒント {HintsUsedTotal}・答え {AnswersUsedTotal}・5分体験の通算 {Time.unscaledTime - quickStartedAt:0}秒" : null;
            SaveEnabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autocapture") < 0 && !Application.isBatchMode;
            FestivalStats.Enabled = SaveEnabled; // 【文化祭】自動撮影・バッチでは集計を書かない
            if (SaveEnabled) Profile = ActProfile.Load();
            if (Profile != null && FinalMeta.ClearedRoleCount(Profile) >= 1) foreach (var id in new[] { "observer", "commander" }) if (!Profile.finalRoles.Contains(id)) Profile.finalRoles.Add(id); // 【第3巡の QA】前の版で1回クリアした人も、観測士と指揮官の両方（解放の条件が変わった）
            if (Profile.soundOff) { Sfx.Muted = true; Bgm.SetMuted(true); } // 【RC2】音の ON/OFF を覚える
            SoundSettings.Apply(Profile); // 【RC3】音量
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 【RC3・本人「画質が少し悪い」】はじめての起動だけ：画面が 1080p 以上なら 1600×900 の窓で開く（1280×720 では1マスが 58px ほどで小さかった）。
            //   2回目からは Unity が覚えた窓の大きさ（本人が変えた大きさ）のまま
            if (SaveEnabled && !PlayerPrefs.HasKey("rc3_window") && UnityEngine.Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                var res = UnityEngine.Screen.currentResolution;
                if (res.height >= 1080 && res.width >= 1760) UnityEngine.Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                PlayerPrefs.SetInt("rc3_window", 1);
                PlayerPrefs.Save();
            }
#endif
        }

        void EnterDoors()
        {
            Screen = ActScreen.Doors;
            if (SaveEnabled && Run != null) ActSave.Save(Run);
        }

        /// <summary>
        /// 【戦闘中心 v3】部屋の途中のセーブ（部屋に入った・ターンの始め・報酬・商人の買い物）。続きからは同じ部屋から（戦闘は最初から・HP は低い方）。
        /// 中断で戦闘から逃げる・報酬や商品を引き直す、ができないように（外部テストの基準 14）。
        /// </summary>
        public void Checkpoint()
        {
            if (!SaveEnabled || Run == null || !Run.CombatRun || Run.Failed || Run.Cleared || Run.Current == null) return;
            if (Screen == ActScreen.Reward) ActSave.SaveRoom(Run, "reward");
            else if (Screen == ActScreen.Battle) ActSave.SaveRoom(Run, "room", turn != null && turn.Player != null ? turn.Player.Hp : 0, null, turn != null ? Mathf.Max(turn.TurnNumber + trialTurnOffset, restartedThisRoom ? 2 : 0) : 0); // 【RC5・最終検証】やり直した部屋は、1ターン目で中断しても「途中から」のまま（ボーナスが戻っていた）
            else ActSave.SaveRoom(Run, "room");
        }

        public bool HasSave => SaveEnabled && ActSave.Exists();
        public string SaveDescription => HasSave ? ActSave.Describe() : null;

        /// <summary>タイトルの「続きから」。壊れたセーブは使わない（.broken に退避してメッセージ）。</summary>
        public void ContinueRun()
        {
            if (ActSave.TryLoad(Card, out var run, out var error))
            {
                CopyCore.SandboxCopyOff = false;
                CopyCore.SandboxMode = null;
                BattleUI.FullPrediction = false;
                lossHandled = false;
                Run = run;
                if (Run.V4Run && Profile.finalFinished == 0) Run.ForceFirstDoor("v4_i1"); // 【完成版】始めたときと同じ1階の扉に
                rolesOpenAtStart.Clear(); // 【RC2】続きからは、いま解放されている役職を「始めにあった」とみなす
                foreach (var r in ActContent.Roles) if (FinalMeta.IsRoleUnlocked(Profile, r.Id)) rolesOpenAtStart.Add(r.Id);
                PendingBossRetry = false;
                FinalNews.Clear();
                runRecorded = false;
                NewlyUnlocked.Clear();
                FirstTimeCards.Clear();
                Message = null;
                Screen = ActScreen.Doors;
                // 【戦闘中心 v3】部屋の途中から
                var phase = Run.ResumedPhase;
                Run.ResumedPhase = null;
                if (Run.Current != null && phase == "reward") Screen = ActScreen.Reward;
                else if (Run.Current != null && phase == "room")
                {
                    switch (Run.Current.Kind)
                    {
                        case RoomKind.Rest: Screen = ActScreen.Rest; break;
                        case RoomKind.Shop: Screen = ActScreen.Shop; break;
                        case RoomKind.Event: Screen = ActScreen.Event; break;
                        default:
                            int resumedTurn = Run.ResumedBattleTurn;
                            Run.ResumedBattleTurn = 0;
                            StartBattle(Run.Current);
                            Message = "続きから：この部屋を最初から（盤面・山札の順番は同じ。HP は中断したときと入ったときの低い方）"; // 【RC5・レビュー4】メニュー・README と同じ（ActSave の battleHp）
                            if (resumedTurn >= 2)
                            {
                                // 【レビュー】途中から＝罰の無いやり直しにしない：速さ・無傷・ミッションのボーナスは無し。試練は使ったターンを引く
                                restartedThisRoom = true;
                                Run.MidBattleResumes++; // 【RC5・最終ゲート】結果と感想用のコピーに出す
                                bool bossRetry = Run.UseBossRetryForResume(); // 【RC5・最終ゲート】ボスは途中からの再開＝再挑戦（1ランに1回）
                                if (Run.Current.Kind == RoomKind.Trial && battleStage != null && battleStage.turnLimit > 0)
                                    trialTurnOffset = Mathf.Min(resumedTurn - 1, battleStage.turnLimit - 1);
                                Message = "続きから：この部屋を最初から（途中から再開したので、速さ・無傷・ミッションのボーナスは無し" + (trialTurnOffset > 0 ? $"・試練は残り {battleStage.turnLimit - trialTurnOffset} ターン" : "") + (bossRetry ? "・ボスの再挑戦（1ランに1回）を使った" : "") + "）";
                                if (SaveEnabled) Checkpoint(); // 再挑戦を使ったことと回数をすぐ保存
                            }
                            break;
                    }
                }
            }
            else Message = "続きから始められない：" + error;
        }

        bool lossHandled;

        /// <summary>【完成版】今の画面の BGM（title / battle / boss / clear）。</summary>
        string DesiredBgm()
        {
            if (Screen == ActScreen.Battle && Run != null && Run.Current != null) return Run.Current.Kind == RoomKind.Boss || Run.Current.IsMidBoss ? "boss" : "battle";
            if (Screen == ActScreen.Battle && InTutorial) return "battle"; // 【RC3・レビュー】チュートリアルの戦闘もタイトルの曲のままだった
            if (Screen == ActScreen.Result && Run != null && Run.Cleared) return "clear";
            if (Screen == ActScreen.None) return turn != null && turn.Player != null ? "battle" : null; // 検証ステージ
            return "title";
        }

        /// <summary>
        /// 【検証中】ACT の戦闘で負けた瞬間にセーブを消す（「結果へ」を押す前にゲームを閉じて、続きから負けを取り消せないように）。
        /// 戦闘の途中で閉じた場合は、その戦闘のはじめから（部屋の途中のセーブ・HP は低い方）。
        /// </summary>
        void Update()
        {
            Bgm.Play(DesiredBgm()); // 【完成版】画面に合わせた BGM（同じ曲なら何もしない）
            UpdateTutorial(); // 【RC3】
            UpdateFestivalIdle(); // 【文化祭】展示の PC：しばらく操作が無いとき
            // 【戦闘中心 v3】遊んだ時間（結果画面・記録）
            if (Run != null && InAct && !Run.Cleared && !Run.Failed) Run.PlaySeconds += Time.unscaledDeltaTime;
            if (InBattle && Run != null && Run.CombatRun && turn != null)
            {
                // 試練：決まったターン数を過ぎたら失敗（負けではない）
                if (!trialHandled && Run.Current != null && Run.Current.Kind == RoomKind.Trial && turn.State == BattleState.PlayerInput && battleStage != null && battleStage.turnLimit > 0 && turn.TurnNumber + trialTurnOffset > battleStage.turnLimit)
                    FailTrial($"{battleStage.turnLimit}ターンを過ぎた");
                // ターンの始めにセーブ（中断して続きから＝この部屋を最初から・HP は低い方）
                else if (turn.State == BattleState.PlayerInput && turn.TurnNumber != savedTurn) { savedTurn = turn.TurnNumber; Checkpoint(); }
            }
            if (Screen == ActScreen.Battle && !lossHandled && turn != null && turn.State == BattleState.GameOver)
            {
                lossHandled = true;
                RecordLoss();
            }
            // ボスに勝った瞬間にクリアを記録（レビュー2：勝利の画面で閉じると、クリアが記録されずボスを戦い直せた）
            // 【戦闘中心 v3・レビュー】戦闘中心のランは全部の部屋で（勝利の画面で閉じると、勝った戦闘が最初からに戻っていた）
            if (Screen == ActScreen.Battle && !winHandled && turn != null && turn.State == BattleState.Cleared && Run?.Current != null && (Run.Current.Kind == RoomKind.Boss || Run.CombatRun))
                RecordWin();
        }

        /// <summary>負けの記録（1回だけ）：戦闘の内訳・ランの敗北・プロフィール・プレイ記録・セーブの削除。</summary>
        void RecordLoss()
        {
            // 【RC2】再挑戦を選ぶ間は Failed が false に戻っている → 2回目の呼び出し（ProceedAfterBattle）でランを終えないように PendingBossRetry も見る
            //        （撮影で見つけた：負けの画面で「次へ」を押すと負けをもう一度記録してランが終わり、再挑戦のボタンが効かなかった）
            if (Run == null || Run.Failed || Run.Cleared || PendingBossRetry) return;
            LastStats = BattleStats.From(turn.BattleEvents, false, turn.TurnNumber, battleStartHp, turn.Player.Hp);
            NoteBattle(false);
            Run.LoseBattle(LastStats);
            // 【完成版】ボスに負けたら、1回だけ再挑戦を選べる（ランはまだ終わらない）
            // 【RC2】選ぶ画面で閉じて「続きから」にすると、再挑戦を使わずにボスをやり直せた（レビュー）→ 負けた瞬間に「再挑戦を使った」状態で保存する。
            //        あきらめたら、その場で負けに戻す（GiveUpAfterBossLoss）。
            if (Run.CanRetryBoss)
            {
                hpAfterBossLoss = Run.Hp;
                Run.RetryBoss();
                if (SaveEnabled) ActSave.SaveRoom(Run, "room");
                PendingBossRetry = true;
                return;
            }
            FinishRun();
        }

        /// <summary>【検証中】ランをやめる（記録を書き、セーブを消してタイトルへ）。検証メニューの「タイトルへ（ランは終わる）」。</summary>
        public void AbandonRun()
        {
            if (InTutorial) { SkipTutorial(); return; } // 【RC3】チュートリアル中はランのセーブを消さない
            // 戦闘の結果の画面で「やめる」を押したら、先に勝ち負けを反映する（レビュー：ボスに勝った直後にやめるとクリアが記録されなかった）
            if (Screen == ActScreen.Battle && turn != null && (turn.State == BattleState.Cleared || turn.State == BattleState.GameOver)) ProceedAfterBattle();
            if (Run != null && !Run.Cleared && !Run.Failed && !runRecorded)
            {
                Run.Note("## 途中でやめた");
                WriteJournal("途中でやめた");
            }
            if (SaveEnabled) ActSave.Delete();
            OpenTitle();
        }

        /// <summary>扉を選ぶ。</summary>
        public void ChooseDoor(RoomDef room)
        {
            if (Run == null || !Run.Enter(room)) return;
            Message = null;
            if (Run.CombatRun && SaveEnabled) ActSave.SaveRoom(Run, "room"); // 【戦闘中心 v3】部屋に入ったら（中断しても同じ部屋から）
            switch (room.Kind)
            {
                case RoomKind.Battle:
                case RoomKind.Danger:
                case RoomKind.Tactical: // 【戦闘中心 v3】でたらめ通しで、戦術戦・試練の扉を押しても何も起きなかった
                case RoomKind.Trial:
                case RoomKind.Boss: StartBattle(room); break;
                case RoomKind.Rest: Screen = ActScreen.Rest; break;
                case RoomKind.Shop: Screen = ActScreen.Shop; break;
                case RoomKind.Event: Screen = ActScreen.Event; break;
            }
        }

        int savedTurn;
        bool trialHandled;
        /// <summary>【レビュー】続きから戻った試練で、もう使ったターン数（残りのターンだけ遊べる）。</summary>
        int trialTurnOffset;
        /// <summary>試練の残りの上限の表示に使う（今のターン＋使ったターン）。</summary>
        public int TrialTurnOffset => trialTurnOffset;

        /// <summary>【戦闘中心 v3】試練をあきらめられるか（あなたの番）。</summary>
        public bool CanGiveUpTrial => InBattle && Run != null && Run.Current != null && Run.Current.Kind == RoomKind.Trial && !trialHandled && turn.State == BattleState.PlayerInput;

        /// <summary>【戦闘中心 v3】試練をあきらめる（負けではない・報酬なし）。</summary>
        public void GiveUpTrial() { if (CanGiveUpTrial) FailTrial("あきらめた"); }

        /// <summary>【戦闘中心 v3】試練の失敗：負けではない。HP はそのまま、報酬は無し。報酬の画面（空）へ。</summary>
        void FailTrial(string reason)
        {
            if (trialHandled || Run == null) return;
            trialHandled = true;
            winHandled = true;
            LastStats = BattleStats.From(turn.BattleEvents, false, System.Math.Max(1, System.Math.Min(turn.TurnNumber + trialTurnOffset, battleStage != null && battleStage.turnLimit > 0 ? battleStage.turnLimit : turn.TurnNumber)), battleStartHp, turn.Player.Hp);
            NoteBattle(false, "試練：" + reason);
            Run.FailTrial(LastStats);
            Message = $"試練：{reason}。報酬は無し（負けではない・HP はそのまま）";
            Screen = ActScreen.Reward;
            Checkpoint();
        }

        /// <summary>【RC3】自動撮影だけ：地図に無い部屋でも、その階の戦闘として始める（全部屋 × 開始配置の撮影）。</summary>
        public void StartRoomForCapture(RoomDef room, int floor)
        {
            if (Run == null) return;
            Run.SetFloorForTest(floor, room);
            StartBattle(room);
        }

        void StartBattle(RoomDef room)
        {
            var src = Stage(room.Stage);
            if (src == null) { Message = $"ステージ {room.Stage} が見つからない"; Screen = ActScreen.Doors; return; }
            // 見た目のテーマは無ければ古代遺跡（ボスは紫晶神殿）。ルールは元のステージのまま
            if (battleStage != null) { if (Application.isPlaying) Destroy(battleStage); else DestroyImmediate(battleStage); } // 前の戦闘の複製を片付ける（テストは Editor のまま通す）
            var stage = Instantiate(src);
            battleStage = stage;
            stage.name = src.name;
            // 画面の上には部屋の名前（【レビュー】戦闘中心では番号（K2）を出さない・試練は短く：TURN の帯で切れていた）
            stage.stageTitle = Run.CombatRun ? room.Name + (room.Kind == RoomKind.Trial ? "（試練）" : room.Kind == RoomKind.Boss ? "（ボス）" : room.ParTurns > 0 && !Run.LessonBattle ? $"　目安 {room.ParTurns}T" : "") // 【RC5・レビュー4】はじめての戦闘は速さを測らない
                : $"{room.Name}（{src.buttonLabel}）" + (room.ParTurns > 0 ? $"　速さの目安 {room.ParTurns}T" : "") + (room.Kind == RoomKind.Trial && src.turnLimit > 0 ? $"　試練：{src.turnLimit}ターン以内" : "");
            if (room.EnemyHp > 0) stage.enemyMaxHp = room.EnemyHp;
            if (stage.visualTheme == null) stage.visualTheme = room.Kind == RoomKind.Boss ? crystalTheme : ruinsTheme;
            battleStartHp = Run.Hp;
            winHandled = false;
            PendingItem = PendingItemUse.None;
            var setup = Run.MakeBattleSetup();
            battleSeed = setup.Seed;
            restartedThisRoom = false;
            trialHandled = false;
            trialTurnOffset = 0;
            savedTurn = 0;
            turn.LoadActBattle(stage, setup);
            Screen = ActScreen.Battle;
        }

        int battleSeed;
        /// <summary>【Puzzle v2・レビュー】やり直しは1部屋1回（罰の無いやり直しで HP・無傷ボーナスが取り放題だった）。</summary>
        bool restartedThisRoom;
        public bool RestartedThisRoom => restartedThisRoom;

        /// <summary>
        /// 【Puzzle v2】部屋をやり直す（パズルで詰んだとき）。盤面と山札の順番は部屋に入ったときと同じ（引き直しにはならない）。
        /// HP は「入ったとき」と「今」の低い方（パズルブレイカー：倒れる前に押すと HP が戻る保険になっていた）。
        /// アイテムは使った分は戻らない。速さ・無傷・ミッション・危険戦の追加の報酬は無し。
        /// </summary>
        public bool RestartBattle()
        {
            if (!InBattle || Run == null || battleStage == null || turn.State == BattleState.Cleared || turn.State == BattleState.GameOver) return false;
            if (turn.State != BattleState.PlayerInput || restartedThisRoom) return false;
            restartedThisRoom = true;
            Run.Restarts++;
            Run.Note($"- 部屋をやり直した（{turn.TurnNumber}ターン目）");
            winHandled = false;
            PendingItem = PendingItemUse.None;
            var setup = Run.MakeBattleSetup(battleSeed);
            setup.Hp = Mathf.Max(1, Mathf.Min(setup.Hp, turn.Player != null ? turn.Player.Hp : setup.Hp));
            turn.LoadActBattle(battleStage, setup);
            Message = "部屋をやり直した（盤面・山札の順番は入ったときと同じ。HP は今のまま）";
            return true;
        }

        /// <summary>やり直しを出す部屋か（パズル中心のランの部屋）。</summary>
        public bool CanRestart => InBattle && Run != null && Run.PuzzleRun && Run.Current != null && turn.State == BattleState.PlayerInput && !restartedThisRoom;

        /// <summary>戦闘の結果画面の「次へ」。</summary>
        public void ProceedAfterBattle()
        {
            if (InTutorial) return; // 【RC3】チュートリアルは「できた！」の窓で進む
            if (Screen != ActScreen.Battle) return;
            bool won = turn.State == BattleState.Cleared;
            if (!won && turn.State != BattleState.GameOver) return;
            if (!won)
            {
                RecordLoss(); // Update で済んでいれば何もしない
                Screen = ActScreen.Result;
                return;
            }
            RecordWin(); // ボスの勝ちは Update で済んでいれば何もしない
            Screen = Run.Cleared ? ActScreen.Result : ActScreen.Reward;
        }

        bool winHandled;

        /// <summary>勝ちの記録（1戦で1回）：戦闘の内訳・ルーン片・報酬の抽選。ボスならランの終わり（プロフィール・記録・セーブの削除）も。</summary>
        void RecordWin()
        {
            if (winHandled || Run == null) return;
            winHandled = true;
            LastStats = BattleStats.From(turn.BattleEvents, true, turn.TurnNumber, battleStartHp, turn.Player.Hp);
            LastStats.TacticUses = turn.TacticsUsedThisBattle; // 【Combat V4】
            if (battleStage != null) LastStats.Goal = battleStage.goal;
            LastStats.Restarted = restartedThisRoom;
            NoteBattle(true);
            Run.WinBattle(LastStats);
            Message = string.Join("　", new[] { Run.LastParResult, Run.LastMissionResult }.Where(s => s != null));
            if (Message.Length == 0) Message = null;
            if (Message != null) Run.Note("- " + Message);
            if (Run.Cleared) FinishRun();
            else if (Run.CombatRun && SaveEnabled) ActSave.SaveRoom(Run, "reward"); // 【戦闘中心 v3】報酬の画面から続けられる（引き直せない）
        }

        /// <summary>結果画面：同じ役職でもう一度（新しい種）／同じ地図で再挑戦（同じ種）。</summary>
        public void Retry(bool sameSeed)
        {
            if (Run == null) return;
            var role = Run.Role;
            int seed = sameSeed ? Run.Seed : 0;
            StartRun(role, seed, Run.CopyCentric, Run.PuzzleRun, Run.CombatRun, Run.V4Run);
        }

        /// <summary>報酬・休息・商人・特殊部屋を終えて、次の階の扉へ。</summary>
        public void NextFloor()
        {
            Run.Leave();
            Message = null;
            if (Run.Floor >= Run.FloorCount) { Screen = ActScreen.Result; FinishRun(); }
            else EnterDoors();
        }

        /// <summary>戦闘の内訳を記録（ターン・被ダメとその出どころ・倒し方）。</summary>
        void NoteBattle(bool won, string resultText = null)
        {
            var src = new Dictionary<string, int>();
            foreach (var e in turn.BattleEvents)
            {
                if (e.Type != BattleEventType.Damaged || e.Unit == null || !e.Unit.IsPlayer || e.Cause == null) continue;
                // 【RC7】前は大技（赤紫）も「敵のまね攻撃」に入っていた（本人の砲王の負け「まね 22」は、ほとんどが砲王の大技だった）
                string k = e.Cause.Kind == DamageKind.Spike || e.Cause.Kind == DamageKind.Fire || e.Cause.Kind == DamageKind.Gaze ? "環境" : e.Cause.Kind == DamageKind.Trait ? "敵の攻撃（赤）" : e.Cause.Kind == DamageKind.Clock ? "大技（紫）" : e.Cause.ByCopy ? "まね返し" : e.Cause.Source != null && e.Cause.Source.IsPlayer ? "自分" : "そのほか";
                src[k] = (src.TryGetValue(k, out var v) ? v : 0) + e.Amount;
                // 結果画面の「受けたダメージの内訳」はもっと細かく（反射・剣兵の剣・トゲ…：戦闘中の札と同じ言葉）
                string label = UiStyles.DamageCauseLabel(e) ?? "その他";
                Run.AddDamage(label, e.Amount);
                if (e.Amount > 0) Run.LastHit = $"{label} -{e.Amount}";
            }
            Run.AddUsedCards(turn.UsedCardNames);
            var s = LastStats;
            string srcText = src.Count > 0 ? string.Join("・", src.Select(k => k.Key + " " + k.Value)) : "なし";
            Run.Note($"- 戦闘：{resultText ?? (won ? "勝ち" : "負け")} {s.Turns}T・被ダメ {s.DamageTaken}（{srcText}）・環境キル {s.EnvironmentKills}・同士討ち {s.FriendlyFire}・まねで敵に {s.CopyDamage}・まね利用 {s.CopyUses}・まねキル {s.CopyKills.Count}{(turn.TidyUsed ? "・手札整理" : "")}　残り HP {turn.Player.Hp}");
            if (Run.Current != null && Run.Current.Kind == RoomKind.Boss) Run.Note($"- ボス到達時の HP：{battleStartHp}");
            // 【RC7】ターンごとの記録（本人の実プレイで「どのターンに・何で・いくつ」受けたかを後から読む。公開版・ブラウザでは書き出さない）
            turn.FlushTurnTrace();
            if (!BuildInfo.PublicBuild && Application.platform != RuntimePlatform.WebGLPlayer) // 【RC7・コードレビュー】記録は書き出す版だけ（セーブにも入るので）
                foreach (var line in turn.TurnTrace) Run.Note("  - " + line);
        }

        /// <summary>ランが終わったら、プレイ記録を Markdown で書き出す（ビルド：exe の隣の PlaytestLogs／Editor：Logs/playtest）。個人情報なし。</summary>
        void WriteJournal(string resultOverride = null)
        {
            if (Run == null || !SaveEnabled) return;
            if (BuildInfo.PublicBuild || Application.platform == RuntimePlatform.WebGLPlayer) return; // 【完成版】公開版・ブラウザでは書かない
            try
            {
                string result = resultOverride != null ? $"{resultOverride}（{Run.Floor + 1}階 {Run.Current?.Name}）" : Run.Cleared ? "ACT1 CLEAR" : $"敗北（{Run.Floor + 1}階 {Run.Current?.Name}）";
                Run.Note($"## 結果：{result}　合計 {Run.TotalTurns}T・被ダメ {Run.TotalDamage}・稼いだルーン片 {Run.ShardsEarned}・残り {Run.Shards}・ミッション {Run.MissionsDone}");
                Run.Note($"- 最後のデッキ（{Run.Deck.Count}枚）：{string.Join("・", Run.Deck.GroupBy(c => c.displayName).Select(g => g.Count() > 1 ? g.Key + "×" + g.Count() : g.Key))}");
                Run.Note($"- 刻印：{string.Join("・", Run.Imprints.Select(i => ActContent.Imprint(i).Name))}　アイテム（残り）：{string.Join("・", Run.Items.Select(i => ActContent.Item(i).Name))}");
                Run.Note($"- 遊んだ時間：{(int)(Run.PlaySeconds / 60)}分{(int)(Run.PlaySeconds % 60)}秒・意味のあるまね利用 {Run.CopyUsesTotal}・まねキル {Run.CopyKillsTotal}・試練 成功 {Run.TrialsCleared}／失敗 {Run.TrialsFailed}");
                string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath) ?? ".", Application.isEditor ? "Logs/playtest" : "PlaytestLogs");
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, $"act1_{System.DateTime.Now:yyyyMMdd_HHmmss}_{Run.Role.Id}_{Run.Seed}.md");
                System.IO.File.WriteAllText(file, string.Join(System.Environment.NewLine, Run.Journal));
            }
            catch (System.Exception e) { Debug.LogWarning("[ActRunManager] 記録を書けなかった：" + e.Message); }
        }

        // ───── 戦闘中のアイテム ─────

        public bool CanUseItems => Screen == ActScreen.Battle && turn.State == BattleState.PlayerInput && PendingItem == PendingItemUse.None;

        public void UseItem(ItemId id)
        {
            if (!CanUseItems || !Run.Items.Contains(id)) return;
            switch (id)
            {
                case ItemId.Potion:
                    if (turn.Player.Hp >= turn.Player.MaxHp) { Message = "HP は満タン（回復薬は使わずに残した）"; return; }
                    Run.Note($"- アイテム：回復薬（{turn.TurnNumber}T目・HP {turn.Player.Hp}）");
                    turn.HealPlayer(6);
                    Run.ConsumeItem(id);
                    break;
                case ItemId.CommandStone:
                    Run.Note($"- アイテム：命令石（{turn.TurnNumber}T目）");
                    turn.AddTempSlots(1);
                    Run.ConsumeItem(id);
                    break;
                case ItemId.Hourglass:
                    if (turn.UnplannedHandCount == 0) { Message = "引き直す手札が無い（★は使わずに残した）"; return; }
                    Run.Note($"- アイテム：選別の砂時計（{turn.TurnNumber}T目）");
                    turn.Mulligan();
                    Run.ConsumeItem(id);
                    break;
                case ItemId.BlankStone:
                    if (turn.PlannedCommands.Count == 0) { Message = "先に命令を組んでから、まねさせない命令を選ぶ"; return; }
                    PendingItem = PendingItemUse.BlankStone;
                    break;
                case ItemId.Hook:
                    if (turn.Deck.DiscardPile.Count == 0) { Message = "捨て札が無い"; return; }
                    PendingItem = PendingItemUse.Hook;
                    break;
                case ItemId.WarpStone: // 【RC4】救済の移動：行き先を盤面で選ぶ
                    if (turn.PlannedCommands.Count > 0) { Message = "転移石は命令を組む前に使う（組んだ命令を戻してから）"; return; }
                    { var why = RescueRules.WhyNot(turn.Grid.Model); if (why != null) { Message = why + "（転移石は残した）"; return; } }
                    PendingItem = PendingItemUse.WarpStone;
                    break;
            }
            Sfx.Play(Sfx.Kind.Button);
        }

        /// <summary>空白の石：この番号の命令をコピーさせない。</summary>
        public void ChooseBlankSlot(int index)
        {
            if (PendingItem != PendingItemUse.BlankStone) return;
            if (turn.MarkUncopied(index)) { Run.ConsumeItem(ItemId.BlankStone); Run.Note($"- アイテム：空白の石（{turn.TurnNumber}T目・{index + 1}番目の命令）"); }
            PendingItem = PendingItemUse.None;
        }

        /// <summary>回収の鉤：捨て札のこのカードを手札へ。</summary>
        public void ChooseHookCard(CardInstance card)
        {
            if (PendingItem != PendingItemUse.Hook) return;
            if (turn.ReturnFromDiscard(card)) { Run.ConsumeItem(ItemId.Hook); Run.Note($"- アイテム：回収の鉤（{card.Data.displayName}）"); }
            PendingItem = PendingItemUse.None;
        }

        /// <summary>【RC4】転移石：このマスへ移る。</summary>
        public bool ChooseWarpCell(Vector2Int cell)
        {
            if (PendingItem != PendingItemUse.WarpStone) return false;
            if (RescueRules.DamageIfWarped(turn.Grid.Model, cell) >= turn.Player.Hp) { Message = "そのマスでは倒れる（転移石は残した）"; PendingItem = PendingItemUse.None; return false; } // 【RC4・レビュー】炎で倒れるマス
            bool ok = turn.WarpPlayer(cell);
            if (!ok) Message = turn.PlannedCommands.Count > 0 ? "転移石は命令を組む前に使う（転移石は残した）" : "そこへは移れない（転移石は残した）"; // 【RC4・レビュー】黙って失敗していた
            if (ok) { Run.ConsumeItem(ItemId.WarpStone); Run.Note($"- アイテム：転移石（{turn.TurnNumber}T目・({cell.x},{cell.y}) へ・HP {turn.Player.Hp}）"); Sfx.Play(Sfx.Kind.Button); }
            PendingItem = PendingItemUse.None;
            return ok;
        }

        public void CancelItem() => PendingItem = PendingItemUse.None;

        /// <summary>テスト用。</summary>
        public void SetupForTest(TurnManager turnManager, List<CommandData> cardList, List<StageData> stageList)
        {
            turn = turnManager;
            cards = cardList;
            stages = stageList;
            openTitleOnStart = false;
            SaveEnabled = false;
        }

        /// <summary>撮影用：負けを強制する（ゲーム本体では使わない）。</summary>
        public void ForceLoseForCapture()
        {
            turn.Player.TakeDamage(turn.Player.Hp);
            var check = typeof(TurnManager).GetMethod("CheckBattleEnd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            check?.Invoke(turn, null);
        }

        /// <summary>撮影用：戦闘の勝敗を強制する（ゲーム本体では使わない）。</summary>
        public void ForceWinForCapture()
        {
            foreach (var e in turn.Enemies) e.Model.Hp = 0;
            turn.Grid.Model.Goal = RoomGoal.Defeat; // 【Puzzle v2】目標の部屋も撮影では勝ちにする
            var check = typeof(TurnManager).GetMethod("CheckBattleEnd", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            check?.Invoke(turn, null);
        }
    }
}
