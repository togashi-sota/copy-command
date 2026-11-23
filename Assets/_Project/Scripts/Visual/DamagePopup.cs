using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】ダメージ数字（盤面の上に出て、上へ浮きながら消える）。</summary>
    public class DamagePopup : MonoBehaviour
    {
        const float Life = 0.9f;
        TextMesh text;
        Color color;
        float t;

        /// <summary>【企画書 v5・UI】いま出ている一言（文字・位置・出た時刻）。同じ一言は重ねない。</summary>
        static readonly System.Collections.Generic.List<(string label, Vector3 pos, float time)> recentLabels = new System.Collections.Generic.List<(string, Vector3, float)>();

        /// <summary>
        /// 【企画書 v5・UI】文字の一言（「同士討ち！」「大技が敵に命中！」など）。同じ一言が 0.5 秒以内に出ていれば出さない
        /// （同じ攻撃で2体に当たる・同じ敵に2回当たると、同じ文字が重なって二重に見えた）。ダメージの数字は Spawn で毎回出す。
        /// </summary>
        public static void SpawnOnce(Vector3 position, string label, Color color)
        {
            float now = Time.time;
            recentLabels.RemoveAll(x => now - x.time > Life);
            if (recentLabels.Exists(x => x.label == label && (now - x.time < 0.5f || (x.pos - position).sqrMagnitude < 1.2f))) return;
            recentLabels.Add((label, position, now));
            Spawn(position, label, color);
        }

        /// <summary>【文化祭】文字の大きさの倍率（文化祭モードは 1.5：離れて見ている人にも「-4」「同士討ち！」が読める）。</summary>
        public static float Scale = 1f;

        public static void Spawn(Vector3 position, string label, Color color)
        {
            var go = new GameObject("DamagePopup");
            go.transform.position = position + new Vector3(0f, 0.45f, 0f);
            var text = go.AddComponent<TextMesh>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;
            text.characterSize = 0.05f * Scale;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = text.font.material;
            mr.sortingLayerName = SortLayers.Vfx;
            mr.sortingOrder = 50;
            // 黒いふち（同じ文字を少しずらして後ろに4つ）
            foreach (var o in new[] { new Vector2(0.03f, 0f), new Vector2(-0.03f, 0f), new Vector2(0f, 0.03f), new Vector2(0f, -0.03f) })
            {
                var sh = new GameObject("Outline");
                sh.transform.SetParent(go.transform, false);
                sh.transform.localPosition = o;
                var st = sh.AddComponent<TextMesh>();
                st.text = label; st.font = text.font; st.fontSize = text.fontSize; st.characterSize = text.characterSize;
                st.anchor = text.anchor; st.fontStyle = text.fontStyle; st.color = new Color(0f, 0f, 0f, 0.9f);
                var smr = sh.GetComponent<MeshRenderer>();
                smr.sharedMaterial = text.font.material;
                smr.sortingLayerName = SortLayers.Vfx;
                smr.sortingOrder = 49;
            }
            var p = go.AddComponent<DamagePopup>();
            p.text = text;
            p.color = color;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = t / Life;
            // はじめにポンと大きく → 上へ浮いて消える
            float pop = k < 0.15f ? Mathf.Lerp(0.6f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((k - 0.15f) / 0.2f));
            transform.localScale = Vector3.one * pop;
            transform.position += new Vector3(0f, 0.6f * Time.deltaTime, 0f);
            float a = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            foreach (var tm in GetComponentsInChildren<TextMesh>())
            {
                var c = tm == text ? color : tm.color;
                c.a = (tm == text ? 1f : 0.9f) * a;
                tm.color = c;
            }
            if (t >= Life) Destroy(gameObject);
        }
    }
}
