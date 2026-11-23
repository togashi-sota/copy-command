using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CopyCommand.EditorTools
{
    /// <summary>
    /// 確認用：Battle シーンの盤面をカメラで撮影して Logs/board_stageN.png に保存する（UI は写らない）。
    /// バッチ実行：-executeMethod CopyCommand.EditorTools.BoardScreenshot.CaptureAllStages
    /// </summary>
    public static class BoardScreenshot
    {
        public static void CaptureAllStages()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Battle.unity");
            var turn = Object.FindAnyObjectByType<TurnManager>();
            var cam = Camera.main;
            Directory.CreateDirectory("Logs");
            for (int i = 0; i < turn.Stages.Count; i++)
            {
                turn.LoadStage(i);
                Capture(cam, $"Logs/board_stage{i}.png");
            }
            Debug.Log("[BoardScreenshot] 完了");
        }

        static void Capture(Camera cam, string path, int width = 960, int height = 540)
        {
            var rt = new RenderTexture(width, height, 24);
            var oldRect = cam.rect;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            cam.rect = oldRect;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
