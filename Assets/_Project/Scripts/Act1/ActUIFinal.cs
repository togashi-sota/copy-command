using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【完成版・2026-10-20】完成版 ACT1 の画面（タイトル・役職と挑戦・休息（鍛える）・結果（解放・次の目標・ボスの再挑戦））。
    /// 公開版（BuildInfo.ExternalPlaytest）ではこれだけが出る。開発版のタイトルからも「完成版 ACT1」で入れる。
    /// 文字は 14 以上（ブラウザ・小さい画面でも読めるように）。
    /// </summary>
    public partial class ActUI
    {
        static readonly Color CyanText = new Color(0.45f, 0.9f, 1f);
        static readonly Color ClearGreen = new Color(0.45f, 1f, 0.6f);
        static readonly string[] FinalRoleNames = { "旅の剣士", "観測士", "指揮官" };

        static bool IsWeb => Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>【RC4】開発版だけ：研究室・検証ステージのある開発用のタイトルを出す（タイトルの【開発】ボタン）。既定は公開版と同じタイトル。</summary>
        public static bool ShowDevTitle;
        float devResetArmedAt = -10f;
        /// <summary>
        /// 【RC10・ChatGPT「採用担当が30分のランを遊ぶ前提にしない」】タイトルのボタンの並び（比べるため。撮影 -capturetitlelayout）。
        /// 0＝RC9（はじめる → 続きから → チュートリアル｜5分体験）／1＝案A（はじめる → ★5分体験 → チュートリアル｜遊び方 → 続きから）／
        /// 2＝案B（★5分でこのゲームを体験 → はじめる → チュートリアル｜遊び方 → 続きから）。
        /// </summary>
        public static int TitleLayout = 2;

        // ───── タイトル ─────

        void DrawFinalTitle(float w, float h)
        {
            ShadowLabel(new Rect(0, h * 0.10f - 6, w, 60), BuildInfo.Title, new GUIStyle(Big) { fontSize = 46, alignment = TextAnchor.MiddleCenter }, Gold); // 【通常リリース版】「（仮題）」は画面に出さない（BuildInfo.Title）
            ShadowLabel(new Rect(0, h * 0.10f + 52, w, 30), $"{BuildInfo.TitleKana}　―　{BuildInfo.Catch}", new GUIStyle(Big) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(0.95f, 0.97f, 1f)); // 【2026-11-21】読みとキャッチコピー
            GUI.Label(new Rect(w * 0.1f, h * 0.10f + 92, w * 0.8f, 52), "あなたが出した命令を、敵がそれぞれの位置からまねる。\n空振りの斬撃で敵どうしを斬らせ、同じ突進で敵だけを危険へ送れ。",
                new GUIStyle(Small) { fontSize = 17, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(0.88f, 0.92f, 1f) } });
            float bw = 320, x = (w - bw) / 2, y = h * 0.38f;
            // 【RC3・レビュー】窓（遊び方・設定・はじめての案内）が開いている間は、後ろのボタンを描かない（IMGUI は先に描いたボタンが押下を取る：
            //   設定の BGM の棒を動かすと後ろの「はじめる」が反応して役職選びへ飛んでいた）
            bool modalOpen = showHowTo || showSettings || ShowFirstChoice;
            if (TitleLayout != 0) DrawTitleButtonsRC10(w, h, x, y, bw, modalOpen); // 【RC10】案A・案B
            else
            {
            if (!modalOpen && Button(new Rect(x, y, bw, 56), "はじめる", true, true, 22)) act.OpenV4RunRoleSelect();
            string save = act.SaveDescription;
            float yy = y + 68;
            if (save != null && !modalOpen)
            {
                if (Button(new Rect(x, yy, bw, 44), "続きから", false, true, 17)) act.ContinueRun();
                GUI.Label(new Rect(x - 160, yy + 46, bw + 320, 22), save, new GUIStyle(Tiny) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = false });
                yy += 76;
            }
            // 【RC3】チュートリアル（いつでも遊び直せる）と遊び方
            if (!modalOpen && Button(new Rect(x, yy, bw / 2 - 6, 44), "チュートリアル", false, true, 17)) act.StartTutorial();
            if (!modalOpen && Button(new Rect(x + bw / 2 + 6, yy, bw / 2 - 6, 44), "5分体験", false, true, 17)) act.StartQuick(); // 【RC9】核だけを5つ（採用担当など）
            if (!modalOpen) // 【RC9・チュートリアルの初見テスト「チュートリアルと5分体験の違いが分からない」】ボタンの下に一言
            {
                var cap = new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } };
                GUI.Label(new Rect(x, yy + 45, bw / 2 - 6, 16), "本編の操作を全部（9つ）", cap);
                GUI.Label(new Rect(x + bw / 2 + 6, yy + 45, bw / 2 - 6, 16), "面白さを3問で", cap);
            }
            yy += 64;
            if (!modalOpen && Button(new Rect(x, yy, bw / 2 - 6, 38), "遊び方", false, true, 15)) showHowTo = true;
            if (!modalOpen && Button(new Rect(x + bw / 2 + 6, yy, bw / 2 - 6, 38), "設定（音量）", false, true, 15)) showSettings = true;
            yy += 50;
            if (!modalOpen && !IsWeb && Button(new Rect(x + bw / 4, yy, bw / 2, 32), "ゲームを終了", false, true, 13)) Application.Quit();
            }
            if (!modalOpen) DrawFestivalTitleCard(w, h); // 【文化祭】左に大きく「文化祭 3〜5分チャレンジ」
            if (!modalOpen) DrawTitleIllustration(w, h, x, bw); // 【通常リリース版・画面の直し 18】何のゲームかが一目で分かる1枚（5分体験の入口の3コマと同じ絵）
            if (act.Message != null) GUI.Label(new Rect(0, y - 30, w, 22), act.Message, new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.6f, 0.6f) } });

            // 記録（役職ごとのクリア）と次の目標
            DrawRoleRecords(new Rect(w / 2 - 330, h - 168, 660, 96));
            var p = act.Profile;
            string goal = FinalMeta.NextGoal(p);
            if (goal != null) ShadowLabel(new Rect(0, h - 66, w, 24), goal, new GUIStyle(Small) { fontSize = 16, alignment = TextAnchor.MiddleCenter }, CyanText);
            if (!modalOpen && Button(new Rect(w - 220, h - 46, 200, 32), p.hintsOff ? "ヒント OFF" : "ヒント ON", false, true, 14)) act.SetHintsOff(!p.hintsOff); // 【通常リリース版・画面の直し 15】今の状態が分かる形（前は「ON（押すと OFF）」）
            GUI.Label(new Rect(20, h - 40, w - 260, 30), $"ver {BuildInfo.Version}　1ラン 約30 分",
                new GUIStyle(Tiny) { fontSize = 14, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, 0.55f) } });
            if (!BuildInfo.ExternalPlaytest && !modalOpen) DrawDevTitleTools(w, h); // 【RC4】開発版だけ（公開版には出ない）
            if (showHowTo) DrawHowTo(w, h);
            else if (showSettings) DrawSettings(w, h);
            else if (ShowFirstChoice) DrawFirstChoice(w, h); // 【RC3】はじめての起動：チュートリアルを遊ぶ？（前は遊び方の文が自動で出ていた）
        }

        /// <summary>【RC11】タイトルのボタンの列の高さ（案B：5分体験 84・はじめる 72・チュートリアル｜遊び方 62・続きから 66・設定 34）。</summary>
        public static float TitleButtonsTop(float h, bool hasSave)
        {
            float y = h * 0.38f, need = TitleButtonsHeight(hasSave), limit = h - 168f - 8f; // 168＝役職の記録の欄の上（DrawRoleRecords）
            return y + need > limit ? Mathf.Max(h * 0.10f + 88f + 50f, limit - need) : y; // 説明の2行の下より上には行かない
        }
        public static float TitleButtonsHeight(bool hasSave) => (TitleLayout == 2 ? 84f + 72f : 82f + 76f) + 68f + (hasSave ? 66f : 0f) + 34f; // 【第2巡 9】チュートリアルの段 62 → 68

        /// <summary>【RC10】タイトルのボタン（案A・案B）。5分体験は大きく★つき・一言の説明つき。続きからは下（セーブがあるときだけ）。</summary>
        void DrawTitleButtonsRC10(float w, float h, float x, float y, float bw, bool modalOpen)
        {
            var cap = new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.82f, 0.87f, 0.96f) } };
            // 【RC11・ブラウザ版の確認】中断のセーブがある（「続きから」が出る）と、「設定（音量）」「ゲームを終了」が下の役職の記録の欄に隠れていた（RC10 から）
            //   → ボタンの列が記録の欄（h − 168）に届くときは、列ごと上へ（説明の2行の下まで）
            y = TitleButtonsTop(h, act.SaveDescription != null);
            float yy = y;
            void Quick(float height, int size, bool gold)
            {
                var qr = new Rect(x, yy, bw, height);
                if (!modalOpen && Button(qr, TitleLayout == 2 ? "★ 5分でこのゲームを体験" : "★ 5分体験", gold, true, size)) act.StartQuick();
                if (!modalOpen) GUI.Label(new Rect(x - 60, yy + height + 2, bw + 120, 18), "このゲームの面白さを3問で（まね・落石へ送る・まねの連鎖）", cap); // 【通常リリース版・画面の直し 26】5分体験＝面白さを3問で／チュートリアル＝本編の操作を全部（タイトル・はじめての窓・5分体験のおわりでそろえる）
                yy += height + 26;
            }
            void Start(float height, int size, bool gold)
            {
                if (!modalOpen && Button(new Rect(x, yy, bw, height), "はじめる", gold, true, size)) act.OpenV4RunRoleSelect();
                if (!modalOpen) GUI.Label(new Rect(x - 60, yy + height + 2, bw + 120, 18), "本編：1ラン 約30分・8階のボスを目指す", cap);
                yy += height + 26;
            }
            bool clearedOnce = act.Profile != null && act.Profile.finalRecords.Any(fr => fr.clears > 0); // 【第2巡 8】クリアしたあとは「はじめる」が主（金・一番上）。高さは同じ（記録の欄の上に収める計算 TitleButtonsHeight と同じ）
            if (TitleLayout == 2 && clearedOnce) { Start(58, 22, true); Quick(46, 18, false); }
            else if (TitleLayout == 2) { Quick(58, 22, true); Start(46, 18, false); }
            else { Start(56, 22, true); Quick(50, 19, false); }
            if (!modalOpen && Button(new Rect(x, yy, bw / 2 - 6, 40), "チュートリアル", false, true, 16)) act.StartTutorial();
            if (!modalOpen && Button(new Rect(x + bw / 2 + 6, yy, bw / 2 - 6, 40), "遊び方", false, true, 15)) showHowTo = true;
            if (!modalOpen) GUI.Label(new Rect(x - 20, yy + 42, bw / 2 + 14, 18), "本編の操作を全部（9つ）", cap); // 【画面の直し 26】
            yy += 68; // 【第2巡 9】一言が下の設定のボタンに隠れていた（62 → 68）
            string save = act.SaveDescription;
            if (save != null && !modalOpen)
            {
                if (Button(new Rect(x, yy, bw, 40), "続きから", false, true, 16)) act.ContinueRun();
                GUI.Label(new Rect(x - 160, yy + 42, bw + 320, 20), save, new GUIStyle(Tiny) { fontSize = 13, alignment = TextAnchor.UpperCenter, wordWrap = false });
                yy += 66;
            }
            if (!modalOpen && Button(new Rect(x, yy, bw / 2 - 6, 34), "設定（音量）", false, true, 14)) showSettings = true;
            if (!modalOpen && !IsWeb && Button(new Rect(x + bw / 2 + 6, yy, bw / 2 - 6, 34), "ゲームを終了", false, true, 13)) Application.Quit();
        }

        /// <summary>
        /// 【通常リリース版・画面の直し 18「タイトルが文字だけで仮の画面に見える」】ボタンの列の右の空いた所に、小さな盤面1つで
        /// 「あなたの斬撃（空振り）→ 敵Aも同じ向きに まねる → 敵Bに当たって同士討ち」。絵は5分体験の入口の3コマと同じ物だけ（新しい絵は作らない）。
        /// ボタン・窓・下の記録の欄と重ならない（入らない狭い画面では出さない）。
        /// </summary>
        void DrawTitleIllustration(float w, float h, float bx, float bw)
        {
            float px = bx + bw + 84f;                       // ボタンの下の一言（左右 60 はみ出す）とも重ねない
            float pw = Mathf.Min(372f, w - px - 28f);
            float top = TitleButtonsTop(h, act.SaveDescription != null);
            float ph = Mathf.Min(262f, h - 176f - top);
            if (pw < 280f || ph < 220f) return;
            var r = new Rect(px, top, pw, ph);
            DrawUi(r, "panel", PanelBlue, 0.9f);
            DrawFrame(r, new Color(Gold.r, Gold.g, Gold.b, 0.45f), 1.5f);
            ShadowLabel(new Rect(r.x, r.y + 8, r.width, 24), "空振りの斬撃で、同士討ち", new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, Gold);
            float c = Mathf.Min((r.width - 56f) / 4f, 60f);
            var g0 = new Vector2(r.center.x - c * 2f, r.y + 62f);
            Rect Cell(int cx, int cy) => new Rect(g0.x + cx * c, g0.y + cy * c, c - 3f, c - 3f);
            for (int yy = 0; yy < 2; yy++)
                for (int xx = 0; xx < 4; xx++)
                {
                    var cr = Cell(xx, yy);
                    DrawRect(cr, (xx + yy) % 2 == 0 ? new Color(0.22f, 0.25f, 0.32f) : new Color(0.19f, 0.22f, 0.29f));
                    DrawFrame(cr, new Color(0f, 0f, 0f, 0.5f), 1f);
                }
            var cyan = new Color(0.4f, 0.9f, 1f);
            void Slash(int cx, int cy, bool hit)
            {
                var cr = Cell(cx, cy);
                DrawRect(cr, hit ? new Color(0.2f, 0.75f, 0.95f, 0.45f) : new Color(0.3f, 0.7f, 1f, 0.18f)); // あなたの命令とまねは水色（入口の3コマと同じ）
                DrawUiImage(new Rect(cr.x + 5f, cr.y + 5f, cr.width - 10f, cr.height - 10f), "cardart_slash", hit ? 0.5f : 0.7f, ScaleMode.ScaleToFit);
                DrawFrame(cr, cyan, 2f);
            }
            void Unit(int cx, int cy, string key, string letter, Color letterCol, Color frame)
            {
                var cr = Cell(cx, cy);
                var ir = new Rect(cr.x + 4f, cr.y + 4f, cr.width - 8f, cr.height - 8f);
                if (!DrawUiImage(ir, key, 1f, ScaleMode.ScaleAndCrop)) DrawRect(ir, new Color(0.5f, 0.55f, 0.65f));
                if (frame.a > 0f) DrawFrame(cr, frame, 2.5f);
                if (letter != null)
                {
                    var lr = new Rect(cr.x + 2f, cr.yMax - 17f, 30f, 15f);
                    DrawRect(lr, new Color(0f, 0f, 0f, 0.72f));
                    ShadowLabel(lr, "敵" + letter, new GUIStyle(Label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, letterCol);
                }
            }
            Slash(1, 0, false);
            Slash(1, 1, true);
            Unit(0, 0, "portrait_player", null, Color.white, cyan);
            Unit(0, 1, "portrait_normal", "A", new Color(1f, 0.83f, 0.27f), new Color(0.35f, 0.95f, 1f, 0.9f));
            Unit(1, 1, "portrait_normal", "B", new Color(0.93f, 0.93f, 0.96f), new Color(1f, 0.35f, 0.3f));
            var tag = new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(new Rect(Cell(0, 0).x - 14f, Cell(0, 0).y - 19f, c + 28f, 18f), "あなた", tag, cyan);
            ShadowLabel(new Rect(Cell(1, 0).x - 14f, Cell(1, 0).y - 19f, c + 28f, 18f), "空振り", tag, new Color(0.85f, 0.88f, 0.95f));
            var b = Cell(1, 1);
            ShadowLabel(new Rect(b.xMax - 8f, b.y - 2f, 36f, 22f), "-4", new GUIStyle(Big) { fontSize = 19, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.42f, 0.36f));
            ShadowLabel(new Rect(Cell(2, 1).x + 16f, Cell(2, 1).y + 4f, c * 2f, c - 8f), "同士討ち！", new GUIStyle(Big) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 0.85f, 0.35f));
            ShadowLabel(new Rect(Cell(0, 1).x - 14f, Cell(0, 1).yMax + 1f, c + 28f, 18f), "まねる", tag, new Color(0.55f, 0.95f, 1f)); // 敵A は あなたの斬撃を同じ向きに
            var foot = new GUIStyle(Small) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.88f, 0.9f, 0.97f) } };
            string f1 = "あなたの命令を、敵もそれぞれの位置からまねる";
            GUI.Label(new Rect(r.x + 8, g0.y + c * 2f + 22f, r.width - 16, 20), f1, Fit(foot, f1, r.width - 16, 10));
            string f2 = "あなたは一度も当てていない";
            GUI.Label(new Rect(r.x + 8, g0.y + c * 2f + 42f, r.width - 16, 20), f2, Fit(new GUIStyle(foot) { normal = { textColor = new Color(1f, 0.85f, 0.6f) } }, f2, r.width - 16, 10));
        }

        /// <summary>役職ごとの記録（✓ クリア・最少ターン・深度・未解放の条件）。</summary>
        void DrawRoleRecords(Rect r)
        {
            DrawUi(r, "panel", PanelBlue, 0.92f);
            var p = act.Profile;
            float cw = (r.width - 24) / 3f;
            for (int i = 0; i < FinalMeta.RoleOrder.Length; i++)
            {
                string id = FinalMeta.RoleOrder[i];
                var c = new Rect(r.x + 12 + i * cw, r.y + 8, cw - 8, r.height - 16);
                bool open = FinalMeta.IsRoleUnlocked(p, id);
                var rec = p.finalRecords.FirstOrDefault(x => x.roleId == id);
                bool cleared = rec != null && rec.clears > 0;
                ShadowLabel(new Rect(c.x, c.y, c.width, 26), (cleared ? "✓ " : open ? "　" : "［未解放］") + FinalRoleNames[i], new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold }, cleared ? ClearGreen : open ? Color.white : new Color(0.6f, 0.6f, 0.6f));
                string line = !open ? FinalMeta.UnlockText(id)
                    : cleared ? $"クリア {rec.clears}回・最少 {rec.bestTurns}T" + (rec.clearedChallenge > 1 ? $"・深度{rec.clearedChallenge - 1}" : "") + (string.IsNullOrEmpty(rec.bestEchoRank) ? "" : $"・反響{rec.bestEchoRank}")
                    : rec != null && rec.runs > 0 ? $"未クリア・最高 {rec.bestFloor}/8階" : "まだ遊んでいない";
                GUI.Label(new Rect(c.x, c.y + 30, c.width, 44), line, new GUIStyle(Small) { fontSize = cw < 210 ? 12 : 14, wordWrap = true, normal = { textColor = new Color(0.85f, 0.88f, 0.95f) } });
            }
        }

        // ───── 役職と挑戦 ─────

        void DrawFinalRoles(float w, float h)
        {
            string sub = "役職を押すと始まる（Enter でも）。役職の能力は小さい。敵があなたの命令をまねるのは、どの役職も同じ。はじめは「旅の剣士」。";
            if (act.HasSave) sub += confirmRole != null ? "\n中断したランがあります：同じ役職をもう一度押すと、中断したランは消えて新しく始まる" : "\n注意：新しく始めると、続きのセーブは消える（1回目は確認だけ）。";
            Title(w, 20, "役職を選ぶ", sub);
            var p = act.Profile;
            var roles = FinalMeta.RoleOrder.Select(id => ActContent.Roles.First(x => x.Id == id)).ToArray();
            float cw = Mathf.Min(380, (w - 80) / 3f), ch = 236, gap = 16;
            float x0 = (w - (cw * 3 + gap * 2)) / 2;
            for (int i = 0; i < roles.Length; i++)
            {
                var baseRole = roles[i];
                var role = V4Content.Role(baseRole);
                bool open = FinalMeta.IsRoleUnlocked(p, role.Id);
                var r = new Rect(x0 + i * (cw + gap), 124, cw, open ? ch : 118f); // 【通常リリース版・画面の直し 14】未解放の役職の枠は小さく（選べる役職を主に）
                bool hover = open && r.Contains(Event.current.mousePosition);
                DrawUi(r, "panel", PanelBlue, open ? 1f : 0.45f); // 【RC5】乗せたときの赤い窓をやめた（赤＝敵の攻撃）
                if (hover || confirmRole == role.Id) DrawFrame(r, Gold, 2f);
                var rec = p.finalRecords.FirstOrDefault(x => x.roleId == role.Id);
                bool cleared = rec != null && rec.clears > 0;
                ShadowLabel(new Rect(r.x + 14, r.y + 10, r.width - 28, 28), (cleared ? "✓ " : "") + role.Name, new GUIStyle(Label) { fontSize = 21, fontStyle = FontStyle.Bold }, open ? (cleared ? ClearGreen : Gold) : new Color(0.6f, 0.6f, 0.6f));
                if (!open)
                {
                    var lockSt = new GUIStyle(Small) { fontSize = 15, wordWrap = false, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
                    GUI.Label(new Rect(r.x + 14, r.y + 46, r.width - 28, r.height - 52), WrapJa($"［未解放］解放：{FinalMeta.UnlockText(role.Id)}", lockSt, r.width - 28), lockSt);
                    continue;
                }
                var descSt = new GUIStyle(Small) { fontSize = 15, wordWrap = false };
                GUI.Label(new Rect(r.x + 14, r.y + 44, r.width - 28, 70), WrapJa(role.Description, descSt, r.width - 28), descSt); // 【RC5・最終検証】禁則（「確／定」「は／じめて」）
                var tipSt = new GUIStyle(Small) { fontSize = 14, wordWrap = false, normal = { textColor = new Color(1f, 0.9f, 0.65f) } };
                if (false) GUI.Label(new Rect(r.x + 14, r.y + 116, r.width - 28, 54), WrapJa("コツ：" + role.CopyHint, tipSt, r.width - 28), tipSt); // 【通常リリース版・採用担当のレビュー「役職のコツが解き方を先に言う」】出さない // 【RC5・最終検証】禁則（「。」だけの行が出ていた）
                string record = rec == null || rec.runs == 0 ? "記録なし" : cleared ? $"クリア {rec.clears}回・最少 {rec.bestTurns}ターン" + (rec.clearedChallenge > 1 ? $"・深度{rec.clearedChallenge - 1}まで" : "") + (string.IsNullOrEmpty(rec.bestEchoRank) ? "" : $"・反響ランク {rec.bestEchoRank}") : $"最高 {rec.bestFloor}/8階";
                GUI.Label(new Rect(r.x + 14, r.yMax - 32, r.width - 28, 24), $"HP {V4Content.StartHp + role.HpDelta}　{record}", new GUIStyle(Tiny) { fontSize = 14 });
                int kbi = kbIndex; bool byKey = kbActivate && kbFocus == kbi; // この役職のキーボードの番号（Hit が使う）・Enter で押したか
                if (Hit(r))
                {
                    Sfx.Play(Sfx.Kind.Button);
                    if (act.HasSave && confirmRole != role.Id) { confirmRole = role.Id; if (byKey) kbFocus = kbi; } // 【RC5・レビュー】1回の押し間違いで中断したランが消えていた。【コードレビュー】キーボードで選んだら、次の Enter が同じ役職を押すように枠を残す
                    else { confirmRole = null; act.StartRun(baseRole); }
                }
            }

            // 挑戦（反響深度）：【RC12】ACT1 を一度クリアすると選べる（役職とは別：どの役職でも、ふつうでも深度でも遊べる）
            int max = FinalMeta.MaxChallenge(p);
            float cy = 124 + ch + 26;
            if (max > 0)
            {
                act.NextChallenge = Mathf.Clamp(act.NextChallenge, 0, max);
                ShadowLabel(new Rect(0, cy, w, 24), "挑戦（反響深度）", new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, CyanText);
                float bw = 150, bx = (w - (bw * (max + 1) + 10 * max)) / 2;
                for (int lv = 0; lv <= max; lv++)
                    {
                    bool recommend = lv == 1 && !p.finalRecords.Any(r => r.clearedChallenge >= 2); // 【RC12】はじめてのクリアのあと、深度1 を「おすすめ」に（強制はしない）
                    var br = new Rect(bx + lv * (bw + 10), cy + 50, bw, 38); // 【通常リリース版・画面の直し 14】「おすすめ」が見出しに重なっていた → ボタンを下げて、見出しとボタンの間に置く
                    if (Button(br, lv == 0 ? "ふつう" : $"深度 {lv}", act.NextChallenge == lv, true, 16)) act.NextChallenge = lv;
                    if (recommend) ShadowLabel(new Rect(br.x, br.y - 21, br.width, 19), "おすすめ", new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, Gold);
                }
                GUI.Label(new Rect(w * 0.1f, cy + 96, w * 0.8f, 24), V4Content.ChallengeText(act.NextChallenge) + (act.NextChallenge > 1 ? "（下の段階の効果も全部）" : ""), new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.85f, 0.92f, 1f) } });
            }
            else
            {
                act.NextChallenge = 0;
                GUI.Label(new Rect(w * 0.1f, cy + 10, w * 0.8f, 24), "ACT1 を一度クリアすると、挑戦「反響深度」が選べるようになる（役職とは別。どの役職でも遊べる）。", new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } });
            }
            if (Button(new Rect(20, h - 56, 160, 40), "← タイトル", false, true, 16)) act.OpenTitle();
            if (!BuildInfo.ExternalPlaytest && roles.Any(x => !FinalMeta.IsRoleUnlocked(p, x.Id)) && Button(new Rect(w - 240, h - 50, 220, 32), "（開発用）全部を解放", false, true, 13)) act.UnlockAllRolesForTesting();
        }

        // ───── 休息（回復・強化・削除）【RC6】言葉を商人とそろえた（前は 休む・鍛える・祈る） ─────

        void DrawFinalRest(float w, float h)
        {
            var run = act.Run;
            Title(w, 56, "静かな祠", run.RestDone.Count == 0 ? (run.RestActions == 1 ? "3つのうち1つ：回復する・札を強化する・札を削除する。" : $"3つのうち{run.RestActions}つ（同じものは1回）：回復する・札を強化する・札を削除する。") : run.RestFinished ? "選んだ。次の扉へ。" : $"あと{run.RestActions - run.RestDone.Count}つ選べる（選ばずに進んでもよい）。"); // 【通常リリース版】選べる数は ActContent.RestPicks
            float bw = 300, gap = 20, x = (w - (bw * 3 + gap * 2)) / 2, y = 190;
            var a = new Rect(x, y, bw, 200);
            var b = new Rect(x + bw + gap, y, bw, 200);
            var c = new Rect(x + (bw + gap) * 2, y, bw, 200);
            foreach (var r in new[] { a, b, c }) DrawUi(r, "panel", PanelBlue);
            var head = new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            var body = new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true };
            ShadowLabel(new Rect(a.x, a.y + 14, a.width, 30), "回復する", head, new Color(0.4f, 1f, 0.6f));
            GUI.Label(new Rect(a.x + 16, a.y + 54, a.width - 32, 70), $"HP を {run.RestHealAmount} 回復\n（{run.Hp} → {Mathf.Min(run.MaxHp, run.Hp + run.RestHealAmount)}／{run.MaxHp}）", body);
            if (Button(new Rect(a.x + 50, a.yMax - 52, a.width - 100, 40), run.RestDone.Contains("heal") ? "回復した" : run.Hp >= run.MaxHp ? "HP は満タン" : "回復する", true, run.Hp < run.MaxHp && run.CanRest("heal"), 17)) { run.RestHeal(); run.MarkRest("heal"); act.Checkpoint(); if (run.RestFinished) act.NextFloor(); }
            bool canForge = run.AnyUpgradable && run.CanRest("upgrade");
            ShadowLabel(new Rect(b.x, b.y + 14, b.width, 30), "強化する", head, new Color(1f, 0.75f, 0.4f));
            { var ur = new Rect(b.x + 16, b.y + 50, b.width - 32, 92); string ut = WrapFit($"攻撃の札1種類を、このランの間ずっと強くする。\n例：{UpgradeExample()}", new GUIStyle(body) { wordWrap = false }, ur, 12, out var uf); GUI.Label(ur, ut, uf); } // 【第2巡 9】今の強化の形の例（「斬撃 4 →／6」と矢印の後で切れていた） // 【通常リリース版・QA】「強／くする」と切れていた // 【RC11】強化は札ごとに1つ
            if (Button(new Rect(b.x + 50, b.yMax - 52, b.width - 100, 40), run.RestDone.Contains("upgrade") ? "強化した" : canForge || !run.AnyUpgradable ? (run.AnyUpgradable ? "札を選ぶ" : "強化できる札が無い") : "札を選ぶ", true, canForge, 17)) { removeMode = "forge"; forgePick = null; }
            ShadowLabel(new Rect(c.x, c.y + 14, c.width, 30), "削除する", head, new Color(0.85f, 0.7f, 1f));
            GUI.Label(new Rect(c.x + 16, c.y + 54, c.width - 32, 70), "デッキの札を1枚削除（無料）。\n敵に渡したくない札を減らせる。", body);
            if (Button(new Rect(c.x + 50, c.yMax - 52, c.width - 100, 40), run.RestDone.Contains("remove") ? "削除した" : "札を選ぶ", true, run.Deck.Count > run.MinDeck && run.CanRest("remove"), 17)) removeMode = "rest"; // 【RC5・最終検証】呼び名を「札」に
            if (run.V4Run && Button(new Rect(w / 2 - 150, y + 250, 300, 44), run.RestDone.Count > 0 ? "次の扉へ" : "何もせずに次の扉へ", run.RestDone.Count > 0, true, 16)) act.NextFloor(); // 【RC6】2つ目を選ばずに進む【レビュー2回目】何も選べないとき（HP 満タン・強化も削除もできない）に止まっていた
            DrawUpgradedList(new Rect(0, y + 214, w, 24));
        }

        /// <summary>【RC6】強化した札の一覧（1行）。</summary>
        void DrawUpgradedList(Rect r)
        {
            var run = act.Run;
            if (run.Forged.Count == 0) return;
            string text = "強化した札：" + string.Join("・", run.Forged.Select(k => (run.Deck.FirstOrDefault(d => d.name == DeckState.UpgradeBaseName(k))?.displayName ?? DeckState.UpgradeBaseName(k)) + "［" + V4Content.UpgradeName(DeckState.UpgradeKindOf(k)) + "］"));
            GUI.Label(r, text, Fit(new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.8f, 0.5f) } }, text, r.width - 40, 11));
        }

        /// <summary>【RC6】強化で選んでいる札（null＝まだ選んでいない）。</summary>
        CommandData forgePick;

        /// <summary>
        /// 【RC6】強化する札を選ぶ → 3つの形から1つ（自分＋・まね＋・まね－）。休息（removeMode "forge"：終わったら次の階）・商人（"forge_shop"：ルーン片を払う）・中ボスの報酬（"forge_reward"：無料）。
        /// 「強い札は敵にも渡る」を強化でも選ばせる：まね＋は敵どうしを斬らせる人に、まね－は安全に使いたい人に。
        /// </summary>
        void DrawForgePicker(float w, float h)
        {
            var run = act.Run;
            var r = new Rect(w * 0.06f, 60, w * 0.88f, h - 90);
            DrawRect(new Rect(0, 0, w, h), new Color(0.02f, 0.03f, 0.06f, 0.93f)); // 後ろの画面の文字が透けて読みにくい（撮影で確認）【第2巡 6】0.8 → 0.93
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 1f)); // 【第2巡 6】欄は不透明
            DrawUi(r, "panel", PanelBlue);
            string where = removeMode == "forge_shop" ? $"（{run.UpgradePrice} ルーン片）" : removeMode == "forge_reward" ? "（中ボスの報酬・無料）" : "";
            ShadowLabel(new Rect(r.x + 16, r.y + 8, r.width - 200, 26), $"強化{where}：攻撃の札1種類を、このランの間ずっと強くする（同じ札は全部）", new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold }, Gold);
            if (Button(new Rect(r.xMax - 140, r.y + 6, 124, 32), forgePick == null ? "やめる" : "選び直す", false, true, 15)) { if (forgePick == null) removeMode = null; forgePick = null; return; }
            // 【RC11】1枚の札に強化は1つ：札を並べて「前 → 後」を出す。押したら、その場で強化（選び直しは無い：押す前に結果が見えている）
            var kinds = run.Deck.Where(d => run.UpgradeOf(d.name) == null && V4Content.CanUpgradeAny(d)).GroupBy(d => d.name).Select(g => g.First()).ToList();
            if (kinds.Count == 0) { GUI.Label(new Rect(r.x, r.y + 80, r.width, 30), "強化できる札が無い", new GUIStyle(Label) { fontSize = 18, alignment = TextAnchor.MiddleCenter }); return; }
            float cw = 150f, ch = cw * 1.32f, gap = 22f;
            int cols = Mathf.Max(1, Mathf.Min(kinds.Count, (int)((r.width - 40f + gap) / (cw + gap))));
            float x0 = r.center.x - (cols * cw + (cols - 1) * gap) / 2f, y0 = r.y + 56f;
            var pv = new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, wordWrap = false };
            for (int i = 0; i < kinds.Count; i++)
            {
                var c = kinds[i];
                var k = V4Content.DefaultUpgrade(c);
                var cr = new Rect(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + 74f), cw, ch);
                DrawCardV2(cr, c, false, false, 0, true, 1f, 0, k == UpgradeKind.Self ? DeckState.ForgeBonus : k == UpgradeKind.CopyUp && !c.selfInert ? DeckState.CopyUpSelf : 0, k == UpgradeKind.CopyUp ? DeckState.CopyUpgrade : 0); // 【通常リリース版・採用担当のレビュー「強化の画面の札が 4 のまま」】まね＋は あなたも +1
                string line = V4Content.UpgradePreview(c);
                int sp = line.IndexOf('（');
                string top = sp > 0 ? line.Substring(0, sp) : line, sub = sp > 0 ? line.Substring(sp) : "";
                if (top.StartsWith(c.displayName + "　")) top = top.Substring(c.displayName.Length + 1); // 【通常リリース版・ポートフォリオのレビュー「札の下の文が隣の札の文とつながる」】札の名前は上の札にあるので消す
                ShadowLabel(new Rect(cr.x - 8, cr.yMax + 4, cr.width + 16, 20), top, Fit(new GUIStyle(pv) { clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 0.85f, 0.45f) } }, top, cr.width + 16, 10), new Color(1f, 0.85f, 0.45f));
                GUI.Label(new Rect(cr.x - 12, cr.yMax + 24, cr.width + 24, 18), sub, Fit(new GUIStyle(pv) { fontSize = 11, fontStyle = FontStyle.Normal, normal = { textColor = new Color(0.85f, 0.9f, 1f) } }, sub, cr.width + 24, 8));
                if (Button(new Rect(cr.x + 10, cr.yMax + 44, cr.width - 20, 26), "これを強化", true, true, 13))
                {
                    if (removeMode == "forge_shop" ? run.BuyUpgrade(c, k) : run.UpgradeCard(c, k))
                    {
                        Sfx.Play(Sfx.Kind.Card);
                        if (removeMode == "forge_reward") run.UseRewardUpgrade();
                        bool rest = removeMode == "forge";
                        if (rest) run.MarkRest("upgrade"); // 【RC6】休息は2つまで
                        removeMode = null; forgePick = null;
                        act.Checkpoint();
                        if (rest && run.RestFinished) act.NextFloor();
                    }
                    return;
                }
            }
        }

        // ───── 結果 ─────

        void DrawFinalResult(float w, float h)
        {
            var run = act.Run;
            if (act.PendingBossRetry)
            {
                bool midRetry = run.Current != null && run.Current.IsMidBoss; // 【RC7】中ボスでも
                Title(w, 120, $"{run.Current?.Name}に敗れた…", (midRetry ? "中ボス" : "ボス") + "の直前から、1回だけやり直せる（中ボスとボスで1ランに1回。デッキ・札はそのまま。HP は入ったときの値）。" + (midRetry ? "ここで使うと、ボスではやり直せない。" : ""));
                float rbw = 420, rx = (w - rbw) / 2;
                if (Button(new Rect(rx, 260, rbw, 56), $"もう一度挑む（HP {run.BossEntryHp}・1ランに1回）", true, true, 18)) { act.RetryBossNow(); return; }
                if (Button(new Rect(rx, 330, rbw, 44), "あきらめて結果を見る", false, true, 16)) { act.GiveUpAfterBossLoss(); return; }
                GUI.Label(new Rect(w * 0.15f, 392, w * 0.7f, 48), "再挑戦でクリアしても記録に残る（役職の ✓ も付く）。挑戦「反響深度3」では再挑戦できない。", new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(0.85f, 0.9f, 1f) } });
                return;
            }
            bool clear = run.Cleared;
            int floor = Mathf.Min(run.Floor + 1, run.FloorCount);
            int minutes = Mathf.CeilToInt((float)(run.PlaySeconds / 60.0));
            string sub = clear ? $"{run.Role.Name}{(run.Challenge > 0 ? $"・反響深度{run.Challenge}" : "")}・{minutes}分・合計 {run.TotalTurns}ターン{(run.BossRetryUsed ? "・再挑戦あり" : "")}"
                               : $"{run.Role.Name}・{floor}階「{run.Current?.Name}」で倒れた。" + (run.LastHit != null ? $"　最後の一撃：{run.LastHit}" : "");
            Title(w, 22, clear ? "ACT1 CLEAR！" : "敗北…", sub);
            var head = new GUIStyle(Label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false };
            var line = new GUIStyle(Small) { fontSize = 15, wordWrap = false, clipping = TextClipping.Clip };
            var body = new GUIStyle(Small) { fontSize = 15, wordWrap = true };

            // 左：このランの数字
            var L = new Rect(w * 0.04f, 104, w * 0.45f, 500);
            DrawUi(L, "panel", PanelBlue);
            float x = L.x + 18, lw = L.width - 36, y = L.y + 14;
            ShadowLabel(new Rect(x, y, lw, 24), $"到達 {floor}/{run.FloorCount}階", head, Gold);
            // 【RC5・レビュー3】感想に貼り付けられるように、結果の数字を文字でコピー（Windows 版。ブラウザ版はコピーが外へ出ないので出さない）。名前などは入れない
            if (!BuildInfo.PublicBuild && Application.platform != RuntimePlatform.WebGLPlayer && Button(new Rect(L.xMax - 168, L.y + 10, 152, 26), Time.unscaledTime - resultCopiedAt < 3f ? "コピーした" : "結果をコピー（感想用）", false, true, 12)) // 【通常リリース版・画面の直し 22】開発用に見える：公開版では出さない
            {
                GUIUtility.systemCopyBuffer = $"{BuildInfo.Title} {BuildInfo.Version}\n役職：{run.Role.Name}・反響深度{run.Challenge}\n結果：{(clear ? "クリア" : $"{floor}階「{run.Current?.Name}」で負け")}{(run.BossRetryUsed ? "（ボスの再挑戦あり）" : "")}{(run.MidBattleResumes > 0 ? $"（戦闘の途中からの再開 {run.MidBattleResumes}回）" : "")}\n"
                    + $"到達 {floor}/{run.FloorCount}階・遊んだ時間 {minutes}分・合計 {run.TotalTurns}ターン・受けたダメージ {run.DamageBySource.Values.Sum()}\n"
                    + $"まねで敵を動かした・傷つけた {run.CopyUsesTotal}回・敵の大技を敵に当てた {run.BigHitsTotal}回・戦術札 {run.TacticUsesTotal}枚・勝った戦闘 {run.BattlesWon}・無傷の勝利 {run.NoDamageWins}"
                    + (run.Imprints.Count > 0 ? "\n刻印：" + string.Join("・", run.Imprints.Select(i => ActContent.Imprint(i).Name)) : "") // 【RC7】
                    + "\n受けたダメージの内訳：" + string.Join("・", run.DamageBySource.OrderByDescending(k => k.Value).Take(4).Select(k => $"{k.Key} {k.Value}")); // 【RC7】本人の砲王の負けは、何で受けたかが分からなかった
                resultCopiedAt = Time.unscaledTime;
            }
            y += 32;
            GUI.Label(new Rect(x, y, lw, 22), $"遊んだ時間 {minutes}分・合計 {run.TotalTurns}ターン・受けたダメージ {run.DamageBySource.Values.Sum()}", line); y += 28;
            // 【通常リリース版】反響ランク：敵へのダメージのうち、敵自身（まね・敵の攻撃・大技）と仕掛けが与えた割合。クリアしたランだけランクを出す（目安 V4Content.EchoRankBounds）
            string echoRank = run.EchoRank;
            if (echoRank != null) ShadowLabel(new Rect(x, y - 2, lw, 26), $"反響ランク {echoRank}　敵と仕掛けが与えた割合 {run.EchoSharePct}%・受けたダメージ {run.TotalDamage}", new GUIStyle(line) { fontSize = 17, fontStyle = FontStyle.Bold }, echoRank == "S" ? Gold : CyanText);
            else GUI.Label(new Rect(x, y, lw, 22), $"敵と仕掛けが与えた割合 {run.EchoSharePct}%・まねで敵を動かした・傷つけた {run.CopyUsesTotal}回", line);
            y += 28;
            GUI.Label(new Rect(x, y, lw, 22), $"敵の大技を敵に当てた {run.BigHitsTotal}回・戦術札 {run.TacticUsesTotal}枚", line); y += 28;
            GUI.Label(new Rect(x, y, lw, 22), $"デッキ {run.Deck.Count}枚＋戦術札 {run.TacticCards.Count}枚・勝った戦闘 {run.BattlesWon}", line); y += 28;
            if (run.NoDamageWins > 0) { ShadowLabel(new Rect(x, y, lw, 22), $"★ 無傷の勝利 {run.NoDamageWins}戦{(run.NoDamageWins >= run.BattlesWon && run.BattlesWon > 0 ? "（全部無傷！）" : "")}", new GUIStyle(line) { fontStyle = FontStyle.Bold }, new Color(1f, 0.85f, 0.4f)); y += 28; } // 【RC11】
            if (run.MidBattleResumes > 0) { GUI.Label(new Rect(x, y, lw, 22), $"戦闘の途中からの再開 {run.MidBattleResumes}回（中断 → 続きから）", line); y += 28; } // 【RC5・最終ゲート】
            if (run.Imprints.Count > 0) { GUI.Label(new Rect(x, y, lw, 22), "刻印：" + string.Join("・", run.Imprints.Select(i => ActContent.Imprint(i).Name)), line); y += 28; } // 【RC7】
            if (run.Forged.Count > 0) { GUI.Label(new Rect(x, y, lw, 22), "強化した札：" + string.Join("・", run.Forged.Select(k => (run.Deck.FirstOrDefault(d => d.name == DeckState.UpgradeBaseName(k))?.displayName ?? DeckState.UpgradeBaseName(k)) + "［" + V4Content.UpgradeName(DeckState.UpgradeKindOf(k)) + "］")), line); y += 28; } // 【RC6】
            var top = run.UsedCards.OrderByDescending(k => k.Value).Take(5).Select(k => $"{k.Key}×{k.Value}").ToList();
            GUI.Label(new Rect(x, y, lw, 44), $"よく使った札：{(top.Count > 0 ? string.Join("・", top) : "なし")}", body); y += 50;
            ShadowLabel(new Rect(x, y, lw, 22), "このランの流れ", head, new Color(0.75f, 0.85f, 1f)); y += 28;
            // 【RC5・レビュー】前は最後の数行だけで、1〜3階が黙って消えていた → 1つの階を1行に（部屋｜結果）。入りきらないときは「…」
            var compact = new System.Collections.Generic.List<string>();
            foreach (var l in run.History)
            {
                if (!l.StartsWith("　")) compact.Add(l);
                else if (compact.Count > 0 && !compact[compact.Count - 1].Contains("｜")) compact[compact.Count - 1] += "｜" + l.Trim();
                else if (compact.Count > 0 && (l.Trim().StartsWith("勝利") || l.Trim().StartsWith("敗北") || l.Trim().StartsWith("試練"))) { var hd = compact[compact.Count - 1]; compact[compact.Count - 1] = hd.Substring(0, hd.IndexOf('｜')) + "｜" + l.Trim(); } // 再挑戦のあとは最後の結果
            }
            int rows = Mathf.Max(3, (int)((L.yMax - 10 - y) / 20f));
            if (compact.Count > rows) compact = compact.Take(1).Concat(new[] { "…" }).Concat(compact.Skip(compact.Count - (rows - 2))).ToList();
            foreach (var l in compact)
            {
                var fs = new GUIStyle(line) { fontSize = 14, wordWrap = false, clipping = TextClipping.Clip };
                string shown = l;
                if (fs.CalcSize(new GUIContent(shown)).x > lw) { while (shown.Length > 4 && fs.CalcSize(new GUIContent(shown + "…")).x > lw) shown = shown.Substring(0, shown.Length - 1); shown += "…"; } // 【通常リリース版・ポートフォリオのレビュー「…敵どうしに強い・あな で切れる」】入りきらない行は「…」で終える
                GUI.Label(new Rect(x, y, lw, 20), shown, fs);
                y += 20f;
            }

            // 右：起きたこと（初クリア・解放）・次の目標・役職の記録
            var R = new Rect(w * 0.51f, 104, w * 0.45f, 500);
            DrawUi(R, "panel", PanelBlue);
            x = R.x + 18; lw = R.width - 36; y = R.y + 14;
            ShadowLabel(new Rect(x, y, lw, 24), clear ? "このランで解放・記録" : "このランの記録", head, ClearGreen); y += 32;
            if (act.FinalNews.Count == 0) { GUI.Label(new Rect(x, y, lw, 22), clear ? "（新しい解放は無し）" : act.Profile.finalRecords.Any(rr => rr.roleId == run.Role.Id && rr.clears > 0) ? "この役職はクリア済み。ほかの役職・挑戦で新しい物が解放される。" : "クリアすると、新しい役職・札・挑戦が解放される。", body); y += 30; } // 【RC5・レビュー】クリア済みの役職でも「クリアすると解放」と出ていた
            foreach (var n in act.FinalNews)
            {
                ShadowLabel(new Rect(x, y, lw, 24), "★ " + n, new GUIStyle(body) { fontStyle = FontStyle.Bold, wordWrap = false }, Gold);
                y += 28;
            }
            y += 8;
            string goal = FinalMeta.NextGoal(act.Profile);
            if (goal != null) { GUI.Label(new Rect(x, y, lw, 44), goal, new GUIStyle(body) { normal = { textColor = CyanText } }); y += 52; }
            if (!clear)
            {
                ShadowLabel(new Rect(x, y, lw, 22), "次に試すこと", head, new Color(0.6f, 1f, 0.75f)); y += 28;
                foreach (var tip in run.ResultTips().Take(3)) { GUI.Label(new Rect(x, y, lw, 44), "・" + tip, body); y += 44; }
            }
            DrawRoleRecords(new Rect(R.x + 10, R.yMax - 108, R.width - 20, 98));

            // 下：もう一度
            float bw2 = 260, gap = 16, bx = (w - (bw2 * 3 + gap * 2)) / 2, by = h - 64;
            // 【RC2】このランで新しい役職が解放されたら、主のボタンは「その役職で始める」（レビュー：解放しても目立つのは同じ役職のもう一度だった）
            var newRole = act.NewRoleThisRun;
            if (newRole != null)
            {
                if (Button(new Rect(bx, by, bw2, 48), $"新しい役職：{V4Content.Role(newRole).Name}で始める", true, true, 16)) { act.OpenV4RunRoleSelect(); act.StartRun(newRole); return; }
            }
            else if (Button(new Rect(bx, by, bw2, 48), $"もう一度（{run.Role.Name}）", true, true, 17)) { act.Retry(false); return; }
            if (Button(new Rect(bx + bw2 + gap, by, bw2, 48), "役職を選ぶ", false, true, 17)) { act.OpenV4RunRoleSelect(); return; }
            if (Button(new Rect(bx + (bw2 + gap) * 2, by, bw2, 48), "タイトルへ", false, true, 17)) act.OpenTitle();
        }

        // ───── 【RC3】はじめての案内・チュートリアルの完了・設定（音量） ─────

        bool showSettings;
        string confirmRole; // 【RC5・レビュー】続きのセーブがあるとき、役職をもう一度押すまで始めない
        bool ShowFirstChoice => !act.Profile.tutorialOffered && !act.Profile.tutorialDone && !act.Profile.finalRecords.Any(r => r.clears > 0); // 【RC3】RC2 で遊んだ人にも1回（チュートリアルは RC3 から）【通常リリース版・画面の直し 13】ACT1 をクリアした人には出さない（クリアのあとのタイトルに金の「おすすめ」のチュートリアルが出ていた）

        /// <summary>
        /// 【RC4】開発版だけのボタン（左下）：研究室・検証ステージへ／チュートリアル未閲覧にする／はじめての状態に戻す。
        /// 本人は Editor の「再生」で確かめることがある：プロフィール（ラン・クリアの記録）が残っていると、はじめての案内・はじめてのランの教える順番が出ない。
        /// </summary>
        void DrawDevTitleTools(float w, float h)
        {
            var box = new Rect(16, h - 206, 290, 156);
            DrawRect(box, new Color(0.15f, 0.08f, 0.02f, 0.8f));
            DrawFrame(box, new Color(1f, 0.6f, 0.2f, 0.8f), 1f);
            GUI.Label(new Rect(box.x + 8, box.y + 4, box.width - 16, 18), "【開発版だけ】公開版には出ません", new GUIStyle(Tiny) { fontSize = 11, normal = { textColor = new Color(1f, 0.75f, 0.45f) } });
            if (Button(new Rect(box.x + 8, box.y + 24, box.width - 16, 30), "研究室・検証ステージ（開発用のタイトル）", false, true, 12)) ShowDevTitle = true;
            if (Button(new Rect(box.x + 8, box.y + 60, box.width - 16, 30), "チュートリアル未閲覧にする", false, true, 12)) act.DevResetTutorialSeen();
            bool armed = Time.unscaledTime - devResetArmedAt < 3f;
            if (Button(new Rect(box.x + 8, box.y + 96, box.width - 16, 30), armed ? "本当に？（記録は .backup に残る）" : "はじめての状態に戻す", false, true, 12))
            {
                if (armed) { devResetArmedAt = -10f; act.DevResetToFirstLaunch(); }
                else devResetArmedAt = Time.unscaledTime;
            }
            // 【RC4・本人「画質が悪い」の切り分け】Editor の Game ビューの拡大（Scale）や小さい解像度でぼやけるのは Editor の表示。ゲームの画質はビルドで見る
            string res = $"画面 {UnityEngine.Screen.width}×{UnityEngine.Screen.height}";
            GUI.Label(new Rect(box.x + 8, box.y + 130, box.width - 16, 22), Application.isEditor ? res + "：Editor は Game ビューの Scale 1x で見る" : res,
                new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 0.8f, 0.55f) } });
        }

        void DrawFirstChoice(float w, float h)
        {
            // 【RC4】はじめての人に必ず気づいてもらう：大きな窓・「おすすめ」の印・本編との違いを1行で
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.92f)); // 【RC5・最終ゲート】後ろのタイトルの説明文が透けていた（0.7 → 0.88）【第2巡 6】0.92
            var r = new Rect(w / 2 - 320, h * 0.18f, 640, 380);
            DrawUi(r, "panel", PanelBlue, 0.98f);
            DrawFrame(r, Gold, 3f);
            bool returning = act.Profile.finalRuns > 0;
            ShadowLabel(new Rect(r.x, r.y + 20, r.width, 36), returning ? "新しいチュートリアルがあります" : "はじめて遊ぶ方へ", new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter }, Gold);
            ShadowLabel(new Rect(r.x + 30, r.y + 64, r.width - 60, 28), "このゲームでは、あなたの命令を 敵も まねします。", new GUIStyle(Label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, CyanText);
            // 【RC10・WebGL の確認】タイトルの並び（案B：★5分体験が一番上）と、はじめての窓の「おすすめ」をそろえる（前は窓だけチュートリアルが金・5分体験が小さかった）
            bool quickFirst = !returning && TitleLayout == 2;
            string intro = quickFirst
                ? "★5分体験＝このゲームの面白さを3問で。\nチュートリアル＝本編の操作を全部（5〜8分）。\nどちらも、あとからタイトルでいつでも遊べます。" // 【画面の直し 26】
                : "チュートリアル（5〜8分）で、動く・攻撃・まね・赤い予告・敵の動き・緑の札・大技と、1ランの進み方を、実際に操作して覚えます。あとからタイトルの「チュートリアル」でいつでも遊び直せます。";
            var introSt = new GUIStyle(Small) { fontSize = 15, wordWrap = false, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.9f, 0.93f, 1f) } };
            GUI.Label(new Rect(r.x + 40, r.y + 96, r.width - 80, 66), WrapJa(intro, introSt, r.width - 84), introSt); // 【RC11・初見テスト「遊べ／ます。」】禁則で折り返す
            var main = new Rect(r.x + 110, r.y + 168, r.width - 220, 64);
            if (quickFirst) { if (Button(main, "★ 5分でこのゲームを体験", true, true, 22)) { act.DeclineTutorial(); act.StartQuick(); } }
            else if (Button(main, "チュートリアルを遊ぶ", true, true, 22)) { act.DeclineTutorial(); act.StartTutorial(); }
            var badge = new Rect(main.xMax - 70, main.y - 12, 84, 24);
            DrawRect(badge, new Color(0.85f, 0.25f, 0.2f, 0.95f));
            DrawFrame(badge, Color.white, 1f);
            GUI.Label(badge, "おすすめ", new GUIStyle(Tiny) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            // 【RC10】5分だけ触る人（採用担当など）の入口もここに
            if (quickFirst) { if (Button(new Rect(r.x + 60, r.y + 250, r.width / 2 - 70, 44), "チュートリアル", false, true, 17)) { act.DeclineTutorial(); act.StartTutorial(); } }
            else if (Button(new Rect(r.x + 60, r.y + 250, r.width / 2 - 70, 44), "★ 5分体験", false, true, 17)) { act.DeclineTutorial(); act.StartQuick(); }
            if (Button(new Rect(r.x + r.width / 2 + 10, r.y + 250, r.width / 2 - 70, 44), "すぐに本編を始める", false, true, 16)) { act.DeclineTutorial(); act.OpenV4RunRoleSelect(); }
            var sub = new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.75f, 0.8f, 0.9f) } };
            GUI.Label(new Rect(r.x + 60, r.y + 296, r.width / 2 - 70, 18), quickFirst ? "本編の操作を全部（5〜8分）" : "面白さを3問で", sub); // 【画面の直し 26】
            GUI.Label(new Rect(r.x + r.width / 2 + 10, r.y + 296, r.width / 2 - 70, 18), "1ラン 約30分", sub);
            GUI.Label(new Rect(r.x, r.y + 336, r.width, 22), "（本編の1階・2階も、少しずつ教えながら進みます）", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.8f, 0.9f) } });
        }

        // ───── 【RC4】チュートリアルの最後「1ランの流れ」（本人「初心者にはゲーム全体の進み方が分からない」） ─────

        static readonly string[] RunLoopFlow = { "戦闘に勝つ", "報酬の札を選ぶ", "扉を選ぶ", "くり返す", "8階のボス" };

        /// <summary>報酬 → 扉 → 8階の地図を、本物に近い画面で1回ずつ触る。選んだ物は本編に残らない。</summary>
        void DrawTutorialRun(float w, float h)
        {
            int page = act.TutorialRunPage;
            Title(w, 18, $"チュートリアル {TutorialContent.Count}/{TutorialContent.Count}　1ランの流れ", null);
            // 流れの帯（今どこか）
            float fw = Mathf.Min(w - 80, 1000f), fx = (w - fw) / 2, bwid = (fw - 4 * 30) / 5;
            int now = page == 0 ? 1 : page == 1 ? 2 : 4;
            var flowStyle = new GUIStyle(Small) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            for (int i = 0; i < RunLoopFlow.Length; i++)
            {
                var b = new Rect(fx + i * (bwid + 30), 70, bwid, 34);
                bool on = i == now || (page == 2 && i == 3);
                DrawRect(b, on ? new Color(0.8f, 0.62f, 0.2f, 0.95f) : i < now ? new Color(0.25f, 0.35f, 0.55f, 0.9f) : new Color(1f, 1f, 1f, 0.08f));
                DrawFrame(b, on ? Gold : new Color(1f, 1f, 1f, 0.25f), on ? 2f : 1f);
                GUI.Label(b, RunLoopFlow[i], new GUIStyle(flowStyle) { normal = { textColor = on ? Color.black : Color.white } });
                if (i < RunLoopFlow.Length - 1) ShadowLabel(new Rect(b.xMax, b.y, 30, b.height), "→", new GUIStyle(flowStyle) { fontSize = 18 }, new Color(1f, 1f, 1f, 0.7f));
            }
            var body = new GUIStyle(Small) { fontSize = 17, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
            var note = new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = CyanText } };
            bool canNext = false;
            string next = null;
            if (page == 0)
            {
                string rewardText = "戦闘に勝つと、ルーン片と、命令札3枚から1枚を無料で選べる（中ボスは戦術札と、刻印か強化も）。2枚目はルーン片で（取らなくてもよい）。\n取った札はデッキに入る＝デッキが毎戦育つ。強い札は敵もまねる。1枚えらんでみよう。"; var rewardBody = new GUIStyle(body) { fontSize = 16, wordWrap = false }; GUI.Label(new Rect(w * 0.1f, 120, w * 0.8f, 64), WrapJa(rewardText, rewardBody, w * 0.8f), rewardBody); // 【RC5・最終ゲート】3行目が札の上で切れていた（高さ 50 → 64・禁則）
                var cards = TutorialContent.RunLoopRewardCards.Select(act.Card).Where(c => c != null).ToList();
                float cw = 150, ch = 196, gap = 26, cx = (w - (cw * cards.Count + gap * (cards.Count - 1))) / 2;
                for (int i = 0; i < cards.Count; i++)
                {
                    var r = new Rect(cx + i * (cw + gap), 186, cw, ch);
                    DrawCardV2(r, cards[i], i == act.TutorialRunCard, false, 0, true, 1f, 0);
                    if (r.Contains(Event.current.mousePosition)) DrawFrame(r, Gold, 2f);
                    if (KeyboardHit(r, false) || GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Play(Sfx.Kind.Button); act.TutorialRunPickCard(i); }
                }
                if (act.TutorialRunCard >= 0 && act.TutorialRunCard < cards.Count)
                    GUI.Label(new Rect(w * 0.1f, 398, w * 0.8f, 44), $"「{cards[act.TutorialRunCard].displayName}」をデッキに入れた。（見本なので、本編のデッキには入らない）\n本編では、商人で札・強化を買ったり、休息で札を強化したりもできる。", note);
                canNext = act.TutorialRunCard >= 0;
                next = "次へ：扉を選ぶ";
            }
            else if (page == 1)
            {
                GUI.Label(new Rect(w * 0.1f, 122, w * 0.8f, 50), "次の部屋を、扉で選ぶ（ふつうは2つから1つ）。\n扉には、部屋の種類・敵・報酬の傾向が書いてある。どちらかを選んでみよう。", body);
                string[] names = { "鏡の歩兵", "静かな祠" }, kinds = { "通常戦", "休息" }, arts = { "door_battle", "door_rest" }; // 【RC5・最終検証】本番の休息の扉と同じ名前・文
                string[] infos = { "剣兵2体（全まね）。\n勝つと報酬：札3枚から1枚。", "回復・強化・削除から1つ\n（HP の回復・札を強く・札を削除）" }; // 【通常リリース版】休息は3つから1つ
                var kindColors = new[] { new Color(0.75f, 0.8f, 0.95f), new Color(0.4f, 1f, 0.6f) };
                float dw = 280, dh = 250, gap = 60, dx = (w - (dw * 2 + gap)) / 2;
                for (int i = 0; i < 2; i++)
                {
                    var r = new Rect(dx + i * (dw + gap), 182, dw, dh);
                    DrawUi(r, "panel", PanelBlue);
                    bool sel = i == act.TutorialRunDoor;
                    if (sel) DrawFrame(r, Gold, 3f); else if (r.Contains(Event.current.mousePosition)) DrawFrame(r, Gold, 2f);
                    var art = new Rect(r.center.x - 60, r.y + 10, 120, 110);
                    if (!DrawUiImage(art, arts[i], 1f, ScaleMode.ScaleToFit)) DrawRect(art, kindColors[i] * 0.5f);
                    ShadowLabel(new Rect(r.x, r.y + 124, r.width, 28), names[i], new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Gold);
                    GUI.Label(new Rect(r.x, r.y + 152, r.width, 20), kinds[i], new GUIStyle(Small) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = kindColors[i] } });
                    GUI.Label(new Rect(r.x + 14, r.y + 178, r.width - 28, 60), infos[i], new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.UpperCenter, wordWrap = true });
                    if (sel) ShadowLabel(new Rect(r.x, r.yMax - 4, r.width, 24), "この扉を選んだ", new GUIStyle(Small) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Gold);
                    if (KeyboardHit(r, false) || GUI.Button(r, GUIContent.none, GUIStyle.none)) { Sfx.Play(Sfx.Kind.Button); act.TutorialRunPickDoor(i); }
                }
                if (act.TutorialRunDoor >= 0)
                    GUI.Label(new Rect(w * 0.1f, 456, w * 0.8f, 44), "選んだ部屋へ進む（1階ずつ）。HP は戦闘のあとも続く。減ったら休息で回復しよう。", note);
                canNext = act.TutorialRunDoor >= 0;
                next = "次へ：8階の地図";
            }
            else
            {
                GUI.Label(new Rect(w * 0.1f, 122, w * 0.8f, 50), "これを くり返して、8階のボス「鏡の主」を倒すと 1ラン クリア（約30分）。", body);
                float mw = Mathf.Min(w - 80, 1040f), mx = (w - mw) / 2, cw = (mw - 7 * 10) / 8;
                for (int i = 0; i < 8; i++)
                {
                    var r = new Rect(mx + i * (cw + 10), 170, cw, 96);
                    bool boss = i == 7;
                    DrawRect(r, boss ? new Color(0.35f, 0.12f, 0.45f, 0.95f) : new Color(0.1f, 0.14f, 0.25f, 0.95f));
                    DrawFrame(r, boss ? new Color(0.85f, 0.5f, 1f) : new Color(1f, 1f, 1f, 0.3f), boss ? 3f : 1f);
                    ShadowLabel(new Rect(r.x, r.y + 6, r.width, 24), $"{i + 1}階", new GUIStyle(Label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, boss ? new Color(0.95f, 0.75f, 1f) : Gold);
                    GUI.Label(new Rect(r.x + 2, r.y + 34, r.width - 4, 58), TutorialContent.RunLoopFloors[i], new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = Color.white } });
                    if (i < 7) ShadowLabel(new Rect(r.xMax, r.y + 34, 10, 24), "›", new GUIStyle(Small) { fontSize = 14, alignment = TextAnchor.MiddleCenter }, new Color(1f, 1f, 1f, 0.6f));
                }
                string[] lines =
                {
                    "・途中で倒れたら、そのランは終わり。次のランは1階から（中ボスかボスに負けたときは、1ランに1回だけ その部屋の直前からやり直せる）。",
                    "・クリアすると、役職・札・挑戦（難しさ）が増える。HP や攻撃力が永久に上がることはない。",
                    "・はじめてのランは少しずつ：1階は敵が まねるだけ → 2階から敵の攻撃と動き → 3階から緑の札（大技は中ボスと5階から）。",
                };
                var ls = new GUIStyle(Small) { fontSize = 16, wordWrap = true, normal = { textColor = new Color(0.92f, 0.95f, 1f) } };
                float y = 292;
                foreach (var l in lines) { float hh = ls.CalcHeight(new GUIContent(l), mw - 40); GUI.Label(new Rect(mx + 20, y, mw - 40, hh), l, ls); y += hh + 8; }
                canNext = true;
                next = "わかった（チュートリアル完了へ）";
            }
            float nbw = 340;
            if (Button(new Rect((w - nbw) / 2, h - 132, nbw, 50), next, canNext, canNext, 18)) { act.TutorialRunNext(); return; }
            if (page == 0 && !canNext && Button(new Rect((w - 200) / 2, h - 74, 200, 32), "取らずに進む", false, true, 14)) { act.TutorialRunNext(); return; }
            if (Button(new Rect(20, h - 52, 200, 32), "チュートリアルをやめる", false, true, 13)) { act.SkipTutorial(); return; }
            var ev = Event.current;
            if (canNext && ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)) { ev.Use(); act.TutorialRunNext(); }
        }

        /// <summary>【RC4】完了：「あなたが覚えたこと」を 1ターン → 1戦闘 → 1ラン の順に（矢印でつなぐ）。主のボタンは「本編を始める」。</summary>
        void DrawTutorialDone(float w, float h)
        {
            if (act.QuickMode) { DrawQuickDone(w, h); return; } // 【RC9】5分体験のおわり
            Title(w, 22, "チュートリアル完了！", "わからなくなったら、タイトルの「チュートリアル」でいつでも遊び直せます。");
            ShadowLabel(new Rect(0, 112, w, 28), "あなたが覚えたこと", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Gold);
            float bw0 = Mathf.Min(w - 80, 760f), x0 = (w - bw0) / 2, y = 148;
            var st = new GUIStyle(Small) { fontSize = 17, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            for (int i = 0; i < TutorialContent.Summary.Length; i++)
            {
                var r = new Rect(x0, y, bw0, 40);
                DrawUi(r, "panel", PanelBlue, 0.95f);
                if (i == 1) DrawFrame(r, CyanText, 2f);
                string line = TutorialContent.Summary[i];
                int k = line.IndexOf('：');
                string head = k > 0 ? line.Substring(0, k) : line, rest = k > 0 ? line.Substring(k + 1) : "";
                ShadowLabel(new Rect(r.x + 16, r.y, 30, r.height), $"{i + 1}", new GUIStyle(st) { fontStyle = FontStyle.Bold }, Gold);
                ShadowLabel(new Rect(r.x + 44, r.y, 190, r.height), head, new GUIStyle(st) { fontStyle = FontStyle.Bold }, i == 1 ? CyanText : Gold);
                GUI.Label(new Rect(r.x + 236, r.y, r.width - 248, r.height), rest, new GUIStyle(st) { fontSize = 15, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
                y += 40;
                if (i < TutorialContent.Summary.Length - 1) { ShadowLabel(new Rect(x0, y, bw0, 14), "↓", new GUIStyle(Small) { fontSize = 13, alignment = TextAnchor.MiddleCenter }, new Color(1f, 1f, 1f, 0.6f)); y += 14; }
            }
            float bw = 360, x = (w - bw) / 2;
            if (Button(new Rect(x, y + 22, bw, 58), "本編を始める", true, true, 22)) { act.StartFirstRunAfterTutorial(); return; }
            GUI.Label(new Rect(0, y + 82, w, 20), "（役職「旅の剣士」で1階から）", new GUIStyle(Tiny) { fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.85f, 0.95f) } });
            if (Button(new Rect(x + 90, y + 108, bw - 180, 38), "タイトルへ", false, true, 15)) act.OpenTitle();
        }

        /// <summary>【RC9】5分体験のおわり：やったこと5つ → 本編・チュートリアル・タイトル。</summary>
        void DrawQuickDone(float w, float h)
        {
            Title(w, 22, "5分体験、おしまい！", "本編は 1回 約30分：部屋を選んで進み、札を集めて、8階のボスを目指す（負けたら はじめから・毎回ちがう道）。");
            ShadowLabel(new Rect(0, 112, w, 28), "あなたがやったこと", new GUIStyle(Label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Gold);
            float bw0 = Mathf.Min(w - 80, 760f), x0 = (w - bw0) / 2, y = 148;
            var st = new GUIStyle(Small) { fontSize = 17, wordWrap = false, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            for (int i = 0; i < TutorialContent.QuickSummary.Length; i++)
            {
                var r = new Rect(x0, y, bw0, 38);
                DrawUi(r, "panel", PanelBlue, 0.95f);
                string line = TutorialContent.QuickSummary[i];
                int k = line.IndexOf('：');
                string head = k > 0 ? line.Substring(0, k) : line, rest = k > 0 ? line.Substring(k + 1) : "";
                ShadowLabel(new Rect(r.x + 12, r.y, 22, r.height), $"{i + 1}", new GUIStyle(st) { fontStyle = FontStyle.Bold }, Gold);
                if (i < TutorialContent.QuickSummaryIcons.Length) DrawUiImage(new Rect(r.x + 34, r.y + 4, r.height - 8, r.height - 8), TutorialContent.QuickSummaryIcons[i], 1f, ScaleMode.ScaleToFit); // 【RC11】絵
                ShadowLabel(new Rect(r.x + 44 + 22, r.y, 128, r.height), head, new GUIStyle(st) { fontStyle = FontStyle.Bold }, Gold);
                GUI.Label(new Rect(r.x + 196, r.y, r.width - 208, r.height), rest, new GUIStyle(st) { fontSize = 15, normal = { textColor = new Color(0.9f, 0.93f, 1f) } });
                y += 44;
            }
            ShadowLabel(new Rect(0, y + 2, w, 26), "本編では、この3つが1つの盤面で組み合わさる。2問目からと同じく、結果は自分で読む（敵の攻撃のマスと順番は いつも見える）。", new GUIStyle(Small) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, CyanText); // 【RC9・初見テスト「小さい1行で読み飛ばしそう」】
            float bw = 360, x = (w - bw) / 2;
            if (Button(new Rect(x, y + 36, bw, 54), "本編を始める", true, true, 21)) { act.OpenTitle(); act.StartFirstRunAfterTutorial(); return; }
            if (Button(new Rect(x, y + 98, bw / 2 - 6, 38), "チュートリアル", false, true, 15)) { act.StartTutorial(); return; } // 【第2巡 9】字が2行になっていた → 短く・説明は下に
            GUI.Label(new Rect(x - 10, y + 138, bw / 2 + 14, 18), "本編の操作を全部（9つ）", new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.UpperCenter, wordWrap = false, normal = { textColor = new Color(0.82f, 0.87f, 0.96f) } }); // 【画面の直し 26】
            if (Button(new Rect(x + bw / 2 + 6, y + 98, bw / 2 - 6, 38), "タイトルへ", false, true, 14)) act.OpenTitle();
        }

        bool menuQuitArmed;
        float resultCopiedAt = -10f;

        /// <summary>【RC5・レビュー3】戦闘中のメニュー（O キーか右上の「メニュー」）。1 設定・2 遊び方・3 中断してタイトルへ（2回押す）・Esc 閉じる。</summary>
        void DrawBattleMenu(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.92f)); // 【第2巡 6】0.6 → 0.92
            var r = new Rect(w / 2 - 260, h * 0.2f, 520, 340);
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 1f));
            DrawUi(r, "panel", PanelBlue, 0.97f);
            DrawFrame(r, Gold, 2f);
            ShadowLabel(new Rect(r.x, r.y + 16, r.width, 32), "メニュー", new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter }, Gold);
            bool tut = act.InTutorial;
            string quit = tut ? (act.FestivalMode ? "3  やめる（文化祭の LEVEL を選ぶ画面へ）" : (act.QuickMode ? "3  5分体験をやめる（タイトルへ）" : "3  チュートリアルをやめる（タイトルへ）")) : menuQuitArmed ? "3  本当に中断する？（もう一度押す）" : "3  中断してタイトルへ";
            var ev = Event.current;
            int key = ev.type == EventType.KeyDown ? (ev.keyCode == KeyCode.Alpha1 || ev.keyCode == KeyCode.Keypad1 ? 1 : ev.keyCode == KeyCode.Alpha2 || ev.keyCode == KeyCode.Keypad2 ? 2 : ev.keyCode == KeyCode.Alpha3 || ev.keyCode == KeyCode.Keypad3 ? 3 : ev.keyCode == KeyCode.Escape || ev.keyCode == KeyCode.O ? 9 : 0) : 0;
            if (Button(new Rect(r.x + 60, r.y + 70, r.width - 120, 44), "1  設定（音量・画面の揺れと点滅）", false, true, 16) || key == 1) { showSettings = true; menuQuitArmed = false; }
            if (Button(new Rect(r.x + 60, r.y + 124, r.width - 120, 44), "2  遊び方", false, true, 16) || key == 2) { showHowTo = true; menuQuitArmed = false; }
            bool canQuit = act.CanSuspendNow; // 【通常リリース版・QA】ターンの実行中は押せない（負けが消えた・タイトルの裏で戦闘が進んだ）
            if (!canQuit) { quit = "3  中断（ターンが終わってから）"; menuQuitArmed = false; }
            if ((Button(new Rect(r.x + 60, r.y + 178, r.width - 120, 44), quit, false, canQuit, 16) || key == 3) && canQuit)
            {
                if (tut || menuQuitArmed) { menuQuitArmed = false; act.SuspendToTitle(); }
                else menuQuitArmed = true;
            }
            if (!tut) { var ms = new GUIStyle(Small) { fontSize = 11, wordWrap = false, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.85f, 0.88f, 0.95f) } }; GUI.Label(new Rect(r.x + 30, r.y + 226, r.width - 60, 46), WrapJa("中断：「続きから」でこの部屋の最初から（HP は今と入ったときの低い方）。2ターン目以降の中断は、ボーナス無し・ボスの部屋ではボスの再挑戦（残っていれば）を使う。", ms, r.width - 60), ms); } // 【RC5・最終検証】ボスの部屋のことも・禁則
            if (Button(new Rect(r.x + 160, r.y + 276, 200, 44), "閉じる（Esc）", true, true, 16) || key == 9) { act.BattleMenuOpen = false; menuQuitArmed = false; }
            if (key != 0 && ev.type == EventType.KeyDown) ev.Use();
        }

        /// <summary>【RC3・本人の要望「音が大きすぎる」】全体・BGM・効果音の音量（0〜100%）。設定は残る。</summary>
        bool volDirty;

        void DrawSettings(float w, float h)
        {
            DrawRect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.93f)); // 【第2巡 6】0.93【通常リリース版・画面の直し 15】後ろの文字が透けないように（0.6 → 0.82）
            float top = h * 0.16f;
            if (act.Screen == ActScreen.Title) top = Mathf.Max(top, h * 0.10f + 64f); // 【通常リリース版・画面の直し 15】タイトルの題名の字が窓の上からはみ出していた → 窓は題名の下から
            var r = new Rect(w / 2 - 300, top, 600, 410);
            DrawRect(r, new Color(0.04f, 0.05f, 0.09f, 0.97f));
            DrawUi(r, "panel", PanelBlue, 0.97f);
            DrawFrame(r, Gold, 2f);
            ShadowLabel(new Rect(r.x, r.y + 18, r.width, 32), "設定（音量・画面の揺れと点滅）", new GUIStyle(Big) { fontSize = 24, alignment = TextAnchor.MiddleCenter }, Gold);
            var p = act.Profile;
            int Slider(float y, string label, int v)
            {
                GUI.Label(new Rect(r.x + 40, y, 100, 30), label, new GUIStyle(Label) { fontSize = 18, alignment = TextAnchor.MiddleLeft });
                if (Button(new Rect(r.x + 132, y + 2, 32, 26), "－", false, true, 15)) v = Mathf.Max(0, v - 5); // 【RC5・レビュー】キーボードでも（矢印で選んで Enter）
                float nv = GUI.HorizontalSlider(new Rect(r.x + 172, y + 10, 270, 20), v, 0, 100);
                if (Button(new Rect(r.x + 450, y + 2, 32, 26), "＋", false, true, 15)) nv = Mathf.Min(100f, nv + 5f);
                GUI.Label(new Rect(r.x + 486, y, 74, 30), $"{Mathf.RoundToInt(nv)}%", new GUIStyle(Label) { fontSize = 18, alignment = TextAnchor.MiddleRight });
                return Mathf.RoundToInt(nv / 5f) * 5;
            }
            int m = Slider(r.y + 80, "全体", p.volMaster), b = Slider(r.y + 130, "BGM", p.volBgm), s = Slider(r.y + 180, "効果音", p.volSe);
            if (m != p.volMaster || b != p.volBgm || s != p.volSe) { p.volMaster = m; p.volBgm = b; p.volSe = s; p.volSet = true; SoundSettings.Apply(p); volDirty = true; }
            // 【RC3・レビュー】棒を離したら保存（前は「閉じる」まで保存されず、ブラウザのタブをそのまま閉じると消えた）
            if (volDirty && Event.current.rawType == EventType.MouseUp) { act.SaveProfile(); volDirty = false; }
            // 【通常リリース版・画面の直し 15】「ON（押すとOFF）」→ 今の状態が分かる形（「音 ON」。金にはしない：Enter の主のボタンは「閉じる」のまま）
            if (Button(new Rect(r.x + 40, r.y + 236, 240, 40), Sfx.Muted ? "音 OFF" : "音 ON", false, true, 16)) { Sfx.Muted = !Sfx.Muted; Bgm.SetMuted(Sfx.Muted); p.soundOff = Sfx.Muted; act.SaveProfile(); }
            if (Button(new Rect(r.x + 320, r.y + 236, 240, 40), "試しに鳴らす（効果音）", false, true, 15)) Sfx.Play(Sfx.Kind.Copy);
            // 【RC5・レビュー】画面の揺れを止められる（動きに弱い人向け）
            if (Button(new Rect(r.x + 40, r.y + 286, 520, 40), p.shakeOff ? "画面の揺れ・点滅 OFF" : "画面の揺れ・点滅 ON", false, true, 16)) { p.shakeOff = !p.shakeOff; ScreenFx.Disabled = p.shakeOff; act.SaveProfile(); }
            if (Button(new Rect(r.x + 200, r.y + 346, 200, 44), "閉じる", true, true, 17)) { showSettings = false; act.SaveProfile(); }
            var sk = Event.current;
            if (sk.type == EventType.KeyDown && sk.keyCode == KeyCode.Escape) { showSettings = false; act.SaveProfile(); sk.Use(); } // 【RC5】Esc でも閉じる
        }

        /// <summary>
        /// 【完成版】遊び方（1分で読める）。【通常リリース版・画面の直し 11「8段落の文字の壁」】短い4つの見出し（まね／赤と紫／1ターンの順番／操作）に。1つに2〜3行。
        /// 細かいこと（1ターンの被ダメの上限・刻印・ランの進み方）は凡例 L・チュートリアル・扉の画面へ。1行＝「見出し\t本文」（DrawHowTo が見出しを金色で描く）。
        /// </summary>
        static string[] FinalHowToLines() => new[]
        {
            "まね\tあなたが出した命令を、敵もそれぞれの位置から まねる（同じ向き・同じ距離）。だれもいない方への斬撃でも、まねた敵の隣に仲間がいれば当たる。まね方は敵ごとに違う（盤面の印：全＝全部／技＝移動以外／移＝移動だけ）。",
            "赤と紫\t赤（点線）は撃つ敵と一緒に動く（押す・引くでずれる・倒せば消える）。紫（斜線）は床に固定（押しても動かない・撃つ敵を倒せば消える）。★＝大技（数字＝あと何ターン）。どれも敵にも当たる。本編では結果の予告は出ない：自分で読む。",
            "1ターンの順番\t①あなたの命令（左から・3つまで）→ ②敵の攻撃（赤＝敵と動く・紫＝床に固定：A → B → C）→ ③まね → ④★の大技 → ⑤敵の移動 → ⑥床。今の段は盤面の上の帯で光る。",
            "操作\t札をクリック（1〜9）→ 盤面の矢印で向き（矢印キー）→ 実行（Enter）。BS で1つ戻す・Z X C で戦術札・L で凡例とキー・O でメニュー。扉・報酬などの画面も矢印と Enter で選べる。",
        };
    }
}
