using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 演出係。RulesEngine が返した出来事（BattleEvent）を順番に見た目へ反映する。
    /// 今はグレーBOX 用の簡単な演出（マスを光らせる・踏み込み・回転・ダメージ表示・効果音）。
    /// 本番アート・uGUI に移るときは、ここを Tween / Particle / Trail などに差し替える（ルール側は変えない）。
    /// </summary>
    public class BattlePresenter
    {
        static readonly Color AttackFlashColor = new Color(1f, 0.6f, 0.1f, 0.8f);
        static readonly Color InnateFlashColor = new Color(1f, 0.15f, 0.15f, 0.85f);
        static readonly Color UtilityFlashColor = new Color(0.4f, 0.8f, 1f, 0.7f);

        readonly GridManager grid;
        /// <summary>【RC9】床の仕掛けの呼び名（炎・落石・光線）。BattleUI が毎フレーム入れる（「床の危険に命中！」→「落石が敵に命中！」）。</summary>
        public static string FloorHazardName = "炎";
        /// <summary>【RC6】孤立を知らせた敵（同じ敵に2回出さない）。</summary>
        UnitModel isolationAnnounced;

        public BattlePresenter(GridManager grid)
        {
            this.grid = grid;
        }

        /// <summary>敵ごとに、最後に「まね」「接近」の札を出した時刻（1つの動きにつき1回だけ出す）。</summary>
        readonly Dictionary<(int, EnemyMoveKind), float> lastMoveTag = new Dictionary<(int, EnemyMoveKind), float>();
        static readonly Color CopyMoveColor = new Color(0.35f, 0.9f, 1f, 0.9f);
        static readonly Color OwnMoveColor = new Color(1f, 0.62f, 0.15f, 0.95f);

        /// <summary>
        /// 【RC4・本人「まねと敵自身の移動が混ざる」】敵が動いたとき：まねで動いた＝水色の線と「まね」／自分で動いた（接近・位置取り）＝オレンジの線と「接近」「位置取り」。
        /// あなたの押す・引くで動いた敵には付けない。札は同じ敵に 1 秒に1回まで（一歩ずつ出さない）。
        /// </summary>
        void TagEnemyMove(BattleEvent e)
        {
            var kind = TurnForecast.MoveKindOf(e);
            if (kind == EnemyMoveKind.None) return;
            var u = e.Unit;
            bool byCopy = kind == EnemyMoveKind.Copy;
            var from = grid.GridToWorld(e.From);
            var to = grid.GridToWorld(e.To);
            CopyStreak.Spawn(from, to, byCopy ? CopyMoveColor : OwnMoveColor);
            float now = Time.time;
            if (lastMoveTag.TryGetValue((u.Id, kind), out var t) && now - t < 1f) return; // 【RC4・レビュー】まねの直後の「接近」が消えていた → 種類ごと
            lastMoveTag[(u.Id, kind)] = now;
            string label = TurnForecast.MoveTagText(kind, u.Kind);
            DamagePopup.Spawn(to + new Vector3(0f, 0.15f, 0f), label, byCopy ? new Color(0.55f, 0.95f, 1f) : new Color(1f, 0.75f, 0.35f));
        }

        public void Play(IReadOnlyList<BattleEvent> events)
        {
            foreach (var e in events) Play(e);
        }

        void Play(BattleEvent e)
        {
            var view = e.Unit != null ? grid.ViewOf(e.Unit) : null;
            switch (e.Type)
            {
                case BattleEventType.Moved:
                    view?.PlayStep();
                    TagEnemyMove(e); // 【RC4】まね（水色）と敵自身の移動（オレンジ）を見分ける
                    break;
                case BattleEventType.Lunge: view?.PlayLunge(e.Direction); break;
                case BattleEventType.Spin: view?.PlaySpin(); break;
                case BattleEventType.Damaged:
                    view?.ShowDamage(e.Amount);
                    if (e.Text != null && e.Text.StartsWith("鏡の鎧") && e.Unit != null) // 【通常リリース版】ボスの段階2・3：あなたの直接攻撃は鏡の鎧で減る（まね・大技・反射・トゲは通る）
                        Callout.Show($"{e.Text}（まね・大技は通る）", new Color(0.8f, 0.85f, 1f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.45f, 0f));
                    // 【企画書 v5・UI】まねた攻撃：まねた敵から当たった相手へ水色の線（因果が止めた画面でも分かる）
                    if (e.Amount > 0 && e.Unit != null && e.Cause != null && e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit)
                        CopyStreak.Spawn(grid.GridToWorld(e.Cause.Source.Position), grid.GridToWorld(e.Unit.Position), new Color(0.35f, 0.9f, 1f, 1f));
                    // 【Combat V4】敵に当たった大技・まねの攻撃は、成功したことが分かる一言
                    if (e.Unit != null && !e.Unit.IsPlayer && e.Amount > 0 && e.Cause != null)
                    {
                        if (e.Cause.Kind == DamageKind.Clock && e.Cause.Source != null && !e.Cause.Source.IsPlayer)
                        { Callout.Show($"大技が敵に命中！ {e.Cause.Source.KindLetter}→{e.Unit.KindLetter} -{e.Amount}", new Color(0.6f, 1f, 0.6f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.55f, 0f)); Sfx.Play(Sfx.Kind.Chime); HitStop.Do(0.07f); }
                        else if (e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit)
                        {
                            // 【完成版】まねが敵に当たった瞬間（このゲームで一番気持ちいい所）：一言・専用の音・短いヒットストップ・水色の光
                            bool cloneOnLord = e.Unit.Kind == EnemyKind.MirrorLord && e.Cause.Source.Kind == EnemyKind.HealClone; // 【RC9】ボスの段階2の答え合わせ：「写し身が主を斬った」を帯に残す（前は後から来たこの一言に上書きされていた）
                            Callout.Show(cloneOnLord ? $"まね！ 写し身が主を斬った -{e.Amount}" : $"まね！ 同士討ち {e.Cause.Source.KindLetter}→{e.Unit.KindLetter} -{e.Amount}", new Color(0.6f, 0.95f, 1f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.45f, 0f)); // 【RC4】チュートリアルの言葉「まね」とそろえる
                            Sfx.Play(Sfx.Kind.Copy);
                            HitStop.Do(0.08f);
                            ScreenFx.Flash(new Color(0.35f, 0.9f, 1f, 0.18f), 0.18f);
                        }
                        else if (e.Cause.Kind == DamageKind.Trait && !e.Cause.ByCopy && e.Cause.Source != null && !e.Cause.Source.IsPlayer && e.Cause.Source != e.Unit)
                        {
                            // 【RC9・本人「敵の攻撃をずらして別の敵へ当てたら、短く気持ちよく」】敵の赤が敵に当たった：撃った敵と当たった敵を光らせ、赤い線・一言・ヒットストップ
                            Callout.Show($"同士討ち！ {e.Cause.Source.KindLetter}→{e.Unit.KindLetter} -{e.Amount}", new Color(1f, 0.6f, 0.5f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.45f, 0f));
                            CopyStreak.Spawn(grid.GridToWorld(e.Cause.Source.Position), grid.GridToWorld(e.Unit.Position), new Color(1f, 0.4f, 0.3f, 1f));
                            grid.FlashCell(e.Cause.Source.Position, new Color(1f, 0.85f, 0.4f, 0.75f));
                            grid.FlashCell(e.Unit.Position, new Color(1f, 0.35f, 0.3f, 0.8f));
                            Sfx.Play(Sfx.Kind.Copy);
                            HitStop.Do(0.09f);
                            ScreenFx.Flash(new Color(1f, 0.45f, 0.35f, 0.16f), 0.18f);
                        }
                        else if ((e.Cause.Kind == DamageKind.Fire || e.Cause.Kind == DamageKind.Spike) && (e.Cause.Source == null || !e.Cause.Source.IsPlayer))
                        { Callout.Show(e.Cause.Kind == DamageKind.Spike ? "トゲに入った！" : $"{FloorHazardName}が敵に命中！", new Color(1f, 0.85f, 0.45f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.45f, 0f)); Sfx.Play(Sfx.Kind.Chime); }
                    }
                    // 【検証中】あなたが受けたダメージには原因の札（コピー・反射・剣兵の剣・トゲ…）。本人プレイ：何に当たったか分からない
                    if (e.Unit != null && e.Unit.IsPlayer && e.Amount > 0)
                    {
                        string why = UiStyles.DamageCauseLabel(e);
                        if (why != null) DamagePopup.SpawnOnce(grid.GridToWorld(e.Unit.Position) + new Vector3(0f, -0.45f, 0f), why, new Color(1f, 0.75f, 0.6f));
                    }
                    break;
                case BattleEventType.Healed: view?.ShowHeal(e.Amount); break;
                case BattleEventType.LeaderDown:
                    // 【RC11・本人「ボスを倒した時だけ演出を」】部屋の主が倒れた：時間が止まり、白く光って、かけらが飛ぶ・大きな文字
                    if (e.Unit != null)
                    {
                        var at = grid.GridToWorld(e.Cell);
                        for (int k = 0; k < 14; k++)
                        {
                            float ang = k * Mathf.PI * 2f / 14f + 0.2f;
                            float len = 1.1f + (k % 3) * 0.45f;
                            CopyStreak.Spawn(at, at + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * len, k % 2 == 0 ? new Color(1f, 0.95f, 0.7f, 1f) : new Color(0.85f, 0.9f, 1f, 1f));
                        }
                        grid.FlashCell(e.Cell, new Color(1f, 1f, 1f, 0.95f));
                        Sfx.Play(Sfx.Kind.Boom);
                        Sfx.Play(Sfx.Kind.Clear);
                        HitStop.Do(0.45f);
                        ScreenFx.Flash(new Color(1f, 1f, 1f, 0.55f), 0.6f);
                        ScreenFx.Shake(0.35f, 0.7f);
                        BattleUI.ShowLeaderDown(e.Unit, e.Amount);
                    }
                    break;
                case BattleEventType.Died:
                    if (e.Text == "崩れた") { if (e.Unit != null) grid.FlashCell(e.Unit.Position, new Color(0.8f, 0.85f, 1f, 0.6f)); break; } // 【RC11】主が倒れて崩れた敵：音と揺れは主の演出だけ
                    // 【完成版】敵が倒れた：音と小さな揺れ（味方の大技・まねで倒れたことは上の一言で分かる）
                    if (e.Unit != null && !e.Unit.IsPlayer && !e.Unit.IsObject) { Sfx.Play(Sfx.Kind.Kill); ScreenFx.Shake(0.08f, 0.18f); }
                    if (e.Unit != null && !e.Unit.IsPlayer && grid.Model != null && grid.Model.LastStandDouble) // 【RC6】孤立：最後の1体は受けるダメージ2倍（一度だけ知らせる）
                    {
                        var left = grid.Model.Enemies.Where(x => x.IsAlive && !x.IsObject).ToList();
                        if (left.Count == 1 && left[0] != isolationAnnounced && RulesEngine.IsLastStanding(grid.Model, left[0]))
                        {
                            isolationAnnounced = left[0];
                            Callout.Show("最後の1体！受けるダメージ2倍", new Color(1f, 0.8f, 0.35f), grid.GridToWorld(left[0].Position) + new Vector3(0f, 0.75f, 0f));
                        }
                    }
                    break;
                case BattleEventType.SpikePlaced: grid.EnsureSpikeView(e.Cell); break;
                // 【検証中・Copy Core V2】
                case BattleEventType.Deflected:
                    // 【Puzzle v2】あなたの攻撃がはじかれたら、短い理由（長い文は盤面の上の帯）
                    DamagePopup.Spawn(grid.GridToWorld(e.Unit != null ? e.Unit.Position : e.Cell) + new Vector3(0f, 0.3f, 0f), e.Text != null ? "無効！" : "はじいた", e.Text != null ? new Color(1f, 0.55f, 0.5f) : new Color(0.75f, 0.8f, 0.9f));
                    break;
                case BattleEventType.BarrierHit:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), $"封印 -1（残り {e.Amount}）", new Color(0.85f, 0.6f, 1f));
                    break;
                case BattleEventType.BarrierBroken:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.45f, 0f), e.Unit != null && (e.Unit.Kind == EnemyKind.Lord || e.Unit.Kind == EnemyKind.SealLord) ? "守りが割れた！動けない" : "封印が割れた！", new Color(1f, 0.9f, 0.4f));
                    break;
                case BattleEventType.BarrierRestored:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.45f, 0f), e.Unit != null && (e.Unit.Kind == EnemyKind.Lord || e.Unit.Kind == EnemyKind.SealLord) ? "守りが戻った" : "封印が戻った", new Color(0.85f, 0.6f, 1f));
                    break;
                case BattleEventType.GateOpened:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), "扉が開いた", new Color(0.6f, 1f, 0.7f));
                    break;
                case BattleEventType.CoreHit:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), $"鏡核 {e.Amount}/{e.HpAfter}", new Color(0.6f, 0.85f, 1f));
                    grid.FlashCell(e.Cell, new Color(0.5f, 0.8f, 1f, 0.6f));
                    break;
                case BattleEventType.CoresShattered:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), "鏡核が割れた！", new Color(1f, 0.9f, 0.4f));
                    grid.FlashCell(e.Cell, new Color(1f, 0.9f, 0.4f, 0.7f));
                    break;
                case BattleEventType.PhaseChanged:
                    string pt = e.Unit != null && e.Unit.Kind == EnemyKind.MirrorLord
                        ? (e.Amount == 2 ? "段階2：写し身（鏡）が目覚めた" : "段階3：鏡の奔流（核の共鳴で止まる）")
                        : e.Unit != null && e.Unit.Kind == EnemyKind.SealLord
                        ? (e.Amount == 2 ? "段階2：写し身に主を斬らせろ" : "段階3：鏡核を同時に叩け")
                        : (e.Amount == 2 ? "段階2：写し身に斬らせろ" : "段階3：主を円へ");
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.6f, 0f), pt, new Color(1f, 0.95f, 0.5f));
                    if (e.Unit != null && e.Unit.Kind == EnemyKind.MirrorLord) // 【RC10】段階の切り替えは少し派手に（段階2＝水色・段階3＝金）
                    {
                        Sfx.Play(e.Amount == 2 ? Sfx.Kind.Chime : Sfx.Kind.Boom);
                        ScreenFx.Shake(0.22f, 0.45f);
                        ScreenFx.Flash(e.Amount == 2 ? new Color(0.5f, 0.95f, 1f, 0.3f) : new Color(1f, 0.85f, 0.4f, 0.3f), 0.5f);
                        HitStop.Do(0.15f);
                    }
                    break;
                // 【Puzzle v2】
                case BattleEventType.PuzzleSolved:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.8f, 0f), "解けた！", new Color(1f, 1f, 0.6f));
                    grid.FlashCell(e.Cell, new Color(1f, 1f, 0.5f, 0.8f));
                    break;
                case BattleEventType.SealStoneBroken:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), $"封印石が割れた（残り {e.Amount}）", new Color(0.9f, 0.75f, 1f));
                    grid.FlashCell(e.Cell, new Color(0.85f, 0.6f, 1f, 0.7f));
                    break;
                case BattleEventType.StatueTurned:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), "石像が回った", new Color(1f, 0.6f, 1f));
                    break;
                case BattleEventType.ExitOpened:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), "出口が開いた！", new Color(0.6f, 1f, 0.7f));
                    grid.FlashCell(e.Cell, new Color(0.5f, 1f, 0.6f, 0.7f));
                    break;
                case BattleEventType.SealsWeakened:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), "封印が弱まった", new Color(0.9f, 0.75f, 1f));
                    grid.FlashCell(e.Cell, new Color(0.85f, 0.6f, 1f, 0.7f));
                    break;
                case BattleEventType.CarryDone:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.3f, 0f), "運び紋が全部埋まった！", new Color(1f, 0.9f, 0.4f));
                    break;
                case BattleEventType.BarrelBurst:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), "樽が爆発", new Color(1f, 0.6f, 0.3f));
                    grid.FlashCell(e.Cell, new Color(1f, 0.55f, 0.2f, 0.7f));
                    break;
                case BattleEventType.ChestOpened:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), "写し箱が開いた！", new Color(1f, 0.9f, 0.4f));
                    grid.FlashCell(e.Cell, new Color(1f, 0.9f, 0.4f, 0.7f));
                    break;
                case BattleEventType.CloneShattered:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.45f, 0f), "写し身が砕けた！", new Color(1f, 0.9f, 0.4f));
                    grid.FlashCell(e.Cell, new Color(1f, 0.9f, 0.4f, 0.7f));
                    break;
                case BattleEventType.Awakened:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.45f, 0f), "目覚めた", new Color(0.6f, 1f, 0.7f));
                    break;
                case BattleEventType.CrystalBurst:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell), "鏡晶が砕けた", new Color(0.9f, 0.6f, 1f));
                    grid.FlashCell(e.Cell, new Color(0.8f, 0.4f, 1f, 0.6f));
                    break;
                // 【戦闘中心 v3】
                case BattleEventType.ArmorBroken:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.45f, 0f), "鎧が割れた！", new Color(1f, 0.9f, 0.4f));
                    grid.FlashCell(e.Cell, new Color(1f, 0.9f, 0.4f, 0.6f));
                    break;
                case BattleEventType.Resonance:
                    DamagePopup.Spawn(grid.GridToWorld(e.Cell) + new Vector3(0f, 0.6f, 0f), "共鳴！全員動けない", new Color(0.6f, 0.95f, 1f));
                    grid.FlashCell(e.Cell, new Color(0.5f, 0.9f, 1f, 0.8f));
                    break;
                case BattleEventType.BossReflect:
                    // 【RC10・ボスの見せ場】主の攻撃が写し身ではね返った：写し身 → 主へ白い線・両方が光る・一言・ヒットストップ・画面が白く
                    CopyStreak.Spawn(grid.GridToWorld(e.From), grid.GridToWorld(e.To), new Color(0.85f, 1f, 1f, 1f));
                    grid.FlashCell(e.From, new Color(0.6f, 1f, 1f, 0.9f));
                    grid.FlashCell(e.To, new Color(1f, 1f, 0.7f, 0.9f));
                    Callout.Show($"反射！ 主の攻撃がはね返った -{e.Amount}", new Color(0.7f, 1f, 1f), grid.GridToWorld(e.To) + new Vector3(0f, 0.8f, 0f));
                    Sfx.Play(Sfx.Kind.Chime);
                    HitStop.Do(0.12f);
                    ScreenFx.Flash(new Color(0.8f, 1f, 1f, 0.28f), 0.25f);
                    ScreenFx.Shake(0.15f, 0.25f);
                    break;
                case BattleEventType.BossCloneStrike:
                    Callout.Show($"写し身が主を斬った！ -{e.Amount}", new Color(1f, 1f, 0.6f), grid.GridToWorld(e.Cell) + new Vector3(0f, 0.8f, 0f)); // 【RC9・見どころの1枚】主は一番上の段にいるので、上に出す文字が「まね！ 同士討ち」の帯と重なっていた → 同じ帯で置き換える（より詳しい一言）
                    grid.FlashCell(e.Cell, new Color(1f, 1f, 0.5f, 0.8f));
                    break;
                // 【Combat V4】カウント大技・戦術札
                case BattleEventType.ClockWarn:
                    if (e.Unit != null)
                    {
                        Callout.Show($"{e.Text} ①", new Color(1f, 0.5f, 0.95f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.7f, 0f));
                        Sfx.Play(Sfx.Kind.Warn);
                    }
                    break;
                case BattleEventType.ClockFired:
                    if (e.Unit != null)
                    {
                        Callout.Show($"{e.Text}！", new Color(1f, 0.85f, 0.35f), grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.8f, 0f));
                        Sfx.Play(Sfx.Kind.Boom);
                        ScreenFx.Shake(e.Amount >= 6 ? 0.22f : 0.12f, 0.3f);
                        ScreenFx.Flash(new Color(1f, 0.55f, 0.2f, 0.35f), 0.25f);
                    }
                    break;
                case BattleEventType.TacticPlayed:
                    if (grid.Model != null && grid.Model.Player != null)
                        DamagePopup.Spawn(grid.GridToWorld(grid.Model.Player.Position) + new Vector3(0f, 0.9f, 0f), e.Text, new Color(0.5f, 1f, 0.65f));
                    Sfx.Play(Sfx.Kind.Tactic);
                    break;
                case BattleEventType.TargetCell: grid.FlashCell(e.Cell, ColorOf(e.Highlight)); break;
                // Bumped・Died は今は特別な演出なし（倒れたユニットは UnitController が暗くする）
            }
            if (grid.Theme != null) PlayThemed(e);
        }

        /// <summary>
        /// 【検証中】テーマのエフェクト：攻撃のマスに斬撃、当たったら火花（ヒットストップ・数字は UnitController）、
        /// 砲兵・騎士の爆発、兵種の剣・槍のマスに斬撃。マスの光（CellFlash）はそのまま残す（どこに出たか分かるように）。
        /// </summary>
        void PlayThemed(BattleEvent e)
        {
            var theme = grid.Theme;
            switch (e.Type)
            {
                case BattleEventType.Lunge:
                    // 鏡守の騎士V3 の攻撃は「反射」（あなたの攻撃を反対向きに返す）。何が起きたか分かるように一言
                    if (e.Unit != null && e.Unit.CopyTrait == CopyTrait.MirrorAttacks)
                        DamagePopup.Spawn(grid.GridToWorld(e.Unit.Position) + new Vector3(0f, 0.35f, 0f), "反射", new Color(0.8f, 0.65f, 1f));
                    break;
                case BattleEventType.Damaged:
                    if (e.Unit != null) ThemedBoard.Vfx(theme, "vfx_hit", grid.GridToWorld(e.Unit.Position));
                    break;
                case BattleEventType.TargetCell:
                    if (e.Unit == null) break; // 炎床（環境）は床の炎の絵で足りる
                    if (e.Highlight == CellHighlight.Fire)
                        ThemedBoard.Vfx(theme, "vfx_explosion", grid.GridToWorld(e.Cell), Random.Range(0f, 360f), 1.15f);
                    else if (e.Highlight == CellHighlight.Attack || e.Highlight == CellHighlight.Innate || e.Highlight == CellHighlight.Arrow)
                    {
                        var d = e.Cell - e.Unit.Position;
                        var dir = new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y));
                        if (dir == Vector2Int.zero) dir = Vector2Int.right;
                        ThemedBoard.Vfx(theme, "vfx_slash", grid.GridToWorld(e.Cell) - (Vector3)(Vector2)dir * 0.25f, ThemedBoard.Angle(dir), 1.1f);
                    }
                    break;
            }
        }

        static Color ColorOf(CellHighlight h)
        {
            switch (h)
            {
                case CellHighlight.Innate: return InnateFlashColor;
                case CellHighlight.Utility: return UtilityFlashColor;
                case CellHighlight.Arrow: return new Color(1f, 0.15f, 0.15f, 0.8f);
                case CellHighlight.Fire: return new Color(1f, 0.55f, 0.1f, 0.85f);
                case CellHighlight.Heal: return new Color(0.3f, 1f, 0.5f, 0.8f);
                default: return AttackFlashColor;
            }
        }
    }
}
