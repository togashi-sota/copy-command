using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭の集計（この PC の中だけ・個人を特定できる物は書かない）。
    /// 1回の挑戦が終わるたびに CSV へ1行：日付・時刻・LEVEL・終わり方（clear／gameover／restart／quit／idle）・秒・ターン・被ダメ・敵を利用した攻撃（内訳）・ランク・PERFECT・何回目の挑戦・ヒント・版。
    /// お手本（スタッフの実演）の挑戦は書かない。置き場所：Windows のビルド＝exe と同じフォルダの FestivalLogs/festival_quest.csv（USB などで持ち帰れる）／Editor＝Logs/festival/／ブラウザ＝ブラウザの保存領域。
    /// 「本日のランキング」は CSV の今日のクリアの行から（スタッフの「ベストを消す」より後の行だけ）。LEVEL ごとに別（混ぜない）。
    /// 旧版（3戦の文化祭チャレンジ）の festival_plays.csv とは別のファイル（列が違う）。
    /// </summary>
    public static class FestivalStats
    {
        public const string FileName = "festival_quest.csv";
        const string ResetFile = "festival_best_reset.txt";
        public const string Header = "date,time,level,result,seconds,turns,damage,enemy_hits,copy,friendly,hazard,big,no_damage,rank,perfect,attempt,hints,version";

        /// <summary>書くか（自動撮影・バッチ・テストでは書かない）。</summary>
        public static bool Enabled = true;
        /// <summary>テスト用：置き場所を変える（null で既定）。</summary>
        public static string DirOverride;

        public static string Dir
        {
            get
            {
                if (!string.IsNullOrEmpty(DirOverride)) return DirOverride;
                try
                {
                    if (Application.isEditor) return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs/festival");
#if UNITY_STANDALONE && !UNITY_EDITOR
                    return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "FestivalLogs");
#else
                    return Path.Combine(Application.persistentDataPath, "festival");
#endif
                }
                catch (Exception) { return Path.Combine(Application.persistentDataPath, "festival"); }
            }
        }

        public static string CsvPath => Path.Combine(Dir, FileName);

        public class Row
        {
            public DateTime At;
            public int Level;
            public string Result;
            public bool Clear => Result == "clear";
            public float Seconds;
            public int Turns, Damage, EnemyHits, Attempt, Hints;
            public string Rank;
            public bool Perfect;
        }

        static List<Row> cache;
        static DateTime? resetAt;

        public static void Invalidate() { cache = null; resetAt = null; }

        /// <summary>1回の挑戦を書く（お手本は書かない・失敗しても遊びは止めない）。rank は クリアのときだけ（それ以外は "-"）。</summary>
        public static void Append(FestivalRun r, string rank, bool perfect, DateTime now)
        {
            if (r == null || r.Demo) return;
            bool clear = r.Result == "clear";
            var row = new Row { At = now, Level = r.Level, Result = r.Result, Seconds = r.Seconds, Turns = r.Turns, Damage = r.Damage, EnemyHits = r.EnemyHits, Rank = clear ? rank : "-", Perfect = clear && perfect, Attempt = r.Attempt, Hints = r.Hints };
            Load().Add(row);
            if (!Enabled) return;
            try
            {
                Directory.CreateDirectory(Dir);
                bool fresh = !File.Exists(CsvPath);
                var ci = CultureInfo.InvariantCulture;
                string line = string.Join(",", new[]
                {
                    now.ToString("yyyy-MM-dd", ci), now.ToString("HH:mm:ss", ci), LevelKey(r.Level), string.IsNullOrEmpty(r.Result) ? "quit" : r.Result,
                    r.Seconds.ToString("0.0", ci), r.Turns.ToString(ci), r.Damage.ToString(ci), r.EnemyHits.ToString(ci),
                    r.CopyHits.ToString(ci), r.FriendlyHits.ToString(ci), r.HazardHits.ToString(ci), r.BigHits.ToString(ci),
                    clear && r.Damage == 0 ? "1" : "0", row.Rank, row.Perfect ? "1" : "0", r.Attempt.ToString(ci), r.Hints.ToString(ci), BuildInfo.Version,
                });
                File.AppendAllText(CsvPath, (fresh ? Header + "\n" : "") + line + "\n", new UTF8Encoding(false));
                WebStorage.Sync();
            }
            catch (Exception e) { Debug.LogWarning("[FestivalStats] 書けなかった：" + e.Message); }
        }

        public static string LevelKey(int level) => "lv" + (level + 1);
        static int LevelOf(string key) => key == "lv1" ? 0 : key == "lv2" ? 1 : key == "lv3" ? 2 : -1;

        public static List<Row> Load()
        {
            if (cache != null) return cache;
            cache = new List<Row>();
            if (!Enabled) return cache;
            try
            {
                if (!File.Exists(CsvPath)) return cache;
                var ci = CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(CsvPath).Skip(1))
                {
                    var f = line.Split(',');
                    if (f.Length < 17) continue;
                    if (!DateTime.TryParseExact(f[0] + " " + f[1], "yyyy-MM-dd HH:mm:ss", ci, DateTimeStyles.None, out var at)) continue;
                    int lv = LevelOf(f[2]);
                    if (lv < 0) continue;
                    float.TryParse(f[4], NumberStyles.Float, ci, out var sec);
                    int.TryParse(f[5], out var turns); int.TryParse(f[6], out var dmg); int.TryParse(f[7], out var hits);
                    int.TryParse(f[15], out var att); int.TryParse(f[16], out var hints);
                    cache.Add(new Row { At = at, Level = lv, Result = f[3], Seconds = sec, Turns = turns, Damage = dmg, EnemyHits = hits, Rank = f[13], Perfect = f[14] == "1", Attempt = att, Hints = hints });
                }
            }
            catch (Exception e) { Debug.LogWarning("[FestivalStats] 読めなかった：" + e.Message); }
            return cache;
        }

        static DateTime ResetAt()
        {
            if (resetAt.HasValue) return resetAt.Value;
            resetAt = DateTime.MinValue;
            if (!Enabled) return resetAt.Value;
            try
            {
                string p = Path.Combine(Dir, ResetFile);
                if (File.Exists(p) && DateTime.TryParseExact(File.ReadAllText(p).Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) resetAt = t;
            }
            catch (Exception) { }
            return resetAt.Value;
        }

        /// <summary>本日のランキング（この LEVEL のクリアだけ・ランクが上 → ターンが少ない → タイムが速い の順に n 件）。【初見レビュー2回目（戦術好き）「タイム順だと手順を覚えた人が勝つ」】ターンを先に。</summary>
        public static List<Row> TodayTop(int level, DateTime now, int n)
        {
            var since = ResetAt();
            return Load().Where(x => x.Level == level && x.Clear && x.At.Date == now.Date && x.At >= since)
                .OrderBy(x => FestivalRank.Order(x.Rank)).ThenBy(x => x.Turns).ThenBy(x => x.Seconds).Take(n).ToList();
        }

        /// <summary>本日の1位（無ければ null）。</summary>
        public static Row TodayBest(int level, DateTime now) => TodayTop(level, now, 1).FirstOrDefault();

        /// <summary>この記録は本日のランキングで何位か（1〜）。ランク → ターン → 秒。同じなら先の人が上。</summary>
        public static int PlaceOf(int level, DateTime now, string rank, int turns, float seconds)
        {
            var since = ResetAt();
            var rows = Load().Where(x => x.Level == level && x.Clear && x.At.Date == now.Date && x.At >= since).ToList();
            return 1 + rows.Count(x => FestivalRank.Order(x.Rank) < FestivalRank.Order(rank) || (x.Rank == rank && (x.Turns < turns || (x.Turns == turns && x.Seconds < seconds - 0.05f))));
        }

        /// <summary>スタッフ用：本日のベストを消す（記録の行は消さない。この時刻より後だけを数える）。</summary>
        public static void ResetTodayBest(DateTime now)
        {
            resetAt = now;
            if (!Enabled) return;
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, ResetFile), now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)); WebStorage.Sync(); }
            catch (Exception e) { Debug.LogWarning("[FestivalStats] " + e.Message); }
        }

        /// <summary>スタッフ用の集計（今日／全部）：LEVEL ごとの挑戦の回数・クリア・倒れた・やり直し・平均と最短の秒・ランクの数・PERFECT。</summary>
        public static string[] Summary(DateTime now, bool todayOnly)
        {
            var rows = Load().Where(x => !todayOnly || x.At.Date == now.Date).ToList();
            var lines = new List<string> { $"{(todayOnly ? "本日" : "全部")}：挑戦 {rows.Count}回（クリア {rows.Count(x => x.Clear)}・倒れた {rows.Count(x => x.Result == "gameover")}・やり直し {rows.Count(x => x.Result == "restart")}・やめた {rows.Count(x => x.Result == "quit" || x.Result == "idle")}）" };
            for (int lv = 0; lv < 3; lv++)
            {
                var r = rows.Where(x => x.Level == lv).ToList();
                var c = r.Where(x => x.Clear).ToList();
                string ranks = string.Join(" ", new[] { "S", "A", "B", "C" }.Select(k => $"{k}{c.Count(x => x.Rank == k)}"));
                string name = lv < FestivalContent.Levels.Length ? FestivalContent.Levels[lv].Label : $"LEVEL {lv + 1}";
                lines.Add(r.Count == 0 ? $"{name}：0回" :
                    $"{name}：挑戦 {r.Count}回・クリア {c.Count}・倒れた {r.Count(x => x.Result == "gameover")}" +
                    (c.Count > 0 ? $"・平均 {FestivalRank.Time(c.Average(x => x.Seconds))}・最短 {FestivalRank.Time(c.Min(x => x.Seconds))}・平均 {c.Average(x => x.Turns):0.0}ターン・{ranks}・PERFECT {c.Count(x => x.Perfect)}・ヒントあり {c.Count(x => x.Hints > 0)}" : ""));
            }
            return lines.ToArray();
        }
    }
}
