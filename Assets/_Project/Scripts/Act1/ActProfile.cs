using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    public static class ActProfile
    {
        public const int Version = ActProfileData.CurrentVersion;
        /// <summary>最初から選べる役職（分かりやすい3つ）。残りは遊ぶと解放される。</summary>
        public static readonly string[] StartRoles = { "standard", "vanguard", "engineer" };

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "act1_profile.json");

        /// <summary>役職の解放条件（文）。最初から選べる役職は null。</summary>
        public static string UnlockText(string roleId) => roleId switch
        {
            "observer" => "ランを1回終える（勝ち負けは問わない）",
            "mirrorist" => "4階（危険戦）に着く",
            "commander" => "ACT1 を1回クリアする",
            _ => null,
        };

        public static bool IsUnlocked(ActProfileData p, string roleId) =>
            p == null || p.allRolesUnlocked || StartRoles.Contains(roleId) || p.unlockedRoles.Contains(roleId);

        /// <summary>ランの結果を記録して、新しく解放された役職の名前を返す。</summary>
        public static List<string> RecordRun(ActProfileData p, ActRun run, IEnumerable<string> usedCards)
        {
            var newly = new List<string>();
            if (p == null || run == null) return newly;
            p.runsFinished++;
            int floor = Math.Min(run.Floor + 1, run.FloorCount);
            var rec = p.Record(run.Role.Id);
            rec.runs++;
            rec.bestFloor = Math.Max(rec.bestFloor, floor);
            if (run.Cleared) { rec.clears++; p.clears++; }
            foreach (var c in usedCards ?? Enumerable.Empty<string>()) if (!p.usedCards.Contains(c)) p.usedCards.Add(c);
            void Unlock(string id, bool cond)
            {
                if (!cond || IsUnlocked(p, id)) return;
                p.unlockedRoles.Add(id);
                newly.Add(ActContent.Roles.First(r => r.Id == id).Name);
            }
            Unlock("observer", true);
            Unlock("mirrorist", floor >= 4);
            Unlock("commander", run.Cleared);
            return newly;
        }

        public static string ToJson(ActProfileData p) => JsonUtility.ToJson(p, true);

        public static bool TryParse(string json, out ActProfileData p)
        {
            p = null;
            try { p = JsonUtility.FromJson<ActProfileData>(json); }
            catch (Exception) { return false; }
            if (p == null || p.version != Version) return false;
            p.unlockedRoles ??= new List<string>();
            p.records ??= new List<RoleRecord>();
            p.usedCards ??= new List<string>();
            p.hintsSeen ??= new List<string>();
            p.finalRecords ??= new List<RoleRecord>(); // 【完成版】古いプロフィールには無い
            p.finalRoles ??= new List<string>();
            p.finalCards ??= new List<string>();
            return true;
        }

        /// <summary>読む。無ければ新しいプロフィール。壊れていれば .broken へ退避して新しく始める（消さない）。</summary>
        public static ActProfileData Load(string path = null)
        {
            path ??= DefaultPath;
            try
            {
                if (!File.Exists(path)) return new ActProfileData();
                if (TryParse(File.ReadAllText(path), out var p)) return p;
                File.Copy(path, path + ".broken", true);
                File.Delete(path);
            }
            catch (Exception e) { Debug.LogWarning("[ActProfile] 読めなかった：" + e.Message); }
            return new ActProfileData();
        }

        /// <summary>一時ファイルに書いてから置き換える（途中で落ちても前のプロフィールが残る）。</summary>
        public static bool Save(ActProfileData p, string path = null)
        {
            path ??= DefaultPath;
            try
            {
                WebStorage.WriteAllTextSafe(path, ToJson(p)); // 【完成版】WebGL でも残る（置き換えが使えなければ上書き・同期）
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[ActProfile] 書けなかった：" + e.Message); return false; }
        }
    }
}
