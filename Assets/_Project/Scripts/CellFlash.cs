using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// マスを一瞬だけ光らせる（斬撃・押し出しが「どのマスに出たか」を見せるため）。
    /// 空振りでも光るので、敵の攻撃がどこへ外れたか分かる。
    /// </summary>
    public class CellFlash : MonoBehaviour
    {
        const float Duration = 0.45f;
        SpriteRenderer sr;
        Color color;
        float timer;

        public static void Spawn(Transform parent, Vector3 position, Color color, bool themed = false)
        {
            var sr = GreyboxSprites.CreateShape(parent, "CellFlash", GreyboxSprites.Square, color, position, 0.94f, 5);
            if (themed) { sr.sortingLayerName = SortLayers.Telegraph; sr.sortingOrder = 10; color.a *= 0.6f; } // テーマの盤面では層の上に・少し薄く
            var flash = sr.gameObject.AddComponent<CellFlash>();
            flash.sr = sr;
            flash.color = color;
            flash.timer = Duration;
        }

        void Update()
        {
            timer -= Time.deltaTime;
            sr.color = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(timer / Duration));
            if (timer <= 0f) Destroy(gameObject);
        }
    }
}
