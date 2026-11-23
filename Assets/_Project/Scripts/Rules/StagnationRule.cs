using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    /// <summary>
    /// 【検証用・未確定】鏡蝕（きょうしょく）ゲージ：永久待機・手札の整えすぎへの対策案。
    /// - 1ラウンド（PLAYER TURN＋ENEMY TURN）の終わりに、敵の HP 合計がラウンド開始時より減っていなければ +1。
    ///   減っていれば 0 に戻る（ダメージの原因は問わない：直接攻撃・トゲ・同士討ち・押し出しなど）。
    /// - ゲージが最大のまま、さらに減らせなかったラウンドの終わりに、遺跡の呪いでプレイヤーがダメージを受ける。
    ///   ダメージは Penalties の順に増える（最後の値をくり返す）。敵の HP を減らすと、ゲージも呪いの段階も最初に戻る。
    /// 判定は「ダメージが起きたか」ではなく「HP 合計が実際に減ったか」（将来、回復するカードや敵が入っても待機ループにならないように）。
    /// </summary>
    public class StagnationRule
    {
        public int Max { get; }
        public IReadOnlyList<int> Penalties { get; }
        /// <summary>今のゲージ（0〜Max）。</summary>
        public int Gauge { get; private set; }
        /// <summary>最大のあとに呪いを受けた回数（次の呪いの大きさを決める）。</summary>
        public int Overflows { get; private set; }

        public StagnationRule(int max, IEnumerable<int> penalties)
        {
            Max = System.Math.Max(1, max);
            var list = penalties?.Where(p => p > 0).ToList() ?? new List<int>();
            Penalties = list.Count > 0 ? list : new List<int> { 2 };
        }

        public bool AtMax => Gauge >= Max;

        /// <summary>次に呪いを受けるときのダメージ。</summary>
        public int NextPenalty => Penalties[System.Math.Min(Overflows, Penalties.Count - 1)];

        /// <summary>ラウンドの終わり。enemyHpDecreased：敵の HP 合計がこのラウンドで減ったか。戻り値：プレイヤーが受ける呪いのダメージ（0 なら無し）。</summary>
        public int EndRound(bool enemyHpDecreased)
        {
            if (enemyHpDecreased)
            {
                Gauge = 0;
                Overflows = 0;
                return 0;
            }
            if (Gauge < Max)
            {
                Gauge++;
                return 0;
            }
            int penalty = NextPenalty;
            Overflows++;
            return penalty;
        }

        public StagnationRule Clone()
        {
            var c = new StagnationRule(Max, Penalties);
            c.Gauge = Gauge;
            c.Overflows = Overflows;
            return c;
        }

        /// <summary>盤面の敵の HP 合計（倒れた敵は 0）。</summary>
        public static int EnemyHpTotal(BattleModel model) => model.Enemies.Sum(e => System.Math.Max(0, e.Hp));
    }
}
