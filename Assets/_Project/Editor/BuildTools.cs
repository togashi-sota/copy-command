using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// Windows 用の確認ビルドを Builds/CopyCommandProto.exe に作る。
    /// バッチ実行：-executeMethod CopyCommand.EditorTools.BuildTools.BuildWindows
    /// 【戦闘中心 v3】外部テスト版：-executeMethod CopyCommand.EditorTools.BuildTools.BuildExternalPlaytest
    ///   → Builds/ExternalPlaytest/CopyCommandProto_Playtest/（印のファイル StreamingAssets/external_playtest.txt・README・感想フォーム）。ZIP は tools/build-external.ps1。
    /// 【通常リリース版・2026-11-16】公開版・文化祭版は、ビルドしたフォルダの隣（配る物の外）に「フォルダ名.build.json」（版・git のコミット・日時）を書く。
    ///   tools/package_release.py がそれとビルドの中の版を確かめてから ZIP にする。Windows のアイコンは Art/Icon/app_icon.png（tools/art_gen/app_icon.py）をビルドの間だけ入れる。
    ///   WebGL のページの題名は BuildInfo.Title（テンプレートの GAME_TITLE）。
    /// </summary>
    public static class BuildTools
    {
        [MenuItem("CopyCommand/Windows ビルド")]
        public static void BuildWindows() => Build("Builds/CopyCommandProto.exe");

        /// <summary>外部テスト版の置き場所（ZIP にする前のフォルダ）。</summary>
        public const string ExternalDir = "Builds/ExternalPlaytest_RC2/UsedSkillsBelongToEnemy_RC2";

        [MenuItem("CopyCommand/外部テスト版ビルド")]
        public static void BuildExternalPlaytest()
        {
            if (Directory.Exists(ExternalDir)) Directory.Delete(ExternalDir, true); // 前の版の残り（古い記録・印）を持ち込まない
            if (!Build(ExternalDir + "/UsedSkillsBelongToEnemy.exe")) return;
            // 外部テスト版の印（検証用の画面・ボタンを隠す。BuildInfo.ExternalPlaytest）
            string streaming = ExternalDir + "/UsedSkillsBelongToEnemy_Data/StreamingAssets";
            Directory.CreateDirectory(streaming);
            File.WriteAllText(Path.Combine(streaming, BuildInfo.MarkerFile), $"external playtest build {BuildInfo.Version}\n");
            // 配る物ではない Unity のデバッグ情報（中にこの PC のパスが入ることがある）を消す
            foreach (var d in Directory.GetDirectories(ExternalDir, "*DoNotShip*")) Directory.Delete(d, true);
            // README・感想フォーム（リポジトリの tools/external から）
            foreach (var f in new[] { "README.txt", "FEEDBACK.txt" })
            {
                string src = Path.Combine("tools/external", f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(ExternalDir, f), true);
                else Debug.LogWarning($"[BuildTools] {src} が無い");
            }
            Debug.Log($"[BuildTools] 外部テスト版：{ExternalDir}（{BuildInfo.Version}）");
        }

        // ───── 【完成版・2026-10-20】公開版（PUBLIC_BUILD：開発用の画面・自動撮影・プレイの自動記録が入らない） ─────
        public const string ReleaseDir = "Builds/Release";
        public const string ProductName = "SonoItte";

        /// <summary>公開版 Windows：Builds/Release/Windows/SonoItte/SonoItte.exe</summary>
        [MenuItem("CopyCommand/公開版 Windows ビルド")]
        public static void BuildPublicWindows()
        {
            string dir = ReleaseDir + "/Windows/" + ProductName;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (!Build(dir + "/" + ProductName + ".exe", BuildTarget.StandaloneWindows64, true)) return;
            foreach (var d in Directory.GetDirectories(dir, "*DoNotShip*")) Directory.Delete(d, true); // この PC のパスが入ることがある
            CopyReleaseDocs(dir);
            Debug.Log($"[BuildTools] 公開版 Windows：{dir}（{BuildInfo.Version}）");
        }

        /// <summary>公開版 WebGL：Builds/Release/WebGL/SonoItte/（index.html）。</summary>
        [MenuItem("CopyCommand/公開版 WebGL ビルド")]
        public static void BuildPublicWebGL()
        {
            string dir = ReleaseDir + "/WebGL/" + ProductName;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            // itch.io などの配信で Content-Encoding が付かなくても読めるように、圧縮は Gzip＋展開の予備（decompression fallback）
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            // 【RC5・最終ゲート】ファイル名を中身のハッシュに：同じ URL に新しい版を置いたとき、ブラウザに残った前の版の wasm と新しいデータが混ざって起動できなかった（RangeError：IL2CPP の型情報の読み込みで止まる）
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            // 窓に合わせて 16:9 で最大表示するページ（Assets/WebGLTemplates/SonoItteFit）
            PlayerSettings.WebGL.template = "PROJECT:SonoItteFit";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            if (!Build(dir, BuildTarget.WebGL, true)) return;
            Debug.Log($"[BuildTools] 公開版 WebGL：{dir}（{BuildInfo.Version}）");
        }

        // ───── 【文化祭・2026-11-12】文化祭版（PUBLIC_BUILD＋FESTIVAL_BUILD：起動すると文化祭チャレンジの入口・Windows は全画面・セーブは SonoItteFestival） ─────
        public const string FestivalProduct = "SonoItteFestival";

        /// <summary>文化祭版 Windows（全画面・展示の PC 用）：Builds/Release/FestivalQuest/Windows/SonoItteFestival/SonoItteFestival.exe（【文化祭クエスト v2】旧版の Builds/Release/Festival は残す）</summary>
        [MenuItem("CopyCommand/文化祭版 Windows ビルド")]
        public static void BuildFestivalWindows()
        {
            string dir = ReleaseDir + "/FestivalQuest/Windows/" + FestivalProduct;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            if (!Build(dir + "/" + FestivalProduct + ".exe", BuildTarget.StandaloneWindows64, true, BuildOptions.None, true)) return;
            foreach (var d in Directory.GetDirectories(dir, "*DoNotShip*")) Directory.Delete(d, true);
            CopyFestivalDocs(dir);
            Debug.Log($"[BuildTools] 文化祭版 Windows：{dir}（{BuildInfo.FestivalVersion}）");
        }

        /// <summary>文化祭版 WebGL（ポートフォリオ用・あとで載せる）：Builds/Release/FestivalQuest/WebGL/SonoItteFestival/（index.html）。</summary>
        [MenuItem("CopyCommand/文化祭版 WebGL ビルド")]
        public static void BuildFestivalWebGL()
        {
            string dir = ReleaseDir + "/FestivalQuest/WebGL/" + FestivalProduct;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.template = "PROJECT:SonoItteFit";
            PlayerSettings.defaultWebScreenWidth = 1280;
            PlayerSettings.defaultWebScreenHeight = 720;
            if (!Build(dir, BuildTarget.WebGL, true, BuildOptions.None, true)) return;
            Debug.Log($"[BuildTools] 文化祭版 WebGL：{dir}（{BuildInfo.FestivalVersion}）");
        }

        static void CopyFestivalDocs(string dir)
        {
            foreach (var f in new[] { "FESTIVAL_README.txt", "CREDITS.txt", "LICENSE.txt" })
            {
                string src = Path.Combine("tools/release", f);
                if (File.Exists(src)) CopyDoc(src, Path.Combine(dir, f == "FESTIVAL_README.txt" ? "README.txt" : f), BuildInfo.FestivalVersion);
                else Debug.LogWarning($"[BuildTools] {src} が無い");
            }
        }

        /// <summary>【RC5】調べる用の WebGL（公開版と同じ中身＋関数名入り・Development）：Builds/Release/WebGLDiag/SonoItte/。ブラウザの例外の場所を読むため（配らない）。</summary>
        public static void BuildDiagWebGL()
        {
            string dir = ReleaseDir + "/WebGLDiag/" + ProductName;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            bool gzip = System.Environment.GetEnvironmentVariable("DIAG_GZIP") == "1"; // 公開版と同じ圧縮（Gzip＋展開の予備）で試す
            PlayerSettings.WebGL.compressionFormat = gzip ? WebGLCompressionFormat.Gzip : WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.template = "PROJECT:SonoItteFit";
            var oldSymbols = PlayerSettings.WebGL.debugSymbolMode;
            PlayerSettings.WebGL.debugSymbolMode = System.Environment.GetEnvironmentVariable("DIAG_SYM") == "external" ? WebGLDebugSymbolMode.External : WebGLDebugSymbolMode.Embedded; // External＝wasm は公開版と同じで、関数名は別のファイル
            bool dev = System.Environment.GetEnvironmentVariable("DIAG_DEV") == "1"; // 既定は公開版と同じ最適化（関数名だけ足す）
            try { Build(dir, BuildTarget.WebGL, true, dev ? BuildOptions.Development : BuildOptions.None); }
            finally { PlayerSettings.WebGL.debugSymbolMode = oldSymbols; PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip; }
        }

        /// <summary>開発版 Windows（研究室・検証ステージ・F2 の検証メニューも入る）：Builds/Dev/CopyCommandProto.exe</summary>
        [MenuItem("CopyCommand/開発版 Windows ビルド")]
        public static void BuildDevWindows() => Build("Builds/Dev/CopyCommandProto.exe", BuildTarget.StandaloneWindows64, false);

        static void CopyReleaseDocs(string dir)
        {
            // 【通常リリース版】配る説明書は4つ（感想フォーム FEEDBACK.txt は通常版に入れない）。ZIP には package_release.py が tools/release の最新を入れ直す
            foreach (var f in new[] { "README.txt", "CREDITS.txt", "LICENSE.txt", "RELEASE_NOTES.txt" })
            {
                string src = Path.Combine("tools/release", f);
                if (File.Exists(src)) CopyDoc(src, Path.Combine(dir, f), BuildInfo.ReleaseVersion);
                else Debug.LogWarning($"[BuildTools] {src} が無い");
            }
        }

        /// <summary>説明書を写す。{{TITLE}}・{{VERSION}} を BuildInfo の題名・版に置き換える（package_release.py と同じ）。</summary>
        static void CopyDoc(string src, string dst, string version)
        {
            string text = File.ReadAllText(src, System.Text.Encoding.UTF8).Replace("{{TITLE}}", BuildInfo.Title).Replace("{{VERSION}}", version);
            File.WriteAllText(dst, text, new System.Text.UTF8Encoding(false));
        }

        static bool Build(string path) => Build(path, BuildTarget.StandaloneWindows64, false);

        static bool Build(string path, BuildTarget target, bool publicBuild, BuildOptions extra = BuildOptions.None, bool festival = false)
        {
            bool fullWin = (festival || publicBuild) && target == BuildTarget.StandaloneWindows64; // 【通常リリース版・配布のレビュー「1366×768 のノート PC では 1280×720 の窓の下が隠れる」】公開版も全画面ウィンドウ（Alt+Enter で窓にもできる）
            PlayerSettings.fullScreenMode = fullWin ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed; // 【文化祭】展示の PC は全画面（Alt+Enter で窓にもできる）
            bool oldNative = PlayerSettings.defaultIsNativeResolution;
            PlayerSettings.defaultIsNativeResolution = fullWin;
            bool oldSingle = PlayerSettings.forceSingleInstance;
            PlayerSettings.forceSingleInstance = publicBuild && target == BuildTarget.StandaloneWindows64; // 【通常リリース版・配布のレビュー】2つ同時に開くと同じセーブに書き込む
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            // 公開版は会社名・製品名を変える（セーブの場所＝AppData/LocalLow/SonoItteProject/SonoItte。開発版のセーブと混ざらない）。ビルドの後で元に戻す
            preBuildStatus = publicBuild ? Git("status --porcelain -- Assets ProjectSettings Packages") : null; // 【通常リリース版】ビルドの前に見る（ビルド中は Unity が ProjectSettings を書き換えるので、後で見るといつも dirty になった）
            string oldProduct = PlayerSettings.productName, oldCompany = PlayerSettings.companyName, oldVersion = PlayerSettings.bundleVersion;
            if (publicBuild) { PlayerSettings.productName = festival ? FestivalProduct : ProductName; PlayerSettings.companyName = "SonoItteProject"; PlayerSettings.bundleVersion = festival ? BuildInfo.FestivalVersion : BuildInfo.ReleaseVersion; } // 【通常リリース版】エディタでは FESTIVAL_BUILD が無いので、文化祭版の版は明示する // 【文化祭】セーブ・プロフィールは別の場所（SonoItteFestival）
            // 【RC2】公開版だけ：DLL のデバッグ情報にプロジェクトのフォルダのパスが入らないよう、コンパイラの -pathmap を一時的に足す（ビルドの後で消す）
            const string Rsp = "Assets/csc.rsp";
            bool wroteRsp = false;
            if (publicBuild && File.Exists(Rsp) && File.ReadAllText(Rsp).StartsWith("-pathmap:"))
            {
                Debug.LogWarning("[BuildTools] 前のビルドの csc.rsp が残っていた（途中で止まった？）。このビルドの後で消す");
                wroteRsp = true;
            }
            else if (publicBuild && !File.Exists(Rsp))
            {
                string root = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                File.WriteAllText(Rsp, "-pathmap:" + root + "=SonoItte" + System.Environment.NewLine);
                AssetDatabase.ImportAsset(Rsp, ImportAssetOptions.ForceUpdate);
                wroteRsp = true;
            }
            // 【通常リリース版】Windows のアイコン（ビルドの間だけ。後で元に戻す）・WebGL のページの題名
            Texture2D[] oldIcons = target == BuildTarget.StandaloneWindows64 ? ApplyWindowsIcon() : null;
            if (target == BuildTarget.WebGL) PlayerSettings.SetTemplateCustomValue("GAME_TITLE", BuildInfo.Title);
            // 【通常リリース版】版の記録（配る物の外）：前のビルドの記録が残らないよう、先に消す
            string stamp = publicBuild ? StampPath(path, target) : null;
            if (stamp != null && File.Exists(stamp)) File.Delete(stamp);
            try
            {

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Battle.unity" },
                locationPathName = path,
                target = target,
                options = extra,
                extraScriptingDefines = festival ? new[] { "PUBLIC_BUILD", "FESTIVAL_BUILD" } : publicBuild ? new[] { "PUBLIC_BUILD" } : new string[0],
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[BuildTools] {path} result={report.summary.result} errors={report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1); // バッチのときだけ（Editor のメニューから押したときに Editor ごと閉じない）
                return false;
            }
            if (stamp != null) WriteStamp(stamp, target, festival);
            return true;
            }
            finally
            {
                if (oldIcons != null) PlayerSettings.SetIcons(NamedBuildTarget.Standalone, oldIcons, IconKind.Any);
                if (wroteRsp) { AssetDatabase.DeleteAsset(Rsp); AssetDatabase.Refresh(); }
                PlayerSettings.productName = oldProduct;
                PlayerSettings.defaultIsNativeResolution = oldNative;
                PlayerSettings.forceSingleInstance = oldSingle;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.companyName = oldCompany;
                PlayerSettings.bundleVersion = oldVersion;
            }
        }

        // ───── 【通常リリース版・2026-11-16】アイコン・版の記録 ─────

        /// <summary>Windows 版のアイコン（256×256。tools/art_gen/app_icon.py が主人公の絵から作る）。</summary>
        public const string IconPath = "Assets/_Project/Art/Icon/app_icon.png";

        /// <summary>Windows のアイコンを入れる。前のアイコンを返す（入れられなかったら null＝戻さない）。</summary>
        static Texture2D[] ApplyWindowsIcon()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (tex == null && File.Exists(IconPath))
            {
                AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate); // 置いたばかりで、まだ取り込まれていないとき
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            }
            if (tex == null) { Debug.LogWarning($"[BuildTools] アイコン {IconPath} が無い（python tools/art_gen/app_icon.py）。Unity の既定のアイコンのままビルドする"); return null; }
            var target = NamedBuildTarget.Standalone;
            var old = PlayerSettings.GetIcons(target, IconKind.Any);
            int n = Mathf.Max(1, PlayerSettings.GetIconSizes(target, IconKind.Any).Length);
            var icons = new Texture2D[n];
            for (int i = 0; i < n; i++) icons[i] = tex; // どの大きさも同じ絵（Unity が縮める）
            PlayerSettings.SetIcons(target, icons, IconKind.Any);
            return old ?? new Texture2D[n];
        }

        /// <summary>版の記録の場所：ビルドしたフォルダの隣の「フォルダ名.build.json」（例 Builds/Release/Windows/SonoItte.build.json）。ZIP には入らない。</summary>
        public static string StampPath(string path, BuildTarget target)
        {
            string dir = target == BuildTarget.WebGL ? path : Path.GetDirectoryName(path);
            return dir.TrimEnd('/', '\\') + ".build.json";
        }

        [System.Serializable]
        class BuildStamp
        {
            public string product;
            public string version;
            public bool festival;
            public string target;
            public string commit;
            public bool dirty; // コミットしていない変更があった（その変更もビルドに入っている）
            public string builtAt;
            public string unity;
        }

        static string preBuildStatus;

        static void WriteStamp(string stamp, BuildTarget target, bool festival)
        {
            string status = preBuildStatus ?? Git("status --porcelain -- Assets ProjectSettings Packages"); // 【通常リリース版・配布のレビュー】まだ追加していない新しいファイル（Assets の .cs など）も数える
            var s = new BuildStamp
            {
                product = festival ? FestivalProduct : ProductName,
                version = festival ? BuildInfo.FestivalVersion : BuildInfo.ReleaseVersion,
                festival = festival,
                target = target.ToString(),
                commit = Git("rev-parse HEAD"),
                dirty = status.Length > 0, // 確かめられなかった（"unknown"）ときも true
                builtAt = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                unity = Application.unityVersion,
            };
            File.WriteAllText(stamp, JsonUtility.ToJson(s, true) + "\n");
            Debug.Log($"[BuildTools] 版の記録：{stamp}（{s.version}・{s.commit}{(s.dirty ? "＋コミットしていない変更" : "")}）");
        }

        /// <summary>git の出力（失敗したら "unknown"）。</summary>
        static string Git(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    WorkingDirectory = Path.GetFullPath("."),
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 ? output.Trim() : "unknown";
                }
            }
            catch (System.Exception) { return "unknown"; }
        }
    }
}
