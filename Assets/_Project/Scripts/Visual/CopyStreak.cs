using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【企画書 v5・UI】まねた攻撃が当たったとき、まねた敵から当たった相手へ水色の短い線（0.8 秒で消える）。
    /// 「誰もいない所を斬った → 敵がまねた → 敵どうしに当たった」の因果を、止めた画面でも追えるように。ルールには触れない。
    /// </summary>
    public class CopyStreak : MonoBehaviour
    {
        const float Life = 0.8f;
        static Material material;
        LineRenderer line;
        Color color;
        float t;

        public static void Spawn(Vector3 from, Vector3 to, Color color)
        {
            if ((to - from).sqrMagnitude < 0.01f) return;
            if (material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                material = new Material(shader);
            }
            var go = new GameObject("CopyStreak");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = material;
            lr.positionCount = 2;
            // マスの中心どうしだと体に隠れるので、少し内側で始めて少し手前で止める
            var dir = (to - from).normalized;
            lr.SetPosition(0, from + dir * 0.12f);
            lr.SetPosition(1, to - dir * 0.18f);
            lr.startWidth = 0.13f;
            lr.endWidth = 0.05f;
            lr.numCapVertices = 2;
            lr.sortingLayerName = SortLayers.Vfx;
            lr.sortingOrder = 40;
            lr.startColor = color;
            lr.endColor = color;
            var s = go.AddComponent<CopyStreak>();
            s.line = lr;
            s.color = color;
        }

        void Update()
        {
            t += Time.deltaTime;
            float a = t < Life * 0.6f ? 1f : 1f - (t - Life * 0.6f) / (Life * 0.4f);
            var c = color;
            c.a = Mathf.Clamp01(a);
            line.startColor = c;
            line.endColor = new Color(c.r, c.g, c.b, c.a * 0.7f);
            if (t >= Life) Destroy(gameObject);
        }
    }
}
