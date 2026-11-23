namespace CopyCommand
{
    /// <summary>【RC4】チュートリアルの段階の目標（何ができたら「できた！」か）。</summary>
    public enum TutorialGoal { Clear, CopyHit, AvoidRed, OwnMove, Tactic, BigAttack, RunLoop, FixedKill, FriendlyRed, HazardHit, Targets } // 【RC9】固定と倒して消す・敵の攻撃を敵へ・仕掛けへ送る【RC11】Targets＝兵を全部倒すパズル（ターン数・無傷）

    /// <summary>【RC3・2026-10-21 本人の初プレイ】チュートリアルの1つ（部屋は tools/combat_v4_tutorial.txt の StageTU1〜TU7）。</summary>
    public class TutorialStepDef
    {
        public string Name;
        /// <summary>【RC4】部屋（StageTU*）。段階の番号と部屋の番号は同じではない（RC4 で「敵の動き」を5番に入れた）。1ランの流れは null（戦闘しない）。</summary>
        public string Asset;
        public TutorialGoal Goal;
        /// <summary>やること（2〜3行）。読んで覚えるのではなく、1回やってみるための指示。</summary>
        public string[] Lines;
        /// <summary>できたときの一言（何が起きたか・次に何を覚えるか）。</summary>
        public string[] Success;
        /// <summary>戦術札（緑）を持たせる（戦術札の段階だけ）。</summary>
        public string[] Tactics = new string[0];
        /// <summary>【RC11・本人「5分体験は何をやるのか左下に少し出るだけで分からない」】始める前に画面の真ん中に出す MISSION（目的の1行）。</summary>
        public string Mission;
        /// <summary>【RC11】MISSION の下のルールの一言（答えは書かない）。</summary>
        public string MissionRule;
        /// <summary>【RC11】難しさ ★1〜5。</summary>
        public int Stars;
        /// <summary>【RC11】このターン数以内（0＝制限なし）。</summary>
        public int TurnLimit;
        /// <summary>【RC11】無傷で（ダメージを受けたらやり直し）。</summary>
        public bool NoDamage;
        /// <summary>【RC11】2回失敗したら出すヒント（答えそのものではなく、見る所）。</summary>
        public string Hint;
        /// <summary>【RC11・最後の見直し「5問目は崖・段階的なヒントを」】ヒントをもう一度押したときの2つ目（1つ目より具体的）。</summary>
        public string Hint2;
        /// <summary>【RC12・本人「4問目で詰まってやめた・ヒントが足りない」】段階ヒント：1＝どの仕組みを見るか／2＝どの敵を使うか／3＝最初の1手だけ。そのあとに「答えを見る」。</summary>
        public string[] Hints;
        /// <summary>【RC12】答え（札の名前:向き。ターンは | で区切る。例 "Push:R,Walk:D,Pull:R"）。答えを見ても問題は続き、光る札を自分で選んで解く。</summary>
        public string Answer;
        /// <summary>【RC12】きれいな解（このターン数以内・無傷なら「★ きれいに解けた」）。0＝評価しない。合格の条件は TurnLimit・NoDamage。</summary>
        public int BestTurns;
        /// <summary>【RC12・初見テスト「答えを見ても、なぜ解けるのか分からない（次に活かせない）」】解けた窓に出す「なぜ」の1行（3〜5問目）。</summary>
        public string Why;
        /// <summary>【文化祭】結果の予告（向きにマウスを乗せると、まねた後が見える）を出す（はじめてだけ）。</summary>
        public bool Preview;
        /// <summary>【文化祭】撮影・テスト用のきれいな解（画面には出さない：答えは見せない）。書き方は Answer と同じ。</summary>
        public string Solution;
        /// <summary>【文化祭】ミニボスの戦い（MISSION の窓の見出し・演出）。</summary>
        public bool Boss;
    }

    /// <summary>
    /// 【RC3】専用のチュートリアル：移動 → 攻撃 → まね → 赤い予告 → 戦術札 → 大技。見る → 1回やる → 結果を見る。
    /// 【RC4・2026-10-23 本人の RC3 プレイ「チュートリアルがあるように感じない」「ゲーム全体の進み方が分からない」】8つ（5〜8分）：
    /// 赤い予告のあとに「敵の自分の動き（オレンジの矢印）」、最後に戦闘をしない「1ランの流れ（報酬 → 扉 → 8階のボス）」。
    /// </summary>
    public static class TutorialContent
    {
        /// <summary>【RC9】9つ（「固定と、倒して消す」を5番に）。</summary>
        public static int Count => Steps.Length;

        public static readonly TutorialStepDef[] Steps =
        {
            new TutorialStepDef
            {
                Name = "移動", Asset = "StageTU1", Goal = TutorialGoal.Clear,
                Lines = new[]
                {
                    "① 右の手札の「一歩」をクリック → ② 盤面で向き（↑）のマスをクリック（W A S D・矢印キーでもよい）。",
                    "③ 右下の「実行」（Enter キー）。光る「出口」まで歩こう。",
                    "命令は1ターンに3つまで。「一歩」を2枚つなげてもよい。",
                },
                Success = new[] { "命令は左から順に実行される。", "「駆け足」は2マス、「突進」は3マス進む。" },
            },
            new TutorialStepDef
            {
                Name = "攻撃", Asset = "StageTU2", Goal = TutorialGoal.Clear,
                Lines = new[]
                {
                    "「一歩」で敵の隣へ行き、「斬撃」を敵の方へ。",
                    "斬撃＝向いた方の、隣のマスに 4 ダメージ。",
                    "（この敵は練習用：まねもしないし、攻撃もしない）",
                },
                Success = new[] { "敵を全員倒すと勝ち。", "琥珀色の札（押し出し・引き寄せ）は敵を押す・引く。札の数字はダメージかマス数。", "次は、このゲームでいちばん大事なルール「まね」。" }, // 【RC5・レビュー】紫の札と左上の数字
            },
            new TutorialStepDef
            {
                Name = "まね", Asset = "StageTU3", Goal = TutorialGoal.CopyHit,
                Lines = new[]
                {
                    "敵（全まね）は、あなたの命令を「自分の位置から」まねる。",
                    "誰もいない方へ「斬撃」を横向き（← か →）に出して、実行してみよう。",
                    "札を選ぶと、まねる敵に水色の枠が出る。",
                },
                Success = new[] { "まね！ あなたの空振りの斬撃を敵がまねて、隣の敵を斬った。", "同じ命令でも、立っている場所で結果が変わる。これがこのゲーム。", "本編では、まね方が敵ごとに違う（上の敵の欄：全まね・技まね＝攻撃と押し引きだけ・移動まね＝移動だけ）。" }, // 【RC5・レビュー】まね方の種類
            },
            new TutorialStepDef
            {
                Name = "赤い予告とずらし", Asset = "StageTU4", Goal = TutorialGoal.AvoidRed,
                Lines = new[]
                {
                    "赤いマス＝このターンの敵の攻撃。左上の「A」＝撃つ敵（剣兵A）。あなたは赤の上。",
                    "赤（点線）は敵と一緒に動く攻撃。「押し出し」を剣兵の方（↑）へ：攻撃もずれる（移動の札は無い）。",
                    "↑ にマウスを乗せると、ずれた後が盤面に出る。そのまま「実行」。",
                },
                Success = new[] { "逃げなくても、敵を動かせば攻撃は外れる。", "次は、敵を動かしても動かない赤（固定の攻撃）。" }, // 【RC8】固定と追従【RC9】固定は次の段階で体験する【RC10】同じルール（点線・斜線）を3回以上文で言わない
            },
            new TutorialStepDef
            {
                // 【RC9・ChatGPT「チュートリアルに A ずらす・B 倒して消す・C 固定は動かない を必ず」】C と B を1つの部屋で：引き寄せても固定の赤は動かない → 撃つ敵を倒すと消える
                Name = "固定と、倒して消す", Asset = "StageTU8", Goal = TutorialGoal.FixedKill,
                Lines = new[]
                {
                    "砲兵Aの攻撃は紫（斜線）＝床に固定の攻撃。敵を押しても引いても、紫は動かない。",
                    "「引き寄せ」を ↑ へ：A が隣へ来る。↑ にマウスを乗せると「3ダメ」のまま＝紫は動かない（ここまで正しい）。", // 【RC10・初見テスト3回目「指示どおりなのに 3ダメと出て失敗に見える」】
                    "続けて「斬撃」を ↑ へ。撃つ敵が倒れれば、紫も消える（命令は 引き寄せ → 斬撃 の2つ）。",
                },
                Success = new[] { "撃つ敵を倒すと、その敵の攻撃は消える（赤でも紫でも）。", "赤＝敵と動く（押すとずれる）／紫＝床に固定（倒すか、自分がよける）。", "本編では「ずれた後」「消える」「受けるダメージ」の予告は出ない。攻撃元・点線か斜線か・順番を見て、自分で読もう。" },
            },
            new TutorialStepDef
            {
                Name = "敵の動き", Asset = "StageTU7", Goal = TutorialGoal.OwnMove,
                Lines = new[]
                {
                    "オレンジの矢印と影＝ターンの最後に、敵が自分で動く先。追跡兵は毎ターン、あなたへ2マス近づく。",
                    "（水色＝まねで動く・オレンジ＝自分で動く。この追跡兵は練習用で、まねしない）",
                    "隣に来たら「斬撃」2回で先に倒そう（あなたの命令は敵の攻撃より先。倒せば赤の上でも噛まれない）。",
                },
                Success = new[] { "動き方は敵ごとに違う：剣兵はまねが中心・槍兵は構えて待つ・追跡兵は速い。", "敵をクリックすると「このターン：攻撃 → まね → 接近」が見える。" },
            },
            new TutorialStepDef
            {
                Name = "戦術札", Asset = "StageTU5", Goal = TutorialGoal.Tactic,
                Lines = new[]
                {
                    "緑の札＝戦術札：命令（青・赤・琥珀の札）の手助け。命令の枠を使わない・敵はまねしない。",
                    "「集中」（次の直接攻撃 +2）をクリック（Z キー）してから、「斬撃」で斬ろう。",
                    "斬撃 4 ＋ 2 ＝ 6。1回で倒せる。",
                },
                Success = new[] { "戦術札は命令の手助け。いつでも使える。", "戦術札は手札に1枚。使ったら、次のターンにまた1枚引く。" },
                Tactics = new[] { "Tactic_Focus" },
            },
            new TutorialStepDef
            {
                Name = "大技", Asset = "StageTU6", Goal = TutorialGoal.BigAttack,
                Lines = new[]
                {
                    "紫★のマス＝このターンのまねの後に来る大技（床に固定）。敵にも当たる。",
                    "あなたが動くと、全まねの兵も同じ向きに動く（剣兵は練習用で、まねしない）。",
                    "左上の兵を紫のマスへ歩かせよう（あなたが → に「一歩」）。",
                },
                Success = new[] { "敵の大技を、まねで別の敵に当てた！", "大技（紫★）は まねの後に来るので、同じターンに まねで送り込める（ふつうの攻撃は まねの前に終わる）。", "★の数字＝大技まであと何ターン（敵のマスの右下・上の欄）。「今」になると紫のマスが決まる。" }, // 【RC5・レビュー3】
            },
            new TutorialStepDef
            {
                Name = "1ランの流れ", Asset = null, Goal = TutorialGoal.RunLoop,
                Lines = new[] { "戦闘に勝つ → 報酬の札を選ぶ → 扉を選ぶ → … → 8階のボス。" },
                Success = new string[0],
            },
        };

        // ───── 【RC9】5分体験（採用担当など、30分のランを遊べない人に核だけ）：既存のチュートリアルの部屋＋2部屋。はじめの2つは予告あり、3つ目からは本編と同じ（予告なし） ─────

        /// <summary>5分体験で結果の予告を出す段階の数（はじめの2つ）。</summary>
        public const int QuickPreviewSteps = 1; // 【通常リリース版】5分体験は3問（答えが見えるのは1問目だけ）

        public static readonly TutorialStepDef[] QuickSteps =
        {
            // 【RC11・本人「5分体験が簡単すぎる・謎を解いた感がない・3〜5問目は本編で使える物を難しく」】
            //   1〜2問目は答えが見える練習（★〜★★）。3〜5問目は目的だけを出して、自分で解くパズル（★★★〜★★★★★・ターン数と無傷の条件・解は pzsearch search --roomdeck で数えた：tools/rc11_design）
            new TutorialStepDef
            {
                Name = "まね", Asset = "StageTU3", Goal = TutorialGoal.CopyHit, Stars = 1,
                Mission = "空振りの斬撃を、敵にまねさせろ",
                MissionRule = "敵は、あなたの命令を「自分の位置から」同じ向きにまねる。だれもいない方へ斬ると…？", // 【RC12】名前（全まね）は3問目で
                Lines = new[]
                {
                    "右の手札の「斬撃」を選んで、誰もいない横（← か →）へ向けてみよう。",
                    "向きにマウスを乗せると、敵がどうまねるか見える（ここは練習）。決めたら「実行」（Enter）。",
                    "札の水色の欄の右端の 黄色の印＝敵のまねが あなたに当たることがある（向き次第）。", // 【RC12・初見テスト「⚠ の意味がどこにも無い」】
                },
                Success = new[] { "まね！ あなたの空振りを敵がまねて、隣の敵を斬った。", "次は：まねで敵を歩かせて、落石の下へ。" },
            },
            new TutorialStepDef
            {
                Name = "落石へ送る", Asset = "StageQM8", Goal = TutorialGoal.Targets, Stars = 2, TurnLimit = 2, NoDamage = false, BestTurns = 1, // 【RC12・本人「4問目で詰まってやめた」】2ターンまで・ダメージあり（1ターン目の並びの 73% がまだ勝てる：pzsearch forgive）。1ターン・無傷はきれいな解
                Mission = "2ターン以内に、兵A・兵B を落石で倒せ",
                MissionRule = "影のマス＝ターンの最後に岩が落ちる（だれにでも当たる。札の「今」＝このターン・「次」＝次のターン）。兵（全まね）は、あなたと同じ向き・同じマス数だけ歩く。1ターンに1体ずつでもいい。", // 【RC12・初見テスト「岩の今／次の意味が分からない」】
                Lines = new[]
                {
                    "順番：あなた3つ → 兵A 3つ → 兵B 3つ。柱・盤の端・兵・あなたに ぶつかると止まる。",
                    "2ターンある：1ターンに1体ずつでもいい（1ターンで2体なら きれいな解）。", // 【RC12・初見テスト「2体を同じ命令で同時に入れるのは無理に見える」】
                    "岩は最後に落ちる（札の「今」＝このターン）：最後に立っているマスだけ当たる。", // 【RC12・初見テスト3回目「今4 次4 の意味」】
                },
                Hint = "あなたが兵A にぶつかって止まると、兵A は端で止まり、兵B だけが進む",
                Hints = new[]
                {
                    // 【RC12・初見テスト「ヒント1がルールの言い直し」】数えれば動ける形に
                    "兵は あなたが歩いた形（向きとマス数）を そのままなぞる。まず1体だけでいい：兵から影のマスまで、何マス・どの向きかを数えよう。",
                    "兵A は「↓1 → ←1」、兵B は「↑1 → ←1」で 影に入る。1ターンで両方なら、↑ の分だけ 兵A を盤の端で止める。",
                    "最初の1手：「駆け足 ↑」。あなたは兵A にぶつかって止まり、兵A は盤の端で止まる（兵B だけ ↑ へ進む）。",
                },
                Answer = "Dash:U,Walk:D,Walk:L",
                Why = "なぜ：駆け足↑で、あなたは兵A に、兵A は盤の端にぶつかって止まる。兵B だけが ↑ へ進むので、同じ命令でも2体の位置がずれる。",
                Success = new[] { "落石が命中！ あなたは一度も攻撃していない。", "次は：まねの連鎖。" },
            },
            new TutorialStepDef
            {
                Name = "まねの連鎖", Asset = "StageQM10", Goal = TutorialGoal.Targets, Stars = 3, TurnLimit = 2, NoDamage = false, BestTurns = 1, // 【RC12】集大成：まねの連鎖（2ターンまで・ダメージあり。1ターンなら あなたの攻撃は全部空振り＝きれいな解）
                Mission = "2ターン以内に、兵A・兵B を倒せ",
                MissionRule = "兵B と剣兵C は「全まね」：あなたの命令を、それぞれの位置から B → C の順にまねる（兵A は まねない）。まねた攻撃は あなたにも当たる（札の右端の黄色の印）。",
                Lines = new[]
                {
                    "順番：あなた3つ → 兵B が3つまねる → 剣兵C が3つまねる（兵A はまねない）。", // 【RC12・初見テスト4回目「B が3つ終えてから C、が4問目ほどはっきりしない」】
                    "剣兵C は、兵B が動いた後の位置を見てまねる。剣兵C は倒さなくてよい。",
                    "赤（敵の攻撃）は まねより先：まねで赤のマスに入っても、そのターンは当たらない。", // 【RC12・初見テスト4回目「B が赤に入るのに当たらない」】
                },
                Hint = "あなたが当てなくてもいい。だれもいない方への斬撃を、兵B と剣兵C がまねると…",
                Hints = new[]
                {
                    // 【RC12・初見テスト「ヒント1がルールの言い直し」「ヒント2が違う：兵B は HP8 なので斬撃2回」】
                    "あなたの斬撃は当てなくていい。だれもいない方へ斬っても、兵B と剣兵C が それぞれの位置から同じ向きに斬る。",
                    "兵A（HP3）は 兵B のまねた斬撃1回で倒れる。兵B（HP8）は 剣兵C のまねた斬撃2回（4＋4）で倒れる：兵B を剣兵C の真上へ。",
                    "最初の1手：だれもいない ↑ へ「斬撃」。兵B も ↑ を斬って、真上の兵A に当たる。",
                },
                Answer = "Slash:U,Slash:U,Walk:L",
                Why = "なぜ：兵B は斬ってから ← へ歩いて剣兵C の真上へ。剣兵C は そのあとで まねるので、↑ の斬撃2回が兵B に当たる。",
                Success = new[] { "まねの連鎖！ あなたの攻撃は全部空振り。兵B のまねが兵A を、剣兵C のまねが兵B を斬った。", "本編では、これを敵の攻撃・落石・大技と組み合わせて、8階のボス「鏡の主」まで進む。" },
            },
        };

        /// <summary>5分体験の終わりの画面「あなたがやったこと」（左の絵は ui_ic_* と落石・大技の絵）。</summary>
        public static readonly string[] QuickSummary =
        {
            "まね：あなたの命令を、敵がそれぞれの位置からまねる",
            "落石へ送る：まねで敵を歩かせ、落石の下へ", // 【RC11・最後の見直し】問題の名前とそろえた
            "まねの連鎖：あなたの攻撃は空振りでも、敵のまねが敵を倒す",
        };

        /// <summary>【RC11】5分体験の終わりの画面の絵（ui_ の後ろ）。</summary>
        public static readonly string[] QuickSummaryIcons = { "ic_copy", "hz_rock", "cardart_slash" }; // 【通常リリース版】3問（ずらす・赤と紫はチュートリアルと本編の1・2階で）; // 【RC12】5問目は まねの連鎖（斬撃）

        /// <summary>【RC4】1ランの流れ（チュートリアルの8番）の報酬の見本（本当の札・取っても本編には入らない）。</summary>
        public static readonly string[] RunLoopRewardCards = { "Whirl", "Lunge", "Tailwind" }; // 【RC6】本編と同じ形（通常戦は命令札3枚から1枚）。RC5 は命令札2＋戦術札2

        /// <summary>【RC4】1ランの流れの地図（docs/final/RUN_STRUCTURE.md の 8階）。</summary>
        public static readonly string[] RunLoopFloors = { "戦闘", "戦闘", "中ボス\nか 仕掛け", "休息\nか 商人", "戦闘\nか 仕掛け", "中ボス", "商人\nか 休息", "ボス" };

        /// <summary>完了の画面「あなたが覚えたこと」（上から順に：1ターン → 1戦闘 → 1ラン）。</summary>
        public static readonly string[] Summary =
        {
            "命令する：札を選ぶ → 向き → 実行（1ターンに3つまで）",
            "敵もまねる：あなたの命令を、敵がそれぞれの位置からまねる",
            "予告を読む：赤＝敵と動く攻撃（押すとずれる）・紫＝床に固定の攻撃（★は大技）・どちらも倒せば消える。本編では結果の予告は出ない：自分で読む",
            "敵の力を利用する：まねの斬撃や大技で、敵どうしを傷つける（緑の札は手助け）",
            "8階のボスを目指す：勝つ → 札を選ぶ → 扉を選ぶ → … → 8階のボス",
        };
    }
}
