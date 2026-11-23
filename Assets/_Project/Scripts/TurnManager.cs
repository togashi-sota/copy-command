using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    public enum BattleState
    {
        PlayerInput,      // PLAYER TURN：命令を登録中
        PlayerExecuting,  // PLAYER TURN：命令を実行中
        EnemyExecuting,   // ENEMY TURN：コピーした命令を実行中
        Cleared,          // 敵を全員倒した
        GameOver,         // プレイヤーの HP が 0
    }

    /// <summary>手札の方式（比較用のデバッグ切替）。Fixed がステージ本来の固定の手札。</summary>
    public enum HandMode
    {
        Fixed,  // 固定の手札（そのステージのカードが全部並ぶ・同じカードは1ターン1回）
        DeckA,  // デッキ＋A 案（毎ターン2枚ドロー）
        DeckB,  // デッキ＋B 案（3枚未満なら3枚まで＋1枚）
        DeckLarge3,    // 【検証中】大きい手札・定量（初手8・上限10・毎ターン3枚）
        DeckLargeUsed, // 【検証中】大きい手札・使った分＋1（初手8・上限10）
        DeckMid3,      // 【検証中】中くらいの手札・定量（初手7・上限9・毎ターン3枚）。大きい検証デッキを使う
        DeckLarge12,   // 【検証中】大きい手札・上限12（初手8・毎ターン3枚）
        DeckSeven,     // 【検証中・見本ステージの標準】7枚補充（初手7・毎ターン7枚まで補充）。テーマつきのステージで「固定」のときはこれを使う
    }

    /// <summary>
    /// 戦闘の進行役。ステージを読み込み、命令スロットを管理し、PLAYER TURN と ENEMY TURN を切り替える。
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        [SerializeField] List<StageData> stages = new List<StageData>();
        [SerializeField] int startStageIndex = 0;
        [SerializeField] GridManager grid;

        [Header("テンポ（仮値）")]
        [SerializeField] float commandInterval = 0.5f;     // 命令1つごとの間
        [SerializeField] float turnChangeInterval = 0.6f;  // PLAYER TURN → ENEMY TURN の間

        public StageData CurrentStage { get; private set; }
        public int CurrentStageIndex { get; private set; }
        public IReadOnlyList<StageData> Stages => stages;
        public GridManager Grid => grid;
        public UnitController Player { get; private set; }
        /// <summary>敵（実行順：敵A, 敵B, …）。</summary>
        public IReadOnlyList<UnitController> Enemies => enemies;
        /// <summary>1体目の敵（敵が1体のステージ用）。</summary>
        public UnitController Enemy => enemies.Count > 0 ? enemies[0] : null;
        /// <summary>敵を全員倒したか。</summary>
        public bool AllEnemiesDead => enemies.TrueForAll(e => !e.IsAlive || e.Model.IsObject); // 写し砲などの物は数えない（Copy Core V2）
        /// <summary>【Puzzle v2】勝った（討伐は全滅、ほかは部屋の目標）。</summary>
        public bool Won => grid != null && grid.Model != null ? PuzzleRules.Won(grid.Model) : AllEnemiesDead;
        /// <summary>【Puzzle v2】あなたの攻撃がはじかれた理由（その場の一言。画面は数秒だけ出す）。</summary>
        public string LastHint { get; private set; }
        public int LastHintSerial { get; private set; }
        /// <summary>ENEMY TURN で今行動している敵（いなければ null）。</summary>
        public UnitController ActingEnemy { get; private set; }
        /// <summary>この戦闘で起きた出来事（ダメージの原因など。将来のルーン片・任意ミッション・ログ用）。</summary>
        public IReadOnlyList<BattleEvent> BattleEvents => battleEvents;

        public BattleState State { get; private set; }
        public int TurnNumber { get; private set; }
        /// <summary>【RC6】敵（物を除く）の HP が1も減らなかったターンが何回続いたか（詰まったときのヒント）。</summary>
        public int TurnsSinceEnemyHurt { get; private set; }
        int enemyHpAtTurnStart = -1;
        /// <summary>命令スロット（プレイヤーがこのターンに実行する命令）。</summary>
        public IReadOnlyList<CommandRecord> PlannedCommands => plannedCommands;
        /// <summary>いま実行中の命令の番号（0〜2）。実行中でなければ -1。</summary>
        public int ExecutingIndex { get; private set; } = -1;
        public IReadOnlyList<string> Log => log;
        /// <summary>敵の命令コピー係。</summary>
        public EnemyReplayController EnemyReplay { get; } = new EnemyReplayController();
        /// <summary>【RC9】③まねで今まねている命令（ほかの時は null）。</summary>
        public CommandRecord CurrentCopy { get; private set; }

        /// <summary>
        /// Enemy Preview に出す「敵がこのあと実行する命令」。
        /// PLAYER TURN 中はいま登録中のスロット（＝このターンが終わると敵にコピーされる内容）、
        /// ENEMY TURN 中はコピー済みの命令。
        /// </summary>
        public IReadOnlyList<CommandRecord> EnemyPreviewCommands =>
            State == BattleState.EnemyExecuting ? EnemyReplay.CopiedCommands : CopiedSubset();

        /// <summary>
        /// 命令の上限。基本はステージの値（3）。【検証中】ACT1：刻印「第四命令」など（BonusSlots）・アイテム「命令石」（このターンだけ TempSlots）・
        /// カード「号令」（この札も1枠使い、上限 +range）。増えた命令は敵もコピーする（強くなるほど敵に渡す命令も増える）。
        /// </summary>
        public int MaxSlots => (CurrentStage != null ? CurrentStage.maxSlots : 3) + BonusSlots + TempSlots + RallySlots;

        // ───────── 【検証中】ACT1 のビルド用（命令枠・追加ドロー・コピーしない命令）。ふつうのステージでは全部 0 ─────────

        /// <summary>恒久の命令枠の増加（刻印「第四命令」・役職など）。</summary>
        public int BonusSlots { get; set; }
        /// <summary>【検証中】ACT1 の観測士：完全予測をこの戦闘だけ強制で見せる。</summary>
        public bool ActFullPrediction { get; private set; }
        /// <summary>このターンだけの命令枠の増加（アイテム「命令石」）。次の PLAYER TURN で 0 に戻る。</summary>
        public int TempSlots { get; private set; }
        int RallySlots => plannedCommands.Where(r => r.Command.type == CommandType.Rally).Sum(r => Mathf.Max(1, r.Command.range));
        /// <summary>このターン、敵にコピーさせない命令の番号（アイテム「空白の石」）。</summary>
        public IReadOnlyCollection<int> UncopiedSlots => uncopiedSlots;
        readonly HashSet<int> uncopiedSlots = new HashSet<int>();
        /// <summary>次のターンの始めに追加で引く枚数（カード「再考」）。</summary>
        public int PendingDraw { get; private set; }
        int resonateDraw, copyKillDraw;
        bool copyGimmickThisRound, copyKillThisRound;
        /// <summary>【Puzzle v2・解析／刻写】この戦闘で、敵のまねで仕掛けが動いた回数（封印石・石像・鏡核・扉・箱・写し身など）。</summary>
        public int CopyGimmicksThisBattle { get; private set; }
        /// <summary>次のターンの始めに「山札の上3枚から1枚」を選ぶ回数（カード「先読み」）。</summary>
        public int PendingForesight { get; private set; }
        /// <summary>毎ターン、あなたの最初の直接攻撃に足すダメージ（役職「先兵」）。</summary>
        public int PlayerAttackBonusEachTurn { get; set; }
        /// <summary>ラウンドの終わりに呼ばれる（そのラウンドの出来事, 使った命令の数）→ 次のターンに追加で引く枚数（刻印「反響」「余白」）。</summary>
        public System.Func<IReadOnlyList<BattleEvent>, int, int> RoundEndDrawBonus { get; set; }

        /// <summary>この番号の命令は敵にコピーされるか。</summary>
        public bool IsCopied(int index) => index < plannedCommands.Count && !plannedCommands[index].Command.notCopied && !uncopiedSlots.Contains(index);

        /// <summary>敵に渡す命令（コピーされない命令を除いた並び）。</summary>
        List<CommandRecord> CopiedSubset()
        {
            if (!grid.Model.CopyEnabled) return new List<CommandRecord>();
            var list = EnemyReplayController.ToCopied(plannedCommands, uncopiedSlots, grid.Model.CopyMode);
            TurnRules.ApplyCopyMoveBonus(grid.Model, list); // 【Combat V4】操糸・重し（予告にも出る）
            return list;
        }

        void ResetActModifiers()
        {
            BonusSlots = 0;
            TempSlots = 0;
            uncopiedSlots.Clear();
            PendingDraw = 0;
            PendingForesight = 0;
            LockedCommands = 0;
            resonateDraw = 0;
            copyKillDraw = 0;
            PendingRecycle = 0;
            copyGimmickThisRound = false;
            copyKillThisRound = false;
            CopyGimmicksThisBattle = 0;
            CardsDrawnByEffects = 0;
            PlayerAttackBonusEachTurn = 0;
            RoundEndDrawBonus = null;
            TidyMax = 0;
            TidyUsed = false;
            Tactics = null; TacticPlaysThisTurn = 0; echoDraw = 0; pendingSiftCard = null; // 【Combat V4】
            TacticsUsedThisBattle = 0; tacticPlayBonus = 0; PreviewAhead = false; BattleSerial++;
        }

        // ───────── 【Combat V4】戦術札（緑）：別のデッキ・命令枠を使わない・敵はコピーしない ─────────

        /// <summary>戦術札のデッキ（V4 の戦闘だけ。無ければ null）。</summary>
        public TacticDeck Tactics { get; private set; }
        /// <summary>このターンに使った戦術札の枚数（上限 TacticRules.PlaysPerTurn）。</summary>
        public int TacticPlaysThisTurn { get; private set; }
        /// <summary>この戦闘で使った戦術札の枚数（記録用）。</summary>
        public int TacticsUsedThisBattle { get; private set; }

        /// <summary>【RC3】チュートリアルの「できた！」の間は操作を止める（勝ちの画面は出さない）。</summary>
        public void PauseForTutorial() { State = BattleState.Cleared; }
        /// <summary>V4 のルール（カウント大技・敵の積極的な動き）の戦闘か。</summary>
        public bool IsV4 => grid != null && grid.Model != null && grid.Model.V4;
        int echoDraw;
        CommandData pendingSiftCard;
        /// <summary>選別（戦術札）で、捨てる札を選んでいる最中か（選び終わるまで札は使わない）。</summary>
        public bool PendingSift => pendingSiftCard != null;
        readonly List<string> usedTacticNames = new List<string>();
        /// <summary>この戦闘で使った戦術札の名前（記録用）。</summary>
        public IReadOnlyList<string> UsedTacticNames => usedTacticNames;

        /// <summary>1ターンに使える戦術札の枚数（既定 2＋役職「指揮官」の +1）。</summary>
        public int TacticPlayLimit => TacticRules.PlaysPerTurn + tacticPlayBonus;
        int tacticPlayBonus;
        /// <summary>【Combat V4】観測士：大技の範囲を残り②から薄く見せる。</summary>
        public bool PreviewAhead { get; private set; }
        /// <summary>戦闘を読み込むたびに増える（登場演出などの切り替え用）。</summary>
        public int BattleSerial { get; private set; }

        public bool CanPlayTactic(CommandData card) =>
            Tactics != null && card != null && State == BattleState.PlayerInput && TacticPlaysThisTurn < TacticPlayLimit && Tactics.Hand.Contains(card);

        public bool CanUseTactic(CommandData card, UnitController t1 = null, UnitController t2 = null) =>
            CanPlayTactic(card) && !PendingSift && TacticRules.CanUse(grid.Model, card.tactic, t1 != null ? t1.Model : null, t2 != null ? t2.Model : null);

        /// <summary>戦術札を使う。使えたら true。選別は捨てる札を選び終わる（SiftCards）まで札を消費しない。</summary>
        public bool PlayTactic(CommandData card, UnitController t1 = null, UnitController t2 = null)
        {
            if (!CanUseTactic(card, t1, t2)) return false;
            if (card.tactic == TacticKind.Sift)
            {
                if (Deck == null || UnplannedHandCount == 0) return false;
                pendingSiftCard = card;
                return true;
            }
            var ev = new List<BattleEvent>();
            string text = TacticRules.Apply(grid.Model, card.tactic, t1 != null ? t1.Model : null, t2 != null ? t2.Model : null, ev, out var he);
            ConsumeTactic(card, text, ev);
            if (he.DrawCommands > 0 && Deck != null) { var drawn = Deck.Draw(he.DrawCommands); MarkDrawn(drawn); }
            if (he.ExtraSlots > 0) TempSlots += he.ExtraSlots;
            if (he.Recall && Deck != null && Deck.DiscardPile.Count > 0) { var last = Deck.DiscardPile[Deck.DiscardPile.Count - 1]; if (Deck.ReturnFromDiscard(last)) MarkDrawn(new[] { last }); }
            if (he.EchoDraw > 0) echoDraw += he.EchoDraw;
            return true;
        }

        void ConsumeTactic(CommandData card, string text, List<BattleEvent> ev)
        {
            Tactics.Use(card);
            TacticPlaysThisTurn++;
            TacticsUsedThisBattle++;
            usedTacticNames.Add(card.displayName);
            AddLog("戦術札：" + text);
            battleEvents.AddRange(ev);
            grid.Presenter.Play(ev);
        }

        /// <summary>選別：捨てる札（命令に入れていない手札から最大 TidyMax 枚・既定 2）を渡す。選んだら戦術札を使ったことになる。</summary>
        public int SiftCards(IEnumerable<CardInstance> cards)
        {
            if (pendingSiftCard == null || Deck == null || cards == null) return 0;
            var drop = cards.Where(c => c != null && Deck.Hand.Contains(c) && !IsCardPlanned(c)).Distinct().Take(2).ToList();
            if (drop.Count == 0) return 0;
            var keep = new HashSet<CardInstance>(Deck.Hand.Where(c => !drop.Contains(c)));
            int n = Deck.DiscardHandExcept(keep);
            foreach (var c in drop) retainedCards.Remove(c);
            var drawn = Deck.Draw(n);
            MarkDrawn(drawn);
            var ev = new List<BattleEvent> { new BattleEvent { Type = BattleEventType.TacticPlayed, Text = TacticRules.Name(TacticKind.Sift) } };
            var card = pendingSiftCard;
            pendingSiftCard = null;
            ConsumeTactic(card, $"選別：{string.Join("・", drop.Select(c => c.Data.displayName))} を捨てて {drawn.Count} 枚引いた", ev);
            return n;
        }

        /// <summary>選別をやめる（札は使わない）。</summary>
        public void CancelSift() { pendingSiftCard = null; }

        // ───────── 【戦闘中心 v3】手札整理（1戦1回・命令の枠を使わない） ─────────

        /// <summary>手札整理で捨てられる枚数（0 なら使えない）。ACT の戦闘で ActBattleSetup から。</summary>
        public int TidyMax { get; private set; }
        /// <summary>この戦闘で手札整理を使ったか。</summary>
        public bool TidyUsed { get; private set; }
        /// <summary>今、手札整理を使えるか（あなたの番・まだ使っていない）。</summary>
        public bool CanTidy => IsDeckMode && TidyMax > 0 && !TidyUsed && State == BattleState.PlayerInput;

        /// <summary>
        /// 手札整理：手札から（命令に入れていない札を）最大 TidyMax 枚捨てて、同じ枚数引く。捨てた枚数を返す（使えなければ 0）。
        /// 「答えは分かったのに、その種類の札が無い」の保険（本人とChatGPTの提案・2026-10-06）。
        /// </summary>
        public int Tidy(IEnumerable<CardInstance> cards)
        {
            if (!CanTidy || cards == null) return 0;
            var drop = cards.Where(c => c != null && Deck.Hand.Contains(c) && !IsCardPlanned(c)).Distinct().Take(TidyMax).ToList();
            if (drop.Count == 0) return 0;
            var keep = new HashSet<CardInstance>(Deck.Hand.Where(c => !drop.Contains(c)));
            int n = Deck.DiscardHandExcept(keep);
            foreach (var c in drop) retainedCards.Remove(c);
            var drawn = Deck.Draw(n);
            MarkDrawn(drawn);
            TidyUsed = true;
            AddLog($"手札整理：{string.Join("・", drop.Select(c => c.Data.displayName))} を捨てて {drawn.Count} 枚引いた（この戦闘ではもう使えない）");
            return n;
        }

        /// <summary>
        /// 【RC4】アイテム「転移石」：命令を組む前に、あなたを cell へ移す（命令ではない・まねされない・枠を使わない）。できたら true。
        /// 赤（ターンの初めに決まった敵の攻撃）は変わらない：赤から出る・敵の隣から離れるための救済。
        /// </summary>
        public bool WarpPlayer(Vector2Int cell)
        {
            if (State != BattleState.PlayerInput || plannedCommands.Count > 0 || grid.Model == null) return false;
            var ev = new List<BattleEvent>();
            string text = RescueRules.Warp(grid.Model, cell, ev);
            if (text == null) return false;
            AddLog(text);
            battleEvents.AddRange(ev);
            grid.Presenter.Play(ev);
            return true;
        }

        /// <summary>アイテム「命令石」：このターンだけ命令の上限 +n。</summary>
        public void AddTempSlots(int n) { if (State == BattleState.PlayerInput) TempSlots += n; }

        /// <summary>【検証中】砂時計で引き直せる札（命令に入れていない手札）の枚数。</summary>
        public int UnplannedHandCount => Deck == null ? 0 : Deck.Hand.Count(c => !IsCardPlanned(c));

        /// <summary>アイテム「空白の石」：この番号の命令を敵にコピーさせない（このターンだけ）。</summary>
        public bool MarkUncopied(int index)
        {
            if (State != BattleState.PlayerInput || index < 0 || index >= plannedCommands.Count) return false;
            return uncopiedSlots.Add(index);
        }

        /// <summary>アイテム「回復薬」など：あなたの HP を回復。</summary>
        public int HealPlayer(int amount)
        {
            int healed = Player.Model.Heal(amount);
            Player.ShowHeal(healed);
            AddLog($"あなたの HP が {healed} 回復（{Player.Hp}/{Player.MaxHp}）");
            return healed;
        }

        /// <summary>追加で引く（手札の安全上限まで）。</summary>
        public int DrawExtra(int count)
        {
            if (Deck == null || count <= 0) return 0;
            var drawn = Deck.Draw(count);
            MarkDrawn(drawn);
            return drawn.Count;
        }

        /// <summary>アイテム「選別の砂時計」：命令に入れていない手札を全部捨てて、同じ枚数を引き直す。</summary>
        public int Mulligan()
        {
            if (Deck == null || State != BattleState.PlayerInput) return 0;
            var keep = new HashSet<CardInstance>(plannedCommands.Where(r => r.SourceCard != null).Select(r => r.SourceCard));
            int n = Deck.DiscardHandExcept(keep);
            DrawExtra(n);
            AddLog($"選別の砂時計：手札 {n} 枚を引き直した");
            return n;
        }

        /// <summary>アイテム「回収の鉤」：捨て札の1枚を手札へ。</summary>
        public bool ReturnFromDiscard(CardInstance card)
        {
            if (Deck == null || !Deck.ReturnFromDiscard(card)) return false;
            MarkDrawn(new[] { card });
            AddLog($"回収の鉤：「{card.Data.displayName}」を捨て札から手札へ");
            return true;
        }

        /// <summary>カード「先読み」：選べる山札の上のカード（最大3枚）。</summary>
        public IReadOnlyList<CardInstance> ForesightChoices => Deck == null || PendingForesight <= 0 || State != BattleState.PlayerInput ? new List<CardInstance>() : Deck.PeekTop(3);

        /// <summary>先読みで1枚選ぶ（null なら選ばない）。</summary>
        public void ChooseForesight(CardInstance card)
        {
            if (PendingForesight <= 0) return;
            PendingForesight--;
            if (card != null && Deck.TakeFromDrawPile(card))
            {
                MarkDrawn(new[] { card });
                AddLog($"先読み：「{card.Data.displayName}」を手札へ");
            }
        }

        /// <summary>【検証中】ACT1 の戦闘を読み込む（HP・デッキ・命令枠・刻印の効果を持ち込む）。手札は 7枚補充（基本 7・追加ドロー可）。</summary>
        /// <summary>【完成版】次の SetupBattle で使う開始配置（部屋ごとの乱数）。使ったら 0 に戻す。</summary>
        int pendingLayout;
        /// <summary>【完成版】今の戦闘の開始配置の番号（0＝基本・1 以上＝別案）。</summary>
        public int LayoutIndex { get; private set; }

        public void LoadActBattle(StageData stage, ActBattleSetup setup)
        {
            Deck = null;
            BasicWalk = null;
            pendingLayout = setup.Layout;
            SetupBattle(stage, setup.Hp, setup.MaxHp);
            grid.Model.ApplyChallenge(setup.Challenge); // 【完成版】挑戦の段階（解析の BattleModel と同じ）
            grid.Model.ApplyPower(setup.RedDelta, setup.ClockDelta, setup.LastStand); // 【RC6】階ごとの敵の強さ・最後の1体
            grid.Model.EscalateAfter = setup.EscalateAfter; // 【RC12】激化（目安のターンを過ぎると敵の攻撃 +1）
            grid.Model.LastStandDouble = setup.LastStandDouble; // 【RC6】孤立
            grid.Model.CopyEdgeBonus = setup.CopyEdgeBonus; grid.Model.MirrorGuard = setup.MirrorGuard; grid.Model.ClockHandBonus = setup.ClockHandBonus; grid.Model.TrapBonus = setup.TrapBonus; // 【RC7】刻印
            ResetActModifiers(); // 前の戦闘の「次のターン用の効果」（先読み・再考・命令石・空白の石）を持ち越さない
            BonusSlots = setup.BonusSlots;
            PlayerAttackBonusEachTurn = setup.AttackBonusEachTurn;
            RoundEndDrawBonus = setup.RoundEndDrawBonus;
            grid.Model.SpikeFirstBonus = setup.SpikeFirstBonus;
            ActFullPrediction = setup.FullPrediction;
            TidyMax = setup.TidyMax; // 【戦闘中心 v3】
            if (setup.NoClocks) foreach (var e in grid.Model.Enemies) e.Clocks.Clear(); // 【RC3】はじめての1階：大技なし
            grid.Model.NoTraits = setup.NoTraits; // 【RC3】はじめての戦闘：敵は まねるだけ
            if (grid.Model.V4 && setup.TacticCards != null && setup.TacticCards.Count > 0)
            {
                Tactics = new TacticDeck(new System.Random(setup.Seed * 17 + 3)) { HandMax = TacticRules.HandMax + setup.TacticPlayBonus }; // 【Combat V4】【RC6】指揮官は緑の札を1枚多く持てる
                Tactics.StartBattle(setup.TacticCards);
                tacticPlayBonus = setup.TacticPlayBonus;
                PreviewAhead = setup.PreviewAhead;
            }
            Deck = new DeckState(setup.Rng, handSize: setup.BaseHand, policy: DrawPolicy.RefillSeven) { BaseHand = setup.BaseHand };
            if (setup.Forged != null) Deck.ForgedNames = new HashSet<string>(setup.Forged); // 【完成版】鍛えた札
            Deck.StartBattle(setup.Deck);
            MarkDrawn(Deck.LastDrawn);
            AddLog($"手札：基本 {setup.BaseHand} 枚（毎ターン {setup.BaseHand} 枚まで補充・追加ドローで増やせる）。命令は {MaxSlots} つまで（敵も同じ数をまねる）");
        }

        /// <summary>
        /// 手札の方式（デバッグ切替）。DeckA / DeckB にすると、ステージを「デッキから引く」方式で遊べる（手札方式の比較用）。
        /// 変えたらステージを読み込み直す。STAGE 1〜3 の本来の遊び方は Fixed。
        /// </summary>
        public HandMode CurrentHandMode { get; set; } = HandMode.Fixed;
        /// <summary>【検証中】今のステージで実際に使う手札方式（テーマつきの見本ステージで「固定」なら 7枚補充）。</summary>
        public HandMode ActiveHandMode => autoSevenForThemed && CurrentHandMode == HandMode.Fixed && CurrentStage != null && CurrentStage.visualTheme != null ? HandMode.DeckSeven : CurrentHandMode;

        /// <summary>このステージの敵の固有行動（無ければ null）。</summary>
        public CommandData EnemyInnateAction => CurrentStage != null ? CurrentStage.enemyInnateAction : null;
        /// <summary>ENEMY TURN で固有行動を実行中か（Enemy Preview の強調表示用）。</summary>
        public bool IsExecutingInnate { get; private set; }
        /// <summary>このターンの固有行動がまだ実行されていないか（盤面の危険マス表示用）。</summary>
        public bool InnatePending => EnemyInnateAction != null
            && (State == BattleState.PlayerInput || State == BattleState.PlayerExecuting || (State == BattleState.EnemyExecuting && !innateDoneThisTurn));

        /// <summary>次の固有行動が当たるマス（生きている敵それぞれから上下左右へ range マス。盤面外と壁で止まる）。</summary>
        public List<Vector2Int> GetInnateDangerCells()
        {
            var cells = new List<Vector2Int>();
            if (EnemyInnateAction == null) return cells;
            foreach (var e in enemies)
                if (e.IsAlive)
                    foreach (var c in CommandExecutor.CrossCells(grid, e.GridPosition, Mathf.Max(1, EnemyInnateAction.range)))
                        if (!cells.Contains(c)) cells.Add(c);
            return cells;
        }

        /// <summary>
        /// Enemy Preview：この敵がこのあと実行する命令（コピー特性を反映した並び）。
        /// PLAYER TURN 中は登録中のスロットから、ENEMY TURN 中はコピー済みの命令から作る。
        /// </summary>
        public IReadOnlyList<CommandRecord> GetPreviewFor(UnitController enemy)
        {
            // 【Copy Core V2】「すぐ」の方式：命令ごとの形（あなたへ向ける方式は今の位置で）
            if (CopyCore.IsImmediate(grid.Model.CopyMode))
            {
                var list = new List<CommandRecord>();
                if (!grid.Model.CopyEnabled) return list;
                var source = immediatePlan ?? plannedCommands;
                for (int i = 0; i < source.Count; i++)
                {
                    if (uncopiedSlots.Contains(i) && immediatePlan == null) continue;
                    var c = CopyCore.CopyOne(source[i], enemy.Model, Player.Model, grid.Model.CopyMode);
                    if (c != null) list.Add(c);
                }
                return list;
            }
            if (CopyCore.Stunned(enemy.Model)) return new List<CommandRecord>();
            var batch = State == BattleState.EnemyExecuting ? EnemyReplay.CopiedCommands : (IReadOnlyList<CommandRecord>)CopiedSubset();
            return CopyKinds.Filter(enemy.Model, EnemyReplayController.ApplyTrait(batch, enemy.Model.CopyTrait));
        }

        /// <summary>【Copy Core V2】「すぐ」の方式で実行中の命令列（表示用）。</summary>
        List<CommandRecord> immediatePlan;

        // ───────── RUN モード（デッキ） ─────────

        /// <summary>RUN モードのデッキ（チュートリアルでは null）。</summary>
        public DeckState Deck { get; private set; }
        public bool IsDeckMode => Deck != null;
        /// <summary>RUN モードの基本命令「歩く」（カードではない。何回でも使えるが、スロットは1枠使う）。</summary>
        public CommandData BasicWalk { get; private set; }
        /// <summary>このターン終了時に手札に残す（保留する）カード。最大 Deck.MaxRetain 枚。</summary>
        public IReadOnlyCollection<CardInstance> RetainedCards => retainedCards;

        readonly HashSet<CardInstance> retainedCards = new HashSet<CardInstance>();

        readonly List<UnitController> enemies = new List<UnitController>();
        readonly List<BattleEvent> battleEvents = new List<BattleEvent>();
        readonly List<CommandRecord> plannedCommands = new List<CommandRecord>();
        readonly List<string> log = new List<string>();
        CommandExecutor executor;
        bool instant; // テスト用：待ち時間なしで進める
        bool innateDoneThisTurn;

        // 【検証用】プレイテストの自動記録（PlaytestRecorder。本番機能ではない）
        readonly List<string> usedCardNames = new List<string>();
        /// <summary>この戦闘で使ったカードの表示名（ACT1 の結果画面・プロフィール用）。</summary>
        public IReadOnlyList<string> UsedCardNames => usedCardNames;
        /// <summary>【RC7】ターンごとの記録（プレイ記録の Markdown 用）：「T3：斬撃→・一歩↑　HP 18→12（砲王の大技（赤紫） 6）」。</summary>
        readonly List<string> turnTrace = new List<string>();
        public IReadOnlyList<string> TurnTrace => turnTrace;
        string traceHead; int traceStart = -1, traceHp;
        /// <summary>【RC8】このターン（最後に始まったターン）の出来事の始まりの番号。結果の「最後のターンに受けた」に使う。</summary>
        public int TurnEventStart => lastTurnStart;
        /// <summary>【RC10・初見テスト2回目「次のターンに敵が1マス動いている理由が分からない」】このターンの⑤で敵が自分で動いた分（右下の「前のターンに起きたこと」）。</summary>
        public readonly List<BattleEvent> OwnMoveLog = new List<BattleEvent>();
        int lastTurnStart;
        /// <summary>【RC7】記録中のターンを1行にする（ターンの終わり・戦闘の終わり）。</summary>
        public void FlushTurnTrace()
        {
            if (traceStart < 0 || traceHead == null) return;
            var hits = new List<BattleEvent>();
            for (int i = traceStart; i < battleEvents.Count; i++)
            {
                var e = battleEvents[i];
                if (e.Type == BattleEventType.Damaged && e.Unit != null && e.Unit.IsPlayer && e.Amount > 0) hits.Add(e);
            }
            string dmg = hits.Count == 0 ? "" : "（" + string.Join("・", hits.GroupBy(TurnForecast.CauseText).Select(g => $"{g.Key} {g.Sum(e => e.Amount)}")) + "）";
            int hpNow = Player != null && Player.Model != null ? Mathf.Max(0, Player.Model.Hp) : 0;
            turnTrace.Add($"{traceHead}　HP {traceHp}→{hpNow}{dmg}");
            traceStart = -1; traceHead = null;
        }
        readonly List<Vector2Int> roundEndPositions = new List<Vector2Int>();
        int battleStartHp;
        bool battleRecorded = true;

        void OnApplicationQuit()
        {
            if (TurnNumber > 1) RecordPlaytest("中断（ゲームを終了した）");
        }

        void RecordPlaytest(string result)
        {
            if (battleRecorded) return;
            battleRecorded = true;
            PlaytestRecorder.Record(this, result, usedCardNames, roundEndPositions, battleStartHp);
        }

        void Start()
        {
            LoadStage(startStageIndex);
        }

        // ───────── ステージ ─────────

        public void LoadStage(int index)
        {
            if (stages.Count == 0) { Debug.LogError("TurnManager にステージが登録されていません"); return; }
            CurrentStageIndex = Mathf.Clamp(index, 0, stages.Count - 1);
            LoadStage(stages[CurrentStageIndex]);
        }

        /// <summary>
        /// ステージを読み込む。手札の方式が Fixed なら今までどおり固定の手札。
        /// DeckA / DeckB なら、そのステージの比較用デッキから引く（同じステージなら毎回同じシャッフル順で、A と B を公平に比べられる）。
        /// </summary>
        public void LoadStage(StageData stage)
        {
            Deck = null;
            BasicWalk = null;
            ResetActModifiers();
            SetupBattle(stage, stage.playerMaxHp, stage.playerMaxHp);
            // 【検証中】テーマつきの見本ステージ（VS・V4・VB）は、手札が「固定」のままなら 7枚補充 で遊ぶ（完成形の手札）
            var mode = ActiveHandMode;
            if (mode == HandMode.Fixed) return;

            var policy = PolicyOf(mode);
            bool large = policy == DrawPolicy.LargeDrawThree || policy == DrawPolicy.LargeUsedPlusOne || policy == DrawPolicy.MidDrawThree || policy == DrawPolicy.LargeTwelve || policy == DrawPolicy.RefillSeven;
            int handSize = policy == DrawPolicy.MidDrawThree || policy == DrawPolicy.RefillSeven ? DeckState.MidOpening : large ? DeckState.LargeOpening : DeckState.DefaultHandSize;
            Deck = new DeckState(new System.Random(StableSeed(stage.name)), handSize: handSize, policy: policy);
            // 【検証中】大きい手札のときは、ステージに大きい検証デッキがあればそれを使う（M4 以降）
            var deckCards = large && stage.handTestDeckLarge != null && stage.handTestDeckLarge.Count > 0 ? stage.handTestDeckLarge : stage.GetHandTestDeck();
            int opening = large ? 0 : stage.handTestOpeningCount;
            bool keepOrder = !large && stage.handTestKeepOrder;
            Deck.StartBattle(deckCards, opening, keepOrder);
            MarkDrawn(Deck.LastDrawn);
            if (stage.combatRoom) TidyMax = ActContent.TidyMax; // 【戦闘中心 v3】研究室の部屋でも手札整理を試せる（ACT と同じ）
            string fixedText = keepOrder ? "（検証用：初手と山札の順番を固定）"
                             : opening > 0 ? $"（検証用：初手 {opening} 枚を固定）" : "";
            AddLog($"手札方式 {HandModeName(mode)}：デッキ {Deck.DrawPile.Count + Deck.Hand.Count} 枚から{Deck.Hand.Count}枚引いた{fixedText}");
        }

        public static DrawPolicy PolicyOf(HandMode mode)
        {
            switch (mode)
            {
                case HandMode.DeckA: return DrawPolicy.DrawTwo;
                case HandMode.DeckLarge3: return DrawPolicy.LargeDrawThree;
                case HandMode.DeckLargeUsed: return DrawPolicy.LargeUsedPlusOne;
                case HandMode.DeckMid3: return DrawPolicy.MidDrawThree;
                case HandMode.DeckLarge12: return DrawPolicy.LargeTwelve;
                case HandMode.DeckSeven: return DrawPolicy.RefillSeven;
                default: return DrawPolicy.FloorThreePlusOne;
            }
        }

        public static string HandModeName(HandMode mode)
        {
            switch (mode)
            {
                case HandMode.DeckA: return "A（毎ターン2枚）";
                case HandMode.DeckB: return "B（3枚未満なら3枚まで＋1枚）";
                case HandMode.DeckLarge3: return "大：定量（初手8・上限10・毎ターン3枚）";
                case HandMode.DeckLargeUsed: return "大：使った分＋1（初手8・上限10）";
                case HandMode.DeckMid3: return "中：定量（初手7・上限9・毎ターン3枚）";
                case HandMode.DeckLarge12: return "大：上限12（初手8・毎ターン3枚・上限12）";
                case HandMode.DeckSeven: return "7枚補充（毎ターン手札が7枚になるまで引く）";
                default: return "固定";
            }
        }

        static int StableSeed(string text)
        {
            unchecked
            {
                int h = 17;
                foreach (char c in text) h = h * 31 + c;
                return h;
            }
        }

        /// <summary>
        /// RUN の戦闘を読み込む。HP は前の戦闘から持ち越し、デッキ内容から山札を作って5枚引く。
        /// </summary>
        public void LoadRunBattle(StageData stage, int playerHp, int playerMaxHp, IEnumerable<CommandData> deckCards, CommandData walk, System.Random rng)
        {
            Deck = new DeckState(rng);
            BasicWalk = walk;
            SetupBattle(stage, playerHp, playerMaxHp);
            Deck.StartBattle(deckCards);
            MarkDrawn(Deck.LastDrawn);
        }

        void SetupBattle(StageData stage, int playerHp, int playerMaxHp)
        {
            StopAllCoroutines();
            if (TurnNumber > 1) RecordPlaytest("中断（ステージを変えた）");
            RulesEngine.ResetExperiments(); // 研究用の実験の切替は、ゲームでは常に OFF
            HandRules.ResetExperiments(); // 【RC6】手札の決まりもゲームの既定へ
            ActFullPrediction = false;
            CurrentStage = stage;
            LayoutIndex = stage.LayoutIndex(pendingLayout);
            grid.BuildFromRows(stage.GetMapRows(pendingLayout), stage.spikeDamage, stage.visualTheme); // 【完成版】開始配置の別案
            pendingLayout = 0;
            Player = grid.SpawnUnit("プレイヤー", true, playerMaxHp, grid.PlayerStart);
            Player.SetHp(playerHp);
            // 敵：盤面の E の読み順（上の行から・左から）に 敵A, 敵B …。1体だけなら「敵」
            enemies.Clear();
            var starts = grid.EnemyStarts;
            for (int i = 0; i < starts.Count; i++)
            {
                string label = starts.Count == 1 ? "" : ((char)('A' + i)).ToString();
                var color = UnitController.EnemyColors[i % UnitController.EnemyColors.Length];
                enemies.Add(grid.SpawnUnit("敵" + label, false, EnemyHpFor(stage), starts[i], label, color));
            }
            // 【検証中】兵種・最初の向き・環境（炎床・回復ルーン）。解析（BattleModel.FromStage）と同じ設定
            grid.Model.ApplyStage(stage);
            foreach (var e in enemies) { var kc = UnitController.KindColor(e.Model.Kind); if (kc.HasValue) e.SetBaseColor(kc.Value); } // 【仮ビジュアル】兵種ごとの色
            foreach (var u in grid.Units) u.ApplyTheme(stage.visualTheme); // 【検証中】テーマの4方向の絵（兵種が決まった後）
            UiStyles.Theme = stage.visualTheme;
            battleEvents.Clear();
            ActingEnemy = null;
            grid.FitCamera(Camera.main);
            retainedCards.Clear();

            executor = new CommandExecutor(grid);
            plannedCommands.Clear();
            EnemyReplay.Clear();
            log.Clear();
            TurnNumber = 1;
            TurnsSinceEnemyHurt = 0; enemyHpAtTurnStart = -1; // 【RC6】
            ExecutingIndex = -1;
            IsExecutingInnate = false;
            innateDoneThisTurn = false;
            State = BattleState.PlayerInput;
            Step = TurnStep.Input; // 【RC8】
            usedCardNames.Clear();
            turnTrace.Clear(); traceStart = -1; traceHead = null; // 【RC7】
            usedTacticNames.Clear(); // 【Combat V4】
            roundEndPositions.Clear();
            battleStartHp = Player.Hp;
            battleRecorded = false;
            AddLog($"{stage.stageTitle} 開始");

            // 【検証用】鏡蝕ゲージ：デバッグ切替が ON で、ステージが許可しているときだけ（STAGE 1〜3・RUN は許可していない）
            if (EnemyHpFor(stage) != stage.enemyMaxHp)
                AddLog($"【検証】長期戦：敵の HP を2倍（{stage.enemyMaxHp} → {EnemyHpFor(stage)}）");
            Stagnation = StagnationDebugOn && stage.stagnationAllowed ? new StagnationRule(stage.stagnationMax, stage.stagnationPenalties) : null;
            if (Stagnation != null)
                AddLog($"【検証】鏡蝕 ON：敵の HP を減らせないラウンドが {Stagnation.Max} 回続くとゲージが満ち、その後も減らせなければ呪いのダメージ（{string.Join("→", Stagnation.Penalties)}）");
        }

        /// <summary>
        /// 【検証用】長期戦：テスト用ステージ（M1〜M3 など）だけ、敵の HP を2倍にする。
        /// 敵 HP 8 だと2〜3ターンで終わり、手札方式 A/B の差（4ターン目以降に出る）が見えないため。
        /// </summary>
        public bool LongBattleDebugOn { get; set; }

        int EnemyHpFor(StageData stage) => LongBattleDebugOn && stage.isTestStage ? stage.enemyMaxHp * 2 : stage.enemyMaxHp;

        /// <summary>【検証用・未確定】鏡蝕ゲージのデバッグ切替（画面右下）。OFF なら今までどおり。</summary>
        public bool StagnationDebugOn { get; set; }

        /// <summary>【検証中】いま兵種の行動（TRAIT PHASE）を実行中か（表示用）。</summary>
        public bool IsExecutingTrait { get; private set; }
        /// <summary>【検証中】いま環境（炎床）が燃えているところか（表示用）。</summary>
        public bool IsEnvironmentPhase { get; private set; }

        /// <summary>【検証用】今の戦闘の鏡蝕ゲージ（無効なら null）。</summary>
        public StagnationRule Stagnation { get; private set; }

        int EnemyHpTotal() => enemies.Sum(e => Mathf.Max(0, e.Hp));

        /// <summary>【検証用】ラウンドの終わりに鏡蝕ゲージを進める。プレイヤーが倒れて戦闘が終わったら true。</summary>
        bool ApplyStagnation(int enemyHpAtRoundStart)
        {
            if (Stagnation == null) return false;
            int before = Stagnation.Gauge;
            int penalty = Stagnation.EndRound(EnemyHpTotal() < enemyHpAtRoundStart);
            if (penalty > 0)
            {
                var model = Player.Model;
                model.TakeDamage(penalty);
                var events = new List<BattleEvent>
                {
                    new BattleEvent
                    {
                        Type = BattleEventType.Damaged, Unit = model, Amount = penalty, HpAfter = model.Hp,
                        Cause = new DamageCause { Source = model, Kind = DamageKind.Curse },
                    },
                };
                if (!model.IsAlive) events.Add(new BattleEvent { Type = BattleEventType.Died, Unit = model });
                battleEvents.AddRange(events);
                grid.Presenter.Play(events);
                AddLog($"鏡蝕 {Stagnation.Max}/{Stagnation.Max}：敵の HP を減らせなかった。遺跡の呪いで あなたに {penalty} ダメージ（残り HP {Player.Hp}）");
                return CheckBattleEnd();
            }
            if (Stagnation.Gauge > before)
                AddLog($"鏡蝕 {Stagnation.Gauge}/{Stagnation.Max}：このラウンドは敵の HP が減らなかった" +
                       (Stagnation.AtMax ? $"（次も減らせなければ、あなたに {Stagnation.NextPenalty} ダメージ）" : ""));
            else if (before > 0 && Stagnation.Gauge == 0)
                AddLog($"敵の HP が減った → 鏡蝕 0/{Stagnation.Max}");
            return false;
        }

        /// <summary>次の正式ステージがあるか（テスト用ステージへは進まない）。</summary>
        public bool HasNextStage => CurrentStageIndex + 1 < stages.Count && !stages[CurrentStageIndex + 1].isTestStage;

        // ───────── 命令スロット ─────────

        /// <summary>
        /// この技を今スロットに追加できるか。
        /// チュートリアル：使える技で、同じ技は1ターン1回まで。RUN：カード無しで使えるのは「歩く」だけ（何回でも）。
        /// </summary>
        public bool CanAddCommand(CommandData command)
        {
            if (IsDeckMode) return command == BasicWalk && CanAddMore;
            return CanAddMore
                && CurrentStage.availableCommands.Contains(command)
                && !IsUsedThisTurn(command);
        }

        /// <summary>まだ命令を追加できるか（PLAYER TURN の入力中で、スロットに空きがある）。</summary>
        public bool CanAddMore => State == BattleState.PlayerInput && plannedCommands.Count < MaxSlots;

        /// <summary>RUN モード：このカードを今スロットに入れられるか（手札にあり、まだスロットに入っていない）。</summary>
        public bool CanAddCard(CardInstance card) =>
            IsDeckMode && CanAddMore && card != null && Deck.Hand.Contains(card) && !IsCardPlanned(card) &&
            !(card.Data.type == CommandType.Rally && card.Data.planDraw > 0 && plannedCommands.Any(r => r.Command.type == CommandType.Rally && r.Command.planDraw > 0)) && // 号令・改は1ターン1枚（レビュー：重ねると7枠）
            !(card.Data.fromDiscard && !HasRecycleTarget(card.Data)); // 【レビュー】再利用は戻せる札が捨て札にあるときだけ（1ターン目に入れると黙って1枠と取り消しを失っていた）

        /// <summary>再利用の札で戻せる札が、今の捨て札にあるか。</summary>
        public bool HasRecycleTarget(CommandData d) => Deck != null && Deck.DiscardPile.Any(c => d.drawFilter == DrawFilter.Any || (d.drawFilter == DrawFilter.Move) == (CopyKinds.IsMoveType(c.Data.type) && !c.Data.IsNoBoardEffect));

        /// <summary>RUN モード：このカードはもうスロットに入っているか。</summary>
        public bool IsCardPlanned(CardInstance card) => plannedCommands.Exists(r => r.SourceCard == card);

        /// <summary>RUN モード：保留の切り替え（手札にあり、スロットに入っていないカードだけ。最大2枚）。</summary>
        public bool ToggleRetain(CardInstance card)
        {
            if (!IsDeckMode || State != BattleState.PlayerInput || !Deck.Hand.Contains(card)) return false;
            if (retainedCards.Remove(card)) return true;
            if (IsCardPlanned(card) || retainedCards.Count >= Deck.MaxRetain) return false;
            retainedCards.Add(card);
            return true;
        }

        /// <summary>同じ技は1ターンにつき1回まで。</summary>
        public bool IsUsedThisTurn(CommandData command) =>
            plannedCommands.Exists(r => r.Command == command);

        public bool AddCommand(CommandRecord record)
        {
            if (IsDeckMode && record.SourceCard != null)
            {
                if (!CanAddCard(record.SourceCard)) return false;
                retainedCards.Remove(record.SourceCard); // 使うカードは保留にしない
            }
            else if (!CanAddCommand(record.Command)) return false;
            plannedCommands.Add(record);
            ApplyPlanEffects(record);
            return true;
        }

        /// <summary>【Puzzle v2】取り消せない命令の数（引く札を入れたら、そこまでは戻せない：引いた札を戻すと山札の上が見えてしまうため）。</summary>
        public int LockedCommands { get; private set; }

        /// <summary>【Puzzle v2】手を回す札は、命令に入れた瞬間に効く（このターンの命令に使える。号令・改の「枠だけ増えて札が無い」を防ぐ）。</summary>
        void ApplyPlanEffects(CommandRecord record)
        {
            var d = record.Command;
            if (!IsDeckMode || d == null || !d.LocksPlan) return;
            int n = d.PlanDrawCount;
            if (n > 0)
            {
                bool IsMove(CommandData c) => CopyKinds.IsMoveType(c.type) && !c.IsNoBoardEffect;
                bool IsTech(CommandData c) => !CopyKinds.IsMoveType(c.type) && !c.IsNoBoardEffect;
                var drawn = d.drawFilter == DrawFilter.Any ? Deck.Draw(n)
                          : d.drawFilter == DrawFilter.MoveAndTechnique ? Deck.DrawMatching(n / 2 + n % 2, IsMove).Concat(Deck.DrawMatching(n / 2, IsTech)).ToList() // 号令・改：移動1・技1
                          : Deck.DrawMatching(n, c => d.drawFilter == DrawFilter.Move ? IsMove(c) : IsTech(c));
                MarkDrawn(drawn);
                AddLog($"{d.displayName}：{drawn.Count} 枚引いた" + (drawn.Count < n ? "（引ける札が足りない）" : ""));
                CardsDrawnByEffects += drawn.Count;
            }
            if (d.type == CommandType.Insight) PendingForesight++;
            if (d.fromDiscard) { PendingRecycle += Mathf.Max(1, d.range); recycleFilter = d.drawFilter; }
            LockedCommands = plannedCommands.Count;
        }

        /// <summary>【Puzzle v2】再利用：捨て札から選ぶ回数と、その種類。</summary>
        public int PendingRecycle { get; private set; }
        DrawFilter recycleFilter;
        /// <summary>再利用で選べる捨て札（新しい順・最大10）。</summary>
        public IReadOnlyList<CardInstance> RecycleChoices => Deck == null || PendingRecycle <= 0 ? new List<CardInstance>()
            : Deck.DiscardPile.Reverse().Where(c => recycleFilter == DrawFilter.Any || (recycleFilter == DrawFilter.Move) == (CopyKinds.IsMoveType(c.Data.type) && !c.Data.IsNoBoardEffect)).Take(10).ToList();

        /// <summary>再利用：選んだ1枚を手札へ（null なら選ばない）。</summary>
        public void ChooseRecycle(CardInstance card)
        {
            if (PendingRecycle <= 0) return;
            PendingRecycle--;
            if (card != null && Deck.ReturnFromDiscard(card))
            {
                MarkDrawn(new[] { card });
                AddLog($"再利用：「{card.Data.displayName}」を手札へ");
                CardsDrawnByEffects++;
            }
        }

        /// <summary>【Puzzle v2・解析】この戦闘で札の効果で引いた枚数。</summary>
        public int CardsDrawnByEffects { get; private set; }

        public void RemoveLastCommand()
        {
            if (State == BattleState.PlayerInput && plannedCommands.Count > LockedCommands)
                plannedCommands.RemoveAt(plannedCommands.Count - 1);
        }

        public void ClearCommands()
        {
            if (State == BattleState.PlayerInput && plannedCommands.Count > LockedCommands) plannedCommands.RemoveRange(LockedCommands, plannedCommands.Count - LockedCommands);
        }

        /// <summary>最低1命令が必要（0命令での実行は不可）。</summary>
        public bool CanExecute => State == BattleState.PlayerInput && plannedCommands.Count >= 1;

        // ───────── ターン進行 ─────────

        /// <summary>「実行」ボタン。</summary>
        public void ExecuteTurn()
        {
            if (!CanExecute) return;
            StartCoroutine(TurnRoutine());
        }

        /// <summary>テスト用：待ち時間なしで1ターン（PLAYER → ENEMY）を最後まで進める。</summary>
        public void ExecuteTurnImmediately()
        {
            if (!CanExecute) return;
            instant = true;
            RunToEnd(TurnRoutine());
            instant = false;
        }

        IEnumerator TurnRoutine()
        {
            // 1. PLAYER TURN：登録した命令を ①→②→③ の順に実行
            State = BattleState.PlayerExecuting;
            Step = TurnStep.You; // 【RC8】
            foreach (var r in plannedCommands) usedCardNames.Add(r.Command.displayName); // 【検証用】プレイテストの自動記録
            int enemyHpAtRoundStart = EnemyHpTotal(); // 【検証用】鏡蝕の判定に使う
            AddLog($"―― ターン {TurnNumber}：あなたの番 ――"); // 【RC3・レビュー】英語の開発用の文を日本語に
            if (IsDeckMode)
            {
                // 「実行」を押した時点で、命令列に入っているカードを使用済み（捨て札）にする
                var used = new List<CardInstance>();
                foreach (var r in plannedCommands) if (r.SourceCard != null) used.Add(r.SourceCard);
                Deck.UseCards(used);
            }
            grid.Model.BeginRound();
            int roundEventStart = battleEvents.Count;
            // 【RC7】ターンごとの記録：組んだ命令と、このターンの始めの HP
            traceStart = battleEvents.Count; traceHp = Player.Model.Hp; lastTurnStart = battleEvents.Count; OwnMoveLog.Clear(); // 【RC8】
            traceHead = $"T{TurnNumber}：" + (plannedCommands.Count == 0 ? "命令なし" : string.Join("・", plannedCommands.Select(r => r.Command.displayName + (r.Command.UsesDirection ? (r.Direction == Direction.Up ? "↑" : r.Direction == Direction.Down ? "↓" : r.Direction == Direction.Left ? "←" : "→") : ""))));
            int commandsUsed = plannedCommands.Count;
            if (PlayerAttackBonusEachTurn > 0) Player.Model.AttackBonus += PlayerAttackBonusEachTurn; // 【検証中】役職「先兵」
            foreach (var r in plannedCommands)
            {
                if (r.Command.type == CommandType.Foresight) PendingForesight++;
                if (r.Command.type == CommandType.Reconsider) PendingDraw += Mathf.Max(1, r.Command.range);
                if (r.Command.ResonateCount > 0) resonateDraw += r.Command.ResonateCount;
                copyKillDraw += r.Command.copyKillDraw;
            }
            LockedCommands = 0;
            PendingRecycle = 0; // 選ばなかった再利用は消える（次のターンに遅れて効かない：レビュー）
            copyGimmickThisRound = false;
            copyKillThisRound = false;
            if (CopyCore.IsImmediate(grid.Model.CopyMode))
            {
                // 【検証中・Copy Core V2】すぐの方式：あなた① → 敵A① → 敵B① → あなた② → …（解析の CopyCore.PlayImmediate と同じ順番）
                immediatePlan = plannedCommands.ToList();
                var skip = new HashSet<int>(uncopiedSlots);
                for (int i = 0; i < immediatePlan.Count; i++)
                {
                    State = BattleState.PlayerExecuting;
                    ActingEnemy = null;
                    ExecutingIndex = i;
                    AddLog(Execute(Player, immediatePlan[i], false, false));
                    yield return Wait(commandInterval);
                    if (CheckBattleEnd()) { immediatePlan = null; yield break; }
                    if (!grid.Model.CopyEnabled || immediatePlan[i].Command.notCopied || skip.Contains(i)) continue;
                    State = BattleState.EnemyExecuting;
                    foreach (var enemy in enemies.ToArray())
                    {
                        if (!enemy.IsAlive) continue;
                        var rec = CopyCore.CopyOne(immediatePlan[i], enemy.Model, Player.Model, grid.Model.CopyMode);
                        if (rec == null) continue;
                        ActingEnemy = enemy;
                        AddLog(Execute(enemy, rec, true, false));
                        yield return Wait(commandInterval);
                        if (CheckBattleEnd()) { ActingEnemy = null; immediatePlan = null; yield break; }
                    }
                    ActingEnemy = null;
                }
                Player.Model.AttackBonus = 0;
                foreach (var e in enemies) e.Model.AttackBonus = 0;
                ExecutingIndex = -1;
                immediatePlan = null;
                plannedCommands.Clear();
                State = BattleState.EnemyExecuting;
                innateDoneThisTurn = false;
            }
            else
            {
            yield return RunCommands(Player, plannedCommands, false);
            if (CheckBattleEnd()) yield break;

            // 【Combat V4 RC2】あなたの命令 → 敵の兵種の行動・大技（追撃型以外）→ 敵のコピー
            if (grid.Model.V4)
            {
                State = BattleState.EnemyExecuting;
                Step = TurnStep.EnemyAttack; // 【RC8】
                yield return TraitPass(1);
                if (traitEnded) yield break;
            }

            // 2. 使った命令列を敵へコピー（結果ではなく、入力した内容をそのまま）
            EnemyReplay.CopyFrom(CopiedSubset()); // 【検証中】コピーされない命令（忍び足・空白の石）は渡さない。COPY OFF なら空
            plannedCommands.Clear();

            // 3. ENEMY TURN
            State = BattleState.EnemyExecuting;
            Step = TurnStep.Copy; // 【RC8】
            innateDoneThisTurn = false;
            AddLog($"―― ターン {TurnNumber}：敵の番 ――");
            yield return Wait(turnChangeInterval);

            // 敵A が全部の命令を実行 → 敵B が全部の命令を実行 …（順番は固定・Enemy Preview に表示）
            foreach (var enemy in enemies.ToArray())
            {
                if (!enemy.IsAlive) continue;
                ActingEnemy = enemy;

                // 3-1. 固有行動（あれば毎ターン必ず・最初に）。コピーされない
                if (EnemyInnateAction != null)
                {
                    IsExecutingInnate = true;
                    AddLog(Execute(enemy, new CommandRecord(EnemyInnateAction, Direction.Up, 0), false, true) + "【固有行動】");
                    innateDoneThisTurn = true;
                    yield return Wait(commandInterval);
                    IsExecutingInnate = false;
                    if (CheckBattleEnd()) { ActingEnemy = null; yield break; }
                }

                // 3-2. コピーした命令を、敵自身の現在位置から同じ順番で実行（コピー特性を反映）。封印が割れた鏡の番人は写せない
                if (CopyCore.Stunned(enemy.Model)) continue;
                yield return RunCommands(enemy, CopyKinds.Filter(enemy.Model, EnemyReplay.GetCommandsFor(enemy.Model.CopyTrait)), true); // 【Copy Core V3】技写し・歩写し
                if (CheckBattleEnd()) { ActingEnemy = null; yield break; }
            }
            ActingEnemy = null;
            }

            // 3-3.【検証中】ENEMY TRAIT PHASE：兵種の行動（V4 は追撃型だけここ。それ以外は コピーの前に済んでいる）。解析の PhaseRules.TraitPhaseAfter と同じ順番
            yield return TraitPass(grid.Model.V4 ? 2 : 0);
            if (traitEnded) yield break;

            // 4.【検証中】ENVIRONMENT PHASE：炎床（無ければ何も起きない）→ ラウンドを進める
            {
                var envEvents = new List<BattleEvent>();
                var envLog = new List<string>();
                bool burning = grid.Model.Environment.FireCellsNow.Count > 0;
                IsEnvironmentPhase = burning;
                Step = TurnStep.Floor; // 【RC8】
                PhaseRules.EnvironmentPhase(grid.Model, envEvents, envLog);
                foreach (var line in envLog) AddLog(line);
                battleEvents.AddRange(envEvents);
                NoteWeak(envEvents);
                grid.Presenter.Play(envEvents);
                if (burning) yield return Wait(commandInterval);
                IsEnvironmentPhase = false;
                if (CheckBattleEnd()) yield break;
            }

            // 【検証用】鏡蝕ゲージ（OFF なら何もしない）
            if (ApplyStagnation(enemyHpAtRoundStart)) yield break;

            // 4. 次の PLAYER TURN へ（デッキ：未使用カードの扱いとドローは方式による）
            //    RUN 方式：保留したカード以外を捨て、5枚になるまで引く／A 案：2枚引く／B 案：3枚未満なら3枚まで＋1枚
            if (IsDeckMode)
            {
                Deck.EndTurn(retainedCards);
                retainedCards.Clear();
                int before = Deck.ReshuffleCount;
                var drawn = Deck.StartTurnDraw();
                // 【検証中】ACT1：再考・刻印による追加ドロー（基本の補充のあと。7枚を超えてよい）
                int extra = PendingDraw + (RoundEndDrawBonus?.Invoke(battleEvents.Skip(roundEventStart).ToList(), commandsUsed) ?? 0);
                PendingDraw = 0;
                // 【Puzzle v2】共鳴補給：このラウンドに、まねで仕掛けが動いたら
                if (resonateDraw > 0 && copyGimmickThisRound) { extra += resonateDraw; AddLog($"共鳴：まねで仕掛けが動いた → 次のターン +{resonateDraw} 枚"); }
                if (copyKillDraw > 0 && copyKillThisRound) { extra += copyKillDraw; AddLog($"余韻：まねで敵が倒れた → 次のターン +{copyKillDraw} 枚"); }
                resonateDraw = 0;
                copyKillDraw = 0;
                // 【Combat V4】反響：このラウンド、まねで敵にダメージが入ったら次のターン +n 枚
                if (echoDraw > 0)
                {
                    if (battleEvents.Skip(roundEventStart).Any(e => e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null && CopyCore.IsCopyOrigin(e.Cause)))
                    { extra += echoDraw; AddLog($"反響：まねで敵にダメージ → 次のターン +{echoDraw} 枚"); }
                    echoDraw = 0;
                }
                if (extra > 0)
                {
                    var more = Deck.Draw(extra);
                    drawn.AddRange(more);
                    AddLog($"追加で {more.Count} 枚引いた");
                }
                // 【検証用】攻撃札の保証（M1〜M3 の手札方式 A/B 比較だけ。RUN では使わない）
                if (BasicWalk == null && CurrentStage.handTestGuaranteeAttack && !Deck.IsLarge) // 大きい手札では使わない（必要性を比較するため）
                {
                    var swapped = Deck.EnsureAttackInHand();
                    if (swapped != null)
                    {
                        drawn.Add(swapped);
                        AddLog($"【検証用】手札に攻撃札が無かったので、山札の「{swapped.Data.displayName}」と1枚入れ替えた");
                    }
                }
                MarkDrawn(drawn);
                if (Deck.ReshuffleCount > before && Deck.DrawPile.Count + Deck.DiscardPile.Count > 0) AddLog("山札が切れたので、捨て札をシャッフルして山札にした"); // 【RC3】山札が手札と同じ枚数（チュートリアル）では毎ターン出ていた
                if (!Deck.UsesRetainChoice)
                    AddLog($"ターン開始：{drawn.Count}枚引いた（手札 {Deck.Hand.Count}{(Deck.HandLimit < DeckState.SafetyHandLimit ? "/" + Deck.HandLimit : "")}）" + // 【RC3】上限なし（999）は出さない（「7/999」と出ていた）
                           (Deck.LastSkippedDraws > 0 ? $"　手札上限のため {Deck.LastSkippedDraws} 枚引けなかった" : ""));
            }
            roundEndPositions.Add(Player.GridPosition); // 【検証用】プレイテストの自動記録
            if (Tactics != null)
            {
                // 【完成版】使わなかった命令枠1つにつき、戦術札を引く（今は少なく出して次の準備をする、を選べる）
                int unused = TurnRules.UnusedSlotTacticDraw > 0 ? Mathf.Max(0, MaxSlots - TempSlots - RallySlots - commandsUsed) : 0; // 【RC2】号令・命令石の枠は数えない（使わなければ戦術札1枚に化けていた：レビュー）
                int bonusDrawn = unused > 0 ? Tactics.Draw(unused * TurnRules.UnusedSlotTacticDraw) : 0;
                if (bonusDrawn > 0) AddLog($"使わなかった命令枠 {unused} つ → 戦術札を {bonusDrawn} 枚引いた");
                Tactics.StartTurn(); TacticPlaysThisTurn = 0; pendingSiftCard = null;
            } // 【Combat V4】
            TempSlots = 0;
            uncopiedSlots.Clear();
            { int hpNow = grid != null && grid.Model != null ? grid.Model.Enemies.Where(e => e.IsAlive && !e.IsObject).Sum(e => e.Hp) : 0; TurnsSinceEnemyHurt = enemyHpAtTurnStart >= 0 && hpNow >= enemyHpAtTurnStart ? TurnsSinceEnemyHurt + 1 : 0; enemyHpAtTurnStart = hpNow; } // 【RC6】
            FlushTurnTrace(); // 【RC7】
            TurnNumber++;
            Step = TurnStep.Input; // 【RC8】
            State = BattleState.PlayerInput;
        }

        /// <summary>
        /// 命令列を順番に実行する。プレイヤーが倒れた・敵が全滅した・実行中のユニットが倒れた場合は、残りを実行しない。
        /// </summary>
        IEnumerator RunCommands(UnitController actor, IReadOnlyList<CommandRecord> commands, bool isCopy)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                if (isCopy && (CopyCore.Stunned(actor.Model) || actor.Model.Dormant)) break; // 【V3】途中で守りが割れた・段階が変わったら、残りはまねない（【Puzzle v2】眠った像も）
                ExecutingIndex = i;
                CurrentCopy = isCopy ? commands[i] : null; // 【RC9】③まねの間、今まねている命令（画面の帯に「A がまねる：押し斬り↑」）
                AddLog(Execute(actor, commands[i], isCopy, false));
                yield return Wait(commandInterval);
                if (!Player.IsAlive || Won || !actor.IsAlive) break;
            }
            CurrentCopy = null;
            actor.Model.AttackBonus = 0; // 【検証中】闘気はそのユニットの命令列の中だけ（解析の TurnRules と同じ）
            ExecutingIndex = -1;
        }

        string Execute(UnitController actor, CommandRecord record, bool isCopy, bool isInnate)
        {
            string text = executor.Execute(actor, record, isCopy, isInnate);
            battleEvents.AddRange(executor.LastEvents);
            if (isCopy && executor.LastEvents.Any(PuzzleRules.IsGimmickEvent)) CopyGimmicksThisBattle++;
            if (isCopy && executor.LastEvents.Any(PuzzleRules.IsProgressEvent)) copyGimmickThisRound = true; // 共鳴：割れた・開いた・運んだときだけ（当たっただけ・回っただけでは引かない：レビュー）
            // 【戦闘中心 v3・レビュー】戦闘の部屋（Soft）には封印石・紋がほとんど無く、共鳴の一歩が発動しなかった → 鎧割り・鏡晶・樽・石像も
            if (isCopy && grid.Model.SoftRules && executor.LastEvents.Any(e => e.Type == BattleEventType.ArmorBroken || e.Type == BattleEventType.CrystalBurst || e.Type == BattleEventType.BarrelBurst || e.Type == BattleEventType.StatueTurned)) copyGimmickThisRound = true;
            if (isCopy && executor.LastEvents.Any(e => e.Type == BattleEventType.Died && e.Unit != null && !e.Unit.IsPlayer && !e.Unit.IsObject)) copyKillThisRound = true;
            // 【Puzzle v2】あなたが間違えた瞬間をチュートリアルにする（「無効！この敵には“まね攻撃”か仕掛けが必要」など）
            if (!isCopy && actor == Player)
                foreach (var e in executor.LastEvents)
                    if ((e.Type == BattleEventType.Deflected || e.Type == BattleEventType.Damaged) && !string.IsNullOrEmpty(e.Text)) { LastHint = e.Text; LastHintSerial++; } // 【戦闘中心 v3】反響鎧（1だけ通った）も
            NoteWeak(executor.LastEvents);
            return text;
        }

        /// <summary>【Puzzle v2】封印が弱まったら、はじいた理由と同じ帯で知らせる（詰んだように見えないように）。</summary>
        void NoteWeak(IEnumerable<BattleEvent> evs)
        {
            foreach (var e in evs)
                if ((e.Type == BattleEventType.SealsWeakened || e.Type == BattleEventType.Resonance) && !string.IsNullOrEmpty(e.Text)) { LastHint = e.Text; LastHintSerial++; } // 【戦闘中心 v3】共鳴も
        }

        /// <summary>【RC8】ターンの段（画面の順番の帯で光らせる）：入力 → ①あなた → ②敵の攻撃 → ③まね → ④大技 → ⑤敵の移動 → ⑥床。</summary>
        public enum TurnStep { Input, You, EnemyAttack, Copy, BigAttack, Move, Floor }
        public TurnStep Step { get; private set; } = TurnStep.Input;

        /// <summary>勝敗が決まったら true。</summary>
        bool traitEnded;
        /// <summary>【完成版】いま実行中の兵種の行動の部分（0＝行動と大技／1＝行動だけ／2＝大技だけ）。画面の「大技」表示に使う。</summary>
        public int CurrentTraitPart { get; private set; }

        /// <summary>兵種の行動（敵A → 敵B …）。pass 0＝全員／1＝コピーの前／2＝コピーの後（追撃型・完成版では大技も）。順番は PhaseRules.TraitSteps（解析と同じ）。戦闘が終わったら traitEnded。</summary>
        IEnumerator TraitPass(int pass)
        {
            traitEnded = false;
            foreach (var (model, part) in PhaseRules.TraitSteps(grid.Model, pass))
            {
                var enemy = enemies.FirstOrDefault(u => u.Model == model);
                if (enemy == null || !enemy.IsAlive || !PhaseRules.HasTrait(enemy.Model)) continue;
                ActingEnemy = enemy;
                IsExecutingTrait = true;
                CurrentTraitPart = part;
                if (part == PhaseRules.PartAttack) Step = TurnStep.EnemyAttack; // 【RC8】
                else if (part == PhaseRules.PartFire) Step = TurnStep.BigAttack;
                else if (part == PhaseRules.PartPosition || part == PhaseRules.PartTelegraph) Step = TurnStep.Move;
                var traitEvents = new List<BattleEvent>();
                string text = PhaseRules.TraitAction(grid.Model, enemy.Model, traitEvents, part);
                if (text == null && traitEvents.Count == 0) { IsExecutingTrait = false; continue; } // 何も起きない（大技の時計が進んだだけ）なら待たない
                AddLog(text);
                battleEvents.AddRange(traitEvents);
                if (part == PhaseRules.PartPosition) OwnMoveLog.AddRange(traitEvents.Where(te => te.Type == BattleEventType.Moved && te.Unit == enemy.Model));
                grid.Presenter.Play(traitEvents);
                yield return Wait(commandInterval);
                IsExecutingTrait = false;
                if (CheckBattleEnd()) { ActingEnemy = null; traitEnded = true; yield break; }
            }
            CurrentTraitPart = 0;
            ActingEnemy = null;
        }

        bool CheckBattleEnd()
        {
            if (Won)
            {
                State = BattleState.Cleared;
                AddLog(grid.Model.Goal != RoomGoal.Defeat ? "目標を達成した！ 勝利" : enemies.Count == 1 ? "敵の HP が 0 になった！ 勝利" : "敵を全員倒した！ 勝利");
                Sfx.Play(Sfx.Kind.Clear);
                RecordPlaytest("クリア");
                plannedCommands.Clear(); // 【RC11・最後の見直し】あなたの命令の途中で勝つと（主を倒した等）、勝利の窓の横の命令の欄に「斬撃↑」が残っていた
                return true;
            }
            if (!Player.IsAlive)
            {
                State = BattleState.GameOver;
                AddLog("あなたの HP が 0 になった… 敗北");
                Sfx.Play(Sfx.Kind.Lose);
                RecordPlaytest("敗北");
                return true;
            }
            return false;
        }

        /// <summary>F キーで敵の行動の間を短くする（検証用ではなく遊びやすさ）。</summary>
        public static bool FastForward;
        object Wait(float seconds) => instant ? null : new WaitForSeconds(FastForward ? seconds * 0.3f : seconds);

        void MarkDrawn(IEnumerable<CardInstance> cards)
        {
            float now = Application.isPlaying ? Time.time : 0f;
            bool any = false;
            foreach (var c in cards) { c.DrawnAt = now; any = true; }
            if (any && Deck != null && grid != null && grid.Model != null && grid.Model.V4) Deck.SortHandByRole(); // 【RC6】移動 → 攻撃 → 紫 → そのほか の順に並べる
            if (any) Sfx.Play(Sfx.Kind.Draw);
        }

        void AddLog(string line)
        {
            log.Add(line);
            if (log.Count > 200) log.RemoveAt(0);
        }

        /// <summary>入れ子のコルーチンを待ち時間なしで最後まで回す（テスト用）。</summary>
        static void RunToEnd(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator nested) stack.Push(nested);
                }
                else stack.Pop();
            }
        }

        /// <summary>テスト用：ステージ一覧と盤面を直接渡して初期化する。</summary>
        public void SetupForTest(GridManager gridManager, List<StageData> stageList)
        {
            grid = gridManager;
            stages = stageList;
            autoSevenForThemed = false; // テストは選んだ手札方式のまま（固定なら固定）
        }

        /// <summary>【検証中】テーマつきの見本ステージで「固定」のとき、7枚補充で遊ぶか（ゲームでは true。テストは false）。</summary>
        bool autoSevenForThemed = true;
    }
}
