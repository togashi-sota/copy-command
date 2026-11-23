using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】戦闘画面の新しいレイアウト（2026-10-04 本人の参考画像 docs/art_refs/ui_target.webp・ui_spec.webp に寄せた形）。
    /// 手札（デッキ）方式の戦闘で使う（STAGE 1〜3 の固定の手札と RUN は今までの画面）。
    ///   左：上にステージ名・TURN・HP の並び、真ん中に盤面（列・行の番号つき）、下にステージの説明と凡例
    ///   右：手札（4枚＋3枚の2段。カードに「自分」「敵コピー」）→ 実行予定（横に3つ）＋実行 → 敵の予定 → 最新のログ
    /// 方向は盤面のプレイヤーの周りの矢印（またはキーボード）で選ぶ。カードにマウスを乗せると詳しい説明。
    /// キー：1〜9＝カード、矢印・WASD＝方向、Enter／Space＝実行、Backspace＝1つ戻す、Esc＝取り消し、Tab＝詳細、F2＝検証メニュー。
    /// </summary>
    public partial class BattleUI
    {
        [Tooltip("【検証中】UI の絵（カード枠・パネルなど）。盤面にテーマが無いステージでも、UI はこの絵を使う")]
        [SerializeField] VisualTheme uiTheme;
        [Tooltip("【検証中】ACT1 の進行役（アイテム・刻印・結果の「次へ」）")]
        [SerializeField] ActRunManager act;

        const float ShowTopH = 104f;
        internal const float ShowInfoH = 108f;
        /// <summary>【Puzzle v2】左下の欄の高さ（攻略条件を出す部屋は大きめ）。</summary>
        internal float InfoH => V2Layout ? V2BottomH : IsPuzzleUi ? PuzzleInfoH : ShowInfoH; // 【RC8】新しい配置は目的の1行だけ
        internal const float PuzzleInfoH = 150f;
        internal const float ShowBoardFrac = 0.47f;
        /// <summary>方向の矢印の後に描く文字（反射 -N）。矢印のボタンの下に隠れないように（レビュー2）。</summary>
        readonly List<(Rect r, string text, Color color)> lateLabels = new List<(Rect, string, Color)>();
        /// <summary>【RC5・レビュー】名札は盤面の印（予告の点線・転移石の行き先・赤の数字）の上にもう一度描く（名札が読めなくなっていた）。</summary>
        readonly List<(Rect text, string label, Color col, float hp)> plateLate = new List<(Rect, string, Color, float)>();
        /// <summary>【企画書 v5・UI】敵の名札の位置（重ならないように並べた結果）。大技のカウントは名札の右端に描く（BattleUIV4）。</summary>
        internal readonly Dictionary<UnitModel, Rect> plateRects = new Dictionary<UnitModel, Rect>();
        internal const float PlateChipW = 30f; // 【RC6】砂時計の絵＋数字（大技まであと何ターンか）

        bool showDebugMenu, showDetail;
        string hoverTitle, hoverBody;
        /// <summary>【RC7】手札の札にマウスを乗せたときの説明（V4 は右下の説明欄に出す：浮かぶ窓が下の段の札を隠していた）。</summary>
        string cardTipTitle, cardTipBody;
        Vector2 hoverPos;

        static readonly Color PanelBlue = new Color(0.08f, 0.1f, 0.18f, 0.95f);
        static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);

        /// <summary>新しいレイアウトを使うか（手札方式の戦闘・RUN 以外）。</summary>
        bool UseShowcaseLayout => turn != null && turn.IsDeckMode && (run == null || !run.InRun);

        void UpdateCameraRect()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var rect = UiStyles.CamRect(UseShowcaseLayout && ShowingBattle
                ? new Rect(0f, InfoH / RefHeight, BoardFracNow, 1f - (TopH + InfoH) / RefHeight) // 【RC8】盤面を大きく
                : new Rect(0f, TutorialHeight / RefHeight, BoardAreaRatio, 1f - (HeaderHeight + TutorialHeight) / RefHeight)); // 【RC5】縦長の窓の黒い帯の内側
            if (cam.rect != rect) cam.rect = rect;
        }

        void DrawShowcase(float scale)
        {
            if (turn.CurrentStage != null) UiStyles.Theme = turn.CurrentStage.visualTheme != null ? turn.CurrentStage.visualTheme : uiTheme;
            // 【Copy Core V2】カードの「敵コピー」の文は、サンドボックスの戦闘だけ今の方式で書く
            UiStyles.CopyTextMode = IsCopySandbox ? turn.Grid.Model.CopyMode : CopyMode.Current;
            UiStyles.CopyTextOff = IsCopySandbox && !turn.Grid.Model.CopyEnabled;
            float W = Screen.width / scale;
            float bw = W * BoardFracNow; // 【RC8】
            UiStyles.HideSelfHitBadge = false; // 【文化祭クエスト v2】文化祭も本編と同じ表示（旧版は はじめてだけ「まね→あなた」を出さなかった）
            UiStyles.KidNames = null; // 【文化祭クエスト v2】札の名前は本編と同じ（旧版の はじめては ひらがな）
            hoverTitle = null;
            hoverEnemyId = DebugHoverEnemyId; hoverStep = DebugHoverStep; // 【RC8】敵 ↔ 攻撃のマス・順番の帯（撮影ではマウスの代わり）
            cardTipTitle = null; cardTipBody = null; // 【RC7】
            // 【検証中】モーダル（先読み・回収の鉤の選択、詳細）が開いている間は、背面のカード・ボタン・キー操作を止める
            bool picker = PickerOpen || TacticIntroOpen || (InTutorialUi && act.TutorialStepDone) || showLegend || (act != null && act.BattleMenuOpen) || MissionCardOpen || (act != null && (act.FestivalIdlePrompt || act.FestivalGameOverOpen)); // 【文化祭クエスト v2】倒れた窓 // 【文化祭】展示の PC の確認 // 【RC11】5分体験の MISSION の窓 // 【RC5・レビュー3】戦闘中のメニューの後ろ // 【RC4・レビュー】凡例を閉じるクリックが方向の入力になっていた // 【RC3】はじめての戦術札の説明も、後ろの操作を止める【RC4】チュートリアルの「できた！」も（窓の下の手札がクリックを取らない）
            // 【RC4・本人「この方向を押したいのに押せない」】方向を選んでいる間は、方向の入力を画面の中で一番先に受ける
            //   （前は敵のマスのクリック＝敵を選ぶ が先にクリックを取り、隣に敵がいる向きの矢印が押せなかった）
            dirAreas.Clear();
            dirExtraAreas.Clear();
            forecastSkip = turn.UncopiedSlots;
            ForecastSerial = turn.TacticsUsedThisBattle * 100 + (act != null && act.Run != null ? act.Run.Items.Count : 0) + turn.TurnNumber * 10000;
            if (InputPriority.DirectionFirst(DirectionMode, picker, showDetail)) { ComputeDirectionAreas(scale); HandleDirectionPointer(); }
            var input = ModalInput.Begin(picker || showDetail);
            if (showLegend && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.L || Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.Return)) { showLegend = false; Event.current.Use(); } // 【RC5】凡例もキーで閉じる
            if (!picker && !showDetail) HandleShowcaseKeys();

            // 右側の下地（盤面の背景の上に、少し暗いパネル）
            DrawRect(new Rect(bw, 0, W - bw, RefHeight), new Color(0.03f, 0.04f, 0.08f, 0.82f));
            DrawRect(new Rect(0, 0, bw, TopH), new Color(0.03f, 0.04f, 0.08f, 0.55f));
            DrawRect(new Rect(bw, 0, 2, RefHeight), new Color(Gold.r, Gold.g, Gold.b, 0.35f));

            if (V2Layout) DismissShownHintOnExecute(); // 【第2巡 1】
            DrawShowTopBar(W, bw);
            if (V2Layout) DrawV2Top(bw); // 【RC8】あなたと敵（動く順番）・順番の帯
            else DrawShowHpRow(new Rect(10, 52, bw - 20, 48));
            DrawShowBoardOverlay(scale, bw);
            DrawFestivalHud(bw, W); // 【文化祭】時計・敵にやらせた攻撃
            if (InTutorialUi) DrawActTutorialPanel(new Rect(10, RefHeight - InfoH + 6, bw - 20, InfoH - 12)); // 【RC3】チュートリアル
            else if (V2Layout) DrawObjectiveV2(new Rect(6, RefHeight - InfoH + 2, bw - 12, InfoH - 4)); // 【RC8】目的の1行
            else DrawShowInfo(new Rect(10, RefHeight - InfoH + 6, bw - 20, InfoH - 12));
            if (IsPuzzleUi && !InTutorialUi) DrawHintBanner(bw);

            float x = bw + 12, w = W - bw - 24;
            float y = DrawShowHand(x, 52, w);
            if (V2Layout && !FestivalUi && HandShouldDim) DrawRect(new Rect(x - 6, 46, w + 12, y - 40), new Color(0.01f, 0.015f, 0.03f, 0.6f)); // 【通常リリース版・画面の直し 1】実行中・勝ち負けの窓・段階の帯の間は、手札を 40% の明るさに（盤面を見る時間）
            y = DrawShowPlan(x, y + 4, w);
            if (V2Layout)
            {
                // 【RC8】右下は「今指している物」の説明だけ（札・敵・攻撃のマス・順番の段）。ログは詳細（Tab）へ・キーは1行
                y = DrawTutorialFeedbackBox(x, y + 6, w); // 【RC11】チュートリアル・5分体験のヒント／やり直しの一言（左下のルールの行を隠さない）
                y = DrawV4EnemyDetail(x, y + 6, w, RefHeight - y - 6 - 24);
                DrawKeyLineV2(new Rect(x, RefHeight - 21, w, 18));
            }
            else
            {
            y = IsV4Ui ? DrawV4EnemyDetail(x, y + 6, w) : DrawShowEnemyPlan(x, y + 6, w); // 【RC2】V4：敵の一覧は出さない（盤面の敵の頭上＋選んだ敵だけ詳しく）
            DrawShowLog(new Rect(x, y + 4, w, RefHeight - y - 10));
            }

            if (showDebugMenu) DrawShowDebugMenu(new Rect(bw + 12, RefHeight - 150, W - bw - 24, 112));
            ModalInput.End(input);
            if (showDetail && !picker)
            {
                // 詳細：Tab / Esc か、クリックで閉じる（ここだけ入力を受ける）
                var ev = Event.current;
                if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Tab || ev.keyCode == KeyCode.Escape)) { showDetail = false; ev.Use(); }
                DrawShowDetail(new Rect(24, TopH + 10, bw - 48, RefHeight - TopH - InfoH - 20));
            }
            if (showLegend && !(PickerOpen || TacticIntroOpen)) DrawLegendOverlay(new Rect(24, TopH + 10, bw - 48, RefHeight - TopH - 20)); // 【RC5】下の説明欄まで使う（凡例がはみ出していた）
            if (!picker && !showDetail && !showLegend) DrawHoverTip(W);
            DrawFestivalExecDim(bw, W); // 【文化祭・横から見る人のレビュー「右半分の文字の壁」】敵が動いている間は右を暗く
            DrawPickers(W);
            if (TacticIntroOpen) DrawTacticIntro(bw); // 【RC3】はじめて緑の札を持ったとき
            if (FestivalUi) { DrawFestivalClear(bw, W); DrawFestivalGameOver(W); } // 【文化祭クエスト v2】CLEAR!・倒れた窓
            else if (InTutorialUi) DrawTutorialDoneModal(bw, W); // 【RC3】「できた！」（勝ちの画面の代わり）
            else DrawResult(new Rect(bw * 0.5f - 170, RefHeight * 0.5f - 90, 340, 180)); // 【RC11】チュートリアル・5分体験では勝ちの窓を出さない（MISSION の行を挟んで else がずれ、「勝利！ 報酬へ」が「できた！」と並んでいた）
            if (MissionCardOpen) { if (act.QuickIntroOpen) DrawQuickIntro(W); else DrawMissionCard(bw, W); } // 【RC12】入口の窓 → MISSION // 【RC11】5分体験：問題の前に画面の真ん中へ MISSION
            DrawBossIntro(bw);                      // 【Combat V4】中ボス・ボスの登場
            DrawPhaseBanner(bw);                    // 【RC8】ボスの段階が変わった
            DrawFestivalBanner(bw);                 // 【文化祭】同士討ち ×2！ など
            ScreenFx.Update(Camera.main);          // 【Combat V4】画面揺れ・フラッシュ
            ScreenFx.DrawFlash(W, RefHeight);
            DrawFestivalIdlePrompt(W);              // 【文化祭】展示の PC：まだ遊んでいますか？
            if (Event.current.type == EventType.Repaint) { hoverEnemyIdLast = hoverEnemyId; hoverStepLast = hoverStep; } // 【RC8】盤面の塗り（Update）は前のフレームの分
        }

        // ───────── 上：ステージ名・TURN・状態 ─────────

        void DrawShowTopBar(float W, float bw)
        {
            if (!V2Layout) DrawShowTopBarLeft(bw);
            DrawShowTopBarRight(W, bw);
        }

        /// <summary>左上：ステージ名・TURN（RC7 までの配置。【RC8】新しい配置では順番の帯と目的の1行へ）。</summary>
        void DrawShowTopBarLeft(float bw)
        {
            var title = new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            ShadowLabel(new Rect(14, 4, bw - 230, 28), turn.CurrentStage.stageTitle, title, Gold);
            var sub = new GUIStyle(Small) { fontSize = 12, wordWrap = false };
            if (IsCopySandbox)
            {
                var m = turn.Grid.Model;
                ShadowLabel(new Rect(16, 29, bw - 230, 20), m.CopyEnabled ? ShortRule(m.CopyMode) : "COPY OFF：敵はまねしない（比較用）", new GUIStyle(sub) { fontStyle = FontStyle.Bold }, new Color(1f, 0.85f, 0.55f));
            }
            else ShadowLabel(new Rect(16, 29, bw - 230, 20), ThemeSubtitle(), sub, new Color(0.8f, 0.82f, 0.9f));

            var banner = new Rect(bw - 206, 4, 196, 42);
            if (!DrawUiImage(banner, "turn", 1f, ScaleMode.StretchToFill)) DrawRect(banner, PanelBlue);
            int limit = turn.CurrentStage != null ? turn.CurrentStage.turnLimit : 0; // 【戦闘中心 v3】試練は TURN 2/3
            int shownTurn = turn.TurnNumber + (act != null && act.InBattle ? act.TrialTurnOffset : 0); // 【レビュー】続きから戻った試練は使ったターンを足す
            ShadowLabel(banner, limit > 0 ? $"TURN {shownTurn}/{limit}" : $"TURN {turn.TurnNumber}", new GUIStyle(Big) { fontSize = 22, alignment = TextAnchor.MiddleCenter }, limit > 0 && shownTurn >= limit ? new Color(1f, 0.55f, 0.45f) : Gold);
        }

        /// <summary>右上：今の番・アイテム・山札・捨て札・メニュー・凡例・詳細。</summary>
        void DrawShowTopBarRight(float W, float bw)
        {
            // 右上：今の番・山札・捨て札・詳細・検証メニュー
            float x = bw + 12;
            string state; Color sc;
            switch (turn.State)
            {
                case BattleState.EnemyExecuting: state = "敵の番"; sc = new Color(1f, 0.45f, 0.45f); break;
                case BattleState.PlayerExecuting: state = "あなたの命令を実行中"; sc = PlayerTextColor; break;
                case BattleState.Cleared: state = InTutorialUi ? "できた！" : "勝利"; sc = Gold; break;
                case BattleState.GameOver: state = "敗北"; sc = Color.gray; break;
                default: state = $"あなたの番（命令{turn.MaxSlots}つまで）"; sc = PlayerTextColor; break;
            }
            bool inAct = act != null && act.InBattle;

            float rx = W - 12;
            if (!inAct && !BuildInfo.ExternalPlaytest && !CleanCapture) rx = ShowButton(ref rx, "検証", 54, () => showDebugMenu = !showDebugMenu, showDebugMenu); // ACT1 では F2 だけ（本人の検証用）。外部テスト版は出さない
            rx = ShowButton(ref rx, "詳細", 54, () => showDetail = !showDetail, showDetail);
            rx = ShowButton(ref rx, "凡例", 54, () => showLegend = !showLegend, showLegend); // 【Puzzle v2】いつでも確認できる
            // 【RC3・レビュー】戦闘中にも音を消せる【RC5・レビュー3】戦闘中のメニュー（設定・遊び方・中断してタイトルへ）。音の ON/OFF は M キーとメニューの設定
            if (inAct || CleanCapture) rx = ShowButton(ref rx, "メニュー", 70, () => { if (act != null && act.InBattle) act.BattleMenuOpen = true; }, act != null && act.BattleMenuOpen); // 【通常リリース版・ポートフォリオのレビュー「研究室の帯（まね ON・音 ON）が写り、まねを切れると読まれる」】きれいな撮影は本編と同じ形
            else rx = ShowButton(ref rx, Sfx.Muted ? "音 OFF" : "音 ON", 52, () =>
            {
                Sfx.Muted = !Sfx.Muted; Bgm.SetMuted(Sfx.Muted);
                if (act != null && act.Profile != null) { act.Profile.soundOff = Sfx.Muted; act.SaveProfile(); }
            }, Sfx.Muted);
            var deck = turn.Deck;
            var chip = new GUIStyle(Small) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            if (inAct && act.CanRestart)
            {
                // 【Puzzle v2】部屋をやり直す（2回押す：1回目で確認）
                bool armed = Time.unscaledTime - restartArmedAt < 3f;
                rx = ShowButton(ref rx, armed ? "本当に？" : "やり直す", 76, () =>
                {
                    if (Time.unscaledTime - restartArmedAt < 3f) { restartArmedAt = -10f; act.RestartBattle(); ResetPending(); }
                    else restartArmedAt = Time.unscaledTime;
                }, armed);
            }
            if (inAct && act.CanGiveUpTrial)
            {
                // 【戦闘中心 v3】試練をあきらめる（2回押す：1回目で確認）。負けではない
                bool armed = Time.unscaledTime - giveUpArmedAt < 3f;
                rx = ShowButton(ref rx, armed ? "本当に？" : "あきらめる", 86, () =>
                {
                    if (Time.unscaledTime - giveUpArmedAt < 3f) { giveUpArmedAt = -10f; act.GiveUpTrial(); ResetPending(); }
                    else giveUpArmedAt = Time.unscaledTime;
                }, armed);
            }
            // 【RC5・最終ゲート】16:10・4:3（横幅 1152）では、アイテムの欄が約9px しか残らず、転移石が山札の欄の下に隠れていた → 狭いときは今の番の欄と山札・捨て札を短く
            bool narrow = inAct && rx - 196 - (x + 196) - 8 < 70; // 1280×720 は約76（そのまま）・1152 は約9
            float discW = narrow ? 66 : 92, deckW = narrow ? 60 : 86;
            var pill = V2Layout ? new Rect(x - 6, 10, 0, 28) : new Rect(x, 10, inAct ? (narrow ? 132 : 196) : 270, 28); // 【RC8】新しい配置では「あなたの番」は順番の帯（①が光る）。4:3 でアイテムの欄が無くなっていた
            if (!V2Layout)
            {
            DrawUi(pill, "panel", PanelBlue);
            string stateShown = narrow && turn.State == BattleState.PlayerInput ? "あなたの番" : state;
            ShadowLabel(new Rect(pill.x + 8, pill.y, pill.width - 12, pill.height), stateShown, Fit(new GUIStyle(Label) { fontSize = inAct ? 13 : 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false }, stateShown, pill.width - 12, 10), sc); // 【RC2】閉じかっこが切れていた
            }
            if (inAct) DrawActItems(new Rect(pill.xMax + 6, 10, rx - (discW + deckW + 18) - pill.xMax - 8, 28));
            if (QuickUi) return; // 【RC11・5分体験の採用担当「山札・捨て札・操作説明は5分では要らない」】
            rx -= discW + 6;
            DrawUi(new Rect(rx, 10, discW, 28), "panel", PanelBlue);
            string discLabel = $"捨て札 {deck.DiscardPile.Count}";
            GUI.Label(new Rect(rx, 10, discW, 28), discLabel, narrow ? Fit(chip, discLabel, discW - 6, 10) : chip); // 【RC5・最終検証】狭いときも同じ呼び名
            rx -= deckW + 6;
            DrawUi(new Rect(rx, 10, deckW, 28), "panel", PanelBlue);
            GUI.Label(new Rect(rx, 10, deckW, 28), $"山札 {deck.DrawPile.Count}", chip);
        }

        string ThemeSubtitle()
        {
            var th = turn.CurrentStage.visualTheme;
            string hand = HandModeShort(turn.ActiveHandMode);
            if (act != null && act.InBattle && act.Run != null)
            {
                var r = act.Run;
                string imps = r.Imprints.Count > 0 ? string.Join("・", r.Imprints.Select(i => ActContent.Imprint(i).Name)) : "なし";
                return $"ACT1 {r.Floor + 1}/{r.FloorCount}階　ルーン片 {r.Shards}" + (r.V4Run && r.Imprints.Count == 0 ? "" : $"　刻印：{imps}"); // 【RC7】完成版の刻印も（持っていれば）
            }
            if (th == null) return $"検証ステージ（グレーBOX）　手札：{hand}";
            return (th.name.StartsWith("Crystal") ? "紫晶の祭壇" : "忘れられた石の間") + $"　手札：{hand}";
        }

        static string HandModeShort(HandMode m)
        {
            switch (m)
            {
                case HandMode.DeckSeven: return "7枚補充";
                case HandMode.DeckMid3: return "中:7/9";
                case HandMode.DeckLarge12: return "大:8/12";
                case HandMode.DeckLarge3: return "大:3枚";
                case HandMode.DeckLargeUsed: return "大:+1";
                case HandMode.DeckA: return "A:2枚";
                case HandMode.DeckB: return "B:最低3";
                default: return "固定";
            }
        }

        float ShowButton(ref float right, string label, float width, System.Action onClick, bool on = false)
        {
            right -= width;
            var r = new Rect(right, 10, width - 6, 28);
            if (ThemedButton(r, label, on)) onClick();
            return right;
        }

        /// <summary>テーマのボタン（無ければ普通のボタン）。押されたら true。</summary>
        bool ThemedButton(Rect r, string label, bool on = false, bool gold = false, int fontSize = 13, bool enabled = true)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            string tex = gold ? "button_gold" : hover || on ? "button_hover" : "button_normal";
            DrawUi(r, tex, gold ? new Color(0.6f, 0.45f, 0.15f) : new Color(0.15f, 0.18f, 0.28f), enabled ? 1f : 0.4f);
            var st = new GUIStyle(Label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(r, label, st, enabled ? (gold ? new Color(0.15f, 0.08f, 0.02f) : on ? Gold : Color.white) : new Color(1f, 1f, 1f, 0.4f));
            if (!enabled) return false;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Play(Sfx.Kind.Button); return true; }
            return false;
        }

        /// <summary>【RC2】同じ兵種が2体以上いる部屋では、名前に記号（A・B…）を付ける（命令の欄の「まねる：A・B」と結びつくように）。</summary>
        string DupLabel(UnitModel m)
        {
            if (m == null || m.IsPlayer) return "";
            return m.KindLetter ?? ""; // 【RC5】兵種ごとの A・B（原因の一言と同じ。BattleModel.ApplyStage）
        }

        /// <summary>【RC2】V4 の画面の呼び名（癒しの写し身は「写し身」：ボスの盤面で名札が核や隣の名札と重なった）。</summary>
        static string V4Name(UnitModel m) => m.Kind == EnemyKind.HealClone ? "写し身" : m.Kind == EnemyKind.Normal ? "兵" : PhaseRules.KindName(m.Kind); // 【RC3】チュートリアルの兵（前は「通常」）

        // ───────── 上：HP の並び ─────────

        void DrawShowHpRow(Rect area)
        {
            var units = new List<UnitController> { turn.Player };
            units.AddRange(turn.Enemies);
            float gap = 6;
            float cw = Mathf.Min(230f, (area.width - gap * (units.Count - 1)) / units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var r = new Rect(area.x + i * (cw + gap), area.y, cw, area.height);
                bool dead = !u.IsAlive;
                DrawUi(r, u.IsPlayer ? "panel" : "panel", PanelBlue, dead ? 0.5f : 1f);
                var pr = new Rect(r.x + 5, r.y + 5, r.height - 10, r.height - 10);
                if (!DrawUiImage(pr, "portrait_" + KindKey(u.Model), dead ? 0.35f : 1f)) DrawRect(pr, u.BaseColor);
                float tx = pr.xMax + 6, tw = r.xMax - tx - 6;
                string name = u.IsPlayer ? "あなた" : IsV4Ui ? V4Name(u.Model) + DupLabel(u.Model) : $"{u.DisplayName} {(u.Model.Kind != EnemyKind.Normal ? PhaseRules.KindName(u.Model.Kind) : "")}{KindSuffix(u.Model)}";
                var nameStyle = new GUIStyle(Small) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                bool narrow = cw < 180f; // 4体以上：HP の数字はバーの右に（名前と重ならないように）。【RC2】あなたも（「あな20 / 20」と重なっていた）
                ShadowLabel(new Rect(tx, r.y + 3, tw, 18), name, Fit(nameStyle, name, narrow ? tw : tw - 52, 10), u.IsPlayer ? PlayerTextColor : IsV4Ui ? Color.white : Color.Lerp(u.BaseColor, Color.white, 0.45f)); // 【RC5】長い名前は小さく（切らない）
                bool immortal = (u.Model.Kind == EnemyKind.HealClone && !(turn.Grid.Model.SoftRules && !u.Model.Dormant)) || u.Model.IsObject; // 【戦闘中心 v3】殴れる写し身は HP を出す
                string hpText = dead ? "撃破" : u.Model.Kind == EnemyKind.HealClone && u.Model.Dormant ? "眠り" : immortal ? "不死" : narrow ? (u.IsPlayer ? $"{u.Hp}/{u.MaxHp}" : $"{u.Hp}") : $"{u.Hp} / {u.MaxHp}";
                float numW = narrow ? (u.IsPlayer ? 44f : 26f) : 0f;
                ShadowLabel(narrow ? new Rect(tx + tw - numW, r.y + 19, numW, 18) : new Rect(tx, r.y + 3, tw, 18), hpText, new GUIStyle(nameStyle) { alignment = TextAnchor.UpperRight }, Color.white);
                var bar = new Rect(tx, r.y + 24, tw - numW - (narrow ? 3 : 0), 9);
                DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));
                var col = u.IsPlayer ? new Color(0.3f, 0.6f, 1f) : IsV4Ui ? V4EnemyUiColor : u.BaseColor;
                DrawRect(new Rect(bar.x, bar.y, bar.width * u.Hp / Mathf.Max(1, u.MaxHp), bar.height), col);
                DrawFrame(bar, new Color(1f, 1f, 1f, 0.25f), 1f);
                if (!u.IsPlayer && u.Model.Kind == EnemyKind.SealLord && !dead)
                    foreach (var t in new[] { (float)PuzzleBoss.Floor(u.Model, 0) / u.MaxHp, (float)PuzzleBoss.Floor(u.Model, 1) / u.MaxHp }) // 【Puzzle v2】封紋の守護者：段階の境目
                        DrawRect(new Rect(bar.x + bar.width * t - 1, bar.y - 3, 2, bar.height + 6), new Color(1f, 0.95f, 0.6f, 0.95f));
                if (!u.IsPlayer && u.Model.Kind == EnemyKind.Lord && !dead)
                    foreach (var t in new[] { 0.7f, 0.4f }) // 【V3】写し身の主の段階の境目
                        DrawRect(new Rect(bar.x + bar.width * t - 1, bar.y - 3, 2, bar.height + 6), new Color(1f, 0.95f, 0.6f, 0.95f));
                if (!u.IsPlayer && u.Model.Kind == EnemyKind.KnightV3 && !dead)
                {
                    // 【検証中】監査 BD：ボスの段階の目印（60%：大剣が速くなる／30%：爆発が大十字・炎が速くなる）
                    foreach (var t in new[] { 0.6f, 0.3f })
                    {
                        float mx = bar.x + bar.width * t;
                        DrawRect(new Rect(mx - 1, bar.y - 3, 2, bar.height + 6), new Color(1f, 0.95f, 0.6f, 0.95f));
                    }
                }
            }
        }

        static string KindKey(UnitModel m) => m.IsPlayer ? "player" : VisualTheme.KindKey(m.Kind);

        // ───────── 盤面の上：番号・名札・ボスのカウントダウン・方向の矢印 ─────────

        void DrawShowBoardOverlay(float scale, float bw)
        {
            var cam = Camera.main;
            var grid = turn.Grid;
            if (cam == null || grid.Model == null) return;
            Vector2 ToGui(Vector3 world)
            {
                var s = cam.WorldToScreenPoint(world);
                return new Vector2(s.x / scale, (Screen.height - UiStyles.LetterboxY - s.y) / scale);
            }
            // 列・行の番号（盤面の枠の上）
            var num = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            for (int x = 0; x < grid.Width; x++)
            {
                var p = ToGui(grid.GridToWorld(new Vector2Int(x, grid.Height - 1)) + new Vector3(0f, 0.74f, 0f));
                ShadowLabel(new Rect(p.x - 12, p.y - 9, 24, 18), (x + 1).ToString(), num, new Color(0.85f, 0.85f, 0.95f, 0.85f));
            }
            for (int y = 0; y < grid.Height; y++)
            {
                var p = ToGui(grid.GridToWorld(new Vector2Int(0, grid.Height - 1 - y)) + new Vector3(-0.74f, 0f, 0f));
                ShadowLabel(new Rect(p.x - 12, p.y - 9, 24, 18), (y + 1).ToString(), num, new Color(0.85f, 0.85f, 0.95f, 0.85f));
            }

            // 【検証中】回復マス：「+4」（一度だけ・敵も使える）
            foreach (var hc in grid.Model.Environment.HealTiles)
            {
                var p = ToGui(grid.GridToWorld(hc));
                ShadowLabel(new Rect(p.x - 24, p.y - 12, 48, 24), $"+{grid.Model.Environment.HealTileAmount}", new GUIStyle(Big) { fontSize = 17, alignment = TextAnchor.MiddleCenter }, new Color(0.5f, 1f, 0.6f));
            }

            // 【RC3】ワープ陣：同じ色の2マスが1組。2マスを点線で結び、「ワープ」の札（踏んでみるまで分からない、をなくす）
            {
                int pair = 0;
                var done = new HashSet<Vector2Int>();
                foreach (var kv in grid.Model.Board.Warps)
                {
                    if (done.Contains(kv.Key)) continue;
                    done.Add(kv.Key); done.Add(kv.Value);
                    var col = pair % 2 == 0 ? GridManager.WarpColor : GridManager.WarpColor2;
                    var a0 = ToGui(grid.GridToWorld(kv.Key));
                    var b0 = ToGui(grid.GridToWorld(kv.Value));
                    float len = Vector2.Distance(a0, b0);
                    int dots = Mathf.Max(2, Mathf.RoundToInt(len / 12f));
                    bool warpHover = (Event.current.mousePosition - a0).magnitude < 36f || (Event.current.mousePosition - b0).magnitude < 36f;
                    if (CombatV2 && !warpHover) dots = 0; // 【RC8・初見テスト「青い点線がビーム攻撃に見えた」】マウスを乗せたときだけ
                    for (int i = 1; i < dots; i++)
                    {
                        var q = Vector2.Lerp(a0, b0, i / (float)dots);
                        DrawRect(new Rect(q.x - 2, q.y - 2, 4, 4), new Color(col.r, col.g, col.b, 0.55f));
                    }
                    string tagText = "ワープ" + (grid.Model.Board.Warps.Count > 2 ? ((char)('A' + pair)).ToString() : "");
                    foreach (var q in new[] { a0, b0 })
                        ShadowLabel(new Rect(q.x - 30, q.y + 12, 60, 16), tagText, new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(col.r, col.g, col.b, 1f));
                    pair++;
                }
            }

            // 騎士V3：1 になった大技の当たるマスに文字（赤い予告に加えて「何が来るか」）
            foreach (var e in grid.Model.Enemies)
            {
                if (!e.IsAlive || e.Kind != EnemyKind.KnightV3) continue;
                var tags = new List<(Vector2Int, string)>();
                if (e.SlamCd == 1) tags.AddRange(e.SlamCells.Select(c => (c, "剣")));
                if (e.WaveCd == 1) tags.AddRange(PhaseRules.WaveCells(grid.Model, e.WaveCount).Select(c => (c, "炎")));
                if (e.BlastCd == 1) tags.AddRange(e.BombCells.Select(c => (c, "爆")));
                foreach (var (c, t) in tags)
                {
                    var p = ToGui(grid.GridToWorld(c));
                    ShadowLabel(new Rect(p.x - 14, p.y - 14, 28, 28), t, new GUIStyle(Big) { fontSize = 18, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.92f, 0.5f));
                }
            }

            // 名札（敵は上・あなたは下。HP の小さなバーつき）
            // 【企画書 v5・UI】名札は文字の幅で作る。隣り合う敵の名札が重なるときは、重ならなくなるまで1段ずつ上へ（前は 80 幅で固定＝マスより広く、隣の名前が欠けた）
            var plateStyle = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            plateRects.Clear();
            plateLate.Clear();
            var placed = new List<Rect>();
            var plateOrder = grid.Units.Where(x => x.IsAlive).OrderBy(x => x.IsPlayer ? 1 : 0).ThenByDescending(x => x.Model.Position.y).ThenBy(x => x.Model.Position.x).ToList();
            bool arrowsShown = pendingCommand != null && !hasPendingDirection && turn.State == BattleState.PlayerInput;
            foreach (var u in plateOrder)
            {
                if (CombatV2) break; // 【RC8】名札は出さない：敵の記号と HP はマスの下の端、あなたは水色の枠と「▼あなた」
                if (IsV4Ui && u.Model.Kind == EnemyKind.HealClone && u.Model.Dormant) continue; // 【RC5・レビュー】眠る写し身は名札を出さない（主の足もと・隣の数字を隠していた）。マスの中の「眠り」の印と上の HP の欄で分かる
                float off = u.IsPlayer || u.Model.Kind == EnemyKind.KnightV3 || (u.Model.Kind == EnemyKind.HealClone && u.Model.Dormant) ? -0.6f : 0.62f; // ボスは頭の上にカウントダウンを出すので名札は下【RC5・レビュー】眠る写し身の名札も下（主の足もとを隠していた）
                // 方向の矢印が出ている間は、あなたの名札を右下へ（下の矢印に隠れない）
                var p = ToGui(u.transform.position + new Vector3(u.IsPlayer && arrowsShown ? 0.82f : 0f, off, 0f));
                string label = u.IsPlayer ? $"あなた {u.Hp}" : u.Model.IsObject ? $"{u.Model.Label}{PhaseRules.KindName(u.Model.Kind)}" : $"{(u.Model.Label.Length > 0 ? "敵" + u.Model.Label : "敵")} {u.Hp}"; // 【Puzzle v2】道具は HP を出さない
                bool v4Enemy = IsV4Ui && !u.IsPlayer && !u.Model.IsObject;
                if (v4Enemy) label = u.Model.Kind == EnemyKind.HealClone && u.Model.Dormant ? $"写し身{DupLabel(u.Model)}・眠り" : $"{V4Name(u.Model)}{DupLabel(u.Model)} {u.Hp}"; // 【RC2】V4：兵種名＋HP（同じ兵種が2体以上なら A・B…を付ける：「まねる：A・B」と結びつくように）
                float chipW = v4Enemy && u.Model.Clocks.Count > 0 ? PlateChipW : 0f;
                float pw = Mathf.Max(48f, plateStyle.CalcSize(new GUIContent(label)).x + 10f) + chipW;
                var plate = new Rect(p.x - pw / 2f, p.y - 10, pw, 20);
                if (!u.IsPlayer)
                    for (int lift = 0; lift < 3 && placed.Any(q => q.Overlaps(new Rect(plate.x - 1, plate.y, plate.width + 2, plate.height))); lift++)
                        plate.y -= plate.height + 1;
                if (u.IsPlayer && chipRects.Any(q => q.Overlaps(plate))) // 【RC6・レビュー】あなたの名札（足もと）が、下のマスの危険の札を隠していた → 頭の上へ（そこも札や名札があれば左へずらす）
                {
                    var up = ToGui(u.transform.position + new Vector3(0f, 0.62f, 0f));
                    var upPlate = new Rect(up.x - pw / 2f, up.y - 10, pw, 20);
                    if (!arrowsShown && !chipRects.Any(q => q.Overlaps(upPlate)) && !placed.Any(q => q.Overlaps(upPlate))) plate = upPlate;
                    else { var left = chipRects.Where(q => q.Overlaps(plate)).Min(q => q.x); plate.x = Mathf.Min(plate.x, left - plate.width - 2f); }
                }
                if (u.IsPlayer && placed.Any(q => q.Overlaps(new Rect(plate.x - 1, plate.y, plate.width + 2, plate.height))))
                {
                    // 【RC5】あなたの名札（足もと）が、下の段の敵の名札（頭の上）と重なった（中ボスの盤面・720p）→ 頭の上へ。そこも重なるなら下へずらす
                    var up = ToGui(u.transform.position + new Vector3(0f, 0.62f, 0f));
                    var upPlate = new Rect(up.x - pw / 2f, up.y - 10, pw, 20);
                    if (!arrowsShown && !placed.Any(q => q.Overlaps(new Rect(upPlate.x - 1, upPlate.y, upPlate.width + 2, upPlate.height)))) plate = upPlate; // 方向の矢印が出ている間は上の矢印を隠さない
                    else plate.y += plate.height + 1;
                }
                placed.Add(plate);
                plateRects[u.Model] = plate;
                if (u.IsPlayer && IsV4Ui)
                {
                    var me = ToGui(u.transform.position);
                    var near = new Vector2(Mathf.Clamp(me.x, plate.x, plate.xMax), Mathf.Clamp(me.y, plate.y, plate.yMax));
                    if ((near - me).magnitude > 34f) // 【RC7・ブラインドテスト】名札が危険の札をよけて離れると、自分を見失った → 名札からあなたへ点の線
                        for (int k = 1; k < 8; k++) { var q = Vector2.Lerp(near, me, k / 8f); DrawRect(new Rect(q.x - 2f, q.y - 2f, 4f, 4f), new Color(0.45f, 0.75f, 1f, 0.95f)); }
                }
                var textRect = new Rect(plate.x, plate.y, plate.width - chipW, plate.height);
                DrawRect(plate, new Color(0.02f, 0.03f, 0.07f, 0.78f));
                var col = u.IsPlayer ? new Color(0.35f, 0.65f, 1f) : IsV4Ui ? V4EnemyUiColor : u.BaseColor;
                DrawRect(new Rect(plate.x, plate.yMax - 3, textRect.width * u.Hp / Mathf.Max(1, u.MaxHp), 3), col);
                DrawFrame(plate, new Color(col.r, col.g, col.b, 0.7f), 1f);
                ShadowLabel(new Rect(textRect.x, textRect.y - 1, textRect.width, textRect.height), label, plateStyle, Color.white);
                plateLate.Add((textRect, label, col, u.Hp / (float)Mathf.Max(1, u.MaxHp)));
                // 向きが意味を持つ兵種（槍・弓・盾）：向いている側に小さな三角
                var m = u.Model;
                if (!u.IsPlayer && (m.Kind == EnemyKind.Spearman || m.ShootsArrows || m.HasShield))
                {
                    var f = ToGui(u.transform.position + (Vector3)(Vector2)m.Facing.ToOffset() * 0.5f);
                    ShadowLabel(new Rect(f.x - 12, f.y - 12, 24, 24), FacingGlyph(m.Facing), new GUIStyle(Big) { fontSize = 18, alignment = TextAnchor.MiddleCenter }, m.HasShield ? new Color(0.5f, 0.8f, 1f) : new Color(1f, 0.4f, 0.38f)); // 【RC5・レビュー】向き＝次の赤の向きなので赤（前はオレンジ＝敵の動きと同じ色）
                }
                // 騎士V3：頭の上にカウントダウン（剣・爆・炎）。1 は点滅
                if (m.Kind == EnemyKind.KnightV3)
                {
                    // 2026-10-05：上のマス（トゲ）を隠さないよう、騎士自身のマスの上の端に小さく
                    var head = ToGui(u.transform.position + new Vector3(0f, 0.36f, 0f));
                    DrawCountdownChips(new Rect(head.x - 60, head.y - 9, 120, 18), m);
                }
            }

            // 【検証中】あなたのカードの範囲（敵のコピーの結果は出さない）。組んだ命令があれば、その後の位置（「予定」）から
            var planPos = turn.State == BattleState.PlayerInput ? PlayerPreview.PlannedPosition(grid.Model, turn.PlannedCommands, turn.PlayerAttackBonusEachTurn) : grid.Model.Player.Position;
            lateLabels.Clear();
            previewRecordLast = previewRecord; // 【RC8】前の描画の「選んでいる向き」（敵が自分で動く先の予告に入れる）
            previewRecord = null; // 【RC7】
            Rect CellRect(Vector2Int c)
            {
                var w0 = grid.GridToWorld(c);
                var a = ToGui(w0 + new Vector3(-0.46f, 0.46f, 0f));
                var b = ToGui(w0 + new Vector3(0.46f, -0.46f, 0f));
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            DrawCopyCoreMarks(grid.Model, CellRect);
            DrawPuzzleMarks(grid.Model, CellRect); // 【Puzzle v2】封印石・石像・出口・運び紋
            if (CombatV2) { DrawThreatOverlayV2(CellRect); DrawPlayerMarkerV2(CellRect, ToGui, arrowsShown); } // 【RC8】危険（記号・種類・数字）・敵の札・あなたの印
            else if (IsV4Ui) DrawV4BoardOverlay(CellRect); // 【Combat V4】大技のカウント・①の予告・戦術札の対象
            if (turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count > 0 && planPos != grid.Model.Player.Position)
            {
                var gr = CellRect(planPos);
                DrawFrame(gr, Gold, 2f);
                if (!CombatV2) ShadowLabel(new Rect(gr.x - 10, gr.yMax - 16, gr.width + 20, 16), "予定", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, Gold); // 【通常リリース版・画面の直し 5】本編の画面は字を出さない（金の枠だけ：マスの札にかぶっていた）
            }
            if (pendingCommand != null && turn.State == BattleState.PlayerInput)
            {
                var basePos = grid.GridToWorld(planPos);
                Direction? focus = hasPendingDirection ? pendingDirection : DebugFocus;
                if (!hasPendingDirection && DirectionAt(Event.current.mousePosition, out var hoverDir)) focus = hoverDir; // 【RC4】矢印・隣のマス・その向きの範囲
                bool focusFromHover = focus.HasValue; // 【RC8】向きを選んでいる間は「まね」の札を出さない（敵のマスに重なった）
                if ((IsCopySandbox || ShowCopyKinds) && focus.HasValue) DrawCopyArrows(grid.Model, pendingCommand, focus.Value, CellRect);
                // 【Copy Core V3】札を選ぶと、その札をまねる敵だけが光る（研究：手札から「この札を誰が使うか」が一瞬で分かる）
                if (ShowCopyKinds && grid.Model.CopyEnabled && !pendingCommand.notCopied && !pendingCommand.IsNoBoardEffect) // 【Puzzle v2】手を回す札は「まねる：なし」（光らせない）
                    foreach (var en in grid.Model.Enemies)
                        if (en.IsAlive && CopyKinds.Accepts(en, pendingCommand) && !CopyCore.Stunned(en))
                        {
                            var cr = CellRect(en.Position);
                            if (CombatV2)
                            {
                                // 【RC8・初見テスト「敵にも自分と同じ水色の枠 → もう一人の自分？」】まねる敵は水色の点線と「まね」の札（あなたは水色の太い実線の枠）
                                DrawDashedFrame(new Rect(cr.x + 1, cr.y + 1, cr.width - 2, cr.height - 2), new Color(0.35f, 0.95f, 1f, 0.95f), 3f, 6f);
                                if (!focusFromHover)
                                {
                                    var mt = new Rect(cr.center.x - 18, cr.y + cr.height * 0.28f, 36, 15);
                                    DrawRect(mt, new Color(0.02f, 0.2f, 0.25f, 0.92f));
                                    GUI.Label(mt, "まね", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.6f, 1f, 1f) } });
                                }
                                continue;
                            }
                            // 水色＝この札をまねる敵（トゲ・範囲の金色と分ける）。【企画書 v5・UI】「まねる」の文字はやめた（まね方の札と二段に重なった）。上の辺は名札の下から
                            DrawFrame(new Rect(cr.x, cr.y + cr.height * 0.2f, cr.width, cr.height * 0.8f), new Color(0.35f, 0.95f, 1f, 0.95f), 3f);
                        }
                var cat = CategoryOf(pendingCommand);
                // 【RC5・色の意味を固定】あなたの札が届く範囲は白（選んでいる向きは金）。オレンジ＝敵が自分で動く先・赤紫＝大技・紫寄り＝押し引きと重ならないように
                //   （RC4 までは 攻撃＝橙・押し引き＝紫・移動＝青 で、オレンジが「敵の動き」と、紫が「大技」と二重の意味だった）
                Color main = V4PlayerRangeColor;
                if (!IsV4Ui) main = cat == CardCategory.Attack ? new Color(1f, 0.5f, 0.2f) : cat == CardCategory.Support ? new Color(0.75f, 0.5f, 1f)
                           : cat == CardCategory.Buff ? new Color(0.4f, 1f, 0.55f) : new Color(0.3f, 0.55f, 1f); // 【Puzzle v2】移動は青（水色＝まねる敵と分ける）
                var hitStyle = new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.UpperRight };
                foreach (var o in PlayerPreview.Compute(grid.Model, turn.PlannedCommands, pendingCommand, turn.PlayerAttackBonusEachTurn, pendingCard))
                {
                    bool hl = focus.HasValue && o.Direction == focus;
                    if (IsV4Ui && focus.HasValue && !hl && o.Direction.HasValue) continue; // 【RC7・ブラインドテスト】向きにマウスを乗せている間は、その向きの範囲と数字だけ（両方の敵に「4ダメ」が出て、与える・受けるが分からなかった）
                    // 【RC7】置く前の試し：札の実体ごと（強化の ±）。距離を選ぶ札は距離が決まるまで出さない（コードレビュー：いつも短い方の結果を出していた）
                    if (hl && previewRecord == null && o.Direction.HasValue && !pendingCommand.NeedsDistanceChoice) { previewRecord = new CommandRecord(pendingCommand, o.Direction.Value, o.Distance, pendingCard); previewLanding = o.Landing; }
                    if (focus.HasValue && !hl && hasPendingDirection) continue;
                    var col = hl ? Gold : main;
                    float a = hl ? 0.32f : 0.16f;
                    foreach (var c in o.Path)
                    {
                        var r = CellRect(c);
                        DrawRect(r, new Color(col.r, col.g, col.b, a));
                        DrawFrame(r, new Color(col.r, col.g, col.b, 0.85f), c == o.Landing ? 3f : 1f);
                    }
                    foreach (var c in o.AttackCells)
                    {
                        var r = CellRect(c);
                        DrawRect(r, new Color(col.r, col.g, col.b, a));
                        DrawFrame(r, new Color(col.r, col.g, col.b, 0.9f), 2f);
                    }
                    foreach (var c in o.ControlCells)
                    {
                        if (IsV4Ui) { var cr2 = CellRect(c); DrawDashedFrame(new Rect(cr2.x + 2, cr2.y + 2, cr2.width - 4, cr2.height - 4), hl ? Gold : new Color(1f, 1f, 1f, 0.9f), 2f, 5f); } // 【RC5】押し引きで敵が動く先：白い点線
                        if (IsV4Ui && hl && CombatV2 && AnswerPreview) DrawPushGhost(o, c, CellRect); // 【RC9・チュートリアルの初見テスト「押した後の敵の位置が見えない」】チュートリアルだけ：動く敵の薄い絵
                        else DrawFrame(CellRect(c), new Color(0.75f, 0.5f, 1f, 0.9f), 2f);
                    }
                    foreach (var (c, dmg) in o.Hits)
                    {
                        var r = CellRect(c);
                        if (CombatV2)
                        {
                            // 【RC8】右上は危険の数字の札。あなたの攻撃の数字は、敵のマスの真ん中の下に金の札
                            if (focus.HasValue && !hl) continue;
                            var hr = new Rect(r.xMax - 36f, r.y + 2f, 34f, 18f); // 【RC9】右上（向きを選んでいる間はまね方の印を出さない） // 【RC8・初見テスト3】敵の絵を隠さないよう上の端（向きを選んでいる間、上の端の危険の札は出していない）
                            DrawRect(hr, new Color(0.25f, 0.18f, 0.02f, 0.92f));
                            DrawFrame(hr, hl ? Gold : Color.white, 1.5f);
                            ShadowLabel(hr, $"-{dmg}", new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false }, hl ? Gold : Color.white); // 【RC9・初見テスト「3ダメ が自分の受けるダメージに見えた・予告は出ないのに？」】あなたの攻撃でその敵が減る数（あなたの札の効果）
                            continue;
                        }
                        ShadowLabel(new Rect(r.x, r.y, r.width - 2, 20), $"{dmg}ダメ", hitStyle, hl ? Gold : IsV4Ui ? Color.white : new Color(1f, 0.75f, 0.4f));
                    }
                    if (o.SelfDamage > 0 && (hl || !focus.HasValue) && !(CombatV2 && !AnswerPreview)) // 【RC9】本編は「あなた Nダメ」を出さない（トゲの札「トゲ3」がルール）
                    {
                        // 【検証中】監査 UI-12：あなた自身がトゲに入るダメージも出す（敵への -N と同じ場所の左下に赤で）
                        // 着地のマスの左上（矢印は縁の真ん中にあるので避ける）。矢印の後に描く（レビュー2）
                        var r = CellRect(o.Landing);
                        lateLabels.Add((new Rect(r.x + 2, r.y + 1, Mathf.Min(r.width - 4, 70), 18), $"あなた {o.SelfDamage}ダメ", new Color(1f, 0.4f, 0.4f)));
                    }
                    if (o.Healed > 0)
                    {
                        var r = CellRect(o.Landing);
                        DrawRect(r, new Color(0.4f, 1f, 0.55f, a));
                        ShadowLabel(new Rect(r.x, r.y, r.width - 2, 20), $"+{o.Healed}", hitStyle, new Color(0.5f, 1f, 0.6f));
                    }
                    // 【検証中】反射のしるし（鏡兵・鏡守の騎士）：選んでいる向きの攻撃だけ。紫の点線＝反射される範囲。完全予測ではない
                    if (hl && o.Direction.HasValue && (cat == CardCategory.Attack || pendingCommand.type == CommandType.Push || pendingCommand.type == CommandType.Pull))
                    {
                        var purple = new Color(0.82f, 0.55f, 1f);
                        var tagStyle = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                        var skipSlots = act != null ? new HashSet<int>(turn.UncopiedSlots) : null;
                        foreach (var rf in PlayerPreview.Reflections(grid.Model, turn.PlannedCommands, new CommandRecord(pendingCommand, o.Direction.Value, o.Distance), skipSlots, turn.PlayerAttackBonusEachTurn))
                        {
                            foreach (var c in rf.Cells) { var cr = CellRect(c); DrawDashedFrame(new Rect(cr.x + 3, cr.y + 3, cr.width - 6, cr.height - 6), purple, 2f, 5f); }
                            // 札は敵のマスの左の真ん中（上の端はボスのカウントダウンと「-N」、下の真ん中は矢印があるので避ける：レビュー）
                            var er = CellRect(rf.Enemy);
                            var tag = new Rect(er.x + 1, er.center.y - 8, 42, 16);
                            DrawRect(tag, new Color(0.15f, 0.05f, 0.25f, 0.85f));
                            ShadowLabel(tag, rf.Uncertain ? "反射？" : "反射", tagStyle, purple);
                            if (rf.DamageToYou > 0)
                            {
                                // あなたへの反射ダメージは、予定のマスの左上の外（「予定」・矢印と重ねない）。矢印の後に描く
                                var pr = CellRect(planPos);
                                lateLabels.Add((new Rect(pr.x - 74, pr.y + 1, 72, 20), rf.Uncertain ? $"反射？{rf.DamageToYou}ダメ" : $"反射 {rf.DamageToYou}ダメ", purple));
                            }
                        }
                    }
                }
            }

            // 方向の矢印（カードを選んだら、あなたの予定の位置の周りに）
            if (pendingCommand != null && turn.State == BattleState.PlayerInput)
            {
                var pp = grid.GridToWorld(planPos);
                var center = ToGui(pp);
                // 2026-10-05 監査 UI-14：案内の帯は盤面の上の端に固定（前は予定の位置の上に出て、2マス先の範囲と反射のしるしを隠した）
                var bA = CellRect(new Vector2Int(0, 0));
                var bB = CellRect(new Vector2Int(grid.Model.Board.Width - 1, grid.Model.Board.Height - 1));
                float boardTop = Mathf.Min(bA.y, bB.y), boardMid = (Mathf.Min(bA.x, bB.x) + Mathf.Max(bA.xMax, bB.xMax)) / 2f;
                // 列の番号・上の行の敵の名札より上（HP の段の下のすき間）
                var hint = new Rect(boardMid - 285, Mathf.Max(100f, boardTop - bA.height * 0.75f - 24f), 570, 20); // 【RC4】案内の文が長くなった分
                DrawRect(hint, new Color(0f, 0f, 0f, 0.8f));
                string what = hasPendingDirection ? $"『{pendingCommand.displayName}』距離を選ぶ" : $"『{pendingCommand.displayName}』の向き：隣のマスをクリック（W A S D・矢印キー）　右クリック・Esc で取り消し"; // 【RC4】
                if (IsCopySandbox && !hasPendingDirection) what = $"『{pendingCommand.displayName}』の方向を選ぶ　{CopyHintFor(turn.Grid.Model, pendingCommand)}";
                // 【RC7】向きにマウスを乗せている間は、その命令で このターン受けるダメージも案内の帯に（盤面のあなたの上は矢印と重なるので、帯で読む）
                var sd = SelfDamagePreview();
                if (sd.known && !hasPendingDirection)
                {
                    what = $"『{pendingCommand.displayName}』の向き：隣のマス　";
                    string tail = sd.dies ? "→ この向きだと あなたが倒れる" : sd.total > 0 ? $"→ この向きだと あなた {sd.total}ダメ{(sd.back > 0 ? $"（まね返し {sd.back}）" : "")}" : sd.idleHurt ? "→ この向きなら 無傷" : "";
                    var st2 = new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false, richText = true, normal = { textColor = Gold } };
                    string full = what + (tail.Length > 0 ? $"<color={(sd.total > 0 || sd.dies ? "#ff7a6e" : "#7fe08f")}><b>{tail}</b></color>" : "");
                    float tw = st2.CalcSize(new GUIContent(System.Text.RegularExpressions.Regex.Replace(full, "<.*?>", ""))).x;
                    if (tw + 16 > hint.width) DrawRect(new Rect(hint.center.x - tw / 2f - 8f, hint.y, tw + 16f, hint.height), new Color(0f, 0f, 0f, 0.85f)); // 帯より長いときは下地も広げる（右が切れていた）
                    GUI.Label(new Rect(hint.center.x - tw / 2f, hint.y, tw + 8, hint.height), full, st2);
                }
                else
                GUI.Label(hint, what, new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Gold } });
                if (!hasPendingDirection)
                {
                    // 【RC11・本人の RC10 プレイ：NullReferenceException 70回】矢印のボタンを押すと ChooseDirection が pendingCommand を null にする。
                    //   同じループの続き（「空振り」の札）で pendingCommand.type を読んで落ちていた → ループの前に手元へ写し、押したらループを抜ける
                    var cmdNow = pendingCommand;
                    bool diag = cmdNow.type == CommandType.Diagonal;
                    foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
                    {
                        var off = diag ? d.ToDiagonalOffset() : d.ToOffset();
                        // 2026-10-05：矢印は小さく・あなたのマスの縁に（隣のマスの範囲プレビューを隠さない）
                        bool busy = CombatV2 && !AnswerPreview && grid.Model.GetUnitAt(planPos + new Vector2Int(off.x, off.y)) != null; // 【RC9・初見テスト3・4回目「向きの矢印が隣の敵の記号と HP を隠す」】隣に誰かいれば、矢印はあなたのマスの内側へ（隣のマス全体はそのまま押せる）
                        // 【通常リリース版・画面の直し 4「向きの矢印で自分が隠れる」】矢印はマスの縁の少し外寄り・小さめ・半透明（前は隣に誰かいると あなたのマスの内側 0.3 に入り、自分の絵を隠した）
                        var p = ToGui(pp + new Vector3(off.x, off.y, 0f) * (CombatV2 ? (busy ? 0.44f : 0.56f) : busy ? 0.3f : 0.5f));
                        float asz = CombatV2 ? (busy ? 30f : 34f) : busy ? 32f : 38f;
                        var r = new Rect(p.x - asz / 2f, p.y - asz / 2f, asz, asz); // 【RC4】30 → 38（押しやすく）
                        string glyph = diag ? DiagGlyph(d) : DirGlyph(d);
                        bool hot = (DirectionAt(Event.current.mousePosition, out var hd) && hd == d) || DebugFocus == d; // DebugFocus＝撮影用
                        if (hot && dirAreas.TryGetValue(d, out var area)) { DrawRect(area, new Color(Gold.r, Gold.g, Gold.b, 0.18f)); DrawFrame(area, Gold, 2f); }
                        var oldCol = GUI.color;
                        if (CombatV2 && !hot) GUI.color = new Color(oldCol.r, oldCol.g, oldCol.b, oldCol.a * 0.5f); // 【RC10・初見テスト「向きの矢印があなたのマスの赤を隠す」】透けて下の危険が見える【通常リリース版・画面の直し 4】半透明（0.72 → 0.5）
                        bool pressed = ThemedButton(r, glyph, hot, true, 18);
                        GUI.color = oldCol;
                        if (pressed) { ChooseDirection(d); break; } // 入力は DrawShowcase の最初で受ける（ここは見た目と予備）。押したら pendingCommand は null になるのでループを抜ける
                        if (CombatV2 && !diag && WhiffMoveCard(cmdNow))
                        {
                            // 【RC10・初見テスト3回目（戦術好き）「壁や盤の端へ向かう移動を置けるのか・置いたら敵だけ動くのかが画面から分からない（核の『同じ突進で敵だけを危険へ』）」】
                            var tc = planPos + new Vector2Int(off.x, off.y);
                            var bm = grid.Model;
                            if (!bm.Board.IsInside(tc) || bm.Board.IsWall(tc) || bm.GetUnitAt(tc) != null)
                            {
                                var lr = new Rect(r.center.x - 26f, r.yMax - 3f, 52f, 14f);
                                DrawRect(lr, new Color(0f, 0f, 0f, 0.75f));
                                GUI.Label(lr, "空振り", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.9f, 0.6f) } });
                                if (hot) cardTipTitle = "空振り：あなたは動かない";
                                if (hot) cardTipBody = "壁・盤の端・誰かがいる方へは進めない（あなたはその場）。まねる敵は、それぞれの位置から同じ向きに動く。";
                            }
                        }
                    }
                    if (CombatV2) DrawPlayerDangerV2(CellRect, ToGui, true); // 【RC10・初見テスト2回目「4つの矢印で、自分のマスが赤いか見えない」】赤い内枠は矢印の上に（札は矢印の下：4回目「↑が札に隠れる」）
                }
                else
                {
                    var cmdNow = pendingCommand; // 【RC11】Register で pendingCommand が null になる（上の矢印と同じ）
                    int n = cmdNow.maxDistance - cmdNow.minDistance + 1;
                    for (int k = 0; k < n; k++)
                    {
                        int dist = cmdNow.minDistance + k;
                        var r = new Rect(center.x - n * 40 + k * 80, center.y + 40, 74, 34);
                        if (ThemedButton(r, $"{dist}マス", false, true, 15)) { Register(pendingDirection, dist); break; }
                    }
                }
            }
            // 【企画書 v5・UI】一言（同士討ち！など）は盤面の上の帯に1つだけ（名札に隠れない・二重にならない）
            Callout.DrawnAt = Time.unscaledTime;
            if (Callout.Visible)
            {
                var cA = CellRect(new Vector2Int(0, 0));
                var cB = CellRect(new Vector2Int(grid.Model.Board.Width - 1, grid.Model.Board.Height - 1));
                float top = Mathf.Min(cA.y, cB.y), mid = (Mathf.Min(cA.x, cB.x) + Mathf.Max(cA.xMax, cB.xMax)) / 2f;
                var cs = new GUIStyle(Big) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                float cw = cs.CalcSize(new GUIContent(Callout.Text)).x + 36f;
                var cr = new Rect(mid - cw / 2f, Mathf.Max(100f, top - cA.height * 0.75f - 30f), cw, 30);
                float k = (Time.unscaledTime - Callout.ShownAt) / Callout.Life;
                float alpha = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
                DrawRect(cr, new Color(0.02f, 0.03f, 0.07f, 0.88f * alpha));
                DrawFrame(cr, new Color(Callout.Color.r, Callout.Color.g, Callout.Color.b, alpha), 2f);
                ShadowLabel(cr, Callout.Text, cs, new Color(Callout.Color.r, Callout.Color.g, Callout.Color.b, alpha));
            }
            foreach (var (lr, text, color) in lateLabels)
            {
                DrawRect(lr, new Color(0.1f, 0.03f, 0.12f, 0.85f));
                ShadowLabel(lr, text, new GUIStyle(Big) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false }, color);
            }
            if (IsV4Ui) // 【RC5】名札を一番上に（大技のカウントの札は名札の右の別の場所なので消さない）
            {
                var ps = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                foreach (var (tr, label, col, hp) in plateLate)
                {
                    DrawRect(tr, new Color(0.02f, 0.03f, 0.07f, 0.9f));
                    DrawRect(new Rect(tr.x, tr.yMax - 3, tr.width * hp, 3), col);
                    ShadowLabel(new Rect(tr.x, tr.y - 1, tr.width, tr.height), label, ps, Color.white);
                }
                DrawSelfDamageBadge(CellRect, planPos); // 【RC7】
            }
        }

        void DrawCountdownChips(Rect r, UnitModel m)
        {
            var items = new[] { ("剣", m.SlamCd, new Color(1f, 0.35f, 0.3f)), ("爆", m.BlastCd, new Color(1f, 0.6f, 0.2f)), ("炎", m.WaveCd, new Color(0.75f, 0.45f, 1f)) };
            float w = r.width / 3f;
            for (int i = 0; i < 3; i++)
            {
                var (name, cd, col) = items[i];
                var c = new Rect(r.x + i * w + 1, r.y, w - 2, r.height);
                bool soon = cd == 1;
                float blink = soon ? 0.55f + 0.45f * Mathf.Sin(Time.time * 8f) : 1f;
                DrawRect(c, new Color(0.02f, 0.02f, 0.05f, 0.82f));
                DrawFrame(c, new Color(col.r, col.g, col.b, soon ? blink : 0.7f), soon ? 2f : 1f);
                ShadowLabel(c, cd > 0 ? $"{name}{cd}" : $"{name}—", new GUIStyle(Label) { fontSize = r.height < 20 ? 13 : 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, soon ? new Color(1f, 1f, 0.6f) : col);
            }
        }

        // ───────── 左下：ステージの説明と凡例 ─────────

        void DrawShowInfo(Rect r)
        {
            DrawUi(r, "panel", PanelBlue);
            if (IsPuzzleUi)
            {
                // 【Puzzle v2】攻略条件（目的・進み具合・倒し方）を大きめに。右は小さな凡例
                float gw = r.width * 0.7f;
                DrawGoalPanel(new Rect(r.x, r.y, gw, r.height));
                DrawMiniLegend(new Rect(r.x + gw + 4, r.y + 4, r.width - gw - 10, r.height - 8));
                return;
            }
            var stage = turn.CurrentStage;
            string text = turn.TurnNumber >= 2 && !string.IsNullOrEmpty(stage.hintAfterFirstTurn) ? stage.hintAfterFirstTurn : RoomText(stage);
            float lw = r.width * 0.64f;
            var body = new GUIStyle(Tiny) { fontSize = 11, wordWrap = true, clipping = TextClipping.Clip };
            body.normal.textColor = new Color(0.88f, 0.9f, 0.96f);
            var mission = act != null && act.InBattle ? act.Run?.Current?.Mission : null;
            if (mission != null) text = $"【任意ミッション】{mission.Text}（+{mission.Reward} ルーン片）\n" + text;
            GUI.Label(new Rect(r.x + 12, r.y + 8, lw - 16, r.height - 14), text, body);

            // 凡例（色と形＝ギミック）
            var items = new List<(string key, string label, Color fallback)>
            {
                turn.Grid.Model != null && turn.Grid.Model.Environment.HealTiles.Count > 0
                    ? ("heal", "回復マス（1回）", new Color(0.35f, 0.95f, 0.5f)) : ("floor_a", "床", new Color(0.3f, 0.33f, 0.42f)),
                ("wall", "壁（通れない）", new Color(0.4f, 0.42f, 0.5f)),
                ("spike", $"トゲ（{grid0SpikeDamage()}）", new Color(0.95f, 0.75f, 0.3f)), ("warp_a", "ワープ陣", new Color(0.4f, 0.5f, 1f)),
                ("danger", "敵の範囲", new Color(1f, 0.25f, 0.25f)), ("bomb", "爆発の予告", new Color(1f, 0.35f, 0.3f)),
            };
            float lx = r.x + lw, cw = (r.width - lw - 8) / 2f, ch = (r.height - 12) / 3f;
            for (int i = 0; i < items.Count; i++)
            {
                var (key, label, fb) = items[i];
                var cell = new Rect(lx + (i % 2) * cw, r.y + 6 + (i / 2) * ch, cw, ch);
                var icon = new Rect(cell.x, cell.y + 2, ch - 6, ch - 6);
                var tex = LegendTexture(key);
                if (tex != null) GUI.DrawTexture(icon, tex, ScaleMode.ScaleToFit);
                else DrawRect(icon, fb);
                GUI.Label(new Rect(icon.xMax + 4, cell.y, cell.width - icon.width - 6, ch), label, new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.MiddleLeft, wordWrap = false });
            }
        }

        int grid0SpikeDamage() => turn.Grid != null && turn.Grid.Model != null ? turn.Grid.SpikeDamage : 0;

        readonly Dictionary<string, Texture2D> legendCache = new Dictionary<string, Texture2D>();
        VisualTheme legendTheme;

        Texture2D LegendTexture(string key)
        {
            var th = turn.CurrentStage.visualTheme != null ? turn.CurrentStage.visualTheme : uiTheme;
            if (th == null) return null;
            if (th != legendTheme) { legendCache.Clear(); legendTheme = th; }
            if (legendCache.TryGetValue(key, out var t)) return t;
            var prefab = th.Get(key);
            var sr = prefab != null ? prefab.GetComponent<SpriteRenderer>() : null;
            var v = prefab != null ? prefab.GetComponent<SpriteVariant>() : null;
            var sprite = v != null && v.sprites != null && v.sprites.Length > 0 ? v.sprites[0] : sr != null ? sr.sprite : null;
            legendCache[key] = t = sprite != null ? sprite.texture : null;
            return t;
        }

        // ───────── 右：手札 ─────────

        int handPage;

        /// <summary>【Puzzle v2】手札の並び：group なら同じ札を1つの束に（手札に入った順）。テストあり。</summary>
        public static List<List<CardInstance>> HandStacks(IReadOnlyList<CardInstance> hand, bool group)
        {
            var entries = new List<List<CardInstance>>();
            foreach (var c in hand)
            {
                var e = group ? entries.FirstOrDefault(l => l[0].Data == c.Data) : null;
                if (e != null) e.Add(c); else entries.Add(new List<CardInstance> { c });
            }
            return entries;
        }
        float restartArmedAt = -10f;
        float giveUpArmedAt = -10f;
        /// <summary>【戦闘中心 v3】手札整理で捨てる札を選んでいる間。</summary>
        bool tidyMode;
        readonly List<CardInstance> tidyPick = new List<CardInstance>();

        /// <summary>【通常リリース版・画面の直し 1】手札を暗くする間：実行中・勝ち／負け・ボスの段階の帯・中ボスとボスの登場の帯。</summary>
        bool HandShouldDim => turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting || turn.State == BattleState.Cleared || turn.State == BattleState.GameOver
                              || phaseTitle != null || (introTitle != null && Time.unscaledTime - introStart <= 1.8f);

        float DrawShowHand(float x, float y, float w)
        {
            UiStyles.CompactCards = V2Layout; // 【RC9】戦闘の手札は短い札（全文は右下）
            try { return DrawShowHandInner(x, y, w); } finally { UiStyles.CompactCards = false; }
        }

        float DrawShowHandInner(float x, float y, float w)
        {
            var deck = turn.Deck;
            bool input = turn.State == BattleState.PlayerInput;
            var head = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false };
            bool seven = deck.Policy == DrawPolicy.RefillSeven;
            ShadowLabel(new Rect(x, y - 2, 160, 22), FestivalUi ? "手札" : IsV4Ui && turn.Tactics != null ? $"手札 {deck.Hand.Count} ＋ 戦術札 {turn.Tactics.Hand.Count}" : seven ? $"手札 {deck.Hand.Count}（基本 {deck.BaseHand}）" : $"手札 {deck.Hand.Count}/{deck.HandLimit}", head, Gold); // 【文化祭・10歳の立場のレビュー「基本 が分からない」】
            string note = FestivalUi ? "" : seven ? "使わなかった札は残る・毎ターン基本の枚数まで補充" : "使わなかった札は手札に残る"; // 【文化祭】小さい注意書きは出さない
            if (IsV4Ui && turn.Tactics != null) note = turn.Tactics.HandMax <= 1 ? (V2Layout ? "緑（Z）＝枠を使わない・まねされない" : "緑（Z）：枠を使わない・まねされない・毎ターン1枚") : $"緑（Z X）：{turn.Tactics.HandMax}枚まで・枠を使わない・まねされない"; // 【RC6・レビュー】切れていた // 【RC3・レビュー】「あと2枚」は手札の枚数と読まれた【RC6】緑は1枚
            if (turn.PendingSift && !tidyMode) { tidyMode = true; tidyPick.Clear(); } // 【Combat V4】選別（戦術札）：手札整理と同じ画面
            if (!turn.CanTidy && !turn.PendingSift) { tidyMode = false; tidyPick.Clear(); }
            if (IsCopySandbox) DrawCopyModeBar(new Rect(x + 168, y - 3, w - 168, 24));
            else if (IsCopyLabV3 && !tidyMode && !CleanCapture) // 【通常リリース版】きれいな撮影では研究室の帯（まね ON／一覧へ）を出さない
            {
                DrawCopyLabBar(new Rect(x + 168, y - 3, w - 168 - (turn.CanTidy ? 196 : 0), 24));
                // 【戦闘中心 v3】研究室でも手札整理を試せるように（前は研究室の帯で隠れていた）
                if (turn.CanTidy && ThemedButton(new Rect(x + w - 190, y - 3, 190, 24), $"手札整理（{turn.TidyMax}枚・1戦1回）", false, false, 12)) { tidyMode = true; tidyPick.Clear(); ResetPending(); }
            }
            else if (tidyMode)
            {
                // 【戦闘中心 v3】手札整理：捨てる札を選ぶ（クリックで選ぶ・もう一度で外す）
                GUI.Label(new Rect(x + 172, y, w - 172 - 250, 20), $"{(turn.PendingSift ? "選別" : "手札整理")}：捨てる札を {(turn.PendingSift ? 2 : turn.TidyMax)} 枚まで選ぶ（命令に入れた札は選べない）", new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 0.75f, 0.55f) } });
                if (ThemedButton(new Rect(x + w - 246, y - 3, 150, 24), $"捨てて引く（{tidyPick.Count}枚・Enter）", false, true, 12, tidyPick.Count > 0)) { if (turn.PendingSift) turn.SiftCards(tidyPick); else turn.Tidy(tidyPick); tidyMode = false; tidyPick.Clear(); }
                if (ThemedButton(new Rect(x + w - 92, y - 3, 92, 24), "やめる", false, false, 12)) { if (turn.PendingSift) turn.CancelSift(); tidyMode = false; tidyPick.Clear(); }
            }
            else
            {
                float noteW = w - 172 - (turn.CanTidy ? 196 : 0);
                string noteText = note + (turn.CanTidy ? "" : "　（マウスを乗せると詳しい説明）");
                if (!V2Layout) // 【通常リリース版・画面の直し 2】本編の画面は手札の上の補足を出さない（緑の札・残る札の話は凡例 L へ）
                GUI.Label(new Rect(x + 172, y, noteW, 20), noteText, // 【レビュー】手札整理のボタンで切れていた【RC5・レビュー3】入りきらなければ小さく（4:3 で「（Z X C」が切れた）
                     Fit(new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.75f, 0.78f, 0.86f) } }, noteText, noteW, 9));
                if (turn.CanTidy && ThemedButton(new Rect(x + w - 190, y - 3, 190, 24), $"手札整理（{turn.TidyMax}枚・1戦1回）", false, false, 12)) { tidyMode = true; tidyPick.Clear(); ResetPending(); }
            }
            y += 24;
            // 【Puzzle v2・研究】手札の上限なし。8枚を超えたら同じ札を束ねて（×2）2段のまま。スクロール・ページ・3段は「見えない札・読めない札」が出るので使わない。
            // 束ねても 18 種類を超えるときだけページ（実際はデッキの種類数まで）
            bool group = deck.Hand.Count > 8;
            var entries = HandStacks(deck.Hand, group);
            int allSlots = group ? entries.Count : Mathf.Max(deck.DisplayHandSize, deck.Hand.Count);
            const int PageSize = 18;
            int pages = Mathf.Max(1, Mathf.CeilToInt(allSlots / (float)PageSize));
            handPage = Mathf.Clamp(handPage, 0, pages - 1);
            int start = handPage * PageSize;
            int slots = Mathf.Min(PageSize, allSlots - start);
            if (pages > 1)
            {
                var pr = new Rect(x + w - 132, y - 22, 132, 20);
                if (ThemedButton(new Rect(pr.x, pr.y, 36, 20), "◀", false, false, 12, handPage > 0)) handPage--;
                GUI.Label(new Rect(pr.x + 38, pr.y, 56, 20), $"{handPage + 1}/{pages}", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter });
                if (ThemedButton(new Rect(pr.x + 96, pr.y, 36, 20), "▶", false, false, 12, handPage < pages - 1)) handPage++;
            }
            // 【RC6・本人の実プレイ】描く前に戦術札の手札を写しておく：描いている途中で緑の札を使うと手札から消え、残りを読みにいって ArgumentOutOfRangeException（2枚目以降の手前の札を押したとき）
            var tacticHand = TacticHandForDrawing(turn);
            int tn = tacticHand.Count; // 【RC2】戦術札（緑）も同じ手札の列に絵つきのカードとして並べる
            int total = slots + tn;
            int perRow = total <= 8 ? 4 : total <= 12 ? Mathf.CeilToInt(total / 2f) : Mathf.CeilToInt(total / 3f);
            if (tn > 0 && total <= 10 && !(V2Layout && total <= 8)) perRow = 5; // 【RC8・レビュー】新しい配置は 4×2（札の文字が小さかった）
            int rows = Mathf.CeilToInt(total / (float)perRow);
            if (V2Layout) { perRow = total <= 8 ? 4 : Mathf.CeilToInt(total / 2f); rows = 2; } // 【第2巡 4】いつも2段（4列。9枚目からは列を増やして札を細く）：命令の欄と実行ボタンが上下に動かない
            float gap = 8;
            float cw = (w - gap * (perRow - 1)) / perRow;
            float maxH = IsV4Ui && turn.Tactics != null ? 372f : 404f;
            float ch = Mathf.Min(cw * (IsV4Ui && turn.Tactics != null ? 1.6f : 1.3f), (maxH - gap * (rows - 1)) / rows);
            if (V2Layout) ch = Mathf.Min((w - gap * 3f) / 4f * (IsV4Ui && turn.Tactics != null ? 1.6f : 1.3f), (maxH - gap) / 2f); // 【第2巡 4】札の高さは4列のときと同じ（枚数で欄の高さが変わらない）
            var badge = new GUIStyle(Big) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            bool answerLit = false;
            for (int i = start; i < start + slots; i++)
            {
                int row = (i - start) / perRow, col = (i - start) % perRow;
                var rect = new Rect(x + col * (cw + gap), y + row * (ch + gap), cw, ch);
                if (i >= entries.Count)
                {
                    if (!V2Layout) DrawFrame(rect, new Color(1f, 1f, 1f, 0.08f), 1f); // 【RC8・初見テスト】空の枠は「欠けている」に見えた
                    continue;
                }
                var stack = entries[i];
                // 束の中で、選んでいる札 → 入れられる札 → 先頭 の順に代表を決める
                var card = stack.FirstOrDefault(c => c == pendingCard) ?? stack.FirstOrDefault(c => turn.CanAddCard(c)) ?? stack[0];
                bool planned = stack.All(c => turn.IsCardPlanned(c));
                int slotNo = turn.IsCardPlanned(card) ? SlotNumberOf(card) : planned ? SlotNumberOf(stack[0]) : 0;
                bool selected = stack.Contains(pendingCard);
                bool can = turn.CanAddCard(card);
                float age = Time.time - stack.Max(c => c.DrawnAt) - i * 0.05f;
                float t = Mathf.Clamp01(age / 0.3f);
                var drawRect = new Rect(rect.x, rect.y - (1f - t) * 26f, rect.width, rect.height);
                if (tidyMode) card = stack.FirstOrDefault(c => tidyPick.Contains(c)) ?? stack.FirstOrDefault(c => !turn.IsCardPlanned(c)) ?? card;
                bool picked = tidyMode && stack.Any(c => tidyPick.Contains(c));
                DrawShowCard(drawRect, card.Data, selected || picked, planned, slotNo, tidyMode ? !turn.IsCardPlanned(card) : can || selected, Mathf.Lerp(0.2f, 1f, t), group ? 0 : i + 1, card.Forge, card.CopyDelta); // 【RC2】束にした（9枚以上）ときは番号を出さない（キーは手札の順で、束の番号とずれる）
                var nxAns = QuickUi && !planned && !answerLit && !act.TutorialStepDone ? act.NextAnswerStep() : null;
                // 【文化祭・10歳の立場のレビュー「はじめは何を押せばいいか分からない」】はじめての1戦目・まだ何も組んでいない間：攻撃の札を光らせる（向きは自分で選ぶ）
                if (FestivalUi && act.FestivalLevel == 0 && turn.TurnNumber == 1 && act.Festival != null && act.Festival.Attempt == 1 && !act.FestivalDemo && turn.PlannedCommands.Count == 0 && pendingCommand == null && !planned && card.Data.type == CommandType.Slash && !act.TutorialStepDone && !act.MissionOpen)
                    DrawFrame(new Rect(drawRect.x - 3, drawRect.y - 3, drawRect.width + 6, drawRect.height + 6), new Color(1f, 0.85f, 0.3f, Blink(0.45f, 1f, 3f)), 3f);
                if (nxAns.HasValue && card.Data.name == nxAns.Value.card)
                {
                    answerLit = true; // 【RC12・初見テスト「同じ札が2枚光った」】光らせるのは1枚だけ
                    // 【RC12】答えを見た：次に選ぶ札を光らせる（自分で選んで解く）
                    float pz = Blink(0.55f, 1f, 4f);
                    DrawFrame(new Rect(drawRect.x - 3, drawRect.y - 3, drawRect.width + 6, drawRect.height + 6), new Color(1f, 0.85f, 0.3f, pz), 4f);
                    var nb = new Rect(drawRect.x + 6, drawRect.y + drawRect.height * 0.22f, drawRect.width - 12, 22); // 【RC12・初見テスト2回目「次はこれ が『2マス移動』を隠す」】絵の所へ // 【RC12・見直し】上の見出しに重ならないよう、札の中に
                    DrawRect(nb, new Color(0.1f, 0.07f, 0.02f, 0.92f));
                    ShadowLabel(nb, "次はこれ", new GUIStyle(Label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.85f, 0.3f));
                }
                if (picked) { DrawFrame(drawRect, new Color(1f, 0.45f, 0.35f), 3f); ShadowLabel(new Rect(drawRect.x, drawRect.yMax - 22, drawRect.width, 20), $"捨てる{(stack.Count > 1 ? $"×{stack.Count(c => tidyPick.Contains(c))}" : "")}", new GUIStyle(Label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.6f, 0.5f)); }
                if (stack.Count > 1)
                {
                    int left = stack.Count(c => !turn.IsCardPlanned(c));
                    var br = new Rect(drawRect.xMax - 44, drawRect.y + 4, 40, 24);
                    DrawRect(br, new Color(0.05f, 0.05f, 0.1f, 0.9f));
                    DrawFrame(br, Gold, 1.5f);
                    ShadowLabel(br, left == stack.Count ? $"×{stack.Count}" : $"残り{left}", badge, Gold);
                }
                if (rect.Contains(Event.current.mousePosition))
                {
                    string tipTitle = $"{card.Data.displayName}［{CategoryName(card.Data)}］{(stack.Count > 1 ? $"×{stack.Count}" : "")}";
                    string tipBody = $"自分：{SelfText(card.Data)}\n敵のまね：{CopyText(card.Data)}\n\n{card.Data.description}" + (string.IsNullOrEmpty(card.Data.copyNote) ? "" : $"\n敵がまねすると：{card.Data.copyNote}");
                    if (IsV4Ui) { cardTipTitle = tipTitle; cardTipBody = tipBody.Replace("\n\n", "\n"); } // 【RC7】右下の説明欄へ
                    else { hoverTitle = tipTitle; hoverBody = tipBody; hoverPos = new Vector2(rect.x, rect.yMax + 4); }
                }
                if (input && !TacticTargeting && GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    if (tidyMode)
                    {
                        // 束なら、まだ選んでいない札を1枚ずつ足す（上限なら、その束から1枚外す）
                        var add = stack.FirstOrDefault(c => !tidyPick.Contains(c) && !turn.IsCardPlanned(c));
                        if (add != null && tidyPick.Count < (turn.PendingSift ? 2 : turn.TidyMax)) tidyPick.Add(add);
                        else { var rem = stack.FirstOrDefault(c => tidyPick.Contains(c)); if (rem != null) tidyPick.Remove(rem); }
                    }
                    else if (selected) ResetPending();
                    else if (can) Select(card.Data, card);
                }
            }
            for (int j = 0; j < tn; j++)
            {
                int pos = slots + j;
                var rect = new Rect(x + (pos % perRow) * (cw + gap), y + (pos / perRow) * (ch + gap), cw, ch);
                if (j == 0 && pos % perRow > 0) DrawRect(new Rect(rect.x - gap / 2f - 1f, rect.y + 6f, 2f, rect.height - 12f), new Color(TacticGreen.r, TacticGreen.g, TacticGreen.b, 0.7f)); // 【RC6・レビュー「手札の主従が見えない」】命令札と緑の札の間に区切り
                DrawTacticHandCard(rect, tacticHand[j], j, input);
                if (DebugClickTacticIndex == j && Event.current.type == EventType.Repaint) { DebugClickTacticIndex = -1; UseTacticCard(tacticHand[j]); } // 【RC6】自動の通しだけ：描いている途中で緑の札を押す（前の不具合の再現。ふだんは -1）
            }
            return y + rows * ch + (rows - 1) * gap;
        }

        void DrawShowCard(Rect r, CommandData d, bool selected, bool planned, int slotNo, bool enabled, float alpha, int keyNumber, int forge = 0, int copyDelta = 0) =>
            DrawCardV2(r, d, selected, planned, slotNo, enabled, alpha, keyNumber, forge, copyDelta);

        // ───────── 右：実行予定（横に3つ）と実行 ─────────

        /// <summary>【通常リリース版・画面の直し 19】入力中に組んだ命令の写し（実行中、命令の欄を「空き」に戻さないため）。</summary>
        readonly List<CommandRecord> planCache = new List<CommandRecord>();
        readonly List<bool> planCacheCopied = new List<bool>();

        float DrawShowPlan(float x, float y, float w)
        {
            var head = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            bool blank = act != null && act.PendingItem == PendingItemUse.BlankStone;
            // 【検証中】2026-10-05 監査 F6：アイテムを使えなかった理由（「HP は満タン」等）を 4 秒、見出しの代わりに出す（手札の上に重ねない：レビュー2）
            string msg = null;
            if (act != null && act.InBattle && act.Message != null)
            {
                // 同じ文でも、出し直すたびに 4 秒（レビュー：2回目の「満タン」が出なかった）
                if (act.MessageSerial != shownMessageSerial) { shownMessageSerial = act.MessageSerial; shownMessageAt = Time.unscaledTime; }
                if (Time.unscaledTime - shownMessageAt < 4f) msg = act.Message;
            }
            // 【RC4・理解度テスト「1ターンの順番が画面から分からない」】V4 の見出しは、ターンの順番そのもの
            bool clocksHere = turn.Grid.Model != null && turn.Grid.Model.Enemies.Any(e => e.IsAlive && e.Clocks.Count > 0);
            bool actorsHere = turn.Grid.Model != null && turn.Grid.Model.Enemies.Any(e => e.IsAlive && !e.IsObject && PhaseRules.HasTrait(e)); // 敵のいない・攻撃しない練習の部屋は、ふつうの見出し
            string order = IsV4Ui && PhaseRules.IntentOrder && actorsHere && !(turn.Grid.Model != null && turn.Grid.Model.NoTraits)
                ? (clocksHere ? $"命令（{turn.MaxSlots}つまで）→ 敵の攻撃（赤・紫）→ まね → ★大技（紫）→ 敵が動く（オレンジ）{(turn.Grid.Model.Environment.HasFire ? "→ 床" : "")}" : $"命令（{turn.MaxSlots}つまで）→ 敵の攻撃（赤）→ まね → 敵が動く（オレンジの矢印）{(turn.Grid.Model.Environment.HasFire ? "→ 床" : "")}") // 【RC6・ブラインドテスト】床の危険も順番に
                : $"実行予定の命令（{turn.MaxSlots}つまで・左から順に実行 → 敵が同じ命令をまねる）";
            if (V2Layout) order = $"命令（左から順に・{turn.MaxSlots}つまで）"; // 【通常リリース版・画面の直し 2】短く（順番は盤面の上の帯）。【RC10・初見テスト2回目「命令の欄の見出しが画面によって違う」】どの部屋も同じ
            string headText = blank ? "空白の石：まねさせない命令をクリック（Esc でやめる）" : WarpPicking ? "転移石：光るマスへ（クリックか 矢印＋Enter）。数字＝移って何もしないと受ける分・Esc でやめる" : msg ?? order;
            if (IsCopySandbox && msg == null && !blank)
                headText = CopyCore.IsImmediate(turn.Grid.Model.CopyMode) ? $"実行予定の命令（{turn.MaxSlots}つまで・1つごとに あなた → 敵A → 敵B …）" : $"実行予定の命令（{turn.MaxSlots}つまで・あなたが全部 → 敵A が全部 → 敵B …）";
            // 【完成版】初見の読み落とし対策：組んだ命令で「あなたが最後に立つマス」が、赤（敵の攻撃）・赤紫（大技）の上なら、見出しの代わりに赤で知らせる。
            // 【RC3・本人「何で食らったのか分からない」】予告どおりの順番になったので、このターンにあなたが受けるダメージは全部決まる：
            //   敵のまねがあなたに当たる（自分の攻撃のまね）も含めて、原因と量を出す（敵どうしの結果までは出さない）
            bool futureOk = !(CombatV2 && !AnswerPreview) && !(CombatV2 && previewRecordLast != null && pendingCommand != null); // 【RC9】本編は未来の合計（このターン あなたに Nダメージ）を出さない。チュートリアルで向きを試している間も出さない（帯の「この向きなら 無傷」と食い違って見えた）
            string danger = futureOk && !blank && msg == null && IsV4Ui && turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count > 0 && turn.Grid != null && turn.Grid.Model != null
                ? EndCellDanger(turn.Grid.Model, turn.PlannedCommands) : null;
            // 【RC4・本人「何で食らったのか分からない」をゼロに】前のターンに受けたダメージの原因（命令を組み始めるまで）
            UpdateLastTurnDamage();
            // 【RC5・レビュー】命令を組む前でも、今いるマスが危ないなら（前のターンの一言が無いとき）赤い「！」を出す（前は1枚組むまで出なかった）
            if (futureOk && danger == null && !blank && msg == null && !WarpPicking && IsV4Ui && turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count == 0 && turn.Grid != null && turn.Grid.Model != null)
            {
                var idle = EndCellDanger(turn.Grid.Model, new List<CommandRecord>());
                if (idle != null) danger = idle + "（何もしなければ）" + (lastTurnDamage != null ? "　" + lastTurnDamage : ""); // 【RC5・レビュー3】受けた直後でも危ない一言を先に（前は前のターンの一言に置き換わって消えた）
            }
            if (danger != null) headText = danger;
            bool showLast = danger == null && msg == null && !blank && !WarpPicking && IsV4Ui && !V2Layout && turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count == 0 && lastTurnDamage != null; // 【通常リリース版・画面の直し 2】本編の画面は右下の「前のターンに起きたこと」にまとめる（見出しに同じ文を出さない）
            if (showLast) headText = lastTurnDamage;
            ShadowLabel(new Rect(x, y, w, 20), headText, Fit(head, headText, w, 11), blank ? new Color(0.7f, 0.9f, 1f) : msg != null ? new Color(1f, 0.7f, 0.55f) : danger != null ? new Color(1f, 0.45f, 0.45f) : showLast ? new Color(1f, 0.66f, 0.66f) : Gold); // 【RC5・レビュー3】前のターンのダメージは赤み（オレンジは「敵が動く先」の色）
            if (blank && ThemedButton(new Rect(x + w - 90, y - 1, 90, 20), "やめる", false, false, 11)) act.CancelItem();
            y += 22;
            float bwid = 124;
            float area = w - bwid - 8;
            float arrowW = 14;
            int shown = Mathf.Clamp(Mathf.Max(3, turn.MaxSlots), 3, 8);
            float cw = (area - arrowW * (shown - 1)) / shown;
            float ch = 50;
            bool executing = turn.State == BattleState.PlayerExecuting;
            // 【通常リリース版・画面の直し 19「実行中に命令の欄が『空き』に戻り、何を命令したか見えない」】実行中（敵の番まで）は、組んだ命令を残す。
            //   あなたの命令の間は今の命令が金、③まねの間は今まねている命令が水色。勝ち負けの窓のときは残さない（【RC11】勝った後に「斬撃↑」が残っていた）
            bool running = turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting;
            if (turn.State == BattleState.PlayerInput) { planCache.Clear(); planCache.AddRange(turn.PlannedCommands); planCacheCopied.Clear(); for (int k = 0; k < planCache.Count; k++) planCacheCopied.Add(turn.IsCopied(k)); }
            bool fromCache = running && turn.PlannedCommands.Count == 0 && planCache.Count > 0;
            var plan = fromCache ? (IReadOnlyList<CommandRecord>)planCache : turn.PlannedCommands;
            bool IsCopiedAt(int k) => fromCache ? k < planCacheCopied.Count && planCacheCopied[k] : turn.IsCopied(k);
            int copyActive = -1;
            if (fromCache && turn.Step == TurnManager.TurnStep.Copy && turn.CurrentCopy != null && turn.CurrentCopy.Command != null)
                for (int k = 0; k < planCache.Count && copyActive < 0; k++)
                    if (planCache[k].Command == turn.CurrentCopy.Command && planCache[k].Direction == turn.CurrentCopy.Direction) copyActive = k;
            for (int i = 0; i < shown; i++)
            {
                var r = new Rect(x + i * (cw + arrowW), y, cw, ch);
                bool locked = i >= turn.MaxSlots;
                bool has = i < plan.Count;
                bool active = executing && turn.ExecutingIndex == i;
                if (i > 0) GUI.Label(new Rect(r.x - arrowW, r.y, arrowW, ch), "›", new GUIStyle(Big) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } });
                if (locked) { DrawUi(r, "panel", PanelBlue, 0.35f); GUI.Label(r, "（使えない）", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter }); continue; }
                if (!has)
                {
                    DrawUi(r, "panel", PanelBlue, 0.55f);
                    // 【完成版】使わない枠＝次のターンに戦術札 +1（「今は少なく出して、次の準備」を選べる）
                    bool slotBonus = TurnRules.UnusedSlotTacticDraw > 0 && turn.Tactics != null && IsV4Ui;
                    // 【RC5・レビュー】緑の手札は3枚まで：満杯なら引けない（前は満杯でも「+1」と出ていた＝表示どおりにならない）。空いた枠の順に、手札の空きの数だけ
                    int room = slotBonus ? Mathf.Max(0, turn.Tactics.HandMax - turn.Tactics.Hand.Count) : 0;
                    int emptyIndex = i - turn.PlannedCommands.Count;
                    string bonusLine = !slotBonus ? null : emptyIndex < room ? $"<color=#7fe0a0>使わなければ 戦術札 +{TurnRules.UnusedSlotTacticDraw}</color>" : $"<color=#7fe0a0aa>（緑の手札は{turn.Tactics.HandMax}枚で満杯）</color>";
                    GUI.Label(r, bonusLine != null ? $"{i + 1}　空き\n{bonusLine}" : $"{i + 1}　空き", new GUIStyle(Small) { fontSize = 13, richText = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } });
                    continue;
                }
                var rec = plan[i];
                var d = rec.Command;
                var col = CardColor(d);
                DrawUi(r, "panel", PanelBlue);
                DrawRect(new Rect(r.x + 3, r.y + 3, 4, r.height - 6), col);
                float since = Time.time - slotAddedAt[i];
                if (since < 0.4f && !fromCache) DrawRect(r, new Color(1f, 1f, 0.5f, 0.45f * (1f - since / 0.4f)));
                if (active) DrawFrame(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Gold, 3f);
                if (copyActive == i) DrawFrame(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0.4f, 0.95f, 1f, Blink(0.6f, 1f, 6f)), 3f); // 【画面の直し 19】今まねている命令（水色＝まね）
                if (!IsCopiedAt(i))
                {
                    // コピーされない命令（忍び足・空白の石）：薄い水色のふちと札
                    DrawFrame(r, new Color(0.5f, 0.85f, 1f, 0.9f), 2f);
                    GUI.Label(new Rect(r.xMax - 70, r.yMax - 16, 68, 14), "まねされない", new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.LowerRight, normal = { textColor = new Color(0.6f, 0.9f, 1f) } });
                }
                if (blank && !fromCache && turn.IsCopied(i) && GUI.Button(r, GUIContent.none, GUIStyle.none)) act.ChooseBlankSlot(i);
                var b = new Rect(r.x + 8, r.y + 4, 20, 20);
                DrawRect(b, new Color(col.r, col.g, col.b, 0.9f));
                GUI.Label(b, (i + 1).ToString(), new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
                var thumb = new Rect(r.x + 8, r.y + 26, 36, 20);
                DrawUiImage(thumb, "cardart_" + ArtKey(d));
                var name = new GUIStyle(Label) { fontSize = cw < 120 ? 12 : 14, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                ShadowLabel(new Rect(r.x + 32, r.y + 3, r.width - 36, 22), $"{(UiStyles.KidNames != null ? UiStyles.KidNames(d) : d.displayName)} {(d.UsesDirection ? (d.type == CommandType.Diagonal ? DiagGlyph(rec.Direction) : DirGlyph(rec.Direction)) : "")}{(d.NeedsDistanceChoice ? rec.Distance.ToString() : "")}", name, Color.white);
                string slotLine = ShortText(d);
                if (ShowCopyKinds && IsCopiedAt(i))
                {
                    // 【Copy Core V3】この命令を写す敵（全写し・技写し・歩写しで違う）。結果は出さない
                    var who = turn.Enemies.Where(en => en.IsAlive && turn.Grid.Model.CopyEnabled && CopyKinds.Accepts(en.Model, d) && !CopyCore.Stunned(en.Model)).Select(en => IsV4Ui ? V4Name(en.Model) + DupLabel(en.Model) : en.Model.Label.Length > 0 ? en.Model.Label : "敵").ToList(); // 【RC2】名札と同じ呼び名（前は A・B だけで、どの敵か分からなかった）
                    if (V2Layout) who = turn.Enemies.Where(en => en.IsAlive && turn.Grid.Model.CopyEnabled && CopyKinds.Accepts(en.Model, d) && !CopyCore.Stunned(en.Model)).Select(en => en.Model.KindLetter).ToList(); // 【RC8・初見テスト3】名前が切れていた → 記号（上の欄と同じ A・B・C）
                    slotLine = d.IsNoBoardEffect ? "まねる：なし（手札の札）" : who.Count > 0 ? "まねる：" + string.Join(V2Layout ? " " : "・", who) : "まねる：なし"; // 【RC11・QA】「まねる：A B」と同じ言い方
                    if (i < turn.LockedCommands && !fromCache) slotLine += "　（固定）";
                }
                GUI.Label(new Rect(r.x + 48, r.y + 26, r.width - 52, 20), slotLine, new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.85f, 0.87f, 0.95f) } });
            }
            // 実行（金）と 1つ戻す・全部消す
            var exec = new Rect(x + w - bwid, y - 2, bwid, 36);
            if (FestivalUi && act.FestivalLevel == 0 && turn.PlannedCommands.Count > 0 && pendingCommand == null && turn.TurnNumber == 1 && act.TutorialStep == 0) DrawFrame(new Rect(exec.x - 4, exec.y - 4, exec.width + 8, exec.height + 8), new Color(1f, 0.85f, 0.3f, Blink(0.45f, 1f, 3f)), 3f); // 【文化祭】はじめての1戦目：組んだら「実行」を光らせる
            if (ThemedButton(exec, "実行 ▶", false, true, 18, turn.CanExecute && pendingCommand == null && !blank && !TacticTargeting && !turn.PendingSift && !WarpPicking)) { ResetPending(); turn.ExecuteTurn(); }
            bool canUndo = turn.State == BattleState.PlayerInput && turn.PlannedCommands.Count > turn.LockedCommands; // 【Puzzle v2】引く札の前は戻せない
            if (ThemedButton(new Rect(x + w - bwid, y + 37, bwid / 2 - 2, 19), "1つ戻す", false, false, 11, canUndo)) { turn.RemoveLastCommand(); ResetPending(); }
            if (ThemedButton(new Rect(x + w - bwid / 2 + 2, y + 37, bwid / 2 - 2, 19), "全部消す", false, false, 11, canUndo)) { turn.ClearCommands(); ResetPending(); }
            return y + ch + 8;
        }

        // ───── 【RC4】方向を選ぶ入力（画面の中で一番先） ─────
        readonly Dictionary<Direction, Rect> dirAreas = new Dictionary<Direction, Rect>();
        /// <summary>その向きの範囲（移動の道・着地・攻撃のマス）。クリックすればその向き（行きたい所を押せばよい）。</summary>
        readonly List<(Direction dir, Rect rect)> dirExtraAreas = new List<(Direction, Rect)>();

        bool DirectionAt(Vector2 mouse, out Direction dir) => InputPriority.PickDirection(dirAreas, dirExtraAreas, mouse, out dir);

        /// <summary>方向を選んでいる最中か（カードを選んで、まだ向きを決めていない）。</summary>
        bool DirectionMode => pendingCommand != null && !hasPendingDirection && turn != null && turn.State == BattleState.PlayerInput && !TacticTargeting && turn.Grid != null && turn.Grid.Model != null && !WarpPicking;

        /// <summary>【RC4】転移石の行き先を選んでいる。</summary>
        bool WarpPicking => act != null && act.InBattle && act.PendingItem == PendingItemUse.WarpStone;

        /// <summary>4つの向きの当たりの範囲：あなたの予定の位置の隣のマスまるごと（盤面の外でも同じ大きさ）。矢印はその縁に描く。</summary>
        void ComputeDirectionAreas(float scale)
        {
            var cam = Camera.main;
            var grid = turn.Grid;
            if (cam == null) return;
            Vector2 ToGui(Vector3 world) { var s = cam.WorldToScreenPoint(world); return new Vector2(s.x / scale, (Screen.height - UiStyles.LetterboxY - s.y) / scale); }
            var planPos = PlayerPreview.PlannedPosition(grid.Model, turn.PlannedCommands, turn.PlayerAttackBonusEachTurn);
            var pp = grid.GridToWorld(planPos);
            bool diag = pendingCommand.type == CommandType.Diagonal;
            foreach (Direction d in new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right })
            {
                var off = diag ? d.ToDiagonalOffset() : d.ToOffset();
                // 【RC4・レビュー】盤面の外の向きは、マスまるごとの当たりを作らない（上の案内帯・下のヒント帯に重なっていた）。矢印のボタンでは選べる（壁へ向けて空振り＝敵だけ動かす手）
                if (!grid.Model.Board.IsInside(planPos + new Vector2Int(off.x, off.y))) continue;
                var c = pp + new Vector3(off.x, off.y, 0f);
                var a = ToGui(c + new Vector3(-0.5f, 0.5f, 0f));
                var b = ToGui(c + new Vector3(0.5f, -0.5f, 0f));
                dirAreas[d] = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            Rect CellR(Vector2Int c)
            {
                var w0 = grid.GridToWorld(c);
                var a = ToGui(w0 + new Vector3(-0.5f, 0.5f, 0f));
                var b = ToGui(w0 + new Vector3(0.5f, -0.5f, 0f));
                return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            }
            foreach (var o in PlayerPreview.Compute(grid.Model, turn.PlannedCommands, pendingCommand, turn.PlayerAttackBonusEachTurn))
            {
                if (!o.Direction.HasValue) continue;
                foreach (var c in o.Path.Concat(o.AttackCells).Concat(o.ControlCells).Append(o.Landing).Distinct())
                    if (c != planPos) dirExtraAreas.Add((o.Direction.Value, CellR(c)));
            }
        }

        /// <summary>左クリック＝その向き（隣のマスか矢印）・右クリック＝取り消し。ほかの UI より先に受けて、イベントを使い切る。</summary>
        void HandleDirectionPointer()
        {
            var ev = Event.current;
            if (ev.type != EventType.MouseDown) return;
            if (InputPriority.IsCancel(ev.type, ev.button, KeyCode.None)) { ResetPending(); Sfx.Play(Sfx.Kind.Button); ev.Use(); return; }
            if (ev.button != 0) return;
            if (DirectionAt(ev.mousePosition, out var d)) { ChooseDirection(d); ev.Use(); }
        }

        int dmgSeenTurn = -1, dmgSeenCount;
        string lastTurnDamage;

        /// <summary>【RC4】前のターンにあなたが受けたダメージを原因ごとに1行（「前のターン あなた -7：剣兵Aのまね（斬撃） -4・弓兵の攻撃（赤） -3」）。</summary>
        void UpdateLastTurnDamage()
        {
            if (turn == null) return;
            var ev = turn.BattleEvents;
            if (ev.Count < dmgSeenCount || turn.TurnNumber < dmgSeenTurn) { dmgSeenCount = 0; dmgSeenTurn = -1; lastTurnDamage = null; } // 新しい戦闘
            if (turn.State != BattleState.PlayerInput || turn.TurnNumber == dmgSeenTurn) return;
            var hits = new List<BattleEvent>();
            for (int i = dmgSeenCount; i < ev.Count; i++)
                if (ev[i].Type == BattleEventType.Damaged && ev[i].Unit != null && ev[i].Unit.IsPlayer && ev[i].Amount > 0) hits.Add(ev[i]);
            dmgSeenTurn = turn.TurnNumber;
            dmgSeenCount = ev.Count;
            if (hits.Count == 0 || dmgSeenTurn <= 1) { lastTurnDamage = null; return; }
            var groups = hits.GroupBy(TurnForecast.CauseText).Select(g => $"{g.Key} {g.Sum(e => e.Amount)}").ToList();
            lastTurnDamage = $"前のターン あなたは {hits.Sum(e => e.Amount)}ダメージ：" + string.Join("・", groups);
        }

        /// <summary>【RC5】あなたの札が届く範囲の色（白）。色の意味：水色＝まね／赤＝敵の攻撃／赤紫＝大技／オレンジ＝敵が自分で動く先／緑＝戦術札／白・金＝あなたの選択。</summary>
        internal static readonly Color V4PlayerRangeColor = new Color(0.92f, 0.95f, 1f);
        /// <summary>【RC5・レビュー】完成版の敵の名札・HP の帯の色（砂色）。前は兵種ごとの色（槍兵＝橙・弓兵／癒し手＝緑・写し身＝赤紫…）で、盤面の意味の色と重なっていた。見分けは名前と A・B で。</summary>
        internal static readonly Color V4EnemyUiColor = new Color(0.86f, 0.78f, 0.62f);

        static string forecastKey2;
        static TurnForecast forecastCache;
        static readonly List<(string key, TurnForecast f)> forecastLru = new List<(string, TurnForecast)>();
        /// <summary>【RC4・レビュー】戦術札・アイテムを使った回数（使うたびに予告を作り直す）。</summary>
        static int ForecastSerial;
        /// <summary>【RC4・レビュー】空白の石でまねさせない命令（予告にも入れる）。DrawShowcase が毎フレーム入れる。</summary>
        static IReadOnlyCollection<int> forecastSkip;

        /// <summary>
        /// 【RC3・RC4】組んだ命令でこのターンがどうなるか（RC3 の順番は全部決まっている）。盤面と命令が同じなら前の答え（毎フレーム試さない）。
        /// 画面に出すのは、あなたが受けるダメージ（命令の欄の「！」）と、敵が自分で動く先（盤面のオレンジの矢印と影）だけ。
        /// </summary>
        static TurnForecast ForecastFor(BattleModel model, IReadOnlyList<CommandRecord> plan)
        {
            var key = new System.Text.StringBuilder();
            key.Append(model.Environment.Round).Append('|').Append(model.PlayerGuard).Append('|').Append(model.CopyMoveBonus).Append('|').Append(model.NoTraits).Append('|');
            foreach (var r in plan) key.Append(r.Command != null ? r.Command.name : "-").Append(r.Direction).Append(r.Distance).Append(r.SourceCard != null ? $"f{r.SourceCard.Forge}c{r.SourceCard.CopyDelta}" : $"c{r.CopyDelta}").Append(','); // 【RC7・コードレビュー】強化の違いも鍵に
            foreach (var u in model.Units) key.Append(u.Id).Append(':').Append(u.Position.x).Append(',').Append(u.Position.y).Append(',').Append(u.Hp).Append(u.Facing).Append(u.AttackBonus).Append(u.StunRounds).Append(u.Silenced ? 'S' : '-').Append(';');
            foreach (var e in model.Enemies) foreach (var c in e.Clocks) key.Append(c.Cd).Append(c.Telegraphed ? 'T' : 'f');
            if (forecastSkip != null) foreach (var i in forecastSkip) key.Append('s').Append(i);
            key.Append('|').Append(ForecastSerial); // 【RC4・レビュー】照準替え・模倣命令などを使っても古い予告が残っていた
            foreach (var e in model.Enemies) { key.Append(e.ForceAllCopy ? 'F' : '-'); foreach (var c in e.Clocks) foreach (var cell in c.Cells) key.Append(cell.x).Append(cell.y); }
            string k = key.ToString();
            if (k == forecastKey2 && forecastCache != null) return forecastCache;
            // 【RC7】組んだ命令・選んでいる向き・何もしない を同じフレームで聞くので、4つまで覚える（1つだと毎回試し直す）
            for (int i = 0; i < forecastLru.Count; i++)
                if (forecastLru[i].key == k) { var hit = forecastLru[i]; forecastLru.RemoveAt(i); forecastLru.Insert(0, hit); forecastKey2 = k; forecastCache = hit.f; return hit.f; }
            forecastKey2 = k;
            forecastCache = TurnForecast.Compute(model, plan, forecastSkip != null && forecastSkip.Count > 0 ? new HashSet<int>(forecastSkip) : null);
            forecastLru.Insert(0, (k, forecastCache));
            if (forecastLru.Count > 4) forecastLru.RemoveAt(forecastLru.Count - 1);
            return forecastCache;
        }

        /// <summary>【RC7】選んでいる向きの命令（置く前の試し）。無ければ null。</summary>
        CommandRecord previewRecord, previewRecordLast;
        Vector2Int previewLanding;

        /// <summary>
        /// 【RC7・本人の RC6 実プレイ：砲王の大技で HP 18 → 0】このターンにあなたが受けるダメージを、盤面のあなた（命令の後に立つマス）に出す。
        /// 前は命令の欄の上の1行（赤い文字）だけで、目は盤面にあった。計算は前と同じ（ターンを丸ごと試す・RC3 から）：あなたが受ける分だけを見せ、敵どうしの結果は見せない。
        /// 札を選んで向きにマウスを乗せると、その命令を置いたときの分（置く前に分かる）。まねで自分に返る攻撃は、まねた敵から点の線と「まね返し」。
        /// 何もしなければ受けるのに、組んだ命令なら受けないときは緑の「無傷」。
        /// </summary>
        void DrawSelfDamageBadge(System.Func<Vector2Int, Rect> CellRect, Vector2Int planPos)
        {
            var model = turn.Grid != null ? turn.Grid.Model : null;
            if (model == null || !model.V4 || !PhaseRules.IntentOrder || model.NoTraits || turn.State != BattleState.PlayerInput || WarpPicking) return;
            if (CombatV2 && !AnswerPreview) return; // 【RC9】本編は「このターンあなたが受ける合計」「無傷」「まね返し」を出さない（チュートリアルだけ）
            bool preview = previewRecord != null;
            var plan = turn.PlannedCommands.ToList();
            if (preview) plan.Add(previewRecord);
            var f = ForecastFor(model, plan);
            if (f.Failed) return;
            var r = CellRect(preview ? previewLanding : planPos);
            int total = f.PlayerHits.Sum(e => e.Amount);
            var st = new GUIStyle(Big) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            if (total <= 0)
            {
                if (plan.Count == 0) return;
                var idle = ForecastFor(model, new List<CommandRecord>());
                if (idle.Failed || idle.PlayerHits.Sum(e => e.Amount) <= 0) return;
                var gr = CombatV2 ? new Rect(r.xMax - 48f, r.y - 4f, 52f, 18f) : new Rect(r.center.x - 26f, r.yMax - 19f, 52f, 18f); // 【RC8】右上
                DrawRect(gr, new Color(0.03f, 0.18f, 0.08f, 0.92f));
                DrawFrame(gr, new Color(0.45f, 1f, 0.55f), 1.5f);
                ShadowLabel(gr, "無傷", new GUIStyle(st) { fontSize = 12 }, new Color(0.6f, 1f, 0.65f));
                return;
            }
            // まねで自分に返る攻撃：まねた敵（ターンの初めの位置）から、あなたへ点の線
            var back = new Color(1f, 0.45f, 0.85f);
            foreach (var g in f.PlayerHits.Where(e => e.Cause != null && e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer).GroupBy(e => e.Cause.Source.Id))
            {
                var src = model.Enemies.FirstOrDefault(u => u.Id == g.Key && u.IsAlive);
                if (src == null) continue;
                var sr = CellRect(src.Position);
                for (int k = 1; k < 10; k++)
                {
                    var q = Vector2.Lerp(sr.center, r.center, k / 10f);
                    DrawRect(new Rect(q.x - 2.5f, q.y - 2.5f, 5f, 5f), new Color(back.r, back.g, back.b, 0.95f));
                }
                var away = (sr.center - r.center).normalized * sr.width * 0.35f; // 【RC7・ブラインドテスト】あなたと反対側へ（あなたのマスの矢印を隠していた）
                var tr = new Rect(sr.center.x + away.x - 34f, sr.center.y + away.y - 9f, 68f, 18f);
                DrawRect(tr, new Color(0.22f, 0.03f, 0.18f, 0.92f));
                DrawFrame(tr, back, 1.5f);
                ShadowLabel(tr, $"まね返し {g.Sum(e => e.Amount)}", new GUIStyle(st) { fontSize = 11 }, new Color(1f, 0.8f, 0.95f));
            }
            // あなたのマス：脈打つ赤い枠と「！ 6ダメ」（倒れるなら「倒れる」）
            float a = Blink(0.55f, 1f, 6f);
            if (CombatV2)
            {
                // 【RC8】右上の札（危険の数字と同じ場所・同じ読み方）。あなたの水色の枠に赤い枠を重ねない
                string t2 = f.PlayerDies ? "！倒れる" : $"！{total}ダメ";
                var s2 = new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                float w2 = s2.CalcSize(new GUIContent(t2)).x + 10f;
                var cr2 = new Rect(r.xMax - w2 - 2f, r.y + 2f, w2, 21f); // 【RC8・初見テスト2「！2ダメが隣のマスの札に見えた」】マスの中の右上
                DrawFrame(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), new Color(1f, 0.25f, 0.2f, a), 2.5f); // あなたのマスも赤く（赤い十字の真ん中で安全に見えた）
                DrawRect(cr2, new Color(0.55f, 0.03f, 0.03f, 0.97f));
                DrawFrame(cr2, new Color(1f, 0.6f, 0.5f, a), 2f);
                ShadowLabel(cr2, t2, s2, Color.white);
                return;
            }
            DrawFrame(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(1f, 0.25f, 0.2f, a), 3f);
            if (pendingCommand != null && !hasPendingDirection)
            {
                // 向きを選んでいる間：矢印がマスの縁にあるので、左上の角に小さく（数字は案内の帯にも出る）
                var cr = new Rect(r.x + 1f, r.y + 1f, 30f, 18f);
                DrawRect(cr, new Color(0.45f, 0.02f, 0.02f, 0.95f));
                ShadowLabel(cr, f.PlayerDies ? "！" : $"！{total}", new GUIStyle(st) { fontSize = 12 }, Color.white);
                return;
            }
            string text = f.PlayerDies ? "！ 倒れる" : $"！ {total}ダメ"; // ⚠ は字体に無い（□になった）
            float w = st.CalcSize(new GUIContent(text)).x + 14f;
            bool planned = !preview && turn.PlannedCommands.Count > 0 && planPos != model.Player.Position; // 「予定」の文字（マスの下の端）の上へ
            var pr = new Rect(r.center.x - w / 2f, r.yMax - (planned ? 38f : 20f), w, 20f);
            DrawRect(pr, new Color(0.45f, 0.02f, 0.02f, 0.95f));
            DrawFrame(pr, new Color(1f, 0.55f, 0.45f, a), 1.5f);
            ShadowLabel(pr, text, st, Color.white);
        }

        /// <summary>【RC7】向きにマウスを乗せている命令を置いたとき、このターンにあなたが受けるダメージ（案内の帯に出す）。</summary>
        (bool known, int total, int back, bool dies, bool idleHurt) SelfDamagePreview()
        {
            var model = turn.Grid != null ? turn.Grid.Model : null;
            if (model == null || !model.V4 || !PhaseRules.IntentOrder || model.NoTraits || previewRecord == null || turn.State != BattleState.PlayerInput) return (false, 0, 0, false, false);
            if (CombatV2 && !AnswerPreview) return (false, 0, 0, false, false); // 【RC9】本編は「この向きだと あなた Nダメ／無傷」を出さない
            var plan = turn.PlannedCommands.ToList();
            plan.Add(previewRecord);
            var f = ForecastFor(model, plan);
            if (f.Failed) return (false, 0, 0, false, false);
            var idle = ForecastFor(model, turn.PlannedCommands);
            int back = f.PlayerHits.Where(e => e.Cause != null && e.Cause.ByCopy).Sum(e => e.Amount);
            return (true, f.PlayerHits.Sum(e => e.Amount), back, f.PlayerDies, !idle.Failed && idle.PlayerHits.Sum(e => e.Amount) > 0);
        }

        /// <summary>【RC3】組んだ命令で、このターンにあなたが受けるダメージ（原因ごと）を一言で。受けなければ null。</summary>
        static string EndCellDanger(BattleModel model, IReadOnlyList<CommandRecord> plan)
        {
            if (!model.V4 || !PhaseRules.IntentOrder) return EndCellDangerOld(model, plan);
            var f = ForecastFor(model, plan);
            return f.Failed ? EndCellDangerOld(model, plan) : f.DangerText();
        }

        /// <summary>【完成版】組んだ命令のあと、あなたが立つマスが危険なら一言（危険でなければ null）。あなたの命令だけを試す（敵のまねは試さない）。RC3 の順番でない研究用の部屋だけ。</summary>
        static string EndCellDangerOld(BattleModel model, IReadOnlyList<CommandRecord> plan)
        {
            var sim = model.Clone();
            foreach (var r in plan)
            {
                RulesEngine.Execute(sim, new CommandContext(sim.Player, r, false), new List<BattleEvent>());
                if (!sim.Player.IsAlive) return "！ この命令で、あなたが倒れる";
            }
            var p = sim.Player.Position;
            if (ThreatMap.Telegraph(model).Contains(p)) return "！ 最後に立つマスは紫（大技が来る）";
            if (ThreatMap.EnemyAttack(model).Contains(p) || ThreatMap.Bombs(model).Contains(p)) return "！ 最後に立つマスは赤（敵の攻撃が来る）";
            if (model.Environment.FireCellsNow.Contains(p)) return "！ 最後に立つマスは床の危険（このターンの最後）";
            return null;
        }

        // ───────── 右：敵の予定 ─────────

        float DrawShowEnemyPlan(float x, float y, float w)
        {
            var enemies = turn.Enemies;
            var head = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            string order = enemies.Count > 1 ? $"（{string.Join("→", enemies.Select(e => e.Model.Label))} の順）" : "";
            ShadowLabel(new Rect(x, y, w * 0.5f, 20), "敵の予定" + order, head, new Color(1f, 0.6f, 0.55f));
            var toggle = new Rect(x + w - 236, y - 2, 236, 22);
            if (act != null && act.InBattle)
                // 【検証中】ACT1 では検証用の切替を出さない（どの役職でも完全予測が見えてしまうため）。観測士は役職の効果として表示だけ
                GUI.Label(toggle, turn.ActFullPrediction ? "予告：完全予測（観測眼）" : "赤＝敵の今の攻撃範囲（動く前）", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleRight, wordWrap = false, normal = { textColor = new Color(0.85f, 0.75f, 0.75f) } });
            else if (ThemedButton(toggle, Predicting ? "予告：完全予測（検証用）　切替" : "予告：危険範囲（標準）　切替", Predicting, false, 11)) { FullPrediction = !FullPrediction; forecastKey = null; }
            y += 24;
            if (Predicting && predictionText != null && turn.State == BattleState.PlayerInput)
            {
                GUI.Label(new Rect(x, y - 2, w, 18), predictionText, new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = predictionDanger ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 1f, 0.6f) } });
                y += 18;
            }
            float rowH = enemies.Count <= 2 ? 46 : 38;
            bool enemyTurn = turn.State == BattleState.EnemyExecuting;
            foreach (var e in enemies)
            {
                var r = new Rect(x, y, w, rowH - 4);
                // 【検証中】監査 UI-09：敵の行にマウスを乗せると、兵種・コピーの種類・行動の説明
                if (e.IsAlive && r.Contains(Event.current.mousePosition))
                {
                    hoverTitle = $"{e.DisplayName} {PhaseRules.KindName(e.Model.Kind)}　HP {e.Hp}/{e.MaxHp}";
                    hoverBody = EnemyHelpNow(e.Model);
                    hoverPos = new Vector2(r.x, r.y - 120);
                }
                bool acting = enemyTurn && turn.ActingEnemy == e;
                bool danger = turn.State == BattleState.PlayerInput && enemyHitsPlayer.ContainsKey(e.Model.Id) && Predicting;
                DrawUi(r, acting || danger ? "panel_red" : "panel", PanelBlue, e.IsAlive ? 1f : 0.45f);
                if (acting) DrawFrame(r, Gold, 2f);
                var pr = new Rect(r.x + 4, r.y + 4, r.height - 8, r.height - 8);
                if (!DrawUiImage(pr, "portrait_" + KindKey(e.Model), e.IsAlive ? 1f : 0.3f)) DrawRect(pr, e.BaseColor);
                float tx = pr.xMax + 6;
                var name = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                string tag = UnitTag(e.Model);
                ShadowLabel(new Rect(tx, r.y + 2, 120, 18), $"{e.DisplayName}{(e.Model.Kind != EnemyKind.Normal ? " " + PhaseRules.KindName(e.Model.Kind) : "")}{KindSuffix(e.Model)}", name, Color.Lerp(e.BaseColor, Color.white, 0.5f));
                GUI.Label(new Rect(tx, r.y + 18, 120, 16), !e.IsAlive ? "撃破" : e.Model.IsObject ? "倒れない（道具）" : e.Model.Kind == EnemyKind.HealClone && !(turn.Grid.Model.SoftRules && !e.Model.Dormant) ? "傷つかない" : $"HP {e.Hp}/{e.MaxHp}", new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip });
                if (!e.IsAlive) { y += rowH; continue; }
                // 兵種の予告（バッジ＋文）
                float ix = tx + 122;
                var badgeRect = new Rect(ix, r.y + (r.height - 24) / 2, 24, 24);
                DrawUiImage(badgeRect, "kind_" + KindKey(e.Model), 1f, ScaleMode.ScaleToFit);
                string intent = traitForecast.TryGetValue(e.Model.Id, out var tr) ? tr : TraitIntent(e.Model, turn.Grid.Model)
                    ?? (e.Model.CopyTrait == CopyTrait.Mirror ? "鏡：向きを逆にまね（あなたが→なら←）" : "まねるだけ（固有の行動なし）");
                // 2026-10-05 監査 UI-02：反転は 10px の HP の行ではなく、予定の1行目（12px）に
                if (e.Model.CopyTrait == CopyTrait.MirrorAttacks) intent = "反射：攻撃だけ逆向き　" + intent;
                else if (e.Model.CopyTrait == CopyTrait.Mirror && !intent.StartsWith("鏡")) intent = "鏡（逆向きまね）　" + intent;
                var it = new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
                float patW = r.height - 8;
                float iw = w - (ix - x) - 30 - patW - 8;
                GUI.Label(new Rect(ix + 28, r.y + 2, iw, 18), intent, it);
                // コピーする命令（短く）
                var cmds = turn.GetPreviewFor(e);
                var parts = new List<string>();
                for (int i = 0; i < turn.MaxSlots; i++)
                {
                    string body = i < cmds.Count ? ShortCommand(cmds[i]) : "—";
                    parts.Add((acting && turn.ExecutingIndex == i ? "▶" : "") + body);
                }
                string copyLine = (ShowCopyKinds ? CopyKinds.Short(e.Model.CopyKind) + "：" : "まね：") + string.Join("・", parts);
                if (Predicting && enemyResult.TryGetValue(e.Model.Id, out var res)) copyLine += "　→ " + res;
                GUI.Label(new Rect(ix + 28, r.y + 19, iw, 18), copyLine, new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.8f, 0.82f, 0.9f) } });
                // 小さな範囲の図（今の危険範囲）
                DrawMiniPattern(new Rect(r.xMax - patW - 4, r.y + 4, patW, patW), e.Model);
                y += rowH;
            }
            return y;
        }

        /// <summary>敵の今の危険範囲を 5×5 の小さな図で（中心＝その敵。砲兵は予告の中心）。</summary>
        void DrawMiniPattern(Rect r, UnitModel e)
        {
            var model = turn.Grid.Model;
            var cells = PhaseRules.TraitCells(model, e);
            if (e.Kind == EnemyKind.Bomber && e.BombCells.Count > 0) cells = e.BombCells;
            var center = e.Kind == EnemyKind.Bomber && e.BombCells.Count > 0 ? e.BombCells[0] : e.Position;
            DrawRect(r, new Color(0f, 0f, 0f, 0.5f));
            float s = r.width / 5f;
            for (int gx = 0; gx < 5; gx++)
                for (int gy = 0; gy < 5; gy++)
                {
                    var c = center + new Vector2Int(gx - 2, 2 - gy);
                    var cell = new Rect(r.x + gx * s + 0.5f, r.y + gy * s + 0.5f, s - 1f, s - 1f);
                    bool hit = cells.Contains(c);
                    DrawRect(cell, hit ? new Color(1f, 0.25f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.07f));
                    if (c == e.Position) DrawRect(new Rect(cell.center.x - s * 0.22f, cell.center.y - s * 0.22f, s * 0.44f, s * 0.44f), Color.Lerp(turn.Grid.ViewOf(e).BaseColor, Color.white, 0.3f));
                }
        }

        // ───────── 右下：ログとキー ─────────

        void DrawShowLog(Rect r)
        {
            if (r.height < 14) return;
            var lines = turn.Log;
            bool hints = true; // 【RC5・レビュー】キーの一覧はいつも出す（前は欄が狭いと消えていた）。狭いときはログを減らす
            int n = Mathf.Clamp((int)((r.height - 15) / 15), 0, 4);
            var st = new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.75f, 0.78f, 0.86f) } };
            int start = Mathf.Max(0, lines.Count - n);
            for (int i = start; i < lines.Count; i++)
                GUI.Label(new Rect(r.x, r.y + (i - start) * 15, r.width, 16), lines[i], st);
            if (hints)
            {
                string keys = "1〜9 札・矢印 向き・Enter 実行・BS 戻す・Esc 取消・Z 戦術札・QE アイテム・T 敵・L 凡例・M 音・O メニュー・Tab 詳細・F 早送り" + (BuildInfo.ExternalPlaytest ? "" : "・F2 検証");
                var ks = new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, 0.85f) } }; // 【RC5・レビュー3】62% → 85%（読みにくかった）
                GUI.Label(new Rect(r.x, r.y + n * 15, r.width, 16), keys, Fit(ks, keys, r.width, 9)); // 【RC5】キーの一覧（幅に合わせて小さく）
            }
        }

        // ───────── 重ねて出す物 ─────────

        void DrawShowDebugMenu(Rect r)
        {
            DrawUi(new Rect(r.x - 6, r.y - 26, r.width + 12, r.height + 32), "panel", PanelBlue);
            GUI.Label(new Rect(r.x, r.y - 24, r.width, 20), "検証メニュー（手札の方式・ステージ・検証用の切替）　F2 で閉じる", new GUIStyle(Small) { fontSize = 12, normal = { textColor = Gold } });
            if (act != null && act.InBattle)
                GUI.Label(new Rect(r.x, r.y + 14, r.width - 160, 40), "ACT1 の途中は、手札の方式やステージの切替は使えない（ランが壊れるため）。タイトルへ戻ると使える。", new GUIStyle(Small) { fontSize = 12, wordWrap = true });
            else DrawModeButtons(new Rect(r.x, r.y + 8, r.width, 64));
            if (act != null && ThemedButton(new Rect(r.xMax - 170, r.y - 26, 170, 22), act.InBattle ? (abandonArmed ? "本当にやめる？（もう一度）" : "タイトルへ（ランは終わる）") : "タイトルへ", false, false, 11))
            {
                if (act.InBattle && !abandonArmed) { abandonArmed = true; return; }
                abandonArmed = false;
                showDebugMenu = false;
                if (act.InBattle) act.AbandonRun(); else act.OpenTitle();
            }
        }

        /// <summary>【RC3】部屋の説明。はじめての戦闘（敵は まねるだけ）では、そのことを書く（部屋の説明は攻撃・接近のある戦闘の物）。</summary>
        string RoomText(StageData stage)
        {
            if (turn.Grid.Model != null && turn.Grid.Model.NoTraits)
                return "はじめての戦闘：この戦闘の敵は、あなたの命令を まねるだけ（自分からは攻撃しない・近づかない）。\n"
                     + "敵どうしが隣にいるときに、誰もいない方へ斬ってみよう。まねた敵が隣の敵を斬る。敵の攻撃（赤）は次の戦闘から。";
            // 【RC4】大技の無い戦闘（はじめてのランの2階）では、大技（赤紫）の行を出さない
            if (act != null && act.InBattle && act.Run != null && act.Run.LessonNoClocks && !string.IsNullOrEmpty(stage.tutorialText))
                return string.Join("\n", stage.tutorialText.Split('\n').Where(l => !l.Contains("大技") && !l.Contains("紫")))
                     + "\n（はじめてのラン：2階から敵の攻撃（赤）と動き・3階から緑の札・大技は中ボスと5階から）"; // 【RC5・レビュー3】ほかの説明と同じに（3階でも出る文だった）
            // 【通常リリース版・デザイナーのレビュー「詳細（Tab）に部屋の解き方が残っている」】本編（はじめてのランの教える階より後）では出さない
            if (act != null && act.InBattle && act.Run != null && act.Run.V4Run && !act.InTutorial && !(act.Run.FirstLesson && act.Run.Floor <= 1))
                return "";
            return stage.tutorialText;
        }

        void DrawShowDetail(Rect r)
        {
            DrawUi(r, "panel", PanelBlue);
            var stage = turn.CurrentStage;
            var title = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold };
            ShadowLabel(new Rect(r.x + 14, r.y + 10, r.width - 28, 22), $"詳細：{stage.stageTitle}（Tab で閉じる）", title, Gold);
            var body = new GUIStyle(Small) { fontSize = 13, wordWrap = true };
            float h = body.CalcHeight(new GUIContent(RoomText(stage)), r.width - 28);
            GUI.Label(new Rect(r.x + 14, r.y + 38, r.width - 28, h), RoomText(stage), body);
            float y = r.y + 46 + h;
            GUI.Label(new Rect(r.x + 14, y, r.width - 28, 20), "ログ（新しい順）", new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Gold } });
            y += 22;
            var st = new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            var lines = turn.Log;
            for (int i = lines.Count - 1; i >= 0 && y < r.yMax - 18; i--, y += 17)
                GUI.Label(new Rect(r.x + 14, y, r.width - 28, 18), lines[i], st);
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { showDetail = false; Event.current.Use(); }
        }

        /// <summary>【検証中】敵の説明（コピーの種類＋兵種の行動）。マウスを乗せたときに出す。</summary>
        /// <summary>【戦闘中心 v3】今の戦闘が Soft ルールか（EnemyHelp の説明を変える）。</summary>
        static bool turnSoft;

        string EnemyHelpNow(UnitModel m)
        {
            if (turn != null && turn.IsV4) return V4EnemyHelp(m, turn.Grid.Model != null && turn.Grid.Model.NoTraits); // 【Combat V4】
            turnSoft = turn != null && turn.Grid != null && turn.Grid.Model != null && turn.Grid.Model.SoftRules;
            return EnemyHelp(m);
        }

        static string EnemyHelp(UnitModel m)
        {
            string copy = m.CopyTrait == CopyTrait.Mirror ? "まね：あなたの命令を、向きを逆にして同じ距離で（あなたが→なら←）。"
                        : m.CopyTrait == CopyTrait.MirrorAttacks ? "まね：移動はそのまま。攻撃・押す・引くだけ逆向き（反射）。斬ったら横へずれる。"
                        : m.CopyKind != CopyKind.All ? CopyKinds.OneLine(m.CopyKind)
                        : "まね：あなたの命令を、自分の位置から同じ向き・同じ距離で。";
            string trait = m.Kind switch
            {
                EnemyKind.Swordsman when turnSoft && RulesEngine.SwordCounter > 0 => $"行動：まねのあと、隣の4マスを斬る（3）。敵どうしにも当たる。\n反撃：隣からあなたが直接殴ると、生き残った剣兵がその場で斬り返す（{RulesEngine.SwordCounter}）。まね攻撃・2マス先からの突きでは斬り返さない。",
                EnemyKind.Swordsman => "行動：まねのあと、隣の4マスを斬る（3）。敵どうしにも当たる。",
                EnemyKind.Chaser => "行動：まねのあと、あなたへ1マス近づく（攻撃はしない）。トゲの手前に寄せると、まねで踏ませられる。",
                EnemyKind.Fleer => "行動：まねのあと、あなたから1マス離れる（攻撃はしない）。",
                EnemyKind.Shield => $"盾：向いている側（正面）からの直接攻撃と矢を -{RulesEngine.ShieldBlock}。背中・横からは普通に通る。向き＝あなたの最後の命令の向き。",
                EnemyKind.Spearman => "行動：まねのあと、向いている方向へ2マス突く（3）。向き＝あなたの最後の命令の向き。",
                EnemyKind.Archer => "行動：まねのあと、向いている方向へ矢（4マス・2）。柱・壁で止まる。",
                EnemyKind.Bomber => "行動：あなたの位置に照準を予告し、次のラウンドに十字の爆発（3・敵にも当たる）。",
                EnemyKind.KnightV3 => "行動：大剣（周り8マス）・爆発（あなたの位置の十字）・炎の波をカウントダウンで。HP 60%・30% で強くなる。",
                EnemyKind.MirrorSoldier => "行動：なし（まねだけ）。",
                EnemyKind.EchoKnight when turnSoft => m.ArmorBroken ? "反響鎧（割れた）：普通の兵と同じ。" : $"反響鎧：あなたの技は {RulesEngine.EchoSoftDamage} だけ通る。敵のまね攻撃・トゲ・爆発・視線・共鳴が当たると鎧が割れて、普通に傷つく。",
                EnemyKind.EchoKnight => "反響鎧：あなたの技（あなたが実行した命令）では傷つかない。敵のまね・トゲ・同士討ち・鏡晶の爆発なら傷つく。",
                EnemyKind.SealGuard => $"封印 {m.Barrier}：あなたの攻撃では割れない。敵のまね攻撃が当たるたびに -1。0 になると普通に傷つく。",
                EnemyKind.Cannon => "写し砲：動かない・倒れない（勝ち負けに数えない）。あなたの攻撃の技だけをまねして一直線に撃つ。",
                EnemyKind.Warden => "鏡の番人：封印がある間は傷つかない。割れると2ラウンドだけ傷つき、その間はあなたの命令を写せない（殴り返さない）。段階の境目（HP 2/3・1/3）で止まり、すぐ次の封印が戻って中央へ帰る。割り方：まね攻撃 → 鏡晶 → 封印の円。3ラウンドごとに大剣。",
                EnemyKind.SealLord => $"封紋の守護者（全まね）：段階1＝主を光る紋に乗せる／段階2＝剣の像（技まね）のまね攻撃を主に当てる／段階3＝二つの核を同じラウンドに（1つ以上はまね）。成功するたびに -{PuzzleBoss.SuccessDamage}、主はそのターンと次のターン動けない（窓：直接攻撃が効く）。段階の境目（{PuzzleBoss.Floor(m, 0)}・{PuzzleBoss.Floor(m, 1)}）でダメージは止まる。巨大石手：あなたのマスを予告→次のラウンドに {PuzzleBoss.HandDamage}（主にも当たる）。",
                EnemyKind.BladeStatue => "剣の像（技まね）：段階2で光る。動かない・倒れない。あなたの技の札を、その場から同じ向きでまねる。主を斬ると光が消える（1体1回）。",
                EnemyKind.Golem => "運び像（移動まね）：あなたの移動の札だけを同じ向き・同じ距離でまねる道具。倒れない・勝ち負けに数えない。運び紋・踏み板へ運ぶ。",
                EnemyKind.Lord => $"写し身の主（まるまね）：段階1＝{PhaseRules.LordRuleText(0)}と守りが割れる（主は中央へ帰り、2ラウンド動けない）／段階2（70%）＝{PhaseRules.LordRuleText(1)}（写し身が残る間、主は 40% より下へ減らない）／段階3（40%）＝{PhaseRules.LordRuleText(2)}と守りが割れる。光柱（あなたの位置の十字を予告→次のラウンドに3→1ラウンド休む）・3ラウンドごとに大剣（周り8マス・3）。",
                EnemyKind.HealClone when m.Dormant => "癒しの写し身（像）：主の段階2で目覚める。それまでは何もしない。",
                EnemyKind.HealClone when turnSoft => $"癒しの写し身（技まね・HP {m.Hp}）：1体ごとに毎ターン主を +{CombatBoss.CloneHeal}。あなたの技の札を同じ向きでまねる：主が隣にいれば主を斬って -{CombatBoss.CloneStrikeDamage}（写し身は砕ける）。殴って倒してもよい。",
                EnemyKind.HealClone => $"癒しの写し身（技まね・傷つかない）：毎ラウンド主を {PhaseRules.CloneHeal} 回復。あなたの技の札を同じ向きでまねる：主が隣にいれば主を斬り、砕ける。",
                EnemyKind.Mender => $"癒し手（{CopyKinds.Short(m.CopyKind)}）：毎ターンの終わりに、いちばん傷ついた仲間を +{PhaseRules.MenderHeal}（自分は回復しない）。あなたの攻撃をまねて、隣の仲間を殴ってしまう。",
                EnemyKind.MirrorLord => CombatBoss.Describe(m),

                EnemyKind.Normal => "行動：なし（まねるだけ）。",
                _ => "行動：" + PhaseRules.KindName(m.Kind),
            };
            return copy + "\n" + trait + $"\n向き：{m.Facing.ToLabel()}";
        }

        void DrawHoverTip(float W)
        {
            if (hoverTitle == null || pendingCommand != null) return;
            var body = new GUIStyle(Small) { fontSize = 12, wordWrap = true };
            float w = 300;
            float h = body.CalcHeight(new GUIContent(hoverBody), w - 20) + 34;
            var r = new Rect(Mathf.Min(hoverPos.x, W - w - 8), hoverPos.y, w, h);
            if (r.yMax > RefHeight - 4) r.y = RefHeight - 4 - h;
            DrawUi(r, "panel", PanelBlue);
            ShadowLabel(new Rect(r.x + 10, r.y + 6, w - 20, 20), hoverTitle, new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold }, Gold);
            GUI.Label(new Rect(r.x + 10, r.y + 28, w - 20, h - 30), hoverBody, body);
        }

        // ───────── キーボード ─────────

        void HandleShowcaseKeys()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;
            var key = ev.keyCode;
            if (key == KeyCode.Tab) { showDetail = !showDetail; ev.Use(); return; }
            if (key == KeyCode.F2) { if (!BuildInfo.ExternalPlaytest) showDebugMenu = !showDebugMenu; abandonArmed = false; ev.Use(); return; } // 外部テスト版は検証メニューを出さない
            if (key == KeyCode.Escape) { if (TacticTargeting) CancelTactic(); else if (tidyMode) { if (turn.PendingSift) turn.CancelSift(); tidyMode = false; tidyPick.Clear(); } else if (act != null && act.PendingItem != PendingItemUse.None) act.CancelItem(); else if (pendingCommand != null) ResetPending(); else if (!showDetail && !showDebugMenu && act != null && act.InBattle) act.BattleMenuOpen = true; else { showDetail = false; showDebugMenu = false; abandonArmed = false; } ev.Use(); return; } // 【第2巡 13】取り消す物が無ければ Esc でメニュー（メニューも Esc で閉じる）
            if (key == KeyCode.L) { showLegend = !showLegend; ev.Use(); return; } // 【RC5】凡例
            if (key == KeyCode.O && act != null && act.InBattle) { act.BattleMenuOpen = true; ev.Use(); return; } // 【RC5・レビュー3】メニュー
            if (key == KeyCode.M) { Sfx.Muted = !Sfx.Muted; Bgm.SetMuted(Sfx.Muted); if (act != null && act.Profile != null) { act.Profile.soundOff = Sfx.Muted; act.SaveProfile(); } ev.Use(); return; } // 【RC5】音の ON/OFF
            if (key == KeyCode.T && turn.Enemies.Any(e => e.IsAlive)) // 【RC5】敵の説明を順に（マウスで敵をクリックするのと同じ）
            {
                var alive = turn.Enemies.Where(e => e.IsAlive).ToList();
                int at = selectedEnemy == null ? -1 : alive.IndexOf(selectedEnemy);
                selectedEnemy = at + 1 < alive.Count ? alive[at + 1] : null;
                ev.Use();
                return;
            }
            if (tidyMode && turn.State == BattleState.PlayerInput) // 【RC5・レビュー】手札整理・選別も 1〜9 で選んで Enter
            {
                if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
                {
                    int ti = key - KeyCode.Alpha1;
                    var th = turn.Deck.Hand;
                    if (ti < th.Count && !turn.IsCardPlanned(th[ti]))
                    {
                        if (tidyPick.Contains(th[ti])) tidyPick.Remove(th[ti]);
                        else if (tidyPick.Count < (turn.PendingSift ? 2 : turn.TidyMax)) tidyPick.Add(th[ti]);
                    }
                    ev.Use();
                    return;
                }
                if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && tidyPick.Count > 0)
                {
                    if (turn.PendingSift) turn.SiftCards(tidyPick); else turn.Tidy(tidyPick);
                    tidyMode = false; tidyPick.Clear(); ev.Use();
                    return;
                }
            }
            if (key == KeyCode.H && !tidyMode && pendingCommand == null && turn.State == BattleState.PlayerInput && turn.CanTidy) { tidyMode = true; tidyPick.Clear(); ResetPending(); ev.Use(); return; } // 【RC5】手札整理
            if (TacticTargeting && turn.State == BattleState.PlayerInput) { if (HandleTacticTargetKey(key)) ev.Use(); return; } // 【RC5】対象も矢印＋Enter で
            if (WarpPicking && turn.State == BattleState.PlayerInput && HandleWarpKey(key)) { ev.Use(); return; } // 【RC5】転移石の行き先も矢印＋Enter で
            if (tidyMode || TacticTargeting) return; // 手札整理・戦術札の対象選びの間はカード・実行のキーを使わない
            // 【RC5】Q・E＝アイテム（1つ目・2つ目）。転移石は使えるときだけ（ボタンと同じ条件）
            if (pendingCommand == null && (key == KeyCode.Q || key == KeyCode.E) && act != null && act.Run != null && act.CanUseItems)
            {
                int idx = key == KeyCode.Q ? 0 : 1;
                if (idx < act.Run.Items.Count && ItemWhyNot(act.Run.Items[idx]) == null) act.UseItem(act.Run.Items[idx]);
                ev.Use();
                return;
            }
            if (turn.State != BattleState.PlayerInput) return;
            if (pendingCommand == null && HandleTacticKey(key)) { ev.Use(); return; } // 【Combat V4】Z・X・C＝戦術札（RC3 で Q・W・E から）
            if (pendingCommand != null && !hasPendingDirection)
            {
                var d = InputPriority.KeyToDirection(key); // 【RC4】W A S D・矢印キー
                if (d.HasValue) { ChooseDirection(d.Value); ev.Use(); return; }
            }
            if (pendingCommand != null && hasPendingDirection && key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
            {
                int dist = key - KeyCode.Alpha0;
                if (dist >= pendingCommand.minDistance && dist <= pendingCommand.maxDistance) Register(pendingDirection, dist);
                ev.Use();
                return;
            }
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
            {
                UiStyles.KeyNumbersSeen = true; // 【RC10】キーで遊ぶ人には、札の番号を出す
                int i = key - KeyCode.Alpha1;
                var hand = turn.Deck.Hand;
                if (i < hand.Count && turn.CanAddCard(hand[i])) Select(hand[i].Data, hand[i]);
                ev.Use();
                return;
            }
            bool blankPicking = act != null && act.PendingItem == PendingItemUse.BlankStone;
            if ((key == KeyCode.Return || key == KeyCode.KeypadEnter) && pendingCommand == null && turn.CanExecute && !blankPicking && !WarpPicking) { turn.ExecuteTurn(); ev.Use(); return; } // 【RC4・レビュー】Space は窓を閉じるのにも使うので、実行は Enter だけ
            if (key == KeyCode.Backspace && pendingCommand == null) { turn.RemoveLastCommand(); ev.Use(); }
        }

        // ───────── 【検証中】ACT1：アイテム・選ぶ物 ─────────

        void DrawActItems(Rect r)
        {
            var run = act.Run;
            if (run == null) return;
            // 【RC5】16:10 などで欄が狭いと、2つの枠が重なっていた → 入りきらないときは「空き」を出さない（持っているアイテムだけ）
            int shown = r.width >= ActContent.ItemSlots * 38f + 4f * (ActContent.ItemSlots - 1) ? ActContent.ItemSlots : Mathf.Clamp(run.Items.Count, 1, ActContent.ItemSlots);
            float iw = Mathf.Max(34f, (r.width - 4f * (shown - 1)) / shown); // 【RC3】戦闘中の「音」のボタンの分だけ狭く（60 だと山札の欄に重なった）
            for (int i = 0; i < shown; i++)
            {
                var b = new Rect(r.x + i * (iw + 4), r.y, iw, r.height);
                if (i >= run.Items.Count) { DrawFrame(b, new Color(1f, 1f, 1f, 0.12f), 1f); GUI.Label(b, "空き", new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.3f) } }); continue; }
                var id = run.Items[i];
                var def = ActContent.Item(id);
                // 【RC5】転移石は「今使えるか」を見た目で：使えるとき（危ないマスにいる・命令を組む前）は金色に光る。使えないときは暗く、乗せると理由
                string why = ItemWhyNot(id);
                bool usable = act.CanUseItems && why == null;
                int itemFs = Fit(new GUIStyle(Label) { fontSize = 12 }, def.Name, b.width - 16, 9).fontSize; // 【RC5・レビュー4】狭い帯（16:10）で「転」だけになっていた
                if (ThemedButton(b, def.Name, act.PendingItem != PendingItemUse.None, false, itemFs, usable)) act.UseItem(id);
                GUI.Label(new Rect(b.xMax - 13, b.y + 1, 12, 12), i == 0 ? "Q" : "E", new GUIStyle(Tiny) { fontSize = 10, alignment = TextAnchor.UpperRight, normal = { textColor = new Color(1f, 1f, 1f, usable ? 0.75f : 0.35f) } }); // 【RC5】キー
                if (id == ItemId.WarpStone && usable && act.PendingItem == PendingItemUse.None)
                    DrawFrame(b, new Color(Gold.r, Gold.g, Gold.b, Blink(0.1f, 1f, 5f)), 2f); // 【RC5・最終ゲート】点滅の設定で止まる
                if (b.Contains(Event.current.mousePosition)) { hoverTitle = def.Name + "（アイテム・使うと無くなる）"; hoverBody = def.Description + (why != null ? "\n\n今は使えない：" + why : ""); hoverPos = new Vector2(b.x - 100, b.yMax + 4); }
            }
        }

        /// <summary>【RC5】アイテムが今使えない理由（使えるなら null）。ボタンと Q・E のキーで同じ判定。</summary>
        string ItemWhyNot(ItemId id)
        {
            if (id != ItemId.WarpStone || turn.Grid.Model == null) return null;
            return turn.PlannedCommands.Count > 0 ? "命令を組む前に使う（組んだ命令を戻すと使える）" : RescueRules.WhyNot(turn.Grid.Model);
        }

        bool abandonArmed;
        /// <summary>撮影用：矢印にマウスを乗せた状態を作る（ゲームでは null）。</summary>
        public Direction? DebugFocus;

        // ───── 【検証中】Copy Core V2 サンドボックス ─────
        bool IsCopySandbox => turn != null && turn.CurrentStage != null && turn.CurrentStage.copySandbox && (act == null || !act.InBattle);

        static string ShortRule(CopyMode m) => m switch
        {
            CopyMode.MirrorBatch => "まね B：あなたが全部終えたあと、敵は反対向きで使う",
            CopyMode.MirrorImmediate => "まね C：命令1つごとに、すぐ敵が反対向きで使う",
            CopyMode.TowardImmediate => "まね D：命令1つごとに、すぐ敵があなたへ向けて使う",
            CopyMode.MimicReflectImmediate => "まね E：命令1つごとに、すぐ敵が使う（動きはまねる・技ははね返す）",
            _ => "まね A：あなたが全部終えたあと、敵は同じ向きで使う",
        };

        static string CopyHintFor(BattleModel m, CommandData c)
        {
            if (!m.CopyEnabled) return "（COPY OFF）";
            if (!c.UsesDirection) return c.notCopied ? "（敵はまねしない）" : "（敵も同じ技を使う）";
            if (c.notCopied) return "（敵はまねしない）";
            return m.CopyMode switch
            {
                CopyMode.MirrorBatch or CopyMode.MirrorImmediate => c.copyMirrored ? "敵：同じ向き（この札は反対の反対）" : "敵：反対向き（→ なら ←）",
                CopyMode.TowardImmediate => c.copyMirrored ? "敵：あなたと反対の方へ" : "敵：あなたの方へ向けて",
                CopyMode.MimicReflectImmediate => (CopyCore.IsTechnique(c.type) != c.copyMirrored) ? "敵：反対向き（技ははね返す）" : "敵：同じ向き（動きはまねる）",
                _ => c.copyMirrored ? "敵：反対向き" : "敵：同じ向き（→ なら →）",
            };
        }

        /// <summary>方式 A/B/C/D・COPY ON/OFF・一覧へ（変えたらステージを最初から）。</summary>
        /// <summary>【Copy Core V3】研究室の部屋（ACT の外）：COPY ON/OFF と一覧へ。</summary>
        bool IsCopyLabV3 => turn != null && turn.CurrentStage != null && (turn.CurrentStage.copyLab || turn.CurrentStage.puzzleRoom || turn.CurrentStage.combatRoom) && (act == null || !act.InBattle);

        void DrawCopyLabBar(Rect r)
        {
            var m = turn.Grid.Model;
            float x = r.x;
            if (ThemedButton(new Rect(x, r.y, 92, 22), m.CopyEnabled ? "まね ON" : "まね OFF", !m.CopyEnabled, false, 12))
            {
                CopyCore.SandboxCopyOff = !CopyCore.SandboxCopyOff;
                turn.LoadStage(turn.CurrentStageIndex);
                ResetPending();
            }
            x += 96;
            if (act != null && ThemedButton(new Rect(x, r.y, 70, 22), "一覧へ", false, false, 12))
            {
                if (turn.CurrentStage.combatRoom) act.OpenCombatLab();
                else if (turn.CurrentStage.puzzleRoom) act.OpenPuzzleLab();
                else act.OpenCopyLabV3();
            }
        }

        void DrawCopyModeBar(Rect r)
        {
            var m = turn.Grid.Model;
            float x = r.x;
            GUI.Label(new Rect(x, r.y + 2, 64, 20), "まね方式", new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, normal = { textColor = new Color(0.8f, 0.82f, 0.9f) } });
            x += 64;
            foreach (var mode in CopyCore.All)
            {
                bool on = m.CopyMode == mode;
                if (ThemedButton(new Rect(x, r.y, 30, 22), CopyCore.Letter(mode), on, false, 12) && !on)
                {
                    CopyCore.SandboxMode = mode;
                    turn.LoadStage(turn.CurrentStageIndex);
                    ResetPending();
                }
                x += 32;
            }
            x += 6;
            if (ThemedButton(new Rect(x, r.y, 92, 22), m.CopyEnabled ? "まね ON" : "まね OFF", !m.CopyEnabled, false, 12))
            {
                CopyCore.SandboxCopyOff = !CopyCore.SandboxCopyOff;
                turn.LoadStage(turn.CurrentStageIndex);
                ResetPending();
            }
            x += 96;
            if (act != null && ThemedButton(new Rect(x, r.y, 70, 22), "一覧へ", false, false, 12)) act.OpenCopyLab();
        }

        /// <summary>盤面のしるし：踏み板・扉・鏡晶・封印の円、封印の数・反響鎧・写し砲の札。</summary>
        void DrawCopyCoreMarks(BattleModel m, System.Func<Vector2Int, Rect> cellRect)
        {
            var b = m.Board;
            var tag = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            foreach (var c in b.PlayerPlates) { var r = cellRect(c); DrawFrame(r, new Color(0.4f, 0.75f, 1f, 0.95f), 3f); ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 16), "あなた用", tag, new Color(0.5f, 0.8f, 1f)); }
            foreach (var c in b.EnemyPlates) { var r = cellRect(c); DrawFrame(r, new Color(1f, 0.45f, 0.4f, 0.95f), 3f); ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 16), "敵用", tag, new Color(1f, 0.55f, 0.5f)); }
            foreach (var c in b.Gates)
            {
                if (b.GatesOpen) continue;
                var r = cellRect(c);
                DrawRect(r, new Color(0.45f, 0.3f, 0.15f, 0.95f));
                DrawFrame(r, new Color(0.9f, 0.7f, 0.35f), 2f);
                ShadowLabel(r, "扉", new GUIStyle(Big) { fontSize = 16, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.85f, 0.5f));
            }
            foreach (var c in b.Crystals)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.28f, r.center.y - r.height * 0.36f, r.width * 0.56f, r.height * 0.72f);
                DrawRect(inner, new Color(0.65f, 0.4f, 0.95f, 0.9f));
                DrawFrame(inner, new Color(0.95f, 0.85f, 1f), 2f);
                ShadowLabel(inner, "晶", new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter }, Color.white);
            }
            bool sealLord = m.Enemies.Any(e => e.Kind == EnemyKind.SealLord && e.IsAlive);
            foreach (var c in b.Seals)
            {
                var r = cellRect(c);
                if (sealLord)
                {
                    // 【Puzzle v2】封紋の守護者の「光る紋」：段階1の間だけ光る。使った紋は消える
                    var lord = m.Enemies.First(e => e.Kind == EnemyKind.SealLord && e.IsAlive);
                    if (!b.IsLitSeal(c)) continue;
                    bool active = lord.BossStage == 0;
                    var col = active ? new Color(1f, 0.85f, 0.35f, 0.5f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 2.5f))) : new Color(1f, 1f, 1f, 0.25f);
                    DrawRect(new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 16), new Color(col.r, col.g, col.b, active ? 0.18f : 0.05f));
                    DrawFrame(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), col, active ? 3f : 1f);
                    ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 16), "紋", tag, active ? Gold : new Color(0.7f, 0.7f, 0.75f));
                    continue;
                }
                DrawFrame(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), new Color(1f, 1f, 1f, 0.85f), 2f);
                ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 16), "円", tag, Color.white);
            }
            // 【Copy Core V3】爆薬樽（誰の攻撃でも爆発）・写し箱（敵のまねた攻撃でだけ開く）
            foreach (var c in b.Barrels)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.3f, r.center.y - r.height * 0.34f, r.width * 0.6f, r.height * 0.68f);
                DrawRect(inner, new Color(0.75f, 0.25f, 0.12f, 0.95f));
                DrawFrame(inner, new Color(1f, 0.75f, 0.4f), 2f);
                ShadowLabel(inner, "樽", new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter }, Color.white);
            }
            foreach (var c in b.Chests)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.34f, r.center.y - r.height * 0.26f, r.width * 0.68f, r.height * 0.52f);
                DrawRect(inner, new Color(0.55f, 0.3f, 0.75f, 0.95f));
                DrawFrame(inner, Gold, 2f);
                ShadowLabel(inner, "箱", new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter }, Gold);
            }
            // 【Copy Core V3】対の鏡核：このラウンドに当たった物は明るく（同じラウンドに全部当てると割れる）
            var hitCores = new HashSet<Vector2Int>(b.CoreHitsThisRound);
            int coreTotal = b.Cores.Count();
            bool coresAsleep = CombatBoss.CoresAsleep(m); // 【RC2】ボスの段階3まで核は眠り：薄く・数えない（段階1で名札と重なって読みにくかった）
            foreach (var c in b.Cores)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.32f, r.center.y - r.height * 0.32f, r.width * 0.64f, r.height * 0.64f);
                bool hit = hitCores.Contains(c);
                if (coresAsleep && CombatV2)
                {
                    // 【RC8】段階3まで核の説明は出さない。【RC11・最後の確認（QA）「剣兵の赤が隣の床の1マスだけ欠けている」】眠っている核も盤面にあって通れない・攻撃も止まるのに
                    //   何も描いていなかった（見えない壁）→ 眠る写し身の石像と同じく、灰色の石の結晶（文字なし・マウスで説明）
                    ShadowLabel(new Rect(r.x, r.y - r.height * 0.06f, r.width, r.height), "◆", new GUIStyle(Big) { fontSize = Mathf.RoundToInt(r.height * 0.62f), alignment = TextAnchor.MiddleCenter }, new Color(0.5f, 0.53f, 0.58f, 0.95f));
                    GUI.Label(new Rect(r.x, r.yMax - 16f, r.width, 14f), "眠る核", new GUIStyle(Tiny) { fontSize = 10, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.78f, 0.8f, 0.85f, 0.9f) } }); // 【RC11・最後の確認（QA）「名前の無い灰色のひし形」】
                    if (r.Contains(Event.current.mousePosition)) { cardTipTitle = "眠っている鏡の核（石）"; cardTipBody = "段階3で目覚める。それまでは石：通れない・攻撃もここで止まる。"; }
                    continue;
                }
                if (coresAsleep) { DrawRect(inner, new Color(0.25f, 0.35f, 0.5f, 0.45f)); ShadowLabel(inner, "核", new GUIStyle(Big) { fontSize = 13, alignment = TextAnchor.MiddleCenter }, new Color(0.75f, 0.82f, 0.95f, 0.7f)); continue; }
                if (CombatV2)
                {
                    // 【RC8・初見テスト「核の青い四角がボタンに見えた・0/2 が何の数か分からない」】結晶（◆）に見える形・「叩いた 0/2」
                    bool hitNow = hitCores.Contains(c);
                    int ci2 = b.CoreHome.ToList().IndexOf(c);
                    float pulse = hitNow ? 1f : Blink(0.75f, 1f, 3f);
                    ShadowLabel(new Rect(r.x, r.y - r.height * 0.06f, r.width, r.height), "◆", new GUIStyle(Big) { fontSize = Mathf.RoundToInt(r.height * 0.78f), alignment = TextAnchor.MiddleCenter }, hitNow ? new Color(1f, 0.85f, 0.35f) : new Color(0.45f * pulse, 0.75f * pulse, 1f));
                    ShadowLabel(new Rect(r.x, r.y + r.height * 0.22f, r.width, r.height * 0.36f), "核" + (ci2 >= 0 && b.CoreHome.Count > 1 ? (ci2 + 1).ToString() : ""), new GUIStyle(Big) { fontSize = 13, alignment = TextAnchor.MiddleCenter }, Color.white); // 【RC9】核A・核B は敵の記号 A・B と同じ字だった → 核1・核2
                    // 【RC9】「叩いた 0/2」は核を結ぶ線の札「同時に 0/2」へ（核ごとに2回と読まれた）
                    if (r.Contains(Event.current.mousePosition)) { cardTipTitle = "鏡の核"; cardTipBody = $"2つの核を同じターンに叩く（1つ以上は敵のまねで）と共鳴：主に -{CombatBoss.CoreBossDamage}・そのターン主は動けない。核は {RulesEngine.CoreRegrowRounds} ターンで戻る。"; }
                    continue;
                }
                DrawRect(inner, hit ? new Color(1f, 0.85f, 0.35f, 0.95f) : new Color(0.25f, 0.6f, 0.95f, 0.9f));
                DrawFrame(inner, Color.white, 2f);
                int ci = b.CoreHome.ToList().IndexOf(c);
                ShadowLabel(inner, "核" + (ci >= 0 && b.CoreHome.Count > 1 ? ((char)('A' + ci)).ToString() : ""), new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter }, Color.white); // 目的の札と同じ A/B（レビュー）
                ShadowLabel(new Rect(r.x, r.yMax - 15, r.width, 15), $"{hitCores.Count}/{coreTotal}", tag, hit ? Gold : new Color(0.7f, 0.85f, 1f));
            }
            if (ShowCopyKinds && !CombatV2) // 【RC8】まね方は上の敵の欄（水色の小さな字）と、札を選んだときの水色の枠で
                foreach (var u in m.Enemies)
                {
                    // 【Puzzle v2】パズルの部屋では全まね（∞）にも印。ほかの部屋は今までどおり「印なし＝全まね」
                    if (!u.IsAlive || (u.CopyKind == CopyKind.All && !u.Dormant && !IsPuzzleUi) || (u.Kind == EnemyKind.Cannon && !IsPuzzleUi)) continue; // 【Puzzle v2】写し砲にも技まねの印（パズルの部屋）
                    if (u.Kind == EnemyKind.MirrorLord && m.V4) continue; // 【RC5・レビュー】ボスの体を印で隠さない（「主は全まね」は目的の欄と敵の詳細に出る）
                    // 【レビュー】すぐ下のマスにユニットがいると、その名札と印が重なる → 印をマスの中ほどへ
                    var cr = cellRect(u.Position);
                    bool below = m.Enemies.Where(o => o.IsAlive && o != u).Select(o => o.Position).Append(m.Player.Position)
                        .Any(p => { var r2 = cellRect(p); return Mathf.Abs(r2.x - cr.x) < cr.width * 1.1f && Mathf.Abs(r2.y - cr.yMax) < cr.height * 0.5f; }); // 【RC5】斜め下も（名札はマスより広い：ボスの写し身の名札が主の「全まね」に重なった）
                    DrawCopyKindChip(cr, u, tag, below);
                }
            foreach (var u in m.Enemies)
            {
                if (!u.IsAlive || CombatV2) continue; // 【RC8】倒し方の札は出さない（目的の1行・敵の説明へ）
                var r = cellRect(u.Position);
                // 【Puzzle v2】倒し方の短い印（直接× まね○ など）
                string label = HurtTag(m, u);
                if (label == null) continue;
                float lw = tag.CalcSize(new GUIContent(label)).x + 8; // 【Puzzle v2】マスの幅を超えてよい（「まね○」が切れていた：レビュー）
                var lr = new Rect(r.center.x - lw / 2, r.y + 1, lw, 15);
                DrawRect(lr, new Color(0.12f, 0.05f, 0.2f, 0.85f));
                ShadowLabel(lr, label, tag, u.Barrier > 0 ? new Color(0.85f, 0.6f, 1f) : u.Kind == EnemyKind.Warden ? Gold : new Color(0.7f, 0.85f, 1f));
            }
        }

        /// <summary>【Copy Core V3】コピーの種類を見せる（検証部屋・コピー中心のミニ ACT1、または種類の違う敵がいるとき）。</summary>
        bool ShowCopyKinds => turn != null && turn.Grid != null && ((turn.CurrentStage != null && (turn.CurrentStage.copyLab || turn.CurrentStage.puzzleRoom || turn.CurrentStage.combatRoom)) || turn.Grid.Model.Enemies.Any(e => e.CopyKind != CopyKind.All));

        string KindSuffix(UnitModel m) => ShowCopyKinds && !m.IsObject && (m.Dormant || m.CopyKind != CopyKind.All) ? "・" + (m.Dormant ? "像" : CopyKinds.Short(m.CopyKind)) : "";

        /// <summary>コピーの種類の色：全写し＝金、技写し＝赤、歩写し＝青、写さない＝灰（カードの色と同じ考え方：赤い札は技写し、青い札は歩写しが写す）。</summary>
        static Color CopyKindColor(CopyKind k) => k == CopyKind.None ? new Color(0.65f, 0.65f, 0.7f) : k == CopyKind.Technique ? new Color(0.45f, 0.9f, 1f) : k == CopyKind.Move ? new Color(0.6f, 0.95f, 1f) : new Color(0.35f, 0.95f, 1f); // 【RC5・レビュー】まね方の印は水色（まね＝水色）。前は 技まね＝赤・全まね＝金 で、敵の攻撃・選んだ向きの色と重なっていた。見分けは絵と文字

        static string LordTag(BattleModel m, UnitModel u)
        {
            int ph = PhaseRules.LordPhase(u);
            if (ph == 1) return PhaseRules.ClonesLeft(m) > 0 ? "段階2/3：写し身" : "段階2/3";
            if (u.Barrier > 0) return ph == 0 ? "段階1/3：核" : "段階3/3：円";
            return $"段階{ph + 1}/3：割れた";
        }

        /// <summary>カードの向きを選ぶとき：敵それぞれが、その命令を「どの向きで」使うかの矢印（結果やダメージは出さない）。</summary>
        void DrawCopyArrows(BattleModel m, CommandData card, Direction dir, System.Func<Vector2Int, Rect> cellRect)
        {
            if (!m.CopyEnabled || card == null || card.notCopied || !card.UsesDirection) return;
            var rec = new CommandRecord(card, dir, card.minDistance);
            var tag = new GUIStyle(Big) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            foreach (var e in m.Enemies)
            {
                if (!e.IsAlive) continue;
                var c = CopyCore.CopyOne(rec, e, m.Player, m.CopyMode);
                if (c == null) continue;
                var r = cellRect(e.Position);
                var off = c.Direction.ToOffset();
                var p = new Vector2(r.center.x + off.x * r.width * 0.42f, r.center.y - off.y * r.height * 0.42f);
                // 【企画書 v5・UI】上向きは名札（マスの上の辺にかかる）と重なるので、マスの右上の内側へ
                if (c.Direction == Direction.Up) p = new Vector2(r.center.x + r.width * 0.3f, r.center.y - r.height * 0.16f);
                bool v2 = CombatV2; // 【RC9・初見テスト「水色の点線と↑の意味が分からない」】「まね↑」（この敵がまねる向き。結果は出さない）
                var ar = v2 ? new Rect(r.x + 2, r.yMax - 40, 42, 18) : new Rect(p.x - 13, p.y - 13, 26, 26); // 【RC9】マスの中（下の札の上）：右の列で盤面の外へ切れ、右上の「-3」と重なっていた
                DrawRect(ar, new Color(0.03f, 0.2f, 0.25f, 0.92f));
                if (v2) DrawFrame(ar, new Color(0.4f, 0.95f, 1f, 0.9f), 1f);
                ShadowLabel(ar, v2 ? "まね" + DirGlyph(c.Direction) : DirGlyph(c.Direction), v2 ? new GUIStyle(Big) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false } : tag, new Color(0.5f, 1f, 1f)); // 【Puzzle v2】まねの向きは水色（紫は予告の色なので分ける：レビュー）
            }
        }
        int shownMessageSerial;
        float shownMessageAt;

        /// <summary>先読み・回収の鉤の選択画面が開いているか（開いている間は背面を操作できない）。</summary>
        bool PickerOpen => (turn.PendingForesight > 0 && turn.State == BattleState.PlayerInput && turn.ForesightChoices.Count > 0) ||
                           (turn.PendingRecycle > 0 && turn.State == BattleState.PlayerInput && turn.RecycleChoices.Count > 0) ||
                           (act != null && act.PendingItem == PendingItemUse.Hook);

        /// <summary>先読み（山札の上から1枚）・回収の鉤（捨て札から1枚）を選ぶ画面。</summary>
        void DrawPickers(float W)
        {
            List<CardInstance> list = null;
            string title = null;
            System.Action<CardInstance> pick = null;
            System.Action skip = null;
            if (turn.PendingRecycle > 0 && turn.State == BattleState.PlayerInput && turn.RecycleChoices.Count > 0)
            {
                list = turn.RecycleChoices.ToList();
                title = "再利用：捨て札から1枚を手札へ（新しい順）";
                pick = c => turn.ChooseRecycle(c);
                skip = () => turn.ChooseRecycle(null);
            }
            else if (turn.PendingForesight > 0 && turn.State == BattleState.PlayerInput && turn.ForesightChoices.Count > 0)
            {
                list = turn.ForesightChoices.ToList();
                title = "先読み：山札の上から1枚を手札へ";
                pick = c => turn.ChooseForesight(c);
                skip = () => turn.ChooseForesight(null);
            }
            else if (act != null && act.PendingItem == PendingItemUse.Hook)
            {
                list = turn.Deck.DiscardPile.Reverse().Take(10).ToList();
                title = "回収の鉤：捨て札から1枚を手札へ（新しい順）";
                pick = c => act.ChooseHookCard(c);
                skip = () => act.CancelItem();
            }
            if (list == null) return;
            // Esc は「選ばない」（F7）
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Event.current.Use(); skip(); return; }
            float gap = 10;
            float cw = Mathf.Min(130, (W - 80) / Mathf.Max(1, list.Count) - gap), ch = cw * 1.32f; // 枚数が多ければ縮める（画面の外に出さない）
            float w = Mathf.Min(W - 40, list.Count * (cw + gap) + 40);
            var r = new Rect((W - w) / 2, 180, w, ch + 90);
            DrawUi(r, "panel", PanelBlue);
            ShadowLabel(new Rect(r.x + 16, r.y + 8, r.width - 32, 24), title, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            for (int i = 0; i < list.Count; i++)
            {
                var cr = new Rect(r.x + 20 + i * (cw + gap), r.y + 38, cw, ch);
                DrawCardV2(cr, list[i].Data, false, false, 0, true, 1f, 0);
                if (GUI.Button(cr, GUIContent.none, GUIStyle.none)) { pick(list[i]); return; }
            }
            if (ThemedButton(new Rect(r.xMax - 140, r.yMax - 36, 124, 28), "選ばない", false, false, 12)) skip();
        }
    }
}
