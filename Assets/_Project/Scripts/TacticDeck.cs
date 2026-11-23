using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【Combat V4】戦術札（緑）のデッキ。通常の命令札のデッキ（DeckState）とは別。
    /// 手札は最大 HandMax（【RC6】1。指揮官は 2）。戦闘の始めに StartHand 枚、毎ターンの始めに PerTurn 枚引く（手札が上限なら引かない）。
    /// 山札が尽きたら捨て札を混ぜ直す。使った札は捨て札へ（消耗は無し）。
    /// </summary>
    public class TacticDeck
    {
        /// <summary>戦闘の始めの枚数・毎ターン引く枚数（仮値：2 ＋ 1。比べた結果は研究資料）。</summary>
        public static int StartHand = 2, PerTurn = 1;

        readonly System.Random rng;
        readonly List<CommandData> draw = new List<CommandData>();
        readonly List<CommandData> discard = new List<CommandData>();
        readonly List<CommandData> hand = new List<CommandData>();

        public IReadOnlyList<CommandData> Hand => hand;
        public int DrawCount => draw.Count;
        public int DiscardCount => discard.Count;
        public IReadOnlyList<CommandData> DiscardPile => discard;

        /// <summary>【RC6】この戦闘の手札の上限（ふつう TacticRules.HandMax＝1。指揮官は +1）。</summary>
        public int HandMax { get; set; }

        public TacticDeck(System.Random rng) { this.rng = rng ?? new System.Random(1); HandMax = TacticRules.HandMax; }

        public void StartBattle(IEnumerable<CommandData> cards)
        {
            hand.Clear(); discard.Clear(); draw.Clear();
            draw.AddRange(cards.Where(c => c != null));
            Shuffle(draw);
            Draw(StartHand);
        }

        public void StartTurn() { if (PerTurn > 0) Draw(PerTurn); }

        /// <summary>n 枚引く（手札が上限に達したら止まる）。引いた枚数を返す。</summary>
        public int Draw(int n)
        {
            int got = 0;
            for (int i = 0; i < n && hand.Count < HandMax; i++)
            {
                if (draw.Count == 0)
                {
                    if (discard.Count == 0) break;
                    draw.AddRange(discard); discard.Clear(); Shuffle(draw);
                }
                hand.Add(draw[draw.Count - 1]);
                draw.RemoveAt(draw.Count - 1);
                got++;
            }
            return got;
        }

        /// <summary>使う：手札から捨て札へ。</summary>
        public bool Use(CommandData card)
        {
            int i = hand.IndexOf(card);
            if (i < 0) return false;
            hand.RemoveAt(i);
            if (!card.exhaust) discard.Add(card); // 【完成版】消耗の札（1戦に1回）はこの戦闘から消える
            return true;
        }

        /// <summary>報酬などで山札へ入れる札の一覧（戦闘のたびに作り直すので、Run 側のリストを渡す）。</summary>
        void Shuffle(List<CommandData> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }
}
