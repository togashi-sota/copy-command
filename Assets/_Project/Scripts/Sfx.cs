using System.Collections.Generic;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 効果音。【完成版】Resources/Audio/sfx_*.wav（tools/audio_gen/gen_audio.py でコードから作った自作の音）があればそれを、無ければ短い音をコードで作って鳴らす。
    /// プレイ中だけ鳴る（自動テストでは何もしない）。シーンに AudioListener が無いと何も聞こえないので、最初に1つ作る（2026-10-20 監査：ビルドで無音だった）。
    /// </summary>
    public static class Sfx
    {
        public enum Kind { Step, Swing, Hit, Draw, Card, Button, Clear, Lose, Warn, Boom, Tactic, Chime, Copy, Kill } // 【Combat V4】Warn 大技の予告・Boom 大技の発動・Tactic 戦術札・Chime コピー成功／【完成版】Copy まねが敵に当たった・Kill 敵が倒れた

        public static bool Muted;
        /// <summary>【RC7】紹介動画の撮影だけ（DebugAutoCapture）：鳴った効果音（名前・大きさ）。ふだんは null。</summary>
        public static System.Action<string, float> Recorded;
        static AudioSource source;
        static readonly Dictionary<Kind, AudioClip> clips = new Dictionary<Kind, AudioClip>();
        static float lastStepTime;

        /// <summary>
        /// 【RC3・本人「音が大きすぎる」】音ごとの大きさをそろえる倍率（Resources/Audio の WAV を測った RMS から：だいたい -16 dBFS にそろえる。まねが当たった音だけ少し大きく）。
        /// 測った値（2026-10-21）：hit -10.3・boom -9.0・chime -14.4・copy -16.3・swing -16.6・step -16.4・tactic -17.3・button -21.7・warn -20.1 dBFS（RMS）。
        /// </summary>
        static float Gain(Kind k)
        {
            switch (k)
            {
                case Kind.Hit: return 0.52f;
                case Kind.Boom: return 0.45f;
                case Kind.Chime: return 0.62f; // 【RC3】まね成功（コピー成功）の音：BGM より RMS 約 +3 dB（前は +5 dB）
                case Kind.Copy: return 0.8f; // 【RC3】まねが敵に当たった音は残す。ただし BGM より大きすぎない（初期値で BGM より RMS 約 +3.7 dB・前は +5.6 dB）
                case Kind.Kill: return 0.85f;
                case Kind.Warn: return 1.5f; // 【RC5】大技の予告：BGM の大きいところで埋もれていた（tools/audio_gen/mix_qa.py の音ごと）
                case Kind.Button: return 1.4f; // 【RC5】ボタン：短い音で BGM とほぼ同じ大きさだった
                default: return 0.9f;
            }
        }

        static readonly Dictionary<Kind, float> lastPlay = new Dictionary<Kind, float>();
        static readonly List<float> recent = new List<float>();

        public static void Play(Kind kind)
        {
            if (!Application.isPlaying || Muted) return;
            if (kind == Kind.Step)
            {
                // 突進で連続して鳴りすぎないように
                if (Time.time - lastStepTime < 0.06f) return;
                lastStepTime = Time.time;
            }
            // 【RC3】同じ音を 0.05 秒以内に重ねない・0.12 秒に4つより多く鳴らさない（同時に何体も当たると極端に大きくなっていた）
            float now = Time.unscaledTime;
            if (lastPlay.TryGetValue(kind, out var lp) && now - lp < 0.05f) return;
            recent.RemoveAll(t => now - t > 0.12f);
            if (recent.Count >= 4) return;
            lastPlay[kind] = now;
            recent.Add(now);
            if (source == null)
            {
                var go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                EnsureListener();
            }
            if (!clips.TryGetValue(kind, out var clip))
            {
                clip = Resources.Load<AudioClip>("Audio/sfx_" + kind.ToString().ToLowerInvariant()) ?? Create(kind); // 【完成版】作った音があれば優先
                clips[kind] = clip;
            }
            float vol = Volume * Gain(kind) * (recent.Count > 1 ? 0.75f : 1f);
            source.PlayOneShot(clip, vol); // 重なったら少し小さく
            Recorded?.Invoke(kind.ToString().ToLowerInvariant(), vol); // 【RC7】紹介動画の撮影：鳴った音と大きさを記録（音は後で同じ WAV から合成する）
        }

        /// <summary>効果音の音量（0〜1）。</summary>
        public static float Volume = 0.5f; // 【RC3】SoundSettings.Apply が設定（初期は 50%）

        /// <summary>【完成版】AudioListener が1つも無ければ作る（Battle.unity のカメラには付いていなかった）。</summary>
        public static void EnsureListener()
        {
            if (Object.FindAnyObjectByType<AudioListener>() != null) return;
            var cam = Camera.main;
            if (cam != null) cam.gameObject.AddComponent<AudioListener>();
            else { var go = new GameObject("AudioListener"); Object.DontDestroyOnLoad(go); go.AddComponent<AudioListener>(); }
        }

        static AudioClip Create(Kind kind)
        {
            // 種類ごとに（開始周波数, 終了周波数, 長さ秒, ノイズ量）
            float f0, f1, length, noise;
            switch (kind)
            {
                case Kind.Step: f0 = 220; f1 = 180; length = 0.05f; noise = 0.3f; break;
                case Kind.Swing: f0 = 900; f1 = 300; length = 0.10f; noise = 0.6f; break;
                case Kind.Hit: f0 = 160; f1 = 60; length = 0.16f; noise = 0.5f; break;
                case Kind.Draw: f0 = 600; f1 = 900; length = 0.06f; noise = 0.1f; break;
                case Kind.Card: f0 = 500; f1 = 700; length = 0.07f; noise = 0f; break;
                case Kind.Button: f0 = 700; f1 = 700; length = 0.04f; noise = 0f; break;
                case Kind.Clear: f0 = 500; f1 = 1000; length = 0.4f; noise = 0f; break;
                case Kind.Warn: f0 = 440; f1 = 330; length = 0.22f; noise = 0f; break;
                case Kind.Boom: f0 = 110; f1 = 40; length = 0.34f; noise = 0.7f; break;
                case Kind.Tactic: f0 = 420; f1 = 840; length = 0.12f; noise = 0f; break;
                case Kind.Chime: f0 = 880; f1 = 1320; length = 0.16f; noise = 0f; break;
                case Kind.Copy: f0 = 990; f1 = 1480; length = 0.14f; noise = 0.1f; break;
                case Kind.Kill: f0 = 240; f1 = 80; length = 0.3f; noise = 0.4f; break;
                default: f0 = 300; f1 = 100; length = 0.5f; noise = 0f; break; // Lose
            }
            const int rate = 22050;
            int samples = (int)(rate * length);
            var data = new float[samples];
            var rng = new System.Random(1);
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float freq = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * freq / rate;
                float tone = Mathf.Sin(phase);
                float n = (float)(rng.NextDouble() * 2 - 1);
                float envelope = (1f - t) * Mathf.Min(1f, i / (rate * 0.005f));
                data[i] = (tone * (1f - noise) + n * noise) * envelope * 0.6f;
            }
            var clip = AudioClip.Create($"sfx_{kind}", samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
