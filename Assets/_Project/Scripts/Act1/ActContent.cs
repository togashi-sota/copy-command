using System;
using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    // ───────────────────────────────────────────────────────────────
    // 【検証中】ACT1 縦切り（2026-10-04 夜）：中身の定義（部屋・アイテム・刻印・役職・カードの値段と傾向）。
    // データは全部ここ。数値は仮。設計の理由は docs/research/ACT1_RUN.md。
    // ───────────────────────────────────────────────────────────────

    public enum RoomKind { Battle, Danger, Boss, Rest, Shop, Event, Tactical, Trial } // 【戦闘中心 v3】Tactical 戦術戦（全滅＋仕掛け1つ）・Trial 試練（短いパズル。失敗しても負けではない）

    /// <summary>戦闘中に使えるアイテム（命令カードとは別。所持は2つまで・使うと無くなる）。</summary>
    public enum ItemId { Potion, BlankStone, CommandStone, Hourglass, Hook, WarpStone } // 【RC4】WarpStone 転移石（救済の移動・RescueRules）

    /// <summary>刻印（ラン中ずっと効くパッシブ）。コピー・命令のルールに触る物を中心に。</summary>
    public enum ImprintId { FourthCommand, ThornGuide, Echo, Margin, Vanguard, Observer, CopyEdge, MirrorGuard, ClockHand, Trapper, Spoils } // 【RC7】CopyEdge 以降＝完成版の刻印（V4Content.ImprintPool）

    /// <summary>報酬の傾向（扉に書く）。</summary>
    [Flags]
    public enum CardTag { None = 0, Attack = 1, Move = 2, Control = 4, Env = 8, Draw = 16, Multi = 32, Copy = 64 }

    public enum Rarity { Common, Uncommon, Rare }

    public class ItemDef
    {
        public ItemId Id; public string Name; public string Description; public int Price;
    }

    public class ImprintDef
    {
        public ImprintId Id; public string Name; public string Description; public int Price; public string Build;
    }

    public class RoleDef
    {
        public string Id, Name, Description, Playstyle;
        public int HpDelta;
        public int BaseHand = 7;
        public string[] AddCards = new string[0];
        public ImprintId[] Imprints = new ImprintId[0];
        public ItemId[] Items = new ItemId[0];
        /// <summary>【検証中】最初のデッキから抜く札（入れ替え）。</summary>
        public string[] RemoveCards = new string[0];
        /// <summary>【Combat V4】戦闘が終わるたびの回復・戦術札を1ターンに使える枚数の増加・大技の範囲を②から薄く見せる・最初の戦術札に足す札（Tactic_ なし）。</summary>
        public int PostHeal, TacticBonus;
        public bool Preview;
        public string[] AddTactics = new string[0];
        /// <summary>【検証中】出やすい報酬の傾向（報酬・商人の重み ×2）。役職ごとにランの方向が変わる。</summary>
        public CardTag Favored;
        /// <summary>【検証中】この役職での「コピーの使い方」（役職を選ぶ画面に一文）。</summary>
        public string CopyHint = "";
    }

    public class CardInfo
    {
        public string Asset; public Rarity Rarity; public CardTag Tags;
        /// <summary>【検証中】抽選（報酬・商人・鏡・旅立ち）に出さず、理由つきの「写し取り」でだけ手に入る札。</summary>
        public bool EarnedOnly;
        /// <summary>【検証中】2026-10-05 本人プレイ（最初の商人で何も買えない）→ コモン 30・アンコモン 45・レア 65。</summary>
        public int Price => PriceOverride > 0 ? PriceOverride : Rarity == Rarity.Common ? 30 : Rarity == Rarity.Uncommon ? 45 : 65;
        /// <summary>【Puzzle v2】値段の上書き（引く札は 20：盤面で何もしない1枠なので安く。レビュー）。</summary>
        public int PriceOverride;
        /// <summary>レア度の印（カードの上に出す）。</summary>
        public string RarityMark => Rarity == Rarity.Common ? "★" : Rarity == Rarity.Uncommon ? "★★" : "★★★";
    }

    public class RoomDef
    {
        public string Id, Name, Flavor, Stage;
        public RoomKind Kind;
        /// <summary>報酬の傾向（この部屋の報酬は、この傾向のカードが出やすい）。</summary>
        public CardTag RewardTags;
        /// <summary>【検証中】任意ミッション（危険戦だけ。null なら無し）。</summary>
        public Mission Mission;
        /// <summary>ACT1 の中での敵の HP（0 ならステージのまま）。解析（Act1Study）で決めた仮の値。</summary>
        public int EnemyHp;
        /// <summary>【検証中】速さの目安（このターン数以内に勝つとルーン片 +ActContent.ParBonus。0 なら無し）。急がない人を罰しない「少し得」。</summary>
        public int ParTurns;
        /// <summary>【検証中・Copy Core V3】コピー中心のミニ ACT1 の部屋（今の ACT1 の地図には出さない）。</summary>
        public bool CopyRun;
        /// <summary>【Copy Core V3】3本柱（攻撃利用・移動利用・同期利用）。扉に出す。</summary>
        public string Pillar = "";
        /// <summary>【Puzzle v2】パズル中心 ACT1 v2 の部屋（ほかの地図には出さない）。</summary>
        public bool PuzzleRun;
        /// <summary>【Puzzle v2】この部屋で新しく出る物（1部屋1つ。扉に出す）。</summary>
        public string NewThing = "";
        /// <summary>【戦闘中心 v3】戦闘中心 ACT1 v3 の部屋（ほかの地図には出さない）。</summary>
        public bool CombatRun;
        /// <summary>【戦闘中心 v3】コピーを使うと楽になる所（扉に一言。答えの手順は書かない）。</summary>
        public string CopyTip = "";
        /// <summary>【RC4】はじめてのランの2階（大技なし）で、CopyTip の代わりに出す一言（大技を使わない攻め方）。空なら CopyTip。</summary>
        public string LessonTip = "";
        /// <summary>【戦闘中心 v3】1階に出す入門の部屋。</summary>
        public bool Intro;
        /// <summary>【Combat V4】Combat V4 の部屋（カウント大技・敵の積極的な動き・戦術札）。Kind が Danger なら中ボス。</summary>
        public bool V4;
        public bool IsMidBoss => V4 && Kind == RoomKind.Danger;
    }

    public enum MissionKind { EnvironmentKill, FriendlyFire, NoDamage, FastClear, FriendlyKill }

    /// <summary>【検証中】任意ミッション：達成すると少しだけルーン片。危険戦の一部の部屋だけ（毎戦のチェックリストにしない）。</summary>
    public class Mission
    {
        public MissionKind Kind; public int Turns; public int Count = 1; public int Reward = 20;
        public string Text => Kind switch
        {
            MissionKind.EnvironmentKill => Count > 1 ? $"敵を{Count}体、トゲ・炎・爆発・石像の視線で倒す" : "敵をトゲ・炎・爆発・石像の視線で倒す", // 【Puzzle v2】視線も環境で倒した数に入る（レビュー：文に無かった）
            MissionKind.FriendlyKill => "敵の攻撃で、別の敵にとどめを刺させる",
            MissionKind.FriendlyFire => "敵の攻撃で別の敵を傷つける",
            MissionKind.NoDamage => "ノーダメージで勝つ",
            _ => $"{Turns}ターン以内に勝つ",
        };
    }

    /// <summary>【検証中】ACT1 の戦闘に持ち込む物（TurnManager.LoadActBattle に渡す）。</summary>
    public class ActBattleSetup
    {
        public int Hp, MaxHp;
        public List<CommandData> Deck = new List<CommandData>();
        public int BonusSlots;
        public int BaseHand = 7;
        public int SpikeFirstBonus;
        /// <summary>【検証中】観測士：命令を組むと、敵のコピー後の結果まで見える（完全予測）。</summary>
        public bool FullPrediction;
        public int AttackBonusEachTurn;
        public Func<IReadOnlyList<BattleEvent>, int, int> RoundEndDrawBonus;
        public Random Rng = new Random(1);
        /// <summary>【Puzzle v2】Rng の種（やり直しで同じ順番にする）。</summary>
        public int Seed = 1;
        /// <summary>【戦闘中心 v3】手札整理で捨てられる枚数（1戦1回・命令の枠を使わない。0 なら無し）。</summary>
        public int TidyMax;
        /// <summary>【Combat V4】戦術札を1ターンに使える枚数の増加（指揮官）・大技の範囲を残り②から薄く見せる（観測士）。</summary>
        public int TacticPlayBonus;
        public bool PreviewAhead;
        /// <summary>【Combat V4】戦術札のデッキ（null か空なら戦術札なし）。</summary>
        public List<CommandData> TacticCards = new List<CommandData>();
        /// <summary>【RC3】大技なし（はじめてのランの1階・チュートリアル）。</summary>
        public bool NoClocks;
        /// <summary>【RC3】敵は まねるだけ（固有の行動なし）：はじめてのランの最初の戦闘。</summary>
        public bool NoTraits;
        /// <summary>【完成版】鍛えた札の種類（アセット名）。あなたが使うときだけダメージ +2。</summary>
        public HashSet<string> Forged = new HashSet<string>();
        /// <summary>【RC6】階ごとの敵の強さ（V4Content.PowerFor）：雑魚の通常の攻撃・雑魚の大技に足す量・最後の1体が受けるダメージの追加。</summary>
        public int RedDelta, ClockDelta, LastStand;
        /// <summary>【RC12】激化のターン（0＝無し）。</summary>
        public int EscalateAfter;
        /// <summary>【RC6】孤立（最後の1体は受けるダメージ2倍）。</summary>
        public bool LastStandDouble;
        /// <summary>【RC7】刻印（BattleModel の同じ名前へ）。</summary>
        public int CopyEdgeBonus, MirrorGuard, ClockHandBonus, TrapBonus;
        /// <summary>【完成版】挑戦の段階（0＝ふつう）。効果は V4Content.ChallengeText（1：敵の HP +15%（ボス以外）／2：自然回復なし・手札6／3：休息 5・ボスの再挑戦なし・手札5）。</summary>
        public int Challenge;
        /// <summary>【完成版】開始配置の番号（0＝基本の盤面・1 以上＝StageData.altMaps の何番目）。部屋ごとの乱数で決まる＝セーブから戻っても同じ。</summary>
        public int Layout;
    }

    public static class ActContent
    {
        public const int StartHp = 20;
        public const int ItemSlots = 2;
        /// <summary>【検証中】2026-10-05：6 → 8（通常戦でも削られるようになったため）。</summary>
        /// <summary>【通常リリース版・研究】休息で選べる数（3つから）。pzsearch の PZ_RESTPICKS。</summary>
        public static int RestPicks = 1; // 【通常リリース版・ローグライクのレビュー「3つから2つ＝1つ捨てるだけで、ほぼ自動」】3つから1つ・回復 6（Fan 84→80%・Novice 55→48%：Logs/rel/p3/rest1_6.txt）
        public static int RestHeal = 6; // 【通常リリース版】3つから1つにしたので 5 → 6（回復を選ぶと強化・削除はできない） // 【RC12】6 → 5（最大 HP 17・敵の HP ×1.13 と合わせて：docs/final/RC12_DESIGN.md 5章） 【RC11・本人「簡単すぎた」】8 → 6（敵の HP ×1.15・戦闘のあとの回復 0 と合わせて：docs/final/RC11_DESIGN.md 9章）。pzsearch の PZ_RESTHEAL
        public const int RemovePriceBase = 25;
        public const int RemovePriceStep = 15;
        /// <summary>【検証中】速さの報酬（RoomDef.ParTurns 以内に勝つと）。</summary>
        public const int ParBonus = 6;
        /// <summary>【検証中】撃破ボーナス（1体あたり。部屋の敵の数まで）と無傷ボーナス（2026-10-05 経済の作り直し）。</summary>
        /// <summary>2026-10-05 RC の解析：3 → 2（旅立ち・撃破・無傷が重なって最初の商人が約 100 になり、ボット全体のクリア率が上がりすぎたため）。</summary>
        public const int KillBonus = 2;
        /// <summary>【検証中・Copy Core V3】写し箱1つのルーン片（敵のまねた攻撃でだけ開く。任意のごほうび）。</summary>
        public const int ChestShards = 10;
        public const int NoDamageBonus = 5;
        /// <summary>【Puzzle v2】目標の部屋（封印解除・搬送・同期・脱出）を解いたときのルーン片（撃破ボーナスの代わり）。</summary>
        public const int GoalBonus = 8;
        /// <summary>【検証中】旅立ちの選択（2026-10-05）：ルーン片・削除のおまけ・刻印の代償（最大 HP）。解析で 35/15 → 30/10。</summary>
        public const int BoonShards = 30;
        public const int BoonTrimShards = 10;
        public const int BoonImprintHpCost = 3;

        public static readonly ItemDef[] Items =
        {
            new ItemDef { Id = ItemId.Potion, Name = "回復薬", Description = "HP を 6 回復する。", Price = 25 },
            new ItemDef { Id = ItemId.BlankStone, Name = "空白の石", Description = "組んだ命令を1つ選び、このターン敵にまねさせない。", Price = 35 },
            new ItemDef { Id = ItemId.CommandStone, Name = "命令石", Description = "このターンだけ命令の上限 +1（増えた命令も敵がまねする）。", Price = 35 },
            new ItemDef { Id = ItemId.Hourglass, Name = "選別の砂時計", Description = "命令に入れていない手札を全部捨てて、同じ枚数引き直す。", Price = 20 },
            new ItemDef { Id = ItemId.Hook, Name = "回収の鉤", Description = "捨て札から1枚を選んで手札に戻す。", Price = 20 },
            new ItemDef { Id = ItemId.WarpStone, Name = "転移石", Description = "危ないマス（赤・紫・床の危険）にいるときだけ使える。3マス以内の空いたマスへ移る（命令の前）。命令の枠を使わない・敵はまねない・壁は通らない。", Price = 30 }, // 【RC4】
        };

        public static readonly ImprintDef[] Imprints =
        {
            new ImprintDef { Id = ImprintId.FourthCommand, Name = "第四命令", Description = "命令の上限 3→4。敵も4命令をまねする。", Price = 70, Build = "多命令" },
            new ImprintDef { Id = ImprintId.ThornGuide, Name = "棘導", Description = "1ラウンドで最初に敵がトゲを踏んだとき +1 ダメージ。", Price = 70, Build = "環境" },
            new ImprintDef { Id = ImprintId.Echo, Name = "反響の刻印", Description = "敵がまねした命令で敵がダメージを受けたラウンドの次、追加で1枚引く。", Price = 70, Build = "まね操作" },
            new ImprintDef { Id = ImprintId.Margin, Name = "余白の刻印", Description = "命令2つ以下でターンを終えたら、次のターン 札を +1枚（出す命令が少ないほど、敵に渡す命令も少ない）。", Price = 55, Build = "手札" }, // 【RC7】完成版の刻印にも
            // 役職の初期刻印（ショップ・報酬には出ない）
            new ImprintDef { Id = ImprintId.Vanguard, Name = "先兵の構え", Description = "毎ターン、あなたの最初の直接攻撃 +1（敵のまねには乗らない）。", Price = 0, Build = "直接攻撃" },
            new ImprintDef { Id = ImprintId.Observer, Name = "観測眼", Description = "命令を組むと、敵がまねした後の動きと攻撃まで見える（完全予測）。", Price = 0, Build = "読み" },
            // 【RC7】完成版（V4）の刻印：どれも「敵どうし・仕掛け」で戦う手を後押しする小さな力（あなたの直接攻撃には乗らない）。商人・中ボスの報酬・旅立ちの選択で
            new ImprintDef { Id = ImprintId.CopyEdge, Name = "まねの刃", Description = "敵がまねた攻撃が敵に当たると、そのダメージ +2。", Price = 60, Build = "まね" },
            new ImprintDef { Id = ImprintId.MirrorGuard, Name = "鏡の盾", Description = "まね返し（あなたの命令を敵がまねて、あなたに当たる）のダメージ -2。", Price = 55, Build = "守り" },
            new ImprintDef { Id = ImprintId.ClockHand, Name = "時計の針", Description = "敵の大技（紫・★）が敵に当たると、そのダメージ +3。", Price = 60, Build = "大技" },
            new ImprintDef { Id = ImprintId.Trapper, Name = "罠師の刻印", Description = "トゲ・床の危険が敵に当たると、そのダメージ +3。", Price = 55, Build = "仕掛け" },
            new ImprintDef { Id = ImprintId.Spoils, Name = "同士討ちの戦利品", Description = "あなたの攻撃以外（まね・大技・仕掛け）で敵が倒れるたびに、ルーン片 +5（1戦 4体まで）。", Price = 50, Build = "稼ぎ" },
        };

        /// <summary>
        /// 報酬・商人・祭壇に出る刻印。【検証中】2026-10-05：第四命令は外した（指揮官だけ）。解析で「敵も同じ数コピー」では読める人の弱点にならず、
        /// 行動+1／共鳴／2回コピーの案でも4命令が得なままだったため、手に入る場所を絞る（案A＋E。ACT2 ではボス報酬の候補）。
        /// </summary>
        public static ImprintId[] RewardImprints => new[] { ImprintId.ThornGuide, ImprintId.Echo, ImprintId.Margin };

        public static ItemDef Item(ItemId id) => Items.First(i => i.Id == id);
        public static ImprintDef Imprint(ImprintId id) => Imprints.First(i => i.Id == id);

        /// <summary>役職。標準（無能力）は必ず残す。役職は「同じコピー＋命令を違う角度から遊ぶ」もの（別ゲームにしない）。</summary>
        public static readonly RoleDef[] Roles =
        {
            // 2026-10-05：役職ごとに「出やすい報酬」と最初のデッキを変えた（監査：役職の違いが +1 程度で、ランの方向が変わらない）
            new RoleDef { Id = "standard", Name = "旅の剣士（標準）", Description = "能力なし。報酬の傾向も無い。", Playstyle = "基準",
                CopyHint = "敵に向けて斬れば、敵のまねは外側へ空振りする。" },
            new RoleDef { Id = "vanguard", Name = "先兵", Description = "毎ターン最初の直接攻撃 +1（敵のまねには乗らない）。斬撃 +1 枚。攻撃の札が出やすい。", Playstyle = "直接攻撃",
                AddCards = new[] { "Slash" }, Imprints = new[] { ImprintId.Vanguard }, Favored = CardTag.Attack,
                CopyHint = "あなたの +1 は写されない。正面から殴り合う回数を減らせる。" },
            new RoleDef { Id = "observer", Name = "観測士", Description = "敵のまねの結果まで見える（完全予測）。先読み +1 枚。まね操作の札が出やすい。", Playstyle = "読み・初心者向け",
                AddCards = new[] { "Foresight" }, Imprints = new[] { ImprintId.Observer }, Favored = CardTag.Copy,
                CopyHint = "組んだ命令を敵がどう写すか、全部見える。読みの練習に。" },
            new RoleDef { Id = "engineer", Name = "遺跡技師", Description = "刻印「棘導」から。最初のデッキの斬撃1枚が罠設置に。環境の札が出やすい。", Playstyle = "環境利用",
                AddCards = new[] { "Trap" }, RemoveCards = new[] { "Slash" }, Imprints = new[] { ImprintId.ThornGuide }, Favored = CardTag.Env,
                CopyHint = "罠設置も写される。敵が自分の隣にトゲを置き、自分で踏む。" },
            new RoleDef { Id = "commander", Name = "指揮官", Description = "刻印「第四命令」から（敵も4命令）。最大 HP -6。多命令の札が出やすい。", Playstyle = "多命令・上級",
                HpDelta = -6, Imprints = new[] { ImprintId.FourthCommand }, Favored = CardTag.Multi,
                CopyHint = "4つ目の命令も敵に渡る。渡しても困らない命令で埋める。" },
            new RoleDef { Id = "mirrorist", Name = "鏡術師", Description = "空白の石・忍び足・欠け斬りから。まね操作の札が出やすい。", Playstyle = "まね操作",
                AddCards = new[] { "Sneak", "ChippedSlash" }, Items = new[] { ItemId.BlankStone }, Favored = CardTag.Copy,
                CopyHint = "写させない（忍び足）・弱く写させる（欠け斬り）で、まねを選んで渡す。" },
        };

        /// <summary>最初のデッキ（12枚：移動5・攻撃5・制御2）。</summary>
        /// <summary>
        /// 最初のデッキ（12枚）。【RC8・本人「攻撃をかわす手段が移動しかない。ずらしてかわしたい」】斬撃 3 → 2・押し斬り 1 → 2：
        /// 攻撃しながら敵を押す札が増え、敵の攻撃（赤）を押してずらす場面が約 1.7 倍に（ずらし 0.18 → 0.34／戦・向け直し 0.07 → 0.12／戦・初見寄りのボット。Logs/rc8/run_deck2push）。
        /// 解析で入れ替えて比べる（pzsearch の PZ_STARTDECK）。
        /// </summary>
        public static string[] StartDeck = {
            "Walk", "Walk", "Dash", "Dash", "Rush", "Slash", "Slash", "PushSlash", "Thrust", "PushSlash", "Push", "Pull" };

        /// <summary>
        /// 報酬・商人に出るカード（2026-10-05 監査で作り直し：12種）。
        /// 最初のデッキにある基本札（一歩・駆け足・突進・斬撃・突き・押し斬り・押し出し・引き寄せ）は、抽選に出さない
        /// （本人「カードの価値が平ら」・商人に削除したい札が並ぶ、の原因だった）。基本札は理由つきの「写し取り」でだけ出る。
        /// 外した札（アセットは残す）：薙ぎ払い（回転斬りと重なる）・再考と先読み（手札7枚補充では価値が薄い。先読みは観測士の初期札）・踏み込み（効果が見えにくい）。
        /// 値段はレア度：普通 30／珍しい 45／レア 65。
        /// </summary>
        public static readonly CardInfo[] CardPool =
        {
            // 普通：コピーされたときの動きが分かりやすい札
            new CardInfo { Asset = "Sneak", Rarity = Rarity.Common, Tags = CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "Whirl", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "Pierce", Rarity = Rarity.Common, Tags = CardTag.Attack },
            new CardInfo { Asset = "WallKick", Rarity = Rarity.Common, Tags = CardTag.Move | CardTag.Env },
            // 珍しい：盤面・コピーとの関係でビルドの方向を変える札
            new CardInfo { Asset = "Jump", Rarity = Rarity.Uncommon, Tags = CardTag.Move },
            new CardInfo { Asset = "ChippedSlash", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Attack },
            // 反転斬り：鏡兵・騎士の反射を外す「答え」。抽選には出さず、鏡兵を倒した写し取りでだけ手に入る（RC 解析：商人で買えるとボスが楽になりすぎた）
            new CardInfo { Asset = "MirrorSlash", Rarity = Rarity.Rare, Tags = CardTag.Copy | CardTag.Attack, EarnedOnly = true },
            new CardInfo { Asset = "Trap", Rarity = Rarity.Uncommon, Tags = CardTag.Env },
            new CardInfo { Asset = "Lure", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "Spirit", Rarity = Rarity.Uncommon, Tags = CardTag.Attack | CardTag.Multi },
            // レア：ランを大きく変える札
            new CardInfo { Asset = "Rally", Rarity = Rarity.Rare, Tags = CardTag.Multi },
            new CardInfo { Asset = "FirstAid", Rarity = Rarity.Rare, Tags = CardTag.Draw | CardTag.Copy },
        };

        public static CardInfo Info(string asset) => CardPool.FirstOrDefault(c => c.Asset == asset) ?? CopyRunCards.FirstOrDefault(c => c.Asset == asset) ?? PuzzleRunCards.FirstOrDefault(c => c.Asset == asset) ?? V4Content.TacticPool.FirstOrDefault(c => c.Asset == asset);

        /// <summary>抽選に出す札（写し取りだけの札を除く）。</summary>
        public static IEnumerable<CardInfo> RandomPool => CardPool.Where(c => !c.EarnedOnly);

        /// <summary>部屋（扉の向こう）。戦闘は既存の V 系・VS・VB を使う（1ステージ1テーマ）。</summary>
        public static readonly RoomDef[] Rooms =
        {
            // 【検証中】2026-10-05：通常戦4部屋を ACT1 専用の盤面（A1〜A4）へ。「コピーが怖い」を通常戦でも（docs/research/ACT1_TENSION_REPORT.md）
            new RoomDef { Id = "thorn", Name = "棘の回廊", Kind = RoomKind.Battle, Stage = "StageA1", RewardTags = CardTag.Env | CardTag.Move, Flavor = "トゲの間の狭い通路。剣兵のまね移動は柱で止まる", ParTurns = 5 },
            new RoomDef { Id = "warp", Name = "転移の間", Kind = RoomKind.Battle, Stage = "StageA2", RewardTags = CardTag.Move, Flavor = "弓兵が真後ろ。柱の陰で射線を切る／ワープで外す", ParTurns = 5 },
            new RoomDef { Id = "spring", Name = "癒しの泉", Kind = RoomKind.Battle, Stage = "StageA3", RewardTags = CardTag.Attack | CardTag.Draw, Flavor = "回復マス（+4・一度）は敵も取れる。槍の向きは最後の命令", ParTurns = 5 },
            new RoomDef { Id = "mirror", Name = "鏡の礼拝堂", Kind = RoomKind.Battle, Stage = "StageA4", RewardTags = CardTag.Copy, Flavor = "鏡兵は逆にまねする。柱で鏡兵の動きを止める", ParTurns = 4 },
            new RoomDef { Id = "hall", Name = "崩れた石の間", Kind = RoomKind.Battle, Stage = "StageVS", RewardTags = CardTag.Env | CardTag.Attack, Flavor = "砲兵の照準から逃げつつ剣兵をトゲへ", ParTurns = 5 },
            new RoomDef { Id = "altar", Name = "炎の祭壇", Kind = RoomKind.Danger, Stage = "StageV4", RewardTags = CardTag.Env | CardTag.Attack, Flavor = "左右が交互に燃える。爆発の予告も来る", EnemyHp = 20,
                Mission = new Mission { Kind = MissionKind.EnvironmentKill, Count = 2 } },
            new RoomDef { Id = "garrison", Name = "混成の守り", Kind = RoomKind.Danger, Stage = "StageV6", RewardTags = CardTag.Multi | CardTag.Attack, Flavor = "剣・槍・砲の3体。ワープで崩せ", EnemyHp = 18,
                Mission = new Mission { Kind = MissionKind.FriendlyKill } },
            // 【検証中】2026-10-05 RC：3つ目の危険戦（監査 BD-3：通常戦で学んだ「最後の向き」3種＋柱。HP の水増しではなく組み合わせ）。解析で今の危険戦と同じくらいの重さ
            new RoomDef { Id = "watch", Name = "見張りの柱廊", Kind = RoomKind.Danger, Stage = "StageAD1", RewardTags = CardTag.Attack | CardTag.Move, Flavor = "弓・槍・剣の3体。どれも最後の向きで撃つ。柱で射線を切り、トゲへ誘え", EnemyHp = 14,
                Mission = new Mission { Kind = MissionKind.EnvironmentKill } },
            // 【検証中】2026-10-05 RC：HP 38 → 44。報酬の抽選から基本札を外して、ボスに着くデッキが強くなった（ボット Medium のボス戦 10.3T → 9.1T・敗北 27% → 11%）。
            // 本人の「難しさはこのままでいい」に合わせて、ボスだけを地形前の値に戻した（48 ラン：Medium のボス敗北 20%）。敵全体の HP・攻撃力は変えていない
            new RoomDef { Id = "boss", Name = "鏡守の騎士", Kind = RoomKind.Boss, Stage = "StageAB", Flavor = "攻撃を反射する守護者。柱で間合い、回復陣は一度だけ", EnemyHp = 44 },
            new RoomDef { Id = "rest", Name = "静かな祠", Kind = RoomKind.Rest, Flavor = "休む（HP 回復）か、祈ってカードを1枚削除（無料）" },
            new RoomDef { Id = "shop", Name = "遺跡の商人", Kind = RoomKind.Shop, Flavor = "カード・アイテム・刻印を購入。ルーン片を払ってカードの削除も" },
            new RoomDef { Id = "event_mirror", Name = "古い鏡", Kind = RoomKind.Event, Flavor = "鏡に映るのは、まだ持っていない技" },
            new RoomDef { Id = "event_trade", Name = "崩れた祭壇", Kind = RoomKind.Event, Flavor = "祭壇は何かと引き換えに力をくれる" },
        };

        public static RoomDef Room(string id) => Rooms.First(r => r.Id == id);

        // ───── 【検証中・Copy Core V3（2026-10-05）】コピー中心のミニ ACT1 ─────
        // 本人：「コピーは簡単。でもコピーを使う場面が圧倒的に多い」。写し方は今のまま（同じ札・同じ向き・同じ距離）。
        // 敵ごとに何を写すか（全写し・技写し・歩写し）と、盤面のコピー専用の仕掛けで、コピーを「敵を操る道具」にする。
        // 7階：コピーの基礎 → 通常（技写し／対の鏡核）→ 休息/商人 → 通常（歩写し／残り）→ 危険（2つから1つ）→ 商人/休息 → ボス（写し身の主）。
        public static readonly RoomDef[] CopyRooms =
        {
            new RoomDef { Id = "cc_basic", Name = "写しの間", Kind = RoomKind.Battle, Stage = "StageC1", CopyRun = true, Pillar = "移動・攻撃", RewardTags = CardTag.Move | CardTag.Attack, ParTurns = 4,
                Flavor = "敵はあなたの札を同じ向きでまねる。壁で自分だけ止まれば、敵だけ進む" },
            new RoomDef { Id = "cc_tech", Name = "砲台の回廊", Kind = RoomKind.Battle, Stage = "StageC2", CopyRun = true, Pillar = "攻撃", RewardTags = CardTag.Attack, ParTurns = 5,
                Flavor = "手まね兵は歩かず、あなたの手の札だけまねる。反響鎧兵を斬らせろ" },
            new RoomDef { Id = "cc_sync", Name = "対の鏡核", Kind = RoomKind.Battle, Stage = "StageC3", CopyRun = true, Pillar = "同期", RewardTags = CardTag.Copy | CardTag.Attack, ParTurns = 5,
                Flavor = "二つの鏡核を同じラウンドに叩くと、敵の守りが消える。あなたとまねで同時に" },
            new RoomDef { Id = "cc_walk", Name = "運び手の間", Kind = RoomKind.Battle, Stage = "StageC4", CopyRun = true, Pillar = "移動", RewardTags = CardTag.Move | CardTag.Env, ParTurns = 5,
                Flavor = "足まね兵は移動だけまねる。赤い踏み板へ運べ" },
            new RoomDef { Id = "cc_echo", Name = "反響の砦", Kind = RoomKind.Danger, Stage = "StageCD1", CopyRun = true, Pillar = "攻撃・移動", RewardTags = CardTag.Attack | CardTag.Copy, EnemyHp = 0,
                Flavor = "反響鎧兵2体と剣兵。反響鎧兵にあなたの技は効かない。まねとトゲで崩せ", Mission = new Mission { Kind = MissionKind.FriendlyKill } },
            new RoomDef { Id = "cc_crystal", Name = "鏡晶の庭", Kind = RoomKind.Danger, Stage = "StageCD2", CopyRun = true, Pillar = "攻撃・同期", RewardTags = CardTag.Env | CardTag.Copy, EnemyHp = 0,
                Flavor = "鏡晶は敵のまねた攻撃でだけ砕ける。爆発に巻き込め", Mission = new Mission { Kind = MissionKind.EnvironmentKill, Count = 2 } },
            new RoomDef { Id = "cc_boss", Name = "写し身の主", Kind = RoomKind.Boss, Stage = "StageCB", CopyRun = true, Pillar = "同期・攻撃・移動",
                Flavor = "段階1 鏡核を同時に叩く／段階2 癒しの写し身に斬らせて砕く／段階3 まねの移動で円へ" },
        };

        public static RoomDef AnyRoom(string id) => Rooms.Concat(CopyRooms).Concat(PuzzleRooms).Concat(CombatRooms).First(r => r.Id == id);

        // ───── 【Puzzle v2（2026-10-06）】パズル中心 ACT1 v2 ─────
        // 本人：「コピーが存在する戦闘ゲーム」ではなく「コピーを使って盤面を解き、その途中で戦うゲーム」。
        // 部屋の勝ち方を全滅だけにしない（封印解除・搬送・同期・脱出）。1部屋に新しい物は1つ。答え（手順）は見せず、目的だけ見せる。
        // 8階：壁ズレ → 技まね → 移動まね → 休息/商人 → 組み合わせ＋石像 か 危険 → 同期 か 危険 → 商人/休息 → ボス。
        // 盤面・目標・説明は tools/puzzle_rooms.txt（研究 docs/research/PUZZLE_V2.md の推し：R01・R10・H08 など）。
        public static readonly RoomDef[] PuzzleRooms =
        {
            new RoomDef { Id = "pz_shift", Name = "封印石の柱", Kind = RoomKind.Battle, Stage = "StageP1", PuzzleRun = true, Pillar = "移動・攻撃", NewThing = "封印石（まね攻撃でだけ割れる）・壁ズレ", RewardTags = CardTag.Move | CardTag.Copy, ParTurns = 2, // 【Puzzle v2・パズルブレイカー】速さの目安＝最短（総当たり）＋1 と、読み手ボットの平均の間
                Flavor = "同じ命令、違う結果。柱で自分だけ止まれば、敵だけ進む" },
            new RoomDef { Id = "pz_turret", Name = "砲台の封印", Kind = RoomKind.Battle, Stage = "StageP2", PuzzleRun = true, Pillar = "攻撃", NewThing = "写し砲（技まね・一直線に撃つ）", RewardTags = CardTag.Attack | CardTag.Copy, ParTurns = 4, // 最短 2T（総当たり）・読み手 6.3T
                Flavor = "写し砲はあなたの技だけまねて撃つ。柱で止まる。届かない封印石は兵に" },
            new RoomDef { Id = "pz_turret2", Name = "技まねの番兵", Kind = RoomKind.Battle, Stage = "StageP2b", PuzzleRun = true, Pillar = "攻撃", NewThing = "技まね・反響鎧兵（2体で守り合う）", RewardTags = CardTag.Attack, ParTurns = 4, // 最短 3T・読み手 4.3T
                Flavor = "あなたの技は効かない。歩く鎧と歩かない鎧を、まねで斬り合わせろ" },
            new RoomDef { Id = "pz_carry", Name = "通路の操り手", Kind = RoomKind.Battle, Stage = "StageP3", PuzzleRun = true, Pillar = "移動", NewThing = "運び像（移動まね）・運び紋と扉", RewardTags = CardTag.Move | CardTag.Env, ParTurns = 4, // 本当の手札で 3T は千里眼でも 58%（レビュー）
                Flavor = "あなたは下の通路。上下は空振りでも、運び像は動く" },
            new RoomDef { Id = "pz_walker", Name = "壁ズレの運び手", Kind = RoomKind.Battle, Stage = "StageP3b", PuzzleRun = true, Pillar = "移動", NewThing = "運び像（移動まね）・運び紋", RewardTags = CardTag.Move, ParTurns = 2, // 最短 1T（初手の 56%）
                Flavor = "柱で止まる像と、止まらない像。2つの紋に同時に" },
            new RoomDef { Id = "pz_statue", Name = "石像の広間", Kind = RoomKind.Battle, Stage = "StageP4", PuzzleRun = true, Pillar = "攻撃", NewThing = "石像（視線・まねで回る）", RewardTags = CardTag.Copy | CardTag.Env, ParTurns = 4, // 最短 2T・読み手 5.7T
                Flavor = "敵に石像を叩かせて回せ。視線でしか届かない封印石がある" },
            new RoomDef { Id = "pz_sync", Name = "双子の封印", Kind = RoomKind.Battle, Stage = "StageP5", PuzzleRun = true, Pillar = "同期", NewThing = "鏡核（同じターンに全部・1つ以上はまね）", RewardTags = CardTag.Copy | CardTag.Multi, ParTurns = 4, // 最短 2T（突きが要る）・ふつう 3T
                Flavor = "二つの鏡核を同じターンに叩け。あなたが片方、兵のまねがもう片方" },
            new RoomDef { Id = "pz_danger1", Name = "見張りの石像", Kind = RoomKind.Danger, Stage = "StagePD1", PuzzleRun = true, Pillar = "攻撃・移動", NewThing = "巡回する石像", RewardTags = CardTag.Attack | CardTag.Copy,
                Flavor = "石像は撃つたびに回る。上の2体は互いのまねで削り、最後の1体は石像の視線で", Mission = new Mission { Kind = MissionKind.FastClear, Turns = 5 } }, // 【Puzzle v2】視線で倒すのは勝てば必ず達成されたので、速さに（レビュー）
            new RoomDef { Id = "pz_danger3", Name = "封印の行進", Kind = RoomKind.Danger, Stage = "StagePD3", PuzzleRun = true, Pillar = "移動", NewThing = "円の守り（まねの移動で円へ）", RewardTags = CardTag.Move | CardTag.Copy,
                Flavor = "守りの兵を、まねの移動で白い円へ通せ。円は1つ。先に入った兵を出してから、次の兵を入れる", Mission = new Mission { Kind = MissionKind.FriendlyKill } }, // 【Puzzle v2】危険が自分のまねだけなので「ノーダメージ」はほぼ無料だった（レビュー）
            new RoomDef { Id = "pz_danger2", Name = "二つの核と砲兵", Kind = RoomKind.Danger, Stage = "StagePD2", PuzzleRun = true, Pillar = "同期・攻撃", NewThing = "鏡核の守り（同期で守りを割る）", RewardTags = CardTag.Copy | CardTag.Multi,
                Flavor = "核を同時に叩いて守りを割ってから戦う。砲兵の照準から逃げながら", Mission = new Mission { Kind = MissionKind.NoDamage } },
            new RoomDef { Id = "pz_boss", Name = "封紋の守護者", Kind = RoomKind.Boss, Stage = "StagePB", PuzzleRun = true, Pillar = "移動・攻撃・同期", NewThing = "3段階の総合試験",
                Flavor = "段階1 主を光る紋へ運ぶ／段階2 剣の像に主を斬らせる／段階3 核2つを同じターンに" },
        };

        /// <summary>【Puzzle v2】パズル中心 ACT1 v2 の地図（8階）。1 壁ズレ → 2 技まね → 3 移動まね → 休息/商人 → 5 石像 か 危険 → 6 同期 か 危険 → 商人/休息 → ボス。</summary>
        public static List<List<RoomDef>> BuildPuzzleMap(Random rng)
        {
            RoomDef P(string id) => PuzzleRooms.First(r => r.Id == id);
            bool restFirst = rng.Next(2) == 0;
            RoomDef rest = Room("rest"), shop = Room("shop");
            List<RoomDef> Two(string a, string b) => new List<RoomDef> { P(a), P(b) }.OrderBy(_ => rng.Next()).ToList();
            string danger5 = rng.Next(2) == 0 ? "pz_danger1" : "pz_danger3";
            return new List<List<RoomDef>>
            {
                new List<RoomDef> { P("pz_shift") },
                Two("pz_turret", "pz_turret2"),
                Two("pz_carry", "pz_walker"),
                restFirst ? new List<RoomDef> { rest, shop } : new List<RoomDef> { shop, rest },
                Two("pz_statue", danger5),
                Two("pz_sync", "pz_danger2"),
                restFirst ? new List<RoomDef> { shop, rest } : new List<RoomDef> { rest, shop },
                new List<RoomDef> { P("pz_boss") },
            };
        }

        /// <summary>
        /// 【Puzzle v2】パズル中心のランで抽選に出す札：コピー中心と同じ（コピーを止める札・反転・壁蹴り・跳躍・罠設置は出さない）＋手札を回す札。
        /// 号令は「号令・改」（命令 +2・すぐ2枚・1戦1回）に置き換える（本人：命令枠だけ増えて札が無い）。
        /// </summary>
        /// 研究（2026-10-06）：7枚補充では詰まり所は「札の枚数」ではなく「3つの枠」。汎用の「2枚引く」（手繰り寄せ）・深読みは効きが薄いので出さない。
        /// 盤面の効果つき（共鳴の一歩＝一歩・余韻の刃＝斬撃）と、種類を指定して引く札（足取り・武技整理・再利用）、号令・改（枠＋2枚）。
        public static readonly CardInfo[] PuzzleRunCards =
        {
            new CardInfo { Asset = "Footwork", Rarity = Rarity.Common, Tags = CardTag.Draw | CardTag.Move, PriceOverride = 20 },
            new CardInfo { Asset = "Recycle", Rarity = Rarity.Common, Tags = CardTag.Draw | CardTag.Move, PriceOverride = 20 },
            new CardInfo { Asset = "BladeDrill", Rarity = Rarity.Common, Tags = CardTag.Draw | CardTag.Attack, PriceOverride = 20 },
            new CardInfo { Asset = "ResonantStep", Rarity = Rarity.Uncommon, Tags = CardTag.Draw | CardTag.Copy | CardTag.Move },
            new CardInfo { Asset = "EchoBlade", Rarity = Rarity.Uncommon, Tags = CardTag.Draw | CardTag.Copy | CardTag.Attack },
            new CardInfo { Asset = "RallyDraw", Rarity = Rarity.Rare, Tags = CardTag.Multi | CardTag.Draw },
            new CardInfo { Asset = "JointSlash", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Attack, EarnedOnly = true },
        };
        /// <summary>パズル中心で抽選に出す札。号令は号令・改に置き換え、忍び足（あなただけ1マス＝位置関係を札1枚で変える。研究：パズルの一番の穴）は出さない。</summary>
        public static IEnumerable<CardInfo> PuzzleRunPool => CopyRunPool.Where(c => c.Asset != "Rally" && c.Asset != "Sneak").Concat(PuzzleRunCards.Where(c => !c.EarnedOnly));

        /// <summary>コピー中心のミニ ACT1 の地図（7階）。</summary>
        public static List<List<RoomDef>> BuildCopyMap(Random rng)
        {
            RoomDef C(string id) => CopyRooms.First(r => r.Id == id);
            var second = new List<RoomDef> { C("cc_tech"), C("cc_sync") }.OrderBy(_ => rng.Next()).ToList();
            bool restFirst = rng.Next(2) == 0;
            RoomDef rest = Room("rest"), shop = Room("shop");
            return new List<List<RoomDef>>
            {
                new List<RoomDef> { C("cc_basic") },
                second,
                restFirst ? new List<RoomDef> { rest, shop } : new List<RoomDef> { shop, rest },
                new List<RoomDef> { C("cc_walk"), second[0] }.OrderBy(_ => rng.Next()).ToList(),
                new List<RoomDef> { C("cc_echo"), C("cc_crystal") }.OrderBy(_ => rng.Next()).ToList(),
                restFirst ? new List<RoomDef> { shop, rest } : new List<RoomDef> { rest, shop },
                new List<RoomDef> { C("cc_boss") },
            };
        }

        /// <summary>
        /// コピー中心のミニ ACT1 で抽選に出す札。コピーを止める札（忍び足・欠け斬り・反転斬り）と、向きを反転する札（誘い足）は出さない
        /// （本人：コピーを消すデッキが最強にならないこと／反転は頭がこんがらがる）。
        /// </summary>
        /// 罠設置も出さない（レビュー：壁際で「罠設置↓→駆け足↓→駆け足↑」を使うと、敵だけが自分の隣にトゲを置いて往復し、どの部屋も1枚で崩れた）。
        public static readonly string[] CopyRunExcluded = { "ChippedSlash", "MirrorSlash", "Lure", "WallKick", "Jump", "Trap" };
        public static IEnumerable<CardInfo> CopyRunPool => CardPool.Where(c => !c.EarnedOnly && !CopyRunExcluded.Contains(c.Asset));

        /// <summary>【Copy Core V3】コピー中心のランだけの札（号令の札：あなたは何もしない・写した敵が使う）。</summary>
        /// 研究（2026-10-05）の「ACT1 で使う12枚」：抽選7（忍び足・回転斬り・貫通・罠設置・闘気・号令・応急処置）＋刻写だけの看板札。
        /// 忍び足は「1マスの移動だけ写されない」＝技は写るので残す（研究）。反転・欠け・壁蹴り・跳躍は抽選に出さない。
        public static readonly CardInfo[] CopyRunCards =
        {
            new CardInfo { Asset = "CopyBlade", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Attack, EarnedOnly = true },
            new CardInfo { Asset = "Tailwind", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Move, EarnedOnly = true },
            new CardInfo { Asset = "Aim", Rarity = Rarity.Uncommon, Tags = CardTag.Copy | CardTag.Control, EarnedOnly = true },
        };

        /// <summary>
        /// 【Copy Core V3】コピー中心のランの役職（研究 2026-10-05：コピーを無効化する役職にしない）。名前と id は同じ、中身だけ変える。
        /// 先兵：直接攻撃 +1 をやめ、斬撃1枚 → 写し刃（同士討ち）。遺跡技師：一歩1枚 → 追い風（搬送）。鏡術師：空白の石・忍び足・欠け斬り → 狙い定め・写し刃（砲台）。
        /// </summary>
        public static RoleDef CopyRunRole(RoleDef r)
        {
            var c = new RoleDef
            {
                Id = r.Id, Name = r.Name, Description = r.Description, Playstyle = r.Playstyle, HpDelta = r.HpDelta, BaseHand = r.BaseHand,
                AddCards = r.AddCards, Imprints = r.Imprints, Items = r.Items, RemoveCards = r.RemoveCards, Favored = r.Favored, CopyHint = r.CopyHint,
            };
            switch (r.Id)
            {
                case "standard":
                    c.CopyHint = "敵を一列に並べて、何もない方へ斬ってみよう（敵が敵を斬る）。";
                    break;
                case "vanguard":
                    c.Name = "先兵（扇動）"; c.Description = "斬撃1枚が写し刃に：あなたは2、まねた敵は5で斬る（敵どうしで斬らせる）。攻撃の札が出やすい。"; c.Playstyle = "同士討ち";
                    c.AddCards = new[] { "CopyBlade" }; c.RemoveCards = new[] { "Slash" }; c.Imprints = new ImprintId[0];
                    c.CopyHint = "並べて、空振り。敵が敵を斬る。";
                    break;
                case "engineer":
                    c.Description = "刻印「棘導」から。一歩2枚が追い風（あなた1マス・まねた敵2マス）に。環境の札が出やすい。"; c.Playstyle = "搬送（トゲへ運ぶ）";
                    c.AddCards = new[] { "Tailwind", "Tailwind" }; c.RemoveCards = new[] { "Walk", "Walk" }; c.Favored = CardTag.Move | CardTag.Copy;
                    c.CopyHint = "自分は壁で止まり、敵だけトゲへ。";
                    break;
                case "mirrorist":
                    c.Description = "押し出し1枚が狙い定めに、斬撃1枚が写し刃に。まねを止める物は持たない。"; c.Playstyle = "砲台（向きを決めて撃たせる）";
                    c.AddCards = new[] { "Aim", "CopyBlade" }; c.RemoveCards = new[] { "Push", "Slash" }; c.Items = new ItemId[0];
                    c.CopyHint = "槍兵・弓兵・手まね兵の向きを決めて、仲間を撃たせる。";
                    break;
                case "observer":
                    c.CopyHint = "まねた後の結果まで見える。二つの的を同じラウンドに。";
                    break;
                case "commander":
                    c.CopyHint = "4つ目の命令も敵がまねる。まねさせて得な命令で埋める。";
                    break;
            }
            return c;
        }

        // ───── 【戦闘中心 v3（2026-10-06）】戦闘中心 ACT1 v3 ─────
        // 本人：パズル中心 v2 はコピーを使わせることには成功したが、パズル比率が高すぎて戦闘の気持ちよさが減った。
        // 「普通にカードで戦っても遊べる。でも敵のコピーを上手く使うと、1人で戦うより圧倒的に上手く戦える」へ。
        // 部屋は3種類：通常戦（全滅）・戦術戦（全滅＋仕掛け1つ）・試練（短いパズル・失敗しても負けではない）＋危険戦・ボス。仕掛けは Soft（BattleModel.SoftRules）。
        // 盤面・数値は tools/combat_rooms.txt（研究・選んだ理由は docs/research/COMBAT_ACT1_V3.md）。

        /// <summary>試練に成功したときのルーン片（戦闘より少し少ない。失敗は 0）。</summary>
        public const int TrialShards = 12;
        /// <summary>
        /// 手札整理（1戦1回・命令の枠を使わない）で捨てて引き直せる枚数。研究（2026-10-06・tools/pzsearch sim --tidy）：2 枚と 3 枚を比べて 2。
        /// 「答えは分かったのに、その種類の札が無い」を減らす保険。速さの報酬からは外さない（使っても損にしない）。
        /// </summary>
        public const int TidyMax = 2;

        public static readonly RoomDef[] CombatRooms = CombatRoomTable.All;

        /// <summary>
        /// 【戦闘中心 v3】地図（8階）：1 通常×2（入門）→ 2 通常×2 → 3 通常 か 試練 → 4 休息/商人 → 5 戦術×2 → 6 危険 か 戦術 → 7 商人/休息 → 8 ボス。
        /// どの階も2つの扉から1つ。部屋は種類ごとの山から無作為（同じ部屋は1ランに1回だけ出る）。
        /// </summary>
        public static List<List<RoomDef>> BuildCombatMap(Random rng)
        {
            List<RoomDef> Pool(Func<RoomDef, bool> f) => CombatRooms.Where(f).OrderBy(_ => rng.Next()).ToList();
            var intro = Pool(r => r.Kind == RoomKind.Battle && r.Intro);
            var combat = Pool(r => r.Kind == RoomKind.Battle && !r.Intro);
            var tactical = Pool(r => r.Kind == RoomKind.Tactical);
            var trials = Pool(r => r.Kind == RoomKind.Trial);
            var dangers = Pool(r => r.Kind == RoomKind.Danger);
            var boss = CombatRooms.First(r => r.Kind == RoomKind.Boss);
            bool restFirst = rng.Next(2) == 0;
            RoomDef rest = Room("rest"), shop = Room("shop");
            RoomDef Take(List<RoomDef> a, List<RoomDef> fallback) { var src = a.Count > 0 ? a : fallback; var x = src[0]; src.RemoveAt(0); return x; }
            List<RoomDef> Pair(RoomDef a, RoomDef b) => new List<RoomDef> { a, b }.OrderBy(_ => rng.Next()).ToList();
            var f1 = new List<RoomDef> { Take(intro, combat), Take(intro, combat) };
            var f2 = new List<RoomDef> { Take(combat, intro), Take(combat, intro) };
            var f3 = Pair(Take(combat, intro), Take(trials, combat));
            var f5 = new List<RoomDef> { Take(tactical, combat), Take(tactical, combat) };
            var f6 = Pair(Take(dangers, tactical), Take(tactical, combat));
            return new List<List<RoomDef>>
            {
                f1, f2, f3,
                restFirst ? new List<RoomDef> { rest, shop } : new List<RoomDef> { shop, rest },
                f5, f6,
                restFirst ? new List<RoomDef> { shop, rest } : new List<RoomDef> { rest, shop },
                new List<RoomDef> { boss },
            };
        }

        /// <summary>【戦闘中心 v3】抽選に出す札：パズル中心と同じ（コピーを止める札・反転・壁蹴り・跳躍・罠設置は出さない＋手札を回す札）。</summary>
        public static IEnumerable<CardInfo> CombatRunPool => PuzzleRunPool;

        /// <summary>【戦闘中心 v3】役職は3つから（本人：v3 を固めるまでは旅の剣士・先兵・観測士程度でよい。その後「コピーの使い方を変える役職」を足す）。</summary>
        public static readonly string[] CombatRoleIds = { "standard", "vanguard", "observer" };

        /// <summary>【戦闘中心 v3】役職の中身（名前と id は同じ）。観測士の初期札は先読み → 足取り（先読みは7枚補充では価値が薄い）。</summary>
        public static RoleDef CombatRole(RoleDef r)
        {
            var c = new RoleDef
            {
                Id = r.Id, Name = r.Name, Description = r.Description, Playstyle = r.Playstyle, HpDelta = r.HpDelta, BaseHand = r.BaseHand,
                AddCards = r.AddCards, Imprints = r.Imprints, Items = r.Items, RemoveCards = r.RemoveCards, Favored = r.Favored, CopyHint = r.CopyHint,
            };
            switch (r.Id)
            {
                case "standard":
                    c.Name = "旅の剣士"; c.Description = "能力なし。報酬の傾向も無い。はじめての人に。";
                    c.CopyHint = "敵を一列に並べて、何もない方へ斬ると、敵が敵を斬る。";
                    break;
                case "vanguard":
                    c.Description = "毎ターン最初の直接攻撃 +1（敵のまねには乗らない）。斬撃 +1 枚。攻撃の札が出やすい。";
                    c.CopyHint = "あなたの +1 はまねられない。反響鎧はまねで割ってから正面で殴る。";
                    break;
                case "observer":
                    c.Description = "敵がまねた後の結果まで見える（完全予測）。足取り（移動の札を2枚引く）+1 枚。まね操作の札が出やすい。";
                    c.AddCards = new[] { "Footwork" };
                    c.CopyHint = "組んだ命令を敵がどうまねるか、全部見える。読みの練習に。";
                    break;
            }
            return c;
        }

        /// <summary>兵種の短い名前（刻写の理由に出す）。</summary>
        public static string KindLabel(EnemyKind k) => k == EnemyKind.Normal ? "兵" : PhaseRules.KindName(k);

        /// <summary>
        /// ACT1 の階（各階で2つの扉から1つ選ぶ）。7部屋：通常 → 通常 → 休息/商人 → 危険/通常 → 危険/特殊 → 商人/休息 → ボス。
        /// 【検証中】2026-10-05 地図 v2（本人「1回やったらもういい」・監査：地図が実質6通り、4階は危険戦しか選べず HP 6 で入ると負けが決まる）：
        /// ・1階は {棘・泉・転移} から2つ（鏡の礼拝堂は1階に出さない：本人が最初の部屋でコピーだけで 11 ダメージ）。
        /// ・2階は {鏡・崩れた石の間・1階の残り} から2つ。その残りが4階の通常戦。
        /// ・4階と5階に危険戦が1つずつ（どちらの危険戦が先かは無作為）。危険戦は「HP に余裕がある人の賭け」（避けると報酬が減る）。
        /// 通常戦5部屋は今までどおり1回ずつ出る。盤面そのものは変えない（「同じステージは毎回同じ配置」は本人の決定）。
        /// </summary>
        public static List<List<RoomDef>> BuildMap(Random rng)
        {
            var first = new[] { "thorn", "spring", "warp" }.Select(Room).OrderBy(_ => rng.Next()).ToList();
            var second = new List<RoomDef> { Room("mirror"), Room("hall"), first[2] }.OrderBy(_ => rng.Next()).ToList();
            var dangers = Rooms.Where(r => r.Kind == RoomKind.Danger && !r.CopyRun).OrderBy(_ => rng.Next()).ToList();
            var events = Rooms.Where(r => r.Kind == RoomKind.Event && !r.CopyRun).OrderBy(_ => rng.Next()).ToList();
            bool restFirst = rng.Next(2) == 0;
            RoomDef rest = Room("rest"), shop = Room("shop");
            var map = new List<List<RoomDef>>
            {
                new List<RoomDef> { first[0], first[1] },
                new List<RoomDef> { second[0], second[1] },
                restFirst ? new List<RoomDef> { rest, shop } : new List<RoomDef> { shop, rest },
                new List<RoomDef> { dangers[0], second[2] },
                new List<RoomDef> { dangers[1 % dangers.Count], events[0] },
                restFirst ? new List<RoomDef> { shop, rest } : new List<RoomDef> { rest, shop },
                new List<RoomDef> { Room("boss") },
            };
            return map;
        }

        /// <summary>
        /// 戦闘に勝ったときのルーン片（報酬は戦闘の終わりに確定。戦闘中に稼ぎ続ける方式にはしない）。
        /// 経緯：案D で通常 18→15・ボス 60→0（ACT1 の中で使い道が無い）→ 【検証中】2026-10-05 本人プレイ「ルーン片が集まらず商人で何も買えない」→ 通常 20・危険 35・ボス 0（ACT1 の中では使えないため）。
        /// これに撃破 +2/体（RC で 3→2）・無傷 +5・速さ +6・ミッション 20。最初の商人で「新しいカード1枚」か「回復薬＋削除」が買える額（約 50）。
        /// RC の解析（旅立ち・撃破が重なって多すぎた）→ 通常 16・危険 30。
        /// </summary>
        public static int ShardsFor(RoomKind kind) => kind == RoomKind.Danger ? 30 : kind == RoomKind.Boss ? 0 : kind == RoomKind.Tactical ? 20 : kind == RoomKind.Trial ? TrialShards : 16; // 【戦闘中心 v3】戦術戦 20・試練（成功）12
    }
}
