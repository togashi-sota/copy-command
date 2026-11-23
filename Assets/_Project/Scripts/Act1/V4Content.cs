using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【Combat V4（2026-10-07）】Combat V4 / External Playtest RC1 の中身：部屋・地図・役職・戦術札の抽選。
    /// 方針（本人）：コピーは常時 ON が前提。敵自身が危険（固有行動＋カウント大技）で、盤面も動く。その危険をコピーと位置と戦術札で逆利用する。
    /// 盤面・数値は tools/combat_v4_rooms.txt（pzsearch sim / act と run-setup が読む）。研究は docs/research/COMBAT_V4.md。
    /// </summary>
    public static class V4Content
    {
        // ───── 部屋 ─────
        // 【RC5・レビュー】速さの目安（ParTurns）＝部屋の監査（pzsearch audit）の Aware の平均ターン ×1.4 を四捨五入（前は部屋ごとに手で決めていて、Aware の 1.15〜1.82 倍とばらばらだった）
        public static readonly RoomDef[] Rooms =
        {
            // 通常戦（敵の固有行動＋大技）。1階は入門の3つから2つ（【完成版】鏡の歩兵を追加）
            new RoomDef { Id = "v4_i1", Name = "鏡の歩兵", Kind = RoomKind.Battle, Stage = "StageXD", CombatRun = true, V4 = true, Intro = true, RewardTags = CardTag.Attack | CardTag.Copy, ParTurns = 5,
                Flavor = "剣兵2（全まね）。剣兵は隣を斬る", CopyTip = "剣兵どうしを隣に並べて、誰もいない方へ斬る" },
            new RoomDef { Id = "v4_n1", Name = "前衛の二人", Kind = RoomKind.Battle, Stage = "StageX1", CombatRun = true, V4 = true, Intro = true, RewardTags = CardTag.Attack | CardTag.Move, ParTurns = 7,
                Flavor = "剣兵（全まね）と槍兵（技まね）。斜めの柱2本", CopyTip = "剣兵（全まね）をまねで歩かせ、八方斬り・槍の突きに相手を巻き込む", LessonTip = "剣兵（全まね）をまねで歩かせて、槍兵の突き（赤）の前に並べる（赤は まねの前に終わるので、当たるのは次のターン）" },
            new RoomDef { Id = "v4_n2", Name = "十字砲火", Kind = RoomKind.Battle, Stage = "StageX2", CombatRun = true, V4 = true, Intro = true, RewardTags = CardTag.Move | CardTag.Control, ParTurns = 7,
                Flavor = "弓兵（移動まね）と剣兵。柱が矢を止める", CopyTip = "あなたの移動で弓兵を動かし、射線へ剣兵を" },
            new RoomDef { Id = "v4_n3", Name = "盾の陰の砲兵", Kind = RoomKind.Battle, Stage = "StageX3", CombatRun = true, V4 = true, RewardTags = CardTag.Attack | CardTag.Env, ParTurns = 11,
                Flavor = "盾兵の横に砲兵2。硬い盾兵。四隅に柱", CopyTip = "硬い盾兵は、自分で殴るより砲兵に壊させる" },
            new RoomDef { Id = "v4_n4", Name = "追跡の群れ", Kind = RoomKind.Battle, Stage = "StageX4", CombatRun = true, V4 = true, RewardTags = CardTag.Move | CardTag.Attack, ParTurns = 7,
                Flavor = "追跡兵2（速い・隣を噛む）と剣兵。ワープ1組とトゲ", CopyTip = "剣兵をまねでワープへ送り、群れから離す（敵は自分からはワープに入らない）" },
            new RoomDef { Id = "v4_n5", Name = "槍の回廊", Kind = RoomKind.Battle, Stage = "StageX5", CombatRun = true, V4 = true, RewardTags = CardTag.Attack | CardTag.Control, ParTurns = 9,
                Flavor = "槍兵2と剣兵。真ん中に1マスの回廊", CopyTip = "回廊へ剣兵を歩かせ、槍の突き・貫通突撃の射線に並べる", LessonTip = "回廊へ剣兵を歩かせ、槍兵の突き（赤）の前に並べる（当たるのは次のターン）" },
            // 【完成版】通常戦（追加）
            new RoomDef { Id = "v4_n6", Name = "癒し手の陣", Kind = RoomKind.Battle, Stage = "StageXE", CombatRun = true, V4 = true, RewardTags = CardTag.Attack | CardTag.Copy, ParTurns = 8,
                Flavor = "癒し手・剣兵・槍兵。癒し手が仲間を回復する", CopyTip = "壁で剣兵（全まね）を止めて癒し手の隣へ運び、まねの斬撃で先に倒す" },
            new RoomDef { Id = "v4_n7", Name = "盾と槍の挟撃", Kind = RoomKind.Battle, Stage = "StageXF", CombatRun = true, V4 = true, RewardTags = CardTag.Control | CardTag.Attack, ParTurns = 10,
                Flavor = "槍兵2と盾兵。盾は正面だけ固い", CopyTip = "貫通突撃の予告（紫）が出たら射線から出て、盾兵を射線に残す", LessonTip = "盾兵を槍兵の突き（赤）の前へ歩かせる（当たるのは次のターン）。盾は横・背中からは通る" },
            // 戦術戦（動く危険つき）
            new RoomDef { Id = "v4_t1", Name = "動く炎床", Kind = RoomKind.Tactical, Stage = "StageX6", CombatRun = true, V4 = true, RewardTags = CardTag.Control | CardTag.Env, ParTurns = 6,
                Flavor = "炎床が行・列・隅へ動く。剣兵・槍兵", CopyTip = "炎の上へ敵を動かす（次の形は薄く見える）" },
            new RoomDef { Id = "v4_t2", Name = "落石の広間", Kind = RoomKind.Tactical, Stage = "StageX7", CombatRun = true, V4 = true, RewardTags = CardTag.Move | CardTag.Env, ParTurns = 8,
                Flavor = "落石が決まった場所へ。盾兵2と剣兵（自分では近づかない）", CopyTip = "落ちる場所に敵を立たせる" },
            new RoomDef { Id = "v4_t3", Name = "レーザー回廊", Kind = RoomKind.Tactical, Stage = "StageX8", CombatRun = true, V4 = true, RewardTags = CardTag.Control | CardTag.Attack, ParTurns = 8,
                Flavor = "レーザーが上から下へ掃く。盾兵・剣兵・弓兵", CopyTip = "盾兵を掃く行へ押し出す" },
            // 【完成版】戦術戦（追加）
            new RoomDef { Id = "v4_t4", Name = "トゲの通路", Kind = RoomKind.Tactical, Stage = "StageXG", CombatRun = true, V4 = true, RewardTags = CardTag.Move | CardTag.Env, ParTurns = 8,
                Flavor = "弓兵2（移動まね）と剣兵。通り道がトゲしかない壁", CopyTip = "壁の手前で動いて、壁の向こうの弓兵だけをトゲの通り道へ歩かせる" },
            // 中ボス（本体＋護衛）
            new RoomDef { Id = "v4_m1", Name = "処刑騎士", Kind = RoomKind.Danger, Stage = "StageX9", CombatRun = true, V4 = true, RewardTags = CardTag.Attack | CardTag.Control,
                Flavor = "大技3つ。処刑のあと2ターン動けない。護衛：癒し手・弓兵。騎士のそばにトゲ", CopyTip = "全まねの騎士を、あなたの移動のまねでトゲへ歩かせる。処刑の直線（紫）には護衛を残す" }, // 【RC5・レビュー】一言は監査で一番多い当て方から（騎士：トゲ 22・護衛へのまね 8）
            new RoomDef { Id = "v4_m2", Name = "時計仕掛けの砲王", Kind = RoomKind.Danger, Stage = "StageXA", CombatRun = true, V4 = true, RewardTags = CardTag.Control | CardTag.Env,
                Flavor = "動かない砲台。大技3つ。護衛：剣兵2", CopyTip = "動かない砲王の隣へ護衛の剣兵（全まね）を歩かせ、斬撃のまねで砲王を斬らせる。砲撃の予告マスへ護衛を送る" }, // 【RC5・レビュー】砲王：大技→護衛 12・まね→砲王 11（前は予告マスだけ）
            new RoomDef { Id = "v4_m3", Name = "獣王", Kind = RoomKind.Danger, Stage = "StageXB", CombatRun = true, V4 = true, RewardTags = CardTag.Move | CardTag.Attack,
                Flavor = "移動をまねて、自分でも近づく。大技3つ。護衛：砲兵・追跡兵", CopyTip = "寄ってくる獣王が護衛の隣に来たら、その向きへ攻撃：技まねの護衛が獣王を斬る。暴走突進の線へ砲兵を送る" }, // 【RC5・レビュー】獣王：まね→獣王 15・大技→護衛 6（前は暴走突進だけ）
            // ボス
            new RoomDef { Id = "v4_boss", Name = "鏡の主", Kind = RoomKind.Boss, Stage = "StageXC", CombatRun = true, V4 = true,
                Flavor = "主も写し身も、あなたの全部をまねる。段階2は写し身に主を斬らせ、段階3は鏡核をまねで同時に叩く", CopyTip = "主をトゲへ歩かせる・写し身と護衛のまね攻撃と大技を主へ当てる" },
        };

        public static RoomDef Room(string id) => Rooms.First(r => r.Id == id);

        /// <summary>【完成版】後半（5・6階）に出す部屋（初見のボットの被ダメが多かった部屋）。前半は残りから先に選ぶ。</summary>
        public static readonly string[] LateRooms = { "v4_n3", "v4_n4" }; // 【RC2】仕掛け戦は外した（毎ラン同じ2部屋が後半に出ていた）
        /// <summary>【RC3】動く仕掛けの部屋（炎床・落石・レーザー）は5階。止まった仕掛け（トゲの通路）は3階：階ごとに覚える物を1つずつ増やす（本人の初プレイのあとの見直し）。</summary>
        public static readonly string[] LateTactical = { "v4_t1", "v4_t2", "v4_t3" };

        /// <summary>
        /// 地図（8階）：1 入門 → 2 通常（柱と壁）×2 → 3 トゲの通路／中ボス → 4 休息／商人 → 5 通常（ワープ）／動く仕掛け → 6 中ボス／中ボス → 7 商人／休息 → 8 ボス。各階は2つの扉から1つ。
        /// 【RC3】盤面も階ごとに育てる：1階＝ほぼ広い＋柱 → 2階＝壁・柱（同じ移動で、あなたと敵の結果が変わる）→ 3階＝狭い通り道と止まったトゲ（または中ボス）
        ///        → 5階＝ワープ・動く仕掛け（炎床・落石・レーザー）→ 6階＝中ボス → 8階＝ボス（覚えた物の組み合わせ）。
        /// 入門3・通常5・戦術4・中ボス3から種で無作為（同じ部屋は1ランに1回だけ）。中ボスは3階と6階で別のもの。
        /// </summary>
        /// <summary>【RC5・コードレビュー】地図の作り方の版。セーブに残し、古いセーブは古い作り方で同じ地図に戻す（地図は種から作り直すので）。
        /// 0＝RC5 のレビュー2回目の前（3階の中ボスに砲王も出た）／2＝今（3階は砲王以外）。</summary>
        public const int MapRev = 2;

        /// <summary>
        /// 【RC5・レビュー3】扉の「難しさ ★」（1〜5）。前は部屋の種類だけで決めていて、中身と合わなかった（盾の陰の砲兵が ★2・動く炎床が ★3・砲王と獣王が同じ ★4）。
        /// 部屋の監査（Logs/final/rc5f_audit.txt）の初見に近いボット（Novice）の勝ちと、画面の助けを使う初見寄り（Learner）の被ダメ・ターンから付けた。
        /// </summary>
        public static int DangerOf(RoomDef r) => r.Id switch
        {
            "v4_i1" => 1,
            "v4_n1" or "v4_n2" or "v4_n4" or "v4_n5" or "v4_n6" or "v4_n7" or "v4_t1" => 2,
            "v4_n3" or "v4_t2" or "v4_t3" or "v4_t4" => 3,
            "v4_m1" or "v4_m3" => 4,
            "v4_m2" or "v4_boss" => 5,
            _ => r.Kind == RoomKind.Boss ? 5 : r.IsMidBoss ? 4 : r.Kind == RoomKind.Tactical ? 3 : 2,
        };

        public static List<List<RoomDef>> BuildMap(Random rng, int mapRev = MapRev)
        {
            List<RoomDef> Shuffle(IEnumerable<RoomDef> src) => src.OrderBy(_ => rng.Next()).ToList();
            var intro = Shuffle(Rooms.Where(r => r.Kind == RoomKind.Battle && r.Intro));
            // 【完成版】2階は Late でない部屋（柱と壁）。5階は Late の部屋（盾の陰の砲兵＝一番長い通常戦・追跡の群れ＝ワープ。【RC5・レビュー3】追跡の群れは難しめではなく「新しい仕掛け」）
            var normal = Shuffle(Rooms.Where(r => r.Kind == RoomKind.Battle && !r.Intro)).OrderBy(r => LateRooms.Contains(r.Id) ? 1 : 0).ToList();
            var staticHazard = Shuffle(Rooms.Where(r => r.Kind == RoomKind.Tactical && !LateTactical.Contains(r.Id)));
            var movingHazard = Shuffle(Rooms.Where(r => r.Kind == RoomKind.Tactical && LateTactical.Contains(r.Id)));
            var mids = Shuffle(Rooms.Where(r => r.Kind == RoomKind.Danger));
            RoomDef Take(List<RoomDef> a) { var x = a[0]; a.RemoveAt(0); return x; }
            RoomDef TakeLate(List<RoomDef> a) { var x = a[a.Count - 1]; a.RemoveAt(a.Count - 1); return x; } // 【完成版】5階は Late の部屋から
            List<RoomDef> Pair(RoomDef a, RoomDef b) => new List<RoomDef> { a, b }.OrderBy(_ => rng.Next()).ToList();
            bool restFirst = rng.Next(2) == 0;
            RoomDef rest = ActContent.Room("rest"), shop = ActContent.Room("shop");
            var f1 = new List<RoomDef> { Take(intro), Take(intro) }; // 【完成版】入門3つから2つ
            var f2 = Pair(Take(normal), Take(normal)); // 【RC3】柱と壁の部屋（槍の回廊・癒し手の陣・盾と槍の挟撃から2つ）
            var mid3 = mapRev >= 2 ? mids.First(r => r.Id != "v4_m2") : mids[0]; // 【RC5・レビュー】3階の中ボスは砲王以外（初見に一番難しい部屋が、緑の札と大技を覚える3階に来ていた）。乱数の使い方は前と同じ
            mids.Remove(mid3);
            var f3 = Pair(Take(staticHazard), mid3); // 【RC3】止まった仕掛け（トゲの通路）か中ボス
            // 【RC2】6階は「残りの中ボス2体から1つ」（中盤の山場を必ず1回。前は仕掛け戦との2択で、中ボスを避けると山場がボスだけだった：レビュー）。
            var f5 = Pair(TakeLate(normal), Take(movingHazard)); // 【RC3】ワープの部屋（後半の通常戦）か動く仕掛け
            var f6 = Pair(Take(mids), Take(mids));
            return new List<List<RoomDef>>
            {
                f1, f2, f3,
                restFirst ? new List<RoomDef> { rest, shop } : new List<RoomDef> { shop, rest },
                f5, f6,
                restFirst ? new List<RoomDef> { shop, rest } : new List<RoomDef> { rest, shop },
                new List<RoomDef> { Room("v4_boss") },
            };
        }

        // ───── 役職（3つ） ─────
        public static readonly string[] RoleIds = { "standard", "commander", "observer" };

        /// <summary>役職の中身（名前と id は ActContent.Roles のものを使う）。役職の能力は小さく：ゲームの核（コピーと大技の逆利用）を隠さない。</summary>
        public static RoleDef Role(RoleDef r)
        {
            var c = new RoleDef { Id = r.Id, Name = r.Name, Description = r.Description, Playstyle = r.Playstyle, HpDelta = 0, BaseHand = 7, Favored = CardTag.None, CopyHint = r.CopyHint };
            switch (r.Id)
            {
                case "standard":
                    c.Name = "旅の剣士"; c.Description = "戦闘が終わるたびに HP +1。ほかの能力は無い。いちばん安定する。はじめての人に。"; c.Playstyle = "安定"; // 【RC12・本人「役職は縦の強さではなく遊び方の違いに」】
                    c.PostHeal = 1; c.CopyHint = "誰もいない方へ斬ると、隣の敵がまねて仲間を斬る。"; // 【RC5・最終ゲート】はじめてのランではまだ大技が出ない
                    break;
                case "commander":
                    c.Name = "指揮官"; c.Description = "敵どうしを戦わせる役職。最初のデッキに「けしかけ」（あなたは斬らず、まねた敵だけが斬る）・緑の札「模倣命令」（敵全員が全部まねる）。緑の札を2枚まで持てる。そのかわり最大 HP -2。"; c.Playstyle = "戦術札・上級";
                    c.HpDelta = -2; // 【RC12】最大 HP 15：緑の札を使いこなせば強いが、雑だと苦しい（FanLo 41 → 28%・Learner 99 → 97%：docs/final/RC12_DESIGN.md 6章）
                    // 【RC5】大号令（命令枠 +2・敵のまねも +2）→ 模倣命令。大号令は どのボットでも被ダメが増える札だった（指揮官だけ弱い原因：Learner 81%→ 外すと 88%・Skilled 被ダメ 3.8→3.0）。
                    //   模倣命令は核（まね）を強める札：Learner 91%・Skilled 被ダメ 2.9・まね 6.6/戦（旅の剣士 93%・3.0・6.4）。docs/final/RC5_CHANGES.md
                    if (RoleCards) { c.RemoveCards = new[] { "Slash" }; c.AddCards = new[] { "Incite" }; } // 【通常リリース版】斬撃1枚 → けしかけ（あなたは斬らない・まねた敵だけが斬る）：敵どうしを戦わせる役職
                    c.TacticBonus = 1; c.AddTactics = new[] { "Mimic" }; c.CopyHint = "模倣命令のラウンドは、技まね・移動まねの敵も全部まねる：敵どうしを斬らせる好機。";
                    c.Favored = CardTag.Copy; // 【RC6・レビュー「役職がデッキに触らない」】報酬に「敵に渡す札」（写し刃・合わせ斬り・追い風・けしかけ・進め）が出やすい
                    break;
                case "observer":
                    c.Name = "観測士"; c.Description = "敵を危険へ送る役職。大技の範囲が2ターン前から薄く見える。最初のデッキに「追い風」（あなたは1マス・まねた敵は遠くへ）・緑の札「遅鐘」。そのかわり最大 HP -3。"; c.Playstyle = "読み・不安定";
                    c.HpDelta = -3; // 【RC12・本人「観測士の方が簡単に感じる」】最大 HP 14：先が見えて読める人には旅の剣士と同じくらい、読まない人には苦しい（人らしいボット Fan 92 → 74%・Learner 92 → 87%）
                    if (RoleCards) { c.RemoveCards = new[] { "Walk" }; c.AddCards = new[] { "Tailwind" }; } // 【通常リリース版】一歩1枚 → 追い風（敵だけ遠くへ運ぶ）：見えた大技・罠へ敵を送る役職
                    c.Preview = true; c.AddTactics = new[] { "LateBell" }; c.CopyHint = "薄く見えた大技の範囲は、まだ動く。撃つターンの紫で確定。";
                    c.Favored = CardTag.Move; // 【RC6・レビュー】報酬に移動の札（踏み込み・跳躍・追い風・進め・忍び足）が出やすい
                    break;
            }
            return c;
        }

        // ───── 戦術札 ─────
        /// <summary>最初の戦術札（6枚）。早鐘が最初から1枚入っている（大技を前倒しする面白さを最初の戦闘から試せる）。</summary>
        /// <summary>【通常リリース版・デザイナーとローグライクのレビュー「最初の緑の札の多くが、どのゲームにもある効果（集中×2＝自分で殴るのを強める：核と逆）」】2枚目の集中を「せき立て」（このラウンド、まねで動く敵 +1マス）に。</summary>
        public static readonly string[] CoreStartTactics = { "Tactic_Focus", "Tactic_Weight", "Tactic_Guard", "Tactic_Mend", "Tactic_Tangle", "Tactic_EarlyBell" }; // 【第3巡の QA】チュートリアル7 が教える「集中」は最初の1枚だけ残す（抽選には出さない）。深呼吸を外した // 【通常リリース版】集中 2枚 → 重し・せき立て（まねで動く敵 ±1マス）
        /// <summary>RC12 までの最初の6枚（比べ用）。</summary>
        public static readonly string[] OldStartTactics = { "Tactic_Breath", "Tactic_Focus", "Tactic_Guard", "Tactic_Mend", "Tactic_Focus", "Tactic_EarlyBell" };
        /// <summary>最初の緑の札（静的な初期化の順番：Core・Old の後に置く）。</summary>
        public static string[] StartTactics = CoreStartTactics; // 【通常リリース版】2枚目の集中 → せき立て（pzsearch の PZ_OLDTACTICS=1 で前の6枚）
        /// <summary>【通常リリース版・レビュー「役職の違いが HP の数字だけ」】役職ごとに最初のデッキの1枚を「まねとの関係が違う札」に（pzsearch の PZ_ROLECARDS=0 で前のまま）。</summary>
        public static bool RoleCards = true; // 【RC3】最初の札から「号令」（命令の枠 +1・敵のまねも増える）を外し、2枚目の「集中」に（はじめは効き目が分かる札だけ：本人「緑の札が分からない」）

        /// <summary>報酬・商人に出る戦術札（中ボス専用は含まない）。</summary>
        public static readonly CardInfo[] TacticPool =
        {
            new CardInfo { Asset = "Tactic_Breath", Rarity = Rarity.Common, Tags = CardTag.Draw },
            new CardInfo { Asset = "Tactic_Sift", Rarity = Rarity.Common, Tags = CardTag.Draw },
            new CardInfo { Asset = "Tactic_Focus", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "Tactic_Guard", Rarity = Rarity.Common, Tags = CardTag.None },
            new CardInfo { Asset = "Tactic_Mend", Rarity = Rarity.Common, Tags = CardTag.None },
            new CardInfo { Asset = "Tactic_Muster", Rarity = Rarity.Uncommon, Tags = CardTag.Multi },
            new CardInfo { Asset = "Tactic_Recall", Rarity = Rarity.Uncommon, Tags = CardTag.Draw },
            new CardInfo { Asset = "Tactic_EarlyBell", Rarity = Rarity.Uncommon, Tags = CardTag.Copy },
            new CardInfo { Asset = "Tactic_LateBell", Rarity = Rarity.Uncommon, Tags = CardTag.Copy },
            new CardInfo { Asset = "Tactic_Echo", Rarity = Rarity.Uncommon, Tags = CardTag.Copy },
            new CardInfo { Asset = "Tactic_Silence", Rarity = Rarity.Uncommon, Tags = CardTag.Control },
            new CardInfo { Asset = "Tactic_Tangle", Rarity = Rarity.Rare, Tags = CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "Tactic_Weight", Rarity = Rarity.Rare, Tags = CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "Tactic_Mimic", Rarity = Rarity.Rare, Tags = CardTag.Copy },
            new CardInfo { Asset = "Tactic_GreatMuster", Rarity = Rarity.Rare, Tags = CardTag.Multi },
        };

        /// <summary>中ボスを倒したときに選べる専用の戦術札（倒した敵の力を奪う）。3つ目は抽選のレア。</summary>
        public static string[] MidBossTactics(string roomId)
        {
            switch (roomId)
            {
                case "v4_m1": return new[] { "Tactic_Toll", "Tactic_Parry" };        // 処刑騎士：処刑の鐘・見切り
                case "v4_m2": return new[] { "Tactic_Detonate", "Tactic_Retarget" }; // 砲王：即時爆破・照準替え
                case "v4_m3": return new[] { "Tactic_Pursuit", "Tactic_Roar" };      // 獣王：追撃命令・咆哮
                default: return new string[0];
            }
        }

        public static int TacticPrice(CardInfo c) => c.Rarity == Rarity.Common ? 28 : c.Rarity == Rarity.Uncommon ? 38 : 52; // 【RC6】22/34/50 → 28/38/52（ルーン片がランの終わりに約100余っていた）

        /// <summary>【RC6】完成版の命令札の値段（普通 40・珍しい 55・レア 70。前は 30/45/65 で、ルーン片が約100余った：Logs/rc6/econ）。</summary>
        public static int CommandPrice(CardInfo c) => c.Rarity == Rarity.Common ? 40 : c.Rarity == Rarity.Uncommon ? 55 : 70;

        // ───── 【完成版・2026-10-20】命令札の報酬（盤面で働く札だけ・「あなたが使うと／敵がまねると」がはっきり違う札を中心に） ─────
        /// <summary>
        /// 完成版の命令札の抽選。基本札（最初のデッキ）は出さない。
        /// 3つの系統：①強い攻撃（敵にも渡る：回転斬り・貫通・薙ぎ払い）／②敵に渡す札（まねた敵の方が強い・あなたは弱い：写し刃・合わせ斬り・追い風）／
        /// ③守りの札（まねは弱い・まねされない：欠け斬り・踏み込み・忍び足）。Locked＝最初は出ない（クリアで解放：FinalMeta）。
        /// </summary>
        public static readonly CardInfo[] FinalCommandPool =
        {
            new CardInfo { Asset = "Whirl", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "Pierce", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "Sweep", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "Lunge", Rarity = Rarity.Common, Tags = CardTag.Move },
            new CardInfo { Asset = "Tailwind", Rarity = Rarity.Common, Tags = CardTag.Move | CardTag.Copy },
            // 【完成版・2026-10-20】狙い定め（Aim）は外した：その札だけを取るランが一番負けた（取らない 72% → 51%）。使いどころが分かりにくい
            new CardInfo { Asset = "CopyBlade", Rarity = Rarity.Uncommon, Tags = CardTag.Attack | CardTag.Copy },
            new CardInfo { Asset = "JointSlash", Rarity = Rarity.Uncommon, Tags = CardTag.Attack | CardTag.Copy },
            new CardInfo { Asset = "ChippedSlash", Rarity = Rarity.Uncommon, Tags = CardTag.Attack },
            new CardInfo { Asset = "Jump", Rarity = Rarity.Uncommon, Tags = CardTag.Move },
            new CardInfo { Asset = "Spirit", Rarity = Rarity.Uncommon, Tags = CardTag.Attack | CardTag.Multi },
            new CardInfo { Asset = "FirstAid", Rarity = Rarity.Rare, Tags = CardTag.Draw },
            // 解放される札（クリアで）：号令の札（あなたは何もしない・まねた敵だけが動く／斬る）と、まねされない移動
            new CardInfo { Asset = "Incite", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Attack }, // 【RC2】解放の札は Uncommon（Rare だと解放しても次のランの約7割で一度も出なかった：レビュー）
            new CardInfo { Asset = "March", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "Sneak", Rarity = Rarity.Uncommon, Tags = CardTag.Move },
        };

        /// <summary>
        /// 【通常リリース版・ローグライクのレビュー「報酬の3枚が『同じ攻撃』に偏る」】命令札の系統：0＝強い攻撃（敵も同じ強さ）／1＝敵に渡す札（まねた敵の方が強い・あなたは弱いか何もしない）／2＝守り・移動（まねは弱い・まねされない）。
        /// 報酬の3枚は系統ごとに1枚（RewardFamilies）：毎回「強さを分け合う／敵にあげる／安全」の三択になる（強い札ほど敵にも渡る＝核の選択）。
        /// </summary>
        public static int FamilyOf(string asset) => asset switch
        {
            "Whirl" or "Pierce" or "Sweep" or "Spirit" => 0,
            "CopyBlade" or "JointSlash" or "Tailwind" or "Incite" or "March" => 1,
            _ => 2,
        };

        /// <summary>【通常リリース版】報酬の命令札を系統ごとに1枚ずつ出す（pzsearch の PZ_FAMILIES=0 で前の抽選）。</summary>
        public static bool RewardFamilies = true;

        /// <summary>【完成版】最初は抽選に出ない札（FinalMeta で解放）。</summary>
        public static readonly string[] LockedCards = { "Incite", "March" }; // 【通常リリース版】忍び足（まねされない移動）は最初から守りの系統に。解放は核の札 けしかけ・進め だけ

        /// <summary>【完成版】報酬・商人に出る戦術札（反響・再演・模倣命令は使われ方が分かりにくいので外した。アセットは残す）。</summary>
        public static readonly string[] FinalTacticExcluded = { "Tactic_Echo", "Tactic_Recall", "Tactic_Mimic", "Tactic_Focus", "Tactic_Breath", "Tactic_Sift" }; // 【通常リリース版・デザイナーとローグライクのレビュー】集中・深呼吸・選別は抽選から外す（核の札：早鐘・遅鐘・沈黙・せき立て・重し を引きやすく。残心・手当ては初見の助けで残す）
        public static IEnumerable<CardInfo> FinalTacticPool => TacticPool.Where(c => !FinalTacticExcluded.Contains(c.Asset));

        /// <summary>【完成版】鍛えられる札（攻撃の札だけ：あなたが使うときだけ +2。敵のまねは元のまま）。</summary>
        public static bool CanForge(CommandData c) => c != null && c.type != CommandType.Tactic && c.damage > 0 && !c.selfInert; // 【RC2】けしかけ等（あなたは攻撃しない札）は鍛えても意味が無い

        /// <summary>
        /// 【RC6・本人「強化を まね につなげる」】強化できるか：自分＋は あなたが攻撃する札だけ。まね＋・まね－は 敵のまねがダメージを出す札だけ（けしかけ等も）。
        /// </summary>
        public static bool CanUpgrade(CommandData c, UpgradeKind kind)
        {
            if (c == null || c.type == CommandType.Tactic || c.damage <= 0) return false;
            if (kind == UpgradeKind.Self) return !c.selfInert;
            if (kind == UpgradeKind.CopyDown && c.selfInert) return false; // 【RC6・レビュー】けしかけ等は まね－にしても弱くなるだけ
            return c.damage - c.copyDamageCut > 0;
        }

        /// <summary>
        /// 【RC11・本人「強化を押したら何になるか想像できない」／ChatGPT「1枚の札に強化は1種類で」】札ごとに強化は1つに決める：
        /// ふつうの攻撃＝あなたが使うと +2（敵のまねは元のまま）。敵に渡す札（写し刃・けしかけ など、あなたの一撃が無い札）＝敵のまね +3。
        /// （RC6〜RC10 は 自分＋／まね＋／まね－ の3つから選んだ。古いセーブの「名前@-」はそのまま読む）
        /// </summary>
        public static bool SingleUpgrade = true;
        public static UpgradeKind DefaultUpgrade(CommandData c) => c == null ? UpgradeKind.Self
            : c.selfInert || c.copyDamageCut < 0 ? UpgradeKind.CopyUp
            : UpgradeMode == 1 && c.damage > 0 && c.copyDamageCut == 0 ? UpgradeKind.CopyUp // 【通常リリース版】敵も同じ強さで使う攻撃の札は、強くするほど敵にも渡る
            : UpgradeKind.Self;
        /// <summary>
        /// 【通常リリース版・デザイナーとローグライクのレビュー「強化（あなたが使うときだけ +2）が核と逆：育つほど自分で殴る方が得」】
        /// 1＝敵も同じ強さで使う攻撃の札は「まね＋」（あなた +1・敵のまね +3）。まねが弱い札（欠け斬りなど）だけ「自分＋」。0＝RC11（ほとんど自分＋）。pzsearch の PZ_UPMODE。
        /// </summary>
        public static int UpgradeMode = 1;
        public static bool CanUpgradeAny(CommandData c) => SingleUpgrade ? CanUpgrade(c, DefaultUpgrade(c)) : CanUpgrade(c, UpgradeKind.Self) || CanUpgrade(c, UpgradeKind.CopyUp);
        /// <summary>【RC11】強化の前と後（選ぶ前に見せる）：「斬撃 4 → 6（あなたが使うときだけ）」。</summary>
        public static string UpgradePreview(CommandData c)
        {
            if (c == null) return "";
            var k = DefaultUpgrade(c);
            int cd = Math.Max(0, c.damage - c.copyDamageCut);
            if (k == UpgradeKind.Self) return $"{c.displayName}　{c.damage}ダメ → {c.damage + DeckState.ForgeBonus}ダメ（あなたが使うときだけ・敵のまねは {cd} のまま）";
            if (c.selfInert) return $"{c.displayName}　敵のまね {cd}ダメ → {cd + DeckState.CopyUpgrade}ダメ（敵どうしを斬らせる札・あなたには当たらない）";
            return $"{c.displayName}　あなた {c.damage}→{c.damage + DeckState.CopyUpSelf}・敵 {cd}→{cd + DeckState.CopyUpgrade}（まね返しも強くなる）"; // 【通常リリース版】短く（強化の画面で切れていた）
        }

        /// <summary>【RC6】強化の名前と短い説明（画面と記録で同じ言葉）。</summary>
        public static string UpgradeName(UpgradeKind k) => k == UpgradeKind.Self ? "自分＋" : k == UpgradeKind.CopyUp ? "まね＋" : "まね－";
        public static string UpgradeLine(UpgradeKind k) => k == UpgradeKind.Self ? $"あなたが使うと +{DeckState.ForgeBonus}（敵のまねは元のまま）"
            : k == UpgradeKind.CopyUp ? $"敵のまねが +{DeckState.CopyUpgrade}・あなたは +{DeckState.CopyUpSelf}（敵どうしに強い・あなたに当たっても強い）"
            : $"敵のまねが -{DeckState.CopyDowngrade}・あなたは +{DeckState.CopyDownSelf}（安全）";

        // ───── 【RC6・本人の実プレイ「HP20 に対して雑魚が強すぎる」】階ごとの敵の強さ ─────
        /// <summary>
        /// 雑魚の通常の攻撃（赤）の強さ（階 1〜8）。前はどの階も 3。解析（pzsearch）は PZ_RED="2,2,…" で比べる。
        /// 【RC6】1〜5階 2・6階から 3（docs/final/RC6_DESIGN.md 3章・Logs/rc6/power）。
        /// </summary>
        public static int[] RedByFloor = { 2, 2, 2, 2, 2, 2, 3, 3 }; // 【RC7】6階（中ボスの階）も 2：6階の負けが一番多かった（本人も 6階で負け）。3 は 7〜8階だけ
        /// <summary>雑魚の大技（八方斬り 5・貫通突撃 5・三連射 4・飛びかかり 5・三連爆撃 4・盾突進 3）に足す量（階 1〜8）。PZ_CLOCKD。【RC6】1〜5階 -1。</summary>
        public static int[] ClockDeltaByFloor = { -1, -1, -1, -1, -1, 0, 0, 0 };
        /// <summary>最後の1体（ボス以外）が受けるダメージの追加（0＝無し）。PZ_LASTSTAND。</summary>
        public static int LastStandBonus = 0;
        /// <summary>
        /// 【RC6・本人「最後の1体の片付けが長い」】孤立：最後の1体（ボス・中ボスの本体以外）が受けるダメージは2倍。
        /// 比べた結果（Logs/rc6/boss）：通常戦で最後の1体のターン 43〜55% → 31〜42%・通常戦 8.6〜9.9 → 7.0〜7.6 ターン。+1・+2 ではほとんど縮まなかった。PZ_LASTDOUBLE=0 で無し。
        /// </summary>
        public static bool LastStandDouble = true;
        /// <summary>
        /// 【RC6・レビュー「無料の入れ替えで削除が無価値・デッキが育たない」】札を手に入れたら1枚外せる「入れ替え」（RC2〜RC5）を外した。
        /// 手札の保証（移動・攻撃2枚ずつ）でデッキが薄まっても困らなくなった：入れ替え無しで Learner 95 → 96%・Novice 64 → 65%・Rookie2 54 → 57%（Logs/rc6/rev/noswap.txt）。
        /// デッキを薄くするのは 商人・休息の削除で。解析は PZ_SWAP（ボットの入れ替え）とは別。
        /// </summary>
        public static bool FreeSwap = false;
        /// <summary>【RC6・解析だけ】仕掛けの部屋（戦術戦）は雑魚の大技を弱めない。**採らなかった**（仕掛けの部屋の被ダメ 1.9 → 2.0：ほとんど変わらない）。解析は PZ_TACFULL=1。</summary>
        public static bool TacticalFullClocks = false;

        /// <summary>【RC12】激化：目安のターン（ParTurns）＋EscalateSlack を過ぎると雑魚の攻撃 +1。0＝使わない。ボスの部屋は使わない（ボスは自分の段階で強くなる）。</summary>
        public static bool Escalation = false;
        public static int EscalateSlack = 0;
        public static bool EscalateMidBoss = true;
        public static int EscalateAfterFor(RoomDef room)
        {
            if (!Escalation || room == null || room.ParTurns <= 0) return 0;
            if (room.Kind == RoomKind.Boss) return 0;
            if (room.Kind == RoomKind.Danger && !EscalateMidBoss) return 0;
            return room.ParTurns + EscalateSlack;
        }

        /// <summary>この階・部屋の敵の強さ（赤に足す量・雑魚の大技に足す量）。</summary>
        public static (int red, int clock) PowerFor(int floor, RoomDef room)
        {
            int f = System.Math.Max(0, System.Math.Min(RedByFloor.Length - 1, floor));
            int clock = ClockDeltaByFloor[System.Math.Min(ClockDeltaByFloor.Length - 1, f)];
            if (TacticalFullClocks && room != null && room.Kind == RoomKind.Tactical) clock = 0; // 【RC6・レビュー「仕掛けの部屋が易しい」】
            return (RedByFloor[f] - PhaseRules.V4Melee, clock);
        }

        /// <summary>【完成版】挑戦の段階の説明。</summary>
        public static string ChallengeText(int level) => level switch
        {
            0 => "ふつう",
            1 => "反響深度1：まね返し +2（敵のまねた攻撃が あなたに当たると +2）", // 【通常リリース版】前は「敵の HP +15%」（HP を増やすだけ＝本人「このゲームらしくない」・戦闘が長くなる）
            2 => "反響深度2：＋手札 6 枚", // 【通常リリース版・ローグライクのレビュー「深度2・3 は回復の数字をいじるだけ・旅の剣士の能力が消える」】深度は まね返し と 手札（命令の数）だけ
            3 => "反響深度3：まね返し +3・手札 5 枚・やり直しなし",
            _ => "",
        };
        public const int MaxChallenge = 3;

        /// <summary>
        /// 【通常リリース版・反響ランク】クリアしたランの評価：敵へのダメージのうち、敵自身（まね・敵の攻撃・大技）と仕掛けが与えた割合。
        /// 自分で殴るだけでも勝てるが、核（まね）を使うほど上がる＝2周目以降の目標。目安はボットで測った（Logs/rel/p1）。
        /// </summary>
        public static int[] EchoRankBounds = { 65, 50, 35 }; // 【通常リリース版】ボットのクリアしたラン（Logs/rel/p2/mid46_e0.txt）：Skilled 平均 64%・Fan 58%・Learner 54%・まねを考えない Blind 24%。S は上手い人の半分くらい・まねを使わないと C
        public static string EchoRankOf(int pct) => pct >= EchoRankBounds[0] ? "S" : pct >= EchoRankBounds[1] ? "A" : pct >= EchoRankBounds[2] ? "B" : "C";
        /// <summary>【通常リリース版・第2巡】ランで受けたダメージの目安（S 10 以下・A 20 以下・B 30 以下）。割合だけだと、戦闘が長引くほど偶然の同士討ちで上がった（雑なボットほど S が多い）。</summary>
        public static int[] EchoDamageBounds = { 10, 20, 30 };
        static string DamageRankOf(int dmg) => dmg <= EchoDamageBounds[0] ? "S" : dmg <= EchoDamageBounds[1] ? "A" : dmg <= EchoDamageBounds[2] ? "B" : "C";
        /// <summary>反響ランク：敵が敵を傷つけた割合のランクと、受けたダメージのランクの低い方。</summary>
        public static string EchoRankOf(int pct, int damageTaken)
        {
            string a = EchoRankOf(pct), b = DamageRankOf(damageTaken);
            return "CBAS".IndexOf(a) <= "CBAS".IndexOf(b) ? a : b; // 低い方（C ＜ B ＜ A ＜ S）
        }
        /// <summary>【通常リリース版】反響深度3 の休息の回復（ふつうより 3 少ない）。</summary>
        public static int DeepRestHeal => System.Math.Max(1, ActContent.RestHeal - 3);
        /// <summary>【RC5・レビュー】挑戦で減る手札（深度2 で 1 枚・深度3 で 2 枚）。HP と回復の変化は、ほとんど被弾しない慣れた人に効かなかった（Skilled：深度0〜3で被ダメ 3.4〜3.5・クリア 100%）。
        /// 手札を減らすと、Skilled の被ダメ 3.5 → 5.5（深度2）→ 7.9（深度3）・ターンも増える。Learner は深度2 で 74% → 73%・深度3 で 61% → 39%（Logs/rc5_audit/exp_r3）。</summary>
        public static int ChallengeHandCut(int level) => level >= 3 ? 2 : level >= 2 ? 1 : 0;
        /// <summary>
        /// 【RC7・本人「ショップをもっと・スレスパみたいに」・レビュー「2ラン目の新鮮さが弱い」】完成版の刻印（ランの間ずっと効く小さな力）5つ。
        /// どれも核（敵どうし・仕掛け・出す命令の数）を後押しする：まねの刃・鏡の盾・時計の針・罠師の刻印・余白の刻印。
        /// 手に入る場所：旅立ちの選択（いつも1つ並ぶ・最大 HP -2）・商人に1つ（55〜60）・中ボスの報酬（刻印2つから1つ か 無料の強化、どちらか）。
        /// 【RC7・設計レビュー】「同士討ちの戦利品」（ルーン片 +5）は外した：ルーン片はランの終わりに 60〜70 余っていて、取ると損だった。
        /// 代わりに「余白の刻印」（命令2つ以下なら次のターン +1枚）＝毎ターン「何枚出す＝敵に何枚渡す」を変える刻印。
        /// </summary>
        public static readonly ImprintId[] ImprintPool = { ImprintId.CopyEdge, ImprintId.ClockHand, ImprintId.Trapper }; // 【通常リリース版・ローグライクのレビュー】鏡の盾（まね返し -2）は反響深度1（まね返し +2）をちょうど打ち消す・余白（命令2つ以下で札 +1）は どのボットでも損で核と無関係 → 外した（コードは残す）。残る3つは どれも「敵を使うと得」
        /// <summary>
        /// 【RC7】刻印の強さ。最初は +1・-1・+2・+2 だった → 1つずつ1階から持たせた比べ（150 ラン）で、どれもクリアの差が ±4% 以内（ばらつきの範囲）＝効いているのが感じられない大きさ。
        /// 核（敵どうし・仕掛け）が「効いた」と分かる大きさへ：まねの刃 +2・鏡の盾 -2・時計の針 +3・罠師 +3（Logs/rc7/design/imp_*）。
        /// </summary>
        public const int ImprintCopyEdge = 2, ImprintMirrorGuard = 2, ImprintClockHand = 3, ImprintTrap = 3;
        /// <summary>【RC7】旅立ちの選択で刻印を取るときの代償（最大 HP）。前の版の 3 は HP 20 に重い。</summary>
        public const int BoonImprintHpCost = 2;
        /// <summary>【RC7】同士討ちの戦利品：あなたの攻撃以外で倒れた敵1体ごとのルーン片（1戦 SpoilsMax 体まで）。</summary>
        public const int SpoilsShards = 5, SpoilsMax = 4;

        /// <summary>【RC6】商人の強化の値段（1回の訪問で1回）。</summary>
        public const int UpgradePrice = 40;
        /// <summary>報酬の2枚目を追加で取るときの値段（1枚目は無料）。</summary>
        public const int ExtraRewardCommand = 30, ExtraRewardTactic = 30; // 【RC6】命令札 25 → 30（商人の札 40 より少し安い）
        /// <summary>【RC6・レビュー「珍しい札は報酬で買う方が安い」】報酬の2枚目の値段：普通 30・珍しい 40・レア 55（商人は 40/55/70）。</summary>
        public static int ExtraRewardPrice(CardInfo c) => c == null || c.Rarity == Rarity.Common ? ExtraRewardCommand : c.Rarity == Rarity.Uncommon ? 40 : 55;
        /// <summary>報酬を何も取らずに先へ進むと、ルーン片が少し。【完成版】0（取らないのが一番強かったので、取らないことにおまけは付けない。入れ替えでデッキは薄く保てる）。</summary>
        public static int SkipRewardShards = 0; // 解析（pzsearch の PZ_SKIPSHARDS）で変えられるように static
        /// <summary>戦闘のあとの自然回復（通常戦・戦術戦・中ボス）。</summary>
        public static int PostBattleHeal = 0; // 【RC11・本人「簡単すぎた」】1 → 0（休息 6・敵の HP ×1.15 と合わせて）。pzsearch の PZ_POSTHEAL
        /// <summary>
        /// 【RC12・本人の RC11 実プレイ「本編は少し簡単」・仕様「難しさは火力だけでなく、HP の管理・回復でも」】完成版のランの最大 HP（役職の HpDelta はこの上に足す）。
        /// 20 → 17。ほかの 20（チュートリアル・5分体験・前のモード）は ActContent.StartHp のまま。比べた数字は docs/final/RC12_DESIGN.md 5章（pzsearch の PZ_ROLEPATCH=hp:-3）。
        /// </summary>
        public static int StartHp = 16; // 【通常リリース版・第3巡】敵の HP の水増し（RC11 ×1.15・RC12 ×1.13）を戻し、難しさは あなたの最大 HP で合わせた（敵の HP で難しくしない：本人の監査項目5）。Fan 76%・Novice 58%・戦闘が短く（Logs/rel/p5/b_hp130_p16.txt）
    }
}
