using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// 【完成版・2026-10-20】セーブ・プロフィールのファイルを安全に書く（一時ファイル → 置き換え。置き換えが使えない環境ではそのまま上書き）。
    /// WebGL では、書いたあとにブラウザの保存領域（IndexedDB）へ同期する（Plugins/WebGL/WebStorage.jslib）。再読み込みしても記録が残る。
    /// </summary>
    public static class WebStorage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CopyCommand_SyncFiles();
#endif

        /// <summary>ブラウザの保存領域へ同期する（WebGL 以外では何もしない）。</summary>
        public static void Sync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { CopyCommand_SyncFiles(); } catch (System.Exception e) { Debug.LogWarning("[WebStorage] 同期できなかった：" + e.Message); }
#endif
        }

        /// <summary>一時ファイルに書いてから置き換える。置き換え（File.Replace）が使えなければ、そのまま上書き。最後に同期。</summary>
        public static void WriteAllTextSafe(string path, string text)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (System.Exception)
            {
                // ブラウザのファイルの仕組み（Emscripten）では Replace が使えないことがある → 上書き
                File.WriteAllText(path, text);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (System.Exception) { }
            }
            Sync();
        }
    }
}
