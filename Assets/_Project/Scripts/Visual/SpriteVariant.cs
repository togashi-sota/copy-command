using UnityEngine;

namespace CopyCommand
{
    /// <summary>【検証中】床の種類違い：マスの位置で決まった1枚を選ぶ（毎回同じ盤面なら同じ見た目）。</summary>
    public class SpriteVariant : MonoBehaviour
    {
        public Sprite[] sprites;

        public void Pick(Vector2Int cell)
        {
            if (sprites == null || sprites.Length == 0) return;
            int h = (cell.x * 73856093) ^ (cell.y * 19349663);
            GetComponent<SpriteRenderer>().sprite = sprites[Mathf.Abs(h) % sprites.Length];
        }
    }
}
