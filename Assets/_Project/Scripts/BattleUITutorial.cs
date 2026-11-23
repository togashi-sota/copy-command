using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【RC3・2026-10-21 本人の初プレイ】チュートリアルの画面（BattleUI の partial）：左下の欄に「やること」、できたら「できた！」の窓。
    /// 戦闘の画面にほかの説明は足さない（必要なときだけ出す）。
    /// </summary>
    public partial class BattleUI
    {
        bool InTutorialUi => act != null && act.InTutorial && act.InBattle;
        float festivalQuitArmedAt = -99f;

        /// <summary>左下（ふだんは目的・凡例の欄）：チュートリアルの「やること」。</summary>
        void DrawActTutorialPanel(Rect r)
        {
            var steps = act.TutorialSteps; // 【RC9】チュートリアル／5分体験
            int step = Mathf.Clamp(act.TutorialStep, 0, steps.Length - 1);
            var def = steps[step];
            DrawRect(r, new Color(0.03f, 0.05f, 0.1f, 0.94f));
            DrawFrame(r, Gold, 2f);
            string limit = def.TurnLimit > 0 && !(act.QuickMode && !string.IsNullOrEmpty(def.Mission)) ? $"　あと{Mathf.Max(0, def.TurnLimit - turn.TurnNumber + 1)}ターン" + (def.NoDamage ? "・無傷で" : "") : "";
            string head = act.FestivalMode ? $"{act.FestivalDef.Label}　{act.FestivalDef.Quest}" : act.QuickMode ? $"MISSION {step + 1}/{steps.Length}　{def.Name}{limit}" : $"チュートリアル {step + 1}/{steps.Length}　{def.Name}"; // 【文化祭】 // 【RC12・初見テスト「（答えは出ない）と書いてあるのに『答えを見る』がある」】見出しから外した
            var headR = new Rect(r.x + 12, r.y + 6, r.width - (act.HasTutorialHint && (!act.AnswerPreviewOn || act.FestivalMode) ? 444 : 318), 26); // 【RC11】ヒントのボタンの分 // 【RC10・ブラウザ版の確認】「（結果の予告なし）」で見出しが「やり直す」のボタンに重なった → ボタンの左まで・入りきらなければ小さく
            ShadowLabel(headR, head, Fit(new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false }, head, headR.width, 12), Gold);
            // 【文化祭】やめるは2回押す（子どもが押してしまっても、すぐには戻らない）
            bool quitArmed = act.FestivalMode && Time.unscaledTime - festivalQuitArmedAt < 3f;
            string quitLabel = act.FestivalMode ? (quitArmed ? "本当にやめる？（もう一度）" : "やめる（LEVEL 選択へ）") : act.QuickMode ? "5分体験をやめる" : "チュートリアルをやめる";
            if (ThemedButton(new Rect(r.xMax - 172, r.y + 6, 164, 24), quitLabel, quitArmed, false, 12))
            {
                if (act.FestivalMode && !quitArmed) festivalQuitArmedAt = Time.unscaledTime;
                else { festivalQuitArmedAt = -99f; act.SkipTutorial(); return; }
            }
            if (act.HasTutorialHint && (!act.AnswerPreviewOn || act.FestivalMode) && !act.TutorialStepDone) // 【文化祭】予告ありの はじめても ヒントを出す // 【RC12・見直し】解けた後はヒントのボタンを出さない
            {
                // 【RC11】答えの出ない3〜5問目だけ（H は手札整理のキーなので、ボタンだけ）【RC12】段階ヒント 1/3 → 2/3 → 3/3 → 答えを見る
                var hb = new Rect(r.xMax - 426, r.y + 6, 120, 24); // 【RC12】「ヒント（あと3）」の分だけ広く
                if (act.HintLevel < act.HintCount) { if (ThemedButton(hb, $"ヒント（あと{act.HintCount - act.HintLevel}）", false, false, 12)) act.ShowTutorialHint(); } // 【RC12・初見テスト「1/3 は『3つ中の1つ目を見た』に読める」】
                else if (act.CanShowAnswer) { if (ThemedButton(hb, "答えを見る", false, true, 12)) act.ShowAnswer(); }
            }
            if (act.FestivalMode && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.R) // 【文化祭クエスト v2】最初からやり直す（右上のボタンと同じ：来場者は2回押す・お手本は1回）
            {
                Event.current.Use();
                if (act.FestivalDemo || Time.unscaledTime - festivalRestartArmedAt < 3f) { festivalRestartArmedAt = -99f; act.FestivalRestart(); return; }
                festivalRestartArmedAt = Time.unscaledTime;
            }
            if (!act.FestivalMode && (ThemedButton(new Rect(r.xMax - 300, r.y + 6, 120, 24), "やり直す（R）", false, false, 12) || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.R))) { if (Event.current.type == EventType.KeyDown) Event.current.Use(); act.RestartTutorialStep(); return; } // 【RC4・レビュー】段階のはじめから（詰まったとき）【RC5】R キーでも
            var body = new GUIStyle(Small) { fontSize = 15, wordWrap = false, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            float y = r.y + 36;
            // 【RC11・ブラウザ版の確認】ヒント／やり直しの一言が、ルールの最後の行（「兵A は全部まねる」など）を隠していた
            //   → 今の配置（V2）では右下の欄の上へ（DrawTutorialFeedbackBox）。前の配置だけ、入るまで本文を小さくしてここに出す
            var fs = new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true, clipping = TextClipping.Clip };
            string fb = string.IsNullOrEmpty(act.TutorialFeedback) || V2Layout ? null : "→ " + act.TutorialFeedback;
            float fh = fb == null ? 0f : Mathf.Max(22f, fs.CalcHeight(new GUIContent(fb), r.width - 24));
            // 【RC12・見直し】3行の問題（3・4問目）で最後の行が欄の下で切れていた → ヒントの一言が無くても、入るまで小さく
            {
                float top = y + (act.QuickMode && !string.IsNullOrEmpty(def.Mission) ? (def.Lines.Length >= 3 ? 21f : 24f) : 0f);
                for (int size = 15; size >= 11; size--) // 【第2巡 9「MISSION の欄の3行目が下の縁で切れる」】字の高さを少し多めに見て（+5）、縁の上 6px に収まるまで小さく（11 まで）
                {
                    body.fontSize = size;
                    float need = 0f;
                    foreach (var rawLine in def.Lines) need += Mathf.Max(size + 5f, body.CalcHeight(new GUIContent(WrapJa(rawLine, body, r.width - 24)), r.width - 24)) + 1f;
                    if (top + need + fh <= r.yMax - 6f) break;
                }
            }
            if (act.QuickMode && !string.IsNullOrEmpty(def.Mission))
            {
                // 【RC11・初見テスト（ゲームをしない人）「窓を閉じたら、何体倒せばいいか書いていない」】MISSION の目的を左下の1行目に残す
                string goal = "目的：" + def.Mission;
                var gs = new GUIStyle(body) { fontStyle = FontStyle.Bold, fontSize = 15 };
                ShadowLabel(new Rect(r.x + 12, y, r.width - 24, 22), goal, Fit(gs, goal, r.width - 24, 11), Gold);
                y += def.Lines.Length >= 3 ? 21 : 24; // 【RC12】3行の問題は少し詰める
            }
            foreach (var rawLine in def.Lines)
            {
                string line = WrapJa(rawLine, body, r.width - 24); // 【RC5・最終検証】禁則（「まねしな／い。」の1文字の行）
                float hgt = Mathf.Max(body.fontSize + 5f, body.CalcHeight(new GUIContent(line), r.width - 24)); // 【第2巡 9】上の計算と同じ
                GUI.Label(new Rect(r.x + 12, y, r.width - 24, hgt), line, body);
                y += hgt + 1;
            }
            if (fb != null)
            {
                // 【RC4・レビュー】長い一言が「…倒そ」で切れていた → 折り返す（欄に入らなければ上へずらす）
                float fy = Mathf.Min(y + 2, r.yMax - fh - 2);
                DrawRect(new Rect(r.x + 8, fy - 1, r.width - 16, fh + 2), new Color(0.03f, 0.05f, 0.1f, 0.94f));
                ShadowLabel(new Rect(r.x + 12, fy, r.width - 24, fh), fb, fs, new Color(1f, 0.72f, 0.4f));
            }
        }

        /// <summary>【RC11】チュートリアル・5分体験のヒント／やり直しの一言（今の配置）：右下の説明の欄の上に、枠つきで出す。返り値は下の端。</summary>
        float DrawTutorialFeedbackBox(float x, float y, float w)
        {
            if (!InTutorialUi || act.TutorialStepDone) return y - 6f; // 【RC12・初見テスト「解けた後も『次は：…』が残る」】
            // 【RC12】詰まったら（ヒントを全部見る前でも、2回やり直す・2分半たつと）「答えを見る」をここにも
            bool answerBtn = act.CanShowAnswer && act.HintLevel < act.HintCount;
            var nextStep = act.NextAnswerStep();
            string text = act.TutorialFeedback;
            if (nextStep.HasValue) text = (string.IsNullOrEmpty(text) ? "" : text + "\n") + $"次は：光っている「{act.CardDisplayName(nextStep.Value.card)}」を選んで、盤面の {ActRunManager.DirGlyph(nextStep.Value.dir)} の矢印を押す"; // 【RC12・初見テスト「↑ をどこで押すのか」】
            if (string.IsNullOrEmpty(text) && answerBtn) text = "詰まったら、答えを見てから 自分で操作してもいい";
            if (string.IsNullOrEmpty(text)) return y - 6f;
            var fs = new GUIStyle(Small) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true, clipping = TextClipping.Clip };
            string fb = WrapJa("→ " + text, fs, w - 24);
            float fh = Mathf.Max(24f, fs.CalcHeight(new GUIContent(fb), w - 24)) + (answerBtn ? 30f : 0f);
            var r = new Rect(x, y, w, fh + 14);
            DrawRect(r, new Color(0.06f, 0.05f, 0.03f, 0.96f));
            DrawFrame(r, new Color(1f, 0.72f, 0.4f), 2f);
            if (Time.unscaledTime - act.HintShownAt < 2f) DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.85f, 0.3f, Blink(0.4f, 1f, 6f)), 3f); // 【RC12・初見テスト4回目「ヒントがボタンから遠い右の欄に出る」】出た直後は光らせる
            ShadowLabel(new Rect(r.x + 12, r.y + 7, r.width - 24, fh - (answerBtn ? 30f : 0f)), fb, fs, new Color(1f, 0.78f, 0.48f));
            if (answerBtn && ThemedButton(new Rect(r.xMax - 132, r.yMax - 34, 120, 26), "答えを見る", false, true, 12)) act.ShowAnswer();
            return r.yMax;
        }

        /// <summary>【RC11】5分体験の戦闘（山札・捨て札・長い操作説明を出さない）。</summary>
        bool QuickUi => act != null && act.InTutorial && act.InBattle && act.QuickMode;
        /// <summary>【RC12】まね方の名前（全まね など）を出さない：5分体験の1・2問目【文化祭】はじめて（ふつう・挑戦は出す：挑戦1は まね方3種の部屋）。</summary>
        bool HideCopyKindNames => QuickUi && !act.FestivalMode && act.TutorialStep < 1; // 【通常リリース版】3問になったので、名前（全まね）は2問目から // 【文化祭クエスト v2】文化祭は本編と同じ（まね方の名前を出す）

        /// <summary>【RC11】5分体験：問題の前の MISSION の窓が開いている。</summary>
        bool MissionCardOpen => act != null && act.InTutorial && act.InBattle && act.QuickMode && act.MissionOpen;

        /// <summary>
        /// 【RC11・本人「5分体験は何をすればいいのか左下に少し出るだけで分からない。最初に画面全体にバーンと出して」】
        /// 問題の前に、画面の真ん中へ MISSION（番号・★の難しさ・目的の1行・ルールの一言）。開始（Enter）で盤面を触れる。答えは書かない。
        /// </summary>
        void DrawMissionCard(float bw, float totalW)
        {
            if (act.FestivalMode) { DrawFestivalMission(bw, totalW); return; } // 【文化祭・初見レビュー「窓が盤面を隠す」】右に出して盤面を見せる
            var steps = act.TutorialSteps;
            int step = Mathf.Clamp(act.TutorialStep, 0, steps.Length - 1);
            var def = steps[step];
            DrawRect(new Rect(0, 0, totalW, RefHeight), new Color(0f, 0f, 0f, 0.62f));
            float cw = Mathf.Min(760f, totalW - 80f);
            var r = new Rect((totalW - cw) / 2f, RefHeight * 0.5f - 170, cw, 330);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.97f));
            DrawFrame(r, Gold, 3f);
            DrawRect(new Rect(r.x, r.y, r.width, 6), Gold);
            string stars = new string('★', Mathf.Clamp(def.Stars, 0, 5)) + new string('☆', Mathf.Clamp(5 - def.Stars, 0, 5));
            string missionHead = act.FestivalMode ? (def.Boss ? "ミニボス" : $"第{step + 1}戦") + $"　{act.FestivalDef.Label}" : $"MISSION {step + 1}／{steps.Length}"; // 【文化祭】
            ShadowLabel(new Rect(r.x, r.y + 18, r.width, 44), missionHead, new GUIStyle(Big) { fontSize = 36, alignment = TextAnchor.MiddleCenter }, def.Boss && act.FestivalMode ? new Color(1f, 0.6f, 0.4f) : Gold);
            ShadowLabel(new Rect(r.x, r.y + 64, r.width, 26), act.FestivalMode ? def.Name : $"{def.Name}　{stars}", new GUIStyle(Label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.85f, 0.45f));
            var ms = new GUIStyle(Label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            string mission = WrapJa(def.Mission ?? "", ms, r.width - 60);
            float mh = ms.CalcHeight(new GUIContent(mission), r.width - 60);
            ShadowLabel(new Rect(r.x + 30, r.y + 104, r.width - 60, mh), mission, ms, Color.white);
            if (!string.IsNullOrEmpty(def.MissionRule))
            {
                var rs = new GUIStyle(Small) { fontSize = 16, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.62f, 0.95f, 1f) } };
                string rule = WrapJa(def.MissionRule, rs, r.width - 80);
                GUI.Label(new Rect(r.x + 40, r.y + 112 + mh, r.width - 80, 60), rule, rs);
            }
            string tail = act.FestivalMode ? $"HP {act.FestivalDef.Hp}・ターンの制限なし" + (act.AnswerPreviewOn ? "・向きにマウスを乗せると、結果が見える" : "・結果の予告なし（自分で読む）") : def.TurnLimit > 0 ? $"{def.TurnLimit}ターン以内" + (def.NoDamage ? "・無傷で" : "") + (def.BestTurns > 0 ? $"（{def.BestTurns}ターン・無傷なら ★きれいな解）" : "") + (act.AnswerPreviewOn ? "" : "・結果の予告なし（自分で読む）") : act.AnswerPreviewOn ? "向きにマウスを乗せると、結果が見える（練習）" : "結果の予告なし：自分で読む";
            GUI.Label(new Rect(r.x, r.yMax - 92, r.width, 22), tail, new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.8f, 0.55f) } });
            var ev = Event.current;
            bool key = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space);
            if (ThemedButton(new Rect(r.center.x - 120, r.yMax - 62, 240, 46), "開始（Enter）", true, false, 19) || key) { if (key) ev.Use(); Sfx.Play(Sfx.Kind.Button); act.StartMission(); }
        }

        // ───── 【RC12】5分体験の入口：何のゲームか（絵で）→ やってみる → 名前（全まね など）はその後 ─────

        void DrawQuickIntro(float totalW)
        {
            DrawRect(new Rect(0, 0, totalW, RefHeight), new Color(0f, 0f, 0f, 0.8f));
            float cw = Mathf.Min(1140f, totalW - 40f);
            var r = new Rect((totalW - cw) / 2f, 22f, cw, RefHeight - 44f);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.98f));
            DrawFrame(r, Gold, 3f);
            DrawRect(new Rect(r.x, r.y, r.width, 6), Gold);
            ShadowLabel(new Rect(r.x, r.y + 18, r.width, 54), "あなたの命令を、敵もまねる。", new GUIStyle(Big) { fontSize = 40, alignment = TextAnchor.MiddleCenter }, Gold);
            var sub = new GUIStyle(Label) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            string subText = "敵を直接たたくだけではない。敵に あなたの命令を使わせて戦う、ターン制の戦術ゲーム。";
            ShadowLabel(new Rect(r.x + 20, r.y + 74, r.width - 40, 28), subText, Fit(sub, subText, r.width - 40, 13), Color.white);
            float gap = 46f;
            float pw = (r.width - 40f - gap * 2f) / 3f, ph = 292f, py = r.y + 118f;
            for (int i = 0; i < 3; i++)
            {
                var pr = new Rect(r.x + 20f + i * (pw + gap), py, pw, ph);
                DrawIntroPanel(pr, i);
                if (i < 2) ShadowLabel(new Rect(pr.xMax, py + ph * 0.5f - 22f, gap, 44f), "▶", new GUIStyle(Big) { fontSize = 30, alignment = TextAnchor.MiddleCenter }, Gold);
            }
            var bs = new GUIStyle(Small) { fontSize = 17, wordWrap = false, normal = { textColor = new Color(0.9f, 0.94f, 1f) } };
            string[] lines = new[]
            {
                "・敵は それぞれの位置から、同じ向き・同じ距離で まねる。だれもいない方への斬撃でも、まねた敵の攻撃は当たる。",
                "・動きもまねる：同じ「突進」で、あなたは安全な所へ、敵だけを危険（敵の攻撃・落石）へ送れる。",
            };
            float by = py + ph + 16f;
            foreach (var l in lines)
            {
                string w = WrapJa(l, bs, r.width - 80f);
                float h = bs.CalcHeight(new GUIContent(w), r.width - 80f);
                GUI.Label(new Rect(r.x + 40f, by, r.width - 80f, h), w, bs);
                by += h + 6f;
            }
            GUI.Label(new Rect(r.x, r.yMax - 92f, r.width, 22f), act.FestivalMode ? "敵の位置・敵の攻撃・床の仕掛けを利用して、数ターンで攻略しよう。" : "このあと 3つのミッションで、遊びながら覚える（詰まったらヒントと答えあり）", new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.8f, 0.55f) } });
            var ev = Event.current;
            bool key = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space);
            if (ThemedButton(new Rect(r.center.x - 130f, r.yMax - 64f, 260f, 48f), "やってみる（Enter）", true, false, 20) || key) { if (key) ev.Use(); Sfx.Play(Sfx.Kind.Button); act.CloseQuickIntro(); }
        }

        /// <summary>【RC12】入口の3コマ：① あなたが空振りの斬撃 → ② 敵Aも同じ向きに斬撃 → ③ 敵Bに当たる（同士討ち）。盤面は 4×2 マス。</summary>
        void DrawIntroPanel(Rect pr, int idx)
        {
            DrawRect(pr, new Color(0.06f, 0.08f, 0.15f, 0.98f));
            DrawFrame(pr, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 2f);
            bool mir = false; // 【文化祭クエスト v2】旧版（はじめての1戦目が ←）に合わせた向きの反転は外した
            string cut = "斬撃";
            string[] heads = { $"あなた：{cut} {(mir ? "←" : "→")}", $"敵も まねる：{cut} {(mir ? "←" : "→")}", "敵どうしで 同士討ち！" }; // 【RC12・初見テスト「①②③が、戦闘の上の順番の帯（①あなた ②敵の攻撃…）と食い違う」】番号は外した
            string[] subs = { "だれもいない方へ（空振り）", "敵Aも 自分の位置から 同じ向きに", "あなたは一度も当てていない" };
            Color[] hc = { new Color(0.6f, 0.82f, 1f), new Color(0.4f, 0.95f, 1f), new Color(1f, 0.62f, 0.5f) };
            var hs = new GUIStyle(Label) { fontSize = 21, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(new Rect(pr.x + 6f, pr.y + 8f, pr.width - 12f, 30f), heads[idx], Fit(hs, heads[idx], pr.width - 12f, 13), hc[idx]);
            var ss = new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.85f, 0.88f, 0.96f) } };
            GUI.Label(new Rect(pr.x + 6f, pr.y + 38f, pr.width - 12f, 20f), subs[idx], Fit(ss, subs[idx], pr.width - 12f, 10));
            float c = Mathf.Min((pr.width - 36f) / 4f, 64f);
            var g0 = new Vector2(pr.center.x - c * 2f, pr.y + 92f); // 【RC12・見直し】上に「あなた」「空振り」の札
            Rect Cell(int x, int y) => new Rect(g0.x + (mir ? 3 - x : x) * c, g0.y + y * c, c - 3f, c - 3f);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 4; x++)
                {
                    var cr = Cell(x, y);
                    DrawRect(cr, (x + y) % 2 == 0 ? new Color(0.22f, 0.25f, 0.32f) : new Color(0.19f, 0.22f, 0.29f));
                    DrawFrame(cr, new Color(0f, 0f, 0f, 0.5f), 1f);
                }
            void Unit(int x, int y, string key, string letter, Color frame)
            {
                var cr = Cell(x, y);
                var ir = new Rect(cr.x + 4f, cr.y + 4f, cr.width - 8f, cr.height - 8f);
                if (!DrawUiImage(ir, key, 1f, ScaleMode.ScaleAndCrop)) { DrawRect(ir, frame); }
                if (frame.a > 0f) DrawFrame(cr, frame, 3f);
                if (letter != null)
                {
                    var lr = new Rect(cr.x + 2f, cr.yMax - 21f, 36f, 19f);
                    DrawRect(lr, new Color(0f, 0f, 0f, 0.7f));
                    ShadowLabel(lr, "敵" + letter, new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, letter == "A" ? EnemyIdColors[0] : EnemyIdColors[1]);
                }
            }
            void Slash(int x, int y, bool hit)
            {
                var cr = Cell(x, y);
                // 【RC12・初見テスト「赤い斬撃が、本編の赤（敵の攻撃）と同じ色」】あなたの命令とまねは水色（本編のまねの色）
                DrawRect(cr, hit ? new Color(0.2f, 0.75f, 0.95f, 0.5f) : new Color(0.3f, 0.7f, 1f, 0.2f));
                DrawUiImage(new Rect(cr.x + 6f, cr.y + 6f, cr.width - 12f, cr.height - 12f), "cardart_slash", hit ? 0.55f : 0.75f, ScaleMode.ScaleToFit); // 【RC12・見直し】当たるマスは半透明（下の敵Bが見える）
                DrawFrame(cr, new Color(0.45f, 0.9f, 1f), 2f);
            }
            var none = new Color(0f, 0f, 0f, 0f);
            var you = new Color(0.4f, 0.85f, 1f);
            var copy = new Color(0.35f, 0.95f, 1f);
            // あなた（上の段の左）・敵A（下の段の左）・敵B（敵Aの右）
            Unit(0, 0, "portrait_player", null, idx == 0 ? you : none);
            TagAbove(Cell(0, 0), "あなた", you);
            Unit(0, 1, "portrait_normal", "A", idx == 1 ? copy : none);
            if (idx < 2) Unit(1, 1, "portrait_normal", "B", none);
            if (idx == 0) { Slash(1, 0, false); TagAbove(Cell(1, 0), "空振り", new Color(0.85f, 0.88f, 0.95f)); }
            if (idx == 1) { Slash(1, 1, true); TagUnder(Cell(0, 1), "まねる", copy); }
            if (idx == 2)
            {
                Unit(1, 1, "portrait_normal", "B", new Color(1f, 0.35f, 0.3f));
                var br = Cell(1, 1);
                ShadowLabel(new Rect(mir ? br.x - 34f : br.xMax - 6f, br.y - 4f, 40f, 24f), "-4", new GUIStyle(Big) { fontSize = 22, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.4f, 0.35f));
                ShadowLabel(new Rect(mir ? Cell(3, 0).x : Cell(2, 0).x, Cell(2, 0).y + 6f, c * 2f, c - 12f), "同士討ち！", new GUIStyle(Big) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.85f, 0.35f));
            }
            // 下の一言：コマごとの「何が起きたか」
            string[] foot = cut == "きる" ? new[] { "あなたの こうげきは だれにも 当たらない", "敵Aは あなたの命令を そのまま つかう", "敵Bが 敵Aの「きる」で きずつく" } : new[] { "あなたの攻撃は だれにも当たらない", "敵Aは あなたの命令を そのまま使う", "敵Bが 敵Aの斬撃で傷つく" }; // 【文化祭】はじめては ひらがな
            GUI.Label(new Rect(pr.x + 6f, pr.yMax - 34f, pr.width - 12f, 24f), foot[idx], Fit(new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.92f, 0.75f) } }, foot[idx], pr.width - 12f, 10));
        }

        void TagAbove(Rect cell, string text, Color col)
        {
            ShadowLabel(new Rect(cell.x - 20f, cell.y - 21f, cell.width + 40f, 20f), text, new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, col);
        }

        void TagUnder(Rect cell, string text, Color col)
        {
            ShadowLabel(new Rect(cell.x - 20f, cell.yMax + 2f, cell.width + 40f, 20f), text, new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, col);
        }

        /// <summary>【RC3】はじめて緑の札（戦術札）を持った戦闘で、一度だけ出す説明（本人「緑の札をいつ・どう使うのか分からなかった」）。</summary>
        bool TacticIntroOpen => act != null && act.InBattle && !act.InTutorial && act.Profile != null && !act.Profile.tacticIntroSeen && IsV4Ui &&
                                turn.Tactics != null && turn.Tactics.Hand.Count > 0 && turn.State == BattleState.PlayerInput;

        void DrawTacticIntro(float bw)
        {
            var body = new GUIStyle(Small) { fontSize = 16, wordWrap = false, normal = { textColor = new Color(0.92f, 1f, 0.95f) } };
            string[] lines =
            {
                "・命令（1ターンに3つ）とは別に使える。命令の枠を使わない。敵はまねしない",
                "・札の帯が系統：攻撃の助け（次の直接攻撃 +2）・守り・回復・敵を操る（大技の時間をずらす）・手札・命令",
                "・命令を組む前でも後でも、Z キーかクリックで使う",
                "・手札は1枚（指揮官は2枚）。使ったら、次のターンにまた1枚引く",
            };
            for (int i = 0; i < lines.Length; i++) lines[i] = WrapJa(lines[i], body, 580 - 48); // 【RC5・最終検証】禁則（行末の「（」）
            float textH = 0f; foreach (var l in lines) textH += body.CalcHeight(new GUIContent(l), 580 - 48) + 4f;
            var r = new Rect(bw * 0.5f - 290, RefHeight * 0.5f - 150, 580, Mathf.Max(300f, 54f + textH + 62f)); // 【RC4】文が増えた分だけ広く【RC5・最終検証】文の量に合わせて高く
            DrawRect(new Rect(0, 0, bw, RefHeight), new Color(0f, 0f, 0f, 0.35f));
            DrawRect(r, new Color(0.02f, 0.06f, 0.04f, 0.97f));
            DrawFrame(r, new Color(0.45f, 1f, 0.6f), 3f);
            // 【RC4】チュートリアルを飛ばした人にも、遊んだ人にも、本編ではじめて緑の札を持った戦闘で1回（遊んだ人には「おさらい」）
            string title = act.Profile.tutorialDone ? "おさらい：緑の札は命令とは別" : "緑の札（戦術札）は命令とは別";
            ShadowLabel(new Rect(r.x, r.y + 12, r.width, 32), title, new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter }, new Color(0.55f, 1f, 0.7f));
            float y = r.y + 54;
            foreach (var l in lines) { float hh = body.CalcHeight(new GUIContent(l), r.width - 48); GUI.Label(new Rect(r.x + 24, y, r.width - 48, hh), l, body); y += hh + 4; }
            if (ThemedButton(new Rect(r.center.x - 110, r.yMax - 52, 220, 40), "わかった", true, false, 17) ||
                (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.Space)))
            {
                act.Profile.tacticIntroSeen = true;
                act.SaveProfile();
                if (Event.current.type == EventType.KeyDown) Event.current.Use();
            }
        }

        /// <summary>「できた！」の窓（次へ）。</summary>
        float tutorialDoneAt = -1f;

        void DrawTutorialDoneModal(float bw, float totalW)
        {
            if (!act.TutorialStepDone) { tutorialDoneAt = -1f; return; }
            if (tutorialDoneAt < 0f) tutorialDoneAt = Time.unscaledTime;
            // 【RC12・初見テスト「最後の一撃の途中で『できた！』が出た」】最後の演出を 0.8 秒見せてから出す
            if (Time.unscaledTime - tutorialDoneAt < 0.8f) return;
            // 【RC3・レビュー】結果の盤面を隠さないよう、右側（手札の上）に出す。出てすぐの Enter・クリックは受けない（読まずに飛ばさない）
            bool ready = Time.unscaledTime - tutorialDoneAt > 1.4f;
            var steps = act.TutorialSteps; // 【RC9】
            int step = Mathf.Clamp(act.TutorialStep, 0, steps.Length - 1);
            var def = steps[step];
            float rw = Mathf.Min(560f, totalW - bw - 40f);
            var bodyMeasure = new GUIStyle(Small) { fontSize = 15, wordWrap = false };
            var successLines = new System.Collections.Generic.List<string>(def.Success); if (!string.IsNullOrEmpty(def.Why)) successLines.Add(def.Why); // 【RC12・初見テスト】なぜ解けるか
            float textH = 0f; foreach (var sl in successLines) textH += Mathf.Max(20f, bodyMeasure.CalcHeight(new GUIContent(WrapJa(sl, bodyMeasure, rw - 40)), rw - 40)) + 4f;
            var r = new Rect(bw + (totalW - bw - rw) * 0.5f, RefHeight * 0.5f - 160, rw, Mathf.Max(220f, 56f + textH + 66f + (def.BestTurns > 0 ? 24f : 0f))); // 【RC5・最終ゲート】文が多いとボタンに重なりそうだった → 文の量に合わせて高く
            DrawRect(r, new Color(0.02f, 0.04f, 0.08f, 0.96f));
            DrawFrame(r, new Color(0.45f, 0.9f, 1f), 3f);
            ShadowLabel(new Rect(r.x, r.y + 12, r.width, 36), "できた！", new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, new Color(0.45f, 0.9f, 1f));
            var body = new GUIStyle(Small) { fontSize = 15, wordWrap = false, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            float y = r.y + 56;
            foreach (var raw in successLines)
            {
                string line = WrapJa(raw, body, r.width - 40); // 【RC5・最終検証】禁則（「、」で始まる行）
                float hgt = Mathf.Max(20f, body.CalcHeight(new GUIContent(line), r.width - 40));
                GUI.Label(new Rect(r.x + 20, y, r.width - 40, hgt), line, body);
                y += hgt + 4;
            }
            if (def.BestTurns > 0)
            {
                // 【RC12】いくつか解き方がある。まねを上手く使うと「1ターン・無傷」できれいに解ける（できなくても先へ進める）
                bool clean = act.TutorialSolveTurns <= def.BestTurns && !act.TutorialSolveHurt;
                // 【RC12・初見テスト「答えを見たのに『きれいに解けた』」】答えを見たときは、答えが どんな解き方かだけを言う（責めない）
                string rate = act.AnswerShown ? $"答えの手順は、{def.BestTurns}ターン・無傷の解き方。ほかの解き方もある"
                    : clean ? $"★ きれいに解けた！（{def.BestTurns}ターン・無傷）" : $"クリア！（{def.BestTurns}ターン・無傷で解く、もっときれいな方法もある）";
                if (act.AnswerShown) clean = false;
                ShadowLabel(new Rect(r.x + 12, y, r.width - 24, 22), rate, Fit(new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, rate, r.width - 24, 10), clean ? Gold : new Color(0.75f, 0.85f, 0.95f));
            }
            bool last = step + 1 >= steps.Length;
            string next = last ? (act.QuickMode ? "5分体験のおわりへ" : "チュートリアル完了へ") : $"次へ（{step + 2}/{steps.Length}　{steps[step + 1].Name}）";
            if (ThemedButton(new Rect(r.center.x - 150, r.yMax - 56, 300, 44), next, true, false, 17, ready) && ready) { Sfx.Play(Sfx.Kind.Button); act.NextTutorialStep(); }
            var ev = Event.current;
            if (ready && ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space)) { ev.Use(); act.NextTutorialStep(); }
        }
    }
}
