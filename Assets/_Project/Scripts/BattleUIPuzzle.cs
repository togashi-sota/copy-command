using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【Puzzle v2・2026-10-06 本人プレイ】「このEnemyどうやって倒すの？」「Bossは今何をすればいいの？」が分からなかった →
    /// 攻略条件（何を成立させるか）を盤面の下に常時、大きめに出す。答え（手順）は出さない。
    /// ・【目的】一文＋進み具合（✓／×）
    /// ・倒し方：特別な敵だけ「直接 ×／まね ○／仕掛け ○」
    /// ・まね方の印：全まね／技まね／移動まね（最初の2ターンは文字つき、その後はアイコンだけ）
    /// ・はじかれた理由：その場で数秒（「無効！この敵には“まね攻撃”か仕掛けが必要」）
    /// ・凡例：いつでも開ける（上の「凡例」）
    /// </summary>
    public partial class BattleUI
    {
        bool showLegend;
        /// <summary>【RC5】凡例を開いている間は、ACT1 のヒントの帯を出さない（凡例の上に重なっていた）。</summary>
        public bool LegendOpen => showLegend;
        int shownHintSerial = -1;
        float shownHintAt = -10f;
        string shownHint;

        /// <summary>攻略条件の欄を出す戦闘か（パズルの部屋・検証部屋・目標が討伐以外・ボス）。</summary>
        bool IsPuzzleUi => turn != null && turn.Grid != null && turn.Grid.Model != null && turn.CurrentStage != null &&
                           (turn.CurrentStage.puzzleRoom || turn.CurrentStage.copyLab || turn.CurrentStage.combatRoom || turn.Grid.Model.Goal != RoomGoal.Defeat ||
                            turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord || e.Kind == EnemyKind.Golem || e.EchoArmor || e.Kind == EnemyKind.Cannon) ||
                            turn.Grid.Model.Board.StatueOrder.Count > 0 || turn.Grid.Model.Board.SealStoneHome.Count > 0);

        static readonly Color GoalDone = new Color(0.45f, 1f, 0.55f);
        static readonly Color GoalTodo = new Color(1f, 0.6f, 0.5f);

        /// <summary>【RC4】部屋の一言。はじめてのランの2階（大技なし）は、大技を使わない攻め方（RoomDef.LessonTip）。</summary>
        string LessonGoalLine(StageData stage)
        {
            var run = act != null && act.InBattle ? act.Run : null;
            if (run != null && run.LessonFloor2 && run.Current != null && !string.IsNullOrEmpty(run.Current.LessonTip)) return "2階：" + run.Current.LessonTip; // 【RC5・最終ゲート】「（緑の札は3階から・…）」を足すと3行目で切れた（扉の文と1階の結果に書いてある）
            return stage.sandboxGoal;
        }

        /// <summary>左下：攻略条件（目的・進み具合・倒し方）と、部屋の一言。</summary>
        void DrawGoalPanel(Rect r)
        {
            var m = turn.Grid.Model;
            var stage = turn.CurrentStage;
            float x = r.x + 10, y = r.y + 4, w = r.width - 16;
            var lord = m.Enemies.FirstOrDefault(e => e.IsAlive && (e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord));
            string head = lord != null ? $"段階{PuzzleBoss.PhaseNumber(lord)}/3　【目的】" : "【目的】"; // 【RC5】名札の「段階1/3」と同じ言い方（前は PHASE）
            string goal = PuzzleRules.GoalSentence(m, lord != null ? null : stage.goalText);
            var hs = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            float hw = hs.CalcSize(new GUIContent(head)).x;
            ShadowLabel(new Rect(x, y, hw + 4, 22), head, hs, Gold);
            ShadowLabel(new Rect(x + hw, y, w - hw, 22), goal, hs, Color.white);
            y += 23;
            string rule = lord != null ? PuzzleBoss.RuleLine(lord) : null;
            if (rule != null) { ShadowLabel(new Rect(x, y - 2, w, 19), rule, Fit(new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip }, rule, w, 9), new Color(0.85f, 0.9f, 1f)); y += 18; } // 【RC5・最終検証】16:10・4:3 で「予告」が切れていた

            // 【Combat V4 RC2】長い説明は出さない：目的 ＋ この部屋のしかけ（1行）＋ 詳細の案内
            if (IsV4Ui)
            {
                string hz = stage.hazardName ?? "";
                string hzLine = hz.Contains("レーザー") ? "レーザー：1行ずつ掃く（細線＝次／太い光＝今ターン）全員に4ダメージ" : // 【RC7】「-4」→「4ダメージ」
                                hz.Contains("落石") ? "落石：影とヒビ＝次のターン／岩＝今ターンに落ちる　全員に4ダメージ" :
                                hz.Contains("炎") ? "炎床：火種＝次のターン／炎＝今ターンに燃える　全員に4ダメージ" :
                                lord != null ? CombatBoss.ShortHint(lord) : LessonGoalLine(stage);
                // 【RC2】部屋の一言は2行まで折り返す（「処刑の直線に」で切れて、肝心のコツが読めなかった：初見のレビュー）
                var hzStyle = new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
                string hzRaw = hzLine;
                if (!string.IsNullOrEmpty(hzRaw)) hzLine = WrapJa(hzRaw, hzStyle, w); // 【RC5・最終検証】禁則（「、」で始まる行）
                if (!string.IsNullOrEmpty(hzLine) && hzStyle.CalcHeight(new GUIContent(hzLine), w) > 34f) { hzStyle.fontSize = 11; hzLine = WrapJa(hzRaw, hzStyle, w); } // 【RC5・最終ゲート】2行に収まらないときは小さく（16:10 の狭い欄）
                float hzH = string.IsNullOrEmpty(hzLine) ? 0 : Mathf.Min(34f, hzStyle.CalcHeight(new GUIContent(hzLine), w));
                if (!string.IsNullOrEmpty(hzLine)) ShadowLabel(new Rect(x, y - 2, w, hzH + 2), hzLine, hzStyle, new Color(1f, 0.9f, 0.7f));
                ShadowLabel(new Rect(x, y + Mathf.Max(20f, hzH + 2f), w, 18), "敵にマウスを乗せる／クリックで詳しい説明　凡例ボタンで色の意味", new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip }, new Color(0.7f, 0.75f, 0.9f));
                return;
            }

            // 進み具合（✓ 済み・× まだ）
            var items = PuzzleRules.Progress(m).Where(i => !i.Text.StartsWith("段階")).ToList();
            var chip = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float cx = x;
            int chipRows = items.Count > 0 ? 1 : 0;
            foreach (var it in items)
            {
                string t = (it.Done ? "済 " : "未 ") + it.Text;
                float cw = chip.CalcSize(new GUIContent(t)).x + 12;
                if (cx + cw > x + w) { if (chipRows >= 2) break; chipRows++; cx = x; y += 21; } // 2段まで折り返す（レビュー：札が黙って消えた）
                var cr = new Rect(cx, y, cw, 19);
                DrawRect(cr, it.Done ? new Color(0.08f, 0.3f, 0.12f, 0.9f) : new Color(0.25f, 0.08f, 0.08f, 0.9f));
                DrawFrame(cr, it.Done ? GoalDone : GoalTodo, 1f);
                ShadowLabel(cr, t, chip, it.Done ? GoalDone : GoalTodo);
                cx += cw + 4;
            }
            if (items.Count > 0) y += 22;

            // 倒し方（特別な敵だけ。同じ種類は1行に）
            var seen = new HashSet<string>();
            var hurtStyle = new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            foreach (var e in m.Enemies)
            {
                var how = PuzzleRules.HowToHurt(m, e);
                if (how == null) continue;
                string name = PhaseRules.KindName(e.Kind);
                if (!seen.Add(name)) continue;
                var (direct, copy, gimmick, note) = how.Value;
                // 【レビュー】Soft の部屋は○×の表をやめて1文に（「仕掛け ×」が、弓兵の矢でも割れることと食い違って見えた）
                string line = e.IsObject || m.SoftRules || (e.Kind == EnemyKind.HealClone && !m.SoftRules) || e.Kind == EnemyKind.Mender || e.Kind == EnemyKind.MirrorLord
                    ? $"{name}：{note}"
                    : $"{name}：あなたの直接 {(direct ? "○" : "×")}　まね攻撃 {(copy ? "○" : "×")}　仕掛け {(gimmick ? "○" : "×")}　（{note}）";
                if (y + 18 > r.yMax - 2) break;
                ShadowLabel(new Rect(x, y, w, 19), line, hurtStyle, direct ? new Color(0.85f, 0.88f, 0.95f) : new Color(0.85f, 0.75f, 1f));
                y += 18;
            }

            // 部屋の一言（残りの高さで）
            string text = turn.TurnNumber >= 2 && !string.IsNullOrEmpty(stage.hintAfterFirstTurn) ? stage.hintAfterFirstTurn : stage.tutorialText;
            // 【レビュー】鏡の主は段階ごとの文（段階3でも段階1の文から始まり、肝心の所が切れていた）
            var bossLord = m.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord && e.IsAlive);
            if (bossLord != null && bossLord.BossStage > 0) text = CombatBoss.PhaseHint(bossLord);
            var mission = act != null && act.InBattle ? act.Run?.Current?.Mission : null;
            if (mission != null) text = $"【任意】{mission.Text}（+{mission.Reward}）　" + text;
            if (y < r.yMax - 14 && !string.IsNullOrEmpty(text))
            {
                var body = new GUIStyle(Tiny) { fontSize = 11, wordWrap = true, clipping = TextClipping.Clip };
                body.normal.textColor = new Color(0.78f, 0.8f, 0.88f);
                GUI.Label(new Rect(x, y, w, r.yMax - y - 2), text, body);
            }
        }

        /// <summary>右下：小さな凡例（色とまね方）。全部は上の「凡例」。</summary>
        void DrawMiniLegend(Rect r)
        {
            var rows = IsV4Ui ? new List<(Color c, string t)>
            {
                (new Color(1f, 0.2f, 0.2f), "赤＝敵の攻撃（今ターン）"), // 【RC5・レビュー】欄の幅に収まる短さに（右で切れていた）【RC6】絵：剣・矢・爆発
                (new Color(1f, 0.45f, 0.95f), "紫＝床に固定・★＝大技（敵にも当たる）"), // 【RC6】絵：星
                (new Color(1f, 0.85f, 0.45f), "★の数字＝大技まで何ターン"), // 【RC6】
                (new Color(0.92f, 0.95f, 1f), "白い枠＝札が届く"), // 【RC5】オレンジから白へ（オレンジは敵の動きだけ）
                (new Color(1f, 0.62f, 0.15f), "オレンジの矢印＝敵が動く先"),
                (new Color(0.35f, 0.95f, 1f), "水色＝まねる敵"), // 【RC5・レビュー】まね（核）と緑が凡例に無かった
                (new Color(0.3f, 0.85f, 0.5f), "緑の札＝戦術札"),
                (new Color(0.62f, 0.62f, 0.66f), "炎・岩・光＝床の危険"),
            } : new List<(Color c, string t)>
            {
                (new Color(1f, 0.2f, 0.2f), "赤＝敵の攻撃（今の位置・向き）"),
                (TelegraphMarkColor, "紫＝ボス・仕掛けの予告"),
                (new Color(0.3f, 0.55f, 1f), "青/橙/琥珀枠＝あなたの移動/攻撃/押す引く"),
                (new Color(0.35f, 0.95f, 1f), "水色＝この札をまねる敵と向き"),
            };
            var st = new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            float longest = rows.Max(row => st.CalcSize(new GUIContent(row.t)).x);
            if (longest > r.width - 34) st = Fit(st, rows.OrderByDescending(row => row.t.Length).First().t, r.width - 34, 10); // 欄が狭いときは小さく（切らない）
            // 【RC5】V4 はまね方の行を出さない（名札に「全まね」などの文字がある。720p で「移動まね」が切れていた）→ 色の行を広く
            float rh = (r.height - (IsV4Ui ? 4 : 26)) / rows.Count;
            // 【RC6・ブラインドテスト「凡例に剣・星の絵の意味が無い」】色の四角の右に、マスの札と同じ絵
            var icons = IsV4Ui ? new List<string> { "hz_sword", "hz_big", "hz_clock", null, null, null, null, "hz_flame" } : new List<string>();
            if (IsV4Ui && turn.Grid != null && turn.Grid.Model != null)
            {
                // 【RC6・レビュー「はじめての戦闘でも8行全部」】盤面に無い物（大技・床の危険）の行は出さない
                var bm = turn.Grid.Model;
                bool anyClock = bm.Enemies.Any(e => e.IsAlive && e.Clocks.Count > 0), anyFloor = bm.Environment.HasFire || bm.Board.Spikes.Any();
                bool anyTactic = turn.Tactics != null, enemiesAct = !bm.NoTraits; // 【RC6・レビュー2回目】
                for (int i = rows.Count - 1; i >= 0; i--)
                    if ((!anyClock && (icons[i] == "hz_big" || icons[i] == "hz_clock")) || (!anyFloor && icons[i] == "hz_flame")
                        || (!anyTactic && rows[i].t.StartsWith("緑の札")) || (!enemiesAct && (rows[i].t.StartsWith("赤＝") || rows[i].t.StartsWith("オレンジ"))))
                    { rows.RemoveAt(i); icons.RemoveAt(i); }
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var sw = new Rect(r.x, r.y + 2 + i * rh + (rh - 12) / 2, 12, 12);
                DrawRect(sw, new Color(rows[i].c.r, rows[i].c.g, rows[i].c.b, 0.85f));
                float tx = sw.xMax + 4;
                if (i < icons.Count && icons[i] != null && DrawUiImage(new Rect(tx, sw.y - 1, 14, 14), icons[i], 1f, ScaleMode.ScaleToFit)) tx += 16;
                if (i < icons.Count && icons[i] == "hz_flame" && DrawUiImage(new Rect(tx - 2, sw.y - 1, 14, 14), "hz_emitter", 1f, ScaleMode.ScaleToFit)) tx += 14; // 【RC6・ブラインドテスト2】レーザーの絵も
                GUI.Label(new Rect(tx, r.y + 2 + i * rh, r.xMax - tx, rh), rows[i].t, st);
            }
            if (IsV4Ui) return;
            var kinds = new GUIStyle(Tiny) { fontSize = IsV4Ui ? 11 : 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            float kx = r.x;
            foreach (var k in new[] { CopyKind.All, CopyKind.Technique, CopyKind.Move })
            {
                DrawCopyKindIcon(new Rect(kx, r.yMax - 20, 16, 16), k, CopyKindColor(k));
                string n = CopyKinds.Short(k);
                float nw = kinds.CalcSize(new GUIContent(n)).x;
                ShadowLabel(new Rect(kx + 17, r.yMax - 22, Mathf.Min(nw + 4, r.xMax - kx - 17), 20), n, kinds, CopyKindColor(k));
                kx += 17 + nw + (IsV4Ui ? 3 : 8); // 【RC2】「移動まね」が枠からはみ出していた（【RC3】まだ「移動ま」で切れていた → 10px・間 3）
            }
        }

        /// <summary>凡例（全部）。上の「凡例」で開く。クリックで閉じる。</summary>
        void DrawLegendOverlay(Rect r)
        {
            DrawRect(r, new Color(0.02f, 0.03f, 0.07f, 0.95f));
            DrawFrame(r, Gold, 2f);
            var head = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false };
            ShadowLabel(new Rect(r.x + 14, r.y + 8, r.width - 28, 24), CombatV2 ? "凡例とキー（クリック・L・Esc で閉じる）" : "凡例（クリック・L・Esc で閉じる）", head, Gold);
            var b = turn.Grid.Model.Board;
            var lines = new List<string>
            {
                "まね方（敵の印）",
                "　　 全まね：あなたの札を全部、同じ向き・同じ距離でまねる",
                "　　 技まね：歩かない。技の札（攻撃・押す・引く）だけ、その場からまねる＝砲台",
                "　　 移動まね：移動の札（青）だけまねる。攻撃しない＝駒",
                "言葉",
                "　あなたの直接：あなたが使った札の攻撃。まね攻撃：敵があなたの札をまねて出した攻撃",
                "　仕掛け：トゲ・石像の視線・爆発など（だれの攻撃でもない）",
                "盤面の色",
                "　赤：敵の固定攻撃（今の位置・向きのまま。砲兵の爆撃予告も赤）",
                "　紫：ボス・仕掛けの予告（石像の視線・光柱・大剣・鏡の奔流）",
                "　青／橙／琥珀の枠：あなたの札の範囲（移動／攻撃／押す・引く）",
                "　水色の枠「まねる」と水色の矢印：今選んだ札をまねる敵と、その向き",
                "　金：選んだ方向・命令の後のあなたの位置（予定）",
                "1ターンの順番：あなた → 敵のまね（敵A → 敵B …）→ 敵の固定攻撃（赤）→ 仕掛け（紫）",
                "仕掛け",
            };
            if (CombatV2)
            {
                // 【RC8】新しい盤面の印に合わせて短く（1つの色は1つの意味・形でも分ける）
                lines = new List<string>
                {
                    "まね方（敵のマスの右上の印 全・技・移／上の敵の欄の水色の字）", // 【通常リリース版・画面の直し 27】盤面の印の字も
                    "　　 全まね：あなたの札を全部、同じ向き・同じ距離でまねる",
                    "　　 技まね：攻撃・押す・引くだけ、その場からまねる（移動はまねない）",
                    "　　 移動まね：移動の札（青）だけまねる",
                    "1ターンの順番（上の帯：今の段が光る）",
                    "　①あなたの命令 → ②敵の攻撃（赤：A → B → C）→ ③まね → ④大技（紫・★：部屋全体で1ターンに1つ）→ ⑤敵の移動と次の予告 → ⑥床。1ターンに受けるダメージは 8 まで",
                    "敵の記号 A・B・C（＝動く順番・敵ごとの色）",
                    "　敵のマスの下の札と、攻撃のマスの左上の札が同じ。敵か攻撃のマスにマウスを乗せると、その敵の攻撃だけが光る",
                    "攻撃の種類（形で分ける）",
                    "　赤（点線の縁）＝敵と一緒に動く攻撃（剣・槍・噛みつき・矢）。押す・引くでずれる。倒せば消える",
                    "　紫（斜線）＝床に固定の攻撃（砲兵の爆撃・ボスの光柱・奔流）。押しても動かない。撃つ敵を倒せば消える",
                    "　★の紫＝大技。敵のマスの「★3」と上の欄の「大技 あと3」＝大技まで あと3ターン（撃つターンは当たるマスが紫で出る・まねの後に撃つ・敵にも当たる）。撃つ敵を倒せば消える", // 【通常リリース版・画面の直し 27】
                    "　炎・岩・光線＝床の仕掛け（誰のものでもない・敵にも当たる）。薄い・点線の「次」＝次のターン",
                    "数字",
                    "　マスの左上の札［記号 ダメージ］＝その攻撃1つのダメージ（重なったら自分で足す）。あなたのマスに来る攻撃は、内側が赤い太い枠",
                    AnswerPreview ? "　札を選んで向きにマウスを乗せると：ずれた攻撃は新しい場所（前の場所は点線と「ずれる」）、消える攻撃は「×」と「倒すと消える」" : "　まね・押した結果は前もって出ない（自分で読む）。実行の後、右下の「前のターンに起きたこと」に出る",
                    "ほかの色",
                    "　水色＝あなた（太い枠）と、選んだ札をまねる敵／白い枠＝あなたの札が届く（金＝選んでいる向き）／オレンジの影と矢印＝敵が自分で動く先（移動先）／緑＝戦術札／青＝ワープ",
                    "札", // 【通常リリース版・画面の直し 1・2】札の左上の数字は無くした。手札の上の補足（緑の札・残る札）はここへ
                    "　上＝あなたの効果／水色の欄＝敵のまね。右端の黄色の印＝敵のまねた攻撃が あなたにも当たりうる（向きしだい）",
                    "　緑＝戦術札（Z）：命令の枠を使わない・敵はまねしない。使わなかった命令の札は手札に残る",
                    "キー", // 【通常リリース版・画面の直し 2】画面の下は4つだけ（Enter・BS・L・O）。全部のキーはここ
                    "　1〜9 札・矢印／W A S D 向き・Enter 実行・BS 1つ戻す・Esc 取り消し（何も無ければメニュー）・Z X C 戦術札・Q E アイテム・T 敵の説明・M 音・Tab 詳細・O メニュー・F 早送り", // 【第2巡 10】完成版に手札整理は無い（H を外した）
                };
            }
            else if (IsV4Ui)
            {
                // 【Combat V4】凡例は短く（大技・戦術札・敵の行動）【RC5・レビュー】今のルールに合わせて書き直し（前は「次の敵の行動で発動」・色の一部が古い・枠からはみ出していた）
                lines = new List<string>
                {
                    "まね方（敵の印）",
                    "　　 全まね：あなたの札を全部、同じ向き・同じ距離でまねる",
                    "　　 技まね：攻撃・押す・引くだけ、その場からまねる（移動はまねない）",
                    "　　 移動まね：移動の札（青）だけまねる",
                    "1ターンの順番",
                    "　あなたの命令 → 敵の攻撃（赤・紫）→ 敵のまね（敵A が全部 → 敵B …）→ ★の大技（紫）→ 敵が自分で動く（オレンジの矢印）→ 床の危険", // 【RC11・QA】紫の爆撃・光柱は②
                    "　★まねで敵を送り込んで同じターンに当たるのは★の紫（大技）。赤は まねの前に終わる：まねで赤のマスへ送った敵に当たるのは次のターン（そこがまた赤なら）", // 【RC5・レビュー3】赤と赤紫の違い（いつ当たるか）を並べて書く
                    "盤面の色（1つの色は1つの意味）",
                    "　赤＝敵と動く攻撃／紫＝床に固定の攻撃（砲兵の照準・ボスの光柱・奔流は②で、★の大技は④のまねの後に発動・敵にも当たる）",
                    "　マスの右上の札＝［いつ］［原因］［ダメージ］（例「今 剣 3ダメ」）：今＝このターンに当たる（濃い）／次＝次のターン（薄い・点線）。剣・矢・爆発＝敵の攻撃／★の紫＝大技／光柱＝ボス／炎・岩・光線＝床",
                    "　あなたのマスの赤い枠と「！ 6ダメ」＝組んだ命令で、このターンにあなたが受けるダメージ（命令を置く前に、向きにマウスを乗せても出る）。緑の「無傷」＝その命令なら受けない。「まね返し」＝あなたの命令を敵がまねて、あなたに当たる",
                    "　オレンジの矢印と影＝敵が自分で動く先／水色の枠＝選んだ札をまねる敵／白い枠＝あなたの札が届く（金＝選んでいる向き）／青＝ワープ",
                    "大技（頭の上の数字）",
                    "　大技までのターン数。①で範囲が紫に決まって動かない。まねで敵を送り込めば、敵にも当たる",
                    "床の危険（濃い＝今ターン／薄い＝次のターン・数字＝ダメージ・敵にも当たる）",
                    "　炎と火種／岩と影・ヒビ／太い光と細い線（レーザー）", // 【RC5・レビュー3】砲兵の照準は「敵の攻撃（赤）」の段で爆発する（床の危険ではない）
                    "札",
                    "　緑＝戦術札：命令の枠を使わない・敵はまねしない・手札は1枚（Z）", // 【通常リリース版】札の左上の数字は無くした
                    "　早鐘＝大技を1ターン早める／遅鐘＝遅らせる／照準替え＝大技の狙いを別の敵へ／模倣命令＝このターン、敵全員が全まね",
                    "　札の右上の★＝珍しさ（★が多いほど珍しい。扉の★は難しさ）",
                };
            }
            if (b.Warps.Count > 0) lines.Add("　ワープ：同じ色の2マスが1組（点線）。入ると反対側へ出る（敵も）。出口がふさがっていれば動かない"); // 【RC3】
            if (b.SealStoneHome.Count > 0) lines.Add("　封：封印石。敵の“まね攻撃”か石像の視線でだけ割れる（あなたの攻撃ははじく）");
            if (b.StatueOrder.Count > 0) lines.Add("　像：石像。毎ターンの終わりに向いている列を撃つ（3）。敵の“まね攻撃”が当たると時計回りに回る");
            if (b.SealStoneHome.Count > 0 || b.StatueOrder.Count > 0) lines.Add("　詰み防止：倒せる敵が全滅したら、封印石・石像はあなたの攻撃でも動く（遅い道）");
            if (b.Exits.Count > 0) lines.Add("　出：出口。封印が全部解けたら開く。あなたが入れば勝ち");
            if (b.CarryMarks.Count > 0) lines.Add("　紋：運び紋。全部に敵か運び像を同時に立たせる");
            if (b.CoreHome.Count > 0 && turn.Grid.Model.SoftRules) lines.Add($"　核：鏡核。全部を同じターンに叩く（1つ以上は“まね攻撃”）と共鳴：敵全員に {RulesEngine.ResonanceDamage}・そのターンは動けない。{RulesEngine.CoreRegrowRounds}ターンで戻る");
            else if (b.CoreHome.Count > 0) lines.Add("　核：鏡核。全部を同じターンに叩くと割れる（1つ以上は“まね攻撃”）");
            if (turn.Grid.Model.SoftRules && turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.EchoKnight)) lines.Add($"　反響鎧兵：あなたの技は {RulesEngine.EchoSoftDamage} だけ。まね攻撃・トゲ・爆発・視線・共鳴が当たると鎧が割れて普通の兵に");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.Mender)) lines.Add($"　癒し手：毎ターンの終わりに、いちばん傷ついた仲間を +{PhaseRules.MenderHeal}。技まね：隣の仲間を殴らせられる");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.MirrorLord)) lines.Add($"　鏡の主：段階2・3 は鏡の鎧（あなたの直接攻撃 -{RulesEngine.LordDirectGuard}）／段階2 写し身は鏡（主の攻撃をはね返す・まね攻撃でも -{CombatBoss.CloneStrikeDamage}）／段階3 鏡の奔流（赤）は鏡核の共鳴で止まる");
            if (turn.TidyMax > 0) lines.Add($"　手札整理：1戦1回、手札を {turn.TidyMax} 枚まで捨てて引き直す（命令の枠は使わない）");
            if (b.Seals.Count > 0 && turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.SealLord)) lines.Add("　紋：光る紋。主（敵A）を乗せると成功（1回で消える）");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.BladeStatue)) lines.Add("　剣の像：技まねの道具。段階2で光り、主を斬ると光が消える");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.SealLord)) lines.Add("　巨大石手：あなたのマスを予告（紫）→ 次のターンに落ちる（3・主にも当たる）→ 1ターン休む");
            if (b.Gates.Count > 0) lines.Add("　扉：条件がそろうと開く");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.Golem)) lines.Add("　運び像：移動だけまねる道具。倒れない・数えない");
            if (turn.Grid.Model.Enemies.Any(e => e.Kind == EnemyKind.Cannon)) lines.Add("　写し砲：技だけまねて一直線に撃つ道具。倒れない・数えない");
            lines.Add("　トゲ：入るたびにダメージ（敵も）。壁・柱：通れない（自分だけ止まると敵だけ進む＝壁ズレ）");
            var st = new GUIStyle(Tiny) { fontSize = IsV4Ui ? 12 : 13, wordWrap = true };
            float y = r.y + 36;
            int li = 0;
            foreach (var l in lines)
            {
                bool iconLine = li >= 1 && li <= 3;
                if (iconLine) DrawCopyKindIcon(new Rect(r.x + 14, y + 2, 16, 16), li == 1 ? CopyKind.All : li == 2 ? CopyKind.Technique : CopyKind.Move, CopyKindColor(li == 1 ? CopyKind.All : li == 2 ? CopyKind.Technique : CopyKind.Move));
                float ind = iconLine ? 8f : 0f; // 【RC5・最終ゲート】アイコンが「全まね」の最初の字に重なっていた → 字を少し右へ
                li++;
                bool sec = !l.StartsWith("　");
                var ls = sec ? new GUIStyle(st) { fontStyle = FontStyle.Bold } : st;
                float lh = Mathf.Max(18f, ls.CalcHeight(new GUIContent(l), r.width - 28 - ind)); // 【RC5・レビュー】長い行は折り返す（前は右で切れていた）
                ShadowLabel(new Rect(r.x + 14 + ind, y, r.width - 28 - ind, lh), l, ls, sec ? Gold : new Color(0.9f, 0.92f, 0.97f));
                y += lh + (sec ? 3 : 1);
            }
            if (Event.current.type == EventType.MouseDown) { showLegend = false; Event.current.Use(); }
        }

        /// <summary>はじかれた理由（あなたが間違えた瞬間）。盤面の上に 4 秒。</summary>
        void DrawHintBanner(float bw)
        {
            if (turn.LastHintSerial != shownHintSerial) { shownHintSerial = turn.LastHintSerial; shownHintAt = Time.unscaledTime; shownHint = turn.LastHint; }
            if (string.IsNullOrEmpty(shownHint) || Time.unscaledTime - shownHintAt > 4.5f) return;
            var st = new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float tw = Mathf.Min(bw - 40, st.CalcSize(new GUIContent(shownHint)).x + 40);
            var rr = new Rect(bw * 0.5f - tw * 0.5f, TopH + 6, tw, 30);
            DrawRect(rr, new Color(0.35f, 0.04f, 0.06f, 0.92f));
            DrawFrame(rr, new Color(1f, 0.5f, 0.45f), 2f);
            ShadowLabel(rr, shownHint, st, new Color(1f, 0.85f, 0.8f));
        }

        /// <summary>敵の頭上の短い印（倒し方）。普通の敵は null。</summary>
        static string HurtTag(BattleModel m, UnitModel u)
        {
            if (u.Kind == EnemyKind.Lord) return LordTag(m, u);
            if (u.Kind == EnemyKind.SealLord) return u.Barrier == 0 ? "窓！直接○" : u.BossStage == 0 ? "段階1/3：光る紋へ" : u.BossStage == 1 ? "段階2/3：剣の像" : "段階3/3：核";
            if (u.Kind == EnemyKind.BladeStatue) return u.BossStage > 0 ? "剣の像・済" : u.Dormant ? "剣の像（眠り）" : "剣の像・光";
            if (u.Kind == EnemyKind.MirrorLord) return CombatBoss.Tag(u); // 【戦闘中心 v3】
            if (u.Kind == EnemyKind.HealClone) return u.Dormant ? (m.V4 ? null : "写し身（眠り）") : m.SoftRules ? "主を回復中（倒せる）" : "傷つかない"; // 【RC2】V4 は名札に「眠り」 // 【RC2】「像」が「僕」と読めた・斬らせる仕掛けは V4 では起きない
            if (u.Kind == EnemyKind.Mender) return $"癒し +{PhaseRules.MenderHeal}";
            if (u.StunRounds > 0) return "動けない";
            if (u.Kind == EnemyKind.EchoKnight && m.SoftRules) return u.ArmorBroken ? "鎧が割れた" : $"鎧：直接{RulesEngine.EchoSoftDamage} まね○";
            if (u.Kind == EnemyKind.Cannon) return "写し砲・不死";
            if (u.Kind == EnemyKind.Golem) return "運び像・不死";
            var how = PuzzleRules.HowToHurt(m, u);
            if (how == null) return u.Kind == EnemyKind.Warden && u.Barrier == 0 ? "割れた！写せない" : null;
            var (direct, copy, gimmick, _) = how.Value;
            if (u.Barrier > 0 && u.BarrierRule == 3) return "守り：鏡核";
            if (u.Barrier > 0 && u.BarrierRule == 2) return "守り：円へ";
            return $"直接{(direct ? "○" : "×")} まね{(copy ? "○" : "×")}";
        }

        /// <summary>まね方の印（∞・剣・足あとの絵）。最初の2ターンは名前つき、その後は絵だけ。</summary>
        void DrawCopyKindChip(Rect cell, UnitModel u, GUIStyle tag, bool blockedBelow = false)
        {
            var k = u.Dormant ? CopyKind.None : u.ForceAllCopy ? CopyKind.All : u.CopyKind; // 【RC5】模倣命令のラウンドは全まね（指揮官の最初の札）
            bool named = (turn.TurnNumber <= 2 || u.ForceAllCopy) && cell.width >= 48;
            string t = u.Dormant ? (u.Kind == EnemyKind.HealClone ? "眠り" : "像") : named ? CopyKinds.Short(k) : ""; // 【RC5】眠る写し身は「眠り」
            var col = CopyKindColor(k);
            // 【企画書 v5・UI】名前つきのときは絵を出さず、文字がマスに収まる大きさにする（前は「移動まね」の頭が絵に隠れ、右が切れて「多動まね」に見えた）
            bool icon = t.Length == 0 && !u.Dormant;
            var st = new GUIStyle(tag) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            while (t.Length > 0 && st.fontSize > 9 && st.CalcSize(new GUIContent(t)).x + 6 > cell.width - 2) st.fontSize--;
            float cw = icon ? 18 : st.CalcSize(new GUIContent(t)).x + 6;
            var br = new Rect(cell.center.x - cw / 2f, blockedBelow ? cell.y + cell.height * 0.5f - 4 : cell.yMax - 17, cw, 16);
            DrawRect(br, new Color(col.r * 0.3f, col.g * 0.3f, col.b * 0.3f, 0.92f));
            DrawFrame(br, col, 1.5f);
            if (icon) DrawCopyKindIcon(new Rect(br.x + 2, br.y + 1, 14, 14), k, col);
            if (t.Length > 0) ShadowLabel(br, t, st, col);
        }

        // ───── まね方の絵（絵文字の ⚔・👣 はフォントに無く □ になったので、小さな絵を作る） ─────
        static readonly string[] SwordPix =
        {
            "..........##",
            ".........###",
            "........###.",
            ".......###..",
            "..#...###...",
            "...#.###....",
            "....###.....",
            "....##......",
            "...#..#.....",
            "..#.........",
            ".#..........",
            "#...........",
        };
        static readonly string[] FootPix =
        {
            "......##....",
            ".....####...",
            ".....####...",
            "......##....",
            ".##.........",
            "####...##...",
            "####..####..",
            ".##...####..",
            "...#...##...",
            ".......#....",
            "............",
            "............",
        };
        static readonly string[] InfPix =
        {
            "............",
            "............",
            "............",
            ".##......##.",
            "#..#....#..#",
            "#...#..#...#",
            "#....##....#",
            "#...#..#...#",
            "#..#....#..#",
            ".##......##.",
            "............",
            "............",
        };
        static readonly Dictionary<CopyKind, Texture2D> iconCache = new Dictionary<CopyKind, Texture2D>();

        static Texture2D IconTexture(CopyKind k)
        {
            if (iconCache.TryGetValue(k, out var t) && t != null) return t;
            var pix = k == CopyKind.Technique ? SwordPix : k == CopyKind.Move ? FootPix : InfPix;
            t = new Texture2D(12, 12, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                    t.SetPixel(x, 11 - y, pix[y][x] == '#' ? Color.white : new Color(1f, 1f, 1f, 0f));
            t.Apply();
            iconCache[k] = t;
            return t;
        }

        /// <summary>まね方の絵（色はまね方の色）。</summary>
        public static void DrawCopyKindIcon(Rect r, CopyKind k, Color col)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width, r.height), IconTexture(k), ScaleMode.ScaleToFit);
            GUI.color = col;
            GUI.DrawTexture(r, IconTexture(k), ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        /// <summary>パズルの仕掛けの絵（封印石・石像と視線の向き・出口・運び紋）。</summary>
        void DrawPuzzleMarks(BattleModel m, System.Func<Vector2Int, Rect> cellRect)
        {
            var b = m.Board;
            var big = new GUIStyle(Big) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            var tag = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            foreach (var c in b.SealStones)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.33f, r.center.y - r.height * 0.36f, r.width * 0.66f, r.height * 0.72f);
                DrawRect(inner, new Color(0.3f, 0.25f, 0.45f, 0.95f));
                DrawFrame(inner, new Color(0.85f, 0.7f, 1f), 2f);
                int si = b.SealStoneHome.ToList().IndexOf(c);
                ShadowLabel(inner, "封" + (si >= 0 ? ((char)('A' + si)).ToString() : ""), big, new Color(0.95f, 0.85f, 1f)); // 目的の札と同じ A/B/C（レビュー）
            }
            foreach (var c in b.StatueOrder)
            {
                var r = cellRect(c);
                var inner = new Rect(r.center.x - r.width * 0.34f, r.center.y - r.height * 0.34f, r.width * 0.68f, r.height * 0.68f);
                DrawRect(inner, new Color(0.42f, 0.4f, 0.36f, 0.97f));
                DrawFrame(inner, new Color(1f, 0.6f, 1f), 2f);
                ShadowLabel(inner, "像", big, Color.white);
                var off = b.StatueFacing(c).ToOffset();
                var p = new Vector2(r.center.x + off.x * r.width * 0.4f, r.center.y - off.y * r.height * 0.4f);
                ShadowLabel(new Rect(p.x - 11, p.y - 11, 22, 22), DirGlyph(b.StatueFacing(c)), new GUIStyle(Big) { fontSize = 18, alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.55f, 1f));
            }
            bool open = PuzzleRules.ExitOpen(b);
            foreach (var c in b.Exits)
            {
                var r = cellRect(c);
                DrawRect(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), open ? new Color(0.2f, 0.75f, 0.4f, 0.55f) : new Color(0.25f, 0.25f, 0.3f, 0.55f));
                DrawFrame(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), open ? GoalDone : new Color(0.6f, 0.6f, 0.7f), 2f);
                ShadowLabel(r, open ? "出口" : "出口\n(閉)", new GUIStyle(big) { fontSize = 13 }, open ? GoalDone : new Color(0.75f, 0.75f, 0.8f));
            }
            foreach (var c in b.CarryMarks)
            {
                var r = cellRect(c);
                var u = m.GetUnitAt(c);
                bool filled = u != null && !u.IsPlayer;
                DrawFrame(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), filled ? GoalDone : new Color(1f, 0.85f, 0.4f, 0.95f), 3f);
                ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 16), "運び紋", tag, filled ? GoalDone : new Color(1f, 0.85f, 0.4f));
            }
        }
    }
}
