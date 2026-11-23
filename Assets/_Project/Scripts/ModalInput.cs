using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】モーダル（前面の選択画面）が開いている間、背面の UI に入力（クリック・キー）を渡さない（2026-10-05 本人プレイ：先読みの選択中に後ろのボタンが反応した）。
    /// IMGUI は先に描いた物が先に入力を受け取るので、背面を描く前に入力の種類を Ignore に変え、前面を描く前に戻す。
    /// 使い方：var t = ModalInput.Begin(modal); 背面を描く; ModalInput.End(t); 前面（モーダル）を描く。uGUI へ移しても同じ仕様（前面がある間は背面を押せない）。
    /// </summary>
    public static class ModalInput
    {
        /// <summary>この種類の入力を、モーダル中は背面へ渡さないか。</summary>
        public static bool Blocks(bool modal, EventType type) =>
            modal && (type == EventType.MouseDown || type == EventType.MouseUp || type == EventType.MouseDrag ||
                      type == EventType.KeyDown || type == EventType.KeyUp || type == EventType.ScrollWheel);

        /// <summary>【RC5】今、背面（モーダルの後ろ）を描いているか。キーボードの選択は背面のボタンを数えない。</summary>
        public static bool Blocking { get; private set; }

        public static EventType Begin(bool modal)
        {
            Blocking = modal;
            var ev = Event.current;
            if (ev == null) return EventType.Ignore;
            var type = ev.type;
            if (Blocks(modal, type)) ev.type = EventType.Ignore;
            return type;
        }

        public static void End(EventType original)
        {
            Blocking = false;
            var ev = Event.current;
            if (ev != null && ev.type == EventType.Ignore && original != EventType.Ignore) ev.type = original;
        }
    }
}
