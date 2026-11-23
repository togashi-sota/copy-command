using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>戦術札を使うときに何を選ぶか。</summary>
    public enum TacticTarget
    {
        None,          // 選ばない（すぐ使う）
        Enemy,         // 敵を1体選ぶ
        EnemyThenEnemy // 敵を2体選ぶ（照準替え：撃つ敵 → 新しい中心の敵）
    }

    /// <summary>戦術札の手札・命令枠への効果（盤面ではなく TurnManager／シミュレーターが実行する）。</summary>
    public struct TacticHandEffect
    {
        public int DrawCommands;   // 通常札を引く
        public int ExtraSlots;     // このターンの命令枠
        public bool Sift;          // 手札を2枚まで捨てて引き直す
        public bool Recall;        // 最後に使った通常の命令の札を手札へ
        public int EchoDraw;       // 反響：このラウンドまねでダメージが入ったら次のターン +n
    }

    /// <summary>
    /// 【Combat V4】戦術札（緑）の効果。Enemy はコピーしない・命令枠を使わない。1ターンに使える枚数は TurnManager 側の上限（既定2）。
    /// 盤面（BattleModel）に起きる効果はここ、手札・命令枠の効果は TacticHandEffect で返す。
    /// </summary>
    public static class TacticRules
    {
        public const int PlaysPerTurn = 2;
        /// <summary>【RC6】緑の札の手札の上限（HandRules.TacticHandMax。前は定数 3）。</summary>
        public static int HandMax => HandRules.TacticHandMax;

        public static TacticTarget TargetOf(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.EarlyBell:
                case TacticKind.LateBell:
                case TacticKind.Silence:
                case TacticKind.Toll:
                case TacticKind.Detonate: return TacticTarget.Enemy;
                case TacticKind.Retarget: return TacticTarget.EnemyThenEnemy;
                default: return TacticTarget.None;
            }
        }

        /// <summary>この敵を第1の対象に選べるか（その札で意味のある敵だけ）。</summary>
        public static bool IsValidTarget(BattleModel m, TacticKind k, UnitModel e)
        {
            if (e == null || !e.IsAlive || e.IsPlayer) return false;
            switch (k)
            {
                case TacticKind.EarlyBell: return PickForHasten(e) != null;
                case TacticKind.LateBell: return PickForDelay(e) != null;
                case TacticKind.Silence: return !e.Silenced && (e.Clocks.Count > 0 || PhaseRules.HasTrait(e));
                case TacticKind.Toll: return e.Clocks.Any(c => c.Cd >= 2);
                case TacticKind.Detonate: return e.Clocks.Any(c => c.Cd >= 2);
                case TacticKind.Retarget: return e.Clocks.Any(c => c.Telegraphed && CanRetarget(c));
                default: return false;
            }
        }

        static bool CanRetarget(Clock c) => !(c.Shape == ClockShape.Ring8 || c.Shape == ClockShape.Ring12 || c.Shape == ClockShape.Roar || c.Shape == ClockShape.LaneFacing || c.Shape == ClockShape.Fan3); // 【RC5・レビュー3】三連射は向け直せない（弓兵の向きで決まる）

        /// <summary>早鐘で早める時計：残り2以上のうち、いちばん近いもの。</summary>
        public static Clock PickForHasten(UnitModel e) => e.Clocks.Where(c => c.Cd >= 2).OrderBy(c => c.Cd).FirstOrDefault();
        /// <summary>遅鐘で遅らせる時計：予告中（残り1）を優先、なければいちばん近いもの。</summary>
        public static Clock PickForDelay(UnitModel e) => e.Clocks.Where(c => c.Cd <= c.Period + 1 && !c.DelayedThisCycle).OrderBy(c => c.Cd).FirstOrDefault(); // 【RC5・レビュー3】1周期に1回まで

        /// <summary>使えるか（対象の正しさだけ。手札・1ターンの枚数は呼ぶ側が見る）。</summary>
        public static bool CanUse(BattleModel m, TacticKind k, UnitModel t1, UnitModel t2)
        {
            if (m == null || m.Player == null || !m.Player.IsAlive) return false;
            switch (TargetOf(k))
            {
                case TacticTarget.None:
                    if (k == TacticKind.Mend) return m.Player.Hp < m.Player.MaxHp;
                    if (k == TacticKind.Guard) return m.PlayerGuard < 2;
                    if (k == TacticKind.Parry) return m.PlayerGuard < 4;
                    if (k == TacticKind.Roar) return m.Enemies.Any(e => e.IsAlive && !e.IsObject && Manhattan(e.Position, m.Player.Position) == 1);
                    return true;
                case TacticTarget.Enemy: return IsValidTarget(m, k, t1);
                default: return IsValidTarget(m, k, t1) && t2 != null && t2.IsAlive && t2 != t1 && !t2.IsPlayer;
            }
        }

        static int Manhattan(Vector2Int a, Vector2Int b) => System.Math.Abs(a.x - b.x) + System.Math.Abs(a.y - b.y);

        /// <summary>盤面への効果を実行して、ログの文を返す。手札・命令枠への効果は effect に入る。</summary>
        public static string Apply(BattleModel m, TacticKind k, UnitModel t1, UnitModel t2, List<BattleEvent> events, out TacticHandEffect effect)
        {
            effect = new TacticHandEffect();
            var p = m.Player;
            string name = Name(k);
            events?.Add(new BattleEvent { Type = BattleEventType.TacticPlayed, Text = name });
            switch (k)
            {
                case TacticKind.Breath: effect.DrawCommands = 2; return $"{name}：命令札を2枚引く";
                case TacticKind.Sift: effect.Sift = true; return $"{name}：手札を2枚まで捨てて引き直す";
                case TacticKind.Muster: effect.ExtraSlots = 1; return $"{name}：このターンの命令枠 +1";
                case TacticKind.GreatMuster: effect.ExtraSlots = 2; return $"{name}：このターンの命令枠 +2";
                case TacticKind.Recall: effect.Recall = true; return $"{name}：最後に使った命令の札を手札へ";
                case TacticKind.Echo: effect.EchoDraw = 1; return $"{name}：まねで敵にダメージが入れば、次のターン +1枚";
                case TacticKind.Focus: p.AttackBonus += 2; return $"{name}：次の直接攻撃 +2";
                case TacticKind.Guard: m.PlayerGuard = System.Math.Max(m.PlayerGuard, 2); return $"{name}：次に受けるダメージ -2";
                case TacticKind.Parry: m.PlayerGuard = System.Math.Max(m.PlayerGuard, 4); return $"{name}：次に受けるダメージ -4";
                case TacticKind.Mend:
                {
                    int healed = p.Heal(3);
                    events?.Add(new BattleEvent { Type = BattleEventType.Healed, Unit = p, Amount = healed, HpAfter = p.Hp, Cell = p.Position });
                    return $"{name}：HP +{healed}";
                }
                case TacticKind.Mimic:
                    foreach (var e in m.Enemies.Where(e => e.IsAlive && !e.IsObject)) e.ForceAllCopy = true;
                    return $"{name}：このラウンド、敵全員があなたの札を全部まねる";
                case TacticKind.Tangle: m.CopyMoveBonus = System.Math.Min(2, m.CopyMoveBonus + 1); return $"{name}：このラウンド、敵のまねる移動 +1マス";
                case TacticKind.Weight: m.CopyMoveBonus = System.Math.Max(-1, m.CopyMoveBonus - 1); return $"{name}：このラウンド、敵のまねる移動 -1マス";
                case TacticKind.Pursuit: m.CopyMoveBonus = System.Math.Min(3, m.CopyMoveBonus + 2); return $"{name}：このラウンド、敵のまねる移動 +2マス";
                case TacticKind.Silence: t1.Silenced = true; return $"{name}：{t1.Name}の固有行動をこのラウンド止める";
                case TacticKind.EarlyBell:
                {
                    var c = PickForHasten(t1);
                    if (c == null || !Clocks.Hasten(m, t1, c)) return $"{name}：早められる大技が無い";
                    return $"{name}：{t1.Name}の{c.Name}が残り{c.Cd}";
                }
                case TacticKind.LateBell:
                {
                    var c = PickForDelay(t1);
                    if (c == null || !Clocks.Delay(m, t1, c)) return $"{name}：遅らせられる大技が無い";
                    return $"{name}：{t1.Name}の{c.Name}が残り{c.Cd}";
                }
                case TacticKind.Toll:
                {
                    int n = 0;
                    foreach (var c in t1.Clocks) if (Clocks.Hasten(m, t1, c)) n++;
                    return $"{name}：{t1.Name}の大技 {n}個を1ターン早めた";
                }
                case TacticKind.Detonate:
                {
                    var c = t1.Clocks.Where(x => x.Cd >= 2).OrderBy(x => x.Cd).FirstOrDefault();
                    if (c == null || !Clocks.Detonate(m, t1, c)) return $"{name}：発動させられる大技が無い";
                    return $"{name}：{PhaseRules.ShortName(t1)}の{c.Name}をこのターンのまねの後に発動"; // 【RC5・レビュー】前は「ターンの終わり」
                }
                case TacticKind.Retarget:
                {
                    var c = t1.Clocks.FirstOrDefault(x => x.Telegraphed && CanRetarget(x));
                    if (c == null || !Clocks.Retarget(m, t1, c, t2)) return $"{name}：向け直せる大技が無い";
                    return $"{name}：{t1.Name}の{c.Name}を{t2.Name}のいる場所へ向け直した";
                }
                case TacticKind.Roar:
                {
                    int n = 0;
                    foreach (var e in m.Enemies.Where(e => e.IsAlive && !e.IsObject && !e.IsImmovable && Manhattan(e.Position, p.Position) == 1).ToList())
                    {
                        var d = e.Position - p.Position;
                        var to = e.Position + d;
                        RulesEngine.ApplyDamage(m, p, null, false, e, 1, DamageKind.Direct, events ?? new List<BattleEvent>());
                        if (e.IsAlive && !m.IsBlocked(to)) RulesEngine.EnterCell(m, e, to, p, null, false, events ?? new List<BattleEvent>());
                        n++;
                    }
                    return $"{name}：隣の敵 {n}体を外へ押した";
                }
            }
            return name;
        }

        public static string Name(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.Breath: return "深呼吸";
                case TacticKind.Sift: return "選別";
                case TacticKind.Muster: return "号令";
                case TacticKind.GreatMuster: return "大号令";
                case TacticKind.Recall: return "再演";
                case TacticKind.EarlyBell: return "早鐘";
                case TacticKind.LateBell: return "遅鐘";
                case TacticKind.Silence: return "沈黙";
                case TacticKind.Mimic: return "模倣命令";
                case TacticKind.Tangle: return "せき立て";
                case TacticKind.Weight: return "重し";
                case TacticKind.Focus: return "集中";
                case TacticKind.Guard: return "残心";
                case TacticKind.Mend: return "手当て";
                case TacticKind.Echo: return "反響";
                case TacticKind.Toll: return "処刑の鐘";
                case TacticKind.Detonate: return "即時爆破";
                case TacticKind.Retarget: return "照準替え";
                case TacticKind.Pursuit: return "追い立て";
                case TacticKind.Roar: return "咆哮";
                case TacticKind.Parry: return "見切り";
                default: return "戦術札";
            }
        }

        /// <summary>札の1行の説明（戦闘中の戦術札の欄・14字ほど）。</summary>
        public static string Short(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.Breath: return "命令札を2枚引く";
                case TacticKind.Sift: return "手札2枚を引き直す";
                case TacticKind.Muster: return "命令枠 +1（まねも +1）";
                case TacticKind.GreatMuster: return "命令枠 +2（まねも +2）";
                case TacticKind.Recall: return "最後の札を手札へ";
                case TacticKind.EarlyBell: return "敵の大技を1ターン早める";
                case TacticKind.LateBell: return "敵の大技を1ターン遅らせる";
                case TacticKind.Silence: return "敵1体の攻撃を止める";
                case TacticKind.Mimic: return "敵全員が全まねになる";
                case TacticKind.Tangle: return "まねで動く敵 +1マス";
                case TacticKind.Weight: return "まねで動く敵 -1マス";
                case TacticKind.Focus: return "次の直接攻撃 +2";
                case TacticKind.Guard: return "次に受けるダメージ -2";
                case TacticKind.Mend: return "HP +3";
                case TacticKind.Echo: return "まねで当たれば次 +1枚";
                case TacticKind.Toll: return "敵の全大技を1ターン早める";
                case TacticKind.Detonate: return "大技を今すぐ撃たせる";
                case TacticKind.Retarget: return "大技の狙いを別の敵へ";
                case TacticKind.Pursuit: return "まねで動く敵 +2マス";
                case TacticKind.Roar: return "隣の敵を押す（まねされない）";
                case TacticKind.Parry: return "次に受けるダメージ -4";
                default: return "";
            }
        }

        /// <summary>
        /// 【RC11・本人「緑の札の効果がいまいち分からない。どういう使い方ができるのかイメージが湧かない」】
        /// 札の顔の下の段：いつ使うと得か（短く：手札の札の幅で切れないように 9 文字まで。【RC11・初見テスト「▸敵が範囲にいるう」で切れていた】）。全部の緑の札が「何が起きる（効果）」と「いつ使う（使いどき）」の2つを持つ。
        /// </summary>
        public static string When(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.Breath: return "札が足りない時";
                case TacticKind.Sift: return "札を入れ替える";
                case TacticKind.Muster: return "まねを1回多く";
                case TacticKind.GreatMuster: return "まねを2回多く";
                case TacticKind.Recall: return "同じ札をもう一度";
                case TacticKind.EarlyBell: return "敵が範囲にいる今";
                case TacticKind.LateBell: return "逃げきれない時";
                case TacticKind.Silence: return "危ない攻撃の前";
                case TacticKind.Mimic: return "全員にまねさせる";
                case TacticKind.Tangle: return "危険まであと1歩";
                case TacticKind.Weight: return "危険の手前で止める";
                case TacticKind.Focus: return "とどめの前";
                case TacticKind.Guard: return "よけきれない時";
                case TacticKind.Mend: return "HPが減ったとき";
                case TacticKind.Echo: return "まねで斬る番";
                case TacticKind.Toll: return "敵が範囲にいる今";
                case TacticKind.Detonate: return "敵が範囲に入った";
                case TacticKind.Retarget: return "大技を敵の上へ";
                case TacticKind.Pursuit: return "遠くの危険へ送る";
                case TacticKind.Roar: return "まねさせず押す";
                case TacticKind.Parry: return "大技が来る時";
                default: return "";
            }
        }

        /// <summary>札の説明（カードに出す一言）。</summary>
        public static string Text(TacticKind k)
        {
            switch (k)
            {
                case TacticKind.Breath: return "命令札を2枚引く。";
                case TacticKind.Sift: return "手札を2枚まで捨てて、同じ枚数引く。";
                case TacticKind.Muster: return "このターンの命令枠 +1（敵も同じ数をまねする）。";
                case TacticKind.GreatMuster: return "このターンの命令枠 +2（敵も同じ数をまねする）。";
                case TacticKind.Recall: return "最後に使った通常の命令の札を手札へ戻す。";
                case TacticKind.EarlyBell: return "敵を1体選ぶ。その敵の大技（いちばん近いもの）を1ターン早める。敵どうしを並べてから撃たせよう。";
                case TacticKind.LateBell: return "敵を1体選ぶ。その敵の大技（いちばん近いもの）を1ターン遅らせる（同じ大技は、発動するまでに1回だけ）。";
                case TacticKind.Silence: return "敵を1体選ぶ。このターン、固有行動と大技を止める（まねはする）。";
                case TacticKind.Mimic: return "このターン、敵全員があなたの札を全部まねる。";
                case TacticKind.Tangle: return "このターン、まねで動く敵は1マス多く進む（あなたは同じ）。危険まであと1マス届かない敵を送り込む（跳躍は2マスのまま）。";
                case TacticKind.Weight: return "このターン、まねで動く敵は1マス少なく進む（最低1・あなたは同じ）。あなたと敵の位置をずらす・敵を危険の手前で止める。";
                case TacticKind.Focus: return "次の直接攻撃 +2。";
                case TacticKind.Guard: return "次に受けるダメージ -2（1回）。";
                case TacticKind.Mend: return "HP +3（1戦に1回）。"; // 【RC2】消耗（無害な敵を残して回復し続ける止め方を防ぐ）
                case TacticKind.Echo: return "このターン、まねで敵にダメージが入ったら次のターン +1枚。";
                case TacticKind.Toll: return "敵を1体選ぶ。その敵の全部の大技を1ターンずつ早める。";
                case TacticKind.Detonate: return "敵を1体選ぶ。その敵の大技を、このターンのまねの後（敵が自分で動く前）に発動させる。";
                case TacticKind.Retarget: return "予告中の大技を撃つ敵と、新しい狙いの敵を選ぶ。大技がその敵のいる場所を狙い直す（周りを打つ技・向いた方へ撃つ技・三連射は向け直せない）。";
                case TacticKind.Pursuit: return "このターン、まねで動く敵は2マス多く進む（あなたは同じ）。遠くの罠・大技・ほかの敵の攻撃へ送り込む（跳躍は2マスのまま）。";
                case TacticKind.Roar: return "あなたの隣の敵を外へ1マス押す（1ダメージ）。敵はこれをまねしない：押しても、あなたが押し返されない。";
                case TacticKind.Parry: return "次に受けるダメージ -4（1回）。";
                default: return "";
            }
        }
    }
}
