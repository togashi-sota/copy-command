using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>【検証中】1回の戦闘の結果（報酬の「写し取り」・ミッション・記録に使う）。BattleEvent から作る。</summary>
    public class BattleStats
    {
        public bool Won;
        public int Turns, HpBefore, HpAfter;
        public List<EnemyKind> Defeated = new List<EnemyKind>();
        /// <summary>トゲ・炎・爆発（砲兵）で倒した敵の数。</summary>
        public int EnvironmentKills;
        /// <summary>敵の攻撃（コピー・兵種の行動）で別の敵が傷ついた回数。</summary>
        public int FriendlyFire;
        /// <summary>敵がコピーした命令で敵がダメージを受けた回数。</summary>
        public int CopyDamage;
        /// <summary>【検証中】敵の攻撃（コピー・兵種の行動。爆発は除く）で「とどめ」を刺された敵の数。</summary>
        public int FriendlyKills;
        public int DamageTaken => Math.Max(0, HpBefore - HpAfter);
        /// <summary>【検証中】実際に受けたダメージの合計（回復で相殺しない）。無傷の判定に使う。</summary>
        public int GrossDamage;
        /// <summary>受けたダメージ（回復で相殺しない方。手で作った統計では差し引きの方）。</summary>
        public int DamageForRecord => Math.Max(GrossDamage, DamageTaken);
        /// <summary>【検証中・Copy Core V3】写し取り v2：コピーを使って倒した敵（兵種と倒し方）。倒し方：写し攻撃／写し砲／鏡晶／写しの移動でトゲ。</summary>
        public List<(EnemyKind kind, string how)> CopyKills = new List<(EnemyKind, string)>();
        /// <summary>【Copy Core V3】開けた写し箱の数（1つにつきルーン片 +ActContent.ChestShards）。</summary>
        public int ChestsOpened;
        /// <summary>【Puzzle v2】まねで動かした仕掛けの種類（運び紋・封印石・石像・鏡核・ボスの段階）。刻写 v2 の看板札に使う。</summary>
        public HashSet<string> CopyPuzzles = new HashSet<string>();
        /// <summary>【Puzzle v2】部屋の目標（討伐以外なら「目標」のルーン片）。</summary>
        public RoomGoal Goal = RoomGoal.Defeat;
        /// <summary>【Puzzle v2】やり直した部屋（無傷・速さ・ミッションのボーナスは無し）。</summary>
        public bool Restarted;
        /// <summary>
        /// 【戦闘中心 v3】意味のあるコピー利用（記録用の近似）：まね・写し砲が敵（道具以外も）にダメージを与えた回数＋まねで仕掛けが動いた回数（石像・鏡核・樽・鏡晶・箱）＋共鳴＋搬送のとどめ。
        /// </summary>
        public int CopyUses;
        /// <summary>【Combat V4】敵の大技が敵に当たった回数（大技の逆利用）・大技が発動した回数・使った戦術札の枚数（ActRunManager が入れる）。</summary>
        public int BigHitsOnEnemies, BigFired, TacticUses;
        /// <summary>【通常リリース版・反響ランク】敵（道具を除く）が受けたダメージの合計と、そのうちあなたの直接攻撃でない分（敵のまね・敵の攻撃・大技・仕掛け・反射）。</summary>
        public int EnemyDamage, EnemyDamageByOthers;

        public static BattleStats From(IReadOnlyList<BattleEvent> events, bool won, int turns, int hpBefore, int hpAfter)
        {
            var s = new BattleStats { Won = won, Turns = turns, HpBefore = hpBefore, HpAfter = hpAfter };
            var lastCause = new Dictionary<UnitModel, DamageCause>();
            var copyMoved = new HashSet<UnitModel>(); // 【戦闘中心 v3】まねの移動で動かされた敵（自分で動いたら外す）＝搬送
            foreach (var e in events)
            {
                if (e.Type == BattleEventType.Moved && e.Unit != null && !e.Unit.IsPlayer)
                {
                    if (e.Cause != null && e.Cause.ByCopy) copyMoved.Add(e.Unit);
                    else copyMoved.Remove(e.Unit);
                }
                if (e.Type == BattleEventType.ArmorBroken && e.Cause != null && (e.Cause.ByCopy || e.Cause.Kind == DamageKind.Resonance)) s.CopyPuzzles.Add("鎧割り"); // 【戦闘中心 v3】
                if (e.Type == BattleEventType.Resonance) { s.CopyUses++; s.CopyPuzzles.Add("同期"); }
                if (e.Type == BattleEventType.BossCloneStrike) s.CopyUses++;
                if (e.Cause != null && e.Cause.ByCopy && (e.Type == BattleEventType.StatueTurned || e.Type == BattleEventType.CoreHit || e.Type == BattleEventType.BarrelBurst || e.Type == BattleEventType.CrystalBurst || e.Type == BattleEventType.ChestOpened)) s.CopyUses++;
                if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer) s.GrossDamage += e.Amount;
                if (e.Type == BattleEventType.ChestOpened) s.ChestsOpened++;
                if (e.Type == BattleEventType.ClockFired) s.BigFired++; // 【Combat V4】
                if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && e.Cause.Kind == DamageKind.Clock && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit)
                { s.BigHitsOnEnemies++; s.CopyUses++; }
                // 【Puzzle v2】まねで動いた仕掛け（あなたの直接の分は数えない）
                if (e.Cause != null && e.Cause.ByCopy && PuzzleRules.IsGimmickEvent(e))
                {
                    string kind = e.Type == BattleEventType.CarryDone ? "運び"
                                : e.Type == BattleEventType.SealStoneBroken ? "封印"
                                : e.Type == BattleEventType.StatueTurned ? "石像"
                                : e.Type == BattleEventType.CoreHit || e.Type == BattleEventType.CoresShattered ? "同期" : null;
                    if (kind != null) s.CopyPuzzles.Add(kind);
                }
                if (e.Type == BattleEventType.PuzzleSolved) s.CopyPuzzles.Add(e.Amount == 1 ? "運び" : e.Amount == 2 ? "写し身" : "同期");
                if (e.Type == BattleEventType.ExitOpened && e.Cause != null && e.Cause.ByCopy) s.CopyPuzzles.Add("脱出"); // 【Puzzle v2】まねで出口を開けたときだけ（封印が弱まった後に自分で割ったときは出さない：レビュー）
                if (e.Unit == null || e.Unit.IsPlayer) continue;
                if (e.Type == BattleEventType.Damaged && e.Amount > 0 && !e.Unit.IsObject) // 【通常リリース版】反響ランク
                {
                    s.EnemyDamage += e.Amount;
                    if (e.Cause == null || !(CopyCore.IsPlayerDirect(e.Cause.Source, e.Cause.ByCopy) && (e.Cause.Kind == DamageKind.Direct || e.Cause.Kind == DamageKind.Trait))) s.EnemyDamageByOthers += e.Amount; // 【通常リリース版・QA】トゲの出どころは「押した人」なので、押してトゲへ入れた分も あなたの直接攻撃に数えていた
                }
                if (e.Type == BattleEventType.Damaged && e.Cause != null)
                {
                    lastCause[e.Unit] = e.Cause;
                    var src = e.Cause.Source;
                    if (src != null && !src.IsPlayer && src != e.Unit) s.FriendlyFire++;
                    if (e.Cause.ByCopy) s.CopyDamage++;
                    if (e.Amount > 0 && e.Cause.Kind != DamageKind.Resonance && CopyCore.IsCopyOrigin(e.Cause)) s.CopyUses++;
                }
                if (e.Type == BattleEventType.Died)
                {
                    s.Defeated.Add(e.Unit.Kind);
                    if (lastCause.TryGetValue(e.Unit, out var cc) && CopyKillHow(cc) is string how) s.CopyKills.Add((e.Unit.Kind, how));
                    else if (lastCause.TryGetValue(e.Unit, out var mc) && copyMoved.Contains(e.Unit) && mc != null && (mc.Kind == DamageKind.Gaze || mc.Kind == DamageKind.Fire || mc.Kind == DamageKind.Spike || (mc.Kind == DamageKind.Trait && mc.Source != null && mc.Source != e.Unit && !mc.Source.IsPlayer)))
                    { s.CopyKills.Add((e.Unit.Kind, "搬送")); s.CopyUses++; } // 【戦闘中心 v3】まねで動かした敵が、視線・炎・トゲ・ほかの敵の技で倒れた
                    if (lastCause.TryGetValue(e.Unit, out var c) &&
                        (c.Kind == DamageKind.Spike || c.Kind == DamageKind.Fire || c.Kind == DamageKind.Crystal || c.Kind == DamageKind.Gaze || (c.Kind == DamageKind.Trait && c.Source != null && c.Source.Kind == EnemyKind.Bomber)))
                        s.EnvironmentKills++;
                    else if (c != null && c.Source != null && !c.Source.IsPlayer && c.Source != e.Unit) s.FriendlyKills++;
                }
            }
            return s;
        }

        /// <summary>コピーを使った倒し方（無ければ null）：敵が写した攻撃・写し砲・鏡晶の爆発・写した移動でトゲ。</summary>
        public static string CopyKillHow(DamageCause c)
        {
            if (c == null) return null;
            if (c.Kind == DamageKind.Crystal) return "鏡晶";
            if (c.Kind == DamageKind.Clock && c.Source != null && !c.Source.IsPlayer) return "大技の逆利用"; // 【Combat V4】
            if (c.Kind == DamageKind.Resonance) return "共鳴"; // 【戦闘中心 v3】
            if (c.Source != null && c.Source.Kind == EnemyKind.Cannon) return "写し砲";
            if (c.Kind == DamageKind.Spike && c.ByCopy) return "写しの移動でトゲ";
            if (c.ByCopy && c.Source != null && !c.Source.IsPlayer) return "写し攻撃";
            if (c.Kind == DamageKind.Trait && c.Source != null && !c.Source.IsPlayer) return "兵種の誤射";
            return null;
        }
    }

    /// <summary>報酬の候補1枚（reason が空でなければ「写し取り」枠）。</summary>
    public class RewardCard
    {
        public CommandData Card; public string Reason;
        public bool IsCopySlot => !string.IsNullOrEmpty(Reason);
        /// <summary>【Combat V4】戦術札（緑）か。戦術札のデッキへ入る。</summary>
        public bool IsTactic => Card != null && Card.type == CommandType.Tactic;
    }

    /// <summary>【検証中】旅立ちの選択の種類（2026-10-05）。</summary>
    public enum BoonKind { Card, Imprint, Shards, Supplies, Trim }

    /// <summary>旅立ちの選択肢1つ。</summary>
    public class BoonOption
    {
        public BoonKind Kind; public CommandData Card; public ImprintId Imprint; public string Title, Text;
    }

    public class ShopCard
    {
        public CommandData Card; public int Price; public bool Sold;
    }

    public class ShopItem
    {
        public ItemId Id; public int Price; public bool Sold;
    }

    /// <summary>
    /// 【検証中】ACT1 のラン（データだけ。MonoBehaviour にしない＝テストしやすい）。
    /// 持ち越す物：HP・最大 HP・デッキ・ルーン片・アイテム（2つまで）・刻印・役職・階・乱数の種。
    /// 進行：扉を選ぶ → 部屋（戦闘／休息／商人／特殊）→ 戦闘なら報酬 → 次の階の扉 … → ボス → ACT1 CLEAR。
    /// </summary>
    public class ActRun
    {
        public RoleDef Role { get; }
        public int Seed { get; }
        public Random Rng { get; }
        public int MaxHp { get; private set; }
        public int Hp { get; private set; }
        public int Shards { get; private set; }
        public List<CommandData> Deck { get; } = new List<CommandData>();
        public List<ItemId> Items { get; } = new List<ItemId>();
        public List<ImprintId> Imprints { get; } = new List<ImprintId>();
        public List<List<RoomDef>> Map { get; }
        /// <summary>次に扉を選ぶ階（0 始まり）。</summary>
        public int Floor { get; private set; }
        public RoomDef Current { get; private set; }
        public bool Failed { get; private set; }
        public bool Cleared { get; private set; }
        public int RemovePrice { get; private set; } = ActContent.RemovePriceBase;
        public readonly List<string> History = new List<string>();
        /// <summary>【検証中】プレイテストの記録（選んだ扉・避けた扉・報酬・商人・アイテム・戦闘の内訳）。終わったら Markdown に書き出す。</summary>
        public readonly List<string> Journal = new List<string>();

        public void Note(string line) => Journal.Add(line);

        // ───── 【完成版・2026-10-20】鍛える・解放・挑戦・ボスの再挑戦 ─────
        /// <summary>鍛えた札の種類（アセット名）。あなたが使うときだけダメージ +2（敵のまねは元のまま）。</summary>
        public HashSet<string> Forged { get; } = new HashSet<string>();
        /// <summary>解放済みの札（FinalMeta）。V4Content.LockedCards のうち、これに入っている札だけが抽選に出る。</summary>
        public HashSet<string> UnlockedCards { get; } = new HashSet<string>();
        /// <summary>挑戦の段階（0＝ふつう・最大 V4Content.MaxChallenge）。</summary>
        public int Challenge { get; set; }
        /// <summary>ボスに負けたとき、ボスの直前から1回だけやり直した（反響深度3 では使えない）。</summary>
        public bool BossRetryUsed { get; private set; }
        /// <summary>ボスの部屋に入ったときの HP（再挑戦はこの HP から）。</summary>
        public int BossEntryHp { get; private set; }
        /// <summary>【RC7・設計レビュー】やり直し（1ランに1回）は中ボスとボスで共有（本人は6階の中ボスでランが終わった。戦闘は弱くせず、1回だけ戻れる。6階で使えばボスでは無い）。</summary>
        /// <summary>【RC12】やり直しの範囲：0＝無し・1＝ボスだけ・2＝中ボスとボス（RC7〜RC11）。</summary>
        public static int RetryMode = 2;
        public bool CanRetryBoss => V4Run && Failed && Current != null && (RetryMode >= 2 ? (Current.Kind == RoomKind.Boss || Current.IsMidBoss) : RetryMode == 1 && Current.Kind == RoomKind.Boss) && !BossRetryUsed && Challenge < 3;
        /// <summary>【RC5・最終ゲート】戦闘の途中（2ターン目以降）から「続きから」で再開した回数。中断はやり直しになるので、結果と感想用のコピーに出す。</summary>
        public int MidBattleResumes { get; set; }

        /// <summary>【RC5・最終ゲート】ボスの部屋を途中から再開した＝やり直し：ボスの再挑戦（1ランに1回）を使ったことにする。使えたら true。</summary>
        public bool UseBossRetryForResume()
        {
            if (!V4Run || Current == null || Current.Kind != RoomKind.Boss || BossRetryUsed || Challenge >= 3) return false;
            BossRetryUsed = true;
            History.Add("　ボスに再挑戦（途中から再開した）");
            Note("- ボスの部屋を途中から再開：ボスの再挑戦を使った");
            return true;
        }

        /// <summary>ボスに負けた後、ボスの直前（入ったときの HP）からもう一度（1ランに1回）。</summary>
        public bool RetryBoss()
        {
            if (!CanRetryBoss) return false;
            Failed = false;
            BossRetryUsed = true;
            Hp = Math.Max(1, BossEntryHp);
            History.Add($"　{Current.Name}に再挑戦（1回だけ）"); // 【RC7】中ボスでも
            Note($"- {Current.Name}に再挑戦（HP {Hp}）");
            return true;
        }

        /// <summary>【RC2】ボスの再挑戦をやめた：負けに戻す（再挑戦は使わなかったことにする）。</summary>
        public void GiveUpBossRetry(int hpAfterLoss)
        {
            if (!V4Run || Current == null || (Current.Kind != RoomKind.Boss && !Current.IsMidBoss) || !BossRetryUsed || Failed) return;
            Failed = true;
            BossRetryUsed = false;
            Hp = Math.Max(0, hpAfterLoss);
            int hi = History.LastIndexOf($"　{Current.Name}に再挑戦（1回だけ）");
            if (hi >= 0) History.RemoveAt(hi);
            History.Add("　再挑戦はしなかった");
            Note("- 再挑戦はしなかった");
        }

        /// <summary>
        /// 【RC4】はじめてのランの2階の扉（大技なしで、自分で動く敵が1種類）。
        /// 最初は槍の回廊・癒し手の陣にしたが、大技なしの槍の回廊は槍兵2体が動かない砦になり、ボットが 40 ターンで倒しきれない戦闘が出た（Learner 6/100 ラン）→ 測って決める（pzsearch の PZ_LESSON2）。
        /// </summary>
        public static string[] LessonFloor2Rooms = { "v4_n1", "v4_n6" };

        /// <summary>【完成版】はじめてのラン：1階の扉に、この部屋を必ず入れる（教える順番：まず同士討ちの基本）。</summary>
        public void ForceFirstDoor(string roomId)
        {
            if (V4Run) FirstLesson = true; // 【RC2】はじめてのランの1階は、移動・攻撃・まねだけ（LessonBattle）
            if (!V4Run || Map.Count == 0) return;
            var room = V4Content.Rooms.FirstOrDefault(r => r.Id == roomId);
            // 【RC3・レビュー】1階は「鏡の歩兵」の扉1つだけ（前はもう1つの扉も「敵は まねるだけ」になり、その部屋の説明（八方斬りを槍兵へ…）と合わなかった）
            if (room != null) Map[0] = new List<RoomDef> { room };
            // 【RC4・敵対的レビュー＋測定】はじめてのランの2階は LessonFloor2Rooms（前衛の二人・癒し手の陣：自分で動く敵＝離れた剣兵がいる。
            //   「盾と槍の挟撃」は自分で動く敵が0体・「槍の回廊」は大技なしだと動かない槍兵2体の砦になった）
            if (Map.Count > 1)
            {
                var lesson2 = LessonFloor2Rooms.Select(id => V4Content.Rooms.FirstOrDefault(r => r.Id == id)).Where(r => r != null).ToList();
                var moved = Map[1].Where(r => !lesson2.Contains(r)).ToList();
                if (lesson2.Count == 2)
                {
                    // 抜いた部屋（盾と槍の挟撃）は、使われていない後半の通常戦の扉と入れ替える（同じ部屋が1ランに2回出ないように）
                    for (int f = 2; f < Map.Count && moved.Count > 0; f++)
                        for (int k = 0; k < Map[f].Count && moved.Count > 0; k++)
                            if (lesson2.Contains(Map[f][k])) { Map[f][k] = moved[0]; moved.RemoveAt(0); }
                    Map[1] = lesson2;
                }
            }
            // 【RC3】はじめてのランは、5階の通常戦をワープの部屋（追跡の群れ）にする（ワープを必ず一度は見る。2回目からは半々）
            if (Map.Count > 4)
            {
                int i = Map[4].FindIndex(r => r.Id == "v4_n3");
                var warp = V4Content.Rooms.FirstOrDefault(r => r.Id == "v4_n4");
                if (i >= 0 && warp != null && !Map.Any(f => f.Contains(warp))) Map[4][i] = warp;
            }
        }

        /// <summary>札の種類を鍛える（休息で）。攻撃の札だけ。同じ種類は1回。【RC6】自分＋。</summary>
        public bool ForgeCard(CommandData c) => UpgradeCard(c, UpgradeKind.Self);

        /// <summary>【RC6】その種類の札の強化（無ければ null）。</summary>
        public UpgradeKind? UpgradeOf(string name)
        {
            if (Forged.Contains(name)) return UpgradeKind.Self;
            if (Forged.Contains(name + "@+")) return UpgradeKind.CopyUp;
            if (Forged.Contains(name + "@-")) return UpgradeKind.CopyDown;
            return null;
        }

        /// <summary>【RC6】強化できる札の種類があるか（休息・商人・中ボスの報酬）。</summary>
        public bool AnyUpgradable => Deck.Any(d => UpgradeOf(d.name) == null && V4Content.CanUpgradeAny(d));

        /// <summary>【RC6】札の種類を強化する（自分＋・まね＋・まね－のどれか1つ。同じ種類は1回）。休息・商人・中ボスの報酬。</summary>
        public bool UpgradeCard(CommandData c, UpgradeKind kind)
        {
            if (c == null || !V4Content.CanUpgrade(c, kind) || UpgradeOf(c.name) != null || !Deck.Contains(c)) return false;
            Forged.Add(DeckState.UpgradeKey(c.name, kind));
            History.Add($"　強化：{c.displayName}［{V4Content.UpgradeName(kind)}］"); // 【通常リリース版】説明は強化の画面にある（結果の「このランの流れ」で切れていた）
            Note($"- 強化：{c.displayName}［{V4Content.UpgradeName(kind)}］");
            return true;
        }

        /// <summary>【解析専用】HP を決めた値にする（回復の比較で「少し削られた状態」から始めるため）。ゲームでは使わない。</summary>
        public void SetHpForStudy(int hp) => Hp = Math.Max(1, Math.Min(MaxHp, hp));
        /// <summary>【RC12・解析だけ】最大 HP を変える（役職の比べ：pzsearch の PZ_ROLEPATCH=hp:-3）。</summary>
        public void AdjustMaxHpForStudy(int d) { MaxHp = Math.Max(1, MaxHp + d); Hp = Math.Max(1, Math.Min(MaxHp, Hp + Math.Max(0, d))); }
        /// <summary>テスト・解析用。</summary>
        public void SetShardsForStudy(int shards) => Shards = Math.Max(0, shards);

        /// <summary>【検証中】中断セーブから戻す（ActSave.TryRestore だけが使う）。地図は種から作り直すので同じ。乱数は階の数だけ進めてずらす。</summary>
        public void ApplySave(ActSaveData d, List<CommandData> deck, List<ItemId> items, List<ImprintId> imprints)
        {
            Floor = d.floor;
            MaxHp = d.maxHp;
            Hp = d.hp;
            Shards = d.shards;
            RemovePrice = d.removePrice;
            Deck.Clear(); Deck.AddRange(deck);
            Items.Clear(); Items.AddRange(items);
            Imprints.Clear(); Imprints.AddRange(imprints);
            History.Clear(); History.AddRange(d.history ?? new List<string>());
            Journal.Clear(); Journal.AddRange(d.journal ?? new List<string>());
            BattlesWon = d.battlesWon; TotalTurns = d.totalTurns; TotalDamage = d.totalDamage; ShardsEarned = d.shardsEarned; MissionsDone = d.missionsDone;
            ParBonuses = d.parBonuses;
            NoDamageWins = d.noDamageWins;
            BoonTaken = true; // セーブは扉の画面でだけ作る＝旅立ちの選択は済んでいる
            UsedCards.Clear();
            for (int i = 0; i < Math.Min(d.usedKeys?.Count ?? 0, d.usedValues?.Count ?? 0); i++) UsedCards[d.usedKeys[i]] = d.usedValues[i];
            DamageBySource.Clear();
            for (int i = 0; i < Math.Min(d.damageKeys?.Count ?? 0, d.damageValues?.Count ?? 0); i++) DamageBySource[d.damageKeys[i]] = d.damageValues[i];
            HpFlow.Clear(); HpFlow.AddRange(d.hpFlow ?? new List<int>());
            BigHitsTotal = Math.Max(0, d.bigHits); TacticUsesTotal = Math.Max(0, d.tacticUses);
            PlaySeconds = Math.Max(0, d.playSeconds); TrialsCleared = d.trialsCleared; TrialsFailed = d.trialsFailed; CopyUsesTotal = d.copyUses; CopyKillsTotal = d.copyKills;
            EnemyDamageTotal = Math.Max(0, d.enemyDamage); EnemyDamageByOthersTotal = Math.Max(0, d.enemyDamageByOthers); // 【通常リリース版】反響ランク
            // 【完成版】鍛えた札・解放済みの札・挑戦・ボスの再挑戦
            Forged.Clear(); foreach (var f in d.forged ?? new List<string>()) if (Deck.Any(c => c.name == DeckState.UpgradeBaseName(f))) Forged.Add(f); // 【RC6】「名前@+」「名前@-」も
            UnlockedCards.Clear(); foreach (var u in d.unlockedCards ?? new List<string>()) UnlockedCards.Add(u);
            Challenge = Math.Max(0, Math.Min(V4Content.MaxChallenge, d.challenge));
            BossRetryUsed = d.bossRetryUsed;
            BossEntryHp = Math.Max(0, d.bossEntryHp);
            MidBattleResumes = Math.Max(0, d.midBattleResumes); // 【RC5・最終ゲート】
            for (int i = 0; i < d.floor * 17; i++) Rng.Next();
            Journal.Add($"（{d.floor + 1}階から再開）");
        }
        public int BattlesWon, TotalTurns, TotalDamage, ShardsEarned, MissionsDone;
        /// <summary>【検証中】速さの報酬を取った回数。</summary>
        public int ParBonuses;
        /// <summary>【RC11】無傷で勝った戦闘の数（ランの結果の「★ 無傷の勝利」）。</summary>
        public int NoDamageWins;
        /// <summary>【検証中】このランで使ったカード（表示名 → 回数）。結果画面の「よく使ったカード」「初めて使ったカード」。</summary>
        public readonly Dictionary<string, int> UsedCards = new Dictionary<string, int>();
        /// <summary>【検証中】このランで受けたダメージの出どころ（敵のコピー／兵種の行動／環境）。結果画面の「主な原因」。</summary>
        public readonly Dictionary<string, int> DamageBySource = new Dictionary<string, int>();
        /// <summary>【検証中】旅立ちの選択を済ませたか。</summary>
        public bool BoonTaken;

        public void AddUsedCards(IEnumerable<string> names)
        {
            foreach (var n in names) UsedCards[n] = (UsedCards.TryGetValue(n, out var v) ? v : 0) + 1;
        }

        public void AddDamage(string source, int amount)
        {
            if (amount <= 0) return;
            DamageBySource[source] = (DamageBySource.TryGetValue(source, out var v) ? v : 0) + amount;
        }

        /// <summary>【検証中】部屋に入ったときの HP（結果画面の「HP の流れ」）。最後は終わったときの HP。</summary>
        public readonly List<int> HpFlow = new List<int>();
        /// <summary>【検証中】負けたときの最後の一撃（「弓兵の矢 -3」など）。セーブしない（ランの終わりだけで使う）。</summary>
        public string LastHit;

        // ───── 旅立ちの選択（2026-10-05 本人「1回やったらもういい」→ 毎ラン、始まりの方向が変わる）─────

        public List<BoonOption> BoonOptions { get; } = new List<BoonOption>();

        /// <summary>5つから3つ（種で決まる。地図・報酬とは別の乱数なので、選んでも地図はずれない）。どれも小さめ（敵は弱くしない）。</summary>
        public void RollBoons()
        {
            BoonOptions.Clear();
            if (BoonTaken) return;
            var r = new Random(Seed ^ 0x2B0F3);
            var all = new List<BoonOption>();
            var cards = Pool.Where(c => c.Rarity == Rarity.Uncommon && Deck.All(d => d.name != c.Asset)).ToList(); // 解析でレアは外した（強すぎ）
            if (cards.Count > 0)
            {
                var pick = cards[r.Next(cards.Count)];
                var cd = card(pick.Asset);
                if (cd != null) all.Add(new BoonOption { Kind = BoonKind.Card, Card = cd, Title = $"技を1枚：{cd.displayName}", Text = $"「{cd.displayName}」（{pick.RarityMark}）をデッキに加える。" });
            }
            if (V4Run)
            {
                // 【Combat V4】戦術札の選択肢（珍しい札）。刻印は出さない（第四命令などはコピーの数を増やして V4 の核を崩す）
                var tc = V4Content.FinalTacticPool.Where(c => c.Rarity == Rarity.Uncommon && TacticCards.All(d => d.name != c.Asset) && card(c.Asset) != null).ToList();
                if (tc.Count > 0)
                {
                    var tp = tc[r.Next(tc.Count)];
                    var tcd = card(tp.Asset);
                    all.Add(new BoonOption { Kind = BoonKind.Card, Card = tcd, Title = $"戦術札を1枚：{tcd.displayName}", Text = $"戦術札「{tcd.displayName}」（{tp.RarityMark}）を戦術札のデッキに加える。{TacticRules.Short(tcd.tactic)}。" });
                }
            }
            var imps = V4Run ? V4Content.ImprintPool.Where(i => !Has(i)).ToList() : ActContent.RewardImprints.Where(i => !Has(i)).ToList(); // 【RC7】完成版の刻印も旅立ちに
            if (imps.Count > 0)
            {
                var id = imps[r.Next(imps.Count)];
                var def = ActContent.Imprint(id);
                int cost = V4Run ? V4Content.BoonImprintHpCost : ActContent.BoonImprintHpCost;
                all.Add(new BoonOption { Kind = BoonKind.Imprint, Imprint = id, Title = $"刻印：{def.Name}（最大 HP -{cost}）", Text = $"{def.Description}\nランの間ずっと効く。代わりに最大 HP が {cost} 減る。" });
            }
            all.Add(new BoonOption { Kind = BoonKind.Shards, Title = $"ルーン片 +{ActContent.BoonShards}", Text = "最初の商人で、珍しい札か回復薬＋削除に手が届く。" });
            if (Items.Count == 0) all.Add(new BoonOption { Kind = BoonKind.Supplies, Title = "回復薬と回収の鉤", Text = "アイテム2つ：回復薬（HP 6 回復）と回収の鉤（捨て札から1枚を手札へ）。" });
            else if (V4Run && Items.Count < ActContent.ItemSlots) all.Add(new BoonOption { Kind = BoonKind.Supplies, Title = "回復薬とルーン片 +10", Text = "アイテム：回復薬（HP 6 回復）。ルーン片も 10。（アイテムの枠は2つ：はじめの転移石と合わせて満杯）" }); // 【RC4】転移石で枠が1つ埋まった
            all.Add(new BoonOption { Kind = BoonKind.Trim, Title = $"1枚削除＋ルーン片 +{ActContent.BoonTrimShards}", Text = "最初のデッキから、いらない1枚を選んで削除する。" });
            if (V4Run && all.Any(o => o.Kind == BoonKind.Imprint))
            {
                // 【RC7・設計レビュー】完成版は刻印を必ず1つ並べる（刻印の多くが6階の中ボスで来て、ボス1戦にしか効いていなかった）
                var imp = all.First(o => o.Kind == BoonKind.Imprint);
                BoonOptions.Add(imp);
                foreach (var o in all.Where(o => o != imp).OrderBy(_ => r.Next()).Take(2)) BoonOptions.Add(o);
                var shuffled = BoonOptions.OrderBy(_ => r.Next()).ToList();
                BoonOptions.Clear(); BoonOptions.AddRange(shuffled);
            }
            else
            foreach (var o in all.OrderBy(_ => r.Next()).Take(3)) BoonOptions.Add(o);
        }

        /// <summary>旅立ちの選択を受け取る（削除は removeIndex の1枚）。</summary>
        public bool TakeBoon(BoonOption b, int removeIndex = -1)
        {
            if (BoonTaken || b == null || !BoonOptions.Contains(b)) return false;
            string removed = null;
            switch (b.Kind)
            {
                case BoonKind.Card: if (b.Card.type == CommandType.Tactic) TacticCards.Add(b.Card); else Deck.Add(b.Card); break;
                case BoonKind.Imprint:
                    if (Has(b.Imprint)) return false;
                    Imprints.Add(b.Imprint);
                    MaxHp -= V4Run ? V4Content.BoonImprintHpCost : ActContent.BoonImprintHpCost; // 【RC7】
                    Hp = Math.Min(Hp, MaxHp);
                    break;
                case BoonKind.Shards: AddShards(ActContent.BoonShards); break;
                case BoonKind.Supplies:
                    if (Items.Count == 0) { AddItem(ItemId.Potion); AddItem(ItemId.Hook); }
                    else { AddItem(ItemId.Potion); AddShards(10); } // 【RC4】
                    break;
                case BoonKind.Trim:
                    if (Deck.Count <= MinDeck || removeIndex < 0 || removeIndex >= Deck.Count) return false;
                    removed = Deck[removeIndex].displayName;
                    Deck.RemoveAt(removeIndex);
                    AddShards(ActContent.BoonTrimShards);
                    break;
            }
            Note($"- 旅立ちの選択：{b.Title}{(removed != null ? $"（{removed} を削除）" : "")}　候補：{string.Join("・", BoonOptions.Select(o => o.Title))}");
            History.Add($"　旅立ち：{b.Title}{(removed != null ? $"（{removed}）" : "")}");
            BoonTaken = true;
            BoonOptions.Clear();
            return true;
        }

        /// <summary>
        /// 【検証中】結果画面の「次に試すこと」（決まった規則で最大2行）。被ダメの出どころ・HP・ルーン片の残りから選ぶ。
        /// 2026-10-05 本人「負けても原因が分からない」系の監査（R1）。
        /// </summary>
        public List<string> ResultTips()
        {
            var tips = new List<string>();
            int Get(string k) => DamageBySource.TryGetValue(k, out var v) ? v : 0;
            int total = DamageBySource.Values.Sum();
            int reflect = Get("反射"), copy = Get("まね"), env = Get("トゲ") + Get("炎");
            int trait = Get("剣兵の剣") + Get("槍兵の槍") + Get("爆発") + Get("弓兵の矢") + Get("騎士の大技") + Get("兵種の技") + Get("敵の攻撃");
            if (Cleared) tips.Add(V4Run ? "次は別の役職で。被ダメを減らすなら、まねで敵どうしを斬らせ、大技の範囲へ敵を送る" : "次は別の役職か、同じ地図で被ダメを減らす（旅立ちの選択も変えてみる）");
            // 【RC5・負けた理由が分かる】倒れた部屋のコツを先に（その部屋で何をすればよかったか）。最後の一撃は結果画面の左に出る
            if (V4Run && !Cleared && Current != null && !string.IsNullOrEmpty(Current.CopyTip))
                tips.Add($"{Current.Name}のコツ：{(LessonNoClocks && !string.IsNullOrEmpty(Current.LessonTip) ? Current.LessonTip : Current.CopyTip)}");
            int clock = Get("大技（紫）");
            if (V4Run && total > 0 && clock * 3 >= total) tips.Add($"被ダメ {total} のうち大技が {clock}。敵の★の数字が大技までのターン。撃つターンは紫のマスに出る：紫の外へ・まねで敵を紫へ送る");
            if (reflect >= 4) tips.Add($"反射で {reflect}。騎士・鏡兵は攻撃を逆向きに返す：斬ったら、次の命令で横へずれる");
            if (total > 0 && copy * 2 >= total) tips.Add($"被ダメ {total} のうちまねが {copy}。最後の命令の向きを、敵のいない方へ");
            else if (total > 0 && trait * 2 >= total) tips.Add(V4Run ? $"敵の攻撃（赤）で {trait}。赤はターンの初めに決まる：赤の外で命令を終える（あなたのマスに来る攻撃は赤い太い枠）" : $"兵種の技で {trait}。赤い範囲は敵が動く前：まねで動いた先も読む");
            if (env >= 3) tips.Add("トゲ・炎はあなたにも効く。押し出し・引き寄せで、敵の方を乗せる");
            if (!Cleared && Current != null && (Current.Kind == RoomKind.Danger || Current.Kind == RoomKind.Boss) && HpFlow.Count > 0 && HpFlow[HpFlow.Count - 1] * 10 <= MaxHp * 4)
                tips.Add($"HP {HpFlow[HpFlow.Count - 1]}/{MaxHp} で{(Current.Kind == RoomKind.Boss ? "ボス" : V4Run ? "中ボス" : "危険戦")}へ入った。休息（+{(V4Run ? RestHealAmount : ActContent.RestHeal)}）・回復薬・通常戦の扉を選べた");
            if (Shards >= 45 && Map.Take(Floor).Any(f => f.Any(r => r.Kind == RoomKind.Shop))) tips.Add($"ルーン片が {Shards} 残った。商人の回復薬・削除・{(V4Run ? "札" : "刻印")}に使える"); // 【RC5・最終ゲート】完成版の商人は刻印を売らない
            if (ParBonuses == 0 && BattlesWon >= 2) tips.Add($"速さの報酬（目安のターン以内）は +{ActContent.ParBonus}。急げる部屋だけ狙う");
            if (tips.Count == 0) tips.Add(V4Run ? "空振りの斬撃で敵どうしを斬らせる・同じ突進で敵だけを大技の範囲へ。まねを使うほど被ダメが減る" : "同じ地図で再挑戦すると、扉と報酬の選び方を変えて比べられる"); // 【完成版】結果画面に「同じ地図」のボタンは無い
            return tips.Take(2).ToList();
        }

        public void SkipBoon()
        {
            if (BoonTaken) return;
            Note($"- 旅立ちの選択：取らなかった　候補：{string.Join("・", BoonOptions.Select(o => o.Title))}");
            BoonTaken = true;
            BoonOptions.Clear();
        }

        // 部屋ごとの中身
        public List<RewardCard> RewardCards { get; } = new List<RewardCard>();
        public int RewardPicksLeft { get; private set; }
        public List<ImprintId> RewardImprints { get; } = new List<ImprintId>();
        public List<ShopCard> ShopCards { get; } = new List<ShopCard>();
        public List<ShopItem> ShopItems { get; } = new List<ShopItem>();
        public ImprintId? ShopImprint { get; private set; }
        public bool ShopImprintSold { get; private set; }
        public bool ShopRemoveUsed { get; private set; }
        public List<CommandData> EventCards { get; } = new List<CommandData>();
        public string LastMissionResult { get; private set; }
        public string LastParResult { get; private set; }
        /// <summary>【検証中】直前の勝利のルーン片の内訳（報酬画面に出す）。</summary>
        public string LastRewardBreakdown { get; private set; }
        /// <summary>【RC6】直前の戦闘で増えたルーン片（報酬の画面の数え上げ）。</summary>
        public int LastRewardShards { get; private set; }

        readonly Func<string, CommandData> card;

        /// <summary>【検証中・Copy Core V3】コピー中心のミニ ACT1（地図・抽選の札が違う）。</summary>
        public bool CopyCentric { get; }
        /// <summary>【Puzzle v2】パズル中心 ACT1 v2（コピー中心の札・役職・刻写＋パズルの地図・手札を回す札）。</summary>
        public bool PuzzleRun { get; }
        /// <summary>【戦闘中心 v3】戦闘中心 ACT1 v3（戦闘が主役・コピーは強い武器。抽選の札と刻写はコピー中心と同じ、役職は3つ、地図は v3）。</summary>
        public bool CombatRun { get; }
        /// <summary>【Combat V4】Combat V4 / External Playtest RC1（敵の固有行動・カウント大技・戦術札・中ボス）。戦闘中心 v3 のランの仕組み（部屋ごとの乱数・途中のセーブ）を使う。</summary>
        public bool V4Run { get; }
        /// <summary>【Combat V4】戦術札のデッキ（命令札のデッキとは別）。</summary>
        public List<CommandData> TacticCards { get; } = new List<CommandData>();
        /// <summary>【Combat V4】報酬の追加購入（1枚目は無料・2枚目からルーン片）を、この報酬であと何回できるか。</summary>
        public int ExtraBuysLeft { get; private set; }
        /// <summary>【Combat V4】この戦闘のあとの回復・使った大技などの一言（報酬の画面に出す）。</summary>
        public string LastHealText { get; private set; }
        /// <summary>【Combat V4】大技を敵に当てた回数の合計・戦術札を使った回数の合計（結果画面・記録）。</summary>
        public int BigHitsTotal, TacticUsesTotal;
        /// <summary>【戦闘中心 v3】試練の成功・失敗の数と、意味のあるコピー利用・まねキルの合計（結果画面・記録）。</summary>
        public int TrialsCleared, TrialsFailed, CopyUsesTotal, CopyKillsTotal;
        /// <summary>【通常リリース版・反響ランク】このランで敵が受けたダメージの合計と、あなたの直接攻撃でない分。</summary>
        public int EnemyDamageTotal, EnemyDamageByOthersTotal;
        /// <summary>【通常リリース版・反響ランク】敵へのダメージのうち、敵自身（まね・敵の攻撃・大技）と仕掛けが与えた割合（%）。</summary>
        public int EchoSharePct => EnemyDamageTotal > 0 ? (int)Math.Round(100.0 * EnemyDamageByOthersTotal / EnemyDamageTotal) : 0;
        /// <summary>【通常リリース版・反響ランク】S・A・B・C（クリアしたランだけ。目安は V4Content.EchoRankOf）。</summary>
        public string EchoRank => Cleared ? V4Content.EchoRankOf(EchoSharePct, TotalDamage) : null; // 【通常リリース版・第2巡のレビュー「雑なボットほど S が多い」】割合と受けたダメージの低い方
        /// <summary>【戦闘中心 v3】遊んだ時間（秒。ActRunManager が足す。セーブにも残る）。</summary>
        public double PlaySeconds;
        /// <summary>【戦闘中心 v3】部屋ごとの乱数（種・階・部屋で決まる）：中断して続きから入り直しても、山札の順番・報酬・商品が同じ（引き直しの悪用を防ぐ）。</summary>
        System.Random roomRng;
        System.Random R => CombatRun && roomRng != null ? roomRng : Rng;

        /// <summary>種・階・部屋 Id から決まる数（文字列の GetHashCode は実行ごとに変わることがあるので使わない）。</summary>
        public static int StableSeed(int seed, int floor, string id)
        {
            unchecked
            {
                int h = seed * 7919 + floor * 104729 + 17;
                foreach (char c in id ?? "") h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }
        /// <summary>
        /// 削除できるデッキの下限（この枚数以下なら削除できない）。【Puzzle v2・研究】パズル中心は 10（8枚以下だと毎ターン同じ手がそろい、パズルが同じ手の繰り返しになる）。今の ACT1 は 5。
        /// </summary>
        public int MinDeck => PuzzleRun ? 10 : CombatRun ? 8 : 5; // 【戦闘中心 v3】8（削り切ると毎ターン同じ手）
        /// <summary>抽選に出す札（コピー中心のランはコピーを止める札を出さない）。</summary>
        IEnumerable<CardInfo> Pool => V4Run ? V4CommandPool : CombatRun ? ActContent.CombatRunPool : PuzzleRun ? ActContent.PuzzleRunPool : CopyCentric ? ActContent.CopyRunPool : ActContent.RandomPool;

        /// <summary>【Combat V4】命令札の抽選：手札を回す札（引く・号令・先読みなど）は戦術札のデッキへ移したので出さない。盤面で働く札だけ。</summary>
        IEnumerable<CardInfo> V4CommandPool => V4Content.FinalCommandPool.Where(c => card(c.Asset) != null && (!V4Content.LockedCards.Contains(c.Asset) || UnlockedCards.Contains(c.Asset))); // 【完成版】

        public ActRun(RoleDef role, int seed, Func<string, CommandData> cardLookup, bool copyCentric = false, bool puzzle = false, bool combat = false, bool v4 = false, int mapRev = V4Content.MapRev)
        {
            PuzzleRun = puzzle && !combat;
            V4Run = v4;
            if (v4) combat = true; // 【Combat V4】戦闘中心のランの仕組みの上に作る
            CombatRun = combat;
            if (puzzle || combat) copyCentric = true; // パズル中心・戦闘中心はコピー中心の札・刻写を使う
            CopyCentric = copyCentric;
            if (v4 && role != null) role = V4Content.Role(role); // 【Combat V4】役職は3つ・小さな能力
            else if (combat && role != null) role = ActContent.CombatRole(role); // 【戦闘中心 v3】役職は元の能力（先兵の +1 など）＋v3 の一言
            else if (copyCentric && role != null) role = ActContent.CopyRunRole(role); // 【Copy Core V3】コピー中心のランの役職
            Role = role;
            Seed = seed;
            Rng = new Random(seed);
            card = cardLookup;
            MaxHp = (v4 ? V4Content.StartHp : ActContent.StartHp) + role.HpDelta; // 【RC12】完成版は 17
            Hp = MaxHp;
            var start = ActContent.StartDeck.ToList();
            foreach (var r in role.RemoveCards ?? new string[0]) start.Remove(r); // 入れ替え（1枚ずつ）
            foreach (var n in start.Concat(role.AddCards)) { var c = card(n); if (c != null) Deck.Add(c); }
            Imprints.AddRange(role.Imprints);
            Items.AddRange(role.Items);
            MapRev = mapRev;
            Map = v4 ? V4Content.BuildMap(Rng, mapRev) : combat ? ActContent.BuildCombatMap(Rng) : puzzle ? ActContent.BuildPuzzleMap(Rng) : copyCentric ? ActContent.BuildCopyMap(Rng) : ActContent.BuildMap(Rng);
            if (v4) Items.Add(ItemId.WarpStone); // 【RC4・本人「追い詰められたときの救済が少ない」】はじめに転移石を1つ（商人でも買える）
            if (v4)
            {
                foreach (var n in V4Content.StartTactics.Concat((role.AddTactics ?? new string[0]).Select(a => "Tactic_" + a))) { var c = card(n); if (c != null) TacticCards.Add(c); }
            }
        }

        public IReadOnlyList<RoomDef> Doors => Floor < Map.Count ? Map[Floor] : new List<RoomDef>();
        public int FloorCount => Map.Count;
        public bool Has(ImprintId id) => Imprints.Contains(id);

        // ───── 刻印・役職が戦闘に与える物 ─────
        public int BonusSlots => Has(ImprintId.FourthCommand) ? 1 : 0;
        public int BaseHand => Role.BaseHand - (V4Run ? V4Content.ChallengeHandCut(Challenge) : 0); // 【RC5・レビュー】反響深度2・3 は手札が減る
        /// <summary>【RC5・コードレビュー】地図の作り方の版（V4Content.MapRev。古いセーブから続けるときだけ古い版）。</summary>
        public int MapRev { get; private set; } = V4Content.MapRev;
        /// <summary>【検証中】2026-10-05：棘導 +2 → +1（遺跡技師が強すぎたため）。</summary>
        public int SpikeFirstBonus => Has(ImprintId.ThornGuide) ? 1 : 0;
        /// <summary>【検証中】観測士（観測眼）：完全予測が見える（2026-10-05：手札8 → 「読み」の役職へ）。</summary>
        public bool FullPrediction => Has(ImprintId.Observer);
        public int AttackBonusEachTurn => Has(ImprintId.Vanguard) ? 1 : 0;

        /// <summary>ラウンドの終わり：次のターンに追加で引く枚数（反響・余白）。</summary>
        public int RoundEndDraw(IReadOnlyList<BattleEvent> roundEvents, int commandsUsed)
        {
            int n = 0;
            if (Has(ImprintId.Echo) && roundEvents.Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Cause != null && e.Cause.ByCopy)) n++;
            if (Has(ImprintId.Margin) && commandsUsed <= 2) n++;
            return n;
        }

        public ActBattleSetup MakeBattleSetup() => MakeBattleSetup(R.Next());

        /// <summary>【RC2】はじめてのラン（ForceFirstDoor）。【RC5】セーブに firstLesson として保存する（前は保存せず、続きから ActRunManager が付け直していた：部屋の途中から再開できなかった）</summary>
        public bool FirstLesson { get; private set; }
        /// <summary>【RC2】はじめてのランの1階の戦闘：戦術札・手札整理を出さない（1階は移動・攻撃・まねだけ：初見のレビュー）。</summary>
        public bool LessonBattle => V4Run && FirstLesson && Floor == 0;
        /// <summary>
        /// 【RC4・本人「初心者にはゲーム全体の進み方が分からない」】はじめてのランの2階：敵の攻撃（赤）と動きは出るが、大技と戦術札（緑）はまだ（3階から）。
        /// 覚える物を階ごとに1つずつ：1階＝まね → 2階＝赤と敵の動き → 3階＝緑の札と大技（中ボスか止まったトゲ）。
        /// </summary>
        public bool LessonFloor2 => V4Run && FirstLesson && Floor == 1;
        /// <summary>
        /// 【RC4】この戦闘は大技なし：はじめてのランの1・2階と、3階の中ボスでない部屋（3階は緑の札。大技は中ボスを選んだ人と5階から）。
        /// 敵対的レビュー（3人）：3階で緑の札・大技・弓兵・おさらいの窓が一度に来ていた。
        /// </summary>
        public bool LessonNoClocks => LessonBattle || LessonFloor2 || (V4Run && FirstLesson && Floor == 2 && Current != null && !Current.IsMidBoss && Current.Kind != RoomKind.Boss);

        /// <summary>【Puzzle v2】種を決めて作る（やり直しは同じ種＝同じ山札の順番。引き直しには使えない）。</summary>
        public ActBattleSetup MakeBattleSetup(int seed) => new ActBattleSetup
        {
            Hp = Hp, MaxHp = MaxHp, Deck = new List<CommandData>(Deck), BonusSlots = BonusSlots, BaseHand = BaseHand,
            SpikeFirstBonus = SpikeFirstBonus, AttackBonusEachTurn = AttackBonusEachTurn, RoundEndDrawBonus = RoundEndDraw, FullPrediction = FullPrediction,
            Rng = new Random(seed), Seed = seed,
            TidyMax = CombatRun && !LessonBattle && !V4Run ? ActContent.TidyMax : 0, // 【通常リリース版・デザイナーと採用担当のレビュー「システム過多・手札をいじる手段が5つ重なる」】完成版（V4）では手札整理を出さない（解析のボットも使っていなかった）
            NoClocks = LessonNoClocks, // 【RC3】はじめてのランの1階は大技も出さない（移動・攻撃・まねだけ）【RC4】2階も（大技は3階から）
            NoTraits = LessonBattle, // 【RC3・本人「固有行動は初戦から出す必要はない」】敵は まねるだけ（攻撃・接近は2階から）
            TacticCards = V4Run && !(LessonBattle || LessonFloor2) ? new List<CommandData>(TacticCards) : new List<CommandData>(), // 【RC4】戦術札は3階から
            TacticPlayBonus = V4Run ? Role.TacticBonus : 0,
            PreviewAhead = V4Run && Role.Preview,
            Forged = new HashSet<string>(Forged),
            Challenge = V4Run ? Challenge : 0,
            CopyEdgeBonus = V4Run && Has(ImprintId.CopyEdge) ? V4Content.ImprintCopyEdge : 0, MirrorGuard = V4Run && Has(ImprintId.MirrorGuard) ? V4Content.ImprintMirrorGuard : 0, ClockHandBonus = V4Run && Has(ImprintId.ClockHand) ? V4Content.ImprintClockHand : 0, TrapBonus = V4Run && Has(ImprintId.Trapper) ? V4Content.ImprintTrap : 0, // 【RC7】刻印
            RedDelta = V4Run ? V4Content.PowerFor(Floor, Current).red : 0, ClockDelta = V4Run ? V4Content.PowerFor(Floor, Current).clock : 0, LastStand = V4Run ? V4Content.LastStandBonus : 0, LastStandDouble = V4Run && V4Content.LastStandDouble, EscalateAfter = V4Run && !LessonBattle ? V4Content.EscalateAfterFor(Current) : 0, // 【RC6】
            Layout = CaptureLayout >= 0 ? CaptureLayout : LessonBattle ? 0 : V4Run && Current != null ? (Current.Id == "v4_i1" ? 1 + (StableSeed(Seed, Floor, Current.Id + "#layout") & 0xffff) % 2 : StableSeed(Seed, Floor, Current.Id + "#layout") & 0xffff) : 0, // 【RC5・レビュー3】鏡の歩兵の配置0（剣兵が隣どうし＝待つだけで斬り合う）は、はじめての戦闘（敵が攻撃しない）だけ。ほかのランは配置1・2（1マス空いている：まねで並べる） // 【RC3】はじめての戦闘は いつも同じ配置（柱で剣兵が分かれる）
        };

        /// <summary>【Puzzle v2・解析】部屋をやり直した回数（ラン全体）。</summary>
        public int Restarts;

        // ───── 【戦闘中心 v3】中断セーブから部屋の途中へ戻る（ActSave だけが使う）。部屋の中身は部屋ごとの乱数で作り直すので同じ ─────

        /// <summary>続きから戻った部屋の段階（"room"／"reward"。ActRunManager が画面を決めたら null に戻す）。</summary>
        public string ResumedPhase { get; set; }
        /// <summary>【レビュー】続きから戻った戦闘で、中断したときのターン（0＝戦闘の前・1＝1ターン目の始め）。ActRunManager が使ったら 0 に戻す。</summary>
        public int ResumedBattleTurn { get; set; }

        public void ResumeRoom(RoomDef room, List<int> shopSold = null, bool imprintSold = false, bool removeUsed = false, ActSaveData stock = null, bool upgradeUsed = false, List<string> restDone = null)
        {
            Current = room;
            RestDone.Clear(); foreach (var k in restDone ?? new List<string>()) if (k == "heal" || k == "upgrade" || k == "remove") RestDone.Add(k); // 【RC6】休息の途中から
            ResumedPhase = "room";
            roomRng = new System.Random(StableSeed(Seed, Floor, room.Id));
            switch (room.Kind)
            {
                case RoomKind.Shop:
                    RollShop();
                    if (stock != null) RestoreShopStock(stock); // 【RC5・最終ゲート】入ったときの品ぞろえ（買った札で候補が変わり、作り直すと引き直しになっていた）
                    foreach (var i in shopSold ?? new List<int>())
                    {
                        if (i >= 100 && i - 100 < ShopItems.Count) ShopItems[i - 100].Sold = true;
                        else if (i >= 0 && i < ShopCards.Count) ShopCards[i].Sold = true;
                    }
                    ShopImprintSold = imprintSold;
                    ShopRemoveUsed = removeUsed;
                    ShopUpgradeUsed = upgradeUsed; // 【RC6】
                    break;
                case RoomKind.Event: RollEvent(); break;
            }
        }

        void RestoreShopStock(ActSaveData d)
        {
            int nc = d.shopCards?.Count ?? 0, ni = d.shopItems?.Count ?? 0;
            if (nc != (d.shopCardPrices?.Count ?? 0) || ni != (d.shopItemPrices?.Count ?? 0)) return; // 形がおかしいときは作り直したまま
            var cards = new List<ShopCard>();
            for (int i = 0; i < nc; i++)
            {
                var c = card(d.shopCards[i]);
                if (c == null) return;
                cards.Add(new ShopCard { Card = c, Price = Math.Max(0, d.shopCardPrices[i]) });
            }
            var items = new List<ShopItem>();
            for (int i = 0; i < ni; i++)
            {
                if (!Enum.TryParse(d.shopItems[i], out ItemId id) || !Enum.IsDefined(typeof(ItemId), id)) return;
                items.Add(new ShopItem { Id = id, Price = Math.Max(0, d.shopItemPrices[i]) });
            }
            ShopCards.Clear(); ShopCards.AddRange(cards);
            ShopItems.Clear(); ShopItems.AddRange(items);
            // 【RC7】刻印も入ったときの物（買うと「持っていない物」が変わり、作り直すと別の刻印が「購入済み」になった）
            if (!string.IsNullOrEmpty(d.shopImprint) && Enum.TryParse(d.shopImprint, out ImprintId sid) && Enum.IsDefined(typeof(ImprintId), sid)) ShopImprint = sid;
            else if (d.shopImprint == "-") ShopImprint = null;
        }

        /// <summary>【テスト専用】階と部屋を決める（その階の部屋の乱数も作る）。</summary>
        /// <summary>【RC3】自動撮影だけ：開始配置の番号を決める（-1＝ふだんどおり種で選ぶ）。</summary>
        public static int CaptureLayout = -1;

        public void SetFloorForTest(int floor, RoomDef room) { Floor = floor; Current = room; roomRng = new System.Random(StableSeed(Seed, floor, room.Id)); }

        public void SetTacticCards(List<CommandData> cards) { TacticCards.Clear(); TacticCards.AddRange(cards); }

        public void ResumeReward(RoomDef room, List<RewardCard> cards, int picks, List<ImprintId> imprints, string breakdown, int extraBuys = -1, int upgrades = 0)
        {
            Current = room;
            ResumedPhase = "reward";
            roomRng = new System.Random(StableSeed(Seed, Floor, room.Id) ^ 0x5A5A);
            RewardCards.Clear(); RewardCards.AddRange(cards);
            RewardPicksLeft = picks;
            ExtraBuysLeft = !V4Run || room.IsMidBoss ? 0 : extraBuys >= 0 ? extraBuys : picks > 0 ? 1 : 0;
            RewardImprints.Clear(); RewardImprints.AddRange(imprints);
            LastRewardBreakdown = string.IsNullOrEmpty(breakdown) ? null : breakdown;
            RewardUpgradesLeft = V4Run && room.IsMidBoss ? Math.Max(0, Math.Min(1, upgrades)) : 0; // 【RC6】中ボスの無料の強化
        }

        // ───── 扉 ─────

        /// <summary>扉を選んで部屋に入る（その階の扉だけ）。</summary>
        public bool Enter(RoomDef room)
        {
            // 【Copy Core V3】コピー中心のラン：4階の2つ目の扉は「2階で選ばなかった部屋」（レビュー：同じ部屋が2回出た）
            if (CopyCentric && !PuzzleRun && !CombatRun && Floor == 1 && Map.Count > 3 && Map[1].Contains(room)) // 【戦闘中心 v3】v3 の地図には無い（でたらめ通しで落ちた）
            {
                var other = Map[1].FirstOrDefault(r => r != room);
                if (other != null) Map[3] = new List<RoomDef> { Map[3].First(r => r.Id == "cc_walk"), other };
            }
            if (Failed || Cleared || !Doors.Contains(room)) return false;
            Current = room;
            if (CombatRun) roomRng = new System.Random(StableSeed(Seed, Floor, room.Id)); // 【戦闘中心 v3】
            HpFlow.Add(Hp);
            if (room.Kind == RoomKind.Boss || room.IsMidBoss) BossEntryHp = Hp; // 【完成版】ボスの再挑戦はこの HP から【RC7】中ボスも
            History.Add($"{Floor + 1}階：{room.Name}");
            var avoided = Doors.Where(r => r != room).Select(r => r.Name);
            Note($"## {Floor + 1}階：{room.Name}（{room.Kind}）　避けた扉：{(avoided.Any() ? string.Join("・", avoided) : "なし")}　HP {Hp}/{MaxHp}・ルーン片 {Shards}");
            RestDone.Clear(); // 【RC6】
            switch (room.Kind)
            {
                case RoomKind.Shop: RollShop(); break;
                case RoomKind.Event: RollEvent(); break;
            }
            return true;
        }

        /// <summary>【Combat V4】何も取らずに進むとルーン片が入る状態か（報酬を1枚も取っていない）。</summary>
        public bool SkipBonusAvailable => V4Run && Current != null && Current.Kind != RoomKind.Rest && Current.Kind != RoomKind.Shop && Current.Kind != RoomKind.Event && RewardCards.Count > 0 && RewardPicksLeft == (Current.IsMidBoss ? 2 : 1) && ExtraBuysLeft == (Current.IsMidBoss ? 0 : 1);

        /// <summary>部屋を出て次の階へ。</summary>
        public void Leave()
        {
            if (RewardCards.Count > 0 && Current != null && (Current.Kind == RoomKind.Battle || Current.Kind == RoomKind.Danger || Current.Kind == RoomKind.Tactical || Current.Kind == RoomKind.Trial) && RewardPicksLeft == (Current.Kind == RoomKind.Danger ? 2 : 1))
                Note($"- 報酬を取らなかった（スキップ）：{string.Join("・", RewardCards.Select(r => r.Card.displayName + (r.IsCopySlot ? "［写］" : "")))}");
            // 【Combat V4】何も取らずに進むと、ルーン片が少し（取らない選択もデッキを薄く保つ戦略）
            if (SkipBonusAvailable && V4Content.SkipRewardShards > 0)
            {
                Shards += V4Content.SkipRewardShards;
                Note($"- 報酬を取らずに進んだ：ルーン片 +{V4Content.SkipRewardShards}");
            }
            RewardCards.Clear();
            RewardImprints.Clear();
            RewardPicksLeft = 0;
            ExtraBuysLeft = 0;
            RewardUpgradesLeft = 0;
            SwapsLeft = 0;
            TacticSwapsLeft = 0;
            Current = null;
            roomRng = null;
            Floor++;
        }

        // ───── 戦闘 ─────

        /// <summary>【戦闘中心 v3】直前の試練が失敗だったか（報酬の画面の見出し）。</summary>
        public bool LastTrialFailed;

        public void WinBattle(BattleStats stats)
        {
            LastTrialFailed = false;
            Hp = Math.Max(1, Math.Min(MaxHp, stats.HpAfter));
            BattlesWon++;
            TotalTurns += stats.Turns;
            if (stats.DamageForRecord == 0 && !stats.Restarted && Current.Kind != RoomKind.Trial) NoDamageWins++; // 【RC11】
            TotalDamage += stats.DamageForRecord;
            // 【検証中】ルーン片の内訳（勝利・撃破・無傷・速さ・ミッション）。報酬画面に「+20 勝利＋6 撃破…」と出す
            int baseShards = ActContent.ShardsFor(Current.Kind);
            int shards = baseShards;
            var parts = new List<string>();
            if (baseShards > 0) parts.Add($"勝利 {baseShards}");
            if (Current.Kind == RoomKind.Trial) TrialsCleared++; // 【戦闘中心 v3】試練：撃破・無傷のボーナスは無し
            CopyUsesTotal += stats.CopyUses;
            CopyKillsTotal += stats.CopyKills.Count;
            EnemyDamageTotal += stats.EnemyDamage; EnemyDamageByOthersTotal += stats.EnemyDamageByOthers; // 【通常リリース版】反響ランク
            if (Current.Kind != RoomKind.Boss && Current.Kind != RoomKind.Trial)
            {
                int kills = Math.Min(stats.Defeated.Count, 4);
                if (kills > 0) { shards += kills * ActContent.KillBonus; parts.Add($"撃破 {kills * ActContent.KillBonus}"); }
                if (stats.DamageForRecord == 0 && !stats.Restarted) { shards += ActContent.NoDamageBonus; parts.Add($"無傷 {ActContent.NoDamageBonus}"); }
            }
            if (stats.ChestsOpened > 0) { shards += stats.ChestsOpened * ActContent.ChestShards; parts.Add($"写し箱 {stats.ChestsOpened * ActContent.ChestShards}"); }
            if (V4Run && Has(ImprintId.Spoils) && Current.Kind != RoomKind.Boss) // 【RC7】刻印「同士討ちの戦利品」
            {
                int k = Math.Min(V4Content.SpoilsMax, stats.EnvironmentKills + stats.FriendlyKills);
                if (k > 0) { shards += k * V4Content.SpoilsShards; parts.Add($"戦利品 {k * V4Content.SpoilsShards}"); }
            }
            // 【Puzzle v2】目標の部屋（敵を倒さずに解いた部屋も、撃破と同じくらい稼げる）
            if (stats.Goal != RoomGoal.Defeat && Current.Kind != RoomKind.Boss) { shards += ActContent.GoalBonus; parts.Add($"目標 {ActContent.GoalBonus}"); }
            LastMissionResult = null;
            if (Current.Mission != null && !stats.Restarted)
            {
                bool done = Current.Mission.Kind switch
                {
                    MissionKind.EnvironmentKill => stats.EnvironmentKills >= Math.Max(1, Current.Mission.Count),
                    MissionKind.FriendlyFire => stats.FriendlyFire > 0,
                    MissionKind.FriendlyKill => stats.FriendlyKills >= Math.Max(1, Current.Mission.Count),
                    MissionKind.NoDamage => stats.DamageForRecord == 0,
                    _ => stats.Turns <= Current.Mission.Turns,
                };
                LastMissionResult = done ? $"任意ミッション達成：{Current.Mission.Text}（+{Current.Mission.Reward}）" : $"任意ミッション：{Current.Mission.Text}（未達成）";
                if (done) { shards += Current.Mission.Reward; MissionsDone++; parts.Add($"ミッション {Current.Mission.Reward}"); }
            }
            LastParResult = null;
            if (Current.ParTurns > 0 && !stats.Restarted && !LessonBattle) // 【RC5・レビュー3】はじめての戦闘（まねるだけ）は速さを測らない（目安は ふつうの鏡の歩兵の物）
            {
                bool fast = stats.Turns <= Current.ParTurns;
                LastParResult = fast ? $"速さの報酬：{Current.ParTurns}ターン以内（+{ActContent.ParBonus}）" : $"速さの報酬：{Current.ParTurns}ターン以内（今回 {stats.Turns}ターン）";
                if (fast) { shards += ActContent.ParBonus; ParBonuses++; parts.Add($"速さ {ActContent.ParBonus}"); }
            }
            Shards += shards;
            ShardsEarned += shards;
            LastRewardBreakdown = parts.Count > 0 ? $"ルーン片 +{shards}（{string.Join("＋", parts)}）　所持 {Shards}" : $"所持 {Shards}";
            LastRewardShards = shards; // 【RC6】報酬の画面で数え上げる
            History.Add(Current.Kind == RoomKind.Trial ? $"　試練：成功（{stats.Turns}T・ルーン片 +{shards}）" : $"　勝利（{stats.Turns}T・被ダメ {stats.DamageForRecord}・ルーン片 +{shards}）");
            if (Current.Kind == RoomKind.Boss) { Cleared = true; return; }
            LastHealText = null;
            if (V4Run)
            {
                // 【Combat V4】自然回復（通常戦・戦術戦 +1・中ボス +2）に、役職の回復。回復しすぎてHP管理が消えないよう小さく
                int heal = (Current.Kind == RoomKind.Danger ? V4Content.PostBattleHeal + 1 : V4Content.PostBattleHeal) + Role.PostHeal; // 【完成版】反響深度2 からは自然回復なし
                int before = Hp;
                Hp = Math.Min(MaxHp, Hp + heal);
                if (Hp > before) { LastHealText = $"戦闘のあとの回復 HP +{Hp - before}"; History.Add($"　回復 +{Hp - before}"); }
                BigHitsTotal += stats.BigHitsOnEnemies;
                TacticUsesTotal += stats.TacticUses;
            }
            RollRewards(stats);
        }

        /// <summary>【戦闘中心 v3】試練の失敗（ターン切れ・あきらめた）：負けではない。HP はそのまま持ち越し、報酬は無し。</summary>
        public void FailTrial(BattleStats stats)
        {
            LastTrialFailed = true;
            Hp = Math.Max(1, Math.Min(MaxHp, stats.HpAfter));
            TrialsFailed++;
            TotalTurns += stats.Turns;
            TotalDamage += stats.DamageForRecord;
            CopyUsesTotal += stats.CopyUses;
            RewardCards.Clear();
            RewardImprints.Clear();
            RewardPicksLeft = 0;
            LastMissionResult = null;
            LastParResult = null;
            LastRewardBreakdown = $"試練は失敗（報酬なし・負けではない）。所持 {Shards}";
            History.Add($"　試練：失敗（{stats.Turns}T）");
            Note($"- 試練：失敗（{stats.Turns}T・HP {Hp}）");
        }

        public void LoseBattle(BattleStats stats)
        {
            TotalTurns += stats.Turns;
            TotalDamage += stats.DamageForRecord;
            Hp = 0;
            Failed = true;
            History.Add($"　敗北（{stats.Turns}T）");
        }

        // ───── カード報酬：3枚＋「写し取り」1枚から1枚（危険戦は5枚から2枚まで＋刻印2択）。スキップできる ─────

        public void RollRewards(BattleStats stats)
        {
            RewardCards.Clear();
            RewardImprints.Clear();
            if (V4Run) { RollRewardsV4(stats); return; }
            bool danger = Current.Kind == RoomKind.Danger && !(PuzzleRun && stats.Restarted); // 【Puzzle v2】やり直した危険戦は追加の報酬なし
            bool trial = Current.Kind == RoomKind.Trial;
            if (!trial) foreach (var copy in CopySlotCards(stats).Take(danger || CopyCentric ? 2 : 1)) RewardCards.Add(copy); // 【戦闘中心 v3】試練は刻写なし（珍しい札の3択）
            int total = danger ? 5 : trial ? 3 : 4;
            var pool = Pool.Where(c => RewardCards.All(r => r.Card.name != c.Asset)).ToList();
            if (trial) pool = pool.Where(c => c.Rarity != Rarity.Common).ToList(); // 試練の報酬は珍しい札から
            while (RewardCards.Count < total && pool.Count > 0)
            {
                var pick = WeightedPick(pool, Current.RewardTags);
                pool.Remove(pick);
                var cd = card(pick.Asset);
                if (cd != null) RewardCards.Add(new RewardCard { Card = cd });
            }
            RewardPicksLeft = danger ? 2 : 1;
            if (danger)
                RewardImprints.AddRange(ActContent.RewardImprints.Where(i => !Has(i)).OrderBy(_ => R.Next()).Take(2));
        }

        /// <summary>【戦闘中心 v3・レビュー】刻写の看板札を持っているときに代わりに出す、同じ系統の札。</summary>
        static readonly Dictionary<string, string[]> EngraveSiblings = new Dictionary<string, string[]>
        {
            { "CopyBlade", new[] { "JointSlash", "Aim", "EchoBlade" } },
            { "Tailwind", new[] { "Footwork", "Recycle" } },
            { "Aim", new[] { "CopyBlade", "BladeDrill" } },
            { "JointSlash", new[] { "CopyBlade", "ResonantStep" } },
            { "EchoBlade", new[] { "BladeDrill", "JointSlash" } },
            { "ResonantStep", new[] { "EchoBlade", "Footwork" } },
        };

        /// <summary>「写し取り」：その戦闘で起きたこと（同士討ち・環境で倒した・倒した兵種）に関係するカード。</summary>
        public List<RewardCard> CopySlotCards(BattleStats stats)
        {
            var list = new List<RewardCard>();
            void Add(string asset, string reason)
            {
                var c = card(asset);
                // 【戦闘中心 v3・レビュー】もう持っている看板札（写し刃など）は、同じ系統の別の札へ（毎戦同じ札が並んでいた）
                if (c != null && CombatRun && Deck.Contains(c) && EngraveSiblings.TryGetValue(asset, out var sibs))
                    c = sibs.Select(card).Where(x => x != null && !Deck.Contains(x) && list.All(r => r.Card != x)).OrderBy(_ => R.Next()).FirstOrDefault() ?? c;
                if (c != null && list.All(r => r.Card != c)) list.Add(new RewardCard { Card = c, Reason = reason });
            }
            void AddOne(string[] options, string reason)
            {
                foreach (var o in options.OrderBy(_ => R.Next()))
                {
                    var c = card(o);
                    if (c != null && list.All(r => r.Card != c)) { list.Add(new RewardCard { Card = c, Reason = reason }); return; }
                }
            }
            // 【検証中・Copy Core V3】写し取り v2（コピー中心のラン）：コピーを使って倒した敵がいれば「刻写」（倒し方と兵種で決まる札）。
            // 普通に倒しただけなら普通の報酬（刻写の枠は出ない）。毎回コピーキルを強制しない
            if (CopyCentric)
            {
                // 【Puzzle v2】刻写 v2：どう解いたか → デッキの育ち方（運び＝追い風・足取り／封印・写し身＝狙い定め・武技整理／同期＝共鳴補給・号令・改／石像＝狙い定め）
                if (PuzzleRun || CombatRun) // 【戦闘中心 v3】戦闘中の同期・石像・鎧割りも刻写へ
                    foreach (var p in stats.CopyPuzzles.OrderBy(x => x))
                    {
                        switch (p)
                        {
                            // 研究（2026-10-06）：一番大きい成功で看板札、2枚目は同じ系統の手を回す札
                            case "運び": Add("Tailwind", "刻写：まねの移動で運んだ"); AddOne(new[] { "Footwork" }, "刻写：運び（2枚目）"); break;
                            case "封印": Add("JointSlash", "刻写：まね攻撃で封印を割った"); AddOne(new[] { "BladeDrill" }, "刻写：砲台（2枚目）"); break;
                            case "写し身": Add("CopyBlade", "刻写：像に主を斬らせた"); break;
                            case "同期": Add("JointSlash", "刻写：あなたとまねで同時に"); AddOne(new[] { "ResonantStep" }, "刻写：同期（2枚目）"); break;
                            case "石像": Add("ResonantStep", "刻写：まね攻撃で石像を回した"); AddOne(new[] { "EchoBlade" }, "刻写：石像（2枚目）"); break;
                            case "脱出": Add("Footwork", "刻写：封印を解いて出口へ"); AddOne(new[] { "Recycle" }, "刻写：脱出（2枚目）"); break;
                            case "鎧割り": Add("EchoBlade", "刻写：まねで反響鎧を割った"); AddOne(new[] { "BladeDrill" }, "刻写：鎧割り（2枚目）"); break; // 【戦闘中心 v3】
                        }
                        if (list.Count >= 2) return list;
                    }
                foreach (var (kind, how) in stats.CopyKills.Distinct())
                {
                    AddOne(Engraving(how), $"刻写：{ActContent.KindLabel(kind)}を{how}で倒した");
                    var second = EngravingByKind(kind);
                    if (PuzzleRun || CombatRun) second = second.Select(x => x == "Rally" ? "RallyDraw" : x).ToArray(); // レビュー：旧「号令」（消耗なし）が出ていた
                    if (second.Length > 0) AddOne(second, $"刻写：{ActContent.KindLabel(kind)}の技");
                    if (list.Count >= 2) break;
                }
                return list;
            }
            // 【検証中】優先：倒した兵種 ＞ 環境でとどめ ＞ 同士討ちでとどめ（2026-10-05：同士討ちが毎戦起きて押し斬り・引き寄せに偏ったため、
            // 「同士討ちした」だけでは出さない）。兵種ごとに2枚の候補から1枚
            foreach (var kind in stats.Defeated.Distinct())
            {
                switch (kind)
                {
                    case EnemyKind.Archer: AddOne(new[] { "Pierce", "Thrust" }, "写し取り：弓兵の射線"); break;
                    case EnemyKind.Spearman: AddOne(new[] { "Thrust", "Pierce" }, "写し取り：槍兵の突き"); break;
                    case EnemyKind.Swordsman: AddOne(new[] { "Whirl", "ChippedSlash" }, "写し取り：剣兵の剣"); break;
                    case EnemyKind.Bomber: AddOne(new[] { "Trap", "Push" }, "写し取り：砲兵の仕掛け"); break;
                    case EnemyKind.MirrorSoldier: AddOne(new[] { "ChippedSlash", "MirrorSlash" }, "写し取り：鏡兵の反射への答え"); break;
                    case EnemyKind.KnightV2:
                    case EnemyKind.KnightV3: Add("Rally", "写し取り：騎士の号令"); break;
                }
            }
            if (stats.EnvironmentKills > 0) AddOne(new[] { "Trap", "Push", "Pull" }, "写し取り：トゲ・炎・爆発でとどめ");
            if (stats.FriendlyKills > 0) AddOne(new[] { "PushSlash", "Pull" }, "写し取り：同士討ちでとどめ");
            if (list.Count == 0)
            {
                // 受け皿：この部屋の報酬の傾向の札（前は「再考」。2026-10-05 に再考を報酬から外した）
                var fav = Pool.Where(c => (c.Tags & Current.RewardTags) != 0).ToList();
                if (fav.Count == 0) fav = Pool.ToList();
                Add(fav[R.Next(fav.Count)].Asset, "写し取り：戦いの記憶");
            }
            return list;
        }

        /// <summary>
        /// 【Copy Core V3】刻写（研究 2026-10-05）：倒し方で看板札（同士討ち＝写し刃／搬送＝追い風／砲台・誤射＝狙い定め）、
        /// 倒した兵種で2枚目（剣兵＝回転斬り・槍兵＝貫通・弓兵＝狙い定め・砲兵＝罠設置・盾兵＝闘気・反響鎧兵＝号令か応急処置）。
        /// </summary>
        static string[] Engraving(string how) => how switch
        {
            "写し攻撃" => new[] { "CopyBlade" },
            "写しの移動でトゲ" => new[] { "Tailwind" },
            "搬送" => new[] { "Tailwind" },      // 【戦闘中心 v3】まねで動かした敵が視線・炎・トゲ・ほかの敵の技で倒れた
            "共鳴" => new[] { "JointSlash" },    // 【戦闘中心 v3】鏡核の共鳴でとどめ
            _ => new[] { "Aim" }, // 写し砲・鏡晶・兵種の誤射
        };

        static string[] EngravingByKind(EnemyKind kind) => kind switch
        {
            EnemyKind.Swordsman => new[] { "Whirl" },
            EnemyKind.Spearman => new[] { "Pierce" },
            EnemyKind.Archer => new[] { "Aim", "Pierce" },
            EnemyKind.Bomber => new[] { "Spirit" },
            EnemyKind.Shield => new[] { "Spirit" },
            EnemyKind.EchoKnight => new[] { "Rally", "FirstAid" },
            EnemyKind.SealGuard => new[] { "Rally", "FirstAid" },
            EnemyKind.Mender => new[] { "FirstAid" },   // 【戦闘中心 v3】癒し手
            _ => new string[0],
        };

        /// <summary>【RC6】命令札の珍しさ（報酬の画面で光らせる）。抽選に無い札は普通。</summary>
        public Rarity Pool4Rarity(CommandData c) => c == null ? Rarity.Common : V4Content.FinalCommandPool.FirstOrDefault(x => x.Asset == c.name)?.Rarity ?? Rarity.Common;

        CardInfo WeightedPick(List<CardInfo> pool, CardTag favored)
        {
            // 重み：レア度（普通6・珍しい3・レア1）×部屋の傾向（×2）×役職の傾向（×2）
            int Weight(CardInfo c) => (c.Rarity == Rarity.Common ? 6 : c.Rarity == Rarity.Uncommon ? 3 : 1) * ((c.Tags & favored) != 0 ? 2 : 1) * ((c.Tags & Role.Favored) != 0 ? 2 : 1);
            int total = pool.Sum(Weight);
            int r = R.Next(total);
            foreach (var c in pool) { r -= Weight(c); if (r < 0) return c; }
            return pool[pool.Count - 1];
        }

        public bool TakeRewardCard(RewardCard rc)
        {
            if (RewardPicksLeft <= 0 || !RewardCards.Remove(rc)) return false;
            if (rc.IsTactic) { TacticCards.Add(rc.Card); OnTacticGained(); } else { Deck.Add(rc.Card); OnCommandGained(); }
            RewardPicksLeft--;
            Note($"- 報酬：{rc.Card.displayName}{(rc.IsCopySlot ? $"（{rc.Reason}）" : "")}　候補：{string.Join("・", RewardCards.Select(r => r.Card.displayName))}");
            History.Add($"　報酬：{rc.Card.displayName}{(rc.IsCopySlot ? "（写し取り）" : "")}");
            return true;
        }

        // ───── 【Combat V4】報酬：命令札2枚＋戦術札2枚（中ボスは専用の戦術札2つ＋抽選のレアの戦術札1つ＋命令札2枚＋戦術札1枚から2枚）。1枚目は無料・2枚目からルーン片。取らずに進むとルーン片が少し ─────

        void RollRewardsV4(BattleStats stats)
        {
            bool mid = Current.IsMidBoss;
            var cmdPool = V4CommandPool.Where(c => Deck.Count(d => d.name == c.Asset) < 2).ToList(); // 同じ札は2枚まで
            var tacPool = V4Content.FinalTacticPool.Where(c => TacticCards.Count(d => d.name == c.Asset) < 2 && card(c.Asset) != null).ToList();
            bool lesson = LessonBattle || LessonFloor2; // 【RC5・レビュー3】はじめてのランの1・2階は命令札3枚から（緑の札は3階で教える前に報酬に出ていた）
            // 【RC6・本人「報酬が少ない・ずっと同じ手札」】通常の戦闘は命令札3枚から1枚（緑は出さない：緑は手札1枚になり、取っても手札が変わらなかった）。緑は中ボスと商人で
            int cmdN = mid ? 2 : 3, tacN = mid ? 1 : 0; // 【RC2】中ボスでも命令札を2枚から
            if (mid)
            {
                foreach (var a in V4Content.MidBossTactics(Current.Id)) { var cd = card(a); if (cd != null) RewardCards.Add(new RewardCard { Card = cd, Reason = "中ボスの戦術札" }); }
                // 3つ目は抽選のレアの戦術札（専用の2つとは別に、毎回少し違う選択肢）
                var rares = tacPool.Where(c => c.Rarity == Rarity.Rare).ToList();
                if (rares.Count > 0) { var pick = rares[R.Next(rares.Count)]; tacPool.Remove(pick); var cd = card(pick.Asset); if (cd != null) RewardCards.Add(new RewardCard { Card = cd, Reason = "中ボスの戦術札" }); }
            }
            // 大技を敵に当てたら、早鐘・遅鐘・沈黙のような「大技を操る札」が出やすい（戦い方を次の戦闘へつなぐ）
            var famOrder = new[] { 0, 1, 2 }.OrderBy(_ => R.Next()).ToArray(); // 【通常リリース版】系統ごとに1枚（中ボスは2つの系統から）
            for (int i = 0; i < cmdN && cmdPool.Count > 0; i++)
            {
                var famPool = V4Content.RewardFamilies && i < famOrder.Length ? cmdPool.Where(c => V4Content.FamilyOf(c.Asset) == famOrder[i]).ToList() : null;
                var pick = WeightedPick(famPool != null && famPool.Count > 0 ? famPool : cmdPool, Current.RewardTags);
                cmdPool.Remove(pick);
                var cd = card(pick.Asset);
                if (cd != null) RewardCards.Add(new RewardCard { Card = cd });
            }
            for (int i = 0; i < tacN && tacPool.Count > 0; i++)
            {
                var pick = WeightedPick(tacPool, stats != null && stats.BigHitsOnEnemies > 0 ? CardTag.Copy : CardTag.None);
                tacPool.Remove(pick);
                var cd = card(pick.Asset);
                if (cd != null) RewardCards.Add(new RewardCard { Card = cd, Reason = stats != null && stats.BigHitsOnEnemies > 0 && (pick.Tags & CardTag.Copy) != 0 ? "大技を敵に当てた" : null });
            }
            RewardPicksLeft = mid ? 2 : 1;
            ExtraBuysLeft = mid ? 0 : 1;
            RewardUpgradesLeft = mid && AnyUpgradable ? 1 : 0; // 【RC6】中ボスは無料の強化も（報酬がはっきり多い）
            RewardImprints.Clear();
            if (mid) RewardImprints.AddRange(V4Content.ImprintPool.Where(i => !Has(i)).OrderBy(_ => R.Next()).Take(2)); // 【RC7】中ボスは刻印も（2つから1つ・無料）
        }

        public int ExtraPriceOf(RewardCard rc) => rc.IsTactic ? V4Content.ExtraRewardTactic : V4Content.ExtraRewardPrice(V4Content.FinalCommandPool.FirstOrDefault(x => rc.Card != null && x.Asset == rc.Card.name)); // 【RC6】珍しさで

        /// <summary>報酬の追加購入（無料の枚数を取ったあと。ルーン片を払って、もう1枚）。</summary>
        // ───── 【完成版・2026-10-20】入れ替え：命令札を手に入れたら、デッキの札を1枚外してよい（任意・無料） ─────
        // 理由：札を足すほど攻撃と移動の割合が崩れ、報酬を取らない方が勝ちやすかった（シミュレーション：取らない 72%・ふつうに取る 61%）。
        // 外すのは任意。敵に渡す危険（強い札ほど敵も使う）はそのまま残る。

        /// <summary>入れ替えであと何枚外せるか（命令札を手に入れるたびに +1。部屋を出ると 0）。</summary>
        public int SwapsLeft { get; private set; }

        void OnCommandGained() { if (V4Run && V4Content.FreeSwap) SwapsLeft++; }

        /// <summary>戦術札の入れ替えであと何枚外せるか（戦術札を手に入れるたびに +1）。</summary>
        public int TacticSwapsLeft { get; private set; }

        void OnTacticGained() { if (V4Run && V4Content.FreeSwap) TacticSwapsLeft++; }

        /// <summary>戦術札の入れ替え：戦術札のデッキの index の札を外す（無料）。最低 4 枚は残す。</summary>
        public bool TacticSwapRemoveAt(int index)
        {
            if (!V4Run || TacticSwapsLeft <= 0 || TacticCards.Count <= MinTactics || index < 0 || index >= TacticCards.Count) return false;
            var c = TacticCards[index];
            TacticCards.RemoveAt(index);
            TacticSwapsLeft--;
            History.Add($"　入れ替え（戦術札 {c.displayName} を外した）");
            Note($"- 入れ替え：戦術札 {c.displayName} を外した");
            return true;
        }

        public const int MinTactics = 4;

        /// <summary>セーブから：入れ替えの残り（報酬・商人の画面から続けたとき）。</summary>
        public void RestoreSwaps(int commands, int tactics) { if (!V4Run || !V4Content.FreeSwap) return; SwapsLeft = Math.Max(0, Math.Min(3, commands)); TacticSwapsLeft = Math.Max(0, Math.Min(3, tactics)); }

        /// <summary>入れ替え：デッキの index の札を外す（無料）。</summary>
        public bool SwapRemoveAt(int index)
        {
            if (!V4Run || SwapsLeft <= 0 || Deck.Count <= MinDeck || index < 0 || index >= Deck.Count) return false;
            var c = Deck[index];
            Deck.RemoveAt(index);
            ForgetUpgradeIfGone(c);
            SwapsLeft--;
            History.Add($"　入れ替え（{c.displayName} を外した）");
            Note($"- 入れ替え：{c.displayName} をデッキから外した");
            return true;
        }

        public bool BuyRewardCard(RewardCard rc)
        {
            if (!V4Run || RewardPicksLeft > 0 || ExtraBuysLeft <= 0 || !RewardCards.Contains(rc)) return false;
            int price = ExtraPriceOf(rc);
            if (Shards < price) return false;
            Shards -= price;
            ExtraBuysLeft--;
            RewardCards.Remove(rc);
            if (rc.IsTactic) { TacticCards.Add(rc.Card); OnTacticGained(); } else { Deck.Add(rc.Card); OnCommandGained(); }
            Note($"- 報酬（追加購入 {price}）：{rc.Card.displayName}");
            History.Add($"　報酬（購入 {price}）：{rc.Card.displayName}");
            return true;
        }

        public bool TakeRewardImprint(ImprintId id)
        {
            if (!RewardImprints.Contains(id)) return false;
            Imprints.Add(id);
            RewardImprints.Clear();
            if (V4Run) RewardUpgradesLeft = 0; // 【RC7・設計レビュー】中ボスの報酬は「刻印か無料の強化か」どちらか1つ
            History.Add($"　刻印：{ActContent.Imprint(id).Name}");
            Note($"- 刻印：{ActContent.Imprint(id).Name}");
            return true;
        }

        // ───── 商人（カード購入・削除・アイテム・刻印） ─────

        void RollShop()
        {
            ShopCards.Clear();
            ShopItems.Clear();
            // 【検証中】2026-10-05：商人には最初のデッキに無いカードだけを並べる（「削除したいカードが売られている」を無くす）
            var pool = Pool.Where(c => !ActContent.StartDeck.Contains(c.Asset) && !(V4Run && Deck.Count(d => d.name == c.Asset) >= 2)).ToList(); // 【RC5・レビュー】完成版は報酬と同じく同じ札は2枚まで
            for (int i = 0; i < (V4Run ? 3 : 4) && pool.Count > 0; i++) // 【RC6】完成版は命令札3（前は2）
            {
                var from = V4Run && i == 2 && pool.Any(c => c.Rarity != Rarity.Common) ? pool.Where(c => c.Rarity != Rarity.Common).ToList() : pool; // 【RC6】3枚目は珍しい札（目玉）
                var pick = WeightedPick(from, CardTag.None);
                pool.Remove(pick);
                var cd = card(pick.Asset);
                if (cd != null) ShopCards.Add(new ShopCard { Card = cd, Price = V4Run ? V4Content.CommandPrice(pick) : pick.Price });
            }
            if (V4Run)
            {
                // 【Combat V4】商人は戦術札も売る（命令札2＋戦術札2）
                var tpool = V4Content.FinalTacticPool.Where(c => card(c.Asset) != null && TacticCards.Count(d => d.name == c.Asset) < 2).ToList(); // 【RC2】同じ戦術札は2枚まで（報酬と同じ）
                for (int i = 0; i < 2 && tpool.Count > 0; i++)
                {
                    var pick = WeightedPick(tpool, CardTag.None);
                    tpool.Remove(pick);
                    ShopCards.Add(new ShopCard { Card = card(pick.Asset), Price = V4Content.TacticPrice(pick) });
                }
            }
            // 【検証中】2026-10-05：回復薬は必ず1つ並ぶ（通常戦でも HP が減るようになったので、回復を買える場所を確実に）。もう1つは無作為
            ShopItems.Add(new ShopItem { Id = ItemId.Potion, Price = ActContent.Item(ItemId.Potion).Price });
            foreach (var it in ActContent.Items.Where(i => i.Id != ItemId.Potion && !(CopyCentric && i.Id == ItemId.BlankStone) && !(i.Id == ItemId.WarpStone && (!V4Run || Items.Contains(ItemId.WarpStone)))).OrderBy(_ => R.Next()).Take(1)) ShopItems.Add(new ShopItem { Id = it.Id, Price = it.Price }); // 【RC4】転移石は持っていないときだけ
            var imprints = (V4Run ? V4Content.ImprintPool : ActContent.RewardImprints).Where(i => !Has(i)).ToList();
            // 【RC5・レビュー】完成版（V4）は刻印を売らない：説明の無い前の版の仕組みが、商人だけに残っていた
            // 【RC7・本人「ショップをもっと」】完成版の刻印（5つ・核を後押しする小さな力）を1つ売る。説明は商人・報酬・上の帯・README・チュートリアルに
            ShopImprint = imprints.Count > 0 ? imprints[R.Next(imprints.Count)] : (ImprintId?)null;
            ShopImprintSold = false;
            ShopRemoveUsed = false;
            ShopUpgradeUsed = false;
        }

        // ───── 【RC6】強化（商人：ルーン片・1回の訪問で1回／中ボスの報酬：無料で1回） ─────

        /// <summary>この商人で強化を買ったか（1回の訪問で1回）。</summary>
        public bool ShopUpgradeUsed { get; private set; }
        public int UpgradePrice => V4Content.UpgradePrice;

        /// <summary>商人で強化を買う。</summary>
        public bool BuyUpgrade(CommandData c, UpgradeKind kind)
        {
            if (!V4Run || Current == null || Current.Kind != RoomKind.Shop || ShopUpgradeUsed || Shards < UpgradePrice) return false;
            if (!UpgradeCard(c, kind)) return false;
            Shards -= UpgradePrice;
            ShopUpgradeUsed = true;
            Note($"- 商人：強化 {c.displayName}［{V4Content.UpgradeName(kind)}］（{UpgradePrice}）残り {Shards}");
            return true;
        }

        /// <summary>報酬の画面で、無料で強化できる回数（中ボスの報酬で 1）。</summary>
        public int RewardUpgradesLeft { get; private set; }
        public void UseRewardUpgrade() { if (RewardUpgradesLeft > 0) RewardUpgradesLeft--; if (V4Run) RewardImprints.Clear(); } // 【RC7】強化を選んだら刻印は無し（どちらか1つ）

        public bool BuyCard(ShopCard sc)
        {
            if (sc.Sold || Shards < sc.Price) return false;
            Shards -= sc.Price;
            sc.Sold = true;
            if (sc.Card.type == CommandType.Tactic) { TacticCards.Add(sc.Card); OnTacticGained(); } else { Deck.Add(sc.Card); OnCommandGained(); }
            History.Add($"　購入：{sc.Card.displayName}（{sc.Price}）");
            Note($"- 商人：カード {sc.Card.displayName}（{sc.Price}）残り {Shards}");
            return true;
        }

        public bool BuyItem(ShopItem si)
        {
            if (si.Sold || Shards < si.Price || Items.Count >= ActContent.ItemSlots) return false;
            Shards -= si.Price;
            si.Sold = true;
            Items.Add(si.Id);
            History.Add($"　購入：{ActContent.Item(si.Id).Name}（{si.Price}）");
            Note($"- 商人：アイテム {ActContent.Item(si.Id).Name}（{si.Price}）残り {Shards}");
            return true;
        }

        public bool BuyImprint()
        {
            if (ShopImprint == null || ShopImprintSold) return false;
            int price = ActContent.Imprint(ShopImprint.Value).Price;
            if (Shards < price) return false;
            Shards -= price;
            Imprints.Add(ShopImprint.Value);
            ShopImprintSold = true;
            History.Add($"　購入：刻印 {ActContent.Imprint(ShopImprint.Value).Name}（{price}）");
            Note($"- 商人：刻印 {ActContent.Imprint(ShopImprint.Value).Name}（{price}）残り {Shards}");
            return true;
        }

        /// <summary>商人でカードを1枚削除（同じ種類の最初の1枚）。画面からは RemoveCardPaidAt（選んだ1枚）を使う。</summary>
        public bool RemoveCardPaid(CommandData c) => RemoveCardPaidAt(Deck.IndexOf(c));

        /// <summary>
        /// 商人でデッキの index 番目の「その1枚」を削除（1回の訪問で1回。値段は使うたびに上がる）。
        /// 2026-10-05 本人プレイ：同じ種類を選ぶと全部消えるように見えた → 種類ではなく、選んだ1枚だけを消す。
        /// </summary>
        /// <summary>【RC6・レビュー2回目】その種類の札がデッキから無くなったら、強化の記録も消す（前は残って、取り直すと強化つきになり、続きからでは消えた）。</summary>
        void ForgetUpgradeIfGone(CommandData c)
        {
            if (c == null || Deck.Any(d => d.name == c.name)) return;
            Forged.Remove(c.name); Forged.Remove(c.name + "@+"); Forged.Remove(c.name + "@-");
        }

        public bool RemoveCardPaidAt(int index)
        {
            if (ShopRemoveUsed || Shards < RemovePrice || Deck.Count <= MinDeck || index < 0 || index >= Deck.Count) return false;
            var c = Deck[index];
            Deck.RemoveAt(index);
            ForgetUpgradeIfGone(c);
            Shards -= RemovePrice;
            History.Add($"　削除：{c.displayName}（{RemovePrice}）");
            Note($"- 商人：削除 {c.displayName}（{RemovePrice}）");
            RemovePrice += ActContent.RemovePriceStep;
            ShopRemoveUsed = true;
            return true;
        }

        // ───── 休息 ─────

        /// <summary>【完成版】休息の回復（反響深度3 では 5）。</summary>
        public int RestHealAmount => ActContent.RestHeal; // 【通常リリース版】深度で休息の回復は変えない（深度は まね返し と 手札だけ）

        /// <summary>
        /// 【RC6・レビュー「7階は商人が休息をほぼ支配」】休息は 回復・強化・削除 の3つから2つ（同じものは1回）。完成版（V4）のランだけ。前の版は1つ。
        /// 遊ぶ人が選んだ物を覚える（続きからでも同じ）。画面が MarkRest を呼ぶ（RestHeal などの中では数えない：前の版・テストと同じ使い方のまま）。
        /// </summary>
        public HashSet<string> RestDone { get; } = new HashSet<string>();
        public int RestActions => V4Run ? ActContent.RestPicks : 1; // 【通常リリース版】ActContent.RestPicks（ローグライクのレビュー「3つから2つ＝1つ捨てるだけで、ほぼ自動」）
        public bool CanRest(string kind) => !RestDone.Contains(kind) && RestDone.Count < RestActions;
        public void MarkRest(string kind) => RestDone.Add(kind);
        public bool RestFinished => RestDone.Count >= RestActions;

        public int RestHeal()
        {
            int before = Hp;
            Hp = Math.Min(MaxHp, Hp + RestHealAmount);
            History.Add($"　休息で回復（HP +{Hp - before}）");
            Note($"- 休息：HP +{Hp - before}（{Hp}/{MaxHp}）");
            return Hp - before;
        }

        public bool RestRemove(CommandData c) => RestRemoveAt(Deck.IndexOf(c));

        /// <summary>休息（祈る）：デッキの index 番目の「その1枚」を無料で削除。</summary>
        public bool RestRemoveAt(int index)
        {
            if (Deck.Count <= MinDeck || index < 0 || index >= Deck.Count) return false;
            var c = Deck[index];
            Deck.RemoveAt(index);
            ForgetUpgradeIfGone(c);
            History.Add($"　休息で削除（{c.displayName}）");
            Note($"- 休息：{c.displayName} を削除（無料）");
            return true;
        }

        // ───── 特殊部屋 ─────

        bool altarUsed;

        void RollEvent()
        {
            EventCards.Clear();
            altarUsed = false;
            if (Current.Id == "event_mirror")
            {
                var pool = Pool.Where(c => c.Rarity != Rarity.Common).OrderBy(_ => R.Next()).Take(3);
                foreach (var c in pool) { var cd = card(c.Asset); if (cd != null) EventCards.Add(cd); }
            }
            else if (Current.Id == "event_trade")
                RewardImprints.AddRange(ActContent.RewardImprints.Where(i => !Has(i)).OrderBy(_ => R.Next()).Take(2));
        }

        /// <summary>古い鏡：HP 3 を払って、珍しい札1枚。</summary>
        public bool MirrorTake(CommandData c)
        {
            if (!EventCards.Contains(c) || Hp <= 3) return false;
            Hp -= 3;
            Deck.Add(c);
            EventCards.Clear();
            History.Add($"　古い鏡：HP -3 で {c.displayName}");
            Note($"- 古い鏡：HP -3 で {c.displayName}（HP {Hp}/{MaxHp}）");
            return true;
        }

        /// <summary>崩れた祭壇：最大 HP -3 で刻印を1つ。</summary>
        public bool AltarImprint(ImprintId id)
        {
            if (altarUsed || !RewardImprints.Contains(id) || MaxHp <= 8) return false;
            altarUsed = true;
            MaxHp -= 3;
            Hp = Math.Min(Hp, MaxHp);
            Imprints.Add(id);
            RewardImprints.Clear();
            History.Add($"　崩れた祭壇：最大 HP -3 で刻印 {ActContent.Imprint(id).Name}");
            Note($"- 崩れた祭壇：最大 HP -3 で刻印 {ActContent.Imprint(id).Name}（HP {Hp}/{MaxHp}）");
            return true;
        }

        /// <summary>崩れた祭壇：ルーン片 +25（安全な方）。</summary>
        public void AltarShards()
        {
            if (altarUsed) return; // 二重に受け取らない
            altarUsed = true;
            Shards += 25;
            ShardsEarned += 25;
            RewardImprints.Clear();
            History.Add("　崩れた祭壇：ルーン片 +25");
            Note($"- 崩れた祭壇：ルーン片 +25（所持 {Shards}）");
        }

        // ───── アイテム ─────

        public bool AddItem(ItemId id)
        {
            if (Items.Count >= ActContent.ItemSlots) return false;
            Items.Add(id);
            return true;
        }

        public bool ConsumeItem(ItemId id) => Items.Remove(id);

        /// <summary>戦闘中の回復（回復薬）をランの HP に反映する前に使う：戦闘が終わると戦闘の HP がランの HP になる。</summary>
        public void SetHpForTest(int hp) => Hp = hp;

        /// <summary>テスト用：ルーン片を足す。</summary>
        public void AddShards(int n) { Shards += n; ShardsEarned += n; }
    }
}
