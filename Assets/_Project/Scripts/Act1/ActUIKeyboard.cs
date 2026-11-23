using UnityEngine;
using static CopyCommand.UiStyles;

namespace CopyCommand
{
    /// <summary>
    /// 【RC5・キーボードだけでも遊べる】戦闘以外の画面（タイトル・役職・扉・報酬・商人・休息・結果・チュートリアルの1ランの流れ）のキー操作。
    /// 矢印・W A S D・Tab で選ぶ（白の枠）→ Enter・Space で押す。何も選んでいないときの Enter は、その画面の主のボタン（金色）に枠を出すだけ（押すのは次の Enter）。
    /// 【RC5・コードレビュー】押したら選択を消す・ボタンの数が変わったら消す（頁が変わると、同じ番号が「やめる」になっていた）・画面が変わった直後 0.25 秒は Enter を受けない（マウスと同じ連打よけ）。
    /// 作りは単純：その画面で描いたボタンに描いた順の番号を付け、番号で選ぶ（IMGUI は毎回同じ順で描く）。モーダルの後ろのボタンは数えない（ModalInput.Blocking）。
    /// 戦闘中は BattleUI のキー（1〜9・W A S D・Enter・Z X C・Q E。戦術札の対象・転移石の行き先も矢印＋Enter）だけ。
    /// </summary>
    public partial class ActUI
    {
        int kbFocus = -1, kbIndex, kbCount, kbPrimary = -1, kbPrimaryNext = -1;
        bool kbActivate;
        ActScreen kbScreen;
        string kbModalKey;

        /// <summary>今、キーボードの選択が出ているか（白の枠）。</summary>
        public bool KeyboardFocusVisible => kbFocus >= 0;

        void KeyboardBegin()
        {
            kbIndex = 0;
            kbPrimaryNext = -1;
            // 画面やモーダルが変わったら選択を消す（前の画面の番号で押さない）
            string modalKey = $"{removeMode}|{showDeck}|{showHowTo}|{showSettings}|{ShowFirstChoice}";
            if (act.Screen != kbScreen || modalKey != kbModalKey) { kbScreen = act.Screen; kbModalKey = modalKey; kbFocus = -1; kbActivate = false; }
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;
            int dir = ev.keyCode == KeyCode.Tab ? (ev.shift ? -1 : 1)
                    : ev.keyCode == KeyCode.RightArrow || ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.D || ev.keyCode == KeyCode.S ? 1
                    : ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.UpArrow || ev.keyCode == KeyCode.A || ev.keyCode == KeyCode.W ? -1 : 0;
            if (dir != 0 && kbCount > 0)
            {
                kbFocus = kbFocus < 0 ? (kbPrimary >= 0 ? kbPrimary : dir > 0 ? 0 : kbCount - 1) : (kbFocus + dir + kbCount) % kbCount;
                ev.Use();
                return;
            }
            if ((ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter || ev.keyCode == KeyCode.Space) && kbCount > 0)
            {
                ev.Use();
                if (Guarded) return; // 画面が変わった直後・押した直後（長押しの連続入力で次の画面まで進まない）
                if (kbFocus < 0) { kbFocus = kbPrimary >= 0 ? kbPrimary : 0; return; } // 扉の画面で、選ばずに左の扉へ入っていた
                kbActivate = true;
            }
        }

        void KeyboardEnd()
        {
            kbActivate = false; // 押したボタンが無くても、次の入力まで持ち越さない
            if (Event.current.type != EventType.Repaint) return;
            if (kbIndex != kbCount) kbFocus = -1; // ボタンの数が変わった（頁・売り切れ）：前の番号で別のボタンを押さない
            kbCount = kbIndex;
            kbPrimary = kbPrimaryNext;
        }

        /// <summary>押せる場所を1つ数える。キーボードで選ばれていれば白の枠、Enter で押されたら true。</summary>
        bool KeyboardHit(Rect r, bool primary)
        {
            if (ModalInput.Blocking || act == null || act.InBattle) return false;
            int i = kbIndex++;
            if (primary && kbPrimaryNext < 0) kbPrimaryNext = i;
            if (i == kbFocus && Event.current.type == EventType.Repaint)
            {
                float pulse = Blink(0.5f, 1f, 6f); // 【RC5・最終ゲート】点滅の設定で止まる
                DrawFrame(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), new Color(0f, 0f, 0f, 0.75f), 2f); // 【RC5】金のボタンの上で金の枠が見えにくかった → 白の枠＋暗い縁
                DrawFrame(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(1f, 1f, 1f, pulse), 3f);
            }
            if (kbActivate && i == kbFocus) { kbActivate = false; kbFocus = -1; return true; }
            return false;
        }
    }
}
