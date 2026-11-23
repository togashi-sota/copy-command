using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【Combat V4（2026-10-07）】戦闘画面の追加：カウント大技の表示（盤面の上の「あと N」・①の予告・敵の一覧）、戦術札の欄（緑・命令枠なし）、
    /// 戦術札の対象選び（敵をクリック）。BattleUIShowcase から呼ぶ。
    /// </summary>
    public partial class BattleUI
    {
        static readonly Color TacticGreen = new Color(0.3f, 0.85f, 0.5f);
        static readonly Color ClockWarnColor = new Color(0.95f, 0.25f, 0.9f);

        CommandData tacticPending;
        UnitController tacticTarget1;
        /// <summary>戦術札の対象を選んでいる最中（ほかの操作を止める）。</summary>
        bool TacticTargeting => tacticPending != null;

        void CancelTactic() { tacticPending = null; tacticTarget1 = null; kbTacticIndex = -1; }

        // 【RC5・キーボードだけでも遊べる】戦術札の対象・転移石の行き先も、矢印で選んで Enter（前はクリックだけ）
        int kbTacticIndex = -1;
        Vector2Int? kbWarpCell;

        List<UnitController> TacticTargetsNow() =>
            tacticPending == null ? new List<UnitController>() : turn.Enemies.Where(IsTacticCandidate).ToList();

        /// <summary>今選べる対象。2体目（狙いを向ける敵）は、1体目以外の生きている敵なら誰でも（TacticRules.CanUse と同じ）。</summary>
        bool IsTacticCandidate(UnitController e)
        {
            if (tacticPending == null || e == null || !e.IsAlive) return false;
            if (TacticRules.TargetOf(tacticPending.tactic) == TacticTarget.EnemyThenEnemy && tacticTarget1 != null)
                return e != tacticTarget1 && !e.Model.IsPlayer; // 【RC5・コードレビュー】前は2体目も「大技が①の敵」だけで、選べないことがあった
            return TacticRules.IsValidTarget(turn.Grid.Model, tacticPending.tactic, e.Model);
        }

        UnitController KbTacticTarget()
        {
            var list = TacticTargetsNow();
            return kbTacticIndex >= 0 && kbTacticIndex < list.Count ? list[kbTacticIndex] : null;
        }

        /// <summary>戦術札の対象選び：矢印・W A S D で次の敵、Enter・Space で決める。</summary>
        bool HandleTacticTargetKey(KeyCode key)
        {
            var list = TacticTargetsNow();
            if (list.Count == 0) return false;
            var d = InputPriority.KeyToDirection(key);
            if (d.HasValue)
            {
                int step = d == Direction.Left || d == Direction.Up ? -1 : 1;
                kbTacticIndex = kbTacticIndex < 0 ? (step > 0 ? 0 : list.Count - 1) : (kbTacticIndex + step + list.Count) % list.Count;
                return true;
            }
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                var e = KbTacticTarget();
                if (e == null) { kbTacticIndex = 0; return true; } // 何も選んでいなければ、まず1体目を選ぶ
                kbTacticIndex = -1;
                PickTacticTarget(e);
                return true;
            }
            return false;
        }

        /// <summary>転移石の行き先：矢印・W A S D でその向きの一番近い行き先へ、Enter・Space で移る（倒れるマスは選ばない）。</summary>
        bool HandleWarpKey(KeyCode key)
        {
            var m = turn.Grid.Model;
            var targets = RescueRules.Targets(m).Where(c => !warpDamage.TryGetValue(c, out var dmg) || dmg < m.Player.Hp).ToList();
            if (targets.Count == 0) return false;
            if (kbWarpCell.HasValue && !targets.Contains(kbWarpCell.Value)) kbWarpCell = null;
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                if (!kbWarpCell.HasValue) { kbWarpCell = targets.OrderBy(c => Mathf.Abs(c.x - m.Player.Position.x) + Mathf.Abs(c.y - m.Player.Position.y)).First(); return true; }
                var to = kbWarpCell.Value;
                kbWarpCell = null;
                act.ChooseWarpCell(to);
                return true;
            }
            var d = InputPriority.KeyToDirection(key);
            if (!d.HasValue) return false;
            var from = kbWarpCell ?? m.Player.Position;
            var v = d.Value.ToOffset();
            var best = targets.Where(c => c != from && (c.x - from.x) * v.x + (c.y - from.y) * v.y > 0)
                .OrderBy(c => Mathf.Abs((c.x - from.x) * v.x + (c.y - from.y) * v.y) + 2 * Mathf.Abs((c.x - from.x) * v.y + (c.y - from.y) * v.x))
                .Cast<Vector2Int?>().FirstOrDefault();
            if (best.HasValue) kbWarpCell = best;
            else if (!kbWarpCell.HasValue) kbWarpCell = targets[0];
            return true;
        }

        static string DigitFor(int cd) => cd.ToString();

        // ───────── 戦術札の欄（手札の下） ─────────

        const float TacticTrayH = 64f;

        float DrawTacticTray(float x, float y, float w, bool input)
        {
            var td = turn.Tactics;
            var head = new GUIStyle(Label) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = false };
            int left = Mathf.Max(0, turn.TacticPlayLimit - turn.TacticPlaysThisTurn);
            string sub = turn.PendingSift ? "選別：上の手札から捨てる札を選ぶ" : TacticTargeting ? $"『{tacticPending.displayName}』対象の敵を盤面か敵の欄でクリック（矢印で選んで Enter・Esc でやめる）" : $"戦術札（命令枠を使わない・敵はまねしない）　このターンあと {left} 枚　山札 {td.DrawCount}・捨て札 {td.DiscardCount}";
            ShadowLabel(new Rect(x, y - 3, w, 22), sub, head, TacticGreen);
            y += 24;
            float gap = 8;
            int slots = td.HandMax;
            float cw = Mathf.Min((w - gap * (slots - 1)) / slots, 300f); // 【RC6】緑は1枚の特別な欄（幅いっぱいに伸ばさない）
            float ch = TacticTrayH - 26;
            string[] keys = { "Z", "X", "C" }; // 【RC3・レビュー】Q W E → Z X C（W は向きの WASD とぶつかり、札を選ぶ前に押すと戦術札が出ていた）
            for (int i = 0; i < slots; i++)
            {
                var r = new Rect(x + i * (cw + gap), y, cw, ch);
                if (i >= td.Hand.Count)
                {
                    DrawFrame(r, new Color(TacticGreen.r, TacticGreen.g, TacticGreen.b, 0.18f), 1f);
                    GUI.Label(r, "（空き）", new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.28f) } });
                    continue;
                }
                var card = td.Hand[i];
                bool playable = turn.CanPlayTactic(card) && !turn.PendingSift;
                bool usable = playable && TacticRules.TargetOf(card.tactic) != TacticTarget.None ? TacticHasAnyTarget(card) : playable && TacticRules.CanUse(turn.Grid.Model, card.tactic, null, null);
                bool picked = tacticPending == card || (turn.PendingSift && card.tactic == TacticKind.Sift);
                DrawUi(r, "panel", new Color(0.05f, 0.18f, 0.1f, 0.95f), usable || picked ? 1f : 0.55f);
                DrawRect(new Rect(r.x + 2, r.y + 2, 4, r.height - 4), TacticGreen);
                if (picked) DrawFrame(r, Gold, 3f);
                else if (usable && r.Contains(Event.current.mousePosition)) DrawFrame(r, TacticGreen, 2f);
                var nameSt = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Normal, wordWrap = false, clipping = TextClipping.Clip };
                ShadowLabel(new Rect(r.x + 10, r.y + 2, r.width - 40, 20), card.displayName, nameSt, usable || picked ? Color.white : new Color(1f, 1f, 1f, 0.55f));
                var keySt = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                var kr = new Rect(r.xMax - 24, r.y + 4, 18, 16);
                DrawRect(kr, new Color(0f, 0f, 0f, 0.55f));
                GUI.Label(kr, keys[i], keySt);
                string tx = TacticRules.Text(card.tactic);
                GUI.Label(new Rect(r.x + 10, r.y + 21, r.width - 14, r.height - 20), $"［{TacticCategoryName(card.tactic)}］" + TacticRules.Short(card.tactic), new GUIStyle(Tiny) { fontSize = 10, wordWrap = true, clipping = TextClipping.Clip, normal = { textColor = new Color(0.82f, 0.95f, 0.85f) } });
                if (r.Contains(Event.current.mousePosition))
                {
                    hoverTitle = $"{card.displayName}［戦術札・{TacticCategoryName(card.tactic)}・{TacticTargetText(card.tactic)}］"; // 【RC4】系統
                    hoverBody = tx + "\n\n戦術札は別のデッキ。命令の枠を使わず、敵もまねしない。1ターンに " + turn.TacticPlayLimit + " 枚まで。使った札は捨て札へ（山札が尽きたら混ぜ直す）。" + (usable ? "" : "\n（今は使えない：" + TacticWhyNot(card) + "）");
                    hoverPos = new Vector2(r.x, r.y - 100);
                }
                if (input && !TacticTargeting && GUI.Button(r, GUIContent.none, GUIStyle.none) && usable) UseTacticCard(card);
                else if (input && TacticTargeting && GUI.Button(r, GUIContent.none, GUIStyle.none)) CancelTactic();
            }
            return y + ch + 4;
        }

        /// <summary>【RC2】戦術札（緑）を、命令札と同じカードの見た目で手札の列に出す（絵つき・命令枠を使わない・敵はまねない）。</summary>
        void DrawTacticHandCard(Rect r, CommandData card, int index, bool input)
        {
            bool playable = turn.CanPlayTactic(card) && !turn.PendingSift;
            bool usable = playable && TacticRules.TargetOf(card.tactic) != TacticTarget.None ? TacticHasAnyTarget(card) : playable && TacticRules.CanUse(turn.Grid.Model, card.tactic, null, null);
            bool picked = tacticPending == card || (turn.PendingSift && card.tactic == TacticKind.Sift);
            DrawCardV2(r, card, picked, false, 0, usable || picked, 1f, 0);
            if (picked) DrawFrame(r, Gold, 3f);
            else if (usable && r.Contains(Event.current.mousePosition)) DrawFrame(r, TacticGreen, 2f);
            string[] keys = { "Z", "X", "C" }; // 【RC3・レビュー】Q W E → Z X C（W は向きの WASD とぶつかり、札を選ぶ前に押すと戦術札が出ていた）
            var kr = new Rect(r.xMax - 22, r.y + 5, 17, 16);
            DrawRect(kr, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(kr, index < 3 ? keys[index] : "", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter });
            if (r.Contains(Event.current.mousePosition))
            {
                hoverTitle = $"{card.displayName}［戦術札・{TacticCategoryName(card.tactic)}・{TacticTargetText(card.tactic)}］"; // 【RC4】系統
                hoverBody = TacticRules.Text(card.tactic) + "\n\n戦術札は別のデッキ。命令の枠を使わず、敵もまねしない。1ターンに " + turn.TacticPlayLimit + " 枚まで。使った札は捨て札へ（山札が尽きたら混ぜ直す）。" + (usable ? "" : "\n今は使えない：" + TacticWhyNot(card));
                hoverPos = new Vector2(r.x, r.yMax + 4);
            }
            if (input && !TacticTargeting && GUI.Button(r, GUIContent.none, GUIStyle.none) && usable) UseTacticCard(card);
            else if (input && TacticTargeting && GUI.Button(r, GUIContent.none, GUIStyle.none)) CancelTactic();
        }

        // ───────── 敵の詳細（選んだ敵だけ・一覧は出さない） ─────────

        UnitController selectedEnemy;

        /// <summary>右下：クリックした敵の説明だけ（常に5体分を読ませない）。選んでいなければ短い案内。</summary>
        float DrawV4EnemyDetail(float x, float y, float w, float hOverride = 0f)
        {
            if (selectedEnemy != null && !selectedEnemy.IsAlive) selectedEnemy = null;
            float h = hOverride > 0f ? hOverride : 112f; // 【RC4】92 → 112（このターンの流れの1行）【RC8】新しい配置は下まで
            var r = new Rect(x, y, w, h);
            DrawUi(r, "panel", PanelBlue);
            if (cardTipTitle != null)
            {
                // 【RC7】手札の札の説明（浮かぶ窓の代わり）
                ShadowLabel(new Rect(r.x + 12, r.y + 4, r.width - 24, 20), cardTipTitle, new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false }, Gold);
                var tipSt = new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.88f, 0.9f, 0.98f) } };
                GUI.Label(new Rect(r.x + 12, r.y + 24, r.width - 24, r.height - 28), WrapJa(cardTipBody ?? "", tipSt, r.width - 24), tipSt); // 【通常リリース版・画面の直し 16】単語の途中で改行しない
                return y + h;
            }
            if (selectedEnemy == null)
            {
                float lh = 0f;
                if (CombatV2) { lh = DrawLastTurnInDetail(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height * 0.62f)); r = new Rect(r.x, r.y + lh, r.width, r.height - lh); } // 【RC9】前のターンに起きたこと（過去ははっきり）
                if (DrawsHintsItself) { float hh = DrawHintInDetail(new Rect(r.x + 6, r.y + 6, r.width - 12, (r.height - 6f) * (lh > 0f ? 1f : 0.55f))); r = new Rect(r.x, r.y + hh, r.width, r.height - hh); } // 【RC8】ヒントは右下の欄の上【RC9・初見テスト4回目「ログが出るとヒントの下が切れる」】前のターンの記録があるときは、残りを全部ヒントに（いつもの案内は省く）
                if (FestivalUi && (lh <= 0f || act.FestivalDemo)) { DrawFestivalGuide(r); return y + h; } // 【文化祭クエスト v2】お手本中は 記録の下に「お手本の例」 // 【文化祭・初見レビュー「一番広い欄が空・操作が小さい字」】やることを大きく
                if (lh > 0f) return y + h; // いつもの案内（凡例の言葉）は盤面の下の凡例と同じ：記録があるターンは出さない（途中で切れていた）
                // 【RC3・レビュー】大技の無い戦闘（チュートリアル・はじめての戦闘）では、大技の数字の説明を出さない
                bool anyClock = turn.Grid.Model != null && turn.Grid.Model.Enemies.Any(en => en.IsAlive && en.Clocks.Count > 0);
                string idle = CombatV2 ? IdleGuideV2() : "あなたの命令を、敵もそれぞれの位置からまねます（命令の欄の「まねる：…」）。\n" + (anyClock ? "敵の頭上の砂時計＝大技まであと何ターン（今＝このターンのまねの後に発動）。" : "") + "敵をクリックすると、ここに説明が出ます。";
                var idleSt = new GUIStyle(Tiny) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.8f, 0.84f, 0.94f) } };
                GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 14), WrapJa(idle, idleSt, r.width - 24), idleSt); // 【RC11・QA】禁則（「ン）。」だけが次の行）
                return y + h;
            }
            var e = selectedEnemy;
            var pr = new Rect(r.x + 6, r.y + 6, 44, 44);
            if (!DrawUiImage(pr, "portrait_" + KindKey(e.Model), 1f)) DrawRect(pr, e.BaseColor);
            ShadowLabel(new Rect(pr.xMax + 8, r.y + 4, r.width - 70, 20), $"{PhaseRules.ShortName(e.Model)}　HP {e.Hp}/{e.MaxHp}　{CopyKinds.Short(e.Model.CopyKind)}{(e.Model.AfterCopy && !PhaseRules.IntentOrder ? "・後追い" : "")}", new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false }, Color.white);
            // 【RC4】このターンの流れ（① 攻撃（赤）→ ② まね → ③ 自分の動き）を1行
            string flow = ThisTurnFlow(e.Model);
            if (flow != null) GUI.Label(new Rect(pr.xMax + 8, r.yMax - 18, r.width - 70, 16), flow, new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 0.8f, 0.5f) } });
            GUI.Label(new Rect(pr.xMax + 8, r.y + 24, r.width - 70, r.height - 26 - (flow != null ? 16 : 0)), V4EnemyHelp(e.Model, turn.Grid.Model != null && turn.Grid.Model.NoTraits), new GUIStyle(Tiny) { fontSize = 11, wordWrap = true, clipping = TextClipping.Clip, normal = { textColor = new Color(0.86f, 0.9f, 0.98f) } });
            if (GUI.Button(new Rect(r.xMax - 24, r.y + 4, 20, 20), "×")) selectedEnemy = null;
            return y + h;
        }

        /// <summary>【RC4】選んだ敵の、このターンの流れ（組んだ命令で試算）。例：「このターン：攻撃（赤）→ まね 一歩↑ → 接近2」。</summary>
        string ThisTurnFlow(UnitModel e)
        {
            var model = turn.Grid.Model;
            if (model == null || !e.IsAlive || turn.State != BattleState.PlayerInput || !PhaseRules.IntentOrder) return null;
            if (model.NoTraits) return "このターン：あなたの命令をまねるだけ";
            if (!PhaseRules.HasTrait(e)) return "このターン：動けない（攻撃も大技もしない）";
            var parts = new List<string>();
            if (ThreatMap.EnemyAttack(model).Count > 0 && PhaseRules.TraitCells(model, e).Count > 0) parts.Add("攻撃（赤）");
            var copied = turn.PlannedCommands.Where(c => c.Command != null && CopyKinds.Accepts(e, c.Command) && !c.Command.notCopied).Select(c => c.Command.displayName + (c.Command.IsNoBoardEffect ? "" : c.Direction.ToLabel().Substring(0, 1))).ToList();
            if (copied.Count > 0) parts.Add("まね " + string.Join("・", copied));
            else if (turn.PlannedCommands.Count == 0) parts.Add("まね（あなたの命令しだい）");
            var f = ForecastFor(model, CombatV2 && !AnswerPreview ? new List<CommandRecord>() : turn.PlannedCommands); // 【RC9】本編は敵の「つもり」（ターンの初め）
            var mv = f.Failed ? null : f.OwnMoves.FirstOrDefault(m => m.EnemyId == e.Id);
            if (mv != null) parts.Add(mv.Label);
            else if (e.Kind == EnemyKind.Spearman || e.Kind == EnemyKind.Shield) parts.Add("あなたの方を向く");
            return "このターン：" + string.Join(" → ", parts);
        }

        static string TacticTargetText(TacticKind k)
        {
            switch (TacticRules.TargetOf(k))
            {
                case TacticTarget.Enemy: return "敵を選ぶ";
                case TacticTarget.EnemyThenEnemy: return "敵を2体選ぶ";
                default: return "すぐ使う";
            }
        }

        string TacticWhyNot(CommandData card)
        {
            if (turn.TacticPlaysThisTurn >= turn.TacticPlayLimit) return $"このターンは {turn.TacticPlayLimit} 枚使った";
            switch (card.tactic)
            {
                case TacticKind.Mend: return "HP が満タン";
                case TacticKind.Roar: return "隣に敵がいない";
                case TacticKind.EarlyBell: return "早められる大技が無い（残り2以上のものだけ）";
                case TacticKind.LateBell: return "対象の敵がいない";
                case TacticKind.Toll:
                case TacticKind.Detonate: return "残り2以上の大技を持つ敵がいない";
                case TacticKind.Retarget: return "予告中（①）の大技を持つ敵がいない／あなたの位置で決まらない形";
                case TacticKind.Silence: return "対象の敵がいない";
                case TacticKind.Sift: return "手札に捨てられる札が無い";
                default: return "今は効果がない";
            }
        }

        bool TacticHasAnyTarget(CommandData card)
        {
            var m = turn.Grid.Model;
            return m.Enemies.Any(e => TacticRules.IsValidTarget(m, card.tactic, e)) &&
                   (TacticRules.TargetOf(card.tactic) != TacticTarget.EnemyThenEnemy || m.Enemies.Count(e => e.IsAlive && !e.IsObject) >= 2);
        }

        void UseTacticCard(CommandData card)
        {
            ResetPending();
            if (turn.PendingSift) return;
            var kind = TacticRules.TargetOf(card.tactic);
            if (kind == TacticTarget.None)
            {
                if (!turn.PlayTactic(card)) return;
                if (turn.PendingSift) { tidyMode = true; tidyPick.Clear(); }
                return;
            }
            tacticPending = card;
            tacticTarget1 = null;
        }

        /// <summary>対象の敵を選んだ（盤面のマス・敵の欄から）。</summary>
        void PickTacticTarget(UnitController e)
        {
            if (tacticPending == null || e == null || !e.IsAlive) return;
            var m = turn.Grid.Model;
            var kind = TacticRules.TargetOf(tacticPending.tactic);
            if (kind == TacticTarget.Enemy)
            {
                if (!TacticRules.IsValidTarget(m, tacticPending.tactic, e.Model)) return;
                var card = tacticPending;
                CancelTactic();
                turn.PlayTactic(card, e);
                return;
            }
            if (tacticTarget1 == null)
            {
                if (!TacticRules.IsValidTarget(m, tacticPending.tactic, e.Model)) return;
                tacticTarget1 = e;
                return;
            }
            if (e == tacticTarget1) { tacticTarget1 = null; return; }
            var c2 = tacticPending; var t1 = tacticTarget1;
            CancelTactic();
            turn.PlayTactic(c2, t1, e);
        }

        /// <summary>【RC6】手札の欄を描くときの戦術札：描く前に写しておく（描いている途中で札を使うと手札から消える。前は ArgumentOutOfRangeException）。</summary>
        public static List<CommandData> TacticHandForDrawing(TurnManager t) => t != null && t.IsV4 && t.Tactics != null ? t.Tactics.Hand.ToList() : new List<CommandData>();
        /// <summary>【RC6】自動の通し（DebugAutoCapture）だけが使う：次に描くとき、この番号の緑の札を押したことにする（-1＝何もしない）。</summary>
        public static int DebugClickTacticIndex = -1;

        /// <summary>Z・X・C：戦術札（【RC3】前は Q・W・E。W が向きの WASD とぶつかった）。</summary>
        bool HandleTacticKey(KeyCode key)
        {
            if (!IsV4Ui || turn.Tactics == null) return false;
            if (key == KeyCode.F) { TurnManager.FastForward = !TurnManager.FastForward; return true; }
            int i = key == KeyCode.Z ? 0 : key == KeyCode.X ? 1 : key == KeyCode.C ? 2 : -1;
            if (i < 0) return false;
            if (TacticTargeting) { CancelTactic(); return true; }
            if (i >= turn.Tactics.Hand.Count) return true;
            var card = turn.Tactics.Hand[i];
            if (turn.CanPlayTactic(card) && !turn.PendingSift && (TacticRules.TargetOf(card.tactic) != TacticTarget.None ? TacticHasAnyTarget(card) : TacticRules.CanUse(turn.Grid.Model, card.tactic, null, null))) UseTacticCard(card);
            return true;
        }

        bool IsV4Ui => turn != null && turn.IsV4;

        // ───────── ボス・中ボスの登場（名前の帯・画面揺れ・音） ─────────

        int introSerial = -1;
        float introStart = -10f;
        string introTitle, introSub;

        static bool IsBossKind(EnemyKind k) => k == EnemyKind.ExecKnight || k == EnemyKind.CannonKing || k == EnemyKind.BeastKing || k == EnemyKind.MirrorLord;

        void DrawBossIntro(float bw)
        {
            if (!IsV4Ui) return;
            if (introSerial != turn.BattleSerial)
            {
                introSerial = turn.BattleSerial;
                introTitle = null;
                var boss = turn.Enemies.FirstOrDefault(e => IsBossKind(e.Model.Kind));
                if (boss != null && turn.TurnNumber == 1)
                {
                    introStart = Time.unscaledTime;
                    introTitle = (boss.Model.Kind == EnemyKind.MirrorLord ? "ボス　" : FestivalUi ? "ミニボス　" : "中ボス　") + PhaseRules.KindName(boss.Model.Kind); // 【文化祭】ミニボス
                    introSub = boss.Model.Clocks.Count > 0 ? "大技：" + string.Join("・", boss.Model.Clocks.Select(c => $"{c.Name}（{c.Period}ターンごと）")) : ""; // 【第3巡の QA】扉と同じ書き方 // 【RC5・レビュー】（3）がダメージに見えた【レビュー3】丸数字は「あと何ターン」だけに使う
                    ScreenFx.Shake(0.2f, 0.45f);
                    ScreenFx.Flash(new Color(1f, 0.85f, 0.4f, 0.3f), 0.5f); // 【第2巡 2】金の1色
                    Sfx.Play(Sfx.Kind.Boom);
                    if (boss.Model.Kind == EnemyKind.MirrorLord) introTitle = null; // 【RC11】ボスは「第一段階 鏡の主」の帯（DrawPhaseBanner）が代わり：前は2つの帯が重なっていた
                }
            }
            if (introTitle == null) return;
            if (MissionCardOpen) { introStart = Time.unscaledTime; return; } // 【文化祭】MISSION の窓を閉じてから出す（窓の上に重なっていた）
            float t = Time.unscaledTime - introStart;
            if (t > 1.8f) return;
            // 【RC2】最初のクリック・キーで消す（盤面の2〜3段目を隠していた：初見のレビュー）。消すだけで、その入力は盤面に渡す
            if (t > 0.3f && Event.current != null && (Event.current.type == EventType.MouseDown || Event.current.type == EventType.KeyDown)) { introTitle = null; return; }
            float a = t < 0.25f ? t / 0.25f : t > 1.4f ? 1f - (t - 1.4f) / 0.4f : 1f;
            if (V2Layout) { DrawTopBanner(bw, introTitle, introSub, a); return; } // 【第2巡 2】盤面に重ねない（上の欄に2行・金の1色）
            var band = new Rect(0, 230, bw, 96);
            DrawRect(band, new Color(0.02f, 0.01f, 0.06f, 0.82f * a));
            DrawRect(new Rect(0, band.y, bw, 3), new Color(1f, 0.5f, 0.95f, a));
            DrawRect(new Rect(0, band.yMax - 3, bw, 3), new Color(1f, 0.5f, 0.95f, a));
            var c1 = new Color(1f, 0.85f, 0.45f, a);
            ShadowLabel(new Rect(0, band.y + 8, bw, 48), introTitle, new GUIStyle(Big) { fontSize = 36, alignment = TextAnchor.MiddleCenter }, c1);
            GUI.Label(new Rect(0, band.y + 58, bw, 28), introSub, new GUIStyle(Small) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.85f, 1f, a) } });
        }

        // ───────── 敵の一覧（V4：1行を低く・大技のカウントを並べる） ─────────

        float DrawShowEnemyPlanV4(float x, float y, float w)
        {
            var enemies = turn.Enemies.Where(e => !e.Model.IsObject || e.IsAlive).ToList();
            var head = new GUIStyle(Label) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = false };
            ShadowLabel(new Rect(x, y, w * 0.45f, 20), "敵（大技のカウント）", head, new Color(1f, 0.6f, 0.55f));
            GUI.Label(new Rect(x + w * 0.45f, y, w * 0.55f, 20), "紫のマス＝①になった大技の範囲（まねの後に発動・敵にも当たる）", new GUIStyle(Tiny) { fontSize = 11, alignment = TextAnchor.MiddleRight, wordWrap = false, normal = { textColor = new Color(0.95f, 0.7f, 0.95f) } });
            y += 22;
            int cols = enemies.Count > 4 ? 2 : 1;
            float gap = 6;
            float cw = (w - gap * (cols - 1)) / cols;
            float rowH = enemies.Count > 4 ? 35f : 40f;
            bool enemyTurn = turn.State == BattleState.EnemyExecuting;
            for (int i = 0; i < enemies.Count; i++)
            {
                var e = enemies[i];
                int col = i % cols, row = i / cols;
                var r = new Rect(x + col * (cw + gap), y + row * rowH, cw, rowH - 3);
                bool acting = enemyTurn && turn.ActingEnemy == e;
                bool near = e.IsAlive && e.Model.Clocks.Any(c => c.Cd == 1);
                DrawUi(r, near ? "panel_red" : "panel", PanelBlue, e.IsAlive ? 1f : 0.4f);
                if (acting) DrawFrame(r, Gold, 2f);
                var pr = new Rect(r.x + 3, r.y + 3, r.height - 6, r.height - 6);
                if (!DrawUiImage(pr, "portrait_" + KindKey(e.Model), e.IsAlive ? 1f : 0.3f)) DrawRect(pr, e.BaseColor);
                float tx = pr.xMax + 5;
                string nm = $"{PhaseRules.ShortName(e.Model)}";
                var nameSt = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
                ShadowLabel(new Rect(tx, r.y + 2, cw * 0.46f, 15), nm, nameSt, Color.white); // 【RC5】兵種の色をやめた（意味の色と重ならない）
                var sub = new GUIStyle(Tiny) { fontSize = 10, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.8f, 0.82f, 0.9f) } };
                string stat = !e.IsAlive ? "撃破" : e.Model.Dormant ? "眠り" : $"HP {e.Hp}/{e.MaxHp}・{(e.Model.ForceAllCopy ? "全まね（模倣命令）" : CopyKinds.Short(e.Model.CopyKind))}{(e.Model.Silenced ? "・沈黙" : "")}";
                GUI.Label(new Rect(tx, r.y + 17, cw * 0.46f, 14), stat, sub);
                if (e.IsAlive && e.Model.Clocks.Count > 0)
                {
                    float cx = r.x + cw * 0.47f, chipW = (r.xMax - cx - 3) / Mathf.Max(1, e.Model.Clocks.Count) - 2;
                    for (int k = 0; k < e.Model.Clocks.Count; k++)
                    {
                        var c = e.Model.Clocks[k];
                        var cr = new Rect(cx + k * (chipW + 2), r.y + 2, chipW, r.height - 4);
                        bool warn = c.Cd == 1;
                        float pulse = warn ? Blink(0.3f, 1f, 8f) : 1f;
                        DrawRect(cr, warn ? new Color(0.75f * pulse, 0.1f, 0.65f * pulse, 0.95f) : new Color(0.12f, 0.14f, 0.24f, 0.95f));
                        DrawFrame(cr, warn ? new Color(1f, 0.6f, 0.95f) : new Color(0.55f, 0.6f, 0.85f, 0.7f), warn ? 2f : 1f);
                        var cst = new GUIStyle(Tiny) { fontSize = chipW < 56 ? 9 : 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
                        GUI.Label(new Rect(cr.x, cr.y, cr.width, cr.height * 0.5f), c.Name, cst);
                        GUI.Label(new Rect(cr.x, cr.y + cr.height * 0.45f, cr.width, cr.height * 0.55f), warn ? "次で発動！" : $"あと {DigitFor(c.Cd)}", new GUIStyle(cst) { fontSize = warn ? 11 : 10, normal = { textColor = warn ? new Color(1f, 0.95f, 0.5f) : new Color(0.85f, 0.88f, 1f) } });
                        if (cr.Contains(Event.current.mousePosition))
                        {
                            hoverTitle = $"{PhaseRules.ShortName(e.Model)}の大技：{c.Name}（あと {c.Cd} ターン）";
                            hoverBody = ClockHelp(c);
                            hoverPos = new Vector2(Mathf.Min(cr.x, 600f), r.y - 110);
                        }
                    }
                }
                if (e.IsAlive && r.Contains(Event.current.mousePosition) && hoverTitle == null)
                {
                    hoverTitle = $"{PhaseRules.ShortName(e.Model)}　HP {e.Hp}/{e.MaxHp}";
                    hoverBody = EnemyHelpNow(e.Model);
                    hoverPos = new Vector2(Mathf.Min(r.x, 600f), r.y - 130);
                }
                // 戦術札の対象に選べる敵：枠
                if (TacticTargeting && e.IsAlive && (IsTacticCandidate(e) || e == tacticTarget1))
                {
                    DrawFrame(r, tacticTarget1 == e ? Gold : TacticGreen, 3f);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) PickTacticTarget(e);
                }
            }
            return y + ((enemies.Count + cols - 1) / cols) * rowH;
        }

        static string ClockHelp(Clock c)
        {
            string shape;
            switch (c.Shape)
            {
                case ClockShape.Ring8: shape = "自分の周り8マス"; break;
                case ClockShape.Ring12: shape = "自分の周り8マス＋縦横の2マス目"; break;
                case ClockShape.LaneFacing: shape = $"向いている方向へ {c.Length} マス（貫通）"; break;
                case ClockShape.LaneToward: shape = $"あなたの方向へ最大 {c.Length} マス（貫通）"; break;
                case ClockShape.Fan3: shape = "向きの3列（あなたと同じ行・列ならあなたの方向）"; break;
                case ClockShape.Cross1: shape = "あなたのいたマスの十字（5マス）"; break;
                case ClockShape.BigCross: shape = "あなたのいたマスの大十字（9マス）"; break;
                case ClockShape.Bombard: shape = "あなたのいたマスと、左右2マスの2か所の十字"; break;
                case ClockShape.Slam3x3: shape = "あなたのいたマスの3×3"; break;
                case ClockShape.RowSweep: shape = "あなたのいた行まるごと"; break;
                case ClockShape.ColSweep: shape = "あなたのいた列まるごと"; break;
                default: shape = "周り8マス（外へ押し出す）"; break;
            }
            return $"{c.Name}：{shape}に {c.Damage} ダメージ。{(c.Charge ? "撃つ敵はそこへ突っ込む。" : "")}{(c.Shove ? "当たった相手は1マス押される。" : "")}\n" +
                   $"残り ① になると当たるマスが紫で決まって見える（そのあとは動かない）。このターンのまねの後（敵が自分で動く前）に発動し、そのマスにいる全員（敵も）に当たる。周期 {c.Period} ターン。\n" +
                   "敵どうしを予告のマスへ入れたり、戦術札（早鐘・遅鐘・照準替えなど）で発動を前後させよう。";
        }

        // ───────── 盤面の上：大技の「あと N」・①の予告のダメージ・戦術札の対象 ─────────

        // ───────── 盤面ギミックの印（炎・落石・レーザー・爆撃・回復）：読まなくても分かる絵 ─────────

        /// <summary>【RC6・本人「-3 だけでは何の数字か・いつ来るか分からない」】マスの危険：合計ダメージと、一番大きい原因の絵（剣・矢・爆発・大技・光柱・床）。</summary>
        internal struct CellHazard { public int Dmg; public string Icon; public int IconDmg; public string Icon2; }
        /// <summary>このターンに当たる危険（今）と、次のターンの危険（次：炎の火種・岩の影・次のレーザー）。</summary>
        readonly Dictionary<Vector2Int, CellHazard> cellDamage = new Dictionary<Vector2Int, CellHazard>(), futureDamage = new Dictionary<Vector2Int, CellHazard>();
        static void AddHazard(Dictionary<Vector2Int, CellHazard> map, Vector2Int c, int dmg, string icon)
        {
            map.TryGetValue(c, out var h);
            h.Dmg += dmg;
            if (h.Icon != null && h.Icon != icon) h.Icon2 = dmg > h.IconDmg ? h.Icon : icon; // 【RC6・ブラインドテスト】「-8」に絵が1つ（赤3＋大技5）→ 原因が2つなら2つとも
            if (dmg > h.IconDmg || h.Icon == null) { h.Icon = icon; h.IconDmg = dmg; }
            map[c] = h;
        }
        void AddCellDamage(Vector2Int c, int dmg, string icon) => AddHazard(cellDamage, c, dmg, icon);
        void AddFutureDamage(Vector2Int c, int dmg, string icon) => AddHazard(futureDamage, c, dmg, icon);
        /// <summary>【RC6】敵の通常の攻撃（赤）の原因の絵：弓兵＝矢・砲兵＝爆発・ほか＝剣。</summary>
        static string RedIcon(UnitModel e) => e.Kind == EnemyKind.Archer ? "hz_arrow" : e.Kind == EnemyKind.Bomber || e.Kind == EnemyKind.CannonKing ? "hz_blast" : "hz_sword";
        /// <summary>【RC5・レビュー3】数字の札の縁の色：赤紫の大技のマス（①）・床の危険だけのマス（白っぽい）。赤と赤紫を色だけで見分けなくてよいように。</summary>
        readonly HashSet<Vector2Int> clockCells = new HashSet<Vector2Int>(), floorCells = new HashSet<Vector2Int>(), redCells = new HashSet<Vector2Int>();
        /// <summary>【RC5・レビュー4】上の辺に数字の札（ダメージ・赤の小さな数字・①）が出るマス。敵が動く先の札を、ここでは上の辺に置かない。</summary>
        readonly HashSet<Vector2Int> topChipCells = new HashSet<Vector2Int>();

        /// <summary>
        /// 【RC2】マスごとの合計ダメージ。【RC6・本人「-3 だけでは何か・いつか分からない」】数字の札＝［いつ］［原因の絵］［ダメージ］：
        /// 「今」＝このターンに当たる（濃い・縁がゆっくり脈打つ）／「次」＝次のターン（薄い・点線）。絵は剣・矢・爆発・大技（赤紫の星）・光柱・炎・岩・光線。
        /// </summary>
        void DrawCellDamage(System.Func<Vector2Int, Rect> CellRect)
        {
            chipRects.Clear();
            foreach (var kv in futureDamage)
                if (!cellDamage.ContainsKey(kv.Key)) chipRects.Add(DrawHazardChip(CellRect(kv.Key), kv.Value, false, false, true));
            foreach (var kv in cellDamage)
            {
                bool clock = clockCells.Contains(kv.Key), floorOnly = floorCells.Contains(kv.Key) && !clock && !redCells.Contains(kv.Key);
                chipRects.Add(DrawHazardChip(CellRect(kv.Key), kv.Value, true, clock, floorOnly));
            }
        }

        static readonly Color ChipRed = new Color(1f, 0.6f, 0.5f), ChipMagenta = new Color(1f, 0.45f, 0.95f), ChipFloor = new Color(0.92f, 0.92f, 0.86f);

        /// <summary>【RC6】数字の札1つ（マスの右上）：［今／次］［原因の絵］［-ダメージ］。</summary>
        /// <summary>【RC6】数字の札の場所（前のフレーム）：あなたの名札が札を隠さないように避ける。</summary>
        internal readonly List<Rect> chipRects = new List<Rect>();

        internal static Rect DrawHazardChip(Rect cell, CellHazard h, bool now, bool clock, bool floorOnly)
        {
            bool two = !string.IsNullOrEmpty(h.Icon2);
            // 【RC7・本人「-3 だとマイナスの数字で、何の数字か分からない」】「-3」をやめて「3ダメ」（数字は太く・「ダメ」は小さく）。
            //   1段：［今／次］［原因の絵］［3ダメ］。絵が2つ（赤＋大技など）のときは、マスより広くならないように2段：［今］［絵2つ］／［8ダメ］。
            var numSt = new GUIStyle(Tiny) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            var unitSt = new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            string num = h.Dmg.ToString();
            float numW = numSt.CalcSize(new GUIContent(num)).x, unitW = unitSt.CalcSize(new GUIContent("ダメ")).x;
            float tabW = 13f;
            float iconW = two ? 21f : 15f;
            const float rowH = 18f;
            float w = two ? Mathf.Max(tabW + iconW + 4f, numW + unitW + 6f) : tabW + iconW + numW + unitW + 5f;
            float ch = two ? rowH * 2f : rowH + 1f;
            var chip = new Rect(cell.xMax - w - 1f, cell.y + 2f, w, ch);
            Color edge = clock ? ChipMagenta : floorOnly ? ChipFloor : ChipRed;
            DrawRect(chip, new Color(0f, 0f, 0f, now ? 0.82f : 0.5f));
            if (now) DrawFrame(chip, new Color(edge.r, edge.g, edge.b, Blink(0.55f, 1f, 5f)), clock ? 2f : 1.5f);
            else DrawDashedFrame(chip, new Color(edge.r, edge.g, edge.b, 0.7f), 1f, 3f);
            var tab = new Rect(chip.x, chip.y, tabW, rowH);
            DrawRect(tab, now ? (clock ? new Color(0.6f, 0.08f, 0.55f, 0.95f) : floorOnly ? new Color(0.42f, 0.4f, 0.32f, 0.95f) : new Color(0.62f, 0.08f, 0.06f, 0.95f)) : new Color(0.25f, 0.27f, 0.32f, 0.85f));
            GUI.Label(tab, now ? "今" : "次", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = now ? Color.white : new Color(0.85f, 0.88f, 0.95f) } });
            if (two)
            {
                DrawUiImage(new Rect(tab.xMax + 1f, chip.y + 1f, 13f, 13f), h.Icon2, now ? 1f : 0.6f, ScaleMode.ScaleToFit);
                DrawUiImage(new Rect(tab.xMax + 8f, chip.y + 4f, 13f, 13f), h.Icon, now ? 1f : 0.6f, ScaleMode.ScaleToFit);
            }
            else if (!string.IsNullOrEmpty(h.Icon)) DrawUiImage(new Rect(tab.xMax + 1f, chip.y + 1.5f, iconW, iconW), h.Icon, now ? 1f : 0.6f, ScaleMode.ScaleToFit);
            var col = clock ? new Color(1f, 0.82f, 1f) : floorOnly ? new Color(0.96f, 0.96f, 0.9f) : new Color(1f, 0.86f, 0.72f);
            var shown = now ? col : new Color(col.r, col.g, col.b, 0.7f);
            var numRect = two ? new Rect(chip.x + 2f, chip.y + rowH, numW, rowH) : new Rect(tab.xMax + iconW + 2f, chip.y, numW, rowH + 1f);
            GUI.Label(numRect, num, new GUIStyle(numSt) { normal = { textColor = shown } });
            GUI.Label(new Rect(numRect.xMax + 1f, numRect.y + 1f, unitW + 2f, numRect.height), "ダメ", new GUIStyle(unitSt) { normal = { textColor = shown } });
            return chip;
        }

        static void HazardIcon(Rect cell, string icon, float alpha, bool occupied, float pulse = 1f)
        {
            float size = occupied ? cell.width * 0.5f : cell.width * 0.82f * pulse;
            var r = occupied ? new Rect(cell.x + 2, cell.y + cell.height - size - 2, size, size) : new Rect(cell.center.x - size / 2, cell.center.y - size / 2, size, size);
            DrawUiImage(r, icon, alpha, ScaleMode.ScaleToFit);
        }

        void DrawV4Hazards(System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            var env = model.Environment;
            var occ = new HashSet<Vector2Int>(model.Enemies.Where(e => e.IsAlive).Select(e => e.Position));
            occ.Add(model.Player.Position);
            string hz = turn.CurrentStage != null ? turn.CurrentStage.hazardName : "";
            bool laser = hz.Contains("レーザー"), rockfall = hz.Contains("落石");
            float t = Time.unscaledTime;
            var dmgSt = new GUIStyle(Tiny) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, wordWrap = false };
            if (ShowingBattle && env.HasFire)
            {
                var now = env.FireCellsNow.ToList();
                var next = env.FireCellsNext.Where(c => !env.FireCellsNow.Contains(c)).ToList();
                if (laser)
                {
                    // レーザー：行ごとに、今ターン＝太いビーム、次ターン＝細い照準線。発射口は左端
                    foreach (var row in next.GroupBy(c => c.y))
                    {
                        var xs = row.Select(c => CellRect(c)).ToList();
                        float x0 = xs.Min(r => r.x), x1 = xs.Max(r => r.xMax), cy = xs[0].center.y;
                        DrawRect(new Rect(x0, cy - 2, x1 - x0, 4), new Color(1f, 0.92f, 0.65f, 0.55f));
                        DrawDashedFrame(new Rect(x0 + 2, cy - xs[0].height * 0.28f, x1 - x0 - 4, xs[0].height * 0.56f), new Color(1f, 0.92f, 0.7f, 0.5f), 1.5f, 6f);
                        foreach (var c in row) AddFutureDamage(c, env.FireDamage, "hz_emitter"); // 【RC6・ブラインドテスト2】次の行の全部のマスに「次」の札（左端だけでは、ほかのマスに来るのか分からなかった）
                    }
                    foreach (var row in now.GroupBy(c => c.y))
                    {
                        var xs = row.Select(c => CellRect(c)).ToList();
                        float x0 = xs.Min(r => r.x), x1 = xs.Max(r => r.xMax), cy = xs[0].center.y, hh = xs[0].height * 0.36f;
                        float fl = Blink(0.75f, 1f, 22f); // 【RC5・レビュー3】ゆっくり・設定で止まる。色は床の危険の白っぽい光（赤は敵の攻撃だけ）
                        DrawRect(new Rect(x0, cy - hh, x1 - x0, hh * 2), new Color(1f, 0.92f, 0.6f, 0.32f * fl));
                        DrawRect(new Rect(x0, cy - hh * 0.55f, x1 - x0, hh * 1.1f), new Color(1f, 0.97f, 0.8f, 0.8f * fl));
                        DrawRect(new Rect(x0, cy - hh * 0.2f, x1 - x0, hh * 0.4f), new Color(1f, 1f, 1f, 0.95f));
                        DrawUiImage(new Rect(x0 - 2, cy - xs[0].height * 0.3f, xs[0].height * 0.6f, xs[0].height * 0.6f), "hz_emitter", 1f, ScaleMode.ScaleToFit);
                        foreach (var c in row) { AddCellDamage(c, env.FireDamage, "hz_emitter"); floorCells.Add(c); }
                    }
                }
                else
                {
                    string iconNow = rockfall ? "hz_rock" : "hz_flame", iconNext = rockfall ? "hz_rock_shadow" : "hz_ember";
                    foreach (var c in next) { if (model.Board.IsWall(c)) continue; HazardIcon(CellRect(c), iconNext, 0.85f, occ.Contains(c)); AddFutureDamage(c, env.FireDamage, iconNow); } // 【RC6】次のターンの床にも「次」の札（柱・壁のマスは出さない：レビュー）
                    foreach (var c in now)
                    {
                        var r = CellRect(c);
                        float pulse = rockfall ? 1f : Blink(0.88f, 1f, 10f, c.x * 1.7f + c.y);
                        HazardIcon(r, iconNow, 1f, occ.Contains(c), pulse);
                        DrawFrame(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), rockfall ? new Color(0.9f, 0.8f, 0.6f, 0.9f) : new Color(1f, 0.55f, 0.1f, 0.9f), 2f);
                        AddCellDamage(c, env.FireDamage, iconNow); floorCells.Add(c);
                    }
                }
            }
            // 回復マス（緑の十字・光る）
            if (ShowingBattle)
                foreach (var c in env.HealTiles)
                    HazardIcon(CellRect(c), "hz_heal", Blink(0.6f, 1f, 3f), occ.Contains(c));
            // 砲兵の爆撃の予告：照準（赤い床ではなく、狙いの印）
            foreach (var c in ThreatMap.Bombs(model))
            {
                var r = CellRect(c);
                HazardIcon(r, "hz_aim", 0.9f, occ.Contains(c), Blink(0.8f, 1f, 8f));
                AddCellDamage(c, PhaseRules.Bomb(model), "hz_blast"); // 【RC6】階ごとの強さ・原因の絵
            }
        }

        string warpKey;
        readonly Dictionary<Vector2Int, int> warpDamage = new Dictionary<Vector2Int, int>();

        /// <summary>
        /// 【RC4】転移石の行き先：光るマス（白い枠）と、そこへ移って何もしなかったときにこのターン受けるダメージ（「-5」・無ければ「安全」）。
        /// クリックで移る。右クリックでやめる（Esc はキーボードの方で）。
        /// </summary>
        void DrawWarpTargets(BattleModel model, System.Func<Vector2Int, Rect> CellRect)
        {
            var targets = RescueRules.Targets(model);
            string key = $"{turn.TurnNumber}|{model.Player.Position}|{model.Player.Hp}|{turn.TacticsUsedThisBattle}|{model.PlayerGuard}|{string.Join(",", model.Enemies.Select(e => e.Position + ":" + e.Hp + (e.Silenced ? "S" : "") + string.Join("", e.Clocks.Select(c => c.Cd))))}"; // 【RC4・レビュー】戦術札を使ったら作り直す
            if (key != warpKey)
            {
                warpKey = key;
                warpDamage.Clear();
                foreach (var c in targets) warpDamage[c] = RescueRules.DamageIfWarped(model, c);
            }
            var ev = Event.current;
            var st = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float pulse = Blink(0.3f, 1f, 6f);
            foreach (var c in targets)
            {
                var r = CellRect(c);
                bool mouse = r.Contains(ev.mousePosition);
                bool hot = mouse || kbWarpCell == c; // 【RC5】キーボードで選んでいるマスも光らせる（クリックはマウスの下のマスだけ：レビュー）
                warpDamage.TryGetValue(c, out int dmg);
                DrawRect(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(0.4f, 0.5f, 1f, hot ? 0.38f : 0.16f * pulse + 0.06f)); // 【RC5・レビュー3】転移石の行き先は青（ワープと同じ。水色＝まね・緑＝戦術札と分ける）
                DrawFrame(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), hot ? Gold : new Color(0.6f, 0.7f, 1f, 0.95f), hot ? 3f : 2f);
                if (kbWarpCell == c) DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Color.white, 3f); // 【RC5・レビュー】キーボードで選んでいるマスは、色だけでなく太い外枠でも
                var lr = new Rect(r.center.x - 24, r.yMax - 18, 48, 15);
                DrawRect(lr, dmg > 0 ? new Color(0.35f, 0.04f, 0.04f, 0.9f) : new Color(0.06f, 0.08f, 0.2f, 0.9f));
                GUI.Label(lr, dmg > 0 ? $"{dmg}ダメ" : "0", new GUIStyle(st) { normal = { textColor = dmg > 0 ? new Color(1f, 0.6f, 0.55f) : Color.white } }); // 【RC5・レビュー3】0 は白（緑＝戦術札） // 【RC4・レビュー】「安全」は言い過ぎ（あなたの命令のまねは別）
                if (dmg >= model.Player.Hp) { ShadowLabel(new Rect(r.x, r.y, r.width, r.height - 16), "×", new GUIStyle(st) { fontSize = 22 }, new Color(1f, 0.4f, 0.4f)); continue; } // 倒れるマス
                if (mouse && ev.type == EventType.MouseDown && ev.button == 0) { act.ChooseWarpCell(c); ev.Use(); return; }
            }
            if (ev.type == EventType.MouseDown && ev.button == 1) { act.CancelItem(); ev.Use(); }
        }

        /// <summary>【RC4】自分で動く敵の色（オレンジ）。まね＝水色と分ける。</summary>
        internal static readonly Color OwnMoveColor = new Color(1f, 0.62f, 0.15f);

        /// <summary>
        /// 【RC4・本人「いつ近づくのか分からない」】このターンの最後に、敵が自分で動く先：まねのあとの位置 → オレンジの矢印 → 薄い影（行き先）・短い語（接近②・位置取り）。
        /// 組んだ命令に合わせて動く（ターンを丸ごと試算）。ターンの初め（命令なし）から出る。まねで動く分は出さない（まねを読むのは遊ぶ人）。
        /// </summary>
        void DrawOwnMoveForecast(BattleModel model, System.Func<Vector2Int, Rect> CellRect)
        {
            if (turn.State != BattleState.PlayerInput || !PhaseRules.IntentOrder || model.NoTraits) return;
            // 【RC9】本編：敵が自分で動く先は、ターンの初めの盤面から（敵の「つもり」）。命令を組んでも作り直さない（押した後の動きは遊ぶ人が読む）
            var planF = CombatV2 && !AnswerPreview ? new List<CommandRecord>() : turn.PlannedCommands.ToList();
            if (CombatV2 && AnswerPreview && pendingCommand != null && previewRecordLast != null) planF.Add(previewRecordLast); // 【RC8・初見テスト2】押す先に接近の影が出ていた：選んでいる向きも入れて試す
            var f = ForecastFor(model, planF);
            if (f.Failed) return;
            var tag = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            foreach (var mv in f.OwnMoves)
            {
                var unit = turn.Enemies.FirstOrDefault(u => u.Model.Id == mv.EnemyId);
                if (unit == null || !unit.IsAlive) continue;
                // 【RC4・敵対的レビュー】まねで動く敵は、道と影を出さない：出すと「まねで柱に止まったか」が見えて、まねを読む遊びを画面がしてしまう
                //   （もう1人のレビュー「矢印が空のマスから出る」も、これで起きない）。敵の上に「まねのあと 接近2」だけ
                // 【RC5・レビュー】まねで動こうとして壁で止まった敵（位置が同じ）にも道と影を出していた＝「まねで止まった」が見えてしまう → まねの移動がある敵は、位置が同じでも札だけ
                bool copiesMove = (CombatV2 && !AnswerPreview) ? turn.PlannedCommands.Select((pc, i) => (pc, i)).Any(x => x.pc.Command != null && turn.IsCopied(x.i) && CopyKinds.IsMoveType(x.pc.Command.type) && CopyKinds.Accepts(unit.Model, x.pc.Command)) : unit.Model.Position != mv.From || turn.PlannedCommands.Select((pc, i) => (pc, i)).Any(x => x.pc.Command != null && turn.IsCopied(x.i) && CopyKinds.IsMoveType(x.pc.Command.type) && CopyKinds.Accepts(unit.Model, x.pc.Command)); // 【RC5・コードレビュー】まねされない札（忍び足・白紙の枠）は数えない
                if (copiesMove)
                {
                    var ur = CellRect(unit.Model.Position);
                    var tr = new Rect(ur.center.x - 36, ur.yMax - 34, 72, 15); // 【RC6・ブラインドテスト】「技まね」の札と重なっていた → まね方の札の上（マスの下の外は次の行の数字の札と重なった）
                    DrawRect(tr, new Color(0.25f, 0.12f, 0f, 0.9f));
                    DrawFrame(tr, OwnMoveColor, 1f);
                    GUI.Label(tr, "まね後 " + mv.Label, new GUIStyle(tag) { fontSize = 11, normal = { textColor = new Color(1f, 0.85f, 0.55f) } });
                    continue;
                }
                // 道：まねのあとの位置から、通るマスの中心をつなぐ（点の線＋最後に三角）
                var pts = new List<Vector2> { CellRect(mv.From).center };
                pts.AddRange(mv.Path.Select(c => CellRect(c).center));
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    int dots = 7;
                    for (int k = 1; k < dots; k++)
                    {
                        var q = Vector2.Lerp(pts[i], pts[i + 1], k / (float)dots);
                        if (plateRects.Values.Any(pr => pr.Contains(q))) continue; // 【RC5】名札の文字の上には点を打たない
                        DrawRect(new Rect(q.x - 2.5f, q.y - 2.5f, 5, 5), new Color(OwnMoveColor.r, OwnMoveColor.g, OwnMoveColor.b, 0.95f));
                    }
                }
                var to = CellRect(mv.To);
                bool onThreat = CombatV2 && cellThreats.TryGetValue(mv.To, out var mvt) && (mvt.Dmg > 0 || mvt.NextDmg > 0); // 【RC10・初見テスト2回目「赤・点線・移動先の枠が三重」→ 3回目「赤・半透明の敵の絵・矢印・札が重なる」】攻撃のマスでは矢印と「A 移動先」だけ
                // 影（薄い顔＋点線の枠）
                if (!onThreat && !DrawUiImage(new Rect(to.x + to.width * 0.18f, to.y + to.height * 0.18f, to.width * 0.64f, to.height * 0.64f), "portrait_" + KindKey(unit.Model), 0.38f))
                    DrawRect(new Rect(to.x + 6, to.y + 6, to.width - 12, to.height - 12), new Color(unit.BaseColor.r, unit.BaseColor.g, unit.BaseColor.b, 0.3f));
                if (!onThreat) DrawFrame(new Rect(to.x + 3, to.y + 3, to.width - 6, to.height - 6), new Color(OwnMoveColor.r, OwnMoveColor.g, OwnMoveColor.b, 0.8f), 1.5f); // 【RC10・初見テスト3回目「移動先の点線の枠が、敵と動く攻撃の点線と同じ形」】細い実線
                var last = pts.Count >= 2 ? pts[pts.Count - 1] - pts[pts.Count - 2] : Vector2.zero;
                string glyph = Mathf.Abs(last.x) > Mathf.Abs(last.y) ? (last.x > 0 ? "▶" : "◀") : (last.y > 0 ? "▼" : "▲");
                ShadowLabel(new Rect(to.center.x - 10, to.center.y - 11, 20, 22), glyph, new GUIStyle(Big) { fontSize = 16, alignment = TextAnchor.MiddleCenter }, OwnMoveColor);
                var lr = new Rect(to.center.x - 28, to.yMax - 17, 56, 15);
                bool showLabel = !CombatV2; // 【RC10・初見テスト3回目「移動先と書いてある時と無い時がある」】文字はいつも（攻撃のマスでは点線の枠だけ出さない）【通常リリース版・画面の直し 5】本編の画面は「移動先」の字を出さない（攻撃の札にかぶっていた）：オレンジの影と矢印だけ（言葉は凡例・敵の説明で「移動先」）
                if (plateRects.Values.Any(p => p.Overlaps(lr))) // 【RC5】上へ動く敵：行き先の下の辺に自分の名札がある（「接近」が名札を隠した）→ 上の辺へ
                {
                    lr.y = to.y + 3;
                    if (topChipCells.Contains(mv.To)) showLabel = false; // 【RC5・レビュー4】上の辺にダメージの数字や①があると重なって「接近3」「位置 -4」と読めた → 文字は出さない（矢印と点線の枠は残る・詳しくは敵の説明）
                }
                if (showLabel)
                {
                    DrawRect(lr, new Color(0.25f, 0.12f, 0.0f, 0.9f));
                    DrawFrame(lr, OwnMoveColor, 1f);
                    GUI.Label(lr, CombatV2 ? $"{unit.Model.KindLetter} 移動先" : mv.Label, new GUIStyle(tag) { normal = { textColor = new Color(1f, 0.85f, 0.55f) } }); // 【RC10・初見テスト「接近は攻撃？移動先？」】
                }
            }
        }

        void DrawV4BoardOverlay(System.Func<Vector2Int, Rect> CellRect)
        {
            var model = turn.Grid.Model;
            cellDamage.Clear(); futureDamage.Clear(); clockCells.Clear(); floorCells.Clear(); redCells.Clear();
            DrawV4Hazards(CellRect);
            // ①の予告：当たるマスにダメージの数（紫のマスの上）
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || (model.V4 && !PhaseRules.HasTrait(e))) continue; // 【RC5・レビュー3】動けない敵（処刑のあと・沈黙）は撃たない：数字も枠も出さない（赤紫のマスと同じ）
                foreach (var c in e.Clocks.Where(c => c.Telegraphed))
                {
                    int dmg = c.Damage + Clocks.DamageBonus;
                    foreach (var cell in c.Cells)
                    {
                        var r = CellRect(cell);
                        DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(1f, 0.45f, 0.95f, 0.9f), 2f);
                        AddCellDamage(cell, dmg, "hz_big"); clockCells.Add(cell);
                    }
                }
            }
            // 【RC2】効いている強化（残心・見切りのガード／集中の攻撃+）を、あなたのマスの上に
            {
                var pm = model.Player;
                var pr = CellRect(pm.Position);
                var chips = new List<string>();
                if (model.PlayerGuard > 0) chips.Add($"ガード -{model.PlayerGuard}");
                if (pm.AttackBonus > 0) chips.Add($"攻撃 +{pm.AttackBonus}");
                for (int k = 0; k < chips.Count; k++)
                {
                    var cr = new Rect(pr.x - 8, pr.yMax - 2 + k * 17, pr.width + 16, 16);
                    DrawRect(cr, new Color(0.04f, 0.2f, 0.12f, 0.92f));
                    DrawFrame(cr, TacticGreen, 1f);
                    GUI.Label(cr, chips[k], new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(0.7f, 1f, 0.8f) } });
                }
            }
            // 【RC3・レビュー】大技の①と通常の攻撃（赤）が重なるマスの数字が、大技だけだった（例：八方斬り①の剣兵の隣は 3＋5＝8 なのに -5）。
            //   数字が出ているマスにだけ、そのマスを赤にしている敵の通常の攻撃を足す（赤だけのマスは今までどおり色だけ）
            // 【RC6】赤のマスも同じ札（［今］［剣・矢・爆発・光柱］［ダメージ］）。前は赤だけのマスは小さな「-3」だけだった
            void AddRed(Vector2Int c, int d, string icon)
            {
                if (!model.Board.IsInside(c)) return;
                if (model.Enemies.Any(x => x.IsAlive && x.Dormant && x.Position == c)) return; // 【RC5・レビュー】眠っている写し身はダメージを受けない（「-3」が出ていた）
                AddCellDamage(c, d, icon); redCells.Add(c);
            }
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || (model.V4 && !PhaseRules.HasTrait(e))) continue; // 【RC5・レビュー3】動けない敵の「-3」を出さない（赤のマスと同じ）
                int d = PhaseRules.AttackDamageOf(model, e);
                if (d <= 0) continue;
                foreach (var c in PhaseRules.TraitCells(model, e).Distinct()) AddRed(c, d, RedIcon(e));
            }
            foreach (var e in model.Enemies.Where(x => x.IsAlive && x.Kind == EnemyKind.MirrorLord && PhaseRules.HasTrait(x))) // 【RC5】鏡の主の光柱・奔流（赤）の数字
            {
                foreach (var c in e.BombCells.Distinct()) AddRed(c, CombatBoss.PillarDamage, "hz_pillar");
                if (e.SlamCd == 1) foreach (var c in e.SlamCells.Distinct()) AddRed(c, e.BossStage >= 2 ? CombatBoss.ChargeDamage : CombatBoss.SlamDamage, "hz_big");
            }
            topChipCells.Clear(); topChipCells.UnionWith(cellDamage.Keys);
            DrawOwnMoveForecast(model, CellRect); // 【RC4】敵が自分で動く先【RC5・レビュー3】数字の札の下に（前は影が「-3」を隠した）
            DrawCellDamage(CellRect); // 【RC2】1マスの合計ダメージを1つだけ（炎・大技・爆撃が重なっても読める）
            if (WarpPicking) DrawWarpTargets(model, CellRect); // 【RC4】転移石の行き先
            else kbWarpCell = null;
            // 【RC2】敵の頭上：名札の右に大技のカウント（丸数字）。①は強く光り、技名を名札の上に出す・敵のマスを縁取る
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || e.Clocks.Count == 0) continue;
                var near = Clocks.Nearest(e);
                if (near == null) continue;
                var r = CellRect(e.Position);
                bool warn = near.Cd == 1;
                float pulse = warn ? Blink(0.4f, 1f, 9f) : 1f;
                // 【企画書 v5・UI】名札（BattleUIShowcase が重ならないように並べた位置）の右端に入れる
                var plate = plateRects.TryGetValue(e, out var pr0) ? pr0 : new Rect(r.center.x - 40, r.y - r.height * 0.16f - 10, 80, 20);
                var chip = new Rect(plate.xMax - PlateChipW, plate.y, PlateChipW, plate.height);
                DrawRect(chip, warn ? new Color(0.85f * pulse, 0.1f, 0.75f * pulse, 0.97f) : new Color(0.05f, 0.06f, 0.14f, 0.92f));
                DrawFrame(chip, warn ? new Color(1f, 0.75f, 1f) : new Color(1f, 0.85f, 0.45f, 0.9f), warn ? 2f : 1.5f);
                DrawUiImage(new Rect(chip.x + 1f, chip.y + (chip.height - 14f) / 2f, 14f, 14f), "hz_clock", 1f, ScaleMode.ScaleToFit); // 【RC6】砂時計＝大技までのカウント（数字だけでは何の数か分からなかった）
                GUI.Label(new Rect(chip.x + 13f, chip.y, chip.width - 13f, chip.height), warn ? "今" : near.Cd.ToString(), new GUIStyle(Tiny) { fontSize = warn ? 12 : 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = warn ? Color.white : new Color(1f, 0.9f, 0.5f) } }); // 【RC6・ブラインドテスト】①は「今」（マスの札と同じ言葉。「1」は「あと1ターン」と読まれた）
                if (warn)
                {
                    DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(1f, 0.45f, 0.95f, pulse), 3f);
                    var nameSt = new GUIStyle(Tiny) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                    float nw = nameSt.CalcSize(new GUIContent(near.Name)).x + 8f;
                    // 【RC7・ブラインドテスト】名札の上だと、上の段の敵の「全まね」（水色の札）と重なって、まね方の札に見えた → 砂時計の右に赤紫の札
                    var nameRect = new Rect(plate.xMax + 1f, plate.y, nw, plate.height);
                    if (!plateRects.Any(kv => kv.Key != e && kv.Value.Overlaps(nameRect)))
                    {
                        DrawRect(nameRect, new Color(0.45f, 0.05f, 0.4f, 0.95f));
                        DrawFrame(nameRect, new Color(1f, 0.6f, 1f, pulse), 1f);
                        ShadowLabel(nameRect, near.Name, nameSt, new Color(1f, 0.9f, 1f));
                    }
                }
                if (e.Silenced) ShadowLabel(new Rect(r.x, r.yMax - 30, r.width, 14), "沈黙", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, new Color(0.7f, 0.9f, 1f));
            }
            // 【RC6】孤立：最後の1体（ボス・中ボスの本体以外）は受けるダメージ2倍。マスの左上に印
            if (model.LastStandDouble)
                foreach (var e in model.Enemies)
                {
                    if (!RulesEngine.IsLastStanding(model, e)) continue;
                    var r = CellRect(e.Position);
                    var tag = new Rect(r.x + 2, r.y + 2, 52, 16);
                    DrawRect(tag, new Color(0.35f, 0.22f, 0.02f, 0.92f));
                    DrawFrame(tag, new Color(1f, 0.8f, 0.35f, Blink(0.6f, 1f, 4f)), 1f);
                    GUI.Label(tag, "孤立×2", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.88f, 0.5f) } });
                    if (r.Contains(Event.current.mousePosition) && hoverTitle == null) { hoverTitle = "孤立"; hoverBody = "最後の1体は、受けるダメージが2倍（ボス・中ボスの本体は除く）。"; hoverPos = new Vector2(Mathf.Min(r.x, 400f), r.yMax + 4); }
                }
            // 【RC2】後追い（コピーのあとに動く）の敵：印をつける。順番が違うのはこの敵だけ
            foreach (var e in model.Enemies)
            {
                if (!e.IsAlive || !e.AfterCopy || PhaseRules.IntentOrder) continue; // 【RC3】予告どおりの順番では全員が同じ順番（後追いの印は出さない）
                var r = CellRect(e.Position);
                var tag = new Rect(r.xMax - 46, r.y + 2, 44, 14); // 【企画書 v5・UI】右上（下の段はまね方の札。前は重なって「技まね」が隠れた）
                DrawRect(tag, new Color(0.35f, 0.18f, 0.02f, 0.92f));
                DrawFrame(tag, new Color(1f, 0.7f, 0.25f), 1f);
                GUI.Label(tag, "後追い", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 0.85f, 0.5f) } });
            }
            // 敵のマスにマウスを乗せたら説明（盤面の上でも読める）
            foreach (var e in turn.Enemies)
            {
                if (!e.IsAlive || e.Model.IsObject) continue;
                var r = CellRect(e.Model.Position);
                if (r.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown && InputPriority.EnemySelectAllowed(Event.current.button, pendingCommand != null, TacticTargeting, PickerOpen, showDetail)) { selectedEnemy = e; Event.current.Use(); } // 【RC2】クリックで詳細（【RC4】札を選んでいる間は方向・距離の入力が先）
                if (selectedEnemy == e) DrawFrame(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), Gold, 2f);
                if (r.Contains(Event.current.mousePosition) && hoverTitle == null)
                {
                    hoverTitle = $"{PhaseRules.ShortName(e.Model)}　HP {e.Hp}/{e.MaxHp}";
                    hoverBody = EnemyHelpNow(e.Model) + (e.Model.Clocks.Count > 0 ? "\n\n大技：" + string.Join("／", e.Model.Clocks.Select(c => $"{c.Name}（あと{c.Cd}）")) : "");
                    hoverPos = new Vector2(Mathf.Min(r.x, 400f), r.yMax + 4);
                }
            }
            // 観測士：残り②の大技の範囲を薄く（①で確定するので、あなたの動きで変わる）
            if (turn.PreviewAhead && turn.State == BattleState.PlayerInput)
                foreach (var e in model.Enemies)
                {
                    if (!e.IsAlive) continue;
                    foreach (var c in e.Clocks.Where(c => c.Cd == 2))
                        foreach (var cell in Clocks.PreviewCells(model, e, c))
                        {
                            var r = CellRect(cell);
                            DrawDashedFrame(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(1f, 0.6f, 0.95f, 0.8f), 2f, 5f);
                            if (!cellDamage.ContainsKey(cell)) DrawHazardChip(r, new CellHazard { Dmg = c.Damage + Clocks.DamageBonus, Icon = "hz_big" }, false, true, false); // 【RC6】②の範囲にも「次」の札
                        }
                }
            // 戦術札：対象にできる敵を光らせる・クリックで選ぶ
            if (TacticTargeting)
            {
                foreach (var e in turn.Enemies)
                {
                    if (!e.IsAlive || !(IsTacticCandidate(e) || e == tacticTarget1)) continue;
                    var r = CellRect(e.Model.Position);
                    bool sel = tacticTarget1 == e;
                    DrawFrame(r, sel ? Gold : TacticGreen, 4f);
                    if (KbTacticTarget() == e) DrawFrame(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), new Color(1f, 1f, 1f, Blink(0.5f, 1f, 6f)), 3f); // 【RC5】キーボードで選んでいる敵（白）【最終ゲート】点滅の設定で止まる
                    ShadowLabel(new Rect(r.x, r.yMax - 16, r.width, 15), sel ? "1体目" : "対象", new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, sel ? Gold : TacticGreen);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) PickTacticTarget(e);
                }
                var bA = CellRect(new Vector2Int(0, 0));
                var bB = CellRect(new Vector2Int(model.Board.Width - 1, model.Board.Height - 1));
                float boardMid = (Mathf.Min(bA.x, bB.x) + Mathf.Max(bA.xMax, bB.xMax)) / 2f, boardTop = Mathf.Min(bA.y, bB.y);
                var hint = new Rect(boardMid - 230, Mathf.Max(100f, boardTop - bA.height * 0.75f - 24f), 460, 20);
                DrawRect(hint, new Color(0f, 0f, 0f, 0.82f));
                string what = TacticRules.TargetOf(tacticPending.tactic) == TacticTarget.EnemyThenEnemy
                    ? (tacticTarget1 == null ? $"『{tacticPending.displayName}』1体目：大技を撃つ敵を選ぶ（矢印で選んで Enter）" : $"『{tacticPending.displayName}』2体目：狙いを向ける敵を選ぶ（矢印で選んで Enter・Esc でやめる）")
                    : $"『{tacticPending.displayName}』対象の敵を選ぶ（クリックか、矢印で選んで Enter・Esc でやめる）";
                GUI.Label(hint, what, new GUIStyle(Tiny) { fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = TacticGreen } });
            }
        }

        // ───────── 敵の説明（V4：まね方・固有行動・大技） ─────────

        static string V4EnemyHelp(UnitModel m, bool noTraits = false)
        {
            string copy = CopyKinds.OneLine(m.CopyKind);
            string trait;
            if (noTraits) return copy + "\nこの戦闘では、まねるだけ（自分からは攻撃しない・近づかない）。攻撃と接近は次の戦闘から。";
            switch (m.Kind)
            {
                // 【RC4】敵の自分の動きは兵種の個性（全員が毎ターン近づくのはやめた）。自分で動く敵は、動く先をオレンジの矢印で予告
                case EnemyKind.Swordsman: trait = "攻撃：隣の4マス（赤）を斬る（敵にも当たる）。ふだんは まねで動く。3マス以上離れると、ターンの最後に1マス詰める（盤面のふちに追い込まれたときも・オレンジの矢印）。"; break; // 【RC5】槍兵と同じ「3マス以上離れると詰める」
                case EnemyKind.Spearman: trait = "攻撃：向いている方の2マス（赤）を突く（敵にも当たる）。ふだんは動かず、毎ターンの最後にあなたの方を向く（次の赤）。3マス以上離れると1マス詰める（オレンジの矢印）。"; break;
                case EnemyKind.Archer: trait = "攻撃：向いている方へ矢（赤い線・4マス・最初に当たった1体）。毎ターンの最後に、あなたの行・列へ1マス動いて狙う（オレンジの矢印）。柱が矢を止める。"; break;
                case EnemyKind.Chaser: trait = "攻撃：隣の4マス（赤）を噛む（敵にも当たる）。毎ターンの最後に、あなたへ2マスまで近づく（オレンジの矢印で予告）。"; break;
                case EnemyKind.Bomber: trait = "攻撃：照準（紫の十字・床に固定）へ爆撃（敵にも当たる・盾も通る）。まねのあと、あなたのいるマスに次の照準をつける。自分では動かない。"; break;
                case EnemyKind.Shield: trait = "攻撃なし。自分では動かない。まねのあと、あなたの方へ盾を向ける。正面からの斬撃・突き・矢は -3（敵のまねでも）。横・背中・砲兵の爆撃・大技は通る。"; break;
                case EnemyKind.Mender: trait = "攻撃なし。まねのあと、いちばん傷ついた仲間を回復する。"; break;
                case EnemyKind.ExecKnight: trait = "攻撃：隣の4マス（赤）を斬る。毎ターンの最後に、あなたへ1マス近づく（オレンジの矢印）。大技は追撃・大薙ぎ（3ターンごと）・処刑（5ターンごと）。処刑のあとは刃が床に刺さって2ターン動けない（攻め時）。処刑の直線は敵にも当たる。"; break;
                case EnemyKind.CannonKing: trait = "行動：動かない。小さな砲撃から巨砲まで3つの大技を撃つ中ボス。"; break;
                case EnemyKind.BeastKing: trait = "毎ターンの最後に、あなたへ1マス近づく（オレンジの矢印）。飛びかかり・咆哮・暴走突進（どれも紫で予告）を持つ中ボス。"; break;
                case EnemyKind.MirrorLord: trait = CombatBoss.DescribeV4(m); break;
                case EnemyKind.HealClone: trait = $"眠っている間は何もしない（まねもしない）。主の段階2で目覚め、毎ターン主を +{CombatBoss.CloneHeal}。まねた攻撃が主に当たると主に -{CombatBoss.CloneStrikeDamage} で砕ける。殴って倒してもよい。"; break;
                default: trait = "行動：なし（まねるだけ）。"; break;
            }
            string clocks = m.Clocks.Count > 0 ? "\n大技：" + string.Join("／", m.Clocks.Select(c => $"{c.Name}（{c.Period}ターンごと・{c.Damage}）")) : "";
            return copy + "\n" + trait + clocks + $"\n向き：{m.Facing.ToLabel()}";
        }
    }
}

namespace CopyCommand
{
    /// <summary>【Combat V4】戦術札（緑）のカードの顔。報酬・商人・デッキの画面で使う。</summary>
    public static class TacticCardView
    {
        public static void Draw(Rect r, CommandData c, bool enabled = true, float alpha = 1f)
        {
            // 【RC2】戦闘の手札と同じカードの見た目（絵つき・緑・命令枠なし）。レア度は右上
            UiStyles.DrawCardV2(r, c, false, false, 0, enabled, alpha, 0);
            var info = ActContent.Info(c.name);
            if (info != null)
            {
                var col = info.Rarity == Rarity.Rare ? new Color(1f, 0.78f, 0.3f) : info.Rarity == Rarity.Uncommon ? new Color(0.65f, 0.85f, 1f) : new Color(0.8f, 0.8f, 0.8f);
                UiStyles.ShadowLabel(new Rect(r.xMax - 40, r.y + 4, 36, 16), info.RarityMark, new GUIStyle(UiStyles.Tiny) { fontSize = 12, alignment = TextAnchor.MiddleRight, wordWrap = false }, col);
            }
        }
    }
}
