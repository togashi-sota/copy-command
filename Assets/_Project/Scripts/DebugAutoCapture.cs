using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 確認用の自動撮影。起動引数に -autocapture <フォルダ> を付けたときだけ動く（普段のプレイには影響しない）。
    /// -capturestage <番号>（0 = STAGE 1、1 = STAGE 2、2 = STAGE 3、4〜6 = 複数Enemyテスト M1〜M3、-1 = RUN）で、
    /// そのステージの想定ルートを自動で入力し、各場面を撮って終了する。
    /// -capturehand <0〜5>（固定／A／B／大:3枚／大:使用+1／中:7/9）で手札方式を切り替えて撮る（A/B は手札にあるカードだけでルートを入力し、無ければ飛ばす）。
    /// </summary>
    public partial class DebugAutoCapture : MonoBehaviour // 【RC7】紹介動画の撮影は DebugAutoCapture.Video.cs
    {
        string folder;
        int stageIndex;
        HandMode handMode = HandMode.Fixed;
        bool stagnation;
        int stallTurns;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (BuildInfo.PublicBuild) return; // 【完成版】公開版には入れない（起動引数で自動プレイ・撮影ができてしまう）
            var args = System.Environment.GetCommandLineArgs();
            string folder = null;
            int stage = 0, hand = 0, stagnationOn = 0, stall = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-autocapture") folder = args[i + 1];
                if (args[i] == "-capturestage") int.TryParse(args[i + 1], out stage);
                if (args[i] == "-capturehand") int.TryParse(args[i + 1], out hand);
                if (args[i] == "-capturestagnation") int.TryParse(args[i + 1], out stagnationOn);
                if (args[i] == "-capturestall") int.TryParse(args[i + 1], out stall);
                if (args[i] == "-capturesmokeruns") int.TryParse(args[i + 1], out smokeRuns); // 【RC5・レビュー3】でたらめ通しのラン数（既定 12）
                if (args[i] == "-captureact" && args[i + 1] == "1") stage = -2; // 【検証中】ACT1 を1周（勝敗は撮影のため強制）
                if (args[i] == "-captureact" && args[i + 1] == "2") stage = -3; // 【検証中】負け → 結果画面 → もう一度（RC の確認）
                if (args[i] == "-captureact" && args[i + 1] == "3") stage = -4; // 【検証中】でたらめに遊ぶ通しの確認（例外が出ないか。勝敗は強制しない）
                if (args[i] == "-captureact" && args[i + 1] == "4") stage = -8; // 【Puzzle v2】パズル中心 ACT1 v2 をでたらめに通す（やり直し・再利用も）
                if (args[i] == "-captureact" && args[i + 1] == "5") stage = -9; // 【戦闘中心 v3】戦闘中心 ACT1 v3 をでたらめに通す（手札整理・試練の失敗・セーブの往復も）
                if (args[i] == "-capturesuper" && args[i + 1] == "1") proposalSuper = true; // 【企画書 v5】2倍の解像度で撮る
                if (args[i] == "-capturefinal" && args[i + 1] == "1") stage = -14; // 【完成版】完成版の画面一式（はじめての人の状態から・ボスの再挑戦・クリアの解放）
                if (args[i] == "-capturerooms" && (args[i + 1] == "1" || args[i + 1] == "2")) { stage = -16; roomsQuick = args[i + 1] == "2"; } // 2＝画質の比べ用に3場面だけ // 【RC3】全部屋 × 開始配置3つの始めの画面（盤面の見直し）・はじめての戦闘（まねるだけ）
                if (args[i] == "-capturerc4" && args[i + 1] == "1") stage = -17; // 【RC4】はじめての人の入口・方向の入力（端・中央）・敵の動きの予告・敵の詳細・転移石・2階／3階の教え方
                if (args[i] == "-capturetutorial" && args[i + 1] == "1") stage = -15; // 【RC3】チュートリアルを最後まで（決めた手で）・各段階の始め／組んだ命令／できた！
                if (args[i] == "-capturev4" && args[i + 1] == "1") stage = -11; // 【Combat V4】画面一式（タイトル・役職・扉・各部屋・大技の予告・戦術札・ボス・報酬・商人・結果）
                if (args[i] == "-captureproposal" && (args[i + 1] == "1" || args[i + 1] == "2" || args[i + 1] == "3")) { stage = -13; proposalHeroOnly = args[i + 1] == "2"; proposalJobHunt = args[i + 1] == "3"; proposalSuper = true; } // 【企画書】企画書用の場面（本物のルールで手順を再生し、途中の瞬間を撮る）
                if (args[i] == "-capturevideo" && (args[i + 1] == "1" || args[i + 1] == "2")) { stage = -20; videoScenesOnly = args[i + 1] == "2"; } // 2＝scenes.txt の場面だけ（就活資料 V8） // 【RC7】紹介動画（30fps のコマ・効果音の記録）
                if (args[i] == "-capturerc8" && args[i + 1] == "1") stage = -21; // 【RC8】新しい戦闘画面の見直し用の場面
                if (args[i] == "-capturerc9" && args[i + 1] == "1") stage = -22; // 【RC9】本編は答えを出さない画面の見直し用
                if (args[i] == "-capturefestival" && (args[i + 1] == "1" || args[i + 1] == "2")) stage = args[i + 1] == "1" ? -23 : -24; // 【文化祭】画面一式／でたらめ通し
                if (args[i] == "-capturefixed" && int.TryParse(args[i + 1], out var fx9)) BattleUI.FixedLabelMode = fx9; // 【RC9】「固定」の文字（0 出さない・1 はじめだけ・2 いつも）
                if (args[i] == "-captureclean" && args[i + 1] == "1") BattleUI.CleanCapture = true; // 【通常リリース版】ヒントの帯とキーの行を隠して撮る
                if (args[i] == "-captureanswer" && args[i + 1] == "1") BattleUI.DebugAnswerPreview = true; // 【RC9】比べ用：本編でも答えを出す
                if (args[i] == "-capturelayout" && int.TryParse(args[i + 1], out var lay8)) BattleUI.LayoutVariant = lay8; // 【RC8】配置の案（0＝RC7・1＝新しい）
                if (args[i] == "-captureact" && args[i + 1] == "6") stage = -12; // 【Combat V4】でたらめ通し（戦術札・追加購入・セーブの往復も）
                if (args[i] == "-capturecombat" && args[i + 1] == "1") stage = -10; // 【戦闘中心 v3】外部テスト版の画面一式（タイトル・遊び方・役職・扉・各部屋・ボスの段階・手札整理・試練・結果）
                if (args[i] == "-capturecopylab" && args[i + 1] == "1") stage = -5; // 【検証中】Copy Core V2：一覧 → 各ステージ（始め・向きの矢印・1ターン後）
                if (args[i] == "-capturecopylab" && args[i + 1] == "3") stage = -6; // 【検証中】Copy Core V3：タイトル・研究室・各部屋・コピー中心 ACT1 の入口
                if (args[i] == "-capturepuzzle" && args[i + 1] == "1") stage = -7; // 【Puzzle v2】タイトル・パズル研究室・各部屋・ボスの段階・大きい手札・パズル中心 ACT1 v2 の入口
            }
            if (folder == null) return;
            var capture = new GameObject("DebugAutoCapture").AddComponent<DebugAutoCapture>();
            capture.folder = Path.GetFullPath(folder); // 【戦闘中心 v3】相対パスはデータのフォルダ基準になり保存できなかった
            capture.stageIndex = stage;
            capture.handMode = (HandMode)hand;
            capture.stagnation = stagnationOn == 1;
            capture.stallTurns = stall;
        }

        /// <summary>ステージごとの想定ルート（技の種類, 方向, 距離）。</summary>
        static List<(CommandType, Direction, int)[]> Route(int stage)
        {
            if (stage >= 7) // 【検証中】M4 以降：兵種・環境の予告と行動を撮るための汎用ルート（攻略ルートではない）
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Walk, Direction.Up, 1), (CommandType.Slash, Direction.Right, 0) },
                    new[] { (CommandType.Rush, Direction.Left, 2), (CommandType.Slash, Direction.Up, 0) },
                    new[] { (CommandType.Walk, Direction.Down, 1), (CommandType.Push, Direction.Right, 0), (CommandType.Slash, Direction.Left, 0) },
                };
            if (stage == 4) // M1 挟み撃ち：①一歩← 斬撃← 駆け足↓ ②斬撃↑ ③斬撃↑
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Walk, Direction.Left, 1), (CommandType.Slash, Direction.Left, 0), (CommandType.Rush, Direction.Down, 2) },
                    new[] { (CommandType.Slash, Direction.Up, 0) },
                    new[] { (CommandType.Slash, Direction.Up, 0) },
                };
            if (stage == 5) // M2 同士討ち：①一歩↓ 駆け足→ 斬撃↑ ②一歩↑ 駆け足↑ 斬撃← ③斬撃↑
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Walk, Direction.Down, 1), (CommandType.Rush, Direction.Right, 2), (CommandType.Slash, Direction.Up, 0) },
                    new[] { (CommandType.Walk, Direction.Up, 1), (CommandType.Rush, Direction.Up, 2), (CommandType.Slash, Direction.Left, 0) },
                    new[] { (CommandType.Slash, Direction.Up, 0) },
                };
            if (stage == 6) // M3 トゲと2体：①一歩← 駆け足→ 斬撃→ ②駆け足← 一歩→ 斬撃→
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Walk, Direction.Left, 1), (CommandType.Rush, Direction.Right, 2), (CommandType.Slash, Direction.Right, 0) },
                    new[] { (CommandType.Rush, Direction.Left, 2), (CommandType.Walk, Direction.Right, 1), (CommandType.Slash, Direction.Right, 0) },
                };
            if (stage == 2) // STAGE 3：①斬撃↑→押し出し↑→一歩↓ ②斬撃↑→押し出し↑
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Slash, Direction.Up, 0), (CommandType.Push, Direction.Up, 0), (CommandType.Walk, Direction.Down, 1) },
                    new[] { (CommandType.Slash, Direction.Up, 0), (CommandType.Push, Direction.Up, 0) },
                };
            if (stage == 1) // STAGE 2：3ターンのルート
                return new List<(CommandType, Direction, int)[]>
                {
                    new[] { (CommandType.Walk, Direction.Down, 1) },
                    new[] { (CommandType.Rush, Direction.Right, 3), (CommandType.Walk, Direction.Left, 1) },
                    new[] { (CommandType.Slash, Direction.Up, 0), (CommandType.Walk, Direction.Right, 1) },
                };
            var walkSlash = new[] { (CommandType.Walk, Direction.Right, 1), (CommandType.Slash, Direction.Right, 0) };
            return new List<(CommandType, Direction, int)[]> { walkSlash, walkSlash, walkSlash }; // STAGE 1
        }

        IEnumerator Start()
        {
            Application.runInBackground = true; // 撮影中に別の窓が前に出ても止まらない（ほかの解析と同時に撮ると止まった）
            Directory.CreateDirectory(folder);
            var turn = FindAnyObjectByType<TurnManager>();
            yield return null;
            if (stageIndex == -2) { yield return CaptureAct(turn); Application.Quit(); yield break; }
            if (stageIndex == -17) { yield return CaptureRC4(turn); Application.Quit(); yield break; } // 【RC4】
            if (stageIndex == -3) { yield return CaptureActLoss(turn); Application.Quit(); yield break; }
            if (stageIndex == -4) { yield return SmokeAct(turn, 12); Application.Quit(); yield break; }
            if (stageIndex == -8) { yield return SmokeAct(turn, 12, true); Application.Quit(); yield break; }
            if (stageIndex == -9) { yield return SmokeAct(turn, 12, false, true); Application.Quit(); yield break; }
            if (stageIndex == -10) { yield return CaptureCombat(turn); Application.Quit(); yield break; }
            if (stageIndex == -11) { yield return CaptureV4(turn); Application.Quit(); yield break; }
            if (stageIndex == -15) { yield return CaptureTutorial(turn); Application.Quit(); yield break; }
            if (stageIndex == -16) { yield return CaptureRooms(turn); Application.Quit(); yield break; }
            if (stageIndex == -14) { yield return CaptureFinal(turn); Application.Quit(); yield break; }
            if (stageIndex == -13) { yield return CaptureProposal(turn); Application.Quit(); yield break; }
            if (stageIndex == -12) { yield return SmokeAct(turn, smokeRuns, false, true, true); Application.Quit(); yield break; }
            if (stageIndex == -20) { yield return CaptureVideo(turn); Application.Quit(); yield break; } // 【RC7】
            if (stageIndex == -21) { yield return CaptureRC8(turn); Application.Quit(); yield break; } // 【RC8】
            if (stageIndex == -22) { yield return CaptureRC9(turn); Application.Quit(); yield break; } // 【RC9】
            if (stageIndex == -23 || stageIndex == -24) { yield return CaptureFestival(turn, stageIndex == -24); Application.Quit(); yield break; } // 【文化祭】
            if (stageIndex == -5) { yield return CaptureCopyLab(turn); Application.Quit(); yield break; }
            if (stageIndex == -6) { yield return CaptureCopyLabV3(turn); Application.Quit(); yield break; }
            if (stageIndex == -7) { yield return CapturePuzzle(turn); Application.Quit(); yield break; }
            if (stageIndex < 0) { yield return CaptureRun(turn); Application.Quit(); yield break; }
            turn.CurrentHandMode = handMode;
            turn.StagnationDebugOn = stagnation; // 【検証用】鏡蝕ゲージ
            turn.LoadStage(stageIndex);
            yield return new WaitForSeconds(1f);
            yield return Shot("1_start");

            bool large = turn.IsDeckMode && turn.Deck.IsLarge;
            if (large) yield return ShotDiagonalPicker(turn);

            var route = Route(stageIndex);
            // 【検証用】-capturestall N：最初に N ターン「斬撃↑」（M1〜M3 では盤面が何も変わらない待機）を入れて、鏡蝕ゲージと呪いを撮る
            for (int i = 0; i < stallTurns; i++) route.Insert(0, new[] { (CommandType.Slash, Direction.Up, 0) });
            for (int t = 0; t < route.Count && turn.State == BattleState.PlayerInput; t++)
            {
                int planned = 0;
                var steps = large ? LargeHandRoute(turn, t) : route[t];
                foreach (var (type, dir, dist) in steps)
                {
                    if (turn.IsDeckMode)
                    {
                        // 手札方式 A/B：手札にある同じ種類のカードを使う（無ければその命令は飛ばす）
                        CardInstance card = null;
                        foreach (var c in turn.Deck.Hand)
                            if (c.Data.type == type && !turn.IsCardPlanned(c)) { card = c; break; }
                        if (card == null) continue;
                        if (turn.AddCommand(new CommandRecord(card.Data, dir, dist, card))) planned++;
                    }
                    else if (turn.AddCommand(new CommandRecord(turn.CurrentStage.availableCommands.Find(c => c.type == type), dir, dist))) planned++;
                }
                if (planned == 0) break;
                yield return null; // 予告（結果の予測）は次のフレームの Update で作られるので1フレーム待つ
                yield return Shot($"t{t + 1}_a_registered");

                turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting) yield return null;
                if (turn.State == BattleState.EnemyExecuting && turn.EnemyInnateAction != null)
                {
                    while (!turn.IsExecutingInnate && turn.State == BattleState.EnemyExecuting) yield return null;
                    yield return new WaitForSeconds(0.1f);
                    yield return Shot($"t{t + 1}_b_enemy_innate");
                }
                if (turn.Enemies.Count > 1)
                {
                    // 敵が複数：敵ごとに「最後の命令を実行中」の場面を撮る（敵A → 敵B の順）
                    foreach (var enemy in turn.Enemies)
                    {
                        if (!enemy.IsAlive) continue; // 倒れている敵は動かない
                        while (turn.State == BattleState.EnemyExecuting && !(turn.ActingEnemy == enemy && turn.ExecutingIndex == planned - 1)) yield return null;
                        if (turn.State != BattleState.EnemyExecuting) break;
                        yield return new WaitForSeconds(0.15f);
                        yield return Shot($"t{t + 1}_c_enemy{enemy.Model.Label}_last_command");
                    }
                }
                else
                {
                    while (turn.State == BattleState.EnemyExecuting && turn.ExecutingIndex != planned - 1) yield return null;
                    yield return new WaitForSeconds(0.15f);
                    yield return Shot($"t{t + 1}_c_enemy_last_command");
                }
                while (turn.State != BattleState.PlayerInput && turn.State != BattleState.Cleared && turn.State != BattleState.GameOver) yield return null;
                yield return new WaitForSeconds(0.4f);
            }
            yield return new WaitForSeconds(1f);
            yield return Shot("9_end");
            Application.Quit();
        }

        /// <summary>
        /// 【検証中】-captureact 1：ACT1 を1周して各画面を撮る（タイトル → 役職 → 扉 → 戦闘 → 報酬 → 商人／休息／特殊 → … → ボス → 結果）。
        /// 戦闘は1ターンだけ遊んでから勝敗を強制する（撮影のため。ゲーム本体の挙動ではない）。扉は階ごとに左右を交互に選ぶ。
        /// </summary>
        /// <summary>
        /// 【検証中】RC の確認：ACT1 を「でたらめに」通しで遊ぶ（役職を変えて runs 回）。手札から無作為に命令を組んで実行、報酬・商人・休息・特殊部屋も無作為。
        /// 25 ターンで終わらない戦闘だけ勝ちを強制する。目的は「普通の操作の組み合わせで例外・進行不能が出ないか」（ゲームの強さは測らない）。
        /// </summary>
        static int smokeRuns = 12;

        IEnumerator SmokeAct(TurnManager turn, int runs, bool puzzle = false, bool combat = false, bool v4 = false)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var rng = new System.Random(777);
            var dirs = new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right };
            int battles = 0, turnsPlayed = 0, forced = 0, wins = 0, losses = 0, clears = 0, restarts = 0, tidies = 0, giveUps = 0, saveChecks = 0, tacticUsed = 0, extraBuys = 0, uiTacticClicks = 0, smokeUpgrades = 0, smokeRetries = 0, smokeImprints = 0;
            var roles = v4 ? ActContent.Roles.Where(r => V4Content.RoleIds.Contains(r.Id)).ToArray() : combat ? ActContent.Roles.Where(r => ActContent.CombatRoleIds.Contains(r.Id)).ToArray() : ActContent.Roles;
            if (combat) Time.timeScale = 8f; // 【戦闘中心 v3】確認だけなので演出の待ち時間を縮める（1ランに10分以上かかった）
            // 【戦闘中心 v3】例外・エラーを数えて、1ランごとにフォルダへ書く（途中で止められても結果が残る）
            int errors = 0; string firstError = null;
            Application.LogCallback onLog = (msg, stack, type) => { if (type == LogType.Exception || type == LogType.Error) { errors++; if (firstError == null) firstError = msg + " / " + stack; } };
            Application.logMessageReceived += onLog;
            string summaryPath = Path.Combine(folder, "smoke_summary.txt");
            for (int run = 0; run < runs; run++)
            {
                act.OpenTitle();
                act.StartRun(roles[run % roles.Length], 1000 + run, puzzle || combat, puzzle, combat, v4);
                yield return null;
                if (act.Screen == ActScreen.Boon)
                {
                    var opts = act.Run.BoonOptions;
                    if (opts.Count > 0 && rng.Next(4) > 0) { var b = opts[rng.Next(opts.Count)]; act.ChooseBoon(b, b.Kind == BoonKind.Trim ? rng.Next(act.Run.Deck.Count) : -1); }
                    if (act.Screen == ActScreen.Boon) act.SkipBoon();
                }
                int guard = 0;
                while (act.Screen == ActScreen.Doors && guard++ < 20)
                {
                    var doors = act.Run.Doors;
                    act.ChooseDoor(doors[rng.Next(doors.Count)]);
                    yield return new WaitForSeconds(0.2f);
                    if (act.Screen == ActScreen.Battle)
                    for (int attempt = 0; attempt < 2 && act.Screen == ActScreen.Battle; attempt++) // 【RC7】やり直し（中ボス・ボス）も通す
                    {
                        battles++;
                        int t = 0;
                        int cap = run % 2 == 0 ? 25 : 8; // 偶数のランは本当に勝ち負けまで、奇数のランは 8 ターンで勝ちを強制して先の階（商人・休息・ボス）まで通す
                        while (turn.State != BattleState.Cleared && turn.State != BattleState.GameOver && t < cap && act.Screen == ActScreen.Battle)
                        {
                            while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                            yield return null; // ActRunManager.Update（試練のターン切れ・セーブ）を先に回す
                            if (turn.State != BattleState.PlayerInput || act.Screen != ActScreen.Battle) break;
                            if (v4 && turn.Tactics != null && turn.Tactics.Hand.Count >= 2 && rng.Next(3) == 0)
                            {
                                // 【RC6】手札の欄を描いている途中で、最初の緑の札を押す（本人の実プレイで出た ArgumentOutOfRangeException の再現）
                                int idx = -1; for (int q = 0; q < turn.Tactics.Hand.Count; q++) { var hc = turn.Tactics.Hand[q]; if (TacticRules.TargetOf(hc.tactic) == TacticTarget.None && hc.tactic != TacticKind.Sift && turn.CanPlayTactic(hc)) { idx = q; break; } }
                                if (idx >= 0 && idx < turn.Tactics.Hand.Count - 1) { BattleUI.DebugClickTacticIndex = idx; uiTacticClicks++; yield return null; yield return null; BattleUI.DebugClickTacticIndex = -1; }
                            }
                            if (v4 && turn.Tactics != null)
                            {
                                // 【Combat V4】戦術札をでたらめに（対象も無作為）。使えないものは飛ばす
                                for (int tp = 0; tp < 2 && turn.Tactics.Hand.Count > 0 && rng.Next(3) > 0; tp++)
                                {
                                    var tcard = turn.Tactics.Hand[rng.Next(turn.Tactics.Hand.Count)];
                                    var live = turn.Enemies.Where(e => e.IsAlive).ToList();
                                    UnitController a1 = live.Count > 0 ? live[rng.Next(live.Count)] : null, a2 = live.Count > 1 ? live[rng.Next(live.Count)] : null;
                                    if (turn.PlayTactic(tcard, a1, a2)) tacticUsed++;
                                    if (turn.PendingSift) { if (rng.Next(2) == 0) turn.SiftCards(turn.Deck.Hand.Where(c => !turn.IsCardPlanned(c)).Take(2).ToList()); else turn.CancelSift(); }
                                }
                            }
                            if (combat)
                            {
                                // 【戦闘中心 v3】手札整理・試練をあきらめる・部屋の途中のセーブの往復（例外が出ないか）
                                if (turn.CanTidy && rng.Next(3) == 0 && turn.Tidy(turn.Deck.Hand.Where(c => !turn.IsCardPlanned(c)).Take(1 + rng.Next(2)).ToList()) > 0) tidies++;
                                if (act.CanGiveUpTrial && rng.Next(5) == 0) { act.GiveUpTrial(); giveUps++; break; }
                                var json = JsonUtility.ToJson(ActSave.CaptureRoom(act.Run, "room", turn.Player.Hp));
                                if (!ActSave.TryRestore(json, act.Card, out _, out var err)) Debug.LogError("[DebugAutoCapture] セーブの往復に失敗：" + err);
                                saveChecks++;
                            }
                            t++;
                            if (run % 2 == 1 && turn.Player.Hp <= (v4 ? 19 : 6)) turn.HealPlayer(20); // V4 は敵が強いので、奇数のランは毎ターン満タン（先の階・ボスまで通す） // 奇数のランは倒れないように（撮影・確認のためだけ）
                            while (turn.PendingForesight > 0 && turn.ForesightChoices.Count > 0) turn.ChooseForesight(rng.Next(2) == 0 ? turn.ForesightChoices[0] : null);
                            while (turn.PendingRecycle > 0) turn.ChooseRecycle(turn.RecycleChoices.Count > 0 && rng.Next(2) == 0 ? turn.RecycleChoices[0] : null);
                            if (puzzle && t == 3 && rng.Next(4) == 0 && act.RestartBattle()) restarts++; // 【Puzzle v2】やり直し
                            if (act.Run.Items.Count > 0 && rng.Next(5) == 0)
                            {
                                act.UseItem(act.Run.Items[rng.Next(act.Run.Items.Count)]);
                                if (act.PendingItem == PendingItemUse.Hook && turn.Deck.DiscardPile.Count > 0) act.ChooseHookCard(turn.Deck.DiscardPile[0]);
                                if (act.PendingItem != PendingItemUse.None) act.CancelItem();
                            }
                            int want = 1 + rng.Next(Mathf.Max(1, turn.MaxSlots));
                            for (int k = 0; k < want; k++)
                            {
                                var hand = turn.Deck.Hand.Where(c => turn.CanAddCard(c)).ToList();
                                if (hand.Count == 0) break;
                                var card = hand[rng.Next(hand.Count)];
                                int dist = card.Data.minDistance + rng.Next(Mathf.Max(1, card.Data.maxDistance - card.Data.minDistance + 1));
                                turn.AddCommand(new CommandRecord(card.Data, dirs[rng.Next(4)], dist, card));
                            }
                            if (act.Run.Items.Contains(ItemId.BlankStone) && turn.PlannedCommands.Count > 0 && rng.Next(4) == 0)
                            {
                                act.UseItem(ItemId.BlankStone);
                                if (act.PendingItem == PendingItemUse.BlankStone) act.ChooseBlankSlot(0);
                            }
                            if (!turn.CanExecute) break;
                            turn.ExecuteTurn();
                            yield return null;
                        }
                        turnsPlayed += t;
                        while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                        yield return null;
                        if (act.Screen == ActScreen.Battle)
                        {
                            if (turn.State == BattleState.PlayerInput) { act.ForceWinForCapture(); forced++; }
                            yield return new WaitForSeconds(0.2f);
                            if (turn.State == BattleState.Cleared) wins++; else losses++;
                            act.ProceedAfterBattle();
                        }
                        yield return null;
                        if (act.PendingBossRetry && rng.Next(2) == 0) { act.RetryBossNow(); smokeRetries++; yield return new WaitForSeconds(0.2f); continue; } // 【RC7】中ボスでもやり直す
                        if (act.Screen == ActScreen.Reward)
                        {
                            var run0 = act.Run;
                            while (run0.RewardPicksLeft > 0 && run0.RewardCards.Count > 0 && rng.Next(4) > 0) run0.TakeRewardCard(run0.RewardCards[rng.Next(run0.RewardCards.Count)]);
                            if (v4 && run0.RewardPicksLeft == 0 && run0.ExtraBuysLeft > 0 && run0.RewardCards.Count > 0 && rng.Next(2) == 0 && run0.BuyRewardCard(run0.RewardCards[rng.Next(run0.RewardCards.Count)])) extraBuys++;
                            if (run0.RewardImprints.Count > 0 && rng.Next(2) == 0 && run0.TakeRewardImprint(run0.RewardImprints[0])) smokeImprints++;
                            if (run0.RewardUpgradesLeft > 0 && rng.Next(3) > 0 && SmokeUpgrade(run0, rng, false)) { run0.UseRewardUpgrade(); smokeUpgrades++; } // 【RC6】中ボスの無料の強化
                            act.NextFloor();
                        }
                    }
                    else if (act.Screen == ActScreen.Shop)
                    {
                        var r = act.Run;
                        foreach (var sc in r.ShopCards.ToList()) if (rng.Next(2) == 0) r.BuyCard(sc);
                        foreach (var si in r.ShopItems.ToList()) if (rng.Next(2) == 0) r.BuyItem(si);
                        if (rng.Next(3) == 0 && r.BuyImprint()) smokeImprints++;
                        if (rng.Next(2) == 0) r.RemoveCardPaidAt(rng.Next(r.Deck.Count));
                        if (rng.Next(2) == 0 && SmokeUpgrade(r, rng, true)) smokeUpgrades++; // 【RC6】商人の強化
                        act.NextFloor();
                    }
                    else if (act.Screen == ActScreen.Rest)
                    {
                        for (int k = 0; k < 2 && !act.Run.RestFinished; k++) // 【RC6】回復・強化・削除から2つ
                        {
                            int pick = rng.Next(3);
                            if (pick == 0 && act.Run.CanRest("heal")) { act.Run.RestHeal(); act.Run.MarkRest("heal"); }
                            else if (pick == 1 && act.Run.CanRest("upgrade") && SmokeUpgrade(act.Run, rng, false)) { act.Run.MarkRest("upgrade"); smokeUpgrades++; }
                            else if (act.Run.CanRest("remove") && act.Run.RestRemoveAt(rng.Next(act.Run.Deck.Count))) act.Run.MarkRest("remove");
                        }
                        act.NextFloor();
                    }
                    else if (act.Screen == ActScreen.Event)
                    {
                        var r = act.Run;
                        if (r.EventCards.Count > 0 && rng.Next(2) == 0) r.MirrorTake(r.EventCards[0]);
                        else if (r.RewardImprints.Count > 0 && rng.Next(2) == 0) r.AltarImprint(r.RewardImprints[0]);
                        else if (r.Current != null && r.Current.Id == "event_trade") r.AltarShards();
                        act.NextFloor();
                    }
                    yield return null;
                }
                if (act.Run != null && act.Run.Cleared) clears++;
                yield return Shot($"s{run:00}_{act.Screen.ToString().ToLower()}");
                File.WriteAllText(summaryPath, $"ラン {run + 1}/{runs}・戦闘 {battles}（勝ち {wins}・負け {losses}・強制 {forced}）・ターン {turnsPlayed}・クリア {clears}・手札整理 {tidies}・試練をあきらめた {giveUps}・セーブの往復 {saveChecks}・戦術札 {tacticUsed}（手札の欄で押した {uiTacticClicks}）・追加購入 {extraBuys}・強化 {smokeUpgrades}・刻印 {smokeImprints}・やり直し {smokeRetries}・例外/エラー {errors}\n{firstError}");
            }
            Application.logMessageReceived -= onLog;
            Debug.Log($"[DebugAutoCapture] でたらめ通し{(combat ? "（戦闘中心 v3）" : puzzle ? "（パズル中心）" : "")}：ラン {runs}・戦闘 {battles}（勝ち {wins}・負け {losses}・ターン上限で強制 {forced}）・ターン {turnsPlayed}・クリア {clears}・やり直し {restarts}・手札整理 {tidies}・試練をあきらめた {giveUps}・セーブの往復 {saveChecks}");
        }

        /// <summary>
        /// 【Combat V4】-capturev4 1：画面一式（1280×720 で読めるか・大技の予告・戦術札・ボス・報酬・商人・結果）。
        /// 外部テスト版のタイトル → 遊び方 → 役職 → 扉 → 各階（戦闘の始め・大技の予告・戦術札の対象選び・報酬）→ 研究室の各部屋 → ボス → 結果。
        /// </summary>
        IEnumerator CaptureV4(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            ScreenFx.Disabled = true;
            BuildInfo.SetForTest(true);
            act.OpenTitle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("v00_title");
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, true);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("v01_howto");
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, false);
            act.OpenV4RunRoleSelect();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("v02_roles");
            act.StartRun(ActContent.Roles[0], 4242, true, false, true, true);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) { yield return Shot("v03_boon"); act.SkipBoon(); }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("v04_doors_f1");
            for (int f = 0; f < 7 && act.Screen == ActScreen.Doors; f++)
            {
                if (f > 0) yield return Shot($"v05_doors_f{f + 1}");
                var room = (f == 2 || f == 5) ? act.Run.Doors.FirstOrDefault(d => d.IsMidBoss) ?? act.Run.Doors[0] : act.Run.Doors[f % act.Run.Doors.Count];
                act.ChooseDoor(room);
                yield return new WaitForSeconds(0.8f);
                if (act.Screen == ActScreen.Battle)
                {
                    yield return Shot($"v06_f{f + 1}_{room.Id}_start");
                    // ①の大技の予告を見せる（撮影のため、いちばん近い時計を①にする）
                    var shooter = turn.Grid.Model.Enemies.FirstOrDefault(e => e.IsAlive && e.Clocks.Count > 0);
                    if (shooter != null)
                    {
                        var c = Clocks.Nearest(shooter);
                        c.Cd = 1; Clocks.Telegraph(turn.Grid.Model, shooter, c);
                        yield return new WaitForSeconds(0.3f);
                        yield return Shot($"v07_f{f + 1}_{room.Id}_warn");
                    }
                    // 戦術札：対象を選ぶ（早鐘など、対象のある札）
                    var tgt = turn.Tactics != null ? turn.Tactics.Hand.FirstOrDefault(cd => TacticRules.TargetOf(cd.tactic) != TacticTarget.None) : null;
                    if (tgt != null)
                    {
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, tgt);
                        yield return null;
                        yield return Shot($"v08_f{f + 1}_{room.Id}_target");
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, null);
                    }
                    act.ForceWinForCapture();
                    yield return new WaitForSeconds(0.3f);
                    turn.Player.Model.Hp = Mathf.Max(3, turn.Player.Model.Hp - 7);
                    act.ProceedAfterBattle();
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"v09_f{f + 1}_reward");
                    act.NextFloor();
                }
                else
                {
                    yield return Shot($"v06_f{f + 1}_{room.Kind.ToString().ToLower()}");
                    if (room.Kind == RoomKind.Shop) { typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, true); yield return null; yield return Shot($"v06_f{f + 1}_deck"); typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, false); }
                    act.NextFloor();
                }
                yield return new WaitForSeconds(0.3f);
            }
            act.AbandonRun();
            BuildInfo.SetForTest(null);
            // 研究室：各部屋（始め・①の予告・札を選んだ・1ターン後）
            act.OpenV4Lab();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("v10_lab");
            int n = 0;
            foreach (var (index, st) in act.LabStagesV3())
            {
                n++;
                act.StartLabV4(index);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"v{n + 10:00}_{st.buttonLabel}_1_start");
                if (n == 1)
                {
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, true);
                    yield return null;
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_1b_legend");
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, false);
                }
                var shooter = turn.Grid.Model.Enemies.FirstOrDefault(e => e.IsAlive && e.Clocks.Count > 0);
                if (shooter != null)
                {
                    var c = Clocks.Nearest(shooter);
                    c.Cd = 1; Clocks.Telegraph(turn.Grid.Model, shooter, c);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_2_warn");
                }
                var card = turn.Deck.Hand.FirstOrDefault(c2 => c2.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                if (card != null && ui != null)
                {
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Up;
                    yield return null;
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_3_card");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
                var walk = turn.Deck.Hand.FirstOrDefault(c2 => c2.Data.type == CommandType.Rush || c2.Data.type == CommandType.Walk);
                var slash = turn.Deck.Hand.FirstOrDefault(c2 => c2.Data.type == CommandType.Slash);
                if (slash != null) turn.AddCommand(new CommandRecord(slash.Data, Direction.Up, 0, slash));
                if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Left, walk.Data.minDistance, walk));
                var bell = turn.Tactics != null ? turn.Tactics.Hand.FirstOrDefault(cd => cd.tactic == TacticKind.Focus || cd.tactic == TacticKind.Guard) : null;
                if (bell != null) turn.PlayTactic(bell);
                yield return null;
                yield return Shot($"v{n + 10:00}_{st.buttonLabel}_4_planned");
                if (turn.CanExecute) turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"v{n + 10:00}_{st.buttonLabel}_5_after");
                var lord = turn.Grid.Model.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord && e.IsAlive);
                if (lord != null && turn.State == BattleState.PlayerInput)
                {
                    RulesEngine.ApplyDamage(turn.Grid.Model, turn.Player.Model, null, false, lord, lord.Hp - CombatBoss.Floor(lord, 0), DamageKind.Direct, new List<BattleEvent>());
                    PhaseRules.EnvironmentPhase(turn.Grid.Model, new List<BattleEvent>(), null);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_6_phase2");
                    RulesEngine.ApplyDamage(turn.Grid.Model, turn.Player.Model, null, false, lord, lord.Hp - CombatBoss.Floor(lord, 1), DamageKind.Direct, new List<BattleEvent>());
                    lord.SlamCd = 1;
                    lord.SlamCells = CombatBoss.ChargeShape(turn.Grid.Model, lord.Position);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_7_phase3");
                }
                if (n == 1 && turn.State == BattleState.PlayerInput && turn.Tactics != null)
                {
                    var sift = Card("Tactic_Sift");
                    typeof(BattleUI).GetField("tidyMode", flags).SetValue(ui, true);
                    yield return null;
                    yield return Shot($"v{n + 10:00}_{st.buttonLabel}_8_tidy");
                    typeof(BattleUI).GetField("tidyMode", flags).SetValue(ui, false);
                }
            }
            ScreenFx.Disabled = false;
            // 結果画面（負け）
            act.StartRun(ActContent.Roles[0], 4243, true, false, true, true);
            yield return null;
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(0.6f);
            act.ForceLoseForCapture();
            yield return new WaitForSeconds(0.3f);
            act.ProceedAfterBattle();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("v90_result_loss");
        }

        /// <summary>
        /// 【完成版・2026-10-20】-capturefinal 1：完成版の画面一式（はじめての人の状態から）。
        /// タイトル → 遊び方 → 役職 → 旅立ち → 1階の扉（鏡の歩兵）→ 戦闘の始め → 危険の一言 → 報酬 → … → 休息・鍛える → 中ボス → ボス → 負け → 再挑戦の画面 → 再挑戦 → クリアの結果（解放）→ タイトル（記録）→ 役職（✓・挑戦）。
        /// 撮影ではセーブもプロフィールも保存しない（SaveEnabled=false）。勝敗は強制。
        /// </summary>
        /// <summary>
        /// 【RC3】チュートリアルを決めた手で最後まで遊ぶ：はじめての案内 → 6つの段階（始め・組んだ命令・できた！）→ 完了の画面。
        /// どの段階も「できた！」にならなければ tutorial_summary.txt に書く（自動の確認）。
        /// </summary>
        IEnumerator CaptureTutorial(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            BuildInfo.SetForTest(true);
            ScreenFx.Disabled = true;
            var summary = new System.Text.StringBuilder();
            act.OpenTitle();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("t00_title_first_choice");
            act.StartTutorial();
            yield return new WaitForSeconds(0.6f);
            // 【RC4】段階ごとのターン（敵の動きは2ターン：近づいてくる → 斬る）
            var plans = new List<(CommandType t, Direction d)[][]>
            {
                new[] { new[] { (CommandType.Walk, Direction.Up), (CommandType.Walk, Direction.Up) } },  // 1 移動：出口まで2マス
                new[] { new[] { (CommandType.Walk, Direction.Up), (CommandType.Slash, Direction.Up) } }, // 2 攻撃
                new[] { new[] { (CommandType.Slash, Direction.Right) } },                                // 3 まね：左の兵がまねて右の兵を斬る
                new[] { new[] { (CommandType.Push, Direction.Up) } },                                    // 4 【RC8】動かずに、剣兵を押して赤をずらす
                new[] { new[] { (CommandType.Slash, Direction.Up) }, new[] { (CommandType.Slash, Direction.Up), (CommandType.Slash, Direction.Up) } }, // 5 敵の動き：追跡兵が隣へ → 2回斬る
                new[] { new[] { (CommandType.Slash, Direction.Up) } },                                   // 6 集中 → 斬撃
                new[] { new[] { (CommandType.Walk, Direction.Right) } },                                 // 7 まねで兵を赤紫へ
            };
            for (int s = 0; s < plans.Count; s++)
            {
                yield return new WaitForSeconds(0.3f);
                yield return Shot($"t{s + 1}_a_start");
                var def = TutorialContent.Steps[s];
                if (def.Goal == TutorialGoal.Tactic && turn.Tactics != null && turn.Tactics.Hand.Count > 0) { turn.PlayTactic(turn.Tactics.Hand[0]); yield return new WaitForSeconds(0.3f); }
                for (int k = 0; k < plans[s].Length && !act.TutorialStepDone; k++)
                {
                    foreach (var (t, d) in plans[s][k])
                    {
                        var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == t && turn.CanAddCard(c));
                        if (card == null) { summary.AppendLine($"段階{s + 1}：手札に {t} が無い"); continue; }
                        turn.AddCommand(new CommandRecord(card.Data, d, card.Data.minDistance, card));
                    }
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot($"t{s + 1}_b{k}_planned");
                    if (turn.CanExecute) turn.ExecuteTurn();
                    float t0 = Time.time;
                    while ((turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) && Time.time - t0 < 20f) yield return null;
                    yield return new WaitForSeconds(0.6f);
                    yield return Shot($"t{s + 1}_c{k}_result");
                }
                summary.AppendLine($"段階{s + 1} {def.Name}：{(act.TutorialStepDone ? "できた" : "できていない（" + act.TutorialFeedback + "）")}");
                if (!act.TutorialStepDone) break;
                act.NextTutorialStep();
            }
            // 【RC4】8番：1ランの流れ（報酬 → 扉 → 8階の地図）
            if (act.Screen == ActScreen.TutorialRun)
            {
                yield return new WaitForSeconds(0.3f);
                yield return Shot("t8_a_reward");
                act.TutorialRunPickCard(0);
                yield return null;
                yield return Shot("t8_b_reward_picked");
                act.TutorialRunNext();
                yield return null;
                yield return Shot("t8_c_doors");
                act.TutorialRunPickDoor(0);
                yield return null;
                yield return Shot("t8_d_door_picked");
                act.TutorialRunNext();
                yield return null;
                yield return Shot("t8_e_map");
                act.TutorialRunNext();
                summary.AppendLine("段階8 1ランの流れ：3頁");
            }
            yield return new WaitForSeconds(0.6f);
            yield return Shot("t99_done");
            summary.AppendLine($"最後の画面：{act.Screen}");
            File.WriteAllText(Path.Combine(folder, "tutorial_summary.txt"), summary.ToString());
        }

        /// <summary>
        /// 【RC4・2026-10-23】-capturerc4 1：本人の RC3 プレイで分かった問題が直ったかを、はじめての人の状態から撮る。
        /// タイトル（はじめての案内）→ はじめてのラン 1階（まねだけ・方向の入力）→ 2階（赤と敵の動き・オレンジの矢印・敵の詳細・あなたを盤面の端へ置いて方向の入力・
        /// 前のターンのダメージの原因・転移石の行き先）→ 3階（緑の札のおさらい・系統の帯）→ 5階（追跡兵）。勝敗は強制。結果は rc4_summary.txt にも。
        /// </summary>
        IEnumerator CaptureRC4(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var actUi = FindAnyObjectByType<ActUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var kbFocus = typeof(ActUI).GetField("kbFocus", flags); // 【RC5】キーボードの選択（白の枠）を撮る
            var summary = new System.Text.StringBuilder();
            summary.AppendLine($"画面 {Screen.width}x{Screen.height}");
            BuildInfo.SetForTest(true);
            ScreenFx.Disabled = true;
            act.OpenTitle();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("r00_title_first_choice");
            act.DeclineTutorial();
            act.OpenV4RunRoleSelect();
            act.StartRun(ActContent.Roles.First(r => r.Id == "standard"), 7001);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) { yield return Shot("r01_boon"); act.SkipBoon(); }
            summary.AppendLine($"はじめのアイテム：{string.Join("・", act.Run.Items.Select(i => ActContent.Item(i).Name))}");
            IEnumerator Dir(string name, Direction d)
            {
                var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk) ?? turn.Deck.Hand.FirstOrDefault();
                if (card == null) yield break;
                typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                typeof(BattleUI).GetField("pendingCard", flags)?.SetValue(ui, card);
                ui.DebugFocus = d;
                yield return null; yield return null;
                yield return Shot(name);
                ui.DebugFocus = null;
                typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                typeof(BattleUI).GetField("pendingCard", flags)?.SetValue(ui, null);
            }
            for (int f = 0; f < 6 && act.Run != null && !act.Run.Cleared; f++)
            {
                if (act.Screen != ActScreen.Doors) break;
                yield return Shot($"r1{f}_doors");
                if (f == 1 && kbFocus != null && actUi != null) { kbFocus.SetValue(actUi, 1); yield return null; yield return null; yield return Shot("r1k_doors_keyboard"); kbFocus.SetValue(actUi, -1); }
                var doors = act.Run.Doors;
                var room = doors.FirstOrDefault(d => d.Id == "v4_n4") ?? doors.FirstOrDefault(d => d.Id == "v4_t4") ?? doors.FirstOrDefault(d => d.Kind == RoomKind.Battle || d.Kind == RoomKind.Tactical) ?? doors[0];
                act.ChooseDoor(room);
                yield return new WaitForSeconds(0.8f);
                if (act.Screen != ActScreen.Battle) { act.NextFloor(); yield return new WaitForSeconds(0.3f); continue; }
                var m = turn.Grid.Model;
                summary.AppendLine($"{f + 1}階 {room.Name}：大技 {(m.Enemies.Any(e => e.Clocks.Count > 0) ? "あり" : "なし")}・敵の固有行動 {(m.NoTraits ? "なし" : "あり")}・戦術札 {(turn.Tactics != null && turn.Tactics.Hand.Count > 0 ? turn.Tactics.Hand.Count + "枚" : "なし")}" +
                                   $"・自分で動く予告 {TurnForecast.Compute(m, new List<CommandRecord>()).OwnMoves.Count}");
                yield return Shot($"r2{f}_{room.Id}_start"); // 3階（はじめて緑の札を持つ戦闘）は「緑は命令とは別」の説明つき
                if (turn.Tactics != null && turn.Tactics.Hand.Count > 0) act.Profile.tacticIntroSeen = true; // 緑の札の説明は、はじめて持った戦闘で撮った（以後は盤面を隠さない）
                if (f == 0) yield return Dir("r30_dir_center", Direction.Up);
                if (f == 1)
                {
                    // 敵の詳細（このターンの流れ）
                    var foe = turn.Enemies.FirstOrDefault(e => e.IsAlive && PhaseRules.MovesOnItsOwn(e.Model)) ?? turn.Enemies.FirstOrDefault(e => e.IsAlive);
                    typeof(BattleUI).GetField("selectedEnemy", flags).SetValue(ui, foe);
                    yield return null;
                    yield return Shot("r31_enemy_detail");
                    typeof(BattleUI).GetField("selectedEnemy", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, true); // 【RC5】凡例
                    yield return null; yield return null;
                    yield return Shot("r31b_legend");
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, false);
                    // あなたを盤面の端・中央へ置いて、外向きの方向（盤面の外の当たり）
                    var b = m.Board;
                    var spots = new List<(string name, Vector2Int c, Direction d)>
                    {
                        ("left", new Vector2Int(0, b.Height / 2), Direction.Left), ("right", new Vector2Int(b.Width - 1, b.Height / 2), Direction.Right),
                        ("y0", new Vector2Int(b.Width / 2, 0), Direction.Down), ("ymax", new Vector2Int(b.Width / 2, b.Height - 1), Direction.Up),
                    };
                    var home = m.Player.Position;
                    foreach (var (name, c0, d) in spots)
                    {
                        var c = Enumerable.Range(0, b.Width * b.Height).Select(k => new Vector2Int(k % b.Width, k / b.Width))
                            .Where(x => (name == "left" ? x.x == 0 : name == "right" ? x.x == b.Width - 1 : name == "y0" ? x.y == 0 : x.y == b.Height - 1) && !m.IsBlocked(x))
                            .OrderBy(x => Mathf.Abs(x.x - c0.x) + Mathf.Abs(x.y - c0.y)).Cast<Vector2Int?>().FirstOrDefault();
                        if (c == null) continue;
                        m.Player.Position = c.Value;
                        yield return new WaitForSeconds(0.5f);
                        yield return Dir($"r32_dir_{name}", d);
                    }
                    m.Player.Position = home;
                    yield return new WaitForSeconds(0.4f);
                    // 前のターンのダメージの原因：赤のマスに立って、何もしないで1ターン
                    var red = ThreatMap.EnemyAttack(m).Where(x => !m.IsBlocked(x)).OrderBy(x => Mathf.Abs(x.x - home.x) + Mathf.Abs(x.y - home.y)).Cast<Vector2Int?>().FirstOrDefault();
                    if (red != null) { m.Player.Position = red.Value; yield return new WaitForSeconds(0.4f); }
                    yield return Shot("r33_before_hit");
                    // 転移石の行き先
                    act.UseItem(ItemId.WarpStone);
                    yield return null; yield return null;
                    yield return Shot("r34_warp_pick");
                    summary.AppendLine($"転移石：行き先 {RescueRules.Targets(m).Count} マス・選んでいる {act.PendingItem}");
                    // 【RC5】キーボードで行き先を選ぶ（Enter で一番近いマス → 矢印で右へ）
                    var hw = typeof(BattleUI).GetMethod("HandleWarpKey", flags);
                    if (hw != null)
                    {
                        hw.Invoke(ui, new object[] { KeyCode.Return });
                        hw.Invoke(ui, new object[] { KeyCode.RightArrow });
                        yield return null; yield return null;
                        yield return Shot("r34k_warp_keyboard");
                        summary.AppendLine($"転移石（キー）：選んでいるマス {typeof(BattleUI).GetField("kbWarpCell", flags).GetValue(ui)}・あなた {m.Player.Position}");
                    }
                    act.CancelItem();
                    var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                    if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Left, walk.Data.minDistance, walk));
                    if (turn.CanExecute) turn.ExecuteTurn();
                    float t0 = Time.time;
                    while ((turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) && Time.time - t0 < 20f) yield return null;
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("r35_after_hit");
                    summary.AppendLine($"1ターン後：HP {m.Player.Hp}/{m.Player.MaxHp}");
                }
                act.ForceWinForCapture();
                yield return new WaitForSeconds(0.3f);
                act.ProceedAfterBattle();
                yield return new WaitForSeconds(0.3f);
                if (act.Screen == ActScreen.Reward && f == 0)
                {
                    yield return Shot("r40_reward");
                    if (kbFocus != null && actUi != null) { kbFocus.SetValue(actUi, 0); yield return null; yield return null; yield return Shot("r41_reward_keyboard"); kbFocus.SetValue(actUi, -1); }
                }
                act.NextFloor();
                yield return new WaitForSeconds(0.3f);
            }
            File.WriteAllText(Path.Combine(folder, "rc4_summary.txt"), summary.ToString());
            ScreenFx.Disabled = false;
            BuildInfo.SetForTest(null);
        }

        IEnumerator CaptureFinal(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var danger = typeof(BattleUI).GetMethod("EndCellDanger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            BuildInfo.SetForTest(true);
            ScreenFx.Disabled = true;
            act.OpenTitle();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("f00_title_first");
            bool offeredBefore = act.Profile.tutorialOffered; // 【第2巡 14】f01 は はじめての窓を閉じたタイトル（前は f00 と同じ画像だった）
            act.Profile.tutorialOffered = true;
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, false);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("f01_title");
            act.Profile.tutorialOffered = offeredBefore;
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, true);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("f02_howto");
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, false);
            typeof(ActUI).GetField("showSettings", flags).SetValue(actUi, true); // 【RC5】設定（音量）も撮る
            yield return new WaitForSeconds(0.2f);
            yield return Shot("f02b_settings");
            typeof(ActUI).GetField("showSettings", flags).SetValue(actUi, false);
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, false);
            act.OpenV4RunRoleSelect();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("f03_roles_first");
            act.StartRun(ActContent.Roles[0], 5151);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) { yield return Shot("f04_boon"); act.SkipBoon(); }
            yield return new WaitForSeconds(0.3f);
            bool forged = false, dangerShot = false;
            for (int f = 0; f < 8 && act.Run != null && !act.Run.Cleared; f++)
            {
                if (act.Screen != ActScreen.Doors) break;
                yield return Shot($"f1{f}_doors");
                if (f == 1) { typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, true); yield return new WaitForSeconds(0.2f); yield return Shot("f1b_deck"); typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, false); } // 【RC5】デッキの一覧
                var doors = act.Run.Doors;
                var room = doors.FirstOrDefault(d => d.Id == "v4_i1") ?? doors.FirstOrDefault(d => d.IsMidBoss) ?? doors.FirstOrDefault(d => d.Kind == RoomKind.Rest && !forged) ?? doors.FirstOrDefault(d => d.Kind == RoomKind.Shop) ?? doors[0]; // 【RC5】商人も撮る
                act.ChooseDoor(room);
                yield return new WaitForSeconds(0.8f);
                if (act.Screen == ActScreen.Rest)
                {
                    yield return Shot($"f2{f}_rest");
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, "forge");
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, null);
                    yield return null;
                    yield return Shot($"f2{f}_forge");
                    var c = act.Run.Deck.FirstOrDefault(V4Content.CanForge);
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, c); // 【RC6】3つの形（自分＋・まね＋・まね－）
                    yield return null;
                    yield return Shot($"f2{f}_forge_kinds");
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, null);
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, null);
                    if (c != null) act.Run.UpgradeCard(c, V4Content.DefaultUpgrade(c)); // 【通常リリース版】撮影もゲームと同じ強化の形（前は いつも自分＋で、画面とずれた）
                    forged = true;
                    act.NextFloor();
                    yield return new WaitForSeconds(0.3f);
                    continue;
                }
                if (act.Screen == ActScreen.Shop && act.Run.AnyUpgradable) // 【RC6】商人の強化
                {
                    yield return Shot($"f2{f}_shop");
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, "forge_shop");
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, act.Run.Deck.FirstOrDefault(d => act.Run.UpgradeOf(d.name) == null && V4Content.CanUpgradeAny(d)));
                    yield return null;
                    yield return Shot($"f2{f}_shop_forge");
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, null);
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, null);
                    act.NextFloor(); yield return new WaitForSeconds(0.3f); continue;
                }
                if (act.Screen != ActScreen.Battle) { yield return Shot($"f2{f}_{act.Screen.ToString().ToLower()}"); act.NextFloor(); yield return new WaitForSeconds(0.3f); continue; }
                yield return Shot($"f3{f}_{room.Id}_start");
                if (f == 1) { act.BattleMenuOpen = true; yield return null; yield return Shot("f3m_battle_menu"); act.BattleMenuOpen = false; yield return null; } // 【RC5・レビュー3】戦闘中のメニュー
                act.Profile.tacticIntroSeen = true; // 【RC3】戦術札のはじめての説明は1回だけ撮る（以後の場面で盤面を隠さない）
                // 危険の一言：最後に立つマスが危ない手を探して並べる（1回だけ）
                if (!dangerShot && danger != null)
                {
                    foreach (var card in turn.Deck.Hand.Where(h => h.Data.type == CommandType.Walk || h.Data.type == CommandType.Rush).ToList())
                    {
                        foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                        {
                            var rec = new CommandRecord(card.Data, d, card.Data.minDistance, card);
                            if (danger.Invoke(null, new object[] { turn.Grid.Model, new List<CommandRecord> { rec } }) is string s && s.Length > 0)
                            {
                                turn.AddCommand(rec);
                                dangerShot = true;
                                break;
                            }
                        }
                        if (dangerShot) break;
                    }
                    if (dangerShot) { yield return null; yield return Shot($"f3{f}_{room.Id}_danger"); turn.ClearCommands(); }
                }
                if (room.Kind == RoomKind.Boss)
                {
                    act.ForceLoseForCapture();
                    yield return new WaitForSeconds(0.3f);
                    act.ProceedAfterBattle();
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("f80_boss_lost_retry");
                    act.RetryBossNow();
                    yield return new WaitForSeconds(0.8f);
                    yield return Shot("f81_boss_retry_start");
                    act.ForceWinForCapture();
                    yield return new WaitForSeconds(0.3f);
                    act.ProceedAfterBattle();
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot("f82_result_clear");
                    break;
                }
                act.ForceWinForCapture();
                yield return new WaitForSeconds(0.3f);
                turn.Player.Model.Hp = Mathf.Max(6, turn.Player.Model.Hp - 4);
                act.ProceedAfterBattle();
                yield return new WaitForSeconds(0.25f);
                if (f == 0) yield return Shot($"f4{f}a_reward_count"); // 【RC6】ルーン片の数え上げの途中
                yield return new WaitForSeconds(1.1f); // 【RC6】札が1枚ずつ出そろうまで
                yield return Shot($"f4{f}_reward");
                if (f == 0)
                {
                    // 入れ替え：命令札を取る → 入れ替えのボタン → 外す札を選ぶ画面
                    var cmd = act.Run.RewardCards.FirstOrDefault(r => !r.IsTactic);
                    if (cmd != null && act.Run.TakeRewardCard(cmd))
                    {
                        yield return null;
                        yield return Shot($"f4{f}b_reward_swap");
                        typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, "swap");
                        yield return null;
                        yield return Shot($"f4{f}c_swap_picker");
                        typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, null);
                    }
                }
                act.NextFloor();
                yield return new WaitForSeconds(0.3f);
            }
            act.OpenTitle();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("f90_title_after_clear");
            act.OpenV4RunRoleSelect();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("f91_roles_after_clear");
            act.Profile.challengeUnlocked = 1; // 【RC5・レビュー】前は挑戦が解放されていない画面（f91 と同じ）を撮っていた
            act.OpenV4RunRoleSelect();
            yield return new WaitForSeconds(0.2f);
            act.NextChallenge = 1;
            yield return new WaitForSeconds(0.2f);
            yield return Shot("f92_roles_challenge1");
            // 負けの結果（ボス以外）
            act.StartRun(ActContent.Roles[0], 5152);
            yield return null;
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(0.6f);
            act.ForceLoseForCapture();
            yield return new WaitForSeconds(0.3f);
            act.ProceedAfterBattle();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("f93_result_loss");
            ScreenFx.Disabled = false;
            BuildInfo.SetForTest(null);
        }

        // ───── 【企画書】企画書用の場面（2026-10-09）─────
        // 手順は tools/pzsearch の showcase で、本物のルールから探した（Logs/proposal/）。手札・戦術札は、撮影のため必要な札が手に来るように並べるだけ（ルールは変えない）。

        /// <summary>【企画書 v4】-captureproposal 2：山場の場面だけ撮る。</summary>
        static bool proposalHeroOnly, proposalSuper, proposalJobHunt, videoScenesOnly;

        IEnumerator CaptureProposal(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            ScreenFx.Disabled = true;
            TurnManager.FastForward = false;
            BuildInfo.SetForTest(true); // 外部テスト版と同じ画面（検証用のボタンを出さない）
            act.OpenV4Lab();
            yield return new WaitForSeconds(0.3f);
            // 【就活資料 2026-11-18】-captureproposal 3：コピーバトル（仮）の企画書・仕様書・動画用（1.0.0 のルールで pzsearch showcase copy/env/shift/bell/self が見つけた手順。Logs/jh/probe*_out.txt）
            if (proposalJobHunt)
            {
                yield return ProposalScene(turn, act, ui, flags, "StageX3", "j1_ff", new[] { new[] { ("Slash", Direction.Left, "") } }); // 盾の陰の砲兵：空振りの斬撃 → 砲兵C のまねが盾兵B に 4
                yield return ProposalScene(turn, act, ui, flags, "StageX2", "j2_ff", new[] { new[] { ("Thrust", Direction.Up, "") } }); // 十字砲火：空振りの突き → 剣兵B のまねが弓兵A に 3
                yield return ProposalScene(turn, act, ui, flags, "StageX7", "j3_rock", new[] { new[] { ("Walk", Direction.Down, ""), ("Dash", Direction.Right, "") } }); // 落石の広間：あなたは壁で止まり、盾兵A・剣兵B が岩の下へ
                yield return ProposalScene(turn, act, ui, flags, "StageX6", "j4_fire", new[] { new[] { ("Dash", Direction.Up, "") } }); // 動く炎床：同じ駆け足↑で剣兵B だけが炎へ
                yield return ProposalScene(turn, act, ui, flags, "StageX9", "j5_shift", new[] { new[] { ("Dash", Direction.Up, "") }, new[] { ("Push", Direction.Left, "") } }); // 処刑騎士：騎士を押す → 騎士の剣が癒し手C に 4
                yield return ProposalScene(turn, act, ui, flags, "StageX9", "j6_bell", new[] { new[] { ("Dash", Direction.Left, "") }, new[] { ("T:EarlyBell", Direction.Up, "A"), ("Pull", Direction.Up, "") } }); // 処刑騎士：早鐘 → 騎士の追撃が護衛2体に
                yield return ProposalScene(turn, act, ui, flags, "StageX4", "j7_self", new[] { new[] { ("Rush", Direction.Down, ""), ("+Whirl", Direction.Up, "") } }); // 追跡の群れ：回転斬り（報酬の札）を まねた追跡兵B があなたを斬る
                yield return ProposalScene(turn, act, ui, flags, "StageX4", "j8_good", new[] { new[] { ("Walk", Direction.Up, ""), ("+Whirl", Direction.Up, "") } }); // 追跡の群れ：同じ回転斬りでも、剣兵C のまねが追跡兵B に
                BuildInfo.SetForTest(null);
                ScreenFx.Disabled = false;
                yield break;
            }
            // 【企画書 v4】山場（最初のデッキで出せる手順だけ。pzsearch showcase multi で探した）
            // h3：処刑騎士（引き寄せ→ ｜ 斬撃→・斬撃→）：癒し手がまねた引き寄せで騎士が隣へ。あなたの斬撃は空振り、まねた斬撃で3体が互いに傷つく
            yield return ProposalScene(turn, act, ui, flags, "StageX9", "h3", new[] { new[] { ("Pull", Direction.Right, "") }, new[] { ("Slash", Direction.Right, ""), ("Slash", Direction.Right, "") } });
            // h4：追跡の群れ（突進→ ｜ 一歩↓・突き←）：まねた突きで3体が1回ずつ
            yield return ProposalScene(turn, act, ui, flags, "StageX4", "h4", new[] { new[] { ("Rush", Direction.Right, "") }, new[] { ("Walk", Direction.Down, ""), ("Thrust", Direction.Left, "") } });
            // h5：処刑騎士（一歩← ｜ 駆け足→・突き→）
            yield return ProposalScene(turn, act, ui, flags, "StageX9", "h5", new[] { new[] { ("Walk", Direction.Left, "") }, new[] { ("Dash", Direction.Right, ""), ("Thrust", Direction.Right, "") } });
            // h6：レーザー回廊（早鐘＠C・駆け足← ｜ 突き→）：大技・まね・レーザーで3体
            yield return ProposalScene(turn, act, ui, flags, "StageX8", "h6", new[] { new[] { ("T:EarlyBell", Direction.Up, "C"), ("Dash", Direction.Left, "") }, new[] { ("Thrust", Direction.Right, "") } });
            // w1：追跡の群れ（駆け足↑・回転斬り）：報酬で取った回転斬りで2体を斬る。技まねの追跡兵2体が同じ回転斬りをまねて、あなたを斬る
            yield return ProposalScene(turn, act, ui, flags, "StageX4", "w1", new[] { new[] { ("Dash", Direction.Up, ""), ("+Whirl", Direction.Up, "") } });
            // 【企画書 v6】w2：追跡の群れ（突進← ｜ 引き寄せ← ｜ 回転斬り）：まねる敵があなたの隣にいない並びなら、まねた回転斬りは敵どうしに当たる（w1 の逆の例）
            yield return ProposalScene(turn, act, ui, flags, "StageX4", "w2", new[] { new[] { ("Rush", Direction.Left, "") }, new[] { ("Pull", Direction.Left, "") }, new[] { ("+Whirl", Direction.Up, "") } });
            // 【企画書 v6】h7：槍の回廊（突進↑ ｜ 押し出し← ｜ 斬撃←）：3ターン目の空振りの斬撃1枚で、槍の向き・剣兵の剣と八方斬り・まねた斬撃が重なり、3体に7回当たる
            yield return ProposalScene(turn, act, ui, flags, "StageX5", "h7", new[] { new[] { ("Rush", Direction.Up, "") }, new[] { ("Push", Direction.Left, "") }, new[] { ("Slash", Direction.Left, "") } });
            if (proposalHeroOnly) { BuildInfo.SetForTest(null); ScreenFx.Disabled = false; yield break; }
            // P3：レーザー回廊（突進↑）：あなたは安全な行へ。移動をまねる盾兵・弓兵は同じ突進で、上の端のレーザーの行へ
            yield return ProposalScene(turn, act, ui, flags, "StageX8", "p3", new[] { new[] { ("Rush", Direction.Up, "") } });
            // P4：動く炎床（斬撃→）：あなたの斬撃は空振り。剣兵（全まね）の同じ斬撃が、近づいた槍兵に当たる
            yield return ProposalScene(turn, act, ui, flags, "StageX6", "p4", new[] { new[] { ("Slash", Direction.Right, "") } });
            // P6：時計仕掛けの砲王（駆け足← ｜ 早鐘＠剣兵C・駆け足↑）：早鐘で回転斬りを前倒し → 護衛と砲王本体に当たる
            yield return ProposalScene(turn, act, ui, flags, "StageXA", "p6", new[] { new[] { ("Dash", Direction.Left, "") }, new[] { ("T:EarlyBell", Direction.Up, "C"), ("Dash", Direction.Up, "") } });
            // P2（逆の例）：槍の回廊（斬撃↓・突き↑）：あなたの突きは剣兵に当たる。でも剣兵は、あなたには空振りの斬撃↓をまねて、あなたを斬る
            yield return ProposalScene(turn, act, ui, flags, "StageX5", "p2b", new[] { new[] { ("Slash", Direction.Down, ""), ("Thrust", Direction.Up, "") } });
            // 十字砲火（突進← ｜ 突き↑）：移動まねの弓兵は壁で動けず、全まねの剣兵だけが弓兵の下へ。次の「突き↑」を剣兵がまねて弓兵を突く
            yield return ProposalScene(turn, act, ui, flags, "StageX2", "p7", new[] { new[] { ("Rush", Direction.Left, "") }, new[] { ("Thrust", Direction.Up, "") } });
            // P5：落石の広間（一歩↓）：あなたは壁で止まる。剣兵だけが同じ一歩で落石の影へ
            yield return ProposalScene(turn, act, ui, flags, "StageX7", "p5", new[] { new[] { ("Walk", Direction.Down, "") } });
            BuildInfo.SetForTest(null);
            ScreenFx.Disabled = false;
        }

        IEnumerator ProposalScene(TurnManager turn, ActRunManager act, BattleUI ui, System.Reflection.BindingFlags flags, string stageName, string tag, (string card, Direction dir, string target)[][] turns)
        {
            var labs = act.LabStagesV3();
            int index = labs.FirstOrDefault(l => l.stage.name == stageName).index;
            act.StartLabV4(index);
            yield return new WaitForSeconds(2.2f); // 中ボスの登場帯が消えるまで
            yield return Shot($"{tag}_0_start");
            for (int t = 0; t < turns.Length; t++)
            {
                var plan = turns[t];
                // 必要な札を手へ（撮影のための並べ替えだけ）
                foreach (var step in plan)
                {
                    if (step.card.StartsWith("T:"))
                    {
                        var kind = (TacticKind)System.Enum.Parse(typeof(TacticKind), step.card.Substring(2));
                        if (!turn.Tactics.Hand.Any(c => c.tactic == kind)) ForceTacticInHand(turn.Tactics, kind);
                        continue;
                    }
                    if (step.card.StartsWith("+")) { var rc = Card(step.card.Substring(1)); if (rc != null && !turn.Deck.Hand.Any(c => c.Data == rc)) ForceCardInHand(turn.Deck, rc); continue; } // 【企画書 v4】報酬で取った札（デッキに入った状態）
                    int need = plan.Count(x => x.card == step.card), have = turn.Deck.Hand.Count(c => c.Data.name == step.card);
                    if (have < need) turn.Deck.DrawMatching(need - have, d => d.name == step.card); // 同じ札を2枚使う手順（最初のデッキにある枚数まで）
                }
                yield return null;
                int k = 0;
                foreach (var step in plan)
                {
                    k++;
                    if (step.card.StartsWith("T:"))
                    {
                        var kind = (TacticKind)System.Enum.Parse(typeof(TacticKind), step.card.Substring(2));
                        var tc = turn.Tactics.Hand.First(c => c.tactic == kind);
                        var target = turn.Enemies.First(e => e.Model.Label == step.target);
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, tc);
                        yield return null;
                        yield return Shot($"{tag}_t{t + 1}_{k}_tactic_target");
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, null);
                        turn.PlayTactic(tc, target);
                        yield return new WaitForSeconds(0.4f);
                        yield return Shot($"{tag}_t{t + 1}_{k}_tactic_played");
                        continue;
                    }
                    var card = turn.Deck.Hand.First(c => c.Data.name == step.card.TrimStart('+') && !turn.IsCardPlanned(c));
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = step.dir;
                    yield return null;
                    yield return new WaitForSeconds(0.1f);
                    yield return Shot($"{tag}_t{t + 1}_{k}_select");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                    turn.AddCommand(new CommandRecord(card.Data, step.dir, card.Data.minDistance, card));
                }
                yield return null;
                yield return Shot($"{tag}_t{t + 1}_8_planned");
                yield return ExecuteWithShots(turn, $"{tag}_t{t + 1}");
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"{tag}_t{t + 1}_9_after");
            }
        }

        static void ForceCardInHand(DeckState deck, CommandData data)
        {
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var hand = (List<CardInstance>)typeof(DeckState).GetField("hand", f).GetValue(deck);
            hand.Add(new CardInstance(data));
        }

        static void ForceTacticInHand(TacticDeck deck, TacticKind kind)
        {
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var hand = (List<CommandData>)typeof(TacticDeck).GetField("hand", f).GetValue(deck);
            var draw = (List<CommandData>)typeof(TacticDeck).GetField("draw", f).GetValue(deck);
            var discard = (List<CommandData>)typeof(TacticDeck).GetField("discard", f).GetValue(deck);
            var src = draw.FirstOrDefault(c => c.tactic == kind) != null ? draw : discard;
            var card = src.FirstOrDefault(c => c.tactic == kind);
            if (card == null) return;
            src.Remove(card);
            if (hand.Count >= deck.HandMax) { draw.Add(hand[0]); hand.RemoveAt(0); }
            hand.Add(card);
        }

        /// <summary>【RC6・でたらめ通し】強化（自分＋・まね＋・まね－のどれか）。商人ならルーン片を払う。</summary>
        static bool SmokeUpgrade(ActRun run, System.Random rng, bool shop)
        {
            var kinds = run.Deck.Where(d => run.UpgradeOf(d.name) == null && V4Content.CanUpgradeAny(d)).ToList();
            if (kinds.Count == 0) return false;
            var c = kinds[rng.Next(kinds.Count)];
            var k = (UpgradeKind)rng.Next(3);
            if (!V4Content.CanUpgrade(c, k)) k = UpgradeKind.CopyUp;
            return shop ? run.BuyUpgrade(c, k) : run.UpgradeCard(c, k);
        }

        /// <summary>実行して、あなたの命令の後・敵の行動（兵種）・敵のコピー・床の発動の瞬間を撮る。</summary>
        IEnumerator ExecuteWithShots(TurnManager turn, string prefix)
        {
            turn.ExecuteTurn();
            bool player = false, env = false;
            var traitShot = new HashSet<UnitController>();
            var copyShot = new HashSet<UnitController>();
            float start = Time.time;
            while ((turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) && Time.time - start < 30f)
            {
                if (!player && turn.State == BattleState.EnemyExecuting) { player = true; yield return Shot($"{prefix}_a_player_done"); }
                var ae = turn.ActingEnemy;
                if (ae != null && turn.IsExecutingTrait && traitShot.Add(ae)) { yield return new WaitForSeconds(0.3f); yield return Shot($"{prefix}_b_trait_{ae.Model.Label}"); continue; }
                if (ae != null && !turn.IsExecutingTrait && turn.State == BattleState.EnemyExecuting && copyShot.Add(ae)) { yield return new WaitForSeconds(0.38f); yield return Shot($"{prefix}_c_copy_{ae.Model.Label}"); yield return new WaitForSeconds(0.45f); yield return Shot($"{prefix}_c2_copy_{ae.Model.Label}"); continue; }
                if (!env && turn.IsEnvironmentPhase) { env = true; yield return new WaitForSeconds(0.25f); yield return Shot($"{prefix}_d_env"); continue; }
                yield return null;
            }
        }

        CommandData Card(string asset) => FindAnyObjectByType<ActRunManager>().Card(asset);

        /// <summary>
        /// 【戦闘中心 v3】-capturecombat 1：外部テスト版の画面一式（1280×720 で読めるかの確認）。
        /// 外部テスト版のタイトル → 遊び方 → 役職 → 扉 → 研究室の各部屋（始め・札を選んだ・組んだ・1ターン後）→ ボスの段階2・3（撮影のため直接切り替える）→ 手札整理 → 試練 → 結果。
        /// </summary>
        IEnumerator CaptureCombat(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            BuildInfo.SetForTest(true);
            act.OpenTitle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("k00_title_tester");
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, true);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("k01_howto");
            typeof(ActUI).GetField("showHowTo", flags).SetValue(actUi, false);
            act.OpenCombatRunRoleSelect();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("k02_roles");
            act.StartRun(ActContent.Roles[0], 4242, true, false, true);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) { yield return Shot("k03_boon"); act.SkipBoon(); }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("k04_doors_f1");
            // 各階の扉（勝ちを強制して進む）
            for (int f = 0; f < 7 && act.Screen == ActScreen.Doors; f++)
            {
                if (f > 0) yield return Shot($"k05_doors_f{f + 1}");
                var room = act.Run.Doors.FirstOrDefault(d => d.Kind == RoomKind.Trial) ?? act.Run.Doors[f % act.Run.Doors.Count];
                act.ChooseDoor(room);
                yield return new WaitForSeconds(0.8f);
                if (act.Screen == ActScreen.Battle)
                {
                    yield return Shot($"k06_f{f + 1}_{room.Id}_start");
                    if (room.Kind == RoomKind.Trial)
                    {
                        act.GiveUpTrial();
                        yield return new WaitForSeconds(0.3f);
                        yield return Shot($"k07_f{f + 1}_trial_failed");
                        act.NextFloor();
                        continue;
                    }
                    act.ForceWinForCapture();
                    yield return new WaitForSeconds(0.3f);
                    act.ProceedAfterBattle();
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"k07_f{f + 1}_reward");
                    act.NextFloor();
                }
                else
                {
                    yield return Shot($"k06_f{f + 1}_{room.Kind.ToString().ToLower()}");
                    act.NextFloor();
                }
                yield return new WaitForSeconds(0.3f);
            }
            act.AbandonRun();
            BuildInfo.SetForTest(null);
            // 研究室：各部屋（始め・札を選んだ・組んだ・1ターン後）
            act.OpenCombatLab();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("k10_lab");
            int n = 0;
            foreach (var (index, st) in act.LabStagesV3())
            {
                n++;
                act.ExitToStages(index);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"k{n + 10:00}_{st.buttonLabel}_1_start");
                if (n == 1)
                {
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, true);
                    yield return null;
                    yield return Shot($"k{n + 10:00}_{st.buttonLabel}_1b_legend");
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, false);
                }
                var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                if (card != null && ui != null)
                {
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Up;
                    yield return null;
                    yield return Shot($"k{n + 10:00}_{st.buttonLabel}_2_card_selected");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
                var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Rush || c.Data.type == CommandType.Walk);
                var slash = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
                if (slash != null) turn.AddCommand(new CommandRecord(slash.Data, Direction.Up, 0, slash));
                if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Down, walk.Data.minDistance, walk));
                yield return null;
                yield return Shot($"k{n + 10:00}_{st.buttonLabel}_3_planned");
                if (turn.CanExecute) turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"k{n + 10:00}_{st.buttonLabel}_4_after");
                // ボス：段階2・3（撮影のため直接切り替える）
                var lord = turn.Grid.Model.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord && e.IsAlive);
                if (lord != null && turn.State == BattleState.PlayerInput)
                {
                    RulesEngine.ApplyDamage(turn.Grid.Model, turn.Player.Model, null, false, lord, lord.Hp - CombatBoss.Floor(lord, 0), DamageKind.Direct, new List<BattleEvent>());
                    PhaseRules.EnvironmentPhase(turn.Grid.Model, new List<BattleEvent>(), null);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"k{n + 10:00}_{st.buttonLabel}_5_phase2");
                    RulesEngine.ApplyDamage(turn.Grid.Model, turn.Player.Model, null, false, lord, lord.Hp - CombatBoss.Floor(lord, 1), DamageKind.Direct, new List<BattleEvent>());
                    lord.SlamCd = 1;
                    lord.SlamCells = CombatBoss.ChargeShape(turn.Grid.Model, lord.Position);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"k{n + 10:00}_{st.buttonLabel}_6_phase3");
                }
                // 手札整理（最初の部屋だけ）
                if (n == 1 && turn.State == BattleState.PlayerInput)
                {
                    typeof(BattleUI).GetField("tidyMode", flags).SetValue(ui, true);
                    var pick = (List<CardInstance>)typeof(BattleUI).GetField("tidyPick", flags).GetValue(ui);
                    pick.Clear();
                    pick.AddRange(turn.Deck.Hand.Where(c => !turn.IsCardPlanned(c)).Take(2));
                    yield return null;
                    yield return Shot($"k{n + 10:00}_{st.buttonLabel}_7_tidy");
                    typeof(BattleUI).GetField("tidyMode", flags).SetValue(ui, false);
                    pick.Clear();
                }
            }
            // 大きい手札
            var first = act.LabStagesV3().FirstOrDefault();
            act.OpenCombatLab();
            act.ExitToStages(first.index);
            yield return new WaitForSeconds(0.6f);
            turn.DrawExtra(14);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("k80_big_hand");
            // 結果画面（負け）
            act.StartRun(ActContent.Roles[0], 4243, true, false, true);
            yield return null;
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(0.6f);
            act.ForceLoseForCapture();
            yield return new WaitForSeconds(0.3f);
            act.ProceedAfterBattle();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("k90_result_loss");
        }

        /// <summary>
        /// 【検証中・Copy Core V2】一覧の画面 → 各サンドボックスのステージで「始め」「斬撃を選んで左に矢印を合わせた状態（敵ごとのコピーの向き）」「1ターン実行した後」を撮る。
        /// 方式は C（反転・すぐ）。最後に CV1 を A と D でも撮る（比べる用）。
        /// </summary>
        /// <summary>【検証中・Copy Core V3】タイトル → 研究室 → 各部屋（始め・札を選んだとき・組んだとき・1ターン後）→ コピー中心 ACT1（役職・扉・最初の戦闘）。</summary>
        IEnumerator CaptureCopyLabV3(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            act.OpenTitle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("v00_title");
            act.OpenCopyLabV3();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("v01_lab");
            int n = 0;
            foreach (var (index, st) in act.LabStagesV3())
            {
                n++;
                act.ExitToStages(index);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"v{n + 1:00}_{st.buttonLabel}_1_start");
                var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                if (card != null && ui != null)
                {
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Left;
                    yield return null;
                    yield return Shot($"v{n + 1:00}_{st.buttonLabel}_2_card_selected");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
                var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk);
                var slash = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
                if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Left, 1, walk));
                if (slash != null) turn.AddCommand(new CommandRecord(slash.Data, Direction.Left, 0, slash));
                yield return null;
                yield return Shot($"v{n + 1:00}_{st.buttonLabel}_3_planned");
                if (turn.CanExecute) turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"v{n + 1:00}_{st.buttonLabel}_4_after");
            }
            // コピー中心 ACT1：役職 → 扉 → 最初の戦闘
            act.OpenCopyRunRoleSelect();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("v90_copyrun_roles");
            act.StartRun(ActContent.Roles[0], 4242, true);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("v91_copyrun_doors");
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(1f);
            yield return Shot("v92_copyrun_battle");
            act.AbandonRun();
        }

        /// <summary>
        /// 【Puzzle v2】タイトル → パズル研究室 → 各部屋（始め＝札を選ぶ前から敵の範囲と攻略条件・札を選んだとき・凡例・組んだとき・1ターン後）
        /// → ボスの段階1〜3（段階は撮影のため直接切り替える）→ 大きい手札 → パズル中心 ACT1 v2（役職・扉・最初の戦闘）。
        /// </summary>
        IEnumerator CapturePuzzle(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = null;
            CopyCore.SandboxCopyOff = false;
            act.OpenTitle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("p00_title");
            act.OpenPuzzleLab();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("p01_lab");
            int n = 0;
            foreach (var (index, st) in act.LabStagesV3())
            {
                n++;
                act.ExitToStages(index);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"p{n + 1:00}_{st.buttonLabel}_1_start");
                if (n == 1)
                {
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, true);
                    yield return null;
                    yield return Shot($"p{n + 1:00}_{st.buttonLabel}_1b_legend");
                    typeof(BattleUI).GetField("showLegend", flags).SetValue(ui, false);
                }
                var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                if (card != null && ui != null)
                {
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Up;
                    yield return null;
                    yield return Shot($"p{n + 1:00}_{st.buttonLabel}_2_card_selected");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
                var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Rush || c.Data.type == CommandType.Walk);
                var slash = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
                if (slash != null) turn.AddCommand(new CommandRecord(slash.Data, Direction.Up, 0, slash));
                if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Down, walk.Data.minDistance, walk));
                yield return null;
                yield return Shot($"p{n + 1:00}_{st.buttonLabel}_3_planned");
                if (turn.CanExecute) turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                yield return new WaitForSeconds(0.5f);
                yield return Shot($"p{n + 1:00}_{st.buttonLabel}_4_after");
                // ボス：段階2・3の表示（撮影のため直接切り替える）
                var lord = turn.Grid.Model.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.SealLord && e.IsAlive);
                if (lord != null && turn.State == BattleState.PlayerInput)
                {
                    lord.BossStage = 1; lord.Hp = PuzzleBoss.Floor(lord, 0);
                    foreach (var c in turn.Grid.Model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue)) c.Dormant = false;

                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"p{n + 1:00}_{st.buttonLabel}_5_phase2");
                    lord.BossStage = 2; lord.Hp = PuzzleBoss.Floor(lord, 1);
                    foreach (var c in turn.Grid.Model.Enemies.Where(e => e.Kind == EnemyKind.BladeStatue)) { c.Dormant = true; c.BossStage = 1; }

                    yield return new WaitForSeconds(0.3f);
                    yield return Shot($"p{n + 1:00}_{st.buttonLabel}_6_phase3");
                }
            }
            // 大きい手札（上限なし：19 枚）
            act.OpenPuzzleLab();
            var first = act.LabStagesV3().FirstOrDefault();
            act.ExitToStages(first.index);
            yield return new WaitForSeconds(0.6f);
            turn.DrawExtra(30);
            yield return new WaitForSeconds(0.6f);
            yield return Shot("p80_big_hand");
            // パズル中心 ACT1 v2：役職 → 扉 → 最初の戦闘
            act.OpenPuzzleRunRoleSelect();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("p90_puzzlerun_roles");
            act.StartRun(ActContent.Roles[0], 4242, true, true);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("p91_puzzlerun_doors");
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(1f);
            yield return Shot("p92_puzzlerun_battle");
            act.AbandonRun();
        }

        IEnumerator CaptureCopyLab(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            CopyCore.SandboxMode = CopyMode.MirrorImmediate;
            CopyCore.SandboxCopyOff = false;
            act.OpenCopyLab();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("c00_lab");
            int n = 0;
            foreach (var (index, st) in act.SandboxStages())
            {
                n++;
                act.ExitToStages(index);
                yield return new WaitForSeconds(0.8f);
                yield return Shot($"c{n:00}_1_start");
                var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash) ?? turn.Deck.Hand.FirstOrDefault();
                if (card != null && ui != null)
                {
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Left;
                    yield return null;
                    yield return Shot($"c{n:00}_2_copy_arrows");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
                // 1ターン：一歩←・斬撃←（あれば）
                var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk);
                var slash = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
                if (walk != null) turn.AddCommand(new CommandRecord(walk.Data, Direction.Left, 1, walk));
                if (slash != null) turn.AddCommand(new CommandRecord(slash.Data, Direction.Left, 0, slash));
                yield return null;
                yield return Shot($"c{n:00}_3_planned");
                if (turn.CanExecute) turn.ExecuteTurn();
                while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"c{n:00}_4_after");
            }
            // 方式ごとのコピーの矢印（斬撃←）。E は「動きはまねる」も見せるため一歩←も撮る
            foreach (var mode in new[] { CopyMode.Current, CopyMode.TowardImmediate, CopyMode.MimicReflectImmediate })
            {
                CopyCore.SandboxMode = mode;
                var first = act.SandboxStages().FirstOrDefault();
                act.ExitToStages(first.index);
                yield return new WaitForSeconds(0.8f);
                foreach (var type in mode == CopyMode.MimicReflectImmediate ? new[] { CommandType.Slash, CommandType.Walk } : new[] { CommandType.Slash })
                {
                    var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == type);
                    if (card == null || ui == null) continue;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    ui.DebugFocus = Direction.Left;
                    yield return null;
                    yield return Shot($"c90_{CopyCore.Letter(mode)}_{type}_copy_arrows");
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                }
            }
            CopyCore.SandboxMode = null;
        }

        IEnumerator CaptureActLoss(TurnManager turn)
        {
            // 【検証中】RC の確認：1階の戦闘で負け（撮影のため強制）→ 結果画面 → 「もう一度」→ 旅立ち → 扉 → 「同じ地図で再挑戦」も確かめる
            var act = FindAnyObjectByType<ActRunManager>();
            act.OpenTitle();
            act.StartRun(ActContent.Roles[0], 4242);
            yield return new WaitForSeconds(0.3f);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.ChooseDoor(act.Run.Doors[0]);
            yield return new WaitForSeconds(1f);
            yield return Shot("l01_battle");
            act.ForceLoseForCapture();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("l02_lost");
            act.ProceedAfterBattle();
            yield return new WaitForSeconds(0.4f);
            yield return Shot("l03_result");
            int seed = act.Run.Seed;
            var firstDoors = string.Join(",", act.Run.Map.SelectMany(f => f).Select(r => r.Id));
            act.Retry(true);
            yield return new WaitForSeconds(0.4f);
            yield return Shot("l04_retry_same_map");
            Debug.Log($"[DebugAutoCapture] 同じ地図で再挑戦：種 {seed} → {act.Run.Seed}・地図が同じ {firstDoors == string.Join(",", act.Run.Map.SelectMany(f => f).Select(r => r.Id))}・画面 {act.Screen}");
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("l05_doors");
            act.AbandonRun();
            yield return new WaitForSeconds(0.3f);
            yield return Shot("l06_title");
        }

        IEnumerator CaptureAct(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            act.OpenTitle();
            yield return new WaitForSeconds(0.6f);
            yield return Shot("a00_title");
            act.OpenRoleSelect();
            yield return new WaitForSeconds(0.2f);
            yield return Shot("a01_roles");
            int roleIndex = 0;
            var cargs = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < cargs.Length - 1; i++) if (cargs[i] == "-capturerole") int.TryParse(cargs[i + 1], out roleIndex);
            act.StartRun(ActContent.Roles[Mathf.Clamp(roleIndex, 0, ActContent.Roles.Length - 1)], 12345);
            if (act.Screen == ActScreen.Boon)
            {
                yield return new WaitForSeconds(0.3f);
                yield return Shot("a00b_boon");
                var boon = act.Run.BoonOptions.FirstOrDefault(b => b.Kind != BoonKind.Trim);
                if (boon != null) act.ChooseBoon(boon); else act.SkipBoon();
            }
            int guard = 0;
            while (act.Screen != ActScreen.Result && guard++ < 20)
            {
                var run = act.Run;
                int floor = run.Floor + 1;
                yield return new WaitForSeconds(0.2f);
                yield return Shot($"a{floor:00}_0_doors");
                var doors = run.Doors;
                // 撮影用：3階は商人・5階は特殊部屋・6階は休息を選ぶ（いろいろな画面を撮るため）
                var want = floor == 3 ? RoomKind.Shop : floor == 5 ? RoomKind.Event : floor == 6 ? RoomKind.Rest : (RoomKind?)null;
                var door = want.HasValue ? doors.FirstOrDefault(d => d.Kind == want.Value) : null;
                act.ChooseDoor(door ?? doors[(floor + 1) % doors.Count]);
                yield return new WaitForSeconds(1f);
                switch (act.Screen)
                {
                    case ActScreen.Battle:
                        yield return Shot($"a{floor:00}_1_battle");
                        // 【検証中】範囲プレビュー：1つ目の命令（一歩）を入れてから、斬撃を選んだ状態（方向を選ぶ前）を撮る
                        {
                            var ui = FindAnyObjectByType<BattleUI>();
                            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                            var walk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Walk || (c.Data.type == CommandType.Rush && c.Data.maxDistance == 2));
                            var atk = turn.Deck.Hand.FirstOrDefault(c => c.Data.type == CommandType.Slash);
                            if (ui != null && walk != null && atk != null)
                            {
                                turn.AddCommand(new CommandRecord(walk.Data, Direction.Up, walk.Data.minDistance, walk));
                                typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, atk.Data);
                                typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, atk);
                                typeof(BattleUI).GetField("hasPendingDirection", flags).SetValue(ui, false);
                                yield return null;
                                yield return Shot($"a{floor:00}_1b_preview");
                                // 【検証中】反射のしるし：鏡兵・騎士がいれば、その方へ向きを決めた状態も撮る
                                var mirror = turn.Enemies.FirstOrDefault(en => en.IsAlive && (en.Model.CopyTrait == CopyTrait.Mirror || en.Model.CopyTrait == CopyTrait.MirrorAttacks));
                                if (mirror != null)
                                {
                                    var from = PlayerPreview.PlannedPosition(turn.Grid.Model, turn.PlannedCommands);
                                    var d = mirror.Model.Position - from;
                                    var dir = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x >= 0 ? Direction.Right : Direction.Left) : (d.y >= 0 ? Direction.Up : Direction.Down);
                                    typeof(BattleUI).GetField("pendingDirection", flags).SetValue(ui, dir);
                                    typeof(BattleUI).GetField("hasPendingDirection", flags).SetValue(ui, true);
                                    yield return null;
                                    yield return Shot($"a{floor:00}_1c_reflect");
                                    typeof(BattleUI).GetField("hasPendingDirection", flags).SetValue(ui, false);
                                }
                                typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                                typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                                turn.RemoveLastCommand();
                            }
                        }
                        if (run.Items.Count > 0) { act.UseItem(run.Items[0]); yield return new WaitForSeconds(0.3f); }
                        foreach (var (type, dir, dist) in LargeHandRoute(turn, 0))
                        {
                            CardInstance card = null;
                            foreach (var c in turn.Deck.Hand)
                                if (c.Data.type == type && !turn.IsCardPlanned(c)) { card = c; break; }
                            if (card != null) turn.AddCommand(new CommandRecord(card.Data, dir, dist, card));
                        }
                        yield return null;
                        yield return Shot($"a{floor:00}_2_planned");
                        if (turn.CanExecute) turn.ExecuteTurn();
                        while (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) yield return null;
                        yield return new WaitForSeconds(0.5f);
                        yield return Shot($"a{floor:00}_3_turn2");
                        if (turn.State == BattleState.PlayerInput) act.ForceWinForCapture();
                        yield return new WaitForSeconds(0.5f);
                        yield return Shot($"a{floor:00}_4_won");
                        act.ProceedAfterBattle();
                        yield return new WaitForSeconds(0.3f);
                        yield return Shot($"a{floor:00}_5_after");
                        if (act.Screen == ActScreen.Reward)
                        {
                            if (run.RewardCards.Count > 0) run.TakeRewardCard(run.RewardCards[0]);
                            if (run.RewardImprints.Count > 0) run.TakeRewardImprint(run.RewardImprints[0]);
                            yield return new WaitForSeconds(0.2f);
                            yield return Shot($"a{floor:00}_6_reward_taken");
                            act.NextFloor();
                        }
                        break;
                    case ActScreen.Shop:
                        run.AddShards(120);
                        yield return Shot($"a{floor:00}_1_shop");
                        if (run.ShopCards.Count > 0) run.BuyCard(run.ShopCards[0]);
                        if (run.ShopItems.Count > 0) run.BuyItem(run.ShopItems[0]);
                        run.BuyImprint();
                        yield return new WaitForSeconds(0.2f);
                        yield return Shot($"a{floor:00}_2_shop_bought");
                        act.NextFloor();
                        break;
                    default:
                        yield return Shot($"a{floor:00}_1_{act.Screen.ToString().ToLower()}");
                        if (act.Screen == ActScreen.Rest) run.RestHeal();
                        act.NextFloor();
                        break;
                }
            }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("a99_result");
        }

        /// <summary>
        /// -capturestage -1：RUN の画面一式を撮る（RUN 開始 → BATTLE 1 の操作 → 報酬 → BATTLE 2 → 報酬 → 休憩 → BATTLE 3 → CLEAR）。
        /// 戦闘の勝敗は撮影のために強制的に決める（敵の HP を 0 にする）。ゲーム本体の挙動ではない。
        /// </summary>
        IEnumerator CaptureRun(TurnManager turn)
        {
            var run = FindAnyObjectByType<RunManager>();
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var pendingCommand = typeof(BattleUI).GetField("pendingCommand", flags);
            var pendingCard = typeof(BattleUI).GetField("pendingCard", flags);
            var checkEnd = typeof(TurnManager).GetMethod("CheckBattleEnd", flags);

            run.OpenRunStart();
            yield return new WaitForSeconds(0.5f);
            yield return Shot("r0_run_start");

            run.StartRun();
            yield return new WaitForSeconds(0.12f);
            yield return Shot("r1a_battle1_drawing");
            yield return new WaitForSeconds(1f);
            yield return Shot("r1b_battle1_start");

            // カードを1枚選んだ状態（方向選び）
            var first = turn.Deck.Hand[0];
            pendingCommand.SetValue(ui, first.Data);
            pendingCard.SetValue(ui, first);
            yield return Shot("r2_card_selected");
            pendingCommand.SetValue(ui, null);
            pendingCard.SetValue(ui, null);

            // 歩く → カード → 歩く を登録し、残りのカードを1枚保留
            turn.AddCommand(new CommandRecord(turn.BasicWalk, Direction.Up, 1));
            turn.AddCommand(new CommandRecord(first.Data, Direction.Up, first.Data.minDistance, first));
            turn.AddCommand(new CommandRecord(turn.BasicWalk, Direction.Down, 1));
            turn.ToggleRetain(turn.Deck.Hand[1]);
            yield return new WaitForSeconds(0.2f);
            yield return Shot("r3_planned_and_retained");

            turn.ExecuteTurn();
            while (!turn.IsExecutingInnate && turn.State != BattleState.PlayerInput && turn.State != BattleState.Cleared && turn.State != BattleState.GameOver) yield return null;
            yield return new WaitForSeconds(0.1f);
            yield return Shot("r4_enemy_innate");
            while (turn.State != BattleState.PlayerInput && turn.State != BattleState.Cleared && turn.State != BattleState.GameOver) yield return null;
            yield return new WaitForSeconds(0.1f);
            yield return Shot("r5_turn2_redraw");
            yield return new WaitForSeconds(0.8f);
            yield return Shot("r5b_turn2");

            for (int battle = 0; battle < 3; battle++)
            {
                if (turn.State == BattleState.PlayerInput)
                {
                    turn.Enemy.TakeDamage(999);
                    checkEnd.Invoke(turn, null);
                }
                yield return new WaitForSeconds(0.6f);
                yield return Shot($"r6_battle{battle + 1}_won");
                run.ProceedAfterBattle();
                yield return new WaitForSeconds(0.3f);
                if (run.Screen == RunScreen.Reward)
                {
                    yield return Shot($"r7_reward_after_battle{battle + 1}");
                    if (battle == 0) run.TakeReward(run.Run.RewardOptions[0]);
                    else run.SkipReward();
                }
                if (run.Screen == RunScreen.Rest)
                {
                    yield return new WaitForSeconds(0.2f);
                    yield return Shot("r8_rest");
                    run.Rest();
                }
                if (run.Screen == RunScreen.Battle)
                {
                    yield return new WaitForSeconds(1f);
                    yield return Shot($"r9_battle{battle + 2}_start");
                }
            }
            yield return new WaitForSeconds(0.3f);
            yield return Shot("r10_run_clear");
        }

        /// <summary>【検証中】大きい手札：手札にある新しい移動札を優先して2枚＋斬撃（新しい札の表示と動きを撮るため。攻略ルートではない）。</summary>
        static (CommandType, Direction, int)[] LargeHandRoute(TurnManager turn, int t)
        {
            var order = new[] { CommandType.Jump, CommandType.Diagonal, CommandType.Slide, CommandType.Blink, CommandType.Swap, CommandType.Rush, CommandType.Walk };
            var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
            var steps = new List<(CommandType, Direction, int)>();
            // 【検証中】闘気があれば「移動 → 闘気 → 攻撃」、なければ「移動2つ → 攻撃」。攻撃は押し斬り・薙ぎ払い・斬撃（突き）の順に手札にあるもの
            bool spirit = System.Linq.Enumerable.Any(turn.Deck.Hand, c => c.Data.type == CommandType.Spirit);
            int moves = spirit ? 1 : 2;
            foreach (var type in order)
            {
                if (steps.Count == moves) break;
                var card = System.Linq.Enumerable.FirstOrDefault(turn.Deck.Hand, c => c.Data.type == type);
                if (card == null) continue;
                steps.Add((type, dirs[(t + steps.Count) % 4], card.Data.maxDistance));
            }
            if (spirit) steps.Add((CommandType.Spirit, Direction.Up, 0));
            var attack = CommandType.Slash;
            foreach (var type in new[] { CommandType.PushSlash, CommandType.Sweep, CommandType.Slash })
                if (System.Linq.Enumerable.Any(turn.Deck.Hand, c => c.Data.type == type)) { attack = type; break; }
            steps.Add((attack, dirs[(t + 2) % 4], 0));
            return steps.ToArray();
        }

        /// <summary>【検証中】斜め歩を選んだ状態（方向ボタンが ↗↘↙↖ になる）を撮る。</summary>
        IEnumerator ShotDiagonalPicker(TurnManager turn)
        {
            yield return ShotSelected(turn, CommandType.Diagonal, "1b_diagonal_picker");
            // 【仮UI】攻撃札を選んだ状態（名前・短い効果・敵がコピーすると）
            foreach (var type in new[] { CommandType.PushSlash, CommandType.Sweep, CommandType.Spirit, CommandType.Slash })
                if (System.Linq.Enumerable.Any(turn.Deck.Hand, c => c.Data.type == type)) { yield return ShotSelected(turn, type, "1c_attack_selected"); break; }
        }

        IEnumerator ShotSelected(TurnManager turn, CommandType type, string name)
        {
            var card = System.Linq.Enumerable.FirstOrDefault(turn.Deck.Hand, c => c.Data.type == type);
            if (card == null) yield break;
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var pendingCommand = typeof(BattleUI).GetField("pendingCommand", flags);
            var pendingCard = typeof(BattleUI).GetField("pendingCard", flags);
            pendingCommand.SetValue(ui, card.Data);
            pendingCard.SetValue(ui, card);
            yield return Shot(name);
            pendingCommand.SetValue(ui, null);
            pendingCard.SetValue(ui, null);
        }

        /// <summary>
        /// 【RC3】-capturerooms 1：全部屋 × 開始配置3つの始めの画面と、1ターン後（一歩 ↑ だけ）。はじめてのランの最初の戦闘（敵は まねるだけ）も。
        /// 盤面の作り直しを、人の目で（初見の立場で）見直すため。
        /// </summary>
        static bool roomsQuick;

        IEnumerator CaptureRooms(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            ScreenFx.Disabled = true;
            BuildInfo.SetForTest(true);
            // はじめてのランの最初の戦闘（鏡の歩兵・まねるだけ）
            act.StartRun(ActContent.Roles[0], 4242, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            act.Run.ForceFirstDoor("v4_i1");
            ActRun.CaptureLayout = 0;
            act.StartRoomForCapture(V4Content.Room("v4_i1"), 0);
            yield return new WaitForSeconds(0.8f);
            yield return Shot("r00_lesson_v4_i1");
            act.AbandonRun();
            yield return new WaitForSeconds(0.3f);
            // 2回目以降のランの扱い（敵の攻撃・接近・大技あり）
            act.Profile.finalFinished = 1;
            act.Profile.tacticIntroSeen = true; // 戦術札のはじめての説明（盤面を隠す）は、別の撮影で見る
            act.StartRun(ActContent.Roles[0], 4243, true, false, true, true);
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            var walk = act.Card("Walk");
            var rooms = V4Content.Rooms.Where(r => r.V4 && (r.Kind == RoomKind.Battle || r.Kind == RoomKind.Tactical || r.Kind == RoomKind.Danger || r.Kind == RoomKind.Boss)).ToList();
            if (roomsQuick) rooms = rooms.Where(r => r.Id == "v4_n4" || r.Id == "v4_t1").ToList();
            var log = new System.Text.StringBuilder();
            foreach (var room in rooms)
            {
                for (int lay = 0; lay < (roomsQuick ? 1 : 3); lay++)
                {
                    ActRun.CaptureLayout = lay;
                    act.StartRoomForCapture(room, room.Kind == RoomKind.Boss ? 7 : 1);
                    yield return new WaitForSeconds(room.Kind == RoomKind.Danger || room.Kind == RoomKind.Boss ? 2.4f : 0.7f); // 【RC5】中ボス・ボスは登場帯が消えてから（名札の重なりを見る）
                    if (act.Screen != ActScreen.Battle) { log.AppendLine($"{room.Id} {lay}: 戦闘にならない（{act.Screen}）"); break; }
                    if (turn.LayoutIndex != lay) { log.AppendLine($"{room.Id} {lay}: 別案が無い（{turn.LayoutIndex}）"); break; }
                    yield return Shot($"r_{room.Id}_{lay}_start");
                    log.AppendLine($"{room.Id} {lay}: {turn.CurrentStage.stageTitle}　敵 {turn.Grid.Model.Enemies.Count(e => e.IsAlive)}・ワープ {turn.Grid.Model.Board.Warps.Count}");
                }
            }
            ActRun.CaptureLayout = -1;
            act.AbandonRun();
            File.WriteAllText(Path.Combine(folder, "rooms_summary.txt"), log.ToString());
            BuildInfo.SetForTest(null);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"), proposalSuper ? 2 : 1); // 【企画書 v4】企画書用は2倍の解像度
            yield return null;
        }
    }
}
