using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC3・2026-10-21 本人「音が大きすぎる」】全体・BGM・効果音の音量（プロフィールに残る。初期値は 全体 50%・BGM 35%・効果音 50%）。
    /// RC2 は 全体 100%・BGM 32%・効果音 50%（効果音は音ごとの大きさをそろえていなかった）。BGM は RC2 よりだいたい 5 dB、効果音（当たり・爆発）は 12 dB ほど小さい。
    /// </summary>
    public static class SoundSettings
    {
        public static void Apply(ActProfileData p)
        {
            if (p == null) return;
            AudioListener.volume = Mathf.Clamp01(p.volMaster / 100f);
            Bgm.SetVolume(p.volBgm / 100f);
            Sfx.Volume = Mathf.Clamp01(p.volSe / 100f);
            ScreenFx.Disabled = p.shakeOff; // 【RC5】画面の揺れ（設定と同じ所で）
        }
    }
}
