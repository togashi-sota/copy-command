using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 1枚のカード（デッキ内の実体）。同じ「斬撃」が2枚あっても、別々のカードとして区別するために使う。
    /// </summary>
    public class CardInstance
    {
        static int nextId = 1;
        public readonly int Id;
        public readonly CommandData Data;
        /// <summary>手札に入った時刻（ドローの見た目の演出用）。</summary>
        public float DrawnAt;
        /// <summary>【完成版】鍛えた札：あなたが使うときだけダメージ +Forge（敵のまねは元の強さ。敵がまねるのは形だけ）。</summary>
        public int Forge;
        /// <summary>【RC6】まね＋／まね－の札：敵がこの札をまねたときのダメージ +CopyDelta（あなたが使うときは元のまま）。</summary>
        public int CopyDelta;

        public CardInstance(CommandData data)
        {
            Id = nextId++;
            Data = data;
        }

        public override string ToString() => $"{Data.displayName}#{Id}";
    }

    /// <summary>【RC6】札の強化の種類：自分＋（あなたが使うと +2）・まね＋（あなた +1・敵のまね +3：敵どうしに強いが、あなたにも強い）・まね－（あなた +1・敵のまね -2：安全）。</summary>
    public enum UpgradeKind { Self, CopyUp, CopyDown }

    /// <summary>ターン開始時の引き方。</summary>
    public enum DrawPolicy
    {
        /// <summary>RUN 縦切り版の方式：手札が5枚になるまで補充。未使用カードは2枚まで保留、残りは捨て札。</summary>
        RefillToFiveRetainTwo,
        /// <summary>手札方式 A 案：2ターン目以降、ターン開始時に2枚引く。未使用カードは全部手札に残る。手札上限 7。</summary>
        DrawTwo,
        /// <summary>手札方式 B 案（最低保証）：手札が3枚未満なら3枚まで補充し、さらに1枚。3枚以上なら1枚。未使用カードは全部残る。手札上限 7。</summary>
        FloorThreePlusOne,
        /// <summary>【検証中】大きい手札・定量：初手8枚・上限10。2ターン目以降、毎ターン3枚引く。</summary>
        LargeDrawThree,
        /// <summary>【検証中】大きい手札・使った分＋1：初手8枚・上限10。前のターンに使った枚数＋1枚を引く（手札は毎ターン1枚ずつ増え、使うほど山札が速く回る）。</summary>
        LargeUsedPlusOne,
        /// <summary>【検証中】中くらいの手札：初手7枚・上限9。2ターン目以降、毎ターン3枚引く（PHASE 7 のシミュレーションで第1候補）。</summary>
        MidDrawThree,
        /// <summary>【検証中】大きい手札・上限12：初手8枚・毎ターン3枚・上限12（普段は8〜10枚、温存すれば12枚まで溜められる）。</summary>
        LargeTwelve,
        /// <summary>【検証中・見本ステージの標準】7枚補充：初手7枚、毎ターン手札が7枚になるまで引く（未使用は残る）。手札はいつも7枚見える（2026-10-04 本人の UI 方針）。</summary>
        RefillSeven,
    }

    /// <summary>
    /// 1回の戦闘中のデッキ循環（RUN モードと、手札方式の比較用）。
    ///   山札（Draw Pile）→ 手札（Hand）→ 使用 or 保留 → 捨て札（Discard Pile）
    /// - 戦闘開始：デッキ全体をシャッフルして山札にし、手札が HandSize 枚になるまで引く。
    /// - 「実行」：命令列に入ったカードを捨て札へ（UseCards）。
    /// - ターン終了：保留指定した未使用カード（最大 MaxRetain 枚）だけ手札に残し、残りは捨て札へ（EndTurn）。
    /// - 次ターン開始：手札が HandSize 枚になるまで引く。山札が切れたら捨て札をシャッフルして山札にする。
    /// 「歩く」はカードではないので、ここには入らない。
    /// </summary>
    public class DeckState
    {
        public const int DefaultHandSize = 5;
        public const int DefaultMaxRetain = 2;
        /// <summary>手札方式 A/B の手札上限（仮）。上限のときのドローは行わない。</summary>
        public const int DefaultHandLimit = 7;
        /// <summary>【検証中】大きい手札方式の初手と上限。</summary>
        public const int LargeOpening = 8;
        public const int LargeHandLimit = 10;
        /// <summary>【検証中】中くらいの手札方式（初手7・上限9・毎ターン3枚）。</summary>
        public const int MidOpening = 7;
        public const int MidHandLimit = 9;
        /// <summary>【検証中】上限12の方式。</summary>
        public const int TwelveHandLimit = 12;
        /// <summary>【検証中】7枚補充方式の基本の手札（ターン開始時、これより少なければここまで引く）。</summary>
        public const int SevenHand = 7;
        /// <summary>
        /// 7枚補充方式の上限。【Puzzle v2・2026-10-06 本人決定】ハード上限は無し（実質の最大はデッキ＋作られた札）。
        /// 前は 15（UI と悪用のため）。手札が多いときは UI が段を増やす。数字はループ防止の保険（届かない値）。
        /// </summary>
        public const int SafetyHandLimit = 999;
        /// <summary>7枚補充方式の基本の手札の枚数（役職などで変えられる。既定 7）。</summary>
        public int BaseHand { get; set; } = SevenHand;
        /// <summary>画面に出す手札の目安（7枚補充なら基本の枚数、それ以外は上限）。</summary>
        public int DisplayHandSize => Policy == DrawPolicy.RefillSeven ? BaseHand : HandLimit;

        public int HandSize { get; }
        public int MaxRetain { get; }
        public DrawPolicy Policy { get; }
        /// <summary>手札の上限（A/B 案）。RUN 方式では HandSize と同じ。</summary>
        public int HandLimit => Policy == DrawPolicy.RefillToFiveRetainTwo ? HandSize : Policy == DrawPolicy.MidDrawThree ? MidHandLimit : Policy == DrawPolicy.LargeTwelve ? TwelveHandLimit : Policy == DrawPolicy.RefillSeven ? SafetyHandLimit : IsLarge ? LargeHandLimit : DefaultHandLimit;
        /// <summary>【検証中】大きい手札方式か（初手8・上限10）。</summary>
        public bool IsLarge => Policy == DrawPolicy.LargeDrawThree || Policy == DrawPolicy.LargeUsedPlusOne || Policy == DrawPolicy.MidDrawThree || Policy == DrawPolicy.LargeTwelve || Policy == DrawPolicy.RefillSeven;
        /// <summary>前のターンに使った（捨て札にした）枚数。「使った分＋1」方式で使う。</summary>
        public int LastUsedCount { get; private set; }
        /// <summary>未使用カードを保留枚数まで選ぶ方式か（RUN 方式）。A/B 案は全部残るので選ばない。</summary>
        public bool UsesRetainChoice => Policy == DrawPolicy.RefillToFiveRetainTwo;
        /// <summary>直近のターン開始で、手札上限のために引けなかった枚数。</summary>
        public int LastSkippedDraws { get; private set; }
        public IReadOnlyList<CardInstance> DrawPile => drawPile;
        public IReadOnlyList<CardInstance> Hand => hand;
        public IReadOnlyList<CardInstance> DiscardPile => discardPile;
        /// <summary>直近の Draw で引いたカード（演出用）。</summary>
        public IReadOnlyList<CardInstance> LastDrawn => lastDrawn;
        /// <summary>捨て札をシャッフルして山札に戻した回数（ログ・テスト用）。</summary>
        public int ReshuffleCount { get; private set; }

        readonly List<CardInstance> drawPile = new List<CardInstance>();   // 末尾が山札の一番上
        readonly List<CardInstance> hand = new List<CardInstance>();
        readonly List<CardInstance> discardPile = new List<CardInstance>();
        readonly List<CardInstance> lastDrawn = new List<CardInstance>();
        readonly System.Random rng;

        public DeckState(System.Random rng, int handSize = DefaultHandSize, int maxRetain = DefaultMaxRetain,
            DrawPolicy policy = DrawPolicy.RefillToFiveRetainTwo)
        {
            this.rng = rng;
            HandSize = handSize;
            MaxRetain = maxRetain;
            Policy = policy;
        }

        /// <summary>
        /// ターン開始時のドロー（2ターン目以降）。方式ごとに引く枚数が違う。引いたカードを返す。
        /// A 案：2枚／B 案：3枚未満なら3枚まで＋1枚、3枚以上なら1枚／RUN 方式：5枚になるまで。
        /// 手札上限のときは、そのドローは行わない（LastSkippedDraws に数える）。
        /// </summary>
        public List<CardInstance> StartTurnDraw()
        {
            switch (Policy)
            {
                case DrawPolicy.DrawTwo:
                    return Draw(2);
                case DrawPolicy.FloorThreePlusOne:
                    int toThree = hand.Count < 3 ? 3 - hand.Count : 0;
                    return Draw(toThree + 1);
                case DrawPolicy.LargeDrawThree:
                case DrawPolicy.MidDrawThree:
                case DrawPolicy.LargeTwelve:
                    return Draw(3);
                case DrawPolicy.RefillSeven:
                    if (HandRules.Mode == HandShape.Guarantee || HandRules.Mode == HandShape.Pools)
                    {
                        CycleExtraControl(); // 【RC6】持ちすぎた紫は捨て札へ（手札の紫は1枚まで：使わない紫がたまって手札が濁った）
                        return DrawShaped(System.Math.Max(0, BaseHand - hand.Count));
                    }
                    return Draw(System.Math.Max(0, BaseHand - hand.Count)); // 7枚未満なら7枚まで。7枚以上ならそのまま
                case DrawPolicy.LargeUsedPlusOne:
                    int count = LastUsedCount + 1;
                    LastUsedCount = 0;
                    return Draw(count);
                default:
                    LastSkippedDraws = 0;
                    return DrawToHandSize();
            }
        }

        /// <summary>count 枚引く（手札上限なら引かない。山札が切れたら捨て札をシャッフル）。</summary>
        public List<CardInstance> Draw(int count)
        {
            lastDrawn.Clear();
            LastSkippedDraws = 0;
            for (int i = 0; i < count; i++)
            {
                if (hand.Count >= HandLimit) { LastSkippedDraws++; continue; }
                if (!DrawOne()) break;
            }
            return new List<CardInstance>(lastDrawn);
        }

        bool DrawOne()
        {
            if (drawPile.Count == 0)
            {
                if (discardPile.Count == 0) return false;
                drawPile.AddRange(discardPile);
                discardPile.Clear();
                Shuffle(drawPile);
                ReshuffleCount++;
            }
            var card = drawPile[drawPile.Count - 1];
            drawPile.RemoveAt(drawPile.Count - 1);
            hand.Add(card);
            lastDrawn.Add(card);
            return true;
        }

        /// <summary>【RC6】役割の保証で、山札の上以外から引いた回数（デッキの順番を曲げた回数：解析用）。</summary>
        public int Corrections { get; private set; }

        /// <summary>
        /// 【RC6】count 枚引く。ただし足りないときだけ直す：
        /// ・残りの枚数で移動・攻撃の最低枚数（HandRules.MinMove／MinAttack、B 案は +1）に届かなくなりそうなら、その役割の札を山札の上から探して引く（山札に無ければ捨て札から）。
        /// ・紫（操作）が手札に HandRules.ControlSoftCap 枚あれば、紫は飛ばす（山札に残る）。
        /// デッキに無い役割は作らない（デッキを組んだ意味を残す）。
        /// </summary>
        public List<CardInstance> DrawShaped(int count)
        {
            lastDrawn.Clear();
            LastSkippedDraws = 0;
            int bonus = HandRules.Mode == HandShape.Pools ? 1 : 0;
            int minMove = HandRules.MinMove + bonus, minAttack = HandRules.MinAttack + bonus;
            for (int i = 0; i < count; i++)
            {
                if (hand.Count >= HandLimit) { LastSkippedDraws++; continue; }
                int remaining = count - i;
                int needMove = System.Math.Max(0, minMove - HandRules.Count(hand, HandRole.Move));
                int needAttack = System.Math.Max(0, minAttack - HandRules.Count(hand, HandRole.Attack));
                HandRole? want = null;
                if (needMove + needAttack >= remaining && needMove + needAttack > 0) want = needMove >= needAttack ? HandRole.Move : HandRole.Attack;
                bool avoidControl = HandRules.Count(hand, HandRole.Control) >= HandRules.ControlSoftCap;
                if (!DrawOneShaped(want, avoidControl)) break;
            }
            return new List<CardInstance>(lastDrawn);
        }

        bool DrawOneShaped(HandRole? want, bool avoidControl)
        {
            if (drawPile.Count == 0)
            {
                if (discardPile.Count == 0) return false;
                drawPile.AddRange(discardPile);
                discardPile.Clear();
                Shuffle(drawPile);
                ReshuffleCount++;
            }
            int idx = -1;
            if (want != null)
            {
                idx = drawPile.FindLastIndex(c => HandRules.RoleOf(c.Data) == want.Value); // 末尾が山札の一番上
                if (idx < 0)
                {
                    int di = RandomIndex(discardPile, c => HandRules.RoleOf(c.Data) == want.Value); // 【RC6・レビュー】前は直前に使った札が戻った
                    if (di >= 0)
                    {
                        var fromDiscard = discardPile[di];
                        discardPile.RemoveAt(di);
                        hand.Add(fromDiscard);
                        lastDrawn.Add(fromDiscard);
                        Corrections++;
                        return true;
                    }
                }
                else if (idx != drawPile.Count - 1) Corrections++;
            }
            if (idx < 0 && avoidControl)
            {
                idx = drawPile.FindLastIndex(c => HandRules.RoleOf(c.Data) != HandRole.Control);
                if (idx >= 0 && idx != drawPile.Count - 1) Corrections++;
                if (idx < 0)
                {
                    // 【RC6】山札が紫だけ：捨て札から紫でない札（無ければ紫を引く＝デッキに無い札は作らない）
                    int di = RandomIndex(discardPile, c => HandRules.RoleOf(c.Data) != HandRole.Control);
                    if (di >= 0)
                    {
                        var fromDiscard = discardPile[di];
                        discardPile.RemoveAt(di);
                        hand.Add(fromDiscard);
                        lastDrawn.Add(fromDiscard);
                        Corrections++;
                        return true;
                    }
                }
            }
            if (idx < 0) idx = drawPile.Count - 1;
            var card = drawPile[idx];
            drawPile.RemoveAt(idx);
            hand.Add(card);
            lastDrawn.Add(card);
            return true;
        }

        /// <summary>
        /// 【RC6・本人「移動・攻撃が主役、紫は脇」】手札を役割の順に並べる（移動 → 攻撃 → 紫 → そのほか。同じ役割の中は前の順のまま）。
        /// 数字キーは手札の順なので、並べ替えても押した札と光る札は同じ。
        /// </summary>
        public void SortHandByRole()
        {
            var sorted = hand.Select((c, i) => (c, i)).OrderBy(x => (int)HandRules.RoleOf(x.c.Data)).ThenBy(x => x.i).Select(x => x.c).ToList();
            hand.Clear();
            hand.AddRange(sorted);
        }

        /// <summary>【RC6】条件に合う札の位置を でたらめに1つ（無ければ -1）。</summary>
        int RandomIndex(List<CardInstance> list, System.Predicate<CardInstance> match)
        {
            var idx = new List<int>();
            for (int i = 0; i < list.Count; i++) if (match(list[i])) idx.Add(i);
            return idx.Count == 0 ? -1 : idx[rng.Next(idx.Count)];
        }

        /// <summary>【RC6・D 案】戦闘の始めの手札に移動か攻撃が最低枚数より少なければ、1回だけ引き直す。</summary>
        public bool MulliganIfNeeded()
        {
            if (HandRules.Count(hand, HandRole.Move) >= HandRules.MinMove && HandRules.Count(hand, HandRole.Attack) >= HandRules.MinAttack) return false;
            int n = hand.Count;
            drawPile.AddRange(hand);
            hand.Clear();
            Shuffle(drawPile);
            for (int i = 0; i < n; i++) if (!DrawOne()) break;
            Corrections++;
            return true;
        }

        /// <summary>【RC6】紫が目安より多ければ、多い分を捨て札へ（ターンの始め。古い札から）。A 案・B 案で使う。</summary>
        void CycleExtraControl()
        {
            while (HandRules.Count(hand, HandRole.Control) > HandRules.ControlSoftCap)
            {
                var c = hand.First(x => HandRules.RoleOf(x.Data) == HandRole.Control);
                hand.Remove(c);
                discardPile.Add(c);
                Corrections++;
            }
        }

        /// <summary>
        /// 【Puzzle v2】条件に合う札を count 枚引く（山札の上から探す。山札に無ければ捨て札を混ぜて探し直す）。足取り・武技整理。
        /// </summary>
        public List<CardInstance> DrawMatching(int count, System.Func<CommandData, bool> match)
        {
            lastDrawn.Clear();
            LastSkippedDraws = 0;
            for (int i = 0; i < count; i++)
            {
                if (hand.Count >= HandLimit) { LastSkippedDraws++; continue; }
                int idx = drawPile.FindLastIndex(c => match(c.Data)); // 末尾が山札の一番上
                if (idx < 0 && discardPile.Any(c => match(c.Data)))
                {
                    drawPile.AddRange(discardPile);
                    discardPile.Clear();
                    Shuffle(drawPile);
                    ReshuffleCount++;
                    idx = drawPile.FindLastIndex(c => match(c.Data));
                }
                if (idx < 0) break;
                var card = drawPile[idx];
                drawPile.RemoveAt(idx);
                hand.Add(card);
                lastDrawn.Add(card);
            }
            return new List<CardInstance>(lastDrawn);
        }

        /// <summary>【検証中】山札の上から count 枚（引く順）。見るだけ。</summary>
        public List<CardInstance> PeekTop(int count)
        {
            var list = new List<CardInstance>();
            for (int i = drawPile.Count - 1; i >= 0 && list.Count < count; i--) list.Add(drawPile[i]);
            return list;
        }

        /// <summary>【RC8】撮影だけ：札を手札に足す（場面を作るため）。</summary>
        public void DebugAddToHand(CardInstance card) { if (card != null) hand.Add(card); }

        /// <summary>【検証中】山札の特定のカードを手札へ（先読み）。</summary>
        public bool TakeFromDrawPile(CardInstance card)
        {
            if (hand.Count >= HandLimit || !drawPile.Remove(card)) return false;
            hand.Add(card);
            return true;
        }

        /// <summary>【検証中】捨て札の特定のカードを手札へ（回収の鉤）。</summary>
        public bool ReturnFromDiscard(CardInstance card)
        {
            if (hand.Count >= HandLimit || !discardPile.Remove(card)) return false;
            hand.Add(card);
            return true;
        }

        /// <summary>【検証中】keep 以外の手札を捨て札へ。捨てた枚数を返す（選別の砂時計）。</summary>
        public int DiscardHandExcept(ICollection<CardInstance> keep)
        {
            int n = 0;
            foreach (var card in hand.ToList())
            {
                if (keep.Contains(card)) continue;
                hand.Remove(card);
                discardPile.Add(card);
                n++;
            }
            return n;
        }

        /// <summary>戦闘開始：デッキ内容から山札を作り直し、手札を引く。</summary>
        /// <summary>【完成版】鍛えた札の種類（アセット名）。この種類の札は、あなたが使うときだけダメージ +ForgeBonus。</summary>
        public HashSet<string> ForgedNames = new HashSet<string>();
        public const int ForgeBonus = 2;
        /// <summary>
        /// 【RC6】まね＋・まね－の強さ（敵のまねのダメージ ±）と、まね－の札をあなたが使うときの上乗せ。解析（pzsearch の PZ_COPYUP・PZ_COPYDOWN・PZ_DOWNSELF）で比べる。
        /// 比べた結果（Logs/rc6/upg・60 ラン）：自分＋2 は Learner 91%・Novice 55%／まね＋2 は 90%・45%／まね－2 は 88%・46% ＝ 自分＋がいつも答え。
        /// まね＋3（88%・50%：大きいが両刃）・まね－2＋自分＋1（93%・58%：安全）で3つが並んだ。
        /// </summary>
        public static int CopyUpgrade = 3, CopyDowngrade = 2, CopyDownSelf = 1;
        /// <summary>
        /// 【RC6】まね＋の札をあなたが使うときの上乗せ（解析 PZ_UPSELF）。最終の数字で まね＋（あなた 0）は Novice 40%・Rookie2 48%（自分＋ 64%・62%）＝罠に近かった →
        /// 1 にして 57%・52%（Logs/rc6/upg2）。3つの形は「あなたは強くなる」が共通で、違いは敵のまねを そのまま／強く／弱く だけ。
        /// </summary>
        public static int CopyUpSelf = 1;
        /// <summary>【RC6】強化の一覧（ForgedNames）の書き方：「名前」＝自分＋・「名前@+」＝まね＋・「名前@-」＝まね－。</summary>
        public static string UpgradeKey(string name, UpgradeKind kind) => kind == UpgradeKind.CopyUp ? name + "@+" : kind == UpgradeKind.CopyDown ? name + "@-" : name;
        public static string UpgradeBaseName(string key) => key.EndsWith("@+") || key.EndsWith("@-") ? key.Substring(0, key.Length - 2) : key;
        public static UpgradeKind UpgradeKindOf(string key) => key.EndsWith("@+") ? UpgradeKind.CopyUp : key.EndsWith("@-") ? UpgradeKind.CopyDown : UpgradeKind.Self;

        public void StartBattle(IEnumerable<CommandData> deck) => StartBattle(deck, 0, false);

        /// <summary>
        /// 戦闘開始（検証用の指定つき）。openingCount：デッキの先頭から何枚を初手に固定するか。
        /// keepOrder：残りの山札もリストの順番どおりに引く（1周目だけ）。false なら残りはシャッフル。
        /// 初手が HandSize 枚に足りなければ、山札から引いて補う。
        /// </summary>
        public void StartBattle(IEnumerable<CommandData> deck, int openingCount, bool keepOrder)
        {
            drawPile.Clear();
            hand.Clear();
            discardPile.Clear();
            lastDrawn.Clear();
            ReshuffleCount = 0;
            LastUsedCount = 0;
            var cards = deck.Select(d => new CardInstance(d) { Forge = ForgedNames.Contains(d.name) ? ForgeBonus : ForgedNames.Contains(d.name + "@-") ? CopyDownSelf : ForgedNames.Contains(d.name + "@+") && !d.selfInert ? CopyUpSelf : 0,
                CopyDelta = ForgedNames.Contains(d.name + "@+") ? CopyUpgrade : ForgedNames.Contains(d.name + "@-") ? -CopyDowngrade : 0 }).ToList(); // 【RC6】まね＋・まね－
            int opening = System.Math.Max(0, System.Math.Min(openingCount, System.Math.Min(HandSize, cards.Count)));
            var rest = cards.Skip(opening).ToList();
            if (keepOrder) rest.Reverse(); // 末尾が山札の一番上なので、リストの先頭から引けるように逆にする
            else Shuffle(rest);            // 従来と同じ並べ方（同じ種なら今までと同じ順番になる）
            drawPile.AddRange(rest);
            if (opening == 0 && !keepOrder)
            {
                if (HandRules.Mode == HandShape.Legacy) DrawToHandSize();
                else
                {
                    DrawShaped(HandSize - hand.Count); // 【RC6】移動・攻撃が切れない手札
                    if (HandRules.Mode == HandShape.Mulligan) MulliganIfNeeded();
                }
                return;
            }
            foreach (var card in cards.Take(opening)) { hand.Add(card); lastDrawn.Add(card); }
            while (hand.Count < HandSize && drawPile.Count > 0)
            {
                var card = drawPile[drawPile.Count - 1];
                drawPile.RemoveAt(drawPile.Count - 1);
                hand.Add(card);
                lastDrawn.Add(card);
            }
        }

        /// <summary>手札が HandSize 枚になるまで引く。山札が切れたら捨て札をシャッフルして山札にする。</summary>
        public List<CardInstance> DrawToHandSize()
        {
            lastDrawn.Clear();
            while (hand.Count < HandSize)
                if (!DrawOne()) break; // デッキが手札より少ない
            return new List<CardInstance>(lastDrawn);
        }

        /// <summary>命令列に入ったカードを使用済みにする（手札 → 捨て札）。</summary>
        public void UseCards(IEnumerable<CardInstance> cards)
        {
            foreach (var card in cards)
                if (hand.Remove(card)) { if (!card.Data.exhaust) discardPile.Add(card); LastUsedCount++; } // 【検証中】消耗の札はこの戦闘から消える
        }

        /// <summary>
        /// ターン終了：retained（最大 MaxRetain 枚、手札にあるもの）だけ残し、他の手札は捨て札へ。
        /// </summary>
        public void EndTurn(IEnumerable<CardInstance> retained)
        {
            if (!UsesRetainChoice) return; // A/B 案：未使用カードは全部手札に残る
            var keep = new HashSet<CardInstance>(retained.Where(hand.Contains).Take(MaxRetain));
            foreach (var card in hand.ToList())
            {
                if (keep.Contains(card)) continue;
                hand.Remove(card);
                discardPile.Add(card);
            }
        }

        public int CountInHand(CommandData data) => hand.Count(c => c.Data == data);

        /// <summary>
        /// 【検証用】手札に攻撃札（ダメージのある札）が1枚も無ければ、手札の最後の1枚と、山札（無ければ捨て札）の
        /// 一番上の攻撃札を入れ替える。手札の枚数は変わらない（A/B の比較条件をそろえるため）。
        /// 手札から抜いた札は山札の一番下（捨て札から取ったときは捨て札）へ。入れ替えた攻撃札を返す（無ければ null）。
        /// </summary>
        public CardInstance EnsureAttackInHand()
        {
            if (hand.Count == 0 || hand.Any(IsAttack)) return null;
            List<CardInstance> source = drawPile;
            var found = drawPile.LastOrDefault(IsAttack); // 末尾が山札の一番上
            if (found == null) { source = discardPile; found = discardPile.FirstOrDefault(IsAttack); }
            if (found == null) return null;
            source.Remove(found);
            var given = hand[hand.Count - 1];
            hand.RemoveAt(hand.Count - 1);
            if (source == drawPile) drawPile.Insert(0, given);
            else discardPile.Add(given);
            hand.Add(found);
            return found;
        }

        static bool IsAttack(CardInstance card) => card.Data.damage > 0;

        void Shuffle(List<CardInstance> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
