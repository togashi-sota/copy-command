using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【完成版・2026-10-20】BGM（ループ）。曲は tools/audio_gen/gen_audio.py でコードから作った WAV（自作・外部の素材なし）を
    /// Resources/Audio/bgm_*.wav から読む。無ければ鳴らさない（自動テスト・バッチでも何もしない）。
    /// 曲：title（タイトル・扉）／battle（通常戦）／boss（中ボス・ボス）／clear（クリアの結果）。
    /// </summary>
    public static class Bgm
    {
        static AudioSource source;
        static string current;
        public static float Volume = 0.32f;
        /// <summary>【RC7】紹介動画の撮影だけ：曲が変わった（null＝止めた）。ふだんは null。</summary>
        public static System.Action<string> Recorded;
        static bool muted;

        /// <summary>【RC3】音量を変える（鳴っている曲にもすぐ効く）。</summary>
        public static void SetVolume(float v)
        {
            Volume = Mathf.Clamp01(v);
            if (source != null) source.volume = Volume;
        }

        public static void SetMuted(bool m)
        {
            muted = m;
            if (source != null) source.mute = m;
        }

        /// <summary>曲を変える（同じ曲なら何もしない）。null で止める。</summary>
        public static void Play(string track)
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            if (track == current) return;
            current = track;
            Recorded?.Invoke(track); // 【RC7】
            Ensure();
            if (track == null) { source.Stop(); return; }
            var clip = Resources.Load<AudioClip>("Audio/bgm_" + track);
            if (clip == null) { source.Stop(); return; }
            source.clip = clip;
            source.loop = track != "clear";
            source.volume = Volume;
            source.mute = muted;
            source.Play();
        }

        static void Ensure()
        {
            if (source != null) return;
            var go = new GameObject("Bgm");
            Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            Sfx.EnsureListener();
        }
    }
}
