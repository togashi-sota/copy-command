using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【企画書 v5・UI】盤面の上の帯に出す一言（「同士討ち！」「大技が敵に命中！」「八方斬り！」など）。
    /// 前は盤面の中の文字（TextMesh）で出していて、名札（画面の上に描く）に隠れたり、同じ一言が重なって二重に見えた。
    /// 同じ一言は続けて出さない（表示中なら時間だけ延ばす）。描くのは BattleUIShowcase（盤面の上・列の番号の上の帯）。
    /// </summary>
    public static class Callout
    {
        public const float Life = 1.1f;
        public static string Text { get; private set; }
        public static Color Color { get; private set; }
        public static float ShownAt { get; private set; } = -10f;

        public static bool Visible => Text != null && Time.unscaledTime - ShownAt < Life;

        /// <summary>帯を描く画面（BattleUIShowcase）が直前のフレームで描かれたか。古い画面（STAGE 1〜3・RUN）では盤面の中の文字で出す。</summary>
        public static float DrawnAt { get; set; } = -10f;
        public static bool HasBar => Time.unscaledTime - DrawnAt < 0.5f;

        /// <summary>帯があれば帯に、無ければ盤面の中の文字（DamagePopup）で。</summary>
        public static void Show(string text, Color color, Vector3 fallbackWorld)
        {
            if (!HasBar) { DamagePopup.SpawnOnce(fallbackWorld, text, color); return; }
            Show(text, color);
        }

        public static void Show(string text, Color color)
        {
            if (Visible && Text == text) { ShownAt = Time.unscaledTime; return; }
            Text = text;
            Color = color;
            ShownAt = Time.unscaledTime;
        }
    }
}
