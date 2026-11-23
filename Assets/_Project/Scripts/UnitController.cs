using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// ユニットの見た目（Player / Enemy 共通）。ルール上のデータは UnitModel が持ち、ここは表示だけを担当する。
    /// 位置はモデルの位置へ滑らかに追いかける。ダメージ表示・踏み込み・回転は BattlePresenter から呼ばれる。
    /// </summary>
    public class UnitController : MonoBehaviour
    {
        public static readonly Color PlayerColor = new Color(0.25f, 0.5f, 1f);
        public static readonly Color EnemyColor = new Color(0.9f, 0.25f, 0.25f);
        /// <summary>複数の敵の色（敵A, 敵B, …）。</summary>
        public static readonly Color[] EnemyColors =
        {
            new Color(0.9f, 0.25f, 0.25f),
            new Color(0.85f, 0.3f, 0.85f),
            new Color(0.95f, 0.55f, 0.15f),
        };

        const float MoveSpeed = 7f;      // 見た目の移動速度（マス/秒）。瞬間移動に見えないように少し遅め
        const float FlashTime = 0.25f;   // ダメージを受けたときに白く光る時間
        const float LungeTime = 0.18f;   // 攻撃時に相手の方へ踏み込む演出の時間
        const float SpinTime = 0.3f;     // 上下左右攻撃のときに回る演出の時間

        /// <summary>ルール上のデータ。</summary>
        public UnitModel Model { get; private set; }
        public string DisplayName => Model.Name;
        public bool IsPlayer => Model.IsPlayer;
        public int Hp => Model.Hp;
        public int MaxHp => Model.MaxHp;
        public Vector2Int GridPosition => Model.Position;
        public bool IsAlive => Model.IsAlive;
        public Color BaseColor => baseColor;
        /// <summary>直近に受けたダメージ（画面に「-4」などを出すため）と、その表示の残り時間。</summary>
        public int LastDamage { get; private set; }
        public float DamagePopupTimer { get; private set; }

        GridManager grid;
        SpriteRenderer body;
        Color baseColor;
        float flashTimer;
        float lungeTimer;
        Vector2 lungeDir;
        float spinTimer;

        // 【検証中】テーマ（4方向の絵）を使うときの演出。null ならグレーBOX のまま
        VisualTheme theme;
        VisualTheme.UnitSprites sprites, statueSprites; // 【RC11】眠っている写し身は石の像の絵
        SpriteRenderer flash;
        SpriteRenderer shadowSr;
        SpriteRenderer idRing; // 【RC10】足もとの敵の色の輪
        static Sprite ringSprite;

        /// <summary>【RC10】白い楕円の輪（敵の色で塗る）。</summary>
        static Sprite RingSprite()
        {
            if (ringSprite != null) return ringSprite;
            const int w = 96, h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f - w / 2f) / (w / 2f), ny = (y + 0.5f - h / 2f) / (h / 2f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float a = Mathf.Clamp01((d - 0.68f) / 0.06f) * Mathf.Clamp01((1f - d) / 0.06f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            ringSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 96f);
            return ringSprite;
        }
        Vector3 moveFrom, moveTo;
        float moveT = 1f, moveDur, moveDist;
        Vector2Int shownCell;
        bool shownAlive = true;
        float deathTimer;
        const float DeathTime = 0.55f;
        float bodyScale = 1f;
        Color themedTint = Color.white; // 【RC5】騎士の絵の敵の薄い色

        /// <summary>【検証中】テーマの絵で描いているか（ダメージ数字は盤面の上に出す）。</summary>
        public bool Themed => sprites != null;

        public void Bind(GridManager gridManager, UnitModel model, Color color)
        {
            grid = gridManager;
            Model = model;
            baseColor = color;
            if (body == null)
                body = GreyboxSprites.CreateShape(transform, "Body", GreyboxSprites.Square, baseColor, Vector3.zero, 0.7f, 10);
            body.color = baseColor;
            transform.position = grid.GridToWorld(model.Position);
        }

        /// <summary>【仮ビジュアル】色を変える（兵種ごとの色）。テーマの絵のときは絵の色のまま（色は UI の名札だけに使う）。</summary>
        public void SetBaseColor(Color color)
        {
            baseColor = color;
            if (body != null && !Themed) body.color = color;
        }

        /// <summary>
        /// 【検証中】テーマの4方向の絵に切り替える（兵種が決まった後に呼ぶ）。テーマに絵が無い兵種はグレーBOX のまま。
        /// 足元の影・白く光る重ね絵も作る。
        /// </summary>
        public void ApplyTheme(VisualTheme t)
        {
            theme = t;
            sprites = t != null ? t.UnitFor(Model) : null;
            statueSprites = t != null && Model.Kind == EnemyKind.HealClone ? t.UnitForKey("statue") : null;
            if (sprites == null) return;
            body.sprite = sprites.For(Model.Facing);
            body.color = Color.white;
            // ボスは大きく（当たり判定は1マスのまま。参考画像の「2×2」はルールを変えるので見た目だけ少し大きく）
            // 【RC5・レビュー】同じ「騎士」の絵を盾兵・処刑騎士・獣王・鏡の主が使っていて見分けにくかった → 鏡の主はもっと大きく、盾兵はふつうの大きさ、騎士の絵の敵は兵種の色を薄く重ねる
            bodyScale = Model.Kind == EnemyKind.MirrorLord ? 1.38f : Model.Kind == EnemyKind.BeastKing ? 1.05f : Model.Kind == EnemyKind.ExecKnight || Model.Kind == EnemyKind.CannonKing || sprites.kind == "knight" ? 1.25f : 1f; // 【RC10・初見テスト「獣王の角が隣のマスの赤を隠す」】獣王は角が横に広いので小さめ // 【RC10】兵種ごとの絵になった（盾兵は盾の絵・ふつうの大きさ） // 【RC9・初見テスト「獣王の大きな絵が隣のマスにはみ出し、隣の攻撃の持ち主を読み違える」】1.65/1.4 → 1.5/1.25
            var kc = !Model.IsPlayer && sprites.kind == "knight" ? KindColor(Model.Kind) : null;
            themedTint = kc.HasValue ? Color.Lerp(Color.white, kc.Value, 0.3f) : Color.white;
            body.color = themedTint;
            body.transform.localScale = Vector3.one * bodyScale;
            body.sortingLayerName = SortLayers.Units;
            body.sortingOrder = 2;
            var sh = t.Spawn("shadow", transform, transform.position);
            if (sh != null) shadowSr = sh.GetComponent<SpriteRenderer>();
            // 【RC10・初見テスト「青ずきんに黄色の A の札・赤ずきんに青の B：色が逆に見える」】足もとに敵の色（A・B・C）の輪：記号の色と体を結ぶ（体そのものは兵種の色のまま）
            if (!Model.IsPlayer && !Model.IsObject)
            {
                var rg = new GameObject("IdRing");
                rg.transform.SetParent(transform, false);
                rg.transform.localPosition = new Vector3(0f, -0.14f, 0f); // 下の端の記号・HP の帯に隠れない高さ（足もと）
                rg.transform.localScale = new Vector3(0.95f, 0.95f, 1f);
                idRing = rg.AddComponent<SpriteRenderer>();
                idRing.sprite = RingSprite();
                idRing.sortingLayerName = SortLayers.Units;
                idRing.sortingOrder = 1;
            }
            var fl = new GameObject("Flash");
            fl.transform.SetParent(body.transform, false);
            flash = fl.AddComponent<SpriteRenderer>();
            flash.sprite = body.sprite;
            if (t.flashMaterial != null) flash.sharedMaterial = t.flashMaterial;
            flash.sortingLayerName = SortLayers.Units;
            flash.sortingOrder = 3;
            flash.color = new Color(1f, 1f, 1f, 0f);
            shownCell = Model.Position;
        }

        /// <summary>【仮ビジュアル】兵種ごとの色（同じ兵種は同じ色）。通常兵は null（敵A・B… の色のまま）。</summary>
        public static Color? KindColor(EnemyKind kind)
        {
            switch (kind)
            {
                case EnemyKind.Swordsman: return new Color(0.85f, 0.25f, 0.22f);
                case EnemyKind.Spearman: return new Color(0.95f, 0.55f, 0.15f);
                case EnemyKind.Bomber: return new Color(0.75f, 0.6f, 0.1f);
                case EnemyKind.Archer: return new Color(0.3f, 0.7f, 0.3f);
                case EnemyKind.MirrorSoldier: return new Color(0.6f, 0.35f, 0.9f);
                case EnemyKind.Shield: return new Color(0.3f, 0.5f, 0.9f);
                case EnemyKind.Knight:
                case EnemyKind.KnightV2:
                case EnemyKind.KnightV3: return new Color(0.85f, 0.7f, 0.2f);
                case EnemyKind.Mender: return new Color(0.3f, 0.85f, 0.55f);     // 【戦闘中心 v3】癒し手（緑）
                case EnemyKind.MirrorLord: return new Color(0.75f, 0.55f, 1f);   // 【戦闘中心 v3】鏡の主（紫）
                case EnemyKind.Chaser: return new Color(0.9f, 0.45f, 0.35f);     // 【Combat V4】追跡兵
                case EnemyKind.ExecKnight: return new Color(0.7f, 0.15f, 0.2f);  // 処刑騎士（暗い赤）
                case EnemyKind.CannonKing: return new Color(0.9f, 0.55f, 0.1f);  // 砲王（橙）
                case EnemyKind.BeastKing: return new Color(0.75f, 0.5f, 0.25f);  // 獣王（茶）
                default: return null;
            }
        }

        /// <summary>RUN モード：前の戦闘から持ち越した HP で始める。</summary>
        public void SetHp(int hp) => Model.Hp = Mathf.Clamp(hp, 0, MaxHp);

        /// <summary>ルール上の位置を直接変える（テスト・デバッグ用。通常は RulesEngine が動かす）。</summary>
        public void SetGridPosition(Vector2Int cell)
        {
            if (cell != Model.Position) PlayStep();
            Model.Position = cell;
        }

        /// <summary>ダメージを与える（テスト・デバッグ用。通常は RulesEngine が与える）。</summary>
        public void TakeDamage(int amount)
        {
            Model.TakeDamage(amount);
            ShowDamage(amount);
        }

        /// <summary>ダメージの演出：白く光り、「-4」などの数字を出す。</summary>
        public void ShowDamage(int amount)
        {
            flashTimer = FlashTime;
            LastDamage = amount;
            Sfx.Play(Sfx.Kind.Hit);
            if (Themed)
            {
                // 【検証中】盤面の上のダメージ数字＋ヒットストップ（UI の数字は出さない）
                DamagePopup.Spawn(transform.position, $"-{amount}", IsPlayer ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.92f, 0.55f));
                HitStop.Do(amount >= 4 ? 0.09f : 0.06f);
                return;
            }
            DamagePopupTimer = 1.0f;
        }

        /// <summary>【検証中】回復の演出：「+3」などの数字を出す（LastDamage を負の値にして区別する）。</summary>
        public void ShowHeal(int amount)
        {
            if (amount <= 0) return; // HP 満タンで回復 0 のとき「-0」（ダメージに見える）を出さない
            LastDamage = -amount;
            if (Themed) { DamagePopup.Spawn(transform.position, $"+{amount}", new Color(0.45f, 1f, 0.6f)); return; }
            DamagePopupTimer = 1.0f;
        }

        public void PlayStep() => Sfx.Play(Sfx.Kind.Step);

        /// <summary>攻撃の演出：指定方向へ一瞬踏み込む。</summary>
        public void PlayLunge(Vector2Int dir)
        {
            lungeDir = dir;
            lungeTimer = LungeTime;
            Sfx.Play(Sfx.Kind.Swing);
        }

        /// <summary>上下左右攻撃の演出：くるっと回る。</summary>
        public void PlaySpin()
        {
            spinTimer = SpinTime;
            Sfx.Play(Sfx.Kind.Swing);
        }

        void Update()
        {
            if (grid == null || Model == null) return;
            if (Themed) { UpdateThemed(); return; }
            transform.position = Vector3.MoveTowards(transform.position, grid.GridToWorld(Model.Position), MoveSpeed * Time.deltaTime);

            if (lungeTimer > 0f) lungeTimer -= Time.deltaTime;
            if (spinTimer > 0f) spinTimer -= Time.deltaTime;
            float lunge = lungeTimer > 0f ? Mathf.Sin(Mathf.PI * (1f - lungeTimer / LungeTime)) * 0.3f : 0f;
            body.transform.localPosition = (Vector3)(lungeDir * lunge);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, spinTimer > 0f ? 360f * (1f - spinTimer / SpinTime) : 0f);

            if (flashTimer > 0f) flashTimer -= Time.deltaTime;
            if (DamagePopupTimer > 0f) DamagePopupTimer -= Time.deltaTime;
            Color c = flashTimer > 0f ? Color.white : baseColor;
            if (!IsAlive) c = Color.Lerp(baseColor, Color.black, 0.6f);
            body.color = c;
        }

        /// <summary>
        /// 【検証中】テーマの絵の演出：向きの切り替え・滑る移動＋軽い跳ね・踏み込み・待機中の呼吸・白く光る・倒れる。
        /// ルールの位置が変わったら、その区間をなめらかに動く（遠すぎる＝ワープは一瞬）。
        /// </summary>
        void UpdateThemed()
        {
            float dt = Time.deltaTime;
            if (Model.Position != shownCell)
            {
                moveFrom = transform.position;
                moveTo = grid.GridToWorld(Model.Position);
                moveDist = Vector3.Distance(moveFrom, moveTo);
                moveDur = Mathf.Clamp(moveDist / MoveSpeed, 0.12f, 0.4f);
                ThemedBoard.Vfx(theme, "vfx_dust", moveFrom);
                if (moveDist > 3.5f)
                {
                    moveT = 1f;
                    transform.position = moveTo;
                    ThemedBoard.Vfx(theme, "vfx_hit", moveTo, 0f, 0.8f);
                }
                else moveT = 0f;
                shownCell = Model.Position;
            }
            float hop = 0f;
            if (moveT < 1f)
            {
                moveT = Mathf.Min(1f, moveT + dt / moveDur);
                transform.position = Vector3.Lerp(moveFrom, moveTo, Mathf.SmoothStep(0f, 1f, moveT));
                hop = Mathf.Sin(Mathf.PI * moveT) * 0.12f;
            }
            else transform.position = grid.GridToWorld(Model.Position);

            if (lungeTimer > 0f) lungeTimer -= dt;
            if (spinTimer > 0f) spinTimer -= dt;
            float lunge = lungeTimer > 0f ? Mathf.Sin(Mathf.PI * (1f - lungeTimer / LungeTime)) * 0.3f : 0f;
            body.transform.localPosition = (Vector3)(lungeDir * lunge) + new Vector3(0f, hop + (bodyScale - 1f) * 0.3f, 0f);
            body.transform.localRotation = Quaternion.Euler(0f, 0f, spinTimer > 0f ? 360f * (1f - spinTimer / SpinTime) : 0f);

            bool stone = statueSprites != null && Model.Dormant && Model.Kind == EnemyKind.HealClone && IsAlive;
            body.sprite = (stone ? statueSprites : sprites).For(Model.Facing);
            flash.sprite = body.sprite;
            if (flashTimer > 0f) flashTimer -= dt;
            flash.color = new Color(1f, 1f, 1f, Mathf.Clamp01(flashTimer / FlashTime));

            if (IsAlive)
            {
                // 待機中の呼吸（ユニットごとに少しずらす）
                float breathe = moveT >= 1f ? Mathf.Sin(Time.time * 3f + Model.Id) * 0.025f : 0f;
                body.transform.localScale = new Vector3(1f - breathe * 0.5f, 1f + breathe, 1f) * bodyScale;
                shownAlive = true;
                // 【RC10・ChatGPT「まだ使わない物は背景に」】眠っている写し身は灰色の石の像（呼吸もしない）。目覚めたら写し身の水色に戻る
                bool asleep = Model.Dormant && Model.Kind == EnemyKind.HealClone;
                if (asleep) body.transform.localScale = Vector3.one * bodyScale;
                body.color = asleep ? (statueSprites != null ? Color.white : new Color(0.42f, 0.44f, 0.5f, 1f)) : themedTint; // 【RC11】石の像は不透明（半透明だと「通れる」と読まれた）
                if (shadowSr != null) shadowSr.color = asleep ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
                if (idRing != null) { var ic = BattleUI.EnemyIdColor(Model); idRing.color = asleep ? new Color(ic.r, ic.g, ic.b, 0f) : new Color(ic.r, ic.g, ic.b, 0.9f); }
                return;
            }
            if (shownAlive)
            {
                shownAlive = false;
                deathTimer = 0f;
                ThemedBoard.Vfx(theme, "vfx_death", transform.position);
            }
            deathTimer += Time.unscaledDeltaTime > 0f && Time.timeScale == 0f ? Time.unscaledDeltaTime * 0.25f : dt;
            bool leaderDeath = PhaseRules.IsLeader(Model); // 【RC11】部屋の主は、ふくらんで白く光ってから消える（ゆっくり）
            float k = Mathf.Clamp01(deathTimer / (leaderDeath ? DeathTime * 3f : DeathTime));
            body.transform.localScale = Vector3.one * (leaderDeath ? Mathf.Lerp(1f, 1.45f, k) : Mathf.Lerp(1f, 0.6f, k)) * bodyScale;
            body.color = leaderDeath ? new Color(Mathf.Lerp(themedTint.r, 1f, k * 1.6f), Mathf.Lerp(themedTint.g, 1f, k * 1.6f), Mathf.Lerp(themedTint.b, 1f, k * 1.6f), 1f - k * k)
                                     : new Color(themedTint.r, themedTint.g, themedTint.b, 1f - k);
            if (shadowSr != null) shadowSr.color = new Color(1f, 1f, 1f, 1f - k);
            if (idRing != null) idRing.color = new Color(idRing.color.r, idRing.color.g, idRing.color.b, 0.9f * (1f - k));
        }
    }
}
