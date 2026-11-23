using UnityEngine;

namespace CopyCommand
{
    /// <summary>
    /// グレーBOX用の図形（四角・三角）をコードで作る。画像素材は使わない。
    /// どちらも 1×1 ユニットの大きさ。色は SpriteRenderer.color で付ける。
    /// </summary>
    public static class GreyboxSprites
    {
        static Sprite square;
        static Sprite triangle;

        public static Sprite Square
        {
            get
            {
                if (square == null)
                {
                    var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var pixels = new Color[16];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
                    tex.SetPixels(pixels);
                    tex.Apply();
                    square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                }
                return square;
            }
        }

        public static Sprite Triangle
        {
            get
            {
                if (triangle == null)
                {
                    const int size = 64;
                    var tex = new Texture2D(size, size) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    for (int y = 0; y < size; y++)
                    {
                        // 上に向かって細くなる三角形
                        float halfWidth = (size - 1 - y) * 0.5f;
                        for (int x = 0; x < size; x++)
                        {
                            bool inside = Mathf.Abs(x - (size - 1) * 0.5f) <= halfWidth;
                            tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                        }
                    }
                    tex.Apply();
                    triangle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                }
                return triangle;
            }
        }

        /// <summary>子オブジェクトとして色付きの図形を作る。</summary>
        public static SpriteRenderer CreateShape(Transform parent, string name, Sprite sprite, Color color, Vector3 position, float scale, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortingOrder;
            return sr;
        }
    }
}
