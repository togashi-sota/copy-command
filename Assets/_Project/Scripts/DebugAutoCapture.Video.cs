using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【RC7・本人の依頼「CM みたいな紹介動画（ナレーションなし・約3分）」】-capturevideo 1：見どころの場面を本物のルールで自動に遊び、
    /// 30fps のコマ（JPG）を書き出す。効果音・BGM は鳴った時刻と大きさを timeline.txt に書き、あとで同じ WAV から音を合成する（tools/video/）。
    /// 字幕も tools/video/ で後から付ける（場面の区切りは timeline の scene 行）。開発版だけ（公開版には DebugAutoCapture が入らない）。
    /// </summary>
    public partial class DebugAutoCapture
    {
        bool videoRecording;
        /// <summary>コマの倍率（-capturesuper 1 のとき 2）。</summary>
        int videoSuper => proposalSuper ? 2 : 1;
        int videoFrame;
        readonly List<string> videoTimeline = new List<string>();

        void VMark(string kind, string text) => videoTimeline.Add($"{videoFrame}\t{kind}\t{text}");

        /// <summary>毎フレームの終わりに1コマ（撮影中だけ）。Time.captureFramerate＝30 なので、ゲームの時間は1コマ＝1/30 秒ずつ進む（書き出しが遅くても動きは滑らか）。</summary>
        IEnumerator VideoFrames()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (!videoRecording) continue;
                var tex = ScreenCapture.CaptureScreenshotAsTexture(videoSuper); // 2＝縦横2倍（盤面を寄せても粗くならない）
                File.WriteAllBytes(Path.Combine(folder, $"f{videoFrame:D5}.jpg"), tex.EncodeToJPG(90));
                Destroy(tex);
                videoFrame++;
            }
        }

        /// <summary>場面の始め（字幕と編集の区切り）。</summary>
        void VScene(string name) => VMark("scene", name);

        static IEnumerator Hold(float seconds) { yield return new WaitForSeconds(seconds); }

        IEnumerator VWaitTurn(TurnManager turn, float max = 40f)
        {
            float t0 = Time.time;
            while ((turn.State == BattleState.PlayerExecuting || turn.State == BattleState.EnemyExecuting) && Time.time - t0 < max) yield return null;
        }

        /// <summary>部屋を研究室の戦闘として読む（見出しを付け替えた写し・開始配置を指定）。</summary>
        IEnumerator VLoadRoom(TurnManager turn, string stageName, string title, int layout = 0, int hp = -1, string map = null, string hps = null, string kinds = null, string opts = null)
        {
            var src = turn.Stages.FirstOrDefault(s => s != null && s.name == stageName);
            if (src == null) { VMark("error", "no stage " + stageName); yield break; }
            var stage = Instantiate(src);
            stage.name = src.name;
            if (title != null) stage.stageTitle = title;
            if (map != null) { stage.map = map; stage.enemyHpList = hps ?? ""; stage.enemyCopyTypes = kinds ?? ""; stage.enemyFacings = ""; } // 説明用の小さな盤面（見た目は元の部屋）
            // 【就活資料 V8】説明用の盤面の切り替え（opts：nofire＝仕掛けの床なし・fire=形（/ で行・部屋ファイルと同じ）＝その1つだけ・still＝敵が自分で動かない・noclock＝大技なし・notraits＝敵は まねるだけ）
            bool noTraits = false;
            foreach (var o in (opts ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                if (o == "notraits") noTraits = true; // 敵は まねるだけ（はじめての戦闘と同じ：自分の攻撃・接近なし）
                else if (o == "nofire") stage.firePatterns = new List<string>();
                else if (o.StartsWith("fire=")) stage.firePatterns = new List<string> { o.Substring(5) };
                else if (o == "still") stage.stillEnemies = true;
                else if (o == "noclock") stage.noClocks = true;
            }
            if (layout > 0 && stage.altMaps != null && layout <= stage.altMaps.Count) { stage.map = stage.altMaps[layout - 1]; stage.altMaps = new List<string>(); }
            else stage.altMaps = new List<string>();
            if (hp <= 0 || hp > V4Content.StartHp) hp = V4Content.StartHp; // 今のゲームの最大 HP（前は 20 のままだった）
            var setup = new ActBattleSetup { Hp = hp, MaxHp = V4Content.StartHp, BaseHand = 7, Seed = 12345, Rng = new System.Random(12345), TidyMax = ActContent.TidyMax };
            setup.NoTraits = noTraits;
            if (map == null) ActRunManager.ApplyLabPower(setup, src.name); // 【就活資料 2026-11-18】本編の部屋は、ふつう出る階の敵の強さで（前は 7〜8階の強さ＝雑魚の赤 3 で撮っていた）
            foreach (var n in ActContent.StartDeck) { var c = Card(n); if (c != null) setup.Deck.Add(c); }
            foreach (var n in V4Content.StartTactics) { var c = Card(n); if (c != null) setup.TacticCards.Add(c); }
            setup.Deck.Add(Card("Whirl")); setup.Deck.Add(Card("Pierce"));
            var act = FindAnyObjectByType<ActRunManager>();
            act.StartLabV4(turn.Stages.ToList().IndexOf(src)); // 研究室の戦闘の画面へ（ランの画面を閉じる）
            yield return null;
            turn.LoadActBattle(stage, setup); // 見出しを付け替えた写しで読み直す
            yield return null;
        }

        /// <summary>
        /// 手順を遊ぶ（pzsearch の probe と同じ書き方："Walk:R,Slash:U|T:EarlyBell@B,Thrust:L"）。札は撮影のため手札に寄せるだけ（ルールは変えない）。
        /// 札を選ぶ → 向きの試し（範囲・「！ 〇ダメ」）を見せる → 置く → 実行（ふつうの速さ）→ 結果を見せる。
        /// </summary>
        /// <summary>場面の結果を timeline に（敵どうし・大技・床・あなたの攻撃で、敵が何回・いくつ傷ついたか。字幕と確かめ用）。</summary>
        void VResult(TurnManager turn, int from)
        {
            var ev = turn.BattleEvents;
            int copy = 0, clock = 0, floor = 0, direct = 0, kills = 0, back = 0;
            for (int i = from; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.Type == BattleEventType.Died && e.Unit != null && !e.Unit.IsPlayer) kills++;
                if (e.Type != BattleEventType.Damaged || e.Unit == null || e.Amount <= 0 || e.Cause == null) continue;
                if (e.Unit.IsPlayer) { if (e.Cause.ByCopy) back += e.Amount; continue; }
                if (e.Cause.ByCopy) copy++; else if (e.Cause.Kind == DamageKind.Clock) clock++; else if (e.Cause.Kind == DamageKind.Spike || e.Cause.Kind == DamageKind.Fire) floor++; else if (e.Cause.Source != null && e.Cause.Source.IsPlayer) direct++; else copy++;
            }
            VMark("result", $"まね{copy}・大技{clock}・床{floor}・あなた{direct}・撃破{kills}・まね返し{back}");
        }

        IEnumerator VPlay(TurnManager turn, string plan, float look = 0.9f, float after = 1.2f)
        {
            int evStart = turn.BattleEvents.Count;
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var turnText in plan.Split('|'))
            {
                if (turn.State != BattleState.PlayerInput) break;
                var steps = turnText.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                // 必要な札を手へ（撮影のための並べ替えだけ）
                foreach (var st in steps.Where(x => !x.StartsWith("T:")))
                {
                    string name = st.Split(':')[0];
                    int need = steps.Count(x => x.Split(':')[0] == name), have = turn.Deck.Hand.Count(c => c.Data.name == name);
                    if (have < need) { var cd = Card(name); for (int k = have; k < need && cd != null; k++) ForceCardInHand(turn.Deck, cd); }
                }
                yield return Hold(0.5f);
                foreach (var st in steps)
                {
                    if (st.StartsWith("T:"))
                    {
                        var body = st.Substring(2).Split('@');
                        var kind = (TacticKind)System.Enum.Parse(typeof(TacticKind), body[0]);
                        if (!turn.Tactics.Hand.Any(c => c.tactic == kind)) ForceTacticInHand(turn.Tactics, kind);
                        var tc = turn.Tactics.Hand.First(c => c.tactic == kind);
                        var target = body.Length > 1 ? turn.Enemies.FirstOrDefault(e => e.IsAlive && e.Model.Label == body[1]) : null;
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, tc);
                        yield return Hold(look);
                        typeof(BattleUI).GetField("tacticPending", flags).SetValue(ui, null);
                        turn.PlayTactic(tc, target);
                        yield return Hold(0.7f);
                        continue;
                    }
                    var parts = st.Split(':');
                    var dir = parts.Length > 1 ? (parts[1] == "U" ? Direction.Up : parts[1] == "D" ? Direction.Down : parts[1] == "L" ? Direction.Left : Direction.Right) : Direction.Up;
                    var card = turn.Deck.Hand.FirstOrDefault(c => c.Data.name == parts[0] && !turn.IsCardPlanned(c));
                    if (card == null) { VMark("error", "no card " + parts[0]); continue; }
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
                    Sfx.Play(Sfx.Kind.Card);
                    ui.DebugFocus = dir;
                    yield return Hold(look);
                    ui.DebugFocus = null;
                    typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
                    typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
                    turn.AddCommand(new CommandRecord(card.Data, dir, card.Data.minDistance, card));
                    Sfx.Play(Sfx.Kind.Button);
                    yield return Hold(0.35f);
                }
                yield return Hold(0.6f);
                Sfx.Play(Sfx.Kind.Button);
                turn.ExecuteTurn();
                yield return VWaitTurn(turn);
                yield return Hold(after);
            }
            VResult(turn, evStart);
        }

        /// <summary>札を選んで、向きを順に見せる（置く前の「！ 〇ダメ」・まね返しの点線）。最後の向きで置く。</summary>
        IEnumerator VHover(TurnManager turn, string cardName, Direction[] dirs, float each)
        {
            var ui = FindAnyObjectByType<BattleUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var cd = Card(cardName);
            int evStart = turn.BattleEvents.Count;
            if (!turn.Deck.Hand.Any(c => c.Data == cd)) ForceCardInHand(turn.Deck, cd);
            var card = turn.Deck.Hand.First(c => c.Data == cd && !turn.IsCardPlanned(c));
            typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, card.Data);
            typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, card);
            Sfx.Play(Sfx.Kind.Card);
            foreach (var d in dirs) { ui.DebugFocus = d; yield return Hold(each); }
            ui.DebugFocus = null;
            typeof(BattleUI).GetField("pendingCommand", flags).SetValue(ui, null);
            typeof(BattleUI).GetField("pendingCard", flags).SetValue(ui, null);
            turn.AddCommand(new CommandRecord(card.Data, dirs[dirs.Length - 1], card.Data.minDistance, card));
            Sfx.Play(Sfx.Kind.Button);
            yield return Hold(0.8f);
            Sfx.Play(Sfx.Kind.Button);
            turn.ExecuteTurn();
            yield return VWaitTurn(turn);
            yield return Hold(1.6f);
            VResult(turn, evStart);
        }

        /// <summary>【就活資料 V8】撮影の終わり（コマと timeline を閉じる）。</summary>
        IEnumerator VEnd()
        {
            videoRecording = false;
            yield return null;
            Sfx.Recorded = null; Bgm.Recorded = null;
            Time.captureFramerate = 0;
            File.WriteAllLines(Path.Combine(folder, "timeline.txt"), videoTimeline);
            BuildInfo.SetForTest(null);
        }

        /// <summary>【就活資料 V8】カードの一覧（ランのデッキの画面に、実際にあるカードを並べて撮る。名前は「,」区切り）。</summary>
        IEnumerator VDeck(string names)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            act.Profile.hintsOff = true;
            act.OpenV4RunRoleSelect();
            yield return null;
            act.StartRun(ActContent.Roles[0], 7177);
            yield return null;
            if (act.Screen == ActScreen.Boon) act.SkipBoon();
            yield return null;
            act.Run.Deck.Clear(); act.Run.TacticCards.Clear();
            foreach (var n in names.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                var cd = Card(n);
                if (cd == null) { VMark("error", "no card " + n); continue; }
                if (cd.type == CommandType.Tactic) act.Run.TacticCards.Add(cd); else act.Run.Deck.Add(cd);
            }
            typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, true);
            yield return Hold(5f);
            typeof(ActUI).GetField("showDeck", flags).SetValue(actUi, false);
            yield return null;
        }

        IEnumerator CaptureVideo(TurnManager turn)
        {
            var act = FindAnyObjectByType<ActRunManager>();
            var actUi = FindAnyObjectByType<ActUI>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            BuildInfo.SetForTest(true); // 外部テスト版と同じ画面（検証用のボタンを出さない）
            ScreenFx.Disabled = false; // 揺れ・光はそのまま（紹介動画）
            TurnManager.FastForward = false;
            act.Profile.hintsOff = true; // ヒントの帯を出さない（撮影だけ・保存しない）
            act.Profile.tacticIntroSeen = true;
            act.Profile.tutorialOffered = true; act.Profile.howToSeen = true; // タイトルの「はじめて遊ぶ方へ」を出さない（撮影だけ）
            Sfx.Recorded = (k, v) => VMark("sfx", $"{k}\t{v:0.###}");
            Bgm.Recorded = t => VMark("bgm", t ?? "-");
            Time.captureFramerate = 30;
            StartCoroutine(VideoFrames());

            // ───── 1. タイトル ─────
            act.OpenTitle();
            yield return null;
            videoRecording = true;
            VScene("title");
            yield return Hold(4f);

            if (!videoScenesOnly) {
            // ───── 2. まね（つかみ）：誰もいない方へ斬る → 隣の敵がまねて、仲間を斬る ─────
            yield return VLoadRoom(turn, "StageTU3", BuildInfo.Title);
            VScene("copy_hook");
            yield return Hold(1.2f);
            yield return VPlay(turn, "Slash:R", 1.4f, 1.8f);

            // ───── 3. 大技を敵へ：まねで敵を赤紫へ歩かせる ─────
            yield return VLoadRoom(turn, "StageTU6", "大技は、敵にも当たる");
            foreach (var e in turn.Grid.Model.Enemies) // チュートリアルと同じ：剣兵の八方斬りを始めから「今」（赤紫が見えている）
            {
                if (!e.IsAlive || e.Clocks.Count == 0) continue;
                e.Clocks[0].Cd = 1;
                Clocks.Telegraph(turn.Grid.Model, e, e.Clocks[0]);
            }
            yield return null;
            VScene("bigattack");
            yield return Hold(1.2f);
            yield return VPlay(turn, "Walk:R", 1.2f, 2.2f);

            // ───── 3b. 強い一手は、敵にも渡る：まね返し（置く前に「！ 4ダメ」と点線で分かる） ─────
            yield return VLoadRoom(turn, "StageTU3", "強い一手は、敵にも渡る", 0, -1, ".......\n.......\n.EPE...\n.......\n.......\n.......", "12,12", "AA");
            VScene("copyback");
            yield return Hold(1.0f);
            yield return VHover(turn, "Slash", new[] { Direction.Up, Direction.Right, Direction.Left, Direction.Right }, 1.1f);
            }

            // ───── 4. 実戦の見どころ（tools/video/scenes.txt の手順） ─────
            var scenesFile = Path.Combine(folder, "scenes.txt");
            if (File.Exists(scenesFile))
            {
                foreach (var line in File.ReadAllLines(scenesFile))
                {
                    // 書き方：場面名 <TAB> 部屋のアセット <TAB> 見出し <TAB> 開始配置 <TAB> 手順
                    if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                    var c = line.Split('\t');
                    if (c.Length < 5) continue;
                    if (c[1] == "#deck") { VScene(c[0]); yield return VDeck(c[4]); continue; } // 【就活資料 V8】カードの一覧
                    // 【就活資料 V8】6列目から：説明用の盤面（/ で行）・敵の HP・まね方（A/T/M）・切り替え
                    string vmap = c.Length > 5 && c[5] != "-" ? c[5].Replace('/', '\n') : null;
                    yield return VLoadRoom(turn, c[1], c[2] == "-" ? null : c[2], int.TryParse(c[3], out var lay) ? lay : 0, -1, vmap,
                        c.Length > 6 && c[6] != "-" ? c[6] : null, c.Length > 7 && c[7] != "-" ? c[7] : null, c.Length > 8 ? c[8] : null);
                    if (c.Length > 8 && c[8].Split(';').Contains("clock1")) // 【就活資料 V8】紫（大技）を始めから予告（チュートリアル6と同じ）
                    {
                        foreach (var e in turn.Grid.Model.Enemies)
                        {
                            if (!e.IsAlive || e.Clocks.Count == 0) continue;
                            e.Clocks[0].Cd = 1;
                            Clocks.Telegraph(turn.Grid.Model, e, e.Clocks[0]);
                        }
                        yield return null;
                    }
                    VScene(c[0]);
                    yield return Hold(1.6f); // 中ボス・ボスの登場帯
                    if (c[4] == "-") { yield return Hold(1.6f); continue; } // 【就活資料 V8】部屋の始めの盤面だけ
                    yield return VPlay(turn, c[4], 1.0f, 1.4f);
                }
            }

            if (videoScenesOnly) { yield return VEnd(); yield break; } // 【就活資料 V8】場面だけ
            // ───── 5. ランの流れ（報酬・強化・商人・刻印・休息・扉・結果） ─────
            act.Profile.hintsOff = true;
            act.OpenV4RunRoleSelect();
            yield return null;
            VScene("roles");
            yield return Hold(2.2f);
            act.StartRun(ActContent.Roles[0], 7177);
            yield return null;
            if (act.Screen == ActScreen.Boon) { VScene("boon"); yield return Hold(2.4f); var imp = act.Run.BoonOptions.FirstOrDefault(b => b.Kind == BoonKind.Imprint); if (imp != null) act.ChooseBoon(imp); else act.SkipBoon(); }
            yield return Hold(0.4f);
            bool shopDone = false, restDone = false, rewardDone = false, midDone = false;
            for (int f = 0; f < 8 && act.Run != null && !act.Run.Cleared; f++)
            {
                if (act.Screen != ActScreen.Doors) break;
                if (f == 0 || f == 3) { VScene("doors"); yield return Hold(2.2f); }
                var doors = act.Run.Doors;
                var room = doors.FirstOrDefault(d => d.IsMidBoss && !midDone) ?? doors.FirstOrDefault(d => d.Kind == RoomKind.Shop && !shopDone) ?? doors.FirstOrDefault(d => d.Kind == RoomKind.Rest && !restDone) ?? doors[0];
                act.ChooseDoor(room);
                yield return Hold(0.6f);
                if (act.Screen == ActScreen.Shop)
                {
                    shopDone = true;
                    VScene("shop");
                    act.Run.SetShardsForStudy(Mathf.Max(act.Run.Shards, 160));
                    yield return Hold(2.4f);
                    if (act.Run.ShopImprint != null) { act.Run.BuyImprint(); Sfx.Play(Sfx.Kind.Card); yield return Hold(1.8f); }
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, "forge_shop");
                    var up = act.Run.Deck.FirstOrDefault(d => act.Run.UpgradeOf(d.name) == null && V4Content.CanUpgradeAny(d) && d.damage > 0);
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, up);
                    yield return Hold(2.6f);
                    typeof(ActUI).GetField("forgePick", flags).SetValue(actUi, null);
                    typeof(ActUI).GetField("removeMode", flags).SetValue(actUi, null);
                    if (up != null) act.Run.BuyUpgrade(up, UpgradeKind.CopyUp);
                    yield return Hold(0.8f);
                    act.NextFloor(); yield return Hold(0.4f); continue;
                }
                if (act.Screen == ActScreen.Rest)
                {
                    restDone = true;
                    VScene("rest");
                    yield return Hold(2.6f);
                    act.NextFloor(); yield return Hold(0.4f); continue;
                }
                if (act.Screen != ActScreen.Battle) { act.NextFloor(); yield return Hold(0.3f); continue; }
                if (room.Kind == RoomKind.Boss)
                {
                    VScene("boss_intro");
                    yield return Hold(3.2f);
                    act.ForceWinForCapture();
                    yield return Hold(0.8f);
                    act.ProceedAfterBattle();
                    yield return null;
                    VScene("result");
                    yield return Hold(4f);
                    break;
                }
                bool showReward = !rewardDone || (room.IsMidBoss && !midDone);
                if (showReward) { VScene(room.IsMidBoss ? "midboss_intro" : "battle_intro"); yield return Hold(room.IsMidBoss ? 2.6f : 1.4f); }
                act.ForceWinForCapture();
                yield return Hold(showReward ? 0.8f : 0.1f);
                act.ProceedAfterBattle();
                yield return null;
                if (showReward)
                {
                    VScene(room.IsMidBoss ? "midboss_reward" : "reward");
                    yield return Hold(3.2f); // ルーン片の数え上げ → 札が1枚ずつ
                    if (act.Run.RewardImprints.Count > 0) { act.Run.TakeRewardImprint(act.Run.RewardImprints[0]); Sfx.Play(Sfx.Kind.Card); yield return Hold(1.2f); }
                    var rc = act.Run.RewardCards.FirstOrDefault(r => !r.IsTactic) ?? act.Run.RewardCards.FirstOrDefault();
                    if (rc != null) { act.Run.TakeRewardCard(rc); Sfx.Play(Sfx.Kind.Card); }
                    yield return Hold(1.2f);
                    if (room.IsMidBoss) midDone = true; else rewardDone = true;
                }
                else
                {
                    var rc = act.Run.RewardCards.FirstOrDefault(r => !r.IsTactic);
                    if (rc != null) act.Run.TakeRewardCard(rc);
                    if (act.Run.RewardImprints.Count > 0) act.Run.TakeRewardImprint(act.Run.RewardImprints[0]);
                }
                act.NextFloor();
                yield return Hold(0.3f);
            }

            // ───── 6. 終わり：タイトル ─────
            act.OpenTitle();
            yield return null;
            VScene("end");
            yield return Hold(4f);
            videoRecording = false;
            yield return null;
            Sfx.Recorded = null; Bgm.Recorded = null;
            Time.captureFramerate = 0;
            File.WriteAllLines(Path.Combine(folder, "timeline.txt"), videoTimeline);
            BuildInfo.SetForTest(null);
        }
    }
}
