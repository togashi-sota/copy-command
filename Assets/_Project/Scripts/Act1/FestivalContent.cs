namespace CopyCommand
{
    /// <summary>【文化祭クエスト v2】LEVEL 1つ分＝1つのクエスト（1つの戦闘を数ターンかけて攻略する）。</summary>
    public class FestivalLevelDef
    {
        /// <summary>入口の大きい文字（LEVEL 1）と難しさ（初級）。</summary>
        public string Label, Grade;
        /// <summary>クエストの名前（反響の訓練場）。</summary>
        public string Quest;
        /// <summary>このクエストの特殊なルール（1行）。説明はこれだけ：詳しくは「？」・凡例・ヒント。</summary>
        public string Rule;
        /// <summary>目的（敵を全員倒せ・砲王を倒せ）と敵の数・顔ぶれ（入口と開始の窓）。</summary>
        public string Goal, Enemies;
        /// <summary>目安の時間（入口）・おすすめの人。</summary>
        public string Minutes, For;
        /// <summary>HP（倒れたら同じ LEVEL を最初から）。</summary>
        public int Hp;
        /// <summary>ランク：敵を利用した攻撃の目標・受けたダメージの目安（以下）・ターンの目安（以内）。</summary>
        public int EnemyUseGoal, DamageLimit, ParTurns;
        /// <summary>入口のカードの絵（兵種の顔）。</summary>
        public string[] Icons;
        /// <summary>開始の窓の敵の一覧：{顔の絵, 名前, まね方・攻撃}。</summary>
        public string[][] EnemyCards;
        /// <summary>この LEVEL で見せたいこと（資料・スタッフ用）。</summary>
        public string Point;
        /// <summary>長く止まったときの強いヒント（LEVEL 3 だけ：3分たつか 7ターン目から）。null なら無し。</summary>
        public string LateHint;
        /// <summary>スタッフのお手本の一例（スタッフ向けの説明書・資料。画面には出さない）。</summary>
        public string DemoScript;
        /// <summary>戦闘（部屋・目的の文・ヒント3段・クリアの一言・きれいな解）。</summary>
        public TutorialStepDef Battle;
    }

    /// <summary>
    /// 【文化祭クエスト v2・2026-11-13】文化祭のクエスト（部屋は tools/festival_quests.txt → StageQ1〜Q3）。
    /// 本人の実プレイ「3戦で仕組みを順に体験＝チュートリアルの延長。1〜2ターンで答えが出て終わる。1回遊んだ・攻略した満足感が弱い」を受けて作り直した：
    ///   LEVEL を1つ選び、1つの完成したクエスト（数ターンかけて攻略する普通の戦闘）を遊ぶ。
    ///   LEVEL 1＝本作の核（まね）が一番分かる／LEVEL 2＝敵の攻撃・盤面まで利用する／LEVEL 3＝本作でしか起きない複合的な連鎖。
    /// 普通に殴っても勝てるが、コピーと敵の攻撃を使うと かなり楽・速い（本編と同じ考え方）。答え（どの札をどの向き）は出さない。
    /// 設計：docs/festival/FESTIVAL_QUEST_V2.md
    /// </summary>
    public static class FestivalContent
    {
        public static readonly FestivalLevelDef[] Levels =
        {
            // LEVEL 1：本作の核（まね）が一番分かる。剣兵2・兵1・柱2・トゲ2（左右対称）。直接だけ 6.0T ／ 敵を利用 3.5T（上手）
            new FestivalLevelDef
            {
                Label = "LEVEL 1", Grade = "初級", Quest = "反響の訓練場",
                Rule = "あなたの命令を、敵もまねる。\n敵の位置を利用して倒そう。",
                Goal = "敵を全員倒せ", Enemies = "剣兵2・兵1（3体）", Minutes = "3〜4分", For = "はじめての人に",
                Hp = 20, EnemyUseGoal = 10, DamageLimit = 3, ParTurns = 5,
                Icons = new[] { "portrait_swordsman", "portrait_normal", "portrait_swordsman" },
                EnemyCards = new[]
                {
                    new[] { "portrait_swordsman", "剣兵 ×2", "全まね（移動も攻撃も）・隣の4マスを斬る（赤）" },
                    new[] { "portrait_normal", "兵", "全まね・自分からは攻撃しない" },
                },
                Point = "空振りの斬撃でも、まねた敵の斬撃は隣の敵に当たる／動きのまねで敵をトゲへ／柱で片方だけ止まると並びが変わる。",
                DemoScript = "あなたの命令を、敵もまねます。たとえば → に斬ると（だれもいなくても）、兵も → に斬るので、となりの剣兵に当たります。↓ に歩くと、剣兵も ↓ に歩いてトゲを踏みます。",
                Battle = new TutorialStepDef
                {
                    Name = "反響の訓練場", Asset = "StageQ1", Goal = TutorialGoal.Clear, Stars = 1,
                    MissionRule = "あなたの命令を、敵もまねる。敵の位置を利用して倒そう。",
                    Lines = new[]
                    {
                        "剣兵・兵（全まね）：あなたの移動も攻撃も、同じ向きにまねる。",
                        "剣兵は 隣の4マス（赤）を斬る（敵にも当たる）。兵は攻撃しない。",
                        "トゲ：入るたびに 3ダメージ（敵も）。柱は まねた移動も止める。",
                    },
                    Hints = new[]
                    {
                        "だれもいない方へ斬っても、敵は 同じ向きに まねて斬る。敵の となりに 別の敵がいる向きは？",
                        "剣兵の 真下は トゲ。あなたが ↓ に歩くと、剣兵も ↓ に歩く。",
                        "柱で止まる敵と止まらない敵がいると、並び方が変わる。敵どうしが となりに並んだら、その向きへ 空振りの斬撃。",
                    },
                    Success = new[] { "あなたの命令が、敵の攻撃になった！" },
                    Solution = "Slash:R,Walk:D,Slash:R|Walk:L,PushSlash:U,Slash:D|Walk:R,Walk:D,PushSlash:U|Walk:R,Walk:D,Dash:L",
                },
            },
            // LEVEL 2：敵の攻撃・盤面まで利用する。剣兵・槍兵・砲兵・兵・柱2・トゲ1・大技。直接だけ 7.6T ／ 敵を利用 4.6T（上手）。【初見レビュー1回目】盾兵（正面 -3）は外した：新しいルールが多すぎた
            new FestivalLevelDef
            {
                Label = "LEVEL 2", Grade = "中級", Quest = "交差する射線",
                Rule = "まねで動いた敵は、赤い攻撃ごと動く。\n敵を動かして 攻撃先を変えよう。",
                Goal = "敵を全員倒せ", Enemies = "剣兵・槍兵・砲兵・兵（4体）", Minutes = "4〜6分", For = "ゲームが好きな人に",
                Hp = 20, EnemyUseGoal = 10, DamageLimit = 4, ParTurns = 6,
                Icons = new[] { "portrait_swordsman", "portrait_spearman", "portrait_bomber", "portrait_normal" },
                EnemyCards = new[]
                {
                    new[] { "portrait_swordsman", "剣兵", "全まね・隣の4マスを斬る（赤：一緒に動く）" },
                    new[] { "portrait_spearman", "槍兵", "技まね（動かない）・正面2マスを突く（赤）" },
                    new[] { "portrait_bomber", "砲兵", "技まね・あなたの十字を予告（紫：床に固定）" },
                    new[] { "portrait_normal", "兵", "全まね・自分からは攻撃しない" },
                },
                Point = "赤（敵と一緒に動く）は押す・引く・まねの移動でずれる／紫（床に固定：砲兵の十字・大技）は動かない／まね＋敵の攻撃の2段。",
                DemoScript = "赤は 敵と一緒に動く攻撃です。この剣兵を 引き寄せると、剣兵の赤（となりの4マス）も一緒に動くので、となりの敵に当たります。砲兵の十字は 床に固定なので、あなただけ離れれば 敵だけに落ちます。",
                Battle = new TutorialStepDef
                {
                    Name = "交差する射線", Asset = "StageQ2", Goal = TutorialGoal.Clear, Stars = 2,
                    MissionRule = "まねで動いた敵は、赤い攻撃ごと動く。敵を動かして 攻撃先を変えよう。",
                    Lines = new[]
                    {
                        "赤＝敵と一緒に動く（剣兵は隣の4マス・槍兵は正面2マス）。押す・引く・まねの移動で ずらせる。",
                        "紫＝床に固定（砲兵の十字・大技）。予告のあとで動いても、同じマスに落ちる。",
                        "押し出し・引き寄せ・まねの移動で 敵を動かすと、その敵の赤も動く。",
                    },
                    Hints = new[]
                    {
                        "押す・引くで 敵を動かすと、その敵の赤も 一緒に動く。",
                        "剣兵の となり・槍兵の前（赤の中）に 別の敵を入れると、敵が敵を攻撃する。",
                        "砲兵の十字（紫）は 床に固定。敵のそばで予告させてから、あなただけ離れよう。",
                    },
                    Success = new[] { "敵の攻撃を ずらして、敵どうしで 撃ちあわせた！" },
                    Solution = "Walk:R,Dash:R,Slash:L|Slash:D,Pull:L,Dash:U|Walk:U,Slash:L,PushSlash:R|Pull:D,Dash:L,Walk:U",
                },
            },
            // LEVEL 3：本作でしか起きない複合的な連鎖。獣王（中ボス・移動まね）＋剣兵（全まね）・槍兵（技まね）・盾兵（移動まね）・柱2・トゲ2（左右対称）。直接だけ 9.3T ／ 敵を利用 4.6T（上手）
            new FestivalLevelDef
            {
                Label = "LEVEL 3", Grade = "上級", Quest = "反響の王",
                Rule = "敵ごとに まね方が違う。\n数ターン先の連鎖を作ろう。",
                Goal = "獣王を倒せ", Enemies = "獣王（ミニボス）＋剣兵・槍兵・盾兵", Minutes = "5〜8分", For = "戦術・パズルが好きな人に",
                Hp = 16, EnemyUseGoal = 10, DamageLimit = 6, ParTurns = 7,
                Icons = new[] { "portrait_beastking", "portrait_swordsman", "portrait_spearman", "portrait_shield" },
                EnemyCards = new[]
                {
                    new[] { "portrait_beastking", "獣王（ミニボス）", "移動まね・自分でも近づく・飛びかかり（紫の線）" },
                    new[] { "portrait_swordsman", "剣兵", "全まね（移動も攻撃も）・隣の4マス" },
                    new[] { "portrait_spearman", "槍兵", "技まね（動かない）・正面2マス" },
                    new[] { "portrait_shield", "盾兵", "移動まね・正面 -3" },
                },
                Point = "1ターン目に盤面を作る（獣王を移動まねで運ぶ）→ 2ターン目に攻撃の向きを作る → 槍兵の赤＋まねの斬撃＋大技が1ターンに重なる（15ダメージ）。",
                DemoScript = "獣王は 移動まね：あなたと同じに動きます。槍兵は 技まね：動かずに、あなたの斬撃をまねます。獣王を 槍兵の前まで歩かせて、獣王の方へ斬ると、槍兵の赤・まねの斬撃・大技が 全部 獣王に当たります。",
                LateHint = "獣王を 槍兵の すぐ前（槍の赤の中）へ 歩かせよう。そこで 獣王の方へ 斬撃と押し斬り：槍兵の赤＋まね＋大技が 全部 獣王へ。",
                Battle = new TutorialStepDef
                {
                    Name = "反響の王", Asset = "StageQ3", Goal = TutorialGoal.Clear, Stars = 3, Boss = true,
                    MissionRule = "敵ごとに まね方が違う。数ターン先の連鎖を作ろう。",
                    Lines = new[]
                    {
                        "獣王（移動まね）：あなたと同じに動き、自分でも近づく。紫の線＝飛びかかり（敵にも当たる）。",
                        "剣兵＝全まね・槍兵＝技まね（動かない・正面2マス）・盾兵＝移動まね（正面 -3）。",
                        "獣王を倒せば勝ち（手下は逃げる）。トゲは 獣王にも効く。",
                    },
                    Hints = new[]
                    {
                        "獣王は 移動まね：あなたと同じに動く。トゲや 槍兵の前へ 歩かせよう。",
                        "槍兵は 技まね（動かない）。槍兵の となりに 獣王が来たら、その向きへ 空振りの斬撃。",
                        "剣兵は 全まね。剣兵と獣王を となりに並べると、剣兵の赤も まねの斬撃も 獣王へ。",
                    },
                    Success = new[] { "手下の攻撃と 大技の連鎖で、王を倒した！" },
                    Solution = "Dash:D,Walk:R,Push:U|Slash:L,Pull:D,Walk:R|Push:R,PushSlash:L,Slash:D|Dash:R,PushSlash:D,Slash:R|Pull:U,Slash:R,PushSlash:R",
                },
            },
        };

        /// <summary>結果の画面の、本編への一言。</summary>
        public const string MainGameLine = "本編では、カードを集めながら8階のボスを目指すローグライクとして遊べます。";
        /// <summary>本編を始める前の確認（展示の PC）。</summary>
        public const string MainGameMinutes = "本編は 1回 約25〜30分です";
    }
}
