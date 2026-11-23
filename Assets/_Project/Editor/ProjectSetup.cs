using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// データ（ステージ）と Battle シーンを作るエディタ用ツール。
    /// メニュー「CopyCommand > Setup」またはバッチ実行（-executeMethod CopyCommand.EditorTools.ProjectSetup.Run）で使う。
    /// 既にあるステージデータは上書きしない（Inspector で調整した内容を消さないため）。
    /// シーンは毎回作り直す。
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string StageFolder = Root + "/Data/Stages";
        const string CommandFolder = Root + "/Data/Commands";
        const string ScenePath = Root + "/Scenes/Battle.unity";

        // STAGE 2 の説明文（2026-10-03：STAGE 1〜3 は敵の固有行動なし。トゲは入るたびに5）
        static readonly string Stage2Tutorial = Lines(
            "あなたの命令は、次の ENEMY TURN に敵がそのまま使う（カード・方向・距離まで同じ。突進なら敵も3マス）。",
            "黄色のトゲは、入るたびに5ダメージ。敵が入っても同じ。",
            "敵の通り道にトゲがあるなら…？ 敵に「突進」を使わせて、自分からトゲへ突っ込ませよう（最大2命令）。");
        static readonly string Stage2Hint = Lines(
            "敵は、あなたと同じ命令を「敵の位置から」使う。あなたの通り道は安全でも、敵の通り道にはトゲがあることがある。",
            "盤面の端では、あなたは止まっても、敵は同じ命令で動けることがある。このズレも使える。");

        const string RushText = "3マス固定で移動（途中で止められない）。ぶつかったら手前で止まる";
        const string DashText = "2マス固定で移動（途中で止められない）。ぶつかったら手前で止まる";

        [MenuItem("CopyCommand/Setup（データ作成とシーン再生成）")]
        public static void Run()
        {
            ArtSetup.BuildAll(); // 【検証中】Art/ の画像 → Sprite 設定・Prefab・Animation・テーマ（VS ステージが使う）
            CreateCommandAssets();
            ApplyFixedDistanceSpec();
            AssetDatabase.SaveAssets();
            var paths = CreateStageAssets();
            ApplyMultiTestValidationSpec();
            var runPaths = CreateRunBattleAssets();
            AssetDatabase.SaveAssets();
            BuildScene(paths, runPaths);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] 完了");
        }

        static void CreateCommandAssets()
        {
            CreateIfMissing<CommandData>(CommandFolder + "/Walk.asset", c =>
            {
                c.displayName = "一歩";
                c.type = CommandType.Walk;
                c.description = "1マス移動";
                c.minDistance = 1;
                c.maxDistance = 1;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Rush.asset", c =>
            {
                c.displayName = "突進";
                c.type = CommandType.Rush;
                c.description = RushText;
                c.minDistance = 3;
                c.maxDistance = 3;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Dash.asset", c =>
            {
                c.displayName = "駆け足";
                c.type = CommandType.Rush;
                c.description = DashText;
                c.minDistance = 2;
                c.maxDistance = 2;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Slash.asset", c =>
            {
                c.displayName = "斬撃";
                c.type = CommandType.Slash;
                c.description = "隣のマスに4ダメージ";
                c.minDistance = 0;
                c.maxDistance = 0;
                c.damage = 4;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Push.asset", c =>
            {
                c.displayName = "押し出し";
                c.type = CommandType.Push;
                c.description = "隣のユニットを1マス押す";
                c.minDistance = 0;
                c.maxDistance = 0;
            });
            // 敵の固有行動専用（手札には入れない）
            CreateIfMissing<CommandData>(CommandFolder + "/EnemyAroundSlash.asset", c =>
            {
                c.displayName = "周囲斬り";
                c.type = CommandType.AroundSlash;
                c.description = "上下左右の隣に4ダメージ（斜めは外れる）";
                c.minDistance = 0;
                c.maxDistance = 0;
                c.damage = 4;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/EnemyCrossSlash.asset", c =>
            {
                c.displayName = "十字斬り";
                c.type = CommandType.AroundSlash;
                c.description = "上下左右へ2マスまで4ダメージ（壁で止まる・斜めは外れる）";
                c.minDistance = 0;
                c.maxDistance = 0;
                c.damage = 4;
                c.range = 2;
            });

            // RUN の報酬カード（AI 案。docs/RUN_DESIGN.md）
            CreateIfMissing<CommandData>(CommandFolder + "/Thrust.asset", c =>
            {
                c.displayName = "突き";
                c.type = CommandType.Slash;
                c.description = Lines("2マス先まで届く", "最初の相手に3ダメージ");
                c.minDistance = 0;
                c.maxDistance = 0;
                c.damage = 3;
                c.range = 2;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Whirl.asset", c =>
            {
                c.displayName = "回転斬り";
                c.type = CommandType.AroundSlash;
                c.description = Lines("方向なし", "上下左右の隣に3ダメージ");
                c.minDistance = 0;
                c.maxDistance = 0;
                c.damage = 3;
                c.range = 1;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Pull.asset", c =>
            {
                c.displayName = "引き寄せ";
                c.type = CommandType.Pull;
                c.description = Lines("3マス以内の相手を", "1マス引き寄せる");
                c.minDistance = 0;
                c.maxDistance = 0;
                c.range = 3;
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Trap.asset", c =>
            {
                c.displayName = "罠設置";
                c.type = CommandType.PlaceSpike;
                c.description = Lines("隣の空いた床に", "トゲを置く");
                c.minDistance = 0;
                c.maxDistance = 0;
            });

            // 【検証中】新しい移動札（M4 以降の大きい検証デッキ用）
            CreateIfMissing<CommandData>(CommandFolder + "/Jump.asset", c =>
            {
                c.displayName = "跳躍"; c.type = CommandType.Jump; c.minDistance = 2; c.maxDistance = 2;
                c.description = "ちょうど2マス先へ着地。間の壁・柱・トゲ・ユニットは越える。着地点がふさがっていれば跳べない";
                c.copyNote = "敵も2マス跳ぶ。柱を越えて追ってくる／あなたを飛び越えて挟み撃ちの位置が入れ替わる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Diagonal.asset", c =>
            {
                c.displayName = "斜め歩"; c.type = CommandType.Diagonal; c.minDistance = 1; c.maxDistance = 1;
                c.description = "斜めに1マス（↑＝右上・→＝右下・↓＝左下・←＝左上）";
                c.copyNote = "敵も同じ斜めへ。射線（行・列）から全員そろって外れる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Slide.asset", c =>
            {
                c.displayName = "滑走"; c.type = CommandType.Slide; c.minDistance = 0; c.maxDistance = 0;
                c.description = "壁・ユニット・端にぶつかるまで進む。通ったトゲは毎回踏む";
                c.copyNote = "敵も止まるまで滑る。止まる位置は敵ごとに全く違う（ズレが最大）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Blink.asset", c =>
            {
                c.displayName = "転移"; c.type = CommandType.Blink; c.minDistance = 3; c.maxDistance = 3;
                c.description = "ちょうど3マス先へ瞬間移動（壁も越える）。着地点がふさがっていれば失敗";
                c.copyNote = "敵も3マス先へ。壁の向こうへ敵を送れる／敵が壁を越えてくる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Swap.asset", c =>
            {
                c.displayName = "入れ替え"; c.type = CommandType.Swap; c.minDistance = 0; c.maxDistance = 0;
                c.description = "隣のユニットと位置を入れ替える（どちらも相手のマスに入る：トゲ・ルーンが発動）";
                c.copyNote = "敵も隣のユニットと入れ替わる。あなたが隣にいれば、あなたの位置を動かされる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/VarRush.asset", c =>
            {
                c.displayName = "可変突進"; c.type = CommandType.Rush; c.minDistance = 2; c.maxDistance = 3;
                c.description = "2か3マスを選んで移動（途中で止められない）";
                c.copyNote = "敵は、あなたが選んだ距離をそのまま使う";
            });
            // 【検証専用】中央固定の対策案（盤面の端・ユニットの位置で結果が変わる移動）。どのデッキ・ステージにも入れない（解析とテストだけ）
            CreateIfMissing<CommandData>(CommandFolder + "/WallKick.asset", c =>
            {
                c.displayName = "壁蹴り"; c.type = CommandType.WallKick; c.minDistance = 0; c.maxDistance = 0;
                c.description = "背中側が壁・盤面の端なら、選んだ方向へ最大3マス。そうでなければ1マスだけ";
                c.copyNote = "壁を背にしていない敵は1マスしか動かない。端にいる者だけが大きく動ける";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Vault.asset", c =>
            {
                c.displayName = "踏み越え"; c.type = CommandType.Vault; c.minDistance = 0; c.maxDistance = 0;
                c.description = "正面の隣のユニットを越えて、2マス先へ着地。正面に誰もいなければ空振り";
                c.copyNote = "正面に誰かがいる敵だけが跳ぶ。あなたの背後の敵は、あなたを越えて前に出る";
            });
            // 【検証中】新しい攻撃札（2026-10-04：「どう攻撃するか」の選択肢を増やす。docs/research/ATTACK_CARDS.md）
            CreateIfMissing<CommandData>(CommandFolder + "/PushSlash.asset", c =>
            {
                c.displayName = "押し斬り"; c.type = CommandType.PushSlash; c.minDistance = 0; c.maxDistance = 0; c.damage = 3; c.range = 1;
                c.description = "隣に3ダメージ＋1マス押す";
                c.copyNote = "敵もあなたを斬って押す。押された先にトゲ・炎があると危険";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Sweep.asset", c =>
            {
                c.displayName = "薙ぎ払い"; c.type = CommandType.Sweep; c.minDistance = 0; c.maxDistance = 0; c.damage = 2; c.range = 1;
                c.description = "正面と左右の3マスに2ダメージ";
                c.copyNote = "敵も3マスを払う。隣り合う敵同士が当たりやすい（あなたも当たりやすい）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Pierce.asset", c =>
            {
                c.displayName = "貫通"; c.type = CommandType.Pierce; c.minDistance = 0; c.maxDistance = 0; c.damage = 2; c.range = 3;
                c.description = "正面3マスの全員に2ダメージ";
                c.copyNote = "敵の列の後ろから撃たせると、前の敵ごと貫く。あなたも並ぶと一緒に貫かれる";
            });
            // 【検証中】ACT1 のビルド用（2026-10-04 夜）：ドロー・多命令・コピー操作
            CreateIfMissing<CommandData>(CommandFolder + "/Foresight.asset", c =>
            {
                c.displayName = "先読み"; c.type = CommandType.Foresight; c.minDistance = 0; c.maxDistance = 0; c.range = 3;
                c.description = "次のターンの始めに、山札の上3枚を見て1枚を手札へ（盤面では何もしない）";
                c.copyNote = "敵がコピーしても何も起きない（敵の命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Reconsider.asset", c =>
            {
                c.displayName = "再考"; c.type = CommandType.Reconsider; c.minDistance = 0; c.maxDistance = 0; c.range = 2;
                c.description = "次のターン、追加で2枚引く（盤面では何もしない）";
                c.copyNote = "敵がコピーしても何も起きない";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Rally.asset", c =>
            {
                c.displayName = "号令"; c.type = CommandType.Rally; c.minDistance = 0; c.maxDistance = 0; c.range = 2;
                c.description = "このターンの命令の上限 +2（この札も1枠使う＝実質 +1）。増えた命令は敵もコピーする";
                c.copyNote = "号令そのものは何も起きないが、増えた命令は全部敵に渡る";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Sneak.asset", c =>
            {
                c.displayName = "忍び足"; c.type = CommandType.Walk; c.minDistance = 1; c.maxDistance = 1; c.notCopied = true;
                c.description = "1マス移動。この命令は敵にコピーされない";
                c.copyNote = "コピーされない（敵はこの命令を飛ばす）";
            });
            // 【検証中】コピー操作（2026-10-04 深夜：D 系統の強化を検証）
            CreateIfMissing<CommandData>(CommandFolder + "/MirrorSlash.asset", c =>
            {
                c.displayName = "反転斬り"; c.type = CommandType.Slash; c.minDistance = 0; c.maxDistance = 0; c.damage = 3; c.range = 1; c.copyMirrored = true;
                c.description = "正面の隣に3ダメージ。敵はこの命令を反対向きでまねする";
                c.copyNote = "敵は反対向きに斬る：背後の敵の斬撃はあなたから外れる。斬った正面の敵は斬り返してくる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/ReverseCopy.asset", c =>
            {
                c.displayName = "逆さ写し"; c.type = CommandType.ReverseCopy; c.minDistance = 0; c.maxDistance = 0;
                c.description = "このターン、敵は命令を逆の順番でコピーする（盤面では何もしない）";
                c.copyNote = "敵は最後の命令から順に実行する";
            });
            // 【検証中】一文で分かるコピー操作（2026-10-05：反転斬り・逆さ写しは分かりにくい／効果が見えないため、報酬から外して入れ替え）
            CreateIfMissing<CommandData>(CommandFolder + "/ChippedSlash.asset", c =>
            {
                c.displayName = "欠け斬り"; c.type = CommandType.Slash; c.minDistance = 0; c.maxDistance = 0; c.damage = 4; c.range = 1; c.copyDamageCut = 3;
                c.description = "正面の隣に4ダメージ。敵がコピーすると威力は1";
                c.copyNote = "欠けた写し：敵も同じ向きに斬るが、威力は1しかない";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Lunge.asset", c =>
            {
                c.displayName = "踏み込み"; c.type = CommandType.Rush; c.minDistance = 2; c.maxDistance = 2; c.copyDistanceCut = 1;
                c.description = "2マス進む。敵がコピーすると1マスだけ";
                c.copyNote = "短い写し：敵は1マスしか動かない＝敵との間合いが1マス詰まる（離れる）";
            });
            // 【検証中】コピーされる回復（2026-10-05 本人の案）：自分を回復。敵がコピーすると、敵も自分を回復する
            CreateIfMissing<CommandData>(CommandFolder + "/FirstAid.asset", c =>
            {
                // 解析（Act1Tension.RunHealCard）：4〜5 だと毎戦ほぼ満タンに戻り戦闘が延びた → 3・消耗（1戦で1回）
                c.displayName = "応急処置"; c.type = CommandType.Heal; c.minDistance = 0; c.maxDistance = 0; c.range = 3; c.exhaust = true;
                c.description = "自分の HP を3回復（この戦闘で1回）。敵がコピーすると、敵も自分を3回復する";
                c.copyNote = "敵も回復する：満タンに近い敵なら無駄になる。瀕死の敵に渡すと延命させる";
            });
            // 【検証中】誘い足（2026-10-05 カード監査）：あなたは1マス、敵は反対向きに1マス＝間合いが2変わる
            CreateIfMissing<CommandData>(CommandFolder + "/Lure.asset", c =>
            {
                c.displayName = "誘い足"; c.type = CommandType.Walk; c.minDistance = 1; c.maxDistance = 1; c.copyMirrored = true;
                c.description = "1マス移動。敵はこの命令を反対向きでまねする（敵の方へ歩けば敵も寄ってくる）";
                c.copyNote = "敵は反対向きに1マス：近づけば寄ってくる／離れれば離れる＝間合いが2変わる";
            });
            // 【検証中・Copy Core V3】刻写の看板札（研究 2026-10-05：倒し方で手に入る。あなたには弱く、まねた敵には強い）
            CreateIfMissing<CommandData>(CommandFolder + "/CopyBlade.asset", c =>
            {
                c.displayName = "写し刃"; c.type = CommandType.PushSlash; c.minDistance = 0; c.maxDistance = 0; c.damage = 2; c.copyDamageCut = -3;
                c.description = "隣に2＋1マス押す。まねた敵は隣に5＋1マス押す";
                c.copyNote = "敵（まるまね・手まね）は 5＋押す。並べた敵どうしを斬らせる札。押すので並びは崩れる";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Tailwind.asset", c =>
            {
                c.displayName = "追い風"; c.type = CommandType.Rush; c.minDistance = 1; c.maxDistance = 1; c.copyDistanceCut = -1;
                c.description = "1マス進む。まねた敵は2マス進む";
                c.copyNote = "敵（まるまね・足まね）は2マス進む。敵だけ遠くへ運ぶ（トゲ・踏み板・円）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Aim.asset", c =>
            {
                c.displayName = "狙い定め"; c.type = CommandType.Aim; c.minDistance = 0; c.maxDistance = 0;
                c.description = "向きだけ変える（何も動かない）。まねた敵も向きだけ変える";
                c.copyNote = "槍兵・弓兵・盾兵の向きを決める。向いた先の仲間を突かせる・撃たせる";
            });
            // 【検証中・Copy Core V3】号令の札：あなたは何もしない。写した敵だけが使う（研究で「斬撃の空振り・追い風と同じ役」と判断し、抽選からは外した）
            CreateIfMissing<CommandData>(CommandFolder + "/Incite.asset", c =>
            {
                c.displayName = "けしかけ"; c.type = CommandType.Slash; c.minDistance = 0; c.maxDistance = 0; c.damage = 4; c.range = 1; c.selfInert = true;
                c.description = "あなたは何もしない。写した敵が、矢印の向きの隣を斬る（4）";
                c.copyNote = "敵（全写し・技写し）が、その場から矢印の向きに斬る。敵どうしを斬らせる号令";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/March.asset", c =>
            {
                c.displayName = "進め"; c.type = CommandType.Walk; c.minDistance = 1; c.maxDistance = 1; c.selfInert = true;
                c.description = "あなたは動かない。写した敵が、矢印の向きに1マス進む";
                c.copyNote = "敵（全写し・歩写し）が1マス進む。トゲ・踏み板・円へ運ぶ号令";
            });
            // 【Puzzle v2（2026-10-06）】手札を回す札：命令に入れた瞬間に効く（このターンの命令に使える）。敵がまねても盤面では何も起きない
            CreateIfMissing<CommandData>(CommandFolder + "/DrawTwo.asset", c =>
            {
                c.displayName = "手繰り寄せ"; c.type = CommandType.Draw; c.minDistance = 0; c.maxDistance = 0; c.range = 2;
                c.description = "命令に入れた瞬間に2枚引く（このターンの命令に使える）。入れたら取り消せない";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Footwork.asset", c =>
            {
                c.displayName = "足取り"; c.type = CommandType.Draw; c.minDistance = 0; c.maxDistance = 0; c.range = 2; c.drawFilter = DrawFilter.Move;
                c.description = "命令に入れた瞬間に、移動の札を2枚引く（山札から探す）";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/BladeDrill.asset", c =>
            {
                c.displayName = "武技整理"; c.type = CommandType.Draw; c.minDistance = 0; c.maxDistance = 0; c.range = 2; c.drawFilter = DrawFilter.Technique;
                c.description = "命令に入れた瞬間に、技の札（攻撃・押す・引く）を2枚引く（山札から探す）";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Insight.asset", c =>
            {
                c.displayName = "深読み"; c.type = CommandType.Insight; c.minDistance = 0; c.maxDistance = 0;
                c.description = "命令に入れた瞬間に、山札の上3枚から1枚を選んで手札へ";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Resonance.asset", c =>
            {
                c.displayName = "共鳴補給"; c.type = CommandType.Resonate; c.minDistance = 0; c.maxDistance = 0; c.range = 2;
                c.description = "このラウンドに、敵のまねで仕掛け（封印石・石像・鏡核・扉など）が動いたら、次のターン +2枚";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            // 研究（2026-10-06）の5枚＋武技整理・合わせ斬り（刻写）。数値を変えたら毎回反映する（CreateOrUpdate）
            CreateOrUpdate<CommandData>(CommandFolder + "/RallyDraw.asset", c =>
            {
                c.displayName = "号令・改"; c.type = CommandType.Rally; c.minDistance = 0; c.maxDistance = 0; c.range = 2; c.planDraw = 2; c.drawFilter = DrawFilter.MoveAndTechnique; c.exhaust = true;
                c.description = "このターン命令の上限 +2（この札も1枠）、命令に入れた瞬間に移動の札1枚と技の札1枚を引く。1戦1回";
                c.copyNote = "増えた命令も全部まねされる（号令自身は、敵では何もしない）";
            });
            CreateOrUpdate<CommandData>(CommandFolder + "/Recycle.asset", c =>
            {
                c.displayName = "再利用"; c.type = CommandType.Draw; c.minDistance = 0; c.maxDistance = 0; c.range = 1; c.fromDiscard = true; c.drawFilter = DrawFilter.Move;
                c.description = "命令に入れた瞬間に、捨て札の移動の札を1枚選んで手札へ（「さっきの一歩をもう一度」）";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            // 【Combat V4】戦術札（緑）：別のデッキ・命令枠を使わない・敵はまねしない。1枚1アセット（Tactic_<種類>.asset）
            foreach (TacticKind tk in System.Enum.GetValues(typeof(TacticKind)))
            {
                if (tk == TacticKind.None) continue;
                var kind = tk;
                CreateOrUpdate<CommandData>(CommandFolder + "/Tactic_" + kind + ".asset", c =>
                {
                    c.displayName = TacticRules.Name(kind); c.type = CommandType.Tactic; c.tactic = kind; c.minDistance = 0; c.maxDistance = 0; c.notCopied = true;
                    c.description = TacticRules.Text(kind);
                    c.exhaust = kind == TacticKind.Mend; // 【RC2】手当ては1戦に1回
                    c.copyNote = "戦術札：命令枠を使わない。敵はまねしない";
                });
            }
            CreateOrUpdate<CommandData>(CommandFolder + "/ResonantStep.asset", c =>
            {
                c.displayName = "共鳴の一歩"; c.type = CommandType.Walk; c.minDistance = 1; c.maxDistance = 1; c.resonateDraw = 2;
                c.description = "1マス移動。このラウンドに、まね（敵の移動・攻撃）で仕掛けが動いたら（紋・封印石・核・扉。戦闘の部屋では鎧割り・鏡晶・樽・石像も）、次のターン +2枚";
                c.copyNote = "敵も同じ向きに1マス（引くのはあなただけ）";
            });
            CreateOrUpdate<CommandData>(CommandFolder + "/EchoBlade.asset", c =>
            {
                c.displayName = "余韻の刃"; c.type = CommandType.Slash; c.minDistance = 0; c.maxDistance = 0; c.damage = 4; c.range = 1; c.copyKillDraw = 1;
                c.description = "正面の隣に4。このターンに、まね（敵の攻撃・まねの移動）で敵が倒れたら、次のターン +1枚";
                c.copyNote = "敵も同じ向きに斬る（4）。引くのはあなただけ";
            });
            CreateOrUpdate<CommandData>(CommandFolder + "/JointSlash.asset", c =>
            {
                c.displayName = "合わせ斬り"; c.type = CommandType.Slash; c.minDistance = 0; c.maxDistance = 0; c.damage = 3; c.range = 1; c.copyWide = true;
                c.description = "正面の隣に3。まねた敵は正面と左右の3マスに3（広い）";
                c.copyNote = "まねた敵だけ3マス幅で斬る（同期の位置が1マスずれても届く。あなたに当たる危険も広い）";
            });
            CreateOrUpdate<CommandData>(CommandFolder + "/Footwork.asset", c =>
            {
                c.displayName = "足取り"; c.type = CommandType.Draw; c.minDistance = 0; c.maxDistance = 0; c.range = 2; c.drawFilter = DrawFilter.Move;
                c.description = "命令に入れた瞬間に、移動の札を2枚引く（山札から探す）。「この距離の移動札が無い」を直す";
                c.copyNote = "敵は何もしない（命令1つ分が空く）";
            });
            CreateIfMissing<CommandData>(CommandFolder + "/Spirit.asset", c =>
            {
                c.displayName = "闘気"; c.type = CommandType.Spirit; c.minDistance = 0; c.maxDistance = 0; c.damage = 0; c.range = 3;
                c.description = "このターン、次の直接攻撃＋3";
                c.copyNote = "敵も力をためる。闘気の後の攻撃は敵も＋3（あなたに当たると大ダメージ）";
            });

            // 「敵にコピーされると…」の説明（空のときだけ書く。Inspector で直した文は消さない）
            SetCardText("Jump", "ちょうど2マス先へ着地。間の壁・柱・トゲ・ユニットは越える。着地点がふさがっていれば跳べない",
                "敵も2マス跳ぶ。柱を越えて追ってくる／あなたを飛び越えて挟み撃ちの位置が入れ替わる");
            SetCardText("MirrorSlash", "正面の隣に3ダメージ。敵はこの命令を反対向きでまねする（正面で斬った鏡兵・騎士は斬り返さない）",
                "普通の敵は反対向きに斬る（正面の敵は斬り返してくる）。鏡兵・騎士は二重に反転して、あなたと同じ向き＝正面の鏡兵・騎士からは返ってこないが、背後にいれば当たる");
            SetCardText("WallKick", "背中側が壁・柱・盤面の端なら、選んだ方向へ3マス。そうでなければ1マスだけ",
                "背中が壁・柱・盤面の端の敵だけ3マス動き、ほかの敵は1マス：柱を背にして離れると、敵は1マスしか追ってこない");
            FillCopyNote("Walk", "敵も同じ方向へ1マス歩く。壁や端で止まると位置がずれる");
            FillCopyNote("Rush", "敵も同じ方向・距離で突進。敵の通り道にトゲがあれば自分から踏む");
            FillCopyNote("Slash", "敵も同じ方向を斬る。敵に向かって斬れば、コピーは反対側に出て空振り");
            FillCopyNote("Push", "敵も同じ方向へ押す。敵に向かって押せばコピーは空振り。2人の位置関係を変えられる");
            FillCopyNote("Thrust", "届く距離が長いぶん、2マス離れていても当たることがある");
            FillCopyNote("Whirl", "方向が無いので、コピーの時点で敵の隣にいると必ず当たる");
            FillCopyNote("Pull", "敵も同じ方向へ引き寄せる。敵に向かって使えばコピーは空振り");
            FillCopyNote("Trap", "敵も自分の隣（同じ向き）にトゲを置く。盤面のトゲが増え続ける");
        }

        /// <summary>
        /// 2026-10-03 本人決定「移動距離を自由選択にしない」を、すでにあるデータにも反映する（何度実行しても同じ結果）。
        /// 一歩＝1マス固定、駆け足＝2マス固定、突進＝3マス固定。距離はプレイヤーが選ばない（方向だけ選ぶ）。
        /// </summary>
        static void ApplyFixedDistanceSpec()
        {
            var walk = Command("Walk");
            walk.displayName = "一歩";
            walk.minDistance = 1;
            walk.maxDistance = 1;
            walk.copyNote = "敵も同じ方向へ1マス動く。壁や端で止まると位置がずれる";
            EditorUtility.SetDirty(walk);

            var rush = Command("Rush");
            rush.minDistance = 3;
            rush.maxDistance = 3;
            rush.description = RushText;
            rush.copyNote = "敵も同じ方向へ3マス突進（距離も同じ）。敵の通り道にトゲがあれば自分から踏む";
            EditorUtility.SetDirty(rush);

            var dash = Command("Dash");
            dash.minDistance = 2;
            dash.maxDistance = 2;
            dash.description = DashText;
            if (string.IsNullOrEmpty(dash.copyNote)) dash.copyNote = "敵も同じ方向へ2マス走る（距離も同じ）";
            EditorUtility.SetDirty(dash);

            // STAGE 2：固有行動を外し、説明文を最新ルールに（2026-10-03 本人決定：STAGE 1〜3 は固有行動なし）
            var stage2 = AssetDatabase.LoadAssetAtPath<StageData>($"{StageFolder}/Stage2.asset");
            if (stage2 != null)
            {
                stage2.enemyInnateAction = null;
                stage2.tutorialText = Stage2Tutorial;
                stage2.hintAfterFirstTurn = Stage2Hint;
                EditorUtility.SetDirty(stage2);
            }

            // チュートリアルの説明文の「歩く」を「一歩」に
            foreach (var name in new[] { "Stage1", "Stage2" })
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>($"{StageFolder}/{name}.asset");
                if (stage == null) continue;
                stage.tutorialText = stage.tutorialText.Replace("歩く", "一歩");
                stage.hintAfterFirstTurn = stage.hintAfterFirstTurn.Replace("歩く", "一歩");
                EditorUtility.SetDirty(stage);
            }
            var sandbox = AssetDatabase.LoadAssetAtPath<StageData>($"{StageFolder}/Sandbox_AllCommands.asset");
            if (sandbox != null && !sandbox.availableCommands.Contains(dash))
            {
                sandbox.availableCommands.Insert(1, dash);
                EditorUtility.SetDirty(sandbox);
            }
        }

        /// <summary>ルールを変えたカードの説明を、既存のアセットにも書き直す（CreateIfMissing は新しく作るときだけ書くため）。</summary>
        static void SetCardText(string name, string description, string copyNote)
        {
            var c = Command(name);
            if (c == null) return;
            if (c.description == description && c.copyNote == copyNote) return;
            c.description = description;
            c.copyNote = copyNote;
            EditorUtility.SetDirty(c);
        }

        static void FillCopyNote(string name, string note)
        {
            var c = Command(name);
            if (c == null || !string.IsNullOrEmpty(c.copyNote)) return;
            c.copyNote = note;
            EditorUtility.SetDirty(c);
        }

        /// <summary>RUN の3戦。トゲは 2（RUN の仮値：歩くが何回でも使えるとトゲ往復が強すぎるため）。</summary>
        static List<string> CreateRunBattleAssets()
        {
            var list = new List<string>();
            list.Add(CreateIfMissing<StageData>(StageFolder + "/RunBattle1.asset", s =>
            {
                s.stageTitle = "背中の壁";
                s.buttonLabel = "R1";
                s.tutorialText = Lines(
                    "敵の左は壁、上は盤面の端。敵が動けない方向の命令を渡すと、あなたと敵の位置がずれる。",
                    "敵の固有行動は【周囲斬り】（赤いマス）。トゲは 2 ダメージ（RUN の仮値）。");
                s.map = Lines("..#E...", ".......", ".^...^.", ".......", "...P...");
                s.enemyMaxHp = 12;
                s.spikeDamage = 2;
                s.maxSlots = 3;
                s.enemyInnateAction = Command("EnemyAroundSlash");
            }));
            list.Add(CreateIfMissing<StageData>(StageFolder + "/RunBattle2.asset", s =>
            {
                s.stageTitle = "廊下";
                s.buttonLabel = "R2";
                s.tutorialText = Lines(
                    "敵は壁に挟まれた廊下にいる。廊下の出入口は3か所。廊下の両端にトゲ。",
                    "廊下の中の敵は上下に動けない。あなたの命令が、敵にとってどこで止まるかを考えよう。");
                s.map = Lines("#######", ".^.E.^.", "#.#.#.#", ".......", "...P...");
                s.enemyMaxHp = 14;
                s.spikeDamage = 2;
                s.maxSlots = 3;
                s.enemyInnateAction = Command("EnemyAroundSlash");
            }));
            list.Add(CreateIfMissing<StageData>(StageFolder + "/RunBattle3.asset", s =>
            {
                s.stageTitle = "中央の島";
                s.buttonLabel = "R3";
                s.tutorialText = Lines(
                    "最後の敵の固有行動は【十字斬り】：上下左右へ2マスまで届く（壁で止まる）。まっすぐ下がっても逃げられない。",
                    "斜めへ逃げるか、柱（壁）の陰に入ろう。");
                s.map = Lines(".......", ".#.^.#.", "...E...", ".#.^.#.", "...P...");
                s.enemyMaxHp = 16;
                s.spikeDamage = 2;
                s.maxSlots = 3;
                s.enemyInnateAction = Command("EnemyCrossSlash");
            }));
            return list;
        }

        static CommandData Command(string name) => AssetDatabase.LoadAssetAtPath<CommandData>($"{CommandFolder}/{name}.asset");

        /// <summary>ステージデータを作り、シーンに登録する順番でパスを返す。</summary>
        static List<string> CreateStageAssets()
        {
            var list = new List<string>();

            // STAGE 1「コピーされる」：一歩・斬撃、壁なし・トゲなし、最大2命令。
            // 配置の理由は docs/LEVEL_DESIGN.md を参照。
            list.Add(CreateIfMissing<StageData>(StageFolder + "/Stage1.asset", s =>
            {
                s.stageTitle = "STAGE 1 コピーされる";
                s.buttonLabel = "1";
                s.tutorialText = Lines(
                    "あなたが使った技は、次の ENEMY TURN に敵が【同じ技・同じ方向・同じ順番】で使います。",
                    "まずは ①一歩 →右　②斬撃 →右 を登録して「実行」してみよう。",
                    "登録した命令は、右の赤い枠「敵の予定」にもそのまま入ります。");
                s.hintAfterFirstTurn = Lines(
                    "敵も「一歩 →右」「斬撃 →右」を使った？ 敵の斬撃は、敵のさらに右に出て空振りしたはず。",
                    "同じ命令でも、使う人の位置が違えば結果が変わる。敵の左から右へ斬れば、敵のコピーは当たらない。",
                    "逆に、敵のいない方向へ斬ると…？");
                s.map = Lines(
                    ".......",
                    ".P.E...",
                    ".......");
                s.playerMaxHp = 12;
                s.enemyMaxHp = 12;
                s.maxSlots = 2;
                s.availableCommands = new List<CommandData> { Command("Walk"), Command("Slash") };
            }));

            // STAGE 2「コピーを利用する」：一歩・突進（3マス固定）・斬撃、トゲ2つ、最大2命令、敵の固有行動「周囲斬り」。
            // 案1「端で一歩ずらす」（本人採用）。配置の理由は docs/LEVEL_DESIGN.md と STAGE2_DESIGN.md。
            list.Add(CreateIfMissing<StageData>(StageFolder + "/Stage2.asset", s =>
            {
                s.stageTitle = "STAGE 2 コピーを利用する";
                s.buttonLabel = "2";
                s.tutorialText = Stage2Tutorial;
                s.hintAfterFirstTurn = Stage2Hint;
                s.map = Lines(
                    ".....",
                    ".....",
                    "^.E..",
                    "....^",
                    "..P..");
                s.playerMaxHp = 12;
                s.enemyMaxHp = 12;
                s.spikeDamage = 5;
                s.maxSlots = 2;
                s.availableCommands = new List<CommandData> { Command("Walk"), Command("Rush"), Command("Slash") };
                s.enemyInnateAction = null; // STAGE 1〜3 は固有行動なし（2026-10-03 本人決定）
            }));

            // STAGE 3「命令を組み合わせる」：一歩・駆け足・斬撃・押し出し（突進なし）、最大3命令、固有行動なし、敵 HP 18（仮）。
            // トゲの小部屋に敵。斬撃→押し出しの順番と、「敵の次のターンのための3枚目」を教える。理由は docs/LEVEL_DESIGN.md 8章。
            list.Add(CreateIfMissing<StageData>(StageFolder + "/Stage3.asset", s =>
            {
                s.stageTitle = "STAGE 3 命令を組み合わせる";
                s.buttonLabel = "3";
                s.tutorialText = Lines(
                    "命令は最大3つ。全部使わなくてもいい。使った命令は、全部そのまま敵に渡る。",
                    "敵の真上にトゲがある。「斬撃」と「押し出し」、どちらを先に使う？ 順番で結果が変わる。",
                    "3つ目の命令は、あなたのためではなく「敵の次の行動」のために使うこともできる。");
                s.hintAfterFirstTurn = Lines(
                    "押し出しを先に使うと、敵が離れて斬撃が空振りする。斬撃 → 押し出し の順なら両方当たる。",
                    "あなたには意味のない命令でも、敵がコピーすると意味が生まれることがある。敵をトゲから下ろす命令は？");
                s.map = Lines(
                    ".###.",
                    ".#^#.",
                    ".#E#.",
                    "..P..");
                s.playerMaxHp = 12;
                s.enemyMaxHp = 18;
                s.spikeDamage = 5;
                s.maxSlots = 3;
                s.availableCommands = new List<CommandData> { Command("Walk"), Command("Dash"), Command("Slash"), Command("Push") };
                s.enemyInnateAction = null;
            }));

            // テスト用：4つの技・壁・トゲを自由に試すステージ（正式ステージではない）
            list.Add(CreateIfMissing<StageData>(StageFolder + "/Sandbox_AllCommands.asset", s =>
            {
                s.stageTitle = "TEST 全技お試し";
                s.buttonLabel = "T";
                s.isTestStage = true;
                s.tutorialText = "動作確認用。技・壁（濃いグレー）・トゲ（黄色の三角）を自由に試せます。正式なステージではありません。";
                s.map = Lines(
                    ".......",
                    ".#..^..",
                    ".P...E.",
                    "..^..#.",
                    ".......");
                s.maxSlots = 3;
                s.availableCommands = new List<CommandData> { Command("Walk"), Command("Dash"), Command("Rush"), Command("Slash"), Command("Push") };
            }));

            // 複数Enemyテスト（通常コピーの敵2体）。正式ステージではない（isTestStage）。STAGE 3 と同じカード・最大3命令・敵 HP 8×2（仮）。
            // 盤面は C# 解析（SolverRunner：候補探し → 詳しい解析）で選んだ。理由と解析結果は docs/LEVEL_DESIGN.md 9章。
            list.Add(CreateIfMissing<StageData>(StageFolder + "/StageM1.asset", s =>
            {
                s.stageTitle = "TEST M1 挟み撃ち（同じ斬撃・違う結果）";
                s.buttonLabel = "M1";
                s.isTestStage = true;
                s.tutorialText = Lines(
                    "敵が2体。ENEMY TURN は 敵A が全部の命令 → 敵B が全部の命令 の順。",
                    "右の敵を斬ると、左の敵のコピーはあなたを斬る。同じ「斬撃」でも、敵ごとに結果が違う。",
                    "斬ったあと、その場所にいたら…？ 動いたら、空いた場所に誰が来る？");
                s.map = Lines(
                    "EPE..",
                    "#....",
                    "#....",
                    ".....",
                    ".....");
                SetMultiTestValues(s);
            }));
            list.Add(CreateIfMissing<StageData>(StageFolder + "/StageM2.asset", s =>
            {
                s.stageTitle = "TEST M2 同士討ちを狙う";
                s.buttonLabel = "M2";
                s.isTestStage = true;
                s.tutorialText = Lines(
                    "敵の攻撃は、別の敵にも当たる。ただし敵が勝手に戦うことはない。当たるのは、あなたが渡した命令だけ。",
                    "敵2体は離れている。壁や端で片方だけ止めて、2体を隣に並べられれば…？",
                    "直接殴ろうとすると、もう1体のコピーに斬られやすい。");
                s.map = Lines(
                    "#E.E.",
                    ".....",
                    "..P#.",
                    ".....",
                    ".....");
                SetMultiTestValues(s);
            }));
            list.Add(CreateIfMissing<StageData>(StageFolder + "/StageM3.asset", s =>
            {
                s.stageTitle = "TEST M3 トゲと2体";
                s.buttonLabel = "M3";
                s.isTestStage = true;
                s.tutorialText = Lines(
                    "左の敵のすぐ左にトゲ。左へ動けば、左の敵はトゲに入る。",
                    "でも同じ命令で、右の敵もあなたに近づく。そのあとの「斬撃」は、右の敵にもコピーされる。",
                    "片方に有利な命令が、もう片方では危険になる。");
                s.map = Lines(
                    "......",
                    "^E.P.E",
                    "......",
                    "......");
                SetMultiTestValues(s);
            }));
            list.AddRange(ApplyNextTestSet());
            list.AddRange(CreateCopySandboxStages());
            list.AddRange(CreateCopyV3Stages());
            list.AddRange(CreatePuzzleStages());
            list.AddRange(CreateCombatStages());
            return list;
        }

        // ───── 【戦闘中心 v3（2026-10-06）】戦闘中心 ACT1 v3 の部屋 ─────
        // 盤面・数値・説明は tools/combat_rooms.txt（tools/pzsearch の sim / act と同じファイル。asset= のある部屋だけアセットにする）
        static List<string> CreateCombatStages()
        {
            var paths = new List<string>();
            foreach (var c in PuzzleStudy.ParseCandidates("tools/combat_rooms.txt").Concat(PuzzleStudy.ParseCandidates("tools/combat_v4_rooms.txt")).Concat(PuzzleStudy.ParseCandidates("tools/combat_v4_tutorial.txt")).Concat(PuzzleStudy.ParseCandidates("tools/festival_rooms.txt")).Concat(PuzzleStudy.ParseCandidates("tools/festival_quests.txt"))) // 【文化祭クエスト v2】tools/festival_quests.txt 【文化祭】tools/festival_rooms.txt 【Combat V4】【RC3】チュートリアル
            {
                if (string.IsNullOrEmpty(c.asset)) continue;
                var src = c.stage;
                string path = NextStage(new StageSpec { File = c.asset, Label = c.label ?? c.id, Title = src.stageTitle, Facings = src.enemyFacings, EnemyHp = 8, SpikeDamage = src.spikeDamage,
                    Theme = c.asset.StartsWith("StageKB") || c.asset.StartsWith("StageXC") ? "Crystal" : "Ruins", Tutorial = src.tutorialText, Map = src.GetMapRows() });
                var s = AssetDatabase.LoadAssetAtPath<StageData>(path);
                s.playerMaxHp = 20;
                s.copySandbox = false;
                s.copyLab = false;
                s.puzzleRoom = false;
                s.combatRoom = true;
                s.combatV4 = src.combatV4;                 // 【Combat V4】
                if (src.combatV4) { s.map = src.map; s.altMaps = new List<string>(src.altMaps); } // 【完成版】盤面と開始配置の別案は部屋ファイルが正（毎回そろえる）
                s.firePatterns = new List<string>(src.firePatterns);
                s.fireDamage = src.fireDamage;
                s.hazardName = src.hazardName;
                s.turnLimit = src.turnLimit;
                s.noClocks = src.noClocks;         // 【RC3】
                s.stillEnemies = src.stillEnemies; // 【RC11】敵は自分では動かない
                s.bomberAim = src.bomberAim;       // 【RC11】砲兵の最初の照準
                s.clocksReady = src.clocksReady;   // 【RC11】大技を始めから①
                s.clocksReadyOnly = src.clocksReadyOnly; // 【RC11】その記号の敵だけ
                s.clockKeep = src.clockKeep;       // 【文化祭】大技は先頭の N 個だけ
                s.tutorialStep = src.tutorialStep; // 【RC3】
                if (src.tutorialStep > 0 && src.availableCommands != null && src.availableCommands.Count > 0) s.availableCommands = new List<CommandData>(src.availableCommands);
                s.copyMode = CopyMode.Current;
                s.enemyHpList = src.enemyHpList;
                s.enemyCopyTypes = src.enemyCopyTypes;
                s.coreGuard = src.coreGuard;
                s.circleGuard = src.circleGuard;
                s.goal = src.goal;
                s.goalText = src.goalText;
                s.statueFacings = src.statueFacings;
                s.statueSpin = src.statueSpin;
                s.sandboxGoal = string.IsNullOrEmpty(src.sandboxGoal) ? src.goalText : src.sandboxGoal;
                s.handTestDeckLarge = new List<CommandData>(src.handTestDeckLarge);
                s.handTestDeck = new List<CommandData>(src.handTestDeck);
                EditorUtility.SetDirty(s);
                paths.Add(path);
            }
            return paths;
        }

        // ───── 【検証中】Copy Core V2 サンドボックス（2026-10-05）：コピーを使わないと勝てないマイクロステージ ─────
        // 既定の方式は C（反転・すぐ）。タイトル →「Copy Core V2」から、方式 A/B/C/D と COPY ON/OFF を切り替えて遊ぶ。ACT1 には入れない
        static List<string> CreateCopySandboxStages()
        {
            var paths = new List<string>();
            paths.Add(SandboxStage("StageCV1", "CV1", "CV1 反響鎧", "6,6,8", "DDD",
                "反響鎧兵は、あなたの技では傷つかない。敵の技（コピー）ならトゲ・同士討ちで傷つく",
                Lines("反響鎧兵（青い鎧）：あなたの攻撃・押し出しでは傷つかない。",
                      "敵があなたの命令をコピーして動いたり攻撃したりした結果（トゲに入る・同士討ち）なら傷つく。",
                      "トゲ（3）：コピーの移動で敵をトゲへ歩かせる。目の前の兵士（敵C）は普通に倒せる。"),
                ".......", ".R^.^R.", ".......", "...E...", "...P...", "......."));
            paths.Add(SandboxStage("StageCV2", "CV2", "CV2 コピー封印", "8,10", "DD",
                "封印兵は、封印（2）がある間は傷つかない。敵のコピー攻撃を2回当てると封印が割れる",
                Lines("封印兵（紫）：封印 2。あなたの攻撃では割れない。敵のコピー攻撃（同士討ち）が当たるたびに -1。",
                      "封印が割れたら、あなたの攻撃も通る。",
                      "隣の兵士（敵A）にコピーで封印兵を斬らせる：コピーの向きを読む。"),
                ".......", "..EG...", ".......", "...P...", ".......", "......."));
            paths.Add(SandboxStage("StageCV3", "CV3", "CV3 二つの踏み板", "6,6", "DD",
                "あなたが青い踏み板、敵が赤い踏み板に同時に乗ると扉が開く。コピーの移動で敵を運ぶ",
                Lines("青い踏み板（あなた用）と赤い踏み板（敵用）。両方が同時に踏まれると、右下の扉が開く。",
                      "扉の奥の敵（敵B）は、扉が開かないと届かない。",
                      "敵Aはあなたの移動をコピーする：あなたが動くと、敵Aがどこへ行くかを読む。"),
                "......q", ".......", "....E..", "...P...", "..q....", ".p...#g", ".....#E"));
            paths.Add(SandboxStage("StageCV4", "CV4", "CV4 鏡晶", "8,8", "DD",
                "鏡晶は、敵のコピー攻撃が当たると砕けて、まわり8マスに 4 ダメージ。あなたの攻撃でははじく",
                Lines("鏡晶（紫の結晶）：あなたの攻撃では砕けない。敵がコピーした攻撃が当たると砕けて、まわり8マスに 4。",
                      "反響鎧兵も鏡晶の爆発なら傷つく。あなたも近くにいると巻き込まれる。",
                      "鏡晶は2つだけ。足りない分は、反響鎧兵どうしを殴り合わせるか、右下の隅のトゲへコピーの移動で歩かせる。"),
                ".......", ".XR.RX.", ".......", ".......", "...P...", "......^"));
            paths.Add(SandboxStage("StageCV5", "CV5", "CV5 写し砲", "8,8,1,1", "DDUU",
                "写し砲は、あなたの攻撃の技だけをコピーして一直線に撃つ。反響鎧兵を砲の射線へ運んで撃たせる",
                Lines("写し砲（左下・右下）：動かない・倒れない。あなたの攻撃の技をコピーして、一直線に撃つ（最初に当たった相手へ）。",
                      "反響鎧兵はあなたの技では傷つかない。写し砲の弾なら傷つく。",
                      "あなたが射線に立つと、あなたが撃たれる。"),
                ".......", ".R...R.", ".......", "...P...", ".......", "Z.....Z"));
            paths.Add(SandboxStage("StageCV6", "CV6", "CV6 鏡の番人（マイクロボス）", "24,8", "DD",
                "鏡の番人は封印が割れた2ラウンドだけ傷つく。割り方は段階で変わる：コピー攻撃 → 鏡晶 → 封印の円",
                Lines("封印が割れると2ラウンドだけ傷つき、その間は写せない（殴っても返らない）。HP 2/3・1/3 で止まって封印が戻り、中央へ帰る。",
                      "割り方：コピー攻撃2回 → 鏡晶の爆発 → コピーの移動で白い円へ。砕けた鏡晶は毎ラウンド戻る。3ラウンドごとに大剣（周り8マス・3）。"),
                ".o...o.", ".......", "..X.X..", "...W...", "..E....", ".......", "...P..."));
            return paths;
        }

        // ───── 【Puzzle v2（2026-10-06）】パズル中心 ACT1 v2 の部屋 ─────
        // 盤面・目標・説明は tools/puzzle_rooms.txt（ボットの解析 PuzzleStudy.RunCandidates と同じファイル。asset= のある部屋だけアセットにする）
        static List<string> CreatePuzzleStages()
        {
            var paths = new List<string>();
            foreach (var c in PuzzleStudy.ParseCandidates("tools/puzzle_rooms.txt"))
            {
                if (string.IsNullOrEmpty(c.asset)) continue;
                var src = c.stage;
                string path = NextStage(new StageSpec { File = c.asset, Label = c.label ?? c.id, Title = src.stageTitle, Facings = src.enemyFacings, EnemyHp = 8, SpikeDamage = src.spikeDamage,
                    Theme = c.asset.StartsWith("StagePB") ? "Crystal" : "Ruins", Tutorial = src.tutorialText, Map = src.GetMapRows() });
                var s = AssetDatabase.LoadAssetAtPath<StageData>(path);
                s.playerMaxHp = 20;
                s.copySandbox = false;
                s.copyLab = false;
                s.puzzleRoom = true;
                s.copyMode = CopyMode.Current;
                s.enemyHpList = src.enemyHpList;
                s.enemyCopyTypes = src.enemyCopyTypes;
                s.coreGuard = src.coreGuard;
                s.circleGuard = src.circleGuard;
                s.goal = src.goal;
                s.goalText = src.goalText;
                s.statueFacings = src.statueFacings;
                s.statueSpin = src.statueSpin;
                s.sandboxGoal = src.goalText;
                s.handTestDeckLarge = new List<CommandData>(src.handTestDeckLarge);
                s.handTestDeck = new List<CommandData>(src.handTestDeck);
                EditorUtility.SetDirty(s);
                paths.Add(path);
            }
            return paths;
        }

        // ───── 【検証中・Copy Core V3（2026-10-05）】コピー中心のミニ ACT1 と研究室の部屋 ─────
        // 写し方は今のまま（方式 A：同じ札・同じ向き・同じ距離）。敵ごとのコピーの種類（A 全写し／T 技写し／M 歩写し）と盤面の仕掛けで、コピーを「敵を操る道具」にする。
        // copyLab：研究室の一覧に出る・COPY ON/OFF が効く。ACT1（今の地図）には出ない
        static List<string> CreateCopyV3Stages()
        {
            // 説明は2〜3行・1行40字前後（レビュー：左下の欄で切れていた）。印：「手」＝手まね、「足」＝足まね、印なし＝まるまね
            var paths = new List<string>();
            paths.Add(CopyV3Stage("StageC0", "C0", "C0 まねの3種（見本）", "6,6,6", "DDD", "ATM", false,
                "見本：同じ札で、誰が何をまねるかの違い。印なし＝全部、「手」＝手の札（青以外）だけ、「足」＝青い札だけ",
                Lines("左から 印なし（全部まねる）・「手」（青以外の札だけ・歩かない）・「足」（青い札だけ・攻撃しない）。",
                      "↓へ歩くと、印なしと「足」がトゲへ。斬ると、印なしと「手」が斬る。"),
                ".......", ".E.E.E.", ".^.^.^.", ".......", "...P...", "......."));
            paths.Add(CopyV3Stage("StageC1", "C1", "C1 写しの間（基礎・壁ズレ）", "8,8", "DD", "AA", false,
                "初級：敵（印なし）はあなたの札を同じ向き・同じ距離でまねる。柱で自分だけ止まると敵だけ進む。樽・写し箱・トゲ。COPY OFF でも勝てるが遅い",
                Lines("敵（印なし）はあなたの札を、同じ向き・同じ距離でまねる。柱で自分だけ止まると、敵だけ進む。",
                      "樽＝札の攻撃で爆発（周り8マスに3）。箱＝敵がまねた攻撃でだけ開く（ルーン片）。"),
                ".......", ".$EbE..", "..^.^..", "...#...", "...P...", "......."));
            paths.Add(CopyV3Stage("StageC2", "C2", "C2 砲台の回廊（手まね）", "8,8,8", "DDD", "AAT", false,
                "中級・攻撃：反響鎧兵にあなたの技は効かない。歩かない手まね兵の隣へ反響鎧兵を歩かせ、手まね兵に斬らせる。COPY OFF では倒せない",
                Lines("「手」の兵は歩かず、手の札（攻撃・押す・引く）だけその場でまねる＝砲台。",
                      "反響鎧兵にあなたの技は効かない（最後の1体になると効く）。手まね兵の隣へ歩かせてから斬る向きを選ぶ。"),
                ".......", ".R...Y.", ".......", "....E..", ".......", "...P..."));
            paths.Add(CopyV3Stage("StageC3", "C3", "C3 対の鏡核（同期）", "8,8,8", "DDD", "AAA", true,
                "中級・同期：二つの核を同じラウンドに叩くと、敵の守りが消える。あなたとまねで同時に（1人では届かない）。砲兵の予告も避ける",
                Lines("敵は核の守りで傷つかない。二つの核を同じラウンドに叩くと守りが消える（1人では届かない距離）。",
                      "核へ歩けば、敵も核へ歩く。ぶつかって止まったら同じ向きに斬る。砲兵＝あなたの位置に十字を予告。"),
                "...c..E", ".......", "....S..", "......O", ".......", "c...P.."));
            paths.Add(CopyV3Stage("StageC4", "C4", "C4 運び手の間（足まね）", "8,8", "DD", "AM", false,
                "中級・移動：「足」の兵は青い札だけまねる。あなたが青、足まね兵が赤の板に同時に乗ると扉が開く（扉の外に敵がいなくなっても開く）",
                Lines("「足」の兵は青い札（移動）だけ同じ向きでまねる。あなたが青い板、足まね兵が赤い板に同時に乗ると扉が開く。",
                      "右の壁で自分だけ止めて運び、柱で足まね兵だけ止めてから青い板へ（壁ズレ2回）。"),
                "##.K.##", "##ggg##", "...^...", "...E.#q", ".......", "p..P..."));
            paths.Add(CopyV3Stage("StageCD1", "CD1", "CD1 反響の砦（危険）", "8,10,8,8", "DDDD", "AAAT", false,
                "危険・攻撃と移動：反響鎧兵2体にあなたの技は効かない。トゲへ歩かせる・手まね兵や互いに斬らせる・砲兵の爆発に巻き込む。COPY OFF では勝てない",
                Lines("反響鎧兵2体にあなたの技は効かない（トゲ・まね・爆発なら効く。最後の1体になると効く）。",
                      "砲兵＝あなたの位置に十字を予告し、次のラウンドに爆発（3・敵にも当たる）。"),
                "R..O..R", "..^.^..", "...E...", ".....^.", ".......", "...P..."));
            paths.Add(CopyV3Stage("StageCD2", "CD2", "CD2 鏡晶の庭（危険）", "10,12,12", "DDD", "AAA", false,
                "危険・攻撃：鏡晶は敵のまねた攻撃でだけ砕け、周り8マスに4。剣兵・槍兵を爆発に巻き込む。COPY OFF だと HP 12 を正面から削るしかない",
                Lines("鏡晶（晶）は敵がまねた攻撃でだけ砕け、周り8マスに 4（あなたにも当たる）。",
                      "剣兵＝周り4マス、槍兵＝最後の向きへ2マス、砲兵＝予告した十字（どれも敵にも当たる）。"),
                "...O...", ".X...X.", "..S....", "...X...", "....Y..", "...P..."));
            paths.Add(CopyV3Stage("StageCB", "CB", "CB 写し身の主（ボス）", "30,8,8", "DDD", "ATT", false,
                "ボス：段階1 核を同時に叩く（同期）／段階2 癒しの写し身に主を斬らせて砕く（攻撃）／段階3 まねの移動で主を円へ（移動）",
                Lines("段階1：二つの核を同じラウンドに叩く（主もあなたの斬撃をまねる）→ 守りが割れ、主は中央で2ターン動けない。",
                      "段階2：写し身に主を斬らせて砕く（全部砕くと主は 40% へ）。段階3：まねの移動で主を白い円へ。光柱・大剣に注意。"),
                "o.....o", ".c.....", "...L...", ".H...H.", ".......", ".c.....", "...P..."));
            return paths;
        }

        static string CopyV3Stage(string file, string label, string title, string hpList, string facings, string copyTypes, bool coreGuard, string goal, string tutorial, params string[] map)
        {
            string path = NextStage(new StageSpec { File = file, Label = label, Title = title, Facings = facings, EnemyHp = 8, SpikeDamage = 3, Theme = file == "StageCB" ? "Crystal" : "Ruins", Tutorial = tutorial, Map = map });
            var s = AssetDatabase.LoadAssetAtPath<StageData>(path);
            s.playerMaxHp = 20;
            s.copySandbox = false;
            s.copyLab = true;
            s.copyMode = CopyMode.Current;
            s.enemyHpList = hpList;
            s.enemyCopyTypes = copyTypes;
            s.coreGuard = coreGuard;
            s.sandboxGoal = goal;
            var deck = new List<CommandData>();
            foreach (var n in new[] { "Walk", "Walk", "Dash", "Dash", "Rush", "Slash", "Slash", "Slash", "Thrust", "PushSlash", "Push", "Pull" }) deck.Add(Command(n));
            s.handTestDeckLarge = deck;
            s.handTestDeck = new List<CommandData>(deck);
            EditorUtility.SetDirty(s);
            return path;
        }

        /// <summary>サンドボックスの1ステージ（NextStage の設定 ＋ HP 20・ACT1 と同じ最初のデッキ・方式 C）。</summary>
        static string SandboxStage(string file, string label, string title, string hpList, string facings, string goal, string tutorial, params string[] map)
        {
            string path = NextStage(new StageSpec { File = file, Label = label, Title = title, Facings = facings, EnemyHp = 8, SpikeDamage = 3, Theme = "Ruins", Tutorial = tutorial, Map = map });
            var s = AssetDatabase.LoadAssetAtPath<StageData>(path);
            s.playerMaxHp = 20;
            s.copySandbox = true;
            s.copyMode = CopyMode.MirrorImmediate;
            s.enemyHpList = hpList;
            s.sandboxGoal = goal;
            // ACT1 と同じ最初のデッキ（12枚：一歩2・駆け足2・突進・斬撃3・突き・押し斬り・押し出し・引き寄せ）。7枚補充
            var deck = new List<CommandData>();
            foreach (var n in new[] { "Walk", "Walk", "Dash", "Dash", "Rush", "Slash", "Slash", "Slash", "Thrust", "PushSlash", "Push", "Pull" }) deck.Add(Command(n));
            s.handTestDeckLarge = deck;
            s.handTestDeck = new List<CommandData>(deck);
            EditorUtility.SetDirty(s);
            return path;
        }

        /// <summary>複数Enemyテスト共通の値（STAGE 3 と同じカード、最大3命令、固有行動なし、敵 HP 8）。</summary>
        static void SetMultiTestValues(StageData s)
        {
            s.playerMaxHp = 12;
            s.enemyMaxHp = 8;
            s.spikeDamage = 5;
            s.maxSlots = 3;
            s.availableCommands = new List<CommandData> { Command("Walk"), Command("Dash"), Command("Slash"), Command("Push") };
            s.enemyInnateAction = null;
            SetValidationDeck(s);
        }

        /// <summary>
        /// 【検証用】M1〜M3 の手札方式 A/B 用デッキ（2026-10-03 本人の再テスト依頼）。完成版の初期デッキ仕様ではない。
        /// 10枚：斬撃3・押し出し2・一歩3・駆け足2。初手5枚を「斬撃・斬撃・一歩・駆け足・押し出し」に固定し、
        /// 残りも決まった順番で引く（A と B で同じ初手・同じ山札順。山札が切れた後のシャッフルは A/B 共通の種）。
        /// 鏡蝕ゲージの検証も M1〜M3 で行う（ON/OFF は画面右下）。
        /// </summary>
        static void SetValidationDeck(StageData s)
        {
            s.handTestDeck = new List<CommandData>
            {
                Command("Slash"), Command("Slash"), Command("Walk"), Command("Dash"), Command("Push"), // 初手（固定）
                Command("Slash"), Command("Walk"), Command("Push"), Command("Dash"), Command("Walk"),   // 山札（この順に引く）
            };
            s.handTestOpeningCount = 5;
            s.handTestKeepOrder = true;
            s.handTestGuaranteeAttack = true; // Claude Code の追加提案：攻撃札が1枚も無いターンを無くす（手札の枚数は変えない）
            s.stagnationAllowed = true;
            s.stagnationMax = 3;
            s.stagnationPenalties = new List<int> { 2, 3, 4 };
        }

        /// <summary>
        /// 【検証中】次期検証セット（M4〜M10・M6b・MB1）。兵種・炎床・回復ルーンを1つずつ試すテスト用ステージ。
        /// 生成専用のテストステージなので、毎回この内容で上書きする（盤面は解析で調整した。理由は docs/LEVEL_DESIGN.md 10章）。
        /// 盤面の記号：E 通常 / K 弓兵 / T 盾兵 / C 追跡兵 / N 逃げ兵 / B 鏡守の騎士 / 1〜9 回復ルーンの地点。
        /// </summary>
        static List<string> ApplyNextTestSet()
        {
            var paths = new List<string>();
            // M4 弓兵：弓兵・あなた・通常の敵が同じ行。敵を斬る（→）と、弓兵も → を向き、背中から矢が飛んでくる（背中側の法則の遠距離版）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM4", Label = "M4", Title = "TEST M4 弓兵", Facings = "R",
                Tutorial = Lines(
                    "弓兵（K）：コピーのあと、向いている方向へ矢を放つ（4マス・最初のユニットに2）。矢は敵にも当たる。",
                    "向き＝最後にコピーした命令の方向。あなたの命令列の「最後の方向」が、弓兵の狙いになる。",
                    "赤いマス＝今組んでいる命令のあとに矢が通るマス（組み替えると変わる）。"),
                Map = new[]
                {
                    ".......",
                    ".......",
                    "K..P.E.",
                    ".......",
                    "...#...",
                    ".......",
                },
            }));
            // M5 盾兵：盾の向き＝最後にコピーした方向。このターンの最後の方向が、次のターンの盾の向きになる
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM5", Label = "M5", Title = "TEST M5 盾兵", Facings = "D",
                Tutorial = Lines(
                    "盾兵（T）：正面（「盾」の側）からの直接攻撃と矢は -3。トゲ・炎は防げない。",
                    "向き＝最後にコピーした命令の方向。このターン最後に使った方向が、次のターンの盾の向きになる。",
                    "横・後ろから斬る／押し出してトゲへ／別の敵に斬らせる。"),
                Map = new[]
                {
                    ".......",
                    "...T...",
                    ".^.....",
                    ".E.....",
                    "...P...",
                    ".......",
                },
            }));
            // M6 追跡兵：毎ターン近づいてくる＝あなたの「背中側」に回り込んでくる敵
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM6", Label = "M6", Title = "TEST M6 追跡兵",
                Tutorial = Lines(
                    "追跡兵（C）：コピーのあと、あなたへ1マス近づく（紫のマス＝移動先）。",
                    "規則：縦と横で遠い方から。同じなら横。トゲ・燃えるマス・回復ルーンには自分から入らない。隣まで来たら止まる。",
                    "背中側に来た追跡兵は、あなたの攻撃のコピーであなたを斬る。近づいてくる位置を読む。"),
                Map = new[]
                {
                    ".......",
                    ".^...C.",
                    ".......",
                    "...P...",
                    ".E...^.",
                    ".......",
                },
            }));
            // M6b 逃げ兵：離れていく敵を、壁や角へ追い込む
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM6b", Label = "M6b", Title = "TEST M6b 逃げ兵",
                Tutorial = Lines(
                    "逃げ兵（N）：コピーのあと、あなたから1マス離れる（紫のマス＝移動先）。",
                    "規則：縦と横で遠い方へ。同じなら横。壁・端・トゲで逃げられなければ止まる。",
                    "角や壁際へ追い込むと逃げられない。盤面の端へ行く理由になる。"),
                Map = new[]
                {
                    ".......",
                    ".#.....",
                    "...N...",
                    ".E.P...",
                    ".....#.",
                    ".......",
                },
            }));
            // M7 炎床：真ん中の4行が奇数列・偶数列で交互に燃える（上下の端の行＝敵の最初の行は燃えない）。真ん中では毎ラウンド位置を変える必要がある
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM7", Label = "M7", Title = "TEST M7 炎床",
                Tutorial = Lines(
                    "炎床：橙のマスは、このラウンドの最後（コピー → 兵種の行動 のあと）に燃える。上にいる全員に3（敵にも）。",
                    "薄い橙＝次のラウンドに燃えるマス。真ん中の4行が、奇数列 → 偶数列 の順に交互に燃える（上下の行・左右の端の列は燃えない）。",
                    "真ん中で同じ場所に留まれない（左右の端の列は燃えない避難所）。逃げる命令は敵もコピーする。敵を燃えるマスへ運ぶ。"),
                Map = new[]
                {
                    ".E.....",
                    ".......",
                    ".......",
                    "...P...",
                    ".......",
                    ".....E.",
                },
                Fire = new[]
                {
                    "......./.F.F.F./.F.F.F./.F.F.F./.F.F.F./.......",
                    "......./..F.F../..F.F../..F.F../..F.F../.......",
                },
            }));
            // M8 回復ルーン：あなたは HP 6 から。ルーンは四隅を循環。取りに行く命令は敵もコピーする
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM8", Label = "M8", Title = "TEST M8 回復ルーン", PlayerStartHp = 6,
                Tutorial = Lines(
                    "回復ルーン（緑）：入ったユニットが3回復（敵も）。使うと次の地点（薄い緑）へ移る。1戦で3回まで。",
                    "あなたは HP 6 から始まる。ルーンは 左上 → 右上 → 右下 → 左下 の順に、盤面の四隅を回る。",
                    "あなたが取りに行く命令は、敵もコピーする。敵に先に踏ませないように。"),
                Map = new[]
                {
                    "1.....2",
                    "..E....",
                    ".......",
                    "...P...",
                    ".......",
                    "4....E3",
                },
            }));
            // M9 複合：M4 の弓兵の並び ＋ M7 の炎の帯
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM9", Label = "M9", Title = "TEST M9 複合（弓兵＋炎床）", Facings = "R",
                Tutorial = Lines(
                    "弓兵（K）＋通常（E）＋炎床（M4 と M7 の組み合わせ）。",
                    "矢（赤）と炎（橙）の両方を避けながら、矢と炎を敵に当てる。",
                    "炎は真ん中の4行で、奇数列 → 偶数列 を交互にくり返す（弓兵の列は燃えない）。"),
                Map = new[]
                {
                    ".......",
                    ".......",
                    ".K.P...",
                    ".......",
                    ".....E.",
                    ".......",
                },
                Fire = new[]
                {
                    "......./.F.F.F./...F.F./.F.F.F./.F.F.../.......",
                    "......./..F.F../..F.F../..F.F../..F.F../.......",
                },
            }));
            // M10 実戦寄り：盾兵・弓兵・通常の3体＋回復ルーン（あなたは HP 8 から）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM10", Label = "M10", Title = "TEST M10 実戦寄り（盾兵＋弓兵＋通常＋回復）", Facings = "DRD", PlayerStartHp = 8,
                Tutorial = Lines(
                    "敵3体：盾兵（T）・弓兵（K）・通常（E）＋回復ルーン（緑）。あなたは HP 8 から。ACT1 の通常戦を想定。",
                    "同じ命令の「最後の方向」が、盾兵には盾の向きを、弓兵には狙いを与える。",
                    "ルーンは 左上 → 右上 → 右下 → 左下。敵に踏ませない。"),
                Map = new[]
                {
                    "1..T..2",
                    ".......",
                    "K..P...",
                    ".......",
                    ".....E.",
                    "4.....3",
                },
            }));
            // MB1 鏡守の騎士：方向反転コピー（鏡）＋前半は盾、後半は矢＋炎床
            paths.Add(NextStage(new StageSpec
            {
                File = "StageMB1", Label = "MB1", Title = "TEST MB1 鏡守の騎士（簡易ボス）", EnemyHp = 20, Facings = "D",
                Tutorial = Lines(
                    "鏡守の騎士（B）：あなたの命令を「方向を反対にして」コピーする（鏡）。前半は盾（正面からの攻撃・矢は -3）。",
                    "向き＝最後にコピーした（反対向きの）方向。HP が半分以下になると盾を失い、矢を放つ。次のラウンドから炎床も燃える。",
                    "前半は「盾の向きをずらしてから攻める」、後半は「射線と炎を避けながら攻める」。"),
                Map = new[]
                {
                    ".......",
                    "...B...",
                    ".......",
                    ".#...#.",
                    ".......",
                    "...P...",
                    ".......",
                },
                Fire = new[]
                {
                    "......./......./FF...FF/FF...FF/......./......./.......",
                    "......./......./......./..FFF../..FFF../......./.......",
                },
                FireAfterKnightHalf = true,
            }));
            // 【検証専用】M11 中央が燃える：検証すること＝「中央3×3に居続けると明確に損」になれば、高機動札があっても中央固定が崩れるか（CENTER_STAGE_IDEAS.md #1）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM11", Label = "M11", Title = "TEST M11 中央が燃える（検証専用）",
                Tutorial = Lines(
                    "【検証専用】中央3×3が、1ラウンドおきに燃える（3ダメージ・敵にも）。外周は燃えない。",
                    "最初のラウンドの最後に中央が燃える。次のラウンドは燃えない。その次はまた燃える。",
                    "中央は「敵を送り込む場所」。敵を中央に置き去りにすると炎で削れる。"),
                Map = new[]
                {
                    ".E.....",
                    ".......",
                    ".......",
                    "...P...",
                    ".......",
                    ".....E.",
                },
                Fire = new[]
                {
                    "......./......./..FFF../..FFF../..FFF../.......",
                    "......./......./......./......./......./.......",
                },
            }));
            // 【検証専用】M12 中央の柱：検証すること＝盤面の形で中央を「両側に届く場所」でなくすと、中央固定と敵の遠隔操作（高機動札）が弱まるか（CENTER_STAGE_IDEAS.md #10）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageM12", Label = "M12", Title = "TEST M12 中央の柱（検証専用）",
                Tutorial = Lines(
                    "【検証専用】中央の4本の柱（壁）が、移動・攻撃・射線をさえぎる。",
                    "柱にぶつかったコピーは空振りする（あなたも敵も）。柱の向こうへは斬撃が届かない。",
                    "柱の周りを回り込む／柱で敵のコピーだけを止める。"),
                Map = new[]
                {
                    ".......",
                    ".E...E.",
                    "..#.#..",
                    "...P...",
                    "..#.#..",
                    ".......",
                },
            }));
            // ───── 【検証中】Combat V2（2026-10-04）：どの敵も「コピー＋固有行動1つ」、どの盤面も「動く理由」を1つ ─────
            // 記号：S 剣兵 / Y 槍兵 / O 砲兵 / M 鏡兵 / V 鏡守の騎士V2 / w・u ワープ陣（同じ文字の2マスが1組）。トゲは3、敵 HP 10（仮）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV1", Label = "V1", Title = "V1 剣兵＋トゲ", Facings = "DD", EnemyHp = 14, SpikeDamage = 3,
                Tutorial = Lines(
                    "剣兵（剣）：あなたの命令をコピーしたあと、自分の上下左右（隣の4マス）を斬る（3）。敵にも当たる。",
                    "ラウンドの終わりに剣兵の隣にいると斬られる。斬ってから下がっても、剣兵はコピーで追ってくる。",
                    "中央のトゲ（3）：敵を押し込む／自分は踏まない。"),
                Map = new[] { ".S...S.", ".......", "..^.^..", "...P...", ".......", "......." },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV2", Label = "V2", Title = "V2 弓兵＋剣兵＋ワープ陣", Facings = "LD", EnemyHp = 14, SpikeDamage = 3,
                Tutorial = Lines(
                    "ワープ陣（水色の◆）：入ると対のワープ陣へ移る（敵も同じ）。出口がふさがっていれば移らない。",
                    "弓兵（弓）：コピーのあと、向いている直線へ矢（4マス・2）。剣兵（剣）：隣の4マスを斬る（3）。",
                    "ワープで射線から逃げる／敵をワープさせて射線に入れる。"),
                Map = new[] { "w.....K", ".......", "..S....", "...P...", ".......", "......w" },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV3", Label = "V3", Title = "V3 槍兵＋移動する回復ルーン", Facings = "DU", EnemyHp = 14, SpikeDamage = 3, PlayerStartHp = 8,
                Tutorial = Lines(
                    "槍兵（槍）：コピーのあと、向いている方向へ2マスまっすぐ突く（3）。離れていても届く。",
                    "回復ルーン（緑）：入ると3回復（敵も）。使うと次の地点（薄い緑）へ移る：左上→右上→右下→左下。あなたは HP 8 から。",
                    "ルーンの取り合い：取りに行く命令は敵もコピーする。"),
                Map = new[] { "1.....2", "..Y....", ".......", "...P...", "....Y..", "4.....3" },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV4", Label = "V4", Title = "V4 砲兵＋安全地帯の移動", Facings = "DU", EnemyHp = 14, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "砲兵（砲）：あなたのいる場所を中心に十字5マスを予告し（橙の×）、次のラウンドに爆発（3・敵にも）。止まっていると当たる。",
                    "炎床：左半分＋中央の列 → 右半分＋中央の列 が交互に燃える（上下の行は燃えない）。最初のラウンドは左側が燃える。",
                    "安全な側へ動き続ける。爆発や炎に敵を残す。"),
                Map = new[] { "...O...", ".......", ".......", "...P...", ".......", ".S....." },
                Fire = new[]
                {
                    "......./FFFF.../FFFF.../FFFF.../FFFF.../.......",
                    "......./...FFFF/...FFFF/...FFFF/...FFFF/.......",
                },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV5", Label = "V5", Title = "V5 鏡兵＋中央が燃える", EnemyHp = 14, SpikeDamage = 3,
                Tutorial = Lines(
                    "鏡兵（鏡）：あなたの命令を「方向を反対にして」コピーする。あなたが右へ行けば、鏡兵は左へ。",
                    "炎床：中央3×3が1ラウンドおきに燃える（最初のラウンドから）。",
                    "鏡兵を中央へ送り込んで燃やす／自分は中央から出る。"),
                Map = new[] { ".M.....", ".......", ".......", "...P...", ".......", ".....M." },
                Fire = new[]
                {
                    "......./......./..FFF../..FFF../..FFF../.......",
                    "......./......./......./......./......./.......",
                },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageV6", Label = "V6", Title = "V6 剣・槍・砲＋ワープ陣", Facings = "DRL", EnemyHp = 14, SpikeDamage = 3,
                Tutorial = Lines(
                    "剣兵（隣4マス）・槍兵（向いた方向へ2マス）・砲兵（十字の爆発予告）の3体＋ワープ陣（左上⇔右下）。",
                    "どの敵の行動も、あなたの命令の最後の方向（向き）と、あなたの位置で決まる。",
                    "ワープで位置を崩す。爆発の予告に敵を入れる。"),
                Map = new[] { "w..S...", ".......", "Y......", "...P...", "......O", "......w" },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageVB", Label = "VB", Title = "VB 鏡守の騎士V3（検証ボス）", Facings = "D", EnemyHp = 55, SpikeDamage = 3, Theme = "Crystal",
                Tutorial = Lines(
                    "鏡守の騎士V3（王）：攻撃だけ反対向きにコピーする（あなたが騎士を斬ると、騎士の斬撃はあなたへ返る）。移動はそのままコピー。",
                    "王の上の数字＝あと何ラウンドで来るか（最初から3つとも見える）：剣＝大剣（周り8マス・4）／爆＝爆発（あなたの位置の十字・3）／炎＝炎の波（中央3×3 ⇔ 外周・3）。1 になると当たるマスが光る。",
                    "HP 60% 以下：大剣が速くなる（3→2 ラウンドごと）。HP 30% 以下：爆発が大十字（9マス）・炎の波が速くなる（4→3）。",
                    "トゲ（3）とワープ陣2組（同じ色どうし・対角）：騎士をトゲへ誘う／ワープで間合いを外す。"),
                Map = new[] { "u.....w", ".......", "..^.^..", "...Q...", ".......", "w..P..u", "......." },
            }));
            // 【検証中】Visual Vertical Slice（2026-10-04）：見た目の見本の1ステージ。ルールは V 系と同じ（剣兵＋砲兵・トゲ・壁・ワープ陣）。
            // 見た目のテーマ（ArtSetup が作る RuinsTheme）を使う唯一のステージ。ゲーム性の検証用ではなく「完成版に近い見た目」の確認用
            paths.Add(NextStage(new StageSpec
            {
                File = "StageVS", Label = "VS", Title = "VS 神殿の見本（Visual Vertical Slice）", Facings = "DR", EnemyHp = 14, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "【見た目の見本】古代神殿の盤面。黄色の枠＝トゲ（3）、水色の魔法陣＝ワープ陣（もう一方へ移る）、壁の柱は通れない。",
                    "砲兵（爆弾）：あなたの位置に照準（赤い円）を予告し、次のラウンドに爆発（3・敵にも）。剣兵（角の兜）：コピーのあと、隣の4マスを斬る（3）。",
                    "赤い斜線＝今の敵の攻撃範囲。爆発の予告から動きながら、剣兵をトゲや爆発へ誘う。"),
                Map = new[] { "w..#...", ".....O.", "..^....", "...#...", ".S..^..", "...P...", "#.....w" },
            }));
            // ───── 【検証中】ACT1 専用の通常戦（2026-10-05）：「コピーが怖い」を通常戦に。V 系（単体の検証ステージ）は変えずに残す ─────
            // 盤面は Act1Tension（ボット4種・コピー圧力）で 6 回比べて選んだ（tools/act1_rooms.txt・docs/research/ACT1_TENSION_REPORT.md）。1ステージ1テーマ
            paths.Add(NextStage(new StageSpec
            {
                File = "StageA1", Label = "A1", Title = "ACT1 棘の回廊", Facings = "DU", EnemyHp = 12, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "剣兵（剣）：あなたの命令をコピーしたあと、自分の隣4マスを斬る（3）。敵どうしにも当たる。",
                    "剣兵は上と下から挟んでいる。あなたが下がると、剣兵もコピーで同じだけ動く＝逃げても隣についてくる。",
                    "柱（通れない）：トゲの横に2本。真ん中だけが狭い通路。剣兵のコピー移動は柱で止まる＝柱を使えば間合いを変えられる。"),
                // 2026-10-05 本人プレイ後：地形（柱で狭い通路）を足した（tools/act1_rooms.txt の thorn_w1）
                Map = new[] { "..S....", ".......", ".^#.#^.", "...P...", ".......", "....S.." },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageA2", Label = "A2", Title = "ACT1 転移の間", Facings = "DU", EnemyHp = 14, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "弓兵（弓）：コピーのあと、向いている方向へ矢（4マス・2）。向きは「あなたの最後の命令の方向」になる。",
                    "弓兵はあなたの真後ろ（下）。上の剣兵を斬って最後に上を向くと、背中を撃たれる。",
                    "柱（通れない）2本：矢も柱で止まる＝柱の陰で射線を切る。ワープ陣（水色）は弓兵の列から外れる手段。"),
                // 2026-10-05 本人プレイ後：柱2本（warp_w2）
                Map = new[] { "w...S..", "....#..", "...P...", "..#....", "...K...", "......w" },
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageA3", Label = "A3", Title = "ACT1 癒しの泉", Facings = A3Facings, EnemyHp = A3EnemyHp, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(A3Tutorial),
                Map = A3Map,
            }));
            paths.Add(NextStage(new StageSpec
            {
                File = "StageA4", Label = "A4", Title = "ACT1 鏡の礼拝堂", Facings = "DD", EnemyHp = 12, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "鏡兵（鏡）：あなたの命令を「反対の方向」でコピーする。鏡兵を斬ると、鏡兵の斬撃はあなたへ返ってくる。",
                    "剣兵（剣）はそのままの向きでコピーし、隣4マスを斬る（3）。同じ命令で、鏡兵と剣兵は逆へ動く。",
                    "炎床：中央3×3が1ラウンドおきに燃える（最初のラウンドから）。柱2本（上下）：逆に動く鏡兵を柱で止める。"),
                // 2026-10-05 本人プレイ後：柱2本（mirror_w2）
                Map = new[] { ".M.....", "...#...", ".......", "...P...", ".......", "...#.S." },
                Fire = new[]
                {
                    "......./......./..FFF../..FFF../..FFF../.......",
                    "......./......./......./......./......./.......",
                },
            }));
            // 【検証中】ACT1 の3つ目の危険戦（2026-10-05 RC）：弓・槍・剣の「最後の向き」3種＋柱2本＋トゲ。tools/act1_rooms.txt の ad_watch
            paths.Add(NextStage(new StageSpec
            {
                File = "StageAD1", Label = "AD1", Title = "ACT1 見張りの柱廊", Facings = "DRL", EnemyHp = 14, SpikeDamage = 3, Theme = "Ruins",
                Tutorial = Lines(
                    "弓兵（上）・槍兵（左）・剣兵（右）：どれもコピーのあと、あなたの最後の命令の向きで撃つ・突く・斬る。",
                    "弓兵はあなたの真上の列。最後に上を向くと、縦の射線に入ったまま撃たれる。柱（通れない・矢も止まる）2本で射線を切る。",
                    "下のトゲ（3）2つ：押し出し・コピー移動で敵をトゲへ。"),
                Map = new[] { "...K...", ".#...#.", ".......", "Y..P..S", ".......", "..^.^.." },
            }));
            // 【検証中】ACT1 のボス（2026-10-05 本人プレイ後）：VB と同じ騎士V3。ワープを外して「柱2本＋回復陣（一度）＋トゲ」。HP は ACT1 の部屋（38）
            paths.Add(NextStage(new StageSpec
            {
                File = "StageAB", Label = "AB", Title = "ACT1 鏡守の騎士", Facings = "D", EnemyHp = 38, SpikeDamage = 3, Theme = "Crystal",
                Tutorial = Lines(
                    "鏡守の騎士（王）：攻撃だけ反対向きにコピーする（騎士を斬ると、騎士の斬撃はあなたへ返る。斬ったら横へ）。移動はそのままコピー。",
                    "王の上の数字＝あと何ラウンドで来るか：剣＝大剣（周り8マス・4）／爆＝爆発（あなたの位置の十字・3）／炎＝炎の波（中央3×3 ⇔ 外周・3）。",
                    "柱（通れない）2本：騎士のコピー移動を止める・大十字の爆発も柱で止まる。トゲ（3）へ騎士を誘う。",
                    "回復陣（緑・+4）：一度だけ。騎士が先に踏むと騎士が回復する。"),
                Map = new[] { ".......", ".......", ".#^.^#.", "...Q...", ".......", "...P...", "...h..." },
            }));
            AssetDatabase.SaveAssets();
            return paths;
        }

        // 【検証中】ACT1 の癒しの泉（A3）：槍兵が左（同じ行の2マス）と右下。最後に横を向くと、どちらかの槍が届く
        // （上下から挟む案は罠 30〜41% で一番「コピーが怖い」が、種によって慎重なボットでも 21 ダメージ・敗北が出た＝振れ幅が大きすぎるので不採用）
        // 2026-10-05 本人プレイ後：動くルーンをやめて「回復マス2つ（+4・一度・敵も）＋壁1つ」（spring_w1）
        static readonly string[] A3Map = { "...h...", ".......", ".Y.P...", ".......", ".#...Y.", "h......" };
        const string A3Facings = "RL";
        const int A3EnemyHp = 14;
        static readonly string[] A3Tutorial =
        {
            "槍兵（槍）：コピーのあと、向いている方向へ2マス突く（3）。向きは「あなたの最後の命令の方向」になる。",
            "左の槍兵は同じ行の2マス左。最後に右を向くと刺される。右下の槍兵は、あなたと同じ行に来ると左を突く。",
            "回復マス（緑・+4）：一度踏むと消える。敵も取れる（取りに行く移動も敵がコピーする）。左下の柱は通れない。",
        };

        /// <summary>次期検証セットの1ステージ分の指定。</summary>
        class StageSpec
        {
            public string File, Label, Title, Tutorial, Facings = "";
            public string[] Map, Fire;
            public int EnemyHp = 8, PlayerStartHp, SpikeDamage = 5;
            public bool FireAfterKnightHalf;
            /// <summary>【検証中】見た目のテーマ（"Ruins"＝古代遺跡／"Crystal"＝紫晶神殿）。null ならグレーBOX。</summary>
            public string Theme;
        }

        static string NextStage(StageSpec spec)
        {
            string path = $"{StageFolder}/{spec.File}.asset";
            var s = AssetDatabase.LoadAssetAtPath<StageData>(path);
            bool isNew = s == null;
            if (isNew) s = ScriptableObject.CreateInstance<StageData>();
            s.stageTitle = spec.Title;
            s.buttonLabel = spec.Label;
            s.isTestStage = true;
            s.tutorialText = spec.Tutorial;
            s.hintAfterFirstTurn = "";
            s.map = Lines(spec.Map);
            s.playerMaxHp = 12;
            s.playerStartHp = spec.PlayerStartHp;
            s.enemyMaxHp = spec.EnemyHp;
            s.spikeDamage = spec.SpikeDamage;
            s.maxSlots = 3;
            s.availableCommands = new List<CommandData> { Command("Walk"), Command("Dash"), Command("Slash"), Command("Push") };
            s.enemyInnateAction = null;
            s.enemyFacings = spec.Facings;
            s.firePatterns = spec.Fire != null ? new List<string>(spec.Fire) : new List<string>();
            s.fireDamage = 3;
            s.fireAfterKnightHalf = spec.FireAfterKnightHalf;
            s.runeUses = 3;
            s.runeHeal = 3;
            s.visualTheme = spec.Theme != null ? AssetDatabase.LoadAssetAtPath<VisualTheme>(ArtSetup.ThemePathOf(spec.Theme)) : null;
            SetValidationDeck(s); // 手札 A/B 用の検証デッキ・攻撃札の保証・鏡蝕の許可（M1〜M3 と同じ）
            // 【検証中】大きい手札用の検証デッキ（2026-10-04 2回目の作り直し：本人プレイで「特殊札が多すぎる」。基本行動中心に）。完成版のデッキではない
            //   移動8（一歩3・駆け足3・突進2）／攻撃8（斬撃3・突き2・押し斬り・薙ぎ払い・闘気）／位置操作3（押し出し2・引き寄せ）／特殊1（跳躍）
            //   （2026-10-04 3回目：入れ替えは本人プレイで「使い道が少ない」→ 引き寄せに。入れ替えは専用ギミックと組めるまで外す）
            //   手札7枚なら平均：攻撃2.8・移動2.8・押し出し0.7・特殊0.7。前のデッキ（移動9に特殊5）は docs/research/HAND_BALANCE_STUDY.md
            s.handTestDeckLarge = new List<CommandData>
            {
                Command("Walk"), Command("Walk"), Command("Walk"), Command("Dash"), Command("Dash"), Command("Dash"), Command("Rush"), Command("Rush"),
                Command("Slash"), Command("Slash"), Command("Slash"), Command("Thrust"), Command("Thrust"),
                Command("PushSlash"), Command("Sweep"), Command("Spirit"),
                Command("Push"), Command("Push"), Command("Pull"), Command("Jump"),
            };
            if (isNew) AssetDatabase.CreateAsset(s, path);
            else EditorUtility.SetDirty(s);
            return path;
        }

        /// <summary>既存の M1〜M3 に検証用デッキと鏡蝕の許可を反映する（STAGE 1〜3 には触れない）。</summary>
        static void ApplyMultiTestValidationSpec()
        {
            foreach (var name in new[] { "StageM1", "StageM2", "StageM3" })
            {
                var s = AssetDatabase.LoadAssetAtPath<StageData>($"{StageFolder}/{name}.asset");
                if (s == null) continue;
                SetValidationDeck(s);
                EditorUtility.SetDirty(s);
            }
            AssetDatabase.SaveAssets();
        }

        static void SetList(SerializedProperty prop, List<UnityEngine.Object> items)
        {
            prop.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        /// <summary>複数行の文字列を作る（盤面や説明文用）。</summary>
        static string Lines(params string[] lines) => string.Join("\n", lines);

        /// <summary>【Puzzle v2】無ければ作り、あれば中身を上書きする（試作の札の数値を毎回反映）。</summary>
        static string CreateOrUpdate<T>(string path, Action<T> init) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                init(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            else { init(asset); EditorUtility.SetDirty(asset); }
            return path;
        }

        static string CreateIfMissing<T>(string path, Action<T> init) where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
            {
                var asset = ScriptableObject.CreateInstance<T>();
                init(asset);
                AssetDatabase.CreateAsset(asset, path);
            }
            return path;
        }

        static void BuildScene(List<string> stagePaths, List<string> runBattlePaths)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 背景用カメラ：画面全体を毎フレーム塗りつぶす（Main Camera は盤面の範囲だけを描くため、
            // これが無いと UI 部分に前のフレームの文字が残って重なる）
            var bgGo = new GameObject("Background Camera");
            var bgCam = bgGo.AddComponent<Camera>();
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            bgCam.cullingMask = 0;
            bgCam.depth = -10;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            camGo.transform.position = new Vector3(0, 0, -10);

            var battle = new GameObject("Battle");
            var grid = battle.AddComponent<GridManager>();
            var turn = battle.AddComponent<TurnManager>();
            var runManager = battle.AddComponent<RunManager>();
            var ui = battle.AddComponent<BattleUI>();
            var uiSo = new SerializedObject(ui);
            uiSo.FindProperty("turn").objectReferenceValue = turn;
            uiSo.FindProperty("run").objectReferenceValue = runManager;
            uiSo.FindProperty("uiTheme").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTheme>(ArtSetup.ThemePath); // 【検証中】UI の絵
            uiSo.ApplyModifiedPropertiesWithoutUndo();
            // 【検証中】ACT1 縦切り（タイトル → 役職 → 扉 → 戦闘・報酬・商人・休息 → ボス）
            var act = battle.AddComponent<ActRunManager>();
            var actSo = new SerializedObject(act);
            actSo.FindProperty("turn").objectReferenceValue = turn;
            var allCards = AssetDatabase.FindAssets("t:CommandData", new[] { CommandFolder }).Select(g => (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<CommandData>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            SetList(actSo.FindProperty("cards"), allCards);
            SetList(actSo.FindProperty("stages"), stagePaths.ConvertAll(p => (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<StageData>(p)));
            actSo.FindProperty("ruinsTheme").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTheme>(ArtSetup.ThemePathOf("Ruins"));
            actSo.FindProperty("crystalTheme").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTheme>(ArtSetup.ThemePathOf("Crystal"));
            actSo.ApplyModifiedPropertiesWithoutUndo();
            var actUi = battle.AddComponent<ActUI>();
            var actUiSo = new SerializedObject(actUi);
            actUiSo.FindProperty("act").objectReferenceValue = act;
            actUiSo.FindProperty("uiTheme").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTheme>(ArtSetup.ThemePath);
            actUiSo.ApplyModifiedPropertiesWithoutUndo();
            uiSo.Update();
            uiSo.FindProperty("act").objectReferenceValue = act;
            uiSo.ApplyModifiedPropertiesWithoutUndo();
            var runUi = battle.AddComponent<RunUI>();
            var runUiSo = new SerializedObject(runUi);
            runUiSo.FindProperty("run").objectReferenceValue = runManager;
            runUiSo.ApplyModifiedPropertiesWithoutUndo();

            // RUN の設定（初期デッキ：斬撃3・突進3・押し出し2。報酬候補7種）
            var runSo = new SerializedObject(runManager);
            runSo.FindProperty("turn").objectReferenceValue = turn;
            SetList(runSo.FindProperty("runBattles"), runBattlePaths.ConvertAll(p => (UnityEngine.Object)AssetDatabase.LoadAssetAtPath<StageData>(p)));
            SetList(runSo.FindProperty("initialDeck"), new List<UnityEngine.Object> {
                Command("Slash"), Command("Slash"), Command("Slash"),
                Command("Rush"), Command("Rush"), Command("Rush"),
                Command("Push"), Command("Push") });
            SetList(runSo.FindProperty("rewardPool"), new List<UnityEngine.Object> {
                Command("Slash"), Command("Rush"), Command("Push"),
                Command("Thrust"), Command("Whirl"), Command("Pull"), Command("Trap") });
            runSo.FindProperty("walkCommand").objectReferenceValue = Command("Walk");
            runSo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(turn);
            so.FindProperty("grid").objectReferenceValue = grid;
            var stageProp = so.FindProperty("stages");
            stageProp.arraySize = stagePaths.Count;
            for (int i = 0; i < stagePaths.Count; i++)
                stageProp.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<StageData>(stagePaths[i]);
            // 【検証中】起動したら見本ステージ（VS：完成形に近い見た目と UI）から始める。STAGE 1〜3 は画面右上「検証」から選べる
            int vs = stagePaths.FindIndex(p => p.EndsWith("/StageVS.asset"));
            so.FindProperty("startStageIndex").intValue = vs >= 0 ? vs : 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
