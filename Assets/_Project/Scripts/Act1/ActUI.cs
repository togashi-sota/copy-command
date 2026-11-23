using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】ACT1 の戦闘以外の画面（タイトル・役職・扉・報酬・商人・休息・特殊部屋・結果）。仮 UI（IMGUI）。
    /// 戦闘中は BattleUI（新しい戦闘画面）が描き、アイテムと刻印もそこに出す。
    /// </summary>
    public partial class ActUI : MonoBehaviour
    {
        [SerializeField] ActRunManager act;
        BattleUI battleUi;
        [SerializeField] VisualTheme uiTheme;

        static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
        static readonly Color PanelBlue = new Color(0.08f, 0.1f, 0.18f, 0.95f);
        bool showDeck;
        int pickRemove = -1;      // 削除の確認待ち（デッキの何枚目か。種類ではなく「その1枚」）
        string removeMode;        // "shop" / "rest"
        readonly Dictionary<string, string> roomInfo = new Dictionary<string, string>();
        BoonOption pendingBoon;   // 旅立ち「1枚削除」で削除する札を選んでいる間

        // 【検証中】2026-10-05 監査 F3：ダブルクリックの2回目が、変わったばかりの画面のボタンを押してしまう（役職を見ずに指揮官で始まる等）。
        // 画面が変わった直後と、ボタンを押した直後の 0.25 秒はクリックを受け付けない
        const float ClickGuard = 0.25f;
        ActScreen lastScreen;
        float screenChangedAt = -10f, lastClickAt = -10f;
        bool Guarded => Time.unscaledTime - screenChangedAt < ClickGuard || Time.unscaledTime - lastClickAt < ClickGuard;

        /// <summary>透明なボタン（カード・扉の札など）。押したら連打よけの時刻を残す。</summary>
        bool Hit(Rect r)
        {
            if (!KeyboardHit(r, false) && !GUI.Button(r, GUIContent.none, GUIStyle.none)) return false; // 【RC5】キーボードでも選べる
            lastClickAt = Time.unscaledTime;
            return true;
        }

        void OnGUI()
        {
            if (act != null && act.Screen != lastScreen) { lastScreen = act.Screen; screenChangedAt = Time.unscaledTime; act.BattleMenuOpen = false; } // 【RC5・レビュー3】画面が変わったら戦闘中のメニューは閉じる
            if (act != null && act.InBattle)
            {
                // 戦闘中のヒントだけここで描く（戦闘画面は BattleUI。帯は左の盤面の下・説明欄の上。手札を隠さない）
                Setup();
                GUI.depth = -20;
                float s = ApplyScale();
                // 盤面の幅（画面の 47%）と下の説明欄（108）に合わせる（4:3 でも右の欄にはみ出さない：レビュー2）
                if (battleUi == null) battleUi = FindAnyObjectByType<BattleUI>();
                float bw = Screen.width / s * (battleUi != null ? battleUi.BoardFracNow : BattleUI.ShowBoardFrac); // 【RC8】盤面の幅は今の配置
                // 【レビュー】説明欄の高さは BattleUI と同じ判定で（戦闘中心 v3 の部屋で、帯が【目的】と目標を隠していた）
                float infoH = battleUi != null ? battleUi.InfoH : act.Turn != null && act.Turn.CurrentStage != null && act.Turn.CurrentStage.puzzleRoom ? BattleUI.PuzzleInfoH : BattleUI.ShowInfoH;
                if (battleUi == null || (!battleUi.LegendOpen && !battleUi.DrawsHintsItself)) DrawHint(new Rect(8, RefHeight - infoH - 36, bw - 16, 34), 12); // 【RC8】新しい配置は右下の欄（BattleUI）が描く
                if (act.BattleMenuOpen) // 【RC5・レビュー3】戦闘中のメニュー（前は戦闘中に設定・遊び方・タイトルへが無かった）
                {
                    float bw0 = Screen.width / s;
                    if (showSettings) DrawSettings(bw0, RefHeight);
                    else if (showHowTo) DrawHowTo(bw0, RefHeight);
                    else DrawBattleMenu(bw0, RefHeight);
                    var bev = Event.current;
                    if (bev.isMouse || bev.isKey || bev.type == EventType.ScrollWheel) bev.Use(); // 後ろの戦闘画面へ渡さない
                }
                else { showSettings = false; showHowTo = false; menuQuitArmed = false; }
                return;
            }
            if (act == null || !act.InAct) return;
            UiStyles.CopyTextMode = CopyMode.Current; // ACT1 の画面のカードは今の方式の文（Copy Core V2 のサンドボックスの後でも）
            UiStyles.CopyTextOff = false;
            if (Guarded && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseUp)) Event.current.Use();
            Setup();
            KeyboardBegin(); // 【RC5】矢印・Tab で選ぶ・Enter で押す
            UiStyles.Theme = uiTheme;
            GUI.depth = -10;
            float scale = ApplyScale();
            float w = Screen.width / scale, h = RefHeight;
            DrawBackground(w, h);
            // モーダル（カードの削除・デッキ一覧）が開いている間は、後ろの画面にクリック・キーを渡さない
            bool modal = removeMode != null || (showDeck && act.Run != null) || (showSettings && act.Screen != ActScreen.Title);
            if (act.Screen != ActScreen.Boon) pendingBoon = null;
            var input = ModalInput.Begin(modal);
            if (act.Screen != ActScreen.Role) confirmRole = null; // 【RC5・コードレビュー】役職の画面を離れたら確認を取り消す（前はタイトルへ戻っても「確認済み」のままで、次の1回で中断したランが消えた）
            switch (act.Screen)
            {
                case ActScreen.Title: DrawTitle(w, h); break;
                case ActScreen.Role: DrawRoles(w, h); break;
                case ActScreen.Boon: DrawBoon(w, h); break;
                case ActScreen.CopyLab: DrawCopyLab(w, h); break;
                case ActScreen.CopyLabV3: DrawCopyLabV3(w, h); break;
                case ActScreen.Doors: DrawDoors(w, h); break;
                case ActScreen.Reward: DrawReward(w, h); break;
                case ActScreen.Shop: DrawShop(w, h); break;
                case ActScreen.Rest: DrawRest(w, h); break;
                case ActScreen.Event: DrawEvent(w, h); break;
                case ActScreen.Result: DrawResult(w, h); break;
                case ActScreen.TutorialDone: DrawTutorialDone(w, h); break; // 【RC3】
                case ActScreen.TutorialRun: DrawTutorialRun(w, h); break; // 【RC4】チュートリアルの最後「1ランの流れ」
                case ActScreen.FestivalEntry: DrawFestivalEntry(w, h); break; // 【文化祭】入口（難易度を選ぶ）
                case ActScreen.FestivalResult: DrawFestivalResult(w, h); break; // 【文化祭】結果（ランク・タイム）
            }
            if (act.Run != null && act.Screen != ActScreen.Result && act.Screen != ActScreen.Title && act.Screen != ActScreen.Role) DrawStatus(w);
            if (!modal) DrawHint(new Rect(w / 2 - 400, h - 116, 800, 34), 14);
            ModalInput.End(input);
            // 用語（2026-10-05 本人プレイ：売る／手放す／削除が混ざって分かりにくい）：購入＝ルーン片を払って手に入れる／削除＝デッキから永久に取り除く。売却は無い
            if (removeMode == "shop" && act.Screen == ActScreen.Shop) DrawRemovePicker(w, h, i => { bool ok = act.Run.RemoveCardPaidAt(i); act.Checkpoint(); return ok; }, $"デッキ整理：{act.Run.RemovePrice} ルーン片を払い、選んだ1枚をデッキから削除します", $"（-{act.Run.RemovePrice} ルーン片）");
            else if (removeMode == "rest" && act.Screen == ActScreen.Rest) DrawRemovePicker(w, h, i => { bool ok = act.Run.RestRemoveAt(i); if (ok) { act.Run.MarkRest("remove"); act.Checkpoint(); } return ok; }, "削除：選んだ1枚をデッキから削除します（無料）", "（無料）");
            else if ((removeMode == "forge" && act.Screen == ActScreen.Rest) || (removeMode == "forge_shop" && act.Screen == ActScreen.Shop) || (removeMode == "forge_reward" && act.Screen == ActScreen.Reward)) DrawForgePicker(w, h); // 【完成版】鍛える【RC6】商人・中ボスの報酬でも
            else if (removeMode == "swap" && (act.Screen == ActScreen.Reward || act.Screen == ActScreen.Shop))
                DrawRemovePicker(w, h, i => { bool ok = act.Run.SwapRemoveAt(i); act.Checkpoint(); return ok; }, "入れ替え：新しい命令札の代わりに、デッキから1枚外す（外さなくてもよい）", "（無料）", null, "外す", act.Run.Deck.Count - 1); // 【完成版】NEW＝今取った札
            else if (removeMode == "tswap" && (act.Screen == ActScreen.Reward || act.Screen == ActScreen.Shop))
                DrawRemovePicker(w, h, i => { bool ok = act.Run.TacticSwapRemoveAt(i); act.Checkpoint(); return ok; }, "入れ替え：新しい戦術札の代わりに、戦術札を1枚外す（外さなくてもよい）", "（無料）", act.Run.TacticCards, "外す", act.Run.TacticCards.Count - 1);
            else if (removeMode == "boon" && act.Screen == ActScreen.Boon && pendingBoon != null)
            {
                var b = pendingBoon;
                DrawRemovePicker(w, h, i => { act.ChooseBoon(b, i); return true; }, $"旅立ち：選んだ1枚をデッキから削除し、ルーン片 +{ActContent.BoonTrimShards}", $"（+{ActContent.BoonTrimShards} ルーン片）");
            }
            else if (removeMode != null && act.Screen != ActScreen.Shop && act.Screen != ActScreen.Rest && act.Screen != ActScreen.Boon && !((removeMode == "swap" || removeMode == "tswap") && act.Screen == ActScreen.Reward)) removeMode = null;
            if (showDeck && act.Run != null) DrawDeckOverlay(w, h);
            if (showSettings && act.Screen != ActScreen.Title) DrawSettings(w, h); // 【RC5・レビュー】ランの途中でも音量を変えられる（前はタイトルだけ）
            DrawFestivalIdleOverlay(w, h); // 【文化祭】展示の PC：まだ遊んでいますか？
            KeyboardEnd();
        }

        // ───── 共通 ─────

        /// <summary>
        /// 【通常リリース版・画面の直し 12】画面の見出しの下の文と同じことを言うヒント：D1 扉（扉の先が少し見える…）・F1 報酬（命令札3枚から1枚…）・D3 商人（札・アイテム・刻印・強化…）・D5 休息（回復・強化・削除…）。
        /// 新しいことを言うヒント（HP が少ない D6・中ボス D4・次はボス D7・刻印 I1）は出す。
        /// </summary>
        static readonly HashSet<string> SameAsSubtitleHints = new HashSet<string> { "D1", "F1", "D3", "D5" };

        /// <summary>【検証中】ヒントの帯（1行・× で閉じると二度と出ない）。</summary>
        void DrawHint(Rect r, int fontSize)
        {
            var hint = act.CurrentHint();
            if (hint == null) return;
            if (SameAsSubtitleHints.Contains(hint.Value.Key)) return; // 【通常リリース版・画面の直し 12】見出しの下の文と同じことを言うヒント（扉・報酬・商人・休息）は出さない
            DrawRect(r, new Color(0.05f, 0.12f, 0.1f, 0.94f));
            DrawFrame(r, new Color(0.45f, 0.95f, 0.7f, 0.9f), 1.5f);
            ShadowLabel(new Rect(r.x + 10, r.y + 1, r.width - 66, r.height - 2), WrapJa("ヒント：" + hint.Value.Value, new GUIStyle(Small) { fontSize = fontSize, wordWrap = false }, r.width - 66), new GUIStyle(Small) { fontSize = fontSize, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft }, new Color(0.75f, 1f, 0.85f)); // 【RC5・最終検証】禁則（行末の「（」）
            var x = new Rect(r.xMax - 52, r.y + 4, 46, 26);
            DrawRect(x, new Color(1f, 1f, 1f, x.Contains(Event.current.mousePosition) ? 0.25f : 0.12f));
            ShadowLabel(x, "×", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Color.white);
            if (GUI.Button(x, GUIContent.none, GUIStyle.none)) { act.DismissHint(hint.Value.Key); lastClickAt = Time.unscaledTime; }
        }

        void DrawBackground(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0.03f, 0.035f, 0.06f, 1f));
            var bg = BackgroundTexture();
            if (bg != null)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, w, h), bg, ScaleMode.ScaleAndCrop);
                GUI.color = old;
            }
        }

        Texture2D bgTex;
        Texture2D BackgroundTexture()
        {
            if (bgTex != null || uiTheme == null) return bgTex;
            var prefab = uiTheme.Get("background");
            var sr = prefab != null ? prefab.GetComponent<SpriteRenderer>() : null;
            bgTex = sr != null && sr.sprite != null ? sr.sprite.texture : null;
            return bgTex;
        }

        void Title(float w, float y, string text, string sub = null)
        {
            ShadowLabel(new Rect(0, y, w, 44), text, new GUIStyle(Big) { fontSize = 30, alignment = TextAnchor.MiddleCenter }, Gold);
            if (sub != null)
            {
                var ss = new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.85f, 0.87f, 0.95f) } };
                var subLines = WrapJa(sub, ss, w * 0.8f).Split('\n'); // 【通常リリース版・画面の直し 16】単語の途中で改行しない（禁則・文節）
                for (int i = 0; i < subLines.Length; i++) GUI.Label(new Rect(w * 0.1f, y + 42 + i * 20, w * 0.8f, 22), subLines[i], ss); // 【第2巡 9】1行ずつ 20px の間（行がくっついていた）
            }
        }

        bool Button(Rect r, string label, bool gold = false, bool enabled = true, int size = 15)
        {
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            DrawUi(r, gold ? "button_gold" : hover ? "button_hover" : "button_normal", gold ? new Color(0.6f, 0.45f, 0.15f) : new Color(0.15f, 0.18f, 0.28f), enabled ? 1f : 0.4f);
            ShadowLabel(r, label, new GUIStyle(Label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true },
                enabled ? (gold ? new Color(0.15f, 0.08f, 0.02f) : Color.white) : new Color(1f, 1f, 1f, 0.4f));
            if (!enabled) return false;
            if (KeyboardHit(r, gold) || GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Play(Sfx.Kind.Button); lastClickAt = Time.unscaledTime; return true; } // 【RC5】キーボードでも押せる
            return false;
        }

        /// <summary>上の帯：HP・ルーン片・アイテム・刻印・デッキ。</summary>
        /// <summary>【RC6】数の増減の演出（上の帯の下に「+N」が出て消える）。</summary>
        struct DeltaState { public int last; public int delta; public float at; public bool init; }
        DeltaState fxShards, fxDeck;

        void DeltaFx(ref DeltaState s, int now, Rect r)
        {
            if (!s.init) { s.init = true; s.last = now; }
            if (now != s.last) { s.delta = now - s.last; s.at = Time.unscaledTime; s.last = now; }
            float t = Time.unscaledTime - s.at;
            if (s.delta == 0 || t > 1.6f) return;
            float a = t < 1.1f ? 1f : 1f - (t - 1.1f) / 0.5f;
            var rr = new Rect(r.x, r.y + t * 10f, r.width, r.height);
            ShadowLabel(rr, (s.delta > 0 ? "+" : "") + s.delta, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false }, s.delta > 0 ? new Color(0.55f, 1f, 0.9f, a) : new Color(1f, 0.6f, 0.5f, a));
        }

        /// <summary>【第2巡 9】強化の例（今の強化の形：V4Content.UpgradePreview）。デッキの斬撃（無ければ強化できる札）で。</summary>
        string UpgradeExample()
        {
            var deck = act.Run != null ? act.Run.Deck : null;
            var c = deck == null ? null : deck.FirstOrDefault(d => d != null && d.type == CommandType.Slash && d.copyDamageCut == 0 && !d.copyWide && !d.copyMirrored) ?? deck.FirstOrDefault(d => d != null && V4Content.CanUpgradeAny(d));
            return c != null ? V4Content.UpgradePreview(c) : "札を選ぶ前に、前 → 後 が出る";
        }

        void DrawStatus(float w)
        {
            var run = act.Run;
            var r = new Rect(10, 8, w - 20, 40);
            DrawUi(r, "panel", PanelBlue);
            var st = new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            float x = r.x + 12;
            ShadowLabel(new Rect(x, r.y, 110, r.height), $"{run.Role.Name}", st, Gold); x += 110;
            ShadowLabel(new Rect(x, r.y, 72, r.height), $"{Mathf.Min(run.Floor + 1, run.FloorCount)}/{run.FloorCount}階", st, Color.white); x += 80; // 【第2巡 5】どの画面でも今の階（報酬・休息・商人）
            ShadowLabel(new Rect(x, r.y, 110, r.height), $"HP {run.Hp}/{run.MaxHp}", st, new Color(0.5f, 0.75f, 1f));
            var bar = new Rect(x, r.y + 30, 96, 5);
            DrawRect(bar, new Color(0, 0, 0, 0.6f));
            DrawRect(new Rect(bar.x, bar.y, bar.width * run.Hp / Mathf.Max(1, run.MaxHp), bar.height), new Color(0.3f, 0.6f, 1f));
            x += 116;
            int shownShards = run.Shards;
            if (act.Screen == ActScreen.Reward && run.V4Run && run.LastRewardShards > 0) { float ra = RewardAnimTime(run); if (ra < 0.6f) shownShards = run.Shards - run.LastRewardShards + Mathf.RoundToInt(run.LastRewardShards * Mathf.Clamp01(ra / 0.6f)); } // 【RC6・レビュー】真ん中の数え上げと同じ数
            if (DrawUiImage(new Rect(x - 4, r.y + 9, 22, 22), "hz_rune", 1f, ScaleMode.ScaleToFit)) x += 18; // 【RC6・レビュー「ルーン片に絵が無い」】
            ShadowLabel(new Rect(x, r.y, 120, r.height), $"ルーン片 {shownShards}", st, new Color(0.6f, 0.95f, 1f));
            if (act.Screen == ActScreen.Reward) fxShards.last = shownShards; else // 【RC11・初見テスト「左上の +1 が何の数字か分からない」】報酬の画面は真ん中の数え上げだけ（上の帯の +1 が数え上げのたびに出ていた）
            DeltaFx(ref fxShards, shownShards, new Rect(x + 40, r.yMax - 4, 80, 20)); // 【RC6】増えたら「+18」・使ったら「-35」（報酬の画面では数え上げと同じ数：レビュー2回目）
            x += 120;
            string items = run.Items.Count == 0 ? "なし" : string.Join("・", run.Items.Select(i => ActContent.Item(i).Name));
            ShadowLabel(new Rect(x, r.y, 230, r.height), $"アイテム {run.Items.Count}/{ActContent.ItemSlots}：{items}", new GUIStyle(st) { fontSize = 12 }, Color.white); x += 236;
            string imps = run.Imprints.Count == 0 ? "なし" : string.Join("・", run.Imprints.Select(i => ActContent.Imprint(i).Name));
            var impRect = new Rect(x, r.y, Mathf.Max(60f, r.xMax - x - 196), r.height); // 【RC7】設定のボタン（xMax-188）と重ねない
            if (!(run.V4Run && run.Imprints.Count == 0)) // 【RC7】完成版の刻印も（持っていないときは出さない）
                ShadowLabel(impRect, $"刻印：{imps}", new GUIStyle(st) { fontSize = 12, clipping = TextClipping.Clip }, new Color(0.85f, 0.7f, 1f));
            bool impHover = run.Imprints.Count > 0 && impRect.Contains(Event.current.mousePosition);
            if (Button(new Rect(r.xMax - 122, r.y + 5, 116, 30), run.V4Run ? $"デッキ {run.Deck.Count}＋戦術{run.TacticCards.Count}" : $"デッキ {run.Deck.Count}枚", false, true, 12)) showDeck = !showDeck;
            DeltaFx(ref fxDeck, run.Deck.Count, new Rect(r.xMax - 122, r.yMax - 4, 60, 20)); // 【RC6】デッキが増えた・減った
            if (run.V4Run && Button(new Rect(r.xMax - 188, r.y + 5, 60, 30), "設定", false, true, 12)) showSettings = true; // 【RC5】音量・画面の揺れ
            if (impHover)
            {
                // 【RC7】刻印の説明（上の帯にマウス）
                var lines = run.Imprints.Select(i => { var d = ActContent.Imprint(i); return $"{d.Name}：{d.Description}"; }).ToList();
                var tst = new GUIStyle(Small) { fontSize = 13, wordWrap = true };
                float tw = 520f, th = tst.CalcHeight(new GUIContent(string.Join("\n", lines)), tw - 24) + 40;
                var tr = new Rect(Mathf.Min(impRect.x, w - tw - 8), r.yMax + 4, tw, th);
                DrawUi(tr, "panel", PanelBlue);
                ShadowLabel(new Rect(tr.x + 12, tr.y + 6, tw - 24, 20), "刻印（ランの間ずっと効く）", new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold }, new Color(0.85f, 0.7f, 1f));
                GUI.Label(new Rect(tr.x + 12, tr.y + 30, tw - 24, th - 34), string.Join("\n", lines), tst);
            }
        }

        void DrawDeckOverlay(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0.01f, 0.012f, 0.025f, 0.93f)); // 【RC5・最終ゲート】後ろの扉の文が透けて読みにくかった【第2巡 6】0.8 → 0.93
            var r = new Rect(w * 0.08f, 56, w * 0.84f, h - 80);
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 1f)); // 【第2巡 6】欄は不透明（窓の絵は半透明）
            DrawUi(r, "panel", PanelBlue);
            ShadowLabel(new Rect(r.x + 16, r.y + 8, r.width, 24), $"デッキ（{act.Run.Deck.Count}枚）　クリックか Esc で閉じる", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            if (act.Run.V4Run)
            {
                // 【Combat V4】上：命令札／下：戦術札（緑）
                DrawCardGrid(new Rect(r.x + 16, r.y + 40, r.width - 32, (r.height - 50) * 0.66f), act.Run.Deck, null);
                float ty = r.y + 40 + (r.height - 50) * 0.66f + 8;
                ShadowLabel(new Rect(r.x + 16, ty, r.width, 20), $"戦術札（{act.Run.TacticCards.Count}枚・別のデッキ・命令枠を使わない）", new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold }, new Color(0.3f, 0.85f, 0.5f));
                var tc = act.Run.TacticCards;
                float tw = Mathf.Min(150, (r.width - 32 - 6 * 9) / Mathf.Max(6, Mathf.Min(10, tc.Count)));
                int trows = Mathf.Max(1, (tc.Count + 9) / 10); // 【RC5・レビュー】前は6枚でも高さを2行に割って、札の効果が見えなかった
                float th = (r.yMax - ty - 40) / trows;
                for (int i = 0; i < tc.Count; i++) TacticCardView.Draw(new Rect(r.x + 16 + (i % 10) * (tw + 6), ty + 24 + (i / 10) * th, tw, th - 4), tc[i]);
            }
            else DrawCardGrid(new Rect(r.x + 16, r.y + 40, r.width - 32, r.height - 50), act.Run.Deck, null);
            if (Event.current.type == EventType.MouseDown) { showDeck = false; Event.current.Use(); }
            var dk = Event.current; // 【RC5・レビュー】キーボードだけでは閉じられなかった（閉じ込め）
            if (dk.type == EventType.KeyDown && (dk.keyCode == KeyCode.Escape || dk.keyCode == KeyCode.Return || dk.keyCode == KeyCode.KeypadEnter || dk.keyCode == KeyCode.Space)) { showDeck = false; dk.Use(); }
        }

        /// <summary>
        /// 【RC2】報酬・商人の命令札の上に、敵のまねがあなたと比べてどうなるかの一言（読んで選べるように）。
        /// 敵だけが動く／まねされない／まねは弱い／敵の方が強い／同じ攻撃（あなたにも当たる）／同じ動き。
        /// </summary>
        static (string text, Color color) CopyBadge(CommandData d)
        {
            if (d == null || d.type == CommandType.Tactic) return (null, Color.white);
            if (d.selfInert) return ("敵だけが動く", new Color(0.45f, 0.9f, 1f));
            if (d.notCopied) return ("まねされない", new Color(0.55f, 0.95f, 0.65f));
            if (d.copyDamageCut > 0 || d.copyDistanceCut > 0) return ("まねは弱い", new Color(0.55f, 0.95f, 0.65f));
            if (d.copyDistanceCut < 0 && d.damage <= 0) return ("敵の方が遠くへ動く", new Color(0.45f, 0.9f, 1f)); // 【RC5・レビュー4】追い風（移動の札）に「敵どうしに」は合わなかった
            if (d.copyWide && d.copyDamageCut >= 0 && d.copyDistanceCut >= 0) return ("敵の方が広い（敵どうしに）", new Color(0.45f, 0.9f, 1f)); // 【RC11・QA】合わせ斬り：見出し「強い」・本文「広い」が合わなかった
            if (d.copyDamageCut < 0 || d.copyDistanceCut < 0 || d.copyWide) return ("敵の方が強い（敵どうしに）", new Color(0.45f, 0.9f, 1f)); // 【RC2】敵に渡して利用する札は、まね成功と同じ水色
            if (d.type == CommandType.Heal) return ("敵も回復する", new Color(1f, 0.7f, 0.4f));
            if (d.type == CommandType.Spirit) return ("敵も強くなる", new Color(1f, 0.7f, 0.4f));
            if (d.damage > 0) return ("同じ攻撃（あなたにも）", new Color(1f, 0.55f, 0.55f));
            return ("同じ動き", new Color(0.75f, 0.85f, 1f));
        }

        static void DrawCopyBadge(Rect card, CommandData d)
        {
            var (text, col) = CopyBadge(d);
            if (text == null) return;
            var r = new Rect(card.x, card.y - 22, card.width, 20);
            DrawRect(r, new Color(0.05f, 0.06f, 0.1f, 0.85f));
            var st = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = col } };
            // 【RC2】商人の狭いカードでは文字がはみ出して隣と重なった → 収まらなければ「まね：」を省き、それでも長ければ小さく
            string label = "まね：" + text;
            if (st.CalcSize(new GUIContent(label)).x > r.width - 4) st.fontSize = 10; // 【RC5・最終ゲート】先に小さく（「まね：」を省くと札の効果に読めた）
            if (st.CalcSize(new GUIContent(label)).x > r.width - 4) st.fontSize = 9;
            if (st.CalcSize(new GUIContent(label)).x > r.width - 4) label = "ま：" + text;
            GUI.Label(r, label, st);
        }

        /// <summary>【検証中】カードの右上にレア度（★〜★★★）。</summary>
        void DrawRarity(Rect r, CommandData c)
        {
            // 【RC2】完成版の抽選のレア度を出す（前は古い表のレア度で、抽選と食い違っていた：レビュー）
            var info = (act != null && act.Run != null && act.Run.V4Run ? V4Content.FinalCommandPool.Concat(V4Content.FinalTacticPool).FirstOrDefault(i => i.Asset == c.name) : null) ?? ActContent.Info(c.name);
            if (info == null) return;
            var col = info.Rarity == Rarity.Rare ? new Color(1f, 0.78f, 0.3f) : info.Rarity == Rarity.Uncommon ? new Color(0.65f, 0.85f, 1f) : new Color(0.8f, 0.8f, 0.8f);
            ShadowLabel(new Rect(r.xMax - 64, r.y + 4, 58, 16), info.RarityMark, new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleRight, wordWrap = false }, col);
        }

        /// <summary>【RC6】札の強化の見た目（自分＋の数・まね±の数）。</summary>
        (int forge, int copyDelta) UpgradeLook(CommandData c)
        {
            var k = act.Run?.UpgradeOf(c.name);
            if (k == null) return (0, 0);
            return k == UpgradeKind.Self ? (DeckState.ForgeBonus, 0) : k == UpgradeKind.CopyUp ? (c.selfInert ? 0 : DeckState.CopyUpSelf, DeckState.CopyUpgrade) : (DeckState.CopyDownSelf, -DeckState.CopyDowngrade);
        }

        /// <summary>カードを並べる。onClick があればクリックで選べる。</summary>
        void DrawCardGrid(Rect area, IList<CommandData> cardsList, System.Action<int> onClick, int highlight = -1, int newIndex = -1, bool previewForge = false)
        {
            int n = cardsList.Count;
            if (n == 0) return;
            int perRow = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(n * area.width / area.height * 0.75f)), 4, 10);
            int rows = Mathf.CeilToInt(n / (float)perRow);
            float gap = 6;
            float cw = (area.width - gap * (perRow - 1)) / perRow;
            float ch = Mathf.Min(cw * 1.3f, (area.height - gap * (rows - 1)) / rows);
            cw = Mathf.Min(cw, ch / 1.3f);
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(area.x + (i % perRow) * (cw + gap), area.y + (i / perRow) * (ch + gap), cw, ch);
                if (cardsList[i].type == CommandType.Tactic) { TacticCardView.Draw(r, cardsList[i], true); if (i == highlight) DrawFrame(r, Gold, 3f); } // 【完成版】戦術札の入れ替え
                else { var (fg, cd) = UpgradeLook(cardsList[i]); DrawCardV2(r, cardsList[i], i == highlight, false, 0, true, 1f, 0, previewForge ? 0 : fg, previewForge ? 0 : cd); } // 【RC5】鍛えた札はデッキの一覧でも【RC6】強化の3種類
                if (i == newIndex) { var tag = new Rect(r.xMax - 50, r.y + 30, 44, 18); DrawRect(tag, new Color(0.85f, 0.65f, 0.1f, 0.95f)); GUI.Label(tag, "NEW", new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } }); } // 【RC2】今取った札 // 【RC5・レビュー】NEW の印が効果の文を隠していた → 絵の右上へ
                if (onClick != null && (KeyboardHit(r, false) || GUI.Button(r, GUIContent.none, GUIStyle.none))) onClick(i); // 【RC5】キーボードでも選べる
            }
        }

        // ───── タイトル・役職 ─────

        bool showHowTo;

        /// <summary>【戦闘中心 v3】外部テスト版のタイトル：はじめる・続きから・遊び方・終了だけ（検証用の入口は出さない）。</summary>
        void DrawTesterTitle(float w, float h)
        {
            Title(w, h * 0.13f, "使った技は敵のもの（仮）", "デッキ構築 × ローグライク × 戦術バトル。あなたが使った命令を、敵もそのまままねする。\n敵は自分でも攻撃してくる。その危険な大技を、まねと位置と戦術札で、逆に利用して戦おう。");
            float bw = 320, x = (w - bw) / 2, y = h * 0.40f;
            if (!showHowTo && Button(new Rect(x, y, bw, 56), "はじめる", true, true, 22)) act.OpenV4RunRoleSelect(); // 【Combat V4】
            string save = act.SaveDescription;
            if (save != null && !showHowTo)
            {
                if (!showHowTo && Button(new Rect(x, y + 68, bw, 42), "続きから", false, true, 16)) act.ContinueRun();
                GUI.Label(new Rect(x - 120, y + 112, bw + 240, 20), save, new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.UpperCenter, wordWrap = false });
            }
            float y2 = y + (save != null ? 140 : 72);
            if (!showHowTo && Button(new Rect(x, y2, bw, 42), "遊び方（1分で読める）", false, true, 16)) showHowTo = true;
            if (!showHowTo && Button(new Rect(x + bw / 4, y2 + 56, bw / 2, 36), "ゲームを終了", false, true, 14)) Application.Quit();
            if (act.Message != null) GUI.Label(new Rect(0, y - 30, w, 22), act.Message, new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.6f, 0.6f) } });
            var p = act.Profile;
            string rec = p.runsFinished == 0 ? "はじめての方は「遊び方」を先にどうぞ（いつでもゲーム中の「凡例」で確認できる）" : $"記録：ラン {p.runsFinished} 回・ACT1 クリア {p.clears} 回";
            ShadowLabel(new Rect(0, h - 76, w, 22), rec, new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter }, new Color(0.75f, 0.85f, 1f));
            if (!showHowTo && Button(new Rect(w - 200, h - 50, 180, 30), p.hintsOff ? "ヒント OFF" : "ヒント ON", false, true, 12)) act.SetHintsOff(!p.hintsOff);
            GUI.Label(new Rect(20, h - 44, w - 240, 40), $"開発中のプレイテスト版 {BuildInfo.Version}（仮アート・仕様は変わります）。1ラン 25〜40 分の想定。\nプレイの記録（個人情報なし）は、ゲームのフォルダの PlaytestLogs に残ります。送るかどうかは自由です。",
                new GUIStyle(Tiny) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } });
            if (!act.Profile.howToSeen && Event.current.type == EventType.Layout) { act.Profile.howToSeen = true; act.SaveProfile(); showHowTo = true; } // 【RC2】はじめて起動したときは、遊び方を先に見せる
            if (showHowTo) DrawHowTo(w, h);
        }

        /// <summary>【戦闘中心 v3】遊び方（1枚）。クリックで閉じる。</summary>
        void DrawHowTo(float w, float h)
        {
            var r = new Rect(w * 0.08f, 40, w * 0.84f, h - 80);
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.92f)); // 【第2巡 6】0.75 → 0.92
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 1f)); // 【RC5・レビュー】後ろのタイトルの文字が透けていた
            DrawUi(r, "panel", PanelBlue);
            ShadowLabel(new Rect(r.x + 20, r.y + 12, r.width - 40, 30), "遊び方（クリックか Enter で閉じる）", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold }, Gold);
            var lines = BuildInfo.ExternalPlaytest || act.NextRunV4 || (act.Run != null && act.Run.V4Run) ? FinalHowToLines() : new[]
            {
                "1. 手札から最大3枚を選んで「命令」を組み、向きを決めて「実行」。カードの色：青＝移動・赤＝攻撃・琥珀＝押す/引く。",
                "2. あなたの命令が終わると、敵が A→B→C の順に1体ずつ、同じ命令を「自分の位置から・同じ向き・同じ距離」でまねる（まね）。",
                "　 敵のまね攻撃は敵にも当たる。並んだ敵の前で空振りすれば、敵が敵を斬る。柱で自分だけ止まれば、敵だけ進む。",
                "3. まね方は敵ごとに違う：∞＝全部まねる／剣の印＝技だけ（歩かない砲台）／足あとの印＝移動だけ（運べる駒）。",
                "4. 赤いマス＝敵の今の攻撃範囲（動く前）。敵があなたの移動をまねて動くと、範囲も一緒にずれる。紫＝ボス・仕掛けの予告。カードを選ぶと、まねる敵と向きが水色で出る。",
                "5. 普通に殴っても勝てる。でもまねを使うと、速く・安全に勝てる（ゲーム中の「凡例」「詳細」、敵にマウスを乗せると説明）。",
                "6. 扉を選んで進む（8階）。HP は次の部屋へ持ち越し。勝つとカード・ルーン片。商人で購入・削除、祠で回復か削除。",
                "7. 手札整理：1戦1回、手札を2枚まで捨てて引き直せる（命令の枠は使わない）。欲しい種類の札が無いときに。",
                "8. 試練の部屋は短いパズル：決まったターン数以内に解けば珍しい札。時間切れ・あきらめは負けではない（報酬が無いだけ）。ただし HP が 0 になれば負け。",
                "操作：マウス／1〜9 でカード・矢印かWASDで向き・Enter で実行・Backspace で1つ戻す・Esc で取り消し・Tab で詳細。",
            };
            var st = new GUIStyle(Small) { fontSize = 19, wordWrap = true };
            // 【RC5・レビュー3】文字を大きく（前は 15 で欄の半分しか使っていなかった）：欄に入る一番大きい大きさ（19〜14）
            while (st.fontSize > 14 && lines.Sum(l => st.CalcHeight(new GUIContent(l), r.width - 48) + 8 + (l.Contains("\t") ? 34 : 0)) > r.height - 70) st.fontSize--;
            float y = r.y + 54;
            foreach (var l0 in lines)
            {
                string l = l0;
                int tab = l.IndexOf('\t');
                if (tab > 0)
                {
                    // 【通常リリース版・画面の直し 11】見出し（金色）＋本文 2〜3行
                    ShadowLabel(new Rect(r.x + 24, y, r.width - 48, 30), l.Substring(0, tab), new GUIStyle(Label) { fontSize = st.fontSize + 3, fontStyle = FontStyle.Bold, wordWrap = false }, Gold);
                    y += 32;
                    l = l.Substring(tab + 1);
                }
                var stn = new GUIStyle(st) { wordWrap = false };
                string wl = WrapJa(l, stn, r.width - 48); // 【RC5・レビュー4】禁則（「。」で始まる行が出ていた）
                float hh = stn.CalcHeight(new GUIContent(wl), r.width - 48);
                GUI.Label(new Rect(r.x + 24, y, r.width - 48, hh), wl, stn);
                y += hh + (tab > 0 ? 18 : 8);
            }
            var hev = Event.current; // 【RC5】キーでも閉じる（Esc・Enter・Space）
            if (hev.type == EventType.MouseDown || (hev.type == EventType.KeyDown && (hev.keyCode == KeyCode.Escape || hev.keyCode == KeyCode.Return || hev.keyCode == KeyCode.KeypadEnter || hev.keyCode == KeyCode.Space))) { showHowTo = false; hev.Use(); }
        }

        void DrawTitle(float w, float h)
        {
            // 【RC4・本人の RC3 実プレイ「チュートリアルが無い」】Editor・開発版の「再生」も、公開版と同じタイトルから（はじめての案内・チュートリアル）。
            //   前は開発用の研究室のタイトルが出て、チュートリアルの入口が無かった。研究室・検証ステージは【開発】ボタンを押したときだけ
            if (BuildInfo.ExternalPlaytest || !ShowDevTitle) { DrawFinalTitle(w, h); return; }
            if (Button(new Rect(20, 20, 260, 36), "← 公開版と同じタイトルへ", false, true, 13)) { ShowDevTitle = false; return; }
            Title(w, h * 0.16f, "使った技は敵のもの（仮）", "あなたの命令は、そのまま敵にまねされる。\nACT1 縦切り版【検証中】：古代遺跡の扉を選びながら、鏡守の騎士を目指す。");
            float bw = 320, x = (w - bw) / 2, y = h * 0.42f;
            if (Button(new Rect(x, y - 66, bw, 52), "完成版 ACT1（RC）", true, true, 18)) act.OpenV4RunRoleSelect(); // 【完成版】
            if (Button(new Rect(x + bw + 20, y - 66, 250, 40), "公開版のタイトルを見る\n（戻すには再起動）", false, true, 11)) { BuildInfo.SetForTest(true); }
            if (Button(new Rect(x - 270, y - 66, 250, 40), "Combat V4 研究室（部屋を1つずつ）", false, true, 12)) act.OpenV4Lab();
            if (Button(new Rect(x, y, bw, 52), "ACT1 を始める（古い縦切り版）", false, true, 16)) act.OpenRoleSelect();
            string save = act.SaveDescription;
            if (save != null && Button(new Rect(x - bw * 0.6f - 16, y + 6, bw * 0.6f, 40), "続きから", false, true, 15)) act.ContinueRun();
            if (save != null) GUI.Label(new Rect(x - bw * 0.6f - 40, y + 48, bw * 0.6f + 48, 20), save, new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.UpperCenter, wordWrap = false });
            if (act.Message != null) GUI.Label(new Rect(0, y - 30, w, 22), act.Message, new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.6f, 0.6f) } });
            int vs = act.Turn.Stages.ToList().FindIndex(s => s != null && s.name == "StageVS");
            if (Button(new Rect(x, y + 66, bw, 40), "見本ステージで遊ぶ（VS・1戦）", false, true, 15)) act.ExitToStages(Mathf.Max(0, vs));
            if (Button(new Rect(x, y + 114, bw, 40), "検証ステージ（STAGE 1〜・V 系・ボス）", false, true, 15)) act.ExitToStages(0);
            if (Button(new Rect(x + bw / 4, y + 172, bw / 2, 34), "ゲームを終了", false, true, 13)) Application.Quit();
            // 【検証中・Copy Core V2】コピーの再設計のサンドボックス（ACT1 には入れていない）
            if (Button(new Rect(x + bw + 20, y + 66, 250, 40), "まね V3 研究室（部屋を1つずつ）", false, true, 13)) act.OpenCopyLabV3();
            if (Button(new Rect(x + bw + 20 + 260, y, 200, 52), "パズル研究室\n（部屋を1つずつ）", false, true, 13)) act.OpenPuzzleLab();
            if (Button(new Rect(x + bw + 20, y + 114, 250, 40), "Copy Core V2（反転まねの研究）", false, true, 12)) act.OpenCopyLab();
            // 【検証中・Copy Core V3】コピー中心のミニ ACT1（2026-10-05）
            if (Button(new Rect(x + bw + 20, y + 162, 250, 34), "まね中心 ACT1（V3 試作）", false, true, 13)) act.OpenCopyRunRoleSelect();
            // 【戦闘中心 v3（2026-10-06）】戦闘が主役・コピーは強い武器（外部テスト版はこれだけ）
            if (Button(new Rect(x + bw + 20, y, 250, 52), "戦闘中心 ACT1 v3（前の版）", false, true, 15)) act.OpenCombatRunRoleSelect();
            if (Button(new Rect(x + bw + 20 + 260, y + 66, 200, 40), "パズル中心 ACT1 v2", false, true, 13)) act.OpenPuzzleRunRoleSelect();
            if (Button(new Rect(x + bw + 20 + 260, y + 114, 200, 40), "戦闘研究室（v3 の部屋）", false, true, 13)) act.OpenCombatLab();
            if (Button(new Rect(x - 230, y + 114, 200, 40), "外部テスト版の画面を見る\n（戻すには再起動）", false, true, 11)) { BuildInfo.SetForTest(true); }
            GUI.Label(new Rect(x + bw + 20, y + 200, 250, 76), "戦闘中心 ACT1 v3：8階・ボス「鏡の主」。普通に戦っても勝てる。敵のまねを使うと圧倒的に上手く戦える（同士討ち・砲台・搬送・共鳴）。",
                new GUIStyle(Tiny) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.85f, 0.95f, 1f) } });
            // 【検証中】記録（ランをまたいで残る物：解放した役職・クリア回数）
            var p = act.Profile;
            int unlocked = ActContent.Roles.Count(r => act.IsRoleUnlocked(r));
            string rec = p.runsFinished == 0 ? "記録：まだ無し（1回終えると役職「観測士」が解放される）" : $"記録：ラン {p.runsFinished} 回・ACT1 クリア {p.clears} 回・役職 {unlocked}/{ActContent.Roles.Length} 解放";
            ShadowLabel(new Rect(0, h - 72, w, 22), rec, new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter }, new Color(0.75f, 0.85f, 1f));
            if (Button(new Rect(w - 200, h - 50, 180, 30), p.hintsOff ? "ヒント OFF" : "ヒント ON", false, true, 12)) act.SetHintsOff(!p.hintsOff);
            GUI.Label(new Rect(0, h - 40, w, 24), "仮アート・仮 UI。ACT1 は 7 部屋（通常・通常・休息/商人・危険/通常・危険/特殊・商人/休息・ボス）。1ラン 25〜40 分の想定。",
                new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.5f) } });
        }

        void DrawRoles(float w, float h)
        {
            if (act.NextRunV4) { DrawFinalRoles(w, h); return; } // 【完成版】
            string sub = act.NextRunV4
                ? "役職の能力は小さい：ゲームの核（敵のまねと大技を逆に利用する）は、どの役職も同じ。迷ったら「旅の剣士」。"
                : act.NextRunCombat
                ? "役職で、最初のデッキと出やすい報酬が変わる。あなたの命令が敵にまねられるのは、どの役職も同じ。迷ったら「旅の剣士」。"
                : act.NextRunPuzzle
                ? "パズル中心 ACT1 v2（試作）：役職で最初のデッキと「まねの使い方」が変わる。試作なので全部の役職を選べる。"
                : act.NextRunCopyCentric
                ? "まね中心 ACT1（試作）：役職で最初のデッキと「まねの使い方」が変わる。試作なので全部の役職を選べる。"
                : "役職で、出やすい報酬と最初のデッキが変わる。あなたの命令が敵にまねされるのは、どの役職も同じ。";
            if (act.HasSave) sub += "\n注意：新しく始めると、続きのセーブは消える。";
            Title(w, 20, "役職を選ぶ", sub);
            var roles = act.NextRunV4 ? ActContent.Roles.Where(x => V4Content.RoleIds.Contains(x.Id)).ToArray()
                      : act.NextRunCombat ? ActContent.Roles.Where(x => ActContent.CombatRoleIds.Contains(x.Id)).ToArray() : ActContent.Roles; // 【戦闘中心 v3】3つ
            int perRow = 3;
            float cw = Mathf.Min(380, (w - 80) / perRow), ch = 196, gap = 14;
            float x0 = (w - (cw * perRow + gap * (perRow - 1))) / 2;
            for (int i = 0; i < roles.Length; i++)
            {
                var baseRole = roles[i];
                var role = act.NextRunV4 ? V4Content.Role(baseRole) : act.NextRunCombat ? ActContent.CombatRole(baseRole) : act.NextRunCopyCentric ? ActContent.CopyRunRole(baseRole) : baseRole; // 【Copy Core V3】コピー中心のランの役職
                bool open = act.NextRunCopyCentric || act.IsRoleUnlocked(baseRole);
                var r = new Rect(x0 + (i % perRow) * (cw + gap), 118 + (i / perRow) * (ch + gap), cw, ch);
                bool hover = open && r.Contains(Event.current.mousePosition);
                DrawUi(r, hover ? "panel_red" : "panel", PanelBlue, open ? 1f : 0.45f);
                ShadowLabel(new Rect(r.x + 14, r.y + 8, r.width - 28, 26), role.Name, new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold }, open ? Gold : new Color(0.6f, 0.6f, 0.6f));
                GUI.Label(new Rect(r.x + 14, r.y + 34, r.width - 28, 20), role.Playstyle, new GUIStyle(Small) { fontSize = 12, normal = { textColor = new Color(0.7f, 0.85f, 1f) } });
                if (!open)
                {
                    GUI.Label(new Rect(r.x + 14, r.y + 60, r.width - 28, 60), $"【未解放】\n解放：{ActProfile.UnlockText(role.Id)}", new GUIStyle(Small) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } });
                    continue;
                }
                GUI.Label(new Rect(r.x + 14, r.y + 56, r.width - 28, 54), role.Description, new GUIStyle(Small) { fontSize = 13, wordWrap = true });
                GUI.Label(new Rect(r.x + 14, r.y + 112, r.width - 28, 40), "コツ：" + role.CopyHint, new GUIStyle(Small) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(1f, 0.9f, 0.65f) } });
                int hp = ActContent.StartHp + role.HpDelta;
                var rec = act.Profile.records.FirstOrDefault(x => x.roleId == role.Id);
                string record = rec == null || rec.runs == 0 ? "記録なし" : $"最高 {rec.bestFloor}/{(act.NextRunCombat ? 8 : 7)}階・クリア {rec.clears}";
                GUI.Label(new Rect(r.x + 14, r.yMax - 28, r.width - 28, 20), $"HP {hp}　手札 {role.BaseHand}　{record}", new GUIStyle(Tiny) { fontSize = 12 });
                if (Hit(r)) { Sfx.Play(Sfx.Kind.Button); act.StartRun(baseRole); }
            }
            if (Button(new Rect(20, h - 56, 140, 40), "← タイトル", false)) act.OpenTitle();
            if (!act.NextRunCopyCentric && !BuildInfo.ExternalPlaytest && roles.Any(x => !act.IsRoleUnlocked(x)) && Button(new Rect(w - 220, h - 50, 200, 30), "（検証用）全役職を解放", false, true, 12)) act.UnlockAllRolesForTesting();
        }

        // ───── 【検証中】Copy Core V2 サンドボックス ─────

        void DrawCopyLab(float w, float h)
        {
            Title(w, 18, "Copy Core V2（まねの再設計）", "まねの方式を選んで、まね必須のマイクロステージを遊ぶ。ACT1 は今のまま（方式 A）。");
            var m = CopyCore.SandboxMode ?? CopyMode.MirrorImmediate;
            float y = 96;
            // 方式
            ShadowLabel(new Rect(40, y, 200, 22), "まねの方式", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            float bx = 40;
            foreach (var mode in CopyCore.All)
            {
                var r = new Rect(bx, y + 26, (w - 80 - 10 * (CopyCore.All.Length - 1)) / CopyCore.All.Length, 64);
                bool on = mode == m;
                DrawUi(r, on ? "panel_red" : "panel", PanelBlue);
                if (on) DrawFrame(r, Gold, 2f);
                ShadowLabel(new Rect(r.x + 10, r.y + 4, r.width - 20, 22), CopyCore.Name(mode) + (mode == CopyMode.MirrorImmediate ? "（本人案）" : mode == CopyMode.MimicReflectImmediate ? "（研究の推し）" : mode == CopyMode.Current ? "（ACT1）" : ""), new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold }, on ? Gold : Color.white);
                GUI.Label(new Rect(r.x + 10, r.y + 26, r.width - 20, 36), CopyCore.OneLine(mode), new GUIStyle(Small) { fontSize = 12, wordWrap = true });
                if (Hit(r)) CopyCore.SandboxMode = mode;
                bx += r.width + 10;
            }
            y += 100;
            if (Button(new Rect(40, y, 200, 32), CopyCore.SandboxCopyOff ? "まね OFF（敵はまねない）" : "まね ON", CopyCore.SandboxCopyOff, true, 13)) CopyCore.SandboxCopyOff = !CopyCore.SandboxCopyOff;
            GUI.Label(new Rect(250, y + 6, w - 290, 22), "COPY OFF にすると、敵はあなたの命令を一切使わない（同じステージで比べる用）。戦闘中も手札の上で切り替えられる。", new GUIStyle(Small) { fontSize = 12, wordWrap = false });
            y += 44;
            // ステージ
            var stages = act.SandboxStages();
            float cw = (w - 80 - 20) / 3, ch = 170;
            for (int i = 0; i < stages.Count; i++)
            {
                var (index, st) = stages[i];
                var r = new Rect(40 + (i % 3) * (cw + 10), y + (i / 3) * (ch + 10), cw, ch);
                DrawUi(r, "panel", PanelBlue);
                if (r.Contains(Event.current.mousePosition)) DrawFrame(r, Gold, 2f);
                ShadowLabel(new Rect(r.x + 12, r.y + 8, r.width - 24, 24), st.stageTitle, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
                GUI.Label(new Rect(r.x + 12, r.y + 36, r.width - 24, 84), st.sandboxGoal, new GUIStyle(Small) { fontSize = 13, wordWrap = true });
                if (Button(new Rect(r.x + 30, r.yMax - 44, r.width - 60, 34), "遊ぶ", true, true, 15))
                {
                    if (!CopyCore.SandboxMode.HasValue) CopyCore.SandboxMode = m;
                    act.ExitToStages(index);
                    return;
                }
            }
            if (Button(new Rect(20, h - 56, 140, 40), "← タイトル", false)) act.OpenTitle();
        }

        /// <summary>【検証中・Copy Core V3】コピーの種類の研究室：コピー中心のミニ ACT1 の部屋を1つずつ（COPY ON/OFF で比べる）。</summary>
        void DrawCopyLabV3(float w, float h)
        {
            if (act.LabV4) Title(w, 18, "Combat V4 研究室（V4 の部屋を1つずつ）", "最初のデッキと最初の戦術札で、部屋を1つずつ遊ぶ。敵の大技（あと N）を、まねと戦術札で別の敵へ向けよう。");
            else if (act.LabCombat) Title(w, 18, "戦闘研究室（戦闘中心 ACT1 v3 の部屋）", "部屋を1つずつ遊ぶ。普通に倒しても勝てる。COPY OFF と比べると、まねを使った方がどれだけ楽かが分かる。");
            else if (act.LabPuzzle) Title(w, 18, "パズル研究室（パズル中心 ACT1 v2 の部屋）", "部屋を1つずつ遊ぶ。まね方は「同じ札・同じ向き・同じ距離」。全まね／技まね（歩かない＝砲台）／移動まね（攻撃しない＝駒）。");
            else Title(w, 18, "まね V3 研究室（まねは簡単。使い道は多い）", "まね方は今のまま（同じ札・同じ向き・同じ距離）。敵ごとに何をまねるかが違う：まるまね（印なし）＝全部／手まね＝手の札（青以外）だけ・歩かない／足まね＝足の札（青い移動）だけ。");
            float y = 92;
            if (Button(new Rect(40, y, 220, 32), CopyCore.SandboxCopyOff ? "COPY OFF（敵は写さない）" : "COPY ON", CopyCore.SandboxCopyOff, true, 13)) CopyCore.SandboxCopyOff = !CopyCore.SandboxCopyOff;
            GUI.Label(new Rect(270, y + 6, w - 310, 22), "COPY OFF にすると、敵はあなたの命令を一切写さない（同じ部屋で比べる用）。戦闘中も手札の上で切り替えられる。", new GUIStyle(Small) { fontSize = 12, wordWrap = false });
            y += 44;
            var stages = act.LabStagesV3();
            int perRow = act.LabCombat ? 6 : 4;
            float cw = (w - 80 - 10 * (perRow - 1)) / perRow, ch = act.LabCombat ? 112 : 168;
            for (int i = 0; i < stages.Count; i++)
            {
                var (index, st) = stages[i];
                var r = new Rect(40 + (i % perRow) * (cw + 10), y + (i / perRow) * (ch + 10), cw, ch);
                DrawUi(r, "panel", PanelBlue);
                if (r.Contains(Event.current.mousePosition)) DrawFrame(r, Gold, 2f);
                ShadowLabel(new Rect(r.x + 10, r.y + 6, r.width - 20, 24), st.stageTitle, new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip }, Gold);
                GUI.Label(new Rect(r.x + 10, r.y + 30, r.width - 20, act.LabCombat ? 46 : 90), st.puzzleRoom ? $"目的：{GoalLabel(st.goal)}　{(string.IsNullOrEmpty(st.goalText) ? "" : st.goalText)}" : st.sandboxGoal, new GUIStyle(Small) { fontSize = act.LabCombat ? 11 : 12, wordWrap = true, clipping = TextClipping.Clip });
                if (Button(new Rect(r.x + 24, r.yMax - (act.LabCombat ? 34 : 40), r.width - 48, act.LabCombat ? 26 : 30), "遊ぶ", true, true, 14))
                {
                    CopyCore.SandboxMode = null;
                    if (act.LabV4) act.StartLabV4(index); else act.ExitToStages(index);
                    return;
                }
            }
            if (Button(new Rect(20, h - 56, 140, 40), "← タイトル", false)) act.OpenTitle();
        }

        // ───── 旅立ちの選択 ─────

        void DrawBoon(float w, float h)
        {
            var run = act.Run;
            Title(w, 56, "旅立ちの選択", "1つ選ぶ（選ばなくてもよい）。どれも小さな助けで、敵は変わらない。ランごとに候補が変わる。");
            var opts = run.BoonOptions;
            int n = opts.Count;
            float pw = Mathf.Min(340, (w - 80 - 20 * (n - 1)) / Mathf.Max(1, n)), ph = 360;
            float x0 = (w - (pw * n + 20 * (n - 1))) / 2, y = 150;
            for (int i = 0; i < n; i++)
            {
                var o = opts[i];
                var r = new Rect(x0 + i * (pw + 20), y, pw, ph);
                DrawUi(r, o.Kind == BoonKind.Imprint ? "panel_red" : "panel", PanelBlue);
                if (r.Contains(Event.current.mousePosition)) DrawFrame(r, Gold, 2f);
                float ty = r.y + 14;
                if (o.Kind == BoonKind.Card && o.Card != null)
                {
                    var cr = new Rect(r.center.x - 66, ty, 132, 172);
                    if (o.Card.type == CommandType.Tactic) TacticCardView.Draw(cr, o.Card);
                    else { DrawCardV2(cr, o.Card, false, false, 0, true, 1f, 0); DrawRarity(cr, o.Card); }
                    ty += 180;
                }
                else ty += 30;
                ShadowLabel(new Rect(r.x + 12, ty, r.width - 24, 26), o.Title, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true }, Gold);
                GUI.Label(new Rect(r.x + 16, ty + 32, r.width - 32, 90), o.Text, new GUIStyle(Small) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter });
                if (Button(new Rect(r.x + 40, r.yMax - 52, r.width - 80, 40), o.Kind == BoonKind.Trim ? "削除する札を選ぶ" : "これにする", true, true, 15))
                {
                    if (o.Kind == BoonKind.Trim) { pendingBoon = o; removeMode = "boon"; }
                    else act.ChooseBoon(o);
                    break;
                }
            }
            if (Button(new Rect(w / 2 - 110, h - 64, 220, 44), "選ばずに進む", false)) act.SkipBoon();
        }

        // ───── 扉 ─────

        void DrawDoors(float w, float h)
        {
            var run = act.Run;
            bool oneDoor = run.Doors.Count == 1; // 【通常リリース版・画面の直し 20】扉が1つしかない階で「扉を選ぶ」は合わない
            Title(w, 56, $"{run.Floor + 1}階 / {run.FloorCount}階　{(oneDoor ? "次の部屋へ" : "扉を選ぶ")}", oneDoor ? "この階の扉は1つ。扉の先の敵・仕掛け・難しさが少しだけ分かる。" : "扉の先の敵・仕掛け・難しさが少しだけ分かる（中ボスは報酬が多い）。今のデッキで行きたい方へ。");
            DrawProgress(new Rect(40, 150, w - 80, 26));
            var doors = run.Doors;
            float dw = Mathf.Min(360, (w - 80 - 20 * (doors.Count - 1)) / doors.Count), dh = 400;
            float x0 = (w - (dw * doors.Count + 20 * (doors.Count - 1))) / 2;
            for (int i = 0; i < doors.Count; i++)
                DrawDoor(new Rect(x0 + i * (dw + 20), 190, dw, dh), doors[i]);
            if (act.Message != null) GUI.Label(new Rect(0, h - 40, w, 24), act.Message, new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.6f, 0.6f) } });
        }

        void DrawProgress(Rect r)
        {
            var run = act.Run;
            float cw = r.width / run.FloorCount;
            for (int i = 0; i < run.FloorCount; i++)
            {
                var c = new Rect(r.x + i * cw + 2, r.y, cw - 4, r.height);
                bool done = i < run.Floor, now = i == run.Floor;
                DrawRect(c, done ? new Color(0.3f, 0.45f, 0.7f, 0.8f) : now ? new Color(0.8f, 0.65f, 0.25f, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
                string label = i == run.FloorCount - 1 ? "ボス" : string.Join("/", run.Map[i].Select(KindShort).Distinct());
                GUI.Label(c, $"{i + 1}階 {label}", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = now ? Color.black : Color.white } });
            }
        }

        static string KindShort(RoomDef r) => r.IsMidBoss ? "中ボス" : r.Kind switch
        {
            RoomKind.Battle => "戦闘", RoomKind.Danger => "危険", RoomKind.Boss => "ボス", RoomKind.Rest => "休息", RoomKind.Shop => "商人", RoomKind.Tactical => "仕掛け", RoomKind.Trial => "試練", _ => "特殊",
        };

        void DrawDoor(Rect r, RoomDef room)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            var kindColor = room.Kind switch
            {
                RoomKind.Danger => new Color(1f, 0.45f, 0.3f), RoomKind.Boss => new Color(0.85f, 0.5f, 1f), RoomKind.Rest => new Color(0.4f, 1f, 0.6f),
                RoomKind.Shop => new Color(1f, 0.85f, 0.4f), RoomKind.Event => new Color(0.6f, 0.8f, 1f), RoomKind.Tactical => new Color(1f, 0.75f, 0.35f), RoomKind.Trial => new Color(0.55f, 0.9f, 1f), _ => new Color(0.75f, 0.8f, 0.95f),
            };
            DrawUi(r, room.Kind == RoomKind.Danger || room.Kind == RoomKind.Boss ? "panel_red" : "panel", PanelBlue);
            if (hover) DrawFrame(r, Gold, 2f);
            // 扉の絵
            // 【レビュー】戦闘中心は絵を少し小さくして本文を広く（扉の文がはみ出し、速さの報酬がボタンの下に隠れていた）
            bool compact = room.CombatRun;
            float artH = compact ? 126 : 150, top = compact ? -24 : 0;
            var art = new Rect(r.x + r.width / 2 - 80, r.y + 14, 160, artH);
            // 試練・戦術戦の絵は無いので、通常戦の扉に色を乗せる
            if (!DrawUiImage(art, "door_" + room.Kind.ToString().ToLower(), 1f, ScaleMode.ScaleToFit))
            {
                var prev = GUI.color; GUI.color = Color.Lerp(Color.white, kindColor, 0.55f);
                bool drawn = DrawUiImage(art, "door_battle", 1f, ScaleMode.ScaleToFit);
                GUI.color = prev;
                if (!drawn) DrawRect(art, kindColor * 0.5f);
            }
            ShadowLabel(new Rect(r.x, r.y + 170 + top, r.width, 30), room.Name, new GUIStyle(Label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Gold);
            GUI.Label(new Rect(r.x, r.y + 200 + top, r.width, 20), KindLabel(room), new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = kindColor } });
            var body = new GUIStyle(Small) { fontSize = 13, wordWrap = false };
            var infoRect = new Rect(r.x + 16, r.y + 226 + top, r.width - 32, compact ? r.height - 226 - top - 56 : 120);
            GUI.Label(infoRect, WrapFit(Info(room), body, infoRect, 10, out var infoFit), infoFit); // 【RC5・最終ゲート】禁則【RC6・レビュー】入りきらなければ小さく（獣王の報酬の行が切れていた）
            if (Button(new Rect(r.x + 30, r.yMax - 50, r.width - 60, 38), "この扉へ", true, true, 16)) { roomInfo.Clear(); act.ChooseDoor(room); }
        }

        static string KindLabel(RoomDef r) => r.IsMidBoss ? "中ボス（本体＋護衛・報酬が多い）" : r.Kind switch
        {
            RoomKind.Battle => "通常戦", RoomKind.Danger => "危険戦（報酬が多い）", RoomKind.Boss => "ボス", RoomKind.Rest => "休息",
            RoomKind.Shop => "商人", RoomKind.Tactical => "仕掛けの部屋（" + (r.Id == "v4_t1" ? "動く炎床" : r.Id == "v4_t2" ? "落石" : r.Id == "v4_t3" ? "レーザー" : r.Id == "v4_t4" ? "トゲ" : "レーザー・落石など") + "）", RoomKind.Trial => "試練（短いパズル・時間切れは負けではない）", _ => "特殊な部屋", // 【RC4】トゲの通路にも「レーザー・落石など」と出ていた【RC5・レビュー】この注記が後ろの2つの腕を隠していた（試練などで例外）
        };

        /// <summary>【RC5・最終ゲート】完成版の商人の扉：命令札・戦術札・回復薬・削除の値段と、今のルーン片で買える物。</summary>
        public static string ShopDoorTextV4(string flavor, ActRun run)
        {
            var cmd = V4Content.FinalCommandPool.Where(c => !ActContent.StartDeck.Contains(c.Asset)).Select(V4Content.CommandPrice).ToList(); // 【RC6】完成版の値段
            var tac = V4Content.FinalTacticPool.Select(V4Content.TacticPrice).ToList();
            int potion = ActContent.Item(ItemId.Potion).Price;
            var can = new List<string>();
            if (cmd.Count > 0 && run.Shards >= cmd.Min()) can.Add("命令札");
            if (tac.Count > 0 && run.Shards >= tac.Min()) can.Add("戦術札");
            if (run.Shards >= potion) can.Add("回復薬");
            if (run.Shards >= run.UpgradePrice) can.Add("強化");
            if (run.Shards >= run.RemovePrice) can.Add("削除");
            string range(List<int> p) => p.Count == 0 ? "—" : p.Min() == p.Max() ? $"{p.Min()}" : $"{p.Min()}〜";
            return $"{flavor}\n命令札 {range(cmd)}・戦術札 {range(tac)}・回復薬 {potion}・強化 {run.UpgradePrice}・削除 {run.RemovePrice}\n今 {run.Shards} → 買える物：{(can.Count > 0 ? string.Join("・", can) : "なし")}"; // 【RC5・最終検証】「値段：」を外す（「削/除」で切れていた）【RC6】強化
        }

        /// <summary>扉に書く情報（敵・ギミック・危険度・報酬の傾向）。ステージのデータから作る。</summary>
        string Info(RoomDef room)
        {
            var run = act.Run;
            string key = $"{room.Id}|{run?.Seed}|{run?.Challenge}"; // 【RC5・最終ゲート】ランと挑戦ごと（次のランに前のランの文が残らない）
            bool live = room.Kind == RoomKind.Shop || room.Kind == RoomKind.Rest; // 商人・休息はルーン片・HP で変わる：覚えない（4階の商人の「今 83」が7階にも出ていた）
            if (!live && roomInfo.TryGetValue(key, out var cached)) return cached;
            string text = room.Flavor;
            if (room.Kind == RoomKind.Shop && run != null && run.V4Run) text = ShopDoorTextV4("命令札・戦術札・アイテム・刻印・強化を買える。ルーン片を払って札の削除も", run); // 部屋の一言（前の版）は「刻印を購入」だった // 【RC5・最終ゲート】完成版は刻印を売らない・戦術札は 22 から
            else if (room.Kind == RoomKind.Shop && run != null)
            {
                // 【検証中】2026-10-05：商人の扉に「今のルーン片で買える物」（本人が HP 6・30 で入って回復薬を買えなかったため）
                int cheapCard = ActContent.RandomPool.Where(c => !ActContent.StartDeck.Contains(c.Asset)).Min(c => c.Price);
                var can = new List<string>();
                if (run.Shards >= cheapCard) can.Add("札");
                if (run.Shards >= ActContent.Item(ItemId.Potion).Price) can.Add("回復薬");
                if (run.Shards >= run.RemovePrice) can.Add("削除");
                text = $"{room.Flavor}\n値段：カード {cheapCard}〜65・回復薬 {ActContent.Item(ItemId.Potion).Price}・削除 {run.RemovePrice}・刻印 60〜70\n今 {run.Shards} → 買える物：{(can.Count > 0 ? string.Join("・", can) : "なし")}";
            }
            else if (room.Kind == RoomKind.Rest && run != null)
                text = run.V4Run // 【RC2】鍛えるが書かれていなかった・深度3の回復 5 も反映
                    ? $"回復・強化・削除から{run.RestActions}つ\n回復：HP +{run.RestHealAmount}（{run.Hp} → {Mathf.Min(run.MaxHp, run.Hp + run.RestHealAmount)}）\n強化：攻撃の札1種類をずっと強く\n削除：札を1枚削除（無料）"
                    : $"{room.Flavor}\n休む：HP +{ActContent.RestHeal}（{run.Hp} → {Mathf.Min(run.MaxHp, run.Hp + ActContent.RestHeal)}）／祈る：札を1枚削除（無料）";
            var stage = room.Stage != null ? act.Turn.Stages.FirstOrDefault(s => s != null && s.name == room.Stage) : null;
            if (stage != null)
            {
                var m = BattleModel.FromStage(stage);
                var kinds = room.CopyRun || room.PuzzleRun
                    ? m.Enemies.Where(e => !e.IsObject).GroupBy(e => (e.Kind, e.CopyKind)).Select(g => $"{(g.Key.Kind == EnemyKind.Normal ? "兵" : PhaseRules.KindName(g.Key.Kind))}{(g.Key.CopyKind != CopyKind.All ? "（" + CopyKinds.Short(g.Key.CopyKind) + "）" : "")}×{g.Count()}")
                    : m.Enemies.GroupBy(e => e.Kind).Select(g => $"{PhaseRules.KindName(g.Key)}{(g.Key == EnemyKind.Normal ? "（コピーだけ）" : "")}×{g.Count()}");
                var gimmicks = new List<string>();
                if (m.Board.Spikes.Any()) gimmicks.Add("トゲ");
                if (m.Board.Warps.Count > 0) gimmicks.Add("ワープ陣");
                bool walls = false;
                for (int x = 0; x < m.Board.Width && !walls; x++) for (int y = 0; y < m.Board.Height; y++) if (m.Board.IsWall(new Vector2Int(x, y))) { walls = true; break; }
                if (walls) gimmicks.Add("柱・壁");
                if (m.Environment.HealTiles.Count > 0) gimmicks.Add("回復マス");
                if (stage.firePatterns != null && stage.firePatterns.Count > 0) gimmicks.Add("炎床");
                if (m.Environment.RuneCell.HasValue) gimmicks.Add("回復ルーン");
                if (m.Enemies.Any(e => e.Kind == EnemyKind.Bomber)) gimmicks.Add("爆発");
                if (m.Board.CoreHome.Count > 0) gimmicks.Add("対の鏡核");
                if (m.Board.Crystals.Any()) gimmicks.Add("鏡晶");
                if (m.Board.Gates.Count > 0) gimmicks.Add("踏み板と扉");
                if (m.Board.Seals.Count > 0) gimmicks.Add("封印の円");
                if (m.Board.Barrels.Any()) gimmicks.Add("爆薬樽");
                if (m.Board.Chests.Any()) gimmicks.Add("写し箱");
                if (m.Board.StatueOrder.Count > 0) gimmicks.Add("石像");
                if (m.Board.SealStoneHome.Count > 0) gimmicks.Add("封印石");
                if (m.Board.CarryMarks.Count > 0) gimmicks.Add("運び紋");
                int danger = room.Kind == RoomKind.Boss ? 5 : room.Kind == RoomKind.Danger ? 4 : Mathf.Clamp(m.Enemies.Count() + (gimmicks.Count > 1 ? 1 : 0), 1, 3);
                text = $"敵：{string.Join("・", kinds)}\n仕掛け：{(gimmicks.Count > 0 ? string.Join("・", gimmicks) : "なし")}\n危険度：{new string('★', danger)}{new string('☆', 5 - danger)}" +
                       $"\n報酬の傾向：{TagText(room.RewardTags)}\n{room.Flavor}";
                if (room.V4) // 【Combat V4】一言・コピーの使いどころ・敵・大技・仕掛け・危険度・報酬
                {
                    bool bossRoom = room.IsMidBoss || room.Kind == RoomKind.Boss;
                    var foes = m.Enemies.Skip(bossRoom ? 1 : 0).Where(e => !e.IsObject && e.Kind != EnemyKind.HealClone).GroupBy(e => e.Kind).Select(g => $"{PhaseRules.KindName(g.Key)}×{g.Count()}（{CopyKinds.Short(g.First().CopyKind)}）"); // 【RC6・レビュー】「(全)」では分からない
                    // 【RC5・レビュー3】大技は「名前 何ターンごと」（丸数字は頭の上の「あと何ターン」だけ）。同じ名前の大技を別の兵種が持つときは兵種名を付ける（獣王と追跡兵の飛びかかり）
                    var clockList = m.Enemies.Where(e => e.IsAlive && e.Clocks.Count > 0).SelectMany(e => e.Clocks.Select(c => (kind: e.Kind, name: c.Name, period: c.Period))).Distinct().ToList();
                    var dupClock = new HashSet<string>(clockList.GroupBy(x => x.name).Where(g => g.Select(x => x.kind).Distinct().Count() > 1).Select(g => g.Key));
                    IEnumerable<string> clockNames = clockList.Select(x => (dupClock.Contains(x.name) ? PhaseRules.KindName(x.kind) + "の" : "") + x.name + "（" + x.period + "ターンごと）").Distinct(); // 【通常リリース版・採用担当のレビュー「大技（何ターンごと）：追撃 3 が作りかけの文に見える」】
                    string hazard = !string.IsNullOrEmpty(stage.hazardName) ? stage.hazardName : m.Board.Spikes.Any() ? "トゲ" : walls ? "柱" : "なし";
                    // 【RC3・レビュー】はじめてのランの1階（敵は まねるだけ）：扉にも「剣兵は隣を斬る」「大技：八方斬り」と出ていた
                    if (act.Run != null && act.Run.FirstLesson && act.Run.Floor == 0)
                        return $"はじめての戦闘：敵は あなたの命令を まねるだけ（攻撃しない・近づかない・大技なし）\n敵：{string.Join("・", foes)}\n仕掛け：{hazard}　難しさ：★☆☆☆☆";
                    // 【RC4】はじめてのランの3階（中ボスでない部屋）：大技は無い（扉にも書かない）
                    if (act.Run != null && act.Run.FirstLesson && act.Run.Floor == 2 && !room.IsMidBoss)
                        clockNames = new string[0];
                    // 【RC4】はじめてのランの2階：敵の攻撃（赤）と動きが始まる。大技・戦術札は3階から（扉にも大技を書かない）
                    if (act.Run != null && act.Run.FirstLesson && act.Run.Floor == 1)
                        return $"2階から：敵が攻撃（赤）し、動く（緑の札は3階から・大技は中ボスと5階から）\n{room.Flavor}\nまね：{(string.IsNullOrEmpty(room.LessonTip) ? room.CopyTip : room.LessonTip)}\n敵：{string.Join("・", foes)}\n仕掛け：{hazard}　難しさ：★★☆☆☆";
                    int stars = V4Content.DangerOf(room); // 【RC5・レビュー3】部屋ごとの難しさ（監査の数字から）
                    string reward = room.Kind == RoomKind.Boss ? "" : room.IsMidBoss ? "\n報酬：札を2枚（中ボスの力の戦術札も）＋刻印か強化（無料）" : "\n報酬：命令札3から1枚（追加はルーン片）"; // 【通常リリース版・QA】前は刻印が書かれていなかった（実装：刻印2つから1つ か 無料の強化）
                    // 【通常リリース版・レビュー「扉に部屋の答えが書いてある（本編はルールだけ・RC9）」】扉は 何がいるか・仕掛け・大技・難しさ だけ。
                    //   部屋の当て方（CopyTip）は、はじめてのランの1・2階（教える階）だけ扉に出す（上の return）。
                    text = $"{room.Flavor}\n{(bossRoom ? "本体：" + PhaseRules.KindName(m.Enemies.First().Kind) + "　護衛：" : "敵：")}{string.Join("・", foes)}\n大技：{(clockNames.Any() ? string.Join("・", clockNames) : "なし")}\n仕掛け：{hazard}　難しさ：{new string('★', stars)}{new string('☆', 5 - stars)}"
                         + reward + (room.ParTurns > 0 ? $"\n速さ：{room.ParTurns}ターン以内ならルーン片 +{ActContent.ParBonus}" : ""); // 【RC2】「危険 ★」が何か分からなかった（初見のレビュー）
                    roomInfo[key] = text;
                    return text;
                }
                if (room.CombatRun) // 【戦闘中心 v3】一言・コピーの使いどころ・敵・仕掛け・危険度・報酬（扉に収まる6〜7行）
                {
                    int killers = m.Enemies.Count(e => !e.IsObject && e.Kind != EnemyKind.HealClone);
                    bool traits = m.Enemies.Any(e => !e.IsObject && PhaseRules.HasTrait(e)) || m.Board.StatueOrder.Count > 0;
                    int stars = room.Kind == RoomKind.Boss ? 5 : room.Kind == RoomKind.Danger ? 4 : room.Kind == RoomKind.Tactical ? 3 : room.Kind == RoomKind.Trial ? 1 : Mathf.Clamp((killers >= 2 ? 1 : 0) + (killers >= 3 ? 1 : 0) + (traits ? 1 : 0), 1, 3);
                    var tools = m.Enemies.Where(e => e.IsObject).GroupBy(e => e.Kind).Select(g => $"{PhaseRules.KindName(g.Key)}×{g.Count()}");
                    // 【レビュー】敵の行は兵種ごとに1つ（まね方は数だけ）：「兵×2（全1・移1）」
                    var foes = m.Enemies.Where(e => !e.IsObject && e.Kind != EnemyKind.HealClone).GroupBy(e => e.Kind).Select(g =>
                    {
                        var ks = g.GroupBy(e => e.CopyKind).ToList();
                        string kindsText = ks.Count == 1 ? CopyKinds.Short(ks[0].Key) : string.Join("・", ks.Select(k => CopyKinds.Short(k.Key).Substring(0, 1) + k.Count()));
                        return $"{(g.Key == EnemyKind.Normal ? "兵" : PhaseRules.KindName(g.Key))}×{g.Count()}（{kindsText}）";
                    });
                    text = $"{room.Flavor}\nまねの使いどころ：{room.CopyTip}\n敵：{(foes.Any() ? string.Join("・", foes) : "なし")}{(tools.Any() ? "　道具：" + string.Join("・", tools) : "")}\n仕掛け：{(gimmicks.Count > 0 ? string.Join("・", gimmicks) : "なし")}"
                         + $"\n危険度：{new string('★', stars)}{new string('☆', 5 - stars)}" + (room.ParTurns > 0 ? $"　速さ：{room.ParTurns}T 以内で +{ActContent.ParBonus}" : "")
                         + (room.Kind == RoomKind.Trial ? $"\n成功：珍しい札1枚＋ルーン片 {ActContent.TrialShards}／時間切れ：報酬なし" : "")
                         + (room.Mission != null ? $"\n任意ミッション：{room.Mission.Text}（+{room.Mission.Reward}）" : "");
                    roomInfo[key] = text;
                    return text;
                }
                if (room.PuzzleRun) // 【Puzzle v2】目的・新しい物・一言・敵・危険度（扉に収まる5行）
                {
                    // 危険度は中身から（通常戦：敵の数と固定攻撃。敵のいない部屋は★1：レビュー）
                    int killers = m.Enemies.Count(e => !e.IsObject);
                    bool traits = m.Enemies.Any(e => !e.IsObject && PhaseRules.HasTrait(e)) || m.Board.StatueOrder.Count > 0;
                    int stars = room.Kind == RoomKind.Boss ? 5 : room.Kind == RoomKind.Danger ? 4 : Mathf.Clamp((killers > 0 ? 1 : 0) + (killers >= 2 ? 1 : 0) + (traits ? 1 : 0), 1, 3);
                    var tools = m.Enemies.Where(e => e.IsObject).GroupBy(e => e.Kind).Select(g => $"{PhaseRules.KindName(g.Key)}×{g.Count()}");
                    text = $"目的：{GoalLabel(stage.goal)}（{PuzzleRules.GoalSentence(m, stage.goalText)}）\n新しい物：{room.NewThing}\n{room.Flavor}\n敵：{(kinds.Any() ? string.Join("・", kinds) : "なし")}{(tools.Any() ? "　道具：" + string.Join("・", tools) : "")}\n危険度：{new string('★', stars)}{new string('☆', 5 - stars)}"
                         + (room.Mission != null ? $"\n任意ミッション：{room.Mission.Text}（+{room.Mission.Reward}）" : "");
                    roomInfo[key] = text;
                    return text;
                }
                if (room.CopyRun) // 【Copy Core V3】コピーの使い方と一言を先に（報酬の傾向・速さは省く：扉に収まるように）
                {
                    int stars = room.Kind == RoomKind.Boss ? 5 : room.Kind == RoomKind.Danger ? 4 : 2;
                    text = $"まね方：{room.Pillar}\n{room.Flavor}\n敵：{string.Join("・", kinds)}\n仕掛け：{(gimmicks.Count > 0 ? string.Join("・", gimmicks) : "なし")}\n危険度：{new string('★', stars)}{new string('☆', 5 - stars)}"
                         + (room.Mission != null ? $"\n任意ミッション：{room.Mission.Text}（+{room.Mission.Reward}）" : "");
                    roomInfo[key] = text;
                    return text;
                }
                if (room.Mission != null) text += $"\n任意ミッション：{room.Mission.Text}（+{room.Mission.Reward}）";
                if (room.ParTurns > 0) text += $"\n速さの報酬：{room.ParTurns}T 以内で +{ActContent.ParBonus}（遅くても損なし）";
            }
            roomInfo[key] = text;
            return text;
        }

        /// <summary>【Puzzle v2】部屋の目標の種類（扉に出す）。</summary>
        public static string GoalLabel(RoomGoal g) => g switch
        {
            RoomGoal.Unseal => "封印解除",
            RoomGoal.Carry => "搬送",
            RoomGoal.Sync => "同期",
            RoomGoal.Escape => "脱出",
            _ => "討伐",
        };

        static string TagText(CardTag t)
        {
            var list = new List<string>();
            if ((t & CardTag.Attack) != 0) list.Add("攻撃");
            if ((t & CardTag.Move) != 0) list.Add("移動");
            if ((t & CardTag.Control) != 0) list.Add("制御");
            if ((t & CardTag.Env) != 0) list.Add("環境");
            if ((t & CardTag.Draw) != 0) list.Add("ドロー");
            if ((t & CardTag.Multi) != 0) list.Add("多命令");
            if ((t & CardTag.Copy) != 0) list.Add("まね操作");
            return list.Count > 0 ? string.Join("・", list) : "いろいろ";
        }

        // ───── 報酬 ─────

        /// <summary>【Combat V4】報酬：命令札2＋戦術札2（中ボスは専用の戦術札つき）。1枚目は無料・2枚目からルーン片。</summary>
        string rewardAnimKey;
        float rewardAnimAt;

        /// <summary>【RC6・本人「報酬の見せ方」】報酬の画面を開いてからの時間（勝利 → ルーン片の数え上げ 0.6 秒 → 札が1枚ずつ）。</summary>
        float RewardAnimTime(ActRun run)
        {
            string key = $"{run.Floor}|{run.Current?.Id}|{run.BattlesWon}";
            if (key != rewardAnimKey) { rewardAnimKey = key; rewardAnimAt = Time.unscaledTime; }
            return Time.unscaledTime - rewardAnimAt;
        }

        void DrawRewardV4(float w, float h)
        {
            // 【RC11】はじめて刻印が出たら、説明の窓（後ろの報酬の画面は押せない）
            bool intro = act.Run.RewardImprints.Count > 0 && act.Profile != null && !act.Profile.imprintIntroSeen && RewardAnimTime(act.Run) > 0.8f;
            var mi = ModalInput.Begin(intro);
            DrawRewardV4Body(w, h);
            ModalInput.End(mi);
            if (intro) DrawImprintIntro(w, h);
        }

        void DrawRewardV4Body(float w, float h)
        {
            var run = act.Run;
            float at = RewardAnimTime(run);
            string shards = run.LastRewardBreakdown ?? "";
            if (at < 0.7f && run.LastRewardShards > 0) shards = $"ルーン片 +{Mathf.RoundToInt(run.LastRewardShards * Mathf.Clamp01(at / 0.6f))}"; // 【RC6】数え上げ
            string heal = run.LastHealText != null ? "　" + run.LastHealText : "";
            string pick = run.RewardPicksLeft > 0 ? $"札を {run.RewardPicksLeft} 枚まで無料で選べる" + (run.ExtraBuysLeft > 0 ? "（選んだあと、ルーン片でもう1枚買える）" : "") + "。強い札は、敵もまねる（札の上の「まね」）" + (run.RewardCards.Any(rc => rc.Card != null && rc.Card.type == CommandType.Tactic) ? "　緑＝戦術札" : "") // 【RC5・最終ゲート】戦術札が出ない階（はじめてのランの1・2階）は出さない【RC6】核を報酬でも
                        : run.ExtraBuysLeft > 0 ? "もう1枚、ルーン片で買える（取らなくてもよい）" : "札を受け取った（次の扉へ）";
            Title(w, 52, run.Current.IsMidBoss ? "中ボス撃破！" : "勝利！", $"{shards}{heal}　{act.Message ?? ""}\n{pick}".Replace("\n", "\n"));
            if (at < 0.95f && run.LastRewardShards > 0) // 【RC6】勝利 → ルーン片（真ん中に大きく数え上げ）→ 札
            {
                float fade = at < 0.7f ? 1f : 1f - (at - 0.7f) / 0.25f;
                int shown = Mathf.RoundToInt(run.LastRewardShards * Mathf.Clamp01(at / 0.6f));
                DrawUiImage(new Rect(w / 2 - 110, 248, 54, 54), "hz_rune", fade, ScaleMode.ScaleToFit); // 【RC6】
                ShadowLabel(new Rect(0, 240, w, 70), $"+{shown}", new GUIStyle(Big) { fontSize = 56, alignment = TextAnchor.MiddleCenter }, new Color(0.55f, 0.95f, 1f, fade));
                ShadowLabel(new Rect(0, 308, w, 30), "ルーン片", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.7f, 0.95f, 1f, fade));
            }
            var list = run.RewardCards;
            int n = list.Count;
            float cw = Mathf.Min(run.V4Run && run.Current != null && run.Current.IsMidBoss ? 168 : 190, (w - 80) / Mathf.Max(4, n) - 12), ch = cw * 1.32f; // 【RC7】刻印の段があるときは札を少し小さく（強化のボタンがヒントの帯に重なった）
            float x0 = (w - (cw * n + 14 * (n - 1))) / 2, y = 168;
            for (int i = 0; i < n; i++)
            {
                var rc = list[i];
                float appear = Mathf.Clamp01((at - 0.55f - i * 0.12f) / 0.25f); // 【RC6】札は1枚ずつ（下から）
                if (appear <= 0f) continue;
                var r = new Rect(x0 + i * (cw + 14), y + 26 + (1f - appear) * 26f, cw, ch);
                if (rc.IsCopySlot)
                {
                    DrawRect(new Rect(r.x - 4, y, r.width + 8, 22), new Color(0.55f, 0.4f, 0.1f, 0.9f));
                    GUI.Label(new Rect(r.x - 4, y, r.width + 8, 22), rc.Reason, new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip });
                }
                bool free = run.RewardPicksLeft > 0;
                int price = run.ExtraPriceOf(rc);
                bool canBuy = !free && run.ExtraBuysLeft > 0 && run.Shards >= price;
                bool enabled = (free || canBuy) && appear >= 1f;
                if (rc.IsTactic) TacticCardView.Draw(r, rc.Card, enabled, appear);
                else { var (rfg, rcd) = UpgradeLook(rc.Card); DrawCardV2(r, rc.Card, false, false, 0, enabled || appear < 1f, appear, 0, rfg, rcd); DrawRarity(r, rc.Card); if (!rc.IsCopySlot) DrawCopyBadge(r, rc.Card); } // 【RC6・レビュー2回目】強化した種類の札は、取ると強化つき
                if (rc.Card != null && rc.Card.type != CommandType.Tactic && appear >= 1f && run.Pool4Rarity(rc.Card) != Rarity.Common) // 【RC6】珍しい札は光る
                    DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), run.Pool4Rarity(rc.Card) == Rarity.Rare ? new Color(1f, 0.8f, 0.3f, Blink(0.5f, 1f, 3f)) : new Color(0.6f, 0.85f, 1f, Blink(0.4f, 0.9f, 3f)), 2f);
                if (!free && run.ExtraBuysLeft > 0)
                    ShadowLabel(new Rect(r.x, r.yMax + 4, r.width, 22), canBuy ? $"追加で取る（{price}）" : $"{price}（あと {price - run.Shards}）", new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, canBuy ? new Color(0.6f, 0.95f, 1f) : new Color(1f, 1f, 1f, 0.45f));
                bool takeBtn = false;
                if (free && enabled)
                {
                    // 【通常リリース版・画面の直し 17】札の下に「取る」（押せると分かる印）。札を押しても同じ
                    var tb = new Rect(r.center.x - 52f, r.yMax + 5f, 104f, 22f);
                    bool th = tb.Contains(Event.current.mousePosition) || r.Contains(Event.current.mousePosition);
                    DrawUi(tb, th ? "button_hover" : "button_gold", new Color(0.6f, 0.45f, 0.15f));
                    ShadowLabel(tb, "取る", new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, th ? Gold : new Color(0.15f, 0.08f, 0.02f));
                    takeBtn = GUI.Button(tb, GUIContent.none, GUIStyle.none);
                }
                if (enabled && (Hit(r) || takeBtn))
                {
                    Sfx.Play(Sfx.Kind.Card);
                    if (free) run.TakeRewardCard(rc); else run.BuyRewardCard(rc);
                    act.Checkpoint();
                    break;
                }
            }
            bool nothingTaken = run.RewardCards.Count == n && n > 0;
            // 【完成版】入れ替え：命令札を取ったら、デッキの札を1枚外してよい（任意）
            float sy = y + 26 + ch + 34;
            bool both = run.SwapsLeft > 0 && run.TacticSwapsLeft > 0;
            if (run.SwapsLeft > 0 && Button(new Rect(both ? w / 2 - 470 : w / 2 - 230, sy, 460, 40), $"入れ替え：デッキの命令札を1枚外す（任意・無料・この画面だけ）", false, run.Deck.Count > run.MinDeck, 15))
                removeMode = "swap";
            if (run.TacticSwapsLeft > 0 && Button(new Rect(both ? w / 2 + 10 : w / 2 - 230, sy, 460, 40), $"入れ替え：戦術札を1枚外す（任意・無料・この画面だけ）", false, run.TacticCards.Count > ActRun.MinTactics, 15))
                removeMode = "tswap";
            float uy = sy + 48;
            if (run.RewardImprints.Count > 0 || (run.V4Run && run.Current != null && run.Current.IsMidBoss)) // 【RC7】中ボスの報酬：刻印（2つから1つ・無料）。場所は最初から空けておく（出そろってから・取ってから、強化のボタンが動いていた）
            {
                uy = sy + 98;
                if (run.RewardImprints.Count > 0 && at >= 0.8f + n * 0.12f)
                {
                    ShadowLabel(new Rect(0, sy - 6, w, 22), "どちらか1つ：【刻印】ランの間ずっと効く力（下の2つから）／【強化】攻撃の札1種類がずっと強くなる", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.85f, 0.7f, 1f));
                    DrawImprintChoices(new Rect(w / 2 - 380, sy + 18, 760, 72), run.RewardImprints, id => { run.TakeRewardImprint(id); act.Checkpoint(); });
                }
            }
            if (run.RewardUpgradesLeft > 0 && Button(new Rect(w / 2 - 230, uy, 460, 40), run.RewardImprints.Count > 0 ? "または【強化】攻撃の札1種類をずっと強く" : "中ボスの報酬：【強化】攻撃の札1種類をずっと強く（無料）", true, run.AnyUpgradable, 15)) { removeMode = "forge_reward"; forgePick = null; } // 【RC6】【RC7】刻印とどちらか
            bool ready = at >= 0.8f + n * 0.12f; // 【RC6・レビュー】札が出そろう前に「取らずに」を押せた
            if (ready && Button(new Rect(w / 2 - 150, h - 64, 300, 44), run.SkipBonusAvailable ? (V4Content.SkipRewardShards > 0 ? $"取らずに次の扉へ（ルーン片 +{V4Content.SkipRewardShards}）" : "取らずに次の扉へ") : "次の扉へ", !run.SkipBonusAvailable && run.RewardPicksLeft == 0, ready)) act.NextFloor(); // 【第2巡 7】取らずに進むは灰色（取ったあとの「次の扉へ」だけ金） // 【通常リリース版・画面の直し 21】札が出そろうまでは出さない（数え上げの間、ボタンが「取らずに次の扉へ」だけだった）
        }

        void DrawReward(float w, float h)
        {
            if (act.Run.V4Run) { DrawRewardV4(w, h); return; } // 【Combat V4】
            var run = act.Run;
            string shards = run.LastRewardBreakdown ?? $"ルーン片 +{ActContent.ShardsFor(run.Current.Kind)}";
            bool trial = run.Current != null && run.Current.Kind == RoomKind.Trial;
            string head = trial ? (run.LastTrialFailed ? "試練：失敗（負けではない）" : "試練：成功！") : "勝利！";
            string pickText = run.LastTrialFailed && trial ? "報酬は無し。HP はそのまま、次の扉へ進める" : run.RewardPicksLeft > 0 ? $"カードを {run.RewardPicksLeft} 枚まで選べる（取らなくてもよい：デッキが薄い方が欲しいカードを引きやすい）" : "カードを受け取った（次の扉へ）";
            Title(w, 56, head, $"{shards}　{act.Message ?? ""}\n{pickText}");
            var cardsList = run.RewardCards;
            int n = cardsList.Count;
            float cw = Mathf.Min(176, (w - 80) / Mathf.Max(4, n) - 12), ch = cw * 1.32f;
            float x0 = (w - (cw * n + 14 * (n - 1))) / 2, y = 160;
            for (int i = 0; i < n; i++)
            {
                var rc = cardsList[i];
                var r = new Rect(x0 + i * (cw + 14), y + 26, cw, ch);
                if (rc.IsCopySlot)
                {
                    DrawRect(new Rect(r.x - 4, y, r.width + 8, 22), new Color(0.55f, 0.4f, 0.1f, 0.9f));
                    GUI.Label(new Rect(r.x - 4, y, r.width + 8, 22), rc.Reason, new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip });
                }
                DrawCardV2(r, rc.Card, false, false, 0, run.RewardPicksLeft > 0, 1f, 0);
                DrawRarity(r, rc.Card);
                if (run.RewardPicksLeft > 0 && Hit(r)) { Sfx.Play(Sfx.Kind.Card); run.TakeRewardCard(rc); act.Checkpoint(); break; }
            }
            float iy = y + 40 + ch;
            if (run.RewardImprints.Count > 0)
            {
                ShadowLabel(new Rect(0, iy, w, 22), "刻印を1つ選べる（危険戦の報酬・取らなくてもよい）", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.85f, 0.7f, 1f));
                DrawImprintChoices(new Rect(w / 2 - 380, iy + 26, 760, 84), run.RewardImprints, id => { run.TakeRewardImprint(id); act.Checkpoint(); });
            }
            if (Button(new Rect(w / 2 - 110, h - 64, 220, 44), "次の扉へ", true)) act.NextFloor();
        }

        /// <summary>
        /// 【RC11・本人「刻印って何？強化との違いが分からない。どれを取ったら何が変わるか想像できない」】はじめて刻印が出たときだけ、大きく説明する（1回）。
        /// </summary>
        bool DrawImprintIntro(float w, float h)
        {
            if (act.Profile == null || act.Profile.imprintIntroSeen) return false;
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.92f)); // 【第2巡 6】0.78 → 0.92
            var r = new Rect(w / 2 - 330, h * 0.18f, 660, 380);
            DrawRect(r, new Color(0.04f, 0.04f, 0.09f, 1f));
            DrawFrame(r, Gold, 3f);
            ShadowLabel(new Rect(r.x, r.y + 14, r.width, 36), "刻印と強化", new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, Gold);
            var hs = new GUIStyle(Label) { fontSize = 19, fontStyle = FontStyle.Bold, wordWrap = false };
            var bs = new GUIStyle(Small) { fontSize = 15, wordWrap = true, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            ShadowLabel(new Rect(r.x + 30, r.y + 64, r.width - 60, 26), "【刻印】このランの間ずっと効く力", hs, new Color(0.85f, 0.7f, 1f));
            GUI.Label(new Rect(r.x + 30, r.y + 92, r.width - 60, 58), "札とは別に、何個でも持てる（画面の上の帯にマウスで説明）。どれも、まね・敵の攻撃・仕掛けで戦うほど得をする。例：時計の針＝敵の大技が敵に当たると +3。", bs);
            ShadowLabel(new Rect(r.x + 30, r.y + 160, r.width - 60, 26), "【強化】攻撃の札1種類が、ずっと強くなる", hs, new Color(1f, 0.8f, 0.45f));
            GUI.Label(new Rect(r.x + 30, r.y + 188, r.width - 60, 58), $"同じ名前の札は全部。例：{UpgradeExample()}。押す前に「前 → 後」が見える。", bs);
            GUI.Label(new Rect(r.x + 30, r.y + 254, r.width - 60, 40), "中ボスの報酬：札（2枚まで無料）に加えて、刻印か強化のどちらか1つ。迷ったら：敵を使って戦うなら刻印・強い札を自分で使うなら強化。", new GUIStyle(bs) { normal = { textColor = new Color(0.62f, 0.95f, 1f) } });
            var ev = Event.current;
            bool key = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.Space || ev.keyCode == KeyCode.KeypadEnter);
            if (Button(new Rect(r.center.x - 110, r.yMax - 62, 220, 44), "わかった", true, true, 17) || key)
            {
                if (key) ev.Use();
                act.Profile.imprintIntroSeen = true;
                act.SaveProfile();
            }
            return true;
        }

        void DrawImprintChoices(Rect area, List<ImprintId> ids, System.Action<ImprintId> take)
        {
            float cw = (area.width - 14 * (ids.Count - 1)) / Mathf.Max(1, ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                var def = ActContent.Imprint(ids[i]);
                var r = new Rect(area.x + i * (cw + 14), area.y, cw, area.height);
                bool hover = r.Contains(Event.current.mousePosition);
                DrawUi(r, hover ? "panel_red" : "panel", PanelBlue);
                ShadowLabel(new Rect(r.x + 12, r.y + 6, r.width - 24, 22), $"{def.Name}　［{def.Build}］", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold }, new Color(0.85f, 0.7f, 1f));
                GUI.Label(new Rect(r.x + 12, r.y + 30, r.width - 24, r.height - 34), def.Description, new GUIStyle(Small) { fontSize = 13, wordWrap = true });
                if (Hit(r)) { Sfx.Play(Sfx.Kind.Card); take(ids[i]); break; }
            }
        }

        // ───── 商人 ─────

        void DrawShop(float w, float h)
        {
            var run = act.Run;
            Title(w, 56, "遺跡の商人", act.Run != null && act.Run.V4Run ? "ルーン片で札・アイテム・刻印・強化を買える。札の削除もできる（強化と削除は1回の訪問で1回ずつ）。売ることはできない。" : "ルーン片でカード・アイテム・刻印を購入できる。ルーン片を払ってデッキのカードを1枚削除することもできる（1回の訪問で1回）。売ることはできない。") /* 【RC5】完成版は刻印を売らない */;
            float y = 150;
            ShadowLabel(new Rect(40, y, 300, 22), "札", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            float cw = 150, ch = cw * 1.3f;
            for (int i = 0; i < run.ShopCards.Count; i++)
            {
                var sc = run.ShopCards[i];
                var r = new Rect(40 + i * (cw + 14), y + 26 + (run.V4Run ? 22 : 0), cw, ch); // 【RC2】札の上の「まね：…」が見出しと重ならないように
                if (sc.Card.type == CommandType.Tactic) TacticCardView.Draw(r, sc.Card, !sc.Sold, sc.Sold ? 0.35f : 1f);
                else { var (sfg, scd) = UpgradeLook(sc.Card); DrawCardV2(r, sc.Card, false, false, 0, !sc.Sold, sc.Sold ? 0.35f : 1f, 0, sfg, scd); DrawRarity(r, sc.Card); if (run.V4Run && !sc.Sold) DrawCopyBadge(r, sc.Card); }
                if (Button(new Rect(r.x + 10, r.yMax + 6, r.width - 20, 30), sc.Sold ? "購入済み" : run.Shards >= sc.Price ? $"購入（{sc.Price}）" : $"{sc.Price}（あと {sc.Price - run.Shards}）", false, !sc.Sold && run.Shards >= sc.Price, 13))
                { run.BuyCard(sc); act.Checkpoint(); }
            }
            float rx = 40 + Mathf.Max(4, run.ShopCards.Count) * (cw + 14) + 6; // 【RC6】命令札3＋戦術札2
            ShadowLabel(new Rect(rx, y, 300, 22), "アイテム（2つまで）", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            for (int i = 0; i < run.ShopItems.Count; i++)
            {
                var si = run.ShopItems[i];
                var def = ActContent.Item(si.Id);
                var r = new Rect(rx, y + 26 + i * 86, w - rx - 40, 78);
                DrawUi(r, "panel", PanelBlue, si.Sold ? 0.4f : 1f);
                ShadowLabel(new Rect(r.x + 12, r.y + 6, r.width - 150, 22), def.Name, new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold }, Color.white);
                var ds = new GUIStyle(Small) { fontSize = 12, wordWrap = false };
                string dt = WrapJa(def.Description, ds, r.width - 150);
                while (ds.fontSize > 10 && ds.CalcHeight(new GUIContent(dt), r.width - 150) > 46f) { ds.fontSize--; dt = WrapJa(def.Description, ds, r.width - 150); } // 【RC5・最終検証】16:10・4:3 で転移石の説明が欄からはみ出した
                GUI.Label(new Rect(r.x + 12, r.y + 30, r.width - 150, 46), dt, ds);
                if (Button(new Rect(r.xMax - 130, r.y + 22, 120, 32), si.Sold ? "購入済み" : run.Shards >= si.Price ? $"購入（{si.Price}）" : $"{si.Price}（あと {si.Price - run.Shards}）", false, !si.Sold && run.Shards >= si.Price && run.Items.Count < ActContent.ItemSlots, 13))
                { run.BuyItem(si); act.Checkpoint(); }
            }
            if (run.ShopImprint != null)
            {
                var def = ActContent.Imprint(run.ShopImprint.Value);
                float iy0 = y + 26 + 2 * 86 + (run.V4Run ? 8 : 0);
                if (run.V4Run) { ShadowLabel(new Rect(rx, iy0, 420, 22), "刻印（ランの間ずっと効く・いくつでも持てる）", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, new Color(0.85f, 0.7f, 1f)); iy0 += 26; } // 【RC7】アイテムの続きに見えた
                var r = new Rect(rx, iy0, w - rx - 40, 78);
                DrawUi(r, "panel_red", PanelBlue, run.ShopImprintSold ? 0.4f : 1f);
                ShadowLabel(new Rect(r.x + 12, r.y + 6, r.width - 150, 22), $"刻印：{def.Name}［{def.Build}］", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold }, new Color(0.85f, 0.7f, 1f));
                var ds = new GUIStyle(Small) { fontSize = 12, wordWrap = false };
                string dt = WrapJa(def.Description, ds, r.width - 150);
                while (ds.fontSize > 10 && ds.CalcHeight(new GUIContent(dt), r.width - 150) > 46f) { ds.fontSize--; dt = WrapJa(def.Description, ds, r.width - 150); } // 【RC5・最終検証】16:10・4:3 で転移石の説明が欄からはみ出した
                GUI.Label(new Rect(r.x + 12, r.y + 30, r.width - 150, 46), dt, ds);
                if (Button(new Rect(r.xMax - 130, r.y + 22, 120, 32), run.ShopImprintSold ? "購入済み" : run.Shards >= def.Price ? $"購入（{def.Price}）" : $"{def.Price}（あと {def.Price - run.Shards}）", false, !run.ShopImprintSold && run.Shards >= def.Price, 13))
                { run.BuyImprint(); act.Checkpoint(); }
            }
            float by = y + 26 + ch + 50 + (run.V4Run ? 22 : 0);
            if (Button(new Rect(40, by, 340, 40), run.ShopRemoveUsed ? "削除は済み" : $"削除：札を1枚（{run.RemovePrice} ルーン片）", false, !run.ShopRemoveUsed && run.Shards >= run.RemovePrice && run.Deck.Count > run.MinDeck, 14))
                removeMode = "shop";
            if (run.V4Run && Button(new Rect(40, by + 48, 340, 40), run.ShopUpgradeUsed ? "強化は済み" : !run.AnyUpgradable ? "強化できる札が無い" : $"強化：攻撃の札1種類（{run.UpgradePrice} ルーン片）", false, !run.ShopUpgradeUsed && run.AnyUpgradable && run.Shards >= run.UpgradePrice, 14))
            { removeMode = "forge_shop"; forgePick = null; } // 【RC6】自分＋・まね＋・まね－
            if (run.SwapsLeft > 0 && Button(new Rect(400, by, 360, 40), $"入れ替え：デッキの命令札を1枚外す（任意・無料）", false, run.Deck.Count > run.MinDeck, 14))
                removeMode = "swap"; // 【完成版】札を買ったら、同じ種類の札を1枚外してよい
            if (run.TacticSwapsLeft > 0 && Button(new Rect(780, by, 360, 40), $"入れ替え：戦術札を1枚外す（任意・無料）", false, run.TacticCards.Count > ActRun.MinTactics, 14))
                removeMode = "tswap";
            if (Button(new Rect(w / 2 - 110, h - 64, 220, 44), "立ち去る（次の扉へ）", false)) { removeMode = null; act.NextFloor(); } // 【第2巡 7】灰色（買う物より強くしない）
        }

        void DrawRemovePicker(float w, float h, System.Func<int, bool> remove, string title, string cost, IList<CommandData> list = null, string verb = "削除する", int newIndex = -1)
        {
            var r = new Rect(w * 0.06f, 60, w * 0.88f, h - 90);
            DrawRect(new Rect(0, 0, w, h), new Color(0.02f, 0.03f, 0.06f, 0.93f)); // 【完成版】後ろの画面の文字が透けないように【第2巡 6】0.8 → 0.93
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 1f)); // 【RC5・レビュー】窓の絵が半透明で、後ろの文字が透けていた
            DrawUi(r, "panel", PanelBlue);
            ShadowLabel(new Rect(r.x + 16, r.y + 8, r.width - 200, 24), title, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold }, Gold);
            if (Button(new Rect(r.xMax - 140, r.y + 6, 124, 30), "やめる", false, true, 13)) { removeMode = null; pickRemove = -1; return; }
            var deck = list ?? act.Run.Deck;
            if (pickRemove >= deck.Count) pickRemove = -1;
            DrawCardGrid(new Rect(r.x + 16, r.y + 44, r.width - 32, r.height - 100), deck, i => pickRemove = i, pickRemove, newIndex);
            if (pickRemove >= 0)
            {
                int same = deck.Count(c => c == deck[pickRemove]);
                GUI.Label(new Rect(r.x + 16, r.yMax - 76, r.width - 32, 22), $"選んだ1枚だけを{(verb == "削除する" ? "削除" : "外す")}：{(list != null ? "戦術札" : "デッキ")}の「{deck[pickRemove].displayName}」{same}枚 → {same - 1}枚・{deck.Count}枚 → {deck.Count - 1}枚",
                    new GUIStyle(Small) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Gold } });
            }
            if (pickRemove >= 0 && Button(new Rect(r.center.x - 170, r.yMax - 48, 340, 38), $"「{deck[pickRemove].displayName}」を1枚{verb}{cost}", true, true, 14))
            {
                remove(pickRemove);
                pickRemove = -1;
                removeMode = null;
                if (act.Screen == ActScreen.Rest && (!act.Run.V4Run || act.Run.RestFinished)) act.NextFloor(); // 【RC6】完成版の休息は2つまで
            }
        }

        // ───── 休息 ─────

        void DrawRest(float w, float h)
        {
            var run = act.Run;
            if (run.V4Run) { DrawFinalRest(w, h); return; } // 【完成版】休む・鍛える・祈る
            Title(w, 56, "静かな祠", "どちらか1つ。休んで HP を戻すか、祈ってデッキのカードを1枚削除するか（無料）。");
            float bw = 340, x = w / 2 - bw - 10, y = 200;
            var a = new Rect(x, y, bw, 160);
            var b = new Rect(x + bw + 20, y, bw, 160);
            DrawUi(a, "panel", PanelBlue);
            DrawUi(b, "panel", PanelBlue);
            ShadowLabel(new Rect(a.x, a.y + 14, a.width, 28), "休む", new GUIStyle(Big) { fontSize = 22, alignment = TextAnchor.MiddleCenter }, new Color(0.4f, 1f, 0.6f));
            GUI.Label(new Rect(a.x + 16, a.y + 48, a.width - 32, 50), $"HP を {ActContent.RestHeal} 回復（{run.Hp}/{run.MaxHp}）", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true });
            if (Button(new Rect(a.x + 60, a.yMax - 50, a.width - 120, 38), "休む", true)) { run.RestHeal(); act.NextFloor(); }
            ShadowLabel(new Rect(b.x, b.y + 14, b.width, 28), "祈る", new GUIStyle(Big) { fontSize = 22, alignment = TextAnchor.MiddleCenter }, new Color(0.85f, 0.7f, 1f));
            GUI.Label(new Rect(b.x + 16, b.y + 48, b.width - 32, 50), "デッキのカードを1枚削除（無料）", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true });
            if (Button(new Rect(b.x + 60, b.yMax - 50, b.width - 120, 38), "カードを選ぶ", true, run.Deck.Count > run.MinDeck)) removeMode = "rest";
        }

        // ───── 特殊部屋 ─────

        void DrawEvent(float w, float h)
        {
            var run = act.Run;
            var room = run.Current;
            Title(w, 56, room.Name, room.Flavor);
            if (room.Id == "event_mirror")
            {
                GUI.Label(new Rect(0, 140, w, 24), "HP を 3 払って、鏡に映ったカードを1枚もらう（取らずに立ち去ってもよい）", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter });
                float cw = 170, ch = cw * 1.3f;
                float x0 = (w - (cw * run.EventCards.Count + 14 * (run.EventCards.Count - 1))) / 2;
                for (int i = 0; i < run.EventCards.Count; i++)
                {
                    var c = run.EventCards[i];
                    var r = new Rect(x0 + i * (cw + 14), 180, cw, ch);
                    DrawCardV2(r, c, false, false, 0, run.Hp > 3, 1f, 0);
                    if (run.Hp > 3 && Hit(r)) { run.MirrorTake(c); act.NextFloor(); return; }
                }
            }
            else
            {
                GUI.Label(new Rect(0, 140, w, 24), "最大 HP を 3 払って刻印を1つ得るか、ルーン片 25 をもらうか", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter });
                if (run.RewardImprints.Count > 0)
                    DrawImprintChoices(new Rect(w / 2 - 380, 180, 760, 90), run.RewardImprints, id => { if (run.AltarImprint(id)) act.NextFloor(); });
                if (Button(new Rect(w / 2 - 150, 300, 300, 40), "ルーン片 25 をもらう", false)) { run.AltarShards(); act.NextFloor(); return; }
            }
            if (Button(new Rect(w / 2 - 110, h - 64, 220, 44), "立ち去る", false)) act.NextFloor(); // 【第2巡 7】
        }

        // ───── 結果 ─────

        /// <summary>
        /// 【検証中】結果画面（2026-10-05 本人「1回やったらもういい」・監査 R1/UI-05）：
        /// 何階まで行けたか（自己ベスト）・HP の流れ・何で削られたか・次に試すこと・解放 → すぐ「もう一度」。
        /// </summary>
        void DrawResult(float w, float h)
        {
            var run = act.Run;
            if (run.V4Run) { DrawFinalResult(w, h); return; } // 【完成版】
            bool clear = run.Cleared;
            int floor = Mathf.Min(run.Floor + 1, run.FloorCount);
            string sub = clear ? $"{(run.CombatRun ? "鏡の主" : run.PuzzleRun ? "封紋の守護者" : "鏡守の騎士")}を倒した。{run.Role.Name}・合計 {run.TotalTurns}ターン・受けたダメージ {run.TotalDamage}"
                               : $"{run.Role.Name}・{floor}階「{run.Current?.Name}」で倒れた。" + (run.LastHit != null ? $"　最後の一撃：{run.LastHit}" : "");
            Title(w, 22, clear ? "ACT1 CLEAR！" : "敗北…", sub);
            var head = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false };
            var body = new GUIStyle(Small) { fontSize = 13, wordWrap = true };
            var line1 = new GUIStyle(Small) { fontSize = 13, wordWrap = false, clipping = TextClipping.Clip };

            // 左：数字（到達・HP・被ダメの内訳・ルーン片・ビルド・使ったカード）
            var L = new Rect(w * 0.04f, 104, w * 0.45f, 500);
            DrawUi(L, "panel", PanelBlue);
            float x = L.x + 16, lw = L.width - 32, y = L.y + 12;
            int prev = act.PreviousBest;
            string best = prev <= 0 ? "この役職で初めての記録" : floor > prev ? $"自己ベスト更新！（前 {prev}/{run.FloorCount}）" : $"自己ベスト {prev}/{run.FloorCount}";
            ShadowLabel(new Rect(x, y, lw, 24), $"到達 {floor}/{run.FloorCount}階　{best}", head, Gold); y += 28;
            var flow = run.HpFlow.Concat(new[] { run.Hp }).Select(v => v.ToString());
            GUI.Label(new Rect(x, y, lw, 20), $"HP の流れ（部屋に入った時 → 最後）：{string.Join("→", flow)}", line1); y += 26;
            int total = run.DamageBySource.Values.Sum();
            ShadowLabel(new Rect(x, y, lw, 22), $"受けたダメージ {total} の内訳", head, new Color(1f, 0.72f, 0.6f)); y += 24;
            if (total == 0) { GUI.Label(new Rect(x, y, lw, 20), "なし", line1); y += 22; }
            foreach (var kv in run.DamageBySource.OrderByDescending(k => k.Value).Take(5))
            {
                GUI.Label(new Rect(x, y, 112, 20), kv.Key, line1);
                float bw = Mathf.Max(4, (lw - 170) * kv.Value / Mathf.Max(1, total));
                var col = kv.Key == "反射" ? new Color(0.75f, 0.45f, 1f) : kv.Key == "まね" ? new Color(1f, 0.6f, 0.25f) : kv.Key == "トゲ" || kv.Key == "炎" ? new Color(0.9f, 0.85f, 0.3f) : new Color(0.95f, 0.35f, 0.35f);
                DrawRect(new Rect(x + 116, y + 4, bw, 12), col);
                GUI.Label(new Rect(x + 122 + bw, y, 50, 20), kv.Value.ToString(), line1);
                y += 21;
            }
            y += 8;
            GUI.Label(new Rect(x, y, lw, 20), $"ルーン片：稼いだ {run.ShardsEarned}・残り {run.Shards}・速さの報酬 {run.ParBonuses}回・任意ミッション {run.MissionsDone}", line1); y += 24;
            if (run.V4Run) { GUI.Label(new Rect(x, y, lw, 20), $"遊んだ時間 {(int)(run.PlaySeconds / 60)}分・意味のあるまね利用 {run.CopyUsesTotal}回・大技を敵に当てた {run.BigHitsTotal}回・戦術札 {run.TacticUsesTotal}枚", line1); y += 24; }
            else if (run.CombatRun) { GUI.Label(new Rect(x, y, lw, 20), $"遊んだ時間 {(int)(run.PlaySeconds / 60)}分・意味のあるまね利用 {run.CopyUsesTotal}回・まねキル {run.CopyKillsTotal}・試練 {run.TrialsCleared}成功／{run.TrialsFailed}失敗", line1); y += 24; }
            string imps = run.Imprints.Count > 0 ? string.Join("・", run.Imprints.Select(i => ActContent.Imprint(i).Name)) : "なし";
            GUI.Label(new Rect(x, y, lw, 20), $"デッキ {run.Deck.Count}枚・刻印：{imps}・勝った戦闘 {run.BattlesWon}", line1); y += 24;
            var top = run.UsedCards.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key}×{k.Value}").ToList();
            GUI.Label(new Rect(x, y, lw, 40), $"よく使ったカード：{(top.Count > 0 ? string.Join("・", top) : "なし")}", body); y += 40;
            if (act.FirstTimeCards.Count > 0 && act.Profile.runsFinished > 1) // 1回目のランは全部「初めて」なので出さない
            {
                ShadowLabel(new Rect(x, y, lw, 40), $"初めて使ったカード：{string.Join("・", act.FirstTimeCards.Take(6))}", new GUIStyle(body) { fontStyle = FontStyle.Bold }, Gold);
                y += 40;
            }
            GUI.Label(new Rect(x, L.yMax - 26, lw, 20), $"種 {run.Seed}（「同じ地図で再挑戦」で同じ扉・同じ報酬の候補）", new GUIStyle(Tiny) { fontSize = 11, normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } });

            // 右：次に試すこと・解放・短い履歴
            var R = new Rect(w * 0.51f, 104, w * 0.45f, 500);
            DrawUi(R, "panel", PanelBlue);
            x = R.x + 16; lw = R.width - 32; y = R.y + 12;
            ShadowLabel(new Rect(x, y, lw, 22), "次に試すこと", head, new Color(0.6f, 1f, 0.75f)); y += 26;
            foreach (var tip in run.ResultTips())
            {
                GUI.Label(new Rect(x, y, lw, 40), "・" + tip, body);
                y += 40;
            }
            if (act.NewlyUnlocked.Count > 0 && !run.CombatRun) // 【戦闘中心 v3】v3 の役職は最初から3つ（ほかの遊び方の解放は出さない：外部テストで混乱する）
            {
                ShadowLabel(new Rect(x, y, lw, 24), $"新しい役職が解放された：{string.Join("・", act.NewlyUnlocked)}", new GUIStyle(head) { fontSize = 16 }, Gold);
                y += 28;
            }
            var locked = run.CombatRun ? null : ActContent.Roles.FirstOrDefault(r => !act.IsRoleUnlocked(r));
            if (locked != null) { GUI.Label(new Rect(x, y, lw, 20), $"次の解放：{locked.Name}（{ActProfile.UnlockText(locked.Id)}）", line1); y += 24; }
            y += 6;
            ShadowLabel(new Rect(x, y, lw, 22), "このランの流れ", head, new Color(0.75f, 0.85f, 1f)); y += 24;
            int rows = Mathf.Max(3, (int)((R.yMax - 10 - y) / 18f));
            foreach (var l in run.History.Skip(Mathf.Max(0, run.History.Count - rows)))
            {
                GUI.Label(new Rect(x, y, lw, 18), l, new GUIStyle(line1) { fontSize = 12 });
                y += 18f;
            }

            // 下：すぐもう一度
            float bw2 = 224, gap = 14, bx = (w - (bw2 * 4 + gap * 3)) / 2, by = h - 64;
            if (Button(new Rect(bx, by, bw2, 46), $"もう一度（{run.Role.Name}）", true, true, 14)) { act.Retry(false); return; }
            if (Button(new Rect(bx + (bw2 + gap), by, bw2, 46), "同じ地図で再挑戦", false, true, 14)) { act.Retry(true); return; }
            if (Button(new Rect(bx + (bw2 + gap) * 2, by, bw2, 46), "役職を選ぶ", false, true, 14)) { if (run.V4Run) act.OpenV4RunRoleSelect(); else if (run.CombatRun) act.OpenCombatRunRoleSelect(); else act.OpenRoleSelect(); return; }
            if (Button(new Rect(bx + (bw2 + gap) * 3, by, bw2, 46), "タイトルへ", false, true, 14)) act.OpenTitle();
        }
    }
}
