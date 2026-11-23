using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【RC8・Combat UI V2（2026-11-08 本人の RC7 実プレイのあと）】戦闘画面の作り直し（完成版の V4 の戦闘）。docs/final/COMBAT_UI_V2.md。
    /// 本人：「どの攻撃がどの敵か分からない」「固定の攻撃と、敵を動かすとずれる攻撃を分けて」「動く順番」「自分がどこか分からない」「ごちゃごちゃ」。
    ///   ・敵の記号 A・B・C（＝動く順番）と色。攻撃のマスにも同じ記号の札。敵 ↔ 攻撃のマスはマウスで互いに光る
    ///   ・赤の点線＝敵と一緒に動く（押す・引く・倒すでずれる・消える）／◎＝床に決まった攻撃（押しても動かない・倒せば消える）／赤紫＝大技（床に決まる）
    ///   ・札を選んで向きにマウスを乗せると、ずれた後の攻撃（前の場所は点線）と、消える攻撃（×）を盤面に出す。あなたが受ける分は「！」
    ///   ・実行中も危険は見えたまま：敵と一緒に滑り、倒れたら消え、済んだ段から消える。上の順番の帯で今の段が光る
    ///   ・あなた：水色の太い枠・頭上の「▼あなた」。あなたのマスの真ん中には何も重ねない
    ///   ・配置：盤面を大きく（画面の 58%）。上＝あなたと敵（動く順番）・順番の帯。下＝目的の1行。凡例・長い説明はボタンの奥
    /// </summary>
    public partial class BattleUI
    {
        // ───────── 【RC9】本編は「答え」を出さない・「ルール」ははっきり（2026-11-09 本人の判断「親切すぎる。自分で考える戦術ゲームの面白さを削る」） ─────────
        //   本編：攻撃元（A・B・C）・固定／追従（実線／点線）・攻撃1つのダメージ・いつ（②④⑥）・順番・大技まで何ターン、は出す。
        //         ずれた後の攻撃・前の場所・「倒すと消える」・あなたが受ける合計（！N・まね返し）・「無傷」・敵どうしの結果、は出さない。
        //   チュートリアル（と5分体験のはじめ）：RC8 の予告を全部出す（Before → 操作 → After を見せて覚える）。
        //   実行の後：何で何ダメージ受けたか（原因ごと）と、敵どうしで何が起きたかは、はっきり出す（右下の「前のターン」）。

        /// <summary>撮影・比べる用：本編でも結果の予告を出す（チュートリアルと同じ）。</summary>
        public static bool DebugAnswerPreview;
        /// <summary>【RC9】結果の予告を出すか（チュートリアル・5分体験のはじめだけ）。</summary>
        internal bool AnswerPreview => DebugAnswerPreview || (act != null && act.AnswerPreviewOn);
        /// <summary>【RC9】「固定」の文字：0＝出さない（実線だけ）／1＝はじめのうちだけ（各ランの1〜3階・チュートリアル）／2＝いつも。どれも攻撃1つに1つだけ（RC8 はマスごと）。撮影の -capturefixed。</summary>
        public static int FixedLabelMode = 0; // 【RC9】1回目「固定の表示が見当たらない」→ 2（攻撃1つに1つ）→ 2回目「固定が一部のマスにしか無い（字の無い斜線マスは動く？）」→ 文字はやめ、斜線をすべての固定のマスに・盤面の下にいつも凡例
        bool ShowFixedLabel => FixedLabelMode == 2 || (FixedLabelMode == 1 && (act == null || act.InTutorial || act.Run == null || act.Run.Floor <= 2));

        // ───────── 配置 ─────────

        /// <summary>配置の案（撮影で比べる）：0＝A（RC7 の配置のまま・新しい盤面の印）／1＝B（盤面を大きく：採用）。撮影の -capturelayout。</summary>
        public static int LayoutVariant = 1;
        /// <summary>【通常リリース版・ポートフォリオのレビュー】撮影用の「きれいな画面」：ヒントの帯と下のキーの行だけを隠す（ゲームの中身・盤面は変えない）。撮影の -captureclean 1。</summary>
        public static bool CleanCapture;
        /// <summary>新しい盤面の印を使う戦闘（完成版の V4 の戦闘・チュートリアル）。</summary>
        bool CombatV2 => IsV4Ui && turn.Grid != null && turn.Grid.Model != null && turn.Grid.Model.V4 && PhaseRules.IntentOrder;
        /// <summary>新しい配置（B）。</summary>
        bool V2Layout => CombatV2 && LayoutVariant == 1;
        internal const float V2TopH = 92f;
        const float V2BoardFrac = 0.58f;
        float V2BottomH => InTutorialUi ? 124f : 30f; // 【RC8】チュートリアルの説明の3行目が切れていた
        /// <summary>【RC8】ACT1 のヒントを、右下の欄に自分で描く（盤面の下の段を隠さない）。</summary>
        public bool DrawsHintsItself => V2Layout && act != null && act.InBattle;

        /// <summary>【第2巡 1】基本の説明のヒント（ボス戦では出さない）：本編の読み方・まね・赤・まね方・大技・戦術札。</summary>
        static readonly HashSet<string> BasicHints = new HashSet<string> { "V0", "V1", "V2", "V3", "V5", "V6", "V7", "V8", "V9" };
        /// <summary>【第2巡 1】入力中に右下に出したヒント（実行したら、× と同じく閉じる：一度閉じたら出ない）。</summary>
        string shownHintKey;

        /// <summary>【第2巡 1】ヒントは最初の実行で閉じる（読んだ・読まなかったに関わらず、同じヒントを毎回の戦闘に出さない）。</summary>
        void DismissShownHintOnExecute()
        {
            if (shownHintKey == null || act == null) return;
            if (turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) { act.DismissHint(shownHintKey); shownHintKey = null; }
            else if (!act.InBattle) shownHintKey = null;
        }

        /// <summary>【RC8】右下の欄の上：ACT1 のヒント（緑の枠・× で閉じる）。描いた高さを返す。</summary>
        float DrawHintInDetail(Rect r)
        {
            if (act == null || !act.InBattle || CleanCapture) return 0f; // 【通常リリース版】撮影の「きれいな画面」ではヒントを出さない
            var hint = act.CurrentHint();
            if (hint == null) return 0f;
            // 【第2巡 1「本編では、まね・敵の攻撃の結果は…が毎回の戦闘に出ている」】ボス戦では基本の説明のヒントを出さない
            if (BasicHints.Contains(hint.Value.Key) && act.Run != null && act.Run.Current != null && act.Run.Current.Kind == RoomKind.Boss) return 0f;
            shownHintKey = hint.Value.Key; // 実行したら閉じる（DismissShownHintOnExecute）
            var st = new GUIStyle(Small) { fontSize = 12, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft };
            string text = WrapJa("ヒント：" + hint.Value.Value, st, r.width - 46);
            float h = Mathf.Min(r.height, st.CalcHeight(new GUIContent(text), r.width - 46) + 10f);
            var box = new Rect(r.x, r.y, r.width, h);
            DrawRect(box, new Color(0.05f, 0.14f, 0.1f, 0.96f));
            DrawFrame(box, new Color(0.45f, 0.95f, 0.7f, 0.9f), 1.5f);
            ShadowLabel(new Rect(box.x + 8, box.y + 5, box.width - 46, box.height - 6), text, st, new Color(0.75f, 1f, 0.85f));
            var x = new Rect(box.xMax - 32, box.y + 4, 28, 22);
            DrawRect(x, new Color(1f, 1f, 1f, x.Contains(Event.current.mousePosition) ? 0.25f : 0.12f));
            ShadowLabel(x, "×", new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, Color.white);
            if (GUI.Button(x, GUIContent.none, GUIStyle.none)) act.DismissHint(hint.Value.Key);
            return h + 4f;
        }

        /// <summary>
        /// 【RC9・本人「未来の答えは出しすぎない／過去に何が起きたかは明確に」】右下の欄の上：前のターンに起きたこと。
        /// 「あなた -7：剣兵A（赤）2・大技 3・落石 2」と、敵どうし（同士討ち・まね・仕掛け）・倒した敵。何も無ければ出さない。描いた高さを返す。
        /// </summary>
        float DrawLastTurnInDetail(Rect r)
        {
            if (turn.State != BattleState.PlayerInput || turn.TurnNumber <= 1) return 0f;
            var lines = LastTurnLines();
            if (lines.Count == 0) return 0f;
            var st = new GUIStyle(Small) { fontSize = 12, wordWrap = true, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft, richText = true };
            float tw = r.width - 20f;
            var hs = lines.Select(l => st.CalcHeight(new GUIContent(l), tw)).ToList(); // 【RC9】長い行（敵どうし）は折り返す（右が切れていた）
            float h = 22f + hs.Sum() + 4f;
            var box = new Rect(r.x, r.y, r.width, Mathf.Min(h, r.height));
            DrawRect(box, new Color(0.1f, 0.05f, 0.06f, 0.96f));
            DrawFrame(box, new Color(1f, 0.6f, 0.5f, 0.8f), 1.5f);
            ShadowLabel(new Rect(box.x + 8, box.y + 3, box.width - 16, 18), "前のターンに起きたこと", new GUIStyle(st) { fontStyle = FontStyle.Bold, wordWrap = false }, new Color(1f, 0.8f, 0.7f));
            float y = box.y + 22f;
            for (int i = 0; i < lines.Count; i++)
            {
                if (y + hs[i] > box.yMax + 2f) break; // 【RC9】欄からはみ出す行は描かない（下のヒントに重なっていた）
                GUI.Label(new Rect(box.x + 10, y, tw, hs[i]), lines[i], new GUIStyle(st) { normal = { textColor = new Color(0.95f, 0.95f, 1f) } });
                y += hs[i];
            }
            return box.height + 4f;
        }

        /// <summary>前のターンの出来事（あなたの被ダメ・敵どうし・倒した敵）。最大4行。</summary>
        List<string> LastTurnLines()
        {
            var ev = turn.BattleEvents;
            var lines = new List<string>();
            if (ev == null) return lines;
            int s = Mathf.Clamp(turn.TurnEventStart, 0, ev.Count);
            var dmg = new List<BattleEvent>();
            for (int i = s; i < ev.Count; i++) if (ev[i].Type == BattleEventType.Damaged && ev[i].Unit != null && ev[i].Amount > 0) dmg.Add(ev[i]);
            var mine = dmg.Where(e => e.Unit.IsPlayer).ToList();
            if (mine.Count > 0)
                lines.Add($"<color=#ff8a7a><b>あなた -{mine.Sum(e => e.Amount)}</b></color>：" + string.Join("・", mine.GroupBy(CauseTextUi).Select(g => $"{g.Key} {g.Sum(e => e.Amount)}")));
            else if (dmg.Count > 0) lines.Add("<color=#8fe0a0><b>あなたは無傷</b></color>");
            // 【RC11】1ターンの上限（8）であふれた分を防いだ
            int capped = 0;
            for (int i = s; i < ev.Count; i++)
                if (ev[i].Type == BattleEventType.Damaged && ev[i].Unit != null && ev[i].Unit.IsPlayer && ev[i].Text != null && ev[i].Text.StartsWith("1ターンの上限"))
                { var m2 = System.Text.RegularExpressions.Regex.Match(ev[i].Text, @"：(\d+) 防いだ"); if (m2.Success) capped += int.Parse(m2.Groups[1].Value); }
            if (capped > 0) lines.Add($"<color=#8fe0a0><b>上限</b></color>：1ターンに受けるのは {RulesEngine.PlayerTurnCap} まで（{capped} 防いだ）");
            // 【RC9・初見テスト2回目「自分の与えたダメージがログに無い」】あなたの攻撃
            var mineHit = dmg.Where(e => !e.Unit.IsPlayer && e.Cause != null && e.Cause.Source != null && e.Cause.Source.IsPlayer && !e.Cause.ByCopy).ToList();
            if (mineHit.Count > 0) lines.Add("<color=#ffe08a><b>あなたの攻撃</b></color>：" + string.Join("・", mineHit.GroupBy(e => e.Unit.Id).Select(g => $"{Tag(g.First().Unit)} {g.Sum(e => e.Amount)}")));
            // 敵どうし：だれの何が、だれに（あなたの直接の攻撃は除く）
            var friendly = dmg.Where(e => !e.Unit.IsPlayer && e.Cause != null && !(e.Cause.Source != null && e.Cause.Source.IsPlayer && !e.Cause.ByCopy)).ToList();
            // 【RC11・最後の確認（QA）「写し身C→鏡の主A 6 は写し身が殴ったように読める」】はね返りは別の行に
            var reflected = friendly.Where(e => e.Text == "反射").ToList();
            friendly = friendly.Where(e => e.Text != "反射").ToList();
            if (reflected.Count > 0) lines.Add("<color=#e0b0ff><b>反射</b></color>：" + string.Join("・", reflected.Select(e => $"{(e.Cause.Source != null ? Tag(e.Cause.Source) : "写し身")}ではね返り → {Tag(e.Unit)} {e.Amount}")));
            if (friendly.Count > 0)
            {
                var parts = friendly.GroupBy(e => (CauseTextUi(e), e.Unit.Id)).Select(g => $"{g.Key.Item1}→{Tag(g.First().Unit)} {g.Sum(e => e.Amount)}").ToList();
                lines.Add("<color=#9fe6ff><b>敵どうし</b></color>：" + string.Join("・", parts.Take(4)) + (parts.Count > 4 ? " …" : ""));
            }
            // 【RC10・初見テスト2回目「主に 6 なのに HP が 4 しか減っていない」】敵の回復
            var heals = new List<BattleEvent>();
            for (int i = s; i < ev.Count; i++) if (ev[i].Type == BattleEventType.Healed && ev[i].Unit != null && !ev[i].Unit.IsPlayer && ev[i].Amount > 0) heals.Add(ev[i]);
            if (heals.Count > 0) lines.Add("<color=#8fe0a0><b>回復</b></color>：" + string.Join("・", heals.GroupBy(e => e.Unit.Id).Select(g => $"{Tag(g.First().Unit)} +{g.Sum(e => e.Amount)}")));
            // 【RC10・初見テスト2回目（3人）「次のターンに敵が1マス動いている理由が分からない」】⑤で敵が自分で動いた分
            var own = turn.OwnMoveLog.Where(e => e.Unit != null && !e.Unit.IsPlayer).ToList();
            if (own.Count > 0)
            {
                var parts = own.GroupBy(e => e.Unit.Id).Select(g => (u: g.First().Unit, d: g.Last().To - g.First().From)).Where(q => q.d != Vector2Int.zero)
                    .Select(q => $"{Tag(q.u)} {MoveGlyphs(q.d)}").ToList();
                if (parts.Count > 0) lines.Add("<color=#ffb070><b>⑤移動</b></color>：" + string.Join("・", parts.Take(4)) + (parts.Count > 4 ? " …" : ""));
            }
            var died = new List<string>();
            for (int i = s; i < ev.Count; i++) if (ev[i].Type == BattleEventType.Died && ev[i].Unit != null && !ev[i].Unit.IsPlayer && !ev[i].Unit.IsObject) died.Add(Tag(ev[i].Unit));
            if (died.Count > 0) lines.Add("<color=#ffd27a><b>倒れた</b></color>：" + string.Join("・", died.Distinct()) + "（その敵の攻撃は消えた）");
            return lines;
        }

        static string Tag(UnitModel u) => u == null ? "?" : PhaseRules.ShortName(u);

        /// <summary>【RC10】動いた向きの矢印（盤面の +y＝画面の上）。2マスなら「↓↓」。</summary>
        static string MoveGlyphs(Vector2Int d)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(3, Mathf.Abs(d.x)); i++) sb.Append(d.x > 0 ? "→" : "←");
            for (int i = 0; i < Mathf.Min(3, Mathf.Abs(d.y)); i++) sb.Append(d.y > 0 ? "↑" : "↓");
            return sb.ToString();
        }

        /// <summary>原因の呼び名（床の危険は 炎・落石・光線 の名前で）。</summary>
        string CauseTextUi(BattleEvent e)
        {
            if (e.Cause != null && e.Cause.Kind == DamageKind.Fire) return FloorName();
            return TurnForecast.CauseText(e);
        }

        /// <summary>盤面の上の高さ（今の配置）。</summary>
        internal float TopH => V2Layout ? V2TopH : ShowTopH;
        /// <summary>盤面の幅（画面の割合・今の配置）。ActUI のヒントの帯も合わせる。</summary>
        public float BoardFracNow => V2Layout ? V2BoardFrac : ShowBoardFrac;

        // ───────── 敵の記号と色（A＝黄・B＝白・C＝桃・D＝砂・E＝ミント・F＝橙：【RC11】B と D を入れ替えた・【通常リリース版・QA】D の空色は「水色＝あなた・まねる敵」と、ミントは緑の札とぶつかるので砂色に（第2巡の QA）） ─────────

        static readonly Color[] EnemyIdColors =
        {
            new Color(1f, 0.83f, 0.27f), new Color(0.93f, 0.93f, 0.96f), new Color(1f, 0.6f, 0.74f), // 【RC11・UI/UX 2回「B の水色が、あなた・まねる敵の水色とぶつかる」】B＝白・D＝空（D はボスの部屋だけ）
            new Color(0.88f, 0.74f, 0.54f), new Color(0.5f, 0.95f, 0.62f), new Color(1f, 0.68f, 0.32f), // 【RC9・3回目「A の黄と D の砂色が似ている」】D＝白・E＝砂色 // 【RC9・初見テスト「緑が敵Dと緑の札の両方」→ 黄緑をやめ、2回目「薄紫が大技の赤紫に近い」→ 砂色
        };

        /// <summary>【RC8・本人「この敵はこの色、と対応させて」】敵の色（記号の札・HP の帯・盤面の敵の足もと）。攻撃のマスの塗り（赤・赤紫）は意味の色のまま。</summary>
        internal static Color EnemyIdColor(UnitModel u)
        {
            var l = u != null ? u.KindLetter : null;
            int i = string.IsNullOrEmpty(l) ? 0 : l[0] - 'A';
            int n = EnemyIdColors.Length;
            return EnemyIdColors[((i % n) + n) % n];
        }

        /// <summary>敵の記号の札（色の四角に黒い文字）。</summary>
        internal static void DrawLetterBadge(Rect r, UnitModel u, float alpha = 1f, bool muted = false)
        {
            if (u == null || string.IsNullOrEmpty(u.KindLetter)) return;
            var c = muted ? new Color(0.46f, 0.48f, 0.54f) : EnemyIdColor(u); // 【通常リリース版・画面の直し 3】順番の帯の今でない段は灰色
            DrawRect(r, new Color(c.r, c.g, c.b, alpha));
            DrawFrame(r, new Color(0f, 0f, 0f, 0.85f * alpha), 1f);
            GUI.Label(r, u.KindLetter, new GUIStyle(Tiny) { fontSize = Mathf.Max(9, Mathf.RoundToInt(r.height * 0.78f)), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.04f, 0.04f, 0.07f, alpha) } });
        }

        // ───────── 危険の一覧（Update で作る・OnGUI と盤面の塗りで同じものを使う） ─────────

        List<Threat> threatsBase = new List<Threat>(), threatsShown = new List<Threat>();
        List<Threats.Change> threatChanges = new List<Threats.Change>();
        string threatKeyV2;
        /// <summary>マウスを乗せている敵（記号の札・盤面の敵・攻撃のマス）。-1＝なし。前のフレームの分は盤面の塗りに使う。</summary>
        int hoverEnemyId = -1, hoverEnemyIdLast = -1;
        /// <summary>マウスを乗せている順番の帯の段（-1＝なし）。</summary>
        int hoverStep = -1, hoverStepLast = -1;
        /// <summary>撮影だけ：マウスを乗せたことにする敵・段（-1＝なし）。</summary>
        public int DebugHoverEnemyId = -1, DebugHoverStep = -1;

        /// <summary>今、盤面に出す危険（入力中＝組んだ命令＋試している向きのあと／実行中＝今の盤面から、済んだ段を除く）。</summary>
        void ComputeThreatsV2()
        {
            var model = turn.Grid.Model;
            if (turn.State == BattleState.PlayerInput)
            {
                // 【RC9】本編は、命令を組んでも危険を作り直さない（今の盤面の危険＝ルール。ずれた後は遊ぶ人が読む）
                var plan = AnswerPreview ? turn.PlannedCommands.ToList() : new List<CommandRecord>();
                if (AnswerPreview && previewRecord != null) plan.Add(previewRecord);
                var key = new System.Text.StringBuilder();
                key.Append(turn.BattleSerial).Append('|').Append(turn.TurnNumber).Append('|').Append(model.Environment.Round).Append('|').Append(ForecastSerial).Append('|');
                foreach (var r in plan) key.Append(r.Command != null ? r.Command.name : "-").Append(r.Direction).Append(r.Distance).Append(r.SourceCard != null ? r.SourceCard.Forge : 0).Append(',');
                foreach (var u in model.Units) key.Append(u.Position.x).Append(',').Append(u.Position.y).Append(',').Append(u.Hp).Append(u.Facing).Append(u.StunRounds).Append(u.Silenced ? 'S' : '-').Append(u.Dormant ? 'D' : '-').Append(';');
                foreach (var e in model.Enemies) { key.Append(e.BombCells.Count).Append(e.SlamCd); foreach (var c in e.Clocks) key.Append(c.Cd).Append(c.Cells.Count); }
                string k = key.ToString();
                if (k == threatKeyV2) return;
                threatKeyV2 = k;
                threatsBase = Threats.Compute(model);
                threatsShown = plan.Count > 0 ? Threats.Compute(PlayerPreview.AfterPlanned(model, plan, turn.PlayerAttackBonusEachTurn)) : threatsBase;
                if (FestivalUi) { threatsBase = threatsBase.Where(t => t.Timing != ThreatTiming.NextFloor).ToList(); threatsShown = threatsShown.Where(t => t.Timing != ThreatTiming.NextFloor).ToList(); } // 【文化祭・初見レビュー「今4 次4 の意味が分からない」】岩は毎ターン同じ所：「次」は出さない
                threatChanges = plan.Count > 0 ? Threats.Diff(threatsBase, threatsShown) : new List<Threats.Change>();
                return;
            }
            threatKeyV2 = null;
            threatChanges = new List<Threats.Change>();
            if (turn.State != BattleState.PlayerExecuting && turn.State != BattleState.EnemyExecuting) { threatsBase = new List<Threat>(); threatsShown = threatsBase; return; }
            // 実行中：今の盤面から（押す・引くで一緒に動く・倒れたら消える）。済んだ段の危険は消す
            var step = turn.Step;
            var order = model.Enemies.ToList();
            int acting = step == TurnManager.TurnStep.EnemyAttack && turn.ActingEnemy != null ? order.IndexOf(turn.ActingEnemy.Model) : -1;
            threatsBase = Threats.Compute(model);
            threatsShown = threatsBase.Where(t =>
                t.Timing == ThreatTiming.Attack ? step < TurnManager.TurnStep.EnemyAttack || (step == TurnManager.TurnStep.EnemyAttack && (acting < 0 || order.IndexOf(t.Source) >= acting))
                : t.Timing == ThreatTiming.BigAttack ? step <= TurnManager.TurnStep.BigAttack
                : t.Timing == ThreatTiming.Floor ? step < TurnManager.TurnStep.Floor
                : false).ToList();
        }

        /// <summary>盤面の塗り（敵の絵の下）：赤＝②敵の攻撃・赤紫＝④大技。マウスを乗せた敵の分以外は薄く。実行中は、敵と一緒に滑る（敵の絵の位置に合わせる）。</summary>
        void UpdateThreatMarksV2()
        {
            var grid = turn.Grid;
            var model = grid.Model;
            ComputeThreatsV2();
            BattlePresenter.FloorHazardName = FloorName(); // 【RC9】
            bool live = turn.State != BattleState.PlayerInput;
            int focus = hoverEnemyIdLast;
            var red = new List<Vector3>(); var redDim = new List<Vector3>(); var mag = new List<Vector3>(); var magDim = new List<Vector3>(); var gone = new List<Vector3>();
            foreach (var t in threatsShown)
            {
                if (t.Timing == ThreatTiming.Floor || t.Timing == ThreatTiming.NextFloor) continue;
                bool dim = (focus >= 0 && t.SourceId != focus) || (hoverStepLast >= 0 && !StepMatches(hoverStepLast, t));
                Vector3 off = Vector3.zero;
                if (live && t.Kind == ThreatKind.Follow && t.Source != null)
                {
                    var v = grid.ViewOf(t.Source);
                    if (v != null) { off = v.transform.position - grid.GridToWorld(t.Source.Position); off.z = 0f; }
                }
                var list = t.Kind == ThreatKind.Fixed ? (dim ? magDim : mag) : (dim ? redDim : red); // IsPurple と同じ // 【RC11・本人「赤は敵と動く・赤紫は固定、とやっと分かった」】色＝種類（赤＝敵と動く／紫＝床に固定：砲兵の爆撃・ボスの光柱・大技）。前は色＝いつ（赤＝②・赤紫＝④）で、固定の赤があった
                foreach (var c in t.Cells) list.Add(grid.GridToWorld(c) + off);
            }
            foreach (var ch in threatChanges)
                if (ch.Gone && ch.Before.Kind == ThreatKind.Follow) foreach (var c in ch.Before.Cells) gone.Add(grid.GridToWorld(c));
            grid.ShowMarksAt("arrow", red, ArrowMarkColor);
            grid.ShowMarksAt("arrowDim", redDim, new Color(ArrowMarkColor.r, ArrowMarkColor.g, ArrowMarkColor.b, ArrowMarkColor.a * 0.35f));
            grid.ShowMarksAt("arrowGone", gone, new Color(ArrowMarkColor.r, ArrowMarkColor.g, ArrowMarkColor.b, ArrowMarkColor.a * 0.28f));
            grid.ShowMarksAt("telegraph", mag, TelegraphMarkColor, 0.86f);
            grid.ShowMarksAt("telegraphDim", magDim, new Color(TelegraphMarkColor.r, TelegraphMarkColor.g, TelegraphMarkColor.b, TelegraphMarkColor.a * 0.35f), 0.86f);
            var none = new List<Vector2Int>();
            grid.ShowMarks("bomb", none, BombMarkColor); grid.ShowMarks("bombNext", none, BombNextMarkColor); grid.ShowMarks("arrowSim", none, SimMarkColor);
            grid.ShowMarks("wave", none, BombNextMarkColor); grid.ShowMarks("step", none, StepMarkColor);
            grid.ShowMarks("fire", none, FireMarkColor); grid.ShowMarks("fireNext", none, FireNextMarkColor);
            grid.ShowMarks("rune", none, RuneMarkColor); grid.ShowMarks("runeNext", none, RuneNextMarkColor);
            grid.ShowMarks("heal", ShowingBattle ? model.Environment.HealTiles.ToList() : none, RuneMarkColor, 0.62f);
        }

        /// <summary>【RC11】紫で描く危険（床に固定：砲兵の爆撃・ボスの光柱・大技）。赤は敵と動く攻撃。</summary>
        public static bool IsPurple(Threat t) => t != null && t.Kind == ThreatKind.Fixed;

        /// <summary>順番の帯の段（②＝1・④＝3・⑥＝5）とその危険。</summary>
        static bool StepMatches(int step, Threat t) =>
            step == 1 ? t.Timing == ThreatTiming.Attack : step == 3 ? t.Timing == ThreatTiming.BigAttack : step == 5 ? t.Timing == ThreatTiming.Floor || t.Timing == ThreatTiming.NextFloor : false;

        // ───────── 上：あなたと敵（動く順番）・順番の帯 ─────────

        void DrawV2Top(float bw)
        {
            var model = turn.Grid.Model;
            float y = 4, h = 42;
            // あなた
            var p = turn.Player;
            var pr = new Rect(8, y, 168, h);
            DrawUi(pr, "panel", PanelBlue);
            var face = new Rect(pr.x + 4, pr.y + 4, h - 8, h - 8);
            if (!DrawUiImage(face, "portrait_player", 1f)) DrawRect(face, new Color(0.3f, 0.6f, 1f));
            float tx = face.xMax + 6, tw = pr.xMax - tx - 6;
            ShadowLabel(new Rect(tx, pr.y + 2, tw, 18), "あなた", new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false }, PlayerTextColor);
            ShadowLabel(new Rect(tx, pr.y + 2, tw, 18), $"{p.Hp}/{p.MaxHp}", new GUIStyle(Small) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.UpperRight }, Color.white);
            var bar = new Rect(tx, pr.y + 23, tw, 11);
            DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));
            DrawRect(new Rect(bar.x, bar.y, bar.width * p.Hp / Mathf.Max(1, p.MaxHp), bar.height), new Color(0.3f, 0.62f, 1f));
            DrawFrame(bar, new Color(1f, 1f, 1f, 0.3f), 1f);
            // 敵：動く順番に（A → B → C）。眠っている像は出さない
            var enemies = turn.Enemies.Where(e => !e.Model.IsObject && !(e.Model.Kind == EnemyKind.HealClone && (e.Model.Dormant || !e.IsAlive))).ToList(); // 眠っている像・主に還った（倒した）写し身は出さない
            float x = pr.xMax + 10, avail = bw - x - 8 - (turn.Enemies.Any(e => e.IsAlive && e.Model.Kind == EnemyKind.HealClone && e.Model.Dormant) ? 70f : 0f);
            int n = Mathf.Max(1, enemies.Count);
            const float gap = 14f;
            float cw = Mathf.Min(196f, (avail - gap * (n - 1)) / n);
            bool exec = turn.State == BattleState.EnemyExecuting || turn.State == BattleState.PlayerExecuting;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                var r = new Rect(x + i * (cw + gap), y, cw, h);
                if (i > 0) ShadowLabel(new Rect(r.x - gap, r.y, gap, h), "›", new GUIStyle(Big) { fontSize = 20, alignment = TextAnchor.MiddleCenter }, new Color(1f, 1f, 1f, 0.55f));
                bool alive = e.IsAlive;
                bool hov = alive && r.Contains(Event.current.mousePosition);
                if (hov)
                {
                    hoverEnemyId = e.Model.Id;
                    cardTipTitle = $"{PhaseRules.ShortName(e.Model)}　HP {e.Hp}/{e.MaxHp}　{CopyKindPlain(e.Model.CopyKind)}";
                    cardTipBody = EnemyHelpNow(e.Model) + (e.Model.Clocks.Count > 0 ? "\n大技：" + string.Join("／", e.Model.Clocks.Select(c => $"{c.Name}（{(c.Cd == 1 && c.Telegraphed ? "このターン" : $"あと{c.Cd}")}）")) : "") +
                                  (RulesEngine.IsLastStanding(turn.Grid.Model, e.Model) && turn.Grid.Model.LastStandDouble ? "\n最後の1体×2：最後に残った1体は、受けるダメージが2倍（ボス・中ボスの本体は除く）。" : "");
                }
                bool lit = hoverEnemyIdLast == e.Model.Id || selectedEnemy == e;
                DrawUi(r, "panel", PanelBlue, alive ? 1f : 0.4f);
                var idc = EnemyIdColor(e.Model);
                DrawRect(new Rect(r.x, r.y, 4, r.height), new Color(idc.r, idc.g, idc.b, alive ? 1f : 0.4f));
                if (lit) DrawFrame(r, idc, 2f);
                if (exec && turn.ActingEnemy == e) DrawFrame(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Gold, 2f);
                DrawLetterBadge(new Rect(r.x + 7, r.y + 4, 17, 17), e.Model, alive ? 1f : 0.45f);
                var fr = new Rect(r.x + 7, r.y + 22, 17, 17);
                DrawUiImage(fr, "portrait_" + KindKey(e.Model), alive ? 1f : 0.3f);
                float ex = r.x + 28, ew = r.xMax - ex - 4;
                bool clock = alive && e.Model.Clocks.Count > 0;
                float chipW = clock ? 36f : 0f;
                string nm = V4Name(e.Model);
                var nameSt = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow }; // 【RC11】Clip だと「主」の上が切れて「鏡のキ」に見えた（幅は Fit で収める）
                ShadowLabel(new Rect(ex, r.y + 1, ew - chipW - 2, 18), nm, Fit(nameSt, nm, ew - chipW - 30, 9), alive ? (IsBossKind(e.Model.Kind) ? Gold : Color.white) : new Color(1f, 1f, 1f, 0.5f)); // 【通常リリース版・画面の直し 23】ボス・中ボスの本体は金の名前（盤面の冠と同じ色）
                if (alive)
                {
                    ShadowLabel(new Rect(ex, r.y + 1, ew - chipW - 2, 18), e.Hp.ToString(), new GUIStyle(nameSt) { alignment = TextAnchor.UpperRight }, Color.white);
                    var hb = new Rect(ex, r.y + 19, ew - chipW - 2, 6);
                    DrawRect(hb, new Color(0f, 0f, 0f, 0.6f));
                    DrawRect(new Rect(hb.x, hb.y, hb.width * e.Hp / Mathf.Max(1, e.MaxHp), hb.height), idc);
                    // 【通常リリース版・画面の直し 10「720p で 8px（全まね（全部））」】短い言葉だけを大きく（かっこの説明は凡例 L・マウスの説明へ）
                    bool lastTwo = RulesEngine.IsLastStanding(turn.Grid.Model, e.Model) && turn.Grid.Model.LastStandDouble;
                    string sub = CopyCore.Stunned(e.Model) ? "今はまねない" : e.Model.Kind == EnemyKind.MirrorLord ? $"段階{e.Model.BossStage + 1}/3・{CopyKinds.Short(e.Model.CopyKind)}" : (e.Model.ForceAllCopy ? "全まね（模倣）" : CopyKinds.Short(e.Model.CopyKind)) + (e.Model.Silenced ? "・沈黙" : !PhaseRules.HasTrait(e.Model) && turn.Grid.Model.V4 ? (e.Model.Kind == EnemyKind.Normal ? (e.Model.CopyKind == CopyKind.None ? "・攻撃しない" : "・まねるだけ") : "・動けない") : "") + (lastTwo ? "・被ダメ2倍" : ""); // 【RC10・初見テスト2回目「2倍ダメは誰が？」】 // 【RC9・初見テスト「全まねなのに③まねに入っていない」「段階2/3・全…が切れる」】
                    if (HideCopyKindNames) sub = e.Model.CopyKind == CopyKind.None ? "まねない" : "あなたの命令をまねる"; // 【RC12】5分体験の1・2問目は名前（全まね）を出さない：何をするかだけ
                    GUI.Label(new Rect(ex, r.y + 25, ew - chipW - 2, 17), sub, Fit(new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = CopyKindColor(e.Model.CopyKind) } }, sub, ew - chipW - 2, 10)); // 【RC9・初見テスト4回目「まねない(この…が切れている」】短い言葉＋入りきらなければ小さく
                    if (clock) DrawClockChip(new Rect(r.xMax - chipW - 4, r.y + 6, chipW, 30), e.Model, true);
                }
                else GUI.Label(new Rect(ex, r.y + 20, ew, 16), "撃破", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, 0.5f) } });
                if (alive && Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
                {
                    if (TacticTargeting) PickTacticTarget(e);
                    else selectedEnemy = selectedEnemy == e ? null : e;
                    Event.current.Use();
                }
            }
            // 【RC8・レビュー】眠っている写し身の像：盤面にいるのに上の欄に無かった → 最後に小さく1つ（段階2で目覚める）
            int statues = turn.Enemies.Count(e => e.IsAlive && e.Model.Kind == EnemyKind.HealClone && e.Model.Dormant);
            if (statues > 0) // 【RC9】1回目「像（段階2）」の文字をやめた → 2回目「Zz の2体が上の欄にいない（B・C が抜けている）」→ 記号を薄く＋Zz だけ（文字は無し・マウスで説明）
            {
                float sx = x + enemies.Count * (cw + gap);
                var sr = new Rect(Mathf.Min(sx, bw - 70), y + 8, 62, 26);
                DrawRect(sr, new Color(0.12f, 0.13f, 0.18f, 0.9f));
                DrawFrame(sr, new Color(0.6f, 0.62f, 0.7f, 0.8f), 1f);
                var stEnemies = turn.Enemies.Where(e => e.IsAlive && e.Model.Kind == EnemyKind.HealClone && e.Model.Dormant).ToList();
                for (int k = 0; k < stEnemies.Count && k < 3; k++) DrawLetterBadge(new Rect(sr.x + 3 + k * 15, sr.y + 6, 14, 14), stEnemies[k].Model, 0.55f);
                GUI.Label(new Rect(sr.x + 3 + Mathf.Min(3, stEnemies.Count) * 15, sr.y, sr.width - 6 - Mathf.Min(3, stEnemies.Count) * 15, sr.height), "Zz", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.8f, 0.82f, 0.9f) } });
                if (sr.Contains(Event.current.mousePosition)) { cardTipTitle = "写し身の像（まだ動かない）"; cardTipBody = $"段階2（主の HP {Mathf.Max(0, turn.Grid.Model.Enemies.Where(e => e.Kind == EnemyKind.MirrorLord).Select(e => CombatBoss.Floor(e, 0)).DefaultIfEmpty(0).First())}）で目覚める。それまでは通れない石の像。"; }
            }
            DrawV2Timeline(new Rect(8, 50, bw - 16, 36));
        }

        /// <summary>【RC8・初見テスト「技まね・移動まねの意味が分からない」】まね方＋何をまねるか（短く）。</summary>
        static string CopyKindPlain(CopyKind k) => k == CopyKind.All ? "全まね（全部）" : k == CopyKind.Technique ? "技まね（移動以外）" : k == CopyKind.Move ? "移動まね（移動だけ）" : "まねない";

        /// <summary>大技までのカウント（砂時計と数字。①は「今」と赤紫）。</summary>
        void DrawClockChip(Rect r, UnitModel e, bool vertical)
        {
            var near = Clocks.Nearest(e);
            if (near == null) return;
            bool warn = near.Cd == 1 && near.Telegraphed;
            float pulse = warn ? Blink(0.45f, 1f, 8f) : 1f;
            DrawRect(r, warn ? new Color(0.8f * pulse, 0.1f, 0.72f * pulse, 0.97f) : new Color(0.05f, 0.06f, 0.14f, 0.92f));
            DrawFrame(r, warn ? new Color(1f, 0.75f, 1f) : new Color(1f, 0.85f, 0.45f, 0.85f), warn ? 2f : 1f);
            if (vertical)
            {
                // 【RC8・初見テスト「砂時計の数字が何か分からない」】上の欄は言葉で「大技 あと2」
                GUI.Label(new Rect(r.x, r.y + 1f, r.width, 13f), "大技", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = warn ? Color.white : new Color(1f, 0.75f, 0.95f) } }); // 【通常リリース版・画面の直し 10】10 → 11（720p で小さかった）
                GUI.Label(new Rect(r.x, r.y + 13f, r.width, r.height - 13f), warn ? "今！" : $"あと{near.Cd}", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = warn ? Color.white : new Color(1f, 0.9f, 0.5f) } });
            }
            else
            {
                float sw = r.width < 26f ? 10f : 12f;
                GUI.Label(new Rect(r.x + 1f, r.y, sw, r.height), "★", new GUIStyle(Tiny) { fontSize = r.width < 26f ? 9 : 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color(1f, 0.6f, 0.95f) } }); // 【RC9・2回目「砂時計が I や X に見える」】★＝大技（札の「B ★4」と同じ）
                GUI.Label(new Rect(r.x + sw, r.y, r.width - sw, r.height), warn ? "今" : near.Cd.ToString(), new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = warn ? Color.white : new Color(1f, 0.9f, 0.5f) } });
            }
            if (r.Contains(Event.current.mousePosition))
            {
                cardTipTitle = $"{PhaseRules.ShortName(e)}の大技：{near.Name}（{(warn ? "このターン" : $"あと {near.Cd} ターン")}）";
                cardTipBody = ClockHelp(near);
            }
        }

        /// <summary>
        /// 【RC8・本人「動く順番が大事なのに分かりにくい」】順番の帯：TURN ｜ ①あなた › ②攻撃 A B C › ③まね › ④大技 › ⑤移動 › ⑥床。
        /// 入力中は①が光る。実行中は今の段が光り、②③では今動いている敵の記号が大きく光る。マウスを乗せると、その段の危険だけ盤面で光る（右下に説明）。
        /// この部屋で起きない段は出さない（大技の無い部屋に④は無い）。順番は TurnManager の実行と同じ（TurnStep）。
        /// </summary>
        void DrawV2Timeline(Rect r)
        {
            var model = turn.Grid.Model;
            DrawRect(r, new Color(0.02f, 0.03f, 0.07f, 0.7f));
            // TURN
            int limit = turn.CurrentStage != null ? turn.CurrentStage.turnLimit : 0;
            float fx = r.x + 2;
            if (act != null && act.Run != null && act.InBattle && !act.InTutorial) // 【第2巡 5】上の欄にいつも今の階（前は左下の目的の行の小さい字だけ）
            {
                var fr = new Rect(fx, r.y + 2, 74, r.height - 4);
                DrawUi(fr, "panel", PanelBlue);
                string fl = $"{Mathf.Min(act.Run.Floor + 1, act.Run.FloorCount)}/{act.Run.FloorCount}階";
                ShadowLabel(fr, fl, Fit(new GUIStyle(Big) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = false }, fl, fr.width - 6, 12), Color.white);
                fx = fr.xMax + 4;
            }
            var tr = new Rect(fx, r.y + 2, 92, r.height - 4);
            DrawUi(tr, "panel", PanelBlue);
            if (limit <= 0 && model.EscalateAfter > 0)
            {
                // 【RC12】激化：目安のターン（ParTurns）までは「TURN 5/7」、過ぎたら「激化 +1」（敵の攻撃が強くなる）。マウスで説明
                bool esc = turn.TurnNumber > model.EscalateAfter;
                string tt = esc ? $"T{turn.TurnNumber} 激化+{model.EscalateBonus}" : $"TURN {turn.TurnNumber}/{model.EscalateAfter}";
                ShadowLabel(tr, tt, Fit(new GUIStyle(Big) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = false }, tt, tr.width - 6, 11), esc ? new Color(1f, 0.45f, 0.35f) : turn.TurnNumber == model.EscalateAfter ? new Color(1f, 0.75f, 0.45f) : Gold);
                if (tr.Contains(Event.current.mousePosition)) { cardTipTitle = "目安のターンと激化"; cardTipBody = $"この部屋の目安は {model.EscalateAfter}ターン。目安のうちに勝つと速さの報酬（ルーン片）。{model.EscalateAfter + 1}ターン目からは激化：敵の攻撃（赤）が +{model.EscalateBonus}。\nよけ続けて長引かせるほど危ない。少し受けてでも早く倒すか、無傷で粘るかを選ぶ。"; }
            }
            else ShadowLabel(tr, limit > 0 ? $"TURN {turn.TurnNumber}/{limit}" : $"TURN {turn.TurnNumber}", new GUIStyle(Big) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = false }, limit > 0 && turn.TurnNumber >= limit ? new Color(1f, 0.55f, 0.45f) : Gold);
            bool attacks = !model.NoTraits && model.Enemies.Any(e => e.IsAlive && !e.IsObject && PhaseRules.AttackDamageOf(model, e) > 0 || e.IsAlive && (e.Kind == EnemyKind.Bomber || e.Kind == EnemyKind.MirrorLord));
            bool clocks = model.Enemies.Any(e => e.IsAlive && e.Clocks.Count > 0);
            bool moves = !model.NoTraits && !model.NoPositioning && model.Enemies.Any(e => e.IsAlive && !e.IsObject && (PhaseRules.MovesOnItsOwn(e) || e.Kind == EnemyKind.Bomber || e.Kind == EnemyKind.Spearman || e.Kind == EnemyKind.Archer || e.Kind == EnemyKind.Mender || e.Kind == EnemyKind.HealClone));
            bool floor = model.Environment.HasFire;
            var steps = new List<(int idx, TurnManager.TurnStep step, string label, Color col)>();
            steps.Add((0, TurnManager.TurnStep.You, "①あなた", new Color(0.6f, 0.82f, 1f)));
            if (attacks) steps.Add((1, TurnManager.TurnStep.EnemyAttack, "②攻撃", new Color(1f, 0.5f, 0.45f)));
            if (model.CopyEnabled) steps.Add((2, TurnManager.TurnStep.Copy, "③まね", new Color(0.4f, 0.95f, 1f)));
            if (clocks) steps.Add((3, TurnManager.TurnStep.BigAttack, "④大技", new Color(1f, 0.5f, 0.95f)));
            if (moves || (clocks && !(QuickUi && model.NoPositioning))) steps.Add((4, TurnManager.TurnStep.Move, "⑤移動", OwnMoveColor)); // 【RC12・初見テスト「3問目で5段の順番が一度に来る」】5分体験のパズル（敵は自分で動かない）では出さない
            if (floor) steps.Add((5, TurnManager.TurnStep.Floor, "⑥床", new Color(0.95f, 0.88f, 0.72f)));
            steps = steps.Select(s => (s.idx, s.step, s.label.Length > 1 && s.label[0] >= '①' && s.label[0] <= '⑳' ? s.label.Substring(1) : s.label, s.col)).ToList(); // 【通常リリース版・初見と UI/UX のレビュー「①③⑥と番号が飛んでバグに見える」】番号は出さない（順番は › で分かる）。前は文化祭版だけ // 【文化祭・初見レビュー「①③⑥と番号が飛んでバグに見える」】番号を出さない
            var cur = turn.State == BattleState.PlayerInput ? TurnManager.TurnStep.You : turn.Step;
            bool running = turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting;
            var enemies = model.Enemies.Where(e => e.IsAlive && !e.IsObject && !e.Dormant).ToList();
            // 【RC8・初見テスト「攻撃 A B C なのに A と C に赤が無い」】②は今このターン攻撃する敵だけ・③はまねる敵だけ
            var attackers = enemies.Where(e => threatsBase.Any(t => t.SourceId == e.Id && t.Timing == ThreatTiming.Attack)).ToList();
            var copiers = enemies.Where(e => e.CopyKind != CopyKind.None && !CopyCore.Stunned(e)).ToList();
            if (turn.State == BattleState.PlayerInput && !model.NoTraits) { lastOwnMoveIds = new HashSet<int>(ForecastFor(model, new List<CommandRecord>()).OwnMoves.Select(m => m.EnemyId)); lastOwnMoveTurn = turn.TurnNumber; }
            // 【RC10・初見テスト（戦術好き）「押された剣兵が⑤で動いたのに、帯の⑤に記号が無い」】⑤の段が始まったときの位置から実際に動いた敵は、その場で⑤に足す（帯＝実際の動き）
            if (running && cur == TurnManager.TurnStep.Move && lastOwnMoveIds != null && lastOwnMoveTurn == turn.TurnNumber)
            {
                if (movePosTurn != turn.TurnNumber) { movePosTurn = turn.TurnNumber; movePosAtStart = model.Enemies.Where(e => e.IsAlive).ToDictionary(e => e.Id, e => e.Position); }
                foreach (var e in model.Enemies) if (e.IsAlive && movePosAtStart.TryGetValue(e.Id, out var p0) && p0 != e.Position) lastOwnMoveIds.Add(e.Id);
            }
            var ownIds = !model.NoTraits && lastOwnMoveIds != null && lastOwnMoveTurn == turn.TurnNumber ? lastOwnMoveIds : null; // 【RC9・4回目「実行中に⑤の記号が変わる」】入力中（ターンの初め）の一覧を実行中も
            var movers = enemies.Where(e => !model.NoTraits && !model.NoPositioning && PhaseRules.MovesOnItsOwn(e) && (ownIds == null || ownIds.Contains(e.Id))).ToList(); // 【RC8・初見テスト2「⑤移動に記号が無い」】【RC9・2回目「⑤に B がいるのに B の移動先が無い」】予告に動く先がある敵だけ
            var lab = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            float x = tr.xMax + 10;
            for (int i = 0; i < steps.Count; i++)
            {
                var (idx, step, label, col) = steps[i];
                var who = idx == 1 ? attackers : idx == 2 ? copiers : idx == 4 ? movers : null;
                bool withLetters = who != null && who.Count > 0;
                float lw = lab.CalcSize(new GUIContent(label)).x;
                float w = lw + 14 + (withLetters ? who.Count * 17 + 2 + (idx == 2 ? (who.Count - 1) * 7 : 0) : 0);
                var cr = new Rect(x, r.y + 4, w, r.height - 8);
                bool now = cur == step || (cur == TurnManager.TurnStep.Input && idx == 0);
                bool past = running && cur > step;
                if (cr.Contains(Event.current.mousePosition)) hoverStep = idx;
                // 【通常リリース版・画面の直し 3】今の段だけ色（マウスを乗せた段も）。ほかの段は暗い灰色（A B C の字も灰色）
                bool lit = now || hoverStep == idx || hoverStepLast == idx;
                var grey = new Color(0.56f, 0.58f, 0.64f, past ? 0.45f : 0.85f);
                DrawRect(cr, now ? new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.95f) : new Color(0.06f, 0.07f, 0.12f, past ? 0.5f : 0.9f));
                DrawFrame(cr, now ? new Color(col.r, col.g, col.b, running ? Blink(0.6f, 1f, 8f) : 1f) : lit ? new Color(col.r, col.g, col.b, 0.75f) : new Color(0.4f, 0.42f, 0.48f, past ? 0.25f : 0.5f), now ? 2f : 1f);
                ShadowLabel(new Rect(cr.x + 7, cr.y, lw + 4, cr.height), label, lab, lit ? col : grey);
                if (withLetters)
                {
                    float bx = cr.x + 9 + lw;
                    foreach (var e in who)
                    {
                        bool actingNow = running && now && turn.ActingEnemy != null && turn.ActingEnemy.Model == e;
                        bool hov = hoverEnemyIdLast == e.Id; // 【RC8】敵にマウス → 帯のその敵の記号も光る（敵 ↔ 攻撃のマス ↔ 順番）
                        float s = actingNow || hov ? 17f : 14f;
                        var brr = new Rect(bx, cr.center.y - s / 2f, s, s);
                        DrawLetterBadge(brr, e, past ? 0.35f : (running && now && !actingNow ? 0.55f : 1f), !lit && !hov);
                        if (hov) DrawFrame(new Rect(brr.x - 2, brr.y - 2, brr.width + 4, brr.height + 4), Color.white, 1.5f);
                        bx += 17f;
                        if (idx == 2 && e != who[who.Count - 1]) { GUI.Label(new Rect(bx - 2f, cr.y, 9f, cr.height), "›", new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.6f, 1f, 1f) } }); bx += 7f; } // 【RC9・4回目「まねは命令ごと？まとめて？」】A が全部 › B が全部
                    }
                }
                x = cr.xMax;
                if (i < steps.Count - 1)
                {
                    ShadowLabel(new Rect(x, r.y, 14, r.height), "›", new GUIStyle(Big) { fontSize = 18, alignment = TextAnchor.MiddleCenter }, new Color(1f, 1f, 1f, 0.5f));
                    x += 14;
                }
            }
            // 【RC9・初見テスト2回目「実行中に、だれが何をまねているか分からない（命令の枠も空き）」】③の間は、今まねている敵と命令
            if (running && turn.Step == TurnManager.TurnStep.Copy && turn.ActingEnemy != null && turn.CurrentCopy != null && turn.CurrentCopy.Command != null)
            {
                var cc = turn.CurrentCopy;
                string what = $"がまねる：{cc.Command.displayName}{(cc.Command.UsesDirection ? DirGlyph(cc.Direction) : "")}";
                var cst = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft };
                float cw2 = cst.CalcSize(new GUIContent(what)).x + 34f;
                var cr2 = new Rect(Mathf.Min(x + 10, r.xMax - cw2 - 4), r.y + 4, cw2, r.height - 8);
                DrawRect(cr2, new Color(0.02f, 0.2f, 0.25f, 0.95f));
                DrawFrame(cr2, new Color(0.4f, 0.95f, 1f, 0.9f), 1.5f);
                DrawLetterBadge(new Rect(cr2.x + 5, cr2.center.y - 9, 18, 18), turn.ActingEnemy.Model);
                ShadowLabel(new Rect(cr2.x + 27, cr2.y, cw2 - 27, cr2.height), what, cst, new Color(0.6f, 1f, 1f));
            }
            if (hoverStep >= 0)
            {
                cardTipTitle = StepTitle(hoverStep);
                cardTipBody = StepHelp(hoverStep);
            }
        }

        HashSet<int> lastOwnMoveIds; int lastOwnMoveTurn = -1;
        Dictionary<int, Vector2Int> movePosAtStart = new Dictionary<int, Vector2Int>(); int movePosTurn = -1; // 【RC10】⑤の段の初めの位置

        static string StepTitle(int idx) => idx switch
        {
            0 => "① あなた：組んだ命令を左から順に",
            1 => "② 敵の攻撃（赤・紫）：A → B → C の順",
            2 => "③ まね：敵があなたの命令をまねる（A が全部 → B が全部 …）",
            3 => "④ 大技（紫・★）：床に決まったマスへ",
            4 => "⑤ 敵の移動と予告",
            _ => "⑥ 床の仕掛け（炎・落石・光線）",
        };

        static string StepHelp(int idx) => idx switch
        {
            0 => "あなたの命令が一番先。押す・引くで このあとの敵の攻撃（赤）をずらし、倒せば その敵の攻撃（赤・紫）を消せる。",
            1 => "敵は今いる場所から攻撃する。赤（点線の縁）＝敵と動く攻撃（押す・引くでずれる・倒せば消える）。紫（斜線）＝床に固定の攻撃（押しても動かないが、撃つ敵を倒せば消える）。敵にも当たる。",
            2 => "敵はあなたの命令を、それぞれの位置からまねる（水色＝この札をまねる敵）。まねで大技（紫）や床の仕掛けへ歩かせられる。赤はもう終わっているので、まねでは当たらない。",
            3 => "①になった大技は、紫のマスに決まって動かない。撃つ敵が倒れたら起きない。マスにいる全員に当たる（敵にも）。",
            4 => "敵が自分で近づく・向きを変える（オレンジの矢印）。次のターンの赤と大技がここで決まる。",
            _ => "炎・落石・光線は、そのマスにいる全員に当たる（敵にも）。誰のものでもないので、倒しても消えない。",
        };

        // ───────── 下：目的の1行 ─────────

        void DrawObjectiveV2(Rect r)
        {
            var model = turn.Grid.Model;
            var stage = turn.CurrentStage;
            DrawRect(r, new Color(0.02f, 0.03f, 0.07f, 0.78f));
            var lord = model.Enemies.FirstOrDefault(e => e.IsAlive && e.Kind == EnemyKind.MirrorLord);
            string head = lord != null ? $"段階{lord.BossStage + 1}/3　【目的】" : "【目的】";
            // 【RC11・採用担当の最後の見直し「戦闘の画面で今が何階か分からない」】本編のランでは、目的の前に「3/8階」
            // 【第2巡 5】今の階は上の欄（順番の帯の左）へ移した（同じことを2か所に出さない）
            string goal = lord != null ? BossObjective(model, lord) : PuzzleRules.GoalSentence(model, stage.goalText);
            var hs = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft }; // 【RC8・レビュー】小さかった
            float hw = hs.CalcSize(new GUIContent(head)).x;
            ShadowLabel(new Rect(r.x + 8, r.y, hw + 4, r.height), head, hs, lord != null ? Gold : new Color(Gold.r * 0.8f, Gold.g * 0.8f, Gold.b * 0.8f, 0.85f)); // 【RC10】画面の強弱：ふつうの部屋の目的は控えめ
            // 【RC9・初見テスト2回目「点線・斜線の凡例が常に見える所に無い」】右に小さな凡例（絵＋2字）：点線＝敵と動く・斜線＝固定・★＝大技。部屋の名前と階は凡例（L）とメニューで
            float legW = 232f;
            float gw = r.width - hw - legW - 34; // 【通常リリース版・画面の直し 9】凡例の帯との間を空ける（720p で目的の最後の字が凡例の点線に触れていた）
            ShadowLabel(new Rect(r.x + 8 + hw, r.y, gw, r.height), goal, Fit(new GUIStyle(hs) { fontSize = lord != null ? 15 : 14 }, goal, gw, 11), lord != null ? Color.white : new Color(0.82f, 0.85f, 0.92f, 0.9f)); // 1行（ボスの目的は短い言い方：BossObjective）
            DrawThreatLegendV2(new Rect(r.xMax - legW - 4, r.y + 2, legW, r.height - 4));
        }

        /// <summary>【RC9】盤面の下の小さな凡例：［点線の枠］敵と動く・［斜線］固定・★ 大技・L 詳しく。</summary>
        void DrawThreatLegendV2(Rect r)
        {
            var st = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color(0.78f, 0.8f, 0.88f, 0.9f) } }; // 【RC10】凡例は控えめ（いつも出すが、目立たせない）
            float s = Mathf.Min(18f, r.height - 4f), y = r.center.y - s / 2f, x = r.x;
            var a = new Rect(x, y, s, s);
            DrawRect(a, new Color(0.45f, 0.06f, 0.06f, 0.9f));
            var dc = new Color(0.92f, 0.93f, 0.97f); // 【RC9・4回目「凡例の見本に点線が見えない」】太く・明るく【RC10・UI/UX「点線は敵ごとの色なのに凡例は黄色だけ」】白（どの敵の色でも）
            for (float q = a.x; q < a.xMax; q += 6f) { DrawRect(new Rect(q, a.y, Mathf.Min(3.5f, a.xMax - q), 2.5f), dc); DrawRect(new Rect(q, a.yMax - 2.5f, Mathf.Min(3.5f, a.xMax - q), 2.5f), dc); }
            for (float q = a.y; q < a.yMax; q += 6f) { DrawRect(new Rect(a.x, q, 2.5f, Mathf.Min(3.5f, a.yMax - q)), dc); DrawRect(new Rect(a.xMax - 2.5f, q, 2.5f, Mathf.Min(3.5f, a.yMax - q)), dc); }
            GUI.Label(new Rect(a.xMax + 3, r.y, 62, r.height), "敵と動く", new GUIStyle(st) { normal = { textColor = new Color(1f, 0.62f, 0.55f, 0.95f) } });
            x = a.xMax + 66;
            var b = new Rect(x, y, s, s);
            DrawRect(b, new Color(0.5f, 0.08f, 0.46f, 0.9f)); // 【RC11】固定＝紫
            DrawHatch(new Rect(b.x - 3, b.y - 3, b.width + 6, b.height + 6), 0.9f);
            DrawFrame(b, Color.white, 1.5f);
            GUI.Label(new Rect(b.xMax + 3, r.y, 32, r.height), "固定", new GUIStyle(st) { normal = { textColor = new Color(1f, 0.6f, 0.97f, 0.95f) } });
            x = b.xMax + 38;
            GUI.Label(new Rect(x, r.y, 50, r.height), "★大技", new GUIStyle(st) { normal = { textColor = new Color(1f, 0.7f, 0.97f) } });
            if (turn.Grid.Model.Environment.HasFire) // 【RC9・4回目「岩は固定なのに斜線が無い（凡例に無い）」】床の仕掛けは絵（誰のものでもない）：詳しく L の代わりに［絵］床
            {
                DrawUiImage(new Rect(x + 50f, y, s, s), FloorIconKey(false), 1f, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(x + 52f + s, r.y, 30, r.height), "床", st);
            }
            else GUI.Label(new Rect(x + 48, r.y, 52, r.height), "詳しく L", new GUIStyle(st) { fontStyle = FontStyle.Normal, normal = { textColor = new Color(0.75f, 0.78f, 0.88f) } });
            if (r.Contains(Event.current.mousePosition)) { cardTipTitle = "盤面の印"; cardTipBody = "赤（点線の枠）＝撃つ敵と動く攻撃（押す・引くでずれる・倒せば消える）／紫（斜線）＝床に固定の攻撃（押しても動かない・撃つ敵を倒せば消える）／★＝大技（紫・あと何ターン）。マスの札［A 2］＝撃つ敵と、その攻撃のダメージ（重なったら自分で足す）。"; }
        }

        /// <summary>【RC8・本人「核・眠り・写し身・段階…何をすればいいか分からなかった」】ボスの段階ごとに、することを1つだけ。</summary>
        static string BossObjective(BattleModel model, UnitModel lord)
        {
            // 【通常リリース版・画面の直し 9】1行に収まる短い言い方（720p で凡例の帯に触れていた・文字が 10px まで小さくなった）。くわしい段階のルールは段階の帯とマウスの説明
            if (lord.StunRounds > 0) return lord.BossStage == 1 ? "反射で主がひるんだ：次の光柱は来ない" : "主が動けない（共鳴）：今のうちに攻撃！"; // 【RC10・初見テスト3回目】ひるみはこのターンの残りだけ
            switch (lord.BossStage)
            {
                // 【RC9・初見テスト「ボスの目的文は手順そのもの（答え）」】手順ではなくルールで書く（どう使うかは遊ぶ人が考える）
                case 0: return $"主に攻撃を当てる（HP {CombatBoss.Floor(lord, 0)} で段階2）"; // 「主は全部まねる」は上の敵の欄（段階1/3・全まね）と段階の帯
                case 1: return model != null && !model.Enemies.Any(e => e.IsAlive && e.Kind == EnemyKind.HealClone) ? $"写し身はもういない・HP {CombatBoss.Floor(lord, 1)} で段階3" : $"写し身は鏡（主の攻撃をはね返す）・HP {CombatBoss.Floor(lord, 1)} で段階3"; // 【RC11・最後の見直し（初見）「写し身がいないのに目的が写し身のまま・段階3へ進む HP が無い」】
                default: return $"核2つを同じターンに叩く（1つ以上は敵のまね）：主に -{CombatBoss.CoreBossDamage}"; // 【RC11・QA】段階の帯と同じ言い方（「1ターン止まる」は段階の帯）
            }
        }

        // ───────── 盤面：危険（記号・数字・種類）・敵の札・あなた ─────────

        struct CellThreat
        {
            public int Dmg, NextDmg;
            public bool Red, Big, Floor, FixedRed, FollowRed;
            public List<UnitModel> Sources;
        }

        readonly Dictionary<Vector2Int, CellThreat> cellThreats = new Dictionary<Vector2Int, CellThreat>();

        /// <summary>
        /// 【RC9・本人「同じ意味を重ねない」】札を出すマス：攻撃1つにつき撃つ敵にいちばん近いマス（線の起点）・誰かが立っているマス・攻撃が2つ以上重なるマスだけ。
        /// 床の仕掛け（行・列）はまとまりの左上のマスだけ（レーザーの行に同じ札が7枚並んでいた）。ほかのマスは外周の線と塗りで同じ攻撃だと分かる。
        /// </summary>
        readonly Dictionary<Threat, HashSet<Vector2Int>> pillCells = new Dictionary<Threat, HashSet<Vector2Int>>();

        /// <summary>【RC9・あなたの見え方】盤面の札を出すマスか：あなたのマスには重ねない（あなたの絵をつぶさない・攻撃は「▼あなた」の札の中）。</summary>
        bool DrawsPillsOn(Vector2Int c) => turn.Grid.Model.Player == null || c != turn.Grid.Model.Player.Position;

        /// <summary>【RC9】「▼あなた」の札に並べる攻撃：今あなたのマスに来る攻撃を1つずつ（②赤 → ④大技 → ⑥床・記号順）。合計は出さない。</summary>
        List<Threat> ThreatsOnYou()
        {
            var pos = turn.Grid.Model.Player.Position;
            return threatsShown.Where(t => t.Cells.Contains(pos) && t.Timing != ThreatTiming.NextFloor)
                .OrderBy(t => t.Timing == ThreatTiming.Attack ? 0 : t.Timing == ThreatTiming.BigAttack ? 1 : 2).ThenBy(t => t.Source != null ? t.Source.KindLetter : "~").ToList();
        }

        void BuildPillCells()
        {
            pillCells.Clear();
            var model = turn.Grid.Model;
            var overlap = new Dictionary<Vector2Int, int>();
            foreach (var t in threatsShown) if (t.Timing != ThreatTiming.NextFloor) foreach (var c in t.Cells) overlap[c] = overlap.TryGetValue(c, out var n) ? n + 1 : 1;
            foreach (var t in threatsShown)
            {
                var set = new HashSet<Vector2Int>();
                var cells = new HashSet<Vector2Int>(t.Cells);
                if (t.Source != null && t.Cells.Count > 0)
                    set.Add(t.Cells.OrderBy(c => Mathf.Abs(c.x - t.Source.Position.x) + Mathf.Abs(c.y - t.Source.Position.y)).First());
                else
                    foreach (var c in t.Cells) if (!cells.Contains(c + Vector2Int.left) && !cells.Contains(c + Vector2Int.up)) set.Add(c);
                foreach (var c in t.Cells)
                    if ((model.GetUnitAt(c) != null && model.GetUnitAt(c).IsAlive) || (t.Timing != ThreatTiming.NextFloor && overlap.TryGetValue(c, out var k) && k >= 2)) set.Add(c);
                pillCells[t] = set;
            }
        }

        void BuildCellThreats()
        {
            BuildPillCells();
            cellThreats.Clear();
            foreach (var t in threatsShown)
                foreach (var c in t.Cells)
                {
                    cellThreats.TryGetValue(c, out var ct);
                    if (ct.Sources == null) ct.Sources = new List<UnitModel>();
                    if (t.Timing == ThreatTiming.NextFloor) { ct.NextDmg += t.Damage; cellThreats[c] = ct; continue; }
                    ct.Dmg += t.Damage;
                    if (t.Timing == ThreatTiming.Attack) { ct.Red = true; if (t.Kind == ThreatKind.Follow) ct.FollowRed = true; else ct.FixedRed = true; }
                    else if (t.Timing == ThreatTiming.BigAttack) ct.Big = true;
                    else ct.Floor = true;
                    if (t.Source != null && !ct.Sources.Any(s => s.Id == t.Source.Id)) ct.Sources.Add(t.Source);
                    cellThreats[c] = ct;
                }
        }

        /// <summary>【RC8】盤面の上（IMGUI）。DrawShowBoardOverlay から、V4 の戦闘で呼ぶ（前の DrawV4BoardOverlay の代わり）。</summary>
        void DrawThreatOverlayV2(System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            var ev = Event.current;
            BuildCellThreats();
            // マウスの下のマス：敵 → その敵／攻撃のマス → そこへ撃つ敵（1体なら）
            Vector2Int? mouseCell = null;
            for (int x = 0; x < model.Board.Width && mouseCell == null; x++)
                for (int y = 0; y < model.Board.Height; y++)
                    if (CellRect(new Vector2Int(x, y)).Contains(ev.mousePosition)) { mouseCell = new Vector2Int(x, y); break; }
            if (mouseCell.HasValue && pendingCommand == null && !TacticTargeting && !WarpPicking)
            {
                var u = model.GetUnitAt(mouseCell.Value);
                if (u != null && !u.IsPlayer && !u.IsObject && !u.Dormant) hoverEnemyId = u.Id;
                else if (cellThreats.TryGetValue(mouseCell.Value, out var mt) && (mt.Dmg > 0 || mt.NextDmg > 0))
                {
                    if (mt.Sources.Count == 1) hoverEnemyId = mt.Sources[0].Id;
                    cardTipTitle = $"このマス：{(mt.Dmg > 0 ? $"このターン {mt.Dmg}ダメージ" : $"次のターン {mt.NextDmg}ダメージ")}";
                    cardTipBody = CellThreatText(mouseCell.Value);
                }
            }
            int focus = hoverEnemyId >= 0 ? hoverEnemyId : selectedEnemy != null ? selectedEnemy.Model.Id : -1;

            DrawFloorArtV2(CellRect);
            DrawFadingThreats(CellRect, model);
            DrawBossPhaseGuide(CellRect);

            // ずれる・消える（あなたの命令のあと）：前の場所は点線、消える攻撃は ×
            foreach (var ch in threatChanges)
            {
                var col = ch.Before.Timing == ThreatTiming.BigAttack ? new Color(1f, 0.5f, 0.95f) : new Color(1f, 0.45f, 0.4f);
                foreach (var c in ch.Before.Cells)
                {
                    if (!ch.Gone && ch.After.Cells.Contains(c)) continue;
                    var r = CellRect(c);
                    // 【RC8・初見テスト3「点線が3つの意味（一緒に動く・前の場所・押す先）」】前の場所は薄い細い枠と「前」
                    DrawFrame(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), new Color(col.r, col.g, col.b, 0.5f), 1f);
                    if (!ch.Gone) GUI.Label(new Rect(r.x + 6, r.y + 5, 20, 14), "前", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, wordWrap = false, normal = { textColor = new Color(col.r, col.g, col.b, 0.8f) } });
                    if (ch.Gone)
                    {
                        bool occ = model.GetUnitAt(c) != null; // 【RC8・初見テスト2】あなたの体に赤い × が乗った → 誰かがいるマスは右上に小さく
                        var xr = occ ? new Rect(r.xMax - 22, r.y + 2, 20, 20) : r;
                        ShadowLabel(xr, "×", new GUIStyle(Big) { fontSize = occ ? 16 : Mathf.RoundToInt(r.height * 0.45f), alignment = TextAnchor.MiddleCenter }, new Color(col.r, col.g, col.b, 0.85f));
                    }
                }
                // 札：消える／ずれる（敵の記号つき）
                var anchor = ch.Gone ? ch.Before.Cells[0] : ch.After.Cells.FirstOrDefault(c => !ch.Before.Cells.Contains(c));
                if (!ch.Gone && !ch.After.Cells.Any(c => !ch.Before.Cells.Contains(c))) anchor = ch.After.Cells[0];
                var ar = CellRect(anchor);
                string txt = ch.Gone ? "倒すと消える" : "ずれる";
                var ts = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                float w = ts.CalcSize(new GUIContent(txt)).x + 24;
                var tagR = new Rect(ar.center.x - w / 2f, ar.yMax - 18, w, 16);
                DrawRect(tagR, ch.Gone ? new Color(0.1f, 0.1f, 0.12f, 0.92f) : new Color(0.35f, 0.05f, 0.05f, 0.92f));
                DrawFrame(tagR, col, 1f);
                DrawLetterBadge(new Rect(tagR.x + 2, tagR.y + 2, 12, 12), ch.Source);
                GUI.Label(new Rect(tagR.x + 14, tagR.y, tagR.width - 14, tagR.height), txt, new GUIStyle(ts) { normal = { textColor = Color.white } });
            }

            // 【RC9・本人「1マスに札が3〜4個」「同じ意味を重ねない」】攻撃のまとまりごとに外周の線を1本（撃つ敵の色：点線＝敵と一緒に動く／実線＝床に決まる）。
            //   マスの中は左上の小さな札の積み上げ「A 2」「B ★4」「炎 3」だけ（記号＋ダメージ。RC8 のマスごとの枠2本・右上の合計・記号・三角・マスごとの「固定」をまとめた）。
            //   合計は出さない（重なったときの合計は遊ぶ人が足す：本人と ChatGPT の判断）。
            DrawThreatOutlines(CellRect, focus);
            if (QuickUi || (act != null && act.InTutorial)) DrawPillarTags(CellRect); // 【RC11・5分体験の初見テスト「柱が落とし穴に見えた」】
            foreach (var kv in cellThreats)
            {
                var c = kv.Key; var ct = kv.Value;
                var r = CellRect(c);
                bool dim = focus >= 0 && !ct.Sources.Any(s => s.Id == focus);
                if (hoverStep >= 0) dim = !(hoverStep == 1 ? ct.Red : hoverStep == 3 ? ct.Big : hoverStep == 5 ? ct.Floor || ct.NextDmg > 0 : false);
                float a = dim ? 0.3f : 1f;
                if (!DrawsPillsOn(c)) continue; // 【RC9・初見テスト1回目「自分のマスに札が重なって絵がつぶれる」】あなたのマスの攻撃は「▼あなた」の札の中に並べる
                // 向きを選んでいる間、敵のマスは記号だけ（数字は出さない：敵の絵とあなたの攻撃の数字を隠さない）
                pillFloorShort = model.GetUnitAt(c) != null && model.GetUnitAt(c).IsAlive;
                DrawThreatPills(c, r, a, focus, pendingCommand != null && model.GetUnitAt(c) != null && !model.GetUnitAt(c).IsPlayer);
                pillFloorShort = false;
            }
            if (ShowFixedLabel) DrawFixedLabels(CellRect, focus);

            // 敵が自分で動く先（オレンジ）・転移石の行き先。【RC8・初見テスト3】向きを選んでいる間は出さない（⑤の話：押す先と接近の影が同じマスに見えた）
            if (pendingCommand == null) DrawOwnMoveForecast(model, CellRect);
            if (WarpPicking) DrawWarpTargets(model, CellRect);
            else kbWarpCell = null;

            // 敵の札：記号・HP の帯・大技のカウント（マスの下の端）。マウスを乗せた敵は太い枠
            foreach (var e in turn.Enemies)
            {
                var m = e.Model;
                if (!e.IsAlive || m.IsObject) continue;
                var r = CellRect(m.Position);
                if (m.Dormant && m.Kind == EnemyKind.HealClone)
                {
                    // 【RC9】「像」の文字は出さない：暗くして「まだ動かない」に見せる（マウスで説明）
                    // 【RC11・本人「半透明だから通れると思った」】絵が石の像（不透明・台座）になったので、暗くする板はやめる
                    // 【RC9・初見テスト「文字も HP も無い黒いフードは何？」】記号を薄く（上の欄に B・C が無い理由）と、眠りの Zz
                    DrawLetterBadge(new Rect(r.x + 3, r.yMax - 18, 15, 15), m, 0.5f);
                    ShadowLabel(new Rect(r.xMax - 26, r.y + 2, 24, 18), "Zz", new GUIStyle(Tiny) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(0.75f, 0.82f, 1f, 0.9f));
                    if (r.Contains(ev.mousePosition)) { cardTipTitle = "石の像（眠っている写し身・通れない）"; cardTipBody = $"段階2で目覚める。それまでは通れない石の像（壁と同じ）。目覚めると毎ターン主を +{CombatBoss.CloneHeal} 回復する。写し身は鏡：主の攻撃が当たると主へはね返る（-{CombatBoss.CloneStrikeDamage}）。"; }
                    continue;
                }
                var idc = EnemyIdColor(m);
                bool lit = focus == m.Id;
                if (lit) DrawFrame(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), idc, 3f);
                bool acting = (turn.State == BattleState.EnemyExecuting) && turn.ActingEnemy == e;
                if (acting) DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Gold, 2f);
                bool warn = m.Clocks.Any(c => c.Cd == 1 && c.Telegraphed) && PhaseRules.HasTrait(m);
                if (warn) DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(1f, 0.45f, 0.95f, Blink(0.4f, 1f, 9f)), 3f);
                float bh = Mathf.Clamp(r.height * 0.2f, 13f, 17f);
                var badge = new Rect(r.x + 2, r.yMax - bh - 5, bh, bh);
                bool hasClock = m.Clocks.Count > 0;
                float clockW = hasClock ? (r.width < 70f ? 23f : 28f) : 0f; // 【RC9・Claude の通し】小さいマス（チュートリアル・5分体験）で大技の札が HP「20」の0を隠して「2」に見えた
                DrawRect(new Rect(badge.x - 1, badge.y - 1, r.width - 2, bh + 5), new Color(0f, 0f, 0f, 0.6f));
                DrawLetterBadge(badge, m);
                if (IsBossKind(m.Kind)) // 【通常リリース版・画面の直し 23「中ボスがどれか盤面だけでは分からない」】ボス・中ボスの本体：マスの上の真ん中に金の冠と金の縁
                {
                    DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(Gold.r, Gold.g, Gold.b, 0.85f), 2f);
                    DrawCrown(new Rect(r.center.x - 12f, r.y + 2f, 24f, 15f), Gold);
                }
                // 【RC8・初見テスト2「HP の数字の下半分が帯に隠れる」】数字は記号の右（帯と重ねない）・帯は一番下の細い線
                GUI.Label(new Rect(badge.xMax + 3, badge.center.y - 9f, r.width - bh - clockW - 10, 18f), e.Hp.ToString(), new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = Color.white } }); // 【RC9】小さいマス（チュートリアル）で数字の上下が切れて「×」に見えた
                var hb = new Rect(r.x + 2, r.yMax - 4, r.width - 4, 3);
                DrawRect(hb, new Color(0f, 0f, 0f, 0.7f));
                DrawRect(new Rect(hb.x, hb.y, hb.width * e.Hp / Mathf.Max(1, e.MaxHp), hb.height), idc);
                if (hasClock) DrawClockChip(new Rect(r.xMax - clockW - 2, badge.y, clockW, bh), m, false);
                if (pendingCommand == null) DrawCopyKindMark(new Rect(r.xMax - 21f, r.y + 2f, 19f, 19f), m); // 【通常リリース版・画面の直し 10】全／技／移の印を大きく（15 → 19：上の欄の小さい字の代わり） // 向きを選んでいる間は「まね↑」が代わり（3回目「-3・全・まね↑が重なる」） // 【RC9】まね方の印はマスの右上（下の札は HP・大技の砂時計で埋まっていて入らなかった）
                if (m.Silenced || (!PhaseRules.HasTrait(m) && m.Kind != EnemyKind.Normal)) ShadowLabel(new Rect(r.x, r.y + r.height * 0.36f, r.width, 14), m.Silenced ? "沈黙" : "動けない", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.7f, 0.9f, 1f)); // 【RC9】ただの兵（攻撃しない・まねで動く）に「動けない」は誤り
                if (r.Contains(ev.mousePosition) && pendingCommand == null)
                {
                    cardTipTitle = $"{PhaseRules.ShortName(m)}　HP {e.Hp}/{e.MaxHp}　{CopyKinds.Short(m.CopyKind)}";
                    cardTipBody = EnemyHelpNow(m) + (m.Clocks.Count > 0 ? "\n大技：" + string.Join("／", m.Clocks.Select(c => $"{c.Name}（{(c.Cd == 1 && c.Telegraphed ? "このターン" : $"あと{c.Cd}")}）")) : "");
                }
                if (r.Contains(ev.mousePosition) && ev.type == EventType.MouseDown && InputPriority.EnemySelectAllowed(ev.button, pendingCommand != null, TacticTargeting, PickerOpen, showDetail)) { selectedEnemy = selectedEnemy == e ? null : e; ev.Use(); }
            }

            // 孤立（最後の1体は2倍）：【RC8・初見テスト「孤立×2 が HP と重なる」】盤面ではマスの上の外に小さく（上の欄にも「孤立×2」）
            if (model.LastStandDouble && false) // 【RC8・初見テスト2】盤面の上の札は隣のマスの数字と重なった → 上の敵の欄の「孤立×2」だけ
                foreach (var e in model.Enemies)
                {
                    if (!RulesEngine.IsLastStanding(model, e)) continue;
                    var r = CellRect(e.Position);
                    var tag = new Rect(r.center.x - 26, r.y - 14, 52, 15);
                    DrawRect(tag, new Color(0.35f, 0.22f, 0.02f, 0.92f));
                    DrawFrame(tag, new Color(1f, 0.8f, 0.35f, Blink(0.6f, 1f, 4f)), 1f);
                    GUI.Label(tag, "孤立×2", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.88f, 0.5f) } });
                }

            // 観測士：残り②の大技の範囲を薄く
            if (turn.PreviewAhead && turn.State == BattleState.PlayerInput)
                foreach (var e in model.Enemies)
                {
                    if (!e.IsAlive) continue;
                    foreach (var c in e.Clocks.Where(c => c.Cd == 2))
                        foreach (var cell in Clocks.PreviewCells(model, e, c))
                        {
                            var r = CellRect(cell);
                            DrawDashedFrame(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(1f, 0.6f, 0.95f, 0.8f), 2f, 5f);
                        }
                }

            // 【RC2】効いている強化（ガード・攻撃+）：あなたの名札の下
            // 戦術札の対象
            if (TacticTargeting)
            {
                foreach (var e in turn.Enemies)
                {
                    if (!e.IsAlive || !(IsTacticCandidate(e) || e == tacticTarget1)) continue;
                    var r = CellRect(e.Model.Position);
                    bool sel = tacticTarget1 == e;
                    DrawFrame(r, sel ? Gold : TacticGreen, 4f);
                    if (KbTacticTarget() == e) DrawFrame(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), new Color(1f, 1f, 1f, Blink(0.5f, 1f, 6f)), 3f);
                    ShadowLabel(new Rect(r.x, r.y + 2, r.width, 15), sel ? "1体目" : "対象", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, sel ? Gold : TacticGreen);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) PickTacticTarget(e);
                }
                var bA = CellRect(new Vector2Int(0, 0));
                var bB = CellRect(new Vector2Int(model.Board.Width - 1, model.Board.Height - 1));
                float boardMid = (Mathf.Min(bA.x, bB.x) + Mathf.Max(bA.xMax, bB.xMax)) / 2f, boardTop = Mathf.Min(bA.y, bB.y);
                var hint = new Rect(boardMid - 230, boardTop + 2, 460, 20);
                DrawRect(hint, new Color(0f, 0f, 0f, 0.82f));
                string what = TacticRules.TargetOf(tacticPending.tactic) == TacticTarget.EnemyThenEnemy
                    ? (tacticTarget1 == null ? $"『{tacticPending.displayName}』1体目：大技を撃つ敵を選ぶ（矢印で選んで Enter）" : $"『{tacticPending.displayName}』2体目：狙いを向ける敵を選ぶ（矢印で選んで Enter・Esc でやめる）")
                    : $"『{tacticPending.displayName}』対象の敵を選ぶ（クリックか、矢印で選んで Enter・Esc でやめる）";
                GUI.Label(hint, what, new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = TacticGreen } });
            }
        }

        // ───── 【RC9】攻撃の外周の線・マスの札の積み上げ・「固定」（攻撃1つに1つ） ─────

        /// <summary>攻撃のまとまり（Threat）ごとに外周の線を1本。撃つ敵の色・点線＝敵と一緒に動く・実線＝床に決まる。敵が2体なら少し内側にずらす（A・B・C で 3・7・11px）。</summary>
        /// <summary>【RC11】チュートリアルと5分体験では、柱のマスに小さく「柱」（通れない・ぶつかると止まる）。</summary>
        void DrawPillarTags(System.Func<Vector2Int, Rect> CellRect)
        {
            var b = turn.Grid.Model.Board;
            var st = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (!b.IsWall(c)) continue;
                    var r = CellRect(c);
                    var tag = new Rect(r.center.x - 13, r.yMax - 17, 26, 15);
                    var pl = turn.Player;
                    if (pl != null && pl.Model.Position == c + Vector2Int.down) tag.y = r.y + 2; // 【RC12・初見テスト「▼あなた の札が すぐ上の『柱』を隠す」】あなたの真上の柱は、札を上の端へ
                    DrawRect(tag, new Color(0.08f, 0.08f, 0.1f, 0.85f));
                    ShadowLabel(tag, "柱", st, new Color(0.92f, 0.9f, 0.85f));
                }
        }

        void DrawThreatOutlines(System.Func<Vector2Int, Rect> CellRect, int focus)
        {
            var o = CellRect(Vector2Int.zero);
            var gridUp = CellRect(Vector2Int.up).center.y < o.center.y ? Vector2Int.up : Vector2Int.down; // 画面の上の隣
            var gridRight = CellRect(Vector2Int.right).center.x > o.center.x ? Vector2Int.right : Vector2Int.left;
            foreach (var t in threatsShown)
            {
                if (t.Timing == ThreatTiming.Floor || t.Timing == ThreatTiming.NextFloor || t.Cells.Count == 0) continue;
                bool dim = (focus >= 0 && t.SourceId != focus) || (hoverStep >= 0 && !StepMatches(hoverStep, t));
                var sc = t.Source != null ? EnemyIdColor(t.Source) : new Color(1f, 0.45f, 0.95f);
                var col = new Color(sc.r, sc.g, sc.b, dim ? 0.3f : 0.95f);
                int li = t.Source != null && !string.IsNullOrEmpty(t.Source.KindLetter) ? t.Source.KindLetter[0] - 'A' : 0;
                float inset = 3f + (((li % 3) + 3) % 3) * 4f;
                bool dashed = t.Kind == ThreatKind.Follow;
                float th = dashed ? 3f : 2.5f; // 【RC9・初見テスト1回目「点線と実線が細くて見分けにくい」】
                if (!dashed) foreach (var c in t.Cells) DrawHatch(CellRect(c), dim ? 0.3f : 0.85f); // 【RC9】固定＝床に斜線（床に刻まれた）
                var set = new HashSet<Vector2Int>(t.Cells);
                foreach (var c in t.Cells)
                {
                    var r = CellRect(c);
                    bool T = !set.Contains(c + gridUp), B = !set.Contains(c - gridUp), L = !set.Contains(c - gridRight), R = !set.Contains(c + gridRight);
                    float x0 = r.x + inset, x1 = r.xMax - inset, y0 = r.y + inset, y1 = r.yMax - inset;
                    float hx0 = L ? x0 : r.x, hx1 = R ? x1 : r.xMax, vy0 = T ? y0 : r.y, vy1 = B ? y1 : r.yMax; // 隣も同じ攻撃なら、線を隣までつなげる
                    if (T) EdgeH(hx0, hx1, y0, col, th, dashed);
                    if (B) EdgeH(hx0, hx1, y1 - th, col, th, dashed);
                    if (L) EdgeV(x0, vy0, vy1, col, th, dashed);
                    if (R) EdgeV(x1 - th, vy0, vy1, col, th, dashed);
                }
            }
        }

        static Texture2D hatchTex;

        /// <summary>【RC9】固定の攻撃のマスに斜線（床に刻まれた＝押しても動かない）。1マス1回の描画（斜線の絵は実行時に1回だけ作る）。</summary>
        static void DrawHatch(Rect r, float alpha)
        {
            if (hatchTex == null)
            {
                const int n = 32;
                hatchTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int d = ((x + y) % 16 + 16) % 16;
                        // 【RC9・初見テスト3・4回目「紫の床（ボス）だと斜線が見えにくい」】明るい線の隣に暗い線（どの床の色でも縞に見える）
                        px[y * n + x] = d < 3 ? new Color32(255, 240, 230, 170) : d < 5 ? new Color32(25, 10, 18, 120) : new Color32(0, 0, 0, 0);
                    }
                hatchTex.SetPixels32(px);
                hatchTex.Apply();
            }
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            var inner = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
            GUI.DrawTextureWithTexCoords(inner, hatchTex, new Rect(0, 0, inner.width / 32f, inner.height / 32f));
            GUI.color = old;
        }

        static void EdgeH(float x0, float x1, float y, Color c, float th, bool dashed)
        {
            if (!dashed) { DrawRect(new Rect(x0, y, x1 - x0, th), c); return; }
            for (float x = x0; x < x1; x += 11f) DrawRect(new Rect(x, y, Mathf.Min(7f, x1 - x), th), c);
        }

        static void EdgeV(float x, float y0, float y1, Color c, float th, bool dashed)
        {
            if (!dashed) { DrawRect(new Rect(x, y0, th, y1 - y0), c); return; }
            for (float y = y0; y < y1; y += 11f) DrawRect(new Rect(x, y, th, Mathf.Min(7f, y1 - y)), c);
        }

        /// <summary>床の仕掛けの小さな絵（札の頭）。</summary>
        string FloorIconKey(bool next)
        {
            string hz = turn.CurrentStage != null ? turn.CurrentStage.hazardName ?? "" : "";
            return hz.Contains("落石") ? (next ? "hz_rock_shadow" : "hz_rock") : hz.Contains("レーザー") ? "hz_emitter" : next ? "hz_ember" : "hz_flame";
        }

        /// <summary>
        /// 【RC9】マスの左上の札の積み上げ：攻撃1つに1枚［記号 ダメージ］。②の赤＝暗い赤・④大技＝赤紫と★・⑥床＝絵（次のターンは薄い点線の「次」）。
        /// 敵と一緒に動く攻撃は、記号の横に撃つ敵の方向の三角。3枚より多ければ「+」。compact＝記号だけ（向きを選んでいる間の敵のマス）。
        /// </summary>
        void DrawThreatPills(Vector2Int c, Rect cell, float alpha, int focus, bool compact)
        {
            // 【RC9・初見テスト1回目「記号の無い赤いマスがある」】起点・誰かが立つ・重なるマス以外は、記号だけの小さな札（床の仕掛けは絵があるので出さない）
            var items = threatsShown.Where(t => t.Cells.Contains(c) && (t.Source != null || !pillCells.TryGetValue(t, out var pc0) || pc0.Contains(c)))
                .OrderBy(t => t.Timing == ThreatTiming.Attack ? 0 : t.Timing == ThreatTiming.BigAttack ? 1 : t.Timing == ThreatTiming.Floor ? 2 : 3)
                .ThenBy(t => t.Source != null ? t.Source.KindLetter : "~").ToList();
            // 【RC10・初見テスト2回目（4人）「今4 が炎の列の1マスにしか無い・列全部が今燃えるのか分からない」】床の仕掛けの、札の無いマスにも小さな「今」「次」
            var floorRest = threatsShown.Where(t => t.Source == null && (t.Timing == ThreatTiming.Floor || t.Timing == ThreatTiming.NextFloor) && t.Cells.Contains(c) && pillCells.TryGetValue(t, out var pc1) && !pc1.Contains(c)).ToList();
            if (items.Count == 0 && floorRest.Count == 0) return;
            const float h = PillH;
            float x = cell.x + 2f, y = cell.y + 2f;
            int shown = 0;
            foreach (var t in floorRest.Where(t => !items.Contains(t)).Take(1))
            {
                bool nx = t.Timing == ThreatTiming.NextFloor;
                if (pillTabSt == null) pillTabSt = new GUIStyle(Tiny) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
                var tr = new Rect(x, y, 14f, 16f);
                DrawRect(tr, nx ? new Color(0.17f, 0.19f, 0.25f, 0.7f * alpha) : new Color(0.32f, 0.3f, 0.26f, 0.92f * alpha) /* 【RC11・UI/UX「オレンジは敵が自分で動く先だけに」】床の「今」は石の色 */);
                if (nx) DrawDashedFrame(tr, new Color(1f, 1f, 1f, 0.55f * alpha), 1f, 3f); else DrawFrame(tr, new Color(1f, 1f, 1f, 0.6f * alpha), 1f);
                GUI.Label(tr, nx ? "次" : "今", new GUIStyle(pillTabSt) { fontSize = 10, normal = { textColor = nx ? new Color(0.85f, 0.88f, 0.95f, 0.85f * alpha) : new Color(1f, 1f, 1f, alpha) } });
                x += 16f;
            }
            foreach (var t in items)
            {
                if (shown >= 3)
                {
                    ShadowLabel(new Rect(x, y, 14f, h), "+", new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(1f, 1f, 1f, alpha));
                    break;
                }
                bool small = compact; // 【RC9・初見テスト2回目「数字が1マスにしか無い・数字の無いマスは何ダメ？」】撃つ敵のある攻撃は、どのマスも［記号 ダメージ］（そろえる）
                float w = PillWidth(t, small);
                if (x + w > cell.xMax - 2f && x > cell.x + 2f) { x = cell.x + 2f; y += h + 1f; }
                float pa = alpha * (focus >= 0 && t.Source != null && t.SourceId != focus ? 0.55f : 1f);
                DrawPill(new Rect(x, y, w, h), t, pa, small);
                x += w + 2f;
                shown++;
            }
        }

        static GUIStyle pillNumSt, pillTabSt, pillEnemySt;
        /// <summary>【第2巡 12】攻撃の札の高さ（16 → 18）。</summary>
        const float PillH = 18f;
        /// <summary>【第2巡 12「桃の点線は赤の上で見えない」】撃つ敵のある攻撃の札：敵の色で塗った札に黒い字「A 2」（少し大きく）。</summary>
        static GUIStyle PillEnemySt() => pillEnemySt ?? (pillEnemySt = new GUIStyle(Tiny) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) });
        static string PillEnemyText(Threat t, bool small) => small ? t.Source.KindLetter : $"{t.Source.KindLetter} {t.Damage}";

        /// <summary>【通常リリース版・画面の直し 24】床の仕掛けの札の字：岩（落石）・光（レーザー）・炎。</summary>
        string FloorShortName() { string n = FloorName(); return n == "落石" ? "岩" : n == "光線" ? "光" : "炎"; }
        /// <summary>【通常リリース版・画面の直し 24】床の仕掛けの札の数字は「4ダメ」（「+4」と読まれた）。</summary>
        string FloorPillNumber(Threat t) => pillFloorShort ? t.Damage.ToString() : $"{t.Damage}ダメ";
        /// <summary>【画面の直し 24】敵のいるマスでは「ダメ」を省く（右上の 全・技・移 の印にかぶった）。DrawThreatPills が決める。</summary>
        bool pillFloorShort;
        static GUIStyle pillFloorNumSt;
        /// <summary>【画面の直し 24】床の札の「4ダメ」は少し小さく（小さいマスでもはみ出さない）。</summary>
        static GUIStyle PillFloorNumSt() => pillFloorNumSt ?? (pillFloorNumSt = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) });

        /// <summary>【RC9】札1枚の幅（記号＋ダメージ。small＝記号だけ。三角は無し）。</summary>
        float PillWidth(Threat t, bool small = false)
        {
            if (pillNumSt == null) pillNumSt = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            if (t.Source != null) return PillEnemySt().CalcSize(new GUIContent(PillEnemyText(t, small))).x + 9f; // 【第2巡 12】
            bool next = t.Timing == ThreatTiming.NextFloor || t.Timing == ThreatTiming.Floor; // 【RC10】床は今・次の札
            string num = small ? "" : t.Source == null ? FloorPillNumber(t) : t.Damage.ToString(); // 【RC10・初見テスト2回目「B★5 の5はダメージ？ ★の数字＝あと何ターンと食い違う」】★は「大技まで あと何ターン」だけ。大技の札は赤紫の地で分ける
            float numW = small ? 0f : (t.Source == null ? PillFloorNumSt() : pillNumSt).CalcSize(new GUIContent(num)).x;
            return 2f + (next ? 11f : 0f) + (t.Source == null ? 12f : 14f) + (small ? 0f : numW + 3f) + 2f;
        }

        /// <summary>【RC9】札1枚：②の赤＝暗い赤・④大技＝赤紫と★・⑥床＝絵（次のターンは薄い点線の「次」）。</summary>
        void DrawPill(Rect pr, Threat t, float pa, bool small)
        {
            if (pillNumSt == null) PillWidth(t);
            if (pillTabSt == null) pillTabSt = new GUIStyle(Tiny) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            bool next = t.Timing == ThreatTiming.NextFloor, big = t.Timing == ThreatTiming.BigAttack, floor = t.Timing == ThreatTiming.Floor || next;
            if (t.Source != null)
            {
                // 【第2巡 12】敵の色で塗った札に黒い字。種類は縁で：赤（敵と動く）＝黒の点線／紫（固定）＝黒の実線と斜線
                var ec = EnemyIdColor(t.Source);
                DrawRect(pr, new Color(ec.r, ec.g, ec.b, 0.97f * pa));
                if (t.Kind == ThreatKind.Fixed) { DrawHatch(new Rect(pr.x - 1f, pr.y - 1f, pr.width + 2f, pr.height + 2f), 0.4f * pa); DrawFrame(pr, new Color(0.05f, 0.02f, 0.08f, 0.95f * pa), 1.5f); }
                else DrawDashedFrame(pr, new Color(0.05f, 0.02f, 0.02f, 0.95f * pa), 1.5f, 3f);
                GUI.Label(pr, PillEnemyText(t, small), new GUIStyle(PillEnemySt()) { normal = { textColor = new Color(0.03f, 0.03f, 0.05f, pa) } });
                return;
            }
            if (next) pa *= 0.75f;
            float h = pr.height;
            var bg = big || t.Kind == ThreatKind.Fixed ? new Color(0.42f, 0.04f, 0.38f) : next ? new Color(0.17f, 0.19f, 0.25f) : floor ? new Color(0.32f, 0.3f, 0.26f) : new Color(0.4f, 0.04f, 0.04f); // 【RC11】固定は紫 // 【RC10・初見テスト「今の4と次の4の違いが小さい」】今＝橙・次＝灰色
            DrawRect(pr, new Color(bg.r, bg.g, bg.b, 0.93f * pa));
            // 【RC9・3回目「紫の床・自分の絵の上では点線と斜線が見分けにくい」】札そのものにも種類：固定＝斜線・敵と動く＝撃つ敵の色の点線の縁
            if (t.Kind == ThreatKind.Fixed) DrawHatch(new Rect(pr.x - 2f, pr.y - 2f, pr.width + 4f, pr.height + 4f), 0.55f * pa);
            if (next) DrawDashedFrame(pr, new Color(1f, 1f, 1f, 0.7f * pa), 1f, 3f);
            else if (t.Kind == ThreatKind.Follow && t.Source != null) { var sc = EnemyIdColor(t.Source); DrawDashedFrame(pr, new Color(sc.r, sc.g, sc.b, 0.95f * pa), 1.5f, 3f); }
            else DrawFrame(pr, big ? new Color(1f, 0.55f, 0.95f, 0.9f * pa) : new Color(1f, 1f, 1f, 0.7f * pa), 1f);
            float px = pr.x + 2f;
            if (floor) { GUI.Label(new Rect(px, pr.y, 11f, h), next ? "次" : "今", new GUIStyle(pillTabSt) { normal = { textColor = next ? new Color(0.85f, 0.88f, 0.95f, pa) : new Color(1f, 1f, 1f, pa) } }); px += 11f; }
            var lead = new Rect(px, pr.y + 1f, t.Source == null ? 12f : 14f, 14f);
            if (t.Source != null) DrawLetterBadge(lead, t.Source, pa);
            else // 【通常リリース版・画面の直し 24「落石と炎の小さな絵が見分けられない・数字の意味が分からない」】絵の代わりに字（岩・炎・光）
                GUI.Label(lead, FloorShortName(), new GUIStyle(pillTabSt) { fontSize = 11, normal = { textColor = new Color(1f, 0.82f, 0.55f, pa) } });
            px += lead.width;
            if (!small)
            {
                string num = t.Source == null ? FloorPillNumber(t) : t.Damage.ToString();
                var ns = t.Source == null ? PillFloorNumSt() : pillNumSt;
                float numW = ns.CalcSize(new GUIContent(num)).x;
                var tc = big ? new Color(1f, 0.85f, 1f, pa) : floor ? new Color(1f, 0.93f, 0.75f, pa) : new Color(1f, 0.9f, 0.82f, pa);
                GUI.Label(new Rect(px + 1f, pr.y, numW + 2f, h), num, new GUIStyle(ns) { normal = { textColor = tc } });
            }
        }

        /// <summary>【RC9・初見テスト「まね方が上の欄の小さな字にしか無い」】敵のマスの下の札に、まね方の印（全＝全部・技＝攻撃と押し引き・移＝移動だけ）。まねない敵には出さない。</summary>
        void DrawCopyKindMark(Rect r, UnitModel m)
        {
            if (!turn.Grid.Model.CopyEnabled || m.CopyKind == CopyKind.None || m.IsObject) return;
            if (HideCopyKindNames) return; // 【RC12・初見テスト4回目「1問目の敵にも『全』の印：まねる と 全まね の違いが分からない」】名前は3問目から
            bool off = CopyCore.Stunned(m);
            string g = m.CopyKind == CopyKind.All ? "全" : m.CopyKind == CopyKind.Technique ? "技" : "移";
            DrawRect(r, new Color(0.02f, 0.2f, 0.26f, off ? 0.5f : 0.92f));
            DrawFrame(r, new Color(0.4f, 0.95f, 1f, off ? 0.35f : 0.85f), 1f);
            GUI.Label(r, g, new GUIStyle(Tiny) { fontSize = Mathf.Max(9, Mathf.RoundToInt(r.height * 0.72f)), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color(0.6f, 1f, 1f, off ? 0.45f : 1f) } });
            if (off) DrawRect(new Rect(r.x - 1f, r.center.y - 1f, r.width + 2f, 2f), new Color(1f, 0.35f, 0.3f, 0.95f)); // 【RC11・QA「上の欄は今はまねないのに、盤面は全のまま」】今はまねない＝取り消し線
            if (r.Contains(Event.current.mousePosition)) { cardTipTitle = $"{PhaseRules.ShortName(m)}：{CopyKindPlain(m.CopyKind)}"; cardTipBody = "まね方：全＝あなたの命令を全部まねる／技＝攻撃・押す・引くだけ／移＝移動だけ。まねる敵は、それぞれの位置から同じ向きに。順番は A が全部 → B が全部 …"; }
        }

        /// <summary>【RC9】「固定」：床に決まった攻撃（赤の実線・赤紫の大技）1つに1つだけ。空いたマスのうち画面でいちばん下・左のマスの下の端。</summary>
        void DrawFixedLabels(System.Func<Vector2Int, Rect> CellRect, int focus)
        {
            var model = turn.Grid.Model;
            var used = new HashSet<Vector2Int>();
            foreach (var t in threatsShown)
            {
                if (t.Kind != ThreatKind.Fixed || t.Cells.Count == 0 || (t.Timing != ThreatTiming.Attack && t.Timing != ThreatTiming.BigAttack)) continue;
                var free = t.Cells.Where(c => model.GetUnitAt(c) == null && !used.Contains(c)).ToList();
                if (free.Count == 0) continue;
                var anchor = free.OrderByDescending(c => CellRect(c).y).ThenBy(c => CellRect(c).x).First();
                used.Add(anchor);
                var r = CellRect(anchor);
                bool dim = (focus >= 0 && t.SourceId != focus) || (hoverStep >= 0 && !StepMatches(hoverStep, t));
                float a = dim ? 0.3f : 1f;
                var fr = new Rect(r.x + 3, r.yMax - 17, 34, 14);
                DrawRect(fr, new Color(0.08f, 0.08f, 0.1f, 0.88f * a));
                DrawFrame(fr, new Color(0.9f, 0.9f, 0.95f, 0.8f * a), 1f);
                GUI.Label(fr, "固定", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, a) } });
            }
        }

        /// <summary>【RC8】マスの右上の数字の札（RC9 からは使わない：DrawThreatPills）。</summary>
        Vector2Int cellOf;
        void DrawThreatChip(Rect cell, CellThreat ct, float alpha, int focus)
        {
            bool now = ct.Dmg > 0;
            int dmg = now ? ct.Dmg : ct.NextDmg;
            var numSt = new GUIStyle(Tiny) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            var unitSt = new GUIStyle(Tiny) { fontSize = 9, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            string num = dmg.ToString();
            float numW = numSt.CalcSize(new GUIContent(num)).x, unitW = unitSt.CalcSize(new GUIContent("ダメ")).x;
            float tabW = now ? 0f : 13f;
            float w = tabW + numW + unitW + 6f;
            var chip = new Rect(cell.xMax - w - 2f, cell.y + 2f, w, 18f);
            Color edge = ct.Big || (ct.FixedRed && !ct.FollowRed) ? new Color(1f, 0.45f, 0.95f) : ct.Red ? new Color(1f, 0.55f, 0.48f) : new Color(0.92f, 0.92f, 0.86f);
            DrawRect(chip, new Color(0f, 0f, 0f, (now ? 0.85f : 0.55f) * alpha));
            if (now) DrawFrame(chip, new Color(edge.r, edge.g, edge.b, alpha), ct.Big ? 2f : 1.5f);
            else DrawDashedFrame(chip, new Color(edge.r, edge.g, edge.b, 0.7f * alpha), 1f, 3f);
            if (!now) GUI.Label(new Rect(chip.x, chip.y, tabW, chip.height), "次", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.85f, 0.88f, 0.95f, alpha) } });
            var col = ct.Big || (ct.FixedRed && !ct.FollowRed) ? new Color(1f, 0.82f, 1f) : ct.Red ? new Color(1f, 0.86f, 0.72f) : new Color(0.96f, 0.96f, 0.9f);
            var shown = new Color(col.r, col.g, col.b, (now ? 1f : 0.75f) * alpha);
            var numRect = new Rect(chip.x + tabW + 2f, chip.y, numW, chip.height);
            GUI.Label(numRect, num, new GUIStyle(numSt) { normal = { textColor = shown } });
            GUI.Label(new Rect(numRect.xMax + 1f, numRect.y + 1f, unitW + 2f, numRect.height), "ダメ", new GUIStyle(unitSt) { normal = { textColor = shown } });
            // 撃つ敵の記号（左上）。3体以上は2つ＋「+」
            if (now && ct.Sources != null && ct.Sources.Count > 0)
            {
                float s = 15f;
                var srcs = ct.Sources.OrderBy(u => u.KindLetter).ToList();
                for (int i = 0; i < Mathf.Min(2, srcs.Count); i++)
                {
                    bool twoRows = srcs.Count >= 2 && chip.x < cell.x + 2f + 2 * (s + 11f); // 【RC8・初見テスト3「B▶C▼ の札が数字に重なる」】
                    var br = new Rect(cell.x + 2f + i * (s + 11f), cell.y + 2f + (twoRows ? 20f : 0f), s, s);
                    float ba = (focus >= 0 && srcs[i].Id != focus ? 0.4f : 1f) * alpha;
                    DrawLetterBadge(br, srcs[i], ba);
                    // 【RC8・初見テスト「固定か動くかが分からない」】敵と一緒に動く攻撃は、記号の横に「撃つ敵の方向」の三角（この攻撃はその敵にくっついている）
                    var src = srcs[i];
                    if (threatsShown.Any(t => t.SourceId == src.Id && t.Kind == ThreatKind.Follow && t.Cells.Contains(cellOf)))
                    {
                        var d = src.Position - cellOf;
                        string g = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? (d.x > 0 ? "▶" : "◀") : (d.y > 0 ? "▲" : "▼");
                        var gc = EnemyIdColor(src);
                        ShadowLabel(new Rect(br.xMax, br.y - 1f, 11f, s + 2f), g, new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, new Color(gc.r, gc.g, gc.b, ba));
                    }
                }
                if (srcs.Count > 2) ShadowLabel(new Rect(cell.x + 2f + 2 * (s + 1f), cell.y + 2f, 12f, s), "+", new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold }, Color.white);
            }
        }

        /// <summary>マウスを乗せたマスの説明（右下の欄）：だれの何が・何ダメ・どうすれば消える／ずれるか。</summary>
        string CellThreatText(Vector2Int cell)
        {
            var lines = new List<string>();
            foreach (var t in threatsShown.Where(t => t.Cells.Contains(cell)))
            {
                string who = t.Source != null ? PhaseRules.ShortName(t.Source) + "の" : "";
                string kind = t.Kind == ThreatKind.Follow ? "敵と一緒に動く：押す・引くでずれる／倒せば消える"
                            : t.Kind == ThreatKind.Fixed ? (t.Timing == ThreatTiming.BigAttack ? "大技：床に決まっている（押しても動かない）／撃つ敵を倒せば消える・敵にも当たる" : "床に決まっている（押しても動かない）／撃つ敵を倒せば消える")
                            : "床の仕掛け（誰のものでもない・敵にも当たる）";
                string when = t.Timing == ThreatTiming.Attack ? "②" : t.Timing == ThreatTiming.BigAttack ? "④" : t.Timing == ThreatTiming.Floor ? "⑥" : "次のターン";
                lines.Add($"{when} {who}{(t.Source != null ? t.Name : FloorName())} {t.Damage}ダメ　…{kind}");
            }
            return string.Join("\n", lines);
        }

        string FloorName()
        {
            string hz = turn.CurrentStage != null ? turn.CurrentStage.hazardName ?? "" : "";
            return hz.Contains("落石") ? "落石" : hz.Contains("レーザー") ? "光線" : "炎";
        }

        /// <summary>床の仕掛けの絵（炎・落石・光線・回復）と、床に決まった赤の照準。【RC8・本人「岩は通ったらダメージを食らいそうに見えない」】落石：次＝大きな影と落ちてくる岩・今＝岩とヒビと土煙。</summary>
        void DrawFloorArtV2(System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            var env = model.Environment;
            var occ = new HashSet<Vector2Int>(model.Units.Where(u => u.IsAlive).Select(u => u.Position));
            string hz = turn.CurrentStage != null ? turn.CurrentStage.hazardName ?? "" : "";
            bool laser = hz.Contains("レーザー"), rockfall = hz.Contains("落石");
            bool showNow = turn.State == BattleState.PlayerInput || turn.Step < TurnManager.TurnStep.Floor; // 床の段のあとは次のターンの床（ラウンドが進んでいる）
            if (ShowingBattle && env.HasFire)
            {
                var now = env.FireCellsNow.ToList();
                var next = env.FireCellsNext.Where(c => !env.FireCellsNow.Contains(c)).ToList();
                if (laser)
                {
                    foreach (var row in next.GroupBy(c => c.y))
                    {
                        var xs = row.Select(c => CellRect(c)).ToList();
                        float x0 = xs.Min(r => r.x), x1 = xs.Max(r => r.xMax), cy = xs[0].center.y;
                        DrawRect(new Rect(x0, cy - 2, x1 - x0, 4), new Color(1f, 0.92f, 0.65f, 0.55f));
                        DrawDashedFrame(new Rect(x0 + 2, cy - xs[0].height * 0.28f, x1 - x0 - 4, xs[0].height * 0.56f), new Color(1f, 0.92f, 0.7f, 0.5f), 1.5f, 6f);
                    }
                    if (showNow)
                        foreach (var row in now.GroupBy(c => c.y))
                        {
                            var xs = row.Select(c => CellRect(c)).ToList();
                            float x0 = xs.Min(r => r.x), x1 = xs.Max(r => r.xMax), cy = xs[0].center.y, hh = xs[0].height * 0.36f;
                            float fl = Blink(0.75f, 1f, 22f);
                            DrawRect(new Rect(x0, cy - hh, x1 - x0, hh * 2), new Color(1f, 0.92f, 0.6f, 0.32f * fl));
                            DrawRect(new Rect(x0, cy - hh * 0.55f, x1 - x0, hh * 1.1f), new Color(1f, 0.97f, 0.8f, 0.8f * fl));
                            DrawRect(new Rect(x0, cy - hh * 0.2f, x1 - x0, hh * 0.4f), new Color(1f, 1f, 1f, 0.95f));
                            DrawUiImage(new Rect(x0 - 2, cy - xs[0].height * 0.3f, xs[0].height * 0.6f, xs[0].height * 0.6f), "hz_emitter", 1f, ScaleMode.ScaleToFit);
                        }
                }
                else
                {
                    string iconNow = rockfall ? "hz_rock_shadow" : "hz_flame", iconNext = rockfall ? "hz_rock_shadow" : "hz_ember"; // 【RC8・レビュー「今の岩は置かれた障害物・次は穴に見える」】このターン＝落ちてくる岩と影（落ちた岩は床の段の動きだけ）・次のターン＝薄い影
                    foreach (var c in next)
                    {
                        if (model.Board.IsWall(c) || !showNow) continue; // 床の段のあとは、もう次のターン（ラウンドが進んだ）
                        var r = CellRect(c);
                        if (rockfall) DrawRect(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(0.15f, 0.08f, 0.05f, 0.32f));
                        var gc = GUI.color;
                        if (rockfall) GUI.color = new Color(gc.r * 0.62f, gc.g * 0.64f, gc.b * 0.7f, gc.a); // 【通常リリース版・画面の直し 24】次の落石の影は灰色に（次の炎の火種のオレンジの輪と見分ける）
                        HazardIcon(r, iconNext, rockfall ? 0.42f : 0.85f, occ.Contains(c));
                        GUI.color = gc;
                    }
                    if (showNow)
                        foreach (var c in now)
                        {
                            var r = CellRect(c);
                            float pulse = rockfall ? 1f : Blink(0.88f, 1f, 10f, c.x * 1.7f + c.y);
                            if (rockfall) DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(0.45f, 0.18f, 0.08f, 0.38f));
                            HazardIcon(r, iconNow, 1f, occ.Contains(c), pulse);
                            DrawFrame(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), rockfall ? new Color(0.92f, 0.88f, 0.8f, 0.95f) : new Color(1f, 0.55f, 0.1f, 0.9f), 2f); // 【RC8・初見テスト】岩の枠がオレンジで敵に見えた → 石の色
                        }
                }
            }
            if (ShowingBattle)
                foreach (var c in env.HealTiles)
                    HazardIcon(CellRect(c), "hz_heal", Blink(0.6f, 1f, 3f), occ.Contains(c));
            if (ShowingBattle && rockfall) DrawFallingRocks(CellRect);
            // 【RC8・初見テスト「黄色い枠のひし形の意味が分からない」】トゲ：マスの下に「トゲ3」（入るたび・敵も）
            if (ShowingBattle)
                foreach (var c in model.Board.Spikes)
                {
                    var r = CellRect(c);
                    var tr = new Rect(r.center.x - 20, r.yMax - 15, 40, 14);
                    DrawRect(tr, new Color(0.2f, 0.15f, 0.02f, 0.85f));
                    GUI.Label(tr, $"トゲ{model.Board.SpikeDamage}", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.85f, 0.4f) } });
                    if (r.Contains(Event.current.mousePosition) && pendingCommand == null && cardTipTitle == null) { cardTipTitle = "トゲ"; cardTipBody = $"入るたびに {model.Board.SpikeDamage}（敵も）。まねで敵を歩かせて踏ませられる。"; }
                }
        }

        /// <summary>
        /// 【RC8・本人「自分がどこにいるかすら分からない」】あなたの印：水色の太い枠（白の内枠）と頭上の「▼あなた HP」。危険の数字や名札をあなたのマスの真ん中に重ねない。
        /// 方向の矢印が出ている間は、上の矢印を隠さないように左上へ。
        /// </summary>
        void DrawPlayerMarkerV2(System.Func<Vector2Int, Rect> CellRect, System.Func<Vector3, Vector2> ToGui, bool arrowsShown)
        {
            var p = turn.Player;
            if (p == null || !p.IsAlive) return;
            var pos = ToGui(p.transform.position);
            var cr = CellRect(p.Model.Position);
            var r = new Rect(pos.x - cr.width / 2f, pos.y - cr.height / 2f, cr.width, cr.height); // 絵の位置（動いている間も追う）
            float a = Blink(0.75f, 1f, 4f);
            var cyan = new Color(0.35f, 0.92f, 1f, a);
            DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(0f, 0f, 0f, 0.6f), 1f);
            DrawFrame(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), cyan, 3f);
            DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(1f, 1f, 1f, 0.8f), 1f);
            DrawPlayerDangerV2(CellRect, ToGui);
            // 【第2巡 3「名札が上下に動く・危険のマスで札が顔にかぶる」】名札は いつも あなたのマスの下の端の中（敵の記号と HP と同じ置き場所）に小さく。
            //   攻撃の札は上の角（DrawPlayerDangerV2）。どちらも顔と体（真ん中）には重ねない。向きを選んでいる間も同じ場所（矢印はこの上に描く）
            string label = $"あなた {p.Hp}";
            var st = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            bool targeted = ThreatsOnYou().Count > 0;
            float w = Mathf.Min(r.width - 6f, st.CalcSize(new GUIContent(label)).x + 10f);
            var tag = new Rect(r.center.x - w / 2f, r.yMax - 17f, w, 15f);
            DrawRect(tag, targeted ? new Color(0.32f, 0.05f, 0.06f, 0.96f) : new Color(0.04f, 0.2f, 0.38f, 0.95f)); // 【RC9】あなたが狙われていると赤い札（専用の警告）
            DrawFrame(tag, cyan, 1f);
            GUI.Label(tag, label, Fit(new GUIStyle(st) { normal = { textColor = Color.white } }, label, tag.width - 2f, 9));
            playerTagRect = tag; playerTagCell = p.Model.Position;
            // 効いている強化（ガード・攻撃+）
            var model = turn.Grid.Model;
            var chips = new List<string>();
            if (model.PlayerGuard > 0) chips.Add($"ガード -{model.PlayerGuard}");
            if (p.Model.AttackBonus > 0) chips.Add($"攻撃 +{p.Model.AttackBonus}");
            for (int k = 0; k < chips.Count; k++)
            {
                var cc = new Rect(r.x - 8, r.yMax + 2 + k * 17, r.width + 16, 16);
                DrawRect(cc, new Color(0.04f, 0.2f, 0.12f, 0.92f));
                DrawFrame(cc, TacticGreen, 1f);
                GUI.Label(cc, chips[k], new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.7f, 1f, 0.8f) } });
            }
        }

        /// <summary>
        /// 【RC10・初見テスト2回目（5人中4人）「自分のマスが赤いかが、絵・名札・向きの矢印で隠れて見えない」】あなたのマスに攻撃が来るときは、
        /// マスの内側に赤い太い枠（点滅：設定で止まる）と、ほかのマスと同じ左上の札［A 2］［B 2］（1つずつ・合計は出さない：A30）。向きを選んでいる間は矢印の上に描く。
        /// </summary>
        internal void DrawPlayerDangerV2(System.Func<Vector2Int, Rect> CellRect, System.Func<Vector3, Vector2> ToGui, bool frameOnly = false)
        {
            var p = turn.Player;
            if (p == null || !p.IsAlive || turn.Grid.Model.Player == null) return;
            var onMe = ThreatsOnYou();
            if (onMe.Count == 0) return;
            var pos = ToGui(p.transform.position);
            var cr = CellRect(p.Model.Position);
            var r = new Rect(pos.x - cr.width / 2f, pos.y - cr.height / 2f, cr.width, cr.height);
            DrawFrame(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(1f, 0.22f, 0.18f, Blink(0.7f, 1f, 5f)), 3f);
            if (AnswerPreview || frameOnly) return; // 【RC9・Claude の通し】チュートリアルは「！3ダメ」（答え）があなたのマスに出る（札は同じことの繰り返し）
            // 【通常リリース版・画面の直し 4・5「自分のマスの A2・B2・名札が積み重なって自分が隠れる」】札はマスの角へ（1つ目＝左上・2つ目＝右上・3つ目＝左下・4つ目＝右下）。
            //   真ん中（あなたの顔と体）には重ねない。【RC10・初見テスト3回目「A2 B2 が縦に積まれて自分の絵を隠す」】
            // 【第2巡 3】札は上の端の角だけ（1つ目＝左上・2つ目＝右上・3つ目からは左上の札の右へ。入らなければ「+」）。下の端は名札
            float lx = r.x + 1f, rx = r.xMax - 1f;
            for (int i = 0; i < onMe.Count; i++)
            {
                var t = onMe[i];
                float pw = PillWidth(t);
                bool right = i == 1;
                if (!right && i > 0 && lx + pw > rx - 2f) { ShadowLabel(new Rect(lx, r.y + 1f, 14f, PillH), "+", new GUIStyle(Tiny) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false }, Color.white); break; }
                var pr = right ? new Rect(rx - pw, r.y + 1f, pw, PillH) : new Rect(lx, r.y + 1f, pw, PillH);
                DrawPill(pr, t, 1f, false);
                if (right) rx = pr.x - 1f; else lx = pr.xMax + 1f;
            }
        }

        // ───── 倒れた敵の攻撃が消える・押した敵の攻撃がずれる（実行中）─────
        // 【RC9・本人「未来の答えを消す代わりに、実行の瞬間をはっきり」】予告で「倒すと消える」「ずれる」と書かない代わりに、起きた瞬間を見せる：
        //   倒れた → その敵の攻撃が縮んで消え「攻撃が消えた」／押した・引いた → 前の場所に薄い枠が残って消え、新しい場所が光り「ずれた」
        readonly Dictionary<int, List<Vector2Int>> lastThreatCells = new Dictionary<int, List<Vector2Int>>();
        readonly Dictionary<int, Vector2Int> lastSourcePos = new Dictionary<int, Vector2Int>();
        readonly List<(List<Vector2Int> cells, float at, Color col, int kind)> fadingThreats = new List<(List<Vector2Int>, float, Color, int)>(); // kind 0＝消えた・1＝前の場所・2＝新しい場所

        /// <summary>【RC11・初見テスト「右の端の『Dの攻撃が消え…』が燭台に重なって切れる」】一言は端の列に出さない（1列内側へ）。</summary>
        Vector2Int InsideCol(Vector2Int c) => new Vector2Int(Mathf.Clamp(c.x, 1, Mathf.Max(1, turn.Grid.Model.Board.Width - 2)), c.y);

        void DrawFadingThreats(System.Func<Vector2Int, Rect> CellRect, BattleModel model)
        {
            if (turn.State != BattleState.PlayerInput) ComputeThreatsV2(); // 今の盤面から作り直す（Update より先に OnGUI が来ると、位置だけ新しく危険が古いままで「ずれた」を見落とした）
            var now = new Dictionary<int, List<Vector2Int>>();
            // 入力中：ターンの初めの盤面の危険を覚えておく（実行の1つ目の命令は、最初の描画より先に盤面を変えることがある：RC9 の撮影で「ずれた」が出なかった）
            foreach (var t in turn.State == BattleState.PlayerInput ? threatsBase : threatsShown)
                if (t.Source != null && t.Timing == ThreatTiming.Attack) { if (!now.TryGetValue(t.SourceId, out var l)) now[t.SourceId] = l = new List<Vector2Int>(); l.AddRange(t.Cells); }
            if (turn.State == BattleState.PlayerInput)
            {
                fadingThreats.Clear();
                lastThreatCells.Clear(); foreach (var kv in now) lastThreatCells[kv.Key] = kv.Value;
                lastSourcePos.Clear(); foreach (var e in model.Enemies) if (e.IsAlive) lastSourcePos[e.Id] = e.Position;
                return;
            }
            foreach (var kv in lastThreatCells)
            {
                var u = model.UnitById(kv.Key);
                if (u == null) continue;
                var col = EnemyIdColor(u);
                if (!now.TryGetValue(kv.Key, out var cur))
                {
                    if (!u.IsAlive)
                    {
                        fadingThreats.Add((kv.Value, Time.unscaledTime, col, 0)); // その敵が倒れた
                        var mid = kv.Value[kv.Value.Count / 2];
                        DamagePopup.Spawn(turn.Grid.GridToWorld(InsideCol(mid)) + new Vector3(0f, 0.2f, 0f), $"{u.KindLetter}の攻撃が消えた", new Color(1f, 0.85f, 0.7f));
                    }
                    continue;
                }
                // 撃つ敵が動いて（あなたの押す・引く・まね）、攻撃の場所が変わった
                if (turn.Step == TurnManager.TurnStep.You && u.IsAlive && lastSourcePos.TryGetValue(kv.Key, out var p0) && p0 != u.Position && !(cur.Count == kv.Value.Count && cur.All(kv.Value.Contains)))
                {
                    fadingThreats.Add((kv.Value, Time.unscaledTime, col, 1));
                    fadingThreats.Add((cur.ToList(), Time.unscaledTime, col, 2));
                    DamagePopup.Spawn(turn.Grid.GridToWorld(InsideCol(u.Position)) + new Vector3(0f, 0.62f, 0f), $"{u.KindLetter}の攻撃がずれた", new Color(1f, 0.8f, 0.6f));
                }
            }
            lastThreatCells.Clear();
            foreach (var kv in now) lastThreatCells[kv.Key] = kv.Value;
            lastSourcePos.Clear();
            foreach (var e in model.Enemies) if (e.IsAlive) lastSourcePos[e.Id] = e.Position;
            fadingThreats.RemoveAll(f => Time.unscaledTime - f.at > (f.kind == 0 ? 0.7f : 0.9f));
            foreach (var (cells, at, col, kind) in fadingThreats)
            {
                float k = (Time.unscaledTime - at) / (kind == 0 ? 0.7f : 0.9f);
                foreach (var c in cells)
                {
                    var r = CellRect(c);
                    if (kind == 0)
                    {
                        float g = 4f + k * 12f;
                        DrawRect(new Rect(r.x + g, r.y + g, r.width - g * 2, r.height - g * 2), new Color(1f, 0.3f, 0.2f, 0.4f * (1f - k)));
                        DrawFrame(new Rect(r.x + g, r.y + g, r.width - g * 2, r.height - g * 2), new Color(1f, 0.85f, 0.7f, 0.95f * (1f - k)), 2f);
                    }
                    else if (kind == 1) DrawDashedFrame(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), new Color(col.r, col.g, col.b, 0.8f * (1f - k)), 2f, 5f);
                    else DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(1f, 1f, 1f, 0.9f * (1f - k)), 3f);
                }
            }
        }

        /// <summary>【RC8】「▼あなた」の札の場所（このフレーム）。あなたが受ける「！」は、同じマスならこの札の右に並べる（重ねない）。</summary>
        internal Rect playerTagRect;
        Vector2Int playerTagCell = new Vector2Int(-99, -99);

        // ───── 落石の段：岩が上から落ちる（読まなくても「ここに落ちる」が分かるように） ─────
        float floorStepAt = -10f;
        bool floorShook;

        void DrawFallingRocks(System.Func<Vector2Int, Rect> CellRect)
        {
            var env = turn.Grid.Model.Environment;
            if (turn.Step != TurnManager.TurnStep.Floor || turn.State == BattleState.PlayerInput) { floorStepAt = -10f; floorShook = false; return; }
            if (floorStepAt < 0f) floorStepAt = Time.unscaledTime;
            float t = Time.unscaledTime - floorStepAt;
            var fell = env.FireCells(env.Round - 1);
            foreach (var c in fell)
            {
                var r = CellRect(c);
                float k = Mathf.Clamp01(t / 0.22f);
                float y = Mathf.Lerp(r.y - r.height * 2.2f, r.y + 2f, k * k);
                DrawUiImage(new Rect(r.x + r.width * 0.12f, y, r.width * 0.76f, r.height * 0.76f), "hz_rock", 1f, ScaleMode.ScaleToFit);
                if (k >= 1f && t < 0.6f)
                {
                    float d = (t - 0.22f) / 0.38f;
                    DrawRect(new Rect(r.x - d * 10f, r.yMax - 14f, r.width + d * 20f, 10f), new Color(0.85f, 0.8f, 0.72f, 0.6f * (1f - d)));
                }
            }
            if (t >= 0.22f && !floorShook && fell.Count > 0) { floorShook = true; ScreenFx.Shake(0.15f, 0.25f); }
        }

        /// <summary>
        /// 【RC8】右下の欄の初めの文（何も指していないとき）。【通常リリース版・画面の直し 2「説明が同時に5〜6か所」】決まり文句（「…マウスを乗せると、ここに説明が出ます」・読み方の3行）は出さない：
        /// 右下は「前のターンに起きたこと」だけ（無ければ空）。読み方は 盤面の下の凡例・L の凡例・ヒント・チュートリアルにある。
        /// </summary>
        string IdleGuideV2() => "";

        /// <summary>【RC8】負けた理由（最後のターンにあなたが受けたダメージ・原因ごと）。結果の欄に出す。</summary>
        string DeathCauseText()
        {
            var ev = turn.BattleEvents;
            if (ev == null || ev.Count == 0) return null;
            var hits = new List<BattleEvent>();
            for (int i = Mathf.Clamp(turn.TurnEventStart, 0, ev.Count); i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0) hits.Add(e);
            }
            if (hits.Count == 0) return null;
            var groups = hits.GroupBy(TurnForecast.CauseText).Select(g => $"{g.Key} {g.Sum(x => x.Amount)}").ToList();
            return $"最後のターンに受けた {hits.Sum(x => x.Amount)}：" + string.Join("・", groups);
        }

        /// <summary>右下の1行：キー。</summary>
        void DrawKeyLineV2(Rect r)
        {
            if (FestivalUi || CleanCapture) return; // 【文化祭・初見レビュー「説明が3か所にある」】右下の「やること」に1つ
            if (QuickUi) // 【RC11】5分体験は短く
            {
                string qk = "札をクリック → 盤面の矢印で向き → 実行（Enter）・R やり直す・L 凡例";
                GUI.Label(r, qk, Fit(new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, 0.8f) } }, qk, r.width, 9));
                return;
            }
            string keys = "Enter 実行・BS 戻す・L 凡例とキー・O メニュー"; // 【通常リリース版・画面の直し 2】短く（全部のキーは凡例 L の中）
            var ks = new GUIStyle(Tiny) { fontSize = 12, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, 0.72f) } };
            GUI.Label(r, keys, Fit(ks, keys, r.width, 9));
        }

        // ───────── 【RC9・本人「ボスで何をすればいいか、説明文なしでもある程度」】段階ごとの狙いを絵で ─────────
        //   段階1＝主の四隅に金の角（狙う相手）／段階2＝起きた写し身から主へ水色の矢印（写し身のまねで主を斬る）／段階3＝核2つを金の点線で結び「同時」

        void DrawBossPhaseGuide(System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            var lord = model.Enemies.FirstOrDefault(e => e.IsAlive && e.Kind == EnemyKind.MirrorLord);
            if (lord == null) return;
            // 【RC10・ChatGPT「段階1→2→3で見た目と音に差」】盤面のふちの色：段階2＝写し身の水色・段階3＝核の金（段階1は無し）。実行中も出す
            if (lord.BossStage >= 1)
            {
                var c0 = CellRect(new Vector2Int(0, 0)); var c1 = CellRect(new Vector2Int(model.Board.Width - 1, model.Board.Height - 1));
                var br = Rect.MinMaxRect(Mathf.Min(c0.x, c1.x) - 6, Mathf.Min(c0.y, c1.y) - 6, Mathf.Max(c0.xMax, c1.xMax) + 6, Mathf.Max(c0.yMax, c1.yMax) + 6);
                var ec = lord.BossStage == 1 ? new Color(0.85f, 0.88f, 0.95f, 0.5f * Blink(0.6f, 1f, 2f)) : new Color(1f, 0.82f, 0.35f, 0.6f * Blink(0.6f, 1f, 2f)); // 【第2巡】段階2のふちは銀（水色は あなた と まね だけ：写し身も銀のガラス）
                DrawFrame(br, ec, 4f);
                DrawFrame(new Rect(br.x - 4, br.y - 4, br.width + 8, br.height + 8), new Color(ec.r, ec.g, ec.b, ec.a * 0.4f), 3f);
            }
            if (turn.State != BattleState.PlayerInput) return;
            var lr = CellRect(lord.Position);
            float pulse = Blink(0.55f, 1f, 4f);
            if (lord.BossStage <= 1) TargetCorners(lr, new Color(1f, 0.85f, 0.35f, pulse));
            if (lord.BossStage == 1 && false) // 【RC9・初見テスト2回目「写し身から主への点線の意味が分からない／答えのヒントに見える」】やめた（段階2の狙いは目的の1行と主の角）
            {
                foreach (var c in model.Enemies.Where(e => e.IsAlive && e.Kind == EnemyKind.HealClone && !e.Dormant))
                {
                    var cr = CellRect(c.Position);
                    var a = cr.center; var b = lr.center;
                    var dir = (b - a).normalized;
                    var from = a + dir * cr.width * 0.42f; var to = b - dir * lr.width * 0.46f;
                    int n = Mathf.Max(3, Mathf.RoundToInt(Vector2.Distance(from, to) / 9f));
                    for (int k = 0; k <= n; k++) { var q = Vector2.Lerp(from, to, k / (float)n); DrawRect(new Rect(q.x - 2.5f, q.y - 2.5f, 5f, 5f), new Color(0.4f, 0.95f, 1f, 0.9f * pulse)); }
                    string g = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y) ? (dir.x > 0 ? "▶" : "◀") : (dir.y > 0 ? "▼" : "▲");
                    ShadowLabel(new Rect(to.x - 10, to.y - 11, 20, 22), g, new GUIStyle(Big) { fontSize = 16, alignment = TextAnchor.MiddleCenter }, new Color(0.45f, 0.95f, 1f, pulse));
                }
            }
            if (lord.BossStage >= 2 && !CombatBoss.CoresAsleep(model))
            {
                var cores = model.Board.Cores.ToList();
                if (cores.Count >= 2)
                {
                    var a = CellRect(cores[0]).center; var b = CellRect(cores[1]).center;
                    var dir = (b - a).normalized; float rw = CellRect(cores[0]).width;
                    var from = a + dir * rw * 0.45f; var to = b - dir * rw * 0.45f;
                    int n = Mathf.Max(3, Mathf.RoundToInt(Vector2.Distance(from, to) / 10f));
                    for (int k = 0; k <= n; k++) { var q = Vector2.Lerp(from, to, k / (float)n); DrawRect(new Rect(q.x - 2f, q.y - 2f, 4f, 4f), new Color(1f, 0.85f, 0.35f, 0.85f * pulse)); }
                    var mid = (from + to) / 2f;
                    int hitN = model.Board.CoreHitsThisRound.Count();
                    var c0r = CellRect(cores[0]); var tag = new Rect(c0r.center.x - 27f, c0r.yMax - 17f, 54f, 16f); // 【RC11・初見テスト「同時 0/2 が主の絵と列の番号に重なる」】左の核のマスの下の端へ（前は主のマスの左上）
                    DrawRect(tag, new Color(0.25f, 0.18f, 0.02f, 0.92f));
                    DrawFrame(tag, new Color(1f, 0.85f, 0.35f, pulse), 1.5f);
                    GUI.Label(tag, $"同時 {hitN}/{cores.Count}", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.92f, 0.6f) } }); // 【RC9・初見テスト「叩いた 0/2 が核ごと2回か2つのうちいくつか分からない」】核の下の数字はやめ、結ぶ線の札に
                    foreach (var c in cores) TargetCorners(CellRect(c), new Color(0.85f, 0.95f, 1f, pulse)); // 【RC9・2回目「核の黄色い括弧が敵Aの黄色と同じ」】白
                }
            }
        }

        /// <summary>【RC9】チュートリアルだけ：押す・引く札の向きを試すと、動く敵の行き先に薄い絵と矢印（Before → After を盤面で）。</summary>
        void DrawPushGhost(PlayerPreview.Option o, Vector2Int to, System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            var start = PlayerPreview.AfterPlanned(model, turn.PlannedCommands, turn.PlayerAttackBonusEachTurn);
            var dir = o.Direction.HasValue ? o.Direction.Value.ToOffset() : Vector2Int.zero;
            UnitModel who = null;
            for (int k = 1; k <= 4 && who == null; k++)
            {
                var c = to - dir * k;
                var u = start.GetUnitAt(c);
                if (u != null && !u.IsPlayer) who = u;
                if (u == null) { var u2 = start.GetUnitAt(to + dir * k); if (u2 != null && !u2.IsPlayer) who = u2; }
            }
            if (who == null) return;
            var tr = CellRect(to);
            if (!DrawUiImage(new Rect(tr.x + tr.width * 0.15f, tr.y + tr.height * 0.12f, tr.width * 0.7f, tr.height * 0.7f), "portrait_" + KindKey(who), 0.5f))
                DrawRect(new Rect(tr.x + 8, tr.y + 8, tr.width - 16, tr.height - 16), new Color(1f, 1f, 1f, 0.2f));
            var lab = new Rect(tr.x + 2, tr.yMax - 17, tr.width - 4, 15);
            DrawRect(lab, new Color(0f, 0f, 0f, 0.7f));
            GUI.Label(lab, $"{who.KindLetter} はここへ", new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = Color.white } });
        }

        /// <summary>【通常リリース版・画面の直し 23】ボス・中ボスの本体の印（冠：3つの山と帯。字体に無い記号を使わず四角で描く）。</summary>
        internal static void DrawCrown(Rect r, Color c)
        {
            var dark = new Color(0f, 0f, 0f, 0.8f);
            float w = r.width, h = r.height, sw = w * 0.26f, band = h * 0.42f;
            for (int pass = 0; pass < 2; pass++)
            {
                var col = pass == 0 ? dark : c;
                float o = pass == 0 ? 1.2f : 0f, g = pass == 0 ? 1.2f : 0f;
                DrawRect(new Rect(r.x - g + o, r.yMax - band - g + o, w + g * 2, band + g * 2), col);
                for (int k = 0; k < 3; k++)
                {
                    float sx = r.x + k * (w - sw) / 2f;
                    float top = k == 1 ? r.y : r.y + h * 0.18f;
                    DrawRect(new Rect(sx - g + o, top - g + o, sw + g * 2, r.yMax - top + g * 2), col);
                }
            }
            DrawRect(new Rect(r.center.x - 1.5f, r.yMax - band * 0.7f, 3f, 3f), new Color(0.85f, 0.2f, 0.25f)); // 宝石
        }

        /// <summary>マスの四隅に L 字の角（狙う相手）。</summary>
        static void TargetCorners(Rect r, Color c)
        {
            float l = r.width * 0.26f, t = 3f, o = 2f;
            var x0 = r.x - o; var y0 = r.y - o; var x1 = r.xMax + o; var y1 = r.yMax + o;
            DrawRect(new Rect(x0, y0, l, t), c); DrawRect(new Rect(x0, y0, t, l), c);
            DrawRect(new Rect(x1 - l, y0, l, t), c); DrawRect(new Rect(x1 - t, y0, t, l), c);
            DrawRect(new Rect(x0, y1 - t, l, t), c); DrawRect(new Rect(x0, y1 - l, t, l), c);
            DrawRect(new Rect(x1 - l, y1 - t, l, t), c); DrawRect(new Rect(x1 - t, y1 - l, t, l), c);
        }

        // ───────── ボスの段階が変わった（帯） ─────────

        int phaseSeenSerial = -1, phaseSeen = -1;
        float phaseShownAt = -10f;
        string phaseTitle, phaseSub;

        /// <summary>【RC8・本人「核・眠り・写し身・段階…何をすればいいか分からない」】段階が変わった瞬間に、盤面の上へ大きく「段階2：写し身が目覚めた」と、することを1行。</summary>
        void DrawPhaseBanner(float bw)
        {
            if (!CombatV2) return;
            var lord = turn.Grid.Model.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.MirrorLord);
            if (lord == null) return;
            // 【RC11・本人「段階1・2で何をすればいいか分からない。普通に殴れちゃいそうでワクワクもない」】段階の始まり（段階1は戦闘の始め）に、
            //   画面の真ん中へ「第○段階」と、その段階のルールを2行（解き方は書かない：ルールは伝え、解き方は考えさせる）
            if (phaseSeenSerial != turn.BattleSerial) { phaseSeenSerial = turn.BattleSerial; phaseSeen = -1; phaseTitle = null; }
            if (lord.IsAlive && lord.BossStage != phaseSeen && turn.State == BattleState.PlayerInput)
            {
                phaseSeen = lord.BossStage;
                phaseShownAt = Time.unscaledTime;
                phaseTitle = PhaseIntroTitle(lord.BossStage);
                phaseSub = PhaseIntroRule(lord);
                if (lord.BossStage > 0) ScreenFx.Flash(new Color(1f, 0.85f, 0.4f, 0.3f), 0.45f); // 【第2巡 2】色は1つ（前は段階2が水色）
            }
            if (phaseTitle == null) return;
            float t = Time.unscaledTime - phaseShownAt;
            const float hold = 4.2f;
            if (t > hold) { phaseTitle = null; return; }
            if (t > 0.6f && Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.KeyDown)) { Event.current.Use(); phaseTitle = null; return; }
            float a = t < 0.25f ? t / 0.25f : t > hold - 0.25f ? 1f - (t - (hold - 0.25f)) / 0.25f : 1f; // 【RC11・初見テスト「帯が半透明で下の文字とぶつかる」】消えるのは最後の 0.25 秒だけ
            DrawTopBanner(bw, phaseTitle, phaseSub, a); // 【第2巡 2】盤面に重ねない：上の欄（あなたと敵・順番の帯）の所に2行・色は金の1つ
        }

        /// <summary>
        /// 【第2巡 2「段階の帯が盤面の上の段（主・核）を隠す／前は下の段で自分を隠した」】ボスの段階・中ボスとボスの登場の帯：盤面に重ねず、
        /// 盤面の上の欄（あなたと敵の欄・順番の帯：TopH）に重ねて2行（見出し＋ルール）。色は金の1つ（段階ごとの色はやめた）。数秒で消える。
        /// </summary>
        internal void DrawTopBanner(float bw, string title, string sub, float a)
        {
            var band = new Rect(0, 0, bw, TopH);
            DrawRect(band, new Color(0.02f, 0.02f, 0.05f, 0.97f * a));
            DrawRect(new Rect(0, band.yMax - 3, bw, 3), new Color(Gold.r, Gold.g, Gold.b, a));
            var ts = new GUIStyle(Big) { fontSize = 28, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            ShadowLabel(new Rect(8, band.y + 6, bw - 16, 40), title, Fit(ts, title, bw - 24, 18), new Color(Gold.r, Gold.g, Gold.b, a));
            if (string.IsNullOrEmpty(sub)) return;
            var rs = new GUIStyle(Small) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, a) } };
            GUI.Label(new Rect(8, band.y + 50, bw - 16, 32), sub, Fit(rs, sub, bw - 24, 12));
        }

        /// <summary>【RC11】段階の始まりの大きな見出し。</summary>
        public static string PhaseIntroTitle(int stage) => stage == 0 ? "第一段階　鏡の主" : stage == 1 ? "第二段階　鏡像、覚醒" : "第三段階　核の共鳴";

        /// <summary>【RC11】その段階のルール（1行・解き方は書かない）。</summary>
        public static string PhaseIntroRule(UnitModel lord) =>
            lord.BossStage == 0 ? $"主はあなたの命令を全部まねる。主に攻撃を当てて削れ（HP {CombatBoss.Floor(lord, 0)} で第二段階）"
            : lord.BossStage == 1 ? $"主は鏡の鎧（直接攻撃 -{RulesEngine.LordDirectGuard}）。写し身は鏡：主の光柱・大技が当たると主へはね返る（-{CombatBoss.CloneStrikeDamage}）" // 【通常リリース版】鏡の鎧【画面の直し 4】段階の帯は2行まで：1行に収まる言い方
            : $"鏡の鎧のまま。左右の核2つを同じターンに叩くと共鳴（1つ以上は敵のまねた攻撃で）：主に -{CombatBoss.CoreBossDamage}・1ターン止まる";

        /// <summary>【RC11】その段階の注意（小さく1行）。【通常リリース版・画面の直し 4】段階の帯は2行までにしたので帯には出さない（敵・像にマウスを乗せた説明にある）。</summary>
        public static string PhaseIntroNote(UnitModel lord) =>
            lord.BossStage == 0 ? "灰色の石像＝段階2で目覚める写し身・灰色のひし形＝段階3で目覚める核。眠っている間も通れない（攻撃も止まる）"
            : lord.BossStage == 1 ? "写し身は毎ターン主を回復する。主の十字（光柱）は1ターンおき。段階が変わったこのターンは、主はまねない"
            : "主を直接殴っても削れる。共鳴は大きく削る近道（核は数ターンで戻る）。このターンは、主はまねない";
    }
}
