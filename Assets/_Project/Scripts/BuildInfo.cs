using System.IO;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【戦闘中心 v3（2026-10-06）】版と「外部テスト版」かどうか。
    /// 外部テスト版（Builds/ExternalPlaytest の ZIP）は、検証用の画面・ボタン（研究室・検証ステージ・F2 の検証メニュー・役職の全解放）を隠し、
    /// タイトルは「はじめる／続きから／遊び方」だけにする。印は StreamingAssets/external_playtest.txt（BuildTools.BuildExternalPlaytest が作る）か、起動引数 -externalplaytest。
    /// </summary>
    public static class BuildInfo
    {
        /// <summary>画面の隅・記録・README に出す版（外部テストの感想と記録を突き合わせる）。</summary>
        public const string Version = FestivalBuild ? FestivalVersion : ReleaseVersion;

        /// <summary>
        /// 【通常リリース版】ゲームの題名（画面・結果のコピー・WebGL のページ・配る説明書の {{TITLE}}）。
        /// 【2026-11-21 本人決定】COPY COMMAND（コピコマ）・キャッチ「自分の命令が、敵の武器になる。」（前の題「その一手、敵もまねる。」）。
        /// 画面の題名だけを変えた：セーブの場所（productName・公開版の SonoItteProject）・WebGL のテンプレート名・ファイル名などの内部の名前は変えない（前のセーブがそのまま読める）。
        /// </summary>
        public const string Title = "COPY COMMAND";

        /// <summary>【2026-11-21】題名の読み（略称）とキャッチコピー（タイトル画面の題名の下）。</summary>
        public const string TitleKana = "コピコマ";
        public const string Catch = "自分の命令が、敵の武器になる。";

        /// <summary>【通常リリース版・2026-11-16】文化祭のあと、就活の主力作品として仕上げる版。</summary>
        public const string ReleaseVersion = "1.0.0";

        /// <summary>【文化祭クエスト v2】文化祭版だけの版（旧版は 1.1.0-festival。1.2.1＝次の人へで本編も はじめての状態に：2026-11-14）。記録として残す。</summary>
        public const string FestivalVersion = "1.2.1-festival-quest";

        /// <summary>【完成版】公開版（Windows・WebGL の配布物）。BuildTools が PUBLIC_BUILD を付けてビルドする。開発用の画面・自動撮影・プレイの自動記録は入らない。</summary>
#if PUBLIC_BUILD
        public const bool PublicBuild = true;
#else
        public const bool PublicBuild = false;
#endif

        /// <summary>【文化祭・2026-11-12】文化祭版（FESTIVAL_BUILD：起動すると文化祭チャレンジの入口）。BuildTools.BuildFestivalWindows／BuildFestivalWebGL が付ける。</summary>
#if FESTIVAL_BUILD
        public const bool FestivalBuild = true;
#else
        public const bool FestivalBuild = false;
#endif

        /// <summary>【文化祭】起動したら文化祭の入口から（文化祭版・起動引数 -festival）。</summary>
        public static bool Festival => FestivalBuild || (!PublicBuild && HasArg("-festival")); // 【通常リリース版】公開版は起動引数でも文化祭へ入らない（開発版の撮影だけ）

        /// <summary>【文化祭】展示の PC（キオスク）：しばらく操作が無いと確認してから入口へ戻る・「次の人へ」で本編の中断セーブも消す。文化祭版の Windows・起動引数 -festivalkiosk。ブラウザ版は使わない（一人で遊ぶ）。</summary>
        public static bool FestivalKiosk => (FestivalBuild && Application.platform != RuntimePlatform.WebGLPlayer) || (!PublicBuild && HasArg("-festivalkiosk"));

        static bool HasArg(string a)
        {
            try { return System.Array.IndexOf(System.Environment.GetCommandLineArgs(), a) >= 0; }
            catch (System.Exception) { return false; }
        }

        static bool? cached;

        /// <summary>外部テスト版か。</summary>
        public static bool ExternalPlaytest
        {
            get
            {
                if (cached.HasValue) return cached.Value;
                if (PublicBuild) { cached = true; return true; } // 【完成版】公開版は常に（WebGL は起動引数・StreamingAssets のファイルを読めない）
                bool on = false;
                try
                {
                    on = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-externalplaytest") >= 0 ||
                         File.Exists(Path.Combine(Application.streamingAssetsPath, MarkerFile));
                }
                catch (System.Exception) { on = false; }
                cached = on;
                return on;
            }
        }

        /// <summary>外部テスト版の印のファイル名（StreamingAssets の中）。</summary>
        public const string MarkerFile = "external_playtest.txt";

        /// <summary>テスト用：外部テスト版かどうかを決める（null で元に戻す）。</summary>
        public static void SetForTest(bool? value) => cached = value;
    }
}
