using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 1回の戦闘のルール上の状態（盤面＋ユニット）。GameObject を使わないので、テストや解析で何度でも複製して試せる。
    /// </summary>
    public class BattleModel
    {
        public BoardModel Board { get; }
        public IReadOnlyList<UnitModel> Units => units;
        public UnitModel Player => units.FirstOrDefault(u => u.IsPlayer);
        /// <summary>敵（実行順：敵A, 敵B, …）。</summary>
        public IEnumerable<UnitModel> Enemies => units.Where(u => !u.IsPlayer);
        /// <summary>勝ち＝勝ち負けに数える敵（写し砲などの物を除く）が全員倒れた。</summary>
        public bool AllEnemiesDead => Enemies.Where(e => !e.IsObject).All(e => !e.IsAlive);
        /// <summary>【検証中・Copy Core V2】コピーの基本規則（ACT1・今までのステージは Current）。</summary>
        public CopyMode CopyMode = CopyMode.Current;
        /// <summary>【Copy Core V2】false なら敵はコピーしない（COPY OFF の比較用）。</summary>
        public bool CopyEnabled = true;
        /// <summary>【Puzzle v2】部屋の勝ち方（既定は討伐）。</summary>
        public RoomGoal Goal = RoomGoal.Defeat;
        /// <summary>
        /// 【戦闘中心 v3】Soft ルール（StageData.combatRoom）：仕掛けは「解かないと終わらない」ではなく「使うと圧倒的に楽」。
        /// 反響鎧は直接でも 1（まね攻撃・仕掛けで鎧が割れる）／鏡核の共鳴（敵全員にダメージ・動けない。数ターンで戻る）／癒しの写し身は殴れる。
        /// </summary>
        public bool SoftRules;
        /// <summary>【Combat V4】StageData.combatV4：カウント大技・敵の積極的な動き・戦術札。Soft ルールも含む。</summary>
        public bool V4;
        /// <summary>
        /// 【RC3・本人「固有行動は初戦から出す必要はない」】敵は まねるだけ（自分からは攻撃しない・近づかない・向きを変えない）。
        /// はじめてのランの最初の戦闘（ActRun.LessonBattle）だけ。赤（敵の攻撃）も出ない。
        /// </summary>
        public bool NoTraits;
        /// <summary>【RC4】敵は自分では動かない（位置取りをしない。攻撃・大技はする）。チュートリアルの大技の段階だけ（覚える物を1つに）。</summary>
        public bool NoPositioning;
        /// <summary>【Combat V4】残心：次にあなたが受けるダメージを減らす量（1回ぶん・使うと 0）。</summary>
        public int PlayerGuard;
        /// <summary>【Combat V4】操糸（＋）／重し（－）：このラウンド、敵がコピーする移動の距離に足す量。ラウンドの終わりに 0。</summary>
        public int CopyMoveBonus;
        /// <summary>【検証中】炎床・回復ルーン（無ければ空の環境）。</summary>
        public EnvironmentModel Environment { get; set; } = new EnvironmentModel();

        readonly List<UnitModel> units = new List<UnitModel>();

        public BattleModel(BoardModel board)
        {
            Board = board;
        }

        /// <summary>
        /// 盤面の文字列からユニットまで含めて作る（解析・テスト用）。名前は TurnManager と同じ：
        /// プレイヤー／敵が1体なら「敵」、複数なら読み順に「敵A」「敵B」…。
        /// </summary>
        public static BattleModel FromRows(string[] rows, int spikeDamage, int playerHp, int enemyHp)
        {
            var model = new BattleModel(BoardModel.Parse(rows, spikeDamage));
            model.AddUnit("プレイヤー", "", Team.Player, playerHp, model.Board.PlayerStart);
            var starts = model.Board.EnemyStarts;
            for (int i = 0; i < starts.Count; i++)
            {
                string label = starts.Count == 1 ? "" : ((char)('A' + i)).ToString();
                model.AddUnit("敵" + label, label, Team.Enemy, enemyHp, starts[i]);
            }
            model.ApplyStage(null);
            return model;
        }

        /// <summary>ステージのデータから作る（解析用）。</summary>
        public static BattleModel FromStage(StageData stage) => FromStage(stage, 0);

        /// <summary>【完成版】開始配置の別案（layout）つき。</summary>
        public static BattleModel FromStage(StageData stage, int layout)
        {
            var model = FromRows(stage.GetMapRows(layout), stage.spikeDamage, stage.playerMaxHp, stage.enemyMaxHp);
            model.ApplyStage(stage);
            return model;
        }

        /// <summary>
        /// 【完成版】挑戦の段階：1 以上＝敵の HP +15%（鏡の主を除く・切り上げ）。2・3 の効果（自然回復なし・休息 5・再挑戦なし）はラン側。
        /// ゲーム（TurnManager.LoadActBattle）と解析で同じ。
        /// </summary>
        /// <summary>
        /// 【RC6・本人「HP20 に対して雑魚が強すぎる・ボスはあまり攻撃してこない」】階ごとの敵の強さ。
        /// redDelta：雑魚の通常の攻撃（赤）に足す量／clockDelta：雑魚の大技（八方斬り・三連射…）に足す量（中ボス・ボスの大技は変えない）／lastStand：最後の1体が受けるダメージの追加。
        /// ゲーム（TurnManager.LoadActBattle）と解析（pzsearch）で同じ。
        /// </summary>
        public void ApplyPower(int redDelta, int clockDelta, int lastStand)
        {
            EnemyDamageDelta = redDelta;
            LastStandBonus = lastStand;
            if (clockDelta == 0) return;
            foreach (var e in Enemies)
            {
                if (!e.IsAlive || e.IsObject || PhaseRules.IsBossKind(e.Kind)) continue;
                foreach (var c in e.Clocks) c.Damage = System.Math.Max(1, c.Damage + clockDelta);
            }
        }

        /// <summary>
        /// 【通常リリース版・研究】反響深度1 の中身の案（pzsearch の PZ_DEPTHMODE で比べる）。
        /// 0＝RC12（敵の HP +15%・ボス以外）／1＝まね返し +2（敵のまねた攻撃があなたに当たると +2）／2＝目覚め（まねない兵が技まねに）／3＝まね返し +1 と目覚め。
        /// </summary>
        public static int DepthMode = 1; // 【通常リリース版】1＝まね返し +2（Fan 76→68%・ターンは同じ。HP +15% は 76→61%・ターン +5：Logs/rel/depth）

        /// <summary>【通常リリース版】反響深度：敵のまねた攻撃があなたに当たると +N（まね返し）。</summary>
        public int EchoBack;

        public void ApplyChallenge(int level)
        {
            // 【完成版・改】深度1 から敵の HP +15%（ボス以外）。前の「最初の大技が1ターン早い」は、慣れた人にはむしろ楽だった
            // （大技を敵に当てる機会が早く来る：Skilled の被ダメ 7.0 → 6.5）ので外した。深度2・3 はラン側（ActRun）。
            if (level <= 0) return;
            if (DepthMode == 1 || DepthMode == 3) EchoBack = DepthMode == 1 ? (level >= 3 ? 3 : 2) : 1; // 【通常リリース版】深度1・2 は +2、深度3 は +3
            foreach (var e in Enemies)
            {
                if (!e.IsAlive || e.IsObject) continue;
                if (DepthMode == 0 && e.Kind != EnemyKind.MirrorLord) e.OverrideMaxHp((e.MaxHp * 115 + 99) / 100);
                if ((DepthMode == 2 || DepthMode == 3) && e.CopyKind == CopyKind.None) e.CopyKind = CopyKind.Technique;
            }
        }

        /// <summary>
        /// 【検証中】盤面の記号から敵の兵種を、ステージのデータから敵の最初の向きと環境（炎床・回復ルーン）を設定する。
        /// TurnManager（ゲーム）と FromStage（解析）の両方がここを通るので、同じ状態から始まる。stage が null なら既定値。
        /// </summary>
        public void ApplyStage(StageData stage)
        {
            var enemies = Enemies.ToList();
            var kinds = Board.EnemyKinds;
            string facings = stage != null ? stage.enemyFacings ?? "" : "";
            for (int i = 0; i < enemies.Count; i++)
            {
                if (i < kinds.Count) enemies[i].Kind = kinds[i];
                // 鏡守の騎士は「鏡」：方向を反対にしてコピーする（盾の正面・矢の向きも反対の方向で決まる）
                if (enemies[i].Kind == EnemyKind.Knight || enemies[i].Kind == EnemyKind.KnightV2 || enemies[i].Kind == EnemyKind.MirrorSoldier)
                    enemies[i].CopyTrait = CopyTrait.Mirror;
                if (enemies[i].Kind == EnemyKind.KnightV3)
                {
                    enemies[i].CopyTrait = CopyTrait.MirrorAttacks;
                    // 【検証中】最初から3つのカウントダウンが見える（剣3 爆4 炎6）。段階で始まる案のときは大剣だけ
                    var boss = PhaseRules.BossV3;
                    enemies[i].SlamCd = boss.AllFromStart ? boss.SlamStart : boss.SlamPeriod[0];
                    enemies[i].BlastCd = boss.AllFromStart ? boss.BlastStart : 0;
                    enemies[i].WaveCd = boss.AllFromStart ? boss.WaveStart : 0;
                }
                enemies[i].Facing = i < facings.Length ? ParseFacing(facings[i]) : Direction.Down;
                // 【Copy Core V2】敵ごとの HP（"14,14,30" のように読み順。空や 0 ならステージの値のまま）と封印
                if (stage != null && !string.IsNullOrEmpty(stage.enemyHpList))
                {
                    var parts = stage.enemyHpList.Split(',');
                    if (i < parts.Length && int.TryParse(parts[i].Trim(), out var hp) && hp > 0) enemies[i].OverrideMaxHp(hp);
                }
                if (enemies[i].Kind == EnemyKind.SealGuard) { enemies[i].Barrier = 2; enemies[i].BarrierRule = 0; }
                // 【Copy Core V3】コピーの種類（写し砲は技写し）。ステージの指定があればそれ
                enemies[i].CopyKind = enemies[i].Kind == EnemyKind.Cannon || enemies[i].Kind == EnemyKind.HealClone || enemies[i].Kind == EnemyKind.Mender ? CopyKind.Technique : CopyKind.All; // 【戦闘中心 v3】癒し手も既定は技まね
                if (stage != null && !string.IsNullOrEmpty(stage.enemyCopyTypes))
                {
                    var letters = stage.enemyCopyTypes.Where(ch => !char.IsWhiteSpace(ch) && ch != ',').ToList();
                    if (i < letters.Count && CopyKinds.Parse(letters[i]) is CopyKind ck) enemies[i].CopyKind = ck;
                }
                if (enemies[i].Kind == EnemyKind.Warden) { enemies[i].Barrier = PhaseRules.WardenBarrier(0); enemies[i].BarrierRule = 0; enemies[i].SlamCd = PhaseRules.WardenSlamPeriod; enemies[i].Home = enemies[i].Position; }
                if (enemies[i].Kind == EnemyKind.Lord) { enemies[i].Barrier = 1; enemies[i].BarrierRule = 3; enemies[i].SlamCd = PhaseRules.LordSlamPeriod; enemies[i].Home = enemies[i].Position; }
                if (enemies[i].Kind == EnemyKind.HealClone) { enemies[i].Dormant = Enemies.Any(e => e.Kind == EnemyKind.Lord || e.Kind == EnemyKind.SealLord || e.Kind == EnemyKind.MirrorLord); enemies[i].Home = enemies[i].Position; }
                if (enemies[i].Kind == EnemyKind.SealLord) PuzzleBoss.Setup(this, enemies[i]);
                if (enemies[i].Kind == EnemyKind.MirrorLord)
                {
                    var kindByStage = enemies[i].CopyKind;
                    CombatBoss.Setup(this, enemies[i]); // 【戦闘中心 v3】鏡の主
                    if (stage != null && !string.IsNullOrEmpty(stage.enemyCopyTypes)) enemies[i].CopyKind = kindByStage; // 【RC2】ステージの文字（kinds=）が先（Setup が全まねに戻していた）
                }
                if (stage != null && stage.circleGuard && Board.Seals.Count > 0 && (enemies[i].Kind == EnemyKind.Normal || enemies[i].Kind == EnemyKind.EchoKnight))
                { enemies[i].Barrier = 1; enemies[i].BarrierRule = 2; }
                if (enemies[i].Kind == EnemyKind.BladeStatue) { enemies[i].CopyKind = CopyKind.Technique; enemies[i].Dormant = true; enemies[i].Home = enemies[i].Position; }
                // 【Copy Core V3】鏡核の守り（ステージの指定）：鏡核を全部同じラウンドに叩くまで傷つかない
                if (stage != null && stage.coreGuard && Board.CoreHome.Count > 0 && !enemies[i].IsObject && enemies[i].Kind != EnemyKind.Lord && enemies[i].Kind != EnemyKind.SealLord && enemies[i].Kind != EnemyKind.MirrorLord && enemies[i].Kind != EnemyKind.HealClone)
                { enemies[i].Barrier = 1; enemies[i].BarrierRule = 3; }
            }
            // 【Copy Core V2】コピーの方式：サンドボックスのステージだけ、選んだ方式・COPY OFF を使う
            if (stage != null)
            {
                CopyMode = stage.copySandbox && CopyCore.SandboxMode.HasValue ? CopyCore.SandboxMode.Value : stage.copyMode;
                CopyEnabled = !((stage.copySandbox || stage.copyLab || stage.puzzleRoom || stage.combatRoom) && CopyCore.SandboxCopyOff);
                SoftRules = stage.combatRoom || stage.combatV4; // 【戦闘中心 v3】
                V4 = stage.combatV4;
            }
            Environment = EnvironmentModel.Create(Board, stage);
            // 【Puzzle v2】目標と石像の向き
            if (stage != null)
            {
                Goal = stage.goal;
                Board.StatueSpin = stage.statueSpin;
                string sf = stage.statueFacings ?? "";
                for (int i = 0; i < Board.StatueOrder.Count; i++) Board.SetStatueFacing(Board.StatueOrder[i], i < sf.Length ? ParseFacing(sf[i]) : Direction.Down);
            }
            // 【Puzzle v2】道具のまね方は固定（ステージの文字は当てない）：運び像＝移動まね、写し砲・剣の像＝技まね
            foreach (var u in Enemies)
            {
                if (u.Kind == EnemyKind.Golem) u.CopyKind = CopyKind.Move;
                if (u.Kind == EnemyKind.Cannon || u.Kind == EnemyKind.BladeStatue) u.CopyKind = CopyKind.Technique;
            }
            if (stage != null && stage.playerStartHp > 0 && Player != null) Player.Hp = Mathf.Min(stage.playerStartHp, Player.MaxHp);
            // 【Combat V4】兵種ごとの標準のカウント大技・中ボスのまね方（ステージの指定があればそれが先）
            if (V4)
            {
                foreach (var u in Enemies)
                {
                    u.Clocks = stage != null && stage.noClocks ? new List<Clock>() : Clocks.DefaultFor(u.Kind); // 【RC3】チュートリアル・はじめての1階は大技なし
                    if (stage != null && stage.clockKeep > 0 && u.Clocks.Count > stage.clockKeep) u.Clocks.RemoveRange(stage.clockKeep, u.Clocks.Count - stage.clockKeep); // 【文化祭】大技は先頭の N 個だけ（bigs=）
                    u.AfterCopy = u.Kind == EnemyKind.Chaser; // 【RC2】追跡兵だけ「後追い」：あなたの命令とコピーが終わってから動いて噛みつく
                    if (stage == null || string.IsNullOrEmpty(stage.enemyCopyTypes))
                    {
                        // 本人案：剣兵＝全まね／槍兵・追跡兵・砲兵・癒し手＝技まね／弓兵・盾兵＝移動まね。中ボスは 砲王＝技・獣王＝移動・処刑騎士＝全
                        if (u.Kind == EnemyKind.CannonKing || u.Kind == EnemyKind.Spearman || u.Kind == EnemyKind.Chaser || u.Kind == EnemyKind.Bomber) u.CopyKind = CopyKind.Technique;
                        else if (u.Kind == EnemyKind.BeastKing || u.Kind == EnemyKind.Shield || u.Kind == EnemyKind.Archer) u.CopyKind = CopyKind.Move;
                        else if (u.Kind == EnemyKind.MirrorLord) u.CopyKind = CopyKind.Technique; // 巨大な本体：あまり動かない（技はまねる＝護衛を巻き込む）
                    }
                    if (u.Kind == EnemyKind.CannonKing || u.Kind == EnemyKind.ExecKnight || u.Kind == EnemyKind.BeastKing) u.Home = u.Position;
                }
                PhaseRules.InitialIntent(this); // 【RC3】最初のターンの赤：槍兵・弓兵・盾兵はあなたの方を向いて始まる（大技の最初の予告もこの向きで）
                Clocks.Setup(this);
            }
            // 【RC8・本人「どの攻撃がどの敵か分からない」「動く順番を分かりやすく」】記号は敵全体の並び順（＝動く順番）：A → B → C。
            //   盤面の敵の札・攻撃のマスの札・順番の帯で同じ記号と色（UiStyles.EnemyColor）。1体だけの兵種にも付ける（記号が「誰か」と「何番目か」を表す）。
            //   （RC5 は兵種ごとの A・B だった：攻撃のマスの札では「A」が2体いると区別できない）
            {
                int n = 0;
                foreach (var e in Enemies.Where(e => !e.IsObject)) e.KindLetter = ((char)('A' + n++)).ToString();
            }
            // 【RC11】敵は自分では動かない（まねでだけ動く）・砲兵の最初の照準
            if (stage != null && stage.stillEnemies) NoPositioning = true;
            if (stage != null && stage.clocksReady && V4)
                foreach (var e in Enemies) if (e.IsAlive && e.Clocks.Count > 0 && (string.IsNullOrEmpty(stage.clocksReadyOnly) || stage.clocksReadyOnly.Contains(e.KindLetter))) { e.Clocks[0].Cd = 1; Clocks.Telegraph(this, e, e.Clocks[0]); }
            // 【RC12・初見テスト「撃たれないまま終わる大技（あと3）が気になる」】5分体験のパズル（still）で ready=記号 のときは、ほかの敵の大技を持たせない（1〜2ターンでは撃たない）
            if (stage != null && stage.clocksReady && V4 && stage.stillEnemies && !string.IsNullOrEmpty(stage.clocksReadyOnly))
                foreach (var e in Enemies) if (!stage.clocksReadyOnly.Contains(e.KindLetter)) e.Clocks.Clear();
            if (stage != null && !string.IsNullOrEmpty(stage.bomberAim) && Player != null)
            {
                var xy = stage.bomberAim.Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0], out var ax) && int.TryParse(xy[1], out var ay))
                {
                    var center = new Vector2Int(ax, Board.Height - 1 - ay);
                    foreach (var e in Enemies) if (e.IsAlive && e.Kind == EnemyKind.Bomber) e.BombCells = PhaseRules.BombShape(this, center);
                }
            }
        }

        static Direction ParseFacing(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case 'U': return Direction.Up;
                case 'L': return Direction.Left;
                case 'R': return Direction.Right;
                default: return Direction.Down;
            }
        }

        public UnitModel AddUnit(string name, string label, Team team, int maxHp, Vector2Int position)
        {
            var unit = new UnitModel(units.Count, name, label, team, maxHp, position);
            units.Add(unit);
            return unit;
        }

        /// <summary>そのマスにいる（生きている）ユニット。いなければ null。倒れたユニットは盤面から消える扱い。</summary>
        public UnitModel GetUnitAt(Vector2Int cell)
        {
            foreach (var unit in units)
                if (unit.IsAlive && unit.Position == cell) return unit;
            return null;
        }

        /// <summary>壁・盤面の外・ユニットのどれかがあって入れないマスなら true。</summary>
        public bool IsBlocked(Vector2Int cell) => Board.IsWall(cell) || GetUnitAt(cell) != null;

        /// <summary>同じ ID のユニット（複製したモデルで、元のユニットに対応するものを探す）。</summary>
        public UnitModel UnitById(int id) => units[id];

        /// <summary>【検証中】刻印「棘導」：ラウンドで最初に敵がトゲを踏んだときの追加ダメージ（0 なら無し）。</summary>
        public int SpikeFirstBonus;
        /// <summary>このラウンドで棘導の追加ダメージを使ったか。</summary>
        public bool SpikeBonusUsed;

        /// <summary>【研究・解析専用】兵種の行動（剣・槍・弓・爆発）のダメージに足す量（多命令の実験 TurnRules.ExperimentMultiRule）。ゲームでは 0。</summary>
        public int TraitBonus;
        /// <summary>【RC6】雑魚の通常の攻撃（赤：剣・槍・噛みつき・矢・爆発）に足す量（階ごと：V4Content.PowerFor。マイナスで弱く・最低 1）。</summary>
        public int EnemyDamageDelta;
        /// <summary>【RC12・本人「全部よけられて簡単・ヒリヒリしない」】激化：このターン数を過ぎると（Environment.Round ＞ EscalateAfter）、雑魚の攻撃（赤）+EscalateBonus。0＝無し。
        ///   よけ続けて長引かせるほど危なくなる → 「2ダメ受けてでも早く倒す」か「無傷で長引かせる」かを選ぶ。部屋の目安（ParTurns）を使う。</summary>
        public int EscalateAfter;
        public int EscalateBonus = 1;
        /// <summary>【RC12】今のターンが激化しているか。</summary>
        public bool Escalated => EscalateAfter > 0 && Environment != null && Environment.Round > EscalateAfter;
        /// <summary>【RC6】最後の1体（物を除く・ボス以外）が受けるダメージに足す量（片付けが作業にならないように：孤立）。0＝無し。</summary>
        public int LastStandBonus;
        /// <summary>【RC6】孤立：最後の1体（物を除く・ボスと中ボスの本体以外）が受けるダメージは2倍（ACT1 のランだけ）。</summary>
        public bool LastStandDouble;
        /// <summary>【RC7】刻印：まねの刃（敵のまねが敵に +）・鏡の盾（まね返し -）・時計の針（敵の大技が敵に +）・罠師（トゲ・床が敵に +）。0＝無し。</summary>
        public int CopyEdgeBonus, MirrorGuard, ClockHandBonus, TrapBonus;
        /// <summary>【研究・解析専用】共鳴（4命令以上のラウンドの数。多命令の実験 C）。</summary>
        public int Resonance;

        /// <summary>ラウンドの始め（ラウンドごとの効果を戻す）。</summary>
        /// <summary>【RC11】このラウンド（ターン）にあなたが受けたダメージの合計（1ターンの上限 RulesEngine.PlayerTurnCap に使う）。</summary>
        public int PlayerTakenThisRound;

        public void BeginRound()
        {
            PlayerTakenThisRound = 0; // 【RC11】
            RulesEngine.UpdateIsolation(this); // 【RC6】
            SpikeBonusUsed = false; Board.ResetCoreHits();
            // 【RC5・監査】弓兵の赤（ターンの初めの矢の線）の長さを覚える：線をふさいでいた敵をあなたが倒しても、矢は赤の外へ伸びない（前は予告の外で当たった：レーザー回廊）
            if (V4 && PhaseRules.IntentOrder)
                foreach (var e in Enemies)
                    if (e.IsAlive && e.Kind == EnemyKind.Archer) e.ArrowReach = PhaseRules.ArrowLane(this, e).Count;
        }

        public BattleModel Clone()
        {
            var copy = new BattleModel(Board.Clone());
            foreach (var u in units) copy.units.Add(u.Clone());
            copy.Environment = Environment.Clone();
            copy.SpikeFirstBonus = SpikeFirstBonus;
            copy.SpikeBonusUsed = SpikeBonusUsed;
            copy.TraitBonus = TraitBonus;
            copy.EnemyDamageDelta = EnemyDamageDelta;
            copy.EscalateAfter = EscalateAfter; copy.EscalateBonus = EscalateBonus; // 【RC12】
            copy.LastStandBonus = LastStandBonus;
            copy.LastStandDouble = LastStandDouble;
            copy.PlayerTakenThisRound = PlayerTakenThisRound; // 【RC11】
            copy.EchoBack = EchoBack; // 【通常リリース版】反響深度
            copy.CopyEdgeBonus = CopyEdgeBonus; copy.MirrorGuard = MirrorGuard; copy.ClockHandBonus = ClockHandBonus; copy.TrapBonus = TrapBonus; // 【RC7】
            copy.Resonance = Resonance;
            copy.CopyMode = CopyMode;
            copy.CopyEnabled = CopyEnabled;
            copy.Goal = Goal;
            copy.SoftRules = SoftRules;
            copy.V4 = V4;
            copy.NoTraits = NoTraits;
            copy.NoPositioning = NoPositioning;
            copy.PlayerGuard = PlayerGuard;
            copy.CopyMoveBonus = CopyMoveBonus;
            return copy;
        }
    }
}
