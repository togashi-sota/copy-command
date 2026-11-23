using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】一度だけ再生するエフェクト（Animator のクリップの長さで消える）。</summary>
    public class OneShotVfx : MonoBehaviour
    {
        public float lifetime = 0.4f;
        float t;

        void Update()
        {
            t += Time.deltaTime;
            if (t >= lifetime) Destroy(gameObject);
        }
    }
}
