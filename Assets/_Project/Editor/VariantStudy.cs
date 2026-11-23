using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using static CopyCommand.EditorTools.LargeHandStudy;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 【研究】移動札の修正案・トゲの数値・攻略の系統の解析（ゲームには何も入れない。解析中だけ実験用の切替を ON）。
    ///  RunMoveVariants：転移・跳躍・滑走の修正案（Logs/move_variants.txt）
    ///  RunSpikeStudy：トゲ 2〜5（Logs/spike_study.txt）
    ///  RunFamilies：盤面ごとの攻略の系統（直接・同士討ち・トゲ・炎・矢・混合）（Logs/families_report.txt）
    /// どれも近似（ビーム探索）。
    /// </summary>
    public static class VariantStudy
    {
        static List<CommandData> Basic() => new List<CommandData> { Card("Walk"), Card("Dash"), Card("Slash"), Card("Push") };
        static StageData Stage(string name) => AssetDatabase.LoadAssetAtPath<StageData>($"Assets/_Project/Data/Stages/{name}.asset");
        static List<List<CommandRecord>> Seqs(List<CommandData> cards) => BattleSolver.Sequences(new BattleSolver.Settings { Cards = cards, MaxSlots = 3 });

        static readonly string[] MoveOnlyCards = { "Walk", "Dash", "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" };
        static readonly string[] FullCards = { "Walk", "Dash", "Slash", "Push", "Rush", "VarRush", "Jump", "Diagonal", "Slide", "Blink", "Swap" };

        // ───────── 転移・跳躍・滑走の修正案 ─────────

        public static void RunMoveVariants()
        {
            var sb = new StringBuilder("【研究】移動札の修正案（解析だけ。ゲームには入れていない）。基本4枚＋その札、近似のビーム探索\n\n");
            try
            {
                sb.AppendLine("■ 転移：0＝今（3マス・壁越し）／1＝壁越し不可（3マス）／2＝2マス（壁越し）");
                Compare(sb, "Blink", new[] { "StageM4", "StageM10", "StageM7" }, m => RulesEngine.ExperimentBlinkMode = m, 3);
                sb.AppendLine("■ 跳躍：0＝今／1＝間のマスにユニットかトゲがあるときだけ跳べる");
                Compare(sb, "Jump", new[] { "StageM4", "StageM10", "StageM7" }, m => RulesEngine.ExperimentJumpMode = m, 2);
                sb.AppendLine("■ 滑走：0＝今（通ったトゲを毎回踏む）／1＝トゲは1回の滑走で1回だけ／2＝床は止まったマスだけ／3＝最大3マス");
                Compare(sb, "Slide", new[] { "StageM4", "StageM10" }, m => RulesEngine.ExperimentSlideMode = m, 4);
                sb.AppendLine("  ○ 滑走の修正案と、トゲの盤面（M5・M6）の危険な攻略（20枚デッキの全種類）");
                var full = Seqs(FullCards.Select(Card).ToList());
                foreach (var name in new[] { "StageM5", "StageM6" })
                    for (int mode = 0; mode < 4; mode++)
                    {
                        RulesEngine.ExperimentSlideMode = mode;
                        var start = BattleModel.FromStage(Stage(name));
                        sb.AppendLine($"    {name} 滑走{mode}：{QuickWins(start, full)}");
                        Write(sb, "Logs/move_variants.txt");
                    }
            }
            finally { RulesEngine.ExperimentBlinkMode = 0; RulesEngine.ExperimentJumpMode = 0; RulesEngine.ExperimentSlideMode = 0; }
            Write(sb, "Logs/move_variants.txt");
        }

        static void Compare(StringBuilder sb, string card, string[] stages, System.Action<int> setMode, int modes)
        {
            var seqs = Seqs(new List<CommandData>(Basic()) { Card(card) });
            foreach (var name in stages)
            {
                var stage = Stage(name);
                var start = BattleModel.FromStage(stage);
                var center = Center(start.Board);
                for (int m = 0; m < modes; m++)
                {
                    setMode(m);
                    var best = Search(start, seqs, (x, ev, cells) => true);
                    var centerOnly = Search(start, seqs, (x, ev, cells) => cells.All(center.Contains));
                    sb.AppendLine($"    {name} 案{m}：最短 {Show(best)}／中央だけ {Show(centerOnly)}");
                    Write(sb, "Logs/move_variants.txt");
                }
                setMode(0);
            }
            sb.AppendLine();
        }

        // ───────── M4 を盤面だけで直せるか ─────────

        /// <summary>
        /// 【研究】M4（弓兵）の中央固定を、カードやルールではなく盤面の変更だけで直せるか。解析用にステージをコピーして盤面を変える（アセットは変えない）。
        /// 結果は Logs/m4_board_fix.txt。
        /// </summary>
        public static void RunBoardFixM4()
        {
            var sb = new StringBuilder("【研究】M4 を盤面だけで直せるか（解析用のコピー。M4 のアセットは変えない）。基本4枚＋転移／＋跳躍、近似\n\n");
            var src = Stage("StageM4");
            var variants = new (string name, string[] map, string facings, string[] fire)[]
            {
                ("原案（弓兵・あなた・敵が同じ行）", null, null, null),
                ("案1 敵Bを右下の隅へ（同じ行に並べない）", new[] { ".......", ".......", "K..P...", ".......", "...#...", ".....E." }, "R", null),
                ("案2 弓兵を上辺に置き下向き、敵Bは左下の隅", new[] { "...K...", ".......", ".......", "...P...", "...#...", "E......" }, "D", null),
                ("案3 案1＋中央が1ラウンドおきに燃える", new[] { ".......", ".......", "K..P...", ".......", "...#...", ".....E." }, "R",
                    new[] { "......./......./..FFF../..FFF../..FFF../.......", "......./......./......./......./......./......." }),
                ("案4 案1＋四隅の回復ルーン（あなた HP 8 から）", new[] { "1.....2", ".......", "K..P...", ".......", "...#...", "4....E3" }, "R", null),
            };
            foreach (var card in new[] { "Blink", "Jump" })
            {
                var seqs = Seqs(new List<CommandData>(Basic()) { Card(card) });
                sb.AppendLine($"■ 基本4枚＋{Card(card).displayName}");
                foreach (var v in variants)
                {
                    var copy = Object.Instantiate(src);
                    if (v.map != null) copy.map = string.Join("\n", v.map);
                    if (v.facings != null) copy.enemyFacings = v.facings;
                    copy.firePatterns = v.fire != null ? new List<string>(v.fire) : new List<string>();
                    if (v.name.StartsWith("案4")) copy.playerStartHp = 8;
                    var start = BattleModel.FromStage(copy);
                    var center = Center(start.Board);
                    var best = Search(start, seqs, (m, ev, cells) => true);
                    var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                    var fam = Families(start, seqs);
                    sb.AppendLine($"  {v.name}：最短 {Show(best)}／中央だけ {Show(centerOnly)}");
                    sb.AppendLine($"      系統：{FamilyText(fam)}");
                    Object.DestroyImmediate(copy);
                    Write(sb, "Logs/m4_board_fix.txt");
                }
                sb.AppendLine();
            }
        }

        // ───────── 新しい攻撃札を入れたときの攻略の系統（2026-10-04）─────────

        /// <summary>
        /// 【研究】攻撃札を増やした手札（一歩・駆け足・跳躍・斬撃・突き・押し斬り・薙ぎ払い・闘気・押し出し）で、M4〜M12 の攻略の系統を調べ、
        /// 前の手札（基本4枚＋跳躍＋入れ替え）と比べる。「攻撃札が無いから仕方なくギミック」になっていないかを見る。結果は Logs/families_attack.txt。
        /// </summary>
        public static void RunFamiliesNewAttacks()
        {
            var sb = new StringBuilder("【研究】攻撃札を増やした手札での攻略の系統（近似）" + "\n");
            sb.AppendLine("前＝基本4枚＋跳躍＋入れ替え（直接攻撃は斬撃だけ）／新＝一歩・駆け足・跳躍・斬撃・突き・押し斬り・薙ぎ払い・闘気・押し出し\n");
            var before = Seqs(new List<CommandData>(Basic()) { Card("Jump"), Card("Swap") });
            var after = Seqs(new[] { "Walk", "Dash", "Jump", "Slash", "Thrust", "PushSlash", "Sweep", "Spirit", "Push" }.Select(Card).ToList());
            foreach (var name in new[] { "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageM11", "StageM12", "StageMB1" })
            {
                var stage = Stage(name);
                var start = BattleModel.FromStage(stage);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var f0 = Families(start, before);
                var f1 = Families(start, after);
                string Rounded(Dictionary<string, Win> f) => string.Join("・", f.Keys.Select(Round).Distinct());
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（{watch.Elapsed.TotalMinutes:0.0}分）");
                sb.AppendLine($"  前：丸め {f0.Keys.Select(Round).Distinct().Count()}（{Rounded(f0)}）　{FamilyText(f0)}");
                sb.AppendLine($"  新：丸め {f1.Keys.Select(Round).Distinct().Count()}（{Rounded(f1)}）　{FamilyText(f1)}");
                Write(sb, "Logs/families_attack.txt");
            }
        }

        // ───────── ACT1 通常戦8戦の事前検証 ─────────

        /// <summary>
        /// 【研究】ACT1_NORMAL_BATTLES.md の8戦を、解析用のコピー（StageM4 を元に盤面を差し替え）で事前検証する。アセットは作らない。
        /// 提案中のルールで見る：トゲ3・滑走は床が止まったマスだけ（ExperimentSlideMode=2）。カード＝基本4枚＋入れ替え＋滑走（ACT1 の通常取得札の代表）。
        /// 合格ライン（案）：①中央だけの攻略が最短より明確に損 ②丸めた系統が2つ以上 ③1ターン勝利なし。結果は Logs/normal_battles_check.txt。
        /// </summary>
        public static void RunNormalBattleCheck()
        {
            var sb = new StringBuilder("【研究】ACT1 通常戦8戦の事前検証（解析用のコピー。トゲ3・滑走は止まったマスだけ・基本4枚＋入れ替え＋滑走、近似）" + "\n\n");
            var src = Stage("StageM4");
            string none = "";
            var designs = new (string name, string[] map, string facings, string[] fire, int startHp, int runeUses, int[] mirror)[]
            {
                ("N1 片寄せ", new[] { ".....E.", ".......", "......E", ".......", ".P.....", ".....E." }, none, null, 0, 3, new int[0]),
                ("N2 分断の壁", new[] { "...#...", ".E.#...", ".......", "...#.P.", "...#.E.", "...#..." }, none, null, 0, 3, new int[0]),
                ("N3 外周のトゲ", new[] { "^^...^^", "...E...", ".......", "...P...", "....E..", "^^...^^" }, none, null, 0, 3, new int[0]),
                ("N4 十字の射線", new[] { ".......", "K......", "...E...", ".......", "......K", "...P..." }, "RDL", null, 0, 3, new int[0]),
                ("N5 巡るルーン", new[] { "1.....2", "..E....", ".......", "...P...", ".....E.", "4.....3" }, none, null, 8, 3, new int[0]),
                ("N6 燃える中央", new[] { ".E.....", ".......", ".......", ".......", "......K", ".P....." }, "DL",
                    new[] { "......./......./..FFF../..FFF../..FFF../.......", "......./......./......./......./......./......." }, 0, 3, new int[0]),
                ("N7 左右の安全地帯", new[] { ".......", ".E...E.", ".......", "...P...", ".......", "...E..." }, none,
                    new[] { "FFF..../FFF..../FFF..../FFF..../FFF..../FFF....", "....FFF/....FFF/....FFF/....FFF/....FFF/....FFF" }, 0, 3, new[] { 1 }),
                ("N8 二つの祭壇", new[] { "1..#...", "...#.K.", "..E....", ".......", "...#E..", "..P#..2" }, "LDD", null, 0, 2, new[] { 2 }),
            };
            var cards = new List<CommandData>(Basic()) { Card("Swap"), Card("Slide") };
            var seqs = Seqs(cards);
            var richSeqs = Seqs(new List<CommandData>(cards) { Card("Jump") });
            try
            {
                RulesEngine.ExperimentSlideMode = 2;
                foreach (var d in designs)
                {
                    var copy = Object.Instantiate(src);
                    copy.map = string.Join("\n", d.map);
                    copy.enemyFacings = d.facings;
                    copy.spikeDamage = 3;
                    copy.firePatterns = d.fire != null ? new List<string>(d.fire) : new List<string>();
                    copy.fireDamage = 3;
                    copy.runeUses = d.runeUses;
                    copy.runeHeal = 3;
                    copy.playerStartHp = d.startHp;
                    var start = BattleModel.FromStage(copy);
                    var enemies = start.Enemies.ToList();
                    foreach (int i in d.mirror) if (i < enemies.Count) enemies[i].CopyTrait = CopyTrait.Mirror;
                    var center = Center(start.Board);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var best = Search(start, seqs, (m, ev, cells) => true);
                    var centerOnly = Search(start, seqs, (m, ev, cells) => cells.All(center.Contains));
                    var fam = Families(start, seqs);
                    var rounded = fam.Keys.Select(Round).Distinct().ToList();
                    sb.AppendLine($"=== {d.name}（{watch.Elapsed.TotalMinutes:0.0}分）");
                    sb.AppendLine($"  最短 {Show(best)}／中央だけ {Show(centerOnly)}");
                    sb.AppendLine($"  丸めた系統 {rounded.Count}（{string.Join("・", rounded)}）：{FamilyText(fam)}");
                    sb.AppendLine($"  ＋跳躍（レア）で：{QuickWins(start, richSeqs)}");
                    Object.DestroyImmediate(copy);
                    Write(sb, "Logs/normal_battles_check.txt");
                }
            }
            finally { RulesEngine.ExperimentSlideMode = 0; }
        }

        static string Round(string family) =>
            family.StartsWith("混合") ? "混合" : family == "トゲ" || family == "炎" || family == "矢" ? "環境" : family;

        // ───────── トゲ 2〜5 ─────────

        public static void RunSpikeStudy()
        {
            var sb = new StringBuilder("【研究】トゲのダメージ 2〜5（敵HP 8・あなたHP 12）。解析だけ（ステージのコピーで値を変える）。近似\n");
            sb.AppendLine("最短・系統＝基本4枚（一歩・駆け足・斬撃・押し出し）。移動だけ＝攻撃札（斬撃・押し出し）なしの全移動札。1ターン勝利＝20枚デッキの全種類\n");
            var basicSeqs = Seqs(Basic());
            var moveSeqs = Seqs(MoveOnlyCards.Select(Card).ToList());
            var fullSeqs = Seqs(FullCards.Select(Card).ToList());
            foreach (var name in new[] { "StageM3", "StageM5", "StageM6" })
            {
                var src = Stage(name);
                sb.AppendLine($"=== {src.name} {src.stageTitle}");
                foreach (int spike in new[] { 2, 3, 4, 5 })
                {
                    var copy = Object.Instantiate(src);
                    copy.spikeDamage = spike;
                    var start = BattleModel.FromStage(copy);
                    var best = Search(start, basicSeqs, (m, ev, cells) => true);
                    var fam = Families(start, basicSeqs);
                    var moveOnly = Search(start, moveSeqs, (m, ev, cells) => true);
                    sb.AppendLine($"  トゲ{spike}：最短 {Show(best)}／移動だけで勝つ {Show(moveOnly)}");
                    sb.AppendLine($"         系統：{FamilyText(fam)}");
                    sb.AppendLine($"         全種類：{QuickWins(start, fullSeqs)}");
                    Object.DestroyImmediate(copy);
                    Write(sb, "Logs/spike_study.txt");
                }
                sb.AppendLine();
            }
            Write(sb, "Logs/spike_study.txt");
        }

        // ───────── 攻略の系統 ─────────

        static readonly string[] FamilyStages = { "StageM1", "StageM2", "StageM3", "StageM4", "StageM5", "StageM6", "StageM6b", "StageM7", "StageM8", "StageM9", "StageM10", "StageM11", "StageM12", "StageMB1" };

        /// <summary>全ステージの攻略の系統。カード：基本4枚＋跳躍＋入れ替え（M1〜M3 は基本4枚）。</summary>
        public static void RunFamilies()
        {
            var sb = new StringBuilder("【研究】攻略の系統（勝ち筋の数）。近似（系統ごとに探索の枠を分けたビーム探索・1ターンに60状態）\n");
            sb.AppendLine("系統＝敵へのダメージの6割以上を占める原因（直接＝あなたの攻撃／同士討ち／トゲ／炎／矢）。6割に届かなければ「混合」。");
            sb.AppendLine("最短の勝ちから1ターン遅いものまでを集める。T＝ターン、HP＝残りHP。\n");
            var rich = Seqs(new List<CommandData>(Basic()) { Card("Jump"), Card("Swap") });
            var basic = Seqs(Basic());
            foreach (var name in FamilyStages)
            {
                var stage = Stage(name);
                if (stage == null) continue;
                var start = BattleModel.FromStage(stage);
                bool early = name == "StageM1" || name == "StageM2" || name == "StageM3";
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var fam = Families(start, early ? basic : rich);
                sb.AppendLine($"=== {stage.name} {stage.stageTitle}（{(early ? "基本4枚" : "基本4枚＋跳躍＋入れ替え")}・{watch.Elapsed.TotalMinutes:0.0}分）");
                sb.AppendLine($"  系統 {fam.Count} 種類：{FamilyText(fam)}");
                foreach (var kv in fam.OrderBy(k => k.Value.turns))
                    sb.AppendLine($"    {kv.Key}：{kv.Value.turns}T HP{kv.Value.hp}　内訳 {kv.Value.mix}　例：{string.Join(" ⇒ ", kv.Value.path.Select(Seq))}");
                Write(sb, "Logs/families_report.txt");
            }
        }

        internal class Win { public int turns, hp; public string mix; public List<List<CommandRecord>> path; }

        static string FamilyText(Dictionary<string, Win> fam) =>
            fam.Count == 0 ? "勝ち筋が見つからず" : string.Join("／", fam.OrderBy(k => k.Value.turns).Select(k => $"{k.Key} {k.Value.turns}T HP{k.Value.hp}"));

        static string Category(BattleEvent e)
        {
            switch (e.Cause.Kind)
            {
                case DamageKind.Direct: return e.Cause.Source != null && e.Cause.Source.IsPlayer ? "直接" : "同士討ち";
                case DamageKind.Spike: return "トゲ";
                case DamageKind.Fire: return "炎";
                case DamageKind.Trait: return "矢";
                default: return "その他";
            }
        }

        static string Dominant(Dictionary<string, int> dmg, out string mix)
        {
            int total = dmg.Values.Sum();
            mix = string.Join("・", dmg.OrderByDescending(k => k.Value).Select(k => $"{k.Key}{k.Value}"));
            if (total == 0) return "なし";
            var top = dmg.OrderByDescending(k => k.Value).ToList();
            if (top[0].Value * 10 >= total * 6) return top[0].Key;
            return $"混合（{top[0].Key}＋{top[1].Key}）";
        }

        /// <summary>
        /// 系統ごとの勝ち筋を集める。ビームの枠を「その時点のダメージの主な原因」ごとに分けて、1つの系統だけが残るのを防ぐ。
        /// 最初の勝ちが見つかったターンの次のターンまで続ける。
        /// </summary>
        internal static Dictionary<string, Win> Families(BattleModel start, List<List<CommandRecord>> seqs, int maxStates = 60, int maxTurns = 7)
        {
            var wins = new Dictionary<string, Win>();
            var frontier = new List<(BattleModel m, List<List<CommandRecord>> path, Dictionary<string, int> dmg)>
                { (start.Clone(), new List<List<CommandRecord>>(), new Dictionary<string, int>()) };
            var seen = new HashSet<string> { BattleSolver.Key(start) };
            int firstWin = -1;
            for (int turn = 1; turn <= maxTurns; turn++)
            {
                if (firstWin > 0 && turn > firstWin + 1) break;
                var next = new List<(BattleModel, List<List<CommandRecord>>, Dictionary<string, int>)>();
                foreach (var (model, path, dmg) in frontier)
                    foreach (var seq in seqs)
                    {
                        var m = model.Clone();
                        var events = new List<BattleEvent>();
                        var outcome = TurnRules.PlayTurn(m, seq, null, events);
                        if (outcome == TurnOutcome.GameOver) continue;
                        var d = new Dictionary<string, int>(dmg);
                        foreach (var e in events)
                            if (e.Type == BattleEventType.Damaged && e.Unit != null && !e.Unit.IsPlayer && e.Cause != null)
                            {
                                string c = Category(e);
                                d[c] = (d.TryGetValue(c, out int v) ? v : 0) + e.Amount;
                            }
                        var np = new List<List<CommandRecord>>(path) { seq };
                        if (outcome == TurnOutcome.Cleared)
                        {
                            if (firstWin < 0) firstWin = turn;
                            string fam = Dominant(d, out string mix);
                            if (!wins.TryGetValue(fam, out var w) || turn < w.turns || (turn == w.turns && m.Player.Hp > w.hp))
                                wins[fam] = new Win { turns = turn, hp = m.Player.Hp, mix = mix, path = np };
                            continue;
                        }
                        if (seen.Add(BattleSolver.Key(m))) next.Add((m, np, d));
                    }
                if (next.Count == 0) break;
                // 系統（今の主な原因）ごとに枠を分けて残す
                var buckets = next.GroupBy(x => Dominant(x.Item3, out _)).ToList();
                int per = Mathf.Max(8, maxStates / Mathf.Max(1, buckets.Count));
                frontier = buckets.SelectMany(g => g.OrderBy(x => StagnationRule.EnemyHpTotal(x.Item1) * 100 - x.Item1.Player.Hp * 10).Take(per)).ToList();
            }
            return wins;
        }
    }
}
