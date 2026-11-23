using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭の画面（ActUI の partial）：入口（LEVEL 1〜3 を選ぶ）・結果（ランク・PERFECT・タイム・本日のランキング）・スタッフ用の集計（F9）。
    /// 長い文は読ませない：大きい見出し・敵の顔・短い一文。スタッフ用の集計は F9 でだけ開く（遊ぶ人には見えない）。
    /// </summary>
    public partial class ActUI
    {
        static readonly Color[] FestivalLevelColor = { new Color(0.45f, 0.95f, 0.6f), new Color(0.45f, 0.85f, 1f), new Color(1f, 0.55f, 0.45f) };

        bool festivalStaffOpen;
        bool festivalResetArmed;
        bool festivalMainResetArmed;
        float festivalMainResetAt;
        /// <summary>【文化祭クエスト v2・スタッフの立場のレビュー】混雑時は本編へのボタンを隠す（F9 で切り替え・起動ごとに表示に戻る）。</summary>
        static bool festivalHideMain;

        /// <summary>入口：タイトルと3つの LEVEL（カードごと押せる・1 2 3 キーでも）。</summary>
        void DrawFestivalEntry(float w, float h)
        {
            ShadowLabel(new Rect(0, 18, w, 30), "その一手、敵もまねる。（仮題）", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 1f, 1f, 0.75f));
            ShadowLabel(new Rect(0, 46, w, 64), "文化祭クエスト", new GUIStyle(Big) { fontSize = 50, alignment = TextAnchor.MiddleCenter }, Gold);
            string lead = "あなたの命令を、敵も まねる。敵の位置・敵の攻撃・床の仕掛けを利用して勝つ、ターン制の戦術ゲーム。";
            ShadowLabel(new Rect(20, 112, w - 40, 30), lead, Fit(new GUIStyle(Label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = false }, lead, w - 40, 13), Color.white);
            ShadowLabel(new Rect(0, 146, w, 26), "LEVEL を えらんでください（1つのクエストを攻略）", new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, CyanText);

            int n = FestivalContent.Levels.Length;
            float gap = 24f, cw = Mathf.Min(370f, (w - 80f - gap * (n - 1)) / Mathf.Max(1, n)), ch = 380f;
            float x0 = (w - (cw * n + gap * (n - 1))) * 0.5f, y0 = 184f;
            var now = System.DateTime.Now;
            var ev = Event.current;
            int key = ev.type == EventType.KeyDown && !festivalStaffOpen ? (ev.keyCode == KeyCode.Alpha1 || ev.keyCode == KeyCode.Keypad1 ? 1 : ev.keyCode == KeyCode.Alpha2 || ev.keyCode == KeyCode.Keypad2 ? 2 : ev.keyCode == KeyCode.Alpha3 || ev.keyCode == KeyCode.Keypad3 ? 3 : 0) : 0;
            for (int i = 0; i < n; i++)
            {
                var def = FestivalContent.Levels[i];
                var col = FestivalLevelColor[Mathf.Clamp(i, 0, 2)];
                var r = new Rect(x0 + i * (cw + gap), y0, cw, ch);
                bool hover = !festivalStaffOpen && r.Contains(ev.mousePosition);
                DrawUi(r, "panel", PanelBlue, 0.97f);
                DrawFrame(r, hover ? Gold : col, hover ? 4f : 2.5f);
                DrawRect(new Rect(r.x, r.y, r.width, 8), col);
                ShadowLabel(new Rect(r.x, r.y + 14, r.width, 34), $"{def.Label}　{def.Grade}", new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, col);
                ShadowLabel(new Rect(r.x + 8, r.y + 52, r.width - 16, 40), def.Quest, Fit(new GUIStyle(Big) { fontSize = 30, alignment = TextAnchor.MiddleCenter }, def.Quest, r.width - 16, 16), Gold);
                // 敵の顔
                int ni = def.Icons == null ? 0 : def.Icons.Length;
                float isz = ni == 0 ? 0f : Mathf.Min(68f, (r.width - 40f - (ni - 1) * 8f) / ni), ix = r.center.x - (isz * ni + (ni - 1) * 8f) * 0.5f, iy = r.y + 100f;
                for (int k = 0; k < ni; k++)
                {
                    var ir = new Rect(ix + k * (isz + 8f), iy, isz, isz);
                    DrawRect(ir, new Color(0.04f, 0.06f, 0.12f, 0.9f));
                    DrawFrame(ir, new Color(1f, 1f, 1f, 0.25f), 1.5f);
                    DrawUiImage(new Rect(ir.x + 4, ir.y + 4, ir.width - 8, ir.height - 8), def.Icons[k], 1f, ScaleMode.ScaleToFit);
                }
                var es = new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.86f, 0.6f) } };
                GUI.Label(new Rect(r.x + 8, r.y + 176, r.width - 16, 22), "敵：" + def.Enemies, Fit(es, "敵：" + def.Enemies, r.width - 16, 10));
                var rs = new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.7f, 0.94f, 1f) } };
                string rule = WrapJa(def.Rule, rs, r.width - 28);
                GUI.Label(new Rect(r.x + 14, r.y + 202, r.width - 28, 60), rule, rs);
                var fs = new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.88f, 0.9f, 0.97f) } };
                string forText = $"目安 {def.Minutes}・{def.For}";
                GUI.Label(new Rect(r.x + 8, r.y + 264, r.width - 16, 22), forText, Fit(fs, forText, r.width - 16, 10));
                var best = FestivalStats.TodayBest(i, now);
                string bestText = best != null ? $"本日の1位　{best.Rank}・{best.Turns}ターン・{FestivalRank.Time(best.Seconds)}" : "本日の1位　まだ なし";
                ShadowLabel(new Rect(r.x, r.y + 290, r.width, 24), bestText, new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, best != null ? Gold : new Color(0.7f, 0.75f, 0.85f));
                // 下のボタンは見た目だけ：カードのどこを押しても始まる（Hit＝キーボードの選択でも）
                var br = new Rect(r.x + 30, r.yMax - 54, r.width - 60, 44);
                DrawUi(br, "button_gold", new Color(0.6f, 0.45f, 0.15f), hover ? 1f : 0.85f);
                ShadowLabel(br, $"{i + 1}　この クエストを あそぶ", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(0.15f, 0.08f, 0.02f));
                if (!festivalStaffOpen && !showSettings && (Hit(r) || key == i + 1))
                {
                    if (key == i + 1) ev.Use();
                    Sfx.Play(Sfx.Kind.Button);
                    act.StartFestival(i);
                    return;
                }
            }
            var bs = new GUIStyle(Small) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.9f, 0.92f, 1f) } };
            GUI.Label(new Rect(0, y0 + ch + 12, w, 24), "どの LEVEL も、普通に戦っても勝てる。敵を利用すると、ずっと楽に・速く勝てる。クリアするとランク（S〜C）とタイム。", Fit(bs, "どの LEVEL も、普通に戦っても勝てる。敵を利用すると、ずっと楽に・速く勝てる。クリアするとランク（S〜C）とタイム。", w - 40, 11));
            if (!festivalStaffOpen && !festivalHideMain && Button(new Rect(20, h - 52, 240, 36), "本編を遊ぶ（タイトルへ）", false, true, 14)) { if (BuildInfo.FestivalKiosk) mainConfirm = true; else { act.FestivalToMain(); return; } }
            if (!festivalStaffOpen && Button(new Rect(w - 220, h - 52, 200, 36), "設定（音量）", false, true, 14)) showSettings = true;
            GUI.Label(new Rect(280, h - 46, w - 520, 26), $"{BuildInfo.Version}　タイトル・アートは仮", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } });
            if (mainConfirm) DrawMainConfirm(w, h);
            HandleFestivalStaffKey();
            if (festivalStaffOpen) DrawFestivalStaff(w, h); // 設定の窓は ActUI の最後で描く（後ろの入力を止める）
        }

        /// <summary>タイトル（本編）の左：文化祭クエストへの大きな入口。</summary>
        void DrawFestivalTitleCard(float w, float h)
        {
            if (!BuildInfo.Festival) return; // 【通常リリース版・2026-11-16】文化祭の入口は文化祭版だけ（通常版のタイトルには出さない）
            float cw = Mathf.Min(300f, w * 0.5f - 200f);
            if (cw < 200f) return; // 狭い窓では出さない（真ん中のボタンと重なる）
            var r = new Rect(Mathf.Max(24f, w * 0.5f - 160f - 40f - cw), h * 0.36f, cw, 170f);
            bool hover = r.Contains(Event.current.mousePosition);
            DrawUi(r, "panel", PanelBlue, 0.97f);
            DrawFrame(r, hover ? Gold : new Color(1f, 0.6f, 0.45f), hover ? 4f : 3f);
            DrawRect(new Rect(r.x, r.y, r.width, 8), new Color(1f, 0.6f, 0.45f));
            ShadowLabel(new Rect(r.x, r.y + 16, r.width, 34), "文化祭", new GUIStyle(Big) { fontSize = 30, alignment = TextAnchor.MiddleCenter }, Gold);
            ShadowLabel(new Rect(r.x, r.y + 52, r.width, 30), "クエスト", new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter }, Gold);
            GUI.Label(new Rect(r.x + 10, r.y + 86, r.width - 20, 22), "LEVEL 1〜3 から1つ選んで攻略", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
            var br = new Rect(r.x + 24, r.yMax - 52, r.width - 48, 40);
            DrawUi(br, "button_gold", new Color(0.6f, 0.45f, 0.15f), 1f);
            ShadowLabel(br, "あそぶ", new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.15f, 0.08f, 0.02f));
            if (Hit(r)) { Sfx.Play(Sfx.Kind.Button); act.OpenFestival(); }
        }

        /// <summary>F9：スタッフ用の集計を開く／閉じる（遊ぶ人には案内しない）。</summary>
        void HandleFestivalStaffKey()
        {
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.F9) { festivalStaffOpen = !festivalStaffOpen; festivalResetArmed = false; festivalMainResetArmed = false; ev.Use(); }
        }

        float resultEnterAt = -1f;
        ActScreen resultSeenScreen;

        /// <summary>結果：ランク（大きく）・PERFECT・できたこと3つ・タイム・本日のランキング・次の人へ／もう一度／別の LEVEL／もっと遊ぶ（本編）。</summary>
        void DrawFestivalResult(float w, float h)
        {
            var r = act.Festival;
            if (r == null) { act.OpenFestival(); return; }
            if (resultSeenScreen != ActScreen.FestivalResult) { resultSeenScreen = ActScreen.FestivalResult; resultEnterAt = Time.unscaledTime; }
            float t = Time.unscaledTime - resultEnterAt;
            var def = act.FestivalDef;
            string rank = act.FestivalRankLetter ?? "C";
            bool perfect = act.FestivalPerfect;
            Color rc = rank == "S" ? new Color(1f, 0.82f, 0.3f) : rank == "A" ? new Color(0.5f, 0.95f, 1f) : rank == "B" ? new Color(0.55f, 1f, 0.6f) : new Color(0.85f, 0.85f, 0.9f);
            ShadowLabel(new Rect(0, 14, w, 56), "QUEST CLEAR!", new GUIStyle(Big) { fontSize = 46, alignment = TextAnchor.MiddleCenter }, Gold);
            ShadowLabel(new Rect(0, 66, w, 28), $"{def.Label} {def.Grade}　{def.Quest}" + (r.Demo ? "　（お手本：記録しません）" : r.Attempt > 1 ? $"　（{r.Attempt}回目の挑戦）" : ""), new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, FestivalLevelColor[Mathf.Clamp(act.FestivalLevel, 0, 2)]);

            // 紙吹雪はパネルの後ろ（パネルは不透明）：前に出すと文字を隠した（文化祭クエスト v2 の撮影）
            DrawResultConfetti(w, h, t, rank, perfect);
            // 左：ランク（大きく）
            float lx = w * 0.5f - 470f, top = 108f;
            var rr = new Rect(lx, top, 300, 300);
            DrawRect(rr, new Color(0.04f, 0.06f, 0.13f, 1f));
            DrawUi(rr, "panel", PanelBlue, 1f);
            DrawFrame(rr, rc, perfect ? 6f : 4f);
            ShadowLabel(new Rect(rr.x, rr.y + 10, rr.width, 28), "ランク", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Color.white);
            float pop = Mathf.Clamp01(t / 0.35f);
            int rsize = Mathf.RoundToInt(Mathf.Lerp(60f, 170f, pop) + (pop >= 1f && rank == "S" ? Mathf.Sin(t * 4f) * 6f : 0f));
            if (perfect) rsize = Mathf.RoundToInt(rsize * 0.85f); // 【初見レビュー2回目（採用担当）「PERFECT と S が重なる」】PERFECT のときは S を小さく・下へ
            ShadowLabel(new Rect(rr.x, rr.y + (perfect ? 62 : 34), rr.width, perfect ? 180 : 200), rank, new GUIStyle(Big) { fontSize = rsize, alignment = TextAnchor.MiddleCenter }, rc);
            if (perfect) ShadowLabel(new Rect(rr.x, rr.y + 36, rr.width, 30), "★ PERFECT ★", new GUIStyle(Label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.92f, 0.5f, Blink(0.6f, 1f, 2f)));
            string praise = perfect ? "ノーダメージ・1回目で S！" : rank == "S" ? "S ランク！ 敵を使いこなした！" : rank == "A" ? "すごい！ あと1つで S" : rank == "B" ? "クリア！ 敵をもっと利用すると上がる" : "クリア！ 次は 敵に攻撃させよう";
            GUI.Label(new Rect(rr.x, rr.yMax - 76, rr.width, 18), "できたこと 3つ＝S・2つ＝A・1つ＝B", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } });
            ShadowLabel(new Rect(rr.x + 8, rr.yMax - 56, rr.width - 16, 44), praise, Fit(new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, praise, rr.width - 16, 12), rc);

            // 右：タイム・本日の順位・できたこと
            var pr = new Rect(lx + 320, top, 620, 300);
            DrawRect(pr, new Color(0.04f, 0.06f, 0.13f, 1f));
            DrawUi(pr, "panel", PanelBlue, 1f);
            DrawFrame(pr, new Color(Gold.r, Gold.g, Gold.b, 0.7f), 2f);
            ShadowLabel(new Rect(pr.x + 24, pr.y + 12, 300, 40), $"タイム　{FestivalRank.Time(r.Seconds)}", new GUIStyle(Big) { fontSize = 32, alignment = TextAnchor.MiddleLeft, wordWrap = false }, Color.white);
            string placeText = r.Demo ? "お手本は ランキングに入りません" : act.FestivalPlace == 1 ? "本日の1位！" : act.FestivalPlace > 1 && act.FestivalPlace <= 3 ? $"本日の{act.FestivalPlace}位！" : act.FestivalPlace > 3 ? $"本日の{act.FestivalPlace}位" : "";
            if (placeText.Length > 0)
            {
                bool top3 = !r.Demo && act.FestivalPlace >= 1 && act.FestivalPlace <= 3;
                var bc = top3 ? new Color(1f, 0.85f, 0.3f, Blink(0.55f, 1f, 3f)) : new Color(0.8f, 0.85f, 0.95f);
                ShadowLabel(new Rect(pr.x + 320, pr.y + 12, pr.width - 340, 40), placeText, new GUIStyle(Label) { fontSize = top3 ? 26 : 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false }, bc);
            }
            var checks = FestivalRank.Checks(def, r);
            var names = FestivalRank.CheckNames(def);
            string[] values =
            {
                EnemyUseBreakdown(r),
                r.Damage == 0 ? "ダメージ 0" : $"受けたダメージ {r.Damage}",
                $"{r.Turns}ターン（目安 {def.ParTurns}）",
            };
            float cy = pr.y + 66;
            for (int i = 0; i < 3; i++)
            {
                var row = new Rect(pr.x + 20, cy, pr.width - 40, 56);
                DrawRect(row, new Color(0.04f, 0.06f, 0.12f, 0.8f));
                bool ok = checks[i];
                ShadowLabel(new Rect(row.x + 8, row.y, 40, row.height), ok ? "✓" : "・", new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, ok ? ClearGreen : new Color(0.55f, 0.58f, 0.65f));
                ShadowLabel(new Rect(row.x + 52, row.y + 3, row.width - 60, 26), names[i], new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false }, ok ? Color.white : new Color(0.7f, 0.73f, 0.8f));
                GUI.Label(new Rect(row.x + 52, row.y + 29, row.width - 60, 24), values[i], Fit(new GUIStyle(Small) { fontSize = 15, wordWrap = false, normal = { textColor = new Color(0.82f, 0.88f, 1f) } }, values[i], row.width - 60, 10));
                cy += 62;
            }
            string perfectRule = $"今回の最大の連鎖：1ターンに敵へ {r.BestTurnDamage}ダメージ　／　★ PERFECT＝S ＋ ノーダメージ ＋ 1回目の挑戦";
            GUI.Label(new Rect(pr.x + 20, pr.yMax - 30, pr.width - 40, 22), perfectRule, Fit(new GUIStyle(Tiny) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.88f, 0.6f, 0.85f) } }, perfectRule, pr.width - 40, 10));

            // 下：本日のランキング（この LEVEL だけ）・本編への一言・ボタン
            var ranking = FestivalStats.TodayTop(act.FestivalLevel, System.DateTime.Now, 3);
            float ry = top + 312;
            string rankLine = ranking.Count == 0 ? $"本日のランキング（{def.Label}）：まだ なし" : $"本日のランキング（{def.Label}・この PC で）　" + string.Join("　", ranking.Select((x, k) => $"{k + 1}位 {x.Rank} {x.Turns}ターン {FestivalRank.Time(x.Seconds)}" + (x.Perfect ? "★" : "")));
            ShadowLabel(new Rect(0, ry, w, 26), rankLine, Fit(new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, rankLine, w - 40, 12), new Color(1f, 0.85f, 0.45f));
            ShadowLabel(new Rect(0, ry + 30, w, 24), FestivalContent.MainGameLine, Fit(new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, FestivalContent.MainGameLine, w - 40, 11), CyanText);
            float bw = 228f, gap = 14f, by = ry + 64f, bx = w * 0.5f - (bw * 4f + gap * 3f) * 0.5f;
            bool ready = t > 1.0f; // 出てすぐのクリックで飛ばさない
            if (r.Demo)
            {
                // お手本のあと：来場者の番（同じ LEVEL を最初から・開始の窓）
                if (Button(new Rect(w * 0.5f - 200, by, 400, 56), "来場者の番へ（同じ LEVEL を最初から）", true, ready, 18) && ready) { act.FestivalAgain(); return; }
                if (Button(new Rect(w * 0.5f - 120, by + 66, 240, 40), "LEVEL を選ぶ画面へ", false, ready, 15) && ready) { act.FestivalOtherLevel(); return; }
            }
            else
            {
                if (Button(new Rect(bx, by, bw, 56), "次の人へ", true, ready, 21) && ready) { act.FestivalNextPerson(); return; }
                if (Button(new Rect(bx + (bw + gap), by, bw, 56), "もう一度", false, ready, 19) && ready) { act.FestivalAgain(); return; }
                if (Button(new Rect(bx + (bw + gap) * 2, by, bw, 56), "別の LEVEL", false, ready, 19) && ready) { act.FestivalOtherLevel(); return; }
                if (!festivalHideMain && Button(new Rect(bx + (bw + gap) * 3, by, bw, 56), "もっと遊ぶ（本編）", false, ready, 18) && ready) { if (BuildInfo.FestivalKiosk) mainConfirm = true; else { act.FestivalToMain(); return; } }
                var cap = new GUIStyle(Tiny) { fontSize = 13, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } };
                GUI.Label(new Rect(bx, by + 58, bw, 18), "人が替わるときは こちら", cap);
                GUI.Label(new Rect(bx + (bw + gap), by + 58, bw, 18), $"同じ {def.Label} を もう一度", cap);
                string nextHint = act.FestivalLevel + 1 < FestivalContent.Levels.Length ? $"次は {FestivalContent.Levels[act.FestivalLevel + 1].Label} はどう？" : "LEVEL 1〜3 から選ぶ";
                GUI.Label(new Rect(bx + (bw + gap) * 2, by + 58, bw, 18), "同じ人が続けて：" + nextHint, Fit(cap, "同じ人が続けて：" + nextHint, bw, 10));
                if (!festivalHideMain) GUI.Label(new Rect(bx + (bw + gap) * 3, by + 58, bw, 18), "8階のボスを目指す（約25〜30分）", cap);
            }
            if (mainConfirm) DrawMainConfirm(w, h);
            HandleFestivalStaffKey();
            if (festivalStaffOpen) DrawFestivalStaff(w, h);
        }

        bool mainConfirm;

        /// <summary>【文化祭・10歳の立場のレビュー「敵の攻撃 0 の意味が分からない」】敵を利用した攻撃の内訳（0 の物は出さない）：「まね 5回＋敵の攻撃 2回＝7回」。</summary>
        static string EnemyUseBreakdown(FestivalRun r)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (r.CopyHits > 0) parts.Add($"まね {r.CopyHits}回");
            if (r.FriendlyHits > 0) parts.Add($"敵の攻撃 {r.FriendlyHits}回");
            if (r.HazardHits > 0) parts.Add($"罠 {r.HazardHits}回");
            if (r.BigHits > 0) parts.Add($"大技 {r.BigHits}回");
            return parts.Count == 0 ? "敵を利用した攻撃 0回（自分で斬った）" : string.Join("＋", parts) + $"＝{r.EnemyHits}回";
        }

        /// <summary>【文化祭・採用担当のレビュー「本編を押されると展示の PC が約30分ふさがる」】展示の PC では「本編は約25〜30分」を確かめてから。</summary>
        void DrawMainConfirm(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.7f));
            var r = new Rect(w * 0.5f - 300, h * 0.5f - 120, 600, 240);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.98f));
            DrawFrame(r, Gold, 3f);
            ShadowLabel(new Rect(r.x, r.y + 22, r.width, 40), FestivalContent.MainGameMinutes, new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, Gold);
            GUI.Label(new Rect(r.x + 20, r.y + 70, r.width - 40, 50), "並んでいる人がいたら、ゆずってね。", new GUIStyle(Small) { fontSize = 16, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
            if (Button(new Rect(r.x + 40, r.yMax - 70, 240, 48), "本編をはじめる", false, true, 18)) { mainConfirm = false; act.FestivalToMain(); return; }
            if (Button(new Rect(r.xMax - 280, r.yMax - 70, 240, 48), "もどる", true, true, 18)) mainConfirm = false;
        }

        /// <summary>結果の紙吹雪（S・PERFECT は多め）。画面の揺れ・点滅を止める設定では出さない。</summary>
        void DrawResultConfetti(float w, float h, float t, string rank, bool perfect)
        {
            if (ScreenFx.Disabled) return;
            int n = perfect ? 170 : rank == "S" ? 120 : rank == "A" ? 80 : 50;
            Color[] cols = { new Color(1f, 0.85f, 0.35f), new Color(0.45f, 0.9f, 1f), new Color(1f, 0.5f, 0.6f), new Color(0.6f, 1f, 0.6f), new Color(0.85f, 0.6f, 1f) };
            float fade = Mathf.Clamp01(6f - t * 0.6f);
            if (fade <= 0f) return;
            for (int i = 0; i < n; i++)
            {
                float seed = i * 78.233f;
                float fx = Mathf.Repeat(Mathf.Sin(seed) * 43758.5453f, 1f);
                float speed = 70f + 60f * Mathf.Repeat(Mathf.Sin(seed * 1.3f) * 9631.13f, 1f);
                float y = -20f + (t * speed + fx * 400f) % (h + 40f);
                float x = fx * w + Mathf.Sin(t * 2.5f + i) * 18f;
                float s = 11f + 7f * Mathf.Repeat(fx * 5.1f, 1f);
                var c = cols[i % cols.Length]; c.a = 0.95f * fade;
                DrawRect(new Rect(x, y, s, s * 0.6f), c);
            }
        }

        /// <summary>スタッフ用：本日の集計・全部の集計・記録のファイルの場所・本日のベストを消す・ゲームを終了。</summary>
        void DrawFestivalStaff(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.75f));
            var r = new Rect(w * 0.5f - 440, 50, 880, h - 100);
            DrawRect(r, new Color(0.03f, 0.04f, 0.08f, 0.98f));
            DrawFrame(r, Gold, 2f);
            ShadowLabel(new Rect(r.x, r.y + 12, r.width, 32), "スタッフ用：文化祭の記録（F9 で閉じる）", new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter }, Gold);
            var now = System.DateTime.Now;
            var st = new GUIStyle(Small) { fontSize = 16, wordWrap = false, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            float y = r.y + 56;
            foreach (var line in FestivalStats.Summary(now, true).Concat(new[] { "" }).Concat(FestivalStats.Summary(now, false)))
            {
                GUI.Label(new Rect(r.x + 30, y, r.width - 60, 24), line, Fit(st, line, r.width - 60, 11));
                y += 26;
            }
            y += 8;
            var ss = new GUIStyle(st) { fontSize = 14, wordWrap = true };
            string howto = "お手本：LEVEL の開始の窓で「お手本（スタッフ用）」か、戦闘中に F8。お手本の挑戦は記録しない。終わったら右上の「最初からやり直す（来場者の番へ）」。";
            GUI.Label(new Rect(r.x + 30, y, r.width - 60, 40), WrapJa(howto, ss, r.width - 60), ss);
            y += 44;
            string path = FestivalStats.CsvPath;
            GUI.Label(new Rect(r.x + 30, y, r.width - 60, 40), WrapJa("記録のファイル（CSV・個人の情報なし）：" + path, new GUIStyle(st) { fontSize = 13 }, r.width - 60), new GUIStyle(st) { fontSize = 13, wordWrap = true });
            y += 44;
            GUI.Label(new Rect(r.x + 30, y, r.width - 60, 22), "記録するもの：日時・LEVEL・終わり方（クリア／倒れた／やり直し／やめた）・タイム・ターン・被ダメ・敵を利用した攻撃・ランク・PERFECT・何回目・ヒントの数", Fit(new GUIStyle(st) { fontSize = 13 }, "記録するもの：日時・LEVEL・終わり方（クリア／倒れた／やり直し／やめた）・タイム・ターン・被ダメ・敵を利用した攻撃・ランク・PERFECT・何回目・ヒントの数", r.width - 60, 10));
            float by = r.yMax - 64;
            if (Button(new Rect(r.x + 30, by, 270, 44), festivalResetArmed ? "本当に消す？（もう一度）" : "本日のランキングを消す（2回押す）", false, true, 14))
            {
                if (festivalResetArmed) { FestivalStats.ResetTodayBest(now); festivalResetArmed = false; }
                else festivalResetArmed = true;
            }
            if (Button(new Rect(r.x + 320, by, 250, 44), festivalHideMain ? "本編へのボタン：隠している" : "本編へのボタン：出している", festivalHideMain, true, 14)) festivalHideMain = !festivalHideMain;
            // 【2026-11-14 本人】本編を はじめての状態に（前の人がクリアしても、次の人が1階の練習・チュートリアルの案内から遊べる。「次の人へ」でも自動で戻る）
            if (Button(new Rect(r.x + 30, by - 54, 270, 40), festivalMainResetArmed ? "本当に戻す？（もう一度）" : "本編を はじめての状態に（2回押す）", false, true, 14))
            {
                if (festivalMainResetArmed) { act.FestivalResetMainGame(); festivalMainResetArmed = false; festivalMainResetAt = Time.unscaledTime; }
                else festivalMainResetArmed = true;
            }
            if (festivalMainResetAt > 0f && Time.unscaledTime - festivalMainResetAt < 4f)
                GUI.Label(new Rect(r.x + 312, by - 54, 300, 40), "本編を はじめての状態に戻しました", new GUIStyle(st) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.6f, 1f, 0.7f) } });
            if (!IsWeb && Button(new Rect(r.xMax - 250, by - 54, 220, 40), "ゲームを終了", false, true, 14)) Application.Quit();
            if (Button(new Rect(r.xMax - 250, by, 220, 44), "閉じる", true, true, 16)) { festivalStaffOpen = false; festivalResetArmed = false; festivalMainResetArmed = false; }
        }

        /// <summary>展示の PC：戦闘以外の画面での「まだ遊んでいますか？」（本編のランの扉・報酬など）。</summary>
        void DrawFestivalIdleOverlay(float w, float h)
        {
            if (act == null || !act.FestivalIdlePrompt) return;
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.72f));
            var r = new Rect(w * 0.5f - 300, h * 0.5f - 120, 600, 240);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.98f));
            DrawFrame(r, Gold, 3f);
            ShadowLabel(new Rect(r.x, r.y + 24, r.width, 44), "まだ遊んでいますか？", new GUIStyle(Big) { fontSize = 32, alignment = TextAnchor.MiddleCenter }, Gold);
            GUI.Label(new Rect(r.x, r.y + 78, r.width, 26), $"{Mathf.CeilToInt(act.FestivalIdleLeft)}秒後に、はじめの画面へ戻ります", new GUIStyle(Small) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
            var ev = Event.current;
            bool any = ev.type == EventType.KeyDown || ev.type == EventType.MouseDown;
            if (Button(new Rect(r.center.x - 130, r.yMax - 76, 260, 50), "続ける", true, true, 20) || any) { if (any) ev.Use(); act.FestivalKeepPlaying(); }
        }
    }
}
