using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// IMGUI の共通スタイルと描画部品（BattleUI と RunUI で共有）。プロトタイプ用の仮 UI。
    /// </summary>
    public static class UiStyles
    {
        public const float RefHeight = 720f;

        public static GUIStyle Label, Small, Tiny, Big, Button, CardTitle, CardText, Center;
        /// <summary>【検証中】見た目のテーマ（カード枠の絵）。null なら今までの色の四角。</summary>
        public static VisualTheme Theme;
        static Font font;

        /// <summary>OnGUI の最初に呼ぶ。日本語は Windows のフォントを使う。</summary>
        public static void Setup()
        {
            if (font == null)
            {
                // 【完成版】同梱のフォント（BIZ UDPゴシック・SIL Open Font License 1.1・Resources/Fonts）。WebGL（OS のフォントが無い）でも日本語が出る
                font = Resources.Load<Font>("Fonts/BIZUDPGothic-Regular");
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Segoe UI Symbol", "Segoe UI Emoji", "MS Gothic" }, 16); // 【Puzzle v2】⚔・👣・✓ は記号のフォントで
            }
            GUI.skin.font = font;
            if (Label != null) return;
            Label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = false };
            Label.normal.textColor = Color.white;
            Small = new GUIStyle(Label) { fontSize = 14, wordWrap = true };
            Tiny = new GUIStyle(Label) { fontSize = 12, wordWrap = true };
            Big = new GUIStyle(Label) { fontSize = 28, fontStyle = FontStyle.Bold };
            Button = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            CardTitle = new GUIStyle(Label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            CardText = new GUIStyle(Tiny) { alignment = TextAnchor.UpperCenter };
            Center = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        /// <summary>【RC5】一番狭い横幅（720 の高さで 1152＝16:10）。これより縦長の窓（4:3 など）は、上下に黒い帯を出して 16:10 の画面にする（前は右の欄の文が切れた）。</summary>
        public const float MinGuiWidth = 1152f;
        /// <summary>画面の拡大率（高さ 720 が基準。16:10 より縦長なら横幅 1152 が基準）。</summary>
#if UNITY_WEBGL && !UNITY_EDITOR
        public static float GuiScale => Screen.height / RefHeight; // ブラウザ版はページ（SonoItteFit）がいつも 16:9 に合わせるので、高さだけ（RC4 と同じ。読み込み直後の大きさの食い違いで画面が切れて見えたことがある）
#else
        public static float GuiScale => Mathf.Min(Screen.height / RefHeight, Screen.width / MinGuiWidth);
#endif
        /// <summary>【RC5】縦長の窓の上下の黒い帯の高さ（画面のピクセル。16:10 以上の横長なら 0）。</summary>
        public static float LetterboxY => Mathf.Max(0f, (Screen.height - RefHeight * GuiScale) * 0.5f);

        /// <summary>画面の高さ 720 を基準に拡大縮小する。戻り値は拡大率。【RC5】縦長の窓では上下に黒い帯（中身は 16:10 と同じ並び）。</summary>
        public static float ApplyScale()
        {
            float scale = GuiScale, oy = LetterboxY;
            if (oy > 0.5f && Event.current != null && Event.current.type == EventType.Repaint)
            {
                GUI.matrix = Matrix4x4.identity;
                DrawRect(new Rect(0f, 0f, Screen.width, oy + 1f), Color.black);
                DrawRect(new Rect(0f, Screen.height - oy - 1f, Screen.width, oy + 1f), Color.black);
            }
            GUI.matrix = Matrix4x4.TRS(new Vector3(0f, oy, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
            return scale;
        }

        /// <summary>【RC5】カメラの画面座標（下が 0）→ 画面の座標（上が 0・拡大前）。上下の黒い帯の分をずらす。</summary>
        public static Vector2 ScreenToGui(Vector3 s, float scale) => new Vector2(s.x / scale, (Screen.height - LetterboxY - s.y) / scale);

        /// <summary>【RC5】盤面のカメラの範囲（0〜1）を、上下の黒い帯の内側に合わせる。</summary>
        public static Rect CamRect(Rect r)
        {
            float oy = LetterboxY;
            if (oy <= 0f || Screen.height <= 0) return r;
            float fh = RefHeight * GuiScale / Screen.height, off = oy / Screen.height;
            return new Rect(r.x, off + r.y * fh, r.width, r.height * fh);
        }

        public static void DrawRect(Rect r, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>点線の枠（反射のしるしなど「まだ確定ではない」範囲）。</summary>
        public static void DrawDashedFrame(Rect r, Color c, float t = 2f, float dash = 6f)
        {
            for (float x = r.x; x < r.xMax; x += dash * 2)
            {
                float len = Mathf.Min(dash, r.xMax - x);
                DrawRect(new Rect(x, r.y, len, t), c);
                DrawRect(new Rect(x, r.yMax - t, len, t), c);
            }
            for (float y = r.y; y < r.yMax; y += dash * 2)
            {
                float len = Mathf.Min(dash, r.yMax - y);
                DrawRect(new Rect(r.x, y, t, len), c);
                DrawRect(new Rect(r.xMax - t, y, t, len), c);
            }
        }

        public static void DrawFrame(Rect r, Color color, float thickness = 2f)
        {
            DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
            DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
            DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        /// <summary>【仮デザイン】カードの系統。色と左上の札の種類（移・攻・補・強）を決める。</summary>
        public enum CardCategory { Move, Attack, Support, Buff }

        /// <summary>【検証中】あなたが受けたダメージの原因（短い札）。コピー／反射／剣兵の剣／槍兵の槍／弓兵の矢／爆発／トゲ／炎 など。</summary>
        public static string DamageCauseLabel(BattleEvent e)
        {
            var c = e?.Cause;
            if (c == null) return null;
            switch (c.Kind)
            {
                case DamageKind.Spike: return "トゲ";
                case DamageKind.Fire: return "炎";
                case DamageKind.Curse: return "呪い";
                case DamageKind.Gaze: return "石像の視線";
                case DamageKind.Clock: return "大技（紫）"; // 【RC5】前は「敵の攻撃」にまとめていた（負けた理由が分からない）
            }
            if (c.Kind == DamageKind.Trait)
            {
                switch (c.Source?.Kind)
                {
                    case EnemyKind.Swordsman: return "剣兵の剣";
                    case EnemyKind.Spearman: return "槍兵の槍";
                    case EnemyKind.Bomber: return "爆発";
                    case EnemyKind.Archer: return "弓兵の矢";
                    case EnemyKind.KnightV3: return "騎士の大技";
                    default: return "兵種の技";
                }
            }
            if (c.Source != null && !c.Source.IsPlayer)
            {
                // 「反射」は本当に向きが逆になった攻撃だけ（回転斬り・反転斬りは反射ではなく普通のコピー：レビュー）
                var cmd = c.Command;
                bool reversed = cmd != null && cmd.UsesDirection && !cmd.copyMirrored &&
                                (c.Source.CopyTrait == CopyTrait.Mirror || (c.Source.CopyTrait == CopyTrait.MirrorAttacks && EnemyReplayController.IsReflected(cmd.type)));
                if (c.ByCopy && reversed) return "反射";
                return c.ByCopy ? "まね" : "敵の攻撃";
            }
            return c.Source != null && c.Source.IsPlayer ? "自分" : null;
        }

        public static CardCategory CategoryOf(CommandData data)
        {
            switch (data.type)
            {
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Pierce:
                case CommandType.Sweep:
                case CommandType.AroundSlash: return CardCategory.Attack;
                case CommandType.Push:
                case CommandType.Pull:
                case CommandType.Swap:
                case CommandType.Aim:
                case CommandType.PlaceSpike: return CardCategory.Support;
                case CommandType.Spirit:
                case CommandType.Foresight:
                case CommandType.Reconsider:
                case CommandType.Rally:
                case CommandType.ReverseCopy:
                case CommandType.Draw:
                case CommandType.Insight:
                case CommandType.Resonate:
                case CommandType.Tactic:
                case CommandType.Heal: return CardCategory.Buff;
                default: return CardCategory.Move;
            }
        }

        /// <summary>カードの色（docs/art_refs/ui_target：移動＝青、攻撃＝赤、制御（押し出し・引き寄せ・罠）＝琥珀（RC11 まで紫）、支援（闘気）＝緑）。</summary>
        public static Color CardColor(CommandData data)
        {
            switch (CategoryOf(data))
            {
                case CardCategory.Attack: return new Color(0.62f, 0.15f, 0.13f);
                case CardCategory.Support: return new Color(0.62f, 0.40f, 0.12f); // 【RC11】紫 → 琥珀（紫は床に固定の攻撃の色：初見テスト「押し出しの札の紫と、紫の攻撃が同じ色」）
                case CardCategory.Buff: return new Color(0.14f, 0.50f, 0.34f);
                default: return new Color(0.15f, 0.33f, 0.68f);
            }
        }

        /// <summary>系統の名前（カードの帯に出す）。</summary>
        public static string CategoryName(CommandData data)
        {
            if (data.type == CommandType.Tactic) return TacticCategoryName(data.tactic); // 【RC2】緑のカード（命令枠を使わない）【RC4】帯は系統（何の助けか）
            if (data.type == CommandType.Draw || data.type == CommandType.Insight || data.type == CommandType.Resonate) return "手札"; // 【Puzzle v2】手を回す札（敵がまねても何も起きない）
            switch (CategoryOf(data))
            {
                case CardCategory.Attack: return "攻撃";
                case CardCategory.Support: return "押し引き"; // 【RC2】遊び方の「押す・引く」とそろえる（【RC11】色は琥珀）
                case CardCategory.Buff: return "支援";
                default: return "移動";
            }
        }

        /// <summary>
        /// 【RC4・本人「緑の札をどう活用するか直感的でない」】戦術札の系統（カードの帯・説明に出す）。
        /// 攻撃の助け＝次の攻撃を強く／守り・回復＝受けるダメージを減らす・HP／敵を操る＝大技の時間・まねの距離・敵を止める／手札・命令＝引く・命令の枠。
        /// </summary>
        public static string TacticCategoryName(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.Focus: return "攻撃の助け";
                case TacticKind.Guard: case TacticKind.Mend: case TacticKind.Parry: return "守り・回復";
                case TacticKind.Breath: case TacticKind.Sift: case TacticKind.Muster: case TacticKind.GreatMuster: case TacticKind.Recall: case TacticKind.Echo: return "手札・命令";
                case TacticKind.None: return "戦術札";
                default: return "敵を操る";
            }
        }

        /// <summary>テーマの UI 画像の名前に使う系統のキー。</summary>
        public static string CategoryKey(CommandData data)
        {
            switch (CategoryOf(data))
            {
                case CardCategory.Attack: return "attack";
                case CardCategory.Support: return "control";
                case CardCategory.Buff: return "support";
                default: return "move";
            }
        }

        /// <summary>【検証中】テーマのカード枠（無ければ null）。</summary>
        static Texture2D CardFrame(CommandData data) => Theme != null ? Theme.Ui("card_" + CategoryKey(data)) : null;

        /// <summary>カードの絵の名前（ui_cardart_〇〇）。</summary>
        public static string ArtKey(CommandData d)
        {
            switch (d.type)
            {
                case CommandType.Walk: return d.notCopied ? "sneak" : "walk";
                case CommandType.Rush: return d.maxDistance <= 1 ? "walk" : d.minDistance <= 2 && d.maxDistance <= 2 ? "dash" : "rush";
                case CommandType.Jump: return "jump";
                case CommandType.Diagonal: return "diagonal";
                case CommandType.Slide: return "slide";
                case CommandType.Blink: return "blink";
                case CommandType.WallKick: return "wallkick";
                case CommandType.Vault: return "vault";
                case CommandType.Slash: return d.copyMirrored ? "mirrorslash" : d.range > 1 ? "thrust" : "slash";
                case CommandType.PushSlash: return "pushslash";
                case CommandType.Sweep: return "sweep";
                case CommandType.Pierce: return "pierce";
                case CommandType.AroundSlash: return "around";
                case CommandType.Spirit: return "spirit";
                case CommandType.Push: return "push";
                case CommandType.Pull: return "pull";
                case CommandType.Swap: return "swap";
                case CommandType.PlaceSpike: return "spike";
                case CommandType.Foresight: return "foresight";
                case CommandType.Reconsider: return "reconsider";
                case CommandType.Rally: return "rally";
                case CommandType.Draw:
                case CommandType.Resonate: return "reconsider";
                case CommandType.Insight: return "foresight";
                case CommandType.ReverseCopy: return "reverse";
                case CommandType.Heal: return "heal";
                case CommandType.Aim: return "foresight";
                case CommandType.Tactic: return "tac_" + d.tactic.ToString().ToLower();
                default: return "walk";
            }
        }

        /// <summary>カードの左上の数字（移動＝マス数、攻撃＝ダメージ、闘気＝上がる量）。コストではない（エネルギー制は使わない）。</summary>
        /// <summary>【RC5・レビュー3】左上の数字の単位（コストに見えないように：KNOWN_ISSUES C3）。</summary>
        public static string CardNumberUnit(CommandData d)
        {
            switch (d.type)
            {
                case CommandType.Walk: case CommandType.Rush: case CommandType.Jump: case CommandType.Diagonal: case CommandType.Blink: case CommandType.Pull: case CommandType.Push: return "マス";
                case CommandType.Slash: case CommandType.PushSlash: case CommandType.Sweep: case CommandType.Pierce: case CommandType.AroundSlash: return "ダメ";
                case CommandType.Heal: return "HP";
                default: return "";
            }
        }

        public static string CardNumber(CommandData d)
        {
            switch (d.type)
            {
                case CommandType.Walk:
                case CommandType.Rush: return d.minDistance == d.maxDistance ? d.minDistance.ToString() : $"{d.minDistance}-{d.maxDistance}";
                case CommandType.Jump: return "2";
                case CommandType.Diagonal: return "1";
                case CommandType.Blink: return "3";
                case CommandType.Slide: return "∞";
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Sweep:
                case CommandType.Pierce:
                case CommandType.AroundSlash: return d.damage.ToString();
                case CommandType.Spirit: return "+" + Mathf.Max(1, d.range);
                case CommandType.Reconsider: return "+" + Mathf.Max(1, d.range);
                case CommandType.Foresight: return "3";
                case CommandType.Insight: return "3";
                case CommandType.Draw: return "+" + Mathf.Max(1, d.range);
                case CommandType.Resonate: return "+" + Mathf.Max(1, d.range);
                case CommandType.Rally:
                case CommandType.Heal: return "+" + Mathf.Max(1, d.range);
                case CommandType.Pull: return Mathf.Max(2, d.range).ToString();
                case CommandType.Push: return "1";
                case CommandType.Tactic: return "";
                default: return "";
            }
        }

        /// <summary>カードに出す「自分が何をするか」（短い文。カードの幅で2行まで）。</summary>
        public static string SelfText(CommandData d, int bonus = 0) // bonus＝鍛えた分（あなたが使うときだけ）
        {
            int dmg = d.damage + bonus;
            switch (d.type)
            {
                case CommandType.Tactic: return TacticRules.Short(d.tactic);
                case CommandType.Walk: return d.notCopied ? "1マス移動（敵はまねしない）。" : d.copyMirrored ? "1マス移動（敵は反対向き）。" : $"{Mathf.Max(1, d.minDistance)}マス移動。";
                case CommandType.Foresight: return "次のターン、山札の上3枚から1枚を手札へ。";
                case CommandType.Reconsider: return $"次のターン +{Mathf.Max(1, d.range)}枚引く。";
                case CommandType.Rally: return d.planDraw > 0 ? $"このターン命令 +{Mathf.Max(1, d.range)}・すぐ{d.planDraw}枚引く{(d.exhaust ? "（1戦1回）" : "")}。" : $"このターン命令の上限 +{Mathf.Max(1, d.range)}。";
                case CommandType.Draw: return d.fromDiscard ? $"すぐ捨て札から{(d.drawFilter == DrawFilter.Move ? "移動の札を" : "")}{Mathf.Max(1, d.range)}枚選んで手札へ。" : d.drawFilter == DrawFilter.Move ? $"すぐ移動の札を{Mathf.Max(1, d.range)}枚引く。" : d.drawFilter == DrawFilter.Technique ? $"すぐ技の札を{Mathf.Max(1, d.range)}枚引く。" : $"すぐ{Mathf.Max(1, d.range)}枚引く（この命令の後に使える）。";
                case CommandType.Insight: return "すぐ山札の上3枚から1枚を手札へ。";
                case CommandType.Resonate: return $"このラウンド、まねで仕掛けが動けば次のターン +{Mathf.Max(1, d.range)}枚。";
                case CommandType.ReverseCopy: return "このターン、敵は逆の順番でまね。";
                case CommandType.Heal: return $"自分の HP を {Mathf.Max(1, d.range)} 回復{(d.exhaust ? "（1戦1回）" : "")}。";
                case CommandType.Rush: return d.minDistance == d.maxDistance ? (d.minDistance <= 1 ? "1マス進む。" : $"{d.minDistance}マス進む（止まれない）。") : $"{d.minDistance}か{d.maxDistance}マス進む。"; // 【RC5】1マスの札に「止まれない」は要らない
                case CommandType.Jump: return "2マス先へ跳ぶ（間の壁・柱・敵も越える）。";
                case CommandType.Diagonal: return "斜めに1マス移動。";
                case CommandType.Slide: return "ぶつかるまで滑る。";
                case CommandType.Blink: return "3マス先へ転移（壁も越える）。";
                case CommandType.WallKick: return "壁を背にすると3マス。";
                case CommandType.Vault: return "正面の敵を越えて2マス先へ。";
                case CommandType.Slash: return d.range > 1 ? $"正面 {d.range}マス先まで {dmg}。" : $"正面の隣に {dmg}ダメージ。";
                case CommandType.PushSlash: return $"隣に {dmg}ダメージ＋1マス押す。"; // 【RC2】「3＋1マス」と読めた
                case CommandType.Sweep: return $"正面と左右の3マスに {dmg}。";
                case CommandType.Pierce: return $"正面の直線の全員に {dmg}。";
                case CommandType.AroundSlash: return $"上下左右に {dmg}。";
                case CommandType.Spirit: return $"次の攻撃 +{Mathf.Max(1, d.range)}（このターン）。";
                case CommandType.Push: return "正面の隣を1マス押す。";
                case CommandType.Pull: return $"{Mathf.Max(2, d.range)}マス内の1体を1マス引く。";
                case CommandType.Swap: return "隣と位置を入れ替える。";
                case CommandType.PlaceSpike: return "正面の隣にトゲを置く。";
                default: return d.description;
            }
        }

        /// <summary>カードに出す「敵がコピーすると何をするか」。敵は自分の位置から、同じ方向・同じ距離で実行する。</summary>
        /// <summary>【検証中・Copy Core V2】カードの「敵コピー」の文をどの方式で書くか（サンドボックスの戦闘だけ B/C/D。ほかは A）。</summary>
        public static CopyMode CopyTextMode = CopyMode.Current;
        public static bool CopyTextOff;

        public static string CopyText(CommandData d)
        {
            if (d.type == CommandType.Tactic) return "敵はまねしない・" + (TacticRules.TargetOf(d.tactic) == TacticTarget.None ? "すぐ使う" : TacticRules.TargetOf(d.tactic) == TacticTarget.Enemy ? "敵を選んで使う" : "敵を2体選ぶ"); // 【RC3】「枠なし」が分かりにくかった（本人）
            if (CopyTextOff) return "COPY OFF：敵はまねしない。";
            if (CopyTextMode != CopyMode.Current && d.UsesDirection && !d.notCopied) return CopyTextV2(d, CopyTextMode);
            switch (d.type)
            {
                case CommandType.Foresight:
                case CommandType.Draw:
                case CommandType.Insight:
                case CommandType.Resonate:
                case CommandType.Reconsider: return "敵は何もしない（命令1つ分が空く）。";
                case CommandType.Rally: return "増えた命令も全部まねされる。";
                case CommandType.ReverseCopy: return "敵は何もしない（順番だけ逆に）。";
                case CommandType.Heal: return $"敵も自分の HP を {Mathf.Max(1, d.range)} 回復。";
                case CommandType.Slash when d.copyMirrored: return "敵は反対向き（鏡兵・騎士はあなたと同じ向き）。";
                case CommandType.Slash when d.copyWide: return $"まねた敵は正面と左右の3マスに {d.damage}（あなたより広い）。"; // 【RC5・レビュー】合わせ斬り：前は「敵も同じ攻撃」と出ていた
                case CommandType.Slash when d.copyDamageCut > 0: return $"敵も斬るが、威力は {Mathf.Max(0, d.damage - d.copyDamageCut)}（欠けた写し）。";
                case CommandType.PushSlash when d.copyDamageCut < 0: return $"まねた敵は隣に {d.damage - d.copyDamageCut}＋1マス押す（あなたより強い）。";
                case CommandType.Rush when d.copyDistanceCut < 0: return $"まねた敵は {d.minDistance - d.copyDistanceCut}マス進む（あなたより遠く）。";
                case CommandType.Aim: return "まねた敵も向きだけ変える（槍兵・弓兵の向きを決める）。";
                case CommandType.Rush when d.copyDistanceCut > 0: return $"敵は {Mathf.Max(0, d.minDistance - d.copyDistanceCut)}マスだけ動く（短い写し）。";
                case CommandType.Walk when d.notCopied: return "まねされない（敵は飛ばす）。";
                case CommandType.Walk when d.copyMirrored: return "敵は反対向きに1マス（鏡兵は同じ向き）。";
                case CommandType.WallKick: return "背中が壁・柱・端の敵だけ3マス、他は1マス。";
                case CommandType.Jump: return "敵も2マス跳ぶ（間の柱・壁・敵も越える）。";
                case CommandType.Walk:
                case CommandType.Rush:
                case CommandType.Diagonal:
                case CommandType.Slide:
                case CommandType.Blink:
                case CommandType.Vault: return "敵も同じ向きに同じだけ動く。";
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Sweep:
                case CommandType.Pierce: return "敵も同じ攻撃（あなたにも当たる）";
                case CommandType.AroundSlash: return "敵も上下左右を攻撃。";
                case CommandType.Spirit: return $"敵の次の攻撃も +{Mathf.Max(1, d.range)}。";
                case CommandType.Push: return "敵も同じ向きに押す。";
                case CommandType.Pull: return "敵も同じ向きから引く。";
                case CommandType.Swap: return "敵も同じ向きの隣と入れ替わる。";
                case CommandType.PlaceSpike: return "敵も同じ向きにトゲを置く。";
                default: return d.copyNote;
            }
        }

        /// <summary>【Copy Core V2】B・C（反対向き）／D（あなたへ）の「敵コピー」の文。</summary>
        static string CopyTextV2(CommandData d, CopyMode m)
        {
            if (m == CopyMode.MimicReflectImmediate)
            {
                bool tech = CopyCore.IsTechnique(d.type);
                if (d.type == CommandType.Slash && d.copyDamageCut > 0) return $"敵もすぐ{(d.copyMirrored ? "同じ" : "反対")}向きに斬るが、威力は {Mathf.Max(0, d.damage - d.copyDamageCut)}。";
                if (!tech) return d.copyMirrored ? "敵もすぐ反対向きに動く（この札だけ逆）。" : "敵もすぐ同じ向きに同じだけ動く（まねる）。";
                if (d.copyMirrored) return "敵もすぐ同じ向きに使う（この札ははね返らない）。";
                return d.type == CommandType.Push || d.type == CommandType.Pull || d.type == CommandType.Swap || d.type == CommandType.PlaceSpike
                    ? "敵もすぐ反対向きに使う（はね返す）。" : "敵もすぐ反対向きに攻撃（当てた敵から返ってくる）。";
            }
            bool toward = m == CopyMode.TowardImmediate;
            bool rev = d.copyMirrored; // 反転斬り・誘い足：方式の向きのさらに反対
            string dir = toward ? (rev ? "あなたと反対の方へ" : "あなたの方へ") : (rev ? "同じ向きに" : "反対向きに");
            string when = CopyCore.IsImmediate(m) ? "すぐ" : "あとで";
            switch (d.type)
            {
                case CommandType.Slash when d.copyDamageCut > 0: return $"敵も{when}{dir}斬るが、威力は {Mathf.Max(0, d.damage - d.copyDamageCut)}。";
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Sweep:
                case CommandType.Pierce: return toward ? $"敵も{when}あなたの方へ攻撃する。" : rev ? $"敵も{when}同じ向きに攻撃（背後の敵に注意）。" : $"敵も{when}反対向きに攻撃（向かい合うと斬り返される）。";
                case CommandType.Push: return $"敵も{when}{dir}押す。";
                case CommandType.Pull: return $"敵も{when}{dir}引く。";
                case CommandType.PlaceSpike: return $"敵も{when}{dir}トゲを置く。";
                case CommandType.WallKick: return $"敵も{when}{dir}動く（背中が壁の敵だけ3マス）。";
                default: return toward ? $"敵も{when}あなたの方へ同じだけ動く。" : rev ? $"敵も{when}同じ向きに同じだけ動く。" : $"敵も{when}反対向きに同じだけ動く（向かい合うと近づく）。";
            }
        }

        static readonly System.Collections.Generic.Dictionary<Texture2D, GUIStyle> nineStyles = new System.Collections.Generic.Dictionary<Texture2D, GUIStyle>();

        /// <summary>9 スライスで描く（ふち border px は伸ばさない）。tint は色の掛け算。</summary>
        public static void Draw9(Rect r, Texture2D tex, int border, Color tint)
        {
            if (tex == null || Event.current.type != EventType.Repaint) return;
            if (!nineStyles.TryGetValue(tex, out var st))
            {
                st = new GUIStyle { border = new RectOffset(border, border, border, border) };
                st.normal.background = tex;
                nineStyles[tex] = st;
            }
            var old = GUI.color;
            GUI.color = tint;
            st.Draw(r, false, false, false, false);
            GUI.color = old;
        }

        /// <summary>テーマの UI 画像を 9 スライスで描く（無ければ色の四角）。</summary>
        public static void DrawUi(Rect r, string name, Color fallback, float alpha = 1f)
        {
            var tex = Theme != null ? Theme.Ui(name) : null;
            if (tex == null) { DrawRect(r, new Color(fallback.r, fallback.g, fallback.b, fallback.a * alpha)); return; }
            Draw9(r, tex, Theme.UiBorder(name), new Color(1f, 1f, 1f, alpha));
        }

        /// <summary>テーマの UI 画像をそのまま（縦横比を保って切り抜き）描く。無ければ何もしない。</summary>
        public static bool DrawUiImage(Rect r, string name, float alpha = 1f, ScaleMode mode = ScaleMode.ScaleAndCrop)
        {
            var tex = Theme != null ? Theme.Ui(name) : null;
            if (tex == null) return false;
            var old = GUI.color;
            GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
            GUI.DrawTexture(r, tex, mode);
            GUI.color = old;
            return true;
        }

        static readonly Color CardGold = new Color(0.95f, 0.8f, 0.45f);

        /// <summary>【検証中】カード1枚（戦闘の手札・報酬・商人で共通。参考画像のカード：左上の数字・名前・絵・系統の帯・自分の効果・敵コピー）。</summary>
        /// <summary>行頭に来てはいけない文字（句読点・閉じかっこ・小さい仮名など）。</summary>
        const string NoLineStart = "。、）」』！？ー…・：ぁぃぅぇぉっゃゅょァィゥェォッャュョ";
        static readonly System.Collections.Generic.Dictionary<string, string> wrapCache = new System.Collections.Generic.Dictionary<string, string>();

        /// <summary>
        /// 【RC5・レビュー】日本語の折り返し（禁則）：IMGUI の折り返しは「。」だけの行を作る（「押す」＋改行＋「。」）。
        /// 幅を超えたら改行を入れる。行頭に来てはいけない文字は前の行にぶら下げる。描くときは wordWrap = false で。
        /// </summary>
        static bool IsWordChar(char c) => (c >= '\u30A0' && c <= '\u30FF' && c != '\u30FB') || char.IsDigit(c) || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '０' && c <= '９') || c == '-' || c == '+' || c == '−'; // 「-2」を切らない（【RC5・レビュー3】全角の「＋」は語に含めない：「3ダメージ＋1マス」が切れない1語になっていた）
        /// <summary>【RC5・レビュー3】行末に来てはいけない文字（開きかっこ）。</summary>
        const string NoLineEnd = "（「『［(【";

        public static string WrapJa(string text, GUIStyle style, float width) => WrapJa(text, style, width, false);

        /// <summary>greedy＝行をいっぱいまで詰める（文節より行数を優先：狭い欄で入りきらないときの最後の手段。WrapFit が使う）。</summary>
        public static string WrapJa(string text, GUIStyle style, float width, bool greedy)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return text;
            string key = text + "|" + style.fontSize + "|" + (int)width + (greedy ? "|g" : "");
            if (wrapCache.TryGetValue(key, out var hit)) return hit;
            var s = new GUIStyle(style) { wordWrap = false };
            var sb = new System.Text.StringBuilder();
            var lines = new System.Collections.Generic.List<string>();
            foreach (var para in text.Split('\n'))
            {
                lines.Clear();
                int n = para.Length, start = 0;
                while (start < n)
                {
                    if (lines.Count > 0) while (start < n && para[start] == ' ') start++; // 【RC5・最終検証】折り返した行の頭に半角空白を残さない
                    if (start >= n) break;
                    int end = start + 1; // para[start..end) が幅に入る（最低1文字）
                    while (end < n && s.CalcSize(new GUIContent(para.Substring(start, end + 1 - start))).x <= width) end++;
                    if (end >= n) { lines.Add(para.Substring(start)); break; }
                    int cut = ChooseBreak(para, start, end, greedy);
                    lines.Add(para.Substring(start, cut - start));
                    start = cut;
                }
                // 【RC5・レビュー3】最後の行が1文字（＋句読点）だけにならないように、前の行の最後の1文字を下ろす（「く。」「る」だけの行）
                int ln = lines.Count;
                if (ln >= 2)
                {
                    string last = lines[ln - 1], prev = lines[ln - 2];
                    string lastBody = last.TrimEnd('。', '、', '）', '」');
                    int body = lastBody.Length;
                    if (body <= 1 && !(body == 1 && IsWordChar(lastBody[0])) && prev.Length >= 4 && NoLineStart.IndexOf(prev[prev.Length - 1]) < 0 && NoLineEnd.IndexOf(prev[prev.Length - 1]) < 0)
                    {
                        lines[ln - 2] = prev.Substring(0, prev.Length - 1);
                        lines[ln - 1] = prev[prev.Length - 1] + last;
                    }
                }
                foreach (var l in lines) sb.Append(l).Append('\n');
            }
            string result = sb.ToString().TrimEnd('\n');
            if (wrapCache.Count > 2000) wrapCache.Clear();
            wrapCache[key] = result;
            return result;
        }

        static bool IsKanji(char c) => (c >= '\u4E00' && c <= '\u9FFF') || c == '々' || c == '〆';
        static bool IsHiragana(char c) => c >= '\u3041' && c <= '\u309F';
        /// <summary>この文字の後ろで切ると読みやすい（句読点・中黒・閉じかっこ・コロン・矢印）。</summary>
        const string GoodBreakAfter = "、。・：，．）」』】！？／";
        /// <summary>文節の終わりになりやすい ひらがな（助詞・活用の終わり）。「遊び／方」「押し／出し」の び・し は入れない。</summary>
        const string ParticleEnds = "のをにはがでとへもやかてたるいくず";
        /// <summary>ひらがなの前でも切ってよい助詞（「範囲に／いる今」「あなたの／こと」）。</summary>
        const string ParticleBreak = "のをにはがへも";

        /// <summary>
        /// 【通常リリース版・画面の直し 16「水色の／字」「誰／の攻撃か」】行を切る位置（start &lt; 戻り値 &lt;= end）。para[start..end) は幅に入り、para[end] で溢れる。
        /// 守ること：行頭禁止の文字で始めない・開きかっこで終えない・カタカナ／数字／英字の語の途中で切らない・半角空白の直後の数字を切り離さない（「隣に 4」）。
        /// 選ぶ順（行が短くなりすぎない範囲＝7 割より後ろ）：① 句読点・中黒・閉じかっこの後 → ② 助詞などの ひらがな → 漢字／カタカナ の境（文節の切れ目。ただし「の／字）」の1字だけの語は切らない）
        /// → ③ 語の中らしい所（漢字→ひらがな「誰／の」・漢字どうし「水／色」・送りがな→漢字「遊び／方」・ひらがなどうし「まね／る」）以外 → ④ 守ることだけ満たす一番後ろ。どれも無ければ前と同じ（行頭禁止の文字はぶら下げ）。
        /// </summary>
        static int ChooseBreak(string para, int start, int end, bool greedy = false)
        {
            bool Valid(int b)
            {
                char p = para[b - 1], c = para[b];
                if (NoLineStart.IndexOf(c) >= 0 || NoLineEnd.IndexOf(p) >= 0) return false;
                if (IsWordChar(p) && IsWordChar(c)) return false;
                if (p == ' ' && IsWordChar(c)) return false;
                if (p == '→' || c == '→' || (p == ' ' && b >= 2 && para[b - 2] == '→') || (c == ' ' && b + 1 < para.Length && para[b + 1] == '→')) return false; // 【第2巡 9】「斬撃 4 →／6」：矢印の前後では切らない
                return true;
            }
            int minB = greedy ? end + 1 : start + Mathf.Max(1, Mathf.CeilToInt((end - start) * 0.55f)); // greedy は下の「守ることだけ」へ
            for (int b = end; b >= minB; b--) if (Valid(b) && GoodBreakAfter.IndexOf(para[b - 1]) >= 0) return b;
            for (int b = end; b >= minB; b--)
            {
                if (!Valid(b) || ParticleEnds.IndexOf(para[b - 1]) < 0 || IsHiragana(para[b])) continue;
                bool oneKanjiWord = IsKanji(para[b]) && (b + 1 >= para.Length || (!IsKanji(para[b + 1]) && !IsHiragana(para[b + 1])));
                if (!oneKanjiWord) return b;
            }
            for (int b = end; b >= minB; b--)
            {
                if (!Valid(b)) continue;
                char p = para[b - 1], c = para[b];
                if (IsKanji(p) && (IsHiragana(c) || IsKanji(c))) continue; // 「誰／の」「水／色」
                if (IsHiragana(p) && IsKanji(c) && ParticleEnds.IndexOf(p) < 0) continue; // 「遊び／方」
                if (IsHiragana(p) && IsHiragana(c) && ParticleBreak.IndexOf(p) < 0) continue; // 「まね／る」（「範囲に／いる」は可）
                return b;
            }
            for (int b = end; b > start; b--) if (Valid(b)) return b;
            // 守れる所が無い：行頭禁止の文字はぶら下げ（最後の手段）
            int hang = end;
            while (hang < para.Length && NoLineStart.IndexOf(para[hang]) >= 0) hang++;
            return hang > end ? hang : end;
        }

        /// <summary>【RC5・レビュー3】禁則で折り返して、欄の高さに入りきらなければ文字を小さくする（最小 minSize）。札の文が 720p で切れていた。</summary>
        public static string WrapFit(string text, GUIStyle style, Rect rect, int minSize, out GUIStyle fitted)
        {
            var s = new GUIStyle(style) { wordWrap = false };
            string w = WrapJa(text, s, rect.width);
            while (s.fontSize > minSize && s.CalcHeight(new GUIContent(w), rect.width) > rect.height + 1f) { s.fontSize--; w = WrapJa(text, s, rect.width); }
            if (s.CalcHeight(new GUIContent(w), rect.width) > rect.height + 1f)
            {
                // 【通常リリース版・画面の直し 16】文節で切ると行が増えて入りきらないときは、行をいっぱいまで詰める（大きい字から）
                var g = new GUIStyle(style) { wordWrap = false };
                string gw = WrapJa(text, g, rect.width, true);
                while (g.fontSize > minSize && g.CalcHeight(new GUIContent(gw), rect.width) > rect.height + 1f) { g.fontSize--; gw = WrapJa(text, g, rect.width, true); }
                s = g; w = gw;
            }
            fitted = s;
            return w;
        }

        /// <summary>
        /// 【RC9・本人「カードの文字が多い」】戦闘の手札の札は短く：名前・絵・効果1行・まね1行（全文は右下の欄）。DrawShowHand が描く間だけ true。
        /// </summary>
        public static bool CompactCards;

        /// <summary>【RC10】数字キーで札を選んだことがあるか（戦闘の手札のキー番号は、それまで出さない）。</summary>
        public static bool KeyNumbersSeen;

        /// <summary>【RC9】敵のまねの短い1行（札の顔）。全文は CopyText（右下の欄）。</summary>
        public static string CopyShort(CommandData d, int copyDelta = 0)
        {
            if (d.type == CommandType.Tactic || d.notCopied) return "まねされない";
            if (CopyTextOff) return "まねされない（COPY OFF）";
            if (d.IsNoBoardEffect) return "敵は何もしない";
            int cd = Mathf.Max(0, d.damage - d.copyDamageCut + copyDelta);
            switch (d.type)
            {
                case CommandType.Walk when d.copyMirrored: return "敵は反対向きに1マス";
                case CommandType.Rush when d.copyDistanceCut != 0: return $"敵も同じ向きに{Mathf.Max(0, d.minDistance - d.copyDistanceCut)}マス";
                case CommandType.Walk:
                case CommandType.Rush:
                case CommandType.Diagonal:
                case CommandType.Slide:
                case CommandType.Blink:
                case CommandType.Vault:
                case CommandType.Jump: return "敵も同じ向きに動く";
                case CommandType.WallKick: return "敵も同じ向きに動く（壁を背に3）";
                case CommandType.Slash when d.copyMirrored: return $"敵は反対向きに {cd}";
                case CommandType.Slash when d.copyWide: return $"敵は前3マスに {cd}";
                case CommandType.Slash:
                case CommandType.PushSlash:
                case CommandType.Sweep:
                case CommandType.Pierce: return $"敵も同じ攻撃 {cd}（あなたにも）";
                case CommandType.AroundSlash: return $"敵も上下左右に {cd}";
                case CommandType.Push: return "敵も同じ向きに押す";
                case CommandType.Pull: return "敵も同じ向きから引く";
                case CommandType.Swap: return "敵も隣と入れ替わる";
                case CommandType.PlaceSpike: return "敵もトゲを置く";
                case CommandType.Heal: return "敵も回復する";
                case CommandType.Spirit: return "敵の次の攻撃も＋";
                case CommandType.Aim: return "敵も向きだけ変える";
                default:
                    var full = CopyText(d) ?? "";
                    return full.Length > 16 ? full.Substring(0, 15) + "…" : full;
            }
        }

        public static void DrawCardV2(Rect r, CommandData d, bool selected, bool planned, int slotNo, bool enabled, float alpha, int keyNumber, int forge = 0, int copyDelta = 0)
        {
            bool compact = CompactCards;
            // 【RC5・レビュー】鍛えた札：あなたが使うときの数字（敵のまねは元の数字）。前は札の顔が元の数字のままだった
            bool copyUp = copyDelta != 0 && d.damage > 0 && d.type != CommandType.Tactic; // 【RC6】まね＋・まね－
            bool forged = forge > 0 && d.damage > 0 && d.type != CommandType.Tactic && !copyUp;
            bool selfPlus = forge > 0 && d.damage > 0 && d.type != CommandType.Tactic; // 【RC6】まね－の札も あなたが使うと +1
            string cat = CategoryKey(d);
            var col = CardColor(d);
            if (selected) r = new Rect(r.x, r.y - 6, r.width, r.height);
            float dim = planned ? 0.42f : enabled ? (compact && !selected ? 0.76f : 1f) : 0.62f; // 【RC10・UI/UX「手札が盤面より強く見える」】戦闘の手札は選んでいない札を少し暗く（盤面を一番強く）
            var tint = new Color(dim, dim, dim, alpha);
            var frame = Theme != null ? Theme.Ui("card_" + cat) : null;
            if (frame != null) Draw9(r, frame, Theme.UiBorder("card_" + cat), tint);
            else { DrawRect(r, new Color(col.r * dim, col.g * dim, col.b * dim, alpha)); DrawFrame(r, new Color(1f, 1f, 1f, 0.4f), 1.5f); }
            bool small = r.width < 130f;

            // 名前
            var tStyle = new GUIStyle(CardTitle) { fontSize = small ? 13 : 15, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0) };
            string shown = KidNames != null ? KidNames(d) : d.displayName; // 【文化祭】はじめては ひらがな
            string nameText = forged || copyUp ? shown + "＋" : shown;
            ShadowLabel(new Rect(r.x + 22, r.y + 5, r.width - 44, 24), nameText, Fit(tStyle, nameText, r.width - 44, 10), new Color(dim, dim, dim * 0.95f + 0.05f, alpha)); // 【RC5・レビュー3】右上のキーの番号と重ならない幅
            // 絵
            float ax = r.x + 9, aw = r.width - 18;
            float copyH = compact ? 42f : r.height >= 165f ? 54f : 40f; // 【RC9】短い札は まね（見出し＋2行まで）
            float ah = r.height < 150f ? Mathf.Clamp(r.height - 30f - copyH - 44f, 14f, 22f) : Mathf.Clamp(r.height - 30f - copyH - 44f, 30f, aw * 0.56f); // 【RC5・レビュー】背の低い札（デッキの一覧）で効果の文が隠れていた
            var art = new Rect(ax, r.y + 29, aw, ah);
            var artOld = GUI.color;
            if (compact) GUI.color = new Color(artOld.r * 0.58f, artOld.g * 0.58f, artOld.b * 0.62f, artOld.a); // 【通常リリース版・画面の直し 1】戦闘の手札は札の地（絵）を暗く：色は枠と種類の帯に（盤面を一番強く）
            if (!DrawUiImage(art, "cardart_" + ArtKey(d), alpha * dim)) DrawRect(art, new Color(col.r * 0.5f, col.g * 0.5f, col.b * 0.5f, alpha));
            GUI.color = artOld;
            DrawFrame(art, new Color(0f, 0f, 0f, 0.5f * alpha), 1f);
            // 系統の帯
            var rib = new Rect(r.x + r.width / 2 - 34, art.yMax - 9, 68, 18);
            if (!DrawUiImage(rib, "ribbon_" + cat, alpha, ScaleMode.StretchToFill)) DrawRect(rib, col);
            GUI.Label(rib, CategoryName(d), new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, normal = { textColor = new Color(1f, 1f, 1f, alpha * dim) } });
            if (forged) // 【RC5・コードレビュー】鍛えた分と まねの数字は絵の上の札（文の欄に足すと 720p で切れた）。まねの数字は RulesEngine の Dmg と同じ式
            {
                var ft = new Rect(art.x + 2, art.y + 1, art.width - 4, 14);
                DrawRect(ft, new Color(0.25f, 0.17f, 0.02f, 0.88f * alpha));
                string ftxt = $"自分＋{forge}"; // 【通常リリース版・画面の直し 1】「自分+2（敵のまね 4）」が狭い札で両端から切れていた → 短く（敵のまねの数字は下の「敵も N」・札の文にある）
                GUI.Label(ft, ftxt, Fit(new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(1f, 0.9f, 0.5f, alpha) } }, ftxt, ft.width - 2, 8));
            }
            else if (copyUp) // 【RC6】まね＋（敵どうしに強い）・まね－（安全）：敵のまねの数字。式は RulesEngine の Dmg と同じ
            {
                var ft = new Rect(art.x + 2, art.y + 1, art.width - 4, 14);
                DrawRect(ft, copyDelta > 0 ? new Color(0.02f, 0.2f, 0.26f, 0.9f * alpha) : new Color(0.08f, 0.18f, 0.1f, 0.9f * alpha));
                string ctxt = $"{(copyDelta > 0 ? "まね＋" : "まね－")}{Mathf.Abs(copyDelta)}{(selfPlus ? $"・自分＋{forge}" : "")}"; // 【通常リリース版・画面の直し 1】短く（敵のまねの数字は下の「敵も N」・札の文）
                GUI.Label(ft, ctxt, Fit(new GUIStyle(Tiny) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = copyDelta > 0 ? new Color(0.6f, 0.95f, 1f, alpha) : new Color(0.7f, 1f, 0.75f, alpha) } }, ctxt, ft.width - 2, 8));
            }
            if (compact) DrawCardFace(r, d, art, alpha, dim, selfPlus ? d.damage + forge : -1, copyDelta, small, copyH); // 【RC10】戦闘の手札：アイコン＋短い文字（全文は右下）
            else
            {
            // 自分の効果
            float ty = art.yMax + 10;
            var body = new GUIStyle(Tiny) { fontSize = small ? 11 : 12, wordWrap = true, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft }; // 【RC5】10/11 → 11/12（720p で札の効果が読める）
            body.normal.textColor = new Color(dim, dim, dim, alpha);
            var bodyRect = new Rect(r.x + 9, ty, r.width - 18, r.yMax - copyH - 6 - ty);
            string self = compact ? ShortText(d, selfPlus ? d.damage + forge : -1) : selfPlus ? SelfText(d, forge) : SelfText(d); // 【RC5・コードレビュー】前は「Nダメージ」の文だけ置き換えていて、「…に 3。」の札は元の数字のままだった【RC9】短い札は効果1行
            string selfW = WrapFit(self, body, bodyRect, 9, out var bodyFit);
            GUI.Label(bodyRect, selfW, bodyFit); // 【RC5】禁則【レビュー3】入りきらなければ小さく
            // 敵コピー
            var cb = new Rect(r.x + 7, r.yMax - copyH - 7, r.width - 14, copyH);
            bool tactic = d.type == CommandType.Tactic; // 【RC2】緑：敵はまねしない・命令枠を使わない
            // 【RC5・レビュー】「敵のまね」の欄は水色（まね＝水色。前は赤で「敵の攻撃」と同じ色だった）
            DrawRect(cb, tactic ? new Color(0.04f, 0.3f, 0.14f, 0.6f * alpha) : new Color(0.02f, 0.17f, 0.22f, 0.72f * alpha));
            DrawFrame(cb, tactic ? new Color(0.4f, 1f, 0.6f, 0.4f * alpha) : new Color(0.35f, 0.95f, 1f, 0.45f * alpha), 1f);
            var cbHead = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = tactic ? new Color(0.55f, 1f, 0.7f, alpha * dim) : new Color(0.55f, 0.95f, 1f, alpha * dim) } };
            string cbHeadText = tactic ? "戦術札（命令の枠を使わない）" : compact ? "まね" : "敵のまね";
            GUI.Label(new Rect(cb.x + 4, cb.y, cb.width - 8, 15), cbHeadText, Fit(cbHead, cbHeadText, cb.width - 8, 9)); // 【RC5・レビュー3】4:3 で「命令の枠 0」が札の外へ出ていた
            var cbody = new GUIStyle(Tiny) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip }; // 【RC5】10 → 11・禁則は WrapJa
            cbody.normal.textColor = tactic ? new Color(0.85f, 1f, 0.9f, alpha * dim) : new Color(0.88f, 0.97f, 1f, alpha * dim);
            var cbodyRect = new Rect(cb.x + 4, cb.y + 13, cb.width - 8, cb.height - 13);
            bool shortCopy = compact || r.height < 150f; // 【第2巡 9「デッキの欠け斬りの文が下で切れる」】背の低い札（デッキの一覧）も まねは短い文
            string copyLine = shortCopy ? CopyShort(d, copyDelta) : CopyText(d); // 【RC9】短い札は1行
            if (tactic && !compact) copyLine = "使いどき：" + TacticRules.When(d.tactic) + "\n（まねされない・すぐ使う）"; // 【RC11】
            if (!shortCopy && (selfPlus || copyUp) && d.damage > 0 && !tactic) copyLine = $"敵のまねは {Mathf.Max(0, d.damage - d.copyDamageCut + copyDelta)}ダメ：" + copyLine.Replace("同じ攻撃", "同じ形の攻撃"); // 【RC6・レビュー】強化した札は、まねの数字も文に（帯と文が食い違って見えた）【レビュー2回目】「同じ攻撃」→「同じ形の攻撃」
            string copyW = WrapFit(copyLine, cbody, cbodyRect, 9, out var cbodyFit);
            GUI.Label(cbodyRect, copyW, cbodyFit); // 【RC5・レビュー3】入りきらなければ小さく（デッキの一覧で「あなたにも当」で切れていた）
            }
            // 【通常リリース版・画面の直し 1】左上の菱形の数字（と単位）は出さない：同じ数が効果の行にある・コストに見える・1〜9 のキーとぶつかる（種類の帯は残す）
            // 右上のキー番号【RC10・初見テスト3・4回目「右上の 1〜7 はコスト？」】戦闘の手札では、数字キーを一度押すまで出さない（下の操作の行に「1〜9 札」）
            if (keyNumber > 0 && (KeyNumbersSeen || !CompactCards)) // 【RC5・レビュー】キーの番号が 10px・45% で見えにくかった【レビュー3】枠の線に重なって半分見えなかった → 札の内側に暗い下地つき
            {
                var kr = new Rect(r.xMax - 21, r.y + 6, 15, 15);
                DrawRect(kr, new Color(0f, 0f, 0f, 0.45f * alpha));
                DrawFrame(kr, new Color(1f, 1f, 1f, 0.3f * alpha), 1f); // 【RC10・初見テスト「右上の番号がコストに見える」】キーの形（枠）・控えめな灰色
                GUI.Label(kr, keyNumber.ToString(), new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color(0.8f, 0.82f, 0.88f, 0.75f * alpha) } });
            }
            if (planned)
            {
                var o = new Rect(r.x + r.width / 2 - 34, art.center.y - 18, 68, 36);
                DrawRect(o, new Color(0f, 0f, 0f, 0.75f));
                DrawFrame(o, CardGold, 2f);
                ShadowLabel(o, $"{slotNo}番目", new GUIStyle(Label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter }, CardGold);
            }
            if (selected)
            {
                float pulse = Blink(0.2f, 1f, 6f); // 【RC5・最終ゲート】点滅の設定で止まる
                DrawFrame(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 0.9f, 0.4f, pulse), 3f);
            }
        }

        /// <summary>【RC5・レビュー3】点滅（lo〜hi）。速さは 1 秒に1回まで（前はレーザーが 3.5 回）。設定「画面の揺れ・点滅：OFF」なら点滅しない（hi のまま）。</summary>
        public static float Blink(float lo, float hi, float radPerSec, float phase = 0f)
        {
            if (ScreenFx.Disabled) return hi;
            float sp = Mathf.Min(radPerSec, 6.2f);
            return lo + (hi - lo) * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * sp + phase));
        }

        /// <summary>【RC5】文字が幅に収まるまで小さくする（最小 minSize）。長い名前（時計仕掛けの砲王）が切れていた。</summary>
        public static GUIStyle Fit(GUIStyle style, string text, float width, int minSize = 10)
        {
            var s = new GUIStyle(style);
            while (s.fontSize > minSize && s.CalcSize(new GUIContent(text)).x > width) s.fontSize--;
            return s;
        }

        /// <summary>影つきの文字（明るい背景の上でも読めるように）。</summary>
        public static void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * color.a);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        public static string CategoryTag(CommandData data)
        {
            switch (CategoryOf(data))
            {
                case CardCategory.Attack: return "攻";
                case CardCategory.Support: return "制";
                case CardCategory.Buff: return "支";
                default: return "移";
            }
        }

        /// <summary>
        /// 【RC10・ChatGPT「札の表面は 名前・アイコン・自分への基本効果・敵のまね だけ」】戦闘の手札の札の顔：
        /// ［種類のアイコン］短い効果／水色の欄に［↻］敵のまね（短く）／まね攻撃があなたにも当たる札は［⚠］自分にも。全文は右下の欄。
        /// </summary>
        static void DrawCardFace(Rect r, CommandData d, Rect art, float alpha, float dim, int dmgOverride, int copyDelta, bool small, float copyH)
        {
            float icon = small ? 15f : 17f;
            var st = new GUIStyle(Tiny) { fontSize = small ? 12 : 13, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0) };
            st.normal.textColor = new Color(dim, dim, dim, alpha);
            // ① 自分の効果：アイコン＋短い文字（緑の札など長いものは2行まで・入りきらなければ小さく）
            float y = art.yMax + 11f;
            float bottom = r.yMax - copyH - 8f;
            DrawUiImage(new Rect(r.x + 8, y + 1, icon, icon), "ic_" + FaceIconKey(d), alpha * dim, ScaleMode.ScaleToFit);
            var er = new Rect(r.x + 10 + icon, y, r.width - 18 - icon, Mathf.Max(icon + 2f, bottom - y));
            string eff = ShortText(d, dmgOverride);
            GUIStyle effFit; string effW;
            var effOne = Fit(st, eff, er.width, 10);
            if (eff.IndexOf('\n') < 0 && effOne.CalcSize(new GUIContent(eff)).x <= er.width) { effW = eff; effFit = effOne; } // 【通常リリース版・画面の直し 1】短い効果は1行に（狭い札で「2マス先ま／で 3」と切れていた）
            else effW = WrapFit(eff, st, er, 9, out effFit);
            GUI.Label(er, effW, effFit);
            // ② 敵のまね（水色の欄）と ③ 自分にも
            string copy = FaceCopy(d, copyDelta, out var copyIcon, out var selfHit);
            bool tactic = d.type == CommandType.Tactic;
            if (tactic) { copy = "▶" + TacticRules.When(d.tactic); copyIcon = "tactic"; } // 【RC11】緑の札：使いどき
            var cb = new Rect(r.x + 7, r.yMax - copyH - 7, r.width - 14, copyH);
            DrawRect(cb, tactic ? new Color(0.04f, 0.3f, 0.14f, 0.6f * alpha) : new Color(0.02f, 0.17f, 0.22f, 0.72f * alpha));
            DrawFrame(cb, tactic ? new Color(0.4f, 1f, 0.6f, 0.4f * alpha) : new Color(0.35f, 0.95f, 1f, 0.45f * alpha), 1f);
            float rowH = cb.height - 4f; // 【通常リリース版・採用担当／UI/UX／デザイナーのレビュー「⚠まね→あなた がほぼ全部の札にあり、情報になっていない」】2行目をやめ、右端の小さい ⚠ だけに（文は右下の説明・凡例）
            var cs = new GUIStyle(st) { alignment = TextAnchor.MiddleLeft, normal = { textColor = tactic || copyIcon == "nocopy" ? new Color(0.8f, 0.95f, 0.85f, alpha * dim) : new Color(0.75f, 0.97f, 1f, alpha * dim) } };
            var row1 = new Rect(cb.x + 3, cb.y + 2, cb.width - 6, rowH);
            var ct = new Rect(row1.x + icon + 3, row1.y, row1.width - icon - 3 - (selfHit ? icon + 2 : 0f), row1.height);
            var copyOne = Fit(cs, copy, ct.width, 9);
            if (tactic && copyOne.CalcSize(new GUIContent(copy)).x > ct.width) // 【通常リリース版・画面の直し 1】札が多い（狭い）とき「▸敵が範囲にい」で切れていた → 緑の札の使いどきは欄の幅いっぱいに2行まで（絵は省く）
            {
                var full = new Rect(cb.x + 4, cb.y + 2, cb.width - 8, cb.height - 4);
                string cw2 = WrapFit(copy, new GUIStyle(cs) { fontSize = 11 }, full, 9, out var cf2);
                GUI.Label(full, cw2, new GUIStyle(cf2) { alignment = TextAnchor.MiddleLeft });
            }
            else
            {
                DrawUiImage(new Rect(row1.x, row1.center.y - icon / 2f, icon, icon), "ic_" + copyIcon, alpha * dim, ScaleMode.ScaleToFit);
                GUI.Label(ct, copy, copyOne);
            }
            if (selfHit && !(tactic))
            {
                DrawUiImage(new Rect(row1.xMax - icon, row1.center.y - icon / 2f, icon, icon), "ic_selfhit", alpha * dim, ScaleMode.ScaleToFit); // ⚠＝敵のまねた攻撃が あなたにも当たりうる // 【RC10・初見テスト2回目「あなたにも＝自分に反動？」】敵のまねがあなたに当たる // 【RC10・初見テスト「自分にも＝自分の攻撃で自分が傷つく？」】敵のまねが あなたにも当たる（「あなたにも当たる」は札の幅で切れた）
            }
        }

        /// <summary>【RC10】札の種類のアイコン（ui_ic_*）。</summary>
        public static string FaceIconKey(CommandData d)
        {
            if (d.type == CommandType.Tactic) return "tactic";
            switch (d.type)
            {
                case CommandType.Push: return "push";
                case CommandType.Pull: return "pull";
                case CommandType.Swap: return "control";
            }
            switch (CategoryOf(d))
            {
                case CardCategory.Attack: return "attack";
                case CardCategory.Support: return "control";
                case CardCategory.Buff: return "support";
                default: return "move";
            }
        }

        /// <summary>【RC10】まね攻撃があなたにも当たりうる札（札の顔に「⚠ 自分にも」）。</summary>
        /// <summary>【文化祭】はじめてでは「まね→あなた」を出さない（子どもが札を使うのをこわがる）。</summary>
        public static bool HideSelfHitBadge;
        /// <summary>【文化祭・10歳の立場のレビュー「斬撃・駆け足が読めない」】札の名前の置き換え（はじめてだけ：斬撃→きる など）。null なら そのまま。</summary>
        public static System.Func<CommandData, string> KidNames;

        public static bool FaceSelfHit(CommandData d) =>
            !HideSelfHitBadge && !(d.type == CommandType.Tactic || d.notCopied || CopyTextOff || d.IsNoBoardEffect) &&
            ((d.damage > 0 && (d.type == CommandType.Slash || d.type == CommandType.PushSlash || d.type == CommandType.Sweep || d.type == CommandType.Pierce || d.type == CommandType.AroundSlash))
             || d.type == CommandType.Push || d.type == CommandType.Pull); // 【RC10・初見テスト3回目（3人）「押し出し・引き寄せで、敵が自分を押す・引くのか不明」】まねであなたも動かされる

        /// <summary>【RC10】敵のまねの札の顔（とても短く：「敵も 4」「敵も動く」「まねされない」）。アイコンは copy／nocopy。</summary>
        public static string FaceCopy(CommandData d, int copyDelta, out string icon, out bool selfHit)
        {
            selfHit = FaceSelfHit(d);
            icon = "copy";
            if (d.type == CommandType.Tactic || d.notCopied || CopyTextOff) { icon = "nocopy"; return "まねされない"; }
            if (d.IsNoBoardEffect) { icon = "nocopy"; return "敵は何もしない"; }
            int cd = Mathf.Max(0, d.damage - d.copyDamageCut + copyDelta);
            switch (d.type)
            {
                case CommandType.Walk when d.copyMirrored: return "敵は反対へ";
                case CommandType.Rush when d.copyDistanceCut != 0: return $"敵も{Mathf.Max(0, d.minDistance - d.copyDistanceCut)}マス";
                case CommandType.Walk:
                case CommandType.Rush:
                case CommandType.Diagonal:
                case CommandType.Slide:
                case CommandType.Blink:
                case CommandType.Vault:
                case CommandType.WallKick:
                case CommandType.Jump: return "敵も動く";
                case CommandType.Slash when d.copyMirrored: return $"敵は逆向き {cd}";
                case CommandType.Slash when d.copyWide: return $"敵は前3 {cd}";
                case CommandType.PushSlash: return $"敵も {cd}・押す"; // 【RC10・初見テスト「まねで押しまでするのか」】
                case CommandType.Slash:
                case CommandType.Sweep:
                case CommandType.Pierce: return $"敵も {cd}";
                case CommandType.AroundSlash: return $"敵も周り {cd}";
                case CommandType.Push: return "敵も押す";
                case CommandType.Pull: return "敵も引く";
                case CommandType.Swap: return "敵も交換";
                case CommandType.PlaceSpike: return "敵もトゲ";
                case CommandType.Heal: return "敵も回復";
                case CommandType.Spirit: return "敵も強く";
                case CommandType.Aim: return "敵も向く";
                default:
                    var s = CopyShort(d, copyDelta) ?? "";
                    return s.Length > 8 ? s.Substring(0, 7) + "…" : s;
            }
        }

        /// <summary>カードに出す短い効果（説明文の代わり。全文は選んだときに出る）。</summary>
        public static string ShortText(CommandData d, int dmgOverride = -1)
        {
            int dm = dmgOverride >= 0 ? dmgOverride : d.damage; // 【RC9】鍛えた札は あなたの数字
            switch (d.type)
            {
                case CommandType.Tactic: return TacticRules.Short(d.tactic); // 【RC9・初見テスト3・4回目「早鐘の説明文が枠からはみ出す」】緑の札も短い1行（全文は右下）
                case CommandType.Walk: return $"{Mathf.Max(1, d.minDistance)}マス移動";
                case CommandType.Rush: return d.minDistance == d.maxDistance ? $"{d.minDistance}マス移動" : $"{d.minDistance}か{d.maxDistance}マス";
                case CommandType.Jump: return "2マス先へ跳ぶ";
                case CommandType.Diagonal: return "斜め1マス";
                case CommandType.Slide: return "止まるまで滑る";
                case CommandType.Blink: return "3マス先へ転移";
                case CommandType.WallKick: return "壁を背に3マス";
                case CommandType.Vault: return "正面の敵を越える";
                case CommandType.Slash: return d.range > 1 ? $"{d.range}マス先まで {dm}" : $"隣に {dm}";
                case CommandType.PushSlash: return $"隣に {dm}\n1マス押す"; // 【RC10・初見テスト「3＋1＝4ダメ？」→ 2回目「3・1マス が 3.1マスに読める」】押すのはいつも1マス（全文は右下）
                case CommandType.Sweep: return $"前3マスに {dm}";
                case CommandType.Pierce: return $"直線{d.range}マス全員 {dm}";
                case CommandType.AroundSlash: return $"上下左右に {dm}";
                case CommandType.Push: return "隣を1マス押す";
                case CommandType.Pull: return $"{Mathf.Max(2, d.range)}マス内を1マス引く"; // 【RC10・初見テスト「何マス引くのか不明」】範囲の中の最初の1体を1マス（全文と同じ。「隣へ」は誤りだった）
                case CommandType.Swap: return "隣と位置を交換";
                case CommandType.PlaceSpike: return "隣にトゲを置く";
                case CommandType.Spirit: return $"次の攻撃 +{Mathf.Max(1, d.range)}";
                case CommandType.Foresight: return "上3枚から1枚";
                case CommandType.Reconsider: return $"次ターン +{Mathf.Max(1, d.range)}枚";
                case CommandType.Rally: return d.planDraw > 0 ? $"命令+{Mathf.Max(1, d.range)}・{d.planDraw}枚引く" : $"上限 +{Mathf.Max(1, d.range)}";
                case CommandType.Draw: return d.fromDiscard ? "捨て札から1枚" : d.drawFilter == DrawFilter.Move ? $"移動札を{Mathf.Max(1, d.range)}枚" : d.drawFilter == DrawFilter.Technique ? $"技の札を{Mathf.Max(1, d.range)}枚" : $"すぐ{Mathf.Max(1, d.range)}枚引く";
                case CommandType.Insight: return "上3枚から1枚（すぐ）";
                case CommandType.Resonate: return $"まね成功で+{Mathf.Max(1, d.range)}枚";
                case CommandType.ReverseCopy: return "順番を逆に";
                default: return d.description;
            }
        }

        /// <summary>
        /// カードを1枚描く（仮デザイン：系統の色＋左上の種類＋大きい名前＋短い効果）。クリックされたら true。
        /// highlighted（選択中）は少し大きく・明るく描く。showCopyNote なら「敵にコピーされると…」も書く（報酬画面など大きいカード用）。
        /// </summary>
        public static bool DrawCard(Rect r, CommandData data, string footer, bool enabled, bool highlighted, bool dimmed, bool showCopyNote = false, float alpha = 1f)
        {
            var click = r;
            var oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (highlighted) r = new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6);
            Color bg = CardColor(data);
            if (highlighted) bg = Color.Lerp(bg, Color.white, 0.18f);
            if (dimmed) bg = Color.Lerp(bg, Color.black, 0.6f);
            var frame = CardFrame(data);
            if (frame != null)
            {
                // 【検証中】テーマのカード枠（9 スライス）＋上に系統の色の帯
                Draw9(r, frame, Theme.UiBorder("card_" + CategoryKey(data)), new Color(dimmed ? 0.45f : 1f, dimmed ? 0.45f : 1f, dimmed ? 0.45f : 1f, alpha));
                DrawRect(new Rect(r.x + 7, r.y + 7, r.width - 14, 24), new Color(bg.r, bg.g, bg.b, 0.55f));
                if (highlighted) DrawFrame(r, Color.yellow, 3f);
            }
            else
            {
                DrawRect(r, bg);
                DrawFrame(r, highlighted ? Color.yellow : new Color(1f, 1f, 1f, dimmed ? 0.2f : 0.55f), highlighted ? 3f : 1.5f);
            }

            // 左上：系統の札（移・攻・補・強）
            var tag = new GUIStyle(Tiny) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            DrawRect(new Rect(r.x + 3, r.y + 3, 16, 16), new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(r.x + 3, r.y + 2, 16, 18), CategoryTag(data), tag);

            bool small = r.height < 90f && !showCopyNote;
            var title = new GUIStyle(CardTitle) { fontSize = showCopyNote ? 22 : small ? 17 : 18, wordWrap = false, clipping = TextClipping.Clip };
            GUI.Label(new Rect(r.x + 14, r.y + 4, r.width - 18, 26), data.displayName, title);

            bool hasFooter = !string.IsNullOrEmpty(footer);
            if (showCopyNote)
            {
                var text = new GUIStyle(CardText) { fontSize = 15 };
                GUI.Label(new Rect(r.x + 4, r.y + 40, r.width - 8, r.height * 0.5f - 40), data.description, text);
                if (!string.IsNullOrEmpty(data.copyNote))
                {
                    var note = new GUIStyle(text) { fontSize = 14 };
                    note.normal.textColor = new Color(1f, 0.7f, 0.7f);
                    GUI.Label(new Rect(r.x + 8, r.y + r.height * 0.5f, r.width - 16, r.height * 0.45f), "敵にまねされると：\n" + data.copyNote, note);
                }
            }
            else if (!(small && hasFooter))
            {
                // 短い効果（全文は選んだときに下の欄へ）
                var text = new GUIStyle(CardText) { fontSize = small ? 13 : 14, fontStyle = FontStyle.Bold, wordWrap = true };
                text.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
                GUI.Label(new Rect(r.x + 3, r.y + 30, r.width - 6, small ? 24 : 40), ShortText(data), text);
            }
            if (hasFooter)
            {
                var foot = new GUIStyle(Tiny) { alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold, fontSize = 13 };
                foot.normal.textColor = Color.yellow;
                GUI.Label(new Rect(r.x + 2, r.yMax - 22, r.width - 4, 20), footer, foot);
            }
            GUI.color = oldColor;

            if (!enabled) return false;
            return GUI.Button(click, GUIContent.none, GUIStyle.none);
        }
    }
}
