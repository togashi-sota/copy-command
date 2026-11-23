using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭のクエストの戦闘の画面（BattleUI の partial）：
    /// 右上＝LEVEL・時計・敵を利用した数・「最初からやり直す」（いつでも見える）／お手本中の帯（記録しない）／
    /// ターンの終わりの大きな一言（同士討ち ×2！・連鎖！ など：横から見ている人にも分かる）／CLEAR!／倒れた窓／開始の窓（右の欄：盤面は見えたまま）。
    /// 答え（どの札をどの向き）は出さない。
    /// </summary>
    public partial class BattleUI
    {
        bool FestivalUi => act != null && act.FestivalMode && act.InBattle;
        float festivalRestartArmedAt = -99f;

        /// <summary>右上：LEVEL・時計・敵を利用した数・最初からやり直す。お手本中は板の上に橙の帯。</summary>
        void DrawFestivalHud(float bw, float W)
        {
            if (!FestivalUi || act.Festival == null) return;
            var def = act.FestivalDef;
            bool demo = act.FestivalDemo;
            // 右上の「メニュー 凡例 詳細」の左（盤面の向きの案内・順番の帯を隠さない）
            float rw = Mathf.Max(300f, W - bw - 250f);
            var r = new Rect(bw + 8, 8, rw, 34);
            DrawRect(r, demo ? new Color(0.35f, 0.16f, 0.02f, 0.92f) : new Color(0.02f, 0.03f, 0.07f, 0.85f));
            DrawFrame(r, demo ? new Color(1f, 0.6f, 0.2f) : new Color(Gold.r, Gold.g, Gold.b, 0.6f), 1.5f);
            bool modal = act.TutorialStepDone || act.FestivalGameOverOpen || act.MissionOpen || act.FestivalIdlePrompt || act.BattleMenuOpen;
            float bwid = modal ? 0f : demo ? 250f : 158f;
            string text = demo ? "お手本中" : $"{FestivalRank.Time(act.FestivalElapsed)}　敵を利用 ×{act.Festival.EnemyHits}"; // LEVEL の名前は左下の欄にある（右上は時計と回数だけ：文字が切れていた）
            var tr = new Rect(r.x + 8, r.y, r.width - bwid - 14, r.height);
            ShadowLabel(tr, text, Fit(new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false }, text, tr.width, 11), demo ? new Color(1f, 0.85f, 0.55f) : new Color(1f, 0.9f, 0.6f));
            // 最初からやり直す：来場者の挑戦では2回押す（間違えて押しても すぐには消えない）。お手本のあとは1回で来場者の番へ
            bool armed = !demo && Time.unscaledTime - festivalRestartArmedAt < 3f;
            string label = demo ? "最初からやり直す（来場者の番へ）" : armed ? "もう一度押すと最初から" : "最初から（2回押す）";
            var br = new Rect(r.xMax - bwid - 4, r.y + 3, bwid, r.height - 6);
            if (!modal && ThemedButton(br, label, demo, true, 12))
            {
                if (demo || armed) { festivalRestartArmedAt = -99f; Sfx.Play(Sfx.Kind.Button); act.FestivalRestart(); return; }
                festivalRestartArmedAt = Time.unscaledTime;
            }
            if (demo)
            {
                // 盤面の下にも（スタッフが横で説明している、と来場者に分かる）。上の HP の行は隠さない。画面のふちも橙
                var band = new Rect(0, RefHeight - InfoH - 30f, bw, 28);
                DrawRect(band, new Color(0.45f, 0.2f, 0.02f, 0.88f));
                DrawFrame(new Rect(2, 2, W - 4, RefHeight - 4), new Color(1f, 0.6f, 0.2f, 0.85f), 4f);
                ShadowLabel(band, "お手本中：スタッフが操作しています（終わったら「最初からやり直す」で交代）", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.9f, 0.65f));
            }
        }

        float festivalBannerShown = -99f;

        /// <summary>ターンの終わりの大きな一言（2.2 秒）。ボスへの大ダメージ・連鎖は もっと大きく・画面を揺らす。</summary>
        void DrawFestivalBanner(float bw)
        {
            if (!FestivalUi || string.IsNullOrEmpty(act.FestivalBanner)) return;
            float t = Time.unscaledTime - act.FestivalBannerAt;
            if (t < 0f || t > 2.2f) return;
            if (festivalBannerShown != act.FestivalBannerAt)
            {
                festivalBannerShown = act.FestivalBannerAt;
                if (act.FestivalBannerBig) { ScreenFx.Shake(0.25f, 0.5f); ScreenFx.Flash(new Color(1f, 0.85f, 0.4f, 0.35f), 0.4f); Sfx.Play(Sfx.Kind.Boom); }
                else Sfx.Play(Sfx.Kind.Chime);
            }
            float pop = t < 0.18f ? Mathf.Lerp(0.6f, 1.12f, t / 0.18f) : t < 0.3f ? Mathf.Lerp(1.12f, 1f, (t - 0.18f) / 0.12f) : 1f;
            float a = t > 1.7f ? 1f - (t - 1.7f) / 0.5f : 1f;
            int size = Mathf.RoundToInt((act.FestivalBannerBig ? 54 : 44) * pop);
            // 【文化祭・横から見る人のレビュー「帯がボスを隠す」】右の欄（手札）の上に出す：盤面の当たった所は見えたまま
            float W = Screen.width / UiStyles.GuiScale;
            var band = new Rect(bw, RefHeight * 0.30f, Mathf.Max(200f, W - bw), 110);
            DrawRect(band, new Color(0.02f, 0.02f, 0.05f, 0.88f * a));
            DrawRect(new Rect(band.x, band.y, band.width, 4), new Color(1f, 0.82f, 0.35f, a));
            DrawRect(new Rect(band.x, band.yMax - 4, band.width, 4), new Color(1f, 0.82f, 0.35f, a));
            var col = act.FestivalBannerBig ? new Color(1f, 0.55f, 0.35f, a) : new Color(1f, 0.88f, 0.4f, a);
            ShadowLabel(band, act.FestivalBanner, Fit(new GUIStyle(Big) { fontSize = size, alignment = TextAnchor.MiddleCenter, wordWrap = false }, act.FestivalBanner, band.width - 20, 24), col);
        }

        float festivalClearAt = -1f;

        /// <summary>クエストの「CLEAR!」：一言・この挑戦の数字・結果を見る。紙吹雪。</summary>
        void DrawFestivalClear(float bw, float totalW)
        {
            if (!act.TutorialStepDone) { festivalClearAt = -1f; return; }
            if (festivalClearAt < 0f) festivalClearAt = Time.unscaledTime;
            float t = Time.unscaledTime - festivalClearAt;
            if (t < 0.8f) return; // 最後の一撃を見せる
            bool ready = t > 1.4f;
            var lv = act.FestivalDef;
            var def = lv.Battle;
            DrawConfetti(new Rect(0, 0, bw, RefHeight), t - 0.8f);
            float rw = Mathf.Min(560f, totalW - bw - 40f);
            var r = new Rect(bw + (totalW - bw - rw) * 0.5f, RefHeight * 0.5f - 170, rw, 330);
            DrawRect(r, new Color(0.02f, 0.04f, 0.08f, 0.97f));
            DrawFrame(r, Gold, 3f);
            DrawRect(new Rect(r.x, r.y, r.width, 6), Gold);
            float pop = Mathf.Clamp01((t - 0.8f) / 0.25f);
            ShadowLabel(new Rect(r.x, r.y + 14, r.width, 64), "CLEAR!", new GUIStyle(Big) { fontSize = Mathf.RoundToInt(Mathf.Lerp(30, 60, pop)), alignment = TextAnchor.MiddleCenter }, Gold);
            ShadowLabel(new Rect(r.x, r.y + 78, r.width, 26), $"{lv.Label}　{lv.Quest}　攻略！", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(0.62f, 0.92f, 1f));
            var body = new GUIStyle(Small) { fontSize = 16, wordWrap = false, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            float y = r.y + 112;
            foreach (var raw in def.Success ?? new string[0])
            {
                string line = WrapJa(raw, body, r.width - 40);
                float hgt = Mathf.Max(22f, body.CalcHeight(new GUIContent(line), r.width - 40));
                GUI.Label(new Rect(r.x + 20, y, r.width - 40, hgt), line, body);
                y += hgt + 4;
            }
            var f = act.Festival;
            int turns = f != null ? f.Turns : turn.TurnNumber;
            string stat = f == null ? "" : $"敵を利用した攻撃 ×{f.EnemyHits}　　被ダメ {f.Damage}　　{turns}ターン　　{FestivalRank.Time(f.Seconds)}";
            ShadowLabel(new Rect(r.x + 12, y + 6, r.width - 24, 28), stat, Fit(new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, stat, r.width - 24, 12), new Color(1f, 0.9f, 0.55f));
            if (def.Boss) GUI.Label(new Rect(r.x, r.yMax - 92, r.width, 22), "ボスを倒すと、手下も にげだす。", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } });
            if (ThemedButton(new Rect(r.center.x - 160, r.yMax - 62, 320, 48), "結果を見る（Enter）", true, true, 18, ready) && ready) { Sfx.Play(Sfx.Kind.Button); act.NextTutorialStep(); return; }
            var ev = Event.current;
            if (ready && ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space)) { ev.Use(); act.NextTutorialStep(); }
        }

        /// <summary>倒れた（HP 0）：もう一度挑戦（同じ LEVEL を最初から）／別の LEVEL へ／次の人へ。ランは失わない。</summary>
        void DrawFestivalGameOver(float totalW)
        {
            if (!FestivalUi || !act.FestivalGameOverOpen) return;
            DrawRect(new Rect(0, 0, totalW, RefHeight), new Color(0f, 0f, 0f, 0.62f));
            var r = new Rect(totalW * 0.5f - 330, RefHeight * 0.5f - 165, 660, 330);
            DrawRect(r, new Color(0.04f, 0.03f, 0.06f, 0.98f));
            DrawFrame(r, new Color(1f, 0.45f, 0.4f), 3f);
            ShadowLabel(new Rect(r.x, r.y + 18, r.width, 50), "倒れた…", new GUIStyle(Big) { fontSize = 42, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.6f, 0.55f));
            var f = act.Festival;
            string stat = f == null ? "" : $"{act.FestivalDef.Quest}　{Mathf.Max(1, f.Turns)}ターン・敵を利用した攻撃 ×{f.EnemyHits}";
            ShadowLabel(new Rect(r.x, r.y + 72, r.width, 26), stat, Fit(new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, stat, r.width - 40, 12), new Color(1f, 0.9f, 0.6f));
            string tip = "敵の攻撃（赤・紫）を、敵に当てられないか？ ヒントのボタンも使えます。";
            GUI.Label(new Rect(r.x + 30, r.y + 106, r.width - 60, 24), tip, Fit(new GUIStyle(Small) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.85f, 0.92f, 1f) } }, tip, r.width - 60, 11));
            bool demo = act.FestivalDemo;
            float bw2 = 196f, gap = 14f, bx = r.center.x - (bw2 * 3f + gap * 2f) * 0.5f, by = r.yMax - 96f;
            if (ThemedButton(new Rect(bx, by, bw2, 56), demo ? "来場者の番へ（最初から）" : "もう一度挑戦（Enter）", true, true, 17))
            { Sfx.Play(Sfx.Kind.Button); if (demo) act.FestivalRestart(); else act.FestivalRetryAfterGameOver(); return; }
            if (ThemedButton(new Rect(bx + bw2 + gap, by, bw2, 56), "別の LEVEL へ", false, true, 17)) { Sfx.Play(Sfx.Kind.Button); act.FestivalOtherLevel(); return; }
            if (ThemedButton(new Rect(bx + (bw2 + gap) * 2f, by, bw2, 56), "次の人へ", false, true, 17)) { Sfx.Play(Sfx.Kind.Button); act.FestivalNextPerson(); return; }
            GUI.Label(new Rect(r.x, by + 60, r.width, 20), "もう一度挑戦＝同じ LEVEL を はじめの盤面から（HP も全回復）", new GUIStyle(Tiny) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.75f, 0.8f, 0.9f) } });
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)) { ev.Use(); if (demo) act.FestivalRestart(); else act.FestivalRetryAfterGameOver(); }
        }

        /// <summary>紙吹雪（簡単な四角。時刻から決まる・乱数を使わない）。画面の揺れ・点滅を止める設定では出さない。</summary>
        void DrawConfetti(Rect area, float t)
        {
            if (t < 0f || ScreenFx.Disabled) return;
            Color[] cols = { new Color(1f, 0.85f, 0.35f), new Color(0.45f, 0.9f, 1f), new Color(1f, 0.5f, 0.6f), new Color(0.6f, 1f, 0.6f), new Color(0.85f, 0.6f, 1f) };
            for (int i = 0; i < 46; i++)
            {
                float seed = i * 12.9898f;
                float fx = Mathf.Repeat(Mathf.Sin(seed) * 43758.5453f, 1f);
                float speed = 90f + 70f * Mathf.Repeat(Mathf.Sin(seed * 1.7f) * 9631.13f, 1f);
                float y = area.y - 20f + (t * speed + fx * 120f) % (area.height + 40f);
                float x = area.x + fx * area.width + Mathf.Sin(t * 3f + i) * 14f;
                float s = 6f + 4f * Mathf.Repeat(fx * 7.3f, 1f);
                float fade = Mathf.Clamp01(3.2f - t * 0.5f);
                if (fade <= 0f) return;
                var c = cols[i % cols.Length]; c.a = 0.9f * fade;
                DrawRect(new Rect(x, y, s, s * 0.6f), c);
            }
        }

        /// <summary>展示の PC：しばらく操作が無いとき（戦闘中）。どこかを押せば続ける。</summary>
        void DrawFestivalIdlePrompt(float totalW)
        {
            if (act == null || !act.FestivalIdlePrompt) return;
            DrawRect(new Rect(0, 0, totalW, RefHeight), new Color(0f, 0f, 0f, 0.72f));
            var r = new Rect(totalW * 0.5f - 300, RefHeight * 0.5f - 120, 600, 240);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.98f));
            DrawFrame(r, Gold, 3f);
            ShadowLabel(new Rect(r.x, r.y + 24, r.width, 44), "まだ遊んでいますか？", new GUIStyle(Big) { fontSize = 32, alignment = TextAnchor.MiddleCenter }, Gold);
            GUI.Label(new Rect(r.x, r.y + 78, r.width, 26), $"{Mathf.CeilToInt(act.FestivalIdleLeft)}秒後に、はじめの画面へ戻ります", new GUIStyle(Small) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
            var ev = Event.current;
            bool any = ev.type == EventType.KeyDown || ev.type == EventType.MouseDown;
            if (ThemedButton(new Rect(r.center.x - 130, r.yMax - 76, 260, 50), "続ける", true, true, 20) || any) { if (any) ev.Use(); act.FestivalKeepPlaying(); }
        }

        /// <summary>【文化祭・横から見る人のレビュー「右半分の文字の壁で、動きが見えない」】敵が動いている間は右の欄を暗く（盤面に目が行く）。</summary>
        void DrawFestivalExecDim(float bw, float W)
        {
            if (!FestivalUi) return;
            if (turn.State != BattleState.PlayerExecuting && turn.State != BattleState.EnemyExecuting) return;
            DrawRect(new Rect(bw + 2, 44, W - bw - 2, RefHeight - 44), new Color(0.01f, 0.01f, 0.03f, 0.6f));
        }

        /// <summary>右下の欄（何も指していないとき）：目的・敵を利用した数・操作。</summary>
        void DrawFestivalGuide(Rect r)
        {
            var lv = act.FestivalDef;
            float y = r.y + 10;
            if (act.FestivalDemo && !string.IsNullOrEmpty(lv.DemoScript))
            {
                // 【文化祭クエスト v2・スタッフの立場のレビュー】お手本中：スタッフ用の話す例（作者がいなくても案内できる）
                ShadowLabel(new Rect(r.x + 14, y, r.width - 28, 26), "お手本の例（スタッフ用）", new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false }, new Color(1f, 0.7f, 0.35f));
                y += 30;
                var ds = new GUIStyle(Small) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(1f, 0.92f, 0.8f) } };
                string script = WrapJa(lv.DemoScript, ds, r.width - 32);
                GUI.Label(new Rect(r.x + 16, y, r.width - 32, r.yMax - y - 30), script, ds);
                GUI.Label(new Rect(r.x + 16, r.yMax - 28, r.width - 32, 22), "終わったら 右上の「最初からやり直す（来場者の番へ）」", new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = new Color(1f, 0.75f, 0.4f) } });
                return;
            }
            string goal = "目的：" + lv.Goal;
            ShadowLabel(new Rect(r.x + 14, y, r.width - 28, 26), goal, Fit(new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, wordWrap = false }, goal, r.width - 28, 13), Gold);
            y += 30;
            var rs = new GUIStyle(Small) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(0.62f, 0.95f, 1f) } };
            string rule = WrapJa(lv.Rule, rs, r.width - 32);
            float rh = rs.CalcHeight(new GUIContent(rule), r.width - 32);
            GUI.Label(new Rect(r.x + 16, y, r.width - 32, rh), rule, rs);
            y += rh + 8;
            string[] ops = { "① 手札の札を選ぶ　② 盤面の矢印で向き", "③ 命令は3つまで →「実行」（Enter）" };
            var os = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false };
            foreach (var o in ops)
            {
                if (y + 24 > r.yMax) break;
                ShadowLabel(new Rect(r.x + 16, y, r.width - 32, 24), o, Fit(os, o, r.width - 32, 11), Color.white);
                y += 25;
            }
            var ns = new GUIStyle(Small) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } };
            string note = $"ランク S＝敵を利用した攻撃 {lv.EnemyUseGoal}回以上・{(lv.DamageLimit <= 0 ? "ノーダメージ" : $"被ダメ {lv.DamageLimit}以下")}・{lv.ParTurns}ターン以内";
            if (y + 20 <= r.yMax) GUI.Label(new Rect(r.x + 16, y + 2, r.width - 32, r.yMax - y - 4), WrapJa(note, ns, r.width - 32), ns);
        }

        /// <summary>
        /// 【文化祭クエスト v2】開始の窓（右の欄・盤面は見えたまま）：LEVEL・クエスト名・目的・特殊ルール1つ・敵の数・HP・S の条件。
        /// ［自分で挑戦］［お手本（スタッフ）］［？ 遊び方（絵）］。長い説明は出さない（スタッフが横で実際の画面で見せる）。
        /// </summary>
        void DrawFestivalMission(float bw, float totalW)
        {
            var lv = act.FestivalDef;
            var def = lv.Battle;
            DrawRect(new Rect(0, 0, bw, RefHeight), new Color(0f, 0f, 0f, 0.15f));
            DrawRect(new Rect(bw, 0, totalW - bw, RefHeight), new Color(0f, 0f, 0f, 0.72f));
            var r = new Rect(bw + 14, 52, totalW - bw - 28, RefHeight - 76);
            Color accent = act.FestivalLevel == 0 ? new Color(0.45f, 0.95f, 0.6f) : act.FestivalLevel == 1 ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.55f, 0.45f);
            DrawRect(r, new Color(0.03f, 0.04f, 0.09f, 0.98f));
            DrawFrame(r, accent, 3f);
            DrawRect(new Rect(r.x, r.y, r.width, 6), accent);
            float y = r.y + 14;
            ShadowLabel(new Rect(r.x, y, r.width, 30), $"{lv.Label}　{lv.Grade}", new GUIStyle(Label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, accent);
            y += 32;
            ShadowLabel(new Rect(r.x, y, r.width, 50), lv.Quest, Fit(new GUIStyle(Big) { fontSize = 40, alignment = TextAnchor.MiddleCenter }, lv.Quest, r.width - 30, 22), Gold);
            y += 58;
            var ms = new GUIStyle(Label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(new Rect(r.x + 10, y, r.width - 20, 34), "目的：" + lv.Goal, Fit(ms, "目的：" + lv.Goal, r.width - 20, 14), Color.white);
            y += 42;
            var rs = new GUIStyle(Small) { fontSize = 20, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.62f, 0.95f, 1f) } };
            string rule = WrapJa(lv.Rule, rs, r.width - 44);
            float rh = rs.CalcHeight(new GUIContent(rule), r.width - 44);
            GUI.Label(new Rect(r.x + 22, y, r.width - 44, rh), rule, rs);
            y += rh + 12;
            var es = new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            string enemies = $"敵：{lv.Enemies}　／　HP {lv.Hp}";
            ShadowLabel(new Rect(r.x + 10, y, r.width - 20, 26), enemies, Fit(es, enemies, r.width - 20, 12), new Color(1f, 0.85f, 0.55f));
            y += 30;
            var ts = new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.85f, 0.88f, 0.95f) } };
            string s = $"ランク S＝敵を利用した攻撃 {lv.EnemyUseGoal}回以上・{(lv.DamageLimit <= 0 ? "ノーダメージ" : $"被ダメ {lv.DamageLimit}以下")}・{lv.ParTurns}ターン以内";
            GUI.Label(new Rect(r.x + 10, y, r.width - 20, 22), s, Fit(ts, s, r.width - 20, 10));
            y += 24;
            string tail = $"目安 {lv.Minutes}・ターンの制限なし・倒れても その場で もう一度";
            GUI.Label(new Rect(r.x + 10, y, r.width - 20, 22), tail, Fit(ts, tail, r.width - 20, 10));
            y += 34;
            // 敵の一覧（顔・名前・まね方）：開始の窓の空いた所。盤面のどれか、横の人にも指で示せる
            if (lv.EnemyCards != null)
            {
                float rowH = Mathf.Min(58f, (r.yMax - 140f - y) / Mathf.Max(1, lv.EnemyCards.Length));
                var ns = new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = false };
                var ss = new GUIStyle(Small) { fontSize = 15, wordWrap = false, normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
                foreach (var ec in lv.EnemyCards)
                {
                    if (rowH < 30f) break;
                    var row = new Rect(r.x + 40, y, r.width - 80, rowH - 6);
                    DrawRect(row, new Color(0.06f, 0.08f, 0.15f, 0.9f));
                    float isz = row.height - 6;
                    DrawUiImage(new Rect(row.x + 4, row.y + 3, isz, isz), ec[0], 1f, ScaleMode.ScaleToFit);
                    ShadowLabel(new Rect(row.x + isz + 14, row.y + 2, 260, row.height * 0.5f), ec[1], ns, Color.white);
                    GUI.Label(new Rect(row.x + isz + 14, row.y + row.height * 0.5f, row.width - isz - 20, row.height * 0.5f - 2), ec[2], Fit(ss, ec[2], row.width - isz - 20, 11));
                    y += rowH;
                }
            }

            // ボタン：自分で挑戦（大）・お手本（スタッフ）・？遊び方
            var ev = Event.current;
            bool key = ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space);
            var main = new Rect(r.center.x - 150, r.yMax - 128, 300, 54);
            if (ThemedButton(main, "自分で挑戦（Enter）", true, true, 21) || key) { if (key) ev.Use(); Sfx.Play(Sfx.Kind.Button); act.FestivalBeginPlay(); return; }
            float sw = Mathf.Min(220f, (r.width - 60f) * 0.5f);
            if (ThemedButton(new Rect(r.center.x - sw - 8, r.yMax - 62, sw, 40), "お手本（スタッフ用）", false, true, 14)) { Sfx.Play(Sfx.Kind.Button); act.FestivalBeginDemo(); return; }
            if (ThemedButton(new Rect(r.center.x + 8, r.yMax - 62, sw, 40), "？ 遊び方（絵）", false, true, 14)) { Sfx.Play(Sfx.Kind.Button); act.FestivalShowIntro(); }
        }
    }
}
