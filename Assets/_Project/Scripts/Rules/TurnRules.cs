using System.Collections.Generic;
using System.Linq;

namespace CopyCommand
{
    public enum TurnOutcome
    {
        Continue,  // 戦闘は続く
        Cleared,   // 敵を全員倒した
        GameOver,  // プレイヤーが倒れた
    }

    /// <summary>
    /// 1ターン（PLAYER TURN → ENEMY TURN）のルール上の進行。GameObject を使わない。
    /// 解析（BattleSolver）とテストで使う。実際のゲームの TurnManager と同じ順番・同じ止まり方をする
    /// （TurnRulesParityTests で、同じ入力から同じ結果になることを確認している）。
    ///   1. プレイヤーが ①→②→③ を実行（プレイヤーが倒れた／敵が全滅したら残りは実行しない）
    ///   2. 敵A：固有行動（あれば）→ コピー命令（コピー特性を反映）。敵A が倒れたら敵A の残りは実行しない
    ///   3. 敵B：同様 …（プレイヤーが倒れた／敵が全滅した時点で終わり）
    /// </summary>
    public static class TurnRules
    {
        /// <summary>
        /// 【研究・解析専用の実験】true のとき「あなたが空振りした移動命令（位置が変わらなかった移動）は、敵にコピーされない」。
        /// ゲーム本体（TurnManager）は使わない。解析（LargeHandStudy.RunWhiffRuleExperiment）だけが一時的に true にする。
        /// </summary>
        public static bool ExperimentSkipWhiffedMoves;

        /// <summary>
        /// 【研究・解析専用の実験】多命令の再設計の比較（2026-10-05）。0＝今のルール（ゲームは常に 0）。
        /// 1＝案B：4命令以上のラウンドは兵種の行動 +1／2＝案C：共鳴（4命令以上のラウンドごとに +1 がたまる）／3＝案D：4つ目以降の命令は敵が2回コピー／4＝長期戦の圧力：6ラウンド目から兵種の行動 +1。
        /// </summary>
        public static int ExperimentMultiRule;

        /// <summary>
        /// 【完成版・検証中】使わなかった命令枠1つにつき、次のターンに戦術札を何枚引くか（0＝無し）。
        /// 「今は1〜2枚で止めて、次のターンの準備をする」選択を作るための案（研究：DESIGN_RECOMMENDATIONS C6）。ゲームと解析で共用。
        /// </summary>
        public static int UnusedSlotTacticDraw = 0;
        /// <summary>
        /// 【RC6・解析だけ】孤立した敵（最後の1体）は移動をまねない。**採らなかった**：最後の1体のターン 30% → 43%・トゲの通路の負けが増えた
        /// （まねの移動で敵をトゲや射程へ連れてくるのが、片付けの手段になっていた：Logs/rc6/rules/r_nm.txt）。解析は PZ_LASTNOMOVE=1。
        /// </summary>
        public static bool IsolationNoMove = false; // 【RC6】0（緑の手札は1枚・毎ターン1枚引く：使わない枠で引いても手札が満杯で引けず、表示だけが残った）

        /// <param name="noCopy">【研究・解析専用】コピーさせない敵の Id（「コピーを読み落とした人の見え方」「コピーが無い世界」の比較に使う）。ゲームは null。</param>
        public static TurnOutcome PlayTurn(BattleModel model, IReadOnlyList<CommandRecord> playerCommands, CommandData innate,
            List<BattleEvent> events = null, List<string> log = null, ICollection<int> noCopy = null)
        {
            model.BeginRound();
            if (ExperimentMultiRule == 1) model.TraitBonus = playerCommands.Count >= 4 ? 1 : 0;
            else if (ExperimentMultiRule == 2) { if (playerCommands.Count >= 4) model.Resonance++; model.TraitBonus = model.Resonance; }
            else if (ExperimentMultiRule == 4) model.TraitBonus = model.Environment.Round >= 6 ? 1 : 0;
            var outcome = PlayCommandsAndCopies(model, playerCommands, innate, events, log, noCopy, null, null, model.V4);
            if (outcome != TurnOutcome.Continue) return outcome;
            // 【検証中】③ 兵種の行動 → ④ 環境（兵種・環境が無いステージでは何も起きない）
            outcome = PhaseRules.TraitPhaseAfter(model, events, log); // 【RC2】V4：兵種の行動は コピーの前に済んでいる（追撃型だけここ）
            if (outcome != TurnOutcome.Continue) return outcome;
            return PhaseRules.EnvironmentPhase(model, events, log);
        }

        /// <summary>① プレイヤーの命令 → ② 敵のコピー（予告の計算にも使う：この後の位置から、矢の線や移動先を出す）。</summary>
        public static TurnOutcome PlayCommandsAndCopies(BattleModel model, IReadOnlyList<CommandRecord> playerCommands, CommandData innate,
            List<BattleEvent> events = null, List<string> log = null, ICollection<int> noCopy = null, CopyCore.CopyStep onStep = null, ICollection<int> skipSlots = null, bool withTrait = false)
        {
            // 【検証中・Copy Core V2】「すぐ」の方式：命令1つごとに、あなた → 敵A → 敵B …
            if (CopyCore.IsImmediate(model.CopyMode)) return CopyCore.PlayImmediate(model, playerCommands, events, log, noCopy, skipSlots, onStep);
            var player = model.Player;
            var skipped = new HashSet<int>();
            if (ExperimentSkipWhiffedMoves)
            {
                for (int i = 0; i < playerCommands.Count; i++)
                {
                    var before = player.Position;
                    RunCommands(model, player, new[] { playerCommands[i] }, false, events, log);
                    if (IsMove(playerCommands[i].Command.type) && player.Position == before) skipped.Add(i);
                    if (!model.Player.IsAlive || PuzzleRules.Won(model)) break;
                }
            }
            else RunCommands(model, player, playerCommands, false, events, log);
            player.AttackBonus = 0; // 【検証中】闘気はそのユニットの命令列の中だけ
            var outcome = Check(model);
            if (outcome != TurnOutcome.Continue) return outcome;

            // 【Combat V4 RC2】あなたの命令 → 敵の兵種の行動・大技 → 敵のコピー（追撃型だけ コピーのあと）
            if (withTrait && model.V4)
            {
                outcome = PhaseRules.TraitPhase(model, events, log, 1);
                if (outcome != TurnOutcome.Continue) return outcome;
            }

            if (skipSlots != null) skipped.UnionWith(skipSlots);
            var copied = EnemyReplayController.ToCopied(playerCommands, skipped, model.CopyMode); // 【検証中】忍び足・反転斬り・逆さ写し・B 反対向き
            if (!model.CopyEnabled) copied.Clear(); // 【Copy Core V2】COPY OFF
            ApplyCopyMoveBonus(model, copied); // 【Combat V4】操糸・重し
            if (ExperimentMultiRule == 3 && copied.Count > 3) copied.AddRange(copied.Skip(3).Select(r => r.Clone()).ToList());
            RulesEngine.UpdateIsolation(model); // 【RC6】あなたの命令と敵の攻撃で1体になった敵は、このターンから移動をまねない

            foreach (var enemy in new List<UnitModel>(model.Enemies))
            {
                if (!enemy.IsAlive || (noCopy != null && noCopy.Contains(enemy.Id)) || CopyCore.Stunned(enemy)) continue;
                if (innate != null)
                {
                    string text = RulesEngine.Execute(model, new CommandContext(enemy, new CommandRecord(innate, Direction.Up, 0), false, true), events);
                    log?.Add(text + "【固有行動】");
                    outcome = Check(model);
                    if (outcome != TurnOutcome.Continue) return outcome;
                }
                var traitApplied = EnemyReplayController.ApplyTrait(copied, enemy.CopyTrait);
                var mine = CopyKinds.Filter(enemy, traitApplied); // 【Copy Core V3】技写し・歩写しは一部だけ写す
                if (onStep == null) RunCommands(model, enemy, mine, true, events, log);
                else
                {
                    // 解析・しるし用：コピー1つずつの出来事を渡す（番号は、あなたの命令の何番目をコピーしたか）
                    var map = Enumerable.Range(0, playerCommands.Count).Where(i => !playerCommands[i].Command.notCopied && !skipped.Contains(i)).ToList();
                    if (playerCommands.Any(r => r.Command.type == CommandType.ReverseCopy)) map.Reverse();
                    if (mine.Count != traitApplied.Count) // 写さなかった命令の番号も外す
                        map = Enumerable.Range(0, System.Math.Min(map.Count, traitApplied.Count)).Where(k => CopyKinds.Accepts(enemy, traitApplied[k].Command)).Select(k => map[k]).ToList();
                    for (int k = 0; k < mine.Count; k++)
                    {
                        if (CopyCore.Stunned(enemy) || enemy.Dormant) break; // 【Puzzle v2】剣の像は1回斬らせたら眠る（同じターンの残りは斬らない）
                        var step = new List<BattleEvent>();
                        string text = RulesEngine.Execute(model, new CommandContext(enemy, mine[k], true), step); // log が null でも必ず実行（?. は右辺ごと飛ばす）
                        log?.Add(text);
                        onStep(enemy, k < map.Count ? map[k] : -1, mine[k], step);
                        events?.AddRange(step);
                        if (!model.Player.IsAlive || PuzzleRules.Won(model) || !enemy.IsAlive) break;
                    }
                }
                enemy.AttackBonus = 0;
                outcome = Check(model);
                if (outcome != TurnOutcome.Continue) return outcome;
            }
            return TurnOutcome.Continue;
        }

        static void RunCommands(BattleModel model, UnitModel actor, IReadOnlyList<CommandRecord> commands, bool isCopy,
            List<BattleEvent> events, List<string> log)
        {
            foreach (var c in commands)
            {
                if (isCopy && (CopyCore.Stunned(actor) || actor.Dormant)) break; // 【V3】途中で守りが割れた・段階が変わったら、残りはまねない（【Puzzle v2】眠った像も）
                string text = RulesEngine.Execute(model, new CommandContext(actor, c, isCopy), events);
                log?.Add(text);
                if (!model.Player.IsAlive || PuzzleRules.Won(model) || !actor.IsAlive) break;
            }
        }

        /// <summary>【Combat V4】操糸（＋）・重し（－）：このラウンドに敵がコピーする移動の距離を変える（最低 1）。ゲーム（TurnManager）と解析で共用。</summary>
        public static void ApplyCopyMoveBonus(BattleModel model, List<CommandRecord> copied)
        {
            if (model == null || model.CopyMoveBonus == 0 || copied == null) return;
            for (int i = copied.Count - 1; i >= 0; i--)
            {
                var r = copied[i];
                if (!IsMove(r.Command.type) || r.Command.type == CommandType.Swap) continue;
                if (r.Command.type == CommandType.Walk)
                {
                    // 一歩は距離を持たない：操糸なら、同じ向きにもう1歩ずつ（重しは最低1歩のまま）
                    for (int k = 0; k < model.CopyMoveBonus; k++) copied.Insert(i + 1, new CommandRecord(r.Command, r.Direction, r.Distance, r.SourceCard) { CopyDelta = r.CopyDelta });
                }
                else copied[i] = new CommandRecord(r.Command, r.Direction, System.Math.Max(1, r.Distance + model.CopyMoveBonus), r.SourceCard) { CopyDelta = r.CopyDelta };
            }
        }

        static bool IsMove(CommandType t) => t == CommandType.Walk || t == CommandType.Rush || t == CommandType.Jump || t == CommandType.Diagonal ||
                                             t == CommandType.Slide || t == CommandType.Blink || t == CommandType.Swap || t == CommandType.WallKick || t == CommandType.Vault;

        /// <summary>勝敗の判定（TurnManager と同じ順：敵の全滅を先に見る）。</summary>
        public static TurnOutcome Check(BattleModel model)
        {
            if (PuzzleRules.Won(model)) return TurnOutcome.Cleared; // 【Puzzle v2】討伐は全滅、ほかは目標の達成（脱出はあなたが生きていること込み）
            if (!model.Player.IsAlive) return TurnOutcome.GameOver;
            return TurnOutcome.Continue;
        }
    }
}
