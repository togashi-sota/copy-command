using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証中】ACT1 の中断セーブ（1枠）。扉を選ぶ画面に来たときに自動で保存し、クリア・敗北で消す。
    /// 【戦闘中心 v3 から】部屋に入った時・報酬の画面でも保存する（CaptureRoom・ActRunManager.Checkpoint）。戦闘の途中で閉じたら、その戦闘のはじめから（HP は低い方）。
    /// 保存先：Application.persistentDataPath/act1_save.json（Windows：%USERPROFILE%/AppData/LocalLow/会社名/製品名/）。
    /// 安全：一時ファイルに書いてから置き換える／読み込みは中身を全部確かめ、壊れていれば使わない（ファイルは消さず .broken に退避）。
    /// </summary>
    public static class ActSave
    {
        /// <summary>2026-10-05：1 → 2（地図 v2 で、同じ種から作る地図が変わったため。古いセーブは使わない）。2026-10-06：3（部屋の途中から続ける）。</summary>
        public const int Version = ActSaveData.CurrentVersion;
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "act1_save.json");

        public static ActSaveData Capture(ActRun run) => new ActSaveData
        {
            roleId = run.Role.Id, seed = run.Seed, floor = run.Floor, copyCentric = run.CopyCentric && !run.CombatRun, puzzle = run.PuzzleRun, combat = run.CombatRun, hp = run.Hp, maxHp = run.MaxHp, shards = run.Shards, removePrice = run.RemovePrice,
            deck = run.Deck.Select(c => c.name).ToList(),
            items = run.Items.Select(i => i.ToString()).ToList(),
            imprints = run.Imprints.Select(i => i.ToString()).ToList(),
            history = new List<string>(run.History),
            journal = new List<string>(run.Journal),
            battlesWon = run.BattlesWon, totalTurns = run.TotalTurns, totalDamage = run.TotalDamage, shardsEarned = run.ShardsEarned, missionsDone = run.MissionsDone,
            savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            parBonuses = run.ParBonuses,
            noDamageWins = run.NoDamageWins,
            usedKeys = run.UsedCards.Keys.ToList(), usedValues = run.UsedCards.Values.ToList(),
            damageKeys = run.DamageBySource.Keys.ToList(), damageValues = run.DamageBySource.Values.ToList(),
            hpFlow = new List<int>(run.HpFlow),
            playSeconds = run.PlaySeconds, trialsCleared = run.TrialsCleared, trialsFailed = run.TrialsFailed, copyUses = run.CopyUsesTotal, copyKills = run.CopyKillsTotal,
            v4 = run.V4Run, tactics = run.TacticCards.Select(c => c.name).ToList(), bigHits = run.BigHitsTotal, tacticUses = run.TacticUsesTotal, enemyDamage = run.EnemyDamageTotal, enemyDamageByOthers = run.EnemyDamageByOthersTotal,
            forged = run.Forged.ToList(), unlockedCards = run.UnlockedCards.ToList(), challenge = run.Challenge, bossRetryUsed = run.BossRetryUsed, bossEntryHp = run.BossEntryHp, // 【完成版】
            firstLesson = run.FirstLesson, mapRev = run.MapRev, // 【RC5】
            midBattleResumes = run.MidBattleResumes, // 【RC5・最終ゲート】
        };

        /// <summary>【戦闘中心 v3】部屋の途中のセーブ（部屋に入った／報酬の画面）。扉の画面は Capture のまま（roomId 空）。</summary>
        public static ActSaveData CaptureRoom(ActRun run, string phase, int battleHp = 0, int battleTurn = 0)
        {
            var d = Capture(run);
            if (run.Current == null) return d;
            d.roomId = run.Current.Id;
            d.roomPhase = phase;
            d.battleHp = battleHp;
            d.battleTurn = battleTurn;
            d.swapsLeft = run.SwapsLeft;
            d.tacticSwapsLeft = run.TacticSwapsLeft;
            if (phase == "reward")
            {
                d.rewardCards = run.RewardCards.Select(r => r.Card.name).ToList();
                d.rewardReasons = run.RewardCards.Select(r => r.Reason ?? "").ToList();
                d.rewardPicks = run.RewardPicksLeft;
                d.rewardExtra = run.ExtraBuysLeft;
                d.rewardImprints = run.RewardImprints.Select(i => i.ToString()).ToList();
                d.rewardText = run.LastRewardBreakdown ?? "";
                d.rewardUpgrades = run.RewardUpgradesLeft; // 【RC6】
            }
            if (run.Current.Kind == RoomKind.Rest) d.restDone = run.RestDone.ToList(); // 【RC6】
            if (run.Current.Kind == RoomKind.Shop)
            {
                d.shopSold = Enumerable.Range(0, run.ShopCards.Count).Where(i => run.ShopCards[i].Sold).Concat(Enumerable.Range(0, run.ShopItems.Count).Where(i => run.ShopItems[i].Sold).Select(i => 100 + i)).ToList();
                d.shopImprintSold = run.ShopImprintSold;
                d.shopRemoveUsed = run.ShopRemoveUsed;
                d.shopUpgradeUsed = run.ShopUpgradeUsed; // 【RC6】
                d.shopStock = true; // 【RC5・最終ゲート】品ぞろえそのもの（続きからで作り直さない）
                d.shopCards = run.ShopCards.Select(c => c.Card.name).ToList();
                d.shopCardPrices = run.ShopCards.Select(c => c.Price).ToList();
                d.shopItems = run.ShopItems.Select(i => i.Id.ToString()).ToList();
                d.shopItemPrices = run.ShopItems.Select(i => i.Price).ToList();
                d.shopImprint = run.ShopImprint?.ToString() ?? "-"; // 【RC7】
            }
            return d;
        }

        /// <summary>【戦闘中心 v3】部屋の途中で保存（一時ファイル → 置き換え）。</summary>
        public static bool SaveRoom(ActRun run, string phase, int battleHp = 0, string path = null, int battleTurn = 0) => Write(JsonUtility.ToJson(CaptureRoom(run, phase, battleHp, battleTurn), true), path);

        public static string ToJson(ActRun run) => JsonUtility.ToJson(Capture(run), true);

        /// <summary>JSON からランを作り直す。おかしな所があれば false と理由（ランは作らない）。</summary>
        public static bool TryRestore(string json, Func<string, CommandData> lookup, out ActRun run, out string error)
        {
            run = null;
            error = null;
            ActSaveData d;
            try { d = JsonUtility.FromJson<ActSaveData>(json); }
            catch (Exception e) { error = "JSON を読めない：" + e.Message; return false; }
            if (d == null) { error = "空のデータ"; return false; }
            if (d.version != Version) { error = $"ゲームの更新で、前の版（{d.version}）のセーブは使えない。新しく始めてください"; return false; }
            var role = ActContent.Roles.FirstOrDefault(r => r.Id == d.roleId);
            if (role == null) { error = $"知らない役職：{d.roleId}"; return false; }
            if (d.maxHp <= 0 || d.hp <= 0 || d.hp > d.maxHp || d.maxHp > 99) { error = $"HP がおかしい（{d.hp}/{d.maxHp}）"; return false; }
            if (d.shards < 0 || d.removePrice < 0) { error = "ルーン片・値段がおかしい"; return false; }
            if (d.deck == null || d.deck.Count == 0 || d.deck.Count > 80) { error = "デッキがおかしい"; return false; }
            var deck = new List<CommandData>();
            foreach (var n in d.deck)
            {
                var c = lookup(n);
                if (c == null) { error = $"知らないカード：{n}"; return false; }
                deck.Add(c);
            }
            var items = new List<ItemId>();
            foreach (var s in d.items ?? new List<string>())
            {
                if (!Enum.TryParse(s, out ItemId id) || !Enum.IsDefined(typeof(ItemId), id)) { error = $"知らないアイテム：{s}"; return false; }
                items.Add(id);
            }
            if (items.Count > ActContent.ItemSlots) { error = "アイテムが多すぎる"; return false; }
            var imprints = new List<ImprintId>();
            foreach (var s in d.imprints ?? new List<string>())
            {
                if (!Enum.TryParse(s, out ImprintId id) || !Enum.IsDefined(typeof(ImprintId), id)) { error = $"知らない刻印：{s}"; return false; }
                imprints.Add(id);
            }
            var tactics = new List<CommandData>();
            foreach (var n in d.tactics ?? new List<string>())
            {
                var c = lookup(n);
                if (c == null || c.type != CommandType.Tactic) { error = $"知らない戦術札：{n}"; return false; }
                tactics.Add(c);
            }
            if (d.v4 && (tactics.Count == 0 || tactics.Count > 60)) { error = "戦術札のデッキがおかしい"; return false; }
            var r = new ActRun(role, d.seed, lookup, d.copyCentric, d.puzzle, d.combat, d.v4, d.mapRev); // 【RC5・コードレビュー】古いセーブは古い地図の作り方で
            if (d.v4) r.SetTacticCards(tactics);
            if (d.v4 && d.firstLesson) r.ForceFirstDoor("v4_i1"); // 【RC5】はじめてのランの扉（地図は種から作り直すので、決めた扉もここで同じに）
            if (d.floor < 0 || d.floor >= r.FloorCount) { error = $"階がおかしい（{d.floor}）"; return false; }
            r.ApplySave(d, deck, items, imprints);
            // 【戦闘中心 v3】部屋の途中から（部屋はその階の扉の1つでなければ使わない）
            if (!string.IsNullOrEmpty(d.roomId))
            {
                var room = r.Doors.FirstOrDefault(x => x.Id == d.roomId);
                // 【RC5】RC4 までのセーブには firstLesson が無い：はじめてのランの扉（2階の癒し手の陣・5階の追跡の群れ）なら、はじめてのランとして読む
                if (room == null && d.v4 && !d.firstLesson) { r.ForceFirstDoor("v4_i1"); room = r.Doors.FirstOrDefault(x => x.Id == d.roomId); }
                if (room == null) { error = $"知らない部屋：{d.roomId}"; return false; }
                if (d.roomPhase == "reward")
                {
                    var cards = new List<RewardCard>();
                    for (int i = 0; i < (d.rewardCards?.Count ?? 0); i++)
                    {
                        var c = lookup(d.rewardCards[i]);
                        if (c == null) { error = $"知らないカード：{d.rewardCards[i]}"; return false; }
                        cards.Add(new RewardCard { Card = c, Reason = d.rewardReasons != null && i < d.rewardReasons.Count && d.rewardReasons[i].Length > 0 ? d.rewardReasons[i] : null });
                    }
                    var imps = new List<ImprintId>();
                    foreach (var s in d.rewardImprints ?? new List<string>()) if (Enum.TryParse(s, out ImprintId id) && Enum.IsDefined(typeof(ImprintId), id)) imps.Add(id);
                    r.ResumeReward(room, cards, Mathf.Clamp(d.rewardPicks, 0, 2), imps, d.rewardText, Mathf.Clamp(d.rewardExtra, 0, 1), d.rewardUpgrades);
                    r.RestoreSwaps(d.swapsLeft, d.tacticSwapsLeft);
                }
                else
                {
                    r.ResumeRoom(room, d.shopSold, d.shopImprintSold, d.shopRemoveUsed, d.shopStock ? d : null, d.shopUpgradeUsed, d.restDone);
                    if (room.Kind == RoomKind.Shop) r.RestoreSwaps(d.swapsLeft, d.tacticSwapsLeft);
                    if (d.battleHp > 0) r.SetHpForTest(Mathf.Clamp(Mathf.Min(r.Hp, d.battleHp), 1, r.MaxHp)); // 戦闘の途中で閉じた：最初から・HP は低い方
                    r.ResumedBattleTurn = Mathf.Max(0, d.battleTurn);
                }
            }
            run = r;
            return true;
        }

        /// <summary>保存（一時ファイル → 置き換え）。失敗しても例外を出さない。</summary>
        public static bool Save(ActRun run, string path = null) => Write(ToJson(run), path);

        static bool Write(string json, string path)
        {
            path = path ?? DefaultPath;
            try
            {
                // 監査 F9：消してから移すと、その間に落ちたときセーブが無くなる → 置き換え（Replace）を使う。【完成版】WebGL では Replace が使えなければ上書き・書いたら同期
                WebStorage.WriteAllTextSafe(path, json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ActSave] 保存できなかった：" + e.Message);
                return false;
            }
        }

        public static bool Exists(string path = null) => File.Exists(path ?? DefaultPath);

        /// <summary>読み込み。壊れていたら .broken に退避して false（元の場所には残さない＝次の起動で同じ失敗をしない）。</summary>
        public static bool TryLoad(Func<string, CommandData> lookup, out ActRun run, out string error, string path = null)
        {
            path = path ?? DefaultPath;
            run = null;
            error = null;
            if (!File.Exists(path)) { error = "セーブが無い"; return false; }
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception e) { error = "読めない（" + e.GetType().Name + "）"; return false; } // 【外部テスト】例外の文にはフォルダの場所（ユーザー名）が入るので画面に出さない
            if (TryRestore(json, lookup, out run, out error)) return true;
            try { File.Copy(path, path + ".broken", true); File.Delete(path); } catch (Exception) { /* 退避できなくてもゲームは続ける */ }
            return false;
        }

        public static void Delete(string path = null)
        {
            path = path ?? DefaultPath;
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            WebStorage.Sync(); // 【完成版】ブラウザでも消したことを残す
        }

        /// <summary>保存されている内容の短い説明（タイトルの「続きから」に出す）。読めなければ null。</summary>
        public static string Describe(string path = null)
        {
            path = path ?? DefaultPath;
            try
            {
                var d = JsonUtility.FromJson<ActSaveData>(File.ReadAllText(path));
                var role = ActContent.Roles.FirstOrDefault(r => r.Id == d.roleId);
                if (role != null && d.version != Version) return $"前の版のセーブ（{role.Name}・{d.floor + 1}階）：続きからは使えない";
                if (role == null) return null;
                string name = d.combat ? ActContent.CombatRole(role).Name : d.copyCentric ? ActContent.CopyRunRole(role).Name : role.Name;
                string where = string.IsNullOrEmpty(d.roomId) ? "" : d.roomPhase == "reward" ? "・報酬の画面" : "・部屋の中（戦闘は最初から）";
                return $"{(d.combat ? "" : d.puzzle ? "パズル中心・" : d.copyCentric ? "コピー中心・" : "")}{name}・{d.floor + 1}階{where}・HP {(d.battleHp > 0 ? Mathf.Min(d.hp, d.battleHp) : d.hp)}/{d.maxHp}・{d.savedAt}";
            }
            catch (Exception) { return null; }
        }
    }
}
