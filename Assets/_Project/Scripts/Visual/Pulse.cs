using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】明るさが脈打つ（予告・回復ルーン）。色の a は外から決め、ここは倍率だけ。</summary>
    public class Pulse : MonoBehaviour
    {
        public float speed = 4f;
        public float min = 0.6f;
        SpriteRenderer sr;
        float baseAlpha = -1f;

        public void SetBaseAlpha(float a) => baseAlpha = a;

        void Update()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (baseAlpha < 0f) baseAlpha = sr.color.a;
            var c = sr.color;
            c.a = ScreenFx.Disabled ? baseAlpha : baseAlpha * Mathf.Lerp(min, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * speed)); // 【RC5・最終検証】設定「画面の揺れ・点滅」で止まる（赤・ワープ・火種などの脈動）
            sr.color = c;
        }
    }
}
