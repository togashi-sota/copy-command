using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【Combat V4】画面の演出（仮）：画面揺れ（カメラ）と画面フラッシュ（IMGUI で上から重ねる）。
    /// 大技の発動・ボスの登場・大きなダメージで使う。最終アートでなくても「何が起きたか」が気持ちよく伝わるように。
    /// プレイ中だけ動く（自動テスト・自動撮影では何もしない）。
    /// </summary>
    public static class ScreenFx
    {
        static float shakeUntil, shakeStart, shakeMag;
        static float flashUntil, flashStart;
        static Color flashColor;
        static Vector3 camBase;
        static bool camBaseSet;

        public static bool Disabled;

        public static void Shake(float magnitude, float seconds)
        {
            if (!Application.isPlaying || Disabled) return;
            shakeStart = Time.unscaledTime; shakeUntil = shakeStart + seconds; shakeMag = Mathf.Max(shakeMag * (Time.unscaledTime < shakeUntil ? 0.5f : 0f), magnitude);
        }

        public static void Flash(Color color, float seconds)
        {
            if (!Application.isPlaying || Disabled) return;
            flashColor = color; flashStart = Time.unscaledTime; flashUntil = flashStart + seconds;
        }

        /// <summary>毎フレーム：カメラの揺れを反映する（BattleUI の OnGUI から。ほかのカメラ操作と競合しないよう、揺れていないときは何もしない）。</summary>
        public static void Update(Camera cam)
        {
            if (cam == null || !Application.isPlaying) return;
            if (Time.unscaledTime < shakeUntil)
            {
                if (!camBaseSet) { camBase = cam.transform.localPosition; camBaseSet = true; }
                float k = 1f - (Time.unscaledTime - shakeStart) / Mathf.Max(0.01f, shakeUntil - shakeStart);
                var off = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * shakeMag * k;
                cam.transform.localPosition = camBase + off;
            }
            else if (camBaseSet)
            {
                cam.transform.localPosition = camBase;
                camBaseSet = false;
                shakeMag = 0f;
            }
        }

        /// <summary>画面全体にフラッシュを重ねる（OnGUI の最後に呼ぶ）。</summary>
        public static void DrawFlash(float width, float height)
        {
            if (Time.unscaledTime >= flashUntil) return;
            float k = 1f - (Time.unscaledTime - flashStart) / Mathf.Max(0.01f, flashUntil - flashStart);
            var c = flashColor; c.a *= Mathf.Clamp01(k);
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
