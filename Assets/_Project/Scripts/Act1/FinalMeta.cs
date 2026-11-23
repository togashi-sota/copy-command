using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【完成版・2026-10-20】完成版 ACT1 の周回（横に広がる解放だけ。HP や攻撃力が増える恒久強化は入れない）。
    /// - 役職：最初は旅の剣士だけ。旅の剣士でクリア → 観測士。2つの役職でクリア → 指揮官。
    /// - 札：役職ごとの初クリアで、その役職の「号令の札」が報酬に出るようになる（剣士 → けしかけ／観測士 → 進め／指揮官 → 忍び足）。
    /// - 挑戦（反響深度）：【RC12】はじめてのクリアで 1（役職とは別）。深度 n でクリア → n+1（最大 V4Content.MaxChallenge）。
    /// - 記録：役職ごとのクリア（✓）・最高到達・最少ターン・最短の分・クリアした深度。
    /// Unity に依存しない（解析・テストで同じ規則を使う）。
    /// </summary>
    public static class FinalMeta
    {
        public static readonly string[] RoleOrder = { "standard", "observer", "commander" };

        /// <summary>役職の初クリアで解放される札。</summary>
        public static string CardForRole(string roleId) => roleId switch
        {
            "standard" => "Incite",
            "observer" => "March",
            "commander" => null, // 【通常リリース版】忍び足は最初から
            _ => null,
        };

        public static bool IsRoleUnlocked(ActProfileData p, string roleId) =>
            roleId == "standard" || p == null || p.allRolesUnlocked || p.finalRoles.Contains(roleId);

        public static string UnlockText(string roleId) => roleId switch
        {
            "observer" => "旅の剣士で ACT1 をクリア",
            "commander" => "ACT1 をクリア",
            _ => null,
        };

        public static int ClearedRoleCount(ActProfileData p) => p == null ? 0 : p.finalRecords.Count(r => r.clears > 0);

        /// <summary>報酬に出せる札（LockedCards のうち解放済みのもの）。</summary>
        public static HashSet<string> UnlockedCards(ActProfileData p)
        {
            if (p == null || p.allRolesUnlocked) return new HashSet<string>(V4Content.LockedCards);
            return new HashSet<string>(p.finalCards);
        }

        /// <summary>【通常リリース版】反響ランク a が b より良いか（S ＞ A ＞ B ＞ C ＞ 無し）。</summary>
        public static bool RankBetter(string a, string b) => RankValue(a) > RankValue(b);
        static int RankValue(string r) => r == "S" ? 4 : r == "A" ? 3 : r == "B" ? 2 : r == "C" ? 1 : 0;

        public static int MaxChallenge(ActProfileData p) => p == null ? 0 : p.allRolesUnlocked ? V4Content.MaxChallenge : Math.Min(V4Content.MaxChallenge, p.challengeUnlocked);

        /// <summary>
        /// ランの終わりに記録する。新しく起きたこと（解放・自己ベスト）の文を返す（結果画面に出す）。
        /// minutes＝遊んだ分（切り上げ）。
        /// </summary>
        public static List<string> RecordRun(ActProfileData p, ActRun run, int minutes)
        {
            var news = new List<string>();
            if (p == null || run == null) return news;
            p.finalFinished++;
            var rec = p.FinalRecord(run.Role.Id);
            rec.runs++;
            int floor = Math.Min(run.Floor + 1, run.FloorCount);
            if (floor > rec.bestFloor && rec.bestFloor > 0 && !run.Cleared) news.Add($"自己ベスト：{floor}階まで（前 {rec.bestFloor}階）");
            rec.bestFloor = Math.Max(rec.bestFloor, floor);
            if (!run.Cleared) return news;

            p.finalClears++;
            bool firstClear = rec.clears == 0;
            rec.clears++;
            if (!run.BossRetryUsed) rec.cleanClears++;
            if (rec.bestTurns == 0 || run.TotalTurns < rec.bestTurns) { if (rec.bestTurns > 0) news.Add($"最少ターン更新：{run.TotalTurns}ターン（前 {rec.bestTurns}）"); rec.bestTurns = run.TotalTurns; }
            if (minutes > 0 && (rec.bestMinutes == 0 || minutes < rec.bestMinutes)) rec.bestMinutes = minutes;
            if (run.Challenge + 1 > rec.clearedChallenge)
            {
                if (run.Challenge > 0) news.Add($"{run.Role.Name}：反響深度{run.Challenge} をクリア");
                rec.clearedChallenge = run.Challenge + 1;
            }
            if (firstClear) news.Add($"{run.Role.Name} で初クリア！");
            // 【通常リリース版】反響ランク（敵へのダメージのうち、敵自身と仕掛けが与えた割合）：役職ごとに一番よいものを残す
            string rank = run.EchoRank;
            if (rank != null && RankBetter(rank, rec.bestEchoRank))
            {
                if (!string.IsNullOrEmpty(rec.bestEchoRank)) news.Add($"反響ランク更新：{rank}（前 {rec.bestEchoRank}）");
                rec.bestEchoRank = rank;
            }

            // 札：役職の初クリア
            var card = CardForRole(run.Role.Id);
            if (firstClear && card != null && !p.finalCards.Contains(card)) { p.finalCards.Add(card); news.Add($"新しい札が報酬に出るようになった：{CardName(card)}"); }

            // 役職
            void UnlockRole(string id, bool cond)
            {
                if (!cond || p.finalRoles.Contains(id)) return;
                p.finalRoles.Add(id);
                news.Add($"新しい役職：{RoleName(id)}");
            }
            UnlockRole("observer", run.Role.Id == "standard" || ClearedRoleCount(p) >= 1);
            UnlockRole("commander", ClearedRoleCount(p) >= 1); // 【通常リリース版・ローグライクとデザイナーのレビュー「一番核らしい役職（敵どうしを戦わせる）が2回クリアの向こう」】1回目のクリアで観測士と指揮官の両方

            // 挑戦
            int before = p.challengeUnlocked;
            // 【RC12・本人「2周目だから難しい」と「観測士だからプレイが変わる」は別の仕組みに】はじめてのクリアで反響深度1（役職とは別。どの役職でも選べる）
            if (ClearedRoleCount(p) >= 1) p.challengeUnlocked = Math.Max(p.challengeUnlocked, 1);
            if (run.Challenge >= 1) p.challengeUnlocked = Math.Max(p.challengeUnlocked, Math.Min(V4Content.MaxChallenge, run.Challenge + 1));
            if (p.challengeUnlocked > before) news.Add($"挑戦「反響深度{p.challengeUnlocked}」が選べるようになった");
            return news;
        }

        /// <summary>次の目標（タイトル・結果画面に一言）。</summary>
        public static string NextGoal(ActProfileData p)
        {
            if (p == null) return null;
            if (!p.allRolesUnlocked)
            {
                if (!p.finalRoles.Contains("observer")) return "次の目標：旅の剣士で ACT1 をクリア（観測士と指揮官が解放される）";
                if (!p.finalRoles.Contains("commander")) return "次の目標：別の役職でもクリア（指揮官が解放される）";
            }
            if (ClearedRoleCount(p) < 1) return "次の目標：ACT1 をクリア（新しい役職と、挑戦「反響深度1」が解放される）";
            if (p.challengeUnlocked < V4Content.MaxChallenge) return $"次の目標：反響深度{Math.Max(1, p.challengeUnlocked)} でクリア（役職はどれでも）";
            if (!p.finalRecords.Any(r => r.clearedChallenge > V4Content.MaxChallenge)) return $"次の目標：反響深度{V4Content.MaxChallenge} でクリア"; // 【RC5・レビュー3】深度3 をクリアする前に「すべて解放」と出ていた
            if (!p.finalRecords.Any(r => r.bestEchoRank == "S")) return "次の目標：反響ランク S（敵へのダメージの大半を敵どうし・仕掛けで与え、受けたダメージ 10 以下でクリア）"; // 【通常リリース版】
            return "すべての挑戦をクリアした。役職ごとの反響ランク S・最少ターンを目指そう";
        }

        static string RoleName(string id) => id switch { "standard" => "旅の剣士", "observer" => "観測士", "commander" => "指揮官", _ => id };
        static string CardName(string asset) => asset switch { "Incite" => "けしかけ", "March" => "進め", "Sneak" => "忍び足", _ => asset };
    }
}
