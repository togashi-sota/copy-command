using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】ヒットストップ：当たった瞬間にほんの少し時間を止める。
    /// Time.timeScale を 0 にして、実時間で戻す（ターン進行の待ち時間もその分だけ延びる＝演出と進行がずれない）。
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        static HitStop instance;
        float until;

        public static void Do(float seconds)
        {
            if (!Application.isPlaying) return;
            if (instance == null)
            {
                var go = new GameObject("HitStop");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<HitStop>();
            }
            instance.until = Mathf.Max(instance.until, Time.unscaledTime + seconds);
            Time.timeScale = 0f;
        }

        void Update()
        {
            if (Time.timeScale == 0f && Time.unscaledTime >= until) Time.timeScale = 1f;
        }
    }
}
