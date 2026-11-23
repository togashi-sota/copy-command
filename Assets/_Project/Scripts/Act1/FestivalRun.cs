using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭の1回の挑戦（1つのクエスト＝1つの戦闘）の記録（純粋な C#：テストと記録で使う）。
    /// 挑戦＝LEVEL を選んで始めてから、クリア・倒れた・最初からやり直した・やめた まで。やり直すと新しい挑戦（時間・ターン・ヒントも 0 から）。
    /// お手本（スタッフの実演）の挑戦は記録しない（CSV・ランキングに入れない）。個人を特定できる物は持たない（名前・入力した文字は無い）。
    /// </summary>
    public class FestivalRun
    {
        public int Level;
        /// <summary>始めた時刻・終えた時刻（Time.unscaledTime）。</summary>
        public float StartedAt, EndedAt;
        public int Turns, Damage, Hints;
        /// <summary>この LEVEL で何回目の挑戦か（1＝はじめての挑戦。倒れた・やり直したで +1。次の人へ・別の LEVEL で 1 に戻る）。</summary>
        public int Attempt = 1;
        /// <summary>お手本（スタッフの実演）：記録しない。</summary>
        public bool Demo;
        /// <summary>敵を利用した回数（敵が受けたダメージのうち、あなたの直接攻撃ではないもの）：まね・敵の攻撃（同士討ち）・仕掛け（落石・トゲ・炎）・大技。</summary>
        public int CopyHits, FriendlyHits, HazardHits, BigHits;
        /// <summary>1ターンに敵へ与えた合計の最大（「ボスに大ダメージ」の演出・記録）。</summary>
        public int BestTurnDamage;
        public bool Finished, Abandoned;
        /// <summary>終わり方（記録の result 列）：clear・gameover・restart・quit・idle。</summary>
        public string Result = "";

        public int EnemyHits => CopyHits + FriendlyHits + HazardHits + BigHits;
        /// <summary>かかった秒（終わっていなければ now まで）。</summary>
        public float SecondsAt(float now) => System.Math.Max(0f, (Finished || Abandoned ? EndedAt : now) - StartedAt);
        public float Seconds => SecondsAt(EndedAt);

        /// <summary>敵が受けた1つのダメージを分ける：あなたの直接攻撃（自分の命令の斬撃など）は数えない。</summary>
        public void CountEnemyHit(DamageCause c)
        {
            if (c == null) { HazardHits++; return; }
            if (c.Source != null && c.Source.IsPlayer && !c.ByCopy && c.Kind == DamageKind.Direct) return; // あなたの直接攻撃
            if (c.Kind == DamageKind.Clock) BigHits++;
            else if (CopyCore.IsCopyOrigin(c)) CopyHits++;
            else if (c.Kind == DamageKind.Fire || c.Kind == DamageKind.Spike || c.Kind == DamageKind.Gaze) HazardHits++;
            else FriendlyHits++;
        }

        /// <summary>あなたの直接攻撃か（「敵を利用した」に入らない）。</summary>
        public static bool IsPlayerDirect(DamageCause c) => c != null && c.Source != null && c.Source.IsPlayer && !c.ByCopy && c.Kind == DamageKind.Direct;
    }

    /// <summary>
    /// 【文化祭クエスト v2】ランク（クリアしたときだけ）：3つの「できたこと」の数で S／A／B／C。タイムはランクに入れない（本日のランキングで競う）。
    /// PERFECT＝S ＋ ノーダメージ ＋ はじめての挑戦（倒れていない・やり直していない）。
    /// </summary>
    public static class FestivalRank
    {
        /// <summary>できたこと：0 敵を利用した回数が目標以上・1 受けたダメージが目安以下・2 ターンが目安以内。</summary>
        public static bool[] Checks(FestivalLevelDef def, FestivalRun r) => new[]
        {
            r.EnemyHits >= def.EnemyUseGoal,
            r.Damage <= def.DamageLimit,
            r.Turns <= def.ParTurns,
        };

        public static string Rank(FestivalLevelDef def, FestivalRun r)
        {
            int n = Checks(def, r).Count(x => x);
            return n >= 3 ? "S" : n == 2 ? "A" : n == 1 ? "B" : "C";
        }

        /// <summary>★ PERFECT：S・ノーダメージ・はじめての挑戦。</summary>
        public static bool Perfect(FestivalLevelDef def, FestivalRun r) => r.Finished && Rank(def, r) == "S" && r.Damage == 0 && r.Attempt == 1;

        /// <summary>できたことの名前（結果の画面・記録）。</summary>
        public static string[] CheckNames(FestivalLevelDef def) => new[]
        {
            $"敵を利用した攻撃 {def.EnemyUseGoal}回以上",
            def.DamageLimit <= 0 ? "ノーダメージ" : $"受けたダメージ {def.DamageLimit}以下",
            $"{def.ParTurns}ターン以内",
        };

        /// <summary>ランクの順（ランキングの並べ方：ランクが上 → タイムが速い）。</summary>
        public static int Order(string rank) => rank == "S" ? 0 : rank == "A" ? 1 : rank == "B" ? 2 : rank == "C" ? 3 : 9;

        public static string Time(float sec)
        {
            int s = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(sec));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
