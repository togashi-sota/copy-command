using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【検証用】プレイテストの自動記録（本番機能ではない）。戦闘が終わるたび（クリア・敗北・途中でステージを変えた）に、
    /// 1戦分の数字を Markdown で追記する。本人は「感想」の行だけ後から書けばよい。
    /// 保存先：ビルド＝exe と同じフォルダの PlaytestLogs/、Editor＝プロジェクトの Logs/playtest/。1回の起動につき1ファイル。
    /// 記録するのはゲームの数字だけ（名前などの個人情報は記録しない）。プレイ中（Application.isPlaying）だけ動く。テストでは書かない。
    /// </summary>
    public static class PlaytestRecorder
    {
        static string sessionFile;
        /// <summary>【RC12】記録に足す1行（ActRunManager：5分体験のヒント・答え・かかった秒）。</summary>
        public static System.Func<string> Extra;

        public static void Record(TurnManager turn, string result, IReadOnlyList<string> usedCards, IReadOnlyList<Vector2Int> roundEnds, int startHp)
        {
            if (!Application.isPlaying || turn.CurrentStage == null) return;
            if (BuildInfo.PublicBuild || Application.platform == RuntimePlatform.WebGLPlayer) return; // 【完成版】公開版・ブラウザでは書かない（exe の隣に勝手に書かない）
            try
            {
                string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", Application.isEditor ? "Logs/playtest" : "PlaytestLogs");
                Directory.CreateDirectory(dir);
                if (sessionFile == null)
                {
                    sessionFile = Path.Combine(dir, $"playtest_session_{System.DateTime.Now:yyyyMMdd_HHmmss}.md");
                    File.AppendAllText(sessionFile, $"# プレイテスト記録（{System.DateTime.Now:yyyy-MM-dd HH:mm} 開始・自動記録）\n\n各戦闘の「感想」だけ後から書く。\n", Encoding.UTF8);
                }
                File.AppendAllText(sessionFile, Build(turn, result, usedCards, roundEnds, startHp), Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PlaytestRecorder] 記録できなかった：" + e.Message); // 記録の失敗でゲームは止めない
            }
        }

        static string Build(TurnManager turn, string result, IReadOnlyList<string> usedCards, IReadOnlyList<Vector2Int> roundEnds, int startHp)
        {
            var model = turn.Grid.Model;
            var board = model.Board;
            var events = turn.BattleEvents;
            var player = turn.Player.Model;

            // 敵へのダメージの内訳と、敵を倒した原因（倒れる直前のダメージ）
            var damage = new Dictionary<string, int>();
            var kills = new List<string>();
            BattleEvent lastHit = null;
            foreach (var e in events)
            {
                if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer)
                {
                    string k = CauseName(e.Cause);
                    damage[k] = (damage.TryGetValue(k, out int v) ? v : 0) + e.Amount;
                    lastHit = e;
                }
                if (e.Type == BattleEventType.Died && e.Unit != null && !e.Unit.IsPlayer)
                    kills.Add($"{e.Unit.Name}＝{(lastHit != null && lastHit.Unit == e.Unit ? CauseName(lastHit.Cause) : "不明")}");
            }
            int taken = events.Where(e => e.Type == BattleEventType.Damaged && e.Unit == player).Sum(e => e.Amount);
            int healed = events.Where(e => e.Type == BattleEventType.Healed && e.Unit == player).Sum(e => e.Amount);
            int whiffs = events.Count(e => e.Type == BattleEventType.Bumped && e.Unit == player);

            // 使ったマス（あなたが入ったマス）と、ラウンド終了時に中央3×3にいた割合
            var cells = new HashSet<Vector2Int>();
            foreach (var e in events) if (e.Type == BattleEventType.Moved && e.Unit == player) { cells.Add(e.From); cells.Add(e.To); }
            if (cells.Count == 0) cells.Add(player.Position);
            int floor = 0;
            for (int x = 0; x < board.Width; x++) for (int y = 0; y < board.Height; y++) if (!board.IsWall(new Vector2Int(x, y))) floor++;
            int cx = (board.Width - 1) / 2, cy = (board.Height - 1) / 2;
            int inCenter = roundEnds.Count(p => Mathf.Abs(p.x - cx) <= 1 && Mathf.Abs(p.y - cy) <= 1);

            var cards = usedCards.GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}");
            var sb = new StringBuilder();
            sb.Append($"\n## {System.DateTime.Now:HH:mm} {turn.CurrentStage.stageTitle}：{result}\n");
            sb.Append($"- 手札方式：{TurnManager.HandModeName(turn.CurrentHandMode)}／鏡蝕：{(turn.StagnationDebugOn ? "ON" : "OFF")}／長期戦：{(turn.LongBattleDebugOn ? "ON" : "OFF")}\n");
            sb.Append($"- ターン：{turn.TurnNumber}／あなたの HP：{startHp} → {player.Hp}（受けたダメージ {taken}・回復 {healed}）\n");
            sb.Append($"- 使ったカード：{(usedCards.Count == 0 ? "なし" : string.Join("、", cards))}／あなたの空振り（移動がふさがれた）：{whiffs}回\n");
            sb.Append($"- 敵へのダメージ：{(damage.Count == 0 ? "なし" : string.Join("・", damage.Select(kv => $"{kv.Key}{kv.Value}")))}\n");
            sb.Append($"- 倒した原因：{(kills.Count == 0 ? "なし" : string.Join("、", kills))}\n");
            sb.Append($"- 使ったマス：{cells.Count}/{floor}（{100 * cells.Count / Mathf.Max(1, floor)}%）／ラウンド終了時に中央3×3：{inCenter}/{roundEnds.Count}\n");
            var extra = Extra?.Invoke(); // 【RC12】5分体験のヒント・答え・時間（人のテストの記録）
            if (!string.IsNullOrEmpty(extra)) sb.Append("- " + extra + "\n");
            sb.Append("- 感想：\n");
            return sb.ToString();
        }

        static string CauseName(DamageCause c)
        {
            if (c == null) return "不明";
            switch (c.Kind)
            {
                case DamageKind.Direct: return c.Source != null && c.Source.IsPlayer ? "あなたの攻撃" : "同士討ち";
                case DamageKind.Spike: return "トゲ";
                case DamageKind.Fire: return "炎";
                case DamageKind.Trait: return "兵種の技"; // 矢・剣・槍・爆発・騎士のカウントダウン
                case DamageKind.Curse: return "鏡蝕の呪い";
                case DamageKind.Innate: return "固有行動";
                default: return c.Kind.ToString();
            }
        }
    }
}
