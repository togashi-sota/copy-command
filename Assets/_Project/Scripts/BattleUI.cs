using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 戦闘画面の UI（プロトタイプ用に IMGUI で描く）。
    /// 左：上に HP とターン表示、真ん中に盤面、下に説明文。
    /// 右（チュートリアル）：技ボタン → 方向 → 距離 → 命令スロット → ボタン → 敵の予定 → ログ。
    /// 右（RUN）：手札カード＋歩く → 保留 → 方向 → 命令スロット → ボタン → 敵の予定 → ログ。
    /// 画面の高さ 720 を基準に拡大縮小する。RUN の戦闘以外の画面（報酬・休憩など）は RunUI が描く。
    /// </summary>
    public partial class BattleUI : MonoBehaviour
    {
        [SerializeField] TurnManager turn;
        [SerializeField] RunManager run;

        const float BoardAreaRatio = 0.56f;  // 画面の左 56% が盤面エリア
        const float HeaderHeight = 96f;
        const float TutorialHeight = 120f;

        static readonly Color PlayerTextColor = new Color(0.55f, 0.75f, 1f);
        static readonly Color EnemyTextColor = new Color(1f, 0.55f, 0.55f);
        static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.35f);

        // 入力の途中経過（技 → 方向 → 距離）
        CommandData pendingCommand;
        CardInstance pendingCard;   // RUN：選んだ手札のカード（歩くなら null）
        Direction pendingDirection;
        bool hasPendingDirection;
        // スロットに入れた瞬間の演出用
        readonly float[] slotAddedAt = { -10f, -10f, -10f, -10f, -10f, -10f, -10f, -10f };
        int lastPlannedCount;

        bool ShowingBattle => turn != null && turn.CurrentStage != null && (run == null || !run.InRun || run.Screen == RunScreen.Battle) && (act == null || !act.InAct || act.InBattle);

        void Awake()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                // 盤面は左側の、ヘッダーと説明文の間に描く
                cam.rect = UiStyles.CamRect(new Rect(0f, TutorialHeight / RefHeight, BoardAreaRatio, 1f - (HeaderHeight + TutorialHeight) / RefHeight));
            }
        }

        void Update()
        {
            if (turn == null || turn.Grid == null || turn.CurrentStage == null) return;
            UpdateCameraRect(); // 【検証中】新しいレイアウトでは盤面の場所が違う
            turn.Grid.FitCamera(Camera.main);
            if (turn.State != BattleState.PlayerInput) ResetPending();
            // 盤面の危険マス（次の固有行動が当たるマス）
            turn.Grid.ShowDangerCells(turn.InnatePending && ShowingBattle ? turn.GetInnateDangerCells() : new List<Vector2Int>());
            // 【検証中】兵種の行動・環境の予告（矢の線・移動先・炎・回復ルーン）
            UpdateForecast();
            // スロットに命令が増えたら、その枠を一瞬光らせる
            int count = turn.PlannedCommands.Count;
            if (count > lastPlannedCount && count <= slotAddedAt.Length) { slotAddedAt[count - 1] = Time.time; Sfx.Play(Sfx.Kind.Card); }
            lastPlannedCount = count;
        }

        void OnGUI()
        {
            if (!ShowingBattle) return;
            Setup();
            float scale = ApplyScale();
            if (UseShowcaseLayout) { DrawShowcase(scale); return; } // 【検証中】参考画像に寄せた新しい戦闘画面（BattleUIShowcase.cs）
            UiStyles.Theme = turn.CurrentStage.visualTheme;
            float width = Screen.width / scale;
            float boardWidth = width * BoardAreaRatio;

            DrawHeader(new Rect(0, 0, boardWidth, HeaderHeight));
            DrawUnitLabels(scale);
            DrawBombTags(scale);
            DrawTutorial(new Rect(12, RefHeight - TutorialHeight + 4, boardWidth - 24, TutorialHeight - 12));
            var right = new Rect(boardWidth + 8, 8, width - boardWidth - 16, RefHeight - 16);
            if (turn.IsDeckMode) DrawRunPanel(right);
            else DrawTutorialPanel(right);
            DrawResult(new Rect(boardWidth * 0.5f - 170, RefHeight * 0.5f - 90, 340, 180));
        }

        // ───────── 左側 ─────────

        void DrawHeader(Rect area)
        {
            string title = turn.IsDeckMode && run != null && run.Run != null
                ? $"RUN BATTLE {run.Run.BattleIndex + 1}/{run.Run.BattleCount}　{turn.CurrentStage.stageTitle}"
                : turn.CurrentStage.stageTitle;
            string titleText = $"{title}　　TURN {turn.TurnNumber}";
            GUI.Label(new Rect(area.x + 12, area.y + 6, area.width - 24, 30), titleText, Label);

            // 【検証用】鏡蝕ゲージ（ON のときだけ）。満ちていたら、次に減らせなかったときの呪いのダメージを赤で予告
            var stagnation = turn.Stagnation;
            if (stagnation != null)
            {
                float tx = area.x + 190; // 自分の HP の文字の右（見出しの「PLAYER TURN」と重ならないように）
                string gauge = "鏡蝕 " + new string('●', stagnation.Gauge) + new string('○', stagnation.Max - stagnation.Gauge) + $" {stagnation.Gauge}/{stagnation.Max}";
                if (stagnation.AtMax) gauge += $"（-{stagnation.NextPenalty}）";
                var oldColor = GUI.contentColor;
                GUI.contentColor = stagnation.AtMax ? new Color(1f, 0.45f, 0.45f) : new Color(0.75f, 0.6f, 1f);
                GUI.Label(new Rect(tx, area.y + 44, (area.width - 36) * 0.5f - 180, 24), gauge, new GUIStyle(Small) { fontSize = 15 });
                GUI.contentColor = oldColor;
            }

            string stateText; Color stateColor;
            switch (turn.State)
            {
                case BattleState.EnemyExecuting: stateText = "ENEMY TURN"; stateColor = EnemyTextColor; break;
                case BattleState.Cleared: stateText = "STAGE CLEAR"; stateColor = Color.yellow; break;
                case BattleState.GameOver: stateText = "GAME OVER"; stateColor = Color.gray; break;
                default: stateText = "PLAYER TURN"; stateColor = PlayerTextColor; break;
            }
            var old = GUI.contentColor;
            GUI.contentColor = stateColor;
            GUI.Label(new Rect(area.x + 12, area.y + 2, area.width - 24, 40), stateText, new GUIStyle(Big) { alignment = TextAnchor.UpperRight });
            GUI.contentColor = old;

            float barWidth = (area.width - 36) * 0.5f;
            bool runCarry = turn.IsDeckMode && run != null && run.InRun;
            DrawHpBar(new Rect(area.x + 12, area.y + 44, barWidth, 40), runCarry ? "あなた（RUN で持ち越し）" : "あなた", turn.Player, UnitController.PlayerColor);
            int n = turn.Enemies.Count;
            float ew = (barWidth - 8 * (n - 1)) / Mathf.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                var e = turn.Enemies[i];
                string barName = e.Model.Kind != EnemyKind.Normal ? $"{e.DisplayName} {PhaseRules.KindName(e.Model.Kind)}" : e.DisplayName;
                DrawHpBar(new Rect(area.x + 24 + barWidth + i * (ew + 8), area.y + 44, ew, 40), barName, e, e.BaseColor);
            }
        }

        void DrawHpBar(Rect r, string name, UnitController unit, Color color)
        {
            string text = r.width < 170 ? $"{name} {unit.Hp}/{unit.MaxHp}" : $"{name}　HP {unit.Hp} / {unit.MaxHp}";
            GUI.Label(new Rect(r.x, r.y - 2, r.width, 24), text, new GUIStyle(Small) { wordWrap = false, clipping = TextClipping.Clip });
            var bar = new Rect(r.x, r.y + 24, r.width, 12);
            DrawRect(bar, new Color(0.25f, 0.25f, 0.25f));
            DrawRect(new Rect(bar.x, bar.y, bar.width * unit.Hp / Mathf.Max(1, unit.MaxHp), bar.height), color);
        }

        // ───────── 【検証中】予告（兵種の行動・環境） ─────────

        static readonly Color ArrowMarkColor = new Color(1f, 0.16f, 0.02f, 0.4f); // 【RC5・レビュー3】少し橙に寄せた赤（青い石の床の上で赤紫に近く見えた）
        static readonly Color SimMarkColor = new Color(1f, 0.85f, 0.85f, 0.22f);
        internal static readonly Color TelegraphMarkColor = new Color(0.95f, 0.2f, 0.95f, 0.42f);

        /// <summary>【Puzzle v2】赤の層（兵の攻撃）。命令・選んだ札に関係なく、今の盤面だけで決まる（回帰テストあり）。</summary>
        public static List<Vector2Int> BaseThreat(BattleModel model) => ThreatMap.EnemyAttack(model);

        /// <summary>【Puzzle v2】テスト用：予告の表示を今すぐ作り直す。</summary>
        public void RefreshForecastForTest() { forecastKey = null; UpdateForecast(); }
        /// <summary>【Puzzle v2】テスト用。</summary>
        public void SetupForTest(TurnManager turnManager) => turn = turnManager;
        /// <summary>【RC9】テスト用：ACT1 の戦闘（本編／チュートリアル）につなぐ。</summary>
        public void SetupForTest(TurnManager turnManager, ActRunManager actManager) { turn = turnManager; act = actManager; }

        /// <summary>
        /// 【RC9】テスト用：札を選んで向きにマウスを乗せた（preview）ときに、盤面に出す物。
        /// changes＝「ずれる・倒すと消える」の数、shownDiffers＝出す危険がターンの初めと違う、selfKnown＝「あなた Nダメ／無傷」を出す、danger＝命令の欄の「！ このターン…」。
        /// </summary>
        public (int changes, bool shownDiffers, bool selfKnown, bool danger) AnswerPreviewForTest(CommandRecord preview)
        {
            previewRecord = preview;
            threatKeyV2 = null;
            ComputeThreatsV2();
            bool differs = threatsShown.Count != threatsBase.Count || threatsShown.Zip(threatsBase, (a, b) => a.SourceId != b.SourceId || a.Cells.Count != b.Cells.Count || !a.Cells.All(b.Cells.Contains)).Any(x => x);
            var sd = SelfDamagePreview();
            bool futureOk = !(CombatV2 && !AnswerPreview);
            bool danger = futureOk && EndCellDanger(turn.Grid.Model, new List<CommandRecord> { preview }) != null;
            previewRecord = null;
            return (threatChanges.Count, differs, sd.known, danger);
        }
        /// <summary>【RC9】テスト用：今の盤面の危険（ルール）。</summary>
        public List<Threat> ThreatsShownForTest() { threatKeyV2 = null; ComputeThreatsV2(); return threatsShown; }
        /// <summary>【RC9】テスト用：前のターンに起きたこと（右下の欄）。</summary>
        public List<string> LastTurnLinesForTest() => LastTurnLines();
        public bool DrawsPillsOnForTest(Vector2Int c) => DrawsPillsOn(c);
        /// <summary>【RC11】向きの矢印に「空振り」を出す札（移動の一歩・突進）。null でも false（押した直後は pendingCommand が null）。</summary>
        public static bool WhiffMoveCard(CommandData c) => c != null && (c.type == CommandType.Walk || c.type == CommandType.Rush);
        public List<Threat> ThreatsOnYouForTest() { threatKeyV2 = null; ComputeThreatsV2(); return ThreatsOnYou(); }
        /// <summary>【RC9】テスト用：この戦闘は新しい盤面の印（CombatV2）か。</summary>
        public bool CombatV2ForTest => CombatV2;
        static readonly Color StepMarkColor = new Color(0.75f, 0.35f, 1f, 0.45f);
        static readonly Color FireMarkColor = new Color(1f, 0.5f, 0.05f, 0.5f);
        static readonly Color FireNextMarkColor = new Color(1f, 0.6f, 0.2f, 0.16f);
        static readonly Color RuneMarkColor = new Color(0.2f, 1f, 0.45f, 0.5f);
        static readonly Color RuneNextMarkColor = new Color(0.2f, 1f, 0.45f, 0.16f);
        static readonly Color BombMarkColor = new Color(1f, 0.35f, 0f, 0.6f);
        static readonly Color BombNextMarkColor = new Color(1f, 0.55f, 0.1f, 0.25f);

        /// <summary>【Combat V3】騎士V3 のカウントダウン（剣3・爆—・炎— など。— はまだ使わない段階）。</summary>
        static string CountdownText(UnitModel e) =>
            $"剣{(e.SlamCd > 0 ? e.SlamCd.ToString() : "—")}・爆{(e.BlastCd > 0 ? e.BlastCd.ToString() : "—")}・炎{(e.WaveCd > 0 ? e.WaveCd.ToString() : "—")}";

        /// <summary>【Combat V2】敵の予定欄に出す、兵種の行動の短い予告（剣 前3マス↓ など）。</summary>
        static string TraitIntent(UnitModel e, BattleModel model = null)
        {
            if (model != null && model.NoTraits) return null; // 【RC3】はじめての戦闘：敵は まねるだけ
            bool soft = model != null && model.SoftRules; // 【戦闘中心 v3・レビュー】Soft の部屋の文（反響鎧・剣兵の反撃）
            switch (e.Kind)
            {
                case EnemyKind.Swordsman: return soft && RulesEngine.SwordCounter > 0 ? $"剣 周囲4マス・隣から殴ると斬り返す（{RulesEngine.SwordCounter}）" : "剣 周囲4マス";
                case EnemyKind.Chaser: return "近づく：まねの後、あなたへ1マス";
                case EnemyKind.Fleer: return "離れる：まねの後、あなたから1マス";
                case EnemyKind.Spearman: return $"槍 正面2マス{DirGlyph(e.Facing)}";
                case EnemyKind.Bomber: return e.BombCells.Count > 0 ? "砲 予告の爆発→次を予告" : "砲 次の爆発を予告";
                case EnemyKind.KnightV3: return CountdownText(e);
                case EnemyKind.KnightV2:
                    return "剣 周囲4マス" + (e.ShootsArrows ? $"＋矢{DirGlyph(e.Facing)}" : "") + (e.Hp * 10 <= e.MaxHp * 4 ? "（第3段階）" : e.ShootsArrows ? "（第2段階）" : "（第1段階）");
            }
            // 【検証中・Copy Core V2】
            switch (e.Kind)
            {
                case EnemyKind.EchoKnight:
                    if (soft) return e.ArmorBroken ? "鎧が割れた（普通の兵）" : $"鎧：あなたの技は {RulesEngine.EchoSoftDamage} だけ。まね攻撃・兵の攻撃・仕掛けで割れる";
                    return "反響鎧：あなたの技では傷つかない（まね攻撃・トゲ・視線なら傷つく。最後の1体なら効く）";
                case EnemyKind.SealGuard: return e.Barrier > 0 ? $"封印 {e.Barrier}：敵のまね攻撃が当たるたびに -1" : "封印なし：あなたの攻撃も通る";
                case EnemyKind.Cannon: return "写し砲：あなたの攻撃の技だけ、一直線に撃つ（倒れない）";
                case EnemyKind.Warden:
                    return (e.Barrier > 0 ? $"封印 {e.Barrier}（割り方：{PhaseRules.WardenRuleText(e.BarrierRule)}）" : $"封印が割れている（あと {Mathf.Max(1, PhaseRules.WardenOpenRounds - e.BarrierBrokenRounds)}R）")
                           + $"・大剣まで {e.SlamCd}";
                // 【Copy Core V3】
                case EnemyKind.Lord:
                {
                    int ph = PhaseRules.LordPhase(e);
                    string state = ph == 1 ? "段階2：写し身に主を斬らせる（全部砕くまで 40% より下へ減らない）"
                                 : e.Barrier > 0 ? $"段階{ph + 1}：守り（{PhaseRules.LordRuleText(ph)}）"
                                 : $"段階{ph + 1}：守りが割れている（あと {Mathf.Max(1, PhaseRules.LordOpenRounds - e.BarrierBrokenRounds)}R）";
                    return state + $"・光柱（予告→落ちる→休む）・大剣まで {e.SlamCd}";
                }
                case EnemyKind.HealClone: return e.Dormant ? "像：主の段階2で目覚める" : $"毎ターン主を回復・技まね・主を斬ると砕ける";
                case EnemyKind.SealLord:
                    return (e.Barrier == 0 ? $"窓：直接攻撃が効く（あと {Mathf.Max(1, PuzzleBoss.WindowRounds - e.BarrierBrokenRounds)}ターン）" : $"段階{e.BossStage + 1}：{PuzzleBoss.ObjectiveSentence(null, e)}")
                           + (e.BombCells.Count > 0 ? "・石手が落ちる（紫）" : e.BlastCd > 0 ? "・石手は休み" : "・石手を予告する");
                case EnemyKind.BladeStatue: return e.BossStage > 0 ? "光が消えた" : e.Dormant ? "眠り：段階2で光る" : "技まね：主を斬らせる";
                case EnemyKind.Golem: return "運び像：移動だけまねる（倒れない）";
                // 【戦闘中心 v3】
                case EnemyKind.Mender: return e.StunRounds > 0 ? "共鳴で動けない" : $"癒し：いちばん傷ついた仲間を +{PhaseRules.MenderHeal}";
                case EnemyKind.MirrorLord:
                    return e.StunRounds > 0 ? "共鳴で動けない（今が攻めどき）"
                         : (e.BossStage >= 2 ? (e.SlamCd == 1 ? "鏡の奔流が次に来る（紫）" : $"鏡の奔流まで あと{e.SlamCd}ターン") : (e.SlamCd == 1 ? "大剣が次に来る（紫）" : $"大剣まで あと{e.SlamCd}ターン"))
                           + (e.BombCells.Count > 0 ? "・光柱が落ちる（紫）" : e.BlastCd > 0 ? "・光柱は休み" : "・光柱を予告する");
            }
            if (e.ShootsArrows) return $"矢 {e.Facing.ToLabel()}";
            if (e.HasShield) return $"盾 {e.Facing.ToLabel()}（正面 -{RulesEngine.ShieldBlock}）";
            return null;
        }

        string forecastKey;
        /// <summary>敵ごとの「このあとの兵種の行動」の説明（敵の予定欄に出す）。キーはユニットの Id。</summary>
        readonly Dictionary<int, string> traitForecast = new Dictionary<int, string>();

        /// <summary>
        /// 今組んでいる命令をコピーした後の盤面を、ルール（TurnRules / PhaseRules）で試算して、
        /// 矢の通るマス・追跡兵／逃げ兵の移動先・このラウンドに燃えるマス・回復ルーンを盤面に塗る。命令を組み替えるたびに更新。
        /// 兵種の行動は敵A → 敵B の順に1体ずつ試算する（前の敵の行動の結果も反映した、実際と同じ予告）。
        /// </summary>
        void UpdateForecast()
        {
            var grid = turn.Grid;
            var model = grid.Model;
            if (model == null) return;
            if (CombatV2) { forecastKey = null; UpdateThreatMarksV2(); return; } // 【RC8】危険の一覧（Rules/ThreatModel）から毎フレーム（敵と一緒に滑る・マウスで光る）
            grid.ShowMarksAt("arrowDim", new List<Vector3>(), ArrowMarkColor); grid.ShowMarksAt("arrowGone", new List<Vector3>(), ArrowMarkColor); grid.ShowMarksAt("telegraphDim", new List<Vector3>(), TelegraphMarkColor); // 【RC8】前の戦闘の塗りを残さない
            bool planning = turn.State == BattleState.PlayerInput && ShowingBattle;
            string key = $"{turn.CurrentStage.name}|{turn.TurnNumber}|{turn.State}|{model.Environment.Round}|{model.Environment.RuneIndex}|{ShowingBattle}|{Predicting}|" +
                         string.Join(";", model.Units.Select(u => $"{u.Position.x},{u.Position.y},{u.Hp},{u.BossStage},{u.Barrier},{(u.Dormant ? 1 : 0)},{(int)u.Facing},{u.StunRounds},{(u.Silenced ? 1 : 0)}")) + "|" + // 【Puzzle v2】敵の状態が変わったら作り直す

                         string.Join(",", turn.PlannedCommands.Select(c => c.ToDisplayText()));
            if (key == forecastKey) return;
            forecastKey = key;

            var arrows = new List<Vector2Int>();
            var steps = new List<Vector2Int>();
            var bombNext = new List<Vector2Int>();
            traitForecast.Clear();
            enemyResult.Clear();
            predictionText = null;
            var sim = model.Clone();
            var simArrows = new List<Vector2Int>();
            // 【Puzzle v2・2026-10-06 本人プレイ】敵の今の攻撃範囲は、ターンの始めから「必ず」出す（札を選んでも・命令を組んでも・完全予測でも消さない）。
            //   赤＝兵の攻撃（剣・槍・弓・砲兵の爆撃）、紫＝ボス・仕掛けの予告。コピーの後の未来は出さない（完全予測は別の薄い層）。
            if (planning)
            {
                // 【RC5・レビュー3】組んだ命令で押す・引く・入れ替える敵は、赤も動いた先で出す（攻撃はあなたの命令の後の位置から：前は赤が元の位置のままで、予告に無いマスで当たって見えた）
                var redModel = model;
                if (model.V4 && turn.PlannedCommands.Count > 0)
                {
                    var after = PlayerPreview.AfterPlanned(model, turn.PlannedCommands, turn.PlayerAttackBonusEachTurn);
                    foreach (var e in model.Enemies)
                    {
                        var a = after.UnitById(e.Id);
                        if (!e.IsAlive || a == null || !a.IsAlive || (a.Position == e.Position && a.Facing == e.Facing)) continue;
                        if (redModel == model) redModel = model.Clone();
                        var u = redModel.UnitById(e.Id);
                        u.Position = a.Position; u.Facing = a.Facing;
                    }
                }
                arrows.AddRange(BaseThreat(redModel));
                foreach (var e in model.Enemies)
                {
                    if (!e.IsAlive) continue;
                    string t = TraitIntent(e, model); // 【レビュー】追跡兵・逃げ兵も「近づく／離れる」を出す（前は「まねるだけ」と出ていた）
                    if (t != null) traitForecast[e.Id] = t;
                }
            }
            // 【検証用】完全予測（FullPrediction）：命令を組むたびに1ラウンド試算して、コピー後の攻撃範囲と結果まで出す。命令が0のときは出さない
            if (planning && Predicting && turn.PlannedCommands.Count > 0)
            {
                var events = new List<BattleEvent>();
                sim.BeginRound(); // 実行と同じ：ラウンドの始まり・先兵の +1（レビュー）
                if (turn.PlayerAttackBonusEachTurn > 0) sim.Player.AttackBonus += turn.PlayerAttackBonusEachTurn;
                var outcome = TurnRules.PlayCommandsAndCopies(sim, turn.PlannedCommands.Select(c => c.Clone()).ToList(), turn.EnemyInnateAction, events);
                if (outcome == TurnOutcome.Continue)
                {
                    foreach (var e in sim.Enemies.ToList())
                    {
                        if (!e.IsAlive) continue;
                        if (e.Kind != EnemyKind.Chaser && e.Kind != EnemyKind.Fleer)
                        {
                            // 【Combat V2】剣（前方3マス）・槍（正面2マス）・矢・予告済みの爆発：当たるマスを（完全予測の薄い層に）
                            simArrows.AddRange(PhaseRules.TraitCells(sim, e));
                            string t = TraitIntent(e, sim);
                            if (t != null) traitForecast[e.Id] = t;
                        }
                        if (e.Kind == EnemyKind.Chaser || e.Kind == EnemyKind.Fleer)
                        {
                            var t = PhaseRules.StepTarget(sim, e);
                            if (t.HasValue) steps.Add(t.Value);
                            traitForecast[e.Id] = t.HasValue ? $"{(e.Kind == EnemyKind.Chaser ? "近づく" : "離れる")} {DirText(t.Value - e.Position)}" : "動かない";
                        }
                        else if (e.HasShield)
                            traitForecast[e.Id] = $"盾 {e.Facing.ToLabel()}（正面 -{RulesEngine.ShieldBlock}）";
                        PhaseRules.TraitAction(sim, e, events); // 次の敵の予告のために、この敵の行動を試算に反映
                        if (e.Kind == EnemyKind.Bomber) bombNext.AddRange(e.BombCells); // 実行したら次に予告される爆発
                        if (!sim.Player.IsAlive || PuzzleRules.Won(sim)) break;
                    }
                    if (sim.Player.IsAlive && !PuzzleRules.Won(sim)) PhaseRules.EnvironmentPhase(sim, events, null);
                }
                Summarize(sim, events);
            }
            grid.ShowMarks("arrow", arrows, ArrowMarkColor);
            grid.ShowMarks("arrowSim", simArrows, SimMarkColor, 0.6f); // 【検証用】完全予測：コピーの後の範囲（今の範囲の上に薄く）
            // 【Combat V2】砲兵：予告済みの爆発（命令に関係なく、次の兵種フェーズで必ず爆発する）と、実行したら次に予告されるマス
            grid.ShowMarks("bomb", planning ? ThreatMap.Bombs(model) : new List<Vector2Int>(), BombMarkColor, 0.7f);
            // 【Puzzle v2】紫：ボス・仕掛けの予告（光柱・大剣・石像の視線）
            grid.ShowMarks("telegraph", planning ? ThreatMap.Telegraph(model) : new List<Vector2Int>(), TelegraphMarkColor, 0.86f);
            grid.ShowMarks("bombNext", bombNext, BombNextMarkColor, 0.45f);
            // 【Combat V3】騎士V3：炎の波の範囲（カウントダウン中はずっと薄く見せる。1 になったら赤くなる）
            var wave = new List<Vector2Int>();
            if (ShowingBattle)
                foreach (var e in model.Enemies)
                    if (e.IsAlive && e.Kind == EnemyKind.KnightV3 && e.WaveCd > 1) wave.AddRange(PhaseRules.WaveCells(model, e.WaveCount));
            grid.ShowMarks("wave", wave, BombNextMarkColor, 0.5f);
            grid.ShowMarks("step", steps, StepMarkColor, 0.6f);
            var env = model.Environment;
            // 【RC2】落石・レーザーは炎と違う色の下地（絵の印は BattleUIV4.DrawV4Hazards）
            string hz = turn.CurrentStage != null ? turn.CurrentStage.hazardName ?? "" : "";
            var fireNow = hz.Contains("レーザー") ? new Color(1f, 0.2f, 0.35f, 0.22f) : hz.Contains("落石") ? new Color(0.75f, 0.65f, 0.5f, 0.3f) : FireMarkColor;
            var fireNext = hz.Contains("レーザー") ? new Color(1f, 0.4f, 0.5f, 0.08f) : hz.Contains("落石") ? new Color(0.5f, 0.45f, 0.4f, 0.14f) : FireNextMarkColor;
            grid.ShowMarks("fire", ShowingBattle && !model.V4 ? env.FireCellsNow.ToList() : new List<Vector2Int>(), fireNow);
            grid.ShowMarks("fireNext", ShowingBattle && !model.V4 ? env.FireCellsNext.Where(c => !env.FireCellsNow.Contains(c)).ToList() : new List<Vector2Int>(), fireNext);
            var rune = new List<Vector2Int>();
            if (ShowingBattle && env.RuneCell.HasValue) rune.Add(env.RuneCell.Value);
            grid.ShowMarks("rune", rune, RuneMarkColor, 0.5f);
            // 【検証中】回復マス（一度踏むと消える。あなたも敵も使える）
            grid.ShowMarks("heal", ShowingBattle ? env.HealTiles.ToList() : new List<Vector2Int>(), RuneMarkColor, 0.62f);
            var runeNext = new List<Vector2Int>();
            if (ShowingBattle && env.NextRuneCell.HasValue) runeNext.Add(env.NextRuneCell.Value);
            grid.ShowMarks("runeNext", runeNext, RuneNextMarkColor, 0.5f);
        }

        /// <summary>
        /// 【検証用】完全予測（命令を組むたびに、コピー後の攻撃範囲と結果まで出す）。既定は OFF＝ターン開始時点の危険範囲だけ（Combat V3、本人の希望）。
        /// 右下の「予測」ボタンで切り替える。
        /// </summary>
        public static bool FullPrediction;
        /// <summary>完全予測を出すか（切替が ON、または ACT1 の観測士）。</summary>
        bool Predicting => FullPrediction || (turn != null && turn.ActFullPrediction);

        // 【仮UI】結果の予測（命令を組むたびに、ルールで1ラウンド試算した結果）
        readonly Dictionary<int, string> enemyResult = new Dictionary<int, string>();
        readonly Dictionary<int, bool> enemyHitsPlayer = new Dictionary<int, bool>();
        string predictionText;
        bool predictionDanger;

        /// <summary>試算の出来事から「敵ごとに何が起きるか」（あなたに何ダメージ・どの敵に何ダメージ・自分が何ダメージ受けるか）と全体の要約を作る。</summary>
        void Summarize(BattleModel sim, List<BattleEvent> events)
        {
            enemyHitsPlayer.Clear();
            int playerLoss = 0, enemyLoss = 0;
            var dealt = new Dictionary<int, Dictionary<string, int>>();
            var taken = new Dictionary<int, int>();
            foreach (var ev in events)
            {
                if (ev.Type != BattleEventType.Damaged || ev.Unit == null) continue;
                if (ev.Unit.IsPlayer) playerLoss += ev.Amount; else enemyLoss += ev.Amount;
                if (!ev.Unit.IsPlayer) taken[ev.Unit.Id] = (taken.TryGetValue(ev.Unit.Id, out int t0) ? t0 : 0) + ev.Amount;
                var src = ev.Cause?.Source;
                if (src == null || src.IsPlayer || src.Id == ev.Unit.Id) continue;
                if (!dealt.TryGetValue(src.Id, out var d)) dealt[src.Id] = d = new Dictionary<string, int>();
                string name = ev.Unit.IsPlayer ? "あなた" : ev.Unit.Name;
                d[name] = (d.TryGetValue(name, out int v) ? v : 0) + ev.Amount;
                if (ev.Unit.IsPlayer) enemyHitsPlayer[src.Id] = true;
            }
            foreach (var e in sim.Enemies)
            {
                var parts = new List<string>();
                if (dealt.TryGetValue(e.Id, out var d)) parts.AddRange(d.OrderBy(k => k.Key == "あなた" ? 0 : 1).Select(k => $"{k.Key}に -{k.Value}"));
                string result = parts.Count > 0 ? string.Join("・", parts) : "誰にも当たらない";
                if (taken.TryGetValue(e.Id, out int tk)) result += $"　／　受ける -{tk}{(e.IsAlive ? "" : "（倒れる）")}";
                enemyResult[e.Id] = result;
            }
            predictionDanger = playerLoss > 0;
            predictionText = !sim.Player.IsAlive ? $"予測：あなたが倒れる（-{playerLoss}）"
                           // 【Puzzle v2】パズルの部屋では「勝利」を出さない（パズルブレイカー：観測士の完全予測で、緑の文が出るまで並べ替えるだけで解けた）
                           : PuzzleRules.Won(sim) && !IsPuzzleUi ? (sim.Goal == RoomGoal.Defeat ? "予測：敵が全滅して勝利" : "予測：目標を達成して勝利")
                           : $"予測：あなた {(playerLoss > 0 ? $"-{playerLoss}" : "無傷")}　／　" + (enemyLoss > 0 ? $"敵の合計 -{enemyLoss}" : "敵へのダメージなし");
        }

        static string DirText(Vector2Int d) => d.x > 0 ? "→右" : d.x < 0 ? "←左" : d.y > 0 ? "↑上" : "↓下";

        /// <summary>【仮UI】盤面のユニットに出すアイコン（通常兵は無し）。</summary>
        static string UnitIcon(UnitModel m)
        {
            switch (m.Kind)
            {
                case EnemyKind.Archer: return "弓";
                case EnemyKind.Shield: return "盾";
                case EnemyKind.Chaser: return "追";
                case EnemyKind.Fleer: return "逃";
                case EnemyKind.Knight: return "騎";
                case EnemyKind.Swordsman: return "剣";
                case EnemyKind.Spearman: return "槍";
                case EnemyKind.Bomber: return "砲";
                case EnemyKind.MirrorSoldier: return "鏡";
                case EnemyKind.KnightV2: return "王";
                case EnemyKind.KnightV3: return "王";
            }
            return m.CopyTrait == CopyTrait.Mirror ? "鏡" : m.CopyTrait == CopyTrait.Reverse ? "逆" : null;
        }

        static string FacingGlyph(Direction d)
        {
            switch (d)
            {
                case Direction.Up: return "▲";
                case Direction.Down: return "▼";
                case Direction.Left: return "◀";
                default: return "▶";
            }
        }

        /// <summary>盤面のユニットの近くに名前と HP、受けたダメージを出す。</summary>
        void DrawUnitLabels(float scale)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var unit in turn.Grid.Units)
            {
                if (!unit.IsAlive && !unit.IsPlayer && turn.Enemies.Count > 1) continue; // 倒れた敵の表示は消す
                // 敵は上側、あなたは下側に表示（上下に並んだときに重ならないように）
                float offsetY = unit.IsPlayer ? -0.62f : 0.62f;
                Vector3 screen = cam.WorldToScreenPoint(unit.transform.position + new Vector3(0f, offsetY, 0f));
                var pos = new Vector2(screen.x / scale, (Screen.height - UiStyles.LetterboxY - screen.y) / scale);
                var old = GUI.contentColor;
                GUI.contentColor = unit.IsPlayer ? PlayerTextColor : Color.Lerp(unit.BaseColor, Color.white, 0.4f);
                var m = unit.Model;
                string kindText = !unit.IsPlayer && m.Kind != EnemyKind.Normal ? PhaseRules.KindName(m.Kind) + " " : "";
                GUI.Label(new Rect(pos.x - 70, pos.y - 12, 140, 24), $"{(unit.IsPlayer ? "あなた" : unit.DisplayName)} {kindText}{unit.Hp}", new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter });
                // 【仮UI】兵種・コピーの種類のアイコン（ユニットの真ん中に大きく：弓・盾・追・逃・騎・鏡）
                string icon = UnitIcon(m);
                if (!unit.IsPlayer && unit.IsAlive && icon != null && !unit.Themed) // テーマの絵なら絵で兵種が分かるので出さない
                {
                    Vector3 c = cam.WorldToScreenPoint(unit.transform.position);
                    var cp = new Vector2(c.x / scale, (Screen.height - UiStyles.LetterboxY - c.y) / scale);
                    var iconStyle = new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 24 };
                    GUI.contentColor = new Color(0f, 0f, 0f, 0.6f);
                    GUI.Label(new Rect(cp.x - 19, cp.y - 15, 40, 32), icon, iconStyle);
                    GUI.contentColor = Color.white;
                    GUI.Label(new Rect(cp.x - 20, cp.y - 16, 40, 32), icon, iconStyle);
                }
                // 【検証中】向き：盾（青い「盾」）・弓（赤い矢印）を、向いている側に表示
                bool melee = m.Kind == EnemyKind.Spearman || m.Kind == EnemyKind.KnightV2; // 剣兵は向きに関係なく周囲4マスなので向きを出さない
                if (!unit.IsPlayer && unit.IsAlive && (m.HasShield || m.ShootsArrows || melee))
                {
                    Vector3 side = cam.WorldToScreenPoint(unit.transform.position + (Vector3)(Vector2)m.Facing.ToOffset() * 0.42f);
                    var sp = new Vector2(side.x / scale, (Screen.height - UiStyles.LetterboxY - side.y) / scale);
                    GUI.contentColor = m.HasShield ? new Color(0.45f, 0.75f, 1f) : melee && !m.ShootsArrows ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.35f, 0.35f);
                    GUI.Label(new Rect(sp.x - 20, sp.y - 13, 40, 26), m.HasShield ? "盾" : FacingGlyph(m.Facing), new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                }
                if (unit.DamagePopupTimer > 0f)
                {
                    // 受けたダメージ（黄色の -4）・回復（緑の +3）を少しずつ上に浮かせて表示
                    bool heal = unit.LastDamage < 0;
                    GUI.contentColor = heal ? new Color(0.4f, 1f, 0.5f) : Color.yellow;
                    float rise = (1f - unit.DamagePopupTimer) * 20f;
                    GUI.Label(new Rect(pos.x - 60, pos.y - 38 - rise, 120, 30), heal ? $"+{-unit.LastDamage}" : $"-{unit.LastDamage}", new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
                }
                GUI.contentColor = old;
            }
        }

        /// <summary>【Combat V2】砲兵の予告済みの爆発マスに「爆」を出す（炎床の橙と見分けるため）。</summary>
        void DrawBombTags(float scale)
        {
            var cam = Camera.main;
            if (cam == null || turn.Grid.Model == null) return;
            var style = new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            var old = GUI.contentColor;
            foreach (var e in turn.Grid.Model.Enemies)
            {
                if (!e.IsAlive) continue;
                if (e.Kind == EnemyKind.KnightV3)
                {
                    // カウントダウン（王の上）
                    Vector3 head = cam.WorldToScreenPoint(turn.Grid.GridToWorld(e.Position) + new Vector3(0f, 0.95f, 0f));
                    var hp = new Vector2(head.x / scale, (Screen.height - UiStyles.LetterboxY - head.y) / scale);
                    var chip = new Rect(hp.x - 70, hp.y - 14, 140, 26);
                    DrawRect(chip, new Color(0f, 0f, 0f, 0.65f));
                    GUI.contentColor = new Color(1f, 0.9f, 0.4f);
                    GUI.Label(chip, CountdownText(e), new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold });
                    var tags = new List<(Vector2Int, string)>();
                    if (e.SlamCd == 1) tags.AddRange(e.SlamCells.Select(c => (c, "剣")));
                    if (e.WaveCd == 1) tags.AddRange(PhaseRules.WaveCells(turn.Grid.Model, e.WaveCount).Select(c => (c, "炎")));
                    foreach (var (c, t) in tags)
                    {
                        Vector3 tp = cam.WorldToScreenPoint(turn.Grid.GridToWorld(c));
                        var tpos = new Vector2(tp.x / scale, (Screen.height - UiStyles.LetterboxY - tp.y) / scale);
                        GUI.contentColor = new Color(0f, 0f, 0f, 0.7f);
                        GUI.Label(new Rect(tpos.x - 19, tpos.y - 13, 40, 30), t, style);
                        GUI.contentColor = new Color(1f, 0.95f, 0.3f);
                        GUI.Label(new Rect(tpos.x - 20, tpos.y - 14, 40, 30), t, style);
                    }
                    if (e.BlastCd != 1) continue; // 爆発の予告マスは、カウントが 1 のときだけ下で「爆」を出す
                }
                if (turn.Grid.Theme != null) continue; // 【検証中】テーマの盤面では照準の絵が「爆」の代わり
                foreach (var c in e.BombCells)
                {
                    Vector3 sp = cam.WorldToScreenPoint(turn.Grid.GridToWorld(c));
                    var pos = new Vector2(sp.x / scale, (Screen.height - UiStyles.LetterboxY - sp.y) / scale);
                    GUI.contentColor = new Color(0f, 0f, 0f, 0.7f);
                    GUI.Label(new Rect(pos.x - 19, pos.y - 13, 40, 30), "爆", style);
                    GUI.contentColor = new Color(1f, 0.95f, 0.3f);
                    GUI.Label(new Rect(pos.x - 20, pos.y - 14, 40, 30), "爆", style);
                }
            }
            GUI.contentColor = old;
        }

        void DrawTutorial(Rect r)
        {
            var stage = turn.CurrentStage;
            string text = turn.TurnNumber >= 2 && !string.IsNullOrEmpty(stage.hintAfterFirstTurn) ? stage.hintAfterFirstTurn : stage.tutorialText;
            if (string.IsNullOrEmpty(text)) return;
            DrawRect(r, PanelColor);
            GUI.Label(new Rect(r.x + 10, r.y + 6, r.width - 20, r.height - 12), text, Small);
            if (turn.IsDeckMode && run != null && run.InRun &&
                GUI.Button(new Rect(r.xMax - 190, r.yMax - 30, 184, 26), "RUN をやめる（チュートリアルへ）", new GUIStyle(Button) { fontSize = 12 }))
                run.ExitToTutorial();
        }

        float resultShownAt = -1f;

        /// <summary>【RC11】部屋の主が倒れた瞬間（大きな「○○ 撃破！」を出してから勝ちの欄）。</summary>
        public static float LeaderDownAt = -99f;
        static string leaderDownName, leaderDownSub;
        public const float LeaderDownHold = 2.6f;

        public static void ShowLeaderDown(UnitModel leader, int collapsed)
        {
            LeaderDownAt = Time.unscaledTime;
            leaderDownName = PhaseRules.KindName(leader.Kind);
            leaderDownSub = collapsed <= 0 ? "部屋の主を倒した" : leader.Kind == EnemyKind.MirrorLord ? "主が倒れ、残った敵も崩れ去った" : "主が倒れ、護衛は逃げ出した"; // 【RC11・QA】写し身がいないのに「写し身も」と出ていた
        }

        /// <summary>【RC11】「○○ 撃破！」の大きな文字（勝ちの欄の前・2.6 秒・クリックか Enter で先へ）。出している間は true。</summary>
        bool DrawLeaderDownBanner()
        {
            float t = Time.unscaledTime - LeaderDownAt;
            if (t < 0f || t > LeaderDownHold || leaderDownName == null) return false;
            var ev = Event.current;
            if (t > 0.9f && ev != null && (ev.type == EventType.MouseDown || (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.Space || ev.keyCode == KeyCode.KeypadEnter)))) { ev.Use(); LeaderDownAt = -99f; return false; }
            float W = Screen.width / GuiScale, H = Screen.height / GuiScale;
            float k = Mathf.Clamp01(t / 0.35f);
            float a = t > LeaderDownHold - 0.4f ? Mathf.Clamp01((LeaderDownHold - t) / 0.4f) : 1f;
            var band = new Rect(0, H * 0.36f, W, 120);
            DrawRect(band, new Color(0f, 0f, 0f, 0.78f * a));
            DrawRect(new Rect(0, band.y, W, 3), new Color(1f, 0.85f, 0.4f, a));
            DrawRect(new Rect(0, band.yMax - 3, W, 3), new Color(1f, 0.85f, 0.4f, a));
            var big = new GUIStyle(Big) { fontSize = Mathf.RoundToInt(Mathf.Lerp(64f, 44f, k)), alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(new Rect(0, band.y + 8, W, 70), $"{leaderDownName} 撃破！", big, new Color(1f, 0.88f, 0.45f, a));
            ShadowLabel(new Rect(0, band.y + 76, W, 30), leaderDownSub, new GUIStyle(Label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(0.9f, 0.93f, 1f, a));
            return true;
        }

        void DrawResult(Rect r)
        {
            if (turn.State != BattleState.Cleared && turn.State != BattleState.GameOver) { resultShownAt = -1f; return; }
            if (turn.State == BattleState.Cleared && DrawLeaderDownBanner()) { resultShownAt = -1f; return; } // 【RC11】ボス・中ボスは「撃破！」を見せてから
            if (resultShownAt < 0f) resultShownAt = Time.unscaledTime;
            bool cleared = turn.State == BattleState.Cleared;
            bool inRun = turn.IsDeckMode && run != null && run.InRun;
            bool actBattle = act != null && act.InBattle;
            // 【通常リリース版・画面の直し 25】ボスを倒した窓は大きく「鏡の主 撃破！」（前はふつうの勝利と同じ小さい窓）
            bool bossDown = cleared && actBattle && act.Run != null && act.Run.Current != null && act.Run.Current.Kind == RoomKind.Boss;
            float top = 0f;
            if (bossDown) { top = 44f; r = new Rect(r.center.x - 240f, r.y - 50f, 480f, r.height + top + 10f); }
            if (actBattle && UseShowcaseLayout) DrawRect(new Rect(0, 0, BoardFracNow * (Screen.width / UiStyles.GuiScale), RefHeight), new Color(0f, 0f, 0f, bossDown ? 0.68f : 0.5f)); // 【通常リリース版・画面の直し 8】窓の後ろの盤面を暗く（窓の字が盤面の模様に溶けていた）
            DrawUi(r, "panel", new Color(0f, 0f, 0f, 0.85f));
            if (bossDown) { DrawFrame(r, new Color(1f, 0.85f, 0.4f, 0.9f), 3f); DrawRect(new Rect(r.x, r.y, r.width, 5f), new Color(1f, 0.85f, 0.4f)); }
            var old = GUI.contentColor;
            GUI.contentColor = cleared ? Color.yellow : new Color(1f, 0.62f, 0.55f); // 【通常リリース版・画面の直し 8】「敗北」は読める色（前は弱い灰色）
            string title = cleared ? (inRun || actBattle ? "勝利！" : "STAGE CLEAR!") : (inRun ? "敗北… RUN 終了" : actBattle ? "敗北…" : "GAME OVER");
            if (bossDown)
            {
                var boss = turn.Grid.Model.Enemies.FirstOrDefault(e => IsBossKind(e.Kind));
                title = (boss != null ? PhaseRules.KindName(boss.Kind) : "ボス") + " 撃破！";
                GUI.contentColor = old;
                ShadowLabel(new Rect(r.x, r.y + 14, r.width, 70), title, new GUIStyle(Big) { fontSize = 40, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.88f, 0.45f));
            }
            else GUI.Label(new Rect(r.x, r.y + 14, r.width, 40), title, new GUIStyle(Big) { alignment = TextAnchor.MiddleCenter });
            GUI.contentColor = old;
            string info = inRun && cleared ? $"ターン数：{turn.TurnNumber}　残り HP {turn.Player.Hp}（次へ持ち越し）" : $"かかったターン数：{turn.TurnNumber}";
            if (turn.CurrentStage != null && turn.CurrentStage.copySandbox)
            {
                // 【検証中・Copy Core V2】サンドボックス：コピーがどれだけ効いたか
                int kills = 0, copyKills = 0, copyHits = 0;
                var lastCause = new Dictionary<UnitModel, DamageCause>();
                foreach (var e in turn.BattleEvents)
                {
                    if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Cause != null) { lastCause[e.Unit] = e.Cause; if (!e.Unit.IsPlayer && CopyCore.IsCopyOrigin(e.Cause) && e.Amount > 0) copyHits++; }
                    if (e.Type == BattleEventType.BarrierHit || e.Type == BattleEventType.BarrierBroken || e.Type == BattleEventType.CrystalBurst) copyHits++;
                    if (e.Type == BattleEventType.Died && e.Unit != null && !e.Unit.IsPlayer) { kills++; if (lastCause.TryGetValue(e.Unit, out var c) && CopyCore.IsCopyOrigin(c)) copyKills++; }
                }
                var m = turn.Grid.Model;
                info = $"{turn.TurnNumber}ターン・方式 {CopyCore.Name(m.CopyMode)}{(m.CopyEnabled ? "" : "（COPY OFF）")}\nまねで倒した {copyKills}/{kills}・まねが効いた回数 {copyHits}";
            }
            if (actBattle)
            {
                // 【検証中】監査 UI-13：ACT1 では被ダメの内訳と、負けたときは最後の一撃
                var src = new Dictionary<string, int>();
                string last = null;
                foreach (var e in turn.BattleEvents)
                {
                    if (e.Type != BattleEventType.Damaged || e.Unit == null || !e.Unit.IsPlayer || e.Amount <= 0) continue;
                    string k = UiStyles.DamageCauseLabel(e) ?? "その他";
                    src[k] = (src.TryGetValue(k, out var v) ? v : 0) + e.Amount;
                    last = $"{k} -{e.Amount}";
                }
                int sum = src.Values.Sum();
                string why = !cleared && CombatV2 ? DeathCauseText() : null; // 【RC8】「何で負けた？」：最後のターンに受けた分を原因（記号つき）ごと
                info = $"{turn.TurnNumber}ターン・被ダメ {sum}" + (sum > 0 ? $"（{string.Join("・", src.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value))}）" : "（無傷）") +
                       (cleared ? $"　残り HP {turn.Player.Hp}" : (act != null && act.Run != null ? $"（入ったときの HP {act.BattleStartHp}）" : "") + (why != null ? $"\n{why}" : last != null ? $"\n最後の一撃：{last}" : "")); // 【RC11・初見テスト「被ダメ 2 で HP 0？」】負けたときは、この戦闘に入ったときの HP も
            }
            var infoSt = new GUIStyle(Label) { alignment = TextAnchor.UpperCenter, wordWrap = false, fontSize = actBattle ? 13 : Label.fontSize };
            GUI.Label(new Rect(r.x + 8, r.y + 52 + top, r.width - 16, 56), WrapJa(info, infoSt, r.width - 16), infoSt); // 【通常リリース版・画面の直し 16】単語の途中で改行しない
            if (actBattle && cleared && !InTutorialUi && !turn.BattleEvents.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0))
            {
                // 【RC11・本人「ノーダメクリアで紋章みたいなのが付くのは面白い」】無傷の勝利の印（記録とルーン片 +5。強さは変わらない）
                var badge = new Rect(r.xMax - 92, r.y + 8, 84, 30);
                DrawRect(badge, new Color(0.35f, 0.25f, 0.02f, 0.95f));
                DrawFrame(badge, new Color(1f, 0.85f, 0.35f), 2f);
                ShadowLabel(badge, "★ 無傷", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.88f, 0.45f));
            }

            float bw = 140;
            if (act != null && act.InBattle)
            {
                // 【検証中】ACT1：勝てば報酬へ、負ければ結果へ
                // 【RC5・コードレビュー】キーボードだけでも先へ（Enter・Space）。最後の命令の Enter が残らないよう、出てから 0.5 秒は受けない
                var kev = Event.current;
                bool key = kev.type == EventType.KeyDown && (kev.keyCode == KeyCode.Return || kev.keyCode == KeyCode.KeypadEnter || kev.keyCode == KeyCode.Space) && Time.unscaledTime - resultShownAt > 0.5f;
                if (key) kev.Use();
                // 【通常リリース版・画面の直し 8】ボスを倒したら次は報酬ではなく ACT1 クリアの画面
                if (GUI.Button(new Rect(r.center.x - bw * 0.75f, r.y + 110 + top, bw * 1.5f, 48), (cleared ? (bossDown ? "ACT1 クリアへ" : "報酬へ") : "結果へ") + "（Enter）", Button) || key) { Sfx.Play(Sfx.Kind.Button); act.ProceedAfterBattle(); }
                return;
            }
            if (inRun)
            {
                if (GUI.Button(new Rect(r.center.x - bw * 0.75f, r.y + 110, bw * 1.5f, 48), "次へ", Button)) { Sfx.Play(Sfx.Kind.Button); run.ProceedAfterBattle(); }
                return;
            }
            if (GUI.Button(new Rect(r.x + 20, r.y + 110, bw, 48), "リトライ", Button)) turn.LoadStage(turn.CurrentStageIndex);
            bool canGoRun = cleared && !turn.HasNextStage && run != null;
            GUI.enabled = cleared && (turn.HasNextStage || canGoRun);
            if (GUI.Button(new Rect(r.xMax - 20 - bw, r.y + 110, bw, 48), canGoRun ? "RUN へ進む" : "次のステージへ", Button))
            {
                if (canGoRun) run.OpenRunStart();
                else turn.LoadStage(turn.CurrentStageIndex + 1);
            }
            GUI.enabled = true;
        }

        // ───────── 右側（チュートリアル：今までどおり） ─────────

        void DrawTutorialPanel(Rect area)
        {
            float x = area.x, y = area.y, w = area.width;

            // 手札（技ボタン）
            GUI.Label(new Rect(x, y, w, 24), "手札（技を選ぶ。同じ技は1ターン1回まで）", Label);
            y += 28;
            float cardWidth = 112, cardGap = 8;
            for (int i = 0; i < turn.CurrentStage.availableCommands.Count; i++)
            {
                var command = turn.CurrentStage.availableCommands[i];
                if (command == null) continue;
                bool selected = pendingCommand == command;
                GUI.enabled = turn.CanAddCommand(command) && !selected;
                string caption = turn.IsUsedThisTurn(command) ? $"{command.displayName}\n(使用済み)" : command.displayName;
                if (GUI.Button(new Rect(x + i * (cardWidth + cardGap), y, cardWidth, 46), caption, Button)) Select(command, null);
                GUI.enabled = true;
            }
            y += 54;

            DrawRect(new Rect(x, y, w, 128), PanelColor);
            DrawDirectionPicker(new Rect(x + 8, y + 6, w - 16, 116), false);
            y += 136;

            y = DrawSlotsAndPreview(x, y, w, 30);

            DrawLog(new Rect(x, y, w, area.yMax - y - 74));
            DrawModeButtons(new Rect(x, area.yMax - 66, w, 64));
        }

        // ───────── 右側（RUN：手札カード） ─────────

        void DrawRunPanel(Rect area)
        {
            float x = area.x, y = area.y, w = area.width;
            var deck = turn.Deck;
            bool input = turn.State == BattleState.PlayerInput;

            string retainText = deck.UsesRetainChoice ? $" ／ 保留 {turn.RetainedCards.Count}/{deck.MaxRetain}" : "（未使用カードは手札に残る）";
            GUI.Label(new Rect(x, y, w, 26),
                $"手札 {deck.Hand.Count}/{deck.HandLimit}　山札 {deck.DrawPile.Count} ／ 捨て札 {deck.DiscardPile.Count}{retainText}", Label);
            y += 26;

            // （RUN のみ）歩く＝基本命令 ＋ 手札（RUN は5枚、A/B は上限7枚）
            bool hasWalk = turn.BasicWalk != null;
            // 【検証中】大きい手札（上限10）は2段に並べる
            int rows = deck.HandLimit > 7 ? 2 : 1;
            int perRow = Mathf.CeilToInt(deck.HandLimit / (float)rows);
            int columns = (hasWalk ? 1 : 0) + perRow;
            float gap = 5, cw = (w - gap * (columns - 1)) / columns, ch = rows > 1 ? 66 : 108;
            if (hasWalk)
            {
                bool walkSelected = pendingCommand == turn.BasicWalk && pendingCard == null;
                if (DrawCard(new Rect(x, y, cw, ch), turn.BasicWalk, "何回でも", input && turn.CanAddMore, walkSelected, !input || !turn.CanAddMore))
                    Select(turn.BasicWalk, null);
            }
            int offset = hasWalk ? 1 : 0;
            for (int i = 0; i < deck.HandLimit; i++)
            {
                int row = i / perRow, col = i % perRow;
                var rect = new Rect(x + (cw + gap) * (col + offset), y + row * (ch + 4), cw, ch);
                if (i >= deck.Hand.Count) { DrawFrame(rect, new Color(1f, 1f, 1f, 0.1f)); continue; }
                var card = deck.Hand[i];
                bool planned = turn.IsCardPlanned(card);
                bool retained = turn.RetainedCards.Contains(card);
                // ドローの演出：引いたばかりのカードは上から落ちてくる
                float age = Time.time - card.DrawnAt - i * 0.06f;
                float t = Mathf.Clamp01(age / 0.3f);
                var drawRect = new Rect(rect.x, rect.y - (1f - t) * 30f, rect.width, rect.height);
                string footer = planned ? $"スロット{SlotNumberOf(card)}" : retained ? "保留中" : "";
                if (DrawCard(drawRect, card.Data, footer, turn.CanAddCard(card), pendingCard == card || retained, planned, false, Mathf.Lerp(0.2f, 1f, t)))
                    Select(card.Data, card);
                // 保留ボタン（RUN 方式のみ）
                if (deck.UsesRetainChoice)
                {
                    GUI.enabled = input && !planned && (retained || turn.RetainedCards.Count < deck.MaxRetain);
                    if (GUI.Button(new Rect(rect.x, rect.yMax + 2, rect.width, 22), retained ? "保留中 ✓" : "保留する", new GUIStyle(Button) { fontSize = 12 }))
                        turn.ToggleRetain(card);
                    GUI.enabled = true;
                }
            }
            y += ch * rows + 4 * (rows - 1) + (deck.UsesRetainChoice ? 28 : 6);

            DrawRect(new Rect(x, y, w, 92), PanelColor);
            DrawDirectionPicker(new Rect(x + 6, y + 4, w - 12, 86), true);
            y += 98;

            y = DrawSlotsAndPreview(x, y, w, 26);
            bool inRun = run != null && run.InRun;
            DrawLog(new Rect(x, y, w, area.yMax - y - (inRun ? 0 : 70)));
            if (!inRun) DrawModeButtons(new Rect(x, area.yMax - 66, w, 64));
        }

        int SlotNumberOf(CardInstance card)
        {
            for (int i = 0; i < turn.PlannedCommands.Count; i++)
                if (turn.PlannedCommands[i].SourceCard == card) return i + 1;
            return 0;
        }

        // ───────── 共通：スロット・ボタン・敵の予定 ─────────

        float DrawSlotsAndPreview(float x, float y, float w, float rowH)
        {
            bool input = turn.State == BattleState.PlayerInput;
            DrawSlots(new Rect(x, y, w, 24 + 3 * rowH), "命令スロット（あなたが ①→②→③ の順に実行）", turn.PlannedCommands,
                turn.State == BattleState.PlayerExecuting ? turn.ExecutingIndex : -1, PlayerTextColor, rowH, true);
            y += 24 + 3 * rowH + 6;

            GUI.enabled = input && turn.PlannedCommands.Count > 0;
            if (GUI.Button(new Rect(x, y, 100, 36), "1つ戻す", Button)) { turn.RemoveLastCommand(); ResetPending(); }
            if (GUI.Button(new Rect(x + 108, y, 100, 36), "全部消す", Button)) { turn.ClearCommands(); ResetPending(); }
            GUI.enabled = turn.CanExecute;
            if (GUI.Button(new Rect(x + w - 150, y, 150, 36), "実行", Button)) { ResetPending(); Sfx.Play(Sfx.Kind.Button); turn.ExecuteTurn(); }
            GUI.enabled = true;
            y += 42;

            // スロット → 敵の予定 のつながりを見せる
            var old = GUI.contentColor;
            GUI.contentColor = EnemyTextColor;
            string arrow = turn.State == BattleState.EnemyExecuting
                ? "▼ あなたの命令をまねした敵が実行中 ▼"
                : turn.IsDeckMode ? "▼ 実行すると、上の命令がそのまま敵に渡る（カードは渡らない・効果だけ）▼" : "▼ 実行すると、上の命令がそのまま敵に渡る ▼";
            GUI.Label(new Rect(x, y - 4, w, 20), arrow, new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
            GUI.contentColor = old;
            y += 18;

            if (turn.Enemies.Count > 1 || turn.Enemies.Any(e => e.Model.Kind != EnemyKind.Normal)) return DrawMultiEnemyPreview(x, y, w);

            // 敵の予定（Enemy Preview）：【1】固有行動（あれば）→ 【2】あなたから渡る命令
            var innate = turn.EnemyInnateAction;
            float innateHeight = innate != null ? 50 : 0;
            float noteHeight = innate != null ? 0 : 24;
            float previewHeight = innateHeight + 24 + 3 * rowH + noteHeight;
            DrawRect(new Rect(x - 4, y - 4, w + 8, previewHeight + 8), new Color(0.6f, 0.1f, 0.1f, 0.25f));
            float py = y;
            if (innate != null)
            {
                GUI.Label(new Rect(x, py - 2, w, 26), "敵の予定　【1】固有行動（毎ターン最初に必ず・まねされない）", Label);
                var row = new Rect(x, py + 24, w, 24);
                bool active = turn.IsExecutingInnate;
                DrawRect(row, active ? new Color(EnemyTextColor.r, EnemyTextColor.g, EnemyTextColor.b, 0.45f) : new Color(1f, 0.2f, 0.2f, 0.18f));
                GUI.Label(new Rect(row.x + 8, row.y + 1, row.width - 16, row.height), $"{(active ? "▶" : "")}{innate.displayName}：{innate.description}", Label);
                py += innateHeight;
            }
            DrawSlots(new Rect(x, py, w, 24 + 3 * rowH),
                innate != null ? "【2】あなたから渡る命令（敵の位置から同じ順番・方向・距離）" : "敵の予定（このあと敵が実行する命令）",
                turn.EnemyPreviewCommands, turn.State == BattleState.EnemyExecuting ? turn.ExecutingIndex : -1, EnemyTextColor, rowH, false);
            if (innate == null)
                GUI.Label(new Rect(x, py + 24 + 3 * rowH + 2, w, 22), "※ 敵は自分の位置から、同じ順番・同じ方向・同じ距離で実行します", Small);
            return y + previewHeight + 10;
        }

        /// <summary>
        /// 【仮UI】敵が複数（または兵種がいる）ときの敵の予定：1体2行。
        ///  1行目＝敵の名前（兵種・コピーの種類）と、コピーする命令（短く：斬撃← など）
        ///  2行目＝このまま実行したときの結果の予測（あなたに -4・敵Bに -2・受ける -3 など）。あなたに当たる敵は赤で強調。
        /// 一番上に全体の予測（あなた -X ／ 敵の合計 -Y）。ENEMY TURN 中は行動中の敵と命令を ▶ で示す。
        /// </summary>
        float DrawMultiEnemyPreview(float x, float y, float w)
        {
            var enemies = turn.Enemies;
            const float rowH = 44f;
            float height = 26 + enemies.Count * rowH + 4;
            DrawRect(new Rect(x - 4, y - 4, w + 8, height + 8), new Color(0.6f, 0.1f, 0.1f, 0.22f));
            GUI.Label(new Rect(x, y - 2, w * 0.45f, 26), $"敵の予定（{string.Join("→", enemies.Select(e => e.Model.Label.Length > 0 ? e.Model.Label : "敵"))} の順）", Label);
            if (predictionText != null && turn.State == BattleState.PlayerInput)
            {
                var old0 = GUI.contentColor;
                GUI.contentColor = predictionDanger ? new Color(1f, 0.45f, 0.45f) : new Color(0.6f, 1f, 0.6f);
                GUI.Label(new Rect(x + w * 0.42f, y - 2, w * 0.58f, 26), predictionText, new GUIStyle(Label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold });
                GUI.contentColor = old0;
            }
            else if (turn.State == BattleState.PlayerInput)
                GUI.Label(new Rect(x + w * 0.42f, y, w * 0.58f, 24), Predicting ? "命令を入れると結果を予測します" : "赤＝今の敵の攻撃範囲（命令後は自分で読む）",
                    new GUIStyle(Small) { alignment = TextAnchor.UpperRight });
            float ry = y + 26;
            bool enemyTurn = turn.State == BattleState.EnemyExecuting;
            foreach (var e in enemies)
            {
                var row = new Rect(x, ry, w, rowH - 4);
                bool acting = enemyTurn && turn.ActingEnemy == e;
                bool danger = turn.State == BattleState.PlayerInput && enemyHitsPlayer.ContainsKey(e.Model.Id);
                var tint = e.BaseColor;
                DrawRect(row, acting ? new Color(tint.r, tint.g, tint.b, 0.5f) : new Color(tint.r, tint.g, tint.b, 0.16f));
                if (danger) DrawFrame(row, new Color(1f, 0.25f, 0.25f, 0.9f), 2f);
                string head = $"{(acting ? "▶" : "")}{e.DisplayName}{UnitTag(e.Model)}";
                string line1;
                if (!e.IsAlive) line1 = $"{head}：倒れた";
                else
                {
                    var cmds = turn.GetPreviewFor(e);
                    var parts = new List<string>();
                    for (int i = 0; i < turn.MaxSlots; i++)
                    {
                        string body = i < cmds.Count ? ShortCommand(cmds[i]) : "—";
                        bool now = acting && turn.ExecutingIndex == i;
                        parts.Add($"{(now ? "▶" : "")}{body}");
                    }
                    line1 = $"{head}：" + string.Join("・", parts);
                    if (traitForecast.TryGetValue(e.Model.Id, out var tr)) line1 += $"　＋{tr}";
                }
                GUI.Label(new Rect(row.x + 6, row.y + 1, row.width - 12, 22), line1, new GUIStyle(Label) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip });
                if (Predicting && e.IsAlive && turn.State == BattleState.PlayerInput && enemyResult.TryGetValue(e.Model.Id, out var res))
                {
                    var old = GUI.contentColor;
                    GUI.contentColor = danger ? new Color(1f, 0.55f, 0.55f) : new Color(0.85f, 0.85f, 0.85f);
                    GUI.Label(new Rect(row.x + 18, row.y + 20, row.width - 24, 20), "→ " + res, new GUIStyle(Small) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip });
                    GUI.contentColor = old;
                }
                ry += rowH;
            }
            return y + height + 8;
        }

        /// <summary>兵種・コピーの種類の短い札（弓兵・盾兵・鏡 など）。通常は空。</summary>
        static string UnitTag(UnitModel m)
        {
            var tags = new List<string>();
            if (m.Kind != EnemyKind.Normal) tags.Add(PhaseRules.KindName(m.Kind));
            if (m.CopyTrait == CopyTrait.Mirror && m.Kind != EnemyKind.Knight && m.Kind != EnemyKind.MirrorSoldier) tags.Add("鏡");
            if (m.CopyTrait == CopyTrait.MirrorAttacks) tags.Add("攻撃反転");
            if (m.CopyTrait == CopyTrait.Reverse) tags.Add("逆順");
            return tags.Count > 0 ? $"［{string.Join("・", tags)}］" : "";
        }

        /// <summary>命令の短い表示（斬撃← ／ 駆け足↑2 ／ 斜め歩↗ ／ 闘気）。</summary>
        static string ShortCommand(CommandRecord r)
        {
            var c = r.Command;
            if (!c.UsesDirection) return c.displayName;
            string arrow = c.type == CommandType.Diagonal ? DiagGlyph(r.Direction) : DirGlyph(r.Direction);
            return c.displayName + arrow + (c.NeedsDistanceChoice ? r.Distance.ToString() : "");
        }

        static string DirGlyph(Direction d) => d == Direction.Up ? "↑" : d == Direction.Down ? "↓" : d == Direction.Left ? "←" : "→";
        static string DiagGlyph(Direction d) => d == Direction.Up ? "↗" : d == Direction.Right ? "↘" : d == Direction.Down ? "↙" : "↖";

        static string TraitName(CopyTrait trait)
        {
            switch (trait)
            {
                case CopyTrait.Reverse: return "逆順まね";
                case CopyTrait.Mirror: return "方向反転まね";
                default: return "通常まね";
            }
        }

        /// <summary>
        /// 画面右下の切替（2段）。上の段：【検証用】手札方式・鏡蝕・長期戦。下の段：ステージ切替と RUN。
        /// どれも選ぶと今のステージを読み込み直す。r の高さは 64。
        /// </summary>
        void DrawModeButtons(Rect r)
        {
            var small = new GUIStyle(Button) { fontSize = 12 };
            var oldBg = GUI.backgroundColor;

            // ── 上の段：検証用の切替 ──
            float y = r.y;
            float bx = r.x;
            GUI.Label(new Rect(bx, y + 6, 40, 24), "手札", Small);
            bx += 40;
            // 固定／A／B ＋【検証中】大きい手札（初手8・上限10）2方式 ＋ 中くらいの手札（初手7・上限9）
            string[] names = { "固定", "A:2枚", "B:最低3", "大:3枚", "大:+1", "中:7/9", "大:8/12", "7枚" };
            float hw = Mathf.Min(60f, (r.width - 40f - 260f) / names.Length - 2f);
            for (int i = 0; i < names.Length; i++, bx += hw + 2f)
            {
                var mode = (HandMode)i;
                if (turn.ActiveHandMode == mode) GUI.backgroundColor = Color.cyan;
                if (GUI.Button(new Rect(bx, y, hw, 30), names[i], new GUIStyle(small) { fontSize = hw < 58f ? 11 : 12, padding = new RectOffset(1, 1, 2, 2) }))
                {
                    ResetPending();
                    turn.CurrentHandMode = mode;
                    turn.LoadStage(turn.CurrentStageIndex);
                }
                GUI.backgroundColor = oldBg;
            }

            // 【検証用】鏡蝕ゲージの ON/OFF。対象外のステージ（STAGE 1〜3 など）では押せない
            bx += 8;
            bool allowed = turn.CurrentStage != null && turn.CurrentStage.stagnationAllowed;
            if (turn.StagnationDebugOn) GUI.backgroundColor = new Color(0.8f, 0.6f, 1f);
            GUI.enabled = allowed;
            string label = !allowed ? "鏡蝕 対象外" : turn.StagnationDebugOn ? "鏡蝕 ON" : "鏡蝕 OFF";
            if (GUI.Button(new Rect(bx, y, 76, 30), label, small))
            {
                ResetPending();
                turn.StagnationDebugOn = !turn.StagnationDebugOn;
                turn.LoadStage(turn.CurrentStageIndex);
            }
            GUI.backgroundColor = oldBg;
            bx += 80;
            // 【検証用】完全予測の ON/OFF（既定 OFF＝ターン開始時点の危険範囲だけ）
            GUI.enabled = true;
            if (Predicting) GUI.backgroundColor = new Color(0.5f, 1f, 0.6f);
            if (GUI.Button(new Rect(bx, y, 76, 30), Predicting ? "予測 ON" : "予測 OFF", small)) { FullPrediction = !FullPrediction; forecastKey = null; }
            GUI.backgroundColor = oldBg;
            bx += 80;

            // 【検証用】長期戦（テスト用ステージだけ敵 HP ×2）：手札方式 A/B の差を見るため
            GUI.enabled = turn.CurrentStage != null && turn.CurrentStage.isTestStage;
            if (turn.LongBattleDebugOn) GUI.backgroundColor = new Color(1f, 0.8f, 0.4f);
            if (GUI.Button(new Rect(bx, y, 76, 30), turn.LongBattleDebugOn ? "敵HP ×2" : "敵HP ×1", small))
            {
                ResetPending();
                turn.LongBattleDebugOn = !turn.LongBattleDebugOn;
                turn.LoadStage(turn.CurrentStageIndex);
            }
            GUI.enabled = true;
            GUI.backgroundColor = oldBg;

            // ── 下の段：ステージ切替と RUN ──
            y = r.y + 34;
            bx = r.x;
            // ステージが増えても1行に収まるように、ボタンの幅を自動で決める
            float bw = Mathf.Min(32f, (r.width - 44f) / Mathf.Max(1, turn.Stages.Count) - 2f);
            var stageStyle = new GUIStyle(small) { fontSize = bw < 30f ? 10 : 12, padding = new RectOffset(1, 1, 2, 2) };
            for (int i = 0; i < turn.Stages.Count; i++, bx += bw + 2f)
            {
                if (i == turn.CurrentStageIndex) GUI.backgroundColor = Color.yellow;
                if (GUI.Button(new Rect(bx, y, bw, 30), turn.Stages[i].buttonLabel, stageStyle)) { ResetPending(); turn.LoadStage(i); }
                GUI.backgroundColor = oldBg;
            }
            if (run != null && GUI.Button(new Rect(bx, y, 40, 30), "RUN", small)) { ResetPending(); run.OpenRunStart(); }
        }

        // ───────── 方向・距離の選択 ─────────

        void Select(CommandData command, CardInstance card)
        {
            if (act != null && act.PendingItem == PendingItemUse.WarpStone) act.CancelItem(); // 【RC4】札を選んだら、転移石の行き先選びはやめる（命令を組む前に使う物）
            pendingCommand = command;
            pendingCard = card;
            hasPendingDirection = false;
            Sfx.Play(Sfx.Kind.Button);
            if (!command.UsesDirection) Register(Direction.Up, 0); // 方向の無い技（回転斬り）はすぐ登録
        }

        void DrawDirectionPicker(Rect r, bool compact)
        {
            if (pendingCommand == null)
            {
                string hint;
                switch (turn.State)
                {
                    case BattleState.PlayerInput:
                        hint = turn.PlannedCommands.Count >= turn.MaxSlots
                            ? "スロットがいっぱいです。「実行」を押してください。"
                            : turn.IsDeckMode && turn.Deck.UsesRetainChoice
                                ? $"カードか「歩く」を選ぶと、ここで方向を選べます（最大 {turn.MaxSlots} 命令）。\n使わないカードは「保留する」で次のターンに2枚まで残せます（残りは捨て札）。"
                            : turn.IsDeckMode
                                ? $"カードを選ぶと、ここで方向を選べます（最大 {turn.MaxSlots} 命令）。\n使わなかったカードは手札に残ります（手札の上限 {turn.Deck.HandLimit} 枚）。"
                                : $"手札から技を選ぶと、ここで方向を選べます。（このステージは最大 {turn.MaxSlots} 命令）";
                        break;
                    case BattleState.Cleared:
                    case BattleState.GameOver:
                        hint = "戦闘終了。";
                        break;
                    default:
                        hint = "実行中…";
                        break;
                }
                // 【検証用】鏡蝕が満ちていたら、命令を組む前に必ず目に入るように警告を先頭へ
                var st = turn.Stagnation;
                if (st != null && st.AtMax && turn.State == BattleState.PlayerInput)
                {
                    var oldColor = GUI.contentColor;
                    GUI.contentColor = new Color(1f, 0.5f, 0.5f);
                    GUI.Label(new Rect(r.x, r.y, r.width, 26), $"鏡蝕が満ちている：このラウンドで敵の HP を減らせないと、あなたに {st.NextPenalty} ダメージ", Small);
                    GUI.contentColor = oldColor;
                    GUI.Label(new Rect(r.x, r.y + 26, r.width, r.height - 26), hint, Small);
                    return;
                }
                GUI.Label(new Rect(r.x, r.y, r.width, r.height), hint, Small);
                return;
            }

            if (compact)
            {
                // 【仮UI】選んだカード：系統の色で名前と短い効果を大きく、説明と「敵がコピーすると」を下に短く
                float tw = r.width - 200;
                var nameStyle = new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                nameStyle.normal.textColor = Color.Lerp(CardColor(pendingCommand), Color.white, 0.55f);
                GUI.Label(new Rect(r.x, r.y - 2, tw, 24), $"［{CategoryTag(pendingCommand)}］{pendingCommand.displayName}　{ShortText(pendingCommand)}", nameStyle);
                // 説明の全文は短い効果と重なるので出さず、「敵がコピーすると」を2行で（キャンセルボタンと重ならない高さ）
                string note = string.IsNullOrEmpty(pendingCommand.copyNote) ? pendingCommand.description.Replace("\n", "／") : "敵がまねすると：" + pendingCommand.copyNote;
                var noteStyle = new GUIStyle(Tiny) { wordWrap = true, clipping = TextClipping.Clip };
                noteStyle.normal.textColor = string.IsNullOrEmpty(pendingCommand.copyNote) ? Color.white : new Color(1f, 0.65f, 0.65f);
                GUI.Label(new Rect(r.x, r.y + 22, tw, 34), note, noteStyle);
            }
            else
                GUI.Label(new Rect(r.x, r.y, r.width - 110, 22), $"『{pendingCommand.displayName}』{pendingCommand.description}", Small);
            float cancelY = compact ? r.yMax - 26 : r.y;
            float cancelX = compact ? r.x : r.xMax - 100;
            if (GUI.Button(new Rect(cancelX, cancelY, 100, 26), "キャンセル", Button)) { ResetPending(); return; }

            if (!hasPendingDirection)
            {
                // 十字に並べる
                float bw = compact ? 56 : 40, bh = compact ? 27 : 28;
                float cx = compact ? r.xMax - 128 : r.x + 150, cy = compact ? r.y : r.y + 30;
                if (!compact) GUI.Label(new Rect(r.x, r.y + 26, 200, 22), "方向を選ぶ：", Small);
                // 【検証中】斜め歩は、斜めの矢印で表示（↑の位置＝右上、→＝右下、↓＝左下、←＝左上）
                bool diag = pendingCommand.type == CommandType.Diagonal;
                var arrowStyle = new GUIStyle(Button) { fontSize = compact ? 20 : 16, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 0, 0) };
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.95f, 0.5f);
                if (GUI.Button(new Rect(cx, cy, bw, bh), diag ? "↗" : "↑", arrowStyle)) ChooseDirection(Direction.Up);
                if (GUI.Button(new Rect(cx - bw - 4, cy + bh + 2, bw, bh), diag ? "↖" : "←", arrowStyle)) ChooseDirection(Direction.Left);
                if (GUI.Button(new Rect(cx + bw + 4, cy + bh + 2, bw, bh), diag ? "↘" : "→", arrowStyle)) ChooseDirection(Direction.Right);
                if (GUI.Button(new Rect(cx, cy + 2 * (bh + 2), bw, bh), diag ? "↙" : "↓", arrowStyle)) ChooseDirection(Direction.Down);
                GUI.backgroundColor = oldBg;
            }
            else
            {
                float dx = compact ? r.xMax - 180 : r.x, dy = compact ? r.y + 24 : r.y + 54;
                GUI.Label(new Rect(dx, compact ? r.y : r.y + 26, 180, 22), $"方向：{pendingDirection.ToLabel()}　距離：", Small);
                for (int d = pendingCommand.minDistance; d <= pendingCommand.maxDistance; d++)
                {
                    int index = d - pendingCommand.minDistance;
                    if (GUI.Button(new Rect(dx + index * 88, dy, 80, 36), $"{d}マス", Button)) Register(pendingDirection, d);
                }
            }
        }

        void ChooseDirection(Direction dir)
        {
            if (pendingCommand.NeedsDistanceChoice)
            {
                pendingDirection = dir;
                hasPendingDirection = true;
            }
            else
            {
                Register(dir, pendingCommand.minDistance);
            }
        }

        void Register(Direction dir, int distance)
        {
            turn.AddCommand(new CommandRecord(pendingCommand, dir, distance, pendingCard));
            ResetPending();
        }

        void ResetPending()
        {
            pendingCommand = null;
            pendingCard = null;
            hasPendingDirection = false;
        }

        /// <summary>①②③ の枠を描く。highlightIndex の枠は「実行中」として強調する。</summary>
        void DrawSlots(Rect r, string title, IReadOnlyList<CommandRecord> commands, int highlightIndex, Color accent, float rowH, bool flashOnAdd)
        {
            GUI.Label(new Rect(r.x, r.y - 2, r.width, 26), title, Label);
            string[] marks = { "①", "②", "③" };
            for (int i = 0; i < 3; i++)
            {
                var slot = new Rect(r.x, r.y + 24 + i * rowH, r.width, rowH - 4);
                bool locked = i >= turn.MaxSlots;
                bool active = i == highlightIndex;
                DrawRect(slot, active ? new Color(accent.r, accent.g, accent.b, 0.45f) : new Color(1f, 1f, 1f, locked ? 0.03f : 0.08f));
                // カードを入れた瞬間に枠が光る
                float since = Time.time - slotAddedAt[i];
                if (flashOnAdd && since < 0.4f && i < commands.Count)
                    DrawRect(slot, new Color(1f, 1f, 0.5f, 0.5f * (1f - since / 0.4f)));
                string text = locked ? "（このステージでは使えない）" : i < commands.Count ? commands[i].ToDisplayText() : "";
                if (i < commands.Count && turn.IsDeckMode && flashOnAdd) text += commands[i].SourceCard == null ? "（基本）" : "（カード）";
                var rowStyle = rowH < 30 ? new GUIStyle(Label) { fontSize = 14 } : Label;
                GUI.Label(new Rect(slot.x + 8, slot.y - 1, slot.width - 16, slot.height + 4), $"{(active ? "▶" : "")}{marks[i]}  {text}", rowStyle);
            }
        }

        void DrawLog(Rect r)
        {
            if (r.height < 24) return;
            DrawRect(r, PanelColor);
            int lineHeight = 20;
            int maxLines = Mathf.Max(1, (int)((r.height - 8) / lineHeight));
            var lines = turn.Log;
            int start = Mathf.Max(0, lines.Count - maxLines);
            var logStyle = new GUIStyle(Small) { wordWrap = false, clipping = TextClipping.Overflow, fontSize = 13 };
            for (int i = start; i < lines.Count; i++)
                GUI.Label(new Rect(r.x + 6, r.y + 4 + (i - start) * lineHeight, r.width - 12, lineHeight + 4), lines[i], logStyle);
        }
    }
}
